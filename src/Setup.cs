// First-time setup, inside the launcher's section window (no window of its own): a welcome, then six setup slides for
// language, save import, player type, optional existing-client folders, Azeroth Stats, addons and (for new installs) game download. Choices stay staged
// until the final slide. Setup cannot be closed before it is done: cancelling it shows a last page that
// says what the launcher is for, with START SETUP AGAIN. PLAY stays grayed out until setup is done.
// While it runs no Community video is shown; when it is done the home screen's Community comes back.
using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Windows.Forms;

namespace AzerothLauncher
{
    public partial class LauncherApp
    {
        // 0 welcome, 1 language, 2 website save, 3 player type, 4 existing-client folders, 5 Azeroth Stats, 6 addons, 7 game, 8 cancelled
        int setupPage;
        bool setupCloud, setupCloudFolderRequired;
        bool setupExistingClient;
        bool setupGameFolderRequired;
        bool? setupStats;                                     // the answer to the Azeroth Stats offer
        string setupAddons;                                   // "download" when the player wants addons, else null
        bool setupDownloading;                                // first-time game download is in progress
        bool setupBrowseWhileInstalling;                      // allow Download More during setup-triggered game download
        bool setupFirstTime;                                  // setup was never done before (not RUN SETUP AGAIN)
        string setupImported;
        string setupImportFile;                               // selected save is validated now, imported only when setup finishes
        string setupGameDir;
        string setupLanguage;
        Dictionary<string, string> setupVals, setupDefaults;
        Dictionary<string, object> setupResult;
        readonly HashSet<string> setupManualFolderDirs = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        Action setupFooterNext;
        string setupFooterNextLabel;

        // Setup keeps explicitly configured folders, but does not guess conventional save or addon locations.
        void ShowSetupDialog()
        {
            setupFirstTime = !Cfg.Bool("setup_done", false);
            // A genuinely new setup starts with cloud saving off. Preserve an existing player's explicit saved choice.
            setupCloud = setupFirstTime ? false : Cfg.Bool("cloud_enabled", false);
            setupCloudFolderRequired = false;
            setupExistingClient = false;
            setupGameFolderRequired = false;
            setupBrowseWhileInstalling = false;
            setupVals = new Dictionary<string, string>();
            setupDefaults = new Dictionary<string, string>();
            setupManualFolderDirs.Clear();
            // Keep local game files beside the launcher/game by default. Save data remains in the game's
            // fixed AppData location, independent of the cloud-sync choice.
            setupGameDir = GameDir();
            setupDefaults["addons_dir"] = Path.Combine(setupGameDir, "benilla-config", "AddOns");
            setupDefaults["saved_dir"] = Path.Combine(setupGameDir, "benilla-config", "saved");
            setupDefaults["save_dir"] = Util.DefaultSaveDir;
            setupVals["cloud_dir"] = Cfg.Str("cloud_dir");
            foreach (var kv in setupDefaults)
            {
                string configured = Cfg.Str(kv.Key);
                setupVals[kv.Key] = string.IsNullOrWhiteSpace(configured) ? kv.Value : configured;
                if (!string.IsNullOrWhiteSpace(configured)) setupManualFolderDirs.Add(kv.Key);
            }
            setupLanguage = Cfg.Str("language");
            if (string.IsNullOrWhiteSpace(setupLanguage)) setupLanguage = Lang.Code;
            Config.Frozen = false;                            // (after a cancelled first setup: saving again)
            // nothing else stays open while setup runs: the launcher's own message boxes and dialogs are closed
            try
            {
                var open = new List<Form>();
                foreach (Form f in Application.OpenForms) if (f is WowDialog) open.Add(f);
                foreach (Form f in open) f.Close();
            }
            catch (Exception) { }
            // The per-install marker decides whether setup is shown; the saved config only classifies cancellation
            // cleanup and preserves explicit choices for players who have already completed setup.
            setupImported = null;
            setupImportFile = null;
            setupResult = null;
            setupStats = null;
            setupAddons = null;
            setupPage = 0;
            setupOpen = true;
            current = "Setup";
            SetSetupNavigationEnabled(false);
            ApplyLanguage(setupLanguage, false);
            foreach (var b in nav.Values) b.Active = false;
            RefreshStrip();
            ShowSide();
            BuildPanel("Setup");
        }

        void SetupGo(int page)
        {
            // Final guard: an existing-client choice can never enter the download page. If its selected folder
            // no longer contains the game executable, return to the folder chooser instead.
            if (page == 7 && setupExistingClient)
            {
                if (!File.Exists(Path.Combine(setupGameDir, Util.GameExe)))
                {
                    setupGameFolderRequired = true;
                    page = 4;
                }
                else
                {
                    if (setupResult == null) setupResult = new Dictionary<string, object>();
                    setupResult["game_dir"] = Path.GetFullPath(setupGameDir);
                    setupResult["installed_by_launcher"] = true;
                    SetupLeave(setupAddons, setupStats);
                    return;
                }
            }
            setupPage = page;
            BuildPanel("Setup");
        }

