// The talent trees of every class, as the game has them (assets/talents.txt, read from the game at wow.aethus.pl by
// tools/game_talents.py), and the pieces the New Player Guide's class pages draw them with:
//   * TalentTreeView: one spec's tree, its talents' icons on the game's grid (7 rows, 4 columns) with their ranks,
//     the 10, 20 and 30 point milestones marked; pointing at a talent shows its text and the spells it changes
//   * SpellRow: one spell of the class, its icon, the guide's line about it, and the talents that change it, and the spells that name it
// The icons come from the same place as the STATS page's (Wowhead's image host, cached; SpellIcons.cs).
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Text.RegularExpressions;
using System.Windows.Forms;

namespace AzerothLauncher
{
    public class Talent
    {
        public string Key = "", Name = "", Icon = "", Text = "", Spec = "", Class = "";
        public int Row, Col, Ranks;
        public List<string> Changes = new List<string>();     // the class's spells its text names

        // the text as the game shows it, with the number that grows with each rank as "X"
        public string Shown { get { return Text.Replace("{v}%", "X%").Replace("{v2}%", "Y%").Replace("{v}", "X").Replace("{v2}", "Y"); } }
        public int Points { get { return Row * 5; } }          // points in the tree it needs (the milestones: 10, 20, 30)
        public string Id { get { return Spec + "/" + Key; } }  // unique within its class
        public bool Milestone { get { return Text.StartsWith("Milestone") || Text.StartsWith("Capstone"); } }
    }

    public class TalentTree
    {
        public string Class = "", Spec = "";
        public List<Talent> Talents = new List<Talent>();
    }

    public static class Talents
    {
        static List<TalentTree> trees;
        static Dictionary<string, string> abilities;           // ability key -> name, as the game names them

        // Stable key bridge used when an external gear code refers to a class spell by key.
        public static string AbilityKey(string abilityName)
        {
            Load();
            if (string.IsNullOrWhiteSpace(abilityName)) return null;
            foreach (var ability in abilities)
                if (string.Equals(ability.Value, abilityName.Trim(), StringComparison.OrdinalIgnoreCase)) return ability.Key;
            return null;
        }

        static void Load()
        {
            if (trees != null) return;
            trees = new List<TalentTree>();
            abilities = new Dictionary<string, string>(StringComparer.Ordinal);
            TalentTree cur = null;
            bool on = false, abil = false;
            foreach (string raw in Util.ReadResource("AS.talents.txt").Replace("\r\n", "\n").Split('\n'))
            {
                string line = raw.Trim();
                if (line == "===") { on = true; continue; }
                if (!on || line.Length == 0) continue;
                if (line.StartsWith("@tree "))
                {
                    string[] h = line.Substring(6).Split('|');
                    cur = new TalentTree();
                    cur.Class = h[0].Trim();
                    cur.Spec = h.Length > 1 ? h[1].Trim() : "";
                    trees.Add(cur);
                    abil = false;
                    continue;
                }
                if (line == "@abilities") { abil = true; continue; }
                string[] f = line.Split(new[] { " | " }, StringSplitOptions.None);
                if (abil)
                {
                    if (f.Length >= 2) abilities[f[0].Trim()] = f[1].Trim();
                    continue;
                }
                if (cur == null || f.Length < 7) continue;
                var t = new Talent();
                t.Key = f[0].Trim(); t.Name = f[1].Trim(); t.Icon = f[2].Trim().ToLowerInvariant();
                int.TryParse(f[3], out t.Row); int.TryParse(f[4], out t.Col); int.TryParse(f[5], out t.Ranks);
                t.Text = string.Join(" | ", f, 6, f.Length - 6).Trim().Replace("\\n", "\n");
                t.Spec = cur.Spec; t.Class = cur.Class;
                cur.Talents.Add(t);
            }
            // which spells each talent changes: the abilities its text names
            var names = new List<string>(abilities.Values);
            names.Sort(delegate(string a, string b) { return b.Length.CompareTo(a.Length); });
            foreach (var tr in trees)
                foreach (var t in tr.Talents)
                {
                    string text = t.Text;
                    foreach (string n in names)
                    {
                        if (n.Length < 4) continue;
                        if (!Regex.IsMatch(text, @"(?<![A-Za-z])" + Regex.Escape(n) + @"(?:'?s)?(?![A-Za-z])")) continue;
                        if (!t.Changes.Contains(n)) t.Changes.Add(n);
                        text = Regex.Replace(text, Regex.Escape(n), "");   // "Shadow Bolt" is not also "Shadow ..."
                    }
                }
        }

        public static List<TalentTree> Of(string className)
        {
            Load();
            return trees.FindAll(delegate(TalentTree t) { return string.Equals(t.Class, className, StringComparison.OrdinalIgnoreCase); });
        }

