[CmdletBinding()]
param(
    [string]$UnityPath = 'C:/Program Files/Unity Editors/6000.3.17f1/Editor/Unity.exe',
    [Parameter(Mandatory = $true)][string]$Serial,
    [switch]$SkipBuild,
    [int]$BuildTimeoutSeconds = 1800,
    [int]$DeviceTimeoutSeconds = 120
)
$ErrorActionPreference = 'Stop'
$root = (Resolve-Path (Join-Path $PSScriptRoot '../../../..')).Path
$project = Join-Path $root 'Temp/GSC'
$package = 'net.majdata.graphicsstatevalidation'
$editorData = Join-Path (Split-Path -Parent $UnityPath) 'Data'
$adb = Join-Path $editorData 'PlaybackEngines/AndroidPlayer/SDK/platform-tools/adb.exe'
$version = (Select-String -LiteralPath (Join-Path $root 'ProjectSettings/ProjectVersion.txt') -Pattern '^m_EditorVersion: (.+)$').Matches[0].Groups[1].Value
if ([Diagnostics.FileVersionInfo]::GetVersionInfo($UnityPath).ProductVersion -notmatch ('^' + [regex]::Escape($version) + '(?:_|$)')) {
    throw "Use the repository's Unity $version."
}
function Invoke-Adb([string[]]$Arguments) {
    $output = & $adb -s $Serial @Arguments 2>&1
    if ($LASTEXITCODE -ne 0) {
        throw "ADB failed: $output"
    }
    return $output
}
Invoke-Adb @('get-state') | Out-Null
if (!$SkipBuild) {
    foreach ($directory in @('Assets/Probe', 'Assets/Editor', 'Assets/Storage', 'Assets/Rendering/Shaders',
        'Assets/Plugins/MajdataPlay/Rendering/RenderPipelines/MajRenderPipeline/Library', 'Packages', 'ProjectSettings')) {
        New-Item -ItemType Directory -Path (Join-Path $project $directory) -Force | Out-Null
    }
    Copy-Item -LiteralPath (Join-Path $root 'ProjectSettings/ProjectVersion.txt') -Destination (Join-Path $project 'ProjectSettings/ProjectVersion.txt') -Force
    $uniTaskPath = (Join-Path $root 'ThirdParty/UniTask/src/UniTask/Assets/Plugins/UniTask').Replace('\', '/')
    @{ dependencies = @{
        'com.cysharp.unitask' = "file:$uniTaskPath"
        'com.unity.ugui' = '2.0.0'
        'com.unity.modules.androidjni' = '1.0.0'
        'com.unity.modules.imgui' = '1.0.0'
        'com.unity.modules.jsonserialize' = '1.0.0'
        'com.unity.modules.ui' = '1.0.0'
    } } | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath (Join-Path $project 'Packages/manifest.json') -Encoding utf8NoBOM
    foreach ($name in @('GraphicsStateDeviceProbe.cs', 'DiagnosticStub.cs')) {
        Copy-Item -LiteralPath (Join-Path $PSScriptRoot $name) -Destination (Join-Path $project 'Assets/Probe') -Force
    }
    Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'GraphicsStateDeviceBuild.cs') -Destination (Join-Path $project 'Assets/Editor') -Force
    Copy-Item -LiteralPath (Join-Path $root 'Assets/Scripts/Rendering/AndroidGraphicsStateWarmup.cs') -Destination (Join-Path $project 'Assets/Probe') -Force
    Get-ChildItem -LiteralPath (Join-Path $root 'Assets/Plugins/MajdataPlay/IO/Storage') -Filter '*.cs' -File | Copy-Item -Destination (Join-Path $project 'Assets/Storage') -Force
    foreach ($shader in @('Common/SpriteDefault.shader', 'UI/Default.shader')) {
        Copy-Item -LiteralPath (Join-Path $root "Assets/Rendering/Shaders/$shader") -Destination (Join-Path $project 'Assets/Rendering/Shaders') -Force
        Copy-Item -LiteralPath (Join-Path $root "Assets/Rendering/Shaders/$shader.meta") -Destination (Join-Path $project 'Assets/Rendering/Shaders') -Force
    }
    $include = 'Assets/Plugins/MajdataPlay/Rendering/RenderPipelines/MajRenderPipeline/Library/MajMipSampling.hlsl'
    Copy-Item -LiteralPath (Join-Path $root $include) -Destination (Join-Path $project $include) -Force
    $log = Join-Path $project 'build.log'
    $process = Start-Process -FilePath $UnityPath -WindowStyle Hidden -ArgumentList @(
        '-batchmode', '-nographics', '-buildTarget', 'Android', '-projectPath', ('"' + $project + '"'),
        '-executeMethod', 'MajdataPlay.Tests.GraphicsStates.GraphicsStateDeviceBuild.Run',
        '-logFile', ('"' + $log + '"')
    ) -PassThru
    $null = $process.Handle
    if (!$process.WaitForExit($BuildTimeoutSeconds * 1000)) {
        throw "Build exceeded timeout; process $($process.Id) remains running. Inspect $log."
    }
    $process.Refresh()
    if ($process.ExitCode -ne 0 -or !(Select-String -LiteralPath $log -Pattern 'GRAPHICS_STATE_BUILD_PASSED' -Quiet)) {
        Get-Content -LiteralPath $log -Tail 80
        throw "Graphics-state build failed; see $log."
    }
}
$apk = Join-Path $project 'Artifacts/GraphicsStates.apk'
Invoke-Adb @('install', '-r', $apk) | Write-Output
Invoke-Adb @('shell', 'am', 'force-stop', $package) | Out-Null
Invoke-Adb @('shell', 'rm', '-f', "/sdcard/Android/data/$package/files/result.txt") | Out-Null
Invoke-Adb @('shell', 'input', 'keyevent', 'KEYCODE_WAKEUP') | Out-Null
Invoke-Adb @('shell', 'wm', 'dismiss-keyguard') | Out-Null
Invoke-Adb @('shell', 'am', 'start', '-n', "$package/com.unity3d.player.UnityPlayerActivity") | Out-Null
$deadline = [DateTime]::UtcNow.AddSeconds($DeviceTimeoutSeconds)
do {
    Start-Sleep -Seconds 2
    $result = (& $adb -s $Serial shell cat "/sdcard/Android/data/$package/files/result.txt" 2>$null) -join "`n"
    if ($LASTEXITCODE -eq 0 -and $result -match '^(PASS|FAIL):') {
        $result | Set-Content -LiteralPath (Join-Path $project 'device-result.txt') -Encoding utf8NoBOM
        $devicePid = ((Invoke-Adb @('shell', 'pidof', $package)) -join ' ').Trim().Split(' ')[0]
        Invoke-Adb @('logcat', "--pid=$devicePid", '-d', '-v', 'brief') | Set-Content -LiteralPath (Join-Path $project 'device.log') -Encoding utf8NoBOM
        Write-Output $result
        if ($result -notmatch '^PASS:') {
            throw 'Graphics-state device validation failed.'
        }
        Invoke-Adb @('pull', "/sdcard/Android/data/$package/files/probe.graphicsstate", (Join-Path $project 'probe.graphicsstate')) | Out-Null
        Write-Output "Evidence: $project/device-result.txt; capture is a probe, not a full game capture."
        return
    }
} while ([DateTime]::UtcNow -lt $deadline)
throw 'Graphics-state device validation timed out; inspect the dedicated app and device log.'
