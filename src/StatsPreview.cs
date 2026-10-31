// A developer tool, not used by players: "--preview-stats <saved folder> <out folder>" renders the STATS page for
// the Azeroth Stats or legacy saved-variable file in <saved folder> into PNG pictures (every tab, overall and for the most played class),
// so the layout can be checked without the game. Settings are only changed in memory, nothing is saved.
using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Windows.Forms;

namespace AzerothLauncher
{
    public partial class LauncherApp
    {
        public static void RenderStatsPreview(string savedDir, string outDir)
        {
            Directory.CreateDirectory(outDir);
            PreviewMode = true;
            var app = new LauncherApp();
            app.Cfg.Set("saved_dir", Path.GetFullPath(savedDir));
            app.Cfg.Set("addons_dir", Path.Combine(Path.GetFullPath(outDir), "AddOns"));
            app.Cfg.Set("cloud_enabled", false);
            app.StartPosition = FormStartPosition.Manual;
            app.Location = new Point(0, 0);
            app.Show();
            app.ShowSide();
            RunStats st = Stats.Load(app.SyncSettings().SavedDir);
            string topClass = st != null && st.Classes.Count > 0 ? st.Classes[0].Key : null;
            if (st != null)                                  // ask for every spell icon now, so the pictures show them
            {
                foreach (var ab in st.Abilities) SpellIcons.Spell(ab.Name, null);
                foreach (var c in st.Classes) SpellIcons.Class(c.Key, null);
                foreach (var n in st.Nemesis) SpellIcons.Npc(n.Name, null);
                foreach (var g in st.Races) SpellIcons.Race(g.Key + "|" + g.Name, null);
                foreach (var g in st.Zones) SpellIcons.Zone(g.Key + "|" + g.Name, null);
                foreach (var h in st.History) { SpellIcons.Zone(h.Zone, null); foreach (var ab in h.Abilities) SpellIcons.Spell(ab.Name, null); }
                SpellIcons.WaitForAll(30000);
            }
            foreach (string cls in new[] { null, topClass })
            {
                for (int tab = 0; tab < StatTabs.Length; tab++)
                {
                    app.current = "Stats";
                    app.statsClass = cls;
                    app.statsTab = tab;
                    app.BuildPanel("Stats");
                    Application.DoEvents();
                    for (int k = 0; k < 3; k++)                  // spell icons arrive from Wowhead while the page is shown
                    {
                        SpellIcons.WaitForAll(15000);
                        app.panel.Refresh();
                        Application.DoEvents();
                    }
                    string name = (cls ?? "all") + "-" + tab + "-" + StatTabs[tab].ToLowerInvariant();
                    using (var win = new Bitmap(app.side.Width, app.side.Height))
                    {
                        app.side.DrawToBitmap(win, new Rectangle(0, 0, win.Width, win.Height));
                        win.Save(Path.Combine(outDir, name + "-window.png"), ImageFormat.Png);
                    }
                    ScrollPanel sp = FindScroll(app.panel);
                    if (sp != null)
                    {
                        int h = 10, wdt = sp.ClientSize.Width;
                        foreach (Control c in sp.Controls) h = Math.Max(h, c.Bottom + 10);
                        using (var page = new Bitmap(wdt, h))
                        {
                            using (var g = Graphics.FromImage(page)) g.Clear(sp.BackColor);
                            foreach (Control c in sp.Controls)
                                if (c.Width > 0 && c.Height > 0) DrawDeep(c, page, c.Left, c.Top);
                            page.Save(Path.Combine(outDir, name + "-page.png"), ImageFormat.Png);
                        }
                    }
                }
                if (topClass == null) break;
            }
            // the New Player Guide: topic cards, a topic made of pages, and pages with video cards
            {
                var topics = Data.Guide;
                int classes = 0;
                for (int i = 0; i < topics.Count; i++) if (topics[i].Title == "Classes") { classes = i; break; }
                foreach (int[] view in new[] { new[] { -1, -1 }, new[] { classes, -1 }, new[] { 0, -1 }, new[] { 1, -1 }, new[] { classes, 0 }, new[] { classes, 4 }, new[] { classes, 6 }, new[] { classes + 1, -1 } })
                {
                    app.current = "Guide";
                    app.ShowSide();
                    app.gTopic = view[0];
                    app.gPage = view[1];
                    app.BuildPanel("Guide");
                    Application.DoEvents();
                    using (var win = new Bitmap(app.side.Width, app.side.Height))
                    {
                        app.side.DrawToBitmap(win, new Rectangle(0, 0, win.Width, win.Height));
                        win.Save(Path.Combine(outDir, "guide-" + view[0] + "-" + view[1] + ".png"), ImageFormat.Png);
                    }
                    ScrollPanel gsp = FindScroll(app.panel);
                    if (gsp != null)
                    {
                        int h = 10;
                        foreach (Control c in gsp.Controls) h = Math.Max(h, c.Bottom + 10);
                        using (var page = new Bitmap(gsp.ClientSize.Width, h))
                        {
                            using (var g = Graphics.FromImage(page)) g.Clear(gsp.BackColor);
                            foreach (Control c in gsp.Controls)
                                if (c.Width > 0 && c.Height > 0) DrawDeep(c, page, c.Left, c.Top);
                            page.Save(Path.Combine(outDir, "guide-" + view[0] + "-" + view[1] + "-page.png"), ImageFormat.Png);
                        }
                    }
                }
                {   // the Talent Tree Builder's class picker
                    app.current = "Guide";
                    app.ShowSide();
                    app.gTopic = -1; app.gPage = -1; app.gTalents = null; app.gBuilder = true;
                    app.BuildPanel("Guide");
                    Application.DoEvents();
                    PreviewShot(app, Path.Combine(outDir, "guide-builder.png"));
                    app.gBuilder = false;
                }
                {   // the Gear Planner: a Priest in the Transcendence (Epic) set with some jewelry
                    app.current = "Guide";
                    app.ShowSide();
                    app.gTopic = -1; app.gPage = -1; app.gTalents = null; app.gBuilder = false; app.gPlanner = true;
                    app.plannerClass = "Priest";
                    app.plannerSlots.Clear();
                    foreach (var p in Items.Gear)
                        if (p.Class == "Priest" && p.Rarity == "Epic") app.plannerSlots[p.Slot == "Main Hand" ? "Main Hand" : p.Slot] = p;
                    foreach (var j in Items.Jewelry)
                    {
                        if (j.Key == "accuria") app.plannerSlots["Finger 1"] = j;
                        if (j.Key == "dragonslayer") app.plannerSlots["Finger 2"] = j;
                        if (j.Key == "hoj") app.plannerSlots["Trinket 1"] = j;
                        if (j.Key == "prestor") app.plannerSlots["Neck"] = j;
                    }
                    app.BuildPanel("Guide");
                    Application.DoEvents();
                    PreviewShot(app, Path.Combine(outDir, "guide-planner.png"));
                    var stack = new System.Collections.Generic.Stack<Control>();
                    stack.Push(app.panel);
                    while (stack.Count > 0)
                    {
                        Control c = stack.Pop();
                        if (c is GearSheet)
                        {
                            using (var bmp = new Bitmap(c.Width, c.Height))
                            {
                                c.DrawToBitmap(bmp, new Rectangle(0, 0, c.Width, c.Height));
                                bmp.Save(Path.Combine(outDir, "guide-planner-sheet.png"), ImageFormat.Png);
                            }
                            break;
                        }
                        foreach (Control k in c.Controls) stack.Push(k);
                    }
                    app.gPlanner = false;
                    // the Items topic's Priest page
                    int items = Data.Guide.FindIndex(delegate(GuideTopic g) { return g.Title == "Items"; });
                    if (items >= 0)
                        foreach (int pg in new[] { 0, 5 })
                        {
                            app.gTopic = items; app.gPage = pg; app.guideMain = false;
                            app.BuildPanel("Guide");
                            Application.DoEvents();
                            PreviewShot(app, Path.Combine(outDir, "guide-items-" + pg + ".png"));
                        }
                }
                {   // the Warrior's talent trees with a test build
                    app.current = "Guide";
                    app.ShowSide();
                    app.gTopic = classes; app.gPage = 0; app.gTalents = "Warrior";
                    app.liveBuild = TalentBuild.Parse("Arms/imp_heroic=5;Arms/deflection=3;Arms/deep_wounds=1;Fury/cruelty=5", "Warrior");
                    app.liveBuildCls = "Warrior";
                    app.BuildPanel("Guide");
                    Application.DoEvents();
                    using (var win = new Bitmap(app.side.Width, app.side.Height))
                    {
                        app.side.DrawToBitmap(win, new Rectangle(0, 0, win.Width, win.Height));
                        win.Save(Path.Combine(outDir, "guide-talents.png"), ImageFormat.Png);
                    }
                    ScrollPanel tsp = FindScroll(app.panel);
                    if (tsp != null)
                    {
                        int h = 10;
                        foreach (Control c in tsp.Controls) h = Math.Max(h, c.Bottom + 10);
                        using (var page = new Bitmap(tsp.ClientSize.Width, h))
                        {
                            using (var g = Graphics.FromImage(page)) g.Clear(tsp.BackColor);
                            foreach (Control c in tsp.Controls) DrawDeep(c, page, c.Left, c.Top);
                            page.Save(Path.Combine(outDir, "guide-talents-page.png"), ImageFormat.Png);
                        }
                    }
                    app.gTalents = null;
                }
                app.current = "Stats";
                app.BuildPanel("Stats");
            }
            // every other section, to check they all use the launcher's colors
            // the AZS addons the launcher carries, installed into the preview's AddOns folder (their files, the data
            // written from the guide among them, are kept with the pictures to be checked)
            try
            {
                string addonsDir = Path.Combine(Path.GetFullPath(outDir), "AddOns");
                Directory.CreateDirectory(addonsDir);
                foreach (var a in BuiltinAddons.All) BuiltinAddons.Install(addonsDir, a);
            }
            catch (Exception) { }
            foreach (string sec in new[] { "Setup", "Setup Folders", "Cloud Save", "Addons", "Addons Download", "Addons Download 112", "Changelog", "Options", "About Launcher", "Guide" })
            {
                app.Show();                                  // the preview starts as a visible launcher window
                app.ShowSide();
                string build = sec;
                if (sec == "Addons Download") { build = "Addons"; app.addonMode = "download"; app.addonCategory = 0; }
                else if (sec == "Addons Download 112") { build = "Addons"; app.addonMode = "download"; app.addonCategory = 3; }
                else if (sec == "Addons") app.addonMode = "installed";
                if (sec == "Setup" || sec == "Setup Folders") { app.ShowSetupDialog(); build = "Setup"; app.setupPage = sec == "Setup" ? 0 : 3; }
                if (sec == "Cloud Save") { build = "Options"; app.optionsTab = 1; app.optionsLang = false; }   // Options > CLOUD SAVE
                else if (sec == "Options") app.optionsTab = 0;
                app.current = build;
                if (sec == "Guide") { app.gTopic = -1; app.gPage = -1; app.guideQuery = "frost"; }   // the search, with a word typed
                try { app.BuildPanel(build); }
                catch (Exception ex)
                {
                    using (var eb = new Bitmap(900, 300))
                    {
                        using (var g = Graphics.FromImage(eb)) { g.Clear(Color.White); g.DrawString(ex.ToString(), new Font("Arial", 8), Brushes.Black, new RectangleF(4, 4, 892, 292)); }
                        eb.Save(Path.Combine(outDir, "section-error-" + sec.Replace(" ", "-") + ".png"), ImageFormat.Png);
                    }
                    continue;
                }
                Application.DoEvents();
                app.panel.Refresh();
                string nm = "section-" + sec.Replace(" ", "-").ToLowerInvariant();
                using (var win = new Bitmap(app.panel.Width, app.panel.Height))
                {   // control by control, back to front (DrawToBitmap of the whole window skips some of them)
                    using (var g = Graphics.FromImage(win)) g.Clear(app.panel.BackColor);
                    for (int i = app.panel.Controls.Count - 1; i >= 0; i--)
                    {
                        Control c = app.panel.Controls[i];
                        if (c.Visible && c.Width > 0 && c.Height > 0) c.DrawToBitmap(win, c.Bounds);
                    }
                    win.Save(Path.Combine(outDir, nm + ".png"), ImageFormat.Png);
                }
                ScrollPanel ssp = FindScroll(app.panel);
                if (ssp != null)
                {
                    int h = 10;
                    foreach (Control c in ssp.Controls) h = Math.Max(h, c.Bottom + 10);
                    using (var page = new Bitmap(ssp.ClientSize.Width, Math.Min(h, 3000)))
                    {
                        using (var g = Graphics.FromImage(page)) g.Clear(ssp.BackColor);
                        foreach (Control c in ssp.Controls)
                            if (c.Width > 0 && c.Height > 0) DrawDeep(c, page, c.Left, c.Top);
                        page.Save(Path.Combine(outDir, nm + "-page.png"), ImageFormat.Png);
                    }
                }
            }
            // the main window with Community (the home screen), with a section open, then with none
            foreach (string shot in new[] { "home-community", "home-section", "home" })
            {
                if (shot == "home-community") { app.current = "Community"; app.ShowSide(); app.BuildPanel("Community"); }
                if (shot == "home-section") { app.current = "Stats"; app.BuildPanel("Stats"); }
                if (shot == "home") app.side.Hide();
                PreviewShot(app, Path.Combine(outDir, shot + ".png"));
            }
            // every tab of Options
            for (int t = 0; t < OptionTabs.Length; t++)
            {
                app.optionsTab = t;
                app.current = "Options";
                app.ShowSide();
                app.BuildPanel("Options");
                PreviewShot(app, Path.Combine(outDir, "options-" + t + ".png"));
            }
            app.optionsTab = 0;
            // every language: the home screen and Options, to see that nothing runs out of its space
            string langDir = Path.Combine(outDir, "lang");
            Directory.CreateDirectory(langDir);
            foreach (var kv in Lang.Available())
            {
                Lang.Load(kv.Key);
                Theme.PickFont();
                foreach (string sec in new[] { "Community", "Options", "Guide" })
                {
                    app.current = sec;
                    app.ShowSide();
                    app.BuildPanel(sec);
                    PreviewShot(app, Path.Combine(langDir, kv.Key + "-" + sec.ToLowerInvariant() + ".png"));
                }
            }
            Lang.Load("en");
            Theme.PickFont();
        }

