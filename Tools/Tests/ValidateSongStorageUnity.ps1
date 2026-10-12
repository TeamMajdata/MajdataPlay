[CmdletBinding()]
param(
    [string]$UnityPath = 'C:/Program Files/Unity Editors/6000.3.17f1/Editor/Unity.exe',
    [ValidateRange(1, 100)]
    [int]$Repetitions = 12,
    [ValidateRange(30, 1800)]
    [int]$TimeoutSeconds = 300,
    [switch]$CompileOnly
)

$ErrorActionPreference = 'Stop'
$root = (Resolve-Path (Join-Path $PSScriptRoot '../..')).Path
$expected = (Select-String -LiteralPath (Join-Path $root 'ProjectSettings/ProjectVersion.txt') -Pattern '^m_EditorVersion: (.+)$').Matches[0].Groups[1].Value
if ($expected -ne '6000.3.17f1') {
    throw "This harness targets Unity 6000.3.17f1; the repository specifies $expected."
}
if (!(Test-Path -LiteralPath $UnityPath -PathType Leaf)) {
    throw "Unity Editor not found: $UnityPath"
}
$version = [Diagnostics.FileVersionInfo]::GetVersionInfo($UnityPath).ProductVersion
if ($version -notmatch ('^' + [regex]::Escape($expected) + '(?:_|$)')) {
    throw "Use Unity $expected; the selected executable reports $version."
}

$project = [IO.Path]::GetFullPath((Join-Path $root 'Temp/SongStorageUnityValidation'))
$boundary = $project.TrimEnd([IO.Path]::DirectorySeparatorChar) + [IO.Path]::DirectorySeparatorChar
$assets = Join-Path $project 'Assets/Validation'
# Never clean the main project or follow junctions out of the disposable project.
foreach ($directory in @($root, (Join-Path $root 'Temp'), $project, (Join-Path $project 'Assets'), $assets)) {
    if (Test-Path -LiteralPath $directory) {
        $item = Get-Item -LiteralPath $directory -Force
        if (!$item.PSIsContainer -or ($item.Attributes -band [IO.FileAttributes]::ReparsePoint)) {
            throw "Refusing staging through a non-directory or reparse point: $directory"
        }
    }
}
$lockFile = Join-Path $project 'Temp/UnityLockfile'
if (Test-Path -LiteralPath $lockFile) {
    try {
        $lockStream = [IO.File]::Open($lockFile, 'Open', 'ReadWrite', 'None')
        $lockStream.Dispose()
    }
    catch {
        throw "The isolated project is already open. Do not run two validations concurrently: $project"
    }
}
if (Test-Path -LiteralPath $assets) {
    $resolvedAssets = [IO.Path]::GetFullPath((Resolve-Path -LiteralPath $assets).Path)
    if (!$resolvedAssets.StartsWith($boundary, [StringComparison]::OrdinalIgnoreCase) -or
        $resolvedAssets -ne [IO.Path]::GetFullPath((Join-Path $project 'Assets/Validation'))) {
        throw "Refusing to clear a staging directory outside the isolated project: $resolvedAssets"
    }
    $reparsePoints = @(Get-ChildItem -LiteralPath $assets -Recurse -Force | Where-Object { $_.Attributes -band [IO.FileAttributes]::ReparsePoint })
    if ($reparsePoints.Count -ne 0) {
        throw 'Refusing to clear staging containing reparse points.'
    }
    Remove-Item -LiteralPath $resolvedAssets -Recurse -Force
}
$production = Join-Path $assets 'Production'
$dependencies = Join-Path $assets 'Dependencies'
$editor = Join-Path $assets 'Editor'
foreach ($directory in @($production, $dependencies, $editor, (Join-Path $project 'Packages'), (Join-Path $project 'ProjectSettings'))) {
    New-Item -ItemType Directory -Force -Path $directory | Out-Null
}
Copy-Item -LiteralPath (Join-Path $root 'ProjectSettings/ProjectVersion.txt') -Destination (Join-Path $project 'ProjectSettings/ProjectVersion.txt') -Force
# These built-in modules satisfy UniTask's Unity references; no registry packages are upgraded.
$modules = @('assetbundle', 'unitywebrequest', 'physics', 'physics2d', 'particlesystem', 'animation', 'imgui', 'ui', 'uielements', 'tilemap')
$manifest = @{ dependencies = @{} }
foreach ($module in $modules) {
    $manifest.dependencies["com.unity.modules.$module"] = '1.0.0'
}
# Windows PowerShell 5's UTF8 encoding writes a BOM, which Unity rejects in JSON files.
$utf8 = [Text.UTF8Encoding]::new($false)
[IO.File]::WriteAllText((Join-Path $project 'Packages/manifest.json'), ($manifest | ConvertTo-Json -Depth 4), $utf8)
$assembly = @{
    name = 'MajdataPlay.SongStorage.Validation.Editor'
    includePlatforms = @('Editor')
    allowUnsafeCode = $true
}
[IO.File]::WriteAllText((Join-Path $assets 'MajdataPlay.SongStorage.Validation.Editor.asmdef'), ($assembly | ConvertTo-Json), $utf8)
[IO.File]::WriteAllLines((Join-Path $project 'Assets/csc.rsp'), [string[]]@('-unsafe', '-nullable:enable', '-langVersion:preview'), $utf8)

