[CmdletBinding()]
param(
    [string]$UnityPath = 'C:/Program Files/Unity Editors/6000.3.17f1/Editor/Unity.exe',
    [ValidateSet('ARM64', 'ARMv7', 'Both')][string]$Architecture = 'Both',
    [int]$TimeoutSeconds = 1800
)

$ErrorActionPreference = 'Stop'

# Unity's JSON readers reject the UTF-8 byte order mark that Windows PowerShell adds for
# "-Encoding UTF8", so generated JSON is written as UTF-8 without a mark on every PowerShell version.
function Set-JsonFile {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true, ValueFromPipeline = $true)][string]$Value,
        [Parameter(Mandatory = $true)][string]$LiteralPath
    )
    process {
        [IO.File]::WriteAllText($LiteralPath, $Value)
    }
}
$root = (Resolve-Path (Join-Path $PSScriptRoot '../..')).Path
$project = [IO.Path]::GetFullPath((Join-Path $root 'Temp/AndroidStorageDeviceValidation'))
$workspacePrefix = $root.TrimEnd([IO.Path]::DirectorySeparatorChar) + [IO.Path]::DirectorySeparatorChar
if (!$project.StartsWith($workspacePrefix, [StringComparison]::OrdinalIgnoreCase)) {
    throw 'The isolated project must remain inside the workspace.'
}
if ((Test-Path -LiteralPath $project) -and ((Get-Item -LiteralPath $project).Attributes -band [IO.FileAttributes]::ReparsePoint)) {
    throw 'The isolated project must not be a reparse point.'
}
$expected = (Select-String -LiteralPath (Join-Path $root 'ProjectSettings/ProjectVersion.txt') -Pattern '^m_EditorVersion: (.+)$').Matches[0].Groups[1].Value
$version = [Diagnostics.FileVersionInfo]::GetVersionInfo($UnityPath).ProductVersion
if ($version -notmatch ('^' + [regex]::Escape($expected) + '(?:_|$)')) {
    throw "Use Unity $expected; the selected executable reports $version."
}
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
$io = Join-Path $project 'Assets/IO'
$android = Join-Path $project 'Assets/Android'
$diagnostics = Join-Path $project 'Assets/Diagnostics'
$editor = Join-Path $project 'Assets/Editor'
$validation = Join-Path $project 'Assets/Validation'
foreach ($directory in @($io, $android, $diagnostics, $editor, $validation, (Join-Path $project 'Packages'), (Join-Path $project 'ProjectSettings'))) {
    New-Item -ItemType Directory -Force -Path $directory | Out-Null
}
Copy-Item -LiteralPath (Join-Path $root 'ProjectSettings/ProjectVersion.txt') -Destination (Join-Path $project 'ProjectSettings/ProjectVersion.txt') -Force
@{ dependencies = @{
    'com.unity.modules.androidjni' = '1.0.0'
    'com.unity.modules.imgui' = '1.0.0'
    'com.unity.modules.jsonserialize' = '1.0.0'
} } | ConvertTo-Json -Depth 4 | Set-JsonFile -LiteralPath (Join-Path $project 'Packages/manifest.json')
$ioSource = Join-Path $root 'Assets/Plugins/MajdataPlay/IO'
$androidSource = Join-Path $root 'Assets/Plugins/MajdataPlay/Platform/Android'
foreach ($name in @('MajdataPlay.IO.asmdef', 'MajdataPlay.IO.asmdef.meta')) {
    Copy-Item -LiteralPath (Join-Path $ioSource $name) -Destination $io -Force
}
Get-ChildItem -LiteralPath (Join-Path $ioSource 'Storage') -Filter '*.cs' -File | Copy-Item -Destination $io -Force
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
Get-ChildItem -LiteralPath (Join-Path $androidSource 'Storage') -Filter '*.cs' -File | Copy-Item -Destination $android -Force
Copy-Item -LiteralPath (Join-Path $androidSource 'IO/KeyCode.cs') -Destination $android -Force
Copy-Item -LiteralPath (Join-Path $androidSource 'IO/AndroidKeyboard.cs') -Destination $android -Force
# Remove only this generated staging directory, after verifying its absolute boundary.
$runtime = [IO.Path]::GetFullPath((Join-Path $android 'Runtime'))
$projectPrefix = $project.TrimEnd([IO.Path]::DirectorySeparatorChar) + [IO.Path]::DirectorySeparatorChar
if (!$runtime.StartsWith($projectPrefix, [StringComparison]::OrdinalIgnoreCase)) {
    throw 'Refusing to replace a staging directory outside the isolated project.'
}
foreach ($ancestor in @($project, (Join-Path $project 'Assets'), $android, $runtime)) {
    if (Test-Path -LiteralPath $ancestor) {
        $item = Get-Item -LiteralPath $ancestor -Force
        if (!$item.PSIsContainer -or ($item.Attributes -band [IO.FileAttributes]::ReparsePoint)) {
            throw "Refusing staged-directory replacement through a file or reparse point: $ancestor"
        }
    }
}
if (Test-Path -LiteralPath $runtime) {
    Remove-Item -LiteralPath $runtime -Recurse -Force
}
if (Test-Path -LiteralPath ($runtime + '.meta') -PathType Leaf) {
    Remove-Item -LiteralPath ($runtime + '.meta') -Force
}
$wrapperProject = Join-Path $project 'Tools/AndroidJavaWrapper'
$wrapperSources = [IO.Path]::GetFullPath((Join-Path $wrapperProject 'src'))
if (!$wrapperSources.StartsWith($projectPrefix, [StringComparison]::OrdinalIgnoreCase)) {
    throw 'Refusing to replace wrapper sources outside the isolated project.'
}
for ($ancestor = $wrapperSources; $ancestor -and ($ancestor.StartsWith($projectPrefix, [StringComparison]::OrdinalIgnoreCase) -or $ancestor -eq $project); $ancestor = Split-Path -Parent $ancestor) {
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
# Include production partial extensions and freshly generated device-probe bindings in one DLL.
$productionSources = Join-Path $root 'Tools/AndroidJavaWrapper/src'
foreach ($file in Get-ChildItem -LiteralPath $productionSources -Recurse -Filter '*.e.cs' -File) {
    $relative = $file.FullName.Substring($productionSources.Length + 1)
    $destination = Join-Path $wrapperSources $relative
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
@{ name = 'MajdataPlay.Diagnostics' } | ConvertTo-Json | Set-JsonFile -LiteralPath (Join-Path $diagnostics 'MajdataPlay.Diagnostics.asmdef')
Copy-Item -LiteralPath (Join-Path $root 'Assets/Plugins/MajdataPlay/Diagnostics/MajdataPlay.Diagnostics.asmdef.meta') -Destination $diagnostics -Force
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'AndroidStorageDeviceValidation/DeviceValidationStubs.cs') -Destination $diagnostics -Force
$toolSource = Join-Path $PSScriptRoot 'AndroidStorageDeviceValidation'
@{
    name = 'MajdataPlay.Storage.DeviceValidation'
    references = @('MajdataPlay.IO', 'MajdataPlay.Platform.Android')
} | ConvertTo-Json | Set-JsonFile -LiteralPath (Join-Path $validation 'MajdataPlay.Storage.DeviceValidation.asmdef')
Copy-Item -LiteralPath (Join-Path $toolSource 'AndroidStorageDeviceValidation.cs') -Destination $validation -Force
$jarAssets = Join-Path $project 'Assets/StreamingAssets/JarValidation'
foreach ($directory in @($jarAssets, (Join-Path $jarAssets 'implicit/nested'), (Join-Path $jarAssets 'names'))) {
    New-Item -ItemType Directory -Force -Path $directory | Out-Null
}
$jarText = "MajdataPlay packaged UTF-8 你好" + [char]10
[IO.File]::WriteAllText((Join-Path $jarAssets 'plain.txt'), $jarText, [Text.UTF8Encoding]::new($false))
[IO.File]::WriteAllText((Join-Path $jarAssets 'names/space # 中文 %20.txt'), $jarText, [Text.UTF8Encoding]::new($false))
$jarPattern = [byte[]]::new(65553)
for ($index = 0; $index -lt $jarPattern.Length; $index++) {
    $jarPattern[$index] = [byte]($index % 251)
}
[IO.File]::WriteAllBytes((Join-Path $jarAssets 'implicit/nested/pattern.bin'), $jarPattern)
[IO.File]::WriteAllBytes((Join-Path $jarAssets 'empty.bin'), [byte[]]::new(0))
@{ name = 'MajdataPlay.Storage.DeviceBuild.Editor'; includePlatforms = @('Editor') } | ConvertTo-Json | Set-JsonFile -LiteralPath (Join-Path $editor 'MajdataPlay.Storage.DeviceBuild.Editor.asmdef')
Copy-Item -LiteralPath (Join-Path $toolSource 'AndroidStorageDeviceBuild.cs') -Destination $editor -Force
$javaTarget = Join-Path $project 'Assets/Plugins/Android/src'
New-Item -ItemType Directory -Force -Path $javaTarget | Out-Null
Copy-Item -LiteralPath (Join-Path $root 'Assets/Plugins/Android/src/java') -Destination $javaTarget -Recurse -Force
Copy-Item -LiteralPath (Join-Path $root 'Assets/Plugins/Android/AndroidManifest.xml') -Destination (Join-Path $project 'Assets/Plugins/Android/AndroidManifest.xml') -Force
foreach ($library in Get-ChildItem -LiteralPath (Join-Path $root 'Assets/Plugins/Android') -Directory -Filter '*.androidlib') {
    Copy-Item -LiteralPath $library.FullName -Destination (Join-Path $project 'Assets/Plugins/Android') -Recurse -Force
    Copy-Item -LiteralPath ($library.FullName + '.meta') -Destination (Join-Path $project 'Assets/Plugins/Android') -Force
}
$probeJava = Join-Path $project 'Assets/Plugins/Android/src/java/net/majdata/validation'
New-Item -ItemType Directory -Force -Path $probeJava | Out-Null
Copy-Item -LiteralPath (Join-Path $toolSource 'DeviceJniProbe.java') -Destination $probeJava -Force
$editorData = Join-Path (Split-Path -Parent $UnityPath) 'Data'
$androidPlayer = Join-Path $editorData 'PlaybackEngines/AndroidPlayer'
$unityArchive = Join-Path $androidPlayer 'Variations/mono/Release/Classes/classes.jar'
if (!(Test-Path -LiteralPath $unityArchive -PathType Leaf)) {
    $unityArchive = Join-Path $androidPlayer 'Variations/il2cpp/Release/Classes/classes.jar'
}
$unityActivity = Join-Path $androidPlayer 'Source/com/unity3d/player/UnityPlayerActivity.java'
foreach ($inputPath in @($unityArchive, $unityActivity)) {
    if (!(Test-Path -LiteralPath $inputPath -PathType Leaf)) {
        throw "Required Unity launcher metadata is missing: $inputPath"
    }
}
$generatorArguments = @($cliAssembly, '--sdk', (Join-Path $androidPlayer 'SDK'), '--output', $wrapperSources,
    '--namespace', 'MajdataPlay.Platform.Android.Runtime', '--no-docs', '--input', $unityArchive, '--input', $unityActivity)
foreach ($file in Get-ChildItem -LiteralPath (Join-Path $project 'Assets/Plugins/Android') -Recurse -File | Sort-Object FullName) {
    if ($file.Extension -in @('.java', '.class', '.jar', '.aar')) {
        $generatorArguments += @('--input', $file.FullName)
    }
}
& dotnet @generatorArguments
if ($LASTEXITCODE -ne 0) {
    throw 'Fresh CLI generation for the isolated Android device project failed.'
}
& dotnet $cliAssembly '--build-wrapper' $project '--unity-data' $editorData
if ($LASTEXITCODE -ne 0) {
    throw 'Standalone wrapper DLL compilation for the isolated Android device project failed.'
}
$variables = @('UNITY_EDITOR_PATH', 'UNITY_ANDROID_SDK', 'UNITY_JAVA_HOME')
$oldValues = @{}
foreach ($name in $variables) {
    $oldValues[$name] = [Environment]::GetEnvironmentVariable($name, 'Process')
}
try {
    $env:UNITY_EDITOR_PATH = $UnityPath
    $env:UNITY_ANDROID_SDK = Join-Path $androidPlayer 'SDK'
    $env:UNITY_JAVA_HOME = Join-Path $androidPlayer 'OpenJDK'
    $log = Join-Path $project 'build.log'
    $apk = Join-Path $project "Artifacts/storage-validation-$Architecture.apk"
    $process = Start-Process -FilePath $UnityPath -WindowStyle Hidden -ArgumentList @(
        '-batchmode', '-nographics', '-buildTarget', 'Android', '-projectPath', ([char]34 + $project + [char]34),
        '-executeMethod', 'MajdataPlay.Tests.AndroidStorageDeviceBuild.Run',
        '-storageArchitecture', $Architecture, '-storageApk', ([char]34 + $apk + [char]34),
        '-logFile', ([char]34 + $log + [char]34)
    ) -PassThru
    $null = $process.Handle
    if (!$process.WaitForExit($TimeoutSeconds * 1000)) {
        throw "Android IL2CPP build exceeded $TimeoutSeconds seconds (PID $($process.Id)); it was not killed. Inspect $log."
    }
    $process.Refresh()
    if ($process.ExitCode -ne 0 -or !(Test-Path -LiteralPath $apk) -or !(Select-String -LiteralPath $log -Pattern 'FILE_SYSTEM_DEVICE_IL2CPP_BUILD_PASSED' -Quiet)) {
        Get-Content -LiteralPath $log -Tail 100
        throw "Android IL2CPP build failed (exit $($process.ExitCode)); see $log."
    }
    Write-Output "FILE_SYSTEM_DEVICE_APK_READY: $apk"
    Write-Output "Build log: $log"
}
finally {
    foreach ($name in $variables) {
        [Environment]::SetEnvironmentVariable($name, $oldValues[$name], 'Process')
    }
}
