// Cloud sync engine of the Azeroth Survivor Launcher. Its framework comes from KeijinDE's sync engine, used with
// his blessing (MIT License, Copyright (c) 2026 KeijinDE, see CREDITS.md).
// Same sync folder layout, fingerprints and rules as that engine and the earlier Python launcher, so all of
// them can share one folder and this launcher keeps the sync memory of the old one.
//
// No merging: per area one complete state wins and the overwritten one is backed up as a ZIP first.
// Areas: save game and addon settings. The AddOns folder itself is not synced (an older copy from another PC could
// replace an addon updated here): an addon whose settings come from the cloud is installed by the launcher instead.
// Uploads update the cloud folder in place (no .asl-new / .asl-old
// folders inside the cloud drive); downloads swap the local folder through a staging copy.
//   <sync folder>\manifest.json   fingerprints of the last upload
//   <sync folder>\data\save|addons|saved
//   <sync folder>\history\        cloud backups (last 10 per area)
//   <state folder>\state.json     fingerprints of the last sync on this PC
//   <state folder>\backups\       local backups (last 20 per area)
//   <state folder>\downloads\     copies of the last 5 cloud downloads (last resort, restored only by hand)
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;

namespace AzerothLauncher
{
    public enum Level { Info, Good, Warn, Error, Dim }
    public enum Choice { Cloud, Local, Abort }

    public class SyncError : UserError
    {
        public SyncError(string message) : base(message) { }
    }

    public class SyncSettings
    {
        // ...\AddOns\AddOns is never what the game reads: the outer AddOns folder is.
        public static string OuterAddonsDir(string path)
        {
            string trimmed = (path ?? "").TrimEnd('\\', '/');
            string parent = Path.GetDirectoryName(trimmed);
            if (!string.IsNullOrEmpty(parent) &&
                Path.GetFileName(trimmed).Equals("AddOns", StringComparison.OrdinalIgnoreCase) &&
                Path.GetFileName(parent).Equals("AddOns", StringComparison.OrdinalIgnoreCase))
                return parent;
            return path;
        }

        public string GameExe, CloudDir, SaveDir, ConfigDir, StateDir, AddonsDir, SavedDir;

        public SyncSettings(string gameExe, string cloudDir, string saveDir, string configDir, string stateDir,
                            string addonsDir, string savedDir)
        {
            GameExe = gameExe;
            CloudDir = cloudDir;
            SaveDir = saveDir;
            ConfigDir = configDir;
            StateDir = stateDir;
            // AddOns / addon settings normally live inside the config folder; setup can point elsewhere
            AddonsDir = string.IsNullOrEmpty(addonsDir) ? Path.Combine(configDir ?? "", "AddOns") : addonsDir;
            AddonsDir = OuterAddonsDir(AddonsDir);
            SavedDir = string.IsNullOrEmpty(savedDir) ? Path.Combine(configDir ?? "", "saved") : savedDir;
        }
    }

    public class SyncSet
    {
        public string Name, Label, Dir, Cloud;
        public string[] Exclude;
        public bool Optional;

        public SyncSet(string name, string label, string localDir, string cloudData, string[] exclude, bool optional)
        {
            Name = name;
            Label = label;
            Dir = localDir;
            Cloud = Path.Combine(cloudData, name);
            Exclude = exclude ?? new string[0];
            Optional = optional;
        }
    }

    public class FileEntry
    {
        public string Rel, Full;
        public long Length;
    }

    public class SyncEngine
    {
        public const string NoneHash = "NONE", EmptyHash = "EMPTY";
        const string StageSuffix = ".asl-new", OldSuffix = ".asl-old", HashPrefix = "2:";
        // the names the first versions used for the same leftovers; still cleaned up
        static readonly string[][] EarlierSuffixes = { new[] { ".azsync-new", ".azsync-old" } };
        const int KeepLocalBackups = 20, KeepCloudBackups = 10, KeepDownloads = 5;
        public static readonly string Machine = Environment.MachineName;

        class Eval
        {
            public SyncSet Set;
            public string Local, Cloud, Last, Expected;
        }

        readonly SyncSettings S;
        public Action<string, Level> Log = delegate(string m, Level l) { };
        public Func<string, bool> Confirm = delegate(string m) { return false; };
        public Func<List<SyncSet>, bool, Choice> AskConflict = delegate(List<SyncSet> s, bool d) { return Choice.Abort; };
        public Action<string, int> Progress = delegate(string t, int p) { };
        public Func<bool> GameRunning;
        public List<SyncSet> Sets;

        string pLabel = "";
        long pDone, pTotal;
        int pFiles, pFilesTotal, pLastPct = -1;
        DateTime pLast = DateTime.MinValue;

        public SyncEngine(SyncSettings settings)
        {
            S = settings;
            GameRunning = delegate { return Util.GameRunning(); };
            string cloudData = Path.Combine(settings.CloudDir ?? "", "data");
            Sets = new List<SyncSet>();
            Sets.Add(new SyncSet("save", "Save game", settings.SaveDir, cloudData, new[] { "language.txt" }, false));
            Sets.Add(new SyncSet("saved", "Addon settings", settings.SavedDir, cloudData, null, true));
        }

        string CloudData { get { return Path.Combine(S.CloudDir, "data"); } }
        string History { get { return Path.Combine(S.CloudDir, "history"); } }
        string ManifestPath { get { return Path.Combine(S.CloudDir, "manifest.json"); } }
        string StateFile { get { return Path.Combine(S.StateDir, "state.json"); } }
        string DownloadsDir { get { return Path.Combine(S.StateDir, "downloads"); } }

