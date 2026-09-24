using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace winstall
{
    /// <summary>
    /// MInstall-style main window: category dropdown + Run button on top, a single
    /// checklist (icon, name, version, action glyph) in the middle, search + counter
    /// + bulk buttons and a details pane at the bottom. The UI language comes from
    /// locales/*.json next to the executable (hamburger menu); embedded English
    /// is used when no files are found.
    /// </summary>
    public class MainForm : Form
    {
        // Gray cue banner for the search box (EM_SETCUEBANNER).
        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern int SendMessage(IntPtr hWnd, int msg, int wParam, string lParam);
        private const int EM_SETCUEBANNER = 0x1501;

        private Button btnView;
        private ContextMenuStrip viewMenu;
        private int viewIndex;
        private Button btnGo;
        private Button btnUpgradeAll;
        private Button btnRefresh;
        private Button btnMenu;
        private ContextMenuStrip hamburger;
        private ListView lv;
        private ColumnHeader colName;
        private ColumnHeader colVer;
        private ColumnHeader colAct;
        private TextBox txtSearch;
        private Label lblCount;
        private CheckBox chkAll;
        private Button btnNone;
        private Button btnUpg;
        private TextBox txtDesc;
        private StatusStrip status;
        private ToolStripStatusLabel lblStatus;
        private ToolStripProgressBar progress;
        private Timer searchTimer;
        private IconProvider icons;

        private List<PackageInfo> installed = new List<PackageInfo>();
        private List<PackageInfo> searchBase; // Null when no search is active; the installed list shows then.
        private bool busy;
        private bool syncing;
        private bool autoCheckEnabled;

        public MainForm()
        {
            L.Startup();
            Text = L.T("app_title");
            try { Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath); }
            catch { }
            StartPosition = FormStartPosition.CenterScreen;
            Size = new Size(900, 620);
            MinimumSize = new Size(700, 480);
            Font = new Font("Segoe UI", 9f);

            icons = new IconProvider();
            BuildUi();
            // Resolve the auto-check state off the UI thread to keep startup snappy.
            Task.Run(delegate { autoCheckEnabled = AutoCheck.IsEnabled(); });

            searchTimer = new Timer();
            searchTimer.Interval = 500;
            searchTimer.Tick += delegate { searchTimer.Stop(); OnSearchGo(); };

            Shown += delegate { ApplyTheme(); RefreshAll(); };
            FormClosed += delegate
            {
                try { Microsoft.Win32.SystemEvents.UserPreferenceChanged -= OnUserPreferenceChanged; }
                catch { }
                if (icons != null) icons.Dispose();
            };
            Microsoft.Win32.SystemEvents.UserPreferenceChanged += OnUserPreferenceChanged;
        }

        private void OnUserPreferenceChanged(object sender, Microsoft.Win32.UserPreferenceChangedEventArgs e)
        {
            // Live switch only makes sense in system-following mode; the check is cheap.
            if (Options.Theme != "system") return;
            try
            {
                if (IsHandleCreated) BeginInvoke(new Action(ApplyTheme));
            }
            catch { }
        }

        private void BuildUi()
        {
            // Top bar: category dropdown + Run.
            var top = new Panel();
            top.Dock = DockStyle.Top;
            top.Height = 34;
            top.Padding = new Padding(6, 6, 4, 2);
            Controls.Add(top);

            // View selector: a flat button with a themed dropdown menu instead of
            // a ComboBox, whose arrow button and popup ignore dark colors.
            btnView = new Button();
            btnView.Dock = DockStyle.Fill;
            btnView.TextAlign = ContentAlignment.MiddleLeft;
            btnView.Click += delegate
            {
                if (viewMenu != null)
                    viewMenu.Show(btnView, new Point(0, btnView.Height));
            };
            top.Controls.Add(btnView);
            viewMenu = new ContextMenuStrip();
            viewIndex = 0;
            RebuildViewMenu();
            btnRefresh = new Button();
            btnRefresh.Text = L.T("btn_refresh");
            btnRefresh.Width = 90;
            btnRefresh.Dock = DockStyle.Right;
            btnRefresh.Click += delegate { RefreshAll(); };
            top.Controls.Add(btnRefresh);

            btnUpgradeAll = new Button();
            btnUpgradeAll.Text = L.T("btn_update_all");
            btnUpgradeAll.Width = 120;
            btnUpgradeAll.Dock = DockStyle.Right;
            btnUpgradeAll.Click += delegate { UpgradeAll(); };
            top.Controls.Add(btnUpgradeAll);

            btnGo = new Button();
            btnGo.Text = L.T("btn_go");
            btnGo.Width = 110;
            btnGo.Dock = DockStyle.Right;
            btnGo.Click += delegate { ApplyChecked(); };
            top.Controls.Add(btnGo);

            btnMenu = new Button();
            btnMenu.Text = "☰";
            btnMenu.Font = new Font("Segoe UI", 10f, FontStyle.Bold);
            btnMenu.Width = 42;
            btnMenu.Dock = DockStyle.Right;
            btnMenu.Click += delegate
            {
                if (hamburger != null)
                    hamburger.Show(btnMenu, new Point(0, btnMenu.Height));
            };
            top.Controls.Add(btnMenu);

            hamburger = new ContextMenuStrip();
            BuildHamburger();
            // Rescan locales before showing the menu so freshly dropped files appear.
            hamburger.Opening += delegate { L.Rescan(); BuildHamburger(); };
            // Bottom bar: search row, counter, bulk actions.
            var bottom = new Panel();
            bottom.Dock = DockStyle.Bottom;
            bottom.Height = 118;
            Controls.Add(bottom);

            var searchRow = new Panel();
            searchRow.Dock = DockStyle.Top;
            searchRow.Height = 30;
            searchRow.Padding = new Padding(6, 4, 4, 2);
            // The search row is added later: WinForms docks the last-added control first,
            // so the Fill pane goes in before the Top row. See bottom.Controls.Add below.

            var lblFind = new Label();
            lblFind.Text = "🔍";
            lblFind.AutoSize = true;
            lblFind.Dock = DockStyle.Left;
            lblFind.Padding = new Padding(0, 4, 2, 0);
            searchRow.Controls.Add(lblFind);

            btnUpg = new Button();
            btnUpg.Text = L.T("btn_show_upd");
            btnUpg.Width = 105;
            btnUpg.Dock = DockStyle.Right;
            btnUpg.Click += delegate { SelectUpgradable(); };
            searchRow.Controls.Add(btnUpg);

            btnNone = new Button();
            btnNone.Text = L.T("btn_none");
            btnNone.Width = 80;
            btnNone.Dock = DockStyle.Right;
            btnNone.Click += delegate { UncheckAll(); };
            searchRow.Controls.Add(btnNone);

            chkAll = new CheckBox();
            chkAll.Text = L.T("chk_all");
            chkAll.AutoSize = true;
            chkAll.Dock = DockStyle.Right;
            chkAll.Padding = new Padding(6, 4, 2, 0);
            chkAll.CheckedChanged += delegate
            {
                if (syncing) return;
                SetAllChecked(chkAll.Checked);
            };
            searchRow.Controls.Add(chkAll);

            lblCount = new Label();
            lblCount.Text = L.F("count_fmt", 0, 0);
            lblCount.AutoSize = true;
            lblCount.Dock = DockStyle.Right;
            lblCount.Padding = new Padding(6, 5, 6, 0);
            lblCount.ForeColor = SystemColors.GrayText;
            lblCount.Tag = "muted";
            searchRow.Controls.Add(lblCount);

            txtSearch = new TextBox();
            txtSearch.BorderStyle = BorderStyle.None;
            txtSearch.Dock = DockStyle.Fill;
            txtSearch.TextChanged += delegate
            {
                searchTimer.Stop();
                searchTimer.Start();
            };
            var searchFrame = FrameFor(txtSearch);
            searchRow.Controls.Add(searchFrame);
            // Dock the Fill control last, otherwise it stretches underneath the buttons.
            searchRow.Controls.SetChildIndex(searchFrame, 0);
            SendMessage(txtSearch.Handle, EM_SETCUEBANNER, 0, L.T("search_cue"));

            // Details pane for the selected package.
            txtDesc = new TextBox();
            txtDesc.BorderStyle = BorderStyle.None;
            txtDesc.Dock = DockStyle.Fill;
            txtDesc.Multiline = true;
            txtDesc.ReadOnly = true;
            txtDesc.ScrollBars = ScrollBars.Vertical;
            txtDesc.BackColor = SystemColors.Window;
            txtDesc.ForeColor = SystemColors.GrayText;
            txtDesc.Text = L.T("desc_legend");
            bottom.Controls.Add(FrameFor(txtDesc));
            // The search row goes in after the details pane so Top docks before Fill.
            bottom.Controls.Add(searchRow);

            // Single list.
            lv = new ListView();
            lv.Dock = DockStyle.Fill;
            lv.View = View.Details;
            lv.CheckBoxes = true;
            lv.FullRowSelect = true;
            lv.GridLines = false;
            lv.MultiSelect = false;
            lv.HideSelection = false;
            lv.OwnerDraw = true;
            lv.DrawColumnHeader += delegate (object s, DrawListViewColumnHeaderEventArgs e) { Theme.DrawHeader(e); };
            lv.DrawItem += delegate (object s, DrawListViewItemEventArgs e) { e.DrawDefault = true; };
            lv.DrawSubItem += delegate (object s, DrawListViewSubItemEventArgs e) { e.DrawDefault = true; };
            lv.SmallImageList = icons.Images;
            colName = lv.Columns.Add(L.T("col_program"), 400);
            colVer = lv.Columns.Add(L.T("col_version"), 110);
            colVer.TextAlign = HorizontalAlignment.Right;
            colAct = lv.Columns.Add("✦", 44);
            colAct.TextAlign = HorizontalAlignment.Center;
            lv.ItemChecked += Lv_ItemChecked;
            lv.SelectedIndexChanged += delegate { UpdateDesc(); };
            lv.Resize += delegate { FitColumns(); };
            Controls.Add(lv);
            lv.BringToFront();

            status = new StatusStrip();
            lblStatus = new ToolStripStatusLabel(L.T("status_ready"));
            lblStatus.Spring = true;
            lblStatus.TextAlign = ContentAlignment.MiddleLeft;
            progress = new ToolStripProgressBar();
            progress.Visible = false;
            progress.Style = ProgressBarStyle.Marquee;
            status.Items.Add(lblStatus);
            status.Items.Add(progress);
            Controls.Add(status);

            FitColumns();
            ApplyTheme();
        }

        internal void ApplyTheme()
        {
            Theme.Apply(this, hamburger, Theme.IsDark(Options.Theme));
            foreach (ListViewItem it in lv.Items) PaintRow(it);
            UpdateDesc();
        }

        internal int RowCount { get { return lv.Items.Count; } }

        // Test seam: full client-area bitmap for theme verification.
        internal Bitmap CaptureForm()
        {
            var bmp = new Bitmap(ClientSize.Width, ClientSize.Height);
            DrawToBitmap(bmp, new Rectangle(Point.Empty, bmp.Size));
            return bmp;
        }

        // Test seam: average color of the list header area for theme verification.
        internal Color SampleHeaderColor()
        {
            if (!lv.IsHandleCreated || lv.ClientSize.Width < 10) return Color.Empty;
            using (var bmp = new Bitmap(lv.ClientSize.Width, lv.ClientSize.Height))
            {
                lv.DrawToBitmap(bmp, new Rectangle(Point.Empty, bmp.Size));
                long r = 0, n = 0;
                for (int x = 8; x < bmp.Width - 8; x += 13)
                    for (int y = 6; y < 14 && y < bmp.Height; y += 2)
                    {
                        Color px = bmp.GetPixel(x, y);
                        r += px.R; n++;
                    }
                if (n == 0) return Color.Empty;
                int avg = (int)(r / n);
                return Color.FromArgb(avg, avg, avg);
            }
        }

        private void PaintRow(ListViewItem it)
        {
            var p = it.Tag as PackageInfo;
            if (p == null) return;
            Theme.Palette c = Theme.Current;
            bool check = it.Checked;
            string glyph = ActionGlyph(p.ActionFor(check));
            if (glyph == "" && p.IsInstalled && p.HasUpdate) glyph = "↑";
            if (it.SubItems.Count > 2)
            {
                it.SubItems[2].Text = glyph;
                it.SubItems[2].ForeColor = check ? c.CheckedGreen : c.GrayText;
            }
            if (it.SubItems.Count > 1)
                it.SubItems[1].ForeColor = check ? c.CheckedGreen : c.VersionBlue;
            it.ForeColor = check ? c.CheckedGreen
                : (p.IsInstalled ? c.Text : c.DimText);
        }

        // Borderless edit inside a 1px themed frame; border tints on focus.
        private static Panel FrameFor(TextBox tb)
        {
            var frame = new Panel();
            frame.Dock = DockStyle.Fill;
            frame.Padding = new Padding(1);
            frame.Tag = "frame";
            tb.Dock = DockStyle.Fill;
            frame.Controls.Add(tb);
            tb.Enter += delegate { frame.BackColor = Theme.Current.FocusBorder; };
            tb.Leave += delegate { frame.BackColor = Theme.Current.FrameBorder; };
            return frame;
        }
        private void FitColumns()
        {
            if (lv == null || colName == null) return;
            // Columns span the full client width, including the zone under the
            // scrollbar: with owner-drawn headers any gap would stay unpainted (white).
            colName.Width = Math.Max(150, lv.ClientSize.Width - colVer.Width - 44);
            // Every leftover pixel goes to the last column for the same reason.
            colAct.Width = Math.Max(44, lv.ClientSize.Width - colName.Width - colVer.Width);
        }

        private void BuildHamburger()
        {
            hamburger.Items.Clear();
            hamburger.Items.Add(L.T("btn_update_all"), null, delegate { UpgradeAll(); });
            hamburger.Items.Add(new ToolStripSeparator());
            hamburger.Items.Add(L.T("menu_export"), null, delegate { ExportList(); });
            hamburger.Items.Add(L.T("menu_import"), null, delegate { ImportList(); });
            hamburger.Items.Add(new ToolStripSeparator());
            hamburger.Items.Add(L.T("menu_sel_upd"), null, delegate { SelectUpgradable(); });
            hamburger.Items.Add(L.T("menu_uncheck"), null, delegate { UncheckAll(); });
            var mAuto = new ToolStripMenuItem(L.T("menu_autocheck"));
            mAuto.Checked = autoCheckEnabled;
            mAuto.Click += delegate { ToggleAutoCheck(); };
            hamburger.Items.Add(mAuto);
            hamburger.Items.Add(new ToolStripSeparator());
            hamburger.Items.Add(L.T("menu_logs"), null, delegate { OpenWingetLogs(); });
            var mLogsDir = new ToolStripMenuItem(L.T("menu_logsdir"));
            mLogsDir.Click += delegate { ChooseLogsDir(); };
            hamburger.Items.Add(mLogsDir);
            if (!string.IsNullOrWhiteSpace(Options.LogsDir))
            {
                var mLogsDef = new ToolStripMenuItem(L.T("menu_logs_default"));
                mLogsDef.Click += delegate
                {
                    Options.LogsDir = "";
                    Options.Save();
                };
                hamburger.Items.Add(mLogsDef);
            }
            var mLang = new ToolStripMenuItem(L.T("menu_lang"));
            foreach (var li in L.Available)
            {
                string code = li.Code;
                var mi = new ToolStripMenuItem(li.Name);
                mi.Checked = code.Equals(L.Code, StringComparison.OrdinalIgnoreCase);
                mi.Click += delegate { L.Set(code); ApplyLanguage(); };
                mLang.DropDownItems.Add(mi);
            }
            hamburger.Items.Add(mLang);
            var mTheme = new ToolStripMenuItem(L.T("menu_theme"));
            string[] themeCodes = new string[] { "dark", "light", "system" };
            string[] themeKeys = new string[] { "theme_dark", "theme_light", "theme_system" };
            for (int i = 0; i < themeCodes.Length; i++)
            {
                string code = themeCodes[i];
                var mi = new ToolStripMenuItem(L.T(themeKeys[i]));
                mi.Checked = code.Equals(Options.Theme, StringComparison.OrdinalIgnoreCase);
                mi.Click += delegate
                {
                    Options.Theme = code;
                    Options.Save();
                    ApplyTheme();
                };
                mTheme.DropDownItems.Add(mi);
            }
            hamburger.Items.Add(mTheme);
            hamburger.Items.Add(L.T("menu_about"), null, delegate { ShowAbout(); });
            hamburger.Items.Add(new ToolStripSeparator());
            hamburger.Items.Add(L.T("menu_exit"), null, delegate { Close(); });
            Theme.PaintMenu(hamburger); // Rebuilds (e.g. on menu open) reset item colors.
        }

        /// <summary>Retranslates the UI, preserving checked rows.</summary>
        private void ApplyLanguage()
        {
            var keep = new HashSet<PackageInfo>();
            foreach (var kv in CollectChecked()) keep.Add(kv.Key);

            RebuildViewMenu();

            Text = L.T("app_title");
            btnRefresh.Text = L.T("btn_refresh");
            btnUpg.Text = L.T("btn_show_upd");
            btnNone.Text = L.T("btn_none");
            chkAll.Text = L.T("chk_all");
            colName.Text = L.T("col_program");
            colVer.Text = L.T("col_version");
            SendMessage(txtSearch.Handle, EM_SETCUEBANNER, 0, L.T("search_cue"));
            UpdateUpgradeAllButton();
            BuildHamburger();
            ApplyFilter(); // Rebuilds rows and clears checks; restored below.
            lv.ItemChecked -= Lv_ItemChecked;
            try
            {
                foreach (ListViewItem it in lv.Items)
                {
                    var p = it.Tag as PackageInfo;
                    if (p == null) continue;
                    it.Checked = keep.Contains(p);
                    PaintRow(it);
                }
            }
            finally { lv.ItemChecked += Lv_ItemChecked; }
            RefreshCounts();
            UpdateDesc();
        }

        private void SetBusy(bool b, string text)
        {
            busy = b;
            progress.Visible = b;
            btnRefresh.Enabled = !b;
            btnGo.Enabled = !b;
            btnUpgradeAll.Enabled = !b && HasAnyUpdate();
            btnMenu.Enabled = !b;
            btnView.Enabled = !b;
            // The search box stays editable during operations.
            if (text != null) lblStatus.Text = text;
            Cursor = b ? Cursors.WaitCursor : Cursors.Default;
        }

        private void UpdateUpgradeAllButton()
        {
            int nUp = 0;
            foreach (var p in installed)
                if (p.HasUpdate) nUp++;
            btnUpgradeAll.Text = nUp == 0 ? L.T("btn_update_all") : L.F("btn_update_all_n", nUp);
            btnUpgradeAll.Enabled = !busy && nUp > 0;
        }

        // Refresh.

        private async void RefreshAll()
        {
            if (busy) return;
            string exe = Backend.ResolveExe();
            if (exe == null)
            {
                // No backend, no app: offer the App Installer like the Inno setup
                // does, then exit either way. Refusal simply closes the program.
                var dr = MessageBox.Show(this, L.T("msg_no_backend"), L.T("msg_no_backend_t"),
                    MessageBoxButtons.YesNo, MessageBoxIcon.Warning);
                if (dr == DialogResult.Yes)
                {
                    try { System.Diagnostics.Process.Start(Backend.DownloadPageUrl); }
                    catch { }
                }
                Close();
                return;
            }

            SetBusy(true, L.T("status_reading"));
            try
            {
                var tList = WingetRunner.GetInstalledAsync();
                var tUp = WingetRunner.GetUpgradesAsync();
                await Task.WhenAll(tList, tUp).ConfigureAwait(true);

                installed = tList.Result;
                var ups = tUp.Result;

                var upById = new Dictionary<string, UpgradeEntry>(StringComparer.OrdinalIgnoreCase);
                var upByName = new Dictionary<string, UpgradeEntry>(StringComparer.OrdinalIgnoreCase);
                foreach (var u in ups)
                {
                    if (!upById.ContainsKey(u.Id)) upById[u.Id] = u;
                    if (!string.IsNullOrWhiteSpace(u.Name) && !upByName.ContainsKey(u.Name.Trim()))
                        upByName[u.Name.Trim()] = u;
                }

                foreach (var p in installed)
                {
                    UpgradeEntry hit = null;
                    // Installed via winget: exact id match is the common case.
                    upById.TryGetValue(p.Id, out hit);
                    // Side-loaded (ARP entry without a source): match by exact name
                    // and upgrade through the linked UpgradeId.
                    if (hit == null && !string.IsNullOrWhiteSpace(p.Name))
                        upByName.TryGetValue(p.Name.Trim(), out hit);
                    if (hit != null)
                    {
                        p.HasUpdate = true;
                        p.AvailableVersion = hit.Available;
                        if (!p.Id.Equals(hit.Id, StringComparison.OrdinalIgnoreCase))
                            p.UpgradeId = hit.Id;
                    }
                    else if (WingetRunner.LooksLikeVersion(p.AvailableVersion) &&
                             WingetRunner.LooksLikeVersion(p.InstalledVersion) &&
                             !p.AvailableVersion.Trim().Equals(p.InstalledVersion.Trim(), StringComparison.OrdinalIgnoreCase))
                    {
                        // Newer `winget list` reports the available version itself (4th column);
                        // trust it when `winget upgrade` missed the row.
                        p.HasUpdate = true;
                    }
                }
                // Upgradable rows first.
                installed = installed
                    .OrderByDescending(delegate (PackageInfo p) { return p.HasUpdate; })
                    .ThenBy(delegate (PackageInfo p) { return p.DisplayName; })
                    .ToList();

                searchBase = null;
                txtSearch.Clear();
                searchTimer.Stop(); // Clear() rearms the timer; stop it before it repaints over the status.
                SetView(0);
                ApplyFilter();
                int nUp = installed.Count(delegate (PackageInfo p) { return p.HasUpdate; });
                lblStatus.Text = L.F("status_full", installed.Count, nUp);
                UpdateUpgradeAllButton();
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, L.F("msg_winget_err", ex.Message), L.T("msg_err_t"),
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            finally
            {
                SetBusy(false, null);
            }
        }

        // View filter. Exactly three views, no overlap: Installed, Updates, Search.
        // Typing a query switches to Search automatically; clearing it switches back.

        private static readonly string[] ViewKeys = new string[]
        {
            "view_installed", "view_updates", "view_search"
        };

        private void RebuildViewMenu()
        {
            viewMenu.Items.Clear();
            for (int i = 0; i < ViewKeys.Length; i++)
            {
                int idx = i;
                var mi = new ToolStripMenuItem(L.T(ViewKeys[i]));
                mi.Checked = idx == viewIndex;
                mi.Click += delegate
                {
                    SetView(idx);
                    ApplyFilter();
                };
                viewMenu.Items.Add(mi);
            }
            btnView.Text = L.T(ViewKeys[viewIndex]) + "  ▾";
            Theme.PaintMenu(viewMenu);
        }

        private void SetView(int i)
        {
            viewIndex = i;
            RebuildViewMenu();
        }

        private void ApplyFilter()
        {
            List<PackageInfo> src = searchBase ?? installed;
            int view = viewIndex;
            if (searchBase == null && view == 2)
            {
                // Search without a query has nothing to show.
                RenderList(new List<PackageInfo>());
                lblStatus.Text = L.T("status_type_query");
                return;
            }
            List<PackageInfo> data;
            if (view == 1)
                data = src.Where(delegate (PackageInfo p) { return p.HasUpdate; }).ToList();
            else if (view == 0 && searchBase != null)
                data = src.Where(delegate (PackageInfo p) { return p.IsInstalled; }).ToList();
            else
                data = new List<PackageInfo>(src);
            RenderList(data);
        }

        private static string ActionGlyph(PendingAction a)
        {
            switch (a)
            {
                case PendingAction.Install: return "+";
                case PendingAction.Upgrade: return "↑";
                case PendingAction.Uninstall: return "×";
                default: return "";
            }
        }

        private void RenderList(List<PackageInfo> data)
        {
            syncing = true;
            lv.BeginUpdate();
            lv.Items.Clear();
            foreach (var p in data)
            {
                int img = icons.GetIndex(p);
                var item = new ListViewItem(p.DisplayName, img);
                item.Tag = p;
                item.Checked = false;
                item.UseItemStyleForSubItems = false;
                // MInstall-style single version: upgradable rows show the target version.
                string ver = p.HasUpdate ? p.AvailableVersion
                    : (p.IsInstalled ? p.InstalledVersion : p.AvailableVersion);
                item.SubItems.Add(string.IsNullOrWhiteSpace(ver) ? L.T("desc_na") : ver);
                item.SubItems.Add("");
                // Upgradable rows go bold.
                if (p.HasUpdate)
                    item.Font = new Font(lv.Font, FontStyle.Bold);
                lv.Items.Add(item);
                PaintRow(item); // Single place that owns all row colors.
            }
            lv.EndUpdate();
            syncing = false;
            FitColumns();
            RefreshCounts();
            UpdateDesc();
        }

        private void Lv_ItemChecked(object sender, ItemCheckedEventArgs e)
        {
            if (syncing) return;
            // ItemChecked also fires during rebuilds; ignore those.
            if (e.Item == null || e.Item.Tag == null) return;
            PaintRow(e.Item);
            RefreshCounts();
        }

        private void SetAllChecked(bool check)
        {
            foreach (ListViewItem it in lv.Items)
                it.Checked = check; // Glyphs and counters update through Lv_ItemChecked.
        }

        private List<KeyValuePair<PackageInfo, PendingAction>> CollectChecked()
        {
            var res = new List<KeyValuePair<PackageInfo, PendingAction>>();
            foreach (ListViewItem it in lv.Items)
            {
                if (!it.Checked) continue;
                var p = it.Tag as PackageInfo;
                if (p == null) continue;
                var a = p.ActionFor(true);
                if (a != PendingAction.None)
                    res.Add(new KeyValuePair<PackageInfo, PendingAction>(p, a));
            }
            return res;
        }

        private void RefreshCounts()
        {
            int n = CollectChecked().Count;
            btnGo.Text = n == 0 ? L.T("btn_go") : L.F("btn_go_n", n);
            lblCount.Text = L.F("count_fmt", n, lv.Items.Count);
            syncing = true;
            chkAll.Checked = lv.Items.Count > 0 && n == lv.Items.Count;
            syncing = false;
        }

        // Details pane.

        private readonly Dictionary<string, string> showCache =
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        private int descSeq;

        private string BuildBaseDesc(PackageInfo p)
        {
            var sb = new StringBuilder();
            sb.AppendLine(p.DisplayName);
            sb.Append(L.T("desc_id")).Append(p.Id);
            if (!string.IsNullOrWhiteSpace(p.UpgradeId))
                sb.Append(L.F("desc_upd_via", p.UpgradeId));
            sb.AppendLine();
            sb.Append(L.T("desc_installed")).Append(string.IsNullOrWhiteSpace(p.InstalledVersion) ? L.T("desc_na") : p.InstalledVersion);
            sb.Append(L.T("desc_avail")).Append(string.IsNullOrWhiteSpace(p.AvailableVersion) ? L.T("desc_na") : p.AvailableVersion);
            sb.Append(L.T("desc_source")).Append(string.IsNullOrWhiteSpace(p.Source) ? L.T("desc_na") : p.Source);
            if (p.IsArp && p.IsInstalled)
                sb.Append(L.T("desc_arp"));
            sb.AppendLine();
            return sb.ToString();
        }

        private async void UpdateDesc()
        {
            if (lv.SelectedItems.Count == 0)
            {
                txtDesc.ForeColor = Theme.Current.GrayText;
                txtDesc.Text = L.T("desc_legend");
                return;
            }
            var p = lv.SelectedItems[0].Tag as PackageInfo;
            if (p == null) return;

            string showId = !p.IsInstalled ? p.Id : p.EffectiveUpgradeId;
            if (showId.StartsWith("ARP\\", StringComparison.OrdinalIgnoreCase))
            {
                txtDesc.ForeColor = Theme.Current.Text;
                txtDesc.Text = BuildBaseDesc(p) + L.T("desc_notfound");
                return;
            }

            string cached;
            if (showCache.TryGetValue(showId, out cached))
            {
                txtDesc.ForeColor = Theme.Current.Text;
                txtDesc.Text = BuildBaseDesc(p) + cached;
                return;
            }

            txtDesc.ForeColor = Theme.Current.Text;
            txtDesc.Text = BuildBaseDesc(p) + L.T("desc_loading");

            int my = ++descSeq;
            string key = showId;
            string raw = await WingetRunner.ShowAsync(key).ConfigureAwait(true);
            if (my != descSeq) return; // Stale response; the selection already moved on.
            if (lv.SelectedItems.Count == 0) return;
            var cur = lv.SelectedItems[0].Tag as PackageInfo;
            if (cur == null) return;
            string curId = !cur.IsInstalled ? cur.Id : cur.EffectiveUpgradeId;
            if (!curId.Equals(key, StringComparison.OrdinalIgnoreCase)) return;

            string text;
            if (raw == null)
            {
                text = L.T("desc_no");
            }
            else
            {
                string pub, desc;
                WingetRunner.ParseShow(raw, out pub, out desc);
                var sb = new StringBuilder();
                if (!string.IsNullOrWhiteSpace(pub))
                    sb.Append(L.T("desc_pub")).Append(pub).AppendLine();
                sb.Append(string.IsNullOrWhiteSpace(desc) ? L.T("desc_no") : desc);
                text = sb.ToString();
            }
            showCache[key] = text;
            txtDesc.Text = BuildBaseDesc(cur) + text;
        }

        // Search.

        private async void OnSearchGo()
        {
            if (busy) return;
            string q = txtSearch.Text.Trim();
            if (q.Length == 0)
            {
                searchBase = null;
                if (viewIndex == 2)
                    SetView(0); // Was on Search; return to Installed.
                ApplyFilter();
                lblStatus.Text = L.F("status_short_n", installed.Count);
                return;
            }
            // Short queries filter the installed list only (instant, no round-trip).
            if (q.Length < 2)
            {
                searchBase = installed.Where(delegate (PackageInfo p)
                {
                    return p.DisplayName.IndexOf(q, StringComparison.OrdinalIgnoreCase) >= 0 ||
                           p.Id.IndexOf(q, StringComparison.OrdinalIgnoreCase) >= 0;
                }).ToList();
                ApplyFilter();
                return;
            }

            // Local filter first (instant).
            var local = installed.Where(delegate (PackageInfo p)
            {
                return p.DisplayName.IndexOf(q, StringComparison.OrdinalIgnoreCase) >= 0 ||
                       p.Id.IndexOf(q, StringComparison.OrdinalIgnoreCase) >= 0;
            }).ToList();

            SetBusy(true, L.F("status_searching", q));
            try
            {
                var remote = await WingetRunner.SearchAsync(q).ConfigureAwait(true);
                var knownIds = new HashSet<string>(installed.Select(delegate (PackageInfo p) { return p.Id; }),
                    StringComparer.OrdinalIgnoreCase);
                var knownNames = new HashSet<string>(
                    installed.Where(delegate (PackageInfo p) { return !string.IsNullOrWhiteSpace(p.Name); })
                             .Select(delegate (PackageInfo p) { return p.Name.Trim(); }),
                    StringComparer.OrdinalIgnoreCase);
                // Merge in only what isn't installed yet. Match by id and by name:
                // a side-loaded app (ARP row) would otherwise surface as "new" and
                // `install` would fail with "already installed" while `upgrade` works.
                foreach (var r in remote)
                {
                    if (knownIds.Contains(r.Id)) continue;
                    if (!string.IsNullOrWhiteSpace(r.Name) && knownNames.Contains(r.Name.Trim())) continue;
                    local.Add(r);
                }
                searchBase = local
                    .OrderByDescending(delegate (PackageInfo p) { return p.HasUpdate; })
                    .ThenByDescending(delegate (PackageInfo p) { return p.IsInstalled; })
                    .ThenBy(delegate (PackageInfo p) { return p.DisplayName; })
                    .ToList();
                SetView(2); // A query is active; switch to the Search view.
                ApplyFilter();
                lblStatus.Text = L.F("status_found", q, local.Count,
                    local.Count(delegate (PackageInfo p) { return p.IsInstalled; }));
            }
            catch (Exception ex)
            {
                lblStatus.Text = L.F("status_search_err", ex.Message);
            }
            finally
            {
                SetBusy(false, null);
            }
        }

        private bool HasAnyUpdate()
        {
            foreach (var p in installed)
                if (p.HasUpdate) return true;
            return false;
        }

        /// <summary>
        /// Did winget refuse the upgrade because the installer technology differs
        /// from the installed one (exit code 0x8A15008E)? Falls back to message
        /// matching in RU/EN when the code alone is inconclusive.
        /// </summary>
        private static bool IsTechnologyDiffers(WingetResult r)
        {
            if (r.ExitCode == unchecked((int)0x8A15008E)) return true;
            string all = r.StdOut + "\n" + r.StdErr;
            return all.IndexOf("технолог", StringComparison.OrdinalIgnoreCase) >= 0
                || all.IndexOf("technology differs", StringComparison.OrdinalIgnoreCase) >= 0
                || all.IndexOf("installer technology", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        /// <summary>
        /// Reinstall path for the changed-installer-technology case:
        /// uninstall first, then install the fresh version.
        /// Returns 1 on success, 0 on failure, -1 when the user declines.
        /// </summary>
        private async Task<int> ReinstallAsync(PackageInfo pkg, StringBuilder log)
        {
            var yn = MessageBox.Show(this,
                L.F("msg_reinstall", pkg.DisplayName),
                L.T("msg_reinstall_t"), MessageBoxButtons.YesNo, MessageBoxIcon.Question);
            if (yn != DialogResult.Yes)
            {
                log.AppendLine(L.F("log_skip", pkg.DisplayName));
                log.AppendLine();
                return -1;
            }

            lblStatus.Text = L.F("status_removing", pkg.DisplayName);
            var u = await WingetRunner.RunAsync(
                WingetRunner.BuildArgs("uninstall", pkg),
                WingetRunner.OperationTimeoutMs).ConfigureAwait(true);
            if (u.ExitCode != 0 && !string.IsNullOrWhiteSpace(pkg.UpgradeId))
            {
                // The list id didn't uninstall; retry with the winget id.
                var alt = new PackageInfo();
                alt.Id = pkg.UpgradeId;
                alt.Name = pkg.Name;
                u = await WingetRunner.RunAsync(
                    WingetRunner.BuildArgs("uninstall", alt),
                    WingetRunner.OperationTimeoutMs).ConfigureAwait(true);
            }
            if (u.ExitCode != 0)
            {
                log.AppendLine(L.F("log_uninst_fail", pkg.DisplayName, u.ExitCode));
                string tail = (u.StdOut + "\n" + u.StdErr).Trim();
                if (tail.Length > WingetRunner.MaxOutputTail) tail = tail.Substring(tail.Length - WingetRunner.MaxOutputTail);
                if (!string.IsNullOrWhiteSpace(tail)) log.AppendLine(tail);
                log.AppendLine();
                return 0;
            }

            lblStatus.Text = L.F("status_installing", pkg.DisplayName);
            var ins = new PackageInfo();
            ins.Id = pkg.EffectiveUpgradeId;
            ins.Name = pkg.Name;
            var ir = await WingetRunner.RunAsync(
                WingetRunner.BuildArgs("install", ins),
                WingetRunner.OperationTimeoutMs).ConfigureAwait(true);
            bool good = ir.ExitCode == 0;
            log.AppendLine(L.F("log_reinstall", good ? "OK" : "FAIL", pkg.DisplayName, ir.ExitCode));
            if (!good)
            {
                string tail2 = (ir.StdOut + "\n" + ir.StdErr).Trim();
                if (tail2.Length > WingetRunner.MaxOutputTail) tail2 = tail2.Substring(tail2.Length - WingetRunner.MaxOutputTail);
                if (!string.IsNullOrWhiteSpace(tail2)) log.AppendLine(tail2);
            }
            log.AppendLine();
            return good ? 1 : 0;
        }

        // Apply.

        private async void ApplyChecked()
        {
            if (busy) return;
            var jobs = CollectChecked();
            if (jobs.Count == 0)
            {
                MessageBox.Show(this, L.T("msg_nothing"), L.T("msg_nothing_t"),
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            int nDel = jobs.Count(delegate (KeyValuePair<PackageInfo, PendingAction> j) { return j.Value == PendingAction.Uninstall; });
            if (nDel > 0)
            {
                var dr = MessageBox.Show(this,
                    L.F("msg_del", nDel),
                    L.T("msg_del_t"), MessageBoxButtons.YesNo, MessageBoxIcon.Warning);
                if (dr != DialogResult.Yes) return;
            }

            SetBusy(true, L.T("status_working"));
            var log = new StringBuilder();
            int ok = 0, fail = 0;
            try
            {
                foreach (var job in jobs)
                {
                    var pkg = job.Key;
                    string verb = job.Value == PendingAction.Install ? "install" :
                                  job.Value == PendingAction.Upgrade ? "upgrade" : "uninstall";
                    lblStatus.Text = string.Format("{0}: {1}…", PackageInfo.ActionText(job.Value), pkg.DisplayName);
                    string args = WingetRunner.BuildArgs(verb, pkg);
                    var r = await WingetRunner.RunAsync(args, WingetRunner.OperationTimeoutMs).ConfigureAwait(true);
                    bool good = r.ExitCode == 0;
                    if (!good && job.Value == PendingAction.Upgrade && IsTechnologyDiffers(r))
                    {
                        // winget refused a direct upgrade (installer technology differs);
                        // uninstall + fresh install is the only way forward.
                        int rc = await ReinstallAsync(pkg, log).ConfigureAwait(true);
                        if (rc > 0) ok++;
                        else if (rc == 0) fail++;
                        continue;
                    }
                    if (good) ok++; else fail++;
                    log.AppendLine(L.F("log_row", good ? "OK" : "FAIL", pkg.DisplayName, pkg.Id, r.ExitCode));
                    if (!good)
                    {
                        // Include the exact command and the output tail for diagnosability.
                        log.AppendLine(L.F("log_cmd", args));
                        string tail = (r.StdOut + "\n" + r.StdErr).Trim();
                        if (tail.Length > WingetRunner.MaxOutputTail) tail = tail.Substring(tail.Length - WingetRunner.MaxOutputTail);
                        if (!string.IsNullOrWhiteSpace(tail)) log.AppendLine(tail);
                    }
                    log.AppendLine();
                }

                MessageBox.Show(this, L.F("log_done", ok, fail, log),
                    "winstall", MessageBoxButtons.OK,
                    fail == 0 ? MessageBoxIcon.Information : MessageBoxIcon.Warning);
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, L.F("msg_err", ex.Message), L.T("msg_err_t"),
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            finally
            {
                SetBusy(false, L.T("status_done"));
                RefreshAll();
            }
        }

        // Upgrade all.

        private async void UpgradeAll()
        {
            if (busy) return;
            int n = 0;
            foreach (var p in installed)
                if (p.HasUpdate) n++;
            if (n == 0)
            {
                MessageBox.Show(this, L.T("msg_no_updates"), L.T("msg_nothing_t"),
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            var dr = MessageBox.Show(this,
                L.F("msg_upd_all_q", n),
                L.T("msg_upd_all_t"), MessageBoxButtons.YesNo, MessageBoxIcon.Question);
            if (dr != DialogResult.Yes) return;

            SetBusy(true, L.T("status_working"));
            try
            {
                // One call upgrades everything; cheaper than per-package runs.
                string args = WingetRunner.BuildUpgradeAllArgs();
                var r = await WingetRunner.RunAsync(args, WingetRunner.BulkTimeoutMs).ConfigureAwait(true);
                bool good = r.ExitCode == 0;
                string tail = (r.StdOut + "\n" + r.StdErr).Trim();
                if (tail.Length > WingetRunner.MaxBulkOutputTail) tail = tail.Substring(tail.Length - WingetRunner.MaxBulkOutputTail);
                MessageBox.Show(this,
                    (good ? L.T("msg_upd_all_done") : L.F("msg_upd_all_code", r.ExitCode)) + "\n\n" + tail,
                    L.T("msg_upd_all_t"),
                    MessageBoxButtons.OK,
                    good ? MessageBoxIcon.Information : MessageBoxIcon.Warning);
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, L.F("msg_err", ex.Message), L.T("msg_err_t"),
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            finally
            {
                SetBusy(false, L.T("status_done"));
                RefreshAll();
            }
        }

        // Package list import/export.

        private async void ExportList()
        {
            if (busy) return;
            using (var dlg = new SaveFileDialog())
            {
                dlg.Filter = L.T("dlg_filter");
                dlg.FileName = L.T("dlg_export_name");
                if (dlg.ShowDialog(this) != DialogResult.OK) return;
                SetBusy(true, L.T("status_exporting"));
                try
                {
                    var r = await WingetRunner.RunAsync(
                        "export -o \"" + dlg.FileName.Replace("\"", "") + "\" --include-versions --accept-source-agreements --disable-interactivity",
                        WingetRunner.DefaultTimeoutMs).ConfigureAwait(true);
                    if (r.ExitCode == 0)
                        lblStatus.Text = L.F("status_exported", dlg.FileName);                    else
                        MessageBox.Show(this,
                            string.Format(L.T("msg_export_fail"), r.ExitCode, r.StdOut + r.StdErr),
                            L.T("msg_export_t"), MessageBoxButtons.OK, MessageBoxIcon.Warning);
                }
                catch (Exception ex)
                {
                    MessageBox.Show(this, L.F("msg_err", ex.Message), L.T("msg_err_t"),
                        MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
                finally { SetBusy(false, null); }
            }
        }

        private async void ImportList()
        {
            if (busy) return;
            using (var dlg = new OpenFileDialog())
            {
                dlg.Filter = L.T("dlg_filter");
                if (dlg.ShowDialog(this) != DialogResult.OK) return;
                var dr = MessageBox.Show(this,
                    L.F("msg_import_q", dlg.FileName),
                    L.T("msg_import_t"), MessageBoxButtons.YesNo, MessageBoxIcon.Question);
                if (dr != DialogResult.Yes) return;
                SetBusy(true, L.T("status_importing"));
                try
                {
                    var r = await WingetRunner.RunAsync(
                        "import -i \"" + dlg.FileName.Replace("\"", "") + "\" --ignore-unavailable --accept-package-agreements --accept-source-agreements --disable-interactivity",
                        WingetRunner.BulkTimeoutMs).ConfigureAwait(true);
                    string tail = (r.StdOut + "\n" + r.StdErr).Trim();
                    if (tail.Length > WingetRunner.MaxBulkOutputTail) tail = tail.Substring(tail.Length - WingetRunner.MaxBulkOutputTail);
                    MessageBox.Show(this,
                        (r.ExitCode == 0 ? L.T("msg_upd_all_done") : L.F("msg_upd_all_code", r.ExitCode)) + "\n\n" + tail,
                        L.T("msg_import_t"), MessageBoxButtons.OK,
                        r.ExitCode == 0 ? MessageBoxIcon.Information : MessageBoxIcon.Warning);
                }
                catch (Exception ex)
                {
                    MessageBox.Show(this, L.F("msg_err", ex.Message), L.T("msg_err_t"),
                        MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
                finally
                {
                    SetBusy(false, L.T("status_done"));
                    RefreshAll();
                }
            }
        }

        private void SelectUpgradable()
        {
            // Check upgradable rows only; handy before Run.
            lv.ItemChecked -= Lv_ItemChecked;
            try
            {
                foreach (ListViewItem it in lv.Items)
                {
                    var p = it.Tag as PackageInfo;
                    if (p == null) continue;
                    it.Checked = p.IsInstalled && p.HasUpdate;
                    PaintRow(it);
                }
            }
            finally { lv.ItemChecked += Lv_ItemChecked; }
            RefreshCounts();
        }

        private void UncheckAll()
        {
            lv.ItemChecked -= Lv_ItemChecked;
            try
            {
                foreach (ListViewItem it in lv.Items)
                {
                    it.Checked = false;
                    PaintRow(it);
                }
            }
            finally { lv.ItemChecked += Lv_ItemChecked; }
            RefreshCounts();
        }

        private void ToggleAutoCheck()
        {
            if (busy) return;
            if (autoCheckEnabled)
            {
                string err = AutoCheck.Disable();
                if (err == null)
                {
                    autoCheckEnabled = false;
                    MessageBox.Show(this, L.T("msg_auto_off"), L.T("msg_auto_off_t"),
                        MessageBoxButtons.OK, MessageBoxIcon.Information);
                }
                else
                    MessageBox.Show(this, L.F("msg_auto_err", err), L.T("msg_auto_err_t"),
                        MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
            else
            {
                string err = AutoCheck.Enable();
                if (err == null)
                {
                    autoCheckEnabled = true;
                    MessageBox.Show(this, L.T("msg_auto_on"), L.T("msg_auto_on_t"),
                        MessageBoxButtons.OK, MessageBoxIcon.Information);
                }
                else
                    MessageBox.Show(this, L.F("msg_auto_err", err), L.T("msg_auto_err_t"),
                        MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }

        private void ChooseLogsDir()
        {
            if (busy) return;
            using (var dlg = new FolderBrowserDialog())
            {
                dlg.Description = L.T("dlg_logsdir");
                dlg.ShowNewFolderButton = true;
                try
                {
                    dlg.SelectedPath = string.IsNullOrWhiteSpace(Options.LogsDir)
                        ? Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments)
                        : Options.LogsDir;
                }
                catch { }
                if (dlg.ShowDialog(this) != DialogResult.OK) return;
                try { System.IO.Directory.CreateDirectory(dlg.SelectedPath); }
                catch (Exception ex)
                {
                    MessageBox.Show(this, L.F("msg_err", ex.Message), L.T("msg_err_t"),
                        MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }
                Options.LogsDir = dlg.SelectedPath;
                Options.Save();
                lblStatus.Text = L.F("status_logsdir", dlg.SelectedPath);
            }
        }

        private void OpenWingetLogs()
        {
            try
            {
                if (!string.IsNullOrWhiteSpace(Options.LogsDir) &&
                    System.IO.Directory.Exists(Options.LogsDir))
                {
                    System.Diagnostics.Process.Start(Options.LogsDir);
                    return;
                }
                string dir = System.IO.Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    @"Packages\Microsoft.DesktopAppInstaller_8wekyb3d8bbwe\LocalState\DiagOutputDir");
                if (System.IO.Directory.Exists(dir))
                    System.Diagnostics.Process.Start(dir);
                else
                    System.Diagnostics.Process.Start("winget", "--open-logs");
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, L.F("msg_logs_err", ex.Message),
                    L.T("msg_logs_t"), MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }

        private void ShowAbout()
        {
            MessageBox.Show(this, L.T("msg_about"), L.T("msg_about_t"),
                MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
    }
}
