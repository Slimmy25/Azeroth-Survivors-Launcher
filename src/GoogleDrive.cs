// Google Drive for Cloud Save (STEAM DECK EXPERIMENTAL): the launcher syncs with Google Drive itself, so a Steam Deck in
// Game Mode needs no sync app. The cloud sync engine (Sync.cs) is unchanged: it works on a local copy of one Drive
// folder, "Azeroth Survivors Launcher Cloud Save". Down() makes the copy match Drive before the engine reads it; Up()
// sends what the engine changed in it.
//   Sign-in: Google's sign-in for TVs and limited-input devices. The launcher shows a code and google.com/device, and
//   the player signs in on a phone or PC. Scope: drive.file (only the files the launcher itself made) and email.
//   The lasting sign-in (refresh token) is kept with Windows' data protection for this user.
//   Offline: an upload that fails is marked pending and sent first at the next sync. If Drive changed in the meantime,
//   the player chooses whose progress to keep; the other one is kept in a folder beside the copy.
// The app's ID and secret are not in the code: the build embeds them from GitHub's Actions secrets
// (GOOGLE_CLIENT_ID, GOOGLE_CLIENT_SECRET; tools\Compile-Launcher.ps1). Without them the sign-in is not offered.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Threading;

namespace AzerothLauncher
{
    public static class GoogleDrive
    {
        public const string FolderName = "Azeroth Survivors Launcher Cloud Save";
        const string Scope = "email https://www.googleapis.com/auth/drive.file";
        const string FolderMime = "application/vnd.google-apps.folder";
        const string TokenUrl = "https://oauth2.googleapis.com/token";
        const string FilesUrl = "https://www.googleapis.com/drive/v3/files";
        const string UploadUrl = "https://www.googleapis.com/upload/drive/v3/files";
        static readonly byte[] Entropy = Encoding.UTF8.GetBytes("Azeroth Survivors Launcher Google Drive");

        public static string RefreshToken;                    // set by the launcher (from its settings) before Drive is used

        static string clientId, clientSecret;
        static bool clientLoaded;
        static string accessToken, accessFor;
        static DateTime accessUntil;
        static readonly object gate = new object();

        static GoogleDrive()
        {
            try
            {   // as Net does: TLS 1.2 on where the old .NET defaults would not reach modern servers
                if ((int)ServicePointManager.SecurityProtocol != 0)
                    ServicePointManager.SecurityProtocol |= (SecurityProtocolType)3072;
            }
            catch (Exception) { }
        }

        static void LoadClient()
        {
            if (clientLoaded) return;
            clientLoaded = true;
            try
            {
                string[] lines = Util.ReadResource("AS.google-oauth.txt").Replace("\r", "").Split('\n');
                if (lines.Length >= 2) { clientId = lines[0].Trim(); clientSecret = lines[1].Trim(); }
            }
            catch (Exception) { }
        }

        // this build carries the launcher's Google app ID (made by the release build)
        public static bool Available
        {
            get { LoadClient(); return !string.IsNullOrEmpty(clientId) && !string.IsNullOrEmpty(clientSecret); }
        }

        // ------------------------------------------------------------------ signing in
        public class SignIn
        {
            public string DeviceCode, UserCode, Url;
            public int Interval, ExpiresIn;
        }

        public static SignIn StartSignIn()
        {
            LoadClient();
            var form = new Dictionary<string, string>();
            form["client_id"] = clientId;
            form["scope"] = Scope;
            Reply r = PostForm("https://oauth2.googleapis.com/device/code", form);
            if (r.Status != 200) throw new UserError("Google did not start the sign-in: " + ErrorText(r.Text));
            Dictionary<string, object> d = Json.AsDict(Json.Parse(r.Text)) ?? new Dictionary<string, object>();
            var s = new SignIn();
            s.DeviceCode = Json.Str(d, "device_code");
            s.UserCode = Json.Str(d, "user_code");
            s.Url = Json.Str(d, "verification_url");
            if (s.Url.Length == 0) s.Url = Json.Str(d, "verification_uri");
            if (s.Url.Length == 0) s.Url = "https://www.google.com/device";
            int.TryParse(Json.Str(d, "interval"), NumberStyles.Integer, CultureInfo.InvariantCulture, out s.Interval);
            int.TryParse(Json.Str(d, "expires_in"), NumberStyles.Integer, CultureInfo.InvariantCulture, out s.ExpiresIn);
            if (s.Interval < 1) s.Interval = 5;
            if (s.ExpiresIn < 60) s.ExpiresIn = 1800;
            if (s.DeviceCode.Length == 0 || s.UserCode.Length == 0) throw new UserError("Google's sign-in answer was incomplete. Try again.");
            return s;
        }

