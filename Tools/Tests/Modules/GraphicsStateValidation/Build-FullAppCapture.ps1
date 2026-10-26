<#
.SYNOPSIS
Builds an isolated, automatically driven full-app Android graphics-state capture player.
.DESCRIPTION
Requires a separate, fully initialized copy of MajdataPlay with Assets/Tests excluded.
Preserve all asset GUIDs when preparing that copy. This script refuses the source checkout.
The capture uses its own package identifier and target SDK 36 to isolate device storage.
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$ProjectPath,
    [string]$UnityPath,
    [ValidateSet('Vulkan', 'OpenGLES3')][string]$GraphicsApi = 'Vulkan',
    [ValidateSet('Matrix', 'Legacy', 'Single')][string]$CaptureMode = 'Matrix',
    [string]$OutputDirectory
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version 3
$repository = (Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '../../../..')).Path
$project = (Resolve-Path -LiteralPath $ProjectPath).Path
if ($project -eq $repository -or $project.StartsWith($repository + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) {
    throw 'Use an isolated project outside the source checkout.'
}
$versionFile = Join-Path $project 'ProjectSettings/ProjectVersion.txt'
$version = (Select-String -LiteralPath $versionFile -Pattern '^m_EditorVersion: (.+)$').Matches[0].Groups[1].Value.Trim()
if ($version -ne '6000.3.17f1') {
    throw "The capture requires Unity 6000.3.17f1, found $version."
}
if (Test-Path -LiteralPath (Join-Path $project 'Assets/Tests')) {
    throw 'Exclude Assets/Tests from this capture copy before building; this is a player capture, not a Unity test run.'
}
if (!$UnityPath) {
    $UnityPath = Join-Path $env:ProgramFiles "Unity Editors/$version/Editor/Unity.exe"
    if (!(Test-Path -LiteralPath $UnityPath)) {
        $UnityPath = Join-Path $env:ProgramFiles "Unity/Hub/Editor/$version/Editor/Unity.exe"
    }
}
if (!(Test-Path -LiteralPath $UnityPath)) {
    throw 'Unity was not found. Pass -UnityPath for the pinned Editor.'
}
if ([Diagnostics.FileVersionInfo]::GetVersionInfo($UnityPath).ProductVersion -notmatch ('^' + [regex]::Escape($version) + '(?:_|$)')) {
    throw "Use the repository's Unity $version."
}
if (!$OutputDirectory) {
    $OutputDirectory = Join-Path (Split-Path $project -Parent) 'GraphicsCapture'
}
$output = [IO.Path]::GetFullPath($OutputDirectory)
if ($output -eq $repository -or $output.StartsWith($repository + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) {
    throw 'Write capture build outputs outside the source checkout.'
}
New-Item -ItemType Directory -Force -Path $output | Out-Null
$nativeDestination = Join-Path $project 'Assets/Plugins/Android/arm64-v8a'
New-Item -ItemType Directory -Force -Path $nativeDestination | Out-Null
foreach ($nativeName in @('libSkiaSharp.so', 'libHarfBuzzSharp.so')) {
    $nativeSource = Join-Path $project "ThirdParty/SkiaForUnity/Library/arm64-v8a/$nativeName"
    Copy-Item -LiteralPath $nativeSource -Destination (Join-Path $nativeDestination $nativeName)
}
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'FullAppCaptureBuild.cs') -Destination (Join-Path $project 'Assets/Scripts/Editor/AndroidFullCaptureBuild.cs')
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'FullAppCaptureDriver.cs') -Destination (Join-Path $project 'Assets/Scripts/Rendering/AndroidFullCaptureDriver.cs')
$apk = Join-Path $output "MajdataPlay.GraphicsCapture-$GraphicsApi.apk"
$log = Join-Path $output "build-$GraphicsApi.log"
$arguments = @('-batchmode', '-nographics', '-quit', '-projectPath', $project, '-buildTarget', 'Android',
    '-executeMethod', 'MajdataPlay.Editor.AndroidFullCaptureBuild.Build', '-captureOutput', $apk,
    '-captureApi', $GraphicsApi, '-captureMode', $CaptureMode, '-logFile', $log)
$quoted = @($arguments | ForEach-Object { if ($_ -match '\s') { '"' + $_ + '"' } else { $_ } })
$process = Start-Process -FilePath $UnityPath -ArgumentList $quoted -WindowStyle Hidden -PassThru
$null = $process.Handle
$process.WaitForExit()
$process.Refresh()
if ($process.ExitCode -ne 0) {
    throw "Capture build failed ($($process.ExitCode)); inspect $log."
}
Write-Host "Capture APK: $apk"
Write-Host 'Package: net.majdata.majdataplay.graphicscapture'
Write-Host "Capture mode: $CaptureMode. The driver initializes once, plays complete Game/Result runs, and exits automatically."
