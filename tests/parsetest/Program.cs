using System;
using winstall;

class P {
    static void Check(string name, bool cond) {
        Console.WriteLine((cond ? "PASS " : "FAIL ") + name);
        if (!cond) Environment.ExitCode = 1;
    }
    static bool CheckNoEmpty(System.Collections.Generic.Dictionary<string, string> m) {
        foreach (var kv in m) if (kv.Key != "act_none" && kv.Value.Length == 0) return false;
        return true;
    }
    static string FindLocales() {
        try {
            string d = System.IO.Path.GetDirectoryName(System.Reflection.Assembly.GetExecutingAssembly().Location);
            while (d != null) {
                string cand = System.IO.Path.Combine(d, "locales");
                if (System.IO.Directory.Exists(cand)
                    && System.IO.File.Exists(System.IO.Path.Combine(cand, "en.json"))
                    && System.IO.File.Exists(System.IO.Path.Combine(cand, "ru.json")))
                    return cand;
                d = System.IO.Path.GetDirectoryName(d);
            }
        } catch { }
        return null;
    }
    static int Main() {
        // New 5-column layout.
        string five =
            "Имя                             ИД                                Версия     Доступно   Источник\r\n" +
            "-----------------------------------------------------------------------------------------------\r\n" +
            "Docker Desktop                 Docker.DockerDesktop              4.88.1     4.91.0     winget\r\n" +
            "LLVM                           LLVM.LLVM                         22.1.8     23.1.2     winget\r\n" +
            "Abiotic Factor                 ARP\\Machine\\X64\\Steam App 427410  Unknown\r\n";
        var l5 = WingetRunner.ParseList(five);
        Check("5col count=3", l5.Count == 3);
        Check("5col docker src=winget", l5[0].Source == "winget");
        Check("5col docker avail=4.91.0", l5[0].AvailableVersion == "4.91.0");
        Check("5col llvm src=winget", l5[1].Source == "winget");
        Check("5col llvm avail=23.1.2", l5[1].AvailableVersion == "23.1.2");
        Check("5col arp src empty", l5[2].Source == "");
        Check("5col arp IsArp", l5[2].IsArp);

        // Legacy 4-column layout.
        string four =
            "Name  ID  Version  Source\r\n" +
            "--------------------------------\r\n" +
            "Git                                 Git.Git                                       2.55.0.3         winget\r\n" +
            "Denuvo Anti-Cheat                   ARP\\Machine\\X64\\Denuvo Anti-Cheat             6.9.2.7013\r\n";
        var l4 = WingetRunner.ParseList(four);
        Check("4col count=2", l4.Count == 2);
        Check("4col git src=winget", l4[0].Source == "winget");
        Check("4col git avail empty", l4[0].AvailableVersion == "");
        Check("4col arp src empty", l4[1].Source == "");

        // 4 tokens where the 4th is a version (new layout, empty source).
        string fourV =
            "Name  ID  Version  Available\r\n" +
            "--------------------------------\r\n" +
            "LLVM                           LLVM.LLVM                         22.1.8     23.1.2\r\n";
        var l4v = WingetRunner.ParseList(fourV);
        Check("4col-ver src empty", l4v[0].Source == "");
        Check("4col-ver avail=23.1.2", l4v[0].AvailableVersion == "23.1.2");

        // EffectiveUpgradeId
        var p = new PackageInfo();
        p.Id = "ARP\\Machine\\X64\\LLVM"; p.UpgradeId = "LLVM.LLVM";
        Check("EffectiveUpgradeId", p.EffectiveUpgradeId == "LLVM.LLVM");
        var p2 = new PackageInfo();
        p2.Id = "Git.Git";
        Check("EffectiveUpgradeId fallback", p2.EffectiveUpgradeId == "Git.Git");

        // winget show: publisher + description block.
        string show =
            "Найдено Steam [Valve.Steam]\r\n" +
            "Версия: 2.10.91.91\r\n" +
            "Издатель: Valve Corporation\r\n" +
            "Моникер: steam\r\n" +
            "Описание:\r\n" +
            "  Steam is the ultimate destination for playing games.\r\n" +
            "  Steam automates game updates.\r\n" +
            "Домашняя страница: https://store.steampowered.com/about/\r\n" +
            "Лицензия: Proprietary\r\n";
        string pub, desc;
        WingetRunner.ParseShow(show, out pub, out desc);
        Check("show publisher", pub == "Valve Corporation");
        Check("show desc 2 lines", desc.Contains("ultimate destination") && desc.Contains("automates game updates"));
        Check("show desc stops at next section", !desc.Contains("store.steampowered") && !desc.Contains("Proprietary"));

        // Locales: en/ru key parity, \n unescaping, fallback.
        // The repo root is found by walking up from the test binary (locales/ lives there).
        string locDir = FindLocales();
        Check("locales dir found", locDir != null);
        if (locDir == null) return 1;
        string ec, en2;
        System.Collections.Generic.Dictionary<string, string> enMap;
        string rc, rn;
        System.Collections.Generic.Dictionary<string, string> ruMap;
        Check("parse en.json", L.ParseFile(locDir + "\\en.json", out ec, out en2, out enMap) && ec == "en");
        Check("parse ru.json", L.ParseFile(locDir + "\\ru.json", out rc, out rn, out ruMap) && rc == "ru" && rn == "Русский");
        bool sameKeys = enMap.Count == ruMap.Count;
        if (sameKeys) foreach (var k in enMap.Keys) if (!ruMap.ContainsKey(k)) { sameKeys = false; break; }
        Check("en/ru key parity (" + enMap.Count + " keys)", sameKeys);
        Check("unescape \\n", enMap["msg_no_backend"].Contains("\n") && !enMap["msg_no_backend"].Contains("\\n"));
        Check("no empty ru values", CheckNoEmpty(ruMap));
        Check("fallback unknown key", L.T("no_such_key_xyz") == "no_such_key_xyz");
        Check("embedded fallback act", new PackageInfo().ActionFor(true) == PendingAction.Install && PackageInfo.ActionText(PendingAction.Upgrade) == "upgrade");

        // Per-verb flag sets, centralized in options (no implicit flags).
        // Isolated config so a real options.ini can never leak into expectations.
        string fdir = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "winstall-flags-" + System.Guid.NewGuid().ToString("N"));
        Options.DirOverride = fdir;
        try
        {
            Options.Reset();
            var bp = new PackageInfo();
            bp.Id = "LLVM.LLVM";
            Check("upgrade defaults",
                WingetRunner.BuildArgs("upgrade", bp) == "upgrade --id \"LLVM.LLVM\" " + Options.DefaultUpdFlags);
            Check("uninstall defaults without package-agreements",
                WingetRunner.BuildArgs("uninstall", bp) == "uninstall --id \"LLVM.LLVM\" " + Options.DefaultRemFlags
                && !WingetRunner.BuildArgs("uninstall", bp).Contains("accept-package"));
            var bp2 = new PackageInfo();
            bp2.Id = "ARP\\Machine\\X64\\LLVM"; bp2.UpgradeId = "LLVM.LLVM";
            Check("upgrade uses UpgradeId", WingetRunner.BuildArgs("upgrade", bp2).Contains("LLVM.LLVM") && !WingetRunner.BuildArgs("upgrade", bp2).Contains("ARP"));
            Options.UpdFlags = "--include-unknown";
            Check("custom upd wins",
                WingetRunner.BuildArgs("upgrade", bp) == "upgrade --id \"LLVM.LLVM\" --include-unknown");
            Check("add untouched by upd",
                WingetRunner.BuildArgs("install", bp).Contains("--silent"));
            Options.RemFlags = "";
            Check("empty rem set",
                WingetRunner.BuildArgs("uninstall", bp) == "uninstall --id \"LLVM.LLVM\"");
            Check("upgrade-all uses upd",
                WingetRunner.BuildUpgradeAllArgs() == "upgrade --all --include-unknown");
            // --log file wiring (winget takes a file path, not a directory).
            var bp3 = new PackageInfo();
            bp3.Id = "Valve.Steam";
            Options.LogsDir = fdir;
            string withLog = WingetRunner.BuildArgs("install", bp3);
            Check("log flag wired", withLog.Contains("--log \"" + fdir + "\\winstall-install-Valve.Steam-"));
            Options.LogsDir = "";
            Check("no log without dir", !WingetRunner.BuildArgs("install", bp3).Contains("--log"));
        }
        finally
        {
            Options.DirOverride = null;
            Options.Reset();
            try { System.IO.Directory.Delete(fdir, true); } catch { }
        }

