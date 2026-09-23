using System;
using System.Windows.Forms;

namespace winstall
{
    static class Program
    {
        [STAThread]
        static void Main(string[] args)
        {
            // Для корректного чтения UTF-8 вывода winget (русские имена).
            try { Console.OutputEncoding = System.Text.Encoding.UTF8; } catch { }

            // Тихий режим для задачи планировщика: проверить и уведомить, без окна.
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
