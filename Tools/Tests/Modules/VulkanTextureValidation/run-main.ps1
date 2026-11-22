param(
    [ValidateRange(1, 10)][int] $Attempts = 1,
    [ValidateRange(10, 600)][int] $SecondsInGame = 200,
    [switch] $DisableObsCapture,
    [ValidateSet('vulkan', 'd3d12')][string] $Graphics = 'vulkan',
    [ValidateSet('Hardware', 'Software')][string] $ExpectedDecoder = 'Hardware',
    [ValidateSet('ffmpeg', 'synthetic')][string] $FrameSource = 'ffmpeg',
    [switch] $StaticSyntheticFrame,
    [switch] $DecoupleSyntheticUpload,
    [switch] $ReadbackGate,
    [switch] $NativeQueueFence,
    [switch] $BufferBarrier,
    [ValidateSet('Narrow', 'Broad')][string] $BufferBarrierMode = 'Broad',
    [ValidateRange(0, 10)][int] $DriverObservationSeconds = 0,
    [ValidateSet('Any', 'SplitJobs', 'Threaded', 'Direct')][string] $ExpectedThreadingMode = 'Any',
    [string] $PlayerDirectory = '',
    [string] $OutputDirectory = ''
)
$ErrorActionPreference = 'Stop'
if (($ReadbackGate -or $NativeQueueFence) -and ($FrameSource -ne 'synthetic' -or $StaticSyntheticFrame -or $DecoupleSyntheticUpload -or ($ReadbackGate -and $NativeQueueFence))) {
    throw 'Select one completion backend for the sampled dynamic synthetic source without static or decoupled controls.'
}
$module = (Resolve-Path -LiteralPath $PSScriptRoot).Path
if ($BufferBarrier -and ($FrameSource -ne 'synthetic' -or $StaticSyntheticFrame -or $DecoupleSyntheticUpload -or $ReadbackGate -or $NativeQueueFence)) {
    throw 'BufferBarrier requires the ordinary sampled dynamic synthetic source without other experimental schedules.'
}
if (-not $PlayerDirectory) {
    $PlayerDirectory = Join-Path $module '.work/MainPlayer'
}
$gameDirectory = (Resolve-Path -LiteralPath $PlayerDirectory).Path
$workPrefix = [IO.Path]::GetFullPath((Join-Path $module '.work')).TrimEnd('\') + '\'
if (-not $gameDirectory.StartsWith($workPrefix, [StringComparison]::OrdinalIgnoreCase)) {
    throw 'The Player must be an isolated copy under this module .work directory.'
}
$game = Join-Path $gameDirectory 'MajdataPlay.exe'
if (-not (Test-Path -LiteralPath (Join-Path $gameDirectory 'fixture-manifest.json') -PathType Leaf)) {
    throw 'Run prepare-main.ps1 first; this runner only accepts its isolated MainPlayer fixture.'
}
if (-not $OutputDirectory) {
    $OutputDirectory = Join-Path $module ('.work/main-' + (Get-Date -Format 'yyyyMMdd-HHmmss') + '-' + [guid]::NewGuid().ToString('N').Substring(0, 6))
}
if (Test-Path -LiteralPath $OutputDirectory) {
    throw 'Use a new output directory so previous logs cannot be mixed into this case.'
}
New-Item -ItemType Directory -Path $OutputDirectory | Out-Null
$output = (Resolve-Path -LiteralPath $OutputDirectory).Path
Add-Type -AssemblyName System.Windows.Forms
if (-not ('MainProbeWindow' -as [type])) {
    Add-Type -TypeDefinition @'
using System;
using System.Runtime.InteropServices;
/// <summary>Checks the foreground owner before sending input to the launched game.</summary>
public static class MainProbeWindow
{
    /// <summary>Returns the foreground window handle.</summary>
    [DllImport("user32.dll")]
    public static extern IntPtr GetForegroundWindow();
    /// <summary>Returns the owner process of a window.</summary>
    /// <param name="window">The window to inspect.</param>
    /// <param name="owner">Receives the owning process identifier.</param>
    /// <returns>The window thread identifier.</returns>
    [DllImport("user32.dll")]
    public static extern uint GetWindowThreadProcessId(IntPtr window, out uint owner);
    /// <summary>Injects one keyboard transition for a verified foreground game window.</summary>
    /// <param name="virtualKey">The Windows virtual-key code.</param>
    /// <param name="scanCode">The optional hardware scan code.</param>
    /// <param name="flags">Zero for key down or two for key up.</param>
    /// <param name="extraInfo">The pointer-sized application value, normally zero.</param>
    [DllImport("user32.dll", EntryPoint = "keybd_event", ExactSpelling = true, CallingConvention = CallingConvention.Winapi)]
    public static extern void KeybdEvent(byte virtualKey, byte scanCode, uint flags, UIntPtr extraInfo);
    /// <summary>Restores the selected diagnostic window without sending keyboard input.</summary>
    /// <param name="window">The verified diagnostic window handle.</param>
    /// <param name="command">The Win32 show command, normally restore.</param>
    /// <returns>Whether the window was previously visible.</returns>
    [DllImport("user32.dll")]
    private static extern bool ShowWindowAsync(IntPtr window, int command);
    /// <summary>Requests foreground ownership for the selected window.</summary>
    /// <param name="window">The verified diagnostic window handle.</param>
    /// <returns>Whether the request succeeded.</returns>
    [DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(IntPtr window);
    /// <summary>Temporarily shares input queues while activating the selected window.</summary>
    /// <param name="source">The caller thread identifier.</param>
    /// <param name="target">The foreground thread identifier.</param>
    /// <param name="attach">Whether to attach or detach the input queues.</param>
    /// <returns>Whether the operation succeeded.</returns>
    [DllImport("user32.dll")]
    private static extern bool AttachThreadInput(uint source, uint target, bool attach);
    /// <summary>Gets the current caller thread identifier.</summary>
    /// <returns>The native identifier of the calling thread.</returns>
    [DllImport("kernel32.dll")]
    private static extern uint GetCurrentThreadId();
    /// <summary>Activates only a window owned by the diagnostic child and always detaches its temporary input queue.</summary>
    /// <param name="window">The diagnostic process main-window handle.</param>
    /// <param name="expectedOwner">The exact child process identifier.</param>
    /// <returns>Whether the verified diagnostic process owns the foreground window afterward.</returns>
    public static bool ActivateOwnedWindow(IntPtr window, uint expectedOwner)
    {
        uint owner;
        if (window == IntPtr.Zero || GetWindowThreadProcessId(window, out owner) == 0 || owner != expectedOwner)
        {
            return false;
        }
        ShowWindowAsync(window, 9);
        SetForegroundWindow(window);
        var foreground = GetForegroundWindow();
        uint foregroundOwner;
        var foregroundThread = GetWindowThreadProcessId(foreground, out foregroundOwner);
        if (foregroundOwner == expectedOwner)
        {
            return true;
        }
        var callerThread = GetCurrentThreadId();
        var attached = foregroundThread != 0 && foregroundThread != callerThread
            && AttachThreadInput(callerThread, foregroundThread, true);
        try
        {
            SetForegroundWindow(window);
        }
        finally
        {
            if (attached)
            {
                AttachThreadInput(callerThread, foregroundThread, false);
            }
        }
        GetWindowThreadProcessId(GetForegroundWindow(), out owner);
        return owner == expectedOwner;
    }
}
'@
}
$shell = New-Object -ComObject WScript.Shell
$rows = New-Object 'System.Collections.Generic.List[object]'
for ($attempt = 1; $attempt -le $Attempts; $attempt++) {
    $case = Join-Path $output ('attempt-' + $attempt)
    New-Item -ItemType Directory -Path $case | Out-Null
    $playerLog = Join-Path $case 'Player.log'
    $runtimeLog = Join-Path $gameDirectory 'Logs/MajPlayRuntime.log'
    $started = Get-Date
    $configHashes = @{}
    foreach ($name in @('settings.json', 'system-settings.json')) {
        $configHashes[$name] = (Get-FileHash -LiteralPath (Join-Path $gameDirectory $name) -Algorithm SHA256).Hash
    }
    $bootPath = Join-Path $gameDirectory 'MajdataPlay_Data/boot.config'
    $bootHash = (Get-FileHash -LiteralPath $bootPath -Algorithm SHA256).Hash
    Copy-Item -LiteralPath $bootPath -Destination (Join-Path $case 'boot.config')
    $artifactNames = @('MajdataPlay_Data/Managed/MajdataPlay.Core.dll', 'MajdataPlay_Data/Managed/MajdataPlay.FFmpeg.dll', 'MajdataPlay_Data/Managed/MajdataPlay.MajRenderPipe.dll', 'MajdataPlay_Data/Managed/MajdataPlay.SyntheticFrameProbe.dll', 'MajdataPlay_Data/Plugins/x86_64/FFmpegUnityBridge.dll', 'MajdataPlay_Data/Plugins/x86_64/MajdataPlayVulkanPacing.dll')
    $artifactHashes = @{}
    foreach ($artifactName in $artifactNames) {
        $artifactPath = Join-Path $gameDirectory $artifactName
        $artifactHashes[$artifactName] = if (Test-Path -LiteralPath $artifactPath -PathType Leaf) { (Get-FileHash -LiteralPath $artifactPath -Algorithm SHA256).Hash } else { 'ABSENT' }
    }
    $safeSettings = Get-Content -LiteralPath (Join-Path $gameDirectory 'system-settings.json') -Raw | ConvertFrom-Json
    if ($safeSettings.Network.Online.Enable -or $safeSettings.Common.RecordMode -ne 'Disable' -or
        $safeSettings.Decoding.PreferredDecoder -ne $ExpectedDecoder -or
        $safeSettings.IO.InputDevice.ButtonRing.Type -ne 'Keyboard' -or
        $safeSettings.IO.InputDevice.TouchPanel.Enable -or $safeSettings.IO.OutputDevice.Led.Enable) {
        throw 'The isolated configuration no longer matches the offline keyboard-only fixture.'
    }
    $arguments = @("-force-$Graphics", '-logFile', ('"{0}"' -f $playerLog))
    $oldObs = [Environment]::GetEnvironmentVariable('DISABLE_VULKAN_OBS_CAPTURE', 'Process')
    $childObs = if ($DisableObsCapture) { '1' } else { $oldObs }
    $oldSyntheticStatic = [Environment]::GetEnvironmentVariable('MAJ_SYNTHETIC_STATIC', 'Process')
    $childSyntheticStatic = if ($StaticSyntheticFrame) { '1' } else { '0' }
    $oldSyntheticDecouple = [Environment]::GetEnvironmentVariable('MAJ_SYNTHETIC_DECOUPLE', 'Process')
    $childSyntheticDecouple = if ($DecoupleSyntheticUpload) { '1' } else { '0' }
    $oldReadbackGate = [Environment]::GetEnvironmentVariable('MAJ_SYNTHETIC_READBACK_GATE', 'Process')
    $childReadbackGate = if ($ReadbackGate) { '1' } else { '0' }
    $oldNativeQueueFence = [Environment]::GetEnvironmentVariable('MAJ_SYNTHETIC_NATIVE_FENCE', 'Process')
    $childNativeQueueFence = if ($NativeQueueFence) { '1' } else { '0' }
    $oldBufferBarrier = [Environment]::GetEnvironmentVariable('MAJ_SYNTHETIC_BUFFER_BARRIER', 'Process')
    $childBufferBarrier = if (-not $BufferBarrier) { '0' } elseif ($BufferBarrierMode -eq 'Narrow') { '1' } else { '2' }
    [ordered]@{
        Started = $started.ToString('o'); Arguments = $arguments; ConfigSHA256 = $configHashes
        DisableObsCapture = [bool]$DisableObsCapture; ChildObsDisableValue = $childObs
        FrameSource = $FrameSource; ExpectedDecoder = $ExpectedDecoder; DriverObservationSeconds = $DriverObservationSeconds
        ExpectedThreadingMode = $ExpectedThreadingMode; BootConfigSHA256 = $bootHash
        StaticSyntheticFrame = [bool]$StaticSyntheticFrame; ChildSyntheticStaticValue = $childSyntheticStatic
        DecoupleSyntheticUpload = [bool]$DecoupleSyntheticUpload; ChildSyntheticDecoupleValue = $childSyntheticDecouple
        ReadbackGate = [bool]$ReadbackGate; ChildReadbackGateValue = $childReadbackGate
        NativeQueueFence = [bool]$NativeQueueFence; ChildNativeQueueFenceValue = $childNativeQueueFence
        BufferBarrier = [bool]$BufferBarrier; ChildBufferBarrierValue = $childBufferBarrier; BufferBarrierMode = $BufferBarrierMode
        Executable = $game; WorkingDirectory = $gameDirectory; ArtifactSHA256 = $artifactHashes
    } | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath (Join-Path $case 'launch.json') -Encoding utf8
    $process = $null
    $entered = $null
    $reason = 'nav-failed'
    $events = @()
    $moduleCaptured = $false
    $cleanupStopped = $false
    $deviceLost = $false
    $failure = $null
    $runtimeText = ''
    $respondingAfterDriverEvent = $null
    $aliveAfterDriverObservation = $null
    $activationAttempts = 0
    $foregroundMatches = 0
    $keysSent = 0
    $lastNavigationReport = $started
    try {
        try {
            if ($DisableObsCapture) {
                [Environment]::SetEnvironmentVariable('DISABLE_VULKAN_OBS_CAPTURE', '1', 'Process')
            }
            [Environment]::SetEnvironmentVariable('MAJ_SYNTHETIC_STATIC', $childSyntheticStatic, 'Process')
            [Environment]::SetEnvironmentVariable('MAJ_SYNTHETIC_DECOUPLE', $childSyntheticDecouple, 'Process')
            [Environment]::SetEnvironmentVariable('MAJ_SYNTHETIC_READBACK_GATE', $childReadbackGate, 'Process')
            [Environment]::SetEnvironmentVariable('MAJ_SYNTHETIC_NATIVE_FENCE', $childNativeQueueFence, 'Process')
            [Environment]::SetEnvironmentVariable('MAJ_SYNTHETIC_BUFFER_BARRIER', $childBufferBarrier, 'Process')
            $process = Start-Process -FilePath $game -WorkingDirectory $gameDirectory -ArgumentList $arguments -PassThru
        } finally {
            # Restore the caller immediately after creating the child, before any navigation.
            [Environment]::SetEnvironmentVariable('DISABLE_VULKAN_OBS_CAPTURE', $oldObs, 'Process')
            [Environment]::SetEnvironmentVariable('MAJ_SYNTHETIC_STATIC', $oldSyntheticStatic, 'Process')
            [Environment]::SetEnvironmentVariable('MAJ_SYNTHETIC_DECOUPLE', $oldSyntheticDecouple, 'Process')
            [Environment]::SetEnvironmentVariable('MAJ_SYNTHETIC_READBACK_GATE', $oldReadbackGate, 'Process')
            [Environment]::SetEnvironmentVariable('MAJ_SYNTHETIC_NATIVE_FENCE', $oldNativeQueueFence, 'Process')
            [Environment]::SetEnvironmentVariable('MAJ_SYNTHETIC_BUFFER_BARRIER', $oldBufferBarrier, 'Process')
        }
        Write-Output ("MAIN START attempt={0} pid={1} obsDisabled={2}" -f $attempt, $process.Id, $childObs)
        $navigationDeadline = $started.AddSeconds(120)
        $totalDeadline = $started.AddSeconds(120 + $SecondsInGame + 30)
        while (-not $process.HasExited -and (Get-Date) -lt $totalDeadline) {
            $now = Get-Date
            if (Test-Path -LiteralPath $runtimeLog -PathType Leaf) {
                $logInfo = Get-Item -LiteralPath $runtimeLog
                if ($logInfo.LastWriteTimeUtc -ge $started.ToUniversalTime()) {
                    $runtimeText = Get-Content -LiteralPath $runtimeLog -Raw
                }
            }
            if ($ExpectedThreadingMode -ne 'Any' -and (Test-Path -LiteralPath $playerLog -PathType Leaf)) {
                $startupThreading = @(Select-String -LiteralPath $playerLog -Pattern 'GfxDevice: creating device client;.*kGfxThreadingMode' | ForEach-Object { $_.Line })
                if ($startupThreading.Count -gt 0 -and -not ($startupThreading -match ('kGfxThreadingMode' + $ExpectedThreadingMode + '\b'))) {
                    $reason = 'threading-mode-mismatch'
                    Write-Output ("MAIN THREADING MISMATCH expected={0} actual={1}" -f $ExpectedThreadingMode, ($startupThreading -join ' | '))
                    break
                }
            }
            $events = @(Get-WinEvent -FilterHashtable @{
                LogName = 'System'; ProviderName = 'nvlddmkm'; Id = 153; StartTime = $started
            } -ErrorAction SilentlyContinue)
            if ($events.Count -gt 0) {
                $reason = 'driver-153'
                if ($DriverObservationSeconds -gt 0) {
                    Write-Output ("MAIN DRIVER EVENT; observing this child for {0}s before cleanup" -f $DriverObservationSeconds)
                    $observeUntil = (Get-Date).AddSeconds($DriverObservationSeconds)
                    while (-not $process.HasExited -and (Get-Date) -lt $observeUntil) {
                        Start-Sleep -Milliseconds 500
                        $process.Refresh()
                    }
                    $aliveAfterDriverObservation = -not $process.HasExited
                    if ($aliveAfterDriverObservation) {
                        $respondingAfterDriverEvent = $process.Responding
                    }
                }
                break
            }
            $deviceLost = $runtimeText -match 'VK_ERROR_DEVICE_LOST'
            if (Test-Path -LiteralPath $playerLog -PathType Leaf) {
                $deviceLost = $deviceLost -or (Select-String -LiteralPath $playerLog -Pattern 'VK_ERROR_DEVICE_LOST' -Quiet)
            }
            if ($deviceLost) {
                $reason = 'device-lost'
                break
            }
            if ($null -eq $entered -and $runtimeText -match 'Scene loaded: Game(?:\s|$)') {
                $entered = $now
                $reason = 'in-game'
                Write-Output ("MAIN GAME attempt={0} time={1:o}; all navigation input is now stopped" -f $attempt, $entered)
            }
            if ($null -ne $entered) {
                # Never activate a window or send keys after this point.
                if ($FrameSource -eq 'synthetic' -and (Test-Path -LiteralPath $playerLog -PathType Leaf)) {
                    $helperFailed = Select-String -LiteralPath $playerLog -Pattern '\[SyntheticVideoProbe\] FAIL|Could not find method .Install. due to a type load error|Could not load file or assembly .MajdataPlay.SyntheticFrameProbe' -Quiet
                    $helperInstalled = Select-String -LiteralPath $playerLog -Pattern '\[SyntheticVideoProbe\] INSTALL width=854 height=480 uploadHz=60' -Quiet
                    if ($helperFailed -or ((($now - $entered).TotalSeconds -ge 15) -and -not $helperInstalled)) {
                        $reason = 'synthetic-helper-invalid'
                        break
                    }
                }
                $sceneTransitions = [regex]::Matches($runtimeText, 'Scene loaded: (?<scene>[^\r\n]+)')
                $lastScene = if ($sceneTransitions.Count -gt 0) { $sceneTransitions[$sceneTransitions.Count - 1].Groups['scene'].Value.Trim() } else { '' }
                if ($lastScene -in @('List', 'Title', 'Setting')) {
                    $reason = 'left-game-before-result'
                    break
                }
                if (-not $moduleCaptured -and ($now - $entered).TotalSeconds -ge 3) {
                    $process.Modules | Select-Object ModuleName, FileName,
                        @{Name='FileVersion';Expression={$_.FileVersionInfo.FileVersion}} |
                        Export-Csv -LiteralPath (Join-Path $case 'modules.csv') -NoTypeInformation -Encoding utf8
                    $moduleCaptured = $true
                }
                if ($runtimeText -match 'Scene loaded: Result(?:\s|$)') {
                    $reason = 'result'
                    break
                }
                if (($now - $entered).TotalSeconds -ge $SecondsInGame) {
                    $reason = 'game-timeout'
                    break
                }
                Start-Sleep -Milliseconds 1000
                $process.Refresh()
                continue
            }
            if ($now -ge $navigationDeadline) {
                $reason = 'nav-failed'
                break
            }
            # Check ownership immediately before sending real keyboard input.
            $activationAttempts++
            [void]$shell.AppActivate($process.Id)
            $process.Refresh()
            if (-not $process.HasExited -and [MainProbeWindow]::ActivateOwnedWindow($process.MainWindowHandle, [uint32]$process.Id)) {
                [uint32] $foregroundOwner = 0
                [void][MainProbeWindow]::GetWindowThreadProcessId([MainProbeWindow]::GetForegroundWindow(), [ref]$foregroundOwner)
                $process.Refresh()
                if (-not $process.HasExited -and $foregroundOwner -eq $process.Id) {
                    $foregroundMatches++
                    # Recheck the scene after activation, so a late log update cannot trigger another navigation send.
                    if (Test-Path -LiteralPath $runtimeLog -PathType Leaf) {
                        $currentLog = Get-Item -LiteralPath $runtimeLog
                        if ($currentLog.LastWriteTimeUtc -ge $started.ToUniversalTime()) {
                            $runtimeText = Get-Content -LiteralPath $runtimeLog -Raw
                        }
                    }
                    if ($runtimeText -notmatch 'Scene loaded: Game(?:\s|$)') {
                        foreach ($navigationKey in @([byte]0x58, [byte]0x43)) {
                            [uint32] $keyOwner = 0
                            [void][MainProbeWindow]::GetWindowThreadProcessId([MainProbeWindow]::GetForegroundWindow(), [ref]$keyOwner)
                            if ($keyOwner -ne $process.Id) {
                                break
                            }
                            try {
                                [MainProbeWindow]::KeybdEvent($navigationKey, 0, 0, [UIntPtr]::Zero)
                                $keysSent++
                                Start-Sleep -Milliseconds 80
                            } finally {
                                [MainProbeWindow]::KeybdEvent($navigationKey, 0, 2, [UIntPtr]::Zero)
                            }
                            Start-Sleep -Milliseconds 80
                            if (Test-Path -LiteralPath $runtimeLog -PathType Leaf) {
                                $navigationLog = Get-Item -LiteralPath $runtimeLog
                                if ($navigationLog.LastWriteTimeUtc -ge $started.ToUniversalTime() -and
                                    (Select-String -LiteralPath $runtimeLog -Pattern 'Scene loaded: Game(?:\s|$)' -Quiet)) {
                                    break
                                }
                            }
                        }
                    }
                }
            }
            if (((Get-Date) - $lastNavigationReport).TotalSeconds -ge 10) {
                Write-Output ("MAIN NAV attempts={0} foreground={1} keyDowns={2}" -f $activationAttempts, $foregroundMatches, $keysSent)
                $lastNavigationReport = Get-Date
            }
            Start-Sleep -Milliseconds 600
            $process.Refresh()
        }
        if ($process.HasExited -and $reason -notin @('result', 'driver-153', 'device-lost')) {
            $reason = 'process-exited'
        } elseif ((Get-Date) -ge $totalDeadline -and $reason -eq 'in-game') {
            $reason = 'case-timeout'
        }
    } catch {
        $failure = $_.Exception.Message
        $reason = 'runner-failed'
    } finally {
        if ($null -ne $process) {
            $process.Refresh()
            if (-not $process.HasExited) {
                # Only this exact child process is stopped; no name-based process termination is used.
                $cleanupStopped = $true
                Stop-Process -Id $process.Id -Force -ErrorAction SilentlyContinue
                [void]$process.WaitForExit(10000)
            }
        }
        $ended = Get-Date
        $events = @(Get-WinEvent -FilterHashtable @{
            LogName = 'System'; ProviderName = 'nvlddmkm'; Id = 153; StartTime = $started; EndTime = $ended
        } -ErrorAction SilentlyContinue)
        $events | ForEach-Object { $_.ToXml() } | Set-Content -LiteralPath (Join-Path $case 'driver-events.xml') -Encoding utf8
        if (Test-Path -LiteralPath $runtimeLog -PathType Leaf) {
            $runtimeInfo = Get-Item -LiteralPath $runtimeLog
            if ($runtimeInfo.LastWriteTimeUtc -ge $started.ToUniversalTime()) {
                Copy-Item -LiteralPath $runtimeLog -Destination (Join-Path $case 'MajPlayRuntime.log')
                $runtimeText = Get-Content -LiteralPath $runtimeLog -Raw
            }
        }
        $expectedGraphics = if ($Graphics -eq 'vulkan') { 'Vulkan' } else { 'Direct3D12' }
        $graphicsMatches = @($runtimeText -split '\r?\n' | Where-Object { $_ -match 'Graphices:' })
        $transferMatches = @($runtimeText -split '\r?\n' | Where-Object { $_ -match '\[(?:Player|Decoder)\].*(Texture transfer=|Prepared;|Opened codec=|Active decoder=)' })
        $engineFailure = $runtimeText -match 'VK_ERROR_DEVICE_LOST|Crash!!!|Crash detected|\[SyntheticVideoProbe\] FAIL'
        if (Test-Path -LiteralPath $playerLog -PathType Leaf) {
            $engineFailure = $engineFailure -or (Select-String -LiteralPath $playerLog -Pattern 'VK_ERROR_DEVICE_LOST|Crash!!!|Crash detected|\[SyntheticVideoProbe\] FAIL' -Quiet)
        }
        if ($events.Count -gt 0) {
            $reason = 'driver-153'
        } elseif ($engineFailure) {
            $reason = 'engine-device-lost-or-crash'
        }
        $graphicsConfirmed = [bool]($graphicsMatches -match $expectedGraphics)
        $fixtureVideoConfirmed = [bool]($transferMatches -match 'codec=h264.*size=854x480')
        $hardwarePrepared = [bool]($transferMatches -match 'type=Hardware')
        $decoderConfirmed = [bool]($transferMatches -match ('Prepared; encoding=h264,.*type=' + $ExpectedDecoder))
        $syntheticInstalled = $false
        $syntheticProgress = $false
        $syntheticModeConfirmed = $false
        $staticUploadCountConfirmed = -not $StaticSyntheticFrame
        $textureBindingConfirmed = -not $DecoupleSyntheticUpload
        $pairedTextureModePresent = $false
        $readbackGateConfirmed = -not $ReadbackGate
        $readbackCounters = @{}
        $nativeQueueFenceConfirmed = -not $NativeQueueFence
        $nativeFenceCounters = @{}
        if (Test-Path -LiteralPath $playerLog -PathType Leaf) {
            $syntheticInstalled = [bool](Select-String -LiteralPath $playerLog -Pattern '\[SyntheticVideoProbe\] INSTALL width=854 height=480 uploadHz=60' -Quiet)
            $progressEvidence = @(Select-String -LiteralPath $playerLog -Pattern '\[SyntheticVideoProbe\] PROGRESS ' | ForEach-Object { $_.Line })
            $syntheticProgress = $progressEvidence.Count -gt 0
            $expectedStaticText = ([bool]$StaticSyntheticFrame).ToString()
            $syntheticModeConfirmed = [bool](Select-String -LiteralPath $playerLog -Pattern ('\[SyntheticVideoProbe\] INSTALL .*; static=' + $expectedStaticText + '(?:;|\s|$)') -Quiet)
            $pairedTextureModePresent = [bool](Select-String -LiteralPath $playerLog -Pattern '\[SyntheticVideoProbe\] INSTALL .*; decoupled=' -Quiet)
            if (-not ($ReadbackGate -or $NativeQueueFence) -and ($pairedTextureModePresent -or $DecoupleSyntheticUpload)) {
                $expectedDecoupledText = ([bool]$DecoupleSyntheticUpload).ToString()
                $expectedUploadDisplayedText = (-not [bool]$DecoupleSyntheticUpload).ToString()
                $installBindingConfirmed = [bool](Select-String -LiteralPath $playerLog -Pattern ('\[SyntheticVideoProbe\] INSTALL .*; decoupled=' + $expectedDecoupledText + '; displayInitialUploads=1; uploadTextureDisplayed=' + $expectedUploadDisplayedText + '(?:;|\s|$)') -Quiet)
                $bindingProgress = @($progressEvidence | Where-Object { $_ -match (' decoupled=' + $expectedDecoupledText + ' bindingChecks=[1-9][0-9]* bindingMismatches=0(?:\s|;|$)') })
                $textureBindingConfirmed = $installBindingConfirmed -and $syntheticProgress -and $bindingProgress.Count -eq $progressEvidence.Count
            }
            if ($ReadbackGate) {
                $gateInstall = @(Select-String -LiteralPath $playerLog -Pattern '\[SyntheticVideoProbe\] INSTALL ' | ForEach-Object { $_.Line })
                $gateInstallConfirmed = [bool]($gateInstall | Where-Object { $_ -match '; readbackGate=True(?:;|\s|$)' -and $_ -match '; poolSize=3(?:;|\s|$)' -and $_ -match '; readbackSupported=True(?:;|\s|$)' })
                if ($progressEvidence.Count -gt 0) {
                    foreach ($key in @('readbackRequests','readbackCompleted','readbackErrors','reuseAfterAck','illegalReuse','bindingMismatches','backpressure')) {
                        $match = [regex]::Match($progressEvidence[$progressEvidence.Count - 1], ('(?:^|\s)' + $key + '=(\d+)(?:\s|$)'))
                        if ($match.Success) {
                            $readbackCounters[$key] = [long]$match.Groups[1].Value
                        }
                    }
                }
                $gateProgressInvalid = @($progressEvidence | Where-Object { $_ -notmatch ' readbackGate=True(?:\s|;|$)' -or $_ -notmatch ' readbackErrors=0(?:\s|;|$)' -or $_ -notmatch ' illegalReuse=0(?:\s|;|$)' -or $_ -notmatch ' bindingMismatches=0(?:\s|;|$)' })
                $readbackGateConfirmed = $gateInstallConfirmed -and $syntheticProgress -and $gateProgressInvalid.Count -eq 0 -and $readbackCounters.Count -eq 7 -and $readbackCounters['readbackRequests'] -gt 0 -and $readbackCounters['readbackCompleted'] -gt 0 -and $readbackCounters['reuseAfterAck'] -gt 0
                $textureBindingConfirmed = $syntheticProgress -and @($progressEvidence | Where-Object { $_ -notmatch ' bindingMismatches=0(?:\s|;|$)' }).Count -eq 0
            } elseif ($progressEvidence -match ' readbackGate=True(?:\s|;|$)') {
                $readbackGateConfirmed = $false
            }
            if ($NativeQueueFence) {
                $nativeInstall = @(Select-String -LiteralPath $playerLog -Pattern '\[SyntheticVideoProbe\] INSTALL ' | ForEach-Object { $_.Line })
                $nativeInstallConfirmed = [bool]($nativeInstall | Where-Object { $_ -match '; nativeQueueFence=True(?:;|\s|$)' -and $_ -match '; poolSize=3(?:;|\s|$)' -and $_ -match '; nativeMarkerAvailable=True(?:;|\s|$)' })
                if ($progressEvidence.Count -gt 0) {
                    foreach ($key in @('markerRequests','markerCompleted','markerErrors','reuseAfterAck','illegalReuse','bindingMismatches','backpressure')) {
                        $match = [regex]::Match($progressEvidence[$progressEvidence.Count - 1], ('(?:^|\s)' + $key + '=(\d+)(?:\s|$)'))
                        if ($match.Success) {
                            $nativeFenceCounters[$key] = [long]$match.Groups[1].Value
                        }
                    }
                }
                $nativeProgressInvalid = @($progressEvidence | Where-Object { $_ -notmatch ' nativeQueueFence=True(?:\s|;|$)' -or $_ -notmatch ' markerErrors=0(?:\s|;|$)' -or $_ -notmatch ' illegalReuse=0(?:\s|;|$)' -or $_ -notmatch ' bindingMismatches=0(?:\s|;|$)' })
                $nativeQueueFenceConfirmed = $nativeInstallConfirmed -and $syntheticProgress -and $nativeProgressInvalid.Count -eq 0 -and $nativeFenceCounters.Count -eq 7 -and $nativeFenceCounters['markerRequests'] -gt 0 -and $nativeFenceCounters['markerCompleted'] -gt 0 -and $nativeFenceCounters['reuseAfterAck'] -gt 0
                $textureBindingConfirmed = $syntheticProgress -and @($progressEvidence | Where-Object { $_ -notmatch ' bindingMismatches=0(?:\s|;|$)' }).Count -eq 0
            } elseif ($progressEvidence -match ' nativeQueueFence=True(?:\s|;|$)') {
                $nativeQueueFenceConfirmed = $false
            }
            if ($StaticSyntheticFrame) {
                $staticUploadCountConfirmed = $syntheticProgress -and @($progressEvidence | Where-Object { $_ -notmatch ' uploads=1(?:\s|$)' }).Count -eq 0
            }
        }
        $frameSourceConfirmed = if ($FrameSource -eq 'synthetic') {
            $syntheticInstalled -and $syntheticProgress -and $syntheticModeConfirmed -and $staticUploadCountConfirmed -and $textureBindingConfirmed -and $readbackGateConfirmed -and $nativeQueueFenceConfirmed -and -not $fixtureVideoConfirmed
        } else {
            $fixtureVideoConfirmed -and $decoderConfirmed
        }
        if ($reason -eq 'result' -and (-not $graphicsConfirmed -or -not $frameSourceConfirmed)) {
            $reason = 'fixture-mismatch'
        }
        $threadingEvidence = @()
        if (Test-Path -LiteralPath $playerLog -PathType Leaf) {
            $threadingEvidence = @(Select-String -LiteralPath $playerLog -Pattern 'GfxDevice: creating device client;.*kGfxThreadingMode' | ForEach-Object { $_.Line })
        }
        $threadingConfirmed = $ExpectedThreadingMode -eq 'Any' -or [bool]($threadingEvidence -match ('kGfxThreadingMode' + $ExpectedThreadingMode + '\b'))
        $bootAfterHash = (Get-FileHash -LiteralPath $bootPath -Algorithm SHA256).Hash
        if ($reason -eq 'result' -and (-not $threadingConfirmed -or $bootAfterHash -ne $bootHash)) {
            $reason = 'threading-mode-or-boot-mismatch'
        }
        $configAfter = @{}
        foreach ($name in @('settings.json', 'system-settings.json')) {
            $configAfter[$name] = (Get-FileHash -LiteralPath (Join-Path $gameDirectory $name) -Algorithm SHA256).Hash
        }
        $artifactAfter = @{}
        foreach ($artifactName in $artifactNames) {
            $artifactPath = Join-Path $gameDirectory $artifactName
            $artifactAfter[$artifactName] = if (Test-Path -LiteralPath $artifactPath -PathType Leaf) { (Get-FileHash -LiteralPath $artifactPath -Algorithm SHA256).Hash } else { 'ABSENT' }
        }
        $configurationUnchanged = @($configHashes.Keys | Where-Object { $configHashes[$_] -ne $configAfter[$_] }).Count -eq 0
        $artifactsUnchanged = @($artifactHashes.Keys | Where-Object { $artifactHashes[$_] -ne $artifactAfter[$_] }).Count -eq 0
        if ($reason -eq 'result' -and (-not $configurationUnchanged -or -not $artifactsUnchanged)) {
            $reason = 'configuration-or-artifact-changed'
        }
        $row = [ordered]@{
            Attempt = $attempt; Pid = $(if ($null -ne $process) { $process.Id } else { $null })
            Started = $started.ToString('o'); EnteredGame = $(if ($null -ne $entered) { $entered.ToString('o') } else { $null })
            Ended = $ended.ToString('o'); StopReason = $reason; Failure = $failure
            ExitCode = $(if ($null -ne $process -and $process.HasExited) { $process.ExitCode } else { $null })
            Driver153Count = $events.Count; ModulesCaptured = $moduleCaptured; CleanupStopped = $cleanupStopped
            EngineFailureInLog = [bool]$engineFailure
            ExpectedThreadingMode = $ExpectedThreadingMode; ThreadingModeConfirmed = $threadingConfirmed; ThreadingEvidence = $threadingEvidence
            BootConfigBeforeSHA256 = $bootHash; BootConfigAfterSHA256 = $bootAfterHash
            NavigationActivationAttempts = $activationAttempts; NavigationForegroundMatches = $foregroundMatches; NavigationKeysSent = $keysSent
            RespondingAfterDriverEvent = $respondingAfterDriverEvent; AliveAfterDriverObservation = $aliveAfterDriverObservation
            FrameSource = $FrameSource; ExpectedDecoder = $ExpectedDecoder; FrameSourceConfirmed = [bool]$frameSourceConfirmed
            StaticSyntheticFrame = [bool]$StaticSyntheticFrame; SyntheticModeConfirmed = $syntheticModeConfirmed; StaticUploadCountConfirmed = $staticUploadCountConfirmed
            DecoupleSyntheticUpload = [bool]$DecoupleSyntheticUpload; PairedTextureModePresent = $pairedTextureModePresent; TextureBindingConfirmed = $textureBindingConfirmed
            ReadbackGate = [bool]$ReadbackGate; ReadbackGateConfirmed = $readbackGateConfirmed; ReadbackCounters = $readbackCounters
            NativeQueueFence = [bool]$NativeQueueFence; NativeQueueFenceConfirmed = $nativeQueueFenceConfirmed; NativeFenceCounters = $nativeFenceCounters
            SyntheticInstalled = $syntheticInstalled; SyntheticProgress = $syntheticProgress; DecoderConfirmed = $decoderConfirmed
            GraphicsConfirmed = $graphicsConfirmed; FixtureVideoConfirmed = $fixtureVideoConfirmed
            HardwarePrepared = $hardwarePrepared
            NativeGpuTransfer = [bool]($transferMatches -match 'no CPU readback|zero-copy')
            GraphicsEvidence = $graphicsMatches; FFmpegEvidence = $transferMatches
            ConfigBeforeSHA256 = $configHashes; ConfigAfterSHA256 = $configAfter; ConfigurationUnchanged = $configurationUnchanged
            ArtifactBeforeSHA256 = $artifactHashes; ArtifactAfterSHA256 = $artifactAfter; ArtifactsUnchanged = $artifactsUnchanged
        }
        $row | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath (Join-Path $case 'result.json') -Encoding utf8
        $rows.Add([pscustomobject]$row)
        $rows | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath (Join-Path $output 'results.json') -Encoding utf8
        Write-Output ("MAIN END attempt={0} reason={1} driver153={2} modules={3}" -f $attempt, $reason, $events.Count, $moduleCaptured)
    }
    if ($events.Count -gt 0 -or $reason -ne 'result') {
        Write-Output "Stopping after the first failing or invalid case. Evidence: $output"
        exit 1
    }
}
Write-Output "MAIN COMPLETE: $output"