        // The setup back arrow always moves exactly one slide; the welcome has no back action.
        void SetupBack()
        {
            if (setupPage > 0 && setupPage <= 8) SetupGo(setupPage - 1);
        }

        void SetupRestart() { ShowSetupDialog(); }

        static readonly string[] SetupSteps = { "First Time Setup", "Language", "Your save", "Player type", "Cloud & folders", "Azeroth Stats", "Addons", "Game" };

        void PanelSetup()
        {
            ClearPanel("Setup");
            setupFooterNext = null;
            setupFooterNextLabel = "NEXT ›";
            // Keep the restart control fixed at the bottom while each slide's content can scroll.
            int setupFooter = 52;
            var sp = MakeScroll(panel, new Rectangle(8, 4, PW - 16, PH - 12 - setupFooter), Theme.Panel, Theme.Panel);
            setupHeadW = 0;
            if (setupPage > 0 && setupPage <= 7)
            {
                int totalSteps = 6;
                int stepNumber = setupPage > 4 && !setupExistingClient ? setupPage - 1 : setupPage;
                var stepL = PutText(Lang.F("Step {0} of {1}", stepNumber, totalSteps), 10, FontStyle.Italic, Theme.GoldDim, 0, 8, 0);
                stepL.Left = PW - 24 - stepL.Width;
                stepL.BringToFront();
                setupHeadW = stepL.Width + 16;                    // the first heading stays clear of it
            }
            putInto = sp;
            try
            {
                int px = 16, pw = sp.ClientSize.Width - 32;
                int end;
                if (setupPage == 0) end = SetupWelcome(px, 6, pw);
                else if (setupPage == 1) end = SetupLanguage(px, 6, pw);
                else if (setupPage == 2) end = SetupSave(px, 6, pw);
                else if (setupPage == 3) end = SetupPlayerType(px, 6, pw);
                else if (setupPage == 4) end = SetupFolders(px, 6, pw);
                else if (setupPage == 5) end = SetupStats(px, 6, pw);
                else if (setupPage == 6) end = SetupAddons(px, 6, pw);
                else if (setupPage == 7) end = SetupDownload(px, 6, pw);
                else if (setupPage == 8) end = SetupAddonHandoff(px, 6, pw);
                else end = SetupCancelled(px, 6, pw);
                var pad = new Panel();
                pad.BackColor = Theme.Panel;
                pad.Bounds = new Rectangle(0, end, 1, 12);
                sp.Controls.Add(pad);
            }
            finally { putInto = null; }
            var cancel = new WowButton("CANCEL SETUP", SetupCancel, 142, 34, 9);
            cancel.Location = new Point(16, PH - 44);
            panel.Controls.Add(cancel);
            cancel.BringToFront();
            var restart = new WowButton("START OVER", SetupRestart, 142, 34, 9);
            restart.Location = new Point(cancel.Right + 8, PH - 44);
            panel.Controls.Add(restart);
            restart.BringToFront();
            if (setupPage == 0 || setupPage == 1)
                setupFooterNext = delegate { SetupGo(setupPage + 1); };
            else if (setupPage == 2)
            {
                setupFooterNext = delegate { SetupGo(3); };
                if (setupImported == null) setupFooterNextLabel = "I'M NEW HERE ›";
            }
            else if (setupPage == 4)
                setupFooterNext = SetupFinishFolders;
            if (setupFooterNext != null)
            {
                var next = new WowButton(setupFooterNextLabel, delegate { setupFooterNext(); }, 190, 38, 11);
                next.Location = new Point(PW - 16 - next.Width, PH - 48);
                panel.Controls.Add(next);
                next.BringToFront();
            }
        }

        int setupHeadW;                                       // room kept free for the step count beside a page heading

        int SetupWelcome(int x, int y, int w)
        {
            var title = PutText("First Time Setup", 15, FontStyle.Bold, Theme.Gold, x, y, w);
            var p = PutText("Thank you for using my community-made launcher. This is designed to help people celebrate how cool this game is and make a unifying one-stop-shop for all Azeroth Survivors needs\n\n" +
                            "During your first time setup please go through this next line of slides that will help set the game up for an existing user, or even help out if you are new\n\n" +
                            "Please follow this step by step guide to help get ready to play", 12, FontStyle.Regular, Theme.Text, x, BottomOf(title) + 12, w);
            return BottomOf(p);
        }

