[CmdletBinding()]
param (
    [Parameter(Mandatory = $false)]
    [string]$AppPath = ".\VictusGuardian.App.exe",

    [Parameter(Mandatory = $false)]
    [string]$OutputDir = ".\diagnostics-output",

    [Parameter(Mandatory = $false)]
    [switch]$SkipPowerCfg
)

$ErrorActionPreference = "Stop"

Write-Host "================================================================================" -ForegroundColor Cyan
Write-Host " VICTUS BATTERY GUARDIAN - PHYSICAL HARDWARE VALIDATION SUITE" -ForegroundColor Cyan
Write-Host " Target Hardware: HP Victus 15.6 (Windows 11 x64)" -ForegroundColor Cyan
Write-Host " Safety Policy:   Read-only, non-elevated, privacy-preserving (no PII uploaded)" -ForegroundColor Cyan
Write-Host "================================================================================" -ForegroundColor Cyan

if (-not (Test-Path $OutputDir)) {
    New-Item -ItemType Directory -Path $OutputDir -Force | Out-Null
}

$timestamp = [DateTime]::UtcNow.ToString("yyyyMMdd-HHmmss")
$diagnosticJsonPath = Join-Path $OutputDir "diagnostic-$timestamp.json"
$acceptanceLogPath = Join-Path $OutputDir "acceptance-run-$timestamp.md"

# 1. Check OS & Environment
$osVersion = [System.Environment]::OSVersion.VersionString
$isWindows = [System.Environment]::OSVersion.Platform -eq [System.PlatformID]::Win32NT

Write-Host "`n[STEP 1] Environment Check" -ForegroundColor Yellow
Write-Host " Operating System: $osVersion"
Write-Host " Current Directory: $PWD"

# 2. Baseline Powercfg Battery Report (if on Windows)
$powercfgDesignCap = "N/A"
$powercfgFullCap = "N/A"
$powercfgCycleCount = "N/A"

