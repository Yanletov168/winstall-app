using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Windows.Forms;
using Microsoft.Win32;

namespace winstall
{
    /// <summary>
    /// Иконки из реестра Uninstall (DisplayIcon / InstallLocation),
    /// чтобы вместо Valve.Steam был знакомый значок Steam.
    /// </summary>
    public class IconProvider : IDisposable
    {
        private readonly ImageList images;
        private readonly Dictionary<string, int> cache =
            new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, string> uninstallIcons =
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        private readonly int defaultIndex;

        public ImageList Images { get { return images; } }

        public IconProvider()
        {
            images = new ImageList();
            images.ColorDepth = ColorDepth.Depth32Bit;
            images.ImageSize = new Size(16, 16);
            images.Images.Add(SystemIcons.Application); // index 0
            defaultIndex = 0;
            try { LoadUninstallMap(); } catch { }
        }

        private void LoadUninstallMap()
        {
            string[] roots = new[]
            {
                @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall",
                @"SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall"
            };
            foreach (var hive in new[] { Registry.LocalMachine, Registry.CurrentUser })
            {
                foreach (var sub in roots)
                {
                    try
                    {
                        using (var key = hive.OpenSubKey(sub))
                        {
                            if (key == null) continue;
                            foreach (var name in key.GetSubKeyNames())
                            {
                                try
                                {
                                    using (var sk = key.OpenSubKey(name))
                                    {
                                        if (sk == null) continue;
                                        string disp = (sk.GetValue("DisplayName") as string) ?? "";
                                        if (string.IsNullOrWhiteSpace(disp)) continue;
                                        string icon =
                                            (sk.GetValue("DisplayIcon") as string) ??
                                            (sk.GetValue("InstallLocation") as string) ?? "";
                                        if (!string.IsNullOrWhiteSpace(icon) &&
                                            !uninstallIcons.ContainsKey(disp))
                                            uninstallIcons[disp] = icon;
                                    }
                                }
                                catch { }
                            }
                        }
                    }
                    catch { }
                }
            }
        }

        public int GetIndex(PackageInfo pkg)
        {
            string key = pkg.Id;
            if (cache.TryGetValue(key, out int idx)) return idx;

            int found = defaultIndex;
            try
            {
                string path = null;
                // 1) Точное совпадение по DisplayName
                if (!string.IsNullOrWhiteSpace(pkg.Name) && uninstallIcons.TryGetValue(pkg.Name.Trim(), out string v))
                    path = v;
                // 2) Поиск по части имени (Steam из "Steam")
                if (path == null)
                {
                    foreach (var kv in uninstallIcons)
                    {
                        if (kv.Key.IndexOf(pkg.DisplayName, StringComparison.OrdinalIgnoreCase) >= 0 ||
                            pkg.DisplayName.IndexOf(kv.Key, StringComparison.OrdinalIgnoreCase) >= 0)
                        {
                            path = kv.Value;
                            break;
                        }
                    }
                }
                if (!string.IsNullOrWhiteSpace(path))
                {
                    string file = CleanIconPath(path);
                    if (file != null && File.Exists(file))
                    {
                        using (var ico = Icon.ExtractAssociatedIcon(file))
                        {
                            if (ico != null)
                            {
                                using (var bmp = ico.ToBitmap())
                                {
                                    images.Images.Add(bmp);
                                    found = images.Images.Count - 1;
                                }
                            }
                        }
                    }
                }
            }
            catch { }

            cache[key] = found;
            return found;
        }

        private static string CleanIconPath(string raw)
        {
            // DisplayIcon бывает вида: "C:\...\app.exe,0" или с кавычками
            string s = raw.Trim().Trim('"');
            int comma = s.LastIndexOf(',');
            if (comma > 0 && s.EndsWith(",0"))
                s = s.Substring(0, comma).Trim().Trim('"');
            // InstallLocation бывает папкой
            if (Directory.Exists(s)) return null;
            return s;
        }

        public void Dispose()
        {
            if (images != null) images.Dispose();
        }
    }
}
