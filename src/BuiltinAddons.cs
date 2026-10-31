// The AZS addons the launcher carries inside itself: AZS Handbook and AZS Minor Mods. Stats are provided by
// Maidenhead's separately maintained Azeroth Stats addon from Download More.
// (AZS Transparency was a test and is gone: Retired lists it, and its folder is taken out of AddOns.) Download More lists them as "builtin:<folder>"; INSTALL writes their files from the launcher, and
// an installed one is brought up to the launcher's version right before the game starts.
// Each one gets its own copy of AZSKit.lua, the look all AZS addons share (addons\AZSKit).
// AZS Handbook also gets AZSHandbookData.lua, made here from the launcher's own guide and talent trees, so the
// guide in game is always the one in the launcher.
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace AzerothLauncher
{
    public class BuiltinAddon
    {
        public string Folder;                                 // the folder in AddOns
        public string[] Files;                                // its own files (resources AS.addon.<Folder>.<file>)
        public string Generated;                              // a file the launcher makes (null: none)
    }

    public static class BuiltinAddons
    {
        public const string Prefix = "builtin:";
        public const string Kit = "AZSKit.lua";

        public static readonly BuiltinAddon[] All =
        {
            new BuiltinAddon { Folder = "AZSHandbook", Files = new[] { "AZSHandbook.toc", "AZSHandbook.lua" }, Generated = "AZSHandbookData.lua" },
            new BuiltinAddon { Folder = "AZSMinorMods", Files = new[] { "AZSMinorMods.toc", "AZSMinorMods.lua" } },
        };

        public static BuiltinAddon Find(string url)
        {
            if (url == null || !url.StartsWith(Prefix, StringComparison.OrdinalIgnoreCase)) return null;
            string f = url.Substring(Prefix.Length).Trim();
            foreach (var a in All) if (string.Equals(a.Folder, f, StringComparison.OrdinalIgnoreCase)) return a;
            return null;
        }

        static IEnumerable<KeyValuePair<string, string>> Contents(BuiltinAddon a)
        {
            foreach (string f in a.Files) yield return new KeyValuePair<string, string>(f, Util.ReadResource("AS.addon." + a.Folder + "." + f));
            yield return new KeyValuePair<string, string>(Kit, Util.ReadResource("AS.addon.kit." + Kit));
            if (a.Generated != null)
                yield return new KeyValuePair<string, string>(a.Generated, HandbookData.Lua());
        }

        // its folder in AddOns, turned on or off, or null
        public static string PathOf(string addonsDir, BuiltinAddon a)
        {
            if (string.IsNullOrEmpty(addonsDir)) return null;
            string on = Path.Combine(addonsDir, a.Folder), off = on + Addons.DisabledSuffix;
            if (Directory.Exists(on)) return on;
            if (Directory.Exists(off)) return off;
            return null;
        }

        static string Norm(string t) { return (t ?? "").Replace("\r\n", "\n").TrimEnd(); }

        public static bool Outdated(string addonsDir, BuiltinAddon a)
        {
            string dir = PathOf(addonsDir, a);
            if (dir == null) return false;
            foreach (var kv in Contents(a))
            {
                string p = Path.Combine(dir, kv.Key);
                try { if (!File.Exists(p) || Norm(File.ReadAllText(p, Encoding.UTF8)) != Norm(kv.Value)) return true; }
                catch (Exception) { return true; }
            }
            return false;
        }

        // writes it into AddOns (where it is, turned off or not); its saved settings are kept
        public static void Install(string addonsDir, BuiltinAddon a)
        {
            if (string.IsNullOrEmpty(addonsDir)) throw new UserError("The AddOns folder is not set. Set the game config folder in Options.");
            string dir = PathOf(addonsDir, a) ?? Path.Combine(addonsDir, a.Folder);
            Directory.CreateDirectory(dir);
            foreach (var kv in Contents(a))
            {
                if (kv.Value.Length == 0) throw new UserError("This launcher was built without " + a.Folder + ".");
                File.WriteAllText(Path.Combine(dir, kv.Key), kv.Value.Replace("\r\n", "\n").Replace("\n", "\r\n"), new UTF8Encoding(false));
            }
        }

        // the installed ones that are not the launcher's version, brought up to it (before the game starts)
        // addons that are gone from the launcher: their folders (on or off) come out of AddOns
        public static readonly string[] Retired = { "AZSTransparency" };

        public static List<string> RemoveRetired(string addonsDir, string savedDir)
        {
            var gone = new List<string>();
            if (string.IsNullOrEmpty(addonsDir) || !Directory.Exists(addonsDir)) return gone;
            foreach (string r in Retired)
                if (Directory.Exists(Path.Combine(addonsDir, r)) || Directory.Exists(Path.Combine(addonsDir, r + Addons.DisabledSuffix)))
                    gone.AddRange(Addons.Uninstall(addonsDir, new List<string> { r }, savedDir));
            return gone;
        }

        public static List<string> UpdateInstalled(string addonsDir)
        {
            var done = new List<string>();
            foreach (var a in All)
                if (PathOf(addonsDir, a) != null && Outdated(addonsDir, a)) { Install(addonsDir, a); done.Add(a.Folder); }
            return done;
        }
    }

    // AZSHandbookData.lua: the launcher's guide (assets\guide.txt) and talent trees (assets\talents.txt) as Lua tables,
    // with the spells' icons and the talents that change each spell, for AZS Handbook in game
    public static class HandbookData
    {
        static string cached;

        static string Q(string s)
        {
            var b = new StringBuilder("\"");
            foreach (char c in s ?? "")
            {
                if (c == '\\') b.Append("\\\\");
                else if (c == '"') b.Append("\\\"");
                else if (c == '\n') b.Append("\\n");
                else if (c == '\r') continue;
                else if (c == '·') b.Append("-");           // the game's fonts may not have it
                else b.Append(c);
            }
            return b.Append('"').ToString();
        }

        static void Page(StringBuilder s, GuidePage p, string indent)
        {
            s.Append(indent).Append("title = ").Append(Q(p.Title)).Append(", desc = ").Append(Q(p.Desc)).Append(",\n");
            s.Append(indent).Append("body = {\n");
            foreach (string raw in p.Body.Split('\n')) s.Append(indent).Append("\t").Append(Q(raw.Trim())).Append(",\n");
            s.Append(indent).Append("},\n");
            s.Append(indent).Append("videos = {");
            foreach (var v in p.Videos) s.Append(" ").Append(Q(v.Title + (v.Author.Length > 0 ? " (video by " + v.Author + ")" : ""))).Append(",");
            s.Append(" },\n");
        }

        public static string Lua()
        {
            if (cached != null) return cached;
            var s = new StringBuilder();
            s.Append("-- AZS Handbook: the launcher's New Player Guide and talent trees, written by the launcher when it installs or\n");
            s.Append("-- updates the addon (so the guide in game is always the launcher's). Do not edit: the launcher writes it again.\n");
            s.Append("AZSHANDBOOK_DATA = {\n");
            s.Append("topics = {\n");
            List<GuideTopic> topics;
            try { topics = Data.Guide; } catch (Exception) { topics = new List<GuideTopic>(); }
            foreach (var t in topics)
            {
                s.Append("{\n");
                Page(s, t, "\t");
                s.Append("\tpages = {\n");
                foreach (var p in t.Pages) { s.Append("\t\t{\n"); Page(s, p, "\t\t\t"); s.Append("\t\t},\n"); }
                s.Append("\t},\n},\n");
            }
            s.Append("},\n");
            // the talent trees: per class, three trees of { name, icon, row, column, ranks, text, { spells it changes } }
            s.Append("talents = {\n");
            var classes = new List<string>();
            foreach (var tal in Talents.All()) if (!classes.Contains(tal.Class)) classes.Add(tal.Class);
            foreach (string c in classes)
            {
                s.Append("[").Append(Q(c)).Append("] = {\n");
                foreach (var tr in Talents.Of(c))
                {
                    s.Append("\t{ spec = ").Append(Q(tr.Spec)).Append(", t = {\n");
                    foreach (var tal in tr.Talents)
                    {
                        s.Append("\t\t{ ").Append(Q(tal.Name)).Append(", ").Append(Q(tal.Icon)).Append(", ").Append(tal.Row).Append(", ")
                         .Append(tal.Col).Append(", ").Append(tal.Ranks).Append(", ").Append(Q(tal.Shown)).Append(", {");
                        foreach (string ch in tal.Changes) s.Append(" ").Append(Q(ch)).Append(",");
                        s.Append(" }, ").Append(tal.Milestone ? "true" : "false").Append(" },\n");
                    }
                    s.Append("\t} },\n");
                }
                s.Append("},\n");
            }
            s.Append("},\n");
            // every spell's icon by its name without case, spaces or punctuation
            s.Append("icons = {\n");
            var seen = new HashSet<string>();
            foreach (string raw in Util.ReadResource("AS.spell-icons.txt").Replace("\r\n", "\n").Split('\n'))
            {
                string line = raw.Trim();
                int bar = line.IndexOf('|');
                if (line.StartsWith("#") || bar <= 0) continue;
                string k = Key(line.Substring(0, bar));
                if (k.Length == 0 || !seen.Add(k)) continue;
                s.Append("\t[").Append(Q(k)).Append("] = ").Append(Q(line.Substring(bar + 1).Trim())).Append(",\n");
            }
            foreach (var tal in Talents.All())
            {
                string k = Key(tal.Name);
                if (k.Length > 0 && seen.Add(k)) s.Append("\t[").Append(Q(k)).Append("] = ").Append(Q(tal.Icon)).Append(",\n");
            }
            s.Append("},\n");
            // the names to highlight in the text: every spell and talent, the longest first
            var names = new List<string>();
            foreach (string raw in Util.ReadResource("AS.spell-icons.txt").Replace("\r\n", "\n").Split('\n'))
            {
                string line = raw.Trim();
                int bar = line.IndexOf('|');
                if (line.StartsWith("#") || bar <= 0) continue;
                string n = line.Substring(0, bar).Trim();
                if (n.Length >= 4 && !names.Contains(n)) names.Add(n);
            }
            foreach (var tal in Talents.All()) if (tal.Name.Length >= 4 && !names.Contains(tal.Name)) names.Add(tal.Name);
            names.Sort(delegate(string a, string b) { return b.Length.CompareTo(a.Length); });
            // the bosses' names: kept plain, so "Smite" in "Mr. Smite" is not taken for the spell
            s.Append("plain = {");
            foreach (string n in Items.BossNames()) s.Append(" ").Append(Q(n)).Append(",");
            s.Append(" },\n");
            s.Append("names = {");
            foreach (string n in names) s.Append(" ").Append(Q(n)).Append(",");
            s.Append(" },\n");
            GearAndSpells(s, topics);
            s.Append("};\n");
            cached = s.ToString();
            return cached;
        }

        static string N(double v) { return v.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture); }

        // the gear of every class (with its card and its bonuses), the jewelry, and for each class's spells the other
        // spells that name them: the same the launcher's Guide, Gear Planner and class pages show
        static void GearAndSpells(StringBuilder s, List<GuideTopic> topics)
        {
            s.Append("rarities = {");
            foreach (string r in Items.Rarities) s.Append(" ").Append(Q(r)).Append(",");
            s.Append(" },\n");
            // gear[class] = { { rarity, slot, name, health, known, { stat = value, ... }, card }, ... }
            s.Append("gear = {\n");
            var classes = new List<string>();
            foreach (var p in Items.Gear) if (!classes.Contains(p.Class)) classes.Add(p.Class);
            foreach (string c in classes)
            {
                s.Append("[").Append(Q(c)).Append("] = {\n");
                foreach (var p in Items.Gear)
                {
                    if (p.Class != c) continue;
                    string card = Items.Card(p);
                    int nl = card.IndexOf('\n');
                    s.Append("\t{ ").Append(Q(p.Rarity)).Append(", ").Append(Q(p.Slot)).Append(", ").Append(Q(p.Name)).Append(", ")
                     .Append(p.Health).Append(", ").Append(p.Known ? "true" : "false").Append(", {");
                    if (p.Known) foreach (var b in p.Bonuses) s.Append(" [").Append(Q(b.Stat)).Append("] = ").Append(N(b.Value)).Append(",");
                    s.Append(" }, ").Append(Q(nl >= 0 ? card.Substring(nl + 1) : card)).Append(" },\n");
                }
                s.Append("},\n");
            }
            s.Append("},\n");
            // itemicons[name] = the real icon of a gear piece that is a real item (the game's own texture in game)
            s.Append("itemicons = {\n");
            foreach (var kv in Items.GearIcons) s.Append("[").Append(Q(kv.Key)).Append("] = ").Append(Q(kv.Value)).Append(",\n");
            s.Append("},\n");
            // sets[class][rarity] = { set name, 4-piece text, { stat = value }, 8-piece text }
            s.Append("sets = {\n");
            foreach (string c in classes)
            {
                s.Append("[").Append(Q(c)).Append("] = {\n");
                foreach (string r in Items.Rarities)
                {
                    var four = Items.FourPiece(r);
                    string eight = Items.EightPiece(c, r);
                    s.Append("\t[").Append(Q(r)).Append("] = { ").Append(Q(Items.SetName(c, r))).Append(", ")
                     .Append(Q(four != null ? four.Text : "not entered yet")).Append(", {");
                    if (four != null) foreach (var b in four.Bonuses) s.Append(" [").Append(Q(b.Stat)).Append("] = ").Append(N(b.Value)).Append(",");
                    s.Append(" }, ").Append(Q(eight ?? "not entered yet")).Append(" },\n");
                }
                s.Append("},\n");
            }
            s.Append("},\n");
            // jewels = { { kind, name, icon, { stat = value }, card }, ... }
            s.Append("jewels = {\n");
            foreach (var j in Items.Jewelry)
            {
                s.Append("\t{ ").Append(Q(j.Kind)).Append(", ").Append(Q(j.Name)).Append(", ").Append(Q(j.Icon)).Append(", {");
                var sum = new Dictionary<string, double>();
                foreach (var b in j.Bonuses) { double v; sum.TryGetValue(b.Stat, out v); sum[b.Stat] = v + b.Value; }
                foreach (var kv in sum) s.Append(" [").Append(Q(kv.Key)).Append("] = ").Append(N(kv.Value)).Append(",");
                s.Append(" }, ").Append(Q(string.Join("\n", j.Lines.ToArray()))).Append(", ").Append(Q(j.Class)).Append(", ").Append(Q(j.Spell)).Append(" },\n");
            }
            s.Append("},\n");
            // codexchanges[class][spell key] = the class-specific neck piece that remakes that spell.
            s.Append("codexchanges = {\n");
            foreach (string cls in Items.Classes)
            {
                bool classHas = false;
                foreach (var j in Items.Jewelry) if (j.IsCodex && j.Class == cls) { classHas = true; break; }
                if (!classHas) continue;
                s.Append("[").Append(Q(cls)).Append("] = {\n");
                foreach (var j in Items.Jewelry)
                {
                    if (!j.IsCodex || j.Class != cls) continue;
                    s.Append("[").Append(Q(Key(j.Spell))).Append("] = { { ")
                     .Append(Q(j.Name)).Append(", ").Append(Q(j.Spell)).Append(", ").Append(Q(Items.Card(j))).Append(" }, },\n");
                }
                s.Append("},\n");
            }
            s.Append("},\n");
            // setchanges[class][spell key] = { { "Faith 8-piece", its text, rarity }, ... }: the class's sets whose 8-piece
            // bonus names the spell (as written, or more than one of it: "Every 4 Wraths")
            s.Append("setchanges = {\n");
            foreach (var t in topics)
            {
                if (t.Title != "Classes") continue;
                foreach (var pg in t.Pages)
                {
                    var lines = new StringBuilder();
                    string sec = "";
                    foreach (string raw in pg.Body.Split('\n'))
                    {
                        string line = raw.Trim();
                        if (line.StartsWith("# ")) { sec = line.Substring(2).Trim(); continue; }
                        if (!sec.StartsWith("Spell pool") || !line.StartsWith("- ")) continue;
                        var m = System.Text.RegularExpressions.Regex.Match(line.Substring(2).Trim(), @"^(.+?(?: \([^)]*\))?): (?=[a-z0-9])(.+)$");
                        if (!m.Success) continue;
                        string spell = System.Text.RegularExpressions.Regex.Replace(m.Groups[1].Value, @"\s*\(.*\)$", "").Trim();
                        if (spell.Length < 3) continue;
                        var word = new System.Text.RegularExpressions.Regex(@"(?<![A-Za-z'])" + System.Text.RegularExpressions.Regex.Escape(spell) + @"(?:s|'s)?(?![A-Za-z])");
                        var hits = new StringBuilder();
                        foreach (string rar in Items.Rarities)
                        {
                            string eight = Items.EightPiece(pg.Title, rar);
                            if (eight == null || !word.IsMatch(eight)) continue;
                            hits.Append(" { ").Append(Q(Items.SetName(pg.Title, rar) + " 8-piece")).Append(", ").Append(Q(eight)).Append(", ").Append(Q(rar)).Append(" },");
                        }
                        if (hits.Length > 0) lines.Append("\t[").Append(Q(Key(spell))).Append("] = {").Append(hits.ToString()).Append(" },\n");
                    }
                    if (lines.Length > 0) s.Append("[").Append(Q(pg.Title)).Append("] = {\n").Append(lines.ToString()).Append("},\n");
                }
            }
            s.Append("},\n");
            // spellchanges[class][spell key] = { other spells of the class whose text names it }
            s.Append("spellchanges = {\n");
            foreach (var t in topics)
            {
                if (t.Title != "Classes") continue;
                foreach (var pg in t.Pages)
                {
                    var pool = new List<KeyValuePair<string, string>>();
                    string sec = "";
                    foreach (string raw in pg.Body.Split('\n'))
                    {
                        string line = raw.Trim();
                        if (line.StartsWith("# ")) { sec = line.Substring(2).Trim(); continue; }
                        if (!sec.StartsWith("Spell pool") || !line.StartsWith("- ")) continue;
                        var m = System.Text.RegularExpressions.Regex.Match(line.Substring(2).Trim(), @"^(.+?(?: \([^)]*\))?): (?=[a-z0-9])(.+)$");
                        if (!m.Success) continue;
                        string game = GuideLinks.GameTip(System.Text.RegularExpressions.Regex.Replace(m.Groups[1].Value, @"\s*\(.*\)$", ""));
                        pool.Add(new KeyValuePair<string, string>(m.Groups[1].Value, game ?? m.Groups[2].Value));
                    }
                    if (pool.Count == 0) continue;
                    var lines = new StringBuilder();
                    foreach (var kv in pool)
                    {
                        var by = SpellRow.Mentioning(kv.Key, pool);
                        if (by.Count == 0) continue;
                        lines.Append("\t[").Append(Q(Key(System.Text.RegularExpressions.Regex.Replace(kv.Key, @"\s*\(.*\)$", "")))).Append("] = {");
                        foreach (var b in by) lines.Append(" ").Append(Q(b.Key)).Append(",");
                        lines.Append(" },\n");
                    }
                    if (lines.Length == 0) continue;
                    s.Append("[").Append(Q(pg.Title)).Append("] = {\n").Append(lines.ToString()).Append("},\n");
                }
            }
            s.Append("},\n");
        }

        public static string Key(string name)
        {
            var b = new StringBuilder();
            foreach (char c in (name ?? "").ToLowerInvariant()) if ((c >= 'a' && c <= 'z') || (c >= '0' && c <= '9')) b.Append(c);
            return b.ToString();
        }
    }
}
