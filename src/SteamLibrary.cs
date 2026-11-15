// Steam Deck / Linux: makes the launcher's "non-Steam game" shortcut look like a real game in the Steam library.
// Runs by itself on every start under Wine / Proton (never on Windows), with no question to the player:
//   - names the shortcut "Azeroth Survivors" and gives it the launcher's icon (shortcuts.vdf)
//   - puts the library artwork in place: cover, wide banner, hero background, logo, icon (config/grid)
//   - adds a couple of game screenshots to the shortcut's screenshots (760/remote/<id>/screenshots)
// Steam reads all of this from the Deck's own files: the launcher reaches them through Wine's Z: drive (the Linux "/").
//
// Steam keeps shortcuts.vdf in memory while it runs and may write its own copy back, so a rename done while Steam is
// open can be undone; the launcher simply does it again on each start, and it sticks from the next Steam restart
// (a Deck reboot). Artwork appears after Steam restarts too. A backup of the first shortcuts.vdf it changes is kept
// beside it (shortcuts.vdf.azs-backup). Artwork the player put there themselves is never replaced: only files this
// launcher wrote (listed in grid/<id>.azs-art) are updated.
//
// The artwork is in assets/steam (cover.png, wide.png, hero.png, logo.png, icon.png, screenshot1.png, screenshot2.png).
// Changing any of it: raise ArtVersion so existing installs pick up the new files.
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.Globalization;
using System.IO;
using System.Text;
using System.Threading;

namespace AzerothLauncher
{
    public static class SteamLibrary
    {
        public const string GameName = "Azeroth Survivors";
        const int ArtVersion = 1;
        static readonly string[] Screenshots = { "screenshot1.png", "screenshot2.png" };

        // on the launcher's start, in the background; never blocks or bothers the player
        public static void RunInBackground()
        {
            if (!Handheld.Wine) return;
            ThreadPool.QueueUserWorkItem(delegate
            {
                try { Run(); }
                catch (Exception e) { Program.LogCrash("Steam library setup", e); }
            });
        }

        static void Run()
        {
            string exe = Path.GetFileName(System.Diagnostics.Process.GetCurrentProcess().MainModule.FileName);
            foreach (string config in SteamConfigFolders())
            {
                string vdf = Path.Combine(config, "shortcuts.vdf");
                if (!File.Exists(vdf)) continue;
                try { Update(config, vdf, exe); }
                catch (Exception e) { Program.LogCrash("Steam library setup (" + config + ")", e); }
            }
        }

        // every Steam account's config folder on this Deck / PC: userdata/<account>/config
        static IEnumerable<string> SteamConfigFolders()
        {
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var homes = new List<string>();
            try { if (Directory.Exists(@"Z:\home")) homes.AddRange(Directory.GetDirectories(@"Z:\home")); } catch (Exception) { }
            if (Directory.Exists(@"Z:\root")) homes.Add(@"Z:\root");
            foreach (string home in homes)
                foreach (string steam in new[] {
                    @".local\share\Steam", @".steam\steam", @".steam\root",
                    @".var\app\com.valvesoftware.Steam\.local\share\Steam" })
                {
                    string userdata = Path.Combine(Path.Combine(home, steam), "userdata");
                    string[] accounts;
                    try { if (!Directory.Exists(userdata)) continue; accounts = Directory.GetDirectories(userdata); }
                    catch (Exception) { continue; }
                    foreach (string account in accounts)
                    {
                        string config = Path.Combine(account, "config");
                        string real;
                        try { real = Path.GetFullPath(config); } catch (Exception) { real = config; }
                        if (Directory.Exists(config) && seen.Add(real)) yield return config;
                    }
                }
        }

