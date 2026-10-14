param(
    [string]$UnityPath = '',
    [int]$TimeoutMinutes = 10
)

$ErrorActionPreference = 'Stop'
$repositoryRoot = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
$versionFile = Join-Path $repositoryRoot 'ProjectSettings/ProjectVersion.txt'
$versionText = Get-Content -LiteralPath $versionFile -Raw
$versionMatch = [regex]::Match($versionText, '(?m)^m_EditorVersion:\s*(\S+)')
if (-not $versionMatch.Success)
{
    throw 'ProjectVersion.txt does not identify the required Unity Editor.'
}
$requiredVersion = $versionMatch.Groups[1].Value
if ([string]::IsNullOrWhiteSpace($UnityPath))
{
    $UnityPath = Join-Path $env:ProgramFiles "Unity Editors/$requiredVersion/Editor/Unity.exe"
}
$UnityPath = [System.IO.Path]::GetFullPath($UnityPath)
if (-not (Test-Path -LiteralPath $UnityPath -PathType Leaf))
{
    throw "The required Unity $requiredVersion executable is missing: $UnityPath"
}
$installedVersion = [System.Diagnostics.FileVersionInfo]::GetVersionInfo($UnityPath).ProductVersion
if ($installedVersion.Split('_')[0] -ne $requiredVersion)
{
    throw "This validation requires Unity $requiredVersion; '$UnityPath' reports '$installedVersion'."
}
if ($TimeoutMinutes -lt 1)
{
    throw 'TimeoutMinutes must be positive.'
}

$validationRoot = Join-Path $repositoryRoot 'Tools/Tests/AndroidJavaWrapperGeneratorValidation'
$workRoot = [System.IO.Path]::GetFullPath((Join-Path $validationRoot '.work'))
$projectRoot = Join-Path $workRoot ('native-editor-' + [guid]::NewGuid().ToString('N'))
$logPath = $projectRoot + '.log'
$editorAssets = Join-Path $projectRoot 'Assets/Plugins/MajdataPlay/Platform/Android/Editor'
$runtimeAssets = Split-Path -Parent $editorAssets
$smokeAssets = Join-Path $projectRoot 'Assets/Editor'
$toolDirectory = Join-Path $repositoryRoot 'Tools/CSharp/AndroidJavaWrapperGenerator'
$stagedTool = Join-Path $projectRoot 'Tools/CSharp/AndroidJavaWrapperGenerator'
foreach ($directory in @($editorAssets, $smokeAssets, $stagedTool, (Join-Path $projectRoot 'ProjectSettings'), (Join-Path $projectRoot 'Packages')))
{
    [System.IO.Directory]::CreateDirectory($directory) | Out-Null
}
$utf8 = [System.Text.UTF8Encoding]::new($false)
[System.IO.File]::WriteAllText((Join-Path $projectRoot 'ProjectSettings/ProjectVersion.txt'), "m_EditorVersion: $requiredVersion" + [Environment]::NewLine, $utf8)
[System.IO.File]::WriteAllText((Join-Path $projectRoot 'Packages/manifest.json'), '{"dependencies":{"com.unity.modules.androidjni":"1.0.0"}}', $utf8)

# Stage the independent CLI without copying its binaries, intermediates, or caches.
foreach ($file in Get-ChildItem -LiteralPath $toolDirectory -Recurse -File)
{
    $relative = $file.FullName.Substring($toolDirectory.Length + 1)
    if ($relative -match '(^|[/\\])(bin|obj|\.work)([/\\]|$)')
    {
        continue
    }
    $destination = Join-Path $stagedTool $relative
    [System.IO.Directory]::CreateDirectory((Split-Path -Parent $destination)) | Out-Null
    Copy-Item -LiteralPath $file.FullName -Destination $destination
}

# Compile the production partial extensions in the same independent wrapper assembly.
$extensionDirectory = Join-Path $repositoryRoot 'Tools/AndroidJavaWrapper/src'
foreach ($file in Get-ChildItem -LiteralPath $extensionDirectory -Recurse -Filter '*.e.cs' -File)
{
    $relative = $file.FullName.Substring($extensionDirectory.Length + 1)
    $destination = Join-Path (Join-Path $projectRoot 'Tools/AndroidJavaWrapper/src') $relative
    [System.IO.Directory]::CreateDirectory((Split-Path -Parent $destination)) | Out-Null
    Copy-Item -LiteralPath $file.FullName -Destination $destination
}

