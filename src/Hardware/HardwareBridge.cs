using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Management;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using Microsoft.Win32;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Shapes;
using System.Windows.Threading;
using System.Windows.Interop;
using Path = System.Windows.Shapes.Path;

namespace AspireSense
{
    // ==========================================
    // HARDWARE BRIDGE: Multi-vendor telemetry & dispatch
    // ==========================================
    public static class HardwareBridge
    {
        // NVIDIA NVML API for exact GPU utilization matching Task Manager
        [DllImport("nvml.dll", EntryPoint = "nvmlInit_v2")]
        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        public static extern int nvmlInit();

        [DllImport("nvml.dll", EntryPoint = "nvmlShutdown")]
        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        public static extern int nvmlShutdown();

        [DllImport("nvml.dll", EntryPoint = "nvmlDeviceGetHandleByIndex_v2")]
        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        public static extern int nvmlDeviceGetHandleByIndex(uint index, out IntPtr device);

        [DllImport("nvml.dll", EntryPoint = "nvmlDeviceGetName")]
        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        public static extern int nvmlDeviceGetName(IntPtr device, StringBuilder name, uint length);

        [StructLayout(LayoutKind.Sequential)]
        public struct nvmlUtilization_t
        {
            public uint gpu;
            public uint memory;
        }

        [DllImport("nvml.dll", EntryPoint = "nvmlDeviceGetUtilizationRates")]
        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        public static extern int nvmlDeviceGetUtilizationRates(IntPtr device, out nvmlUtilization_t utilization);

        [DllImport("nvml.dll", EntryPoint = "nvmlDeviceGetTemperature")]
        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        public static extern int nvmlDeviceGetTemperature(IntPtr device, int sensorType, out uint temp);

        [DllImport("nvml.dll", EntryPoint = "nvmlDeviceGetPowerUsage")]
        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        public static extern int nvmlDeviceGetPowerUsage(IntPtr device, out uint powerMw);

        [DllImport("nvml.dll", EntryPoint = "nvmlDeviceGetEnforcedPowerLimit")]
        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        public static extern int nvmlDeviceGetEnforcedPowerLimit(IntPtr device, out uint limitMw);

        [StructLayout(LayoutKind.Sequential)]
        public struct nvmlMemory_t
        {
            public ulong total;
            public ulong free;
            public ulong used;
        }

        [DllImport("nvml.dll", EntryPoint = "nvmlDeviceGetMemoryInfo")]
        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        public static extern int nvmlDeviceGetMemoryInfo(IntPtr device, out nvmlMemory_t memory);

        [DllImport("nvml.dll", EntryPoint = "nvmlDeviceGetClockInfo")]
        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        public static extern int nvmlDeviceGetClockInfo(IntPtr device, int type, out uint clockMHz);

        [DllImport("nvml.dll", EntryPoint = "nvmlDeviceGetCurrentClocksThrottleReasons")]
        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        public static extern int nvmlDeviceGetCurrentClocksThrottleReasons(IntPtr device, out ulong reasons);

        [DllImport("nvml.dll", EntryPoint = "nvmlDeviceGetCurrPcieLinkGeneration")]
        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        public static extern int nvmlDeviceGetCurrPcieLinkGeneration(IntPtr device, out uint currLinkGen);

        [DllImport("nvml.dll", EntryPoint = "nvmlDeviceGetCurrPcieLinkWidth")]
        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        public static extern int nvmlDeviceGetCurrPcieLinkWidth(IntPtr device, out uint currLinkWidth);

        [StructLayout(LayoutKind.Sequential)]
        public struct MEMORYSTATUSEX
        {
            public uint dwLength;
            public uint dwMemoryLoad;
            public ulong ullTotalPhys;
            public ulong ullAvailPhys;
            public ulong ullTotalPageFile;
            public ulong ullAvailPageFile;
            public ulong ullTotalVirtual;
            public ulong ullAvailVirtual;
            public ulong ullAvailExtendedVirtual;
        }

        [DllImport("kernel32.dll")]
        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        public static extern bool GlobalMemoryStatusEx(ref MEMORYSTATUSEX lpBuffer);

        [StructLayout(LayoutKind.Sequential)]
        public struct SYSTEM_POWER_STATUS
        {
            public byte ACLineStatus;
            public byte BatteryFlag;
            public byte BatteryLifePercent;
            public byte Reserved1;
            public uint BatteryLifeTime;
            public uint BatteryFullLifeTime;
        }

        [DllImport("kernel32.dll")]
        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        public static extern bool GetSystemPowerStatus(out SYSTEM_POWER_STATUS lp);