        static void Update(string config, string vdfPath, string exe)
        {
            byte[] data = File.ReadAllBytes(vdfPath);
            int at = 0;
            VdfNode root = VdfNode.Read(data, ref at);
            VdfNode list = root.Child("shortcuts");
            if (list == null) return;
            foreach (VdfNode shortcut in list.Children)
            {
                string target = shortcut.Str("Exe") ?? "";
                if (target.IndexOf(exe, StringComparison.OrdinalIgnoreCase) < 0) continue;

                uint id = ShortcutId(shortcut, target);
                string grid = Path.Combine(config, "grid");
                Directory.CreateDirectory(grid);
                string icon = Path.Combine(grid, id.ToString(CultureInfo.InvariantCulture) + "_icon.png");
                WriteArtwork(grid, id);
                WriteScreenshots(Path.GetDirectoryName(config), id);

                bool changed = false;
                if (shortcut.Str("AppName") != GameName) { shortcut.SetStr("AppName", GameName); changed = true; }
                string linuxIcon = Handheld.LinuxPath(icon) ?? icon;
                if (File.Exists(icon) && shortcut.Str("icon") != linuxIcon) { shortcut.SetStr("icon", linuxIcon); changed = true; }
                if (changed)
                {
                    string backup = vdfPath + ".azs-backup";
                    if (!File.Exists(backup)) File.Copy(vdfPath, backup);
                    var output = new MemoryStream();
                    root.Write(output);
                    string tmp = vdfPath + ".azs-new";
                    File.WriteAllBytes(tmp, output.ToArray());
                    File.Copy(tmp, vdfPath, true);
                    File.Delete(tmp);
                }
            }
        }

        // the shortcut's id, as Steam names its artwork: its stored appid, or (older Steam) worked out from exe + name
        static uint ShortcutId(VdfNode shortcut, string exe)
        {
            int? stored = shortcut.Int("appid");
            if (stored.HasValue && stored.Value != 0) return unchecked((uint)stored.Value);
            return Crc32(Encoding.UTF8.GetBytes(exe + (shortcut.Str("AppName") ?? ""))) | 0x80000000;
        }

        static void WriteArtwork(string grid, uint id)
        {
            string n = id.ToString(CultureInfo.InvariantCulture);
            string markerPath = Path.Combine(grid, n + ".azs-art");
            var ours = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            int version = 0;
            if (File.Exists(markerPath))
            {
                string[] lines = File.ReadAllLines(markerPath);
                if (lines.Length > 0) int.TryParse(lines[0], out version);
                for (int i = 1; i < lines.Length; i++) ours.Add(lines[i].Trim());
            }
            var files = new[] {
                new[] { "cover.png", n + "p.png" }, new[] { "wide.png", n + ".png" }, new[] { "hero.png", n + "_hero.png" },
                new[] { "logo.png", n + "_logo.png" }, new[] { "icon.png", n + "_icon.png" } };
            var written = new List<string>();
            foreach (string[] f in files)
            {
                string dest = Path.Combine(grid, f[1]);
                bool mine = ours.Contains(f[1]);
                if (File.Exists(dest) && !mine) continue;               // the player's own artwork stays
                if (!File.Exists(dest) || version < ArtVersion) Extract(f[0], dest);
                written.Add(f[1]);
            }
            var marker = new List<string> { ArtVersion.ToString(CultureInfo.InvariantCulture) };
            marker.AddRange(written);
            File.WriteAllLines(markerPath, marker.ToArray());
        }

        // Steam's screenshot folder for the shortcut: userdata/<account>/760/remote/<id>/screenshots (JPEG + thumbnails).
        // Added once; Steam lists them in the shortcut's screenshots after its next restart.
        static void WriteScreenshots(string account, uint id)
        {
            string dir = Path.Combine(Path.Combine(Path.Combine(Path.Combine(account, "760"), "remote"), id.ToString(CultureInfo.InvariantCulture)), "screenshots");
            string marker = Path.Combine(dir, "azs-screenshots.txt");
            if (File.Exists(marker)) return;
            string thumbs = Path.Combine(dir, "thumbnails");
            Directory.CreateDirectory(thumbs);
            for (int i = 0; i < Screenshots.Length; i++)
            {
                string name = "20260101000000_" + (i + 1).ToString(CultureInfo.InvariantCulture) + ".jpg";
                using (Stream s = Util.ResourceStream("AS.steam." + Screenshots[i]))
                using (var image = Image.FromStream(s))
                {
                    image.Save(Path.Combine(dir, name), ImageFormat.Jpeg);
                    int h = Math.Max(1, image.Height * 200 / image.Width);
                    using (var thumb = new Bitmap(image, new Size(200, h))) thumb.Save(Path.Combine(thumbs, name), ImageFormat.Jpeg);
                }
            }
            File.WriteAllText(marker, ArtVersion.ToString(CultureInfo.InvariantCulture));
        }

