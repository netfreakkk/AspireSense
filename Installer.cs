using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Microsoft.Win32;
using IOPath = System.IO.Path;
using ShapePath = System.Windows.Shapes.Path;

namespace AspireSenseInstaller
{
    public class SetupWindow : Window
    {
        private bool isUninstallMode = false;
        private string defaultInstallDir;

        // UI Views
        private Grid viewInstallOptions;
        private Grid viewProgress;
        private Grid viewComplete;
        private Grid viewUninstall;

        // Option Controls
        private TextBox txtInstallDir;
        private CheckBox chkDesktopShortcut;
        private CheckBox chkStartMenuShortcut;
        private CheckBox chkStartup;
        private CheckBox chkLaunchAfter;

        // Progress Controls
        private ProgressBar progBar;
        private TextBlock lblStatus;
        private TextBlock lblDetail;

        public SetupWindow(bool uninstallMode)
        {
            this.isUninstallMode = uninstallMode;
            this.Title = isUninstallMode ? "AspireSense Uninstaller" : "AspireSense Setup v1.0.0";
            this.Width = 680;
            this.Height = 490;
            this.WindowStartupLocation = WindowStartupLocation.CenterScreen;
            this.WindowStyle = WindowStyle.None;
            this.AllowsTransparency = true;
            this.Background = new SolidColorBrush(Color.FromRgb(7, 10, 18));
            this.BorderBrush = new SolidColorBrush(Color.FromRgb(0, 160, 255));
            this.BorderThickness = new Thickness(1);

            defaultInstallDir = IOPath.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", "AspireSense");

            LoadEmbeddedIcon();
            BuildLayout();
        }

        private void LoadEmbeddedIcon()
        {
            try
            {
                Assembly asm = Assembly.GetExecutingAssembly();
                using (Stream s = asm.GetManifestResourceStream("app.ico"))
                {
                    if (s != null)
                    {
                        BitmapImage bmp = new BitmapImage();
                        bmp.BeginInit();
                        bmp.StreamSource = s;
                        bmp.CacheOption = BitmapCacheOption.OnLoad;
                        bmp.EndInit();
                        this.Icon = bmp;
                    }
                }
            }
            catch { }
        }

