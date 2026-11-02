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
    public enum HardwareVendor
    {
        Acer,
        Asus,
        Lenovo,
        Hp,
        Dell,
        Universal
    }

    // ==========================================
    // ACER DIRECT INSYDE H2O HARDWARE BRIDGE
    // Supports Acer Aspire, Nitro & Predator laptops
    // Native communication via Insyde EC buffer protocol (InsydeDCHU.dll)
    // ==========================================
    public static class AcerInsydeBridge
    {
        [DllImport("InsydeDCHU.dll", CallingConvention = CallingConvention.StdCall)]
        public static extern int SetDCHU_Data(int command, byte[] buffer, int length);

        [DllImport("InsydeDCHU.dll", CallingConvention = CallingConvention.StdCall)]
        public static extern int GetDCHU_Data_Buffer(int command, [In, Out] byte[] buffer);

        private static bool? isDllPresent = null;
        public static bool IsAvailable
        {
            get
            {
                if (!isDllPresent.HasValue)
                {
                    string dir1 = AppDomain.CurrentDomain.BaseDirectory;
                    string dir2 = "";
                    try { dir2 = System.IO.Path.GetDirectoryName(System.Reflection.Assembly.GetExecutingAssembly().Location); } catch { }
                    isDllPresent = (File.Exists(System.IO.Path.Combine(dir1, "InsydeDCHU.dll")) ||
                                    (!string.IsNullOrEmpty(dir2) && File.Exists(System.IO.Path.Combine(dir2, "InsydeDCHU.dll"))) ||
                                    File.Exists("InsydeDCHU.dll"));
                }
                return isDllPresent.Value;
            }
        }

        public static bool ResetFans()
        {
            if (!IsAvailable) return false;
            try
            {
                byte[] bufDefault = new byte[] { 1, 0, 0, 34 };
                SetDCHU_Data(121, bufDefault, 4);

                byte[] bufOffset = new byte[] { 0, 0, 0, 14 };
                SetDCHU_Data(121, bufOffset, 4);

                byte[] bufMode = new byte[] { 0, 0, 0, 1 };
                int res = SetDCHU_Data(121, bufMode, 4);
                return res == 121;
            }
            catch { return false; }
        }

        public static bool SetFanMode(int mode)
        {
            if (!IsAvailable) return false;
            try
            {
                if (mode == 0) return ResetFans();
                if (mode != 1 && mode != 3 && mode != 6) return false;

                byte[] buffer = new byte[4];
                buffer[0] = (byte)mode;
                buffer[1] = 0;
                buffer[2] = 0;
                buffer[3] = 1;
                int res = SetDCHU_Data(121, buffer, 4);
                return res == 121;
            }
            catch { return false; }
        }

        public static bool SetCustomSpeed(int dutyPercentage)
        {
            if (!IsAvailable) return false;
            try
            {
                byte[] table = new byte[256];
                int readRes = GetDCHU_Data_Buffer(13, table);
                if (readRes != 13 || table[12] < 1 || table[16] < 25 || table[22] > 115) return false;

                dutyPercentage = Math.Max(30, Math.Min(100, dutyPercentage));
                byte rawDuty = (byte)((dutyPercentage * 255) / 100);

                table[17] = rawDuty;
                table[19] = rawDuty;
                table[21] = rawDuty;

                table[25] = rawDuty;
                table[27] = rawDuty;
                table[29] = rawDuty;

                table[23] = 255;
                table[31] = 255;

                SetDCHU_Data(14, table, 256);

                byte[] modeBuf = new byte[] { 6, 0, 0, 1 };
                SetDCHU_Data(121, modeBuf, 4);

                byte[] offsetBuf = new byte[] { rawDuty, 0, 0, 14 };
                SetDCHU_Data(121, offsetBuf, 4);
                return true;
            }
            catch { return false; }
        }

        public static bool ReadSensors(ref int cpuRpm, ref int gpuRpm, ref int cpuDuty, ref int gpuDuty, ref int cpuTemp, ref int gpuTemp)
        {
            if (!IsAvailable) return false;
            try
            {
                byte[] buf = new byte[256];
                int res = GetDCHU_Data_Buffer(12, buf);
                if (res == 12)
                {
                    int rawCpuRpm = (buf[2] << 8) | buf[3];
                    int rawGpuRpm = (buf[4] << 8) | buf[5];
                    cpuRpm = (rawCpuRpm > 0 && rawCpuRpm < 9000) ? rawCpuRpm : 0;
                    gpuRpm = (rawGpuRpm > 0 && rawGpuRpm < 9000) ? rawGpuRpm : 0;
                    cpuDuty = Math.Min(100, Math.Max(0, (int)Math.Round((buf[16] / 255.0) * 100)));
                    gpuDuty = Math.Min(100, Math.Max(0, (int)Math.Round((buf[19] / 255.0) * 100)));
                    int rawCpuTemp = buf[18];
                    int rawGpuTemp = buf[21];
                    cpuTemp = (rawCpuTemp >= 20 && rawCpuTemp <= 115) ? rawCpuTemp : 45;
                    gpuTemp = (rawGpuTemp >= 20 && rawGpuTemp <= 115) ? rawGpuTemp : 35;
                    return true;
                }
            }
            catch { }
            return false;
        }
    }

    // ==========================================
    // ASUS ACPI / WMI DIRECT HARDWARE BRIDGE
    // Native communication with ASUS TUF Gaming & ROG laptops (ATKACPI / AsusAtkWmi_WMNB)
    // Zero external dependencies: uses Win32 DeviceIoControl and System.Management
    // ==========================================
    public static class AsusAcpiBridge
    {
        private const string FILE_NAME = @"\\.\ATKACPI";
        private const uint CONTROL_CODE = 0x0022240C;

        private const uint DSTS = 0x53545344;
        private const uint DEVS = 0x53564544;
        private const uint INIT = 0x54494E49;

        // ASUS Hardware Endpoints
        public const uint PerformanceMode = 0x00120075; // TUF / ROG Performance modes (0=Balanced, 1=Turbo, 2=Silent)
        public const uint VivoBookMode    = 0x00110019; // Vivobook/Zenbook fallback (0=Standard, 2=Performance, 1=Whisper)
        public const uint CPU_Fan         = 0x00110013; // CPU Fan status & RPM
        public const uint GPU_Fan         = 0x00110014; // GPU Fan status & RPM
        public const uint Mid_Fan         = 0x00110031; // Middle Fan status & RPM
        public const int  Temp_CPU        = 0x00120094; // CPU Package Temperature
        public const int  Temp_GPU        = 0x00120097; // GPU Temperature
        public const uint DevsCPUFanCurve = 0x00110024; // CPU Fan curve points (16 bytes)
        public const uint DevsGPUFanCurve = 0x00110025; // GPU Fan curve points (16 bytes)

        [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        private static extern IntPtr CreateFile(
            string lpFileName,
            uint dwDesiredAccess,
            uint dwShareMode,
            IntPtr lpSecurityAttributes,
            uint dwCreationDisposition,
            uint dwFlagsAndAttributes,
            IntPtr hTemplateFile
        );

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool DeviceIoControl(
            IntPtr hDevice,
            uint dwIoControlCode,
            byte[] lpInBuffer,
            uint nInBufferSize,
            byte[] lpOutBuffer,
            uint nOutBufferSize,
            ref uint lpBytesReturned,
            IntPtr lpOverlapped
        );

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool CloseHandle(IntPtr hObject);

        private const uint GENERIC_READ = 0x80000000;
        private const uint GENERIC_WRITE = 0x40000000;
        private const uint OPEN_EXISTING = 3;
        private const uint FILE_ATTRIBUTE_NORMAL = 0x80;
        private const uint FILE_SHARE_READ = 1;
        private const uint FILE_SHARE_WRITE = 2;

        private static IntPtr handle = new IntPtr(-1);
        private static bool isAvailable = false;
        private static bool isWmiAvailable = false;
        private static readonly object acpiLock = new object();

        public static bool IsAvailable
        {
            get { return isAvailable || isWmiAvailable; }
        }

        public static bool Initialize()
        {
            lock (acpiLock)
            {
                try
                {
                    handle = CreateFile(
                        FILE_NAME,
                        GENERIC_READ | GENERIC_WRITE,
                        FILE_SHARE_READ | FILE_SHARE_WRITE,
                        IntPtr.Zero,
                        OPEN_EXISTING,
                        FILE_ATTRIBUTE_NORMAL,
                        IntPtr.Zero
                    );

                    if (handle != IntPtr.Zero && handle != new IntPtr(-1))
                    {
                        isAvailable = true;
                        return true;
                    }
                }
                catch
                {
                    handle = new IntPtr(-1);
                }

                // Fallback: Check if ASUS WMI provider is installed
                try
                {
                    using (ManagementObjectSearcher searcher = new ManagementObjectSearcher(@"root\wmi", "SELECT * FROM AsusAtkWmi_WMNB"))
                    {
                        using (ManagementObjectCollection coll = searcher.Get())
                        {
                            if (coll != null && coll.Count > 0)
                            {
                                isWmiAvailable = true;
                                return true;
                            }
                        }
                    }
                }
                catch
                {
                    isWmiAvailable = false;
                }

                return false;
            }
        }

        private static byte[] CallMethod(uint methodId, byte[] args)
        {
            if (handle == IntPtr.Zero || handle == new IntPtr(-1))
                return new byte[16];

            byte[] acpiBuf = new byte[8 + args.Length];
            byte[] outBuffer = new byte[16];

            BitConverter.GetBytes(methodId).CopyTo(acpiBuf, 0);
            BitConverter.GetBytes((uint)args.Length).CopyTo(acpiBuf, 4);
            Array.Copy(args, 0, acpiBuf, 8, args.Length);

            uint lpBytesReturned = 0;
            DeviceIoControl(
                handle,
                CONTROL_CODE,
                acpiBuf,
                (uint)acpiBuf.Length,
                outBuffer,
                (uint)outBuffer.Length,
                ref lpBytesReturned,
                IntPtr.Zero
            );

            return outBuffer;
        }

        public static int DeviceSet(uint deviceId, int status)
        {
            lock (acpiLock)
            {
                if (isAvailable && handle != new IntPtr(-1))
                {
                    try
                    {
                        byte[] args = new byte[8];
                        BitConverter.GetBytes(deviceId).CopyTo(args, 0);
                        BitConverter.GetBytes((uint)status).CopyTo(args, 4);

                        byte[] outBuf = CallMethod(DEVS, args);
                        return BitConverter.ToInt32(outBuf, 0);
                    }
                    catch { }
                }

                if (isWmiAvailable)
                {
                    try
                    {
                        using (ManagementObjectSearcher searcher = new ManagementObjectSearcher(@"root\wmi", "SELECT * FROM AsusAtkWmi_WMNB"))
                        {
                            using (ManagementObjectCollection coll = searcher.Get())
                            {
                                foreach (ManagementObject obj in coll)
                                {
                                    using (obj)
                                    {
                                        using (ManagementBaseObject inParams = obj.GetMethodParameters("DEVS"))
                                        {
                                            inParams["Device_ID"] = deviceId;
                                            inParams["Control_status"] = (uint)status;
                                            using (ManagementBaseObject outParams = obj.InvokeMethod("DEVS", inParams, null))
                                            {
                                                if (outParams != null)
                                                    return Convert.ToInt32(outParams["result"]);
                                            }
                                        }
                                    }
                                }
                            }
                        }
                    }
                    catch { }
                }

                return -1;
            }
        }

        public static int DeviceSet(uint deviceId, byte[] parameters)
        {
            lock (acpiLock)
            {
                if (isAvailable && handle != new IntPtr(-1))
                {
                    try
                    {
                        byte[] args = new byte[4 + parameters.Length];
                        BitConverter.GetBytes(deviceId).CopyTo(args, 0);
                        parameters.CopyTo(args, 4);

                        byte[] outBuf = CallMethod(DEVS, args);
                        return BitConverter.ToInt32(outBuf, 0);
                    }
                    catch { }
                }
                return -1;
            }
        }

        public static int DeviceGet(uint deviceId)
        {
            lock (acpiLock)
            {
                if (isAvailable && handle != new IntPtr(-1))
                {
                    try
                    {
                        byte[] args = new byte[8];
                        BitConverter.GetBytes(deviceId).CopyTo(args, 0);
                        byte[] outBuf = CallMethod(DSTS, args);

                        return BitConverter.ToInt32(outBuf, 0) - 65536;
                    }
                    catch { }
                }

                if (isWmiAvailable)
                {
                    try
                    {
                        using (ManagementObjectSearcher searcher = new ManagementObjectSearcher(@"root\wmi", "SELECT * FROM AsusAtkWmi_WMNB"))
                        {
                            using (ManagementObjectCollection coll = searcher.Get())
                            {
                                foreach (ManagementObject obj in coll)
                                {
                                    using (obj)
                                    {
                                        using (ManagementBaseObject inParams = obj.GetMethodParameters("DSTS"))
                                        {
                                            inParams["Device_ID"] = deviceId;
                                            using (ManagementBaseObject outParams = obj.InvokeMethod("DSTS", inParams, null))
                                            {
                                                if (outParams != null)
                                                    return Convert.ToInt32(outParams["result"]) - 65536;
                                            }
                                        }
                                    }
                                }
                            }
                        }
                    }
                    catch { }
                }

                return -1;
            }
        }

        public static bool SetPerformanceMode(int mode)
        {
            // 0 = Balanced, 1 = Turbo, 2 = Silent
            int res = DeviceSet(PerformanceMode, mode);
            if (res != 1)
            {
                // Vivobook fallback: 0 = Standard, 2 = Performance, 1 = Whisper
                int vivoMode = mode;
                if (mode == 1) vivoMode = 2;
                else if (mode == 2) vivoMode = 1;
                res = DeviceSet(VivoBookMode, vivoMode);
            }
            return res == 1;
        }

        public static int GetFanRpm(uint endpoint)
        {
            int raw = DeviceGet(endpoint);
            if (raw < 0) return 0;
            int val = raw & 0xFFFF;
            if (val <= 0 || val >= 10000) return 0;
            if (val <= 120) return val * 100;
            return val;
        }

        public static int GetCpuFanRpm()
        {
            return GetFanRpm(CPU_Fan);
        }

        public static int GetGpuFanRpm()
        {
            return GetFanRpm(GPU_Fan);
        }

        public static int GetCpuTemperature()
        {
            int temp = DeviceGet((uint)Temp_CPU);
            if (temp >= 20 && temp <= 115) return temp;
            return -1;
        }

        public static bool SetCustomDuty(int dutyPercentage)
        {
            dutyPercentage = Math.Max(30, Math.Min(100, dutyPercentage));

            if (dutyPercentage >= 80)
            {
                SetPerformanceMode(1); // Turbo
            }
            else if (dutyPercentage <= 45)
            {
                SetPerformanceMode(2); // Silent
            }
            else
            {
                SetPerformanceMode(0); // Balanced
            }

            try
            {
                byte duty = (byte)dutyPercentage;
                byte p1 = (byte)Math.Max(20, duty * 50 / 100);
                byte p2 = (byte)Math.Max(25, duty * 70 / 100);
                byte p3 = duty;
                byte[] curve = new byte[16] {
                    40, 50, 60, 70, 75, 80, 88, 95,
                    p1, p1, p2, p3, p3, duty, 100, 100
                };

                DeviceSet(DevsCPUFanCurve, curve);
                DeviceSet(DevsGPUFanCurve, curve);
                return true;
            }
            catch
            {
                return true;
            }
        }

        public static bool ResetFans()
        {
            return SetPerformanceMode(0);
        }

        public static void Close()
        {
            lock (acpiLock)
            {
                try
                {
                    ResetFans();
                    if (handle != IntPtr.Zero && handle != new IntPtr(-1))
                    {
                        CloseHandle(handle);
                        handle = new IntPtr(-1);
                    }
                    isAvailable = false;
                    isWmiAvailable = false;
                }
                catch { }
            }
        }
    }

    // ==========================================
    // LENOVO DIRECT ACPI / WMI HARDWARE BRIDGE
    // Supports Lenovo Legion, LOQ, IdeaPad Gaming & ThinkPad laptops
    // ==========================================
    public static class LenovoWmiBridge
    {
        private static bool isAvailable = false;
        public static bool IsAvailable { get { return isAvailable; } }

        public static bool Initialize()
        {
            try
            {
                using (ManagementObjectSearcher s = new ManagementObjectSearcher(@"root\wmi", "SELECT * FROM LNVPC_WMI"))
                {
                    using (ManagementObjectCollection coll = s.Get())
                    {
                        if (coll != null && coll.Count > 0)
                        {
                            isAvailable = true;
                            return true;
                        }
                    }
                }
            }
            catch { }

            try
            {
                using (ManagementObjectSearcher s = new ManagementObjectSearcher(@"root\wmi", "SELECT * FROM Lenovo_Vantage_EC"))
                {
                    using (ManagementObjectCollection coll = s.Get())
                    {
                        if (coll != null && coll.Count > 0)
                        {
                            isAvailable = true;
                            return true;
                        }
                    }
                }
            }
            catch { }

            return false;
        }

        public static bool SetPerformanceMode(int mode)
        {
            // mode: 0=Auto (2=Balanced), 1=Max (3=Performance), 3=Quiet (1=Quiet), 6=Custom (4)
            uint lenovoMode = 2; // Balanced
            if (mode == 1) lenovoMode = 3;      // Performance
            else if (mode == 3) lenovoMode = 1; // Quiet
            else if (mode == 6) lenovoMode = 4; // Custom

            try
            {
                using (ManagementObjectSearcher s = new ManagementObjectSearcher(@"root\wmi", "SELECT * FROM LNVPC_WMI"))
                {
                    using (ManagementObjectCollection coll = s.Get())
                    {
                        foreach (ManagementObject obj in coll)
                        {
                            using (obj)
                            {
                                using (ManagementBaseObject inParams = obj.GetMethodParameters("SetSmartFanMode"))
                                {
                                    inParams["Mode"] = lenovoMode;
                                    using (ManagementBaseObject outParams = obj.InvokeMethod("SetSmartFanMode", inParams, null))
                                    {
                                        return true;
                                    }
                                }
                            }
                        }
                    }
                }
            }
            catch { }

            try
            {
                using (ManagementObjectSearcher s = new ManagementObjectSearcher(@"root\wmi", "SELECT * FROM Lenovo_Vantage_EC"))
                {
                    using (ManagementObjectCollection coll = s.Get())
                    {
                        foreach (ManagementObject obj in coll)
                        {
                            using (obj)
                            {
                                using (ManagementBaseObject inParams = obj.GetMethodParameters("SetPerformanceMode"))
                                {
                                    inParams["Mode"] = lenovoMode;
                                    using (ManagementBaseObject outParams = obj.InvokeMethod("SetPerformanceMode", inParams, null))
                                    {
                                        return true;
                                    }
                                }
                            }
                        }
                    }
                }
            }
            catch { }

            return false;
        }

        public static bool SetCustomDuty(int dutyPercentage)
        {
            if (dutyPercentage >= 75) return SetPerformanceMode(1);
            if (dutyPercentage <= 45) return SetPerformanceMode(3);
            return SetPerformanceMode(0);
        }
    }

    // ==========================================
    // HP DIRECT WMI HARDWARE BRIDGE
    // Supports HP OMEN, Victus & Pavilion Gaming laptops
    // ==========================================
    public static class HpWmiBridge
    {
        private static bool isAvailable = false;
        public static bool IsAvailable { get { return isAvailable; } }

        public static bool Initialize()
        {
            try
            {
                using (ManagementObjectSearcher s = new ManagementObjectSearcher(@"root\wmi", "SELECT * FROM hpqBIntM"))
                {
                    using (ManagementObjectCollection coll = s.Get())
                    {
                        if (coll != null && coll.Count > 0)
                        {
                            isAvailable = true;
                            return true;
                        }
                    }
                }
            }
            catch { }

            try
            {
                using (ManagementObjectSearcher s = new ManagementObjectSearcher(@"root\wmi", "SELECT * FROM HP_BiosSetting"))
                {
                    using (ManagementObjectCollection coll = s.Get())
                    {
                        if (coll != null && coll.Count > 0)
                        {
                            isAvailable = true;
                            return true;
                        }
                    }
                }
            }
            catch { }

            return false;
        }

        public static bool SetThermalMode(int mode)
        {
            // mode: 0=Auto (0=Balanced), 1=Max (1=Performance), 3=Quiet (2=Cool)
            uint hpMode = 0;
            if (mode == 1) hpMode = 1;
            else if (mode == 3) hpMode = 2;

            try
            {
                using (ManagementObjectSearcher s = new ManagementObjectSearcher(@"root\wmi", "SELECT * FROM hpqBIntM"))
                {
                    using (ManagementObjectCollection coll = s.Get())
                    {
                        foreach (ManagementObject obj in coll)
                        {
                            using (obj)
                            {
                                using (ManagementBaseObject inParams = obj.GetMethodParameters("BiosControl"))
                                {
                                    inParams["Command"] = (uint)0x20008;
                                    inParams["Input"] = hpMode;
                                    using (ManagementBaseObject outParams = obj.InvokeMethod("BiosControl", inParams, null))
                                    {
                                        return true;
                                    }
                                }
                            }
                        }
                    }
                }
            }
            catch { }

            return false;
        }
    }

    // ==========================================
    // DELL DIRECT WMAX / SMBIOS HARDWARE BRIDGE
    // Supports Dell Alienware, G-Series (G15/G16) & Inspiron laptops
    // ==========================================
    public static class DellWmiBridge
    {
        private static bool isAvailable = false;
        public static bool IsAvailable { get { return isAvailable; } }

        public static bool Initialize()
        {
            try
            {
                using (ManagementObjectSearcher s = new ManagementObjectSearcher(@"root\wmi", "SELECT * FROM Dell_ThermalInformation"))
                {
                    using (ManagementObjectCollection coll = s.Get())
                    {
                        if (coll != null && coll.Count > 0)
                        {
                            isAvailable = true;
                            return true;
                        }
                    }
                }
            }
            catch { }

            try
            {
                using (ManagementObjectSearcher s = new ManagementObjectSearcher(@"root\wmi", "SELECT * FROM Alienware_Platform"))
                {
                    using (ManagementObjectCollection coll = s.Get())
                    {
                        if (coll != null && coll.Count > 0)
                        {
                            isAvailable = true;
                            return true;
                        }
                    }
                }
            }
            catch { }

            return false;
        }

        public static bool SetThermalMode(int mode)
        {
            // mode: 0=Auto (0=Balanced), 1=Max (1=Performance / G-Mode), 3=Quiet (2=Quiet)
            uint dellMode = 0;
            if (mode == 1) dellMode = 1;
            else if (mode == 3) dellMode = 2;

            try
            {
                using (ManagementObjectSearcher s = new ManagementObjectSearcher(@"root\wmi", "SELECT * FROM Dell_ThermalInformation"))
                {
                    using (ManagementObjectCollection coll = s.Get())
                    {
                        foreach (ManagementObject obj in coll)
                        {
                            using (obj)
                            {
                                using (ManagementBaseObject inParams = obj.GetMethodParameters("SetThermalProfile"))
                                {
                                    inParams["Profile"] = dellMode;
                                    using (ManagementBaseObject outParams = obj.InvokeMethod("SetThermalProfile", inParams, null))
                                    {
                                        return true;
                                    }
                                }
                            }
                        }
                    }
                }
            }
            catch { }

            return false;
        }
    }

    // ==========================================
    // HARDWARE BRIDGE: Multi-vendor telemetry & dispatch
    // ==========================================
    public static class HardwareBridge
    {

        // NVIDIA NVML API for exact GPU utilization matching Task Manager
        [DllImport("nvml.dll", EntryPoint = "nvmlInit_v2")]
        public static extern int nvmlInit();

        [DllImport("nvml.dll", EntryPoint = "nvmlShutdown")]
        public static extern int nvmlShutdown();

        [DllImport("nvml.dll", EntryPoint = "nvmlDeviceGetHandleByIndex_v2")]
        public static extern int nvmlDeviceGetHandleByIndex(uint index, out IntPtr device);

        [DllImport("nvml.dll", EntryPoint = "nvmlDeviceGetName")]
        public static extern int nvmlDeviceGetName(IntPtr device, StringBuilder name, uint length);

        [StructLayout(LayoutKind.Sequential)]
        public struct nvmlUtilization_t
        {
            public uint gpu;
            public uint memory;
        }

        [DllImport("nvml.dll", EntryPoint = "nvmlDeviceGetUtilizationRates")]
        public static extern int nvmlDeviceGetUtilizationRates(IntPtr device, out nvmlUtilization_t utilization);

        [DllImport("nvml.dll", EntryPoint = "nvmlDeviceGetTemperature")]
        public static extern int nvmlDeviceGetTemperature(IntPtr device, int sensorType, out uint temp);

        [DllImport("nvml.dll", EntryPoint = "nvmlDeviceGetPowerUsage")]
        public static extern int nvmlDeviceGetPowerUsage(IntPtr device, out uint powerMw);

        [DllImport("nvml.dll", EntryPoint = "nvmlDeviceGetEnforcedPowerLimit")]
        public static extern int nvmlDeviceGetEnforcedPowerLimit(IntPtr device, out uint limitMw);

        [StructLayout(LayoutKind.Sequential)]
        public struct nvmlMemory_t
        {
            public ulong total;
            public ulong free;
            public ulong used;
        }

        [DllImport("nvml.dll", EntryPoint = "nvmlDeviceGetMemoryInfo")]
        public static extern int nvmlDeviceGetMemoryInfo(IntPtr device, out nvmlMemory_t memory);

        [DllImport("nvml.dll", EntryPoint = "nvmlDeviceGetClockInfo")]
        public static extern int nvmlDeviceGetClockInfo(IntPtr device, int type, out uint clockMHz);

        [DllImport("nvml.dll", EntryPoint = "nvmlDeviceGetCurrentClocksThrottleReasons")]
        public static extern int nvmlDeviceGetCurrentClocksThrottleReasons(IntPtr device, out ulong reasons);

        [DllImport("nvml.dll", EntryPoint = "nvmlDeviceGetCurrPcieLinkGeneration")]
        public static extern int nvmlDeviceGetCurrPcieLinkGeneration(IntPtr device, out uint currLinkGen);

        [DllImport("nvml.dll", EntryPoint = "nvmlDeviceGetCurrPcieLinkWidth")]
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
        public static extern bool GetSystemPowerStatus(out SYSTEM_POWER_STATUS lp);

        [DllImport("kernel32.dll")]
        public static extern ulong GetTickCount64();

        [DllImport("psapi.dll")]
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
                                        if (sName.Contains("NVIDIA") || sName.Contains("GeForce") || sName.Contains("RTX") || sName.Contains("GTX") || sName.Contains("Radeon") || sName.Contains("Arc"))
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

                        if (combined.Contains("asus") || combined.Contains("tuf") || combined.Contains("rog"))
                        {
                            DetectedVendor = HardwareVendor.Asus;
                            ChassisModel = prodName.StartsWith("ASUS", StringComparison.OrdinalIgnoreCase) ? prodName : "ASUS " + prodName;
                        }
                        else if (combined.Contains("lenovo") || combined.Contains("legion") || combined.Contains("ideapad") || combined.Contains("loq"))
                        {
                            DetectedVendor = HardwareVendor.Lenovo;
                            ChassisModel = prodName.StartsWith("Lenovo", StringComparison.OrdinalIgnoreCase) ? prodName : "Lenovo " + prodName;
                        }
                        else if (combined.Contains("hp") || combined.Contains("hewlett") || combined.Contains("omen") || combined.Contains("victus"))
                        {
                            DetectedVendor = HardwareVendor.Hp;
                            ChassisModel = prodName.StartsWith("HP", StringComparison.OrdinalIgnoreCase) ? prodName : "HP " + prodName;
                        }
                        else if (combined.Contains("dell") || combined.Contains("alienware"))
                        {
                            DetectedVendor = HardwareVendor.Dell;
                            ChassisModel = (prodName.StartsWith("Dell", StringComparison.OrdinalIgnoreCase) || prodName.StartsWith("Alienware", StringComparison.OrdinalIgnoreCase)) ? prodName : "Dell " + prodName;
                        }
                        else if (combined.Contains("acer") || combined.Contains("aspire") || combined.Contains("nitro") || combined.Contains("predator"))
                        {
                            DetectedVendor = HardwareVendor.Acer;
                            ChassisModel = prodName.StartsWith("Acer", StringComparison.OrdinalIgnoreCase) ? prodName : "Acer " + prodName;
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

                // 3. Trim non-critical user-session processes without touching system drivers or anti-cheat protected processes
                int currentSession = Process.GetCurrentProcess().SessionId;
                Process[] procs = Process.GetProcesses();
                foreach (Process p in procs)
                {
                    try
                    {
                        if (p.SessionId == currentSession && p.Id != Process.GetCurrentProcess().Id)
                        {
                            string name = p.ProcessName.ToLowerInvariant();
                            if (!name.Contains("csrss") && !name.Contains("lsass") && !name.Contains("services") &&
                                !name.Contains("dwm") && !name.Contains("explorer") && !name.Contains("system") &&
                                !name.Contains("audiodg") && !name.Contains("nvvsvc") && !name.Contains("nvdisplay"))
                            {
                                EmptyWorkingSet(p.Handle);
                            }
                        }
                    }
                    catch { }
                    finally
                    {
                        try { p.Dispose(); } catch { }
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
                    finally
                    {
                        try { procs[i].Dispose(); } catch { }
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
                }
                data.GpuTemp = 35; // Default before NVML reads it
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
                    }
                }
            }
            else
            {
                // Lenovo, HP, Dell, or Universal laptops
                int tzTemp = GetThermalZoneTemperature();
                data.CpuTemp = (tzTemp >= 20 && tzTemp <= 115) ? tzTemp : 48;
                data.GpuTemp = 38;

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
                        // Sanity check: On mobile GPUs with Optimus/D3Cold sleep states,
                        // nvmlDeviceGetPowerUsage returns ~590006 mW (0x900B6) as an ACPI/EC dummy register value when idling or sleeping.
                        // Maximum possible mobile laptop GPU TGP is ~175W (and on RTX 4050 it is 60W-75W).
                        uint maxLimit = (nvmlPowerLimitMw > 0) ? (uint)(nvmlPowerLimitMw * 1.35) : 180000;
                        if (pMw <= maxLimit && pMw < 250000)
                        {
                            data.GpuWatts = (float)Math.Round(pMw / 1000.0, 1);
                        }
                        else
                        {
                            // 590,000+ mW indicates GPU is in deep idle/sleep (Optimus D3Cold)
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

    // ==========================================
    // EC ANTI-HALLUCINATION ARBITER ENGINE
    // ==========================================
    public static class EcAntiHallucinationEngine
    {
        public static bool IsEnabled = true;
        public static int AcousticClampDuty = 35; // Default 35% duty cycle (~2,000-2,200 RPM whisper acoustic floor)
        public static int SustainedThresholdSeconds = 4; // Default 4 seconds
        public static int FailsafeTemp = 86; // 86°C emergency override
        public static bool AutoUnclampGaming = true;
        public static bool PowerBoostTamed = false;

        // Live Telemetry State
        public static double RawCpuTemp = 0;
        public static double FilteredThermalEma = 0;
        public static double HeatsinkSaturationPct = 0;
        public static int InterceptedSpikeCount = 0;
        public static int SustainedLoadDurationSec = 0;
        public static string CurrentClassifierState = "Awaiting Sensor Data...";
        public static bool IsCurrentlyClamped = false;
        public static int CurrentAppliedMode = -1;

        private static List<string> decisionLogs = new List<string>();
        private static readonly object logLock = new object();

        public static void LogDecision(string entry)
        {
            lock (logLock)
            {
                string timeStr = DateTime.Now.ToString("HH:mm:ss");
                decisionLogs.Add(string.Format("[{0}] {1}", timeStr, entry));
                if (decisionLogs.Count > 60) decisionLogs.RemoveAt(0);
            }
        }

        public static List<string> GetRecentLogs(int count)
        {
            lock (logLock)
            {
                int start = Math.Max(0, decisionLogs.Count - count);
                return decisionLogs.GetRange(start, decisionLogs.Count - start);
            }
        }

        public static void ClearLogs()
        {
            lock (logLock)
            {
                decisionLogs.Clear();
                LogDecision("Decision stream cleared by user");
            }
        }

        public static void Toggle()
        {
            IsEnabled = !IsEnabled;
            if (!IsEnabled)
            {
                IsCurrentlyClamped = false;
                CurrentAppliedMode = 0;
                HardwareBridge.ResetToAuto();
                LogDecision("GUARD DISABLED: Manual toggle -> Restored factory EC Auto Mode");
            }
            else
            {
                CurrentAppliedMode = -1;
                LogDecision("GUARD ENABLED: Acoustic arbiter active -> Suppressing transient micro-spikes");
            }
        }

        public static void SetClampDuty(int duty)
        {
            AcousticClampDuty = Math.Max(25, Math.Min(60, duty));
            LogDecision(string.Format("CONFIG: Acoustic clamp ceiling set to {0}% duty", AcousticClampDuty));
        }

        public static void SetHysteresisSeconds(int seconds)
        {
            SustainedThresholdSeconds = Math.Max(1, Math.Min(15, seconds));
            LogDecision(string.Format("CONFIG: Sustained hysteresis delay set to {0} seconds", SustainedThresholdSeconds));
        }

        public static void SetFailsafeTemp(int temp)
        {
            FailsafeTemp = Math.Max(75, Math.Min(95, temp));
            LogDecision(string.Format("CONFIG: Failsafe emergency temperature set to {0}°C", FailsafeTemp));
        }

        public static void TogglePowerBoostTamer()
        {
            try
            {
                PowerBoostTamed = !PowerBoostTamed;
                int targetState = PowerBoostTamed ? 99 : 100;
                string argsAc = string.Format("/setacvalueindex SCHEME_CURRENT SUB_PROCESSOR PROCTHROTTLEMAX {0}", targetState);
                string argsDc = string.Format("/setdcvalueindex SCHEME_CURRENT SUB_PROCESSOR PROCTHROTTLEMAX {0}", targetState);

                ProcessStartInfo psi1 = new ProcessStartInfo("powercfg.exe", argsAc) { CreateNoWindow = true, UseShellExecute = false };
                Process p1 = Process.Start(psi1);
                if (p1 != null) p1.WaitForExit(1000);

                ProcessStartInfo psi2 = new ProcessStartInfo("powercfg.exe", argsDc) { CreateNoWindow = true, UseShellExecute = false };
                Process p2 = Process.Start(psi2);
                if (p2 != null) p2.WaitForExit(1000);

                ProcessStartInfo psiAct = new ProcessStartInfo("powercfg.exe", "/setactive SCHEME_CURRENT") { CreateNoWindow = true, UseShellExecute = false };
                Process pAct = Process.Start(psiAct);
                if (pAct != null) pAct.WaitForExit(1000);

                if (PowerBoostTamed)
                {
                    LogDecision("POWER TAMER: Maximum processor state set to 99% (Micro-burst overvolt eliminated)");
                }
                else
                {
                    LogDecision("POWER TAMER: Maximum processor state restored to 100% (Default Windows turbo)");
                }
            }
            catch (Exception ex)
            {
                LogDecision("POWER TAMER ERROR: " + ex.Message);
            }
        }

        public static void CheckCurrentPowerPlanState()
        {
            try
            {
                ProcessStartInfo psi = new ProcessStartInfo("powercfg.exe", "/query SCHEME_CURRENT SUB_PROCESSOR PROCTHROTTLEMAX")
                {
                    RedirectStandardOutput = true,
                    CreateNoWindow = true,
                    UseShellExecute = false
                };
                using (Process p = Process.Start(psi))
                {
                    if (p != null)
                    {
                        string output = p.StandardOutput.ReadToEnd();
                        p.WaitForExit(1000);
                        if (output.IndexOf("0x00000063", StringComparison.OrdinalIgnoreCase) >= 0)
                        {
                            PowerBoostTamed = true;
                        }
                        else
                        {
                            PowerBoostTamed = false;
                        }
                    }
                }
            }
            catch { }
        }

        public static void Update(int cpuTemp, int gpuTemp, float cpuUsage, float gpuUsage, float cpuWatts, bool isGaming)
        {
            RawCpuTemp = cpuTemp;

            // 1. Calculate Physical Heatsink Thermal EMA (alpha = 0.15)
            if (FilteredThermalEma <= 0) FilteredThermalEma = cpuTemp;
            else FilteredThermalEma = (0.15 * cpuTemp) + (0.85 * FilteredThermalEma);

            // Physical heatsink thermal saturation (40C = 0%, 85C = 100%)
            HeatsinkSaturationPct = Math.Max(0.0, Math.Min(100.0, ((FilteredThermalEma - 40.0) / 45.0) * 100.0));

            // 2. Track workload sustain duration
            bool isHeavyLoad = (cpuUsage >= 45.0f || gpuUsage >= 50.0f);
            if (isHeavyLoad)
            {
                SustainedLoadDurationSec++;
            }
            else
            {
                if (SustainedLoadDurationSec > 0) SustainedLoadDurationSec--;
            }

            // 3. Classify and Decide
            if (!IsEnabled)
            {
                CurrentClassifierState = "⚪ GUARD DISABLED (RAW EC FIRMWARE CONTROL)";
                if (CurrentAppliedMode != 0)
                {
                    CurrentAppliedMode = 0;
                    IsCurrentlyClamped = false;
                    HardwareBridge.ResetToAuto();
                }
                return;
            }

            // Rule 1: Emergency Thermal Failsafe (Safety First)
            if (cpuTemp >= FailsafeTemp || gpuTemp >= 85)
            {
                CurrentClassifierState = string.Format("🚨 EMERGENCY FAILSAFE ({0}°C >= {1}°C) - TURBO ENGAGED", cpuTemp, FailsafeTemp);
                IsCurrentlyClamped = false;
                if (CurrentAppliedMode != 1)
                {
                    CurrentAppliedMode = 1;
                    HardwareBridge.SetFanMode(1); // Force Max cooling
                    LogDecision(string.Format("EMERGENCY FAILSAFE: CPU {0}°C >= {1}°C -> Fans 100% Turbo", cpuTemp, FailsafeTemp));
                }
                return;
            }

            // Rule 2: Active Gaming Unclamp (DirectX ETW or 3D GPU Load)
            if (isGaming && AutoUnclampGaming)
            {
                CurrentClassifierState = "🎮 3D GAMING ACTIVE - FANS UNRESTRICTED";
                if (CurrentAppliedMode != 0)
                {
                    CurrentAppliedMode = 0;
                    IsCurrentlyClamped = false;
                    HardwareBridge.ResetToAuto();
                    string details = GameTelemetryEngine.IsGameActive ?
                        string.Format("{0} ({1:F0} FPS)", GameTelemetryEngine.ActiveProcessName, GameTelemetryEngine.CurrentFps) :
                        string.Format("GPU Load {0:F0}%", gpuUsage);
                    LogDecision("GAMING DETECTED: " + details + " -> Restored Auto cooling");
                }
                return;
            }

            // Rule 3: Sustained Compute (Legitimate Render / Compile / Heavy Work)
            if (SustainedLoadDurationSec >= SustainedThresholdSeconds)
            {
                CurrentClassifierState = string.Format("🔥 SUSTAINED LOAD ({0}s >= {1}s) - ADAPTIVE COOLING", SustainedLoadDurationSec, SustainedThresholdSeconds);
                if (CurrentAppliedMode != 0)
                {
                    CurrentAppliedMode = 0;
                    IsCurrentlyClamped = false;
                    HardwareBridge.ResetToAuto();
                    LogDecision(string.Format("SUSTAINED LOAD: {0}s sustained load -> Restored Auto cooling", SustainedLoadDurationSec));
                }
                return;
            }

            // Rule 4: Transient Micro-Burst / Desktop Browsing (INTERCEPT & PREVENT JET REV)
            // Use native Acer EC Silent Mode (Mode 3): Caps fan RPM and enforces quiet acoustics
            if (cpuTemp >= 65 && cpuUsage < 35.0f && !isHeavyLoad)
            {
                InterceptedSpikeCount++;
                CurrentClassifierState = string.Format("⚡ TRANSIENT SPIKE BLOCKED ({0}°C Die / {1:F0}°C Heatsink) - SILENT MODE", cpuTemp, FilteredThermalEma);
                if (CurrentAppliedMode != 3)
                {
                    CurrentAppliedMode = 3;
                    IsCurrentlyClamped = true;
                    HardwareBridge.SetFanMode(3); // Native Hardware Silent Mode (quiet acoustics)
                    LogDecision(string.Format("SPIKE BLOCKED: {0}°C micro-burst (Load: {1:F0}%) -> Engaged EC Silent Mode (3)", cpuTemp, cpuUsage));
                }
            }
            else
            {
                CurrentClassifierState = "🛡️ BROWSING PROTECTED - HARDWARE SILENT MODE (3)";
                if (CurrentAppliedMode != 3)
                {
                    CurrentAppliedMode = 3;
                    IsCurrentlyClamped = true;
                    HardwareBridge.SetFanMode(3); // Native Hardware Silent Mode (quiet acoustics)
                    LogDecision("DESKTOP BROWSING: Engaged EC Silent Mode (3) - Fan revving suppressed");
                }
            }
        }
    }

    // ==========================================
    // DESIGN TOKENS (APPLE HIG & PRECISION CRAFT)
    // ==========================================
    public static class DesignTokens
    {
        // Surfaces & Backgrounds
        public static readonly Color BgWindow = Color.FromRgb(11, 15, 25);            // #0B0F19 - Deep slate window canvas
        public static readonly Color BgCard = Color.FromRgb(17, 24, 39);              // #111827 - Surface card
        public static readonly Color BgCardHover = Color.FromRgb(24, 33, 53);         // #182135 - Hover state
        public static readonly Color BgCardActive = Color.FromRgb(30, 41, 59);        // #1E293B - Selected state
        public static readonly Color BgInput = Color.FromRgb(15, 23, 42);             // #0F172A - Control / input surface
        public static readonly Color BorderSubtle = Color.FromRgb(31, 41, 61);        // #1F293D - Clean hairline border
        public static readonly Color BorderSubtleLight = Color.FromRgb(51, 65, 85);   // #334155 - Card highlight border
        public static readonly Color BorderFocus = Color.FromRgb(0, 163, 255);        // #00A3FF - Active accent outline

        // Typography (WCAG AA Compliant on dark surfaces)
        public static readonly Color TextPrimaryColor = Color.FromRgb(248, 250, 252);     // #F8FAFC - Near white
        public static readonly Color TextSecondaryColor = Color.FromRgb(148, 163, 184);   // #94A3B8 - Slate-400 (>5.5:1 contrast)
        public static readonly Color TextMutedColor = Color.FromRgb(100, 116, 139);       // #64748B - Slate-500

        public static readonly SolidColorBrush BrushBgWindow = new SolidColorBrush(BgWindow);
        public static readonly SolidColorBrush BrushBgCard = new SolidColorBrush(BgCard);
        public static readonly SolidColorBrush BrushBgCardHover = new SolidColorBrush(BgCardHover);
        public static readonly SolidColorBrush BrushBgCardActive = new SolidColorBrush(BgCardActive);
        public static readonly SolidColorBrush BrushBgInput = new SolidColorBrush(BgInput);
        public static readonly SolidColorBrush BrushBorderSubtle = new SolidColorBrush(BorderSubtle);
        public static readonly SolidColorBrush BrushBorderSubtleLight = new SolidColorBrush(BorderSubtleLight);
        public static readonly SolidColorBrush BrushBorderFocus = new SolidColorBrush(BorderFocus);

        public static readonly SolidColorBrush BrushTextPrimary = new SolidColorBrush(TextPrimaryColor);
        public static readonly SolidColorBrush BrushTextSecondary = new SolidColorBrush(TextSecondaryColor);
        public static readonly SolidColorBrush BrushTextMuted = new SolidColorBrush(TextMutedColor);

        // Semantic Accents
        public static readonly Color AccentBlue = Color.FromRgb(0, 163, 255);         // #00A3FF - Primary brand / active
        public static readonly Color AccentCyan = Color.FromRgb(6, 182, 212);         // #06B6D4 - Secondary telemetry
        public static readonly Color AccentGreen = Color.FromRgb(16, 185, 129);       // #10B981 - Optimal / running
        public static readonly Color AccentAmber = Color.FromRgb(245, 158, 11);       // #F59E0B - Warning
        public static readonly Color AccentRed = Color.FromRgb(239, 68, 68);          // #EF4444 - Critical / Redline
        public static readonly Color AccentPurple = Color.FromRgb(168, 85, 247);      // #A855F7 - Memory

        public static readonly SolidColorBrush BrushAccentBlue = new SolidColorBrush(AccentBlue);
        public static readonly SolidColorBrush BrushAccentCyan = new SolidColorBrush(AccentCyan);
        public static readonly SolidColorBrush BrushAccentGreen = new SolidColorBrush(AccentGreen);
        public static readonly SolidColorBrush BrushAccentAmber = new SolidColorBrush(AccentAmber);
        public static readonly SolidColorBrush BrushAccentRed = new SolidColorBrush(AccentRed);
        public static readonly SolidColorBrush BrushAccentPurple = new SolidColorBrush(AccentPurple);
    }

    // ==========================================
    // APPLE ACTIVITY-STYLE TELEMETRY RING GAUGE
    // ==========================================
    public class SpeedometerGauge : Grid
    {
        private Path bgArc;
        private Path valArc;
        private TextBlock txtHeader;
        private TextBlock txtSub;
        private TextBlock txtTemp;
        private TextBlock txtRpm;
        private TextBlock txtDutyBadge;
        private TextBlock txtWatts;
        private TextBlock txtPercent;
        private TextBlock txtDutyVal;

        private double radius = 84.0;
        private double cx = 105.0;
        private double cy = 105.0;
        private double startAngle = 225.0; // Bottom-left (7:30)
        private double sweepAngle = 270.0; // Sweeps clockwise through 12:00 to bottom-right (4:30)
        private LinearGradientBrush ringGrad;
        private bool isGpuDevice = false;

        public SpeedometerGauge(string hardwareName, string modelSub, double size, bool isGpu = false)
        {
            this.isGpuDevice = isGpu;
            this.Width = size;
            this.HorizontalAlignment = HorizontalAlignment.Center;
            this.VerticalAlignment = VerticalAlignment.Stretch;

            this.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // 0: Chip Header
            this.RowDefinitions.Add(new RowDefinition { Height = new GridLength(210) }); // 1: Activity Ring
            this.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // 2: 3-Pod Telemetry Strip

            // 1. Hardware Category & Title Header
            StackPanel headSp = new StackPanel
            {
                HorizontalAlignment = HorizontalAlignment.Center,
                Margin = new Thickness(0, 0, 0, 10)
            };

            Border chipBadge = new Border
            {
                CornerRadius = new CornerRadius(5),
                Background = isGpu ? new SolidColorBrush(Color.FromArgb(40, 16, 185, 129)) : new SolidColorBrush(Color.FromArgb(40, 6, 182, 212)),
                BorderBrush = isGpu ? DesignTokens.BrushAccentGreen : DesignTokens.BrushAccentCyan,
                BorderThickness = new Thickness(1),
                Padding = new Thickness(8, 2, 8, 2),
                HorizontalAlignment = HorizontalAlignment.Center,
                Margin = new Thickness(0, 0, 0, 4)
            };
            TextBlock chipTxt = new TextBlock
            {
                Text = isGpu ? "⚡ GRAPHICS SILICON" : "🔲 PROCESSOR PACKAGE",
                FontSize = 9.5,
                FontWeight = FontWeights.Bold,
                Foreground = isGpu ? DesignTokens.BrushAccentGreen : DesignTokens.BrushAccentCyan
            };
            chipBadge.Child = chipTxt;
            headSp.Children.Add(chipBadge);

            txtHeader = new TextBlock
            {
                Text = hardwareName,
                FontSize = 14,
                FontWeight = FontWeights.Bold,
                Foreground = DesignTokens.BrushTextPrimary,
                TextAlignment = TextAlignment.Center,
                TextTrimming = TextTrimming.CharacterEllipsis,
                MaxWidth = 310
            };
            headSp.Children.Add(txtHeader);

            txtSub = new TextBlock
            {
                Text = modelSub,
                FontSize = 10.5,
                Foreground = DesignTokens.BrushTextSecondary,
                TextAlignment = TextAlignment.Center,
                Margin = new Thickness(0, 2, 0, 0)
            };
            headSp.Children.Add(txtSub);

            Grid.SetRow(headSp, 0);
            this.Children.Add(headSp);

            // 2. The Apple Activity Ring Canvas Container
            Grid ringHost = new Grid
            {
                Width = 210,
                Height = 210,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            };

            Canvas arcCanvas = new Canvas { Width = 210, Height = 210 };

            // Background Track
            bgArc = new Path
            {
                Stroke = new SolidColorBrush(Color.FromRgb(22, 31, 48)),
                StrokeThickness = 14,
                StrokeStartLineCap = PenLineCap.Round,
                StrokeEndLineCap = PenLineCap.Round,
                Data = CreateArcGeometry(cx, cy, radius, startAngle, startAngle + sweepAngle)
            };
            arcCanvas.Children.Add(bgArc);

            // Active Progress Gradient Arc
            ringGrad = new LinearGradientBrush
            {
                StartPoint = new Point(0, 1),
                EndPoint = new Point(1, 0)
            };
            if (isGpu)
            {
                ringGrad.GradientStops.Add(new GradientStop(Color.FromRgb(16, 185, 129), 0.0));
                ringGrad.GradientStops.Add(new GradientStop(Color.FromRgb(6, 182, 212), 0.7));
                ringGrad.GradientStops.Add(new GradientStop(Color.FromRgb(59, 130, 246), 1.0));
            }
            else
            {
                ringGrad.GradientStops.Add(new GradientStop(Color.FromRgb(6, 182, 212), 0.0));
                ringGrad.GradientStops.Add(new GradientStop(Color.FromRgb(59, 130, 246), 0.7));
                ringGrad.GradientStops.Add(new GradientStop(Color.FromRgb(147, 51, 234), 1.0));
            }

            valArc = new Path
            {
                Stroke = ringGrad,
                StrokeThickness = 14,
                StrokeStartLineCap = PenLineCap.Round,
                StrokeEndLineCap = PenLineCap.Round,
                Effect = new DropShadowEffect
                {
                    Color = isGpu ? Color.FromRgb(16, 185, 129) : Color.FromRgb(6, 182, 212),
                    BlurRadius = 14,
                    ShadowDepth = 0,
                    Opacity = 0.45
                }
            };
            arcCanvas.Children.Add(valArc);
            ringHost.Children.Add(arcCanvas);

            // Centered Hero Metric Stack (Inside the Ring)
            StackPanel ringCenterSp = new StackPanel
            {
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 0, 4)
            };

            txtTemp = new TextBlock
            {
                Text = "-- °C",
                FontSize = 38,
                FontWeight = FontWeights.Bold,
                Foreground = DesignTokens.BrushTextPrimary,
                TextAlignment = TextAlignment.Center
            };
            ringCenterSp.Children.Add(txtTemp);

            txtRpm = new TextBlock
            {
                Text = "-- RPM",
                FontSize = 13,
                FontWeight = FontWeights.SemiBold,
                Foreground = isGpu ? DesignTokens.BrushAccentGreen : DesignTokens.BrushAccentCyan,
                TextAlignment = TextAlignment.Center,
                Margin = new Thickness(0, 1, 0, 0)
            };
            ringCenterSp.Children.Add(txtRpm);

            Border dutyPill = new Border
            {
                CornerRadius = new CornerRadius(10),
                Background = DesignTokens.BrushBgInput,
                BorderBrush = DesignTokens.BrushBorderSubtle,
                BorderThickness = new Thickness(1),
                Padding = new Thickness(8, 2, 8, 2),
                HorizontalAlignment = HorizontalAlignment.Center,
                Margin = new Thickness(0, 6, 0, 0)
            };
            txtDutyBadge = new TextBlock
            {
                Text = "● 0% Duty • Idle",
                FontSize = 9.5,
                FontWeight = FontWeights.SemiBold,
                Foreground = DesignTokens.BrushTextSecondary
            };
            dutyPill.Child = txtDutyBadge;
            ringCenterSp.Children.Add(dutyPill);

            ringHost.Children.Add(ringCenterSp);

            Grid.SetRow(ringHost, 1);
            this.Children.Add(ringHost);

            // 3. Bottom 3-Pod Micro Telemetry Strip (POWER | LOAD | FAN DUTY)
            UniformGrid podGrid = new UniformGrid
            {
                Columns = 3,
                Rows = 1,
                Margin = new Thickness(0, 12, 0, 0)
            };

            Border podPower = CreateTelemetryPod("POWER", out txtWatts, "-- W", DesignTokens.BrushTextPrimary);
            Border podLoad = CreateTelemetryPod("LOAD", out txtPercent, "-- %", isGpu ? DesignTokens.BrushAccentGreen : DesignTokens.BrushAccentCyan);
            Border podDuty = CreateTelemetryPod("FAN DUTY", out txtDutyVal, "-- %", DesignTokens.BrushAccentBlue);

            podGrid.Children.Add(podPower);
            podGrid.Children.Add(podLoad);
            podGrid.Children.Add(podDuty);

            Grid.SetRow(podGrid, 2);
            this.Children.Add(podGrid);

            Update(0, 40, 0, 0, 0);
        }

        private Border CreateTelemetryPod(string caption, out TextBlock valTb, string initialVal, Brush valBrush)
        {
            Border b = new Border
            {
                Background = DesignTokens.BrushBgInput,
                BorderBrush = DesignTokens.BrushBorderSubtle,
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(8),
                Padding = new Thickness(6, 6, 6, 6),
                Margin = new Thickness(3, 0, 3, 0)
            };

            StackPanel sp = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center };
            TextBlock capTb = new TextBlock
            {
                Text = caption,
                FontSize = 9.0,
                FontWeight = FontWeights.SemiBold,
                Foreground = DesignTokens.BrushTextMuted,
                HorizontalAlignment = HorizontalAlignment.Center
            };
            sp.Children.Add(capTb);

            valTb = new TextBlock
            {
                Text = initialVal,
                FontSize = 12.5,
                FontWeight = FontWeights.Bold,
                Foreground = valBrush,
                HorizontalAlignment = HorizontalAlignment.Center,
                Margin = new Thickness(0, 2, 0, 0)
            };
            sp.Children.Add(valTb);

            b.Child = sp;
            return b;
        }

        public void Update(float usage, int temp, int rpm, int duty)
        {
            Update(usage, temp, rpm, duty, 0);
        }

        public void Update(float usage, int temp, int rpm, int duty, float watts)
        {
            txtTemp.Text = string.Format("{0}°C", temp);
            txtRpm.Text = (rpm > 0) ? string.Format("{0:N0} RPM", rpm) : "Passive Idle";
            txtPercent.Text = string.Format("{0:F0}%", usage);
            txtDutyVal.Text = string.Format("{0}%", duty);
            txtWatts.Text = (watts > 0) ? string.Format("{0:F0}W", watts) : "-- W";

            string status = (temp >= 85) ? "Redline" : ((temp >= 72) ? "Warm" : "Optimal");
            txtDutyBadge.Text = string.Format("● {0}% Duty • {1}", duty, status);

            if (temp >= 85)
            {
                txtTemp.Foreground = DesignTokens.BrushAccentRed;
                txtDutyBadge.Foreground = DesignTokens.BrushAccentRed;
                valArc.Stroke = DesignTokens.BrushAccentRed;
            }
            else if (temp >= 72)
            {
                txtTemp.Foreground = DesignTokens.BrushAccentAmber;
                txtDutyBadge.Foreground = DesignTokens.BrushAccentAmber;
                valArc.Stroke = DesignTokens.BrushAccentAmber;
            }
            else
            {
                txtTemp.Foreground = DesignTokens.BrushTextPrimary;
                txtDutyBadge.Foreground = isGpuDevice ? DesignTokens.BrushAccentGreen : DesignTokens.BrushAccentCyan;
                valArc.Stroke = ringGrad;
            }

            // Ring progress mapped to temperature (0..100°C) with minimum visible cap
            double ratio = Math.Max(0.015, Math.Min(1.0, temp / 100.0));
            double targetAngle = startAngle + (sweepAngle * ratio);
            valArc.Data = CreateArcGeometry(cx, cy, radius, startAngle, targetAngle);
        }

        private static PathGeometry CreateArcGeometry(double cx, double cy, double r, double sAngle, double eAngle)
        {
            double sRad = (sAngle - 90) * Math.PI / 180.0;
            double eRad = (eAngle - 90) * Math.PI / 180.0;

            Point startP = new Point(cx + r * Math.Cos(sRad), cy + r * Math.Sin(sRad));
            Point endP = new Point(cx + r * Math.Cos(eRad), cy + r * Math.Sin(eRad));

            bool isLarge = Math.Abs(eAngle - sAngle) > 180.0;

            PathFigure figure = new PathFigure { StartPoint = startP, IsClosed = false, IsFilled = false };
            figure.Segments.Add(new ArcSegment(endP, new Size(r, r), 0, isLarge, SweepDirection.Clockwise, true));

            PathGeometry geo = new PathGeometry();
            geo.Figures.Add(figure);
            return geo;
        }
    }

    // ==========================================
    // SEGMENTED LED BAR METER
    // ==========================================
    public class SegmentedMeter : StackPanel
    {
        private List<Border> segments = new List<Border>();
        private Color activeColor;
        private Color inactiveColor = Color.FromArgb(255, 24, 33, 53);

        public SegmentedMeter(int count, Color active, double segWidth, double segHeight)
        {
            this.Orientation = Orientation.Horizontal;
            this.activeColor = active;
            for (int i = 0; i < count; i++)
            {
                Border b = new Border
                {
                    Width = segWidth,
                    Height = segHeight,
                    CornerRadius = new CornerRadius(1.5),
                    Margin = new Thickness(0, 0, 3, 0),
                    Background = new SolidColorBrush(inactiveColor)
                };
                segments.Add(b);
                this.Children.Add(b);
            }
        }

        public void SetColor(Color col)
        {
            this.activeColor = col;
        }

        public void SetValue(double percent)
        {
            percent = Math.Max(0.0, Math.Min(100.0, percent));
            int activeCount = (int)Math.Round((percent / 100.0) * segments.Count);
            for (int i = 0; i < segments.Count; i++)
            {
                segments[i].Background = (i < activeCount)
                    ? new SolidColorBrush(activeColor)
                    : new SolidColorBrush(inactiveColor);
            }
        }
    }

    // ==========================================
    // IN-GAME METRICS & OVERLAY ENUMS
    // ==========================================
    public enum OverlayPosition
    {
        TopLeft,
        TopRight,
        CenterTop,
        BottomLeft,
        BottomRight
    }

    public enum OverlayStyle
    {
        HorizontalBar,
        CompactMatrix
    }

    // ==========================================
    // ==========================================
    // ZERO-HOOK DIRECTX / D3D REAL-TIME TELEMETRY ENGINE
    // ==========================================
    public static class GameTelemetryEngine
    {
        [StructLayout(LayoutKind.Explicit)]
        public struct WNODE_HEADER
        {
            [FieldOffset(0)] public uint BufferSize;
            [FieldOffset(4)] public uint ProviderId;
            [FieldOffset(8)] public ulong HistoricalContext;
            [FieldOffset(16)] public long TimeStamp;
            [FieldOffset(24)] public Guid Guid;
            [FieldOffset(40)] public uint ClientContext;
            [FieldOffset(44)] public uint Flags;
        }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        public struct EVENT_TRACE_PROPERTIES
        {
            public WNODE_HEADER Wnode;
            public uint BufferSize;
            public uint MinimumBuffers;
            public uint MaximumBuffers;
            public uint MaximumFileSize;
            public uint LogFileMode;
            public uint FlushTimer;
            public uint EnableFlags;
            public int AgeLimit;
            public uint NumberOfBuffers;
            public uint FreeBuffers;
            public uint EventsLost;
            public uint BuffersWritten;
            public uint LogBuffersLost;
            public uint RealTimeBuffersLost;
            public IntPtr LoggerThreadId;
            public uint LogFileNameOffset;
            public uint LoggerNameOffset;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct EVENT_TRACE_LOGFILEW
        {
            public IntPtr LogFileName;
            public IntPtr LoggerName;
            public long CurrentTime;
            public uint BuffersRead;
            public uint ProcessTraceMode;
            public EVENT_TRACE CurrentEvent;
            public TRACE_LOGFILE_HEADER LogfileHeader;
            public IntPtr BufferCallback;
            public uint BufferSize;
            public uint Filled;
            public uint EventsLost;
            public IntPtr EventRecordCallback;
            public uint IsKernelTrace;
            public IntPtr Context;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct EVENT_TRACE
        {
            public EVENT_INSTANCE_HEADER Header;
            public uint InstanceId;
            public uint ParentInstanceId;
            public Guid ParentGuid;
            public IntPtr MofData;
            public uint MofLength;
            public uint ClientContext;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct EVENT_INSTANCE_HEADER
        {
            public ushort Size;
            public ushort FieldTypeFlags;
            public uint Anonymous1;
            public uint ThreadId;
            public uint ProcessId;
            public long TimeStamp;
            public Guid Guid;
            public IntPtr ClientContext;
            public uint EventSize;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct TRACE_LOGFILE_HEADER
        {
            public uint BufferSize;
            public uint Version;
            public uint ProviderVersion;
            public uint NumberOfProcessors;
            public long EndTime;
            public uint TimerResolution;
            public uint MaximumFileSize;
            public uint LogFileMode;
            public uint BuffersWritten;
            public uint StartBuffers;
            public uint Reserved;
            public IntPtr LoggerName;
            public IntPtr LogFileName;
            public TIME_ZONE_INFORMATION TimeZone;
            public long BootTime;
            public long PerfFreq;
            public long StartTime;
            public uint ReservedFlags;
            public uint BuffersLost;
        }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        public struct TIME_ZONE_INFORMATION
        {
            public int Bias;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
            public string StandardName;
            public SYSTEMTIME StandardDate;
            public int StandardBias;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
            public string DaylightName;
            public SYSTEMTIME DaylightDate;
            public int DaylightBias;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct SYSTEMTIME
        {
            public ushort Year;
            public ushort Month;
            public ushort DayOfWeek;
            public ushort Day;
            public ushort Hour;
            public ushort Minute;
            public ushort Second;
            public ushort Milliseconds;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct EVENT_RECORD
        {
            public EVENT_HEADER EventHeader;
            public ETW_BUFFER_CONTEXT BufferContext;
            public ushort ExtendedDataCount;
            public ushort UserDataLength;
            public IntPtr ExtendedData;
            public IntPtr UserData;
            public IntPtr UserContext;
        }

        [StructLayout(LayoutKind.Explicit, Size = 80)]
        public struct EVENT_HEADER
        {
            [FieldOffset(0x00)] public ushort Size;
            [FieldOffset(0x02)] public ushort HeaderType;
            [FieldOffset(0x04)] public ushort Flags;
            [FieldOffset(0x06)] public ushort EventProperty;
            [FieldOffset(0x08)] public uint ThreadId;
            [FieldOffset(0x0C)] public uint ProcessId;
            [FieldOffset(0x10)] public long TimeStamp;
            [FieldOffset(0x18)] public Guid ProviderId;
            [FieldOffset(0x28)] public ushort EventId;
            [FieldOffset(0x2A)] public byte Version;
            [FieldOffset(0x2B)] public byte Channel;
            [FieldOffset(0x2C)] public byte Level;
            [FieldOffset(0x2D)] public byte Opcode;
            [FieldOffset(0x2E)] public ushort Task;
            [FieldOffset(0x30)] public ulong Keyword;
            [FieldOffset(0x38)] public uint KernelTime;
            [FieldOffset(0x3C)] public uint UserTime;
            [FieldOffset(0x38)] public ulong ProcessorTime;
            [FieldOffset(0x40)] public Guid ActivityId;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct ETW_BUFFER_CONTEXT
        {
            public byte ProcessorNumber;
            public byte Alignment;
            public ushort LoggerId;
        }

        [UnmanagedFunctionPointer(CallingConvention.Winapi)]
        public delegate void EventRecordCallback([In] ref EVENT_RECORD EventRecord);

        [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        public static extern uint StartTrace(out ulong TraceHandle, string InstanceName, IntPtr Properties);

        [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        public static extern uint ControlTrace(ulong TraceHandle, string InstanceName, IntPtr Properties, uint ControlCode);

        [DllImport("advapi32.dll", SetLastError = true)]
        public static extern uint EnableTraceEx2(
            ulong TraceHandle,
            ref Guid ProviderId,
            uint ControlCode,
            byte Level,
            ulong MatchAnyKeyword,
            ulong MatchAllKeyword,
            uint Timeout,
            IntPtr EnableParameters
        );

        [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        public static extern IntPtr OpenTraceW(IntPtr Logfile);

        [DllImport("advapi32.dll", SetLastError = true)]
        public static extern uint ProcessTrace(IntPtr[] HandleArray, uint HandleCount, IntPtr StartTime, IntPtr EndTime);

        [DllImport("advapi32.dll", SetLastError = true)]
        public static extern uint CloseTrace(IntPtr TraceHandle);

        [DllImport("user32.dll")]
        public static extern IntPtr GetForegroundWindow();

        [DllImport("user32.dll")]
        public static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

        [DllImport("kernel32.dll", SetLastError = true)]
        public static extern IntPtr OpenProcess(uint dwDesiredAccess, bool bInheritHandle, uint dwProcessId);

        [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        public static extern bool QueryFullProcessImageName(IntPtr hProcess, int dwFlags, StringBuilder lpExeName, ref int lpdwSize);

        [DllImport("kernel32.dll", SetLastError = true)]
        public static extern bool CloseHandle(IntPtr hObject);

        private const uint EVENT_TRACE_REAL_TIME_MODE = 0x00000100;
        private const uint EVENT_CONTROL_CODE_ENABLE_PROVIDER = 1;
        private const uint EVENT_CONTROL_CODE_STOP_TRACE = 1;
        private const uint EVENT_TRACE_CONTROL_FLUSH = 3;
        private const uint PROCESS_TRACE_MODE_REAL_TIME = 0x00000100;
        private const uint PROCESS_TRACE_MODE_EVENT_RECORD = 0x10000000;

        private const string SessionName = "AspireSenseEtwSession";
        private static readonly Guid DxgiProviderGuid = new Guid("CA11C036-0102-4A2D-A6AD-F03CFED5D3C9");
        private static readonly Guid D3D9ProviderGuid = new Guid("783ACA0A-790E-4D7F-8451-AA850511C6B9");

        public static double CurrentFps = 0.0;
        public static double AvgFps = 0.0;
        public static double OnePercentLowFps = 0.0;
        public static double CurrentFrameTimeMs = 0.0;
        public static string ActiveProcessName = "Desktop Window Manager";
        public static uint ActiveProcessId = 0;
        public static bool IsEtwActive = false;

        public static bool IsGameActive
        {
            get
            {
                return CurrentFps >= 10.0 && !string.IsNullOrEmpty(ActiveProcessName) && !IsSystemProcess(ActiveProcessName);
            }
        }

        private static volatile uint targetGamePid = 0;
        private static ulong sessionHandle = 0;
        private static IntPtr consumerHandle = IntPtr.Zero;
        private static IntPtr pProperties = IntPtr.Zero;
        private static IntPtr pLogFile = IntPtr.Zero;
        private static IntPtr pLoggerName = IntPtr.Zero;
        private static EventRecordCallback callbackDelegate;
        private static Thread traceThread;
        private static bool isRunning = false;

        private static readonly object frameLock = new object();
        private static readonly Queue<long> frameTimestamps = new Queue<long>();
        private static readonly List<double> recentFrameTimes = new List<double>();
        private static readonly Queue<double> rollingFpsHistory = new Queue<double>();
        private static long lastFrameTimestamp = 0;
        private static long lastFrameWallClock = 0;
        private static bool wasInLoadingScreen = false;
        private static int selfPid = 0;

        public static void Start()
        {
            if (isRunning) return;
            isRunning = true;
            try
            {
                selfPid = Process.GetCurrentProcess().Id;
            }
            catch { }

            traceThread = new Thread(RunEtwTrace);
            traceThread.IsBackground = true;
            traceThread.Name = "AspireSense-ETW";
            traceThread.Start();
        }

        private static void RunEtwTrace()
        {
            try
            {
                int propSize = Marshal.SizeOf(typeof(EVENT_TRACE_PROPERTIES));
                int nameBytes = (SessionName.Length + 1) * 2;
                int allocSize = propSize + nameBytes + 256;
                pProperties = Marshal.AllocHGlobal(allocSize);

                for (int i = 0; i < allocSize; i++) Marshal.WriteByte(pProperties, i, 0);

                EVENT_TRACE_PROPERTIES props = new EVENT_TRACE_PROPERTIES();
                props.Wnode.BufferSize = (uint)allocSize;
                props.Wnode.Flags = 0x00020000; // WNODE_FLAG_TRACED_GUID
                props.Wnode.ClientContext = 1;  // QPC
                props.Wnode.Guid = Guid.NewGuid();
                props.BufferSize = 64;
                props.MinimumBuffers = 2;
                props.MaximumBuffers = 8;
                props.LogFileMode = EVENT_TRACE_REAL_TIME_MODE;
                props.LoggerNameOffset = (uint)propSize;

                Marshal.StructureToPtr(props, pProperties, false);
                pLoggerName = Marshal.StringToHGlobalUni(SessionName);
                Marshal.Copy(SessionName.ToCharArray(), 0, IntPtr.Add(pProperties, propSize), SessionName.Length);

                // Stop any leftover session first
                ControlTrace(0, SessionName, pProperties, EVENT_CONTROL_CODE_STOP_TRACE);

                uint status = StartTrace(out sessionHandle, SessionName, pProperties);
                if (status != 0)
                {
                    ControlTrace(0, SessionName, pProperties, EVENT_CONTROL_CODE_STOP_TRACE);
                    Thread.Sleep(50);
                    status = StartTrace(out sessionHandle, SessionName, pProperties);
                }

                if (status == 0 && sessionHandle != 0)
                {
                    IsEtwActive = true;

                    // Enable DXGI provider (Informational level, Present events)
                    Guid dxgi = DxgiProviderGuid;
                    EnableTraceEx2(sessionHandle, ref dxgi, EVENT_CONTROL_CODE_ENABLE_PROVIDER, 4, 0xFFFFFFFFFFFFFFFF, 0, 0, IntPtr.Zero);

                    // Enable D3D9 provider (Informational level, Present events)
                    Guid d3d9 = D3D9ProviderGuid;
                    EnableTraceEx2(sessionHandle, ref d3d9, EVENT_CONTROL_CODE_ENABLE_PROVIDER, 4, 0xFFFFFFFFFFFFFFFF, 0, 0, IntPtr.Zero);

                    // Allocate unmanaged memory for EVENT_TRACE_LOGFILEW so it persists during ProcessTrace
                    int logFileSize = Marshal.SizeOf(typeof(EVENT_TRACE_LOGFILEW));
                    pLogFile = Marshal.AllocHGlobal(logFileSize);
                    for (int i = 0; i < logFileSize; i++) Marshal.WriteByte(pLogFile, i, 0);

                    EVENT_TRACE_LOGFILEW logFile = new EVENT_TRACE_LOGFILEW();
                    logFile.LoggerName = pLoggerName;
                    logFile.ProcessTraceMode = PROCESS_TRACE_MODE_REAL_TIME | PROCESS_TRACE_MODE_EVENT_RECORD;

                    callbackDelegate = new EventRecordCallback(OnEventRecord);
                    logFile.EventRecordCallback = Marshal.GetFunctionPointerForDelegate(callbackDelegate);

                    Marshal.StructureToPtr(logFile, pLogFile, false);

                    consumerHandle = OpenTraceW(pLogFile);
                    if (consumerHandle != IntPtr.Zero && consumerHandle != (IntPtr)(-1))
                    {
                        IntPtr[] handles = new IntPtr[] { consumerHandle };
                        ProcessTrace(handles, 1, IntPtr.Zero, IntPtr.Zero);
                    }
                }
                else
                {
                    IsEtwActive = false;
                }
            }
            catch
            {
                IsEtwActive = false;
            }
        }

        private static void OnEventRecord([In] ref EVENT_RECORD record)
        {
            try
            {
                uint pid = record.EventHeader.ProcessId;
                if (pid == 0 || pid == (uint)selfPid) return;

                ushort evId = record.EventHeader.EventId;
                Guid prov = record.EventHeader.ProviderId;

                bool isPresent = false;
                if (prov == DxgiProviderGuid)
                {
                    // ONLY listen to Present completion (Stop) events:
                    // 42 = DXGI Present_Stop
                    // 40 = PresentMultiplaneOverlay_Stop
                    // 44 = DXGITokenPresent_Stop
                    if (evId == 42 || evId == 40 || evId == 44)
                    {
                        isPresent = true;
                    }
                }
                else if (prov == D3D9ProviderGuid)
                {
                    // 2 = D3D9 Present_Stop
                    if (evId == 2)
                    {
                        isPresent = true;
                    }
                }

                if (isPresent)
                {
                    uint targetPid = targetGamePid;
                    if (targetPid == 0)
                    {
                        string autoName = GetProcessNameSafe((int)pid);
                        if (!IsSystemProcess(autoName))
                        {
                            targetGamePid = pid;
                            ActiveProcessId = pid;
                            ActiveProcessName = autoName;
                            targetPid = pid;
                        }
                    }

                    if (targetPid != 0 && pid == targetPid)
                    {
                        long qpcTime = record.EventHeader.TimeStamp;
                        if (qpcTime <= 0)
                        {
                            qpcTime = Stopwatch.GetTimestamp();
                        }

                        lock (frameLock)
                        {
                            if (lastFrameTimestamp > 0)
                            {
                                long deltaTicks = qpcTime - lastFrameTimestamp;
                                double deltaMs = deltaTicks * 1000.0 / Stopwatch.Frequency;

                                // Deduplicate duplicate events for same frame within 0.5ms (e.g. swapchain + MPO overlay)
                                if (deltaMs >= 0.0 && deltaMs < 0.5)
                                {
                                    return;
                                }
                                if (deltaMs < 0.0 && deltaMs > -0.5)
                                {
                                    return;
                                }

                                if (deltaMs >= 0.5 && deltaMs <= 200.0)
                                {
                                    recentFrameTimes.Add(deltaMs);
                                    if (recentFrameTimes.Count > 500)
                                    {
                                        recentFrameTimes.RemoveAt(0);
                                    }
                                }
                            }

                            lastFrameTimestamp = qpcTime;
                            lastFrameWallClock = Stopwatch.GetTimestamp();
                            frameTimestamps.Enqueue(qpcTime);
                        }
                    }
                }
            }
            catch { }
        }

        private static string GetProcessNameSafe(int pid)
        {
            try
            {
                IntPtr hProc = OpenProcess(0x1000 /* PROCESS_QUERY_LIMITED_INFORMATION */, false, (uint)pid);
                if (hProc != IntPtr.Zero)
                {
                    try
                    {
                        StringBuilder sb = new StringBuilder(1024);
                        int size = sb.Capacity;
                        if (QueryFullProcessImageName(hProc, 0, sb, ref size))
                        {
                            return System.IO.Path.GetFileName(sb.ToString());
                        }
                    }
                    finally
                    {
                        CloseHandle(hProc);
                    }
                }
            }
            catch { }

            try
            {
                using (Process p = Process.GetProcessById(pid))
                {
                    return p.ProcessName + ".exe";
                }
            }
            catch { }

            return "Application (" + pid + ")";
        }

        private static bool IsSystemProcess(string name)
        {
            if (string.IsNullOrEmpty(name)) return true;
            string low = name.ToLowerInvariant();
            return low.Contains("explorer") ||
                   low.Contains("taskmgr") ||
                   low.Contains("aspiresense") ||
                   low.Contains("firefox") ||
                   low.Contains("chrome") ||
                   low.Contains("msedge") ||
                   low.Contains("brave") ||
                   low.Contains("opera") ||
                   low.Contains("compattel") ||
                   low.Contains("wmiprvse") ||
                   low.Contains("devenv") ||
                   low.Contains("cmd.exe") ||
                   low.Contains("powershell") ||
                   low.Contains("conhost") ||
                   low.Contains("searchhost") ||
                   low.Contains("textinputhost") ||
                   low.Contains("shellexperiencehost") ||
                   low.Contains("startmenuexperiencehost") ||
                   low.Contains("applicationframehost") ||
                   low.Contains("lockapp") ||
                   low.Contains("dwm");
        }

        public static void UpdateMetrics()
        {
            try
            {
                IntPtr fgHwnd = GetForegroundWindow();
                if (fgHwnd != IntPtr.Zero)
                {
                    uint pid;
                    GetWindowThreadProcessId(fgHwnd, out pid);
                    if (pid != 0 && pid != (uint)selfPid)
                    {
                        string pName = GetProcessNameSafe((int)pid);
                        if (!IsSystemProcess(pName))
                        {
                            if (targetGamePid != pid)
                            {
                                lock (frameLock)
                                {
                                    frameTimestamps.Clear();
                                    recentFrameTimes.Clear();
                                    rollingFpsHistory.Clear();
                                    lastFrameTimestamp = 0;
                                    lastFrameWallClock = 0;
                                    wasInLoadingScreen = false;
                                }
                            }
                            targetGamePid = pid;
                            ActiveProcessId = pid;
                            ActiveProcessName = pName;
                        }
                    }
                }
            }
            catch { }

            long now = Stopwatch.GetTimestamp();
            long timeoutLimit = (long)(Stopwatch.Frequency * 1.5);

            lock (frameLock)
            {
                // If no frame has arrived for 1.5 seconds, game is idle/paused/closed
                if (lastFrameWallClock == 0 || (now - lastFrameWallClock) > timeoutLimit)
                {
                    CurrentFps = 0.0;
                    AvgFps = 0.0;
                    OnePercentLowFps = 0.0;
                    CurrentFrameTimeMs = 0.0;
                    rollingFpsHistory.Clear();
                    recentFrameTimes.Clear();
                    wasInLoadingScreen = true;
                }
                else
                {
                    // 1. Current FPS: 1.0-second rolling window across recent frames (rock-solid, zero micro-hiccups)
                    if (recentFrameTimes.Count >= 4)
                    {
                        double sumMs = 0;
                        int sampleCount = 0;
                        for (int i = recentFrameTimes.Count - 1; i >= 0; i--)
                        {
                            sumMs += recentFrameTimes[i];
                            sampleCount++;
                            if (sumMs >= 1000.0) // 1.0 second window of rendered frames
                            {
                                break;
                            }
                        }

                        double measuredFps = (sumMs > 0 && sampleCount > 0) ? (sampleCount * 1000.0 / sumMs) : 0.0;
                        CurrentFps = Math.Round(measuredFps, 0);
                        CurrentFrameTimeMs = (sampleCount > 0) ? Math.Round(sumMs / sampleCount, 1) : 0.0;

                        // 2. 1% Low FPS: Lowest 1% of FPS in a 4.0-second time window
                        List<double> windowFpsList = new List<double>();
                        double windowSumMs = 0;
                        for (int i = recentFrameTimes.Count - 1; i >= 0; i--)
                        {
                            double frameMs = recentFrameTimes[i];
                            windowSumMs += frameMs;
                            if (frameMs >= 2.5 && frameMs <= 150.0) // legitimate gameplay frame times
                            {
                                windowFpsList.Add(1000.0 / frameMs);
                            }
                            if (windowSumMs >= 4000.0) // 4.0 second time window
                            {
                                break;
                            }
                        }

                        if (windowFpsList.Count >= 8)
                        {
                            windowFpsList.Sort(); // Ascending: lowest FPS first
                            int lowCount = Math.Max(1, (int)Math.Ceiling(windowFpsList.Count * 0.01));
                            double lowFpsSum = 0;
                            for (int i = 0; i < lowCount; i++)
                            {
                                lowFpsSum += windowFpsList[i];
                            }
                            double rawOneLow = Math.Round(lowFpsSum / lowCount, 0);

                            // 1% Low cannot realistically exceed Current FPS
                            rawOneLow = Math.Min(CurrentFps, rawOneLow);

                            // Smooth 1% Low with exponential moving average to prevent strobe jitter
                            if (OnePercentLowFps <= 0)
                            {
                                OnePercentLowFps = rawOneLow;
                            }
                            else
                            {
                                OnePercentLowFps = Math.Round(OnePercentLowFps * 0.65 + rawOneLow * 0.35, 0);
                            }
                        }
                        else
                        {
                            OnePercentLowFps = Math.Round(CurrentFps * 0.9, 0);
                        }

                        // 3. Average FPS: Protected against static loading screen spikes (> 260 FPS)
                        bool isLoadingScreen = (CurrentFps > 260.0);
                        if (isLoadingScreen)
                        {
                            wasInLoadingScreen = true;
                            AvgFps = CurrentFps;
                        }
                        else
                        {
                            if (wasInLoadingScreen)
                            {
                                // Clean transition from loading screen into gameplay!
                                rollingFpsHistory.Clear();
                                recentFrameTimes.Clear();
                                wasInLoadingScreen = false;
                            }

                            if (CurrentFps > 0)
                            {
                                rollingFpsHistory.Enqueue(CurrentFps);
                                if (rollingFpsHistory.Count > 60) // ~12 seconds rolling average
                                {
                                    rollingFpsHistory.Dequeue();
                                }
                                double avgSum = 0;
                                foreach (double s in rollingFpsHistory) avgSum += s;
                                AvgFps = Math.Round(avgSum / rollingFpsHistory.Count, 0);
                            }
                        }
                    }
                    else if (recentFrameTimes.Count > 0)
                    {
                        double lastMs = recentFrameTimes[recentFrameTimes.Count - 1];
                        CurrentFps = (lastMs > 0) ? Math.Round(1000.0 / lastMs, 0) : 0.0;
                        CurrentFrameTimeMs = Math.Round(lastMs, 1);
                        OnePercentLowFps = Math.Round(CurrentFps * 0.9, 0);
                        AvgFps = CurrentFps;
                    }
                }
            }
        }

        public static void Stop()
        {
            isRunning = false;
            try
            {
                if (consumerHandle != IntPtr.Zero && consumerHandle != (IntPtr)(-1))
                {
                    CloseTrace(consumerHandle);
                    consumerHandle = IntPtr.Zero;
                }
                if (sessionHandle != 0 && pProperties != IntPtr.Zero)
                {
                    ControlTrace(sessionHandle, SessionName, pProperties, EVENT_CONTROL_CODE_STOP_TRACE);
                    sessionHandle = 0;
                }
                if (pLogFile != IntPtr.Zero)
                {
                    Marshal.FreeHGlobal(pLogFile);
                    pLogFile = IntPtr.Zero;
                }
                if (pLoggerName != IntPtr.Zero)
                {
                    Marshal.FreeHGlobal(pLoggerName);
                    pLoggerName = IntPtr.Zero;
                }
                if (pProperties != IntPtr.Zero)
                {
                    Marshal.FreeHGlobal(pProperties);
                    pProperties = IntPtr.Zero;
                }
            }
            catch { }
        }
    }

    // ==========================================
    // MINIMAL COCKPIT IN-GAME METRICS OVERLAY WINDOW
    // ==========================================
    public class MetricsOverlayWindow : Window
    {
        public const int GWL_EXSTYLE = -20;
        public const int WS_EX_TOPMOST = 0x00000008;
        public const int WS_EX_TRANSPARENT = 0x00000020;
        public const int WS_EX_LAYERED = 0x00080000;
        public const int WS_EX_NOACTIVATE = 0x08000000;
        public const int WS_EX_TOOLWINDOW = 0x00000080;

        [DllImport("user32.dll", SetLastError = true)]
        public static extern int GetWindowLong(IntPtr hWnd, int nIndex);

        [DllImport("user32.dll", SetLastError = true)]
        public static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);

        private Border rootBorder;
        private StackPanel stackHorizontal;
        private Grid gridMatrix;

        private TextBlock txtFps;
        private TextBlock txtAvgFps;
        private TextBlock txtOneLow;
        private TextBlock txtGpuTemp;
        private TextBlock txtGpuUsage;
        private TextBlock txtGpuWatts;
        private TextBlock txtCpuTemp;
        private TextBlock txtCpuUsage;
        private TextBlock txtCpuWatts;
        private TextBlock txtRamUsage;

        private TextBlock txtMatrixFps;
        private TextBlock txtMatrixTarget;
        private TextBlock txtMatrixGpu;
        private TextBlock txtMatrixCpu;
        private TextBlock txtMatrixRam;

        private OverlayPosition currentPosition = OverlayPosition.TopLeft;
        private OverlayStyle currentStyle = OverlayStyle.HorizontalBar;

        public MetricsOverlayWindow()
        {
            this.Title = "AspireSense In-Game HUD";
            this.WindowStyle = WindowStyle.None;
            this.AllowsTransparency = true;
            this.Background = Brushes.Transparent;
            this.Topmost = true;
            this.ShowInTaskbar = false;
            this.SizeToContent = SizeToContent.WidthAndHeight;

            BuildUI();
        }

        protected override void OnSourceInitialized(EventArgs e)
        {
            base.OnSourceInitialized(e);
            try
            {
                IntPtr hwnd = new WindowInteropHelper(this).Handle;
                int exStyle = GetWindowLong(hwnd, GWL_EXSTYLE);
                SetWindowLong(hwnd, GWL_EXSTYLE, exStyle | WS_EX_TOPMOST | WS_EX_TRANSPARENT | WS_EX_LAYERED | WS_EX_NOACTIVATE | WS_EX_TOOLWINDOW);
            }
            catch { }
            Reposition();
        }

        private void BuildUI()
        {
            rootBorder = new Border
            {
                Background = new SolidColorBrush(Color.FromArgb(235, 11, 15, 25)),
                BorderBrush = new SolidColorBrush(Color.FromArgb(140, 31, 41, 61)),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(8),
                Padding = new Thickness(12, 6, 12, 6),
                Effect = new DropShadowEffect { Color = Colors.Black, BlurRadius = 14, ShadowDepth = 2, Opacity = 0.8 }
            };

            // 1. Horizontal HUD Bar (Single line precision telemetry)
            stackHorizontal = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };

            // FPS Block
            StackPanel spFps = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(2, 0, 6, 0) };
            spFps.Children.Add(new TextBlock { Text = "FPS ", FontSize = 10, FontWeight = FontWeights.SemiBold, Foreground = DesignTokens.BrushTextSecondary, VerticalAlignment = VerticalAlignment.Center });
            txtFps = new TextBlock { Text = "--", FontSize = 14, FontWeight = FontWeights.Bold, Foreground = DesignTokens.BrushAccentGreen, VerticalAlignment = VerticalAlignment.Center };
            spFps.Children.Add(txtFps);
            stackHorizontal.Children.Add(spFps);

            // AVG FPS Block
            StackPanel spAvg = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(4, 0, 6, 0) };
            spAvg.Children.Add(new TextBlock { Text = "AVG ", FontSize = 10, FontWeight = FontWeights.SemiBold, Foreground = DesignTokens.BrushTextSecondary, VerticalAlignment = VerticalAlignment.Center });
            txtAvgFps = new TextBlock { Text = "--", FontSize = 12, FontWeight = FontWeights.Bold, Foreground = DesignTokens.BrushAccentBlue, VerticalAlignment = VerticalAlignment.Center };
            spAvg.Children.Add(txtAvgFps);
            stackHorizontal.Children.Add(spAvg);

            // 1% Low Block
            StackPanel spOneLow = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(4, 0, 6, 0) };
            spOneLow.Children.Add(new TextBlock { Text = "1% ", FontSize = 10, FontWeight = FontWeights.SemiBold, Foreground = DesignTokens.BrushTextSecondary, VerticalAlignment = VerticalAlignment.Center });
            txtOneLow = new TextBlock { Text = "--", FontSize = 12, FontWeight = FontWeights.Bold, Foreground = DesignTokens.BrushAccentCyan, VerticalAlignment = VerticalAlignment.Center };
            spOneLow.Children.Add(txtOneLow);
            stackHorizontal.Children.Add(spOneLow);
            stackHorizontal.Children.Add(CreateDivider());

            // GPU Block
            StackPanel spGpu = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(6, 0, 6, 0) };
            spGpu.Children.Add(new TextBlock { Text = "GPU ", FontSize = 10, FontWeight = FontWeights.SemiBold, Foreground = DesignTokens.BrushAccentCyan, VerticalAlignment = VerticalAlignment.Center });
            txtGpuTemp = new TextBlock { Text = "--°C", FontSize = 12, FontWeight = FontWeights.Bold, Foreground = DesignTokens.BrushTextPrimary, VerticalAlignment = VerticalAlignment.Center };
            spGpu.Children.Add(txtGpuTemp);
            txtGpuUsage = new TextBlock { Text = "0%", FontSize = 11, FontWeight = FontWeights.SemiBold, Foreground = DesignTokens.BrushAccentAmber, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(5, 0, 0, 0) };
            spGpu.Children.Add(txtGpuUsage);
            txtGpuWatts = new TextBlock { Text = "0W", FontSize = 11, FontWeight = FontWeights.SemiBold, Foreground = DesignTokens.BrushAccentCyan, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(5, 0, 0, 0) };
            spGpu.Children.Add(txtGpuWatts);
            stackHorizontal.Children.Add(spGpu);
            stackHorizontal.Children.Add(CreateDivider());

            // CPU Block
            StackPanel spCpu = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(6, 0, 6, 0) };
            spCpu.Children.Add(new TextBlock { Text = "CPU ", FontSize = 10, FontWeight = FontWeights.SemiBold, Foreground = DesignTokens.BrushAccentBlue, VerticalAlignment = VerticalAlignment.Center });
            txtCpuTemp = new TextBlock { Text = "--°C", FontSize = 12, FontWeight = FontWeights.Bold, Foreground = DesignTokens.BrushTextPrimary, VerticalAlignment = VerticalAlignment.Center };
            spCpu.Children.Add(txtCpuTemp);
            txtCpuUsage = new TextBlock { Text = "0%", FontSize = 11, FontWeight = FontWeights.SemiBold, Foreground = DesignTokens.BrushAccentBlue, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(5, 0, 0, 0) };
            spCpu.Children.Add(txtCpuUsage);
            txtCpuWatts = new TextBlock { Text = "0W", FontSize = 11, FontWeight = FontWeights.SemiBold, Foreground = DesignTokens.BrushAccentGreen, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(5, 0, 0, 0) };
            spCpu.Children.Add(txtCpuWatts);
            stackHorizontal.Children.Add(spCpu);
            stackHorizontal.Children.Add(CreateDivider());

            // RAM Block
            StackPanel spRam = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(6, 0, 2, 0) };
            spRam.Children.Add(new TextBlock { Text = "RAM ", FontSize = 10, FontWeight = FontWeights.SemiBold, Foreground = DesignTokens.BrushAccentPurple, VerticalAlignment = VerticalAlignment.Center });
            txtRamUsage = new TextBlock { Text = "-- GB", FontSize = 11.5, FontWeight = FontWeights.Bold, Foreground = DesignTokens.BrushTextPrimary, VerticalAlignment = VerticalAlignment.Center };
            spRam.Children.Add(txtRamUsage);
            stackHorizontal.Children.Add(spRam);

            // 2. Compact Matrix Layout (2-row badge)
            gridMatrix = new Grid { Visibility = Visibility.Collapsed, MinWidth = 230 };
            gridMatrix.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            gridMatrix.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            Grid topRow = new Grid { Margin = new Thickness(0, 0, 0, 4) };
            topRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            topRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            txtMatrixFps = new TextBlock { Text = "FPS: -- (1% Low: --)", FontSize = 12, FontWeight = FontWeights.Bold, Foreground = DesignTokens.BrushAccentGreen };
            Grid.SetColumn(txtMatrixFps, 0);
            topRow.Children.Add(txtMatrixFps);

            txtMatrixTarget = new TextBlock { Text = "Desktop", FontSize = 10, FontWeight = FontWeights.SemiBold, Foreground = DesignTokens.BrushTextSecondary };
            Grid.SetColumn(txtMatrixTarget, 1);
            topRow.Children.Add(txtMatrixTarget);

            Grid.SetRow(topRow, 0);
            gridMatrix.Children.Add(topRow);

            StackPanel botRow = new StackPanel { Orientation = Orientation.Horizontal };
            txtMatrixGpu = new TextBlock { Text = "GPU: --°C (0%)", FontSize = 10, FontWeight = FontWeights.SemiBold, Foreground = DesignTokens.BrushAccentCyan, Margin = new Thickness(0, 0, 8, 0) };
            txtMatrixCpu = new TextBlock { Text = "CPU: --°C (0%)", FontSize = 10, FontWeight = FontWeights.SemiBold, Foreground = DesignTokens.BrushAccentBlue, Margin = new Thickness(0, 0, 8, 0) };
            txtMatrixRam = new TextBlock { Text = "RAM: -- GB", FontSize = 10, FontWeight = FontWeights.SemiBold, Foreground = DesignTokens.BrushAccentPurple };
            botRow.Children.Add(txtMatrixGpu);
            botRow.Children.Add(txtMatrixCpu);
            botRow.Children.Add(txtMatrixRam);

            Grid.SetRow(botRow, 1);
            gridMatrix.Children.Add(botRow);

            Grid contentHolder = new Grid();
            contentHolder.Children.Add(stackHorizontal);
            contentHolder.Children.Add(gridMatrix);

            rootBorder.Child = contentHolder;
            this.Content = rootBorder;
        }

        private Border CreateDivider()
        {
            return new Border
            {
                Width = 1,
                Height = 14,
                Background = DesignTokens.BrushBorderSubtle,
                Margin = new Thickness(5, 0, 5, 0),
                VerticalAlignment = VerticalAlignment.Center
            };
        }

        public void SetPositionPreset(OverlayPosition pos)
        {
            currentPosition = pos;
            Reposition();
        }

        public void SetStylePreset(OverlayStyle style)
        {
            currentStyle = style;
            if (style == OverlayStyle.HorizontalBar)
            {
                stackHorizontal.Visibility = Visibility.Visible;
                gridMatrix.Visibility = Visibility.Collapsed;
            }
            else
            {
                stackHorizontal.Visibility = Visibility.Collapsed;
                gridMatrix.Visibility = Visibility.Visible;
            }
            this.UpdateLayout();
            Reposition();
        }

        public void Reposition()
        {
            double scrW = SystemParameters.PrimaryScreenWidth;
            double scrH = SystemParameters.PrimaryScreenHeight;
            double w = this.ActualWidth > 0 ? this.ActualWidth : 380;
            double h = this.ActualHeight > 0 ? this.ActualHeight : 45;

            switch (currentPosition)
            {
                case OverlayPosition.TopLeft:
                    this.Left = 24;
                    this.Top = 24;
                    break;
                case OverlayPosition.TopRight:
                    this.Left = scrW - w - 24;
                    this.Top = 24;
                    break;
                case OverlayPosition.CenterTop:
                    this.Left = (scrW - w) / 2.0;
                    this.Top = 16;
                    break;
                case OverlayPosition.BottomLeft:
                    this.Left = 24;
                    this.Top = scrH - h - 48;
                    break;
                case OverlayPosition.BottomRight:
                    this.Left = scrW - w - 24;
                    this.Top = scrH - h - 48;
                    break;
            }
        }

        public void UpdateMetrics(double fps, double avgFps, double oneLow, int gpuTemp, float gpuUsage, float gpuWatts, int cpuTemp, float cpuUsage, float cpuWatts, double ramGb, float ramPct, string targetName)
        {
            if (fps > 0)
            {
                txtFps.Text = string.Format("{0:F0}", fps);
                txtAvgFps.Text = string.Format("{0:F0}", avgFps > 0 ? avgFps : fps);
                txtOneLow.Text = string.Format("{0:F0}", oneLow);
                txtFps.Foreground = fps >= 60 ? new SolidColorBrush(Color.FromRgb(0, 255, 140)) : (fps >= 30 ? new SolidColorBrush(Color.FromRgb(0, 210, 255)) : new SolidColorBrush(Color.FromRgb(255, 42, 85)));
                txtAvgFps.Foreground = (avgFps >= 60 || (avgFps == 0 && fps >= 60)) ? new SolidColorBrush(Color.FromRgb(56, 189, 248)) : new SolidColorBrush(Color.FromRgb(0, 210, 255));
                txtOneLow.Foreground = oneLow >= 45 ? new SolidColorBrush(Color.FromRgb(0, 229, 255)) : new SolidColorBrush(Color.FromRgb(255, 170, 0));
            }
            else
            {
                txtFps.Text = "--";
                txtAvgFps.Text = "--";
                txtOneLow.Text = "--";
                txtFps.Foreground = new SolidColorBrush(Color.FromRgb(120, 150, 180));
                txtAvgFps.Foreground = new SolidColorBrush(Color.FromRgb(120, 150, 180));
                txtOneLow.Foreground = new SolidColorBrush(Color.FromRgb(120, 150, 180));
            }

            txtGpuTemp.Text = string.Format("{0}°C", gpuTemp);
            txtGpuUsage.Text = string.Format("{0:F0}%", gpuUsage);
            txtGpuWatts.Text = (gpuWatts > 0) ? string.Format("{0:F0}W", gpuWatts) : "0W";
            txtGpuTemp.Foreground = gpuTemp < 72 ? new SolidColorBrush(Color.FromRgb(0, 229, 255)) : (gpuTemp < 85 ? new SolidColorBrush(Color.FromRgb(255, 170, 0)) : new SolidColorBrush(Color.FromRgb(255, 42, 85)));

            txtCpuTemp.Text = string.Format("{0}°C", cpuTemp);
            txtCpuUsage.Text = string.Format("{0:F0}%", cpuUsage);
            txtCpuWatts.Text = (cpuWatts > 0) ? string.Format("{0:F0}W", cpuWatts) : "0W";
            txtCpuTemp.Foreground = cpuTemp < 75 ? new SolidColorBrush(Color.FromRgb(0, 229, 255)) : (cpuTemp < 88 ? new SolidColorBrush(Color.FromRgb(255, 170, 0)) : new SolidColorBrush(Color.FromRgb(255, 42, 85)));

            txtRamUsage.Text = string.Format("{0:F1}G ({1:F0}%)", ramGb, ramPct);

            if (txtMatrixFps != null)
            {
                txtMatrixFps.Text = (fps > 0) ? string.Format("FPS: {0:F0} (AVG: {1:F0} | 1% LOW: {2:F0})", fps, avgFps > 0 ? avgFps : fps, oneLow) : "FPS: -- (AVG: -- | 1% LOW: --)";
                txtMatrixTarget.Text = !string.IsNullOrEmpty(targetName) ? targetName : "Desktop";
                txtMatrixGpu.Text = string.Format("GPU: {0}°C ({1:F0}% | {2:F0}W)", gpuTemp, gpuUsage, gpuWatts);
                txtMatrixCpu.Text = string.Format("CPU: {0}°C ({1:F0}% | {2:F0}W)", cpuTemp, cpuUsage, cpuWatts);
                txtMatrixRam.Text = string.Format("RAM: {0:F1} GB ({1:F0}%)", ramGb, ramPct);
            }
        }
    }

    // ==========================================
    // MAIN APPLICATION WINDOW: ASPIRESENSE
    // ==========================================
    public class MainWindow : Window
    {
        private DispatcherTimer timer;
        private PerformanceCounter cpuCounter;

        // Navigation Tabs & View Containers
        private List<Border> navTabs = new List<Border>();
        private Grid viewDashboard;
        private Grid viewCooling;
        private Grid viewGaming;
        private Grid viewHealth;
        private Grid viewEcGuard;

        // Dashboard EC Guard Controls
        private Border btnDashboardEcGuard;
        private TextBlock lblDashboardEcGuardIndicator;
        private TextBlock lblDashboardEcGuardStatus;
        private Border btnToolEcGuard;

        // EC Guard View (Tab 4) Controls
        private TextBlock lblEcRawDie;
        private TextBlock lblEcThermalEma;
        private TextBlock lblEcSpikesCount;
        private TextBlock lblEcAcousticHold;
        private TextBlock lblEcClassifierState;
        private SegmentedMeter meterHeatsinkSaturation;
        private TextBlock lblHeatsinkSatText;
        private Border btnEcMasterToggle;
        private TextBlock lblEcMasterToggleText;
        private List<Border> ecClampButtons = new List<Border>();
        private List<Border> ecHysteresisButtons = new List<Border>();
        private List<Border> ecFailsafeButtons = new List<Border>();
        private Border btnEcGameToggle;
        private TextBlock lblEcGameToggleText;
        private Border btnEcBoostTamer;
        private TextBlock lblEcBoostTamerText;
        private TextBlock lblEcBoostTamerStatus;
        private TextBlock txtDecisionLog;

        // Dashboard Speedometers
        private SpeedometerGauge cpuSpeedo;
        private SpeedometerGauge gpuSpeedo;

        // Cooling View Specific Controls
        private TextBlock lblCoolingCpuRpm;
        private SegmentedMeter meterCoolingCpu;
        private TextBlock lblCoolingGpuRpm;
        private SegmentedMeter meterCoolingGpu;
        private Slider sliderDedicatedCustom;
        private TextBlock lblDedicatedCustomVal;
        private Border btnHeatFlush;
        private TextBlock lblHeatFlushText;
        private int heatFlushRemaining = 0;
        private List<Border> curveCards = new List<Border>();

        // Gaming Hub & In-Game Overlay Controls
        private MetricsOverlayWindow overlayWindow;
        private DispatcherTimer overlayTimer;
        private bool isOverlayActive = false;
        private OverlayPosition currentOverlayPos = OverlayPosition.TopLeft;
        private OverlayStyle currentOverlayStyle = OverlayStyle.HorizontalBar;

        private Border btnOverlayMasterToggle;
        private TextBlock lblOverlayMasterToggleText;
        private TextBlock lblOverlayStatusSub;

        private Border btnPosTL, btnPosTR, btnPosTC, btnPosBL, btnPosBR;
        private Border btnStyleHoriz, btnStyleMatrix;

        private TextBlock lblHubFpsVal;
        private TextBlock lblHubOneLowVal;
        private TextBlock lblHubFrameTimeVal;
        private SegmentedMeter meterHubStability;
        private TextBlock lblHubGpuVal;
        private SegmentedMeter meterHubGpu;
        private TextBlock lblHubCpuVal;
        private SegmentedMeter meterHubCpu;
        private TextBlock lblHubRamVal;
        private SegmentedMeter meterHubRam;
        private TextBlock lblHubTargetApp;
        private TextBlock lblHubEngineStatus;

        public const uint MOD_CONTROL = 0x0002;
        public const uint MOD_SHIFT = 0x0004;
        public const uint MOD_NOREPEAT = 0x4000;
        public const int WM_HOTKEY = 0x0312;
        public const uint VK_O = 0x4F;

        [DllImport("user32.dll")]
        public static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

        [DllImport("user32.dll")]
        public static extern bool UnregisterHotKey(IntPtr hWnd, int id);

        private TextBlock lblScenarioStatus;
        private TextBlock lblGamePing;
        private int pingTick = 0;
        private int lastPingMs = 24;

        // Health Controls (Minimalist & Advanced Telemetry)
        private TextBlock lblHealthUptime;
        private TextBlock lblHealthVramText;
        private SegmentedMeter meterHealthVram;
        private TextBlock lblHealthGpuCore;
        private TextBlock lblHealthGpuMem;
        private TextBlock lblHealthGpuThrottle;
        private TextBlock lblHealthGpuHeadroom;

        private TextBlock lblHealthRamText;
        private SegmentedMeter meterHealthRam;
        private TextBlock lblTopProc1;
        private TextBlock lblTopProc2;
        private TextBlock lblTopProc3;

        private TextBlock lblHealthBatteryText;
        private SegmentedMeter meterHealthBattery;
        private TextBlock lblHealthBatteryWear;
        private TextBlock lblHealthCpuHeadroom;
        private TextBlock lblHealthDiskText;
        private SegmentedMeter meterHealthDisk;
        private HardwareBridge.SensorData latestSensorData;

        // Center Fan Mode Buttons
        private Border btnAuto;
        private Border btnQuiet;
        private Border btnMax;

        private TextBlock lblModeDesc;
        private TextBlock lblDiagPower;
        private TextBlock lblDiagBattery;
        private TextBlock lblDiagUptime;

        private Slider sliderCustom;
        private TextBlock lblCustomVal;
        private TextBlock lblStatusMsg;
        private TextBlock lblRamStats;

        private bool winKeyLocked = false;
        private Border btnWinLock;

        private int currentFanMode = 0; // 0=Auto, 1=Max, 3=Quiet, 6=Custom
        private int currentCustomDuty = 75;
        private int watchdogHighHeatCount = 0;
        private int watchdogCoolCount = 0;
        private bool watchdogActive = false;
        private int watchdogPreviousMode = 0;

        public MainWindow()
        {
            this.Title = "ASPIRESENSE";
            this.Width = 1180;
            this.Height = 720;
            this.WindowStartupLocation = WindowStartupLocation.CenterScreen;
            this.WindowStyle = WindowStyle.None;
            this.AllowsTransparency = true;
            this.Background = new SolidColorBrush(Color.FromRgb(7, 10, 18));
            this.BorderBrush = new SolidColorBrush(Color.FromRgb(0, 140, 240));
            this.BorderThickness = new Thickness(1);

            string icoPath = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "app.ico");
            if (File.Exists(icoPath))
            {
                try
                {
                    this.Icon = new System.Windows.Media.Imaging.BitmapImage(new Uri(icoPath, UriKind.Absolute));
                }
                catch { }
            }

            try
            {
                cpuCounter = new PerformanceCounter("Processor", "% Processor Time", "_Total");
                cpuCounter.NextValue();
            }
            catch { }

            HardwareBridge.InitializeHardware();
            HardwareBridge.ResetToAuto();
            overlayWindow = new MetricsOverlayWindow();

            BuildLayout();

            EcAntiHallucinationEngine.CheckCurrentPowerPlanState();
            EcAntiHallucinationEngine.LogDecision("ASPIRASENSE ARBITER INITIALIZED: Monitoring real thermal energy vs transient bursts");
            UpdateDashboardEcGuardUi();

            timer = new DispatcherTimer();
            timer.Interval = TimeSpan.FromSeconds(1);
            timer.Tick += Timer_Tick;
            timer.Start();

            overlayTimer = new DispatcherTimer();
            overlayTimer.Interval = TimeSpan.FromMilliseconds(250);
            overlayTimer.Tick += OverlayTimer_Tick;
            // Note: overlayTimer and GameTelemetryEngine are started on-demand via EnsureTelemetryState()

            // CRITICAL HARDWARE SAFETY: Reset EC fan controller to Auto when app is closed, terminated, or crashes
            this.Closing += (s, e) =>
            {
                if (timer != null)
                {
                    timer.Stop();
                }
                if (overlayTimer != null)
                {
                    overlayTimer.Stop();
                }
                if (overlayWindow != null)
                {
                    try { overlayWindow.Close(); } catch { }
                    overlayWindow = null;
                }
                GameTelemetryEngine.Stop();

                try
                {
                    IntPtr hwnd = new WindowInteropHelper(this).Handle;
                    if (hwnd != IntPtr.Zero)
                    {
                        UnregisterHotKey(hwnd, 9001);
                    }
                }
                catch { }

                if (cpuCounter != null)
                {
                    try { cpuCounter.Dispose(); } catch { }
                    cpuCounter = null;
                }
                HardwareBridge.CloseHardware();
            };

            AppDomain.CurrentDomain.ProcessExit += (s, e) =>
            {
                HardwareBridge.CloseHardware();
            };

            AppDomain.CurrentDomain.UnhandledException += (s, e) =>
            {
                HardwareBridge.CloseHardware();
            };

            Timer_Tick(null, null);
        }

        protected override void OnSourceInitialized(EventArgs e)
        {
            base.OnSourceInitialized(e);
            try
            {
                HwndSource source = HwndSource.FromHwnd(new WindowInteropHelper(this).Handle);
                if (source != null)
                {
                    source.AddHook(HwndHook);
                    RegisterHotKey(source.Handle, 9001, MOD_CONTROL | MOD_SHIFT | MOD_NOREPEAT, VK_O);
                }
            }
            catch { }
        }

        private IntPtr HwndHook(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            if (msg == WM_HOTKEY && wParam.ToInt32() == 9001)
            {
                ToggleOverlay();
                handled = true;
            }
            return IntPtr.Zero;
        }

        private void EnsureTelemetryState()
        {
            bool needTelemetry = isOverlayActive || (viewGaming != null && viewGaming.Visibility == Visibility.Visible);
            if (needTelemetry)
            {
                GameTelemetryEngine.Start();
                if (overlayTimer != null && !overlayTimer.IsEnabled)
                {
                    overlayTimer.Start();
                }
            }
            else
            {
                GameTelemetryEngine.Stop();
                if (overlayTimer != null && overlayTimer.IsEnabled)
                {
                    overlayTimer.Stop();
                }
            }
        }

        private void ToggleOverlay()
        {
            isOverlayActive = !isOverlayActive;
            if (overlayWindow != null)
            {
                if (isOverlayActive)
                {
                    overlayWindow.Show();
                    overlayWindow.Reposition();
                }
                else
                {
                    overlayWindow.Hide();
                }
            }
            UpdateOverlayToggleButton();
            EnsureTelemetryState();
        }

        private void UpdateOverlayToggleButton()
        {
            if (btnOverlayMasterToggle == null || lblOverlayMasterToggleText == null) return;
            if (isOverlayActive)
            {
                btnOverlayMasterToggle.Background = new SolidColorBrush(Color.FromArgb(200, 10, 45, 30));
                btnOverlayMasterToggle.BorderBrush = new SolidColorBrush(Color.FromRgb(0, 255, 140));
                lblOverlayMasterToggleText.Text = "🟢 IN-GAME OVERLAY: ACTIVE (ON)";
                lblOverlayMasterToggleText.Foreground = new SolidColorBrush(Color.FromRgb(0, 255, 140));
                if (lblOverlayStatusSub != null)
                {
                    lblOverlayStatusSub.Text = "HUD ACTIVE ON DISPLAY • PRESS [CTRL + SHIFT + O] TO HIDE";
                    lblOverlayStatusSub.Foreground = new SolidColorBrush(Color.FromRgb(0, 255, 140));
                }
            }
            else
            {
                btnOverlayMasterToggle.Background = new SolidColorBrush(Color.FromArgb(160, 14, 20, 32));
                btnOverlayMasterToggle.BorderBrush = new SolidColorBrush(Color.FromArgb(90, 0, 180, 255));
                lblOverlayMasterToggleText.Text = "⚪ IN-GAME OVERLAY: STANDBY (OFF)";
                lblOverlayMasterToggleText.Foreground = new SolidColorBrush(Color.FromRgb(150, 185, 220));
                if (lblOverlayStatusSub != null)
                {
                    lblOverlayStatusSub.Text = "GLOBAL HOTKEY: PRESS [CTRL + SHIFT + O] TO ACTIVATE ANYTIME";
                    lblOverlayStatusSub.Foreground = new SolidColorBrush(Color.FromRgb(110, 150, 190));
                }
            }
        }

        private void SetOverlayPosition(OverlayPosition pos)
        {
            currentOverlayPos = pos;
            if (overlayWindow != null)
            {
                overlayWindow.SetPositionPreset(pos);
            }
            UpdatePositionButtonStyles();
        }

        private void UpdatePositionButtonStyles()
        {
            SetPillStyle(btnPosTL, currentOverlayPos == OverlayPosition.TopLeft);
            SetPillStyle(btnPosTR, currentOverlayPos == OverlayPosition.TopRight);
            SetPillStyle(btnPosTC, currentOverlayPos == OverlayPosition.CenterTop);
            SetPillStyle(btnPosBL, currentOverlayPos == OverlayPosition.BottomLeft);
            SetPillStyle(btnPosBR, currentOverlayPos == OverlayPosition.BottomRight);
        }

        private void SetOverlayStyle(OverlayStyle style)
        {
            currentOverlayStyle = style;
            if (overlayWindow != null)
            {
                overlayWindow.SetStylePreset(style);
            }
            SetPillStyle(btnStyleHoriz, currentOverlayStyle == OverlayStyle.HorizontalBar);
            SetPillStyle(btnStyleMatrix, currentOverlayStyle == OverlayStyle.CompactMatrix);
        }

        private void SetPillStyle(Border pill, bool isActive)
        {
            if (pill == null) return;
            pill.Background = isActive ? new SolidColorBrush(Color.FromArgb(190, 0, 110, 180)) : new SolidColorBrush(Color.FromArgb(140, 14, 22, 35));
            pill.BorderBrush = isActive ? new SolidColorBrush(Color.FromRgb(0, 229, 255)) : new SolidColorBrush(Color.FromArgb(60, 0, 160, 240));
            TextBlock tb = pill.Child as TextBlock;
            if (tb != null)
            {
                tb.Foreground = isActive ? Brushes.White : new SolidColorBrush(Color.FromRgb(130, 165, 205));
                tb.FontWeight = isActive ? FontWeights.Bold : FontWeights.Normal;
            }
        }

        private void BuildLayout()
        {
            Grid root = new Grid();

            RadialGradientBrush bgGradient = new RadialGradientBrush
            {
                Center = new Point(0.5, 0.4),
                RadiusX = 0.9,
                RadiusY = 0.8,
                GradientStops = new GradientStopCollection
                {
                    new GradientStop(Color.FromArgb(30, 0, 100, 200), 0.0),
                    new GradientStop(Color.FromRgb(12, 16, 28), 0.6),
                    new GradientStop(DesignTokens.BgWindow, 1.0)
                }
            };
            root.Background = bgGradient;

            root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(56) });  // Titlebar
            root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(32) });  // Sub-banner
            root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) }); // Viewport
            root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(32) });  // Footer Bar

            Grid topBar = CreateTopBar();
            Grid.SetRow(topBar, 0);
            root.Children.Add(topBar);

            Grid subBanner = CreateSubBanner();
            Grid.SetRow(subBanner, 1);
            root.Children.Add(subBanner);

            // Container for all functional tab pages
            Grid viewHost = new Grid();

            viewDashboard = CreateDashboardView();
            viewCooling = CreateCoolingView();
            viewGaming = CreateGamingView();
            viewHealth = CreateHealthView();
            viewEcGuard = CreateEcGuardView();

            viewHost.Children.Add(viewDashboard);
            viewHost.Children.Add(viewCooling);
            viewHost.Children.Add(viewGaming);
            viewHost.Children.Add(viewHealth);
            viewHost.Children.Add(viewEcGuard);

            // Default: Show Dashboard
            SwitchTab(0);

            Grid.SetRow(viewHost, 2);
            root.Children.Add(viewHost);

            Grid footer = CreateFooter();
            Grid.SetRow(footer, 3);
            root.Children.Add(footer);

            this.Content = root;
        }

        private Grid CreateTopBar()
        {
            Grid bar = new Grid { Background = DesignTokens.BrushBgWindow };
            bar.MouseDown += (s, e) => { if (e.LeftButton == MouseButtonState.Pressed) this.DragMove(); };

            bar.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(200) });
            bar.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            bar.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(120) });

            StackPanel logoPanel = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(20, 0, 0, 0)
            };

            string logoPath = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "logo.png");
            if (File.Exists(logoPath))
            {
                try
                {
                    System.Windows.Media.Imaging.BitmapImage bmp = new System.Windows.Media.Imaging.BitmapImage();
                    bmp.BeginInit();
                    bmp.UriSource = new Uri(logoPath, UriKind.Absolute);
                    bmp.CacheOption = System.Windows.Media.Imaging.BitmapCacheOption.OnLoad;
                    bmp.EndInit();

                    Image imgLogo = new Image
                    {
                        Source = bmp,
                        Width = 26,
                        Height = 26,
                        Margin = new Thickness(0, 0, 10, 0)
                    };
                    logoPanel.Children.Add(imgLogo);
                }
                catch { }
            }
            else
            {
                Path aLogo = new Path
                {
                    Width = 22,
                    Height = 22,
                    Stretch = Stretch.Fill,
                    Fill = DesignTokens.BrushAccentCyan,
                    Data = Geometry.Parse("M 12,2 L 22,22 L 17,22 L 14,15 L 10,15 L 7,22 L 2,22 Z M 12,8 L 10.5,12 L 13.5,12 Z"),
                    Margin = new Thickness(0, 0, 10, 0)
                };
                logoPanel.Children.Add(aLogo);
            }

            TextBlock txtLogo = new TextBlock
            {
                Text = "AspireSense",
                FontSize = 16,
                FontWeight = FontWeights.Bold,
                Foreground = DesignTokens.BrushTextPrimary,
                VerticalAlignment = VerticalAlignment.Center
            };
            logoPanel.Children.Add(txtLogo);
            Grid.SetColumn(logoPanel, 0);
            bar.Children.Add(logoPanel);

            // Fully Functional Tabs in Segmented Pill Container
            Border pillContainer = new Border
            {
                Background = DesignTokens.BrushBgInput,
                BorderBrush = DesignTokens.BrushBorderSubtle,
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(10),
                Padding = new Thickness(3),
                VerticalAlignment = VerticalAlignment.Center,
                HorizontalAlignment = HorizontalAlignment.Left,
                Margin = new Thickness(16, 0, 0, 0)
            };

            StackPanel tabs = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                VerticalAlignment = VerticalAlignment.Center
            };

            navTabs.Add(CreateTab("Dashboard", 0));
            navTabs.Add(CreateTab("Cooling Control", 1));
            navTabs.Add(CreateTab("Gaming Hub", 2));
            navTabs.Add(CreateTab("System Health", 3));
            navTabs.Add(CreateTab("🛡️ EC Arbiter", 4));

            foreach (Border tab in navTabs)
            {
                tabs.Children.Add(tab);
            }
            pillContainer.Child = tabs;

            Grid.SetColumn(pillContainer, 1);
            bar.Children.Add(pillContainer);

            StackPanel winBtnSp = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                VerticalAlignment = VerticalAlignment.Center,
                HorizontalAlignment = HorizontalAlignment.Right,
                Margin = new Thickness(0, 0, 12, 0)
            };

            Button btnMin = new Button
            {
                Content = "—",
                Width = 36,
                Height = 30,
                Foreground = DesignTokens.BrushTextSecondary,
                Background = Brushes.Transparent,
                BorderThickness = new Thickness(0),
                Cursor = Cursors.Hand,
                FontWeight = FontWeights.Bold
            };
            btnMin.MouseEnter += (s, e) => { btnMin.Background = DesignTokens.BrushBgCardHover; btnMin.Foreground = DesignTokens.BrushTextPrimary; };
            btnMin.MouseLeave += (s, e) => { btnMin.Background = Brushes.Transparent; btnMin.Foreground = DesignTokens.BrushTextSecondary; };
            btnMin.Click += (s, e) => { this.WindowState = WindowState.Minimized; };
            winBtnSp.Children.Add(btnMin);

            Button btnClose = new Button
            {
                Content = "✕",
                Width = 36,
                Height = 30,
                Foreground = DesignTokens.BrushTextSecondary,
                Background = Brushes.Transparent,
                BorderThickness = new Thickness(0),
                Cursor = Cursors.Hand,
                FontWeight = FontWeights.Bold
            };
            btnClose.MouseEnter += (s, e) => { btnClose.Background = DesignTokens.BrushAccentRed; btnClose.Foreground = Brushes.White; };
            btnClose.MouseLeave += (s, e) => { btnClose.Background = Brushes.Transparent; btnClose.Foreground = DesignTokens.BrushTextSecondary; };
            btnClose.Click += (s, e) => { this.Close(); };
            winBtnSp.Children.Add(btnClose);

            Grid.SetColumn(winBtnSp, 2);
            bar.Children.Add(winBtnSp);

            return bar;
        }

        private Border CreateTab(string title, int index)
        {
            Border b = new Border
            {
                Margin = new Thickness(2, 0, 2, 0),
                Padding = new Thickness(14, 6, 14, 6),
                BorderThickness = new Thickness(1),
                BorderBrush = Brushes.Transparent,
                CornerRadius = new CornerRadius(7),
                Cursor = Cursors.Hand,
                Tag = index
            };
            TextBlock t = new TextBlock
            {
                Text = title,
                FontSize = 12,
                FontWeight = FontWeights.Medium,
                Foreground = DesignTokens.BrushTextSecondary
            };
            b.Child = t;

            b.MouseEnter += (s, e) => {
                if (b.BorderBrush == Brushes.Transparent)
                {
                    b.Background = DesignTokens.BrushBgCardHover;
                    t.Foreground = DesignTokens.BrushTextPrimary;
                }
            };
            b.MouseLeave += (s, e) => {
                if (b.BorderBrush == Brushes.Transparent)
                {
                    b.Background = Brushes.Transparent;
                    t.Foreground = DesignTokens.BrushTextSecondary;
                }
            };

            b.MouseDown += (s, e) => { SwitchTab(index); };
            return b;
        }

        private void SwitchTab(int tabIndex)
        {
            viewDashboard.Visibility = (tabIndex == 0) ? Visibility.Visible : Visibility.Collapsed;
            viewCooling.Visibility = (tabIndex == 1) ? Visibility.Visible : Visibility.Collapsed;
            viewGaming.Visibility = (tabIndex == 2) ? Visibility.Visible : Visibility.Collapsed;
            viewHealth.Visibility = (tabIndex == 3) ? Visibility.Visible : Visibility.Collapsed;
            if (viewEcGuard != null)
            {
                viewEcGuard.Visibility = (tabIndex == 4) ? Visibility.Visible : Visibility.Collapsed;
            }

            for (int i = 0; i < navTabs.Count; i++)
            {
                bool active = (i == tabIndex);
                navTabs[i].BorderBrush = active ? DesignTokens.BrushBorderSubtleLight : Brushes.Transparent;
                navTabs[i].Background = active ? DesignTokens.BrushBgCardActive : Brushes.Transparent;
                TextBlock tb = navTabs[i].Child as TextBlock;
                if (tb != null)
                {
                    tb.Foreground = active ? Brushes.White : DesignTokens.BrushTextSecondary;
                    tb.FontWeight = active ? FontWeights.Bold : FontWeights.Medium;
                }
            }
            EnsureTelemetryState();
        }

        private Grid CreateSubBanner()
        {
            Grid g = new Grid { Margin = new Thickness(30, 2, 30, 0), Height = 28 };
            Border badge = new Border
            {
                CornerRadius = new CornerRadius(12),
                Background = DesignTokens.BrushBgInput,
                BorderBrush = DesignTokens.BrushBorderSubtle,
                BorderThickness = new Thickness(1),
                Padding = new Thickness(12, 3, 12, 3),
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            };
            StackPanel sp = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center };

            TextBlock tag = new TextBlock
            {
                Text = "Active Profile: ",
                FontSize = 10.5,
                FontWeight = FontWeights.SemiBold,
                Foreground = DesignTokens.BrushTextSecondary
            };
            lblStatusMsg = new TextBlock
            {
                Text = "Auto (Balanced adaptive)",
                FontSize = 10.5,
                FontWeight = FontWeights.Bold,
                Foreground = DesignTokens.BrushAccentCyan
            };
            sp.Children.Add(tag);
            sp.Children.Add(lblStatusMsg);
            badge.Child = sp;
            g.Children.Add(badge);
            return g;
        }

        // ==========================================
        // PAGE 1: MAIN DASHBOARD VIEW (APPLE DESIGN CRAFT)
        // ==========================================
        private Grid CreateDashboardView()
        {
            Grid root = new Grid();
            root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(82) });

            Grid g = new Grid { Margin = new Thickness(30, 8, 30, 8) };
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(350) });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(350) });

            string cpuGaugeTitle = "Intel Core™ Processor";
            if (!string.IsNullOrEmpty(HardwareBridge.ChassisCpuName))
            {
                if (HardwareBridge.ChassisCpuName.IndexOf("AMD", StringComparison.OrdinalIgnoreCase) >= 0)
                    cpuGaugeTitle = "AMD Ryzen™ Processor";
                else if (HardwareBridge.ChassisCpuName.IndexOf("Intel", StringComparison.OrdinalIgnoreCase) >= 0)
                    cpuGaugeTitle = "Intel Core™ Processor";
                else
                    cpuGaugeTitle = HardwareBridge.ChassisCpuName;
            }

            string gpuGaugeTitle = !string.IsNullOrEmpty(HardwareBridge.ChassisGpuName)
                ? HardwareBridge.ChassisGpuName
                : "NVIDIA GeForce GPU";

            Border cpuBox = CreateSpeedoCard();
            cpuSpeedo = new SpeedometerGauge(cpuGaugeTitle, "CPU package & workload sensor", 320, false);
            cpuBox.Child = cpuSpeedo;
            Grid.SetColumn(cpuBox, 0);
            g.Children.Add(cpuBox);

            Border centerBox = CreateCenterControlBox();
            Grid.SetColumn(centerBox, 1);
            g.Children.Add(centerBox);

            Border gpuBox = CreateSpeedoCard();
            gpuSpeedo = new SpeedometerGauge(gpuGaugeTitle, "Hardware sensor via NVML", 320, true);
            gpuBox.Child = gpuSpeedo;
            Grid.SetColumn(gpuBox, 2);
            g.Children.Add(gpuBox);

            Grid.SetRow(g, 0);
            root.Children.Add(g);

            Grid toolsBar = CreateGamingToolsHub();
            Grid.SetRow(toolsBar, 1);
            root.Children.Add(toolsBar);

            return root;
        }

        private Border CreateSpeedoCard()
        {
            return new Border
            {
                Background = DesignTokens.BrushBgCard,
                CornerRadius = new CornerRadius(14),
                BorderBrush = DesignTokens.BrushBorderSubtle,
                BorderThickness = new Thickness(1),
                Padding = new Thickness(16),
                Effect = new DropShadowEffect
                {
                    Color = Colors.Black,
                    BlurRadius = 20,
                    ShadowDepth = 2,
                    Opacity = 0.4
                }
            };
        }

        private Border CreateCenterControlBox()
        {
            Border card = new Border
            {
                Background = DesignTokens.BrushBgCard,
                CornerRadius = new CornerRadius(14),
                BorderBrush = DesignTokens.BrushBorderSubtle,
                BorderThickness = new Thickness(1),
                Margin = new Thickness(12, 0, 12, 0),
                Padding = new Thickness(18, 16, 18, 16),
                Effect = new DropShadowEffect
                {
                    Color = Colors.Black,
                    BlurRadius = 20,
                    ShadowDepth = 2,
                    Opacity = 0.4
                }
            };

            StackPanel sp = new StackPanel { VerticalAlignment = VerticalAlignment.Center };

            // 1. Control Center Section Header
            StackPanel headSp = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 0, 0, 14) };

            Border tagBadge = new Border
            {
                CornerRadius = new CornerRadius(5),
                Background = new SolidColorBrush(Color.FromArgb(40, 0, 122, 255)),
                BorderBrush = DesignTokens.BrushAccentBlue,
                BorderThickness = new Thickness(1),
                Padding = new Thickness(8, 2, 8, 2),
                HorizontalAlignment = HorizontalAlignment.Center,
                Margin = new Thickness(0, 0, 0, 6)
            };
            TextBlock tagTxt = new TextBlock
            {
                Text = "🎛️ CONTROL CENTER",
                FontSize = 9.5,
                FontWeight = FontWeights.Bold,
                Foreground = DesignTokens.BrushAccentBlue
            };
            tagBadge.Child = tagTxt;
            headSp.Children.Add(tagBadge);

            TextBlock title = new TextBlock
            {
                Text = "Drive & Acoustic Policy",
                FontSize = 15,
                FontWeight = FontWeights.Bold,
                Foreground = DesignTokens.BrushTextPrimary,
                HorizontalAlignment = HorizontalAlignment.Center
            };
            headSp.Children.Add(title);

            lblModeDesc = new TextBlock
            {
                Text = "Auto: Adaptive closed-loop cooling balancing acoustics and silicon thermals.",
                FontSize = 10.5,
                Foreground = DesignTokens.BrushTextSecondary,
                HorizontalAlignment = HorizontalAlignment.Center,
                TextAlignment = TextAlignment.Center,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 4, 0, 0),
                MaxWidth = 360
            };
            headSp.Children.Add(lblModeDesc);

            // Dashboard EC Anti-Hallucination Guard Status & Toggle Pill
            btnDashboardEcGuard = new Border
            {
                Background = new SolidColorBrush(Color.FromArgb(40, 16, 185, 129)),
                BorderBrush = DesignTokens.BrushAccentGreen,
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(9),
                Padding = new Thickness(12, 6, 12, 6),
                Margin = new Thickness(0, 10, 0, 0),
                Cursor = Cursors.Hand
            };

            Grid ecG = new Grid();
            ecG.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            ecG.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            StackPanel ecTextSp = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            lblDashboardEcGuardIndicator = new TextBlock
            {
                Text = "🛡️ EC Guard: ACTIVE",
                FontSize = 11,
                FontWeight = FontWeights.Bold,
                Foreground = DesignTokens.BrushAccentGreen,
                Margin = new Thickness(0, 0, 8, 0)
            };
            ecTextSp.Children.Add(lblDashboardEcGuardIndicator);

            lblDashboardEcGuardStatus = new TextBlock
            {
                Text = "Whisper Silent (35%)",
                FontSize = 10.5,
                Foreground = DesignTokens.BrushTextSecondary,
                VerticalAlignment = VerticalAlignment.Center
            };
            ecTextSp.Children.Add(lblDashboardEcGuardStatus);
            Grid.SetColumn(ecTextSp, 0);
            ecG.Children.Add(ecTextSp);

            Border btnOpenArbiter = new Border
            {
                Background = DesignTokens.BrushBgCardHover,
                BorderBrush = DesignTokens.BrushBorderSubtleLight,
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(5),
                Padding = new Thickness(8, 2, 8, 2),
                VerticalAlignment = VerticalAlignment.Center,
                Cursor = Cursors.Hand
            };
            TextBlock txtArb = new TextBlock
            {
                Text = "Arbiter ❯",
                FontSize = 9.5,
                FontWeight = FontWeights.Bold,
                Foreground = DesignTokens.BrushAccentCyan
            };
            btnOpenArbiter.Child = txtArb;
            btnOpenArbiter.MouseDown += (s, e) => { SwitchTab(4); e.Handled = true; };
            Grid.SetColumn(btnOpenArbiter, 1);
            ecG.Children.Add(btnOpenArbiter);

            btnDashboardEcGuard.Child = ecG;
            btnDashboardEcGuard.MouseDown += (s, e) =>
            {
                EcAntiHallucinationEngine.Toggle();
                UpdateDashboardEcGuardUi();
            };
            headSp.Children.Add(btnDashboardEcGuard);

            sp.Children.Add(headSp);

            // 2. macOS-Style Segmented Control Bar
            Border segContainer = new Border
            {
                Background = DesignTokens.BrushBgInput,
                BorderBrush = DesignTokens.BrushBorderSubtle,
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(12),
                Padding = new Thickness(3),
                Margin = new Thickness(0, 0, 0, 16)
            };

            Grid segGrid = new Grid { Height = 44 };
            segGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            segGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            segGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            btnQuiet = CreateDriveModeButton("🌙 Quiet", "Silent", DesignTokens.AccentCyan, false, () => ApplyMode(3, "Quiet: Whisper-quiet acoustic profile with capped blower RPM."));
            btnAuto = CreateDriveModeButton("✨ Auto", "Adaptive", DesignTokens.AccentBlue, true, () => ApplyMode(0, "Auto: Adaptive closed-loop cooling balancing acoustics and silicon thermals."));
            btnMax = CreateDriveModeButton("🔥 Max", "Turbo", DesignTokens.AccentRed, false, () => ApplyMode(1, "Max: 100% emergency blower speed for sustained heavy compute."));

            Grid.SetColumn(btnQuiet, 0);
            Grid.SetColumn(btnAuto, 1);
            Grid.SetColumn(btnMax, 2);

            segGrid.Children.Add(btnQuiet);
            segGrid.Children.Add(btnAuto);
            segGrid.Children.Add(btnMax);
            segContainer.Child = segGrid;
            sp.Children.Add(segContainer);

            // 3. Manual Blower Override Capsule
            Border overrideBox = new Border
            {
                Background = DesignTokens.BrushBgInput,
                CornerRadius = new CornerRadius(10),
                BorderBrush = DesignTokens.BrushBorderSubtle,
                BorderThickness = new Thickness(1),
                Padding = new Thickness(14, 12, 14, 12),
                Margin = new Thickness(0, 0, 0, 14)
            };

            StackPanel overSp = new StackPanel();

            Grid sliderHeader = new Grid();
            sliderHeader.Children.Add(new TextBlock
            {
                Text = "Manual Fan Override",
                FontSize = 11,
                FontWeight = FontWeights.SemiBold,
                Foreground = DesignTokens.BrushTextPrimary
            });

            Border customValPill = new Border
            {
                CornerRadius = new CornerRadius(6),
                Background = new SolidColorBrush(Color.FromArgb(40, 6, 182, 212)),
                BorderBrush = DesignTokens.BrushAccentCyan,
                BorderThickness = new Thickness(1),
                Padding = new Thickness(8, 2, 8, 2),
                HorizontalAlignment = HorizontalAlignment.Right
            };
            lblCustomVal = new TextBlock
            {
                Text = "75% Duty",
                FontSize = 10.5,
                FontWeight = FontWeights.Bold,
                Foreground = DesignTokens.BrushAccentCyan
            };
            customValPill.Child = lblCustomVal;
            sliderHeader.Children.Add(customValPill);
            overSp.Children.Add(sliderHeader);

            sliderCustom = new Slider
            {
                Minimum = 30,
                Maximum = 100,
                Value = 75,
                TickFrequency = 5,
                Margin = new Thickness(0, 10, 0, 10)
            };
            sliderCustom.ValueChanged += (s, e) => { lblCustomVal.Text = string.Format("{0:F0}% Duty", sliderCustom.Value); };
            overSp.Children.Add(sliderCustom);

            Button btnApplyCustom = new Button
            {
                Content = "Apply Manual Override",
                FontSize = 11,
                FontWeight = FontWeights.Bold,
                Foreground = Brushes.White,
                Background = DesignTokens.BrushAccentBlue,
                BorderThickness = new Thickness(0),
                Height = 32,
                Cursor = Cursors.Hand
            };
            btnApplyCustom.Click += (s, e) =>
            {
                int target = (int)sliderCustom.Value;
                bool ok = HardwareBridge.SetCustomSpeed(target);
                if (ok)
                {
                    currentFanMode = 6;
                    currentCustomDuty = target;
                    lblStatusMsg.Text = string.Format("Manual Override ({0}%) Active", target);
                    if (lblModeDesc != null) lblModeDesc.Text = string.Format("Manual Override: Blowers locked at constant {0}% duty cycle.", target);
                    UpdateDriveModeButton(btnAuto, false, DesignTokens.AccentBlue);
                    UpdateDriveModeButton(btnQuiet, false, DesignTokens.AccentCyan);
                    UpdateDriveModeButton(btnMax, false, DesignTokens.AccentRed);
                }
            };
            overSp.Children.Add(btnApplyCustom);
            overrideBox.Child = overSp;
            sp.Children.Add(overrideBox);

            // 4. Live System Diagnostics Capsule Strip
            UniformGrid diagGrid = new UniformGrid { Columns = 3, Rows = 1 };

            Border podPower = CreateDiagPod("⚡ AC Power", out lblDiagPower);
            Border podBattery = CreateDiagPod("🛡️ Battery", out lblDiagBattery);
            Border podUptime = CreateDiagPod("⏱️ Uptime", out lblDiagUptime);

            diagGrid.Children.Add(podPower);
            diagGrid.Children.Add(podBattery);
            diagGrid.Children.Add(podUptime);
            sp.Children.Add(diagGrid);

            card.Child = sp;
            return card;
        }

        private Border CreateDiagPod(string initialText, out TextBlock tb)
        {
            Border b = new Border
            {
                Background = DesignTokens.BrushBgInput,
                BorderBrush = DesignTokens.BrushBorderSubtle,
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(6),
                Padding = new Thickness(4, 5, 4, 5),
                Margin = new Thickness(2, 0, 2, 0)
            };
            tb = new TextBlock
            {
                Text = initialText,
                FontSize = 9.5,
                FontWeight = FontWeights.SemiBold,
                Foreground = DesignTokens.BrushTextSecondary,
                HorizontalAlignment = HorizontalAlignment.Center
            };
            b.Child = tb;
            return b;
        }

        private Border CreateDriveModeButton(string text, string subtitle, Color accent, bool isSelected, Action onClick)
        {
            Border b = new Border
            {
                Background = isSelected ? new SolidColorBrush(accent) : Brushes.Transparent,
                BorderBrush = isSelected ? new SolidColorBrush(accent) : Brushes.Transparent,
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(9),
                Margin = new Thickness(2),
                Cursor = Cursors.Hand,
                Tag = isSelected,
                Effect = isSelected ? new DropShadowEffect { Color = accent, BlurRadius = 8, ShadowDepth = 0, Opacity = 0.4 } : null
            };

            StackPanel sp = new StackPanel { VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center };
            TextBlock tb = new TextBlock
            {
                Text = text,
                FontSize = 11.5,
                FontWeight = isSelected ? FontWeights.Bold : FontWeights.SemiBold,
                Foreground = isSelected ? Brushes.White : DesignTokens.BrushTextSecondary,
                HorizontalAlignment = HorizontalAlignment.Center
            };
            sp.Children.Add(tb);

            TextBlock sub = new TextBlock
            {
                Text = subtitle,
                FontSize = 9.0,
                Foreground = isSelected ? new SolidColorBrush(Color.FromArgb(220, 255, 255, 255)) : DesignTokens.BrushTextMuted,
                HorizontalAlignment = HorizontalAlignment.Center
            };
            sp.Children.Add(sub);

            b.Child = sp;

            b.MouseEnter += (s, e) => {
                if (b.Tag == null || !(bool)b.Tag)
                {
                    b.Background = DesignTokens.BrushBgCardHover;
                    tb.Foreground = DesignTokens.BrushTextPrimary;
                }
            };
            b.MouseLeave += (s, e) => {
                if (b.Tag == null || !(bool)b.Tag)
                {
                    b.Background = Brushes.Transparent;
                    tb.Foreground = DesignTokens.BrushTextSecondary;
                }
            };

            b.MouseDown += (s, e) => { onClick(); };
            return b;
        }

        private void UpdateDriveModeButton(Border b, bool isSelected, Color accent)
        {
            if (b == null) return;
            b.Tag = isSelected;
            b.Background = isSelected ? new SolidColorBrush(accent) : Brushes.Transparent;
            b.BorderBrush = isSelected ? new SolidColorBrush(accent) : Brushes.Transparent;
            b.Effect = isSelected ? new DropShadowEffect { Color = accent, BlurRadius = 8, ShadowDepth = 0, Opacity = 0.4 } : null;

            StackPanel sp = b.Child as StackPanel;
            if (sp != null && sp.Children.Count >= 2)
            {
                TextBlock tb = sp.Children[0] as TextBlock;
                TextBlock sub = sp.Children[1] as TextBlock;
                if (tb != null)
                {
                    tb.Foreground = isSelected ? Brushes.White : DesignTokens.BrushTextSecondary;
                    tb.FontWeight = isSelected ? FontWeights.Bold : FontWeights.SemiBold;
                }
                if (sub != null)
                {
                    sub.Foreground = isSelected ? new SolidColorBrush(Color.FromArgb(220, 255, 255, 255)) : DesignTokens.BrushTextMuted;
                }
            }
        }

        private void ApplyMode(int mode, string desc)
        {
            currentFanMode = mode;
            HardwareBridge.SetFanMode(mode);
            lblStatusMsg.Text = desc;
            if (lblModeDesc != null) lblModeDesc.Text = desc;

            UpdateDriveModeButton(btnQuiet, mode == 3, DesignTokens.AccentCyan);
            UpdateDriveModeButton(btnAuto, mode == 0, DesignTokens.AccentBlue);
            UpdateDriveModeButton(btnMax, mode == 1, DesignTokens.AccentRed);
        }

        private Grid CreateGamingToolsHub()
        {
            Grid g = new Grid { Margin = new Thickness(30, 0, 30, 8) };
            Border card = new Border
            {
                Background = DesignTokens.BrushBgCard,
                CornerRadius = new CornerRadius(12),
                BorderBrush = DesignTokens.BrushBorderSubtle,
                BorderThickness = new Thickness(1),
                Padding = new Thickness(16, 8, 16, 8)
            };

            Grid inner = new Grid();
            inner.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(240) });
            inner.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            StackPanel ramSp = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            ramSp.Children.Add(new TextBlock { Text = "SYSTEM MEMORY", FontSize = 9.5, FontWeight = FontWeights.SemiBold, Foreground = DesignTokens.BrushTextMuted });
            lblRamStats = new TextBlock { Text = "RAM: --% (-- / -- GB)", FontSize = 12, FontWeight = FontWeights.Bold, Foreground = DesignTokens.BrushTextPrimary, Margin = new Thickness(0, 2, 0, 0) };
            ramSp.Children.Add(lblRamStats);
            Grid.SetColumn(ramSp, 0);
            inner.Children.Add(ramSp);

            StackPanel btnSp = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Right,
                VerticalAlignment = VerticalAlignment.Center
            };

            btnToolEcGuard = CreateToolButton("🛡️ EC Guard: ON", () =>
            {
                EcAntiHallucinationEngine.Toggle();
                UpdateDashboardEcGuardUi();
            });
            btnSp.Children.Add(btnToolEcGuard);

            Border btnRamBoost = CreateToolButton("⚡ Purge RAM", () =>
            {
                HardwareBridge.OptimizeMemory();
                lblStatusMsg.Text = "System RAM purged and optimized";
            });
            btnSp.Children.Add(btnRamBoost);

            Border btnHudToggle = CreateToolButton("🎮 In-Game HUD", () =>
            {
                ToggleOverlay();
            });
            btnSp.Children.Add(btnHudToggle);

            btnWinLock = CreateToolButton("🔒 Win-Key: Unlocked", () =>
            {
                winKeyLocked = !winKeyLocked;
                TextBlock tb = btnWinLock.Child as TextBlock;
                if (tb != null)
                {
                    tb.Text = winKeyLocked ? "🔒 Win-Key: Locked" : "🔓 Win-Key: Unlocked";
                }
                lblStatusMsg.Text = winKeyLocked ? "Gaming Win-Key lock active" : "Win-Key restored";
            });
            btnSp.Children.Add(btnWinLock);

            Border btnFlush = CreateToolButton("❄️ 60s Heat Flush", () =>
            {
                if (heatFlushRemaining > 0)
                {
                    heatFlushRemaining = 0;
                    ApplyMode(0, "Auto (Balanced adaptive)");
                    if (lblHeatFlushText != null) lblHeatFlushText.Text = "❄️ Engage Turbo Flush";
                }
                else
                {
                    heatFlushRemaining = 60;
                    ApplyMode(1, "Max (60s Turbo Heat Flush Engaged)");
                    if (lblHeatFlushText != null) lblHeatFlushText.Text = "❄️ Flushing... (60s Left)";
                }
            });
            btnSp.Children.Add(btnFlush);

            Border btnTaskMgr = CreateToolButton("📊 Task Manager", () =>
            {
                try { Process.Start("taskmgr.exe"); } catch { }
            });
            btnSp.Children.Add(btnTaskMgr);

            Border btnNvidia = CreateToolButton("🎮 NVIDIA Panel", () =>
            {
                try { Process.Start("control.exe", "/name Microsoft.NvidiaControlPanel"); }
                catch
                {
                    try { Process.Start("C:\\Program Files\\NVIDIA Corporation\\Control Panel Client\\nvcplui.exe"); } catch { }
                }
            });
            btnSp.Children.Add(btnNvidia);

            Grid.SetColumn(btnSp, 1);
            inner.Children.Add(btnSp);

            card.Child = inner;
            g.Children.Add(card);
            return g;
        }

        private Border CreateToolButton(string label, Action action)
        {
            Border b = new Border
            {
                Background = DesignTokens.BrushBgInput,
                BorderBrush = DesignTokens.BrushBorderSubtle,
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(6),
                Padding = new Thickness(12, 6, 12, 6),
                Margin = new Thickness(4, 0, 4, 0),
                Cursor = Cursors.Hand
            };
            TextBlock t = new TextBlock
            {
                Text = label,
                FontSize = 10.5,
                FontWeight = FontWeights.SemiBold,
                Foreground = DesignTokens.BrushTextSecondary
            };
            b.Child = t;

            b.MouseEnter += (s, e) => {
                b.Background = DesignTokens.BrushBgCardHover;
                b.BorderBrush = DesignTokens.BrushBorderSubtleLight;
                t.Foreground = DesignTokens.BrushTextPrimary;
            };
            b.MouseLeave += (s, e) => {
                b.Background = DesignTokens.BrushBgInput;
                b.BorderBrush = DesignTokens.BrushBorderSubtle;
                t.Foreground = DesignTokens.BrushTextSecondary;
            };

            b.MouseDown += (s, e) => { action(); };
            return b;
        }

        // ==========================================
        // PAGE 2: COOLING CONTROL STUDIO VIEW
        // ==========================================
        private Grid CreateCoolingView()
        {
            Grid g = new Grid { Margin = new Thickness(35, 12, 35, 12) };
            g.RowDefinitions.Add(new RowDefinition { Height = new GridLength(145) });
            g.RowDefinitions.Add(new RowDefinition { Height = new GridLength(76) });
            g.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

            // Top: Dual Detailed Fan Cards
            Grid topRow = new Grid();
            topRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            topRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            // CPU Dedicated Blower Card
            string cpuCoolingSub = (!string.IsNullOrEmpty(HardwareBridge.ChassisCpuName) ? HardwareBridge.ChassisCpuName : "CPU Package") + " Thermal Extraction";
            StackPanel cpuSp = new StackPanel { Margin = new Thickness(0, 4, 0, 0) };
            lblCoolingCpuRpm = new TextBlock { Text = "0 RPM (0%)", FontSize = 24, FontWeight = FontWeights.Bold, Foreground = DesignTokens.BrushAccentCyan };
            meterCoolingCpu = new SegmentedMeter(20, DesignTokens.AccentCyan, 12, 6);
            meterCoolingCpu.Margin = new Thickness(0, 8, 0, 8);
            TextBlock cpuSub = new TextBlock { Text = "Dynamic curve active: Ramps smoothly based on CPU package temp", FontSize = 10, Foreground = DesignTokens.BrushTextSecondary };
            cpuSp.Children.Add(lblCoolingCpuRpm);
            cpuSp.Children.Add(meterCoolingCpu);
            cpuSp.Children.Add(cpuSub);

            Border cpuCard = CreateSectionCard("CPU Dedicated Cooling Blower", cpuCoolingSub, cpuSp);
            Grid.SetColumn(cpuCard, 0);
            topRow.Children.Add(cpuCard);

            // GPU Dedicated Blower Card
            string gpuCoolingSub = (!string.IsNullOrEmpty(HardwareBridge.ChassisGpuName) ? HardwareBridge.ChassisGpuName : "GPU Silicon") + " Thermal Extraction";
            StackPanel gpuSp = new StackPanel { Margin = new Thickness(0, 4, 0, 0) };
            lblCoolingGpuRpm = new TextBlock { Text = "0 RPM (0%)", FontSize = 24, FontWeight = FontWeights.Bold, Foreground = DesignTokens.BrushAccentBlue };
            meterCoolingGpu = new SegmentedMeter(20, DesignTokens.AccentBlue, 12, 6);
            meterCoolingGpu.Margin = new Thickness(0, 8, 0, 8);
            TextBlock gpuSub = new TextBlock { Text = "Zero-RPM passive mode: Blower completely idles when GPU is cold", FontSize = 10, Foreground = DesignTokens.BrushTextSecondary };
            gpuSp.Children.Add(lblCoolingGpuRpm);
            gpuSp.Children.Add(meterCoolingGpu);
            gpuSp.Children.Add(gpuSub);

            Border gpuCard = CreateSectionCard("GPU Dedicated Cooling Blower", gpuCoolingSub, gpuSp);
            gpuCard.Margin = new Thickness(14, 0, 0, 0);
            Grid.SetColumn(gpuCard, 1);
            topRow.Children.Add(gpuCard);

            Grid.SetRow(topRow, 0);
            g.Children.Add(topRow);

            // Middle: 60-Second Turbo Heat Flush Card
            Border flushCard = new Border
            {
                Background = DesignTokens.BrushBgCard,
                CornerRadius = new CornerRadius(10),
                BorderBrush = DesignTokens.BrushBorderSubtle,
                BorderThickness = new Thickness(1),
                Margin = new Thickness(0, 10, 0, 0),
                Padding = new Thickness(16, 12, 16, 12)
            };
            Grid flushGrid = new Grid();
            flushGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            flushGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(240) });

            StackPanel fLeft = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            fLeft.Children.Add(new TextBlock
            {
                Text = "❄️ 60-Second Turbo Heat Flush",
                FontSize = 12.5,
                FontWeight = FontWeights.Bold,
                Foreground = DesignTokens.BrushTextPrimary
            });
            fLeft.Children.Add(new TextBlock
            {
                Text = "Instantly cycles blowers at 100% duty for 60 seconds to rapidly purge heat pipes, then auto-restores.",
                FontSize = 10,
                Foreground = DesignTokens.BrushTextSecondary,
                Margin = new Thickness(0, 2, 0, 0)
            });
            Grid.SetColumn(fLeft, 0);
            flushGrid.Children.Add(fLeft);

            btnHeatFlush = new Border
            {
                Background = DesignTokens.BrushAccentBlue,
                BorderBrush = DesignTokens.BrushBorderFocus,
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(6),
                Height = 34,
                Cursor = Cursors.Hand,
                VerticalAlignment = VerticalAlignment.Center
            };
            lblHeatFlushText = new TextBlock
            {
                Text = "❄️ Engage Turbo Flush",
                FontSize = 11,
                FontWeight = FontWeights.Bold,
                Foreground = Brushes.White,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            };
            btnHeatFlush.Child = lblHeatFlushText;
            btnHeatFlush.MouseEnter += (s, e) => { btnHeatFlush.Background = new SolidColorBrush(Color.FromRgb(2, 132, 199)); };
            btnHeatFlush.MouseLeave += (s, e) => { btnHeatFlush.Background = DesignTokens.BrushAccentBlue; };
            btnHeatFlush.MouseDown += (s, e) =>
            {
                if (heatFlushRemaining > 0)
                {
                    heatFlushRemaining = 0;
                    ApplyMode(0, "Auto (Balanced adaptive)");
                    lblHeatFlushText.Text = "❄️ Engage Turbo Flush";
                }
                else
                {
                    heatFlushRemaining = 60;
                    ApplyMode(1, "Max (60s Turbo Heat Flush Engaged)");
                    lblHeatFlushText.Text = "❄️ Flushing... (60s Left)";
                }
            };
            Grid.SetColumn(btnHeatFlush, 1);
            flushGrid.Children.Add(btnHeatFlush);

            flushCard.Child = flushGrid;
            Grid.SetRow(flushCard, 1);
            g.Children.Add(flushCard);

            // Bottom: Fan Curve Architecture & Fine Override
            string curveSub = (HardwareBridge.DetectedVendor == HardwareVendor.Asus)
                ? "ASUS TUF / ROG ACPI Firmware dynamic thermal curve"
                : (HardwareBridge.DetectedVendor == HardwareVendor.Lenovo)
                    ? "Lenovo Vantage EC dynamic thermal curve"
                    : (HardwareBridge.DetectedVendor == HardwareVendor.Hp)
                        ? "HP Omen / Victus BIOS thermal control"
                        : (HardwareBridge.DetectedVendor == HardwareVendor.Dell)
                            ? "Dell Alienware / G-Series WMAX dynamic curve"
                            : "Embedded Controller dynamic 4-stage curve";

            StackPanel botSp = new StackPanel { Margin = new Thickness(0, 4, 0, 0) };

            TextBlock intro = new TextBlock
            {
                Text = "EC hardware monitors temperatures continuously and modulates blower speed across 4 calibrated thermal stages:",
                FontSize = 10.5,
                Foreground = DesignTokens.BrushTextSecondary,
                Margin = new Thickness(0, 0, 0, 10)
            };
            botSp.Children.Add(intro);

            curveCards.Clear();
            UniformGrid curveGrid = new UniformGrid { Columns = 4, Margin = new Thickness(0, 0, 0, 14) };
            curveCards.Add(CreateCurveStep("Stage 1 (Idle)", "< 50 °C", "28% Duty (Whisper Quiet)"));
            curveCards.Add(CreateCurveStep("Stage 2 (Cruise)", "50 - 70 °C", "50% Duty (Balanced Daily)"));
            curveCards.Add(CreateCurveStep("Stage 3 (Gaming)", "70 - 85 °C", "66% Duty (High Airflow)"));
            curveCards.Add(CreateCurveStep("Stage 4 (Peak)", "> 85 °C", "100% Duty (Full Turbo Lock)"));

            foreach (Border cCard in curveCards)
            {
                curveGrid.Children.Add(cCard);
            }
            botSp.Children.Add(curveGrid);

            // Dedicated Custom Fan Slider
            Grid sliderRow = new Grid { Margin = new Thickness(0, 4, 0, 0) };
            sliderRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(160) });
            sliderRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            sliderRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(60) });
            sliderRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(160) });

            TextBlock lblDed = new TextBlock { Text = "Fine Manual Override:", FontSize = 11, FontWeight = FontWeights.Bold, Foreground = DesignTokens.BrushTextPrimary, VerticalAlignment = VerticalAlignment.Center };
            Grid.SetColumn(lblDed, 0);
            sliderRow.Children.Add(lblDed);

            sliderDedicatedCustom = new Slider { Minimum = 30, Maximum = 100, Value = 80, TickFrequency = 5, VerticalAlignment = VerticalAlignment.Center };
            sliderDedicatedCustom.ValueChanged += (s, e) => { lblDedicatedCustomVal.Text = string.Format("{0:F0}%", sliderDedicatedCustom.Value); };
            Grid.SetColumn(sliderDedicatedCustom, 1);
            sliderRow.Children.Add(sliderDedicatedCustom);

            lblDedicatedCustomVal = new TextBlock { Text = "80%", FontSize = 12, FontWeight = FontWeights.Bold, Foreground = DesignTokens.BrushAccentCyan, VerticalAlignment = VerticalAlignment.Center, TextAlignment = TextAlignment.Center };
            Grid.SetColumn(lblDedicatedCustomVal, 2);
            sliderRow.Children.Add(lblDedicatedCustomVal);

            Button btnApplyDed = new Button
            {
                Content = "Apply Fan Duty",
                FontSize = 10.5,
                FontWeight = FontWeights.Bold,
                Foreground = Brushes.White,
                Background = DesignTokens.BrushAccentBlue,
                BorderThickness = new Thickness(0),
                Height = 30,
                Cursor = Cursors.Hand
            };
            btnApplyDed.MouseEnter += (s, e) => { btnApplyDed.Background = new SolidColorBrush(Color.FromRgb(2, 132, 199)); };
            btnApplyDed.MouseLeave += (s, e) => { btnApplyDed.Background = DesignTokens.BrushAccentBlue; };
            btnApplyDed.Click += (s, e) =>
            {
                int val = (int)sliderDedicatedCustom.Value;
                bool ok = HardwareBridge.SetCustomSpeed(val);
                if (ok)
                {
                    currentFanMode = 6;
                    currentCustomDuty = val;
                    lblStatusMsg.Text = string.Format("Custom Fan Speed ({0}%) Locked", val);
                    UpdateDriveModeButton(btnAuto, false, DesignTokens.AccentBlue);
                    UpdateDriveModeButton(btnQuiet, false, DesignTokens.AccentCyan);
                    UpdateDriveModeButton(btnMax, false, DesignTokens.AccentRed);
                }
            };
            Grid.SetColumn(btnApplyDed, 3);
            sliderRow.Children.Add(btnApplyDed);

            botSp.Children.Add(sliderRow);

            Border bottomCard = CreateSectionCard("Hardware Fan Curve Architecture", curveSub, botSp);
            bottomCard.Margin = new Thickness(0, 10, 0, 0);
            Grid.SetRow(bottomCard, 2);
            g.Children.Add(bottomCard);

            return g;
        }

        private Border CreateSectionCard(string title, string subtitle, UIElement content)
        {
            Border b = new Border
            {
                Background = DesignTokens.BrushBgCard,
                CornerRadius = new CornerRadius(10),
                BorderBrush = DesignTokens.BrushBorderSubtle,
                BorderThickness = new Thickness(1),
                Padding = new Thickness(16, 12, 16, 12)
            };
            StackPanel wrapper = new StackPanel();
            if (!string.IsNullOrEmpty(title))
            {
                TextBlock t = new TextBlock
                {
                    Text = title,
                    FontSize = 13,
                    FontWeight = FontWeights.Bold,
                    Foreground = DesignTokens.BrushTextPrimary
                };
                wrapper.Children.Add(t);
            }
            if (!string.IsNullOrEmpty(subtitle))
            {
                TextBlock s = new TextBlock
                {
                    Text = subtitle,
                    FontSize = 10,
                    Foreground = DesignTokens.BrushTextSecondary,
                    Margin = new Thickness(0, 2, 0, 8)
                };
                wrapper.Children.Add(s);
            }
            if (content != null)
            {
                wrapper.Children.Add(content);
            }
            b.Child = wrapper;
            return b;
        }

        private Border CreateCurveStep(string label, string temp, string desc)
        {
            Border b = new Border
            {
                Background = DesignTokens.BrushBgInput,
                BorderBrush = DesignTokens.BrushBorderSubtle,
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(6),
                Padding = new Thickness(12, 10, 12, 10),
                Margin = new Thickness(4)
            };
            StackPanel sp = new StackPanel();
            sp.Children.Add(new TextBlock { Text = label, FontSize = 10, FontWeight = FontWeights.SemiBold, Foreground = DesignTokens.BrushTextSecondary });
            sp.Children.Add(new TextBlock { Text = temp, FontSize = 18, FontWeight = FontWeights.Bold, Foreground = DesignTokens.BrushTextPrimary, Margin = new Thickness(0, 3, 0, 3) });
            sp.Children.Add(new TextBlock { Text = desc, FontSize = 10, Foreground = DesignTokens.BrushTextMuted });
            b.Child = sp;
            return b;
        }

        // ==========================================
        // PAGE 3: GAMING HUB VIEW
        // ==========================================
        private Grid CreateGamingView()
        {
            Grid rootGrid = new Grid();

            ScrollViewer sv = new ScrollViewer
            {
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
                Margin = new Thickness(35, 6, 35, 6)
            };

            StackPanel mainSp = new StackPanel();

            // ==========================================
            // 1. IN-GAME OVERLAY CONTROL CENTER
            // ==========================================
            StackPanel ocSp = new StackPanel { Margin = new Thickness(0, 6, 0, 4) };

            Grid ocGrid = new Grid();
            ocGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            ocGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            // Left: Master Toggle
            StackPanel ocLeft = new StackPanel { VerticalAlignment = VerticalAlignment.Center };

            btnOverlayMasterToggle = new Border
            {
                Background = DesignTokens.BrushBgInput,
                BorderBrush = DesignTokens.BrushBorderSubtle,
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(8),
                Padding = new Thickness(16, 10, 16, 10),
                Cursor = Cursors.Hand,
                HorizontalAlignment = HorizontalAlignment.Left
            };
            lblOverlayMasterToggleText = new TextBlock
            {
                Text = "⚪ In-Game Overlay: Standby (Off)",
                FontSize = 12,
                FontWeight = FontWeights.Bold,
                Foreground = DesignTokens.BrushTextSecondary
            };
            btnOverlayMasterToggle.Child = lblOverlayMasterToggleText;
            btnOverlayMasterToggle.MouseEnter += (s, e) => {
                if (!isOverlayActive) btnOverlayMasterToggle.Background = DesignTokens.BrushBgCardHover;
            };
            btnOverlayMasterToggle.MouseLeave += (s, e) => {
                if (!isOverlayActive) btnOverlayMasterToggle.Background = DesignTokens.BrushBgInput;
            };
            btnOverlayMasterToggle.MouseDown += (s, e) => { ToggleOverlay(); };
            ocLeft.Children.Add(btnOverlayMasterToggle);

            lblOverlayStatusSub = new TextBlock
            {
                Text = "Global shortcut: Press [Ctrl + Shift + O] to toggle anytime in-game",
                FontSize = 10,
                Foreground = DesignTokens.BrushTextMuted,
                Margin = new Thickness(2, 6, 0, 0)
            };
            ocLeft.Children.Add(lblOverlayStatusSub);
            Grid.SetColumn(ocLeft, 0);
            ocGrid.Children.Add(ocLeft);

            // Right: Position and Style Selectors
            StackPanel ocRight = new StackPanel { HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Center };

            // Position Pills Row
            StackPanel posRow = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 0, 0, 6) };
            posRow.Children.Add(new TextBlock
            {
                Text = "Position: ",
                FontSize = 10,
                FontWeight = FontWeights.SemiBold,
                Foreground = DesignTokens.BrushTextSecondary,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 6, 0)
            });

            btnPosTL = CreatePillButton("Top-Left", true, () => { SetOverlayPosition(OverlayPosition.TopLeft); });
            btnPosTR = CreatePillButton("Top-Right", false, () => { SetOverlayPosition(OverlayPosition.TopRight); });
            btnPosTC = CreatePillButton("Center-Top", false, () => { SetOverlayPosition(OverlayPosition.CenterTop); });
            btnPosBL = CreatePillButton("Bottom-Left", false, () => { SetOverlayPosition(OverlayPosition.BottomLeft); });
            btnPosBR = CreatePillButton("Bottom-Right", false, () => { SetOverlayPosition(OverlayPosition.BottomRight); });

            posRow.Children.Add(btnPosTL);
            posRow.Children.Add(btnPosTR);
            posRow.Children.Add(btnPosTC);
            posRow.Children.Add(btnPosBL);
            posRow.Children.Add(btnPosBR);
            ocRight.Children.Add(posRow);

            // Style Pills Row
            StackPanel styleRow = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
            styleRow.Children.Add(new TextBlock
            {
                Text = "HUD Style: ",
                FontSize = 10,
                FontWeight = FontWeights.SemiBold,
                Foreground = DesignTokens.BrushTextSecondary,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 6, 0)
            });

            btnStyleHoriz = CreatePillButton("Horizontal Bar", true, () => { SetOverlayStyle(OverlayStyle.HorizontalBar); });
            btnStyleMatrix = CreatePillButton("Compact Matrix", false, () => { SetOverlayStyle(OverlayStyle.CompactMatrix); });

            styleRow.Children.Add(btnStyleHoriz);
            styleRow.Children.Add(btnStyleMatrix);
            ocRight.Children.Add(styleRow);

            Grid.SetColumn(ocRight, 1);
            ocGrid.Children.Add(ocRight);

            ocSp.Children.Add(ocGrid);
            Border overlayCtrlCard = CreateSectionCard("In-Game Metrics Overlay & Cockpit HUD", "Zero-hook kernel telemetry, FPS counter, 1% lows & hardware vitals (Zero anti-cheat impact)", ocSp);
            mainSp.Children.Add(overlayCtrlCard);

            // ==========================================
            // 2. LIVE GAME OVERLAY STATISTICS PREVIEW DASHBOARD
            // ==========================================
            StackPanel hudSp = new StackPanel { Margin = new Thickness(0, 4, 0, 4) };

            // Sub-header showing active process target
            Grid targetBar = new Grid { Margin = new Thickness(0, 0, 0, 10) };
            targetBar.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            targetBar.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            lblHubTargetApp = new TextBlock
            {
                Text = "Active Target: Desktop Window Manager",
                FontSize = 10.5,
                FontWeight = FontWeights.Bold,
                Foreground = DesignTokens.BrushAccentCyan,
                VerticalAlignment = VerticalAlignment.Center
            };
            Grid.SetColumn(lblHubTargetApp, 0);
            targetBar.Children.Add(lblHubTargetApp);

            lblHubEngineStatus = new TextBlock
            {
                Text = "⚡ Zero-Hook ETW DirectX Active",
                FontSize = 10,
                FontWeight = FontWeights.SemiBold,
                Foreground = DesignTokens.BrushAccentGreen,
                VerticalAlignment = VerticalAlignment.Center
            };
            Grid.SetColumn(lblHubEngineStatus, 1);
            targetBar.Children.Add(lblHubEngineStatus);
            hudSp.Children.Add(targetBar);

            // 4-Card Live Stats Grid
            UniformGrid statsGrid = new UniformGrid { Columns = 4, Margin = new Thickness(0, 0, 0, 0) };

            // Block 1: FRAMERATE & 1% LOW
            Border cardFps = CreateTelemetryPod("Framerate & Stability", DesignTokens.AccentGreen);
            StackPanel spCardFps = new StackPanel();
            spCardFps.Children.Add(new TextBlock { Text = "Framerate & Stability", FontSize = 10, FontWeight = FontWeights.SemiBold, Foreground = DesignTokens.BrushAccentGreen, Margin = new Thickness(0, 0, 0, 4) });
            lblHubFpsVal = new TextBlock
            {
                Text = "-- FPS",
                FontSize = 24,
                FontWeight = FontWeights.Bold,
                Foreground = DesignTokens.BrushAccentGreen,
                Margin = new Thickness(0, 0, 0, 2)
            };
            spCardFps.Children.Add(lblHubFpsVal);

            lblHubOneLowVal = new TextBlock
            {
                Text = "Avg: -- | 1% Low: -- FPS",
                FontSize = 11.5,
                FontWeight = FontWeights.SemiBold,
                Foreground = DesignTokens.BrushAccentCyan,
                Margin = new Thickness(0, 0, 0, 2)
            };
            spCardFps.Children.Add(lblHubOneLowVal);

            lblHubFrameTimeVal = new TextBlock
            {
                Text = "-- ms Delivery Latency",
                FontSize = 10,
                Foreground = DesignTokens.BrushTextSecondary,
                Margin = new Thickness(0, 0, 0, 6)
            };
            spCardFps.Children.Add(lblHubFrameTimeVal);

            meterHubStability = new SegmentedMeter(12, DesignTokens.AccentGreen, 12, 4);
            spCardFps.Children.Add(meterHubStability);
            cardFps.Child = spCardFps;
            statsGrid.Children.Add(cardFps);

            // Block 2: GPU TELEMETRY
            Border cardGpu = CreateTelemetryPod("GPU Telemetry", DesignTokens.AccentCyan);
            StackPanel spCardGpu = new StackPanel();
            spCardGpu.Children.Add(new TextBlock { Text = "GPU Telemetry", FontSize = 10, FontWeight = FontWeights.SemiBold, Foreground = DesignTokens.BrushAccentCyan, Margin = new Thickness(0, 0, 0, 4) });
            lblHubGpuVal = new TextBlock
            {
                Text = "--°C | 0% | 0W",
                FontSize = 20,
                FontWeight = FontWeights.Bold,
                Foreground = DesignTokens.BrushTextPrimary,
                Margin = new Thickness(0, 2, 0, 2)
            };
            spCardGpu.Children.Add(lblHubGpuVal);

            TextBlock lblGpuSub = new TextBlock
            {
                Text = HardwareBridge.ChassisGpuName,
                FontSize = 10.5,
                FontWeight = FontWeights.SemiBold,
                Foreground = DesignTokens.BrushAccentCyan,
                Margin = new Thickness(0, 0, 0, 4)
            };
            spCardGpu.Children.Add(lblGpuSub);

            meterHubGpu = new SegmentedMeter(12, DesignTokens.AccentCyan, 12, 4);
            spCardGpu.Children.Add(meterHubGpu);

            TextBlock lblGpuStatSub = new TextBlock
            {
                Text = "Core Clock & Dynamic Boost Active",
                FontSize = 10,
                Foreground = DesignTokens.BrushTextMuted,
                Margin = new Thickness(0, 4, 0, 0)
            };
            spCardGpu.Children.Add(lblGpuStatSub);
            cardGpu.Child = spCardGpu;
            statsGrid.Children.Add(cardGpu);

            // Block 3: CPU TELEMETRY
            Border cardCpu = CreateTelemetryPod("CPU Telemetry", DesignTokens.AccentBlue);
            StackPanel spCardCpu = new StackPanel();
            spCardCpu.Children.Add(new TextBlock { Text = "CPU Telemetry", FontSize = 10, FontWeight = FontWeights.SemiBold, Foreground = DesignTokens.BrushAccentBlue, Margin = new Thickness(0, 0, 0, 4) });
            lblHubCpuVal = new TextBlock
            {
                Text = "--°C | 0% | 0W",
                FontSize = 20,
                FontWeight = FontWeights.Bold,
                Foreground = DesignTokens.BrushTextPrimary,
                Margin = new Thickness(0, 2, 0, 2)
            };
            spCardCpu.Children.Add(lblHubCpuVal);

            TextBlock lblCpuSub = new TextBlock
            {
                Text = HardwareBridge.ChassisCpuName,
                FontSize = 10.5,
                FontWeight = FontWeights.SemiBold,
                Foreground = DesignTokens.BrushAccentBlue,
                Margin = new Thickness(0, 0, 0, 4)
            };
            spCardCpu.Children.Add(lblCpuSub);

            meterHubCpu = new SegmentedMeter(12, DesignTokens.AccentBlue, 12, 4);
            spCardCpu.Children.Add(meterHubCpu);

            TextBlock lblCpuStatSub = new TextBlock
            {
                Text = "Thread Execution Unconstrained",
                FontSize = 10,
                Foreground = DesignTokens.BrushTextMuted,
                Margin = new Thickness(0, 4, 0, 0)
            };
            spCardCpu.Children.Add(lblCpuStatSub);
            cardCpu.Child = spCardCpu;
            statsGrid.Children.Add(cardCpu);

            // Block 4: RAM TELEMETRY
            Border cardRam = CreateTelemetryPod("Memory (RAM)", DesignTokens.AccentPurple);
            StackPanel spCardRam = new StackPanel();
            spCardRam.Children.Add(new TextBlock { Text = "Memory (RAM)", FontSize = 10, FontWeight = FontWeights.SemiBold, Foreground = DesignTokens.BrushAccentPurple, Margin = new Thickness(0, 0, 0, 4) });
            lblHubRamVal = new TextBlock
            {
                Text = "-- / -- GB (0%)",
                FontSize = 18,
                FontWeight = FontWeights.Bold,
                Foreground = DesignTokens.BrushTextPrimary,
                Margin = new Thickness(0, 3, 0, 2)
            };
            spCardRam.Children.Add(lblHubRamVal);

            meterHubRam = new SegmentedMeter(12, DesignTokens.AccentPurple, 12, 4);
            spCardRam.Children.Add(meterHubRam);

            Border btnPurgeRam = new Border
            {
                Background = DesignTokens.BrushBgInput,
                BorderBrush = DesignTokens.BrushAccentPurple,
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(5),
                Padding = new Thickness(10, 4, 10, 4),
                Margin = new Thickness(0, 6, 0, 0),
                Cursor = Cursors.Hand,
                HorizontalAlignment = HorizontalAlignment.Left
            };
            btnPurgeRam.Child = new TextBlock
            {
                Text = "🧹 Purge Standby RAM",
                FontSize = 10,
                FontWeight = FontWeights.SemiBold,
                Foreground = new SolidColorBrush(Color.FromRgb(216, 180, 254))
            };
            btnPurgeRam.MouseEnter += (s, e) => { btnPurgeRam.Background = DesignTokens.BrushBgCardHover; };
            btnPurgeRam.MouseLeave += (s, e) => { btnPurgeRam.Background = DesignTokens.BrushBgInput; };
            btnPurgeRam.MouseDown += (s, e) =>
            {
                HardwareBridge.OptimizeMemory();
                if (lblStatusMsg != null)
                {
                    lblStatusMsg.Text = "System Working Set & Standby RAM Purged";
                    lblStatusMsg.Foreground = DesignTokens.BrushAccentGreen;
                }
            };
            spCardRam.Children.Add(btnPurgeRam);
            cardRam.Child = spCardRam;
            statsGrid.Children.Add(cardRam);

            hudSp.Children.Add(statsGrid);

            Border hudPreviewCard = CreateSectionCard("Live In-Game HUD Telemetry Stream", "Real-time replica of the active in-game overlay metrics", hudSp);
            hudPreviewCard.Margin = new Thickness(0, 8, 0, 0);
            mainSp.Children.Add(hudPreviewCard);

            // ==========================================
            // 3. PERFORMANCE & GAMING SCENARIOS
            // ==========================================
            StackPanel scenSp = new StackPanel { Margin = new Thickness(0, 4, 0, 4) };

            Grid scenHeader = new Grid();
            scenHeader.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            scenHeader.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(220) });

            lblScenarioStatus = new TextBlock
            {
                Text = "Current Profile: Dynamic Auto (Balanced)",
                FontSize = 11,
                FontWeight = FontWeights.Bold,
                Foreground = DesignTokens.BrushAccentCyan,
                VerticalAlignment = VerticalAlignment.Center
            };
            Grid.SetColumn(lblScenarioStatus, 0);
            scenHeader.Children.Add(lblScenarioStatus);

            // 1-Click ULTRA GAME BOOST Button
            Border btnUltraBoost = new Border
            {
                Background = DesignTokens.BrushAccentRed,
                BorderBrush = new SolidColorBrush(Color.FromRgb(255, 100, 140)),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(6),
                Padding = new Thickness(12, 6, 12, 6),
                Cursor = Cursors.Hand
            };
            btnUltraBoost.Child = new TextBlock
            {
                Text = "🚀 Ultra Game Boost",
                FontSize = 11,
                FontWeight = FontWeights.Bold,
                Foreground = Brushes.White,
                HorizontalAlignment = HorizontalAlignment.Center
            };
            btnUltraBoost.MouseEnter += (s, e) => { btnUltraBoost.Background = new SolidColorBrush(Color.FromRgb(220, 38, 38)); };
            btnUltraBoost.MouseLeave += (s, e) => { btnUltraBoost.Background = DesignTokens.BrushAccentRed; };
            btnUltraBoost.MouseDown += (s, e) =>
            {
                ApplyMode(1, "Ultra Game Boost Active (100% Turbo Cooling)");
                HardwareBridge.OptimizeMemory();
                winKeyLocked = true;
                if (btnWinLock != null)
                {
                    TextBlock tb = btnWinLock.Child as TextBlock;
                    if (tb != null) tb.Text = "🔒 Win-Key: Locked";
                }
                try
                {
                    ProcessStartInfo psi = new ProcessStartInfo("powercfg", "/setactive 8c5e7fda-e8bf-4a96-9a85-a6e23a8c635c")
                    {
                        CreateNoWindow = true,
                        UseShellExecute = false
                    };
                    Process.Start(psi);
                }
                catch { }

                lblScenarioStatus.Text = "🚀 Ultra Game Boost Active (100% Fan + RAM Purged + Win-Key Locked)";
                lblScenarioStatus.Foreground = DesignTokens.BrushAccentGreen;
            };
            Grid.SetColumn(btnUltraBoost, 1);
            scenHeader.Children.Add(btnUltraBoost);
            scenSp.Children.Add(scenHeader);

            UniformGrid scenGrid = new UniformGrid { Columns = 3, Margin = new Thickness(0, 10, 0, 0) };
            scenGrid.Children.Add(CreateScenarioBtn("Max (Turbo Boost)", "Maximum cooling, full performance, zero thermal throttling", () =>
            {
                ApplyMode(1, "Max (100% Turbo Boost)");
                lblScenarioStatus.Text = "Active Profile: Max Turbo Boost Mode";
                lblScenarioStatus.Foreground = DesignTokens.BrushAccentRed;
            }));
            scenGrid.Children.Add(CreateScenarioBtn("Auto (Balanced)", "Intelligent dynamic thermal and power balance", () =>
            {
                ApplyMode(0, "Auto (Balanced adaptive)");
                lblScenarioStatus.Text = "Active Profile: Auto Balanced Mode";
                lblScenarioStatus.Foreground = DesignTokens.BrushAccentCyan;
            }));
            scenGrid.Children.Add(CreateScenarioBtn("Quiet (Silent)", "Whisper-quiet acoustic profile, maximum efficiency", () =>
            {
                ApplyMode(3, "Quiet (Silent acoustic)");
                lblScenarioStatus.Text = "Active Profile: Quiet Silent Mode";
                lblScenarioStatus.Foreground = DesignTokens.BrushAccentGreen;
            }));
            scenSp.Children.Add(scenGrid);

            Border scenCard = CreateSectionCard("Performance & Gaming Scenarios", "Instant system clock and cooling profiles", scenSp);
            scenCard.Margin = new Thickness(0, 8, 0, 0);
            mainSp.Children.Add(scenCard);

            // ==========================================
            // 4. LIVE GAMING NETWORK PING TELEMETRY RIBBON
            // ==========================================
            Border pingRibbon = new Border
            {
                Background = DesignTokens.BrushBgCard,
                CornerRadius = new CornerRadius(10),
                BorderBrush = DesignTokens.BrushBorderSubtle,
                BorderThickness = new Thickness(1),
                Margin = new Thickness(0, 8, 0, 0),
                Padding = new Thickness(16, 10, 16, 10)
            };
            Grid pingGrid = new Grid();
            pingGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            pingGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(110) });

            StackPanel pLeft = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            pLeft.Children.Add(new TextBlock
            {
                Text = "🌐 Multiplayer Network Telemetry:  ",
                FontSize = 10.5,
                FontWeight = FontWeights.SemiBold,
                Foreground = DesignTokens.BrushTextSecondary
            });
            lblGamePing = new TextBlock
            {
                Text = "Checking latency...",
                FontSize = 11.5,
                FontWeight = FontWeights.Bold,
                Foreground = DesignTokens.BrushAccentGreen
            };
            pLeft.Children.Add(lblGamePing);
            Grid.SetColumn(pLeft, 0);
            pingGrid.Children.Add(pLeft);

            Border btnPingNow = new Border
            {
                Background = DesignTokens.BrushBgInput,
                BorderBrush = DesignTokens.BrushBorderSubtle,
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(5),
                Padding = new Thickness(10, 5, 10, 5),
                Cursor = Cursors.Hand,
                VerticalAlignment = VerticalAlignment.Center
            };
            btnPingNow.Child = new TextBlock
            {
                Text = "⚡ Ping Now",
                FontSize = 10,
                FontWeight = FontWeights.SemiBold,
                Foreground = DesignTokens.BrushAccentCyan,
                HorizontalAlignment = HorizontalAlignment.Center
            };
            btnPingNow.MouseEnter += (s, e) => { btnPingNow.Background = DesignTokens.BrushBgCardHover; };
            btnPingNow.MouseLeave += (s, e) => { btnPingNow.Background = DesignTokens.BrushBgInput; };
            btnPingNow.MouseDown += (s, e) => { CheckPingAsync(); };
            Grid.SetColumn(btnPingNow, 1);
            pingGrid.Children.Add(btnPingNow);

            pingRibbon.Child = pingGrid;
            mainSp.Children.Add(pingRibbon);

            // ==========================================
            // 5. TOOLS & SYSTEM LAUNCHERS
            // ==========================================
            StackPanel tSp = new StackPanel { Margin = new Thickness(0, 4, 0, 4) };

            UniformGrid utilGrid = new UniformGrid { Columns = 2 };
            utilGrid.Children.Add(CreateLauncherTile("🎮 NVIDIA Control Panel", "Tweak 3D settings, G-Sync & Max Frame Rates", () =>
            {
                try { Process.Start("control.exe", "/name Microsoft.NvidiaControlPanel"); } catch { }
            }));
            utilGrid.Children.Add(CreateLauncherTile("📊 Windows Task Manager", "Inspect process execution & background tasks", () =>
            {
                try { Process.Start("taskmgr.exe"); } catch { }
            }));
            utilGrid.Children.Add(CreateLauncherTile("🎯 DirectX Diagnostics (DxDiag)", "View direct 3D hardware & display driver specs", () =>
            {
                try { Process.Start("dxdiag.exe"); } catch { }
            }));
            utilGrid.Children.Add(CreateLauncherTile("⚡ Windows Power & Battery", "Adjust screen sleep and power management", () =>
            {
                try { Process.Start("ms-settings:powersleep"); } catch { }
            }));
            utilGrid.Children.Add(CreateLauncherTile("🖥️ Windows Graphics Settings", "Configure Hardware-Accelerated GPU Scheduling (HAGS)", () =>
            {
                try { Process.Start("ms-settings:display-advancedgraphicsettings"); } catch { }
            }));
            utilGrid.Children.Add(CreateLauncherTile("📁 AspireSense Folder", "Open application install location & logs", () =>
            {
                try { Process.Start("explorer.exe", AppDomain.CurrentDomain.BaseDirectory); } catch { }
            }));

            tSp.Children.Add(utilGrid);
            Border toolsCard = CreateSectionCard("Gaming Utilities & Acceleration", "Fast access to critical performance tools", tSp);
            toolsCard.Margin = new Thickness(0, 8, 0, 0);
            mainSp.Children.Add(toolsCard);

            sv.Content = mainSp;
            rootGrid.Children.Add(sv);
            return rootGrid;
        }

        private Border CreatePillButton(string text, bool isActive, Action act)
        {
            Border b = new Border
            {
                Background = isActive ? DesignTokens.BrushBgCardActive : DesignTokens.BrushBgInput,
                BorderBrush = isActive ? DesignTokens.BrushBorderFocus : DesignTokens.BrushBorderSubtle,
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(5),
                Padding = new Thickness(10, 4, 10, 4),
                Margin = new Thickness(0, 0, 5, 0),
                Cursor = Cursors.Hand
            };
            TextBlock tb = new TextBlock
            {
                Text = text,
                FontSize = 10,
                FontWeight = isActive ? FontWeights.Bold : FontWeights.Normal,
                Foreground = isActive ? Brushes.White : DesignTokens.BrushTextSecondary
            };
            b.Child = tb;

            b.MouseEnter += (s, e) => {
                if (b.BorderBrush != DesignTokens.BrushBorderFocus)
                {
                    b.Background = DesignTokens.BrushBgCardHover;
                    tb.Foreground = DesignTokens.BrushTextPrimary;
                }
            };
            b.MouseLeave += (s, e) => {
                if (b.BorderBrush != DesignTokens.BrushBorderFocus)
                {
                    b.Background = DesignTokens.BrushBgInput;
                    tb.Foreground = DesignTokens.BrushTextSecondary;
                }
            };

            b.MouseDown += (s, e) => { act(); };
            return b;
        }

        private Border CreateTelemetryPod(string title, Color accent)
        {
            Border b = new Border
            {
                Background = DesignTokens.BrushBgInput,
                BorderBrush = DesignTokens.BrushBorderSubtle,
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(8),
                Padding = new Thickness(14, 10, 14, 10),
                Margin = new Thickness(4)
            };
            StackPanel sp = new StackPanel();
            sp.Children.Add(new TextBlock
            {
                Text = title,
                FontSize = 10,
                FontWeight = FontWeights.SemiBold,
                Foreground = new SolidColorBrush(accent),
                Margin = new Thickness(0, 0, 0, 4)
            });
            b.Tag = sp;
            return b;
        }

        private void CheckPingAsync()
        {
            ThreadPool.QueueUserWorkItem(delegate(object state)
            {
                try
                {
                    using (System.Net.NetworkInformation.Ping p = new System.Net.NetworkInformation.Ping())
                    {
                        System.Net.NetworkInformation.PingReply reply = p.Send("1.1.1.1", 1200);
                        if (reply != null && reply.Status == System.Net.NetworkInformation.IPStatus.Success)
                        {
                            int rtt = (int)reply.RoundtripTime;
                            this.Dispatcher.BeginInvoke((Action)delegate
                            {
                                lastPingMs = rtt;
                                UpdatePingDisplay(rtt);
                            });
                        }
                        else
                        {
                            this.Dispatcher.BeginInvoke((Action)delegate
                            {
                                UpdatePingDisplay(-1);
                            });
                        }
                    }
                }
                catch
                {
                    this.Dispatcher.BeginInvoke((Action)delegate
                    {
                        UpdatePingDisplay(-1);
                    });
                }
            });
        }

        private void UpdatePingDisplay(int rtt)
        {
            if (lblGamePing == null) return;
            if (rtt >= 0)
            {
                string qual = (rtt < 40) ? "Optimal low latency" : ((rtt < 80) ? "Good" : "High latency");
                lblGamePing.Text = string.Format("{0} ms ({1})", rtt, qual);
                if (rtt < 40) lblGamePing.Foreground = DesignTokens.BrushAccentGreen;
                else if (rtt < 80) lblGamePing.Foreground = DesignTokens.BrushAccentCyan;
                else lblGamePing.Foreground = DesignTokens.BrushAccentAmber;
            }
            else
            {
                lblGamePing.Text = "Ping timeout / offline";
                lblGamePing.Foreground = DesignTokens.BrushAccentRed;
            }
        }

        private Border CreateScenarioBtn(string title, string desc, Action act)
        {
            Border b = new Border
            {
                Background = DesignTokens.BrushBgInput,
                BorderBrush = DesignTokens.BrushBorderSubtle,
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(7),
                Padding = new Thickness(14, 10, 14, 10),
                Margin = new Thickness(4),
                Cursor = Cursors.Hand
            };
            StackPanel sp = new StackPanel();
            sp.Children.Add(new TextBlock { Text = title, FontSize = 12, FontWeight = FontWeights.Bold, Foreground = DesignTokens.BrushTextPrimary });
            sp.Children.Add(new TextBlock { Text = desc, FontSize = 10, Foreground = DesignTokens.BrushTextSecondary, Margin = new Thickness(0, 3, 0, 0) });
            b.Child = sp;

            b.MouseEnter += (s, e) => {
                b.Background = DesignTokens.BrushBgCardHover;
                b.BorderBrush = DesignTokens.BrushBorderSubtleLight;
            };
            b.MouseLeave += (s, e) => {
                b.Background = DesignTokens.BrushBgInput;
                b.BorderBrush = DesignTokens.BrushBorderSubtle;
            };

            b.MouseDown += (s, e) => { act(); };
            return b;
        }

        private Border CreateLauncherTile(string title, string desc, Action act)
        {
            Border b = new Border
            {
                Background = DesignTokens.BrushBgInput,
                BorderBrush = DesignTokens.BrushBorderSubtle,
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(8),
                Padding = new Thickness(14, 10, 14, 10),
                Margin = new Thickness(5),
                Cursor = Cursors.Hand
            };
            StackPanel sp = new StackPanel();
            sp.Children.Add(new TextBlock { Text = title, FontSize = 12.5, FontWeight = FontWeights.Bold, Foreground = DesignTokens.BrushAccentCyan });
            sp.Children.Add(new TextBlock { Text = desc, FontSize = 10, Foreground = DesignTokens.BrushTextSecondary, Margin = new Thickness(0, 4, 0, 0) });
            b.Child = sp;

            b.MouseEnter += (s, e) => {
                b.Background = DesignTokens.BrushBgCardHover;
                b.BorderBrush = DesignTokens.BrushBorderSubtleLight;
            };
            b.MouseLeave += (s, e) => {
                b.Background = DesignTokens.BrushBgInput;
                b.BorderBrush = DesignTokens.BrushBorderSubtle;
            };

            b.MouseDown += (s, e) => { act(); };
            return b;
        }

        // ==========================================
        // PAGE 4: SYSTEM HEALTH & SPECS VIEW
        // ==========================================
        private Grid CreateHealthView()
        {
            Grid g = new Grid { Margin = new Thickness(35, 12, 35, 12) };
            g.RowDefinitions.Add(new RowDefinition { Height = new GridLength(88) }); // Top Silicon Ribbon
            g.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) }); // 3 Analytics Cards

            // 1. Top Chassis & Silicon Matrix Ribbon
            Border ribbon = CreateSiliconRibbon();
            Grid.SetRow(ribbon, 0);
            g.Children.Add(ribbon);

            // 2. Lower Analytics Matrix (3 Equal Minimalist Cards)
            Grid lowerGrid = new Grid { Margin = new Thickness(0, 12, 0, 0) };
            lowerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            lowerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            lowerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            // Card 1: RTX 4050 Telemetry Card
            Border gpuCard = CreateGpuHealthCard();
            Grid.SetColumn(gpuCard, 0);
            lowerGrid.Children.Add(gpuCard);

            // Card 2: Memory & Workloads Card
            Border ramCard = CreateMemoryHealthCard();
            Grid.SetColumn(ramCard, 1);
            lowerGrid.Children.Add(ramCard);

            // Card 3: Power, Storage & Diagnostics Card
            Border powerCard = CreatePowerHealthCard();
            Grid.SetColumn(powerCard, 2);
            lowerGrid.Children.Add(powerCard);

            Grid.SetRow(lowerGrid, 1);
            g.Children.Add(lowerGrid);

            return g;
        }

        private Border CreateSiliconRibbon()
        {
            Border ribbon = new Border
            {
                Background = DesignTokens.BrushBgCard,
                CornerRadius = new CornerRadius(10),
                BorderBrush = DesignTokens.BrushBorderSubtle,
                BorderThickness = new Thickness(1),
                Padding = new Thickness(14, 8, 14, 8)
            };

            UniformGrid ug = new UniformGrid { Columns = 5 };

            TextBlock dummy;
            ug.Children.Add(CreateRibbonTile("Processor", HardwareBridge.ChassisCpuName, string.Format("{0} Cores • 4.8 GHz Max", Environment.ProcessorCount), out dummy));
            ug.Children.Add(CreateRibbonTile("Graphics Silicon", HardwareBridge.ChassisGpuName, "PCIe Gen 4.0 x8 Bus", out dummy));
            string ramDesc = string.Format("{0:F0} GB Dual-Channel", HardwareBridge.ChassisTotalRamGb > 0 ? HardwareBridge.ChassisTotalRamGb : 16.0);
            ug.Children.Add(CreateRibbonTile("Memory Architecture", ramDesc, "High-Speed Memory Bus", out dummy));
            ug.Children.Add(CreateRibbonTile("Firmware Platform", HardwareBridge.ChassisBiosVersion, "EC Controller " + HardwareBridge.ChassisEcVersion, out dummy));
            ug.Children.Add(CreateRibbonTile("System Uptime", "0d 00h 00m 00s", HardwareBridge.ChassisOs, out lblHealthUptime));

            ribbon.Child = ug;
            return ribbon;
        }

        private Border CreateRibbonTile(string category, string primary, string secondary, out TextBlock dynamicTb)
        {
            Border b = new Border
            {
                Background = DesignTokens.BrushBgInput,
                BorderBrush = DesignTokens.BrushBorderSubtle,
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(6),
                Padding = new Thickness(10, 7, 10, 7),
                Margin = new Thickness(3)
            };
            StackPanel sp = new StackPanel();
            sp.Children.Add(new TextBlock
            {
                Text = category,
                FontSize = 10,
                FontWeight = FontWeights.SemiBold,
                Foreground = DesignTokens.BrushTextSecondary
            });

            dynamicTb = new TextBlock
            {
                Text = primary,
                FontSize = 12,
                FontWeight = FontWeights.Bold,
                Foreground = DesignTokens.BrushTextPrimary,
                Margin = new Thickness(0, 2, 0, 1),
                TextTrimming = TextTrimming.CharacterEllipsis
            };
            sp.Children.Add(dynamicTb);

            sp.Children.Add(new TextBlock
            {
                Text = secondary,
                FontSize = 10,
                Foreground = DesignTokens.BrushTextMuted,
                TextTrimming = TextTrimming.CharacterEllipsis
            });

            b.Child = sp;
            return b;
        }

        private Border CreateGpuHealthCard()
        {
            StackPanel sp = new StackPanel();

            // VRAM Section
            sp.Children.Add(CreateMetricHeader("VRAM Dedicated Allocation", out lblHealthVramText, "0.0 / 6.0 GB (0%)", DesignTokens.AccentCyan));
            meterHealthVram = new SegmentedMeter(14, DesignTokens.AccentCyan, 16, 5);
            sp.Children.Add(meterHealthVram);

            // Clock Frequencies Dual Tile
            UniformGrid clkGrid = new UniformGrid { Columns = 2, Margin = new Thickness(0, 14, 0, 12) };
            clkGrid.Children.Add(CreateClockTile("Core Clock", out lblHealthGpuCore, "2,130", "MHz"));
            clkGrid.Children.Add(CreateClockTile("VRAM Clock", out lblHealthGpuMem, "8,001", "MHz"));
            sp.Children.Add(clkGrid);

            // Telemetry Status Lines
            StackPanel statSp = new StackPanel { Margin = new Thickness(0, 4, 0, 0) };
            statSp.Children.Add(CreateStatusLine("Throttle Reason:", out lblHealthGpuThrottle, "Optimal (Idle)", DesignTokens.AccentGreen));
            statSp.Children.Add(CreateStatusLine("Thermal Headroom:", out lblHealthGpuHeadroom, "+48°C Margin (to 87°C)", DesignTokens.AccentCyan));
            statSp.Children.Add(CreateStatusLine("Bus Interface:", "PCIe Gen 4.0 x8 Active", DesignTokens.TextSecondaryColor));
            sp.Children.Add(statSp);

            Border card = CreateMinimalCard("RTX 4050 Accelerator", "AD107M", sp);
            card.Margin = new Thickness(0, 0, 6, 0);
            return card;
        }

        private Border CreateMemoryHealthCard()
        {
            StackPanel sp = new StackPanel();

            // RAM Section
            sp.Children.Add(CreateMetricHeader("Physical RAM Utilization", out lblHealthRamText, "-- / -- GB (--%)", DesignTokens.AccentPurple));
            meterHealthRam = new SegmentedMeter(14, DesignTokens.AccentPurple, 16, 5);
            sp.Children.Add(meterHealthRam);

            // Top Memory Consumers
            TextBlock topHeader = new TextBlock
            {
                Text = "Top Active Consumers (RAM)",
                FontSize = 10,
                FontWeight = FontWeights.SemiBold,
                Foreground = DesignTokens.BrushTextSecondary,
                Margin = new Thickness(0, 16, 0, 6)
            };
            sp.Children.Add(topHeader);

            sp.Children.Add(CreateProcessItem("#1", out lblTopProc1));
            sp.Children.Add(CreateProcessItem("#2", out lblTopProc2));
            sp.Children.Add(CreateProcessItem("#3", out lblTopProc3));

            // Instant Purge Button
            Border btnPurge = CreateActionButton("⚡ Purge Unused Memory", () =>
            {
                HardwareBridge.OptimizeMemory();
                if (lblStatusMsg != null)
                {
                    lblStatusMsg.Text = "Memory Purged: Working sets trimmed safely";
                    lblStatusMsg.Foreground = DesignTokens.BrushAccentGreen;
                }
            });
            sp.Children.Add(btnPurge);

            Border card = CreateMinimalCard("Memory & Processes", "32 GB DDR5", sp);
            card.Margin = new Thickness(3, 0, 3, 0);
            return card;
        }

        private Border CreatePowerHealthCard()
        {
            StackPanel sp = new StackPanel();

            // Battery Section
            sp.Children.Add(CreateMetricHeader("Battery Power", out lblHealthBatteryText, "100% (AC Mains)", DesignTokens.AccentGreen));
            meterHealthBattery = new SegmentedMeter(12, DesignTokens.AccentGreen, 18, 5);
            sp.Children.Add(meterHealthBattery);

            lblHealthBatteryWear = new TextBlock
            {
                Text = "Design: 52.5 Wh | Current: 53.8 Wh (0.0% Wear)",
                FontSize = 10,
                Foreground = DesignTokens.BrushTextSecondary,
                Margin = new Thickness(0, 4, 0, 10)
            };
            sp.Children.Add(lblHealthBatteryWear);

            // Storage NVMe Section
            sp.Children.Add(CreateMetricHeader("Primary Storage (C: NVMe)", out lblHealthDiskText, "Checking capacity...", DesignTokens.AccentCyan));
            meterHealthDisk = new SegmentedMeter(12, DesignTokens.AccentCyan, 18, 5);
            sp.Children.Add(meterHealthDisk);

            // CPU Headroom
            StackPanel statSp = new StackPanel { Margin = new Thickness(0, 12, 0, 0) };
            statSp.Children.Add(CreateStatusLine("CPU Silicon Headroom:", out lblHealthCpuHeadroom, "+55°C Margin (to 100°C TJMax)", DesignTokens.AccentGreen));
            sp.Children.Add(statSp);

            // Export Diagnostics Button
            Border btnExport = CreateActionButton("📋 Copy Diagnostics Report", () =>
            {
                CopyDiagnosticsReport();
            });
            sp.Children.Add(btnExport);

            Border card = CreateMinimalCard("Power & Storage Health", "Optimal", sp);
            card.Margin = new Thickness(6, 0, 0, 0);
            return card;
        }

        private Border CreateMinimalCard(string title, string badge, UIElement content)
        {
            Border card = new Border
            {
                Background = DesignTokens.BrushBgCard,
                CornerRadius = new CornerRadius(10),
                BorderBrush = DesignTokens.BrushBorderSubtle,
                BorderThickness = new Thickness(1),
                Padding = new Thickness(16, 14, 16, 14)
            };

            Grid header = new Grid { Margin = new Thickness(0, 0, 0, 12) };
            header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            TextBlock t = new TextBlock
            {
                Text = title,
                FontSize = 12.5,
                FontWeight = FontWeights.Bold,
                Foreground = DesignTokens.BrushTextPrimary,
                VerticalAlignment = VerticalAlignment.Center
            };
            Grid.SetColumn(t, 0);
            header.Children.Add(t);

            Border bPill = new Border
            {
                Background = DesignTokens.BrushBgInput,
                BorderBrush = DesignTokens.BrushBorderSubtle,
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(4),
                Padding = new Thickness(8, 2, 8, 2)
            };
            bPill.Child = new TextBlock
            {
                Text = badge,
                FontSize = 10,
                FontWeight = FontWeights.SemiBold,
                Foreground = DesignTokens.BrushAccentCyan
            };
            Grid.SetColumn(bPill, 1);
            header.Children.Add(bPill);

            StackPanel root = new StackPanel();
            root.Children.Add(header);
            root.Children.Add(content);
            card.Child = root;

            return card;
        }

        private Grid CreateMetricHeader(string label, out TextBlock valTb, string defaultVal, Color valColor)
        {
            Grid g = new Grid { Margin = new Thickness(0, 4, 0, 6) };
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            TextBlock lTb = new TextBlock { Text = label, FontSize = 10, FontWeight = FontWeights.SemiBold, Foreground = DesignTokens.BrushTextSecondary, VerticalAlignment = VerticalAlignment.Center };
            Grid.SetColumn(lTb, 0);
            g.Children.Add(lTb);

            valTb = new TextBlock { Text = defaultVal, FontSize = 11, FontWeight = FontWeights.Bold, Foreground = new SolidColorBrush(valColor), VerticalAlignment = VerticalAlignment.Center };
            Grid.SetColumn(valTb, 1);
            g.Children.Add(valTb);

            return g;
        }

        private Border CreateClockTile(string label, out TextBlock valTb, string defVal, string unit)
        {
            Border b = new Border
            {
                Background = DesignTokens.BrushBgInput,
                BorderBrush = DesignTokens.BrushBorderSubtle,
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(6),
                Padding = new Thickness(10, 8, 10, 8),
                Margin = new Thickness(3)
            };
            StackPanel sp = new StackPanel();
            sp.Children.Add(new TextBlock { Text = label, FontSize = 10, FontWeight = FontWeights.SemiBold, Foreground = DesignTokens.BrushTextSecondary });

            StackPanel numSp = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 2, 0, 0) };
            valTb = new TextBlock { Text = defVal, FontSize = 20, FontWeight = FontWeights.Bold, Foreground = DesignTokens.BrushTextPrimary };
            numSp.Children.Add(valTb);
            numSp.Children.Add(new TextBlock { Text = " " + unit, FontSize = 10.5, FontWeight = FontWeights.SemiBold, Foreground = DesignTokens.BrushAccentCyan, VerticalAlignment = VerticalAlignment.Bottom, Margin = new Thickness(0, 0, 0, 3) });
            sp.Children.Add(numSp);

            b.Child = sp;
            return b;
        }

        private Grid CreateStatusLine(string label, out TextBlock valTb, string defVal, Color valColor)
        {
            Grid g = new Grid { Margin = new Thickness(0, 3, 0, 3) };
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(135) });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            TextBlock lTb = new TextBlock { Text = label, FontSize = 10, FontWeight = FontWeights.SemiBold, Foreground = DesignTokens.BrushTextSecondary, VerticalAlignment = VerticalAlignment.Center };
            Grid.SetColumn(lTb, 0);
            g.Children.Add(lTb);

            valTb = new TextBlock { Text = defVal, FontSize = 10, FontWeight = FontWeights.SemiBold, Foreground = new SolidColorBrush(valColor), VerticalAlignment = VerticalAlignment.Center };
            Grid.SetColumn(valTb, 1);
            g.Children.Add(valTb);

            return g;
        }

        private Grid CreateStatusLine(string label, string val, Color valColor)
        {
            TextBlock dummy;
            return CreateStatusLine(label, out dummy, val, valColor);
        }

        private Border CreateProcessItem(string rank, out TextBlock txt)
        {
            Border b = new Border
            {
                Background = DesignTokens.BrushBgInput,
                BorderBrush = DesignTokens.BrushBorderSubtle,
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(5),
                Padding = new Thickness(10, 6, 10, 6),
                Margin = new Thickness(0, 2, 0, 2)
            };
            Grid g = new Grid();
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(24) });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            TextBlock rTb = new TextBlock { Text = rank, FontSize = 10, FontWeight = FontWeights.Bold, Foreground = DesignTokens.BrushAccentPurple, VerticalAlignment = VerticalAlignment.Center };
            Grid.SetColumn(rTb, 0);
            g.Children.Add(rTb);

            txt = new TextBlock { Text = "Analyzing...", FontSize = 10.5, FontWeight = FontWeights.Medium, Foreground = DesignTokens.BrushTextPrimary, VerticalAlignment = VerticalAlignment.Center };
            Grid.SetColumn(txt, 1);
            g.Children.Add(txt);

            b.Child = g;
            return b;
        }

        private Border CreateActionButton(string label, Action action)
        {
            Border b = new Border
            {
                Background = DesignTokens.BrushBgInput,
                BorderBrush = DesignTokens.BrushBorderSubtle,
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(6),
                Padding = new Thickness(12, 8, 12, 8),
                Margin = new Thickness(0, 10, 0, 0),
                Cursor = Cursors.Hand
            };
            TextBlock t = new TextBlock
            {
                Text = label,
                FontSize = 10.5,
                FontWeight = FontWeights.Bold,
                Foreground = DesignTokens.BrushAccentCyan,
                HorizontalAlignment = HorizontalAlignment.Center
            };
            b.Child = t;
            b.MouseEnter += (s, e) => {
                b.Background = DesignTokens.BrushBgCardHover;
                b.BorderBrush = DesignTokens.BrushBorderSubtleLight;
                t.Foreground = Brushes.White;
            };
            b.MouseLeave += (s, e) => {
                b.Background = DesignTokens.BrushBgInput;
                b.BorderBrush = DesignTokens.BrushBorderSubtle;
                t.Foreground = DesignTokens.BrushAccentCyan;
            };
            b.MouseDown += (s, e) => { action(); };
            return b;
        }

        private void CopyDiagnosticsReport()
        {
            try
            {
                HardwareBridge.SensorData d = latestSensorData ?? HardwareBridge.ReadSensors(cpuCounter);
                string report = string.Format(
                    "================ ASPIRESENSE SYSTEM DIAGNOSTICS ================\n" +
                    "Timestamp:        {0}\n" +
                    "Chassis Model:    {1}\n" +
                    "Processor:        {2} ({3} Cores)\n" +
                    "Graphics Card:    {4}\n" +
                    "BIOS Version:     {5} | EC: {6}\n" +
                    "Operating System: {7}\n" +
                    "System Uptime:    {8}d {9:D2}h {10:D2}m {11:D2}s\n\n" +
                    "--- LIVE HARDWARE SENSORS ---\n" +
                    "CPU Temperature:  {12}°C (Headroom to 100°C TJMax: +{13}°C)\n" +
                    "GPU Temperature:  {14}°C (Headroom to 87°C Limit: +{15}°C)\n" +
                    "GPU Core Clock:   {16} MHz | Mem Clock: {17} MHz\n" +
                    "GPU VRAM In Use:  {18:F1} / {19:F1} GB ({20:F0}%)\n" +
                    "GPU Throttle:     {21}\n" +
                    "System RAM:       {22:F1} / {23:F1} GB ({24:F0}%)\n" +
                    "Battery Status:   {25}% ({26})\n" +
                    "Battery Health:   {27:F1}% ({28:F1}% Degradation)\n" +
                    "=================================================================\n",
                    DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"),
                    HardwareBridge.ChassisModel,
                    HardwareBridge.ChassisCpuName, Environment.ProcessorCount,
                    HardwareBridge.ChassisGpuName,
                    HardwareBridge.ChassisBiosVersion, HardwareBridge.ChassisEcVersion,
                    HardwareBridge.ChassisOs,
                    d.SystemUptime.Days, d.SystemUptime.Hours, d.SystemUptime.Minutes, d.SystemUptime.Seconds,
                    d.CpuTemp, Math.Max(0, 100 - d.CpuTemp),
                    d.GpuTemp, Math.Max(0, 87 - d.GpuTemp),
                    d.GpuCoreClock, d.GpuMemClock,
                    d.VramUsedGb, d.VramTotalGb, d.VramUsage,
                    d.GpuThrottleStatus,
                    d.UsedRamGb, d.TotalRamGb, d.RamUsage,
                    d.BatteryPercent, d.IsAcPlugged ? "AC Mains" : "On Battery",
                    d.BatteryHealthPct, Math.Max(0.0, 100.0 - d.BatteryHealthPct)
                );
                Clipboard.SetText(report);
                if (lblStatusMsg != null)
                {
                    lblStatusMsg.Text = "Diagnostic report copied to clipboard";
                    lblStatusMsg.Foreground = DesignTokens.BrushAccentGreen;
                }
            }
            catch (Exception ex)
            {
                if (lblStatusMsg != null) lblStatusMsg.Text = "Export error: " + ex.Message;
            }
        }

        // ==========================================
        // PAGE 5: EC ANTI-HALLUCINATION ARBITER VIEW
        // ==========================================
        private Grid CreateEcGuardView()
        {
            Grid g = new Grid { Margin = new Thickness(35, 12, 35, 12) };
            g.RowDefinitions.Add(new RowDefinition { Height = new GridLength(88) }); // Top Ribbon: Live Analytics
            g.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) }); // 3 Analytics & Controls Cards

            // 1. Top Ribbon: 5 Silicon & Acoustic Metrics
            Border ribbon = CreateEcGuardRibbon();
            Grid.SetRow(ribbon, 0);
            g.Children.Add(ribbon);

            // 2. Lower Grid: 3 Equal Cards
            Grid lowerGrid = new Grid { Margin = new Thickness(0, 12, 0, 0) };
            lowerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            lowerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            lowerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            // Card 1: Hysteresis & Clamp Settings (Manual Controls)
            Border card1 = CreateEcSettingsCard();
            Grid.SetColumn(card1, 0);
            lowerGrid.Children.Add(card1);

            // Card 2: Gaming Integration & Windows Turbo Tamer (Toggles & Optimizations)
            Border card2 = CreateEcIntegrationCard();
            Grid.SetColumn(card2, 1);
            lowerGrid.Children.Add(card2);

            // Card 3: Live Decision Stream & Telemetry Feed (Real-Time Decision Log)
            Border card3 = CreateEcDecisionLogCard();
            Grid.SetColumn(card3, 2);
            lowerGrid.Children.Add(card3);

            Grid.SetRow(lowerGrid, 1);
            g.Children.Add(lowerGrid);

            return g;
        }

        private Border CreateEcGuardRibbon()
        {
            Border ribbon = new Border
            {
                Background = DesignTokens.BrushBgCard,
                CornerRadius = new CornerRadius(10),
                BorderBrush = DesignTokens.BrushBorderSubtle,
                BorderThickness = new Thickness(1),
                Padding = new Thickness(14, 8, 14, 8)
            };

            UniformGrid ug = new UniformGrid { Columns = 5 };

            ug.Children.Add(CreateRibbonTile("Silicon Die", "--°C", "Raw CPU Junction Reading", out lblEcRawDie));
            ug.Children.Add(CreateRibbonTile("Heatsink EMA", "--°C", "Physical Thermal Mass EMA", out lblEcThermalEma));
            ug.Children.Add(CreateRibbonTile("Spikes Blocked", "0 Spikes", "Jet-Fan Revs Suppressed", out lblEcSpikesCount));
            ug.Children.Add(CreateRibbonTile("Acoustic Hold", "35% Duty", "Whisper-Quiet Floor", out lblEcAcousticHold));
            ug.Children.Add(CreateRibbonTile("Arbiter Status", "Protected", "Live Classifier Decision", out lblEcClassifierState));

            ribbon.Child = ug;
            return ribbon;
        }

        private Border CreateEcSettingsCard()
        {
            StackPanel sp = new StackPanel { Margin = new Thickness(0, 4, 0, 4) };

            // Master Toggle Button
            btnEcMasterToggle = new Border
            {
                Background = new SolidColorBrush(Color.FromArgb(50, 16, 185, 129)),
                BorderBrush = DesignTokens.BrushAccentGreen,
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(8),
                Padding = new Thickness(14, 10, 14, 10),
                Margin = new Thickness(0, 0, 0, 14),
                Cursor = Cursors.Hand
            };
            lblEcMasterToggleText = new TextBlock
            {
                Text = "🛡️ EC Anti-Hallucination Guard: ENABLED",
                FontSize = 11.5,
                FontWeight = FontWeights.Bold,
                Foreground = Brushes.White,
                HorizontalAlignment = HorizontalAlignment.Center
            };
            btnEcMasterToggle.Child = lblEcMasterToggleText;
            btnEcMasterToggle.MouseDown += (s, e) =>
            {
                EcAntiHallucinationEngine.Toggle();
                UpdateDashboardEcGuardUi();
            };
            sp.Children.Add(btnEcMasterToggle);

            // Group 1: Acoustic Clamp Ceiling
            sp.Children.Add(new TextBlock
            {
                Text = "ACOUSTIC CLAMP CEILING (DESKTOP & BROWSING)",
                FontSize = 9.5,
                FontWeight = FontWeights.SemiBold,
                Foreground = DesignTokens.BrushTextMuted,
                Margin = new Thickness(0, 0, 0, 6)
            });

            UniformGrid clampGrid = new UniformGrid { Columns = 4, Margin = new Thickness(0, 0, 0, 14) };
            ecClampButtons.Clear();
            ecClampButtons.Add(CreateEcSelectorPill("30% Silent", 30, EcAntiHallucinationEngine.AcousticClampDuty == 30, (val) => {
                EcAntiHallucinationEngine.SetClampDuty(val);
                UpdateEcSelectorPills(ecClampButtons, val);
                UpdateDashboardEcGuardUi();
            }));
            ecClampButtons.Add(CreateEcSelectorPill("35% Quiet", 35, EcAntiHallucinationEngine.AcousticClampDuty == 35, (val) => {
                EcAntiHallucinationEngine.SetClampDuty(val);
                UpdateEcSelectorPills(ecClampButtons, val);
                UpdateDashboardEcGuardUi();
            }));
            ecClampButtons.Add(CreateEcSelectorPill("40% Mod", 40, EcAntiHallucinationEngine.AcousticClampDuty == 40, (val) => {
                EcAntiHallucinationEngine.SetClampDuty(val);
                UpdateEcSelectorPills(ecClampButtons, val);
                UpdateDashboardEcGuardUi();
            }));
            ecClampButtons.Add(CreateEcSelectorPill("45% Firm", 45, EcAntiHallucinationEngine.AcousticClampDuty == 45, (val) => {
                EcAntiHallucinationEngine.SetClampDuty(val);
                UpdateEcSelectorPills(ecClampButtons, val);
                UpdateDashboardEcGuardUi();
            }));
            foreach (Border b in ecClampButtons) clampGrid.Children.Add(b);
            sp.Children.Add(clampGrid);

            // Group 2: Sustained Load Delay (Hysteresis Window)
            sp.Children.Add(new TextBlock
            {
                Text = "SUSTAINED LOAD DELAY (HYSTERESIS WINDOW)",
                FontSize = 9.5,
                FontWeight = FontWeights.SemiBold,
                Foreground = DesignTokens.BrushTextMuted,
                Margin = new Thickness(0, 0, 0, 6)
            });

            UniformGrid hystGrid = new UniformGrid { Columns = 4, Margin = new Thickness(0, 0, 0, 14) };
            ecHysteresisButtons.Clear();
            ecHysteresisButtons.Add(CreateEcSelectorPill("2 Seconds", 2, EcAntiHallucinationEngine.SustainedThresholdSeconds == 2, (val) => {
                EcAntiHallucinationEngine.SetHysteresisSeconds(val);
                UpdateEcSelectorPills(ecHysteresisButtons, val);
            }));
            ecHysteresisButtons.Add(CreateEcSelectorPill("3 Seconds", 3, EcAntiHallucinationEngine.SustainedThresholdSeconds == 3, (val) => {
                EcAntiHallucinationEngine.SetHysteresisSeconds(val);
                UpdateEcSelectorPills(ecHysteresisButtons, val);
            }));
            ecHysteresisButtons.Add(CreateEcSelectorPill("4s (Best)", 4, EcAntiHallucinationEngine.SustainedThresholdSeconds == 4, (val) => {
                EcAntiHallucinationEngine.SetHysteresisSeconds(val);
                UpdateEcSelectorPills(ecHysteresisButtons, val);
            }));
            ecHysteresisButtons.Add(CreateEcSelectorPill("6 Seconds", 6, EcAntiHallucinationEngine.SustainedThresholdSeconds == 6, (val) => {
                EcAntiHallucinationEngine.SetHysteresisSeconds(val);
                UpdateEcSelectorPills(ecHysteresisButtons, val);
            }));
            foreach (Border b in ecHysteresisButtons) hystGrid.Children.Add(b);
            sp.Children.Add(hystGrid);

            // Group 3: Emergency Failsafe Temperature
            sp.Children.Add(new TextBlock
            {
                Text = "EMERGENCY SAFETY OVERRIDE (FAILSAFE TEMP)",
                FontSize = 9.5,
                FontWeight = FontWeights.SemiBold,
                Foreground = DesignTokens.BrushTextMuted,
                Margin = new Thickness(0, 0, 0, 6)
            });

            UniformGrid failGrid = new UniformGrid { Columns = 4, Margin = new Thickness(0, 0, 0, 8) };
            ecFailsafeButtons.Clear();
            ecFailsafeButtons.Add(CreateEcSelectorPill("82°C", 82, EcAntiHallucinationEngine.FailsafeTemp == 82, (val) => {
                EcAntiHallucinationEngine.SetFailsafeTemp(val);
                UpdateEcSelectorPills(ecFailsafeButtons, val);
            }));
            ecFailsafeButtons.Add(CreateEcSelectorPill("85°C", 85, EcAntiHallucinationEngine.FailsafeTemp == 85, (val) => {
                EcAntiHallucinationEngine.SetFailsafeTemp(val);
                UpdateEcSelectorPills(ecFailsafeButtons, val);
            }));
            ecFailsafeButtons.Add(CreateEcSelectorPill("88°C (Best)", 88, EcAntiHallucinationEngine.FailsafeTemp == 88, (val) => {
                EcAntiHallucinationEngine.SetFailsafeTemp(val);
                UpdateEcSelectorPills(ecFailsafeButtons, val);
            }));
            ecFailsafeButtons.Add(CreateEcSelectorPill("92°C", 92, EcAntiHallucinationEngine.FailsafeTemp == 92, (val) => {
                EcAntiHallucinationEngine.SetFailsafeTemp(val);
                UpdateEcSelectorPills(ecFailsafeButtons, val);
            }));
            foreach (Border b in ecFailsafeButtons) failGrid.Children.Add(b);
            sp.Children.Add(failGrid);

            return CreateSectionCard("Hysteresis & Acoustic Settings", "Manual control over EC interception and delay windows", sp);
        }

        private Border CreateEcIntegrationCard()
        {
            StackPanel sp = new StackPanel { Margin = new Thickness(0, 4, 0, 4) };

            // 1. DirectX Zero-Hook Gaming Auto-Detection
            btnEcGameToggle = new Border
            {
                Background = EcAntiHallucinationEngine.AutoUnclampGaming ? new SolidColorBrush(Color.FromArgb(40, 0, 122, 255)) : DesignTokens.BrushBgInput,
                BorderBrush = EcAntiHallucinationEngine.AutoUnclampGaming ? DesignTokens.BrushAccentBlue : DesignTokens.BrushBorderSubtle,
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(8),
                Padding = new Thickness(12, 8, 12, 8),
                Margin = new Thickness(0, 0, 0, 12),
                Cursor = Cursors.Hand
            };
            StackPanel gSp = new StackPanel();
            lblEcGameToggleText = new TextBlock
            {
                Text = EcAntiHallucinationEngine.AutoUnclampGaming ? "🎮 3D Game Detection: ON (Auto-Unclamp)" : "🎮 3D Game Detection: OFF",
                FontSize = 11,
                FontWeight = FontWeights.Bold,
                Foreground = EcAntiHallucinationEngine.AutoUnclampGaming ? DesignTokens.BrushAccentBlue : DesignTokens.BrushTextSecondary
            };
            gSp.Children.Add(lblEcGameToggleText);
            gSp.Children.Add(new TextBlock
            {
                Text = "Releases acoustic clamps the moment DirectX/Vulkan 3D frames begin via ETW.",
                FontSize = 9.5,
                Foreground = DesignTokens.BrushTextMuted,
                Margin = new Thickness(0, 2, 0, 0)
            });
            btnEcGameToggle.Child = gSp;
            btnEcGameToggle.MouseDown += (s, e) =>
            {
                EcAntiHallucinationEngine.AutoUnclampGaming = !EcAntiHallucinationEngine.AutoUnclampGaming;
                lblEcGameToggleText.Text = EcAntiHallucinationEngine.AutoUnclampGaming ? "🎮 3D Game Detection: ON (Auto-Unclamp)" : "🎮 3D Game Detection: OFF";
                lblEcGameToggleText.Foreground = EcAntiHallucinationEngine.AutoUnclampGaming ? DesignTokens.BrushAccentBlue : DesignTokens.BrushTextSecondary;
                btnEcGameToggle.Background = EcAntiHallucinationEngine.AutoUnclampGaming ? new SolidColorBrush(Color.FromArgb(40, 0, 122, 255)) : DesignTokens.BrushBgInput;
                btnEcGameToggle.BorderBrush = EcAntiHallucinationEngine.AutoUnclampGaming ? DesignTokens.BrushAccentBlue : DesignTokens.BrushBorderSubtle;
                EcAntiHallucinationEngine.LogDecision("CONFIG: Game auto-detection set to " + (EcAntiHallucinationEngine.AutoUnclampGaming ? "ON" : "OFF"));
            };
            sp.Children.Add(btnEcGameToggle);

            // 2. Windows CPU Boost Tamer (99% State)
            btnEcBoostTamer = new Border
            {
                Background = EcAntiHallucinationEngine.PowerBoostTamed ? new SolidColorBrush(Color.FromArgb(40, 16, 185, 129)) : DesignTokens.BrushBgInput,
                BorderBrush = EcAntiHallucinationEngine.PowerBoostTamed ? DesignTokens.BrushAccentGreen : DesignTokens.BrushBorderSubtle,
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(8),
                Padding = new Thickness(12, 8, 12, 8),
                Margin = new Thickness(0, 0, 0, 12),
                Cursor = Cursors.Hand
            };
            StackPanel bSp = new StackPanel();
            lblEcBoostTamerText = new TextBlock
            {
                Text = EcAntiHallucinationEngine.PowerBoostTamed ? "⚡ CPU Turbo Spikes: TAMED (99% Mode)" : "⚡ CPU Turbo Spikes: DEFAULT (100% Boost)",
                FontSize = 11,
                FontWeight = FontWeights.Bold,
                Foreground = EcAntiHallucinationEngine.PowerBoostTamed ? DesignTokens.BrushAccentGreen : DesignTokens.BrushTextSecondary
            };
            bSp.Children.Add(lblEcBoostTamerText);
            lblEcBoostTamerStatus = new TextBlock
            {
                Text = EcAntiHallucinationEngine.PowerBoostTamed ? "Micro-burst turbo overvolt eliminated. 100% base clock speed preserved." : "Windows Balanced: Aggressive turbo burst enabled.",
                FontSize = 9.5,
                Foreground = DesignTokens.BrushTextMuted,
                Margin = new Thickness(0, 2, 0, 0)
            };
            bSp.Children.Add(lblEcBoostTamerStatus);
            btnEcBoostTamer.Child = bSp;
            btnEcBoostTamer.MouseDown += (s, e) =>
            {
                EcAntiHallucinationEngine.TogglePowerBoostTamer();
                lblEcBoostTamerText.Text = EcAntiHallucinationEngine.PowerBoostTamed ? "⚡ CPU Turbo Spikes: TAMED (99% Mode)" : "⚡ CPU Turbo Spikes: DEFAULT (100% Boost)";
                lblEcBoostTamerText.Foreground = EcAntiHallucinationEngine.PowerBoostTamed ? DesignTokens.BrushAccentGreen : DesignTokens.BrushTextSecondary;
                lblEcBoostTamerStatus.Text = EcAntiHallucinationEngine.PowerBoostTamed ? "Micro-burst turbo overvolt eliminated. 100% base clock speed preserved." : "Windows Balanced: Aggressive turbo burst enabled.";
                btnEcBoostTamer.Background = EcAntiHallucinationEngine.PowerBoostTamed ? new SolidColorBrush(Color.FromArgb(40, 16, 185, 129)) : DesignTokens.BrushBgInput;
                btnEcBoostTamer.BorderBrush = EcAntiHallucinationEngine.PowerBoostTamed ? DesignTokens.BrushAccentGreen : DesignTokens.BrushBorderSubtle;
            };
            sp.Children.Add(btnEcBoostTamer);

            // 3. Heatsink Thermal Saturation Bar
            lblHeatsinkSatText = new TextBlock
            {
                Text = "Heatsink Thermal Saturation: --% (--°C EMA)",
                FontSize = 10,
                FontWeight = FontWeights.SemiBold,
                Foreground = DesignTokens.BrushTextSecondary,
                Margin = new Thickness(0, 4, 0, 4)
            };
            sp.Children.Add(lblHeatsinkSatText);

            meterHeatsinkSaturation = new SegmentedMeter(10, DesignTokens.AccentCyan, 28, 4);
            meterHeatsinkSaturation.Margin = new Thickness(0, 0, 0, 12);
            sp.Children.Add(meterHeatsinkSaturation);

            // 4. One-Click EC Hardware Reset
            Border btnResetEc = CreateActionButton("🔄 Reload Baseline EC Hardware Registers", () =>
            {
                HardwareBridge.ResetToAuto();
                EcAntiHallucinationEngine.LogDecision("EC RESET: Reloaded factory EC baseline firmware tables");
                if (lblStatusMsg != null)
                {
                    lblStatusMsg.Text = "EC registers reset to factory baseline";
                    lblStatusMsg.Foreground = DesignTokens.BrushAccentGreen;
                }
            });
            sp.Children.Add(btnResetEc);

            return CreateSectionCard("Gaming & Silicon Integration", "Zero-hook ETW detection, CPU overvolt tamer, and EC reset", sp);
        }

        private Border CreateEcDecisionLogCard()
        {
            StackPanel sp = new StackPanel { Margin = new Thickness(0, 4, 0, 4) };

            Border logBox = new Border
            {
                Background = DesignTokens.BrushBgInput,
                BorderBrush = DesignTokens.BrushBorderSubtle,
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(8),
                Padding = new Thickness(10),
                Height = 220
            };

            ScrollViewer sv = new ScrollViewer
            {
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled
            };

            txtDecisionLog = new TextBlock
            {
                Text = "Initializing Decision Stream...",
                FontSize = 9.5,
                FontFamily = new FontFamily("Consolas, Courier New"),
                Foreground = DesignTokens.BrushTextSecondary,
                TextWrapping = TextWrapping.Wrap
            };
            sv.Content = txtDecisionLog;
            logBox.Child = sv;
            sp.Children.Add(logBox);

            UniformGrid btnGrid = new UniformGrid { Columns = 2, Margin = new Thickness(0, 10, 0, 0) };

            Border btnClear = CreateActionButton("Clear Log", () =>
            {
                EcAntiHallucinationEngine.ClearLogs();
                if (txtDecisionLog != null) txtDecisionLog.Text = "Decision stream cleared by user";
            });
            btnGrid.Children.Add(btnClear);

            Border btnCopy = CreateActionButton("Copy Stream", () =>
            {
                try
                {
                    Clipboard.SetText(string.Join("\n", EcAntiHallucinationEngine.GetRecentLogs(50).ToArray()));
                    if (lblStatusMsg != null)
                    {
                        lblStatusMsg.Text = "Decision stream copied to clipboard";
                        lblStatusMsg.Foreground = DesignTokens.BrushAccentGreen;
                    }
                }
                catch { }
            });
            btnGrid.Children.Add(btnCopy);

            sp.Children.Add(btnGrid);

            return CreateSectionCard("Decision Stream & Telemetry Log", "Real-time classifier state, spike interceptions, and EC commands", sp);
        }

        private Border CreateEcSelectorPill(string label, int value, bool isSelected, Action<int> onClick)
        {
            Border b = new Border
            {
                Background = isSelected ? DesignTokens.BrushAccentBlue : DesignTokens.BrushBgInput,
                BorderBrush = isSelected ? DesignTokens.BrushAccentBlue : DesignTokens.BrushBorderSubtle,
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(6),
                Padding = new Thickness(4, 6, 4, 6),
                Margin = new Thickness(2),
                Cursor = Cursors.Hand,
                Tag = value
            };

            TextBlock tb = new TextBlock
            {
                Text = label,
                FontSize = 9.5,
                FontWeight = isSelected ? FontWeights.Bold : FontWeights.SemiBold,
                Foreground = isSelected ? Brushes.White : DesignTokens.BrushTextSecondary,
                HorizontalAlignment = HorizontalAlignment.Center
            };
            b.Child = tb;

            b.MouseDown += (s, e) => { onClick(value); };
            return b;
        }

        private void UpdateEcSelectorPills(List<Border> pills, int selectedVal)
        {
            foreach (Border b in pills)
            {
                if (b.Tag != null && (int)b.Tag == selectedVal)
                {
                    b.Background = DesignTokens.BrushAccentBlue;
                    b.BorderBrush = DesignTokens.BrushAccentBlue;
                    TextBlock tb = b.Child as TextBlock;
                    if (tb != null)
                    {
                        tb.Foreground = Brushes.White;
                        tb.FontWeight = FontWeights.Bold;
                    }
                }
                else
                {
                    b.Background = DesignTokens.BrushBgInput;
                    b.BorderBrush = DesignTokens.BrushBorderSubtle;
                    TextBlock tb = b.Child as TextBlock;
                    if (tb != null)
                    {
                        tb.Foreground = DesignTokens.BrushTextSecondary;
                        tb.FontWeight = FontWeights.SemiBold;
                    }
                }
            }
        }

        private void UpdateDashboardEcGuardUi()
        {
            bool enabled = EcAntiHallucinationEngine.IsEnabled;
            if (lblDashboardEcGuardIndicator != null)
            {
                lblDashboardEcGuardIndicator.Text = enabled ? "🛡️ EC Guard: ACTIVE" : "⚪ EC Guard: DISABLED";
                lblDashboardEcGuardIndicator.Foreground = enabled ? DesignTokens.BrushAccentGreen : DesignTokens.BrushTextMuted;
            }
            if (lblDashboardEcGuardStatus != null)
            {
                if (!enabled)
                {
                    lblDashboardEcGuardStatus.Text = "Raw EC Control (No Interception)";
                    lblDashboardEcGuardStatus.Foreground = DesignTokens.BrushTextMuted;
                }
                else if (EcAntiHallucinationEngine.IsCurrentlyClamped)
                {
                    lblDashboardEcGuardStatus.Text = "Silent Mode (Anti-Revving Active)";
                    lblDashboardEcGuardStatus.Foreground = DesignTokens.BrushAccentCyan;
                }
                else
                {
                    lblDashboardEcGuardStatus.Text = "Cooling Released";
                    lblDashboardEcGuardStatus.Foreground = DesignTokens.BrushAccentAmber;
                }
            }
            if (btnToolEcGuard != null)
            {
                TextBlock tb = btnToolEcGuard.Child as TextBlock;
                if (tb != null)
                {
                    tb.Text = enabled ? "🛡️ EC Guard: ON" : "⚪ EC Guard: OFF";
                    tb.Foreground = enabled ? DesignTokens.BrushAccentGreen : DesignTokens.BrushTextSecondary;
                }
            }
            if (btnEcMasterToggle != null && lblEcMasterToggleText != null)
            {
                btnEcMasterToggle.Background = enabled ? new SolidColorBrush(Color.FromArgb(50, 16, 185, 129)) : DesignTokens.BrushBgInput;
                btnEcMasterToggle.BorderBrush = enabled ? DesignTokens.BrushAccentGreen : DesignTokens.BrushBorderSubtle;
                lblEcMasterToggleText.Text = enabled ? "🛡️ EC Anti-Hallucination Guard: ENABLED" : "⚪ EC Anti-Hallucination Guard: DISABLED";
                lblEcMasterToggleText.Foreground = enabled ? Brushes.White : DesignTokens.BrushTextSecondary;
            }
        }

        private void UpdateEcGuardView(HardwareBridge.SensorData d)
        {
            if (lblEcRawDie != null)
            {
                lblEcRawDie.Text = string.Format("{0}°C", d.CpuTemp);
                if (d.CpuTemp >= 80) lblEcRawDie.Foreground = DesignTokens.BrushAccentRed;
                else if (d.CpuTemp >= 68) lblEcRawDie.Foreground = DesignTokens.BrushAccentAmber;
                else lblEcRawDie.Foreground = DesignTokens.BrushTextPrimary;
            }
            if (lblEcThermalEma != null)
            {
                lblEcThermalEma.Text = string.Format("{0:F1}°C", EcAntiHallucinationEngine.FilteredThermalEma);
            }
            if (lblEcSpikesCount != null)
            {
                lblEcSpikesCount.Text = string.Format("{0} Spikes", EcAntiHallucinationEngine.InterceptedSpikeCount);
            }
            if (lblEcAcousticHold != null)
            {
                lblEcAcousticHold.Text = EcAntiHallucinationEngine.IsCurrentlyClamped 
                    ? string.Format("{0}% Duty", EcAntiHallucinationEngine.AcousticClampDuty)
                    : "Released";
                lblEcAcousticHold.Foreground = EcAntiHallucinationEngine.IsCurrentlyClamped 
                    ? DesignTokens.BrushAccentGreen 
                    : DesignTokens.BrushAccentAmber;
            }
            if (lblEcClassifierState != null)
            {
                lblEcClassifierState.Text = EcAntiHallucinationEngine.CurrentClassifierState;
            }
            if (meterHeatsinkSaturation != null && lblHeatsinkSatText != null)
            {
                meterHeatsinkSaturation.SetValue(EcAntiHallucinationEngine.HeatsinkSaturationPct);
                lblHeatsinkSatText.Text = string.Format("Heatsink Thermal Saturation: {0:F0}% ({1:F1}°C EMA)", 
                    EcAntiHallucinationEngine.HeatsinkSaturationPct, EcAntiHallucinationEngine.FilteredThermalEma);
            }
            if (txtDecisionLog != null)
            {
                List<string> logs = EcAntiHallucinationEngine.GetRecentLogs(10);
                txtDecisionLog.Text = string.Join("\n", logs.ToArray());
            }
        }

        private Grid CreateFooter()
        {
            Grid f = new Grid { Background = DesignTokens.BrushBgWindow };
            f.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            f.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(350) });

            string archInfo;
            string statusInfo;
            switch (HardwareBridge.DetectedVendor)
            {
                case HardwareVendor.Asus:
                    archInfo = string.Format("AspireSense 2026 | {0} | ASUS Direct ACPI Hardware Engine Active", HardwareBridge.ChassisModel);
                    statusInfo = "Status: ASUS Direct ACPI Active";
                    break;
                case HardwareVendor.Lenovo:
                    archInfo = string.Format("AspireSense 2026 | {0} | Lenovo Vantage EC / WMI Engine Active", HardwareBridge.ChassisModel);
                    statusInfo = "Status: Lenovo WMI Interface Active";
                    break;
                case HardwareVendor.Hp:
                    archInfo = string.Format("AspireSense 2026 | {0} | HP Omen / Victus BIOS WMI Active", HardwareBridge.ChassisModel);
                    statusInfo = "Status: HP WMI Interface Active";
                    break;
                case HardwareVendor.Dell:
                    archInfo = string.Format("AspireSense 2026 | {0} | Dell Alienware / G-Series WMAX Active", HardwareBridge.ChassisModel);
                    statusInfo = "Status: Dell WMAX Interface Active";
                    break;
                case HardwareVendor.Acer:
                    archInfo = string.Format("AspireSense 2026 | {0} | Acer Standalone EC Engine Active", HardwareBridge.ChassisModel);
                    statusInfo = "Status: Standalone EC Active";
                    break;
                default:
                    archInfo = string.Format("AspireSense 2026 | {0} | Universal Telemetry Engine Active", HardwareBridge.ChassisModel);
                    statusInfo = "Status: Universal ACPI Engine Active";
                    break;
            }

            TextBlock info = new TextBlock
            {
                Text = archInfo,
                FontSize = 10,
                Foreground = DesignTokens.BrushTextSecondary,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(30, 0, 0, 0)
            };
            Grid.SetColumn(info, 0);
            f.Children.Add(info);

            TextBlock stat = new TextBlock
            {
                Text = statusInfo,
                FontSize = 10,
                FontWeight = FontWeights.Bold,
                Foreground = DesignTokens.BrushAccentGreen,
                VerticalAlignment = VerticalAlignment.Center,
                HorizontalAlignment = HorizontalAlignment.Right,
                Margin = new Thickness(0, 0, 30, 0)
            };
            Grid.SetColumn(stat, 1);
            f.Children.Add(stat);

            return f;
        }

        private void OverlayTimer_Tick(object sender, EventArgs e)
        {
            try
            {
                GameTelemetryEngine.UpdateMetrics();

                double fps = GameTelemetryEngine.CurrentFps;
                double avgFps = GameTelemetryEngine.AvgFps;
                double oneLow = GameTelemetryEngine.OnePercentLowFps;
                double frameTime = GameTelemetryEngine.CurrentFrameTimeMs;
                string target = GameTelemetryEngine.ActiveProcessName;

                int gpuT = (latestSensorData != null) ? latestSensorData.GpuTemp : 0;
                float gpuU = (latestSensorData != null) ? latestSensorData.GpuUsage : 0f;
                float gpuW = (latestSensorData != null) ? latestSensorData.GpuWatts : 0f;
                int cpuT = (latestSensorData != null) ? latestSensorData.CpuTemp : 0;
                float cpuU = (latestSensorData != null) ? latestSensorData.CpuUsage : 0f;
                float cpuW = (latestSensorData != null) ? latestSensorData.CpuWatts : 0f;
                double ramUsed = (latestSensorData != null) ? latestSensorData.UsedRamGb : 0.0;
                double ramTot = (latestSensorData != null) ? latestSensorData.TotalRamGb : 16.0;
                float ramPct = (latestSensorData != null) ? latestSensorData.RamUsage : 0f;

                if (overlayWindow != null && isOverlayActive)
                {
                    overlayWindow.UpdateMetrics(fps, avgFps, oneLow, gpuT, gpuU, gpuW, cpuT, cpuU, cpuW, ramUsed, ramPct, target);
                }

                if (viewGaming != null && viewGaming.Visibility == Visibility.Visible)
                {
                    if (lblHubFpsVal != null)
                    {
                        lblHubFpsVal.Text = (fps > 0) ? string.Format("{0:F0} FPS", fps) : "-- FPS";
                        lblHubFpsVal.Foreground = (fps >= 60)
                            ? new SolidColorBrush(Color.FromRgb(0, 255, 140))
                            : ((fps >= 30) ? new SolidColorBrush(Color.FromRgb(0, 210, 255)) : new SolidColorBrush(Color.FromRgb(255, 42, 85)));
                    }
                    if (lblHubOneLowVal != null)
                    {
                        lblHubOneLowVal.Text = (fps > 0) ? string.Format("AVG: {0:F0} | 1% LOW: {1:F0} FPS", avgFps > 0 ? avgFps : fps, oneLow) : "AVG: -- | 1% LOW: -- FPS";
                        lblHubOneLowVal.Foreground = (oneLow >= 45) ? new SolidColorBrush(Color.FromRgb(0, 229, 255)) : new SolidColorBrush(Color.FromRgb(255, 170, 0));
                    }
                    if (lblHubFrameTimeVal != null)
                    {
                        lblHubFrameTimeVal.Text = (frameTime > 0) ? string.Format("{0:F1} ms Delivery Latency", frameTime) : "-- ms Delivery Latency";
                    }
                    if (meterHubStability != null)
                    {
                        double stab = (fps > 0) ? Math.Min(100.0, (oneLow / fps) * 100.0) : 0.0;
                        meterHubStability.SetValue(stab);
                    }
                    if (lblHubGpuVal != null)
                    {
                        lblHubGpuVal.Text = string.Format("{0}°C | {1:F0}% | {2:F0}W", gpuT, gpuU, gpuW);
                    }
                    if (meterHubGpu != null) meterHubGpu.SetValue(gpuU);

                    if (lblHubCpuVal != null)
                    {
                        lblHubCpuVal.Text = string.Format("{0}°C | {1:F0}% | {2:F0}W", cpuT, cpuU, cpuW);
                    }
                    if (meterHubCpu != null) meterHubCpu.SetValue(cpuU);

                    if (lblHubRamVal != null)
                    {
                        lblHubRamVal.Text = string.Format("{0:F1} / {1:F1} GB ({2:F0}%)", ramUsed, ramTot, ramPct);
                    }
                    if (meterHubRam != null) meterHubRam.SetValue(ramPct);

                    if (lblHubTargetApp != null)
                    {
                        lblHubTargetApp.Text = string.Format("ACTIVE TARGET: {0}", target);
                    }
                    if (lblHubEngineStatus != null)
                    {
                        lblHubEngineStatus.Text = GameTelemetryEngine.IsEtwActive ? "⚡ ZERO-HOOK ETW DIRECTX ACTIVE" : "📊 DWM TELEMETRY ACTIVE";
                    }
                }
            }
            catch { }
        }

        private void Timer_Tick(object sender, EventArgs e)
        {
            try
            {
                HardwareBridge.SensorData d = HardwareBridge.ReadSensors(cpuCounter);

                // 1. Update 60-Second Turbo Heat Flush Countdown
                if (heatFlushRemaining > 0)
                {
                    heatFlushRemaining--;
                    if (lblHeatFlushText != null)
                    {
                        lblHeatFlushText.Text = string.Format("❄️ FLUSHING... ({0}s LEFT)", heatFlushRemaining);
                    }
                    if (heatFlushRemaining == 0)
                    {
                        HardwareBridge.SetFanMode(0);
                        currentFanMode = 0;
                        if (lblHeatFlushText != null)
                        {
                            lblHeatFlushText.Text = "❄️ ENGAGE TURBO FLUSH";
                        }
                        if (lblStatusMsg != null)
                        {
                            lblStatusMsg.Text = "DYNAMIC AUTO (HEAT FLUSH COMPLETE)";
                            lblStatusMsg.Foreground = new SolidColorBrush(Color.FromRgb(0, 225, 255));
                        }
                        UpdateDriveModeButton(btnAuto, true, Color.FromRgb(0, 144, 255));
                        UpdateDriveModeButton(btnMax, false, Color.FromRgb(255, 30, 86));
                        UpdateDriveModeButton(btnQuiet, false, Color.FromRgb(0, 229, 255));
                    }
                }

                // 2. Periodic Network Latency Ping (Every 5 Seconds)
                pingTick++;
                if (pingTick >= 5)
                {
                    pingTick = 0;
                    CheckPingAsync();
                }

                // 3. Update Dashboard Telemetry Rings & Control Center
                if (cpuSpeedo != null)
                {
                    cpuSpeedo.Update(d.CpuUsage, d.CpuTemp, d.CpuRpm, d.CpuDuty, d.CpuWatts);
                }
                if (gpuSpeedo != null)
                {
                    gpuSpeedo.Update(d.GpuUsage, d.GpuTemp, d.GpuRpm, d.GpuDuty, d.GpuWatts);
                }
                if (lblDiagPower != null)
                {
                    lblDiagPower.Text = d.IsAcPlugged ? "⚡ AC Power" : string.Format("🔋 Bat ({0}%)", d.BatteryPercent);
                }
                if (lblDiagBattery != null)
                {
                    lblDiagBattery.Text = (d.BatteryHealthPct > 0) ? string.Format("🛡️ {0:F0}% Health", d.BatteryHealthPct) : "🛡️ Battery OK";
                }
                if (lblDiagUptime != null)
                {
                    lblDiagUptime.Text = string.Format("⏱️ {0:h\\:mm} Uptime", d.SystemUptime);
                }

                // 3b. Update EC Anti-Hallucination Arbiter Engine
                if (currentFanMode != 1 && heatFlushRemaining == 0)
                {
                    bool isGaming = GameTelemetryEngine.IsGameActive || (d.GpuUsage >= 45.0f && d.GpuTemp >= 58);
                    EcAntiHallucinationEngine.Update(d.CpuTemp, d.GpuTemp, d.CpuUsage, d.GpuUsage, d.CpuWatts, isGaming);
                }
                UpdateDashboardEcGuardUi();

                if (viewEcGuard != null && viewEcGuard.Visibility == Visibility.Visible)
                {
                    UpdateEcGuardView(d);
                }

                // 4. Update Cooling View Blowers & Meters
                if (lblCoolingCpuRpm != null)
                {
                    lblCoolingCpuRpm.Text = string.Format("{0} RPM ({1}%)", d.CpuRpm, d.CpuDuty);
                    if (meterCoolingCpu != null) meterCoolingCpu.SetValue(d.CpuDuty);
                }
                if (lblCoolingGpuRpm != null)
                {
                    lblCoolingGpuRpm.Text = (d.GpuRpm > 0) ? string.Format("{0} RPM ({1}%)", d.GpuRpm, d.GpuDuty) : "0 RPM (PASSIVE IDLE)";
                    if (meterCoolingGpu != null) meterCoolingGpu.SetValue(d.GpuDuty);
                }

                // 5. Update Dynamic 4-Stage Hardware Fan Curve Active Stage
                int maxTemp = Math.Max(d.CpuTemp, d.GpuTemp);
                int activeStage = 0;
                if (maxTemp >= 85) activeStage = 3;
                else if (maxTemp >= 70) activeStage = 2;
                else if (maxTemp >= 50) activeStage = 1;
                else activeStage = 0;

                for (int i = 0; i < curveCards.Count; i++)
                {
                    bool isActive = (i == activeStage);
                    curveCards[i].BorderBrush = isActive ? new SolidColorBrush(Color.FromRgb(0, 229, 255)) : new SolidColorBrush(Color.FromArgb(70, 0, 160, 255));
                    curveCards[i].Background = isActive ? new SolidColorBrush(Color.FromArgb(200, 15, 35, 65)) : new SolidColorBrush(Color.FromArgb(160, 18, 25, 42));
                }

                latestSensorData = d;

                // Update RAM on Dashboard
                if (d.TotalRamGb > 0 && lblRamStats != null)
                {
                    lblRamStats.Text = string.Format("RAM: {0:F0}% ({1:F1} / {2:F1} GB)", d.RamUsage, d.UsedRamGb, d.TotalRamGb);
                }

                // Update System Health & Specs Telemetry View
                if (lblHealthUptime != null)
                {
                    lblHealthUptime.Text = string.Format("{0}d {1:D2}h {2:D2}m {3:D2}s",
                        d.SystemUptime.Days, d.SystemUptime.Hours, d.SystemUptime.Minutes, d.SystemUptime.Seconds);
                }

                // RTX 4050 Telemetry Card
                if (lblHealthVramText != null)
                {
                    lblHealthVramText.Text = string.Format("{0:F1} / {1:F1} GB ({2:F0}%)", d.VramUsedGb, d.VramTotalGb, d.VramUsage);
                    if (meterHealthVram != null) meterHealthVram.SetValue(d.VramUsage);
                }
                if (lblHealthGpuCore != null)
                {
                    lblHealthGpuCore.Text = string.Format("{0:N0}", d.GpuCoreClock);
                }
                if (lblHealthGpuMem != null)
                {
                    lblHealthGpuMem.Text = string.Format("{0:N0}", d.GpuMemClock);
                }
                if (lblHealthGpuThrottle != null)
                {
                    lblHealthGpuThrottle.Text = d.GpuThrottleStatus;
                    if (d.GpuThrottleStatus.Contains("THERMAL"))
                        lblHealthGpuThrottle.Foreground = new SolidColorBrush(Color.FromRgb(255, 60, 60));
                    else if (d.GpuThrottleStatus.Contains("POWER"))
                        lblHealthGpuThrottle.Foreground = new SolidColorBrush(Color.FromRgb(255, 170, 0));
                    else
                        lblHealthGpuThrottle.Foreground = new SolidColorBrush(Color.FromRgb(0, 255, 140));
                }
                if (lblHealthGpuHeadroom != null)
                {
                    int margin = Math.Max(0, 87 - d.GpuTemp);
                    lblHealthGpuHeadroom.Text = string.Format("+{0}°C Margin (to 87°C)", margin);
                }

                // Memory & Workloads Card
                if (lblHealthRamText != null)
                {
                    lblHealthRamText.Text = string.Format("{0:F1} / {1:F1} GB ({2:F0}%)", d.UsedRamGb, d.TotalRamGb, d.RamUsage);
                    if (meterHealthRam != null) meterHealthRam.SetValue(d.RamUsage);
                }

                List<Tuple<string, double>> topProcs = HardwareBridge.GetTopMemoryProcesses();
                if (lblTopProc1 != null && topProcs.Count > 0)
                {
                    lblTopProc1.Text = string.Format("{0} ({1:F0} MB)", topProcs[0].Item1, topProcs[0].Item2);
                }
                if (lblTopProc2 != null && topProcs.Count > 1)
                {
                    lblTopProc2.Text = string.Format("{0} ({1:F0} MB)", topProcs[1].Item1, topProcs[1].Item2);
                }
                if (lblTopProc3 != null && topProcs.Count > 2)
                {
                    lblTopProc3.Text = string.Format("{0} ({1:F0} MB)", topProcs[2].Item1, topProcs[2].Item2);
                }

                // Power & Storage Health Card
                if (lblHealthBatteryText != null)
                {
                    string pwr = d.IsAcPlugged ? "AC Mains Connected" : "Discharging (Battery)";
                    lblHealthBatteryText.Text = string.Format("{0}% ({1})", d.BatteryPercent, pwr);
                    if (meterHealthBattery != null) meterHealthBattery.SetValue(d.BatteryPercent);
                }
                if (lblHealthBatteryWear != null)
                {
                    lblHealthBatteryWear.Text = string.Format("Design: {0:F1} Wh | Full: {1:F1} Wh ({2:F1}% Wear)",
                        d.BatteryDesignMwh / 1000.0, d.BatteryFullMwh / 1000.0, Math.Max(0.0, 100.0 - d.BatteryHealthPct));
                }
                if (lblHealthCpuHeadroom != null)
                {
                    int margin = Math.Max(0, 100 - d.CpuTemp);
                    lblHealthCpuHeadroom.Text = string.Format("+{0}°C Margin (to 100°C TJMax)", margin);
                }

                if (lblHealthDiskText != null && meterHealthDisk != null)
                {
                    try
                    {
                        DriveInfo dInfo = new DriveInfo("C");
                        if (dInfo.IsReady)
                        {
                            double totalGb = dInfo.TotalSize / (1024.0 * 1024.0 * 1024.0);
                            double freeGb = dInfo.AvailableFreeSpace / (1024.0 * 1024.0 * 1024.0);
                            double usedPct = ((totalGb - freeGb) / totalGb) * 100.0;
                            lblHealthDiskText.Text = string.Format("{0:F1} GB Free of {1:F1} GB", freeGb, totalGb);
                            meterHealthDisk.SetValue(usedPct);
                        }
                    }
                    catch { }
                }

                // ACTIVE THERMAL WATCHDOG: Safeguard hardware against sustained excessive temperatures with auto-recovery
                bool isSustainedHighHeat = (d.CpuTemp >= 92 || d.GpuTemp >= 86);
                if (isSustainedHighHeat && (currentFanMode == 3 || (currentFanMode == 6 && currentCustomDuty < 80)))
                {
                    watchdogHighHeatCount++;
                    watchdogCoolCount = 0;
                    if (watchdogHighHeatCount >= 4 && !watchdogActive) // Must sustain 4+ consecutive seconds (prevents false triggers on browser spikes)
                    {
                        watchdogActive = true;
                        watchdogPreviousMode = currentFanMode;
                        HardwareBridge.SetFanMode(1);
                        currentFanMode = 1;
                        if (lblStatusMsg != null)
                        {
                            lblStatusMsg.Text = "⚠️ THERMAL WATCHDOG: HIGH HEAT (AUTO TURBO ENGAGED)";
                            lblStatusMsg.Foreground = new SolidColorBrush(Color.FromRgb(255, 60, 60));
                        }
                        UpdateDriveModeButton(btnMax, true, Color.FromRgb(255, 30, 86));
                        UpdateDriveModeButton(btnAuto, false, Color.FromRgb(0, 144, 255));
                        UpdateDriveModeButton(btnQuiet, false, Color.FromRgb(0, 229, 255));
                    }
                }
                else
                {
                    watchdogHighHeatCount = 0;
                    // Auto-recovery: Once hardware cools down safely (<= 75°C CPU, <= 70°C GPU) for 5 seconds, restore normal mode!
                    if (watchdogActive && d.CpuTemp <= 75 && d.GpuTemp <= 70)
                    {
                        watchdogCoolCount++;
                        if (watchdogCoolCount >= 5)
                        {
                            watchdogActive = false;
                            watchdogCoolCount = 0;
                            int restoreMode = (watchdogPreviousMode == 3) ? 3 : 0;
                            HardwareBridge.SetFanMode(restoreMode);
                            currentFanMode = restoreMode;
                            if (lblStatusMsg != null)
                            {
                                lblStatusMsg.Text = (restoreMode == 3) ? "QUIET (TEMPERATURE NORMALIZED)" : "DYNAMIC AUTO (TEMPERATURE NORMALIZED)";
                                lblStatusMsg.Foreground = new SolidColorBrush(Color.FromRgb(0, 225, 255));
                            }
                            UpdateDriveModeButton(btnAuto, restoreMode == 0, Color.FromRgb(0, 144, 255));
                            UpdateDriveModeButton(btnQuiet, restoreMode == 3, Color.FromRgb(0, 229, 255));
                            UpdateDriveModeButton(btnMax, false, Color.FromRgb(255, 30, 86));
                        }
                    }
                    else
                    {
                        watchdogCoolCount = 0;
                    }
                }
            }
            catch { }
        }

        private static System.Threading.Mutex singleMutex = null;

        [STAThread]
        public static void Main()
        {
            bool createdNew = true;
            try
            {
                singleMutex = new System.Threading.Mutex(true, "Local\\AspireSense_SingleInstance_App", out createdNew);
            }
            catch
            {
                createdNew = true;
            }

            if (!createdNew)
            {
                MessageBox.Show("AspireSense is already running.", "AspireSense", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            try
            {
                Application app = new Application();
                app.Run(new MainWindow());
            }
            finally
            {
                if (singleMutex != null)
                {
                    try { singleMutex.ReleaseMutex(); } catch { }
                    singleMutex.Close();
                }
            }
        }
    }
}
