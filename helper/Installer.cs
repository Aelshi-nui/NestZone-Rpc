using System;
using System.IO;
using System.Text;
using System.Diagnostics;
using System.Collections.Generic;
using Microsoft.Win32;
using System.Web.Script.Serialization;

namespace NestZoneDiscord
{
    internal static class Installer
    {
        private static readonly string[] ChromeKeys = new string[]
        {
            @"Software\Google\Chrome\NativeMessagingHosts\",
            @"Software\Microsoft\Edge\NativeMessagingHosts\",
            @"Software\Chromium\NativeMessagingHosts\",
            @"Software\BraveSoftware\Brave-Browser\NativeMessagingHosts\"
        };
        private const string FirefoxKey = @"Software\Mozilla\NativeMessagingHosts\";
        private const string UninstallKey =
            @"Software\Microsoft\Windows\CurrentVersion\Uninstall\NestZoneDiscord";

        internal static void Install()
        {
            string folder = Program.DataFolder();
            if (!Directory.Exists(folder)) Directory.CreateDirectory(folder);
            string targetExe = Path.Combine(folder, "NestZoneDiscord.exe");
            string currentExe = Process.GetCurrentProcess().MainModule.FileName;

            if (!PathEquals(currentExe, targetExe))
            {
                CopyExe(currentExe, targetExe);
            }

            string chromeManifest = Path.Combine(folder, "chrome-host.json");
            string firefoxManifest = Path.Combine(folder, "firefox-host.json");
            File.WriteAllText(chromeManifest, BuildChromeManifest(), new UTF8Encoding(false));
            File.WriteAllText(firefoxManifest, BuildFirefoxManifest(targetExe), new UTF8Encoding(false));

            for (int i = 0; i < ChromeKeys.Length; i++)
            {
                SetDefault(ChromeKeys[i] + Program.HostName, chromeManifest);
            }
            SetDefault(FirefoxKey + Program.HostName, firefoxManifest);

            WriteUninstallEntry(targetExe, folder);
            Log.Write("Installed to " + folder);
        }

        private static void CopyExe(string src, string dst)
        {
            try
            {
                if (File.Exists(dst))
                {
                    KillOtherHosts(dst);
                    try { File.Delete(dst); }
                    catch
                    {
                        string tmp = dst + ".new";
                        File.Copy(src, tmp, true);
                        try { File.Delete(dst); } catch { }
                        File.Move(tmp, dst);
                        return;
                    }
                }
                File.Copy(src, dst, true);
            }
            catch (Exception ex) { throw new Exception("Could not copy helper: " + ex.Message); }
        }

        private static void KillOtherHosts(string exePath)
        {
            try
            {
                int me = Process.GetCurrentProcess().Id;
                Process[] all = Process.GetProcessesByName("NestZoneDiscord");
                for (int i = 0; i < all.Length; i++)
                {
                    try
                    {
                        if (all[i].Id == me) continue;
                        all[i].Kill();
                        all[i].WaitForExit(3000);
                    }
                    catch { }
                }
            }
            catch { }
        }

        private static string BuildChromeManifest()
        {
            Dictionary<string, object> m = new Dictionary<string, object>();
            m["name"] = Program.HostName;
            m["description"] = "NestZone for Discord";
            m["path"] = "NestZoneDiscord.exe";
            m["type"] = "stdio";
            m["allowed_origins"] = new string[] { "chrome-extension://" + Program.ChromeExtId + "/" };
            return new JavaScriptSerializer().Serialize(m);
        }

        private static string BuildFirefoxManifest(string absExe)
        {
            Dictionary<string, object> m = new Dictionary<string, object>();
            m["name"] = Program.HostName;
            m["description"] = "NestZone for Discord";
            m["path"] = absExe;
            m["type"] = "stdio";
            m["allowed_extensions"] = new string[] { Program.FirefoxExtId };
            return new JavaScriptSerializer().Serialize(m);
        }

        private static void SetDefault(string subKey, string value)
        {
            using (RegistryKey k = Registry.CurrentUser.CreateSubKey(subKey))
            {
                if (k != null) k.SetValue(null, value, RegistryValueKind.String);
            }
        }

