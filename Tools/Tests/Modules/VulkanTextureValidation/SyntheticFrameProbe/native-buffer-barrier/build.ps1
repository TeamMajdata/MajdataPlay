param(
    [string] $CMake = 'C:/Program Files/CMake/bin/cmake.exe'
)
$ErrorActionPreference = 'Stop'
$source = (Resolve-Path -LiteralPath $PSScriptRoot).Path
$build = Join-Path $source '.build'
& $CMake -S $source -B $build -G 'Visual Studio 17 2022' -A x64
if ($LASTEXITCODE -ne 0) { throw 'Buffer-barrier probe configure failed.' }
& $CMake --build $build --config Release --target MajBufferBarrierProbe BufferBarrierStateTests
if ($LASTEXITCODE -ne 0) { throw 'Buffer-barrier probe build failed.' }
& (Join-Path $build 'Release/BufferBarrierStateTests.exe')
if ($LASTEXITCODE -ne 0) { throw 'Host-only buffer-barrier state test failed.' }
$dll = Join-Path $build 'Release/MajdataPlayVulkanPacing.dll'
if (-not (Test-Path -LiteralPath $dll -PathType Leaf)) { throw 'Buffer-barrier probe DLL is missing.' }
$inputs = @('Probe.cpp', 'CPUtests.cpp', 'CMakeLists.txt', 'build.ps1', 'README.md')
$hashes = [ordered]@{}
foreach ($inputName in $inputs) {
    $hashes[$inputName] = (Get-FileHash -LiteralPath (Join-Path $source $inputName) -Algorithm SHA256).Hash
}
$repository = (Resolve-Path -LiteralPath (Join-Path $source '../../../../../..')).Path
foreach ($header in @('IUnityInterface.h', 'IUnityGraphics.h', 'IUnityGraphicsVulkan.h')) {
    $relative = 'Tools/FFmpeg/Native/Unity/' + $header
    $hashes[$relative] = (Get-FileHash -LiteralPath (Join-Path $repository $relative) -Algorithm SHA256).Hash
}
$vulkanHeader = 'Tools/FFmpeg/Native/ThirdParty/Vulkan-Headers/include/vulkan/vulkan_core.h'
$hashes[$vulkanHeader] = (Get-FileHash -LiteralPath (Join-Path $repository $vulkanHeader) -Algorithm SHA256).Hash
[ordered]@{
    BuiltAt = (Get-Date).ToString('o'); ABI = 1; Target = 'Windows x64'
    Compiler = 'MSVC 14.43.34808, Visual Studio 2022 Community; /W4 /WX /MT'
    CMake = (& $CMake --version | Select-Object -First 1)
    Filename = 'MajdataPlayVulkanPacing.dll'
    Identity = 'New buffer-visibility diagnostic probe under a preexisting Unity loader filename; not the original pacing layer and not the mf queue-fence marker'
    SHA256 = (Get-FileHash -LiteralPath $dll -Algorithm SHA256).Hash
    SourceAndHeaderSHA256 = $hashes
    HostStateTests = 'PASS: fake functions only; no Vulkan loader or GPU workload'
    RuntimeValidated = $false; Installed = $false
} | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath (Join-Path $build 'build-manifest.json') -Encoding utf8
Write-Output "BUILD ONLY DONE: $dll"
Write-Output 'No Player, Unity Editor, GPU workload or installation was performed.'
