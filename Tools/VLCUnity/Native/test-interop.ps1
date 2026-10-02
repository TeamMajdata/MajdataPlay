param(
    [ValidateSet('All', 'OpenGL', 'Vulkan')]
    [string]$Backend = 'All',
    [string]$OutputDirectory = "$PSScriptRoot/../../../Temp/VLCUnityInteropTests"
)
$ErrorActionPreference = 'Stop'
$vswhere = Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio/Installer/vswhere.exe'
$visualStudio = & $vswhere -latest -products '*' -requires Microsoft.VisualStudio.Component.VC.Tools.x86.x64 -property installationPath
if (!$visualStudio) { throw 'Visual Studio C++ x64 build tools are required.' }
$toolset = Get-ChildItem (Join-Path $visualStudio 'VC/Tools/MSVC') -Directory | Sort-Object Name -Descending | Select-Object -First 1
$sdkRoot = Join-Path ${env:ProgramFiles(x86)} 'Windows Kits/10'
$sdk = Get-ChildItem (Join-Path $sdkRoot 'Include') -Directory | Sort-Object Name -Descending | Select-Object -First 1
$interopOutput = [IO.Path]::GetFullPath($OutputDirectory)
New-Item -ItemType Directory -Force -Path $interopOutput | Out-Null
$priorInclude = $env:INCLUDE
$priorLib = $env:LIB
try {
    $env:INCLUDE = @((Join-Path $toolset.FullName 'include'), (Join-Path $sdk.FullName 'ucrt'), (Join-Path $sdk.FullName 'shared'), (Join-Path $sdk.FullName 'um'), (Join-Path $sdk.FullName 'winrt')) -join ';'
    $env:LIB = @((Join-Path $toolset.FullName 'lib/x64'), (Join-Path $sdkRoot "Lib/$($sdk.Name)/ucrt/x64"), (Join-Path $sdkRoot "Lib/$($sdk.Name)/um/x64")) -join ';'
    $compiler = Join-Path $toolset.FullName 'bin/Hostx64/x64/cl.exe'
    $backends = if ($Backend -eq 'All') { @('OpenGL', 'Vulkan') } else { @($Backend) }
    foreach ($selectedBackend in $backends) {
        $testName = "${selectedBackend}InteropSmoke"
        $executable = Join-Path $interopOutput "$testName.exe"
        & $compiler /nologo /utf-8 /std:c++17 /EHsc /W4 /WX /O2 /MT "/I$PSScriptRoot/ThirdParty" "/Fo$interopOutput/$testName.obj" "/Fe$executable" "$PSScriptRoot/$testName.cpp" /link d3d11.lib dxgi.lib opengl32.lib user32.lib gdi32.lib
        if ($LASTEXITCODE -ne 0) { throw "$testName compilation failed." }
        & $executable
        if ($LASTEXITCODE -eq 77) {
            Write-Output "$selectedBackend interop is unavailable on this test machine; CPU fallback is required."
        }
        elseif ($LASTEXITCODE -ne 0) {
            throw "$testName failed with exit code $LASTEXITCODE."
        }
    }
}
finally {
    $env:INCLUDE = $priorInclude
    $env:LIB = $priorLib
}
