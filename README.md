# Victus Battery Guardian ⚡🛡️

> Dedicated battery telemetry and longevity guardian for HP Victus 15.6 gaming laptops.

[![CI & Release Pipeline](https://github.com/daoistmadness/victus-battery-guardian/actions/workflows/ci-release.yml/badge.svg)](https://github.com/daoistmadness/victus-battery-guardian/actions/workflows/ci-release.yml)
[![Target Platform](https://img.shields.io/badge/Platform-Windows%2011%20x64-blue.svg)](https://microsoft.com/windows)
[![Framework](https://img.shields.io/badge/.NET-10.0%20LTS-purple.svg)](https://dotnet.microsoft.com)
[![UI Framework](https://img.shields.io/badge/UI-Avalonia%2011.2-red.svg)](https://avaloniaui.net)

---

## Overview

**Victus Battery Guardian** is an unprivileged desktop application tailored specifically for HP Victus 15.6 laptops running Windows 11. It delivers precise battery telemetry, lithium health tracking, charge/discharge wattage monitoring, and longevity notifications to prevent unnecessary battery degradation.

> **Current Status (Milestone 1 — Foundation & Bootstrap):**
> The application shell is fully operational and currently operates in an explicitly labeled **Demo Mode** using simulated telemetry (calibrated to the HP Victus 70 Wh battery profile). Physical hardware sensor queries (IOCTL / WMI) will be introduced in Milestone 2.

---

## Architectural Boundaries

The solution strictly isolates platform-independent business logic from OS-specific integrations:

```
VictusBatteryGuardian.slnx
├── src/
│   ├── VictusGuardian.Core/       # Domain models, health algorithms, alert evaluation (Zero OS dependencies)
│   ├── VictusGuardian.Storage/    # SQLite WAL persistence with idle-write throttling
│   ├── VictusGuardian.Windows/    # Windows Win32 API power status & future IOCTL battery hooks
│   └── VictusGuardian.App/        # Avalonia UI desktop application (Navy/Charcoal/Green MVVM shell)
└── tests/
    ├── VictusGuardian.UnitTests/   # Unit tests for Core & Storage (100% Linux/CI compatible)
    └── VictusGuardian.WindowsTests/# Integration tests with platform guards for Windows runners
```

### Key Design Principles:
1. **Telemetry Provenance**: Every metric is wrapped in `TelemetryValue<T>` with explicit provenance (`Reported`, `Estimated`, `Unavailable`, `Simulated`). Simulated data can **never** masquerade as live hardware telemetry.
2. **Idle Disk Protection**: SQLite storage operates in WAL mode with write throttling; identical telemetry states during idle battery usage are suppressed to protect SSD endurance.
3. **No Elevation Required**: The application runs completely unprivileged (`asInvoker` manifest).
4. **Cross-Platform Compilation**: The entire solution—including the Windows platform library and self-contained Windows x64 single-file executable—builds directly on Linux workstations and CI agents.

---

## Development & Build Instructions

### Prerequisites
- [.NET 10.0 SDK](https://dotnet.microsoft.com/download/dotnet/10.0)
- Node.js 22+ & npm 10+ (for release automation)
- PowerShell 7.4+ (`pwsh`)

### Build Solution
```bash
dotnet restore VictusBatteryGuardian.slnx
dotnet build VictusBatteryGuardian.slnx -c Release
```

### Run Tests
```bash
dotnet test VictusBatteryGuardian.slnx -c Release
```

### Publish Self-Contained Windows x64 Binary
You can generate the Windows single-file executable directly from Ubuntu or Windows:
```bash
pwsh -File ./scripts/package.ps1 -Version 0.1.0 -OutputDir artifacts
```

### Verify Draft Release Artifacts
```bash
pwsh -File ./scripts/verify-draft.ps1 -ArtifactsDir artifacts
```

---

## Release Pipeline & Safety Policy

Victus Battery Guardian follows a **draft-first, verified-packslip** release architecture:
1. Feature branches and PRs run CI build and test matrices on both Ubuntu and Windows runners.
2. Merges to `main` evaluate commits using [Conventional Commits](https://www.conventionalcommits.org/).
3. Semantic-release builds the self-contained Windows package, calculates SHA-256 hashes, generates `packslip.json`, and publishes a **draft** release on GitHub.
4. Draft releases undergo packslip verification before final publication.
5. **Release Safety Policy**: No rebuilding occurs between the verified release artifact and publication.

---

## License

MIT License. See [LICENSE](LICENSE) for details.
