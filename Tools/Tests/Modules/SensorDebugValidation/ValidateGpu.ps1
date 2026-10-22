[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string]$UnityPath
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
# Reuse the existing staging and managed Unity regression before opening the same project with graphics.
& "$PSScriptRoot/Validate.ps1" -UnityPath $UnityPath
$repositoryRoot = (Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '../../../..')).Path
$project = Join-Path $repositoryRoot 'Temp/SensorDebugValidation'
Copy-Item -LiteralPath "$PSScriptRoot/GpuValidation.cs" -Destination "$project/Assets/GpuValidation.cs"
$log = Join-Path $project 'gpu-validation.log'
$process = Start-Process -FilePath $UnityPath -WindowStyle Hidden -ArgumentList @(
    '-batchmode', '-force-d3d11', '-quit', '-projectPath', "`"$project`"",
    '-executeMethod', 'MajdataPlay.Tests.SensorDebugValidation.GpuValidation.Run', '-logFile', "`"$log`""
) -Wait -PassThru
if ($process.ExitCode -ne 0 -or !(Select-String -LiteralPath $log -Pattern 'SENSOR_DEBUG_GPU_VALIDATION_PASSED' -Quiet))
{
    Get-Content -LiteralPath $log -Tail 80
    throw "Sensor debug GPU validation failed. See $log"
}
Select-String -LiteralPath $log -Pattern 'SENSOR_DEBUG_GPU_VALIDATION_PASSED|GPU_CLASSIC_PIXEL|GPU_SENSOR_EDGES'
