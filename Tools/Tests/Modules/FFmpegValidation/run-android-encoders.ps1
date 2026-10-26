[CmdletBinding()]
param(
    [ValidateSet('arm64', 'armv7')][string] $Architecture = 'arm64',
    [string] $UnityEditor = 'C:/Program Files/Unity Editors/6000.3.17f1/Editor',
    [string] $Adb = 'adb',
    [string] $Serial = '',
    [ValidateRange(30, 900)][int] $TimeoutSeconds = 300
)
$ErrorActionPreference = 'Stop'
$root = (Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '../../../..')).Path
$android = Join-Path $UnityEditor 'Data/PlaybackEngines/AndroidPlayer'
$ndk = Join-Path $android 'NDK'
$sdk = Join-Path $android 'SDK'
$javac = Join-Path $android 'OpenJDK/bin/javac.exe'
$java = Join-Path $android 'OpenJDK/bin/java.exe'
$headers = Join-Path $root 'ThirdParty/FFmpeg.AutoGen/FFmpeg/include'
$abi = if ($Architecture -eq 'arm64') { 'arm64-v8a' } else { 'armeabi-v7a' }
$triple = if ($Architecture -eq 'arm64') { 'aarch64-linux-android23' } else { 'armv7a-linux-androideabi23' }
$process = if ($Architecture -eq 'arm64') { '/system/bin/app_process64' } else { '/system/bin/app_process32' }
$clang = Join-Path $ndk 'toolchains/llvm/prebuilt/windows-x86_64/bin/clang.exe'
$native = Join-Path $root "Assets/Plugins/MajdataPlay/FFmpeg/Native/Android/$abi"
$androidJar = Get-ChildItem (Join-Path $sdk 'platforms') -Directory |
    Where-Object { $_.Name -match '^android-(\d+)$' } |
    Sort-Object { [int]($_.Name -replace '^android-', '') } -Descending |
    ForEach-Object { Join-Path $_.FullName 'android.jar' } | Select-Object -First 1
$d8 = Get-ChildItem (Join-Path $sdk 'build-tools') -Directory |
    Where-Object { Test-Path -LiteralPath (Join-Path $_.FullName 'lib/d8.jar') } |
    Sort-Object { [version]$_.Name } -Descending |
    ForEach-Object { Join-Path $_.FullName 'lib/d8.jar' } | Select-Object -First 1
