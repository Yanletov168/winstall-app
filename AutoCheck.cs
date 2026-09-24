using System;
using System.Diagnostics;
using System.Drawing;
using System.Text;
using System.Threading;
using System.Windows.Forms;
using Microsoft.Win32;

namespace winstall
{
    /// <summary>
    /// Update checking without a service and without admin rights: logon is covered
    /// by the HKCU\...\Run autostart entry, wake by the \\winstall\\winstall-wake
    /// scheduler task (ONEVENT: Power-Troubleshooter ID 1). Both run
    /// winstall.exe --check-updates, which quietly counts updates and balloons
    /// the count only. Note: ONLOGON triggers and XML imports require elevation,
    /// while Run + ONEVENT works for a standard user.
    /// </summary>
    public static class AutoCheck
    {
        public const string TaskName = "\\winstall\\winstall-wake";
        public const string CheckArg = "--check-updates";
        private const string RunValue = "winstall";
        private const string RunKey = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Run";
        private const string WakeQuery =
            "*[System[Provider[@Name='Microsoft-Windows-Power-Troubleshooter'] and EventID=1]]";

        public static bool IsEnabled()
        {
            return HasRunValue() && TaskExists();
        }

        public static string Enable()
        {
            string err1 = SetRunValue();
            string err2 = CreateWakeTask();
            if (IsEnabled()) return null;
            // Roll back instead of leaving a half-enabled state.
            try { RemoveRunValue(); } catch { }
            try { DeleteWakeTask(); } catch { }
            return err2 ?? err1 ?? "unknown";
        }

        public static string Disable()
        {
            string err1 = null, err2 = null;
            try { RemoveRunValue(); }
            catch (Exception ex) { err1 = ex.Message; }
            err2 = DeleteWakeTask();
            if (!IsEnabled()) return null;
            return err2 ?? err1 ?? "unknown";
        }

        // Logon autostart.

        private static string RunCommand()
        {
            return "\"" + Application.ExecutablePath + "\" " + CheckArg;
        }

        private static bool HasRunValue()
        {
            try
            {
                using (var k = Registry.CurrentUser.OpenSubKey(RunKey, false))
                {
                    if (k == null) return false;
                    object v = k.GetValue(RunValue);
                    return v is string && ((string)v).Trim().Length > 0;
                }
            }
            catch { return false; }
        }

        private static string SetRunValue()
        {
            try
            {
                using (var k = Registry.CurrentUser.OpenSubKey(RunKey, true))
                {
                    if (k == null) return "no Run key";
                    k.SetValue(RunValue, RunCommand());
                }
                return null;
            }
            catch (Exception ex) { return ex.Message; }
        }

        private static void RemoveRunValue()
        {
            using (var k = Registry.CurrentUser.OpenSubKey(RunKey, true))
            {
                if (k != null) k.DeleteValue(RunValue, false);
            }
        }

        // Wake task.

        private static bool TaskExists()
        {
            return RunSchtasks("/Query /TN \"" + TaskName + "\"") == 0;
        }

        private static string CreateWakeTask()
        {
            // /TR must arrive as a single argv element: outer quotes for CreateProcess,
            // inner ones escaped as \" — otherwise paths with spaces break apart
            // and schtasks reports an invalid parameter.
            string trValue = "\\\"" + Application.ExecutablePath + "\\\" " + CheckArg;
            string args = "/Create /TN \"" + TaskName + "\""
                + " /TR \"" + trValue + "\""
                + " /SC ONEVENT /EC System /MO \"" + WakeQuery + "\" /F";
            StringBuilder err = new StringBuilder();
            int code = RunSchtasks(args, err);
            if (code != 0)
                return err.Length > 0 ? err.ToString().Trim() : "schtasks exit=" + code;
            return null;
        }

