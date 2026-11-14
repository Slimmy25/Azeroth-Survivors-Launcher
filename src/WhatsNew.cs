// What changed since you last played, and the Guide's patch highlights.
//   Since you last played: when PLAY starts the game, the game's patch version, the launcher's version and Slim Suite's version
//   (when it is installed) are remembered. At the next start, if any of them moved on, one window sums up the game's patch notes
//   (each patch's own first lines, word for word), the launcher's changelog and, when Slim Suite is installed, its release notes.
//   Patch highlights: a Wiki tree entry whose name a recent patch note mentions is marked for the newest 3 game patches (the patch
//   that changed it and the next two), then shows normally again. A later change starts its own 3 patches.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows.Forms;

namespace AzerothLauncher
{
    public static class PatchHighlights
    {
        public const int Patches = 3;                         // how many game patches a change stays highlighted

        public sealed class Hit
        {
            public string Version;                            // the newest of the recent patches that names it
            public List<string> Lines = new List<string>();   // that patch's lines naming it, word for word
        }

        // tree entries that are places in the tree, not things a patch changes
        static readonly HashSet<string> Structural = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "Wiki", "WikiRoot", "Classes", "Spells", "Talents", "Gear", "Items", "Codex", "Codex (Necklaces)", "Archive",
            "Warrior", "Paladin", "Hunter", "Rogue", "Priest", "Shaman", "Mage", "Warlock", "Druid", "Overview", "Zones",
            "Bosses", "Heroic", "Normal", "Talent", "Spell", "Set", "Sets", "Ring", "Rings", "Trinket", "Trinkets", "Necklace"
        };

        static List<KeyValuePair<string, string[]>> recent;   // version -> its lines, newest first
        static readonly Dictionary<string, Hit> cache = new Dictionary<string, Hit>(StringComparer.OrdinalIgnoreCase);

        static List<KeyValuePair<string, string[]>> Recent()
        {
            if (recent != null) return recent;
            var list = new List<KeyValuePair<string, string[]>>();
            try
            {
                foreach (string post in Util.Changelog)
                {
                    var m = Regex.Match(Util.PostTitle(post), @"^v?(\d+(?:\.\d+)+)");
                    if (!m.Success) continue;
                    string[] lines = post.Replace("\r", "").Split('\n');
                    list.Add(new KeyValuePair<string, string[]>(m.Groups[1].Value, lines));
                    if (list.Count >= Patches) break;
                }
            }
            catch (Exception) { }
            recent = list;
            return recent;
        }

        // the highlight for a tree entry, or null (an old change, a structural entry, or a name no recent note uses)
        public static Hit For(string label)
        {
            if (string.IsNullOrEmpty(label)) return null;
            string name = label.Trim();
            if (name.Length < 4 || Structural.Contains(name)) return null;
            lock (cache)
            {
                Hit known;
                if (cache.TryGetValue(name, out known)) return known;
                Hit hit = null;
                var word = new Regex(@"(?<![\w'])" + Regex.Escape(name) + @"(?![\w])", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
                foreach (var patch in Recent())                // newest first: the newest patch naming it wins
                {
                    for (int i = 1; i < patch.Value.Length; i++)   // line 0 is the title
                    {
                        string line = patch.Value[i].Trim();
                        if (line.Length == 0 || !word.IsMatch(line)) continue;
                        if (hit == null) hit = new Hit { Version = patch.Key };
                        if (hit.Lines.Count < 4) hit.Lines.Add(line);
                    }
                    if (hit != null) break;
                }
                cache[name] = hit;
                return hit;
            }
        }

        public static string Tip(Hit hit)
        {
            var sb = new StringBuilder();
            sb.Append(Lang.F("Changed in game patch {0}", hit.Version));
            foreach (string line in hit.Lines) sb.Append("\n").Append(line.Length > 220 ? line.Substring(0, 217) + "..." : line);
            return sb.ToString();
        }
    }

    public partial class LauncherApp
    {
        const string SlimSuiteRepo = "Slimmy25/SlimSuite";

        static string LauncherVersionText { get { return Application.ProductVersion ?? ""; } }

        // Slim Suite's version from its .toc in the AddOns folder; "" when it is not installed
        string InstalledSlimSuiteVersion()
        {
            try
            {
                string dir = Cfg.Str("addons_dir");
                if (string.IsNullOrEmpty(dir)) return "";
                string toc = Path.Combine(Path.Combine(dir, "SlimSuite"), "SlimSuite.toc");
                if (!File.Exists(toc)) return "";
                foreach (string line in File.ReadAllLines(toc))
                {
                    var m = Regex.Match(line, @"^##\s*Version\s*:\s*(.+)$", RegexOptions.IgnoreCase);
                    if (m.Success) return m.Groups[1].Value.Trim();
                }
            }
            catch (Exception) { }
            return "";
        }

        // PLAY started the game: what was current then is what "since you last played" compares with
        void RememberPlayedVersions()
        {
            try
            {
                Cfg.Set("played_game_version", Util.GameVersion);
                Cfg.Set("played_launcher_version", LauncherVersionText);
                Cfg.Set("played_slimsuite_version", InstalledSlimSuiteVersion());
                Cfg.Remove("whatsnew_shown");
                Cfg.Save();
            }
            catch (Exception) { }
        }

