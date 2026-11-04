// The STATS page reads Maidenhead's Azeroth Stats SavedVariables (AzerothStats.lua) without changing them.
// Legacy AZS Stats / AS Time Tracker files remain read-only fallback sources. Installing the replacement never
// migrates, merges, or deletes those older records.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;

namespace AzerothLauncher
{
    // A small reader for the Lua that the game writes for saved variables: "Name = value" lines whose values
    // are tables, strings, numbers, true / false and nil. Tables become dictionaries; keys are strings (numbers
    // as "1", "2" ...), and entries without a key are numbered 1, 2 ... as in Lua.
    public class LuaReader
    {
        readonly string s;
        int pos;

        LuaReader(string text) { s = text ?? ""; }

        public static Dictionary<string, object> ParseFile(string text)
        {
            var r = new LuaReader(text);
            var top = new Dictionary<string, object>();
            while (true)
            {
                r.Ws();
                if (r.pos >= r.s.Length) break;
                string name = r.Name();
                if (name == null) throw new FormatException("Unexpected text at " + r.pos + ".");
                r.Ws();
                if (r.Peek() != '=') throw new FormatException("Expected = after " + name + ".");
                r.pos++;
                top[name] = r.Value();
                r.Ws();
                if (r.Peek() == ';') r.pos++;
            }
            return top;
        }

        char Peek() { return pos < s.Length ? s[pos] : '\0'; }

        void Ws()
        {
            while (pos < s.Length)
            {
                char c = s[pos];
                if (char.IsWhiteSpace(c) || c == '﻿') { pos++; continue; }
                if (c == '-' && pos + 1 < s.Length && s[pos + 1] == '-')
                {
                    if (pos + 3 < s.Length && s[pos + 2] == '[' && s[pos + 3] == '[')      // --[[ block ]]
                    {
                        int end = s.IndexOf("]]", pos + 4, StringComparison.Ordinal);
                        pos = end < 0 ? s.Length : end + 2;
                        continue;
                    }
                    while (pos < s.Length && s[pos] != '\n') pos++;                     // -- to the end of the line
                    continue;
                }
                break;
            }
        }

        string Name()
        {
            int start = pos;
            while (pos < s.Length && (char.IsLetterOrDigit(s[pos]) || s[pos] == '_')) pos++;
            if (pos == start || char.IsDigit(s[start])) { pos = start; return null; }
            return s.Substring(start, pos - start);
        }

        object Value()
        {
            Ws();
            char c = Peek();
            if (c == '{') return Table();
            if (c == '"' || c == '\'') return Str();
            if (c == '[' && pos + 1 < s.Length && (s[pos + 1] == '[' || s[pos + 1] == '=')) return LongStr();
            if (c == '-' || c == '.' || char.IsDigit(c)) return Number();
            string word = Name();
            if (word == "true") return true;
            if (word == "false") return false;
            if (word == "nil") return null;
            throw new FormatException("Unexpected value at " + pos + ".");
        }

        object Number()
        {
            int start = pos;
            if (Peek() == '-') pos++;
            if (pos + 1 < s.Length && s[pos] == '0' && (s[pos + 1] == 'x' || s[pos + 1] == 'X'))
            {
                pos += 2;
                int hs = pos;
                while (pos < s.Length && Uri.IsHexDigit(s[pos])) pos++;
                double hv = (double)long.Parse(s.Substring(hs, pos - hs), NumberStyles.HexNumber, CultureInfo.InvariantCulture);
                return s[start] == '-' ? -hv : hv;
            }
            while (pos < s.Length && (char.IsDigit(s[pos]) || s[pos] == '.' || s[pos] == 'e' || s[pos] == 'E' ||
                                      ((s[pos] == '+' || s[pos] == '-') && (s[pos - 1] == 'e' || s[pos - 1] == 'E')))) pos++;
            string t = s.Substring(start, pos - start);
            if (t == "-" ) throw new FormatException("A lone - at " + start + ".");
            string low = s.Substring(pos, Math.Min(4, s.Length - pos)).ToLowerInvariant();     // 1.#INF, -nan, inf
            if (low.StartsWith("#") || low.StartsWith("inf") || low.StartsWith("nan"))
            {
                while (pos < s.Length && (char.IsLetterOrDigit(s[pos]) || s[pos] == '#')) pos++;
                return 0.0;
            }
            double v;
            if (!double.TryParse(t, NumberStyles.Float, CultureInfo.InvariantCulture, out v)) v = 0;
            return v;
        }

        string Str()
        {
            char q = s[pos++];
            var sb = new StringBuilder();
            while (pos < s.Length && s[pos] != q)
            {
                char c = s[pos++];
                if (c != '\\' || pos >= s.Length) { sb.Append(c); continue; }
                char e = s[pos++];
                switch (e)
                {
                    case 'n': sb.Append('\n'); break;
                    case 't': sb.Append('\t'); break;
                    case 'r': sb.Append('\r'); break;
                    case 'a': sb.Append('\a'); break;
                    case 'b': sb.Append('\b'); break;
                    case 'f': sb.Append('\f'); break;
                    case 'v': sb.Append('\v'); break;
                    case '\n': sb.Append('\n'); break;
                    default:
                        if (char.IsDigit(e))                         // \ddd: a byte
                        {
                            int n = e - '0', k = 1;
                            while (k < 3 && pos < s.Length && char.IsDigit(s[pos])) { n = n * 10 + (s[pos++] - '0'); k++; }
                            sb.Append((char)Math.Min(n, 255));
                        }
                        else sb.Append(e);
                        break;
                }
            }
            pos++;                                               // the closing quote
            return Bytes(sb.ToString());
        }

        // the file is read as UTF-8; text written with \ddd escapes is turned back from bytes into UTF-8 too
        static string Bytes(string t)
        {
            bool high = false;
            foreach (char c in t) if (c > 127 && c < 256) { high = true; break; }
            if (!high) return t;
            foreach (char c in t) if (c > 255) return t;
            try
            {
                var b = new byte[t.Length];
                for (int i = 0; i < t.Length; i++) b[i] = (byte)t[i];
                return new UTF8Encoding(false, true).GetString(b);
            }
            catch (Exception) { return t; }
        }

