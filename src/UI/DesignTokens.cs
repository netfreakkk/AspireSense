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
    // DESIGN TOKENS (APPLE HIG & PRECISION CRAFT)
    // ==========================================
    public static class DesignTokens
    {
        // Surfaces & Backgrounds
        public static readonly Color BgWindow = Color.FromRgb(11, 15, 25);            // #0B0F19 - Deep slate window canvas
        public static readonly Color BgCard = Color.FromRgb(17, 24, 39);              // #111827 - Surface card
        public static readonly Color BgCardHover = Color.FromRgb(24, 33, 53);         // #182135 - Hover state
        public static readonly Color BgCardActive = Color.FromRgb(30, 41, 59);        // #1E293B - Selected state
        public static readonly Color BgInput = Color.FromRgb(15, 23, 42);             // #0F172A - Control / input surface
        public static readonly Color BorderSubtle = Color.FromRgb(31, 41, 61);        // #1F293D - Clean hairline border
        public static readonly Color BorderSubtleLight = Color.FromRgb(51, 65, 85);   // #334155 - Card highlight border
        public static readonly Color BorderFocus = Color.FromRgb(0, 163, 255);        // #00A3FF - Active accent outline

        // Typography (WCAG AA Compliant on dark surfaces)
        public static readonly Color TextPrimaryColor = Color.FromRgb(248, 250, 252);     // #F8FAFC - Near white
        public static readonly Color TextSecondaryColor = Color.FromRgb(148, 163, 184);   // #94A3B8 - Slate-400 (>5.5:1 contrast)
        public static readonly Color TextMutedColor = Color.FromRgb(100, 116, 139);       // #64748B - Slate-500

        public static readonly SolidColorBrush BrushBgWindow = new SolidColorBrush(BgWindow);
        public static readonly SolidColorBrush BrushBgCard = new SolidColorBrush(BgCard);
        public static readonly SolidColorBrush BrushBgCardHover = new SolidColorBrush(BgCardHover);
        public static readonly SolidColorBrush BrushBgCardActive = new SolidColorBrush(BgCardActive);
        public static readonly SolidColorBrush BrushBgInput = new SolidColorBrush(BgInput);
        public static readonly SolidColorBrush BrushBorderSubtle = new SolidColorBrush(BorderSubtle);
        public static readonly SolidColorBrush BrushBorderSubtleLight = new SolidColorBrush(BorderSubtleLight);
        public static readonly SolidColorBrush BrushBorderFocus = new SolidColorBrush(BorderFocus);

        public static readonly SolidColorBrush BrushTextPrimary = new SolidColorBrush(TextPrimaryColor);
        public static readonly SolidColorBrush BrushTextSecondary = new SolidColorBrush(TextSecondaryColor);
        public static readonly SolidColorBrush BrushTextMuted = new SolidColorBrush(TextMutedColor);

        // Semantic Accents
        public static readonly Color AccentBlue = Color.FromRgb(0, 163, 255);         // #00A3FF - Primary brand / active
        public static readonly Color AccentCyan = Color.FromRgb(6, 182, 212);         // #06B6D4 - Secondary telemetry
        public static readonly Color AccentGreen = Color.FromRgb(16, 185, 129);       // #10B981 - Optimal / running
        public static readonly Color AccentAmber = Color.FromRgb(245, 158, 11);       // #F59E0B - Warning
        public static readonly Color AccentRed = Color.FromRgb(239, 68, 68);          // #EF4444 - Critical / Redline
        public static readonly Color AccentPurple = Color.FromRgb(168, 85, 247);      // #A855F7 - Memory

        public static readonly SolidColorBrush BrushAccentBlue = new SolidColorBrush(AccentBlue);
        public static readonly SolidColorBrush BrushAccentCyan = new SolidColorBrush(AccentCyan);
        public static readonly SolidColorBrush BrushAccentGreen = new SolidColorBrush(AccentGreen);
        public static readonly SolidColorBrush BrushAccentAmber = new SolidColorBrush(AccentAmber);
        public static readonly SolidColorBrush BrushAccentRed = new SolidColorBrush(AccentRed);
        public static readonly SolidColorBrush BrushAccentPurple = new SolidColorBrush(AccentPurple);
    }
}