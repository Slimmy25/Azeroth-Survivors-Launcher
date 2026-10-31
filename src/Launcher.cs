// The main launcher window: the tall narrow window with PLAY and the section buttons, playing
// (cloud download -> game -> cloud upload), installing the game, cloud sync runs and the launcher's own updates.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Globalization;
using System.IO;
using System.Net;
using System.Text;
using System.Threading;
using System.Windows.Forms;

namespace AzerothLauncher
{
    public partial class LauncherApp : Form, IWheel
    {
        // TEMPORARY: while true, nothing but INSTALL works until the game has been downloaded by this launcher
        // (the flag is stored in the config when a download finishes). Set to false to switch it off.
        public const bool RequireLauncherInstall = true;

        // Where the game is downloaded from, and the download server's login (shared with every player, so it is fine
        // in the open). The launcher downloads it itself (BeginClientDownload), into its own temp folder beside the launcher
        // ("Azeroth Survivors Launcher - Temp": a name no other folder has, so nothing of the player's is ever wiped).
        public const string ClientUrl = "https://budapestmix.hu:5000/download";
        public const string ClientUser = "AzerothSurvivors_uSZfhjS", ClientPassword = "AzerothSurvivors_uSZfhjS";
        // The developers' backup host: a page with a link to the newest archive and its SHA-256 (no login).
        public const string ClientMirrorUrl = "https://azerothsurvivors-mirror.sandervspl.dev/";
        // A third host, on Google Drive (the same game in its own archive, so its own MD5). This copy is published by one
        // of the head moderators of the official Discord, a trusted source: it is kept with its MD5 check and no further
        // measures, by choice (MD5 can only be fooled by the person who made the file, see "Download hosts" in the
        // README). Only hosts endorsed by both Aethus and the mod team are ever used; no unofficial mirrors.
        public const string ClientDriveId = "12EHYJrgdANtgZvND49L_cxQwoObTbkWq";
        // The last resort: the game's own download page. Never one of the three picked at random; only tried after
        // all three have failed. Its archive may be newer than the checksums below, so it is accepted when it is a
        // real .rar/.zip/.7z archive from the game's own site.
        public const string ClientLastResortUrl = "https://wow.aethus.pl/download";
        // Each install picks one of the hosts at random; if that one fails, the others are tried in turn. Every download
        // is checked against the game's known checksums below (update them with a new game version).
        public const string ClientSha256 = "971db7968dc37ec50b18d64a1de7f693e137f6793a7368e4d5c27bd9522eccd8";   // AzerothSurvivorsWindowsNew.rar
        public const string ClientMd5 = "7748bfdffaccd2f46d868cf69ad3e72c";                                       //   (760497456 bytes)
        public const string ClientDriveMd5 = "81887cd5d56dcca3b7dd086b0b498cf6";                                  // the Google Drive archive
        public const string DiscordUrl = "https://discord.gg/azerothsurvivors";
        public const string SupportUrl = "https://buymeacoffee.com/aethusx";      // the game's developer's own page

        const int W = 1100, H = 690;                         // retro launcher window: sections open on the left, News on the right

        public Config Cfg;
        public bool GameRunningFlag, Installing, SyncBusy, Updating, UpdateChecking;   // Updating: the launcher's own update
        bool setupOpen, setupAsked, quitting, closeAfterDownloadCancel;
        ManualResetEvent launcherUpdateCancel;
        readonly List<KeyValuePair<string, Level>> cloudLog = new List<KeyValuePair<string, Level>>();
        RichTextBox logBox;
        string status = "Ready.", cloudNote = "";
        bool addonDlBusy;

        readonly WowButton playBtn, updateBtn, aboutBtn;
        readonly BrandIcon discordIcon;
        readonly HeartIcon supportHeart;
        readonly Dictionary<string, WowButton> nav = new Dictionary<string, WowButton>();
        // the buttons under "Launcher": Guide and its Theorycrafting group, Addons, Stats, Options (Cloud Save is in Options)
        readonly string[] navNames = { "Guide", "New Player Guide", "Theorycrafting", "Addons", "Changelog", "Stats", "Options" };   // Changelog: on the News line   // order under "Launcher" (Discord before Options)
        // Options > Language
        public void SetLanguage(string code)
        {
            if (setupOpen) setupLanguage = code;
            else
            {
                Cfg.Set("language", code);
                Cfg.Save();
            }
            ApplyLanguage(code, true);
        }

        void ApplyLanguage(string code, bool rebuild)
        {
            Lang.Load(code);
            Theme.PickFont();
            foreach (Control c in Controls) c.Invalidate();
            Invalidate();
            sideHead.Invalidate();
            SetSideTitle(sideHead.Title);
            if (rebuild && current != null && current != "Home" && side.Visible) BuildPanel(current);
        }

        public static bool PreviewMode;                     // rendering preview pictures: no update checks, no restarts

        public static LauncherApp App;                       // the running launcher (for the Stats page's static builders)

        public LauncherApp()
        {
            App = this;
            WowDialog.Host = this;                           // questions and messages open inside the launcher
            Theme.PickFont();
            Cfg = Config.Load();
            // Create the first-run marker beside this exe before Shown can start the automatic updater. It remains
            // pending until setup succeeds, including across an automatic updater restart.
            if (!PreviewMode) try { Util.EnsureSetupPendingMarker(); } catch (Exception) { }
            if (!PreviewMode && Util.SetupPendingForThisInstall) status = "First Time Setup";

            Text = "Azeroth Survivors Launcher";
            Icon = Theme.AppIcon;
            FormBorderStyle = FormBorderStyle.FixedSingle;
            MaximizeBox = false;
            ClientSize = new Size(W, H);
            BackColor = Theme.BgBottom;
            DoubleBuffered = true;
            StartPosition = FormStartPosition.CenterScreen;

            // layout: PLAY in the box at the bottom right, the sections under the news, the rest in the bottom bar
            playBtn = Place(new WowButton("PLAY", Play, 88, 49, 12), 977, 578 + (112 - 49) / 2);   // centred in the bottom bar         // smaller: status above, time played below
            playBtn.Skin = Retro.Play;
            foreach (string name in navNames)
            {
                string n = name;
                if (n == "Changelog") continue;              // on the News line (below)
                Action open = n == "Theorycrafting" ? (Action)delegate { GuideGroupPage(2); } :
                              n == "New Player Guide" ? (Action)delegate { GuideGroupPage(0); } : delegate { OpenMain(n); };   // dedicated guide destinations
                nav[n] = Place(new WowButton(n.ToUpperInvariant(), open, 128, 27, 8.5f), 693, 480);
                nav[n].Skin = Retro.Button;
            }
            // UPDATE LAUNCHER and ABOUT LAUNCHER live in Options > Launcher now; these two stay hidden and carry their state
            aboutBtn = Place(new WowButton("ABOUT LAUNCHER", delegate { ShowSection("About Launcher"); }, 159, 27, 8), W - 8 - 159 - 6, 98);
            aboutBtn.Visible = false;
            nav["About Launcher"] = aboutBtn;
            updateBtn = Place(new WowButton("UPDATE LAUNCHER", delegate { CheckLauncherUpdate(true); }, 159, 27, 8), W - 8 - 159 - 6, 64);
            updateBtn.Visible = false;
            // the News line: Discord's own symbol (Blurple, from discord.com/branding, unaltered) left of "News", and
            // CHANGELOG at the very end of the line
            discordIcon = new BrandIcon(Theme.LoadImage("AS.discord-blurple.png"), Theme.LoadImage("AS.discord-white.png"), Lang.T("Join the community!") + "\n" + DiscordUrl);   // the hover ends with where it goes
            discordIcon.Bounds = new Rectangle(725, 203, 26, 20);
            nav["Changelog"] = Place(new WowButton("CHANGELOG", delegate { OpenMain("Changelog"); }, 110, 22, 8), 1085 - 110, 201);
            nav["Changelog"].Skin = Retro.Button;
            discordIcon.Click += delegate { Util.OpenInShell(DiscordUrl); };
            Controls.Add(discordIcon);
            // the enlarged support heart, followed immediately by the Discord icon
            supportHeart = new HeartIcon(Lang.T("Love the game? Support the Dev by buying him a coffee") + "\n" + SupportUrl);
            supportHeart.Bounds = new Rectangle(697, 203, 26, 20);
            supportHeart.Click += delegate { Util.OpenInShell(SupportUrl); };
            Controls.Add(supportHeart);
            LayoutNav();

            BuildSide();
            RefreshPlayLabel();

            FormClosing += OnClosingWindow;
            FormClosed += delegate { StopEdge(); };              // a Community video never outlives the launcher
            VideoCard.Live3D = OpenLive3D;                       // Wowhead's live 3D viewer in an Edge window (Community.cs)
            VideoCard.YouTube = PlayYouTube;                     // YouTube videos play inside Community (Community.cs)
            SpellRow.OpenSet = delegate(string cls, string rarity)   // a set chip on a spell: that set in the Gear Planner
            {
                if (current != "Guide" || !side.Visible) ShowSection("Guide");
                PlannerClass(cls);
                GearSheet.FillSet(cls, rarity, 8, plannerSlots);
                OpenGuideTab(-3);
            };
            GuideLinks.Navigate = delegate(GuideLink l)        // a link in the Guide's text (GuideLinks.cs)
            {
                if (current != "Guide" || !side.Visible) ShowSection("Guide");
                if (NavigateClassInline(l)) return;              // class spells, talents and item links stay in their article
                if (NavigateDirectoryCodex(l)) return;            // a Codex entry opens its class guide context
                guideFindQuery = string.IsNullOrWhiteSpace(l.FindText) ? l.Term : l.FindText;
                guideQuery = guideFindQuery;
                if (l.Class != null)                             // a talent: its class's talent trees
                {
                    SetGuideView(l.Topic, l.Page);
                    gTalents = l.Class;
                    BuildPanel("Guide");
                    return;
                }
                SetGuideView(l.Topic, l.Page);
                After(30, delegate { ShowSpellRow(l.Term); });   // a spell: its row on the page, scrolled to and lit up
            };
            // Restore repaint: everything is redrawn after minimizing and restoring from the taskbar.
            Resize += delegate
            {
                if (WindowState == FormWindowState.Minimized) { wasMinimized = true; return; }
                if (!wasMinimized) return;
                wasMinimized = false;
                After(1, RepaintAfterRestore);
            };
            Shown += delegate
            {
                if (!PreviewMode && !Program.LocalDevelopmentBuild) After(300, StartupUpdateCheck);
                if (!PreviewMode) After(4000, AutoUpdateAddons);    // and newer addons from GitHub, without asking
                After(500, MaybeFirstRun);
                After(700, delegate { if ((current == null || current == "Home") && !setupOpen) ShowHomeCommunity(); });   // home: Community
            };
            VisibleChanged += delegate
            {
                if (Visible && !setupAsked) After(500, MaybeFirstRun);   // started hidden: setup when first opened
            };
        }

        WowButton Place(WowButton b, int x, int y)
        {
            b.Location = new Point(x, y);
            Controls.Add(b);
            return b;
        }

