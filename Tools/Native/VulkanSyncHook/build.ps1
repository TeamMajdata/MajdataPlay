param(
    [string] $CMake = 'C:/Program Files/CMake/bin/cmake.exe',
    [string] $Configuration = 'Release'
)
$ErrorActionPreference = 'Stop'
$source = (Resolve-Path -LiteralPath $PSScriptRoot).Path
$build = Join-Path $source '.build'
& $CMake -S $source -B $build -G 'Visual Studio 17 2022' -A x64 -DMAJVK_BUILD_TESTS=ON
if ($LASTEXITCODE -ne 0) { throw 'Vulkan sync hook configure failed.' }
& $CMake --build $build --config $Configuration --target MajdataPlayVulkanSyncHook MajdataPlayVulkanSyncHookTests
if ($LASTEXITCODE -ne 0) { throw 'Vulkan sync hook build failed.' }
& (Join-Path $build "$Configuration/MajdataPlayVulkanSyncHookTests.exe")
if ($LASTEXITCODE -ne 0) { throw 'Host-only Vulkan sync hook test failed.' }
$dll = Join-Path $build "$Configuration/MajdataPlayVulkanSyncHook.dll"
if (-not (Test-Path -LiteralPath $dll -PathType Leaf)) { throw 'Vulkan sync hook DLL is missing.' }
$inputs = @('src/VulkanSyncHook.cpp', 'tests/VulkanSyncHookTests.cpp', 'CMakeLists.txt', 'build.ps1', 'README.md')
$hashes = [ordered]@{}
foreach ($inputName in $inputs) {
    $hashes[$inputName] = (Get-FileHash -LiteralPath (Join-Path $source $inputName) -Algorithm SHA256).Hash
}
foreach ($header in @('IUnityInterface.h', 'IUnityGraphics.h', 'IUnityGraphicsVulkan.h')) {
    $hashes["Unity/$header"] = (Get-FileHash -LiteralPath (Join-Path $source "Unity/$header") -Algorithm SHA256).Hash
}
$vulkanHeader = '../../FFmpeg/Native/ThirdParty/Vulkan-Headers/include/vulkan/vulkan_core.h'
$hashes[$vulkanHeader] = (Get-FileHash -LiteralPath (Join-Path $source $vulkanHeader) -Algorithm SHA256).Hash
[ordered]@{
    BuiltAt = (Get-Date).ToString('o')
    Target = 'Windows x64'
    Compiler = 'MSVC /W4 /WX /MT /utf-8'
    CMake = (& $CMake --version | Select-Object -First 1)
    Output = 'MajdataPlayVulkanSyncHook.dll'
    DefaultMode = 'narrow'
    DisableSwitch = 'MAJ_VULKAN_SYNC_HOOK=0'
    SHA256 = (Get-FileHash -LiteralPath $dll -Algorithm SHA256).Hash
    SourceAndHeaderSHA256 = $hashes
    HostTests = 'PASS: fake entry points only; no Vulkan loader, device or GPU work'
    RuntimeValidated = $false
} | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath (Join-Path $build 'build-manifest.json') -Encoding utf8
Write-Output "BUILD ONLY DONE: $dll"
Write-Output 'No Player, Unity Editor or GPU workload was performed.'
