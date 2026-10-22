<#
.SYNOPSIS
Runs the MajdataPlay Unity Test Framework suite in an isolated copy of the project.

.DESCRIPTION
Mirrors Assets, Packages, ProjectSettings and ThirdParty into <WorkRoot>/Project, stages the test
sources of this directory into Assets/MajdataPlay.Tests, adds the Code Coverage package to the staged
manifest and runs Unity in batch mode for EditMode and/or PlayMode. The main project and its Library
are only read; nothing is written back to the repository.

Game/<Area> folders compile into Assembly-CSharp-Editor (staged under an Editor folder) so they can
reach the main project's Assembly-CSharp. Plugins/<Module> folders each carry their own test asmdef.
The staged copy grants the test assemblies InternalsVisibleTo access to Assembly-CSharp and every
MajdataPlay library assembly; the repository sources are not changed.

.PARAMETER UnityPath
Unity executable. Defaults to the version in ProjectSettings/ProjectVersion.txt under the usual Hub
and standalone install locations.

.PARAMETER WorkRoot
Staging root. Defaults to %LOCALAPPDATA%/MajdataPlay.Tests on Windows (short and outside synced
folders; Unity package paths exceed MAX_PATH under deep roots) and the temp directory elsewhere.

.PARAMETER TestPlatform
EditMode, PlayMode or All.

.PARAMETER Area
Test areas to stage, such as 'Game/Misc' or 'Plugins/Buffers'. Shared folders are always staged.
Defaults to every area.

.PARAMETER TestFilter
Optional Unity -testFilter expression.

.PARAMETER AssemblyNames
Optional semicolon separated Unity -assemblyNames list.

.PARAMETER Coverage
Collects code coverage for Assembly-CSharp, Assembly-CSharp-Editor and the MajdataPlay.* assemblies.

.PARAMETER SkipProjectSync
Reuses the staged project files and only restages the tests.

.PARAMETER NoLibrarySeed
Does not seed a missing staged Library from the main project's Library.

.PARAMETER NoGraphics
Passes -nographics to Unity. Tests that need a graphics device are then expected to fail.

