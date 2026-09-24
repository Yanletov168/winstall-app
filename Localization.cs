using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;

namespace winstall
{
    public class LangInfo
    {
        public string Code = "en";
        public string Name = "English";
        public string Path; // Null for the embedded English (the exe also runs standalone).
        public bool Embedded;
    }

    /// <summary>
    /// UI localization from JSON files in locales/ next to the executable.
    /// Dropping in locales/xx.json adds a language to the hamburger menu.
    /// en.json and ru.json ship with the app; embedded English covers the no-files case.
    /// </summary>
    public static class L
    {
        public static string ExeDir = AppDomain.CurrentDomain.BaseDirectory;
        public static string LocalesDir { get { return Path.Combine(ExeDir, "locales"); } }
        public static readonly List<LangInfo> Available = new List<LangInfo>();
        public static string Code = "en";
        private static Dictionary<string, string> current =
            new Dictionary<string, string>(StringComparer.Ordinal);

        public static string T(string key)
        {
            string v;
            if (current.TryGetValue(key, out v)) return v;
            if (embeddedEn.TryGetValue(key, out v)) return v;
            return key;
        }

        public static string F(string key, params object[] a)
        {
            return string.Format(T(key), a);
        }

        public static void Startup()
        {
            Rescan();
            string want = Options.Language;
            if (want == "")
            {
                try { want = CultureInfo.CurrentUICulture.TwoLetterISOLanguageName; }
                catch { want = "en"; }
            }
            if (!HasCode(want)) want = "en";
            if (!HasCode(want)) want = Available[0].Code;
            Apply(want, false);
        }

        public static void Set(string code)
        {
            Apply(code, true);
        }

        public static void Rescan()
        {
            var byCode = new Dictionary<string, LangInfo>(StringComparer.OrdinalIgnoreCase);
            var emb = new LangInfo();
            emb.Code = "en"; emb.Name = "English (built-in)"; emb.Embedded = true;
            byCode["en"] = emb;
            try
            {
                if (Directory.Exists(LocalesDir))
                {
                    string[] files = Directory.GetFiles(LocalesDir, "*.json");
                    Array.Sort(files, StringComparer.OrdinalIgnoreCase);
                    foreach (var f in files)
                    {
                        string code, name;
                        Dictionary<string, string> dummy;
                        if (!ParseFile(f, out code, out name, out dummy)) continue;
                        if (string.IsNullOrWhiteSpace(code)) continue;
                        var li = new LangInfo();
                        li.Code = code.Trim();
                        li.Name = string.IsNullOrWhiteSpace(name) ? li.Code : name.Trim();
                        li.Path = f;
                        byCode[li.Code] = li; // A file shadows the embedded entry on a code clash.
                    }
                }
            }
            catch { }
            Available.Clear();
            Available.AddRange(byCode.Values);
        }

        private static bool HasCode(string code)
        {
            foreach (var l in Available)
                if (l.Code.Equals(code, StringComparison.OrdinalIgnoreCase)) return true;
            return false;
        }

        private static void Apply(string code, bool save)
        {
            LangInfo info = null;
            foreach (var l in Available)
                if (l.Code.Equals(code, StringComparison.OrdinalIgnoreCase)) { info = l; break; }
            if (info == null) info = Available[0];
            var map = new Dictionary<string, string>(embeddedEn, StringComparer.Ordinal);
            if (!info.Embedded && !string.IsNullOrWhiteSpace(info.Path))
            {
                try
                {
                    string c2, n2;
                    Dictionary<string, string> m2;
                    if (ParseFile(info.Path, out c2, out n2, out m2) && m2 != null)
                        foreach (var kv in m2) map[kv.Key] = kv.Value;
                }
                catch { }
            }
            current = map;
            Code = info.Code;
            if (save)
            {
                Options.Language = Code;
                Options.Save();
            }
        }