foreach ($file in @($clang, $javac, $java, $androidJar, $d8, (Join-Path $native 'libavcodec.so'))) {
    if (-not $file -or -not (Test-Path -LiteralPath $file)) { throw "Missing validation prerequisite: $file" }
}
if (-not $Serial) {
    $devices = @(& $Adb devices | Where-Object { $_ -match '^([^\s]+)\s+device$' } |
        ForEach-Object { ($_ -split '\s+')[0] })
    if ($devices.Count -ne 1) { throw 'Connect exactly one authorized Android device or pass -Serial.' }
    $Serial = $devices[0]
}
# Device identity is passed as arguments. All remote paths below are generated from safe literals.
$selector = @('-s', $Serial)
function Invoke-Adb([string[]] $Arguments) {
    $result = & $Adb @selector @Arguments 2>&1
    if ($LASTEXITCODE -ne 0) { throw "ADB failed ($LASTEXITCODE): $($result -join [Environment]::NewLine)" }
    return $result
}
$sdkVersion = [int]((Invoke-Adb @('shell', 'getprop', 'ro.build.version.sdk')) -join '')
if ($sdkVersion -lt 29) { throw 'Hardware classification requires Android API 29+; the native libraries still target API 23.' }
$abis = ((Invoke-Adb @('shell', 'getprop', 'ro.product.cpu.abilist')) -join '').Trim() -split ','
if ($abi -notin $abis) { throw "The connected device does not support $abi." }
$runId = [DateTime]::UtcNow.ToString('yyyyMMdd-HHmmss-fffffff')
$work = Join-Path $PSScriptRoot ".work/android-encoders/$Architecture-$runId"
$classes = Join-Path $work 'classes'
$dex = Join-Path $work 'dex'
New-Item -ItemType Directory -Path $classes, $dex | Out-Null
$library = Join-Path $work 'libandroid-encoder-smoke.so'
& $clang "--target=$triple" '-std=c11' '-Wall' '-Wextra' '-Werror' '-shared' '-fPIC' '-Wl,-z,max-page-size=16384' '-Wl,--no-undefined' "-I$headers" (Join-Path $PSScriptRoot 'AndroidEncoderSmoke.c') "-L$native" '-lavformat' '-lavcodec' '-lswscale' '-lavutil' '-lm' '-o' $library
if ($LASTEXITCODE -ne 0) { throw 'Android native encoder regression compilation failed.' }
& $javac '--release' '8' '-classpath' $androidJar '-d' $classes (Join-Path $PSScriptRoot 'AndroidEncoderSmoke.java')
if ($LASTEXITCODE -ne 0) { throw 'Android hardware selector compilation failed.' }
$classFiles = @(Get-ChildItem $classes -Recurse -Filter '*.class' | ForEach-Object { $_.FullName })
& $java '-cp' $d8 'com.android.tools.r8.D8' '--lib' $androidJar '--min-api' '29' '--output' $dex @classFiles
if ($LASTEXITCODE -ne 0) { throw 'Android hardware selector dex generation failed.' }
$remote = "/data/local/tmp/majdata-encoder-$Architecture-$runId"
Invoke-Adb @('shell', 'mkdir', '-p', "$remote/lib", "$remote/output") | Out-Null
foreach ($file in Get-ChildItem $native -Filter '*.so') {
    Invoke-Adb @('push', $file.FullName, "$remote/lib/$($file.Name)") | Out-Null
}
Invoke-Adb @('push', $library, "$remote/lib/libandroid-encoder-smoke.so") | Out-Null
Invoke-Adb @('push', (Join-Path $dex 'classes.dex'), "$remote/classes.dex") | Out-Null
# A shell VM permits real JNI/NDK calls without installing or changing an application.
# Isolate the process and all media in a unique directory; retain evidence for inspection.
$launch = 'cd {0} && export LD_LIBRARY_PATH={0}/lib CLASSPATH={0}/classes.dex; timeout {2} {1} /system/bin net.majdata.ffmpegvalidation.AndroidEncoderSmoke {0}/lib {0}/output >{0}/report.txt 2>&1; echo $? >{0}/exit-code.txt' -f $remote, $process, $TimeoutSeconds
$launchFile = Join-Path $work 'run.sh'
$launch | Set-Content -LiteralPath $launchFile -Encoding ascii -NoNewline
Invoke-Adb @('push', $launchFile, "$remote/run.sh") | Out-Null
Write-Output "Running Android $abi hardware encoder checks; evidence: $work"
$run = Start-Process -FilePath (Get-Command $Adb).Source -ArgumentList @('-s', $Serial, 'shell', 'sh', "$remote/run.sh") -WindowStyle Hidden -PassThru
$completed = $run.WaitForExit(($TimeoutSeconds + 30) * 1000)
if (-not $completed) {
    # Do not stop unrelated apps or media processes on timeout.
    throw "Android encoder validation timed out. Device evidence: $remote"
}
Invoke-Adb @('pull', "$remote/report.txt", (Join-Path $work 'device.txt')) | Out-Null
Invoke-Adb @('pull', "$remote/output", $work) | Out-Null
$report = Get-Content -LiteralPath (Join-Path $work 'device.txt') -Raw
$report -split '\r?\n' | Where-Object { $_ -match '^(REGISTERED|DEVICE|SKIP|PASS|FAIL|UNVERIFIED):?' } | Write-Output
$exitCode = ((Invoke-Adb @('shell', 'cat', "$remote/exit-code.txt")) -join '').Trim()
if ($exitCode -ne '0' -or $report -notmatch '(?m)^PASS: [1-9][0-9]* hardware roundtrips;' -or $report -match '(?m)^FAIL:') {
    throw "Android native hardware encoding failed. See $work/device.txt"
}
Write-Output "PASS: Android $abi device encoding. Full logs and encoded media: $work"
