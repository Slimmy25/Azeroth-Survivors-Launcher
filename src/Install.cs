// Downloads, unpacking archives, installing the game client and addons.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Net;
using System.Text;
using System.Threading;

namespace AzerothLauncher
{
    public class DiskFullException : IOException
    {
        public DiskFullException() : base("There is not enough free disk space.") { }
    }

    // A problem worded so that a player can read it.
    public class UserError : Exception
    {
        public UserError(string message) : base(message) { }
        public UserError(string message, Exception inner) : base(message, inner) { }
    }

    public class CancelledError : UserError
    {
        public CancelledError() : base("Cancelled.") { }
    }

    // ------------------------------------------------------------------ web requests
    public static class Net
    {
        public const string UserAgent = "AzerothSurvivorsLauncher/1.0";

        static Net()
        {
            try
            {   // Windows' own choice (TLS 1.2 / 1.3) when available; otherwise make sure TLS 1.2 is on,
                // because the old .NET defaults (SSL 3 / TLS 1.0) can't talk to modern servers
                if ((int)ServicePointManager.SecurityProtocol != 0)
                    ServicePointManager.SecurityProtocol |= (SecurityProtocolType)3072;
            }
            catch (Exception) { }
        }

        public static bool IsHttps(string url)
        {
            Uri u;
            return Uri.TryCreate(url ?? "", UriKind.Absolute, out u) && u.Scheme == "https" && u.Host.Length > 0
                   && string.IsNullOrEmpty(u.UserInfo);
        }

        public static bool IsWebAddress(string url)
        {
            Uri u;
            return Uri.TryCreate(url ?? "", UriKind.Absolute, out u) && (u.Scheme == "https" || u.Scheme == "http");
        }

        // Opens a web address and follows redirects itself (GitHub and most file hosts use them).
        // httpsOnly: every step must be https.
        public static HttpWebResponse Open(string url, int timeoutMs, bool httpsOnly, string what, string accept = null,
                                           Action<HttpWebRequest> prepare = null)
        {
            string firstHost = null;
            for (int hop = 0; hop < 10; hop++)
            {
                if (httpsOnly ? !IsHttps(url) : !IsWebAddress(url))
                    throw new UserError(hop == 0 ? "The " + what + " address must start with https://"
                                                 : "The " + what + " server redirected to an address that is not secure (https).");
                var req = (HttpWebRequest)WebRequest.Create(url);
                req.UserAgent = UserAgent;
                req.AllowAutoRedirect = false;
                req.Timeout = timeoutMs;
                req.ReadWriteTimeout = Math.Max(timeoutMs, 30000);
                req.Headers["Cache-Control"] = "no-cache";
                req.AutomaticDecompression = DecompressionMethods.None;
                if (accept != null) req.Accept = accept;
                if (firstHost == null) firstHost = req.RequestUri.Host;
                // extra headers (a login) go only to the server first asked, never to one it redirects to
                if (prepare != null && string.Equals(req.RequestUri.Host, firstHost, StringComparison.OrdinalIgnoreCase)) prepare(req);
                HttpWebResponse resp;
                try
                {
                    resp = (HttpWebResponse)req.GetResponse();
                }
                catch (WebException e)
                {
                    var r = e.Response as HttpWebResponse;
                    if (r != null)
                    {
                        int code = (int)r.StatusCode;
                        r.Close();
                        if (code >= 300 && code < 400) throw new UserError("The " + what + " server sent a broken redirect.");
                        throw new UserError("The " + what + " server answered with error " + code + ".");
                    }
                    if (e.Status == WebExceptionStatus.TrustFailure || e.Status == WebExceptionStatus.SecureChannelFailure)
                        throw new UserError("The " + what + " server's security certificate was not accepted. It needs a normal, trusted https certificate.");
                    if (e.Status == WebExceptionStatus.Timeout)
                        throw new UserError("The " + what + " server did not answer in time. Check your internet connection and try again.");
                    throw new UserError("Could not reach the " + what + " server. Check your internet connection and try again.");
                }
                int status = (int)resp.StatusCode;
                if (status >= 300 && status < 400)
                {
                    string loc = resp.Headers["Location"];
                    resp.Close();
                    if (string.IsNullOrEmpty(loc)) throw new UserError("The " + what + " server sent a broken redirect.");
                    url = new Uri(new Uri(url), loc).AbsoluteUri;
                    continue;
                }
                return resp;
            }
            throw new UserError("The " + what + " server redirected too many times.");
        }

