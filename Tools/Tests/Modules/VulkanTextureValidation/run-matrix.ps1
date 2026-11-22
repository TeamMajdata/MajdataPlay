param(
    [Parameter(Mandatory = $true)][string] $Player,
    [Parameter(Mandatory = $true)][string] $OutputDirectory,
    [ValidateRange(10, 600)][int] $SecondsPerCase = 180,
    [ValidateRange(1, 5)][int] $DynamicRepeats = 1,
    [ValidateRange(1, 120)][int] $UploadHz = 30,
    [ValidateSet('standard', 'high-rate', 'refresh-rate')][string] $Mode = 'standard',
    [switch] $DisableObsCapture
)
$ErrorActionPreference = 'Stop'
$playerPath = (Resolve-Path -LiteralPath $Player).Path
$outputPath = [IO.Path]::GetFullPath($OutputDirectory)
if (Test-Path -LiteralPath $outputPath) {
    throw 'Use a new output directory to preserve previous evidence.'
}
New-Item -ItemType Directory -Path $outputPath | Out-Null
$cases = @(
    @{ Name = 'vulkan-static'; Api = 'vulkan'; Static = 1; VSync = 1; Fps = 144 }
)
for ($index = 1; $index -le $DynamicRepeats; $index++) {
    $cases += @{ Name = "vulkan-dynamic-$index"; Api = 'vulkan'; Static = 0; VSync = 1; Fps = 144 }
}
$cases += @{ Name = 'd3d12-dynamic'; Api = 'd3d12'; Static = 0; VSync = 1; Fps = 144 }
$cases += @{ Name = 'vulkan-dynamic-60'; Api = 'vulkan'; Static = 0; VSync = 0; Fps = 60 }
if ($Mode -eq 'high-rate') {
    $cases = @(@{ Name = 'vulkan-dynamic-144-no-vsync'; Api = 'vulkan'; Static = 0; VSync = 0; Fps = 144 })
} elseif ($Mode -eq 'refresh-rate') {
    $cases = @(@{ Name = 'vulkan-dynamic-refresh'; Api = 'vulkan'; Static = 0; VSync = 1; Fps = 144 })
}
$rows = @()
foreach ($case in $cases) {
    $log = Join-Path $outputPath ($case.Name + '.log')
    $started = Get-Date
    $arguments = @(
        "-force-$($case.Api)", '-logFile', ('"' + $log + '"'),
        '-probe-width', '854', '-probe-height', '480', '-probe-upload-hz', "$UploadHz",
        '-probe-seconds', "$SecondsPerCase", '-probe-static', "$($case.Static)",
        '-probe-vsync', "$($case.VSync)", '-probe-fps', "$($case.Fps)",
        '-screen-width', '1080', '-screen-height', '1920', '-screen-fullscreen', '1', '-window-mode', 'borderless'
    )
    Write-Output ("CASE START {0} {1:o}" -f $case.Name, $started)
    $previousObsCapture = [Environment]::GetEnvironmentVariable('DISABLE_VULKAN_OBS_CAPTURE', 'Process')
    try {
        if ($DisableObsCapture) {
            [Environment]::SetEnvironmentVariable('DISABLE_VULKAN_OBS_CAPTURE', '1', 'Process')
        }
        $process = Start-Process -FilePath $playerPath -WorkingDirectory (Split-Path -Parent $playerPath) -ArgumentList $arguments -PassThru
    } finally {
        if ($DisableObsCapture) {
            [Environment]::SetEnvironmentVariable('DISABLE_VULKAN_OBS_CAPTURE', $previousObsCapture, 'Process')
        }
    }
    $deadline = $started.AddSeconds($SecondsPerCase + 45)
    $reason = 'exited'
    $events = @()
    $modulesCaptured = $false
    try {
        while (-not $process.HasExited) {
            Start-Sleep -Milliseconds 1000
            if (-not $modulesCaptured -and ((Get-Date) - $started).TotalSeconds -ge 5) {
                try {
                    $process.Modules | Select-Object ModuleName,FileName | Export-Csv -LiteralPath (Join-Path $outputPath ($case.Name + '.modules.csv')) -NoTypeInformation -Encoding utf8
                    $modulesCaptured = $true
                } catch {
                    Write-Warning "Could not capture modules for $($case.Name): $($_.Exception.Message)"
                }
            }
            $events = @(Get-WinEvent -FilterHashtable @{ LogName = 'System'; ProviderName = 'nvlddmkm'; StartTime = $started } -ErrorAction SilentlyContinue)
            if ($events.Count -gt 0) {
                $reason = 'driver-event'
                break
            }
            if ((Get-Date) -gt $deadline) {
                $reason = 'timeout'
                break
            }
            $process.Refresh()
        }
    } finally {
        $process.Refresh()
        if (-not $process.HasExited) {
            # Only terminate the exact child launched by this case.
            Stop-Process -Id $process.Id -Force -ErrorAction SilentlyContinue
            [void]$process.WaitForExit(10000)
        }
    }
    $ended = Get-Date
    $events = @(Get-WinEvent -FilterHashtable @{ LogName = 'System'; ProviderName = 'nvlddmkm'; StartTime = $started; EndTime = $ended } -ErrorAction SilentlyContinue)
    $events | ForEach-Object { $_.ToXml() } | Set-Content -LiteralPath (Join-Path $outputPath ($case.Name + '.events.xml')) -Encoding utf8
    $completed = (Test-Path -LiteralPath $log) -and (Select-String -LiteralPath $log -Pattern '(?:\[TextureProbe\]|RenderProbe) DONE' -Quiet)
    $failed = (Test-Path -LiteralPath $log) -and (Select-String -LiteralPath $log -Pattern '(?:\[TextureProbe\]|RenderProbe) FAIL|VK_ERROR_DEVICE_LOST|Crash!!!' -Quiet)
    $expectedBackend = if ($case.Api -eq 'vulkan') { 'Vulkan' } else { 'Direct3D12' }
    $backendConfirmed = (Test-Path -LiteralPath $log) -and (Select-String -LiteralPath $log -Pattern ("backend=" + $expectedBackend + ';') -Quiet)
    $row = [pscustomobject]@{
        Case = $case.Name; Started = $started.ToString('o'); Ended = $ended.ToString('o')
        Seconds = [math]::Round(($ended - $started).TotalSeconds, 2); Pid = $process.Id
        ExitCode = $process.ExitCode; StopReason = $reason; Completed = $completed; ErrorInLog = $failed
        BackendConfirmed = $backendConfirmed; DisableObsCapture = [bool]$DisableObsCapture
        InheritedObsDisableValue = $previousObsCapture
        DriverEvents = $events.Count; Log = $log; Arguments = ($arguments -join ' ')
    }
    $rows += $row
    $rows | Export-Csv -LiteralPath (Join-Path $outputPath 'matrix.csv') -NoTypeInformation -Encoding utf8
    Write-Output ($row | ConvertTo-Json -Compress)
    if ($events.Count -gt 0) {
        Write-Output 'A driver event occurred. Stopping this matrix to preserve the first failing condition.'
        break
    }
    if (-not $completed -or $failed -or -not $backendConfirmed -or $process.ExitCode -ne 0) {
        Write-Output 'The probe did not complete with the requested backend. Stopping the invalid matrix.'
        break
    }
}
Write-Output "MATRIX COMPLETE $outputPath"
if (@($rows | Where-Object { -not $_.Completed -or $_.ErrorInLog -or -not $_.BackendConfirmed -or $_.DriverEvents -gt 0 -or $_.ExitCode -ne 0 }).Count -gt 0) {
    exit 1
}
