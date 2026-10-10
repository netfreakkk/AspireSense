# AspireSense v1.1.0 - Multi-Vendor Cockpit Release

**AspireSense v1.1.0** expands from an Acer-exclusive utility into an open-source, multi-vendor fan control and hardware telemetry cockpit engineered for **Acer, ASUS, Lenovo, HP, and Dell** gaming laptops.

Operates directly over native kernel ACPI drivers, vendor WMI interfaces, and NVIDIA NVML—completely independent of proprietary OEM software (Armoury Crate, Lenovo Vantage, OMEN Gaming Hub, Alienware Command Center, Acer Care Center).

---

### What's New in v1.1.0

#### 1. Universal Multi-Vendor Hardware Support
* **ASUS TUF Gaming & ROG Laptops:** Native direct communication via `\\.\ATKACPI` (`IOCTL 0x0022240C`) and `AsusAtkWmi_WMNB`. Live CPU/GPU fan RPM reading and dynamic mode switching.
* **Lenovo Legion, LOQ & IdeaPad Gaming:** Native WMI control over `root\wmi:LNVPC_WMI` (`SetSmartFanMode`) and `Lenovo_Vantage_EC` (`SetPerformanceMode`).
* **HP OMEN, Victus & Pavilion Gaming:** Direct BIOS control via `root\wmi:hpqBIntM` (`BiosControl`) and `HP_BiosSetting`.
* **Dell Alienware & G-Series (G15/G16):** Direct thermal profiles over `root\wmi:Dell_ThermalInformation` (`SetThermalProfile`) and `Alienware_Platform`.
* **Acer Aspire, Nitro & Predator:** Insyde H2O Embedded Controller buffer protocol with isolated `InsydeDCHU.dll`.

#### 2. Adaptive Hardware Installer
* **Chassis Architecture Detection:** Automatically detects target laptop motherboard and manufacturer from BIOS.
* **Selective DLL Payload Extraction:** `InsydeDCHU.dll` is unpacked **only** on Acer machines. On ASUS, Lenovo, HP, Dell, and generic systems, it is omitted entirely to keep installations minimal and clean.
* **Active Process Handling:** Automatically detects and closes running instances before extracting updates to avoid locked-file errors.

#### 3. Cockpit Dashboard Aesthetic with Clean Naming
* **Radial Instrument Cluster:** Dual 270-degree tachometer gauges with 4-stage RPM arcs, dynamic needle physics, and neon crimson Redline zone.
* **Clean Profile Trio:**
  * **`AUTO`** — Balanced dynamic adaptive mode.
  * **`QUIET`** — Silent acoustic low-RPM profile.
  * **`MAX`** — 100% turbo boost maximum cooling.
* **Manual Fan Override:** Precision percentage slider with active duty locking.
* **Dynamic Hardware Footer:** Real-time indicator displaying the detected vendor architecture and active engine.

#### 4. Stability, Safety & Security Hardening
* **Embedded UAC Administrator Manifest:** Guarantees proper elevation for Windows kernel ACPI device driver access on first launch.
* **WMI Resource Protection:** Eliminates COM handle leaks across all hardware bridges and thermal zone polling routines.
* **Thermal Watchdog:** Automatically engages maximum cooling if hardware exceeds thermal thresholds under constrained modes.
* **Exit Auto-Reset:** Guarantees fan controllers return to BIOS Auto control on application close or termination.

---

### Release Assets & Checksums

| Asset | Description | Size | SHA256 Checksum |
| :--- | :--- | :--- | :--- |
| **`AspireSense-Setup.exe`** | Standalone Adaptive Installer (Desktop & Start Menu shortcuts) | ~3.7 MB | `81FB79F64C13C208F91719A1A7F32F9D09DE4909019441657329F443A9436349` |
| **`AspireSense-Portable-v1.1.0.zip`** | Portable release package (extract and run) | ~2.0 MB | `34A44463230693B29823438CC58C4EDD4BE621170EB4622A4FB00E73C15F53EB` |
| **`AspireSense.exe`** | Standalone Executable | ~177 KB | `97D2C374F9CC0F6929DA7E29AE75DF5CB74F424335BCF94EC9459B7A23525C56` |

---

### Requirements
* Windows 10 / 11 (64-bit)
* .NET Framework 4.8+ (pre-installed on Windows 10/11)
* Run as Administrator (requested automatically via embedded UAC manifest)