        // Streams a web file to dest (via dest.part). progress(done, total). Returns the SHA-256 (lower-case hex).
        [ThreadStatic] public static string LastMd5;          // the MD5 of the last file Download fetched on this thread

        public static string Download(string url, string dest, bool httpsOnly, string what, Action<long, long> progress,
                                      ManualResetEvent cancel, long expectedSize, Action<HttpWebRequest> prepare = null)
        {
            string tmp = dest + ".part";
            try
            {
                LastMd5 = null;
                using (var sha = System.Security.Cryptography.SHA256.Create())
                using (var md5 = System.Security.Cryptography.MD5.Create())
                using (HttpWebResponse resp = Open(url, 30000, httpsOnly, what, null, prepare))
                {
                    if ((resp.ContentType ?? "").ToLowerInvariant().Contains("text/html"))
                        throw new UserError("The download link gave a web page instead of the file. It must be a direct download link.");
                    long clen = resp.ContentLength > 0 ? resp.ContentLength : 0;
                    long total = clen > 0 ? clen : expectedSize;
                    long done = 0;
                    var buf = new byte[1 << 16];
                    using (Stream input = resp.GetResponseStream())
                    using (var output = new FileStream(tmp, FileMode.Create, FileAccess.Write, FileShare.None))
                    {
                        while (true)
                        {
                            if (cancel != null && cancel.WaitOne(0)) throw new CancelledError();
                            int n;
                            try { n = input.Read(buf, 0, buf.Length); }
                            catch (IOException) { throw new UserError("The download was interrupted. Check your connection and try again."); }
                            catch (WebException) { throw new UserError("The download was interrupted. Check your connection and try again."); }
                            if (n <= 0) break;
                            output.Write(buf, 0, n);
                            sha.TransformBlock(buf, 0, n, null, 0);
                            md5.TransformBlock(buf, 0, n, null, 0);
                            done += n;
                            if (progress != null) progress(done, total);
                        }
                    }
                    if (clen > 0 && done != clen)
                        throw new UserError("The download was cut off before it finished. Please try again.");
                    sha.TransformFinalBlock(new byte[0], 0, 0);
                    md5.TransformFinalBlock(new byte[0], 0, 0);
                    LastMd5 = Hex(md5.Hash);
                    Util.Replace(tmp, dest);
                    return Hex(sha.Hash);
                }
            }
            catch (Exception)
            {
                try { File.Delete(tmp); } catch (Exception) { }
                throw;
            }
        }

        public static string Hex(byte[] data)
        {
            var sb = new StringBuilder(data.Length * 2);
            foreach (byte b in data) sb.Append(b.ToString("x2"));
            return sb.ToString();
        }

