param(
    [string] $UnityEditor = '',
    [ValidateSet('arm64', 'armv7', 'x86_64')][string] $Architecture = 'arm64',
    [string] $WorkDirectory = (Join-Path $env:LOCALAPPDATA 'MajMBA'),
    [string] $Serial = '',
    [switch] $SkipBuild,
    [ValidateRange(10, 600)][int] $TimeoutSeconds = 120
)
# Builds ManagedBass (core and BASSmix) with a smoke script into an isolated Android IL2CPP Player, checks that IL2CPP
# generated a reverse P/Invoke wrapper for every native callback of ManagedBass, and runs the Player on a device when
# -Serial names one. See ../README.md.
$ErrorActionPreference = 'Stop'
$repo = (Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '../../../../..')).Path
$expectedVersion = (Select-String -LiteralPath (Join-Path $repo 'ProjectSettings/ProjectVersion.txt') -Pattern '^m_EditorVersion: (.+)$').Matches[0].Groups[1].Value
if (-not $UnityEditor) {
    $UnityEditor = @("C:\Program Files\Unity\Hub\Editor\$expectedVersion\Editor\Unity.exe", "C:\Program Files\Unity Editors\$expectedVersion\Editor\Unity.exe") |
        Where-Object { Test-Path -LiteralPath $_ -PathType Leaf } | Select-Object -First 1
    if (-not $UnityEditor) { throw "Unity $expectedVersion was not found; pass -UnityEditor." }
}
$selectedVersion = [Diagnostics.FileVersionInfo]::GetVersionInfo($UnityEditor).ProductVersion
if ($selectedVersion -notmatch ('^' + [regex]::Escape($expectedVersion) + '(?:_|$)')) {
    throw "Use Unity $expectedVersion; the selected executable reports $selectedVersion."
}
$abi = @{ 'arm64' = 'arm64-v8a'; 'armv7' = 'armeabi-v7a'; 'x86_64' = 'x86_64' }[$Architecture]
# Android's Gradle and IL2CPP paths are long; keep the isolated project close to the drive root.
$project = Join-Path $WorkDirectory "P-$Architecture"
$output = Join-Path $WorkDirectory "Out-$Architecture"
$apk = Join-Path $output 'ManagedBassSmoke.apk'
$utf8 = New-Object Text.UTF8Encoding($false)
function Write-Text([string] $Path, [string] $Text) {
    New-Item -ItemType Directory -Path (Split-Path -Parent $Path) -Force | Out-Null
    [IO.File]::WriteAllText($Path, $Text, $utf8)
}
function Copy-WithMeta([string] $Relative) {
    $source = Join-Path $repo $Relative
    $destination = Join-Path $project $Relative
    New-Item -ItemType Directory -Path (Split-Path -Parent $destination) -Force | Out-Null
    if (Test-Path -LiteralPath $destination) { Remove-Item -LiteralPath $destination -Recurse -Force }
    Copy-Item -LiteralPath $source -Destination $destination -Recurse -Force
    if (Test-Path -LiteralPath "$source.meta") { Copy-Item -LiteralPath "$source.meta" -Destination "$destination.meta" -Force }
}

if (-not $SkipBuild) {
    # The production ManagedBass sources, BASS and BASSmix for the target ABI with their import settings, and the Unsafe
    # assembly ManagedBass uses, which the game takes from Assets/Packages.
    foreach ($relative in @('Assets/Plugins/ManagedBass/src/Bass', 'Assets/Plugins/ManagedBass/src/AddOns/BassMix',
            "Assets/Plugins/Un4seen.Bass/Android/$abi/libbass.so", "Assets/Plugins/Un4seen.Bass/Android/$abi/libbassmix.so",
            'Assets/Packages/System.Runtime.CompilerServices.Unsafe.6.1.2')) {
        Copy-WithMeta $relative
    }
    # The game passes -langversion:preview (BassMarshal uses a file-scoped namespace) and allows unsafe code project-wide.
    Write-Text (Join-Path $project 'Assets/csc.rsp') "-langversion:preview`n-unsafe`n"
    Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'ManagedBassIl2CppSmoke.cs') -Destination (Join-Path (New-Item -ItemType Directory -Path (Join-Path $project 'Assets/Smoke') -Force) 'ManagedBassIl2CppSmoke.cs') -Force
    Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'ManagedBassIl2CppBuild.cs') -Destination (Join-Path (New-Item -ItemType Directory -Path (Join-Path $project 'Assets/Editor') -Force) 'ManagedBassIl2CppBuild.cs') -Force
    Write-Text (Join-Path $project 'Assets/Smoke/ManagedBass.Il2Cpp.Smoke.asmdef') '{ "name": "ManagedBass.Il2Cpp.Smoke", "references": [ "ManagedBass", "ManagedBass.BassMix" ] }'
    Write-Text (Join-Path $project 'Assets/Editor/ManagedBass.Il2Cpp.Smoke.Editor.asmdef') '{ "name": "ManagedBass.Il2Cpp.Smoke.Editor", "includePlatforms": [ "Editor" ] }'
    # Keep every ManagedBass callback, so that the generated C++ shows a wrapper for each, used or not.
    Write-Text (Join-Path $project 'Assets/Smoke/link.xml') '<linker><assembly fullname="ManagedBass" preserve="all" /><assembly fullname="ManagedBass.BassMix" preserve="all" /></linker>'
    Write-Text (Join-Path $project 'Packages/manifest.json') '{ "dependencies": { } }'
    Write-Text (Join-Path $project 'ProjectSettings/ProjectVersion.txt') "m_EditorVersion: $expectedVersion`n"

    New-Item -ItemType Directory -Path $output -Force | Out-Null
    if (Test-Path -LiteralPath $apk) { Remove-Item -LiteralPath $apk -Force }
    $buildLog = Join-Path $output 'build.log'
    $arguments = @('-batchmode', '-nographics', '-quit', '-projectPath', ('"' + $project + '"'), '-buildTarget', 'Android',
        '-executeMethod', 'MajdataPlay.Validation.Il2Cpp.ManagedBassIl2CppBuild.Run', '-smokeArchitecture', $Architecture,
        '-smokeOutput', ('"' + $output + '"'), '-logFile', ('"' + $buildLog + '"'))
    Write-Output "Building the Android $Architecture IL2CPP Player in $project ..."
    $process = Start-Process -FilePath $UnityEditor -ArgumentList $arguments -WindowStyle Hidden -PassThru
    $process.WaitForExit()
    if ($process.ExitCode -ne 0 -or -not (Test-Path -LiteralPath $apk)) {
        Get-Content -LiteralPath $buildLog -Tail 60
        throw "The Player build failed (exit code $($process.ExitCode)): $buildLog"
    }
}

