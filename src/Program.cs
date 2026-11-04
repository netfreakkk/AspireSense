using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows;

namespace AspireSense
{
    public static class Program
    {
        private static Mutex singleMutex = null;

        [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Auto)]
        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        public static extern uint RegisterWindowMessage(string lpString);

        [DllImport("user32.dll")]
        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        public static extern bool PostMessage(IntPtr hWnd, uint Msg, IntPtr wParam, IntPtr lParam);

        public static readonly IntPtr HWND_BROADCAST = new IntPtr(0xffff);
        public const string MSG_SHOW_INSTANCE = "ASPIRESENSE_SHOW_RUNNING_WINDOW";

        [STAThread]
        public static void Main(string[] args)
        {
            bool createdNew = true;
            try
            {
                singleMutex = new Mutex(true, "Local\\AspireSense_SingleInstance_App", out createdNew);
            }
            catch
            {
                createdNew = true;
            }

            if (!createdNew)
            {
                // Another instance is already running in background/tray:
                // Broadcast custom message so the running window restores to foreground smoothly
                uint msg = RegisterWindowMessage(MSG_SHOW_INSTANCE);
                PostMessage(HWND_BROADCAST, msg, IntPtr.Zero, IntPtr.Zero);
                return;
            }

            bool startMinimized = false;
            if (args != null)
            {
                foreach (string arg in args)
                {
                    if (arg.Equals("--minimized", StringComparison.OrdinalIgnoreCase) ||
                        arg.Equals("-minimized", StringComparison.OrdinalIgnoreCase) ||
                        arg.Equals("/minimized", StringComparison.OrdinalIgnoreCase) ||
                        arg.Equals("--tray", StringComparison.OrdinalIgnoreCase))
                    {
                        startMinimized = true;
                        break;
                    }
                }
            }

            try
            {
                Application app = new Application();
                MainWindow mainWin = new MainWindow(startMinimized);
                app.Run(mainWin);
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
