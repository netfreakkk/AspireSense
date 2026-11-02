using System;
using System.Management;

namespace AspireSense
{
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
}
