using System;
using System.Management;

namespace AspireSense
{
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
}