        public static IEnumerable<Talent> All()
        {
            Load();
            foreach (var tr in trees) foreach (var t in tr.Talents) yield return t;
        }

        // the talents of a class that change a spell
        public static List<Talent> Changing(string className, string spell)
        {
            var list = new List<Talent>();
            foreach (var tr in Of(className))
                foreach (var t in tr.Talents)
                    foreach (string c in t.Changes)
                        if (string.Equals(c, spell, StringComparison.OrdinalIgnoreCase)) { list.Add(t); break; }
            return list;
        }

        public static string HoverText(Talent t)
        {
            string head = t.Spec + "  ·  " + (t.Ranks > 1 ? Lang.T("Ranks") + " " + t.Ranks : "1 " + Lang.T("rank")) +
                          (t.Row > 0 ? "  ·  " + Lang.T("needs") + " " + t.Points + " " + Lang.T("points") : "");
            string s = head + "\n\n" + t.Shown;
            if (t.Changes.Count > 0) s += "\n\n" + Lang.T("Changes") + ": " + string.Join(", ", t.Changes.ToArray());
            if (t.Text.Contains("{v}")) s += "\n" + Lang.T("X grows with every rank.");
            return s;
        }
    }

    // a test build in the Guide's talent trees: the points put in each talent of one class. Up to 51 points, and no
    // more than a talent's ranks; the game's other rules (a row opens after 5 points in its tree) are not enforced,
    // so any build can be tried out
    public class TalentBuild
    {
        public const int MaxPoints = 51;
        readonly Dictionary<string, int> points = new Dictionary<string, int>(StringComparer.Ordinal);
        public Action Changed;

        public int Get(Talent t) { int n; return points.TryGetValue(t.Id, out n) ? n : 0; }

        public int Spent { get { int s = 0; foreach (int v in points.Values) s += v; return s; } }

        public int SpentIn(TalentTree tree) { int s = 0; foreach (var t in tree.Talents) s += Get(t); return s; }

        // As in the game: a talent in row N opens once its own tree has 5 x N points in the rows above it. Each tree
        // counts on its own; the other two trees' points do not open it.
        List<TalentTree> trees = new List<TalentTree>();

        TalentTree TreeOf(Talent t)
        {
            foreach (var tr in trees) if (tr.Spec == t.Spec) return tr;
            return null;
        }

        // the points in a talent's tree, in the rows above the given row
        public int AboveRow(Talent t, int row)
        {
            var tr = TreeOf(t);
            if (tr == null) return int.MaxValue;
            int s = 0;
            foreach (var u in tr.Talents) if (u.Row < row) s += Get(u);
            return s;
        }

        public bool Open(Talent t) { return AboveRow(t, t.Row) >= t.Points; }

        public bool Add(Talent t)
        {
            if (Get(t) >= t.Ranks || Spent >= MaxPoints || !Open(t)) return false;
            points[t.Id] = Get(t) + 1;
            if (Changed != null) Changed();
            return true;
        }

        // a point can come back only if every talent taken further down still has its rows above it filled
        public bool CanRemove(Talent t)
        {
            if (Get(t) <= 0) return false;
            var tr = TreeOf(t);
            if (tr == null) return true;
            foreach (var u in tr.Talents)
                if (u.Row > t.Row && Get(u) > 0 && AboveRow(u, u.Row) - 1 < u.Points) return false;
            return true;
        }

        public bool Remove(Talent t)
        {
            int n = Get(t);
            if (n <= 0 || !CanRemove(t)) return false;
            if (n == 1) points.Remove(t.Id); else points[t.Id] = n - 1;
            if (Changed != null) Changed();
            return true;
        }

        public void Reset() { points.Clear(); if (Changed != null) Changed(); }

        // "Arms/deep_wounds=1;Fury/cruelty=5": kept in the launcher's settings, one build per class
        public string Save()
        {
            var parts = new List<string>();
            foreach (var kv in points) parts.Add(kv.Key + "=" + kv.Value);
            return string.Join(";", parts.ToArray());
        }

        // Compact, versioned plain-text format for sharing builds between launcher users.
        // Example: AZST1|Warrior|Arms/deep_wounds=1;Fury/cruelty=5
        public string ShareCode(string className) { return "AZST1|" + className + "|" + Save(); }

