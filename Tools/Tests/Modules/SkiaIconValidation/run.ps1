[CmdletBinding()]
param(
    [string]$UnityPath,
    [switch]$Baseline
)

$ErrorActionPreference = 'Stop'
$root = (Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '../../../..')).Path
$project = [IO.Path]::GetFullPath((Join-Path $root 'Temp/SkiaIconValidation'))
$expectedVersion = (Select-String -LiteralPath (Join-Path $root 'ProjectSettings/ProjectVersion.txt') -Pattern '^m_EditorVersion: (.+)$').Matches[0].Groups[1].Value
if (!$UnityPath) {
    $UnityPath = @(
        "C:/Program Files/Unity/Hub/Editor/$expectedVersion/Editor/Unity.exe",
        "C:/Program Files/Unity Editors/$expectedVersion/Editor/Unity.exe"
    ) | Where-Object { Test-Path -LiteralPath $_ -PathType Leaf } | Select-Object -First 1
}
if (!$UnityPath -or !(Test-Path -LiteralPath $UnityPath -PathType Leaf)) {
    throw "Unity $expectedVersion was not found; pass -UnityPath."
}
$actualVersion = (Get-Item -LiteralPath $UnityPath).VersionInfo.ProductVersion.Split('_')[0]
if ($actualVersion -ne $expectedVersion -or $expectedVersion -ne '6000.3.17f1') {
    throw "This validation requires Unity 6000.3.17f1; the repository specifies $expectedVersion and the selected Editor is $actualVersion."
}

