param(
    [Parameter(Mandatory = $true)][string] $GameDirectory,
    [ValidateSet('vulkan', 'd3d12')][string] $Graphics = 'vulkan'
)
$ErrorActionPreference = 'Stop'
$directory = (Resolve-Path -LiteralPath $GameDirectory).Path
$executable = Join-Path $directory 'MajdataPlay.exe'
if (-not (Test-Path -LiteralPath $executable -PathType Leaf) -or
    -not (Test-Path -LiteralPath (Join-Path $directory 'MajdataPlay_Data') -PathType Container)) {
    throw 'Select a MajdataPlay Player directory containing MajdataPlay.exe and MajdataPlay_Data.'
}
# Create a child-specific environment without changing this shell or the registry.
$launch = New-Object System.Diagnostics.ProcessStartInfo
$launch.FileName = $executable
$launch.WorkingDirectory = $directory
$launch.Arguments = "-force-$Graphics"
$launch.UseShellExecute = $false
$launch.EnvironmentVariables['DISABLE_VULKAN_OBS_CAPTURE'] = '1'
$gameProcess = [Diagnostics.Process]::Start($launch)
if ($null -eq $gameProcess) {
    throw 'The Player did not start.'
}
Write-Output ("Started MajdataPlay PID {0} with the OBS Vulkan capture layer disabled for this child." -f $gameProcess.Id)
# Normal interactive use: no navigation, monitoring, termination, or settings edits.