        // asks Google every few seconds whether the player has signed in; the lasting sign-in, or null when cancelled
        public static string WaitForSignIn(SignIn s, Func<bool> cancelled)
        {
            LoadClient();
            DateTime until = DateTime.UtcNow.AddSeconds(s.ExpiresIn);
            int wait = s.Interval;
            while (DateTime.UtcNow < until)
            {
                for (int i = 0; i < wait * 10; i++)
                {
                    if (cancelled()) return null;
                    Thread.Sleep(100);
                }
                var form = new Dictionary<string, string>();
                form["client_id"] = clientId;
                form["client_secret"] = clientSecret;
                form["device_code"] = s.DeviceCode;
                form["grant_type"] = "urn:ietf:params:oauth:grant-type:device_code";
                Reply r = PostForm(TokenUrl, form);
                Dictionary<string, object> d = Json.AsDict(Json.Parse(r.Text)) ?? new Dictionary<string, object>();
                if (r.Status == 200)
                {
                    string refresh = Json.Str(d, "refresh_token");
                    if (refresh.Length == 0) throw new UserError("Google signed you in but did not give the launcher lasting access. Try again.");
                    Remember(Json.Str(d, "access_token"), Json.Str(d, "expires_in"), refresh);
                    return refresh;
                }
                string error = Json.Str(d, "error");
                if (error == "authorization_pending") continue;
                if (error == "slow_down") { wait += 5; continue; }
                if (error == "access_denied") throw new UserError("The sign-in was declined on Google's page.");
                if (error == "expired_token") break;
                throw new UserError("Google sign-in failed: " + ErrorText(r.Text));
            }
            throw new UserError("The sign-in code expired before it was used. Press SIGN IN WITH GOOGLE again.");
        }

        static void Remember(string token, string expiresIn, string refresh)
        {
            int seconds;
            if (!int.TryParse(expiresIn, NumberStyles.Integer, CultureInfo.InvariantCulture, out seconds)) seconds = 3600;
            accessToken = token;
            accessFor = refresh;
            accessUntil = DateTime.UtcNow.AddSeconds(Math.Max(60, seconds - 120));
        }

        static string Access()
        {
            lock (gate)
            {
                if (!string.IsNullOrEmpty(accessToken) && accessFor == RefreshToken && DateTime.UtcNow < accessUntil) return accessToken;
                if (string.IsNullOrEmpty(RefreshToken))
                    throw new UserError("Not signed in to Google Drive. Sign in under Options > Cloud Save.");
                LoadClient();
                var form = new Dictionary<string, string>();
                form["client_id"] = clientId;
                form["client_secret"] = clientSecret;
                form["refresh_token"] = RefreshToken;
                form["grant_type"] = "refresh_token";
                Reply r = PostForm(TokenUrl, form);
                Dictionary<string, object> d = Json.AsDict(Json.Parse(r.Text)) ?? new Dictionary<string, object>();
                if (r.Status != 200)
                {
                    if (Json.Str(d, "error") == "invalid_grant")
                        throw new UserError("Google Drive's sign-in has ended (it was removed or expired). Sign in again under Options > Cloud Save.");
                    throw new UserError("Google Drive refused the sign-in: " + ErrorText(r.Text));
                }
                Remember(Json.Str(d, "access_token"), Json.Str(d, "expires_in"), RefreshToken);
                return accessToken;
            }
        }

        public static string Email()
        {
            try
            {
                Reply r = Send("GET", "https://www.googleapis.com/oauth2/v3/userinfo", null, null, true);
                return r.Status == 200 ? Json.Str(Json.AsDict(Json.Parse(r.Text)), "email") : "";
            }
            catch (Exception) { return ""; }
        }

        // signing out: Google forgets the launcher's access too (best effort)
        public static void Revoke(string refresh)
        {
            if (string.IsNullOrEmpty(refresh)) return;
            try
            {
                var form = new Dictionary<string, string>();
                form["token"] = refresh;
                PostForm("https://oauth2.googleapis.com/revoke", form);
            }
            catch (Exception) { }
            lock (gate) { accessToken = null; accessFor = null; }
        }

