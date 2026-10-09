[CmdletBinding()]
param(
    [string]$UnityPath = 'C:/Program Files/Unity Editors/6000.3.17f1/Editor/Unity.exe',
    [string[]]$PlayerTargets = @('StandaloneWindows64', 'Android'),
    [switch]$SkipJavaCompilation
)

$ErrorActionPreference = 'Stop'
$root = (Resolve-Path (Join-Path $PSScriptRoot '../..')).Path
$expected = (Select-String -LiteralPath (Join-Path $root 'ProjectSettings/ProjectVersion.txt') -Pattern '^m_EditorVersion: (.+)$').Matches[0].Groups[1].Value
$version = [Diagnostics.FileVersionInfo]::GetVersionInfo($UnityPath).ProductVersion
if ($version -notmatch ('^' + [regex]::Escape($expected) + '(?:_|$)')) {
    throw "Use Unity $expected; the selected executable reports $version."
}
# Install the production analyzer before copying it into the isolated compilation.
& (Join-Path $root 'Tools/AndroidJavaGenerator/build.ps1') -Install -UnityPath $UnityPath
$project = Join-Path $root 'Temp/FileSystemUnityValidation'
$io = Join-Path $project 'Assets/IO'
$android = Join-Path $project 'Assets/Android'
$diagnostics = Join-Path $project 'Assets/Diagnostics'
$editor = Join-Path $project 'Assets/Editor'
foreach ($directory in @($io, $android, $diagnostics, $editor, (Join-Path $project 'Packages'), (Join-Path $project 'ProjectSettings'))) {
    New-Item -ItemType Directory -Force -Path $directory | Out-Null
}
Copy-Item -LiteralPath (Join-Path $root 'ProjectSettings/ProjectVersion.txt') -Destination (Join-Path $project 'ProjectSettings/ProjectVersion.txt')
@{ dependencies = @{ 'com.unity.modules.androidjni' = '1.0.0' } } | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath (Join-Path $project 'Packages/manifest.json')
$ioSource = Join-Path $root 'Assets/Plugins/MajdataPlay/IO'
$androidSource = Join-Path $root 'Assets/Plugins/MajdataPlay/Platform/Android'
foreach ($name in @('MajdataPlay.IO.asmdef', 'MajdataPlay.IO.asmdef.meta')) {
    Copy-Item -LiteralPath (Join-Path $ioSource $name) -Destination $io
}
Get-ChildItem -LiteralPath (Join-Path $ioSource 'Storage') -Filter '*.cs' -File | Copy-Item -Destination $io
$analyzerName = 'MajdataPlay.SourceGenerators.AndroidJava.dll'
$analyzerMeta = Join-Path $androidSource "$analyzerName.meta"
if (!(Select-String -LiteralPath $analyzerMeta -Pattern '^\s*-\s+RoslynAnalyzer\s*$' -Quiet)) {
    throw 'The installed Android Java generator must have the RoslynAnalyzer label.'
}
foreach ($name in @('MajdataPlay.Platform.Android.asmdef', 'AndroidRuntime.cs', 'AndroidJni.cs', 'JavaInvocationException.cs', 'JavaClassAttribute.cs', 'JavaApiConfigurationAttribute.cs', $analyzerName)) {
    $source = Join-Path $androidSource $name
    Copy-Item -LiteralPath $source -Destination $android -Force
    Copy-Item -LiteralPath "$source.meta" -Destination $android -Force
}
Get-ChildItem -LiteralPath (Join-Path $androidSource 'Storage') -Filter '*.cs' -File | Copy-Item -Destination $android
# Stage the real enum, but retain the narrow AndroidKeyboard double used by storage validation.
foreach ($name in @('IO/KeyCode.cs', 'IO/KeyCode.cs.meta')) {
    Copy-Item -LiteralPath (Join-Path $androidSource $name) -Destination $android -Force
}
# Copy-Item merges directories. Remove only obsolete legacy files from an earlier isolated run.
$projectFullPath = [IO.Path]::GetFullPath($project)
$projectBoundary = $projectFullPath.TrimEnd([IO.Path]::DirectorySeparatorChar, [IO.Path]::AltDirectorySeparatorChar) + [IO.Path]::DirectorySeparatorChar
$stagedRuntime = [IO.Path]::GetFullPath((Join-Path $android 'Runtime'))
if (!$stagedRuntime.StartsWith($projectBoundary, [StringComparison]::OrdinalIgnoreCase)) {
    throw "The staged Runtime directory must remain inside the isolated project: $stagedRuntime"
}
foreach ($directory in @($root, (Join-Path $root 'Temp'), $projectFullPath, (Join-Path $projectFullPath 'Assets'), $android, $stagedRuntime)) {
    if (Test-Path -LiteralPath $directory) {
        $item = Get-Item -LiteralPath $directory -Force
        if (!$item.PSIsContainer -or ($item.Attributes -band [IO.FileAttributes]::ReparsePoint)) {
            throw "Refusing legacy cleanup through a non-directory or reparse point: $directory"
        }
    }
}
foreach ($name in @('Activity.cs', 'Activity.cs.meta', 'Intent.cs', 'Intent.cs.meta', 'KeyEvent.cs', 'KeyEvent.cs.meta')) {
    $legacyPath = [IO.Path]::GetFullPath((Join-Path $stagedRuntime $name))
    if (!$legacyPath.StartsWith($projectBoundary, [StringComparison]::OrdinalIgnoreCase) -or
        ![string]::Equals([IO.Path]::GetDirectoryName($legacyPath), $stagedRuntime, [StringComparison]::OrdinalIgnoreCase)) {
        throw "Refusing legacy cleanup outside the staged Runtime root: $legacyPath"
    }
    if (Test-Path -LiteralPath $legacyPath) {
        $item = Get-Item -LiteralPath $legacyPath -Force
        if ($item.PSIsContainer -or ($item.Attributes -band [IO.FileAttributes]::ReparsePoint)) {
            throw "Refusing legacy cleanup of a directory or reparse point: $legacyPath"
        }
        Remove-Item -LiteralPath $legacyPath -Force
    }
}
# Keep all production categories and their asset metadata; generated code stays in Roslyn.
Copy-Item -LiteralPath (Join-Path $androidSource 'Runtime') -Destination $android -Recurse -Force
Copy-Item -LiteralPath (Join-Path $androidSource 'Runtime.meta') -Destination $android -Force
# Relative Java inputs must resolve inside this project, not against the main checkout.
foreach ($relativePath in @('Tools/AndroidJavaGenerator/Java/JavaApiExtractor.java', 'Assets/Plugins/Android/src/java/net/majdata/majdataplay/StorageAccess.java')) {
    $source = Join-Path $root $relativePath
    $destination = Join-Path $project $relativePath
    New-Item -ItemType Directory -Force -Path (Split-Path -Parent $destination) | Out-Null
    Copy-Item -LiteralPath $source -Destination $destination -Force
    if (Test-Path -LiteralPath "$source.meta" -PathType Leaf) {
        Copy-Item -LiteralPath "$source.meta" -Destination "$destination.meta" -Force
    }
}
@{ name = 'MajdataPlay.Diagnostics' } | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $diagnostics 'MajdataPlay.Diagnostics.asmdef')
Copy-Item -LiteralPath (Join-Path $root 'Assets/Plugins/MajdataPlay/Diagnostics/MajdataPlay.Diagnostics.asmdef.meta') -Destination $diagnostics
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'FileSystemUnityValidationStubs.cs') -Destination $diagnostics
@{
    name = 'MajdataPlay.Storage.Tests.Editor'
    references = @('MajdataPlay.IO', 'MajdataPlay.Platform.Android')
    includePlatforms = @('Editor')
} | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $editor 'MajdataPlay.Storage.Tests.Editor.asmdef')
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'FileSystemUnityValidation.cs') -Destination $editor

