namespace winstall
{
    /// <summary>
    /// Что сделает галочка в стиле MInstall.
    /// </summary>
    public enum PendingAction
    {
        None,
        Install,   // пакет не установлен -> установить
        Upgrade,   // установлен и есть обновление -> обновить
        Uninstall  // установлен и свежий -> удалить
    }

    public class PackageInfo
    {
        public string Id = "";
        public string Name = "";
        public string InstalledVersion = "";
        public string AvailableVersion = "";
        public string Source = "";
        public bool IsInstalled;
        public bool HasUpdate;
        /// <summary>
        /// Настоящий winget-Id для обновления. Заполняется, когда программа
        /// ставилась мимо winget (в list видна как ARP\... с пустым Source),
        /// а в `winget upgrade` она же фигурирует под нормальным Id
        /// (например Valve.Steam). Тогда обновляем именно по нему.
        /// </summary>
        public string UpgradeId = "";

        /// <summary>ARP-запись: установлено не через winget.</summary>
        public bool IsArp
        {
            get
            {
                return string.IsNullOrWhiteSpace(Source) ||
                    Id.StartsWith("ARP\\", System.StringComparison.OrdinalIgnoreCase);
            }
        }

        /// <summary>Id, который надо передавать в `winget upgrade --id`.</summary>
        public string EffectiveUpgradeId
        {
            get { return !string.IsNullOrWhiteSpace(UpgradeId) ? UpgradeId : Id; }
        }

        /// <summary>
        /// "Нативное" отображаемое имя: колонка Name из winget,
        /// а не голый Id вида Valve.Steam.
        /// </summary>
        public string DisplayName
        {
            get
            {
                if (!string.IsNullOrWhiteSpace(Name))
                    return Name.Trim();
                return PrettyId(Id);
            }
        }

        public PendingAction ActionFor(bool isChecked)
        {
            if (!isChecked) return PendingAction.None;
            if (!IsInstalled) return PendingAction.Install;
            if (HasUpdate) return PendingAction.Upgrade;
            return PendingAction.Uninstall;
        }

        public static string ActionText(PendingAction a)
        {
            switch (a)
            {
                case PendingAction.Install: return L.T("act_install");
                case PendingAction.Upgrade: return L.T("act_upgrade");
                case PendingAction.Uninstall: return L.T("act_uninstall");
                default: return L.T("act_none");
            }
        }

        public static string PrettyId(string id)
        {
            if (string.IsNullOrWhiteSpace(id)) return "?";
            // ARP\Machine\X64\Excel2024Retail -> Excel2024Retail
            string s = id.Replace('\\', '.').Replace('/', '.');
            string[] parts = s.Split('.');
            string last = parts[parts.Length - 1];
            return last;
        }
    }
}
