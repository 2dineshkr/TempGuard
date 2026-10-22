using System;
using System.Globalization;
using System.IO;

namespace TempGuard
{
    internal sealed class AppSettings
    {
        public int WarningCelsius { get; set; }
        public int CriticalCelsius { get; set; }
        public bool StartWithWindows { get; set; }
        public bool StartupPromptShown { get; set; }

        private static string SettingsFolder
        {
            get { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "TempGuard"); }
        }

        private static string SettingsFile
        {
            get { return Path.Combine(SettingsFolder, "settings.ini"); }
        }

        public static AppSettings Load()
        {
            AppSettings settings = new AppSettings
            {
                WarningCelsius = 75,
                CriticalCelsius = 88,
                StartWithWindows = StartupManager.IsEnabled,
                StartupPromptShown = false
            };

            try
            {
                if (!File.Exists(SettingsFile)) return settings;
                foreach (string raw in File.ReadAllLines(SettingsFile))
                {
                    string[] parts = raw.Split(new[] { '=' }, 2);
                    if (parts.Length != 2) continue;
                    int number;
                    bool flag;
                    if (parts[0] == "WarningCelsius" && int.TryParse(parts[1], NumberStyles.Integer,
                        CultureInfo.InvariantCulture, out number)) settings.WarningCelsius = number;
                    if (parts[0] == "CriticalCelsius" && int.TryParse(parts[1], NumberStyles.Integer,
                        CultureInfo.InvariantCulture, out number)) settings.CriticalCelsius = number;
                    if (parts[0] == "StartupPromptShown" && bool.TryParse(parts[1], out flag))
                        settings.StartupPromptShown = flag;
                }
            }
            catch { }

            settings.WarningCelsius = Math.Max(45, Math.Min(100, settings.WarningCelsius));
            settings.CriticalCelsius = Math.Max(settings.WarningCelsius + 1, Math.Min(110, settings.CriticalCelsius));
            return settings;
        }

        public void Save()
        {
            Directory.CreateDirectory(SettingsFolder);
            File.WriteAllLines(SettingsFile, new[]
            {
                "WarningCelsius=" + WarningCelsius.ToString(CultureInfo.InvariantCulture),
                "CriticalCelsius=" + CriticalCelsius.ToString(CultureInfo.InvariantCulture),
                "StartupPromptShown=" + StartupPromptShown
            });
        }
    }
}