        private void BuildLayout()
        {
            Grid root = new Grid();

            RadialGradientBrush bgGradient = new RadialGradientBrush
            {
                Center = new Point(0.5, 0.4),
                RadiusX = 0.8,
                RadiusY = 0.7,
                GradientStops = new GradientStopCollection
                {
                    new GradientStop(Color.FromArgb(70, 0, 75, 160), 0.0),
                    new GradientStop(Color.FromArgb(25, 0, 30, 80), 0.5),
                    new GradientStop(Color.FromArgb(0, 0, 0, 0), 1.0)
                }
            };
            root.Background = bgGradient;

            root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(50) });  // Titlebar
            root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) }); // Viewhost
            root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(35) });  // Footer

            // Top Titlebar
            Grid topBar = CreateTopBar();
            Grid.SetRow(topBar, 0);
            root.Children.Add(topBar);

            // View Host
            Grid viewHost = new Grid { Margin = new Thickness(35, 15, 35, 15) };
            viewInstallOptions = CreateInstallOptionsView();
            viewProgress = CreateProgressView();
            viewComplete = CreateCompleteView();
            viewUninstall = CreateUninstallView();

            viewHost.Children.Add(viewInstallOptions);
            viewHost.Children.Add(viewProgress);
            viewHost.Children.Add(viewComplete);
            viewHost.Children.Add(viewUninstall);

            if (isUninstallMode)
            {
                viewInstallOptions.Visibility = Visibility.Collapsed;
                viewUninstall.Visibility = Visibility.Visible;
            }
            else
            {
                viewInstallOptions.Visibility = Visibility.Visible;
                viewUninstall.Visibility = Visibility.Collapsed;
            }

            Grid.SetRow(viewHost, 1);
            root.Children.Add(viewHost);

            // Footer
            Grid footer = CreateFooter();
            Grid.SetRow(footer, 2);
            root.Children.Add(footer);

            this.Content = root;
        }

        private Grid CreateTopBar()
        {
            Grid bar = new Grid { Background = new SolidColorBrush(Color.FromRgb(10, 14, 25)) };
            bar.MouseDown += (s, e) => { if (e.LeftButton == MouseButtonState.Pressed) this.DragMove(); };

            bar.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            bar.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(80) });

            StackPanel titleSp = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(15, 0, 0, 0) };

            Image logoImg = GetEmbeddedLogoImage(24);
            if (logoImg != null)
            {
                logoImg.Margin = new Thickness(0, 0, 8, 0);
                titleSp.Children.Add(logoImg);
            }

            TextBlock txtTitle = new TextBlock
            {
                Text = isUninstallMode ? "ASPIRESENSE UNINSTALL WIZARD" : "ASPIRESENSE SETUP WIZARD v1.0.0",
                FontSize = 13,
                FontWeight = FontWeights.Bold,
                Foreground = Brushes.White,
                VerticalAlignment = VerticalAlignment.Center
            };
            titleSp.Children.Add(txtTitle);
            Grid.SetColumn(titleSp, 0);
            bar.Children.Add(titleSp);

            StackPanel btnSp = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 0, 10, 0) };

            Button btnMin = new Button
            {
                Content = "—",
                Width = 30,
                Height = 28,
                Foreground = Brushes.White,
                Background = Brushes.Transparent,
                BorderThickness = new Thickness(0),
                Cursor = Cursors.Hand,
                FontWeight = FontWeights.Bold
            };
            btnMin.Click += (s, e) => { this.WindowState = WindowState.Minimized; };
            btnSp.Children.Add(btnMin);

            Button btnClose = new Button
            {
                Content = "✕",
                Width = 30,
                Height = 28,
                Foreground = Brushes.White,
                Background = Brushes.Transparent,
                BorderThickness = new Thickness(0),
                Cursor = Cursors.Hand,
                FontWeight = FontWeights.Bold
            };
            btnClose.Click += (s, e) => { this.Close(); };
            btnSp.Children.Add(btnClose);

            Grid.SetColumn(btnSp, 1);
            bar.Children.Add(btnSp);

            return bar;
        }

        private Grid CreateInstallOptionsView()
        {
            Grid g = new Grid();
            g.RowDefinitions.Add(new RowDefinition { Height = new GridLength(80) });
            g.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            g.RowDefinitions.Add(new RowDefinition { Height = new GridLength(50) });

            // Header info
            string detectedMfg = DetectChassisManufacturer();
            string mfgDetail = "Universal Native Windows Driver Stack (Zero external DLLs needed)";
            if (detectedMfg == "Acer") mfgDetail = "Acer Insyde H2O EC Hardware Stack (InsydeDCHU.dll deployed)";
            else if (detectedMfg == "ASUS") mfgDetail = "ASUS Direct ACPI Stack (Native Windows Driver, Zero 3rd-party DLLs)";
            else if (detectedMfg == "Lenovo") mfgDetail = "Lenovo Vantage ACPI/WMI Stack (Native Windows Driver, Zero 3rd-party DLLs)";
            else if (detectedMfg == "HP") mfgDetail = "HP Omen WMI Bios Stack (Native Windows Driver, Zero 3rd-party DLLs)";
            else if (detectedMfg == "Dell") mfgDetail = "Dell Alienware/G-Series WMAX Stack (Native Windows Driver, Zero 3rd-party DLLs)";

            StackPanel headerSp = new StackPanel();
            TextBlock title = new TextBlock
            {
                Text = "Install AspireSense on this computer",
                FontSize = 18,
                FontWeight = FontWeights.Black,
                Foreground = new SolidColorBrush(Color.FromRgb(0, 210, 255))
            };
            TextBlock sub = new TextBlock
            {
                Text = "Open-source hardware fan control & telemetry cockpit for gaming laptops (Acer, ASUS, Lenovo, HP, Dell).",
                FontSize = 11,
                Foreground = new SolidColorBrush(Color.FromRgb(150, 180, 220)),
                Margin = new Thickness(0, 3, 0, 0)
            };

            Border hwPill = new Border
            {
                Background = new SolidColorBrush(Color.FromArgb(90, 0, 160, 255)),
                BorderBrush = new SolidColorBrush(Color.FromArgb(140, 0, 210, 255)),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(4),
                Padding = new Thickness(8, 3, 8, 3),
                Margin = new Thickness(0, 6, 0, 0),
                HorizontalAlignment = HorizontalAlignment.Left
            };
            hwPill.Child = new TextBlock
            {
                Text = "DETECTED PLATFORM: " + detectedMfg.ToUpperInvariant() + " | " + mfgDetail,
                FontSize = 9,
                FontWeight = FontWeights.Bold,
                Foreground = new SolidColorBrush(Color.FromRgb(0, 240, 255))
            };

            headerSp.Children.Add(title);
            headerSp.Children.Add(sub);
            headerSp.Children.Add(hwPill);
            Grid.SetRow(headerSp, 0);
            g.Children.Add(headerSp);

            // Middle options card
            Border card = new Border
            {
                Background = new SolidColorBrush(Color.FromArgb(170, 12, 18, 32)),
                BorderBrush = new SolidColorBrush(Color.FromArgb(70, 0, 150, 255)),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(8),
                Padding = new Thickness(18, 14, 18, 14)
            };

            StackPanel optsSp = new StackPanel();

            // Destination Folder
            TextBlock lblDest = new TextBlock { Text = "DESTINATION DIRECTORY", FontSize = 10, FontWeight = FontWeights.Bold, Foreground = new SolidColorBrush(Color.FromRgb(100, 160, 230)) };
            optsSp.Children.Add(lblDest);

            Grid pathGrid = new Grid { Margin = new Thickness(0, 6, 0, 16) };
            pathGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            pathGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(90) });

            txtInstallDir = new TextBox
            {
                Text = defaultInstallDir,
                Height = 32,
                Background = new SolidColorBrush(Color.FromArgb(200, 10, 15, 26)),
                Foreground = Brushes.White,
                BorderBrush = new SolidColorBrush(Color.FromArgb(90, 0, 160, 255)),
                BorderThickness = new Thickness(1),
                Padding = new Thickness(8, 6, 8, 6),
                FontSize = 12,
                VerticalContentAlignment = VerticalAlignment.Center
            };
            Grid.SetColumn(txtInstallDir, 0);
            pathGrid.Children.Add(txtInstallDir);

            Button btnBrowse = new Button
            {
                Content = "Browse...",
                Height = 32,
                Margin = new Thickness(8, 0, 0, 0),
                Background = new SolidColorBrush(Color.FromArgb(180, 20, 30, 50)),
                Foreground = Brushes.White,
                BorderBrush = new SolidColorBrush(Color.FromArgb(90, 0, 160, 255)),
                BorderThickness = new Thickness(1),
                Cursor = Cursors.Hand,
                FontSize = 11,
                FontWeight = FontWeights.SemiBold
            };
            btnBrowse.Click += (s, e) =>
            {
                var dialog = new System.Windows.Forms.FolderBrowserDialog();
                dialog.SelectedPath = txtInstallDir.Text;
                dialog.Description = "Select installation folder for AspireSense";
                if (dialog.ShowDialog() == System.Windows.Forms.DialogResult.OK)
                {
                    txtInstallDir.Text = dialog.SelectedPath;
                }
            };
            Grid.SetColumn(btnBrowse, 1);
            pathGrid.Children.Add(btnBrowse);

            optsSp.Children.Add(pathGrid);

            // Shortcuts Options
            TextBlock lblShortcuts = new TextBlock { Text = "INSTALLATION OPTIONS", FontSize = 10, FontWeight = FontWeights.Bold, Foreground = new SolidColorBrush(Color.FromRgb(100, 160, 230)), Margin = new Thickness(0, 0, 0, 6) };
            optsSp.Children.Add(lblShortcuts);

            chkDesktopShortcut = CreateStyledCheckBox("Create Desktop Shortcut", true);
            optsSp.Children.Add(chkDesktopShortcut);

            chkStartMenuShortcut = CreateStyledCheckBox("Create Start Menu Shortcut", true);
            optsSp.Children.Add(chkStartMenuShortcut);

            chkStartup = CreateStyledCheckBox("Launch AspireSense on Windows Startup", false);
            optsSp.Children.Add(chkStartup);

            // Space info
            TextBlock spaceInfo = new TextBlock
            {
                Text = "Space required: ~4.5 MB | Free space on drive: Available | License: MIT Open-Source",
                FontSize = 9.5,
                Foreground = new SolidColorBrush(Color.FromRgb(120, 150, 180)),
                Margin = new Thickness(0, 12, 0, 0)
            };
            optsSp.Children.Add(spaceInfo);

            card.Child = optsSp;
            Grid.SetRow(card, 1);
            g.Children.Add(card);

            // Action Buttons
            StackPanel actSp = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Center };

            Button btnCancel = new Button
            {
                Content = "Cancel",
                Width = 90,
                Height = 34,
                Background = Brushes.Transparent,
                Foreground = new SolidColorBrush(Color.FromRgb(160, 180, 200)),
                BorderThickness = new Thickness(0),
                Cursor = Cursors.Hand,
                FontWeight = FontWeights.SemiBold,
                Margin = new Thickness(0, 0, 12, 0)
            };
            btnCancel.Click += (s, e) => { this.Close(); };
            actSp.Children.Add(btnCancel);

            Button btnInstall = new Button
            {
                Content = "Install AspireSense",
                Width = 160,
                Height = 34,
                Background = new LinearGradientBrush(Color.FromRgb(0, 140, 255), Color.FromRgb(0, 80, 200), 45),
                Foreground = Brushes.White,
                BorderThickness = new Thickness(0),
                Cursor = Cursors.Hand,
                FontWeight = FontWeights.Bold,
                FontSize = 12
            };
            btnInstall.Click += (s, e) => { StartInstallation(); };
            actSp.Children.Add(btnInstall);

            Grid.SetRow(actSp, 2);
            g.Children.Add(actSp);

            return g;
        }

        private CheckBox CreateStyledCheckBox(string text, bool isChecked)
        {
            return new CheckBox
            {
                Content = text,
                IsChecked = isChecked,
                Foreground = Brushes.White,
                FontSize = 11.5,
                Margin = new Thickness(0, 3, 0, 3),
                Cursor = Cursors.Hand
            };
        }

        private Grid CreateProgressView()
        {
            Grid g = new Grid { Visibility = Visibility.Collapsed };
            StackPanel sp = new StackPanel { VerticalAlignment = VerticalAlignment.Center };

            Image logoCenter = GetEmbeddedLogoImage(72);
            if (logoCenter != null)
            {
                logoCenter.HorizontalAlignment = HorizontalAlignment.Center;
                logoCenter.Margin = new Thickness(0, 0, 0, 20);
                sp.Children.Add(logoCenter);
            }

            lblStatus = new TextBlock
            {
                Text = "Installing AspireSense...",
                FontSize = 18,
                FontWeight = FontWeights.Black,
                Foreground = new SolidColorBrush(Color.FromRgb(0, 210, 255)),
                HorizontalAlignment = HorizontalAlignment.Center,
                Margin = new Thickness(0, 0, 0, 8)
            };
            sp.Children.Add(lblStatus);

            lblDetail = new TextBlock
            {
                Text = "Preparing installation package...",
                FontSize = 11,
                Foreground = new SolidColorBrush(Color.FromRgb(150, 180, 220)),
                HorizontalAlignment = HorizontalAlignment.Center,
                Margin = new Thickness(0, 0, 0, 18)
            };
            sp.Children.Add(lblDetail);

            progBar = new ProgressBar
            {
                Height = 12,
                Minimum = 0,
                Maximum = 100,
                Value = 10,
                Foreground = new SolidColorBrush(Color.FromRgb(0, 210, 255)),
                Background = new SolidColorBrush(Color.FromArgb(150, 20, 30, 50)),
                BorderThickness = new Thickness(0),
                Margin = new Thickness(40, 0, 40, 0)
            };
            sp.Children.Add(progBar);

            g.Children.Add(sp);
            return g;
        }

        private Grid CreateCompleteView()
        {
            Grid g = new Grid { Visibility = Visibility.Collapsed };
            StackPanel sp = new StackPanel { VerticalAlignment = VerticalAlignment.Center };

            Image logoCenter = GetEmbeddedLogoImage(80);
            if (logoCenter != null)
            {
                logoCenter.HorizontalAlignment = HorizontalAlignment.Center;
                logoCenter.Margin = new Thickness(0, 0, 0, 18);
                sp.Children.Add(logoCenter);
            }

            TextBlock title = new TextBlock
            {
                Text = "Installation Complete!",
                FontSize = 22,
                FontWeight = FontWeights.Black,
                Foreground = new SolidColorBrush(Color.FromRgb(0, 225, 140)),
                HorizontalAlignment = HorizontalAlignment.Center,
                Margin = new Thickness(0, 0, 0, 6)
            };
            sp.Children.Add(title);

            TextBlock desc = new TextBlock
            {
                Text = "AspireSense is now installed and ready to optimize your laptop thermals.",
                FontSize = 12,
                Foreground = Brushes.White,
                HorizontalAlignment = HorizontalAlignment.Center,
                Margin = new Thickness(0, 0, 0, 20)
            };
            sp.Children.Add(desc);

            chkLaunchAfter = CreateStyledCheckBox("Launch AspireSense now", true);
            chkLaunchAfter.HorizontalAlignment = HorizontalAlignment.Center;
            chkLaunchAfter.Margin = new Thickness(0, 0, 0, 20);
            sp.Children.Add(chkLaunchAfter);

            Button btnFinish = new Button
            {
                Content = "Finish",
                Width = 140,
                Height = 36,
                Background = new LinearGradientBrush(Color.FromRgb(0, 200, 140), Color.FromRgb(0, 130, 90), 45),
                Foreground = Brushes.White,
                BorderThickness = new Thickness(0),
                Cursor = Cursors.Hand,
                FontWeight = FontWeights.Bold,
                FontSize = 13,
                HorizontalAlignment = HorizontalAlignment.Center
            };
            btnFinish.Click += (s, e) =>
            {
                if (chkLaunchAfter.IsChecked == true)
                {
                    string targetExe = IOPath.Combine(txtInstallDir.Text.Trim(), "AspireSense.exe");
                    if (File.Exists(targetExe))
                    {
                        try { Process.Start(targetExe); } catch { }
                    }
                }
                this.Close();
            };
            sp.Children.Add(btnFinish);

            g.Children.Add(sp);
            return g;
        }

        private Grid CreateUninstallView()
        {
            Grid g = new Grid { Visibility = Visibility.Collapsed };
            StackPanel sp = new StackPanel { VerticalAlignment = VerticalAlignment.Center };

            Image logoCenter = GetEmbeddedLogoImage(70);
            if (logoCenter != null)
            {
                logoCenter.HorizontalAlignment = HorizontalAlignment.Center;
                logoCenter.Margin = new Thickness(0, 0, 0, 16);
                sp.Children.Add(logoCenter);
            }

            TextBlock title = new TextBlock
            {
                Text = "Uninstall AspireSense",
                FontSize = 20,
                FontWeight = FontWeights.Black,
                Foreground = new SolidColorBrush(Color.FromRgb(255, 80, 80)),
                HorizontalAlignment = HorizontalAlignment.Center,
                Margin = new Thickness(0, 0, 0, 8)
            };
            sp.Children.Add(title);

            TextBlock desc = new TextBlock
            {
                Text = "This will remove AspireSense, its dependencies, shortcuts, and settings from your PC.\nYour laptop's EC fans will return to factory default automatic control.",
                FontSize = 11.5,
                Foreground = Brushes.LightGray,
                TextAlignment = TextAlignment.Center,
                HorizontalAlignment = HorizontalAlignment.Center,
                Margin = new Thickness(20, 0, 20, 24)
            };
            sp.Children.Add(desc);

            StackPanel btnSp = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center };

            Button btnCancel = new Button
            {
                Content = "Cancel",
                Width = 100,
                Height = 34,
                Background = Brushes.Transparent,
                Foreground = Brushes.White,
                BorderThickness = new Thickness(0),
                Cursor = Cursors.Hand,
                FontWeight = FontWeights.SemiBold,
                Margin = new Thickness(0, 0, 15, 0)
            };
            btnCancel.Click += (s, e) => { this.Close(); };
            btnSp.Children.Add(btnCancel);

            Button btnProceed = new Button
            {
                Content = "Uninstall Now",
                Width = 140,
                Height = 34,
                Background = new SolidColorBrush(Color.FromRgb(200, 40, 40)),
                Foreground = Brushes.White,
                BorderThickness = new Thickness(0),
                Cursor = Cursors.Hand,
                FontWeight = FontWeights.Bold
            };
            btnProceed.Click += (s, e) => { ExecuteUninstall(); };
            btnSp.Children.Add(btnProceed);

            sp.Children.Add(btnSp);
            g.Children.Add(sp);
            return g;
        }

        private Grid CreateFooter()
        {
            Grid f = new Grid { Background = new SolidColorBrush(Color.FromRgb(8, 11, 20)) };
            TextBlock t = new TextBlock
            {
                Text = "AspireSense 2026 | Universal Fan Control & Telemetry (Acer, ASUS, Lenovo, HP, Dell) | github.com/netfreakkk/AspireSense",
                FontSize = 9.5,
                Foreground = new SolidColorBrush(Color.FromRgb(100, 130, 170)),
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(20, 0, 0, 0)
            };
            f.Children.Add(t);
            return f;
        }

        private Image GetEmbeddedLogoImage(double size)
        {
            try
            {
                Assembly asm = Assembly.GetExecutingAssembly();
                using (Stream s = asm.GetManifestResourceStream("logo.png"))
                {
                    if (s != null)
                    {
                        BitmapImage bmp = new BitmapImage();
                        bmp.BeginInit();
                        bmp.StreamSource = s;
                        bmp.CacheOption = BitmapCacheOption.OnLoad;
                        bmp.EndInit();

                        return new Image
                        {
                            Source = bmp,
                            Width = size,
                            Height = size,
                            Effect = new DropShadowEffect { Color = Color.FromRgb(0, 210, 255), BlurRadius = 14, ShadowDepth = 0, Opacity = 0.85 }
                        };
                    }
                }
            }
            catch { }
            return null;
        }

        private void StartInstallation()
        {
            string installDir = txtInstallDir.Text.Trim();
            if (string.IsNullOrEmpty(installDir))
            {
                MessageBox.Show("Please select a valid installation directory.", "AspireSense Setup", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            viewInstallOptions.Visibility = Visibility.Collapsed;
            viewProgress.Visibility = Visibility.Visible;

            bool makeDesktop = chkDesktopShortcut.IsChecked == true;
            bool makeStartMenu = chkStartMenuShortcut.IsChecked == true;
            bool enableStartup = chkStartup.IsChecked == true;

            Thread t = new Thread(new ThreadStart(delegate()
            {
                DoInstallWork(installDir, makeDesktop, makeStartMenu, enableStartup);
            }));
            t.IsBackground = true;
            t.Start();
        }

        private static string DetectChassisManufacturer()
        {
            try
            {
                using (RegistryKey key = Registry.LocalMachine.OpenSubKey(@"HARDWARE\DESCRIPTION\System\BIOS"))
                {
                    if (key != null)
                    {
                        string mfg = (key.GetValue("SystemManufacturer") ?? "").ToString();
                        string board = (key.GetValue("BaseBoardManufacturer") ?? "").ToString();
                        string prod = (key.GetValue("SystemProductName") ?? "").ToString();
                        string combined = (mfg + " " + board + " " + prod).ToLowerInvariant();

                        if (combined.Contains("asus") || combined.Contains("tuf") || combined.Contains("rog")) return "ASUS";
                        if (combined.Contains("lenovo") || combined.Contains("legion") || combined.Contains("ideapad") || combined.Contains("loq")) return "Lenovo";
                        if (combined.Contains("hp") || combined.Contains("hewlett") || combined.Contains("omen") || combined.Contains("victus")) return "HP";
                        if (combined.Contains("dell") || combined.Contains("alienware")) return "Dell";
                        if (combined.Contains("acer") || combined.Contains("aspire") || combined.Contains("nitro") || combined.Contains("predator")) return "Acer";
                    }
                }
            }
            catch { }
            return "Universal";
        }

        private void DoInstallWork(string installDir, bool makeDesktop, bool makeStartMenu, bool enableStartup)
        {
            try
            {
                UpdateProgress(15, "Creating destination folder...", "Directory: " + installDir);
                if (!Directory.Exists(installDir))
                {
                    Directory.CreateDirectory(installDir);
                }
                Thread.Sleep(300);

                string detectedMfg = DetectChassisManufacturer();
                UpdateProgress(20, "Detecting Hardware Architecture...", "Target: " + detectedMfg + " (Tailoring deployment payload)");
                Thread.Sleep(200);

                // Extract Embedded Files - conditionally load InsydeDCHU.dll only if Acer hardware is detected!
                List<string> filesList = new List<string>();
                filesList.Add("AspireSense.exe");
                if (detectedMfg == "Acer")
                {
                    filesList.Add("InsydeDCHU.dll");
                }
                filesList.Add("logo.png");
                filesList.Add("app.ico");
                filesList.Add("README.md");
                filesList.Add("LICENSE");

                string[] filesToExtract = filesList.ToArray();
                Assembly asm = Assembly.GetExecutingAssembly();

                for (int i = 0; i < filesToExtract.Length; i++)
                {
                    string fName = filesToExtract[i];
                    int pct = 25 + (int)((i / (double)filesToExtract.Length) * 45);
                    UpdateProgress(pct, "Extracting: " + fName, "Unpacking standalone binaries...");

                    using (Stream src = asm.GetManifestResourceStream(fName))
                    {
                        if (src != null)
                        {
                            string destFile = IOPath.Combine(installDir, fName);
                            using (FileStream fs = new FileStream(destFile, FileMode.Create, FileAccess.Write))
                            {
                                src.CopyTo(fs);
                            }
                        }
                    }
                    Thread.Sleep(150);
                }

                // Copy uninstaller into destination
                UpdateProgress(75, "Configuring Uninstaller...", "Writing Uninstall.exe...");
                string installerExe = Assembly.GetExecutingAssembly().Location;
                string destUninstall = IOPath.Combine(installDir, "Uninstall.exe");
                try
                {
                    File.Copy(installerExe, destUninstall, true);
                }
                catch { }

                // Create Shortcuts
                UpdateProgress(85, "Configuring Windows Shortcuts...", "Registering shell shortcuts...");
                string appExe = IOPath.Combine(installDir, "AspireSense.exe");
                string appIco = IOPath.Combine(installDir, "app.ico");

                if (makeDesktop)
                {
                    string desktopPath = IOPath.Combine(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), "AspireSense.lnk");
                    CreateShellShortcut(desktopPath, appExe, appIco, installDir, "AspireSense - Hardware Fan Control");
                }

                if (makeStartMenu)
                {
                    string startMenuFolder = IOPath.Combine(Environment.GetFolderPath(Environment.SpecialFolder.StartMenu), "Programs");
                    string startMenuShortcut = IOPath.Combine(startMenuFolder, "AspireSense.lnk");
                    CreateShellShortcut(startMenuShortcut, appExe, appIco, installDir, "AspireSense - Hardware Fan Control");
                }

                if (enableStartup)
                {
                    try
                    {
                        using (RegistryKey key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run", true))
                        {
                            if (key != null) key.SetValue("AspireSense", "\"" + appExe + "\"");
                        }
                    }
                    catch { }
                }

                // Register Windows Add/Remove Programs
                UpdateProgress(95, "Registering with Windows...", "Apps & Features entry...");
                RegisterWindowsUninstall(installDir, destUninstall, appIco);

                UpdateProgress(100, "Finalizing installation...", "Ready!");
                Thread.Sleep(400);

                Dispatcher.Invoke(new Action(delegate()
                {
                    viewProgress.Visibility = Visibility.Collapsed;
                    viewComplete.Visibility = Visibility.Visible;
                }));
            }
            catch (Exception ex)
            {
                Dispatcher.Invoke(new Action(delegate()
                {
                    MessageBox.Show("Installation failed: " + ex.Message, "AspireSense Setup Error", MessageBoxButton.OK, MessageBoxImage.Error);
                    this.Close();
                }));
            }
        }

        private void UpdateProgress(int pct, string status, string detail)
        {
            Dispatcher.Invoke(new Action(delegate()
            {
                if (progBar != null) progBar.Value = pct;
                if (lblStatus != null) lblStatus.Text = status;
                if (lblDetail != null) lblDetail.Text = detail;
            }));
        }

        private static void CreateShellShortcut(string shortcutPath, string targetPath, string iconPath, string workingDir, string description)
        {
            try
            {
                Type shellType = Type.GetTypeFromProgID("WScript.Shell");
                if (shellType != null)
                {
                    object shell = Activator.CreateInstance(shellType);
                    object shortcut = shellType.InvokeMember("CreateShortcut", BindingFlags.InvokeMethod, null, shell, new object[] { shortcutPath });
                    if (shortcut != null)
                    {
                        Type scType = shortcut.GetType();
                        scType.InvokeMember("TargetPath", BindingFlags.SetProperty, null, shortcut, new object[] { targetPath });
                        scType.InvokeMember("WorkingDirectory", BindingFlags.SetProperty, null, shortcut, new object[] { workingDir });
                        scType.InvokeMember("Description", BindingFlags.SetProperty, null, shortcut, new object[] { description });
                        if (File.Exists(iconPath))
                        {
                            scType.InvokeMember("IconLocation", BindingFlags.SetProperty, null, shortcut, new object[] { iconPath });
                        }
                        scType.InvokeMember("Save", BindingFlags.InvokeMethod, null, shortcut, null);
                    }
                }
            }
            catch { }
        }

        private static void RegisterWindowsUninstall(string installDir, string uninstallPath, string iconPath)
        {
            try
            {
                using (RegistryKey key = Registry.CurrentUser.CreateSubKey(@"Software\Microsoft\Windows\CurrentVersion\Uninstall\AspireSense"))
                {
                    if (key != null)
                    {
                        key.SetValue("DisplayName", "AspireSense");
                        key.SetValue("DisplayVersion", "1.0.0");
                        key.SetValue("Publisher", "netfreakkk");
                        key.SetValue("DisplayIcon", iconPath);
                        key.SetValue("InstallLocation", installDir);
                        key.SetValue("UninstallString", "\"" + uninstallPath + "\" /uninstall");
                        key.SetValue("InstallDate", DateTime.Now.ToString("yyyyMMdd"));
                        key.SetValue("EstimatedSize", 4800, RegistryValueKind.DWord);
                        key.SetValue("HelpLink", "https://github.com/netfreakkk/AspireSense");
                        key.SetValue("URLInfoAbout", "https://github.com/netfreakkk/AspireSense");
                        key.SetValue("NoModify", 1, RegistryValueKind.DWord);
                        key.SetValue("NoRepair", 1, RegistryValueKind.DWord);
                    }
                }
            }
            catch { }
        }

        private void ExecuteUninstall()
        {
            try
            {
                // Kill running AspireSense processes
                Process[] procs = Process.GetProcessesByName("AspireSense");
                foreach (Process p in procs)
                {
                    try { p.CloseMainWindow(); } catch { }
                    try { p.Kill(); } catch { }
                }

                // Remove Desktop shortcut
                string desktopPath = IOPath.Combine(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), "AspireSense.lnk");
                if (File.Exists(desktopPath)) { try { File.Delete(desktopPath); } catch { } }

                // Remove Start Menu shortcut
                string startMenuShortcut = IOPath.Combine(Environment.GetFolderPath(Environment.SpecialFolder.StartMenu), "Programs", "AspireSense.lnk");
                if (File.Exists(startMenuShortcut)) { try { File.Delete(startMenuShortcut); } catch { } }

                // Remove Run on Startup registry
                try
                {
                    using (RegistryKey key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run", true))
                    {
                        if (key != null) key.DeleteValue("AspireSense", false);
                    }
                }
                catch { }

                // Remove Add/Remove Programs entry
                try
                {
                    Registry.CurrentUser.DeleteSubKeyTree(@"Software\Microsoft\Windows\CurrentVersion\Uninstall\AspireSense", false);
                }
                catch { }

                // Remove application files
                string installDir = IOPath.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
                string[] filesToDelete = new string[] { "AspireSense.exe", "InsydeDCHU.dll", "logo.png", "app.ico", "README.md", "LICENSE" };
                foreach (string f in filesToDelete)
                {
                    string p = IOPath.Combine(installDir, f);
                    if (File.Exists(p)) { try { File.Delete(p); } catch { } }
                }

                // Self delete script
                string batchScript = IOPath.Combine(IOPath.GetTempPath(), "remove_aspiresense.bat");
                string batchContent = string.Format("@echo off\r\ntimeout /t 1 /nobreak >nul\r\nrmdir /s /q \"{0}\"\r\ndel \"%~f0\"\r\n", installDir);
                File.WriteAllText(batchScript, batchContent);
                Process.Start(new ProcessStartInfo
                {
                    FileName = batchScript,
                    WindowStyle = ProcessWindowStyle.Hidden,
                    CreateNoWindow = true
                });

                MessageBox.Show("AspireSense has been successfully uninstalled from your PC.", "Uninstallation Complete", MessageBoxButton.OK, MessageBoxImage.Information);
                this.Close();
            }
            catch (Exception ex)
            {
                MessageBox.Show("Error during uninstall: " + ex.Message, "Uninstall Error", MessageBoxButton.OK, MessageBoxImage.Error);
                this.Close();
            }
        }
    }

    class Program
    {
        [STAThread]
        static void Main(string[] args)
        {
            bool isUninstall = false;
            foreach (string arg in args)
            {
                if (arg.Equals("/uninstall", StringComparison.OrdinalIgnoreCase) || arg.Equals("-uninstall", StringComparison.OrdinalIgnoreCase))
                {
                    isUninstall = true;
                }
            }

            // Also check if running as Uninstall.exe
            string currentExeName = IOPath.GetFileName(Assembly.GetExecutingAssembly().Location);
            if (currentExeName.IndexOf("uninstall", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                isUninstall = true;
            }

            Application app = new Application();
            app.Run(new SetupWindow(isUninstall));
        }
    }
}
