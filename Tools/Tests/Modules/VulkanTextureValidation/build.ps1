param(
    [string] $UnityEditor = 'E:/Program Files/Unity/6000.3.17f1/Editor/Unity.exe'
)
$ErrorActionPreference = 'Stop'
$editor = Get-Item -LiteralPath $UnityEditor
$version = $editor.VersionInfo.ProductVersion
if ($version -notmatch '^6000\.3\.17f1(?:_|$)') {
    throw "Use Unity 6000.3.17f1; selected ProductVersion is $version"
}
Write-Output "Unity ProductVersion: $version"
$module = (Resolve-Path -LiteralPath $PSScriptRoot).Path
$work = Join-Path $module '.work'
$project = Join-Path $work 'Project'
$player = Join-Path $work 'Player'
$marker = Join-Path $project '.texture-probe-project'
if ((Test-Path -LiteralPath $project) -and -not (Test-Path -LiteralPath $marker -PathType Leaf)) {
    throw "Refusing to overwrite an unrecognized project: $project"
}
foreach ($folder in @($work, $project, $player, (Join-Path $project 'Assets/Runtime/Resources'),
    (Join-Path $project 'Assets/Editor'), (Join-Path $project 'Packages'), (Join-Path $project 'ProjectSettings'))) {
    New-Item -ItemType Directory -Path $folder -Force | Out-Null
}
Set-Content -LiteralPath $marker -Value 'VulkanTextureValidation isolated project' -Encoding ascii
Copy-Item -LiteralPath (Join-Path $module 'RenderProbe.cs') -Destination (Join-Path $project 'Assets/Runtime/RenderProbe.cs') -Force
Copy-Item -LiteralPath (Join-Path $module 'RenderProbe.shader') -Destination (Join-Path $project 'Assets/Runtime/Resources/RenderProbe.shader') -Force
Copy-Item -LiteralPath (Join-Path $module 'ProbeBuild.cs') -Destination (Join-Path $project 'Assets/Editor/ProbeBuild.cs') -Force
Set-Content -LiteralPath (Join-Path $project 'Packages/manifest.json') -Value '{"dependencies":{}}' -Encoding ascii
Set-Content -LiteralPath (Join-Path $project 'ProjectSettings/ProjectVersion.txt') -Value "m_EditorVersion: 6000.3.17f1`nm_EditorVersionWithRevision: 6000.3.17f1 (cf0352b38e81)" -Encoding ascii
$executable = Join-Path $player 'RenderProbe.exe'
$buildLog = Join-Path $work 'build.log'
$unityArguments = @('-batchmode', '-nographics', '-quit', '-projectPath', ('"{0}"' -f $project),
    '-buildTarget', 'Win64', '-executeMethod', 'MajdataPlay.Validation.VulkanTexture.ProbeBuild.Run',
    '-probe-build-output', ('"{0}"' -f $executable), '-logFile', ('"{0}"' -f $buildLog))
$buildProcess = Start-Process -FilePath $editor.FullName -ArgumentList $unityArguments -PassThru
$buildProcess.WaitForExit()
$buildExit = $buildProcess.ExitCode
if ($buildExit -ne 0) {
    throw "Unity build exited $buildExit. Inspect $buildLog"
}
if (-not (Test-Path -LiteralPath $executable -PathType Leaf)) {
    throw "Unity returned success without the expected executable: $executable. Inspect $buildLog"
}
if (-not (Select-String -LiteralPath $buildLog -SimpleMatch -Quiet -Pattern '[TextureProbe] BUILD DONE')) {
    throw "Unity did not confirm this build completed. Inspect $buildLog"
}
$sourceHashes = @()
foreach ($sourceName in @('RenderProbe.cs', 'RenderProbe.shader', 'ProbeBuild.cs', 'build.ps1')) {
    $sourceHashes += [ordered]@{
        Path = $sourceName
        SHA256 = (Get-FileHash -LiteralPath (Join-Path $module $sourceName) -Algorithm SHA256).Hash.ToLowerInvariant()
    }
}
$manifest = [ordered]@{
    UnityProductVersion = $version
    Target = 'StandaloneWindows64'
    Backend = 'Mono'
    GraphicsJobs = $true
    ColorSpace = 'Gamma'
    Sources = $sourceHashes
    ExecutableSHA256 = (Get-FileHash -LiteralPath $executable -Algorithm SHA256).Hash.ToLowerInvariant()
    UnityPlayerSHA256 = (Get-FileHash -LiteralPath (Join-Path $player 'UnityPlayer.dll') -Algorithm SHA256).Hash.ToLowerInvariant()
}
$manifest | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath (Join-Path $work 'build-manifest.json') -Encoding ascii
Write-Output "BUILD DONE: $executable"
Write-Output "Build log: $buildLog"
Write-Output 'The Player was not launched. Use the explicit commands in README.md for controlled GPU runs.'
