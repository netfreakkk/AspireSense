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
        // PAGE 4: SYSTEM HEALTH & SPECS VIEW
        // ==========================================
        private Grid CreateHealthView()
        {
            Grid g = new Grid { Margin = new Thickness(35, 12, 35, 12) };
            g.RowDefinitions.Add(new RowDefinition { Height = new GridLength(88) }); // Top Silicon Ribbon
            g.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) }); // 3 Analytics Cards

            // 1. Top Chassis & Silicon Matrix Ribbon
            Border ribbon = CreateSiliconRibbon();
            Grid.SetRow(ribbon, 0);
            g.Children.Add(ribbon);

            // 2. Lower Analytics Matrix (3 Equal Minimalist Cards)
            Grid lowerGrid = new Grid { Margin = new Thickness(0, 12, 0, 0) };
            lowerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            lowerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            lowerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            // Card 1: RTX 4050 Telemetry Card
            Border gpuCard = CreateGpuHealthCard();
            Grid.SetColumn(gpuCard, 0);
            lowerGrid.Children.Add(gpuCard);

            // Card 2: Memory & Workloads Card
            Border ramCard = CreateMemoryHealthCard();
            Grid.SetColumn(ramCard, 1);
            lowerGrid.Children.Add(ramCard);

            // Card 3: Power, Storage & Diagnostics Card
            Border powerCard = CreatePowerHealthCard();
            Grid.SetColumn(powerCard, 2);
            lowerGrid.Children.Add(powerCard);

            Grid.SetRow(lowerGrid, 1);
            g.Children.Add(lowerGrid);

            return g;
        }

        private Border CreateSiliconRibbon()
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

            TextBlock dummy;
            ug.Children.Add(CreateRibbonTile("Processor", HardwareBridge.ChassisCpuName, string.Format("{0} Cores • 4.8 GHz Max", Environment.ProcessorCount), out dummy));
            ug.Children.Add(CreateRibbonTile("Graphics Silicon", HardwareBridge.ChassisGpuName, "PCIe Gen 4.0 x8 Bus", out dummy));
            string ramDesc = string.Format("{0:F0} GB Dual-Channel", HardwareBridge.ChassisTotalRamGb > 0 ? HardwareBridge.ChassisTotalRamGb : 16.0);
            ug.Children.Add(CreateRibbonTile("Memory Architecture", ramDesc, "High-Speed Memory Bus", out dummy));
            ug.Children.Add(CreateRibbonTile("Firmware Platform", HardwareBridge.ChassisBiosVersion, "EC Controller " + HardwareBridge.ChassisEcVersion, out dummy));
            ug.Children.Add(CreateRibbonTile("System Uptime", "0d 00h 00m 00s", HardwareBridge.ChassisOs, out lblHealthUptime));

            ribbon.Child = ug;
            return ribbon;
        }

        private Border CreateRibbonTile(string category, string primary, string secondary, out TextBlock dynamicTb)
        {
            Border b = new Border
            {
                Background = DesignTokens.BrushBgInput,
                BorderBrush = DesignTokens.BrushBorderSubtle,
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(6),
                Padding = new Thickness(10, 7, 10, 7),
                Margin = new Thickness(3)
            };
            StackPanel sp = new StackPanel();
            sp.Children.Add(new TextBlock
            {
                Text = category,
                FontSize = 10,
                FontWeight = FontWeights.SemiBold,
                Foreground = DesignTokens.BrushTextSecondary
            });

            dynamicTb = new TextBlock
            {
                Text = primary,
                FontSize = 12,
                FontWeight = FontWeights.Bold,
                Foreground = DesignTokens.BrushTextPrimary,
                Margin = new Thickness(0, 2, 0, 1),
                TextTrimming = TextTrimming.CharacterEllipsis
            };
            sp.Children.Add(dynamicTb);

            sp.Children.Add(new TextBlock
            {
                Text = secondary,
                FontSize = 10,
                Foreground = DesignTokens.BrushTextMuted,
                TextTrimming = TextTrimming.CharacterEllipsis
            });

            b.Child = sp;
            return b;
        }

        private Border CreateGpuHealthCard()
        {
            StackPanel sp = new StackPanel();

            // VRAM Section
            sp.Children.Add(CreateMetricHeader("VRAM Dedicated Allocation", out lblHealthVramText, "0.0 / 6.0 GB (0%)", DesignTokens.AccentCyan));
            meterHealthVram = new SegmentedMeter(14, DesignTokens.AccentCyan, 16, 5);
            sp.Children.Add(meterHealthVram);

            // Clock Frequencies Dual Tile
            UniformGrid clkGrid = new UniformGrid { Columns = 2, Margin = new Thickness(0, 14, 0, 12) };
            clkGrid.Children.Add(CreateClockTile("Core Clock", out lblHealthGpuCore, "2,130", "MHz"));
            clkGrid.Children.Add(CreateClockTile("VRAM Clock", out lblHealthGpuMem, "8,001", "MHz"));
            sp.Children.Add(clkGrid);

            // Telemetry Status Lines
            StackPanel statSp = new StackPanel { Margin = new Thickness(0, 4, 0, 0) };
            statSp.Children.Add(CreateStatusLine("Throttle Reason:", out lblHealthGpuThrottle, "Optimal (Idle)", DesignTokens.AccentGreen));
            statSp.Children.Add(CreateStatusLine("Thermal Headroom:", out lblHealthGpuHeadroom, "+48°C Margin (to 87°C)", DesignTokens.AccentCyan));
            statSp.Children.Add(CreateStatusLine("Bus Interface:", "PCIe Gen 4.0 x8 Active", DesignTokens.TextSecondaryColor));
            sp.Children.Add(statSp);

            Border card = CreateMinimalCard("RTX 4050 Accelerator", "AD107M", sp);
            card.Margin = new Thickness(0, 0, 6, 0);
            return card;
        }

        private Border CreateMemoryHealthCard()
        {
            StackPanel sp = new StackPanel();

            // RAM Section
            sp.Children.Add(CreateMetricHeader("Physical RAM Utilization", out lblHealthRamText, "-- / -- GB (--%)", DesignTokens.AccentPurple));
            meterHealthRam = new SegmentedMeter(14, DesignTokens.AccentPurple, 16, 5);
            sp.Children.Add(meterHealthRam);

            // Top Memory Consumers
            TextBlock topHeader = new TextBlock
            {
                Text = "Top Active Consumers (RAM)",
                FontSize = 10,
                FontWeight = FontWeights.SemiBold,
                Foreground = DesignTokens.BrushTextSecondary,
                Margin = new Thickness(0, 16, 0, 6)
            };
            sp.Children.Add(topHeader);

            sp.Children.Add(CreateProcessItem("#1", out lblTopProc1));
            sp.Children.Add(CreateProcessItem("#2", out lblTopProc2));
            sp.Children.Add(CreateProcessItem("#3", out lblTopProc3));

            // Instant Purge Button
            Border btnPurge = CreateActionButton("⚡ Purge Unused Memory", () =>
            {
                HardwareBridge.OptimizeMemory();
                if (lblStatusMsg != null)
                {
                    lblStatusMsg.Text = "Memory Purged: Working sets trimmed safely";
                    lblStatusMsg.Foreground = DesignTokens.BrushAccentGreen;
                }
            });
            sp.Children.Add(btnPurge);

            Border card = CreateMinimalCard("Memory & Processes", "32 GB DDR5", sp);
            card.Margin = new Thickness(3, 0, 3, 0);
            return card;
        }

        private Border CreatePowerHealthCard()
        {
            StackPanel sp = new StackPanel();

            // Battery Section
            sp.Children.Add(CreateMetricHeader("Battery Power", out lblHealthBatteryText, "100% (AC Mains)", DesignTokens.AccentGreen));
            meterHealthBattery = new SegmentedMeter(12, DesignTokens.AccentGreen, 18, 5);
            sp.Children.Add(meterHealthBattery);

            lblHealthBatteryWear = new TextBlock
            {
                Text = "Design: 52.5 Wh | Current: 53.8 Wh (0.0% Wear)",
                FontSize = 10,
                Foreground = DesignTokens.BrushTextSecondary,
                Margin = new Thickness(0, 4, 0, 10)
            };
            sp.Children.Add(lblHealthBatteryWear);

            // Storage NVMe Section
            sp.Children.Add(CreateMetricHeader("Primary Storage (C: NVMe)", out lblHealthDiskText, "Checking capacity...", DesignTokens.AccentCyan));
            meterHealthDisk = new SegmentedMeter(12, DesignTokens.AccentCyan, 18, 5);
            sp.Children.Add(meterHealthDisk);

            // CPU Headroom
            StackPanel statSp = new StackPanel { Margin = new Thickness(0, 12, 0, 0) };
            statSp.Children.Add(CreateStatusLine("CPU Silicon Headroom:", out lblHealthCpuHeadroom, "+55°C Margin (to 100°C TJMax)", DesignTokens.AccentGreen));
            sp.Children.Add(statSp);

            // Export Diagnostics Button
            Border btnExport = CreateActionButton("📋 Copy Diagnostics Report", () =>
            {
                CopyDiagnosticsReport();
            });
            sp.Children.Add(btnExport);

            Border card = CreateMinimalCard("Power & Storage Health", "Optimal", sp);
            card.Margin = new Thickness(6, 0, 0, 0);
            return card;
        }

        private Border CreateMinimalCard(string title, string badge, UIElement content)
        {
            Border card = new Border
            {
                Background = DesignTokens.BrushBgCard,
                CornerRadius = new CornerRadius(10),
                BorderBrush = DesignTokens.BrushBorderSubtle,
                BorderThickness = new Thickness(1),
                Padding = new Thickness(16, 14, 16, 14)
            };

            Grid header = new Grid { Margin = new Thickness(0, 0, 0, 12) };
            header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            TextBlock t = new TextBlock
            {
                Text = title,
                FontSize = 12.5,
                FontWeight = FontWeights.Bold,
                Foreground = DesignTokens.BrushTextPrimary,
                VerticalAlignment = VerticalAlignment.Center
            };
            Grid.SetColumn(t, 0);
            header.Children.Add(t);

            Border bPill = new Border
            {
                Background = DesignTokens.BrushBgInput,
                BorderBrush = DesignTokens.BrushBorderSubtle,
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(4),
                Padding = new Thickness(8, 2, 8, 2)
            };
            bPill.Child = new TextBlock
            {
                Text = badge,
                FontSize = 10,
                FontWeight = FontWeights.SemiBold,
                Foreground = DesignTokens.BrushAccentCyan
            };
            Grid.SetColumn(bPill, 1);
            header.Children.Add(bPill);

            StackPanel root = new StackPanel();
            root.Children.Add(header);
            root.Children.Add(content);
            card.Child = root;

            return card;
        }

        private Grid CreateMetricHeader(string label, out TextBlock valTb, string defaultVal, Color valColor)
        {
            Grid g = new Grid { Margin = new Thickness(0, 4, 0, 6) };
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            TextBlock lTb = new TextBlock { Text = label, FontSize = 10, FontWeight = FontWeights.SemiBold, Foreground = DesignTokens.BrushTextSecondary, VerticalAlignment = VerticalAlignment.Center };
            Grid.SetColumn(lTb, 0);
            g.Children.Add(lTb);

            valTb = new TextBlock { Text = defaultVal, FontSize = 11, FontWeight = FontWeights.Bold, Foreground = new SolidColorBrush(valColor), VerticalAlignment = VerticalAlignment.Center };
            Grid.SetColumn(valTb, 1);
            g.Children.Add(valTb);

            return g;
        }

        private Border CreateClockTile(string label, out TextBlock valTb, string defVal, string unit)
        {
            Border b = new Border
            {
                Background = DesignTokens.BrushBgInput,
                BorderBrush = DesignTokens.BrushBorderSubtle,
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(6),
                Padding = new Thickness(10, 8, 10, 8),
                Margin = new Thickness(3)
            };
            StackPanel sp = new StackPanel();
            sp.Children.Add(new TextBlock { Text = label, FontSize = 10, FontWeight = FontWeights.SemiBold, Foreground = DesignTokens.BrushTextSecondary });

            StackPanel numSp = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 2, 0, 0) };
            valTb = new TextBlock { Text = defVal, FontSize = 20, FontWeight = FontWeights.Bold, Foreground = DesignTokens.BrushTextPrimary };
            numSp.Children.Add(valTb);
            numSp.Children.Add(new TextBlock { Text = " " + unit, FontSize = 10.5, FontWeight = FontWeights.SemiBold, Foreground = DesignTokens.BrushAccentCyan, VerticalAlignment = VerticalAlignment.Bottom, Margin = new Thickness(0, 0, 0, 3) });
            sp.Children.Add(numSp);

            b.Child = sp;
            return b;
        }

        private Grid CreateStatusLine(string label, out TextBlock valTb, string defVal, Color valColor)
        {
            Grid g = new Grid { Margin = new Thickness(0, 3, 0, 3) };
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(135) });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            TextBlock lTb = new TextBlock { Text = label, FontSize = 10, FontWeight = FontWeights.SemiBold, Foreground = DesignTokens.BrushTextSecondary, VerticalAlignment = VerticalAlignment.Center };
            Grid.SetColumn(lTb, 0);
            g.Children.Add(lTb);

            valTb = new TextBlock { Text = defVal, FontSize = 10, FontWeight = FontWeights.SemiBold, Foreground = new SolidColorBrush(valColor), VerticalAlignment = VerticalAlignment.Center };
            Grid.SetColumn(valTb, 1);
            g.Children.Add(valTb);

            return g;
        }

        private Grid CreateStatusLine(string label, string val, Color valColor)
        {
            TextBlock dummy;
            return CreateStatusLine(label, out dummy, val, valColor);
        }

        private Border CreateProcessItem(string rank, out TextBlock txt)
        {
            Border b = new Border
            {
                Background = DesignTokens.BrushBgInput,
                BorderBrush = DesignTokens.BrushBorderSubtle,
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(5),
                Padding = new Thickness(10, 6, 10, 6),
                Margin = new Thickness(0, 2, 0, 2)
            };
            Grid g = new Grid();
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(24) });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            TextBlock rTb = new TextBlock { Text = rank, FontSize = 10, FontWeight = FontWeights.Bold, Foreground = DesignTokens.BrushAccentPurple, VerticalAlignment = VerticalAlignment.Center };
            Grid.SetColumn(rTb, 0);
            g.Children.Add(rTb);

            txt = new TextBlock { Text = "Analyzing...", FontSize = 10.5, FontWeight = FontWeights.Medium, Foreground = DesignTokens.BrushTextPrimary, VerticalAlignment = VerticalAlignment.Center };
            Grid.SetColumn(txt, 1);
            g.Children.Add(txt);

            b.Child = g;
            return b;
        }

        private Border CreateActionButton(string label, Action action)
        {
            Border b = new Border
            {
                Background = DesignTokens.BrushBgInput,
                BorderBrush = DesignTokens.BrushBorderSubtle,
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(6),
                Padding = new Thickness(12, 8, 12, 8),
                Margin = new Thickness(0, 10, 0, 0),
                Cursor = Cursors.Hand
            };
            TextBlock t = new TextBlock
            {
                Text = label,
                FontSize = 10.5,
                FontWeight = FontWeights.Bold,
                Foreground = DesignTokens.BrushAccentCyan,
                HorizontalAlignment = HorizontalAlignment.Center
            };
            b.Child = t;
            b.MouseEnter += (s, e) => {
                b.Background = DesignTokens.BrushBgCardHover;
                b.BorderBrush = DesignTokens.BrushBorderSubtleLight;
                t.Foreground = Brushes.White;
            };
            b.MouseLeave += (s, e) => {
                b.Background = DesignTokens.BrushBgInput;
                b.BorderBrush = DesignTokens.BrushBorderSubtle;
                t.Foreground = DesignTokens.BrushAccentCyan;
            };
            b.MouseDown += (s, e) => { action(); };
            return b;
        }

        private void CopyDiagnosticsReport()
        {
            try
            {
                HardwareBridge.SensorData d = latestSensorData ?? HardwareBridge.ReadSensors(cpuCounter);
                string report = string.Format(
                    "================ ASPIRESENSE SYSTEM DIAGNOSTICS ================\n" +
                    "Timestamp:        {0}\n" +
                    "Chassis Model:    {1}\n" +
                    "Processor:        {2} ({3} Cores)\n" +
                    "Graphics Card:    {4}\n" +
                    "BIOS Version:     {5} | EC: {6}\n" +
                    "Operating System: {7}\n" +
                    "System Uptime:    {8}d {9:D2}h {10:D2}m {11:D2}s\n\n" +
                    "--- LIVE HARDWARE SENSORS ---\n" +
                    "CPU Temperature:  {12}°C (Headroom to 100°C TJMax: +{13}°C)\n" +
                    "GPU Temperature:  {14}°C (Headroom to 87°C Limit: +{15}°C)\n" +
                    "GPU Core Clock:   {16} MHz | Mem Clock: {17} MHz\n" +
                    "GPU VRAM In Use:  {18:F1} / {19:F1} GB ({20:F0}%)\n" +
                    "GPU Throttle:     {21}\n" +
                    "System RAM:       {22:F1} / {23:F1} GB ({24:F0}%)\n" +
                    "Battery Status:   {25}% ({26})\n" +
                    "Battery Health:   {27:F1}% ({28:F1}% Degradation)\n" +
                    "=================================================================\n",
                    DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"),
                    HardwareBridge.ChassisModel,
                    HardwareBridge.ChassisCpuName, Environment.ProcessorCount,
                    HardwareBridge.ChassisGpuName,
                    HardwareBridge.ChassisBiosVersion, HardwareBridge.ChassisEcVersion,
                    HardwareBridge.ChassisOs,
                    d.SystemUptime.Days, d.SystemUptime.Hours, d.SystemUptime.Minutes, d.SystemUptime.Seconds,
                    d.CpuTemp, Math.Max(0, 100 - d.CpuTemp),
                    d.GpuTemp, Math.Max(0, 87 - d.GpuTemp),
                    d.GpuCoreClock, d.GpuMemClock,
                    d.VramUsedGb, d.VramTotalGb, d.VramUsage,
                    d.GpuThrottleStatus,
                    d.UsedRamGb, d.TotalRamGb, d.RamUsage,
                    d.BatteryPercent, d.IsAcPlugged ? "AC Mains" : "On Battery",
                    d.BatteryHealthPct, Math.Max(0.0, 100.0 - d.BatteryHealthPct)
                );
                Clipboard.SetText(report);
                if (lblStatusMsg != null)
                {
                    lblStatusMsg.Text = "Diagnostic report copied to clipboard";
                    lblStatusMsg.Foreground = DesignTokens.BrushAccentGreen;
                }
            }
            catch (Exception ex)
            {
                if (lblStatusMsg != null) lblStatusMsg.Text = "Export error: " + ex.Message;
            }
        }

    }
}