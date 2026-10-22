using System;
using System.Threading;
using System.Windows.Forms;

namespace TempGuard
{
    internal static class Program
    {
        private static Mutex _mutex;

        [STAThread]
        private static void Main(string[] args)
        {
            bool ownsMutex;
            _mutex = new Mutex(true, "Local\\TempGuard.SingleInstance", out ownsMutex);
            bool waitForPreviousInstance = Array.Exists(args, delegate(string value)
            {
                return string.Equals(value, "--wait-for-instance", StringComparison.OrdinalIgnoreCase);
            });

            if (!ownsMutex && waitForPreviousInstance)
            {
                try { ownsMutex = _mutex.WaitOne(6000); }
                catch (AbandonedMutexException) { ownsMutex = true; }
            }

            if (!ownsMutex)
            {
                MessageBox.Show("TempGuard is already running in the system tray.", "TempGuard",
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            bool startMinimized = Array.Exists(args, delegate(string value)
            {
                return string.Equals(value, "--minimized", StringComparison.OrdinalIgnoreCase);
            });

            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            using (MainForm form = new MainForm(startMinimized))
            {
                Application.Run(form);
            }

            _mutex.ReleaseMutex();
            _mutex.Dispose();
        }
    }
}
