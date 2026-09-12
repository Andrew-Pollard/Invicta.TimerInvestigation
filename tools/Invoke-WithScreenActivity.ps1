# © 2026 Andrew Pollard. All rights reserved.

<#
.SYNOPSIS
Runs samples while a small window redraws on every frame, so timers follow the display's refresh rate.

.DESCRIPTION
Timers only follow the display's refresh rate while something on screen changes on every frame. This script opens
a small, always-on-top window whose counter redraws once per composition frame, runs each sample in turn with its
output written to a file, then closes the window.

The window paces itself with DwmFlush rather than a timer, so it does not change the global timer resolution, and
it does not flash. While it is open it also asks Windows to keep the displays on, because a run lasts longer than
a short screen timeout.

.PARAMETER Sample
The names of the sample projects to run, such as WakeGrid.

.PARAMETER OutputDirectory
The directory to write each sample's output to, as <Sample>.txt.

.PARAMETER RedrawInterval
Redraw on a Windows Forms timer with this interval in milliseconds, instead of on every frame. A 15 ms interval
keeps the screen changing often enough for Windows to leave VSync interrupts running, but not on every frame.

.EXAMPLE
tools\Invoke-WithScreenActivity.ps1 -Sample WakeGrid -OutputDirectory results\screens-on-120hz

.EXAMPLE
tools\Invoke-WithScreenActivity.ps1 -Sample WakeGrid -OutputDirectory results\screens-on-180hz-redraw-15ms -RedrawInterval 15
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [string[]] $Sample,

    [Parameter(Mandatory)]
    [string] $OutputDirectory,

    [int] $RedrawInterval = 0
)

$ErrorActionPreference = 'Stop'
$repositoryRoot = Split-Path -Parent $PSScriptRoot

& dotnet build (Join-Path $repositoryRoot 'Invicta.TimerInvestigation.slnx') -c Release --nologo -v q
if ($LASTEXITCODE -ne 0) {
    throw "The build failed with exit code $LASTEXITCODE."
}

$window = @'
Add-Type -ReferencedAssemblies System.Windows.Forms, System.Drawing -TypeDefinition @"
using System;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace Invicta
{
    public static class ActivityWindow
    {
        [DllImport("kernel32.dll")]
        private static extern IntPtr GetConsoleWindow();

        [DllImport("user32.dll")]
        private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

        [DllImport("kernel32.dll")]
        private static extern uint SetThreadExecutionState(uint esFlags);

        [DllImport("dwmapi.dll")]
        private static extern int DwmFlush();

        public static void Run(int interval)
        {
            // Hide only the console. Starting the process hidden would hide the form too, because Windows applies
            // the start-up window state to a process's first window.
            ShowWindow(GetConsoleWindow(), 0);

            // ES_CONTINUOUS | ES_DISPLAY_REQUIRED: keep the screen timeout from turning the displays off.
            SetThreadExecutionState(0x80000002);

            long count = 0;
            Form form = new Form
            {
                Text = "Screen activity 0",
                FormBorderStyle = FormBorderStyle.FixedToolWindow,
                StartPosition = FormStartPosition.Manual,
                Location = new Point(40, 40),
                ClientSize = new Size(240, 60),
                TopMost = true,
            };
            Font font = new Font("Consolas", 20);
            form.Paint += (sender, e) =>
            {
                e.Graphics.Clear(SystemColors.Window);
                TextRenderer.DrawText(e.Graphics, count.ToString(), font, form.ClientRectangle, SystemColors.WindowText);
            };

            // The title carries the frame count, so the script that started this window can work out its frame rate.
            Action frame = () =>
            {
                count++;
                if (count % 30 == 0)
                {
                    form.Text = "Screen activity " + count;
                }

                form.Invalidate();
            };

            if (interval > 0)
            {
                // Redraw on a Windows Forms timer instead, for comparison.
                Timer timer = new Timer { Interval = interval };
                timer.Tick += (sender, e) => frame();
                timer.Start();
                Application.Run(form);
                return;
            }

            // Redraw once per composition frame: DwmFlush returns after the desktop window manager composes the
            // next frame, so the window changes at the display's refresh rate without any timer.
            form.Show();
            while (form.Visible)
            {
                frame();
                form.Update();
                DwmFlush();
                Application.DoEvents();
            }
        }
    }
}
"@
[Invicta.ActivityWindow]::Run(__INTERVAL__)
'@
$window = $window.Replace('__INTERVAL__', [string]$RedrawInterval)
$redraw = if ($RedrawInterval -gt 0) { "every $RedrawInterval ms" } else { 'on every frame' }
$encoded = [Convert]::ToBase64String([Text.Encoding]::Unicode.GetBytes($window))
Add-Type -AssemblyName System.Speech
$voice = New-Object System.Speech.Synthesis.SpeechSynthesizer
$voice.Volume = 100
$noun = if ($Sample.Count -eq 1) { 'sample' } else { 'samples' }
$voice.Speak("Opening a small counter window and running $($Sample.Count) $noun with the screens on.")