        // page 2: the language, every one the launcher speaks; the choice is shown at once
        int SetupLanguage(int x, int y, int w)
        {
            var q = PutText("Which language do you want the launcher in?", 13, FontStyle.Bold, Theme.Gold, x, y, w - setupHeadW);
            y = BottomOf(q) + 10;
            int bw = 150, gap = 8, cw = (int)(bw * Theme.Zoom), cols = Math.Max(1, (w + gap) / (cw + gap)), col = 0;
            foreach (var kv in Lang.Available())
            {
                string code = kv.Key;
                var b = new WowButton(kv.Value, delegate { SetLanguage(code); }, bw, 28, 9);
                b.Selected = code == Lang.Code;
                Put(b, x + col * (cw + gap), y);
                if (++col >= cols) { col = 0; y += (int)(28 * Theme.Zoom) + 6; }
            }
            if (col > 0) y += (int)(28 * Theme.Zoom) + 6;
            return y;
        }

        // page 3: moving over from the website? The save is imported before anything else
        int SetupSave(int x, int y, int w)
        {
            var q = PutText("Moving over from the website?", 13, FontStyle.Bold, Theme.Gold, x, y, w - setupHeadW);
            var p = PutText("Played Azeroth Survivors in your browser? Bring your progress with you: your heroes, the zones you opened, " +
                            "your gear, talents and codex.\n\nOn the website, open Options > Saves and press Export Save. Then choose " +
                            "that .sav file here. The game opens with your progress, ready to play.", 11, FontStyle.Regular, Theme.Text, x, BottomOf(q) + 4, w);
            y = BottomOf(p) + 14;
            if (setupImported != null)
            {
                var ok = PutText("✓ Save selected: " + setupImported + " (will import when setup finishes)", 11, FontStyle.Bold, Theme.Good, x, y, w, false);
                y = BottomOf(ok) + 12;
                var again = Put(new WowButton("IMPORT ANOTHER", SetupImport, 190, 38, 10), x, y);
                Put(new WowButton("CLEAR", delegate { setupImported = setupImportFile = null; BuildPanel("Setup"); }, 92, 38, 9), again.Right + 12, y);
                return again.Bottom;
            }
            var im = Put(new WowButton("IMPORT MY SAVE", SetupImport, 220, 42, 12), x, y);
            return im.Bottom;
        }

        // Existing client owners get the folder controls; browser players and new players skip them and install
        // directly beside the launcher. The game's own save location remains untouched.
        int SetupPlayerType(int x, int y, int w)
        {
            var q = PutText("Do you already have the Azeroth Survivors game client on this PC?", 13, FontStyle.Bold, Theme.Gold, x, y, w - setupHeadW);
            var p = PutText("If yes, setup will let you choose where the existing game and its AddOns are located. If no, the game will download into the folder containing this launcher.",
                            11, FontStyle.Regular, Theme.Text, x, BottomOf(q) + 4, w);
            y = BottomOf(p) + 14;
            var yes = Put(new WowButton("YES, I HAVE THE CLIENT", delegate
            {
                setupExistingClient = true;
                setupGameFolderRequired = !File.Exists(Path.Combine(setupGameDir, Util.GameExe));
                SetupGo(4);
            }, 250, 42, 11), x, y);
            Put(new WowButton("NO, DOWNLOAD IT", delegate
            {
                setupExistingClient = false;
                setupGameFolderRequired = false;
                setupGameDir = Util.LauncherDir;
                SetupGo(5);
            }, 220, 42, 11), yes.Right + 12, y);
            return yes.Bottom;
        }

        void SetupImport()
        {
            string saveDir = setupVals["save_dir"] ?? "";
            if (saveDir.Length == 0 || !Directory.Exists(saveDir))
            {
                saveDir = AskFolder(side, "Choose where to save the imported game progress", Util.Home);
                if (saveDir == null) return;
                setupVals["save_dir"] = Path.GetFullPath(saveDir);
            }
            using (var d = new OpenFileDialog())
            {
                d.Title = Lang.T("Choose the save you exported from the website (.sav)");
                d.Filter = Lang.T("Azeroth Survivors save (*.sav)|*.sav|All files (*.*)|*.*");
                string downloads = Path.Combine(Util.Home, "Downloads");
                if (Directory.Exists(downloads)) d.InitialDirectory = downloads;
                if (d.ShowDialog(side) != DialogResult.OK) return;
                try { SaveImport.Read(d.FileName); }
                catch (Exception e) { Msg.Show(side, e.Message, "Import save", MessageBoxButtons.OK, MessageBoxIcon.Error); return; }
                setupImportFile = d.FileName;
                setupImported = Path.GetFileName(d.FileName);
            }
            BuildPanel("Setup");
        }

