param(
    [string] $UnityEditor = 'C:\Program Files\Unity Editors\6000.3.17f1\Editor\Unity.exe'
)
$ErrorActionPreference = 'Stop'
$repo = (Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '../../..')).Path
$project = Join-Path $PSScriptRoot '.work/P'
foreach ($folder in @('Assets/Editor', 'Assets/Interop', 'Assets/Plugins/Android/armeabi-v7a', 'Assets/Packages', 'Packages', 'ProjectSettings')) {
    New-Item -ItemType Directory -Path (Join-Path $project $folder) -Force | Out-Null
}
Copy-Item -LiteralPath (Join-Path $repo 'Assets/Plugins/MajdataPlay/Net/Curl/Core/PInvoke') -Destination (Join-Path $project 'Assets/Interop') -Recurse -Force
Copy-Item -LiteralPath (Join-Path $repo 'Assets/Packages/System.Runtime.CompilerServices.Unsafe.6.1.2') -Destination (Join-Path $project 'Assets/Packages') -Recurse -Force
foreach ($file in @('libcurl.so', 'libcurl.so.meta')) {
    Copy-Item -LiteralPath (Join-Path $repo "Assets/Plugins/MajdataPlay/Net/Curl/Plugins/Android/armeabi-v7a/$file") -Destination (Join-Path $project "Assets/Plugins/Android/armeabi-v7a/$file") -Force
}
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'Smoke.cs') -Destination (Join-Path $project 'Assets/Smoke.cs') -Force
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'SmokeBuild.cs') -Destination (Join-Path $project 'Assets/Editor/SmokeBuild.cs') -Force
Set-Content -LiteralPath (Join-Path $project 'Packages/manifest.json') -Value '{"dependencies": {}}' -Encoding utf8
Set-Content -LiteralPath (Join-Path $project 'ProjectSettings/ProjectVersion.txt') -Value 'm_EditorVersion: 6000.3.17f1' -Encoding utf8
Set-Content -LiteralPath (Join-Path $project 'Assets/csc.rsp') -Value '-unsafe' -Encoding utf8
$log = Join-Path $project 'build.log'
$arguments = @('-batchmode', '-nographics', '-quit', '-projectPath', ('"' + $project + '"'), '-buildTarget', 'Android', '-logFile', ('"' + $log + '"'), '-executeMethod', 'CurlAbiSmokeBuild.Run')
$process = Start-Process -FilePath $UnityEditor -ArgumentList $arguments -WindowStyle Hidden -PassThru
Write-Output "Unity PID: $($process.Id); log: $log"
$process.WaitForExit()
if ($process.ExitCode -ne 0) {
    Get-Content -LiteralPath $log -Tail 70
    throw "Unity exited with $($process.ExitCode)"
}
Write-Output "APK: $project/Output/CurlAbi.apk"
