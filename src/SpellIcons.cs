// Pictures for the STATS page, shown from Wowhead's image host (wow.zamimg.com) at runtime; nothing is built into
// the exe. Each picture is fetched only when a row that shows it is on screen, then kept in the launcher's temp
// folder so it is not downloaded again. Until it is there (or when it cannot be fetched) the row keeps its badge.
//   * spells: assets\spell-icons.txt names each spell's icon; a spell missing from it is looked up on Wowhead
//     (Classic first, then the current game) the first time it is shown, and the answer is remembered
//   * classes: Wowhead's class icons (classicon_mage ...)
//   * creatures (Nemesis): assets\npc-models.txt (from the CMaNGOS Classic database) gives the creature's model,
//     and Wowhead's picture of that model is shown
using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Windows.Forms;

namespace AzerothLauncher
{
    public static class SpellIcons
    {
        const string IconHost = "https://wow.zamimg.com/images/wow/icons/large/";
        static Dictionary<string, string> names;              // lower-case spell name -> icon name (the list)
        static readonly Dictionary<string, string> found = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        static readonly Dictionary<string, Image> images = new Dictionary<string, Image>();   // picture key -> picture (null: failed)
        static readonly HashSet<string> loading = new HashSet<string>();
        static readonly List<Control> waiting = new List<Control>();

        static string CacheDir { get { return Path.Combine(Util.TempDir, "spell-icons"); } }
        static string FoundFile { get { return Path.Combine(CacheDir, "looked-up.txt"); } }   // name|answer, "" = none

        static void Load()
        {
            if (names != null) return;
            names = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            try
            {
                foreach (string raw in Util.ReadResource("AS.spell-icons.txt").Replace("\r\n", "\n").Split('\n'))
                {
                    string line = raw.Trim();
                    if (line.Length == 0 || line.StartsWith("#")) continue;
                    int bar = line.LastIndexOf('|');
                    if (bar <= 0) continue;
                    string icon = line.Substring(bar + 1).Trim().ToLowerInvariant();
                    if (Regex.IsMatch(icon, "^[a-z0-9_-]+$")) names[Key(line.Substring(0, bar))] = icon;
                }
            }
            catch (Exception) { }
            try
            {
                if (File.Exists(FoundFile))
                    foreach (string raw in File.ReadAllLines(FoundFile))
                    {
                        int bar = raw.LastIndexOf('|');
                        if (bar > 0) lock (found) found[raw.Substring(0, bar)] = raw.Substring(bar + 1);
                    }
            }
            catch (Exception) { }
        }

        // "Frostbolt (Rank 4)" and "Frostbolt 4" are Frostbolt
        static string Clean(string spell)
        {
            string s = Regex.Replace(spell ?? "", @"\s*\(.*?\)\s*$", "");
            return Regex.Replace(s, @"\s+\d+$", "").Trim();
        }

        // how names are matched: case, spaces, colons, dashes and apostrophes (straight or curly) do not count, so
        // "Shadow Word: Pain", "Shadow Word Pain" and "shadow_word_pain" are one spell; short forms count as the spell
        static readonly Dictionary<string, string> Short = new Dictionary<string, string> {
            { "swpain", "shadowwordpain" }, { "swp", "shadowwordpain" }, { "pwshield", "powerwordshield" }, { "pws", "powerwordshield" },
            { "swdeath", "shadowworddeath" }, { "pwfortitude", "powerwordfortitude" }, { "mark", "huntersmark" } };

        static string Key(string name)
        {
            string k = Regex.Replace(Clean(name).ToLowerInvariant(), "[^a-z0-9]", "");
            string full;
            return Short.TryGetValue(k, out full) ? full : k;
        }

        // ---- what the rows ask for: the picture, or null for now (`repaint` is redrawn when it arrives)
        public static Image Spell(string spell, Control repaint)
        {
            Load();
            string name = Clean(spell);
            if (name.Length == 0) return null;
            string icon;
            if (names.TryGetValue(Key(name), out icon)) return Icon(icon, repaint);
            return Lookup("spell:" + name, repaint, delegate { return FindSpellIcon(name); }, delegate(string ans) { return IconHost + ans + ".jpg"; });
        }

