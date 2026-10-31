// The New Player Guide's links and hover texts. Every name the guide knows (its topics and pages, the classes,
// every spell in a class's spell pool, the talents, the races and their racials, the zones) is found in the
// guide's text: click it to go to the page about it, point at it to read what it does.
//
// The hover texts come from the game itself: assets/game-text.txt holds what the game says about classes, races,
// racials, zones and talents (taken from the game at wow.aethus.pl). A spell's hover text is the guide's own line
// about it in its class's spell pool (the game builds a spell's tooltip with your numbers during a run).
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows.Forms;

namespace AzerothLauncher
{
    public class GuideLink
    {
        public string Term = "", Tip = "";
        public string FindText = "";                           // text to locate on the destination page after navigation
        public int Topic = -1, Page = -1;                    // where a click goes (-1, -1: nowhere, a hover text only)
        public string Class;                                 // a talent: its class (a click opens its talent trees)
    }

    public static class GuideLinks
    {
        static Dictionary<string, GuideLink> links;
        static Dictionary<string, List<Talent>> shared;       // talent names in more than one class's tree
        static Dictionary<string, string> spellClasses;       // spell and the class whose spell pool contains it
        static int classesAt = -1;                            // the Classes topic
        static Regex finder;
        public static Action<GuideLink> Navigate;            // set by the launcher: open the Guide where the link goes

        // the Guide's words (spells, talents, items, races, zones) whose name holds every word typed, the ones that start
        // with it first: the search box's suggestions. Only those that lead somewhere.
        public static List<GuideLink> Matching(string q, int max)
        {
            if (links == null) Build();
            var list = new List<GuideLink>();
            string[] words = (q ?? "").ToLowerInvariant().Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            if (words.Length == 0) return list;
            Func<string, bool> matches = delegate(string value)
            {
                string n = (value ?? "").ToLowerInvariant();
                foreach (string w in words) if (!n.Contains(w)) return false;
                return true;
            };
            foreach (var entry in links)
            {
                GuideLink l = entry.Value;
                if (l.Topic < 0 && l.Class == null) continue;
                if (matches(l.Term) || matches(entry.Key)) list.Add(l);
            }

            // A spell search also offers any 8-piece set bonus that names it. Codex stays out of search;
            // the spell and item links in class guides lead to the class-specific passage.
            try
            {
                List<GuideTopic> topics = Data.Guide;
                int itemsTopic = topics.FindIndex(delegate(GuideTopic g) { return g.Title == "Items"; });
                if (itemsTopic >= 0 && spellClasses != null)
                {
                    List<GuidePage> pages = topics[itemsTopic].Pages;
                    foreach (var pair in spellClasses)
                    {
                            int split = pair.Key.IndexOf('|');
                            if (split < 0) continue;
                            string spell = pair.Key.Substring(0, split), cls = pair.Value;
                            if (!matches(spell)) continue;
                            string pattern = @"(?<![A-Za-z'])" + Regex.Escape(spell) + @"(?:s|'s)?(?![A-Za-z])";
                            for (int r = 0; r < Items.Rarities.Length; r++)
                            {
                                string rarity = Items.Rarities[r];
                                string bonus = Items.EightPiece(cls, rarity);
                                if (string.IsNullOrEmpty(bonus) || !Regex.IsMatch(bonus, pattern, RegexOptions.IgnoreCase)) continue;
                                string gearPage = cls + " Gear";
                                int gearIndex = pages.FindIndex(delegate(GuidePage g) { return g.Title == gearPage; });
                                list.Add(new GuideLink
                                {
                                    Term = spell + " · " + Items.SetName(cls, rarity) + " 8-piece",
                                    FindText = spell,
                                    Topic = itemsTopic,
                                    Page = gearIndex,
                                    Tip = bonus
                                });
                            }
                    }
                }
            }
            catch (Exception) { }

            // Avoid duplicate labels if a spell has more than one matching route into the same section.
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            list.RemoveAll(delegate(GuideLink l) { return !seen.Add(l.Term); });
            string first = words[0];
            list.Sort(delegate(GuideLink a, GuideLink b)
            {
                int sa = a.Term.ToLowerInvariant().StartsWith(first) ? 0 : 1, sb = b.Term.ToLowerInvariant().StartsWith(first) ? 0 : 1;
                return sa != sb ? sa.CompareTo(sb) : a.Term.Length.CompareTo(b.Term.Length);
            });
            if (list.Count > max) list.RemoveRange(max, list.Count - max);
            return list;
        }
        public static Func<Dictionary<string, string>> CapturedTips;   // optional game-captured tooltip provider; no stats addon is wired here
        static Dictionary<string, GuideLink> byKey;           // the same links by Norm(name): "shadowbolt" -> Shadow Bolt

        public static void Reset() { links = null; }        // read again (new tooltips from the game)

        // a name without case, spaces, apostrophes or a plural s: "Shadow Bolts" and "shadowbolt" are both "shadowbolt"
        static string Norm(string s)
        {
            string k = Regex.Replace((s ?? "").ToLowerInvariant(), "[^a-z0-9]", "");
            return k;
        }

        static Dictionary<string, string> GameText()
        {
            var d = new Dictionary<string, string>(StringComparer.Ordinal);
            bool on = false;
            foreach (string raw in Util.ReadResource("AS.game-text.txt").Replace("\r\n", "\n").Split('\n'))
            {
                string line = raw.Trim();
                if (line == "===") { on = true; continue; }
                if (!on || line.Length == 0 || line.StartsWith("#")) continue;
                int bar = line.IndexOf('|');
                if (bar <= 0) continue;
                d[line.Substring(0, bar).Trim()] = line.Substring(bar + 1).Trim();
            }
            return d;
        }

        static void Add(string term, int topic, int page, string tip)
        {
            term = (term ?? "").Trim();
            if (term.Length < 4 || links.ContainsKey(term)) return;
            var l = new GuideLink();
            l.Term = term;
            l.FindText = term;
            l.Topic = topic;
            l.Page = page;
            l.Tip = tip ?? "";
            links[term] = l;
        }

        // a spell pool line: the name ends at the first ": " followed by the (lower-case) description, so a name with a colon
        // of its own stays whole ("Power Word: Shield: absorbs..." is Power Word: Shield, not Power Word)
        static readonly Regex Bullet = new Regex(@"^- ([^(]+?)(?: \([^)]*\))?: (?=[a-z0-9])(.+)$");
        static readonly string[] TreeWords = { "Arms", "Fury", "Protection", "Holy", "Retribution", "Beast Mastery", "Marksmanship",
            "Survival", "Assassination", "Combat", "Subtlety", "Discipline", "Shadow", "Elemental", "Enhancement", "Restoration",
            "Arcane", "Fire", "Frost", "Affliction", "Demonology", "Destruction", "Balance", "Feral", "Feral Combat" };

