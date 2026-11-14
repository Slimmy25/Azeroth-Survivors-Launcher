// Downloads, unpacking archives, installing the game client and addons.
using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Net;
using System.Text;
using System.Threading;
using SharpCompress.Archives;
using SharpCompress.Common;
using SharpCompress.Readers;

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

    // The download is not the file its release published: never install it, and try nothing else in its place.
    public class DigestMismatchError : UserError
    {
        public DigestMismatchError(string message) : base(message) { }
    }

    // The download holds no addon (no folder with a .toc file): the next file of the release can be tried.
    public class NoAddonInPackageError : UserError
    {
        public NoAddonInPackageError(string message) : base(message) { }
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
                        bool rateLimited = (code == 403 || code == 429) &&
                            (r.Headers["X-RateLimit-Remaining"] == "0" || !string.IsNullOrEmpty(r.Headers["Retry-After"]));
                        r.Close();
                        if (rateLimited) throw new UserError("GitHub's request limit has been reached. Please wait and try again. Nothing was installed.");
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
                                      ManualResetEvent cancel, long expectedSize, Action<HttpWebRequest> prepare = null,
                                      bool sizeMustMatch = false)
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
                    // a file of another size can never pass its checksum: stop before downloading all of it
                    if (sizeMustMatch && expectedSize > 0 && clen > 0 && clen != expectedSize)
                        throw new UserError("The " + what + " offers a different file than expected (" + Util.SizeText(clen) + " instead of " +
                                            Util.SizeText(expectedSize) + ").");
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
                        if (top.Length > 0 && !tops.Exists(t => string.Equals(t, top, StringComparison.OrdinalIgnoreCase))) tops.Add(top);
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

        // Extracts game archives with bundled managed code. No executable or archive utility is
        // searched for or started on the player's computer.
        static void ExtractManaged(string path, string dest)
        {
            string root = Path.GetFullPath(dest);
            string rootPrefix = root.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
            var entries = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
            var written = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var parentDirectories = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            using (IArchive archive = ArchiveFactory.OpenArchive(path))
            {
                if (archive.IsEncrypted)
                    throw new UserError("The game archive is password-protected. Please use an unencrypted game download.");
                if (!archive.IsComplete)
                    throw new UserError("The game archive is incomplete or needs another archive part. Nothing was installed.");

                // Validate every path and link before writing any archive contents.
                foreach (IArchiveEntry entry in archive.Entries)
                {
                    string relative = SafeArchivePath(entry.Key, entry.IsDirectory, entry.LinkTarget);
                    if (entries.ContainsKey(relative))
                        throw new UserError("The game archive contains duplicate or conflicting file paths. Nothing was installed.");
                    string[] parts = relative.Split(Path.DirectorySeparatorChar);
                    string parentPath = "";
                    for (int i = 0; i < parts.Length - 1; i++)
                    {
                        parentPath = parentPath.Length == 0 ? parts[i] : Path.Combine(parentPath, parts[i]);
                        bool parentIsDirectory;
                        parentDirectories.Add(parentPath);
                        if (entries.TryGetValue(parentPath, out parentIsDirectory) && !parentIsDirectory)
                            throw new UserError("The game archive contains conflicting file paths. Nothing was installed.");
                    }
                    if (!entry.IsDirectory && parentDirectories.Contains(relative))
                        throw new UserError("The game archive contains conflicting file paths. Nothing was installed.");
                    string target = Path.GetFullPath(Path.Combine(root, relative));
                    if (!target.StartsWith(rootPrefix, StringComparison.OrdinalIgnoreCase))
                        throw new UserError("The game archive contains an unsafe path. Nothing was installed.");
                    entries.Add(relative, entry.IsDirectory);
                }

                // Non-solid archives expose independent entry streams; solid/7z archives
                // must be read in sequence to avoid repeatedly decompressing earlier files.
                if (!archive.IsSolid && archive.Type != ArchiveType.SevenZip)
                {
                    foreach (IArchiveEntry entry in archive.Entries)
                    {
                        string relative = SafeArchivePath(entry.Key, entry.IsDirectory, entry.LinkTarget);
                        string target = Path.GetFullPath(Path.Combine(root, relative));
                        if (!target.StartsWith(rootPrefix, StringComparison.OrdinalIgnoreCase))
                            throw new UserError("The game archive contains an unsafe path. Nothing was installed.");
                        if (entry.IsDirectory) { Directory.CreateDirectory(target); continue; }
                        Directory.CreateDirectory(Path.GetDirectoryName(target));
                        using (Stream input = entry.OpenEntryStream())
                        using (var output = new FileStream(target, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                            input.CopyTo(output);
                    }
                    return;
                }

                using (IReader reader = archive.ExtractAllEntries())
                {
                    int count = 0;
                    while (reader.MoveToNextEntry())
                    {
                        if (count >= entries.Count)
                            throw new UserError("The archive contents changed while extracting. Nothing was installed.");
                        string readerPath = SafeArchivePath(reader.Entry.Key, reader.Entry.IsDirectory, reader.Entry.LinkTarget);
                        bool isDirectory;
                        if (!entries.TryGetValue(readerPath, out isDirectory) || !written.Add(readerPath) ||
                            reader.Entry.IsDirectory != isDirectory)
                            throw new UserError("The archive contents changed while extracting. Nothing was installed.");
                        count++;
                        string target = Path.GetFullPath(Path.Combine(root, readerPath));
                        if (!target.StartsWith(rootPrefix, StringComparison.OrdinalIgnoreCase))
                            throw new UserError("The game archive contains an unsafe path. Nothing was installed.");

                        if (isDirectory || reader.Entry.IsDirectory)
                        {
                            Directory.CreateDirectory(target);
                            continue;
                        }

                        string parent = Path.GetDirectoryName(target);
                        if (!string.IsNullOrEmpty(parent)) Directory.CreateDirectory(parent);
                        using (var output = new FileStream(target, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                            reader.WriteEntryTo(output);
                    }
                    if (count != entries.Count)
                        throw new UserError("The archive contents changed while extracting. Nothing was installed.");
                }
            }
        }

        static string SafeArchivePath(string key, bool isDirectory, string linkTarget)
        {
            if (string.IsNullOrEmpty(key) || linkTarget != null)
                throw new UserError("The game archive contains an empty path or symbolic link. Nothing was installed.");

            string path = key.Replace('\\', '/');
            if (path.StartsWith("/", StringComparison.Ordinal) || path.IndexOf(':') >= 0)
                throw new UserError("The game archive contains an unsafe path. Nothing was installed.");
            if (path.EndsWith("/", StringComparison.Ordinal))
            {
                if (!isDirectory) throw new UserError("The game archive contains an unsafe path. Nothing was installed.");
                path = path.Substring(0, path.Length - 1);
            }

            string[] parts = path.Split('/');
            if (parts.Length == 0) throw new UserError("The game archive contains an unsafe path. Nothing was installed.");
            char[] invalid = Path.GetInvalidFileNameChars();
            for (int i = 0; i < parts.Length; i++)
            {
                string part = parts[i];
                if (part.Length == 0 || part == "." || part == ".." || part.EndsWith(".", StringComparison.Ordinal) ||
                    part.EndsWith(" ", StringComparison.Ordinal))
                    throw new UserError("The game archive contains an unsafe path. Nothing was installed.");
                foreach (char c in part)
                    if (Array.IndexOf(invalid, c) >= 0)
                        throw new UserError("The game archive contains an unsafe path. Nothing was installed.");

                string device = part.Split('.')[0].TrimEnd(' ', '.').ToUpperInvariant();
                if (device == "CON" || device == "PRN" || device == "AUX" || device == "NUL" ||
                    (device.Length == 4 && (device.StartsWith("COM", StringComparison.Ordinal) || device.StartsWith("LPT", StringComparison.Ordinal)) &&
                     device[3] >= '1' && device[3] <= '9'))
                    throw new UserError("The game archive contains a reserved Windows file name. Nothing was installed.");
            }
            return string.Join(Path.DirectorySeparatorChar.ToString(), parts);
        }

        public static void Extract(string path, string dest, string kind)
        {
            Directory.CreateDirectory(dest);
            if (kind != "zip" && kind != "rar" && kind != "7z")
                throw new UserError("The game archive format is not supported by the built-in extractor.");
            try { ExtractManaged(path, dest); }
            catch (UserError) { throw; }
            catch (IOException) { throw; }
            catch (Exception e)
            {
                throw new UserError("The game archive could not be extracted. It may be damaged, encrypted, incomplete, or use an unsupported method. Nothing was installed.", e);
            }
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
        static readonly object githubReleaseGate = new object();

        // Share one short-lived response across startup checks and installs, including
        // launcher restarts. Each cached response belongs to exactly one API URL.
        // GitHub allows 60 API requests an hour without signing in. When it says no (or is down), the answer comes from
        // the last saved copy, however old, and failing that from the release's public web page; only a 404 (no such
        // release) is passed on as it is, because callers act on it.
        static readonly Dictionary<string, KeyValuePair<DateTime, Dictionary<string, object>>> githubReleasePages =
            new Dictionary<string, KeyValuePair<DateTime, Dictionary<string, object>>>(StringComparer.OrdinalIgnoreCase);

        internal static Dictionary<string, object> ReadGitHubReleaseMetadata(string api, bool refresh = false)
        {
            lock (githubReleaseGate)
            {
                string key;
                using (var sha = System.Security.Cryptography.SHA256.Create())
                    key = BitConverter.ToString(sha.ComputeHash(Encoding.UTF8.GetBytes(api))).Replace("-", "");
                string cache = Path.Combine(Util.CacheDir, "github-releases", key + ".json");
                TimeSpan fresh = refresh ? TimeSpan.FromMinutes(2) : TimeSpan.FromMinutes(20);
                try
                {
                    TimeSpan age = DateTime.UtcNow - File.GetLastWriteTimeUtc(cache);
                    if (File.Exists(cache) && age >= TimeSpan.Zero && age < fresh)
                    {
                        var saved = Json.AsDict(Json.Parse(File.ReadAllText(cache)));
                        if (saved != null) return saved;
                    }
                }
                catch (Exception) { } // A missing/unreadable cache always falls back to GitHub.
                KeyValuePair<DateTime, Dictionary<string, object>> page;
                if (githubReleasePages.TryGetValue(api, out page) && DateTime.UtcNow - page.Key < fresh) return page.Value;
                string text;
                try
                {
                    using (var response = Net.Open(api, 15000, true, "GitHub", "application/vnd.github+json"))
                    using (var reader = new StreamReader(response.GetResponseStream(), Encoding.UTF8)) text = reader.ReadToEnd();
                }
                catch (UserError e)
                {
                    if (e.Message.IndexOf("error 404", StringComparison.OrdinalIgnoreCase) >= 0) throw;
                    try
                    {
                        if (File.Exists(cache))
                        {
                            var saved = Json.AsDict(Json.Parse(File.ReadAllText(cache)));
                            if (saved != null) return saved;
                        }
                    }
                    catch (Exception) { }
                    Dictionary<string, object> fromPage = null;
                    try { fromPage = ReadGitHubReleasePage(api); } catch (Exception) { }
                    if (fromPage != null)
                    {
                        githubReleasePages[api] = new KeyValuePair<DateTime, Dictionary<string, object>>(DateTime.UtcNow, fromPage);
                        return fromPage;
                    }
                    throw;
                }
                var release = Json.AsDict(Json.Parse(text));
                if (release == null) throw new UserError("GitHub returned invalid release information. Nothing was installed.");
                try
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(cache));
                    File.WriteAllText(cache, text, Encoding.UTF8);
                }
                catch (Exception) { } // Verification can still use the freshly fetched response.
                return release;
            }
        }

        // The same release read from github.com's own pages, which the API's hourly limit does not cover:
        // /releases/latest leads to /releases/tag/<tag>, and /releases/expanded_assets/<tag> lists its files
        // (with their sha256 digests where GitHub shows them). Null when the page cannot be read.
        internal static Dictionary<string, object> ReadGitHubReleasePage(string api)
        {
            const string prefix = "https://api.github.com/repos/";
            if (!api.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) return null;
            string[] parts = api.Substring(prefix.Length).Split('/');
            if (parts.Length < 4 || !string.Equals(parts[2], "releases", StringComparison.OrdinalIgnoreCase)) return null;
            string repo = parts[0] + "/" + parts[1];
            string tag;
            if (parts.Length == 4 && string.Equals(parts[3], "latest", StringComparison.OrdinalIgnoreCase))
            {
                using (var response = Net.Open("https://github.com/" + repo + "/releases/latest", 15000, true, "GitHub", "text/html"))
                {
                    string path = response.ResponseUri.AbsolutePath;
                    int at = path.IndexOf("/releases/tag/", StringComparison.OrdinalIgnoreCase);
                    if (at < 0) return null;                    // no release at all: GitHub shows the release list instead
                    tag = Uri.UnescapeDataString(path.Substring(at + "/releases/tag/".Length).TrimEnd('/'));
                }
            }
            else if (parts.Length >= 5 && string.Equals(parts[3], "tags", StringComparison.OrdinalIgnoreCase))
                tag = Uri.UnescapeDataString(string.Join("/", parts, 4, parts.Length - 4));
            else return null;
            if (tag.Length == 0) return null;
            string html;
            using (var response = Net.Open("https://github.com/" + repo + "/releases/expanded_assets/" + Uri.EscapeDataString(tag),
                                           15000, true, "GitHub", "text/html"))
            using (var reader = new StreamReader(response.GetResponseStream(), Encoding.UTF8)) html = reader.ReadToEnd();
            var assets = new List<object>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (string block in html.Split(new[] { "<li" }, StringSplitOptions.None))
            {
                var link = System.Text.RegularExpressions.Regex.Match(block, "href=\"(/[^/\"]+/[^/\"]+/releases/download/[^\"]+)\"",
                                                                      System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                if (!link.Success) continue;
                string href = WebUtility.HtmlDecode(link.Groups[1].Value);
                string name = Uri.UnescapeDataString(href.Substring(href.LastIndexOf('/') + 1));
                if (name.Length == 0 || !seen.Add(name)) continue;
                var digest = System.Text.RegularExpressions.Regex.Match(block, "sha256:([0-9a-fA-F]{64})");
                var asset = new Dictionary<string, object>();
                asset["name"] = name;
                asset["browser_download_url"] = "https://github.com" + href;
                asset["digest"] = digest.Success ? "sha256:" + digest.Groups[1].Value.ToLowerInvariant() : "";
                assets.Add(asset);
            }
            var release = new Dictionary<string, object>();
            release["tag_name"] = tag;
            release["draft"] = false;
            release["assets"] = assets;
            return release;
        }

        internal static string GitHubAssetSha256(string url)
        {
            Uri uri;
            if (!Uri.TryCreate(url, UriKind.Absolute, out uri) ||
                !string.Equals(uri.Host, "github.com", StringComparison.OrdinalIgnoreCase)) return null;
            string[] parts = uri.AbsolutePath.Trim('/').Split('/');
            if (uri.Scheme != "https" || parts.Length != 6 || parts[2] != "releases" || parts[3] != "download")
                return null;                                   // not a release file (a source archive): nothing to compare
            string api = "https://api.github.com/repos/" + parts[0] + "/" + parts[1] +
                         "/releases/tags/" + Uri.EscapeDataString(Uri.UnescapeDataString(parts[4]));
            Dictionary<string, object> release;
            try { release = ReadGitHubReleaseMetadata(api); }
            catch (UserError) { return null; }                 // GitHub busy: the https download itself still goes ahead
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
                return valid ? sha : null;                     // older uploads have no digest: install without one
            }
            return null;
        }

        // Callers resolving /releases/latest supply the digest from that same release asset.
        // Direct GitHub asset URLs (including mirrored bundles) resolve their digest here.
        public static List<string> InstallAddonZip(string url, string addonDir, string keepAs, string expectedSha256)
        {
            return InstallAddonZip(url, addonDir, keepAs, expectedSha256, null);
        }

        // expectedSha256: the published digest, null to look it up on GitHub, or NoDigest when none exists (a source archive).
        // expectedFolders: the catalog's folders for this addon. With them, a download without any addon in it throws
        // NoAddonInPackageError so the caller can try the release's next file; without them (a ZIP on this PC) a ZIP
        // with no .toc anywhere is unpacked as it is, as before.
        public const string NoDigest = "none";

        public static List<string> InstallAddonZip(string url, string addonDir, string keepAs, string expectedSha256, IList<string> expectedFolders)
        {
            if (string.Equals(expectedSha256, NoDigest, StringComparison.OrdinalIgnoreCase)) expectedSha256 = null;
            else if (!File.Exists(url) && string.IsNullOrWhiteSpace(expectedSha256))
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
                else actualSha256 = Net.Download(url, tmp, true, "addon", null, null, 0);
                if (!string.IsNullOrWhiteSpace(expectedSha256) &&
                    !string.Equals(actualSha256, expectedSha256.Trim(), StringComparison.OrdinalIgnoreCase))
                    throw new DigestMismatchError("The downloaded addon does not match the SHA-256 published for this exact addon release. Nothing was installed.");
                if (Kind(tmp) != "zip")
                    throw new UserError("The link did not give a .zip file. It must be a direct download link " +
                                        "(share pages such as LimeWire do not work).");
                bool catalog = expectedFolders != null && expectedFolders.Count > 0;
                var tops = ExtractAddonPackage(tmp, addonDir, expectedFolders);
                if (tops == null)
                {
                    if (catalog) throw new NoAddonInPackageError("The download has no addon folder in it (no .toc file).");
                    tops = ExtractZip(tmp, addonDir);
                }
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

        // ---------------------------------------------------------- addon packages
        // A folder of the ZIP holding .toc files: an addon (or a wrapper around one, as GitHub's source archives are).
        sealed class TocFolder
        {
            public string Path;              // inside the ZIP, '/' separated, no trailing '/'; "" for the ZIP's root
            public string Segment;           // the folder's own name ("" for the root)
            public List<string> Tocs = new List<string>();   // .toc names without ".toc"
            public string Name;              // the AddOns folder it installs as
            public bool Proper;              // its own name is one of its .toc names: an addon exactly as the game wants it
            public int Depth;
            public List<int> Version;        // "## Version:" of its own .toc, or null
            public string Parent { get { int at = Path.LastIndexOf('/'); return at < 0 ? (Path.Length == 0 ? null : "") : Path.Substring(0, at); } }
        }

        static bool SkippedPath(string path)
        {
            foreach (string part in path.Split('/'))
                if (part == "__MACOSX" || (part.Length > 1 && part[0] == '.')) return true;
            return false;
        }

        static List<int> TocVersion(ZipArchiveEntry entry)
        {
            try
            {
                string text;
                using (var reader = new StreamReader(entry.Open(), Encoding.UTF8)) text = reader.ReadToEnd();
                var m = System.Text.RegularExpressions.Regex.Match(text, @"^\s*##\s*Version\s*:\s*(.+)$",
                    System.Text.RegularExpressions.RegexOptions.Multiline | System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                if (!m.Success) return null;
                var parts = new List<int>();
                foreach (System.Text.RegularExpressions.Match d in System.Text.RegularExpressions.Regex.Matches(m.Groups[1].Value, @"\d+"))
                {
                    int n;
                    if (int.TryParse(d.Value, out n)) parts.Add(n);
                }
                return parts.Count > 0 ? parts : null;
            }
            catch (Exception) { return null; }
        }

        // >0 when a is the better copy of the same addon: the newer version, then the one named as the game wants, then the shallower
        static int CompareTocFolders(TocFolder a, TocFolder b)
        {
            if (a.Version != null && b.Version != null)
            {
                for (int i = 0; i < Math.Max(a.Version.Count, b.Version.Count); i++)
                {
                    int x = i < a.Version.Count ? a.Version[i] : 0, y = i < b.Version.Count ? b.Version[i] : 0;
                    if (x != y) return x > y ? 1 : -1;
                }
            }
            if (a.Proper != b.Proper) return a.Proper ? 1 : -1;
            if (a.Depth != b.Depth) return a.Depth < b.Depth ? 1 : -1;
            return 0;
        }

        static bool Inside(string path, string folder)
        {
            return folder.Length == 0 ? path.Length > 0 : path.StartsWith(folder + "/", StringComparison.Ordinal);
        }

        // Unpacks only the addons found in a ZIP, each into its own folder of addonDir, whatever the ZIP's layout:
        // the folder itself, a folder inside a wrapper (GitHub's source archives), or the addon's files at the ZIP's root.
        // Loose files beside the addons (readmes, installers) are left out. Where a ZIP holds the same addon twice,
        // the newer "## Version:" wins. Returns the folders made, or null when the ZIP holds no .toc at all.
        public static List<string> ExtractAddonPackage(string zipPath, string addonDir, IList<string> expectedFolders)
        {
            var expected = new List<string>();
            if (expectedFolders != null)
                foreach (string f in expectedFolders)
                {
                    string bare = (f ?? "").Trim();
                    if (bare.EndsWith(Addons.DisabledSuffix, StringComparison.OrdinalIgnoreCase)) bare = bare.Substring(0, bare.Length - Addons.DisabledSuffix.Length);
                    if (bare.Length > 0) expected.Add(bare);
                }
            Func<string, bool> isExpected = delegate(string n)
            {
                foreach (string e in expected) if (string.Equals(e, n, StringComparison.OrdinalIgnoreCase)) return true;
                return false;
            };
            try
            {
                using (ZipArchive z = ZipFile.OpenRead(zipPath))
                {
                    var folders = new Dictionary<string, TocFolder>(StringComparer.Ordinal);
                    foreach (ZipArchiveEntry e in z.Entries)
                    {
                        string path = e.FullName.Replace('\\', '/').TrimStart('/');
                        if (path.EndsWith("/") || !path.EndsWith(".toc", StringComparison.OrdinalIgnoreCase) || SkippedPath(path)) continue;
                        int slash = path.LastIndexOf('/');
                        string dir = slash < 0 ? "" : path.Substring(0, slash);
                        string toc = System.IO.Path.GetFileNameWithoutExtension(path.Substring(slash + 1));
                        if (toc.Length == 0) continue;
                        TocFolder folder;
                        if (!folders.TryGetValue(dir, out folder))
                        {
                            folder = new TocFolder { Path = dir, Segment = dir.Substring(dir.LastIndexOf('/') + 1), Depth = dir.Length == 0 ? 0 : dir.Split('/').Length };
                            folders[dir] = folder;
                        }
                        folder.Tocs.Add(toc);
                    }
                    if (folders.Count == 0) return null;

                    foreach (TocFolder f in folders.Values)
                    {
                        string own = null, wanted = null;
                        foreach (string t in f.Tocs)
                        {
                            if (f.Segment.Length > 0 && string.Equals(t, f.Segment, StringComparison.OrdinalIgnoreCase)) own = f.Segment;
                            if (wanted == null && isExpected(t)) wanted = t;
                        }
                        f.Proper = own != null;
                        if (own != null) f.Name = own;
                        else if (wanted != null) f.Name = wanted;                        // "CreedUI-1.2/CreedUI.toc" installs as CreedUI
                        else if (f.Segment.Length > 0 && isExpected(f.Segment)) f.Name = f.Segment;
                        else if (f.Tocs.Count == 1 || f.Segment.Length == 0) f.Name = f.Tocs[0];
                        else f.Name = f.Segment;
                        foreach (char c in System.IO.Path.GetInvalidFileNameChars()) if (f.Name != null && f.Name.IndexOf(c) >= 0) f.Name = null;
                        if (f.Name == "." || f.Name == "..") f.Name = null;
                        string versionToc = (f.Path.Length == 0 ? "" : f.Path + "/") + f.Name + ".toc";
                        foreach (ZipArchiveEntry e in z.Entries)
                            if (f.Name != null && string.Equals(e.FullName.Replace('\\', '/').TrimStart('/'), versionToc, StringComparison.OrdinalIgnoreCase))
                            { f.Version = TocVersion(e); break; }
                    }

                    // the best copy of each addon
                    var best = new Dictionary<string, TocFolder>(StringComparer.OrdinalIgnoreCase);
                    foreach (TocFolder f in folders.Values)
                    {
                        if (f.Name == null) continue;
                        TocFolder held;
                        if (!best.TryGetValue(f.Name, out held) || CompareTocFolders(f, held) > 0) best[f.Name] = f;
                    }

                    var chosen = new List<TocFolder>();
                    foreach (TocFolder f in best.Values) if (isExpected(f.Name)) chosen.Add(f);
                    if (chosen.Count > 0)
                    {
                        // addons packed beside the expected ones (a bundle's second folder) come too
                        var parents = new List<string>();
                        foreach (TocFolder f in chosen) parents.Add(f.Parent);
                        foreach (TocFolder f in best.Values)
                            if (!chosen.Contains(f) && f.Proper && parents.Contains(f.Parent)) chosen.Add(f);
                    }
                    else
                    {
                        // nothing by the expected names (renamed, or a ZIP from this PC): the outermost addons in it
                        int top = int.MaxValue;
                        foreach (TocFolder f in best.Values) top = Math.Min(top, f.Depth);
                        foreach (TocFolder f in best.Values) if (f.Depth == top) chosen.Add(f);
                    }
                    // an addon found inside another chosen one travels with it
                    var kept = new List<TocFolder>();
                    foreach (TocFolder f in chosen)
                    {
                        bool nested = false;
                        foreach (TocFolder g in chosen) if (g != f && Inside(f.Path, g.Path)) nested = true;
                        if (!nested) kept.Add(f);
                    }
                    if (kept.Count == 0) return null;

                    string root = System.IO.Path.GetFullPath(addonDir).TrimEnd('\\') + "\\";
                    var names = new List<string>();
                    foreach (TocFolder f in kept)
                    {
                        string folderRoot = System.IO.Path.GetFullPath(System.IO.Path.Combine(root, f.Name)).TrimEnd('\\') + "\\";
                        if (!folderRoot.StartsWith(root, StringComparison.OrdinalIgnoreCase))
                            throw new UserError("The .zip contains an unsafe path; nothing was installed.");
                        Directory.CreateDirectory(folderRoot);
                        foreach (ZipArchiveEntry e in z.Entries)
                        {
                            string path = e.FullName.Replace('\\', '/').TrimStart('/');
                            if (f.Path.Length > 0 && !Inside(path, f.Path)) continue;
                            string rel = f.Path.Length == 0 ? path : path.Substring(f.Path.Length + 1);
                            if (rel.Length == 0 || SkippedPath(rel)) continue;
                            string target = System.IO.Path.GetFullPath(System.IO.Path.Combine(folderRoot, rel.Replace('/', '\\')));
                            if (!target.StartsWith(folderRoot, StringComparison.OrdinalIgnoreCase))
                                throw new UserError("The .zip contains an unsafe path; nothing was installed.");
                            if (rel.EndsWith("/")) { Directory.CreateDirectory(target); continue; }
                            Directory.CreateDirectory(System.IO.Path.GetDirectoryName(target));
                            if (File.Exists(target)) File.SetAttributes(target, FileAttributes.Normal);
                            e.ExtractToFile(target, true);
                        }
                        names.Add(f.Name);
                    }
                    return names;
                }
            }
            catch (InvalidDataException)
            {
                throw new UserError("The file is damaged or incomplete.");
            }
        }
    }
}
