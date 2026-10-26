[CmdletBinding()]
param(
    [switch]$Install,
    [string]$UnityPath = 'C:/Program Files/Unity Editors/6000.3.17f1/Editor/Unity.exe'
)

$ErrorActionPreference = 'Stop'
$root = (Resolve-Path (Join-Path $PSScriptRoot '../../..')).Path
$project = Join-Path $root 'Tools/CSharp/AndroidJavaGenerator/AndroidJavaGenerator.csproj'
& dotnet build $project --configuration Release
if ($LASTEXITCODE -ne 0) {
    throw 'The independent Android Java source generator build failed.'
}
$dll = Join-Path $root 'Temp/AndroidJavaGeneratorBuild/bin/netstandard2.0/MajdataPlay.SourceGenerators.AndroidJava.dll'
if (!(Test-Path -LiteralPath $dll -PathType Leaf)) {
    throw "The analyzer build did not produce $dll."
}
Write-Output "Built analyzer: $dll"

if ($Install) {
    $expectedVersion = (Select-String -LiteralPath (Join-Path $root 'ProjectSettings/ProjectVersion.txt') -Pattern '^m_EditorVersion: (.+)$').Matches[0].Groups[1].Value
    $version = [Diagnostics.FileVersionInfo]::GetVersionInfo($UnityPath).ProductVersion
    if ($version -notmatch ('^' + [regex]::Escape($expectedVersion) + '(?:_|$)')) {
        throw "Use Unity $expectedVersion; the selected editor reports $version."
    }
    $destination = Join-Path $root 'Assets/Plugins/MajdataPlay/Platform/Android/MajdataPlay.SourceGenerators.AndroidJava.dll'
    if (!(Test-Path -LiteralPath "$destination.meta" -PathType Leaf)) {
        Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'MajdataPlay.SourceGenerators.AndroidJava.dll.meta') -Destination "$destination.meta"
    }
    Copy-Item -LiteralPath $dll -Destination $destination -Force
    Write-Output "Installed Unity analyzer: $destination"
    Write-Output 'Let Unity finish importing and compiling. Java input changes require script recompilation; see the README.'
}
