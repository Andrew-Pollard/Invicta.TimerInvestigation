# © 2026 Andrew Pollard. All rights reserved.
# Licensed under the MIT License.

#Requires -RunAsAdministrator

<#
.SYNOPSIS
Records a kernel trace of interrupts, DPCs, timers and thread wake-ups while WakeGrid runs under five scenarios.

.DESCRIPTION
Starts Windows Performance Recorder with TimerWakeUps.wprp, then runs WakeGrid for about 16 seconds in each of
these scenarios, announcing each step aloud:

  desktop             the screens on and the mouse and keyboard left alone
  mouse-moving        the user moving the mouse continuously
  silent-audio        a loop of silence playing through the default audio device
  redraw-every-frame  a small window redrawing on every composition frame
  displays-asleep     the displays asleep

It then stops the trace and writes trace.etl, timeline.json and each scenario's WakeGrid output to the output
directory. Analyze the trace with tools\TimerTraceAnalysis.

The trace contains details of the machine, its network configuration, its devices and every running process.
Share the analysis rather than the trace itself.

.PARAMETER OutputDirectory
The directory to write the trace and results to.

.EXAMPLE
tools\Trace-TimerWakeUps.ps1 -OutputDirectory $env:TEMP\timer-trace
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [string] $OutputDirectory
)

$ErrorActionPreference = 'Stop'
$repositoryRoot = Split-Path -Parent $PSScriptRoot
$wakeGrid = Join-Path $repositoryRoot 'samples\WakeGrid\bin\Release\net10.0\WakeGrid.exe'
if (-not (Test-Path -LiteralPath $wakeGrid)) {
    throw "Build the solution in Release first; $wakeGrid was not found."
}

$outputPath = (New-Item -ItemType Directory -Force -Path $OutputDirectory).FullName
$utf8 = New-Object Text.UTF8Encoding $false

Add-Type -AssemblyName System.Speech
$voice = New-Object System.Speech.Synthesis.SpeechSynthesizer
$voice.Volume = 100

Add-Type -Namespace Invicta -Name TraceNative -MemberDefinition @'
[DllImport("user32.dll")]
public static extern bool PostMessage(IntPtr hWnd, uint Msg, IntPtr wParam, IntPtr lParam);

[DllImport("user32.dll")]
public static extern void mouse_event(uint dwFlags, int dx, int dy, uint dwData, UIntPtr dwExtraInfo);
'@

function Say([string] $text) {
    Write-Host "$(Get-Date -Format o) $text"
    $voice.Speak($text)
}

$timeline = New-Object System.Collections.Generic.List[object]

function Invoke-Scenario([string] $name) {
    $file = Join-Path $outputPath "$name.txt"
    $start = [DateTime]::UtcNow
    $ErrorActionPreference = 'Continue'
    cmd /c "`"$wakeGrid`" 4 > `"$file`" 2>&1"
    $ErrorActionPreference = 'Stop'
    $timeline.Add([pscustomobject]@{ Name = $name; StartUtc = $start.ToString('o'); EndUtc = [DateTime]::UtcNow.ToString('o') })
}

# One second of silent 48 kHz, 16-bit stereo PCM for the audio scenario.
$silence = Join-Path $outputPath 'silence.wav'
$sampleRate = 48000
$channels = 2
$bytesPerSample = 2
$dataBytes = $sampleRate * $channels * $bytesPerSample
$stream = New-Object IO.MemoryStream
$writer = New-Object IO.BinaryWriter $stream
$writer.Write([Text.Encoding]::ASCII.GetBytes('RIFF'))
$writer.Write([int](36 + $dataBytes))
$writer.Write([Text.Encoding]::ASCII.GetBytes('WAVEfmt '))
$writer.Write([int]16)
$writer.Write([int16]1)
$writer.Write([int16]$channels)
$writer.Write([int]$sampleRate)
$writer.Write([int]($sampleRate * $channels * $bytesPerSample))
$writer.Write([int16]($channels * $bytesPerSample))
$writer.Write([int16](8 * $bytesPerSample))
$writer.Write([Text.Encoding]::ASCII.GetBytes('data'))
$writer.Write([int]$dataBytes)
$writer.Write((New-Object byte[] $dataBytes))
[IO.File]::WriteAllBytes($silence, $stream.ToArray())

# A small window that redraws once per composition frame, as in Invoke-WithScreenActivity.ps1.
$window = @'
Add-Type -ReferencedAssemblies System.Windows.Forms, System.Drawing -TypeDefinition @"
using System;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows.Forms;

