param(
    [string] $CMake = 'C:/Program Files/CMake/bin/cmake.exe'
)
$ErrorActionPreference = 'Stop'
$source = (Resolve-Path -LiteralPath $PSScriptRoot).Path
$build = Join-Path $source '.build'
$release = Join-Path $build 'Release'

& $CMake -S $source -B $build -G 'Visual Studio 17 2022' -A x64
if ($LASTEXITCODE -ne 0) { throw 'Sync-layer configure failed.' }
& $CMake --build $build --config Release --target MajdataPlaySyncLayer SyncLayerStateTests
if ($LASTEXITCODE -ne 0) { throw 'Sync-layer build failed.' }

& (Join-Path $release 'SyncLayerStateTests.exe')
if ($LASTEXITCODE -ne 0) { throw 'Host-only sync-layer state test failed.' }

$dll = Join-Path $release 'VkLayer_majdataplay_sync.dll'
if (-not (Test-Path -LiteralPath $dll -PathType Leaf)) { throw 'Sync-layer DLL is missing.' }

# The loader only sees layers whose manifest sits next to the DLL, so both are staged in
# one directory that can be used directly as VK_LAYER_PATH.
$manifestName = 'VkLayer_majdataplay_sync.json'
$manifest = Join-Path $source $manifestName
Copy-Item -LiteralPath $manifest -Destination (Join-Path $release $manifestName) -Force

# Export check is evidence only: it is skipped when dumpbin cannot be located, but a
# dumpbin run that really is missing an entry point fails the build.
$required = @('vkNegotiateLoaderLayerInterfaceVersion', 'vkGetInstanceProcAddr', 'vkGetDeviceProcAddr',
    'msl_copy_calls', 'msl_barrier_calls', 'msl_skipped_calls', 'msl_errors', 'msl_mode')
$exports = @()
$dumpbin = $null
$toolsetRoot = 'C:\Program Files\Microsoft Visual Studio\2022\Community\VC\Tools\MSVC'
if (Test-Path -LiteralPath $toolsetRoot) {
    $dumpbin = Get-ChildItem -LiteralPath $toolsetRoot -Directory |
        Sort-Object -Property Name -Descending |
        ForEach-Object { Join-Path $_.FullName 'bin/Hostx64/x64/dumpbin.exe' } |
        Where-Object { Test-Path -LiteralPath $_ } |
        Select-Object -First 1
}
if ($dumpbin) {
    $lines = & $dumpbin /nologo /exports $dll
    if ($LASTEXITCODE -ne 0) { throw "dumpbin failed on $dll." }
    foreach ($line in $lines) {
        if ($line -match '^\s+\d+\s+[0-9A-Fa-f]+\s+[0-9A-Fa-f]+\s+(\S+)\s*$') { $exports += $Matches[1] }
    }
    foreach ($name in $required) {
        if ($exports -notcontains $name) { throw "Export '$name' is missing from $dll." }
    }
    Write-Output "EXPORTS: $($exports -join ', ')"
} else {
    Write-Output 'EXPORTS: skipped (dumpbin not found next to the Visual Studio 2022 toolset).'
}

$inputs = @('SyncLayer.cpp', 'CPUtests.cpp', 'CMakeLists.txt', 'build.ps1', 'README.md', $manifestName)
$hashes = [ordered]@{}
foreach ($inputName in $inputs) {
    $hashes[$inputName] = (Get-FileHash -LiteralPath (Join-Path $source $inputName) -Algorithm SHA256).Hash
}
$repository = (Resolve-Path -LiteralPath (Join-Path $source '../../../../../..')).Path
$vulkanHeader = 'Tools/FFmpeg/Native/ThirdParty/Vulkan-Headers/include/vulkan/vulkan_core.h'
$hashes[$vulkanHeader] = (Get-FileHash -LiteralPath (Join-Path $repository $vulkanHeader) -Algorithm SHA256).Hash
$hashes[$manifestName] = (Get-FileHash -LiteralPath (Join-Path $release $manifestName) -Algorithm SHA256).Hash

[ordered]@{
    BuiltAt = (Get-Date).ToString('o'); Target = 'Windows x64'
    Compiler = 'MSVC 14.43.34808, Visual Studio 2022 Community; /W4 /WX /MT /utf-8'
    CMake = (& $CMake --version | Select-Object -First 1)
    Filename = 'VkLayer_majdataplay_sync.dll'
    LayerName = 'VK_LAYER_MAJDATAPLAY_SYNC'
    Manifest = $manifestName
    LayerApiVersion = '1.1.0'
    ModeEnvironment = 'MAJ_SYNC_LAYER_MODE'
    DisableEnvironment = 'MAJDATAPLAY_SYNC_DISABLE'
    Exports = $exports
    SHA256 = (Get-FileHash -LiteralPath $dll -Algorithm SHA256).Hash
    SourceAndHeaderSHA256 = $hashes
    HostStateTests = 'PASS: fake function pointers only; no Vulkan loader, device or GPU workload'
    RuntimeValidated = $false; Installed = $false
} | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath (Join-Path $build 'build-manifest.json') -Encoding utf8
Copy-Item -LiteralPath (Join-Path $build 'build-manifest.json') -Destination (Join-Path $release 'build-manifest.json') -Force
Write-Output "BUILD ONLY DONE: $dll"
Write-Output "Layer manifest staged for VK_LAYER_PATH: $(Join-Path $release $manifestName)"
Write-Output 'No gfxrecon-replay, Player, Unity Editor, GPU workload or installation was performed.'