# Only replace source directories inside the ignored isolated project; retain its import cache.
$projectBoundary = $project.TrimEnd('\', '/') + [IO.Path]::DirectorySeparatorChar
foreach ($directory in @($root, (Join-Path $root 'Temp'), $project)) {
    if (Test-Path -LiteralPath $directory) {
        $item = Get-Item -LiteralPath $directory -Force
        if (!$item.PSIsContainer -or ($item.Attributes -band [IO.FileAttributes]::ReparsePoint)) {
            throw "Refusing to stage through a file or reparse point: $directory"
        }
    }
}
foreach ($name in @('Assets', 'ThirdParty', 'Packages')) {
    $target = [IO.Path]::GetFullPath((Join-Path $project $name))
    if (!$target.StartsWith($projectBoundary, [StringComparison]::OrdinalIgnoreCase)) {
        throw "The staging target must stay inside $project : $target"
    }
    if (Test-Path -LiteralPath $target) {
        $item = Get-Item -LiteralPath $target -Force
        if (!$item.PSIsContainer -or ($item.Attributes -band [IO.FileAttributes]::ReparsePoint)) {
            throw "Refusing to replace a file or reparse point: $target"
        }
        Remove-Item -LiteralPath $target -Recurse -Force
    }
}

$io = Join-Path $project 'Assets/IO'
$validation = Join-Path $project 'Assets/Editor'
$package = Join-Path $project 'ThirdParty/SkiaForUnity'
foreach ($directory in @($io, $validation, (Join-Path $package 'Editor'), (Join-Path $package 'Runtime'),
    (Join-Path $package 'SkiaSharp-Bindings'), (Join-Path $project 'Packages'), (Join-Path $project 'ProjectSettings'))) {
    New-Item -ItemType Directory -Force -Path $directory | Out-Null
}
Copy-Item -LiteralPath (Join-Path $root 'ProjectSettings/ProjectVersion.txt') -Destination (Join-Path $project 'ProjectSettings/ProjectVersion.txt')
$encoding = New-Object Text.UTF8Encoding($false)
$manifest = @{ dependencies = @{
    'com.ammariqais.skiaforunity' = 'file:../ThirdParty/SkiaForUnity'
    'com.unity.modules.imageconversion' = '1.0.0'
    'com.unity.modules.imgui' = '1.0.0'
} } | ConvertTo-Json -Depth 4
[IO.File]::WriteAllText((Join-Path $project 'Packages/manifest.json'), $manifest, $encoding)
$packageManifest = @{ name = 'com.ammariqais.skiaforunity'; version = '4.2.2'; displayName = 'Skia Icon Validation Fixture' } | ConvertTo-Json
[IO.File]::WriteAllText((Join-Path $package 'package.json'), $packageManifest, $encoding)

$ioSource = Join-Path $root 'Assets/Plugins/MajdataPlay/IO'
Get-ChildItem -LiteralPath (Join-Path $ioSource 'Storage') -File -Filter '*.cs' | Copy-Item -Destination $io
foreach ($name in @('MajdataPlay.IO.asmdef', 'MajdataPlay.IO.asmdef.meta')) {
    Copy-Item -LiteralPath (Join-Path $ioSource $name) -Destination $io
}
$skiaSource = Join-Path $root 'ThirdParty/SkiaForUnity'
foreach ($relative in @('Editor/SkiaSharp.Editor.asmdef', 'Editor/SkiaSharp.Editor.asmdef.meta',
    'Runtime/SkiaSharp.Unity.asmdef', 'Runtime/SkiaSharp.Unity.asmdef.meta',
    'SkiaSharp-Bindings/SkiaSharp.asmdef', 'SkiaSharp-Bindings/SkiaSharp.asmdef.meta')) {
    Copy-Item -LiteralPath (Join-Path $skiaSource $relative) -Destination (Join-Path $package $relative)
}
$productionTarget = Join-Path $package 'Editor/HBIconAssigner.cs'
if ($Baseline) {
    $source = & git -C $root show 'HEAD:ThirdParty/SkiaForUnity/Editor/HBIconAssigner.cs'
    if ($LASTEXITCODE -ne 0) {
        throw 'Could not read the committed HBIconAssigner baseline.'
    }
    [IO.File]::WriteAllText($productionTarget, (($source -join "`r`n") + "`r`n"), $encoding)
} else {
    Copy-Item -LiteralPath (Join-Path $skiaSource 'Editor/HBIconAssigner.cs') -Destination $productionTarget
}
foreach ($name in @('HB_TEXTBlock', 'HBTextAnimator', 'HBInputField', 'SkiaGraphic')) {
    $fixture = @"
#nullable enable
using UnityEngine;

namespace SkiaSharp.Unity.HB
{
    /// <summary>Provides a named MonoScript fixture for the production icon assigner.</summary>
    public sealed class $name : MonoBehaviour
    {
    }
}
"@
    [IO.File]::WriteAllText((Join-Path $package "Runtime/$name.cs"), ($fixture + "`r`n"), $encoding)
}
# The real runtime asmdef still references the binding assembly; one empty type creates it.
$bindingFixture = @'
#nullable enable
namespace SkiaSharp
{
    /// <summary>Creates the binding assembly required by the production assembly definitions.</summary>
    internal static class BindingFixture
    {
    }
}
'@
[IO.File]::WriteAllText((Join-Path $package 'SkiaSharp-Bindings/BindingFixture.cs'), ($bindingFixture + "`r`n"), $encoding)
$validationAssembly = @{
    name = 'MajdataPlay.Tests.SkiaIcons.Editor'
    references = @('MajdataPlay.IO')
    includePlatforms = @('Editor')
} | ConvertTo-Json
[IO.File]::WriteAllText((Join-Path $validation 'MajdataPlay.Tests.SkiaIcons.Editor.asmdef'), $validationAssembly, $encoding)
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'Validation.cs') -Destination $validation

$suffix = if ($Baseline) { '-baseline' } else { '' }
$log = Join-Path $project "validation$suffix.log"
$process = Start-Process -FilePath $UnityPath -WindowStyle Hidden -ArgumentList @(
    '-batchmode', '-nographics', '-projectPath', "`"$project`"",
    '-executeMethod', 'MajdataPlay.Tests.SkiaIcons.Validation.Run', '-logFile', "`"$log`""
) -PassThru
$process.WaitForExit()
if ($process.ExitCode -ne 0 -or !(Test-Path -LiteralPath $log) -or
    !(Select-String -LiteralPath $log -Pattern 'SKIA_ICON_VALIDATION_PASSED' -Quiet)) {
    if (Test-Path -LiteralPath $log) {
        Get-Content -LiteralPath $log -Tail 80
    }
    throw "Skia icon validation failed (exit $($process.ExitCode)). See $log"
}
Select-String -LiteralPath $log -Pattern 'SKIA_ICON_' | ForEach-Object { $_.Line }
Write-Output "Skia icon validation passed. Log: $log"
