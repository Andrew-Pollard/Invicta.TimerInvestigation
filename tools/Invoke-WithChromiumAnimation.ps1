# © 2026 Andrew Pollard. All rights reserved.

<#
.SYNOPSIS
Runs samples while a small Microsoft Edge window animates animation.html.

.DESCRIPTION
Opens animation.html as an app window in a temporary Edge profile, so it runs as a separate browser instance and
closing it leaves any other Edge windows alone. Once the page is animating, each sample runs in turn with its
output written to a file, followed by the page's measured frame rate. The window and the temporary profile are
removed afterwards.

Other Chromium-based applications that are animating at the same time, including Electron apps, have the same
effect as the page; minimise them first for a clean comparison.

.PARAMETER Sample
The sample projects to run, each optionally followed by its arguments, such as 'WakeGrid 3'.

.PARAMETER OutputDirectory
The directory to write each sample's output to, as <Sample>.txt.

.PARAMETER Mode
What the page animates: 'both' (the default), 'raf' for requestAnimationFrame only, 'css' for a CSS spinner only,
or 'static' for nothing. The frame rate is only measured when requestAnimationFrame runs.

.EXAMPLE
tools\Invoke-WithChromiumAnimation.ps1 -Sample 'WakeGrid 3', ResolutionWatch -OutputDirectory results\chromium-animation
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [string[]] $Sample,

    [Parameter(Mandatory)]
    [string] $OutputDirectory,

    [ValidateSet('both', 'raf', 'css', 'static')]
    [string] $Mode = 'both'
)

$ErrorActionPreference = 'Stop'
$repositoryRoot = Split-Path -Parent $PSScriptRoot

$edge = @(
    (Join-Path ${env:ProgramFiles(x86)} 'Microsoft\Edge\Application\msedge.exe'),
    (Join-Path $env:ProgramFiles 'Microsoft\Edge\Application\msedge.exe')
) | Where-Object { Test-Path -LiteralPath $_ } | Select-Object -First 1
if (-not $edge) {
    throw 'Microsoft Edge was not found.'
}

Add-Type -Namespace Invicta -Name Windows -MemberDefinition @'
public delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

[DllImport("user32.dll")]
public static extern bool EnumWindows(EnumWindowsProc callback, IntPtr lParam);

[DllImport("user32.dll")]
public static extern bool IsWindowVisible(IntPtr hWnd);

[DllImport("user32.dll", CharSet = CharSet.Unicode)]
public static extern int GetWindowText(IntPtr hWnd, System.Text.StringBuilder text, int count);

[DllImport("user32.dll")]
public static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int X, int Y, int cx, int cy, uint uFlags);
'@

# Finds the page's window by its title; returns its handle and the frame count its title carries, or $null.
function Find-PageWindow {
    $script:pageWindow = $null
    [void][Invicta.Windows]::EnumWindows({
            param($handle, $unused)
            $title = New-Object System.Text.StringBuilder 256
            [void][Invicta.Windows]::GetWindowText($handle, $title, $title.Capacity)
            if ([Invicta.Windows]::IsWindowVisible($handle) -and $title.ToString() -match "^$Mode (\d+)$") {
                $script:pageWindow = [pscustomobject]@{ Handle = $handle; Frames = [long]$Matches[1] }
            }

            $true
        }, [IntPtr]::Zero)
    $script:pageWindow
}

function Get-FrameCount {
    $window = Find-PageWindow
    if ($window) { $window.Frames } else { -1 }
}

& dotnet build (Join-Path $repositoryRoot 'Invicta.TimerInvestigation.slnx') -c Release --nologo -v q
if ($LASTEXITCODE -ne 0) {
    throw "The build failed with exit code $LASTEXITCODE."
}

$profileDirectory = Join-Path ([IO.Path]::GetTempPath()) "Invicta.TimerInvestigation-edge-$([guid]::NewGuid().ToString('N'))"
$page = [Uri]::new((Join-Path $PSScriptRoot 'animation.html')).AbsoluteUri + "?mode=$Mode"
$outputPath = (New-Item -ItemType Directory -Force -Path $OutputDirectory).FullName
$utf8 = New-Object Text.UTF8Encoding $false
$failed = @()

