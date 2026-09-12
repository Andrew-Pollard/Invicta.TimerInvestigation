# © 2026 Andrew Pollard. All rights reserved.

<#
.SYNOPSIS
Changes the refresh rate of every attached display, without saving the change.

.DESCRIPTION
Each display keeps its resolution and colour depth. Windows is asked to validate the new mode first, and the
script stops if any display rejects it. The change is applied dynamically rather than written to the registry,
so the saved rate returns after signing out or restarting, or immediately with -Restore.

Some drivers apply a neighbouring mode: on the machine these results come from, asking for 60 Hz produced 59 Hz.
Use -Restore rather than the original rate to return to the saved settings exactly.

With no rate, the script lists each display's current mode and the rates it supports at its current resolution.

.PARAMETER Rate
The refresh rate to apply, in hertz.

.PARAMETER Restore
Returns every display to the mode saved in Windows' display settings, undoing any change made by this script.

.EXAMPLE
tools\Set-DisplayRefreshRate.ps1

.EXAMPLE
tools\Set-DisplayRefreshRate.ps1 -Rate 120

.EXAMPLE
tools\Set-DisplayRefreshRate.ps1 -Restore
#>
[CmdletBinding(DefaultParameterSetName = 'List')]
param(
    [Parameter(Mandatory, ParameterSetName = 'Change')]
    [int] $Rate,

    [Parameter(Mandatory, ParameterSetName = 'Restore')]
    [switch] $Restore
)

$ErrorActionPreference = 'Stop'

if (-not ('Invicta.DisplaySettings' -as [type])) {
    Add-Type -TypeDefinition @'
using System;
using System.Runtime.InteropServices;

namespace Invicta
{
    public static class DisplaySettings
    {
        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        public struct DISPLAY_DEVICE
        {
            public int cb;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string DeviceName;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string DeviceString;
            public int StateFlags;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string DeviceID;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string DeviceKey;
        }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        public struct DEVMODE
        {
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string dmDeviceName;
            public short dmSpecVersion;
            public short dmDriverVersion;
            public short dmSize;
            public short dmDriverExtra;
            public int dmFields;
            public int dmPositionX;
            public int dmPositionY;
            public int dmDisplayOrientation;
            public int dmDisplayFixedOutput;
            public short dmColor;
            public short dmDuplex;
            public short dmYResolution;
            public short dmTTOption;
            public short dmCollate;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string dmFormName;
            public short dmLogPixels;
            public int dmBitsPerPel;
            public int dmPelsWidth;
            public int dmPelsHeight;
            public int dmDisplayFlags;
            public int dmDisplayFrequency;
            public int dmICMMethod;
            public int dmICMIntent;
            public int dmMediaType;
            public int dmDitherType;
            public int dmReserved1;
            public int dmReserved2;
            public int dmPanningWidth;
            public int dmPanningHeight;
        }

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        public static extern bool EnumDisplayDevicesW(string lpDevice, int iDevNum, ref DISPLAY_DEVICE lpDisplayDevice, int dwFlags);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        public static extern bool EnumDisplaySettingsW(string lpszDeviceName, int iModeNum, ref DEVMODE lpDevMode);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        public static extern int ChangeDisplaySettingsExW(string lpszDeviceName, ref DEVMODE lpDevMode, IntPtr hwnd, int dwflags, IntPtr lParam);

        [DllImport("user32.dll", CharSet = CharSet.Unicode, EntryPoint = "ChangeDisplaySettingsExW")]
        public static extern int ResetDisplaySettings(string lpszDeviceName, IntPtr lpDevMode, IntPtr hwnd, int dwflags, IntPtr lParam);
    }
}
'@
}

$DISPLAY_DEVICE_ATTACHED_TO_DESKTOP = 0x1
$ENUM_CURRENT_SETTINGS = -1
$DM_BITSPERPEL = 0x40000
$DM_PELSWIDTH = 0x80000
$DM_PELSHEIGHT = 0x100000
$DM_DISPLAYFREQUENCY = 0x400000
$CDS_TEST = 0x2
$DISP_CHANGE_SUCCESSFUL = 0

