// Addons: switched off by renaming the folder to "<Name>.disabled" (the game no longer finds it) and on by
// renaming it back. Nothing is deleted, except by UNINSTALL.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text.RegularExpressions;

namespace AzerothLauncher
{
    public static class Addons
    {
        public const string DisabledSuffix = ".disabled";
        static readonly HashSet<string> UnsafeTops = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            { "interface", "addons", "wtf", "data", "saved", "benilla-config", "fonts", "logs" };

        // every addon folder: name -> enabled, sorted by name (the game's own Blizzard_ folders are left out)
        public static List<KeyValuePair<string, bool>> Scan(string addonDir)
        {
            var found = new Dictionary<string, bool>(StringComparer.Ordinal);
            foreach (string d in Directory.GetDirectories(addonDir))
            {
                string name = Path.GetFileName(d);
                if (name.StartsWith("blizzard_", StringComparison.OrdinalIgnoreCase)) continue;
                if (name.EndsWith(DeletedSuffix, StringComparison.OrdinalIgnoreCase)) { Util.DeleteTreeQuiet(d); continue; }   // left from an uninstall
                if (name.EndsWith(DisabledSuffix, StringComparison.Ordinal))
                {
                    string baseName = name.Substring(0, name.Length - DisabledSuffix.Length);
                    if (!found.ContainsKey(baseName)) found[baseName] = false;
                }
                else found[name] = true;                    // an enabled copy wins over a stray .disabled
            }
            var list = new List<KeyValuePair<string, bool>>(found);
            list.Sort(delegate(KeyValuePair<string, bool> a, KeyValuePair<string, bool> b)
            {
                return string.Compare(a.Key.ToLowerInvariant(), b.Key.ToLowerInvariant(), StringComparison.Ordinal);
            });
            return list;
        }

        // ---- addons that can't be on together. Each pair: the preferred one first. Switching one on switches the other
        // off; when both are found on any other way (installed, a profile, TOGGLE ALL, by hand), the preferred one
        // stays and the other is switched off. A folder matches by its name or its .toc Title, letters and digits
        // only, without case ("Survivor UI", "SurvivorUI" and "Survivor_UI" are one).
        public class Conflict
        {
            public string[] Preferred, Other;                // the names that match each side
            public string Why;                               // shown in the status line
        }

