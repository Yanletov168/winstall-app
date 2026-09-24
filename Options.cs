using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace winstall
{
    /// <summary>
    /// Single configuration file (options.ini next to the exe, AppData fallback).
    /// Owns the UI language (migrated from the legacy language.txt), the cached
    /// winget location, the custom installer-log directory and the silent mode.
    /// Safe to edit by hand while the app is closed.
    /// </summary>
    public static class Options
    {
        internal static string DirOverride { get; set; }

        private static bool loaded;
        private static string language = "";
        private static string wingetPath = "";
        private static string logsDir = "";
        private static bool silent = true;

        // "" = follow the OS language.
        public static string Language
        {
            get { EnsureLoaded(); return language; }
            set { EnsureLoaded(); language = (value ?? "").Trim(); }
        }

        // "" = backend unknown, detect on next launch.
        public static string WingetPath
        {
            get { EnsureLoaded(); return wingetPath; }
            set { EnsureLoaded(); wingetPath = (value ?? "").Trim(); }
        }

        // "" = winget default log location.
        public static string LogsDir
        {
            get { EnsureLoaded(); return logsDir; }
            set { EnsureLoaded(); logsDir = (value ?? "").Trim(); }
        }

        public static bool Silent
        {
            get { EnsureLoaded(); return silent; }
            set { EnsureLoaded(); silent = value; }
        }

        public static void EnsureLoaded()
        {
            if (loaded) return;
            loaded = true;
            Load();
        }

        internal static void Reset()
        {
            loaded = false;
            language = "";
            wingetPath = "";
            logsDir = "";
            silent = true;
        }

        public static void Save()
        {
            EnsureLoaded();
            var sb = new StringBuilder();
            sb.AppendLine("; winstall options. Safe to edit by hand while the app is closed.");
            sb.AppendLine("[winstall]");
            sb.AppendLine("language=" + language);
            sb.AppendLine("winget_path=" + wingetPath);
            sb.AppendLine("logs_dir=" + logsDir);
            sb.AppendLine("silent=" + (silent ? "true" : "false"));
            foreach (var p in ConfigFiles())
            {
                try
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(p));
                    File.WriteAllText(p, sb.ToString(), new UTF8Encoding(false));
                    return;
                }
                catch { }
            }
        }

        private static string[] ConfigFiles()
        {
            if (!string.IsNullOrWhiteSpace(DirOverride))
                return new string[] { Path.Combine(DirOverride, "options.ini") };
            return new string[]
            {
                Path.Combine(L.ExeDir, "options.ini"),
                Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "winstall", "options.ini")
            };
        }

        private static void Load()
        {
            var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var p in ConfigFiles())
            {
                try
                {
                    if (!File.Exists(p)) continue;
                    ParseInto(File.ReadAllText(p, Encoding.UTF8), map);
                    break;
                }
                catch { }
            }
            string v;
            if (map.TryGetValue("language", out v)) language = v.Trim();
            if (map.TryGetValue("winget_path", out v)) wingetPath = v.Trim();
            if (map.TryGetValue("logs_dir", out v)) logsDir = v.Trim();
            if (map.TryGetValue("silent", out v))
                silent = v.Trim().Equals("true", StringComparison.OrdinalIgnoreCase);
            if (language == "") ImportLegacyLanguage();
        }

        private static void ParseInto(string text, Dictionary<string, string> map)
        {
            foreach (var raw in text.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries))
            {
                string line = raw.Trim();
                if (line.Length == 0 || line[0] == ';' || line[0] == '#' || line.StartsWith("["))
                    continue;
                int eq = line.IndexOf('=');
                if (eq <= 0) continue;
                map[line.Substring(0, eq).Trim()] = line.Substring(eq + 1).Trim();
            }
        }

        private static void ImportLegacyLanguage()
        {
            foreach (var p in LegacyLanguageFiles())
            {
                try
                {
                    if (!File.Exists(p)) continue;
                    string code = File.ReadAllText(p, Encoding.UTF8).Trim();
                    if (code.Length > 0) language = code;
                    try { File.Delete(p); } catch { }
                    Save();
                    break;
                }
                catch { }
            }
        }

        private static string[] LegacyLanguageFiles()
        {
            if (!string.IsNullOrWhiteSpace(DirOverride))
                return new string[] { Path.Combine(DirOverride, "language.txt") };
            return new string[]
            {
                Path.Combine(L.ExeDir, "language.txt"),
                Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "winstall", "language.txt")
            };
        }
    }
}
