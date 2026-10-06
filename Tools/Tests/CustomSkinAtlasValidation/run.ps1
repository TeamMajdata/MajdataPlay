param(
    [string]$UnityPath = 'C:/Program Files/Unity Editors/6000.3.17f1/Editor/Unity.exe',
    [ValidateSet('d3d11', 'glcore', 'vulkan')]
    [string]$GraphicsApi = 'd3d11',
    [switch]$Baseline
)

$ErrorActionPreference = 'Stop'
$root = (Resolve-Path (Join-Path $PSScriptRoot '../../..')).Path
$project = Join-Path $root 'Temp/CustomSkinAtlasValidation'
$assets = Join-Path $project 'Assets'
if (!(Test-Path -LiteralPath $UnityPath -PathType Leaf)) {
    throw "Unity Editor not found: $UnityPath"
}
$expectedVersion = (Select-String -LiteralPath (Join-Path $root 'ProjectSettings/ProjectVersion.txt') -Pattern '^m_EditorVersion: (.+)$').Matches[0].Groups[1].Value
$actualVersion = (Get-Item -LiteralPath $UnityPath).VersionInfo.ProductVersion.Split('_')[0]
if ($actualVersion -ne $expectedVersion) {
    throw "This repository requires Unity $expectedVersion; the selected Editor is $actualVersion."
}
$utf8WithoutBom = New-Object Text.UTF8Encoding($false)

foreach ($directory in @($assets, (Join-Path $project 'Packages'), (Join-Path $project 'ProjectSettings'))) {
    New-Item -ItemType Directory -Force -Path $directory | Out-Null
}
Copy-Item -LiteralPath (Join-Path $root 'ProjectSettings/ProjectVersion.txt') -Destination (Join-Path $project 'ProjectSettings/ProjectVersion.txt')

# Keep registry packages local so this isolated check needs no network access.
$dependencies = @{}
foreach ($name in @('com.unity.burst', 'com.unity.collections', 'com.unity.mathematics',
    'com.unity.nuget.mono-cecil', 'com.unity.test-framework', 'com.unity.test-framework.performance', 'com.unity.ext.nunit')) {
    $package = Get-ChildItem -LiteralPath (Join-Path $root 'Library/PackageCache') -Directory |
        Where-Object { $_.Name.StartsWith($name + '@', [StringComparison]::Ordinal) } |
        Select-Object -First 1
    if (!$package) {
        throw "Open the main project with its specified Unity version to populate the $name package cache."
    }
    $dependencies[$name] = 'file:' + $package.FullName.Replace('\', '/')
}
foreach ($name in @('com.unity.modules.imageconversion', 'com.unity.modules.vectorgraphics',
    'com.unity.modules.jsonserialize', 'com.unity.modules.imgui')) {
    $dependencies[$name] = '1.0.0'
}
$manifest = @{ dependencies = $dependencies } | ConvertTo-Json -Depth 4
[IO.File]::WriteAllText((Join-Path $project 'Packages/manifest.json'), $manifest, $utf8WithoutBom)

$uniTask = Join-Path $root 'Assets/Plugins/UniTask/src/Runtime'
if (!(Test-Path -LiteralPath (Join-Path $uniTask 'UniTask.asmdef'))) {
    throw 'Initialize the UniTask submodule with git submodule update --init --recursive.'
}
$uniTaskTarget = Join-Path $assets 'UniTask'
New-Item -ItemType Directory -Force -Path $uniTaskTarget | Out-Null
Get-ChildItem -LiteralPath $uniTask | Where-Object { $_.Name -notin @('External', 'External.meta') } |
    Copy-Item -Destination $uniTaskTarget -Recurse -Force

# Alias only the device capability query in the isolated copy. The AtlasBuilder,
# Texture2D constructor, PackTextures, border scaling and mesh code stay intact.
if ($Baseline) {
    $sourceLines = & git -C $root show 'HEAD:Assets/Scripts/Scenes/Game/Misc/Notes/Skins/CustomSkin.cs'
    if ($LASTEXITCODE -ne 0) {
        throw 'Could not read the committed CustomSkin baseline.'
    }
    $source = ($sourceLines -join "`r`n") + "`r`n"
} else {
    $source = [IO.File]::ReadAllText((Join-Path $root 'Assets/Scripts/Scenes/Game/Misc/Notes/Skins/CustomSkin.cs'))
}
$source = "using SystemInfo = CustomSkinAtlasValidation.DeviceCapabilities;`r`n" + $source
[IO.File]::WriteAllText((Join-Path $assets 'CustomSkin.cs'), $source, $utf8WithoutBom)
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'Validation.cs') -Destination $assets
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'Services.cs') -Destination $assets
[IO.File]::WriteAllText((Join-Path $assets 'csc.rsp'), "-unsafe`r`n", $utf8WithoutBom)

$method = 'CustomSkinAtlasValidation.Validation.Run'
$logName = "validation-$GraphicsApi.log"
if ($Baseline) {
    $method = 'CustomSkinAtlasValidation.Validation.RunBaseline'
    $logName = "validation-baseline-$GraphicsApi.log"
}
$log = Join-Path $project $logName
$process = Start-Process -FilePath $UnityPath -WindowStyle Hidden -ArgumentList @(
    '-batchmode', "-force-$GraphicsApi", '-projectPath', "`"$project`"",
    '-executeMethod', $method, '-logFile', "`"$log`""
) -PassThru
$process.WaitForExit()
if ($process.ExitCode -ne 0 -or !(Test-Path -LiteralPath $log) -or
    !(Select-String -LiteralPath $log -Pattern 'CUSTOM_SKIN_ATLAS_VALIDATION_PASSED' -Quiet)) {
    if (Test-Path -LiteralPath $log) {
        Get-Content -LiteralPath $log -Tail 100
    }
    throw "CustomSkin atlas validation failed. See $log"
}
Select-String -LiteralPath $log -Pattern 'CUSTOM_SKIN_ATLAS_' | ForEach-Object { $_.Line }
Write-Output "CustomSkin atlas validation passed. Log: $log"