        public static readonly Conflict[] Conflicts =
        {
            // Transition rule for older installs: if both the replacement and legacy tracker exist, leave Azeroth Stats
            // enabled and switch off AZS Stats. The legacy folder and its SavedVariables are preserved.
            new Conflict { Preferred = new[] { "AzerothStats" }, Other = new[] { "AZSStats" },
                           Why = "both record your runs and statistics" },
            new Conflict { Preferred = new[] { "SurvivorUI", "SurvivorsUI" }, Other = new[] { "AZSMinorMods" },
                           Why = "both set raid marks on bosses and party members" },
            // The interfaces (Download More > UI) that replace the same parts of the game conflict. Survivor UI and
            // SurvivorUI Classic are distributed and toggled as one bundle; their author provides the in-game chooser.
            new Conflict { Preferred = new[] { "SurvivorUI", "SurvivorsUI" }, Other = new[] { "SurvivorsDragonUi" },
                           Why = "both replace the game's interface" },
            new Conflict { Preferred = new[] { "SurvivorUIClassic" }, Other = new[] { "SurvivorsDragonUi" },
                           Why = "both replace the game's interface" },
            new Conflict { Preferred = new[] { "SurvivorUI", "SurvivorsUI" }, Other = new[] { "KeijinUI" },
                           Why = "both replace the game's interface" },
            new Conflict { Preferred = new[] { "SurvivorUIClassic" }, Other = new[] { "KeijinUI" },
                           Why = "both replace the game's interface" },
            new Conflict { Preferred = new[] { "SurvivorsDragonUi" }, Other = new[] { "KeijinUI" },
                           Why = "both replace the game's interface" },
            new Conflict { Preferred = new[] { "SurvivorUI", "SurvivorsUI" }, Other = new[] { "BordoUI" },
                           Why = "both replace the game's interface" },
            new Conflict { Preferred = new[] { "SurvivorUIClassic" }, Other = new[] { "BordoUI" },
                           Why = "both replace the game's interface" },
            new Conflict { Preferred = new[] { "SurvivorUI", "SurvivorsUI" }, Other = new[] { "BlackwellUI" },
                           Why = "both replace the game's interface" },
            new Conflict { Preferred = new[] { "SurvivorUIClassic" }, Other = new[] { "BlackwellUI" },
                           Why = "both replace the game's interface" },
            new Conflict { Preferred = new[] { "SurvivorsDragonUi" }, Other = new[] { "BordoUI" },
                           Why = "both replace the game's interface" },
            new Conflict { Preferred = new[] { "SurvivorsDragonUi" }, Other = new[] { "BlackwellUI" },
                           Why = "both replace the game's interface" },
            new Conflict { Preferred = new[] { "KeijinUI" }, Other = new[] { "BordoUI" },
                           Why = "both replace the game's interface" },
            new Conflict { Preferred = new[] { "KeijinUI" }, Other = new[] { "BlackwellUI" },
                           Why = "both replace the game's interface" },
            new Conflict { Preferred = new[] { "BordoUI" }, Other = new[] { "BlackwellUI" },
                           Why = "both replace the game's interface" },
            new Conflict { Preferred = new[] { "SurvivorUI", "SurvivorsUI" }, Other = new[] { "AetherHUD" },
                           Why = "both replace the game's interface" },
            new Conflict { Preferred = new[] { "SurvivorUIClassic" }, Other = new[] { "AetherHUD" },
                           Why = "both replace the game's interface" },
            new Conflict { Preferred = new[] { "SurvivorsDragonUi" }, Other = new[] { "AetherHUD" },
                           Why = "both replace the game's interface" },
            new Conflict { Preferred = new[] { "KeijinUI" }, Other = new[] { "AetherHUD" },
                           Why = "both replace the game's interface" },
            new Conflict { Preferred = new[] { "BordoUI" }, Other = new[] { "AetherHUD" },
                           Why = "both replace the game's interface" },
            new Conflict { Preferred = new[] { "BlackwellUI" }, Other = new[] { "AetherHUD" },
                           Why = "both replace the game's interface" },
        };

        static string Key(string s)
        {
            var sb = new System.Text.StringBuilder();
            foreach (char c in s ?? "") if (char.IsLetterOrDigit(c)) sb.Append(char.ToLowerInvariant(c));
            return sb.ToString();
        }

        static string TocTitle(string addonDir, string name)
        {
            foreach (string folder in new[] { name, name + DisabledSuffix })
            {
                string toc = Path.Combine(Path.Combine(addonDir, folder), name + ".toc");
                try
                {
                    if (!File.Exists(toc)) continue;
                    foreach (string line in File.ReadAllLines(toc))
                    {
                        var m = Regex.Match(line, @"^##\s*Title\s*:\s*(.+)$");
                        if (m.Success) return Regex.Replace(m.Groups[1].Value, @"\|c[0-9a-fA-F]{8}|\|r", "");
                    }
                }
                catch (Exception) { }
            }
            return "";
        }

        static bool Matches(string addonDir, string name, string[] names)
        {
            string k = Key(name), t = Key(TocTitle(addonDir, name));
            foreach (string n in names) if (Key(n) == k || (t.Length > 0 && Key(n) == t)) return true;
            return false;
        }

        // the installed addons that conflict with this one (and the rule)
        public static List<KeyValuePair<string, Conflict>> PartnersOf(string addonDir, string name, List<KeyValuePair<string, bool>> installed)
        {
            var list = new List<KeyValuePair<string, Conflict>>();
            foreach (var c in Conflicts)
            {
                string[] other = Matches(addonDir, name, c.Preferred) ? c.Other : Matches(addonDir, name, c.Other) ? c.Preferred : null;
                if (other == null) continue;
                foreach (var a in installed)
                    if (a.Key != name && Matches(addonDir, a.Key, other)) list.Add(new KeyValuePair<string, Conflict>(a.Key, c));
            }
            return list;
        }

        public static bool IsPreferred(string addonDir, string name, Conflict c) { return Matches(addonDir, name, c.Preferred); }

