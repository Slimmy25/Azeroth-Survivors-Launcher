// The Community section: what the home screen shows whenever no other section is open. It features ONE of the
// players' build videos (the Guide's Build Guides topic in assets/guide.txt), picked at random and changed every
// 10 minutes, with its title and credit to the person who made it. Nothing plays by itself: a click plays it right
// here, inside the section's borders, like the New Player Guide's videos.
//
// Windows' old web view (which the Guide's own videos use) cannot play YouTube any more, so a YouTube video plays in
// Microsoft Edge (part of every Windows 10/11): Edge is started in app mode with a profile of its own (in the
// launcher's data folder), and its window is taken into the section as a child window, without its frame, sized
// to the section. Leaving the video (the back arrow, another section, closing the launcher) closes that Edge.
// Without Edge, the video opens in the web browser instead.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Windows.Forms;

namespace AzerothLauncher
{
    public partial class LauncherApp
    {
        static int communityPick = -1;                       // the featured video (index in BuildVideos)
        static DateTime communityPicked = DateTime.MinValue;
        GuideVideo communityPlaying;                         // the video playing in the section, or null
        Process edgeVideo;                                   // the Edge that plays it
        Panel edgeHost;                                      // where its window sits
        IntPtr edgeWnd = IntPtr.Zero;
        System.Windows.Forms.Timer communityTimer;
        const int RotateMinutes = 10;

        // every video of the Build Guides topic, newest first, and the topic's index
        static List<GuideVideo> BuildVideos(out int topicIndex)
        {
            topicIndex = -1;
            var list = new List<GuideVideo>();
            List<GuideTopic> topics;
            try { topics = Data.Guide; }
            catch (Exception) { return list; }
            for (int i = 0; i < topics.Count; i++)
            {
                if (!string.Equals(topics[i].Title, "Build Guides", StringComparison.OrdinalIgnoreCase)) continue;
                topicIndex = i;
                list.AddRange(topics[i].Videos);
                foreach (var p in topics[i].Pages) list.AddRange(p.Videos);
                break;
            }
            return list;
        }

        // The big streamers' videos (videos/big-streamers/videos.txt): the featured videos while Big Streamers is ticked
        // (the default), swapping every 10 minutes like the build videos do, and always first in MORE VIDEOS. A video with no title
        // gets its title and channel from YouTube (oEmbed) the first time it is shown. Every other video is a content
        // creator's (videos/content-creators).
        static GuideVideo[] headlines;

        static GuideVideo[] Headlines
        {
            get
            {
                if (headlines != null) return headlines;
                var list = new List<GuideVideo>();
                try
                {
                    foreach (string raw in Util.ReadResource("AS.big-streamers.txt").Replace("\r\n", "\n").Split('\n'))
                    {
                        string line = raw.Trim();
                        if (line.Length == 0 || line.StartsWith("#")) continue;
                        if (line.StartsWith("@")) continue;    // (an old "@until" date line: no longer used)
                        string[] f = line.Split('|');
                        var v = new GuideVideo { Url = f[0].Trim() };
                        if (f.Length > 1) v.Title = f[1].Trim();
                        if (f.Length > 2) v.Author = f[2].Trim();
                        if (f.Length > 3) v.AuthorUrl = f[3].Trim();
                        list.Add(v);
                    }
                }
                catch (Exception) { }
                headlines = list.ToArray();
                return headlines;
            }
        }

        // the featured video: a headline video while they run, otherwise a random build video; a different one
        // every 10 minutes (while nothing is playing)
        // the two boxes beside Community: 0 neither (as before), 1 Big Streamers only, 2 Content Creators only
        static int communityFilter = -1;
        System.Windows.Forms.Timer filterTimer;

        static GuideVideo Featured(List<GuideVideo> list)
        {
            if (communityFilter == 1 && Headlines.Length > 0) list = new List<GuideVideo>(Headlines);
            else if (communityFilter == 2)                    // the content creators: every video that is not a big streamer's
                list = list.FindAll(delegate(GuideVideo v) { return Array.IndexOf(Headlines, v) < 0; });
            if (list.Count == 0) return null;
            if (communityPick < 0 || communityPick >= list.Count || (DateTime.Now - communityPicked).TotalMinutes >= RotateMinutes)
            {
                var rnd = new Random();
                int pick = rnd.Next(list.Count);
                if (list.Count > 1 && pick == communityPick) pick = (pick + 1 + rnd.Next(list.Count - 1)) % list.Count;
                communityPick = pick;
                communityPicked = DateTime.Now;
            }
            return list[communityPick];
        }

        // fills in a video's missing title and channel from YouTube's oEmbed, then redraws Community
        static readonly HashSet<string> titleAsked = new HashSet<string>();

        static string JsonField(string json, string key)
        {
            var m = System.Text.RegularExpressions.Regex.Match(json, "\"" + key + "\"\\s*:\\s*\"((?:[^\"\\\\]|\\\\.)*)\"");
            if (!m.Success) return "";
            try { return System.Text.RegularExpressions.Regex.Unescape(m.Groups[1].Value); }
            catch (Exception) { return m.Groups[1].Value; }
        }

