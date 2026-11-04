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
    // MAIN APPLICATION WINDOW (CORE SHELL & LIFECYCLE)
    // ==========================================
    // ==========================================
    // MAIN APPLICATION WINDOW: ASPIRESENSE
    // ==========================================
    public partial class MainWindow : Window
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
        private Border btnToolSleepShield;
        private TextBlock lblToolSleepShieldText;
        private Border btnEcSleepShield;
        private TextBlock lblEcSleepShieldTitle;
        private TextBlock lblEcSleepShieldSub;
        private System.Windows.Forms.ToolStripMenuItem itemTraySleepShield;
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
        public const int WM_POWERBROADCAST = 0x0218;
        public const int PBT_APMSUSPEND = 0x0004;
        public const int PBT_APMRESUMESUSPEND = 0x0007;
        public const int PBT_APMRESUMEAUTOMATIC = 0x0012;

        [DllImport("user32.dll")]
        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        public static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

        [DllImport("user32.dll")]
        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
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
        private bool startInTray = false;
        private System.Windows.Forms.NotifyIcon trayIcon;
        private bool isExplicitExit = false;
        private bool balloonTipShown = false;
        private uint wmShowInstance = 0;

        public MainWindow() : this(false) { }

        public MainWindow(bool startMinimized)
        {
            this.startInTray = startMinimized;
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
            UpdateSleepShieldUi();

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
                if (!isExplicitExit)
                {
                    e.Cancel = true;
                    HideToTray();
                    return;
                }

                if (trayIcon != null)
                {
                    try { trayIcon.Visible = false; trayIcon.Dispose(); trayIcon = null; } catch { }
                }
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

            Microsoft.Win32.SystemEvents.PowerModeChanged += (s, e) =>
            {
                if (e.Mode == Microsoft.Win32.PowerModes.Suspend)
                {
                    OnSystemSuspend();
                }
                else if (e.Mode == Microsoft.Win32.PowerModes.Resume)
                {
                    OnSystemResume();
                }
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
                    wmShowInstance = Program.RegisterWindowMessage(Program.MSG_SHOW_INSTANCE);
                }
            }
            catch { }

            InitSystemTray();

            if (startInTray)
            {
                this.WindowState = WindowState.Minimized;
                this.Hide();
            }
        }

        private IntPtr HwndHook(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            if (msg == WM_HOTKEY && wParam.ToInt32() == 9001)
            {
                ToggleOverlay();
                handled = true;
            }
            else if (wmShowInstance != 0 && msg == (int)wmShowInstance)
            {
                ShowFromTray();
                handled = true;
            }
            else if (msg == WM_POWERBROADCAST)
            {
                int eventCode = wParam.ToInt32();
                if (eventCode == PBT_APMSUSPEND)
                {
                    OnSystemSuspend();
                }
                else if (eventCode == PBT_APMRESUMESUSPEND || eventCode == PBT_APMRESUMEAUTOMATIC)
                {
                    OnSystemResume();
                }
            }
            return IntPtr.Zero;
        }

        private bool isSystemSuspended = false;
        private int lastResumeTick = 0;
        private readonly object powerLock = new object();

        private void InitSystemTray()
        {
            try
            {
                string icoPath = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "app.ico");
                System.Drawing.Icon icon = File.Exists(icoPath) ? new System.Drawing.Icon(icoPath) : System.Drawing.SystemIcons.Application;

                trayIcon = new System.Windows.Forms.NotifyIcon
                {
                    Icon = icon,
                    Text = "AspireSense - Thermal & Acoustic Arbiter",
                    Visible = true
                };

                System.Windows.Forms.ContextMenuStrip menu = new System.Windows.Forms.ContextMenuStrip();

                System.Windows.Forms.ToolStripMenuItem itemOpen = new System.Windows.Forms.ToolStripMenuItem("⚡ Open AspireSense");
                itemOpen.Font = new System.Drawing.Font(itemOpen.Font, System.Drawing.FontStyle.Bold);
                itemOpen.Click += (s, e) => ShowFromTray();
                menu.Items.Add(itemOpen);

                menu.Items.Add(new System.Windows.Forms.ToolStripSeparator());

                System.Windows.Forms.ToolStripMenuItem itemAuto = new System.Windows.Forms.ToolStripMenuItem("🛡️ Dynamic Auto");
                itemAuto.Click += (s, e) => { ApplyMode(0, "Dynamic Auto"); };
                menu.Items.Add(itemAuto);

                System.Windows.Forms.ToolStripMenuItem itemQuiet = new System.Windows.Forms.ToolStripMenuItem("🤫 Silent / Quiet Mode");
                itemQuiet.Click += (s, e) => { ApplyMode(3, "Silent / Quiet (Acoustic Cap)"); };
                menu.Items.Add(itemQuiet);

                System.Windows.Forms.ToolStripMenuItem itemMax = new System.Windows.Forms.ToolStripMenuItem("🚀 Turbo Max (100%)");
                itemMax.Click += (s, e) => { ApplyMode(1, "Max (100% Turbo Cooling)"); };
                menu.Items.Add(itemMax);

                System.Windows.Forms.ToolStripMenuItem itemFlush = new System.Windows.Forms.ToolStripMenuItem("❄️ Engage Turbo Flush (60s)");
                itemFlush.Click += (s, e) =>
                {
                    heatFlushRemaining = 60;
                    ApplyMode(1, "Max (60s Turbo Heat Flush Engaged)");
                };
                menu.Items.Add(itemFlush);

                menu.Items.Add(new System.Windows.Forms.ToolStripSeparator());

                System.Windows.Forms.ToolStripMenuItem itemHud = new System.Windows.Forms.ToolStripMenuItem("🎮 Toggle In-Game HUD (Ctrl+Shift+O)");
                itemHud.Click += (s, e) => ToggleOverlay();
                menu.Items.Add(itemHud);

                menu.Items.Add(new System.Windows.Forms.ToolStripSeparator());

                itemTraySleepShield = new System.Windows.Forms.ToolStripMenuItem("🌙 Sleep Ghost Shield");
                itemTraySleepShield.Checked = HardwareBridge.SleepWakeShieldEnabled;
                itemTraySleepShield.Click += (s, e) =>
                {
                    HardwareBridge.ToggleSleepWakeProtection();
                    UpdateSleepShieldUi();
                };
                menu.Items.Add(itemTraySleepShield);

                menu.Items.Add(new System.Windows.Forms.ToolStripSeparator());

                System.Windows.Forms.ToolStripMenuItem itemExit = new System.Windows.Forms.ToolStripMenuItem("❌ Exit AspireSense");
                itemExit.Click += (s, e) => ExitApplication();
                menu.Items.Add(itemExit);

                trayIcon.ContextMenuStrip = menu;

                trayIcon.DoubleClick += (s, e) => ShowFromTray();
                trayIcon.MouseClick += (s, e) =>
                {
                    if (e.Button == System.Windows.Forms.MouseButtons.Left)
                    {
                        if (this.IsVisible && this.WindowState != WindowState.Minimized)
                        {
                            HideToTray();
                        }
                        else
                        {
                            ShowFromTray();
                        }
                    }
                };
            }
            catch { }
        }

        public void ShowFromTray()
        {
            this.Show();
            this.WindowState = WindowState.Normal;
            this.Activate();
            this.Focus();
        }

        public void HideToTray()
        {
            this.Hide();
            if (!balloonTipShown)
            {
                balloonTipShown = true;
                if (trayIcon != null)
                {
                    try
                    {
                        trayIcon.ShowBalloonTip(2500, "AspireSense Protected",
                            "AspireSense is guarding thermals in the background. Click this tray icon anytime to open.",
                            System.Windows.Forms.ToolTipIcon.Info);
                    }
                    catch { }
                }
            }
        }

        public void ExitApplication()
        {
            isExplicitExit = true;
            if (trayIcon != null)
            {
                try { trayIcon.Visible = false; trayIcon.Dispose(); trayIcon = null; } catch { }
            }
            this.Close();
        }

        public void UpdateSleepShieldUi()
        {
            bool enabled = HardwareBridge.SleepWakeShieldEnabled;

            if (lblToolSleepShieldText != null && btnToolSleepShield != null)
            {
                lblToolSleepShieldText.Text = enabled ? "🌙 Sleep Shield: ON" : "🌙 Sleep Shield: OFF";
                lblToolSleepShieldText.Foreground = enabled ? DesignTokens.BrushAccentCyan : DesignTokens.BrushTextSecondary;
                btnToolSleepShield.Background = enabled ? new SolidColorBrush(Color.FromArgb(40, 6, 182, 212)) : DesignTokens.BrushBgInput;
                btnToolSleepShield.BorderBrush = enabled ? DesignTokens.BrushAccentCyan : DesignTokens.BrushBorderSubtle;
                btnToolSleepShield.ToolTip = enabled
                    ? "Ghost Wake Shield ACTIVE: USB jitter, mouse bumps, and monitor power-off will NOT wake the laptop. Click to toggle OFF."
                    : "Ghost Wake Shield OFF: External mouse and keyboard CAN wake the laptop. Click to toggle ON.";
            }

            if (lblEcSleepShieldTitle != null && btnEcSleepShield != null)
            {
                lblEcSleepShieldTitle.Text = enabled
                    ? "🌙 Ghost Sleep-Wake Shield: ACTIVE"
                    : "🌙 Ghost Sleep-Wake Shield: DISABLED";
                lblEcSleepShieldTitle.Foreground = enabled ? DesignTokens.BrushAccentCyan : DesignTokens.BrushTextSecondary;
                if (lblEcSleepShieldSub != null)
                {
                    lblEcSleepShieldSub.Text = enabled
                        ? "Blocks external monitor off, USB hub spikes & mouse jitter from waking laptop."
                        : "External peripherals are allowed to wake laptop. Monitor power-off may trigger wake.";
                }
                btnEcSleepShield.Background = enabled ? new SolidColorBrush(Color.FromArgb(40, 6, 182, 212)) : DesignTokens.BrushBgInput;
                btnEcSleepShield.BorderBrush = enabled ? DesignTokens.BrushAccentCyan : DesignTokens.BrushBorderSubtle;
            }

            if (itemTraySleepShield != null)
            {
                itemTraySleepShield.Checked = enabled;
            }
        }

        private void OnSystemSuspend()
        {
            lock (powerLock)
            {
                if (isSystemSuspended) return;
                isSystemSuspended = true;

                try
                {
                    HardwareBridge.EnsureSleepWakeProtection(false);
                    EcAntiHallucinationEngine.LogDecision("POWER SUSPEND: Sleep detected -> Restoring fans to Auto & pausing background telemetry");
                    if (timer != null) timer.Stop();
                    if (overlayTimer != null) overlayTimer.Stop();
                    GameTelemetryEngine.Stop();
                    HardwareBridge.ResetToAuto();
                    HardwareBridge.CloseHardware();
                }
                catch { }
            }
        }

        private void OnSystemResume()
        {
            lock (powerLock)
            {
                int now = Environment.TickCount;
                if (lastResumeTick != 0 && (now - lastResumeTick) < 1500) return; // Debounce dual notifications
                lastResumeTick = now;
                isSystemSuspended = false;

                try
                {
                    EcAntiHallucinationEngine.LogDecision("POWER RESUME: System wake detected -> Suppressing post-wake EC emergency 100% fan ramp");

                    // Allow hardware bus, PCIe dGPU, and EC firmware ~600ms to power up and stabilize
                    DispatcherTimer resumeTimer = new DispatcherTimer();
                    resumeTimer.Interval = TimeSpan.FromMilliseconds(600);
                    resumeTimer.Tick += (s, e) =>
                    {
                        resumeTimer.Stop();
                        try
                        {
                            HardwareBridge.InitializeHardware();

                            // Force firmware handshake to cancel EC POST emergency 100% turbo fan blast
                            if (HardwareBridge.DetectedVendor == HardwareVendor.Acer)
                            {
                                AcerInsydeBridge.ResetFans();
                                if (currentFanMode != 0)
                                {
                                    HardwareBridge.SetFanMode(currentFanMode);
                                }
                            }
                            else
                            {
                                HardwareBridge.SetFanMode(currentFanMode);
                            }

                            // Reset anti-hallucination engine calibration from fresh post-wake temperatures
                            EcAntiHallucinationEngine.FilteredThermalEma = 0;
                            EcAntiHallucinationEngine.SustainedLoadDurationSec = 0;
                            EcAntiHallucinationEngine.CurrentAppliedMode = -1;
                            watchdogHighHeatCount = 0;
                            watchdogCoolCount = 0;
                            watchdogActive = false;

                            if (timer != null && !timer.IsEnabled) timer.Start();
                            EnsureTelemetryState();

                            EcAntiHallucinationEngine.LogDecision("POWER RESUME COMPLETE: Fans normalized and thermal monitoring restored.");
                        }
                        catch { }
                    };
                    resumeTimer.Start();
                }
                catch { }
            }
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
                Content = "\u2014",
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
            btnMin.Click += (s, e) => { HideToTray(); };
            winBtnSp.Children.Add(btnMin);

            Button btnClose = new Button
            {
                Content = "\u2715",
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
            btnClose.Click += (s, e) => { HideToTray(); };
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

                // 2. EC Anti-Hallucination Arbiter Engine (Always evaluate safety logic in background)
                if (currentFanMode != 1 && heatFlushRemaining == 0)
                {
                    bool isGaming = GameTelemetryEngine.IsGameActive || (d.GpuUsage >= 45.0f && d.GpuTemp >= 58);
                    EcAntiHallucinationEngine.Update(d.CpuTemp, d.GpuTemp, d.CpuUsage, d.GpuUsage, d.CpuWatts, isGaming);
                }

                // 3. Active Thermal Watchdog (Always safeguard hardware in background)
                EvaluateThermalWatchdog(d);

                // Live System Tray Status Tooltip
                if (trayIcon != null)
                {
                    string modeStr = currentFanMode == 1 ? "Turbo" : (currentFanMode == 3 ? "Quiet" : (currentFanMode == 6 ? "Custom" : "Auto"));
                    string tip = string.Format("AspireSense • CPU {0}°C | GPU {1}°C [{2}]", d.CpuTemp, d.GpuTemp, modeStr);
                    if (tip.Length > 63) tip = tip.Substring(0, 63);
                    try { trayIcon.Text = tip; } catch { }
                }

                // 4. PERFORMANCE OPTIMIZATION (Action Plan 4.4):
                // Skip all heavy WPF visual tree layout/rendering when window is minimized or hidden
                if (this.WindowState == WindowState.Minimized || !this.IsVisible)
                {
                    return;
                }

                // 5. Periodic Network Latency Ping (Every 5 Seconds, only when UI visible)
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

            }
            catch { }
        }

        private void EvaluateThermalWatchdog(HardwareBridge.SensorData d)
        {
            try
            {
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
    }
}