        // switched on by hand: the addons it conflicts with go off; returns them
        public static List<string> SwitchOffPartners(string addonDir, string name)
        {
            var off = new List<string>();
            var installed = Scan(addonDir);
            foreach (var p in PartnersOf(addonDir, name, installed))
                foreach (var a in installed)
                    if (a.Key == p.Key && a.Value) { SetEnabled(addonDir, a.Key, false); off.Add(a.Key); }
            return off;
        }

        // both sides of a pair on: the preferred one stays, the other goes off; returns what went off
        public static List<string> EnforceConflicts(string addonDir)
        {
            var off = new List<string>();
            if (string.IsNullOrEmpty(addonDir) || !Directory.Exists(addonDir)) return off;
            var installed = Scan(addonDir);
            foreach (var a in installed)
            {
                if (!a.Value) continue;
                foreach (var p in PartnersOf(addonDir, a.Key, installed))
                {
                    if (!IsPreferred(addonDir, a.Key, p.Value)) continue;
                    foreach (var b in installed)
                        if (b.Key == p.Key && b.Value && !off.Contains(b.Key))
                            try { SetEnabled(addonDir, b.Key, false); off.Add(b.Key); }
                            catch (Exception) { }
                }
            }
            return off;
        }

        // ---- Cloud Save syncs only the addons' settings. The Download More addons that have settings here (a file in
        // the settings folder named like one of its folders: "<Folder>.lua") but none of their folders in the AddOns
        // folder, enabled or switched off: the launcher installs them.
        public static List<AddonEntry> MissingForSettings(string addonDir, string savedDir, List<AddonEntry> catalog)
        {
            var missing = new List<AddonEntry>();
            if (string.IsNullOrEmpty(addonDir) || string.IsNullOrEmpty(savedDir) || !Directory.Exists(savedDir)) return missing;
            var stems = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var stack = new Stack<string>();
            stack.Push(savedDir);
            while (stack.Count > 0)
            {
                string cur = stack.Pop();
                try
                {
                    foreach (string f in Directory.GetFiles(cur))
                    {
                        string n = Path.GetFileName(f);
                        if (n.EndsWith(".lua", StringComparison.OrdinalIgnoreCase) || n.EndsWith(".lua.bak", StringComparison.OrdinalIgnoreCase))
                            stems.Add(n.Split('.')[0]);
                    }
                    foreach (string d in Directory.GetDirectories(cur)) if (!Util.IsLink(d)) stack.Push(d);
                }
                catch (Exception) { }
            }
            var present = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (Directory.Exists(addonDir)) foreach (var a in Scan(addonDir)) present.Add(a.Key);
            foreach (var e in catalog)
            {
                if (e.Folders.Count == 0 || File.Exists(e.Url)) continue;   // no folders known, or a zip kept on this PC
                bool has = false, settings = false;
                foreach (string f in e.Folders)
                {
                    if (present.Contains(f)) has = true;
                    if (stems.Contains(f)) settings = true;
                }
                if (settings && !has) missing.Add(e);
            }
            return missing;
        }

        // ---- an addon that comes as several folders (Survivor UI with Survivor Plates, BugSack with !BugGrabber): one of
        // them is the addon's own (its name), the others come with it. They are shown as one and switched together.
        static string Squash(string s)
        {
            var b = new System.Text.StringBuilder();
            foreach (char c in (s ?? "").ToLowerInvariant()) if (char.IsLetterOrDigit(c)) b.Append(c);
            return b.ToString();
        }

        // the Download More entry a folder belongs to, or null
        public static AddonEntry EntryOf(string folder)
        {
            try
            {
                foreach (var e in Data.Addons)
                    foreach (string f in e.Folders) if (string.Equals(f, folder, StringComparison.OrdinalIgnoreCase)) return e;
            }
            catch (Exception) { }
            return null;
        }

        // the entry's own folder: the one named like the addon (not a "!" library)
        public static string MainFolder(AddonEntry e)
        {
            if (e == null || e.Folders.Count == 0) return null;
            string n = Squash(Regex.Replace(e.Name, @"\s+By\s+.*$", "", RegexOptions.IgnoreCase));
            foreach (string f in e.Folders) if (!f.StartsWith("!") && (n.StartsWith(Squash(f)) || Squash(f).StartsWith(n))) return f;
            foreach (string f in e.Folders) if (!f.StartsWith("!")) return f;
            return e.Folders[0];
        }

