[CmdletBinding()]
param(
    [string]$UnityPath = 'C:/Program Files/Unity Editors/6000.3.17f1/Editor/Unity.exe',
    [switch]$SkipAnalyzerBuild
)

$ErrorActionPreference = 'Stop'
$root = (Resolve-Path (Join-Path $PSScriptRoot '../..')).Path
$expected = (Select-String -LiteralPath (Join-Path $root 'ProjectSettings/ProjectVersion.txt') -Pattern '^m_EditorVersion: (.+)$').Matches[0].Groups[1].Value
$version = [Diagnostics.FileVersionInfo]::GetVersionInfo($UnityPath).ProductVersion
if ($version -notmatch ('^' + [regex]::Escape($expected) + '(?:_|$)')) {
    throw "Use Unity $expected; the selected executable reports $version."
}
if (!$SkipAnalyzerBuild) {
    & (Join-Path $root 'Tools/CSharp/AndroidJavaGenerator/build.ps1')
}
$dll = Join-Path $root 'Temp/AndroidJavaGeneratorBuild/bin/netstandard2.0/MajdataPlay.SourceGenerators.AndroidJava.dll'
if (!(Test-Path -LiteralPath $dll -PathType Leaf)) {
    throw 'Build the independent analyzer first.'
}
$project = Join-Path $root 'Temp/AndroidJavaGeneratorUnityValidation'
$runtime = Join-Path $project 'Assets/Runtime'
$editor = Join-Path $project 'Assets/Editor'
$androidEditor = Join-Path $project 'Assets/AndroidEditor'
foreach ($directory in @($runtime, $editor, $androidEditor, (Join-Path $project 'Packages'), (Join-Path $project 'ProjectSettings'))) {
    New-Item -ItemType Directory -Force -Path $directory | Out-Null
}
Copy-Item -LiteralPath (Join-Path $root 'ProjectSettings/ProjectVersion.txt') -Destination (Join-Path $project 'ProjectSettings/ProjectVersion.txt')
@{ dependencies = @{ 'com.unity.modules.androidjni' = '1.0.0' } } | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath (Join-Path $project 'Packages/manifest.json')
@{
    name = 'MajdataPlay.Platform.Android'
    includePlatforms = @('Android', 'Editor')
} | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $runtime 'MajdataPlay.Platform.Android.asmdef')
foreach ($name in @('AndroidJni', 'JavaClassAttribute', 'JavaApiConfigurationAttribute', 'JavaInvocationException')) {
    Copy-Item -LiteralPath (Join-Path $root "Assets/Plugins/MajdataPlay/Platform/Android/$name.cs") -Destination $runtime
}
Copy-Item -LiteralPath (Join-Path $root 'Assets/Plugins/MajdataPlay/Platform/Android/Runtime/Java/Lang/JavaObject.cs') -Destination $runtime
Copy-Item -LiteralPath $dll -Destination $runtime
Copy-Item -LiteralPath (Join-Path $root 'Tools/CSharp/AndroidJavaGenerator/MajdataPlay.SourceGenerators.AndroidJava.dll.meta') -Destination $runtime
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'AndroidJavaGeneratorUnityValidation.cs') -Destination $editor
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'AndroidJavaGeneratorUnityBindings.cs') -Destination $runtime
@{
    name = 'MajdataPlay.Platform.Android.Editor'
    references = @('MajdataPlay.Platform.Android')
    includePlatforms = @('Editor')
} | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $androidEditor 'MajdataPlay.Platform.Android.Editor.asmdef')
Copy-Item -LiteralPath (Join-Path $root 'Assets/Plugins/MajdataPlay/Platform/Android/Editor/JavaWrapperRefresh.cs') -Destination $androidEditor
$editorData = Join-Path (Split-Path -Parent $UnityPath) 'Data'
$variables = @('UNITY_ANDROID_SDK', 'UNITY_JAVA_HOME', 'MAJDATA_JAVA_EXTRACTOR')
$oldValues = @{}
foreach ($name in $variables) {
    $oldValues[$name] = [Environment]::GetEnvironmentVariable($name, 'Process')
}
try {
    $env:UNITY_ANDROID_SDK = Join-Path $editorData 'PlaybackEngines/AndroidPlayer/SDK'
    $env:UNITY_JAVA_HOME = Join-Path $editorData 'PlaybackEngines/AndroidPlayer/OpenJDK'
    $env:MAJDATA_JAVA_EXTRACTOR = Join-Path $root 'Tools/CSharp/AndroidJavaGenerator/Java/JavaApiExtractor.java'
    $log = Join-Path $project 'validation.log'
    $process = Start-Process -FilePath $UnityPath -WindowStyle Hidden -ArgumentList @(
        '-batchmode', '-nographics', '-projectPath', "`"$project`"", '-executeMethod',
        'MajdataPlay.Tests.AndroidJavaGeneratorUnityValidation.Run', '-logFile', "`"$log`""
    ) -PassThru
    # Retain the native process handle so ExitCode remains available after a fast exit.
    $null = $process.Handle
    if (!$process.WaitForExit(300000)) {
        # Leave the process and its log available for inspection rather than killing an unknown editor state.
        throw "Isolated Unity validation exceeded five minutes (PID $($process.Id)). Inspect $log."
    }
    $process.Refresh()
    Write-Output "Isolated Unity exit code: $($process.ExitCode)"
    if ($null -eq $process.ExitCode -or $process.ExitCode -ne 0 -or !(Select-String -LiteralPath $log -Pattern 'ANDROID_JAVA_GENERATOR_UNITY_PASSED' -Quiet)) {
        Get-Content -LiteralPath $log -Tail 100
        throw "Isolated Unity generator validation failed. See $log"
    }
    Select-String -LiteralPath $log -Pattern 'ANDROID_JAVA_GENERATOR_UNITY_' | ForEach-Object { $_.Line }
    Write-Output "Unity generator validation passed. Log: $log"
}
finally {
    foreach ($name in $variables) {
        [Environment]::SetEnvironmentVariable($name, $oldValues[$name], 'Process')
    }
}