        // the lasting sign-in as stored in the settings: protected for this Windows user (Wine and Proton have the same)
        public static string Protect(string refresh)
        {
            try
            {
                return "dp:" + Convert.ToBase64String(ProtectedData.Protect(Encoding.UTF8.GetBytes(refresh), Entropy, DataProtectionScope.CurrentUser));
            }
            catch (Exception)
            {
                return "b64:" + Convert.ToBase64String(Encoding.UTF8.GetBytes(refresh));
            }
        }

        public static string Unprotect(string stored)
        {
            try
            {
                if (string.IsNullOrEmpty(stored)) return null;
                if (stored.StartsWith("dp:", StringComparison.Ordinal))
                    return Encoding.UTF8.GetString(ProtectedData.Unprotect(Convert.FromBase64String(stored.Substring(3)), Entropy, DataProtectionScope.CurrentUser));
                if (stored.StartsWith("b64:", StringComparison.Ordinal))
                    return Encoding.UTF8.GetString(Convert.FromBase64String(stored.Substring(4)));
            }
            catch (Exception) { }
            return null;
        }

        // ------------------------------------------------------------------ the local copy
        class Entry { public string Id, Md5; public bool Folder; }

        class Tree
        {
            public readonly Dictionary<string, Entry> Files = new Dictionary<string, Entry>(StringComparer.Ordinal);
            public readonly Dictionary<string, string> Dirs = new Dictionary<string, string>(StringComparer.Ordinal);
        }

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
        public static void ResetCopy(string copy)
        {
            try { if (File.Exists(StateFile(copy))) File.Delete(StateFile(copy)); } catch (Exception) { }
            if (Directory.Exists(copy)) Util.DeleteTreeQuiet(copy);
            Directory.CreateDirectory(copy);
        }

        // Drive has nothing in the launcher's folder yet
        public static bool IsEmpty()
        {
            string root = RootFolder();
            return Children(root).Count == 0;
        }

        // the copy made to match Drive. A pending upload goes first when Drive has not changed since; when it has,
        // ask(question) decides: true keeps this device's progress (Drive's is kept beside the copy), false takes Drive's.
        public static void Down(string copy, Func<string, bool> ask, Action<string> log)
        {
            Directory.CreateDirectory(copy);
            string root = RootFolder();
            Tree tree = ListTree(root);
            string drivePrint = PrintOf(tree);
            State st = LoadState(copy);
            if (st.Pending)
            {
                if (drivePrint == st.Print)
                {
                    log("Google Drive: sending the upload that could not be sent last time.");
                    UpTo(copy, root, tree, st, log);
                    return;
                }
                string aside = Path.Combine(Path.GetDirectoryName(copy.TrimEnd('\\', '/')), "google-drive-kept",
                                            DateTime.Now.ToString("yyyy-MM-dd HH-mm-ss", CultureInfo.InvariantCulture));
                bool keepDevice = ask("Your last upload from this device never reached Google Drive, and Google Drive has changed since " +
                                      "(another device synced).\n\nKeep this device's progress? YES uploads this device's progress. " +
                                      "NO takes Google Drive's. The other one is kept in a folder on this device either way.");
                if (keepDevice)
                {
                    SaveTreeTo(tree, aside);
                    log("Google Drive's files were kept in " + aside + "; this device's progress is uploaded.");
                    UpTo(copy, root, tree, st, log);
                    return;
                }
                CopyFolder(copy, aside);
                log("This device's unsent progress was kept in " + aside + "; Google Drive's progress is used.");
            }
            Dictionary<string, string> local = LocalFiles(copy);
            int fetched = 0;
            foreach (var kv in tree.Files)
            {
                string full;
                if (local.TryGetValue(kv.Key, out full) && kv.Value.Md5.Length > 0 && Md5(full) == kv.Value.Md5) continue;
                string target = Path.Combine(copy, kv.Key.Replace('/', '\\'));
                Directory.CreateDirectory(Path.GetDirectoryName(target));
                string temp = target + ".asl-download";
                File.WriteAllBytes(temp, Download(kv.Value.Id));
                if (File.Exists(target)) File.Delete(target);
                File.Move(temp, target);
                fetched++;
            }
            int removed = 0;
            foreach (var kv in local)
                if (!tree.Files.ContainsKey(kv.Key)) { File.Delete(kv.Value); removed++; }
            RemoveEmptyFolders(copy);
            st.Print = drivePrint;
            st.Pending = false;
            SaveState(copy, st);
            if (fetched + removed > 0) log("Google Drive: " + fetched + " file(s) downloaded, " + removed + " removed from the local copy.");
        }

