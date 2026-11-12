# Victus Battery Guardian — Memory, Rendering & Storage Architecture

**Milestone 4.6 Technical Documentation**  
*Target Environment: HP Victus 15.6 Gaming Laptop (Windows 11 x64, .NET 10 LTS)*

---

## 1. Executive Summary & Observed Baseline Metrics

Physical device profiling of Victus Battery Guardian (v0.2.0, commit `85239f9`) revealed the following baseline resource footprint:

- **Working Set (Total):** ~231 MB (112 MB Private Working Set + 119 MB Shared Working Set)
- **Commit Size (Private Bytes):** ~174 MB
- **Threads:** 38–40 active threads
- **Handles:** ~677 handles
- **Loaded Modules:** 77 dynamic link libraries
- **Disk I/O (12 min runtime):** ~16.68 MB written across 4,317 write operations (5-second polling tick)
- **Extracted Bundles:** 4 directories totaling ~66 MB in `%TEMP%\.net\VictusGuardian.App\` (~16.6 MB per bundle)

Analysis demonstrated that the apparent "memory bloat" in Task Manager consists of three distinct components:
1. **Shared Graphics Working Set (~119 MB):** Avalonia's Direct3D/ANGLE GPU pipeline allocates swapchains and shared driver surfaces mapped to physical RAM.
2. **Extracted Bundles in `%TEMP%`:** .NET single-file native library extraction creates bundle directories per unique executable build.
3. **SQLite WAL Write Volume:** Every 5-second poll committed a new database row and checkpointed the SQLite Write-Ahead Log.

---

## 2. Hardware Acceleration vs. Software Rendering

### 2.1 Avalonia Win32 Platform Options Architecture
Victus Battery Guardian implements dual-backend rendering configuration via `Win32PlatformOptions`:

- **Software Rendering (Production Default):**
  - Rendering Mode: `[Win32RenderingMode.Software]`
  - Composition Mode: `[Win32CompositionMode.RedirectionSurface]`
  - Physical Benchmark Results on HP Victus 15.6:
    - Working Set: **55.9 MB to 75.7 MB** (down from ~231 MB baseline, ~70% reduction)
    - CPU Utilization: **0.3% to 0.4%** (negligible background overhead, indistinguishable from hardware idle)
    - Fresh Launch: ~100 MB settling to ~55–75 MB
    - Tray State (Windows Efficiency Mode): ~35–50 MB, < 0.1% CPU
  - Advantages: Drastically reduces working set, eliminates AMD Radeon user-mode driver surface allocations, eliminates Direct3D/ANGLE swapchain overhead, and allows Windows 11 EcoQoS (Efficiency Mode) to cleanly suspend paint activity in the notification tray.
- **Hardware Accelerated (Explicit Compatibility / Diagnostic Mode):**
  - Rendering Mode: `[Win32RenderingMode.AngleEgl, Win32RenderingMode.Software]`
  - Composition Mode: `[Win32CompositionMode.WinUIComposition, Win32CompositionMode.DirectComposition, Win32CompositionMode.RedirectionSurface]`
  - Launch arguments: `--hardware-rendering`, `--hardware`, `--render-mode=hardware`, `--render-mode hardware`
  - Environment variables: `VICTUS_RENDER_MODE=hardware` or `VICTUS_HARDWARE_RENDERING=1`
  - Advantages: Direct3D 11 / ANGLE EGL acceleration for systems where hardware compositing is explicitly preferred.
  - Tradeoff: ~100–120 MB shared driver working set allocated in physical RAM.

### 2.2 Architectural Decision & Justification
**Software Rendering is the default Windows renderer starting in Milestone 4.7.**
Physical device benchmarking on the HP Victus 15.6 disproved the hypothesis that software rendering would cause battery-draining CPU loops. Instead, Avalonia's software rasterizer on Windows GDI RedirectionSurface uses only 0.3%–0.4% CPU while cutting working set by over 150 MB. For a battery monitoring utility designed to live in the system tray for days, this represents a substantial improvement in resource efficiency. Hardware rendering is fully preserved via `--hardware-rendering` for compatibility.

---

## 3. Single-File Extraction & Temp Storage Mechanics

### 3.1 Why Extracted Directories Exist
In `.NET 10` single-file self-contained packaging (`PublishSingleFile=true`, `IncludeNativeLibrariesForSelfExtract=true`), native C++ libraries (such as Avalonia's `libSkiaSharp.dll`, ANGLE's `libEGL.dll`, `libGLESv2.dll`, and `e_sqlite3.dll`) cannot be loaded directly from memory via standard Windows `LoadLibraryExW`.

The .NET runtime hosts an internal bundle extraction engine that extracts native dependencies into:
```
%TEMP%\.net\VictusGuardian.App\<bundleId>\
```
where `<bundleId>` is a 12-character cryptographic hash derived from the executable contents.

### 3.2 Key Operational Facts
- **Reuse on Identical Builds:** When running the exact same binary multiple times, the .NET runtime detects that `<bundleId>` already exists and **reuses** the extracted files without re-extracting.
- **New Directory on Recompilation / Updates:** Whenever a new version or rebuild is executed, the binary SHA changes, generating a new `<bundleId>`. The runtime intentionally leaves old bundles behind to prevent crashing any instances of older versions that might still be running.
- **Not an Application Memory Leak:** The files in `%TEMP%\.net\` represent temporary disk storage, not active RAM usage.

### 3.3 Safe Cleanup Script
A conservative cleanup utility is provided at [`scripts/clean-extracted-bundles.ps1`](file:///home/mikhailryu/code/repos/victusguardian/scripts/clean-extracted-bundles.ps1):
- **Dry-Run by Default:** Runs safely without modifying files unless explicitly invoked with `-DryRun:$false -Force`.
- **Active Process Protection:** Detects running `VictusGuardian.App` processes and verifies file lock handles before deleting any bundle.
- **Symlink / Reparse Point Safety:** Validates directory attributes to prevent junction traversal.

```powershell
# Audit stale bundles (dry-run)
pwsh scripts/clean-extracted-bundles.ps1

