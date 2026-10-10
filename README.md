# AspireSense (Open-Source Edition)

A standalone, high-performance hardware monitor and cooling control center tailored for **Acer Aspire 7 (RTX 4050 / A715-79G)** and compatible gaming laptops.

---

## 🧭 Fully Functional Navigation Tabs

### 1. **Dashboard**
- **Dual Speedometers**:
  - **Left (CPU)**: Live rotating speedometer needle, % utilization, dynamic color-coded temperature, and fan RPM.
  - **Right (GPU)**: Direct NVIDIA NVML hardware sensor (0% when idle / on Firefox, exact match to Task Manager), temperature, and fan RPM.
- **Center Cooling Master**:
  - **MAX (100% TURBO)**: Blasts fans to full speed.
  - **AUTO**: Dynamic intelligent firmware curves (whisper quiet).
  - **QUIET**: Low-RPM eco profile.
  - **Manual Speed Override**: Working hardware curve percentage slider (30% – 100% with Motor Stall & 100°C Overheat Protection).
- **Active Hardware Thermal Watchdog**:
  - Automatically overrides fans to Turbo if CPU exceeds 90°C or GPU exceeds 85°C.
- **Fail-Safe Crash & Exit Auto-Reset**:
  - Embedded Controller fan registers are guaranteed to restore to factory dynamic Auto mode on app close or termination.
- **Quick Gaming Tools Hub**:
  - Standby Memory Flush / Boost button.
  - Win-Key Gaming Lock toggle.
  - Instant shortcuts to Task Manager & NVIDIA Control Panel.

### 2. **Cooling Control Studio**
- **Dedicated Fan Blowers**:
  - Large dedicated CPU Blower gauge, RPM readout, and duty progress bar.
  - Large dedicated GPU Blower gauge with zero-RPM idle indicator.
- **Hardware Thermal Curve Table**:
  - Displays the 4 calibrated stages of your laptop's Embedded Controller: Stage 1 (45°C), Stage 2 (60°C), Stage 3 (80°C), and Stage 4 (100°C).
- **Fine Manual Speed Override**:
  - Full-width slider with immediate hardware fan lock.

### 3. **Gaming Hub**
- **Performance Scenarios**:
  - 🚀 **Turbo Gaming**: Full cooling and performance profile.
  - ⚖️ **Balanced Daily**: Auto fan curves and balanced power.
  - 🍃 **Eco & Silent**: Energy saving and quiet fans.
- **Game Acceleration Utilities**:
  - NVIDIA Control Panel launcher.
  - Windows Task Manager launcher.
  - DirectX Diagnostic (DxDiag) launcher.
  - Windows Power & Battery options launcher.

### 4. **System Health & Telemetry**
- **Hardware Identity**:
  - Detected model: Acer Aspire A715-79G.
  - GPU: NVIDIA GeForce RTX 4050 Laptop GPU (6 GB VRAM).
  - CPU Cores, Insyde EC Controller v1.13, Windows 11 64-bit architecture.
- **Memory & Power Health**:
  - Live RAM bar (Used vs Available vs Total GB) with live load percentage.
  - Live Battery & AC Power status (Plugged In vs On Battery with charge %).
- **Local Storage Drives**:
  - Live list of internal drives (C:\, etc.) with used/free capacity bars.

---

## 🚀 100% Independent & Portable
- **Zero dependencies on Control Center 3 (CC3)**.
- **Won't break on future Windows updates**.
- **Under 45 KB**, native hardware-accelerated WPF executable.

---

## How to Launch
Double-click:
`AspireSense.exe`
*(Keep `InsydeDCHU.dll` in the same directory).*
