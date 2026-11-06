// The section window: a WoW-whisper-style box that opens to the right of the launcher, with Cloud Save,
// Addons (and Download More), Options, Changelog, Guide, Stats (StatsPanel.cs) and About Launcher.
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Globalization;
using System.IO;
using System.Windows.Forms;

namespace AzerothLauncher
{
    // the bronze title bar: round emblem portrait, the section's name, a red X
    // The section window's title bar: the back arrow, then the trail of where you are ("Guide › Theorycrafting ›
    // Talent Tree Builder › Priest"): click a step to go back to it. The red X closes the section.
    public class SideHead : Control
    {
        public Action CloseClicked;
        public Action HelpClicked;                           // "Lost?" beside the X: how the path at the top works
        public Action FavoriteClicked;
        public Action HelpLeft;
        bool helpShown;                                      // only on a section's other pages, never on its main page
        public bool HelpShown { get { return helpShown; } set { if (helpShown != value) { helpShown = value; Invalidate(); } } }
        public Action BackClicked;                           // null: no back arrow
        public Form Owner2;
        bool closable = true;
        public bool Closable { get { return closable; } set { closable = value; Invalidate(); } }   // no X on Community
        bool closeEnabled = true;
        public bool CloseEnabled { get { return closeEnabled; } set { if (closeEnabled != value) { closeEnabled = value; Invalidate(); } } }
        string note = "";                                    // a short line at the right end of the bar (Community: while a video plays)
        public string Note { get { return note; } set { note = value ?? ""; Invalidate(); } }
        int NoteW { get { return note.Length == 0 ? 0 : Theme.Px(12) + TextRenderer.MeasureText(note, Theme.F(9, FontStyle.Bold)).Width; } }
        public Action<Point> Moved;
        Point? dragOffset;
        List<KeyValuePair<string, Action>> crumbs = new List<KeyValuePair<string, Action>>();
        readonly List<KeyValuePair<Rectangle, Action>> hot = new List<KeyValuePair<Rectangle, Action>>();
        readonly List<KeyValuePair<Rectangle, int>> crumbRects = new List<KeyValuePair<Rectangle, int>>();
        Rectangle favoriteBox = Rectangle.Empty;
        // a step's drop-down (the other choices at that level), and where its arrow was drawn
        readonly Dictionary<int, List<KeyValuePair<string, Action>>> menus = new Dictionary<int, List<KeyValuePair<string, Action>>>();
        readonly List<KeyValuePair<Rectangle, int>> arrows = new List<KeyValuePair<Rectangle, int>>();
        int hoverArrow = -1;
        int hover = -1;                                      // the step under the pointer (-2: the back arrow)
        bool favoriteHover;
        Form hoverHost;