        void FetchTitle(GuideVideo v)
        {
            if (v.Title.Length > 0 || titleAsked.Contains(v.Url)) return;
            titleAsked.Add(v.Url);
            ThreadPool.QueueUserWorkItem(delegate
            {
                try
                {
                    var req = (System.Net.HttpWebRequest)System.Net.WebRequest.Create(
                        "https://www.youtube.com/oembed?format=json&url=" + Uri.EscapeDataString(WatchUrl(v)));
                    req.Timeout = 10000;
                    string json;
                    using (var r = req.GetResponse())
                    using (var sr = new StreamReader(r.GetResponseStream(), Encoding.UTF8)) json = sr.ReadToEnd();
                    string t = JsonField(json, "title"), a = JsonField(json, "author_name"), au = JsonField(json, "author_url");
                    Ui(delegate
                    {
                        if (t.Length > 0) v.Title = t;
                        if (v.Author.Length == 0) v.Author = a;
                        if (v.AuthorUrl.Length == 0) v.AuthorUrl = au;
                        if (current == "Community" && side.Visible && communityPlaying == null) BuildPanel("Community");   // also MORE VIDEOS
                    });
                }
                catch (Exception) { }
            });
        }

        // the home screen: Community, unless its X was pressed
        void ShowHomeCommunity()
        {
            if (side == null) return;
            ShowSection("Community");
        }

        void PanelCommunity()
        {
            if (communityTimer == null)                       // every minute: is it time for the next video?
            {
                communityTimer = new System.Windows.Forms.Timer();
                communityTimer.Interval = 60 * 1000;
                communityTimer.Tick += delegate
                {
                    if (current == "Community" && side.Visible && communityPlaying == null &&
                        (DateTime.Now - communityPicked).TotalMinutes >= RotateMinutes) BuildPanel("Community");
                };
                communityTimer.Start();
            }
            if (communityPlaying != null) { CommunityPlayer(communityPlaying); return; }
            if (communityMore) { CommunityMoreVideos(); return; }
            ClearPanel("Community");
            // Big Streamers is ticked at every start (Slimjim, 2026-10-28) until the player picks Content Creators; that choice is kept
            if (communityFilter < 0) communityFilter = Cfg.Str("community_filter") == "2" ? 2 : 1;
            int ti;
            GuideVideo v = Featured(BuildVideos(out ti));
            if (v != null) FetchTitle(v);
            // two boxes at the top: Big Streamers / Content Creators (one at a time, or neither). Five seconds after a
            // change, the featured video is picked again from that one's own shuffle
            int bx = 24;
            for (int k = 1; k <= 2; k++)
            {
                int which = k;
                var cb = UI.Check(k == 1 ? "Big Streamers" : "Content Creators", communityFilter == k, Theme.Gold, Theme.Panel, 10);
                cb.Size = VStack.Measure(cb);
                cb.CheckedChanged += delegate
                {
                    // always one box ticked, never neither (Slimjim, 2026-10-28): unticking the ticked one ticks it again
                    if (!((CheckBox)cb).Checked && communityFilter == which) { ((CheckBox)cb).Checked = true; return; }
                    if (((CheckBox)cb).Checked && communityFilter == which) return;   // re-ticked above: nothing changed
                    communityFilter = ((CheckBox)cb).Checked ? which : (communityFilter == which ? 0 : communityFilter);
                    Cfg.Set("community_filter", communityFilter);
                    Cfg.Save();
                    if (filterTimer == null)
                    {
                        filterTimer = new System.Windows.Forms.Timer();
                        filterTimer.Interval = 5000;
                        filterTimer.Tick += delegate
                        {
                            filterTimer.Stop();
                            communityPick = -1;               // a new pick, from the chosen shuffle
                            if (current == "Community" && side.Visible && communityPlaying == null && !communityMore) BuildPanel("Community");
                        };
                    }
                    filterTimer.Stop();
                    filterTimer.Start();
                    // the other box is unticked at once (only one at a time)
                    foreach (Control c in panel.Controls)
                        if (c is CheckBox && c != cb && ((CheckBox)c).Checked && communityFilter == which) ((CheckBox)c).Checked = false;
                };
                Put(cb, bx, 8);
                bx = cb.Right + 14;
            }
            int top = 8 + (int)(32 * Theme.Zoom) + 8;
            if (bx > PW - 20 - (int)(200 * Theme.Zoom)) top += (int)(32*Theme.Zoom)+8;          // (no line about whose video it is: the ticked box says it)
            if (v == null)
            {
                PutText("No community videos yet.", 11, FontStyle.Italic, Theme.GoldDim, 24, top + 8, 0);
                return;
            }
            // the video's title as the heading, the credit under it, then its picture with a play button
            var title = PutText(v.Title.Length > 0 ? v.Title : "Video", 13, FontStyle.Bold, Theme.Gold, 24, top, PW - 48, false);
            int y = BottomOf(title) + 2;
            if (v.Author.Length > 0)
            {
                var by = PutText(Lang.T("by") + " " + v.Author, 10.5f, FontStyle.Regular, Theme.Text, 24, y, 0, false);
                if (v.AuthorUrl.Length > 0)
                {
                    string au = v.AuthorUrl;
                    by.Cursor = Cursors.Hand;
                    by.Click += delegate { Util.OpenInShell(au); };
                }
                y = BottomOf(by) + 6;
            }
            int h = PH - 12 - y, w = Math.Min(PW - 48, h * 16 / 9);
            var pic = new VideoPoster(v, new Size(w, h));
            pic.Location = new Point((PW - w) / 2, y);
            pic.Click += delegate { PlayYouTube(v); };
            panel.Controls.Add(pic);
            pic.BringToFront();
            if (MoreVideos().Count > 1)
                Put(new WowButton("MORE VIDEOS ›", delegate { communityMore = true; moreOrder = null; BuildPanel("Community"); }, 200, 30, 9),
                    PW - 20 - (int)(200 * Theme.Zoom), bx > PW - 20 - (int)(200*Theme.Zoom) ? 8+(int)(32*Theme.Zoom)+4 : 8);
        }