        // WINSTALL_WINGET override replaces detection entirely ("none" forces "no backend").
        System.Environment.SetEnvironmentVariable("WINSTALL_WINGET", @"Z:\custom\winget.exe");
        var forced = new System.Collections.Generic.List<string>(WingetRunner.CandidatePaths());
        Check("override yields only itself", forced.Count == 1 && forced[0] == @"Z:\custom\winget.exe");
        System.Environment.SetEnvironmentVariable("WINSTALL_WINGET", "none");
        Check("none override yields nothing", new System.Collections.Generic.List<string>(WingetRunner.CandidatePaths()).Count == 0);
        System.Environment.SetEnvironmentVariable("WINSTALL_WINGET", null);
        Check("no candidates -> null",
            WingetRunner.FirstExisting(new string[] { @"Z:\definitely\not\here\winget.exe", "", null }) == null);
        bool hasAlias = false;
        foreach (var c in WingetRunner.CandidatePaths())
            if (c.EndsWith(@"Microsoft\WindowsApps\winget.exe", StringComparison.OrdinalIgnoreCase)) hasAlias = true;
        Check("candidates include alias", hasAlias);

        // options.ini round-trip in an isolated dir, plus legacy language.txt migration.
        string bdir = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "winstall-test-" + System.Guid.NewGuid().ToString("N"));
        Options.DirOverride = bdir;
        try
        {
            Options.Reset();
            Check("options defaults", Options.Language == "" && Options.WingetPath == ""
                && Options.LogsDir == "" && Options.UpdFlags == Options.DefaultUpdFlags
                && Options.AddFlags == Options.DefaultAddFlags && Options.RemFlags == Options.DefaultRemFlags);
            string self = System.Reflection.Assembly.GetExecutingAssembly().Location;
            Options.Language = "ru";
            Options.WingetPath = self;
            Options.LogsDir = bdir;
            Options.Theme = "dark";
            Options.UpdFlags = "--include-unknown";
            Options.AddFlags = "";
            Options.RemFlags = "--silent";
            Options.Save();
            Options.Reset();
            Check("options roundtrip", Options.Language == "ru" && Options.WingetPath == self
                && Options.LogsDir == bdir && Options.Theme == "dark" && Options.UpdFlags == "--include-unknown"
                && Options.AddFlags == "" && Options.RemFlags == "--silent");
            Options.Theme = "banana";
            Check("theme falls back to system", Options.Theme == "system");
            // Stale winget path is kept verbatim (existence is checked by Backend, not here).
            Options.WingetPath = @"Z:\definitely\not\here\winget.exe";
            Check("options keeps stale path", Options.WingetPath.EndsWith("winget.exe"));
            // Legacy migration: no options.ini, old language.txt present.
            System.IO.File.Delete(System.IO.Path.Combine(bdir, "options.ini"));
            System.IO.File.WriteAllText(System.IO.Path.Combine(bdir, "language.txt"), "ru");
            Options.Reset();
            Check("legacy language migrated", Options.Language == "ru");
            Check("legacy file consumed", !System.IO.File.Exists(System.IO.Path.Combine(bdir, "language.txt")));
        }
        finally
        {
            Options.DirOverride = null;
            Options.Reset();
            try { System.IO.Directory.Delete(bdir, true); } catch { }
        }

        // Live backend resolve: sane result plus cache file (tolerates machines without winget).
        string bdir2 = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "winstall-test2-" + System.Guid.NewGuid().ToString("N"));
        Options.DirOverride = bdir2;
        try
        {
            Options.Reset();
            string live = Backend.ResolveExe();
            Check("live resolve sane", live == null || live.EndsWith("winget.exe", StringComparison.OrdinalIgnoreCase));
            if (live != null)
                Check("resolve cached to file", System.IO.File.Exists(System.IO.Path.Combine(bdir2, "options.ini")));
            Backend.Invalidate();
            Check("invalidate clears", Options.WingetPath == "");
        }
        finally
        {
            Options.DirOverride = null;
            Options.Reset();
            try { System.IO.Directory.Delete(bdir2, true); } catch { }
        }
        // Scheduler task: full cycle (create, verify, delete).
        try { AutoCheck.Disable(); } catch { }
        Check("task initially off", !AutoCheck.IsEnabled());
        string enErr = AutoCheck.Enable();
        Check("task enable" + (enErr == null ? "" : ": " + enErr), enErr == null);
        Check("task visible", AutoCheck.IsEnabled());
        string disErr = AutoCheck.Disable();
        Check("task disable" + (disErr == null ? "" : ": " + disErr), disErr == null);
        Check("task gone", !AutoCheck.IsEnabled());

        // Theming: real shown form on an STA thread, header pixels per mode.
        // Skipped where no winget backend exists (the form would sit on a dialog).
        if (Backend.ResolveExe() == null)
        {
            Console.WriteLine("SKIP theme UI test (no winget backend)");
        }
        else
        {
            Exception themeErr = null;
            var t = new System.Threading.Thread(delegate ()
            {
                try { RunThemeUiTest(); }
                catch (Exception ex) { themeErr = ex; }
            });
            t.SetApartmentState(System.Threading.ApartmentState.STA);
            t.Start();
            if (!t.Join(180000)) themeErr = new TimeoutException("theme UI test hung");
            Check("theme UI cycle", themeErr == null);
            if (themeErr != null) Console.WriteLine("theme error: " + themeErr);
        }

        Console.WriteLine("done");
        return 0;
    }

    static void RunThemeUiTest()
    {
        string tdir = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "winstall-theme-" + System.Guid.NewGuid().ToString("N"));
        Options.DirOverride = tdir;
        try
        {
            Options.Reset();
            using (var f = new MainForm())
            {
                f.Show();
                DateTime until = DateTime.Now.AddSeconds(90);
                while (f.RowCount == 0 && DateTime.Now < until)
                {
                    System.Windows.Forms.Application.DoEvents();
                    System.Threading.Thread.Sleep(500);
                }
                if (f.RowCount == 0)
                    throw new Exception("list never loaded");
                string[] modes = new string[] { "light", "dark", "system" };
                foreach (var m in modes)
                {
                    Options.Theme = m;
                    f.ApplyTheme();
                    System.Windows.Forms.Application.DoEvents();
                    System.Threading.Thread.Sleep(800);
                    System.Drawing.Color h = f.SampleHeaderColor();
                    if (h.IsEmpty) throw new Exception("no header sample in " + m);
                    bool expectDark = m == "dark" || (m == "system" && Theme.SystemUsesDark());
                    if (expectDark && h.R > 90)
                        throw new Exception("header not dark in " + m + " (R=" + h.R + ")");
                    if (!expectDark && h.R < 180)
                        throw new Exception("header not light in " + m + " (R=" + h.R + ")");
                    double mean = MeanBrightness(f.CaptureForm());
                    if (expectDark && mean > 110)
                        throw new Exception("form not dark in " + m + " (mean=" + (int)mean + ")");
                    if (!expectDark && mean < 140)
                        throw new Exception("form not light in " + m + " (mean=" + (int)mean + ")");
                }
                f.Close();
            }
        }
        finally
        {
            Options.DirOverride = null;
            Options.Reset();
            try { System.IO.Directory.Delete(tdir, true); } catch { }
        }
    }

    static double MeanBrightness(System.Drawing.Bitmap bmp)
    {
        long sum = 0;
        long n = 0;
        using (bmp)
        {
            for (int y = 0; y < bmp.Height; y += 11)
                for (int x = 0; x < bmp.Width; x += 11)
                {
                    System.Drawing.Color px = bmp.GetPixel(x, y);
                    sum += (px.R + px.G + px.B) / 3;
                    n++;
                }
        }
        return n == 0 ? 0 : (double)sum / n;
    }
}
