param(
    [string]$UnityEditor = 'C:\Program Files\Unity Editors\6000.3.17f1\Editor\Unity.exe',
    [ValidateSet('d3d11', 'd3d12', 'vulkan', 'glcore')]
    [string[]]$GraphicsApi = @('d3d11', 'd3d12', 'vulkan', 'glcore'),
    [switch]$ForceCpu,
    [string]$NativePlugin,
    [int]$TimeoutSeconds = 180
)

$ErrorActionPreference = 'Stop'
$repoRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../../..'))
$validationRoot = Join-Path $repoRoot 'Temp/VLCUnityGraphicsValidation'
$projectRoot = Join-Path $validationRoot 'Project'
$resultRoot = Join-Path $validationRoot 'Results'
$pluginSource = Join-Path $repoRoot 'Assets/Plugins/VLCUnity/Runtime/Plugins/Windows/x86_64'
$pluginTarget = Join-Path $projectRoot 'Assets/Plugins/x86_64'
$runtimeTarget = Join-Path $projectRoot 'Assets/VLCUnity'
$editorTarget = Join-Path $projectRoot 'Assets/Editor'

if (-not (Test-Path -LiteralPath $UnityEditor -PathType Leaf)) {
    throw "Unity editor not found: $UnityEditor"
}
foreach ($directory in @($projectRoot, $resultRoot, $pluginTarget, $runtimeTarget, $editorTarget,
    (Join-Path $projectRoot 'Packages'), (Join-Path $projectRoot 'ProjectSettings'))) {
    New-Item -ItemType Directory -Path $directory -Force | Out-Null
}

# No source project settings or imported assets are changed. All generated
# fixtures, player logs and library imports stay in this ignored Temp project.
Copy-Item -Path (Join-Path $pluginSource '*') -Destination $pluginTarget -Recurse -Force
if ($NativePlugin) {
    Copy-Item -LiteralPath $NativePlugin -Destination (Join-Path $pluginTarget 'VLCUnityPlugin.dll') -Force
}
Copy-Item -LiteralPath (Join-Path $repoRoot 'Assets/Plugins/VLCUnity/Runtime/VlcVideoOutput.cs') -Destination $runtimeTarget -Force
Copy-Item -LiteralPath (Join-Path $repoRoot 'Assets/Plugins/VLCUnity/Runtime/Internal/VlcCpuVideoOutput.cs') -Destination $runtimeTarget -Force
Copy-Item -LiteralPath (Join-Path $repoRoot 'Assets/Plugins/VLCUnity/Runtime/Internal/OnLoad.cs') -Destination $runtimeTarget -Force
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'UnityGraphicsValidation.cs') -Destination $editorTarget -Force
Copy-Item -LiteralPath (Join-Path $repoRoot 'ProjectSettings/ProjectVersion.txt') -Destination (Join-Path $projectRoot 'ProjectSettings/ProjectVersion.txt') -Force
[IO.File]::WriteAllText((Join-Path $projectRoot 'Packages/manifest.json'), '{"dependencies":{"com.unity.modules.imageconversion":"1.0.0"}}')

function Quote-Argument([string]$Value) {
    if ($Value.Contains('"')) { throw 'A command argument contains an unsupported quote.' }
    return '"' + $Value + '"'
}

$results = @()
foreach ($api in $GraphicsApi) {
    $name = $api
    if ($ForceCpu) { $name += '-cpu' }
    $resultPath = Join-Path $resultRoot ($name + '.json')
    $logPath = Join-Path $resultRoot ($name + '.log')
    # A unique result filename prevents accidentally accepting a previous run.
    $runResult = Join-Path $resultRoot ($name + '-' + [Guid]::NewGuid().ToString('N') + '.json')
    $arguments = @('-batchmode', '-projectPath', (Quote-Argument $projectRoot),
        ('-force-' + $api), '-executeMethod', 'VlcUnityGraphicsValidation.Run',
        '-logFile', (Quote-Argument $logPath), '-vlc-result', (Quote-Argument $runResult),
        '-vlc-sample', (Quote-Argument (Join-Path $repoRoot 'Assets/StreamingAssets/MovieBG/title.webm')))
    if ($ForceCpu) { $arguments += '-vlc-force-cpu' }
    Write-Host "Validating VLCUnity: $name"
    $process = Start-Process -FilePath $UnityEditor -ArgumentList $arguments -WindowStyle Hidden -PassThru
    if (-not $process.WaitForExit($TimeoutSeconds * 1000)) {
        Stop-Process -Id $process.Id -Force
        throw "Unity validation timed out for $name. Log: $logPath"
    }
    if (-not (Test-Path -LiteralPath $runResult)) {
        throw "Unity produced no validation result for $name (exit $($process.ExitCode)). Log: $logPath"
    }
    Copy-Item -LiteralPath $runResult -Destination $resultPath -Force
    $result = Get-Content -LiteralPath $runResult -Raw | ConvertFrom-Json
    $expectedApi = @{ d3d11 = 'Direct3D11'; d3d12 = 'Direct3D12'; vulkan = 'Vulkan'; glcore = 'OpenGLCore' }[$api]
    if ($result.graphicsApi -ne $expectedApi) {
        throw "Unity selected $($result.graphicsApi) instead of $expectedApi. Log: $logPath"
    }
    if (-not $result.success -or $process.ExitCode -ne 0) {
        throw "VLC validation failed for ${name}: $($result.error). Log: $logPath"
    }
    if ($ForceCpu -and $result.gpuInterop) { throw "Forced CPU validation unexpectedly selected GPU interop: $name" }
    $results += $result
}
$results | Format-Table graphicsApi, forcedCpu, gpuInterop, decodedFrames, casesPassed, success
Write-Host "Results and logs: $resultRoot"