        public static bool TryParseShareCode(string text, string className, out TalentBuild build, out string error)
        {
            build = null;
            error = "";
            string payload = (text ?? "").Trim();
            string[] parts = payload.Split(new[] { '|' }, 3);
            if (parts.Length == 3 && parts[0] == "AZST1")
            {
                if (!string.Equals(parts[1].Trim(), className, StringComparison.OrdinalIgnoreCase))
                {
                    error = "This code is for " + parts[1].Trim() + ". Open that class in the Talent Builder, then import it again.";
                    return false;
                }
                payload = parts[2].Trim();
            }
            else if (payload.StartsWith("AZST", StringComparison.Ordinal))
            {
                error = "This talent code version is not supported. Export a new code from the launcher.";
                return false;
            }
            var known = new Dictionary<string, Talent>(StringComparer.Ordinal);
            foreach (var tree in Talents.Of(className))
                foreach (var talent in tree.Talents) known[talent.Id] = talent;
            var seen = new HashSet<string>(StringComparer.Ordinal);
            int total = 0;
            foreach (string part in payload.Split(';'))
            {
                if (part.Trim().Length == 0) continue;
                int eq = part.LastIndexOf('='), rank;
                Talent talent;
                string id = eq > 0 ? part.Substring(0, eq).Trim() : "";
                if (eq <= 0 || !int.TryParse(part.Substring(eq + 1).Trim(), out rank) ||
                    !known.TryGetValue(id, out talent) || rank < 1 || rank > talent.Ranks || !seen.Add(id))
                {
                    error = "That talent code is invalid or contains a talent that does not belong to " + className + ". Copy the whole code and try again.";
                    return false;
                }
                total += rank;
                if (total > TalentBuild.MaxPoints)
                {
                    error = "That talent code spends more than 51 points, so it cannot be imported.";
                    return false;
                }
            }
            build = Parse(payload, className);
            if (build.Spent != total)
            {
                build = null;
                error = "That talent code has talents whose required rows are not unlocked. Export the build again from the Talent Builder.";
                return false;
            }
            return true;
        }

        public static TalentBuild Parse(string text, string className)
        {
            var b = new TalentBuild();
            b.trees = Talents.Of(className);
            var byId = new Dictionary<string, Talent>(StringComparer.Ordinal);
            foreach (var tr in b.trees) foreach (var t in tr.Talents) byId[t.Id] = t;
            foreach (string part in (text ?? "").Split(';'))
            {
                int eq = part.LastIndexOf('='), n;
                Talent t;
                if (eq <= 0 || !int.TryParse(part.Substring(eq + 1), out n) || !byId.TryGetValue(part.Substring(0, eq), out t)) continue;
                n = Math.Min(n, Math.Min(t.Ranks, MaxPoints - b.Spent));
                if (n > 0) b.points[t.Id] = n;
            }
            // a build saved before the rows were enforced: the talents whose rows above are not filled come off
            for (bool changed = true; changed; )
            {
                changed = false;
                foreach (var t in byId.Values)
                    if (b.Get(t) > 0 && !b.Open(t)) { b.points.Remove(t.Id); changed = true; }
            }
            return b;
        }
    }

    // one spec's tree, like the game's talent window: the spec's name over a grid of the talents' icons
    public class TalentTreeView : Control
    {
        readonly TalentTree tree;
        readonly List<KeyValuePair<Rectangle, Talent>> cells = new List<KeyValuePair<Rectangle, Talent>>();
        Talent over;
        const int Cols = 4, Rows = 7;
        public TalentBuild Build;                            // set: a test build, click to add a point, right-click to take one back

        int cellSize;

        public TalentTreeView(TalentTree tree, int width) : this(tree, width, int.MaxValue) { }