        // page 4: cloud saving, and where the files are
        int SetupFolders(int x, int y, int w)
        {
            var a = PutText("A.  Cloud saving", 13, FontStyle.Bold, Theme.Gold, x, y, w - setupHeadW);
            y = BottomOf(a) + 4;
            var cb = UI.Check("Keep my progress in the cloud (Google Drive, OneDrive, Dropbox ...)\nand share it between my PCs",
                              setupCloud, Theme.Text, Theme.Panel, 11);
            cb.Size = VStack.Measure(cb);
            cb.CheckedChanged += delegate
            {
                setupCloud = cb.Checked;
                if (!setupCloud) setupCloudFolderRequired = false;
                BeginInvoke(new Action(delegate { BuildPanel("Setup"); }));
            };
            Put(cb, x - 4, y);
            y = BottomOf(cb) + 4;
            if (setupCloud) y = SetupFolderRow("Sync folder (inside your cloud drive)", "cloud_dir", delegate
            {
                string d = PickCloudDir(side, setupVals["cloud_dir"]);
                if (d != null) { setupVals["cloud_dir"] = d; setupCloudFolderRequired = false; BuildPanel("Setup"); }
            }, false, x, y, w);
            else
            {
                var off = PutText("Cloud saving stays off. You can turn it on later in Options > CLOUD SAVE.", 10, FontStyle.Italic, Theme.GoldDim, x, y, w);
                y = BottomOf(off) + 2;
            }
            if (setupCloud && setupCloudFolderRequired)
            {
                string message = (setupVals["cloud_dir"] ?? "").Trim().Length == 0
                    ? "Choose a sync folder, or turn off cloud saving to continue."
                    : "Choose your game saves folder, or turn off cloud saving to continue.";
                var required = PutText(message, 10, FontStyle.Bold, Theme.Bad, x, y + 2, w);
                y = BottomOf(required) + 4;
            }
            Put(UI.Line(Theme.Bronze, w, 1), x, y + 6);
            var b = PutText("B.  Where are your files?", 13, FontStyle.Bold, Theme.Gold, x, y + 14, w);
            var n = PutText("Defaults are filled in for you. Change a folder if your files live somewhere else.",
                            10, FontStyle.Regular, Theme.Text, x, BottomOf(b), w);
            y = BottomOf(n) + 4;
            if (setupExistingClient)
                y = SetupExistingGameFolderRow(x, y, w);
            y = SetupFolderRow("AddOns", "addons_dir", delegate { SetupPick("addons_dir", "Select your AddOns folder"); }, true, x, y, w);
            y = SetupFolderRow("Game saves", "save_dir", delegate
            {
                SetupPick("save_dir", "Select the folder that holds your game saves");
            }, true, x, y, w);
            y = SetupFolderRow("Addon settings", "saved_dir", delegate
            {
                SetupPick("saved_dir", "Select the folder that holds your addon settings");
            }, true, x, y, w);
            return y;
        }

        // Existing players must point setup at the directory that directly contains the game executable.
        // Recheck the exact executable before allowing them to continue, so this path cannot fall through to download.
        int SetupExistingGameFolderRow(int x, int y, int w)
        {
            bool found = !string.IsNullOrWhiteSpace(setupGameDir) && File.Exists(Path.Combine(setupGameDir, Util.GameExe));
            var name = PutText("Game client", 11, FontStyle.Bold, Theme.Gold, x, y, 0);
            string note = found ? "✓ found" : setupGameFolderRequired ? "choose the folder with AzerothSurvivors.exe" : "not found";
            PutText(note, 9, FontStyle.Italic, found ? Theme.Good : Theme.Bad, name.Right + 8, y + 3, 0);
            int btnW = (int)(100 * Theme.Zoom);
            var path = PutText(setupGameDir.Length > 0 ? setupGameDir : Lang.T("(not set)"), 9, FontStyle.Regular,
                               found ? Theme.LinkBlue : Theme.GoldDim, x, BottomOf(name), w - btnW - 12, false);
            Put(new WowButton(found ? "CHANGE" : "CHOOSE", delegate
            {
                string d = AskFolder(side, "Choose the folder containing AzerothSurvivors.exe",
                                     setupGameDir.Length > 0 && Directory.Exists(setupGameDir) ? setupGameDir : Util.Home);
                if (d == null) return;
                string previousGameDir = setupGameDir;
                setupGameDir = Path.GetFullPath(d);
                UpdateSetupFolderDefaults(previousGameDir, setupGameDir);
                setupGameFolderRequired = !File.Exists(Path.Combine(setupGameDir, Util.GameExe));
                BuildPanel("Setup");
            }, 100, 28, 9), x + w - btnW, y + Math.Max(0, (BottomOf(path) - y - (int)(28 * Theme.Zoom)) / 2));
            y = Math.Max(BottomOf(path), y + (int)(28 * Theme.Zoom)) + 4;
            if (setupGameFolderRequired)
            {
                var error = PutText("Choose the folder that contains the existing game client before continuing.",
                                    10, FontStyle.Bold, Theme.Bad, x, y, w);
                y = BottomOf(error) + 4;
            }
            return y;
        }