        // what changed in the copy, sent to Drive. Nothing changed: no connection is made. A failure leaves it pending.
        public static void Up(string copy, Action<string> log)
        {
            if (!Directory.Exists(copy)) return;
            State st = LoadState(copy);
            if (PrintOf(LocalHashes(copy)) == st.Print && !st.Pending) return;
            try
            {
                string root = RootFolder();
                UpTo(copy, root, ListTree(root), st, log);
            }
            catch (Exception)
            {
                st.Pending = true;
                try { SaveState(copy, st); } catch (Exception) { }
                throw;
            }
        }

        static void UpTo(string copy, string root, Tree tree, State st, Action<string> log)
        {
            Dictionary<string, string> local = LocalFiles(copy);
            var hashes = new Dictionary<string, string>(StringComparer.Ordinal);
            int sent = 0, removed = 0;
            foreach (var kv in local)
            {
                string md5 = Md5(kv.Value);
                hashes[kv.Key] = md5;
                Entry e;
                if (tree.Files.TryGetValue(kv.Key, out e))
                {
                    if (e.Md5 == md5) continue;
                    Replace(e.Id, File.ReadAllBytes(kv.Value));
                }
                else
                {
                    int slash = kv.Key.LastIndexOf('/');
                    string dir = slash < 0 ? "" : kv.Key.Substring(0, slash);
                    Create(kv.Key.Substring(slash + 1), FolderFor(tree, dir), File.ReadAllBytes(kv.Value));
                }
                sent++;
            }
            foreach (var kv in tree.Files)
                if (!local.ContainsKey(kv.Key)) { Remove(kv.Value.Id); removed++; }
            st.Print = PrintOf(hashes);
            st.Pending = false;
            SaveState(copy, st);
            if (sent + removed > 0) log("Google Drive: " + sent + " file(s) uploaded, " + removed + " removed.");
        }

        // a whole folder copied (an existing sync folder moving into Google Drive, or a copy kept aside)
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

        static void SaveTreeTo(Tree tree, string folder)
        {
            foreach (var kv in tree.Files)
            {
                string target = Path.Combine(folder, kv.Key.Replace('/', '\\'));
                Directory.CreateDirectory(Path.GetDirectoryName(target));
                File.WriteAllBytes(target, Download(kv.Value.Id));
            }
        }

        // relative path ('/' separated) -> full path, skipping the launcher's own leftovers
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

        static Dictionary<string, string> LocalHashes(string copy)
        {
            var hashes = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var kv in LocalFiles(copy)) hashes[kv.Key] = Md5(kv.Value);
            return hashes;
        }

        static void RemoveEmptyFolders(string copy)
        {
            var dirs = new List<string>(Directory.GetDirectories(copy, "*", SearchOption.AllDirectories));
            dirs.Sort(delegate(string a, string b) { return b.Length.CompareTo(a.Length); });   // deepest first
            foreach (string d in dirs)
                try { if (Directory.GetFileSystemEntries(d).Length == 0) Directory.Delete(d); } catch (Exception) { }
        }

        static string Md5(string path)
        {
            using (var md5 = MD5.Create())
            using (var s = File.OpenRead(path)) return Hex(md5.ComputeHash(s));
        }

        static string Hex(byte[] bytes)
        {
            var sb = new StringBuilder(bytes.Length * 2);
            foreach (byte b in bytes) sb.Append(b.ToString("x2", CultureInfo.InvariantCulture));
            return sb.ToString();
        }

        static string PrintOf(Tree tree)
        {
            var hashes = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var kv in tree.Files) hashes[kv.Key] = kv.Value.Md5;
            return PrintOf(hashes);
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

