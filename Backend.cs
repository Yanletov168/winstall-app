using System;
using System.IO;

namespace winstall
{
    /// <summary>
    /// WinGet backend selection. winstall never reimplements the package manager:
    /// it uses the system-wide winget and remembers a working location in
    /// backend.txt (next to the exe, AppData fallback). A missing backend is
    /// re-probed on every launch so installing App Installer later just works;
    /// a backend that fails at runtime invalidates the cache and is re-detected once.
    /// </summary>
    public static class Backend
    {
        public const string DownloadPageUrl = "https://github.com/microsoft/winget-cli/releases/latest";

        internal static string CacheDirOverride { get; set; }

        private static bool probed;
        private static string exe;

        public static string ResolveExe()
        {
            if (probed) return exe;
            probed = true;
            exe = ReadCache();
            if (exe != null) return exe;
            exe = WingetRunner.FindWinget();
            if (exe != null) WriteCache(exe);
            return exe;
        }

        public static void Invalidate()
        {
            probed = false;
            exe = null;
            foreach (var p in CacheFiles())
            {
                try { if (File.Exists(p)) File.Delete(p); } catch { }
            }
        }

        private static string[] CacheFiles()
        {
            if (!string.IsNullOrWhiteSpace(CacheDirOverride))
                return new string[] { Path.Combine(CacheDirOverride, "backend.txt") };
            return new string[]
            {
                Path.Combine(L.ExeDir, "backend.txt"),
                Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "winstall", "backend.txt")
            };
        }

        internal static string ReadCache()
        {
            foreach (var p in CacheFiles())
            {
                try
                {
                    if (!File.Exists(p)) continue;
                    string[] lines = File.ReadAllLines(p);
                    if (lines.Length >= 2 && lines[0].Trim() == "system" && File.Exists(lines[1].Trim()))
                        return lines[1].Trim();
                }
                catch { }
            }
            return null;
        }

        internal static void WriteCache(string path)
        {
            string content = "system" + "\r\n" + path + "\r\n";
            foreach (var p in CacheFiles())
            {
                try
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(p));
                    File.WriteAllText(p, content);
                    return;
                }
                catch { }
            }
        }
    }
}
