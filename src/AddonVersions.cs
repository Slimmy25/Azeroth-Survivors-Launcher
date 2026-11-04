using System;
using System.Collections.Generic;
using System.Globalization;

namespace AzerothLauncher
{
    // Version labels: the installed version beside each installed addon's name, and the newest release's version
    // on the Download More cards. A release is only shown once its repository has answered.
    public partial class LauncherApp
    {
        bool catalogVersionsBusy;
        readonly HashSet<string> catalogVersionsTried = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        // The version of the copy installed for this folder, or "" when it was not installed from a GitHub release.
        string InstalledVersionLabel(string folder)
        {
            AddonEntry entry = CatalogAddonForFolder(folder);
            if (entry == null) return "";
            string identity = Convert.ToString(AddonSetting(AddonInstalledRelease(), entry.Name), CultureInfo.InvariantCulture);
            if (string.IsNullOrEmpty(identity)) return "";
            int bar = identity.IndexOf('|');
            return bar > 0 ? identity.Substring(0, bar) : "";
        }

        // The newest release's version for a catalog entry, or "" when its repository has not answered yet.
        string LatestVersionLabel(AddonEntry entry)
        {
            if (entry == null || !IsDynamicGitHubAddonUrl(entry.Url)) return "";
            GitHubAddonRelease release = DynamicGitHubRelease(entry.Url);
            return release != null && !string.IsNullOrEmpty(release.Tag) ? release.Tag : "";
        }

        // Asks each catalog repository for its newest release, once per session, without installing anything.
        void EnsureCatalogVersions()
        {
            if (catalogVersionsBusy || PreviewMode) return;
            var pending = new List<string>();
            foreach (AddonEntry entry in Data.Addons)
            {
                if (!IsDynamicGitHubAddonUrl(entry.Url) || entry.WorkInProgress) continue;
                string key = entry.Url.TrimEnd('/');
                if (dynamicAddonReleaseChecked.Contains(key) || catalogVersionsTried.Contains(key) || pending.Contains(key)) continue;
                pending.Add(key);
            }
            if (pending.Count == 0) return;
            foreach (string key in pending) catalogVersionsTried.Add(key);
            catalogVersionsBusy = true;
            Bg(delegate
            {
                var found = new Dictionary<string, GitHubAddonRelease>(StringComparer.OrdinalIgnoreCase);
                foreach (string key in pending)
                    try { found[key] = ResolveGitHubAddonRelease(key, false); } catch (Exception) { }
                Ui(delegate
                {
                    catalogVersionsBusy = false;
                    foreach (var pair in found)
                        if (pair.Value != null)
                        {
                            dynamicAddonReleases[pair.Key] = pair.Value;
                            dynamicAddonReleaseChecked.Add(pair.Key);
                        }
                    if (current == "Addons") BuildPanel("Addons");
                });
            });
        }
    }
}
