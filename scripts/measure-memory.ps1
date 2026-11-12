<#
.SYNOPSIS
    Profiles memory, CPU, and disk I/O metrics for Victus Battery Guardian on Windows.

.DESCRIPTION
    Measures physical Working Set (WS), Private Commit (Private Bytes), Handle count, Thread count,
    CPU utilization, and Disk I/O operations/transfers over a specified duration.
    Supports matrix comparison across Hardware vs Software rendering and Self-Contained vs
    Framework-Dependent variants.

.PARAMETER ProcessName
    Name of the process to monitor (default: VictusGuardian.App).

.PARAMETER DurationSeconds
    Duration of sampling in seconds (default: 60).

.PARAMETER IntervalSeconds
    Interval between samples in seconds (default: 5).

.PARAMETER Label
    A descriptive label for this benchmark run (e.g. "Hardware-Default", "Software-Angle-Disabled", "FrameworkDependent").

.PARAMETER OutputDir
    Directory to save CSV logs and markdown reports (default: ./benchmark-output).

.EXAMPLE
    .\measure-memory.ps1 -Label "Hardware-Default" -DurationSeconds 120
    # Samples running process for 2 minutes with hardware rendering.

.EXAMPLE
    .\measure-memory.ps1 -Label "Software-Rendering" -DurationSeconds 120
    # Samples running process launched with --software-rendering.
#>
[CmdletBinding()]
param (
    [Parameter(Mandatory = $false)]
    [string]$ProcessName = "VictusGuardian.App",

    [Parameter(Mandatory = $false)]
    [int]$DurationSeconds = 60,

    [Parameter(Mandatory = $false)]
    [int]$IntervalSeconds = 5,

    [Parameter(Mandatory = $false)]
    [string]$Label = "Hardware-Default",

    [Parameter(Mandatory = $false)]
    [string]$OutputDir = "./benchmark-output"
)

$ErrorActionPreference = "Stop"

Write-Host "================================================================================" -ForegroundColor Cyan
Write-Host " VICTUS BATTERY GUARDIAN - RESOURCE & MEMORY PROFILING SUITE" -ForegroundColor Cyan
Write-Host " Target Hardware: HP Victus 15.6 (Windows 11 x64)" -ForegroundColor Cyan
Write-Host " Variant / Label: $Label" -ForegroundColor Cyan
Write-Host " Sampling:        Working Set, Private Bytes, I/O Operations, CPU, Threads" -ForegroundColor Cyan
Write-Host "================================================================================" -ForegroundColor Cyan

if (-not (Test-Path $OutputDir)) {
    New-Item -ItemType Directory -Path $OutputDir -Force | Out-Null
}

$proc = Get-Process -Name $ProcessName -ErrorAction SilentlyContinue
if (-not $proc) {
    Write-Warning "Process '$ProcessName' not found running. Please launch Victus Battery Guardian first."
    exit 1
}

Write-Host "Monitoring Process: $($proc.ProcessName) (PID: $($proc.Id))" -ForegroundColor Green
Write-Host "Duration:           $DurationSeconds seconds"
Write-Host "Sampling Interval:  $IntervalSeconds seconds"
Write-Host "Sampling start..." -ForegroundColor Yellow

$samples = [System.Collections.Generic.List[PSObject]]::new()
$startTime = [DateTime]::UtcNow
$endTime = $startTime.AddSeconds($DurationSeconds)

$initialCpu = $proc.TotalProcessorTime.TotalSeconds
$initialTime = [DateTime]::UtcNow

