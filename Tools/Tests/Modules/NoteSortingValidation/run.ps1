param(
    [string]$UnityPath = 'C:/Program Files/Unity Editors/6000.3.17f1/Editor/Unity.exe',
    [switch]$SkipRendering
)

$ErrorActionPreference = 'Stop'
$root = (Resolve-Path (Join-Path $PSScriptRoot '../../../..')).Path
$project = Join-Path $root 'Temp/NoteSortingValidation'
$assets = Join-Path $project 'Assets'
if (!(Test-Path -LiteralPath $UnityPath -PathType Leaf)) {
    throw "Unity Editor not found: $UnityPath"
}
$expectedVersion = (Select-String -LiteralPath (Join-Path $root 'ProjectSettings/ProjectVersion.txt') -Pattern '^m_EditorVersion: (.+)$').Matches[0].Groups[1].Value
$actualVersion = (Get-Item -LiteralPath $UnityPath).VersionInfo.ProductVersion.Split('_')[0]
if ($actualVersion -ne $expectedVersion) {
    throw "This repository requires Unity $expectedVersion; the selected Editor is $actualVersion."
}
foreach ($directory in @($assets, (Join-Path $project 'Packages'), (Join-Path $project 'ProjectSettings'))) {
    New-Item -ItemType Directory -Force -Path $directory | Out-Null
}
Copy-Item -LiteralPath (Join-Path $root 'ProjectSettings/ProjectVersion.txt') -Destination (Join-Path $project 'ProjectSettings/ProjectVersion.txt')
Copy-Item -LiteralPath (Join-Path $root 'ProjectSettings/TagManager.asset') -Destination (Join-Path $project 'ProjectSettings/TagManager.asset')
$encoding = New-Object System.Text.UTF8Encoding($false)
$manifest = @{ dependencies = @{ 'com.unity.modules.animation' = '1.0.0' } } | ConvertTo-Json -Depth 4
[IO.File]::WriteAllText((Join-Path $project 'Packages/manifest.json'), $manifest, $encoding)
foreach ($source in @('Assets/Scripts/Scenes/Game/Utils/NoteHelper.cs',
    'Assets/Scripts/IO/Base/Enums/SensorArea.cs',
    'Assets/Scripts/IO/Base/Enums/SensorGroup.cs',
    'Assets/Scripts/Scenes/Game/Misc/Notes/Slide/SlideOKShape.cs')) {
    Copy-Item -LiteralPath (Join-Path $root $source) -Destination $assets
}
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'Validation.cs') -Destination $assets
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'Services.cs') -Destination $assets
$animationRoot = [IO.Path]::GetFullPath((Join-Path $root 'Assets/Animation'))
$animationFiles = @()
foreach ($directory in @('NoteEffects', 'TouchEffects', 'OutlineEffect')) {
    $animationFiles += Get-ChildItem -LiteralPath (Join-Path $animationRoot $directory) -File -Filter '*.anim' -Recurse
}
$animationFiles += Get-Item -LiteralPath (Join-Path $animationRoot 'UI/TouchFeedbackAnim.anim')
foreach ($file in $animationFiles) {
    # Windows PowerShell 5.1 has no Path.GetRelativePath; every file is enumerated below the normalized root.
    $relative = $file.FullName.Substring($animationRoot.Length).TrimStart('\', '/')
    $target = Join-Path $assets (Join-Path 'Animation' $relative)
    New-Item -ItemType Directory -Force -Path (Split-Path $target) | Out-Null
    Copy-Item -LiteralPath $file.FullName -Destination $target
    Copy-Item -LiteralPath ($file.FullName + '.meta') -Destination ($target + '.meta')
}
$log = Join-Path $project 'validation.log'
$method = 'NoteSortingValidation.Validation.Run'
$graphics = '-force-d3d11'
if ($SkipRendering) {
    $method = 'NoteSortingValidation.Validation.RunWithoutRendering'
    $graphics = '-nographics'
}
$process = Start-Process -FilePath $UnityPath -WindowStyle Hidden -ArgumentList @(
    '-batchmode', $graphics, '-quit', '-projectPath', "`"$project`"",
    '-executeMethod', $method, '-logFile', "`"$log`""
) -PassThru
$process.WaitForExit()
if ($process.ExitCode -ne 0 -or !(Test-Path -LiteralPath $log) -or
    !(Select-String -LiteralPath $log -Pattern 'NOTE_SORTING_VALIDATION_PASSED' -Quiet)) {
    if (Test-Path -LiteralPath $log) {
        Get-Content -LiteralPath $log -Tail 100
    }
    throw "Note sorting validation failed. See $log"
}
if (!$SkipRendering -and !(Select-String -LiteralPath $log -Pattern 'NOTE_SORTING_RENDERING_PASSED' -Quiet)) {
    throw "Note sorting rendering did not finish. See $log"
}
Select-String -LiteralPath $log -Pattern 'NOTE_SORTING_' | ForEach-Object { $_.Line }
Write-Output "Note sorting validation passed. Log: $log"