        [DllImport("kernel32.dll")]
        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        public static extern ulong GetTickCount64();

        [DllImport("psapi.dll")]
        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        public static extern int EmptyWorkingSet(IntPtr hwProc);

        private static readonly object ecLock = new object();
        private static bool nvmlReady = false;
        private static IntPtr nvmlDevice = IntPtr.Zero;
        private static uint nvmlPowerLimitMw = 0;
        private static PerformanceCounter cpuPowerCounter = null;
        private static bool cpuPowerInitialized = false;

        // Hardware Identity & Detection
        public static HardwareVendor DetectedVendor = HardwareVendor.Acer;
        public static bool IsAsusHardware = false;
        public static int CurrentFanMode = 0;
        public static int CurrentCustomDuty = 50;

        // Chassis & Firmware Profile
        public static string ChassisCpuName = "High-Performance Processor";
        public static string ChassisGpuName = "Dedicated Graphics";
        public static string ChassisBiosVersion = "System BIOS";
        public static string ChassisEcVersion = "EC Controller";
        public static string ChassisModel = "Gaming Laptop";
        public static string ChassisOs = "Windows 11 (64-bit)";
        public static double ChassisTotalRamGb = 16.0;
        public static int CachedBatteryDesignMwh = 54000;
        public static int CachedBatteryFullMwh = 54000;

        public static bool IsHardwareControlAvailable
        {
            get
            {
                if (DetectedVendor == HardwareVendor.Acer) return AcerInsydeBridge.IsAvailable;
                if (DetectedVendor == HardwareVendor.Asus) return AsusAcpiBridge.IsAvailable;
                if (DetectedVendor == HardwareVendor.Lenovo) return LenovoWmiBridge.IsAvailable;
                if (DetectedVendor == HardwareVendor.Hp) return HpWmiBridge.IsAvailable;
                if (DetectedVendor == HardwareVendor.Dell) return DellWmiBridge.IsAvailable;
                return false;
            }
        }

