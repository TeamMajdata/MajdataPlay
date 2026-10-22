[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)][string] $Apk,
    [ValidateSet('armv7', 'arm64')][string] $Architecture = 'arm64',
    [string] $Adb = 'C:/Program Files/Unity Editors/6000.3.17f1/Editor/Data/PlaybackEngines/AndroidPlayer/SDK/platform-tools/adb.exe',
    [string] $Serial = '',
    [string] $Media = '',
    [ValidateRange(30, 900)][int] $TimeoutSeconds = 300,
    [switch] $SkipInstall
)
$ErrorActionPreference = 'Stop'
$package = 'net.majdata.ffmpegcontent.validation'
$activity = 'net.majdata.ffmpegplayer.validation.AndroidContentDeviceActivity'
$apkPath = (Resolve-Path -LiteralPath $Apk).Path
if (-not $Serial) {
    $devices = @(& $Adb devices | Where-Object { $_ -match '^([^\s]+)\s+device$' } | ForEach-Object { ($_ -split '\s+')[0] })
    if ($devices.Count -ne 1) { throw 'Connect exactly one authorized Android device or pass -Serial.' }
    $Serial = $devices[0]
}
$selector = @('-s', $Serial)
function Invoke-Adb([string[]] $Arguments) {
    $result = & $Adb @selector @Arguments 2>&1
    if ($LASTEXITCODE -ne 0) { throw "ADB failed ($LASTEXITCODE): $($Arguments -join ' '); $($result -join [Environment]::NewLine)" }
    return $result
}
$abis = (Invoke-Adb @('shell', 'getprop', 'ro.product.cpu.abilist')) -join ''
$abi = if ($Architecture -eq 'arm64') { 'arm64-v8a' } else { 'armeabi-v7a' }
if ($abi -notin ($abis.Trim() -split ',')) { throw "The connected device does not support $abi." }
$api = ((Invoke-Adb @('shell', 'getprop', 'ro.build.version.sdk')) -join '').Trim()
$model = ((Invoke-Adb @('shell', 'getprop', 'ro.product.model')) -join '').Trim()
Write-Output "Device: $model; API $api; requested ABI $abi"
Write-Output "APK SHA256: $((Get-FileHash -LiteralPath $apkPath -Algorithm SHA256).Hash)"
if (-not $SkipInstall) { Invoke-Adb @('install', '-r', $apkPath) | Write-Output }
$installed = Invoke-Adb @('shell', 'dumpsys', 'package', $package)
$installedAbi = ($installed | Select-String -Pattern 'primaryCpuAbi=' | Select-Object -First 1).Line
if (-not $installedAbi -or $installedAbi -notmatch [regex]::Escape($abi)) { throw "Installed test package ABI does not match ${abi}: $installedAbi" }
# Only a new random directory is created; no existing user document is overwritten.
if ($Media) {
    $mediaPath = (Resolve-Path -LiteralPath $Media).Path
    $deviceFolder = '/sdcard/Download/MajdataFFmpegContentValidation-' + [Guid]::NewGuid().ToString('N').Substring(0, 8)
    $deviceMedia = "$deviceFolder/test.mp4"
    Invoke-Adb @('shell', 'mkdir', '-p', $deviceFolder) | Out-Null
    Invoke-Adb @('push', $mediaPath, $deviceMedia) | Write-Output
    Write-Output "On the phone, choose Download/$([IO.Path]::GetFileName($deviceFolder))/test.mp4 in the document picker."
    Write-Output 'The fixture is kept on the device; this runner never deletes selected documents or revokes their grants.'
}
$report = "/sdcard/Android/data/$package/files/ffmpeg-content-smoke.txt"
Invoke-Adb @('shell', 'am', 'force-stop', $package) | Out-Null
# Delete only the previous report in this dedicated validation app, never game data.
Invoke-Adb @('shell', 'rm', '-f', $report) | Out-Null
Invoke-Adb @('shell', 'am', 'start', '-n', "$package/$activity") | Out-Null
$deadline = [DateTime]::UtcNow.AddSeconds($TimeoutSeconds)
$processId = ''
while ([DateTime]::UtcNow -lt $deadline) {
    Start-Sleep -Milliseconds 1000
    if (-not $processId) { $processId = ((& $Adb @selector shell pidof $package 2>$null) -join '').Trim() }
    $result = (& $Adb @selector shell cat $report 2>$null) -join [Environment]::NewLine
    if ($LASTEXITCODE -eq 0 -and $result -match '^(PASS|FAIL):') {
        $destination = Join-Path (Split-Path -Parent $apkPath) "content-$Architecture-device.txt"
        [IO.File]::WriteAllText($destination, $result)
        if ($processId -match '^\d+$') {
            $diagnostics = & $Adb @selector logcat -d --pid=$processId -v threadtime 2>&1
            [IO.File]::WriteAllLines("$destination.logcat.txt", [string[]]$diagnostics)
        }
        Write-Output $result
        Write-Output "Result: $destination"
        if ($result -notmatch '^PASS:') { throw 'Native content device validation failed; inspect the durable report and process log.' }
        $bits = if ($Architecture -eq 'arm64') { '64' } else { '32' }
        if ($result -notmatch "processBits=$bits" -or $result -notmatch 'backend=IL2CPP' -or $result -notmatch 'contentProtocol=True') {
            throw 'The report does not establish the requested ABI, IL2CPP backend and native content protocol.'
        }
        return
    }
}
throw "Native content device validation timed out. Check the phone's picker and $report; the app was not stopped."
