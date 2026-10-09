[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)][string]$Serial,
    [string]$UnityPath = 'C:/Program Files/Unity Editors/6000.3.17f1/Editor/Unity.exe',
    [string]$ApkPath,
    [ValidateSet('arm64-v8a', 'armeabi-v7a')][string]$Abi = 'arm64-v8a',
    [ValidateSet('full', 'replay', 'release')][string]$Mode = 'full',
    [switch]$Install,
    [switch]$StartOnly,
    [switch]$ObserveOnly,
    [int]$TimeoutSeconds = 600
)

$ErrorActionPreference = 'Stop'
$root = (Resolve-Path (Join-Path $PSScriptRoot '../..')).Path
$adb = Join-Path (Split-Path -Parent $UnityPath) 'Data/PlaybackEngines/AndroidPlayer/SDK/platform-tools/adb.exe'
$package = 'net.majdata.storagevalidation'
$activity = "$package/net.majdata.majdataplay.MajdataPlayActivity"
if (!(Test-Path -LiteralPath $adb -PathType Leaf)) {
    throw "Missing bundled ADB: $adb"
}
$state = & $adb -s $Serial get-state 2>&1
if ($LASTEXITCODE -ne 0 -or "$state".Trim() -ne 'device') {
    throw 'Select one connected, authorized Android device.'
}
$results = Join-Path $root 'Temp/AndroidStorageDeviceValidation/DeviceResults'
New-Item -ItemType Directory -Force -Path $results | Out-Null
$runId = [Guid]::NewGuid().ToString('N')
$logPath = Join-Path $results "$Abi-$Mode-$runId.log"
if ($Install) {
    if (!$ApkPath) {
        $ApkPath = Join-Path $root 'Temp/AndroidStorageDeviceValidation/Artifacts/storage-validation-Both.apk'
    }
    if (!(Test-Path -LiteralPath $ApkPath -PathType Leaf)) {
        throw "Build the isolated IL2CPP APK first: $ApkPath"
    }
    & $adb -s $Serial install --abi $Abi -r $ApkPath
    if ($LASTEXITCODE -ne 0) {
        throw 'Installation failed; no runtime test has passed.'
    }
}
if (!$ObserveOnly) {
    # Only the dedicated validation package is stopped. Its grants and data remain for replay.
    & $adb -s $Serial shell am force-stop $package
    if ($LASTEXITCODE -ne 0) {
        throw 'Cannot stop the dedicated validation application.'
    }
    & $adb -s $Serial shell am start -n $activity --es validationMode $Mode
    if ($LASTEXITCODE -ne 0) {
        throw 'Cannot launch the dedicated validation application.'
    }
}
$deadline = [DateTime]::UtcNow.AddSeconds($TimeoutSeconds)
$seen = [Collections.Generic.HashSet[string]]::new()
do {
    $devicePid = ((& $adb -s $Serial shell pidof $package) -join ' ').Trim().Split(' ')[0]
    if ($devicePid -match '^[0-9]+$') {
        $lines = @(& $adb -s $Serial logcat --pid=$devicePid -d -v brief)
        if ($LASTEXITCODE -ne 0) {
            throw 'Cannot capture validation-process logs.'
        }
        # The private log is ignored; only explicit non-sensitive test markers are displayed.
        $lines | Set-Content -LiteralPath $logPath -Encoding UTF8
        foreach ($line in $lines) {
            if ($line -match '(FILE_SYSTEM_DEVICE_[A-Z0-9_]+)') {
                $marker = $Matches[1]
                if ($seen.Add($marker)) {
                    Write-Output $marker
                }
                if ($marker -match 'FILE_SYSTEM_DEVICE_(?:[A-Z0-9_]*_)?FAILED') {
                    throw "Device validation failed. Private application log: $logPath"
                }
                if ($marker -match ('FILE_SYSTEM_DEVICE_' + $Mode.ToUpperInvariant() + '_PASSED')) {
                    $resultPath = '/sdcard/Android/data/' + $package + '/files/android-storage-device-result.json'
                    $rawResult = (& $adb -s $Serial shell cat $resultPath) -join [Environment]::NewLine
                    if ($LASTEXITCODE -ne 0) {
                        throw 'Cannot read the dedicated application result; a log marker alone is insufficient.'
                    }
                    $result = $rawResult | ConvertFrom-Json
                    $expectedPointerSize = if ($Abi -eq 'arm64-v8a') { 8 } else { 4 }
                    if (!$result.Terminal -or !$result.EnableIl2Cpp -or $result.Mode -ne $Mode -or
                        $result.Marker -ne ('FILE_SYSTEM_DEVICE_' + $Mode.ToUpperInvariant() + '_PASSED') -or
                        $result.ProcessId -ne [int]$devicePid -or $result.PointerSize -ne $expectedPointerSize) {
                        throw 'The durable result does not match this process, mode, requested ABI, or IL2CPP backend.'
                    }
                    $rawResult | Set-Content -LiteralPath (Join-Path $results "$Abi-$Mode-$runId.json") -Encoding UTF8
                    Write-Output "Device validation log: $logPath"
                    Write-Output "Validated actual pointer size: $($result.PointerSize); IL2CPP: $($result.EnableIl2Cpp)."
                    return
                }
            }
        }
    }
    if ($StartOnly) {
        Write-Output "FILE_SYSTEM_DEVICE_RUNNING: choose only a disposable test directory in the SAF picker, then observe completion. Log: $logPath"
        return
    }
    Start-Sleep -Seconds 3
} while ([DateTime]::UtcNow -lt $deadline)
throw "Device validation did not complete within $TimeoutSeconds seconds. Unlock the device and inspect the picker/test screen. Log: $logPath"
