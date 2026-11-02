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
        // PAGE 5: EC ANTI-HALLUCINATION ARBITER VIEW
        // ==========================================
        private Grid CreateEcGuardView()
        {
            Grid g = new Grid { Margin = new Thickness(35, 12, 35, 12) };
            g.RowDefinitions.Add(new RowDefinition { Height = new GridLength(88) }); // Top Ribbon: Live Analytics
            g.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) }); // 3 Analytics & Controls Cards

            // 1. Top Ribbon: 5 Silicon & Acoustic Metrics
            Border ribbon = CreateEcGuardRibbon();
            Grid.SetRow(ribbon, 0);
            g.Children.Add(ribbon);

            // 2. Lower Grid: 3 Equal Cards
            Grid lowerGrid = new Grid { Margin = new Thickness(0, 12, 0, 0) };
            lowerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            lowerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            lowerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            // Card 1: Hysteresis & Clamp Settings (Manual Controls)
            Border card1 = CreateEcSettingsCard();
            Grid.SetColumn(card1, 0);
            lowerGrid.Children.Add(card1);

            // Card 2: Gaming Integration & Windows Turbo Tamer (Toggles & Optimizations)
            Border card2 = CreateEcIntegrationCard();
            Grid.SetColumn(card2, 1);
            lowerGrid.Children.Add(card2);

            // Card 3: Live Decision Stream & Telemetry Feed (Real-Time Decision Log)
            Border card3 = CreateEcDecisionLogCard();
            Grid.SetColumn(card3, 2);
            lowerGrid.Children.Add(card3);

            Grid.SetRow(lowerGrid, 1);
            g.Children.Add(lowerGrid);

            return g;
        }

        private Border CreateEcGuardRibbon()
        {
            Border ribbon = new Border
            {
                Background = DesignTokens.BrushBgCard,
                CornerRadius = new CornerRadius(10),
                BorderBrush = DesignTokens.BrushBorderSubtle,
                BorderThickness = new Thickness(1),
                Padding = new Thickness(14, 8, 14, 8)
            };

            UniformGrid ug = new UniformGrid { Columns = 5 };

            ug.Children.Add(CreateRibbonTile("Silicon Die", "--°C", "Raw CPU Junction Reading", out lblEcRawDie));
            ug.Children.Add(CreateRibbonTile("Heatsink EMA", "--°C", "Physical Thermal Mass EMA", out lblEcThermalEma));
            ug.Children.Add(CreateRibbonTile("Spikes Blocked", "0 Spikes", "Jet-Fan Revs Suppressed", out lblEcSpikesCount));
            ug.Children.Add(CreateRibbonTile("Acoustic Hold", "35% Duty", "Whisper-Quiet Floor", out lblEcAcousticHold));
            ug.Children.Add(CreateRibbonTile("Arbiter Status", "Protected", "Live Classifier Decision", out lblEcClassifierState));

            ribbon.Child = ug;
            return ribbon;
        }

        private Border CreateEcSettingsCard()
        {
            StackPanel sp = new StackPanel { Margin = new Thickness(0, 4, 0, 4) };

            // Master Toggle Button
            btnEcMasterToggle = new Border
            {
                Background = new SolidColorBrush(Color.FromArgb(50, 16, 185, 129)),
                BorderBrush = DesignTokens.BrushAccentGreen,
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(8),
                Padding = new Thickness(14, 10, 14, 10),
                Margin = new Thickness(0, 0, 0, 14),
                Cursor = Cursors.Hand
            };
            lblEcMasterToggleText = new TextBlock
            {
                Text = "🛡️ EC Anti-Hallucination Guard: ENABLED",
                FontSize = 11.5,
                FontWeight = FontWeights.Bold,
                Foreground = Brushes.White,
                HorizontalAlignment = HorizontalAlignment.Center
            };
            btnEcMasterToggle.Child = lblEcMasterToggleText;
            btnEcMasterToggle.MouseDown += (s, e) =>
            {
                EcAntiHallucinationEngine.Toggle();
                UpdateDashboardEcGuardUi();
            };
            sp.Children.Add(btnEcMasterToggle);

            // Group 1: Acoustic Clamp Ceiling
            sp.Children.Add(new TextBlock
            {
                Text = "ACOUSTIC CLAMP CEILING (DESKTOP & BROWSING)",
                FontSize = 9.5,
                FontWeight = FontWeights.SemiBold,
                Foreground = DesignTokens.BrushTextMuted,
                Margin = new Thickness(0, 0, 0, 6)
            });

            UniformGrid clampGrid = new UniformGrid { Columns = 4, Margin = new Thickness(0, 0, 0, 14) };
            ecClampButtons.Clear();
            ecClampButtons.Add(CreateEcSelectorPill("30% Silent", 30, EcAntiHallucinationEngine.AcousticClampDuty == 30, (val) => {
                EcAntiHallucinationEngine.SetClampDuty(val);
                UpdateEcSelectorPills(ecClampButtons, val);
                UpdateDashboardEcGuardUi();
            }));
            ecClampButtons.Add(CreateEcSelectorPill("35% Quiet", 35, EcAntiHallucinationEngine.AcousticClampDuty == 35, (val) => {
                EcAntiHallucinationEngine.SetClampDuty(val);
                UpdateEcSelectorPills(ecClampButtons, val);
                UpdateDashboardEcGuardUi();
            }));
            ecClampButtons.Add(CreateEcSelectorPill("40% Mod", 40, EcAntiHallucinationEngine.AcousticClampDuty == 40, (val) => {
                EcAntiHallucinationEngine.SetClampDuty(val);
                UpdateEcSelectorPills(ecClampButtons, val);
                UpdateDashboardEcGuardUi();
            }));
            ecClampButtons.Add(CreateEcSelectorPill("45% Firm", 45, EcAntiHallucinationEngine.AcousticClampDuty == 45, (val) => {
                EcAntiHallucinationEngine.SetClampDuty(val);
                UpdateEcSelectorPills(ecClampButtons, val);
                UpdateDashboardEcGuardUi();
            }));
            foreach (Border b in ecClampButtons) clampGrid.Children.Add(b);
            sp.Children.Add(clampGrid);

            // Group 2: Sustained Load Delay (Hysteresis Window)
            sp.Children.Add(new TextBlock
            {
                Text = "SUSTAINED LOAD DELAY (HYSTERESIS WINDOW)",
                FontSize = 9.5,
                FontWeight = FontWeights.SemiBold,
                Foreground = DesignTokens.BrushTextMuted,
                Margin = new Thickness(0, 0, 0, 6)
            });

            UniformGrid hystGrid = new UniformGrid { Columns = 4, Margin = new Thickness(0, 0, 0, 14) };
            ecHysteresisButtons.Clear();
            ecHysteresisButtons.Add(CreateEcSelectorPill("2 Seconds", 2, EcAntiHallucinationEngine.SustainedThresholdSeconds == 2, (val) => {
                EcAntiHallucinationEngine.SetHysteresisSeconds(val);
                UpdateEcSelectorPills(ecHysteresisButtons, val);
            }));
            ecHysteresisButtons.Add(CreateEcSelectorPill("3 Seconds", 3, EcAntiHallucinationEngine.SustainedThresholdSeconds == 3, (val) => {
                EcAntiHallucinationEngine.SetHysteresisSeconds(val);
                UpdateEcSelectorPills(ecHysteresisButtons, val);
            }));
            ecHysteresisButtons.Add(CreateEcSelectorPill("4s (Best)", 4, EcAntiHallucinationEngine.SustainedThresholdSeconds == 4, (val) => {
                EcAntiHallucinationEngine.SetHysteresisSeconds(val);
                UpdateEcSelectorPills(ecHysteresisButtons, val);
            }));
            ecHysteresisButtons.Add(CreateEcSelectorPill("6 Seconds", 6, EcAntiHallucinationEngine.SustainedThresholdSeconds == 6, (val) => {
                EcAntiHallucinationEngine.SetHysteresisSeconds(val);
                UpdateEcSelectorPills(ecHysteresisButtons, val);
            }));
            foreach (Border b in ecHysteresisButtons) hystGrid.Children.Add(b);
            sp.Children.Add(hystGrid);

            // Group 3: Emergency Failsafe Temperature
            sp.Children.Add(new TextBlock
            {
                Text = "EMERGENCY SAFETY OVERRIDE (FAILSAFE TEMP)",
                FontSize = 9.5,
                FontWeight = FontWeights.SemiBold,
                Foreground = DesignTokens.BrushTextMuted,
                Margin = new Thickness(0, 0, 0, 6)
            });

            UniformGrid failGrid = new UniformGrid { Columns = 4, Margin = new Thickness(0, 0, 0, 8) };
            ecFailsafeButtons.Clear();
            ecFailsafeButtons.Add(CreateEcSelectorPill("82°C", 82, EcAntiHallucinationEngine.FailsafeTemp == 82, (val) => {
                EcAntiHallucinationEngine.SetFailsafeTemp(val);
                UpdateEcSelectorPills(ecFailsafeButtons, val);
            }));
            ecFailsafeButtons.Add(CreateEcSelectorPill("85°C", 85, EcAntiHallucinationEngine.FailsafeTemp == 85, (val) => {
                EcAntiHallucinationEngine.SetFailsafeTemp(val);
                UpdateEcSelectorPills(ecFailsafeButtons, val);
            }));
            ecFailsafeButtons.Add(CreateEcSelectorPill("88°C (Best)", 88, EcAntiHallucinationEngine.FailsafeTemp == 88, (val) => {
                EcAntiHallucinationEngine.SetFailsafeTemp(val);
                UpdateEcSelectorPills(ecFailsafeButtons, val);
            }));
            ecFailsafeButtons.Add(CreateEcSelectorPill("92°C", 92, EcAntiHallucinationEngine.FailsafeTemp == 92, (val) => {
                EcAntiHallucinationEngine.SetFailsafeTemp(val);
                UpdateEcSelectorPills(ecFailsafeButtons, val);
            }));
            foreach (Border b in ecFailsafeButtons) failGrid.Children.Add(b);
            sp.Children.Add(failGrid);

            return CreateSectionCard("Hysteresis & Acoustic Settings", "Manual control over EC interception and delay windows", sp);
        }

        private Border CreateEcIntegrationCard()
        {
            StackPanel sp = new StackPanel { Margin = new Thickness(0, 4, 0, 4) };

            // 1. DirectX Zero-Hook Gaming Auto-Detection
            btnEcGameToggle = new Border
            {
                Background = EcAntiHallucinationEngine.AutoUnclampGaming ? new SolidColorBrush(Color.FromArgb(40, 0, 122, 255)) : DesignTokens.BrushBgInput,
                BorderBrush = EcAntiHallucinationEngine.AutoUnclampGaming ? DesignTokens.BrushAccentBlue : DesignTokens.BrushBorderSubtle,
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(8),
                Padding = new Thickness(12, 8, 12, 8),
                Margin = new Thickness(0, 0, 0, 12),
                Cursor = Cursors.Hand
            };
            StackPanel gSp = new StackPanel();
            lblEcGameToggleText = new TextBlock
            {
                Text = EcAntiHallucinationEngine.AutoUnclampGaming ? "🎮 3D Game Detection: ON (Auto-Unclamp)" : "🎮 3D Game Detection: OFF",
                FontSize = 11,
                FontWeight = FontWeights.Bold,
                Foreground = EcAntiHallucinationEngine.AutoUnclampGaming ? DesignTokens.BrushAccentBlue : DesignTokens.BrushTextSecondary
            };
            gSp.Children.Add(lblEcGameToggleText);
            gSp.Children.Add(new TextBlock
            {
                Text = "Releases acoustic clamps the moment DirectX/Vulkan 3D frames begin via ETW.",
                FontSize = 9.5,
                Foreground = DesignTokens.BrushTextMuted,
                Margin = new Thickness(0, 2, 0, 0)
            });
            btnEcGameToggle.Child = gSp;
            btnEcGameToggle.MouseDown += (s, e) =>
            {
                EcAntiHallucinationEngine.AutoUnclampGaming = !EcAntiHallucinationEngine.AutoUnclampGaming;
                lblEcGameToggleText.Text = EcAntiHallucinationEngine.AutoUnclampGaming ? "🎮 3D Game Detection: ON (Auto-Unclamp)" : "🎮 3D Game Detection: OFF";
                lblEcGameToggleText.Foreground = EcAntiHallucinationEngine.AutoUnclampGaming ? DesignTokens.BrushAccentBlue : DesignTokens.BrushTextSecondary;
                btnEcGameToggle.Background = EcAntiHallucinationEngine.AutoUnclampGaming ? new SolidColorBrush(Color.FromArgb(40, 0, 122, 255)) : DesignTokens.BrushBgInput;
                btnEcGameToggle.BorderBrush = EcAntiHallucinationEngine.AutoUnclampGaming ? DesignTokens.BrushAccentBlue : DesignTokens.BrushBorderSubtle;
                EcAntiHallucinationEngine.LogDecision("CONFIG: Game auto-detection set to " + (EcAntiHallucinationEngine.AutoUnclampGaming ? "ON" : "OFF"));
            };
            sp.Children.Add(btnEcGameToggle);

            // 2. Windows CPU Boost Tamer (99% State)
            btnEcBoostTamer = new Border
            {
                Background = EcAntiHallucinationEngine.PowerBoostTamed ? new SolidColorBrush(Color.FromArgb(40, 16, 185, 129)) : DesignTokens.BrushBgInput,
                BorderBrush = EcAntiHallucinationEngine.PowerBoostTamed ? DesignTokens.BrushAccentGreen : DesignTokens.BrushBorderSubtle,
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(8),
                Padding = new Thickness(12, 8, 12, 8),
                Margin = new Thickness(0, 0, 0, 12),
                Cursor = Cursors.Hand
            };
            StackPanel bSp = new StackPanel();
            lblEcBoostTamerText = new TextBlock
            {
                Text = EcAntiHallucinationEngine.PowerBoostTamed ? "⚡ CPU Turbo Spikes: TAMED (99% Mode)" : "⚡ CPU Turbo Spikes: DEFAULT (100% Boost)",
                FontSize = 11,
                FontWeight = FontWeights.Bold,
                Foreground = EcAntiHallucinationEngine.PowerBoostTamed ? DesignTokens.BrushAccentGreen : DesignTokens.BrushTextSecondary
            };
            bSp.Children.Add(lblEcBoostTamerText);
            lblEcBoostTamerStatus = new TextBlock
            {
                Text = EcAntiHallucinationEngine.PowerBoostTamed ? "Micro-burst turbo overvolt eliminated. 100% base clock speed preserved." : "Windows Balanced: Aggressive turbo burst enabled.",
                FontSize = 9.5,
                Foreground = DesignTokens.BrushTextMuted,
                Margin = new Thickness(0, 2, 0, 0)
            };
            bSp.Children.Add(lblEcBoostTamerStatus);
            btnEcBoostTamer.Child = bSp;
            btnEcBoostTamer.MouseDown += (s, e) =>
            {
                EcAntiHallucinationEngine.TogglePowerBoostTamer();
                lblEcBoostTamerText.Text = EcAntiHallucinationEngine.PowerBoostTamed ? "⚡ CPU Turbo Spikes: TAMED (99% Mode)" : "⚡ CPU Turbo Spikes: DEFAULT (100% Boost)";
                lblEcBoostTamerText.Foreground = EcAntiHallucinationEngine.PowerBoostTamed ? DesignTokens.BrushAccentGreen : DesignTokens.BrushTextSecondary;
                lblEcBoostTamerStatus.Text = EcAntiHallucinationEngine.PowerBoostTamed ? "Micro-burst turbo overvolt eliminated. 100% base clock speed preserved." : "Windows Balanced: Aggressive turbo burst enabled.";
                btnEcBoostTamer.Background = EcAntiHallucinationEngine.PowerBoostTamed ? new SolidColorBrush(Color.FromArgb(40, 16, 185, 129)) : DesignTokens.BrushBgInput;
                btnEcBoostTamer.BorderBrush = EcAntiHallucinationEngine.PowerBoostTamed ? DesignTokens.BrushAccentGreen : DesignTokens.BrushBorderSubtle;
            };
            sp.Children.Add(btnEcBoostTamer);

            // 3. Heatsink Thermal Saturation Bar
            lblHeatsinkSatText = new TextBlock
            {
                Text = "Heatsink Thermal Saturation: --% (--°C EMA)",
                FontSize = 10,
                FontWeight = FontWeights.SemiBold,
                Foreground = DesignTokens.BrushTextSecondary,
                Margin = new Thickness(0, 4, 0, 4)
            };
            sp.Children.Add(lblHeatsinkSatText);

            meterHeatsinkSaturation = new SegmentedMeter(10, DesignTokens.AccentCyan, 28, 4);
            meterHeatsinkSaturation.Margin = new Thickness(0, 0, 0, 12);
            sp.Children.Add(meterHeatsinkSaturation);

            // 4. One-Click EC Hardware Reset
            Border btnResetEc = CreateActionButton("🔄 Reload Baseline EC Hardware Registers", () =>
            {
                HardwareBridge.ResetToAuto();
                EcAntiHallucinationEngine.LogDecision("EC RESET: Reloaded factory EC baseline firmware tables");
                if (lblStatusMsg != null)
                {
                    lblStatusMsg.Text = "EC registers reset to factory baseline";
                    lblStatusMsg.Foreground = DesignTokens.BrushAccentGreen;
                }
            });
            sp.Children.Add(btnResetEc);

            return CreateSectionCard("Gaming & Silicon Integration", "Zero-hook ETW detection, CPU overvolt tamer, and EC reset", sp);
        }

        private Border CreateEcDecisionLogCard()
        {
            StackPanel sp = new StackPanel { Margin = new Thickness(0, 4, 0, 4) };

            Border logBox = new Border
            {
                Background = DesignTokens.BrushBgInput,
                BorderBrush = DesignTokens.BrushBorderSubtle,
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(8),
                Padding = new Thickness(10),
                Height = 220
            };

            ScrollViewer sv = new ScrollViewer
            {
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled
            };

            txtDecisionLog = new TextBlock
            {
                Text = "Initializing Decision Stream...",
                FontSize = 9.5,
                FontFamily = new FontFamily("Consolas, Courier New"),
                Foreground = DesignTokens.BrushTextSecondary,
                TextWrapping = TextWrapping.Wrap
            };
            sv.Content = txtDecisionLog;
            logBox.Child = sv;
            sp.Children.Add(logBox);

            UniformGrid btnGrid = new UniformGrid { Columns = 2, Margin = new Thickness(0, 10, 0, 0) };

            Border btnClear = CreateActionButton("Clear Log", () =>
            {
                EcAntiHallucinationEngine.ClearLogs();
                if (txtDecisionLog != null) txtDecisionLog.Text = "Decision stream cleared by user";
            });
            btnGrid.Children.Add(btnClear);

            Border btnCopy = CreateActionButton("Copy Stream", () =>
            {
                try
                {
                    Clipboard.SetText(string.Join("\n", EcAntiHallucinationEngine.GetRecentLogs(50).ToArray()));
                    if (lblStatusMsg != null)
                    {
                        lblStatusMsg.Text = "Decision stream copied to clipboard";
                        lblStatusMsg.Foreground = DesignTokens.BrushAccentGreen;
                    }
                }
                catch { }
            });
            btnGrid.Children.Add(btnCopy);

            sp.Children.Add(btnGrid);

            return CreateSectionCard("Decision Stream & Telemetry Log", "Real-time classifier state, spike interceptions, and EC commands", sp);
        }

        private Border CreateEcSelectorPill(string label, int value, bool isSelected, Action<int> onClick)
        {
            Border b = new Border
            {
                Background = isSelected ? DesignTokens.BrushAccentBlue : DesignTokens.BrushBgInput,
                BorderBrush = isSelected ? DesignTokens.BrushAccentBlue : DesignTokens.BrushBorderSubtle,
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(6),
                Padding = new Thickness(4, 6, 4, 6),
                Margin = new Thickness(2),
                Cursor = Cursors.Hand,
                Tag = value
            };

            TextBlock tb = new TextBlock
            {
                Text = label,
                FontSize = 9.5,
                FontWeight = isSelected ? FontWeights.Bold : FontWeights.SemiBold,
                Foreground = isSelected ? Brushes.White : DesignTokens.BrushTextSecondary,
                HorizontalAlignment = HorizontalAlignment.Center
            };
            b.Child = tb;

            b.MouseDown += (s, e) => { onClick(value); };
            return b;
        }

        private void UpdateEcSelectorPills(List<Border> pills, int selectedVal)
        {
            foreach (Border b in pills)
            {
                if (b.Tag != null && (int)b.Tag == selectedVal)
                {
                    b.Background = DesignTokens.BrushAccentBlue;
                    b.BorderBrush = DesignTokens.BrushAccentBlue;
                    TextBlock tb = b.Child as TextBlock;
                    if (tb != null)
                    {
                        tb.Foreground = Brushes.White;
                        tb.FontWeight = FontWeights.Bold;
                    }
                }
                else
                {
                    b.Background = DesignTokens.BrushBgInput;
                    b.BorderBrush = DesignTokens.BrushBorderSubtle;
                    TextBlock tb = b.Child as TextBlock;
                    if (tb != null)
                    {
                        tb.Foreground = DesignTokens.BrushTextSecondary;
                        tb.FontWeight = FontWeights.SemiBold;
                    }
                }
            }
        }

        private void UpdateDashboardEcGuardUi()
        {
            bool enabled = EcAntiHallucinationEngine.IsEnabled;
            if (lblDashboardEcGuardIndicator != null)
            {
                lblDashboardEcGuardIndicator.Text = enabled ? "🛡️ EC Guard: ACTIVE" : "⚪ EC Guard: DISABLED";
                lblDashboardEcGuardIndicator.Foreground = enabled ? DesignTokens.BrushAccentGreen : DesignTokens.BrushTextMuted;
            }
            if (lblDashboardEcGuardStatus != null)
            {
                if (!enabled)
                {
                    lblDashboardEcGuardStatus.Text = "Raw EC Control (No Interception)";
                    lblDashboardEcGuardStatus.Foreground = DesignTokens.BrushTextMuted;
                }
                else if (EcAntiHallucinationEngine.IsCurrentlyClamped)
                {
                    lblDashboardEcGuardStatus.Text = "Silent Mode (Anti-Revving Active)";
                    lblDashboardEcGuardStatus.Foreground = DesignTokens.BrushAccentCyan;
                }
                else
                {
                    lblDashboardEcGuardStatus.Text = "Cooling Released";
                    lblDashboardEcGuardStatus.Foreground = DesignTokens.BrushAccentAmber;
                }
            }
            if (btnToolEcGuard != null)
            {
                TextBlock tb = btnToolEcGuard.Child as TextBlock;
                if (tb != null)
                {
                    tb.Text = enabled ? "🛡️ EC Guard: ON" : "⚪ EC Guard: OFF";
                    tb.Foreground = enabled ? DesignTokens.BrushAccentGreen : DesignTokens.BrushTextSecondary;
                }
            }
            if (btnEcMasterToggle != null && lblEcMasterToggleText != null)
            {
                btnEcMasterToggle.Background = enabled ? new SolidColorBrush(Color.FromArgb(50, 16, 185, 129)) : DesignTokens.BrushBgInput;
                btnEcMasterToggle.BorderBrush = enabled ? DesignTokens.BrushAccentGreen : DesignTokens.BrushBorderSubtle;
                lblEcMasterToggleText.Text = enabled ? "🛡️ EC Anti-Hallucination Guard: ENABLED" : "⚪ EC Anti-Hallucination Guard: DISABLED";
                lblEcMasterToggleText.Foreground = enabled ? Brushes.White : DesignTokens.BrushTextSecondary;
            }
        }

        private void UpdateEcGuardView(HardwareBridge.SensorData d)
        {
            if (lblEcRawDie != null)
            {
                lblEcRawDie.Text = string.Format("{0}°C", d.CpuTemp);
                if (d.CpuTemp >= 80) lblEcRawDie.Foreground = DesignTokens.BrushAccentRed;
                else if (d.CpuTemp >= 68) lblEcRawDie.Foreground = DesignTokens.BrushAccentAmber;
                else lblEcRawDie.Foreground = DesignTokens.BrushTextPrimary;
            }
            if (lblEcThermalEma != null)
            {
                lblEcThermalEma.Text = string.Format("{0:F1}°C", EcAntiHallucinationEngine.FilteredThermalEma);
            }
            if (lblEcSpikesCount != null)
            {
                lblEcSpikesCount.Text = string.Format("{0} Spikes", EcAntiHallucinationEngine.InterceptedSpikeCount);
            }
            if (lblEcAcousticHold != null)
            {
                lblEcAcousticHold.Text = EcAntiHallucinationEngine.IsCurrentlyClamped 
                    ? string.Format("{0}% Duty", EcAntiHallucinationEngine.AcousticClampDuty)
                    : "Released";
                lblEcAcousticHold.Foreground = EcAntiHallucinationEngine.IsCurrentlyClamped 
                    ? DesignTokens.BrushAccentGreen 
                    : DesignTokens.BrushAccentAmber;
            }
            if (lblEcClassifierState != null)
            {
                lblEcClassifierState.Text = EcAntiHallucinationEngine.CurrentClassifierState;
            }
            if (meterHeatsinkSaturation != null && lblHeatsinkSatText != null)
            {
                meterHeatsinkSaturation.SetValue(EcAntiHallucinationEngine.HeatsinkSaturationPct);
                lblHeatsinkSatText.Text = string.Format("Heatsink Thermal Saturation: {0:F0}% ({1:F1}°C EMA)", 
                    EcAntiHallucinationEngine.HeatsinkSaturationPct, EcAntiHallucinationEngine.FilteredThermalEma);
            }
            if (txtDecisionLog != null)
            {
                List<string> logs = EcAntiHallucinationEngine.GetRecentLogs(10);
                txtDecisionLog.Text = string.Join("\n", logs.ToArray());
            }
        }
    }
}