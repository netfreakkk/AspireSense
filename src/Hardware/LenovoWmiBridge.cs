using System;
using System.Management;

namespace AspireSense
{
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
}
