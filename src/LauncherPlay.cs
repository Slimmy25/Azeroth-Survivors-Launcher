// The main launcher window, continued: PLAY (cloud download -> game -> cloud upload) and play time.
// Moved out of Launcher.cs unchanged; it is the same LauncherApp class.
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
    public partial class LauncherApp
    {
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
            if (GoogleDrivePaused())                         // saving to Google Drive needs STEAM DECK EXPERIMENTAL
                Notify("Cloud Save is paused: " + CloudServiceName() + " needs STEAM DECK EXPERIMENTAL (Options > Launcher). This session is not synced.");
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
                    string driveProblem = DriveDown();       // Google Drive: its local copy brought up to date first
                    if (driveProblem != null)
                    {
                        bool playAnyway = UiCall<bool>(delegate
                        {
                            ShowWindowFront();
                            return Ask("Cloud Save", CloudServiceName() + " could not be reached:\n" + driveProblem + "\n\nPlay anyway with the save on " +
                                                     "this device? Nothing is synced this time; the next sync compares both and asks if they differ.");
                        });
                        if (!playAnyway)
                        {
                            problems.Add(CloudServiceName() + " could not be reached, so the game was not launched.\n\n" + driveProblem);
                            Ui(delegate { SetCloudNote(CloudServiceName() + " not reached - game not launched"); SetStatus("Ready."); });
                            return;
                        }
                        eng = null;                          // no download now and no upload after
                        Ui(delegate { SetCloudNote("Playing without cloud sync this time"); });
                    }
                }
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
                // the AZS addon the launcher carries (AZS Handbook): an installed one brought up to the launcher's
                // version too, and the retired ones (AZS Transparency, AZS Minor Mods: Slim Suite's old folder) taken out
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
                    string environmentNote = PrepareGameEnvironment(psi);   // Steam Deck experimental: WGPU_BACKEND=vulkan
                    if (environmentNote != null) LogStatusOnly(environmentNote);
                    string gameDir = Path.GetDirectoryName(exe);
                    DateTime stamp = GameFilesStamp(gameDir);
                    sessionUpdated = false;
                    sessionCrashed = false;
                    using (Process p = Process.Start(psi))
                    {
                        launched = true;
                        RememberPlayedVersions();            // "what changed since you last played" compares with these
                        Ui(StepAsideForGame);                // hidden (Game Mode) or minimized while it runs, if chosen
                        p.WaitForExit();                     // returns on normal exit or crash
                        Ui(ReturnAfterGame);                 // back at once (no black screen while it waits below)
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
                        Ui(StepAsideForGame);                // the game opened again (its update): out of its way again
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
                    Ui(ReturnAfterGame);                     // back before the upload, so it is seen
                    SyncLog("Game closed." + (sessionCrashed ? " (it did not close normally)" : ""), Level.Info);
                    // Patch 0.141.1: after a crash the game saves a report and dump in its Diagnostics folder.
                    if (sessionCrashed) SyncLog(Lang.T("If the game crashed, it saved a crash report in its Diagnostics folder. Sending it to the game's developer helps fix the crash."), Level.Info);
                    if (eng == null) Ui(delegate { SetCloudNote("Game has closed."); });
                }
                string driveBefore = launched && eng != null ? DriveDown() : null;
                if (driveBefore != null)
                {
                    problems.Add(CloudServiceName() + " could not be reached, so your progress was not uploaded. It stays on this device " +
                                 "and is uploaded at the next sync.\n\n" + driveBefore);
                    Ui(delegate { SetCloudNote(CloudServiceName() + " not reached - progress kept on this device"); });
                }
                else if (launched && eng != null)
                {
                    try
                    {
                        if (eng.Push(false))
                        {
                            string driveAfter = DriveUp();
                            if (driveAfter == null) Ui(delegate { SetStatus("Ready."); SetCloudNote(""); });
                            else
                            {
                                problems.Add("Your progress is saved on this device, but " + CloudServiceName() + " could not be reached. " +
                                             "It is uploaded at the next sync.\n\n" + driveAfter);
                                Ui(delegate { SetCloudNote(CloudServiceName() + " upload waiting - see the Cloud Save log"); });
                            }
                        }
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
            ReturnAfterGame();                               // (if it was still out of the way)
            if (closeWhenSessionDone)
            {
                CloseIfAskedAfterSession(problems.Count > 0);
                if (problems.Count > 0) Warn("Launcher", string.Join("\n\n", problems));
                return;
            }
            if (CloseAfterSession(problems)) return;          // chosen in Options: back to Steam once all is saved
            After(3000, AutoUpdateAddons);                    // updates that waited for the game to close
            Invalidate();                                    // the play time the game just saved
            RefreshPlayLabel();
            // the launcher comes back up only when there is something to say: the game updated itself, it crashed, or
            // something went wrong (an upload). Otherwise the launcher stays visible after the game closes.
            if (problems.Count > 0 || sessionUpdated || sessionCrashed) ShowWindowFront();
            if (problems.Count > 0) Warn("Launcher", string.Join("\n\n", problems));
        }
    }
}