if ($isWindows -and -not $SkipPowerCfg) {
    Write-Host "`n[STEP 2] Generating Official Windows Battery Baseline (powercfg /batteryreport)..." -ForegroundColor Yellow
    $batteryReportPath = Join-Path $OutputDir "battery-report-$timestamp.html"
    try {
        Start-Process -FilePath "powercfg.exe" -ArgumentList "/batteryreport", "/output", "`"$batteryReportPath`"" -NoNewWindow -Wait
        if (Test-Path $batteryReportPath) {
            Write-Host "  -> Battery report saved locally to: $batteryReportPath" -ForegroundColor Green
            $htmlContent = Get-Content -Path $batteryReportPath -Raw

            # Safely extract Design Capacity (e.g. 70,070 mWh)
            if ($htmlContent -match 'DESIGN CAPACITY.*?<span.*?>([\d,]+)\s*mWh') {
                $powercfgDesignCap = "$($matches[1].Replace(',', '')) mWh"
            }
            if ($htmlContent -match 'FULL CHARGE CAPACITY.*?<span.*?>([\d,]+)\s*mWh') {
                $powercfgFullCap = "$($matches[1].Replace(',', '')) mWh"
            }
            if ($htmlContent -match 'CYCLE COUNT.*?<span.*?>([\d,]+)') {
                $powercfgCycleCount = $matches[1].Replace(',', '')
            }

            Write-Host "  -> Baseline Design Capacity:      $powercfgDesignCap"
            Write-Host "  -> Baseline Full Charge Capacity: $powercfgFullCap"
            Write-Host "  -> Baseline Cycle Count:          $powercfgCycleCount"
        }
    }
    catch {
        Write-Warning "Could not run powercfg: $_"
    }
}

# 3. Query Application Diagnostics
Write-Host "`n[STEP 3] Executing Application Hardware Diagnostics..." -ForegroundColor Yellow
if (-not (Test-Path $AppPath)) {
    Write-Error "Application executable not found at: $AppPath"
    return
}

$proc = Start-Process -FilePath $AppPath -ArgumentList "--diagnostics", "--json", "--out", "`"$diagnosticJsonPath`"" -NoNewWindow -Wait -PassThru

if ($proc.ExitCode -ne 0) {
    Write-Error "Application diagnostic runner exited with error code: $($proc.ExitCode)"
    return
}

if (-not (Test-Path $diagnosticJsonPath)) {
    Write-Error "Diagnostic output JSON was not produced at: $diagnosticJsonPath"
    return
}

$diag = Get-Content -Path $diagnosticJsonPath -Raw | ConvertFrom-Json

Write-Host "  -> App Version:      $($diag.Version) (Commit: $($diag.CommitSha))"
Write-Host "  -> Telemetry Tier:   $($diag.ActiveProviderTier)"
Write-Host "  -> Telemetry Source: $($diag.TelemetrySource)"
Write-Host "  -> Demo Mode:        $($diag.IsDemoMode)"

Write-Host "`n--------------------------------------------------------------------------------"
Write-Host " TELEMETRY METRIC COMPARISON (POWERCGF BASELINE vs APPLICATION)"
Write-Host "--------------------------------------------------------------------------------"
Write-Host (" {0,-28} | {1,-18} | {2,-24} | {3,-12}" -f "Metric", "Powercfg Baseline", "Application Reading", "Provenance")
Write-Host "--------------------------------------------------------------------------------"
Write-Host (" {0,-28} | {1,-18} | {2,-24} | {3,-12}" -f "Battery Percentage", "Taskbar Flyout", $diag.Metrics.BatteryPercentage.Value, $diag.Metrics.BatteryPercentage.Provenance)
Write-Host (" {0,-28} | {1,-18} | {2,-24} | {3,-12}" -f "AC Line Status", "AC Indicator", $diag.Metrics.AcPowerStatus.Value, $diag.Metrics.AcPowerStatus.Provenance)
Write-Host (" {0,-28} | {1,-18} | {2,-24} | {3,-12}" -f "Charging State", "LED / Icon", $diag.Metrics.ChargingState.Value, $diag.Metrics.ChargingState.Provenance)
Write-Host (" {0,-28} | {1,-18} | {2,-24} | {3,-12}" -f "Full Charge Capacity", $powercfgFullCap, $diag.Metrics.FullChargeCapacityMwh.Value, $diag.Metrics.FullChargeCapacityMwh.Provenance)
Write-Host (" {0,-28} | {1,-18} | {2,-24} | {3,-12}" -f "Design Capacity", $powercfgDesignCap, $diag.Metrics.DesignCapacityMwh.Value, $diag.Metrics.DesignCapacityMwh.Provenance)
Write-Host (" {0,-28} | {1,-18} | {2,-24} | {3,-12}" -f "Cycle Count", $powercfgCycleCount, $diag.Metrics.CycleCount.Value, $diag.Metrics.CycleCount.Provenance)
Write-Host (" {0,-28} | {1,-18} | {2,-24} | {3,-12}" -f "Charge/Discharge Rate", "N/A (Dynamic)", $diag.Metrics.ChargeRateWatts.Value, $diag.Metrics.ChargeRateWatts.Provenance)
Write-Host (" {0,-28} | {1,-18} | {2,-24} | {3,-12}" -f "Voltage", "N/A (Dynamic)", $diag.Metrics.VoltageVolts.Value, $diag.Metrics.VoltageVolts.Provenance)
Write-Host (" {0,-28} | {1,-18} | {2,-24} | {3,-12}" -f "Battery Name", "Device Manager", $diag.Metrics.BatteryDeviceName.Value, $diag.Metrics.BatteryDeviceName.Provenance)
Write-Host (" {0,-28} | {1,-18} | {2,-24} | {3,-12}" -f "Manufacturer", "HP / OEM", $diag.Metrics.Manufacturer.Value, $diag.Metrics.Manufacturer.Provenance)
Write-Host (" {0,-28} | {1,-18} | {2,-24} | {3,-12}" -f "Chemistry", "Li-ion / ACPI", $diag.Metrics.Chemistry.Value, $diag.Metrics.Chemistry.Provenance)
Write-Host (" {0,-28} | {1,-18} | {2,-24} | {3,-12}" -f "Serial Number", "[Privacy Masked]", $diag.Metrics.SerialNumber.Value, $diag.Metrics.SerialNumber.Provenance)
Write-Host "--------------------------------------------------------------------------------"
Write-Host " Supported Sensors:   $($diag.SensorChannels.Supported -join ', ')"
Write-Host " Unsupported Sensors: $($diag.SensorChannels.Unsupported -join ', ')"
Write-Host "================================================================================"

# 4. Generate Acceptance Log Template
$logTemplate = @"
# Hardware Acceptance Log: HP Victus 15.6

- Date / Time (UTC): $([DateTime]::UtcNow.ToString("o"))
- Host OS: $osVersion
- App Version: $($diag.Version)
- Commit SHA: $($diag.CommitSha)
- Active Telemetry Provider: $($diag.ActiveProviderTier)
- Telemetry Source: $($diag.TelemetrySource)

## Sensor Provenance Verification
- Percentage: $($diag.Metrics.BatteryPercentage.Value) [Provenance: $($diag.Metrics.BatteryPercentage.Provenance)]
- AC Status: $($diag.Metrics.AcPowerStatus.Value) [Provenance: $($diag.Metrics.AcPowerStatus.Provenance)]
- Full Charge Capacity: $($diag.Metrics.FullChargeCapacityMwh.Value) (Baseline: $powercfgFullCap)
- Design Capacity: $($diag.Metrics.DesignCapacityMwh.Value) (Baseline: $powercfgDesignCap)
- Cycle Count: $($diag.Metrics.CycleCount.Value) (Baseline: $powercfgCycleCount)
- Voltage: $($diag.Metrics.VoltageVolts.Value)
- Rate: $($diag.Metrics.ChargeRateWatts.Value)

## Physical Test Scenario Checklist
- [ ] 1. Start on battery power (Discharging state with negative rate displayed)
- [ ] 2. Connect charger (Transitions to Online, positive rate displayed within 5s)
- [ ] 3. Disconnect charger (Transitions to Offline, negative rate displayed within 5s)
- [ ] 4. Compare percentage against Windows taskbar flyout (Within 1%)
- [ ] 5. Compare capacities against powercfg /batteryreport baseline
- [ ] 6. Unknown sensors handled without crashing
- [ ] 7. Windows sleep and resume (App recovers telemetry, no stale warning persists)
- [ ] 8. Application restart (Settings and SQLite history preserved)
- [ ] 9. Application shutdown (Handle cleanup confirmed, no background lingering)
- [ ] 10. Polling verification (5-second polling does not prevent system modern standby)

**Sign-off Status:** PENDING PHYSICAL VERIFICATION BY OPERATOR
"@

$logTemplate | Out-File -FilePath $acceptanceLogPath -Encoding utf8
Write-Host "`n[COMPLETED] Acceptance log template generated at: $acceptanceLogPath" -ForegroundColor Green
Write-Host "All diagnostic data remains strictly local in $OutputDir" -ForegroundColor Green