        static readonly Dictionary<string, string> ClassIcons = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) {
            { "warrior", "classicon_warrior" }, { "paladin", "classicon_paladin" }, { "hunter", "classicon_hunter" },
            { "rogue", "classicon_rogue" }, { "priest", "classicon_priest" }, { "shaman", "classicon_shaman" },
            { "mage", "classicon_mage" }, { "warlock", "classicon_warlock" }, { "druid", "classicon_druid" },
            { "deathknight", "classicon_deathknight" } };

        public static Image Class(string classKey, Control repaint)
        {
            string k = Regex.Replace((classKey ?? "").ToLowerInvariant(), "[^a-z]", "");
            string icon;
            return ClassIcons.TryGetValue(k, out icon) ? Icon(icon, repaint) : null;
        }

        // races and zones (Classes page): Classic's race portraits, and each zone's own art from the achievements
        static readonly Dictionary<string, string> RaceIcons = new Dictionary<string, string> {
            { "human", "race_human_male" }, { "dwarf", "race_dwarf_male" }, { "nightelf", "race_nightelf_male" },
            { "gnome", "race_gnome_male" }, { "orc", "race_orc_male" }, { "undead", "race_scourge_male" },
            { "scourge", "race_scourge_male" }, { "tauren", "race_tauren_male" }, { "troll", "race_troll_male" } };
        static readonly Dictionary<string, string> ZoneIcons = new Dictionary<string, string> {
            { "elwynnforest", "achievement_zone_elwynnforest" }, { "duskwood", "achievement_zone_duskwood" },
            { "warsonggulch", "achievement_bg_captureflag_wsg" }, { "ungorocrater", "achievement_zone_ungorocrater_01" },
            { "arathibasin", "achievement_bg_winab" }, { "feralas", "achievement_zone_feralas" },
            { "gurubashiarena", "achievement_zone_stranglethorn_01" }, { "stranglethornvale", "achievement_zone_stranglethorn_01" },
            { "scarletmonastery", "spell_holy_righteousfury" }, { "alteracvalley", "achievement_bg_winav" },
            { "blackrockmountain", "achievement_zone_blackrock_01" }, { "gatesofahnqiraj", "achievement_zone_silithus_01" },
            { "ahnqiraj", "achievement_zone_silithus_01" }, { "naxxramas", "achievement_dungeon_naxxramas" } };

        public static Image Race(string race, Control repaint)
        {
            string icon;
            foreach (string part in (race ?? "").Split('|'))       // "key|Name": either one
                if (RaceIcons.TryGetValue(Key(part), out icon)) return Icon(icon, repaint);
            return null;
        }

        // The original WoW micro-menu button texture, used only for Talent Tree Builder favorites.
        public static Image TalentButton(Control repaint)
        {
            return Picture("favorite_talent_button",
                "https://warcraft.wiki.gg/wiki/Special:FilePath/Micromenu_talents_btn_32x41.png", repaint);
        }

        public static Image Zone(string zone, Control repaint)
        {
            string icon;
            foreach (string part in (zone ?? "").Split('|'))       // "key|Name": either one
                if (ZoneIcons.TryGetValue(Key(part), out icon)) return Icon(icon, repaint);
            return null;
        }

        public static Image Icon(string icon, Control repaint)
        {
            if (string.IsNullOrEmpty(icon) || !Regex.IsMatch(icon, "^[a-z0-9_-]+$")) return null;
            return Picture("icon_" + icon, IconHost + icon + ".jpg", repaint);
        }

        // a real item's 3D preview (gear, jewelry or a Transmog look): assets\item-models.txt gives its display id;
        // the Wowhead model-viewer image is fetched and cached when first hovered
        static Dictionary<string, int> itemModels;

        static int ItemDisplay(string name)
        {
            if (itemModels == null)
            {
                var d = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
                try
                {
                    foreach (string raw in Util.ReadResource("AS.item-models.txt").Replace("\r\n", "\n").Split('\n'))
                    {
                        if (raw.StartsWith("#")) continue;
                        string[] f = raw.Split('|');
                        int id;
                        if (f.Length >= 3 && int.TryParse(f[2].Trim(), out id) && id > 0) d[f[0].Trim()] = id;
                    }
                }
                catch (Exception) { }
                itemModels = d;
            }
            int m;
            return name != null && itemModels.TryGetValue(name.Trim(), out m) ? m : 0;
        }

