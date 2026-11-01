// Optional SurvivorsForge metadata and artwork enrichment for the existing Addons catalog.
// The add-on list and installer remain owned by this launcher. SurvivorsForge is only consulted
// for entries explicitly tagged with a stable Forge id in assets/addons.txt.
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Threading;

namespace AzerothLauncher
{
    public sealed class ForgeAddonInfo
    {
        public string Id, Name, Tag, AssetUrl, Sha256, Trust, ImagePath, LogoPath;
        public List<string> ImagePaths = new List<string>();
        public bool Removed, HasSourceScreenshots;
    }

    public static class SurvivorsForge
    {
        const string Api = "https://forgeapi.rezondes.net";
        const string LogoUrl = "https://forge.rezondes.net/static/icon.png";
        const int RefreshHours = 168;               // persisted handoff cadence: no more often than once per seven days
        const int RequestTimeoutMs = 12000;
        const int MaxJsonBytes = 8 * 1024 * 1024;
        const int MaxImageBytes = 16 * 1024 * 1024;
        static readonly object gate = new object();
        static bool loaded, registryBusy;
        static Dictionary<string, object> state;
        static List<Action> registryCallbacks = new List<Action>();
        static HashSet<string> detailsBusy = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        static string CacheDir { get { return Path.Combine(Util.DataDir, "SurvivorsForge"); } }
        static string StatePath { get { return Path.Combine(CacheDir, "cache.json"); } }
        static string ImagesDir { get { return Path.Combine(CacheDir, "images"); } }

        static string UtcStamp(DateTime value) { return value.ToUniversalTime().ToString("o", CultureInfo.InvariantCulture); }
        static DateTime ReadUtc(string key)
        {
            lock (gate)
            {
                DateTime value;
                return DateTime.TryParse(Json.Str(State(), key), CultureInfo.InvariantCulture,
                    DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out value) ? value : DateTime.MinValue;
            }
        }

        static Dictionary<string, object> State()
        {
            EnsureLoaded();
            return state;
        }

        static void EnsureLoaded()
        {
            lock (gate)
            {
                if (loaded) return;
                loaded = true;
                state = null;
                try { state = Json.AsDict(Json.Parse(File.ReadAllText(StatePath, Encoding.UTF8))); }
                catch (Exception) { }
                if (state == null) state = new Dictionary<string, object>();
                if (Json.AsDict(Json.Get(state, "details")) == null) state["details"] = new Dictionary<string, object>();
            }
        }

        static void SaveState()
        {
            lock (gate)
            {
                try
                {
                    Directory.CreateDirectory(CacheDir);
                    Util.WriteAtomic(StatePath, Json.Write(state, true));
                }
                catch (Exception) { }                     // metadata must never block the launcher
            }
        }

        public static void Start()
        {
            if (Program.DisplayPreviewBuild) return;   // the isolated graphics build stays offline
            EnsureLoaded();
            // SurvivorsForge is contacted only when the user opens Download More. Review labels
            // are local catalog decisions; the on-demand lookup supplies artwork and install hashes.
        }

        static bool RegistryAvailable()
        {
            lock (gate) return Json.AsDict(Json.Get(state, "registry")) != null;
        }

