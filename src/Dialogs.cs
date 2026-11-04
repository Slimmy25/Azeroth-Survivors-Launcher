// The pop-up windows: first-time setup, game install and cloud conflict.
using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Threading;
using System.Windows.Forms;

namespace AzerothLauncher
{
    // First-time setup: page 1 asks whether you are moving over from the website and imports your save (so the game opens
    // with your progress), page 2 is (A) cloud saving yes/no, (B) where AddOns, game saves and addon settings are, and
    // page 3 asks whether to go to the Addons section. Result = settings to store, or null when closed early.
    public class SetupDialog : WowDialog
    {
        readonly LauncherApp app;
        public Dictionary<string, object> Result;
        public string Goto;                                  // "download" / "installed" / null
        bool cloudOn;
        readonly Dictionary<string, string> vals = new Dictionary<string, string>();
        readonly Dictionary<string, string> defaults = new Dictionary<string, string>();

        public SetupDialog(LauncherApp app) : base("First-time setup")
        {
            this.app = app;
            SyncSettings ss = app.SyncSettings();
            cloudOn = app.Cfg.Bool("setup_done", false) ? app.CloudEnabled() : app.Cfg.Bool("cloud_enabled", true);
            vals["cloud_dir"] = app.Cfg.Str("cloud_dir");
            vals["addons_dir"] = ss.AddonsDir;
            vals["save_dir"] = ss.SaveDir;
            vals["saved_dir"] = ss.SavedDir;
            defaults["addons_dir"] = Path.Combine(ss.ConfigDir, "AddOns");
            defaults["saved_dir"] = Path.Combine(ss.ConfigDir, "saved");
            defaults["save_dir"] = Util.DefaultSaveDir;
            Page0();
        }

        string imported;                                     // the save imported on page 1 (its file name), or null

        // page 1: moving over from the website? Import the save first, before anything else
        void Page0()
        {
            BackAction = Close;                              // page 1: the back arrow leaves setup (same as LATER)
            Begin();
            Heading("Welcome, Adventurer", "Step 1 of 3", 26);
            Para("Moving over from the website?", 13, FontStyle.Bold, Theme.Gold, 26, 0, 0, 0);
            Para("Played Azeroth Survivors in your browser? Bring your progress with you: your heroes, the zones you opened, " +
                 "your gear, talents and codex.\n\nOn the website, open Options > Saves and press Export Save. Then choose " +
                 "that .sav file here. The game opens with your progress, ready to play.", 11, FontStyle.Regular, Theme.Text, 26, 4, 10, 0);
            if (imported != null)
            {
                Para("✓ Imported: " + imported, 11, FontStyle.Bold, Theme.Good, 26, 4, 4, 0);
                Body.Add(new HRow(12, B("NEXT", Page1, 170, 38, 12), B("IMPORT ANOTHER", ImportSave, 190, 38, 10)),
                         Align.Center, 26, 14, 26, 16);
            }
            else
                Body.Add(new HRow(12, B("IMPORT MY SAVE", ImportSave, 220, 42, 12), B("I'M NEW HERE", Page1, 190, 42, 11)),
                         Align.Center, 26, 14, 26, 16);
            Finish();
        }

        void ImportSave()
        {
            string saveDir = vals.ContainsKey("save_dir") && (vals["save_dir"] ?? "").Length > 0 ? vals["save_dir"] : Util.DefaultSaveDir;
            string name = app.ImportSave(this, saveDir);
            if (name == null) return;
            imported = name;
            Page0();
        }

        void Page1()
        {
            BackAction = Page0;                              // page 2: back to the import page
            Begin();
            Heading("Welcome, Adventurer", "Step 2 of 3", 26);
            Para("A.  Cloud saving", 13, FontStyle.Bold, Theme.Gold, 26, 0, 0, 0);
            var cb = UI.Check("Keep my progress in the cloud (Google Drive, OneDrive, Dropbox ...)\nand share it between my PCs",
                              cloudOn, Theme.Text, Theme.Panel, 11);
            cb.CheckedChanged += delegate { cloudOn = cb.Checked; BeginInvoke(new Action(Page1)); };
            Body.Add(cb, Align.Left, 22, 2, 26, 0);
            if (cloudOn) FolderRow("Sync folder (inside your cloud drive)", "cloud_dir", PickCloud, false);
            else Para("Cloud saving stays off. You can turn it on later in Cloud Save.", 10, FontStyle.Italic, Theme.GoldDim, 26, 2, 2, 0);
            Body.Add(UI.Line(Theme.Bronze, 10, 1), Align.Fill, 26, 10, 26, 6);
            Para("B.  Where are your files?", 13, FontStyle.Bold, Theme.Gold, 26, 0, 0, 0);
            Para("Folders found at their usual place are used as they are. Choose the ones that are missing.",
                 10, FontStyle.Regular, Theme.Text, 26, 0, 2, 0);
            FolderRow("AddOns", "addons_dir", delegate { Pick("addons_dir", "Select your AddOns folder"); }, true);
            FolderRow("Game saves", "save_dir", delegate
            {
                Pick("save_dir", "Select the folder that holds your save game (normally %APPDATA%\\wenilla-rogue)");
            }, true);
            FolderRow("Addon settings", "saved_dir", delegate
            {
                Pick("saved_dir", "Select your addon settings folder (the 'saved' folder in benilla-config)");
            }, true);
            Body.Add(new HRow(12, B("FINISH", FinishPage1, 170, 38, 12), B("LATER", Close, 120, 38, 11)),
                     Align.Center, 26, 14, 26, 16);
            Finish();
        }