        // MORE VIDEOS: the big streamers' videos first, always at the top in their own order, then all the
        // other videos in a random order, shuffled again each time MORE VIDEOS is opened; a click plays it here
        bool communityMore;
        List<GuideVideo> moreOrder;                          // the order shown, kept while the page is open

        static List<GuideVideo> MoreVideos()
        {
            // Community's two boxes decide what is listed: Big Streamers only theirs, Content Creators only the others
            var list = new List<GuideVideo>();
            if (communityFilter != 2) list.AddRange(Headlines);   // the big streamers: always first, in their order
            if (communityFilter == 1 && Headlines.Length > 0) return list;
            int ti;
            var rest = new List<GuideVideo>();
            foreach (var v in BuildVideos(out ti))
                if (!list.Contains(v) && !rest.Contains(v) && Array.IndexOf(Headlines, v) < 0) rest.Add(v);
            var rnd = new Random();
            for (int i = rest.Count - 1; i > 0; i--)
            {
                int j = rnd.Next(i + 1);
                var t = rest[i]; rest[i] = rest[j]; rest[j] = t;
            }
            list.AddRange(rest);
            return list;
        }

        // the videos watched for more than 45 seconds (their YouTube ids, kept in the settings): MORE VIDEOS marks
        // them "Watched!"
        const double WatchedSeconds = 45;
        HashSet<string> watched;

        bool IsWatched(GuideVideo v)
        {
            if (watched == null)
            {
                watched = new HashSet<string>(StringComparer.Ordinal);
                foreach (string id in (Cfg.Str("videos_watched") ?? "").Split(','))
                    if (id.Trim().Length > 0) watched.Add(id.Trim());
            }
            string vid = VideoCard.YouTubeIdOf(v.Url);
            return vid != null && watched.Contains(vid);
        }

        void MarkWatched(GuideVideo v)
        {
            string vid = VideoCard.YouTubeIdOf(v.Url);
            if (vid == null || IsWatched(v)) return;
            watched.Add(vid);
            Cfg.Set("videos_watched", string.Join(",", new List<string>(watched).ToArray()));
            if (!PreviewMode) Cfg.Save();
        }

        void CommunityMoreVideos()
        {
            ClearPanel("More Videos");
            if (moreOrder == null) moreOrder = MoreVideos();
            var list = moreOrder;
            foreach (var mv in list) FetchTitle(mv);
            int top = 8;
            var sp = MakeScroll(panel, new Rectangle(20, top, PW - 40, PH - 12 - top), Theme.Panel, Theme.Bronze);
            int gap=Theme.Px(12), cols=Math.Max(1,Math.Min(3,sp.ClientSize.Width/Theme.Px(210)));
            int cw = (sp.ClientSize.Width - 20 - gap * (cols - 1)) / cols, ph = cw * 9 / 16;
            int y = 6, rowH = 0;
            for (int i = 0; i < list.Count; i++)
            {
                GuideVideo v = list[i];
                int col = i % cols;
                if (col == 0 && i > 0) { y += rowH + gap; rowH = 0; }
                int x = 4 + col * (cw + gap);
                var pic = new VideoPoster(v, new Size(cw, ph));
                pic.Watched = IsWatched(v);
                pic.Location = new Point(x, y);
                pic.Click += delegate { PlayYouTube(v); };
                sp.Controls.Add(pic);
                var t = UI.Text(v.Title.Length > 0 ? v.Title : "Video", 9.5f, FontStyle.Bold, Theme.Gold, sp.BackColor, cw, false);
                t.Size = VStack.Measure(t);
                t.Location = new Point(x, y + ph + 4);
                t.Cursor = Cursors.Hand;
                t.Click += delegate { PlayYouTube(v); };
                sp.Controls.Add(t);
                int hh = ph + 4 + t.Height;
                if (v.Author.Length > 0)
                {
                    var by = UI.Text(Lang.T("by") + " " + v.Author, 8.5f, FontStyle.Regular, Theme.Text, sp.BackColor, cw, false);
                    by.Size = VStack.Measure(by);
                    by.Location = new Point(x, y + hh);
                    sp.Controls.Add(by);
                    hh += by.Height;
                }
                rowH = Math.Max(rowH, hh);
            }
            var pad = new Panel();
            pad.Bounds = new Rectangle(4, y + rowH, 1, 10);
            pad.BackColor = sp.BackColor;
            sp.Controls.Add(pad);
        }

        // the video playing, filling the whole section box, with an X over its corner to close it
        void CommunityPlayer(GuideVideo v)
        {
            ClearPanel(v.Title.Length > 0 ? v.Title : "Video");
            sideHead.Note = Lang.T("ADS ARE YOUTUBE. NOT ME.");   // only while a video plays (ClearPanel clears it)
            edgeHost = new Panel();
            edgeHost.BackColor = Color.Black;
            edgeHost.Bounds = new Rectangle(0, 0, PW, PH);
            Put(edgeHost, 0, 0);
            edgeHost.Size = new Size(PW, PH);
            var wait = new Label();
            wait.Text = Lang.T("Loading the video...");
            wait.ForeColor = Theme.GoldDim;
            wait.BackColor = Color.Black;
            wait.Font = Theme.F(10, FontStyle.Italic);
            wait.AutoSize = true;
            wait.Location = new Point(16, 14);
            edgeHost.Controls.Add(wait);
            edgeHost.Resize += delegate { FitEdge(); PlaceVideoClose(); };
            edgeHost.VisibleChanged += delegate { PlaceVideoClose(); };
            edgeHost.Disposed += delegate { if (videoClose != null && !videoClose.IsDisposed) videoClose.Hide(); };
            StartEdge(v);
            ShowVideoClose();
        }