        public static bool HasItemModel(string name) { return ItemDisplay(name) > 0; }

        public static Image ItemModel(string name, Control repaint)
        {
            int d = ItemDisplay(name);
            if (d <= 0) return null;
            return Picture("item3d_" + d, "https://wow.zamimg.com/modelviewer/classic/webthumbs/item/" + (d & 255) + "/" + d + ".jpg", repaint);
        }

        // the model picture of a creature (Nemesis): assets\npc-models.txt gives its model, Wowhead has the picture
        static Dictionary<string, int> npcModels;

        public static Image Npc(string name, Control repaint)
        {
            int model = NpcModel(name);
            if (model <= 0) return null;
            return Picture("npc_" + model, NpcUrl(model), repaint, true);
        }

        static string NpcUrl(int model)
        {
            return "https://wow.zamimg.com/modelviewer/classic/webthumbs/npc/" + (model & 255) + "/" + model + ".jpg";
        }

        // NpcFaces: the creature's face, cut and ready (downloaded now if it is not in the cache yet), or null
        internal static Bitmap NpcFace(string name)
        {
            int model = NpcModel(name);
            if (model <= 0) return null;
            Directory.CreateDirectory(CacheDir);
            string path = Path.Combine(CacheDir, "npc_" + model + ".jpg");
            if (!File.Exists(path))
            {
                byte[] data = Get(NpcUrl(model));
                if (data == null || data.Length == 0) return null;
                File.WriteAllBytes(path + ".part", data);
                if (File.Exists(path)) File.Delete(path);
                File.Move(path + ".part", path);
            }
            Bitmap src;
            using (var tmp = Image.FromFile(path)) src = new Bitmap(tmp);
            Bitmap f = Face(src);
            if (f != src) src.Dispose();
            return f;
        }

        static readonly object npcLock = new object();

        static int NpcModel(string name)
        {
            name = (name ?? "").Trim();
            if (name.Length == 0) return 0;
            lock (npcLock)
            {
            if (npcModels == null)
            {
                npcModels = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
                try
                {
                    foreach (string raw in Util.ReadResource("AS.npc-models.txt").Replace("\r\n", "\n").Split('\n'))
                    {
                        int bar = raw.LastIndexOf('|'), id;
                        if (raw.StartsWith("#") || bar <= 0 || !int.TryParse(raw.Substring(bar + 1), out id) || id <= 0) continue;
                        string n = raw.Substring(0, bar);
                        if (!npcModels.ContainsKey(n)) npcModels[n] = id;
                    }
                }
                catch (Exception) { }
            }
            int model;
            return npcModels.TryGetValue(name, out model) ? model : 0;
            }
        }

        // ---- a name that has to be looked up first; the answer (or "none") is remembered on disk
        static Image Lookup(string key, Control repaint, Func<string> find, Func<string, string> url)
        {
            string ans;
            bool known;
            lock (found) known = found.TryGetValue(key, out ans);
            if (known) return ans.Length == 0 ? null : Picture(key, url(ans), repaint);
            lock (images)
            {
                if (repaint != null && !waiting.Contains(repaint)) waiting.Add(repaint);
                if (!loading.Add("?" + key)) return null;
            }
            ThreadPool.QueueUserWorkItem(delegate
            {
                string a = null;
                try { a = find(); } catch (Exception) { }
                if (a != null)                               // null: could not ask (offline): try again next time
                {
                    lock (found) found[key] = a;
                    try
                    {
                        Directory.CreateDirectory(CacheDir);
                        lock (found) File.AppendAllText(FoundFile, key + "|" + a + "\r\n", Encoding.UTF8);
                    }
                    catch (Exception) { }
                }
                Done("?" + key, null, false);
            });
            return null;
        }

        static Image Picture(string key, string url, Control repaint) { return Picture(key, url, repaint, false); }

