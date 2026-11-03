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
        public static bool IsEstimatedSensor = false;

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
                string powercfg = System.IO.Path.Combine(Environment.SystemDirectory, "powercfg.exe");
                PowerBoostTamed = !PowerBoostTamed;
                int targetState = PowerBoostTamed ? 99 : 100;
                string argsAc = string.Format("/setacvalueindex SCHEME_CURRENT SUB_PROCESSOR PROCTHROTTLEMAX {0}", targetState);
                string argsDc = string.Format("/setdcvalueindex SCHEME_CURRENT SUB_PROCESSOR PROCTHROTTLEMAX {0}", targetState);

                ProcessStartInfo psi1 = new ProcessStartInfo(powercfg, argsAc) { CreateNoWindow = true, UseShellExecute = false };
                using (Process p1 = Process.Start(psi1))
                {
                    if (p1 != null) p1.WaitForExit(1000);
                }

                ProcessStartInfo psi2 = new ProcessStartInfo(powercfg, argsDc) { CreateNoWindow = true, UseShellExecute = false };
                using (Process p2 = Process.Start(psi2))
                {
                    if (p2 != null) p2.WaitForExit(1000);
                }

                ProcessStartInfo psiAct = new ProcessStartInfo(powercfg, "/setactive SCHEME_CURRENT") { CreateNoWindow = true, UseShellExecute = false };
                using (Process pAct = Process.Start(psiAct))
                {
                    if (pAct != null) pAct.WaitForExit(1000);
                }

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
                string powercfg = System.IO.Path.Combine(Environment.SystemDirectory, "powercfg.exe");
                ProcessStartInfo psi = new ProcessStartInfo(powercfg, "/query SCHEME_CURRENT SUB_PROCESSOR PROCTHROTTLEMAX")
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
}
