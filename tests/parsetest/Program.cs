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

        // Per-verb flags: uninstall has no --accept-package-agreements (winget prints usage, 0x8A150002).
        var bp = new PackageInfo();
        bp.Id = "LLVM.LLVM";
        Check("uninstall has no package-agreements", !WingetRunner.BuildArgs("uninstall", bp).Contains("accept-package"));
        Check("uninstall has silent", WingetRunner.BuildArgs("uninstall", bp).Contains("--silent"));
        Check("install has package-agreements", WingetRunner.BuildArgs("install", bp).Contains("accept-package-agreements"));
        Check("upgrade has package-agreements", WingetRunner.BuildArgs("upgrade", bp).Contains("accept-package-agreements"));
        var bp2 = new PackageInfo();
        bp2.Id = "ARP\\Machine\\X64\\LLVM"; bp2.UpgradeId = "LLVM.LLVM";
        Check("upgrade uses UpgradeId", WingetRunner.BuildArgs("upgrade", bp2).Contains("LLVM.LLVM") && !WingetRunner.BuildArgs("upgrade", bp2).Contains("ARP"));

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

        // Backend cache round-trip in an isolated dir (stale entries are ignored).
        string bdir = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "winstall-test-" + System.Guid.NewGuid().ToString("N"));
        Backend.CacheDirOverride = bdir;
        try
        {
            Backend.WriteCache(@"Z:\definitely\not\here\winget.exe");
            Check("stale cache ignored", Backend.ReadCache() == null);
            string self = System.Reflection.Assembly.GetExecutingAssembly().Location;
            Backend.WriteCache(self);
            Check("cache roundtrip", Backend.ReadCache() == self);
        }
        finally
        {
            Backend.CacheDirOverride = null;
            try { System.IO.Directory.Delete(bdir, true); } catch { }
        }
        try { AutoCheck.Disable(); } catch { }
        Check("task initially off", !AutoCheck.IsEnabled());
        string enErr = AutoCheck.Enable();
        Check("task enable" + (enErr == null ? "" : ": " + enErr), enErr == null);
        Check("task visible", AutoCheck.IsEnabled());
        string disErr = AutoCheck.Disable();
        Check("task disable" + (disErr == null ? "" : ": " + disErr), disErr == null);
        Check("task gone", !AutoCheck.IsEnabled());

        Console.WriteLine("done");
        return 0;
    }
}