        private static void WriteUninstallEntry(string exe, string folder)
        {
            using (RegistryKey k = Registry.CurrentUser.CreateSubKey(UninstallKey))
            {
                if (k == null) return;
                k.SetValue("DisplayName", "NestZone for Discord", RegistryValueKind.String);
                k.SetValue("DisplayVersion", Program.Version, RegistryValueKind.String);
                k.SetValue("Publisher", "NestZone", RegistryValueKind.String);
                k.SetValue("DisplayIcon", exe, RegistryValueKind.String);
                k.SetValue("UninstallString", "\"" + exe + "\" --uninstall", RegistryValueKind.String);
                k.SetValue("InstallLocation", folder, RegistryValueKind.String);
                k.SetValue("NoModify", 1, RegistryValueKind.DWord);
                k.SetValue("NoRepair", 1, RegistryValueKind.DWord);
            }
        }

        internal static void Uninstall(bool quiet)
        {
            for (int i = 0; i < ChromeKeys.Length; i++)
            {
                DeleteKey(ChromeKeys[i] + Program.HostName);
            }
            DeleteKey(FirefoxKey + Program.HostName);
            DeleteKey(UninstallKey);

            string folder = Program.DataFolder();
            string currentExe = Process.GetCurrentProcess().MainModule.FileName;
            bool runningFromFolder = IsUnder(currentExe, folder);

            if (Directory.Exists(folder) && IsOurFolder(folder))
            {
                if (runningFromFolder)
                {
                    ScheduleFolderDelete(folder);
                }
                else
                {
                    try { Directory.Delete(folder, true); } catch (Exception ex) { Log.Write("del folder: " + ex.Message); }
                }
            }
            Log.Write("Uninstalled");
        }

        private static void ScheduleFolderDelete(string folder)
        {
            if (!IsOurFolder(folder)) { Log.Write("refusing to delete " + folder); return; }
            try
            {
                ProcessStartInfo psi = new ProcessStartInfo();
                // Full path: a bare "cmd.exe" is searched in the exe's own
                // folder first, where a planted copy could be run instead.
                psi.FileName = Path.Combine(Environment.SystemDirectory, "cmd.exe");
                psi.Arguments = "/c ping 127.0.0.1 -n 3 >nul & rmdir /s /q \"" + folder + "\"";
                psi.CreateNoWindow = true;
                psi.UseShellExecute = false;
                psi.WindowStyle = ProcessWindowStyle.Hidden;
                Process.Start(psi);
            }
            catch (Exception ex) { Log.Write("sched del: " + ex.Message); }
        }

        // Only ever delete %LOCALAPPDATA%\NestZone\Discord, never a drive root
        // or some other folder if the known-folder path is odd.
        private static bool IsOurFolder(string folder)
        {
            try
            {
                string f = Path.GetFullPath(folder).TrimEnd('\\');
                string local = Path.GetFullPath(Environment.GetFolderPath(
                    Environment.SpecialFolder.LocalApplicationData)).TrimEnd('\\');
                if (local.Length < 4) return false;
                string expected = local + @"\NestZone\Discord";
                return string.Equals(f, expected, StringComparison.OrdinalIgnoreCase);
            }
            catch { return false; }
        }

        private static void DeleteKey(string subKey)
        {
            try { Registry.CurrentUser.DeleteSubKeyTree(subKey, false); }
            catch (Exception ex) { Log.Write("del key " + subKey + ": " + ex.Message); }
        }

        private static bool PathEquals(string a, string b)
        {
            try { return string.Equals(Path.GetFullPath(a), Path.GetFullPath(b), StringComparison.OrdinalIgnoreCase); }
            catch { return string.Equals(a, b, StringComparison.OrdinalIgnoreCase); }
        }

        private static bool IsUnder(string path, string folder)
        {
            try
            {
                string p = Path.GetFullPath(path);
                string f = Path.GetFullPath(folder);
                if (!f.EndsWith("\\")) f += "\\";
                return p.StartsWith(f, StringComparison.OrdinalIgnoreCase);
            }
            catch { return false; }
        }
    }
}

