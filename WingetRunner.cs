using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace winstall
{
    public class WingetResult
    {
        public int ExitCode;
        public string StdOut = "";
        public string StdErr = "";
    }

    /// <summary>One row of winget upgrade output: installed vs. available version.</summary>
    public class UpgradeEntry
    {
        public string Name = "";
        public string Id = "";
        public string Installed = "";
        public string Available = "";
    }

    public static class WingetRunner
    {
        public const int DefaultTimeoutMs = 120000;
        public const int SearchTimeoutMs = 60000;
        public const int OperationTimeoutMs = 600000;
        public const int BulkTimeoutMs = 7200000;
        public const int MaxOutputTail = 1500;
        public const int MaxBulkOutputTail = 2000;

        private static readonly Regex Split2Plus = new Regex(@"\s{2,}", RegexOptions.Compiled);

        internal static IEnumerable<string> CandidatePaths()
        {
            // Explicit backend selection replaces detection entirely.
            // "none" forces the no-backend path (used to test it).
            string forced = Environment.GetEnvironmentVariable("WINSTALL_WINGET");
            if (forced != null)
            {
                if (!string.IsNullOrWhiteSpace(forced) &&
                    !forced.Trim().Equals("none", StringComparison.OrdinalIgnoreCase))
                    yield return forced.Trim();
                yield break;
            }
            var paths = new List<string>();
            try
            {
                using (var r = new Process())
                {
                    r.StartInfo.FileName = "where.exe";
                    r.StartInfo.Arguments = "winget";
                    r.StartInfo.UseShellExecute = false;
                    r.StartInfo.RedirectStandardOutput = true;
                    r.StartInfo.CreateNoWindow = true;
                    r.Start();
                    string o = r.StandardOutput.ReadToEnd();
                    r.WaitForExit(5000);
                    foreach (var line in o.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries))
                    {
                        string p = line.Trim();
                        if (p.EndsWith("winget.exe", StringComparison.OrdinalIgnoreCase))
                            paths.Add(p);
                    }
                }
            }
            catch { }
            foreach (var p in paths) yield return p;
            string local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            yield return Path.Combine(local, @"Microsoft\WindowsApps\winget.exe");
        }

        internal static string FirstExisting(IEnumerable<string> candidates)
        {
            foreach (var p in candidates)
            {
                try
                {
                    if (!string.IsNullOrWhiteSpace(p) && File.Exists(p)) return p;
                }
                catch { }
            }
            return null;
        }

        public static string FindWinget()
        {
            try { return FirstExisting(CandidatePaths()); }
            catch { return null; }
        }

        public static Task<WingetResult> RunAsync(string args, int timeoutMs = DefaultTimeoutMs)
        {
            return Task.Run(() =>
            {
                string exe = Backend.ResolveExe() ?? "winget";
                try
                {
                    return RunOnce(exe, args, timeoutMs);
                }
                catch (Win32Exception)
                {
                    // Cached backend binary is gone; re-detect once and retry.
                    Backend.Invalidate();
                    return RunOnce(Backend.ResolveExe() ?? "winget", args, timeoutMs);
                }
            });
        }

        private static WingetResult RunOnce(string exe, string args, int timeoutMs)
        {
            var res = new WingetResult();
            var psi = new ProcessStartInfo(exe, args);
            psi.UseShellExecute = false;
            psi.RedirectStandardOutput = true;
            psi.RedirectStandardError = true;
            psi.CreateNoWindow = true;
            psi.StandardOutputEncoding = Encoding.UTF8;
            psi.StandardErrorEncoding = Encoding.UTF8;
            // Unattended operation requires non-interactive winget.
            psi.EnvironmentVariables["WINGET_DISABLE_INTERACTIVITY"] = "1";

            var sbOut = new StringBuilder();
            var sbErr = new StringBuilder();
            using (var p = new Process())
            {
                p.StartInfo = psi;
                p.OutputDataReceived += (s, e) => { if (e.Data != null) sbOut.AppendLine(e.Data); };
                p.ErrorDataReceived += (s, e) => { if (e.Data != null) sbErr.AppendLine(e.Data); };
                p.Start();
                p.BeginOutputReadLine();
                p.BeginErrorReadLine();
                bool exited = p.WaitForExit(timeoutMs);
                if (!exited)
                {
                    try { p.Kill(); } catch { }
                    res.ExitCode = -1;
                }
                else
                {
                    res.ExitCode = p.ExitCode;
                }
            }
            res.StdOut = sbOut.ToString();
            res.StdErr = sbErr.ToString();
            return res;
        }

        // winget table parsing: columns are separated by 2+ spaces.

        private static string[] SplitRow(string line)
        {
            return Split2Plus.Split(line.Trim());
        }

        private static bool IsDashes(string line)
        {
            string t = line.Trim();
            if (t.Length < 3) return false;
            foreach (char c in t)
                if (c != '-' && c != ' ') return false;
            return t.Contains("-");
        }

        private static List<string[]> TableRows(string stdout)
        {
            var rows = new List<string[]>();
            if (string.IsNullOrWhiteSpace(stdout)) return rows;
            string[] lines = stdout.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
            bool dataStarted = false;
            foreach (var raw in lines)
            {
                string line = raw.TrimEnd();
                if (string.IsNullOrWhiteSpace(line)) continue;
                if (IsDashes(line)) { dataStarted = true; continue; }
                if (!dataStarted) continue; // Header row.
                if (line.StartsWith("<") || line.StartsWith("Найдено") || line.StartsWith("Found") ||
                    line.StartsWith("Доступно") || line.StartsWith("No ")) continue;
                var parts = SplitRow(line);
                if (parts.Length >= 3) rows.Add(parts);
            }
            return rows;
        }

        /// <summary>
        /// winget list output. Column layout depends on the winget version:
        /// legacy: Name | Id | Version | Source;
        /// current: Name | Id | Version | Available | Source (Available may be empty).
        /// A 4-token row is ambiguous and resolved by shape: sources never look like versions.
        /// </summary>
        public static List<PackageInfo> ParseList(string stdout)
        {
            var list = new List<PackageInfo>();
            foreach (var p in TableRows(stdout))
            {
                string name = p.Length > 0 ? p[0] : "";
                string id = p.Length > 1 ? p[1] : "";
                string ver = p.Length > 2 ? p[2] : "";
                string avail = "";
                string src = "";
                if (p.Length >= 5) { avail = p[3]; src = p[4]; }
                else if (p.Length == 4)
                {
                    // Either [Name, Id, Ver, Source] (legacy) or
                    // [Name, Id, Ver, Available] with an empty Source (current).
                    if (LooksLikeVersion(p[3])) avail = p[3];
                    else src = p[3];
                }
                if (string.IsNullOrWhiteSpace(id)) continue;
                if (avail.Trim() == "—" || avail.Equals("Unknown", StringComparison.OrdinalIgnoreCase))
                    avail = "";
                list.Add(new PackageInfo
                {
                    Name = name, Id = id, InstalledVersion = ver, AvailableVersion = avail, Source = src,
                    IsInstalled = true, HasUpdate = false
                });
            }
            return list;
        }

        private static readonly Regex VersionLike = new Regex(@"^\d[\d\.\-+_]*$", RegexOptions.Compiled);

        /// <summary>Version-shaped tokens (4.91.0) as opposed to source names.</summary>
        public static bool LooksLikeVersion(string s)
        {
            if (string.IsNullOrWhiteSpace(s)) return false;
            s = s.Trim();
            if (s == "—" || s.Equals("Unknown", StringComparison.OrdinalIgnoreCase)) return false;
            return VersionLike.IsMatch(s);
        }

        /// <summary>winget upgrade columns: Name | Id | Version | Available | Source.</summary>
        public static List<UpgradeEntry> ParseUpgrades(string stdout)
        {
            var list = new List<UpgradeEntry>();
            foreach (var p in TableRows(stdout))
            {
                if (p.Length < 4) continue;
                string name = p[0], id = p[1], ver = p[2], avail = p[3];
                if (!string.IsNullOrWhiteSpace(id) && !string.IsNullOrWhiteSpace(avail))
                    list.Add(new UpgradeEntry { Name = name, Id = id, Installed = ver, Available = avail });
            }
            return list;
        }

        /// <summary>winget search columns: Name | Id | Version | [Match] | Source.</summary>
        public static List<PackageInfo> ParseSearch(string stdout)
        {
            var list = new List<PackageInfo>();
            foreach (var p in TableRows(stdout))
            {
                string name, id, ver, src;
                if (p.Length >= 5) { name = p[0]; id = p[1]; ver = p[2]; src = p[4]; }
                else if (p.Length == 4) { name = p[0]; id = p[1]; ver = p[2]; src = p[3]; }
                else continue;
                if (string.IsNullOrWhiteSpace(id)) continue;
                list.Add(new PackageInfo
                {
                    Name = name, Id = id, AvailableVersion = ver, Source = src,
                    IsInstalled = false, HasUpdate = false
                });
            }
            return list;
        }

        public static async Task<List<PackageInfo>> GetInstalledAsync()
        {
            var r = await RunAsync("list --accept-source-agreements --disable-interactivity").ConfigureAwait(false);
            return ParseList(r.StdOut);
        }

        public static async Task<List<UpgradeEntry>> GetUpgradesAsync()
        {
            var r = await RunAsync("upgrade --accept-source-agreements --disable-interactivity").ConfigureAwait(false);
            return ParseUpgrades(r.StdOut);
        }

        public static async Task<List<PackageInfo>> SearchAsync(string query, int count = 30)
        {
            string q = query.Replace("\"", ""); // Keep the command line intact.
            var r = await RunAsync("search \"" + q + "\" -n " + count + " --accept-source-agreements --disable-interactivity", SearchTimeoutMs).ConfigureAwait(false);
            return ParseSearch(r.StdOut);
        }

        /// <summary>Full package card (winget show); null when the package is unknown.</summary>
        public static async Task<string> ShowAsync(string id)
        {
            string q = id.Replace("\"", "");
            var r = await RunAsync("show --id \"" + q + "\" --accept-source-agreements --disable-interactivity", SearchTimeoutMs).ConfigureAwait(false);
            if (r.ExitCode != 0) return null;
            return r.StdOut;
        }

        /// <summary>Extracts the publisher and the description block from winget show output.</summary>
        public static void ParseShow(string stdout, out string publisher, out string description)
        {
            publisher = "";
            description = "";
            if (string.IsNullOrWhiteSpace(stdout)) return;
            string[] lines = stdout.Split(new[] { '\r', '\n' }, StringSplitOptions.None);

            foreach (var raw in lines)
            {
                string t = raw.Trim();
                if (t.StartsWith("Издатель:") || t.StartsWith("Publisher:"))
                {
                    int c = t.IndexOf(':');
                    if (c >= 0) publisher = t.Substring(c + 1).Trim();
                    break;
                }
            }

            bool inDesc = false;
            var sb = new StringBuilder();
            foreach (var raw in lines)
            {
                if (!inDesc)
                {
                    string t = raw.Trim();
                    if (t.Equals("Описание:") || t.Equals("Description:"))
                        inDesc = true;
                    else if (t.StartsWith("Описание:") || t.StartsWith("Description:"))
                    {
                        // Same-line text is rare, but handle it.
                        int c = t.IndexOf(':');
                        if (c >= 0 && c + 1 < t.Length) sb.AppendLine(t.Substring(c + 1).Trim());
                        inDesc = true;
                    }
                    continue;
                }
                if (raw.Length > 0 && (raw[0] == ' ' || raw[0] == '\t'))
                {
                    string t = raw.Trim();
                    if (t.Length > 0) sb.AppendLine(t);
                    continue;
                }
                if (raw.Trim().Length == 0) continue;
                break; // Indented block is over, other sections follow.
            }
            description = sb.ToString().Trim();
        }

        /// <summary>
        /// Builds the winget command line from the centralized per-verb flag sets
        /// (Options.UpdFlags/AddFlags/RemFlags). Only --id is structural; everything
        /// else comes from options.ini. A configured logs_dir adds a per-operation
        /// --log file (winget takes a file path, not a directory).
        /// </summary>
        public static string BuildArgs(string verb, PackageInfo pkg)
        {
            string useId = verb == "upgrade" ? pkg.EffectiveUpgradeId : pkg.Id;
            string args = verb + " --id \"" + useId.Replace("\"", "") + "\"";
            string flags = "";
            if (verb == "install") flags = Options.AddFlags;
            else if (verb == "upgrade") flags = Options.UpdFlags;
            else if (verb == "uninstall") flags = Options.RemFlags;
            if (!string.IsNullOrWhiteSpace(flags)) args += " " + flags.Trim();
            args += BuildLogArg(verb, useId, Options.LogsDir);
            return args;
        }

        public static string BuildUpgradeAllArgs()
        {
            string args = "upgrade --all";
            if (!string.IsNullOrWhiteSpace(Options.UpdFlags)) args += " " + Options.UpdFlags.Trim();
            args += BuildLogArg("upgrade", "all", Options.LogsDir);
            return args;
        }

        internal static string BuildLogArg(string verb, string id, string logDir)
        {
            if (string.IsNullOrWhiteSpace(logDir)) return "";
            try
            {
                Directory.CreateDirectory(logDir);
                string safe = id.Replace('\\', '.').Replace('/', '.');
                foreach (var c in Path.GetInvalidFileNameChars()) safe = safe.Replace(c, '_');
                if (safe.Length > 60) safe = safe.Substring(0, 60);
                string file = string.Format(
                    "winstall-{0}-{1}-{2:yyyyMMdd-HHmmss}.log", verb, safe, DateTime.Now);
                return " --log \"" + Path.Combine(logDir, file).Replace("\"", "") + "\"";
            }
            catch { return ""; }
        }
    }
}