        // the whole window as a picture, painted by hand: the build machine's screen is smaller than the window
        static void PreviewShot(LauncherApp app, string file)
        {
                app.Show();                                      // the preview starts as a visible launcher window
            app.Invalidate(true);
            Application.DoEvents();
            using (var home = new Bitmap(W, H))
            {
                using (var g = Graphics.FromImage(home))
                    app.OnPaint(new PaintEventArgs(g, new Rectangle(0, 0, W, H)));
                for (int i = app.Controls.Count - 1; i >= 0; i--)
                {
                    Control c = app.Controls[i];
                    if (c.Visible && c.Width > 0 && c.Height > 0) c.DrawToBitmap(home, c.Bounds);
                }
                home.Save(file, ImageFormat.Png);
            }
        }

        static ScrollPanel FindScroll(Control c)
        {
            foreach (Control k in c.Controls)
            {
                var s = k as ScrollPanel;
                if (s != null) return s;
                s = FindScroll(k);
                if (s != null) return s;
            }
            return null;
        }

        // a control and everything in it (DrawToBitmap alone leaves out what sits inside some painted controls)
        static void DrawDeep(Control c, Bitmap page, int x, int y)
        {
            if (c.Width <= 0 || c.Height <= 0) return;
            c.DrawToBitmap(page, new Rectangle(x, y, c.Width, c.Height));
            for (int i = c.Controls.Count - 1; i >= 0; i--)
            {
                Control k = c.Controls[i];
                DrawDeep(k, page, x + k.Left, y + k.Top);
            }
        }
    }
}