        /// <summary>Parses a locale file: {"code":"xx","name":"...","strings":{...}}.</summary>
        public static bool ParseFile(string path, out string code, out string name, out Dictionary<string, string> map)
        {
            code = null; name = null;
            map = new Dictionary<string, string>(StringComparer.Ordinal);
            string text;
            try { text = File.ReadAllText(path, Encoding.UTF8); }
            catch { return false; }

            int si = text.IndexOf("\"strings\"", StringComparison.Ordinal);
            string header = si >= 0 ? text.Substring(0, si) : text;
            code = RegexMatch(header, "\"code\"\\s*:\\s*\"([^\"]+)\"");
            name = RegexMatch(header, "\"name\"\\s*:\\s*\"([^\"]+)\"");
            if (si < 0 || string.IsNullOrWhiteSpace(code)) return false;

            int brace = text.IndexOf('{', si);
            if (brace < 0) return false;
            string body = BraceBlock(text, brace);
            if (body == null) return false;

            var pair = new Regex("\"((?:[^\"\\\\]|\\\\.)*)\"\\s*:\\s*\"((?:[^\"\\\\]|\\\\.)*)\"");
            foreach (Match m in pair.Matches(body))
                map[Unescape(m.Groups[1].Value)] = Unescape(m.Groups[2].Value);
            return true;
        }

        private static string RegexMatch(string text, string pattern)
        {
            var m = Regex.Match(text, pattern);
            return m.Success ? m.Groups[1].Value : null;
        }

        private static string BraceBlock(string text, int open)
        {
            bool inStr = false;
            bool esc = false;
            int depth = 0;
            for (int i = open; i < text.Length; i++)
            {
                char c = text[i];
                if (inStr)
                {
                    if (esc) esc = false;
                    else if (c == '\\') esc = true;
                    else if (c == '"') inStr = false;
                }
                else
                {
                    if (c == '"') inStr = true;
                    else if (c == '{') depth++;
                    else if (c == '}')
                    {
                        depth--;
                        if (depth == 0) return text.Substring(open + 1, i - open - 1);
                    }
                }
            }
            return null;
        }

        private static string Unescape(string s)
        {
            var sb = new StringBuilder(s.Length);
            for (int i = 0; i < s.Length; i++)
            {
                if (s[i] == '\\' && i + 1 < s.Length)
                {
                    char n = s[i + 1];
                    if (n == 'n') { sb.Append('\n'); i++; }
                    else if (n == 'r') { sb.Append('\r'); i++; }
                    else if (n == 't') { sb.Append('\t'); i++; }
                    else if (n == '"') { sb.Append('"'); i++; }
                    else if (n == '\\') { sb.Append('\\'); i++; }
                    else if (n == '/' ) { sb.Append('/'); i++; }
                    else if (n == 'u' && i + 5 < s.Length)
                    {
                        try
                        {
                            sb.Append((char)Convert.ToInt32(s.Substring(i + 2, 4), 16));
                            i += 5;
                        }
                        catch { sb.Append(s[i]); }
                    }
                    else sb.Append(s[i]);
                }
                else sb.Append(s[i]);
            }
            return sb.ToString();
        }

        // Embedded English fallback for the no-files case.
        private static readonly Dictionary<string, string> embeddedEn =
            new Dictionary<string, string>(StringComparer.Ordinal);

