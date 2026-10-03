param(
    [Parameter(Mandatory=$true)][string] $Apk,
    [string] $Serial,
    [string] $Adb = 'adb',
    [switch] $SkipInstall,
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
Invoke-Adb @('get-state') | Out-Null
$sdk = [int](Invoke-Adb @('shell','getprop','ro.build.version.sdk'))
if ($sdk -lt 26) { throw 'AHardwareBuffer playback requires Android API 26 or newer.' }
if (-not $SkipInstall) { Invoke-Adb @('install','-r',$apkPath) | Write-Output }
$report = "/sdcard/Android/data/$package/files/ffmpeg-smoke.txt"
# Only remove this test app's previous report; preserve device logs and media.
Invoke-Adb @('shell','rm','-f',$report) | Out-Null
Invoke-Adb @('shell','am','force-stop',$package) | Out-Null
$activity = Invoke-Adb @('shell','cmd','package','resolve-activity','--brief',$package)
$component = @($activity | Where-Object { $_ -match '^net\.majdata\.ffmpegplayer\.validation/' }) | Select-Object -Last 1
if (-not $component) { throw "Cannot resolve test activity: $activity" }
Invoke-Adb @('shell','am','start','-n',$component.Trim()) | Out-Null
$deadline = [DateTime]::UtcNow.AddSeconds($TimeoutSeconds)
do {
    Start-Sleep -Milliseconds 1000
    $result = (& $Adb @selector shell cat $report 2>$null) -join "`n"
    if ($LASTEXITCODE -eq 0 -and $result -match '^(PASS|FAIL):') {
        Write-Output $result
        $destination = Join-Path (Split-Path -Parent $apkPath) 'vulkan-hardware-device.txt'
        [IO.File]::WriteAllText($destination, $result)
        $diagnostics = Invoke-Adb @('shell','cat',"$report.diagnostics.log")
        [IO.File]::WriteAllLines("$destination.diagnostics.log", [string[]]$diagnostics)
        if ($result -notmatch '^PASS:.*Vulkan.*(AHardwareBuffer|Vulkan Video native decode)') { throw 'Android hardware playback failed.' }
        exit 0
    }
} while ([DateTime]::UtcNow -lt $deadline)
& $Adb @selector logcat -d -t 200 Unity:I '*:S'
throw 'Android test timed out; inspect the Unity log and FFmpeg error message above.'
