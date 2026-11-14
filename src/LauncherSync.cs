// The main launcher window, continued: running a Cloud Save sync (download before the game, upload after it).
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
                File.AppendAllText(logf, DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss ", CultureInfo.InvariantCulture) + "[" + level + "] " + msg + "\r\n",
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
                    string driveDown = DriveDown();          // Google Drive: its local copy first
                    if (driveDown != null) throw new UserError(CloudServiceName() + " could not be reached: " + driveDown);
                    ok = kind == "status" ? eng.Status() : kind == "pull" ? eng.Pull(force) :
                         kind == "restore" ? eng.RestoreDownload(stamp) : eng.Push(force);
                    string driveUp = DriveUp();              // and what changed in it, sent back
                    if (driveUp != null) throw new UserError("Saved in the launcher's copy, but " + CloudServiceName() + " could not be reached: " + driveUp +
                                                             " It is uploaded at the next sync.");
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
            if (closeWhenSessionDone && !GameRunningFlag) { CloseIfAskedAfterSession(err != null); if (err == null) return; }
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
                string releaseIdentity = null;
                try
                {
                    Directory.CreateDirectory(ss.AddonsDir);
                    List<string> names;
                    BuiltinAddon builtin = BuiltinAddons.Find(entry.Url);
                    if (builtin != null) { BuiltinAddons.Install(ss.AddonsDir, builtin); names = new List<string> { builtin.Folder }; }
                    else
                    {
                        string installUrl = entry.Url, expectedSha = null;
                        List<KeyValuePair<string, string>> alternatives = null;
                        if (IsDynamicGitHubAddonUrl(entry.Url))
                        {
                            GitHubAddonRelease release = ResolveGitHubAddonRelease(entry.Url);
                            installUrl = release.Url;
                            expectedSha = release.Digest;
                            alternatives = release.Alternatives;
                            releaseIdentity = release.Identity;
                        }
                        names = InstallAddonUpdateSafely(installUrl, ss.AddonsDir, null, expectedSha, new List<string>(entry.Folders), false, null,
                                                         entry.Folders, alternatives);
                        AddonArchive.Tidy(entry.Name);
                    }
                    string name = entry.Name;
                    string savedReleaseIdentity = releaseIdentity;
                    var rec = new List<object>();
                    foreach (string n in names) rec.Add(n);
                    Ui(delegate
                    {
                        AddonInstalls()[name] = rec;
                        AddonInstalledAt()[name] = DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture);
                        if (!string.IsNullOrEmpty(savedReleaseIdentity)) AddonInstalledRelease()[name] = savedReleaseIdentity;
                        Cfg.Save();
                    });
                    SyncLog(name + " installed: its settings came from the cloud.", Level.Good);
                    done.Add(name);
                }
                catch (Exception e) { SyncLog("  " + entry.Name + " not installed: " + e.Message, Level.Warn); }
            }
            return done;
        }
    }
}