        // ------------------------------------------------------------------ small tools
        public void Ui(Action a)
        {
            try { if (IsHandleCreated && !IsDisposed) BeginInvoke(a); }
            catch (Exception) { }
        }

        // runs fn on the UI thread from a worker thread and returns its result
        T UiCall<T>(Func<T> fn)
        {
            return (T)Invoke(fn);
        }

        public static void After(int ms, Action a)
        {
            var t = new System.Windows.Forms.Timer();
            t.Interval = Math.Max(1, ms);
            t.Tick += delegate
            {
                t.Stop();
                t.Dispose();
                a();
            };
            t.Start();
        }

        static void Bg(ThreadStart work)
        {
            var t = new Thread(work);
            t.IsBackground = true;
            t.Start();
        }

        public static string AskFolder(IWin32Window owner, string title, string start)
        {
            using (var d = new FolderBrowserDialog())
            {
                d.Description = Lang.T(title);
                d.ShowNewFolderButton = true;
                if (!string.IsNullOrEmpty(start) && Directory.Exists(start)) d.SelectedPath = start;
                return d.ShowDialog(owner) == DialogResult.OK && d.SelectedPath.Length > 0 ? d.SelectedPath : null;
            }
        }

        public bool Ask(string title, string msg)
        {
            return Msg.Show(this, msg, title, MessageBoxButtons.YesNo, MessageBoxIcon.Question) == DialogResult.Yes;
        }

        public void Info(string title, string msg) { Msg.Show(this, msg, title, MessageBoxButtons.OK, MessageBoxIcon.Information); }
        public void Warn(string title, string msg) { Msg.Show(this, msg, title, MessageBoxButtons.OK, MessageBoxIcon.Warning); }
        public void Error(string title, string msg) { Msg.Show(this, msg, title, MessageBoxButtons.OK, MessageBoxIcon.Error); }

        // ------------------------------------------------------------------ settings
        public string GameDir()
        {
            string d = Cfg.Str("game_dir");
            return d.Length > 0 ? d : Util.LauncherDir;      // by default the folder the launcher sits in
        }

        public bool ClientPresent()
        {
            if (!File.Exists(Path.Combine(GameDir(), Util.GameExe))) return false;
            return !RequireLauncherInstall || Cfg.Bool("installed_by_launcher", false);
        }

        // Persist the requested first-time marker and keep it in sync with the game root folder.
        // A game is considered present only when its executable is in the selected root.
        void UpdateNewPlayerFlag()
        {
            bool found = File.Exists(Path.Combine(GameDir(), Util.GameExe));
            bool value = !found;
            if (!Cfg.Has("new player") || Cfg.Bool("new player", value) != value)
            {
                Cfg.Set("new player", value);
                Cfg.Save();
            }
        }

        // false while the temporary "install the game with this launcher first" rule is blocking
        public bool GateOpen() { return !RequireLauncherInstall || ClientPresent() || (Installing && setupBrowseWhileInstalling); }

        public bool CloudEnabled() { return Cfg.Bool("cloud_enabled", Cfg.Str("cloud_dir").Trim().Length > 0); }

        public SyncSettings SyncSettings()
        {
            string game = GameDir();
            string config = Cfg.Str("config_dir");
            if (config.Length == 0 || !Directory.Exists(config)) config = Path.Combine(game, "benilla-config");
            string save = Cfg.Str("save_dir");
            return new SyncSettings(Path.Combine(game, Util.GameExe), Cfg.Str("cloud_dir").Trim(),
                                    save.Length > 0 ? save : Util.DefaultSaveDir, config, Util.StateDir,
                                    Cfg.Str("addons_dir"), Cfg.Str("saved_dir"));
        }

        // ------------------------------------------------------------------ drawing the launcher
        // the launcher art (layout after the WoW Retro Launcher); the newest patch notes in the News panel
        // what never changes on the home screen (the picture, the title, its byline, the headings, the game version) is
        // drawn once into a picture of its own; a repaint copies the part it needs and draws only what moves on top
        Bitmap homeLayer;
        string homeLayerKey;
        readonly Dictionary<string, SizeF> newsMeasured = new Dictionary<string, SizeF>();

        // the whole window is drawn by OnPaint from the picture above: no fill first (it only makes the window flicker)
        protected override void OnPaintBackground(PaintEventArgs e)
        {
            if (homeLayer == null || e.ClipRectangle.Right > W || e.ClipRectangle.Bottom > H) base.OnPaintBackground(e);
        }

        Bitmap HomeLayer()
        {
            string key = Lang.Code + "|" + Theme.FontName + "|" + W + "x" + H;
            if (homeLayer != null && homeLayerKey == key) return homeLayer;
            if (homeLayer != null) homeLayer.Dispose();
            homeLayer = new Bitmap(W, H, System.Drawing.Imaging.PixelFormat.Format32bppPArgb);
            homeLayer.SetResolution(96, 96);
            homeLayerKey = key;
            using (Graphics g = Graphics.FromImage(homeLayer))
            {
                Image bg = Retro.Get("background");
                if (bg != null) Theme.Draw(g, bg, new Rectangle(0, 0, W, H));
                else g.Clear(Theme.BgBottom);
                Theme.Smooth(g);
                g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;
                // title: "Azeroth Survivors" in big gold letters over the section area, the emblem to its right
                using (var path = new GraphicsPath())
                using (var sf = new StringFormat())
                {
                    path.AddString("Azeroth Survivors", Theme.TitleFamily(), (int)FontStyle.Bold, 58f, new PointF(0, 0), sf);   // the decorative title font
                    RectangleF b = path.GetBounds();
                    Image em = Theme.Logo;
                    int emW = em != null ? 128 : 0, emH = em != null ? em.Height * 128 / em.Width : 0;
                    float total = b.Width + (em != null ? 16 + emW : 0);
                    float x0 = 8 + (670 - total) / 2, y0 = 70 + (120 - b.Height) / 2;
                    using (var m = new Matrix())
                    {
                        m.Translate(x0 - b.X, y0 - b.Y);
                        path.Transform(m);
                    }
                    using (var sh = new GraphicsPath())
                    {
                        sh.AddPath(path, false);
                        using (var m = new Matrix()) { m.Translate(3, 4); sh.Transform(m); }
                        using (var sb = new SolidBrush(Color.FromArgb(200, 0, 0, 0))) g.FillPath(sb, sh);
                    }
                    RectangleF pb = path.GetBounds();
                    using (var lg = new LinearGradientBrush(pb, Theme.Hex("#fff1b8"), Theme.Hex("#c9821a"), LinearGradientMode.Vertical))
                        g.FillPath(lg, path);
                    using (var pen = new Pen(Theme.Hex("#4a2a08"), 2f)) g.DrawPath(pen, path);
                    if (em != null) g.DrawImage(em, pb.Right + 16, 70 + (120 - emH) / 2, emW, emH);
                    // under the title: who made the game
                    string by = Lang.T("A Game Made By Aethus");
                    Font byf = Theme.F(13, FontStyle.Italic);
                    float bw = g.MeasureString(by, byf).Width, bx = pb.Left + (pb.Width - bw) / 2, byy = pb.Bottom + 12;
                    using (var bsh = new SolidBrush(Color.Black)) g.DrawString(by, byf, bsh, bx + 1, byy + 1);
                    using (var bb = new SolidBrush(Theme.Hex("#e8d9a8"))) g.DrawString(by, byf, bb, bx, byy);
                }
                using (var gold = new SolidBrush(Retro.Gold))
                using (var shadow = new SolidBrush(Color.Black))
                {
                    Font hf = Theme.F(11);
                    g.DrawString(Lang.T("News"), hf, shadow, 757, 205);    // centered after the heart and Discord icons
                    g.DrawString(Lang.T("News"), hf, gold, 756, 204);
                    g.DrawString(Lang.T("Launcher"), hf, shadow, 698, 447);
                    g.DrawString(Lang.T("Launcher"), hf, gold, 697, 446);
                }
                string version = Data.GameVersion;
                using (var vb = new SolidBrush(Theme.Hex("#b8a27a")))            // bottom-left corner
                    g.DrawString(Lang.T("Game version") + (version.Length > 0 ? ": " + version : ""), Theme.F(9.5f), vb, 12, 670);   // a little bigger
            }
            return homeLayer;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            Rectangle clip = Rectangle.Intersect(e.ClipRectangle, new Rectangle(0, 0, W, H));
            if (clip.Width > 0 && clip.Height > 0)
            {
                g.InterpolationMode = InterpolationMode.NearestNeighbor;
                g.PixelOffsetMode = PixelOffsetMode.Half;
                g.CompositingMode = CompositingMode.SourceCopy;
                g.DrawImage(HomeLayer(), clip, clip, GraphicsUnit.Pixel);
                g.CompositingMode = CompositingMode.SourceOver;
            }
            Theme.Smooth(g);
            g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;
            // News: the newest patch note
            var posts = Data.Changelog;
            if (posts.Count > 0)
            {
                string[] lines = posts[0].Trim('\n').Split('\n');
                using (var title = new SolidBrush(Retro.Gold))
                using (var body = new SolidBrush(Theme.Hex("#e8dcc0")))
                using (var shadow = new SolidBrush(Color.Black))
                {
                    Font tf = Theme.F(11, FontStyle.Bold), bf = Theme.F(9);
                    // the whole post, scrolled by newsScroll inside NewsArea (mouse wheel, or drag the slim bar)
                    Region oldClip = g.Clip;
                    g.SetClip(NewsArea);
                    float y = NewsArea.Y - newsScroll;
                    g.DrawString(Lang.T("Patch") + " " + lines[0].Trim(), tf, shadow, 698, y + 1);
                    g.DrawString(Lang.T("Patch") + " " + lines[0].Trim(), tf, title, 697, y);
                    y += 22;
                    for (int i = 1; i < lines.Length; i++)
                    {
                        string l = lines[i].Trim();
                        if (l.Length == 0 || l.Trim('-', '=', '_', '\u2014', '\u2013').Length == 0) continue;   // blank or a rule
                        if (l.StartsWith("- ") || l.StartsWith("* ")) l = "\u2022 " + l.Substring(2);
                        SizeF sz;                            // measured once per line and font
                        string mk = l + "\u0001" + bf.Size + bf.Name;
                        if (!newsMeasured.TryGetValue(mk, out sz)) { sz = g.MeasureString(l, bf, NewsTextW); newsMeasured[mk] = sz; }
                        if (y + sz.Height > NewsArea.Y && y < NewsArea.Bottom)
                        {
                            var box = new RectangleF(700, y, NewsTextW, sz.Height + 2);
                            g.DrawString(l, bf, shadow, new RectangleF(box.X + 1, box.Y + 1, box.Width, box.Height));
                            g.DrawString(l, bf, body, box);
                        }
                        y += sz.Height + 1;
                    }
                    newsHeight = (int)(y + newsScroll - NewsArea.Y) + 4;
                    g.Clip = oldClip;
                    Rectangle thumb = NewsThumb();
                    if (!thumb.IsEmpty)
                    {
                        using (var tb = new SolidBrush(Color.FromArgb(120, Theme.CardEdge))) g.FillRectangle(tb, NewsBar.X + 2, NewsBar.Y, 2, NewsBar.Height);
                        using (var tb = new SolidBrush(newsDrag || newsBarHover ? Theme.Gold : Theme.GoldDim)) g.FillRectangle(tb, thumb);
                    }
                }
            }
            WeeklyRecap(g);
            // bottom bar: the status above PLAY says everything
            using (var gb = new SolidBrush(Retro.Gold))
            using (var sf = new StringFormat())
            {
                // bottom bar, right: status above PLAY, time played below it, the game version in the corner
                sf.Alignment = StringAlignment.Center;
                var rc = new RectangleF(800, 0, 292, 16);
                using (var sb = new SolidBrush(Theme.Hex("#fff2b0")))
                {
                    // centred over PLAY; a long status that would run off the window ends at the right edge instead
                    string st = Lang.T(status == "Ready." && !ClientPresentPaint()
                        ? (RequireLauncherInstall ? "Game client not found. Press INSTALL to download it first."
                                                  : "Game client not found. Press INSTALL, or set the game folder in Options.")
                        : status);
                    Font stf = Theme.F(9, FontStyle.Italic);
                    float cxp = playBtn.Left + playBtn.Width / 2f;
                    if (dlActive)                            // the game download: its bar takes the status line's place
                    {
                        int pct = dlTotal > 0 ? (int)Math.Min(100, dlDone * 100 / dlTotal) : -1;
                        st = Lang.T(pct >= 0 ? "Downloading the game: " + pct + "%  (" + Util.FmtSize(dlDone) + " of " + Util.FmtSize(dlTotal) + ")"
                                             : "Downloading the game: " + Util.FmtSize(dlDone));
                        Font df = Theme.F(8, FontStyle.Italic);
                        float dw = g.MeasureString(st, df).Width;
                        const int bw = 250, bh = 8;
                        float bx = Math.Min(cxp - bw / 2f, W - 10 - bw);
                        float by = playBtn.Top - 12;
                        g.DrawString(st, df, sb, Math.Min(cxp - dw / 2, W - 10 - dw), by - 15);
                        using (var back = new SolidBrush(Theme.Hex("#1a1410"))) g.FillRectangle(back, bx, by, bw, bh);
                        if (pct > 0)
                            using (var fill = new LinearGradientBrush(new RectangleF(bx, by, bw, bh), Theme.Hex("#f4d27a"), Theme.Hex("#a8761e"), 90f))
                                g.FillRectangle(fill, bx, by, bw * pct / 100f, bh);
                        using (var edge = new Pen(Theme.Hex("#c49a48"))) g.DrawRectangle(edge, bx, by, bw, bh);
                    }
                    else
                    {
                        float sw = g.MeasureString(st, stf).Width;
                        float sx = Math.Min(cxp - sw / 2, W - 10 - sw);
                        g.DrawString(st, stf, sb, sx, playBtn.Top - 26);
                    }
                }
            }
        }

