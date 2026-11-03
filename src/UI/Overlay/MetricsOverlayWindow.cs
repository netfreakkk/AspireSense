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
    // MINIMAL COCKPIT IN-GAME METRICS OVERLAY WINDOW
    // ==========================================
    public class MetricsOverlayWindow : Window
    {
        public const int GWL_EXSTYLE = -20;
        public const int WS_EX_TOPMOST = 0x00000008;
        public const int WS_EX_TRANSPARENT = 0x00000020;
        public const int WS_EX_LAYERED = 0x00080000;
        public const int WS_EX_NOACTIVATE = 0x08000000;
        public const int WS_EX_TOOLWINDOW = 0x00000080;

        [DllImport("user32.dll", SetLastError = true)]
        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        public static extern int GetWindowLong(IntPtr hWnd, int nIndex);

        [DllImport("user32.dll", SetLastError = true)]
        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        public static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);

        private Border rootBorder;
        private StackPanel stackHorizontal;
        private Grid gridMatrix;

        private TextBlock txtFps;
        private TextBlock txtAvgFps;
        private TextBlock txtOneLow;
        private TextBlock txtGpuTemp;
        private TextBlock txtGpuUsage;
        private TextBlock txtGpuWatts;
        private TextBlock txtCpuTemp;
        private TextBlock txtCpuUsage;
        private TextBlock txtCpuWatts;
        private TextBlock txtRamUsage;

        private TextBlock txtMatrixFps;
        private TextBlock txtMatrixTarget;
        private TextBlock txtMatrixGpu;
        private TextBlock txtMatrixCpu;
        private TextBlock txtMatrixRam;

        private OverlayPosition currentPosition = OverlayPosition.TopLeft;
        private OverlayStyle currentStyle = OverlayStyle.HorizontalBar;

        public MetricsOverlayWindow()
        {
            this.Title = "AspireSense In-Game HUD";
            this.WindowStyle = WindowStyle.None;
            this.AllowsTransparency = true;
            this.Background = Brushes.Transparent;
            this.Topmost = true;
            this.ShowInTaskbar = false;
            this.SizeToContent = SizeToContent.WidthAndHeight;

            BuildUI();
        }

        protected override void OnSourceInitialized(EventArgs e)
        {
            base.OnSourceInitialized(e);
            try
            {
                IntPtr hwnd = new WindowInteropHelper(this).Handle;
                int exStyle = GetWindowLong(hwnd, GWL_EXSTYLE);
                SetWindowLong(hwnd, GWL_EXSTYLE, exStyle | WS_EX_TOPMOST | WS_EX_TRANSPARENT | WS_EX_LAYERED | WS_EX_NOACTIVATE | WS_EX_TOOLWINDOW);
            }
            catch { }
            Reposition();
        }

        private void BuildUI()
        {
            rootBorder = new Border
            {
                Background = new SolidColorBrush(Color.FromArgb(235, 11, 15, 25)),
                BorderBrush = new SolidColorBrush(Color.FromArgb(140, 31, 41, 61)),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(8),
                Padding = new Thickness(12, 6, 12, 6),
                Effect = new DropShadowEffect { Color = Colors.Black, BlurRadius = 14, ShadowDepth = 2, Opacity = 0.8 }
            };

            // 1. Horizontal HUD Bar (Single line precision telemetry)
            stackHorizontal = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };

            // FPS Block
            StackPanel spFps = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(2, 0, 6, 0) };
            spFps.Children.Add(new TextBlock { Text = "FPS ", FontSize = 10, FontWeight = FontWeights.SemiBold, Foreground = DesignTokens.BrushTextSecondary, VerticalAlignment = VerticalAlignment.Center });
            txtFps = new TextBlock { Text = "--", FontSize = 14, FontWeight = FontWeights.Bold, Foreground = DesignTokens.BrushAccentGreen, VerticalAlignment = VerticalAlignment.Center };
            spFps.Children.Add(txtFps);
            stackHorizontal.Children.Add(spFps);

            // AVG FPS Block
            StackPanel spAvg = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(4, 0, 6, 0) };
            spAvg.Children.Add(new TextBlock { Text = "AVG ", FontSize = 10, FontWeight = FontWeights.SemiBold, Foreground = DesignTokens.BrushTextSecondary, VerticalAlignment = VerticalAlignment.Center });
            txtAvgFps = new TextBlock { Text = "--", FontSize = 12, FontWeight = FontWeights.Bold, Foreground = DesignTokens.BrushAccentBlue, VerticalAlignment = VerticalAlignment.Center };
            spAvg.Children.Add(txtAvgFps);
            stackHorizontal.Children.Add(spAvg);

            // 1% Low Block
            StackPanel spOneLow = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(4, 0, 6, 0) };
            spOneLow.Children.Add(new TextBlock { Text = "1% ", FontSize = 10, FontWeight = FontWeights.SemiBold, Foreground = DesignTokens.BrushTextSecondary, VerticalAlignment = VerticalAlignment.Center });
            txtOneLow = new TextBlock { Text = "--", FontSize = 12, FontWeight = FontWeights.Bold, Foreground = DesignTokens.BrushAccentCyan, VerticalAlignment = VerticalAlignment.Center };
            spOneLow.Children.Add(txtOneLow);
            stackHorizontal.Children.Add(spOneLow);
            stackHorizontal.Children.Add(CreateDivider());

            // GPU Block
            StackPanel spGpu = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(6, 0, 6, 0) };
            spGpu.Children.Add(new TextBlock { Text = "GPU ", FontSize = 10, FontWeight = FontWeights.SemiBold, Foreground = DesignTokens.BrushAccentCyan, VerticalAlignment = VerticalAlignment.Center });
            txtGpuTemp = new TextBlock { Text = "--°C", FontSize = 12, FontWeight = FontWeights.Bold, Foreground = DesignTokens.BrushTextPrimary, VerticalAlignment = VerticalAlignment.Center };
            spGpu.Children.Add(txtGpuTemp);
            txtGpuUsage = new TextBlock { Text = "0%", FontSize = 11, FontWeight = FontWeights.SemiBold, Foreground = DesignTokens.BrushAccentAmber, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(5, 0, 0, 0) };
            spGpu.Children.Add(txtGpuUsage);
            txtGpuWatts = new TextBlock { Text = "0W", FontSize = 11, FontWeight = FontWeights.SemiBold, Foreground = DesignTokens.BrushAccentCyan, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(5, 0, 0, 0) };
            spGpu.Children.Add(txtGpuWatts);
            stackHorizontal.Children.Add(spGpu);
            stackHorizontal.Children.Add(CreateDivider());

            // CPU Block
            StackPanel spCpu = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(6, 0, 6, 0) };
            spCpu.Children.Add(new TextBlock { Text = "CPU ", FontSize = 10, FontWeight = FontWeights.SemiBold, Foreground = DesignTokens.BrushAccentBlue, VerticalAlignment = VerticalAlignment.Center });
            txtCpuTemp = new TextBlock { Text = "--°C", FontSize = 12, FontWeight = FontWeights.Bold, Foreground = DesignTokens.BrushTextPrimary, VerticalAlignment = VerticalAlignment.Center };
            spCpu.Children.Add(txtCpuTemp);
            txtCpuUsage = new TextBlock { Text = "0%", FontSize = 11, FontWeight = FontWeights.SemiBold, Foreground = DesignTokens.BrushAccentBlue, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(5, 0, 0, 0) };
            spCpu.Children.Add(txtCpuUsage);
            txtCpuWatts = new TextBlock { Text = "0W", FontSize = 11, FontWeight = FontWeights.SemiBold, Foreground = DesignTokens.BrushAccentGreen, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(5, 0, 0, 0) };
            spCpu.Children.Add(txtCpuWatts);
            stackHorizontal.Children.Add(spCpu);
            stackHorizontal.Children.Add(CreateDivider());

            // RAM Block
            StackPanel spRam = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(6, 0, 2, 0) };
            spRam.Children.Add(new TextBlock { Text = "RAM ", FontSize = 10, FontWeight = FontWeights.SemiBold, Foreground = DesignTokens.BrushAccentPurple, VerticalAlignment = VerticalAlignment.Center });
            txtRamUsage = new TextBlock { Text = "-- GB", FontSize = 11.5, FontWeight = FontWeights.Bold, Foreground = DesignTokens.BrushTextPrimary, VerticalAlignment = VerticalAlignment.Center };
            spRam.Children.Add(txtRamUsage);
            stackHorizontal.Children.Add(spRam);

            // 2. Compact Matrix Layout (2-row badge)
            gridMatrix = new Grid { Visibility = Visibility.Collapsed, MinWidth = 230 };
            gridMatrix.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            gridMatrix.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            Grid topRow = new Grid { Margin = new Thickness(0, 0, 0, 4) };
            topRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            topRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            txtMatrixFps = new TextBlock { Text = "FPS: -- (1% Low: --)", FontSize = 12, FontWeight = FontWeights.Bold, Foreground = DesignTokens.BrushAccentGreen };
            Grid.SetColumn(txtMatrixFps, 0);
            topRow.Children.Add(txtMatrixFps);

            txtMatrixTarget = new TextBlock { Text = "Desktop", FontSize = 10, FontWeight = FontWeights.SemiBold, Foreground = DesignTokens.BrushTextSecondary };
            Grid.SetColumn(txtMatrixTarget, 1);
            topRow.Children.Add(txtMatrixTarget);

            Grid.SetRow(topRow, 0);
            gridMatrix.Children.Add(topRow);

            StackPanel botRow = new StackPanel { Orientation = Orientation.Horizontal };
            txtMatrixGpu = new TextBlock { Text = "GPU: --°C (0%)", FontSize = 10, FontWeight = FontWeights.SemiBold, Foreground = DesignTokens.BrushAccentCyan, Margin = new Thickness(0, 0, 8, 0) };
            txtMatrixCpu = new TextBlock { Text = "CPU: --°C (0%)", FontSize = 10, FontWeight = FontWeights.SemiBold, Foreground = DesignTokens.BrushAccentBlue, Margin = new Thickness(0, 0, 8, 0) };
            txtMatrixRam = new TextBlock { Text = "RAM: -- GB", FontSize = 10, FontWeight = FontWeights.SemiBold, Foreground = DesignTokens.BrushAccentPurple };
            botRow.Children.Add(txtMatrixGpu);
            botRow.Children.Add(txtMatrixCpu);
            botRow.Children.Add(txtMatrixRam);

            Grid.SetRow(botRow, 1);
            gridMatrix.Children.Add(botRow);

            Grid contentHolder = new Grid();
            contentHolder.Children.Add(stackHorizontal);
            contentHolder.Children.Add(gridMatrix);

            rootBorder.Child = contentHolder;
            this.Content = rootBorder;
        }

        private Border CreateDivider()
        {
            return new Border
            {
                Width = 1,
                Height = 14,
                Background = DesignTokens.BrushBorderSubtle,
                Margin = new Thickness(5, 0, 5, 0),
                VerticalAlignment = VerticalAlignment.Center
            };
        }

        public void SetPositionPreset(OverlayPosition pos)
        {
            currentPosition = pos;
            Reposition();
        }

        public void SetStylePreset(OverlayStyle style)
        {
            currentStyle = style;
            if (style == OverlayStyle.HorizontalBar)
            {
                stackHorizontal.Visibility = Visibility.Visible;
                gridMatrix.Visibility = Visibility.Collapsed;
            }
            else
            {
                stackHorizontal.Visibility = Visibility.Collapsed;
                gridMatrix.Visibility = Visibility.Visible;
            }
            this.UpdateLayout();
            Reposition();
        }

        public void Reposition()
        {
            double scrW = SystemParameters.PrimaryScreenWidth;
            double scrH = SystemParameters.PrimaryScreenHeight;
            double w = this.ActualWidth > 0 ? this.ActualWidth : 380;
            double h = this.ActualHeight > 0 ? this.ActualHeight : 45;

            switch (currentPosition)
            {
                case OverlayPosition.TopLeft:
                    this.Left = 24;
                    this.Top = 24;
                    break;
                case OverlayPosition.TopRight:
                    this.Left = scrW - w - 24;
                    this.Top = 24;
                    break;
                case OverlayPosition.CenterTop:
                    this.Left = (scrW - w) / 2.0;
                    this.Top = 16;
                    break;
                case OverlayPosition.BottomLeft:
                    this.Left = 24;
                    this.Top = scrH - h - 48;
                    break;
                case OverlayPosition.BottomRight:
                    this.Left = scrW - w - 24;
                    this.Top = scrH - h - 48;
                    break;
            }
        }

        public void UpdateMetrics(double fps, double avgFps, double oneLow, int gpuTemp, float gpuUsage, float gpuWatts, int cpuTemp, float cpuUsage, float cpuWatts, double ramGb, float ramPct, string targetName)
        {
            if (fps > 0)
            {
                txtFps.Text = string.Format("{0:F0}", fps);
                txtAvgFps.Text = string.Format("{0:F0}", avgFps > 0 ? avgFps : fps);
                txtOneLow.Text = string.Format("{0:F0}", oneLow);
                txtFps.Foreground = fps >= 60 ? new SolidColorBrush(Color.FromRgb(0, 255, 140)) : (fps >= 30 ? new SolidColorBrush(Color.FromRgb(0, 210, 255)) : new SolidColorBrush(Color.FromRgb(255, 42, 85)));
                txtAvgFps.Foreground = (avgFps >= 60 || (avgFps == 0 && fps >= 60)) ? new SolidColorBrush(Color.FromRgb(56, 189, 248)) : new SolidColorBrush(Color.FromRgb(0, 210, 255));
                txtOneLow.Foreground = oneLow >= 45 ? new SolidColorBrush(Color.FromRgb(0, 229, 255)) : new SolidColorBrush(Color.FromRgb(255, 170, 0));
            }
            else
            {
                txtFps.Text = "--";
                txtAvgFps.Text = "--";
                txtOneLow.Text = "--";
                txtFps.Foreground = new SolidColorBrush(Color.FromRgb(120, 150, 180));
                txtAvgFps.Foreground = new SolidColorBrush(Color.FromRgb(120, 150, 180));
                txtOneLow.Foreground = new SolidColorBrush(Color.FromRgb(120, 150, 180));
            }

            txtGpuTemp.Text = string.Format("{0}°C", gpuTemp);
            txtGpuUsage.Text = string.Format("{0:F0}%", gpuUsage);
            txtGpuWatts.Text = (gpuWatts > 0) ? string.Format("{0:F0}W", gpuWatts) : "0W";
            txtGpuTemp.Foreground = gpuTemp < 72 ? new SolidColorBrush(Color.FromRgb(0, 229, 255)) : (gpuTemp < 85 ? new SolidColorBrush(Color.FromRgb(255, 170, 0)) : new SolidColorBrush(Color.FromRgb(255, 42, 85)));

            txtCpuTemp.Text = string.Format("{0}°C", cpuTemp);
            txtCpuUsage.Text = string.Format("{0:F0}%", cpuUsage);
            txtCpuWatts.Text = (cpuWatts > 0) ? string.Format("{0:F0}W", cpuWatts) : "0W";
            txtCpuTemp.Foreground = cpuTemp < 75 ? new SolidColorBrush(Color.FromRgb(0, 229, 255)) : (cpuTemp < 88 ? new SolidColorBrush(Color.FromRgb(255, 170, 0)) : new SolidColorBrush(Color.FromRgb(255, 42, 85)));

            txtRamUsage.Text = string.Format("{0:F1}G ({1:F0}%)", ramGb, ramPct);

            if (txtMatrixFps != null)
            {
                txtMatrixFps.Text = (fps > 0) ? string.Format("FPS: {0:F0} (AVG: {1:F0} | 1% LOW: {2:F0})", fps, avgFps > 0 ? avgFps : fps, oneLow) : "FPS: -- (AVG: -- | 1% LOW: --)";
                txtMatrixTarget.Text = !string.IsNullOrEmpty(targetName) ? targetName : "Desktop";
                txtMatrixGpu.Text = string.Format("GPU: {0}°C ({1:F0}% | {2:F0}W)", gpuTemp, gpuUsage, gpuWatts);
                txtMatrixCpu.Text = string.Format("CPU: {0}°C ({1:F0}% | {2:F0}W)", cpuTemp, cpuUsage, cpuWatts);
                txtMatrixRam.Text = string.Format("RAM: {0:F1} GB ({1:F0}%)", ramGb, ramPct);
            }
        }
    }
}