        // ---- playing: Edge's window as a child of the section
        [DllImport("user32.dll")] static extern IntPtr SetParent(IntPtr child, IntPtr parent);
        [DllImport("user32.dll")] static extern int GetWindowLong(IntPtr hWnd, int index);
        [DllImport("user32.dll")] static extern int SetWindowLong(IntPtr hWnd, int index, int value);
        [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW", SetLastError = true)]
        static extern IntPtr SetWindowLongPointer64(IntPtr hWnd, int index, IntPtr value);
        [DllImport("user32.dll", EntryPoint = "SetWindowLongW", SetLastError = true)]
        static extern int SetWindowLongPointer32(IntPtr hWnd, int index, int value);
        [DllImport("user32.dll")] static extern bool MoveWindow(IntPtr hWnd, int x, int y, int w, int h, bool repaint);
        [DllImport("user32.dll")] static extern bool SetWindowPos(IntPtr hWnd, IntPtr after, int x, int y, int w, int h, uint flags);
        [DllImport("user32.dll")] static extern bool ShowWindow(IntPtr hWnd, int cmd);
        [DllImport("user32.dll")] static extern bool IsWindow(IntPtr hWnd);
        [DllImport("user32.dll")] static extern bool IsWindowVisible(IntPtr hWnd);
        [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint pid);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern int GetClassName(IntPtr hWnd, StringBuilder s, int max);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern int GetWindowTextLength(IntPtr hWnd);
        delegate bool EnumProc(IntPtr hWnd, IntPtr lp);
        [DllImport("user32.dll")] static extern bool EnumWindows(EnumProc cb, IntPtr lp);
        const int GWL_STYLE = -16, GWL_EXSTYLE = -20;
        const int WS_CHILD = 0x40000000, WS_VISIBLE = 0x10000000, WS_POPUP = unchecked((int)0x80000000), WS_CLIPSIBLINGS = 0x04000000;
        const int WS_CAPTION = 0x00C00000, WS_THICKFRAME = 0x00040000, WS_SYSMENU = 0x00080000;
        const int WS_MINIMIZEBOX = 0x00020000, WS_MAXIMIZEBOX = 0x00010000, WS_EX_APPWINDOW = 0x00040000;

        // GWLP_HWNDPARENT means the owner for a top-level popup. HWNDs are pointer-sized;
        // the style helpers above intentionally use 32-bit values and must not store one.
        static void SetNativeVideoOwner(IntPtr window, IntPtr owner)
        {
            if (IntPtr.Size == 8) SetWindowLongPointer64(window, -8, owner);
            else SetWindowLongPointer32(window, -8, owner.ToInt32());
        }

        // the video player's Edge window: a visible Edge window (Chrome's window class, with a title) of an msedge.exe
        // started with the player's own profile. Edge often hands its first process over to another one, so the
        // window is looked for by the profile, not by the process the launcher started.
        static IntPtr PlayerWindow(string profile, HashSet<IntPtr> before)
        {
            var pids = new HashSet<uint>();
            foreach (int id in PlayerProcesses(profile)) pids.Add((uint)id);
            IntPtr found = IntPtr.Zero;
            EnumWindows(delegate(IntPtr h, IntPtr lp)
            {
                if (!IsWindowVisible(h) || GetWindowTextLength(h) == 0 || before.Contains(h)) return true;
                var cls = new StringBuilder(64);
                GetClassName(h, cls, 64);
                if (cls.ToString() != "Chrome_WidgetWin_1") return true;
                uint wp;
                GetWindowThreadProcessId(h, out wp);
                if (!pids.Contains(wp)) return true;
                found = h;
                return false;
            }, IntPtr.Zero);
            return found;
        }

        // every Edge window there is now (so one that was already open is never taken)
        static HashSet<IntPtr> EdgeWindowsNow()
        {
            var set = new HashSet<IntPtr>();
            EnumWindows(delegate(IntPtr h, IntPtr lp)
            {
                var cls = new StringBuilder(64);
                GetClassName(h, cls, 64);
                if (cls.ToString() == "Chrome_WidgetWin_1") set.Add(h);
                return true;
            }, IntPtr.Zero);
            return set;
        }

        // the msedge.exe processes running with the player's profile
        static List<int> PlayerProcesses(string profile)
        {
            var list = new List<int>();
            try
            {
                using (var q = new System.Management.ManagementObjectSearcher(
                    "SELECT ProcessId, CommandLine FROM Win32_Process WHERE Name = 'msedge.exe'"))
                    foreach (System.Management.ManagementObject o in q.Get())
                    {
                        string cmd = Convert.ToString(o["CommandLine"]) ?? "";
                        if (cmd.IndexOf(profile, StringComparison.OrdinalIgnoreCase) >= 0) list.Add(Convert.ToInt32(o["ProcessId"]));
                    }
            }
            catch (Exception) { }
            return list;
        }

        // an Edge left over from an earlier launcher (it crashed) still holds the video player's profile: a new one
        // would hand the video to it and quit, so it is closed first
        static void KillOldPlayers(string profile)
        {
            try
            {
                using (var q = new System.Management.ManagementObjectSearcher(
                    "SELECT ProcessId, CommandLine FROM Win32_Process WHERE Name = 'msedge.exe'"))
                    foreach (System.Management.ManagementObject o in q.Get())
                    {
                        string cmd = Convert.ToString(o["CommandLine"]) ?? "";
                        if (cmd.IndexOf(profile, StringComparison.OrdinalIgnoreCase) < 0) continue;
                        try { Process.GetProcessById(Convert.ToInt32(o["ProcessId"])).Kill(); } catch (Exception) { }
                    }
            }
            catch (Exception) { }
        }

        static string EdgePath()
        {
            foreach (string k in new[] { "ProgramFiles(x86)", "ProgramFiles", "ProgramW6432", "LOCALAPPDATA" })
            {
                string root = Environment.GetEnvironmentVariable(k);
                if (string.IsNullOrEmpty(root)) continue;
                string p = Path.Combine(root, "Microsoft", "Edge", "Application", "msedge.exe");
                if (File.Exists(p)) return p;
            }
            return null;
        }

        // ---- Wowhead's live 3D viewer (the weapon glows): Wowhead's still pictures don't show enchant glows, its live
        // viewer does, and that needs a modern browser. It opens in an Edge window of its own (app mode, a profile of
        // its own beside the video player's), streamed like a video: nothing is kept but Edge's own cache. One at a time;
        // it closes with the launcher. Without Edge, the page opens in the web browser.
        Process live3D;

        bool OpenLive3D(GuideVideo v)
        {
            StopLive3D();
            string edge = EdgePath();
            if (edge == null) { Util.OpenInShell(v.Url); return true; }
            try
            {
                string profile = Path.Combine(Path.GetTempPath(), "AzerothLauncher-3D");
                live3D = Process.Start(edge, "--app=\"" + v.Url + "\" --user-data-dir=\"" + profile + "\"" +
                                             " --window-size=1100,800 --no-first-run --no-default-browser-check");
            }
            catch (Exception) { Util.OpenInShell(v.Url); }
            return true;
        }

        void StopLive3D()
        {
            try { if (live3D != null && !live3D.HasExited) live3D.Kill(); }
            catch (Exception) { }
            live3D = null;
        }

        static string WatchUrl(GuideVideo v)
        {
            string id = VideoCard.YouTubeIdOf(v.Url);
            return id != null ? "https://www.youtube.com/watch?v=" + id : v.Url;
        }

        // a click on a YouTube video (here or in the Guide): it plays in the Community section
        bool PlayYouTube(GuideVideo v)
        {
            if (EdgePath() == null) { Util.OpenInShell(WatchUrl(v)); return true; }
            communityPlaying = v;
            if (current == "Community" && side.Visible) BuildPanel("Community");
            else { current = null; ShowSection("Community"); }
            return true;
        }

        void StartEdge(GuideVideo v)
        {
            StopEdge();
            string edge = EdgePath();
            if (edge == null) return;
            string profile = Path.Combine(Util.DataDir, "video-player");
            Process p;
            KillOldPlayers(profile);
            var before = EdgeWindowsNow();
            int port = EdgeDevTools.FreePort();
            try
            {
                Directory.CreateDirectory(profile);
                var psi = new ProcessStartInfo(edge,
                    "--user-data-dir=\"" + profile + "\" --no-first-run --no-default-browser-check" +
                    // one --disable-features only (Edge keeps just the last one). A window of another program can't
                    // show Edge's GPU (DirectComposition) output, which left the video white: Edge draws in software.
                    " --disable-features=msEdgeSidebarV2,CalculateNativeWinOcclusion --disable-backgrounding-occluded-windows" +
                    " --disable-renderer-backgrounding --disable-gpu --disable-gpu-compositing --disable-direct-composition" +
                    " --autoplay-policy=no-user-gesture-required --remote-debugging-port=" + port +
                    " --window-position=-20000,-20000 --window-size=640,360 --app=\"" + WatchUrl(v) + "\"");
                psi.UseShellExecute = false;
                p = Process.Start(psi);
            }
            catch (Exception) { Util.OpenInShell(WatchUrl(v)); communityPlaying = null; return; }
            edgeVideo = p;
            edgeProfile = profile;
            // its window appears after a moment (often from another Edge process than the one started): then it is
            // moved into the section
            ThreadPool.QueueUserWorkItem(delegate
            {
                IntPtr hwnd = IntPtr.Zero;
                for (int i = 0; i < 150 && hwnd == IntPtr.Zero; i++)
                {
                    Thread.Sleep(i < 50 ? 60 : 150);
                    if (p != edgeVideo) return;               // stopped meanwhile
                    try { hwnd = PlayerWindow(profile, before); }
                    catch (Exception) { }
                }
                if (hwnd == IntPtr.Zero)
                {
                    // never seen: no stray window is left behind, and the section says so
                    if (p != edgeVideo) return;
                    KillOldPlayers(profile);
                    Ui(delegate
                    {
                        if (edgeHost == null || edgeHost.IsDisposed) return;
                        foreach (Control c in edgeHost.Controls)
                            if (c is Label) c.Text = Lang.T("The video could not be shown here. Press the back arrow and try again.");
                    });
                    return;
                }
                Ui(delegate { Adopt(p, hwnd); });
                WatchPage(p, port, v);
            });
        }

        // The page, through Edge's DevTools: YouTube's page is styled so only the player shows, filling the window,
        // and while the video is fullscreen, Edge's window leaves the section and covers the whole screen.
        const string PlayerOnly =
            "(function(){function add(){if(document.getElementById('azs-player-only'))return;" +
            "var s=document.createElement('style');s.id='azs-player-only';s.textContent=" +
            "'html,body{overflow:hidden!important;background:#000!important}" +
            "#masthead-container,ytd-masthead,#secondary,#below,#comments,#related,tp-yt-app-drawer,ytd-mini-guide-renderer,ytd-popup-container{display:none!important}" +
            "#movie_player{position:fixed!important;left:0!important;top:0!important;width:100vw!important;height:100vh!important;z-index:2147483646!important;background:#000!important}" +
            ".html5-video-container,.html5-video-container video{width:100vw!important;height:100vh!important;left:0!important;top:0!important}" +
            "video{object-fit:contain!important}';" +
            "(document.head||document.documentElement).appendChild(s);}" +
            "if(document.readyState==='loading')document.addEventListener('DOMContentLoaded',add);else add();" +
            // Esc (not fullscreen) asks the launcher to close the video: the key goes to Edge, so the page notes it
            "if(!window.__azsKey){window.__azsKey=1;document.addEventListener('keydown',function(e){if(e.key==='Escape'&&!document.fullscreenElement)window.__azsEsc=1;},true);}" +
            "var n=0,t=setInterval(function(){add();window.dispatchEvent(new Event('resize'));if(++n>20)clearInterval(t);},500);})();";

        // the page's state, as a number: 1 while fullscreen, plus 2 while its video is playing
        const string PageState =
            "(function(){var v=document.querySelector('video');" +
            "return (document.fullscreenElement?1:0)+((v&&!v.paused&&!v.ended&&v.readyState>2)?2:0)+(window.__azsEsc?4:0);})()";

        void WatchPage(Process p, int port, GuideVideo v)
        {
            var dt = new EdgeDevTools();
            try
            {
                for (int i = 0; i < 40 && !dt.Open; i++)
                {
                    if (p != edgeVideo) return;
                    if (!dt.Connect(port)) Thread.Sleep(250);
                }
                if (!dt.Open)
                {
                    Ui(delegate { if (p == edgeVideo) RevealEdge(); });   // nothing to wait for: shown at once
                    // no DevTools: the time the player stays open counts as watching
                    var opened = DateTime.Now;
                    while (p == edgeVideo && (DateTime.Now - opened).TotalSeconds < WatchedSeconds) Thread.Sleep(500);
                    if (p == edgeVideo) Ui(delegate { MarkWatched(v); });
                    return;
                }
                dt.OnEveryPage(PlayerOnly);
                dt.Eval(PlayerOnly);
                bool full = false, marked = false, shown = false;
                double played = 0;
                var started = DateTime.Now;
                while (p == edgeVideo && dt.Open)
                {
                    Thread.Sleep(shown ? 300 : 100);
                    string r = dt.Eval(PageState);
                    if (r == null) continue;
                    var m = System.Text.RegularExpressions.Regex.Match(r, "\"value\":(\\d)");
                    int state = m.Success ? m.Groups[1].Value[0] - '0' : 0;
                    // Edge's window stays hidden behind "Loading the video..." until the video (or its ad) plays, so the
                    // white page YouTube starts with is never seen; after 8 seconds it shows anyway (a click may be needed)
                    if (!shown && ((state & 2) != 0 || (DateTime.Now - started).TotalSeconds > 8))
                    {
                        shown = true;
                        Ui(delegate { if (p == edgeVideo) RevealEdge(); });
                    }
                    if ((state & 4) != 0) { Ui(delegate { if (p == edgeVideo) LeaveCommunityVideo(); }); return; }   // Esc
                    if ((state & 2) != 0 && !marked)
                    {
                        played += 0.3;                        // only the time the video really plays
                        if (played > WatchedSeconds) { marked = true; Ui(delegate { MarkWatched(v); }); }
                    }
                    bool now = (state & 1) != 0;
                    if (now == full) continue;
                    full = now;
                    bool f = now;
                    Ui(delegate { if (p == edgeVideo) VideoFullscreen(f); });
                }
            }
            finally { dt.Dispose(); }
        }

        [DllImport("user32.dll")] static extern bool SetForegroundWindow(IntPtr hWnd);
        bool videoFull;

        // the video's fullscreen: Edge's window becomes a top window covering the launcher's screen; back in the
        // section when it ends
        void VideoFullscreen(bool on)
        {
            if (edgeWnd == IntPtr.Zero || !IsWindow(edgeWnd) || edgeHost == null || edgeHost.IsDisposed) return;
            videoFull = on;
            if (on)
            {
                Rectangle sc = Screen.FromControl(edgeHost).Bounds;
                SetParent(edgeWnd, IntPtr.Zero);
                int style = GetWindowLong(edgeWnd, GWL_STYLE);
                style &= ~WS_CHILD;
                style |= WS_POPUP | WS_VISIBLE;
                SetWindowLong(edgeWnd, GWL_STYLE, style);
                SetNativeVideoOwner(edgeWnd, Handle);
                // Fullscreen stays above its launcher owner, but is not globally topmost.
                // Windows hides owned popups with their minimized owner; Alt+Tab can put
                // another application over this window without interrupting playback.
                SetWindowPos(edgeWnd, new IntPtr(-2), sc.X, sc.Y, sc.Width, sc.Height, 0x0020 | 0x0040);
                SetForegroundWindow(edgeWnd);
                PlaceVideoClose();
            }
            else
            {
                SetWindowPos(edgeWnd, new IntPtr(-2), 0, 0, 0, 0, 0x0001 | 0x0002);                     // not topmost
                SetNativeVideoOwner(edgeWnd, IntPtr.Zero);
                int style = GetWindowLong(edgeWnd, GWL_STYLE);
                style &= ~WS_POPUP;
                style |= WS_CHILD | WS_VISIBLE | WS_CLIPSIBLINGS;
                SetWindowLong(edgeWnd, GWL_STYLE, style);
                SetParent(edgeWnd, edgeHost.Handle);
                SetWindowPos(edgeWnd, IntPtr.Zero, 0, 0, 0, 0, 0x0001 | 0x0002 | 0x0004 | 0x0010 | 0x0020);
                FitEdge();
                PlaceVideoClose();
            }
        }

        string edgeProfile;                                   // the player's Edge profile (its processes are found by it)

        void Adopt(Process p, IntPtr hwnd)
        {
            if (p != edgeVideo || edgeHost == null || edgeHost.IsDisposed) { KillOldPlayers(edgeProfile ?? ""); return; }
            int style = GetWindowLong(hwnd, GWL_STYLE);
            style &= ~(WS_POPUP | WS_CAPTION | WS_THICKFRAME | WS_SYSMENU | WS_MINIMIZEBOX | WS_MAXIMIZEBOX);
            style |= WS_CHILD | WS_VISIBLE | WS_CLIPSIBLINGS;
            SetWindowLong(hwnd, GWL_STYLE, style);
            SetWindowLong(hwnd, GWL_EXSTYLE, GetWindowLong(hwnd, GWL_EXSTYLE) & ~WS_EX_APPWINDOW);   // no taskbar button
            SetParent(hwnd, edgeHost.Handle);
            SetWindowPos(hwnd, IntPtr.Zero, 0, 0, 0, 0, 0x0001 | 0x0002 | 0x0004 | 0x0010 | 0x0020);   // frame changed
            edgeWnd = hwnd;
            FitEdge();
            ShowWindow(hwnd, 0);                              // hidden until the video plays (RevealEdge): no white page
            ShowVideoClose();
        }

        // the video plays: Edge's window comes out from behind "Loading the video..."
        void RevealEdge()
        {
            if (edgeWnd == IntPtr.Zero || !IsWindow(edgeWnd) || edgeHost == null || edgeHost.IsDisposed) return;
            foreach (Control c in edgeHost.Controls) c.Visible = false;
            FitEdge();
            ShowWindow(edgeWnd, 5);
        }

        void FitEdge()
        {
            if (videoFull || edgeWnd == IntPtr.Zero || edgeHost == null || edgeHost.IsDisposed || !IsWindow(edgeWnd)) return;
            // Edge draws its own title bar inside the window: it is pushed up out of sight
            int bar;
            using (var g = edgeHost.CreateGraphics()) bar = (int)Math.Round(33 * g.DpiY / 96f);
            MoveWindow(edgeWnd, 0, -bar, edgeHost.ClientSize.Width, edgeHost.ClientSize.Height + bar, true);
        }

        // closes the playing video's Edge (leaving the video, another section, the launcher closing)
        void StopEdge()
        {
            if (videoClose != null && !videoClose.IsDisposed) videoClose.Hide();
            Process p = edgeVideo;
            edgeVideo = null;
            edgeWnd = IntPtr.Zero;
            videoFull = false;
            if (p == null) return;
            try { if (!p.HasExited) p.Kill(); } catch (Exception) { }
            try { p.Dispose(); } catch (Exception) { }
            // the Edge that took the video over, and its helpers
            string prof = edgeProfile;
            if (!string.IsNullOrEmpty(prof)) ThreadPool.QueueUserWorkItem(delegate { KillOldPlayers(prof); });
        }

        // the X over the video's top right corner: a small window of its own, so it stays above Edge's
        VideoClose videoClose;

        void ShowVideoClose()
        {
            if (edgeHost == null || edgeHost.IsDisposed) return;
            Form owner = edgeHost.TopLevelControl as Form;
            if (owner == null) return;
            if (videoClose == null || videoClose.IsDisposed)
            {
                videoClose = new VideoClose();
                videoClose.Pressed += delegate { LeaveCommunityVideo(); };
            }
            if (!placeHooked)
            {
                placeHooked = true;
                EventHandler again = delegate { PlaceVideoClose(); };
                owner.LocationChanged += again;
                owner.SizeChanged += again;
                owner.VisibleChanged += again;
                LocationChanged += again;
            }
            PlaceVideoClose();
        }

        bool placeHooked;

        void PlaceVideoClose()
        {
            if (videoClose == null || videoClose.IsDisposed) return;
            Form top = edgeHost != null && !edgeHost.IsDisposed ? edgeHost.TopLevelControl as Form : null;
            bool on = !videoFull && top != null && edgeVideo != null && edgeHost.Visible && top.Visible && top.WindowState != FormWindowState.Minimized;
            if (!on) { videoClose.Hide(); return; }
            videoClose.SetDisplayScale(Theme.DisplayScale);
            int inset = Theme.Px(10);
            Point p = edgeHost.PointToScreen(new Point(Math.Max(0, edgeHost.ClientSize.Width - videoClose.Width - inset), inset));
            videoClose.Location = p;
            if (!videoClose.Visible) videoClose.Show(top);
        }

        // Any Community X/back action lands on its featured-video home, never the prior More Videos list.
        bool LeaveCommunityVideo()
        {
            if (current != "Community" || (communityPlaying == null && !communityMore)) return false;
            communityPlaying = null;
            communityMore = false;
            moreOrder = null;
            StopEdge();
            BuildPanel("Community");
            ShowWindowFront();
            RestoreAfterCommunityVideoClose();
            return true;
        }

        void RestoreAfterCommunityVideoClose()
        {
            Action restoreIfMinimized = delegate
            {
                if (quitting || current != "Community" || setupOpen) return;
                if (!Visible || WindowState == FormWindowState.Minimized) ShowWindowFront();
            };

            // Edge is a separate process/window. Its exit and the cleanup of its helpers can briefly minimize
            // the owning launcher after the close click has already rebuilt the Community page.
            After(150, restoreIfMinimized);
            After(700, restoreIfMinimized);
        }
    }

