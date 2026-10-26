param(
    [string]$UnityPath,
    [string]$SkinPath,
    [double]$BudgetMiB = 1024,
    [ValidateSet('d3d11', 'glcore', 'vulkan')]
    [string]$GraphicsApi = 'd3d11',
    [switch]$Baseline,
    [switch]$Player
)

$ErrorActionPreference = 'Stop'
$root = (Resolve-Path (Join-Path $PSScriptRoot '../../../..')).Path
$project = Join-Path $root 'Temp/CustomSkinMemoryValidation'
$assets = Join-Path $project 'Assets'
$expectedVersion = (Select-String -LiteralPath (Join-Path $root 'ProjectSettings/ProjectVersion.txt') -Pattern '^m_EditorVersion: (.+)$').Matches[0].Groups[1].Value
if (!$UnityPath) {
    $UnityPath = @(
        "C:/Program Files/Unity/Hub/Editor/$expectedVersion/Editor/Unity.exe",
        "C:/Program Files/Unity Editors/$expectedVersion/Editor/Unity.exe"
    ) | Where-Object { Test-Path -LiteralPath $_ -PathType Leaf } | Select-Object -First 1
}
if (!$UnityPath -or !(Test-Path -LiteralPath $UnityPath -PathType Leaf)) {
    throw "Unity Editor $expectedVersion not found; pass -UnityPath."
}
$actualVersion = (Get-Item -LiteralPath $UnityPath).VersionInfo.ProductVersion.Split('_')[0]
if ($actualVersion -ne $expectedVersion) {
    throw "This repository requires Unity $expectedVersion; the selected Editor is $actualVersion."
}
if (!$SkinPath) {
    $SkinPath = Join-Path $root 'Assets/StreamingAssets/Skins/default'
}
$SkinPath = (Resolve-Path -LiteralPath $SkinPath).Path
$utf8WithoutBom = New-Object Text.UTF8Encoding($false)

# Restage from scratch so files removed from production are not left behind.
if (Test-Path -LiteralPath $assets) {
    Remove-Item -LiteralPath $assets -Recurse -Force
}
foreach ($directory in @($assets, (Join-Path $project 'Packages'), (Join-Path $project 'ProjectSettings'))) {
    New-Item -ItemType Directory -Force -Path $directory | Out-Null
}
Copy-Item -LiteralPath (Join-Path $root 'ProjectSettings/ProjectVersion.txt') -Destination (Join-Path $project 'ProjectSettings/ProjectVersion.txt')

# Keep registry packages local so this isolated check needs no network access.
$dependencies = @{}
foreach ($name in @('com.unity.burst', 'com.unity.collections', 'com.unity.mathematics',
    'com.unity.nuget.mono-cecil', 'com.unity.test-framework', 'com.unity.test-framework.performance', 'com.unity.ext.nunit')) {
    $package = Get-ChildItem -LiteralPath (Join-Path $root 'Library/PackageCache') -Directory |
        Where-Object { $_.Name.StartsWith($name + '@', [StringComparison]::Ordinal) } |
        Select-Object -First 1
    if (!$package) {
        throw "Open the main project with its specified Unity version to populate the $name package cache."
    }
    $dependencies[$name] = 'file:' + $package.FullName.Replace('\', '/')
}
foreach ($name in @('com.unity.modules.imageconversion', 'com.unity.modules.vectorgraphics',
    'com.unity.modules.jsonserialize', 'com.unity.modules.imgui')) {
    $dependencies[$name] = '1.0.0'
}
$manifest = @{ dependencies = $dependencies } | ConvertTo-Json -Depth 4
[IO.File]::WriteAllText((Join-Path $project 'Packages/manifest.json'), $manifest, $utf8WithoutBom)

$uniTask = Join-Path $root 'Assets/Plugins/UniTask/src/Runtime'
if (!(Test-Path -LiteralPath (Join-Path $uniTask 'UniTask.asmdef'))) {
    throw 'Initialize the UniTask submodule with git submodule update --init --recursive.'
}
$uniTaskTarget = Join-Path $assets 'UniTask'
New-Item -ItemType Directory -Force -Path $uniTaskTarget | Out-Null
Get-ChildItem -LiteralPath $uniTask | Where-Object { $_.Name -notin @('External', 'External.meta') } |
    Copy-Item -Destination $uniTaskTarget -Recurse -Force

$storageTarget = Join-Path $assets 'Storage'
New-Item -ItemType Directory -Force -Path $storageTarget | Out-Null
Get-ChildItem -LiteralPath (Join-Path $root 'Assets/Plugins/MajdataPlay/IO/Storage') -File -Filter '*.cs' |
    Copy-Item -Destination $storageTarget -Force

# Real SkiaSharp: the binding's own sources (compiled against this project's API profile)
# plus its Windows x64 native library.
$skiaSource = Join-Path $root 'Assets/Plugins/SkiaForUnity/SkiaSharp-Bindings'
$skiaTarget = Join-Path $assets 'SkiaSharp'
New-Item -ItemType Directory -Force -Path $skiaTarget | Out-Null
Get-ChildItem -LiteralPath $skiaSource -File | Where-Object { $_.Extension -in @('.cs', '.asmdef') } |
    Copy-Item -Destination $skiaTarget -Force
$pluginTarget = Join-Path $assets 'Plugins/x86_64'
New-Item -ItemType Directory -Force -Path $pluginTarget | Out-Null
Copy-Item -LiteralPath (Join-Path $root 'Assets/Plugins/SkiaForUnity/Library/win-x64/libSkiaSharp.dll') -Destination $pluginTarget

# Stage the production sources under test. Only files whose dependencies the harness stubs are copied.
$sources = @(
    'Assets/Scripts/Scenes/Game/Misc/Notes/Skins/CustomSkin.cs',
    'Assets/Scripts/Scenes/Game/Misc/Notes/Skins/AtlasLayout.cs',
    'Assets/Plugins/MajdataPlay/Drawing/DecodedImage.cs',
    'Assets/Plugins/MajdataPlay/Drawing/EncodedImage.cs',
    'Assets/Plugins/MajdataPlay/Drawing/TextureLoader.cs',
    'Assets/Plugins/MajdataPlay/Drawing/Extensions/SKBitmapExtensions.cs'
)
$productionTarget = Join-Path $assets 'Production'
New-Item -ItemType Directory -Force -Path $productionTarget | Out-Null
foreach ($source in $sources) {
    $target = Join-Path $productionTarget ([IO.Path]::GetFileName($source))
    if ($Baseline) {
        # Files added after the committed version do not exist in the baseline.
        if (!(& git -C $root ls-tree --name-only HEAD -- $source)) {
            continue
        }
        # Byte-exact copy of the committed file so legacy-encoded sources are preserved.
        $process = Start-Process -FilePath 'git' -ArgumentList @('-C', "`"$root`"", 'show', "HEAD:$source") `
            -RedirectStandardOutput $target -NoNewWindow -Wait -PassThru
        if ($process.ExitCode -ne 0) {
            throw "Could not read the committed $source."
        }
    } else {
        Copy-Item -LiteralPath (Join-Path $root $source) -Destination $target
    }
}
$editorTarget = Join-Path $assets 'Editor'
New-Item -ItemType Directory -Force -Path $editorTarget | Out-Null
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'Validation.cs') -Destination $editorTarget
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'Probe.cs') -Destination $assets
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'Services.cs') -Destination $assets
[IO.File]::WriteAllText((Join-Path $assets 'csc.rsp'), "-unsafe`r`n", $utf8WithoutBom)