        int SetupFolderRow(string label, string key, Action chooser, bool defaultOk, int x, int y, int w)
        {
            string path = setupVals.ContainsKey(key) ? setupVals[key] ?? "" : "";
            bool found = path.Length > 0 && Directory.Exists(path);
            bool isDefault = setupDefaults.ContainsKey(key) && Util.SamePath(path, setupDefaults[key]);
            var name = PutText(label, 11, FontStyle.Bold, Theme.Gold, x, y, 0);
            string note = found ? (isDefault ? "✓ found" : "✓ chosen") : (path.Length == 0 ? "not chosen yet" : isDefault ? "Created if not found" : "not found - please choose it");
            PutText(note, 9, FontStyle.Italic, found ? Theme.Good : isDefault && path.Length > 0 ? Theme.GoldDim : Theme.Bad, name.Right + 8, y + 3, 0);
            int btnW = (int)(100 * Theme.Zoom);
            var pathL = PutText(path.Length > 0 ? path : Lang.T("(not set)"), 9, FontStyle.Regular, Theme.LinkBlue, x, BottomOf(name), w - btnW - 12, false);
            int h = BottomOf(pathL) - y;
            Put(new WowButton(found ? "CHANGE" : "CHOOSE", chooser, 100, 28, 9), x + w - btnW, y + Math.Max(0, (h - (int)(28 * Theme.Zoom)) / 2));
            return Math.Max(BottomOf(pathL), y + (int)(28 * Theme.Zoom)) + 6;
        }

        void SetupPick(string key, string title)
        {
            string cur = setupVals.ContainsKey(key) ? setupVals[key] ?? "" : "";
            string d = AskFolder(side, title, Directory.Exists(cur) ? cur :
                (cur.Length > 0 && Directory.Exists(Path.GetDirectoryName(cur)) ? Path.GetDirectoryName(cur) : Util.Home));
            if (d == null) return;
            setupVals[key] = Path.GetFullPath(d);
            setupManualFolderDirs.Add(key);
            BuildPanel("Setup");
        }

        void SetupFinishFolders()
        {
            if (setupExistingClient && !File.Exists(Path.Combine(setupGameDir, Util.GameExe)))
            {
                setupGameFolderRequired = true;
                BuildPanel("Setup");
                return;
            }
            if (setupCloud && (setupVals["cloud_dir"] ?? "").Trim().Length == 0)
            {
                setupCloudFolderRequired = true;
                BuildPanel("Setup");
                return;
            }
            if (setupCloud && (setupVals["save_dir"] ?? "").Trim().Length == 0)
            {
                setupCloudFolderRequired = true;
                BuildPanel("Setup");
                return;
            }
            setupCloudFolderRequired = false;
            var res = new Dictionary<string, object>();
            res["cloud_enabled"] = setupCloud;
            if (setupExistingClient)
            {
                res["game_dir"] = Path.GetFullPath(setupGameDir);
                res["installed_by_launcher"] = true;
            }
            if (setupCloud) res["cloud_dir"] = setupVals["cloud_dir"];
            foreach (string key in new[] { "addons_dir", "saved_dir", "save_dir" })
                if (!string.IsNullOrWhiteSpace(setupVals[key]) &&
                    (setupManualFolderDirs.Contains(key) || !Util.SamePath(setupVals[key], setupDefaults[key])))
                    res[key] = Path.GetFullPath(setupVals[key]);
            setupResult = res;
            SetupGo(5);
        }

        // page 5: Azeroth Stats, asked before the optional addons
        int SetupStats(int x, int y, int w)
        {
            var q = PutText("Do you want to track your stats in and out of the game?", 13, FontStyle.Bold, Theme.Gold, x, y, w - setupHeadW);
            var p = PutText("Azeroth Stats records runs and observed build changes in game (/azstats). The launcher can show saved " +
                            "run totals, classes, recent run reports, combat samples and the build journal. The addon's reports, " +
                            "comparisons and configurable in-game HUD remain available there.", 11, FontStyle.Regular, Theme.Text, x, BottomOf(q) + 4, w);
            y = BottomOf(p) + 14;
            var yes = Put(new WowButton("YES, TRACK MY STATS", delegate { setupStats = true; SetupGo(6); }, 250, 42, 12), x, y);
            Put(new WowButton("NO", delegate { setupStats = false; SetupGo(6); }, 140, 42, 12), yes.Right + 12, y);
            return yes.Bottom;
        }

        // page 6: the other addons, all optional
        int SetupAddons(int x, int y, int w)
        {
            string question = setupStats == true ? "Would you like to download more addons?" : "Would you like to download addons?";
            var q = PutText(question, 13, FontStyle.Bold, Theme.Gold, x, y, w - setupHeadW);
            var p = PutText("They are all optional: you choose each one you want in Addons > DOWNLOAD MORE.", 11, FontStyle.Regular, Theme.Text, x, BottomOf(q) + 4, w);
            y = BottomOf(p) + 14;
            var yes = Put(new WowButton("YES, ADDONS", delegate { setupAddons = "download"; SetupGo(8); }, 200, 42, 12), x, y);
            Put(new WowButton("NO THANKS", delegate { setupAddons = null; SetupNext(); }, 170, 42, 11), yes.Right + 12, y);
            return yes.Bottom;
        }