.PARAMETER TimeoutMinutes
Maximum time for each Unity invocation.
#>
[CmdletBinding()]
param(
    [string]$UnityPath,
    [string]$WorkRoot,
    [ValidateSet('All', 'EditMode', 'PlayMode')]
    [string]$TestPlatform = 'All',
    [string[]]$Area = @(),
    [string]$TestFilter,
    [string]$AssemblyNames,
    [switch]$Coverage,
    [switch]$SkipProjectSync,
    [switch]$NoLibrarySeed,
    [switch]$NoGraphics,
    [int]$TimeoutMinutes = 90
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version 3

$CoveragePackage = 'com.unity.testtools.codecoverage'
$CoveragePackageVersion = '1.2.6'
$StagedTestsFolder = 'MajdataPlay.Tests'
$InternalsFileName = 'MajdataPlay.Tests.Internals.cs'
$TestSourceRoots = @('Game', 'Plugins')
$IsWindowsHost = [Environment]::OSVersion.Platform -eq [PlatformID]::Win32NT

$root = (Resolve-Path (Join-Path $PSScriptRoot '../../..')).Path
$version = (Select-String -LiteralPath (Join-Path $root 'ProjectSettings/ProjectVersion.txt') -Pattern '^m_EditorVersion: (.+)$').Matches[0].Groups[1].Value.Trim()

function Resolve-UnityPath {
    if ($UnityPath) {
        if (!(Test-Path -LiteralPath $UnityPath -PathType Leaf)) {
            throw "Unity executable not found: $UnityPath"
        }
        return (Resolve-Path -LiteralPath $UnityPath).Path
    }
    $candidates = @()
    if ($IsWindowsHost) {
        $programFiles = $env:ProgramFiles
        $candidates += Join-Path $programFiles "Unity/Hub/Editor/$version/Editor/Unity.exe"
        $candidates += Join-Path $programFiles "Unity Editors/$version/Editor/Unity.exe"
    }
    else {
        $candidates += "/Applications/Unity/Hub/Editor/$version/Unity.app/Contents/MacOS/Unity"
        $candidates += Join-Path $HOME "Unity/Hub/Editor/$version/Editor/Unity"
    }
    foreach ($candidate in $candidates) {
        if (Test-Path -LiteralPath $candidate -PathType Leaf) {
            return $candidate
        }
    }
    throw "Unity $version was not found. Pass -UnityPath. Tried: $($candidates -join ', ')"
}

function Resolve-WorkRoot {
    if ($WorkRoot) {
        return [IO.Path]::GetFullPath($WorkRoot)
    }
    if ($IsWindowsHost -and $env:LOCALAPPDATA) {
        return Join-Path $env:LOCALAPPDATA 'MajdataPlay.Tests'
    }
    return Join-Path ([IO.Path]::GetTempPath()) 'MajdataPlay.Tests'
}

function Sync-Directory {
    param(
        [Parameter(Mandatory)][string]$Source,
        [Parameter(Mandatory)][string]$Destination,
        [string[]]$ExcludeDirectories = @(),
        [string[]]$ExcludeFiles = @()
    )
    New-Item -ItemType Directory -Force -Path $Destination | Out-Null
    if ($IsWindowsHost) {
        $arguments = @($Source, $Destination, '/MIR', '/XJ', '/R:2', '/W:1', '/NFL', '/NDL', '/NJH', '/NJS', '/NP')
        if ($ExcludeDirectories.Count -gt 0) {
            $arguments += '/XD'
            $arguments += $ExcludeDirectories
        }
        if ($ExcludeFiles.Count -gt 0) {
            $arguments += '/XF'
            $arguments += $ExcludeFiles
        }
        & robocopy @arguments | Out-Null
        if ($LASTEXITCODE -ge 8) {
            throw "robocopy failed ($LASTEXITCODE): $Source -> $Destination"
        }
        $global:LASTEXITCODE = 0
        return
    }
    $arguments = @('-a', '--delete')
    foreach ($directory in $ExcludeDirectories) {
        $arguments += "--exclude=$directory/"
    }
    foreach ($file in $ExcludeFiles) {
        $arguments += "--exclude=$file"
    }
    $arguments += ($Source.TrimEnd('/') + '/')
    $arguments += $Destination
    & rsync @arguments
    if ($LASTEXITCODE -ne 0) {
        throw "rsync failed ($LASTEXITCODE): $Source -> $Destination"
    }
}

function Get-NestedRepositoryExclusions {
    param([Parameter(Mandatory)][string]$Directory)
    $git = Get-Command git -ErrorAction SilentlyContinue
    if (!$git) {
        return @()
    }
    $entries = & git -C $root ls-files --others --directory --exclude-standard -- $Directory 2>$null
    if ($LASTEXITCODE -ne 0) {
        $global:LASTEXITCODE = 0
        return @()
    }
    $exclusions = @()
    foreach ($entry in $entries) {
        if (!$entry.EndsWith('/')) {
            continue
        }
        $full = [IO.Path]::GetFullPath((Join-Path $root $entry.TrimEnd('/')))
        if (Test-Path -LiteralPath (Join-Path $full '.git')) {
            Write-Warning "Skipping untracked nested repository: $entry"
            $exclusions += $full
        }
    }
    return $exclusions
}

function Set-StagedManifest {
    param([Parameter(Mandatory)][string]$Project)
    $packages = Join-Path $Project 'Packages'
    New-Item -ItemType Directory -Force -Path $packages | Out-Null
    $manifest = Get-Content -LiteralPath (Join-Path $root 'Packages/manifest.json') -Raw | ConvertFrom-Json
    $manifest.dependencies | Add-Member -NotePropertyName $CoveragePackage -NotePropertyValue $CoveragePackageVersion -Force
    $json = $manifest | ConvertTo-Json -Depth 16
    $target = Join-Path $packages 'manifest.json'
    if (!(Test-Path -LiteralPath $target) -or (Get-Content -LiteralPath $target -Raw) -ne $json) {
        [IO.File]::WriteAllText($target, $json, [Text.UTF8Encoding]::new($false))
    }
    $lockSource = Join-Path $root 'Packages/packages-lock.json'
    $lockStamp = Join-Path $Project 'Packages/.source-lock-hash'
    if (Test-Path -LiteralPath $lockSource) {
        $hash = (Get-FileHash -LiteralPath $lockSource -Algorithm SHA256).Hash
        if (!(Test-Path -LiteralPath $lockStamp) -or (Get-Content -LiteralPath $lockStamp -Raw).Trim() -ne $hash) {
            Copy-Item -LiteralPath $lockSource -Destination (Join-Path $packages 'packages-lock.json') -Force
            Set-Content -LiteralPath $lockStamp -Value $hash
        }
    }
}

function Sync-Project {
    param([Parameter(Mandatory)][string]$Project)
    $stagedTests = [IO.Path]::GetFullPath((Join-Path $Project "Assets/$StagedTestsFolder"))
    $assetExclusions = @(Get-NestedRepositoryExclusions -Directory 'Assets')
    $assetFileExclusions = @()
    foreach ($exclusion in $assetExclusions) {
        $assetFileExclusions += "$exclusion.meta"
    }
    # The staged test folder and the generated internals files only exist in the copy; exclude them so the mirror keeps them.
    Sync-Directory -Source (Join-Path $root 'Assets') -Destination (Join-Path $Project 'Assets') `
        -ExcludeDirectories (@($assetExclusions) + @($stagedTests)) `
        -ExcludeFiles (@($assetFileExclusions) + @("$stagedTests.meta", $InternalsFileName, "$InternalsFileName.meta"))
    $projectSettings = [IO.Path]::GetFullPath((Join-Path $Project 'ProjectSettings/ProjectSettings.asset'))
    Sync-Directory -Source (Join-Path $root 'ProjectSettings') -Destination (Join-Path $Project 'ProjectSettings') -ExcludeFiles @($projectSettings)
    # PlayMode tests of the main project compile into Assembly-CSharp, which references the test
    # runner only when "Enable playmode tests for all assemblies" is on.
    $settingsText = [IO.File]::ReadAllText((Join-Path $root 'ProjectSettings/ProjectSettings.asset'))
    $settingsText = $settingsText -replace 'playModeTestRunnerEnabled: 0', 'playModeTestRunnerEnabled: 1'
    Write-FileIfChanged -Path $projectSettings -Content $settingsText
    # Local file: packages in the manifest resolve relative to Packages, so ThirdParty keeps its relative location.
    Sync-Directory -Source (Join-Path $root 'ThirdParty') -Destination (Join-Path $Project 'ThirdParty') `
        -ExcludeDirectories @('.git') -ExcludeFiles @('.git')
    Set-StagedManifest -Project $Project
}

function Get-SelectedAreas {
    param([Parameter(Mandatory)][string]$SourceRoot)
    $selected = @()
    foreach ($directory in Get-ChildItem -LiteralPath (Join-Path $PSScriptRoot $SourceRoot) -Directory -ErrorAction SilentlyContinue) {
        $name = "$SourceRoot/$($directory.Name)"
        if ($directory.Name -eq 'Shared' -or $Area.Count -eq 0 -or $Area -contains $name) {
            $selected += $directory
        }
    }
    return $selected
}

function Sync-Tests {
    param([Parameter(Mandatory)][string]$Project)
    foreach ($requested in $Area) {
        $parts = $requested -split '/'
        if ($parts.Count -ne 2 -or $TestSourceRoots -notcontains $parts[0] -or !(Test-Path -LiteralPath (Join-Path $PSScriptRoot $requested) -PathType Container)) {
            throw "Unknown test area '$requested'. Use Game/<Area> or Plugins/<Module>."
        }
    }
    # Unity generates the .meta files of staged tests; keep them so GUIDs stay stable between runs.
    Sync-Directory -Source (Join-Path $PSScriptRoot 'Shared') -Destination (Join-Path $Project "Assets/$StagedTestsFolder/Shared") -ExcludeFiles @('*.meta')
    $gameEditor = Join-Path $Project "Assets/$StagedTestsFolder/Editor/Game"
    $gamePlayMode = Join-Path $Project "Assets/$StagedTestsFolder/PlayMode/Game"
    $plugins = Join-Path $Project "Assets/$StagedTestsFolder/Plugins"

    # Game/<Area> compiles into Assembly-CSharp-Editor (EditMode); its PlayMode subfolder is staged
    # outside every Editor folder so it compiles into Assembly-CSharp and runs in Play Mode.
    $gameAreas = @(Get-SelectedAreas -SourceRoot 'Game')
    Remove-UnselectedAreas -DestinationRoot $gameEditor -Selected $gameAreas
    Remove-UnselectedAreas -DestinationRoot $gamePlayMode -Selected @($gameAreas | Where-Object { Test-Path -LiteralPath (Join-Path $_.FullName 'PlayMode') })
    foreach ($directory in $gameAreas) {
        Sync-Directory -Source $directory.FullName -Destination (Join-Path $gameEditor $directory.Name) -ExcludeDirectories @('PlayMode') -ExcludeFiles @('*.meta')
        $playMode = Join-Path $directory.FullName 'PlayMode'
        if (Test-Path -LiteralPath $playMode) {
            Sync-Directory -Source $playMode -Destination (Join-Path $gamePlayMode $directory.Name) -ExcludeFiles @('*.meta')
        }
    }

    # Plugins/<Module> carries its own asmdefs, including an optional PlayMode/ asmdef.
    $pluginAreas = @(Get-SelectedAreas -SourceRoot 'Plugins')
    Remove-UnselectedAreas -DestinationRoot $plugins -Selected $pluginAreas
    foreach ($directory in $pluginAreas) {
        Sync-Directory -Source $directory.FullName -Destination (Join-Path $plugins $directory.Name) -ExcludeFiles @('*.meta')
    }
}

function Remove-UnselectedAreas {
    param(
        [Parameter(Mandatory)][string]$DestinationRoot,
        [System.IO.DirectoryInfo[]]$Selected = @()
    )
    New-Item -ItemType Directory -Force -Path $DestinationRoot | Out-Null
    $selectedNames = @($Selected | ForEach-Object { $_.Name })
    foreach ($staged in Get-ChildItem -LiteralPath $DestinationRoot -Directory) {
        if ($selectedNames -notcontains $staged.Name) {
            Remove-Item -LiteralPath $staged.FullName -Recurse -Force
            Remove-Item -LiteralPath "$($staged.FullName).meta" -Force -ErrorAction SilentlyContinue
        }
    }
}

function Write-FileIfChanged {
    param(
        [Parameter(Mandatory)][string]$Path,
        [Parameter(Mandatory)][string]$Content
    )
    if ((Test-Path -LiteralPath $Path) -and [IO.File]::ReadAllText($Path) -eq $Content) {
        return
    }
    [IO.File]::WriteAllText($Path, $Content, [Text.UTF8Encoding]::new($false))
}

function Get-InternalsSource {
    param([Parameter(Mandatory)][string[]]$Friends)
    $lines = @('// Generated by Tools/Tests/MajdataPlay.Tests/Run-Tests.ps1 in the staged test copy only.')
    foreach ($friend in $Friends) {
        $lines += "[assembly: System.Runtime.CompilerServices.InternalsVisibleTo(`"$friend`")]"
    }
    return ($lines -join "`r`n") + "`r`n"
}

