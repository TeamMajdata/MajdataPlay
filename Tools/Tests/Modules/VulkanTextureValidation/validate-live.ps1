param(
    [Parameter(Mandatory=$true)][string] $OutputDirectory,
    [switch] $BufferBarrier,
    [ValidateSet('Narrow', 'Broad')][string] $BufferBarrierMode = 'Broad',
    [ValidateSet('ffmpeg', 'synthetic')][string] $FrameSource = 'synthetic',
    [ValidateSet('Hardware', 'Software')][string] $ExpectedDecoder = 'Hardware',
    [string] $PlayerDirectory='',
    [string] $SdkBin='E:/_Game/majp-test/vkdiag/sdk/Bin'
)
$ErrorActionPreference='Stop'
$module=(Resolve-Path -LiteralPath $PSScriptRoot).Path
$work=[IO.Path]::GetFullPath((Join-Path $module '.work')).TrimEnd('\')+'\'
$output=[IO.Path]::GetFullPath($OutputDirectory)
if(-not $output.StartsWith($work,[StringComparison]::OrdinalIgnoreCase) -or (Test-Path -LiteralPath $output)){
    throw 'Use a new validation output directory under this module .work.'
}
if(-not $PlayerDirectory){$PlayerDirectory=Join-Path $work 'MainSynthetic'}
$manifest=(Get-Item -LiteralPath (Join-Path $SdkBin 'VkLayer_khronos_validation.json')).FullName
$layerInfo=Get-Content -LiteralPath $manifest -Raw|ConvertFrom-Json
if($layerInfo.layer.name -ne 'VK_LAYER_KHRONOS_validation'){throw 'Unexpected validation layer manifest.'}
$layer=(Get-Item -LiteralPath (Join-Path $SdkBin $layerInfo.layer.library_path)).FullName
New-Item -ItemType Directory -Path $output|Out-Null
$settings=Join-Path $output 'layer-settings'
New-Item -ItemType Directory -Path $settings|Out-Null
$validationLog=Join-Path $output 'validation.log'
$settingsPath=Join-Path $settings 'vk_layer_settings.txt'
@('khronos_validation.validate_core = true',
  'khronos_validation.validate_sync = true',
  'khronos_validation.syncval_full_validation = true',
  'khronos_validation.debug_action = VK_DBG_LAYER_ACTION_LOG_MSG',
  'khronos_validation.report_flags = error,warn,info',
  'khronos_validation.duplicate_message_limit = 100',
  ('khronos_validation.log_filename = '+$validationLog))|Set-Content -LiteralPath $settingsPath -Encoding ascii
$values=[ordered]@{
    VK_LAYER_PATH=(Resolve-Path -LiteralPath $SdkBin).Path
    VK_INSTANCE_LAYERS='VK_LAYER_KHRONOS_validation'
    VK_LAYER_SETTINGS_PATH=$settings
    VK_LOADER_DEBUG='error,warn,layer'
    GFXRECON_DISABLE='1'
    VK_ADD_INSTANCE_LAYERS=$null
    VK_ADD_LAYER_PATH=$null
    VK_LOADER_LAYERS_ENABLE=$null
    VK_LOADER_LAYERS_DISABLE=$null
    VK_LAYER_ENABLES=$null
    VK_LAYER_DISABLES=$null
    MAJ_SYNTHETIC_BUFFER_BARRIER=$(if(-not $BufferBarrier){$null}elseif($BufferBarrierMode -eq 'Narrow'){'1'}else{'2'})
}
[ordered]@{PreparedAt=(Get-Date).ToString('o');Layer=$layer;LayerSHA256=(Get-FileHash -LiteralPath $layer -Algorithm SHA256).Hash;SettingsSHA256=(Get-FileHash -LiteralPath $settingsPath -Algorithm SHA256).Hash;Environment=$values;GpuAssistedRequested=$false;BufferBarrierRequested=[bool]$BufferBarrier;CaptureRequested=$false;DebugFailActionRequested=$false;ObsDisabled=$true;Scope='Direct isolated Player core+sync validation, without GFXR capture or replay'}|ConvertTo-Json -Depth 5|Set-Content -LiteralPath (Join-Path $output 'validation-plan.json') -Encoding utf8
$previous=@{}
$runnerSuccess=$false
try{
    foreach($key in $values.Keys){$previous[$key]=[Environment]::GetEnvironmentVariable($key,'Process');[Environment]::SetEnvironmentVariable($key,$values[$key],'Process')}
    $runnerArguments = @{ Attempts = 1; SecondsInGame = 200; Graphics = 'vulkan'; FrameSource = $FrameSource
        ExpectedDecoder = $ExpectedDecoder
        DisableObsCapture = $true; DriverObservationSeconds = 5; ExpectedThreadingMode = 'SplitJobs'
        PlayerDirectory = $PlayerDirectory; OutputDirectory = (Join-Path $output 'run') }
    if ($BufferBarrier) { $runnerArguments['BufferBarrier'] = $true; $runnerArguments['BufferBarrierMode'] = $BufferBarrierMode }
    & (Join-Path $module 'run-main.ps1') @runnerArguments
    $runnerSuccess=$?
}finally{
    foreach($key in $previous.Keys){[Environment]::SetEnvironmentVariable($key,$previous[$key],'Process')}
}
$modulesPath=Join-Path $output 'run/attempt-1/modules.csv'
$moduleConfirmed=$false
if(Test-Path -LiteralPath $modulesPath){$rows=Import-Csv -LiteralPath $modulesPath;$moduleConfirmed=@($rows|Where-Object {$_.ModuleName -eq 'VkLayer_khronos_validation.dll' -and $_.FileName.Equals($layer,[StringComparison]::OrdinalIgnoreCase)}).Count -eq 1}
$text=if(Test-Path -LiteralPath $validationLog){Get-Content -LiteralPath $validationLog -Raw}else{''}
$hazards=@([regex]::Matches($text,'SYNC-HAZARD-[A-Z-]+')|ForEach-Object{$_.Value})
$result=[ordered]@{FinishedAt=(Get-Date).ToString('o');RunnerSucceeded=$runnerSuccess;ValidationModuleConfirmed=$moduleConfirmed;ValidationLogPresent=(Test-Path -LiteralPath $validationLog);CoreStatusPrinted=($text -match 'Core Checks');SynchronizationStatusPrinted=($text -match 'Synchronization');HazardMessages=$hazards.Count;HazardKinds=@($hazards|Sort-Object -Unique);NoCapture=$true;NoReplay=$true;NoGpuAssisted=$true;Log=$validationLog}
$result|ConvertTo-Json -Depth 5|Set-Content -LiteralPath (Join-Path $output 'validation-result.json') -Encoding utf8
Write-Output ($result|ConvertTo-Json -Depth 5)
if(-not $moduleConfirmed -or -not $result.ValidationLogPresent){exit 1}
