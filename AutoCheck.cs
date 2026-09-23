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
    /// Автопроверка обновлений без службы и без прав админа:
    /// вход в систему — через автозапуск HKCU\...\Run,
    /// пробуждение — через задачу планировщика \\winstall\\winstall-wake
    /// (триггер ONEVENT: Power-Troubleshooter ID 1).
    /// Оба запускают winstall.exe --check-updates: тихо считает обновления
    /// и показывает баллун только с количеством, без списка программ.
    /// Проверено: ONLOGON-триггер и импорт XML требуют админа, а связка
    /// Run + ONEVENT работает из-под обычного пользователя.
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
            // Откат: не оставляем половинчатое состояние.
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

        // ---------- автозапуск (вход) ----------

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

        // ---------- задача на пробуждение ----------

        private static bool TaskExists()
        {
            return RunSchtasks("/Query /TN \"" + TaskName + "\"") == 0;
        }

        private static string CreateWakeTask()
        {
            // /TR целиком — один argv-элемент: внешние кавычки для CreateProcess,
            // внутренние экранируем как \" — иначе путь с пробелами развалится
            // (см.: schtasks ругался «Неправильный параметр» из Default Project).
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
                // Задачи уже нет — это тоже успех.
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

        private static int RunSchtasks(string args, System.Text.StringBuilder stderr)
        {
            try
            {
                var psi = new ProcessStartInfo("schtasks.exe", args);
                psi.UseShellExecute = false;
                psi.RedirectStandardOutput = true;
                psi.RedirectStandardError = true;
                psi.CreateNoWindow = true;
                // schtasks пишет в OEM-кодировке консоли.
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
        /// Тихий режим: посчитать обновления, показать баллун с количеством
        /// (без списка программ), клик по баллуну открывает winstall.
        /// </summary>
        public static void RunCheckAndNotify()
        {
            int n = -1;
            for (int attempt = 0; attempt < 2 && n < 0; attempt++)
            {
                try
                {
                    // После сна/свежего входа сеть может ещё подниматься — повтор.
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
            Icon icon = null;
            try { icon = Icon.ExtractAssociatedIcon(exe); }
            catch { }
            using (var ni = new NotifyIcon())
            {
                ni.Icon = icon ?? SystemIcons.Information;
                ni.Text = "winstall";
                ni.Visible = true;
                ni.BalloonTipTitle = L.T("check_title");
                ni.BalloonTipText = L.F("check_balloon", n);
                ni.BalloonTipIcon = ToolTipIcon.Info;
                using (var done = new ManualResetEvent(false))
                {
                    ni.BalloonTipClicked += delegate
                    {
                        try { Process.Start(exe); }
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
