param(
    [string] $UnityEditor = 'C:\Program Files\Unity Editors\6000.3.17f1\Editor\Unity.exe',
    [ValidateSet('Mono', 'IL2CPP')][string] $Backend = 'Mono',
    [ValidateSet('x64', 'x86', 'armv7', 'arm64')][string] $Architecture = 'x64',
    [ValidateSet('Windows', 'Android', 'Linux')][string] $Platform = 'Windows',
    [ValidateSet('d3d11', 'd3d12', 'glcore', 'vulkan')][string] $Graphics = 'd3d11',
    [switch] $Hardware,
    [switch] $RequireHardware,
    [switch] $RequireNativeDecoder,
    [switch] $TestRecovery,
    [switch] $HardwareCpuUpload,
    [switch] $TestDecoderPreference,
    [switch] $TestCameraCapture,
    [switch] $CaptureUrp,
    [switch] $CaptureHardware,
    [ValidateSet('MPEG4', 'H264', 'HEVC', 'AV1', 'VP9')][string] $CaptureFormat = 'MPEG4',
    [ValidateSet('CBR', 'VBR')][string] $CaptureRateControl = 'VBR',
    [switch] $SkipBuild,
    [switch] $BuildOnly,
    [string] $NativeDirectory = '',
    [string] $Media = '',
    [string] $WorkDirectory = ''
)
$ErrorActionPreference = 'Stop'
if ($CaptureUrp -or $CaptureHardware) { $TestCameraCapture = $true }
if ($TestCameraCapture -and $Platform -ne 'Windows') { throw 'Camera recording validation currently requires a Windows Player' }
if ($RequireNativeDecoder) {
    if ($Platform -ne 'Windows' -or $Graphics -notin @('d3d12', 'vulkan')) { throw 'Native decoder validation requires Windows D3D12 or Vulkan' }
    $RequireHardware = $true
}
if (($HardwareCpuUpload -or $TestDecoderPreference) -and ($RequireHardware -or $TestRecovery)) { throw 'CPU transport / preference tests must permit CPU upload; do not combine with RequireHardware or TestRecovery' }
if ($HardwareCpuUpload -or $TestDecoderPreference) { $Hardware = $true }
if (($HardwareCpuUpload -or $TestDecoderPreference) -and $Platform -ne 'Windows') { throw 'Decoder preference / CPU transport validation currently requires a Windows Player' }
if ($TestRecovery) { $RequireHardware = $true }
if ($RequireHardware) { $Hardware = $true }
if ($TestRecovery -and $Platform -ne 'Windows') { throw 'Recovery fault injection currently requires a Windows Player' }
if (($Platform -eq 'Windows' -and $Architecture -notin @('x64','x86')) -or ($Platform -eq 'Android' -and $Architecture -notin @('armv7','arm64')) -or ($Platform -eq 'Linux' -and $Architecture -ne 'x64')) { throw 'Invalid platform / architecture combination' }
if ($Platform -eq 'Android' -and $Architecture -eq 'arm64' -and $Backend -eq 'Mono') { throw 'Android ARM64 requires IL2CPP' }
if ($Platform -eq 'Linux' -and $Backend -ne 'Mono') { throw 'This Windows-hosted Linux Player validation supports Mono; build Linux IL2CPP on a supported native toolchain separately' }
if ($Platform -eq 'Linux' -and $Graphics -notin @('glcore','vulkan')) { throw 'Linux Player validation requires -Graphics glcore or vulkan' }
$repo = (Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '../../..')).Path
$work = if (-not $WorkDirectory) { Join-Path $PSScriptRoot '.work' }
    elseif ([IO.Path]::IsPathRooted($WorkDirectory)) { [IO.Path]::GetFullPath($WorkDirectory) }
    else { [IO.Path]::GetFullPath((Join-Path $repo $WorkDirectory)) }