function Stop-TemporaryEdge {
    Get-CimInstance Win32_Process |
        Where-Object { $_.CommandLine -and $_.CommandLine.Contains($profileDirectory) } |
        ForEach-Object { Stop-Process -Id $_.ProcessId -Force -ErrorAction SilentlyContinue }
    Start-Sleep -Seconds 2
}

try {
    $edgeArguments = @(
        "--user-data-dir=`"$profileDirectory`"", '--no-first-run', '--no-default-browser-check',
        '--window-size=360,120', '--window-position=40,40', "--app=$page")

    # Pages do not animate on a new profile's first launch, so launch once to initialize the profile first.
    Start-Process -FilePath $edge -ArgumentList $edgeArguments | Out-Null
    Start-Sleep -Seconds 8
    Stop-TemporaryEdge

    Start-Process -FilePath $edge -ArgumentList $edgeArguments | Out-Null
    # A new profile takes a while to start. Wait for the page's window and keep it on top, because Chromium stops
    # animating windows it considers covered; then wait for the frame count to rise if the page counts frames.
    $deadline = (Get-Date).AddSeconds(30)
    $previous = -1
    $ready = $false
    while (-not $ready -and (Get-Date) -lt $deadline) {
        Start-Sleep -Milliseconds 500
        $window = Find-PageWindow
        if (-not $window) {
            continue
        }

        # HWND_TOPMOST, with SWP_NOSIZE | SWP_NOMOVE | SWP_SHOWWINDOW.
        [void][Invicta.Windows]::SetWindowPos($window.Handle, [IntPtr](-1), 0, 0, 0, 0, 0x0043)
        $ready = if ($Mode -in 'raf', 'both') { $previous -ge 0 -and $window.Frames -gt $previous } else { $true }
        $previous = $window.Frames
    }

    if (-not $ready) {
        throw 'The animation page did not start; is its window visible?'
    }

    Start-Sleep -Seconds 3

    $rate = (Get-CimInstance Win32_VideoController | Where-Object CurrentRefreshRate | Select-Object -First 1).CurrentRefreshRate
    foreach ($entry in $Sample) {
        $name, $arguments = $entry -split ' ', 2
        $project = Join-Path $repositoryRoot "samples\$name"
        $file = Join-Path $outputPath "$name.txt"
        $header = "Recorded $(Get-Date -Format o) with the screens on at $rate Hz and animation.html open in Edge (mode $Mode).`r`n`r`n"
        [IO.File]::WriteAllText($file, $header, $utf8)

        Write-Host "$(Get-Date -Format o) Running $entry."
        $startFrames = Get-FrameCount
        $stopwatch = [Diagnostics.Stopwatch]::StartNew()
        # cmd does the redirection, so Windows PowerShell neither re-encodes the output nor treats stderr as errors.
        $ErrorActionPreference = 'Continue'
        cmd /c "dotnet run -c Release --no-build --project `"$project`" -- $arguments >> `"$file`" 2>&1"
        $exitCode = $LASTEXITCODE
        $ErrorActionPreference = 'Stop'
        if ($Mode -in 'raf', 'both' -and $startFrames -ge 0) {
            $frameRate = ((Get-FrameCount) - $startFrames) / $stopwatch.Elapsed.TotalSeconds
            [IO.File]::AppendAllText($file, ("`r`nThe page animated at {0:F0} frames per second during this run.`r`n" -f $frameRate), $utf8)
        }

        Write-Host "$(Get-Date -Format o) $name finished with exit code $exitCode."
        if ($exitCode -ne 0) {
            $failed += $name
        }
    }
}
finally {
    Stop-TemporaryEdge
    if (Test-Path -LiteralPath $profileDirectory) {
        Remove-Item -LiteralPath $profileDirectory -Recurse -Force -ErrorAction SilentlyContinue
    }
}

if ($failed.Count -ne 0) {
    throw "These samples failed: $($failed -join ', ')."
}