        // the paint asks often; the disk is asked at most every 2 seconds
        bool clientPresentSeen;
        DateTime clientPresentAt;
        bool ClientPresentPaint()
        {
            if ((DateTime.UtcNow - clientPresentAt).TotalSeconds > 2) { clientPresentSeen = ClientPresent(); clientPresentAt = DateTime.UtcNow; }
            return clientPresentSeen;
        }

        // ---- "This week": a card in the empty lower left of the home screen (recent recorded runs)
        RunStats recapStats;
        DateTime recapAt;

        void WeeklyRecap(Graphics g)
        {
            // Under the Launcher buttons: recent run activity and the available kind of play-time data.
            var lines = new List<string[]>();                // { heading, text }
            RunStats st = null;
            // The addon's file is read at most every 3 seconds (repaints happen often).
            if ((DateTime.UtcNow - recapAt).TotalSeconds > 3)
            {
                try { recapStats = StatsEnabled() ? AddonStats() : null; } catch (Exception) { recapStats = null; }
                recapAt = DateTime.UtcNow;
            }
            st = recapStats;
            if (st != null && st.History.Count > 0) lines.Add(new[] { Lang.T("This week"), WeekText(st) });
            PlayStats ps = st != null ? st.Play : new PlayStats();
            if (st != null && st.Source == "Azeroth Stats" && st.Total.Time > 0 && StatsEnabled() && Cfg.Bool("home_playtime", true))
                lines.Add(new[] { Lang.T("Time in runs"), Duration((long)st.Total.Time) + " · " + Lang.T(Big(st.Total.Runs) + " runs recorded") });
            else if (ps.Sessions > 0 && StatsEnabled() && Cfg.Bool("home_playtime", true))
                lines.Add(new[] { Lang.T("Time played"), Duration(ps.Seconds) + "  ·  " + Lang.T("Last session") + " " + Duration(ps.LastSeconds) });
            if (lines.Count == 0) return;
            var box = new RectangleF(694, 546, 394, 30);
            using (var path = Armory.Round(box, 5))
            {
                using (var b = new SolidBrush(Color.FromArgb(190, 14, 13, 20))) g.FillPath(b, path);
                using (var p = new Pen(Color.FromArgb(150, Theme.GoldDim), 1)) g.DrawPath(p, path);
            }
            Font hf = Theme.F(8, FontStyle.Bold), bf = Theme.F(8);
            float lh = lines.Count == 1 ? box.Height : box.Height / 2;
            using (var gold = new SolidBrush(Retro.Gold))
            using (var text = new SolidBrush(Theme.Text))
            using (var sf = new StringFormat(StringFormatFlags.NoWrap))
            {
                sf.Trimming = StringTrimming.EllipsisCharacter;
                sf.LineAlignment = StringAlignment.Center;
                for (int i = 0; i < lines.Count; i++)
                {
                    float y = box.Y + i * lh + (lines.Count == 1 ? 0 : (i == 0 ? 1 : -1));
                    string head = lines[i][0] + ":";
                    // a long line (a long language, many hours) gets smaller letters instead of being cut off
                    Font h2 = hf, b2 = bf;
                    for (float sz = 8; sz >= 6; sz -= 0.25f)
                    {
                        h2 = Theme.FFit(sz, FontStyle.Bold); b2 = Theme.FFit(sz, FontStyle.Regular);
                        if (g.MeasureString(head, h2).Width + 2 + g.MeasureString(lines[i][1], b2).Width <= box.Width - 16) break;
                    }
                    float hw = g.MeasureString(head, h2).Width;
                    g.DrawString(head, h2, gold, new RectangleF(box.X + 8, y, hw + 2, lh), sf);
                    g.DrawString(lines[i][1], b2, text, new RectangleF(box.X + 8 + hw + 2, y, box.Width - 16 - hw - 2, lh), sf);
                }
            }
        }

        // "3 runs, 1 won · best DPS 205 (Mage) · most played with Brisa", or when the week was quiet, when you last played
        string WeekText(RunStats st)
        {
            DateTime since = DateTime.Now.AddDays(-7);
            int runs = 0, wins = 0;
            double bestDps = 0;
            StatRun bestRun = null;
            var partners = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            foreach (var r in st.History)
            {
                DateTime d;
                if (!DateTime.TryParse(r.Date, System.Globalization.CultureInfo.InvariantCulture,
                                       System.Globalization.DateTimeStyles.None, out d) || d < since) continue;
                runs++;
                if (r.Kind == "victory") wins++;
                if (r.Dps > bestDps) { bestDps = r.Dps; bestRun = r; }
                foreach (string p in r.Party.Split(','))
                {
                    string n = p.Trim();
                    if (n.Length == 0) continue;
                    int c;
                    partners.TryGetValue(n, out c);
                    partners[n] = c + 1;
                }
            }
            var parts = new List<string>();
            if (runs == 0)
            {
                DateTime last;
                string ago = DateTime.TryParse(st.History[0].Date, System.Globalization.CultureInfo.InvariantCulture,
                                               System.Globalization.DateTimeStyles.None, out last)
                             ? Lang.T(((int)(DateTime.Now - last).TotalDays) + " days ago") : "";
                parts.Add(Lang.T("No runs yet this week.") + (ago.Length > 0 ? " " + Lang.T("Your last run was " + ago + ".") : ""));
            }
            else
            {
                parts.Add(Lang.T(runs + " runs, " + wins + " won"));
                if (bestRun != null)
                {
                    StatBlock cb = st.Class(bestRun.ClassKey);
                    StatRecord rec;
                    bool high = cb != null && cb.Best.TryGetValue("dps", out rec) && rec.Value <= bestRun.Dps + 0.5;
                    parts.Add(high ? Lang.T("new " + bestRun.Class + " DPS high: " + Big(bestRun.Dps))
                                   : Lang.T("best DPS " + Big(bestRun.Dps) + " (" + bestRun.Class + ")"));
                }
                string top = null;
                int most = 0;
                foreach (var kv in partners) if (kv.Value > most) { most = kv.Value; top = kv.Key; }
                if (top != null) parts.Add(Lang.T("most played with " + top));
            }
            return string.Join(" · ", parts.ToArray());
        }

        // ---- the News panel scrolls when the patch note is longer than its space
        static readonly Rectangle NewsArea = new Rectangle(694, 236, 386, 442 - 236);
        static readonly Rectangle NewsBar = new Rectangle(1080, 238, 6, 442 - 240);
        const int NewsTextW = 372;
        int newsScroll, newsHeight;
        bool newsDrag, newsBarHover;
        int newsDragOffset;

        Rectangle NewsThumb()
        {
            if (newsHeight <= NewsArea.Height) return Rectangle.Empty;
            int h = Math.Max(24, NewsBar.Height * NewsArea.Height / newsHeight);
            int y = NewsBar.Y + (NewsBar.Height - h) * newsScroll / Math.Max(1, newsHeight - NewsArea.Height);
            return new Rectangle(NewsBar.X, y, NewsBar.Width, h);
        }

        void ScrollNews(int to)
        {
            int max = Math.Max(0, newsHeight - NewsArea.Height);
            to = Math.Max(0, Math.Min(max, to));
            if (to == newsScroll) return;
            newsScroll = to;
            Invalidate(new Rectangle(NewsArea.X - 4, NewsArea.Y - 2, NewsBar.Right - NewsArea.X + 8, NewsArea.Height + 4));
        }

        public void Wheel(int delta)
        {
            Point p = PointToClient(Cursor.Position);
            Rectangle hot = Rectangle.Union(NewsArea, NewsBar);
            if (hot.Contains(p)) ScrollNews(newsScroll - delta / 120 * 40);
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            Rectangle t = NewsThumb();
            Rectangle bar = new Rectangle(NewsBar.X - 4, NewsBar.Y, NewsBar.Width + 8, NewsBar.Height);
            if (e.Button == MouseButtons.Left && !t.IsEmpty && bar.Contains(e.Location))
            {
                newsDrag = true;
                newsDragOffset = t.Contains(e.Location) ? e.Y - t.Y : t.Height / 2;
                DragNews(e.Y);
            }
            base.OnMouseDown(e);
        }

