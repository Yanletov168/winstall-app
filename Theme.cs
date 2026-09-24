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
            public Color ButtonHover;
            public Color ButtonDown;
            public Color FrameBorder;
            public Color FocusBorder;
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
            MenuSelected = SystemColors.Highlight,
            ButtonHover = SystemColors.ControlLight,
            ButtonDown = SystemColors.ControlDark,
            FrameBorder = SystemColors.ControlDark,
            FocusBorder = SystemColors.Highlight
        };

        public static readonly Palette Dark = new Palette
        {
            FormBg = Color.FromArgb(0x20, 0x20, 0x20),
            Window = Color.FromArgb(0x1E, 0x1E, 0x1E),
            Control = Color.FromArgb(0x38, 0x38, 0x38),
            Text = Color.FromArgb(0xF5, 0xF5, 0xF5),
            GrayText = Color.FromArgb(0xB8, 0xB8, 0xB8),
            DimText = Color.FromArgb(0x9A, 0x9A, 0x9A),
            VersionBlue = Color.FromArgb(0x7F, 0xB8, 0xFF),
            CheckedGreen = Color.FromArgb(0x7B, 0xD8, 0x8A),
            HeaderBg = Color.FromArgb(0x2D, 0x2D, 0x2D),
            HeaderText = Color.FromArgb(0xF5, 0xF5, 0xF5),
            HeaderBorder = Color.FromArgb(0x3D, 0x3D, 0x3D),
            MenuBg = Color.FromArgb(0x2B, 0x2B, 0x2B),
            MenuText = Color.FromArgb(0xF5, 0xF5, 0xF5),
            MenuBorder = Color.FromArgb(0x50, 0x50, 0x50),
            MenuSelected = Color.FromArgb(0x40, 0x40, 0x40),
            ButtonHover = Color.FromArgb(0x45, 0x45, 0x45),
            ButtonDown = Color.FromArgb(0x33, 0x33, 0x33),
            FrameBorder = Color.FromArgb(0x50, 0x50, 0x50),
            FocusBorder = Color.FromArgb(0x4C, 0xC2, 0xFF)
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
            Paint(form, Current, dark);
            PaintMenu(menu);
            DarkScrollBars(form, dark);
            SetTitleBarDark(form, dark);
        }

        public static void PaintMenu(ContextMenuStrip menu)
        {
            if (menu == null) return;
            Palette c = Current;
            menu.Renderer = c == Dark
                ? (ToolStripRenderer)new DarkRenderer(c)
                : (ToolStripRenderer)new ToolStripProfessionalRenderer();
            menu.BackColor = c.MenuBg;
            menu.ForeColor = c.MenuText;
            PaintMenuItems(menu.Items, c);
        }

        private static void PaintMenuItems(ToolStripItemCollection items, Palette c)
        {
            foreach (ToolStripItem it in items)
            {
                it.BackColor = c.MenuBg;
                it.ForeColor = c.MenuText;
                if (it is ToolStripMenuItem mi) PaintMenuItems(mi.DropDownItems, c);
            }
        }

        private static void Paint(Control x, Palette c, bool dark)
        {
            if (x is TextBox) { x.BackColor = c.Window; x.ForeColor = c.Text; }
            else if (x is Button b)
            {
                b.ForeColor = c.Text;
                if (dark)
                {
                    b.FlatStyle = FlatStyle.Flat;
                    b.BackColor = c.Control;
                    b.FlatAppearance.BorderColor = c.MenuBorder;
                    b.FlatAppearance.BorderSize = 1;
                    b.FlatAppearance.MouseOverBackColor = c.ButtonHover;
                    b.FlatAppearance.MouseDownBackColor = c.ButtonDown;
                }
                else
                {
                    b.FlatStyle = FlatStyle.Standard;
                    b.BackColor = c.Control;
                }
            }
            else if (x is ComboBox cb)
            {
                cb.BackColor = c.Window;
                cb.ForeColor = c.Text;
                cb.FlatStyle = dark ? FlatStyle.Flat : FlatStyle.Standard;
            }
            else if (x is CheckBox) { x.ForeColor = c.Text; }
            else if (x is ListView) { x.BackColor = c.Window; x.ForeColor = c.Text; }
            else if (x is StatusStrip) { x.BackColor = c.Control; x.ForeColor = c.Text; }
            else if (x is Label lb) { lb.ForeColor = Equals(lb.Tag, "muted") ? c.GrayText : c.Text; }
            else if (x is Panel p && Equals(p.Tag, "frame")) { p.BackColor = c.FrameBorder; }
            else if (x is Panel || x is Form) { x.BackColor = c.FormBg; x.ForeColor = c.Text; }
            foreach (Control ch in x.Controls) Paint(ch, c, dark);
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

        private sealed class DarkRenderer : ToolStripProfessionalRenderer
        {
            public DarkRenderer(Palette palette) : base(new DarkTable(palette)) { }

            // White check glyph; the table only tints its background.
            protected override void OnRenderItemCheck(ToolStripItemImageRenderEventArgs e)
            {
                Rectangle r = e.ImageRectangle;
                int s = Math.Min(r.Width, r.Height);
                if (s < 6) return;
                int m = Math.Max(2, s / 5);
                Point[] pts = new Point[]
                {
                    new Point(r.Left + m, r.Top + r.Height / 2),
                    new Point(r.Left + r.Width / 2 - 1, r.Bottom - m),
                    new Point(r.Right - m, r.Top + m)
                };
                var old = e.Graphics.SmoothingMode;
                e.Graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
                using (var pen = new Pen(Color.White, Math.Max(2, s / 8)))
                    e.Graphics.DrawLines(pen, pts);
                e.Graphics.SmoothingMode = old;
            }

            // White submenu arrow; the default one follows the OS theme and vanishes on dark.
            protected override void OnRenderArrow(ToolStripArrowRenderEventArgs e)
            {
                Rectangle r = e.ArrowRectangle;
                int s = Math.Min(r.Width, r.Height);
                if (s < 6) return;
                int m = Math.Max(2, s / 4);
                Point[] tri;
                if (e.Direction == ArrowDirection.Left)
                    tri = new Point[]
                    {
                        new Point(r.Right - m, r.Top + m),
                        new Point(r.Right - m, r.Bottom - m),
                        new Point(r.Left + m, r.Top + r.Height / 2)
                    };
                else if (e.Direction == ArrowDirection.Up)
                    tri = new Point[]
                    {
                        new Point(r.Left + m, r.Bottom - m),
                        new Point(r.Right - m, r.Bottom - m),
                        new Point(r.Left + r.Width / 2, r.Top + m)
                    };
                else if (e.Direction == ArrowDirection.Down)
                    tri = new Point[]
                    {
                        new Point(r.Left + m, r.Top + m),
                        new Point(r.Right - m, r.Top + m),
                        new Point(r.Left + r.Width / 2, r.Bottom - m)
                    };
                else
                    tri = new Point[]
                    {
                        new Point(r.Left + m, r.Top + m),
                        new Point(r.Left + m, r.Bottom - m),
                        new Point(r.Right - m, r.Top + r.Height / 2)
                    };
                var old = e.Graphics.SmoothingMode;
                e.Graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
                using (var brush = new SolidBrush(Color.White))
                    e.Graphics.FillPolygon(brush, tri);
                e.Graphics.SmoothingMode = old;
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

        [DllImport("uxtheme.dll", CharSet = CharSet.Unicode)]
        private static extern int SetWindowTheme(IntPtr hwnd, string appName, string idList);

        // Dark Explorer scrollbars on 1809+; silently ignored elsewhere.
        private static void DarkScrollBars(Control x, bool dark)
        {
            try
            {
                if (x.IsHandleCreated && (x is ListView || x is TextBox || x is ComboBox))
                {
                    if (dark)
                        SetWindowTheme(x.Handle, "DarkMode_Explorer", null);
                    else
                        SetWindowTheme(x.Handle, null, null);
                }
            }
            catch { }
            foreach (Control ch in x.Controls) DarkScrollBars(ch, dark);
        }

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