        static void Build()
        {
            links = new Dictionary<string, GuideLink>(StringComparer.Ordinal);
            spellClasses = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            var text = GameText();
            List<GuideTopic> topics;
            try { topics = Data.Guide; }
            catch (Exception) { topics = new List<GuideTopic>(); }
            int racesTopic = -1, zonesTopic = -1;
            for (int t = 0; t < topics.Count; t++)
            {
                if (topics[t].Title.StartsWith("Races")) racesTopic = t;
                if (topics[t].Title.StartsWith("Zones")) zonesTopic = t;
            }
            // pages first (a class page wins over a mention elsewhere), then the spells and talents on them
            for (int t = 0; t < topics.Count; t++)
                if (!string.Equals(topics[t].Title, "Archive", StringComparison.OrdinalIgnoreCase))
                for (int p = 0; p < topics[t].Pages.Count; p++)
                {
                    GuidePage pg = topics[t].Pages[p];
                    string tip;
                    Add(pg.Title, t, p, text.TryGetValue(pg.Title, out tip) ? tip : pg.Desc);
                }
            for (int t = 0; t < topics.Count; t++)
            {
                if (string.Equals(topics[t].Title, "Archive", StringComparison.OrdinalIgnoreCase)) continue;
                if (topics[t].Title.Length >= 6) Add(topics[t].Title, t, -1, topics[t].Desc);
                for (int p = -1; p < topics[t].Pages.Count; p++)
                {
                    GuidePage pg = p < 0 ? (GuidePage)topics[t] : topics[t].Pages[p];
                    string section = "";
                    foreach (string raw in pg.Body.Split('\n'))
                    {
                        string line = raw.Trim();
                        if (line.StartsWith("# ")) { section = line.Substring(2).Trim(); continue; }
                        if (!line.StartsWith("- ")) continue;
                        Match m = Bullet.Match(line);
                        if (section.StartsWith("Spell pool") && m.Success)
                        {
                            string name = m.Groups[1].Value.Trim();
                            spellClasses[name + "|" + pg.Title] = pg.Title;
                            string tip;
                            Add(name, t, p, text.TryGetValue(name, out tip) ? tip : Capital(m.Groups[2].Value.Trim()));
                        }
                        else if (section.StartsWith("Talent") && line.IndexOf(':') > 0)
                        {
                            foreach (string part in line.Substring(line.IndexOf(':') + 1).Split(','))
                                foreach (string n in part.Split('/'))
                                {
                                    string name = n.Trim().TrimEnd('.');
                                    int dot = name.IndexOf('.');
                                    if (dot > 0) name = name.Substring(0, dot).Trim();
                                    string tip;
                                    if (text.TryGetValue(name, out tip)) Add(name, t, p, tip);
                                }
                        }
                    }
                }
            }
            // every talent of every class: its text from the game's tree; a click opens its class's page
            int classesTopic = topics.FindIndex(delegate(GuideTopic g) { return g.Title == "Classes"; });
            classesAt = classesTopic;
            // a talent name found in more than one class's tree (Flurry: Warrior and Shaman) belongs to no single class:
            // on a class page it is that class's talent (ForPage); anywhere else its hover shows every class's version
            var byName = new Dictionary<string, List<Talent>>(StringComparer.Ordinal);
            foreach (var tal in Talents.All())
            {
                List<Talent> l;
                if (!byName.TryGetValue(tal.Name, out l)) byName[tal.Name] = l = new List<Talent>();
                if (!l.Exists(delegate(Talent x) { return x.Class == tal.Class; })) l.Add(tal);
            }
            shared = new Dictionary<string, List<Talent>>(StringComparer.Ordinal);
            foreach (var kv in byName) if (kv.Value.Count > 1) shared[kv.Key] = kv.Value;
            foreach (var tal in Talents.All())
            {
                int cp = classesTopic < 0 ? -1 : topics[classesTopic].Pages.FindIndex(delegate(GuidePage g) { return g.Title == tal.Class; });
                if (!links.ContainsKey(tal.Name))
                {
                    Add(tal.Name, classesTopic, cp, Talents.HoverText(tal));
                    GuideLink tl;
                    if (links.TryGetValue(tal.Name, out tl)) tl.Class = tal.Class;
                }
            }
            foreach (var kv in shared)
            {
                GuideLink tl;
                if (!links.TryGetValue(kv.Key, out tl) || tl.Class == null) continue;   // a spell or page of that name wins
                var tips = new List<string>();
                foreach (var tal in kv.Value) tips.Add(Lang.T(tal.Class) + ": " + Talents.HoverText(tal));
                tl.Tip = Lang.T("A talent of more than one class") + "\n\n" + string.Join("\n\n", tips.ToArray());
            }
            // every Transmog look: how to earn it; a real item's hover also shows its 3D model picture (Wowhead's model viewer)
            int tmTopic = topics.FindIndex(delegate(GuideTopic g) { return g.Title == "Transmog"; });
            try
            {
                foreach (var look in Transmog.All)
                {
                    string pageName = look.Kind == "appearance" ? "Appearances" : look.Kind == "tabard" ? "Tabards" : "Weapon Glows";
                    int tp = tmTopic < 0 ? -1 : topics[tmTopic].Pages.FindIndex(delegate(GuidePage g) { return g.Title == pageName; });
                    Add(look.Name, tmTopic, tp, look.How);
                }
            }
            catch (Exception) { }
            // the theorycrafting tools: a click opens them (-2 the Talent Tree Builder, -3 the Gear Planner)
            Add("Talent Tree Builder", -2, -1, "Pick a class and try out a build of up to 51 points on its three talent trees.");
            Add("Gear Planner", -3, -1, "Put gear and jewelry on a character sheet like the game's and see what they add.");
            // every item: gear and ordinary jewelry link to their guide; Codex links point to class-guide passages
            int itemsTopic = topics.FindIndex(delegate(GuideTopic g) { return g.Title == "Items"; });
            if (itemsTopic >= 0)
            {
                var pages = topics[itemsTopic].Pages;
                try
                {
                    foreach (var piece in Items.Gear)
                    {
                        string cls = piece.Class + " Gear";
                        int pg = pages.FindIndex(delegate(GuidePage g) { return g.Title == cls; });
                        if (!links.ContainsKey(piece.Name)) Add(piece.Name, itemsTopic, pg, Items.Card(piece));
                    }
                    int jp = pages.FindIndex(delegate(GuidePage g) { return g.Title.StartsWith("Rings"); });
                    int codexClassesTopic = topics.FindIndex(delegate(GuideTopic g) { return g.Title == "Classes"; });
                    foreach (var j in Items.Jewelry) if (j.IsCodex && !links.ContainsKey(j.Name) && codexClassesTopic >= 0)
                    {
                        int classPage = topics[codexClassesTopic].Pages.FindIndex(delegate(GuidePage g) { return g.Title == j.Class; });
                        if (classPage >= 0) Add(j.Name, codexClassesTopic, classPage, Items.Card(j));
                    }
                    foreach (var j in Items.Jewelry) if (!j.IsCodex && !links.ContainsKey(j.Name)) Add(j.Name, itemsTopic, jp, Items.Card(j));
                }
                catch (Exception) { }
            }
            // races, racials and zones: the game's own words; a click opens their topic
            foreach (var kv in text)
            {
                if (Array.IndexOf(TreeWords, kv.Key) >= 0) continue;
                if (links.ContainsKey(kv.Key)) { if (links[kv.Key].Tip.Length == 0) links[kv.Key].Tip = kv.Value; continue; }
                bool zone = kv.Value.Length > 0 && Array.IndexOf(new[] { "Elwynn Forest", "Duskwood", "Warsong Gulch", "Arathi Basin",
                    "Feralas", "Gurubashi Arena", "Alterac Valley", "Blackrock Mountain", "Gates of Ahn'Qiraj", "Un'Goro Crater",
                    "Scarlet Monastery", "Naxxramas" }, kv.Key) >= 0;
                Add(kv.Key, zone ? zonesTopic : racesTopic, -1, kv.Value);
            }
            foreach (string w in TreeWords) links.Remove(w);
            // if a game-captured tooltip provider is supplied, its own words replace the guide's line about that spell
            Dictionary<string, string> seen = null;
            try { if (CapturedTips != null) seen = CapturedTips(); } catch (Exception) { }
            if (seen != null)
                foreach (var kv in seen)
                {
                    GuideLink l;
                    if (links.TryGetValue(kv.Key, out l)) l.Tip = kv.Value;
                    else Add(kv.Key, -1, -1, kv.Value);
                }
            // Keep each canonical destination, but let localized Guide text match its translated
            // spell, talent, item, page, or topic name and route through the same destination.
            var canonical = new List<KeyValuePair<string, GuideLink>>(links);
            foreach (var pair in canonical)
            {
                string localized = Lang.T(pair.Key);
                if (localized.Length >= 4 && !string.Equals(localized, pair.Key, StringComparison.OrdinalIgnoreCase) && !links.ContainsKey(localized))
                    links[localized] = pair.Value;
            }
            byKey = new Dictionary<string, GuideLink>(StringComparer.Ordinal);
            foreach (var l in links.Values) if (!byKey.ContainsKey(Norm(l.Term))) byKey[Norm(l.Term)] = l;
            var terms = new List<string>(links.Keys);
            // the bosses' names are found too, but link nowhere: "Mr. Smite" stays plain instead of linking Smite
            foreach (string boss in Items.BossNames()) if (!links.ContainsKey(boss) && !terms.Contains(boss)) terms.Add(boss);
            terms.Sort(delegate(string a, string b) { return b.Length.CompareTo(a.Length); });   // longest first
            // a name is found only as written (its possessive and plural too: "Ignite's", "Wraths"), so "fear" or
            // "execute" in a sentence stays plain text
            var parts = new List<string>();
            foreach (string term in terms) parts.Add(Regex.Escape(term));
            finder = parts.Count == 0 ? null
                : new Regex(@"(?<![A-Za-z'])(" + string.Join("|", parts.ToArray()) + @")(?:'s|s)?(?![A-Za-z])");
        }