        void DragNews(int mouseY)
        {
            Rectangle t = NewsThumb();
            if (t.IsEmpty) return;
            int range = NewsBar.Height - t.Height;
            int top = Math.Max(0, Math.Min(range, mouseY - newsDragOffset - NewsBar.Y));
            ScrollNews(range <= 0 ? 0 : top * (newsHeight - NewsArea.Height) / range);
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            if (newsDrag) DragNews(e.Y);
            bool over = new Rectangle(NewsBar.X - 4, NewsBar.Y, NewsBar.Width + 8, NewsBar.Height).Contains(e.Location) && !NewsThumb().IsEmpty;
            if (over != newsBarHover)
            {
                newsBarHover = over;
                Cursor = over ? Cursors.Hand : Cursors.Default;
                Invalidate(new Rectangle(NewsBar.X - 2, NewsBar.Y, NewsBar.Width + 4, NewsBar.Height));
            }
            base.OnMouseMove(e);
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            if (newsDrag) { newsDrag = false; Invalidate(new Rectangle(NewsBar.X - 2, NewsBar.Y, NewsBar.Width + 4, NewsBar.Height)); }
            base.OnMouseUp(e);
        }

        public void SetStatus(string msg)
        {
            status = msg ?? "";
            if (status == "Ready." && Util.SetupPendingForThisInstall) status = "First Time Setup";
            Invalidate(new Rectangle(0, 578, W, H - 578));
        }

        // every message goes to the one status line above PLAY (there is no note in the bottom left any more)
        public void SetCloudNote(string text)
        {
            cloudNote = text ?? "";
            if (cloudNote.Length > 0) SetStatus(cloudNote);
        }

        // a section button is hidden while its feature is off: Cloud Save (cloud saving) and Stats (Options)
        bool NavShown(string name)
        {
            if (name == "Cloud Save") return false;         // opened from Options > CLOUD SAVE
            if (name == "Stats") return StatsEnabled();
            return true;
        }

        // stack the section buttons
        void LayoutNav()
        {
            int k = 0;                                       // three per row under "Launcher"
            foreach (string name in navNames)
            {
                if (name == "Changelog") continue;           // its place is on the News line
                if (!NavShown(name)) { nav[name].Visible = false; continue; }
                nav[name].Visible = true;
                nav[name].Location = new Point(693 + (k % 3) * 134, 480 + (k / 3) * 34);
                k++;
            }
            if (strip != null) LayoutStrip();
        }

        public void RefreshPlayLabel()
        {
            bool firstSetupPending = Util.SetupPendingForThisInstall;
            if (setupOpen || firstSetupPending) { playBtn.Label = "INSTALL"; playBtn.FontSize = 20; }
            else if (GameRunningFlag) { playBtn.Label = "PLAYING..."; playBtn.FontSize = 24; }
            else if (Installing) { playBtn.Label = "INSTALLING"; playBtn.FontSize = 18; }
            else if (Updating) { playBtn.Label = "UPDATING"; playBtn.FontSize = 20; }
            else if (ClientPresent()) { playBtn.Label = "PLAY"; playBtn.FontSize = 24; }
            else { playBtn.Label = "REQUIRES INSTALL"; playBtn.FontSize = 13; }
            playBtn.Tip = (setupOpen || firstSetupPending) ? "Click to continue to the next step of First Time Setup." :
                           Installing ? "Click to stop the game download." :
                           ClientPresent() ? "" : "Click to install Azeroth Survivors.";
            // Play stays illuminated and acts as the current slide's NEXT action during setup.
            playBtn.GrayedTip = "";
            playBtn.Grayed = false;
            Invalidate(new Rectangle(0, 578, W, H - 578));
            RefreshGates();
        }

        void SetSetupNavigationEnabled(bool enabled)
        {
            foreach (var b in nav.Values)
            {
                b.GrayedTip = enabled ? "" : "Finish first-time setup first.";
                b.Grayed = !enabled;
            }
            updateBtn.GrayedTip = enabled ? "" : "Finish first-time setup first.";
            updateBtn.Grayed = !enabled;
            aboutBtn.GrayedTip = enabled ? "" : "Finish first-time setup first.";
            aboutBtn.Grayed = !enabled;
            playBtn.GrayedTip = "";
            playBtn.Grayed = false;
            discordIcon.Enabled = enabled;
            supportHeart.Enabled = enabled;
            if (enabled) RefreshGates();
        }

        // Cloud Save and Addons are grayed out (and say why on hover) until the game is installed; every other button
        // (Guide, Changelog, Stats, Options, About, Discord, Update) works the same with or without the game
        const string CloudNeedsGame = "Cloud Save needs the game. Press INSTALL to download it first.";
        const string AddonsNeedGame = "Addons need the game. Press INSTALL to download it first.";

        public const string StatsNeedAddon = "Disabled when Azeroth Stats or saved stats are unavailable";

            // Azeroth Stats is preferred; a legacy AZS Stats installation remains readable during transition.
        public bool StatsAddonActive()
        {
            try
            {
                string p = Stats.AddonPath(AddonsDir());
                return p != null && !p.EndsWith(Addons.DisabledSuffix, StringComparison.OrdinalIgnoreCase);
            }
            catch (Exception) { return false; }
        }

        void RefreshGates()
        {
            bool statsOff = !StatsAddonActive();
            WowButton sb;
            if (nav.TryGetValue("Stats", out sb))
            {
                sb.GrayedTip = setupOpen ? "Finish first-time setup first." : StatsNeedAddon;
                sb.Grayed = setupOpen || statsOff;
            }
            bool off = !GateOpen();
            foreach (string name in new[] { "Cloud Save", "Addons" })
            {
                string tip = name == "Addons" ? AddonsNeedGame : CloudNeedsGame;
                WowButton b;
                if (nav.TryGetValue(name, out b))
                {
                    b.GrayedTip = setupOpen ? "Finish first-time setup first." : tip;
                    b.Grayed = setupOpen || off;
                }
            }
        }

        // call after cloud saving is turned on or off: show/hide every cloud reference
        public void ApplyCloudVisibility()
        {
            LayoutNav();
            if (!CloudEnabled()) SetCloudNote("");
            Invalidate();
            RebuildCurrent();
        }

        public void SetCloudEnabled(bool on)
        {
            Cfg.Set("cloud_enabled", on);
            Cfg.Save();
            ApplyCloudVisibility();
            if (on && Cfg.Str("cloud_dir").Trim().Length == 0) ChooseCloudDir();      // cloud on needs a sync folder
        }

        public void Notify(string msg)
        {
            SetStatus(Lang.T(msg));
        }

        bool wasMinimized;

        // back from the taskbar: the section shown again (the game's start may have closed it) and every part redrawn
        void RepaintAfterRestore()
        {
            if (!Visible || WindowState == FormWindowState.Minimized) return;
            try
            {
                if (side != null && !side.Visible && !setupOpen)
                {
                    if (current != null && current != "Home") { ShowSide(); BuildPanel(current); }
                    else ShowHomeCommunity();
                }
                Invalidate(true);
                Update();
            }
            catch (Exception) { }
        }

        public void ShowWindowFront()
        {
            Show();
            WindowState = FormWindowState.Normal;
            TopMost = true;
            Activate();
            BringToFront();
            After(200, delegate { TopMost = false; });
            if (current != null && current != "Home") ShowSide();
        }

        void OnClosingWindow(object sender, FormClosingEventArgs e)
        {
            if (quitting || e.CloseReason != CloseReason.UserClosing) return;
            e.Cancel = true;
            if (Installing)
            {
                ShowDownloadClosePrompt();
                return;
            }
            QuitApp();
        }

        public void QuitApp()
        {
            if (quitting) return;
            if (Installing)
            {
                ShowDownloadClosePrompt();
                return;
            }
            quitting = true;
            if (launcherUpdateCancel != null) launcherUpdateCancel.Set();
            StopEdge();
            StopLive3D();
            Application.Exit();
        }

        void CloseAfterCancellingGameDownload()
        {
            closeAfterDownloadCancel = true;
            if (dlActive && installCancel != null)
            {
                SetStatus("Stopping the game download before closing...");
                HideDownloadClosePrompt();
                installCancel.Set();
            }
            else if (Installing)
            {
                SetStatus("The game files are being installed. The launcher will close when that finishes.");
                HideDownloadClosePrompt();
            }
            else
            {
                closeAfterDownloadCancel = false;
                HideDownloadClosePrompt();
                QuitApp();
            }
        }

        void KeepGameDownloadRunning()
        {
            closeAfterDownloadCancel = false;
            HideDownloadClosePrompt();
            Activate();
        }

        // ------------------------------------------------------------------ first-time setup
        void MaybeFirstRun()
        {
            UpdateNewPlayerFlag();
            // Completion belongs to this executable location, not to movable/cloud-backed settings. A first run in
            // any new location must offer setup even when an old config says setup_done or game data already exists.
            bool markerForcesSetup = Util.SetupPendingForThisInstall;
            bool setupComplete = Util.SetupCompleteForThisInstall;
            if (setupAsked || setupOpen || PreviewMode || (!markerForcesSetup && setupComplete && Cfg.Bool("setup_done", false))) return;
            if (!Visible) return;                            // started hidden: asked when the window is first opened
            // Let the automatic launcher update finish first. The pending marker stays beside the exe across the swap,
            // so the updated launcher still enters setup before showing normal use.
            if (Updating || startupLauncherCheck)
            {
                After(500, MaybeFirstRun);
                return;
            }
            if (markerForcesSetup || !setupComplete)
            {
                // Leave the false marker where it is until setup succeeds, even if old settings claim setup was done.
                try { Util.EnsureSetupPendingMarker(); } catch (Exception) { }
                setupAsked = true;
                RunSetup();
                return;
            }
            setupAsked = true;
            if (AdoptExistingInstall()) return;
            RunSetup();
        }

        static bool SameFolder(string a, string b)
        {
            try { return string.Equals(Path.GetFullPath(a).TrimEnd('\\'), Path.GetFullPath(b).TrimEnd('\\'), StringComparison.OrdinalIgnoreCase); }
            catch (Exception) { return string.Equals(a, b, StringComparison.OrdinalIgnoreCase); }
        }

        // No settings of the launcher's own (its folder next to it is gone: a new copy of the exe, the folder moved or
        // deleted) does not mean a new player. When the game is here and has been played (its save or its config folder
        // exist), the launcher just carries on with them: setup is skipped, everything is in Options as usual.
        bool AdoptExistingInstall()
        {
            try
            {
                string game = GameDir();
                if (!File.Exists(Path.Combine(game, Util.GameExe))) return false;
                SyncSettings ss = SyncSettings();
                bool played = Directory.Exists(ss.SaveDir) && Directory.GetFileSystemEntries(ss.SaveDir).Length > 0;
                bool configured = Directory.Exists(ss.ConfigDir) && Directory.GetFileSystemEntries(ss.ConfigDir).Length > 0;
                if (!played && !configured) return false;
                Cfg.Set("setup_done", true);
                Cfg.Set("setup_folder", Util.LauncherDir);       // the folder this setup was for
                Cfg.Set("installed_by_launcher", true);        // the player's own copy counts as installed
                if (!Cfg.Has("cloud_enabled")) Cfg.Set("cloud_enabled", false);
                if (!Cfg.Has("stats_enabled")) Cfg.Set("stats_enabled", Stats.AddonPath(AddonsDir()) != null);
                Cfg.Save();
                SetStatus("Found your game and its saves: first-time setup skipped. Every setting is in Options.");
                RefreshGates();
                RefreshPlayLabel();
                Invalidate();
                return true;
            }
            catch (Exception) { return false; }
        }

