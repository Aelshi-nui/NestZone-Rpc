using System;
using System.Windows.Forms;

namespace NestZoneDiscord
{
    internal static class Program
    {
        internal const string HostName = "tech.nestzone.discord";
        internal const string ChromeExtId = "fkpdelihmdmckifbemjjfpblaoingmen";
        internal const string FirefoxExtId = "discord@nestzone.tech";
        internal const string ClientId = "1558768405585203200";
        internal const string Version = "1.0.1";
        internal const string ExeUrl = "https://github.com/Aelshi-nui/NestZone-Rpc/releases/latest/download/NestZoneDiscord.exe";

        private const uint LOAD_LIBRARY_SEARCH_SYSTEM32 = 0x00000800;

        [System.Runtime.InteropServices.DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool SetDefaultDllDirectories(uint flags);

        [System.Runtime.InteropServices.DllImport("kernel32.dll", SetLastError = true, CharSet = System.Runtime.InteropServices.CharSet.Unicode)]
        private static extern bool SetDllDirectory(string path);

        // The exe is often started from the Downloads folder. Make every later
        // DLL load come from System32 only, so a DLL dropped next to the exe
        // cannot be loaded into this process (DLL planting).
        private static void HardenDllSearch()
        {
            try { SetDllDirectory(""); } catch { }
            try { SetDefaultDllDirectories(LOAD_LIBRARY_SEARCH_SYSTEM32); } catch { }
        }

        [STAThread]
        private static int Main(string[] args)
        {
            HardenDllSearch();
            try
            {
                if (IsNativeHostLaunch(args))
                {
                    NativeHost host = new NativeHost();
                    host.Run();
                    return 0;
                }

                bool quiet = HasArg(args, "--quiet");
                if (HasArg(args, "--uninstall"))
                {
                    Installer.Uninstall(quiet);
                    return 0;
                }

                // --install, or no args (double-click): install.
                Installer.Install();
                if (!quiet)
                {
                    MessageBox.Show(
                        "NestZone for Discord is set up. Now add the NestZone for Discord extension to your browser. Keep the Discord app open while you watch.",
                        "NestZone for Discord", MessageBoxButtons.OK, MessageBoxIcon.Information);
                }
                return 0;
            }
            catch (Exception ex)
            {
                if (!HasArg(args, "--quiet"))
                {
                    MessageBox.Show("NestZone for Discord could not finish:\r\n\r\n" + ex.Message,
                        "NestZone for Discord", MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
                return 1;
            }
        }

        private static bool IsNativeHostLaunch(string[] args)
        {
            if (args == null || args.Length == 0) return false;
            string a0 = args[0];
            if (a0.StartsWith("chrome-extension://", StringComparison.OrdinalIgnoreCase)) return true;
            for (int i = 0; i < args.Length; i++)
            {
                string a = args[i];
                if (a.EndsWith(".json", StringComparison.OrdinalIgnoreCase)) return true;
                if (string.Equals(a, FirefoxExtId, StringComparison.OrdinalIgnoreCase)) return true;
            }
            return false;
        }

        private static bool HasArg(string[] args, string name)
        {
            if (args == null) return false;
            for (int i = 0; i < args.Length; i++)
            {
                if (string.Equals(args[i], name, StringComparison.OrdinalIgnoreCase)) return true;
            }
            return false;
        }

        internal static string DataFolder()
        {
            string local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            return System.IO.Path.Combine(System.IO.Path.Combine(local, "NestZone"), "Discord");
        }
    }
}
