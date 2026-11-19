<#
.SYNOPSIS
Builds the MajdataPlay Vulkan texture upload plug-in for a Windows x64 player and stages it into Assets.

.DESCRIPTION
Compiles MajdataPlayTextureUpload.dll with CMake, Ninja and the installed MSVC toolchain, copies it to
Assets/Plugins/MajdataPlay/TextureUpload/Native/Windows/x86_64/ and writes a build-manifest.json with the
compiler, the DLL hash and LF-normalized hashes of every source input. Existing Unity .meta files are left
untouched. With -Test, the deterministic staging ring tests are built and run for the host first.

.PARAMETER Cl
Path to cl.exe. Defaults to the newest MSVC toolset installed below -VisualStudio.

.PARAMETER VcVars
Path to vcvars64.bat. Defaults to the one of the Visual Studio installation that provides Cl.

.PARAMETER CMake
Path to cmake.exe. Defaults to cmake on PATH, then to the CMake bundled with Visual Studio.

.PARAMETER Ninja
Path to ninja.exe. Defaults to ninja on PATH, then to the Ninja bundled with Visual Studio.

.PARAMETER VisualStudio
Root of a Visual Studio installation used to discover cl.exe, vcvars64.bat and Ninja.

.PARAMETER WindowsSdk
Windows SDK version to build against. Defaults to the newest one under C:\Program Files (x86)\Windows Kits\10.

.PARAMETER BuildRoot
Directory for CMake build trees.

.PARAMETER Test
Builds and runs the staging ring tests for the host before building the plug-in.
#>
param(
    [string]$Cl,
    [string]$VcVars,
    [string]$CMake,
    [string]$Ninja,
    [string]$VisualStudio = 'C:\Program Files\Microsoft Visual Studio\2022\Community',
    [string]$WindowsSdk,
    [string]$BuildRoot = (Join-Path $PSScriptRoot '.build'),
    [switch]$Test
)
$ErrorActionPreference = 'Stop'

$repository = (Resolve-Path (Join-Path $PSScriptRoot '..\..\..')).Path
$output = Join-Path $repository 'Assets\Plugins\MajdataPlay\TextureUpload\Native\Windows\x86_64'

function Find-Cl {
    if ($Cl) {
        return (Resolve-Path $Cl).Path
    }
    $toolsets = Get-ChildItem -Path (Join-Path $VisualStudio 'VC\Tools\MSVC') -Directory -ErrorAction SilentlyContinue |
        Sort-Object -Property Name -Descending
    foreach ($toolset in $toolsets) {
        $candidate = Join-Path $toolset.FullName 'bin\Hostx64\x64\cl.exe'
        if (Test-Path $candidate) {
            return $candidate
        }
    }
    throw "cl.exe not found below $VisualStudio. Pass -Cl or -VisualStudio."
}

function Find-VcVars {
    if ($VcVars) {
        return (Resolve-Path $VcVars).Path
    }
    $candidate = Join-Path $VisualStudio 'VC\Auxiliary\Build\vcvars64.bat'
    if (Test-Path $candidate) {
        return $candidate
    }
    throw "vcvars64.bat not found below $VisualStudio. Pass -VcVars or -VisualStudio."
}

function Find-CMake {
    if ($CMake) {
        return (Resolve-Path $CMake).Path
    }
    $onPath = Get-Command cmake -ErrorAction SilentlyContinue
    if ($onPath) {
        return $onPath.Source
    }
    $bundled = Join-Path $VisualStudio 'Common7\IDE\CommonExtensions\Microsoft\CMake\CMake\bin\cmake.exe'
    if (Test-Path $bundled) {
        return $bundled
    }
    throw 'cmake not found. Pass -CMake or install CMake.'
}

function Find-Ninja([string]$CMakePath) {
    if ($Ninja) {
        return (Resolve-Path $Ninja).Path
    }
    $beside = Join-Path (Split-Path $CMakePath) 'ninja.exe'
    if (Test-Path $beside) {
        return $beside
    }
    $bundled = Join-Path $VisualStudio 'Common7\IDE\CommonExtensions\Microsoft\CMake\Ninja\ninja.exe'
    if (Test-Path $bundled) {
        return $bundled
    }
    $onPath = Get-Command ninja -ErrorAction SilentlyContinue
    if ($onPath) {
        return $onPath.Source
    }
    throw 'ninja not found. Pass -Ninja or install Ninja.'
}