        void FolderRow(string label, string key, Action chooser, bool defaultOk)
        {
            string path = vals.ContainsKey(key) ? vals[key] ?? "" : "";
            bool found = path.Length > 0 && Directory.Exists(path);
            var row = new Panel();
            row.BackColor = Theme.Panel;
            var name = UI.Text(label, 11, FontStyle.Bold, Theme.Gold, Theme.Panel, 0);
            string note = found ? (defaultOk ? "✓ found" : "✓ chosen") : (path.Length == 0 ? "not chosen yet" : "not found - please choose it");
            var noteL = UI.Text(note, 9, FontStyle.Italic, found ? Theme.Good : Theme.Bad, Theme.Panel, 0);
            var pathL = UI.Text(path.Length > 0 ? path : Lang.T("(not set)"), 9, FontStyle.Regular, Theme.LinkBlue, Theme.Panel, 470, false);
            var btn = B(found ? "CHANGE" : "CHOOSE", chooser, 100, 28, 9);
            Size ns = VStack.Measure(name), ps = VStack.Measure(pathL), os = VStack.Measure(noteL);
            int w = 600;
            name.Location = new Point(0, 0);
            noteL.Location = new Point(ns.Width + 10, ns.Height - os.Height - 1);
            pathL.Location = new Point(0, ns.Height);
            int h = Math.Max(ns.Height + ps.Height, 30);
            btn.Location = new Point(w - btn.Width, (h - btn.Height) / 2);
            row.Controls.Add(name);
            row.Controls.Add(noteL);
            row.Controls.Add(pathL);
            row.Controls.Add(btn);
            row.Size = new Size(w, h);
            Body.Add(row, Align.Left, 26, 4, 26, 0);
        }

        void Pick(string key, string title)
        {
            string cur = vals.ContainsKey(key) ? vals[key] ?? "" : "";
            string d = LauncherApp.AskFolder(this, title, Directory.Exists(cur) ? cur :
                (cur.Length > 0 && Directory.Exists(Path.GetDirectoryName(cur)) ? Path.GetDirectoryName(cur) : Util.Home));
            if (d != null)
            {
                vals[key] = Path.GetFullPath(d);
                Page1();
            }
        }

        void PickCloud()
        {
            string d = app.PickCloudDir(this, vals["cloud_dir"]);
            if (d != null)
            {
                vals["cloud_dir"] = d;
                Page1();
            }
        }

        void FinishPage1()
        {
            if (cloudOn && (vals["cloud_dir"] ?? "").Trim().Length == 0)
            {
                Msg.Show(this, "Choose a sync folder for cloud saving, or untick\n\"Keep my progress in the cloud\".", "Setup");
                return;
            }
            var missing = new List<string>();
            foreach (var kv in new[] { new[] { "AddOns", "addons_dir" }, new[] { "Game saves", "save_dir" }, new[] { "Addon settings", "saved_dir" } })
                if (!Directory.Exists(vals[kv[1]] ?? "")) missing.Add(kv[0]);
            if (missing.Count > 0 && Msg.Show(this, "These folders were not found:\n\n  " + string.Join("\n  ", missing) +
                    "\n\nFinish anyway? You can change them later in Options.", "Setup", MessageBoxButtons.YesNo) != DialogResult.Yes)
                return;
            var res = new Dictionary<string, object>();
            res["cloud_enabled"] = cloudOn;
            if (cloudOn) res["cloud_dir"] = vals["cloud_dir"];
            foreach (string key in new[] { "addons_dir", "saved_dir", "save_dir" })
                if (!Util.SamePath(vals[key], defaults[key])) res[key] = Path.GetFullPath(vals[key]);   // only stored when not the default
            Result = res;                                     // saved when this window closes
            Page2();
        }

        // page 3: addons. All of them are optional; if the player wants addons, Azeroth Stats is asked about first (before
        // the list), and is only turned on when they want their stats tracked in and out of the game.
        public bool? TrackStats;                              // the answer about Azeroth Stats (null: not asked)