        // ------------------------------------------------------------------ Drive itself
        static string RootFolder()
        {
            string q = "name='" + FolderName + "' and mimeType='" + FolderMime + "' and 'root' in parents and trashed=false";
            Reply r = Send("GET", FilesUrl + "?pageSize=10&fields=" + Uri.EscapeDataString("files(id)") + "&q=" + Uri.EscapeDataString(q), null, null, true);
            Check(r, "find its Cloud Save folder");
            foreach (object o in Json.AsList(Json.Get(Json.AsDict(Json.Parse(r.Text)), "files")) ?? new List<object>())
            {
                string id = Json.Str(Json.AsDict(o), "id");
                if (id.Length > 0) return id;
            }
            return CreateFolder(FolderName, "root");
        }

        static Dictionary<string, Entry> Children(string folderId)
        {
            var found = new Dictionary<string, Entry>(StringComparer.Ordinal);
            string page = null;
            do
            {
                string q = "'" + folderId + "' in parents and trashed=false";
                string url = FilesUrl + "?pageSize=1000&fields=" + Uri.EscapeDataString("nextPageToken,files(id,name,mimeType,md5Checksum)") +
                             "&q=" + Uri.EscapeDataString(q) + (page != null ? "&pageToken=" + Uri.EscapeDataString(page) : "");
                Reply r = Send("GET", url, null, null, true);
                Check(r, "list its Cloud Save folder");
                Dictionary<string, object> d = Json.AsDict(Json.Parse(r.Text));
                foreach (object o in Json.AsList(Json.Get(d, "files")) ?? new List<object>())
                {
                    Dictionary<string, object> f = Json.AsDict(o);
                    string name = Json.Str(f, "name");
                    // only plain names: a file's place on this device comes from them
                    if (name.Length == 0 || name == "." || name == ".." || name.IndexOfAny(new[] { '/', '\\', ':' }) >= 0 || found.ContainsKey(name)) continue;
                    var e = new Entry();
                    e.Id = Json.Str(f, "id");
                    e.Md5 = Json.Str(f, "md5Checksum").ToLowerInvariant();
                    e.Folder = Json.Str(f, "mimeType") == FolderMime;
                    found[name] = e;
                }
                page = Json.Str(d, "nextPageToken");
                if (page.Length == 0) page = null;
            } while (page != null);
            return found;
        }

        static Tree ListTree(string rootId)
        {
            var tree = new Tree();
            tree.Dirs[""] = rootId;
            var queue = new Queue<string>();
            queue.Enqueue("");
            while (queue.Count > 0)
            {
                string rel = queue.Dequeue();
                foreach (var kv in Children(tree.Dirs[rel]))
                {
                    string path = rel.Length == 0 ? kv.Key : rel + "/" + kv.Key;
                    if (kv.Value.Folder) { tree.Dirs[path] = kv.Value.Id; queue.Enqueue(path); }
                    else tree.Files[path] = kv.Value;
                }
            }
            return tree;
        }

        // the Drive folder for a relative folder path, made where missing
        static string FolderFor(Tree tree, string dir)
        {
            string id;
            if (tree.Dirs.TryGetValue(dir, out id)) return id;
            int slash = dir.LastIndexOf('/');
            string parent = FolderFor(tree, slash < 0 ? "" : dir.Substring(0, slash));
            id = CreateFolder(dir.Substring(slash + 1), parent);
            tree.Dirs[dir] = id;
            return id;
        }

        static string CreateFolder(string name, string parent)
        {
            string meta = "{\"name\":" + JsonText(name) + ",\"mimeType\":\"" + FolderMime + "\",\"parents\":[" + JsonText(parent) + "]}";
            Reply r = Send("POST", FilesUrl + "?fields=id", "application/json; charset=UTF-8", Encoding.UTF8.GetBytes(meta), true);
            Check(r, "make a folder");
            string id = Json.Str(Json.AsDict(Json.Parse(r.Text)), "id");
            if (id.Length == 0) throw new UserError("Google Drive made a folder but did not say which.");
            return id;
        }

        static void Create(string name, string parent, byte[] data)
        {
            string boundary = "asl" + Guid.NewGuid().ToString("N");
            string meta = "{\"name\":" + JsonText(name) + ",\"parents\":[" + JsonText(parent) + "]}";
            using (var body = new MemoryStream())
            {
                Write(body, "--" + boundary + "\r\nContent-Type: application/json; charset=UTF-8\r\n\r\n" + meta + "\r\n--" + boundary +
                            "\r\nContent-Type: application/octet-stream\r\n\r\n");
                body.Write(data, 0, data.Length);
                Write(body, "\r\n--" + boundary + "--\r\n");
                Reply r = Send("POST", UploadUrl + "?uploadType=multipart&fields=id", "multipart/related; boundary=" + boundary, body.ToArray(), true);
                Check(r, "upload " + name);
            }
        }