function Find-WindowsSdk {
    if ($WindowsSdk) {
        return $WindowsSdk
    }
    $root = 'C:\Program Files (x86)\Windows Kits\10\Lib'
    $versions = Get-ChildItem -Path $root -Directory -ErrorAction SilentlyContinue |
        Where-Object { $_.Name -match '^10\.\d+\.\d+\.\d+$' } |
        Sort-Object -Property { [version]$_.Name } -Descending
    if (-not $versions) {
        throw "No Windows SDK found below $root."
    }
    return $versions[0].Name
}

function Import-MsvcEnvironment {
    # vcvars64.bat provides INCLUDE/LIB/PATH for the Windows SDK and the MSVC runtime. Its environment is
    # imported into this session once so the build below runs the tools directly and their output stays visible.
    $vcvars = Find-VcVars
    $lines = & cmd.exe /c "call `"$vcvars`" >nul && set"
    foreach ($line in $lines) {
        if ($line -match '^([^=]+)=(.*)$') {
            Set-Item -Path ("env:" + $matches[1]) -Value $matches[2]
        }
    }
}

function Invoke-Tool([string[]]$Command) {
    # Runs a tool and fails on a non-zero exit code. Judging by exit code avoids PowerShell turning native stderr
    # lines into terminating errors under ErrorActionPreference 'Stop'.
    $executable = $Command[0]
    $arguments = @($Command | Select-Object -Skip 1)
    $previous = $ErrorActionPreference
    $ErrorActionPreference = 'Continue'
    try {
        & $executable @arguments 2>&1 | ForEach-Object { "$_" }
    }
    finally {
        $ErrorActionPreference = $previous
    }
    if ($LASTEXITCODE -ne 0) {
        throw "$executable failed with exit code $LASTEXITCODE"
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

$clPath = Find-Cl
$cmakePath = Find-CMake
$ninjaPath = Find-Ninja $cmakePath
Write-Host "cl:        $clPath"
Write-Host "cmake:     $cmakePath"
Write-Host "ninja:     $ninjaPath"
Import-MsvcEnvironment

$common = @('-G', 'Ninja', "-DCMAKE_MAKE_PROGRAM=$(ConvertTo-CMakePath $ninjaPath)", '-DCMAKE_BUILD_TYPE=Release',
    "-DCMAKE_CXX_COMPILER=$(ConvertTo-CMakePath $clPath)")

if ($Test) {
    $testBuild = Join-Path $BuildRoot 'tests-x64'
    Invoke-Tool (@($cmakePath, '-S', $PSScriptRoot, '-B', $testBuild) + $common + @('-DMAJVKU_BUILD_TESTS=ON'))
    Invoke-Tool @($cmakePath, '--build', $testBuild, '--target', 'MajdataPlayTextureUploadTests')
    & (Join-Path $testBuild 'MajdataPlayTextureUploadTests.exe')
    if ($LASTEXITCODE -ne 0) {
        throw "MajdataPlayTextureUploadTests failed with exit code $LASTEXITCODE"
    }
}

$build = Join-Path $BuildRoot 'win-x64'
Invoke-Tool (@($cmakePath, '-S', $PSScriptRoot, '-B', $build) + $common)
Invoke-Tool @($cmakePath, '--build', $build, '--target', 'MajdataPlayTextureUpload')

New-Item -ItemType Directory -Force -Path $output | Out-Null
$dll = Join-Path $output 'MajdataPlayTextureUpload.dll'
Copy-Item (Join-Path $build 'MajdataPlayTextureUpload.dll') $dll -Force

$bytes = [System.IO.File]::ReadAllBytes($dll)
# Read the compiler version from the file instead of cl.exe's banner, which is localized on some hosts.
$clVersion = (Get-Item $clPath).VersionInfo
$manifest = [ordered]@{
    target = 'win-x64'
    abi = 1
    builtUtc = [DateTime]::UtcNow.ToString('o')
    compiler = 'MSVC ' + $clVersion.ProductVersion + ' (' + $clVersion.FileVersion + ')'
    compilerPath = $clPath
    cmake = @(& $cmakePath --version)[0]
    windowsSdk = Find-WindowsSdk
    vulkanHeaderVersion = Get-VulkanHeaderVersion
    files = @([ordered]@{ file = 'MajdataPlayTextureUpload.dll'; sha256 = Get-Sha256 $bytes; bytes = $bytes.Length })
    sourceSha256Lf = Get-SourceHashes
}
$json = ($manifest | ConvertTo-Json -Depth 4) -replace "`r`n", "`n"
[System.IO.File]::WriteAllText((Join-Path $output 'build-manifest.json'), $json + "`n", (New-Object System.Text.UTF8Encoding $false))
Write-Host "OK win-x64 -> $dll"
