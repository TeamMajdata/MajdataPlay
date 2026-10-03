param(
    [string]$UnityEditorData = 'C:/Program Files/Unity Editors/6000.3.17f1/Editor/Data'
)

$ErrorActionPreference = 'Stop'
$repoRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../../..'))
$testRoot = Join-Path $repoRoot 'Temp/VLCUnityPlatformMatrix'
$runtime = Join-Path $repoRoot 'Assets/Plugins/VLCUnity/Runtime'
$editor = Join-Path $repoRoot 'Assets/Plugins/VLCUnity/Editor'
$cases = @(
    @{ Name = 'WindowsPlayer'; Defines = 'UNITY_STANDALONE_WIN'; IOS = $false },
    @{ Name = 'LinuxPlayer'; Defines = 'UNITY_STANDALONE_LINUX'; IOS = $false },
    @{ Name = 'MacPlayer'; Defines = 'UNITY_STANDALONE_OSX'; IOS = $false },
    @{ Name = 'AndroidPlayer'; Defines = 'UNITY_ANDROID'; IOS = $false },
    @{ Name = 'IOSPlayer'; Defines = 'UNITY_IOS'; IOS = $true },
    # A target switch must not change the native ABI used by the editor host.
    @{ Name = 'WindowsEditorAndroid'; Defines = 'UNITY_EDITOR;UNITY_EDITOR_WIN;UNITY_ANDROID'; IOS = $false },
    @{ Name = 'MacEditorIOS'; Defines = 'UNITY_EDITOR;UNITY_EDITOR_OSX;UNITY_IOS'; IOS = $false },
    @{ Name = 'LinuxEditorAndroid'; Defines = 'UNITY_EDITOR;UNITY_EDITOR_LINUX;UNITY_ANDROID'; IOS = $false }
)

function Escape-Xml([string]$Value) { [Security.SecurityElement]::Escape($Value) }
$referenceFiles = @(Get-ChildItem -LiteralPath (Join-Path $UnityEditorData 'Managed/UnityEngine') -Filter '*.dll')
$xcode = Join-Path $UnityEditorData 'PlaybackEngines/iOSSupport/UnityEditor.iOS.Extensions.Xcode.dll'
if (Test-Path -LiteralPath $xcode) { $referenceFiles += Get-Item -LiteralPath $xcode }
$references = ($referenceFiles | ForEach-Object {
    '<Reference Include="' + (Escape-Xml $_.BaseName) + '"><HintPath>' + (Escape-Xml $_.FullName) + '</HintPath></Reference>'
}) -join [Environment]::NewLine

foreach ($case in $cases) {
    $directory = Join-Path $testRoot $case.Name
    New-Item -ItemType Directory -Path $directory -Force | Out-Null
    $binding = Join-Path $runtime 'Plugins/LibVLCSharp.dll'
    if ($case.IOS) { $binding = Join-Path $runtime 'Plugins/iOS/LibVLCSharp.dll' }
    $editorSource = ''
    if ($case.Defines.Contains('UNITY_EDITOR;')) {
        $editorSource = '<Compile Include="' + (Escape-Xml (Join-Path $editor '*.cs')) + '" />'
    }
    $project = @"
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <EnableDefaultCompileItems>false</EnableDefaultCompileItems>
    <EnableNETAnalyzers>false</EnableNETAnalyzers>
    <GenerateAssemblyInfo>false</GenerateAssemblyInfo>
    <DefineConstants>$($case.Defines);UNITY_2018_1_OR_NEWER;UNITY_2019_3_OR_NEWER;UNITY_2020_1_OR_NEWER;UNITY_2021_2_OR_NEWER;UNITY_6000_0_OR_NEWER</DefineConstants>
    <NoWarn>1701;1702;0649</NoWarn>
  </PropertyGroup>
  <ItemGroup>
    <Compile Include="$(Escape-Xml (Join-Path $runtime '*.cs'))" />
    <Compile Include="$(Escape-Xml (Join-Path $runtime 'Internal/*.cs'))" />
    $editorSource
    <Reference Include="LibVLCSharp"><HintPath>$(Escape-Xml $binding)</HintPath></Reference>
    $references
  </ItemGroup>
</Project>
"@
    $projectPath = Join-Path $directory 'Platform.csproj'
    [IO.File]::WriteAllText($projectPath, $project)
    Write-Host "Compiling VLCUnity: $($case.Name)"
    & dotnet build $projectPath --nologo -v quiet /p:UseSharedCompilation=false
    if ($LASTEXITCODE -ne 0) { throw "Platform compilation failed: $($case.Name)" }
}
Write-Host 'All eight platform/host combinations compiled. This checks C# branches, not native SDK linking or device playback.'