        public static string FileSha256(string path)
        {
            using (var sha = System.Security.Cryptography.SHA256.Create())
            using (var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 1 << 20))
                return Hex(sha.ComputeHash(fs));
        }
    }

    // ------------------------------------------------------------------ archives
    public static class Archive
    {
        public static readonly string[] Exts = { ".zip", ".rar", ".7z" };

        // "rar", "zip" or "7z" from the first bytes of a file (whatever its name says), else null
        public static string Kind(string path)
        {
            try
            {
                var head = new byte[8];
                int n;
                using (var fs = File.OpenRead(path)) n = fs.Read(head, 0, 8);
                if (n >= 6 && head[0] == 'R' && head[1] == 'a' && head[2] == 'r' && head[3] == '!' && head[4] == 0x1a && head[5] == 0x07) return "rar";
                if (n >= 4 && head[0] == 'P' && head[1] == 'K' && ((head[2] == 3 && head[3] == 4) || (head[2] == 5 && head[3] == 6))) return "zip";
                if (n >= 6 && head[0] == '7' && head[1] == 'z' && head[2] == 0xbc && head[3] == 0xaf && head[4] == 0x27 && head[5] == 0x1c) return "7z";
            }
            catch (Exception) { }
            return null;
        }

        // Unpacks a .zip, refusing any entry that would land outside dest. Returns the top-level names.
        public static List<string> ExtractZip(string zipPath, string dest)
        {
            string root = Path.GetFullPath(dest).TrimEnd('\\') + "\\";
            var tops = new List<string>();
            try
            {
                using (ZipArchive z = ZipFile.OpenRead(zipPath))
                {
                    foreach (ZipArchiveEntry e in z.Entries)
                    {
                        string target = Path.GetFullPath(Path.Combine(root, e.FullName.Replace('/', '\\')));
                        if (!target.StartsWith(root, StringComparison.OrdinalIgnoreCase) &&
                            !string.Equals(target.TrimEnd('\\') + "\\", root, StringComparison.OrdinalIgnoreCase))
                            throw new UserError("The .zip contains an unsafe path; nothing was installed.");
                        string top = e.FullName.Replace('\\', '/').Split('/')[0];
                        if (top.Length > 0 && !tops.Contains(top)) tops.Add(top);
                    }
                    foreach (ZipArchiveEntry e in z.Entries)
                    {
                        string target = Path.GetFullPath(Path.Combine(root, e.FullName.Replace('/', '\\')));
                        if (e.FullName.EndsWith("/") || e.FullName.EndsWith("\\"))
                        {
                            Directory.CreateDirectory(target);
                            continue;
                        }
                        Directory.CreateDirectory(Path.GetDirectoryName(target));
                        if (File.Exists(target)) File.SetAttributes(target, FileAttributes.Normal);
                        e.ExtractToFile(target, true);
                    }
                }
            }
            catch (InvalidDataException)
            {
                throw new UserError("The file is damaged or incomplete.");
            }
            return tops;
        }

        // Programs that can unpack a .rar / .7z here, in the order they are tried.
        static List<string[]> Tools()
        {
            var roots = new List<string>();
            foreach (string k in new[] { "ProgramFiles", "ProgramFiles(x86)", "ProgramW6432" })
            {
                string v = Environment.GetEnvironmentVariable(k);
                if (!string.IsNullOrEmpty(v) && !roots.Contains(v)) roots.Add(v);
            }
            var list = new List<string[]>();      // { exe, kind }
            foreach (string n in new[] { "7z.exe", "7za.exe", "7zr.exe" }) list.Add(new[] { Path.Combine(Util.LauncherDir, n), "7z" });
            foreach (string r in roots) list.Add(new[] { Path.Combine(r, "7-Zip", "7z.exe"), "7z" });
            foreach (string n in new[] { "UnRAR.exe" }) list.Add(new[] { Path.Combine(Util.LauncherDir, n), "unrar" });
            foreach (string r in roots)
            {
                list.Add(new[] { Path.Combine(r, "WinRAR", "UnRAR.exe"), "unrar" });
                list.Add(new[] { Path.Combine(r, "WinRAR", "WinRAR.exe"), "unrar" });
            }
            string sysroot = Environment.GetEnvironmentVariable("SystemRoot") ?? @"C:\Windows";
            list.Add(new[] { Path.Combine(sysroot, "System32", "tar.exe"), "tar" });   // Windows 10/11's own tar reads many .rar files
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var outList = new List<string[]>();
            foreach (var t in list)
                if (seen.Add(t[0]) && File.Exists(t[0])) outList.Add(t);
            return outList;
        }

        static string Quote(string s) { return "\"" + s + "\""; }

        public static void Extract(string path, string dest, string kind)
        {
            Directory.CreateDirectory(dest);
            if (kind == "zip")
            {
                ExtractZip(path, dest);
                return;
            }
            var tools = Tools();
            if (tools.Count == 0)
                throw new UserError("The file is a ." + kind + " archive, and no program to unpack it was found.\n\n" +
                                    "Install the free 7-Zip (7-zip.org) or WinRAR and try again.");
            var problems = new List<string>();
            foreach (var t in tools)
            {
                string args;
                if (t[1] == "7z") args = "x -y " + Quote("-o" + dest) + " " + Quote(path);
                else if (t[1] == "unrar") args = "x -y -o+ -ibck " + Quote(path) + " " + Quote(dest.TrimEnd('\\') + "\\");
                else args = "-xf " + Quote(path) + " -C " + Quote(dest);
                try
                {
                    var psi = new ProcessStartInfo(t[0], args);
                    psi.UseShellExecute = false;
                    psi.CreateNoWindow = true;
                    psi.RedirectStandardOutput = true;
                    psi.RedirectStandardError = true;
                    using (Process p = Process.Start(psi))
                    {
                        // read both streams on their own threads so a chatty tool never blocks
                        string err = "";
                        var errThread = new Thread(delegate() { try { err = p.StandardError.ReadToEnd(); } catch (Exception) { } });
                        errThread.IsBackground = true;
                        errThread.Start();
                        string output = p.StandardOutput.ReadToEnd();
                        p.WaitForExit();
                        errThread.Join(5000);
                        if (p.ExitCode == 0) return;
                        string msg = (err.Trim().Length > 0 ? err : output).Trim();
                        if (msg.Length > 160) msg = msg.Substring(0, 160);
                        problems.Add(Path.GetFileName(t[0]) + ": " + (msg.Length > 0 ? msg : "exit code " + p.ExitCode));
                    }
                }
                catch (Exception e)
                {
                    problems.Add(Path.GetFileName(t[0]) + ": " + e.Message);
                }
            }
            if (problems.Count > 4) problems.RemoveRange(4, problems.Count - 4);
            throw new UserError("The archive could not be unpacked.\n\n" + string.Join("\n", problems));
        }

        // The newest recently downloaded .rar/.zip/.7z in the launcher's folder or Downloads
        // (a name with "azeroth" wins over a newer one without), or null.
        public static string FindDownloaded()
        {
            DateTime cutoff = DateTime.Now.AddDays(-2);
            string best = null;
            bool bestAz = false;
            DateTime bestTime = DateTime.MinValue;
            foreach (string folder in new[] { Util.LauncherDir, Util.DownloadsDir })
            {
                string[] files;
                try { files = Directory.GetFiles(folder); }
                catch (Exception) { continue; }
                foreach (string f in files)
                {
                    try
                    {
                        string ext = Path.GetExtension(f).ToLowerInvariant();
                        if (Array.IndexOf(Exts, ext) < 0) continue;
                        var fi = new FileInfo(f);
                        if (fi.LastWriteTime < cutoff || fi.Length <= 1024 * 1024 || Kind(f) == null) continue;
                        bool az = fi.Name.ToLowerInvariant().Contains("azeroth");
                        if (best == null || (az && !bestAz) || (az == bestAz && fi.LastWriteTime > bestTime))
                        {
                            best = f;
                            bestAz = az;
                            bestTime = fi.LastWriteTime;
                        }
                    }
                    catch (Exception) { }
                }
            }
            return best;
        }

        // the shallowest copy of the game exe inside an unpacked folder, or null
        public static string FindGameExe(string root)
        {
            string best = null;
            int depth = int.MaxValue;
            foreach (string f in Directory.GetFiles(root, Util.GameExe, SearchOption.AllDirectories))
            {
                int d = f.Split('\\').Length;
                if (d < depth) { depth = d; best = f; }
            }
            return best;
        }

        // Moves src (file or folder) to dst, merging folders and replacing files. The launcher itself is never overwritten.
        // keep: a file that is already there stays as it is (the player's own settings, addons and saves)
        static void MergeInto(string src, string dst) { MergeInto(src, dst, false); }

        static void MergeInto(string src, string dst, bool keep)
        {
            if (string.Equals(Path.GetFullPath(dst), Path.GetFullPath(Application_ExePath()), StringComparison.OrdinalIgnoreCase))
                return;
            if (Directory.Exists(src))
            {
                Directory.CreateDirectory(dst);
                foreach (string child in Directory.GetFileSystemEntries(src))
                    MergeInto(child, Path.Combine(dst, Path.GetFileName(child)), keep);
            }
            else
            {
                if (keep && File.Exists(dst)) return;
                Directory.CreateDirectory(Path.GetDirectoryName(dst));
                if (File.Exists(dst)) File.SetAttributes(dst, FileAttributes.Normal);
                Util.Replace(src, dst);
            }
        }

        static string Application_ExePath() { return System.Windows.Forms.Application.ExecutablePath; }

        // Unpacks a downloaded game archive so that AzerothSurvivors.exe sits directly in destDir
        // (an archive that wraps everything in one folder is unwrapped).
        public static void InstallGame(string archive, string destDir)
        {
            string kind = Kind(archive);
            if (kind == null) throw new UserError("That file is not a .rar, .zip or .7z archive.");
            Directory.CreateDirectory(destDir);
            // unpacked on the same drive so the files can be moved into place: in the launcher's own folder when
            // the game goes next to the launcher, otherwise in the game folder
            bool besideLauncher = Util.DataBesideExe &&
                string.Equals(Path.GetFullPath(destDir).TrimEnd('\\'), Util.LauncherDir.TrimEnd('\\'), StringComparison.OrdinalIgnoreCase);
            string stage = besideLauncher ? Path.Combine(Util.TempDir, "install") : Path.Combine(destDir, ".azeroth_install_tmp");
            Util.DeleteTreeQuiet(stage);
            Directory.CreateDirectory(stage);
            try
            {
                Extract(archive, stage, kind);
                string exe = FindGameExe(stage);
                if (exe == null) throw new UserError("The archive was unpacked, but " + Util.GameExe + " was not inside it.");
                string root = Path.GetDirectoryName(exe);
                // installing again over a game that is already here: the game files are replaced, but nothing the
                // player already has in benilla-config (addons, addon settings, options, key bindings) is overwritten
                foreach (string item in Directory.GetFileSystemEntries(root))
                    MergeInto(item, Path.Combine(destDir, Path.GetFileName(item)),
                              string.Equals(Path.GetFileName(item), "benilla-config", StringComparison.OrdinalIgnoreCase));
            }
            finally
            {
                Util.DeleteTreeQuiet(stage);
            }
            if (!File.Exists(Path.Combine(destDir, Util.GameExe)))
                throw new UserError("The install finished, but " + Util.GameExe + " is not in " + destDir + ".");
            Directory.CreateDirectory(Path.Combine(destDir, "benilla-config", "AddOns"));
        }

        // Downloads an addon .zip straight from its link and unpacks it into addonDir. Returns the top-level folder names.
        public static List<string> InstallAddonZip(string url, string addonDir) { return InstallAddonZip(url, addonDir, null); }

        // keepAs: where a copy of the downloaded .zip is kept (Addon Archive, for rollback), or null
        public static List<string> InstallAddonZip(string url, string addonDir, string keepAs)
        {
            return InstallAddonZip(url, addonDir, keepAs, null);
        }

        // Resolve mirrors against their own hosting repository, not an upstream project's
        // different ZIP or SurvivorsForge metadata. Fetch afresh so re-uploaded assets do
        // not inherit an old digest. Locally supplied/rollback ZIPs have no release URL.
        internal static string GitHubAssetSha256(string url)
        {
            Uri uri;
            if (!Uri.TryCreate(url, UriKind.Absolute, out uri) ||
                !string.Equals(uri.Host, "github.com", StringComparison.OrdinalIgnoreCase)) return null;
            string[] parts = uri.AbsolutePath.Trim('/').Split('/');
            if (uri.Scheme != "https" || parts.Length != 6 || parts[2] != "releases" || parts[3] != "download")
                throw new UserError("This GitHub addon link must identify an exact release ZIP before it can be verified.");
            string api = "https://api.github.com/repos/" + parts[0] + "/" + parts[1] +
                         "/releases/tags/" + Uri.EscapeDataString(Uri.UnescapeDataString(parts[4]));
            Dictionary<string, object> release;
            using (var response = Net.Open(api, 15000, true, "GitHub", "application/vnd.github+json"))
            using (var reader = new StreamReader(response.GetResponseStream(), Encoding.UTF8))
                release = Json.AsDict(Json.Parse(reader.ReadToEnd()));
            foreach (object value in Json.AsList(Json.Get(release, "assets")) ?? new List<object>())
            {
                var asset = Json.AsDict(value);
                Uri assetUri;
                if (!Uri.TryCreate(Json.Str(asset, "browser_download_url"), UriKind.Absolute, out assetUri) ||
                    !string.Equals(uri.AbsoluteUri, assetUri.AbsoluteUri, StringComparison.Ordinal)) continue;
                string digest = Json.Str(asset, "digest");
                string sha = digest.StartsWith("sha256:", StringComparison.OrdinalIgnoreCase) ? digest.Substring(7) : "";
                bool valid = sha.Length == 64;
                foreach (char c in sha) if (!Uri.IsHexDigit(c)) valid = false;
                if (valid) return sha;
                throw new UserError("GitHub has no SHA-256 digest for this exact addon ZIP. Nothing was installed.");
            }
            throw new UserError("The configured addon ZIP is not listed in its GitHub release. Nothing was installed.");
        }

        // Callers resolving /releases/latest supply the digest from that same release asset.
        // Direct GitHub asset URLs (including mirrored bundles) resolve their digest here.
        public static List<string> InstallAddonZip(string url, string addonDir, string keepAs, string expectedSha256)
        {
            if (!File.Exists(url) && string.IsNullOrWhiteSpace(expectedSha256))
                expectedSha256 = GitHubAssetSha256(url);
            Directory.CreateDirectory(addonDir);
            Directory.CreateDirectory(Util.TempDir);
            string tmp = Path.Combine(Util.TempDir, "addon_" + Guid.NewGuid().ToString("N") + ".zip");
            try
            {
                string actualSha256;
                if (File.Exists(url))
                {
                    File.Copy(url, tmp, true);                        // MY ADDONS: a .zip on this PC
                    using (var sha = System.Security.Cryptography.SHA256.Create())
                    using (var input = File.OpenRead(tmp))
                        actualSha256 = BitConverter.ToString(sha.ComputeHash(input)).Replace("-", "").ToLowerInvariant();
                }
                else actualSha256 = Net.Download(url, tmp, false, "addon", null, null, 0);
                if (!string.IsNullOrWhiteSpace(expectedSha256) &&
                    !string.Equals(actualSha256, expectedSha256.Trim(), StringComparison.OrdinalIgnoreCase))
                    throw new UserError("The downloaded addon does not match the SHA-256 published for this exact addon release. Nothing was installed.");
                if (Kind(tmp) != "zip")
                    throw new UserError("The link did not give a .zip file. It must be a direct download link " +
                                        "(share pages such as LimeWire do not work).");
                var tops = ExtractZip(tmp, addonDir);
                if (keepAs != null) try { File.Copy(tmp, keepAs, true); } catch (Exception) { }
                return tops;
            }
            catch (UserError e)
            {
                if (e.Message.StartsWith("The download link gave a web page"))
                    throw new UserError("The link did not give a .zip file. It must be a direct download link " +
                                        "(share pages such as LimeWire do not work).");
                throw;
            }
            finally
            {
                try { File.Delete(tmp); } catch (Exception) { }
            }
        }
    }
}
