# Victus Battery Guardian ⚡🛡️

> Dedicated battery telemetry and longevity guardian for HP Victus 15.6 gaming laptops.

[![CI & Release Pipeline](https://github.com/daoistmadness/victus-battery-guardian/actions/workflows/ci-release.yml/badge.svg)](https://github.com/daoistmadness/victus-battery-guardian/actions/workflows/ci-release.yml)
[![Target Platform](https://img.shields.io/badge/Platform-Windows%2011%20x64-blue.svg)](https://microsoft.com/windows)
[![Framework](https://img.shields.io/badge/.NET-10.0%20LTS-purple.svg)](https://dotnet.microsoft.com)
[![UI Framework](https://img.shields.io/badge/UI-Avalonia%2011.2-red.svg)](https://avaloniaui.net)

---

## Overview

**Victus Battery Guardian** is an unprivileged desktop application tailored specifically for HP Victus 15.6 laptops running Windows 11. It delivers precise battery telemetry, lithium health tracking, charge/discharge wattage monitoring, and longevity notifications to prevent unnecessary battery degradation.

> **Current Status (Milestone 2 — Windows Telemetry & CI Readiness):**
> Real, read-only Windows battery telemetry is implemented via direct device interface queries (`SetupAPI` enumeration + `IOCTL_BATTERY_QUERY_*`).
> The application uses a multi-tier composite telemetry architecture with safe fallback to `GetSystemPowerStatus`, explicit `Demo Mode` controls, unmanaged handle lifecycle management, and sleep/resume resilience.
> Release readiness is established with explicit unsigned packslip declarations and production publish gates.

---

## Architectural Boundaries

The solution strictly isolates platform-independent business logic from OS-specific integrations:

```
VictusBatteryGuardian.slnx
├── src/
│   ├── VictusGuardian.Core/       # Domain models, provenance types, health algorithms, alert evaluation
│   ├── VictusGuardian.Storage/    # SQLite WAL persistence with v1 -> v2 schema migrations & idle write throttling
│   ├── VictusGuardian.Windows/    # SetupAPI enumeration, IOCTL direct battery interface, Win32 fallback, power events
│   └── VictusGuardian.App/        # Avalonia UI desktop application (Navy/Charcoal/Green MVVM shell)
├── tests/
│   ├── VictusGuardian.UnitTests/   # Unit tests with INativeBatteryDeviceApi mocks (100% Linux/CI compatible)
│   └── VictusGuardian.WindowsTests/# Integration tests with skippable physical target guards
└── docs/
    ├── HARDWARE_VALIDATION.md     # Reproducible physical HP Victus validation checklist
    └── RELEASE_AND_PACKSLIP.md    # Packslip manifest and Sigstore keyless signing architecture
```

### Telemetry Provider Hierarchy:
1. **Tier 1 (Preferred):** Direct Device IOCTL (`IOCTL_BATTERY_QUERY_TAG`, `IOCTL_BATTERY_QUERY_INFORMATION`, `IOCTL_BATTERY_QUERY_STATUS`). Delivers remaining capacity (mWh), full charge capacity (mWh), factory design capacity (mWh), instantaneous voltage (mV), signed charge/discharge rate (mW), cycle count, and device identities.
2. **Tier 2 (Fallback):** Win32 `GetSystemPowerStatus`. Provides AC line state, charging flag, and battery life percentage if IOCTL device queries fail.
3. **Tier 3 (Terminal):** Explicit `Telemetry Unavailable` state. The application **NEVER** silently falls back to simulated data.
4. **Demo Mode:** Available **only** when explicitly enabled by the user in Settings for evaluation and UI testing.

---

## Key Design & Safety Principles

1. **Strict Provenance Guarantees**: Every metric is wrapped in `TelemetryValue<T>` with explicit provenance (`Reported`, `Estimated`, `Unavailable`, `Simulated`).
2. **No Elevation Required**: Runs entirely in unprivileged user space (`asInvoker`).
3. **Battery Replacement & Sleep Resilience**: Battery tags are monitored for changes (indicating battery replacement). Windows suspend/resume events safely clear caches and refresh hardware handles.
4. **Transparent Packaging**: The Packslip manifest explicitly records `isSigned: false` to accurately represent checksum-only verification pending Sigstore OIDC keyless signing.
5. **Cross-Platform Compilation**: The entire solution—including Windows native P/Invoke libraries and self-contained win-x64 single-file packages—builds cleanly on Linux development workstations.

---

## Development & Build Instructions

### Prerequisites
- [.NET 10.0 SDK](https://dotnet.microsoft.com/download/dotnet/10.0)
- Node.js 22+ & npm 10+ (for release automation)
- PowerShell 7.4+ (`pwsh`)

### Build Solution
```bash
dotnet restore VictusBatteryGuardian.slnx
dotnet build VictusBatteryGuardian.slnx -c Release --warnaserror
```

### Run Tests
```bash
dotnet test VictusBatteryGuardian.slnx -c Release
```

### Publish Self-Contained Windows x64 Binary
```bash
pwsh -File ./scripts/package.ps1 -Version 0.2.0-beta -OutputDir artifacts
```

### Verify Draft Release Artifacts
```bash
pwsh -File ./scripts/verify-draft.ps1 -ArtifactsDir artifacts
```

---

## Physical Hardware Validation

Before claiming physical hardware validation, follow the protocol documented in [`docs/HARDWARE_VALIDATION.md`](docs/HARDWARE_VALIDATION.md) on an actual HP Victus 15.6 laptop running Windows 11.