function Set-InternalsVisibleTo {
    param([Parameter(Mandatory)][string]$Project)
    # Friends come from every test asmdef in the repository, not only the staged areas, so the
    # production assemblies do not recompile whenever a different -Area subset is staged.
    # Each module's optional PlayMode assembly is granted up front for the same reason.
    $friends = @('Assembly-CSharp-Editor')
    foreach ($asmdef in Get-ChildItem -LiteralPath $PSScriptRoot -Recurse -Filter '*.asmdef') {
        $name = (Get-Content -LiteralPath $asmdef.FullName -Raw | ConvertFrom-Json).name
        $friends += $name
        if (!$name.EndsWith('.PlayMode')) {
            $friends += "$name.PlayMode"
        }
    }
    $friends = @($friends | Sort-Object -Unique)
    # A file directly under the staged test folder (no asmdef) compiles into Assembly-CSharp.
    Write-FileIfChanged -Path (Join-Path $Project "Assets/$StagedTestsFolder/$InternalsFileName") -Content (Get-InternalsSource -Friends @('Assembly-CSharp-Editor'))
    $source = Get-InternalsSource -Friends $friends
    foreach ($asmdef in Get-ChildItem -LiteralPath (Join-Path $Project 'Assets/Plugins/MajdataPlay') -Recurse -Filter '*.asmdef') {
        Write-FileIfChanged -Path (Join-Path $asmdef.DirectoryName $InternalsFileName) -Content $source
    }
}

