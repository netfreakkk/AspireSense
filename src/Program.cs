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
    public static class Program
    {
        private static System.Threading.Mutex singleMutex = null;

        [STAThread]
        public static void Main()
        {
            bool createdNew = true;
            try
            {
                singleMutex = new System.Threading.Mutex(true, "Local\\AspireSense_SingleInstance_App", out createdNew);
            }
            catch
            {
                createdNew = true;
            }

            if (!createdNew)
            {
                MessageBox.Show("AspireSense is already running.", "AspireSense", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            try
            {
                Application app = new Application();
                app.Run(new MainWindow());
            }
            finally
            {
                if (singleMutex != null)
                {
                    try { singleMutex.ReleaseMutex(); } catch { }
                    singleMutex.Close();
                }
            }
        }
    }
}