        private static string DeleteWakeTask()
        {
            StringBuilder err = new StringBuilder();
            int code = RunSchtasks("/Delete /TN \"" + TaskName + "\" /F", err);
            if (code != 0)
            {
                string t = err.ToString();
                // Already gone counts as success (message matched in RU/EN).
                if (t.IndexOf("найти", StringComparison.OrdinalIgnoreCase) >= 0
                    || t.IndexOf("cannot find", StringComparison.OrdinalIgnoreCase) >= 0
                    || t.IndexOf("find the file", StringComparison.OrdinalIgnoreCase) >= 0)
                    return null;
                return t.Length > 0 ? t.Trim() : "schtasks exit=" + code;
            }
            return null;
        }

        private static int RunSchtasks(string args)
        {
            return RunSchtasks(args, null);
        }

        private static int RunSchtasks(string args, StringBuilder stderr)
        {
            try
            {
                var psi = new ProcessStartInfo("schtasks.exe", args);
                psi.UseShellExecute = false;
                psi.RedirectStandardOutput = true;
                psi.RedirectStandardError = true;
                psi.CreateNoWindow = true;
                // schtasks writes in the console OEM code page.
                try
                {
                    int oem = GetOEMCP();
                    psi.StandardOutputEncoding = System.Text.Encoding.GetEncoding(oem);
                    psi.StandardErrorEncoding = System.Text.Encoding.GetEncoding(oem);
                }
                catch { }
                using (var p = new Process())
                {
                    p.StartInfo = psi;
                    p.Start();
                    string eo = p.StandardOutput.ReadToEnd();
                    string ee = p.StandardError.ReadToEnd();
                    p.WaitForExit(30000);
                    if (stderr != null)
                    {
                        string all = (eo + "\n" + ee).Trim();
                        if (all.Length > 800) all = all.Substring(0, 800);
                        stderr.Append(all);
                    }
                    return p.ExitCode;
                }
            }
            catch (Exception ex)
            {
                if (stderr != null) stderr.Append(ex.Message);
                return -1;
            }
        }

        [System.Runtime.InteropServices.DllImport("kernel32.dll")]
        private static extern int GetOEMCP();

        /// <summary>
        /// Quiet mode: count updates, balloon the count (no program list);
        /// clicking the balloon opens winstall.
        /// </summary>
        public static void RunCheckAndNotify()
        {
            int n = -1;
            for (int attempt = 0; attempt < 2 && n < 0; attempt++)
            {
                try
                {
                    // The network may still be coming up after sleep/fresh logon; retry once.
                    if (attempt > 0) Thread.Sleep(90000);
                    n = WingetRunner.GetUpgradesAsync().GetAwaiter().GetResult().Count;
                }
                catch
                {
                    n = -1;
                }
            }
            if (n <= 0) return;

            string exe = Application.ExecutablePath;
            ShowBalloon(
                L.T("check_title"),
                L.F("check_balloon", n),
                delegate
                {
                    try { Process.Start(exe); }
                    catch { }
                });
        }

        public static void RunMissingNotify()
        {
            ShowBalloon(L.T("check_title"), L.T("check_missing"), null);
        }

        private static void ShowBalloon(string title, string text, Action onClick)
        {
            string exe = Application.ExecutablePath;
            Icon icon = null;
            try { icon = Icon.ExtractAssociatedIcon(exe); }
            catch { }
            using (var ni = new NotifyIcon())
            {
                ni.Icon = icon ?? SystemIcons.Information;
                ni.Text = "winstall";
                ni.Visible = true;
                ni.BalloonTipTitle = title;
                ni.BalloonTipText = text;
                ni.BalloonTipIcon = ToolTipIcon.Info;
                using (var done = new ManualResetEvent(false))
                {
                    ni.BalloonTipClicked += delegate
                    {
                        try { if (onClick != null) onClick(); }
                        catch { }
                        done.Set();
                    };
                    ni.BalloonTipClosed += delegate { done.Set(); };
                    ni.ShowBalloonTip(15000);
                    using (var t = new System.Threading.Timer(delegate { done.Set(); }, null, 25000, Timeout.Infinite))
                    {
                        while (!done.WaitOne(200))
                            Application.DoEvents();
                    }
                }
                ni.Visible = false;
            }
        }
    }
}