        // First-time setup, and RUN SETUP AGAIN: the save, folders, cloud saving and addons questions. Nothing is
        // downloaded here: the game is only downloaded when the player presses INSTALL (and says they don't have it).
        public void RunSetup()
        {
            if (GameRunningFlag) { Info("Setup", Util.GameRunning() ? "Close the game first, then run setup again." : StillUploading); return; }
            if (Updating || Installing) { SetStatus("Please wait until the launcher update or the install has finished."); return; }
            ShowSetupDialog();
        }

        // INSTALL (no game yet): first asks whether the game is already on this PC. Yes: its folder is chosen and the
        // setup goes on as usual; no: the launcher downloads it.
        void AskHaveGame()
        {
            DialogResult r = Msg.Show(this, "Do you already have Azeroth Survivors downloaded on this PC?\n\n" +
                                            "YES: choose the folder it is in (the one with AzerothSurvivors.exe).\n" +
                                            "NO: the launcher downloads and installs it for you.",
                                      "Install", MessageBoxButtons.YesNoCancel);
            if (r == DialogResult.Yes)
            {
                if (!ChooseGameFolder()) return;
                if (!Cfg.Bool("setup_done", false)) After(200, ShowSetupDialog);
            }
            else if (r == DialogResult.No) InstallClient(!Cfg.Bool("setup_done", false));
        }

        // ------------------------------------------------------------------ cloud folder choices
        public string PickCloudDir(IWin32Window owner, string currentDir)
        {
            var clouds = SyncEngine.FindClouds();
            string start = !string.IsNullOrEmpty(currentDir) ? currentDir :
                           Cfg.Str("cloud_dir").Length > 0 ? Cfg.Str("cloud_dir") : (clouds.Count > 0 ? clouds[0].Value : Util.Home);
            string d = AskFolder(owner, "Select a folder inside your cloud drive (Google Drive, OneDrive, Dropbox ...)",
                                 Directory.Exists(start) ? start : Util.Home);
            if (d == null) return null;
            d = Path.GetFullPath(d);
            bool topLevel = Util.IsDriveRoot(d);
            foreach (var c in clouds) if (Util.SamePath(d, c.Value)) topLevel = true;
            if (topLevel && Msg.Show(owner, "That is the top level of your cloud drive.\n\nCreate and use a subfolder named " +
                                            "Azeroth Survivor Launcher inside it?", "Sync folder", MessageBoxButtons.YesNo) == DialogResult.Yes)
                d = Path.Combine(d, "Azeroth Survivor Launcher");
            return d;
        }

        public void ChooseCloudDir()
        {
            string d = PickCloudDir(this, null);
            if (d != null)
            {
                Cfg.Set("cloud_dir", d);
                Cfg.Save();
                RebuildCurrent();
            }
        }

        // ------------------------------------------------------------------ cloud sync runs
        SyncEngine MakeEngine()
        {
            var eng = new SyncEngine(SyncSettings());
            eng.Log = SyncLog;
            eng.Confirm = delegate(string msg)
            {
                return UiCall<bool>(delegate
                {
                    ShowWindowFront();
                    return Ask("Cloud Save", msg);
                });
            };
            eng.AskConflict = delegate(List<SyncSet> sets, bool downloading)
            {
                return UiCall<Choice>(delegate
                {
                    ShowWindowFront();
                    using (var dlg = new ConflictDialog(sets, downloading))
                    {
                        dlg.PlaceOver(this);
                        dlg.ShowModal(this);
                        return dlg.Result;
                    }
                });
            };
            return eng;
        }

        void SyncLog(string msg, Level level)
        {
            lock (cloudLog)
            {
                cloudLog.Add(new KeyValuePair<string, Level>(msg, level));
                if (cloudLog.Count > 400) cloudLog.RemoveRange(0, cloudLog.Count - 400);
            }
            try
            {
                Directory.CreateDirectory(Util.StateDir);
                string logf = Path.Combine(Util.StateDir, "log.txt");
                if (File.Exists(logf) && new FileInfo(logf).Length > 1000000)
                    Util.Replace(logf, Path.Combine(Util.StateDir, "log.old.txt"));
                File.AppendAllText(logf, DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss ", CultureInfo.InvariantCulture) + msg + "\r\n",
                                   Encoding.UTF8);
            }
            catch (Exception) { }
            Ui(delegate
            {
                if (logBox != null && !logBox.IsDisposed)
                {
                    UI.Append(logBox, Lang.T(msg) + "\n", UI.LevelColor(level), null);
                    logBox.ScrollToCaret();
                }
            });
        }

        public void RunSync(string kind, bool force, bool quiet)
        {
            RunSync(kind, force, quiet, null);
        }

        // kind: status, pull, push, or restore (stamp = which kept download)
        public void RunSync(string kind, bool force, bool quiet, string stamp)
        {
            if (SyncBusy || GameRunningFlag || Installing)
            {
                SetStatus("Another task is still running - wait for it to finish.");
                return;
            }
            if (!CloudEnabled()) return;
            if (!GateOpen())                                 // no cloud sync before the game is installed
            {
                if (quiet) Notify(CloudNeedsGame); else SetStatus(CloudNeedsGame);
                return;
            }
            if (Cfg.Str("cloud_dir").Trim().Length == 0)
            {
                string msg = "Choose a sync folder inside your cloud drive first (Cloud Save or Options).";
                if (quiet) Notify(msg); else Warn("Cloud Save", msg);
                return;
            }
            SyncBusy = true;
            SyncEngine eng = MakeEngine();
            Bg(delegate
            {
                bool ok = false;
                string err = null;
                try
                {
                    ok = kind == "status" ? eng.Status() : kind == "pull" ? eng.Pull(force) :
                         kind == "restore" ? eng.RestoreDownload(stamp) : eng.Push(force);
                }
                catch (Exception e)
                {
                    err = e.Message;
                    SyncLog("  " + err, Level.Error);
                }
                finally { eng.EndProgress(); }
                Ui(delegate { SyncDone(kind, ok, err, quiet); });
            });
        }

        void SyncDone(string kind, bool ok, string err, bool quiet)
        {
            SyncBusy = false;
            string stamp = DateTime.Now.ToString("HH:mm:ss", CultureInfo.InvariantCulture);
            string text = err != null ? "Cloud sync failed." : kind == "status" ? "Status checked at " + stamp :
                          kind == "restore" ? (ok ? "Earlier download restored." : "Cancelled, nothing changed.") :
                          ok ? (kind == "pull" ? "Downloaded from the cloud" : "Saves uploaded") + " at " + stamp
                             : "Not everything was synced - see the Cloud Save log.";
            SetStatus("Ready.");                             // cloud results stay in the Cloud Save log
            if (quiet) Notify(err ?? text);
            else if (err != null)
            {
                ShowWindowFront();
                Error("Cloud Save", err);
            }
            if (current == "Options" && optionsTab == 1 && !optionsLang) RebuildCurrent();   // Options > CLOUD SAVE
            // settings downloaded: the addons they belong to are installed if they are missing
            if (ok && err == null && (kind == "pull" || kind == "restore"))
                Bg(delegate
                {
                    if (InstallAddonsForSettings().Count > 0)
                        Ui(delegate { if (current == "Addons") BuildPanel("Addons"); });
                });
        }

        // Cloud Save syncs only the addons' settings: a Download More addon whose settings are here but whose folders
        // are not is installed (on a background thread). Returns the names installed.
        List<string> InstallAddonsForSettings()
        {
            var done = new List<string>();
            SyncSettings ss = SyncSettings();
            List<AddonEntry> missing;
            try { missing = Addons.MissingForSettings(ss.AddonsDir, ss.SavedDir, Data.Addons); }
            catch (Exception e) { SyncLog("  Addons for the synced settings not checked: " + e.Message, Level.Warn); return done; }
            foreach (var entry in missing)
            {
                try
                {
                    Directory.CreateDirectory(ss.AddonsDir);
                    List<string> names;
                    BuiltinAddon builtin = BuiltinAddons.Find(entry.Url);
                    if (builtin != null) { BuiltinAddons.Install(ss.AddonsDir, builtin); names = new List<string> { builtin.Folder }; }
                    else names = Archive.InstallAddonZip(entry.Url, ss.AddonsDir);
                    string name = entry.Name;
                    var rec = new List<object>();
                    foreach (string n in names) rec.Add(n);
                    Ui(delegate
                    {
                        AddonInstalls()[name] = rec;
                        AddonInstalledAt()[name] = DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture);
                        Cfg.Save();
                    });
                    SyncLog(name + " installed: its settings came from the cloud.", Level.Good);
                    done.Add(name);
                }
                catch (Exception e) { SyncLog("  " + entry.Name + " not installed: " + e.Message, Level.Warn); }
            }
            return done;
        }

        // ------------------------------------------------------------------ play: download newest -> run game -> upload
        void Play()
        {
            if (setupOpen || Util.SetupPendingForThisInstall)
            {
                if (!setupOpen)
                {
                    RunSetup();
                    if (!setupOpen) return;
                }
                if (setupFooterNext != null) setupFooterNext();
                else SetStatus("Choose an option on this setup step to continue.");
                return;
            }
            if (GameRunningFlag) return;
            if (Installing)
            {
                InstallClient();                             // asks whether to stop the download
                return;
            }
            if (Updating)
            {
                SetStatus("The launcher is updating itself. Please wait until that has finished.");
                return;
            }
            if (!ClientPresent())
            {
                RunSetup();
                if (setupOpen && setupPage == 0) SetupGo(1);   // REQUIRES INSTALL acts as NEXT on the welcome slide
                return;
            }
            // PLAY first makes sure this is the newest launcher: a newer one is installed (and the launcher restarts)
            // instead of starting the game; when GitHub can't be reached, the game starts as it is
            if (!playChecking && !StartedAfterUpdate && !PreviewMode && !Program.LocalDevelopmentBuild)
            {
                playChecking = true;
                SetStatus("Checking for a launcher update...");
                Bg(delegate
                {
                    LauncherRelease rel = null;
                    bool differs = false;
                    try { rel = SelfUpdate.FetchLatest(); differs = SelfUpdate.Differs(rel); }
                    catch (Exception) { }
                    Ui(delegate
                    {
                        playChecking = false;
                        if (differs && rel != null && !Updating)
                        {
                            launcherRelease = rel;
                            RefreshUpdateBtn();
                            SetStatus("A new launcher (" + rel.Name + ") is installed first; press PLAY again after it restarts.");
                            ForceLauncherUpdate();
                            return;
                        }
                        StartGame();
                    });
                });
                return;
            }
            if (playChecking) return;
            StartGame();
        }

        bool playChecking;                                   // PLAY is checking for a launcher update