public static class ActivityWindow
{
    [DllImport("kernel32.dll")] static extern IntPtr GetConsoleWindow();
    [DllImport("user32.dll")] static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);
    [DllImport("kernel32.dll")] static extern uint SetThreadExecutionState(uint esFlags);
    [DllImport("dwmapi.dll")] static extern int DwmFlush();

    public static void Run()
    {
        ShowWindow(GetConsoleWindow(), 0);
        SetThreadExecutionState(0x80000002);
        long count = 0;
        Form form = new Form
        {
            Text = "Screen activity", FormBorderStyle = FormBorderStyle.FixedToolWindow, StartPosition = FormStartPosition.Manual,
            Location = new Point(40, 40), ClientSize = new Size(240, 60), TopMost = true,
        };
        Font font = new Font("Consolas", 20);
        form.Paint += (s, e) =>
        {
            e.Graphics.Clear(SystemColors.Window);
            TextRenderer.DrawText(e.Graphics, count.ToString(), font, form.ClientRectangle, SystemColors.WindowText);
        };
        form.Show();
        while (form.Visible)
        {
            count++;
            form.Invalidate();
            form.Update();
            DwmFlush();
            Application.DoEvents();
        }
    }
}
"@
[ActivityWindow]::Run()
'@
$encodedWindow = [Convert]::ToBase64String([Text.Encoding]::Unicode.GetBytes($window))

Say 'Starting the kernel trace. This will take about three minutes. I will tell you what to do at each step.'
& wpr.exe -start "$(Join-Path $PSScriptRoot 'TimerWakeUps.wprp')!TimerWakeUps" -filemode
if ($LASTEXITCODE -ne 0) {
    throw "wpr -start failed with exit code $LASTEXITCODE."
}

try {
    Say 'Scenario one. Please leave the mouse and keyboard alone.'
    Start-Sleep -Seconds 8
    Invoke-Scenario 'desktop'

    Say 'Scenario two. Please move the mouse continuously, in slow circles, until I say stop.'
    Start-Sleep -Seconds 3
    Invoke-Scenario 'mouse-moving'
    Say 'Stop moving the mouse.'

    Say 'Scenario three. I will play silent audio. Please leave everything alone.'
    $player = New-Object System.Media.SoundPlayer $silence
    $player.PlayLooping()
    Start-Sleep -Seconds 3
    try {
        Invoke-Scenario 'silent-audio'
    }
    finally {
        $player.Stop()
        $player.Dispose()
    }

    Say 'Scenario four. A small counter window will appear. Please leave everything alone.'
    $windowProcess = Start-Process -FilePath 'powershell.exe' -ArgumentList '-NoProfile', '-Sta', '-EncodedCommand', $encodedWindow -PassThru
    Start-Sleep -Seconds 5
    try {
        Invoke-Scenario 'redraw-every-frame'
    }
    finally {
        Stop-Process -Id $windowProcess.Id -ErrorAction SilentlyContinue
    }

    Say 'Scenario five. Putting the displays to sleep. Please do not touch anything until I say so.'
    Start-Sleep -Seconds 2
    # WM_SYSCOMMAND with SC_MONITORPOWER and 2 turns the displays off.
    [void][Invicta.TraceNative]::PostMessage([IntPtr]0xFFFF, 0x0112, [IntPtr]0xF170, [IntPtr]2)
    Start-Sleep -Seconds 10
    Invoke-Scenario 'displays-asleep'
    [Invicta.TraceNative]::mouse_event(0x0001, 1, 0, 0, [UIntPtr]::Zero)
    [Invicta.TraceNative]::mouse_event(0x0001, -1, 0, 0, [UIntPtr]::Zero)
    Start-Sleep -Seconds 3
}
finally {
    [IO.File]::WriteAllText((Join-Path $outputPath 'timeline.json'), ($timeline | ConvertTo-Json), $utf8)
    Say 'Scenarios finished. Saving the trace, which can take a minute.'
    & wpr.exe -stop (Join-Path $outputPath 'trace.etl') 'Timer wake-ups'
    $stopExitCode = $LASTEXITCODE
    Remove-Item -LiteralPath $silence -ErrorAction SilentlyContinue
}

if ($stopExitCode -eq 0) {
    Say 'The trace is saved. You can use the computer normally again.'
}
else {
    Say 'Saving the trace failed. Please check the window for details.'
    throw "wpr -stop failed with exit code $stopExitCode."
}
