# AGENTS.md — Development Guidelines for AI Agents

Welcome to **Victus Battery Guardian**. As an AI agent contributing to this repository, you must adhere strictly to the following rules, architectural constraints, and safety guidelines.

---

## 1. Safety & Git Release Rules

- **Branching Strategy**:
  - Always work in dedicated feature/chore branches (e.g., `feat/milestone-2-hardware-telemetry`, `chore/foundation-bootstrap`).
  - **NEVER** push directly to `main`.
  - **NEVER** push tags or trigger production releases without explicit user authorization.
  - Follow [Conventional Commits](https://www.conventionalcommits.org/) format (e.g., `feat:`, `fix:`, `refactor:`, `test:`, `docs:`, `chore:`).
- **Environment Boundaries**:
  - The Linux server (`OPRSERVER`) is a development workstation, not a production application server.
  - Do not install system-wide permanent services, alter global daemon configurations, or introduce external server dependencies.

---

## 2. Solution Architecture & Assembly Separation

The solution is divided into 4 main projects and 2 test projects:

| Project | Target | Permitted Dependencies | Forbidden Dependencies |
| :--- | :--- | :--- | :--- |
| `VictusGuardian.Core` | `net10.0` | None (pure C# / .NET BCL abstractions) | **NO** Windows APIs, **NO** SQLite, **NO** Avalonia UI |
| `VictusGuardian.Storage` | `net10.0` | `VictusGuardian.Core`, `Microsoft.Data.Sqlite` | **NO** UI, **NO** Windows-specific APIs |
| `VictusGuardian.Windows` | `net10.0` | `VictusGuardian.Core`, Win32 P/Invoke | Must compile on Linux with `[SupportedOSPlatform("windows")]` |
| `VictusGuardian.App` | `net10.0` (WinExe) | Core, Storage, Windows, Avalonia UI, CommunityToolkit.Mvvm | Business logic belongs in Core/Storage |

---

## 3. Telemetry & Hardware Rules

- **Strict Provenance**:
  - Every sensor value is wrapped in `TelemetryValue<T>` with an explicit `TelemetrySource` (`Reported`, `Estimated`, `Unavailable`, `Simulated`).
  - **NEVER** present simulated telemetry as real hardware data.
  - When in Demo Mode, the UI must prominently display "DEMO MODE ACTIVE" with simulated badges.
- **Unprivileged Execution**:
  - The application must execute under standard user credentials (`asInvoker` manifest).
  - Never request administrative elevation unless explicitly approved for an optional low-level driver probe.
- **Hardware Safety**:
  - This application is a telemetry monitor and longevity advisor.
  - **DO NOT** attempt direct EC (Embedded Controller) flashing, ACPI table rewriting, or dangerous charging control overrides that could damage hardware.

---

## 4. Performance & Persistence Guidelines

- **SQLite WAL Mode**:
  - Use SQLite with Write-Ahead Logging (`PRAGMA journal_mode = WAL; PRAGMA synchronous = NORMAL;`).
- **Idle Write Throttling**:
  - Background telemetry polling runs every few seconds, but disk writes must be throttled.
  - Suppress database inserts during idle periods if battery percentage and state remain unchanged.

---

## 5. Testing & Verification

- Every new domain feature must include unit tests in `VictusGuardian.UnitTests`.
- Windows-specific tests in `VictusGuardian.WindowsTests` must be guarded with `OperatingSystem.IsWindows()` or custom test attributes so they skip or pass cleanly on Linux CI.
- Verify that Windows x64 self-contained packaging succeeds via `pwsh -File ./scripts/package.ps1`.
