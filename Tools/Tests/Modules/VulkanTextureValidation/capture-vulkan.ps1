param(
    [switch] $Run,
    [switch] $NativeQueueFence,
    [string] $PlayerDirectory = '',
    [string] $SdkBin = 'E:/_Game/majp-test/vkdiag/sdk/Bin',
    [string] $OutputDirectory = '',
    [ValidateRange(0.25, 20)][double] $MaxOutputGB = 6,
    [ValidateRange(30, 500)][int] $ReserveGB = 30,
    [ValidateRange(10, 240)][int] $SecondsInGame = 200,
    [ValidateRange(30, 360)][int] $MaxTotalSeconds = 360
)
$ErrorActionPreference = 'Stop'
$module = (Resolve-Path -LiteralPath $PSScriptRoot).Path
$work = [IO.Path]::GetFullPath((Join-Path $module '.work')).TrimEnd('\')
if (-not $PlayerDirectory) { $PlayerDirectory = Join-Path $work 'MainSynthetic' }
$player = (Resolve-Path -LiteralPath $PlayerDirectory).Path.TrimEnd('\')
if (-not $player.StartsWith($work + '\', [StringComparison]::OrdinalIgnoreCase)) {
    throw 'Only an isolated Player below this module .work directory is accepted.'
}
$game = (Get-Item -LiteralPath (Join-Path $player 'MajdataPlay.exe')).FullName
$runner = (Get-Item -LiteralPath (Join-Path $module 'run-main.ps1')).FullName
$bin = (Resolve-Path -LiteralPath $SdkBin).Path
$manifestPath = (Get-Item -LiteralPath (Join-Path $bin 'VkLayer_gfxreconstruct.json')).FullName
$layerManifest = Get-Content -LiteralPath $manifestPath -Raw | ConvertFrom-Json
if ($layerManifest.layer.name -ne 'VK_LAYER_LUNARG_gfxreconstruct') { throw 'Unexpected capture layer manifest.' }
$layer = (Get-Item -LiteralPath (Join-Path $bin $layerManifest.layer.library_path)).FullName
$infoTool = (Get-Item -LiteralPath (Join-Path $bin 'gfxrecon-info.exe')).FullName
if (-not $OutputDirectory) {
    $OutputDirectory = Join-Path $work ('capture-startup-' + (Get-Date -Format 'yyyyMMdd-HHmmss') + '-' + [guid]::NewGuid().ToString('N').Substring(0, 8))
}
$output = [IO.Path]::GetFullPath($OutputDirectory).TrimEnd('\')
if (-not $output.StartsWith($work + '\', [StringComparison]::OrdinalIgnoreCase)) { throw 'Capture output must remain under this module .work.' }
if (Test-Path -LiteralPath $output) { throw 'Use a new output directory; previous evidence is never overwritten.' }
foreach ($target in @($player, $output, $bin, $runner)) {
    if ($target -match '[\r\n"]') { throw 'Paths containing quotes or newlines are unsupported.' }
    for ($ancestor = $target; $ancestor; $ancestor = Split-Path -Parent $ancestor) {
        if ((Test-Path -LiteralPath $ancestor) -and ((Get-Item -LiteralPath $ancestor -Force).Attributes -band [IO.FileAttributes]::ReparsePoint)) {
            throw "Refusing a path through a reparse point: $ancestor"
        }
    }
}
$expected = @{
    'MajdataPlay_Data/Managed/MajdataPlay.Core.dll' = '84BFFF325022DE2E86CE2B5757FBEF9BDB159C70FBAEE36DD9CEB2525AB423FF'
    'MajdataPlay_Data/Managed/MajdataPlay.FFmpeg.dll' = '1505F81968C01E3D91172AC14213AB1FACCA7A3654541FA629E0112D19EDF6B5'
    'MajdataPlay_Data/Managed/MajdataPlay.MajRenderPipe.dll' = '7F30CBC5C3945FC03EEBD4DA2DF9FDEA2F5024FD56DE1FA77C4AA81051C5DF60'
    'MajdataPlay_Data/Managed/MajdataPlay.SyntheticFrameProbe.dll' = '269781F9A748152003F07B267C379CBA002CE7A51DE715AD9429D555FDEB5313'
    'MajdataPlay_Data/Plugins/x86_64/MajdataPlayVulkanPacing.dll' = 'A8827B4468A2A4A1CEA534D362406016BCE0D0FE14C4A71131D402751961BCDF'
}
$artifactHashes = @{}
foreach ($relative in $expected.Keys) {
    $hash = (Get-FileHash -LiteralPath (Join-Path $player $relative) -Algorithm SHA256).Hash
    if ($hash -ne $expected[$relative]) { throw "The prepared capture baseline changed: $relative" }
    $artifactHashes[$relative] = $hash
}
if (Test-Path -LiteralPath (Join-Path $player 'MajdataPlay_Data/Plugins/x86_64/FFmpegUnityBridge.dll')) { throw 'The FFmpeg native bridge must remain absent.' }
foreach ($relative in @('MajdataPlay.exe', 'UnityPlayer.dll', 'MajdataPlay_Data/boot.config', 'settings.json', 'system-settings.json')) {
    $artifactHashes[$relative] = (Get-FileHash -LiteralPath (Join-Path $player $relative) -Algorithm SHA256).Hash
}
$running = @(Get-Process MajdataPlay -ErrorAction SilentlyContinue | Where-Object { $_.Path -eq $game })
if ($running.Count) { throw 'The isolated Player is already running.' }
New-Item -ItemType Directory -Path $output | Out-Null
$captureDirectory = Join-Path $output 'capture'
New-Item -ItemType Directory -Path $captureDirectory | Out-Null
$layerSettingsDirectory = Join-Path $output 'layer-settings'
New-Item -ItemType Directory -Path $layerSettingsDirectory | Out-Null
Set-Content -LiteralPath (Join-Path $layerSettingsDirectory 'vk_layer_settings.txt') -Value '# Intentionally empty. Capture options are provided by the child environment.' -Encoding ascii
$driveName = (Get-Item -LiteralPath $output).PSDrive.Name
$limit = [long]($MaxOutputGB * 1GB)
$reserve = [long]$ReserveGB * 1GB
$initialFree = (Get-PSDrive -Name $driveName).Free
if ($initialFree -lt ($reserve + $limit + 1GB)) { throw 'Insufficient free space for output budget, reserve, and one GiB polling allowance.' }
$childEnvironment = [ordered]@{
    VK_LAYER_PATH = $bin
    VK_INSTANCE_LAYERS = 'VK_LAYER_LUNARG_gfxreconstruct'
    VK_LAYER_SETTINGS_PATH = $layerSettingsDirectory
    VK_LOADER_DEBUG = 'error,warn,layer'
    GFXRECON_CAPTURE_FILE = (Join-Path $captureDirectory 'startup.gfxr')
    GFXRECON_CAPTURE_FILE_TIMESTAMP = 'false'
    GFXRECON_CAPTURE_COMPRESSION_TYPE = 'LZ4'
    GFXRECON_CAPTURE_FILE_FLUSH = 'true'
    GFXRECON_MEMORY_TRACKING_MODE = 'page_guard'
    GFXRECON_PAGE_GUARD_EXTERNAL_MEMORY = 'false'
    GFXRECON_LOG_LEVEL = 'info'
    GFXRECON_LOG_FILE = (Join-Path $output 'capture-layer.log')
    GFXRECON_LOG_FILE_FLUSH_AFTER_WRITE = 'true'
    GFXRECON_LOG_OUTPUT_TO_CONSOLE = 'true'
    GFXRECON_CAPTURE_PROCESS_NAME = 'MajdataPlay.exe'
}
# Reset potentially inherited capture/trigger/loader overrides only in the new wrapper environment.
$removeEnvironment = @('GFXRECON_DISABLE', 'GFXRECON_CAPTURE_FRAMES', 'GFXRECON_CAPTURE_TRIGGER', 'GFXRECON_CAPTURE_TRIGGER_FRAMES',
    'GFXRECON_CAPTURE_ENVIRONMENT', 'VK_ADD_LAYER_PATH', 'VK_ADD_IMPLICIT_LAYER_PATH', 'VK_ADD_INSTANCE_LAYERS',
    'VK_IMPLICIT_LAYER_PATH', 'VK_LOADER_LAYERS_ENABLE', 'VK_LOADER_LAYERS_DISABLE', 'VK_LAYER_SETTINGS_PATH')
$supervisorHash = (Get-FileHash -LiteralPath $PSCommandPath -Algorithm SHA256).Hash
$planned = [ordered]@{
    PreparedAt = (Get-Date).ToString('o'); RunRequested = [bool]$Run; Runner = $runner; Player = $game
    SupervisorSHA256 = $supervisorHash
    Baseline = $(if ($NativeQueueFence) { 'sampled with native mf_* queue fence' } else { 'ordinary sampled; no readback/native completion gate' })
    NativeMarkerIdentity = 'Diagnostic mf_* DLL under existing loader filename; not the original pacing layer'
    ArtifactSHA256 = $artifactHashes; LayerManifest = $manifestPath; LayerLibrary = $layer
    LayerSHA256 = (Get-FileHash -LiteralPath $layer -Algorithm SHA256).Hash
    InfoSHA256 = (Get-FileHash -LiteralPath $infoTool -Algorithm SHA256).Hash
    ChildEnvironment = $childEnvironment; RemovedChildEnvironment = $removeEnvironment
    OutputBudgetBytes = $limit; ReserveBytes = $reserve; InitialFreeBytes = $initialFree
    SecondsInGame = $SecondsInGame; TotalTimeoutSeconds = $MaxTotalSeconds; PollMilliseconds = 250
    BudgetSources = @('Visible lengths of all new output files', 'Net output-volume free-space decrease since preflight', 'Absolute free-space reserve')
    BudgetBoundary = 'Soft polling guards; active-writer directory lengths can be stale and writes can overshoot; no old files deleted'
    DiskConsumptionScope = 'Net free-space change on the output volume; not capture or process attribution; other writes can cause false positives and releases can mask growth'
    DirectoryLengthsMayBeStale = $true
    GPUExecutionPerformed = $false; ReplayPermittedByThisScript = $false
    DesktopVideoControllers = @(Get-CimInstance Win32_VideoController | Select-Object Name,DriverVersion,CurrentHorizontalResolution,CurrentVerticalResolution,CurrentRefreshRate)
    DesktopStateIsNotPlayerPresentMeasurement = $true
    CpuInfoTool = $infoTool
}
$planned | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath (Join-Path $output 'capture-plan.json') -Encoding utf8
if (-not $Run) {
    Write-Output "PLAN ONLY: $output"
    Write-Output 'No wrapper, Player, capture layer, Unity or Replay was started. Add -Run only for the controlled capture step.'
    return
}

function Get-OwnedPlayer {
    param([int] $WrapperId, [datetime] $Started)
    $matches = @(Get-CimInstance Win32_Process -Filter ("ParentProcessId=" + $WrapperId) -ErrorAction Stop | Where-Object {
        $_.ExecutablePath -and $_.ExecutablePath.Equals($game, [StringComparison]::OrdinalIgnoreCase) -and
        $_.CreationDate -ge $Started.AddSeconds(-2)
    })
    if ($matches.Count -gt 1) { throw 'More than one direct child matched the single-attempt capture; refusing ambiguous ownership.' }
    if ($matches.Count -eq 1) { return $matches[0] }
    return $null
}
function Stop-OwnedPlayer {
    param($Owned)
    if ($null -eq $Owned) { return }
    $fresh = Get-CimInstance Win32_Process -Filter ("ProcessId=" + $Owned.ProcessId) -ErrorAction SilentlyContinue
    if ($null -eq $fresh) { return }
    if ($fresh.ParentProcessId -ne $Owned.ParentProcessId -or $fresh.CreationDate -ne $Owned.CreationDate -or
        -not $fresh.ExecutablePath -or -not $fresh.ExecutablePath.Equals($game, [StringComparison]::OrdinalIgnoreCase)) {
        throw 'Child identity changed; refusing to terminate an unverified process.'
    }
    $child = Get-Process -Id $Owned.ProcessId -ErrorAction Stop
    if (-not $child.Path.Equals($game, [StringComparison]::OrdinalIgnoreCase) -or
        [math]::Abs(($child.StartTime - $Owned.CreationDate).TotalSeconds) -gt 1) { throw 'Second child identity check failed.' }
    # Pin a handle to this process instance before termination, rather than name-based termination.
    [void]$child.Handle
    if (-not $child.HasExited) { $child.Kill(); [void]$child.WaitForExit(5000) }
}
function Get-OutputBytes {
    $files = @(Get-ChildItem -LiteralPath $output -Recurse -File -Force)
    if (@($files | Where-Object { $_.Attributes -band [IO.FileAttributes]::ReparsePoint }).Count) { throw 'Unexpected output reparse point.' }
    $sum = ($files | Measure-Object Length -Sum).Sum
    return [long]$(if ($null -eq $sum) { 0 } else { $sum })
}

$wrapper = $null
$owned = $null
$outTask = $null
$errTask = $null
$outStream = $null
$errStream = $null
$stdoutPath = Join-Path $output 'runner-stdout.txt'
$stderrPath = Join-Path $output 'runner-stderr.txt'
$stdoutPidConfirmed = $false
$stopReason = 'wrapper-exited'
$failure = $null
$layerLoaded = $false
$started = Get-Date
$moduleRows = @()
$peakOutput = 0L
$peakDiskConsumption = 0L
$minimumFree = $initialFree
try {
    $hostPath = (Get-Process -Id $PID).Path
    if ([IO.Path]::GetFileName($hostPath) -notin @('powershell.exe', 'pwsh.exe')) { throw 'Use a console PowerShell host.' }
    $arguments = @('-NoLogo', '-NoProfile', '-NonInteractive', '-ExecutionPolicy', 'Bypass', '-File', ('"' + $runner + '"'),
        '-Attempts', '1', '-SecondsInGame', "$SecondsInGame", '-Graphics', 'vulkan', '-FrameSource', 'synthetic',
        '-DisableObsCapture', '-ExpectedThreadingMode', 'SplitJobs', '-DriverObservationSeconds', '5',
        '-PlayerDirectory', ('"' + $player + '"'), '-OutputDirectory', ('"' + (Join-Path $output 'run') + '"'))
    if ($NativeQueueFence) { $arguments += '-NativeQueueFence' }
    $startInfo = New-Object System.Diagnostics.ProcessStartInfo
    $startInfo.FileName = $hostPath
    $startInfo.WorkingDirectory = $module
    $startInfo.Arguments = $arguments -join ' '
    $startInfo.UseShellExecute = $false
    $startInfo.RedirectStandardOutput = $true
    $startInfo.RedirectStandardError = $true
    foreach ($key in @($startInfo.EnvironmentVariables.Keys | Where-Object { $_ -like 'GFXRECON_*' })) { $startInfo.EnvironmentVariables.Remove($key) }
    foreach ($key in $removeEnvironment) { $startInfo.EnvironmentVariables.Remove($key) }
    foreach ($key in $childEnvironment.Keys) { $startInfo.EnvironmentVariables[$key] = [string]$childEnvironment[$key] }
    $wrapper = New-Object System.Diagnostics.Process
    $wrapper.StartInfo = $startInfo
    if (-not $wrapper.Start()) { throw 'The capture wrapper did not start.' }
    $outStream = [IO.FileStream]::new($stdoutPath, [IO.FileMode]::CreateNew, [IO.FileAccess]::Write, [IO.FileShare]::ReadWrite, 1, [IO.FileOptions]::Asynchronous)
    $errStream = [IO.FileStream]::new($stderrPath, [IO.FileMode]::CreateNew, [IO.FileAccess]::Write, [IO.FileShare]::ReadWrite, 1, [IO.FileOptions]::Asynchronous)
    $outTask = $wrapper.StandardOutput.BaseStream.CopyToAsync($outStream)
    $errTask = $wrapper.StandardError.BaseStream.CopyToAsync($errStream)
    [ordered]@{WrapperPid=$wrapper.Id;Started=$started.ToString('o');Arguments=$arguments} | ConvertTo-Json -Depth 4 |
        Set-Content -LiteralPath (Join-Path $output 'wrapper-launch.json') -Encoding utf8
    while (-not $wrapper.HasExited) {
        if ($null -eq $owned) {
            $owned = Get-OwnedPlayer -WrapperId $wrapper.Id -Started $started
            if ($null -ne $owned) {
                [ordered]@{Pid=$owned.ProcessId;ParentPid=$owned.ParentProcessId;CreationTime=$owned.CreationDate.ToString('o');Path=$owned.ExecutablePath} |
                    ConvertTo-Json | Set-Content -LiteralPath (Join-Path $output 'owned-child.json') -Encoding utf8
            }
        }
        if ($null -ne $owned -and -not $stdoutPidConfirmed -and (Test-Path -LiteralPath $stdoutPath)) {
            $startLine = Select-String -LiteralPath $stdoutPath -Pattern '^MAIN START attempt=1 pid=(\d+)\b' | Select-Object -First 1
            if ($null -ne $startLine) {
                $stdoutPidConfirmed = [long]$startLine.Matches[0].Groups[1].Value -eq [long]$owned.ProcessId
                if (-not $stdoutPidConfirmed) { $stopReason = 'child-pid-evidence-mismatch'; break }
            }
        }
        if (((Get-Date) - $started).TotalSeconds -ge 15 -and ($null -eq $owned -or -not $stdoutPidConfirmed)) {
            $stopReason = 'unverified-child'
            break
        }
        if ($null -ne $owned -and $stdoutPidConfirmed -and -not $layerLoaded) {
            try {
                $observed = Get-Process -Id $owned.ProcessId -ErrorAction Stop
                if ($observed.Path.Equals($game, [StringComparison]::OrdinalIgnoreCase)) {
                    $moduleRows = @($observed.Modules | Select-Object ModuleName,FileName)
                    $layerLoaded = @($moduleRows | Where-Object { $_.ModuleName -eq 'VkLayer_gfxreconstruct.dll' -and $_.FileName.Equals($layer, [StringComparison]::OrdinalIgnoreCase) }).Count -eq 1
                    if ($layerLoaded) { $moduleRows | Export-Csv -LiteralPath (Join-Path $output 'capture-modules.csv') -NoTypeInformation -Encoding utf8 }
                }
            } catch { }
        }
        $bytes = Get-OutputBytes
        $free = (Get-PSDrive -Name $driveName).Free
        $diskConsumption = [long]$(if ($free -lt $initialFree) { $initialFree - $free } else { 0 })
        if ($bytes -gt $peakOutput) { $peakOutput = $bytes }
        if ($diskConsumption -gt $peakDiskConsumption) { $peakDiskConsumption = $diskConsumption }
        if ($free -lt $minimumFree) { $minimumFree = $free }
        if ($bytes -ge $limit) { $stopReason = 'output-budget'; break }
        # This is a conservative volume-level proxy, not bytes attributable to the capture process.
        if ($diskConsumption -ge $limit) { $stopReason = 'volume-consumption-budget'; break }
        if ($free -lt $reserve) { $stopReason = 'free-space-reserve'; break }
        if (((Get-Date) - $started).TotalSeconds -ge $MaxTotalSeconds) { $stopReason = 'total-timeout'; break }
        if (((Get-Date) - $started).TotalSeconds -ge 40 -and -not $layerLoaded) { $stopReason = 'capture-layer-unconfirmed'; break }
        Start-Sleep -Milliseconds 250
        $wrapper.Refresh()
    }
} catch {
    $failure = $_.Exception.Message
    $stopReason = 'supervisor-failed'
} finally {
    # Stop only this wrapper first, so it cannot create another Player while limits are handled.
    if ($null -ne $wrapper) {
        if (-not $wrapper.HasExited) { $wrapper.Kill(); [void]$wrapper.WaitForExit(5000) }
        try {
            if ($null -eq $owned) { $owned = Get-OwnedPlayer -WrapperId $wrapper.Id -Started $started }
            Stop-OwnedPlayer -Owned $owned
        } catch {
            $failure = 'Cleanup refused: ' + $_.Exception.Message
            $stopReason = 'cleanup-identity-refused'
        }
        try { if ($null -ne $outTask) { [void]$outTask.Wait(5000) } } catch { $failure = 'Output drain: ' + $_.Exception.Message }
        try { if ($null -ne $errTask) { [void]$errTask.Wait(5000) } } catch { $failure = 'Error drain: ' + $_.Exception.Message }
    }
    if ($null -ne $outStream) { $outStream.Dispose() }
    if ($null -ne $errStream) { $errStream.Dispose() }
}
$ended = Get-Date
$captures = @(Get-ChildItem -LiteralPath $captureDirectory -File -Filter '*.gfxr')
$infoRecords = @()
foreach ($capture in $captures) {
    $info = New-Object System.Diagnostics.ProcessStartInfo
    $info.FileName = $infoTool
    $info.Arguments = '"' + $capture.FullName + '"'
    $info.UseShellExecute = $false; $info.RedirectStandardOutput = $true; $info.RedirectStandardError = $true
    $cpu = [Diagnostics.Process]::Start($info)
    $cpuOut = $cpu.StandardOutput.ReadToEndAsync(); $cpuErr = $cpu.StandardError.ReadToEndAsync()
    $timedOut = -not $cpu.WaitForExit(60000)
    if ($timedOut) { $cpu.Kill(); [void]$cpu.WaitForExit(5000) }
    $text = $(if ($cpuOut.Wait(5000)) { $cpuOut.Result } else { '' })
    $errorText = $(if ($cpuErr.Wait(5000)) { $cpuErr.Result } else { '' })
    $text | Set-Content -LiteralPath (Join-Path $output ($capture.Name + '.info.txt')) -Encoding utf8
    $errorText | Set-Content -LiteralPath (Join-Path $output ($capture.Name + '.info-stderr.txt')) -Encoding utf8
    $frameMatch = [regex]::Match($text, 'Total frames:\s*(\d+)')
    $infoRecords += [pscustomobject]@{Path=$capture.FullName;Bytes=$capture.Length;InfoExitCode=$cpu.ExitCode;InfoTimedOut=$timedOut;Frames=$(if($frameMatch.Success){[long]$frameMatch.Groups[1].Value}else{0});Trimmed=($text -match 'trimmed frame range')}
}
$events = @(Get-WinEvent -FilterHashtable @{LogName='System';ProviderName='nvlddmkm';Id=153;StartTime=$started;EndTime=$ended} -ErrorAction SilentlyContinue)
$events | ForEach-Object {$_.ToXml()} | Set-Content -LiteralPath (Join-Path $output 'driver-events.xml') -Encoding utf8
$infoValid = $infoRecords.Count -gt 0 -and @($infoRecords | Where-Object {$_.InfoTimedOut -or $_.InfoExitCode -ne 0 -or $_.Frames -le 0 -or $_.Trimmed}).Count -eq 0
$caseResultPath = Join-Path $output 'run/attempt-1/result.json'
$caseResult = if (Test-Path -LiteralPath $caseResultPath) { Get-Content -LiteralPath $caseResultPath -Raw | ConvertFrom-Json } else { $null }
$caseValid = $null -ne $caseResult -and $caseResult.FrameSourceConfirmed -and $caseResult.GraphicsConfirmed -and
    $caseResult.StopReason -in @('result', 'driver-153', 'engine-device-lost-or-crash', 'device-lost')
$finalFree = (Get-PSDrive -Name $driveName).Free
$finalDiskConsumption = [long]$(if ($finalFree -lt $initialFree) { $initialFree - $finalFree } else { 0 })
if ($finalDiskConsumption -gt $peakDiskConsumption) { $peakDiskConsumption = $finalDiskConsumption }
if ($finalFree -lt $minimumFree) { $minimumFree = $finalFree }
$result = [ordered]@{
    Started=$started.ToString('o');Ended=$ended.ToString('o');StopReason=$stopReason;Failure=$failure
    SupervisorSHA256=$supervisorHash
    WrapperPid=$(if($wrapper){$wrapper.Id}else{$null});ChildPid=$(if($owned){$owned.ProcessId}else{$null})
    LayerModuleConfirmed=$layerLoaded;StdoutChildPidConfirmed=$stdoutPidConfirmed;CaptureFiles=$infoRecords;UntrimmedInfoConfirmed=$infoValid
    CaseOutcome=$(if($caseResult){$caseResult.StopReason}else{'missing-result'});CaseEvidenceValid=[bool]$caseValid
    OutputBudgetBytes=$limit;PeakObservedOutputBytes=$peakOutput;FinalOutputBytes=(Get-OutputBytes);MinimumObservedFreeBytes=$minimumFree
    InitialFreeBytes=$initialFree;FinalObservedFreeBytes=$finalFree;FinalObservedDiskConsumptionBytes=$finalDiskConsumption
    PeakObservedDiskConsumptionBytes=$peakDiskConsumption;DiskConsumptionScope=$planned.DiskConsumptionScope
    BudgetSources=$planned.BudgetSources;DirectoryLengthsMayBeStale=$true
    Driver153Messages=$events.Count;DriverEventsAreNotIndependentFailures=$true
    CaptureValid=($stopReason -eq 'wrapper-exited' -and -not $failure -and $stdoutPidConfirmed -and $layerLoaded -and $infoValid -and $caseValid)
    TerminationMayTruncateCapture=$true;UnverifiedChildCleanupNotAttempted=($null -eq $owned);ReplayPerformed=$false
}
$result | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath (Join-Path $output 'capture-result.json') -Encoding utf8
Write-Output ("CAPTURE SUPERVISOR COMPLETE: " + $output)
Write-Output ($result | ConvertTo-Json -Depth 8)
if (-not $result.CaptureValid) { exit 1 }