        static GuideLink Find(string matched)
        {
            GuideLink l;
            if (links.TryGetValue(matched, out l)) return l;
            string k = Norm(matched);
            if (byKey.TryGetValue(k, out l)) return l;
            return null;
        }

        // what the game (or, until a captured tooltip is available, the guide) says a name does; null if nothing
        // the link is the page's (or topic's) own title: it goes to that page itself
        public static bool IsPage(GuideLink l)
        {
            try
            {
                var topics = Data.Guide;
                if (l.Topic < 0 || l.Topic >= topics.Count) return false;
                string title = l.Page >= 0 && l.Page < topics[l.Topic].Pages.Count ? topics[l.Topic].Pages[l.Page].Title : topics[l.Topic].Title;
                return title == l.Term;
            }
            catch (Exception) { return false; }
        }

        public static string GameTip(string name)
        {
            if (links == null) Build();
            GuideLink l = Find(name ?? "");
            return l != null && l.Tip.Length > 0 ? l.Tip : null;
        }

        static string Capital(string s) { return s.Length == 0 ? s : char.ToUpperInvariant(s[0]) + s.Substring(1); }

        // a shared talent's link on a class page: that class's own talent (its page, its text); otherwise unchanged
        static GuideLink ForPage(GuideLink l, int topic, int page)
        {
            List<Talent> list;
            if (l == null || l.Class == null || shared == null || topic != classesAt || topic < 0 || !shared.TryGetValue(l.Term, out list)) return l;
            string cls;
            try { cls = Data.Guide[topic].Pages[page].Title; }
            catch (Exception) { return l; }
            foreach (var tal in list)
                if (tal.Class == cls)
                    return new GuideLink { Term = l.Term, FindText = l.FindText, Topic = topic, Page = page, Class = cls, Tip = Talents.HoverText(tal) };
            return l;
        }

        // the same, on a page of the Guide: a talent name several classes share follows the class page it is on
        public static List<KeyValuePair<string, GuideLink>> Split(string text, int topic, int page)
        {
            var runs = Split(text);
            for (int i = 0; i < runs.Count; i++)
                if (runs[i].Value != null) runs[i] = new KeyValuePair<string, GuideLink>(runs[i].Key, ForPage(runs[i].Value, topic, page));
            return runs;
        }

        // the text cut into plain runs and links: (text, link or null)
        public static List<KeyValuePair<string, GuideLink>> Split(string text)
        {
            if (links == null) Build();
            var outList = new List<KeyValuePair<string, GuideLink>>();
            int at = 0;
            if (finder != null)
                foreach (Match m in finder.Matches(text))
                {
                    GuideLink l = Find(m.Groups[1].Value);
                    if (l == null) continue;
                    if (m.Index > at) outList.Add(new KeyValuePair<string, GuideLink>(text.Substring(at, m.Index - at), null));
                    outList.Add(new KeyValuePair<string, GuideLink>(m.Value, l));
                    at = m.Index + m.Length;
                }
            if (at < text.Length) outList.Add(new KeyValuePair<string, GuideLink>(text.Substring(at), null));
            return outList;
        }
    }

    // A guide paragraph or bullet with its links: the linked names are drawn in gold and underlined; a click goes to
    // their page (not to the page it is already on), and pointing at one shows what it does.
    public class GuideText : Control
    {
        readonly List<KeyValuePair<string, GuideLink>> runs;
        readonly List<KeyValuePair<RectangleF, GuideLink>> hot = new List<KeyValuePair<RectangleF, GuideLink>>();
        readonly int here, herePage;                         // the page it is on
        readonly Font font, linkFont;
        readonly int wrap;
        GuideLink over;
        int focusLink = -1;
        readonly List<RectangleF> findRects = new List<RectangleF>();
        int findSelected = -1;
        bool findHighlightAll = true;

