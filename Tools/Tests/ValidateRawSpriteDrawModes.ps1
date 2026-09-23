param(
    [Parameter(Mandatory = $true)]
    [string]$UnityPath
)

$ErrorActionPreference = 'Stop'
$root = (Resolve-Path (Join-Path $PSScriptRoot '../..')).Path
$project = Join-Path $root 'Temp/RawSpriteDrawModeValidation'
$assets = Join-Path $project 'Assets'
New-Item -ItemType Directory -Force -Path $assets | Out-Null
New-Item -ItemType Directory -Force -Path (Join-Path $project 'Packages') | Out-Null
New-Item -ItemType Directory -Force -Path (Join-Path $project 'ProjectSettings') | Out-Null
Copy-Item (Join-Path $root 'ProjectSettings/ProjectVersion.txt') (Join-Path $project 'ProjectSettings/ProjectVersion.txt')
Copy-Item (Join-Path $PSScriptRoot 'manifest.json') (Join-Path $project 'Packages/manifest.json')
Copy-Item (Join-Path $PSScriptRoot 'RawSpriteDrawModeValidation.cs') $assets
Copy-Item (Join-Path $root 'Assets/Scripts/Rendering/RawSpriteMeshBuilder.cs') $assets
Copy-Item (Join-Path $root 'Assets/Scripts/Rendering/RawSpriteResources.cs') $assets
$log = Join-Path $project 'validation.log'
$process = Start-Process -FilePath $UnityPath -ArgumentList @(
    '-batchmode', '-nographics', '-quit', '-projectPath', "`"$project`"",
    '-executeMethod', 'RawSpriteDrawModeValidation.Run', '-logFile', "`"$log`""
) -Wait -PassThru
if ($process.ExitCode -ne 0 -or !(Select-String -Path $log -Pattern 'RAW_SPRITE_DRAW_MODE_VALIDATION_PASSED' -Quiet)) {
    Get-Content $log -Tail 80
    throw "Draw mode validation failed. See $log"
}
Write-Output "RawSprite draw mode validation passed. Log: $log"
