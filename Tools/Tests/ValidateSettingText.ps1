param(
    [string]$UnityPath = 'C:/Program Files/Unity Editors/6000.3.17f1/Editor/Unity.exe'
)

$ErrorActionPreference = 'Stop'
$root = (Resolve-Path (Join-Path $PSScriptRoot '../..')).Path
$project = Join-Path $root 'Temp/SettingTextValidation'
$assets = Join-Path $project 'Assets'
$package = Get-ChildItem (Join-Path $root 'Library/PackageCache') -Directory -Filter 'com.unity.ugui@*' | Select-Object -First 1
if (!$package) { throw 'Open the project in Unity once to populate the local uGUI package cache.' }
foreach ($directory in @($assets, (Join-Path $assets 'Fonts'), (Join-Path $project 'Packages'), (Join-Path $project 'ProjectSettings'))) {
    New-Item -ItemType Directory -Force -Path $directory | Out-Null
}
Copy-Item (Join-Path $root 'ProjectSettings/ProjectVersion.txt') (Join-Path $project 'ProjectSettings/ProjectVersion.txt')
@{
    dependencies = @{
        'com.unity.ugui' = 'file:' + $package.FullName.Replace('\', '/')
        'com.unity.modules.ui' = '1.0.0'
        'com.unity.modules.imgui' = '1.0.0'
    }
} | ConvertTo-Json -Depth 4 | Set-Content (Join-Path $project 'Packages/manifest.json')
$resources = Join-Path $project 'EssentialResources'
New-Item -ItemType Directory -Force -Path $resources | Out-Null
& tar -xzf (Join-Path $package.FullName 'Package Resources/TMP Essential Resources.unitypackage') -C $resources
if ($LASTEXITCODE -ne 0) { throw 'Could not unpack TMP Essential Resources.' }
foreach ($entry in Get-ChildItem $resources -Directory) {
    $pathname = Join-Path $entry.FullName 'pathname'
    if (!(Test-Path $pathname)) { continue }
    $relative = (Get-Content $pathname -Raw).Trim()
    $target = [IO.Path]::GetFullPath((Join-Path $project $relative))
    if (!$target.StartsWith($project + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) {
        throw "Invalid resource path: $relative"
    }
    $asset = Join-Path $entry.FullName 'asset'
    if (Test-Path $asset -PathType Leaf) {
        New-Item -ItemType Directory -Force -Path (Split-Path $target) | Out-Null
        Copy-Item $asset $target
    } else {
        New-Item -ItemType Directory -Force -Path $target | Out-Null
    }
    $meta = Join-Path $entry.FullName 'asset.meta'
    if (Test-Path $meta) { Copy-Item $meta ($target + '.meta') }
}
Copy-Item (Join-Path $root 'Assets/Fonts/General/AlimamaFangYuanTiVF-Thin-2.ttf') (Join-Path $assets 'Fonts/SettingValidation.ttf')
foreach ($source in @('SettingTextTint.cs', 'SettingFontWarmup.cs', 'MenuTitleDisplayer.cs')) {
    Copy-Item (Join-Path $root "Assets/Scripts/Scenes/Setting/$source") $assets
}
Copy-Item (Join-Path $PSScriptRoot 'SettingTextValidation.cs') $assets
$log = Join-Path $project 'validation.log'
$process = Start-Process -FilePath $UnityPath -WindowStyle Hidden -ArgumentList @(
    '-batchmode', '-nographics', '-quit', '-projectPath', "`"$project`"",
    '-executeMethod', 'SettingTextValidation.Run', '-logFile', "`"$log`""
) -PassThru
$process.WaitForExit()
if ($process.ExitCode -ne 0 -or !(Select-String -Path $log -Pattern 'SETTING_TEXT_VALIDATION_PASSED' -Quiet)) {
    Get-Content $log -Tail 100
    throw "Setting text validation failed. See $log"
}
Select-String -Path $log -Pattern 'SETTING_TEXT_' | ForEach-Object { $_.Line }
Write-Output "Setting text validation passed. Log: $log"
