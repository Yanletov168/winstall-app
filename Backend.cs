using System;
using System.IO;

namespace winstall
{
    /// <summary>
    /// WinGet backend selection. winstall never reimplements the package manager:
    /// it uses the system-wide winget and remembers a working location in
    /// options.ini (winget_path key, next to the exe, AppData fallback). A missing backend is
    /// re-probed on every launch so installing App Installer later just works;
    /// a backend that fails at runtime invalidates the cache and is re-detected once.
    /// </summary>
    public static class Backend
    {
        public const string DownloadPageUrl = "https://github.com/microsoft/winget-cli/releases/latest";

        private static bool probed;
        private static string exe;

        public static string ResolveExe()
        {
            if (probed) return exe;
            probed = true;
            exe = Options.WingetPath;
            // Empty and stale entries both mean "unknown": detect live.
            if (string.IsNullOrWhiteSpace(exe) || !File.Exists(exe)) exe = null;
            if (exe == null)
            {
                exe = WingetRunner.FindWinget();
                if (exe != null)
                {
                    Options.WingetPath = exe;
                    Options.Save();
                }
            }
            return exe;
        }

        public static void Invalidate()
        {
            probed = false;
            exe = null;
            Options.WingetPath = "";
            Options.Save();
        }
    }
}
