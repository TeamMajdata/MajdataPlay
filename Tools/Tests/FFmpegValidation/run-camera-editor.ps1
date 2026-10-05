param(
    [string] $UnityEditor = 'C:\Program Files\Unity Editors\6000.3.17f1\Editor\Unity.exe',
    [string] $ProjectDirectory = ''
)
$ErrorActionPreference = 'Stop'
$repo = (Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '../../..')).Path
$work = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '.work'))
if (-not $ProjectDirectory) {
    $ProjectDirectory = Join-Path $work 'camera-builtin/Project-x64-native-video'
}
$project = (Resolve-Path -LiteralPath $ProjectDirectory).Path
if (-not $project.StartsWith($work + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) {
    throw 'Inspector validation must use an isolated project below FFmpegValidation/.work.'
}
$version = Join-Path $project 'ProjectSettings/ProjectVersion.txt'
if (-not (Test-Path -LiteralPath $version) -or (Get-Content -LiteralPath $version -Raw) -notmatch '6000\.3\.17f1') {
    throw 'Prepare this isolated Unity 6000.3.17f1 project with run-unity.ps1 -TestCameraCapture first.'
}
Copy-Item -LiteralPath (Join-Path $repo 'Assets/Plugins/MajdataPlay/FFmpeg/Runtime') -Destination (Join-Path $project 'Assets/Plugins/FFmpeg') -Recurse -Force
Copy-Item -LiteralPath (Join-Path $repo 'Assets/Plugins/MajdataPlay/FFmpeg/Editor') -Destination (Join-Path $project 'Assets/Plugins/FFmpeg') -Recurse -Force
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'UnityCameraEditorSmoke.cs') -Destination (Join-Path $project 'Assets/Editor/UnityCameraEditorSmoke.cs') -Force
$utf8 = New-Object Text.UTF8Encoding($false)
$definition = Join-Path $project 'Assets/Editor/FFmpeg.Player.Smoke.Editor.asmdef'
$assembly = Get-Content -LiteralPath $definition -Raw | ConvertFrom-Json
if ('MajdataPlay.FFmpeg' -notin $assembly.references) {
    $assembly.references = @($assembly.references) + @('MajdataPlay.FFmpeg')
    [IO.File]::WriteAllText($definition, ($assembly | ConvertTo-Json -Depth 8), $utf8)
}
$result = Join-Path (Split-Path -Parent $project) 'inspector'
New-Item -ItemType Directory -Path $result -Force | Out-Null
$report = Join-Path $result 'camera-inspector.txt'
$log = Join-Path $result 'camera-inspector.log'
if (Test-Path -LiteralPath $report) {
    Remove-Item -LiteralPath $report
}
# GUI validation deliberately uses the regular Editor event loop. The production
# Inspector is drawn from an EditorWindow's real Layout and Repaint callbacks.
$arguments = @('-force-d3d11', '-projectPath', ('"' + $project + '"'), '-logFile', ('"' + $log + '"'),
    '-executeMethod', 'FFmpegCameraEditorSmoke.Run', '-cameraEditorReport', ('"' + $report + '"'))
$process = Start-Process -FilePath $UnityEditor -ArgumentList $arguments -WindowStyle Hidden -PassThru
if (-not $process.WaitForExit(120000)) {
    $process.Kill()
    throw "Inspector validation timed out: $log"
}
if (-not (Test-Path -LiteralPath $report)) {
    Get-Content -LiteralPath $log -Tail 70
    throw "Inspector validation produced no report: $log"
}
$text = Get-Content -LiteralPath $report -Raw
Write-Output $text
if ($process.ExitCode -ne 0 -or -not $text.StartsWith('PASS:')) {
    throw "Inspector validation failed: $log"
}
