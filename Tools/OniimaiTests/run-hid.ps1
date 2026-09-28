param([string]$JavaHome = $env:JAVA_HOME)
$ErrorActionPreference = 'Stop'
if (-not $JavaHome) { throw 'Set JAVA_HOME or pass -JavaHome (JDK 17+).' }
$project = (Resolve-Path "$PSScriptRoot/../..").Path
$src = "$project/Assets/Plugins/Android/src/java/net/majdata/majdataplay/oniimai"
$out = "$project/Library/OniimaiHidTests"
New-Item -ItemType Directory -Force "$out/src", "$out/classes" | Out-Null
$files = @("$src/UsbIo.java", "$src/Io4Output.java", "$src/Protocol.java")
foreach ($fixture in Get-ChildItem "$PSScriptRoot/hid-transport" -Filter '*.fixture') {
    $target = "$out/src/$($fixture.Name.Replace('.fixture',''))"
    Copy-Item -LiteralPath $fixture.FullName -Destination $target
    $files += $target
}
& "$JavaHome/bin/javac.exe" -encoding UTF-8 --release 17 -d "$out/classes" $files
if ($LASTEXITCODE -ne 0) { throw 'HID javac failed' }
& "$JavaHome/bin/java.exe" -cp "$out/classes" net.majdata.majdataplay.oniimai.UsbTransportTest
if ($LASTEXITCODE -ne 0) { throw 'HID transport test failed' }
