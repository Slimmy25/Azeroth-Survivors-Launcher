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
            // only ever stored protected by Windows (for this Windows user); when that is not possible the sign-in fails
            // instead of keeping the token readable ("b64:" values from older versions are still read)
            try
            {
                return "dp:" + Convert.ToBase64String(ProtectedData.Protect(Encoding.UTF8.GetBytes(refresh), Entropy, DataProtectionScope.CurrentUser));
            }
            catch (Exception e)
            {
                throw new UserError("Windows could not protect the sign-in on this device, so it was not kept (" + e.Message + ").");
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
            public readonly Dictionary<string, Entry> Files = new Dictionary<string, Entry>(StringComparer.OrdinalIgnoreCase);   // matched as CloudCopy matches paths
            public readonly Dictionary<string, string> Dirs = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        }

        // Drive's Cloud Save folder as a flat file list, for CloudCopy (CloudCopy.cs keeps the local copy and its state file;
        // the state format and fingerprints are the same as before, so nothing resyncs). One store is one sync pass: the
        // tree listed by List() gives the ids Download, Upload and Delete use.
        class Store : ICloudStore
        {
            string root;
            Tree tree;

            public string Name { get { return "Google Drive"; } }

            public Dictionary<string, string> List()
            {
                root = RootFolder();
                tree = ListTree(root);
                var list = new Dictionary<string, string>(StringComparer.Ordinal);
                foreach (var kv in tree.Files) list[kv.Key] = kv.Value.Md5;
                return list;
            }

            public string LocalHash(string file) { return Md5(file); }

            Tree Listed() { if (tree == null) List(); return tree; }

            public byte[] Download(string path)
            {
                Entry e;
                if (!Listed().Files.TryGetValue(path, out e)) throw new UserError("Google Drive did not offer the file right now (it may have hit its download limit).");
                return GoogleDrive.Download(e.Id);
            }

            public void Upload(string path, byte[] data)
            {
                Tree t = Listed();
                Entry e;
                if (t.Files.TryGetValue(path, out e)) { Replace(e.Id, data); return; }
                int slash = path.LastIndexOf('/');
                string dir = slash < 0 ? "" : path.Substring(0, slash);
                Create(path.Substring(slash + 1), FolderFor(t, dir), data);
            }

            public void Delete(string path)
            {
                Entry e;
                if (Listed().Files.TryGetValue(path, out e)) Remove(e.Id);
            }
        }

        const string KeptFolder = "google-drive-kept";        // where a kept-aside version goes, as before the move to CloudCopy

        // a new sign-in or account: the copy starts empty and nothing is pending
        public static void ResetCopy(string copy) { CloudCopy.Reset(copy); }

        // Drive has nothing in the launcher's folder yet
        public static bool IsEmpty()
        {
            string root = RootFolder();
            return Children(root).Count == 0;
        }

        // the copy made to match Drive; see CloudCopy.Down
        public static void Down(string copy, Func<string, bool> ask, Action<string> log)
        {
            CloudCopy.Down(new Store(), copy, ask, log, KeptFolder);
        }

        // what changed in the copy, sent to Drive; see CloudCopy.Up
        public static void Up(string copy, Action<string> log)
        {
            CloudCopy.Up(new Store(), copy, log);
        }

        // a whole folder copied (an existing sync folder moving into Google Drive)
        public static void CopyFolder(string from, string to) { CloudCopy.CopyFolder(from, to); }

        static string Md5(string path)
        {
            using (var md5 = MD5.Create())
            using (var s = File.OpenRead(path)) return CloudCopy.Hex(md5.ComputeHash(s));
        }

        // ------------------------------------------------------------------ Drive itself
        static string RootFolder()
        {
            string q = "name='" + FolderName + "' and mimeType='" + FolderMime + "' and 'root' in parents and trashed=false";
            Reply r = Send("GET", FilesUrl + "?pageSize=10&orderBy=createdTime&fields=" + Uri.EscapeDataString("files(id)") + "&q=" + Uri.EscapeDataString(q), null, null, true);
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
