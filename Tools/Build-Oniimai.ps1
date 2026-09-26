param(
    [Parameter(Mandatory=$true)][string]$UnityEditor,
    [string]$Output = ''
)
$ErrorActionPreference = 'Stop'
$project = (Resolve-Path "$PSScriptRoot/..").Path
if (-not $Output) { $Output = Join-Path $project 'Build/Oniimai-MajdataPlay.apk' }
$env:ONIIMAI_APK = [System.IO.Path]::GetFullPath($Output)
New-Item -ItemType Directory -Force (Split-Path $env:ONIIMAI_APK) | Out-Null
$log = Join-Path $project 'Build/Oniimai-build.log'
New-Item -ItemType Directory -Force (Split-Path $log) | Out-Null
$arguments = @('-batchmode','-nographics','-quit','-projectPath',('"' + $project + '"'),'-buildTarget','Android','-executeMethod','OniimaiBuild.Android','-logFile',('"' + $log + '"'))
$process = Start-Process -FilePath $UnityEditor -ArgumentList $arguments -WindowStyle Hidden -Wait -PassThru
if ($process.ExitCode -ne 0 -or -not (Test-Path -LiteralPath $env:ONIIMAI_APK)) { throw "Unity build failed. See $log" }
Write-Output "APK: $env:ONIIMAI_APK"
Write-Output "Log: $log"
