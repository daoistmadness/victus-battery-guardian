<#
.SYNOPSIS
    Safely audits and cleans up stale .NET single-file native extraction bundles for Victus Battery Guardian.

.DESCRIPTION
    In .NET single-file self-contained deployments with native libraries, the runtime extracts native
    dependencies into '%TEMP%\.net\VictusGuardian.App\<bundleId>'. Each unique executable build (SHA)
    receives a distinct bundle directory. While consecutive runs of the exact same executable reuse the
    bundle without re-extracting, running different builds (e.g. following updates or tests) leaves
    older extracted directories behind.

    This script conservatively audits and safely removes stale extraction directories that are no longer
    in use by any running Victus Guardian process.

.PARAMETER DryRun
    When specified (default: true), audits directories and calculates reclaimable space without modifying disk.

.PARAMETER Force
    Executes actual deletion when combined with -DryRun:$false.

.PARAMETER MinAgeDays
    Minimum age in days for a bundle to be considered eligible for cleanup. Default: 0 (all inactive bundles).

.EXAMPLE
    .\clean-extracted-bundles.ps1
    # Dry-run audit: displays all extracted bundles and total space consumed.

.EXAMPLE
    .\clean-extracted-bundles.ps1 -DryRun:$false -Force
    # Safely deletes inactive extracted bundles not held by any running VictusGuardian.App process.
#>
[CmdletBinding(SupportsShouldProcess = $true)]
param (
    [Parameter(Mandatory = $false)]
    [bool]$DryRun = $true,

    [Parameter(Mandatory = $false)]
    [switch]$Force,

    [Parameter(Mandatory = $false)]
    [int]$MinAgeDays = 0
)

$ErrorActionPreference = "Stop"

Write-Host "=================================================="
Write-Host "Victus Battery Guardian - Bundle Extraction Cleaner"
Write-Host "DryRun:       $DryRun"
Write-Host "Force:        $($Force.IsPresent)"
Write-Host "MinAgeDays:   $MinAgeDays"
Write-Host "=================================================="

$tempPath = [System.IO.Path]::GetTempPath()
$targetBase = Join-Path $tempPath ".net"
$appBundleDir = Join-Path $targetBase "VictusGuardian.App"

if (-not (Test-Path $appBundleDir)) {
    Write-Host "No extracted bundles found at: $appBundleDir"
    Write-Host "Storage is clean. 0 bytes consumed."
    return
}

# Ensure directory is not a reparse point (symlink/junction) for security
$dirItem = Get-Item $appBundleDir
if ($dirItem.Attributes -band [System.IO.FileAttributes]::ReparsePoint) {
    throw "Target path '$appBundleDir' is a reparse point (symlink/junction). Aborting for safety."
}

# Check if any VictusGuardian.App process is actively running
$runningProcesses = Get-Process -Name "VictusGuardian.App" -ErrorAction SilentlyContinue
$isRunning = ($runningProcesses -ne $null -and $runningProcesses.Count -gt 0)
if ($isRunning) {
    Write-Warning "Detected $($runningProcesses.Count) active VictusGuardian.App process(es) (PIDs: $(($runningProcesses | Select-Object -ExpandProperty Id) -join ', '))."
    Write-Warning "Active bundles will be strictly skipped to prevent crashing running processes."
}

$bundles = Get-ChildItem -Path $appBundleDir -Directory
if ($bundles.Count -eq 0) {
    Write-Host "No bundle subdirectories present in: $appBundleDir"
    return
}

Write-Host "Found $($bundles.Count) bundle director(y/ies) in '$appBundleDir':"
$cutoffDate = (Get-Date).AddDays(-$MinAgeDays)
$reclaimableBytes = 0
$eligibleCount = 0
$cleanedCount = 0

foreach ($bundle in $bundles) {
    # Skip reparse points inside bundle directory
    if ($bundle.Attributes -band [System.IO.FileAttributes]::ReparsePoint) {
        Write-Warning "Skipping bundle '$($bundle.Name)' because it is a reparse point."
        continue
    }

    # Calculate size
    $files = Get-ChildItem -Path $bundle.FullName -Recurse -File -ErrorAction SilentlyContinue
    $bundleBytes = ($files | Measure-Object -Property Length -Sum).Sum
    if (-not $bundleBytes) { $bundleBytes = 0 }
    $bundleMb = [Math]::Round($bundleBytes / 1MB, 2)

    # Age check
    if ($bundle.LastWriteTime -gt $cutoffDate) {
        Write-Host " - [RETAIN] $($bundle.Name) | $bundleMb MB | Last Modified: $($bundle.LastWriteTime) (Newer than $MinAgeDays days)"
        continue
    }

    # Lock / active check by probing file access
    $isLocked = $false
    foreach ($file in $files) {
        try {
            $stream = [System.IO.File]::Open($file.FullName, [System.IO.FileMode]::Open, [System.IO.FileAccess]::ReadWrite, [System.IO.FileShare]::None)
            $stream.Close()
            $stream.Dispose()
        } catch {
            $isLocked = $true
            break
        }
    }

    if ($isLocked) {
        Write-Host " - [LOCKED] $($bundle.Name) | $bundleMb MB | Currently locked by active process. Skipping." -ForegroundColor Yellow
        continue
    }

    $eligibleCount++
    $reclaimableBytes += $bundleBytes

    if ($DryRun -or (-not $Force.IsPresent)) {
        Write-Host " - [ELIGIBLE] $($bundle.Name) | $bundleMb MB | Last Modified: $($bundle.LastWriteTime) (Dry-Run: would be deleted)" -ForegroundColor Cyan
    } else {
        try {
            Write-Host " - [DELETING] $($bundle.Name) | $bundleMb MB..." -NoNewline
            Remove-Item -Path $bundle.FullName -Recurse -Force
            Write-Host " Done." -ForegroundColor Green
            $cleanedCount++
        } catch {
            Write-Host " FAILED: $($_.Exception.Message)" -ForegroundColor Red
        }
    }
}

$reclaimableMb = [Math]::Round($reclaimableBytes / 1MB, 2)
Write-Host "=================================================="
if ($DryRun -or (-not $Force.IsPresent)) {
    Write-Host "Dry-Run Summary:"
    Write-Host "  Eligible bundle directories: $eligibleCount of $($bundles.Count)"
    Write-Host "  Reclaimable storage:         $reclaimableMb MB"
    Write-Host "  To perform actual cleanup, execute with: -DryRun:`$false -Force"
} else {
    Write-Host "Cleanup Summary:"
    Write-Host "  Removed bundle directories:  $cleanedCount of $eligibleCount"
    Write-Host "  Reclaimed storage:           $reclaimableMb MB"
}
Write-Host "=================================================="
