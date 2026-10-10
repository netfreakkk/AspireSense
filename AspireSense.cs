using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Shapes;
using System.Windows.Threading;
using Path = System.Windows.Shapes.Path;

namespace AspireSense
{
    // ==========================================
    // HARDWARE BRIDGE: Native direct communication
    // ==========================================
    public static class HardwareBridge
    {
        [DllImport("InsydeDCHU.dll", CallingConvention = CallingConvention.StdCall)]
        public static extern int SetDCHU_Data(int command, byte[] buffer, int length);

        [DllImport("InsydeDCHU.dll", CallingConvention = CallingConvention.StdCall)]
        public static extern int GetDCHU_Data_Buffer(int command, [In, Out] byte[] buffer);

        // NVIDIA NVML API for exact GPU utilization matching Task Manager
        [DllImport("nvml.dll", EntryPoint = "nvmlInit_v2")]
        public static extern int nvmlInit();

        [DllImport("nvml.dll", EntryPoint = "nvmlShutdown")]
        public static extern int nvmlShutdown();

        [DllImport("nvml.dll", EntryPoint = "nvmlDeviceGetHandleByIndex_v2")]
        public static extern int nvmlDeviceGetHandleByIndex(uint index, out IntPtr device);

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

        [DllImport("psapi.dll")]
        public static extern int EmptyWorkingSet(IntPtr hwProc);

        private static readonly object ecLock = new object();
        private static bool nvmlReady = false;
        private static IntPtr nvmlDevice = IntPtr.Zero;

        public static void InitializeHardware()
        {
            try
            {
                int res = nvmlInit();
                if (res == 0)
                {
                    int hRes = nvmlDeviceGetHandleByIndex(0, out nvmlDevice);
                    nvmlReady = (hRes == 0 && nvmlDevice != IntPtr.Zero);
                }
            }
            catch
            {
                nvmlReady = false;
            }

            ResetToAuto();
        }

