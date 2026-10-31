using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace AzerothLauncher
{
    // The launcher's smaller representation of an ASBuilds build. Unsupported ASBuilder fields are
    // intentionally ignored; only talent ranks and selected jewelry enter the local build.
    internal sealed class AsBuildsBuild
    {
        public string Class = "", Name = "", Talent = "";
        public Dictionary<string, string> Gear = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
    }

    internal static class AsBuildsInterop
    {
        const string Alphabet = "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789-_";
        static readonly string[] Stages = { "Early Game", "Mid Game", "End Game", "Endless" };

        sealed class Pick
        {
            public string Kind, Key;
            public bool Alternate, Banish;
        }

        sealed class Stage
        {
            public readonly List<Pick> Picks = new List<Pick>();
        }

        sealed class Reader
        {
            readonly byte[] data;
            int at;
            public Reader(byte[] bytes) { data = bytes ?? new byte[0]; }
            public int Remaining { get { return data.Length - at; } }
            public int? Byte()
            {
                if (at >= data.Length) return null;
                return data[at++];
            }
            public long? Var()
            {
                long value = 0, factor = 1;
                for (int i = 0; i < 5; i++)
                {
                    int? c = Byte();
                    if (!c.HasValue) return null;
                    value += (c.Value & 127) * factor;
                    if (value > int.MaxValue) return null;
                    if ((c.Value & 128) == 0) return value;
                    factor *= 128;
                }
                return null;
            }
            public byte[] Bytes()
            {
                long? size = Var();
                if (!size.HasValue || size.Value > Remaining) return null;
                byte[] value = new byte[(int)size.Value];
                Buffer.BlockCopy(data, at, value, 0, value.Length);
                at += value.Length;
                return value;
            }
            public string String()
            {
                byte[] value = Bytes();
                return value == null ? null : Encoding.UTF8.GetString(value);
            }
        }

        public static bool TryRead(string code, out List<AsBuildsBuild> builds, out string error)
        {
            builds = new List<AsBuildsBuild>();
            error = "";
            string text = (code ?? "").Trim();
            if (text.Length > 2000000) { error = "The ASBuilds code is larger than the supported profile limit."; return false; }
            if (text.StartsWith("ASB2:", StringComparison.Ordinal))
            {
                byte[] body;
                if (!TryUnwrap(text, "ASB2:", false, out body, out error)) return false;
                AsBuildsBuild build;
                if (!ReadBuildBody(body, out build, out error)) return false;
                builds.Add(build);
                return true;
            }
            if (text.StartsWith("ASBP:", StringComparison.Ordinal))
                return ReadProfile(text, builds, out error);

            error = "Paste an ASB2 build code or an ASBP profile code from ASBuilds.";
            return false;
        }

        static bool ReadProfile(string text, List<AsBuildsBuild> builds, out string error)
        {
            error = "The ASBuilds profile code is damaged or incomplete.";
            byte[] body;
            if (!TryUnwrap(text, "ASBP:", true, out body, out error)) return false;
            var r = new Reader(body);
            if (r.Byte() != 5) { error = "This ASBuilds profile version is not supported."; return false; }
            long? count = r.Var();
            if (!count.HasValue || count.Value > 300) { error = "The ASBuilds profile is invalid or too large."; return false; }
            for (int i = 0; i < (int)count.Value; i++)
            {
                string uid = r.String();
                long? revision = r.Var();
                int? flag = r.Byte();
                if (uid == null || !revision.HasValue || !flag.HasValue) return false;
                if (flag.Value == 1) continue;                 // tombstones do not describe a build
                byte[] raw = r.Bytes();
                string profileTalent = r.String();           // website-only talent metadata; body has rank data
                if (raw == null || profileTalent == null) return false;
                AsBuildsBuild build;
                string why;
                if (!ReadBuildBody(raw, out build, out why)) { error = why; return false; }
                // ASBuilds stores profile talent allocations as JSON and strips that JSON from
                // the compact body. Prefer the body code when present; otherwise translate the
                // profile's class-local talent keys into the launcher's Spec/key form.
                if (string.IsNullOrWhiteSpace(build.Talent) || build.Talent.TrimStart().StartsWith("{"))
                    build.Talent = profileTalent.TrimStart().StartsWith("{")
                        ? ProfileTalentCode(build.Class, profileTalent)
                        : NormalizeTalentCode(profileTalent, build.Class);
                builds.Add(build);
            }
            if (r.Remaining != 0) { error = "The ASBuilds profile has unexpected trailing data."; return false; }
            if (builds.Count == 0) { error = "The ASBuilds profile contains no active builds."; return false; }
            error = "";
            return true;
        }

        static bool ReadBuildBody(byte[] body, out AsBuildsBuild output, out string error)
        {
            output = null;
            error = "The ASBuilds build is damaged or incomplete.";
            var r = new Reader(body);
            if (r.Var() != 4) { error = "This ASBuilds build version is not supported."; return false; }
            string externalClass = r.String();
            if (externalClass == null) return false;
            string cls = LocalClass(externalClass);
            if (cls == null) { error = "This ASBuilds build uses a class the launcher does not support."; return false; }
            long? fill = r.Var();
            string name = r.String();
            long? itemCount = r.Var();
            if (!fill.HasValue || name == null || !itemCount.HasValue || itemCount.Value > 4096) return false;

            var items = new Pick[(int)itemCount.Value];
            for (int i = 0; i < items.Length; i++)
            {
                long? reference = r.Var();
                int? hi = r.Byte(), lo = r.Byte();
                if (!reference.HasValue || !hi.HasValue || !lo.HasValue) return false;
                int kindId = (int)(reference.Value % 8);
                string kind = kindId == 1 ? "ring" : kindId == 2 ? "trinket" : kindId == 3 ? "amulet" : kindId == 4 ? "legend" : null;
                string key = FindLocalKey(kind, cls, hi.Value * 256 + lo.Value);
                if (kind != null && key != null) items[i] = new Pick { Kind = kind, Key = key };
            }

            long? stageCount = r.Var();
            if (!stageCount.HasValue || stageCount.Value > 64) return false;
            var stages = new List<Stage>();
            for (int s = 0; s < (int)stageCount.Value; s++)
            {
                string stageName = r.String();
                long? pickCount = r.Var();
                if (stageName == null || !pickCount.HasValue || pickCount.Value > 4096) return false;
                var stage = new Stage();
                for (int p = 0; p < (int)pickCount.Value; p++)
                {
                    long? reference = r.Var(), target = r.Var();
                    if (!reference.HasValue || !target.HasValue) return false;
                    int item = (int)(reference.Value / 4), flags = (int)(reference.Value % 4);
                    if (item >= 0 && item < items.Length && items[item] != null)
                    {
                        Pick source = items[item];
                        stage.Picks.Add(new Pick { Kind = source.Kind, Key = source.Key,
                            Banish = (flags & 1) != 0, Alternate = (flags & 2) != 0 });
                    }
                }
                if (r.String() == null) return false;          // stage notes are intentionally not imported
                stages.Add(stage);
            }

            // ASB2 appends a link, optional statistics/targets, then its compact talent-rank text.
            string link = r.Remaining > 0 ? r.String() : "";
            string extra = r.Remaining > 0 ? r.String() : "";
            string talent = r.Remaining > 0 ? r.String() : "";
            if (link == null || extra == null || talent == null) return false;

            output = new AsBuildsBuild { Class = cls, Name = name.Trim().Length == 0 ? "Imported build" : name.Trim(), Talent = NormalizeTalentCode(talent, cls) };
            CopySelectedGear(stages, output.Gear);
            if (r.Remaining > 0)
            {
                string local = r.String();
                try
                {
                    var metadata = Json.AsDict(Json.Parse(local ?? "{}"));
                    if (metadata != null) foreach (var value in metadata)
                        if (value.Value is string && (value.Key.StartsWith("$armor:") || value.Key.StartsWith("$star:")))
                            output.Gear[value.Key] = (string)value.Value;
                }
                catch (Exception) { }
            }
            error = "";
            return true;
        }

        static void CopySelectedGear(List<Stage> stages, Dictionary<string, string> destination)
        {
            // A local gear sheet is a single loadout: take its latest stage with selected gear,
            // prefer the Endless stage, and keep main picks only (alternatives are not equipped).
            int chosen = -1;
            for (int i = stages.Count - 1; i >= 0; i--)
                if (stages[i].Picks.Any(delegate(Pick p) { return !p.Alternate && !p.Banish && p.Kind != "spell"; }))
                { chosen = i; break; }
            if (chosen < 0) return;
            int rings = 0, trinkets = 0;
            foreach (Pick p in stages[chosen].Picks)
            {
                if (p.Alternate || p.Banish) continue;
                if (p.Kind == "ring" && rings < 2) destination["Finger " + (++rings)] = p.Key;
                else if (p.Kind == "trinket" && trinkets < 2) destination["Trinket " + (++trinkets)] = p.Key;
                else if ((p.Kind == "amulet" || p.Kind == "legend") && !destination.ContainsKey("Amulet")) destination["Amulet"] = p.Key;
            }
        }

        static string FindLocalKey(string kind, string cls, int hash)
        {
            if (kind == null) return null;
            var found = new List<string>();
            foreach (Jewel jewel in Items.Jewelry)
            {
                if (kind == "legend")
                {
                    if (!jewel.IsCodex || !string.Equals(jewel.Class, cls, StringComparison.OrdinalIgnoreCase)) continue;
                    string spellKey = Talents.AbilityKey(jewel.Spell);
                    if (spellKey != null && KeyHash(spellKey) == hash) found.Add(jewel.Key);
                }
                else if (!jewel.IsCodex && string.Equals(jewel.Kind, KindName(kind), StringComparison.OrdinalIgnoreCase) && KeyHash(jewel.Key) == hash)
                    found.Add(jewel.Key);
            }
            return found.Count == 1 ? found[0] : null;         // ambiguous keys are discarded, never guessed
        }

        static string KindName(string kind) { return kind == "ring" ? "Ring" : kind == "trinket" ? "Trinket" : "Amulet"; }

        public static string LocalClass(string external)
        {
            foreach (string cls in Items.Classes)
                if (string.Equals(cls, external, StringComparison.OrdinalIgnoreCase)) return cls;
            return null;
        }

        static string ExternalClass(string local) { return (local ?? "").Trim().ToLowerInvariant(); }

        public static string ExportBuild(string cls, string name, string talent, Dictionary<string, string> gear)
        {
            return Wrap("ASB2:", BuildBody(cls, name, talent, gear));
        }

        public static string ExportProfile(IEnumerable<AsBuildsBuild> builds)
        {
            var list = builds == null ? new List<AsBuildsBuild>() : builds.Where(delegate(AsBuildsBuild b) { return b != null && LocalClass(b.Class) != null; }).Take(300).ToList();
            using (var stream = new MemoryStream())
            {
                stream.WriteByte(5);
                PutVar(stream, list.Count);
                foreach (AsBuildsBuild build in list)
                {
                    PutString(stream, Guid.NewGuid().ToString("N"));
                    PutVar(stream, 1);
                    stream.WriteByte(0);
                    PutBytes(stream, BuildBody(build.Class, build.Name, build.Talent, build.Gear));
                    PutString(stream, TalentJson(build.Class, build.Talent));
                }
                return Wrap("ASBP:", stream.ToArray());
            }
        }

        static byte[] BuildBody(string cls, string name, string talent, Dictionary<string, string> gear)
        {
            string externalClass = ExternalClass(cls);
            List<Pick> picks = GearPicks(gear);
            using (var stream = new MemoryStream())
            {
                stream.WriteByte(4);
                PutString(stream, externalClass);
                stream.WriteByte(1);                           // fill is harmless; the site can edit it
                PutString(stream, (name ?? "Imported build").Replace('\r', ' ').Replace('\n', ' '));
                PutVar(stream, picks.Count);
                foreach (Pick pick in picks)
                {
                    // The key hash is authoritative; the position hint may differ between game data revisions.
                    int kind = KindId(pick.Kind);
                    PutVar(stream, kind);
                    int hash = KeyHash(pick.Key);
                    stream.WriteByte((byte)(hash >> 8)); stream.WriteByte((byte)hash);
                }
                stream.WriteByte(4);
                for (int stage = 0; stage < 4; stage++)
                {
                    PutString(stream, "");                       // use the website's current stage label
                    PutVar(stream, picks.Count);
                    for (int p = 0; p < picks.Count; p++)
                    {
                        PutVar(stream, p * 4);                    // main pick; rank/value is not represented locally
                        PutVar(stream, 0);
                    }
                    PutString(stream, "");
                }
                string rankCode = NormalizeTalentCode(talent, cls);
                bool hasTalent = rankCode.Length > 0;
                // No launcher-only armor, star levels, spells, stage notes, or ASBuilder target stats are encoded.
                if (hasTalent || gear != null)
                {
                    PutString(stream, "");                       // image link
                    PutString(stream, "");                       // stage stats / second targets
                    PutString(stream, rankCode);                 // the website's compact talent-rank form
                }
                // Optional launcher metadata follows the standard fields. ASBuilder's reader
                // stops after talent text; launcher users also retain armor selections and stars.
                if (gear != null) PutString(stream, Json.Write(gear.ToDictionary(pair => pair.Key, pair => (object)pair.Value), false));
                return stream.ToArray();
            }
        }

        static List<Pick> GearPicks(Dictionary<string, string> gear)
        {
            var result = new List<Pick>();
            if (gear == null) return result;
            string[] slots = { "Finger 1", "Finger 2", "Trinket 1", "Trinket 2", "Amulet" };
            foreach (string slot in slots)
            {
                string key;
                if (!gear.TryGetValue(slot, out key)) continue;
                Jewel jewel = Items.Jewelry.FirstOrDefault(delegate(Jewel j) { return string.Equals(j.Key, key, StringComparison.OrdinalIgnoreCase); });
                if (jewel == null) continue;
                if (jewel.IsCodex)
                {
                    string spellKey = Talents.AbilityKey(jewel.Spell);
                    if (!string.IsNullOrEmpty(spellKey)) result.Add(new Pick { Kind = "legend", Key = spellKey });
                }
                else if (jewel.Kind == "Ring") result.Add(new Pick { Kind = "ring", Key = jewel.Key });
                else if (jewel.Kind == "Trinket") result.Add(new Pick { Kind = "trinket", Key = jewel.Key });
                else if (jewel.Kind == "Amulet") result.Add(new Pick { Kind = "amulet", Key = jewel.Key });
            }
            return result;
        }

        static int KindId(string kind) { return kind == "ring" ? 1 : kind == "trinket" ? 2 : kind == "amulet" ? 3 : 4; }

        static string NormalizeTalentCode(string code, string cls)
        {
            if (string.IsNullOrWhiteSpace(code)) return "";
            TalentBuild parsed; string error;
            if (TalentBuild.TryParseShareCode(code, cls, out parsed, out error)) return parsed.Save();
            // ASBuilds may know newer talents. Keep recognized ranks and let the launcher ignore unknown keys.
            var known = new Dictionary<string, Talent>(StringComparer.Ordinal);
            foreach (TalentTree tree in Talents.Of(cls)) foreach (Talent talent in tree.Talents) known[talent.Id] = talent;
            var accepted = new List<string>();
            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (string part in code.Split(';'))
            {
                int eq = part.LastIndexOf('=');
                if (eq <= 0) continue;
                string id = part.Substring(0, eq).Trim();
                int rank; Talent t;
                if (!int.TryParse(part.Substring(eq + 1).Trim(), out rank) || rank < 1 || !known.TryGetValue(id, out t) || !seen.Add(id)) continue;
                accepted.Add(id + "=" + Math.Min(rank, t.Ranks));
            }
            TalentBuild normalized = TalentBuild.Parse(string.Join(";", accepted.ToArray()), cls);
            return normalized.Save();
        }

        static string TalentJson(string cls, string talent)
        {
            string normalized = NormalizeTalentCode(talent, cls);
            var values = new List<string>();
            foreach (string part in normalized.Split(';'))
            {
                int eq = part.LastIndexOf('=');
                if (eq <= 0) continue;
                string id = part.Substring(eq + 1);
                int slash = part.IndexOf('/');
                if (slash < 0) continue;
                string key = part.Substring(slash + 1, eq - slash - 1);
                int rank;
                if (int.TryParse(id, out rank)) values.Add("\"" + EscapeJson(key) + "\":" + rank);
            }
            return values.Count == 0 ? "" : "{" + string.Join(",", values.ToArray()) + "}";
        }

        static string ProfileTalentCode(string cls, string json)
        {
            if (string.IsNullOrWhiteSpace(json)) return "";
            Dictionary<string, object> values;
            try { values = Json.AsDict(Json.Parse(json)); }
            catch (Exception) { return ""; }
            if (values == null) return "";
            var ranks = new Dictionary<string, int>(StringComparer.Ordinal);
            foreach (TalentTree tree in Talents.Of(cls))
                foreach (Talent talent in tree.Talents)
                {
                    object value;
                    if (!values.TryGetValue(talent.Key, out value) && !values.TryGetValue(talent.Id, out value)) continue;
                    int rank;
                    if (value is int) rank = (int)value;
                    else if (value is long) rank = (int)(long)value;
                    else if (value is double) rank = (int)(double)value;
                    else if (!int.TryParse(Convert.ToString(value), out rank)) continue;
                    if (rank > 0) ranks[talent.Id] = Math.Min(rank, talent.Ranks);
                }
            var parts = new List<string>();
            foreach (TalentTree tree in Talents.Of(cls))
                foreach (Talent talent in tree.Talents)
                {
                    int rank;
                    if (ranks.TryGetValue(talent.Id, out rank)) parts.Add(talent.Id + "=" + rank);
                }
            return string.Join(";", parts.ToArray());
        }

        static string EscapeJson(string value) { return (value ?? "").Replace("\\", "\\\\").Replace("\"", "\\\""); }

        static bool TryUnwrap(string text, string prefix, bool profile, out byte[] body, out string error)
        {
            body = null;
            error = "The ASBuilds code is damaged or incomplete.";
            string value = Regex.Replace(text.Substring(prefix.Length), @"\s", "");
            int checksumLength = 2;
            if (value.Length <= checksumLength) return false;
            string checksum = value.Substring(0, checksumLength), encoded = value.Substring(checksumLength);
            if (Checksum(encoded) != checksum) { error = "The ASBuilds code checksum does not match."; return false; }
            try
            {
                string base64 = encoded.Replace('-', '+').Replace('_', '/');
                if (base64.Length % 4 == 1) return false;
                base64 = base64.PadRight(base64.Length + (4 - base64.Length % 4) % 4, '=');
                body = Convert.FromBase64String(base64);
                return true;
            }
            catch (FormatException) { return false; }
        }

        static string Wrap(string prefix, byte[] body)
        {
            string encoded = Convert.ToBase64String(body).TrimEnd('=').Replace('+', '-').Replace('/', '_');
            return prefix + Checksum(encoded) + encoded;
        }

        static string Checksum(string text)
        {
            int a = 1, b = 0;
            foreach (byte value in Encoding.ASCII.GetBytes(text))
            {
                a = (a + value) % 251;
                b = (b + a) % 251;
            }
            int n = (a * 251 + b) % 4096;
            return new string(new[] { Alphabet[n / 64], Alphabet[n % 64] });
        }

        static int KeyHash(string key)
        {
            int hash = 7;
            foreach (byte value in Encoding.UTF8.GetBytes(key ?? "")) hash = (hash * 31 + value) & 65535;
            return hash;
        }

        static void PutVar(Stream stream, int value)
        {
            uint n = (uint)Math.Max(0, value);
            while (n >= 128) { stream.WriteByte((byte)(128 + n % 128)); n /= 128; }
            stream.WriteByte((byte)n);
        }
        static void PutBytes(Stream stream, byte[] value)
        {
            value = value ?? new byte[0];
            PutVar(stream, value.Length);
            stream.Write(value, 0, value.Length);
        }
        static void PutString(Stream stream, string value) { PutBytes(stream, Encoding.UTF8.GetBytes(value ?? "")); }
    }
}
