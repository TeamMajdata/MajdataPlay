[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string]$UnityPath
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$repositoryRoot = (Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '../../../..')).Path
& "$PSScriptRoot/ValidateSceneFlags.ps1"
$project = Join-Path $repositoryRoot 'Temp/SensorDebugValidation'
$assets = Join-Path $project 'Assets'
foreach ($directory in @($assets, "$project/Packages", "$project/ProjectSettings"))
{
    New-Item -ItemType Directory -Path $directory -Force | Out-Null
}
Copy-Item -LiteralPath "$repositoryRoot/ProjectSettings/ProjectVersion.txt" -Destination "$project/ProjectSettings/ProjectVersion.txt"
'{"dependencies":{"com.unity.modules.physics":"1.0.0"}}' | Set-Content -LiteralPath "$project/Packages/manifest.json" -Encoding utf8
$newtonsoft = Join-Path (Split-Path -Parent $UnityPath) 'Data/Managed/Newtonsoft.Json.dll'
Copy-Item -LiteralPath $newtonsoft -Destination $assets
foreach ($source in @('Validation.cs', 'Stubs.cs'))
{
    Copy-Item -LiteralPath "$PSScriptRoot/$source" -Destination $assets
}
$sources = @(
    'Assets/csc.rsp',
    'Assets/Scripts/Global/DummyTouchPanelRenderer.cs',
    'Assets/Scripts/Misc/Settings/DebugOptions.cs',
    'Assets/Scripts/Misc/Settings/Enums/SensorDebugStyle.cs',
    'Assets/Scripts/Misc/Settings/Enums/SensorDebugPosition.cs',
    'Assets/Scripts/Misc/Settings/Enums/OffsetUnitOption.cs',
    'Assets/Scripts/Misc/Settings/Enums/DJAutoPolicyOption.cs',
    'Assets/Scripts/Misc/Settings/Attributes/StepAttribute.cs',
    'Assets/Scripts/Misc/Settings/Attributes/RangeAttribute.cs',
    'Assets/Scripts/Misc/Settings/Attributes/HideInSettingUIAttribute.cs',
    'Assets/Scripts/Misc/Settings/Attributes/OptionEnumeratorAttribute.cs',
    'Assets/Scripts/Misc/Settings/Interfaces/IOptionEnumerator.cs',
    'Assets/Plugins/MajdataPlay/Diagnostics/Enums/LogLevel.cs',
    'Assets/Models/JudgeAreas.fbx',
    'Assets/Models/JudgeAreas.fbx.meta',
    'Assets/Models/JudgeAreas.mat',
    'Assets/Models/JudgeAreas.mat.meta',
    'Assets/Rendering/Shaders/Common/SensorBorder.shader',
    'Assets/Rendering/Shaders/Common/SensorBorder.shader.meta'
)
foreach ($source in $sources)
{
    $destination = Join-Path $project $source
    New-Item -ItemType Directory -Path (Split-Path -Parent $destination) -Force | Out-Null
    Copy-Item -LiteralPath (Join-Path $repositoryRoot $source) -Destination $destination
}
foreach ($directory in @('Assets/Models/JudgeAreaBorders', 'Assets/Plugins/MajdataPlay/Rendering/RenderPipelines/MajRenderPipeline/Library'))
{
    $destination = Join-Path $project $directory
    New-Item -ItemType Directory -Path (Split-Path -Parent $destination) -Force | Out-Null
    Copy-Item -LiteralPath (Join-Path $repositoryRoot $directory) -Destination (Split-Path -Parent $destination) -Recurse -Force
}
$log = Join-Path $project 'validation.log'
$process = Start-Process -FilePath $UnityPath -WindowStyle Hidden -ArgumentList @(
    '-batchmode', '-nographics', '-quit', '-projectPath', "`"$project`"",
    '-executeMethod', 'MajdataPlay.Tests.SensorDebugValidation.Validation.Run', '-logFile', "`"$log`""
) -Wait -PassThru
if ($process.ExitCode -ne 0 -or !(Select-String -LiteralPath $log -Pattern 'SENSOR_DEBUG_VALIDATION_PASSED' -Quiet))
{
    Get-Content -LiteralPath $log -Tail 80
    throw "Sensor debug validation failed. See $log"
}
Select-String -LiteralPath $log -Pattern 'SENSOR_DEBUG_VALIDATION_PASSED'
