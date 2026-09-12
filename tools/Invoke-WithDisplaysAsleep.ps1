# © 2026 Andrew Pollard. All rights reserved.

<#
.SYNOPSIS
Runs samples with the displays asleep, announcing each step aloud.

.DESCRIPTION
Display refresh interrupts change when timers are serviced, so measurements of the system tick are only clean
while nothing is being drawn. This script builds the solution, puts the displays to sleep, runs each sample in
turn with its output written to a file, then wakes the displays. Progress is spoken because the screens are off.

Any mouse or keyboard input wakes the displays and spoils the run.

.PARAMETER Sample
The names of the sample projects to run, such as WakeGrid.

.PARAMETER OutputDirectory
The directory to write each sample's output to, as <Sample>.txt.

.PARAMETER SettleSeconds
How long to wait after the displays go to sleep before the first sample starts. Windows stops VSync interrupts
after ten idle frames, so a few seconds is ample.

.EXAMPLE
tools\Invoke-WithDisplaysAsleep.ps1 -Sample WakeGrid, DurationSweep -OutputDirectory results\displays-asleep
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [string[]] $Sample,

    [Parameter(Mandatory)]
    [string] $OutputDirectory,

    [int] $SettleSeconds = 10
)

$ErrorActionPreference = 'Stop'
$repositoryRoot = Split-Path -Parent $PSScriptRoot

Add-Type -Namespace Invicta -Name User32 -MemberDefinition @'
[DllImport("user32.dll")]
public static extern bool PostMessage(IntPtr hWnd, uint Msg, IntPtr wParam, IntPtr lParam);

[DllImport("user32.dll")]
public static extern void mouse_event(uint dwFlags, int dx, int dy, uint dwData, UIntPtr dwExtraInfo);
'@
Add-Type -AssemblyName System.Speech

$voice = New-Object System.Speech.Synthesis.SpeechSynthesizer
$voice.Volume = 100

function Write-Timestamped([string] $message) {
    Write-Host "$(Get-Date -Format o) $message"
}

& dotnet build (Join-Path $repositoryRoot 'Invicta.TimerInvestigation.slnx') -c Release --nologo -v q
if ($LASTEXITCODE -ne 0) {
    throw "The build failed with exit code $LASTEXITCODE."
}

$outputPath = (New-Item -ItemType Directory -Force -Path $OutputDirectory).FullName
$noun = if ($Sample.Count -eq 1) { 'sample' } else { 'samples' }
$voice.Speak("Putting the displays to sleep to run $($Sample.Count) $noun. Please don't touch the mouse or keyboard until I say so.")

# WM_SYSCOMMAND with SC_MONITORPOWER and 2 turns the displays off.
[void][Invicta.User32]::PostMessage([IntPtr]0xFFFF, 0x0112, [IntPtr]0xF170, [IntPtr]2)
Write-Timestamped 'Displays sent to sleep.'
Start-Sleep -Seconds $SettleSeconds

$failed = @()
foreach ($name in $Sample) {
    $voice.Speak("Running $name.")
    $project = Join-Path $repositoryRoot "samples\$name"
    $file = Join-Path $outputPath "$name.txt"
    $header = "Recorded $(Get-Date -Format o) with the displays asleep.`r`n`r`n"
    [IO.File]::WriteAllText($file, $header, (New-Object Text.UTF8Encoding $false))

    Write-Timestamped "Running $name."
    # cmd does the redirection, so Windows PowerShell neither re-encodes the output nor treats stderr as errors.
    $ErrorActionPreference = 'Continue'
    cmd /c "dotnet run -c Release --no-build --project `"$project`" >> `"$file`" 2>&1"
    $exitCode = $LASTEXITCODE
    $ErrorActionPreference = 'Stop'
    Write-Timestamped "$name finished with exit code $exitCode."
    if ($exitCode -ne 0) {
        $failed += $name
    }
}

# A one-pixel mouse movement there and back wakes the displays.
[Invicta.User32]::mouse_event(0x0001, 1, 0, 0, [UIntPtr]::Zero)
[Invicta.User32]::mouse_event(0x0001, -1, 0, 0, [UIntPtr]::Zero)
Start-Sleep -Seconds 3

if ($failed.Count -eq 0) {
    $voice.Speak('All done. The displays are waking up, and you can use the computer again.')
}
else {
    $voice.Speak("Finished, but $($failed.Count) failed. The displays are waking up, and you can use the computer again.")
}

$voice.Dispose()
if ($failed.Count -ne 0) {
    throw "These samples failed: $($failed -join ', ')."
}