function Initialize-Library {
    param([Parameter(Mandatory)][string]$Project)
    $library = Join-Path $Project 'Library'
    $source = Join-Path $root 'Library'
    if ($NoLibrarySeed -or (Test-Path -LiteralPath $library) -or !(Test-Path -LiteralPath $source)) {
        return
    }
    Write-Host "Seeding the staged Library from $source"
    try {
        Sync-Directory -Source $source -Destination $library -ExcludeDirectories @('Bee', 'BurstCache', 'il2cpp_cache') -ExcludeFiles @('*.lock')
    }
    catch {
        Write-Warning "Library seeding failed, Unity will import from scratch: $_"
    }
}

function Get-QuotedArgument {
    param([Parameter(Mandatory)][string]$Value)
    if ($Value -match '[\s"]') {
        return '"' + $Value.Replace('"', '\"') + '"'
    }
    return $Value
}

function Invoke-Unity {
    param(
        [Parameter(Mandatory)][string]$Executable,
        [Parameter(Mandatory)][string[]]$Arguments,
        [Parameter(Mandatory)][string]$LogFile
    )
    $quoted = @($Arguments | ForEach-Object { Get-QuotedArgument $_ })
    $startArguments = @{
        FilePath = $Executable
        ArgumentList = $quoted
        PassThru = $true
    }
    if ($IsWindowsHost) {
        $startArguments['WindowStyle'] = 'Hidden'
    }
    $process = Start-Process @startArguments
    if (!$process.WaitForExit($TimeoutMinutes * 60 * 1000)) {
        $process.Kill()
        throw "Unity timed out after $TimeoutMinutes minutes. Log: $LogFile"
    }
    return $process.ExitCode
}

