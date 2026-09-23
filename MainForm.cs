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
    /// Интерфейс в духе MInstall: сверху выпадашка категорий + «Выполнить»,
    /// один список «галочка — иконка — имя — версия — значок действия»,
    /// внизу поиск + счётчик + «Выбрать все / Ничего / Обновляемые»
    /// и панель описания выбранной программы.
    /// Язык интерфейса — из файлов locales/*.json рядом с программой,
    /// выбор через меню ☰. Без файлов работает встроенный английский.
    /// </summary>
    public class MainForm : Form
    {
        // Cue-banner (серый hint) для поля поиска на Win32
        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern int SendMessage(IntPtr hWnd, int msg, int wParam, string lParam);
        private const int EM_SETCUEBANNER = 0x1501;

        private ComboBox cmbView;
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

        // Полные данные
        private List<PackageInfo> installed = new List<PackageInfo>();
        private List<PackageInfo> searchBase; // null = поиска нет, показываем установленные
        private List<PackageInfo> shown = new List<PackageInfo>();
        private bool busy;
        private bool syncing;
        private bool changingView;
        private bool autoCheckEnabled;

        public MainForm()
        {
            L.Startup();
            Text = L.T("app_title");
            try { Icon = System.Drawing.Icon.ExtractAssociatedIcon(Application.ExecutablePath); }
            catch { }
            StartPosition = FormStartPosition.CenterScreen;
            Size = new Size(900, 620);
            MinimumSize = new Size(700, 480);
            Font = new Font("Segoe UI", 9f);

            icons = new IconProvider();
            BuildUi();
            // Состояние автопроверки читаем в фоне, чтобы не тормозить старт.
            Task.Run(delegate { autoCheckEnabled = AutoCheck.IsEnabled(); });

            searchTimer = new Timer();
            searchTimer.Interval = 500;
            searchTimer.Tick += delegate { searchTimer.Stop(); OnSearchGo(); };

            Shown += delegate { RefreshAll(); };
            FormClosed += delegate { if (icons != null) icons.Dispose(); };
        }

        private void BuildUi()
        {
            // --- Верх: выпадашка категорий + Выполнить (как в MInstall) ---
            var top = new Panel();
            top.Dock = DockStyle.Top;
            top.Height = 34;
            top.Padding = new Padding(6, 6, 4, 2);
            Controls.Add(top);

            cmbView = new ComboBox();
            cmbView.DropDownStyle = ComboBoxStyle.DropDownList;
            cmbView.Dock = DockStyle.Fill;
            cmbView.Items.AddRange(new object[] {
                L.T("view_installed"),
                L.T("view_updates"),
                L.T("view_search")
            });
            cmbView.SelectedIndex = 0;
            cmbView.SelectedIndexChanged += delegate { if (!changingView) ApplyFilter(); };
            top.Controls.Add(cmbView);

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
            // Перед показом пересканируем папку locales — вдруг файл только что положили.
            hamburger.Opening += delegate { L.Rescan(); BuildHamburger(); };
            // --- Низ: строка поиска + счётчик + массовые кнопки ---
            var bottom = new Panel();
            bottom.Dock = DockStyle.Bottom;
            bottom.Height = 118;
            Controls.Add(bottom);

            var searchRow = new Panel();
            searchRow.Dock = DockStyle.Top;
            searchRow.Height = 30;
            searchRow.Padding = new Padding(6, 4, 4, 2);
            // НЕ добавляем searchRow сейчас: в WinForms последний добавленный
            // докируется первым, поэтому Fill (описание) добавляем раньше,
            // а Top (строка поиска) — позже. См. bottom.Controls.Add ниже.

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
            searchRow.Controls.Add(lblCount);

            txtSearch = new TextBox();
            txtSearch.Dock = DockStyle.Fill;
            txtSearch.TextChanged += delegate
            {
                searchTimer.Stop();
                searchTimer.Start();
            };
            searchRow.Controls.Add(txtSearch);
            // Fill делим последним: иначе поле растянется на всю ширину
            // под кнопками и текст уползёт.
            searchRow.Controls.SetChildIndex(txtSearch, 0);
            SendMessage(txtSearch.Handle, EM_SETCUEBANNER, 0, L.T("search_cue"));

            // --- Панель описания выбранной программы (как в MInstall) ---
            txtDesc = new TextBox();
            txtDesc.Dock = DockStyle.Fill;
            txtDesc.Multiline = true;
            txtDesc.ReadOnly = true;
            txtDesc.ScrollBars = ScrollBars.Vertical;
            txtDesc.BackColor = SystemColors.Window;
            txtDesc.ForeColor = SystemColors.GrayText;
            txtDesc.Text = L.T("desc_legend");
            bottom.Controls.Add(txtDesc);
            // Строку поиска добавляем ПОСЛЕ описания: последний добавленный
            // докируется первым, Top должен делиться раньше Fill.
            bottom.Controls.Add(searchRow);

            // --- Список: один, как в MInstall ---
            lv = new ListView();
            lv.Dock = DockStyle.Fill;
            lv.View = View.Details;
            lv.CheckBoxes = true;
            lv.FullRowSelect = true;
            lv.GridLines = false;
            lv.MultiSelect = false;
            lv.HideSelection = false;
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
        }

        private void FitColumns()
        {
            if (lv == null || colName == null) return;
            int w = lv.ClientSize.Width - colVer.Width - colAct.Width
                - SystemInformation.VerticalScrollBarWidth - 30;
            if (w < 150) w = 150;
            colName.Width = w;
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
            hamburger.Items.Add(L.T("menu_about"), null, delegate { ShowAbout(); });
            hamburger.Items.Add(new ToolStripSeparator());
            hamburger.Items.Add(L.T("menu_exit"), null, delegate { Close(); });
        }

        /// <summary>Применить текущий язык ко всем надписям без потери галочек.</summary>
        private void ApplyLanguage()
        {
            var keep = new HashSet<PackageInfo>();
            foreach (var kv in CollectChecked()) keep.Add(kv.Key);

            changingView = true;
            int idx = cmbView.SelectedIndex;
            cmbView.Items.Clear();
            cmbView.Items.AddRange(new object[] {
                L.T("view_installed"),
                L.T("view_updates"),
                L.T("view_search")
            });
            cmbView.SelectedIndex = idx < 0 || idx > 2 ? 0 : idx;
            changingView = false;

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
            ApplyFilter(); // перерисует (галочки слетят) — вернём ниже
            lv.ItemChecked -= Lv_ItemChecked;
            try
            {
                foreach (ListViewItem it in lv.Items)
                {
                    var p = it.Tag as PackageInfo;
                    if (p == null) continue;
                    bool c = keep.Contains(p);
                    it.Checked = c;
                    if (it.SubItems.Count > 2)
                    {
                        it.SubItems[2].Text = ActionGlyph(p.ActionFor(c));
                        it.SubItems[2].ForeColor = c ? Color.ForestGreen : SystemColors.GrayText;
                    }
                    if (it.SubItems.Count > 1)
                        it.SubItems[1].ForeColor = c ? Color.ForestGreen : Color.MidnightBlue;
                    it.ForeColor = c ? Color.ForestGreen
                        : (p.IsInstalled ? lv.ForeColor : Color.DimGray);
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
            cmbView.Enabled = !b;
            // Поле поиска не блокируем — текст можно править даже во время операции.
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

        // ================= Загрузка =================

        private async void RefreshAll()
        {
            if (busy) return;
            string exe = WingetRunner.FindWinget();
            if (exe == null)
            {
                MessageBox.Show(this, L.T("msg_no_winget"), L.T("msg_no_winget_t"),
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
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
                    // 1) Точное совпадение по Id (нормальный случай: ставили через winget).
                    upById.TryGetValue(p.Id, out hit);
                    // 2) Программа ставилась мимо winget (ARP-запись без Source):
                    //    связываем по точному имени, обновлять будем по UpgradeId.
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
                        // Новый формат `winget list` сам сообщает доступную версию
                        // в 4-й колонке — верим ему, даже если `winget upgrade`
                        // эту строку по какой-то причине не вернул.
                        p.HasUpdate = true;
                    }
                }
                // Обновляемые — наверх, как в MInstall избранное
                installed = installed
                    .OrderByDescending(delegate (PackageInfo p) { return p.HasUpdate; })
                    .ThenBy(delegate (PackageInfo p) { return p.DisplayName; })
                    .ToList();

                searchBase = null;
                txtSearch.Clear();
                searchTimer.Stop(); // Clear() взводит таймер — гасим, иначе через 0.5с перерисует и затрёт статус
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

        // ================= Фильтр категорий =================
        // Категорий всего три, без дублей: Установленные, Обновления, Поиск.
        // Поиск включается сам, когда печатаешь запрос; очистил — возврат.

        private void SetView(int i)
        {
            changingView = true;
            cmbView.SelectedIndex = i;
            changingView = false;
        }

        private void ApplyFilter()
        {
            List<PackageInfo> src = searchBase ?? installed;
            int view = cmbView.SelectedIndex;
            if (searchBase == null && view == 2)
            {
                // «Поиск» без запроса показывать нечего.
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
            shown = data;
            foreach (var p in data)
            {
                int img = icons.GetIndex(p);
                var item = new ListViewItem(p.DisplayName, img);
                item.Tag = p;
                item.Checked = false;
                item.UseItemStyleForSubItems = false;
                // В духе MInstall — одна версия: для обновляемого показываем целевую.
                string ver = p.HasUpdate ? p.AvailableVersion
                    : (p.IsInstalled ? p.InstalledVersion : p.AvailableVersion);
                var subVer = new ListViewItem.ListViewSubItem(item,
                    string.IsNullOrWhiteSpace(ver) ? L.T("desc_na") : ver);
                subVer.ForeColor = Color.MidnightBlue;
                item.SubItems.Add(subVer);
                var subAct = new ListViewItem.ListViewSubItem(item, p.HasUpdate ? "↑" : "");
                subAct.ForeColor = SystemColors.GrayText;
                item.SubItems.Add(subAct);
                // Обновляемые — жирным наверх, как было
                if (p.HasUpdate)
                    item.Font = new Font(lv.Font, FontStyle.Bold);
                // Найденное, но не установленное — серым
                if (!p.IsInstalled)
                    item.ForeColor = Color.DimGray;
                lv.Items.Add(item);
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
            // Событие приходит и при перерисовке — защищаемся
            if (e.Item == null || e.Item.Tag == null) return;
            var p = (PackageInfo)e.Item.Tag;
            var act = p.ActionFor(e.Item.Checked);
            if (e.Item.SubItems.Count > 2)
            {
                e.Item.SubItems[2].Text = ActionGlyph(act);
                e.Item.SubItems[2].ForeColor = e.Item.Checked ? Color.ForestGreen : SystemColors.GrayText;
            }
            if (e.Item.SubItems.Count > 1)
                e.Item.SubItems[1].ForeColor = e.Item.Checked ? Color.ForestGreen : Color.MidnightBlue;
            if (e.Item.Checked)
                e.Item.ForeColor = Color.ForestGreen;
            else if (p.IsInstalled)
                e.Item.ForeColor = lv.ForeColor;
            else
                e.Item.ForeColor = Color.DimGray;
            RefreshCounts();
        }

        private void SetAllChecked(bool check)
        {
            foreach (ListViewItem it in lv.Items)
                it.Checked = check; // через Lv_ItemChecked обновит значки и счётчик
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

        // ================= Описание выбранной =================

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
                txtDesc.ForeColor = SystemColors.GrayText;
                txtDesc.Text = L.T("desc_legend");
                return;
            }
            var p = lv.SelectedItems[0].Tag as PackageInfo;
            if (p == null) return;

            string showId = !p.IsInstalled ? p.Id : p.EffectiveUpgradeId;
            if (showId.StartsWith("ARP\\", StringComparison.OrdinalIgnoreCase))
            {
                txtDesc.ForeColor = SystemColors.WindowText;
                txtDesc.Text = BuildBaseDesc(p) + L.T("desc_notfound");
                return;
            }

            string cached;
            if (showCache.TryGetValue(showId, out cached))
            {
                txtDesc.ForeColor = SystemColors.WindowText;
                txtDesc.Text = BuildBaseDesc(p) + cached;
                return;
            }

            txtDesc.ForeColor = SystemColors.WindowText;
            txtDesc.Text = BuildBaseDesc(p) + L.T("desc_loading");

            int my = ++descSeq;
            string key = showId;
            string raw = await WingetRunner.ShowAsync(key).ConfigureAwait(true);
            if (my != descSeq) return; // выбор уже сменился — чужое не показываем
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

        // ================= Поиск =================

        private async void OnSearchGo()
        {
            if (busy) return;
            string q = txtSearch.Text.Trim();
            if (q.Length == 0)
            {
                searchBase = null;
                if (cmbView.SelectedIndex == 2)
                    SetView(0); // был «Поиск» — возвращаем «Установленные»
                ApplyFilter();
                lblStatus.Text = L.F("status_short_n", installed.Count);
                return;
            }
            // Короткие запросы — только фильтр по установленным (мгновенно)
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

            // Сначала локальный фильтр (мгновенно)
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
                // Добавляем только то, чего ещё нет в установленных.
                // Сравнение и по Id, и по имени: иначе программа, поставленная
                // мимо winget (в списке как ARP\...), покажется в поиске как
                // «новая», а `install` по ней упрётся в «уже установлено,
                // нужна переустановка», хотя консольный `upgrade` работает.
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
                SetView(2); // запрос есть — переключаемся в категорию «Поиск»
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
        /// winget отказался от upgrade, потому что технология установщика
        /// отличается от установленной (код 0x8A15008E, у LLVM было именно это)?
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
        /// Переустановка для случая «технология установщика изменилась»:
        /// сначала uninstall, потом install свежей версии.
        /// Возвращает 1 = получилось, 0 = не вышло, -1 = пользователь отказался.
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
            var u = await WingetRunner.RunAsync(WingetRunner.BuildArgs("uninstall", pkg), 600000).ConfigureAwait(true);
            if (u.ExitCode != 0 && !string.IsNullOrWhiteSpace(pkg.UpgradeId))
            {
                // Не удалилось по Id из списка — пробуем по winget-Id.
                var alt = new PackageInfo();
                alt.Id = pkg.UpgradeId;
                alt.Name = pkg.Name;
                u = await WingetRunner.RunAsync(WingetRunner.BuildArgs("uninstall", alt), 600000).ConfigureAwait(true);
            }
            if (u.ExitCode != 0)
            {
                log.AppendLine(L.F("log_uninst_fail", pkg.DisplayName, u.ExitCode));
                string tail = (u.StdOut + "\n" + u.StdErr).Trim();
                if (tail.Length > 1500) tail = tail.Substring(tail.Length - 1500);
                if (!string.IsNullOrWhiteSpace(tail)) log.AppendLine(tail);
                log.AppendLine();
                return 0;
            }

            lblStatus.Text = L.F("status_installing", pkg.DisplayName);
            var ins = new PackageInfo();
            ins.Id = pkg.EffectiveUpgradeId;
            ins.Name = pkg.Name;
            var ir = await WingetRunner.RunAsync(WingetRunner.BuildArgs("install", ins), 600000).ConfigureAwait(true);
            bool good = ir.ExitCode == 0;
            log.AppendLine(L.F("log_reinstall", good ? "OK" : "FAIL", pkg.DisplayName, ir.ExitCode));
            if (!good)
            {
                string tail2 = (ir.StdOut + "\n" + ir.StdErr).Trim();
                if (tail2.Length > 1500) tail2 = tail2.Substring(tail2.Length - 1500);
                if (!string.IsNullOrWhiteSpace(tail2)) log.AppendLine(tail2);
            }
            log.AppendLine();
            return good ? 1 : 0;
        }

        // ================= Применение галочек =================

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
                    var r = await WingetRunner.RunAsync(args, 600000).ConfigureAwait(true);
                    bool good = r.ExitCode == 0;
                    if (!good && job.Value == PendingAction.Upgrade && IsTechnologyDiffers(r))
                    {
                        // winget сам отказался обновлять: «технология установки
                        // отличается от текущей». Лечится только сносом + установкой заново.
                        int rc = await ReinstallAsync(pkg, log).ConfigureAwait(true);
                        if (rc > 0) ok++;
                        else if (rc == 0) fail++;
                        continue;
                    }
                    if (good) ok++; else fail++;
                    log.AppendLine(L.F("log_row", good ? "OK" : "FAIL", pkg.DisplayName, pkg.Id, r.ExitCode));
                    if (!good)
                    {
                        // Полный хвост вывода + точная команда, чтобы было видно,
                        // почему консольный winget срабатывает, а тут нет.
                        log.AppendLine(L.F("log_cmd", args));
                        string tail = (r.StdOut + "\n" + r.StdErr).Trim();
                        if (tail.Length > 1500) tail = tail.Substring(tail.Length - 1500);
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

        // ================= Обновить всё =================

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
                // Один вызов на всё — быстрее и проще, чем по одному.
                var r = await WingetRunner.RunAsync(
                    "upgrade --all --silent --accept-package-agreements --accept-source-agreements --disable-interactivity",
                    7200000).ConfigureAwait(true);
                bool good = r.ExitCode == 0;
                string tail = (r.StdOut + "\n" + r.StdErr).Trim();
                if (tail.Length > 2000) tail = tail.Substring(tail.Length - 2000);
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

        // ================= Hamburger: export / import + моё =================

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
                        120000).ConfigureAwait(true);
                    if (r.ExitCode == 0)
                        lblStatus.Text = L.F("status_exported", dlg.FileName);
                    else
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
                        7200000).ConfigureAwait(true);
                    string tail = (r.StdOut + "\n" + r.StdErr).Trim();
                    if (tail.Length > 2000) tail = tail.Substring(tail.Length - 2000);
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
            // Ставим галочки только обновляемым — удобно перед «Выполнить».
            lv.ItemChecked -= Lv_ItemChecked;
            try
            {
                foreach (ListViewItem it in lv.Items)
                {
                    var p = it.Tag as PackageInfo;
                    if (p == null) continue;
                    it.Checked = p.IsInstalled && p.HasUpdate;
                    if (it.SubItems.Count > 2)
                    {
                        it.SubItems[2].Text = ActionGlyph(p.ActionFor(it.Checked));
                        it.SubItems[2].ForeColor = it.Checked ? Color.ForestGreen : SystemColors.GrayText;
                    }
                    if (it.SubItems.Count > 1)
                        it.SubItems[1].ForeColor = it.Checked ? Color.ForestGreen : Color.MidnightBlue;
                    it.ForeColor = it.Checked ? Color.ForestGreen
                        : (p.IsInstalled ? lv.ForeColor : Color.DimGray);
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
                    var p = it.Tag as PackageInfo;
                    if (it.SubItems.Count > 2)
                    {
                        it.SubItems[2].Text = (p != null && p.HasUpdate) ? "↑" : "";
                        it.SubItems[2].ForeColor = SystemColors.GrayText;
                    }
                    if (it.SubItems.Count > 1)
                        it.SubItems[1].ForeColor = Color.MidnightBlue;
                    if (p != null)
                        it.ForeColor = p.IsInstalled ? lv.ForeColor : Color.DimGray;
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

        private void OpenWingetLogs()
        {            try
            {
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
