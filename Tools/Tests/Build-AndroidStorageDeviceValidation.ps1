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
& (Join-Path $root 'Tools/CSharp/AndroidJavaGenerator/build.ps1') -Install -UnityPath $UnityPath
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
foreach ($name in @('MajdataPlay.Platform.Android.asmdef', 'AndroidRuntime.cs', 'AndroidJni.cs', 'JavaInvocationException.cs', 'JavaClassAttribute.cs', 'JavaApiConfigurationAttribute.cs', 'MajdataPlay.SourceGenerators.AndroidJava.dll')) {
    $source = Join-Path $androidSource $name
    Copy-Item -LiteralPath $source -Destination $android -Force
    Copy-Item -LiteralPath "$source.meta" -Destination $android -Force
}
Get-ChildItem -LiteralPath (Join-Path $androidSource 'Storage') -Filter '*.cs' -File | Copy-Item -Destination $android -Force
Copy-Item -LiteralPath (Join-Path $androidSource 'IO/KeyCode.cs') -Destination $android -Force
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
Copy-Item -LiteralPath (Join-Path $androidSource 'Runtime') -Destination $android -Recurse -Force
Copy-Item -LiteralPath (Join-Path $androidSource 'Runtime.meta') -Destination $android -Force
@{ name = 'MajdataPlay.Diagnostics' } | ConvertTo-Json | Set-JsonFile -LiteralPath (Join-Path $diagnostics 'MajdataPlay.Diagnostics.asmdef')
Copy-Item -LiteralPath (Join-Path $root 'Assets/Plugins/MajdataPlay/Diagnostics/MajdataPlay.Diagnostics.asmdef.meta') -Destination $diagnostics -Force
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'AndroidStorageDeviceValidation/DeviceValidationStubs.cs') -Destination $diagnostics -Force
$toolSource = Join-Path $PSScriptRoot 'AndroidStorageDeviceValidation'
@{
    name = 'MajdataPlay.Storage.DeviceValidation'
    references = @('MajdataPlay.IO', 'MajdataPlay.Platform.Android')
} | ConvertTo-Json | Set-JsonFile -LiteralPath (Join-Path $validation 'MajdataPlay.Storage.DeviceValidation.asmdef')
Copy-Item -LiteralPath (Join-Path $toolSource 'AndroidStorageDeviceValidation.cs') -Destination $validation -Force
$probeDirectory = Join-Path $runtime 'Validation'
New-Item -ItemType Directory -Force -Path $probeDirectory | Out-Null
Copy-Item -LiteralPath (Join-Path $toolSource 'DeviceJniProbe.cs') -Destination $probeDirectory -Force
@{ name = 'MajdataPlay.Storage.DeviceBuild.Editor'; includePlatforms = @('Editor') } | ConvertTo-Json | Set-JsonFile -LiteralPath (Join-Path $editor 'MajdataPlay.Storage.DeviceBuild.Editor.asmdef')
Copy-Item -LiteralPath (Join-Path $toolSource 'AndroidStorageDeviceBuild.cs') -Destination $editor -Force
$javaTarget = Join-Path $project 'Assets/Plugins/Android/src'
New-Item -ItemType Directory -Force -Path $javaTarget | Out-Null
Copy-Item -LiteralPath (Join-Path $root 'Assets/Plugins/Android/src/java') -Destination $javaTarget -Recurse -Force
Copy-Item -LiteralPath (Join-Path $root 'Assets/Plugins/Android/AndroidManifest.xml') -Destination (Join-Path $project 'Assets/Plugins/Android/AndroidManifest.xml') -Force
$probeJava = Join-Path $project 'Assets/Plugins/Android/src/java/net/majdata/validation'
New-Item -ItemType Directory -Force -Path $probeJava | Out-Null
Copy-Item -LiteralPath (Join-Path $toolSource 'DeviceJniProbe.java') -Destination $probeJava -Force
$extractor = Join-Path $project 'Tools/CSharp/AndroidJavaGenerator/Java'
New-Item -ItemType Directory -Force -Path $extractor | Out-Null
Copy-Item -LiteralPath (Join-Path $root 'Tools/CSharp/AndroidJavaGenerator/Java/JavaApiExtractor.java') -Destination $extractor -Force
$editorData = Join-Path (Split-Path -Parent $UnityPath) 'Data'
$androidPlayer = Join-Path $editorData 'PlaybackEngines/AndroidPlayer'
$variables = @('UNITY_EDITOR_PATH', 'UNITY_ANDROID_SDK', 'UNITY_JAVA_HOME', 'MAJDATA_JAVA_EXTRACTOR')
$oldValues = @{}
foreach ($name in $variables) {
    $oldValues[$name] = [Environment]::GetEnvironmentVariable($name, 'Process')
}
try {
    $env:UNITY_EDITOR_PATH = $UnityPath
    $env:UNITY_ANDROID_SDK = Join-Path $androidPlayer 'SDK'
    $env:UNITY_JAVA_HOME = Join-Path $androidPlayer 'OpenJDK'
    $env:MAJDATA_JAVA_EXTRACTOR = Join-Path $extractor 'JavaApiExtractor.java'
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