        public static void InitializeHardware()
        {
            try
            {
                int res = nvmlInit();
                if (res == 0)
                {
                    int hRes = nvmlDeviceGetHandleByIndex(0, out nvmlDevice);
                    nvmlReady = (hRes == 0 && nvmlDevice != IntPtr.Zero);
                    if (nvmlReady)
                    {
                        StringBuilder sb = new StringBuilder(128);
                        if (nvmlDeviceGetName(nvmlDevice, sb, 128) == 0 && sb.Length > 0)
                        {
                            ChassisGpuName = sb.ToString().Trim();
                        }

                        uint pLim;
                        if (nvmlDeviceGetEnforcedPowerLimit(nvmlDevice, out pLim) == 0 && pLim > 0)
                        {
                            nvmlPowerLimitMw = pLim;
                        }
                    }
                }
            }
            catch
            {
                nvmlReady = false;
            }

            // Fallback GPU detection via WMI if NVML is not available
            if (!nvmlReady || ChassisGpuName == "Dedicated Graphics")
            {
                try
                {
                    using (ManagementObjectSearcher vs = new ManagementObjectSearcher("SELECT Name FROM Win32_VideoController"))
                    {
                        using (ManagementObjectCollection vcoll = vs.Get())
                        {
                            foreach (ManagementObject vobj in vcoll)
                            {
                                using (vobj)
                                {
                                    object vName = vobj["Name"];
                                    if (vName != null)
                                    {
                                        string sName = vName.ToString().Trim();
                                        if (sName.Contains("NVIDIA") || sName.Contains("GeForce") || sName.Contains("RTX") || sName.Contains("GTX") || sName.Contains("Radeon") ||
                                            sName.Contains("Intel(R) Arc") || sName.Contains("Arc(TM)") || sName.Contains("Arc A"))
                                        {
                                            ChassisGpuName = sName;
                                            break;
                                        }
                                        else if (ChassisGpuName == "Dedicated Graphics")
                                        {
                                            ChassisGpuName = sName;
                                        }
                                    }
                                }
                            }
                        }
                    }
                }
                catch { }
            }

            // Read Total System Memory
            try
            {
                MEMORYSTATUSEX mem = new MEMORYSTATUSEX();
                mem.dwLength = (uint)Marshal.SizeOf(typeof(MEMORYSTATUSEX));
                if (GlobalMemoryStatusEx(ref mem))
                {
                    ChassisTotalRamGb = Math.Round(mem.ullTotalPhys / (1024.0 * 1024.0 * 1024.0));
                }
            }
            catch { }

            // Read CPU Profile from Registry
            try
            {
                using (RegistryKey key = Registry.LocalMachine.OpenSubKey(@"HARDWARE\DESCRIPTION\System\CentralProcessor\0"))
                {
                    if (key != null)
                    {
                        object v = key.GetValue("ProcessorNameString");
                        if (v != null) ChassisCpuName = v.ToString().Trim();
                    }
                }
            }
            catch { }

            // Read BIOS & Chassis Profile from Registry
            string sysMfg = "";
            string boardMfg = "";
            string prodName = "";
            try
            {
                using (RegistryKey key = Registry.LocalMachine.OpenSubKey(@"HARDWARE\DESCRIPTION\System\BIOS"))
                {
                    if (key != null)
                    {
                        object sm = key.GetValue("SystemManufacturer");
                        if (sm != null) sysMfg = sm.ToString().Trim();

                        object bm = key.GetValue("BaseBoardManufacturer");
                        if (bm != null) boardMfg = bm.ToString().Trim();

                        object m = key.GetValue("SystemProductName");
                        if (m != null) prodName = m.ToString().Trim();

                        string combined = (sysMfg + " " + boardMfg + " " + prodName).ToLowerInvariant();

                        // Priority 1: Check Acer hardware first (prevents false matches on multi-word BIOS strings)
                        if (combined.Contains("acer") || combined.Contains("aspire") || combined.Contains("nitro") || combined.Contains("predator"))
                        {
                            DetectedVendor = HardwareVendor.Acer;
                            ChassisModel = prodName.StartsWith("Acer", StringComparison.OrdinalIgnoreCase) ? prodName : "Acer " + prodName;
                        }
                        else if (combined.Contains("asus") || combined.Contains("tuf") || combined.Contains("rog"))
                        {
                            DetectedVendor = HardwareVendor.Asus;
                            ChassisModel = prodName.StartsWith("ASUS", StringComparison.OrdinalIgnoreCase) ? prodName : "ASUS " + prodName;
                        }
                        else if (combined.Contains("lenovo") || combined.Contains("legion") || combined.Contains("ideapad") || combined.Contains("loq"))
                        {
                            DetectedVendor = HardwareVendor.Lenovo;
                            ChassisModel = prodName.StartsWith("Lenovo", StringComparison.OrdinalIgnoreCase) ? prodName : "Lenovo " + prodName;
                        }
                        else if (sysMfg.StartsWith("hp", StringComparison.OrdinalIgnoreCase) || sysMfg.StartsWith("hewlett", StringComparison.OrdinalIgnoreCase) ||
                                 combined.Contains("omen") || combined.Contains("victus") || combined.Contains("pavilion"))
                        {
                            DetectedVendor = HardwareVendor.Hp;
                            ChassisModel = prodName.StartsWith("HP", StringComparison.OrdinalIgnoreCase) ? prodName : "HP " + prodName;
                        }
                        else if (combined.Contains("dell") || combined.Contains("alienware"))
                        {
                            DetectedVendor = HardwareVendor.Dell;
                            ChassisModel = (prodName.StartsWith("Dell", StringComparison.OrdinalIgnoreCase) || prodName.StartsWith("Alienware", StringComparison.OrdinalIgnoreCase)) ? prodName : "Dell " + prodName;
                        }
                        else
                        {
                            DetectedVendor = HardwareVendor.Universal;
                            ChassisModel = !string.IsNullOrEmpty(prodName) ? prodName : "Gaming Laptop";
                        }

                        object bv = key.GetValue("BIOSVersion");
                        object br = key.GetValue("BIOSReleaseDate");
                        if (bv != null)
                        {
                            string biosPrefix = (DetectedVendor == HardwareVendor.Acer) ? "Insyde v" : (DetectedVendor.ToString() + " BIOS v");
                            ChassisBiosVersion = biosPrefix + bv.ToString().Trim() + (br != null ? " (" + br.ToString().Trim() + ")" : "");
                        }

                        object ecMaj = key.GetValue("ECFirmwareMajorRelease");
                        object ecMin = key.GetValue("ECFirmwareMinorRelease");
                        if (ecMaj != null && ecMin != null) ChassisEcVersion = string.Format("v{0}.{1}", ecMaj, ecMin);
                    }
                }
            }
            catch { }

            // Probe and initialize appropriate hardware vendor bridge
            if (DetectedVendor == HardwareVendor.Asus)
            {
                IsAsusHardware = true;
                AsusAcpiBridge.Initialize();
            }
            else if (DetectedVendor == HardwareVendor.Lenovo)
            {
                LenovoWmiBridge.Initialize();
            }
            else if (DetectedVendor == HardwareVendor.Hp)
            {
                HpWmiBridge.Initialize();
            }
            else if (DetectedVendor == HardwareVendor.Dell)
            {
                DellWmiBridge.Initialize();
            }
            else if (DetectedVendor == HardwareVendor.Acer)
            {
                // Native Acer Insyde EC bridge handled via AcerInsydeBridge
            }
            else
            {
                // Universal / Custom chassis fallback: probe driver endpoints
                if (AsusAcpiBridge.Initialize())
                {
                    DetectedVendor = HardwareVendor.Asus;
                    IsAsusHardware = true;
                }
                else if (LenovoWmiBridge.Initialize())
                {
                    DetectedVendor = HardwareVendor.Lenovo;
                }
                else if (HpWmiBridge.Initialize())
                {
                    DetectedVendor = HardwareVendor.Hp;
                }
                else if (DellWmiBridge.Initialize())
                {
                    DetectedVendor = HardwareVendor.Dell;
                }
            }

            // Read Windows OS Build from Registry
            try
            {
                using (RegistryKey key = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion"))
                {
                    if (key != null)
                    {
                        object pn = key.GetValue("ProductName");
                        object db = key.GetValue("DisplayVersion");
                        if (pn != null && db != null) ChassisOs = string.Format("{0} ({1})", pn, db);
                    }
                }
            }
            catch { }

            // Dynamic battery full capacity detection via WMI
            try
            {
                using (ManagementObjectSearcher bs = new ManagementObjectSearcher(@"root\wmi", "SELECT FullChargedCapacity FROM BatteryFullChargedCapacity"))
                {
                    using (ManagementObjectCollection bcoll = bs.Get())
                    {
                        foreach (ManagementObject bobj in bcoll)
                        {
                            using (bobj)
                            {
                                int full = Convert.ToInt32(bobj["FullChargedCapacity"]);
                                if (full > 5000 && full < 200000)
                                {
                                    CachedBatteryFullMwh = full;
                                    CachedBatteryDesignMwh = full;
                                }
                            }
                        }
                    }
                }
            }
            catch { }

            ResetToAuto();
        }

        private static bool isHardwareClosed = false;
        private static readonly object closeLock = new object();

        public static void CloseHardware()
        {
            lock (closeLock)
            {
                if (isHardwareClosed) return;
                isHardwareClosed = true;

                try
                {
                    if (DetectedVendor == HardwareVendor.Asus)
                    {
                        AsusAcpiBridge.Close();
                    }
                    else if (DetectedVendor == HardwareVendor.Lenovo)
                    {
                        LenovoWmiBridge.SetPerformanceMode(0);
                    }
                    else if (DetectedVendor == HardwareVendor.Hp)
                    {
                        HpWmiBridge.SetThermalMode(0);
                    }
                    else if (DetectedVendor == HardwareVendor.Dell)
                    {
                        DellWmiBridge.SetThermalMode(0);
                    }
                    else if (DetectedVendor == HardwareVendor.Acer && AcerInsydeBridge.IsAvailable)
                    {
                        AcerInsydeBridge.ResetFans();
                    }
                }
                catch { }

                try
                {
                    if (nvmlReady)
                    {
                        nvmlReady = false;
                        nvmlShutdown();
                    }
                }
                catch { }
            }
        }

        public static bool ResetToAuto()
        {
            CurrentFanMode = 0;
            if (DetectedVendor == HardwareVendor.Asus)
            {
                return AsusAcpiBridge.ResetFans();
            }
            else if (DetectedVendor == HardwareVendor.Lenovo)
            {
                return LenovoWmiBridge.SetPerformanceMode(0);
            }
            else if (DetectedVendor == HardwareVendor.Hp)
            {
                return HpWmiBridge.SetThermalMode(0);
            }
            else if (DetectedVendor == HardwareVendor.Dell)
            {
                return DellWmiBridge.SetThermalMode(0);
            }
            else if (DetectedVendor == HardwareVendor.Acer && AcerInsydeBridge.IsAvailable)
            {
                return AcerInsydeBridge.ResetFans();
            }
            return true;
        }

        public static bool SetFanMode(int mode)
        {
            CurrentFanMode = mode;
            if (DetectedVendor == HardwareVendor.Asus)
            {
                if (mode == 0) return AsusAcpiBridge.SetPerformanceMode(0); // Balanced / Auto
                if (mode == 1) return AsusAcpiBridge.SetPerformanceMode(1); // Turbo / Max
                if (mode == 3) return AsusAcpiBridge.SetPerformanceMode(2); // Silent / Quiet
                if (mode == 6) return AsusAcpiBridge.SetCustomDuty(80);
                return true;
            }
            else if (DetectedVendor == HardwareVendor.Lenovo)
            {
                return LenovoWmiBridge.SetPerformanceMode(mode);
            }
            else if (DetectedVendor == HardwareVendor.Hp)
            {
                return HpWmiBridge.SetThermalMode(mode);
            }
            else if (DetectedVendor == HardwareVendor.Dell)
            {
                return DellWmiBridge.SetThermalMode(mode);
            }
            else if (DetectedVendor == HardwareVendor.Acer && AcerInsydeBridge.IsAvailable)
            {
                return AcerInsydeBridge.SetFanMode(mode);
            }
            return true;
        }

        public static bool SetCustomSpeed(int dutyPercentage)
        {
            CurrentFanMode = 6;
            CurrentCustomDuty = dutyPercentage;
            if (DetectedVendor == HardwareVendor.Asus)
            {
                return AsusAcpiBridge.SetCustomDuty(dutyPercentage);
            }
            else if (DetectedVendor == HardwareVendor.Lenovo)
            {
                return LenovoWmiBridge.SetCustomDuty(dutyPercentage);
            }
            else if (DetectedVendor == HardwareVendor.Hp)
            {
                return HpWmiBridge.SetThermalMode(dutyPercentage >= 75 ? 1 : (dutyPercentage <= 45 ? 2 : 0));
            }
            else if (DetectedVendor == HardwareVendor.Dell)
            {
                return DellWmiBridge.SetThermalMode(dutyPercentage >= 75 ? 1 : (dutyPercentage <= 45 ? 2 : 0));
            }
            else if (DetectedVendor == HardwareVendor.Acer && AcerInsydeBridge.IsAvailable)
            {
                return AcerInsydeBridge.SetCustomSpeed(dutyPercentage);
            }
            return true;
        }

        public static void OptimizeMemory()
        {
            try
            {
                // 1. Collect and finalize all unreferenced .NET heap objects
                GC.Collect(GC.MaxGeneration, GCCollectionMode.Forced);
                GC.WaitForPendingFinalizers();

                // 2. Trim working set of the current process safely
                try
                {
                    EmptyWorkingSet(Process.GetCurrentProcess().Handle);
                }
                catch { }

                // 3. Trim non-critical user-session processes without touching system drivers, active game, or audio stack
                int currentSession = Process.GetCurrentProcess().SessionId;
                uint activeGamePid = GameTelemetryEngine.ActiveProcessId;
                Process[] procs = Process.GetProcesses();
                foreach (Process p in procs)
                {
                    using (p)
                    {
                        try
                        {
                            if (p.SessionId == currentSession && p.Id != Process.GetCurrentProcess().Id && p.Id != (int)activeGamePid)
                            {
                                string name = p.ProcessName.ToLowerInvariant();
                                if (!name.Contains("csrss") && !name.Contains("lsass") && !name.Contains("services") &&
                                    !name.Contains("dwm") && !name.Contains("explorer") && !name.Contains("system") &&
                                    !name.Contains("audiodg") && !name.Contains("nvvsvc") && !name.Contains("nvdisplay") &&
                                    !name.Contains("nvcontainer") && !name.Contains("amdrsserv") && !name.Contains("steam") &&
                                    !name.Contains("discord") && !name.Contains("epicgames") && !name.Contains("aspiresense"))
                                {
                                    EmptyWorkingSet(p.Handle);
                                }
                            }
                        }
                        catch { }
                    }
                }
            }
            catch { }
        }

        public class SensorData
        {
            public int CpuTemp;
            public int GpuTemp;
            public int CpuRpm;
            public int GpuRpm;
            public int CpuDuty;
            public int GpuDuty;
            public float CpuUsage;
            public float GpuUsage;
            public float RamUsage;
            public double TotalRamGb;
            public double UsedRamGb;
            public bool IsAcPlugged;
            public int BatteryPercent;

            // Advanced Telemetry Metrics
            public float CpuWatts;
            public float GpuWatts;
            public double VramTotalGb;
            public double VramUsedGb;
            public double VramFreeGb;
            public float VramUsage;
            public uint GpuCoreClock;
            public uint GpuMemClock;
            public ulong GpuThrottleReasons;
            public string GpuThrottleStatus;
            public uint PcieGen;
            public uint PcieWidth;
            public TimeSpan SystemUptime;
            public int BatteryDesignMwh;
            public int BatteryFullMwh;
            public double BatteryHealthPct;
        }

        private static int procTick = 0;
        private static List<Tuple<string, double>> cachedTopProc = new List<Tuple<string, double>>();

        // Cached WMI thermal zone temperature (avoid hitting WMI every second — triggers CompatTelRunner / telemetry)
        private static int cachedThermalZoneTemp = -1;
        private static int thermalZoneTick = 0;
        private const int THERMAL_ZONE_REFRESH_INTERVAL = 5; // Re-query WMI only every 5 seconds

        public static List<Tuple<string, double>> GetTopMemoryProcesses()
        {
            procTick++;
            if (cachedTopProc.Count > 0 && (procTick % 10 != 0))
            {
                return cachedTopProc;
            }

            try
            {
                Dictionary<string, double> dict = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
                Process[] procs = Process.GetProcesses();
                for (int i = 0; i < procs.Length; i++)
                {
                    using (procs[i])
                    {
                        try
                        {
                            string name = procs[i].ProcessName;
                            if (name.Equals("Idle", StringComparison.OrdinalIgnoreCase) || name.Equals("System", StringComparison.OrdinalIgnoreCase)) continue;
                            double mb = procs[i].WorkingSet64 / (1024.0 * 1024.0);
                            if (dict.ContainsKey(name))
                                dict[name] += mb;
                            else
                                dict[name] = mb;
                        }
                        catch { }
                    }
                }

                List<KeyValuePair<string, double>> list = new List<KeyValuePair<string, double>>(dict);
                list.Sort((a, b) => b.Value.CompareTo(a.Value));

                List<Tuple<string, double>> res = new List<Tuple<string, double>>();
                for (int i = 0; i < Math.Min(3, list.Count); i++)
                {
                    res.Add(new Tuple<string, double>(list[i].Key, list[i].Value));
                }
                cachedTopProc = res;
                return res;
            }
            catch
            {
                return cachedTopProc;
            }
        }

        private static int GetThermalZoneTemperature()
        {
            // Use cached value if available and not stale (avoids WMI overhead every second)
            thermalZoneTick++;
            if (cachedThermalZoneTemp > 0 && (thermalZoneTick % THERMAL_ZONE_REFRESH_INTERVAL != 0))
            {
                return cachedThermalZoneTemp;
            }

            try
            {
                using (ManagementObjectSearcher s = new ManagementObjectSearcher(@"root\wmi", "SELECT CurrentTemperature FROM MSAcpi_ThermalZoneTemperature"))
                {
                    using (ManagementObjectCollection coll = s.Get())
                    {
                        foreach (ManagementObject obj in coll)
                        {
                            using (obj)
                            {
                                double kelvin10 = Convert.ToDouble(obj["CurrentTemperature"]);
                                int celsius = (int)((kelvin10 / 10.0) - 273.15);
                                if (celsius >= 20 && celsius <= 115)
                                {
                                    cachedThermalZoneTemp = celsius;
                                    return celsius;
                                }
                            }
                        }
                    }
                }
            }
            catch { }
            return (cachedThermalZoneTemp > 0) ? cachedThermalZoneTemp : -1;
        }

        public static SensorData ReadSensors(PerformanceCounter cpuCounter)
        {
            SensorData data = new SensorData();
            data.GpuThrottleStatus = "OPTIMAL";
            data.VramTotalGb = 6.0;

            bool isSensorEstimated = false;

            if (DetectedVendor == HardwareVendor.Asus)
            {
                int asusCpuRpm = AsusAcpiBridge.GetCpuFanRpm();
                int asusGpuRpm = AsusAcpiBridge.GetGpuFanRpm();
                int asusCpuTemp = AsusAcpiBridge.GetCpuTemperature();

                data.CpuRpm = asusCpuRpm;
                data.GpuRpm = asusGpuRpm;
                data.CpuDuty = (asusCpuRpm > 0) ? Math.Min(100, Math.Max(20, (asusCpuRpm * 100) / 5400)) : 0;
                data.GpuDuty = (asusGpuRpm > 0) ? Math.Min(100, Math.Max(20, (asusGpuRpm * 100) / 5400)) : 0;

                if (asusCpuTemp >= 20 && asusCpuTemp <= 115)
                {
                    data.CpuTemp = asusCpuTemp;
                }
                else
                {
                    int tzTemp = GetThermalZoneTemperature();
                    data.CpuTemp = (tzTemp >= 20 && tzTemp <= 115) ? tzTemp : 45;
                    isSensorEstimated = true;
                }
                data.GpuTemp = 35; // Initial baseline before NVML reads it
            }
            else if (DetectedVendor == HardwareVendor.Acer && AcerInsydeBridge.IsAvailable)
            {
                lock (ecLock)
                {
                    int cRpm = 0, gRpm = 0, cDuty = 0, gDuty = 0, cTemp = 0, gTemp = 0;
                    if (AcerInsydeBridge.ReadSensors(ref cRpm, ref gRpm, ref cDuty, ref gDuty, ref cTemp, ref gTemp))
                    {
                        data.CpuRpm = cRpm;
                        data.GpuRpm = gRpm;
                        data.CpuDuty = cDuty;
                        data.GpuDuty = gDuty;
                        data.CpuTemp = cTemp;
                        data.GpuTemp = gTemp;
                    }
                    else
                    {
                        int tzTemp = GetThermalZoneTemperature();
                        data.CpuTemp = (tzTemp >= 20 && tzTemp <= 115) ? tzTemp : 45;
                        data.GpuTemp = 35;
                        data.CpuRpm = 1000;
                        data.GpuRpm = 0;
                        isSensorEstimated = true;
                    }
                }
            }
            else
            {
                // Lenovo, HP, Dell, or Universal laptops
                int tzTemp = GetThermalZoneTemperature();
                data.CpuTemp = (tzTemp >= 20 && tzTemp <= 115) ? tzTemp : 48;
                data.GpuTemp = 38;
                isSensorEstimated = true;

                // Calibrated fan telemetry estimation based on active mode & thermal demand
                if (CurrentFanMode == 1) // MAX / Turbo
                {
                    data.CpuDuty = 100;
                    data.GpuDuty = 100;
                    data.CpuRpm = 5000 + (data.CpuTemp > 70 ? 400 : 0);
                    data.GpuRpm = 5000 + (data.CpuTemp > 70 ? 400 : 0);
                }
                else if (CurrentFanMode == 3) // QUIET / Silent
                {
                    data.CpuDuty = 35;
                    data.GpuDuty = 30;
                    data.CpuRpm = 2000;
                    data.GpuRpm = 1800;
                }
                else if (CurrentFanMode == 6) // MANUAL / Custom
                {
                    data.CpuDuty = CurrentCustomDuty;
                    data.GpuDuty = CurrentCustomDuty;
                    data.CpuRpm = Math.Max(1500, CurrentCustomDuty * 52);
                    data.GpuRpm = Math.Max(1500, CurrentCustomDuty * 50);
                }
                else // AUTO / Adaptive Balanced
                {
                    int autoDuty = Math.Min(95, Math.Max(25, 20 + (int)((data.CpuTemp - 35) * 1.3)));
                    data.CpuDuty = autoDuty;
                    data.GpuDuty = autoDuty;
                    data.CpuRpm = Math.Max(1600, autoDuty * 48);
                    data.GpuRpm = Math.Max(1500, autoDuty * 46);
                }
            }

            if (nvmlReady)
            {
                try
                {
                    nvmlUtilization_t util;
                    int uRes = nvmlDeviceGetUtilizationRates(nvmlDevice, out util);
                    if (uRes == 0)
                    {
                        data.GpuUsage = util.gpu;
                    }

                    uint nTemp;
                    int tRes = nvmlDeviceGetTemperature(nvmlDevice, 0, out nTemp);
                    if (tRes == 0 && nTemp > 0 && nTemp < 115)
                    {
                        data.GpuTemp = (int)nTemp;
                    }

                    nvmlMemory_t mem;
                    if (nvmlDeviceGetMemoryInfo(nvmlDevice, out mem) == 0 && mem.total > 0)
                    {
                        data.VramTotalGb = mem.total / (1024.0 * 1024.0 * 1024.0);
                        data.VramUsedGb = mem.used / (1024.0 * 1024.0 * 1024.0);
                        data.VramFreeGb = mem.free / (1024.0 * 1024.0 * 1024.0);
                        data.VramUsage = (float)((mem.used / (double)mem.total) * 100.0);
                    }

                    uint gClock;
                    if (nvmlDeviceGetClockInfo(nvmlDevice, 0, out gClock) == 0)
                    {
                        data.GpuCoreClock = gClock;
                    }
                    uint mClock;
                    if (nvmlDeviceGetClockInfo(nvmlDevice, 2, out mClock) == 0)
                    {
                        data.GpuMemClock = mClock;
                    }

                    ulong reasons;
                    if (nvmlDeviceGetCurrentClocksThrottleReasons(nvmlDevice, out reasons) == 0)
                    {
                        data.GpuThrottleReasons = reasons;
                        if ((reasons & 0x40) != 0 || (reasons & 0x20) != 0)
                            data.GpuThrottleStatus = "THERMAL THROTTLE";
                        else if ((reasons & 0x04) != 0 || (reasons & 0x80) != 0)
                            data.GpuThrottleStatus = "POWER CAP (TGP)";
                        else if ((reasons & 0x01) != 0)
                            data.GpuThrottleStatus = "IDLE (POWER SAVE)";
                        else
                            data.GpuThrottleStatus = "UNCONSTRAINED";
                    }

                    uint pGen;
                    if (nvmlDeviceGetCurrPcieLinkGeneration(nvmlDevice, out pGen) == 0)
                        data.PcieGen = pGen;
                    uint pWidth;
                    if (nvmlDeviceGetCurrPcieLinkWidth(nvmlDevice, out pWidth) == 0)
                        data.PcieWidth = pWidth;

                    uint pMw;
                    if (nvmlDeviceGetPowerUsage(nvmlDevice, out pMw) == 0 && pMw > 0)
                    {
                        uint maxLimit = (nvmlPowerLimitMw > 0) ? (uint)(nvmlPowerLimitMw * 1.35) : 180000;
                        if (pMw <= maxLimit && pMw < 250000)
                        {
                            data.GpuWatts = (float)Math.Round(pMw / 1000.0, 1);
                        }
                        else
                        {
                            if (data.GpuUsage <= 1)
                            {
                                data.GpuWatts = 0f;
                            }
                            else
                            {
                                data.GpuWatts = (float)Math.Round(12.0f + (data.GpuUsage * 0.70f), 1);
                            }
                        }
                    }
                    else if (data.GpuUsage > 0)
                    {
                        data.GpuWatts = (float)Math.Round(12.0f + (data.GpuUsage * 0.70f), 1);
                    }
                }
                catch
                {
                    data.GpuUsage = 0;
                }
            }
            else
            {
                data.GpuUsage = 0;
            }

            // Update estimated sensor flag for UI transparency
            EcAntiHallucinationEngine.IsEstimatedSensor = isSensorEstimated;

            try
            {
                if (cpuCounter != null)
                {
                    data.CpuUsage = cpuCounter.NextValue();
                }
            }
            catch { }

            try
            {
                if (!cpuPowerInitialized)
                {
                    cpuPowerInitialized = true;
                    if (PerformanceCounterCategory.Exists("Energy Meter"))
                    {
                        PerformanceCounterCategory cat = new PerformanceCounterCategory("Energy Meter");
                        string[] instances = cat.GetInstanceNames();
                        string targetInst = null;
                        foreach (string inst in instances)
                        {
                            if (inst.IndexOf("pkg", StringComparison.OrdinalIgnoreCase) >= 0)
                            {
                                targetInst = inst;
                                break;
                            }
                        }
                        if (targetInst == null && instances.Length > 0) targetInst = instances[0];
                        if (targetInst != null)
                        {
                            cpuPowerCounter = new PerformanceCounter("Energy Meter", "Power", targetInst);
                            cpuPowerCounter.NextValue();
                        }
                    }
                }

                if (cpuPowerCounter != null)
                {
                    float mw = cpuPowerCounter.NextValue();
                    if (mw > 0 && mw < 200000)
                    {
                        data.CpuWatts = (float)Math.Round(mw / 1000.0, 1);
                    }
                }
            }
            catch { }

            if (data.CpuWatts <= 0 && data.CpuUsage > 0)
            {
                data.CpuWatts = (float)Math.Round(15.0f + (data.CpuUsage * 0.45f), 1);
            }

            try
            {
                MEMORYSTATUSEX mem = new MEMORYSTATUSEX();
                mem.dwLength = (uint)Marshal.SizeOf(typeof(MEMORYSTATUSEX));
                if (GlobalMemoryStatusEx(ref mem))
                {
                    data.RamUsage = mem.dwMemoryLoad;
                    data.TotalRamGb = mem.ullTotalPhys / (1024.0 * 1024.0 * 1024.0);
                    data.UsedRamGb = (mem.ullTotalPhys - mem.ullAvailPhys) / (1024.0 * 1024.0 * 1024.0);
                }
            }
            catch { }

            try
            {
                SYSTEM_POWER_STATUS pStatus;
                if (GetSystemPowerStatus(out pStatus))
                {
                    data.IsAcPlugged = (pStatus.ACLineStatus == 1);
                    data.BatteryPercent = pStatus.BatteryLifePercent;
                }
            }
            catch { }

            data.BatteryDesignMwh = CachedBatteryDesignMwh;
            data.BatteryFullMwh = CachedBatteryFullMwh;
            data.BatteryHealthPct = (CachedBatteryDesignMwh > 0)
                ? Math.Min(100.0, (CachedBatteryFullMwh / (double)CachedBatteryDesignMwh) * 100.0)
                : 100.0;
            data.SystemUptime = TimeSpan.FromMilliseconds(GetTickCount64());

            return data;
        }
    }
}
