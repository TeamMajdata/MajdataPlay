<#
.SYNOPSIS
Stress-tests a copied Vulkan Player's startup and process-owned window resizing.
.DESCRIPTION
Place the built MajdataPlay Player and a copied settings.json in an ignored .work
directory before running. The default fixture is .work/player-crash. This script
does not copy, update, or rebuild the Player. It starts only that isolated Player,
checks HWND ownership before each window action, and stops only its own processes.
Logs and summary.json are saved below .work/vulkan-startup/<label-guid>.
.EXAMPLE
./Tools/Tests/FFmpegValidation/ValidateVulkanPlayerStartup.ps1 -Attempts 10 -MinimizeRestore
#>
param(
    [string] $PlayerDirectory = (Join-Path $PSScriptRoot '.work/player-crash'),
    [ValidateRange(1, 10)][int] $Attempts = 10,
    [ValidateRange(1, 8)][int] $SecondsPerAttempt = 8,
    [ValidatePattern('^[a-zA-Z0-9_-]+$')][string] $Label = 'baseline',
    [switch] $MinimizeRestore
)

$ErrorActionPreference = 'Stop'
$fixtureRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '.work'))
$playerRoot = [IO.Path]::GetFullPath($PlayerDirectory)
if ($playerRoot -ne $fixtureRoot -and -not $playerRoot.StartsWith($fixtureRoot + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) {
    throw 'Vulkan startup validation is restricted to copied Players within the ignored FFmpegValidation/.work directory.'
}
$executable = Join-Path $playerRoot 'MajdataPlay.exe'
if (-not (Test-Path -LiteralPath $executable)) { throw "Missing copied Player: $executable" }
$bridge = Join-Path $playerRoot 'MajdataPlay_Data/Plugins/x86_64/FFmpegUnityBridge.dll'
$settings = Join-Path $playerRoot 'settings.json'
if (-not (Test-Path -LiteralPath $bridge)) { throw "Missing copied GPU bridge: $bridge" }
if (-not (Test-Path -LiteralPath $settings)) { throw "Missing fixture settings copy: $settings" }
if ($MinimizeRestore -and $SecondsPerAttempt -lt 5) {
    throw 'Minimize/restore validation requires at least five seconds per attempt.'
}
$bridgeHash = (Get-FileHash -LiteralPath $bridge -Algorithm SHA256).Hash
$settingsHash = (Get-FileHash -LiteralPath $settings -Algorithm SHA256).Hash
$runRoot = Join-Path $fixtureRoot ('vulkan-startup/' + $Label + '-' + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $runRoot | Out-Null

Add-Type -TypeDefinition @'
#nullable enable
using System;
using System.Runtime.InteropServices;

namespace MajdataPlay.FFmpeg.Validation
{
    /// <summary>Accesses only the window returned by a process created by this stress test.</summary>
    public static class StartupResizeWindow
    {
        /// <summary>Returns the owner of a borrowed HWND before the test attempts to resize it.</summary>
        /// <param name="window">The process-created window handle to inspect.</param>
        /// <param name="processId">Receives the process identifier that currently owns the handle.</param>
        /// <returns>The window owner's thread identifier, or zero if the handle is invalid.</returns>
        [DllImport("user32.dll", SetLastError = true)]
        public static extern uint GetWindowThreadProcessId(IntPtr window, out uint processId);

        /// <summary>Receives each top-level HWND during the process-owned fallback window lookup.</summary>
        /// <param name="window">The borrowed HWND being inspected.</param>
        /// <param name="context">Unused callback context.</param>
        /// <returns>Whether enumeration should continue.</returns>
        private delegate bool EnumerateWindow(IntPtr window, IntPtr context);

        /// <summary>Enumerates top-level windows without changing any window state.</summary>
        /// <param name="callback">The read-only callback that checks each window owner.</param>
        /// <param name="context">Unused callback context.</param>
        /// <returns>Whether enumeration completed without stopping early.</returns>
        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool EnumWindows(EnumerateWindow callback, IntPtr context);

        /// <summary>Determines whether a top-level window has a caption before considering it as the Player window.</summary>
        /// <param name="window">The process-owned window being considered.</param>
        /// <returns>The caption's character count.</returns>
        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern int GetWindowTextLength(IntPtr window);

        /// <summary>Finds a captioned window only after its current owner matches the test-created process.</summary>
        /// <param name="processId">The identifier returned by this test's Start-Process call.</param>
        /// <returns>A borrowed process-owned HWND, or zero if no matching window exists.</returns>
        public static IntPtr FindOwnedWindow(uint processId)
        {
            var found = IntPtr.Zero;
            EnumWindows((window, context) =>
            {
                GetWindowThreadProcessId(window, out var owner);
                if (owner == processId && GetWindowTextLength(window) > 0)
                {
                    found = window;
                    return false;
                }

                return true;
            }, IntPtr.Zero);
            return found;
        }

        /// <summary>Queues a process-owned window minimize or restore without blocking or requesting activation.</summary>
        /// <param name="window">The process-created HWND whose ownership was just checked.</param>
        /// <param name="command">SW_SHOWMINNOACTIVE (7) or SW_SHOWNOACTIVATE (4).</param>
        /// <returns>Whether the state change request was queued.</returns>
        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool ShowWindowAsync(IntPtr window, int command);

        /// <summary>Resizes the test-created window while preserving its position, activation, and Z order.</summary>
        /// <param name="window">The process-created HWND whose ownership was just checked.</param>
        /// <param name="insertAfter">Unused because the test passes the preserve-Z-order flag.</param>
        /// <param name="x">Unused because the test passes the preserve-position flag.</param>
        /// <param name="y">Unused because the test passes the preserve-position flag.</param>
        /// <param name="width">The new outer window width, in pixels.</param>
        /// <param name="height">The new outer window height, in pixels.</param>
        /// <param name="flags">The Win32 flags that prevent moving, activating, or reordering the window.</param>
        /// <returns>Whether the resize request succeeded.</returns>
        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool SetWindowPos(IntPtr window, IntPtr insertAfter,
            int x, int y, int width, int height, uint flags);
    }
}
'@

$sizes = @(
    @(640, 480), @(872, 1197), @(960, 720), @(641, 481),
    @(870, 1195), @(800, 600), @(320, 240), @(1080, 1440)
)
$rows = [Collections.Generic.List[object]]::new()
for ($attempt = 1; $attempt -le $Attempts; $attempt++) {
    $logPath = Join-Path $runRoot ("attempt-$attempt.log")
    $process = $null
    $alive = $false
    $exitCode = $null
    $resizeRequests = 0
    $resizeFailures = 0
    $foundWindow = $false
    $minimizeRequests = 0
    $restoreRequests = 0
    $windowStateFailures = 0
    $windowStateStep = 0
    $testKilled = $false
    $watch = [Diagnostics.Stopwatch]::StartNew()
    try {
        $process = Start-Process -FilePath $executable -WorkingDirectory $playerRoot -WindowStyle Hidden -PassThru `
            -ArgumentList @('-force-vulkan', '-logFile', ('"' + $logPath + '"'))
        $createdProcessId = $process.Id
        while ($watch.Elapsed.TotalSeconds -lt ($SecondsPerAttempt - 0.08)) {
            $process.Refresh()
            if ($process.HasExited) { break }
            $window = $process.MainWindowHandle
            if ($window -eq [IntPtr]::Zero) {
                $window = [MajdataPlay.FFmpeg.Validation.StartupResizeWindow]::FindOwnedWindow([uint32] $createdProcessId)
            }
            if ($window -ne [IntPtr]::Zero -and $watch.Elapsed.TotalMilliseconds -ge 500) {
                [uint32] $ownerProcessId = 0
                [MajdataPlay.FFmpeg.Validation.StartupResizeWindow]::GetWindowThreadProcessId($window, [ref] $ownerProcessId) | Out-Null
                if ($ownerProcessId -ne $createdProcessId) {
                    throw "Refusing to operate an HWND not owned by created process $createdProcessId."
                }
                $foundWindow = $true
                if ($MinimizeRestore -and $windowStateStep -lt 4 -and $watch.Elapsed.TotalMilliseconds -ge (2000 + ($windowStateStep * 750))) {
                    if ($windowStateStep % 2 -eq 0) {
                        # SW_SHOWMINNOACTIVE minimizes only this owned window without taking focus.
                        $stateRequested = [MajdataPlay.FFmpeg.Validation.StartupResizeWindow]::ShowWindowAsync($window, 7)
                        $minimizeRequests++
                    }
                    else {
                        # SW_SHOWNOACTIVATE restores only this owned window without taking focus.
                        $stateRequested = [MajdataPlay.FFmpeg.Validation.StartupResizeWindow]::ShowWindowAsync($window, 4)
                        $restoreRequests++
                    }
                    if (-not $stateRequested) { $windowStateFailures++ }
                    $windowStateStep++
                }
                $size = $sizes[$resizeRequests % $sizes.Count]
                # Preserve position/Z order/activation and queue asynchronously to bound the test duration.
                $resized = [MajdataPlay.FFmpeg.Validation.StartupResizeWindow]::SetWindowPos(
                    $window, [IntPtr]::Zero, 0, 0, $size[0], $size[1], 0x4016)
                $resizeRequests++
                if (-not $resized) { $resizeFailures++ }
            }
            Start-Sleep -Milliseconds 75
        }
        $process.Refresh()
        $alive = -not $process.HasExited
        if (-not $alive) { $exitCode = $process.ExitCode }
        $watch.Stop()
    }
    finally {
        if ($null -ne $process) {
            $process.Refresh()
            if (-not $process.HasExited) {
                # This retained process object is only the process created above.
                $process.Kill()
                $testKilled = $true
                $process.WaitForExit(2000) | Out-Null
            }
            $process.Dispose()
        }
    }
    $logText = if (Test-Path -LiteralPath $logPath) { [IO.File]::ReadAllText($logPath) } else { '' }
    $row = [pscustomobject]@{
        Attempt = $attempt
        ProcessId = $createdProcessId
        AliveAtDeadline = $alive
        ExitCodeBeforeCleanup = $exitCode
        TitleLoaded = $logText.Contains('Scene loaded: Title')
        CrashLogged = $logText.Contains('Crash!!!')
        AcquireNextImageCrash = $logText.Contains('vk::OnscreenSwapChain::AcquireNextImage')
        WindowFound = $foundWindow
        ResizeRequests = $resizeRequests
        ResizeFailures = $resizeFailures
        MinimizeRequests = $minimizeRequests
        RestoreRequests = $restoreRequests
        WindowStateFailures = $windowStateFailures
        TestKilledOwnProcess = $testKilled
        ObservedSeconds = [Math]::Round($watch.Elapsed.TotalSeconds, 2)
        LogPath = $logPath
        Passed = $alive -and $foundWindow -and $resizeRequests -gt 0 -and $resizeFailures -eq 0 `
            -and $windowStateFailures -eq 0 -and -not $logText.Contains('Crash!!!') `
            -and -not $logText.Contains('vk::OnscreenSwapChain::AcquireNextImage') `
            -and ($MinimizeRestore -or $logText.Contains('Scene loaded: Title')) `
            -and (-not $MinimizeRestore -or ($minimizeRequests -eq 2 -and $restoreRequests -eq 2))
    }
    $rows.Add($row)
    Write-Output ("Attempt $attempt/${Attempts}: alive=$alive title=" + $row.TitleLoaded +
        " crash=" + $row.CrashLogged + " hwnd=$foundWindow resize=$resizeRequests failed=$resizeFailures elapsed=" + $row.ObservedSeconds)
}
$settingsHashAfter = (Get-FileHash -LiteralPath $settings -Algorithm SHA256).Hash
$failedAttempts = @($rows | Where-Object { -not $_.Passed }).Count
$passed = $failedAttempts -eq 0 -and $settingsHash -eq $settingsHashAfter
$summary = [pscustomobject]@{
    Passed = $passed
    PlayerDirectory = $playerRoot
    BridgeSha256 = $bridgeHash
    Attempts = $Attempts
    SecondsPerAttempt = $SecondsPerAttempt
    MinimizeRestore = [bool] $MinimizeRestore
    Survivors = @($rows | Where-Object AliveAtDeadline).Count
    TitleLoaded = @($rows | Where-Object TitleLoaded).Count
    CrashLogged = @($rows | Where-Object CrashLogged).Count
    AcquireNextImageCrashes = @($rows | Where-Object AcquireNextImageCrash).Count
    ResizeRequests = ($rows | Measure-Object ResizeRequests -Sum).Sum
    MinimizeRequests = ($rows | Measure-Object MinimizeRequests -Sum).Sum
    RestoreRequests = ($rows | Measure-Object RestoreRequests -Sum).Sum
    WindowStateFailures = ($rows | Measure-Object WindowStateFailures -Sum).Sum
    FailedAttempts = $failedAttempts
    SettingsHashBefore = $settingsHash
    SettingsHashAfter = $settingsHashAfter
    FixtureSettingsUnchanged = $settingsHash -eq $settingsHashAfter
    Rows = @($rows.ToArray())
}
$summaryPath = Join-Path $runRoot 'summary.json'
[IO.File]::WriteAllText($summaryPath, ($summary | ConvertTo-Json -Depth 5), [Text.UTF8Encoding]::new($false))
Write-Output ("Summary: " + $summary.Survivors + "/$Attempts survivors; " + $summary.TitleLoaded +
    " Title-loaded; " + $summary.CrashLogged + " crash logs; " + $summary.AcquireNextImageCrashes +
    " AcquireNextImage crashes; " + $summary.ResizeRequests + " resize requests; settings unchanged=" + $summary.FixtureSettingsUnchanged)
Write-Output "Evidence: $summaryPath"
if (-not $passed) {
    Write-Error "Vulkan Player startup validation failed: $failedAttempts failed attempts; fixture settings unchanged=$($summary.FixtureSettingsUnchanged)."
    exit 1
}
