<p align="center">
  <img src="logo.png" width="128" height="128" alt="AspireSense Winged Alpha Logo" />
</p>

# AspireSense (Open-Source Edition)

A standalone, high-performance hardware monitor, cooling control cockpit, and anti-hallucination EC arbiter for **Acer, ASUS, Lenovo, HP, Dell**, and compatible gaming laptops. Completely independent of bloated OEM companion software.

---

## 🧭 Fully Functional Navigation Tabs

### 1. **Dashboard**
- **Dual Speedometers**:
  - **Left (CPU)**: Live rotating speedometer needle, % utilization, dynamic color-coded temperature, and fan RPM.
  - **Right (GPU)**: Direct NVIDIA NVML hardware sensor (0% when idle / on Firefox, exact match to Task Manager), temperature, and fan RPM.
- **Center Cooling Master**:
  - **MAX (100% TURBO)**: Blasts fans to full speed.
  - **AUTO**: Dynamic intelligent firmware curves.
  - **QUIET**: Low-RPM eco profile.
  - **Manual Speed Override**: Working hardware curve percentage slider (30% – 100% with Motor Stall & Overheat Protection).
- **Active Hardware Thermal Watchdog**:
  - Automatically overrides fans to Turbo if CPU exceeds 92°C or GPU exceeds 86°C.
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
- **Turbo Heat Flush**:
  - 60-second high-velocity blower flush with automated auto-mode restoration.

### 3. **Gaming Hub & In-Game HUD Overlay**
- **Zero-Hook DirectX ETW Telemetry**:
  - Real-time FPS, 1% Lows, frame time, and GPU/CPU stats directly from DXGI present events.
- **In-Game Overlay HUD**:
  - Non-intrusive OSD (`Ctrl + Shift + O` global hotkey) with custom corner anchors and layout presets.
- **Performance Scenarios & Network Diagnostics**:
  - Instant scenario toggles and live low-jitter ICMP ping monitor.

### 4. **System Health & Hardware Telemetry**
- **Hardware Identity**:
  - Detected chassis model, CPU architecture, discrete GPU specifications, BIOS & EC firmware revisions.
- **Memory, Battery & Storage Health**:
  - Live RAM bar (Used vs Available vs Total GB) with top memory-consuming processes.
  - Battery wear level, design vs full-charge capacity (Wh), and AC charging status.
  - Drive storage headroom and thermal margins.

### 5. **EC Anti-Hallucination Arbiter (Anti-Revving Engine)**
- **Problem Solved**:
  - Eliminates the notorious fan revving / jet engine ramping on modern laptops caused by Windows background tasks (e.g., Microsoft Compatibility Telemetry, Defender) and transient browser spikes.
- **Thermal EMA (Exponential Moving Average)**:
  - Models the real physical copper heatsink thermal mass ($\alpha = 0.15$), ignoring short 1–2 second silicon temperature spikes.
- **Native Hardware Silent Mode (Mode 3)**:
  - Applies Acer's native hardware silent acoustic profile during desktop tasks without constant register spamming.
- **Automated 3D Gaming & Sustained Compute Unclamp**:
  - Automatically detects when a 3D game launches or when sustained compute (> 4 seconds) begins, instantly unclamping fans to full cooling curves.
- **Power Plan Boost Tamer**:
  - Integrated 1-click toggle setting CPU maximum state to 99% via `powercfg`, eliminating micro-burst overvoltage while browsing without perceptible latency.
- **Emergency Thermal Failsafe**:
  - Immediate override to 100% Turbo fans if CPU reaches $\ge 86^\circ\text{C}$ or GPU reaches $\ge 85^\circ\text{C}$.

---

## 🏗️ Production Modular Architecture

AspireSense is structured into clean, maintainable modular layers under `src/`:

```
AspireSense/
├── .gitignore
├── app.ico
├── app.manifest
├── build.bat
├── build_app.rsp
├── build_installer.rsp
├── Installer.cs
├── InsydeDCHU.dll
├── LICENSE
├── logo.png
├── README.md
├── RELEASE_NOTES.md
└── src/
    ├── Program.cs                      # Single-instance Mutex & App Entry Point
    ├── Common/
    │   └── Enums.cs                    # HardwareVendor, OverlayPosition & Style enums
    ├── Hardware/
    │   ├── HardwareBridge.cs           # Unified multi-vendor dispatcher & sensor polling
    │   ├── AcerInsydeBridge.cs         # Acer Aspire/Nitro/Predator Insyde DCHU DLL interop
    │   ├── AsusAcpiBridge.cs           # ASUS TUF/ROG ATKACPI & WMI interop
    │   ├── LenovoWmiBridge.cs          # Lenovo Legion/LOQ Vantage WMI interop
    │   ├── HpWmiBridge.cs              # HP Omen/Victus WMI interop
    │   └── DellWmiBridge.cs            # Dell Alienware/G-Series SMBIOS WMI interop
    ├── Engine/
    │   ├── EcAntiHallucinationEngine.cs # Heatsink thermal EMA, power tamer & arbiter rules
    │   └── GameTelemetryEngine.cs      # Zero-hook DirectX 9/11/12 DXGI ETW frame tracer
    └── UI/
        ├── DesignTokens.cs             # Apple HIG dark mode tokens, brushes & styling
        ├── MainWindow.cs               # Core window shell, navigation, layout & timer ticks
        ├── Controls/
        │   ├── SpeedometerGauge.cs     # Circular SVG-grade needle gauge
        │   └── SegmentedMeter.cs       # Segmented LED bar meter
        ├── Overlay/
        │   └── MetricsOverlayWindow.cs # In-game transparent borderless HUD overlay
        └── Views/
            ├── MainWindow.Dashboard.cs # Tab 0: Dashboard view & quick modes
            ├── MainWindow.Cooling.cs   # Tab 1: Cooling studio & fan curves
            ├── MainWindow.Gaming.cs    # Tab 2: Gaming hub, OSD & network tools
            ├── MainWindow.Health.cs    # Tab 3: System health & hardware specs
            └── MainWindow.Arbiter.cs   # Tab 4: EC Anti-Hallucination Arbiter & logs
```

---

## 🚀 Installation & Downloads

### Option 1: Standalone Windows Installer (Recommended)
Download **`AspireSense-Setup.exe`** from the [GitHub Releases](https://github.com/netfreakkk/AspireSense/releases) page:
- **1-Click Self-Contained Setup**: Includes all native EC drivers and dependencies embedded.
- **Shortcuts**: Automatically creates Desktop and Start Menu shortcuts with high-res icon.
- **Windows Integration**: Fully registered in Windows Settings > Apps & Features with clean 1-click uninstaller.

### Option 2: Portable Edition (No Install Required)
Download **`AspireSense-Portable-v1.1.0.zip`**:
- Extract anywhere and run **`AspireSense.exe`**.
- Keep `InsydeDCHU.dll` in the same directory.

---

## 🛠️ Building from Source
Run `build.bat` in the root folder to compile the application, standalone installer, and release zip in one click using .NET Framework 4.0+.