# Perform cleanup
pwsh scripts/clean-extracted-bundles.ps1 -DryRun:$false -Force
```

---

## 4. Distribution Strategy: Self-Contained vs. Framework-Dependent

| Metric / Feature | Self-Contained (Default) | Framework-Dependent (Alternative) |
|---|---|---|
| **Distribution Size (ZIP)** | ~46.8 MB | ~14.2 MB |
| **Prerequisites** | None (Zero install) | .NET 10 Desktop Runtime required |
| **Extraction Overhead** | Native libraries extracted to `%TEMP%` | Direct execution from runtime path |
| **Memory Footprint** | Baseline | Marginally lower initial working set |
| **Target Audience** | General end-users | Developers / Systems with .NET 10 LTS |

**Decision:** Retain **Self-Contained** as the default production distribution artifact. Packaging script support for `-PublishMode FrameworkDependent` and `-PublishMode Both` enables automated side-by-side benchmarking.

---

## 5. SQLite Polling & Write Suppression Architecture

### 5.1 Problem Statement
In previous releases, every 5-second polling tick wrote a reading to SQLite, producing ~4,317 write operations (~16.7 MB written to SSD) over a 12-minute session.

### 5.2 Optimizations Implemented
1. **Dynamic Write Throttling (`SqliteBatteryHistoryRepository.cs`):**
   - Telemetry readings are suppressed unless a meaningful event occurs:
     - Battery percentage changes by $\ge 0.5\%$.
     - AC power connection state toggles (`Online` vs `Offline`).
     - Charging state changes (`Discharging`, `Charging`, `Full`, etc.).
     - Voltage delta exceeds **250 mV** (capturing load transients).
     - Telemetry provider source or simulation mode changes.
     - Heartbeat interval of **5 minutes** elapses (preserving time-series continuity).
2. **Active Session Caching (`DashboardViewModel.cs`):**
   - In-memory caching of the active charging session record eliminates recurring SQLite read queries during continuous charging or discharging.
3. **Optimized WAL Checkpoint Threshold:**
   - Configured `PRAGMA wal_autocheckpoint = 250;` to prevent unbounded WAL log growth.

---

## 6. Benchmarking Suite (`measure-memory.ps1`)

The benchmark tool [`scripts/measure-memory.ps1`](file:///home/mikhailryu/code/repos/victusguardian/scripts/measure-memory.ps1) samples live process telemetry on physical Windows hardware:

```powershell
# Benchmark hardware-accelerated baseline
pwsh scripts/measure-memory.ps1 -Label "Hardware-Baseline" -DurationSeconds 120

# Benchmark software rendering mode
pwsh scripts/measure-memory.ps1 -Label "Software-Rendering" -DurationSeconds 120
```

The script records:
- Working Set (Physical RAM), Peak Working Set, Private Bytes (Committed Memory)
- Win32 Process I/O counters: `WriteOperationCount`, `WriteTransferCount`
- Active handle count and thread count
- Total user/kernel CPU seconds consumed
