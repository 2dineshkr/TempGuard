using System;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Security.Cryptography;
using Microsoft.Win32;

namespace TempGuard
{
    internal static class SensorSupport
    {
        private const string PawnIoUninstallKey = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\PawnIO";
        private const string InstallerUrl =
            "https://github.com/namazso/PawnIO.Setup/releases/download/2.1.0/PawnIO_setup.exe";
        private const string InstallerSha256 =
            "A3A46226C5E2824F4CDD42BE0EECBABFC672C86F7889710F5AB1E6AD385B47A0";

        public static string InstallerPath
        {
            get
            {
                return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "TempGuard", "Downloads", "PawnIO_setup-2.1.0.exe");
            }
        }

        public static bool IsInstalled
        {
            get
            {
                try
                {
                    using (RegistryKey baseKey = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64))
                    using (RegistryKey key = baseKey.OpenSubKey(PawnIoUninstallKey, false))
                        if (key != null) return true;

                    using (RegistryKey baseKey = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry32))
                    using (RegistryKey key = baseKey.OpenSubKey(PawnIoUninstallKey, false))
                        return key != null;
                }
                catch { return false; }
            }
        }

        public static bool EnsureInstallerDownloaded(out string error)
        {
            error = null;
            if (InstallerIsAuthentic()) return true;

            string folder = Path.GetDirectoryName(InstallerPath);
            string downloadPath = InstallerPath + ".download";
            try
            {
                Directory.CreateDirectory(folder);
                if (File.Exists(downloadPath)) File.Delete(downloadPath);
                ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12;
                using (WebClient client = new WebClient())
                {
                    client.Headers.Add(HttpRequestHeader.UserAgent, "TempGuard/1.0");
                    client.DownloadFile(InstallerUrl, downloadPath);
                }

                if (!FileHasExpectedHash(downloadPath))
                    throw new InvalidDataException("The downloaded installer did not match the published SHA-256 hash.");

                if (File.Exists(InstallerPath)) File.Delete(InstallerPath);
                File.Move(downloadPath, InstallerPath);
                return true;
            }
            catch (Exception ex)
            {
                try { if (File.Exists(downloadPath)) File.Delete(downloadPath); }
                catch { }
                error = ex.Message;
                return false;
            }
        }

        public static bool InstallerIsAuthentic()
        {
            return FileHasExpectedHash(InstallerPath);
        }

        private static bool FileHasExpectedHash(string path)
        {
            if (!File.Exists(path)) return false;
            try
            {
                using (FileStream stream = File.OpenRead(path))
                using (SHA256 sha = SHA256.Create())
                {
                    byte[] hash = sha.ComputeHash(stream);
                    return string.Equals(BitConverter.ToString(hash).Replace("-", ""), InstallerSha256,
                        StringComparison.OrdinalIgnoreCase);
                }
            }
            catch { return false; }
        }

        public static Process StartInstaller()
        {
            return Process.Start(new ProcessStartInfo
            {
                FileName = InstallerPath,
                Arguments = "-install",
                UseShellExecute = true,
                Verb = "runas"
            });
        }
    }
}