        static void Replace(string id, byte[] data)
        {
            Reply r = Send("PATCH", UploadUrl + "/" + Uri.EscapeDataString(id) + "?uploadType=media&fields=id", "application/octet-stream", data, true);
            Check(r, "update a file");
        }

        static void Remove(string id)
        {
            Reply r = Send("DELETE", FilesUrl + "/" + Uri.EscapeDataString(id), null, null, true);
            if (r.Status != 404) Check(r, "remove an old file");
        }

        static byte[] Download(string id)
        {
            Reply r = Send("GET", FilesUrl + "/" + Uri.EscapeDataString(id) + "?alt=media", null, null, true);
            Check(r, "download a file");
            return r.Data ?? new byte[0];
        }

        static void Write(Stream s, string text)
        {
            byte[] b = Encoding.UTF8.GetBytes(text);
            s.Write(b, 0, b.Length);
        }

        static string JsonText(string s)
        {
            var sb = new StringBuilder("\"");
            foreach (char c in s ?? "")
            {
                if (c == '"' || c == '\\') sb.Append('\\').Append(c);
                else if (c < ' ') sb.Append("\\u").Append(((int)c).ToString("x4", CultureInfo.InvariantCulture));
                else sb.Append(c);
            }
            return sb.Append('"').ToString();
        }

        // ------------------------------------------------------------------ requests
        class Reply
        {
            public int Status;
            public byte[] Data;
            public string Text { get { return Data == null ? "" : Encoding.UTF8.GetString(Data); } }
        }

        static void Check(Reply r, string what)
        {
            if (r.Status >= 200 && r.Status < 300) return;
            throw new UserError("Google Drive could not " + what + " (" + r.Status + "): " + ErrorText(r.Text));
        }

        static string ErrorText(string body)
        {
            try
            {
                Dictionary<string, object> d = Json.AsDict(Json.Parse(body));
                Dictionary<string, object> e = Json.AsDict(Json.Get(d, "error"));
                if (e != null && Json.Str(e, "message").Length > 0) return Json.Str(e, "message");
                string s = Json.Str(d, "error_description");
                if (s.Length > 0) return s;
                s = Json.Str(d, "error");
                if (s.Length > 0) return s;
            }
            catch (Exception) { }
            body = body ?? "";
            return body.Length > 200 ? body.Substring(0, 200) : body;
        }

        static Reply PostForm(string url, Dictionary<string, string> form)
        {
            var parts = new List<string>();
            foreach (var kv in form) parts.Add(Uri.EscapeDataString(kv.Key) + "=" + Uri.EscapeDataString(kv.Value ?? ""));
            return Send("POST", url, "application/x-www-form-urlencoded", Encoding.UTF8.GetBytes(string.Join("&", parts.ToArray())), false);
        }

        static Reply Send(string method, string url, string contentType, byte[] body, bool signedIn)
        {
            var req = (HttpWebRequest)WebRequest.Create(url);
            req.Method = method;
            req.UserAgent = Net.UserAgent;
            req.Timeout = 30000;
            req.ReadWriteTimeout = 120000;
            req.AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate;
            if (signedIn) req.Headers[HttpRequestHeader.Authorization] = "Bearer " + Access();
            if (body != null)
            {
                req.ContentType = contentType;
                req.ContentLength = body.Length;
                using (Stream s = req.GetRequestStream()) s.Write(body, 0, body.Length);
            }
            else if (method == "POST" || method == "PATCH") req.ContentLength = 0;
            HttpWebResponse resp;
            try { resp = (HttpWebResponse)req.GetResponse(); }
            catch (WebException e)
            {
                resp = e.Response as HttpWebResponse;
                if (resp == null) throw new UserError("Google could not be reached (" + e.Message + ").");
            }
            using (resp)
            {
                var reply = new Reply();
                reply.Status = (int)resp.StatusCode;
                using (Stream s = resp.GetResponseStream())
                using (var ms = new MemoryStream())
                {
                    if (s != null) s.CopyTo(ms);
                    reply.Data = ms.ToArray();
                }
                return reply;
            }
        }
    }
}