while ([DateTime]::UtcNow -lt $endTime) {
    $currentProc = Get-Process -Id $proc.Id -ErrorAction SilentlyContinue
    if (-not $currentProc) {
        Write-Warning "Process terminated during benchmark run."
        break
    }

    # Query Win32_Process for I/O counters
    $ioData = $null
    try {
        $ioData = Get-CimInstance Win32_Process -Filter "ProcessId = $($proc.Id)" -ErrorAction SilentlyContinue
    } catch {
        # Non-fatal if CIM unavailable
    }

    $now = [DateTime]::UtcNow
    $cpuSec = [Math]::Round($currentProc.TotalProcessorTime.TotalSeconds, 3)

    $sample = [PSCustomObject]@{
        TimestampUtc         = $now.ToString("o")
        Label                = $Label
        PID                  = $currentProc.Id
        WorkingSetMB         = [Math]::Round($currentProc.WorkingSet64 / 1MB, 2)
        PrivateMemoryMB      = [Math]::Round($currentProc.PrivateMemorySize64 / 1MB, 2)
        PagedMemoryMB        = [Math]::Round($currentProc.PagedMemorySize64 / 1MB, 2)
        PeakWorkingSetMB     = [Math]::Round($currentProc.PeakWorkingSet64 / 1MB, 2)
        HandleCount          = $currentProc.HandleCount
        ThreadCount          = $currentProc.Threads.Count
        CpuTotalSeconds      = $cpuSec
        ReadOpCount          = if ($ioData) { [int64]$ioData.ReadOperationCount } else { 0 }
        WriteOpCount         = if ($ioData) { [int64]$ioData.WriteOperationCount } else { 0 }
        ReadTransferKB       = if ($ioData) { [Math]::Round($ioData.ReadTransferCount / 1KB, 2) } else { 0 }
        WriteTransferKB      = if ($ioData) { [Math]::Round($ioData.WriteTransferCount / 1KB, 2) } else { 0 }
    }

    $samples.Add($sample)
    Write-Host "[$($sample.TimestampUtc)] WS: $($sample.WorkingSetMB) MB | Priv: $($sample.PrivateMemoryMB) MB | Writes: $($sample.WriteOpCount) ($($sample.WriteTransferKB) KB) | Handles: $($sample.HandleCount) | Threads: $($sample.ThreadCount)"

    Start-Sleep -Seconds $IntervalSeconds
}

$timestamp = [DateTime]::UtcNow.ToString("yyyyMMdd-HHmmss")
$safeLabel = $Label -replace '[^a-zA-Z0-9_\-]', '_'
$csvPath = Join-Path $OutputDir "memory-profile-$safeLabel-$timestamp.csv"
$samples | Export-Csv -Path $csvPath -NoTypeInformation

$minWs = ($samples | Measure-Object -Property WorkingSetMB -Minimum).Minimum
$maxWs = ($samples | Measure-Object -Property WorkingSetMB -Maximum).Maximum
$avgWs = [Math]::Round(($samples | Measure-Object -Property WorkingSetMB -Average).Average, 2)

$minPriv = ($samples | Measure-Object -Property PrivateMemoryMB -Minimum).Minimum
$maxPriv = ($samples | Measure-Object -Property PrivateMemoryMB -Maximum).Maximum
$avgPriv = [Math]::Round(($samples | Measure-Object -Property PrivateMemoryMB -Average).Average, 2)

$avgHandles = [Math]::Round(($samples | Measure-Object -Property HandleCount -Average).Average, 0)
$avgThreads = [Math]::Round(($samples | Measure-Object -Property ThreadCount -Average).Average, 0)

$deltaWrites = 0
$deltaWriteKB = 0
if ($samples.Count -ge 2) {
    $first = $samples[0]
    $last = $samples[-1]
    $deltaWrites = $last.WriteOpCount - $first.WriteOpCount
    $deltaWriteKB = [Math]::Round($last.WriteTransferKB - $first.WriteTransferKB, 2)
}

$reportPath = Join-Path $OutputDir "memory-report-$safeLabel-$timestamp.md"
$report = @"
# Victus Battery Guardian — Resource & Memory Profile

- **Label / Configuration:** $Label
- **Date (UTC):** $([DateTime]::UtcNow.ToString("yyyy-MM-dd HH:mm:ss"))
- **PID:** $($proc.Id)
- **Duration:** $DurationSeconds seconds ($($samples.Count) samples at ${IntervalSeconds}s interval)

## Summary Metrics

| Metric | Minimum | Maximum | Average / Total |
|---|---|---|---|
| **Working Set (Physical RAM)** | $minWs MB | $maxWs MB | **$avgWs MB** |
| **Private Bytes (Committed Memory)** | $minPriv MB | $maxPriv MB | **$avgPriv MB** |
| **Average Handles** | - | - | $avgHandles |
| **Average Threads** | - | - | $avgThreads |
| **Disk Write Operations Delta** | - | - | $deltaWrites ops ($deltaWriteKB KB) |

## Raw CSV Data
Saved to: `$csvPath`
"@

Set-Content -Path $reportPath -Value $report -Encoding utf8

Write-Host "`nProfile Complete!" -ForegroundColor Green
Write-Host "CSV Log: $csvPath"
Write-Host "Summary: $reportPath"
Write-Host "Average Working Set: $avgWs MB | Average Private Commit: $avgPriv MB | Writes Delta: $deltaWrites ops" -ForegroundColor Cyan
