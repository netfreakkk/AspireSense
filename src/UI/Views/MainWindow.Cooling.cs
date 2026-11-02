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
        // PAGE 2: COOLING CONTROL STUDIO VIEW
        // ==========================================
        private Grid CreateCoolingView()
        {
            Grid g = new Grid { Margin = new Thickness(35, 12, 35, 12) };
            g.RowDefinitions.Add(new RowDefinition { Height = new GridLength(145) });
            g.RowDefinitions.Add(new RowDefinition { Height = new GridLength(76) });
            g.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

            // Top: Dual Detailed Fan Cards
            Grid topRow = new Grid();
            topRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            topRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            // CPU Dedicated Blower Card
            string cpuCoolingSub = (!string.IsNullOrEmpty(HardwareBridge.ChassisCpuName) ? HardwareBridge.ChassisCpuName : "CPU Package") + " Thermal Extraction";
            StackPanel cpuSp = new StackPanel { Margin = new Thickness(0, 4, 0, 0) };
            lblCoolingCpuRpm = new TextBlock { Text = "0 RPM (0%)", FontSize = 24, FontWeight = FontWeights.Bold, Foreground = DesignTokens.BrushAccentCyan };
            meterCoolingCpu = new SegmentedMeter(20, DesignTokens.AccentCyan, 12, 6);
            meterCoolingCpu.Margin = new Thickness(0, 8, 0, 8);
            TextBlock cpuSub = new TextBlock { Text = "Dynamic curve active: Ramps smoothly based on CPU package temp", FontSize = 10, Foreground = DesignTokens.BrushTextSecondary };
            cpuSp.Children.Add(lblCoolingCpuRpm);
            cpuSp.Children.Add(meterCoolingCpu);
            cpuSp.Children.Add(cpuSub);

            Border cpuCard = CreateSectionCard("CPU Dedicated Cooling Blower", cpuCoolingSub, cpuSp);
            Grid.SetColumn(cpuCard, 0);
            topRow.Children.Add(cpuCard);

            // GPU Dedicated Blower Card
            string gpuCoolingSub = (!string.IsNullOrEmpty(HardwareBridge.ChassisGpuName) ? HardwareBridge.ChassisGpuName : "GPU Silicon") + " Thermal Extraction";
            StackPanel gpuSp = new StackPanel { Margin = new Thickness(0, 4, 0, 0) };
            lblCoolingGpuRpm = new TextBlock { Text = "0 RPM (0%)", FontSize = 24, FontWeight = FontWeights.Bold, Foreground = DesignTokens.BrushAccentBlue };
            meterCoolingGpu = new SegmentedMeter(20, DesignTokens.AccentBlue, 12, 6);
            meterCoolingGpu.Margin = new Thickness(0, 8, 0, 8);
            TextBlock gpuSub = new TextBlock { Text = "Zero-RPM passive mode: Blower completely idles when GPU is cold", FontSize = 10, Foreground = DesignTokens.BrushTextSecondary };
            gpuSp.Children.Add(lblCoolingGpuRpm);
            gpuSp.Children.Add(meterCoolingGpu);
            gpuSp.Children.Add(gpuSub);

            Border gpuCard = CreateSectionCard("GPU Dedicated Cooling Blower", gpuCoolingSub, gpuSp);
            gpuCard.Margin = new Thickness(14, 0, 0, 0);
            Grid.SetColumn(gpuCard, 1);
            topRow.Children.Add(gpuCard);

            Grid.SetRow(topRow, 0);
            g.Children.Add(topRow);

            // Middle: 60-Second Turbo Heat Flush Card
            Border flushCard = new Border
            {
                Background = DesignTokens.BrushBgCard,
                CornerRadius = new CornerRadius(10),
                BorderBrush = DesignTokens.BrushBorderSubtle,
                BorderThickness = new Thickness(1),
                Margin = new Thickness(0, 10, 0, 0),
                Padding = new Thickness(16, 12, 16, 12)
            };
            Grid flushGrid = new Grid();
            flushGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            flushGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(240) });

            StackPanel fLeft = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            fLeft.Children.Add(new TextBlock
            {
                Text = "❄️ 60-Second Turbo Heat Flush",
                FontSize = 12.5,
                FontWeight = FontWeights.Bold,
                Foreground = DesignTokens.BrushTextPrimary
            });
            fLeft.Children.Add(new TextBlock
            {
                Text = "Instantly cycles blowers at 100% duty for 60 seconds to rapidly purge heat pipes, then auto-restores.",
                FontSize = 10,
                Foreground = DesignTokens.BrushTextSecondary,
                Margin = new Thickness(0, 2, 0, 0)
            });
            Grid.SetColumn(fLeft, 0);
            flushGrid.Children.Add(fLeft);

            btnHeatFlush = new Border
            {
                Background = DesignTokens.BrushAccentBlue,
                BorderBrush = DesignTokens.BrushBorderFocus,
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(6),
                Height = 34,
                Cursor = Cursors.Hand,
                VerticalAlignment = VerticalAlignment.Center
            };
            lblHeatFlushText = new TextBlock
            {
                Text = "❄️ Engage Turbo Flush",
                FontSize = 11,
                FontWeight = FontWeights.Bold,
                Foreground = Brushes.White,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            };
            btnHeatFlush.Child = lblHeatFlushText;
            btnHeatFlush.MouseEnter += (s, e) => { btnHeatFlush.Background = new SolidColorBrush(Color.FromRgb(2, 132, 199)); };
            btnHeatFlush.MouseLeave += (s, e) => { btnHeatFlush.Background = DesignTokens.BrushAccentBlue; };
            btnHeatFlush.MouseDown += (s, e) =>
            {
                if (heatFlushRemaining > 0)
                {
                    heatFlushRemaining = 0;
                    ApplyMode(0, "Auto (Balanced adaptive)");
                    lblHeatFlushText.Text = "❄️ Engage Turbo Flush";
                }
                else
                {
                    heatFlushRemaining = 60;
                    ApplyMode(1, "Max (60s Turbo Heat Flush Engaged)");
                    lblHeatFlushText.Text = "❄️ Flushing... (60s Left)";
                }
            };
            Grid.SetColumn(btnHeatFlush, 1);
            flushGrid.Children.Add(btnHeatFlush);

            flushCard.Child = flushGrid;
            Grid.SetRow(flushCard, 1);
            g.Children.Add(flushCard);

            // Bottom: Fan Curve Architecture & Fine Override
            string curveSub = (HardwareBridge.DetectedVendor == HardwareVendor.Asus)
                ? "ASUS TUF / ROG ACPI Firmware dynamic thermal curve"
                : (HardwareBridge.DetectedVendor == HardwareVendor.Lenovo)
                    ? "Lenovo Vantage EC dynamic thermal curve"
                    : (HardwareBridge.DetectedVendor == HardwareVendor.Hp)
                        ? "HP Omen / Victus BIOS thermal control"
                        : (HardwareBridge.DetectedVendor == HardwareVendor.Dell)
                            ? "Dell Alienware / G-Series WMAX dynamic curve"
                            : "Embedded Controller dynamic 4-stage curve";

            StackPanel botSp = new StackPanel { Margin = new Thickness(0, 4, 0, 0) };

            TextBlock intro = new TextBlock
            {
                Text = "EC hardware monitors temperatures continuously and modulates blower speed across 4 calibrated thermal stages:",
                FontSize = 10.5,
                Foreground = DesignTokens.BrushTextSecondary,
                Margin = new Thickness(0, 0, 0, 10)
            };
            botSp.Children.Add(intro);

            curveCards.Clear();
            UniformGrid curveGrid = new UniformGrid { Columns = 4, Margin = new Thickness(0, 0, 0, 14) };
            curveCards.Add(CreateCurveStep("Stage 1 (Idle)", "< 50 °C", "28% Duty (Whisper Quiet)"));
            curveCards.Add(CreateCurveStep("Stage 2 (Cruise)", "50 - 70 °C", "50% Duty (Balanced Daily)"));
            curveCards.Add(CreateCurveStep("Stage 3 (Gaming)", "70 - 85 °C", "66% Duty (High Airflow)"));
            curveCards.Add(CreateCurveStep("Stage 4 (Peak)", "> 85 °C", "100% Duty (Full Turbo Lock)"));

            foreach (Border cCard in curveCards)
            {
                curveGrid.Children.Add(cCard);
            }
            botSp.Children.Add(curveGrid);

            // Dedicated Custom Fan Slider
            Grid sliderRow = new Grid { Margin = new Thickness(0, 4, 0, 0) };
            sliderRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(160) });
            sliderRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            sliderRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(60) });
            sliderRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(160) });

            TextBlock lblDed = new TextBlock { Text = "Fine Manual Override:", FontSize = 11, FontWeight = FontWeights.Bold, Foreground = DesignTokens.BrushTextPrimary, VerticalAlignment = VerticalAlignment.Center };
            Grid.SetColumn(lblDed, 0);
            sliderRow.Children.Add(lblDed);

            sliderDedicatedCustom = new Slider { Minimum = 30, Maximum = 100, Value = 80, TickFrequency = 5, VerticalAlignment = VerticalAlignment.Center };
            sliderDedicatedCustom.ValueChanged += (s, e) => { lblDedicatedCustomVal.Text = string.Format("{0:F0}%", sliderDedicatedCustom.Value); };
            Grid.SetColumn(sliderDedicatedCustom, 1);
            sliderRow.Children.Add(sliderDedicatedCustom);

            lblDedicatedCustomVal = new TextBlock { Text = "80%", FontSize = 12, FontWeight = FontWeights.Bold, Foreground = DesignTokens.BrushAccentCyan, VerticalAlignment = VerticalAlignment.Center, TextAlignment = TextAlignment.Center };
            Grid.SetColumn(lblDedicatedCustomVal, 2);
            sliderRow.Children.Add(lblDedicatedCustomVal);

            Button btnApplyDed = new Button
            {
                Content = "Apply Fan Duty",
                FontSize = 10.5,
                FontWeight = FontWeights.Bold,
                Foreground = Brushes.White,
                Background = DesignTokens.BrushAccentBlue,
                BorderThickness = new Thickness(0),
                Height = 30,
                Cursor = Cursors.Hand
            };
            btnApplyDed.MouseEnter += (s, e) => { btnApplyDed.Background = new SolidColorBrush(Color.FromRgb(2, 132, 199)); };
            btnApplyDed.MouseLeave += (s, e) => { btnApplyDed.Background = DesignTokens.BrushAccentBlue; };
            btnApplyDed.Click += (s, e) =>
            {
                int val = (int)sliderDedicatedCustom.Value;
                bool ok = HardwareBridge.SetCustomSpeed(val);
                if (ok)
                {
                    currentFanMode = 6;
                    currentCustomDuty = val;
                    lblStatusMsg.Text = string.Format("Custom Fan Speed ({0}%) Locked", val);
                    UpdateDriveModeButton(btnAuto, false, DesignTokens.AccentBlue);
                    UpdateDriveModeButton(btnQuiet, false, DesignTokens.AccentCyan);
                    UpdateDriveModeButton(btnMax, false, DesignTokens.AccentRed);
                }
            };
            Grid.SetColumn(btnApplyDed, 3);
            sliderRow.Children.Add(btnApplyDed);

            botSp.Children.Add(sliderRow);

            Border bottomCard = CreateSectionCard("Hardware Fan Curve Architecture", curveSub, botSp);
            bottomCard.Margin = new Thickness(0, 10, 0, 0);
            Grid.SetRow(bottomCard, 2);
            g.Children.Add(bottomCard);

            return g;
        }

        private Border CreateSectionCard(string title, string subtitle, UIElement content)
        {
            Border b = new Border
            {
                Background = DesignTokens.BrushBgCard,
                CornerRadius = new CornerRadius(10),
                BorderBrush = DesignTokens.BrushBorderSubtle,
                BorderThickness = new Thickness(1),
                Padding = new Thickness(16, 12, 16, 12)
            };
            StackPanel wrapper = new StackPanel();
            if (!string.IsNullOrEmpty(title))
            {
                TextBlock t = new TextBlock
                {
                    Text = title,
                    FontSize = 13,
                    FontWeight = FontWeights.Bold,
                    Foreground = DesignTokens.BrushTextPrimary
                };
                wrapper.Children.Add(t);
            }
            if (!string.IsNullOrEmpty(subtitle))
            {
                TextBlock s = new TextBlock
                {
                    Text = subtitle,
                    FontSize = 10,
                    Foreground = DesignTokens.BrushTextSecondary,
                    Margin = new Thickness(0, 2, 0, 8)
                };
                wrapper.Children.Add(s);
            }
            if (content != null)
            {
                wrapper.Children.Add(content);
            }
            b.Child = wrapper;
            return b;
        }

        private Border CreateCurveStep(string label, string temp, string desc)
        {
            Border b = new Border
            {
                Background = DesignTokens.BrushBgInput,
                BorderBrush = DesignTokens.BrushBorderSubtle,
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(6),
                Padding = new Thickness(12, 10, 12, 10),
                Margin = new Thickness(4)
            };
            StackPanel sp = new StackPanel();
            sp.Children.Add(new TextBlock { Text = label, FontSize = 10, FontWeight = FontWeights.SemiBold, Foreground = DesignTokens.BrushTextSecondary });
            sp.Children.Add(new TextBlock { Text = temp, FontSize = 18, FontWeight = FontWeights.Bold, Foreground = DesignTokens.BrushTextPrimary, Margin = new Thickness(0, 3, 0, 3) });
            sp.Children.Add(new TextBlock { Text = desc, FontSize = 10, Foreground = DesignTokens.BrushTextMuted });
            b.Child = sp;
            return b;
        }

    }
}