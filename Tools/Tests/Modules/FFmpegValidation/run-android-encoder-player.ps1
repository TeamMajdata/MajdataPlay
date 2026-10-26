[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)][string] $Apk,
    [string] $Adb = 'adb',
    [string] $Serial = '',
    [ValidateSet('H264', 'HEVC')][string] $Format = 'H264',
    [switch] $StandardSizeOnly,
    [switch] $ExpectCbrSoftwareFallback,
    [ValidateRange(30, 900)][int] $TimeoutSeconds = 300,
    [switch] $SkipInstall
)
$ErrorActionPreference = 'Stop'
$package = 'net.majdata.ffmpegencoder.validation'
$apkPath = (Resolve-Path -LiteralPath $Apk).Path
if (-not $Serial) {
    $devices = @(& $Adb devices | Where-Object { $_ -match '^([^\s]+)\s+device$' } |
        ForEach-Object { ($_ -split '\s+')[0] })
    if ($devices.Count -ne 1) { throw 'Connect exactly one authorized Android device or pass -Serial.' }
    $Serial = $devices[0]
}
$selector = @('-s', $Serial)
function Invoke-Adb([string[]] $Arguments) {
    $result = & $Adb @selector @Arguments 2>&1
    if ($LASTEXITCODE -ne 0) { throw "ADB failed ($LASTEXITCODE): $($result -join [Environment]::NewLine)" }
    return $result
}
Invoke-Adb @('get-state') | Out-Null
$power = (Invoke-Adb @('shell', 'dumpsys', 'power')) -join [Environment]::NewLine
$keyguard = (Invoke-Adb @('shell', 'dumpsys', 'window', 'policy')) -join [Environment]::NewLine
if ($power -notmatch 'mWakefulness=Awake' -or $keyguard -match '(?m)^\s*showing=true\s*$') {
    throw 'Unlock the connected device and keep its screen on before running the Unity Player validation.'
}
if (-not $SkipInstall) { Invoke-Adb @('install', '-r', $apkPath) | Write-Output }
$remoteRoot = "/sdcard/Android/data/$package/files"
$report = "$remoteRoot/ffmpeg-encoder-smoke.txt"
Invoke-Adb @('shell', 'am', 'force-stop', $package) | Out-Null
Invoke-Adb @('shell', 'rm', '-f', $report) | Out-Null
$activities = Invoke-Adb @('shell', 'cmd', 'package', 'resolve-activity', '--brief', $package)
$component = @($activities | Where-Object { $_ -match ('^' + [regex]::Escape($package) + '/') }) | Select-Object -Last 1
if (-not $component) { throw "Cannot resolve validation activity: $activities" }
$launchArguments = @('shell', 'am', 'start', '-n', $component.Trim(), '--es', 'encoderFormat', $Format)
if ($StandardSizeOnly) { $launchArguments += @('--es', 'encoderStandardSizeOnly', 'true') }
if ($ExpectCbrSoftwareFallback) { $launchArguments += @('--es', 'encoderExpectCbrSoftwareFallback', 'true') }
Invoke-Adb $launchArguments | Out-Null
$deadline = [DateTime]::UtcNow.AddSeconds($TimeoutSeconds)
do {
    Start-Sleep -Milliseconds 1000
    $text = (& $Adb @selector shell cat $report 2>$null) -join [Environment]::NewLine
    if ($LASTEXITCODE -ne 0 -or $text -notmatch '^(PASS|FAIL):') { continue }
    $destination = Join-Path (Split-Path -Parent $apkPath) ('encoder-device-' + [DateTime]::UtcNow.ToString('yyyyMMdd-HHmmss'))
    New-Item -ItemType Directory -Path $destination | Out-Null
    Invoke-Adb @('pull', $report, (Join-Path $destination 'device.txt')) | Out-Null
    Invoke-Adb @('pull', "$report.diagnostics.log", (Join-Path $destination 'diagnostics.log')) | Out-Null
    # Collect only outputs under this validation application's own files directory.
    $outputMatch = [regex]::Match($text, '(?m)^OUTPUT: (.+)$')
    if ($outputMatch.Success) {
        $output = $outputMatch.Groups[1].Value.Trim()
        if ($output -match ('^/(?:sdcard|storage/emulated/0)/Android/data/' + [regex]::Escape($package) + '/files/encoders-[0-9a-f]{32}$')) {
            Invoke-Adb @('pull', $output, (Join-Path $destination 'output')) | Out-Null
        }
    }
    Write-Output $text
    if ($text -notmatch '^PASS:') { throw "Managed Android encoder validation failed: $destination" }
    Write-Output "Device report, diagnostics and encoded files: $destination"
    return
} while ([DateTime]::UtcNow -lt $deadline)
throw "Managed Android encoder validation timed out; inspect $report and this validation app's logcat."
