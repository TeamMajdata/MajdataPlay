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
# Build only the independent CLI using its pinned .NET SDK.
$cliDirectory = Join-Path $root 'Tools/CSharp/AndroidJavaWrapperGenerator'
Push-Location -LiteralPath $cliDirectory
try {
    & dotnet build 'AndroidJavaWrapperGenerator.csproj' --configuration Release --nologo
    if ($LASTEXITCODE -ne 0) {
        throw 'The independent Android Java wrapper CLI build failed.'
    }
}
finally {
    Pop-Location
}
$cliAssembly = Join-Path $cliDirectory 'bin/Release/net9.0/AndroidJavaWrapperGenerator.dll'
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
foreach ($name in @('MajdataPlay.Platform.Android.asmdef', 'AndroidRuntime.cs')) {
    $source = Join-Path $androidSource $name
    Copy-Item -LiteralPath $source -Destination $android -Force
    Copy-Item -LiteralPath "$source.meta" -Destination $android -Force
}
foreach ($name in @('AndroidJni.cs', 'JavaInvocationException.cs', 'JavaClassAttribute.cs', 'JavaApiConfigurationAttribute.cs', 'MajdataPlay.SourceGenerators.AndroidJava.dll')) {
    foreach ($obsolete in @((Join-Path $android $name), (Join-Path $android "$name.meta"))) {
        if (Test-Path -LiteralPath $obsolete -PathType Leaf) {
            Remove-Item -LiteralPath $obsolete -Force
        }
    }
}
Get-ChildItem -LiteralPath (Join-Path $androidSource 'Storage') -Filter '*.cs' -File | Copy-Item -Destination $android
# Stage the production keyboard callback and enum so startup wiring is not replaced by a double.
foreach ($name in @('IO/KeyCode.cs', 'IO/KeyCode.cs.meta', 'IO/AndroidKeyboard.cs', 'IO/AndroidKeyboard.cs.meta')) {
    Copy-Item -LiteralPath (Join-Path $androidSource $name) -Destination $android -Force
}
# Replace only the verified Runtime staging directory so obsolete bindings cannot survive a prior run.
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
if (Test-Path -LiteralPath $stagedRuntime) {
    Remove-Item -LiteralPath $stagedRuntime -Recurse -Force
}
if (Test-Path -LiteralPath ($stagedRuntime + '.meta') -PathType Leaf) {
    Remove-Item -LiteralPath ($stagedRuntime + '.meta') -Force
}
# Compile generated bindings and production partial extensions outside Unity's Assets tree.
$wrapperProject = Join-Path $project 'Tools/AndroidJavaWrapper'
$wrapperSources = [IO.Path]::GetFullPath((Join-Path $wrapperProject 'src'))
if (!$wrapperSources.StartsWith($projectBoundary, [StringComparison]::OrdinalIgnoreCase)) {
    throw "The wrapper sources must remain inside the isolated project: $wrapperSources"
}
for ($ancestor = $wrapperSources; $ancestor -and ($ancestor.StartsWith($projectBoundary, [StringComparison]::OrdinalIgnoreCase) -or $ancestor -eq $projectFullPath); $ancestor = Split-Path -Parent $ancestor) {
    if (Test-Path -LiteralPath $ancestor) {
        $item = Get-Item -LiteralPath $ancestor -Force
        if (!$item.PSIsContainer -or ($item.Attributes -band [IO.FileAttributes]::ReparsePoint)) {
            throw "Refusing wrapper source replacement through a file or reparse point: $ancestor"
        }
    }
}
if (Test-Path -LiteralPath $wrapperSources) {
    Remove-Item -LiteralPath $wrapperSources -Recurse -Force
}
New-Item -ItemType Directory -Force -Path $wrapperSources | Out-Null
$productionSources = Join-Path $root 'Tools/AndroidJavaWrapper/src'
foreach ($file in Get-ChildItem -LiteralPath $productionSources -Recurse -Filter '*.e.cs' -File) {
    $destination = Join-Path $wrapperSources $file.FullName.Substring($productionSources.Length + 1)
    New-Item -ItemType Directory -Force -Path (Split-Path -Parent $destination) | Out-Null
    Copy-Item -LiteralPath $file.FullName -Destination $destination -Force
}
foreach ($name in @('AndroidJavaWrapper.csproj', 'global.json')) {
    Copy-Item -LiteralPath (Join-Path $root "Tools/AndroidJavaWrapper/$name") -Destination $wrapperProject -Force
}
$isolatedCli = Join-Path $project 'Tools/CSharp/AndroidJavaWrapperGenerator'
New-Item -ItemType Directory -Force -Path $isolatedCli | Out-Null
foreach ($name in @('AndroidJavaWrapperGenerator.csproj', 'global.json')) {
    Copy-Item -LiteralPath (Join-Path $cliDirectory $name) -Destination $isolatedCli -Force
}
# Stage production Java sources in the isolated project before CLI analysis.
$javaSource = Join-Path $root 'Assets/Plugins/Android'
foreach ($file in Get-ChildItem -LiteralPath $javaSource -Recurse -File) {
    if ($file.Extension -notin @('.java', '.class', '.jar', '.aar')) {
        continue
    }
    $relative = $file.FullName.Substring($javaSource.Length + 1)
    $destination = Join-Path (Join-Path $project 'Assets/Plugins/Android') $relative
    New-Item -ItemType Directory -Force -Path (Split-Path -Parent $destination) | Out-Null
    Copy-Item -LiteralPath $file.FullName -Destination $destination -Force
    if (Test-Path -LiteralPath ($file.FullName + '.meta') -PathType Leaf) {
        Copy-Item -LiteralPath ($file.FullName + '.meta') -Destination ($destination + '.meta') -Force
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
$unityJar = Join-Path $androidPlayer 'Variations/mono/Release/Classes/classes.jar'
if (!(Test-Path -LiteralPath $unityJar -PathType Leaf)) {
    $unityJar = Join-Path $androidPlayer 'Variations/il2cpp/Release/Classes/classes.jar'
}
$unityActivity = Join-Path $androidPlayer 'Source/com/unity3d/player/UnityPlayerActivity.java'
foreach ($inputFile in @($unityJar, $unityActivity)) {
    if (!(Test-Path -LiteralPath $inputFile -PathType Leaf)) {
        throw "Missing Unity launcher metadata: $inputFile"
    }
}
$generatorArguments = @($cliAssembly, '--sdk', (Join-Path $androidPlayer 'SDK'), '--output', $wrapperSources,
    '--namespace', 'MajdataPlay.Platform.Android.Runtime', '--no-docs', '--input', $unityJar, '--input', $unityActivity)
foreach ($file in Get-ChildItem -LiteralPath (Join-Path $project 'Assets/Plugins/Android') -Recurse -File | Sort-Object FullName) {
    if ($file.Extension -in @('.java', '.class', '.jar', '.aar')) {
        $generatorArguments += @('--input', $file.FullName)
    }
}
& dotnet @generatorArguments
if ($LASTEXITCODE -ne 0) {
    throw 'Fresh CLI binding generation for the isolated storage project failed.'
}
& dotnet $cliAssembly '--build-wrapper' $project '--unity-data' $editorData
if ($LASTEXITCODE -ne 0) {
    throw 'Standalone wrapper DLL compilation for the isolated storage project failed.'
}
if (!$SkipJavaCompilation) {
    $javac = Join-Path $androidPlayer 'OpenJDK/bin/javac.exe'
    $androidJar = Join-Path $androidPlayer 'SDK/platforms/android-36/android.jar'
    foreach ($inputFile in @($javac, $androidJar, $unityJar, $unityActivity)) {
        if (!(Test-Path -LiteralPath $inputFile -PathType Leaf)) {
            throw "Missing Android Java validation dependency: $inputFile"
        }
    }
    $javaOutput = Join-Path $project 'Temp/JavaClasses'
    New-Item -ItemType Directory -Force -Path $javaOutput | Out-Null
    $javaSources = @(Get-ChildItem -LiteralPath (Join-Path $root 'Assets/Plugins/Android/src/java') -Filter '*.java' -File -Recurse | Select-Object -ExpandProperty FullName)
    $javaSources += Join-Path $PSScriptRoot 'AndroidStorageDeviceValidation/DeviceJniProbe.java'
    $javaSources += $unityActivity
    & $javac '-J-Duser.language=en' '-J-Duser.country=US' '-encoding' 'UTF-8' '--release' '11' '-Xlint:all' '-classpath' "$androidJar;$unityJar" '-d' $javaOutput @javaSources
    if ($LASTEXITCODE -ne 0) {
        throw 'Android Java bridge compilation failed.'
    }
    Write-Output 'FILE_SYSTEM_JAVA_COMPILE_PASSED (API 36 references; no Android runtime validation).'
}
$variables = @('UNITY_EDITOR_PATH', 'UNITY_ANDROID_SDK', 'UNITY_JAVA_HOME')
$oldValues = @{}
foreach ($name in $variables) {
    $oldValues[$name] = [Environment]::GetEnvironmentVariable($name, 'Process')
}
try {
    # Keep the selected Editor and its Android tools consistent for optional Player script compilation.
    $env:UNITY_EDITOR_PATH = $UnityPath
    $env:UNITY_ANDROID_SDK = Join-Path $androidPlayer 'SDK'
    $env:UNITY_JAVA_HOME = Join-Path $androidPlayer 'OpenJDK'
    $targetArgument = if ($PlayerTargets.Count -eq 0) { 'none' } else { $PlayerTargets -join ',' }
    $log = Join-Path $project 'validation.log'
    $process = Start-Process -FilePath $UnityPath -WindowStyle Hidden -ArgumentList @(
        '-batchmode', '-nographics', '-projectPath', ([char]34 + $project + [char]34), '-executeMethod',
        'MajdataPlay.Tests.FileSystemUnityValidation.Run', '-storageValidationTargets', $targetArgument,
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