        string LongStr()
        {
            int start = pos + 1, eq = 0;
            while (start < s.Length && s[start] == '=') { eq++; start++; }
            string close = "]" + new string('=', eq) + "]";
            start++;
            int end = s.IndexOf(close, start, StringComparison.Ordinal);
            if (end < 0) end = s.Length;
            pos = Math.Min(s.Length, end + close.Length);
            string t = s.Substring(start, end - start);
            return t.StartsWith("\n") ? t.Substring(1) : t;
        }

        Dictionary<string, object> Table()
        {
            pos++;                                               // {
            var t = new Dictionary<string, object>();
            int next = 1;
            while (true)
            {
                Ws();
                if (pos >= s.Length) throw new FormatException("A table is not closed.");
                if (Peek() == '}') { pos++; return t; }
                string key = null;
                if (Peek() == '[' && pos + 1 < s.Length && s[pos + 1] != '[' && s[pos + 1] != '=')
                {
                    pos++;
                    object k = Value();
                    Ws();
                    if (Peek() != ']') throw new FormatException("Expected ] at " + pos + ".");
                    pos++;
                    Ws();
                    if (Peek() != '=') throw new FormatException("Expected = at " + pos + ".");
                    pos++;
                    key = KeyText(k);
                }
                else
                {
                    int save = pos;
                    string name = Name();
                    Ws();
                    if (name != null && name != "true" && name != "false" && name != "nil" && Peek() == '=' &&
                        !(pos + 1 < s.Length && s[pos + 1] == '='))
                    {
                        pos++;
                        key = name;
                    }
                    else pos = save;
                }
                object v = Value();
                if (key == null) key = (next++).ToString(CultureInfo.InvariantCulture);
                if (v != null) t[key] = v;
                Ws();
                if (Peek() == ',' || Peek() == ';') pos++;
            }
        }

        static string KeyText(object k)
        {
            if (k is double)
            {
                double d = (double)k;
                if (d == Math.Floor(d) && Math.Abs(d) < 1e15) return ((long)d).ToString(CultureInfo.InvariantCulture);
                return d.ToString("R", CultureInfo.InvariantCulture);
            }
            if (k is bool) return (bool)k ? "true" : "false";
            return Convert.ToString(k, CultureInfo.InvariantCulture) ?? "";
        }

        // ---- reading the parsed values
        public static Dictionary<string, object> Tab(Dictionary<string, object> d, string key)
        {
            object v;
            return d != null && d.TryGetValue(key, out v) ? v as Dictionary<string, object> : null;
        }

        public static double Num(Dictionary<string, object> d, string key)
        {
            object v;
            if (d == null || !d.TryGetValue(key, out v) || v == null) return 0;
            if (v is double) return (double)v;
            double r;
            return double.TryParse(Convert.ToString(v, CultureInfo.InvariantCulture), NumberStyles.Float, CultureInfo.InvariantCulture, out r) ? r : 0;
        }

        public static string Text(Dictionary<string, object> d, string key)
        {
            object v;
            if (d == null || !d.TryGetValue(key, out v) || v == null) return "";
            if (v is double) return KeyText(v);
            return v as string ?? "";
        }

        // the entries 1, 2, 3 ... of a list table, in order
        public static List<Dictionary<string, object>> List(Dictionary<string, object> d)
        {
            var list = new List<Dictionary<string, object>>();
            if (d == null) return list;
            for (int i = 1; ; i++)
            {
                object v;
                if (!d.TryGetValue(i.ToString(CultureInfo.InvariantCulture), out v)) break;
                var t = v as Dictionary<string, object>;
                if (t != null) list.Add(t);
            }
            return list;
        }
    }

    // Session play time, if supplied by a legacy file. Azeroth Stats reports run duration instead.
    public class PlayStats
    {
        public long Seconds, Sessions, Longest, LastSeconds;
        public string LastDate = "";
    }

    // one class's latest recorded hero, gear and numbers
    public class ArmoryChar
    {
        public string ClassKey = "", Class = "", Hero = "", Race = "", Date = "";
        public double Level;
        public readonly List<ArmoryItem> Gear = new List<ArmoryItem>();
        public readonly Dictionary<string, double> Numbers = new Dictionary<string, double>();
    }

    public class ArmoryItem
    {
        public string Slot = "", Name = "", Color = "ffffff", Icon = "";
        public double Id;
    }

    // one personal best
    public class StatRecord
    {
        public string Key = "", Hero = "", Class = "", Race = "", Zone = "", Date = "";
        public double Value;
    }

    // an ability (spell) as taken in runs: how often, with what result, and the damage it dealt
    public class AbilityStat
    {
        public string Key = "", Name = "";
        public double Picks, Wins, Damage, BestRank, Ranks;
    }

    // totals: of every run together, of one class, race or zone
    public class StatBlock
    {
        public string Key = "", Name = "";
        public double ObservedDeaths;
        public bool CoverageAware;
        public readonly HashSet<string> AvailableMetrics = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        public double Runs, Victories, Defeats, Left, Time, Kills, Elites, Guardians, Bosses, Damage, Healing, Taken, Gold, Chests,
                      Magnets, Levels, BestTime, BestLevel, Legendary, Saves;   // legendaries collected, level-up saves
        public readonly Dictionary<string, StatRecord> Best = new Dictionary<string, StatRecord>();
        public readonly List<AbilityStat> Abilities = new List<AbilityStat>();     // the most picked first
        public readonly List<NemesisStat> Nemesis = new List<NemesisStat>();       // the most kills first
    }

    // who killed you: how often, the damage of their killing blows, when last
    public class NemesisStat
    {
        public string Name = "", Last = "";
        public double Kills, Damage, Confirmed, Probable;
    }

    // one death in the death log
    public class DeathStat
    {
        public string Date = "", Killer = "", Spell = "", ClassKey = "", Class = "", Race = "", Zone = "", LastHits = "", TopSources = "";
        public double Amount, Time, Level;
    }

    // a friend you played co-op runs with
    public class FriendStat
    {
        public string Name = "", ClassKey = "", Class = "", First = "", Last = "", FirstWin = "";
        public double Runs, Wins, Time, Kills, Bosses, Damage, Dps, BestTime, BestLevel, MostBosses, FastestWin;
    }

