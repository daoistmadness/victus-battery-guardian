# Physical Hardware Validation Protocol: HP Victus 15.6 (Windows 11 x64)

> [!IMPORTANT]
> **Status:** Pending Physical Target Execution.
> This checklist defines the exact protocol for validating Victus Battery Guardian on the physical target hardware.
> Per project engineering rules, physical validation is **NOT** claimed as passed until executed on the actual HP Victus 15.6 hardware running Windows 11.

---

## 1. Test Environment & Target Specifications

- **Target Device:** HP Victus Gaming Laptop 15.6"
- **Operating System:** Windows 11 64-bit (Build 22621 or newer)
- **Primary Battery:** HP Standard Lithium-Ion Polymer Battery (ACPI `PNP0C0A` compatible, typically 70 Wh / 4-cell or 52.5 Wh / 3-cell)
- **Validation Artifact:** `VictusGuardian-v0.2.0-beta-win-x64.zip` (Self-contained, single-file publish)

---

## 2. Pre-Flight Preparation

1. **Generate Windows Reference Baseline**:
   Open PowerShell as Administrator on the HP Victus and generate the official Windows battery diagnostic report:
   ```powershell
   powercfg /batteryreport /output "$HOME\Desktop\battery-report.html"
   ```
2. **Record Baseline Metrics from HTML Report**:
   - Design Capacity (mWh)
   - Full Charge Capacity (mWh)
   - Cycle Count
   - Manufacturer / Chemistry / Serial Number
3. **Extract Victus Battery Guardian**:
   ```powershell
   Expand-Archive -Path "VictusGuardian-v0.2.0-beta-win-x64.zip" -DestinationPath "$HOME\Desktop\VictusGuardian"
   cd "$HOME\Desktop\VictusGuardian"
   ```

---

## 3. Test Cases Execution Checklist

### Test 1: Device Interface Discovery & Enumeration
- [ ] Launch `VictusGuardian.App.exe` (run without elevation / standard user rights).
- [ ] **Expected Result:** Application starts without requiring administrative UAC prompts.
- [ ] Check Active Telemetry Tier on Dashboard:
  - Must report `Windows IOCTL (Direct Device Interface)` or `Windows Win32 (GetSystemPowerStatus Fallback)`.
  - Must **NOT** report `Demo Mode (Simulated)` or `SIMULATED TELEMETRY`.
- [ ] Verify Battery Identity card:
  - Manufacturer, serial number, chemistry, and device name should match the `powercfg /batteryreport` data.

### Test 2: Capacity & Health Baseline Comparison
- [ ] Compare `Design Capacity` in app with `battery-report.html` (e.g. 70,070 mWh vs reported).
- [ ] Compare `Full Charge Capacity` in app with `battery-report.html`.
- [ ] Compare `Cycle Count` (if supported by Victus embedded controller firmware).
- [ ] Verify `Battery Percentage` matches Windows taskbar battery flyout (within ±1%).
- [ ] Verify Health score formula: `(FullChargeCapacity / DesignCapacity) * 100`.

### Test 3: AC Line Connection & Charging State Transitions
- [ ] **State A: Discharging on Battery**
  - Unplug the barrel-jack AC adapter.
  - Observe Dashboard:
    - Status changes to `On Battery Power (AC Offline)`.
    - Charging state changes to `Discharging`.
    - Discharge rate is displayed with negative polarity (e.g. `-18.5 W`).
    - Estimated time remaining displays valid discharge countdown.
- [ ] **State B: AC Connected & Charging**
  - Plug in the official HP Smart AC Adapter (150W/200W).
  - Observe Dashboard within 5 seconds:
    - Status changes to `AC Adapter Connected`.
    - Charging state changes to `Charging`.
    - Charge rate is displayed with positive polarity (e.g. `+45.2 W`).
    - Estimated time remaining updates to charge completion estimate.
- [ ] **State C: 100% Full Charge State**
  - Allow battery to reach 100%.
  - Verify state transitions to `Full` or `NotCharging` with minimal rate (<1W).

### Test 4: Sensor Capability Reporting
- [ ] Verify `Supported Sensor Channels` accurately reflects available hardware:
  - Percentage: Supported
  - AC Status: Supported
  - Charging State: Supported
  - Remaining Capacity: Supported
  - Full Charge Capacity: Supported
  - Design Capacity: Supported
  - Voltage: Supported (typically ~15.4V for 4-cell, ~11.5V for 3-cell)
  - Charge Rate: Supported
- [ ] Verify `Unsupported Sensor Channels` gracefully documents unpopulated ACPI fields without error.

### Test 5: Suspend & Resume Resilience
- [ ] Close the laptop lid or initiate Windows Sleep (`Start -> Power -> Sleep`).
- [ ] Wait 30 seconds.
- [ ] Wake the laptop and unlock Windows.
- [ ] Observe Victus Battery Guardian:
  - No crash, freeze, or unhandled exception.
  - Device handle safely reopened.
  - Telemetry refreshes immediately upon resume.
  - Stale warning badge clears once first fresh telemetry packet is received.

### Test 6: Explicit Demo Mode Verification
- [ ] Navigate to `⚙️ Demo Settings`.
- [ ] Toggle `Enable Demo Mode`.
- [ ] **Expected Result:**
  - Sidebar immediately shows `DEMO MODE ACTIVE` badge.
  - Top action bar displays `⚠ DEMO MODE ACTIVE — Battery telemetry is simulated for evaluation.`
  - Provenance badge on Overview card switches to `SIMULATED TELEMETRY (DEMO MODE)`.
- [ ] Untoggle `Enable Demo Mode`.
  - **Expected Result:** Application immediately reverts to live hardware IOCTL readings.

### Test 7: SQLite Persistence & Low-Power Throttling
- [ ] Navigate to `📜 Telemetry History`.
- [ ] Verify readings are logged into `battery_history.db`.
- [ ] Confirm idle throttling: while battery percentage is steady, duplicate writes are throttled to prevent SSD wear.

---

## 4. Hardware Validation Sign-off Log

| Date | Target Serial / BIOS | Tester | Result | Notes / Deviations |
| :--- | :--- | :--- | :--- | :--- |
| *Pending* | HP Victus 15-fb0xxx / AMI F.15 | Pending | PENDING | Target run scheduled on physical device. |