        // ---------- listing ----------
        // All files below directory (relative paths use backslashes, as the sync folder format expects).
        public static List<FileEntry> ListFiles(string directory, string[] exclude)
        {
            var output = new List<FileEntry>();
            if (string.IsNullOrEmpty(directory) || !Directory.Exists(directory)) return output;
            string root = Path.GetFullPath(directory).TrimEnd('\\', '/');
            var skip = new HashSet<string>(StringComparer.Ordinal);
            if (exclude != null) foreach (string e in exclude) skip.Add(e.ToLowerInvariant());
            Walk(root, root, skip, output);
            return output;
        }

        static void Walk(string root, string dir, HashSet<string> skip, List<FileEntry> output)
        {
            foreach (string sub in Directory.GetDirectories(dir))
            {
                if (Util.IsLink(sub))
                    throw new SyncError("Linked folders (junction or symbolic link) inside synced folders are not supported: " + sub);
                Walk(root, sub, skip, output);
            }
            foreach (string f in Directory.GetFiles(dir))
            {
                string rel = f.Substring(root.Length + 1);
                if (skip.Contains(rel.ToLowerInvariant())) continue;
                var fe = new FileEntry();
                fe.Rel = rel;
                fe.Full = f;
                fe.Length = new FileInfo(f).Length;
                output.Add(fe);
            }
        }

        public static string NewestTime(string directory, string[] exclude)
        {
            try
            {
                var files = ListFiles(directory, exclude);
                if (files.Count == 0) return "-";
                DateTime newest = DateTime.MinValue;
                foreach (var f in files)
                {
                    DateTime t = File.GetLastWriteTime(f.Full);
                    if (t > newest) newest = t;
                }
                return newest.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture);
            }
            catch (Exception) { return "-"; }
        }

        // ---------- checks before anything is touched ----------
        static List<string> BroadFolders()
        {
            var output = new List<string>();
            string home = Util.Home;
            var cands = new List<string> { home, Path.Combine(home, "Desktop"), Path.Combine(home, "Documents"),
                Util.AppData, Util.LocalAppData, Environment.GetEnvironmentVariable("WINDIR"),
                Environment.GetEnvironmentVariable("ProgramFiles"), Environment.GetEnvironmentVariable("ProgramFiles(x86)"),
                Environment.GetEnvironmentVariable("ProgramData") };
            foreach (string c in cands)
                if (!string.IsNullOrEmpty(c))
                    try { output.Add(Util.Canon(c).ToLowerInvariant()); } catch (Exception) { }
            return output;
        }

        public void Validate()
        {
            if ((S.CloudDir ?? "").Trim().Length == 0)
                throw new SyncError("No sync folder set. Choose a folder in Google Drive, OneDrive, Dropbox or similar in Options.");
            if ((S.SaveDir ?? "").Trim().Length == 0) throw new SyncError("No save game folder set.");
            if ((S.ConfigDir ?? "").Trim().Length == 0) throw new SyncError("No game config folder set.");
            string cloud, state;
            var local = new List<KeyValuePair<string, string>>();
            try
            {
                cloud = Util.Canon(S.CloudDir);
                state = Util.Canon(S.StateDir);
                foreach (var s in Sets) local.Add(new KeyValuePair<string, string>(s.Label, Util.Canon(s.Dir)));
            }
            catch (Exception ex)
            {
                throw new SyncError("A folder path is invalid: " + ex.Message);
            }
            if (Util.IsDriveRoot(cloud))
                throw new SyncError("The sync folder must not be a drive root (" + cloud + "). Use a folder inside it.");
            var broad = BroadFolders();
            foreach (var kv in local)
            {
                string p = kv.Value;
                if (Util.IsDriveRoot(p)) throw new SyncError(kv.Key + " folder must not be a drive root: " + p);
                if (broad.Contains(p.ToLowerInvariant()))
                    throw new SyncError(kv.Key + " folder is a general Windows folder, choose the game's own folder: " + p);
                if (Util.Overlap(p, cloud))
                    throw new SyncError(kv.Key + " folder and sync folder overlap (" + p + " / " + cloud + "). They must be separate.");
                if (Util.Overlap(p, state)) throw new SyncError(kv.Key + " folder overlaps the launcher data folder " + state + ".");
            }
            for (int i = 0; i < local.Count; i++)
                for (int j = i + 1; j < local.Count; j++)
                    if (Util.Overlap(local[i].Value, local[j].Value))
                        throw new SyncError(local[i].Key + " and " + local[j].Key + " folders overlap (" + local[i].Value +
                                            " / " + local[j].Value + ").");
            if (Util.Overlap(cloud, state)) throw new SyncError("The sync folder overlaps the launcher data folder " + state + ".");
            var all = new List<string>();
            foreach (var kv in local) all.Add(kv.Value);
            all.Add(cloud);
            foreach (string p in all)
                if (Directory.Exists(p) && Util.IsLink(p))
                    throw new SyncError("Linked folders (junction or symbolic link) are not supported: " + p);
        }

        // ---------- progress ----------
        void BeginProgress(string label, List<FileEntry> files)
        {
            pLabel = label;
            pTotal = 0;
            foreach (var f in files) pTotal += f.Length;
            pFilesTotal = files.Count;
            pDone = 0;
            pFiles = 0;
            pLastPct = -1;
            pLast = DateTime.MinValue;
            Report(true);
        }

        public void EndProgress() { Progress("", -1); }

        void AddBytes(long n) { pDone += n; Report(false); }
        void FileDone() { pFiles++; Report(false); }

