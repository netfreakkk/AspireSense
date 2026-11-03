using System;
using System.Management;
using System.Runtime.InteropServices;

namespace AspireSense
{
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
        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
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
        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
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
        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
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
}