# Compile once before measuring so import work does not overlap either measured run.
$importLog = Join-Path $project 'import.log'
$process = Start-Process -FilePath $UnityPath -WindowStyle Hidden -ArgumentList @(
    '-batchmode', '-quit', "-force-$GraphicsApi", '-projectPath', "`"$project`"", '-logFile', "`"$importLog`""
) -PassThru
$process.WaitForExit()
if ($process.ExitCode -ne 0) {
    Get-Content -LiteralPath $importLog -Tail 100
    throw "The isolated project failed to import. See $importLog"
}

$suffix = if ($Baseline) { '-baseline' } else { '' }
$probeArguments = @('-skinPath', "`"$SkinPath`"", '-budgetMiB', $BudgetMiB.ToString([Globalization.CultureInfo]::InvariantCulture))
if ($Player) {
    $playerPath = Join-Path $project "Player$suffix/Probe.exe"
    $buildLog = Join-Path $project "build$suffix.log"
    $process = Start-Process -FilePath $UnityPath -WindowStyle Hidden -ArgumentList @(
        '-batchmode', '-projectPath', "`"$project`"", '-executeMethod', 'CustomSkinMemoryValidation.Validation.BuildPlayer',
        '-playerPath', "`"$playerPath`"", '-logFile', "`"$buildLog`""
    ) -PassThru
    $process.WaitForExit()
    if ($process.ExitCode -ne 0 -or !(Test-Path -LiteralPath $playerPath)) {
        Get-Content -LiteralPath $buildLog -Tail 100
        throw "The probe player failed to build. See $buildLog"
    }
}

# Each load mode runs in a fresh process so the first load's peak is not hidden by memory reused from another.
$failed = $false
foreach ($mode in @('async', 'sync')) {
    if ($Player) {
        $log = Join-Path $project "player-$mode-$GraphicsApi$suffix.log"
        $process = Start-Process -FilePath $playerPath -ArgumentList (@(
            "-force-$GraphicsApi", '-screen-fullscreen', '0', '-screen-width', '640', '-screen-height', '360',
            '-logFile', "`"$log`"", '-loadMode', $mode
        ) + $probeArguments) -PassThru
    } else {
        $log = Join-Path $project "validation-$mode-$GraphicsApi$suffix.log"
        $process = Start-Process -FilePath $UnityPath -WindowStyle Hidden -ArgumentList (@(
            '-batchmode', "-force-$GraphicsApi", '-projectPath', "`"$project`"",
            '-executeMethod', 'CustomSkinMemoryValidation.Validation.Run', '-logFile', "`"$log`"", '-loadMode', $mode
        ) + $probeArguments) -PassThru
    }
    $process.WaitForExit()
    if (Test-Path -LiteralPath $log) {
        Select-String -LiteralPath $log -Pattern 'CUSTOM_SKIN_MEMORY_' | ForEach-Object { $_.Line }
    }
    if ($process.ExitCode -ne 0 -or !(Test-Path -LiteralPath $log) -or
        !(Select-String -LiteralPath $log -Pattern 'CUSTOM_SKIN_MEMORY_VALIDATION_PASSED' -Quiet)) {
        if (Test-Path -LiteralPath $log) {
            Get-Content -LiteralPath $log -Tail 60
        }
        Write-Warning "CustomSkin memory validation failed for $mode. See $log"
        $failed = $true
    }
}
if ($failed) {
    throw 'CustomSkin memory validation failed.'
}
Write-Output 'CustomSkin memory validation passed.'
