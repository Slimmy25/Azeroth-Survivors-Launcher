// Launcher updates: the UPDATE button (and a quiet check at start-up) compares this exe with the exe on the
// latest release of the GitHub repository. When they differ, the new one is installed: at start and before PLAY the update is required, as the owner decided; the new exe is
// downloaded next to this one, checked against the release's SHA-256, swapped in, and the launcher restarts.
// Pre-releases (such as "test" and "addons") are never offered: GitHub's "latest release" skips them.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Windows.Forms;

namespace AzerothLauncher
{
    public class LauncherRelease
    {
        public string Tag = "", Name = "", ExeUrl = "", Sha256 = "", Page = "";
        public long Size;
    }

    public static class SelfUpdate
    {
        // Releases and their verified launcher files are published in the public source repository.
        public static readonly string[] Repos = { "Slimmy25/Azeroth-Survivors-Launcher" };
        static string repo = Repos[0];
        public static string Repo { get { return repo; } }
        public static string ReleasesPage { get { return "https://github.com/" + Repo + "/releases/latest"; } }

        static string ExePath { get { return Path.GetFullPath(Application.ExecutablePath); } }

        // The new exe is downloaded into, and the replaced one parked in, the launcher's own folder (same drive, so
        // they can be moved). When that folder is not next to the exe they stay next to the exe.
        static string Scratch(string suffix)
        {
            string name = Path.GetFileName(ExePath) + suffix;
            return Util.DataBesideExe ? Path.Combine(Util.DataDir, name) : ExePath + suffix;
        }

        // the newest published (not pre-) release and its exe
        // the answer is remembered for 10 minutes: PLAY asks every time, and GitHub allows 60 questions an hour
        static LauncherRelease lastRelease;
        static DateTime lastReleaseAt;

        public static LauncherRelease FetchLatest()
        {
            if (lastRelease != null && DateTime.UtcNow - lastReleaseAt < TimeSpan.FromMinutes(10)) return lastRelease;
            LauncherRelease fetched = FetchLatestNow();
            lastRelease = fetched;
            lastReleaseAt = DateTime.UtcNow;
            return fetched;
        }

        static LauncherRelease FetchLatestNow()
        {
            string text = null;
            UserError last = null;
            foreach (string r0 in Repos)
            {
                try
                {
                    using (var resp = Net.Open("https://api.github.com/repos/" + r0 + "/releases/latest", 15000, true, "GitHub",
                                               "application/vnd.github+json"))
                    using (var r = new StreamReader(resp.GetResponseStream(), Encoding.UTF8))
                        text = r.ReadToEnd();
                    if (text.IndexOf(".exe", StringComparison.OrdinalIgnoreCase) < 0) { text = null; continue; }   // no launcher in it
                    repo = r0;
                    break;
                }
                catch (UserError e) { last = e; }
            }
            if (text == null)
            {
                if (last == null || last.Message.Contains("error 404")) throw new UserError("No launcher release has been published on GitHub yet.");
                throw last;
            }
            var d = Json.AsDict(Json.Parse(text));
            if (d == null) throw new UserError("GitHub's answer could not be read.");
            var rel = new LauncherRelease();
            rel.Tag = Json.Str(d, "tag_name");
            rel.Name = Json.Str(d, "name");
            if (rel.Name.Length == 0) rel.Name = rel.Tag;
            rel.Page = Json.Str(d, "html_url");
            string shaUrl = null;
            var assets = Json.AsList(Json.Get(d, "assets")) ?? new List<object>();
            foreach (object o in assets)
            {
                var a = Json.AsDict(o);
                string name = Json.Str(a, "name").ToLowerInvariant();
                if (name.EndsWith(".exe") && (rel.ExeUrl.Length == 0 || name.Contains("launcher")))
                {
                    rel.ExeUrl = Json.Str(a, "browser_download_url");
                    object size = Json.Get(a, "size");
                    rel.Size = size is long ? (long)size : 0;
                    string digest = Json.Str(a, "digest");               // "sha256:<hex>" on newer GitHub
                    rel.Sha256 = digest.StartsWith("sha256:") ? digest.Substring(7).ToLowerInvariant() : "";
                }
                else if (name.EndsWith(".sha256.txt")) shaUrl = Json.Str(a, "browser_download_url");
            }
            if (rel.ExeUrl.Length == 0) throw new UserError("The latest release on GitHub (" + rel.Name + ") has no launcher exe attached.");
            if (rel.Sha256.Length == 0 && shaUrl != null)
            {
                using (var resp = Net.Open(shaUrl, 15000, true, "GitHub", null))
                using (var r = new StreamReader(resp.GetResponseStream(), Encoding.UTF8))
                {
                    var m = Regex.Match(r.ReadToEnd(), @"\b([0-9a-fA-F]{64})\b\s+\S*\.exe", RegexOptions.IgnoreCase);
                    if (m.Success) rel.Sha256 = m.Groups[1].Value.ToLowerInvariant();
                }
            }
            if (rel.Sha256.Length == 0) throw new UserError("The latest release on GitHub has no checksum for its exe, so it is not installed.");
            return rel;
        }

        // true when the latest release's exe is not the one running now
        public static bool Differs(LauncherRelease rel)
        {
            try { return Net.FileSha256(ExePath) != rel.Sha256; }
            catch (Exception) { return true; }
        }

        // downloads the new exe next to this one and checks it; returns its path
        public static string Download(LauncherRelease rel, Action<long, long> progress, ManualResetEvent cancel)
        {
            string dest = Scratch(".download");
            string digest = Net.Download(rel.ExeUrl, dest, true, "GitHub", progress, cancel, rel.Size);
            if (digest != rel.Sha256)
            {
                try { File.Delete(dest); } catch (Exception) { }
                throw new UserError("The downloaded launcher does not match its checksum, so it was not installed. Please try again later.");
            }
            return dest;
        }

        // Swaps the new exe in and starts it. A running exe cannot be overwritten, but it can be renamed:
        // this one becomes "<name>.exe.old" and is deleted by the new launcher when it starts.
        public static void InstallAndRestart(string newExe)
        {
            string exe = ExePath, old = Scratch(".old");
            try
            {
                if (File.Exists(old)) File.Delete(old);
                File.Move(exe, old);
            }
            catch (Exception e)
            {
                try { File.Delete(newExe); } catch (Exception) { }
                throw new UserError("The launcher could not replace itself in this folder (" + e.Message + ").\n\n" +
                                    "Download the new version from GitHub instead:\n" + ReleasesPage);
            }
            try { File.Move(newExe, exe); }
            catch (Exception e)
            {
                try { File.Move(old, exe); } catch (Exception) { }
                throw new UserError("The new launcher could not be put in place (" + e.Message + "). Nothing was changed.");
            }
            // the new launcher waits until this process has fully exited, then opens its window
            Process.Start(new ProcessStartInfo(exe, "--after-update " + Process.GetCurrentProcess().Id) { UseShellExecute = false, WorkingDirectory = Path.GetDirectoryName(exe) });
        }

        // run at start: removes the previous exe left behind by an update, and a half-finished download
        public static void CleanUp()
        {
            foreach (string f in new[] { Scratch(".old"), Scratch(".download"), Scratch(".download.part"),
                                         ExePath + ".old", ExePath + ".download", ExePath + ".download.part" })   // older versions
                for (int i = 0; i < 10 && File.Exists(f); i++)            // the old launcher may still be closing
                {
                    try { File.Delete(f); }
                    catch (Exception) { Thread.Sleep(300); }
                }
        }
    }
}