        public GuideText(string text, float size, Color fg, Color bg, int wrapWidth, int topic, int page)
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer |
                     ControlStyles.ResizeRedraw, true);
            SetStyle(ControlStyles.Selectable, true);
            runs = GuideLinks.Split(Lang.T(text), topic, page);
            here = topic;
            herePage = page;
            font = Theme.F(size);
            linkFont = Theme.F(size, FontStyle.Underline);
            ForeColor = fg;
            BackColor = bg;
            wrap = wrapWidth;
            AccessibleRole = AccessibleRole.Link;
            AccessibleName = Lang.T("Guide links");
            AccessibleDescription = Lang.T("Use the arrow keys to browse links and Enter to open one.");
            TabStop = true;
            Flow(null);
        }

        // every link goes somewhere, a spell on this same page too (to its row); only a page's link to itself does not
        bool Clickable(GuideLink l) { return l != null && (l.Topic >= 0 || l.Topic <= -2) && !(l.Topic == here && l.Page == herePage && GuideLinks.IsPage(l)); }

        static readonly Bitmap measureBmp = new Bitmap(1, 1);

        // the words, laid out once: where each one goes (a repaint, while scrolling for example, only draws them)
        struct Placed { public string Word; public float X, Y, W; public GuideLink Link; }
        readonly List<Placed> placed = new List<Placed>();

        // lays the words out (measured and drawn the same way, GDI+ typographic, so the spaces and the links sit
        // exactly where the text says) and sizes the control
        int Flow(Graphics unused)
        {
            hot.Clear();
            placed.Clear();
            using (Graphics g = Graphics.FromImage(measureBmp))
            {
                g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;   // as the guide's other text
                using (var sf = (StringFormat)StringFormat.GenericTypographic.Clone())
                {
                    sf.FormatFlags |= StringFormatFlags.MeasureTrailingSpaces | StringFormatFlags.NoWrap;
                    float x = 0, y = 0;
                    int lineH = font.Height + 1;
                    float spaceW = g.MeasureString(" ", font, PointF.Empty, sf).Width * 1.15f;
                    foreach (var run in runs)
                    {
                        GuideLink link = run.Value;
                        Font f = link != null ? linkFont : font;
                        // a link stays in one piece when it fits on a line; plain text wraps word by word
                        string[] words = link != null ? new[] { run.Key } : Regex.Split(run.Key, "( )");
                        foreach (string word in words)
                        {
                            if (word.Length == 0) continue;
                            if (word == " ") { if (x > 0) x += spaceW; continue; }
                            float w = g.MeasureString(word, f, PointF.Empty, sf).Width;
                            if (x > 0 && x + w > wrap) { x = 0; y += lineH; }
                            var pw = new Placed();
                            pw.Word = word; pw.X = x; pw.Y = y; pw.W = w; pw.Link = link;
                            placed.Add(pw);
                            if (link != null) hot.Add(new KeyValuePair<RectangleF, GuideLink>(new RectangleF(x, y, w, lineH), link));
                            x += w;
                        }
                    }
                    int h = (int)Math.Ceiling(y) + lineH;
                    Size = new Size(wrap, h);
                    TabStop = false;
                    foreach (var target in hot)
                        if (Clickable(target.Value)) { TabStop = true; break; }
                    return h;
                }
            }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.Clear(BackColor);
            g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;
            int lineH = font.Height + 1;
            using (var all = new SolidBrush(Color.FromArgb(105, 143, 112, 240)))
            using (var current = new SolidBrush(Color.FromArgb(190, 255, 204, 80)))
            {
                for (int i = 0; i < findRects.Count; i++)
                    if (findHighlightAll || i == findSelected)
                    {
                        RectangleF r = findRects[i];
                        if (r.IntersectsWith(e.ClipRectangle)) g.FillRectangle(i == findSelected ? current : all, r);
                    }
            }
            using (var sf = (StringFormat)StringFormat.GenericTypographic.Clone())
            using (var plain = new SolidBrush(ForeColor))
            using (var gold = new SolidBrush(Theme.Hex("#e8b84a")))
            using (var lit = new SolidBrush(Theme.Hex("#fff2b0")))
            {
                sf.FormatFlags |= StringFormatFlags.MeasureTrailingSpaces | StringFormatFlags.NoWrap;
                foreach (var pw in placed)
                {
                    if (pw.Y + lineH < e.ClipRectangle.Top || pw.Y > e.ClipRectangle.Bottom) continue;   // out of sight
                    Brush b = pw.Link == null ? plain : pw.Link == over ? lit : gold;
                    g.DrawString(pw.Word, pw.Link != null ? linkFont : font, b, pw.X, pw.Y, sf);
                }
            }
            if (Focused && focusLink >= 0 && focusLink < hot.Count)
            {
                RectangleF r = hot[focusLink].Key;
                using (var p = new Pen(Theme.Hex("#fff2b0"), 1) { DashStyle = System.Drawing.Drawing2D.DashStyle.Dot })
                    g.DrawRectangle(p, r.X - 2, r.Y, r.Width + 3, r.Height - 1);
            }
        }

        // Search positions use the same measured word runs that are painted and clicked, so matches stay attached
        // to their passage when text wraps or the article scrolls.
        public void SetFind(string query, bool matchCase, bool matchDiacritics, bool wholeWords, bool highlightAll)
        {
            findRects.Clear();
            findSelected = -1;
            findHighlightAll = highlightAll;
            query = (query ?? "").Trim();
            if (query.Length == 0) { Invalidate(); return; }
            string foldedQuery = Fold(query, matchCase, matchDiacritics, null);
            if (foldedQuery.Length == 0) { Invalidate(); return; }
            StringComparison comparison = StringComparison.Ordinal;
            foreach (Placed pw in placed)
            {
                Font f = pw.Link != null ? linkFont : font;
                var sourceMap = new List<int>();
                string foldedWord = Fold(pw.Word, matchCase, matchDiacritics, sourceMap);
                int at = 0;
                while (at <= foldedWord.Length - foldedQuery.Length)
                {
                    int found = foldedWord.IndexOf(foldedQuery, at, comparison);
                    if (found < 0) break;
                    int originalStart = sourceMap[found];
                    int originalEnd = sourceMap[found + foldedQuery.Length - 1] + 1;
                    bool whole = !wholeWords ||
                        ((originalStart == 0 || !char.IsLetterOrDigit(pw.Word[originalStart - 1])) &&
                         (originalEnd == pw.Word.Length || !char.IsLetterOrDigit(pw.Word[originalEnd])));
                    if (whole)
                    {
                        float before = Measure(pw.Word.Substring(0, originalStart), f);
                        float width = Measure(pw.Word.Substring(originalStart, originalEnd - originalStart), f);
                        findRects.Add(new RectangleF(pw.X + before, pw.Y, Math.Max(2, width), font.Height + 1));
                    }
                    at = found + Math.Max(1, foldedQuery.Length);
                }
            }
            Invalidate();
        }

        static string Fold(string value, bool matchCase, bool matchDiacritics, List<int> sourceMap)
        {
            var result = new StringBuilder();
            for (int i = 0; i < value.Length; i++)
            {
                string part = value[i].ToString();
                if (!matchDiacritics) part = part.Normalize(NormalizationForm.FormD);
                foreach (char ch in part)
                {
                    if (!matchDiacritics && CharUnicodeInfo.GetUnicodeCategory(ch) == UnicodeCategory.NonSpacingMark) continue;
                    result.Append(matchCase ? ch : char.ToUpperInvariant(ch));
                    if (sourceMap != null) sourceMap.Add(i);
                }
            }
            return result.ToString();
        }

        static float Measure(string value, Font f)
        {
            if (value.Length == 0) return 0;
            using (Graphics g = Graphics.FromImage(measureBmp))
            using (var sf = (StringFormat)StringFormat.GenericTypographic.Clone())
            {
                sf.FormatFlags |= StringFormatFlags.MeasureTrailingSpaces | StringFormatFlags.NoWrap;
                return g.MeasureString(value, f, PointF.Empty, sf).Width;
            }
        }

        public int FindCount { get { return findRects.Count; } }
        public RectangleF FindBounds(int index) { return index >= 0 && index < findRects.Count ? findRects[index] : RectangleF.Empty; }
        public void SelectFind(int index) { findSelected = index; Invalidate(); }

        GuideLink At(Point p)
        {
            foreach (var h in hot) if (h.Key.Contains(p)) return h.Value;
            return null;
        }

        int AtIndex(Point p)
        {
            for (int i = 0; i < hot.Count; i++) if (Clickable(hot[i].Value) && hot[i].Key.Contains(p)) return i;
            return -1;
        }

        void FocusLink(int index)
        {
            focusLink = index;
            over = index >= 0 && index < hot.Count ? hot[index].Value : null;
            if (over != null)
            {
                AccessibleName = Lang.T(over.Term);
                AccessibleDescription = Lang.T(over.Tip);
                if (over.Tip.Length > 0) GuideHover.Show(this, over.Term, over.Tip, null, SpellIcons.HasItemModel(over.Term) ? over.Term : null);
            }
            else GuideHover.Hide(this);
            Invalidate();
        }

        protected override void OnGotFocus(EventArgs e)
        {
            base.OnGotFocus(e);
            for (int i = 0; i < hot.Count; i++) if (Clickable(hot[i].Value)) { FocusLink(i); return; }
        }

        protected override void OnLostFocus(EventArgs e)
        {
            focusLink = -1;
            over = null;
            AccessibleName = Lang.T("Guide links");
            GuideHover.Hide(this);
            Invalidate();
            base.OnLostFocus(e);
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Left || e.KeyCode == Keys.Up || e.KeyCode == Keys.Right || e.KeyCode == Keys.Down)
            {
                int direction = e.KeyCode == Keys.Left || e.KeyCode == Keys.Up ? -1 : 1;
                int start = focusLink;
                for (int step = 0; step < hot.Count; step++)
                {
                    int at = start < 0 ? (direction > 0 ? -1 : 0) : start;
                    at = (at + direction + hot.Count) % hot.Count;
                    if (Clickable(hot[at].Value)) { FocusLink(at); break; }
                    start = at;
                }
                if (hot.Count > 0) { e.Handled = e.SuppressKeyPress = true; return; }
            }
            if ((e.KeyCode == Keys.Enter || e.KeyCode == Keys.Space) && focusLink >= 0 && focusLink < hot.Count)
            {
                GuideLink link = hot[focusLink].Value;
                if (Clickable(link) && GuideLinks.Navigate != null)
                {
                    GuideHover.Hide(this);
                    WowButton.Fire(delegate { GuideLinks.Navigate(link); });
                }
                e.Handled = e.SuppressKeyPress = true;
                return;
            }
            base.OnKeyDown(e);
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            GuideLink l = At(e.Location);
            if (l == over)
            {
                if (Focused)
                {
                    int index = AtIndex(e.Location);
                    if (index != focusLink) { focusLink = index; Invalidate(); }
                }
                return;
            }
            over = l;
            if (Focused && Clickable(l))
            {
                focusLink = AtIndex(e.Location);
                AccessibleName = Lang.T(l.Term);
                AccessibleDescription = Lang.T(l.Tip);
            }
            Cursor = Clickable(l) ? Cursors.Hand : Cursors.Default;
            Invalidate();
            if (l != null && l.Tip.Length > 0) GuideHover.Show(this, l.Term, l.Tip, null, SpellIcons.HasItemModel(l.Term) ? l.Term : null);
            else GuideHover.Hide();
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            base.OnMouseLeave(e);
            if (!Focused && over != null) { over = null; Invalidate(); }
            Cursor = Cursors.Default;
            if (!Focused) GuideHover.Hide(this);
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            GuideLink l = At(e.Location);
            if (e.Button != MouseButtons.Left || !Clickable(l) || GuideLinks.Navigate == null) return;
            GuideHover.Hide();
            GuideLinks.Navigate(l);
        }
    }

    // the hover box: the name in gold, then what it does, in the launcher's dark and gold
    // A non-clickable question mark with the same gold bevel and themed hover panel as the global help.
    public class HoverHelpButton : Control
    {
        readonly string title, body;
        public HoverHelpButton(string title, string body)
        {
            this.title = title ?? "";
            this.body = body ?? "";
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer |
                     ControlStyles.ResizeRedraw, true);
            SetStyle(ControlStyles.Selectable, true);
            Size = new Size(Math.Max(20, (int)(24 * Theme.Zoom)), Math.Max(16, (int)(18 * Theme.Zoom)));
            Cursor = Cursors.Help;
            BackColor = Theme.Hex("#1c1a26");
            AccessibleRole = AccessibleRole.PushButton;
            AccessibleName = Lang.T(title);
            AccessibleDescription = Lang.T(body);
            TabStop = true;
        }
        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.Clear(BackColor);
            using (var b = new SolidBrush(Theme.Hex("#1c1a26"))) g.FillRectangle(b, ClientRectangle);
            using (var p = new Pen(Theme.Hex("#d6a640"), 2)) g.DrawRectangle(p, 1, 1, Width - 3, Height - 3);
            if (Focused)
                using (var p = new Pen(Theme.Hex("#fff2b0"), 1) { DashStyle = System.Drawing.Drawing2D.DashStyle.Dot })
                    g.DrawRectangle(p, 4, 4, Math.Max(1, Width - 9), Math.Max(1, Height - 9));
            TextRenderer.DrawText(g, "?", Theme.F(10, FontStyle.Bold), ClientRectangle, Theme.Hex("#f0c050"),
                                  TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);
        }
        protected override void OnMouseEnter(EventArgs e)
        {
            base.OnMouseEnter(e);
            GuideHover.ShowHelp(this, Lang.T(title), Lang.T(body));
        }
        protected override void OnGotFocus(EventArgs e)
        {
            base.OnGotFocus(e);
            GuideHover.ShowHelp(this, Lang.T(title), Lang.T(body));
            Invalidate();
        }
        protected override void OnLostFocus(EventArgs e)
        {
            GuideHover.Hide(this);
            Invalidate();
            base.OnLostFocus(e);
        }
        protected override void OnMouseLeave(EventArgs e)
        {
            if (!Focused) GuideHover.Hide(this);
            base.OnMouseLeave(e);
        }
    }

    public class GuideHover : Control
    {
        static GuideHover box;
        static Form hoverHost;
        Control hoverOwner;
        string title = "", body = "";
        static readonly Bitmap measure = new Bitmap(1, 1);
        string splitFor;                                      // the body (and title) the runs below were cut from
        List<KeyValuePair<string, GuideLink>> runs;

        // the body, word by word: the other spells, talents and names the guide knows are drawn in gold (not
        // clickable: the box goes away when the mouse moves to it); returns the height
        int Flow(Graphics g, Font f, bool draw)
        {
            if (splitFor != title + "\u0001" + body)
            {
                splitFor = title + "\u0001" + body;
                runs = new List<KeyValuePair<string, GuideLink>>();
                foreach (var r in GuideLinks.Split(body))
                    runs.Add(r.Value != null && string.Equals(r.Value.Term, title, StringComparison.OrdinalIgnoreCase)
                             ? new KeyValuePair<string, GuideLink>(r.Key, null) : r);
            }
            g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;
            int wrap = W - 20, lineH = f.Height + 1;
            float x = 0, y = 0;
            using (var sf = (StringFormat)StringFormat.GenericTypographic.Clone())
            using (var plain = new SolidBrush(Theme.Text))
            using (var gold = new SolidBrush(Theme.Hex("#e8b84a")))
            {
                sf.FormatFlags |= StringFormatFlags.MeasureTrailingSpaces | StringFormatFlags.NoWrap;
                float spaceW = g.MeasureString(" ", f, PointF.Empty, sf).Width * 1.15f;
                foreach (var run in runs)
                {
                    bool hl = run.Value != null;
                    foreach (string word in hl ? new[] { run.Key } : Regex.Split(run.Key, "( |\n)"))
                    {
                        if (word.Length == 0) continue;
                        if (word == "\n") { x = 0; y += lineH; continue; }
                        if (word == " ") { if (x > 0) x += spaceW; continue; }
                        float w = g.MeasureString(word, f, PointF.Empty, sf).Width;
                        if (x > 0 && x + w > wrap) { x = 0; y += lineH; }
                        if (draw) g.DrawString(word, f, hl ? gold : plain, x, y, sf);
                        x += w;
                    }
                }
            }
            return (int)Math.Ceiling(y) + lineH;
        }
        Image icon;                                           // the spell's or talent's icon, beside its name
        string model;                                         // a real item's name: its model picture under the text
        bool modelLayout;
        Image picture, shownPicture;                          // a picture under the text (ShowPicture)
        const int W = 300, ModelH = 230;

        GuideHover()
        {
            // Keep hover cards in the launcher's client area instead of creating another top-level
            // window. On mixed-DPI desktops, screen-coordinate conversion for owned popup windows
            // can put the card on a different monitor from the launcher.
            TabStop = false;
            BackColor = Theme.Hex("#120f18");
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer, true);
        }

        protected override CreateParams CreateParams
        {
            get
            {
                CreateParams cp = base.CreateParams;
                cp.ExStyle |= 0x08000000 | 0x00000080;       // no activation, no Alt+Tab entry
                return cp;
            }
        }

        public static void Show(Control owner, string title, string body) { Show(owner, title, body, null); }

        public static void Show(Control owner, string title, string body, Image icon) { Show(owner, title, body, icon, null); }

        // a note with a picture under its text (the "Lost?" note's example path)
        public static void ShowPicture(Control owner, string title, string body, Image picture)
        {
            if (box == null || box.IsDisposed) box = new GuideHover();
            box.picture = picture;
            ShowCore(owner, title, body, null, null, false);
        }

        public static void ShowPictureAt(Control owner, string title, string body, Image picture, Point screenAnchor)
        {
            if (box == null || box.IsDisposed) box = new GuideHover();
            box.picture = picture;
            ShowCore(owner, title, body, null, null, false, screenAnchor);
        }

        public static void ShowHelp(Control owner, string title, string body)
        {
            ShowCore(owner, title, body, null, null, false);
        }

        static void HookHost(Control owner)
        {
            Form host = RootHost(owner);
            if (host == hoverHost) return;
            if (hoverHost != null)
            {
                hoverHost.Deactivate -= HoverHostDeactivated;
                hoverHost.FormClosed -= HoverHostClosed;
                hoverHost.VisibleChanged -= HoverHostVisibilityChanged;
                hoverHost.Resize -= HoverHostResized;
                hoverHost.Move -= HoverHostMoved;
            }
            hoverHost = host;
            if (hoverHost != null)
            {
                hoverHost.Deactivate += HoverHostDeactivated;
                hoverHost.FormClosed += HoverHostClosed;
                hoverHost.VisibleChanged += HoverHostVisibilityChanged;
                hoverHost.Resize += HoverHostResized;
                hoverHost.Move += HoverHostMoved;
            }
        }

        // Controls in the section surface live in a non-top-level Form nested in the launcher.
        // Walk both parent and owner links so a hover card is always attached to a real window.
        static Form RootHost(Control owner)
        {
            Control current = owner;
            var seen = new HashSet<Control>();
            while (current != null && seen.Add(current))
            {
                Form form = current as Form;
                if (form != null)
                {
                    if (form.TopLevel) return form;
                    // A child Form can be parented by a Panel, not another Form. Keep walking
                    // through that control tree instead of dropping the hover owner here.
                    if (form.Parent != null) { current = form.Parent; continue; }
                    if (form.Owner != null) { current = form.Owner; continue; }
                }
                current = current.Parent;
            }
            return null;
        }

        static void HoverHostDeactivated(object sender, EventArgs e) { Hide(); }

        static void HoverHostClosed(object sender, FormClosedEventArgs e)
        {
            Hide();
            HookHost(null);
        }

        static void HoverHostVisibilityChanged(object sender, EventArgs e)
        {
            Form host = sender as Form;
            if (host != null && !host.Visible) Hide();
        }

        static void HoverHostResized(object sender, EventArgs e)
        {
            Hide();
        }

        static void HoverHostMoved(object sender, EventArgs e) { Hide(); }

        // Delayed hovers must not appear after their window loses focus (for example during Alt+Tab).
        public static bool CanShow(Control owner)
        {
            if (owner == null || owner.IsDisposed || !owner.Visible) return false;
            Form host = RootHost(owner);
            return host != null && host.Visible && host.WindowState != FormWindowState.Minimized &&
                   (host.ContainsFocus || Form.ActiveForm == host);
        }

        public static void ShowTip(Control owner, string text)
        {
            string body = (text ?? "").Trim();
            if (body.Length == 0) { Hide(owner); return; }
            string title = "";
            int split = body.IndexOf('\n');
            if (split <= 0 || split > 42)
            {
                split = body.IndexOf(':');
                if (split <= 0 || split > 32) split = -1;
            }
            if (split > 0)
            {
                title = body.Substring(0, split).Trim();
                body = body.Substring(split + 1).Trim();
            }
            ShowCore(owner, title, body, null, null, false);
        }

        // model: a real item's name (assets\\item-models.txt): its 3D model picture from Wowhead's model viewer is shown
        // under the text (it loads the first time, then comes from the cache)
        public static void Show(Control owner, string title, string body, Image icon, string model)
        {
            ShowCore(owner, title, body, icon, model, true);
        }

        static void ShowCore(Control owner, string title, string body, Image icon, string model, bool applyGear)
        {
            ShowCore(owner, title, body, icon, model, applyGear, null);
        }

        static void ShowCore(Control owner, string title, string body, Image icon, string model, bool applyGear, Point? screenAnchor)
        {
            if (!CanShow(owner)) { Hide(owner); return; }
            if (box == null || box.IsDisposed) box = new GuideHover();
            HookHost(owner);
            Form host = RootHost(owner);
            if (host == null) { Hide(); return; }
            // Parent the card to the actual launcher HWND. Its position is then expressed in the
            // same client-coordinate space as the launcher, even when monitors use different DPI.
            if (box.Parent != host)
            {
                if (box.Visible) box.HideCard();
                if (box.Parent != null) box.Parent.Controls.Remove(box);
                host.Controls.Add(box);
            }
            box.icon = icon;
            box.model = model;
            Image modelImage = model == null ? null : SpellIcons.ItemModel(model, box);
            box.modelLayout = modelImage != null;
            Image pic = box.picture;
            box.picture = null;                               // a picture is for the one note that passed it
            box.shownPicture = pic;
            box.title = title;
            box.hoverOwner = owner;
            if (applyGear) body = GearEffect.Apply(title, body); // only content hovers get the planner's live totals
            if (model != null || IsItemTitle(title)) body = ShortItemExplanation(body);
            box.body = body;
            Font tf = Theme.F(10.5f, FontStyle.Bold), bf = Theme.F(9.5f);
            int th = title.Length == 0 ? 0 : TextRenderer.MeasureText(title, tf, new Size(W - 20 - (icon != null ? 40 : 0), 0), TextFormatFlags.WordBreak | TextFormatFlags.NoPrefix).Height;
            if (icon != null) th = Math.Max(th, 36);
            int bh;
            using (var mg = Graphics.FromImage(measure)) bh = box.Flow(mg, bf, false);
            Rectangle ownerBounds = host.RectangleToScreen(host.ClientRectangle);
            if (ownerBounds.Width < 40 || ownerBounds.Height < 40) { Hide(); return; }
            Point p;
            if (screenAnchor.HasValue) p = screenAnchor.Value;
            else if (owner.ClientRectangle.Contains(owner.PointToClient(Cursor.Position)) && owner.IsHandleCreated) p = Cursor.Position;
            else p = owner.PointToScreen(new Point(owner.ClientSize.Width / 2, owner.ClientSize.Height / 2));

            // Confine every tooltip to its launcher window. Clamping to the monitor's working area alone lets
            // bottom-edge hovers spill over other apps (the launcher can sit above another window).
            const int margin = 6;
            int availableWidth = ownerBounds.Width - margin * 2;
            int availableHeight = ownerBounds.Height - margin * 2;
            int width = Math.Min(W, availableWidth);
            int height = Math.Min(th + bh + 22 + (box.modelLayout ? ModelH + 8 : 0) + (pic != null ? pic.Height + 10 : 0), availableHeight);
            var size = new Size(width, height);
            int x = p.X + 16, y = p.Y + 20;
            if (x + size.Width > ownerBounds.Right - margin) x = p.X - size.Width - 8;
            if (y + size.Height > ownerBounds.Bottom - margin) y = p.Y - size.Height - 8;
            x = Math.Max(ownerBounds.Left + margin, Math.Min(x, ownerBounds.Right - size.Width - margin));
            y = Math.Max(ownerBounds.Top + margin, Math.Min(y, ownerBounds.Bottom - size.Height - margin));
            box.Bounds = host.RectangleToClient(new Rectangle(x, y, size.Width, size.Height));
            box.Invalidate();
            if (!box.Visible) box.Show();
            box.BringToFront();
        }

        public static new void Hide()
        {
            if (box != null && !box.IsDisposed && box.Visible) box.HideCard();
            if (box != null) box.hoverOwner = null;
        }

        void HideCard() { base.Hide(); }

        static bool IsItemTitle(string title)
        {
            if (string.IsNullOrEmpty(title)) return false;
            foreach (var item in Items.Gear) if (string.Equals(item.Name, title, StringComparison.OrdinalIgnoreCase)) return true;
            foreach (var item in Items.Jewelry) if (string.Equals(item.Name, title, StringComparison.OrdinalIgnoreCase)) return true;
            foreach (var item in Transmog.All) if (string.Equals(item.Name, title, StringComparison.OrdinalIgnoreCase)) return true;
            return false;
        }

        // Keep item cards readable: at most two complete sentences, or two short lines for stat-style cards.
        static string ShortItemExplanation(string value)
        {
            string body = (value ?? "").Trim();
            if (body.Length == 0) return body;
            int sentences = 0;
            for (int i = 0; i < body.Length; i++)
            {
                char c = body[i];
                if (c != '.' && c != '!' && c != '?') continue;
                if (c == '.' && i > 0 && i + 1 < body.Length && char.IsDigit(body[i - 1]) && char.IsDigit(body[i + 1])) continue;
                if (i + 1 < body.Length && !char.IsWhiteSpace(body[i + 1])) continue;
                if (++sentences == 2) return body.Substring(0, i + 1).Trim();
            }
            if (sentences == 0)
            {
                string[] lines = body.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
                if (lines.Length > 2) return lines[0].Trim() + "\n" + lines[1].Trim();
            }
            return body;
        }

        public static void Hide(Control owner)
        {
            if (box != null && !box.IsDisposed && box.hoverOwner == owner) Hide();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.Clear(BackColor);
            using (var p = new Pen(Theme.Hex("#c49a48"), 1)) g.DrawRectangle(p, 0, 0, Width - 1, Height - 1);
            Font tf = Theme.F(10.5f, FontStyle.Bold), bf = Theme.F(9.5f);
            int ix = icon != null ? 40 : 0;
            int th = title.Length == 0 ? 0 : TextRenderer.MeasureText(title, tf, new Size(W - 20 - ix, 0), TextFormatFlags.WordBreak | TextFormatFlags.NoPrefix).Height;
            if (icon != null)
            {
                th = Math.Max(th, 36);
                Theme.Draw(g, icon, new Rectangle(10, 8, 32, 32));
                using (var p = new Pen(Theme.Hex("#8a7040"), 1)) g.DrawRectangle(p, 10, 8, 32, 32);
            }
            if (title.Length > 0)
                TextRenderer.DrawText(g, title, tf, new Rectangle(10 + ix, icon != null ? 14 : 8, W - 20 - ix, th), Theme.Gold, BackColor,
                                      TextFormatFlags.WordBreak | TextFormatFlags.NoPrefix);   // "&" as itself
            var st = g.Save();
            g.TranslateTransform(10, 10 + th);
            Flow(g, bf, true);
            g.Restore(st);
            if (shownPicture != null)
                g.DrawImage(shownPicture, (Width - shownPicture.Width) / 2, Height - shownPicture.Height - 10, shownPicture.Width, shownPicture.Height);
            if (model != null)
            {
                Image pic = SpellIcons.ItemModel(model, this);
                if (pic != null)
                {
                    if (!modelLayout)
                    {
                        modelLayout = true;
                        int targetHeight = Height + ModelH + 8;
                        if (Parent != null)
                        {
                            targetHeight = Math.Min(targetHeight, Parent.ClientSize.Height - 12);
                            Top = Math.Max(6, Math.Min(Top, Parent.ClientSize.Height - targetHeight - 6));
                        }
                        Height = Math.Max(Height, targetHeight);
                        Invalidate();
                        return;
                    }
                    var r = new Rectangle(10, Height - ModelH - 10, W - 20, ModelH);
                    using (var b = new SolidBrush(Theme.Hex("#0b0910"))) g.FillRectangle(b, r);
                    float k = Math.Min((float)r.Width / pic.Width, (float)r.Height / pic.Height);
                    int w = (int)(pic.Width * k), h = (int)(pic.Height * k);
                    g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
                    g.DrawImage(pic, new Rectangle(r.X + (r.Width - w) / 2, r.Y + (r.Height - h) / 2, w, h));
                    using (var p = new Pen(Theme.Hex("#8a7040"), 1)) g.DrawRectangle(p, r);
                }
            }
        }
    }

    // All launcher tooltips share the GuideHover panel's colors, fonts, border and wrapping.
    public sealed class ThemedToolTip : IDisposable
    {
        readonly Dictionary<Control, string> text = new Dictionary<Control, string>();
        sealed class ItemHover { public string Name, Icon; }
        readonly Dictionary<Control, ItemHover> itemHover = new Dictionary<Control, ItemHover>();
        readonly Dictionary<Control, Timer> timers = new Dictionary<Control, Timer>();
        readonly Dictionary<Control, bool> showing = new Dictionary<Control, bool>();
        readonly HashSet<Control> hooked = new HashSet<Control>();
        public int AutoPopDelay = 5000, InitialDelay = 500, ReshowDelay = 100;

        public void SetToolTip(Control control, string value)
        {
            SetToolTip(control, value, null);
        }

        // Item hovers use the same themed card, plus the item icon and an available Wowhead model image.
        public void SetItemToolTip(Control control, string name, string value, string icon)
        {
            SetToolTip(control, value, string.IsNullOrEmpty(value) || string.IsNullOrEmpty(name)
                ? null : new ItemHover { Name = name, Icon = icon });
        }

        void SetToolTip(Control control, string value, ItemHover item)
        {
            if (control == null) return;
            string old;
            bool changed = !text.TryGetValue(control, out old) || old != value;
            ItemHover oldItem;
            bool hadItem = itemHover.TryGetValue(control, out oldItem);
            if (item == null) itemHover.Remove(control);
            else itemHover[control] = item;
            if (hadItem != (item != null) || (hadItem && item != null &&
                (!string.Equals(oldItem.Name, item.Name, StringComparison.Ordinal) ||
                 !string.Equals(oldItem.Icon, item.Icon, StringComparison.Ordinal)))) changed = true;
            if (string.IsNullOrEmpty(value))
            {
                text.Remove(control);
                Stop(control);
                GuideHover.Hide(control);
                itemHover.Remove(control);
                return;
            }
            text[control] = value;
            Hook(control);
            if (changed && IsUnderPointer(control)) Schedule(control);
        }

        public void Hide(Control control)
        {
            if (control == null) return;
            Stop(control);
            GuideHover.Hide(control);
        }

        void Hook(Control control)
        {
            if (!hooked.Add(control)) return;
            control.MouseEnter += delegate { Schedule(control); };
            control.MouseLeave += delegate { Hide(control); };
            control.Disposed += delegate
            {
                Stop(control);
                text.Remove(control);
                itemHover.Remove(control);
                hooked.Remove(control);
            };
        }

        static bool IsUnderPointer(Control control)
        {
            return !control.IsDisposed && control.IsHandleCreated &&
                   control.ClientRectangle.Contains(control.PointToClient(Cursor.Position));
        }

        void Schedule(Control control)
        {
            string value;
            if (!text.TryGetValue(control, out value) || value.Length == 0 || !IsUnderPointer(control) || !GuideHover.CanShow(control))
            {
                Stop(control);
                return;
            }
            Timer timer;
            if (!timers.TryGetValue(control, out timer))
            {
                timer = new Timer();
                timer.Tick += delegate
                {
                    bool isShowing;
                    if (showing.TryGetValue(control, out isShowing) && isShowing)
                    {
                        timer.Stop();
                        showing[control] = false;
                        GuideHover.Hide(control);
                        return;
                    }
                    timer.Stop();
                    string tip;
                    if (!control.IsDisposed && IsUnderPointer(control) && GuideHover.CanShow(control) && text.TryGetValue(control, out tip))
                    {
                        ItemHover item;
                        if (itemHover.TryGetValue(control, out item))
                        {
                            string model = SpellIcons.HasItemModel(item.Name) ? item.Name : null;
                            GuideHover.Show(control, Lang.T(item.Name), Lang.T(tip), SpellIcons.Icon(item.Icon, control), model);
                        }
                        else GuideHover.ShowTip(control, Lang.T(tip));
                        if (AutoPopDelay > 0)
                        {
                            showing[control] = true;
                            timer.Interval = AutoPopDelay;
                            timer.Start();
                        }
                    }
                };
                timers[control] = timer;
            }
            timer.Stop();
            showing[control] = false;
            timer.Interval = Math.Max(1, InitialDelay);
            timer.Start();
        }

        void Stop(Control control)
        {
            Timer timer;
            if (timers.TryGetValue(control, out timer)) timer.Stop();
            showing[control] = false;
        }

        public void Dispose()
        {
            foreach (Control control in hooked) GuideHover.Hide(control);
            foreach (var timer in timers.Values) timer.Dispose();
            timers.Clear();
            showing.Clear();
            text.Clear();
            itemHover.Clear();
            hooked.Clear();
        }
    }
}