        // a tree that fits a height too: its cells are as big as both allow (the tree is as wide as its grid then)
        public TalentTreeView(TalentTree tree, int width, int maxHeight)
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer |
                     ControlStyles.ResizeRedraw, true);
            this.tree = tree;
            int cell = Math.Max(30, (width - 16) / Cols);
            if (maxHeight != int.MaxValue) cell = Math.Max(22, Math.Min(cell, (maxHeight - 38) / Rows));
            cellSize = cell;
            Size = new Size(maxHeight != int.MaxValue ? Math.Min(width, cell * Cols + 16) : width, 30 + Rows * cell + 8);
        }

        int Cell { get { return cellSize; } }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.Clear(Parent != null ? Parent.BackColor : Theme.Panel);
            Theme.Smooth(g);
            var box = new RectangleF(0.5f, 0.5f, Width - 2, Height - 2);
            using (var path = Armory.Round(box, 6))
            {
                using (var b = new LinearGradientBrush(box, Theme.Hex("#1c1610"), Theme.Hex("#0e0c12"), 90f)) g.FillPath(b, path);
                using (var p = new Pen(Theme.Hex("#5a4426"), 1)) g.DrawPath(p, path);
            }
            using (var b = new SolidBrush(Theme.Gold))
                g.DrawString(Lang.T(tree.Spec), Theme.F(11, FontStyle.Bold), b, 10, 6);
            if (Build != null)
            {
                string pts = Build.SpentIn(tree).ToString();
                Font pf = Theme.F(11, FontStyle.Bold);
                SizeF ps = g.MeasureString(pts, pf);
                using (var b = new SolidBrush(Build.SpentIn(tree) > 0 ? Theme.Hex("#7ee05a") : Theme.GoldDim))
                    g.DrawString(pts, pf, b, Width - 10 - ps.Width, 6);
            }
            int cell = Math.Max(16, (Height - 38) / Rows), cellX = Math.Max(16, (Width - 16) / Cols);
            int ic = Theme.CrispIcon(Math.Min(42, Math.Min(cell, cellX) - 6)), x0 = 8, y0 = 30;
            cells.Clear();
            // the line that joins the milestones, as in the game's window
            var ms = tree.Talents.FindAll(delegate(Talent t) { return t.Milestone; });
            ms.Sort(delegate(Talent a, Talent b) { return a.Row.CompareTo(b.Row); });
            using (var p = new Pen(Color.FromArgb(120, Theme.GoldDim), 2))
                for (int i = 1; i < ms.Count; i++)
                    g.DrawLine(p, x0 + ms[i - 1].Col * cellX + cellX / 2, y0 + ms[i - 1].Row * cell + cell / 2,
                               x0 + ms[i].Col * cellX + cellX / 2, y0 + ms[i].Row * cell + cell / 2);
            Font rf = Theme.F(7.5f, FontStyle.Bold);
            foreach (var t in tree.Talents)
            {
                var r = new Rectangle(x0 + t.Col * cellX + (cellX - ic) / 2, y0 + t.Row * cell + (cell - ic) / 2 - 3, ic, ic);
                cells.Add(new KeyValuePair<Rectangle, Talent>(r, t));
                Image img = SpellIcons.Icon(t.Icon, this);
                if (img != null) Theme.Draw(g, img, r);
                else using (var b = new SolidBrush(Theme.Hex("#2a2218"))) g.FillRectangle(b, r);
                int have = Build != null ? Build.Get(t) : 0;
                bool locked = Build != null && have == 0 && !Build.Open(t);
                if (Build != null && have == 0)                // not taken: dimmed, as in the game; not open yet: darker
                    using (var b = new SolidBrush(Color.FromArgb(locked ? 205 : 150, 8, 6, 10))) g.FillRectangle(b, r);
                bool milestone = t.Milestone;
                using (var p = new Pen(t == over ? Theme.Hex("#fff2b0") : milestone ? Theme.Hex("#b27cff") : Theme.Hex("#8a7040"), t == over ? 2 : 1.5f))
                    g.DrawRectangle(p, r);
                string rk = have + "/" + t.Ranks;
                SizeF rs = g.MeasureString(rk, rf);
                var rb = new RectangleF(r.Right - rs.Width + 4, r.Bottom - rs.Height + 4, rs.Width, rs.Height - 1);
                using (var b = new SolidBrush(Color.FromArgb(220, 10, 8, 6))) g.FillRectangle(b, rb);
                Color rc = have >= t.Ranks ? Theme.Hex("#ffd100") : have > 0 ? Theme.Hex("#7ee05a") : Theme.Text;
                using (var b = new SolidBrush(rc)) g.DrawString(rk, rf, b, rb.X, rb.Y - 1);
            }
        }

        Talent At(Point p)
        {
            foreach (var c in cells) if (Rectangle.Inflate(c.Key, 3, 3).Contains(p)) return c.Value;
            return null;
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            Talent t = At(e.Location);
            if (t != over) { over = t; Invalidate(); }
            Cursor = t != null && Build != null ? Cursors.Hand : Cursors.Default;
            if (t != null) ShowTip(t);
            else GuideHover.Hide();
        }

        void ShowTip(Talent t)
        {
            string body = Talents.HoverText(t);
            if (Build != null)
            {
                string add = "";
                if (Build.Get(t) < t.Ranks)
                {
                    if (!Build.Open(t)) add = Lang.T("needs") + " " + t.Points + " " + Lang.T("points") + " (" + tree.Spec + ": " +
                                              Build.AboveRow(t, t.Row) + "/" + t.Points + ") ";
                    else add = (Build.Spent < TalentBuild.MaxPoints ? Lang.T("Click to add a point.") : Lang.T("All 51 points are spent.")) + " ";
                }
                string back = "";
                if (Build.Get(t) > 0)
                    back = Build.CanRemove(t) ? Lang.T("Right-click to take one back.")
                                              : Lang.T("Talents further down need this point.");
                body = Lang.T("Rank") + " " + Build.Get(t) + "/" + t.Ranks + "\n" + body + "\n\n" + add + back;
            }
            GuideHover.Show(this, t.Name, body.TrimEnd(), SpellIcons.Icon(t.Icon, null));
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            if (Build == null) return;
            Talent t = At(e.Location);
            if (t == null) return;
            if (e.Button == MouseButtons.Left) Build.Add(t);
            else if (e.Button == MouseButtons.Right) Build.Remove(t);
            ShowTip(t);
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            base.OnMouseLeave(e);
            if (over != null) { over = null; Invalidate(); }
            GuideHover.Hide();
        }
    }

    // one of a class's spells: its icon, its name, the guide's line about it and the talents that change it
    public class SpellRow : Control
    {
        readonly string name, line, className;
        readonly List<Talent> changers;
        readonly List<KeyValuePair<string, string>> affecting;   // the class's other spells whose text names this one
        readonly int topic, page;
        readonly GuideText text;
        readonly List<KeyValuePair<Rectangle, Talent>> chips = new List<KeyValuePair<Rectangle, Talent>>();
        readonly List<KeyValuePair<Rectangle, KeyValuePair<string, string>>> spellChips = new List<KeyValuePair<Rectangle, KeyValuePair<string, string>>>();
        // set by the launcher: a set chip clicked opens the Gear Planner with that set put on (class, rarity)
        public static Action<string, string> OpenSet;
        // Optional in-page destination for a talent chip. Class guides use this to keep readers in the same article.
        public Action<Talent> OpenTalentInArticle;
        // the class's set bonuses whose text names this spell: { "Faith 8-piece", its text, the rarity }
        readonly List<string[]> setBonuses = new List<string[]>();
        readonly List<KeyValuePair<Rectangle, string[]>> setChips = new List<KeyValuePair<Rectangle, string[]>>();
        readonly List<Jewel> codexItems = new List<Jewel>();
        readonly List<KeyValuePair<Rectangle, Jewel>> codexChips = new List<KeyValuePair<Rectangle, Jewel>>();
        Talent over;
        string overSpell;
        string[] overSet;
        Jewel overCodex;
        const int Ic = 30;

        static string Base(string n) { return Regex.Replace(n ?? "", @"\s*\(.*\)$", "").Trim(); }

        // the spells of the pool (name -> its text) whose text says this spell's name, word for word. A longer name
        // that holds it ("Greater Heal" for "Heal") is not a mention of it
        public static List<KeyValuePair<string, string>> Mentioning(string spell, List<KeyValuePair<string, string>> pool)
        {
            var list = new List<KeyValuePair<string, string>>();
            string me = Base(spell);
            if (me.Length < 3 || pool == null) return list;
            var longer = new List<string>();
            foreach (var kv in pool)
            {
                string n = Base(kv.Key);
                if (n.Length > me.Length && n.Contains(me) && !longer.Contains(n)) longer.Add(n);
            }
            longer.Sort(delegate(string a, string b) { return b.Length.CompareTo(a.Length); });
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var word = new Regex(@"(?<![A-Za-z'])" + Regex.Escape(me) + @"(?![A-Za-z])");
            foreach (var kv in pool)
            {
                string n = Base(kv.Key);
                if (string.Equals(n, me, StringComparison.OrdinalIgnoreCase) || seen.Contains(n)) continue;
                string t = kv.Value ?? "";
                foreach (string l in longer) t = t.Replace(l, "");
                if (!word.IsMatch(t)) continue;
                seen.Add(n);
                list.Add(new KeyValuePair<string, string>(n, kv.Value));
            }
            return list;
        }

        public SpellRow(string name, string line, string className, int width, int topic, int page)
            : this(name, line, className, width, topic, page, null) { }

        public SpellRow(string name, string line, string className, int width, int topic, int page, List<KeyValuePair<string, string>> pool)
        {
            this.topic = topic;
            this.page = page;
            affecting = Mentioning(name, pool);
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer |
                     ControlStyles.ResizeRedraw, true);
            this.name = name;
            this.line = line;
            this.className = className;
            changers = Talents.Changing(className, Regex.Replace(name, @"\s*\(.*\)$", ""));
            foreach (var j in Items.Jewelry)
                if (j.IsCodex && j.Class == className && string.Equals(j.Spell, Base(name), StringComparison.OrdinalIgnoreCase)) codexItems.Add(j);
            // the sets whose 8-piece bonus names this spell, word for word (as for the spells above)
            try
            {
                var setPool = new List<KeyValuePair<string, string>>();
                var rarityOf = new Dictionary<string, string>();
                foreach (string rar in Items.Rarities)
                {
                    string eight = Items.EightPiece(className ?? "", rar);
                    if (eight == null) continue;
                    string key = Items.SetName(className, rar) + " 8-piece";
                    setPool.Add(new KeyValuePair<string, string>(key, eight));
                    rarityOf[key] = rar;
                }
                string me = Base(name);
                // its name as written, or more than one of it ("Every 4 Wraths", "Every 3 Sinister Strikes")
                var word = new Regex(@"(?<![A-Za-z'])" + Regex.Escape(me) + @"(?:s|'s)?(?![A-Za-z])");
                if (me.Length >= 3)
                    foreach (var kv in setPool)
                        if (word.IsMatch(kv.Value)) setBonuses.Add(new[] { kv.Key, kv.Value, rarityOf[kv.Key] });
            }
            catch (Exception) { }
            BackColor = Theme.Hex("#15121c");
            text = new GuideText(line, 9.5f, Theme.Text, BackColor, width - Ic - 26, topic, page);
            text.Location = new Point(Ic + 18, 24);
            Controls.Add(text);
            int h = text.Bottom + 6;
            if (changers.Count > 0) h += 26;
            if (affecting.Count > 0) h += 26;
            if (setBonuses.Count > 0) h += 26;
            if (codexItems.Count > 0) h += 26;
            Size = new Size(width, Math.Max(h, Ic + 14));
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.Clear(Parent != null ? Parent.BackColor : Theme.Panel);
            Theme.Smooth(g);
            var box = new RectangleF(0.5f, 0.5f, Width - 2, Height - 2);
            using (var path = Armory.Round(box, 6))
            {
                using (var b = new SolidBrush(BackColor)) g.FillPath(b, path);
                using (var p = new Pen(lit ? Theme.Gold : Theme.Hex("#3a2e1e"), lit ? 2 : 1)) g.DrawPath(p, path);
            }
            var ir = new Rectangle(8, 8, Ic, Ic);
            Image img = SpellIcons.Spell(Regex.Replace(name, @"\s*\(.*\)$", ""), this);
            if (img != null) Theme.Draw(g, img, ir);
            else using (var b = new SolidBrush(Theme.Hex("#2a2218"))) g.FillRectangle(b, ir);
            using (var p = new Pen(Theme.Hex("#8a7040"), 1)) g.DrawRectangle(p, ir);
            using (var b = new SolidBrush(Theme.Gold)) g.DrawString(name, Theme.F(10.5f, FontStyle.Bold), b, Ic + 16, 5);
            chips.Clear();
            spellChips.Clear();
            setChips.Clear();
            codexChips.Clear();
            Font lf = Theme.F(8, FontStyle.Italic);
            int rowsBelow = (changers.Count > 0 ? 1 : 0) + (affecting.Count > 0 ? 1 : 0) + (setBonuses.Count > 0 ? 1 : 0) + (codexItems.Count > 0 ? 1 : 0);
            float y = Height - 24 - 26 * Math.Max(0, rowsBelow - 1);
            if (changers.Count > 0) { ChipRow(g, lf, y); y += 26; }
            if (affecting.Count > 0) { SpellChipRow(g, lf, y); y += 26; }
            if (setBonuses.Count > 0) { SetChipRow(g, lf, y); y += 26; }
            if (codexItems.Count > 0) CodexChipRow(g, lf, y);
        }

        // the sets whose 8-piece bonus changes this spell: each set's name in its rarity's colour; the hover reads it
        void SetChipRow(Graphics g, Font lf, float y)
        {
            float sx = Ic + 18;
            string label = Lang.T("Set bonuses that change it:");
            using (var b = new SolidBrush(Theme.GoldDim)) g.DrawString(label, lf, b, sx, y + 3);
            sx += g.MeasureString(label, lf).Width + 4;
            Font cf = Theme.F(8, FontStyle.Bold);
            foreach (var sb in setBonuses)
            {
                SizeF sz = g.MeasureString(sb[0], cf);
                var r = new Rectangle((int)sx, (int)y, (int)sz.Width + 8, 18);
                if (r.Right > Width - 6) break;
                using (var b = new SolidBrush(Theme.Hex("#1e1a26"))) g.FillRectangle(b, r);
                using (var p = new Pen(sb == overSet ? Theme.Hex("#fff2b0") : GearSheet.RarityColor(sb[2]), 1)) g.DrawRectangle(p, r);
                using (var b = new SolidBrush(GearSheet.RarityColor(sb[2]))) g.DrawString(sb[0], cf, b, r.X + 4, r.Y + (r.Height - sz.Height) / 2);
                setChips.Add(new KeyValuePair<Rectangle, string[]>(r, sb));
                sx = r.Right + 4;
            }
        }

        // the other spells that name this one: their icons, each a link to its own row
        void SpellChipRow(Graphics g, Font lf, float y)
        {
            float sx = Ic + 18;
            string slabel = Lang.T("Spells that change it:");
            using (var b = new SolidBrush(Theme.GoldDim)) g.DrawString(slabel, lf, b, sx, y + 3);
            sx += g.MeasureString(slabel, lf).Width + 4;
            foreach (var sp in affecting)
            {
                if (sx + 18 > Width - 6) break;
                var r = new Rectangle((int)sx, (int)y, 18, 18);
                Image si = SpellIcons.Spell(sp.Key, this);
                if (si != null) Theme.Draw(g, si, r);
                else using (var b = new SolidBrush(Theme.Hex("#2a2218"))) g.FillRectangle(b, r);
                using (var p = new Pen(sp.Key == overSpell ? Theme.Hex("#fff2b0") : Theme.Hex("#8a7040"), 1)) g.DrawRectangle(p, r);
                spellChips.Add(new KeyValuePair<Rectangle, KeyValuePair<string, string>>(r, sp));
                sx += 22;
            }
        }

        // The Codex neck piece that remakes this spell; it routes through the class guide to its class guide.
        void CodexChipRow(Graphics g, Font lf, float y)
        {
            float x = Ic + 18;
            string label = Lang.T("Codex neck piece:");
            using (var b = new SolidBrush(Theme.GoldDim)) g.DrawString(label, lf, b, x, y + 3);
            x += g.MeasureString(label, lf).Width + 4;
            Font cf = Theme.F(8, FontStyle.Bold);
            foreach (var j in codexItems)
            {
                string name = j.Name;
                SizeF sz = g.MeasureString(name, cf);
                int width = Math.Min((int)sz.Width + 8, Width - (int)x - 6);
                if (width < 24) break;
                var r = new Rectangle((int)x, (int)y, width, 18);
                using (var b = new SolidBrush(Theme.Hex("#1e1a26"))) g.FillRectangle(b, r);
                using (var p = new Pen(j == overCodex ? Theme.Hex("#fff2b0") : GearSheet.RarityColor("Legendary"), 1)) g.DrawRectangle(p, r);
                using (var b = new SolidBrush(GearSheet.RarityColor("Legendary"))) g.DrawString(name, cf, b, new RectangleF(r.X + 4, r.Y + 2, r.Width - 8, r.Height - 4));
                codexChips.Add(new KeyValuePair<Rectangle, Jewel>(r, j));
                x = r.Right + 4;
            }
        }

        void ChipRow(Graphics g, Font lf, float y)
        {
            float x = Ic + 18;
            string label = Lang.T("Talents that change it:");
            using (var b = new SolidBrush(Theme.GoldDim)) g.DrawString(label, lf, b, x, y + 3);
            x += g.MeasureString(label, lf).Width + 4;
            foreach (var t in changers)
            {
                Image ti = SpellIcons.Icon(t.Icon, this);
                var r = new Rectangle((int)x, (int)y, 18, 18);
                if (x + 18 > Width - 6) break;
                if (ti != null) Theme.Draw(g, ti, r);
                else using (var b = new SolidBrush(Theme.Hex("#2a2218"))) g.FillRectangle(b, r);
                using (var p = new Pen(t == over ? Theme.Hex("#fff2b0") : Theme.Hex("#8a7040"), 1)) g.DrawRectangle(p, r);
                chips.Add(new KeyValuePair<Rectangle, Talent>(r, t));
                x += 22;
            }
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            Talent t = null;
            foreach (var c in chips) if (Rectangle.Inflate(c.Key, 2, 2).Contains(e.Location)) t = c.Value;
            string s = null, sText = null;
            foreach (var c in spellChips) if (Rectangle.Inflate(c.Key, 2, 2).Contains(e.Location)) { s = c.Value.Key; sText = c.Value.Value; }
            string[] st = null;
            foreach (var c in setChips) if (Rectangle.Inflate(c.Key, 2, 2).Contains(e.Location)) st = c.Value;
            Jewel cj = null;
            foreach (var c in codexChips) if (Rectangle.Inflate(c.Key, 2, 2).Contains(e.Location)) cj = c.Value;
            if (t != over || s != overSpell || st != overSet || cj != overCodex) { over = t; overSpell = s; overSet = st; overCodex = cj; Invalidate(); }
            Cursor = t != null || s != null || st != null || cj != null ? Cursors.Hand : Cursors.Default;   // a click opens the talent's trees / the spell's row / the set in the planner
            if (t != null) GuideHover.Show(this, t.Name, Talents.HoverText(t), SpellIcons.Icon(t.Icon, null));
            else if (s != null) GuideHover.Show(this, s, sText, SpellIcons.Spell(s, null));
            else if (st != null) GuideHover.Show(this, st[0], st[1] + "\n\n" + Lang.T("all 8 at 3+ stars, half again as strong at 5"), null);
            else if (cj != null) GuideHover.Show(this, cj.Name, Items.Card(cj), SpellIcons.Spell(cj.Spell, null));
            else if (new Rectangle(8, 8, Ic, Ic).Contains(e.Location) || e.Y < 24)
                GuideHover.Show(this, name, SpellHover(), SpellIcons.Spell(Regex.Replace(name, @"\s*\(.*\)$", ""), null));
            else GuideHover.Hide();
        }

        string SpellHover()
        {
            string s = line;
            string game = GuideLinks.GameTip(Regex.Replace(name, @"\s*\(.*\)$", ""));
            if (game != null) s = game;
            if (changers.Count > 0)
            {
                var n = new List<string>();
                foreach (var t in changers) n.Add(t.Name + " (" + t.Spec + ")");
                s += "\n\n" + Lang.T("Talents that change it:") + " " + string.Join(", ", n.ToArray());
            }
            if (affecting.Count > 0)
            {
                var n = new List<string>();
                foreach (var a in affecting) n.Add(a.Key);
                s += "\n\n" + Lang.T("Spells that change it:") + " " + string.Join(", ", n.ToArray());
            }
            if (setBonuses.Count > 0)
            {
                var n = new List<string>();
                foreach (var sb in setBonuses) n.Add(sb[0]);
                s += "\n\n" + Lang.T("Set bonuses that change it:") + " " + string.Join(", ", n.ToArray());
            }
            if (codexItems.Count > 0)
            {
                var n = new List<string>();
                foreach (var j in codexItems) n.Add(j.Name);
                s += "\n\n" + Lang.T("Codex neck piece that remakes it:") + " " + string.Join(", ", n.ToArray());
            }
            return s;
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            base.OnMouseLeave(e);
            if (over != null || overSpell != null || overSet != null || overCodex != null) { over = null; overSpell = null; overSet = null; overCodex = null; Invalidate(); }
            GuideHover.Hide();
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            if (e.Button == MouseButtons.Left && overCodex != null && GuideLinks.Navigate != null)
            {
                GuideHover.Hide();
                var topics = Data.Guide;
                int ti = topics.FindIndex(delegate(GuideTopic g) { return g.Title == "Classes"; });
                int pi = ti < 0 ? -1 : topics[ti].Pages.FindIndex(delegate(GuidePage p) { return p.Title == className; });
                if (ti >= 0 && pi >= 0) GuideLinks.Navigate(new GuideLink { Term = overCodex.Name, FindText = overCodex.Name, Topic = ti, Page = pi, Tip = Items.Card(overCodex) });
                return;
            }
            if (e.Button == MouseButtons.Left && overSet != null && OpenSet != null)
            {
                GuideHover.Hide();
                string[] set = overSet;
                WowButton.Fire(delegate { OpenSet(className, set[2]); });   // the full set (its 8-piece) in the planner
                return;
            }
            if (e.Button == MouseButtons.Left && overSpell != null && GuideLinks.Navigate != null)
            {
                GuideHover.Hide();
                var sl = new GuideLink();                     // another spell: its own row, on this page
                sl.Term = overSpell;
                sl.Topic = topic;
                sl.Page = page;
                GuideLinks.Navigate(sl);
                return;
            }
            if (e.Button != MouseButtons.Left || over == null) return;
            GuideHover.Hide();
            if (OpenTalentInArticle != null)
            {
                Talent target = over;
                OpenTalentInArticle(target);
                return;
            }
            if (GuideLinks.Navigate == null) return;
            var l = new GuideLink();
            l.Term = over.Name;
            l.Class = className;
            l.Topic = -1;
            l.Page = -1;
            try
            {
                var topics = Data.Guide;
                l.Topic = topics.FindIndex(delegate(GuideTopic g) { return g.Title == "Classes"; });
                if (l.Topic >= 0) l.Page = topics[l.Topic].Pages.FindIndex(delegate(GuidePage g) { return g.Title == className; });
            }
            catch (Exception) { }
            if (l.Topic >= 0) GuideLinks.Navigate(l);
        }

        // the spell's name without "(Rank 2)" and the like
        public string Spell { get { return Regex.Replace(name, @"\s*\(.*\)$", ""); } }

        bool lit;
        // lit up in gold for a moment: a link brought you here
        public void Flash()
        {
            lit = true;
            Invalidate();
            var t = new System.Windows.Forms.Timer();
            t.Interval = 1600;
            t.Tick += delegate { t.Stop(); t.Dispose(); lit = false; if (!IsDisposed) Invalidate(); };
            t.Start();
        }
    }
}