function Write-TestSummary {
    param(
        [Parameter(Mandatory)][string]$Platform,
        [Parameter(Mandatory)][string]$ResultFile,
        [Parameter(Mandatory)][string]$LogFile,
        [Parameter(Mandatory)][int]$ExitCode
    )
    if (!(Test-Path -LiteralPath $ResultFile)) {
        Write-Host "[$Platform] No test results (Unity exit code $ExitCode). Log: $LogFile" -ForegroundColor Red
        if (Test-Path -LiteralPath $LogFile) {
            $errors = @(Select-String -LiteralPath $LogFile -Pattern 'error CS\d+' | Select-Object -First 40)
            if ($errors.Count -gt 0) {
                $errors | ForEach-Object { Write-Host $_.Line }
            }
            else {
                Get-Content -LiteralPath $LogFile -Tail 60 | ForEach-Object { Write-Host $_ }
            }
        }
        return $false
    }
    [xml]$document = Get-Content -LiteralPath $ResultFile -Raw
    $run = $document.'test-run'
    $color = 'Green'
    if ([int]$run.failed -gt 0) {
        $color = 'Red'
    }
    Write-Host ("[{0}] total {1}, passed {2}, failed {3}, skipped {4}, inconclusive {5}. Results: {6}" -f `
        $Platform, $run.total, $run.passed, $run.failed, $run.skipped, $run.inconclusive, $ResultFile) -ForegroundColor $color
    foreach ($case in $document.SelectNodes("//test-case[@result='Failed']")) {
        $message = ''
        $node = $case.SelectSingleNode('failure/message')
        if ($node) {
            $message = ($node.InnerText.Trim() -split "`n")[0]
        }
        Write-Host "  FAILED $($case.fullname): $message" -ForegroundColor Red
    }
    return ([int]$run.failed -eq 0 -and [int]$run.total -gt 0)
}