        static L()
        {
            embeddedEn["app_title"] = "winstall \u2014 install and update programs";
            embeddedEn["view_installed"] = "INSTALLED";
            embeddedEn["view_updates"] = "UPDATES AVAILABLE";
            embeddedEn["view_search"] = "SEARCH";
            embeddedEn["btn_refresh"] = "Refresh";
            embeddedEn["btn_update_all"] = "Update all";
            embeddedEn["btn_update_all_n"] = "Update all ({0})";
            embeddedEn["btn_go"] = "\u25B6 Run";
            embeddedEn["btn_go_n"] = "\u25B6 Run ({0})";
            embeddedEn["menu_export"] = "Export list\u2026";
            embeddedEn["menu_import"] = "Import list\u2026";
            embeddedEn["menu_sel_upd"] = "Select all updatable";
            embeddedEn["menu_uncheck"] = "Uncheck all";
            embeddedEn["menu_logs"] = "Open winget logs folder";
            embeddedEn["menu_logsdir"] = "Winget log folder…";
            embeddedEn["menu_logs_default"] = "Default winget logs";
            embeddedEn["dlg_logsdir"] = "Select a folder for winget installer logs:";
            embeddedEn["status_logsdir"] = "Log folder: {0}";
            embeddedEn["menu_about"] = "About";
            embeddedEn["menu_exit"] = "Exit";
            embeddedEn["menu_lang"] = "Language";
            embeddedEn["col_program"] = "Program";
            embeddedEn["col_version"] = "Version";
            embeddedEn["search_cue"] = "Search the whole winget database\u2026 (empty = installed)";
            embeddedEn["btn_show_upd"] = "Updatable";
            embeddedEn["btn_none"] = "None";
            embeddedEn["chk_all"] = "Select all";
            embeddedEn["count_fmt"] = "{0}/{1}";
            embeddedEn["desc_legend"] = "\u2191 update   \u00D7 uninstall   + install. Select a program \u2014 details will appear here.";
            embeddedEn["desc_id"] = "ID: ";
            embeddedEn["desc_upd_via"] = "  (updates via {0})";
            embeddedEn["desc_installed"] = "Installed: ";
            embeddedEn["desc_avail"] = "   Available: ";
            embeddedEn["desc_source"] = "   Source: ";
            embeddedEn["desc_na"] = "\u2014";
            embeddedEn["desc_arp"] = "  (installed outside winget)";
            embeddedEn["desc_loading"] = "Description: loading\u2026";
            embeddedEn["desc_no"] = "Description unavailable.";
            embeddedEn["desc_notfound"] = "Description unavailable: not found in the winget database.";
            embeddedEn["desc_pub"] = "Publisher: ";
            embeddedEn["status_ready"] = "Ready.";
            embeddedEn["status_reading"] = "Reading installed packages\u2026";
            embeddedEn["status_full"] = "Installed: {0}, to update: {1}.";
            embeddedEn["status_short_n"] = "Installed: {0}.";
            embeddedEn["status_searching"] = "Searching the winget database for \u201C{0}\u201D\u2026";
            embeddedEn["status_found"] = "Search \u201C{0}\u201D: found {1} ({2} installed).";
            embeddedEn["status_search_err"] = "Search error: {0}";
            embeddedEn["status_working"] = "Working\u2026";
            embeddedEn["status_done"] = "Done.";
            embeddedEn["status_exported"] = "Exported to {0}";
            embeddedEn["status_type_query"] = "Type a query in the search box below \u2014 it searches the whole winget database.";
            embeddedEn["status_removing"] = "Removing: {0}\u2026";
            embeddedEn["status_installing"] = "Installing fresh: {0}\u2026";
            embeddedEn["msg_no_backend_t"] = "WinGet not found";
            embeddedEn["msg_no_backend"] = "No system-wide WinGet was found.\n\nwinstall is a frontend and needs the Windows Package Manager to work.\n\nPress Yes to open the official download page (App Installer), install it, then restart winstall.";
            embeddedEn["status_no_backend"] = "WinGet not found. Install App Installer, then restart winstall.";
            embeddedEn["check_missing"] = "WinGet is not installed, updates cannot be checked.";
            embeddedEn["msg_err_t"] = "Error";
            embeddedEn["msg_winget_err"] = "winget error:\n{0}";
            embeddedEn["msg_err"] = "Error:\n{0}";
            embeddedEn["msg_nothing_t"] = "winstall";
            embeddedEn["msg_nothing"] = "Nothing checked.\n\nCheck boxes:\n\u2022 updatable \u2014 update (\u2191)\n\u2022 fresh \u2014 uninstall (\u00D7)\n\u2022 new from search \u2014 install (+).";
            embeddedEn["msg_del_t"] = "Confirm removal";
            embeddedEn["msg_del"] = "You checked {0} package(s) for REMOVAL.\nContinue?";
            embeddedEn["log_row"] = "[{0}] {1} ({2}): exit={3}";
            embeddedEn["log_cmd"] = "Command: winget {0}";
            embeddedEn["log_done"] = "Done: {0} succeeded, {1} failed.\n\n{2}";
            embeddedEn["log_skip"] = "[SKIP] {0}: reinstall declined.";
            embeddedEn["log_uninst_fail"] = "[FAIL] {0}: could not uninstall (exit={1}).";
            embeddedEn["log_reinstall"] = "[{0}] {1}: reinstall (uninstall + install), exit={2}.";
            embeddedEn["msg_reinstall_t"] = "Reinstall required";
            embeddedEn["msg_reinstall"] = "{0}: winget reports that the installer technology\ndiffers from the installed version, so a direct\nupdate is impossible.\n\nRemove the package and install the new version?";
            embeddedEn["msg_no_updates"] = "No updates \u2014 everything is fresh.";
            embeddedEn["msg_upd_all_t"] = "Update all";
            embeddedEn["msg_upd_all_q"] = "Update all {0} package(s)?\n\nSame as `winget upgrade --all` (flags come from upd_flags).";
            embeddedEn["msg_upd_all_done"] = "Done.";
            embeddedEn["msg_upd_all_code"] = "Finished with code {0}.";
            embeddedEn["dlg_filter"] = "Winget JSON (*.json)|*.json|All files (*.*)|*.*";
            embeddedEn["dlg_export_name"] = "packages.json";
            embeddedEn["status_exporting"] = "Exporting list\u2026";
            embeddedEn["msg_export_t"] = "Export";
            embeddedEn["msg_export_fail"] = "export exit={0}\n\n{1}";
            embeddedEn["msg_import_q"] = "Install all packages from the file?\n{0}\n\nUnavailable ones will be skipped (--ignore-unavailable).";
            embeddedEn["msg_import_t"] = "Import";
            embeddedEn["status_importing"] = "Importing list\u2026";
            embeddedEn["msg_logs_err"] = "Could not open logs:\n{0}";
            embeddedEn["msg_logs_t"] = "Logs";
            embeddedEn["msg_about_t"] = "About";
            embeddedEn["msg_about"] = "winstall \u2014 a simple MInstall-style winget manager.\n.NET Framework 4.8 WinForms, for Windows 10 IoT Enterprise LTSC 21H2.\n\n\u2191 update, \u00D7 uninstall, + install.";
            embeddedEn["act_install"] = "install";            embeddedEn["act_upgrade"] = "upgrade";
            embeddedEn["act_uninstall"] = "uninstall";
            embeddedEn["act_none"] = "";
            embeddedEn["menu_autocheck"] = "Auto-check for updates";
            embeddedEn["msg_auto_on_t"] = "Auto-check";
            embeddedEn["msg_auto_on"] = "Enabled, no admin rights needed:\n\u2022 autostart at logon (HKCU Run key),\n\u2022 check on wake (the \\winstall\\winstall-wake task).\n\nwinstall will quietly check for updates and show a notification with the count.";
            embeddedEn["msg_auto_off_t"] = "Auto-check";
            embeddedEn["msg_auto_off"] = "Disabled: autostart and the wake task were removed.";
            embeddedEn["msg_auto_err_t"] = "Failed";
            embeddedEn["msg_auto_err"] = "Operation failed:\n{0}";
            embeddedEn["check_title"] = "winstall";
            embeddedEn["check_balloon"] = "Updates available: {0}.";
        }
    }
}