    // the round X that closes a playing video
    public class VideoClose : Form
    {
        public event Action Pressed;
        bool hover;

        public VideoClose()
        {
            AutoScaleMode = AutoScaleMode.None;
            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.Manual;
            SetDisplayScale(Theme.DisplayScale);
            BackColor = Color.Black;
            TransparencyKey = Color.Magenta;
            Cursor = Cursors.Hand;
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer, true);
        }

        public void SetDisplayScale(float density)
        {
            int size = Math.Max(20, (int)Math.Round(34 * density));
            Size = new Size(size, size);
            Invalidate();
        }

        protected override bool ShowWithoutActivation { get { return true; } }

        protected override CreateParams CreateParams
        {
            get
            {
                CreateParams cp = base.CreateParams;
                cp.ExStyle |= 0x08000000 | 0x00000080;       // no activation, no Alt+Tab entry
                return cp;
            }
        }

        protected override void OnMouseEnter(EventArgs e) { hover = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { hover = false; Invalidate(); base.OnMouseLeave(e); }
        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            if (e.Button == MouseButtons.Left && ClientRectangle.Contains(e.Location)) WowButton.Fire(Pressed);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.Clear(Color.Magenta);
            g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.None;   // no pink fringe on the see-through corners
            float scale = Width / 34f;
            int inset = Math.Max(1, (int)Math.Round(scale));
            var r = new Rectangle(inset, inset, Width - 2 * inset - 1, Height - 2 * inset - 1);
            using (var b = new SolidBrush(hover ? Theme.Hex("#c8442f") : Color.FromArgb(30, 22, 18))) g.FillEllipse(b, r);
            using (var p = new Pen(Theme.Gold, 2f * scale)) g.DrawEllipse(p, r);
            int c = Width / 2, d = Math.Max(1, (int)Math.Round(6 * scale));
            using (var p = new Pen(Color.White, 2.5f * scale))
            {
                g.DrawLine(p, c - d, c - d, c + d, c + d);
                g.DrawLine(p, c - d, c + d, c + d, c - d);
            }
        }
    }