        static void RefreshRegistry(Action completed)
        {
            EnsureLoaded();
            bool start = false;
            lock (gate)
            {
                if (completed != null) registryCallbacks.Add(completed);
                if (registryBusy) return;                 // coalesce startup, timer and view-open triggers
                DateTime last = ReadUtc("last_attempt_utc");
                bool due = Json.AsDict(Json.Get(state, "registry")) == null ||
                           DateTime.UtcNow - last >= TimeSpan.FromHours(RefreshHours);
                if (!due)
                {
                    if (completed != null) registryCallbacks.Remove(completed);
                    if (completed != null) RunUi(completed);
                    return;
                }
                registryBusy = true;
                state["last_attempt_utc"] = UtcStamp(DateTime.UtcNow);
                SaveState();
                start = true;
            }
            if (!start) return;
            ThreadPool.QueueUserWorkItem(delegate
            {
                try
                {
                    string etag;
                    lock (gate) etag = Json.Str(state, "etag");
                    int status;
                    string responseEtag;
                    string body = GetText(Api + "/v1/registry", etag, MaxJsonBytes, out status, out responseEtag);
                    lock (gate)
                    {
                        if (status == 200)
                        {
                            Dictionary<string, object> parsed = Json.AsDict(Json.Parse(body));
                            if (Convert.ToInt32(Json.Get(parsed, "schema"), CultureInfo.InvariantCulture) != 1)
                                throw new FormatException("Unsupported SurvivorsForge registry schema.");
                            if (Json.AsList(Json.Get(parsed, "addons")) == null)
                                throw new FormatException("The SurvivorsForge registry did not contain an addon list.");
                            state["registry"] = parsed;  // replacing the registry also honors removals
                            if (!string.IsNullOrEmpty(responseEtag)) state["etag"] = responseEtag;
                        }
                        else if (status != 304) throw new IOException("SurvivorsForge answered with HTTP " + status + ".");
                        state["last_success_utc"] = UtcStamp(DateTime.UtcNow);
                    }
                }
                catch (Exception) { }                       // keep the last successful registry, no retry loop
                List<Action> callbacks;
                lock (gate)
                {
                    SaveState();
                    registryBusy = false;
                    callbacks = new List<Action>(registryCallbacks);
                    registryCallbacks.Clear();
                }
                foreach (Action action in callbacks) RunUi(action);
            });
        }

        static void RunUi(Action action)
        {
            if (action == null) return;
            LauncherApp app = LauncherApp.App;
            if (app != null) app.Ui(action);
        }