        static Image Picture(string key, string url, Control repaint, bool face)
        {
            lock (images)
            {
                Image img;
                if (images.TryGetValue(key, out img)) return img;
                if (repaint != null && !waiting.Contains(repaint)) waiting.Add(repaint);
                if (!loading.Add(key)) return null;
            }
            ThreadPool.QueueUserWorkItem(delegate { Fetch(key, url, face); });
            return null;
        }

        // a creature's picture cut to its face: find the creature (what differs from the plain background), then a
        // square at the top of it, around the head (a tall figure's head is small; a beast's fills more of it)
        static Bitmap Face(Bitmap src)
        {
            int w = src.Width, h = src.Height;
            Color bg = src.GetPixel(2, 2);
            int minX = w, maxX = -1, minY = h, maxY = -1;
            Func<int, int, bool> body = delegate(int x, int y)
            {
                Color c = src.GetPixel(x, y);
                return Math.Abs(c.R - bg.R) + Math.Abs(c.G - bg.G) + Math.Abs(c.B - bg.B) > 48;
            };
            for (int y = 0; y < h; y += 2)
                for (int x = 0; x < w; x += 2)
                    if (body(x, y)) { if (x < minX) minX = x; if (x > maxX) maxX = x; if (y < minY) minY = y; if (y > maxY) maxY = y; }
            if (maxX < 0) return src;
            int bw = maxX - minX + 1, bh = maxY - minY + 1;
            int side = bh > bw * 1.15 ? (int)(bh * 0.30) : (int)(Math.Min(bw, bh) * 0.52);
            side = Math.Max(24, Math.Min(side, Math.Min(w, h)));
            // the head's middle: the middle of the creature in its top rows
            long sum = 0, n = 0;
            int band = Math.Max(4, side / 2);
            for (int y = minY; y < Math.Min(h, minY + band); y += 2)
                for (int x = minX; x <= maxX; x += 2)
                    if (body(x, y)) { sum += x; n++; }
            int cx = n > 0 ? (int)(sum / n) : (minX + maxX) / 2;
            int left = Math.Max(0, Math.Min(w - side, cx - side / 2));
            int top = Math.Max(0, Math.Min(h - side, minY + side / 24));   // a little below the very top: hair and crests
            var crop = new Bitmap(side, side);
            using (var g = Graphics.FromImage(crop))
                g.DrawImage(src, new Rectangle(0, 0, side, side), new Rectangle(left, top, side, side), GraphicsUnit.Pixel);
            return crop;
        }

        static void Fetch(string key, string url) { Fetch(key, url, false); }

        static void Fetch(string key, string url, bool face)
        {
            Image img = null;
            try
            {
                Directory.CreateDirectory(CacheDir);
                string safeKey = Regex.Replace(key, "[^A-Za-z0-9_.-]", "_");
                string path = Path.Combine(CacheDir, safeKey + ".jpg");
                if (key.StartsWith("item3d_", StringComparison.Ordinal))
                {
                    string stem = url.EndsWith(".jpg", StringComparison.OrdinalIgnoreCase) ? url.Substring(0, url.Length - 4) : url;
                    string[] extensions = { ".jpg", ".png" };
                    path = null;
                    foreach (string ext in extensions)
                    {
                        string cached = Path.Combine(CacheDir, safeKey + ext);
                        if (File.Exists(cached)) { path = cached; break; }
                    }
                    if (path == null)
                        foreach (string ext in extensions)
                        {
                            byte[] data = null;
                            try { data = Get(stem + ext); } catch (Exception) { }
                            if (data == null || data.Length == 0) continue;
                            path = Path.Combine(CacheDir, safeKey + ext);
                            File.WriteAllBytes(path + ".part", data);
                            if (File.Exists(path)) File.Delete(path);
                            File.Move(path + ".part", path);
                            break;
                        }
                }
                else if (!File.Exists(path))
                {
                    byte[] data = Get(url);
                    if (data != null && data.Length > 0)
                    {
                        File.WriteAllBytes(path + ".part", data);
                        if (File.Exists(path)) File.Delete(path);
                        File.Move(path + ".part", path);
                    }
                }
                if (path != null && File.Exists(path))
                    using (var tmp = Image.FromFile(path)) img = new Bitmap(tmp);
                if (face && img != null)
                {
                    Bitmap f = Face((Bitmap)img);
                    if (f != img) { img.Dispose(); img = f; }
                }
            }
            catch (Exception) { }
            Done(key, img, true);
        }