    // the featured video's picture with a play button (YouTube's picture, filling the box)
    public class VideoPoster : Control
    {
        readonly GuideVideo video;
        readonly string id;
        bool hover;
        public bool Watched;                                 // a "Watched!" tag over its top-left corner

        public VideoPoster(GuideVideo v, Size size)
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer |
                     ControlStyles.ResizeRedraw, true);
            video = v;
            id = VideoCard.YouTubeIdOf(v.Url);
            Size = size;
            Cursor = Cursors.Hand;
        }

        protected override void OnMouseEnter(EventArgs e) { hover = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { hover = false; Invalidate(); base.OnMouseLeave(e); }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.Clear(Parent != null ? Parent.BackColor : Theme.Panel);
            Theme.Smooth(g);
            var r = new Rectangle(0, 0, Width - 1, Height - 1);
            Image img = id != null ? VideoCard.YouTubeThumb(id, delegate
            {
                try { if (IsHandleCreated) BeginInvoke(new Action(Invalidate)); } catch (Exception) { }
            }) : null;
            using (var clip = Armory.Round(r, 8))
            {
                g.SetClip(clip);
                if (img != null)
                {
                    float s = Math.Max((float)Width / img.Width, Height / (img.Height * 0.75f));
                    float iw = img.Width * s, ih = img.Height * s;
                    Theme.Draw(g, img, (Width - iw) / 2, (Height - ih) / 2, iw, ih);
                    if (!hover) using (var dim = new SolidBrush(Color.FromArgb(50, 0, 0, 0))) g.FillRectangle(dim, r);
                }
                else
                    using (var lg = new System.Drawing.Drawing2D.LinearGradientBrush(r, Theme.Hex("#3b1f4a"), Theme.Hex("#120a18"), 60f))
                        g.FillRectangle(lg, r);
                g.ResetClip();
            }
            using (var pen = new Pen(hover ? Theme.Gold : Theme.Hex("#4a3a22"), hover ? 2f : 1f))
            using (var path = Armory.Round(r, 8)) g.DrawPath(pen, path);
            float pd = Math.Min(70, Height * 0.3f), cx = Width / 2f, cy = Height / 2f;
            using (var b = new SolidBrush(hover ? Theme.Hex("#c8442f") : Color.FromArgb(220, 30, 22, 18))) g.FillEllipse(b, cx - pd / 2, cy - pd / 2, pd, pd);
            using (var pen = new Pen(Theme.Gold, 2f)) g.DrawEllipse(pen, cx - pd / 2, cy - pd / 2, pd, pd);
            float t = pd * 0.32f;
            using (var b = new SolidBrush(Color.White))
                g.FillPolygon(b, new[] { new PointF(cx - t * 0.7f, cy - t), new PointF(cx - t * 0.7f, cy + t), new PointF(cx + t, cy) });
            if (Watched)
            {
                string tag = Lang.T("Watched!");
                Font f = Theme.F(9, FontStyle.Bold);                 // a cached font: not disposed here
                {
                    SizeF ts = g.MeasureString(tag, f);
                    var tr = new Rectangle(8, 8, (int)ts.Width + 14, (int)ts.Height + 6);
                    using (var path = Armory.Round(tr, 6))
                    {
                        using (var b = new SolidBrush(Color.FromArgb(225, 24, 70, 30))) g.FillPath(b, path);
                        using (var pen = new Pen(Theme.Gold, 1f)) g.DrawPath(pen, path);
                    }
                    using (var b = new SolidBrush(Color.White))
                        g.DrawString(tag, f, b, tr.X + (tr.Width - ts.Width) / 2f, tr.Y + (tr.Height - ts.Height) / 2f);
                }
            }
        }
    }
}
