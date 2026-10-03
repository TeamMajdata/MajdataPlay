param([string]$OutputDirectory = "$PSScriptRoot/../../../Build/VLCUnityPortableHostTests")
$ErrorActionPreference = 'Stop'
$vswhere = Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio/Installer/vswhere.exe'
$visualStudio = & $vswhere -latest -products '*' -requires Microsoft.VisualStudio.Component.VC.Tools.x86.x64 -property installationPath
if (!$visualStudio) { throw 'Visual Studio C++ x64 build tools are required for the Windows host test.' }
$toolset = Get-ChildItem (Join-Path $visualStudio 'VC/Tools/MSVC') -Directory | Sort-Object Name -Descending | Select-Object -First 1
$sdkRoot = Join-Path ${env:ProgramFiles(x86)} 'Windows Kits/10'
$sdk = Get-ChildItem (Join-Path $sdkRoot 'Include') -Directory | Sort-Object Name -Descending | Select-Object -First 1
$output = [IO.Path]::GetFullPath($OutputDirectory)
New-Item -ItemType Directory -Force -Path $output | Out-Null
$priorInclude = $env:INCLUDE
$priorLib = $env:LIB
try {
    $env:INCLUDE = @((Join-Path $toolset.FullName 'include'), (Join-Path $sdk.FullName 'ucrt'), (Join-Path $sdk.FullName 'shared'), (Join-Path $sdk.FullName 'um')) -join ';'
    $env:LIB = @((Join-Path $toolset.FullName 'lib/x64'), (Join-Path $sdkRoot "Lib/$($sdk.Name)/ucrt/x64"), (Join-Path $sdkRoot "Lib/$($sdk.Name)/um/x64")) -join ';'
    $compiler = Join-Path $toolset.FullName 'bin/Hostx64/x64/cl.exe'
    & $compiler /nologo /utf-8 /std:c++17 /EHsc /W4 /WX /O2 /MT /DVLCUNITY_STATIC_LIBVLC=1 "/Fo$output/" "$PSScriptRoot/PortableRenderingPlugin.cpp" "$PSScriptRoot/PortableBridgeTests.cpp" /link "/OUT:$output/PortableBridgeTests.exe" "/IMPLIB:$output/PortableBridgeTests.lib"
    if ($LASTEXITCODE -ne 0) { throw 'Portable bridge host tests failed to compile.' }
    & "$output/PortableBridgeTests.exe"
    if ($LASTEXITCODE -ne 0) { throw 'Portable bridge host tests failed.' }
    & $compiler /nologo /utf-8 /std:c++17 /EHsc /W4 /WX /O2 /MT /DVLCUNITY_STATIC_LIBVLC=1 /DVLCUNITY_ENABLE_GPU=1 "/Fo$output/" "$PSScriptRoot/PortableRenderingPlugin.cpp" "$PSScriptRoot/PortableGpuContextTests.cpp" /link "/OUT:$output/PortableGpuContextTests.exe" "/IMPLIB:$output/PortableGpuContextTests.lib"
    if ($LASTEXITCODE -ne 0) { throw 'Portable GPU context host tests failed to compile.' }
    & "$output/PortableGpuContextTests.exe"
    if ($LASTEXITCODE -ne 0) { throw 'Portable GPU context host tests failed.' }
}
finally {
    $env:INCLUDE = $priorInclude
    $env:LIB = $priorLib
}
