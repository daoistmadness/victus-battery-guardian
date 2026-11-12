[CmdletBinding()]
param (
    [Parameter(Mandatory = $false)]
    [string]$ProcessName = "VictusGuardian.App",

    [Parameter(Mandatory = $false)]
    [int]$DurationSeconds = 60,

    [Parameter(Mandatory = $false)]
    [int]$IntervalSeconds = 5,

    [Parameter(Mandatory = $false)]
    [string]$OutputDir = "./benchmark-output"
)

$ErrorActionPreference = "Stop"

Write-Host "================================================================================" -ForegroundColor Cyan
Write-Host " VICTUS BATTERY GUARDIAN - RESOURCE & MEMORY PROFILING SUITE" -ForegroundColor Cyan
Write-Host " Target Hardware: HP Victus 15.6 (Windows 11 x64)" -ForegroundColor Cyan
Write-Host " Measurement: Working Set, Private Commit, Handles, Threads, Background CPU" -ForegroundColor Cyan
Write-Host "================================================================================" -ForegroundColor Cyan

if (-not (Test-Path $OutputDir)) {
    New-Item -ItemType Directory -Path $OutputDir -Force | Out-Null
}

$proc = Get-Process -Name $ProcessName -ErrorAction SilentlyContinue
if (-not $proc) {
    Write-Warning "Process "$ProcessName" not found running. Please launch Victus Battery Guardian first."
    exit 1
}

Write-Host "Monitoring Process: $($proc.ProcessName) (PID: $($proc.Id))" -ForegroundColor Green
Write-Host "Duration:           $DurationSeconds seconds"
Write-Host "Sampling Interval:  $IntervalSeconds seconds"
Write-Host "Sampling start..." -ForegroundColor Yellow

$samples = [System.Collections.Generic.List[PSObject]]::new()
$startTime = [DateTime]::UtcNow
$endTime = $startTime.AddSeconds($DurationSeconds)

while ([DateTime]::UtcNow -lt $endTime) {
    $currentProc = Get-Process -Id $proc.Id -ErrorAction SilentlyContinue
    if (-not $currentProc) {
        Write-Warning "Process terminated during benchmark run."
        break
    }

    $sample = [PSCustomObject]@{
        TimestampUtc         = [DateTime]::UtcNow.ToString("o")
        WorkingSetMB         = [Math]::Round($currentProc.WorkingSet64 / 1MB, 2)
        PrivateMemoryMB      = [Math]::Round($currentProc.PrivateMemorySize64 / 1MB, 2)
        PagedMemoryMB        = [Math]::Round($currentProc.PagedMemorySize64 / 1MB, 2)
        PeakWorkingSetMB     = [Math]::Round($currentProc.PeakWorkingSet64 / 1MB, 2)
        HandleCount          = $currentProc.HandleCount
        ThreadCount          = $currentProc.Threads.Count
        CpuTotalSeconds      = [Math]::Round($currentProc.TotalProcessorTime.TotalSeconds, 3)
    }

    $samples.Add($sample)
    Write-Host "[$($sample.TimestampUtc)] WS: $($sample.WorkingSetMB) MB | Private: $($sample.PrivateMemoryMB) MB | Handles: $($sample.HandleCount) | Threads: $($sample.ThreadCount)"

    Start-Sleep -Seconds $IntervalSeconds
}

$timestamp = [DateTime]::UtcNow.ToString("yyyyMMdd-HHmmss")
$csvPath = Join-Path $OutputDir "memory-profile-$timestamp.csv"
$samples | Export-Csv -Path $csvPath -NoTypeInformation

$minWs = ($samples | Measure-Object -Property WorkingSetMB -Minimum).Minimum
$maxWs = ($samples | Measure-Object -Property WorkingSetMB -Maximum).Maximum
$avgWs = [Math]::Round(($samples | Measure-Object -Property WorkingSetMB -Average).Average, 2)

$minPriv = ($samples | Measure-Object -Property PrivateMemoryMB -Minimum).Minimum
$maxPriv = ($samples | Measure-Object -Property PrivateMemoryMB -Maximum).Maximum
$avgPriv = [Math]::Round(($samples | Measure-Object -Property PrivateMemoryMB -Average).Average, 2)

$reportPath = Join-Path $OutputDir "memory-report-$timestamp.md"
$report = @"
# Victus Battery Guardian — Memory & Resource Profile

- **Date (UTC):** $([DateTime]::UtcNow.ToString("yyyy-MM-dd HH:mm:ss"))
- **PID:** $($proc.Id)
- **Duration:** $DurationSeconds seconds
- **Samples Collected:** $($samples.Count)

## Memory Summary

| Metric | Minimum | Maximum | Average |
|---|---|---|---|
| Working Set (Physical RAM) | $minWs MB | $maxWs MB | $avgWs MB |
| Private Bytes (Committed Memory) | $minPriv MB | $maxPriv MB | $avgPriv MB |

## Raw CSV Data
Saved to: `$csvPath`
"@

Set-Content -Path $reportPath -Value $report -Encoding utf8

Write-Host "`nProfile Complete!" -ForegroundColor Green
Write-Host "CSV Log: $csvPath"
Write-Host "Summary: $reportPath"
Write-Host "Average Working Set: $avgWs MB | Average Private Commit: $avgPriv MB" -ForegroundColor Cyan
