param(
    [Parameter(Mandatory=$true)][string] $OutputDirectory,
    [Parameter(Mandatory=$true)][string] $PlayerDirectory,
    [int] $Seconds = 40,
    [string] $SdkBin = 'E:/_Game/majp-test/vkdiag/sdk/Bin'
)
$ErrorActionPreference = 'Stop'
$module = (Resolve-Path -LiteralPath $PSScriptRoot).Path
$work = [IO.Path]::GetFullPath((Join-Path $module '.work')).TrimEnd('\') + '\'
$output = [IO.Path]::GetFullPath($OutputDirectory)
if (-not $output.StartsWith($work, [StringComparison]::OrdinalIgnoreCase) -or (Test-Path -LiteralPath $output)) {
    throw 'Use a new output directory under this module .work.'
}
$exe = (Get-Item -LiteralPath (Join-Path $PlayerDirectory 'MajdataPlay.exe')).FullName
New-Item -ItemType Directory -Path $output | Out-Null
$settings = Join-Path $output 'layer-settings'
New-Item -ItemType Directory -Path $settings | Out-Null
$log = Join-Path $output 'validation.log'
$settingsPath = Join-Path $settings 'vk_layer_settings.txt'
@(
    'khronos_validation.validate_core = true'
    'khronos_validation.validate_sync = true'
    'khronos_validation.syncval_full_validation = true'
    'khronos_validation.debug_action = VK_DBG_LAYER_ACTION_LOG_MSG'
    'khronos_validation.report_flags = error,warn,info'
    'khronos_validation.duplicate_message_limit = 100'
    ('khronos_validation.log_filename = ' + $log)
) | Set-Content -LiteralPath $settingsPath -Encoding ascii
$previous = @{}
$values = [ordered]@{
    VK_LAYER_PATH = (Resolve-Path -LiteralPath $SdkBin).Path
    VK_INSTANCE_LAYERS = 'VK_LAYER_KHRONOS_validation'
    VK_LAYER_SETTINGS_PATH = $settings
    GFXRECON_DISABLE = '1'
    DISABLE_VULKAN_OBS_CAPTURE = '1'
}
$process = $null
$startInfo = New-Object System.Diagnostics.ProcessStartInfo
$startInfo.FileName = $exe
$startInfo.Arguments = '-force-vulkan'
$startInfo.WorkingDirectory = (Split-Path -Parent $exe)
$startInfo.UseShellExecute = $false
$startInfo.RedirectStandardOutput = $true
$startInfo.RedirectStandardError = $true
foreach ($key in $values.Keys) { $startInfo.EnvironmentVariables[$key] = $values[$key] }
$stderrPath = Join-Path $output 'stderr.txt'
try {
    $process = [System.Diagnostics.Process]::Start($startInfo)
    Start-Sleep -Seconds $Seconds
    $process.Refresh()
    $alive = -not $process.HasExited
    $verified = $false
    if ($alive) {
        $verified = (Get-Process -Id $process.Id).Path -eq $exe
    }
} finally {
    foreach ($key in $previous.Keys) {
        [Environment]::SetEnvironmentVariable($key, $previous[$key], 'Process')
    }
}
$text = if (Test-Path -LiteralPath $log) { Get-Content -LiteralPath $log -Raw } else { '' }
$result = [ordered]@{
    FinishedAt = (Get-Date).ToString('o')
    PlayerDirectory = $PlayerDirectory
    Seconds = $Seconds
    StartedPid = if ($process) { $process.Id } else { $null }
    AliveAtEnd = $alive
    ExePathVerified = $verified
    LogPresent = (Test-Path -LiteralPath $log)
    HazardCount = @([regex]::Matches($text, 'SYNC-HAZARD-[A-Z-]+')).Count
    HazardKinds = @([regex]::Matches($text, 'SYNC-HAZARD-[A-Z-]+') | ForEach-Object { $_.Value } | Sort-Object -Unique)
    BufferSizes = @([regex]::Matches($text, 'size = (\d+)') | ForEach-Object { $_.Groups[1].Value } | Sort-Object -Unique)
    IndexReadHazards = @([regex]::Matches($text, 'INDEX_READ_BIT')).Count
    NavigationPerformed = $false
    Note = 'Launch-and-observe only: no keyboard navigation, no chart, no synthetic helper.'
}
$result | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath (Join-Path $output 'owner-result.json') -Encoding utf8
if ($alive -and $verified) {
    Stop-Process -Id $process.Id
    $process.WaitForExit(10000) | Out-Null
    $result['CleanupStopped'] = $true
    Add-Content -LiteralPath (Join-Path $output 'owner-result.json') -Value '' -Encoding utf8
    ($result | ConvertTo-Json -Depth 4) | Set-Content -LiteralPath (Join-Path $output 'owner-result.json') -Encoding utf8
}
Write-Output ($result | ConvertTo-Json -Depth 4)