        void Report(bool force)
        {
            int pct;
            if (pTotal > 0) pct = (int)Math.Min(100, pDone * 100 / pTotal);
            else pct = pFilesTotal > 0 ? pFiles * 100 / pFilesTotal : 100;
            bool last = pFiles >= pFilesTotal;
            DateTime now = DateTime.Now;
            if (!force && !last && (pct == pLastPct || (now - pLast).TotalSeconds < 0.05)) return;
            pLastPct = pct;
            pLast = now;
            Progress(pLabel + ": " + Math.Min(pFiles, pFilesTotal) + " of " + pFilesTotal + " files, " +
                     Util.SizeText(Math.Min(pDone, pTotal)) + " of " + Util.SizeText(pTotal), pct);
        }

        List<FileEntry> AllFiles(List<SyncSet> sets)
        {
            var output = new List<FileEntry>();
            foreach (var s in sets)
            {
                output.AddRange(ListFiles(s.Dir, s.Exclude));
                output.AddRange(ListFiles(s.Cloud, s.Exclude));
            }
            return output;
        }

        // ---------- fingerprints ----------
        // Format 2: SHA256 over the sorted 'relative path (lower case):FILE SHA256' lines.
        public string TreeHash(string directory, string[] exclude, bool report)
        {
            if (string.IsNullOrEmpty(directory) || !Directory.Exists(directory)) return NoneHash;
            var files = ListFiles(directory, exclude);
            if (files.Count == 0) return EmptyHash;
            var lines = new List<string>();
            var buf = new byte[1 << 16];
            foreach (var f in files)
            {
                using (var sha = SHA256.Create())
                using (var fs = new FileStream(f.Full, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                {
                    int n;
                    while ((n = fs.Read(buf, 0, buf.Length)) > 0)
                    {
                        sha.TransformBlock(buf, 0, n, null, 0);
                        if (report) AddBytes(n);
                    }
                    sha.TransformFinalBlock(new byte[0], 0, 0);
                    lines.Add(f.Rel.ToLowerInvariant() + ":" + Net.Hex(sha.Hash).ToUpperInvariant());
                }
                if (report) FileDone();
            }
            lines.Sort(StringComparer.Ordinal);
            using (var sha = SHA256.Create())
                return Net.Hex(sha.ComputeHash(Encoding.UTF8.GetBytes(string.Join("\n", lines)))).ToUpperInvariant();
        }

        static bool IsEmpty(string h) { return h == NoneHash || h == EmptyHash; }

        // ---------- copy with staging ----------
        // Leftovers of an interrupted sync: "<folder>.asl-old" is put back when the folder itself is missing,
        // otherwise it and "<folder>.asl-new" are removed (also under the first versions' names). A leftover that
        // cannot be removed (no permission, locked by the cloud app) never stops the sync: it is only reported.
        void Recover(string directory)
        {
            foreach (var e in EarlierSuffixes) Recover(directory, directory + e[0], directory + e[1]);
            Recover(directory, directory + StageSuffix, directory + OldSuffix);
        }

        void Recover(string directory, string stage, string old)
        {
            if (Directory.Exists(old))
            {
                if (!Directory.Exists(directory))
                {
                    try
                    {
                        Directory.Move(old, directory);
                        Log("  Restored " + directory + " after an interrupted sync.", Level.Warn);
                    }
                    catch (Exception ex)
                    {
                        throw new SyncError("An interrupted sync left your data in " + old + " and it could not be moved " +
                                            "back (" + ex.Message + "). Rename that folder to " + Path.GetFileName(directory) +
                                            " yourself, then sync again.");
                    }
                }
                else RemoveLeftover(old);
            }
            if (Directory.Exists(stage)) RemoveLeftover(stage);
        }

        void RemoveLeftover(string path)
        {
            try { Util.DeleteTree(path); }
            catch (Exception ex)
            {
                Log("  An old sync folder could not be removed and is ignored: " + path + " (" + ex.Message + "). " +
                    "You can delete it yourself.", Level.Dim);
            }
        }

        void RecoverAll()
        {
            foreach (var s in Sets)
                foreach (string d in new[] { s.Dir, s.Cloud })
                {
                    string full = Path.GetFullPath(d).TrimEnd('\\', '/');
                    string parent = Path.GetDirectoryName(full);
                    if (!string.IsNullOrEmpty(parent) && Directory.Exists(parent)) Recover(full);
                }
        }

        void CopyFile(string src, string dst)
        {
            var buf = new byte[1 << 16];
            using (var fi = new FileStream(src, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
            using (var fo = new FileStream(dst, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                int n;
                while ((n = fi.Read(buf, 0, buf.Length)) > 0)
                {
                    fo.Write(buf, 0, n);
                    AddBytes(n);
                }
            }
            File.SetLastWriteTimeUtc(dst, File.GetLastWriteTimeUtc(src));
            File.SetLastAccessTimeUtc(dst, File.GetLastAccessTimeUtc(src));
        }

        // Makes dst an exact copy of src. Copies into a staging folder, verifies it against expectedHash and only
        // then swaps it in. Excluded files of dst are kept.
        void Mirror(string src, string dst, string[] exclude, string expectedHash)
        {
            dst = Path.GetFullPath(dst).TrimEnd('\\', '/');
            string stage = dst + StageSuffix, old = dst + OldSuffix;
            Directory.CreateDirectory(Path.GetDirectoryName(dst));
            Util.DeleteTree(stage);
            Directory.CreateDirectory(stage);
            try
            {
                foreach (var f in ListFiles(src, exclude))
                {
                    string target = Path.Combine(stage, f.Rel);
                    Directory.CreateDirectory(Path.GetDirectoryName(target));
                    CopyFile(f.Full, target);
                    FileDone();
                }
                if (TreeHash(stage, exclude, false) != expectedHash)
                    throw new SyncError("The source changed while copying (" + src + "). Nothing was replaced, please sync again.");
                foreach (string ex in exclude)                       // keep local only files such as language.txt
                {
                    string keep = Path.Combine(dst, ex);
                    if (File.Exists(keep)) File.Copy(keep, Path.Combine(stage, ex), true);
                }
            }
            catch (Exception)
            {
                Util.DeleteTreeQuiet(stage);
                throw;
            }
            bool hadDst = Directory.Exists(dst);
            try
            {
                if (hadDst) Directory.Move(dst, old);
            }
            catch (Exception ex)
            {
                Util.DeleteTreeQuiet(stage);
                throw new SyncError("The folder is in use and was not replaced: " + dst + " (" + ex.Message + "). " +
                                    "Close programs or Explorer windows that use it and try again.");
            }
            try
            {
                Directory.Move(stage, dst);
            }
            catch (Exception)
            {
                if (hadDst && !Directory.Exists(dst)) Directory.Move(old, dst);
                Util.DeleteTreeQuiet(stage);
                throw;
            }
            try { Util.DeleteTree(old); }
            catch (Exception ex)
            {
                Log("    Could not remove the old copy " + old + " (" + ex.Message + "), it is cleaned up next time.", Level.Dim);
            }
        }

        // Makes the cloud folder dst an exact copy of src without a second folder next to it: cloud drives (Google
        // Drive, OneDrive ...) upload every folder they see and may not let a folder be renamed or removed, so the
        // swap that Mirror does (dst.asl-new / dst.asl-old) is not used there. The files are first copied to the
        // launcher's temp folder and checked (the game may still be writing), then only the changed files are
        // written into dst and the ones that are gone are removed; finally dst is checked against expectedHash.
        void UpdateInPlace(string src, string dst, string[] exclude, string expectedHash)
        {
            dst = Path.GetFullPath(dst).TrimEnd('\\', '/');
            string stage = Path.Combine(Util.TempDir, "upload-" + Path.GetFileName(dst));
            Util.DeleteTree(stage);
            Directory.CreateDirectory(stage);
            try
            {
                var files = ListFiles(src, exclude);
                foreach (var f in files)
                {
                    string target = Path.Combine(stage, f.Rel);
                    Directory.CreateDirectory(Path.GetDirectoryName(target));
                    CopyFile(f.Full, target);
                }
                if (TreeHash(stage, exclude, false) != expectedHash)
                    throw new SyncError("The source changed while copying (" + src + "). Nothing was replaced, please sync again.");

                Directory.CreateDirectory(dst);
                var want = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (var f in ListFiles(stage, exclude))
                {
                    want.Add(f.Rel);
                    string target = Path.Combine(dst, f.Rel);
                    if (File.Exists(target) && Same(f.Full, target)) { FileDone(); continue; }
                    Directory.CreateDirectory(Path.GetDirectoryName(target));
                    if (File.Exists(target)) File.SetAttributes(target, FileAttributes.Normal);
                    File.Copy(f.Full, target, true);
                    File.SetLastWriteTimeUtc(target, File.GetLastWriteTimeUtc(f.Full));
                    FileDone();
                }
                foreach (var f in ListFiles(dst, exclude))             // files that are no longer there
                    if (!want.Contains(f.Rel))
                    {
                        File.SetAttributes(f.Full, FileAttributes.Normal);
                        File.Delete(f.Full);
                    }
                RemoveEmptyFolders(dst);
            }
            catch (UnauthorizedAccessException ex)
            {
                throw new SyncError("The sync folder could not be written (" + ex.Message + "). Check that your cloud app is " +
                                    "running and that you may change files in " + dst + ".");
            }
            catch (IOException ex)
            {
                throw new SyncError("The sync folder could not be written (" + ex.Message + "). Close programs that use " +
                                    dst + " and try again.");
            }
            finally { Util.DeleteTreeQuiet(stage); }
            if (TreeHash(dst, exclude, false) != expectedHash)
                throw new SyncError("The cloud copy does not match your files after the upload (" + dst + "). " +
                                    "Your cloud app may be changing it right now; please sync again in a moment.");
        }

        // same size and same content
        static bool Same(string a, string b)
        {
            try
            {
                if (new FileInfo(a).Length != new FileInfo(b).Length) return false;
                return Net.FileSha256(a) == Net.FileSha256(b);
            }
            catch (Exception) { return false; }
        }

        static void RemoveEmptyFolders(string root)
        {
            foreach (string d in Directory.GetDirectories(root))
            {
                if (Util.IsLink(d)) continue;
                RemoveEmptyFolders(d);
                try
                {
                    if (Directory.GetFileSystemEntries(d).Length == 0) Directory.Delete(d);
                }
                catch (Exception) { }
            }
        }

        static string Stamp()
        {
            DateTime now = DateTime.Now;
            return now.ToString("yyyy-MM-dd_HH-mm-ss-", CultureInfo.InvariantCulture) + now.Millisecond.ToString("000");
        }

        void Backup(string src, string[] exclude, string backupDir, string prefix, int keep)
        {
            Backup(src, exclude, backupDir, prefix, keep, Stamp());
        }

        void Backup(string src, string[] exclude, string backupDir, string prefix, int keep, string stamp)
        {
            var files = ListFiles(src, exclude);
            if (files.Count == 0) return;
            Directory.CreateDirectory(backupDir);
            string zpath = Path.Combine(backupDir, prefix + "_" + stamp + "_" + Machine + ".zip");
            int n = 2;
            while (File.Exists(zpath))
                zpath = Path.Combine(backupDir, prefix + "_" + stamp + "_" + Machine + "_" + (n++) + ".zip");
            string tmp = zpath + ".tmp";
            using (var fs = new FileStream(tmp, FileMode.Create))
            using (var zip = new ZipArchive(fs, ZipArchiveMode.Create))
            {
                foreach (var f in files)
                {
                    var entry = zip.CreateEntry(f.Rel.Replace('\\', '/'), CompressionLevel.Optimal);
                    entry.LastWriteTime = new DateTimeOffset(File.GetLastWriteTime(f.Full));
                    using (Stream es = entry.Open())
                    using (var fi = new FileStream(f.Full, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                        fi.CopyTo(es);
                    AddBytes(f.Length);
                    FileDone();
                }
            }
            File.Move(tmp, zpath);
            var mine = new List<string>();
            foreach (string x in Directory.GetFiles(backupDir))
            {
                string name = Path.GetFileName(x);
                if (name.StartsWith(prefix + "_", StringComparison.Ordinal) && name.EndsWith(".zip", StringComparison.Ordinal))
                    mine.Add(name);
            }
            mine.Sort(StringComparer.Ordinal);
            mine.Reverse();
            for (int i = keep; i < mine.Count; i++)
                try { File.Delete(Path.Combine(backupDir, mine[i])); } catch (Exception) { }
            Log("    Backup: " + zpath, Level.Dim);
        }

        // ---------- state (this PC) and manifest (cloud) ----------
        Dictionary<string, string> LoadState()
        {
            var h = new Dictionary<string, string>();
            if (!File.Exists(StateFile)) return h;
            try
            {
                var j = Json.AsDict(Json.Parse(File.ReadAllText(StateFile, Encoding.UTF8)));
                var hashes = Json.AsDict(Json.Get(j, "Hashes"));
                if (hashes != null)
                    foreach (var kv in hashes) h[kv.Key] = kv.Value == null ? null : Convert.ToString(kv.Value);
            }
            catch (Exception ex)
            {
                string copy = StateFile + ".broken-" + DateTime.Now.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture);
                try { File.Copy(StateFile, copy, true); } catch (Exception) { }
                Log("  The sync memory could not be read (" + ex.Message + "). A copy was kept as " + copy +
                    ". Every area is treated as never synced.", Level.Warn);
            }
            return h;
        }

        static string LastHash(Dictionary<string, string> state, string name)
        {
            string v;
            if (!state.TryGetValue(name, out v) || v == null) return null;
            return v.StartsWith(HashPrefix, StringComparison.Ordinal) ? v.Substring(HashPrefix.Length) : null;
        }

        static string NowIso()
        {
            return DateTimeOffset.Now.ToString("yyyy-MM-dd'T'HH:mm:ss.ffffffzzz", CultureInfo.InvariantCulture);
        }

        void SetLastHash(string name, string value)
        {
            var h = LoadState();
            h[name] = HashPrefix + value;
            Directory.CreateDirectory(S.StateDir);
            var hashes = new Dictionary<string, object>();
            foreach (var kv in h) hashes[kv.Key] = kv.Value;
            var root = new Dictionary<string, object>();
            root["Hashes"] = hashes;
            root["LastSync"] = NowIso();
            root["Device"] = Machine;
            Util.WriteAtomic(StateFile, Json.Write(root, false));
        }

        Dictionary<string, object> LoadManifest()
        {
            try
            {
                if (File.Exists(ManifestPath))
                {
                    var m = Json.AsDict(Json.Parse(File.ReadAllText(ManifestPath, Encoding.UTF8)));
                    if (m != null) return m;
                }
            }
            catch (Exception) { }
            return new Dictionary<string, object>();
        }

        static string ExpectedHash(Dictionary<string, object> manifest, string name)
        {
            var area = Json.AsDict(Json.Get(Json.AsDict(Json.Get(manifest, "Areas")), name));
            object h = Json.Get(area, "Hash");
            return h == null ? null : Convert.ToString(h);
        }

        void WriteManifest(Dictionary<string, string> published, bool uploaded)
        {
            var old = LoadManifest();
            var areas = new Dictionary<string, object>();
            var oldAreas = Json.AsDict(Json.Get(old, "Areas"));
            if (oldAreas != null) foreach (var kv in oldAreas) areas[kv.Key] = kv.Value;
            foreach (var kv in published)
            {
                SyncSet sset = Sets.Find(delegate(SyncSet s) { return s.Name == kv.Key; });
                var files = ListFiles(Path.Combine(CloudData, kv.Key), sset.Exclude);
                long bytes = 0;
                foreach (var f in files) bytes += f.Length;
                var a = new Dictionary<string, object>();
                a["Hash"] = kv.Value;
                a["Files"] = (long)files.Count;
                a["Bytes"] = bytes;
                areas[kv.Key] = a;
            }
            object device = Machine, when = NowIso();
            if (!uploaded && old.ContainsKey("Device") && old.ContainsKey("Time"))
            {
                device = old["Device"];
                when = old["Time"];
            }
            var root = new Dictionary<string, object>();
            root["Format"] = 2L;
            root["Device"] = device;
            root["Time"] = when;
            root["Areas"] = areas;
            Util.WriteAtomic(ManifestPath, Json.Write(root, false));
        }

        public string CloudInfo()
        {
            try
            {
                var m = LoadManifest();
                if (m.ContainsKey("Time") && m.ContainsKey("Device"))
                {
                    DateTimeOffset t;
                    if (DateTimeOffset.TryParse(Convert.ToString(m["Time"], CultureInfo.InvariantCulture), CultureInfo.InvariantCulture,
                                                DateTimeStyles.None, out t))
                        return "last uploaded by " + Convert.ToString(m["Device"]) + " on " +
                               t.ToLocalTime().ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture);
                }
            }
            catch (Exception) { }
            return "no upload info";
        }

        // ---------- checks ----------
        void AssertNotRunning(string what)
        {
            if (GameRunning()) throw new SyncError("The game is running. Close it before you " + what + ".");
        }

        void AssertCloudParent()
        {
            string parent = Path.GetDirectoryName(Path.GetFullPath(S.CloudDir).TrimEnd('\\', '/'));
            if (string.IsNullOrEmpty(parent) || !Directory.Exists(parent))
                throw new SyncError("Sync folder not reachable: " + S.CloudDir + " (is your cloud drive running?)");
        }

        bool Available(SyncSet s, bool say)
        {
            string parent = Path.GetDirectoryName(Path.GetFullPath(s.Dir).TrimEnd('\\', '/'));
            if (s.Optional && !Directory.Exists(parent))
            {
                if (say)
                    Log("  " + s.Label + ": " + parent + " not found, skipped. Set the game config folder in Options.", Level.Warn);
                return false;
            }
            return true;
        }

        List<SyncSet> AvailableSets()
        {
            var r = new List<SyncSet>();
            foreach (var s in Sets) if (Available(s, true)) r.Add(s);
            return r;
        }

        Eval Evaluate(SyncSet s, Dictionary<string, string> state, Dictionary<string, object> manifest)
        {
            var e = new Eval();
            e.Set = s;
            e.Local = TreeHash(s.Dir, s.Exclude, true);
            e.Cloud = TreeHash(s.Cloud, s.Exclude, true);
            e.Last = LastHash(state, s.Name);
            e.Expected = ExpectedHash(manifest, s.Name);
            return e;
        }

        static List<SyncSet> SetsOf(List<Eval> evals)
        {
            var r = new List<SyncSet>();
            foreach (var e in evals) r.Add(e.Set);
            return r;
        }

        // ---------- status (read only) ----------
        public bool Status()
        {
            Validate();
            Log("Sync folder: " + S.CloudDir, Level.Info);
            if (!Directory.Exists(S.CloudDir))
            {
                Log("  The sync folder does not exist yet. It is created on the first upload.", Level.Warn);
                return true;
            }
            Log("  " + CloudInfo(), Level.Dim);
            var state = LoadState();
            var manifest = LoadManifest();
            var sets = AvailableSets();
            BeginProgress("Checking files", AllFiles(sets));
            foreach (var s in sets)
            {
                var e = Evaluate(s, state, manifest);
                Level lv = Level.Warn;
                string text;
                if (e.Local == e.Cloud) { text = "in sync"; lv = Level.Good; }
                else if (IsEmpty(e.Cloud)) text = "cloud empty, upload";
                else if (e.Expected != null && e.Cloud != e.Expected) text = "cloud copy incomplete or still syncing, wait";
                else if (e.Cloud == e.Last) text = "local is newer, upload";
                else if (e.Local == e.Last || IsEmpty(e.Local)) text = "cloud is newer, download";
                else text = "both changed or never synced";
                Log("  " + s.Label + ": " + text + " (local " + NewestTime(s.Dir, s.Exclude) + ", cloud " +
                    NewestTime(s.Cloud, s.Exclude) + ")", lv);
            }
            EndProgress();
            return true;
        }

        // ---------- download ----------
        // force = the player explicitly wants the cloud state: one confirmation, no conflict questions.
        // Returns false if anything was cancelled or could not be downloaded.
        public bool Pull(bool force)
        {
            Log("Downloading from the cloud ...", Level.Info);
            Validate();
            AssertNotRunning("download");
            AssertCloudParent();
            if (!Directory.Exists(S.CloudDir))
            {
                Log("  The sync folder does not exist yet, nothing to download.", Level.Warn);
                return true;
            }
            RecoverAll();
            var state = LoadState();
            var manifest = LoadManifest();
            var take = new List<Eval>();
            var conflicts = new List<Eval>();
            bool complete = true;
            var sets = AvailableSets();
            BeginProgress("Checking files", AllFiles(sets));
            foreach (var s in sets)
            {
                var e = Evaluate(s, state, manifest);
                if (IsEmpty(e.Cloud))
                    Log("  " + s.Label + ": cloud is empty, nothing to download.", Level.Warn);
                else if (e.Local == e.Cloud)
                {
                    SetLastHash(s.Name, e.Local);
                    Log("  " + s.Label + ": up to date.", Level.Good);
                }
                else if (e.Expected != null && e.Cloud != e.Expected && !force)
                {
                    Log("  " + s.Label + ": the cloud copy does not match the last upload yet (incomplete or still syncing). " +
                        "Nothing replaced, try again in a moment.", Level.Warn);
                    complete = false;
                }
                else if (force || IsEmpty(e.Local) || e.Local == e.Last) take.Add(e);
                else if (e.Cloud == e.Last) Log("  " + s.Label + ": local is newer, kept.", Level.Good);
                else conflicts.Add(e);
            }
            EndProgress();
            if (force && take.Count > 0)
            {
                var names = new List<string>();
                foreach (var t in take)
                    names.Add(t.Set.Label + (t.Expected != null && t.Cloud != t.Expected
                        ? " (does not match the last upload, maybe still syncing)" : ""));
                if (!Confirm("Replace your local data with the cloud state?\n\n" + string.Join(", ", names) +
                             "\n\nA backup is made first."))
                {
                    Log("  Cancelled, nothing changed.", Level.Warn);
                    return false;
                }
            }
            if (conflicts.Count > 0)
            {
                Choice choice = AskConflict(SetsOf(conflicts), true);
                if (choice == Choice.Cloud) take.AddRange(conflicts);
                else if (choice == Choice.Local)
                {
                    foreach (var c in conflicts)
                    {
                        SetLastHash(c.Set.Name, c.Cloud);
                        Log("  " + c.Set.Label + ": local state kept, it will replace the cloud on the next upload.", Level.Good);
                    }
                }
                else
                {
                    Log("  Cancelled, nothing changed.", Level.Warn);
                    return false;
                }
            }
            if (take.Count == 0) return complete;
            AssertNotRunning("download");
            BeginProgress("Downloading", AllFiles(SetsOf(take)));
            string stamp = Stamp();                          // one name for all areas of this download
            foreach (var e in take)
            {
                Backup(e.Set.Dir, e.Set.Exclude, Path.Combine(S.StateDir, "backups"), e.Set.Name, KeepLocalBackups);
                Mirror(e.Set.Cloud, e.Set.Dir, e.Set.Exclude, e.Cloud);
                SetLastHash(e.Set.Name, e.Cloud);
                Log("  " + e.Set.Label + ": downloaded from the cloud.", Level.Good);
                KeepDownload(e.Set, stamp);
            }
            EndProgress();
            Log("  Cloud state: " + CloudInfo(), Level.Dim);
            return complete;
        }

        // ---------- last-resort copies of downloads ----------
        // After a download has been checked against its fingerprint, a ZIP of what arrived is kept on this PC
        // (last 5 per area). Sync never reads them; they are only restored by hand from the Cloud Save page.
        void KeepDownload(SyncSet set, string stamp)
        {
            try { Backup(set.Dir, set.Exclude, DownloadsDir, set.Name, KeepDownloads, stamp); }
            catch (Exception ex)
            {
                Log("    Could not keep a local copy of this download (" + ex.Message + ").", Level.Dim);
            }
        }

        public class KeptDownload
        {
            public string Stamp;                             // shared by all areas of one download
            public DateTime Time;
            public List<KeyValuePair<SyncSet, string>> Areas = new List<KeyValuePair<SyncSet, string>>();
        }

        // The kept downloads, newest first.
        public List<KeptDownload> KeptDownloads()
        {
            var byStamp = new Dictionary<string, KeptDownload>();
            var list = new List<KeptDownload>();
            if (!Directory.Exists(DownloadsDir)) return list;
            foreach (string f in Directory.GetFiles(DownloadsDir, "*.zip"))
            {
                string name = Path.GetFileNameWithoutExtension(f);
                foreach (var s in Sets)
                {
                    if (!name.StartsWith(s.Name + "_", StringComparison.Ordinal)) continue;
                    string rest = name.Substring(s.Name.Length + 1);
                    if (rest.Length < 23) break;
                    string stamp = rest.Substring(0, 23);           // yyyy-MM-dd_HH-mm-ss-fff
                    DateTime t;
                    if (!DateTime.TryParseExact(stamp, "yyyy-MM-dd_HH-mm-ss-fff", CultureInfo.InvariantCulture,
                                                DateTimeStyles.None, out t)) break;
                    KeptDownload k;
                    if (!byStamp.TryGetValue(stamp, out k))
                    {
                        k = new KeptDownload();
                        k.Stamp = stamp;
                        k.Time = t;
                        byStamp[stamp] = k;
                        list.Add(k);
                    }
                    k.Areas.Add(new KeyValuePair<SyncSet, string>(s, f));
                    break;
                }
            }
            list.Sort(delegate(KeptDownload a, KeptDownload b) { return b.Time.CompareTo(a.Time); });
            return list;
        }

        // Puts a kept download back in place. The current local state is backed up first, and the sync memory is
        // left alone, so the next sync treats the restored files as a local change (and asks if the cloud changed too).
        public bool RestoreDownload(string stamp)
        {
            Log("Restoring an earlier download ...", Level.Info);
            Validate();
            AssertNotRunning("restore");
            KeptDownload k = KeptDownloads().Find(delegate(KeptDownload x) { return x.Stamp == stamp; });
            if (k == null) throw new SyncError("That kept download is no longer there.");
            var names = new List<string>();
            foreach (var a in k.Areas) names.Add(a.Key.Label);
            if (!Confirm("Replace your local data with the download from " +
                         k.Time.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture) + "?\n\n" + string.Join(", ", names) +
                         "\n\nA backup of the current files is made first. Only do this if your save is broken and the cloud " +
                         "copy cannot fix it."))
            {
                Log("  Cancelled, nothing changed.", Level.Warn);
                return false;
            }
            string work = Path.Combine(S.StateDir, "restore.tmp");
            foreach (var a in k.Areas)
            {
                SyncSet set = a.Key;
                if (!Available(set, true)) continue;
                AssertNotRunning("restore");
                Util.DeleteTree(work);
                Directory.CreateDirectory(work);
                try
                {
                    Archive.ExtractZip(a.Value, work);
                    var files = ListFiles(work, set.Exclude);
                    BeginProgress("Restoring", files);
                    Backup(set.Dir, set.Exclude, Path.Combine(S.StateDir, "backups"), set.Name, KeepLocalBackups);
                    Mirror(work, set.Dir, set.Exclude, TreeHash(work, set.Exclude, false));
                    EndProgress();
                }
                finally { Util.DeleteTreeQuiet(work); }
                Log("  " + set.Label + ": restored from the download of " +
                    k.Time.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture) + ".", Level.Good);
            }
            Log("  The next upload puts this state in the cloud.", Level.Dim);
            return true;
        }

        // ---------- upload ----------
        // Returns false if anything was cancelled or local changes were not uploaded.
        public bool Push(bool force)
        {
            Log("Uploading to the cloud ...", Level.Info);
            Validate();
            AssertNotRunning("upload");
            AssertCloudParent();
            Directory.CreateDirectory(S.CloudDir);
            RecoverAll();
            var state = LoadState();
            var manifest = LoadManifest();
            var upload = new List<Eval>();
            var conflicts = new List<Eval>();
            var published = new Dictionary<string, string>();
            bool complete = true;
            var sets = AvailableSets();
            BeginProgress("Checking files", AllFiles(sets));
            foreach (var s in sets)
            {
                var e = Evaluate(s, state, manifest);
                if (IsEmpty(e.Local))
                    Log("  " + s.Label + ": nothing local, nothing uploaded.", Level.Warn);
                else if (e.Local == e.Cloud)
                {
                    SetLastHash(s.Name, e.Local);
                    if (e.Expected != e.Cloud) published[s.Name] = e.Cloud;
                    Log("  " + s.Label + ": cloud is up to date.", Level.Good);
                }
                else if (force || IsEmpty(e.Cloud) || e.Cloud == e.Last) upload.Add(e);
                else conflicts.Add(e);
            }
            EndProgress();
            if (force && upload.Count > 0)
            {
                var names = new List<string>();
                foreach (var t in upload) names.Add(t.Set.Label);
                if (!Confirm("Replace the cloud state with your local data?\n\n" + string.Join(", ", names) +
                             "\n\nA backup is made first."))
                {
                    Log("  Cancelled, nothing changed.", Level.Warn);
                    return false;
                }
            }
            if (conflicts.Count > 0)
            {
                Choice choice = AskConflict(SetsOf(conflicts), false);
                if (choice == Choice.Local) upload.AddRange(conflicts);
                else if (choice == Choice.Cloud)
                {
                    foreach (var c in conflicts)
                        Log("  " + c.Set.Label + ": cloud kept, your local changes were NOT uploaded. " +
                            "Use \"Download\" to get the cloud state.", Level.Warn);
                    complete = false;
                }
                else
                {
                    Log("  Cancelled, your local changes were NOT uploaded.", Level.Warn);
                    return false;
                }
            }
            bool uploaded = false;
            if (upload.Count > 0)
            {
                AssertNotRunning("upload");
                BeginProgress("Uploading", AllFiles(SetsOf(upload)));
                foreach (var e in upload)
                {
                    // another PC may have uploaded since the check: never overwrite that without asking
                    if (TreeHash(e.Set.Cloud, e.Set.Exclude, false) != e.Cloud)
                    {
                        Log("  " + e.Set.Label + ": the cloud changed during this sync, NOT uploaded. Sync again to decide.", Level.Warn);
                        complete = false;
                        continue;
                    }
                    Backup(e.Set.Cloud, e.Set.Exclude, History, e.Set.Name, KeepCloudBackups);
                    UpdateInPlace(e.Set.Dir, e.Set.Cloud, e.Set.Exclude, e.Local);
                    SetLastHash(e.Set.Name, e.Local);
                    published[e.Set.Name] = e.Local;
                    uploaded = true;
                    Log("  " + e.Set.Label + ": uploaded.", Level.Good);
                }
                EndProgress();
            }
            if (published.Count > 0)
            {
                WriteManifest(published, uploaded);
                if (uploaded)
                    Log("  Written to the sync folder. Your cloud app uploads it in the background, " +
                        "wait until it is done before you switch PCs.", Level.Dim);
            }
            return complete;
        }

        // ---------- cloud drives on this PC: Google Drive, OneDrive, Dropbox ----------
        public static List<KeyValuePair<string, string>> FindClouds()
        {
            var found = new List<KeyValuePair<string, string>>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            Action<string, string> add = delegate(string name, string path)
            {
                try
                {
                    if (!string.IsNullOrEmpty(path) && Directory.Exists(path))
                    {
                        string full = Path.GetFullPath(path).TrimEnd('\\', '/');
                        if (full.Length > 0 && seen.Add(full)) found.Add(new KeyValuePair<string, string>(name, full));
                    }
                }
                catch (Exception) { }
            };
            var google = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "my drive", "meine ablage", "mi unidad",
                "mon drive", "il mio drive", "mijn drive", "meu drive", "mój dysk", "мой диск" };
            foreach (char letter in "CDEFGHIJKLMNOPQRSTUVWXYZ")
            {
                string root = letter + ":\\";
                try
                {
                    if (!Directory.Exists(root)) continue;
                    foreach (string sub in Directory.GetDirectories(root))
                        if (google.Contains(Path.GetFileName(sub))) add("Google Drive", sub);
                }
                catch (Exception) { }
            }
            add("Google Drive", Path.Combine(Util.Home, "Google Drive"));
            foreach (string v in new[] { "OneDrive", "OneDriveConsumer", "OneDriveCommercial" })
                add("OneDrive", Environment.GetEnvironmentVariable(v));
            foreach (string b in new[] { Util.AppData, Util.LocalAppData })
            {
                try
                {
                    string info = Path.Combine(b, "Dropbox", "info.json");
                    if (!File.Exists(info)) continue;
                    var stack = new Stack<object>();
                    stack.Push(Json.Parse(File.ReadAllText(info)));
                    while (stack.Count > 0)
                    {
                        var d = Json.AsDict(stack.Pop());
                        if (d == null) continue;
                        foreach (var kv in d)
                        {
                            if (kv.Key == "path" && kv.Value is string) add("Dropbox", (string)kv.Value);
                            else stack.Push(kv.Value);
                        }
                    }
                }
                catch (Exception) { }
            }
            return found;
        }
    }
}
