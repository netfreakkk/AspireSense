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
    // APPLE ACTIVITY-STYLE TELEMETRY RING GAUGE
    // ==========================================
    public class SpeedometerGauge : Grid
    {
        private Path bgArc;
        private Path valArc;
        private TextBlock txtHeader;
        private TextBlock txtSub;
        private TextBlock txtTemp;
        private TextBlock txtRpm;
        private TextBlock txtDutyBadge;
        private TextBlock txtWatts;
        private TextBlock txtPercent;
        private TextBlock txtDutyVal;

        private double radius = 84.0;
        private double cx = 105.0;
        private double cy = 105.0;
        private double startAngle = 225.0; // Bottom-left (7:30)
        private double sweepAngle = 270.0; // Sweeps clockwise through 12:00 to bottom-right (4:30)
        private LinearGradientBrush ringGrad;
        private bool isGpuDevice = false;

        public SpeedometerGauge(string hardwareName, string modelSub, double size, bool isGpu = false)
        {
            this.isGpuDevice = isGpu;
            this.Width = size;
            this.HorizontalAlignment = HorizontalAlignment.Center;
            this.VerticalAlignment = VerticalAlignment.Stretch;

            this.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // 0: Chip Header
            this.RowDefinitions.Add(new RowDefinition { Height = new GridLength(210) }); // 1: Activity Ring
            this.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // 2: 3-Pod Telemetry Strip

            // 1. Hardware Category & Title Header
            StackPanel headSp = new StackPanel
            {
                HorizontalAlignment = HorizontalAlignment.Center,
                Margin = new Thickness(0, 0, 0, 10)
            };

            Border chipBadge = new Border
            {
                CornerRadius = new CornerRadius(5),
                Background = isGpu ? new SolidColorBrush(Color.FromArgb(40, 16, 185, 129)) : new SolidColorBrush(Color.FromArgb(40, 6, 182, 212)),
                BorderBrush = isGpu ? DesignTokens.BrushAccentGreen : DesignTokens.BrushAccentCyan,
                BorderThickness = new Thickness(1),
                Padding = new Thickness(8, 2, 8, 2),
                HorizontalAlignment = HorizontalAlignment.Center,
                Margin = new Thickness(0, 0, 0, 4)
            };
            TextBlock chipTxt = new TextBlock
            {
                Text = isGpu ? "⚡ GRAPHICS SILICON" : "🔲 PROCESSOR PACKAGE",
                FontSize = 9.5,
                FontWeight = FontWeights.Bold,
                Foreground = isGpu ? DesignTokens.BrushAccentGreen : DesignTokens.BrushAccentCyan
            };
            chipBadge.Child = chipTxt;
            headSp.Children.Add(chipBadge);

            txtHeader = new TextBlock
            {
                Text = hardwareName,
                FontSize = 14,
                FontWeight = FontWeights.Bold,
                Foreground = DesignTokens.BrushTextPrimary,
                TextAlignment = TextAlignment.Center,
                TextTrimming = TextTrimming.CharacterEllipsis,
                MaxWidth = 310
            };
            headSp.Children.Add(txtHeader);

            txtSub = new TextBlock
            {
                Text = modelSub,
                FontSize = 10.5,
                Foreground = DesignTokens.BrushTextSecondary,
                TextAlignment = TextAlignment.Center,
                Margin = new Thickness(0, 2, 0, 0)
            };
            headSp.Children.Add(txtSub);

            Grid.SetRow(headSp, 0);
            this.Children.Add(headSp);

            // 2. The Apple Activity Ring Canvas Container
            Grid ringHost = new Grid
            {
                Width = 210,
                Height = 210,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            };

            Canvas arcCanvas = new Canvas { Width = 210, Height = 210 };

            // Background Track
            bgArc = new Path
            {
                Stroke = new SolidColorBrush(Color.FromRgb(22, 31, 48)),
                StrokeThickness = 14,
                StrokeStartLineCap = PenLineCap.Round,
                StrokeEndLineCap = PenLineCap.Round,
                Data = CreateArcGeometry(cx, cy, radius, startAngle, startAngle + sweepAngle)
            };
            arcCanvas.Children.Add(bgArc);

            // Active Progress Gradient Arc
            ringGrad = new LinearGradientBrush
            {
                StartPoint = new Point(0, 1),
                EndPoint = new Point(1, 0)
            };
            if (isGpu)
            {
                ringGrad.GradientStops.Add(new GradientStop(Color.FromRgb(16, 185, 129), 0.0));
                ringGrad.GradientStops.Add(new GradientStop(Color.FromRgb(6, 182, 212), 0.7));
                ringGrad.GradientStops.Add(new GradientStop(Color.FromRgb(59, 130, 246), 1.0));
            }
            else
            {
                ringGrad.GradientStops.Add(new GradientStop(Color.FromRgb(6, 182, 212), 0.0));
                ringGrad.GradientStops.Add(new GradientStop(Color.FromRgb(59, 130, 246), 0.7));
                ringGrad.GradientStops.Add(new GradientStop(Color.FromRgb(147, 51, 234), 1.0));
            }

            valArc = new Path
            {
                Stroke = ringGrad,
                StrokeThickness = 14,
                StrokeStartLineCap = PenLineCap.Round,
                StrokeEndLineCap = PenLineCap.Round,
                Effect = new DropShadowEffect
                {
                    Color = isGpu ? Color.FromRgb(16, 185, 129) : Color.FromRgb(6, 182, 212),
                    BlurRadius = 14,
                    ShadowDepth = 0,
                    Opacity = 0.45
                }
            };
            arcCanvas.Children.Add(valArc);
            ringHost.Children.Add(arcCanvas);

            // Centered Hero Metric Stack (Inside the Ring)
            StackPanel ringCenterSp = new StackPanel
            {
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 0, 4)
            };

            txtTemp = new TextBlock
            {
                Text = "-- °C",
                FontSize = 38,
                FontWeight = FontWeights.Bold,
                Foreground = DesignTokens.BrushTextPrimary,
                TextAlignment = TextAlignment.Center
            };
            ringCenterSp.Children.Add(txtTemp);

            txtRpm = new TextBlock
            {
                Text = "-- RPM",
                FontSize = 13,
                FontWeight = FontWeights.SemiBold,
                Foreground = isGpu ? DesignTokens.BrushAccentGreen : DesignTokens.BrushAccentCyan,
                TextAlignment = TextAlignment.Center,
                Margin = new Thickness(0, 1, 0, 0)
            };
            ringCenterSp.Children.Add(txtRpm);

            Border dutyPill = new Border
            {
                CornerRadius = new CornerRadius(10),
                Background = DesignTokens.BrushBgInput,
                BorderBrush = DesignTokens.BrushBorderSubtle,
                BorderThickness = new Thickness(1),
                Padding = new Thickness(8, 2, 8, 2),
                HorizontalAlignment = HorizontalAlignment.Center,
                Margin = new Thickness(0, 6, 0, 0)
            };
            txtDutyBadge = new TextBlock
            {
                Text = "● 0% Duty • Idle",
                FontSize = 9.5,
                FontWeight = FontWeights.SemiBold,
                Foreground = DesignTokens.BrushTextSecondary
            };
            dutyPill.Child = txtDutyBadge;
            ringCenterSp.Children.Add(dutyPill);

            ringHost.Children.Add(ringCenterSp);

            Grid.SetRow(ringHost, 1);
            this.Children.Add(ringHost);

            // 3. Bottom 3-Pod Micro Telemetry Strip (POWER | LOAD | FAN DUTY)
            UniformGrid podGrid = new UniformGrid
            {
                Columns = 3,
                Rows = 1,
                Margin = new Thickness(0, 12, 0, 0)
            };

            Border podPower = CreateTelemetryPod("POWER", out txtWatts, "-- W", DesignTokens.BrushTextPrimary);
            Border podLoad = CreateTelemetryPod("LOAD", out txtPercent, "-- %", isGpu ? DesignTokens.BrushAccentGreen : DesignTokens.BrushAccentCyan);
            Border podDuty = CreateTelemetryPod("FAN DUTY", out txtDutyVal, "-- %", DesignTokens.BrushAccentBlue);

            podGrid.Children.Add(podPower);
            podGrid.Children.Add(podLoad);
            podGrid.Children.Add(podDuty);

            Grid.SetRow(podGrid, 2);
            this.Children.Add(podGrid);

            Update(0, 40, 0, 0, 0);
        }

        private Border CreateTelemetryPod(string caption, out TextBlock valTb, string initialVal, Brush valBrush)
        {
            Border b = new Border
            {
                Background = DesignTokens.BrushBgInput,
                BorderBrush = DesignTokens.BrushBorderSubtle,
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(8),
                Padding = new Thickness(6, 6, 6, 6),
                Margin = new Thickness(3, 0, 3, 0)
            };

            StackPanel sp = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center };
            TextBlock capTb = new TextBlock
            {
                Text = caption,
                FontSize = 9.0,
                FontWeight = FontWeights.SemiBold,
                Foreground = DesignTokens.BrushTextMuted,
                HorizontalAlignment = HorizontalAlignment.Center
            };
            sp.Children.Add(capTb);

            valTb = new TextBlock
            {
                Text = initialVal,
                FontSize = 12.5,
                FontWeight = FontWeights.Bold,
                Foreground = valBrush,
                HorizontalAlignment = HorizontalAlignment.Center,
                Margin = new Thickness(0, 2, 0, 0)
            };
            sp.Children.Add(valTb);

            b.Child = sp;
            return b;
        }

        public void Update(float usage, int temp, int rpm, int duty)
        {
            Update(usage, temp, rpm, duty, 0);
        }

        public void Update(float usage, int temp, int rpm, int duty, float watts)
        {
            txtTemp.Text = string.Format("{0}°C", temp);
            txtRpm.Text = (rpm > 0) ? string.Format("{0:N0} RPM", rpm) : "Passive Idle";
            txtPercent.Text = string.Format("{0:F0}%", usage);
            txtDutyVal.Text = string.Format("{0}%", duty);
            txtWatts.Text = (watts > 0) ? string.Format("{0:F0}W", watts) : "-- W";

            string status = (temp >= 85) ? "Redline" : ((temp >= 72) ? "Warm" : "Optimal");
            txtDutyBadge.Text = string.Format("● {0}% Duty • {1}", duty, status);

            if (temp >= 85)
            {
                txtTemp.Foreground = DesignTokens.BrushAccentRed;
                txtDutyBadge.Foreground = DesignTokens.BrushAccentRed;
                valArc.Stroke = DesignTokens.BrushAccentRed;
            }
            else if (temp >= 72)
            {
                txtTemp.Foreground = DesignTokens.BrushAccentAmber;
                txtDutyBadge.Foreground = DesignTokens.BrushAccentAmber;
                valArc.Stroke = DesignTokens.BrushAccentAmber;
            }
            else
            {
                txtTemp.Foreground = DesignTokens.BrushTextPrimary;
                txtDutyBadge.Foreground = isGpuDevice ? DesignTokens.BrushAccentGreen : DesignTokens.BrushAccentCyan;
                valArc.Stroke = ringGrad;
            }

            // Ring progress mapped to temperature (0..100°C) with minimum visible cap
            double ratio = Math.Max(0.015, Math.Min(1.0, temp / 100.0));
            double targetAngle = startAngle + (sweepAngle * ratio);
            valArc.Data = CreateArcGeometry(cx, cy, radius, startAngle, targetAngle);
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
}