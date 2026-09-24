using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace winstall
{
    /// <summary>
    /// Single configuration file (options.ini next to the exe, AppData fallback).
    /// Owns the UI language (migrated from the legacy language.txt), the cached
    /// winget location, the custom installer-log directory and the per-verb
    /// flag sets. There are no hidden flags: what runs is exactly
    /// "winget {verb} --id ... {per-verb set}". Safe to edit by hand
    /// while the app is closed.
    /// </summary>
    public static class Options
    {
        public const string DefaultUpdFlags =
            "--accept-package-agreements --accept-source-agreements --disable-interactivity --silent";
        public const string DefaultAddFlags =
            "--accept-package-agreements --accept-source-agreements --disable-interactivity --silent";
        // uninstall has no --accept-package-agreements: passing it makes winget
        // print usage and exit 0x8A150002, hence the separate default.
        public const string DefaultRemFlags =
            "--accept-source-agreements --disable-interactivity --silent";

        internal static string DirOverride { get; set; }

        private static bool loaded;
        private static string language = "";
        private static string wingetPath = "";
        private static string logsDir = "";
        private static string updFlags = DefaultUpdFlags;
        private static string addFlags = DefaultAddFlags;
        private static string remFlags = DefaultRemFlags;
        private static string theme = "system";

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

        // One flag set per mutating verb. Empty means "bare winget call"
        // (note: without --disable-interactivity winget may prompt and hang).
        public static string UpdFlags        {
            get { EnsureLoaded(); return updFlags; }
            set { EnsureLoaded(); updFlags = (value ?? "").Trim(); }
        }

        public static string AddFlags
        {
            get { EnsureLoaded(); return addFlags; }
            set { EnsureLoaded(); addFlags = (value ?? "").Trim(); }
        }

        public static string RemFlags
        {
            get { EnsureLoaded(); return remFlags; }
            set { EnsureLoaded(); remFlags = (value ?? "").Trim(); }
        }

        // light, dark or system. Anything else falls back to system.
        public static string Theme
        {
            get { EnsureLoaded(); return theme; }
            set
            {
                EnsureLoaded();
                value = (value ?? "").Trim().ToLowerInvariant();
                theme = (value == "light" || value == "dark") ? value : "system";
            }
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
            updFlags = DefaultUpdFlags;
            addFlags = DefaultAddFlags;
            remFlags = DefaultRemFlags;
            theme = "system";
        }

        public static void Save()
        {
            EnsureLoaded();
            var sb = new StringBuilder();
            sb.AppendLine("[winstall]");
            sb.AppendLine("language=" + language);
            sb.AppendLine("winget_path=" + wingetPath);
            sb.AppendLine("logs_dir=" + logsDir);
            sb.AppendLine("upd_flags=" + updFlags);
            sb.AppendLine("add_flags=" + addFlags);
            sb.AppendLine("rem_flags=" + remFlags);
            sb.AppendLine("theme=" + theme);
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
            if (map.TryGetValue("upd_flags", out v)) updFlags = v.Trim();
            if (map.TryGetValue("add_flags", out v)) addFlags = v.Trim();
            if (map.TryGetValue("rem_flags", out v)) remFlags = v.Trim();
            if (map.TryGetValue("theme", out v)) Theme = v;
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
