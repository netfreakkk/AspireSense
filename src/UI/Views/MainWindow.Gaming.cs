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
    public partial class MainWindow
    {
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

    }
}