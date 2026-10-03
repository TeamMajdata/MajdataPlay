param(
    [Parameter(Mandatory = $true)][string]$Ndk,
    [ValidateSet('arm64-v8a', 'armeabi-v7a', 'x86_64')]
    [string[]]$Abi = @('arm64-v8a', 'armeabi-v7a', 'x86_64'),
    [int]$ApiLevel = 23,
    [string]$CMake = 'cmake',
    [string]$Ninja = 'ninja',
    [string]$OutputDirectory = "$PSScriptRoot/../../../Build/VLCUnityPortable/Android",
    [switch]$Install
)
$ErrorActionPreference = 'Stop'
function Write-FolderMetadata([string]$Directory) {
    $metadata = "$Directory.meta"
    if (Test-Path -LiteralPath $metadata) { return }
    $guid = [Guid]::NewGuid().ToString('N')
    @"
fileFormatVersion: 2
guid: $guid
folderAsset: yes
DefaultImporter:
  externalObjects: {}
  userData:
  assetBundleName:
  assetBundleVariant:
"@ | Set-Content -LiteralPath $metadata -Encoding utf8
}
function Write-AndroidPluginMetadata([string]$Library, [string]$Architecture) {
    $metadata = "$Library.meta"
    if (Test-Path -LiteralPath $metadata) { return }
    $guid = [Guid]::NewGuid().ToString('N')
    @"
fileFormatVersion: 2
guid: $guid
PluginImporter:
  externalObjects: {}
  serializedVersion: 2
  iconMap: {}
  executionOrder: {}
  defineConstraints: []
  isPreloaded: 1
  isOverridable: 0
  isExplicitlyReferenced: 0
  validateReferences: 1
  platformData:
  - first:
      Any:
    second:
      enabled: 0
      settings: {}
  - first:
      Editor: Editor
    second:
      enabled: 0
      settings:
        DefaultValueInitialized: true
  - first:
      Android: Android
    second:
      enabled: 1
      settings:
        CPU: $Architecture
  userData:
  assetBundleName:
  assetBundleVariant:
"@ | Set-Content -LiteralPath $metadata -Encoding utf8
}
$ndkPath = (Resolve-Path -LiteralPath $Ndk).Path
$toolchain = Join-Path $ndkPath 'build/cmake/android.toolchain.cmake'
if (!(Test-Path -LiteralPath $toolchain)) { throw "Not an Android NDK: $ndkPath" }
if ($ApiLevel -lt 21) { throw 'The portable bridge requires Android API 21 or newer.' }
$outputRoot = [IO.Path]::GetFullPath($OutputDirectory)
$ninjaPath = (Get-Command $Ninja -ErrorAction Stop).Source
foreach ($targetAbi in $Abi) {
    $build = Join-Path $outputRoot $targetAbi
    & $CMake -S $PSScriptRoot -B $build -G Ninja "-DCMAKE_MAKE_PROGRAM=$ninjaPath" "-DCMAKE_TOOLCHAIN_FILE=$toolchain" "-DANDROID_ABI=$targetAbi" "-DANDROID_PLATFORM=android-$ApiLevel" '-DANDROID_STL=c++_static' '-DCMAKE_BUILD_TYPE=Release'
    if ($LASTEXITCODE -ne 0) { throw "CMake configuration failed for $targetAbi." }
    & $CMake --build $build --config Release
    if ($LASTEXITCODE -ne 0) { throw "Android bridge build failed for $targetAbi." }
    $library = Join-Path $build 'libVLCUnityPlugin.so'
    if (!(Test-Path -LiteralPath $library)) { throw "Missing bridge output: $library" }
    if ($Install) {
        $destination = [IO.Path]::GetFullPath("$PSScriptRoot/../../../Assets/Plugins/VLCUnity/Runtime/Plugins/Android/libs/$targetAbi")
        New-Item -ItemType Directory -Force -Path $destination | Out-Null
        Copy-Item -LiteralPath $library -Destination (Join-Path $destination 'libVLCUnityPlugin.so')
        Write-FolderMetadata $destination
        $libraryDirectory = Split-Path -Parent $destination
        Write-FolderMetadata $libraryDirectory
        Write-FolderMetadata (Split-Path -Parent $libraryDirectory)
        $cpu = switch ($targetAbi) { 'arm64-v8a' { 'ARM64' }; 'armeabi-v7a' { 'ARMv7' }; 'x86_64' { 'X86_64' } }
        Write-AndroidPluginMetadata (Join-Path $destination 'libVLCUnityPlugin.so') $cpu
    }
    Write-Output $library
}
Write-Output 'The bridge is built; matching LibVLC decoder/module binaries must still be supplied for every ABI.'