$sourcePaths = @(
    'Assets/Scripts/Misc/Base/SongStorage.cs',
    'Assets/Scripts/Misc/Collections/SongCollection.cs',
    'Assets/Scripts/Misc/Collections/OnlineSongCollection.cs',
    'Assets/Scripts/Misc/Collections/MyFavoriteSongCollection.cs',
    'Assets/Scripts/Misc/Collections/SongCollectionScanner.cs',
    'Assets/Scripts/Misc/Base/DataFormats/SongDetail.cs',
    'Assets/Scripts/Misc/Base/DataFormats/OnlineSongDetail.cs',
    'Assets/Scripts/Misc/Base/DataFormats/MajnetSongDetail.cs',
    'Assets/Plugins/MajdataPlay/Net/HttpRange.cs',
    'Assets/Plugins/MajdataPlay/Net/HttpErrorCode.cs',
    'Assets/Plugins/MajdataPlay/Net/Exceptions/HttpException.cs',
    'Assets/Plugins/MajdataPlay/Net/UriExtensions.cs',
    'Assets/Scripts/Misc/Base/Interfaces/ISongDetail.cs',
    'Assets/Scripts/Misc/Base/Enums/ChartStorageType.cs',
    'Assets/Scripts/Misc/Base/Enums/ChartStorageLocation.cs',
    'Assets/Scripts/Misc/Settings/Runtime/Enums/SortType.cs'
)
$sourcePaths += @(Get-ChildItem -LiteralPath (Join-Path $root 'Assets/Plugins/MajdataPlay/IO/Storage') -Filter '*.cs' -File | ForEach-Object { $_.FullName.Substring($root.Length + 1).Replace('\', '/') })
$sourceHashes = @{}
foreach ($relativePath in $sourcePaths) {
    $source = Join-Path $root $relativePath
    $destination = Join-Path $production $relativePath
    New-Item -ItemType Directory -Force -Path (Split-Path -Parent $destination) | Out-Null
    Copy-Item -LiteralPath $source -Destination $destination -Force
    $sourceHashes[$relativePath] = (Get-FileHash -LiteralPath $destination -Algorithm SHA256).Hash
}
$harnessHashes = @{}
foreach ($harnessSource in Get-ChildItem -LiteralPath (Join-Path $PSScriptRoot 'SongStorageUnityValidation') -Filter '*.cs' -File) {
    $destination = Join-Path $editor $harnessSource.Name
    Copy-Item -LiteralPath $harnessSource.FullName -Destination $destination -Force
    $relativePath = $harnessSource.FullName.Substring($root.Length + 1).Replace('\', '/')
    $harnessHashes[$relativePath] = (Get-FileHash -LiteralPath $destination -Algorithm SHA256).Hash
}

# Never copy Assembly-CSharp.dll: the modified production sources must really compile.
$dependencyPaths = @(
    'Library/ScriptAssemblies/MajSimai.dll',
    'Library/ScriptAssemblies/UniTask.dll',
    'Library/ScriptAssemblies/ZString.dll',
    'Library/ScriptAssemblies/UnityEngine.UI.dll',
    'Library/ScriptAssemblies/Unity.TextMeshPro.dll',
    'Library/ScriptAssemblies/Unity.InternalAPIEngineBridge.004.dll',
    'Assets/Packages/System.Collections.Immutable.1.7.1/lib/net461/System.Collections.Immutable.dll',
    'Assets/Packages/System.Runtime.CompilerServices.Unsafe.6.1.2/lib/net462/System.Runtime.CompilerServices.Unsafe.dll'
)
foreach ($package in Get-ChildItem -LiteralPath (Join-Path $root 'Assets/Packages') -Directory -Filter 'Nito.*') {
    $framework = Join-Path $package.FullName 'lib/net461'
    if (Test-Path -LiteralPath $framework -PathType Container) {
        $dependencyPaths += @(Get-ChildItem -LiteralPath $framework -Filter '*.dll' -File | ForEach-Object FullName)
    }
}
$newtonsoft = @(Get-ChildItem -LiteralPath (Join-Path $root 'Library/PackageCache') -Directory -Filter 'com.unity.nuget.newtonsoft-json@*' | ForEach-Object { Join-Path $_.FullName 'Runtime/Newtonsoft.Json.dll' } | Where-Object { Test-Path -LiteralPath $_ -PathType Leaf })
if ($newtonsoft.Count -ne 1) {
    throw 'Expected exactly one installed Unity Newtonsoft.Json runtime DLL. Import the main project with the pinned Editor first.'
}
$dependencyPaths += $newtonsoft[0]
$dependencyHashes = @{}
foreach ($dependencyPath in $dependencyPaths) {
    $source = if ([IO.Path]::IsPathRooted($dependencyPath)) { $dependencyPath } else { Join-Path $root $dependencyPath }
    if (!(Test-Path -LiteralPath $source -PathType Leaf)) {
        throw "Missing compiled dependency: $source. Initialize submodules and finish importing the main project first."
    }
    $name = [IO.Path]::GetFileName($source)
    $destination = Join-Path $dependencies $name
    Copy-Item -LiteralPath $source -Destination $destination -Force
    $dependencyHashes[$name] = @{ source = $source; sha256 = (Get-FileHash -LiteralPath $destination -Algorithm SHA256).Hash }
    # UI/TMP satisfy precompiled UniTask/ZString type references, not image/rendering tests.
    # All plugin references are validated; no broken-assembly shortcut is accepted.
    $guid = [Guid]::NewGuid().ToString('N')
    @"
fileFormatVersion: 2
guid: $guid
PluginImporter:
  externalObjects: {}
  serializedVersion: 2
  defineConstraints: []
  isPreloaded: 0
  isOverridable: 0
  isExplicitlyReferenced: 0
  validateReferences: 1
  platformData:
  - first:
      Any:
    second:
      enabled: 1
      settings: {}
  userData:
  assetBundleName:
  assetBundleVariant:
"@ | Set-Content -LiteralPath "$destination.meta" -Encoding UTF8
}
# Use the same existing compiler polyfills as the game, including init-only properties.
$polySharp = Join-Path $root 'Assets/Packages/PolySharp.1.15.0/analyzers/dotnet/cs/PolySharp.SourceGenerators.dll'
foreach ($source in @($polySharp, "$polySharp.meta")) {
    if (!(Test-Path -LiteralPath $source -PathType Leaf)) {
        throw "Missing compiler dependency: $source"
    }
    Copy-Item -LiteralPath $source -Destination $dependencies -Force
}
$provenance = @{ unity = $version; sources = $sourceHashes; harness = $harnessHashes; dependencies = $dependencyHashes; compileOnly = $CompileOnly.IsPresent; repetitions = $Repetitions }
$provenance | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath (Join-Path $project 'staged-inputs.json') -Encoding UTF8

$log = Join-Path $project 'validation.log'
$arguments = @('-batchmode', '-nographics', '-projectPath', ([char]34 + $project + [char]34), '-executeMethod',
    'MajdataPlay.Tests.SongStorageUnityValidation.Run', '-songStorageValidationRepetitions', $Repetitions,
    '-logFile', ([char]34 + $log + [char]34))
if ($CompileOnly) {
    $arguments += '-songStorageValidationCompileOnly'
}
# Do not pass -quit: asynchronous metadata/cleanup must finish while Editor.update runs.
$process = Start-Process -FilePath $UnityPath -WindowStyle Hidden -ArgumentList $arguments -PassThru
$null = $process.Handle
if (!$process.WaitForExit($TimeoutSeconds * 1000)) {
    throw "Isolated validation timed out (PID $($process.Id)); the process was not killed. Inspect $log. Do not start another run until it exits."
}
$process.Refresh()
$marker = if ($CompileOnly) { 'SONG_STORAGE_UNITY_COMPILE_PASSED' } else { 'SONG_STORAGE_UNITY_PASSED' }
if ($null -eq $process.ExitCode -or $process.ExitCode -ne 0 -or !(Test-Path -LiteralPath $log -PathType Leaf) -or
    !(Select-String -LiteralPath $log -Pattern $marker -Quiet)) {
    if (Test-Path -LiteralPath $log -PathType Leaf) {
        Get-Content -LiteralPath $log -Tail 100
    }
    throw "Song storage Unity validation failed (exit $($process.ExitCode)). See $log"
}
if (Select-String -LiteralPath $log -Pattern 'error CS\d+|Unloading broken assembly|scripting_class_is_subclass_of was called with a NULL parameter' -Quiet) {
    throw "Unity compilation or managed assembly loading failed. Inspect $log"
}
# Concurrent agents may still edit production or harness sources. Do not report an older staged snapshot as current.
foreach ($hashes in @($sourceHashes, $harnessHashes)) {
    foreach ($relativePath in $hashes.Keys) {
        if ((Get-FileHash -LiteralPath (Join-Path $root $relativePath) -Algorithm SHA256).Hash -ne $hashes[$relativePath]) {
            throw "Source changed during validation: $relativePath. Rerun to stage the latest production and harness sources. The tested hashes remain in staged-inputs.json."
        }
    }
}
Select-String -LiteralPath $log -Pattern 'SONG_STORAGE_' | ForEach-Object { $_.Line }
Write-Output "Isolated Unity $expected validation passed (exit 0). Log: $log"
