// Stats from Azeroth Stats' run history (the History list, either addon):
//   Personal records: each class's best run by fastest victory, highest level, most kills and best DPS, solo and co-op apart.
//   Solo and co-op: the same averages for runs alone and runs in a party, so party runs (more enemies, a card picked for you
//   after 2 minutes) do not pull your solo numbers down, or the other way round.
using System;
using System.Collections.Generic;
using System.Drawing;

namespace AzerothLauncher
{
    public partial class LauncherApp
    {
        // a run played with others: the addon saw a party
        static bool CoopRun(StatRun r) { return r.Party.Length > 0 || r.PartySnapshot.Count > 0; }

        sealed class RunRecord { public string Label, Value, Sub; }

        // the class's (or everyone's) records from its runs
        RowsBlock PersonalRecords(List<StatRun> runs, string title)
        {
            var rows = new RowsBlock();
            rows.Title = Lang.T(title);
            rows.Empty = Lang.T("Play a few runs with Azeroth Stats to see your records here.");
            var byClass = new Dictionary<string, List<StatRun>>(StringComparer.OrdinalIgnoreCase);
            var names = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (StatRun r in runs)
            {
                string key = r.ClassKey.Length > 0 ? r.ClassKey : "?";
                List<StatRun> list;
                if (!byClass.TryGetValue(key, out list)) { list = new List<StatRun>(); byClass[key] = list; }
                list.Add(r);
                if (r.Class.Length > 0) names[key] = r.Class;
            }
            var keys = new List<string>(byClass.Keys);
            keys.Sort(StringComparer.OrdinalIgnoreCase);
            foreach (string key in keys)
            {
                foreach (bool coop in new[] { false, true })
                {
                    StatRun fastest = null, highest = null, kills = null, dps = null;
                    foreach (StatRun r in byClass[key])
                    {
                        if (CoopRun(r) != coop) continue;
                        if (r.Kind == "victory" && r.Time > 0 && (fastest == null || r.Time < fastest.Time)) fastest = r;
                        if (r.Level > 0 && (highest == null || r.Level > highest.Level)) highest = r;
                        if (r.Kills > 0 && (kills == null || r.Kills > kills.Kills)) kills = r;
                        if (r.Dps > 0 && (dps == null || r.Dps > dps.Dps)) dps = r;
                    }
                    if (fastest == null && highest == null && kills == null && dps == null) continue;
                    string name;
                    if (!names.TryGetValue(key, out name)) name = key;
                    var row = new ArmoryRow();
                    row.Name = name + " · " + Lang.T(coop ? "Co-op" : "Solo");
                    row.ClassPic = key != "?" ? key : null;
                    row.Icon = Armory.Initials(name);
                    row.IconColor = key != "?" ? ClassColor(key) : Theme.Hex("#ffd100");
                    row.NameColor = row.IconColor;
                    var parts = new List<string>();
                    if (fastest != null) parts.Add(Lang.F("Fastest victory {0}", Clock(fastest.Time)));
                    if (highest != null) parts.Add(Lang.F("Highest level {0}", Big(highest.Level)));
                    if (kills != null) parts.Add(Lang.F("Most kills {0}", Big(kills.Kills)));
                    row.Sub = string.Join(" · ", parts.ToArray());
                    row.SubColor = Armory.Bright;
                    row.Value = dps != null ? Big(dps.Dps) + " DPS" : "-";
                    row.ValueSub = dps != null && dps.Date.Length >= 10 ? dps.Date.Substring(0, 10) : Lang.T("best DPS");
                    rows.Rows.Add(row);
                }
            }
            return rows;
        }

        // solo against co-op: how your runs go alone and in a party
        TilesBlock SoloAndCoop(List<StatRun> runs)
        {
            double[] solo = new double[5], party = new double[5];   // runs, wins, time, level, dps (dps runs counted apart)
            double soloDpsRuns = 0, partyDpsRuns = 0;
            foreach (StatRun r in runs)
            {
                bool coop = CoopRun(r);
                double[] t = coop ? party : solo;
                t[0]++;
                if (r.Kind == "victory") t[1]++;
                t[2] += r.Time;
                t[3] += r.Level;
                if (r.Dps > 0) { t[4] += r.Dps; if (coop) partyDpsRuns++; else soloDpsRuns++; }
            }
            var tiles = new TilesBlock();
            tiles.Title = Lang.T("Solo and co-op runs");
            tiles.Items.Add(new[] { Big(solo[0]) + " / " + Big(party[0]), Lang.T("Runs (solo / co-op)") });
            tiles.Items.Add(new[] { Pct(solo[1], solo[0]) + "% / " + Pct(party[1], party[0]) + "%", Lang.T("Win rate (solo / co-op)") });
            tiles.Items.Add(new[] { (solo[0] > 0 ? Clock(solo[2] / solo[0]) : "-") + " / " + (party[0] > 0 ? Clock(party[2] / party[0]) : "-"),
                                    Lang.T("Average run (solo / co-op)") });
            tiles.Items.Add(new[] { (solo[0] > 0 ? Big(Math.Round(solo[3] / solo[0])) : "-") + " / " + (party[0] > 0 ? Big(Math.Round(party[3] / party[0])) : "-"),
                                    Lang.T("Average level (solo / co-op)") });
            tiles.Items.Add(new[] { (soloDpsRuns > 0 ? Big(Math.Round(solo[4] / soloDpsRuns)) : "-") + " / " + (partyDpsRuns > 0 ? Big(Math.Round(party[4] / partyDpsRuns)) : "-"),
                                    Lang.T("Average DPS (solo / co-op)") });
            return tiles;
        }
    }
}
