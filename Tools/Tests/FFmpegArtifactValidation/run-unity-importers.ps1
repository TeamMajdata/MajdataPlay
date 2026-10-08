param(
    [string] $UnityEditor = 'C:/Program Files/Unity Editors/6000.3.17f1/Editor/Unity.exe'
)
$ErrorActionPreference = 'Stop'
$repo = (Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '../../..')).Path
$work = Join-Path $PSScriptRoot '.work/unity-importers'
$project = Join-Path $work 'Project'
$log = Join-Path $work 'unity-editor.log'
$report = Join-Path $project 'importer-audit.txt'
foreach ($architecture in @('x86', 'x86_64', 'arm64')) {
    $bridge = Join-Path $repo "Assets/Plugins/MajdataPlay/FFmpeg/Native/Windows/$architecture/FFmpegUnityBridge.dll"
    if (-not (Test-Path -LiteralPath $bridge -PathType Leaf)) { throw "Missing installed Windows $architecture bridge: $bridge" }
}
foreach ($directory in @('Assets/Editor', 'Assets/FFmpeg/Native', 'Packages', 'ProjectSettings')) {
    New-Item -ItemType Directory -Path (Join-Path $project $directory) -Force | Out-Null
}
Copy-Item -LiteralPath (Join-Path $repo 'Assets/Plugins/MajdataPlay/FFmpeg/Native/Windows') -Destination (Join-Path $project 'Assets/FFmpeg/Native') -Recurse -Force
Copy-Item -LiteralPath (Join-Path $repo 'Assets/Plugins/MajdataPlay/FFmpeg/Native/Windows.meta') -Destination (Join-Path $project 'Assets/FFmpeg/Native/Windows.meta') -Force
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'FFmpegImporterAudit.cs') -Destination (Join-Path $project 'Assets/Editor/FFmpegImporterAudit.cs') -Force
Copy-Item -LiteralPath (Join-Path $repo 'ProjectSettings/ProjectVersion.txt') -Destination (Join-Path $project 'ProjectSettings/ProjectVersion.txt') -Force
[IO.File]::WriteAllText((Join-Path $project 'Packages/manifest.json'), '{"dependencies":{}}')
# Remove only the stale result from this fixture, never a project or asset tree.
if (Test-Path -LiteralPath $report -PathType Leaf) { Remove-Item -LiteralPath $report }
$arguments = @('-batchmode', '-nographics', '-quit', '-projectPath', ('"' + $project + '"'),
    '-executeMethod', 'MajdataPlay.Tests.FFmpeg.FFmpegImporterAudit.Run', '-logFile', ('"' + $log + '"'))
$process = Start-Process -FilePath $UnityEditor -ArgumentList $arguments -WindowStyle Hidden -PassThru
if (-not $process.WaitForExit(300000)) { $process.Kill(); throw "Unity importer audit timed out: $log" }
if ($process.ExitCode -ne 0 -or -not (Test-Path -LiteralPath $report -PathType Leaf)) {
    if (Test-Path -LiteralPath $log -PathType Leaf) { Get-Content -LiteralPath $log -Tail 80 }
    throw "Unity importer audit failed (exit $($process.ExitCode)): $log"
}
$text = Get-Content -LiteralPath $report -Raw
if (-not $text.StartsWith('PASS:')) { throw "Invalid importer audit report: $report" }
Write-Output $text
