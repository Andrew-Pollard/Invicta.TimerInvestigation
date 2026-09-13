# © 2026 Andrew Pollard. All rights reserved.

<#
.SYNOPSIS
Runs samples with the displays asleep, announcing each step aloud.

.DESCRIPTION
Animating applications, such as Chromium-based browsers and apps, change the timer resolution many times a second,
and each change makes every process's overdue timers fire. With the displays asleep nothing animates, so this is
the cleanest condition for measuring timers. The script builds the solution, puts the displays to sleep, runs each
sample in turn with its output written to a file, then wakes the displays. Progress is spoken because the screens
are off.

Any mouse or keyboard input wakes the displays and spoils the run.

.PARAMETER Sample
The sample projects to run, each optionally followed by its arguments, such as 'WakeGrid 3'.

.PARAMETER OutputDirectory
The directory to write each sample's output to, as <Sample>.txt.

.PARAMETER Companion
Another sample, with its arguments, to start two seconds before each sample and wait for afterwards, such as
'TimerNeighbor toggle 20 3'. Its output is appended to the sample's file.

.PARAMETER SettleSeconds
How long to wait after the displays go to sleep before the first sample starts.

.EXAMPLE
tools\Invoke-WithDisplaysAsleep.ps1 -Sample WakeGrid, DurationSweep -OutputDirectory results\displays-asleep

.EXAMPLE
tools\Invoke-WithDisplaysAsleep.ps1 -Sample 'WakeGrid 3' -Companion 'TimerNeighbor toggle 20 3' -OutputDirectory results\neighbor-toggle-3ms
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [string[]] $Sample,

    [Parameter(Mandatory)]
    [string] $OutputDirectory,

    [string] $Companion,

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

$utf8 = New-Object Text.UTF8Encoding $false
$failed = @()
foreach ($entry in $Sample) {
    $name, $arguments = $entry -split ' ', 2
    $voice.Speak("Running $name.")
    $project = Join-Path $repositoryRoot "samples\$name"
    $file = Join-Path $outputPath "$name.txt"
    $alongside = if ($Companion) { " and '$Companion' running alongside" } else { '' }
    $header = "Recorded $(Get-Date -Format o) with the displays asleep$alongside.`r`n`r`n"
    [IO.File]::WriteAllText($file, $header, $utf8)

    $companionProcess = $null
    $companionOutput = Join-Path $outputPath "$name.companion.tmp"
    if ($Companion) {
        $companionName, $companionArguments = $Companion -split ' ', 2
        $companionProject = Join-Path $repositoryRoot "samples\$companionName"
        $companionProcess = Start-Process -FilePath 'dotnet' -NoNewWindow -PassThru -RedirectStandardOutput $companionOutput `
            -ArgumentList "run -c Release --no-build --project `"$companionProject`" -- $companionArguments"
        Start-Sleep -Seconds 2
    }

    Write-Timestamped "Running $entry."
    # cmd does the redirection, so Windows PowerShell neither re-encodes the output nor treats stderr as errors.
    $ErrorActionPreference = 'Continue'
    cmd /c "dotnet run -c Release --no-build --project `"$project`" -- $arguments >> `"$file`" 2>&1"
    $exitCode = $LASTEXITCODE
    $ErrorActionPreference = 'Stop'
    Write-Timestamped "$name finished with exit code $exitCode."
    if ($exitCode -ne 0) {
        $failed += $name
    }

    if ($companionProcess) {
        $companionProcess.WaitForExit()
        [IO.File]::AppendAllText($file, "`r`n" + [IO.File]::ReadAllText($companionOutput), $utf8)
        Remove-Item -LiteralPath $companionOutput
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
