param([string]$JavaHome = $env:JAVA_HOME)
$ErrorActionPreference = 'Stop'
if (-not $JavaHome) { throw 'Set JAVA_HOME or pass -JavaHome (JDK 17+).' }
$project = (Resolve-Path "$PSScriptRoot/../..").Path
$src = "$project/Assets/Plugins/Android/src/java/net/majdata/majdataplay/oniimai"
$out = "$project/Library/OniimaiTests"
New-Item -ItemType Directory -Force $out | Out-Null
$files = @('Protocol','CommandChannel','KeyboardState','LedChannel','LedFrames','LedOutput','Io4Output','CeilingOutput','InputSnapshot','DisplayGeometry','OutputMode','PortSelection','SetupDefaults','DashboardLayout','WidgetFields','UiText','UiTextCatalog') | ForEach-Object { "$src/$_.java" }
$files += @(Get-ChildItem $PSScriptRoot -Filter '*.java' -Recurse | ForEach-Object FullName)
& "$JavaHome/bin/javac.exe" -encoding UTF-8 --release 17 -d $out $files
if ($LASTEXITCODE -ne 0) { throw 'javac failed' }
foreach ($test in @('ProtocolTest','ChannelTest','KeyboardStateTest','LedTest','InputSnapshotTest','LedOutputTest','Io4OutputTest','CeilingOutputTest','DisplayGeometryTest','OutputModeTest','PortSelectionTest','SetupDefaultsTest','DashboardLayoutTest','WidgetFieldsTest')) {
    & "$JavaHome/bin/java.exe" -cp $out "net.majdata.majdataplay.oniimai.$test"
    if ($LASTEXITCODE -ne 0) { throw "$test failed" }
}
# Only the isolated frame merge is built with dotnet; the Unity game is built by Unity.
if (@(dotnet --list-sdks).Count -gt 0) {
dotnet run --project "$PSScriptRoot/FrameMerge/FrameMerge.csproj" --configuration Release
if ($LASTEXITCODE -ne 0) { throw 'Frame merge test failed' }

} else { Write-Output "C# frame checks will run in OniimaiBuild.Android (Unity)." }
