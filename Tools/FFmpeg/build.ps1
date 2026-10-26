param(
    [string[]]$Targets = @('all'),
    [int]$Jobs = [Math]::Min([Environment]::ProcessorCount, 16),
    [switch]$RequireAll,
    [switch]$Probe,
    [switch]$WithBridge,
    [switch]$WithoutBridge,
    [string]$UnityPluginApi = $env:UNITY_PLUGIN_API,
    [switch]$UseWsl,
    [string]$WslDistribution,
    [string]$BuildRoot
)
$ErrorActionPreference = 'Stop'
$arguments = @((Join-Path $PSScriptRoot 'build.py'), '--targets', ($Targets -join ','), '--jobs', $Jobs)
if ($RequireAll) { $arguments += '--require-all' }
if ($Probe) { $arguments += '--probe' }
if ($WithBridge) { $arguments += '--with-bridge' }
if ($WithoutBridge) { $arguments += '--without-bridge' }
if ($UnityPluginApi) { $arguments += @('--unity-plugin-api', $UnityPluginApi) }
if ($UseWsl) {
    # --cd accepts a Windows path; exec arguments bypass shell interpolation (spaces/$ remain literal).
    $wslArguments = @()
    if ($WslDistribution) { $wslArguments += @('-d', $WslDistribution) }
    $wslArguments += @('--cd', $PSScriptRoot, '--')
    if ($BuildRoot) { $wslArguments += @('env', "FFMPEG_BUILD_ROOT=$BuildRoot") }
    $arguments[0] = 'build.py'
    & wsl @wslArguments python3 @arguments
} else {
    $previousBuildRoot = $env:FFMPEG_BUILD_ROOT
    try {
        if ($BuildRoot) { $env:FFMPEG_BUILD_ROOT = $BuildRoot }
        & python @arguments
    } finally { $env:FFMPEG_BUILD_ROOT = $previousBuildRoot }
}
exit $LASTEXITCODE