        // the folders that come with this one (empty for a single-folder addon, or for a folder that is a companion)
        public static List<string> CompanionsOf(string folder)
        {
            var l = new List<string>();
            AddonEntry e = EntryOf(folder);
            if (e == null || e.Folders.Count < 2) return l;
            string main = MainFolder(e);
            if (!string.Equals(main, folder, StringComparison.OrdinalIgnoreCase)) folder = main;
            foreach (string f in e.Folders) if (!string.Equals(f, folder, StringComparison.OrdinalIgnoreCase)) l.Add(f);
            return l;
        }

        // a folder that only comes with another one, when that one is installed: not listed on its own
        public static bool IsCompanion(string addonDir, string folder)
        {
            AddonEntry e = EntryOf(folder);
            if (e == null || e.Folders.Count < 2) return false;
            string main = MainFolder(e);
            if (string.Equals(main, folder, StringComparison.OrdinalIgnoreCase)) return false;
            return Directory.Exists(Path.Combine(addonDir, main)) || Directory.Exists(Path.Combine(addonDir, main + DisabledSuffix));
        }

        // "Survivor UI By morderous" for a Download More addon; else the addon's title, and its author from its .toc
        public static string DisplayName(string addonDir, string folder)
        {
            AddonEntry e = EntryOf(folder);
            if (e != null) return e.Name;
            string title = TocTitle(addonDir, folder), author = TocField(addonDir, folder, "Author");
            string n = string.IsNullOrEmpty(title) ? folder : title;
            return author.Length > 0 && n.IndexOf(" By ", StringComparison.OrdinalIgnoreCase) < 0 ? n + " By " + author : n;
        }

        static string TocField(string addonDir, string name, string field)
        {
            foreach (string folder in new[] { name, name + DisabledSuffix })
            {
                string toc = Path.Combine(Path.Combine(addonDir, folder), name + ".toc");
                try
                {
                    if (!File.Exists(toc)) continue;
                    foreach (string line in File.ReadAllLines(toc))
                    {
                        var m = Regex.Match(line, @"^##\s*" + field + @"\s*:\s*(.+)$");
                        if (m.Success) return Regex.Replace(m.Groups[1].Value, @"\|c[0-9a-fA-F]{8}|\|r", "").Trim();
                    }
                }
                catch (Exception) { }
            }
            return "";
        }

        // switches an addon on or off, with the folders that come with it
        public static void SetEnabled(string addonDir, string name, bool enabled)
        {
            AddonEntry entry = EntryOf(name);
            if (entry != null && entry.Folders.Count > 1)
            {
                SetBundleEnabled(addonDir, entry, enabled);
                return;
            }
            SetEnabledOne(addonDir, name, enabled);
            foreach (string c in CompanionsOf(name))
                try { SetEnabledOne(addonDir, c, enabled); } catch (Exception) { }   // a companion not installed: nothing to do
        }

        static void SetBundleEnabled(string addonDir, AddonEntry entry, bool enabled)
        {
            var installed = new List<string>();
            foreach (string folder in entry.Folders)
            {
                string on = Path.Combine(addonDir, folder);
                string off = on + DisabledSuffix;
                bool isOn = Directory.Exists(on);
                bool isOff = Directory.Exists(off);
                if (isOn && isOff) throw new IOException(folder + " exists in both enabled and disabled forms");
                if (isOn || isOff) installed.Add(folder);   // older installs may predate one of the bundled components
            }
            if (installed.Count == 0) throw new IOException("No installed folders were found for " + entry.Name);

            var changed = new List<string>();
            foreach (string folder in installed)
            {
                string on = Path.Combine(addonDir, folder);
                string off = on + DisabledSuffix;
                bool isOn = Directory.Exists(on);
                if (isOn == enabled) continue;
                try
                {
                    if (enabled) Directory.Move(off, on);
                    else Directory.Move(on, off);
                    changed.Add(folder);
                }
                catch
                {
                    for (int i = changed.Count - 1; i >= 0; i--)
                    {
                        string movedOn = Path.Combine(addonDir, changed[i]);
                        string movedOff = movedOn + DisabledSuffix;
                        try
                        {
                            if (enabled && Directory.Exists(movedOn)) Directory.Move(movedOn, movedOff);
                            else if (!enabled && Directory.Exists(movedOff)) Directory.Move(movedOff, movedOn);
                        }
                        catch (Exception) { }
                    }
                    throw;
                }
            }
        }

