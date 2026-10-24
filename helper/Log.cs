using System;
using System.IO;

namespace NestZoneDiscord
{
    internal static class Log
    {
        private static readonly object Gate = new object();
        private const long MaxBytes = 256 * 1024;

        internal static void Write(string line)
        {
            try
            {
                lock (Gate)
                {
                    string folder = Program.DataFolder();
                    if (!Directory.Exists(folder)) Directory.CreateDirectory(folder);
                    string path = Path.Combine(folder, "helper.log");
                    try
                    {
                        FileInfo fi = new FileInfo(path);
                        if (fi.Exists && fi.Length > MaxBytes) File.Delete(path);
                    }
                    catch { }
                    string stamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
                    File.AppendAllText(path, stamp + "  " + line + "\r\n");
                }
            }
            catch { }
        }
    }
}
