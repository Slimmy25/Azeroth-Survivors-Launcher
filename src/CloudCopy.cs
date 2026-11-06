// A cloud service's folder kept as a local copy, for Cloud Save (STEAM DECK EXPERIMENTAL). The cloud sync engine
// (Sync.cs) works on the copy as on any sync folder; Down() makes the copy match the service before the engine reads
// it, Up() sends what the engine changed. Used for Dropbox (Dropbox.cs). Google Drive (GoogleDrive.cs) has the same
// rules in its own code.
//   Offline: an upload that fails is marked pending and sent first at the next sync. If the service changed in the
//   meantime, the player chooses whose progress to keep; the other one is kept in a folder beside the copy.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace AzerothLauncher
{
    // one cloud service, seen as a flat list of files ('/' separated paths relative to the launcher's folder there)
    public interface ICloudStore
    {
        string Name { get; }                                  // "Dropbox", in messages
        Dictionary<string, string> List();                    // path -> the service's content hash
        string LocalHash(string file);                        // the same hash, of a file on this device
        byte[] Download(string path);
        void Upload(string path, byte[] data);                // made or replaced, its folders made as needed
        void Delete(string path);
    }

    public static class CloudCopy
    {
        class State { public string Print = ""; public bool Pending; }

        static string StateFile(string copy) { return copy.TrimEnd('\\', '/') + ".state.txt"; }

        static State LoadState(string copy)
        {
            var st = new State();
            try
            {
                foreach (string line in File.ReadAllLines(StateFile(copy)))
                {
                    if (line.StartsWith("print=", StringComparison.Ordinal)) st.Print = line.Substring(6);
                    else if (line == "pending=1") st.Pending = true;
                }
            }
            catch (Exception) { }
            return st;
        }

        static void SaveState(string copy, State st)
        {
            File.WriteAllLines(StateFile(copy), new[] { "print=" + st.Print, "pending=" + (st.Pending ? "1" : "0") });
        }

        // a new sign-in or account: the copy starts empty and nothing is pending
        public static void Reset(string copy)
        {
            try { if (File.Exists(StateFile(copy))) File.Delete(StateFile(copy)); } catch (Exception) { }
            if (Directory.Exists(copy)) Util.DeleteTreeQuiet(copy);
            Directory.CreateDirectory(copy);
        }

        public static bool IsEmpty(ICloudStore store) { return store.List().Count == 0; }

        // the copy made to match the service. A pending upload goes first when the service has not changed since; when
        // it has, ask(question) decides: true keeps this device's progress (the service's is kept beside the copy).
        public static void Down(ICloudStore store, string copy, Func<string, bool> ask, Action<string> log)
        {
            Directory.CreateDirectory(copy);
            Dictionary<string, string> remote = store.List();
            string remotePrint = PrintOf(remote);
            State st = LoadState(copy);
            if (st.Pending)
            {
                if (remotePrint == st.Print)
                {
                    log(store.Name + ": sending the upload that could not be sent last time.");
                    UpTo(store, copy, remote, st, log);
                    return;
                }
                string aside = Path.Combine(Path.GetDirectoryName(copy.TrimEnd('\\', '/')), Path.GetFileName(copy.TrimEnd('\\', '/')) + "-kept",
                                            DateTime.Now.ToString("yyyy-MM-dd HH-mm-ss", CultureInfo.InvariantCulture));
                bool keepDevice = ask("Your last upload from this device never reached " + store.Name + ", and " + store.Name +
                                      " has changed since (another device synced).\n\nKeep this device's progress? YES uploads this device's " +
                                      "progress. NO takes " + store.Name + "'s. The other one is kept in a folder on this device either way.");
                if (keepDevice)
                {
                    foreach (var kv in remote)
                    {
                        string target = Path.Combine(aside, kv.Key.Replace('/', '\\'));
                        Directory.CreateDirectory(Path.GetDirectoryName(target));
                        File.WriteAllBytes(target, store.Download(kv.Key));
                    }
                    log(store.Name + "'s files were kept in " + aside + "; this device's progress is uploaded.");
                    UpTo(store, copy, remote, st, log);
                    return;
                }
                CopyFolder(copy, aside);
                log("This device's unsent progress was kept in " + aside + "; " + store.Name + "'s progress is used.");
            }
            Dictionary<string, string> local = LocalFiles(copy);
            int fetched = 0, removed = 0;
            foreach (var kv in remote)
            {
                string full;
                if (local.TryGetValue(kv.Key, out full) && kv.Value.Length > 0 && store.LocalHash(full) == kv.Value) continue;
                string target = Path.Combine(copy, kv.Key.Replace('/', '\\'));
                Directory.CreateDirectory(Path.GetDirectoryName(target));
                string temp = target + ".asl-download";
                File.WriteAllBytes(temp, store.Download(kv.Key));
                if (File.Exists(target)) File.Delete(target);
                File.Move(temp, target);
                fetched++;
            }
            foreach (var kv in local)
                if (!remote.ContainsKey(kv.Key)) { File.Delete(kv.Value); removed++; }
            RemoveEmptyFolders(copy);
            st.Print = remotePrint;
            st.Pending = false;
            SaveState(copy, st);
            if (fetched + removed > 0) log(store.Name + ": " + fetched + " file(s) downloaded, " + removed + " removed from the local copy.");
        }

        // what changed in the copy, sent to the service. Nothing changed: no connection is made. A failure leaves it pending.
        public static void Up(ICloudStore store, string copy, Action<string> log)
        {
            if (!Directory.Exists(copy)) return;
            State st = LoadState(copy);
            var hashes = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var kv in LocalFiles(copy)) hashes[kv.Key] = store.LocalHash(kv.Value);
            if (PrintOf(hashes) == st.Print && !st.Pending) return;
            try { UpTo(store, copy, store.List(), st, log); }
            catch (Exception)
            {
                st.Pending = true;
                try { SaveState(copy, st); } catch (Exception) { }
                throw;
            }
        }

        static void UpTo(ICloudStore store, string copy, Dictionary<string, string> remote, State st, Action<string> log)
        {
            Dictionary<string, string> local = LocalFiles(copy);
            var hashes = new Dictionary<string, string>(StringComparer.Ordinal);
            int sent = 0, removed = 0;
            foreach (var kv in local)
            {
                string hash = store.LocalHash(kv.Value);
                hashes[kv.Key] = hash;
                string there;
                if (remote.TryGetValue(kv.Key, out there) && there == hash) continue;
                store.Upload(kv.Key, File.ReadAllBytes(kv.Value));
                sent++;
            }
            foreach (var kv in remote)
                if (!local.ContainsKey(kv.Key)) { store.Delete(kv.Key); removed++; }
            st.Print = PrintOf(hashes);
            st.Pending = false;
            SaveState(copy, st);
            if (sent + removed > 0) log(store.Name + ": " + sent + " file(s) uploaded, " + removed + " removed.");
        }

        public static void CopyFolder(string from, string to)
        {
            if (!Directory.Exists(from)) return;
            string root = Path.GetFullPath(from).TrimEnd('\\', '/');
            foreach (string f in Directory.GetFiles(root, "*", SearchOption.AllDirectories))
            {
                string target = Path.Combine(to, f.Substring(root.Length + 1));
                Directory.CreateDirectory(Path.GetDirectoryName(target));
                File.Copy(f, target, true);
            }
        }

        static Dictionary<string, string> LocalFiles(string copy)
        {
            var files = new Dictionary<string, string>(StringComparer.Ordinal);
            if (!Directory.Exists(copy)) return files;
            string root = Path.GetFullPath(copy).TrimEnd('\\', '/');
            foreach (string f in Directory.GetFiles(root, "*", SearchOption.AllDirectories))
            {
                if (f.EndsWith(".asl-download", StringComparison.OrdinalIgnoreCase)) continue;
                files[f.Substring(root.Length + 1).Replace('\\', '/')] = f;
            }
            return files;
        }

        static void RemoveEmptyFolders(string copy)
        {
            var dirs = new List<string>(Directory.GetDirectories(copy, "*", SearchOption.AllDirectories));
            dirs.Sort(delegate(string a, string b) { return b.Length.CompareTo(a.Length); });   // deepest first
            foreach (string d in dirs)
                try { if (Directory.GetFileSystemEntries(d).Length == 0) Directory.Delete(d); } catch (Exception) { }
        }

        // one fingerprint for a whole set of files: their paths and contents
        static string PrintOf(Dictionary<string, string> hashes)
        {
            var keys = new List<string>(hashes.Keys);
            keys.Sort(StringComparer.Ordinal);
            var sb = new StringBuilder();
            foreach (string k in keys) sb.Append(k).Append('\n').Append(hashes[k]).Append('\n');
            using (var sha = SHA256.Create()) return Hex(sha.ComputeHash(Encoding.UTF8.GetBytes(sb.ToString())));
        }

        public static string Hex(byte[] bytes)
        {
            var sb = new StringBuilder(bytes.Length * 2);
            foreach (byte b in bytes) sb.Append(b.ToString("x2", CultureInfo.InvariantCulture));
            return sb.ToString();
        }
    }
}
