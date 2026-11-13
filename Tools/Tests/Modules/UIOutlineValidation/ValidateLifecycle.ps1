[CmdletBinding()]
param(
    [string]$UnityPath = 'C:/Program Files/Unity/Hub/Editor/6000.3.17f1/Editor/Unity.exe',
    [ValidateSet('All', 'EditMode', 'PlayMode')]
    [string]$Mode = 'All',
    [string]$TestFilter = '',
    [switch]$StageOnly,
    [switch]$Graphics,
    [ValidateSet('D3D11', 'Vulkan')]
    [string]$GraphicsApi = 'D3D11'
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$root = (Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '../../../..')).Path
$project = Join-Path $root 'Temp/UIOutlineLifecycleValidation'

# Reject an active preview before staging can overwrite or remove any of its files.
if ([Environment]::OSVersion.Platform -eq [PlatformID]::Win32NT)
{
    $normalizedProjectPath = [IO.Path]::GetFullPath($project).Replace('/', '\').TrimEnd([char[]]'\/')
    foreach ($unityProcess in (Get-CimInstance Win32_Process -Filter "Name='Unity.exe'" -ErrorAction Stop))
    {
        if ([string]::IsNullOrWhiteSpace($unityProcess.CommandLine))
        {
            continue
        }
        $projectArgument = [regex]::Match($unityProcess.CommandLine,
            '(?i)(?:^|\s)"?-projectPath"?\s+(?:"(?<path>[^"]+)"|(?<path>\S+))')
        if (!$projectArgument.Success)
        {
            continue
        }
        try
        {
            $openProjectPath = [IO.Path]::GetFullPath($projectArgument.Groups['path'].Value).Replace('/', '\').TrimEnd([char[]]'\/')
        }
        catch
        {
            continue
        }
        if ([string]::Equals($normalizedProjectPath, $openProjectPath, [StringComparison]::OrdinalIgnoreCase))
        {
            throw 'Close the preview Unity Editor for Temp/UIOutlineLifecycleValidation before running validation. No validation files have been changed.'
        }
    }
}

$assets = Join-Path $project 'Assets'
foreach ($directory in @($assets, "$project/Packages", "$project/ProjectSettings"))
{
    New-Item -ItemType Directory -Path $directory -Force | Out-Null
}

# Remove only obsolete copies left by the earlier staging layout, never source artwork.
foreach ($relative in @(
    'Assets/Resources/Rendering/UI/newui-outlined@3x.png',
    'Assets/Resources/Rendering/UI/AtlasAlpha.mat',
    'Assets/Scripts/Misc/Rendering/UiBackgroundMode.cs',
    'Assets/Rendering/Shaders/UI/CollectionOcclusion.hlsl',
    'Assets/Rendering/Shaders/UI/KyantLiquidGlass.hlsl'
    'Assets/Rendering/Materials/UI/List/MainCoverFrameGlass.mat',
    'Assets/Rendering/Materials/UI/List/CoverRingGlass.mat',
    'Assets/Rendering/Materials/UI/List/DifficultyBadgeGlass.mat'
))
{
    $obsolete = Join-Path $project $relative
    foreach ($path in @($obsolete, "$obsolete.meta"))
    {
        if (Test-Path -LiteralPath $path)
        {
            Remove-Item -LiteralPath $path
        }
    }
}

function Copy-ProductionFile([string]$Relative)
{
    $source = Join-Path $root $Relative
    $destination = Join-Path $project $Relative
    New-Item -ItemType Directory -Path (Split-Path -Parent $destination) -Force | Out-Null
    Copy-Item -LiteralPath $source -Destination $destination
    if (Test-Path -LiteralPath "$source.meta")
    {
        Copy-Item -LiteralPath "$source.meta" -Destination "$destination.meta"
    }
}

Copy-ProductionFile 'ProjectSettings/ProjectVersion.txt'
Copy-ProductionFile 'ProjectSettings/TagManager.asset'
Copy-ProductionFile 'Assets/csc.rsp'
$dependencies = @{}
$mainManifest = Get-Content -LiteralPath (Join-Path $root 'Packages/manifest.json') -Raw | ConvertFrom-Json
foreach ($dependency in $mainManifest.dependencies.PSObject.Properties)
{
    if ($dependency.Name.StartsWith('com.unity.modules.', [StringComparison]::Ordinal))
    {
        $dependencies[$dependency.Name] = $dependency.Value
    }
}
foreach ($name in @('com.unity.ugui', 'com.unity.test-framework', 'com.unity.ext.nunit'))
{
    $package = Get-ChildItem -LiteralPath (Join-Path $root 'Library/PackageCache') -Directory -Filter "$name@*" | Select-Object -First 1
    if (!$package)
    {
        throw "The installed project package cache is required: $name"
    }
    $dependencies[$name] = 'file:' + $package.FullName.Replace('\', '/')
}
@{ dependencies = $dependencies } | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath "$project/Packages/manifest.json" -Encoding utf8

$production = 'Assets/Scripts/Misc/Rendering'
foreach ($relative in @("$production/QualityUiImage.cs", "$production/UiOutlineTheme.cs", "$production/UiOutlineMode.cs", "$production/UiOutlineResources.cs", "$production/UiAtlasLayout.cs", "$production/UiAtlasMeshGenerator.cs", "$production/MainDisplayUiClip.cs", "$production/UiBackdropPanel.cs", "$production/UiGlassOpticsAtlas.cs", "$production/UiBackdropForeground.cs", "$production/UiBackdropStyle.cs", "$production/UiPvBackdropBlur.cs", "$production/UiTransitionOverlay.cs"))
{
    Copy-ProductionFile $relative
}
# Join the exact production bridge to this narrow Core assembly; capture behavior is not substituted.
$bridgeSource = Join-Path $root 'Assets/Plugins/MajdataPlay/Rendering/RenderPipelines/MajRenderPipeline/Runtime/MajUiBackdropBridge.cs'
$bridgeDestination = "$assets/Scripts/Misc/Rendering/MajUiBackdropBridge.cs"
Copy-Item -LiteralPath $bridgeSource -Destination $bridgeDestination
Copy-Item -LiteralPath "$bridgeSource.meta" -Destination "$bridgeDestination.meta"
# Stage the actual renderer beside the bridge, including its Editor partial in this
# same isolated assembly. Never substitute the queue-splitting MRP implementation.
$mrpRuntime = Join-Path $root 'Assets/Plugins/MajdataPlay/Rendering/RenderPipelines/MajRenderPipeline/Runtime'
$mrpScripts = "$assets/Scripts/Misc/Rendering/MRP"
New-Item -ItemType Directory -Path $mrpScripts -Force | Out-Null
foreach ($source in (Get-ChildItem -LiteralPath $mrpRuntime -File -Filter '*.cs'))
{
    if ($source.Name -eq 'MajUiBackdropBridge.cs')
    {
        continue
    }
    Copy-Item -LiteralPath $source.FullName -Destination (Join-Path $mrpScripts $source.Name)
    Copy-Item -LiteralPath "$($source.FullName).meta" -Destination (Join-Path $mrpScripts "$($source.Name).meta")
}
$mrpEditorPartial = Join-Path $root 'Assets/Plugins/MajdataPlay/Rendering/RenderPipelines/MajRenderPipeline/Editor/MajCameraRenderer.Editor.cs'
Copy-Item -LiteralPath $mrpEditorPartial -Destination "$mrpScripts/MajCameraRenderer.Editor.cs"
Copy-Item -LiteralPath "$mrpEditorPartial.meta" -Destination "$mrpScripts/MajCameraRenderer.Editor.cs.meta"
$opticsEditor = "$assets/Editor/Optics"
New-Item -ItemType Directory -Path $opticsEditor -Force | Out-Null
Copy-Item -LiteralPath (Join-Path $root 'Assets/Scripts/Editor/Rendering/UiGlassOpticsAtlasBaker.cs') -Destination "$opticsEditor/UiGlassOpticsAtlasBaker.cs"
Copy-Item -LiteralPath (Join-Path $root 'Assets/Scripts/Editor/Rendering/UiGlassOpticsAtlasBaker.cs.meta') -Destination "$opticsEditor/UiGlassOpticsAtlasBaker.cs.meta"
@{
    name = 'MajdataPlay.Core.Editor'
    references = @('MajdataPlay.Core', 'UnityEngine.UI')
    includePlatforms = @('Editor')
} | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath "$opticsEditor/Optics.Editor.asmdef" -Encoding utf8
Copy-ProductionFile 'Assets/Scripts/Misc/Base/Enums/MajScenes.cs'
Copy-Item -LiteralPath (Join-Path $root 'Assets/Scripts/AssemblyInfo.cs') -Destination "$assets/Scripts/AssemblyInfo.cs"
@{
    name = 'MajdataPlay.Core'
    references = @('UnityEngine.UI', 'Unity.TextMeshPro')
} | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath "$assets/Scripts/MajdataPlay.Core.asmdef" -Encoding utf8

# These narrow substitutes cover only diagnostics and read-only test text access.
# No game initialization, quality mapping, Image, material, sprite, or Unity lifecycle is replaced.
@'
using System;
using UnityEngine;
#nullable enable
namespace MajdataPlay.Diagnostics
{
    public static class MajDebug
    {
        /// <summary>Forwards informational diagnostics to the isolated Unity log.</summary>
        /// <param name="tag">The production diagnostic category.</param>
        /// <param name="message">The diagnostic message.</param>
        public static void LogInfo(string tag, string message)
        {
            Debug.Log(tag + ": " + message);
        }
        public static void LogWarning(string tag, string message)
        {
            Debug.LogWarning(tag + ": " + message);
        }
        public static void LogError(string tag, string message)
        {
            Debug.LogError(tag + ": " + message);
        }
    }
}
// Only the main-camera identity is substituted; production capture, filtering and native UGUI remain intact.
namespace MajdataPlay
{
    public static class SceneSwitcher
    {
        public static Camera? MainCamera { get; set; }
        public static MajScenes CurrentScene { get; private set; } = MajScenes.Init;
    }
}
namespace MajdataPlay.IO.Storage
{
    public static class FileSystem
    {
        public static class File
        {
            public static ReadOnlyFile Open(string path)
            {
                return new ReadOnlyFile(path);
            }
        }
    }
    public sealed class ReadOnlyFile
    {
        private readonly string _path;
        public ReadOnlyFile(string path)
        {
            _path = path;
        }
        public string ReadAllText()
        {
            return System.IO.File.ReadAllText(_path);
        }
    }
}
'@ | Set-Content -LiteralPath "$assets/Scripts/IsolatedServices.cs" -Encoding utf8

foreach ($relative in @(
    'Assets/Rendering/Shaders/Common/Opaque.shader',
    'Assets/Rendering/Shaders/Common/SensorBorder.shader',
    'Assets/Models/JudgeAreas.mat',
    'Assets/Rendering/Shaders/UI/Default.shader',
    'Assets/Rendering/Shaders/UI/RoundedRect.shader',
    'Assets/Rendering/Shaders/UI/BlurCircleFade_Gaussian.shader',
    'Assets/Rendering/Shaders/UI/AtlasAlpha.shader',
    'Assets/Rendering/Shaders/UI/AtlasCutoff.shader',
    'Assets/Rendering/Shaders/UI/PvGaussian.shader',
    'Assets/Rendering/Shaders/UI/PvGlass.shader',
    'Assets/Rendering/Shaders/UI/UICircleOutlineShadow.shader',
    'Assets/Plugins/MajdataPlay/Rendering/RenderPipelines/MajRenderPipeline/Runtime/Shaders/MajUnlit.shader',
    'Assets/Plugins/MajdataPlay/Rendering/RenderPipelines/MajRenderPipeline/Runtime/Shaders/MajFinalCopy.shader',
    'Assets/Rendering/Shaders/UI/List/CoverShader.shader',
    'Assets/Rendering/Shaders/UI/List/CoverAlpha.shader',
    'Assets/Rendering/Shaders/UI/List/CoverDecorationGlass.shader',
    'Assets/TextMesh Pro/Shaders/TMP_SDF-Mobile.shader',
    'Assets/TextMesh Pro/Shaders/TMP_SDF.shader',
    'Assets/TextMesh Pro/Shaders/TMP_Sprite.shader',
    'Assets/TextMesh Pro/Shaders/TMPro_Properties.cginc',
    'Assets/TextMesh Pro/Shaders/TMPro.cginc',
    'Assets/TextMesh Pro/Resources/TMP Settings.asset',
    'Assets/Fonts/TMP Font Assets/UI/[UI]Jua-Regular SDF.asset',
    'Assets/TextMesh Pro/Resources/Fonts & Materials/LiberationSans SDF.asset',
    'Assets/TextMesh Pro/Resources/Style Sheets/Default Style Sheet.asset',
    'Assets/TextMesh Pro/Resources/LineBreaking Leading Characters.txt',
    'Assets/TextMesh Pro/Resources/LineBreaking Following Characters.txt',
    'Assets/Sprites/Ribbon/newui@3x.png',
    'Assets/Sprites/Ribbon/newui-outlined@3x.png'
))
{
    Copy-ProductionFile $relative
}
foreach ($relative in @(
    'Assets/Resources/Rendering/UI',
    'Assets/Rendering/Materials/UI',
    'Assets/Plugins/MajdataPlay/Rendering/RenderPipelines/MajRenderPipeline/Library'
))
{
    $destination = Join-Path $project $relative
    New-Item -ItemType Directory -Path (Split-Path -Parent $destination) -Force | Out-Null
    Copy-Item -LiteralPath (Join-Path $root $relative) -Destination (Split-Path -Parent $destination) -Recurse -Force
}

# Preserve real YAML/component identifiers for the serialization test; do not open or resave any scenes.
$atlasGuid = '04e00e0b72a3d284ebec3f7160996aa1'
foreach ($relative in @(& git -C $root -c core.quotepath=false ls-files -- Assets/Scenes Assets/Prefabs))
{
    if ($relative -match '\.(unity|prefab)$' -and [System.IO.File]::ReadAllText((Join-Path $root $relative)).Contains($atlasGuid))
    {
        Copy-ProductionFile $relative
    }
}
foreach ($relative in @(
    'Assets/Scenes/Init.unity',
    'Assets/Scenes/Setting.unity',
    'Assets/Scenes/SortFind.unity',
    'Assets/Prefabs/Setting/MenuTitleDisplayer.prefab',
    'Assets/Prefabs/UI/Buttons.prefab',
    'Assets/Prefabs/UI/ButtonHoldCircle.prefab',
    'Assets/Prefabs/List/Main_Display_ribbon.prefab',
    'Assets/Prefabs/List/Favorite.prefab',
    'Assets/Prefabs/List/Model/LevelDisplayer.prefab',
    'Assets/Prefabs/List/Model/SongCoverDisplayer.prefab',
    'Assets/Prefabs/List/Model/ThumbnailDisplayer.prefab',
    'Assets/Prefabs/List/Model/CollectionDisplayer.prefab',
    'Assets/Sprites/Game/UIs/1080Circle.png',
    'Assets/Sprites/Game/UIs/128circle.png',
    'Assets/Sprites/Game/UIs/256circle.png',
    'Assets/Sprites/dummy.png',
    'Assets/Sprites/color.png',
    'Assets/Sprites/1080Circle_Rev.png',
    'Assets/Sprites/Ribbon/icons.png',
    'Assets/Sprites/Ribbon/heart_empty.png',
    'Assets/Sprites/List/comment.png'
))
{
    Copy-ProductionFile $relative
}

$editTests = "$assets/Tests/EditMode"
$playTests = "$assets/Tests/PlayMode"
foreach ($directory in @($editTests, $playTests))
{
    New-Item -ItemType Directory -Path $directory -Force | Out-Null
}
Copy-Item -LiteralPath (Join-Path $root 'Assets/Tests/EditMode/Game/Rendering/QualityUiImageAssetTests.cs') -Destination $editTests
Copy-Item -LiteralPath (Join-Path $root 'Assets/Tests/EditMode/Game/Rendering/UiAtlasGeometryTests.cs') -Destination $editTests
Copy-Item -LiteralPath (Join-Path $root 'Assets/Tests/EditMode/Game/Rendering/MainDisplayUiClipTests.cs') -Destination $editTests
Copy-Item -LiteralPath (Join-Path $root 'Assets/Tests/EditMode/Game/Rendering/IndependentUiImageAssetTests.cs') -Destination $editTests
Copy-Item -LiteralPath (Join-Path $root 'Assets/Tests/EditMode/Game/Rendering/ListButtonLayerTests.cs') -Destination $editTests
Copy-Item -LiteralPath (Join-Path $root 'Assets/Tests/EditMode/Game/Rendering/AtlasAlphaMaterialTests.cs') -Destination $editTests
Copy-Item -LiteralPath (Join-Path $root 'Assets/Tests/EditMode/Game/Rendering/UiPvBackdropMappingTests.cs') -Destination $editTests
Copy-Item -LiteralPath (Join-Path $root 'Assets/Tests/EditMode/Game/Rendering/UiPvBackdropAssetTests.cs') -Destination $editTests
Copy-Item -LiteralPath (Join-Path $root 'Assets/Tests/EditMode/Game/Rendering/UiBackdropMrpStageTests.cs') -Destination $editTests
Copy-Item -LiteralPath (Join-Path $root 'Assets/Tests/EditMode/Game/Rendering/SensorOverlayMaterialTests.cs') -Destination $editTests
Copy-Item -LiteralPath (Join-Path $root 'Assets/Tests/Shared/TestObjects.cs') -Destination $editTests
Copy-Item -LiteralPath (Join-Path $root 'Assets/Tests/PlayMode/Game/Rendering/UiPvBackdropLifecycleTests.cs') -Destination $playTests
Copy-Item -LiteralPath (Join-Path $root 'Assets/Tests/PlayMode/Game/Rendering/UiGlassOpticsAtlasTests.cs') -Destination $playTests
Copy-Item -LiteralPath (Join-Path $root 'Assets/Tests/PlayMode/Game/Rendering/QualityUiImagePlayModeTests.cs') -Destination $playTests
Copy-Item -LiteralPath (Join-Path $root 'Assets/Tests/PlayMode/Game/Rendering/UiTransitionOverlayPlayModeTests.cs') -Destination $playTests
Copy-Item -LiteralPath (Join-Path $root 'Assets/Tests/Shared/TestObjects.cs') -Destination $playTests
@{
    name = 'MajdataPlay.Tests.Game'
    references = @('UnityEngine.TestRunner', 'UnityEditor.TestRunner', 'MajdataPlay.Core', 'UnityEngine.UI', 'Unity.TextMeshPro')
    includePlatforms = @('Editor')
    overrideReferences = $true
    precompiledReferences = @('nunit.framework.dll')
    defineConstraints = @('UNITY_INCLUDE_TESTS')
    autoReferenced = $false
} | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath "$editTests/Tests.asmdef" -Encoding utf8
@{
    name = 'MajdataPlay.Tests.Game.PlayMode'
    references = @('UnityEngine.TestRunner', 'MajdataPlay.Core', 'UnityEngine.UI', 'Unity.TextMeshPro')
    overrideReferences = $true
    precompiledReferences = @('nunit.framework.dll')
    defineConstraints = @('UNITY_INCLUDE_TESTS')
    autoReferenced = $false
} | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath "$playTests/Tests.asmdef" -Encoding utf8

if ($StageOnly)
{
    Write-Output "UI_OUTLINE_STAGED project=$project"
    return
}

$modes = @('EditMode', 'PlayMode')
if ($Mode -ne 'All')
{
    $modes = @($Mode)
}
foreach ($currentMode in $modes)
{
    $results = Join-Path $project "$currentMode-results.xml"
    $log = Join-Path $project "$currentMode.log"
    $filter = 'MajdataPlay.Tests.Game.Rendering.QualityUiImageAssetTests;MajdataPlay.Tests.Game.Rendering.UiAtlasGeometryTests;MajdataPlay.Tests.Game.Rendering.MainDisplayUiClipTests;MajdataPlay.Tests.Game.Rendering.IndependentUiImageAssetTests;MajdataPlay.Tests.Game.Rendering.ListButtonLayerTests;MajdataPlay.Tests.Game.Rendering.UiPvBackdropMappingTests;MajdataPlay.Tests.Game.Rendering.UiPvBackdropAssetTests;MajdataPlay.Tests.Game.Rendering.SensorOverlayMaterialTests'
    if ($currentMode -eq 'PlayMode')
    {
        $filter = 'MajdataPlay.Tests.Game.Rendering.QualityUiImagePlayModeTests;MajdataPlay.Tests.Game.Rendering.UiPvBackdropLifecycleTests;MajdataPlay.Tests.Game.Rendering.UiTransitionOverlayPlayModeTests'
    }
    if ($TestFilter)
    {
        $filter = $TestFilter
    }
    $arguments = @(
        '-batchmode', '-projectPath', "`"$project`"", '-runTests',
        '-testPlatform', $currentMode, '-testFilter', $filter,
        '-testResults', "`"$results`"", '-logFile', "`"$log`""
    )
    if ($Graphics)
    {
        $arguments += $(if ($GraphicsApi -eq 'Vulkan') { '-force-vulkan' } else { '-force-d3d11' })
    }
    else
    {
        $arguments += '-nographics'
    }
    $process = Start-Process -FilePath $UnityPath -WindowStyle Hidden -ArgumentList $arguments -PassThru
    $process.WaitForExit()
    if (!(Test-Path -LiteralPath $results))
    {
        Get-Content -LiteralPath $log -Tail 70
        throw "$currentMode did not produce results. See $log"
    }
    [xml]$result = Get-Content -LiteralPath $results -Raw
    $summary = $result.'test-run'
    if ($process.ExitCode -ne 0 -or $summary.result -ne 'Passed')
    {
        Get-Content -LiteralPath $log -Tail 70
        throw "$currentMode failed ($($summary.result)). See $results"
    }
    Write-Output "UI_OUTLINE_${currentMode}_PASSED total=$($summary.total) passed=$($summary.passed)"
}
