[CmdletBinding()]
param(
    [string]$ScenePath = '',
    [string]$BaselinePath = ''
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
if ([string]::IsNullOrEmpty($ScenePath))
{
    $ScenePath = Join-Path $PSScriptRoot '../../../../Assets/Scenes/Init.unity'
}

# Read only: neither validation nor baseline comparison saves a Unity asset.
function Get-SensorFlags([string]$Text)
{
    $documents = [regex]::Matches($Text, '(?ms)^--- !u!(?<class>\d+) &(?<id>-?\d+)[^\r\n]*\r?\n.*?(?=^--- |\z)')
    foreach ($document in $documents)
    {
        if ($document.Groups['class'].Value -eq '1')
        {
            $name = [regex]::Match($document.Value, '(?m)^  m_Name: (?<name>[ABDE][1-8]|C[12])\r?$')
            if (!$name.Success)
            {
                continue
            }
            $flag = [regex]::Match($document.Value, '(?m)^  m_StaticEditorFlags: (?<value>\d+)')
            if (!$flag.Success)
            {
                throw "Missing explicit flags for $($name.Groups['name'].Value)."
            }
            $value = $flag.Groups['value']
            [pscustomobject]@{ Name = $name.Groups['name'].Value; Kind = 'Explicit'; Offset = $document.Index + $value.Index; Length = $value.Length; Flags = [int]$value.Value }
        }
        elseif ($document.Groups['class'].Value -eq '1001')
        {
            $names = [regex]::Matches($document.Value, '(?m)^    - target: \{fileID: (?<id>-?\d+),[^\r\n]+\}\r?\n      propertyPath: m_Name\r?\n      value: (?<name>[ABDE][1-8]|C[12])\r?$')
            foreach ($name in $names)
            {
                $pattern = '(?m)^    - target: \{fileID: ' + [regex]::Escape($name.Groups['id'].Value) + ',[^\r\n]+\}\r?\n      propertyPath: m_StaticEditorFlags\r?\n      value: (?<value>\d+)'
                $flag = [regex]::Match($document.Value, $pattern)
                if (!$flag.Success)
                {
                    throw "Missing prefab override flags for $($name.Groups['name'].Value)."
                }
                $value = $flag.Groups['value']
                [pscustomobject]@{ Name = $name.Groups['name'].Value; Kind = 'PrefabOverride'; Offset = $document.Index + $value.Index; Length = $value.Length; Flags = [int]$value.Value }
            }
        }
    }
}

$source = [System.IO.File]::ReadAllText((Resolve-Path -LiteralPath $ScenePath).Path)
$flags = @(Get-SensorFlags $source)
$expected = @('C1', 'C2') + @('A', 'B', 'D', 'E' | ForEach-Object { $group = $_; 1..8 | ForEach-Object { "$group$_" } })
$actual = @($flags.Name | Sort-Object)
if ($flags.Count -ne 34 -or ($actual -join ',') -cne (($expected | Sort-Object) -join ','))
{
    throw 'Expected exactly A1-A8, B1-B8, C1-C2, D1-D8, and E1-E8.'
}
if (@($flags | Where-Object Kind -eq 'Explicit').Count -ne 29 -or @($flags | Where-Object Kind -eq 'PrefabOverride').Count -ne 5)
{
    throw 'The scene must retain 29 explicit areas and five FBX instance overrides.'
}
foreach ($entry in $flags)
{
    # UnityEditor.StaticEditorFlags.BatchingStatic is bit 4; other static flags remain independent.
    if (($entry.Flags -band 4) -ne 0)
    {
        throw "$($entry.Name) still enables static batching; runtime sharedMesh changes can be ignored in Players."
    }
}

if (![string]::IsNullOrEmpty($BaselinePath))
{
    $baseline = [System.IO.File]::ReadAllText((Resolve-Path -LiteralPath $BaselinePath).Path)
    $original = @(Get-SensorFlags $baseline)
    if ($original.Count -ne 34)
    {
        throw 'The baseline must have all 34 sensor flag records.'
    }
    $restored = $source
    foreach ($entry in $flags | Sort-Object Offset -Descending)
    {
        $old = @($original | Where-Object Name -eq $entry.Name)
        if ($old.Count -ne 1 -or $entry.Flags -ne ($old[0].Flags -band (-bnot 4)))
        {
            throw "Unrelated static flags changed for $($entry.Name)."
        }
        $restored = $restored.Remove($entry.Offset, $entry.Length).Insert($entry.Offset, $old[0].Flags.ToString())
    }
    if ($restored -cne $baseline)
    {
        throw 'The scene changed outside the 34 sensor batching flags: check collider, camera, hierarchy, and other serialized data.'
    }
    Write-Output 'SCENE_BASELINE_PRESERVED: restoring only the 34 flag values exactly restores the original scene text.'
}
Write-Output 'SENSOR_SCENE_FLAGS_VALIDATION_PASSED: 29 explicit sensors and five FBX overrides all exclude BatchingStatic.'
