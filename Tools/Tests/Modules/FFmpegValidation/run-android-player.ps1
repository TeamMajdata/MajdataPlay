param(
    [Parameter(Mandatory=$true)][string] $Apk,
    [string] $Serial,
    [string] $Adb = 'adb',
    [switch] $SkipInstall,
    [switch] $Av1Software,
    [string] $Media,
    [int] $TimeoutSeconds = 120
)
$ErrorActionPreference = 'Stop'
$package = 'net.majdata.ffmpegplayer.validation'
$selector = @()
if ($Serial) { $selector = @('-s', $Serial) }
function Invoke-Adb([string[]] $Arguments) {
    $output = & $Adb @selector @Arguments 2>&1
    if ($LASTEXITCODE -ne 0) { throw "adb failed: $output" }
    return $output
}
$apkPath = (Resolve-Path -LiteralPath $Apk).Path
if ($Av1Software -and -not $Media) { throw 'AV1 software validation requires -Media with an 8-bit or 10-bit AV1 fixture.' }
if ($Media -and -not $Av1Software) { throw '-Media currently requires -Av1Software.' }
$mediaPath = if ($Media) { (Resolve-Path -LiteralPath $Media).Path } else { $null }
Invoke-Adb @('get-state') | Out-Null
$sdk = [int](Invoke-Adb @('shell','getprop','ro.build.version.sdk'))
if ($sdk -lt 23) { throw 'This test APK requires Android API 23 or newer.' }
if (-not $Av1Software -and $sdk -lt 26) { throw 'AHardwareBuffer playback requires Android API 26 or newer.' }
if (-not $SkipInstall) { Invoke-Adb @('install','-r',$apkPath) | Write-Output }
$report = "/sdcard/Android/data/$package/files/ffmpeg-smoke.txt"
Invoke-Adb @('shell','am','force-stop',$package) | Out-Null
if ($Av1Software) {
    $deviceMedia = "/sdcard/Android/data/$package/files/ffmpeg-smoke-input.mp4"
    Invoke-Adb @('shell','mkdir','-p',"/sdcard/Android/data/$package/files") | Out-Null
    Invoke-Adb @('push',$mediaPath,$deviceMedia) | Out-Null
}
# Only remove this test app's previous report; preserve device logs and media.
Invoke-Adb @('shell','rm','-f',$report) | Out-Null
$activity = Invoke-Adb @('shell','cmd','package','resolve-activity','--brief',$package)
$component = @($activity | Where-Object { $_ -match '^net\.majdata\.ffmpegplayer\.validation/' }) | Select-Object -Last 1
if (-not $component) { throw "Cannot resolve test activity: $activity" }
$startArguments = @('shell','am','start','-n',$component.Trim())
if ($Av1Software) { $startArguments += @('--es','videoAv1Software','true','--es','videoMedia',$deviceMedia) }
Invoke-Adb $startArguments | Out-Null
$deadline = [DateTime]::UtcNow.AddSeconds($TimeoutSeconds)
do {
    Start-Sleep -Milliseconds 1000
    $result = (& $Adb @selector shell cat $report 2>$null) -join "`n"
    if ($LASTEXITCODE -eq 0 -and $result -match '^(PASS|FAIL):') {
        Write-Output $result
        $reportName = if ($Av1Software) { 'av1-software-device.txt' } else { 'vulkan-hardware-device.txt' }
        $destination = Join-Path (Split-Path -Parent $apkPath) $reportName
        [IO.File]::WriteAllText($destination, $result)
        $diagnostics = Invoke-Adb @('shell','cat',"$report.diagnostics.log")
        [IO.File]::WriteAllLines("$destination.diagnostics.log", [string[]]$diagnostics)
        if ($Av1Software) {
            if ($result -notmatch '^PASS:.*Software RGBA upload') { throw 'Android AV1 software playback failed.' }
            Invoke-Adb @('pull',"$report.png","$destination.png") | Out-Null
        } elseif ($result -notmatch '^PASS:.*Vulkan.*(AHardwareBuffer|Vulkan Video native decode)') { throw 'Android hardware playback failed.' }
        exit 0
    }
} while ([DateTime]::UtcNow -lt $deadline)
& $Adb @selector logcat -d -t 200 Unity:I '*:S'
throw 'Android test timed out; inspect the Unity log and FFmpeg error message above.'