    // co-op runs: totals, records together and the friends (most runs first)
    public class CoopStat
    {
        public double Runs, Wins, Time, Kills, Bosses, Solo, SoloWins, Deaths;     // Deaths: times you went down
        public readonly List<NemesisStat> DeathsBy = new List<NemesisStat>();      // what put you down, the most first
        public readonly Dictionary<string, StatRecord> Best = new Dictionary<string, StatRecord>();
        public readonly List<FriendStat> Friends = new List<FriendStat>();
    }

    // the runs of one month, and how many of them each class played (class key -> runs)
    public class MonthStat
    {
        public string Month = "";
        public double Runs;
        public readonly Dictionary<string, double> Classes = new Dictionary<string, double>();
    }

    public class StatRun
    {
        public string Result = "", Kind = "", Date = "", Hero = "", ClassKey = "", Class = "", Race = "", Zone = "", Party = "", Killer = "";
        public string Affixes = "";                          // the run's Heroic affixes as the game names them ("sanguine,explosive"), since 0.124.0
        public double Time, Level, Kills, Damage, Healing, Gold, Bosses, Dps; // Dps: from the game's damage meter
        public double Elites, Guardians, Taken, Chests, Magnets, Legendary, Saves;
        public readonly Dictionary<string, double> Highs = new Dictionary<string, double>();   // the run's highlights (crit, hit, heal, burst1, burst5)
        public readonly List<AbilityStat> Abilities = new List<AbilityStat>();     // Picks = its rank here
        // Fields retained from Azeroth Stats. They are observations from its bounded local run journal,
        // not fields inferred from the game's older aggregate format.
        public string Id = "", Status = "", Phase = "", ZoneName = "";
        public bool Partial, HeroicKnown, Heroic, AzerothStatsSource;
        public readonly Dictionary<string, double> ObservedValues = new Dictionary<string, double>();
        public readonly List<KeyValuePair<string, string>> Observations = new List<KeyValuePair<string, string>>();
        public readonly List<AbilityStat> ObservedSpells = new List<AbilityStat>();
        public readonly List<KeyValuePair<string, double>> DamageSources = new List<KeyValuePair<string, double>>();
        public readonly List<KeyValuePair<string, double>> HealingSources = new List<KeyValuePair<string, double>>();
        public readonly List<KeyValuePair<string, double>> TakenSources = new List<KeyValuePair<string, double>>();
        public readonly List<FriendStat> PartySnapshot = new List<FriendStat>();
    }

    // parsed data from the active or retained legacy addon file
    public class RunStats
    {
        public string FilePath = "", First = "", Last = "", Source = "AZS Stats";
        public DateTime Written;
        public int Version;
        public StatBlock Total = new StatBlock();
        public readonly List<StatBlock> Classes = new List<StatBlock>(), Races = new List<StatBlock>(), Zones = new List<StatBlock>();
        public readonly List<AbilityStat> Abilities = new List<AbilityStat>();
        public readonly List<MonthStat> Months = new List<MonthStat>();          // the oldest first
        public readonly List<StatRun> History = new List<StatRun>();             // the newest first
        public PlayStats Play = new PlayStats();                                   // time the game ran (counted in game)
        public readonly List<DeathStat> Deaths = new List<DeathStat>();          // the newest first
        public readonly List<NemesisStat> Nemesis = new List<NemesisStat>();
        public CoopStat Coop = new CoopStat();
        public readonly List<StatRun> Builds = new List<StatRun>();               // best builds kept by the addon
        public readonly Dictionary<string, double> BossKills = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);   // bosses you beat
        public readonly List<ArmoryChar> Armory = new List<ArmoryChar>();         // each class's hero, the newest first

