// The main launcher window, continued: downloading and installing the game client.
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
            Invalidate(new Rectangle(0, FooterTop, W, Math.Max(1, H - FooterTop)));
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

        // Try the two regular hosts in random order; if one fails (not when cancelled), try the other.
        static void DownloadClient(string file, Action<long, long> progress, ManualResetEvent cancel, Action<string> say)
        {
            var hosts = new List<int> { 0, 1 };               // 0 main, 1 Google Drive
            lock (hostDice)
                for (int i = hosts.Count - 1; i > 0; i--) { int j = hostDice.Next(i + 1); int t = hosts[i]; hosts[i] = hosts[j]; hosts[j] = t; }
            string[] names = { "main host", "Google Drive host" };
            var errors = new List<string>();
            for (int i = 0; i < hosts.Count; i++)
            {
                int h = hosts[i];
                say(i == 0 ? "Downloading from the " + names[h] + "..."
                           : "The " + names[hosts[i - 1]] + " did not work. Trying the " + names[h] + "...");
                try
                {
                    if (h == 0) DownloadFromMain(file, progress, cancel);
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
                    string sha = Net.Download(url, file, true, "download", progress, cancel, ClientBytes, login, true);
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
    }
}