$editorData = Join-Path (Split-Path -Parent $UnityPath) 'Data'
$androidPlayer = Join-Path $editorData 'PlaybackEngines/AndroidPlayer'
if (!$SkipJavaCompilation) {
    $javac = Join-Path $androidPlayer 'OpenJDK/bin/javac.exe'
    $androidJar = Join-Path $androidPlayer 'SDK/platforms/android-36/android.jar'
    $unityJar = Join-Path $androidPlayer 'Variations/mono/Release/Classes/classes.jar'
    $unityActivity = Join-Path $androidPlayer 'Source/com/unity3d/player/UnityPlayerActivity.java'
    foreach ($inputFile in @($javac, $androidJar, $unityJar, $unityActivity)) {
        if (!(Test-Path -LiteralPath $inputFile -PathType Leaf)) {
            throw "Missing Android Java validation dependency: $inputFile"
        }
    }
    $javaOutput = Join-Path $project 'Temp/JavaClasses'
    New-Item -ItemType Directory -Force -Path $javaOutput | Out-Null
    $javaSources = @(Get-ChildItem -LiteralPath (Join-Path $root 'Assets/Plugins/Android/src/java') -Filter '*.java' -File -Recurse | Select-Object -ExpandProperty FullName)
    $javaSources += $unityActivity
    & $javac '-J-Duser.language=en' '-J-Duser.country=US' '-encoding' 'UTF-8' '--release' '11' '-Xlint:all' '-classpath' "$androidJar;$unityJar" '-d' $javaOutput @javaSources
    if ($LASTEXITCODE -ne 0) {
        throw 'Android Java bridge compilation failed.'
    }
    Write-Output 'FILE_SYSTEM_JAVA_COMPILE_PASSED (API 36 references; no Android runtime validation).'
}
$variables = @('UNITY_EDITOR_PATH', 'UNITY_ANDROID_SDK', 'UNITY_JAVA_HOME', 'MAJDATA_JAVA_EXTRACTOR')
$oldValues = @{}
foreach ($name in $variables) {
    $oldValues[$name] = [Environment]::GetEnvironmentVariable($name, 'Process')
}
try {
    # Resolve {UnityData} and extraction tools from the selected editor, including non-Android targets.
    $env:UNITY_EDITOR_PATH = $UnityPath
    $env:UNITY_ANDROID_SDK = Join-Path $androidPlayer 'SDK'
    $env:UNITY_JAVA_HOME = Join-Path $androidPlayer 'OpenJDK'
    $env:MAJDATA_JAVA_EXTRACTOR = Join-Path $project 'Tools/AndroidJavaGenerator/Java/JavaApiExtractor.java'
    $log = Join-Path $project 'validation.log'
    $process = Start-Process -FilePath $UnityPath -WindowStyle Hidden -ArgumentList @(
        '-batchmode', '-nographics', '-projectPath', ([char]34 + $project + [char]34), '-executeMethod',
        'MajdataPlay.Tests.FileSystemUnityValidation.Run', '-storageValidationTargets', ($PlayerTargets -join ','),
        '-logFile', ([char]34 + $log + [char]34)
    ) -PassThru
    $null = $process.Handle
    if (!$process.WaitForExit(300000)) {
        throw "Isolated Unity validation exceeded five minutes (PID $($process.Id)). Inspect $log; the process was not killed."
    }
    $process.Refresh()
    if ($null -eq $process.ExitCode -or $process.ExitCode -ne 0 -or !(Select-String -LiteralPath $log -Pattern 'FILE_SYSTEM_UNITY_PASSED' -Quiet)) {
        Get-Content -LiteralPath $log -Tail 100
        throw "File system Unity validation failed (exit $($process.ExitCode)). See $log"
    }
    Select-String -LiteralPath $log -Pattern 'FILE_SYSTEM_' | ForEach-Object { $_.Line }
    Write-Output "Isolated Unity validation passed. Log: $log"
}
finally {
    foreach ($name in $variables) {
        [Environment]::SetEnvironmentVariable($name, $oldValues[$name], 'Process')
    }
}