        static string GetText(string url, string etag, int maxBytes, out int status, out string responseEtag)
        {
            status = 0;
            responseEtag = null;
            Uri uri = new Uri(url);
            if (uri.Scheme != Uri.UriSchemeHttps || !string.Equals(uri.Host, "forgeapi.rezondes.net", StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("Unexpected SurvivorsForge API address.");
            var req = (HttpWebRequest)WebRequest.Create(uri);
            req.Method = "GET";
            req.Accept = "application/json";
            req.UserAgent = "AzerothSurvivorsLauncher/3.17 (+https://github.com/Slimmy25/Azeroth-Survivors-Launcher)";
            req.Timeout = RequestTimeoutMs;
            req.ReadWriteTimeout = RequestTimeoutMs;
            req.AllowAutoRedirect = false;
            if (!string.IsNullOrEmpty(etag)) req.Headers[HttpRequestHeader.IfNoneMatch] = etag;
            HttpWebResponse resp = null;
            try
            {
                try { resp = (HttpWebResponse)req.GetResponse(); }
                catch (WebException e)
                {
                    resp = e.Response as HttpWebResponse;
                    if (resp == null) throw;
                }
                status = (int)resp.StatusCode;
                responseEtag = resp.Headers[HttpResponseHeader.ETag];
                if (status == 304) return "";
                if (status < 200 || status >= 300) throw new IOException("SurvivorsForge answered with HTTP " + status + ".");
                using (Stream input = resp.GetResponseStream())
                using (var output = new MemoryStream())
                {
                    byte[] buffer = new byte[32768];
                    int n;
                    while ((n = input.Read(buffer, 0, buffer.Length)) > 0)
                    {
                        if (output.Length + n > maxBytes) throw new InvalidDataException("SurvivorsForge response was too large.");
                        output.Write(buffer, 0, n);
                    }
                    return Encoding.UTF8.GetString(output.ToArray());
                }
            }
            finally { if (resp != null) resp.Close(); }
        }

        static byte[] GetBytes(string url)
        {
            Uri uri = new Uri(url);
            if (uri.Scheme != Uri.UriSchemeHttps || !string.Equals(uri.Host, "forge.rezondes.net", StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("Unexpected SurvivorsForge image address.");
            var req = (HttpWebRequest)WebRequest.Create(uri);
            req.Method = "GET";
            req.UserAgent = "AzerothSurvivorsLauncher/3.17 (+https://github.com/Slimmy25/Azeroth-Survivors-Launcher)";
            req.Timeout = RequestTimeoutMs;
            req.ReadWriteTimeout = RequestTimeoutMs;
            req.AllowAutoRedirect = false;
            using (var resp = (HttpWebResponse)req.GetResponse())
            {
                if ((int)resp.StatusCode < 200 || (int)resp.StatusCode >= 300) throw new IOException("Image request failed.");
                using (Stream input = resp.GetResponseStream())
                using (var output = new MemoryStream())
                {
                    byte[] buffer = new byte[32768];
                    int n;
                    while ((n = input.Read(buffer, 0, buffer.Length)) > 0)
                    {
                        if (output.Length + n > MaxImageBytes) throw new InvalidDataException("SurvivorsForge image was too large.");
                        output.Write(buffer, 0, n);
                    }
                    return output.ToArray();
                }
            }
        }

        static string CacheImage(string url, bool refreshExisting = false)
        {
            if (string.IsNullOrWhiteSpace(url)) return null;
            Uri imageUri = new Uri(url);
            string leaf = Path.GetFileName(imageUri.AbsolutePath);
            if (string.IsNullOrWhiteSpace(leaf) || leaf.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0) return null;
            // Image endpoints commonly reuse names such as icon.webp or screenshot-1.webp.
            // Include the URL hash so two addons cannot overwrite each other's cached art.
            string key;
            using (SHA256 sha = SHA256.Create())
                key = BitConverter.ToString(sha.ComputeHash(Encoding.UTF8.GetBytes(imageUri.AbsoluteUri))).Replace("-", "").ToLowerInvariant();
            string path = Path.Combine(ImagesDir, key + "-" + leaf);
            if (!refreshExisting && File.Exists(path) && new FileInfo(path).Length > 0) return path;
            byte[] bytes = GetBytes(url);
            Directory.CreateDirectory(ImagesDir);
            string temp = path + "." + Guid.NewGuid().ToString("N") + ".part";
            try
            {
                File.WriteAllBytes(temp, bytes);
                Util.Replace(temp, path);
            }
            finally { try { if (File.Exists(temp)) File.Delete(temp); } catch (Exception) { } }
            return path;
        }

        static Dictionary<string, object> RegistryAddon(string id)
        {
            lock (gate)
            {
                Dictionary<string, object> registry = Json.AsDict(Json.Get(State(), "registry"));
                foreach (object value in Json.AsList(Json.Get(registry, "addons")) ?? new List<object>())
                {
                    Dictionary<string, object> item = Json.AsDict(value);
                    if (string.Equals(Json.Str(item, "id"), id, StringComparison.OrdinalIgnoreCase)) return item;
                }
                return null;
            }
        }

        static Dictionary<string, object> CachedDetails(string id)
        {
            lock (gate) return Json.AsDict(Json.Get(Json.AsDict(Json.Get(State(), "details")), id));
        }

        static Dictionary<string, object> ExactVersion(AddonEntry entry, Dictionary<string, object> registryAddon)
        {
            if (entry == null || registryAddon == null || string.IsNullOrWhiteSpace(entry.Url)) return null;
            Dictionary<string, object> latest = Json.AsDict(Json.Get(registryAddon, "latest"));
            if (SameUrl(entry.Url, Json.Str(latest, "asset_url"))) return latest;
            Dictionary<string, object> cached = CachedDetails(entry.ForgeId);
            foreach (object value in Json.AsList(Json.Get(cached, "versions")) ?? new List<object>())
            {
                Dictionary<string, object> version = Json.AsDict(value);
                if (SameUrl(entry.Url, Json.Str(version, "asset_url"))) return version;
            }
            return null;
        }

        static Dictionary<string, object> LatestVersion(Dictionary<string, object> registryAddon)
        {
            return registryAddon == null ? null : Json.AsDict(Json.Get(registryAddon, "latest"));
        }

        static bool SameUrl(string a, string b)
        {
            Uri x, y;
            return Uri.TryCreate(a ?? "", UriKind.Absolute, out x) && Uri.TryCreate(b ?? "", UriKind.Absolute, out y) &&
                   string.Equals(x.AbsoluteUri.TrimEnd('/'), y.AbsoluteUri.TrimEnd('/'), StringComparison.OrdinalIgnoreCase);
        }

        public static ForgeAddonInfo GetInfo(AddonEntry entry)
        {
            if (entry == null) return null;
            ForgeAddonInfo info = GetInfoSingle(entry);
            if (info == null || info.Removed) return info;
            foreach (string id in entry.ForgeIds ?? new List<string>())
            {
                if (string.IsNullOrWhiteSpace(id) || string.Equals(id, entry.ForgeId, StringComparison.OrdinalIgnoreCase)) continue;
                var relatedEntry = new AddonEntry { ForgeId = id, Url = entry.Url };
                ForgeAddonInfo related = GetInfoSingle(relatedEntry);
                if (related == null || related.Removed) continue;
                foreach (string path in related.ImagePaths)
                    if (!string.IsNullOrWhiteSpace(path) && !info.ImagePaths.Contains(path)) info.ImagePaths.Add(path);
                info.HasSourceScreenshots = info.HasSourceScreenshots || related.HasSourceScreenshots;
                if (string.IsNullOrWhiteSpace(info.ImagePath)) info.ImagePath = related.ImagePath;
            }
            return info;
        }

        static ForgeAddonInfo GetInfoSingle(AddonEntry entry)
        {
            if (entry == null) return null;
            if (string.IsNullOrWhiteSpace(entry.ForgeId))
                return string.IsNullOrWhiteSpace(entry.GalleryId) ? null : new ForgeAddonInfo { Id = entry.GalleryId };
            Dictionary<string, object> addon = RegistryAddon(entry.ForgeId);
            if (addon == null)
            {
                if (RegistryAvailable()) return new ForgeAddonInfo { Id = entry.ForgeId, Removed = true };
                return new ForgeAddonInfo { Id = entry.ForgeId, LogoPath = Json.Str(State(), "logo_path") };
            }
            // A locally mirrored ZIP has a different URL from its upstream release. Use the
            // matched upstream addon and latest art for the catalog identity when URLs differ.
            Dictionary<string, object> version = ExactVersion(entry, addon) ?? LatestVersion(addon);
            if (version == null) return new ForgeAddonInfo { Id = entry.ForgeId, Name = Json.Str(addon, "name"), LogoPath = Json.Str(State(), "logo_path") };
            var info = new ForgeAddonInfo
            {
                Id = entry.ForgeId,
                Name = Json.Str(addon, "name"),
                Tag = Json.Str(version, "tag"),
                AssetUrl = Json.Str(version, "asset_url"),
                Sha256 = Json.Str(version, "sha256"),
                Trust = Json.Str(version, "trust"),
                // The SurvivorsForge mark identifies this catalog partnership, not the
                // release's safety state, so show it even before exact-version art loads.
                LogoPath = Json.Str(State(), "logo_path")
            };
            // Artwork is attached to an explicitly matched addon identity. The visible Verified
            // seal is a separate, one-time flag in assets/addons.txt.
            if (string.IsNullOrWhiteSpace(info.Sha256) ||
                string.Equals(info.Trust, "blocked", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(info.Trust, "source_changed", StringComparison.OrdinalIgnoreCase)) return info;
            Dictionary<string, object> details = CachedDetails(entry.ForgeId);
            if (!string.Equals(Json.Str(details, "art_tag"), info.Tag, StringComparison.Ordinal) ||
                !SameUrl(info.AssetUrl, Json.Str(details, "art_asset_url")) ||
                !string.Equals(info.Sha256, Json.Str(details, "art_sha256"), StringComparison.OrdinalIgnoreCase)) return info;
            // Screenshots belong in the picture browser; catalog cards use the addon's icon.
            info.ImagePath = Json.Str(details, "icon_path");
            info.HasSourceScreenshots = string.Equals(Json.Str(details, "has_screenshots"), "true", StringComparison.OrdinalIgnoreCase);
            string detailLogo = Json.Str(details, "logo_path");
            if (!string.IsNullOrWhiteSpace(detailLogo)) info.LogoPath = detailLogo;
            foreach (object path in Json.AsList(Json.Get(details, "image_paths")) ?? new List<object>())
            {
                string value = Convert.ToString(path, CultureInfo.InvariantCulture);
                if (!string.IsNullOrWhiteSpace(value) && !info.ImagePaths.Contains(value)) info.ImagePaths.Add(value);
            }
            if (!string.IsNullOrWhiteSpace(info.ImagePath) && !info.ImagePaths.Contains(info.ImagePath))
                info.ImagePaths.Insert(0, info.ImagePath);
            return info;
        }

        public static string TrustLabel(ForgeAddonInfo info)
        {
            if (info == null) return null;
            if (info.Removed) return "Unavailable";
            if (string.IsNullOrWhiteSpace(info.Trust)) return "Version not matched";
            return string.Equals(info.Trust, "verified", StringComparison.OrdinalIgnoreCase)
                ? "Verified" : CultureInfo.InvariantCulture.TextInfo.ToTitleCase(info.Trust.Replace('_', ' '));
        }

        public static bool CanInstall(AddonEntry entry, out string expectedSha256, out bool askFirst, out string error)
        {
            expectedSha256 = null;
            askFirst = false;
            error = null;
            ForgeAddonInfo info = GetInfo(entry);
            if (info == null) return true;                 // not enrolled: preserve the launcher's existing source path
            // The launcher remains an independent catalog/installer. A removed Forge listing removes
            // enrichment only; it does not invalidate the launcher's own addon source.
            if (info.Removed) return true;
            if (string.IsNullOrWhiteSpace(info.AssetUrl)) return true; // same addon, but this catalog file is a different version/source
            if (string.Equals(info.Trust, "blocked", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(info.Trust, "source_changed", StringComparison.OrdinalIgnoreCase))
            { error = "SurvivorsForge does not allow installing this exact addon version."; return false; }
            if (string.IsNullOrWhiteSpace(info.Sha256))
            { error = "SurvivorsForge has not published a SHA-256 for this addon version, so it cannot be installed yet."; return false; }
            expectedSha256 = info.Sha256;
            askFirst = !entry.ForgeVerified &&
                       (string.Equals(info.Trust, "flagged", StringComparison.OrdinalIgnoreCase) ||
                        string.Equals(info.Trust, "unscanned", StringComparison.OrdinalIgnoreCase));
            return true;
        }

        public static void PrepareDownloadPage(IList<AddonEntry> entries, Action completed)
        {
            if (Program.DisplayPreviewBuild) return;
            RefreshRegistry(delegate
            {
                ThreadPool.QueueUserWorkItem(delegate
                {
                    bool detailsChanged = false;
                    try
                    {
                        string logoPath = Json.Str(State(), "logo_path");
                        if (string.IsNullOrWhiteSpace(logoPath) || !File.Exists(logoPath))
                        {
                            logoPath = CacheImage(LogoUrl);
                            if (!string.IsNullOrWhiteSpace(logoPath))
                            {
                                lock (gate) State()["logo_path"] = logoPath;
                                detailsChanged = true;
                            }
                        }
                    }
                    catch (Exception) { } // branding artwork is optional and never blocks browsing
                    foreach (AddonEntry entry in entries ?? new List<AddonEntry>())
                    {
                        if (entry == null) continue;
                        var ids = new List<string>();
                        if (!string.IsNullOrWhiteSpace(entry.ForgeId)) ids.Add(entry.ForgeId);
                        foreach (string relatedId in entry.ForgeIds ?? new List<string>())
                            if (!string.IsNullOrWhiteSpace(relatedId) && !ids.Contains(relatedId)) ids.Add(relatedId);
                        foreach (string id in ids)
                        {
                            Dictionary<string, object> addon = RegistryAddon(id);
                            if (addon == null) continue;
                            Dictionary<string, object> latest = Json.AsDict(Json.Get(addon, "latest"));
                            string tag = Json.Str(latest, "tag");
                            Dictionary<string, object> cached = CachedDetails(id);
                            DateTime lastRegistrySuccess = ReadUtc("last_success_utc");
                            DateTime detailsFetched;
                            bool detailIsFresh = DateTime.TryParse(Json.Str(cached, "fetched_utc"), CultureInfo.InvariantCulture,
                                DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out detailsFetched) && detailsFetched >= lastRegistrySuccess;
                            bool artMatchesLatest = string.Equals(Json.Str(cached, "art_tag"), tag, StringComparison.Ordinal) &&
                                                    SameUrl(Json.Str(cached, "art_asset_url"), Json.Str(latest, "asset_url"));
                            if (cached != null && string.Equals(Json.Str(cached, "tag"), tag, StringComparison.Ordinal) &&
                                Json.AsList(Json.Get(cached, "versions")) != null &&
                                Json.AsList(Json.Get(cached, "image_paths")) != null &&
                                !string.IsNullOrWhiteSpace(Json.Str(cached, "has_screenshots")) && artMatchesLatest && detailIsFresh) continue;
                            lock (gate) if (detailsBusy.Contains(id)) continue; else detailsBusy.Add(id);
                            try { FetchDetails(new AddonEntry { ForgeId = id, Url = entry.Url }, addon, latest); detailsChanged = true; }
                            catch (Exception) { }
                            finally { lock (gate) detailsBusy.Remove(id); }
                        }
                    }
                    SaveState();
                    if (detailsChanged) RunUi(completed);       // avoids a page-rebuild loop when a cache is already current
                });
            });
        }

        static void FetchDetails(AddonEntry entry, Dictionary<string, object> addon, Dictionary<string, object> latest)
        {
            string id = entry.ForgeId;
            string tag = Json.Str(latest, "tag");
            string slug = Uri.EscapeDataString(id);
            int status;
            string ignored;
            string detailsText = GetText(Api + "/v1/addons/" + slug, null, MaxJsonBytes, out status, out ignored);
            if (status != 200) throw new IOException("Addon details unavailable.");
            Dictionary<string, object> detail = Json.AsDict(Json.Parse(detailsText));
            if (!string.Equals(Json.Str(detail, "slug"), id, StringComparison.OrdinalIgnoreCase)) throw new FormatException("Addon identity mismatch.");
            string versionsText = GetText(Api + "/v1/addons/" + slug + "/versions", null, MaxJsonBytes, out status, out ignored);
            if (status != 200) throw new IOException("Addon versions unavailable.");
            List<object> versions = Json.AsList(Json.Parse(versionsText));
            if (versions == null) throw new FormatException("Addon versions response was invalid.");
            var cached = new Dictionary<string, object>();
            cached["tag"] = tag;
            cached["fetched_utc"] = UtcStamp(DateTime.UtcNow);
            cached["detail"] = detail;
            cached["versions"] = versions;
            cached["image_paths"] = new List<object>();
            Dictionary<string, object> eligible = null;
            foreach (object value in versions)
            {
                Dictionary<string, object> version = Json.AsDict(value);
                if (SameUrl(entry.Url, Json.Str(version, "asset_url"))) { eligible = version; break; }
            }
            // Launcher-hosted mirrors intentionally have a different URL. Artwork is for the
            // addon identity, so if no exact mirror URL exists, use the API's current release.
            if (eligible == null) eligible = latest;
            string icon = Json.Str(detail, "icon_url");
            string eligibleTrust = Json.Str(eligible, "trust");
            if (eligible != null && !string.IsNullOrWhiteSpace(Json.Str(eligible, "sha256")) &&
                !string.Equals(eligibleTrust, "blocked", StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(eligibleTrust, "source_changed", StringComparison.OrdinalIgnoreCase))
            {
                cached["art_tag"] = Json.Str(eligible, "tag");
                cached["art_asset_url"] = Json.Str(eligible, "asset_url");
                cached["art_sha256"] = Json.Str(eligible, "sha256");
                var imagePaths = new List<object>();
                string iconPath = CacheImage(icon);
                cached["icon_url"] = icon;
                cached["icon_path"] = iconPath;
                List<object> images = Json.AsList(Json.Get(detail, "images")) ?? new List<object>();
                foreach (object image in images)
                {
                    Dictionary<string, object> imageInfo = Json.AsDict(image);
                    string imageUrl = Json.Str(imageInfo, "url");
                    if (string.IsNullOrWhiteSpace(imageUrl)) continue;
                    string imagePath = CacheImage(imageUrl);
                    if (!string.IsNullOrEmpty(imagePath) && !imagePaths.Contains(imagePath)) imagePaths.Add(imagePath);
                    if (string.IsNullOrEmpty(Json.Str(cached, "image_path"))) cached["image_path"] = imagePath;
                }
                cached["has_screenshots"] = imagePaths.Count > 0 ? "true" : "false";
                // Prefer an addon screenshot for the card and gallery's first image. Use its
                // icon only when SurvivorsForge has no screenshots for that addon.
                if (imagePaths.Count == 0 && !string.IsNullOrEmpty(iconPath)) imagePaths.Add(iconPath);
                else if (!string.IsNullOrEmpty(iconPath) && !imagePaths.Contains(iconPath)) imagePaths.Add(iconPath);
                if (string.IsNullOrWhiteSpace(Json.Str(cached, "image_path"))) cached["image_path"] = iconPath;
                cached["image_paths"] = imagePaths;
                cached["logo_path"] = CacheImage(LogoUrl);     // shared provider mark: reuse the cache across addon entries
            }
            lock (gate)
            {
                Dictionary<string, object> all = Json.AsDict(Json.Get(state, "details"));
                if (all == null) { all = new Dictionary<string, object>(); state["details"] = all; }
                all[id] = cached;
            }
        }

        public static Image LoadCardImage(ForgeAddonInfo info)
        {
            if (info == null || info.Removed ||
                string.IsNullOrWhiteSpace(info.ImagePath) || !File.Exists(info.ImagePath)) return null;
            return WebPDecoder.LoadImage(info.ImagePath);
        }

        public static List<Image> LoadGalleryImages(ForgeAddonInfo info)
        {
            var result = new List<Image>();
            if (info == null) return result;
            if (!info.Removed && info.HasSourceScreenshots)
            {
                foreach (string path in info.ImagePaths ?? new List<string>())
                {
                    if (string.IsNullOrWhiteSpace(path) || !File.Exists(path)) continue;
                    Image image = WebPDecoder.LoadImage(path);
                    if (image != null) result.Add(image);       // unsupported/corrupt artwork never breaks Addons
                }
            }
            if (result.Count == 0) LoadLocalGallery(info.Id, result);
            return result;
        }

        public static bool HasGalleryImages(ForgeAddonInfo info)
        {
            return info != null && ((!info.Removed && info.HasSourceScreenshots) || LocalGalleryResource(info.Id, 0) != null);
        }

        static void LoadLocalGallery(string id, List<Image> result)
        {
            for (int i = 0; ; i++)
            {
                string name = LocalGalleryResource(id, i);
                if (name == null) break;
                try
                {
                    using (Stream stream = Util.ResourceStream(name))
                    {
                        if (stream == null) continue;
                        using (var memory = new MemoryStream())
                        {
                            stream.CopyTo(memory);
                            Image image = WebPDecoder.LoadImage(memory.ToArray());
                            if (image != null) result.Add(image);
                        }
                    }
                }
                catch (Exception) { }
            }
        }

        static string LocalGalleryResource(string id, int index)
        {
            string prefix = "AS.addon-gallery.";
            if (string.Equals(id, "moveui", StringComparison.OrdinalIgnoreCase)) return index == 0 ? prefix + "moveui.png" : null;
            if (string.Equals(id, "localclock-addon", StringComparison.OrdinalIgnoreCase)) return index == 0 ? prefix + "localclock-addon.png" : null;
            if (string.Equals(id, "blackwellui", StringComparison.OrdinalIgnoreCase)) return index == 0 ? prefix + "blackwellui.png" : null;
            if (string.Equals(id, "azsmoveanything", StringComparison.OrdinalIgnoreCase))
                return index == 0 ? prefix + "kodfod-moveanything-1.png" : index == 1 ? prefix + "kodfod-moveanything-2.png" : null;
            if (string.Equals(id, "aetherhud", StringComparison.OrdinalIgnoreCase)) return index == 0 ? prefix + "aetherhud.webp" : null;
            if (string.Equals(id, "azerloot", StringComparison.OrdinalIgnoreCase)) return index == 0 ? prefix + "azerloot.png" : null;
            if (string.Equals(id, "azerothhub", StringComparison.OrdinalIgnoreCase)) return index == 0 ? prefix + "azerothhub.png" : null;
            return null;
        }

        public static Image LoadWatermark(ForgeAddonInfo info)
        {
            if (info == null || info.Removed ||
                string.IsNullOrWhiteSpace(info.LogoPath) || !File.Exists(info.LogoPath)) return null;
            return WebPDecoder.LoadImage(info.LogoPath);
        }
    }
}
