param(
    [Parameter(Mandatory = $true)][string] $Capture,
    [Parameter(Mandatory = $true)][string] $OutputDirectory,
    [string] $SdkBin = 'E:/_Game/majp-test/vkdiag/sdk/Bin',
    [ValidateSet('captured','virtual')][string] $SwapchainMode = 'virtual',
    [switch] $MatchApplicationName,
    [switch] $Validate,
    [switch] $SkipVirtualSwapchainBlit,
    [ValidateSet('Off', 'Passthrough', 'Narrow', 'Broad')][string] $SyncLayer = 'Off',
    [ValidateRange(10, 300)][int] $TimeoutSeconds = 240,
    [ValidateRange(16, 512)][int] $MaxLogMB = 256
)
$ErrorActionPreference = 'Stop'
$module = (Resolve-Path -LiteralPath $PSScriptRoot).Path
$workPrefix = [IO.Path]::GetFullPath((Join-Path $module '.work')).TrimEnd('\') + '\'
$capturePath = (Resolve-Path -LiteralPath $Capture).Path
$output = [IO.Path]::GetFullPath($OutputDirectory)
if (-not $capturePath.StartsWith($workPrefix, [StringComparison]::OrdinalIgnoreCase) -or
    -not $output.StartsWith($workPrefix, [StringComparison]::OrdinalIgnoreCase)) {
    throw 'Capture and replay output must remain under this module .work.'
}
if ((Test-Path -LiteralPath $output) -or $capturePath -match '[\r\n"]' -or $output -match '[\r\n"]') {
    throw 'Use a new output directory and ordinary quoted paths without embedded quotes or newlines.'
}
$sourceTool = (Get-Item -LiteralPath (Join-Path $SdkBin 'gfxrecon-replay.exe')).FullName
$tool = $sourceTool
New-Item -ItemType Directory -Path $output | Out-Null
if ($MatchApplicationName) {
    $toolDirectory = Join-Path $output 'replayer'
    New-Item -ItemType Directory -Path $toolDirectory | Out-Null
    $tool = Join-Path $toolDirectory 'MajdataPlay.exe'
    Copy-Item -LiteralPath $sourceTool -Destination $tool
    if ((Get-FileHash -LiteralPath $tool -Algorithm SHA256).Hash -ne (Get-FileHash -LiteralPath $sourceTool -Algorithm SHA256).Hash) {
        throw 'The application-name replay copy differs from the original tool.'
    }
}
$initialFree = (Get-PSDrive -Name ((Get-Item -LiteralPath $output).PSDrive.Name)).Free
$settings = Join-Path $output 'layer-settings'
New-Item -ItemType Directory -Path $settings | Out-Null
$layerSettingsPath = Join-Path $settings 'vk_layer_settings.txt'
if ($Validate) {
    Set-Content -LiteralPath $layerSettingsPath -Value @('khronos_validation.validate_core = true','khronos_validation.validate_sync = true','khronos_validation.syncval_full_validation = true') -Encoding ascii
} else {
    Set-Content -LiteralPath $layerSettingsPath -Value '# Empty replay layer settings.' -Encoding ascii
}
$stdout = Join-Path $output 'stdout.txt'
$stderr = Join-Path $output 'stderr.txt'
$replayLog = Join-Path $output 'replay.log'
$measurements = Join-Path $output 'measurements.json'
$arguments = @('--swapchain', $SwapchainMode, '--present-mode', 'capture', '--log-level', 'info', '--log-timestamps',
    '--log-file', ('"' + $replayLog + '"'), '--measurement-file', ('"' + $measurements + '"'), ('"' + $capturePath + '"'))
if ($Validate) { $arguments += @('--validate','--debug-messenger-level','info','--annotate-injected-commands') }
if ($SkipVirtualSwapchainBlit) { $arguments += @('--vssb') }
$launch = New-Object Diagnostics.ProcessStartInfo
$launch.FileName = $tool
$launch.WorkingDirectory = $output
$launch.Arguments = $arguments -join ' '
$launch.UseShellExecute = $false
$launch.RedirectStandardOutput = $true
$launch.RedirectStandardError = $true
foreach ($key in @($launch.EnvironmentVariables.Keys | Where-Object { $_ -like 'GFXRECON_*' -or $_ -like 'VK_LAYER*' -or $_ -like 'VK_ADD_*' -or $_ -like 'VK_INSTANCE_LAYERS' -or $_ -like 'VK_IMPLICIT_LAYER_PATH' })) {
    $launch.EnvironmentVariables.Remove($key)
}
$launch.EnvironmentVariables['GFXRECON_DISABLE'] = '1'
$launch.EnvironmentVariables['DISABLE_VULKAN_OBS_CAPTURE'] = '1'
$launch.EnvironmentVariables['VK_LAYER_SETTINGS_PATH'] = $settings
$launch.EnvironmentVariables['PATH'] = $SdkBin + ';' + $launch.EnvironmentVariables['PATH']
if ($Validate) {
    $launch.EnvironmentVariables['VK_LAYER_PATH'] = $SdkBin
    $launch.EnvironmentVariables['VK_LOADER_DEBUG'] = 'error,warn,layer'
}
$syncLayerDirectory = Join-Path $PSScriptRoot 'SyntheticFrameProbe/native-sync-layer/.build/Release'
if ($SyncLayer -ne 'Off') {
    if (-not (Test-Path -LiteralPath (Join-Path $syncLayerDirectory 'VkLayer_majdataplay_sync.dll') -PathType Leaf)) {
        throw 'The diagnostic synchronization layer build output is missing.'
    }
    $layerMode = switch ($SyncLayer) { 'Passthrough' { '0' } 'Narrow' { '1' } 'Broad' { '2' } }
    $launch.EnvironmentVariables['MAJ_SYNC_LAYER_MODE'] = $layerMode
    $launch.EnvironmentVariables['VK_INSTANCE_LAYERS'] = 'VK_LAYER_MAJDATAPLAY_SYNC'
    $launch.EnvironmentVariables['VK_LAYER_PATH'] = if ($Validate) { $syncLayerDirectory + ';' + $SdkBin } else { $syncLayerDirectory }
    $launch.EnvironmentVariables['VK_LOADER_DEBUG'] = 'error,warn,layer'
}
$started = Get-Date
[ordered]@{
    Started=$started.ToString('o');Executable=$tool;ToolSHA256=(Get-FileHash -LiteralPath $tool -Algorithm SHA256).Hash
    Capture=$capturePath;CaptureSHA256=(Get-FileHash -LiteralPath $capturePath -Algorithm SHA256).Hash
    Arguments=$arguments;TimeoutSeconds=$TimeoutSeconds;MaxLogBytes=([long]$MaxLogMB*1MB)
    CaptureDisabled=$true;ObsDisabled=$true;ValidationRequested=[bool]$Validate;SyncValidationRequested=[bool]$Validate;GpuAssistedValidationRequested=$false
    LayerSettingsSHA256=(Get-FileHash -LiteralPath $layerSettingsPath -Algorithm SHA256).Hash
    SourceTool=$sourceTool;ApplicationNameMatched=[bool]$MatchApplicationName;RuntimeSearchDirectory=$SdkBin
    SynchronizationInjectionRequested=($SyncLayer -ne 'Off');SyncLayer=$SyncLayer;SwapchainMode=$SwapchainMode;SkipVirtualSwapchainBlit=[bool]$SkipVirtualSwapchainBlit;NoInputAutomation=$true;InitialFreeBytes=$initialFree
} | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath (Join-Path $output 'launch.json') -Encoding utf8
$process=$null; $outStream=$null; $errStream=$null; $outTask=$null; $errTask=$null
$stopReason='exited'; $cleanupStopped=$false; $modulesCaptured=$false; $responding=$null; $failure=$null; $validationModuleConfirmed=$false
try {
    $process=New-Object Diagnostics.Process
    $process.StartInfo=$launch
    if(-not $process.Start()){throw 'Replay did not start.'}
    $outStream=[IO.FileStream]::new($stdout,[IO.FileMode]::CreateNew,[IO.FileAccess]::Write,[IO.FileShare]::ReadWrite,1,[IO.FileOptions]::Asynchronous)
    $errStream=[IO.FileStream]::new($stderr,[IO.FileMode]::CreateNew,[IO.FileAccess]::Write,[IO.FileShare]::ReadWrite,1,[IO.FileOptions]::Asynchronous)
    $outTask=$process.StandardOutput.BaseStream.CopyToAsync($outStream)
    $errTask=$process.StandardError.BaseStream.CopyToAsync($errStream)
    Write-Output ("REPLAY START pid={0} time={1:o}" -f $process.Id,$started)
    while(-not $process.HasExited){
        Start-Sleep -Milliseconds 500
        if(-not $modulesCaptured -and ((Get-Date)-$started).TotalSeconds -ge 3){
            try {
                if($process.Path -ne $tool){throw 'Unexpected replay path.'}
                $moduleRows = @($process.Modules | Select-Object ModuleName,FileName)
                $moduleRows | Export-Csv -LiteralPath (Join-Path $output 'modules.csv') -NoTypeInformation -Encoding utf8
                $validationModuleConfirmed = @($moduleRows | Where-Object { $_.ModuleName -eq 'VkLayer_khronos_validation.dll' -and $_.FileName.Equals((Join-Path $SdkBin 'VkLayer_khronos_validation.dll'),[StringComparison]::OrdinalIgnoreCase) }).Count -eq 1
                $modulesCaptured=$true
            } catch { }
        }
        if ($SwapchainMode -eq 'captured' -and (Test-Path -LiteralPath $replayLog -PathType Leaf) -and
            (Select-String -LiteralPath $replayLog -Pattern 'image index returned by vkAcquireNextImageKHR is different' -Quiet)) {
            $stopReason='swapchain-index-mismatch'
            break
        }
        $events=@(Get-WinEvent -FilterHashtable @{LogName='System';ProviderName='nvlddmkm';Id=153;StartTime=$started} -ErrorAction SilentlyContinue)
        if($events.Count){$stopReason='driver-153';$process.Refresh();if(-not $process.HasExited){$responding=$process.Responding};break}
        if(((Get-Date)-$started).TotalSeconds -ge $TimeoutSeconds){$stopReason='timeout';break}
        $bytes=(Get-ChildItem -LiteralPath $output -File -Recurse | Measure-Object Length -Sum).Sum
        if($bytes -ge ([long]$MaxLogMB*1MB)){$stopReason='log-budget';break}
        $free=(Get-PSDrive -Name ((Get-Item -LiteralPath $output).PSDrive.Name)).Free
        if(($initialFree-$free) -ge ([long]$MaxLogMB*1MB)){$stopReason='volume-consumption-budget';break}
        if($free -lt 30GB){$stopReason='disk-reserve';break}
        $process.Refresh()
    }
} catch {$failure=$_.Exception.Message;$stopReason='supervisor-failed'} finally {
    if($null -ne $process){
        $process.Refresh()
        if(-not $process.HasExited){
            if($process.Path -ne $tool){throw 'Refusing to terminate a process outside the launched replay path.'}
            [void]$process.Handle
            $cleanupStopped=$true
            $process.Kill()
            [void]$process.WaitForExit(5000)
        }
    }
    try{if($null -ne $outTask){[void]$outTask.Wait(5000)}}catch{}
    try{if($null -ne $errTask){[void]$errTask.Wait(5000)}}catch{}
    if($outStream){$outStream.Dispose()};if($errStream){$errStream.Dispose()}
}
$ended=Get-Date
$events=@(Get-WinEvent -FilterHashtable @{LogName='System';ProviderName='nvlddmkm';Id=153;StartTime=$started;EndTime=$ended} -ErrorAction SilentlyContinue)
$events | ForEach-Object {$_.ToXml()} | Set-Content -LiteralPath (Join-Path $output 'driver-events.xml') -Encoding utf8
$result=[ordered]@{Started=$started.ToString('o');Ended=$ended.ToString('o');Pid=$(if($process){$process.Id}else{$null});StopReason=$stopReason;ExitCode=$(if($process -and $process.HasExited){$process.ExitCode}else{$null});CleanupStopped=$cleanupStopped;ModulesCaptured=$modulesCaptured;ValidationRequested=[bool]$Validate;ValidationModuleConfirmed=$validationModuleConfirmed;RespondingAtDriverEvent=$responding;Driver153Messages=$events.Count;Failure=$failure;ReplayLog=$replayLog;Measurements=$measurements;NoSynchronizationInjection=$true;SwapchainMode=$SwapchainMode;ApplicationNameMatched=[bool]$MatchApplicationName;CapturedSwapchainMode=($SwapchainMode -eq 'captured')}
$result | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath (Join-Path $output 'result.json') -Encoding utf8
Write-Output ($result | ConvertTo-Json -Depth 5)
if($stopReason -ne 'exited' -or $failure -or $result.ExitCode -ne 0){exit 1}
