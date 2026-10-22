param(
    [Parameter(Mandatory = $true)]
    [string]$UnityPath,
    [string]$CustomSkinPath
)

$ErrorActionPreference = 'Stop'
$root = (Resolve-Path (Join-Path $PSScriptRoot '../../..')).Path
if (!$CustomSkinPath) {
    $CustomSkinPath = Join-Path $root 'Assets/Scripts/Scenes/Game/Misc/Notes/Skins/CustomSkin.cs'
}
$project = Join-Path $root 'Temp/CustomSkinAtlasValidation'
$assets = Join-Path $project 'Assets'
$plugins = Join-Path $assets 'Plugins'
New-Item -ItemType Directory -Force -Path $plugins | Out-Null
New-Item -ItemType Directory -Force -Path (Join-Path $project 'Packages') | Out-Null
New-Item -ItemType Directory -Force -Path (Join-Path $project 'ProjectSettings') | Out-Null
Copy-Item (Join-Path $root 'ProjectSettings/ProjectVersion.txt') (Join-Path $project 'ProjectSettings/ProjectVersion.txt')
@{ dependencies = @{
    'com.unity.modules.imageconversion' = '1.0.0'
    'com.unity.modules.vectorgraphics' = '1.0.0'
} } | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $project 'Packages/manifest.json')
Copy-Item (Join-Path $PSScriptRoot 'CustomSkinAtlasValidation.cs') $assets

# Compile the current production AtlasBuilder, isolating unrelated CustomSkin dependencies.
$source = Get-Content -Raw -LiteralPath $CustomSkinPath
$namespaceOffset = $source.IndexOf('namespace MajdataPlay.Scenes.Game.Notes.Skins')
$builderOffset = $source.IndexOf('    public class AtlasBuilder')
if ($namespaceOffset -lt 0 -or $builderOffset -lt 0) {
    throw 'Could not locate the production AtlasBuilder source.'
}
$header = $source.Substring(0, $namespaceOffset).Replace('using MajdataPlay.Rendering;', '')
$builder = $source.Substring($builderOffset)
$limit = 'Math.Min(8192, SystemInfo.maxTextureSize)'
if (!$builder.Contains($limit)) {
    throw 'AtlasBuilder maximum size expression changed; update the validation limit injection.'
}
# Change only the staged hardware limit so the real ExecutePack can shrink on any GPU.
$builder = $builder.Replace($limit, 'Math.Min(CustomSkinAtlasValidation.MaximumAtlasSize, SystemInfo.maxTextureSize)')
Set-Content -LiteralPath (Join-Path $assets 'AtlasBuilder.cs') -Value ($header + "namespace MajdataPlay.Scenes.Game.Notes.Skins`n{`n" + $builder)
Set-Content -LiteralPath (Join-Path $assets 'csc.rsp') -Value '-unsafe'

# Reuse compiled package dependencies; loading / buffers / logging adapters are in the harness.
foreach ($assembly in @('UniTask', 'Unity.Burst', 'Unity.Collections', 'Unity.Mathematics', 'Unity.VectorGraphics', 'Unity.Profiling.Core', 'UnityEngine.UI')) {
    $assemblyPath = Join-Path $root "Library/ScriptAssemblies/$assembly.dll"
    if (!(Test-Path -LiteralPath $assemblyPath)) {
        throw "Missing $assemblyPath. Open the main project in Unity once to compile its dependencies."
    }
    Copy-Item -LiteralPath $assemblyPath -Destination $plugins
}
foreach ($dependency in @('com.unity.burst@*/Unity.Burst.Unsafe.dll', 'com.unity.collections@*/Unity.Collections.LowLevel.ILSupport/Unity.Collections.LowLevel.ILSupport.dll')) {
    $dependencyPath = Get-ChildItem -Path (Join-Path $root "Library/PackageCache/$dependency") | Select-Object -First 1
    if (!$dependencyPath) { throw "Missing package dependency: $dependency" }
    Copy-Item -LiteralPath $dependencyPath.FullName -Destination $plugins
}

$log = Join-Path $project 'validation.log'
$process = Start-Process -FilePath $UnityPath -ArgumentList @(
    '-batchmode', '-projectPath', "`"$project`"",
    '-executeMethod', 'CustomSkinAtlasValidation.Run', '-logFile', "`"$log`""
) -Wait -PassThru -WindowStyle Hidden
if ($process.ExitCode -ne 0 -or !(Select-String -Path $log -Pattern 'CUSTOM_SKIN_ATLAS_VALIDATION_PASSED' -Quiet)) {
    Get-Content $log -Tail 80
    throw "CustomSkin atlas validation failed. See $log"
}
Write-Output "CustomSkin atlas validation passed. Log: $log"
