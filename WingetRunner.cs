using System;
using System.Collections.Generic;
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

    /// <summary>Строка из `winget upgrade`: что установлено и до чего обновить.</summary>
    public class UpgradeEntry
    {
        public string Name = "";
        public string Id = "";
        public string Installed = "";
        public string Available = "";
    }

    public static class WingetRunner
    {
        private static readonly Regex Split2Plus = new Regex(@"\s{2,}", RegexOptions.Compiled);

        public static string FindWinget()
        {
            // 1) PATH
            try
            {
                var r = new Process();
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
                    if (p.EndsWith("winget.exe", StringComparison.OrdinalIgnoreCase) && File.Exists(p))
                        return p;
                }
            }
            catch { }

            // 2) Стандартный путь App Installer
            string local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            string cand = Path.Combine(local, @"Microsoft\WindowsApps\winget.exe");
            if (File.Exists(cand)) return cand;
            return null;
        }

        public static Task<WingetResult> RunAsync(string args, int timeoutMs = 120000)
        {
            return Task.Run(() =>
            {
                var res = new WingetResult();
                string exe = FindWinget() ?? "winget";
                var psi = new ProcessStartInfo(exe, args);
                psi.UseShellExecute = false;
                psi.RedirectStandardOutput = true;
                psi.RedirectStandardError = true;
                psi.CreateNoWindow = true;
                psi.StandardOutputEncoding = Encoding.UTF8;
                psi.StandardErrorEncoding = Encoding.UTF8;
                // Отключаем интерактив: важно для тихого режима на LTSC.
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
            });
        }

        // ---------- Парсинг таблиц winget (колонки разделены 2+ пробелами) ----------

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
                if (!dataStarted) continue; // пропускаем заголовок
                if (line.StartsWith("<") || line.StartsWith("Найдено") || line.StartsWith("Found") ||
                    line.StartsWith("Доступно") || line.StartsWith("No ")) continue;
                var parts = SplitRow(line);
                if (parts.Length >= 3) rows.Add(parts);
            }
            return rows;
        }

        /// <summary>
        /// winget list -> установленные.
        /// Старый формат: Name | Id | Version | Source.
        /// Новый формат (как на твоей машине): Name | Id | Version | Available | Source,
        /// где Available может быть пустым. Именно поэтому колонка «Источник/ID»
        /// показывала версию: 4-й токен принимали за Source.
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
                    // 4 токена: либо [Name,Id,Ver,Source] (старый формат),
                    // либо [Name,Id,Ver,Available] с пустым Source (новый).
                    // Source версией не бывает — различаем по виду токена.
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

        /// <summary>Похоже ли на версию (4.91.0, 26.7.1376.0), а не на имя источника.</summary>
        public static bool LooksLikeVersion(string s)
        {
            if (string.IsNullOrWhiteSpace(s)) return false;
            s = s.Trim();
            if (s == "—" || s.Equals("Unknown", StringComparison.OrdinalIgnoreCase)) return false;
            return VersionLike.IsMatch(s);
        }

        /// <summary>winget upgrade -> доступные обновления. Колонки: Name | Id | Version | Available | Source.</summary>
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

        /// <summary>winget search -> кандидаты. Колонки: Name | Id | Version | [Match] | Source.</summary>
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
            // Экранируем кавычки
            string q = query.Replace("\"", "");
            var r = await RunAsync("search \"" + q + "\" -n " + count + " --accept-source-agreements --disable-interactivity", 60000).ConfigureAwait(false);
            return ParseSearch(r.StdOut);
        }

        /// <summary>Полная карточка пакета (`winget show`). Null, если пакет не найден.</summary>
        public static async Task<string> ShowAsync(string id)
        {
            string q = id.Replace("\"", "");
            var r = await RunAsync("show --id \"" + q + "\" --accept-source-agreements --disable-interactivity", 60000).ConfigureAwait(false);
            if (r.ExitCode != 0) return null;
            return r.StdOut;
        }

        /// <summary>Вытаскивает из `winget show` издателя и блок описания.</summary>
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
                        // Редкий случай: текст на той же строке.
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
                break; // кончился блок с отступом — дальше другие секции
            }
            description = sb.ToString().Trim();
        }

        public static string BuildArgs(string verb, PackageInfo pkg)
        {
            // Для ARP-записей обновляемся по связанному winget-Id (см. UpgradeId),
            // иначе winget не поймёт, что именно обновлять.
            string useId = verb == "upgrade" ? pkg.EffectiveUpgradeId : pkg.Id;
            string idQ = "\"" + useId.Replace("\"", "") + "\"";
            string common = "--id " + idQ + " --accept-source-agreements --disable-interactivity ";
            switch (verb)
            {
                // ВАЖНО: у uninstall НЕТ --accept-package-agreements (нечего принимать).
                // Если его передать — winget ответит справкой и кодом 0x8A150002,
                // как и было с LLVM. У install/upgrade флаг есть.
                case "install": return "install " + common + "--accept-package-agreements --silent";
                case "upgrade": return "upgrade " + common + "--accept-package-agreements --silent";
                case "uninstall": return "uninstall " + common + "--silent";
                default: return verb + " " + common;
            }
        }
    }
}