# IL2CPP throws NotSupportedException when a delegate without a reverse P/Invoke wrapper is passed to native code. It
# generates a wrapper only for static methods carrying a MonoPInvokeCallback attribute.
$expected = @('Bass_OnStream', 'Bass_OnDSP', 'Bass_OnSync', 'Bass_OnDownload', 'Bass_OnRecord', 'Bass_OnIOSNotify',
    'Extensions_WriteSilence', 'ChannelReferences_OnChannelFree', 'GCPin_OnChannelFree', 'FileProcedureTrampolines_OnClose',
    'FileProcedureTrampolines_OnLength', 'FileProcedureTrampolines_OnRead', 'FileProcedureTrampolines_OnSeek', 'BassMix_OnSyncEx')
$generated = Get-ChildItem -LiteralPath (Join-Path $project 'Library/Bee/artifacts/Android') -Recurse -Filter '*.cpp' |
    Where-Object { $_.FullName -match 'il2cppOutput' } |
    Select-String -Pattern 'ReversePInvokeWrapper_([A-Za-z0-9]+_[A-Za-z0-9]+)_m[0-9A-F]+' -AllMatches |
    ForEach-Object { $_.Matches } | ForEach-Object { $_.Groups[1].Value } | Sort-Object -Unique
$missing = @($expected | Where-Object { $generated -notcontains $_ })
Write-Output ("Reverse P/Invoke wrappers generated for ManagedBass: " + (@($expected | Where-Object { $generated -contains $_ }) -join ', '))
if ($missing.Count -ne 0) { throw ("IL2CPP generated no reverse P/Invoke wrapper for: " + ($missing -join ', ')) }
Write-Output "MANAGEDBASS_IL2CPP_CODEGEN_PASSED ($($expected.Count) callbacks)"

if (-not $Serial) {
    Write-Output "Pass -Serial to run $apk on a device."
    exit 0
}
$adb = Join-Path (Split-Path -Parent $UnityEditor) 'Data/PlaybackEngines/AndroidPlayer/SDK/platform-tools/adb.exe'
$package = 'net.majdata.managedbass.il2cpp'
& $adb -s $Serial install -r $apk
if ($LASTEXITCODE -ne 0) { throw "Installing $apk failed." }
& $adb -s $Serial logcat -c
& $adb -s $Serial shell am start -n "$package/com.unity3d.player.UnityPlayerActivity" | Out-Null
$deadline = [DateTime]::UtcNow.AddSeconds($TimeoutSeconds)
$lines = @()
do {
    Start-Sleep -Seconds 2
    $lines = @(& $adb -s $Serial logcat -d -s Unity:I | Where-Object { $_ -match 'MANAGEDBASS_IL2CPP' })
} until (($lines | Where-Object { $_ -match 'MANAGEDBASS_IL2CPP (PASSED|FAILED)' }) -or [DateTime]::UtcNow -gt $deadline)
$lines | ForEach-Object { Write-Output $_ }
if (-not ($lines | Where-Object { $_ -match 'MANAGEDBASS_IL2CPP PASSED' })) { throw 'The Player did not report MANAGEDBASS_IL2CPP PASSED.' }