        void StartGame()
        {
            // Final launch gate: even if a delayed update callback reaches this method directly, a marker
            // beside the running EXE keeps the game locked until first-time setup completes and moves it.
            if (Util.SetupPendingForThisInstall)
            {
                if (!setupOpen) RunSetup();
                RefreshPlayLabel();
                return;
            }
            if (GameRunningFlag || Installing || Updating || !ClientPresent()) return;
            string exe = Path.Combine(GameDir(), Util.GameExe);
            GameRunningFlag = true;
            RefreshPlayLabel();
            SyncEngine eng = (CloudEnabled() && Cfg.Str("cloud_dir").Trim().Length > 0) ? MakeEngine() : null;
            bool cloudOnButNoFolder = CloudEnabled() && eng == null;
            Bg(delegate { Session(exe, eng, cloudOnButNoFolder); });
        }

        // Keep a small local recovery copy of both new and legacy stats files before launching the game.
        static void BackupStats(string savedDir)
        {
            try
            {
                string dir = Path.Combine(Util.DataDir, "backups", "Stats");
                Directory.CreateDirectory(dir);
                string stamp = DateTime.Now.ToString("yyyy-MM-dd HH-mm-ss");
                foreach (var file in new[] { new[] { Stats.FileName, "AzerothStats" }, new[] { "AZSStats.lua", "AZSStats-legacy" } })
                {
                    string src = Path.Combine(savedDir ?? "", file[0]);
                    if (!File.Exists(src) || new FileInfo(src).Length < 100) continue;
                    File.Copy(src, Path.Combine(dir, file[1] + " " + stamp + ".lua"), true);
                    var files = new List<string>(Directory.GetFiles(dir, file[1] + " *.lua"));
                    files.Sort(StringComparer.Ordinal);
                    for (int i = 0; i < files.Count - 10; i++) { try { File.Delete(files[i]); } catch (Exception) { } }
                }
            }
            catch (Exception) { }
        }

        void Session(string exe, SyncEngine eng, bool cloudOnButNoFolder)
        {
            var problems = new List<string>();
            bool launched = false;
            Ui(delegate { SetCloudNote(""); });
            try
            {
                if (eng != null)
                {
                    bool complete;
                    try
                    {
                        complete = eng.Pull(false);          // asks only on a real conflict
                        eng.EndProgress();
                    }
                    catch (Exception e)
                    {
                        SyncLog("  " + e.Message, Level.Error);
                        problems.Add("The cloud save could NOT be loaded, so the game was not launched.\n\n" + e.Message);
                        Ui(delegate { SetCloudNote("Cloud sync failed - game not launched"); SetStatus("Ready."); });
                        return;
                    }
                    if (!complete)                           // cloud save not loaded -> never start the game
                    {
                        SyncLog("Cloud save was not loaded - game not launched.", Level.Error);
                        problems.Add("The cloud save could NOT be loaded, so the game was not launched.\n\nSee the log in " +
                                     "Cloud Save. If your cloud drive is still syncing, wait for it to finish and press PLAY again.");
                        Ui(delegate { SetCloudNote("Cloud sync failed - game not launched"); SetStatus("Ready."); });
                        return;
                    }
                    Ui(delegate { SetCloudNote(""); });
                    InstallAddonsForSettings();              // the addons whose settings came from the cloud
                }
                else if (cloudOnButNoFolder) problems.Add("No sync folder set - launched without cloud sync.");
                BackupStats(SyncSettings().SavedDir);
                // Azeroth Stats is installed and updated through Download More; the launcher does not overwrite
                // its files at game start. Legacy AZS Stats SavedVariables are read-only and are never removed.
                // the other AZS addons the launcher carries (AZS Handbook, AZS Minor Mods): the installed ones brought up
                // to the launcher's version too, and the retired ones (AZS Transparency) taken out
                try
                {
                    foreach (string f in BuiltinAddons.RemoveRetired(SyncSettings().AddonsDir, SyncSettings().SavedDir))
                        SyncLog(f + " addon removed (it is no longer part of the launcher).", Level.Info);
                    foreach (string f in BuiltinAddons.UpdateInstalled(SyncSettings().AddonsDir))
                        SyncLog(f + " addon updated to the launcher's version.", Level.Info);
                    foreach (string f in Addons.EnforceConflicts(SyncSettings().AddonsDir))
                        SyncLog(f + " addon switched off: it conflicts with another addon that is on.", Level.Info);
                }
                catch (Exception e) { SyncLog("  AZS addons not updated: " + e.Message, Level.Warn); }
                SyncLog("Starting Azeroth Survivors ...", Level.Info);
                Ui(delegate
                {
                    SetStatus("Game running.");
                });
                try
                {
                    var psi = new ProcessStartInfo(exe);
                    psi.WorkingDirectory = Path.GetDirectoryName(exe);
                    psi.UseShellExecute = false;
                    string gameDir = Path.GetDirectoryName(exe);
                    DateTime stamp = GameFilesStamp(gameDir);
                    sessionUpdated = false;
                    sessionCrashed = false;
                    using (Process p = Process.Start(psi))
                    {
                        launched = true;
                        p.WaitForExit();                     // returns on normal exit or crash
                        try { sessionCrashed = p.ExitCode != 0; } catch (Exception) { }
                    }
                    // The game updates itself when it starts: it closes and opens again (possibly through a helper in its
                    // folder). So after it closes, the launcher waits a moment for it to come back: 3 s after a plain
                    // close (a helper starts at once), up to 45 s when its files changed (it updated). Only when it stays
                    // closed is the session over; then the upload starts straight away.
                    while (true)
                    {
                        while (GameProcessRunning(gameDir)) Thread.Sleep(2000);
                        bool patched = GameFilesStamp(gameDir) > stamp;
                        if (patched) sessionUpdated = true;
                        if (patched) Ui(delegate { SetStatus("The game updated itself and is opening again..."); });
                        bool back = false;
                        for (int i = 0; i < (patched ? 90 : 6) && !back; i++)
                        {
                            Thread.Sleep(500);
                            back = GameProcessRunning(gameDir);
                        }
                        if (!back) break;
                        stamp = GameFilesStamp(gameDir);
                        SyncLog(patched ? "The game updated itself and opened again." : "The game opened again.", Level.Info);
                        Ui(delegate { SetStatus("Game running."); });
                    }
                }
                catch (Exception e)
                {
                    problems.Add("Could not start the game: " + e.Message);
                }
                if (launched)
                {
                    SyncLog("Game closed." + (sessionCrashed ? " (it did not close normally)" : ""), Level.Info);
                    if (eng == null) Ui(delegate { SetCloudNote("Game has closed."); });
                }
                if (launched && eng != null)
                {
                    try
                    {
                        if (eng.Push(false))
                            Ui(delegate { SetStatus("Ready."); SetCloudNote(""); });
                        else
                        {
                            problems.Add("Some of your progress was NOT uploaded. See the log in Cloud Save.");
                            Ui(delegate { SetCloudNote("Cloud sync NOT completed - see the Cloud Save log"); });
                        }
                    }
                    catch (Exception e)
                    {
                        SyncLog("  " + e.Message, Level.Error);
                        problems.Add("Cloud upload failed: " + e.Message);
                        Ui(delegate { SetCloudNote("Cloud sync failed - see the Cloud Save log"); });
                    }
                    eng.EndProgress();
                }
            }
            finally
            {
                Ui(delegate { SessionDone(problems); });
            }
        }

        // the newest change to the game's own files (its exe and its Data folder's .MPQ files): later after a session
        // means the game updated itself
        static DateTime GameFilesStamp(string gameDir)
        {
            DateTime newest = DateTime.MinValue;
            try
            {
                var files = new List<string>();
                files.Add(Path.Combine(gameDir, Util.GameExe));
                string data = Path.Combine(gameDir, "Data");
                if (Directory.Exists(data)) files.AddRange(Directory.GetFiles(data, "*.mpq"));
                foreach (string f in files)
                    try { if (File.Exists(f)) { DateTime t = File.GetLastWriteTimeUtc(f); if (t > newest) newest = t; } }
                    catch (Exception) { }
            }
            catch (Exception) { }
            return newest;
        }

        // the game, or any other program of the game's folder (an updater helper) is running: looked up by the names of
        // the .exe files in that folder and the folders right under it (fast: no need to open every running program)
        static bool GameProcessRunning(string gameDir)
        {
            if (Util.GameRunning()) return true;
            try
            {
                var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (string f in Directory.GetFiles(gameDir, "*.exe")) names.Add(Path.GetFileNameWithoutExtension(f));
                foreach (string d in Directory.GetDirectories(gameDir))
                    try { foreach (string f in Directory.GetFiles(d, "*.exe")) names.Add(Path.GetFileNameWithoutExtension(f)); }
                    catch (Exception) { }
                names.Remove(Path.GetFileNameWithoutExtension(Application.ExecutablePath));   // the launcher itself
                foreach (string n in names)
                {
                    Process[] found = Process.GetProcessesByName(n);
                    bool any = found.Length > 0;
                    foreach (Process pr in found) pr.Dispose();
                    if (any) return true;
                }
            }
            catch (Exception) { }
            return false;
        }

        // ------------------------------------------------------------------ play time
        // Duration of an entire player session when a legacy stats file supplies it. The new add-on records run time.
        public static string Duration(long seconds)
        {
            long h = seconds / 3600, m = seconds % 3600 / 60;
            return h > 0 ? h + "h " + m.ToString("00") + "m" : m + "m";
        }

        RunStats statsCache;
        string statsCacheKey = "";

        // Add-on stats, read again only when the saved-variable file changed.
        public RunStats AddonStats()
        {
            try
            {
                string f = Stats.FindFile(SyncSettings().SavedDir);
                if (f == null) { statsCache = null; statsCacheKey = ""; return null; }
                string key = f + "|" + File.GetLastWriteTimeUtc(f).Ticks;
                if (key != statsCacheKey)
                {
                    statsCache = Stats.Load(SyncSettings().SavedDir);
                    statsCacheKey = key;
                }
                return statsCache;
            }
            catch (Exception) { return null; }
        }

        // what the Home screen, Options and Stats show
        public PlayStats ShownPlayStats()
        {
            RunStats st = AddonStats();
            return st != null ? st.Play : new PlayStats();
        }

        // the game has closed, but its session is still finishing (the cloud upload)
        const string StillUploading = "The game has closed and your progress is still uploading to the cloud. Try again in a moment.";

        volatile bool sessionUpdated, sessionCrashed;

        void SessionDone(List<string> problems)
        {
            GameRunningFlag = false;
            After(3000, AutoUpdateAddons);                    // updates that waited for the game to close
            Invalidate();                                    // the play time the game just saved
            RefreshPlayLabel();
            // the launcher comes back up only when there is something to say: the game updated itself, it crashed, or
            // something went wrong (an upload). Otherwise the launcher stays visible after the game closes.
            if (problems.Count > 0 || sessionUpdated || sessionCrashed) ShowWindowFront();
            if (problems.Count > 0) Warn("Launcher", string.Join("\n\n", problems));
        }

        // ------------------------------------------------------------------ installing the game client
        void InstallClient() { InstallClient(false); }

        bool setupAfterInstall;
        bool dlActive;                                       // the game download is running: the status line shows its bar
        long dlDone, dlTotal;

        void SetDownloadBar(bool on, long done, long total)
        {
            dlActive = on;
            dlDone = done;
            dlTotal = total;
            if (closeDownloadCard != null && closeDownloadCard.Visible)
            {
                LayoutDownloadClosePrompt();
                closeDownloadCard.Invalidate();
            }
            Invalidate(new Rectangle(0, 578, W, H - 578));
        }

