using System;
using System.Windows.Forms;

namespace winstall
{
    static class Program
    {
        [STAThread]
        static void Main(string[] args)
        {
            // UTF-8 for winget output (non-ASCII package names).
            try { Console.OutputEncoding = System.Text.Encoding.UTF8; } catch { }

            // Quiet mode for the scheduler task: check, notify, no window.
            foreach (var a in args)
            {
                if (a.Equals(AutoCheck.CheckArg, StringComparison.OrdinalIgnoreCase))
                {
                    L.Startup();
                    AutoCheck.RunCheckAndNotify();
                    return;
                }
            }

            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.Run(new MainForm());
        }
    }
}
