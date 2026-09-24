using System;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using Microsoft.Win32;

namespace winstall
{
    /// <summary>
    /// Light/dark theming for classic WinForms: palette-driven control colors,
    /// an owner-drawn list header (rows keep native rendering, checkboxes included),
    /// a dark title bar and a dark menu renderer.
    /// </summary>
    public static class Theme
    {
        public sealed class Palette
        {
            public Color FormBg;
            public Color Window;
            public Color Control;
            public Color Text;
            public Color GrayText;
            public Color DimText;
            public Color VersionBlue;
            public Color CheckedGreen;
            public Color HeaderBg;
            public Color HeaderText;
            public Color HeaderBorder;
            public Color MenuBg;
            public Color MenuText;
            public Color MenuBorder;
            public Color MenuSelected;
        }

        public static readonly Palette Light = new Palette
        {
            FormBg = SystemColors.Control,
            Window = SystemColors.Window,
            Control = SystemColors.Control,
            Text = SystemColors.WindowText,
            GrayText = SystemColors.GrayText,
            DimText = SystemColors.GrayText,
            VersionBlue = Color.MidnightBlue,
            CheckedGreen = Color.ForestGreen,
            HeaderBg = SystemColors.Control,
            HeaderText = SystemColors.WindowText,
            HeaderBorder = SystemColors.ControlDark,
            MenuBg = SystemColors.Window,
            MenuText = SystemColors.WindowText,
            MenuBorder = SystemColors.ControlDark,
            MenuSelected = SystemColors.Highlight
        };

        public static readonly Palette Dark = new Palette
        {
            FormBg = Color.FromArgb(0x20, 0x20, 0x20),
            Window = Color.FromArgb(0x1E, 0x1E, 0x1E),
            Control = Color.FromArgb(0x38, 0x38, 0x38),
            Text = Color.FromArgb(0xF5, 0xF5, 0xF5),
            GrayText = Color.FromArgb(0xA6, 0xA6, 0xA6),
            DimText = Color.FromArgb(0x9A, 0x9A, 0x9A),
            VersionBlue = Color.FromArgb(0x7F, 0xB8, 0xFF),
            CheckedGreen = Color.FromArgb(0x7B, 0xD8, 0x8A),
            HeaderBg = Color.FromArgb(0x2D, 0x2D, 0x2D),
            HeaderText = Color.FromArgb(0xF5, 0xF5, 0xF5),
            HeaderBorder = Color.FromArgb(0x3D, 0x3D, 0x3D),
            MenuBg = Color.FromArgb(0x2B, 0x2B, 0x2B),
            MenuText = Color.FromArgb(0xF5, 0xF5, 0xF5),
            MenuBorder = Color.FromArgb(0x50, 0x50, 0x50),
            MenuSelected = Color.FromArgb(0x40, 0x40, 0x40)
        };

        public static Palette Current { get; private set; } = Light;

        public static bool IsDark(string mode)
        {
            if (mode == "dark") return true;
            if (mode == "light") return false;
            return SystemUsesDark();
        }

        public static bool SystemUsesDark()
        {
            try
            {
                using (var k = Registry.CurrentUser.OpenSubKey(
                    @"SOFTWARE\Microsoft\Windows\CurrentVersion\Themes\Personalize", false))
                {
                    if (k == null) return false;
                    return Convert.ToInt32(k.GetValue("AppsUseLightTheme", 1)) == 0;
                }
            }
            catch { return false; }
        }

        public static void Apply(Form form, ContextMenuStrip menu, bool dark)
        {
            Current = dark ? Dark : Light;
            Paint(form, Current);
            if (menu != null)
            {
                menu.Renderer = dark
                    ? (ToolStripRenderer)new ToolStripProfessionalRenderer(new DarkTable(Current))
                    : (ToolStripRenderer)new ToolStripProfessionalRenderer();
            }
            SetTitleBarDark(form, dark);
        }