        int SetupAddonHandoff(int x, int y, int w)
        {
            bool alreadyInstalled = File.Exists(Path.Combine(setupGameDir, Util.GameExe));
            var title = PutText(alreadyInstalled ? "Browse Download More" : "Browse while the game downloads", 14,
                                FontStyle.Bold, Theme.Gold, x, y, w);
            string message = alreadyInstalled
                ? "Your game is already installed. Continue to Addons > DOWNLOAD MORE. The Guide will stay available for tips and tricks."
                : "The game will start downloading when you continue. You can browse Addons > DOWNLOAD MORE and the Guide for tips and tricks while it installs.";
            var body = PutText(message, 11, FontStyle.Regular, Theme.Text, x, BottomOf(title) + 8, w);
            y = BottomOf(body) + 16;
            var go = Put(new WowButton(alreadyInstalled ? "OPEN DOWNLOAD MORE" : "START DOWNLOAD & BROWSE", StartSetupAddonBrowse, 260, 40, 10), x, y);
            Put(new WowButton("BACK", delegate { SetupGo(6); }, 110, 40, 10), go.Right + 10, y);
            return go.Bottom;
        }

        void StartSetupAddonBrowse()
        {
            if (Updating || Installing || GameRunningFlag)
            {
                SetStatus("Finish the current update, install, or game session before continuing setup.");
                return;
            }
            bool alreadyInstalled = File.Exists(Path.Combine(setupGameDir, Util.GameExe));
            if (setupResult == null) setupResult = new Dictionary<string, object>();
            setupResult["cloud_enabled"] = setupCloud;
            setupResult["game_dir"] = Path.GetFullPath(setupGameDir);
            if (alreadyInstalled) setupResult["installed_by_launcher"] = true;
            if (alreadyInstalled)
            {
                SetupLeave("download", setupStats);
                return;
            }

            setupBrowseWhileInstalling = true;
            if (!SetupLeave("download", setupStats))
            {
                setupBrowseWhileInstalling = false;
                return;
            }
            setupDownloading = true;
            InstallClient(false, setupGameDir);
        }

        // The game location is always the sixth slide, whether the game is already installed or not.
        void SetupNext()
        {
            if (setupExistingClient)
            {
                if (!File.Exists(Path.Combine(setupGameDir, Util.GameExe)))
                {
                    setupGameFolderRequired = true;
                    SetupGo(4);
                    return;
                }
                if (setupResult == null) setupResult = new Dictionary<string, object>();
                setupResult["game_dir"] = Path.GetFullPath(setupGameDir);
                setupResult["installed_by_launcher"] = true;
                SetupLeave(setupAddons, setupStats);
                return;
            }
            SetupGo(7);
        }

        // slide 6: select the game folder, then explicitly start the download or finish with the existing game
        int SetupDownload(int x, int y, int w)
        {
            var q = PutText("Last step: download the game?", 13, FontStyle.Bold, Theme.Gold, x, y, w - setupHeadW);
            bool alreadyInstalled = setupGameDir.Length > 0 && File.Exists(Path.Combine(setupGameDir, Util.GameExe));
            var p = PutText(alreadyInstalled
                                ? "Your game is already in this folder. Finish setup to return to the launcher."
                                : setupExistingClient
                                    ? "Choose the folder for your game client. The download starts when you click DOWNLOAD GAME."
                                    : "The game will download into the folder containing this launcher when you click DOWNLOAD GAME.",
                            11, FontStyle.Regular, Theme.Text, x, BottomOf(q) + 4, w);
            y = BottomOf(p) + 12;
            string gamePathText = setupGameDir.Length > 0 ? setupGameDir : "No game folder selected";
            if (!setupExistingClient) gamePathText += " (game download location)";
            var path = PutText(gamePathText, 10, FontStyle.Italic,
                               setupGameDir.Length > 0 ? Theme.LinkBlue : Theme.GoldDim, x, y, setupExistingClient ? w - 120 : w);
            if (setupExistingClient) Put(new WowButton("CHOOSE FOLDER", delegate
            {
                string d = AskFolder(side, "Choose a folder for Azeroth Survivors", setupGameDir.Length > 0 && Directory.Exists(setupGameDir) ? setupGameDir : Util.Home);
                if (d == null) return;
                string previousGameDir = setupGameDir;
                setupGameDir = Path.GetFullPath(d);
                UpdateSetupFolderDefaults(previousGameDir, setupGameDir);
                BuildPanel("Setup");
            }, 110, 30, 9), x + w - (int)(110 * Theme.Zoom), y - 3);
            y = BottomOf(path) + 12;
            if (alreadyInstalled)
            {
                var existing = Put(new WowButton("FINISH SETUP", delegate
                {
                    if (setupResult == null) setupResult = new Dictionary<string, object>();
                    setupResult["cloud_enabled"] = setupCloud;
                    setupResult["game_dir"] = Path.GetFullPath(setupGameDir);
                    setupResult["installed_by_launcher"] = true;
                    SetupLeave(setupAddons, setupStats);
                }, 250, 40, 12), x, y);
                return existing.Bottom;
            }
            var dl = Put(new WowButton("DOWNLOAD THE GAME", delegate
            {
                if (setupGameDir.Length == 0)
                {
                    Warn("Game folder", "Choose a folder for the game first.");
                    return;
                }
                if (setupResult == null) setupResult = new Dictionary<string, object>();
                setupResult["cloud_enabled"] = setupCloud;
                setupResult["game_dir"] = Path.GetFullPath(setupGameDir);
                if (!SetupLeave(setupAddons, setupStats)) return;
                setupDownloading = true;
                InstallClient(false, setupGameDir);
            }, 270, 42, 11), x, y);
            dl.Label = "DOWNLOAD GAME";
            return dl.Bottom;
        }

