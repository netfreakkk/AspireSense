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

    }
}