<#
.SYNOPSIS
Builds the MajdataPlay Vulkan pacing layer for Windows and stages it into Assets.

.DESCRIPTION
Cross-compiles MajdataPlayVulkanPacing.dll with llvm-mingw for each requested target, copies it to
Assets/Plugins/MajdataPlay/Rendering/Native/Windows/<cpu>/ and writes a build-manifest.json with the
compiler, the DLL hash and LF-normalized hashes of every source input. Existing Unity .meta files are
left untouched. With -Test, the deterministic layer tests are built for the host and run first.

.PARAMETER Targets
Targets to build: win-x86, win-x64 and/or win-arm64.

.PARAMETER LlvmMingw
Root of an llvm-mingw toolchain. Defaults to LLVM_MINGW, then to the toolchain pinned by
Tools/FFmpeg/dependencies.lock.json under Tools/FFmpeg/.build or FFMPEG_BUILD_ROOT.

.PARAMETER CMake
Path to cmake.exe. Defaults to cmake on PATH, then to the CMake bundled with the project's Unity Editor.

.PARAMETER BuildRoot
Directory for CMake build trees.

.PARAMETER Test
Builds and runs the layer tests for the host before building the targets.

.PARAMETER Probe
After the targets, builds the x64 hardware probe into <BuildRoot>/probe next to a copy of the staged x64 DLL.
Pass -Targets @() to package the probe with the DLL already staged in Assets.
#>
param(
    [string[]]$Targets = @('win-x86', 'win-x64', 'win-arm64'),
    [string]$LlvmMingw = $env:LLVM_MINGW,
    [string]$CMake,
    [string]$BuildRoot = (Join-Path $PSScriptRoot '.build'),
    [switch]$Test,
    [switch]$Probe
)
$ErrorActionPreference = 'Stop'

$repository = (Resolve-Path (Join-Path $PSScriptRoot '..\..\..')).Path
$output = Join-Path $repository 'Assets\Plugins\MajdataPlay\Rendering\Native\Windows'
# Compiler triple, CMake processor and Unity plug-in folder for each target.
$targetSettings = @{
    'win-x86'   = @('i686-w64-mingw32', 'x86', 'x86')
    'win-x64'   = @('x86_64-w64-mingw32', 'x86_64', 'x86_64')
    'win-arm64' = @('aarch64-w64-mingw32', 'aarch64', 'arm64')
}

function Find-LlvmMingw {
    if ($LlvmMingw) {
        return (Resolve-Path $LlvmMingw).Path
    }
    $lock = Get-Content (Join-Path $repository 'Tools\FFmpeg\dependencies.lock.json') -Raw | ConvertFrom-Json
    $name = $lock.llvmMingw.archives.'Windows-x86_64'.file -replace '\.zip$', ''
    $roots = @(Join-Path $repository 'Tools\FFmpeg\.build')
    if ($env:FFMPEG_BUILD_ROOT) {
        $roots += $env:FFMPEG_BUILD_ROOT
    }
    foreach ($root in $roots) {
        $candidate = Join-Path $root "toolchains\$name"
        if (Test-Path (Join-Path $candidate 'bin\x86_64-w64-mingw32-clang++.exe')) {
            return $candidate
        }
    }
    throw "llvm-mingw not found. Set -LlvmMingw or LLVM_MINGW, or fetch $name with Tools/FFmpeg/build-dependencies.py."
}

function Find-CMake {
    if ($CMake) {
        return (Resolve-Path $CMake).Path
    }
    $onPath = Get-Command cmake -ErrorAction SilentlyContinue
    if ($onPath) {
        return $onPath.Source
    }
    $version = (Select-String -Path (Join-Path $repository 'ProjectSettings\ProjectVersion.txt') -Pattern '^m_EditorVersion: (.+)$').Matches[0].Groups[1].Value
    foreach ($editors in @('C:\Program Files\Unity\Hub\Editor', 'C:\Program Files\Unity Editors')) {
        $found = Get-ChildItem -Path (Join-Path $editors "$version\Editor\Data\PlaybackEngines\AndroidPlayer\SDK\cmake\*\bin\cmake.exe") -ErrorAction SilentlyContinue | Select-Object -First 1
        if ($found) {
            return $found.FullName
        }
    }
    throw 'cmake not found. Pass -CMake or install the Unity Android build support that bundles CMake.'
}

