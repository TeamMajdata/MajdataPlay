param(
    [string] $SourceBuild = 'E:/repos/MajdataPlay/MajdataPlay/Build',
    [ValidateSet('MainPlayer', 'MainSynthetic')][string] $DestinationName = 'MainPlayer',
    [string] $FFprobe = 'E:/ffmpeg-7.0.1-full_build/bin/ffprobe.exe'
)
$ErrorActionPreference = 'Stop'
$source = (Resolve-Path -LiteralPath $SourceBuild).Path.TrimEnd('\')
$module = (Resolve-Path -LiteralPath $PSScriptRoot).Path
$work = Join-Path $module '.work'
$target = Join-Path $work $DestinationName
if (Test-Path -LiteralPath $target) {
    throw "Destination must not exist; refusing to overwrite $target"
}
$chartRelative = 'MaiCharts\Original\Transcend My Love'
$video = Join-Path $source "$chartRelative\pv.mp4"
$metadataText = & $FFprobe -v error -select_streams v:0 -show_entries stream=codec_name,width,height,avg_frame_rate -of json $video
if ($LASTEXITCODE -ne 0) {
    throw 'ffprobe could not read the source fixture.'
}
$metadata = ($metadataText -join "`n") | ConvertFrom-Json
if ($metadata.streams[0].width -ne 854 -or $metadata.streams[0].height -ne 480) {
    throw 'The selected source pv.mp4 must be 854 x 480.'
}
$files = New-Object 'System.Collections.Generic.List[object]'
foreach ($relative in @('MajdataPlay.exe', 'UnityPlayer.dll', 'UnityCrashHandler64.exe', 'settings.json', 'system-settings.json')) {
    $files.Add((Get-Item -LiteralPath (Join-Path $source $relative)))
}
foreach ($relative in @('MajdataPlay_Data', 'MonoBleedingEdge', 'D3D12', 'Skins', $chartRelative)) {
    $root = Join-Path $source $relative
    if (-not (Test-Path -LiteralPath $root -PathType Container)) {
        throw "Required source directory is missing: $relative"
    }
    foreach ($entry in Get-ChildItem -LiteralPath $root -Recurse -Force) {
        if ($entry.Attributes -band [System.IO.FileAttributes]::ReparsePoint) {
            throw "Refusing a reparse point in selected input: $($entry.FullName)"
        }
        if ($entry.PSIsContainer) {
            continue
        }
        $name = $entry.FullName.Substring($source.Length + 1)
        if ($name.StartsWith('MajdataPlay_Data\StreamingAssets\MaiCharts\', [StringComparison]::OrdinalIgnoreCase)) {
            continue
        }
        if ($relative -eq $chartRelative -and $entry.Name.EndsWith('.orig', [StringComparison]::OrdinalIgnoreCase)) {
            continue
        }
        $files.Add($entry)
    }
}
$total = ($files | Measure-Object -Property Length -Sum).Sum
$drive = Get-PSDrive -Name ((Get-Item -LiteralPath $module).PSDrive.Name)
$required = [long]($total * 1.1) + 1GB
if ($drive.Free -lt $required) {
    throw "Insufficient free space. Need $required bytes including reserve; have $($drive.Free)."
}
Write-Output ("PREPARE files={0} bytes={1} freeBytes={2}; source pv={3}x{4}@{5}" -f $files.Count, $total,
    $drive.Free, $metadata.streams[0].width, $metadata.streams[0].height, $metadata.streams[0].avg_frame_rate)
New-Item -ItemType Directory -Path $target | Out-Null
$manifest = New-Object 'System.Collections.Generic.List[object]'
foreach ($file in $files) {
    $relative = $file.FullName.Substring($source.Length + 1)
    $destination = Join-Path $target $relative
    New-Item -ItemType Directory -Path (Split-Path -Parent $destination) -Force | Out-Null
    $sourceHash = (Get-FileHash -LiteralPath $file.FullName -Algorithm SHA256).Hash
    Copy-Item -LiteralPath $file.FullName -Destination $destination
    $destinationHash = (Get-FileHash -LiteralPath $destination -Algorithm SHA256).Hash
    if ($sourceHash -ne $destinationHash) {
        throw "Copy hash mismatch: $relative"
    }
    $manifest.Add([pscustomobject]@{ Path = $relative; Bytes = $file.Length; SourceSHA256 = $sourceHash; CopiedSHA256 = $destinationHash })
}
# Only the isolated configuration is parsed and rewritten; its credentials are never printed.
$systemPath = Join-Path $target 'system-settings.json'
$system = Get-Content -LiteralPath $systemPath -Raw | ConvertFrom-Json
$system.Network.Online.Enable = $false
$system.Common.RecordMode = 'Disable'
$system.Decoding.PreferredDecoder = 'Hardware'
$system.Rendering.VSync = $true
$system.Rendering.FPSLimit = 144
$system.Rendering.VulkanLowLatencyVSync = $false
$system.Rendering.FullScreen = $true
$system.Debug.DebugLevel = 'Info'
$system.IO.Manufacturer = 'General'
$system.IO.InputDevice.ButtonRing.Enable = $true
$system.IO.InputDevice.ButtonRing.Type = 'Keyboard'
$system.IO.InputDevice.TouchPanel.Enable = $false
$system.IO.OutputDevice.Led.Enable = $false
# PowerShell's Unicode encoding preserves any non-ASCII setting values without a UTF-8 BOM.
$system | ConvertTo-Json -Depth 40 | Set-Content -LiteralPath $systemPath -Encoding Unicode
# Seed only the selected difficulty through the game's supported legacy import.
# No personal runtime database or song path is copied from the source build.
$runtimeSeed = Join-Path $target 'Runtime'
New-Item -ItemType Directory -Path $runtimeSeed | Out-Null
Set-Content -LiteralPath (Join-Path $runtimeSeed 'config.json') -Value '{"List":{"SelectedDiff":4}}' -Encoding ascii
$manifest | Export-Csv -LiteralPath (Join-Path $target 'copy-manifest.csv') -NoTypeInformation -Encoding utf8
$configHashes = @{}
foreach ($name in @('settings.json', 'system-settings.json')) {
    $configHashes[$name] = (Get-FileHash -LiteralPath (Join-Path $target $name) -Algorithm SHA256).Hash
    $sourceNow = (Get-FileHash -LiteralPath (Join-Path $source $name) -Algorithm SHA256).Hash
    $sourceBefore = ($manifest | Where-Object Path -eq $name).SourceSHA256
    if ($sourceNow -ne $sourceBefore) {
        throw "Source configuration changed during preparation: $name"
    }
}
[ordered]@{
    PreparedAt = (Get-Date).ToString('o'); Source = $source; Destination = $target
    Files = $files.Count; CopiedBytes = $total; ConfigSHA256 = $configHashes; Video = $metadata.streams[0]
    SourceVideoSHA256 = ($manifest | Where-Object Path -eq "$chartRelative\pv.mp4").SourceSHA256
    FFprobe = $metadata
    BuiltInChartsCopied = $false; OriginalVideoBackupsCopied = $false
    NetworkOnline = $false; RecordMode = 'Disable'; Decoder = 'Hardware'; VSync = $true; FPSLimit = 144
    VulkanLowLatencyVSync = $false; FullScreen = $true; ButtonRing = 'Keyboard'; TouchPanel = $false; Led = $false
    InitialDifficulty = 'Master'; RuntimeConfigSeed = 'Runtime/config.json legacy import'
} | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath (Join-Path $target 'fixture-manifest.json') -Encoding utf8
Write-Output "PREPARE DONE: $target"
Write-Output 'No game was launched. Runtime, cache, databases, and logs were not copied.'
