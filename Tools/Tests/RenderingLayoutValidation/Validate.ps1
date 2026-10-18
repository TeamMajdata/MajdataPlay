[CmdletBinding()]
param()

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$repositoryRoot = (Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '../../..')).Path
$assemblyPath = 'Assets/Plugins/MajdataPlay/Rendering/MajdataPlay.Rendering.asmdef'
$pipelineRoot = 'Assets/Plugins/MajdataPlay/Rendering/RenderPipelines/MajRenderPipeline'
$projectRoot = 'Assets/Rendering/RenderPipelines/MajRenderPipeline'
$obsoleteRoot = 'Assets/Rendering/' + 'MajRenderPipeline'
$script:checkCount = 0

function Assert-Condition
{
    param([bool]$Condition, [string]$Message)

    $script:checkCount++
    if (!$Condition)
    {
        throw $Message
    }
}

function Get-AssetGuid
{
    param([string]$MetaPath)

    $content = Get-Content -LiteralPath (Join-Path $repositoryRoot $MetaPath) -Raw
    $match = [regex]::Match($content, '(?m)^guid: ([0-9a-f]{32})\r?$')
    Assert-Condition $match.Success "Missing or invalid GUID: $MetaPath"
    return $match.Groups[1].Value
}

function Get-RepositoryRelativePath
{
    param([string]$Path)

    return $Path.Substring($repositoryRoot.Length + 1).Replace('\', '/')
}

Push-Location $repositoryRoot
try
{
    # Git omits ignored generated outputs and does not descend into submodules.
    $listedFiles = @(& git -c core.quotepath=false ls-files --cached --others --exclude-standard -- Assets Tools ProjectSettings)
    if ($LASTEXITCODE -ne 0)
    {
        throw 'Unable to enumerate repository files with Git.'
    }

    $files = @($listedFiles | Sort-Object -Unique | Where-Object { Test-Path -LiteralPath $_ -PathType Leaf })
    $assetPaths = [System.Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
    foreach ($file in $files)
    {
        if ($file.StartsWith('Assets/', [StringComparison]::Ordinal))
        {
            $null = $assetPaths.Add($file)
        }
    }

    Assert-Condition (!(Test-Path -LiteralPath $obsoleteRoot)) "Obsolete pipeline directory remains: $obsoleteRoot"
    Assert-Condition (!(Test-Path -LiteralPath "$obsoleteRoot.meta")) 'Obsolete pipeline directory metadata remains.'
    foreach ($path in @("$pipelineRoot/Runtime", "$pipelineRoot/Editor", "$pipelineRoot/Library",
        "$projectRoot/Presets", "$projectRoot/Documentation"))
    {
        Assert-Condition (Test-Path -LiteralPath $path -PathType Container) "Missing layout directory: $path"
    }

    $movedGuids = @{}
    foreach ($root in @($pipelineRoot, $projectRoot))
    {
        Assert-Condition (Test-Path -LiteralPath "$root.meta" -PathType Leaf) "Missing root metadata: $root.meta"
        $items = @((Get-Item -LiteralPath $root)) + @(Get-ChildItem -LiteralPath $root -Recurse -Force)
        foreach ($item in $items)
        {
            $path = Get-RepositoryRelativePath $item.FullName
            if (!$item.PSIsContainer -and $item.Extension -eq '.meta')
            {
                $assetPath = $path.Substring(0, $path.Length - 5)
                Assert-Condition (Test-Path -LiteralPath $assetPath) "Orphan metadata: $path"
                continue
            }

            $metaPath = "$path.meta"
            Assert-Condition (Test-Path -LiteralPath $metaPath -PathType Leaf) "Missing asset metadata: $path"
            $guid = Get-AssetGuid $metaPath
            Assert-Condition (!$movedGuids.ContainsKey($guid)) "Duplicate moved GUID: $guid ($path)"
            $movedGuids[$guid] = $metaPath
        }
    }

    # The reusable assembly now owns the parent Rendering directory; its GUID
    # still binds the pipeline's Editor partial and all project consumers.
    $assemblyGuid = Get-AssetGuid "$assemblyPath.meta"
    Assert-Condition (!$movedGuids.ContainsKey($assemblyGuid)) 'Pipeline assembly GUID duplicates a pipeline asset.'
    $movedGuids[$assemblyGuid] = "$assemblyPath.meta"

    foreach ($metaPath in $files | Where-Object { $_ -like 'Assets/*.meta' })
    {
        $content = Get-Content -LiteralPath $metaPath -Raw
        $match = [regex]::Match($content, '(?m)^guid: ([0-9a-f]{32})\r?$')
        if ($match.Success -and $movedGuids.ContainsKey($match.Groups[1].Value))
        {
            Assert-Condition ($movedGuids[$match.Groups[1].Value] -ceq $metaPath) "Moved GUID also exists at: $metaPath"
        }
    }

    Assert-Condition ($assemblyGuid -ceq '4f28209e4fc743e3899c9f026369d7d0') 'Pipeline assembly GUID changed.'
    $assembly = Get-Content -LiteralPath $assemblyPath -Raw | ConvertFrom-Json
    Assert-Condition ($assembly.name -ceq 'MajdataPlay.MajRenderPipe') 'Pipeline assembly identity changed.'
    Assert-Condition (@($assembly.references).Count -eq 0) 'Pipeline assembly must remain engine-only.'
    $assemblyReference = Get-Content -LiteralPath "$pipelineRoot/Editor/MajdataPlay.MajRenderPipe.asmref" -Raw | ConvertFrom-Json
    Assert-Condition ($assemblyReference.reference -ceq "GUID:$assemblyGuid") 'Editor partial does not reference the runtime assembly.'
    Assert-Condition (@(Get-ChildItem -LiteralPath "$pipelineRoot/Editor" -Filter '*.asmdef' -Recurse).Count -eq 0) 'Editor partial must remain in the runtime assembly.'
    foreach ($editorFile in Get-ChildItem -LiteralPath "$pipelineRoot/Editor" -Filter '*.cs' -Recurse)
    {
        $content = Get-Content -LiteralPath $editorFile.FullName -Raw
        Assert-Condition ($content -match '\A(?:\uFEFF)?#if UNITY_EDITOR\b') "Editor source is missing its Player guard: $($editorFile.Name)"
    }

    $presetPath = "$projectRoot/Presets/MRP_Low.asset"
    $presetGuid = Get-AssetGuid "$presetPath.meta"
    Assert-Condition ($presetGuid -ceq '063c2b8a9db74c1ca9a8119baafee242') 'Active pipeline preset GUID changed.'
    $preset = Get-Content -LiteralPath $presetPath -Raw
    $assetScriptGuid = Get-AssetGuid "$pipelineRoot/Runtime/MajRenderPipelineAsset.cs.meta"
    $copyShaderGuid = Get-AssetGuid "$pipelineRoot/Runtime/Shaders/MajFinalCopy.shader.meta"
    Assert-Condition ($preset -match "m_Script: \{[^\r\n]*guid: $assetScriptGuid[,}]") 'Preset no longer references its pipeline asset script.'
    Assert-Condition ($preset -match "_finalCopyShader: \{[^\r\n]*guid: $copyShaderGuid[,}]") 'Preset no longer references its final-copy shader.'
    foreach ($settings in @(
        @{ Path = 'ProjectSettings/GraphicsSettings.asset'; Field = 'm_CustomRenderPipeline' },
        @{ Path = 'ProjectSettings/QualitySettings.asset'; Field = 'customRenderPipeline' }))
    {
        $content = Get-Content -LiteralPath $settings.Path -Raw
        Assert-Condition ($content -match "$($settings.Field): \{[^\r\n]*guid: $presetGuid[,}]") "Active pipeline reference changed: $($settings.Path)"
    }

    $textExtensions = @('.cs', '.csproj', '.asmdef', '.asmref', '.shader', '.hlsl', '.cginc',
        '.md', '.ps1', '.json', '.yml', '.yaml', '.txt')
    foreach ($file in $files | Where-Object { ($_ -like 'Assets/*' -or $_ -like 'Tools/*') -and [System.IO.Path]::GetExtension($_) -in $textExtensions })
    {
        $content = Get-Content -LiteralPath $file -Raw
        if ($null -eq $content)
        {
            continue
        }

        $content = $content.Replace('\', '/')
        Assert-Condition ($content.IndexOf($obsoleteRoot, [StringComparison]::OrdinalIgnoreCase) -lt 0) "Stale pipeline path: $file"
    }

    $shaderFiles = @($files | Where-Object {
        (($_.StartsWith("$pipelineRoot/", [StringComparison]::Ordinal) -or $_.StartsWith('Assets/Rendering/Shaders/', [StringComparison]::Ordinal)) -and
            [System.IO.Path]::GetExtension($_) -in @('.shader', '.hlsl', '.cginc')) -or
        $_ -ceq 'Assets/Plugins/Live2D/Cubism/Rendering/Resources/Live2D/Cubism/Shaders/CubismMajCore.cginc' -or
        $_ -ceq 'Assets/TextMesh Pro/Shaders/TMP_Sprite.shader'
    })
    $includeCount = 0
    foreach ($file in $shaderFiles)
    {
        $content = Get-Content -LiteralPath $file -Raw
        foreach ($match in [regex]::Matches($content, '(?m)^\s*#include\s+"([^"]+)"'))
        {
            $include = $match.Groups[1].Value.Replace('\', '/')
            if ($include.StartsWith('Assets/', [StringComparison]::OrdinalIgnoreCase))
            {
                $includePath = $include
            }
            elseif ($file.StartsWith("$pipelineRoot/Library/", [StringComparison]::Ordinal) -and $include.EndsWith('.hlsl', [StringComparison]::OrdinalIgnoreCase))
            {
                $parent = Split-Path -Parent (Join-Path $repositoryRoot $file)
                $fullPath = [System.IO.Path]::GetFullPath((Join-Path $parent $include))
                $includePath = Get-RepositoryRelativePath $fullPath
            }
            else
            {
                # Unity's built-in includes and SDK-local includes are outside this relocation.
                continue
            }

            Assert-Condition ($assetPaths.Contains($includePath)) "Missing shader include or incorrect path casing: $includePath ($file)"
            $includeCount++
        }
    }
    Assert-Condition ($includeCount -gt 0) 'No relocated shader includes were validated.'

    $settingProjectPath = 'Tools/Tests/SettingValidation/SettingValidation.csproj'
    [xml]$settingProject = Get-Content -LiteralPath $settingProjectPath -Raw
    $diagnosticsLinks = @($settingProject.SelectNodes('//Compile[@Link="Production/MajRenderDiagnostics.cs"]'))
    Assert-Condition ($diagnosticsLinks.Count -eq 1) 'Setting validation must link the production rendering diagnostics once.'
    $expectedLink = "../../../$pipelineRoot/Runtime/MajRenderDiagnostics.cs"
    Assert-Condition ($diagnosticsLinks[0].Include.Replace('\', '/') -ceq $expectedLink) 'Setting validation still links the old rendering diagnostics path.'
    Assert-Condition (Test-Path -LiteralPath (Join-Path (Split-Path -Parent $settingProjectPath) $expectedLink) -PathType Leaf) 'Setting validation rendering diagnostics source is missing.'

    Write-Output "PASS: Rendering layout validation ($script:checkCount checks; $includeCount shader includes)."
}
finally
{
    Pop-Location
}
