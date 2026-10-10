# AspireSense v1.0.0 - Supercar Cockpit Edition

**AspireSense** is an open-source, ultra-low-latency fan control center, thermal monitor, and gaming telemetry suite engineered specifically for Acer Aspire laptops (*Acer Aspire 7 A715-79G with Intel Core 5 210H & NVIDIA GeForce RTX 4050 Laptop GPU*).

Operates directly over the Insyde / Clevo Embedded Controller (EC) and NVIDIA NVML, completely independent of Acer Care Center / Care Center 3.

---

### Key Features

* **Cockpit Speedometer Dashboard:**
  * Dual 270-degree tachometer sweep gauges (7 o'clock to 5 o'clock) with numbered ticks (`0` to `100`).
  * Neon crimson Redline Zone (80% to 100%) with active needle response.
  * Embedded coolant sub-gauges with 5-segment LED thermal meters.
  * Sports Car Drive-Mode Selector:
    * `[ ECO ]` (Silent - 28% Duty)
    * `[ DYNAMIC ]` (Balanced Daily - Adaptive Auto)
    * `[ SPORT+ ]` (100% Maximum Turbo Airflow)
  * Manual automotive fan throttle slider with quick duty lock.

* **Cooling Control Studio:**
  * Dual dedicated blowers with 20-segment cyan/azure LED meters.
  * **60-Second Turbo Heat Flush:** 1-click vapor chamber purge with active countdown timer.
  * Interactive 4-Stage Hardware Curve Visualizer with real-time active stage illumination.

* **Gaming Hub:**
  * **1-Click Ultra Game Boost:** Max Turbo cooling + RAM working set purge + Windows-Key lock + High Performance power plan.
  * **Live Multiplayer CDN Ping Telemetry:** Cloudflare edge ping with latency quality ratings.
  * Hardware acceleration quick launchers (NVIDIA Control Panel, Task Manager, DxDiag, HAGS Graphics).

* **Minimalist System Health & Analytics:**
  * Silicon Chassis Ribbon with live uptime telemetry.
  * RTX 4050 AD107M core & memory clock meters, PCIe bus link, and throttle reason diagnostics.
  * RAM load meter with top 3 consuming processes and 1-click RAM purger.
  * Battery health & wear index calculation (mWh design vs full charge).
  * 1-click Markdown diagnostic report export to clipboard.

* **Hardware Safety & Thermal Watchdog:**
  * Auto-failsafe watchdog engages Turbo cooling if temperatures exceed 90 C under constrained modes.
  * Fail-safe restoration: Automatically restores EC fan controller to BIOS Auto on app close.

---

### Release Assets & Checksums

| Asset | Description | Size | SHA256 Checksum |
| :--- | :--- | :--- | :--- |
| **`AspireSense-Setup.exe`** | Standalone Windows Installer (with Desktop & Start Menu shortcuts) | ~3.7 MB | `7486E81827000167A5C9FE7729E024E68522E5896712E3B6873146644D8086AB` |
| **`AspireSense-Portable-v1.0.0.zip`** | Portable release package (extract and run) | ~2.0 MB | `A0C90F5AC65B8C1B7E5C1BBC7B35D5B7E59B03381380F658B21A71FDD4F2E2C2` |
| **`AspireSense.exe`** | Standalone Executable | ~177 KB | `322C10A247BFE9C966E2FC74E3D93071B3C8B2B87A35EF7FF5C2944D4BAC6340` |

---

### Requirements
* Windows 10 / 11 (64-bit)
* .NET Framework 4.8+ (pre-installed on Windows 10/11)
* Run as Administrator (required for direct EC hardware register access)