        ManualResetEvent installCancel;
        bool downloadInstall;

        // INSTALL: no window of its own. The download and the install run in the launcher's status line (a bar with the
        // percentage above INSTALLING); pressing INSTALLING asks whether to stop the download.
        void InstallClient(bool forSetup) { InstallClient(forSetup, GameDir()); }

        void InstallClient(bool forSetup, string destination)
        {
            if (Updating)
            {
                SetStatus("The launcher is updating itself. Please wait until that has finished.");
                return;
            }
            if (Installing)
            {
                if (installCancel != null && dlActive && Ask("Install", "Stop downloading the game?"))
                    installCancel.Set();
                return;
            }
            if (GameRunningFlag)
            {
                Info("Install", Util.GameRunning() ? "Close the game first, then install it again." : StillUploading);
                return;
            }
            setupAfterInstall = forSetup;
            BeginClientDownload(destination);
        }

        // The game download, done by the launcher: into <launcher folder>\temp, with the download server's login,
        // then unpacked next to the launcher; the temp folder is deleted afterwards, whatever happened.
        void BeginClientDownload(string destination)
        {
            Installing = true;
            RefreshPlayLabel();
            SetStatus("Downloading the game client...");
            SetDownloadBar(true, 0, 0);
            string temp = Path.Combine(Util.LauncherDir, Util.DownloadTempName);
            string dest = Path.GetFullPath(destination);
            installCancel = new ManualResetEvent(false);
            ManualResetEvent cancel = installCancel;
            Bg(delegate
            {
                string error = null;
                bool cancelled = false;
                try
                {
                    Util.DeleteTreeQuiet(temp);
                    Directory.CreateDirectory(temp);
                    string file = Path.Combine(temp, "AzerothSurvivors-client.download");
                    long last = -1;
                    int lastTick = 0;
                    Action<long, long> progress = delegate(long done, long total)
                    {
                        int pct = total > 0 ? (int)(done * 1000 / total) : (int)(done >> 20);
                        if (pct == last && Environment.TickCount - lastTick < 1000) return;
                        last = pct;
                        lastTick = Environment.TickCount;
                        Ui(delegate { SetDownloadBar(true, done, total); });
                    };
                    DownloadClient(file, progress, cancel, delegate(string text) { });
                    Ui(delegate { SetDownloadBar(false, 0, 0); SetStatus("Unpacking the game client..."); });
                    if (Archive.Kind(file) == null)
                        throw new UserError("The download server sent something that is not the game archive. Please try again later.");
                    Archive.InstallGame(file, dest);
                }
                catch (CancelledError) { cancelled = true; }
                catch (Exception e) { error = e.Message; }
                finally { Util.DeleteTreeQuiet(temp); }        // the download is never kept
                Ui(delegate
                {
                    SetDownloadBar(false, 0, 0);
                    installCancel = null;
                    if (cancelled)
                    {
                        Installing = false;
                        setupBrowseWhileInstalling = false;
                        setupAfterInstall = false;
                        RefreshPlayLabel();
                        SetStatus("Download cancelled.");
                        if (closeAfterDownloadCancel)
                        {
                            closeAfterDownloadCancel = false;
                            QuitApp();
                            return;
                        }
                        SetupDownloadCancelled();            // started from setup: setup is cancelled with it
                        return;
                    }
                    setupDownloading = false;                // (the download from setup has ended)
                    if (error != null)
                    {
                        Installing = false;
                        setupBrowseWhileInstalling = false;
                        setupAfterInstall = false;
                        RefreshPlayLabel();
                        SetStatus("Install failed.");
                        if (closeAfterDownloadCancel)
                        {
                            closeAfterDownloadCancel = false;
                            QuitApp();
                            return;
                        }
                        ShowWindowFront();
                        Error("Install", error);
                        return;
                    }
                    bool setup = setupAfterInstall || !Cfg.Bool("setup_done", false);
                    setupAfterInstall = false;
                    setupBrowseWhileInstalling = false;
                    downloadInstall = true;
                    try { InstallDone(null, dest); }
                    finally { downloadInstall = false; }
                    if (closeAfterDownloadCancel)
                    {
                        closeAfterDownloadCancel = false;
                        QuitApp();
                        return;
                    }
                    if (setup) After(300, ShowSetupDialog);  // first-time setup / RUN SETUP AGAIN: on to the folders page
                });
            });
        }

        static readonly Random hostDice = new Random();

        // One of the hosts at random; when it fails (not when you cancel), the next one.
        static void DownloadClient(string file, Action<long, long> progress, ManualResetEvent cancel, Action<string> say)
        {
            var hosts = new List<int> { 0, 1, 2 };            // 0 main, 1 backup, 2 Google Drive
            lock (hostDice)
                for (int i = hosts.Count - 1; i > 0; i--) { int j = hostDice.Next(i + 1); int t = hosts[i]; hosts[i] = hosts[j]; hosts[j] = t; }
            string[] names = { "main host", "backup host", "Google Drive host" };
            var errors = new List<string>();
            for (int i = 0; i < hosts.Count; i++)
            {
                int h = hosts[i];
                say(i == 0 ? "Downloading from the " + names[h] + "..."
                           : "The " + names[hosts[i - 1]] + " did not work. Trying the " + names[h] + "...");
                try
                {
                    if (h == 0) DownloadFromMain(file, progress, cancel);
                    else if (h == 1) DownloadFromMirror(file, progress, cancel);
                    else DownloadFromDrive(file, progress, cancel);
                    return;
                }
                catch (CancelledError) { throw; }
                catch (UserError e) { errors.Add(char.ToUpperInvariant(names[h][0]) + names[h].Substring(1) + ": " + e.Message); }
            }
            say("The other hosts did not work. Trying the game's own download page...");
            try { DownloadFromGameSite(file, progress, cancel); return; }
            catch (CancelledError) { throw; }
            catch (UserError e) { errors.Add("Game's download page: " + e.Message); }
            throw new UserError("No download host worked.\n\n" + string.Join("\n\n", errors.ToArray()));
        }

        // the downloaded game against its known checksums: a damaged or different file is deleted, never installed
        static void CheckClient(string file, string sha, string md5, bool drive, string where)
        {
            bool ok = drive ? string.Equals(md5, ClientDriveMd5, StringComparison.OrdinalIgnoreCase)
                              || string.Equals(sha, ClientSha256, StringComparison.OrdinalIgnoreCase)
                            : string.Equals(sha, ClientSha256, StringComparison.OrdinalIgnoreCase)
                              && (md5 == null || string.Equals(md5, ClientMd5, StringComparison.OrdinalIgnoreCase));
            if (ok) return;
            try { File.Delete(file); } catch (Exception) { }
            throw new UserError("The download from the " + where + " is damaged or not the right file (its checksum does not match). Please try again.");
        }

        // the main host, with the download login: sent the plain HTTP way (Basic); a server that wants it in the
        // address instead gets that next
        static void DownloadFromMain(string file, Action<long, long> progress, ManualResetEvent cancel)
        {
            string basic = Convert.ToBase64String(Encoding.UTF8.GetBytes(ClientUser + ":" + ClientPassword));
            Action<HttpWebRequest> login = delegate(HttpWebRequest r) { r.Headers["Authorization"] = "Basic " + basic; };
            string q = ClientUrl.Contains("?") ? "&" : "?";
            string u = Uri.EscapeDataString(ClientUser), pw = Uri.EscapeDataString(ClientPassword);
            string[] urls = { ClientUrl, ClientUrl + q + "username=" + u + "&password=" + pw, ClientUrl + q + "user=" + u + "&pass=" + pw };
            UserError lastError = null;
            foreach (string url in urls)
            {
                try
                {
                    string sha = Net.Download(url, file, true, "download", progress, cancel, 0, login);
                    CheckClient(file, sha, Net.LastMd5, false, "main host");
                    return;
                }
                catch (CancelledError) { throw; }
                catch (UserError e)
                {
                    lastError = e;
                    if (!e.Message.Contains("error 401") && !e.Message.Contains("error 403")) throw;   // only a refused login tries the next way
                }
            }
            throw new UserError("The download server did not accept the launcher's login." +
                                (lastError != null ? " (" + lastError.Message + ")" : ""));
        }

        // the last resort: the game's own download page (https://wow.aethus.pl/download)
        static void DownloadFromGameSite(string file, Action<long, long> progress, ManualResetEvent cancel)
        {
            string sha = Net.Download(ClientLastResortUrl, file, true, "game's download page", progress, cancel, 0);
            if (string.Equals(sha, ClientSha256, StringComparison.OrdinalIgnoreCase)) return;
            if (Archive.Kind(file) != null) return;
            try { File.Delete(file); } catch (Exception) { }
            throw new UserError("The game's download page did not give a game archive.");
        }