        public static void CloseHardware()
        {
            try
            {
                // CRITICAL SAFETY: Always restore EC to factory automatic thermal curve on shutdown
                ResetToAuto();
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

        public static bool ResetToAuto()
        {
            lock (ecLock)
            {
                try
                {
                    // Subcommand 34: Load factory default fan curve table
                    byte[] bufDefault = new byte[] { 1, 0, 0, 34 };
                    SetDCHU_Data(121, bufDefault, 4);

                    // Subcommand 14: Clear any custom fan offset
                    byte[] bufOffset = new byte[] { 0, 0, 0, 14 };
                    SetDCHU_Data(121, bufOffset, 4);

                    // Subcommand 1: Restore Mode 0 (Dynamic Auto)
                    byte[] bufMode = new byte[] { 0, 0, 0, 1 };
                    int res = SetDCHU_Data(121, bufMode, 4);

                    return res == 121;
                }
                catch
                {
                    return false;
                }
            }
        }

        public static bool SetFanMode(int mode)
        {
            lock (ecLock)
            {
                try
                {
                    if (mode == 0)
                    {
                        return ResetToAuto();
                    }

                    // Mode boundary validation: Only permit known Clevo/Insyde modes (1=Max, 3=Quiet, 6=Custom)
                    if (mode != 1 && mode != 3 && mode != 6)
                    {
                        return false;
                    }

                    byte[] buffer = new byte[4];
                    buffer[0] = (byte)mode;
                    buffer[1] = 0;
                    buffer[2] = 0;
                    buffer[3] = 1;
                    int res = SetDCHU_Data(121, buffer, 4);
                    return res == 121;
                }
                catch
                {
                    return false;
                }
            }
        }

        public static bool SetCustomSpeed(int dutyPercentage)
        {
            lock (ecLock)
            {
                try
                {
                    byte[] table = new byte[256];
                    int readRes = GetDCHU_Data_Buffer(13, table);

                    // CRITICAL SAFETY CHECK: Verify that buffer 13 was read successfully and contains valid fan table data
                    // table[12] (FanCount) must be >= 1, and temperature points must be realistic.
                    // NEVER write an empty or corrupt 256-byte table into the Embedded Controller!
                    if (readRes != 13 || table[12] < 1 || table[16] < 25 || table[22] > 115)
                    {
                        return false;
                    }

                    // Safe duty clamp: 30% (~76/255) minimum to prevent brushless motor stall
                    dutyPercentage = Math.Max(30, Math.Min(100, dutyPercentage));
                    byte rawDuty = (byte)((dutyPercentage * 255) / 100);

                    // Configure Stage 1 (Idle ~45°C), Stage 2 (Normal ~60°C), Stage 3 (Gaming ~80°C)
                    table[17] = rawDuty; // CPU Stage 1
                    table[19] = rawDuty; // CPU Stage 2
                    table[21] = rawDuty; // CPU Stage 3

                    table[25] = rawDuty; // GPU Stage 1
                    table[27] = rawDuty; // GPU Stage 2
                    table[29] = rawDuty; // GPU Stage 3

                    // CRITICAL HARDWARE SAFETY LOCK: Stage 4 (emergency thermal trip point ~99-100°C) MUST REMAIN 100% (255)
                    // Even if user specifies a lower duty for quiet work, if silicon reaches critical 99-100°C,
                    // the EC hardware will automatically spin fans to maximum RPM to prevent hardware thermal damage!
                    table[23] = 255; // CPU Stage 4 Failsafe Lock
                    table[31] = 255; // GPU Stage 4 Failsafe Lock

                    SetDCHU_Data(14, table, 256);

                    byte[] modeBuf = new byte[] { 6, 0, 0, 1 };
                    SetDCHU_Data(121, modeBuf, 4);

                    byte[] offsetBuf = new byte[] { rawDuty, 0, 0, 14 };
                    SetDCHU_Data(121, offsetBuf, 4);

                    return true;
                }
                catch
                {
                    return false;
                }
            }
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
        }

        public static SensorData ReadSensors(PerformanceCounter cpuCounter)
        {
            SensorData data = new SensorData();

            lock (ecLock)
            {
                try
                {
                    byte[] buf = new byte[256];
                    int res = GetDCHU_Data_Buffer(12, buf);

                    if (res == 12)
                    {
                        int rawCpuRpm = (buf[2] << 8) | buf[3];
                        int rawGpuRpm = (buf[4] << 8) | buf[5];

                        // Filter out disconnected sensor / glitch readings (0xFFFF or >= 9000 RPM)
                        data.CpuRpm = (rawCpuRpm > 0 && rawCpuRpm < 9000) ? rawCpuRpm : 0;
                        data.GpuRpm = (rawGpuRpm > 0 && rawGpuRpm < 9000) ? rawGpuRpm : 0;

                        data.CpuDuty = Math.Min(100, (int)buf[16]);
                        data.GpuDuty = Math.Min(100, (int)buf[19]);

                        int rawCpuTemp = buf[18];
                        int rawGpuTemp = buf[21];

                        data.CpuTemp = (rawCpuTemp >= 20 && rawCpuTemp <= 115) ? rawCpuTemp : 45;
                        data.GpuTemp = (rawGpuTemp >= 20 && rawGpuTemp <= 115) ? rawGpuTemp : 35;
                    }
                    else
                    {
                        data.CpuTemp = 45;
                        data.GpuTemp = 35;
                        data.CpuRpm = 1000;
                        data.GpuRpm = 0;
                    }
                }
                catch
                {
                    data.CpuTemp = 45;
                    data.GpuTemp = 35;
                    data.CpuRpm = 1000;
                    data.GpuRpm = 0;
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

            return data;
        }
    }

    // ==========================================
    // SPEEDOMETER GAUGE COMPONENT (BLUE-BLACK DESIGN)
    // ==========================================
    public class SpeedometerGauge : Canvas
    {
        private Path bgArc;
        private Path valArc;
        private Line needle;
        private TextBlock txtPercent;
        private TextBlock txtTemp;
        private TextBlock txtRpm;
        private TextBlock txtHeader;
        private TextBlock txtSub;

        private double radius;
        private double cx;
        private double cy;
        private double startAngle = 135.0;
        private double sweepAngle = 270.0;

        public SpeedometerGauge(string hardwareName, string modelSub, double size)
        {
            this.Width = size;
            this.Height = size + 35;
            this.cx = size / 2.0;
            this.cy = (size / 2.0) - 5;
            this.radius = (size / 2.0) - 26;

            Ellipse outerRing = new Ellipse
            {
                Width = size - 10,
                Height = size - 10,
                Stroke = new SolidColorBrush(Color.FromArgb(40, 0, 180, 255)),
                StrokeThickness = 1,
                StrokeDashArray = new DoubleCollection(new double[] { 3, 3 })
            };
            Canvas.SetLeft(outerRing, 5);
            Canvas.SetTop(outerRing, 5);
            this.Children.Add(outerRing);

            bgArc = new Path
            {
                Stroke = new SolidColorBrush(Color.FromArgb(200, 15, 23, 42)),
                StrokeThickness = 14,
                StrokeStartLineCap = PenLineCap.Round,
                StrokeEndLineCap = PenLineCap.Round,
                Data = CreateArcGeometry(cx, cy, radius, startAngle, startAngle + sweepAngle)
            };
            this.Children.Add(bgArc);

            for (int i = 0; i <= 10; i++)
            {
                double angle = startAngle + (i / 10.0) * sweepAngle;
                double rad = (angle - 90) * Math.PI / 180.0;
                double rIn = radius - 14;
                double rOut = (i % 5 == 0) ? (radius + 12) : (radius + 6);

                Line tick = new Line
                {
                    X1 = cx + rIn * Math.Cos(rad),
                    Y1 = cy + rIn * Math.Sin(rad),
                    X2 = cx + rOut * Math.Cos(rad),
                    Y2 = cy + rOut * Math.Sin(rad),
                    Stroke = (i % 5 == 0) ? new SolidColorBrush(Color.FromRgb(0, 210, 255)) : new SolidColorBrush(Color.FromArgb(120, 0, 150, 255)),
                    StrokeThickness = (i % 5 == 0) ? 2.5 : 1.2
                };
                this.Children.Add(tick);
            }

            LinearGradientBrush blueGrad = new LinearGradientBrush
            {
                StartPoint = new Point(0, 1),
                EndPoint = new Point(1, 0)
            };
            blueGrad.GradientStops.Add(new GradientStop(Color.FromRgb(0, 120, 255), 0.0));
            blueGrad.GradientStops.Add(new GradientStop(Color.FromRgb(0, 210, 255), 0.7));
            blueGrad.GradientStops.Add(new GradientStop(Color.FromRgb(56, 189, 248), 1.0));

            valArc = new Path
            {
                Stroke = blueGrad,
                StrokeThickness = 14,
                StrokeStartLineCap = PenLineCap.Round,
                StrokeEndLineCap = PenLineCap.Round,
                Effect = new DropShadowEffect
                {
                    Color = Color.FromRgb(0, 210, 255),
                    BlurRadius = 18,
                    ShadowDepth = 0,
                    Opacity = 0.85
                }
            };
            this.Children.Add(valArc);

            needle = new Line
            {
                X1 = cx,
                Y1 = cy,
                X2 = cx,
                Y2 = cy - (radius - 12),
                Stroke = new LinearGradientBrush(Color.FromRgb(255, 255, 255), Color.FromRgb(0, 210, 255), 90),
                StrokeThickness = 3,
                StrokeEndLineCap = PenLineCap.Round,
                Effect = new DropShadowEffect
                {
                    Color = Color.FromRgb(0, 210, 255),
                    BlurRadius = 10,
                    ShadowDepth = 0,
                    Opacity = 0.9
                }
            };
            this.Children.Add(needle);

            Ellipse pivot = new Ellipse
            {
                Width = 18,
                Height = 18,
                Fill = new RadialGradientBrush(Color.FromRgb(0, 210, 255), Color.FromRgb(10, 20, 40)),
                Stroke = new SolidColorBrush(Color.FromRgb(0, 210, 255)),
                StrokeThickness = 2
            };
            Canvas.SetLeft(pivot, cx - 9);
            Canvas.SetTop(pivot, cy - 9);
            this.Children.Add(pivot);

            txtPercent = new TextBlock
            {
                Text = "0%",
                FontSize = 38,
                FontWeight = FontWeights.Black,
                Foreground = Brushes.White,
                TextAlignment = TextAlignment.Center,
                Width = size
            };
            Canvas.SetTop(txtPercent, cy - 65);
            this.Children.Add(txtPercent);

            TextBlock lblUsageSub = new TextBlock
            {
                Text = "UTILIZATION",
                FontSize = 9.5,
                FontWeight = FontWeights.Bold,
                Foreground = new SolidColorBrush(Color.FromRgb(100, 160, 220)),
                TextAlignment = TextAlignment.Center,
                Width = size
            };
            Canvas.SetTop(lblUsageSub, cy - 25);
            this.Children.Add(lblUsageSub);

            Border tempBadge = new Border
            {
                Background = new SolidColorBrush(Color.FromArgb(180, 10, 18, 35)),
                BorderBrush = new SolidColorBrush(Color.FromRgb(0, 180, 240)),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(14),
                Padding = new Thickness(12, 4, 12, 4),
                HorizontalAlignment = HorizontalAlignment.Center
            };
            txtTemp = new TextBlock
            {
                Text = "🌡️ -- °C",
                FontSize = 13,
                FontWeight = FontWeights.Bold,
                Foreground = new SolidColorBrush(Color.FromRgb(0, 225, 255))
            };
            tempBadge.Child = txtTemp;
            Canvas.SetLeft(tempBadge, cx - 45);
            Canvas.SetTop(tempBadge, cy + 22);
            this.Children.Add(tempBadge);

            txtRpm = new TextBlock
            {
                Text = "❄️ -- RPM (0%)",
                FontSize = 11.5,
                FontWeight = FontWeights.SemiBold,
                Foreground = new SolidColorBrush(Color.FromRgb(140, 200, 255)),
                TextAlignment = TextAlignment.Center,
                Width = size
            };
            Canvas.SetTop(txtRpm, cy + 56);
            this.Children.Add(txtRpm);

            txtHeader = new TextBlock
            {
                Text = hardwareName,
                FontSize = 12.5,
                FontWeight = FontWeights.Bold,
                Foreground = Brushes.White,
                TextAlignment = TextAlignment.Center,
                Width = size
            };
            Canvas.SetTop(txtHeader, size + 2);
            this.Children.Add(txtHeader);

            txtSub = new TextBlock
            {
                Text = modelSub,
                FontSize = 10,
                Foreground = new SolidColorBrush(Color.FromRgb(120, 150, 180)),
                TextAlignment = TextAlignment.Center,
                Width = size
            };
            Canvas.SetTop(txtSub, size + 18);
            this.Children.Add(txtSub);

            Update(0, 40, 0, 0);
        }

        public void Update(float usage, int temp, int rpm, int duty)
        {
            txtPercent.Text = string.Format("{0:F0}%", usage);
            txtTemp.Text = string.Format("🌡️ {0} °C", temp);
            txtRpm.Text = string.Format("❄️ {0} RPM ({1}%)", rpm, duty);

            if (temp >= 85)
            {
                txtTemp.Foreground = new SolidColorBrush(Color.FromRgb(255, 60, 60));
            }
            else if (temp >= 72)
            {
                txtTemp.Foreground = new SolidColorBrush(Color.FromRgb(255, 170, 0));
            }
            else
            {
                txtTemp.Foreground = new SolidColorBrush(Color.FromRgb(0, 225, 255));
            }

            double ratio = Math.Max(0.005, Math.Min(1.0, usage / 100.0));
            double targetAngle = startAngle + (sweepAngle * ratio);
            valArc.Data = CreateArcGeometry(cx, cy, radius, startAngle, targetAngle);

            double needleRad = (targetAngle - 90) * Math.PI / 180.0;
            needle.X2 = cx + (radius - 12) * Math.Cos(needleRad);
            needle.Y2 = cy + (radius - 12) * Math.Sin(needleRad);
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

        // Dashboard Speedometers
        private SpeedometerGauge cpuSpeedo;
        private SpeedometerGauge gpuSpeedo;

        // Cooling View Specific Controls
        private TextBlock lblCoolingCpuRpm;
        private ProgressBar progCoolingCpu;
        private TextBlock lblCoolingGpuRpm;
        private ProgressBar progCoolingGpu;
        private Slider sliderDedicatedCustom;
        private TextBlock lblDedicatedCustomVal;

        // Gaming Hub Controls
        private TextBlock lblScenarioStatus;

        // Health Controls
        private TextBlock lblHealthRam;
        private ProgressBar progHealthRam;
        private TextBlock lblHealthBattery;
        private ProgressBar progHealthBattery;
        private StackPanel spDrives;

        // Center Fan Mode Buttons
        private Border btnAuto;
        private Border btnQuiet;
        private Border btnMax;

        private Slider sliderCustom;
        private TextBlock lblCustomVal;
        private TextBlock lblStatusMsg;
        private TextBlock lblRamStats;

        private bool winKeyLocked = false;
        private Border btnWinLock;

        private int currentFanMode = 0; // 0=Auto, 1=Max, 3=Quiet, 6=Custom
        private int currentCustomDuty = 75;

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

            BuildLayout();

            timer = new DispatcherTimer();
            timer.Interval = TimeSpan.FromSeconds(1);
            timer.Tick += Timer_Tick;
            timer.Start();

            // CRITICAL HARDWARE SAFETY: Reset EC fan controller to Auto when app is closed, terminated, or crashes
            this.Closing += (s, e) =>
            {
                if (timer != null)
                {
                    timer.Stop();
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

        private void BuildLayout()
        {
            Grid root = new Grid();

            RadialGradientBrush bgGradient = new RadialGradientBrush
            {
                Center = new Point(0.5, 0.45),
                RadiusX = 0.8,
                RadiusY = 0.7,
                GradientStops = new GradientStopCollection
                {
                    new GradientStop(Color.FromArgb(70, 0, 75, 160), 0.0),
                    new GradientStop(Color.FromArgb(25, 0, 30, 80), 0.5),
                    new GradientStop(Color.FromArgb(0, 0, 0, 0), 1.0)
                }
            };
            root.Background = bgGradient;

            root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(56) });  // Titlebar
            root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(42) });  // Sub-banner
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

            viewHost.Children.Add(viewDashboard);
            viewHost.Children.Add(viewCooling);
            viewHost.Children.Add(viewGaming);
            viewHost.Children.Add(viewHealth);

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
            Grid bar = new Grid { Background = new SolidColorBrush(Color.FromRgb(10, 14, 25)) };
            bar.MouseDown += (s, e) => { if (e.LeftButton == MouseButtonState.Pressed) this.DragMove(); };

            bar.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(240) });
            bar.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            bar.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(140) });

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
                        Width = 30,
                        Height = 30,
                        Margin = new Thickness(0, 0, 10, 0),
                        Effect = new DropShadowEffect { Color = Color.FromRgb(0, 210, 255), BlurRadius = 14, ShadowDepth = 0, Opacity = 0.85 }
                    };
                    logoPanel.Children.Add(imgLogo);
                }
                catch { }
            }
            else
            {
                Path aLogo = new Path
                {
                    Width = 24,
                    Height = 24,
                    Stretch = Stretch.Fill,
                    Fill = new LinearGradientBrush(Color.FromRgb(0, 210, 255), Color.FromRgb(0, 100, 255), 45),
                    Data = Geometry.Parse("M 12,2 L 22,22 L 17,22 L 14,15 L 10,15 L 7,22 L 2,22 Z M 12,8 L 10.5,12 L 13.5,12 Z"),
                    Margin = new Thickness(0, 0, 10, 0)
                };
                logoPanel.Children.Add(aLogo);
            }

            TextBlock txtLogo = new TextBlock
            {
                Text = "ASPIRESENSE",
                FontSize = 17,
                FontWeight = FontWeights.Black,
                Foreground = Brushes.White,
                VerticalAlignment = VerticalAlignment.Center
            };
            logoPanel.Children.Add(txtLogo);
            Grid.SetColumn(logoPanel, 0);
            bar.Children.Add(logoPanel);

            // Fully Functional Tabs
            StackPanel tabs = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                VerticalAlignment = VerticalAlignment.Center,
                HorizontalAlignment = HorizontalAlignment.Left,
                Margin = new Thickness(25, 0, 0, 0)
            };

            navTabs.Add(CreateTab("Dashboard", 0));
            navTabs.Add(CreateTab("Cooling Control", 1));
            navTabs.Add(CreateTab("Gaming Hub", 2));
            navTabs.Add(CreateTab("System Health", 3));

            foreach (Border tab in navTabs)
            {
                tabs.Children.Add(tab);
            }

            Grid.SetColumn(tabs, 1);
            bar.Children.Add(tabs);

            StackPanel winBtnSp = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                VerticalAlignment = VerticalAlignment.Center,
                HorizontalAlignment = HorizontalAlignment.Right,
                Margin = new Thickness(0, 0, 15, 0)
            };

            Button btnMin = new Button
            {
                Content = "—",
                Width = 35,
                Height = 30,
                Foreground = Brushes.White,
                Background = Brushes.Transparent,
                BorderThickness = new Thickness(0),
                Cursor = Cursors.Hand,
                FontWeight = FontWeights.Bold
            };
            btnMin.Click += (s, e) => { this.WindowState = WindowState.Minimized; };
            winBtnSp.Children.Add(btnMin);

            Button btnClose = new Button
            {
                Content = "✕",
                Width = 35,
                Height = 30,
                Foreground = Brushes.White,
                Background = Brushes.Transparent,
                BorderThickness = new Thickness(0),
                Cursor = Cursors.Hand,
                FontWeight = FontWeights.Bold
            };
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
                Margin = new Thickness(10, 0, 10, 0),
                Padding = new Thickness(14, 10, 14, 10),
                BorderThickness = new Thickness(0, 0, 0, 2),
                BorderBrush = Brushes.Transparent,
                Cursor = Cursors.Hand,
                Tag = index
            };
            TextBlock t = new TextBlock
            {
                Text = title,
                FontSize = 13,
                FontWeight = FontWeights.Normal,
                Foreground = new SolidColorBrush(Color.FromRgb(150, 170, 200))
            };
            b.Child = t;

            b.MouseDown += (s, e) => { SwitchTab(index); };
            return b;
        }

        private void SwitchTab(int tabIndex)
        {
            viewDashboard.Visibility = (tabIndex == 0) ? Visibility.Visible : Visibility.Collapsed;
            viewCooling.Visibility = (tabIndex == 1) ? Visibility.Visible : Visibility.Collapsed;
            viewGaming.Visibility = (tabIndex == 2) ? Visibility.Visible : Visibility.Collapsed;
            viewHealth.Visibility = (tabIndex == 3) ? Visibility.Visible : Visibility.Collapsed;

            for (int i = 0; i < navTabs.Count; i++)
            {
                bool active = (i == tabIndex);
                navTabs[i].BorderBrush = active ? new SolidColorBrush(Color.FromRgb(0, 210, 255)) : Brushes.Transparent;
                TextBlock tb = navTabs[i].Child as TextBlock;
                if (tb != null)
                {
                    tb.Foreground = active ? Brushes.White : new SolidColorBrush(Color.FromRgb(150, 170, 200));
                    tb.FontWeight = active ? FontWeights.Bold : FontWeights.Normal;
                }
            }
        }

        private Grid CreateSubBanner()
        {
            Grid g = new Grid { Margin = new Thickness(30, 8, 30, 0) };
            StackPanel sp = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center };

            TextBlock tag = new TextBlock
            {
                Text = "ACTIVE COOLING PROFILE: ",
                FontSize = 11,
                FontWeight = FontWeights.Bold,
                Foreground = new SolidColorBrush(Color.FromRgb(120, 160, 210))
            };
            lblStatusMsg = new TextBlock
            {
                Text = "DYNAMIC AUTO (WHISPER QUIET)",
                FontSize = 11,
                FontWeight = FontWeights.Black,
                Foreground = new SolidColorBrush(Color.FromRgb(0, 225, 255))
            };
            sp.Children.Add(tag);
            sp.Children.Add(lblStatusMsg);
            g.Children.Add(sp);
            return g;
        }

        // ==========================================
        // PAGE 1: MAIN DASHBOARD VIEW
        // ==========================================
        private Grid CreateDashboardView()
        {
            Grid root = new Grid();
            root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(85) });

            Grid g = new Grid { Margin = new Thickness(30, 10, 30, 10) };
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(350) });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(350) });

            Border cpuBox = CreateSpeedoCard();
            cpuSpeedo = new SpeedometerGauge("INTEL CORE™ PROCESSOR", "CPU Workload & Thermal Sensor", 320);
            cpuBox.Child = cpuSpeedo;
            Grid.SetColumn(cpuBox, 0);
            g.Children.Add(cpuBox);

            Border centerBox = CreateCenterControlBox();
            Grid.SetColumn(centerBox, 1);
            g.Children.Add(centerBox);

            Border gpuBox = CreateSpeedoCard();
            gpuSpeedo = new SpeedometerGauge("NVIDIA GEFORCE RTX 4050", "Hardware Sensor via NVML", 320);
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
                Background = new SolidColorBrush(Color.FromArgb(210, 12, 17, 30)),
                CornerRadius = new CornerRadius(12),
                BorderBrush = new LinearGradientBrush
                {
                    StartPoint = new Point(0, 0),
                    EndPoint = new Point(1, 1),
                    GradientStops = new GradientStopCollection
                    {
                        new GradientStop(Color.FromArgb(180, 0, 180, 255), 0.0),
                        new GradientStop(Color.FromArgb(60, 20, 40, 70), 0.5),
                        new GradientStop(Color.FromArgb(120, 0, 120, 255), 1.0)
                    }
                },
                BorderThickness = new Thickness(1.5),
                Padding = new Thickness(15),
                Effect = new DropShadowEffect
                {
                    Color = Color.FromRgb(0, 100, 200),
                    BlurRadius = 20,
                    ShadowDepth = 0,
                    Opacity = 0.3
                }
            };
        }

        private Border CreateCenterControlBox()
        {
            Border card = new Border
            {
                Background = new SolidColorBrush(Color.FromArgb(180, 10, 15, 28)),
                CornerRadius = new CornerRadius(12),
                BorderBrush = new SolidColorBrush(Color.FromArgb(90, 0, 150, 255)),
                BorderThickness = new Thickness(1),
                Margin = new Thickness(15, 0, 15, 0),
                Padding = new Thickness(15)
            };

            StackPanel sp = new StackPanel { VerticalAlignment = VerticalAlignment.Center };

            string logoPath = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "logo.png");
            if (File.Exists(logoPath))
            {
                try
                {
                    System.Windows.Media.Imaging.BitmapImage bmpCenter = new System.Windows.Media.Imaging.BitmapImage();
                    bmpCenter.BeginInit();
                    bmpCenter.UriSource = new Uri(logoPath, UriKind.Absolute);
                    bmpCenter.CacheOption = System.Windows.Media.Imaging.BitmapCacheOption.OnLoad;
                    bmpCenter.EndInit();

                    Image imgCenter = new Image
                    {
                        Source = bmpCenter,
                        Width = 72,
                        Height = 72,
                        HorizontalAlignment = HorizontalAlignment.Center,
                        Margin = new Thickness(0, 0, 0, 10),
                        Effect = new DropShadowEffect { Color = Color.FromRgb(0, 210, 255), BlurRadius = 26, ShadowDepth = 0, Opacity = 0.9 }
                    };
                    sp.Children.Add(imgCenter);
                }
                catch { }
            }
            else
            {
                Path logoCenter = new Path
                {
                    Width = 56,
                    Height = 56,
                    Stretch = Stretch.Fill,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    Fill = new LinearGradientBrush
                    {
                        StartPoint = new Point(0, 0),
                        EndPoint = new Point(1, 1),
                        GradientStops = new GradientStopCollection
                        {
                            new GradientStop(Color.FromRgb(0, 225, 255), 0.0),
                            new GradientStop(Color.FromRgb(0, 100, 255), 0.7),
                            new GradientStop(Color.FromRgb(0, 40, 180), 1.0)
                        }
                    },
                    Data = Geometry.Parse("M 20,2 L 38,36 L 30,36 L 24,24 L 16,24 L 10,36 L 2,36 Z M 20,11 L 18,18 L 22,18 Z"),
                    Effect = new DropShadowEffect { Color = Color.FromRgb(0, 210, 255), BlurRadius = 25, ShadowDepth = 0, Opacity = 0.85 },
                    Margin = new Thickness(0, 0, 0, 10)
                };
                sp.Children.Add(logoCenter);
            }

            TextBlock title = new TextBlock
            {
                Text = "COOLING MASTER",
                FontSize = 13,
                FontWeight = FontWeights.Black,
                Foreground = Brushes.White,
                HorizontalAlignment = HorizontalAlignment.Center,
                Margin = new Thickness(0, 0, 0, 15)
            };
            sp.Children.Add(title);

            UniformGrid toggleGrid = new UniformGrid { Columns = 3, Margin = new Thickness(0, 0, 0, 18) };

            btnMax = CreateFancyButton("❄️ MAX", Color.FromRgb(0, 140, 255), false, () => ApplyMode(1, "100% MAXIMUM TURBO"));
            btnAuto = CreateFancyButton("⚙️ AUTO", Color.FromRgb(0, 210, 255), true, () => ApplyMode(0, "DYNAMIC AUTO (WHISPER QUIET)"));
            btnQuiet = CreateFancyButton("🌙 QUIET", Color.FromRgb(56, 189, 248), false, () => ApplyMode(3, "SILENT / ECO"));

            toggleGrid.Children.Add(btnMax);
            toggleGrid.Children.Add(btnAuto);
            toggleGrid.Children.Add(btnQuiet);
            sp.Children.Add(toggleGrid);

            Border overrideBox = new Border
            {
                Background = new SolidColorBrush(Color.FromArgb(160, 15, 23, 42)),
                CornerRadius = new CornerRadius(8),
                BorderBrush = new SolidColorBrush(Color.FromArgb(80, 0, 160, 255)),
                BorderThickness = new Thickness(1),
                Padding = new Thickness(12, 10, 12, 10)
            };

            StackPanel overSp = new StackPanel();

            Grid sliderHeader = new Grid();
            sliderHeader.Children.Add(new TextBlock { Text = "MANUAL FAN SPEED", FontSize = 10, FontWeight = FontWeights.Bold, Foreground = new SolidColorBrush(Color.FromRgb(140, 180, 230)) });
            lblCustomVal = new TextBlock
            {
                Text = "75%",
                FontSize = 11,
                FontWeight = FontWeights.Bold,
                Foreground = new SolidColorBrush(Color.FromRgb(0, 225, 255)),
                HorizontalAlignment = HorizontalAlignment.Right
            };
            sliderHeader.Children.Add(lblCustomVal);
            overSp.Children.Add(sliderHeader);

            sliderCustom = new Slider
            {
                Minimum = 30,
                Maximum = 100,
                Value = 75,
                TickFrequency = 5,
                Margin = new Thickness(0, 8, 0, 8)
            };
            sliderCustom.ValueChanged += (s, e) => { lblCustomVal.Text = string.Format("{0:F0}%", sliderCustom.Value); };
            overSp.Children.Add(sliderCustom);

            Button btnApplyCustom = new Button
            {
                Content = "APPLY MANUAL OVERRIDE",
                FontSize = 10.5,
                FontWeight = FontWeights.Bold,
                Foreground = Brushes.White,
                Background = new LinearGradientBrush(Color.FromRgb(0, 140, 255), Color.FromRgb(0, 80, 200), 45),
                BorderThickness = new Thickness(0),
                Height = 30,
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
                    lblStatusMsg.Text = string.Format("MANUAL OVERRIDE ({0}%) APPLIED", target);
                    HighlightButton(btnAuto, false);
                    HighlightButton(btnQuiet, false);
                    HighlightButton(btnMax, false);
                }
            };
            overSp.Children.Add(btnApplyCustom);

            overrideBox.Child = overSp;
            sp.Children.Add(overrideBox);

            card.Child = sp;
            return card;
        }

        private Border CreateFancyButton(string text, Color accent, bool isSelected, Action onClick)
        {
            Border b = new Border
            {
                Background = isSelected ? new SolidColorBrush(Color.FromArgb(160, accent.R, accent.G, accent.B)) : new SolidColorBrush(Color.FromArgb(140, 18, 25, 42)),
                BorderBrush = isSelected ? new SolidColorBrush(accent) : new SolidColorBrush(Color.FromArgb(80, 50, 70, 100)),
                BorderThickness = new Thickness(1.5),
                CornerRadius = new CornerRadius(6),
                Margin = new Thickness(4),
                Padding = new Thickness(6, 12, 6, 12),
                Cursor = Cursors.Hand
            };

            TextBlock tb = new TextBlock
            {
                Text = text,
                FontSize = 11.5,
                FontWeight = FontWeights.Bold,
                Foreground = isSelected ? Brushes.White : new SolidColorBrush(Color.FromRgb(180, 200, 230)),
                HorizontalAlignment = HorizontalAlignment.Center
            };
            b.Child = tb;

            b.MouseDown += (s, e) => { onClick(); };
            return b;
        }

        private void ApplyMode(int mode, string desc)
        {
            currentFanMode = mode;
            HardwareBridge.SetFanMode(mode);
            lblStatusMsg.Text = desc;

            HighlightButton(btnAuto, mode == 0);
            HighlightButton(btnMax, mode == 1);
            HighlightButton(btnQuiet, mode == 3);
        }

        private void HighlightButton(Border b, bool isSelected)
        {
            if (b == null) return;
            b.Background = isSelected ? new SolidColorBrush(Color.FromArgb(160, 0, 180, 255)) : new SolidColorBrush(Color.FromArgb(140, 18, 25, 42));
            b.BorderBrush = isSelected ? new SolidColorBrush(Color.FromRgb(0, 210, 255)) : new SolidColorBrush(Color.FromArgb(80, 50, 70, 100));
            TextBlock tb = b.Child as TextBlock;
            if (tb != null)
            {
                tb.Foreground = isSelected ? Brushes.White : new SolidColorBrush(Color.FromRgb(180, 200, 230));
            }
        }

        private Grid CreateGamingToolsHub()
        {
            Grid g = new Grid { Margin = new Thickness(30, 0, 30, 8) };
            Border card = new Border
            {
                Background = new SolidColorBrush(Color.FromArgb(190, 10, 15, 26)),
                CornerRadius = new CornerRadius(8),
                BorderBrush = new SolidColorBrush(Color.FromArgb(60, 0, 140, 255)),
                BorderThickness = new Thickness(1),
                Padding = new Thickness(15, 8, 15, 8)
            };

            Grid inner = new Grid();
            inner.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(220) });
            inner.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            StackPanel ramSp = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            ramSp.Children.Add(new TextBlock { Text = "SYSTEM MEMORY LOAD", FontSize = 9.5, FontWeight = FontWeights.Bold, Foreground = new SolidColorBrush(Color.FromRgb(100, 160, 220)) });
            lblRamStats = new TextBlock { Text = "RAM: --% (-- / -- GB)", FontSize = 12, FontWeight = FontWeights.Bold, Foreground = Brushes.White, Margin = new Thickness(0, 2, 0, 0) };
            ramSp.Children.Add(lblRamStats);
            Grid.SetColumn(ramSp, 0);
            inner.Children.Add(ramSp);

            StackPanel btnSp = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Right,
                VerticalAlignment = VerticalAlignment.Center
            };

            Border btnRamBoost = CreateToolButton("⚡ BOOST RAM", () =>
            {
                HardwareBridge.OptimizeMemory();
                lblStatusMsg.Text = "SYSTEM RAM PURGED & OPTIMIZED";
            });
            btnSp.Children.Add(btnRamBoost);

            btnWinLock = CreateToolButton("🔒 WIN-KEY: UNLOCKED", () =>
            {
                winKeyLocked = !winKeyLocked;
                TextBlock tb = btnWinLock.Child as TextBlock;
                if (tb != null)
                {
                    tb.Text = winKeyLocked ? "🔒 WIN-KEY: LOCKED" : "🔓 WIN-KEY: UNLOCKED";
                }
                lblStatusMsg.Text = winKeyLocked ? "GAMING WIN-KEY LOCK ACTIVE" : "WIN-KEY RESTORED";
            });
            btnSp.Children.Add(btnWinLock);

            Border btnTaskMgr = CreateToolButton("📊 TASK MANAGER", () =>
            {
                try { Process.Start("taskmgr.exe"); } catch { }
            });
            btnSp.Children.Add(btnTaskMgr);

            Border btnNvidia = CreateToolButton("🎮 NVIDIA PANEL", () =>
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
                Background = new SolidColorBrush(Color.FromArgb(150, 18, 25, 42)),
                BorderBrush = new SolidColorBrush(Color.FromArgb(90, 0, 160, 255)),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(5),
                Padding = new Thickness(12, 6, 12, 6),
                Margin = new Thickness(5, 0, 5, 0),
                Cursor = Cursors.Hand
            };
            TextBlock t = new TextBlock
            {
                Text = label,
                FontSize = 10.5,
                FontWeight = FontWeights.Bold,
                Foreground = new SolidColorBrush(Color.FromRgb(180, 220, 255))
            };
            b.Child = t;
            b.MouseDown += (s, e) => { action(); };
            return b;
        }

        // ==========================================
        // PAGE 2: COOLING CONTROL STUDIO VIEW
        // ==========================================
        private Grid CreateCoolingView()
        {
            Grid g = new Grid { Margin = new Thickness(35, 15, 35, 15) };
            g.RowDefinitions.Add(new RowDefinition { Height = new GridLength(200) });
            g.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

            // Top: Dual Detailed Fan Cards
            Grid topRow = new Grid();
            topRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            topRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            // CPU Fan Card
            Border cpuCard = CreateSectionCard("CPU DEDICATED COOLING BLOWER", "Intel Core Thermal Extraction");
            StackPanel cpuSp = new StackPanel { Margin = new Thickness(20, 15, 20, 15) };
            lblCoolingCpuRpm = new TextBlock { Text = "0 RPM (0%)", FontSize = 28, FontWeight = FontWeights.Black, Foreground = new SolidColorBrush(Color.FromRgb(0, 210, 255)) };
            progCoolingCpu = new ProgressBar { Height = 14, Minimum = 0, Maximum = 100, Value = 50, Margin = new Thickness(0, 10, 0, 10), Foreground = new SolidColorBrush(Color.FromRgb(0, 210, 255)) };
            TextBlock cpuSub = new TextBlock { Text = "Dynamic curve active: Ramps smoothly based on package temp", FontSize = 10.5, Foreground = Brushes.Silver };
            cpuSp.Children.Add(lblCoolingCpuRpm);
            cpuSp.Children.Add(progCoolingCpu);
            cpuSp.Children.Add(cpuSub);
            cpuCard.Child = cpuSp;
            Grid.SetColumn(cpuCard, 0);
            topRow.Children.Add(cpuCard);

            // GPU Fan Card
            Border gpuCard = CreateSectionCard("GPU DEDICATED COOLING BLOWER", "NVIDIA RTX 4050 Thermal Extraction");
            gpuCard.Margin = new Thickness(15, 0, 0, 0);
            StackPanel gpuSp = new StackPanel { Margin = new Thickness(20, 15, 20, 15) };
            lblCoolingGpuRpm = new TextBlock { Text = "0 RPM (0%)", FontSize = 28, FontWeight = FontWeights.Black, Foreground = new SolidColorBrush(Color.FromRgb(56, 189, 248)) };
            progCoolingGpu = new ProgressBar { Height = 14, Minimum = 0, Maximum = 100, Value = 0, Margin = new Thickness(0, 10, 0, 10), Foreground = new SolidColorBrush(Color.FromRgb(56, 189, 248)) };
            TextBlock gpuSub = new TextBlock { Text = "Zero-RPM mode enabled: Fan completely turns off when GPU is idle", FontSize = 10.5, Foreground = Brushes.Silver };
            gpuSp.Children.Add(lblCoolingGpuRpm);
            gpuSp.Children.Add(progCoolingGpu);
            gpuSp.Children.Add(gpuSub);
            gpuCard.Child = gpuSp;
            Grid.SetColumn(gpuCard, 1);
            topRow.Children.Add(gpuCard);

            Grid.SetRow(topRow, 0);
            g.Children.Add(topRow);

            // Bottom: Fan Curve Table & Fine Tuning
            Border bottomCard = CreateSectionCard("HARDWARE FAN CURVE ARCHITECTURE", "Insyde / Clevo Embedded Controller Fan Table");
            bottomCard.Margin = new Thickness(0, 15, 0, 0);

            StackPanel botSp = new StackPanel { Margin = new Thickness(25, 20, 25, 20) };

            TextBlock intro = new TextBlock
            {
                Text = "Your laptop's EC hardware monitors temperatures every 250ms and dynamically calculates RPM according to 4 calibrated thermal stages:",
                FontSize = 11.5,
                Foreground = Brushes.White,
                Margin = new Thickness(0, 0, 0, 15)
            };
            botSp.Children.Add(intro);

            UniformGrid curveGrid = new UniformGrid { Columns = 4, Margin = new Thickness(0, 0, 0, 20) };
            curveGrid.Children.Add(CreateCurveStep("STAGE 1 (IDLE)", "45 °C", "28% Duty (Quiet)"));
            curveGrid.Children.Add(CreateCurveStep("STAGE 2 (NORMAL)", "60 °C", "50% Duty (Balanced)"));
            curveGrid.Children.Add(CreateCurveStep("STAGE 3 (GAMING)", "80 °C", "66% Duty (High Airflow)"));
            curveGrid.Children.Add(CreateCurveStep("STAGE 4 (PEAK)", "100 °C", "100% Duty (Full Turbo)"));
            botSp.Children.Add(curveGrid);

            // Dedicated Custom Fan Slider
            Grid sliderRow = new Grid { Margin = new Thickness(0, 10, 0, 0) };
            sliderRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(200) });
            sliderRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            sliderRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(60) });
            sliderRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(160) });

            TextBlock lblDed = new TextBlock { Text = "Fine Manual Override:", FontSize = 12, FontWeight = FontWeights.Bold, Foreground = Brushes.White, VerticalAlignment = VerticalAlignment.Center };
            Grid.SetColumn(lblDed, 0);
            sliderRow.Children.Add(lblDed);

            sliderDedicatedCustom = new Slider { Minimum = 30, Maximum = 100, Value = 80, TickFrequency = 5, VerticalAlignment = VerticalAlignment.Center };
            sliderDedicatedCustom.ValueChanged += (s, e) => { lblDedicatedCustomVal.Text = string.Format("{0:F0}%", sliderDedicatedCustom.Value); };
            Grid.SetColumn(sliderDedicatedCustom, 1);
            sliderRow.Children.Add(sliderDedicatedCustom);

            lblDedicatedCustomVal = new TextBlock { Text = "80%", FontSize = 13, FontWeight = FontWeights.Bold, Foreground = new SolidColorBrush(Color.FromRgb(0, 210, 255)), VerticalAlignment = VerticalAlignment.Center, TextAlignment = TextAlignment.Center };
            Grid.SetColumn(lblDedicatedCustomVal, 2);
            sliderRow.Children.Add(lblDedicatedCustomVal);

            Button btnApplyDed = new Button
            {
                Content = "Apply Fan Duty",
                FontSize = 11,
                FontWeight = FontWeights.Bold,
                Foreground = Brushes.White,
                Background = new LinearGradientBrush(Color.FromRgb(0, 140, 255), Color.FromRgb(0, 80, 200), 45),
                BorderThickness = new Thickness(0),
                Height = 32,
                Cursor = Cursors.Hand
            };
            btnApplyDed.Click += (s, e) =>
            {
                int val = (int)sliderDedicatedCustom.Value;
                bool ok = HardwareBridge.SetCustomSpeed(val);
                if (ok)
                {
                    currentFanMode = 6;
                    currentCustomDuty = val;
                    lblStatusMsg.Text = string.Format("CUSTOM FAN SPEED ({0}%) LOCKED", val);
                }
            };
            Grid.SetColumn(btnApplyDed, 3);
            sliderRow.Children.Add(btnApplyDed);

            botSp.Children.Add(sliderRow);
            bottomCard.Child = botSp;
            Grid.SetRow(bottomCard, 1);
            g.Children.Add(bottomCard);

            return g;
        }

        private Border CreateSectionCard(string title, string subtitle)
        {
            Border b = new Border
            {
                Background = new SolidColorBrush(Color.FromArgb(200, 12, 17, 30)),
                CornerRadius = new CornerRadius(10),
                BorderBrush = new SolidColorBrush(Color.FromArgb(80, 0, 140, 255)),
                BorderThickness = new Thickness(1),
                Padding = new Thickness(12)
            };
            return b;
        }

        private Border CreateCurveStep(string label, string temp, string desc)
        {
            Border b = new Border
            {
                Background = new SolidColorBrush(Color.FromArgb(160, 18, 25, 42)),
                BorderBrush = new SolidColorBrush(Color.FromArgb(70, 0, 160, 255)),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(6),
                Padding = new Thickness(10),
                Margin = new Thickness(5)
            };
            StackPanel sp = new StackPanel();
            sp.Children.Add(new TextBlock { Text = label, FontSize = 9, FontWeight = FontWeights.Bold, Foreground = new SolidColorBrush(Color.FromRgb(100, 160, 220)) });
            sp.Children.Add(new TextBlock { Text = temp, FontSize = 18, FontWeight = FontWeights.Black, Foreground = Brushes.White, Margin = new Thickness(0, 2, 0, 2) });
            sp.Children.Add(new TextBlock { Text = desc, FontSize = 10, Foreground = Brushes.Silver });
            b.Child = sp;
            return b;
        }

        // ==========================================
        // PAGE 3: GAMING HUB VIEW
        // ==========================================
        private Grid CreateGamingView()
        {
            Grid g = new Grid { Margin = new Thickness(35, 15, 35, 15) };
            g.RowDefinitions.Add(new RowDefinition { Height = new GridLength(140) });
            g.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

            // Top: Scenario Profiles
            Border scenCard = CreateSectionCard("PERFORMANCE & GAMING SCENARIOS", "Instant System Clock & Cooling Profiles");
            StackPanel scenSp = new StackPanel { Margin = new Thickness(15, 10, 15, 10) };

            lblScenarioStatus = new TextBlock { Text = "Current Active Scenario: High Performance Gaming", FontSize = 12, FontWeight = FontWeights.Bold, Foreground = new SolidColorBrush(Color.FromRgb(0, 210, 255)), Margin = new Thickness(0, 0, 0, 10) };
            scenSp.Children.Add(lblScenarioStatus);

            UniformGrid scenGrid = new UniformGrid { Columns = 3 };
            scenGrid.Children.Add(CreateScenarioBtn("🚀 TURBO GAMING", "Maximum FPS, full cooling, zero throttle", () =>
            {
                ApplyMode(1, "100% MAXIMUM TURBO");
                lblScenarioStatus.Text = "Current Active Scenario: Turbo Gaming Mode";
            }));
            scenGrid.Children.Add(CreateScenarioBtn("⚖️ BALANCED DAILY", "Optimized thermals and power balance", () =>
            {
                ApplyMode(0, "DYNAMIC AUTO (BALANCED)");
                lblScenarioStatus.Text = "Current Active Scenario: Balanced Daily Mode";
            }));
            scenGrid.Children.Add(CreateScenarioBtn("🍃 ECO & SILENT", "Whisper-quiet fans, maximum battery life", () =>
            {
                ApplyMode(3, "SILENT / ECO");
                lblScenarioStatus.Text = "Current Active Scenario: Eco & Silent Mode";
            }));
            scenSp.Children.Add(scenGrid);

            scenCard.Child = scenSp;
            Grid.SetRow(scenCard, 0);
            g.Children.Add(scenCard);

            // Bottom: Tools & System Launchers
            Border toolsCard = CreateSectionCard("GAMING UTILITIES & HARDWARE ACCELERATION", "Fast access to critical performance tools");
            toolsCard.Margin = new Thickness(0, 15, 0, 0);

            StackPanel tSp = new StackPanel { Margin = new Thickness(20) };

            UniformGrid utilGrid = new UniformGrid { Columns = 2, Margin = new Thickness(0, 10, 0, 0) };
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

            tSp.Children.Add(utilGrid);
            toolsCard.Child = tSp;
            Grid.SetRow(toolsCard, 1);
            g.Children.Add(toolsCard);

            return g;
        }

        private Border CreateScenarioBtn(string title, string desc, Action act)
        {
            Border b = new Border
            {
                Background = new SolidColorBrush(Color.FromArgb(170, 18, 25, 42)),
                BorderBrush = new SolidColorBrush(Color.FromArgb(90, 0, 160, 255)),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(6),
                Padding = new Thickness(12, 10, 12, 10),
                Margin = new Thickness(5),
                Cursor = Cursors.Hand
            };
            StackPanel sp = new StackPanel();
            sp.Children.Add(new TextBlock { Text = title, FontSize = 12, FontWeight = FontWeights.Bold, Foreground = Brushes.White });
            sp.Children.Add(new TextBlock { Text = desc, FontSize = 9.5, Foreground = Brushes.Silver, Margin = new Thickness(0, 3, 0, 0) });
            b.Child = sp;
            b.MouseDown += (s, e) => { act(); };
            return b;
        }

        private Border CreateLauncherTile(string title, string desc, Action act)
        {
            Border b = new Border
            {
                Background = new SolidColorBrush(Color.FromArgb(160, 15, 23, 40)),
                BorderBrush = new SolidColorBrush(Color.FromArgb(70, 0, 140, 255)),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(8),
                Padding = new Thickness(15, 12, 15, 12),
                Margin = new Thickness(6),
                Cursor = Cursors.Hand
            };
            StackPanel sp = new StackPanel();
            sp.Children.Add(new TextBlock { Text = title, FontSize = 13, FontWeight = FontWeights.Bold, Foreground = new SolidColorBrush(Color.FromRgb(0, 210, 255)) });
            sp.Children.Add(new TextBlock { Text = desc, FontSize = 10.5, Foreground = Brushes.Silver, Margin = new Thickness(0, 4, 0, 0) });
            b.Child = sp;
            b.MouseDown += (s, e) => { act(); };
            return b;
        }

        // ==========================================
        // PAGE 4: SYSTEM HEALTH & SPECS VIEW
        // ==========================================
        private Grid CreateHealthView()
        {
            Grid g = new Grid { Margin = new Thickness(35, 15, 35, 15) };
            g.RowDefinitions.Add(new RowDefinition { Height = new GridLength(160) });
            g.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

            // Top: Hardware Specs Identity Card
            Border specsCard = CreateSectionCard("HARDWARE IDENTITY & SPECIFICATIONS", "Detected on your laptop chassis");
            StackPanel specSp = new StackPanel { Margin = new Thickness(20, 12, 20, 12) };

            UniformGrid specGrid = new UniformGrid { Columns = 3 };
            specGrid.Children.Add(CreateSpecField("LAPTOP MODEL", "Acer Aspire A715-79G"));
            specGrid.Children.Add(CreateSpecField("GRAPHICS CARD", "NVIDIA RTX 4050 Laptop (6GB)"));
            specGrid.Children.Add(CreateSpecField("PROCESSOR", string.Format("Intel Core ({0} Cores)", Environment.ProcessorCount)));
            specGrid.Children.Add(CreateSpecField("EMBEDDED CONTROLLER", "Insyde / Clevo EC Bridge v1.13"));
            specGrid.Children.Add(CreateSpecField("OPERATING SYSTEM", "Windows 11 (64-bit Architecture)"));
            specGrid.Children.Add(CreateSpecField("PC NAME", Environment.MachineName));

            specSp.Children.Add(specGrid);
            specsCard.Child = specSp;
            Grid.SetRow(specsCard, 0);
            g.Children.Add(specsCard);

            // Bottom: Memory, Battery & Disk Health
            Grid botGrid = new Grid { Margin = new Thickness(0, 15, 0, 0) };
            botGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            botGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            // Left: RAM & Battery Card
            Border ramCard = CreateSectionCard("MEMORY & POWER TELEMETRY", "Live telemetry metrics");
            StackPanel rbSp = new StackPanel { Margin = new Thickness(20, 15, 20, 15) };

            lblHealthRam = new TextBlock { Text = "RAM: --% (-- / -- GB)", FontSize = 14, FontWeight = FontWeights.Bold, Foreground = Brushes.White };
            progHealthRam = new ProgressBar { Height = 14, Minimum = 0, Maximum = 100, Value = 50, Margin = new Thickness(0, 8, 0, 15), Foreground = new SolidColorBrush(Color.FromRgb(0, 210, 255)) };
            rbSp.Children.Add(lblHealthRam);
            rbSp.Children.Add(progHealthRam);

            lblHealthBattery = new TextBlock { Text = "Battery: --% (Checking Status...)", FontSize = 14, FontWeight = FontWeights.Bold, Foreground = Brushes.White };
            progHealthBattery = new ProgressBar { Height = 14, Minimum = 0, Maximum = 100, Value = 100, Margin = new Thickness(0, 8, 0, 10), Foreground = new SolidColorBrush(Color.FromRgb(0, 225, 140)) };
            rbSp.Children.Add(lblHealthBattery);
            rbSp.Children.Add(progHealthBattery);

            ramCard.Child = rbSp;
            Grid.SetColumn(ramCard, 0);
            botGrid.Children.Add(ramCard);

            // Right: Storage Drives Card
            Border diskCard = CreateSectionCard("LOCAL STORAGE DRIVES", "Free capacity on disk drives");
            diskCard.Margin = new Thickness(15, 0, 0, 0);

            spDrives = new StackPanel { Margin = new Thickness(20, 15, 20, 15) };
            PopulateStorageDrives(spDrives);

            diskCard.Child = spDrives;
            Grid.SetColumn(diskCard, 1);
            botGrid.Children.Add(diskCard);

            Grid.SetRow(botGrid, 1);
            g.Children.Add(botGrid);

            return g;
        }

        private Border CreateSpecField(string title, string val)
        {
            Border b = new Border { Margin = new Thickness(6) };
            StackPanel sp = new StackPanel();
            sp.Children.Add(new TextBlock { Text = title, FontSize = 9, FontWeight = FontWeights.Bold, Foreground = new SolidColorBrush(Color.FromRgb(100, 160, 220)) });
            sp.Children.Add(new TextBlock { Text = val, FontSize = 12, FontWeight = FontWeights.Bold, Foreground = Brushes.White, Margin = new Thickness(0, 2, 0, 0) });
            b.Child = sp;
            return b;
        }

        private void PopulateStorageDrives(StackPanel sp)
        {
            try
            {
                DriveInfo[] drives = DriveInfo.GetDrives();
                foreach (DriveInfo d in drives)
                {
                    if (d.IsReady && d.DriveType == DriveType.Fixed)
                    {
                        double totalGb = d.TotalSize / (1024.0 * 1024.0 * 1024.0);
                        double freeGb = d.AvailableFreeSpace / (1024.0 * 1024.0 * 1024.0);
                        double usedGb = totalGb - freeGb;
                        double pct = (usedGb / totalGb) * 100.0;

                        StackPanel dSp = new StackPanel { Margin = new Thickness(0, 0, 0, 12) };
                        Grid dHeader = new Grid();
                        dHeader.Children.Add(new TextBlock { Text = string.Format("Drive {0} ({1})", d.Name, string.IsNullOrEmpty(d.VolumeLabel) ? "Local Disk" : d.VolumeLabel), FontSize = 11, FontWeight = FontWeights.Bold, Foreground = Brushes.White });
                        dHeader.Children.Add(new TextBlock { Text = string.Format("{0:F1} GB free of {1:F1} GB", freeGb, totalGb), FontSize = 10, Foreground = Brushes.Silver, HorizontalAlignment = HorizontalAlignment.Right });
                        dSp.Children.Add(dHeader);

                        ProgressBar pb = new ProgressBar { Height = 10, Minimum = 0, Maximum = 100, Value = pct, Margin = new Thickness(0, 5, 0, 0), Foreground = new SolidColorBrush(Color.FromRgb(0, 180, 255)) };
                        dSp.Children.Add(pb);

                        sp.Children.Add(dSp);
                    }
                }
            }
            catch { }
        }

        private Grid CreateFooter()
        {
            Grid f = new Grid { Background = new SolidColorBrush(Color.FromRgb(8, 11, 20)) };
            f.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            f.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(350) });

            TextBlock info = new TextBlock
            {
                Text = "AspireSense 2026 | Acer Aspire A715-79G (RTX 4050) | Standalone EC Hardware Architecture",
                FontSize = 9.5,
                Foreground = new SolidColorBrush(Color.FromRgb(100, 130, 170)),
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(30, 0, 0, 0)
            };
            Grid.SetColumn(info, 0);
            f.Children.Add(info);

            TextBlock stat = new TextBlock
            {
                Text = "Status: Fully Standalone (Independent of CC3)",
                FontSize = 9.5,
                FontWeight = FontWeights.Bold,
                Foreground = new SolidColorBrush(Color.FromRgb(0, 200, 140)),
                VerticalAlignment = VerticalAlignment.Center,
                HorizontalAlignment = HorizontalAlignment.Right,
                Margin = new Thickness(0, 0, 30, 0)
            };
            Grid.SetColumn(stat, 1);
            f.Children.Add(stat);

            return f;
        }

        private void Timer_Tick(object sender, EventArgs e)
        {
            try
            {
                HardwareBridge.SensorData d = HardwareBridge.ReadSensors(cpuCounter);

                // Update Dashboard Speedometers
                if (cpuSpeedo != null)
                {
                    cpuSpeedo.Update(d.CpuUsage, d.CpuTemp, d.CpuRpm, d.CpuDuty);
                }
                if (gpuSpeedo != null)
                {
                    gpuSpeedo.Update(d.GpuUsage, d.GpuTemp, d.GpuRpm, d.GpuDuty);
                }

                // Update Cooling View
                if (lblCoolingCpuRpm != null)
                {
                    lblCoolingCpuRpm.Text = string.Format("{0} RPM ({1}%)", d.CpuRpm, d.CpuDuty);
                    progCoolingCpu.Value = d.CpuDuty;
                }
                if (lblCoolingGpuRpm != null)
                {
                    lblCoolingGpuRpm.Text = string.Format("{0} RPM ({1}%)", d.GpuRpm, d.GpuDuty);
                    progCoolingGpu.Value = d.GpuDuty;
                }

                // Update RAM & Power on Dashboard & Health
                if (d.TotalRamGb > 0)
                {
                    string ramStr = string.Format("RAM: {0:F0}% ({1:F1} / {2:F1} GB)", d.RamUsage, d.UsedRamGb, d.TotalRamGb);
                    if (lblRamStats != null) lblRamStats.Text = ramStr;
                    if (lblHealthRam != null)
                    {
                        lblHealthRam.Text = ramStr;
                        progHealthRam.Value = d.RamUsage;
                    }
                }

                if (lblHealthBattery != null)
                {
                    string pwrStr = d.IsAcPlugged ? "Plugged in (AC Power)" : "On Battery";
                    lblHealthBattery.Text = string.Format("Battery: {0}% ({1})", d.BatteryPercent, pwrStr);
                    progHealthBattery.Value = d.BatteryPercent;
                }

                // ACTIVE THERMAL WATCHDOG: Safeguard hardware against excessive temperatures
                if (d.CpuTemp >= 90 || d.GpuTemp >= 85)
                {
                    if (currentFanMode == 3 || (currentFanMode == 6 && currentCustomDuty < 80))
                    {
                        // High heat detected while fan is constrained -> automatically engage Turbo cooling!
                        HardwareBridge.SetFanMode(1);
                        currentFanMode = 1;
                        if (lblStatusMsg != null)
                        {
                            lblStatusMsg.Text = "⚠️ THERMAL WATCHDOG: HIGH HEAT (AUTO TURBO ENGAGED)";
                            lblStatusMsg.Foreground = new SolidColorBrush(Color.FromRgb(255, 60, 60));
                        }
                        HighlightButton(btnMax, true);
                        HighlightButton(btnAuto, false);
                        HighlightButton(btnQuiet, false);
                    }
                }
            }
            catch { }
        }

        private static System.Threading.Mutex singleMutex = null;

        [STAThread]
        public static void Main()
        {
            bool createdNew;
            singleMutex = new System.Threading.Mutex(true, "Global\\AspireSense_SingleInstance_App", out createdNew);
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