        private static void Paint(Control x, Palette c)
        {
            if (x is TextBox) { x.BackColor = c.Window; x.ForeColor = c.Text; }
            else if (x is Button) { x.BackColor = c.Control; x.ForeColor = c.Text; }
            else if (x is ComboBox) { x.BackColor = c.Window; x.ForeColor = c.Text; }
            else if (x is CheckBox) { x.ForeColor = c.Text; }
            else if (x is ListView) { x.BackColor = c.Window; x.ForeColor = c.Text; }
            else if (x is StatusStrip) { x.BackColor = c.Control; x.ForeColor = c.Text; }
            else if (x is Label lb) { lb.ForeColor = Equals(lb.Tag, "muted") ? c.GrayText : c.Text; }
            else if (x is Panel || x is Form) { x.BackColor = c.FormBg; x.ForeColor = c.Text; }
            foreach (Control ch in x.Controls) Paint(ch, c);
            if (x is StatusStrip ss)
                foreach (ToolStripItem it in ss.Items) it.ForeColor = c.Text;
        }

        public static void DrawHeader(DrawListViewColumnHeaderEventArgs e)
        {
            Palette c = Current;
            using (var bg = new SolidBrush(c.HeaderBg))
                e.Graphics.FillRectangle(bg, e.Bounds);
            Rectangle text = e.Bounds;
            text.X += 6;
            text.Width -= 12;
            TextRenderer.DrawText(e.Graphics, e.Header.Text, e.Font, text, c.HeaderText,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
            using (var p = new Pen(c.HeaderBorder))
            {
                e.Graphics.DrawLine(p, e.Bounds.Left, e.Bounds.Bottom - 1, e.Bounds.Right, e.Bounds.Bottom - 1);
                e.Graphics.DrawLine(p, e.Bounds.Right - 1, e.Bounds.Top, e.Bounds.Right - 1, e.Bounds.Bottom);
            }
        }

        private sealed class DarkTable : ProfessionalColorTable
        {
            private readonly Palette c;
            public DarkTable(Palette palette) { c = palette; }
            public override Color ToolStripDropDownBackground { get { return c.MenuBg; } }
            public override Color MenuBorder { get { return c.MenuBorder; } }
            public override Color MenuItemBorder { get { return c.MenuSelected; } }
            public override Color MenuItemSelected { get { return c.MenuSelected; } }
            public override Color MenuItemSelectedGradientBegin { get { return c.MenuSelected; } }
            public override Color MenuItemSelectedGradientEnd { get { return c.MenuSelected; } }
            public override Color MenuItemPressedGradientBegin { get { return c.MenuSelected; } }
            public override Color MenuItemPressedGradientEnd { get { return c.MenuSelected; } }
            public override Color CheckBackground { get { return c.MenuSelected; } }
            public override Color CheckSelectedBackground { get { return c.MenuSelected; } }
            public override Color CheckPressedBackground { get { return c.MenuSelected; } }
            public override Color SeparatorDark { get { return c.MenuBorder; } }
            public override Color SeparatorLight { get { return c.MenuBg; } }
            public override Color ImageMarginGradientBegin { get { return c.MenuBg; } }
            public override Color ImageMarginGradientMiddle { get { return c.MenuBg; } }
            public override Color ImageMarginGradientEnd { get { return c.MenuBg; } }
            public override Color ToolStripBorder { get { return c.MenuBg; } }
        }

        [DllImport("dwmapi.dll")]
        private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int value, int size);
        private const int DWMWA_USE_IMMERSIVE_DARK_MODE = 20;

        private static void SetTitleBarDark(Form form, bool dark)
        {
            try
            {
                if (!form.IsHandleCreated) return;
                if (Environment.OSVersion.Version < new Version(10, 0, 17763)) return;
                int v = dark ? 1 : 0;
                DwmSetWindowAttribute(form.Handle, DWMWA_USE_IMMERSIVE_DARK_MODE, ref v, 4);
            }
            catch { }
        }
    }
}