        static void Done(string key, Image img, bool picture)
        {
            Control[] again;
            lock (images)
            {
                if (picture) images[key] = img;
                loading.Remove(key);
                again = waiting.ToArray();
                if (loading.Count == 0) waiting.Clear();
            }
            foreach (Control c in again)
                try { if (!c.IsDisposed && c.IsHandleCreated) c.BeginInvoke(new Action(c.Invalidate)); }
                catch (Exception) { }
        }

        // the screenshot tool: wait until every picture asked for has arrived (or failed)
        public static void WaitForAll(int ms)
        {
            var until = DateTime.Now.AddMilliseconds(ms);
            while (DateTime.Now < until)
            {
                lock (images) if (loading.Count == 0) return;
                Application.DoEvents();
                Thread.Sleep(50);
            }
        }

        // ---- talking to Wowhead
        static byte[] Get(string url)
        {
            try
            {
                if ((int)ServicePointManager.SecurityProtocol != 0) ServicePointManager.SecurityProtocol |= (SecurityProtocolType)3072;
            }
            catch (Exception) { }
            var req = (HttpWebRequest)WebRequest.Create(url);
            req.UserAgent = "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AzerothSurvivorsLauncher";
            req.Timeout = 15000;
            req.ReadWriteTimeout = 15000;
            req.AllowAutoRedirect = true;
            req.Accept = "text/html,application/json,image/*,*/*";
            req.AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate;
            using (var resp = (HttpWebResponse)req.GetResponse())
            using (Stream s = resp.GetResponseStream())
            using (var ms = new MemoryStream())
            {
                s.CopyTo(ms);
                return ms.ToArray();
            }
        }

        static string GetText(string url)
        {
            byte[] b = Get(url);
            return b == null ? "" : Encoding.UTF8.GetString(b);
        }

