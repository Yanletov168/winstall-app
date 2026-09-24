namespace winstall
{
    /// <summary>
    /// What checking the box does, MInstall-style.
    /// </summary>
    public enum PendingAction
    {
        None,
        Install,   // Not installed -> install it.
        Upgrade,   // Installed with an update -> upgrade it.
        Uninstall  // Installed and current -> remove it.
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
        /// Real winget id for upgrades. Set when the app was side-loaded
        /// (shows as ARP\... with an empty source in list) while
        /// `winget upgrade` knows it under its regular id (e.g. Valve.Steam).
        /// </summary>
        public string UpgradeId = "";

        /// <summary>ARP entry: installed outside winget.</summary>
        public bool IsArp
        {
            get
            {
                return string.IsNullOrWhiteSpace(Source) ||
                    Id.StartsWith("ARP\\", System.StringComparison.OrdinalIgnoreCase);
            }
        }

        /// <summary>Id to pass to winget upgrade --id.</summary>
        public string EffectiveUpgradeId
        {
            get { return !string.IsNullOrWhiteSpace(UpgradeId) ? UpgradeId : Id; }
        }

        /// <summary>
        /// Display name: winget's Name column rather than a bare id (Steam, not Valve.Steam).
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
