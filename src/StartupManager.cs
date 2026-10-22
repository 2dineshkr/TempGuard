using Microsoft.Win32;

namespace TempGuard
{
    internal static class StartupManager
    {
        private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
        private const string RunValue = "TempGuard";

        public static bool IsEnabled
        {
            get
            {
                try
                {
                    using (RegistryKey key = Registry.CurrentUser.OpenSubKey(RunKey, false))
                        return key != null && key.GetValue(RunValue) != null;
                }
                catch { return false; }
            }
        }

        public static bool SetEnabled(bool enabled, string executablePath)
        {
            try
            {
                using (RegistryKey key = Registry.CurrentUser.CreateSubKey(RunKey))
                {
                    if (key == null) return false;
                    if (enabled)
                        key.SetValue(RunValue, "\"" + executablePath + "\" --minimized", RegistryValueKind.String);
                    else
                        key.DeleteValue(RunValue, false);
                }
                return IsEnabled == enabled;
            }
            catch { return false; }
        }
    }
}