        public StatBlock Class(string key)
        {
            foreach (var c in Classes) if (c.Key == key) return c;
            return null;
        }
    }

    public static class Stats
    {
        public const string AddonFolder = "AzerothStats", DbName = "AzerothStatsDB";
        public const string FileName = AddonFolder + ".lua";
        const string LegacyAddonFolder = "AZSStats", LegacyDbName = "AZSStatsDB", LegacyFileName = "AZSStats.lua";
        public const string FacesFile = LegacyAddonFolder + "Faces.lua"; // legacy-only icon cache name
        // Retired tracker format. Its file is a legacy read-only fallback; no other addon's data is touched.
        static readonly string[][] OldNames = { new[] { "ASTimeTracker", "ASTimeTrackerDB" } };

        // Prefer the replacement addon's file. Legacy data remains readable when the replacement has not
        // produced SavedVariables yet; no files are moved, merged or deleted by reading them.
        public static string FindFile(string savedDir)
        {
            string f = FindNamed(savedDir, FileName);
            if (f == null) f = FindNamed(savedDir, LegacyFileName);
            foreach (var o in OldNames) if (f == null) f = FindNamed(savedDir, o[0] + ".lua");
            return f;
        }

        // the newest file with this name anywhere in the folder (account or character sub-folders too)
        static string FindNamed(string savedDir, string fileName)
        {
            if (string.IsNullOrEmpty(savedDir) || !Directory.Exists(savedDir)) return null;
            string best = null;
            DateTime when = DateTime.MinValue;
            var stack = new Stack<KeyValuePair<string, int>>();
            stack.Push(new KeyValuePair<string, int>(savedDir, 0));
            while (stack.Count > 0)
            {
                var cur = stack.Pop();
                try
                {
                    foreach (string f in Directory.GetFiles(cur.Key))
                        if (string.Equals(Path.GetFileName(f), fileName, StringComparison.OrdinalIgnoreCase))
                        {
                            DateTime t = File.GetLastWriteTimeUtc(f);
                            if (best == null || t > when) { best = f; when = t; }
                        }
                    if (cur.Value < 6)
                        foreach (string d in Directory.GetDirectories(cur.Key))
                            if (!Util.IsLink(d)) stack.Push(new KeyValuePair<string, int>(d, cur.Value + 1));
                }
                catch (Exception) { }
            }
            return best;
        }

        // null when the addon has not saved anything yet; throws when the file cannot be read
        public static RunStats Load(string savedDir)
        {
            string path = FindFile(savedDir);
            if (path == null) return null;
            string text;
            using (var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
            using (var r = new StreamReader(fs, Encoding.UTF8)) text = r.ReadToEnd();
            var vars = LuaReader.ParseFile(text);
            var db = LuaReader.Tab(vars, DbName);
            bool replacement = db != null;
            if (db == null) db = LuaReader.Tab(vars, LegacyDbName);
            foreach (var o in OldNames) if (db == null) db = LuaReader.Tab(vars, o[1]);
            if (db == null) return null;
            if (replacement) return LoadAzeroth(path, db);
            var st = new RunStats();
            st.Source = "AZS Stats (legacy)";
            st.FilePath = path;
            st.Written = File.GetLastWriteTime(path);
            st.Version = (int)LuaReader.Num(db, "version");
            st.First = LuaReader.Text(db, "first");
            st.Last = LuaReader.Text(db, "last");
            st.Total = Block("", LuaReader.Tab(db, "totals"));
            Bests(LuaReader.Tab(db, "best"), st.Total.Best);
            StatRecord rec;                                  // version 1 kept these only as records
            if (st.Total.BestLevel <= 0 && st.Total.Best.TryGetValue("lvl", out rec)) st.Total.BestLevel = rec.Value;
            if (st.Total.BestTime <= 0 && st.Total.Best.TryGetValue("time", out rec)) st.Total.BestTime = rec.Value;
            Abilities(LuaReader.Tab(db, "abilities"), st.Abilities);
            Groups(LuaReader.Tab(db, "classes"), st.Classes);
            Groups(LuaReader.Tab(db, "races"), st.Races);
            Groups(LuaReader.Tab(db, "zones"), st.Zones);
            var months = LuaReader.Tab(db, "months");
            if (months != null)
                foreach (var kv in months)
                {
                    var m = kv.Value as Dictionary<string, object>;
                    if (m == null) continue;
                    var ms = new MonthStat();
                    ms.Month = kv.Key;
                    ms.Runs = LuaReader.Num(m, "runs");
                    var cl = LuaReader.Tab(m, "classes");
                    if (cl != null) foreach (var c in cl) ms.Classes[c.Key] = LuaReader.Num(cl, c.Key);
                    st.Months.Add(ms);
                }
            st.Months.Sort(delegate(MonthStat a, MonthStat b) { return string.CompareOrdinal(a.Month, b.Month); });
            var hist = LuaReader.List(LuaReader.Tab(db, "history"));
            for (int i = hist.Count - 1; i >= 0; i--) st.History.Add(ReadRun(hist[i]));

            // play time (counted by the addon while the game runs)
            var play = LuaReader.Tab(db, "play");
            st.Play.Seconds = (long)LuaReader.Num(play, "seconds");
            st.Play.Sessions = (long)LuaReader.Num(play, "sessions");
            st.Play.Longest = (long)LuaReader.Num(play, "longest");
            st.Play.LastSeconds = (long)LuaReader.Num(play, "last");
            st.Play.LastDate = LuaReader.Text(play, "lastDate");

            // Nemesis: the death log and who killed you
            var deaths = LuaReader.List(LuaReader.Tab(db, "deaths"));
            for (int i = deaths.Count - 1; i >= 0; i--)
            {
                var d = deaths[i];
                var ds = new DeathStat();
                ds.Date = LuaReader.Text(d, "date");
                ds.Killer = LuaReader.Text(d, "killer");
                ds.Spell = LuaReader.Text(d, "spell");
                ds.Amount = LuaReader.Num(d, "amt");
                ds.ClassKey = LuaReader.Text(d, "class");
                ds.Class = LuaReader.Text(d, "cname");
                ds.Race = LuaReader.Text(d, "rname");
                ds.Zone = LuaReader.Text(d, "zname");
                ds.Time = LuaReader.Num(d, "t");
                ds.Level = LuaReader.Num(d, "lvl");
                ds.LastHits = LuaReader.Text(d, "last");
                ds.TopSources = LuaReader.Text(d, "top");
                st.Deaths.Add(ds);
            }
            Nemeses(LuaReader.Tab(db, "nemesis"), st.Nemesis);

            // best builds (the addon keeps them for DPS highs and victories) and the bosses you beat
            foreach (var h in LuaReader.List(LuaReader.Tab(db, "builds"))) st.Builds.Add(ReadRun(h));
            var bk = LuaReader.Tab(db, "bossKills");
            if (bk != null) foreach (var kv in bk) st.BossKills[kv.Key] = LuaReader.Num(bk, kv.Key);

            // the armory: each class's hero, gear and numbers
            var arm = LuaReader.Tab(db, "armory");
            if (arm != null)
                foreach (var kv in arm)
                {
                    var a = kv.Value as Dictionary<string, object>;
                    if (a == null) continue;
                    var ac = new ArmoryChar();
                    ac.ClassKey = kv.Key;
                    ac.Class = LuaReader.Text(a, "cname");
                    if (ac.Class.Length == 0) ac.Class = Pretty(kv.Key);
                    ac.Hero = LuaReader.Text(a, "hero");
                    ac.Race = LuaReader.Text(a, "race");
                    ac.Date = LuaReader.Text(a, "date");
                    ac.Level = LuaReader.Num(a, "level");
                    foreach (var g in LuaReader.List(LuaReader.Tab(a, "gear")))
                    {
                        var it = new ArmoryItem();
                        it.Slot = LuaReader.Text(g, "slot");
                        it.Name = LuaReader.Text(g, "name");
                        it.Color = LuaReader.Text(g, "color");
                        if (it.Color.Length != 6) it.Color = "ffffff";
                        it.Icon = "";                                // Legacy data did not retain item icons
                        it.Id = LuaReader.Num(g, "id");
                        ac.Gear.Add(it);
                    }
                    var nums = LuaReader.Tab(a, "stats");
                    if (nums != null) foreach (var n in nums) ac.Numbers[n.Key] = LuaReader.Num(nums, n.Key);
                    st.Armory.Add(ac);
                }
            st.Armory.Sort(delegate(ArmoryChar x, ArmoryChar y) { return string.CompareOrdinal(y.Date, x.Date); });
            // co-op
            var co = LuaReader.Tab(db, "coop");
            st.Coop.Runs = LuaReader.Num(co, "runs");
            st.Coop.Wins = LuaReader.Num(co, "wins");
            st.Coop.Time = LuaReader.Num(co, "time");
            st.Coop.Kills = LuaReader.Num(co, "kills");
            st.Coop.Bosses = LuaReader.Num(co, "bosses");
            st.Coop.Solo = LuaReader.Num(co, "solo");
            st.Coop.SoloWins = LuaReader.Num(co, "soloWins");
            st.Coop.Deaths = LuaReader.Num(co, "deaths");
            Nemeses(LuaReader.Tab(co, "deathsBy"), st.Coop.DeathsBy);
            Bests(LuaReader.Tab(co, "best"), st.Coop.Best);
            var bestTab = LuaReader.Tab(co, "best");
            if (bestTab != null)
                foreach (var kv in st.Coop.Best) kv.Value.Hero = LuaReader.Text(LuaReader.Tab(bestTab, kv.Key), "with");
            var friends = LuaReader.Tab(co, "friends");
            if (friends != null)
                foreach (var kv in friends)
                {
                    var f = kv.Value as Dictionary<string, object>;
                    if (f == null) continue;
                    var fs = new FriendStat();
                    fs.Name = kv.Key;
                    fs.ClassKey = LuaReader.Text(f, "class");
                    fs.Class = LuaReader.Text(f, "cname");
                    fs.First = LuaReader.Text(f, "first");
                    fs.Last = LuaReader.Text(f, "last");
                    fs.FirstWin = LuaReader.Text(f, "firstWin");
                    fs.Runs = LuaReader.Num(f, "runs");
                    fs.Wins = LuaReader.Num(f, "wins");
                    fs.Time = LuaReader.Num(f, "time");
                    fs.Kills = LuaReader.Num(f, "kills");
                    fs.Bosses = LuaReader.Num(f, "bosses");
                    fs.Damage = LuaReader.Num(f, "dmg");
                    fs.BestTime = LuaReader.Num(f, "bestTime");
                    fs.BestLevel = LuaReader.Num(f, "bestLvl");
                    fs.MostBosses = LuaReader.Num(f, "mostBosses");
                    fs.FastestWin = LuaReader.Num(f, "fastestWin");
                    st.Coop.Friends.Add(fs);
                }
            st.Coop.Friends.Sort(delegate(FriendStat a, FriendStat b) { return b.Runs.CompareTo(a.Runs); });
            return st;
        }

        // Adapter for Maidenhead's schema-1 SavedVariables. Keep the original observed fields and provenance;
        // only fields with a direct equivalent are mapped into the launcher's older summary cards.
        static RunStats LoadAzeroth(string path, Dictionary<string, object> db)
        {
            var st = new RunStats();
            st.Source = "Azeroth Stats";
            st.FilePath = path;
            st.Written = File.GetLastWriteTime(path);
            st.Version = (int)LuaReader.Num(db, "schema");
            var totals = LuaReader.Tab(db, "totals");
            st.Total = NewBlock("", totals);
            var classes = LuaReader.Tab(db, "classes");
            if (classes != null)
                foreach (var kv in classes)
                {
                    var c = kv.Value as Dictionary<string, object>;
                    if (c == null) continue;
                    st.Classes.Add(NewBlock(kv.Key, c));
                }
            st.Classes.Sort(delegate(StatBlock a, StatBlock b)
            {
                int n = b.Runs.CompareTo(a.Runs);
                return n != 0 ? n : string.Compare(a.Name, b.Name, StringComparison.CurrentCultureIgnoreCase);
            });

            var storedRuns = LuaReader.List(LuaReader.Tab(db, "runs"));
            for (int i = storedRuns.Count - 1; i >= 0; i--)
                st.History.Add(ReadAzerothRun(storedRuns[i]));
            var seenArmory = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            for (int i = storedRuns.Count - 1; i >= 0; i--)
            {
                var rawRun = storedRuns[i];
                string key = LuaReader.Text(rawRun, "class");
                if (key.Length == 0 || !seenArmory.Add(key)) continue;
                ArmoryChar character = ReadAzerothArmory(rawRun);
                if (character.Gear.Count > 0 || character.Numbers.Count > 0) st.Armory.Add(character);
            }

            // Azeroth Stats companion/death evidence is explicitly observational; do not present it as proof
            // of shared victories or confirmed killing blows.
            ReadNewCompanions(totals, st.Coop.Friends);
            st.Coop.Friends.Sort(delegate(FriendStat a, FriendStat b) { return b.Runs.CompareTo(a.Runs); });

            // Build retained-sample race/zone summaries explicitly as samples; these are not lifetime totals.
            // The UI currently avoids displaying these as aggregates to prevent implying otherwise.
            return st;
        }

        static StatBlock NewBlock(string key, Dictionary<string, object> group)
        {
            var b = new StatBlock();
            b.Key = key;
            b.CoverageAware = true;
            b.Name = LuaReader.Text(group, "name");
            if (b.Name.Length == 0) b.Name = Pretty(key);
            b.Runs = LuaReader.Num(group, "runs");
            b.Victories = LuaReader.Num(group, "wins");
            var evidence = LuaReader.Tab(group, "deathEvidence");
            b.ObservedDeaths = LuaReader.Num(evidence, "deaths");
            var sources = LuaReader.Tab(evidence, "sources");
            if (sources != null)
                foreach (var kv in sources)
                {
                    var s = kv.Value as Dictionary<string, object>;
                    if (s == null) continue;
                    double confirmed = LuaReader.Num(s, "confirmed"), probable = LuaReader.Num(s, "probable");
                    if (confirmed + probable > 0)
                        b.Nemesis.Add(new NemesisStat { Name = kv.Key, Confirmed = confirmed, Probable = probable,
                            Kills = confirmed + probable });
                }
            b.Nemesis.Sort(delegate(NemesisStat a, NemesisStat c) { return c.Kills.CompareTo(a.Kills); });
            var sums = LuaReader.Tab(group, "sum");
            var coverage = LuaReader.Tab(group, "coverage");
            if (coverage != null)
                foreach (var kv in coverage) if (Number(kv.Value) > 0) b.AvailableMetrics.Add(kv.Key);
            b.Time = LuaReader.Num(sums, "duration");
            b.Kills = LuaReader.Num(sums, "kills");
            b.Gold = LuaReader.Num(sums, "gold");
            b.Damage = LuaReader.Num(sums, "damage");
            b.Healing = LuaReader.Num(sums, "healing");
            b.Taken = LuaReader.Num(sums, "taken");
            b.Bosses = LuaReader.Num(sums, "bosses");
            b.Elites = LuaReader.Num(sums, "elites");
            b.Guardians = LuaReader.Num(sums, "guardians");
            b.Chests = LuaReader.Num(sums, "chests");
            b.Magnets = LuaReader.Num(sums, "magnets");
            b.BestLevel = LuaReader.Num(sums, "level");
            var records = LuaReader.Tab(group, "records");
            if (records != null)
                foreach (var kv in records)
                {
                    var item = kv.Value as Dictionary<string, object>;
                    if (item == null) continue;
                    var rec = new StatRecord { Key = kv.Key, Value = LuaReader.Num(item, "value"), Date = LuaReader.Text(item, "at") };
                    b.Best[kv.Key] = rec;
                    if (kv.Key == "level") b.BestLevel = rec.Value;
                    if (kv.Key == "duration") b.BestTime = rec.Value;
                }
            // These are bounded observed totals, distinct from native run summaries.
            var spellUsage = LuaReader.Tab(LuaReader.Tab(group, "usage"), "spells");
            if (spellUsage != null)
                foreach (var kv in spellUsage)
                {
                    var s = kv.Value as Dictionary<string, object>;
                    if (s == null) continue;
                    string name = LuaReader.Text(s, "name");
                    if (name.Length == 0) name = Pretty(kv.Key);
                    b.Abilities.Add(new AbilityStat { Key = kv.Key, Name = name, Picks = LuaReader.Num(s, "runs"),
                        Ranks = LuaReader.Num(s, "gains"), BestRank = LuaReader.Num(s, "maxRank") });
                }
            b.Abilities.Sort(delegate(AbilityStat x, AbilityStat y) { return y.Ranks.CompareTo(x.Ranks); });
            return b;
        }

        static double Number(object value)
        {
            if (value is double) return (double)value;
            double d;
            return double.TryParse(Convert.ToString(value, CultureInfo.InvariantCulture), NumberStyles.Float,
                                   CultureInfo.InvariantCulture, out d) ? d : 0;
        }

        static void ReadNewCompanions(Dictionary<string, object> group, List<FriendStat> into)
        {
            var companions = LuaReader.Tab(group, "companions");
            if (companions == null) return;
            foreach (var kv in companions)
            {
                var c = kv.Value as Dictionary<string, object>;
                if (c == null) continue;
                into.Add(new FriendStat { Name = LuaReader.Text(c, "name"), ClassKey = LuaReader.Text(c, "class"),
                    Class = Pretty(LuaReader.Text(c, "class")), Runs = LuaReader.Num(c, "runs"), Wins = LuaReader.Num(c, "wins") });
            }
        }

        static ArmoryChar ReadAzerothArmory(Dictionary<string, object> run)
        {
            var c = new ArmoryChar();
            c.ClassKey = LuaReader.Text(run, "class");
            c.Class = Pretty(c.ClassKey);
            c.Hero = LuaReader.Text(run, "hero");
            c.Race = LuaReader.Text(run, "race");
            c.Date = LuaReader.Text(run, "startedAt");
            c.Level = LuaReader.Num(LuaReader.Tab(run, "values"), "level");
            var gear = LuaReader.Tab(run, "equipment");
            var icons = LuaReader.Tab(run, "equipmentIcons");
            if (gear != null)
                foreach (var kv in gear)
                {
                    var link = kv.Value as string;
                    if (string.IsNullOrEmpty(link)) continue;
                    var name = Regex.Match(link, @"\[([^\]]+)\]");
                    var id = Regex.Match(link, @"Hitem:(\d+)");
                    var item = new ArmoryItem { Slot = kv.Key, Name = name.Success ? name.Groups[1].Value : link };
                    int itemId;
                    if (id.Success && int.TryParse(id.Groups[1].Value, out itemId)) item.Id = itemId;
                    item.Icon = LuaReader.Text(icons, kv.Key);
                    c.Gear.Add(item);
                }
            var stats = LuaReader.Tab(run, "stats");
            if (stats != null) foreach (var kv in stats) c.Numbers[kv.Key] = Number(kv.Value);
            return c;
        }

        static StatRun ReadAzerothRun(Dictionary<string, object> row)
        {
            var run = new StatRun();
            run.AzerothStatsSource = true;
            run.Id = LuaReader.Text(row, "id");
            run.Status = LuaReader.Text(row, "status");
            run.Phase = LuaReader.Text(row, "phase");
            run.Result = LuaReader.Text(row, "result");
            run.Kind = run.Result.Length > 0 ? run.Result : run.Status;
            run.Date = LuaReader.Text(row, "startedAt");
            run.Hero = LuaReader.Text(row, "hero");
            run.ClassKey = LuaReader.Text(row, "class");
            run.Class = Pretty(run.ClassKey);
            run.Race = LuaReader.Text(row, "race");
            run.Zone = LuaReader.Text(row, "zoneName");
            if (run.Zone.Length == 0) run.Zone = LuaReader.Text(row, "zone");
            run.ZoneName = run.Zone;
            run.Partial = row.ContainsKey("partial") && row["partial"] is bool && (bool)row["partial"];
            run.HeroicKnown = row.ContainsKey("heroic") && row["heroic"] is bool;
            run.Heroic = run.HeroicKnown && (bool)row["heroic"];
            var values = LuaReader.Tab(row, "values");
            if (values != null)
                foreach (var kv in values) run.ObservedValues[kv.Key] = Number(kv.Value);
            run.Time = Metric(run, "duration");
            run.Level = Metric(run, "level");
            run.Kills = Metric(run, "kills");
            run.Gold = Metric(run, "gold");
            run.Damage = Metric(run, "damage");
            run.Healing = Metric(run, "healing");
            run.Taken = Metric(run, "taken");
            run.Bosses = Metric(run, "bosses");
            run.Elites = Metric(run, "elites");
            run.Guardians = Metric(run, "guardians");
            run.Chests = Metric(run, "chests");
            run.Magnets = Metric(run, "magnets");
            run.Dps = Metric(run, "peakDps");

            foreach (var spell in LuaReader.List(LuaReader.Tab(row, "spells")))
            {
                string name = LuaReader.Text(spell, "name");
                if (name.Length == 0) name = LuaReader.Text(spell, "spell");
                if (name.Length == 0) name = LuaReader.Text(spell, "key");
                double rank = LuaReader.Num(spell, "rank");
                if (name.Length > 0)
                {
                    run.ObservedSpells.Add(new AbilityStat { Key = LuaReader.Text(spell, "key"), Name = name, Picks = rank });
                    run.Observations.Add(new KeyValuePair<string, string>("Spell rank", name + (rank > 0 ? " · " + Math.Floor(rank).ToString("#,0", CultureInfo.InvariantCulture) : "")));
                }
            }
            var journal = LuaReader.Tab(row, "buildJournal");
            foreach (var entry in LuaReader.List(LuaReader.Tab(journal, "entries")))
            {
                string kind = LuaReader.Text(entry, "kind");
                double at = LuaReader.Num(entry, "t");
                foreach (var change in LuaReader.List(LuaReader.Tab(entry, "changes")))
                {
                    string key = LuaReader.Text(change, "key");
                    string before = change.ContainsKey("before") ? Convert.ToString(change["before"], CultureInfo.InvariantCulture) : "start";
                    string after = change.ContainsKey("after") ? Convert.ToString(change["after"], CultureInfo.InvariantCulture) : "removed";
                    run.Observations.Add(new KeyValuePair<string, string>(kind + " · " + Clock(at), key + ": " + before + " → " + after));
                }
            }
            ReadMeter(row, "damage", run.DamageSources);
            ReadMeter(row, "healing", run.HealingSources);
            ReadMeter(row, "taken", run.TakenSources);
            var party = LuaReader.Tab(LuaReader.Tab(row, "coop"), "comparison");
            foreach (var member in LuaReader.List(LuaReader.Tab(party, "rows")))
            {
                var friend = new FriendStat();
                friend.Name = LuaReader.Text(member, "name");
                friend.ClassKey = LuaReader.Text(member, "class");
                friend.Class = Pretty(friend.ClassKey);
                friend.Damage = LuaReader.Num(member, "damage");
                friend.Dps = LuaReader.Num(member, "dps");
                friend.First = LuaReader.Text(member, "top");
                if (friend.Name.Length > 0) run.PartySnapshot.Add(friend);
            }
            var deaths = LuaReader.List(LuaReader.Tab(row, "deaths"));
            foreach (var death in deaths)
            {
                string source = LuaReader.Text(death, "source");
                string confidence = LuaReader.Text(death, "confidence");
                run.Observations.Add(new KeyValuePair<string, string>("Death evidence",
                    (source.Length > 0 ? source : "Unattributed") + " · " + (confidence.Length > 0 ? confidence : "unknown")));
            }
            return run;
        }

        static double Metric(StatRun run, string key)
        {
            double value;
            return run.ObservedValues.TryGetValue(key, out value) ? value : 0;
        }

        static string Clock(double seconds)
        {
            long s = (long)Math.Max(0, Math.Floor(seconds));
            long h = s / 3600, m = s % 3600 / 60, sec = s % 60;
            return h > 0 ? h + ":" + m.ToString("00", CultureInfo.InvariantCulture) + ":" + sec.ToString("00", CultureInfo.InvariantCulture)
                         : m + ":" + sec.ToString("00", CultureInfo.InvariantCulture);
        }

        static void ReadMeter(Dictionary<string, object> run, string key, List<KeyValuePair<string, double>> into)
        {
            var meters = LuaReader.Tab(run, "meters");
            var meter = LuaReader.Tab(meters, key);
            foreach (var source in LuaReader.List(LuaReader.Tab(meter, "rows")))
            {
                string name = LuaReader.Text(source, "name");
                if (name.Length == 0) name = LuaReader.Text(source, "source");
                double amount = LuaReader.Num(source, "dmg");
                if (name.Length > 0) into.Add(new KeyValuePair<string, double>(name, amount));
            }
            into.Sort(delegate(KeyValuePair<string, double> a, KeyValuePair<string, double> b) { return b.Value.CompareTo(a.Value); });
        }

        static StatBlock Block(string key, Dictionary<string, object> t)
        {
            var b = new StatBlock();
            b.Key = key;
            b.Name = LuaReader.Text(t, "name");
            if (b.Name.Length == 0) b.Name = Pretty(key);
            b.Runs = LuaReader.Num(t, "runs");
            b.Victories = Math.Max(LuaReader.Num(t, "victory"), LuaReader.Num(t, "wins"));
            b.Defeats = LuaReader.Num(t, "defeat");
            b.Left = LuaReader.Num(t, "left");
            b.Time = LuaReader.Num(t, "time");
            b.Kills = LuaReader.Num(t, "kills");
            b.Elites = LuaReader.Num(t, "elites");
            b.Guardians = LuaReader.Num(t, "guardians");
            b.Bosses = LuaReader.Num(t, "bosses");
            b.Damage = LuaReader.Num(t, "dmg");
            b.Taken = LuaReader.Num(t, "taken");
            b.Gold = LuaReader.Num(t, "gold");
            b.Chests = LuaReader.Num(t, "chests");
            b.Magnets = LuaReader.Num(t, "magnets");
            b.Levels = LuaReader.Num(t, "levels");
            b.BestTime = LuaReader.Num(t, "bestTime");
            b.BestLevel = LuaReader.Num(t, "bestLvl");
            b.Legendary = LuaReader.Num(t, "legendary");
            b.Saves = LuaReader.Num(t, "saves");
            return b;
        }

        static void Bests(Dictionary<string, object> best, Dictionary<string, StatRecord> into)
        {
            if (best == null) return;
            foreach (var kv in best)
            {
                var b = kv.Value as Dictionary<string, object>;
                if (b == null) continue;
                var rec = new StatRecord();
                rec.Key = kv.Key;
                rec.Value = LuaReader.Num(b, "v");
                rec.Hero = LuaReader.Text(b, "hero");
                rec.Class = LuaReader.Text(b, "cname");
                rec.Race = LuaReader.Text(b, "rname");
                rec.Zone = LuaReader.Text(b, "zname");
                rec.Date = LuaReader.Text(b, "date");
                into[kv.Key] = rec;
            }
        }

        static void Abilities(Dictionary<string, object> d, List<AbilityStat> into)
        {
            if (d == null) return;
            foreach (var kv in d)
            {
                var a = kv.Value as Dictionary<string, object>;
                if (a == null) continue;
                var ab = new AbilityStat();
                ab.Key = kv.Key;
                ab.Name = LuaReader.Text(a, "name");
                if (ab.Name.Length == 0) ab.Name = Pretty(kv.Key);
                ab.Picks = LuaReader.Num(a, "picks");
                ab.Wins = LuaReader.Num(a, "wins");
                ab.Damage = LuaReader.Num(a, "dmg");
                ab.BestRank = LuaReader.Num(a, "bestRank");
                ab.Ranks = LuaReader.Num(a, "ranks");
                into.Add(ab);
            }
            into.Sort(delegate(AbilityStat x, AbilityStat y)
            {
                int c = y.Picks.CompareTo(x.Picks);
                return c != 0 ? c : y.Damage.CompareTo(x.Damage);
            });
        }

        static void Nemeses(Dictionary<string, object> d, List<NemesisStat> into)
        {
            if (d == null) return;
            foreach (var kv in d)
            {
                var n = kv.Value as Dictionary<string, object>;
                if (n == null) continue;
                var ns = new NemesisStat();
                ns.Name = kv.Key;
                ns.Kills = LuaReader.Num(n, "kills");
                ns.Damage = LuaReader.Num(n, "dmg");
                ns.Last = LuaReader.Text(n, "last");
                into.Add(ns);
            }
            into.Sort(delegate(NemesisStat a, NemesisStat b) { return b.Kills.CompareTo(a.Kills); });
        }

        static void Groups(Dictionary<string, object> d, List<StatBlock> into)
        {
            if (d == null) return;
            foreach (var kv in d)
            {
                var g = kv.Value as Dictionary<string, object>;
                if (g == null) continue;
                var b = Block(kv.Key, g);
                Bests(LuaReader.Tab(g, "best"), b.Best);
                Abilities(LuaReader.Tab(g, "abilities"), b.Abilities);
                Nemeses(LuaReader.Tab(g, "nemesis"), b.Nemesis);
                into.Add(b);
            }
            into.Sort(delegate(StatBlock a, StatBlock b)
            {
                int c = b.Runs.CompareTo(a.Runs);
                return c != 0 ? c : string.Compare(a.Name, b.Name, StringComparison.CurrentCultureIgnoreCase);
            });
        }

        // one run as the addon writes it (history and best builds)
        static StatRun ReadRun(Dictionary<string, object> h)
        {
            var run = new StatRun();

            run.Result = LuaReader.Text(h, "result");
            run.Kind = LuaReader.Text(h, "kind");
            run.Date = LuaReader.Text(h, "date");
            run.Hero = LuaReader.Text(h, "hero");
            run.ClassKey = LuaReader.Text(h, "class");
            run.Class = LuaReader.Text(h, "cname");
            run.Race = LuaReader.Text(h, "rname");
            run.Zone = LuaReader.Text(h, "zname");
            run.Affixes = LuaReader.Text(h, "affixes");
            run.Time = LuaReader.Num(h, "t");
            run.Level = LuaReader.Num(h, "lvl");
            run.Kills = LuaReader.Num(h, "kills");
            run.Damage = LuaReader.Num(h, "dmg");
            run.Dps = LuaReader.Num(h, "dps");
            if (run.Dps <= 0 && run.Time > 0) run.Dps = Math.Round(run.Damage / run.Time);   // older records did not keep DPS
            run.Gold = LuaReader.Num(h, "gold");
            run.Bosses = LuaReader.Num(h, "bosses");
            run.Elites = LuaReader.Num(h, "elites");
            run.Guardians = LuaReader.Num(h, "guardians");
            run.Taken = LuaReader.Num(h, "taken");
            run.Chests = LuaReader.Num(h, "chests");
            run.Legendary = LuaReader.Num(h, "legendary");
            run.Saves = LuaReader.Num(h, "saves");
            var hi = LuaReader.Tab(h, "hi");
            if (hi != null) foreach (var kv in hi) { double v; if (double.TryParse(Convert.ToString(kv.Value, System.Globalization.CultureInfo.InvariantCulture), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out v)) run.Highs[kv.Key] = v; }
            run.Party = LuaReader.Text(h, "party");
            run.Killer = LuaReader.Text(h, "killer");
            foreach (var a in LuaReader.List(LuaReader.Tab(h, "abilities")))
            {
                var ab = new AbilityStat();
                ab.Key = LuaReader.Text(a, "key");
                ab.Name = LuaReader.Text(a, "name");
                if (ab.Name.Length == 0) ab.Name = Pretty(ab.Key);
                ab.Picks = LuaReader.Num(a, "rank");
                ab.Damage = LuaReader.Num(a, "dmg");
                run.Abilities.Add(ab);
            }
            return run;
        }

        // "death_knight" -> "Death Knight"
        public static string Pretty(string key)
        {
            var parts = key.Replace('_', ' ').Split(' ');
            for (int i = 0; i < parts.Length; i++)
                if (parts[i].Length > 0) parts[i] = char.ToUpperInvariant(parts[i][0]) + parts[i].Substring(1);
            return string.Join(" ", parts);
        }

        // ---- the addon, built into the launcher (Download More offers the same files as a zip)
        static string Embedded(string file) { return Util.ReadResource("AS.addon." + file); }

        // the addon's folder in AddOns (also when it is turned off; else an earlier version's folder), or null
        public static string AddonPath(string addonsDir)
        {
            string f = Folder(addonsDir, AddonFolder);
            if (f == null) f = Folder(addonsDir, LegacyAddonFolder);
            foreach (var o in OldNames) if (f == null) f = Folder(addonsDir, o[0]);
            return f;
        }

        static string Folder(string addonsDir, string name)
        {
            if (string.IsNullOrEmpty(addonsDir)) return null;
            string on = Path.Combine(addonsDir, name), off = on + Addons.DisabledSuffix;
            if (Directory.Exists(on)) return on;
            if (Directory.Exists(off)) return off;
            return null;
        }

        public static bool AddonInstalled(string addonsDir) { return AddonPath(addonsDir) != null; }
        public static bool ReplacementInstalled(string addonsDir) { return Folder(addonsDir, AddonFolder) != null; }

    }
}
