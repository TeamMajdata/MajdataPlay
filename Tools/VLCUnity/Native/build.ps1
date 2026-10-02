param(
    [string]$OutputDirectory = "$PSScriptRoot/../../../Temp/VLCUnityNative",
    [switch]$Install,
    [switch]$SmokeTest
)
$ErrorActionPreference = 'Stop'
$vswhere = Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio/Installer/vswhere.exe'
$visualStudio = & $vswhere -latest -products '*' -requires Microsoft.VisualStudio.Component.VC.Tools.x86.x64 -property installationPath
if (!$visualStudio) { throw 'Visual Studio C++ x64 build tools are required.' }
$toolset = Get-ChildItem (Join-Path $visualStudio 'VC/Tools/MSVC') -Directory | Sort-Object Name -Descending | Select-Object -First 1
$sdkRoot = Join-Path ${env:ProgramFiles(x86)} 'Windows Kits/10'
$sdk = Get-ChildItem (Join-Path $sdkRoot 'Include') -Directory | Sort-Object Name -Descending | Select-Object -First 1
$output = [IO.Path]::GetFullPath($OutputDirectory)
New-Item -ItemType Directory -Force -Path $output | Out-Null
$priorInclude = $env:INCLUDE
$priorLib = $env:LIB
try {
    $env:INCLUDE = @((Join-Path $toolset.FullName 'include'), (Join-Path $sdk.FullName 'ucrt'), (Join-Path $sdk.FullName 'shared'), (Join-Path $sdk.FullName 'um'), (Join-Path $sdk.FullName 'winrt')) -join ';'
    $env:LIB = @((Join-Path $toolset.FullName 'lib/x64'), (Join-Path $sdkRoot "Lib/$($sdk.Name)/ucrt/x64"), (Join-Path $sdkRoot "Lib/$($sdk.Name)/um/x64")) -join ';'
    $compiler = Join-Path $toolset.FullName 'bin/Hostx64/x64/cl.exe'
    & $compiler /nologo /utf-8 /std:c++17 /EHsc /O2 /MT /LD /W4 /wd4100 /wd4191 /wd4201 /wd4324 /wd4458 /wd4706 "/I$PSScriptRoot/ThirdParty" "/Fo$output/RenderingPlugin.obj" "$PSScriptRoot/RenderingPlugin.cpp" /link "/OUT:$output/VLCUnityPlugin.dll" "/IMPLIB:$output/VLCUnityPlugin.lib" d3d11.lib d3d12.lib dxgi.lib opengl32.lib user32.lib
    if ($LASTEXITCODE -ne 0) { throw 'Native VLCUnity compilation failed.' }
    if ($SmokeTest) {
        & $compiler /nologo /utf-8 /std:c++17 /EHsc /O2 /MT /W4 /wd4100 /wd4191 /wd4201 /wd4324 /wd4458 /wd4706 "/I$PSScriptRoot/ThirdParty" "/Fo$output/SmokeTest.obj" "$PSScriptRoot/SmokeTest.cpp" /link "/OUT:$output/SmokeTest.exe" "/IMPLIB:$output/SmokeTest.lib" d3d11.lib d3d12.lib dxgi.lib opengl32.lib user32.lib
        if ($LASTEXITCODE -ne 0) { throw 'Native VLCUnity smoke test compilation failed.' }
    }
    if ($Install) {
        $target = [IO.Path]::GetFullPath("$PSScriptRoot/../../../Assets/Plugins/VLCUnity/Runtime/Plugins/Windows/x86_64/VLCUnityPlugin.dll")
        Copy-Item -LiteralPath "$output/VLCUnityPlugin.dll" -Destination $target
    }
    Write-Output "$output/VLCUnityPlugin.dll"
}
finally {
    $env:INCLUDE = $priorInclude
    $env:LIB = $priorLib
}