        void Page2()
        {
            BackAction = Page1;                               // back to the folders page
            Begin();
            Heading("Addons", "Step 3 of 3", 26);
            Para("Your settings are saved.\n\nWould you like to download addons? They are all optional: " +
                 "you choose each one you want.", 12, FontStyle.Regular, Theme.Text, 26, 0, 14, 560);
            Body.Add(new HRow(12, B("YES, ADDONS", PageStats, 200, 42, 12), B("NO THANKS", delegate { Leave(null); }, 170, 42, 11)),
                     Align.Center, 26, 4, 26, 4);
            Body.Add(B("ENABLE / DISABLE MY ADDONS", delegate { Leave("installed"); }, 300, 34, 10), Align.Center, 26, 10, 26, 18);
            Finish();
        }

        void PageStats()
        {
            BackAction = Page2;
            Begin();
            Heading("Azeroth Stats", "Step 3 of 3", 26);
            Para("Do you want to track your stats in and out of the game?", 13, FontStyle.Bold, Theme.Gold, 26, 0, 4, 560);
            Para("Azeroth Stats records runs and build changes in game (/azstats). The launcher can show saved run totals, " +
                 "classes, recent run reports, combat samples and the observed build journal. Its own reports, comparisons " +
                 "and configurable in-game HUD remain available in the addon.", 11, FontStyle.Regular, Theme.Text, 26, 2, 12, 560);
            Body.Add(new HRow(12, B("YES, TRACK MY STATS", delegate { TrackStats = true; Leave("download"); }, 250, 42, 12),
                                  B("NO", delegate { TrackStats = false; Leave("download"); }, 140, 42, 12)),
                     Align.Center, 26, 4, 26, 18);
            Finish();
        }

        void Leave(string go)
        {
            Goto = go;
            Close();
        }
    }

    // One question for all areas in conflict: Use cloud / Keep local / Cancel.
    public class ConflictDialog : WowDialog
    {
        public Choice Result = Choice.Abort;

        public ConflictDialog(List<SyncSet> sets, bool downloading) : base("Which progress do you want to keep?")
        {
            BackAction = delegate { Done(Choice.Abort); };
            Begin();
            Body.Add(UI.Text("Both sides changed", 18, FontStyle.Bold, Theme.Gold, Theme.Panel, 0), Align.Center, 26, 4, 26, 4);
            Body.Add(UI.Line(Theme.GoldDim, 10, 2), Align.Fill, 26, 0, 26, 10);
            Para("This PC and the cloud both changed since the last sync:", 11, FontStyle.Regular, Theme.Text, 26, 0, 0, 0);
            foreach (var st in sets)
                Body.Add(UI.Text("• " + st.Label + ":  this PC " + SyncEngine.NewestTime(st.Dir, st.Exclude) +
                                 "   |   cloud " + SyncEngine.NewestTime(st.Cloud, st.Exclude), 10, FontStyle.Regular,
                                 Theme.LinkBlue, Theme.Panel, 0), Align.Left, 40, 1, 26, 1);
            Para("One complete state wins, the other is backed up first.\nFirst time on this PC? Choose USE CLOUD.\n" +
                 (downloading ? "USE CLOUD replaces your local data. KEEP LOCAL keeps it and uploads it later."
                              : "KEEP LOCAL replaces the cloud. USE CLOUD leaves the cloud and skips your upload."),
                 10, FontStyle.Italic, Theme.Text, 26, 10, 12, 0);
            Body.Add(new HRow(12, B("USE CLOUD", delegate { Done(Choice.Cloud); }, 150, 38, 11),
                                  B("KEEP LOCAL", delegate { Done(Choice.Local); }, 150, 38, 11),
                                  B("CANCEL", delegate { Done(Choice.Abort); }, 120, 38, 11)), Align.Center, 26, 0, 26, 16);
            Finish();
        }

        void Done(Choice c)
        {
            Result = c;
            Close();
        }
    }

    // gold progress bar; Pulse() = a block sweeping back and forth while the length of the work is not known
    public class ProgressBarGold : Control
    {
        double frac;
        int pulseX;
        System.Windows.Forms.Timer timer;

        public ProgressBarGold(int w, int h)
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer, true);
            Size = new Size(w, h);
        }

        public void Set(double f)
        {
            Stop();
            frac = f;
            Invalidate();
        }

        public void Pulse()
        {
            if (timer != null) return;
            timer = new System.Windows.Forms.Timer();
            timer.Interval = 45;
            timer.Tick += delegate { pulseX = (pulseX + 9) % (Width + 100); Invalidate(); };
            timer.Start();
        }

        public void Stop()
        {
            if (timer != null)
            {
                timer.Stop();
                timer.Dispose();
                timer = null;
            }
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing) Stop();
            base.Dispose(disposing);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.Clear(Theme.Ink);
            using (var b = new SolidBrush(Theme.Gold))
            {
                if (timer != null)
                {
                    int x0 = Math.Max(0, pulseX - 100), x1 = Math.Min(Width, pulseX);
                    if (x1 > x0) g.FillRectangle(b, x0, 0, x1 - x0, Height);
                }
                else g.FillRectangle(b, 0, 0, (int)(Width * frac), Height);
            }
            using (var p = new Pen(Theme.GoldDim, 1)) g.DrawRectangle(p, 0, 0, Width - 1, Height - 1);
        }
    }
}
