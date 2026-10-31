// A click on a run in STATS > History: a short post-game summary of it, with a few fun highlights (what the run leaned
// on, its best moments, and how it compares with your other runs of that class).
using System;
using System.Collections.Generic;
using System.Globalization;

namespace AzerothLauncher
{
    public partial class LauncherApp
    {
        static string RsN(double v) { return Math.Round(v).ToString("#,0", CultureInfo.InvariantCulture); }
        static string RsClk(double s) { int t = (int)Math.Round(s); return (t / 60) + ":" + (t % 60).ToString("00"); }

        public void RunSummary(StatRun r)
        {
            var l = new List<string>();
            string result = r.Kind == "victory" ? Lang.T("Victory") : r.Kind == "defeat" ? Lang.T("Defeat") :
                            r.Kind == "left" ? Lang.T("Left early") : r.Kind == "interrupted" ? Lang.T("Interrupted") :
                            r.Kind == "ended" ? Lang.T("Ended") : r.Kind.Length > 0 ? r.Kind : Lang.T("Observed run");
            l.Add(result + " · " + r.Class + (r.Race.Length > 0 ? " (" + r.Race + ")" : "") + (r.Zone.Length > 0 ? " · " + r.Zone : ""));
            if (r.Date.Length > 0) l.Add(r.Date + (r.Party.Length > 0 ? "  ·  " + Lang.T("with") + " " + r.Party : ""));
            if (r.Status.Length > 0) l.Add(Lang.T("Observed status") + ": " + r.Status + (r.Phase.Length > 0 ? " · " + r.Phase : "") + (r.Partial ? " · " + Lang.T("Partial observation") : ""));
            if (r.HeroicKnown) l.Add(r.Heroic ? Lang.T("Heroic") : Lang.T("Normal"));
            if (r.Affixes.Length > 0)                         // the run's Heroic affixes (game names, "sanguine,explosive")
            {
                var ax = new List<string>();
                foreach (string a in r.Affixes.Split(','))
                {
                    string s = a.Trim();
                    if (s.Length > 0) ax.Add(char.ToUpperInvariant(s[0]) + s.Substring(1));
                }
                if (ax.Count > 0) l.Add(Lang.T("Heroic affixes") + ": " + string.Join(", ", ax.ToArray()));
            }
            l.Add("");
            if (!r.AzerothStatsSource)
                l.Add(Lang.F("Survived {0} · level {1} · {2} kills · {3} DPS", RsClk(r.Time), RsN(r.Level), RsN(r.Kills), RsN(r.Dps)));
            else
            {
                var observed = new List<string>();
                if (r.ObservedValues.ContainsKey("duration")) observed.Add(Lang.T("Time " + RsClk(r.Time)));
                if (r.ObservedValues.ContainsKey("level")) observed.Add(Lang.T("Level " + RsN(r.Level)));
                if (r.ObservedValues.ContainsKey("kills")) observed.Add(Lang.T(RsN(r.Kills) + " kills"));
                if (r.Dps > 0) observed.Add(Lang.T(RsN(r.Dps) + " peak run-average DPS"));
                if (observed.Count > 0) l.Add(string.Join(" · ", observed.ToArray()));
            }
            var bits = new List<string>();
            if (r.Bosses > 0 || (r.AzerothStatsSource && r.ObservedValues.ContainsKey("bosses"))) bits.Add(Lang.F("{0} bosses", RsN(r.Bosses)));
            if (r.Elites > 0 || (r.AzerothStatsSource && r.ObservedValues.ContainsKey("elites"))) bits.Add(Lang.F("{0} elites", RsN(r.Elites)));
            if (r.Guardians > 0 || (r.AzerothStatsSource && r.ObservedValues.ContainsKey("guardians"))) bits.Add(Lang.F("{0} guardians", RsN(r.Guardians)));
            if (r.Gold > 0 || (r.AzerothStatsSource && r.ObservedValues.ContainsKey("gold"))) bits.Add(Lang.F("{0} gold", RsN(r.Gold)));
            if (r.Chests > 0 || (r.AzerothStatsSource && r.ObservedValues.ContainsKey("chests"))) bits.Add(Lang.F("{0} caches", RsN(r.Chests)));
            if (r.Magnets > 0 || (r.AzerothStatsSource && r.ObservedValues.ContainsKey("magnets"))) bits.Add(Lang.F("{0} magnets", RsN(r.Magnets)));
            if (bits.Count > 0) l.Add(string.Join(" · ", bits.ToArray()));
            if (r.Damage > 0 || (r.AzerothStatsSource && r.ObservedValues.ContainsKey("damage")))
                l.Add(Lang.F("{0} damage dealt", RsN(r.Damage)) + (r.Taken > 0 || (r.AzerothStatsSource && r.ObservedValues.ContainsKey("taken")) ? " · " + Lang.F("{0} taken", RsN(r.Taken)) : ""));
            if (r.Healing > 0 || (r.AzerothStatsSource && r.ObservedValues.ContainsKey("healing"))) l.Add(Lang.F("{0} healing done", RsN(r.Healing)));
            AddSources(l, "Damage sources", r.DamageSources);
            AddSources(l, "Healing sources", r.HealingSources);
            AddSources(l, "Damage taken sources", r.TakenSources);
            if (r.Observations.Count > 0)
            {
                l.Add("");
                l.Add(Lang.T("Observed build journal"));
                for (int i = 0; i < Math.Min(12, r.Observations.Count); i++)
                    l.Add("  • " + r.Observations[i].Key + ": " + r.Observations[i].Value);
                if (r.Observations.Count > 12) l.Add(Lang.T("More entries are available on the Build Journal tab."));
            }
            if (r.PartySnapshot.Count > 0)
            {
                l.Add("");
                l.Add(Lang.T("Observed party sample"));
                foreach (var member in r.PartySnapshot)
                    l.Add("  • " + member.Name + (member.Class.Length > 0 ? " · " + member.Class : "") +
                          (member.Damage > 0 ? " · " + RsN(member.Damage) + " damage" : "") +
                          (member.Dps > 0 ? " · " + RsN(member.Dps) + " DPS" : "") +
                          (member.First.Length > 0 ? " · top: " + member.First : ""));
            }

            // the fun part
            var fun = new List<string>();
            if (r.Time >= 60 && r.Kills > 0) fun.Add(Lang.F("{0} kills a minute", RsN(r.Kills / (r.Time / 60))));
            double v;
            if (r.Highs.TryGetValue("crit", out v) && v > 0) fun.Add(Lang.F("Biggest crit: {0}", RsN(v)));
            if (r.Highs.TryGetValue("heal", out v) && v > 0) fun.Add(Lang.F("Biggest heal: {0}", RsN(v)));
            if (r.Highs.TryGetValue("burst5", out v) && v > 0) fun.Add(Lang.F("Most kills in 5 seconds: {0}", RsN(v)));
            if (r.Legendary > 0) fun.Add(Lang.F("Legendaries picked up: {0}", RsN(r.Legendary)));
            if (r.Saves > 0) fun.Add(Lang.F("Level-ups that saved you: {0}", RsN(r.Saves)));
            // what the run leaned on: each spell's share of the ranks put into spells
            if (r.Abilities.Count > 0)
            {
                double ranks = 0;
                foreach (var a in r.Abilities) ranks += Math.Max(1, a.Picks);
                var by = new List<AbilityStat>(r.Abilities);
                by.Sort(delegate(AbilityStat a, AbilityStat b) { return b.Picks.CompareTo(a.Picks); });
                var parts = new List<string>();
                for (int i = 0; i < Math.Min(4, by.Count); i++) parts.Add(by[i].Name + " " + RsN(Math.Max(1, by[i].Picks) * 100 / ranks) + "%");
                fun.Add(Lang.T("Spell focus") + ": " + string.Join(", ", parts.ToArray()));
                AbilityStat top = null;
                foreach (var a in r.Abilities) if (a.Damage > 0 && (top == null || a.Damage > top.Damage)) top = a;
                if (top != null && r.Damage > 0) fun.Add(Lang.F("{0} did {1}% of the damage", top.Name, RsN(top.Damage * 100 / r.Damage)));
            }
            // compared with your other runs of this class
            try
            {
                RunStats st = AddonStats();
                if (st != null)
                {
                    int faster = 0, same = 0, better = 0;
                    foreach (var o in st.History)
                    {
                        if (o == r || o.ClassKey != r.ClassKey) continue;
                        same++;
                        if (o.Dps < r.Dps) better++;
                        if (o.Time < r.Time) faster++;
                    }
                    if (same > 0 && better == same) fun.Add(Lang.F("Your best DPS of your last {0} {1} runs", same + 1, r.Class));
                    else if (same > 0 && faster == same) fun.Add(Lang.F("Your longest of your last {0} {1} runs", same + 1, r.Class));
                }
            }
            catch (Exception) { }
            if (r.Killer.Length > 0) fun.Add(Lang.F("Slain by {0}", r.Killer));
            if (fun.Count > 0)
            {
                l.Add("");
                l.Add(Lang.T("Highlights"));
                foreach (string f in fun) l.Add("  • " + f);
            }
            Info(Lang.T("Run summary"), string.Join("\n", l.ToArray()));
        }

        static void AddSources(List<string> lines, string label, List<KeyValuePair<string, double>> sources)
        {
            if (sources == null || sources.Count == 0) return;
            lines.Add("");
            lines.Add(Lang.T(label));
            for (int i = 0; i < Math.Min(5, sources.Count); i++)
                lines.Add("  • " + sources[i].Key + " · " + RsN(sources[i].Value));
        }
    }
}