        // at start: one window with what changed since the last PLAY, once
        void MaybeShowWhatsNew()
        {
            string game0 = Cfg.Str("played_game_version"), launcher0 = Cfg.Str("played_launcher_version");
            if (string.IsNullOrEmpty(game0) && string.IsNullOrEmpty(launcher0)) return;   // never played through the launcher yet
            string game1 = Util.GameVersion, launcher1 = LauncherVersionText;
            string slim0 = Cfg.Str("played_slimsuite_version"), slim1 = InstalledSlimSuiteVersion();
            string key = game1 + "|" + launcher1 + "|" + slim1;
            if (Cfg.Str("whatsnew_shown") == key) return;
            bool gameMoved = Newer(game1, game0), launcherMoved = Newer(launcher1, launcher0);
            bool slimMoved = slim1.Length > 0 && slim0.Length > 0 && Newer(slim1, slim0);
            if (!gameMoved && !launcherMoved && !slimMoved) return;
            Bg(delegate
            {
                string slimNotes = slimMoved ? SlimSuiteNotes(slim1) : null;
                string text = WhatsNewText(gameMoved ? game0 : null, launcherMoved ? launcher0 : null, launcher1,
                                           slimMoved ? slim0 : null, slim1, slimNotes);
                Ui(delegate
                {
                    if (IsDisposed || text.Length == 0) return;
                    Cfg.Set("whatsnew_shown", key);
                    Cfg.Save();
                    Msg.Show(this, text, Lang.T("What changed since you last played"));
                });
            });
        }

        static bool Newer(string now, string then)
        {
            if (string.IsNullOrEmpty(now) || string.IsNullOrEmpty(then)) return false;
            int? c = Util.CompareVersions(now, then);
            return c.HasValue ? c.Value > 0 : !string.Equals(now, then, StringComparison.OrdinalIgnoreCase);
        }

        string WhatsNewText(string gameSince, string launcherSince, string launcherNow, string slimSince, string slimNow, string slimNotes)
        {
            var sb = new StringBuilder();
            if (gameSince != null)
            {
                sb.Append(Lang.T("THE GAME")).Append("\n");
                int shown = 0;
                foreach (string post in Util.Changelog)
                {
                    string title = Util.PostTitle(post);
                    if (Util.ParseVersion(title) == null || !Newer(title, gameSince)) continue;
                    if (shown == 6) { sb.Append(Lang.T("More patches are on the News page.")).Append("\n"); break; }
                    sb.Append(Lang.F("Patch {0}", title)).Append("\n");
                    int lines = 0;
                    foreach (string raw in post.Replace("\r", "").Split('\n'))
                    {
                        string line = raw.Trim();
                        if (line.Length == 0 || line == title) continue;
                        sb.Append("   ").Append(line).Append("\n");    // the patch's own words
                        if (++lines == 3) break;
                    }
                    shown++;
                }
                sb.Append("\n");
            }
            if (launcherSince != null)
            {
                sb.Append(Lang.T("THE LAUNCHER")).Append("\n");
                string current = null;
                int lines = 0;
                foreach (string raw in Util.LauncherChangelog.Split('\n'))
                {
                    string line = raw.Trim();
                    var head = Regex.Match(line, @"^#+\s*v?(\d+(?:\.\d+)*)");
                    if (head.Success)
                    {
                        string v = head.Groups[1].Value;
                        current = Newer(v, launcherSince) && !Newer(v, launcherNow) ? v : null;
                        if (current != null) { sb.Append(Lang.F("Launcher {0}", "v" + v)).Append("\n"); lines = 0; }
                        continue;
                    }
                    if (current == null || !line.StartsWith("- ")) continue;
                    if (lines++ < 4) sb.Append("   • ").Append(line.Substring(2)).Append("\n");
                }
                sb.Append("\n");
            }
            if (slimSince != null)
            {
                sb.Append(Lang.T("SLIM SUITE")).Append("\n");
                sb.Append(Lang.F("Updated from {0} to {1}.", slimSince, slimNow)).Append("\n");
                if (!string.IsNullOrEmpty(slimNotes)) sb.Append(slimNotes).Append("\n");
            }
            return sb.ToString().TrimEnd('\n');
        }

        // the first lines of Slim Suite's release notes for this version (its GitHub release v<version>); null when unreachable
        static string SlimSuiteNotes(string version)
        {
            try
            {
                string tag = version.StartsWith("v", StringComparison.OrdinalIgnoreCase) ? version : "v" + version;
                var release = Archive.ReadGitHubReleaseMetadata("https://api.github.com/repos/" + SlimSuiteRepo + "/releases/tags/" + Uri.EscapeDataString(tag));
                string body = Json.Str(release, "body").Replace("\r", "");
                var sb = new StringBuilder();
                int lines = 0;
                foreach (string raw in body.Split('\n'))
                {
                    string line = raw.Trim().TrimStart('#').Trim();
                    if (line.Length == 0) continue;
                    sb.Append("   ").Append(line).Append("\n");
                    if (++lines == 5) break;
                }
                return sb.ToString().TrimEnd('\n');
            }
            catch (Exception) { return null; }
        }
    }
}