function Get-AttachedDisplay {
    for ($index = 0; ; $index++) {
        $device = New-Object Invicta.DisplaySettings+DISPLAY_DEVICE
        $device.cb = [Runtime.InteropServices.Marshal]::SizeOf($device)
        # PowerShell passes $null as an empty string, so use NullString to pass a real null.
        if (-not [Invicta.DisplaySettings]::EnumDisplayDevicesW([NullString]::Value, $index, [ref]$device, 0)) {
            break
        }

        if ($device.StateFlags -band $DISPLAY_DEVICE_ATTACHED_TO_DESKTOP) {
            $device
        }
    }
}

function Get-DisplayMode([string] $deviceName, [int] $modeNumber) {
    $mode = New-Object Invicta.DisplaySettings+DEVMODE
    $mode.dmSize = [Runtime.InteropServices.Marshal]::SizeOf($mode)
    if ([Invicta.DisplaySettings]::EnumDisplaySettingsW($deviceName, $modeNumber, [ref]$mode)) {
        $mode
    }
}

if ($Restore) {
    # A null mode with no flags returns every display to the settings saved in the registry.
    $result = [Invicta.DisplaySettings]::ResetDisplaySettings([NullString]::Value, [IntPtr]::Zero, [IntPtr]::Zero, 0, [IntPtr]::Zero)
    if ($result -ne $DISP_CHANGE_SUCCESSFUL) {
        throw "Restoring the saved display settings failed (result $result)."
    }

    'Restored the saved display settings.'
    return
}

$displays = @(Get-AttachedDisplay)
if (-not $PSBoundParameters.ContainsKey('Rate')) {
    foreach ($display in $displays) {
        $current = Get-DisplayMode $display.DeviceName $ENUM_CURRENT_SETTINGS
        $rates = New-Object 'System.Collections.Generic.SortedSet[int]'
        for ($modeNumber = 0; ($mode = Get-DisplayMode $display.DeviceName $modeNumber); $modeNumber++) {
            if ($mode.dmPelsWidth -eq $current.dmPelsWidth -and $mode.dmPelsHeight -eq $current.dmPelsHeight) {
                [void]$rates.Add($mode.dmDisplayFrequency)
            }
        }

        '{0} ({1}): {2}x{3} at {4} Hz; supported: {5} Hz' -f $display.DeviceName, $display.DeviceString,
            $current.dmPelsWidth, $current.dmPelsHeight, $current.dmDisplayFrequency, ($rates -join ', ')
    }

    return
}

# Validate every display before changing any of them, using a mode the driver enumerates at the current resolution
# so that an unsupported rate is rejected up front.
$changes = foreach ($display in $displays) {
    $current = Get-DisplayMode $display.DeviceName $ENUM_CURRENT_SETTINGS
    $mode = $null
    for ($modeNumber = 0; ($candidate = Get-DisplayMode $display.DeviceName $modeNumber); $modeNumber++) {
        if ($candidate.dmPelsWidth -eq $current.dmPelsWidth -and
            $candidate.dmPelsHeight -eq $current.dmPelsHeight -and
            $candidate.dmBitsPerPel -eq $current.dmBitsPerPel -and
            $candidate.dmDisplayFrequency -eq $Rate) {
            $mode = $candidate
            break
        }
    }

    if ($null -eq $mode) {
        throw "$($display.DeviceName) has no $Rate Hz mode at its current resolution; no display was changed."
    }

    $mode.dmFields = $DM_PELSWIDTH -bor $DM_PELSHEIGHT -bor $DM_BITSPERPEL -bor $DM_DISPLAYFREQUENCY
    $result = [Invicta.DisplaySettings]::ChangeDisplaySettingsExW($display.DeviceName, [ref]$mode, [IntPtr]::Zero, $CDS_TEST, [IntPtr]::Zero)
    if ($result -ne $DISP_CHANGE_SUCCESSFUL) {
        throw "$($display.DeviceName) does not accept $Rate Hz (result $result); no display was changed."
    }

    [pscustomobject]@{ DeviceName = $display.DeviceName; Mode = $mode }
}

foreach ($change in $changes) {
    $mode = $change.Mode
    $result = [Invicta.DisplaySettings]::ChangeDisplaySettingsExW($change.DeviceName, [ref]$mode, [IntPtr]::Zero, 0, [IntPtr]::Zero)
    if ($result -ne $DISP_CHANGE_SUCCESSFUL) {
        throw "$($change.DeviceName) failed to change to $Rate Hz (result $result)."
    }

    "$($change.DeviceName): now $Rate Hz"
}