        void UpdateSetupFolderDefaults(string previousGameDir, string newGameDir)
        {
            string oldConfig = Path.Combine(previousGameDir, "benilla-config");
            string newConfig = Path.Combine(newGameDir, "benilla-config");
            foreach (var pair in new[] { new[] { "addons_dir", "AddOns" }, new[] { "saved_dir", "saved" } })
            {
                string key = pair[0];
                if (setupManualFolderDirs.Contains(key)) continue;
                string oldPath = Path.Combine(oldConfig, pair[1]);
                string newPath = Path.Combine(newConfig, pair[1]);
                if (Util.SamePath(setupVals[key], oldPath)) setupVals[key] = newPath;
                setupDefaults[key] = newPath;
                if (setupResult != null) setupResult.Remove(key);
            }
        }

        // setup (or the game's download started from it) was cancelled. RUN SETUP AGAIN: back to the home screen with
        // the settings as they were. A first setup: the launcher takes away what it made (CleanUpFirstSetup) and the
        // section window stays up with the last page
        void SetupCancel()
        {
            if (!setupFirstTime)
            {
                setupResult = null;
                SetupLeave(null, null);
                return;
            }
            CleanUpFirstSetup();
            setupStats = null;
            setupAddons = null;
            Cfg.Set("setup_done", false);
            setupOpen = true;
            current = "Setup";
            foreach (var b in nav.Values) b.Active = false;
            RefreshStrip();
            ShowSide();
            RefreshPlayLabel();
            SetupGo(9);
        }

        // A cancelled first setup does not remove user folders or game data.
        void CleanUpFirstSetup()
        {
            Config.Frozen = true;
            // Remove only launcher-owned settings/state and this launcher's uniquely named partial-download folder.
            // DataDir also holds user content (saved addon profiles, downloaded addons, guide videos, and backups),
            // so never recursively delete it. The game, its save directory, addon folders, and cloud data are separate.
            var playerFolders = new List<string>();
            try
            {
                SyncSettings ss = SyncSettings();
                playerFolders.Add(ss.SaveDir);
                playerFolders.Add(ss.AddonsDir);
                playerFolders.Add(ss.SavedDir);
                playerFolders.Add(ss.CloudDir);
            }
            catch (Exception) { }
            if (setupVals != null)
                foreach (string key in new[] { "save_dir", "addons_dir", "saved_dir", "cloud_dir" })
                    if (setupVals.ContainsKey(key)) playerFolders.Add(setupVals[key]);

            foreach (string name in new[] { "config.json", "state.json", "log.txt", "log.old.txt" })
                try
                {
                    string file = Path.Combine(Util.DataDir, name);
                    if (File.Exists(file) && !IsInsidePlayerFolder(file, playerFolders)) File.Delete(file);
                }
                catch (Exception) { }
            string downloadTemp = Path.Combine(Util.LauncherDir, Util.DownloadTempName);
            if (!OverlapsPlayerFolder(downloadTemp, playerFolders))
                try { Util.DeleteTreeQuiet(downloadTemp); } catch (Exception) { }
        }