        static void SetEnabledOne(string addonDir, string name, bool enabled)
        {
            string on = Path.Combine(addonDir, name), off = on + DisabledSuffix;
            if (enabled)
            {
                if (Directory.Exists(on)) return;
                if (!Directory.Exists(off)) throw new IOException(name + " was not found");
                Directory.Move(off, on);
            }
            else
            {
                if (!Directory.Exists(on))
                {
                    if (Directory.Exists(off)) return;
                    throw new IOException(name + " was not found");
                }
                if (Directory.Exists(off)) throw new IOException(name + DisabledSuffix + " already exists next to it");
                Directory.Move(on, off);
            }
        }

        // Everything in the addon settings folder that belongs to addon `folder`: its saved-variable files
        // (Name.lua, Name.lua.bak, ...) in any account or character sub-folder, and any folder named after it.
        static List<string> DataMatches(string savedDir, string folder)
        {
            var found = new List<string>();
            string key = folder.ToLowerInvariant();
            if (string.IsNullOrEmpty(savedDir) || !Directory.Exists(savedDir)) return found;
            var stack = new Stack<string>();
            stack.Push(savedDir);
            while (stack.Count > 0)
            {
                string cur = stack.Pop();
                foreach (string d in Directory.GetDirectories(cur))
                {
                    string low = Path.GetFileName(d).ToLowerInvariant();
                    if (low == key || low == key + DisabledSuffix) found.Add(d);     // its contents go with it
                    else if (!Util.IsLink(d)) stack.Push(d);
                }
                foreach (string f in Directory.GetFiles(cur))
                    if (Path.GetFileName(f).ToLowerInvariant().Split('.')[0] == key) found.Add(f);
            }
            return found;
        }

        // a folder that could not be deleted is renamed to <name>.deleted: the game does not load it (its .toc no
        // longer matches its folder), and the next look at the AddOns folder deletes it
        public const string DeletedSuffix = ".deleted";

        static void Drop(string path)
        {
            if (Directory.Exists(path) && !Util.IsLink(path)) Util.DeleteTree(path);
            else if (Directory.Exists(path)) Directory.Delete(path);
            else
            {
                File.SetAttributes(path, FileAttributes.Normal);
                File.Delete(path);
            }
        }

        // Removes everything an addon put on the computer: each folder its .zip unpacked into addonDir, its
        // ".disabled" copy, and the settings it saved (in savedDir). Returns the names that were removed.
        public static List<string> Uninstall(string addonDir, List<string> names, string savedDir)
        {
            var removed = new List<string>();
            string root = Path.GetFullPath(addonDir).TrimEnd('\\');
            string savedRoot = string.IsNullOrEmpty(savedDir) ? null : Path.GetFullPath(savedDir).TrimEnd('\\');
            foreach (string name in names)
            {
                if (string.IsNullOrEmpty(name) || name != Path.GetFileName(name) || name == "." || name == ".." ||
                    name.StartsWith("blizzard_", StringComparison.OrdinalIgnoreCase) || UnsafeTops.Contains(name))
                    continue;
                foreach (string item in new[] { name, name + DisabledSuffix })
                {
                    string path = Path.Combine(root, item);
                    if ((Directory.Exists(path) || File.Exists(path)) &&
                        string.Equals(Path.GetDirectoryName(Path.GetFullPath(path)), root, StringComparison.OrdinalIgnoreCase))
                    {
                        try { Drop(path); }
                        catch (Exception)
                        {
                            if (!Directory.Exists(path)) throw;
                            string away = path + DeletedSuffix;
                            if (Directory.Exists(away)) Util.DeleteTreeQuiet(away);
                            Directory.Move(path, away);           // out of the game's reach now; deleted later
                            Util.DeleteTreeQuiet(away);
                        }
                        if (!removed.Contains(name)) removed.Add(name);
                    }
                }
                string baseName = name.EndsWith(DisabledSuffix) ? name.Substring(0, name.Length - DisabledSuffix.Length) : name;
                string ext = Path.GetExtension(baseName).ToLowerInvariant();
                if (ext == ".lua" || ext == ".xml" || ext == ".toc") baseName = Path.GetFileNameWithoutExtension(baseName);
                if (savedRoot != null)
                    foreach (string path in DataMatches(savedRoot, baseName))
                    {
                        string real = Path.GetFullPath(path);
                        if (real.StartsWith(savedRoot + "\\", StringComparison.OrdinalIgnoreCase) &&
                            (Directory.Exists(path) || File.Exists(path)))
                        {
                            try { Drop(path); } catch (Exception) { continue; }   // its saved settings: never worth failing over
                            if (!removed.Contains(name)) removed.Add(name);
                        }
                    }
            }
            return removed;
        }

