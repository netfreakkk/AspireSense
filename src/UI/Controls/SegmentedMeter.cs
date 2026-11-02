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
    // SEGMENTED LED BAR METER
    // ==========================================
    public class SegmentedMeter : StackPanel
    {
        private List<Border> segments = new List<Border>();
        private Color activeColor;
        private Color inactiveColor = Color.FromArgb(255, 24, 33, 53);

        public SegmentedMeter(int count, Color active, double segWidth, double segHeight)
        {
            this.Orientation = Orientation.Horizontal;
            this.activeColor = active;
            for (int i = 0; i < count; i++)
            {
                Border b = new Border
                {
                    Width = segWidth,
                    Height = segHeight,
                    CornerRadius = new CornerRadius(1.5),
                    Margin = new Thickness(0, 0, 3, 0),
                    Background = new SolidColorBrush(inactiveColor)
                };
                segments.Add(b);
                this.Children.Add(b);
            }
        }

        public void SetColor(Color col)
        {
            this.activeColor = col;
        }

        public void SetValue(double percent)
        {
            percent = Math.Max(0.0, Math.Min(100.0, percent));
            int activeCount = (int)Math.Round((percent / 100.0) * segments.Count);
            for (int i = 0; i < segments.Count; i++)
            {
                segments[i].Background = (i < activeCount)
                    ? new SolidColorBrush(activeColor)
                    : new SolidColorBrush(inactiveColor);
            }
        }
    }
}