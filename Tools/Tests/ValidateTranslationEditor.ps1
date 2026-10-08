param(
    [string]$UnityPath = 'C:/Program Files/Unity Editors/6000.3.17f1/Editor/Unity.exe'
)

$ErrorActionPreference = 'Stop'
$root = (Resolve-Path (Join-Path $PSScriptRoot '../..')).Path
$project = Join-Path $root 'Temp/TranslationEditorValidation'
$assets = Join-Path $project 'Assets'
$editor = Join-Path $assets 'Editor'
$fixtures = Join-Path $assets 'Fixtures'
$resources = Join-Path $assets 'Resources/Langs'
$package = Get-ChildItem (Join-Path $root 'Library/PackageCache') -Directory -Filter 'com.unity.nuget.newtonsoft-json@*' | Select-Object -First 1
if (!$package) {
    throw 'Open the main project in Unity once to populate its Newtonsoft JSON package cache.'
}
$version = [Diagnostics.FileVersionInfo]::GetVersionInfo($UnityPath).ProductVersion
if ($version -notmatch '^6000\.3\.17f1(?:_|$)') {
    throw "The repository requires Unity 6000.3.17f1; the selected executable reports $version."
}
foreach ($directory in @($editor, $fixtures, $resources, (Join-Path $project 'Packages'), (Join-Path $project 'ProjectSettings'))) {
    New-Item -ItemType Directory -Force -Path $directory | Out-Null
}
Copy-Item (Join-Path $root 'ProjectSettings/ProjectVersion.txt') (Join-Path $project 'ProjectSettings/ProjectVersion.txt')
@{
    dependencies = @{
        'com.unity.nuget.newtonsoft-json' = 'file:' + $package.FullName.Replace('\', '/')
        'com.unity.modules.imgui' = '1.0.0'
        'com.unity.modules.ui' = '1.0.0'
        'com.unity.modules.uielements' = '1.0.0'
    }
} | ConvertTo-Json -Depth 4 | Set-Content (Join-Path $project 'Packages/manifest.json')
foreach ($source in @('TranslationCallAnalyzer.cs', 'TranslationSettingAnalyzer.cs', 'TranslationDocument.cs', 'TranslationManagerWindow.cs')) {
    Copy-Item (Join-Path $root "Assets/Scripts/Editor/Windows/$source") $editor
}
foreach ($source in @('ExecutionGuard.cs', 'LocalizationFixtures.cs', 'CallFixtures.cs', 'SettingMetadataFixtures.cs', 'SettingFixtures.cs', 'OptionEnumeratorFixtures.cs', 'OptionFieldFlowFixtures.cs')) {
    Copy-Item (Join-Path $PSScriptRoot "TranslationValidation/$source") $fixtures
}
Get-ChildItem (Join-Path $root 'Assets/Resources/Langs') -File -Filter '*.json' | ForEach-Object {
    Copy-Item -LiteralPath $_.FullName -Destination $resources
}
Copy-Item (Join-Path $PSScriptRoot 'TranslationEditorValidation.cs') $editor
$log = Join-Path $project 'validation.log'
$process = Start-Process -FilePath $UnityPath -WindowStyle Hidden -ArgumentList @(
    '-batchmode', '-projectPath', "`"$project`"",
    '-executeMethod', 'MajdataPlay.Editor.Windows.TranslationEditorValidation.Run', '-logFile', "`"$log`""
) -PassThru
$process.WaitForExit()
if ($process.ExitCode -ne 0 -or !(Select-String -LiteralPath $log -Pattern 'TRANSLATION_EDITOR_VALIDATION_PASSED' -Quiet)) {
    Get-Content -LiteralPath $log -Tail 100
    throw "Translation editor validation failed. See $log"
}
Select-String -LiteralPath $log -Pattern 'TRANSLATION_EDITOR_' | ForEach-Object { $_.Line }
Write-Output "Translation editor validation passed. Log: $log"
