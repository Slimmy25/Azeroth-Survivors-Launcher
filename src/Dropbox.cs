// Dropbox for Cloud Save (STEAM DECK EXPERIMENTAL), signed in from a phone so a Steam Deck never leaves Game Mode:
//   1. The launcher makes a one-time key pair (PKCE: the verifier stays on this device, only its challenge leaves it)
//      and a short code, and posts the challenge to a relay topic named after the code (ntfy.sh, a free public relay).
//   2. On a phone, the player opens the launcher's sign-in page (docs\dropbox.html on GitHub Pages) and enters the code.
//      The page reads the challenge and sends the player to Dropbox to approve the launcher.
//   3. Dropbox returns to that page with a one-time approval code, which the page posts to the code's reply topic.
//   4. The launcher picks it up and trades it, with its verifier, for a lasting sign-in. The approval code is useless
//      to anyone without that verifier. The launcher then shows which Dropbox account it reached, to be confirmed.
// Access: an App folder (Dropbox/Apps/<the app's name>), nothing else. No app secret: Dropbox's PKCE for public clients.
// The lasting sign-in (refresh token) is kept with Windows' data protection, like Google Drive's (GoogleDrive.Protect).
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
    public class Dropbox : ICloudStore
    {
        public const string AppKey = "qunu64q01u2eg1u";                   // public, as in every copy of the launcher
        public const string SignInPage = "https://slimmy25.github.io/Azeroth-Survivors-Launcher/dropbox.html";
        public const string SignInPageShown = "slimmy25.github.io/Azeroth-Survivors-Launcher/dropbox";
        const string Relay = "https://ntfy.sh/";
        const string CodeLetters = "ABCDEFGHJKMNPQRSTUVWXYZ23456789";       // no 0/O, 1/I/L: easy to type on a phone

        public static string RefreshToken;                                // set by the launcher before Dropbox is used

        static string accessToken, accessFor;
        static DateTime accessUntil;
        static readonly object gate = new object();

        static Dropbox()
        {
            try
            {   // as Net does: TLS 1.2 on where the old .NET defaults would not reach modern servers
                if ((int)ServicePointManager.SecurityProtocol != 0)
                    ServicePointManager.SecurityProtocol |= (SecurityProtocolType)3072;
            }
            catch (Exception) { }
        }

        public string Name { get { return "Dropbox"; } }

        // ------------------------------------------------------------------ signing in from a phone
        public class SignIn
        {
            public string Code;                                           // as shown: ABCD-EFGH
            public string Topic;                                          // asl-dbx-abcdefgh
            public string Verifier;
            public long Since;                                            // unix seconds: replies older than this are ignored
        }

        static string Base64Url(byte[] b)
        {
            return Convert.ToBase64String(b).TrimEnd('=').Replace('+', '-').Replace('/', '_');
        }

        public static SignIn StartSignIn()
        {
            var s = new SignIn();
            using (var rng = RandomNumberGenerator.Create())
            {
                byte[] secret = new byte[32];
                rng.GetBytes(secret);
                s.Verifier = Base64Url(secret);
                byte[] pick = new byte[8];
                rng.GetBytes(pick);
                var code = new StringBuilder();
                foreach (byte b in pick) code.Append(CodeLetters[b % CodeLetters.Length]);
                s.Code = code.ToString(0, 4) + "-" + code.ToString(4, 4);
                s.Topic = "asl-dbx-" + code.ToString().ToLowerInvariant();
            }
            string challenge;
            using (var sha = SHA256.Create()) challenge = Base64Url(sha.ComputeHash(Encoding.ASCII.GetBytes(s.Verifier)));
            s.Since = (long)(DateTime.UtcNow - new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc)).TotalSeconds - 5;
            Reply r = Send("POST", Relay + s.Topic + "-req", "text/plain", Encoding.UTF8.GetBytes("{\"challenge\":\"" + challenge + "\"}"), null);
            if (r.Status < 200 || r.Status >= 300) throw new UserError("The sign-in relay (ntfy.sh) could not be reached (" + r.Status + "). Try again in a moment.");
            return s;
        }

        // waits up to 15 minutes for the phone's approval; the lasting sign-in, or null when cancelled
        public static string WaitForSignIn(SignIn s, Func<bool> cancelled)
        {
            DateTime until = DateTime.UtcNow.AddMinutes(15);
            while (DateTime.UtcNow < until)
            {
                for (int i = 0; i < 50; i++)                // every 5 s: within the relay's request rate
                {
                    if (cancelled()) return null;
                    Thread.Sleep(100);
                }
                Reply r;
                try { r = Send("GET", Relay + s.Topic + "-ret/json?poll=1&since=" + s.Since.ToString(CultureInfo.InvariantCulture), null, null, null); }
                catch (UserError) { continue; }                           // the relay or the connection hiccupped: ask again
                if (r.Status != 200) continue;
                string approval = null;
                foreach (string line in r.Text.Split('\n'))
                {
                    if (line.Trim().Length == 0) continue;
                    Dictionary<string, object> m;
                    try { m = Json.AsDict(Json.Parse(line)); }
                    catch (Exception) { continue; }
                    if (Json.Str(m, "event") == "message" && Json.Str(m, "message").Trim().Length > 0) approval = Json.Str(m, "message").Trim();
                }
                if (approval == null) continue;
                if (approval == "denied") throw new UserError("The sign-in was declined on Dropbox's page.");
                var form = new Dictionary<string, string>();
                form["code"] = approval;
                form["grant_type"] = "authorization_code";
                form["client_id"] = AppKey;
                form["code_verifier"] = s.Verifier;
                form["redirect_uri"] = SignInPage;
                Reply t = PostForm("https://api.dropboxapi.com/oauth2/token", form);
                Dictionary<string, object> d = Json.AsDict(Json.Parse(t.Text)) ?? new Dictionary<string, object>();
                if (t.Status != 200) throw new UserError("Dropbox did not accept the sign-in: " + ErrorText(t.Text));
                string refresh = Json.Str(d, "refresh_token");
                if (refresh.Length == 0) throw new UserError("Dropbox signed you in but did not give the launcher lasting access. Try again.");
                Remember(Json.Str(d, "access_token"), Json.Str(d, "expires_in"), refresh);
                return refresh;
            }
            throw new UserError("The sign-in code expired before it was used. Press SIGN IN WITH DROPBOX again.");
        }

        static void Remember(string token, string expiresIn, string refresh)
        {
            int seconds;
            if (!int.TryParse(expiresIn, NumberStyles.Integer, CultureInfo.InvariantCulture, out seconds)) seconds = 14400;
            accessToken = token;
            accessFor = refresh;
            accessUntil = DateTime.UtcNow.AddSeconds(Math.Max(60, seconds - 120));
        }

        static string Access()
        {
            lock (gate)
            {
                if (!string.IsNullOrEmpty(accessToken) && accessFor == RefreshToken && DateTime.UtcNow < accessUntil) return accessToken;
                if (string.IsNullOrEmpty(RefreshToken)) throw new UserError("Not signed in to Dropbox. Sign in under Options > Cloud Save.");
                var form = new Dictionary<string, string>();
                form["grant_type"] = "refresh_token";
                form["refresh_token"] = RefreshToken;
                form["client_id"] = AppKey;
                Reply r = PostForm("https://api.dropboxapi.com/oauth2/token", form);
                Dictionary<string, object> d = Json.AsDict(Json.Parse(r.Text)) ?? new Dictionary<string, object>();
                if (r.Status != 200)
                {
                    if (Json.Str(d, "error") == "invalid_grant")
                        throw new UserError("Dropbox's sign-in has ended (it was removed or expired). Sign in again under Options > Cloud Save.");
                    throw new UserError("Dropbox refused the sign-in: " + ErrorText(r.Text));
                }
                Remember(Json.Str(d, "access_token"), Json.Str(d, "expires_in"), RefreshToken);
                return accessToken;
            }
        }

        // the signed-in account's email (shown so the player can confirm it is theirs)
        public static string Email()
        {
            try
            {
                Reply r = Send("POST", "https://api.dropboxapi.com/2/users/get_current_account", "application/json",
                               Encoding.UTF8.GetBytes("null"), Access());
                return r.Status == 200 ? Json.Str(Json.AsDict(Json.Parse(r.Text)), "email") : "";
            }
            catch (Exception) { return ""; }
        }

        public static void Revoke(string refresh)
        {
            if (string.IsNullOrEmpty(refresh)) return;
            string saved = RefreshToken;
            try
            {
                RefreshToken = refresh;
                Send("POST", "https://api.dropboxapi.com/2/auth/token/revoke", null, null, Access());
            }
            catch (Exception) { }
            finally { RefreshToken = saved; }       // the device's own sign-in back, even when the revoke failed
            lock (gate) { accessToken = null; accessFor = null; }
        }

        // ------------------------------------------------------------------ the app folder
        public Dictionary<string, string> List()
        {
            var files = new Dictionary<string, string>(StringComparer.Ordinal);
            Reply r = Rpc("files/list_folder", "{\"path\":\"\",\"recursive\":true}");
            while (true)
            {
                Check(r, "list its app folder");
                Dictionary<string, object> d = Json.AsDict(Json.Parse(r.Text));
                foreach (object o in Json.AsList(Json.Get(d, "entries")) ?? new List<object>())
                {
                    Dictionary<string, object> e = Json.AsDict(o);
                    if (Json.Str(e, ".tag") != "file") continue;
                    string path = Json.Str(e, "path_display").TrimStart('/');
                    if (!SafePath(path)) continue;
                    files[path] = Json.Str(e, "content_hash").ToLowerInvariant();
                }
                if (Json.Str(d, "has_more") != "True") break;
                r = Rpc("files/list_folder/continue", "{\"cursor\":" + JsonText(Json.Str(d, "cursor")) + "}");
            }
            return files;
        }

        static bool SafePath(string path)
        {
            if (path.Length == 0) return false;
            foreach (string part in path.Split('/'))
                if (part.Length == 0 || part == "." || part == ".." || part.IndexOfAny(new[] { '\\', ':' }) >= 0) return false;
            return true;
        }

        // Dropbox's content hash: SHA-256 of each 4 MB block, then SHA-256 of those hashes together
        public string LocalHash(string file)
        {
            using (var whole = new MemoryStream())
            using (var f = File.OpenRead(file))
            {
                byte[] block = new byte[4 * 1024 * 1024];
                while (true)
                {
                    int n = 0, got;
                    while (n < block.Length && (got = f.Read(block, n, block.Length - n)) > 0) n += got;
                    if (n == 0) break;
                    using (var sha = SHA256.Create()) { byte[] h = sha.ComputeHash(block, 0, n); whole.Write(h, 0, h.Length); }
                    if (n < block.Length) break;
                }
                using (var sha = SHA256.Create()) return CloudCopy.Hex(sha.ComputeHash(whole.ToArray()));
            }
        }

        public byte[] Download(string path)
        {
            Reply r = Send("POST", "https://content.dropboxapi.com/2/files/download", null, null, Access(),
                           "{\"path\":" + JsonText("/" + path) + "}");
            Check(r, "download " + path);
            return r.Data ?? new byte[0];
        }

        public void Upload(string path, byte[] data)
        {
            Reply r = Send("POST", "https://content.dropboxapi.com/2/files/upload", "application/octet-stream", data, Access(),
                           "{\"path\":" + JsonText("/" + path) + ",\"mode\":\"overwrite\",\"mute\":true}");
            Check(r, "upload " + path);
        }

        public void Delete(string path)
        {
            Reply r = Rpc("files/delete_v2", "{\"path\":" + JsonText("/" + path) + "}");
            if (r.Status != 409) Check(r, "remove " + path);                 // 409: already gone
        }

        Reply Rpc(string endpoint, string json)
        {
            return Send("POST", "https://api.dropboxapi.com/2/" + endpoint, "application/json", Encoding.UTF8.GetBytes(json), Access());
        }

        // JSON text, every character outside plain ASCII escaped (it also goes into a header: Dropbox-API-Arg)
        static string JsonText(string s)
        {
            var sb = new StringBuilder("\"");
            foreach (char c in s ?? "")
            {
                if (c == '"' || c == '\\') sb.Append('\\').Append(c);
                else if (c < ' ' || c > '~') sb.Append("\\u").Append(((int)c).ToString("x4", CultureInfo.InvariantCulture));
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
            throw new UserError("Dropbox could not " + what + " (" + r.Status + "): " + ErrorText(r.Text));
        }

        static string ErrorText(string body)
        {
            try
            {
                Dictionary<string, object> d = Json.AsDict(Json.Parse(body));
                string s = Json.Str(d, "error_summary");
                if (s.Length > 0) return s;
                s = Json.Str(d, "error_description");
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
            return Send("POST", url, "application/x-www-form-urlencoded", Encoding.UTF8.GetBytes(string.Join("&", parts.ToArray())), null);
        }

        static Reply Send(string method, string url, string contentType, byte[] body, string bearer, string apiArg = null)
        {
            var req = (HttpWebRequest)WebRequest.Create(url);
            req.Method = method;
            req.UserAgent = Net.UserAgent;
            req.Timeout = 30000;
            req.ReadWriteTimeout = 120000;
            if (bearer != null) req.Headers[HttpRequestHeader.Authorization] = "Bearer " + bearer;
            if (apiArg != null) req.Headers["Dropbox-API-Arg"] = apiArg;
            if (body != null)
            {
                req.ContentType = contentType;
                req.ContentLength = body.Length;
                using (Stream s = req.GetRequestStream()) s.Write(body, 0, body.Length);
            }
            else if (method == "POST") req.ContentLength = 0;
            HttpWebResponse resp;
            try { resp = (HttpWebResponse)req.GetResponse(); }
            catch (WebException e)
            {
                resp = e.Response as HttpWebResponse;
                if (resp == null) throw new UserError("Dropbox could not be reached (" + e.Message + ").");
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
