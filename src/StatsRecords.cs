// Stats from Azeroth Stats' run history (the History list, either addon):
//   Personal records: each class's best run by fastest victory, highest level, most kills and best DPS, solo and co-op apart.
//   Solo and co-op: the same averages for runs alone and runs in a party, so party runs (more enemies, a card picked for you
//   after 2 minutes) do not pull your solo numbers down, or the other way round.
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;

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

        bool statsClassesOpen = true;                         // the Stats tree shows each class under Classes

        // The Stats pages as a tree on the right, styled like the New Player Handbook's: Overview ... Build Journal, with the
        // classes under Classes. A click, a wheel notch or the controller (right stick and A) moves through it, and the page
        // slides up or down to the one chosen.
        void BuildStatsSidebar(RunStats st, int top, int width)
        {
            float zoom = Theme.Zoom;
            Font regular = Theme.F(7f, FontStyle.Regular), bold = Theme.F(7f, FontStyle.Bold);
            var nav = new GuideContentsPanel { Bounds = new Rectangle(PW - width, top, width, Math.Max(1, PH - top - 12)),
                                               BackColor = panel.BackColor, HideBars = true };
            controllerTree = nav;
            panel.Controls.Add(nav);
            nav.BringToFront();

            // the entries in reading order: (tab, class key or null, label, level)
            var entries = new List<object[]>();
            for (int t = 0; t < StatTabs.Length; t++)
            {
                entries.Add(new object[] { t, null, StatTabs[t], 0 });
                if (t == 1 && st != null && statsClassesOpen)
                    foreach (var c in st.Classes) entries.Add(new object[] { t, c.Key, c.Name, 1 });
            }
            int currentEntry = entries.FindIndex(delegate(object[] e) { return (int)e[0] == statsTab && (string)e[1] == null; });

            Action<int> go = delegate(int index)
            {
                if (index < 0 || index >= entries.Count) return;
                int tab = (int)entries[index][0];
                string key = (string)entries[index][1];
                int direction = index >= currentEntry ? 1 : -1;
                if (!IsDisposed && IsHandleCreated)
                    BeginInvoke((MethodInvoker)delegate
                    {
                        if (IsDisposed) return;
                        if (key != null) SlidePanel(direction, delegate { SetStatsView(key, statsTab == 0 ? 1 : statsTab); }, false);
                        else SlidePanel(direction, delegate { SetStatsView(statsClass, tab); }, false);
                    });
            };
            nav.SectionStep = delegate(int direction)
            {
                // the wheel steps through the pages themselves (the classes are reached with a click or the controller)
                int tab = Math.Max(0, Math.Min(StatTabs.Length - 1, statsTab + direction));
                if (tab == statsTab) return;
                go(entries.FindIndex(delegate(object[] e) { return (int)e[0] == tab && (string)e[1] == null; }));
            };
            controllerTreeFold = delegate(bool show)
            {
                if (nav.IsDisposed || statsClassesOpen == show) return;
                statsClassesOpen = show;
                if (!IsDisposed && IsHandleCreated) BeginInvoke((MethodInvoker)delegate { if (!IsDisposed && current == "Stats") BuildPanel("Stats"); });
            };

            int y = 8, indent = Math.Max(8, (int)(9 * zoom));
            Label currentLink = null;
            for (int i = 0; i < entries.Count; i++)
            {
                int index = i, level = (int)entries[i][3];
                string key = (string)entries[i][1];
                bool isCurrent = i == currentEntry;
                bool isClass = key != null && string.Equals(key, statsClass, StringComparison.OrdinalIgnoreCase);
                string text = Lang.T((string)entries[i][2]);
                int left = level == 0 ? indent : 2 * indent;
                int labelWidth = Math.Max(1, width - left);
                int labelHeight = Math.Max(15, TextRenderer.MeasureText(text, bold, new Size(labelWidth, 100),
                                                                        TextFormatFlags.WordBreak | TextFormatFlags.NoPadding).Height + 2);
                var link = new GuideContentsLabel(bold) { Text = text, AutoSize = false, Height = labelHeight, Width = labelWidth,
                    Left = left, Top = y, UseMnemonic = false, AutoEllipsis = false, TextAlign = ContentAlignment.TopLeft,
                    BackColor = isCurrent ? Theme.Hex("#382914") : isClass ? Theme.Hex("#241e19") : Color.Transparent,
                    ForeColor = isCurrent ? Theme.Hex("#fff2b0") : isClass ? Theme.Hex("#e9bd62") : key != null ? ClassColor(key) : Theme.GoldDim,
                    Cursor = Cursors.Hand, Font = isCurrent || isClass ? bold : regular, AccessibleRole = AccessibleRole.Link };
                Color rest = link.BackColor;
                link.Click += delegate { go(index); };
                link.MouseEnter += delegate { link.BackColor = Theme.Hex("#28212b"); };
                link.MouseLeave += delegate { link.BackColor = rest; };
                nav.Controls.Add(link);
                if (level == 0 && (int)entries[i][0] == 1 && st != null && st.Classes.Count > 0)
                {
                    // Classes' arrow, as in the Wiki tree: open shows each class beneath it
                    var arrow = new Label { Text = statsClassesOpen ? "\u25be" : "\u25b8", AutoSize = false,
                        Bounds = new Rectangle(0, y, indent, labelHeight), Font = Theme.F(7.0f),
                        ForeColor = Theme.Gold, BackColor = Color.Transparent, Cursor = Cursors.Hand, TextAlign = ContentAlignment.TopLeft };
                    arrow.Click += delegate { if (controllerTreeFold != null) controllerTreeFold(!statsClassesOpen); };
                    nav.Controls.Add(arrow);
                }
                if (isCurrent) currentLink = link;
                bool nextIsChild = i + 1 < entries.Count && (int)entries[i + 1][3] > 0;
                y += labelHeight + (nextIsChild ? Math.Max(3, (int)(3 * zoom)) : Math.Max(7, (int)(7 * zoom)));
            }
            nav.AutoScrollMinSize = new Size(0, y + 6);
            if (currentLink != null && currentLink.Bottom > nav.ClientSize.Height) nav.ScrollTo(currentLink.Top - 20);
        }
    }
}