        // the search's results, one object at a time: { "type":6, "id":116, "name":"Frostbolt", "typeName":"Spell", "icon":"..." }
        static IEnumerable<Dictionary<string, string>> Results(string json)
        {
            foreach (Match m in Regex.Matches(json, @"\{[^{}]*""typeName""[^{}]*\}"))
            {
                var d = new Dictionary<string, string>();
                foreach (Match f in Regex.Matches(m.Value, @"""(\w+)"":(?:""((?:[^""\\]|\\.)*)""|(-?\d+))"))
                    d[f.Groups[1].Value] = f.Groups[2].Success && f.Groups[2].Length > 0 ? Regex.Unescape(f.Groups[2].Value) : f.Groups[3].Value;
                yield return d;
            }
        }

        static string Search(string site, string q)
        {
            return GetText("https://www.wowhead.com" + site + "/search/suggestions-template?q=" + Uri.EscapeDataString(q));
        }

        static string FindSpellIcon(string name)
        {
            foreach (string site in new[] { "/classic", "" })
            {
                foreach (var d in Results(Search(site, name)))
                {
                    string t, n, icon;
                    if (d.TryGetValue("typeName", out t) && t == "Spell" && d.TryGetValue("name", out n) &&
                        Key(n) == Key(name) && d.TryGetValue("icon", out icon) &&
                        Regex.IsMatch(icon, "^[a-z0-9_-]+$"))
                        return icon;
                }
            }
            return "";                                       // asked, and Wowhead has no such spell
        }
    }

    // The creature faces for the AZS Stats addon's own Nemesis tab and death log, the same pictures the STATS page
    // shows. The game cannot fetch anything itself, so the launcher writes them into the installed addon on this PC:
    // AZSStats\Faces\<name>.tga (64 x 64, round), and AZSStatsFaces.lua lists them. Nothing of this is uploaded
    // anywhere; the addon's public zip ships only the empty list. The game reads new faces at the next /reload.
    public static class NpcFaces
    {
        static readonly object gate = new object();
        static bool running;

        // the file name the addon looks for: lower case letters and digits only (as AZSStatsUI.lua's NpcKey)
        public static string Key(string name)
        {
            return Regex.Replace((name ?? "").Trim().ToLowerInvariant(), "[^a-z0-9]", "");
        }

        public static void Sync(string addonDir, IEnumerable<string> names)
        {
            if (string.IsNullOrEmpty(addonDir) || !Directory.Exists(addonDir) || names == null) return;
            var todo = new Dictionary<string, string>();
            foreach (string n in names)
            {
                string k = Key(n);
                if (k.Length > 0 && k.Length <= 60 && !todo.ContainsKey(k)) todo[k] = n.Trim();
            }
            lock (gate) { if (running) return; running = true; }
            ThreadPool.QueueUserWorkItem(delegate
            {
                try { Work(addonDir, todo); }
                catch (Exception) { }
                finally { lock (gate) running = false; }
            });
        }

        static void Work(string addonDir, Dictionary<string, string> todo)
        {
            string dir = Path.Combine(addonDir, "Faces");
            Directory.CreateDirectory(dir);
            foreach (var kv in todo)
            {
                string tga = Path.Combine(dir, kv.Key + ".tga");
                if (File.Exists(tga)) continue;
                try
                {
                    using (Bitmap face = SpellIcons.NpcFace(kv.Value))
                        if (face != null) WriteTga(Round(face, 64), tga);
                }
                catch (Exception) { }
            }
            // the list the addon reads: every face that is there
            var sb = new StringBuilder();
            sb.Append("-- Written by the Azeroth Survivors Launcher: the creature faces in the Faces folder.\r\n");
            sb.Append("AZSSTATS_FACES = {\r\n");
            var keys = new List<string>();
            foreach (string f in Directory.GetFiles(dir, "*.tga"))
            {
                string k = Path.GetFileNameWithoutExtension(f);
                if (Regex.IsMatch(k, "^[a-z0-9]+$")) keys.Add(k);
            }
            keys.Sort(StringComparer.Ordinal);
            foreach (string k in keys) sb.Append("\t[\"").Append(k).Append("\"] = 1,\r\n");
            sb.Append("};\r\n");
            string list = Path.Combine(addonDir, Stats.FacesFile), text = sb.ToString();
            string old = File.Exists(list) ? File.ReadAllText(list, Encoding.UTF8) : null;
            if (old != text) File.WriteAllText(list, text, new UTF8Encoding(false));
        }

        // the face scaled to size x size, inside a circle with a soft edge (outside it is see-through)
        static Bitmap Round(Bitmap src, int size)
        {
            var b = new Bitmap(size, size, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
            using (var g = Graphics.FromImage(b))
            {
                g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
                g.PixelOffsetMode = System.Drawing.Drawing2D.PixelOffsetMode.HighQuality;
                g.DrawImage(src, new Rectangle(0, 0, size, size));
            }
            double r = size / 2.0, edge = 1.5;
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                {
                    double dx = x + 0.5 - r, dy = y + 0.5 - r, d = Math.Sqrt(dx * dx + dy * dy);
                    double a = d <= r - edge ? 1 : d >= r ? 0 : (r - d) / edge;
                    Color c = b.GetPixel(x, y);
                    b.SetPixel(x, y, Color.FromArgb((int)(255 * a), c.R, c.G, c.B));
                }
            return b;
        }

        // an uncompressed 32-bit TGA, as the game reads (rows from the bottom up)
        static void WriteTga(Bitmap b, string path)
        {
            using (b)
            {
                int w = b.Width, h = b.Height;
                var data = new byte[18 + w * h * 4];
                data[2] = 2;                                     // true colour, not compressed
                data[12] = (byte)(w & 255); data[13] = (byte)(w >> 8);
                data[14] = (byte)(h & 255); data[15] = (byte)(h >> 8);
                data[16] = 32;
                data[17] = 8;                                    // 8 alpha bits, origin bottom left
                int i = 18;
                for (int y = h - 1; y >= 0; y--)
                    for (int x = 0; x < w; x++)
                    {
                        Color c = b.GetPixel(x, y);
                        data[i++] = c.B; data[i++] = c.G; data[i++] = c.R; data[i++] = c.A;
                    }
                File.WriteAllBytes(path + ".part", data);
                if (File.Exists(path)) File.Delete(path);
                File.Move(path + ".part", path);
            }
        }
    }
}