$process = Start-Process -FilePath 'powershell.exe' -ArgumentList '-NoProfile', '-Sta', '-EncodedCommand', $encoded -PassThru
Start-Sleep -Seconds 3
$process.Refresh()
if ($process.HasExited -or $process.MainWindowHandle -eq [IntPtr]::Zero) {
    Stop-Process -Id $process.Id -ErrorAction SilentlyContinue
    throw 'The screen activity window is not visible, so the displays may not be generating VSync interrupts.'
}

function Get-FrameCount {
    $process.Refresh()
    [long]($process.MainWindowTitle -replace '^Screen activity ', '')
}

$outputPath = (New-Item -ItemType Directory -Force -Path $OutputDirectory).FullName
$failed = @()
try {
    foreach ($name in $Sample) {
        $project = Join-Path $repositoryRoot "samples\$name"
        $file = Join-Path $outputPath "$name.txt"
        $rate = (Get-CimInstance Win32_VideoController | Where-Object CurrentRefreshRate | Select-Object -First 1).CurrentRefreshRate
        $header = "Recorded $(Get-Date -Format o) with the screens on at $rate Hz and a window redrawing $redraw.`r`n`r`n"
        [IO.File]::WriteAllText($file, $header, (New-Object Text.UTF8Encoding $false))

        Write-Host "$(Get-Date -Format o) Running $name."
        $startFrames = Get-FrameCount
        $stopwatch = [Diagnostics.Stopwatch]::StartNew()
        # cmd does the redirection, so Windows PowerShell neither re-encodes the output nor treats stderr as errors.
        $ErrorActionPreference = 'Continue'
        cmd /c "dotnet run -c Release --no-build --project `"$project`" >> `"$file`" 2>&1"
        $exitCode = $LASTEXITCODE
        $ErrorActionPreference = 'Stop'
        $frameRate = ((Get-FrameCount) - $startFrames) / $stopwatch.Elapsed.TotalSeconds
        $footer = "`r`nThe activity window redrew {0:F0} times per second during this run.`r`n" -f $frameRate
        [IO.File]::AppendAllText($file, $footer, (New-Object Text.UTF8Encoding $false))
        Write-Host "$(Get-Date -Format o) $name finished with exit code $exitCode; window at $([math]::Round($frameRate)) fps."
        if ($exitCode -ne 0) {
            $failed += $name
        }
    }
}
finally {
    Stop-Process -Id $process.Id -ErrorAction SilentlyContinue
}

if ($failed.Count -eq 0) {
    $voice.Speak('Finished. The counter window has closed.')
}
else {
    $voice.Speak("Finished, but $($failed.Count) failed. The counter window has closed.")
}

$voice.Dispose()
if ($failed.Count -ne 0) {
    throw "These samples failed: $($failed -join ', ')."
}
