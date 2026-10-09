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
foreach ($name in @('MajdataPlay.Platform.Android.asmdef', 'MajdataPlay.Platform.Android.asmdef.meta', 'AndroidRuntime.cs', 'AndroidJni.cs', 'JavaObject.cs', 'JavaInvocationException.cs')) {
    Copy-Item -LiteralPath (Join-Path $androidSource $name) -Destination $android
}
Get-ChildItem -LiteralPath (Join-Path $androidSource 'Storage') -Filter '*.cs' -File | Copy-Item -Destination $android
@{ name = 'MajdataPlay.Diagnostics' } | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $diagnostics 'MajdataPlay.Diagnostics.asmdef')
Copy-Item -LiteralPath (Join-Path $root 'Assets/Plugins/MajdataPlay/Diagnostics/MajdataPlay.Diagnostics.asmdef.meta') -Destination $diagnostics
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'FileSystemUnityValidationStubs.cs') -Destination $diagnostics
@{
    name = 'MajdataPlay.Storage.Tests.Editor'
    references = @('MajdataPlay.IO', 'MajdataPlay.Platform.Android')
    includePlatforms = @('Editor')
} | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $editor 'MajdataPlay.Storage.Tests.Editor.asmdef')
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'FileSystemUnityValidation.cs') -Destination $editor

if (!$SkipJavaCompilation) {
    $androidPlayer = Join-Path (Split-Path -Parent $UnityPath) 'Data/PlaybackEngines/AndroidPlayer'
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