        // the backup host: its page names the newest archive (the link changes with every upload) and its SHA-256;
        // the download must match the game's known checksums (and the page's, when it shows one)
        static void DownloadFromMirror(string file, Action<long, long> progress, ManualResetEvent cancel)
        {
            string html;
            using (HttpWebResponse resp = Net.Open(ClientMirrorUrl, 30000, true, "backup download", "text/html"))
            using (var rd = new StreamReader(resp.GetResponseStream(), Encoding.UTF8))
                html = rd.ReadToEnd();
            var link = System.Text.RegularExpressions.Regex.Match(html,
                "href\\s*=\\s*[\"']([^\"']*/downloads/[^\"']+\\.(?:rar|zip|7z))[\"']", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
            if (!link.Success) throw new UserError("The backup host's page has no download link right now.");
            string url = new Uri(new Uri(ClientMirrorUrl), System.Net.WebUtility.HtmlDecode(link.Groups[1].Value)).AbsoluteUri;
            var sum = System.Text.RegularExpressions.Regex.Match(html, "SHA-?256.{0,300}?\\b([0-9a-fA-F]{64})\\b",
                System.Text.RegularExpressions.RegexOptions.IgnoreCase | System.Text.RegularExpressions.RegexOptions.Singleline);
            string sha = Net.Download(url, file, true, "backup download", progress, cancel, 0);
            if (sum.Success && !string.Equals(sum.Groups[1].Value, sha, StringComparison.OrdinalIgnoreCase))
            {
                try { File.Delete(file); } catch (Exception) { }
                throw new UserError("The download from the backup host is damaged (its checksum does not match). Please try again.");
            }
            CheckClient(file, sha, Net.LastMd5, false, "backup host");
        }

        // Google Drive: a large file first gives a "can't scan this file for viruses" page whose form holds the real
        // download address; that form is followed, then the file is checked against its MD5
        static void DownloadFromDrive(string file, Action<long, long> progress, ManualResetEvent cancel)
        {
            string url = "https://drive.usercontent.google.com/download?id=" + ClientDriveId + "&export=download&confirm=t";
            // only the first byte is asked for: the file itself is then fetched by the download below
            using (HttpWebResponse resp = Net.Open(url, 30000, true, "Google Drive download", null,
                                                   delegate(HttpWebRequest r) { r.AddRange(0L, 0L); }))
            {
                if ((resp.ContentType ?? "").ToLowerInvariant().Contains("text/html"))
                {
                    string html;
                    using (var rd = new StreamReader(resp.GetResponseStream(), Encoding.UTF8)) html = rd.ReadToEnd();
                    var form = System.Text.RegularExpressions.Regex.Match(html, "<form[^>]*action=\"([^\"]+)\"[^>]*>(.*?)</form>",
                        System.Text.RegularExpressions.RegexOptions.IgnoreCase | System.Text.RegularExpressions.RegexOptions.Singleline);
                    if (!form.Success) throw new UserError("Google Drive did not offer the file right now (it may have hit its download limit).");
                    var q = new List<string>();
                    foreach (System.Text.RegularExpressions.Match m in System.Text.RegularExpressions.Regex.Matches(form.Groups[2].Value,
                        "<input[^>]*name=\"([^\"]+)\"[^>]*value=\"([^\"]*)\"", System.Text.RegularExpressions.RegexOptions.IgnoreCase))
                        q.Add(Uri.EscapeDataString(System.Net.WebUtility.HtmlDecode(m.Groups[1].Value)) + "=" +
                              Uri.EscapeDataString(System.Net.WebUtility.HtmlDecode(m.Groups[2].Value)));
                    url = new Uri(new Uri(url), System.Net.WebUtility.HtmlDecode(form.Groups[1].Value)).AbsoluteUri;
                    url += (url.Contains("?") ? "&" : "?") + string.Join("&", q.ToArray());
                }
            }
            string sha = Net.Download(url, file, true, "Google Drive download", progress, cancel, 0);
            CheckClient(file, sha, Net.LastMd5, true, "Google Drive host");
        }

        bool setupAfterFileInstall;

        public void InstallFromDownloads() { InstallFromDownloads(false); }

        public void InstallFromDownloads(bool forSetup)
        {
            if (Updating || Installing) return;
            setupAfterFileInstall = forSetup;
            string archive = Archive.FindDownloaded();
            if (archive != null)
            {
                var fi = new FileInfo(archive);
                if (!Ask("Install", "Install the game from this file?\n\n" + fi.Name + "  (" + (fi.Length >> 20) + " MB)\nin " + fi.DirectoryName))
                    archive = null;
            }
            if (archive == null)
            {
                using (var d = new OpenFileDialog())
                {
                    d.Title = Lang.T("Choose the game file you downloaded (.rar)");
                    d.InitialDirectory = Util.DownloadsDir;
                    d.Filter = Lang.T("Game archive (*.rar;*.zip;*.7z)|*.rar;*.zip;*.7z|All files (*.*)|*.*");
                    if (d.ShowDialog(this) != DialogResult.OK)
                    {
                        SetStatus("Install cancelled.");
                        return;
                    }
                    archive = d.FileName;
                }
            }
            string dest = Util.LauncherDir;                  // installs next to the launcher, no folder question
            Installing = true;
            RefreshPlayLabel();
            SetStatus("Unpacking the game client...");
            Bg(delegate
            {
                string error = null;
                try
                {
                    Archive.InstallGame(archive, dest);
                    try { File.Delete(archive); } catch (Exception) { }     // the download is no longer needed
                }
                catch (Exception e) { error = e.Message; }
                Ui(delegate { InstallDone(error, dest); });
            });
        }

        void InstallDone(string error, string gameDir)
        {
            Installing = false;
            if (error == null)
            {
                Cfg.Set("game_dir", gameDir);
                Cfg.Set("installed_by_launcher", true);
                    UpdateNewPlayerFlag();
                Cfg.Save();
            }
            Invalidate();
            RebuildCurrent();
            if (error != null)
            {
                SetStatus("Install failed.");
                ShowWindowFront();
                Error("Install", error);
            }
            else
            {
                SetStatus("Install complete - press PLAY.");
                if (Cfg.Bool("stats_pending", false))        // setup opted into Azeroth Stats before the game was here
                {
                    Cfg.Remove("stats_pending");
                    Cfg.Save();
                    InstallStatsAddon();
                }
                Notify("Install complete. Press PLAY to enter Azeroth.");
                if (!downloadInstall && (setupAfterFileInstall || !Cfg.Bool("setup_done", false)))
                    After(300, ShowSetupDialog);             // installed from a file during setup: on to the folders page
                setupAfterFileInstall = false;
            }
        }

        // ------------------------------------------------------------------ the launcher's own updates (checked at start and with UPDATE)
        // The game updates itself when it starts (it closes and opens again): the launcher never updates the game.
        void RefreshUpdateBtn()
        {
            updateBtn.Label = UpdateChecking ? "CHECKING" : Updating ? "UPDATING" : "UPDATE LAUNCHER";
            updateBtn.Selected = launcherRelease != null && !Updating;   // gold: an update is waiting
        }

        void StartupUpdateCheck()
        {
            startupLauncherCheck = true;
            CheckLauncherUpdate(false);
            if (!UpdateChecking) startupLauncherCheck = false;   // the check could not start (another one is running)
        }

        // ---- the launcher itself: compared with the exe on the latest GitHub release ----
        LauncherRelease launcherRelease;                     // a different (newer) launcher on GitHub, when found
        bool launcherUpdateOfferPending;                      // manual update choices are shown inline in Options > Launcher
        bool startupLauncherCheck;                           // the start-up check is still waiting for GitHub

        // Started by a launcher update? Then a launcher that still differs is offered, not forced,
        // so a release whose exe never matches can't restart the launcher over and over.
        static readonly bool StartedAfterUpdate = Array.IndexOf(Environment.GetCommandLineArgs(), "--after-update") >= 0;

        // The UPDATE button and the start-up check: the launcher itself (the game updates itself when it starts).
        // At start-up it stays quiet unless there is something to offer; the button always answers.
        void CheckLauncherUpdate(bool manual)
        {
            if (Updating) return;
            if (UpdateChecking) return;
            UpdateChecking = true;
            RefreshUpdateBtn();
            if (manual) SetStatus("Checking for updates...");
            Bg(delegate
            {
                LauncherRelease rel = null;
                bool differs = false;
                string err = null;
                try
                {
                    rel = SelfUpdate.FetchLatest();
                    differs = SelfUpdate.Differs(rel);
                }
                catch (UserError e) { err = e.Message; }
                catch (Exception e) { err = "The update check failed (" + e.Message + ")."; }
                Ui(delegate { LauncherCheckDone(rel, differs, err, manual); });
            });
        }

        void LauncherCheckDone(LauncherRelease rel, bool differs, string err, bool manual)
        {
            UpdateChecking = false;
            startupLauncherCheck = false;
            launcherRelease = err == null && differs ? rel : null;
            if (launcherRelease == null) launcherUpdateOfferPending = false;
            RefreshUpdateBtn();
            if (!manual && launcherRelease != null && !StartedAfterUpdate)
            {
                ForceLauncherUpdate();                       // at start-up a newer launcher is installed without asking
                return;
            }
            if (err != null)
            {
                if (manual)
                {
                    SetStatus("Could not check for launcher updates.");
                }
            }
            else if (launcherRelease != null)
            {
                SetStatus("Launcher " + rel.Name + " is available.");
                OfferLauncherUpdate(manual);
                return;
            }
            else if (manual)
            {
                SetStatus("The launcher is up to date (" + rel.Name + ").");
            }
        }

        void OfferLauncherUpdate(bool manual)
        {
            LauncherRelease rel = launcherRelease;
            if (rel == null) return;
            if (!manual && setupOpen)
            {
                After(3000, delegate { OfferLauncherUpdate(false); });
                return;
            }
            if (!manual && (!Visible || WindowState == FormWindowState.Minimized))   // in the taskbar: the gold UPDATE
                return;                                      // button waits, quietly (no Windows notice)
            if (GameRunningFlag || Installing || SyncBusy || Updating)
            {
                if (manual) SetStatus(Lang.T("Finish the game, install, or cloud sync first, then check for the launcher update again."));
                return;
            }
            launcherUpdateOfferPending = true;
            SetStatus(Lang.F("Launcher {0} is ready. Choose Install update on the Options > Launcher page.", rel.Name));
            if (current == "Options" && optionsTab == 4 && !optionsLang && !optionsAbout) RebuildCurrent();
        }

        // The start-up update: no question. It waits while the game, an install or a cloud sync runs
        // (so nothing is cut off), then downloads, checks and restarts into the new launcher.
        void ForceLauncherUpdate()
        {
            LauncherRelease rel = launcherRelease;
            if (rel == null || Updating) return;
            if (GameRunningFlag || Installing || SyncBusy || UpdateChecking || setupOpen)
            {
                SetStatus("A launcher update (" + rel.Name + ") will be installed as soon as the game, an install or a cloud sync has finished.");
                After(5000, ForceLauncherUpdate);
                return;
            }
            SetStatus("Updating the launcher to " + rel.Name + "...");   // quietly: no Windows notice (Slimjim, 2026-10-28)
            DownloadLauncher(rel, true);
        }

        void DownloadLauncher(LauncherRelease rel, bool forced)
        {
            launcherUpdateOfferPending = false;
            Updating = true;
            if (launcherUpdateCancel != null) launcherUpdateCancel.Dispose();
            launcherUpdateCancel = new ManualResetEvent(false);
            ManualResetEvent cancel = launcherUpdateCancel;
            RefreshPlayLabel();
            RefreshUpdateBtn();
            SetStatus(forced ? "Updating the launcher to " + rel.Name + "..." : "Downloading the new launcher...");
            Bg(delegate
            {
                string err = null, path = null;
                DateTime last = DateTime.MinValue;
                try
                {
                    path = SelfUpdate.Download(rel, delegate(long done, long total)
                    {
                        if ((DateTime.Now - last).TotalSeconds < 0.2) return;
                        last = DateTime.Now;
                        string t = "Downloading the new launcher... " + (total > 0 ? (done * 100 / total) + "%" : Util.FmtSize(done));
                        Ui(delegate { SetStatus(t); });
                    }, cancel);
                }
                catch (Exception e) { err = e.Message; }
                Ui(delegate { LauncherDownloaded(path, err, forced); });
            });
        }

        void LauncherDownloaded(string path, string err, bool forced)
        {
            if (quitting) return;
            if (err == null)
            {
                try { SelfUpdate.InstallAndRestart(path); }        // the new one opens its window once this one is gone
                catch (Exception e) { err = e.Message; }
            }
            if (err != null)
            {
                Updating = false;
                RefreshPlayLabel();
                RefreshUpdateBtn();
                SetStatus("Launcher update failed.");
                if (forced)
                {
                    // no internet or GitHub down: don't lock the player out; the next start tries again
                    Error("Launcher update", err + "\n\nYou can keep using this version for now. The launcher tries again the next time it starts.");
                }
                else Error("Launcher update", err);
                return;
            }
            quitting = true;                                 // the new launcher is starting: close this one
            Application.Exit();
            // shut down completely even if a window or background thread holds on: the new launcher is waiting
            var kill = new System.Threading.Thread(delegate() { System.Threading.Thread.Sleep(3000); Environment.Exit(0); });
            kill.IsBackground = true;
            kill.Start();
        }
    }
}