$platformPrefix = if ($Platform -eq 'Windows') { '' } else { "$Platform-" }
# Android's Gradle Prefab paths can exceed Ninja's Windows path limit, especially
# for armeabi-v7a. Keep the isolated checkout name short.
$projectName = if ($Platform -eq 'Android') { "P-$Architecture-v4" } else { "Project-$platformPrefix$Architecture-native-video" }
$project = Join-Path $work $projectName
$result = Join-Path $work "$platformPrefix$Architecture-$Backend"
$utf8 = New-Object Text.UTF8Encoding($false)
if (-not $Media) { $Media = Join-Path $repo 'Assets/StreamingAssets/MaiCharts/Original/Zunda Overdance/bg.mp4' }
if (-not (Test-Path -LiteralPath $Media)) { throw "Missing media: $Media" }
function Write-Json([string] $Path, $Value) { [IO.File]::WriteAllText($Path, ($Value | ConvertTo-Json -Depth 8), $utf8) }
foreach ($folder in @('Assets/Editor', 'Assets/Smoke', 'Assets/Plugins/FFmpeg', 'Assets/StreamingAssets', 'Packages', 'ProjectSettings')) {
    New-Item -ItemType Directory -Path (Join-Path $project $folder) -Force | Out-Null
}
New-Item -ItemType Directory -Path $result -Force | Out-Null
if (-not $SkipBuild) {
    # An asmdef at Assets/ would scope the PolySharp analyzer to only the smoke
    # assembly. Keep the harness below Smoke/ so the analyzer applies globally.
    foreach ($obsolete in @('Assets/FFmpeg.Player.Smoke.asmdef', 'Assets/FFmpeg.Player.Smoke.asmdef.meta', 'Assets/UnitySmoke.cs', 'Assets/UnitySmoke.cs.meta')) {
        $obsoleteFile = Join-Path $project $obsolete
        if (Test-Path -LiteralPath $obsoleteFile) { Remove-Item -LiteralPath $obsoleteFile }
    }
    # Use the product diagnostics and formatting implementation, including its real
    # GUID references and C# required/init polyfills. Do not replace logging with stubs.
    New-Item -ItemType Directory -Path (Join-Path $project 'Assets/Plugins/MajdataPlay') -Force | Out-Null
    New-Item -ItemType Directory -Path (Join-Path $project 'Assets/Packages') -Force | Out-Null
    Copy-Item -LiteralPath (Join-Path $repo 'Assets/Plugins/MajdataPlay/Diagnostics') -Destination (Join-Path $project 'Assets/Plugins/MajdataPlay') -Recurse -Force
    Copy-Item -LiteralPath (Join-Path $repo 'Assets/Plugins/ZString') -Destination (Join-Path $project 'Assets/Plugins') -Recurse -Force
    Copy-Item -LiteralPath (Join-Path $repo 'Assets/Packages/PolySharp.1.15.0') -Destination (Join-Path $project 'Assets/Packages') -Recurse -Force
    Copy-Item -LiteralPath (Join-Path $repo 'Assets/Packages/System.Runtime.CompilerServices.Unsafe.6.1.2') -Destination (Join-Path $project 'Assets/Packages') -Recurse -Force
    Copy-Item -LiteralPath (Join-Path $repo 'Assets/csc.rsp') -Destination (Join-Path $project 'Assets/csc.rsp') -Force
    Copy-Item -LiteralPath (Join-Path $repo 'Assets/Plugins/MajdataPlay/FFmpeg/Runtime') -Destination (Join-Path $project 'Assets/Plugins/FFmpeg') -Recurse -Force
    Copy-Item -LiteralPath (Join-Path $repo 'Assets/Plugins/MajdataPlay/FFmpeg/Editor') -Destination (Join-Path $project 'Assets/Plugins/FFmpeg') -Recurse -Force
    if (Test-Path -LiteralPath (Join-Path $repo 'Assets/Plugins/MajdataPlay/FFmpeg/Shaders')) {
        Copy-Item -LiteralPath (Join-Path $repo 'Assets/Plugins/MajdataPlay/FFmpeg/Shaders') -Destination (Join-Path $project 'Assets/Plugins/FFmpeg') -Recurse -Force
    }
    $nativeSource = Join-Path $repo $(if ($Platform -eq 'Linux') { 'Assets/Plugins/MajdataPlay/FFmpeg/Native/Linux' } else { 'Assets/Plugins/MajdataPlay/FFmpeg/Native/Windows' })
    if (-not (Test-Path -LiteralPath $nativeSource)) { throw "Build native libraries first: $nativeSource" }
    New-Item -ItemType Directory -Path (Join-Path $project 'Assets/Plugins/FFmpeg/Native') -Force | Out-Null
    Copy-Item -LiteralPath $nativeSource -Destination (Join-Path $project 'Assets/Plugins/FFmpeg/Native') -Recurse -Force
    if ($NativeDirectory) {
        if ($Platform -ne 'Windows') { throw 'NativeDirectory override currently requires Windows' }
        $override = (Resolve-Path -LiteralPath $NativeDirectory).Path
        $pluginArchitecture = if ($Architecture -eq 'x64') { 'x86_64' } else { 'x86' }
        $destination = Join-Path $project "Assets/Plugins/FFmpeg/Native/Windows/$pluginArchitecture"
        # Retain the real PluginImporter metadata while testing newly built DLLs
        # independently of libraries currently loaded in the user's Editor.
        foreach ($library in @('avcodec-63.dll', 'avdevice-63.dll', 'avfilter-12.dll', 'avformat-63.dll', 'avutil-61.dll', 'swresample-7.dll', 'swscale-10.dll')) {
            Copy-Item -LiteralPath (Join-Path $override $library) -Destination (Join-Path $destination $library) -Force
        }
        $overrideBridge = Join-Path $override 'FFmpegUnityBridge.dll'
        if (Test-Path -LiteralPath $overrideBridge) {
            Copy-Item -LiteralPath $overrideBridge -Destination (Join-Path $destination 'FFmpegUnityBridge.dll') -Force
        }
    }
    if ($Platform -eq 'Android') {
        Copy-Item -LiteralPath (Join-Path $repo 'Assets/Plugins/MajdataPlay/FFmpeg/Native/Android') -Destination (Join-Path $project 'Assets/Plugins/FFmpeg/Native') -Recurse -Force
    }
    Copy-Item -LiteralPath $Media -Destination (Join-Path $project 'Assets/StreamingAssets/test.mp4') -Force
    Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'UnitySmoke.cs') -Destination (Join-Path $project 'Assets/Smoke/UnitySmoke.cs') -Force
    Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'UnityCameraSmoke.cs') -Destination (Join-Path $project 'Assets/Smoke/UnityCameraSmoke.cs') -Force
    Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'UnitySmokeBuild.cs') -Destination (Join-Path $project 'Assets/Editor/UnitySmokeBuild.cs') -Force
    Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'UnityCameraEditorSmoke.cs') -Destination (Join-Path $project 'Assets/Editor/UnityCameraEditorSmoke.cs') -Force
    $package = (Join-Path $repo 'ThirdParty/FFmpeg.AutoGen/Unity') -replace '\\', '/'
    Write-Json (Join-Path $project 'Packages/manifest.json') @{ dependencies = @{ 'net.majdata.ffmpeg-autogen' = "file:$package"; 'com.unity.render-pipelines.universal' = '17.3.0'; 'com.unity.ugui' = '2.0.0'; 'com.unity.modules.ui' = '1.0.0'; 'com.unity.modules.imageconversion' = '1.0.0'; 'com.unity.modules.androidjni' = '1.0.0'; 'com.unity.modules.unitywebrequest' = '1.0.0' } }
    Write-Json (Join-Path $project 'Assets/Smoke/FFmpeg.Player.Smoke.asmdef') @{ name = 'FFmpeg.Player.Smoke'; references = @('MajdataPlay.FFmpeg', 'MajdataPlay.Diagnostics', 'Unity.RenderPipelines.Core.Runtime', 'Unity.RenderPipelines.Universal.Runtime') }
    Write-Json (Join-Path $project 'Assets/Editor/FFmpeg.Player.Smoke.Editor.asmdef') @{ name = 'FFmpeg.Player.Smoke.Editor'; references = @('FFmpeg.Player.Smoke', 'MajdataPlay.FFmpeg', 'Unity.RenderPipelines.Core.Runtime', 'Unity.RenderPipelines.Universal.Runtime'); includePlatforms = @('Editor') }
    [IO.File]::WriteAllText((Join-Path $project 'ProjectSettings/ProjectVersion.txt'), "m_EditorVersion: 6000.3.17f1`n", $utf8)
    $target = if ($Platform -eq 'Android') { 'Android' } elseif ($Platform -eq 'Linux') { 'Linux64' } elseif ($Architecture -eq 'x86') { 'Win' } else { 'Win64' }
    $buildLog = Join-Path $result 'editor.log'
    $buildArgs = @('-batchmode', '-nographics', '-quit', '-projectPath', ('"' + $project + '"'), '-buildTarget', $target,
        '-logFile', ('"' + $buildLog + '"'), '-executeMethod', 'FFmpegPlayerSmokeBuild.Run', '-videoBackend', $Backend,
        '-videoArchitecture', $Architecture, '-videoPlatform', $Platform, '-videoOutput', ('"' + $result + '"'))
    $buildArgs += @('-cameraCapture', $(if ($TestCameraCapture) { 'true' } else { 'false' }), '-captureUrp', $(if ($CaptureUrp) { 'true' } else { 'false' }))
    $process = Start-Process -FilePath $UnityEditor -ArgumentList $buildArgs -WindowStyle Hidden -PassThru
    $process.WaitForExit()
    if ($process.ExitCode -ne 0) { Get-Content -LiteralPath $buildLog -Tail 70; throw "Unity build failed: $buildLog" }
}
if ($BuildOnly) {
    Get-Content -LiteralPath (Join-Path $result 'build.txt')
    Write-Output "Isolated Player built at: $result; runtime validation was not requested."
    return
}
if ($Platform -eq 'Android') {
    Add-Type -AssemblyName System.IO.Compression.FileSystem
    $apk = [IO.Compression.ZipFile]::OpenRead((Join-Path $result 'VideoSmoke.apk'))
    $abi = if ($Architecture -eq 'arm64') { 'arm64-v8a' } else { 'armeabi-v7a' }
    $elfClass = if ($Architecture -eq 'arm64') { 2 } else { 1 }
    $machine = if ($Architecture -eq 'arm64') { 183 } else { 40 }
    try {
        foreach ($library in @('avcodec','avdevice','avfilter','avformat','avutil','swresample','swscale','FFmpegUnityBridge')) {
            $name = "lib/$abi/lib$library.so"
            $entry = $apk.GetEntry($name)
            if (-not $entry) { throw "APK omitted required FFmpeg plug-in: $name" }
            $reader = New-Object IO.BinaryReader($entry.Open())
            try { $header = $reader.ReadBytes(20) } finally { $reader.Dispose() }
            if ($header.Length -ne 20 -or $header[0] -ne 127 -or $header[4] -ne $elfClass -or [BitConverter]::ToUInt16($header, 18) -ne $machine) {
                throw "Wrong ELF architecture in APK: $name"
            }
        }
    } finally { $apk.Dispose() }
    [IO.File]::WriteAllText((Join-Path $result 'packaging.txt'), "PASS: seven FFmpeg libraries and the GPU bridge packaged as $abi ELF class $elfClass machine $machine")
    Get-Content -LiteralPath (Join-Path $result 'build.txt')
    Get-Content -LiteralPath (Join-Path $result 'packaging.txt')
    Write-Output 'APK built; device playback has not been tested.'
    return
}
if ($Platform -eq 'Linux') {
    Get-Content -LiteralPath (Join-Path $result 'build.txt')
    Write-Output "Linux Player built at: $result"
    Write-Output 'Run playback validation on Linux/WSLg with Tools/Tests/FFmpegValidation/run-linux-player.sh.'
    return
}
$suffix = if ($TestCameraCapture) { '-camera' + $(if ($CaptureUrp) { '-urp' } else { '-builtin' }) + $(if ($CaptureHardware) { '-hardware' } else { '-software' }) } elseif ($RequireNativeDecoder) { '-native-decoder' } elseif ($TestRecovery) { '-recovery' } elseif ($TestDecoderPreference) { '-decoder-preference' } elseif ($HardwareCpuUpload) { '-hardware-cpu' } elseif ($Hardware) { '-hardware' } else { '-software' }
if ($TestCameraCapture -and -not $CaptureHardware) { $suffix += "-$CaptureFormat-$CaptureRateControl" }
$report = Join-Path $result "$Graphics$suffix.txt"
$log = Join-Path $result "$Graphics$suffix.log"
if (Test-Path -LiteralPath $report) { Remove-Item -LiteralPath $report }
$hardwareValue = if ($Hardware) { 'true' } else { 'false' }
$requireHardwareValue = if ($RequireHardware) { 'true' } else { 'false' }
$testRecoveryValue = if ($TestRecovery) { 'true' } else { 'false' }
$hardwareCpuValue = if ($HardwareCpuUpload) { 'true' } else { 'false' }
$decoderPreferenceValue = if ($TestDecoderPreference) { 'true' } else { 'false' }
$nativeDecoderValue = if ($RequireNativeDecoder) { $Graphics } else { 'none' }
$playerArgs = @('-batchmode', "-force-$Graphics", '-logFile', ('"' + $log + '"'), '-videoHardware', $hardwareValue, '-videoRequireHardware', $requireHardwareValue, '-videoTestRecovery', $testRecoveryValue, '-videoHardwareCpuUpload', $hardwareCpuValue, '-videoTestDecoderPreference', $decoderPreferenceValue, '-videoReport', ('"' + $report + '"'))
$playerArgs += @('-videoNativeDecoder', $nativeDecoderValue)
if ($TestCameraCapture) {
    # A camera rendering to the display needs the regular Player render loop.
    $playerArgs = @($playerArgs | Where-Object { $_ -ne '-batchmode' })
}
$playerArgs += @('-cameraCapture', $(if ($TestCameraCapture) { 'true' } else { 'false' }), '-captureHardware', $(if ($CaptureHardware) { 'true' } else { 'false' }))
$playerArgs += @('-captureFormat', $CaptureFormat, '-captureRateControl', $CaptureRateControl)
$process = Start-Process -FilePath (Join-Path $result 'VideoSmoke.exe') -ArgumentList $playerArgs -WindowStyle Hidden -PassThru
if (-not $process.WaitForExit(120000)) { $process.Kill(); throw "Player timed out: $log" }
if (-not (Test-Path -LiteralPath $report)) { Get-Content -LiteralPath $log -Tail 60; throw "Player produced no result: $log" }
$text = Get-Content -LiteralPath $report -Raw
Write-Output $text
if ($process.ExitCode -ne 0 -or -not $text.StartsWith('PASS:')) { throw "Player failed: $log" }