function Find-Ninja([string]$CMakePath) {
    $beside = Join-Path (Split-Path $CMakePath) 'ninja.exe'
    if (Test-Path $beside) {
        return $beside
    }
    $onPath = Get-Command ninja -ErrorAction SilentlyContinue
    if ($onPath) {
        return $onPath.Source
    }
    throw 'ninja not found beside cmake or on PATH.'
}

function Invoke-Checked([string]$Executable, [string[]]$Arguments) {
    # Windows PowerShell turns native stderr lines into terminating errors under 'Stop'; judge by exit code instead.
    $previous = $ErrorActionPreference
    $ErrorActionPreference = 'Continue'
    try {
        & $Executable @Arguments 2>&1 | ForEach-Object { "$_" }
    }
    finally {
        $ErrorActionPreference = $previous
    }
    if ($LASTEXITCODE -ne 0) {
        throw "$Executable failed with exit code $LASTEXITCODE"
    }
}

function ConvertTo-CMakePath([string]$Path) {
    # CMake writes cache values into scripts, where backslashes would start escape sequences.
    return $Path.Replace('\', '/')
}

function Get-Sha256([byte[]]$Bytes) {
    $sha = [System.Security.Cryptography.SHA256]::Create()
    try {
        return ([System.BitConverter]::ToString($sha.ComputeHash($Bytes)) -replace '-', '').ToLowerInvariant()
    }
    finally {
        $sha.Dispose()
    }
}

function Get-NormalizedSha256([string]$Path) {
    # Latin-1 maps every byte to one character, so the CRLF replacement keeps all other bytes intact.
    $latin1 = [System.Text.Encoding]::GetEncoding(28591)
    $text = $latin1.GetString([System.IO.File]::ReadAllBytes($Path)).Replace("`r`n", "`n")
    return Get-Sha256 $latin1.GetBytes($text)
}

function Get-SourceHashes {
    $hashes = [ordered]@{}
    $sources = @(Get-Item (Join-Path $PSScriptRoot 'CMakeLists.txt'))
    $sources += Get-ChildItem (Join-Path $PSScriptRoot 'src') -File
    $sources += Get-ChildItem (Join-Path $PSScriptRoot 'Unity') -File -Filter '*.h'
    foreach ($source in ($sources | Sort-Object FullName)) {
        $relative = $source.FullName.Substring($PSScriptRoot.Length + 1).Replace('\', '/')
        $hashes[$relative] = Get-NormalizedSha256 $source.FullName
    }
    return $hashes
}

function Get-VulkanHeaderVersion {
    $header = Join-Path $repository 'Tools\FFmpeg\Native\ThirdParty\Vulkan-Headers\include\vulkan\vulkan_core.h'
    return [int](Select-String -Path $header -Pattern '^#define VK_HEADER_VERSION (\d+)').Matches[0].Groups[1].Value
}

$llvm = Find-LlvmMingw
$cmakePath = Find-CMake
$ninja = Find-Ninja $cmakePath
Write-Host "llvm-mingw: $llvm"
Write-Host "cmake: $cmakePath"

if ($Test) {
    $testBuild = Join-Path $BuildRoot 'tests-x64'
    Invoke-Checked $cmakePath @('-S', $PSScriptRoot, '-B', $testBuild, '-G', 'Ninja', "-DCMAKE_MAKE_PROGRAM=$(ConvertTo-CMakePath $ninja)",
        '-DCMAKE_BUILD_TYPE=Release', '-DCMAKE_SYSTEM_NAME=Windows', '-DCMAKE_SYSTEM_PROCESSOR=x86_64',
        "-DCMAKE_CXX_COMPILER=$(ConvertTo-CMakePath "$llvm\bin\x86_64-w64-mingw32-clang++.exe")", '-DMAJVK_BUILD_TESTS=ON')
    Invoke-Checked $cmakePath @('--build', $testBuild, '--target', 'MajdataPlayVulkanPacingTests')
    Invoke-Checked (Join-Path $testBuild 'MajdataPlayVulkanPacingTests.exe') @()
}

$sourceHashes = Get-SourceHashes
foreach ($target in $Targets) {
    if (-not $targetSettings.ContainsKey($target)) {
        throw "Unknown target $target; use win-x86, win-x64 or win-arm64."
    }
    $triple, $processor, $folder = $targetSettings[$target]
    $compiler = "$llvm\bin\$triple-clang++.exe"
    $build = Join-Path $BuildRoot $target
    $configure = @('-S', $PSScriptRoot, '-B', $build, '-G', 'Ninja', "-DCMAKE_MAKE_PROGRAM=$(ConvertTo-CMakePath $ninja)",
        '-DCMAKE_BUILD_TYPE=Release', '-DCMAKE_SYSTEM_NAME=Windows', "-DCMAKE_SYSTEM_PROCESSOR=$processor",
        "-DCMAKE_CXX_COMPILER=$(ConvertTo-CMakePath $compiler)")
    Invoke-Checked $cmakePath $configure
    Invoke-Checked $cmakePath @('--build', $build, '--target', 'MajdataPlayVulkanPacing')

    $destination = Join-Path $output $folder
    New-Item -ItemType Directory -Force -Path $destination | Out-Null
    $dll = Join-Path $destination 'MajdataPlayVulkanPacing.dll'
    Copy-Item (Join-Path $build 'MajdataPlayVulkanPacing.dll') $dll -Force
    $bytes = [System.IO.File]::ReadAllBytes($dll)
    $manifest = [ordered]@{
        target = $target
        abi = 1
        builtUtc = [DateTime]::UtcNow.ToString('o')
        compiler = @(& $compiler --version)[0]
        cmake = @(& $cmakePath --version)[0]
        vulkanHeaderVersion = Get-VulkanHeaderVersion
        files = @([ordered]@{ file = 'MajdataPlayVulkanPacing.dll'; sha256 = Get-Sha256 $bytes; bytes = $bytes.Length })
        sourceSha256Lf = $sourceHashes
    }
    $json = ($manifest | ConvertTo-Json -Depth 4) -replace "`r`n", "`n"
    [System.IO.File]::WriteAllText((Join-Path $destination 'build-manifest.json'), $json + "`n", (New-Object System.Text.UTF8Encoding $false))
    Write-Host "OK $target -> $dll"
}

if ($Probe) {
    $probeBuild = Join-Path $BuildRoot 'probe'
    Invoke-Checked $cmakePath @('-S', $PSScriptRoot, '-B', $probeBuild, '-G', 'Ninja', "-DCMAKE_MAKE_PROGRAM=$(ConvertTo-CMakePath $ninja)",
        '-DCMAKE_BUILD_TYPE=Release', '-DCMAKE_SYSTEM_NAME=Windows', '-DCMAKE_SYSTEM_PROCESSOR=x86_64',
        "-DCMAKE_CXX_COMPILER=$(ConvertTo-CMakePath "$llvm\bin\x86_64-w64-mingw32-clang++.exe")", '-DMAJVK_BUILD_PROBE=ON')
    Invoke-Checked $cmakePath @('--build', $probeBuild, '--target', 'MajdataPlayVulkanPacingProbe')
    # The probe loads the layer from its own folder; ship the DLL that Unity players get.
    Copy-Item (Join-Path $output 'x86_64\MajdataPlayVulkanPacing.dll') $probeBuild -Force
    Write-Host "PROBE $(Join-Path $probeBuild 'MajdataPlayVulkanPacingProbe.exe')"
}