# Stage every production Java input, preserving metadata when available.
$javaDirectory = Join-Path $repositoryRoot 'Assets/Plugins/Android'
foreach ($file in Get-ChildItem -LiteralPath $javaDirectory -Recurse -File)
{
    if ($file.Extension -notin @('.java', '.class', '.jar', '.aar'))
    {
        continue
    }
    $relative = $file.FullName.Substring($javaDirectory.Length + 1)
    $destination = Join-Path (Join-Path $projectRoot 'Assets/Plugins/Android') $relative
    [System.IO.Directory]::CreateDirectory((Split-Path -Parent $destination)) | Out-Null
    Copy-Item -LiteralPath $file.FullName -Destination $destination
    if (Test-Path -LiteralPath ($file.FullName + '.meta') -PathType Leaf)
    {
        Copy-Item -LiteralPath ($file.FullName + '.meta') -Destination ($destination + '.meta')
    }
}

$productionAndroid = Join-Path $repositoryRoot 'Assets/Plugins/MajdataPlay/Platform/Android'
$runtimeDefinition = Get-Content -LiteralPath (Join-Path $productionAndroid 'MajdataPlay.Platform.Android.asmdef') -Raw | ConvertFrom-Json
$runtimeDefinition.references = @()
[System.IO.File]::WriteAllText((Join-Path $runtimeAssets 'MajdataPlay.Platform.Android.asmdef'), ($runtimeDefinition | ConvertTo-Json -Depth 5), $utf8)
Copy-Item -LiteralPath (Join-Path $productionAndroid 'MajdataPlay.Platform.Android.asmdef.meta') -Destination $runtimeAssets
foreach ($name in @('JavaWrapperRefresh.cs', 'JavaWrapperRefresh.cs.meta', 'JavaWrapperOutput.cs', 'JavaWrapperOutput.cs.meta', 'JavaWrapperGenerationRequest.cs', 'JavaWrapperGenerationRequest.cs.meta', 'MajdataPlay.Platform.Android.Editor.asmdef', 'MajdataPlay.Platform.Android.Editor.asmdef.meta'))
{
    Copy-Item -LiteralPath (Join-Path (Join-Path $productionAndroid 'Editor') $name) -Destination $editorAssets
}
Copy-Item -LiteralPath (Join-Path $validationRoot 'Unity/JavaWrapperEditorSmoke.cs.txt') -Destination (Join-Path $smokeAssets 'JavaWrapperEditorSmoke.cs')

$info = [System.Diagnostics.ProcessStartInfo]::new()
$info.FileName = $UnityPath
$info.Arguments = '-batchmode -nographics -projectPath "' + $projectRoot + '" -executeMethod MajdataPlay.Tests.AndroidJavaWrapperGeneratorValidation.Unity.JavaWrapperEditorSmoke.Run -logFile "' + $logPath + '"'
$info.WorkingDirectory = $projectRoot
$info.UseShellExecute = $false
$info.CreateNoWindow = $true
$info.WindowStyle = [System.Diagnostics.ProcessWindowStyle]::Hidden
Write-Host "Validating standalone wrapper refresh in Unity $requiredVersion."
Write-Host "Isolated project: $projectRoot"
Write-Host "Editor log: $logPath"
$process = [System.Diagnostics.Process]::Start($info)
if ($null -eq $process)
{
    throw 'The isolated Unity Editor process could not be started.'
}
try
{
    $deadline = [DateTime]::UtcNow.AddMinutes($TimeoutMinutes)
    while (-not $process.WaitForExit(1000))
    {
        if ([DateTime]::UtcNow -gt $deadline)
        {
            $process.Kill($true)
            throw "The isolated Editor exceeded the timeout. See $logPath"
        }
    }
    $log = Get-Content -LiteralPath $logPath -Raw
    if ($process.ExitCode -ne 0 -or -not $log.Contains('EDITOR_WRAPPER_SMOKE_PASS:') -or $log -match 'error CS\d{4}')
    {
        throw "Wrapper refresh validation failed with exit code $($process.ExitCode). See $logPath"
    }
    Write-Host ([regex]::Match($log, 'EDITOR_WRAPPER_SMOKE_PASS:[^\r\n]*').Value)
    Write-Host 'CLI cancellation, independent DLL compilation, partial extensions, Editor plugin importing, and repeat-refresh DLL/GUID/timestamp checks passed.'
}
finally
{
    $process.Dispose()
}