        static void Extract(string resource, string dest)
        {
            using (Stream s = Util.ResourceStream("AS.steam." + resource))
            using (var f = File.Create(dest)) s.CopyTo(f);
        }

        static uint Crc32(byte[] data)
        {
            uint crc = 0xFFFFFFFF;
            foreach (byte b in data)
            {
                crc ^= b;
                for (int k = 0; k < 8; k++) crc = (crc & 1) != 0 ? (crc >> 1) ^ 0xEDB88320 : crc >> 1;
            }
            return ~crc;
        }

        // Steam's binary VDF: 0x00 name {children} 0x08 | 0x01 name "string" | 0x02 name int32; names and strings are
        // NUL-terminated UTF-8. Unknown value types are kept as raw bytes so writing back loses nothing.
        sealed class VdfNode
        {
            public byte Type;                 // 0 map, 1 string, 2 int, other: raw
            public string Name;
            public string Text;
            public int Number;
            public byte[] Raw;
            public readonly List<VdfNode> Children = new List<VdfNode>();

            public static VdfNode Read(byte[] d, ref int at)
            {
                var root = new VdfNode { Type = 0, Name = null };
                ReadChildren(root, d, ref at);
                return root;
            }

            static void ReadChildren(VdfNode parent, byte[] d, ref int at)
            {
                while (at < d.Length)
                {
                    byte type = d[at++];
                    if (type == 0x08) return;
                    var node = new VdfNode { Type = type, Name = ReadString(d, ref at) };
                    if (type == 0x00) ReadChildren(node, d, ref at);
                    else if (type == 0x01) node.Text = ReadString(d, ref at);
                    else if (type == 0x02 || type == 0x04 || type == 0x06) { node.Raw = Slice(d, at, 4); node.Number = BitConverter.ToInt32(d, at); at += 4; }
                    else if (type == 0x03) { node.Raw = Slice(d, at, 4); at += 4; }
                    else if (type == 0x07 || type == 0x0A) { node.Raw = Slice(d, at, 8); at += 8; }
                    else throw new InvalidDataException("shortcuts.vdf: unknown value type " + type);
                    parent.Children.Add(node);
                }
            }

            static byte[] Slice(byte[] d, int at, int n) { var b = new byte[n]; Array.Copy(d, at, b, 0, n); return b; }

            static string ReadString(byte[] d, ref int at)
            {
                int start = at;
                while (at < d.Length && d[at] != 0) at++;
                string s = Encoding.UTF8.GetString(d, start, at - start);
                at++;
                return s;
            }

            public VdfNode Child(string name)
            {
                foreach (VdfNode c in Children) if (string.Equals(c.Name, name, StringComparison.OrdinalIgnoreCase)) return c;
                return null;
            }

            public string Str(string name) { VdfNode c = Child(name); return c != null && c.Type == 0x01 ? c.Text : null; }
            public int? Int(string name) { VdfNode c = Child(name); return c != null && c.Type == 0x02 ? (int?)c.Number : null; }

            public void SetStr(string name, string value)
            {
                VdfNode c = Child(name);
                if (c == null) { c = new VdfNode { Type = 0x01, Name = name }; Children.Add(c); }
                c.Type = 0x01; c.Text = value;
            }

            public void Write(Stream o)
            {
                foreach (VdfNode c in Children) c.WriteNode(o);
                o.WriteByte(0x08);
            }

            void WriteNode(Stream o)
            {
                o.WriteByte(Type);
                WriteString(o, Name);
                if (Type == 0x00) Write(o);
                else if (Type == 0x01) WriteString(o, Text ?? "");
                else o.Write(Raw, 0, Raw.Length);
            }

            static void WriteString(Stream o, string s)
            {
                byte[] b = Encoding.UTF8.GetBytes(s ?? "");
                o.Write(b, 0, b.Length);
                o.WriteByte(0);
            }
        }
    }
}