        public SideHead()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer |
                     ControlStyles.ResizeRedraw, true);
            Height = 38;                                     // compact: the sections have little room
            BackColor = Theme.Hex("#0e0d14");
        }

        void SyncHoverHost()
        {
            Form host = FindForm();
            while (host != null && !host.TopLevel) host = host.Parent as Form;
            SetHoverHost(host);
        }

        internal void SetHoverHost(Form host)
        {
            if (host == hoverHost) return;
            if (hoverHost != null) hoverHost.Deactivate -= HoverHostDeactivated;
            hoverHost = host;
            if (hoverHost != null) hoverHost.Deactivate += HoverHostDeactivated;
        }

        protected override void OnParentChanged(EventArgs e)
        {
            base.OnParentChanged(e);
            SyncHoverHost();
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            SyncHoverHost();
        }

        void HoverHostDeactivated(object sender, EventArgs e)
        {
            if (hoverTimer != null) hoverTimer.Stop();
            CloseOpenMenu();
            favoriteHover = false;
            GuideHover.Hide(this);
            if (helpTip) helpTip = false;
        }

        internal void HideTransientUi() { HoverHostDeactivated(this, EventArgs.Empty); }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                if (hoverHost != null) hoverHost.Deactivate -= HoverHostDeactivated;
                hoverHost = null;
                if (hoverTimer != null) { hoverTimer.Stop(); hoverTimer.Dispose(); hoverTimer = null; }
                CloseOpenMenu();
                GuideHover.Hide(this);
            }
            base.Dispose(disposing);
        }

        public string Title
        {
            get { return crumbs.Count > 0 ? crumbs[crumbs.Count - 1].Key : ""; }
            set { crumbs = new List<KeyValuePair<string, Action>> { new KeyValuePair<string, Action>(value, null) }; Invalidate(); }
        }

        // the trail; the last step is where you are (its action is not used)
        public void SetCrumbs(List<KeyValuePair<string, Action>> list)
        {
            if (hoverTimer != null) hoverTimer.Stop();
            CloseOpenMenu();
            FavoriteClicked = null;
            crumbs = list ?? new List<KeyValuePair<string, Action>>();
            menus.Clear();
            hover = -1;
            hoverStep = -1;
            hoverArrow = -1;
            quietStep = -1;
            favoriteHover = false;
            Invalidate();
        }

        // the choices of step i (not the first one): a small arrow beside it opens them
        public void SetMenu(int i, List<KeyValuePair<string, Action>> items)
        {
            if (i <= 0 || items == null || items.Count == 0) return;
            menus[i] = items;
            Invalidate();
        }

        // Header buttons, left to right: LOST, Favorites, Close. Each is a square at the top right of the bar.
        const int HeaderButton = 22, HeaderGap = 4;
        int HeaderRight { get { return Width - Theme.Px(10); } }
        Rectangle CloseBox { get { return closable ? new Rectangle(HeaderRight - Theme.Px(HeaderButton), Theme.Px(8), Theme.Px(HeaderButton), Theme.Px(HeaderButton)) : Rectangle.Empty; } }
        Rectangle FavoriteBox
        {
            get
            {
                if (FavoriteClicked == null) return Rectangle.Empty;
                int right = (closable ? CloseBox.Left : HeaderRight) - Theme.Px(HeaderGap);
                return new Rectangle(right - Theme.Px(HeaderButton), Theme.Px(8), Theme.Px(HeaderButton), Theme.Px(HeaderButton));
            }
        }
        // "Lost?" beside the X: for anyone not sure how to get around (its width follows the word in the language)
        int HelpW { get { return Theme.Px(26) + (int)TextRenderer.MeasureText(Lang.T("Lost?"), Theme.F(9, FontStyle.Bold)).Width; } }
        Rectangle HelpBox
        {
            get
            {
                if (HelpClicked == null || !helpShown) return Rectangle.Empty;
                Rectangle next = !FavoriteBox.IsEmpty ? FavoriteBox : (closable ? CloseBox : Rectangle.Empty);
                int right = (next.IsEmpty ? HeaderRight : next.Left) - Theme.Px(HeaderGap);
                return new Rectangle(right - HelpW, Theme.Px(8), HelpW, Theme.Px(HeaderButton));
            }
        }
        public Point HelpScreenAnchor { get { Rectangle box = HelpBox; return box.IsEmpty ? PointToScreen(Point.Empty) : PointToScreen(new Point(box.Left + box.Width / 2, box.Bottom)); } }
        public Rectangle HelpScreenBounds { get { Rectangle box = HelpBox; return box.IsEmpty ? Rectangle.Empty : RectangleToScreen(box); } }
        Rectangle BackBox { get { return BackClicked != null ? new Rectangle(Theme.Px(44),Theme.Px(8),Theme.Px(30),Theme.Px(20)) : Rectangle.Empty; } }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.Clear(BackColor);
            int w = Math.Max(Width, 200);
            Image bar = Retro.Get("long");                  // the launcher's blue stone, framed in gold
            if (bar != null) Theme.Draw(g, bar, new Rectangle(Theme.Px(22),Theme.Px(7), w - Theme.Px(26),Theme.Px(22)));
            using (var p = new Pen(Theme.Hex("#c49a48"), 2)) g.DrawRectangle(p, Theme.Px(22), Theme.Px(7), w - Theme.Px(26), Theme.Px(21));
            Theme.Smooth(g);
            hot.Clear();
            arrows.Clear();
            menuText.Clear();
            stepRects.Clear();
            crumbRects.Clear();
            favoriteBox = FavoriteBox;
            int x = Theme.Px(50);
            Rectangle bb = BackBox;
            if (!bb.IsEmpty)
            {
                using (var b = new SolidBrush(hover == -2 ? Theme.Hex("#3a2c18") : Theme.Hex("#1c160e"))) g.FillRectangle(b, bb);
                using (var p = new Pen(Theme.Hex("#d6a640"), 1.5f)) g.DrawRectangle(p, bb);
                using (var b = new SolidBrush(Theme.Hex("#fff0b0")))
                    g.FillPolygon(b, new[] { new PointF(bb.X + Theme.Px(11), bb.Y + Theme.Px(10)), new PointF(bb.X + Theme.Px(19), bb.Y + Theme.Px(4)), new PointF(bb.X + Theme.Px(19), bb.Y + Theme.Px(16)) });
                x = bb.Right + 8;
            }
            int right = (closable ? Width - Theme.Px(32) : Width - Theme.Px(8)) - (HelpClicked != null && helpShown ? HelpW + Theme.Px(5) : 0) - NoteW;
            if (note.Length > 0)
                TextRenderer.DrawText(g, note, Theme.F(9, FontStyle.Bold), new Rectangle(right, 0, NoteW, Height), Theme.Hex("#e0a040"),
                                      TextFormatFlags.Right | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine);
            // every step looks the same, the first (root) included: one font, one size, one colour (white under the pointer)
            Font f = Theme.F(10.5f, FontStyle.Bold), fs = f;
            using (var sf = new StringFormat(StringFormatFlags.NoWrap))
            {
                sf.LineAlignment = StringAlignment.Center;
                sf.Trimming = StringTrimming.EllipsisCharacter;
                // from the end: the steps that fit; the first ones go when the trail is too long
                var widths = new List<float>();
                float arrowW = Theme.Px(14);
                for (int i = 0; i < crumbs.Count; i++)
                    widths.Add(g.MeasureString(Lang.T(crumbs[i].Key), i == crumbs.Count - 1 ? f : fs).Width + (menus.ContainsKey(i) ? arrowW : 0));
                float sep = g.MeasureString(" › ", fs).Width;
                int first = 0;
                float total = 0;
                for (int i = 0; i < crumbs.Count; i++) total += widths[i] + (i > 0 ? sep : 0);
                while (first < crumbs.Count - 1 && x + total > right) { total -= widths[first] + sep; first++; }
                for (int i = first; i < crumbs.Count; i++)
                {
                    bool last = i == crumbs.Count - 1;
                    string t = Lang.T(crumbs[i].Key);
                    float tw = Math.Min(widths[i], right - x);
                    bool menu = menus.ContainsKey(i);
                    var r = new RectangleF(x,Theme.Px(7), Math.Max(1, tw - (menu ? arrowW : 0)), Theme.Px(22));
                    crumbRects.Add(new KeyValuePair<Rectangle, int>(Rectangle.Round(r), i));
                    Color c = !last && hover == i ? Color.White : Theme.Hex("#e6cf8c");
                    using (var b = new SolidBrush(c)) g.DrawString(t, last ? f : fs, b, r, sf);
                    if (menu)
                    {
                        // the drop-down arrow: the other choices at this step
                        var ar = new Rectangle((int)r.Right + 1, 11, (int)arrowW - 2, 14);
                        if (hoverArrow == i) using (var b = new SolidBrush(Theme.Hex("#3a2c18"))) g.FillRectangle(b, ar);
                        using (var b = new SolidBrush(hoverArrow == i ? Color.White : Theme.Hex("#d9c48a")))
                            g.FillPolygon(b, new[] { new PointF(ar.X + Theme.Px(2), ar.Y + Theme.Px(5)), new PointF(ar.Right - Theme.Px(2), ar.Y + Theme.Px(5)), new PointF(ar.X + ar.Width / 2f, ar.Y + Theme.Px(10)) });
                        arrows.Add(new KeyValuePair<Rectangle, int>(ar, i));
                        if (last || crumbs[i].Value == null) menuText.Add(new KeyValuePair<Rectangle, int>(Rectangle.Round(r), i));
                        stepRects.Add(new KeyValuePair<Rectangle, int>(Rectangle.Union(Rectangle.Round(r), ar), i));
                    }
                    if (!last && crumbs[i].Value != null)
                    {
                        hot.Add(new KeyValuePair<Rectangle, Action>(Rectangle.Round(r), crumbs[i].Value));
                        if (hover == i)
                            using (var p = new Pen(Theme.Hex("#d9c48a"))) g.DrawLine(p, r.X, r.Bottom - 4, r.X + tw, r.Bottom - 4);
                    }
                    x += (int)tw;
                    if (!last)
                    {
                        using (var b = new SolidBrush(Theme.Hex("#a08850"))) g.DrawString(" › ", fs, b, new RectangleF(x,Theme.Px(7), sep + Theme.Px(2),Theme.Px(22)), sf);
                        x += (int)sep;
                    }
                    if (x >= right) break;
                }
            }
            if (Theme.Portrait != null) Theme.Draw(g, Theme.Portrait, new Rectangle(Theme.Px(2), 1,Theme.Px(36),Theme.Px(36)));
            Rectangle hb = HelpBox;
            if (!hb.IsEmpty)
            {
                using (var b = new SolidBrush(Theme.Hex("#1c1a26"))) g.FillRectangle(b, hb);
                using (var p = new Pen(Theme.Hex("#d6a640"), 2)) g.DrawRectangle(p, hb);
                using (var sf = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center })
                using (var b = new SolidBrush(Theme.Hex("#f0c050"))) g.DrawString(Lang.T("Lost?"), Theme.F(9, FontStyle.Bold), b, new RectangleF(hb.X, hb.Y + Theme.Px(1), hb.Width, hb.Height), sf);
            }
            if (!favoriteBox.IsEmpty)
            {
                using (var b = new SolidBrush(favoriteHover ? Theme.Hex("#fff2b0") : Theme.Gold))
                    TextRenderer.DrawText(g, "★", Theme.F(12, FontStyle.Bold), favoriteBox, b.Color,
                        TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);
            }
            Rectangle xb = CloseBox;
            if (xb.IsEmpty) return;
            using (var b = new SolidBrush(closeEnabled ? Theme.Hex("#2a1c1c") : Theme.Hex("#211f20"))) g.FillRectangle(b, xb);
            using (var p = new Pen(closeEnabled ? Theme.Hex("#d6a640") : Theme.Hex("#6f6658"), 2)) g.DrawRectangle(p, xb);
            using (var p = new Pen(closeEnabled ? Theme.Hex("#e53a22") : Theme.Hex("#79736b"), 3))
            {
                int inset = Theme.Px(6);
                g.DrawLine(p, xb.X + inset, xb.Y + inset, xb.Right - inset - 1, xb.Bottom - inset - 1);
                g.DrawLine(p, xb.Right - inset - 1, xb.Y + inset, xb.X + inset, xb.Bottom - inset - 1);
            }
        }

        int HotAt(Point pt)
        {
            if (BackBox.Contains(pt)) return -2;
            for (int i = 0; i < crumbs.Count; i++)
                foreach (var h in hot)
                    if (h.Key.Contains(pt) && h.Value == crumbs[i].Value) return i;
            return -1;
        }

        // the name of a step with a drop-down and nothing else to do (the last step): a click opens its drop-down
        readonly List<KeyValuePair<Rectangle, int>> menuText = new List<KeyValuePair<Rectangle, int>>();
        int MenuTextAt(Point pt)
        {
            foreach (var a in menuText) if (a.Key.Contains(pt)) return a.Value;
            return -1;
        }

        // hovering a step that has a drop-down opens it (a click on its name still goes there); it closes when the
        // pointer leaves both the step and the list
        readonly List<KeyValuePair<Rectangle, int>> stepRects = new List<KeyValuePair<Rectangle, int>>();
        int hoverStep = -1;
        ContextMenuStrip openMenu;
        int openMenuStep = -1, quietStep = -1;              // quietStep: just closed, not again until the pointer leaves it
        Timer hoverTimer;

        int StepAt(Point pt)
        {
            foreach (var a in stepRects) if (a.Key.Contains(pt)) return a.Value;
            return -1;
        }

        Rectangle StepRect(int i)
        {
            foreach (var a in stepRects) if (a.Value == i) return a.Key;
            return Rectangle.Empty;
        }

        void HoverTick()
        {
            Point pt = PointToClient(Cursor.Position);
            int s = ClientRectangle.Contains(pt) ? StepAt(pt) : -1;
            bool inMenu = openMenu != null && !openMenu.IsDisposed && openMenu.Visible && openMenu.Bounds.Contains(Cursor.Position);

            if (openMenu != null && !openMenu.IsDisposed)
            {
                if (inMenu || s == openMenuStep) return;
                openMenu.Close();
            }
            if (s >= 0 && s == hoverStep && s != quietStep && !Handheld.GameMode) ShowMenu(s, StepRect(s));   // Game Mode: by tap only
        }

        int ArrowAt(Point pt)
        {
            foreach (var a in arrows) if (a.Key.Contains(pt)) return a.Value;
            return -1;
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            int st = StepAt(e.Location);
            if (st != quietStep) quietStep = -1;
            if (st != hoverStep)
            {
                hoverStep = st;
                if (hoverTimer == null)
                {
                    hoverTimer = new Timer();
                    hoverTimer.Interval = 250;
                    hoverTimer.Tick += delegate { HoverTick(); };
                }
                hoverTimer.Start();
            }
            int h = HotAt(e.Location), ha = ArrowAt(e.Location);
            if (ha < 0) ha = MenuTextAt(e.Location);          // the last step's name lights its arrow
            if (h != hover || ha != hoverArrow) { hover = h; hoverArrow = ha; Invalidate(); }
            favoriteHover = !favoriteBox.IsEmpty && favoriteBox.Contains(e.Location);
            Cursor = (closeEnabled && CloseBox.Contains(e.Location)) || h != -1 || ha != -1 || favoriteHover ? Cursors.Hand : Cursors.Default;   // "Lost?" is not clicked: pointed at
            if (favoriteHover) GuideHover.ShowHelp(this, Lang.T("Save to Quick Access"), Lang.T("Save this location to Quick Access."));
            else GuideHover.Hide(this);
            Invalidate();
            // "Lost?": a small note while the pointer is on it (no window over the launcher)
            bool onHelp = HelpBox.Contains(e.Location);
            if (onHelp && !helpTip) { helpTip = true; WowButton.Fire(HelpClicked); }
            else if (!onHelp && helpTip) { helpTip = false; if (HelpLeft != null) HelpLeft(); }
            if (dragOffset.HasValue && Owner2 != null)
            {
                Point s = PointToScreen(e.Location);
                Owner2.Location = new Point(s.X - dragOffset.Value.X, s.Y - dragOffset.Value.Y);
            }
            base.OnMouseMove(e);
        }

        bool helpTip;                                        // the "Lost?" note is showing

        protected override void OnMouseLeave(EventArgs e) { favoriteHover = false; GuideHover.Hide(this); if (helpTip) { helpTip = false; if (HelpLeft != null) HelpLeft(); } if (hover != -1 || hoverArrow != -1) { hover = -1; hoverArrow = -1; Invalidate(); } base.OnMouseLeave(e); }

        void ShowMenu(int i, Rectangle at)
        {
            List<KeyValuePair<string, Action>> items;
            if (!menus.TryGetValue(i, out items)) return;
            if (Handheld.GameMode)                           // a list inside the launcher (Game Mode: one window at a time)
            {
                var picks = new List<KeyValuePair<string, Action>>();
                foreach (var kv in items)
                    picks.Add(new KeyValuePair<string, Action>(Lang.T(kv.Key) + (kv.Key == crumbs[i].Key ? "  ‹" : ""), kv.Value));
                MenuChooser.Open(Lang.T(crumbs[i].Key), picks);
                return;
            }
            if (openMenu != null && !openMenu.IsDisposed) { if (openMenuStep == i) return; openMenu.Close(); }
            var m = ThemeMenus.Create(false);
            // not AutoClose: path clicks still go to the breadcrumb. HoverTick closes the list off-path; host deactivation
            // also closes it (Alt+Tab), and picking an item closes it before navigation.
            m.AutoClose = false;
            openMenu = m;
            openMenuStep = i;
            foreach (var kv in items)
            {
                Action act = kv.Value;
                var it = new ToolStripMenuItem(Lang.T(kv.Key).Replace("&", "&&"));   // "&" is shown as itself, not as an underline
                it.ForeColor = kv.Key == crumbs[i].Key ? Theme.Hex("#f0c050") : Theme.Hex("#f5e9c8");   // where you are now
                it.Click += delegate { m.Close(); WowButton.Fire(act); };
                m.Items.Add(it);
            }
            m.Closed += delegate { if (openMenu == m) { quietStep = openMenuStep; openMenu = null; openMenuStep = -1; } BeginInvoke(new Action(m.Dispose)); };
            m.Show(this, new Point(at.X - 6, Height - 4));
        }

        void CloseOpenMenu() { if (openMenu != null && !openMenu.IsDisposed) openMenu.Close(); }

        // the list of a step, opened by a click: at the same place as when hovered
        void ClickMenu(int i)
        {
            Rectangle at = StepRect(i);
            if (at.IsEmpty) foreach (var a in arrows) if (a.Value == i) at = a.Key;
            if (!at.IsEmpty) ShowMenu(i, at);
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Left && !CloseBox.Contains(e.Location) && !HelpBox.Contains(e.Location) && !FavoriteBox.Contains(e.Location) && Owner2 != null)
            {
                Point s = PointToScreen(e.Location);
                dragOffset = new Point(s.X - Owner2.Left, s.Y - Owner2.Top);   // drag the title bar to move the window
                Capture = true;
            }
            base.OnMouseDown(e);
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            if (dragOffset.HasValue)
            {
                dragOffset = null;
                Capture = false;
                if (Moved != null && Owner2 != null) Moved(Owner2.Location);
            }
            else if (e.Button == MouseButtons.Left && CloseBox.Contains(e.Location) && closeEnabled) { CloseOpenMenu(); WowButton.Fire(CloseClicked); }
            else if (e.Button == MouseButtons.Left && FavoriteBox.Contains(e.Location) && FavoriteClicked != null) WowButton.Fire(FavoriteClicked);
            else if (e.Button == MouseButtons.Left && HelpBox.Contains(e.Location)) { }   // "Lost?" only answers the pointer
            else if (e.Button == MouseButtons.Left && ArrowAt(e.Location) >= 0) ClickMenu(ArrowAt(e.Location));
            else if (e.Button == MouseButtons.Left)
            {
                // a click on a step's name goes back to that place; its list of places beside it opens on the ▾ or
                // when the pointer rests on it (a step that is not a place, the one you are on, opens its list)
                int h = HotAt(e.Location);
                if (h == -2) { CloseOpenMenu(); WowButton.Fire(BackClicked); }
                else if (h >= 0 && crumbs[h].Value != null)
                {
                    CloseOpenMenu();
                    quietStep = h;                           // the list does not pop open again on the page it led to
                    WowButton.Fire(crumbs[h].Value);
                }
                else if (h >= 0 && menus.ContainsKey(h)) ClickMenu(h);
                else
                {
                    int m = MenuTextAt(e.Location);
                    if (m >= 0) ClickMenu(m);
                }
            }
            base.OnMouseUp(e);
        }
    }

    // a panel with a thin coloured border around its content
    public class Bordered : Panel
    {
        public Bordered(Color border, int width)
        {
            BackColor = border;
            Padding = new Padding(width);
        }
    }

    // the All Patch Notes list: the mouse wheel flips the page
    public class WheelPanel : Panel, IWheel
    {
        public Action<int> OnWheel;
        public void Wheel(int delta) { if (OnWheel != null) OnWheel(delta); }
    }

    public partial class LauncherApp
    {
        sealed class GuideFavoriteRoute
        {
            public string Kind, Topic, Page, ClassKey, Url;
            public string Anchor;
            public int ScrollY, AnchorOffset;
            public string PageLabel
            {
                get
                {
                    if (Kind == "wiki") return "Wiki";
                    if (Kind == "buildVideos") return "Build Videos";
                    if (Kind == "guide" || Kind == "guideTalents")
                    {
                        string label = Page.Length > 0 ? Page : Topic;
                        if (string.Equals(label, "Codex (Necklaces)", StringComparison.OrdinalIgnoreCase)) label = "Codex";
                        string anchorLabel = string.Equals(Anchor, "Codex (Necklaces)", StringComparison.OrdinalIgnoreCase) ? "Codex" : Lang.T(Anchor);
                        return Anchor.Length > 0 ? label + " · " + anchorLabel : label;
                    }
                    if (Kind == "group" || Kind == "section") return Topic;
                    if (Kind == "builderClass" || Kind == "plannerClass") return ClassKey;
                    if (Kind == "video") return Page.Length > 0 ? Page : Topic;
                    if (Kind == "builder") return TalentBuilder;
                    if (Kind == "option")
                    {
                        if (Page == "about") return "About Launcher";
                        if (Page == "language") return "Language";
                        int tab;
                        return int.TryParse(ClassKey, out tab) ? OptionTabName(tab) : "Options";
                    }
                    if (Kind == "addon")
                    {
                        int category;
                        return Page == "category" && int.TryParse(ClassKey, out category) && category >= 0 && category < Data.AddonCategories.Length
                            ? Data.AddonCategories[category] : "Download More";
                    }
                    if (Kind == "stats")
                    {
                        int tab;
                        return Page == "class" ? ClassKey : int.TryParse(Url, out tab) && tab >= 0 && tab < StatTabs.Length ? StatTabs[tab] : "Stats";
                    }
                    if (Kind == "setup")
                    {
                        int step;
                        return int.TryParse(ClassKey, out step) && step >= 0 && step < SetupSteps.Length ? SetupSteps[step] : "Setup";
                    }
                    return GearPlanner;
                }
            }
            public GuideFavoriteRoute(string kind, string topic, string page) : this(kind, topic, page, "", "") { }
            public GuideFavoriteRoute(string kind, string topic, string page, string classKey, string url)
            { Kind = string.IsNullOrEmpty(kind) ? "guide" : kind; Topic = topic ?? ""; Page = page ?? ""; ClassKey = classKey ?? ""; Url = url ?? ""; Anchor = ""; }
            public GuideFavoriteRoute(string kind, string topic, string page, string classKey, string url, string anchor, int scrollY, int anchorOffset)
                : this(kind, topic, page, classKey, url)
            { Anchor = anchor ?? ""; ScrollY = Math.Max(0, scrollY); AnchorOffset = Math.Max(0, anchorOffset); }
        }

        const int SideW = 680, SideH = 600;
        const int ClNewest = -1, ClList = -2;                    // changelog views; 0.. = one older post

        Form side;
        SideHead sideHead;
        Panel panel;
        Panel closeDownloadCard;
        WowButton cancelDownloadCloseButton, keepDownloadButton;
        Panel strip;
        const int GuideFavoriteCount = 10;
        static Image codexFavoriteIcon;
        static Image heroicFavoriteIcon;
        static Image zonesBossesFavoriteIcon;
        static Image gearPlannerFavoriteIcon;
        static Image talentTreeFavoriteIcon;
        static Image jewelryFavoriteIcon;
        static Dictionary<string, Image> classFavoriteIcons;
        static Dictionary<string, Image> classGearHelmetImages;

        static Image FavoriteClassIcon(string classKey)
        {
            if (string.IsNullOrEmpty(classKey)) return null;
            string key = classKey.Trim().ToLowerInvariant();
            if (classFavoriteIcons == null) classFavoriteIcons = new Dictionary<string, Image>(StringComparer.OrdinalIgnoreCase);
            Image image;
            if (!classFavoriteIcons.TryGetValue(key, out image))
            {
                image = Theme.LoadImage("AS.favorites.class-icon-" + key + ".jpg");
                classFavoriteIcons[key] = image;
            }
            return image;
        }

        static Image FavoriteGearHelmet(string classKey)
        {
            if (string.IsNullOrEmpty(classKey)) return null;
            string key = classKey.Trim().ToLowerInvariant();
            switch (key)
            {
                case "warrior": case "paladin": case "hunter": case "rogue": case "priest":
                case "shaman": case "mage": case "warlock": case "druid":
                    break;
                default:
                    return null;
            }
            if (classGearHelmetImages == null) classGearHelmetImages = new Dictionary<string, Image>(StringComparer.OrdinalIgnoreCase);
            Image image;
            if (!classGearHelmetImages.TryGetValue(key, out image))
            {
                image = Theme.LoadImage("AS.favorites.class-" + key + ".jpg");
                classGearHelmetImages[key] = image;
            }
            return image;
        }

        static string FavoriteGearClass(GuideFavoriteRoute route)
        {
            if (route == null) return null;
            if (route.Kind == "plannerClass") return route.ClassKey;
            if (route.Kind != "guide" || !string.Equals(route.Topic, "Items", StringComparison.OrdinalIgnoreCase)) return null;
            foreach (string cls in BuilderClasses)
                if (string.Equals(route.Page, cls + " Gear", StringComparison.OrdinalIgnoreCase)) return cls;
            return null;
        }
        readonly IconButton[] favoriteBtns = new IconButton[GuideFavoriteCount];
        readonly GuideFavoriteRoute[] guideFavorites = new GuideFavoriteRoute[GuideFavoriteCount];
        IconButton favoriteDragButton;
        int favoriteDragCurrentSlot = -1, favoriteDragStartY;
        bool favoriteDragStarted, favoriteDragChanged;
        string current;
        string addonMode = "installed";
        int addonCategory;
        int clView = ClNewest, clPage;
        bool clToEnd;
        DateTime clLastStep = DateTime.MinValue;
        Dictionary<string, CheckBox> addonRows;
        Label addonCount;
        // An uninstall question takes the place of My Addons' right column, not a modal dialog.
        // History treats this as a view: Back navigates, but never chooses UNINSTALL or CANCEL. Name: a Download More
        // addon; folder: one folder of the list (its X)
        string askUninstallName, askUninstallFolder;

        static readonly string[] SectionNames = { "Guide", "Cloud Save", "Addons", "Changelog", "Stats", "Options", "About Launcher", "Status Log" };
        int gTopic = -1, gPage = -1;
        string gTalents;                                     // the Guide shows this class's talent trees (a test build)
        TalentBuild liveBuild;                               // the test build on those trees: kept only while they show
        string liveBuildCls;
        GuideVideo gVideo;                                   // the guide: a video playing inside the section (Guide.cs)                         // the guide: -1 = the topic list / the topic itself

        int PW { get { return panel.ClientSize.Width; } }
        int PH { get { return panel.ClientSize.Height; } }

        // ------------------------------------------------------------------ the window itself
        void BuildSide()
        {
            Color bronzeD = Theme.Hex("#2a2620"), dark = Theme.Hex("#0e0d14");   // night stone, like the launcher
            side = new Form();
            side.AutoScaleMode=AutoScaleMode.None;
            WowDialog.SecondaryHost = side;
            side.FormBorderStyle = FormBorderStyle.None;
            side.ShowInTaskbar = false;                      // the sections open inside the launcher, on its left side
            side.TopLevel = false;
            side.StartPosition = FormStartPosition.Manual;
            side.BackColor = bronzeD;
            side.Icon = Theme.AppIcon;
            side.Text = "Addons";
            side.Padding = new Padding(3);
            side.Size = new Size(SideW, SideH);
            side.KeyPreview = true;
            side.FormClosing += delegate(object s, FormClosingEventArgs e)
            {
                if (!quitting && e.CloseReason == CloseReason.UserClosing)     // Alt+F4 / taskbar close = the red X
                {
                    e.Cancel = true;
                    CloseSide();
                }
            };
            var frame = new Bordered(Theme.Hex("#c49a48"), 2);
            frame.Dock = DockStyle.Fill;
            side.Controls.Add(frame);
            var inner = new Panel();
            inner.Dock = DockStyle.Fill;
            inner.BackColor = dark;
            frame.Controls.Add(inner);

            var body = new Panel();
            body.Dock = DockStyle.Fill;
            body.BackColor = dark;
            body.Padding = new Padding(4, 2, 4, 4);
            inner.Controls.Add(body);
            sideHead = new SideHead();
            sideHead.Dock = DockStyle.Top;
            // (no Owner2: the section area does not move)
            sideHead.CloseClicked = CloseSide;
            sideHead.HelpClicked = ShowLostHelpInline;
            sideHead.HelpLeft = DeferHideLostHelpInline;
            sideHead.Moved = delegate(Point p)
            {
                Cfg.Set("side_pos", new List<object> { (long)p.X, (long)p.Y });
                Cfg.Save();
            };
            inner.Controls.Add(sideHead);

            // right: only the Guide locations the player has favorited
            strip = new Panel();
            strip.Dock = DockStyle.Right;
            strip.Width = 32;
            strip.BackColor = dark;
            body.Controls.Add(strip);
            LoadGuideFavorites();
            for (int i = 0; i < GuideFavoriteCount; i++)
            {
                int slot = i;
                Action<Graphics, float, float, Color> paint = delegate(Graphics g, float x, float y, Color c)
                {
                    GuideFavoriteRoute route = guideFavorites[slot];
                    Image favoriteIcon = null;
                    string gearClass = FavoriteGearClass(route);
                    if (gearClass != null)
                        favoriteIcon = FavoriteGearHelmet(gearClass);   // selected tier-set helmet for the class gear page
                    else if (route != null &&
                             ((route.Kind == "guide" && route.Topic == "Classes" && route.ClassKey.Length > 0) ||
                              route.Kind == "builderClass" || route.Kind == "guideTalents" ||
                              (route.Kind == "stats" && route.Page == "class")))
                        favoriteIcon = FavoriteClassIcon(route.ClassKey);
                    if (favoriteIcon == null && route != null &&
                        ((route.Kind == "guide" && route.Topic == "Classes" && route.ClassKey.Length > 0) ||
                         route.Kind == "builderClass" || route.Kind == "guideTalents" ||
                         (route.Kind == "stats" && route.Page == "class")))
                        favoriteIcon = SpellIcons.Class(route.ClassKey, favoriteBtns[slot]);
                    else if (route != null && route.Kind == "guide" && route.Topic == "Archive")
                    {
                        if (codexFavoriteIcon == null) codexFavoriteIcon = Theme.LoadImage("AS.favorites.codex-aura.png");
                        favoriteIcon = codexFavoriteIcon;
                    }
                    else if (route != null && route.Kind == "guide" &&
                             string.Equals(route.Topic, "Heroic & Endless", StringComparison.OrdinalIgnoreCase))
                    {
                        if (heroicFavoriteIcon == null) heroicFavoriteIcon = Theme.LoadImage("AS.favorites.heroic-icon.png");
                        favoriteIcon = heroicFavoriteIcon;
                    }
                    else if (route != null && route.Kind == "guide" && route.Topic == "Items" && route.Page == "Rings, Trinkets & Amulets")
                    {
                        if (jewelryFavoriteIcon == null) jewelryFavoriteIcon = Theme.LoadImage("AS.favorites.jewelry-card.jpg");
                        favoriteIcon = jewelryFavoriteIcon;
                        if (favoriteIcon == null) favoriteIcon = SpellIcons.Icon("inv_inscription_tarot_tsunamicard", favoriteBtns[slot]);
                    }
                    else if (route != null && route.Kind == "planner")
                    {
                        if (gearPlannerFavoriteIcon == null) gearPlannerFavoriteIcon = Theme.LoadImage("AS.favorites.gear-planner.jpg");
                        favoriteIcon = gearPlannerFavoriteIcon;
                    }
                    else if (route != null && route.Kind == "builder")
                    {
                        if (talentTreeFavoriteIcon == null) talentTreeFavoriteIcon = Theme.LoadImage("AS.favorites.talents-button.png");
                        favoriteIcon = talentTreeFavoriteIcon;
                        if (favoriteIcon == null) favoriteIcon = SpellIcons.TalentButton(favoriteBtns[slot]);
                    }
                    else if (route != null && route.Kind == "guide" &&
                             string.Equals(route.Topic, "Zones & Bosses", StringComparison.OrdinalIgnoreCase))
                    {
                        if (zonesBossesFavoriteIcon == null) zonesBossesFavoriteIcon = Theme.LoadImage("AS.favorites.zones-bosses.jpg");
                        favoriteIcon = zonesBossesFavoriteIcon;
                    }
                    if (favoriteIcon != null)
                    {
                        float extent=Math.Max(10,favoriteBtns[slot].Width-Theme.Px(8));
                        float scale = Math.Min(extent / favoriteIcon.Width, extent / favoriteIcon.Height);
                        var iconRect = new RectangleF(x - favoriteIcon.Width * scale / 2f, y - favoriteIcon.Height * scale / 2f,
                            favoriteIcon.Width * scale, favoriteIcon.Height * scale);
                        g.DrawImage(favoriteIcon, iconRect);
                        using (var p = new Pen(c, 1.2f)) g.DrawRectangle(p, Rectangle.Round(iconRect));
                        return;
                    }
                    float diameter=Math.Max(10,favoriteBtns[slot].Width-Theme.Px(8));
                    var ring = new RectangleF(x-diameter/2,y-diameter/2,diameter,diameter);
                    using (var p = new Pen(c, 1.4f)) g.DrawEllipse(p, ring);
                    using(var numberFont=new Font(Theme.F(9).FontFamily,Math.Max(6,diameter*(slot>=9?.55f:.75f)),FontStyle.Bold,GraphicsUnit.Pixel))
                        Theme.CenterText(g, (slot + 1).ToString(), numberFont, c, ring, null);
                };
                var b = new IconButton(paint, delegate { OpenGuideFavorite(slot); });
                b.AccessibleName = Lang.T("Quick Access " + (i + 1));
                b.Tip = "Quick Access " + (i + 1) + " is empty. Use the guide star or a section favorite control to save a location.";
                b.ContextClicked += delegate(Control source, Point at) { RemoveGuideFavorite(slot); };
                b.MouseDown += delegate(object sender, MouseEventArgs e)
                {
                    if (setupOpen || e.Button != MouseButtons.Left) return;
                    favoriteDragButton = b;
                    favoriteDragCurrentSlot = slot;
                    favoriteDragStartY = strip.PointToClient(Cursor.Position).Y;
                    favoriteDragStarted = favoriteDragChanged = false;
                    SetFavoriteDropTarget(-1);
                };
                b.MouseMove += delegate(object sender, MouseEventArgs e)
                {
                    if (favoriteDragButton != b || (Control.MouseButtons & MouseButtons.Left) == 0) return;
                    int y = strip.PointToClient(Cursor.Position).Y;
                    if (!favoriteDragStarted && Math.Abs(y - favoriteDragStartY) >= Math.Max(4, SystemInformation.DragSize.Height / 2))
                        favoriteDragStarted = true;
                    if (!favoriteDragStarted) return;
                    int target = y / 31;
                    if (target < 0) target = 0;
                    if (target >= GuideFavoriteCount) target = GuideFavoriteCount - 1;
                    SetFavoriteDropTarget(target);
                    if (MoveGuideFavoriteTo(favoriteDragCurrentSlot, target))
                    {
                        favoriteDragCurrentSlot = target;
                        favoriteDragChanged = true;
                        b.SuppressNextClick = true;
                    }
                };
                b.MouseUp += delegate(object sender, MouseEventArgs e)
                {
                    if (favoriteDragButton != b) return;
                    if (favoriteDragChanged) { SaveGuideFavorites(); RefreshStrip(); }
                    favoriteDragButton = null;
                    favoriteDragCurrentSlot = -1;
                    favoriteDragStarted = favoriteDragChanged = false;
                    SetFavoriteDropTarget(-1);
                };
                b.KeyDown += delegate(object sender, KeyEventArgs e)
                {
                    if (setupOpen || !e.Shift || (e.KeyCode != Keys.Up && e.KeyCode != Keys.Down)) return;
                    int target = slot + (e.KeyCode == Keys.Up ? -1 : 1);
                    if (MoveGuideFavoriteTo(slot, target))
                    {
                        SaveGuideFavorites();
                        RefreshStrip();
                        if (target >= 0 && target < favoriteBtns.Length && favoriteBtns[target] != null) favoriteBtns[target].Focus();
                    }
                    e.Handled = e.SuppressKeyPress = true;
                };
                favoriteBtns[i] = b;
                strip.Controls.Add(b);
            }
            // left: the chat-style area that holds the section
            var chat = new Bordered(Theme.Hex("#8a7040"), 2);
            chat.Dock = DockStyle.Fill;
            body.Controls.Add(chat);
            chat.BringToFront();
            panel = new Panel();
            panel.Dock = DockStyle.Fill;
            panel.BackColor = Theme.Panel;
            panel.Paint += delegate(object s, PaintEventArgs e) { PaintBackdrop(e.Graphics, panel); };   // Stats: see-through
            chat.Controls.Add(panel);
            lostHelpCard = new ScrollPanel();
            lostHelpCard.BackColor = Theme.Hex("#120f18");
            lostHelpCard.Visible = false;
            lostHelpCard.Paint += PaintLostHelpInline;
            lostHelpCard.MouseEnter += delegate { if (lostHelpTimer != null) lostHelpTimer.Stop(); };
            lostHelpCard.MouseLeave += delegate { DeferHideLostHelpInline(); };
            chat.Controls.Add(lostHelpCard);
            lostHelpCard.BringToFront();
            closeDownloadCard = new ScrollPanel();
            closeDownloadCard.Dock = DockStyle.Fill;
            closeDownloadCard.BackColor = Theme.Hex("#0e0d14");
            closeDownloadCard.Visible = false;
            closeDownloadCard.Paint += PaintDownloadClosePrompt;
            cancelDownloadCloseButton = new WowButton("CANCEL DOWNLOAD & CLOSE", CloseAfterCancellingGameDownload, 210, 34, 8.5f);
            cancelDownloadCloseButton.Skin = Retro.Button;
            keepDownloadButton = new WowButton("STAY & FINISH", KeepGameDownloadRunning, 180, 34, 8.5f);
            keepDownloadButton.Skin = Retro.Button;
            closeDownloadCard.Controls.Add(cancelDownloadCloseButton);
            closeDownloadCard.Controls.Add(keepDownloadButton);
            closeDownloadCard.Resize += delegate { LayoutDownloadClosePrompt(); };
            chat.Controls.Add(closeDownloadCard);
            closeDownloadCard.BringToFront();
            Deactivate += delegate { HideLostHelpInline(); };
            LayoutStrip();
            RefreshStrip();
            Controls.Add(side);
            sideHead.SetHoverHost(this);
            PlaceSide();
            side.PerformLayout();
        }

        void LayoutStrip()
        {
            int count = 0;
            for (int i = 0; i < guideFavorites.Length; i++) if (guideFavorites[i] != null) count++;
            int gap = Theme.Px(3), y = 0;
            int size = Math.Min(Math.Max(16, strip.Width - 6), Math.Max(16, (strip.ClientSize.Height - Math.Max(0, count - 1) * gap) / Math.Max(1, count)));
            for (int i = 0; i < favoriteBtns.Length; i++)
            {
                var button = favoriteBtns[i]; button.Visible = guideFavorites[i] != null;
                if (!button.Visible) continue;
                button.Bounds = new Rectangle((strip.Width - size) / 2, y, size, size);
                y += size + gap;
            }
        }

        void SetFavoriteDropTarget(int target)
        {
            int count = 0;
            while (count < guideFavorites.Length && guideFavorites[count] != null) count++;
            if (target >= count) target = count - 1;
            for (int i = 0; i < favoriteBtns.Length; i++)
                if (favoriteBtns[i] != null)
                    favoriteBtns[i].DropHighlighted = favoriteDragButton != null && favoriteDragStarted && i == target;
        }

        void RefreshStrip()
        {
            for (int i = 0; i < favoriteBtns.Length; i++)
            {
                if (favoriteBtns[i] == null) continue;
                GuideFavoriteRoute route = guideFavorites[i];
                bool bound = route != null;
                favoriteBtns[i].Visible = bound;
                favoriteBtns[i].GrayedTip = setupOpen ? "Finish first-time setup first." : "";
                favoriteBtns[i].Grayed = setupOpen;
                favoriteBtns[i].Tip = setupOpen ? "" : bound ? "Quick Access " + (i + 1) + ": " + route.PageLabel +
                    " (click to open; hold left-click and drag or use Shift+Up/Down to move; right-click or press Menu/Shift+F10, then choose Remove; press Esc to cancel)" : "";
                bool selected = false;
                if (bound && current == "Guide")
                {
                    if (route.Kind == "group")
                    {
                        for (int group = 0; group < 3; group++) if (GroupName(group) == route.Topic) selected = guideGroupPage == group;
                    }
                    else if (route.Kind == "section") selected = route.Topic == "Guide" && guideGroupPage < 0 && gTopic < 0 && !gBuilder && !gPlanner && gVideo == null;
                    else if (route.Kind == "builder") selected = gBuilder && gTalents == null;
                    else if (route.Kind == "planner") selected = gPlanner && plannerClass == null;
                    else if (route.Kind == "builderClass") selected = gBuilder && gTalents == route.ClassKey;
                    else if (route.Kind == "plannerClass") selected = gPlanner && plannerClass == route.ClassKey;
                    else if (route.Kind == "guideTalents") selected = gTalents == route.ClassKey && gTopic >= 0 && gTopic < Data.Guide.Count && Data.Guide[gTopic].Title == route.Topic;
                    else if (gTopic >= 0 && gTopic < Data.Guide.Count)
                        selected = Data.Guide[gTopic].Title == route.Topic && (gPage < 0 ? route.Page.Length == 0 :
                            gPage < Data.Guide[gTopic].Pages.Count && Data.Guide[gTopic].Pages[gPage].Title == route.Page);
                }
                else if (bound && route.Kind == "section")
                    selected = route.Topic == current || route.Topic == "Cloud Save" && current == "Options" && optionsTab == 1;
                else if (bound && route.Kind == "option" && current == "Options")
                {
                    int optionTab;
                    selected = route.Page == "about" ? optionsAbout : route.Page == "language" ? optionsLang :
                        route.Page == "tab" && int.TryParse(route.ClassKey, out optionTab) && optionTab == optionsTab && !optionsAbout && !optionsLang;
                }
                else if (bound && route.Kind == "addon" && current == "Addons")
                {
                    int category;
                    selected = addonMode == "download" && int.TryParse(route.ClassKey, out category) && addonCategory == category;
                }
                else if (bound && route.Kind == "stats" && current == "Stats")
                {
                    int tab;
                    selected = route.Page == "class" ? statsClass == route.ClassKey && statsTab == 0 :
                        int.TryParse(route.Url, out tab) && statsTab == tab && (route.ClassKey.Length == 0 ? statsClass == null : statsClass == route.ClassKey);
                }
                else if (bound && route.Kind == "setup" && current == "Setup")
                {
                    int setupStep;
                    selected = int.TryParse(route.ClassKey, out setupStep) && setupPage == setupStep;
                }
                favoriteBtns[i].Active = !setupOpen && selected;
            }
            LayoutStrip();
        }

        void SetSideTitle(string text)
        {
            sideHead.Closable = current != "Community" || communityPlaying != null || communityMore;
            sideHead.CloseEnabled = current != "Setup";
            sideHead.Title = text;
            sideHead.Note = "";                              // the Community player sets its own after this
            side.Text = Lang.T(Array.IndexOf(SectionNames, current) >= 0 ? current : text);    // the taskbar and Alt+Tab name
        }

        // the open section fills the left side of the launcher window
        void PlaceSide()
        {
            side.Bounds = SideRect;                          // as tall as the News column: from "News" to the last Launcher button
            side.BringToFront();
        }

        // the section area: its top level with the top of the word "News", its bottom level with the Launcher buttons
        // (a little taller than the News column: from under the game's name down to the bottom bar, for more room)
        Rectangle SideRect { get { return ResponsiveSideRect; } }
        Panel lostHelpCard;
        Timer lostHelpTimer;

        void ShowLostHelpInline()
        {
            if (lostHelpCard == null || side == null || !side.Visible) return;
            if (lostHelpTimer != null) lostHelpTimer.Stop();
            int width = Math.Min(Theme.Px(360), lostHelpCard.Parent.ClientSize.Width - 12);
            int height = Math.Min(Theme.Px(320), lostHelpCard.Parent.ClientSize.Height - 12);
            lostHelpCard.Size = new Size(Math.Max(220, width), Math.Max(220, height));
            Point cursor = lostHelpCard.Parent.PointToClient(Cursor.Position);
            int left = Math.Max(0, Math.Min(lostHelpCard.Parent.ClientSize.Width - lostHelpCard.Width, cursor.X));
            int top = cursor.Y + 20;
            if (top + lostHelpCard.Height > lostHelpCard.Parent.ClientSize.Height)
                top = Math.Max(0, lostHelpCard.Parent.ClientSize.Height - lostHelpCard.Height);
            lostHelpCard.Location = new Point(left, top);
            lostHelpCard.Visible = true;
            lostHelpCard.BringToFront();
            lostHelpCard.Invalidate();
        }

        void DeferHideLostHelpInline()
        {
            if (lostHelpCard == null || !lostHelpCard.Visible) return;
            if (lostHelpTimer == null)
            {
                lostHelpTimer = new Timer();
                lostHelpTimer.Interval = 120;
                lostHelpTimer.Tick += delegate
                {
                    lostHelpTimer.Stop();
                    bool overButton = sideHead != null && sideHead.HelpScreenBounds.Contains(Cursor.Position);
                    bool overCard = lostHelpCard.Visible && lostHelpCard.RectangleToScreen(lostHelpCard.ClientRectangle).Contains(Cursor.Position);
                    if (!overButton && !overCard) HideLostHelpInline();
                };
            }
            lostHelpTimer.Stop();
            lostHelpTimer.Start();
        }

        void HideLostHelpInline()
        {
            if (lostHelpTimer != null) lostHelpTimer.Stop();
            if (lostHelpCard != null && lostHelpCard.Visible) lostHelpCard.Visible = false;
        }

        void PaintLostHelpInline(object sender, PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.Clear(Theme.Hex("#120f18"));
            using (var p = new Pen(Theme.Hex("#c49a48"), 2)) g.DrawRectangle(p, 1, 1, lostHelpCard.Width - 3, lostHelpCard.Height - 3);
            var scroll=lostHelpCard as ScrollPanel;
            if(scroll!=null) g.TranslateTransform(0,scroll.AutoScrollPosition.Y);
            int y = Theme.Px(10), width = lostHelpCard.ClientSize.Width - Theme.Px(24);
            TextRenderer.DrawText(g, Lang.T("Lost?"), Theme.F(11, FontStyle.Bold), new Rectangle(12, y, width, Theme.Px(20)), Theme.Gold,
                                  TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine | TextFormatFlags.NoPadding);
            y += Theme.Px(26);
            DrawLostHelpSection(g, "Click the star on a page to save it to Favorites. On Wiki pages, choose your current reading spot or the top of the page.", null, ref y, width);
            DrawLostHelpSection(g, "Click a Favorites tab to open it. Hold left-click and drag to reorder tabs. Right-click a tab to remove it.", PathExamplePart(false), ref y, width);
            if(scroll!=null) scroll.AutoScrollMinSize=new Size(0,y+Theme.Px(8));
        }

        void DrawLostHelpSection(Graphics g, string text, Image picture, ref int y, int width)
        {
            Font font = Theme.F(9.5f);
            int textHeight = TextRenderer.MeasureText(text, font, new Size(width, 500), TextFormatFlags.WordBreak | TextFormatFlags.NoPrefix).Height;
            TextRenderer.DrawText(g, Lang.T(text), font, new Rectangle(12, y, width, textHeight), Theme.Text,
                                  TextFormatFlags.WordBreak | TextFormatFlags.NoPrefix);
            y += textHeight + 5;
            if (picture == null) return;
            float scale = Math.Min(Theme.Px(240) / (float)picture.Width, Theme.Px(160) / (float)picture.Height);
            scale = Math.Min(scale, (float)(width - 12) / picture.Width);
            int w = Math.Max(1, (int)(picture.Width * scale)), h = Math.Max(1, (int)(picture.Height * scale));
            g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
            g.DrawImage(picture, new Rectangle((lostHelpCard.Width - w) / 2, y, w, h));
            y += h + 8;
        }

        void ShowSide()
        {
            PlaceSide();
            side.Show();
            side.BringToFront();
        }

        void HideSide()
        {
            GuideHover.Hide();
            HideLostHelpInline();
            HideDownloadClosePrompt();
            StopEdge();
            communityPlaying = null;
            if (side.Visible) side.Hide();
        }

        void ShowDownloadClosePrompt()
        {
            if (!Installing) { QuitApp(); return; }
            ShowWindowFront();
            if (!side.Visible)
            {
                current = "Options";
                optionsTab = 4;
                ShowSide();
                BuildPanel("Options");
            }
            closeAfterDownloadCancel = false;
            LayoutDownloadClosePrompt();
            closeDownloadCard.Visible = true;
            closeDownloadCard.BringToFront();
            cancelDownloadCloseButton.Focus();
        }

        void HideDownloadClosePrompt()
        {
            if (closeDownloadCard != null) closeDownloadCard.Visible = false;
        }

        Rectangle closeTitleBounds, closeMessageBounds;
        Font closeTitleFont, closeMessageFont;
        bool arrangingDownloadClose;
        string DownloadCloseTitle { get {return Lang.T(dlActive?"The game is downloading":"The game is being installed");} }
        string DownloadCloseMessage { get {return Lang.T(dlActive
            ? "Would you like to cancel the download and close the launcher, or leave it open until the download finishes?"
            : "The download is complete and the game files are being installed. Choose whether to close after installation finishes or leave the launcher open.");} }
        void LayoutDownloadClosePrompt()
        {
            if(closeDownloadCard==null||arrangingDownloadClose)return;
            arrangingDownloadClose=true;
            float previous=Theme.Zoom; Theme.Zoom=Theme.SectionZoom;
            try
            {
                int pad=Theme.Px(18),gap=Theme.Px(10),width=Math.Max(60,closeDownloadCard.ClientSize.Width-pad*2);
                closeTitleFont=Theme.F(15,FontStyle.Bold); closeMessageFont=Theme.F(10);
                int titleHeight=TextRenderer.MeasureText(DownloadCloseTitle,closeTitleFont,new Size(width,1000),TextFormatFlags.WordBreak|TextFormatFlags.NoPrefix).Height;
                int bodyHeight=TextRenderer.MeasureText(DownloadCloseMessage,closeMessageFont,new Size(width,2000),TextFormatFlags.WordBreak|TextFormatFlags.NoPrefix).Height;
                closeTitleBounds=new Rectangle(pad,pad,width,titleHeight);
                closeMessageBounds=new Rectangle(pad,closeTitleBounds.Bottom+gap,width,bodyHeight);
                cancelDownloadCloseButton.Label=dlActive?"CANCEL DOWNLOAD & CLOSE":"CLOSE WHEN FINISHED";
                keepDownloadButton.Label="STAY & FINISH";
                int firstWidth=(int)(210*Theme.Zoom), secondWidth=(int)(180*Theme.Zoom), height=Theme.Px(30);
                bool stacked=firstWidth+secondWidth+gap>width;
                int total=stacked?Math.Min(width,Math.Max(firstWidth,secondWidth)):firstWidth+secondWidth+gap;
                int x=pad+(width-total)/2,y=closeMessageBounds.Bottom+Theme.Px(18);
                cancelDownloadCloseButton.SetDisplayZoom(Theme.Zoom);keepDownloadButton.SetDisplayZoom(Theme.Zoom);
                cancelDownloadCloseButton.Bounds=new Rectangle(x,y,stacked?total:firstWidth,height);
                keepDownloadButton.Bounds=new Rectangle(stacked?x:x+firstWidth+gap,stacked?y+height+gap:y,stacked?total:secondWidth,height);
                var scroll=closeDownloadCard as ScrollPanel;
                if(scroll!=null)scroll.AutoScrollMinSize=new Size(0,keepDownloadButton.Bottom+pad);
            }
            finally {Theme.Zoom=previous;arrangingDownloadClose=false;}
        }

        void PaintDownloadClosePrompt(object sender,PaintEventArgs e)
        {
            Graphics g=e.Graphics;g.Clear(Theme.Hex("#0e0d14"));
            using(var pen=new Pen(Theme.Hex("#c49a48"),2))g.DrawRectangle(pen,1,1,closeDownloadCard.Width-3,closeDownloadCard.Height-3);
            var scroll=closeDownloadCard as ScrollPanel;
            if(scroll!=null)g.TranslateTransform(0,scroll.AutoScrollPosition.Y);
            if(closeTitleFont==null)return;
            TextRenderer.DrawText(g,DownloadCloseTitle,closeTitleFont,closeTitleBounds,Theme.Gold,TextFormatFlags.HorizontalCenter|TextFormatFlags.WordBreak|TextFormatFlags.NoPrefix);
            TextRenderer.DrawText(g,DownloadCloseMessage,closeMessageFont,closeMessageBounds,Theme.Text,TextFormatFlags.HorizontalCenter|TextFormatFlags.WordBreak|TextFormatFlags.NoPrefix);
        }

        // closing a section goes back to the home screen, which shows Community; Community itself cannot be closed
        void CloseSide()
        {
            if (current == "Community")
            {
                if (communityPlaying != null || communityMore) LeaveCommunityVideo();
                return;
            }
            if (current == "Guide" && gPlanner) ResetPlannerState();              // the home screen's Community can only be replaced, never closed
            if (current == "Setup") return;                  // setup cannot be closed before it is done (or cancelled)
            current = "Home";
            foreach (var b in nav.Values) b.Active = false;
            RefreshStrip();
            ShowHomeCommunity();
        }

        public void ShowSection(string name)
        {
            if (setupOpen && name != "Setup") { SetStatus("Finish the first-time setup first."); return; }   // setup stays up until done
            if (name != "Options") optionsAbout = false;
            if (name == "Cloud Save") { optionsTab = 1; optionsLang = false; optionsAbout = false; name = "Options"; }   // it lives in Options > CLOUD SAVE
            // everything works without the game except Cloud Save and Addons, which are grayed out until it is installed
            if ((name == "Cloud Save" || name == "Addons") && !GateOpen())
            {
                SetStatus(name == "Addons" ? AddonsNeedGame : CloudNeedsGame);
                ShowWindowFront();
                return;
            }
            if (name == "Stats" && !StatsAddonActive())     // the Stats page needs a supported stats addon on
            {
                SetStatus(StatsNeedAddon);
                if (current == "Stats") { current = null; ShowSection("Community"); }
                return;
            }
            if (name == "Guide" && current == "Guide" && side.Visible)   // the Guide's button again: back to its main page
            {
                if (gPlanner) ResetPlannerState();
                guideGroupPage = 1;
                gTopic = -1; gPage = -1; guideQuery = ""; gBuilder = false; gPlanner = false; gTalents = null; gVideo = null;
                guideMain = false; guideWikiIndex = true; guideBuildVideos = false;
                BuildPanel("Guide");
                return;
            }
            if (name == current && name != "Home" && side.Visible) return;   // already open: clicking its button again does nothing
            if (current == "Guide" && name != "Guide" && gPlanner) ResetPlannerState();
            current = name;
            foreach (var kv in nav) kv.Value.Active = kv.Key == name;
            RefreshStrip();
            if (name == "Changelog") clView = ClList;          // the list of every patch note to pick from
            if (name == "Guide") { gTopic = -1; gPage = -1; guideQuery = ""; guideLastTopic = 0; gBuilder = false; gPlanner = false; gTalents = null; guideGroup = 1; guideGroupPage = 1; guideMain = false; guideWikiIndex = true; guideBuildVideos = false; }   // the Guide button opens the Wiki introduction
            if (name == "About Launcher") aboutLog = false;
            if (name == "Home")
            {
                current = null;
                ShowSection("Community");
                return;
            }
            ShowSide();
            BuildPanel(name);
        }

        // While a section is rebuilt, nothing in it is drawn: the old page stays on screen until the new one is complete,
        // then it is drawn at once (before, each new control showed up as an empty grey box for a moment)
        [System.Runtime.InteropServices.DllImport("user32.dll")]
        static extern IntPtr SendMessage(IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam);
        [System.Runtime.InteropServices.DllImport("user32.dll")]
        static extern bool RedrawWindow(IntPtr hWnd, IntPtr rect, IntPtr region, uint flags);
        const int WM_SETREDRAW = 0x000B;
        int frozen;

        void Freeze(bool on)
        {
            if (panel == null || panel.IsDisposed || !panel.IsHandleCreated) return;
            if (on)
            {
                if (frozen++ == 0) SendMessage(panel.Handle, WM_SETREDRAW, IntPtr.Zero, IntPtr.Zero);
            }
            else if (frozen > 0 && --frozen == 0)
            {
                SendMessage(panel.Handle, WM_SETREDRAW, new IntPtr(1), IntPtr.Zero);
                // erase, invalidate, every child, now
                RedrawWindow(panel.Handle, IntPtr.Zero, IntPtr.Zero, 0x0004 | 0x0001 | 0x0080 | 0x0100 | 0x0400);
            }
        }

        int panelGeneration;
        bool buildingPanel;
        string pendingPanel;
        bool panelRebuildQueued;
        void BuildPanel(string name)
        {
            if(buildingPanel)
            {
                pendingPanel=name;
                return;
            }
            buildingPanel=true;
            try { BuildPanelCore(name); }
            finally
            {
                buildingPanel=false;
                if(pendingPanel!=null&&!panelRebuildQueued&&IsHandleCreated&&!IsDisposed)
                {
                    panelRebuildQueued=true;
                    BeginInvoke((MethodInvoker)delegate
                    {
                        panelRebuildQueued=false;
                        string target=pendingPanel; pendingPanel=null;
                        if(target!=null&&!IsDisposed) BuildPanel(target);
                    });
                }
            }
        }

        void BuildPanelCore(string name)
        {
            panelGeneration++;
            CaptureDisplayedScrollPositions();
            GuideHover.Hide();
            HideLostHelpInline();
            if (name != "Community") communityPlaying = null;   // another section: the Community video is left
            // the talent trees left (another page, class or section): their test build starts empty next time
            if (name != "Guide" || (!gPlanner && (gTalents == null || gTalents != liveBuildCls))) { liveBuild = null; liveBuildCls = null; }
            bool freeze = !PreviewMode && name != "Community";  // Community's player window is another program's
            if (freeze) Freeze(true);
            panel.SuspendLayout();
            Theme.Zoom = Theme.SectionZoom;                  // everything in a section is a little smaller
            try
            {
                if (name == "Cloud Save") PanelCloud();
                else if (name == "Addons") PanelAddons();
                else if (name == "Options") PanelOptions();
                else if (name == "Changelog") PanelChangelog();
                else if (name == "Guide") PanelGuide();
                else if (name == "Stats") PanelStats();
                else if (name == "About Launcher") PanelAbout();
                else if (name == "Status Log") PanelStatusLog();
                else if (name == "Community") PanelCommunity();
                else if (name == "Setup") PanelSetup();          // first-time setup (Setup.cs)
                RememberView(name);                              // the back arrow's way (History.cs)
                displayedView = name != "Setup" && history.Count > 0 ? history[history.Count - 1] : null;
                // "Lost?" wherever there is a path to follow: not in Community or Setup,
                // and not while a Guide video plays
                sideHead.HelpShown = name != "Community" && name != "Setup" && !(name == "Guide" && gVideo != null);
            }
            finally
            {
                Theme.Zoom = Theme.DisplayScale;
                panelDensity=Theme.DisplayScale;
                panel.ResumeLayout();
                if (freeze) Freeze(false);
            }
            if (restoreScrollPositions != null)
            {
                int[] positions = restoreScrollPositions;
                restoreScrollPositions = null;
                RestoreDisplayedScrollPositions(positions);
            }
            if (name == "Addons") RefreshGates();             // the stats add-on switched on or off: the Stats buttons follow
        }

        // the "Lost?" note demonstrates both breadcrumb navigation and saving a favorite
        static readonly Dictionary<string, Bitmap> pathExamples = new Dictionary<string, Bitmap>();
        static readonly Dictionary<string, Bitmap[]> pathExampleParts = new Dictionary<string, Bitmap[]>();

        static void DrawExampleStar(Graphics g, float centerX, float centerY, float size)
        {
            using (var path = new GraphicsPath())
            {
                PointF[] points = new PointF[10];
                for (int i = 0; i < points.Length; i++)
                {
                    double angle = -Math.PI / 2 + i * Math.PI / 5;
                    float radius = i % 2 == 0 ? size / 2f : size / 4.5f;
                    points[i] = new PointF(centerX + (float)Math.Cos(angle) * radius,
                                           centerY + (float)Math.Sin(angle) * radius);
                }
                path.AddPolygon(points);
                using (var brush = new SolidBrush(Theme.Gold)) g.FillPath(brush, path);
            }
        }

        static Bitmap PathExample()
        {
            string language = Lang.Code ?? "en";
            Bitmap cached;
            if (pathExamples.TryGetValue(language, out cached)) return cached;
            float previousZoom=Theme.Zoom, previousDensity=Theme.DisplayScale;
            Theme.Zoom=.72f; Theme.DisplayScale=1f;
            var bmp = new Bitmap(300, 235);
            try
            {
            using (var g = Graphics.FromImage(bmp))
            {
                g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
                g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;
                g.Clear(Theme.Hex("#120e18"));
                using (var p = new Pen(Theme.Hex("#8a7040"), 1)) g.DrawRectangle(p, 0, 0, bmp.Width - 1, bmp.Height - 1);
                var first = new Rectangle(5, 4, 290, 130);
                var second = new Rectangle(5, 144, 290, 83);
                using (var b = new SolidBrush(Theme.Hex("#0e0d14")))
                {
                    g.FillRectangle(b, first);
                    g.FillRectangle(b, second);
                }
                using (var p = new Pen(Theme.Hex("#8a7040"), 1))
                {
                    g.DrawRectangle(p, first);
                    g.DrawRectangle(p, second);
                }

                Font heading = Theme.F(8, FontStyle.Bold), crumbFont = Theme.F(8.5f, FontStyle.Bold), menuFont = Theme.F(8, FontStyle.Bold);
                TextRenderer.DrawText(g, Lang.T("1  FOLLOW THE PATH"), heading, new Rectangle(10, 7, 180, 14), Theme.Gold,
                                      TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine | TextFormatFlags.NoPadding);

                // Breadcrumb example with an open drop-down under the step that has the arrow.
                var pathBar = new Rectangle(9, 41, 282, 23);
                using (var b = new SolidBrush(Theme.Hex("#1c1a26"))) g.FillRectangle(b, pathBar);
                using (var p = new Pen(Theme.Hex("#d6a640"), 1)) g.DrawRectangle(p, pathBar);
                string firstCrumb = Lang.T("Guide") + "  ›  " + Lang.T("New Player");
                TextRenderer.DrawText(g, firstCrumb, crumbFont, new Rectangle(16, 45, 128, 15), Theme.Hex("#e6cf8c"),
                                      TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine | TextFormatFlags.NoPadding);
                int groupStart = 16 + TextRenderer.MeasureText(Lang.T("Guide") + "  ›  ", crumbFont,
                                     new Size(128, 20), TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine | TextFormatFlags.NoPadding).Width;
                Rectangle arrow = new Rectangle(groupStart + TextRenderer.MeasureText(Lang.T("New Player"), crumbFont,
                                     new Size(100, 18), TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine | TextFormatFlags.NoPadding).Width + 1, 47, 11, 12);
                using (var b = new SolidBrush(Theme.Hex("#e6cf8c")))
                    g.FillPolygon(b, new[] { new PointF(arrow.X + 2, arrow.Y + 3), new PointF(arrow.Right - 2, arrow.Y + 3),
                                             new PointF(arrow.X + arrow.Width / 2f, arrow.Y + 8) });
                var menu = new Rectangle(groupStart - 4, 64, 142, 60);
                using (var b = new SolidBrush(Theme.Hex("#0e0d14"))) g.FillRectangle(b, menu);
                using (var p = new Pen(Theme.Hex("#8a7040"), 1)) g.DrawRectangle(p, menu);
                string[] choices = { "Getting Started", "Controls", "Your First Run", "Zones & Bosses" };
                for (int i = 0; i < choices.Length; i++)
                {
                    Rectangle item = new Rectangle(menu.X + 2, menu.Y + 2 + i * 14, menu.Width - 4, 14);
                    if (i == 3) using (var b = new SolidBrush(Theme.Hex("#3a2c18"))) g.FillRectangle(b, item);
                    TextRenderer.DrawText(g, Lang.T(choices[i]), menuFont, new Rectangle(item.X + 4, item.Y, item.Width - 6, item.Height),
                                          i == 3 ? Theme.Gold : Theme.Hex("#f5e9c8"), TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine | TextFormatFlags.NoPadding);
                }

                TextRenderer.DrawText(g, Lang.T("2  SAVE A FAVORITE"), heading, new Rectangle(10, 148, 180, 14), Theme.Gold,
                                      TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine | TextFormatFlags.NoPadding);
                var savePath = new Rectangle(9, 178, 171, 38);
                using (var b = new SolidBrush(Theme.Hex("#15121c"))) g.FillRectangle(b, savePath);
                using (var p = new Pen(Theme.Hex("#d6a640"), 1)) g.DrawRectangle(p, savePath);
                TextRenderer.DrawText(g, Lang.T("Priest guide"), crumbFont, new Rectangle(savePath.X + 6, savePath.Y + 5, 118, 14),
                                      Theme.Hex("#e6cf8c"), TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine | TextFormatFlags.NoPadding);
                TextRenderer.DrawText(g, Lang.T("Gear section"), menuFont, new Rectangle(savePath.X + 6, savePath.Y + 20, 118, 13),
                                      Theme.GoldDim, TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine | TextFormatFlags.NoPadding);
                DrawExampleStar(g, savePath.Right - 12, savePath.Top + 12, 15);

                TextRenderer.DrawText(g, Lang.T("QUICK ACCESS"), Theme.F(6.5f, FontStyle.Bold), new Rectangle(202, 169, 82, 11),
                                      Theme.Hex("#d9c48a"), TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine | TextFormatFlags.NoPadding);
                var favorite = new Rectangle(202, 182, 83, 22);
                using (var b = new SolidBrush(Theme.Hex("#1c1a26"))) g.FillRectangle(b, favorite);
                using (var p = new Pen(Theme.Hex("#d6a640"), 1)) g.DrawRectangle(p, favorite);
                using (var p = new Pen(Theme.Hex("#f0c050"), 1.3f)) g.DrawEllipse(p, 205, 185, 15, 15);
                Theme.CenterText(g, "1", Theme.F(7, FontStyle.Bold), Theme.Gold, new RectangleF(205, 185, 15, 15), null);
                TextRenderer.DrawText(g, Lang.T("Priest"), menuFont, new Rectangle(225, 185, 55, 15), Theme.Hex("#f5e9c8"),
                                      TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine | TextFormatFlags.NoPadding);
                using (var p = new Pen(Theme.Hex("#d9c48a"), 1.5f))
                {
                    g.DrawLine(p, 181, 193, 199, 193);
                    g.DrawLine(p, 199, 193, 195, 189);
                    g.DrawLine(p, 199, 193, 195, 197);
                }
            }
            pathExamples[language] = bmp;
            return bmp;
            }
            finally { Theme.Zoom=previousZoom; Theme.DisplayScale=previousDensity; }
        }

        static Image PathExamplePart(bool path)
        {
            string language = Lang.Code ?? "en";
            Bitmap[] parts;
            if (!pathExampleParts.TryGetValue(language, out parts))
            {
                Bitmap whole = PathExample();
                parts = new[]
                {
                    whole.Clone(new Rectangle(0, 0, whole.Width, 138), whole.PixelFormat),
                    whole.Clone(new Rectangle(0, 140, whole.Width, 95), whole.PixelFormat)
                };
                pathExampleParts[language] = parts;
            }
            return parts[path ? 0 : 1];
        }

        public void RebuildCurrent()
        {
            RefreshPlayLabel();
            Invalidate();
            if (current != null && current != "Home" && side.Visible) BuildPanel(current);
        }

        // ------------------------------------------------------------------ building blocks
        // Empties the section area; the title goes in the title bar. Every section gets the same back arrow.
        void ClearPanel(string title)
        {
            GuideHover.Hide();
            StopEdge();                                      // a video playing in Community (Community.cs) stops
            edgeHost = null;
            logBox = null;
            while (panel.Controls.Count > 0)
            {
                Control c = panel.Controls[0];
                panel.Controls.RemoveAt(0);
                string snapshot=PreviewMode?ScalingControlSnapshot(c,0):"";
                try { c.Dispose(); }
                catch(Exception error) { throw new InvalidOperationException("Disposing "+c.GetType().FullName+" while opening "+title+"\n"+snapshot,error); }
            }
            if (SeeThroughSection) { SeeThroughSection = false; panel.Invalidate(); }
            SetSideTitle(title);
            // the back arrow and the trail live in the title bar: "Addons › Download More"
            bool back = current == "Setup" ? setupPage > 0 : current != "Community" || communityPlaying != null || communityMore;
            sideHead.BackClicked = back ? (Action)GoBack : null;
            if (current == "Setup")
            {
                var setupTrail = new List<KeyValuePair<string, Action>>();
                setupTrail.Add(new KeyValuePair<string, Action>("Setup", null));
                int last = setupPage == 7 ? 6 : Math.Min(setupPage, SetupSteps.Length - 1);
                for (int i = 0; i <= last; i++)
                {
                    int page = i;
                    Action go = setupPage > 0 && page == setupPage - 1 ? (Action)SetupBack : null;
                    setupTrail.Add(new KeyValuePair<string, Action>(SetupSteps[i], go));
                }
                if (setupPage == 7) setupTrail.Add(new KeyValuePair<string, Action>("Setup cancelled", (Action)SetupBack));
                sideHead.SetCrumbs(setupTrail);
                return;
            }
            var trail = new List<KeyValuePair<string, Action>>();
            string sec = current;
            if (sec != null && sec != "Home" && sec != title)
                trail.Add(new KeyValuePair<string, Action>(sec, delegate { SectionRoot(sec); }));
            trail.Add(new KeyValuePair<string, Action>(title, null));
            sideHead.SetCrumbs(trail);
            BindCrumbFavorites(trail);
        }

        // the drop-down of trail step i: name, action, name, action, ...
        void SetCrumbMenu(int i, List<KeyValuePair<string, Action>> items) { sideHead.SetMenu(i, items); }

        void SetCrumbs(params object[] steps)                // "Guide", action, "Theorycrafting", action, ..., "Priest", null
        {
            var trail = new List<KeyValuePair<string, Action>>();
            for (int i = 0; i + 1 < steps.Length; i += 2) trail.Add(new KeyValuePair<string, Action>((string)steps[i], steps[i + 1] as Action));
            sideHead.SetCrumbs(trail);
            BindCrumbFavorites(trail);
        }

        void BindCrumbFavorites(List<KeyValuePair<string, Action>> trail)
        {
            // The title bar holds the one favorites button for every secondary menu. Its click asks
            // where to save: here, the top of the page, or the closest category header above it.
            sideHead.FavoriteClicked = null;
            favTrail = null;
            if (trail == null || trail.Count == 0 || current == "Community") return;
            favTrail = trail;
            sideHead.FavoriteClicked = delegate { ChooseFavoriteLocation(); };
            sideHead.Invalidate();
        }

        GuideFavoriteRoute CrumbFavoriteRoute(string label, int step)
        {
            if (current == "Community" || label == "Community") return null;
            if (current == "Guide")
            {
                for (int i = 0; i < 3; i++)
                    if (GroupName(i) == label) return new GuideFavoriteRoute("group", label, "");
                if (label == TalentBuilder) return new GuideFavoriteRoute("builder", "", "");
                if (label == GearPlanner) return new GuideFavoriteRoute("planner", "", "");
                return new GuideFavoriteRoute("section", "Guide", "");
            }
            if (current == "Options")
            {
                if (label == "Launcher") return new GuideFavoriteRoute("option", "", "tab", "4", "");
                if (label == "About Launcher") return new GuideFavoriteRoute("option", "", "about");
                if (label == "Language") return new GuideFavoriteRoute("option", "", "language");
                for (int i = 0; i < 5; i++)
                    if (OptionTabName(i) == label) return new GuideFavoriteRoute("option", "", "tab", i.ToString(CultureInfo.InvariantCulture), "");
            }
            if (current == "Addons")
            {
                if (label == "Download More") return new GuideFavoriteRoute("addon", "", "download", addonCategory.ToString(CultureInfo.InvariantCulture), "");
                if (addonMode == "download")
                    for (int i = 0; i < Data.AddonCategories.Length; i++)
                        if (Data.AddonCategories[i] == label)
                            return new GuideFavoriteRoute("addon", "", "category", i.ToString(CultureInfo.InvariantCulture), "");
            }
            if (current == "Stats")
            {
                if (statsClass != null && step == 1)
                    return new GuideFavoriteRoute("stats", "", "class", statsClass, "");
                for (int i = 0; i < StatTabs.Length; i++)
                    if (StatTabs[i] == label)
                        return new GuideFavoriteRoute("stats", "", "tab", statsClass ?? "", i.ToString(CultureInfo.InvariantCulture));
            }
            if (current == "Setup")
            {
                for (int i = 0; i < SetupSteps.Length; i++)
                    if (SetupSteps[i] == label)
                        return new GuideFavoriteRoute("setup", "", "step", i.ToString(CultureInfo.InvariantCulture), "");
            }
            if (label == "Cloud Save") return new GuideFavoriteRoute("section", "Cloud Save", "");
            return new GuideFavoriteRoute("section", current, "");
        }

        // a major button (the big section buttons, the icon strip): always the section's main page, even when that
        // section is already open on one of its other pages (Options on Cloud Save goes back to Options › Folders)
        public void OpenMain(string name)
        {
            if (setupOpen) { ShowSection(name); return; }
            if (name == "Cloud Save")                         // its own page: Options › Cloud Save
            {
                if (current == "Options" && side.Visible) { optionsTab = 1; optionsLang = false; BuildPanel("Options"); }
                else ShowSection(name);
                return;
            }
            if (name == "Guide") { ShowSection("Guide"); return; }   // the Guide's button already goes to its main page
            bool open = name == current && side.Visible;
            if (name == "Options") { optionsTab = 0; optionsLang = false; }
            if (name == "Stats") { statsTab = 0; statsClass = null; }
            if (name == "Addons") { addonMode = "installed"; addonCategory = 0; askUninstallName = askUninstallFolder = null; rollbackOpen = false; }
            if (name == "About Launcher") aboutLog = false;
            if (name == "Community") communityMore = false;
            if (!open) { ShowSection(name); return; }
            if (name == "Community") LeaveCommunityVideo();
            if (name == "Changelog") clView = ClList;
            BuildPanel(name);
        }

        // a section's first page, from the trail in the title bar
        void SectionRoot(string name)
        {
            if (name == "Guide") { GuideMainPage(); return; }
            if (name == "Addons") { addonMode = "installed"; addonCategory = 0; askUninstallName = askUninstallFolder = null; rollbackOpen = false; }
            if (name == "Options") { optionsTab = 0; optionsLang = false; }
            if (name == "Stats") { statsTab = 0; statsClass = null; }
            if (name == "About Launcher") aboutLog = false;
            if (name == "Changelog") clView = ClList;
            if (name == "Community") { LeaveCommunityVideo(); communityMore = false; }
            BuildPanel(name);
        }

        // the back arrow: one step back inside the section, or out of the section when at its first page
        void GoBack()
        {
            if (current == "Setup") { SetupBack(); return; }
            if (LeaveCommunityVideo()) return;
            if (current == "Guide" && gVideo != null) { gVideo = null; BuildPanel("Guide"); return; }   // a video: back to its page
            if (current == "Guide" && BackWithinClassArticle()) return; // return to the prior scroll position after an in-page jump
            if (BackInHistory()) return;                     // restore the previous complete view, including addon context
            if (current == "Addons" && addonMode == "download") { SetAddonMode("installed"); return; }
            if (current == "Options" && optionsLang) { optionsLang = false; BuildPanel("Options"); return; }
            if (current == "Changelog")
            {
                if (clView >= 0) { SetChangelogView(ClList); return; }
                if (clView == ClNewest) { SetChangelogView(ClList); return; }   // the list is the Changelog's first page
            }
            if (current == "Guide")
            {
                if (gVideo != null) { gVideo = null; BuildPanel("Guide"); return; }
                if (gTalents != null) { gTalents = null; BuildPanel("Guide"); return; }   // back to the builder's classes or the class page
                if (gPage >= 0) { SetGuideView(gTopic, -1); return; }
                if (!guideMain && guideQuery.Length == 0)          // a topic or the builder: back to the main page
                {
                    gTopic = -1; gTalents = null; gBuilder = false; gPlanner = false; guideMain = true;
                    BuildPanel("Guide");
                    return;
                }
                if (guideQuery.Length > 0) { guideQuery = ""; SetGuideView(guideLastTopic, -1); return; }
                if (guideGroupPage >= 0) { GuideMainPage(); return; }   // a group's page: back to every group
            }
            CloseSide();
        }

        Control putInto;                                     // where Put / PutText add (the section, or a scrolling page in it)

        T Put<T>(T c, int x, int y) where T : Control
        {
            c.Location = new Point(x, y);
            (putInto ?? panel).Controls.Add(c);
            c.BringToFront();
            return c;
        }

        Label PutText(string text, float size, FontStyle style, Color fg, int x, int y, int wrap, bool translate = true)
        {
            var l = UI.Text(text, size, style, fg, Theme.Panel, wrap, translate);
            l.Size = VStack.Measure(l);
            return Put(l, x, y);
        }

        // A scrollable area without Windows' own (white) scroll bar: a slim painted one in `thumb` colors runs in a
        // gutter on its right.
        ScrollPanel MakeThinScroll(Control parent, Rectangle r, Color bg, Color border, Color thumb, Color thumbHot)
        {
            var frame = new Bordered(border, 1);
            int gutterWidth=Theme.Px(10);
            frame.Padding = new Padding(1, 1, gutterWidth, 1);        // a gutter on the right for the slim bar
            frame.Bounds = r;
            parent.Controls.Add(frame);
            frame.BringToFront();
            var gutter = new Panel();
            gutter.BackColor = bg;
            gutter.Bounds = new Rectangle(r.Width - gutterWidth, 1, gutterWidth-1, r.Height - 2);
            frame.Controls.Add(gutter);
            var sp = new ScrollPanel();
            sp.HideBars = true;
            sp.BackColor = bg;
            sp.Dock = DockStyle.Fill;
            frame.Controls.Add(sp);
            var bar = new ThinScrollBar(sp, Theme.Mix(bg, border, 0.3), thumb, thumbHot);
            bar.Bounds = new Rectangle(1, 3, Math.Max(4,gutterWidth-4), r.Height - 2 - 6);
            gutter.Controls.Add(bar);
            return sp;
        }

        // a scrollable area with a 1 px border; returns the scrolling panel
        ScrollPanel MakeScroll(Control parent, Rectangle r, Color bg, Color border)
        {   // every list and page uses the slim gold scroll bar, like the Guide and Stats
            return MakeThinScroll(parent, r, bg, Theme.CardEdge, Theme.GoldDim, Theme.Gold);
        }

        static int BottomOf(Control c) { return c.Top + c.Height; }

        // ------------------------------------------------------------------ Changelog
        // ------------------------------------------------------------------ Guide: see Guide.cs

        void PanelChangelog()
        {
            var entries = Data.Changelog;
            if (clView >= entries.Count) clView = ClList;
            string title, btn;
            int back;
            if (clView == ClNewest) { title = "Changelog"; btn = "ALL PATCH NOTES ›"; back = ClList; }
            else if (clView == ClList) { title = "All Patch Notes"; btn = "‹ LATEST"; back = ClNewest; }
            else { title = Data.PostTitle(entries[clView]); btn = "‹ ALL PATCH NOTES"; back = ClList; }
            ClearPanel(title);
            Put(new WowButton(btn, delegate { SetChangelogView(back); }, 190, 32, 10), PW - 20 - (int)(190 * Theme.Zoom), 8);
            int top = 8 + (int)(32 * Theme.Zoom) + 6, bottom = PH;
            if (clView >= 0 || clView == ClNewest)           // PREVIOUS (older) / NEXT (newer), always at the bottom
            {
                int v = clView == ClNewest ? 0 : clView;
                bottom = NavRow(entries.Count - v, entries.Count, v + 1 < entries.Count ? (Action)delegate { SetChangelogView(v + 1); } : null,
                                v > 0 ? (Action)delegate { SetChangelogView(v - 1); } : null);
            }
            if (clView == ClList)
            {
                PatchNotesList(entries, top);
                return;
            }
            var sp = MakeScroll(panel, new Rectangle(20, top, PW - 40, bottom - 14 - top), Theme.Panel, Theme.Bronze);
            sp.Overscroll = ChangelogStep;
            ChangelogBox(sp, entries[clView == ClNewest ? 0 : clView]);
            if (clToEnd)                                     // arrived by scrolling up: start at the bottom
            {
                clToEnd = false;
                sp.ScrollToEnd();
            }
        }

        void ChangelogBox(ScrollPanel sp, string text)
        {
            Color boxbg = Theme.Ink;
            string[] lines = text.Trim('\n').Split('\n');
            int w = sp.ClientSize.Width - 8;
            var box = new Bordered(Theme.CardEdge, 1);
            var inner = new Panel();
            inner.BackColor = boxbg;
            inner.Dock = DockStyle.Fill;
            box.Controls.Add(inner);
            int y = 10;
            var t = UI.Text(lines[0], 16, FontStyle.Bold, Theme.Gold, boxbg, w - 32, false);
            t.Size = VStack.Measure(t);
            t.Location = new Point(14, y);
            inner.Controls.Add(t);
            y += t.Height;
            int start = 1;
            if (lines.Length > 1 && System.Text.RegularExpressions.Regex.IsMatch(lines[1].Trim(), "^-{3,}$"))
            {
                var dash = UI.Text(lines[1].Trim(), 11, FontStyle.Regular, Theme.GoldDim, boxbg, w - 32, false);
                dash.Size = VStack.Measure(dash);
                dash.Location = new Point(14, y);
                inner.Controls.Add(dash);
                y += dash.Height;
                start = 2;
            }
            string body = string.Join("\n", lines, start, lines.Length - start).Trim('\n');
            if (body.Length > 0)
            {
                var b = UI.Text(body, 10, FontStyle.Regular, Theme.Text, boxbg, w - 32, false);
                b.Size = VStack.Measure(b);
                b.Location = new Point(14, y + 4);
                inner.Controls.Add(b);
                y += 4 + b.Height + 12;
            }
            else y += 10;
            box.Bounds = new Rectangle(4, 4, w, y + 4);
            sp.Controls.Add(box);
            var pad = new Panel();                           // a little room under the box
            pad.Bounds = new Rectangle(4, box.Bottom, 1, 8);
            pad.BackColor = sp.BackColor;
            sp.Controls.Add(pad);
        }

        // ‹ PREVIOUS · "3 of 40" · NEXT › pinned to the bottom of the section, whatever its size; returns its top
        int NavRow(int at, int count, Action previous, Action next)
        {
            int bh = (int)(34 * Theme.Zoom), by = PH - 10 - bh, bw = (int)(150 * Theme.Zoom);
            if (previous != null) Put(new WowButton("‹ PREVIOUS", previous, 150, 34, 10), 20, by);
            if (next != null) Put(new WowButton("NEXT ›", next, 150, 34, 10), PW - 20 - bw, by);
            var lbl = UI.Text(at + " of " + count, 10, FontStyle.Italic, Theme.GoldDim, Theme.Panel, 0);
            Size ls = VStack.Measure(lbl);
            lbl.Size = ls;
            Put(lbl, (PW - ls.Width) / 2, by + (bh - ls.Height) / 2);
            return by;
        }

        int clRows = Data.ClRows;                            // buttons per column that fit the section (set by the list)

        void PatchNotesList(List<string> entries, int top)
        {
            int rowH = (int)(48 * Theme.Zoom), bh = (int)(34 * Theme.Zoom);
            int areaBottom = PH - 10 - bh - 10;               // above the PREVIOUS / NEXT row
            clRows = Math.Max(1, (areaBottom - top - 4) / rowH);
            int per = clRows * 2;
            int pages = Math.Max(1, (entries.Count + per - 1) / per);
            int page = Math.Min(Math.Max(clPage, 0), pages - 1);
            clPage = page;
            NavRow(page + 1, pages, page > 0 ? (Action)delegate { SetClPage(page - 1); } : null,
                   page + 1 < pages ? (Action)delegate { SetClPage(page + 1); } : null);
            var area = new WheelPanel();
            area.BackColor = Theme.Panel;
            area.Bounds = new Rectangle(20, top, PW - 40, areaBottom - top);
            area.OnWheel = delegate(int delta) { ClPageWheel(delta, pages); };
            panel.Controls.Add(area);
            area.BringToFront();
            if (entries.Count == 0)
            {
                var l = UI.Text("No patch notes yet.", 11, FontStyle.Italic, Theme.GoldDim, Theme.Panel, 0);
                l.Location = new Point(6, 8);
                area.Controls.Add(l);
            }
            int colW = area.Width / 2;
            for (int k = 0, i = page * per; i < Math.Min(entries.Count, (page + 1) * per); k++, i++)
            {
                int col = k / clRows, rowN = k % clRows;     // fill the left column first, then the right
                int n = i;
                string version = Data.PostTitle(entries[i]) + (i == 0 ? " (current)" : "");
                var b = new WowButton(version, delegate { SetChangelogView(n); }, 230, 40, 14);
                b.Location = new Point(col * colW + 4, rowN * rowH + 4);
                area.Controls.Add(b);
            }
        }

        // wheel past the bottom (+1) opens the next older post, past the top (-1) the next newer one
        void ChangelogStep(int direction)
        {
            DateTime now = DateTime.Now;
            if ((now - clLastStep).TotalSeconds < 0.45) return;      // one post per flick of the wheel
            int cur = clView == ClNewest ? 0 : clView;
            int nw = cur + direction;
            if (nw < 0 || nw >= Data.Changelog.Count) return;
            clLastStep = now;
            clToEnd = direction < 0;
            SetChangelogView(nw == 0 ? ClNewest : nw);
        }

        void SetChangelogView(int view)
        {
            clView = view;
            if (view >= 0) clPage = view / (clRows * 2);         // coming back to the list shows this post's page
            BuildPanel("Changelog");
        }

        void SetClPage(int n)
        {
            clPage = n;
            BuildPanel("Changelog");
        }

        void ClPageWheel(int delta, int pages)
        {
            DateTime now = DateTime.Now;
            if ((now - clLastStep).TotalSeconds < 0.45) return;
            int nw = clPage + (delta < 0 ? 1 : -1);
            if (nw >= 0 && nw < pages)
            {
                clLastStep = now;
                SetClPage(nw);
            }
        }

        // ------------------------------------------------------------------ About Launcher
        // About Launcher: ABOUT shows the description, LAUNCHER CHANGELOG the launcher's own changelog
        bool aboutLog;
        // The launcher's own changelog is kept up to date in assets\launcher-changelog.txt but not shipped for now
        // (build.bat leaves it out). Set to true, and put its resource back in build.bat, to show it again.
        const bool ShowLauncherChangelog = false;

        void PanelStatusLog()
        {
            ClearPanel("Status Log");
            var box = UI.Log(Theme.Panel, 11);
            box.Bounds = new Rectangle(20, 8, PW - 34, PH - 26);
            Put(box, box.Left, box.Top);
            List<KeyValuePair<DateTime, string>> entries;
            lock (statusLog) entries = new List<KeyValuePair<DateTime, string>>(statusLog);
            if (entries.Count == 0) { UI.Append(box, Lang.T("No status messages yet.") + "\n", Theme.Text, Theme.F(11)); return; }
            foreach (var entry in entries)
                UI.Append(box, entry.Key.ToString("HH:mm:ss", CultureInfo.InvariantCulture) + "   " + entry.Value + "\n", Theme.Text, Theme.F(11));
            box.SelectionStart = box.TextLength;
            box.ScrollToCaret();
        }

        void PanelAbout()
        {
            ClearPanel("About Launcher");
            if (ShowLauncherChangelog)
            {
                var about = Put(new WowButton("ABOUT", delegate { aboutLog = false; BuildPanel("About Launcher"); }, 130, 32, 10), 20, 8);
                about.Selected = !aboutLog;
                var log = Put(new WowButton("LAUNCHER CHANGELOG", delegate { aboutLog = true; BuildPanel("About Launcher"); }, 230, 32, 10),
                              20 + (int)(140 * Theme.Zoom), 8);
                log.Selected = aboutLog;
            }
            else aboutLog = false;
            var credits = UI.Text("Made by Slimjim\nASBuilder by Cadoky\nAzeroth Stats by Maidenhead\nCloud-save foundation by Keijin", 13, FontStyle.Italic,
                                  Theme.Credit, Theme.Panel, 0);
            credits.TextAlign = ContentAlignment.TopRight;
            credits.Size = VStack.Measure(credits);
            Put(credits, PW - 20 - credits.Width, ShowLauncherChangelog ? 8 + (int)(32 * Theme.Zoom) + 6 : 8);
            var forgeLinks = new LinkLabel();
            forgeLinks.Text = "Addon pictures by SurvivorsForge  ·  website  ·  repository";
            forgeLinks.Font = Theme.F(9, FontStyle.Regular);
            forgeLinks.ForeColor = Theme.Credit;
            forgeLinks.LinkColor = Theme.Gold;
            forgeLinks.ActiveLinkColor = Theme.Gold;
            forgeLinks.VisitedLinkColor = Theme.GoldDim;
            forgeLinks.LinkBehavior = LinkBehavior.HoverUnderline;
            forgeLinks.BackColor = Theme.Panel;
            forgeLinks.AutoSize = true;
            int websiteStart = forgeLinks.Text.IndexOf("SurvivorsForge", StringComparison.Ordinal);
            forgeLinks.Links.Add(websiteStart, "SurvivorsForge".Length, "https://forge.rezondes.net/");
            int siteStart = forgeLinks.Text.LastIndexOf("website", StringComparison.Ordinal);
            forgeLinks.Links.Add(siteStart, "website".Length, "https://forge.rezondes.net/");
            int repoStart = forgeLinks.Text.IndexOf("repository", StringComparison.Ordinal);
            forgeLinks.Links.Add(repoStart, "repository".Length, "https://github.com/Rezondes/SurvivorsForge");
            forgeLinks.LinkClicked += delegate(object sender, LinkLabelLinkClickedEventArgs e)
            {
                string target = e.Link.LinkData as string;
                if (!string.IsNullOrEmpty(target)) Util.OpenInShell(target);
            };
            forgeLinks.Size = TextRenderer.MeasureText(forgeLinks.Text, forgeLinks.Font, new Size(PW - 40, 200),
                TextFormatFlags.SingleLine | TextFormatFlags.NoPadding);
            Put(forgeLinks, PW - 20 - forgeLinks.Width, BottomOf(credits) + 1);
            var line = Put(UI.Line(Theme.GoldDim, PW - 40, 2), 20, BottomOf(forgeLinks) + 4);
            var box = UI.Log(Theme.Panel, 11);
            box.Bounds = new Rectangle(20, BottomOf(line) + 10, PW - 34, PH - BottomOf(line) - 18);
            Put(box, box.Left, box.Top);
            // "# heading", "- bullet", anything else a paragraph (the texts are not translated)
            foreach (string raw in (aboutLog ? Data.LauncherChangelog : Data.About).Split('\n'))
            {
                string ln = raw.TrimEnd();
                box.SelectionStart = box.TextLength;
                if (ln.StartsWith("#"))
                {
                    box.SelectionIndent = 0;
                    box.SelectionHangingIndent = 0;
                    UI.Append(box, (box.TextLength > 0 ? "\n" : "") + ln.TrimStart('#', ' ').Trim() + "\n", Theme.Gold, Theme.F(14, FontStyle.Bold));
                }
                else if (ln.TrimStart().StartsWith("-") || ln.TrimStart().StartsWith("*"))
                {
                    box.SelectionIndent = 14;
                    box.SelectionHangingIndent = 14;
                    UI.Append(box, "• " + ln.TrimStart('-', '*', ' ').Trim() + "\n", Theme.Text, Theme.F(11));
                }
                else if (ln.Trim().Length > 0)
                {
                    box.SelectionIndent = 0;
                    box.SelectionHangingIndent = 0;
                    UI.Append(box, ln.Trim() + "\n\n", Theme.Text, Theme.F(11));
                }
            }
            box.SelectionStart = 0;
            box.ScrollToCaret();
        }

        // ------------------------------------------------------------------ importing a save (setup and Options)
        // asks for a .sav file and puts it in as the game's progress; returns its name, or null when nothing was done
        public string ImportSave(IWin32Window owner, string saveDir)
        {
            if (GameRunningFlag || Util.GameRunning()) { Msg.Show(owner, "Close the game first, then import the save.", "Import save"); return null; }
            string file;
            using (var d = new OpenFileDialog())
            {
                d.Title = Lang.T("Choose the save you exported from the website (.sav)");
                d.Filter = Lang.T("Azeroth Survivors save (*.sav)|*.sav|All files (*.*)|*.*");
                string dl = Path.Combine(Util.Home, "Downloads");
                if (Directory.Exists(dl)) d.InitialDirectory = dl;
                if (d.ShowDialog(owner) != DialogResult.OK) return null;
                file = d.FileName;
            }
            if (string.IsNullOrEmpty(saveDir)) saveDir = Util.DefaultSaveDir;
            try { SaveImport.Read(file); }
            catch (Exception e) { Msg.Show(owner, e.Message, "Import save", MessageBoxButtons.OK, MessageBoxIcon.Error); return null; }
            if (File.Exists(Path.Combine(saveDir, SaveImport.SaveFile)))
            {
                Form previousHost = WowDialog.Host;
                try
                {
                    if (side != null && side.Visible) WowDialog.Host = side;
                    if (Msg.Show(owner, "This PC already has progress in the game.\n\nReplace it with " + Path.GetFileName(file) +
                                 "?\nWhat is there now is kept as a backup in the game's Saves folder.", "Import save",
                                 MessageBoxButtons.YesNo) != DialogResult.Yes) return null;
                }
                finally { WowDialog.Host = previousHost; }
            }
            try { SaveImport.Import(file, saveDir); }
            catch (Exception e)
            {
                Msg.Show(owner, "The save could not be imported:\n\n" + e.Message, "Import save", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return null;
            }
            Cfg.Set("save_imported", DateTime.UtcNow.ToString("o", System.Globalization.CultureInfo.InvariantCulture));
            Cfg.Save();
            return Path.GetFileName(file);
        }

        // Copies the game's save to a portable .sav file for another client or the browser game's import page.
        // The source save is validated first and is never changed by an export.
        public void ExportSave(IWin32Window owner, string saveDir)
        {
            if (GameRunningFlag || Util.GameRunning()) { Msg.Show(owner, "Close the game first, then export the save.", "Export save"); return; }
            if (string.IsNullOrEmpty(saveDir)) saveDir = Util.DefaultSaveDir;
            string source = Path.Combine(saveDir, SaveImport.SaveFile);
            if (!File.Exists(source)) { Msg.Show(owner, "The game save was not found.", "Export save"); return; }
            try { SaveImport.Read(source); }
            catch (Exception e) { Msg.Show(owner, e.Message, "Export save", MessageBoxButtons.OK, MessageBoxIcon.Error); return; }
            using (var d = new SaveFileDialog())
            {
                d.Title = Lang.T("Export a save");
                d.Filter = Lang.T("Azeroth Survivors save (*.sav)|*.sav|All files (*.*)|*.*");
                d.DefaultExt = "sav";
                d.AddExtension = true;
                d.OverwritePrompt = true;
                d.FileName = "Azeroth Survivors Save.sav";
                string dl = Path.Combine(Util.Home, "Downloads");
                d.InitialDirectory = Directory.Exists(dl) ? dl : saveDir;
                if (d.ShowDialog(owner) != DialogResult.OK) return;
                try
                {
                    File.Copy(source, d.FileName, true);
                    SetStatus(Lang.T("Save exported."));
                }
                catch (Exception e) { Msg.Show(owner, Lang.T("The save could not be exported.") + "\n\n" + e.Message, "Export save", MessageBoxButtons.OK, MessageBoxIcon.Error); }
            }
        }

        // ------------------------------------------------------------------ Addons
        string AddonsDir() { return SyncSettings().AddonsDir; }

        void PanelAddons()
        {
            bool dl = addonMode == "download";
            ClearPanel(dl ? "Download More" : "Addons");
            if (dl)
            {
                // no row of tabs: the category is the last step of the path (Addons › Download More › UI ▾), its list
                // the other categories
                var l = new List<KeyValuePair<string, Action>>();
                for (int i = 0; i < Data.AddonCategories.Length; i++)
                {
                    int n = i;
                    l.Add(new KeyValuePair<string, Action>(Data.AddonCategories[i], delegate { addonCategory = n; BuildPanel("Addons"); }));
                }
                if (addonCategory < 0 || addonCategory >= Data.AddonCategories.Length) addonCategory = 0;
                SetCrumbs("Addons", (Action)delegate { SetAddonMode("installed"); }, "Download More", (Action)delegate { BuildPanel("Addons"); },
                          Data.AddonCategories[addonCategory], null);
                SetCrumbMenu(2, l);
                AddonsDownloadView(8);
            }
            else if (addonMode == "updates") AddonUpdatesView();
            else AddonsInstalledView(8);
        }

        void SetAddonMode(string mode)
        {
            addonMode = mode;
            askUninstallName = askUninstallFolder = null;
            rollbackOpen = false;
            BuildPanel("Addons");
        }

        Dictionary<string, object> AddonInstalls() { return Cfg.Section("addon_installs"); }

        string LegacyAddonName(string name)
        {
            foreach (AddonEntry e in Data.Addons)
            {
                if (!string.Equals(e.Name, name, StringComparison.Ordinal)) continue;
                if (e.Folders.Contains("AZSSpellMeter")) return "Damage Meter By Kexo";
                if (e.Folders.Contains("MoveUI")) return "Move UI By Jamie";
            }
            return null;
        }

        object AddonSetting(Dictionary<string, object> section, string name)
        {
            object value;
            if (section != null && section.TryGetValue(name, out value)) return value;
            string legacy = LegacyAddonName(name);
            return !string.IsNullOrEmpty(legacy) && section != null && section.TryGetValue(legacy, out value) ? value : null;
        }

        void ClearLegacyAddonSettings(string name)
        {
            string legacy = LegacyAddonName(name);
            if (string.IsNullOrEmpty(legacy)) return;
            AddonInstalls().Remove(legacy);
            AddonInstalledAt().Remove(legacy);
            AddonInstalledRelease().Remove(legacy);
            AddonPins().Remove(legacy);
        }

        List<string> RecordedFolders(string name)
        {
            var l = Json.AsList(AddonSetting(AddonInstalls(), name));
            if (l == null) return null;
            var r = new List<string>();
            foreach (object o in l) r.Add(Convert.ToString(o));
            return r;
        }

        List<string> AddonFoldersOf(string name)
        {
            foreach (var e in Data.Addons)
                if (e.Name == name && BuiltinAddons.Find(e.Url) != null)
                {
                    string at = BuiltinAddons.PathOf(AddonsDir(), BuiltinAddons.Find(e.Url));
                    return at == null ? new List<string>() : new List<string> { Path.GetFileName(at) };
                }
            List<string> recorded = RecordedFolders(name);
            if (recorded == null)
                foreach (AddonEntry e in Data.Addons)
                    if (string.Equals(e.Name, name, StringComparison.Ordinal)) { recorded = e.Folders; break; }
            return Addons.FoldersOf(AddonsDir(), name, recorded);
        }

        // ---- addon updates: standard mirror assets use upload dates; direct GitHub entries track upstream tag and digest.
        Dictionary<string, object> AddonInstalledAt() { return Cfg.Section("addon_installed_at"); }
        Dictionary<string, object> AddonInstalledRelease() { return Cfg.Section("addon_installed_release"); }
        Dictionary<string, object> AddonPins() { return Cfg.Section("addon_pins"); }   // addon name -> the kept copy rolled back to

        // ---- ROLLBACK (Addons › Advanced): an older kept copy replaces the addon's folders. Its saved settings are not in
        // those folders, so they stay. The addon keeps its on/off switch, and is pinned: [Outdated], no automatic update.
        bool rollbackOpen;

        void RollbackAddon(string name, string zip)
        {
            if (AddonsLocked() || addonDlBusy) return;
            addonDlBusy = true;
            string dir = AddonsDir();
            var olds = AddonFoldersOf(name);
            SetStatus("Installing " + name + " (" + AddonArchive.Label(zip) + ")...");
            Bg(delegate
            {
                string err = null;
                List<string> names = null;
                try
                {
                    bool off = false;
                    foreach (string f in olds)
                    {
                        string bare = f.EndsWith(Addons.DisabledSuffix) ? f.Substring(0, f.Length - Addons.DisabledSuffix.Length) : f;
                        foreach (string p in new[] { Path.Combine(dir, bare), Path.Combine(dir, bare + Addons.DisabledSuffix) })
                            if (Directory.Exists(p)) { if (p.EndsWith(Addons.DisabledSuffix)) off = true; Directory.Delete(p, true); }
                    }
                    names = Archive.InstallAddonZip(zip, dir);
                    if (off)                                   // it was switched off: it stays off
                        foreach (string n in names)
                        {
                            string on = Path.Combine(dir, n), to = on + Addons.DisabledSuffix;
                            if (Directory.Exists(on) && !Directory.Exists(to)) Directory.Move(on, to);
                        }
                }
                catch (Exception e) { err = e.Message; }
                Ui(delegate
                {
                    addonDlBusy = false;
                    if (err != null) { Msg.Show(this, "Could not install " + name + ":\n\n" + err, "Addons", MessageBoxButtons.OK, MessageBoxIcon.Error); return; }
                    var rec = new List<object>();
                    foreach (string n in names) rec.Add(n);
                    AddonInstalls()[name] = rec;
                    AddonPins()[name] = Path.GetFileName(zip);
                    Cfg.Save();
                    SetStatus(name + " installed (" + AddonArchive.Label(zip) + ").");
                    if (current == "Addons") BuildPanel("Addons");
                });
            });
        }

        // NEWEST: the pin goes and the addon is updated to the copy on GitHub
        void UnpinAddon(string name)
        {
            AddonPins().Remove(name);
            ClearLegacyAddonSettings(name);
            Cfg.Save();
            foreach (var e in Data.Addons) if (e.Name == name) { DownloadAddon(e.Name, e.Url); return; }
            BuildPanel("Addons");
        }

        // the ROLLBACK column: a button per addon with kept copies; its list holds each copy (newest first) and NEWEST
        void AddonsRollbackPanel(int cx, int top)
        {
            addonCount = null;
            int w = (int)(190 * Theme.Zoom);
            var head = UI.Text("Rollback", 11, FontStyle.Bold, Theme.Gold, Theme.Panel, w);
            head.Size = VStack.Measure(head);
            Put(head, cx, top);
            var body = UI.Text("The launcher keeps the addons it downloads. Pick an older copy to install it in place of the newest; your addon settings stay as they are. Until you pick Newest, it is marked [Outdated] and not updated.",
                               9, FontStyle.Regular, Theme.Text, Theme.Panel, w);
            body.Size = VStack.Measure(body);
            Put(body, cx, BottomOf(head) + 6);
            int y = BottomOf(body) + 10;
            int any = 0;
            foreach (var e in Data.Addons)
            {
                if (BuiltinAddons.Find(e.Url) != null || AddonFoldersOf(e.Name).Count == 0) continue;
                var vers = AddonArchive.Versions(e.Name);
                string legacyName = LegacyAddonName(e.Name);
                if (vers.Count == 0 && !string.IsNullOrEmpty(legacyName)) vers = AddonArchive.Versions(legacyName);
                if (vers.Count == 0) continue;
                any++;
                string name = e.Name;
                string pin = Convert.ToString(AddonSetting(AddonPins(), name), System.Globalization.CultureInfo.InvariantCulture);
                WowButton b = null;
                b = new WowButton(name + (pin.Length > 0 ? " *" : "") + " ▾", delegate
                {
                    var menu = ThemeMenus.Create(true);
                    var newest = new ToolStripMenuItem(Lang.T("Newest (updates on)"), null, delegate { UnpinAddon(name); });
                    newest.Checked = pin.Length == 0;
                    menu.Items.Add(newest);
                    menu.Items.Add(new ToolStripSeparator());
                    foreach (string z in vers)
                    {
                        string zz = z;
                        var it = new ToolStripMenuItem(AddonArchive.Label(z), null, delegate { RollbackAddon(name, zz); });
                        it.Checked = pin == Path.GetFileName(z);
                        menu.Items.Add(it);
                    }
                    menu.Show(b, new Point(0, b.Height));
                }, 190, 26, 8);
                Put(b, cx, y);
                y = BottomOf(b) + 4;
            }
            if (any == 0)
            {
                var none = UI.Text("No kept copies yet: they are kept from now on, each time an addon is installed or updated.", 9, FontStyle.Italic, Theme.GoldDim, Theme.Panel, w);
                none.Size = VStack.Measure(none);
                Put(none, cx, y);
                y = BottomOf(none) + 8;
            }
            Put(new WowButton("DONE", delegate { rollbackOpen = false; BuildPanel("Addons"); }, 190, 28, 9), cx, y + 6);
        }
        static Dictionary<string, DateTime> mirrorDates;     // zip file name -> uploaded (UTC); null: not asked yet
        static bool mirrorAsking;
        const string AzerothStatsLatestUrl = "https://github.com/Maidenquinn/AzerothStats/releases/latest";
        const string SurvivorsDragonUiLatestUrl = "https://github.com/migwynkriid13/SurvivorsDragonUi/releases/latest";
        const string KeijinUiLatestUrl = "https://github.com/KeijinDE/KeijinUI_modern-as-ui/releases/latest";
        const string AzerothFieldKitLatestUrl = "https://github.com/Maidenquinn/azeroth-fieldkit/releases/latest";
        const string AstinyPlatesLatestUrl = "https://github.com/Valorless/ASTinyPlates/releases/latest";
        const string AsBuilderLatestUrl = "https://github.com/cadoky/ASBuilder/releases/latest";
        const string AzerothHubLatestUrl = "https://github.com/Maidenquinn/AzerothHub/releases/latest";
        static readonly Dictionary<string, GitHubAddonRelease> dynamicAddonReleases = new Dictionary<string, GitHubAddonRelease>(StringComparer.OrdinalIgnoreCase);
        static readonly HashSet<string> dynamicAddonReleaseChecked = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        sealed class GitHubAddonRelease
        {
            public string Tag, Url, Sha256;   // the most likely file of the release, and its digest ("" when GitHub has none)
            public DateTime PublishedAt;
            // the release's other files, most likely first, ending with GitHub's source archive of the tag: each is tried
            // in turn until one holds the addon. Value: the digest, or Archive.NoDigest.
            public List<KeyValuePair<string, string>> Alternatives = new List<KeyValuePair<string, string>>();
            public string Identity { get { return (Tag ?? "") + "|" + (Sha256 ?? ""); } }
            public string Digest { get { return IsSha256(Sha256) ? Sha256 : Archive.NoDigest; } }
        }

        static bool IsDynamicGitHubAddonUrl(string url)
        {
            string normalized = (url ?? "").TrimEnd('/');
            string anyRepo;
            if (TryGitHubLatestRepo(normalized, out anyRepo)) return true;
            return string.Equals(normalized, AzerothStatsLatestUrl, StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(normalized, SurvivorsDragonUiLatestUrl, StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(normalized, KeijinUiLatestUrl, StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(normalized, AzerothFieldKitLatestUrl, StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(normalized, AstinyPlatesLatestUrl, StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(normalized, AsBuilderLatestUrl, StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(normalized, AzerothHubLatestUrl, StringComparison.OrdinalIgnoreCase);
        }

        sealed class GitHubAddonSource
        {
            public string Url, Repo, AssetPrefix;
            public bool FixedAssetName;
            public bool Generic;   // any repo's releases/latest from the catalog: the ZIP is chosen by rule
        }

        // https://github.com/<owner>/<repo>/releases/latest -> "owner/repo"
        static bool TryGitHubLatestRepo(string url, out string repo)
        {
            repo = null;
            string u = (url ?? "").TrimEnd('/');
            const string prefix = "https://github.com/";
            const string suffix = "/releases/latest";
            if (!u.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) || !u.EndsWith(suffix, StringComparison.OrdinalIgnoreCase)) return false;
            string middle = u.Substring(prefix.Length, u.Length - prefix.Length - suffix.Length);
            string[] parts = middle.Split('/');
            if (parts.Length != 2 || parts[0].Length == 0 || parts[1].Length == 0) return false;
            repo = parts[0] + "/" + parts[1];
            return true;
        }

        // asset=<start of the ZIP name> from the catalog line for this URL, or null
        static string AssetPrefixFor(string url)
        {
            string normalized = (url ?? "").TrimEnd('/');
            foreach (AddonEntry entry in Data.Addons)
                if (string.Equals((entry.Url ?? "").TrimEnd('/'), normalized, StringComparison.OrdinalIgnoreCase))
                    return string.IsNullOrEmpty(entry.AssetPrefix) ? null : entry.AssetPrefix;
            return null;
        }

        static GitHubAddonSource GitHubAddonSourceFor(string url)
        {
            string normalized = (url ?? "").TrimEnd('/');
            if (string.Equals(normalized, AzerothStatsLatestUrl, StringComparison.OrdinalIgnoreCase))
                return new GitHubAddonSource { Url = AzerothStatsLatestUrl, Repo = "Maidenquinn/AzerothStats", AssetPrefix = "AzerothStats-" };
            if (string.Equals(normalized, SurvivorsDragonUiLatestUrl, StringComparison.OrdinalIgnoreCase))
                return new GitHubAddonSource { Url = SurvivorsDragonUiLatestUrl, Repo = "migwynkriid13/SurvivorsDragonUi", AssetPrefix = "SurvivorsDragonUi.zip", FixedAssetName = true };
            if (string.Equals(normalized, KeijinUiLatestUrl, StringComparison.OrdinalIgnoreCase))
                return new GitHubAddonSource { Url = KeijinUiLatestUrl, Repo = "KeijinDE/KeijinUI_modern-as-ui", AssetPrefix = "KeijinUI-" };
            if (string.Equals(normalized, AzerothFieldKitLatestUrl, StringComparison.OrdinalIgnoreCase))
                return new GitHubAddonSource { Url = AzerothFieldKitLatestUrl, Repo = "Maidenquinn/azeroth-fieldkit", AssetPrefix = "AzerothFieldKit-" };
            if (string.Equals(normalized, AstinyPlatesLatestUrl, StringComparison.OrdinalIgnoreCase))
                return new GitHubAddonSource { Url = AstinyPlatesLatestUrl, Repo = "Valorless/ASTinyPlates", AssetPrefix = "ASTinyPlates-" };
            if (string.Equals(normalized, AsBuilderLatestUrl, StringComparison.OrdinalIgnoreCase))
                return new GitHubAddonSource { Url = AsBuilderLatestUrl, Repo = "cadoky/ASBuilder", AssetPrefix = "ASBuilder-" };
            if (string.Equals(normalized, AzerothHubLatestUrl, StringComparison.OrdinalIgnoreCase))
                return new GitHubAddonSource { Url = AzerothHubLatestUrl, Repo = "Maidenquinn/AzerothHub", AssetPrefix = "AzerothHub-" };
            string anyRepo;
            if (TryGitHubLatestRepo(normalized, out anyRepo))
                return new GitHubAddonSource { Url = normalized, Repo = anyRepo, AssetPrefix = AssetPrefixFor(normalized), Generic = true };
            return null;
        }

        static bool IsSha256(string value)
        {
            if (string.IsNullOrWhiteSpace(value) || value.Length != 64) return false;
            foreach (char c in value) if (!Uri.IsHexDigit(c)) return false;
            return true;
        }

        static Dictionary<string, object> ReadGitHubRelease(string apiUrl, bool refresh = false)
        {
            return Archive.ReadGitHubReleaseMetadata(apiUrl, refresh);
        }

        // The catalog's AddOns folders for this URL (addons.txt's 4th field)
        static List<string> CatalogFoldersFor(string url)
        {
            string normalized = (url ?? "").TrimEnd('/');
            foreach (AddonEntry entry in Data.Addons)
                if (string.Equals((entry.Url ?? "").TrimEnd('/'), normalized, StringComparison.OrdinalIgnoreCase))
                    return new List<string>(entry.Folders);
            return new List<string>();
        }

        static string LettersAndDigits(string text)
        {
            var sb = new System.Text.StringBuilder();
            foreach (char c in text ?? "") if (char.IsLetterOrDigit(c)) sb.Append(char.ToLowerInvariant(c));
            return sb.ToString();
        }

        // How likely a release file is to be the addon: its catalog asset= start, the addon's folder name,
        // the repo's name and the version in its name count for it; installers and sources count against it.
        static int AddonAssetScore(string name, string tag, GitHubAddonSource source, List<string> folders)
        {
            int score = 0;
            string flat = LettersAndDigits(name);
            if (!string.IsNullOrEmpty(source.AssetPrefix) && name.StartsWith(source.AssetPrefix, StringComparison.OrdinalIgnoreCase)) score += 100;
            foreach (string folder in folders)
            {
                string f = LettersAndDigits(folder);
                if (f.Length > 0 && flat.Contains(f)) { score += 40; break; }
            }
            string repoName = LettersAndDigits(source.Repo.Substring(source.Repo.IndexOf('/') + 1));
            if (repoName.Length > 0 && flat.Contains(repoName)) score += 20;
            string version = LettersAndDigits((tag ?? "").TrimStart('v', 'V'));
            if (version.Length > 0 && flat.Contains(version)) score += 10;
            foreach (string bad in new[] { "installer", "setup", "source", "src", "debug", "symbols" })
                if (flat.Contains(bad)) score -= 200;
            return score;
        }

        // Every ZIP of a release, most likely first, then GitHub's source archive of the tag. No file name has to
        // match: the install opens each in turn and takes the first that holds the addon's folder (its .toc).
        // A GitHub digest is checked where GitHub published one. Null only for a draft or a release without a tag.
        static GitHubAddonRelease ParseGenericGitHubAddonRelease(Dictionary<string, object> release, GitHubAddonSource source)
        {
            if (release == null || string.Equals(Json.Str(release, "draft"), "True", StringComparison.OrdinalIgnoreCase)) return null;
            string tag = Json.Str(release, "tag_name");
            if (tag.Length == 0) return null;
            List<string> folders = CatalogFoldersFor(source.Url);
            var files = new List<KeyValuePair<int, KeyValuePair<string, string>>>();   // score, (url, digest)
            foreach (object value in Json.AsList(Json.Get(release, "assets")) ?? new List<object>())
            {
                Dictionary<string, object> asset = Json.AsDict(value);
                string name = Json.Str(asset, "name"), link = Json.Str(asset, "browser_download_url");
                if (!name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase) || !Net.IsHttps(link)) continue;
                string state = Json.Str(asset, "state");
                if (state.Length > 0 && !string.Equals(state, "uploaded", StringComparison.OrdinalIgnoreCase)) continue;
                string sha = Json.Str(asset, "digest");
                sha = sha.StartsWith("sha256:", StringComparison.OrdinalIgnoreCase) ? sha.Substring(7).ToLowerInvariant() : "";
                if (!IsSha256(sha)) sha = "";
                files.Add(new KeyValuePair<int, KeyValuePair<string, string>>(AddonAssetScore(name, tag, source, folders),
                          new KeyValuePair<string, string>(link, sha)));
            }
            // highest score first; equal scores keep GitHub's order
            var ordered = new List<KeyValuePair<string, string>>();
            var used = new bool[files.Count];
            for (int round = 0; round < files.Count; round++)
            {
                int pick = -1;
                for (int i = 0; i < files.Count; i++)
                    if (!used[i] && (pick < 0 || files[i].Key > files[pick].Key)) pick = i;
                used[pick] = true;
                ordered.Add(files[pick].Value);
            }
            ordered.Add(new KeyValuePair<string, string>("https://github.com/" + source.Repo + "/archive/refs/tags/" +
                                                         Uri.EscapeDataString(tag) + ".zip", ""));
            var result = new GitHubAddonRelease { Tag = tag, Url = ordered[0].Key, Sha256 = ordered[0].Value };
            for (int i = 1; i < ordered.Count; i++)
                result.Alternatives.Add(new KeyValuePair<string, string>(ordered[i].Key, IsSha256(ordered[i].Value) ? ordered[i].Value : Archive.NoDigest));
            DateTime published;
            if (!DateTime.TryParse(Json.Str(release, "published_at"), CultureInfo.InvariantCulture,
                                   DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out published))
                published = DateTime.MinValue;
            result.PublishedAt = published;
            return result;
        }

        static GitHubAddonRelease ParseGitHubAddonRelease(Dictionary<string, object> release, GitHubAddonSource source)
        {
            return ParseGenericGitHubAddonRelease(release, source);
        }

        // /releases/latest; a project with only pre-releases (latest answers 404) gets its newest published release.
        // GitHub busy or out of requests: Archive.ReadGitHubReleaseMetadata answers from its last copy or the release page.
        static GitHubAddonRelease ResolveGitHubAddonRelease(string url, bool refresh = false)
        {
            GitHubAddonSource source = GitHubAddonSourceFor(url);
            if (source == null) throw new UserError("This add-on is not configured for direct GitHub release updates.");
            try
            {
                GitHubAddonRelease stable = ParseGitHubAddonRelease(ReadGitHubRelease(
                    "https://api.github.com/repos/" + source.Repo + "/releases/latest", refresh), source);
                if (stable != null) return stable;
            }
            catch (UserError e)
            {
                if (e.Message.IndexOf("error 404", StringComparison.OrdinalIgnoreCase) < 0) throw;
            }

            string text;
            using (var resp = Net.Open("https://api.github.com/repos/" + source.Repo + "/releases?per_page=100",
                                       15000, true, "GitHub", "application/vnd.github+json"))
            using (var rd = new StreamReader(resp.GetResponseStream(), System.Text.Encoding.UTF8)) text = rd.ReadToEnd();
            GitHubAddonRelease newest = null;
            foreach (object value in Json.AsList(Json.Parse(text)) ?? new List<object>())
            {
                GitHubAddonRelease candidate = ParseGitHubAddonRelease(Json.AsDict(value), source);
                if (candidate != null && (newest == null || candidate.PublishedAt > newest.PublishedAt)) newest = candidate;
            }
            if (newest == null) throw new UserError(source.Repo + " has no published release on GitHub yet.");
            return newest;
        }

        GitHubAddonRelease DynamicGitHubRelease(string url)
        {
            GitHubAddonRelease release;
            return dynamicAddonReleases.TryGetValue((url ?? "").TrimEnd('/'), out release) ? release : null;
        }

        // true when the copy on GitHub is newer than the one installed
        bool AddonHasUpdate(AddonEntry it)
        {
            if (it.WorkInProgress || addonUpdateProblems.ContainsKey(it.Name)) return false;
            if (mirrorDates == null || it.Category == Data.MyAddonsCategory || !Net.IsWebAddress(it.Url)) return false;
            if (AddonSetting(AddonPins(), it.Name) != null) return false;   // rolled back on purpose: left as it is
            if (IsDynamicGitHubAddonUrl(it.Url))
            {
                string sourceUrl = (it.Url ?? "").TrimEnd('/');
                if (!dynamicAddonReleaseChecked.Contains(sourceUrl)) return false;
                GitHubAddonRelease currentRelease = DynamicGitHubRelease(it.Url);
                if (currentRelease == null) return true;  // keep a manual UPDATE available if the startup check failed
                string installedRelease = Convert.ToString(AddonSetting(AddonInstalledRelease(), it.Name), CultureInfo.InvariantCulture);
                // An older mirrored install has no release identity yet, so move it to the current upstream asset once.
                return !string.Equals(installedRelease, currentRelease.Identity, StringComparison.OrdinalIgnoreCase);
            }
            string file = Uri.UnescapeDataString(it.Url.Substring(it.Url.LastIndexOf('/') + 1));
            DateTime up;
            if (!mirrorDates.TryGetValue(file, out up)) return false;
            DateTime installed = DateTime.MinValue;
            string rec = Convert.ToString(AddonSetting(AddonInstalledAt(), it.Name), System.Globalization.CultureInfo.InvariantCulture);
            if (!DateTime.TryParse(rec, System.Globalization.CultureInfo.InvariantCulture,
                                   System.Globalization.DateTimeStyles.AdjustToUniversal | System.Globalization.DateTimeStyles.AssumeUniversal, out installed))
            {
                // installed before the launcher kept the date: the newest file in its folders says when
                foreach (string f in AddonFoldersOf(it.Name))
                    try
                    {
                        string dir = Path.Combine(AddonsDir(), f);
                        foreach (string file2 in Directory.GetFiles(dir, "*", SearchOption.AllDirectories))
                        {
                            DateTime w = File.GetLastWriteTimeUtc(file2);
                            if (w > installed) installed = w;
                        }
                    }
                    catch (Exception) { }
                if (installed == DateTime.MinValue) return false;
            }
            string identity = MirrorAddonIdentity(it);
            string installedIdentity = Convert.ToString(AddonSetting(AddonInstalledRelease(), it.Name), CultureInfo.InvariantCulture);
            return identity.Length > 0 ? !string.Equals(identity, installedIdentity, StringComparison.Ordinal) : up > installed.AddMinutes(1);
        }

        void AddonsDownloadView(int top)
        {
            AskMirrorDates();                                // which addons have a newer copy on GitHub
            Color listbg = Theme.Ink;
            var sp = MakeScroll(panel, new Rectangle(20, top, PW - 40, PH - 12 - top), listbg, Theme.Bronze);
            int cat = Math.Min(Math.Max(addonCategory, 0), Data.AddonCategories.Length - 1);
            var items = new List<AddonEntry>();
            var all = new List<AddonEntry>(Data.Addons);
            foreach (var a in all)
                if (a.Category == Data.AddonCategories[cat]) items.Add(a);
            items.Sort(delegate(AddonEntry a, AddonEntry b) { return string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase); });   // A to Z
            int y0 = 4;
            if (Data.AddonCategories[cat] == Data.MyAddonsCategory)
            {
                var hint = UI.Text("Put addon .zip files in the My Addons folder; they stay on this PC.", 9, FontStyle.Italic,
                                   Theme.GoldDim, listbg, sp.ClientSize.Width - 160);
                hint.Size = VStack.Measure(hint);
                hint.Location = new Point(10, 8);
                sp.Controls.Add(hint);
                var open = new WowButton("OPEN FOLDER", delegate { Directory.CreateDirectory(Data.MyAddonsDir); Util.OpenInShell(Data.MyAddonsDir); }, 130, 28, 9);
                open.Location = new Point(sp.ClientSize.Width - 4 - 140, 6);
                sp.Controls.Add(open);
                y0 = Math.Max(hint.Bottom, open.Bottom) + 8;
            }
            string forgeCopy = "SurvivorsForge helps provide addon pictures and shares its addon expertise.\n" +
                "Visit the SurvivorsForge website to discover even more addons than the ones listed here.";
            var trust = new LinkLabel();
            trust.Text = forgeCopy;
            trust.Font = Theme.F(9, FontStyle.Regular);
            trust.ForeColor = Theme.Text;
            trust.LinkColor = Theme.Gold;
            trust.ActiveLinkColor = Theme.Gold;
            trust.VisitedLinkColor = Theme.GoldDim;
            trust.LinkBehavior = LinkBehavior.HoverUnderline;
            trust.BackColor = listbg;
            trust.AutoSize = false;
            trust.TextAlign = ContentAlignment.TopLeft;
            trust.Size = new Size(Math.Max(80, sp.ClientSize.Width - 20),
                TextRenderer.MeasureText(forgeCopy, trust.Font, new Size(Math.Max(80, sp.ClientSize.Width - 20), 1000),
                    TextFormatFlags.WordBreak | TextFormatFlags.NoPadding).Height + 4);
            int sfStart = forgeCopy.IndexOf("SurvivorsForge website", StringComparison.Ordinal);
            if (sfStart >= 0) trust.Links.Add(sfStart, "SurvivorsForge website".Length, "https://rezondes.github.io/SurvivorsForge/");
            trust.LinkClicked += delegate(object sender, LinkLabelLinkClickedEventArgs e)
            {
                string target = e.Link.LinkData as string;
                if (!string.IsNullOrEmpty(target)) Util.OpenInShell(target);
            };
            trust.Location = new Point(10, y0 + 4);
            sp.Controls.Add(trust);
            int ruleY = trust.Bottom + 7;
            var rule = UI.Line(Theme.Bad, Math.Max(40, sp.ClientSize.Width - 20), 2);
            rule.Location = new Point(10, ruleY);
            sp.Controls.Add(rule);
            y0 = rule.Bottom + 9;

            // Pull provider details only while a download category is open; the seven-day cache prevents view changes
            // from becoming extra API refreshes. Rebuild after the bounded fetch so eligible artwork can appear.
            SurvivorsForge.PrepareDownloadPage(items, delegate
            {
                if (!IsDisposed && current == "Addons" && addonMode == "download") BuildPanel("Addons");
            });
            if (items.Count == 0)
            {
                var l = UI.Text("No downloadable addons listed yet.",
                                11, FontStyle.Italic, Theme.GoldDim, listbg, 0);
                l.Location = new Point(10, y0 + 4);
                sp.Controls.Add(l);
                return;
            }
            int w = sp.ClientSize.Width - 4 - 8;
            int y = y0;
            // every list is small blocks, four to a row
            AddonCards(sp, items, w, y, listbg);
        }

        // Download More (every category): a small block for each addon, four to a row: its name, what it does, and its button at the bottom
        readonly ThemedToolTip addonTips = new ThemedToolTip();

        void AddonCards(ScrollPanel sp, List<AddonEntry> items, int w, int y, Color listbg)
        {
            EnsureCatalogVersions();
            int gap = Theme.Px(6), cols = Math.Max(1, Math.Min(4, w / Theme.Px(155)));
            int cw = (w - gap * (cols - 1)) / cols, textW = cw - 2 - 14;
            int bw = (int)((cw - 2 - 16) / Theme.Zoom);       // the block's button runs along its bottom
            var heights = new List<int>();
            var built = new List<Control>();
            foreach (var item in items)
            {
                var it = item;
                var card = new Bordered(Theme.GoldDim, 1);
                var inner = new Panel();
                inner.BackColor = listbg;
                inner.Dock = DockStyle.Fill;
                card.Controls.Add(inner);
                bool haveIt = AddonFoldersOf(it.Name).Count > 0;
                bool update = haveIt && AddonHasUpdate(it);
                ForgeAddonInfo forge = SurvivorsForge.GetInfo(it);
                // The seal means this catalog entry was manually reviewed once. Keep that
                // decision in the shipped catalog; do not infer it from rolling API status.
                Image watermark = it.ForgeVerified ? SurvivorsForge.LoadWatermark(forge) : null;
                bool verified = it.ForgeVerified;
                var btn = it.WorkInProgress ? new WowButton("Work in Progress", delegate { }, bw, 30, 9)
                        : update ? new WowButton("UPDATE", delegate { CheckAddonUpdates(it, true); }, bw, 30, 9)
                        : haveIt ? new WowButton("UNINSTALL", delegate { UninstallAddon(it.Name); }, bw, 30, 9)
                                 : new WowButton("INSTALL", delegate { DownloadAddon(it.Name, it.Url); }, bw, 30, 9);
                if (it.WorkInProgress) btn.Enabled = false;
                if (update) btn.Selected = true;
                if (btn.Width > cw - 14) btn.Width = cw - 14;  // never wider than its block
                int ty = 6;
                var badge = verified ? UI.Text("Verified", 7.5f, FontStyle.Italic, Theme.Good,
                    listbg, Theme.Px(50), false) : null;
                if (badge != null) badge.Size = VStack.Measure(badge);
                int nameWidth = watermark == null ? textW : Math.Max(30,
                    textW - Theme.Px(16) - (badge == null ? 0 : badge.Width + Theme.Px(4)) - Theme.Px(6));
                var name = UI.Text(it.Name, 9.5f, FontStyle.Bold, Theme.Gold, listbg, nameWidth, false);
                name.Size = VStack.Measure(name);
                name.Location = new Point(7, ty);
                if (watermark != null)
                {
                    var forgeMark = new PictureBox();
                    forgeMark.Image = watermark;
                    forgeMark.SizeMode = PictureBoxSizeMode.Zoom;
                    forgeMark.BackColor = listbg;
                    int logoBadgeGap = Theme.Px(2);
                    int groupWidth = Theme.Px(16) + (badge == null ? 0 : badge.Width + logoBadgeGap);
                    int groupLeft = cw - Theme.Px(7) - groupWidth;
                    forgeMark.Bounds = new Rectangle(groupLeft, ty, Theme.Px(16), Theme.Px(16));
                    forgeMark.Disposed += delegate { try { watermark.Dispose(); } catch (Exception) { } };
                    inner.Controls.Add(forgeMark);
                    if (badge != null)
                    {
                        badge.Location = new Point(forgeMark.Right + logoBadgeGap, ty + Math.Max(0, (forgeMark.Height - badge.Height) / 2));
                        inner.Controls.Add(badge);
                    }
                }
                if (SurvivorsForge.HasGalleryImages(forge))
                {
                    name.Cursor = Cursors.Hand;
                    name.MouseEnter += delegate
                    {
                        if (!GuideHover.CanShow(name)) return;
                        List<Image> gallery = SurvivorsForge.LoadGalleryImages(forge);
                        try { GuideHover.ShowGallery(name, it.Name, "Click to browse addon pictures.", gallery, false); }
                        finally { foreach (Image image in gallery) try { image.Dispose(); } catch (Exception) { } }
                    };
                    name.MouseLeave += delegate { GuideHover.Hide(name); };
                    name.MouseClick += delegate(object sender, MouseEventArgs e)
                    {
                        if (e.Button != MouseButtons.Left || !GuideHover.CanShow(name)) return;
                        List<Image> gallery = SurvivorsForge.LoadGalleryImages(forge);
                        string sourceText = forge.HasSourceScreenshots ? "Screenshots from SurvivorsForge." : "Provided addon screenshots.";
                        try { GuideHover.ShowGallery(name, it.Name, sourceText, gallery, true); }
                        finally { foreach (Image image in gallery) try { image.Dispose(); } catch (Exception) { } }
                    };
                }
                inner.Controls.Add(name);
                // the newest release's version, right-aligned under the Verified badge (when the release is known)
                string latestTag = LatestVersionLabel(it);
                if (latestTag.Length > 0)
                {
                    var versionText = UI.Text(latestTag, 7.5f, FontStyle.Italic, Theme.GoldDim, listbg, Theme.Px(90), false);
                    versionText.Size = VStack.Measure(versionText);
                    versionText.Location = new Point(cw - Theme.Px(7) - versionText.Width, Theme.Px(20));
                    inner.Controls.Add(versionText);
                }
                inner.Controls.Add(name);
                ty += name.Height + 1;
                if (verified && watermark == null && badge != null)
                {
                    badge.Location = new Point(7, ty);
                    inner.Controls.Add(badge);
                    ty += badge.Height + 2;
                }
                if (it.Description.Length > 0)
                {
                    // a short line on the block; the whole description when the pointer rests on it
                    string shortDesc = Lang.T(it.Description);
                    int cut = shortDesc.IndexOfAny(new[] { ':', '.', '(' });
                    if (cut > 12) shortDesc = shortDesc.Substring(0, cut).Trim();
                    if (shortDesc.Length > 64) shortDesc = shortDesc.Substring(0, 62).TrimEnd() + "…";
                    var d = UI.Text(shortDesc, 7.5f, FontStyle.Regular, Theme.Text, listbg, textW, false);
                    addonTips.SetToolTip(d, Lang.T(it.Description));
                    d.Size = VStack.Measure(d);
                    d.Location = new Point(7, ty);
                    inner.Controls.Add(d);
                    ty += d.Height;
                }
                if (!string.IsNullOrWhiteSpace(it.Note))
                {
                    var note = UI.Text(it.Note, 7.5f, FontStyle.Bold, Theme.Gold, listbg, textW, false);
                    note.Size = VStack.Measure(note);
                    note.Location = new Point(7, ty + Theme.Px(3));
                    inner.Controls.Add(note);
                    ty += note.Height + Theme.Px(3);
                }
                if (haveIt)
                {
                    var on = UI.Text("Installed", 7.5f, FontStyle.Italic, Theme.GoldDim, listbg, 0);
                    on.Size = VStack.Measure(on);
                    inner.Controls.Add(on);
                    on.Tag = "installed";
                    ty += on.Height + 2;
                }
                inner.Controls.Add(btn);
                btn.Tag = "button";
                heights.Add(ty + 6 + btn.Height + 8);
                built.Add(card);
            }
            for (int r = 0; r * cols < built.Count; r++)
            {
                int h = 90;
                for (int i = r * cols; i < Math.Min(built.Count, r * cols + cols); i++) h = Math.Max(h, heights[i]);
                for (int i = r * cols; i < Math.Min(built.Count, r * cols + cols); i++)
                {
                    var card = built[i];
                    card.Bounds = new Rectangle(4 + (i % cols) * (cw + gap), y, cw, h);
                    var inner = card.Controls[0];
                    Control b = null;
                    foreach (Control c in inner.Controls)
                        if ((c.Tag as string) == "button") { c.Location = new Point((cw - 2 - c.Width) / 2, h - 2 - 8 - c.Height); b = c; }
                    foreach (Control c in inner.Controls)
                        if ((c.Tag as string) == "installed" && b != null) c.Location = new Point(7, b.Top - c.Height - 2);
                    sp.Controls.Add(card);
                    if (items[i].WorkInProgress)
                    {
                        var tape = new Panel();
                        tape.Bounds = new Rectangle(0, Math.Max(8, h / 2 - Theme.Px(13)), cw - 2, Theme.Px(26));
                        tape.Cursor = Cursors.No;
                        tape.AccessibleName = "Work in Progress";
                        tape.Paint += delegate(object sender, PaintEventArgs e)
                        {
                            Rectangle rct = ((Control)sender).ClientRectangle;
                            e.Graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
                            using (var gold = new SolidBrush(Color.FromArgb(238, 194, 119, 34))) e.Graphics.FillRectangle(gold, rct);
                            using (var dark = new Pen(Color.FromArgb(235, 30, 21, 15), Theme.Px(7)))
                                for (int x = -rct.Height; x < rct.Width + rct.Height; x += Theme.Px(20))
                                    e.Graphics.DrawLine(dark, x, rct.Height, x + rct.Height, 0);
                            Rectangle label = new Rectangle(Theme.Px(4), Theme.Px(3), Math.Max(0, rct.Width - Theme.Px(8)), Math.Max(0, rct.Height - Theme.Px(6)));
                            using (var backing = new SolidBrush(Color.FromArgb(238, 20, 15, 12))) e.Graphics.FillRectangle(backing, label);
                            TextRenderer.DrawText(e.Graphics, "Work in Progress", Theme.F(8.5f, FontStyle.Bold), label,
                                Theme.Gold, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
                        };
                        inner.Controls.Add(tape);
                        tape.BringToFront();
                        addonTips.SetToolTip(tape, "Temporarily unavailable while work is in progress.");
                    }
                }
                y += h + gap;
            }
            var pad = new Panel();
            pad.BackColor = listbg;
            pad.Bounds = new Rectangle(4, y, 1, 4);
            sp.Controls.Add(pad);
        }

        bool AddonsLocked()
        {
            if (GameRunningFlag || SyncBusy)
            {
                SetStatus("Addons can't be changed while the game runs or a sync is running.");
                return true;
            }
            return false;
        }

        // quiet: an automatic update (no message box on a failure; the log says it and the next one goes on)
        void DownloadAddon(string name, string url, Action after = null, bool quiet = false)
        {
            if (AddonsLocked()) { if (after != null) after(); return; }
            AddonEntry catalogEntry = null;
            foreach (AddonEntry candidate in Data.Addons)
                if (string.Equals(candidate.Name, name, StringComparison.Ordinal) && string.Equals(candidate.Url, url, StringComparison.Ordinal))
                {
                    catalogEntry = candidate;
                    break;
                }
            if (catalogEntry != null && catalogEntry.WorkInProgress)
            {
                SetStatus(name + " is temporarily unavailable: work in progress.");
                if (after != null) after();
                return;
            }
            var conflictingOn = new List<string>();
            if (catalogEntry != null)
            {
                try
                {
                    List<KeyValuePair<string, bool>> installed = Addons.Scan(AddonsDir());
                    foreach (string folder in catalogEntry.Folders)
                        foreach (var partner in Addons.PartnersOf(AddonsDir(), folder, installed))
                            foreach (var currentAddon in installed)
                                if (currentAddon.Key == partner.Key && currentAddon.Value && !conflictingOn.Contains(currentAddon.Key))
                                    conflictingOn.Add(currentAddon.Key);
                }
                catch (Exception) { }
                if (conflictingOn.Count > 0 && !quiet && Msg.Show(this,
                    name + " cannot be enabled with " + string.Join(", ", conflictingOn.ToArray()) + ". Install it and switch those addons off?",
                    "Addon conflict", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;
            }
            if (addonDlBusy)
            {
                SetStatus("Another addon is still downloading.");
                return;
            }
            bool keepDisabled = false;
            var existingFolders = AddonFoldersOf(name);
            if (existingFolders.Count > 0)
            {
                keepDisabled = true;
                foreach (string folder in existingFolders)
                    if (Directory.Exists(Path.Combine(AddonsDir(), folder)) && !folder.EndsWith(Addons.DisabledSuffix)) keepDisabled = false;
            }
            addonDlBusy = true;
            SetStatus("Installing " + name + "...");
            string dir = AddonsDir();
            Bg(delegate
            {
                List<string> names = null;
                string err = null;
                string installedRelease = null;
                GitHubAddonRelease resolvedRelease = null;
                BuiltinAddon builtin = BuiltinAddons.Find(url);
                try
                {
                    if (builtin != null) { BuiltinAddons.Install(dir, builtin); names = new List<string> { builtin.Folder }; }
                    else
                    {
                        string downloadUrl = url;
                        string expectedSha = null;
                        List<KeyValuePair<string, string>> alternatives = null;
                        if (IsDynamicGitHubAddonUrl(url))
                        {
                            GitHubAddonRelease release = ResolveGitHubAddonRelease(url);
                            downloadUrl = release.Url;
                            expectedSha = release.Digest;
                            alternatives = release.Alternatives;
                            installedRelease = release.Identity;
                            resolvedRelease = release;
                        }
                        names = InstallAddonUpdateSafely(downloadUrl, dir, Net.IsWebAddress(downloadUrl) ? AddonArchive.NewPath(name) : null, expectedSha, existingFolders, keepDisabled, delegate {return !GameRunningFlag && !Util.GameRunning() && !SyncBusy;},
                                                         catalogEntry != null ? catalogEntry.Folders : null, alternatives);
                        AddonArchive.Tidy(name);              // kept for ROLLBACK (Addons › Advanced)
                    }
                }
                catch (Exception e) { err = e.Message.Length > 0 ? e.Message : e.GetType().Name; }
                Ui(delegate
                {
                    addonDlBusy = false;
                    addonLastResults[name] = err;
                    if (err == null) addonUpdateProblems.Remove(name); else addonUpdateProblems[name] = err;
                    SetStatus(err == null ? name + " installed." : name + " failed.");
                    if (err == null)
                    {

                        var switchedOff = new List<string>();
                        if (catalogEntry != null && !keepDisabled)
                        {
                            foreach (string folder in catalogEntry.Folders)
                                try
                                {
                                    foreach (string off in Addons.SwitchOffPartners(AddonsDir(), folder))
                                        if (!switchedOff.Contains(off)) switchedOff.Add(off);
                                }
                                catch (Exception) { }
                            try
                            {
                                foreach (string off in Addons.EnforceConflicts(AddonsDir()))
                                    if (!switchedOff.Contains(off)) switchedOff.Add(off);
                            }
                            catch (Exception) { }
                        }
                        var rec = new List<object>();
                        foreach (string n in names) rec.Add(n);
                        AddonInstalls()[name] = rec;
                        AddonInstalledAt()[name] = DateTime.UtcNow.ToString("o", System.Globalization.CultureInfo.InvariantCulture);
                        if (string.IsNullOrEmpty(installedRelease) && catalogEntry != null) installedRelease = MirrorAddonIdentity(catalogEntry);
                        if (!string.IsNullOrEmpty(installedRelease)) AddonInstalledRelease()[name] = installedRelease;

                        if (resolvedRelease != null)
                        {
                            string releaseUrl = (url ?? "").TrimEnd('/');
                            dynamicAddonReleases[releaseUrl] = resolvedRelease;
                            dynamicAddonReleaseChecked.Add(releaseUrl);
                        }
                        AddonPins().Remove(name);            // the newest copy: no longer [Outdated]
                        ClearLegacyAddonSettings(name);
                        Cfg.Save();
                        if (current == "Addons" || current == "Stats") BuildPanel(current);
                        if (switchedOff.Count > 0)
                            SetStatus(name + " installed. " + string.Join(", ", switchedOff.ToArray()) + " switched off because they conflict.");
                        if (after != null) after();
                    }
                    else if (quiet)
                    {
                        SyncLog("  " + name + " addon not updated: " + err, Level.Warn);
                        if (after != null) try { after(); } catch (Exception) { }
                    }
                    else Msg.Show(this, "Could not install " + name + ":\n\n" + err, "Addons",
                                         MessageBoxButtons.OK, MessageBoxIcon.Error);
                });
            });
        }

        // UNINSTALL (Download More): the question opens in My Addons' right column, beside the whole list
        void UninstallAddon(string name)
        {
            if (AddonsLocked()) return;
            if (AddonFoldersOf(name).Count == 0)
            {
                SetStatus(name + " is not installed.");
                BuildPanel("Addons");
                return;
            }
            addonMode = "installed";
            askUninstallName = name;
            askUninstallFolder = null;
            BuildPanel("Addons");
        }

        void DoUninstallAddon(string name)
        {
            askUninstallName = askUninstallFolder = null;
            if (AddonsLocked()) { BuildPanel("Addons"); return; }
            var folders = AddonFoldersOf(name);
            List<string> removed;
            try { removed = Addons.Uninstall(AddonsDir(), folders, SyncSettings().SavedDir); }
            catch (Exception e)
            {
                Msg.Show(side, "Could not uninstall " + name + ":\n\n" + e.Message, "Addons", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }
            AddonInstalls().Remove(name);
            AddonInstalledAt().Remove(name);
            AddonInstalledRelease().Remove(name);
            ClearLegacyAddonSettings(name);
            Cfg.Save();
            SetStatus(removed.Count > 0 ? name + " uninstalled." : name + " was not installed.");
            if (current == "Addons") BuildPanel("Addons");
        }

        // one folder of the AddOns folder deleted (the installed list's X), with the settings it saved
        void UninstallFolder(string folder)
        {
            if (AddonsLocked()) return;
            askUninstallFolder = folder;
            askUninstallName = null;
            BuildPanel("Addons");
        }

        void DoUninstallFolder(string folder)
        {
            askUninstallName = askUninstallFolder = null;
            if (AddonsLocked()) { BuildPanel("Addons"); return; }
            var all = new List<string> { folder };
            all.AddRange(Addons.CompanionsOf(folder));       // the folders that came with it go too
            try { Addons.Uninstall(AddonsDir(), all, SyncSettings().SavedDir); }
            catch (Exception e)
            {
                Msg.Show(side, "Could not uninstall " + folder + ":\n\n" + e.Message, "Addons", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }
            // a Download More addon made of this folder is no longer installed
            foreach (var kv in new List<KeyValuePair<string, object>>(AddonInstalls()))
            {
                var l = Json.AsList(kv.Value);
                if (l != null && (l.Contains(folder) || all.Exists(delegate(string x) { return l.Contains(x); }))) { AddonInstalls().Remove(kv.Key); AddonInstalledAt().Remove(kv.Key); AddonInstalledRelease().Remove(kv.Key); }
            }
            Cfg.Save();
            SetStatus(folder + " uninstalled.");
            BuildPanel("Addons");
        }

        // the uninstall question in the right column: what goes, UNINSTALL and CANCEL; the list stays as it is
        void AddonsUninstallAsk(int cx, int top)
        {
            addonCount = null;
            int w = (int)(190 * Theme.Zoom);
            string what = askUninstallName ?? askUninstallFolder;
            var folders = askUninstallName != null ? AddonFoldersOf(askUninstallName) : new List<string> { askUninstallFolder };
            var head = UI.Text(Lang.T("Uninstall") + " " + what + "?", 11, FontStyle.Bold, Theme.Gold, Theme.Panel, w, false);
            head.Size = VStack.Measure(head);
            Put(head, cx, top);
            var body = UI.Text(Lang.T("Folders") + ": " + string.Join(", ", folders) + "\n\n" +
                               Lang.T("This deletes everything it added, including the settings it saved."),
                               9, FontStyle.Regular, Theme.Text, Theme.Panel, w, false);
            body.Size = VStack.Measure(body);
            Put(body, cx, BottomOf(head) + 8);
            string n = askUninstallName, f = askUninstallFolder;
            var yes = Put(new WowButton("UNINSTALL", delegate { if (n != null) DoUninstallAddon(n); else DoUninstallFolder(f); }, 190, 32, 10), cx, BottomOf(body) + 12);
            Put(new WowButton("CANCEL", delegate { askUninstallName = askUninstallFolder = null; BuildPanel("Addons"); }, 190, 28, 9), cx, BottomOf(yes) + 6);
        }

        void AddonsInstalledView(int top)
        {
            string dir = AddonsDir();
            if (!Directory.Exists(dir))
            {
                PutText("No AddOns folder found.\nSet the game config folder in Options\n(the benilla-config folder).", 12,
                        FontStyle.Regular, Theme.Text, 20, top, PW-40);
                return;
            }
            List<KeyValuePair<string, bool>> addons;
            try
            {
                // addons that can't be on together and are both on are shown first, for the player to pick one (PLAY
                // keeps the preferred one if nobody picks)
                addons = Addons.Scan(dir);
            }
            catch (Exception e)
            {
                PutText(e.Message, 11, FontStyle.Regular, Theme.Text, 20, top, PW - 40);
                return;
            }
            int bottom = PH - 12;
            // Advanced settings are a replacement for the basic right-hand submenu, not an extra layer over it.
            // Keeping both sets of controls visible made the profile stack run into DOWNLOAD MORE / REFRESH /
            // TOGGLE ALL on shorter windows. The left addon list stays visible in either mode.
            int cx = PW - 20 - (int)(190 * Theme.Zoom) - Theme.Px(12);
            bool advanced = Cfg.Bool("addons_advanced", false);
            if (askUninstallName != null || askUninstallFolder != null) AddonsUninstallAsk(cx, top);
            else if (rollbackOpen) AddonsRollbackPanel(cx, top);
            else {
            int columnX=cx, columnTop=top, columnBottom=bottom;
            var actions=MakeThinScroll(panel,new Rectangle(cx,top,PW-cx-12,bottom-top),Theme.Panel,Theme.Bronze,Theme.GoldDim,Theme.Gold);
            Control previousPut=putInto; putInto=actions;
            cx=1; top=4; bottom=Math.Max(actions.ClientSize.Height,Theme.Px(advanced?300:170));
            try
            {
            var profiles = Cfg.Section("addon_profiles");
            int py = bottom;
            string active = ActiveAddonProfile();
            {
                // Keep the familiar addon actions at the top in both Basic and Advanced views.
                // Advanced profile controls are anchored separately at the bottom.
                var more = Put(new WowButton("DOWNLOAD MORE ›", delegate { SetAddonMode("download"); }, 190, 32, 10), cx, top);
                int rtop = BottomOf(more) + 6;
                var refresh = Put(new WowButton("REFRESH", delegate { CheckAddonUpdates(null, false); }, 92, 28, 9), cx, rtop);
                Put(new WowButton("FOLDER", delegate { Util.OpenInShell(dir); }, 92, 28, 9), cx + (int)(98 * Theme.Zoom), rtop);
                var toggle = Put(new WowButton("TOGGLE ALL", ToggleAllAddons, 190, 34, 11), cx, BottomOf(refresh) + 6);
                addonCount = UI.Text("", 10, FontStyle.Italic, Theme.GoldDim, Theme.Panel, 0);
                addonCount.AutoSize = false;
                addonCount.TextAlign = ContentAlignment.TopCenter;
                addonCount.Bounds = new Rectangle(cx, BottomOf(toggle) + 2, toggle.Width, Math.Max(20,addonCount.Font.Height+2));
                Put(addonCount, cx, BottomOf(toggle) + 2);
            }
            // The basic view has one bottom button; the advanced view replaces it with profile controls and rollback.
            py -= (int)(28 * Theme.Zoom) + 2;
            Put(new WowButton(advanced ? "BASIC SETTINGS" : "ADVANCED SETTINGS", delegate
            {
                Cfg.Set("addons_advanced", !advanced);
                Cfg.Save();
                BuildPanel("Addons");
            }, 190, 28, 9), cx, py);
            if (advanced)                                    // ROLLBACK: older kept copies of the addons (its own column)
            {
                py -= (int)(28 * Theme.Zoom) + 4;
                Put(new WowButton("ROLLBACK", delegate { rollbackOpen = true; BuildPanel("Addons"); }, 190, 28, 9), cx, py);
            }
            py -= 8;
            foreach (string slot in advanced ? new[] { "2", "1", "none" } : new string[0])
            {
                string s = slot;
                string text;
                if (s == "none")
                {
                    py -= (int)(24 * Theme.Zoom) + 1;
                    Put(new WowButton("USE", delegate { UseNoAddonProfile(); }, 190, 24, 9), cx, py);
                    text = Lang.T(active == "none" ? "In use" : "Not in use");
                }
                else
                {
                    var prof = Json.AsDict(Json.Get(profiles, s));
                    py -= (int)(24 * Theme.Zoom) + 1;
                    Put(new WowButton("SAVE", delegate { SaveAddonProfile(s); }, 92, 24, 9), cx, py);
                    Put(new WowButton("LOAD", delegate { LoadAddonProfile(s); }, 92, 24, 9), cx + (int)(98 * Theme.Zoom), py);
                    text = active == s ? Lang.T("In use") :
                           Lang.T(prof == null ? "empty" : Addons.ProfileCount(prof, "enabled") + " on, " +
                                  Addons.ProfileCount(prof, "disabled") + " off, saved " + Json.Str(prof, "saved"));
                }
                var info = UI.Text(text, 9, FontStyle.Regular, active == s ? Theme.Gold : Theme.Text, Theme.Panel, (int)(190 * Theme.Zoom), false);
                info.Size = VStack.Measure(info);
                py -= info.Height + 1;
                Put(info, cx, py);
                // Profile 1 and 2 can carry a name of the player's own: click the name to type one (Enter keeps it, Esc
                // leaves it, an empty name goes back to "Profile 1")
                string custom = s == "none" ? "" : Json.Str(Cfg.Section("addon_profile_names"), s);
                var head = custom.Length > 0 ? UI.Text(custom, 11, FontStyle.Bold, Theme.Gold, Theme.Panel, 0, false)
                                             : UI.Text(s == "none" ? "No profile [Recommended]" : "Profile " + s, 11, FontStyle.Bold, Theme.Gold, Theme.Panel, 0);
                head.Size = VStack.Measure(head);
                if (head.Width > (int)(160 * Theme.Zoom)) { head.MaximumSize=new Size((int)(160*Theme.Zoom),0); head.Size=VStack.Measure(head); }
                py -= head.Height;
                Put(head, cx, py);
                if (s != "none")
                {
                    head.Cursor = Cursors.IBeam;
                    var rt = new ThemedToolTip { InitialDelay = 300 };
                    rt.SetToolTip(head, Lang.T("Click to give this profile a name of your own."));
                    head.Disposed += delegate { rt.Dispose(); };
                    Label hl = head;
                    float profileZoom=Theme.Zoom;
                    head.Click += delegate
                    {
                        var box = new TextBox();
                        box.Font = hl.Font;
                        box.MaxLength = 24;
                        box.Text = custom;
                        box.BackColor = Theme.Ink;
                        box.ForeColor = Theme.Gold;
                        box.BorderStyle = BorderStyle.FixedSingle;
                        box.Bounds = new Rectangle(hl.Left, hl.Top - 2, (int)(160 * profileZoom), hl.Height + 4);
                        hl.Parent.Controls.Add(box);
                        box.BringToFront();
                        box.Focus();
                        bool done = false;
                        Action<bool> finish = delegate(bool keep)
                        {
                            if (done) return;
                            done = true;
                            if (keep)
                            {
                                string n = box.Text.Trim();
                                if (n.Length == 0) Cfg.Section("addon_profile_names").Remove(s);
                                else Cfg.Section("addon_profile_names")[s] = n;
                                Cfg.Save();
                            }
                            BeginInvoke((Action)delegate { BuildPanel("Addons"); });
                        };
                        box.KeyDown += delegate(object o, KeyEventArgs e)
                        {
                            if (e.KeyCode == Keys.Enter) { e.SuppressKeyPress = true; finish(true); }
                            else if (e.KeyCode == Keys.Escape) { e.SuppressKeyPress = true; finish(false); }
                        };
                        box.LostFocus += delegate { finish(true); };
                    };
                }
                // a small "?" beside the name: point at it to read what this choice does with the game's preferences
                var q = UI.Text("?", 10, FontStyle.Bold, Theme.Panel, Theme.Gold, 0, false);
                q.AutoSize = false;
                q.TextAlign = ContentAlignment.MiddleCenter;
                int qs = (int)(16 * Theme.Zoom);
                q.Size = new Size(qs, qs);
                q.Cursor = Cursors.Help;
                Put(q, cx + head.Width + 4, py + (head.Height - qs) / 2);
                string note = s == "none" ? NoProfileNote : s == "1" ? Profile1Note : Profile2Note;
                var tt = new ThemedToolTip { AutoPopDelay = 30000, InitialDelay = 150, ReshowDelay = 100 };
                tt.SetToolTip(q, Lang.T(note));
                q.Disposed += delegate { tt.Dispose(); };
                py -= 3;
            }
            }
            finally { putInto=previousPut; cx=columnX; top=columnTop; bottom=columnBottom; }
            }
            // --- left: scrollable list with one switch per addon ---
            Color listbg = Theme.Ink;
            var sp = MakeScroll(panel, new Rectangle(20, top, cx - 14 - 20, bottom - top), listbg, Theme.Bronze);
            addonRows = new Dictionary<string, CheckBox>();
            if (addons.Count == 0)
            {
                var l = UI.Text("No addons installed.", 11, FontStyle.Italic, Theme.GoldDim, listbg, 0);
                l.Location = new Point(10, 8);
                sp.Controls.Add(l);
            }
            int w = sp.ClientSize.Width - 4 - 12;
            int y = 2;
            var updateAll = new WowButton("UPDATE ALL", delegate { CheckAddonUpdates(null, true); }, 105, 24, 8);
            updateAll.Location = new Point(Math.Max(6, sp.ClientSize.Width - updateAll.Width - 10), 2);
            updateAll.Grayed = addonDlBusy || autoAddonsBusy || mirrorAsking;
            sp.Controls.Add(updateAll);
            AskMirrorDates();
            // grouped like Download More: AZS addons, WoW 1.12 addons, My Addons, then any other folder
            var cats = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var e in Data.Addons)
                foreach (string f in AddonFoldersOf(e.Name)) if (!cats.ContainsKey(f)) cats[f] = e.Category;
            // every addon of Download More that is not a WoW 1.12 one is an AZS addon here (UI, Gameplay and Misc together)
            Func<string, string> catOf = delegate(string folder)
            {
                string c;
                if (cats.TryGetValue(folder, out c)) return c == Data.Wow112Category || c == Data.MyAddonsCategory ? c : Data.AzsAddonsHeading;
                return Data.AzsAddonsHeading;                // any other folder too (for now: no OTHER ADDONS heading)
            };
            // the pairs that can't be on together and are both on: at the top, joined by a red bracket, until one is off
            var pairs = new List<KeyValuePair<string, string>>();
            var onNow = new Dictionary<string, bool>();
            foreach (var a in addons) onNow[a.Key] = a.Value;
            foreach (var a in addons)
                if (a.Value)
                    foreach (var pt in Addons.PartnersOf(dir, a.Key, addons))
                        if (Addons.IsPreferred(dir, a.Key, pt.Value) && onNow.ContainsKey(pt.Key) && onNow[pt.Key])
                            pairs.Add(new KeyValuePair<string, string>(a.Key, pt.Key));
            conflicted = new HashSet<string>();
            foreach (var pr in pairs) { conflicted.Add(pr.Key); conflicted.Add(pr.Value); }
            const string ConflictHeading = "CONFLICT: PICK ONE";
            Func<string, string> plainCat = catOf;
            catOf = delegate(string folder) { return conflicted.Contains(folder) ? ConflictHeading : plainCat(folder); };
            var order = new List<string> { ConflictHeading, Data.AzsAddonsHeading, Data.Wow112Category };
            order.Add(Data.MyAddonsCategory);
            order.Add("OTHER ADDONS");
            // one row per addon: a folder that comes with another (Survivor Plates, !BugGrabber) is part of its row;
            // the rows go by the addon's own name ("BugSack By ..."), not by folder
            addons.RemoveAll(delegate(KeyValuePair<string, bool> p) { return Addons.IsCompanion(dir, p.Key); });
            var shownName = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var a in addons) shownName[a.Key] = Addons.DisplayName(dir, a.Key);
            // the folders of rolled-back addons (shown with [Outdated])
            var pinnedFolders = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var kv in AddonPins())
                foreach (string f in AddonFoldersOf(kv.Key))
                {
                    pinnedFolders.Add(f);
                    pinnedFolders.Add(f.EndsWith(Addons.DisabledSuffix) ? f.Substring(0, f.Length - Addons.DisabledSuffix.Length) : f + Addons.DisabledSuffix);
                }
            Func<string, string> sortKey = delegate(string f) { return System.Text.RegularExpressions.Regex.Replace(shownName[f], "^[^A-Za-z0-9]+", ""); };
            addons.Sort(delegate(KeyValuePair<string, bool> p1, KeyValuePair<string, bool> p2)
            {
                int c1 = order.IndexOf(catOf(p1.Key)), c2 = order.IndexOf(catOf(p2.Key));
                if (c1 != c2) return c1.CompareTo(c2);
                return string.Compare(sortKey(p1.Key), sortKey(p2.Key), StringComparison.OrdinalIgnoreCase);
            });
            int linkW = pairs.Count > 0 ? 20 * pairs.Count : 0;
            var rowBox = new Dictionary<string, Rectangle>();
            string lastCat = null;
            foreach (var a in addons)
            {
                string name = a.Key;
                string cat = catOf(name);
                if (cat != lastCat)
                {
                    lastCat = cat;
                    var hd = UI.Text(cat, 9, FontStyle.Bold, cat == ConflictHeading ? Theme.Hex("#e0503a") : Theme.Gold, listbg, 0);
                    hd.Size = VStack.Measure(hd);
                    hd.Location = new Point(6, y + (y > 2 ? 8 : 2));
                    sp.Controls.Add(hd);
                    var rule = new Panel();                  // a thin gold line under the heading
                    rule.BackColor = Theme.GoldDim;
                    rule.Bounds = new Rectangle(6, hd.Bottom + 1, w - 4, 1);
                    sp.Controls.Add(rule);
                    y = Math.Max(rule.Bottom + 4, updateAll.Bottom + Theme.Px(3));
                }
                string label = shownName[name];
                string installedTag = InstalledVersionLabel(name);
                if (installedTag.Length > 0) label += "  " + installedTag;
                if (pinnedFolders.Contains(name)) label += "  " + Lang.T("[Outdated]");   // rolled back on purpose (ROLLBACK)
                var cb = UI.Check(label, a.Value, a.Value ? Theme.Text : Theme.GoldDim, listbg, 11, false);
                cb.AutoSize = false;
                int rowWidth=Math.Max(30,w-Theme.Px(108)-linkW);
                int checkboxTextWidth=Math.Max(1,rowWidth-(cb.Font.Height-2)-8);
                Size need=TextRenderer.MeasureText(label,cb.Font,new Size(checkboxTextWidth,int.MaxValue),TextFormatFlags.WordBreak|TextFormatFlags.NoPrefix);
                cb.Bounds=new Rectangle(6,y,rowWidth,Math.Max(22,need.Height+4));
                rowBox[name] = cb.Bounds;
                cb.CheckedChanged += delegate { OnAddonToggle(name); };
                sp.Controls.Add(cb);
                // its folder out of the AddOns folder (with the settings it saved)
                var del = new WowButton("X", delegate { UninstallFolder(name); }, 26, 22, 9);
                del.Location = new Point(6 + w - 30, y + (cb.Height - del.Height) / 2);
                sp.Controls.Add(del);
                AddonEntry updateEntry = CatalogAddonForFolder(name);
                var update = new WowButton("UPDATE", delegate { CheckAddonUpdates(updateEntry, true); }, 66, 22, 8);
                update.Location = new Point(del.Left - update.Width - Theme.Px(4), del.Top);
                update.Grayed = updateEntry == null || updateEntry.WorkInProgress || addonDlBusy || autoAddonsBusy || mirrorAsking;
                update.GrayedTip = updateEntry == null ? "No managed release source is known for this addon." : "Updates are unavailable while another operation is running or the addon is work in progress.";
                update.Tip = "Check this addon's release source and install an available update.";
                sp.Controls.Add(update);
                addonRows[name] = cb;
                y += cb.Height + 1;
            }
            for (int i = 0; i < pairs.Count; i++)
            {
                Rectangle r1 = rowBox[pairs[i].Key], r2 = rowBox[pairs[i].Value];
                var link = new ConflictLink();
                int y1 = Math.Min(r1.Top + r1.Height / 2, r2.Top + r2.Height / 2), y2 = Math.Max(r1.Top + r1.Height / 2, r2.Top + r2.Height / 2);
                link.BackColor = listbg;
                int need = link.WordHeight(), mid = (y1 + y2) / 2;
                int lt = Math.Max(0, Math.Min(y1 - 1, mid - need / 2)), lb = Math.Max(y2 + 2, mid + need / 2);
                link.Bounds = new Rectangle(6 + w - Theme.Px(108) - linkW + i * 20, lt, 20, lb - lt);
                link.Tick1 = y1 - lt;
                link.Tick2 = y2 - lt;
                WowButton.Tips.SetToolTip(link, pairs[i].Key + " / " + pairs[i].Value + ": " +
                                          Lang.T("these two can't be on together: untick the one you don't want."));
                sp.Controls.Add(link);
                link.BringToFront();
            }
            UpdateAddonCount();
        }

        bool settingCheck;
        HashSet<string> conflicted;                         // My Addons: the addons shown first, in a conflict

        void UpdateAddonCount()
        {
            if (addonRows == null || addonCount == null || addonCount.IsDisposed) return;
            int on = 0;
            foreach (var cb in addonRows.Values) if (cb.Checked) on++;
            addonCount.Text = Lang.T(on + " of " + addonRows.Count + " enabled");
        }

        void OnAddonToggle(string name)
        {
            if (settingCheck) return;
            CheckBox cb = addonRows[name];
            bool want = cb.Checked;
            if (AddonsLocked())
            {
                settingCheck = true;
                cb.Checked = !want;
                settingCheck = false;
                return;
            }
            if (want)
            {
                // an addon it can't be on with is on: ask before switching that one off
                var on = new List<string>();
                string why = "";
                try
                {
                    var installed = Addons.Scan(AddonsDir());
                    foreach (var pt in Addons.PartnersOf(AddonsDir(), name, installed))
                        foreach (var a in installed)
                            if (a.Key == pt.Key && a.Value && !on.Contains(a.Key)) { on.Add(a.Key); why = pt.Value.Why; }
                }
                catch (Exception) { }
                if (on.Count > 0)
                {
                    string others = string.Join(", ", on.ToArray());
                    if (!Ask("Addons", Lang.F("{0} can't be on together with {1}: {2}.", name, others, Lang.T(why)) + "\n\n" +
                                       Lang.F("Switch {0} off and turn {1} on?", others, name)))
                    {
                        settingCheck = true;
                        cb.Checked = false;
                        settingCheck = false;
                        return;
                    }
                }
            }
            try { Addons.SetEnabled(AddonsDir(), name, want); }
            catch (Exception e)
            {
                settingCheck = true;
                cb.Checked = !want;
                settingCheck = false;
                Msg.Show(side, "Could not " + (want ? "enable" : "disable") + " " + name + ":\n\n" + e.Message, "Addons",
                                MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }
            cb.ForeColor = want ? Theme.Text : Theme.GoldDim;
            UpdateAddonCount();
            RefreshGates();                                  // the Stats buttons follow the enabled stats add-on
            if (!want && conflicted != null && conflicted.Contains(name))
            {
                BuildPanel("Addons");                        // the conflict is settled: the rows go back in A to Z order
                SetStatus(name + " disabled.");
                return;
            }
            SetStatus(name + " " + (want ? "enabled" : "disabled") + ".");
            if (want)
            {
                // the addons it conflicts with go off
                List<string> off;
                try { off = Addons.SwitchOffPartners(AddonsDir(), name); }
                catch (Exception e) { SetStatus(e.Message); return; }
                if (off.Count > 0)
                {
                    BuildPanel("Addons");
                    SetStatus(name + " enabled. " + string.Join(", ", off.ToArray()) + " " +
                              Lang.T("switched off: the two conflict."));
                }
            }
        }

        void ToggleAllAddons()
        {
            if (AddonsLocked()) return;
            string d = AddonsDir();
            List<KeyValuePair<string, bool>> addons;
            try { addons = Addons.Scan(d); } catch (Exception) { return; }
            if (addons.Count == 0) return;
            bool allOn = true;
            foreach (var a in addons) if (!a.Value) allOn = false;
            bool target = !allOn;                            // all on -> switch all off, otherwise all on
            var errors = new List<string>();
            if (target)
            {
                // Toggle All still honors incompatibilities: turn candidates on in order,
                // switch their conflicting partner off, then keep each pair's preferred member.
                foreach (var a in addons)
                    if (!a.Value)
                        try
                        {
                            Addons.SetEnabled(d, a.Key, true);
                            Addons.SwitchOffPartners(d, a.Key);
                        }
                        catch (Exception e) { errors.Add(a.Key + ": " + e.Message); }
                try { Addons.EnforceConflicts(d); }
                catch (Exception e) { errors.Add("Conflicting addons: " + e.Message); }
            }
            else
            {
                foreach (var a in addons)
                    if (a.Value)
                        try { Addons.SetEnabled(d, a.Key, false); }
                        catch (Exception e) { errors.Add(a.Key + ": " + e.Message); }
            }
            SetStatus("All addons " + (target ? "enabled." : "disabled."));
            BuildPanel("Addons");
            if (errors.Count > 0)
                Msg.Show(side, "Some addons could not be changed:\n\n" + string.Join("\n", errors), "Addons",
                                MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }

        const string Profile1Note = "Profile 1 keeps a full copy of itself in its own isolated space, apart from Profile 2 and No profile: which addons are on, where the interface sits and every addon's settings. A heavy UI addon can change things here without touching the others, and when you come back to Profile 1 it is exactly the way you left it.";
        const string Profile2Note = "Profile 2 keeps a full copy of itself in its own isolated space, apart from Profile 1 and No profile: which addons are on, where the interface sits and every addon's settings. A heavy UI addon can change things here without touching the others, and when you come back to Profile 2 it is exactly the way you left it.";
        const string NoProfileNote = "No profile is the game as it normally is, kept as a full copy in its own isolated space: which addons are on, where the interface sits and every addon's settings. While you use it the launcher changes nothing, and when you come back to it from a profile it is exactly the way you left it. If you don't want the game's preferences changed, use this setting.";

        // which addon profile's game preferences are in place: "1", "2" or "none" (No profile, the default)
        string ActiveAddonProfile()
        {
            string a = Cfg.Str("addon_profile_active");
            return a == "1" || a == "2" ? a : "none";
        }

        // the game's preferences move to another profile: the current ones are stored under the profile being left,
        // then the picked profile's own come back (first time: it starts from the current ones). False on a failure.
        // Each of the three (No profile, Profile 1, Profile 2) is a complete state of its own: which addons are on or off,
        // and the interface (where its frames sit, chat and every addon's saved settings; not graphics or key bindings,
        // AddonProfiles.cs). Leaving one stores both as they are now under it; the one picked gets its own back.
        // A choice picked for the first time starts from the current state. So going back and forth between any of the
        // three always lands where that one was left.
        bool SwitchAddonPreferences(string to)
        {
            string from = ActiveAddonProfile();
            if (from == to) return true;
            try
            {
                var ss = SyncSettings();
                var profiles = Cfg.Section("addon_profiles");
                var now = Addons.MakeProfile(Addons.Scan(AddonsDir()));   // the switches as they are, kept under the one left
                profiles[from] = now;
                AddonProfiles.Store(ss, from);
                if (AddonProfiles.Has(to)) AddonProfiles.Restore(ss, to);
            }
            catch (Exception e)
            {
                Msg.Show(side, e.Message, "Addons");
                return false;
            }
            Cfg.Set("addon_profile_active", to);
            Cfg.Save();
            return true;
        }

        // the picked one's own switches, where it has some (unknown, newer addons are left as they are)
        int ApplyAddonSwitches(string slot, List<string> errors, List<string> unknown)
        {
            var prof = Json.AsDict(Json.Get(Cfg.Section("addon_profiles"), slot));
            return prof == null ? 0 : Addons.ApplyProfile(AddonsDir(), prof, errors, unknown);
        }

        void UseNoAddonProfile()
        {
            if (AddonsLocked()) return;
            if (ActiveAddonProfile() == "none") { SetStatus(Lang.T("No profile in use: the game's preferences are shared again.")); return; }
            if (!SwitchAddonPreferences("none")) return;
            var errors = new List<string>();
            try { ApplyAddonSwitches("none", errors, new List<string>()); }
            catch (Exception e) { errors.Add(e.Message); }
            SetStatus(Lang.T("No profile in use: the game's preferences are shared again."));
            BuildPanel("Addons");
            if (errors.Count > 0)
                Msg.Show(side, "Some addons could not be changed:\n\n" + string.Join("\n", errors), "Addons",
                                MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }

        void SaveAddonProfile(string slot)
        {
            var profiles = Cfg.Section("addon_profiles");
            if (profiles.ContainsKey(slot) && Msg.Show(side, "Replace the saved Profile " + slot + " with the current toggles?",
                                                              "Addons", MessageBoxButtons.YesNo) != DialogResult.Yes) return;
            try { profiles[slot] = Addons.MakeProfile(Addons.Scan(AddonsDir())); }
            catch (Exception e)
            {
                Msg.Show(side, e.Message, "Addons");
                return;
            }
            Cfg.Save();
            SetStatus("Profile " + slot + " saved.");
            BuildPanel("Addons");
        }

        void LoadAddonProfile(string slot)
        {
            if (AddonsLocked()) return;
            if (!SwitchAddonPreferences(slot)) return;      // this profile's own game preferences first, then its switches
            var errors = new List<string>();
            var unknown = new List<string>();
            int changed;
            try { changed = ApplyAddonSwitches(slot, errors, unknown); }
            catch (Exception e)
            {
                Msg.Show(side, e.Message, "Addons");
                return;
            }
            string note = "Profile " + slot + " loaded: " + changed + " changed.";
            if (unknown.Count > 0) note += " " + unknown.Count + " newer addon(s) left as they were.";
            SetStatus(note);
            BuildPanel("Addons");
            if (errors.Count > 0)
                Msg.Show(side, "Some addons could not be changed:\n\n" + string.Join("\n", errors), "Addons",
                                MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }

        // ------------------------------------------------------------------ Cloud Save
        // The last 5 cloud downloads are kept as ZIPs on this PC. Nothing uses them on its own: this menu is the only
        // way back to one, for when the save is broken and the cloud copy cannot fix it.
        void ShowKeptDownloads(Control anchor)
        {
            List<SyncEngine.KeptDownload> kept;
            try { kept = new SyncEngine(SyncSettings()).KeptDownloads(); }
            catch (Exception) { kept = new List<SyncEngine.KeptDownload>(); }
            var menu = ThemeMenus.Create(false);
            var head = new ToolStripMenuItem(Lang.T("Restore a download kept on this PC (last resort):"));
            head.Enabled = false;
            menu.Items.Add(head);
            if (kept.Count == 0)
            {
                var none = new ToolStripMenuItem(Lang.T("No downloads kept yet."));
                none.Enabled = false;
                menu.Items.Add(none);
            }
            foreach (var k in kept)
            {
                var areas = new List<string>();
                foreach (var a in k.Areas) areas.Add(Lang.T(a.Key.Label));
                string stamp = k.Stamp;
                menu.Items.Add(new ToolStripMenuItem(k.Time.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture) + "   (" +
                                                     string.Join(", ", areas) + ")", null,
                                                     delegate { RunSync("restore", false, false, stamp); }));
            }
            if (anchor != null && !anchor.IsDisposed) menu.Show(anchor, new Point(0, anchor.Height));
            else menu.Show(Cursor.Position);
        }

        void PanelCloud()
        {
            ClearPanel("Cloud Save");
            SetCrumbs("Options", (Action)delegate { optionsTab = 1; optionsLang = false; ShowSection("Options"); }, "Cloud Save", null);
            if (!CloudEnabled())
            {
                var t = PutText("Cloud saving is turned off.\nYour progress stays on this PC only.", 12, FontStyle.Regular,
                                Theme.Text, 20, 8, 0);
                Put(new WowButton("TURN ON CLOUD SAVING", delegate { SetCloudEnabled(true); }, 290, 38, 11), 20, BottomOf(t) + 12);
                return;
            }
            var intro = PutText("PLAY downloads the newest state, starts the game and uploads your progress after you close it. " +
                                "One complete state wins; the overwritten one is backed up as a ZIP first.", 10, FontStyle.Regular,
                                Theme.Text, 20, 8, Math.Min(570, PW - 40));
            int y = BottomOf(intro) + 8;
            var lab = PutText("Sync folder", 11, FontStyle.Bold, Theme.Gold, 20, y, 0);
            string cloud = Cfg.Str("cloud_dir");
            var path = PutText(cloud.Length > 0 ? cloud : Lang.T("(not set - choose a folder inside Google Drive, OneDrive or Dropbox)"),
                               9, FontStyle.Regular, Theme.LinkBlue, 20, BottomOf(lab), 420, false);
            int rowH = BottomOf(path) - y;
            Put(new WowButton("CHOOSE", ChooseCloudDir, 100, 30, 10), PW - 20 - 100, y + Math.Max(0, (rowH - 30) / 2));
            string info = "Not synced yet.";
            try { if (cloud.Length > 0 && Directory.Exists(cloud)) info = "Cloud: " + new SyncEngine(SyncSettings()).CloudInfo(); }
            catch (Exception) { }
            var infoL = PutText(info, 9, FontStyle.Italic, Theme.GoldDim, 20, Math.Max(BottomOf(path), y + 30) + 2, PW - 20 - 170 - 30);
            // last resort: put back one of the last 5 downloads kept on this PC (small, out of the way)
            WowButton keptBtn = null;
            keptBtn = new WowButton("EARLIER DOWNLOADS", delegate { ShowKeptDownloads(keptBtn); }, 170, 24, 8);
            Put(keptBtn, PW - 20 - 170, infoL.Top - 3);
            y = Math.Max(BottomOf(infoL), infoL.Top + 21) + 4;
            Put(new WowButton("CHECK STATUS", delegate { RunSync("status", false, false); }, 170, 34, 10), 20, y);
            Put(new WowButton("DOWNLOAD", delegate { RunSync("pull", true, false); }, 170, 34, 10), 20 + 182, y);
            Put(new WowButton("UPLOAD", delegate { RunSync("push", true, false); }, 170, 34, 10), 20 + 364, y);
            y += 34 + 8;
            var frame = new Bordered(Theme.Bronze, 1);
            frame.Bounds = new Rectangle(20, y, PW - 40, PH - 12 - y);
            Put(frame, 20, y);
            var box = UI.Log(Theme.Ink, 9);
            box.Dock = DockStyle.Fill;
            frame.Controls.Add(box);
            lock (cloudLog)
            {
                int from = Math.Max(0, cloudLog.Count - 200);
                for (int i = from; i < cloudLog.Count; i++) UI.Append(box, cloudLog[i].Key + "\n", UI.LevelColor(cloudLog[i].Value), null);
            }
            box.SelectionStart = box.TextLength;
            box.ScrollToCaret();
            logBox = box;
        }

        // ------------------------------------------------------------------ Options
        // Options: a bar of tabs across the top (beside the back arrow) and one short page per tab
        // FOLDERS, CLOUD SAVE (its settings and its page), GRAPHICS and GAMEPLAY (the game's own settings), LAUNCHER (how
        // the launcher behaves, its language, About, setup)
        static readonly string[] OptionTabs = { "FOLDERS", "CLOUD SAVE", "GRAPHICS", "GAMEPLAY", "LAUNCHER" };
        int optionsTab;
        bool optionsLang;                                     // Options › Launcher › Language: the list of languages
        bool optionsAbout;                                    // Options › Launcher › About Launcher: the launcher's overview

        ScrollPanel optionsPage;                             // the page shown, so a rebuild of the same tab keeps its place
        int optionsPageTab = -1;

        void PanelOptions()
        {
            // a setting changed on this tab rebuilds it: it stays where it was scrolled to
            int keep = optionsPage != null && !optionsPage.IsDisposed && optionsPageTab == optionsTab ? -optionsPage.AutoScrollPosition.Y : 0;
            ClearPanel("Options");
            // no row of tabs: the tab is a step of the path at the top (Options › Folders ▾), its list the other tabs
            int top0 = 4;
            Action home = delegate { optionsLang = false; optionsAbout = false; optionsTab = 0; BuildPanel("Options"); };
            if (optionsAbout)
            {
                SetCrumbs("Options", home, "Launcher", (Action)delegate { optionsAbout = false; optionsLang = false; optionsTab = 4; BuildPanel("Options"); }, "About Launcher", null);
                SetCrumbMenu(1, OptionTabMenu());
            }
            else if (optionsLang)                                 // Options › Launcher › Language ▾ : the languages themselves
            {
                // (no list of languages here: a language is only picked on the page itself, never by passing over the path)
                SetCrumbs("Options", home, "Launcher", (Action)delegate { optionsLang = false; optionsTab = 4; BuildPanel("Options"); }, "Language", null);
                SetCrumbMenu(1, OptionTabMenu());
            }
            else
            {
                SetCrumbs("Options", home, OptionTabName(optionsTab), null);
                SetCrumbMenu(1, OptionTabMenu());
            }
            var sp = MakeScroll(panel, new Rectangle(8, top0, PW - 16, PH - 8 - top0), Theme.Panel, Theme.Panel);
            int pageW = sp.ClientSize.Width - 6;              // the page's width: what shows inside the scroll box, beside its bar
            putInto = sp;
            try
            {
                int end;
                if (optionsAbout) end = OptionsAbout(pageW);
                else if (optionsLang) end = OptionsLanguage(pageW);
                else if (optionsTab == 0) end = OptionsFolders(pageW);
                else if (optionsTab == 1) end = OptionsCloud(pageW);
                else if (optionsTab == 2) end = OptionsGame(pageW, new[] { "DISPLAY", "GRAPHICS" });
                else if (optionsTab == 3) end = OptionsGame(pageW, new[] { "GAMEPLAY", "SOUND" });
                else end = OptionsLauncher(pageW);
                var pad = new Panel();
                pad.BackColor = Theme.Panel;
                pad.Bounds = new Rectangle(0, end, 1, 12);
                sp.Controls.Add(pad);
            }
            finally { putInto = null; }
            optionsPage = sp;
            optionsPageTab = optionsTab;
            if (keep > 0) sp.AutoScrollPosition = new Point(0, keep);
        }

        static string OptionTabName(int i)                 // "CLOUD SAVE" -> "Cloud Save", for the trail
        {
            string[] names = { "Folders", "Cloud Save", "Graphics", "Gameplay", "Launcher" };
            return i >= 0 && i < names.Length ? names[i] : "Options";
        }

        List<KeyValuePair<string, Action>> OptionTabMenu()
        {
            var l = new List<KeyValuePair<string, Action>>();
            for (int i = 0; i < OptionTabs.Length; i++)
            {
                int n = i;
                l.Add(new KeyValuePair<string, Action>(OptionTabName(i), delegate { optionsTab = n; optionsLang = false; BuildPanel("Options"); }));
            }
            return l;
        }

        int OptionCheck(string text, bool value, int y, Action<bool> changed)
        {
            var cb = UI.Check(text, value, Theme.Text, Theme.Panel, 11);
            cb.CheckedChanged += delegate { bool on = cb.Checked; BeginInvoke(new Action(delegate { changed(on); })); };
            cb.Size = VStack.Measure(cb);
            Put(cb, 18, y);
            return y + cb.Height + 4;
        }

        // FOLDERS: where the game, its saves, its config and the cloud copy are, and cloud saving on or off
        int OptionsFolders(int PW)
        {
            SyncSettings ss = SyncSettings();
            var rows = new List<object[]>();
            rows.Add(new object[] { "Game folder", GameDir(), new Action(ChooseGameFolderButton) });
            rows.Add(new object[] { "Save game folder", ss.SaveDir, new Action(ChooseSaveDir) });
            rows.Add(new object[] { "Game config folder (AddOns, addon settings)", ss.ConfigDir, new Action(ChooseConfigDir) });
            int y = 6;
            // each folder: OPEN LOCATION (in Explorer) and CHANGE FOLDER beside it; IMPORT A SAVE under them
            int bw = (int)(140 * Theme.Zoom), gap = (int)(8 * Theme.Zoom), room = PW - 40 - 2 * bw - gap - 10;
            foreach (var r in rows)
            {
                var lab = PutText((string)r[0], 11, FontStyle.Bold, Theme.Gold, 20, y, room);
                string v = (string)r[1];
                var val = PutText(string.IsNullOrEmpty(v) ? Lang.T("(not set)") : v, 9, FontStyle.Regular, Theme.LinkBlue, 20, BottomOf(lab), room, false);
                Action act = (Action)r[2];
                int by = y + Math.Max(0, (BottomOf(val) - y - (int)(28 * Theme.Zoom)) / 2);
                Put(new WowButton("CHANGE FOLDER", act, 140, 28, 8.5f), PW - 20 - bw, by);
                var open = new WowButton("OPEN LOCATION", delegate { OpenLocation(v); }, 140, 28, 8.5f);
                open.GrayedTip = "That folder does not exist.";
                open.Grayed = string.IsNullOrEmpty(v) || !Directory.Exists(v);
                Put(open, PW - 20 - 2 * bw - gap, by);
                y = Math.Max(BottomOf(val), by + (int)(28 * Theme.Zoom)) + 10;
            }
            var imp = PutText("Import a save", 11, FontStyle.Bold, Theme.Gold, 20, y + 4, PW - 40 - bw - 10);
            var impNote = PutText("Bring in a save from elsewhere (a backup, another PC) into the save game folder.", 9, FontStyle.Italic,
                                  Theme.GoldDim, 20, BottomOf(imp), PW - 40 - bw - 10);
            Put(new WowButton("IMPORT A SAVE", delegate { if (ImportSave(side, SyncSettings().SaveDir) != null) SetStatus("Save imported."); }, 140, 28, 8.5f),
                PW - 20 - bw, y + 4);
            y = Math.Max(BottomOf(impNote), y + 4 + (int)(28 * Theme.Zoom)) + 8;
            var exp = PutText("Export a save", 11, FontStyle.Bold, Theme.Gold, 20, y + 4, PW - 40 - bw - 10);
            var expNote = PutText("Save a copy to move to another PC or import on the website.", 9, FontStyle.Italic,
                                  Theme.GoldDim, 20, BottomOf(exp), PW - 40 - bw - 10);
            Put(new WowButton("EXPORT SAVE", delegate { ExportSave(side, SyncSettings().SaveDir); }, 140, 28, 8.5f),
                PW - 20 - bw, y + 4);
            return Math.Max(BottomOf(expNote), y + 4 + (int)(28 * Theme.Zoom)) + 8;
        }

        // a folder in Explorer
        void OpenLocation(string dir)
        {
            if (string.IsNullOrEmpty(dir) || !Directory.Exists(dir)) { SetStatus("That folder does not exist."); return; }
            try { System.Diagnostics.Process.Start("explorer.exe", "\"" + dir + "\""); }
            catch (Exception) { SetStatus("That folder does not exist."); }
        }

        // CLOUD SAVE: cloud saving on or off, its sync folder, and its page (sync now, the log, earlier downloads)
        int OptionsCloud(int PW)
        {
            int y = 6;
            y = OptionCheck("Cloud saving", CloudEnabled(), y, delegate(bool on) { SetCloudEnabled(on); });
            if (Handheld.Experimental) y = OptionsCloudServices(PW, y + 6);   // Steam Deck experimental: Google Drive, Dropbox
            else if (GoogleDrivePaused())
            {
                var paused = PutText("Cloud Save is set to " + CloudServiceName() + ", which needs STEAM DECK EXPERIMENTAL (Options > Launcher). " +
                                     "Turn it on again, or choose a sync folder below.", 9.5f, FontStyle.Italic, Theme.GoldDim, 20, y + 2, PW - 40);
                y = BottomOf(paused) + 8;
            }
            if (!CloudEnabled())
            {
                var off = PutText("Turn cloud saving on to keep your save and your addons' settings in Google Drive, OneDrive or Dropbox.",
                                  9.5f, FontStyle.Italic, Theme.GoldDim, 20, y + 2, PW - 40);
                return BottomOf(off);
            }
            y += 6;
            int cbw = (int)(140 * Theme.Zoom), cgap = (int)(8 * Theme.Zoom);
            var lab = PutText(UsingGoogleDrive() || UsingDropbox() ? CloudServiceName() + "'s copy on this device" : "Sync folder (inside your cloud drive)", 11, FontStyle.Bold, Theme.Gold, 20, y, PW - 50 - 2 * cbw - cgap);
            string v = Cfg.Str("cloud_dir");
            var val = PutText(string.IsNullOrEmpty(v) ? Lang.T("(not set)") : v, 9, FontStyle.Regular, Theme.LinkBlue, 20, BottomOf(lab), PW - 50 - 2 * cbw - cgap, false);
            int cby = y + Math.Max(0, (BottomOf(val) - y - 28) / 2);
            Put(new WowButton("CHANGE FOLDER", ChooseCloudDir, 140, 28, 8.5f), PW - 20 - cbw, cby);
            var openCloud = new WowButton("OPEN LOCATION", delegate { OpenLocation(v); }, 140, 28, 8.5f);
            openCloud.GrayedTip = "That folder does not exist.";
            openCloud.Grayed = string.IsNullOrEmpty(v) || !Directory.Exists(v);
            Put(openCloud, PW - 20 - 2 * cbw - cgap, cby);
            y = Math.Max(BottomOf(val), y + 30) + 6;
            if (!GateOpen())                                  // syncing needs the game installed
            {
                var need = PutText(CloudNeedsGame, 9.5f, FontStyle.Italic, Theme.GoldDim, 20, y, PW - 40);
                return BottomOf(need) + 6;
            }
            // what was the Cloud Save page: the state in the cloud, sync now, earlier downloads and the log
            var intro = PutText("PLAY downloads the newest state, starts the game and uploads your progress after you close it. " +
                                "One complete state wins; the overwritten one is backed up as a ZIP first.", 9.5f, FontStyle.Regular,
                                Theme.Text, 20, y, PW - 40);
            y = BottomOf(intro) + 6;
            string info = "Not synced yet.";
            try { if (v.Length > 0 && Directory.Exists(v)) info = "Cloud: " + new SyncEngine(SyncSettings()).CloudInfo(); }
            catch (Exception) { }
            var infoL = PutText(info, 9, FontStyle.Italic, Theme.GoldDim, 20, y + 4, PW - 20 - 170 - 30);
            WowButton keptBtn = null;
            keptBtn = new WowButton("EARLIER DOWNLOADS", delegate { ShowKeptDownloads(keptBtn); }, 170, 24, 8);
            Put(keptBtn, PW - 20 - (int)(170 * Theme.Zoom), y);
            y = Math.Max(BottomOf(infoL), y + (int)(24 * Theme.Zoom)) + 8;
            int bw = (int)(170 * Theme.Zoom) + 12;
            Put(new WowButton("CHECK STATUS", delegate { RunSync("status", false, false); }, 170, 34, 10), 20, y);
            Put(new WowButton("DOWNLOAD", delegate { RunSync("pull", true, false); }, 170, 34, 10), 20 + bw, y);
            Put(new WowButton("UPLOAD", delegate { RunSync("push", true, false); }, 170, 34, 10), 20 + 2 * bw, y);
            y += (int)(34 * Theme.Zoom) + 8;
            var frame = new Bordered(Theme.Bronze, 1);
            // the log fills what is left of the page, so the Cloud Save tab needs no scroll bar
            int logH = Math.Max(70, PH - 12 - y - 22);
            frame.Bounds = new Rectangle(20, y, PW - 40, logH);
            Put(frame, 20, y);
            var box = UI.Log(Theme.Ink, 9);
            box.Dock = DockStyle.Fill;
            frame.Controls.Add(box);
            lock (cloudLog)
            {
                int from = Math.Max(0, cloudLog.Count - 200);
                for (int i = from; i < cloudLog.Count; i++) UI.Append(box, cloudLog[i].Key + "\n", UI.LevelColor(cloudLog[i].Value), null);
            }
            box.SelectionStart = box.TextLength;
            box.ScrollToCaret();
            logBox = box;
            return frame.Bottom + 6;
        }

        // GAME: the game's own settings (display mode, window size, graphics, sound), written into its config.toml;
        // the game reads them when it starts
        int OptionsGame(int PW, string[] groups)
        {
            string file = GameSettings.FileIn(SyncSettings().ConfigDir);
            var now = GameSettings.Read(file);
            bool running = GameRunningFlag || Util.GameRunning();
            int y = 6;
            if (running)                                     // only while the game runs: its settings can't be changed then
            {
                var intro = PutText("The game is running: close it first, it saves its own settings when it closes.",
                                    9.5f, FontStyle.Italic, Theme.Hex("#e07050"), 20, y, PW - 40);
                y = BottomOf(intro) + 8;
            }
            if (file == null)
            {
                var none = PutText("The game config folder is not set. Set it in Options > FOLDERS.", 10, FontStyle.Regular, Theme.Text, 20, y, PW - 40);
                return BottomOf(none);
            }
            Action<GameSetting, string> set = delegate(GameSetting gs, string v)
            {
                if (GameRunningFlag || Util.GameRunning()) { SetStatus("Close the game first: it saves its own settings when it closes."); return; }
                try
                {
                    GameSettings.Set(file, gs.Cvar, v);
                    SetStatus(Lang.T("Game setting saved:") + " " + Lang.T(gs.Label));
                }
                catch (UserError e) { SetStatus(e.Message); }
                catch (Exception e) { SetStatus(Lang.T("Could not save the game setting:") + " " + e.Message); }
                BuildPanel("Options");
            };
            string group = null;
            foreach (GameSetting gs in GameSettings.All)
            {
                if (Array.IndexOf(groups, gs.Group) < 0) continue;   // GRAPHICS: display and graphics; GAMEPLAY: gameplay and sound
                string cur = GameSettings.Value(now, gs);
                GameSetting g = gs;
                if (gs.Group != group)                       // a heading for each group: DISPLAY, GRAPHICS, SOUND, GAMEPLAY
                {
                    group = gs.Group;
                    y += 6;
                    var gh = PutText(group, 13, FontStyle.Bold, Theme.Gold, 20, y, 0);
                    Put(UI.Line(Theme.Bronze, PW - 40, 2), 20, BottomOf(gh) + 2);
                    y = BottomOf(gh) + 10;
                }
                if (gs.Check)
                {
                    y = OptionCheck(gs.Label, cur == "1", y, delegate(bool on) { set(g, on ? "1" : "0"); });
                    if (gs.Note != null)
                    {
                        var cn = PutText(gs.Note, 9.5f, FontStyle.Regular, Theme.Text, 44, y - 4, PW - 64);
                        y = BottomOf(cn) + 6;
                    }
                    continue;
                }
                var lab = PutText(gs.Label, 11.5f, FontStyle.Bold, Theme.Hex("#fff2b0"), 20, y, PW - 40);
                y = BottomOf(lab) + 1;
                if (gs.Note != null)
                {
                    var note = PutText(gs.Note, 9.5f, FontStyle.Regular, Theme.Text, 20, y, PW - 40);
                    y = BottomOf(note) + 4;
                }
                var values = new List<string>(gs.Values);
                var names = new List<string>(gs.Names);
                int at = values.FindIndex(delegate(string v) { return SameSetting(v, cur); });
                if (at < 0 && !string.IsNullOrEmpty(cur)) { values.Add(cur); names.Add(cur.Replace("x", "×")); at = values.Count - 1; }
                int n = values.Count, gap = 6;
                int bw = Math.Min(170, (int)(((PW - 40) - gap * (n - 1)) / (float)n / Theme.Zoom));
                int x = 20;
                for (int i = 0; i < n; i++)
                {
                    string v = values[i];
                    int need;                                // a long name gets the room it needs
                    using (var mg = CreateGraphics()) need = (int)(mg.MeasureString(Lang.T(names[i]), Theme.F(9)).Width / Theme.Zoom) + 20;
                    if (x > 20 && x + (int)(Math.Max(bw, need) * Theme.Zoom) > PW - 20) { x = 20; y += (int)(30 * Theme.Zoom) + 6; }
                    var b = new WowButton(names[i], delegate { set(g, v); }, Math.Max(bw, need), 30, 9);
                    b.Selected = i == at;
                    Put(b, x, y);
                    x += b.Width + gap;
                }
                y += (int)(30 * Theme.Zoom) + 14;
            }
            var where = PutText(Lang.T("Saved in") + " " + file, 8.5f, FontStyle.Regular, Theme.LinkBlue, 20, y, PW - 40, false);
            return BottomOf(where);
        }

        static bool SameSetting(string a, string b)
        {
            if (a == null || b == null) return false;
            double x, y;
            if (double.TryParse(a, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out x) &&
                double.TryParse(b, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out y))
                return Math.Abs(x - y) < 0.001;
            return string.Equals(a.Trim(), b.Trim(), StringComparison.OrdinalIgnoreCase);
        }

        // Time tracking provided by the selected stats source.
        int OptionsPlayTime(int PW, int y)
        {
            var head = PutText("Play time", 11, FontStyle.Bold, Theme.Gold, 20, y, 0);
            RunStats data = AddonStats();
            if (data != null && data.Source == "Azeroth Stats")
            {
                string text = data.Total.Runs > 0
                    ? "Time in recorded runs: " + Duration((long)data.Total.Time) + " across " + Big(data.Total.Runs) + " runs. Azeroth Stats does not count time spent in menus."
                    : "Azeroth Stats records time spent in runs, not time spent in menus. Finish a run to begin recording it.";
                var label = PutText(text, 10, FontStyle.Regular, Theme.Text, 20, BottomOf(head) + 2, PW - 40);
                return BottomOf(label);
            }
            PlayStats ps = ShownPlayStats();
            long sessions = ps.Sessions;
            string stats = sessions == 0
                ? (Stats.AddonInstalled(AddonsDir()) ? "No session play time counted yet."
                                                      : "Install Azeroth Stats to record run and build information.")
                : "Total: " + Duration(ps.Seconds) + " over " + Util.Plural((int)sessions, "session") + "\n" +
                  "Session time retained from the legacy stats file; cloud saving carries addon settings between PCs.";
            var statsLabel = PutText(stats, 10, FontStyle.Regular, Theme.Text, 20, BottomOf(head) + 2, PW - 40);
            return BottomOf(statsLabel);
        }

        // LANGUAGE: every language built into the launcher, in its own name
        int OptionsLanguage(int PW)
        {
            var langs = Lang.Available();
            int y = 6, x = 20, col = 0, bw = 190, cols = Math.Max(1, (PW - 40) / (int)((bw + 10) * Theme.Zoom));
            foreach (var kv in langs)
            {
                string code = kv.Key;
                var b = new WowButton(kv.Value, delegate { SetLanguage(code); }, bw, 28, 9);
                b.Selected = code == Lang.Code;
                Put(b, x + col * (int)((bw + 10) * Theme.Zoom), y);
                if (++col >= cols) { col = 0; y += (int)(28 * Theme.Zoom) + 6; }
            }
            if (col > 0) y += (int)(28 * Theme.Zoom) + 6;
            return y;
        }

        // LAUNCHER: updating the launcher, About, setup again and installing the game from a file
        int OptionsLauncher(int PW)
        {
            int y = 6, bw = 200;
            Action<string, Action, string> row = delegate(string label, Action act, string note)
            {
                var b = Put(new WowButton(label, act, bw, 30, 9), 20, y);
                var t = PutText(note, 9.5f, FontStyle.Italic, Theme.GoldDim, 20 + b.Width + 12, y + 2, PW - 40 - b.Width - 12);
                y = Math.Max(BottomOf(b), BottomOf(t)) + 8;
            };
            // Closing the launcher exits it (no tray) and it stays in its window while the game runs. With STEAM DECK
            // EXPERIMENTAL on, a Steam Deck / Handheld steps aside and comes back instead (LauncherHandheld.cs).
            y = OptionCheck("Show the Stats section (run records and available play time)", StatsEnabled(), y,
                            delegate(bool on) { SetStatsEnabled(on); });
            y = OptionCheck("Show play time on the home screen", Cfg.Bool("home_playtime", true), y,
                            delegate(bool on) { Cfg.Set("home_playtime", on); Cfg.Save(); Invalidate(); });
            // STEAM DECK EXPERIMENTAL: the Steam Deck support, opt-in; its two choices show while it is on
            var deckButton = Put(new WowButton("STEAM DECK EXPERIMENTAL", ToggleSteamDeckExperimental, 240, 30, 9), 20, y);
            deckButton.Selected = Handheld.Experimental;
            var deckNote = PutText(Handheld.Experimental
                    ? "On. In Steam's Game Mode the launcher fills the screen and its lists open inside it; it steps aside while you play. Press again to turn it off."
                    : "Off. Turns on Steam Deck support: full screen in Game Mode, one Steam entry for launcher and game, and the launcher stepping aside while you play.",
                9.5f, FontStyle.Italic, Theme.GoldDim, 20 + deckButton.Width + 12, y + 2, PW - 40 - deckButton.Width - 12);
            y = Math.Max(BottomOf(deckButton), BottomOf(deckNote)) + 8;
            if (Handheld.Experimental)
            {
                y = OptionCheck("Hide the launcher while the game runs (it comes back when the game closes)", HideWhilePlaying(), y,
                                delegate(bool on) { Cfg.Set("hide_while_playing", on); Cfg.Save(); });
                y = OptionCheck("Close the launcher after the game, once the cloud save is uploaded (back to Steam on a Steam Deck)", CloseAfterPlaying(), y,
                                delegate(bool on) { Cfg.Set("close_after_playing", on); Cfg.Save(); });
            }
            y += Theme.Px(6);
            var scaleTitle = PutText("Interface size", 10, FontStyle.Bold, Theme.Gold, 20, y, PW-40);
            y=BottomOf(scaleTitle)+Theme.Px(4);
            var scaleNote=PutText(displayLayout.RequestedScale>displayLayout.Scale+.01f
                ? "Size is limited to fit this display. Choose a smaller size for more room."
                : "Auto follows your monitor. Choose a size to adjust it without changing Windows settings.",9,
                FontStyle.Italic,Theme.GoldDim,20,y,PW-40);
            y=BottomOf(scaleNote)+Theme.Px(6);
            int scaleX=20, scaleHeight=Theme.Px(27), scaleGap=Theme.Px(5);
            foreach(float choice in new[]{0f,.8f,.9f,1f,1.1f,1.25f,1.5f,1.6f})
            {
                float selectedScale=choice;
                int buttonWidth=Theme.Px(choice==0?60:48);
                if(scaleX+buttonWidth>PW-20) { scaleX=20; y+=scaleHeight+scaleGap; }
                var sizeButton=new WowButton(choice==0?"AUTO":(choice*100).ToString("0")+"%",delegate{SetInterfaceScale(selectedScale);},60,27,8);
                sizeButton.Bounds=new Rectangle(scaleX,y,buttonWidth,scaleHeight);
                sizeButton.Selected=Math.Abs(ReadInterfaceScale()-choice)<.001f;
                Put(sizeButton,scaleX,y);
                scaleX+=buttonWidth+scaleGap;
            }
            y+=scaleHeight+Theme.Px(10);
            // Launcher is the end of the path (Options › Launcher): everything it holds is on this page, nothing opens a
            // step of its own. The language is picked from a list under its button; About is written out at the bottom.
            string langName = Lang.Code;
            foreach (var kv in Lang.Available()) if (kv.Key == Lang.Code) langName = kv.Value;
            y += 8;
            row("UPDATE LAUNCHER", delegate { CheckLauncherUpdate(true); }, "Looks for a newer launcher now and installs it (it also does this by itself at every start).");
            if (launcherUpdateOfferPending && launcherRelease != null)
            {
                y += 2;
                var offer = PutText(Lang.F("Launcher {0} is available.", launcherRelease.Name), 10.5f, FontStyle.Bold,
                                    Theme.Gold, 20, y, PW - 40);
                var details = PutText("Install downloads and verifies the update, then restarts the launcher.", 9, FontStyle.Italic,
                                      Theme.GoldDim, 20, BottomOf(offer) + 2, PW - 40);
                y = BottomOf(details) + 8;
                var installUpdate = new WowButton("INSTALL UPDATE", delegate
                {
                    LauncherRelease pending = launcherRelease;
                    if (pending != null) DownloadLauncher(pending, false);
                }, 150, 30, 9);
                Put(installUpdate, 20, y);
                Put(new WowButton("LATER", delegate
                {
                    launcherUpdateOfferPending = false;
                    SetStatus(Lang.T("Launcher update postponed. Press UPDATE when you want it."));
                    RebuildCurrent();
                }, 110, 30, 9), installUpdate.Right + 8, y);
                y += (int)(30 * Theme.Zoom) + 10;
            }
            WowButton langBtn = null;
            Action pickLang = delegate
            {
                var menu = ThemeMenus.Create(true);
                foreach (var kv in Lang.Available())
                {
                    string code = kv.Key;
                    var it = new ToolStripMenuItem(kv.Value.Replace("&", "&&"), null, delegate { SetLanguage(code); });
                    it.Checked = code == Lang.Code;
                    menu.Items.Add(it);
                }
                if (langBtn != null) menu.Show(langBtn, new Point(0, langBtn.Height));
            };
            langBtn = Put(new WowButton(Lang.T("LANGUAGE") + " ▾", pickLang, bw, 30, 9), 20, y);
            var langNote = PutText(Lang.F("The launcher's language (now {0}).", langName), 9.5f, FontStyle.Italic, Theme.GoldDim,
                                   20 + langBtn.Width + 12, y + 2, PW - 40 - langBtn.Width - 12, false);
            y = Math.Max(BottomOf(langBtn), BottomOf(langNote)) + 8;
            row("RUN SETUP AGAIN", delegate { RunSetup(); }, "The first-time setup's steps again, with your settings as they are.");
            row("ERROR LOG", delegate { ShowErrorLogBubble(); }, "Shows what the launcher logged, in plain words, with a COPY button for sending it to others.");
            row(Lang.T("ABOUT LAUNCHER"), delegate { optionsAbout = true; optionsLang = false; BuildPanel("Options"); },
                "What the launcher does and how its parts fit together.");
            return y;                                                     // play time: on the home screen, under the Launcher buttons
        }

        int OptionsAbout(int PW)
        {
            int y = 8;
            foreach (string raw in Data.About.Split('\n'))
            {
                string ln = raw.TrimEnd();
                if (ln.Length == 0) continue;
                Control t;
                if (ln.StartsWith("#")) t = PutText(ln.TrimStart('#', ' '), 12, FontStyle.Bold, Theme.Gold, 20, y + 4, PW - 40, false);
                else if (ln.StartsWith("- ")) t = PutText("•  " + ln.Substring(2), 9.5f, FontStyle.Regular, Theme.Text, 30, y, PW - 50, false);
                else t = PutText(ln, 9.5f, FontStyle.Regular, Theme.Text, 20, y, PW - 40, false);
                y = BottomOf(t) + 3;
            }
            return y;
        }

        // the game folder: one that holds the game is the same as installing it (there is no "install from a file")
        void ChooseGameFolderButton() { ChooseGameFolder(); }

        bool ChooseGameFolder()
        {
            string d = AskFolder(this, "Select your Azeroth Survivors game folder (the one with " + Util.GameExe + ")", GameDir());
            if (d == null) return false;
            if (!File.Exists(Path.Combine(d, Util.GameExe)))
            {
                Warn("Game folder", Lang.T("That folder does not have the game in it") + " (" + Util.GameExe + ").");
                return false;
            }
            Cfg.Set("game_dir", d);
            Cfg.Set("installed_by_launcher", true);           // the player's own copy counts as installed
            UpdateNewPlayerFlag();
            Cfg.Save();
            SetStatus("Game folder set: " + d);
            RefreshPlayLabel();                              // PLAY/INSTALL and the Cloud Save button follow the new folder
            Invalidate();
            RebuildCurrent();
            return true;
        }

        void ChooseSaveDir()
        {
            string d = AskFolder(side, "Select the folder that holds your save game (normally %APPDATA%\\wenilla-rogue)", SyncSettings().SaveDir);
            if (d == null) return;
            Cfg.Set("save_dir", Path.GetFullPath(d));
            Cfg.Save();
            RebuildCurrent();
        }

        // The AddOns or saved folder picked in place of benilla-config would put addons in AddOns\AddOns:
        // the folder above it is the config folder.
        internal static string ConfigFolderOf(string path)
        {
            string trimmed = (path ?? "").TrimEnd('\\', '/');
            string name = Path.GetFileName(trimmed);
            if (name.Equals("AddOns", StringComparison.OrdinalIgnoreCase) || name.Equals("saved", StringComparison.OrdinalIgnoreCase))
            {
                string parent = Path.GetDirectoryName(trimmed);
                if (!string.IsNullOrEmpty(parent)) return parent;
            }
            return path;
        }

        // Earlier builds could put addons in benilla-config\AddOns\AddOns, where the game never looks.
        // Save the outer folder and move what landed inside up into it. When both have the same addon,
        // the newer copy stays and the older one goes to the launcher's temp folder.
        void FixNestedAddonsFolder()
        {
            try
            {
                string saved = Cfg.Str("addons_dir");
                if (saved.Length > 0 && !Util.SamePath(saved, AzerothLauncher.SyncSettings.OuterAddonsDir(saved)))
                {
                    Cfg.Set("addons_dir", AzerothLauncher.SyncSettings.OuterAddonsDir(saved));
                    Cfg.Save();
                }
                string outer = SyncSettings().AddonsDir;
                string inner = Path.Combine(outer, "AddOns");
                if (!Directory.Exists(inner)) return;
                string old = Path.Combine(Util.TempDir, "nested-addons-" + Guid.NewGuid().ToString("N"));
                foreach (string from in Directory.GetFileSystemEntries(inner))
                {
                    try
                    {
                        string to = Path.Combine(outer, Path.GetFileName(from));
                        bool fromIsFolder = Directory.Exists(from);
                        bool toExists = Directory.Exists(to) || File.Exists(to);
                        if (toExists)
                        {
                            DateTime fromAt = fromIsFolder ? Directory.GetLastWriteTimeUtc(from) : File.GetLastWriteTimeUtc(from);
                            DateTime toAt = Directory.Exists(to) ? Directory.GetLastWriteTimeUtc(to) : File.GetLastWriteTimeUtc(to);
                            if (fromAt <= toAt) continue;                 // the outer copy is newer: leave both
                            Directory.CreateDirectory(old);
                            string aside = Path.Combine(old, Path.GetFileName(to));
                            if (Directory.Exists(to)) Directory.Move(to, aside); else File.Move(to, aside);
                        }
                        if (fromIsFolder) Directory.Move(from, to); else File.Move(from, to);
                    }
                    catch (Exception) { }                              // in use: tried again next start
                }
                if (Directory.GetFileSystemEntries(inner).Length == 0) Directory.Delete(inner);
            }
            catch (Exception) { }
        }

        void ChooseConfigDir()
        {
            string d = AskFolder(side, "Select the game's benilla-config folder (contains AddOns and saved)", SyncSettings().ConfigDir);
            if (d == null) return;
            Cfg.Set("config_dir", ConfigFolderOf(Path.GetFullPath(d)));
            Cfg.Remove("addons_dir");                        // AddOns and addon settings follow the config folder
            Cfg.Remove("saved_dir");
            Cfg.Save();
            RebuildCurrent();
        }
    }
}