        public static Dictionary<string, object> MakeProfile(List<KeyValuePair<string, bool>> addons)
        {
            var on = new List<object>();
            var off = new List<object>();
            foreach (var a in addons) (a.Value ? on : off).Add(a.Key);
            var p = new Dictionary<string, object>();
            p["enabled"] = on;
            p["disabled"] = off;
            p["saved"] = DateTime.Now.ToString("MM-dd HH:mm", CultureInfo.InvariantCulture);
            return p;
        }

        static HashSet<string> Names(object list)
        {
            var r = new HashSet<string>(StringComparer.Ordinal);
            var l = Json.AsList(list);
            if (l != null) foreach (object o in l) r.Add(Convert.ToString(o));
            return r;
        }

        public static int ProfileCount(Dictionary<string, object> profile, string key)
        {
            var l = Json.AsList(Json.Get(profile, key));
            return l == null ? 0 : l.Count;
        }

        // Applies a saved profile. Addons the profile does not know (installed later) are left alone.
        public static int ApplyProfile(string addonDir, Dictionary<string, object> profile, List<string> errors, List<string> unknown)
        {
            var wantOn = Names(Json.Get(profile, "enabled"));
            var wantOff = Names(Json.Get(profile, "disabled"));
            int changed = 0;
            foreach (var a in Scan(addonDir))
            {
                bool target;
                if (wantOn.Contains(a.Key) && !a.Value) target = true;
                else if (wantOff.Contains(a.Key) && a.Value) target = false;
                else
                {
                    if (!wantOn.Contains(a.Key) && !wantOff.Contains(a.Key)) unknown.Add(a.Key);
                    continue;
                }
                try
                {
                    SetEnabled(addonDir, a.Key, target);
                    changed++;
                }
                catch (Exception e) { errors.Add(a.Key + ": " + e.Message); }
            }
            return changed;
        }

        // The addon folders that exist for a Download More entry, enabled or switched off: what the launcher
        // recorded when it installed it, else a folder named like the entry (ignoring case, spaces and punctuation)
        // plus its "Name_..." module folders.
        public static List<string> FoldersOf(string addonDir, string name, List<string> recorded)
        {
            var result = new List<string>();
            List<KeyValuePair<string, bool>> present;
            try { present = Scan(addonDir); }
            catch (Exception) { return result; }
            var low = new Dictionary<string, string>();
            foreach (var p in present) low[p.Key.ToLowerInvariant()] = p.Key;
            if (recorded != null && recorded.Count > 0)
            {
                foreach (string w in recorded)
                {
                    string real;
                    if (low.TryGetValue(w.ToLowerInvariant(), out real)) result.Add(real);
                }
                return result;
            }
            string key = Norm(name);
            foreach (var p in present)
                if (key.Length > 0 && (Norm(p.Key) == key || p.Key.ToLowerInvariant().StartsWith(name.ToLowerInvariant() + "_")))
                    result.Add(p.Key);
            return result;
        }

        static string Norm(string t) { return Regex.Replace(t.ToLowerInvariant(), "[^a-z0-9]", ""); }
    }
}
