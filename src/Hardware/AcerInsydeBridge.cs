using System;
using System.IO;
using System.Runtime.InteropServices;

namespace AspireSense
{
    // ==========================================
    // ACER DIRECT INSYDE H2O HARDWARE BRIDGE
    // Supports Acer Aspire, Nitro & Predator laptops
    // Native communication via Insyde EC buffer protocol (InsydeDCHU.dll)
    // ==========================================
    public static class AcerInsydeBridge
    {
        [DllImport("InsydeDCHU.dll", CallingConvention = CallingConvention.StdCall)]
        [DefaultDllImportSearchPaths(DllImportSearchPath.AssemblyDirectory)]
        public static extern int SetDCHU_Data(int command, byte[] buffer, int length);

        [DllImport("InsydeDCHU.dll", CallingConvention = CallingConvention.StdCall)]
        [DefaultDllImportSearchPaths(DllImportSearchPath.AssemblyDirectory)]
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
                    try { dir2 = Path.GetDirectoryName(System.Reflection.Assembly.GetExecutingAssembly().Location); } catch { }
                    isDllPresent = (File.Exists(Path.Combine(dir1, "InsydeDCHU.dll")) ||
                                    (!string.IsNullOrEmpty(dir2) && File.Exists(Path.Combine(dir2, "InsydeDCHU.dll"))));
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
}