        static bool IsInsidePlayerFolder(string path, List<string> folders)
        {
            if (folders == null) return false;
            string full;
            try { full = Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar); }
            catch (Exception) { return true; }
            foreach (string folder in folders)
            {
                if (string.IsNullOrWhiteSpace(folder)) continue;
                string root;
                try { root = Path.GetFullPath(folder).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar); }
                catch (Exception) { continue; }
                if (full.Equals(root, StringComparison.OrdinalIgnoreCase) ||
                    full.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)) return true;
            }
            return false;
        }

        static bool OverlapsPlayerFolder(string path, List<string> folders)
        {
            if (folders == null) return false;
            foreach (string folder in folders)
            {
                if (string.IsNullOrWhiteSpace(folder)) continue;
                if (IsInsidePlayerFolder(path, new List<string> { folder }) ||
                    IsInsidePlayerFolder(folder, new List<string> { path })) return true;
            }
            return false;
        }

        // the game's download, started from setup, was stopped: setup is cancelled with it
        public void SetupDownloadCancelled()
        {
            if (!setupDownloading) return;
            setupDownloading = false;
            setupBrowseWhileInstalling = false;
            RefreshGates();
            RefreshPlayLabel();
            SetStatus("Game download cancelled. Choose INSTALL when you are ready to try again.");
        }

        // page 7: what the launcher is for, and the way back in
        int SetupCancelled(int x, int y, int w)
        {
            var q = PutText("Setup was cancelled", 13, FontStyle.Bold, Theme.Gold, x, y, w);
            var p = PutText("I'm sorry the launcher could not help this time. Everything it set up has been taken away again; your " +
                            "game, your saves and your cloud files are untouched.\n\nIf you need more help, please contact Slimjim " +
                            "in the Discord. Best of luck!", 11, FontStyle.Regular, Theme.Text, x, BottomOf(q) + 4, w);
            y = BottomOf(p) + 14;
            return BottomOf(p);
        }

        // setup ends: what was chosen is saved (nothing when it was left before the folders page: it is asked again next
        // time), then the Addons section or the home screen's Community
        bool SetupLeave(string go, bool? trackStats)
        {
            if (setupResult != null && !CommitSetupImport()) return false;
            setupOpen = false;
            SetSetupNavigationEnabled(true);
            current = null;
            if (setupResult != null)
            {
                if (!string.IsNullOrWhiteSpace(setupLanguage)) Cfg.Set("language", setupLanguage);
                foreach (var kv in setupResult) Cfg.Set(kv.Key, kv.Value);
                foreach (string key in new[] { "addons_dir", "saved_dir", "save_dir" })
                    if (!setupResult.ContainsKey(key)) Cfg.Remove(key);   // leave unset until the player chooses a folder
                Cfg.Set("setup_done", true);
                UpdateNewPlayerFlag();
                Cfg.Set("setup_folder", Util.LauncherDir);       // the folder this setup was for (a copy elsewhere sets up again)
                Cfg.Save();
                try { Util.CompleteSetupForThisInstall(); } catch (Exception) { }
                if (launcherRelease != null) After(500, ForceLauncherUpdate);
                RefreshPlayLabel();                           // PLAY is no longer grayed out
                SetStatus("Setup saved.");
                ApplyCloudVisibility();
                    if (trackStats.HasValue)                      // install Azeroth Stats only when the player opts in
                {
                    SetStatsEnabled(trackStats.Value);
                    if (trackStats.Value && ClientPresent()) InstallStatsAddon();
                    else if (trackStats.Value) { Cfg.Set("stats_pending", true); Cfg.Save(); }   // once the game is installed
                }
                if (go != null)
                {
                    addonMode = go;
                    ShowSection("Addons");
                    if (current == "Addons") return true;     // (Addons waits for the game: then the home screen)
                }
            }
            ShowSection("Community");
            return true;
        }

        bool CommitSetupImport()
        {
            if (string.IsNullOrWhiteSpace(setupImportFile)) return true;
            string saveDir = setupVals != null && setupVals.ContainsKey("save_dir") ? setupVals["save_dir"] : "";
            if (string.IsNullOrWhiteSpace(saveDir)) saveDir = Util.DefaultSaveDir;
            string existing = Path.Combine(saveDir, SaveImport.SaveFile);
            if (File.Exists(existing) && new FileInfo(existing).Length > 0)
            {
                if (Msg.Show(side, "This PC already has progress in the game.\n\nReplace it with " + Path.GetFileName(setupImportFile) +
                               "?\nWhat is there now is kept as a backup in the game's Saves folder.",
                               "Import save", MessageBoxButtons.YesNo) != DialogResult.Yes)
                {
                    setupImportFile = setupImported = null;
                    SetStatus("Existing save kept. Setup is still open; you can finish without importing it.");
                    return false;
                }
            }
            try
            {
                SaveImport.Import(setupImportFile, saveDir);
                Cfg.Set("save_imported", DateTime.UtcNow.ToString("o", System.Globalization.CultureInfo.InvariantCulture));
                return true;
            }
            catch (Exception e)
            {
                Msg.Show(side, "The save could not be imported:\n\n" + e.Message, "Import save", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return false;
            }
        }
    }
}