$unity = Resolve-UnityPath
$work = Resolve-WorkRoot
$project = Join-Path $work 'Project'
$results = Join-Path $work 'Results'
$logs = Join-Path $work 'Logs'
$coverageDirectory = Join-Path $work 'Coverage'
foreach ($directory in @($project, $results, $logs)) {
    New-Item -ItemType Directory -Force -Path $directory | Out-Null
}
if (Test-Path -LiteralPath (Join-Path $project 'Temp/UnityLockfile')) {
    try {
        [IO.File]::Open((Join-Path $project 'Temp/UnityLockfile'), 'Open', 'ReadWrite', 'None').Dispose()
    }
    catch {
        throw "Another Unity instance has $project open."
    }
}

Write-Host "Unity: $unity"
Write-Host "Staged project: $project"
if (!$SkipProjectSync) {
    Sync-Project -Project $project
}
Initialize-Library -Project $project
Sync-Tests -Project $project
Set-InternalsVisibleTo -Project $project

$platforms = @('EditMode', 'PlayMode')
if ($TestPlatform -ne 'All') {
    $platforms = @($TestPlatform)
}
if ($Coverage -and (Test-Path -LiteralPath $coverageDirectory)) {
    Remove-Item -LiteralPath $coverageDirectory -Recurse -Force
}

$passed = $true
foreach ($platform in $platforms) {
    $resultFile = Join-Path $results "$platform.xml"
    $logFile = Join-Path $logs "$platform.log"
    Remove-Item -LiteralPath $resultFile -Force -ErrorAction SilentlyContinue
    $arguments = @('-batchmode', '-projectPath', $project, '-runTests', '-testPlatform', $platform,
        '-testResults', $resultFile, '-logFile', $logFile)
    if ($NoGraphics) {
        $arguments += '-nographics'
    }
    if ($TestFilter) {
        $arguments += @('-testFilter', $TestFilter)
    }
    if ($AssemblyNames) {
        $arguments += @('-assemblyNames', $AssemblyNames)
    }
    if ($Coverage) {
        $options = @(
            'generateHtmlReport',
            'generateAdditionalReports',
            'generateBadgeReport',
            'generateAdditionalMetrics',
            'dontClear',
            'assemblyFilters:+Assembly-CSharp,+Assembly-CSharp-Editor,+MajdataPlay.*,-MajdataPlay.Tests.*',
            "pathFilters:-**/Assets/$StagedTestsFolder/**"
        ) -join ';'
        $arguments += @('-debugCodeOptimization', '-enableCodeCoverage', '-coverageResultsPath', $coverageDirectory, '-coverageOptions', $options)
    }
    Write-Host "Running $platform tests..."
    $exitCode = Invoke-Unity -Executable $unity -Arguments $arguments -LogFile $logFile
    if (!(Write-TestSummary -Platform $platform -ResultFile $resultFile -LogFile $logFile -ExitCode $exitCode)) {
        $passed = $false
    }
}

if ($Coverage) {
    $summary = Get-ChildItem -LiteralPath $coverageDirectory -Recurse -Filter 'Summary.xml' -ErrorAction SilentlyContinue | Select-Object -First 1
    if ($summary) {
        [xml]$coverageSummary = Get-Content -LiteralPath $summary.FullName -Raw
        $node = $coverageSummary.SelectSingleNode('//Summary/Linecoverage')
        if ($node) {
            Write-Host "Line coverage: $($node.InnerText)%"
        }
    }
    Write-Host "Coverage report: $(Join-Path $coverageDirectory 'Report/index.html')"
}

if (!$passed) {
    exit 1
}
Write-Host 'All test runs passed.' -ForegroundColor Green
