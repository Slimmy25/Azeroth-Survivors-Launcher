// The Gear Planner (Guide > Theorycrafting): pick a class, put gear and jewelry in the slots of a character sheet laid
// out like the game's own (C), and see what they add: the sheet's stats and the Run Stats panel beside it.
// The numbers are the bonuses added together (Items.cs), with the 4-piece bonus of every set worn at 4 pieces of 1+ stars
// (two sets of four give both). A click on a piece adds a star (5 at most, then back to 0): stars decide the set bonuses
// reached; what a star adds to a piece is not known yet, so it is not counted. A piece whose bonuses are not
// known yet adds nothing.
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace AzerothLauncher
{
    public partial class LauncherApp
    {
        public const string GearPlanner = "Gear Planner";
        bool gPlanner;                                        // the Gear Planner is open
        string theorycraftClass;                              // one in-session class shared by the Talent Builder and Gear Planner
        string plannerClass;                                  // the class currently shown by the Gear Planner
        readonly Dictionary<string, object> plannerSlots = new Dictionary<string, object>();   // slot -> GearPiece or Jewel

        void GuideGearPlanner()
        {
            GuideHeader(GearPlanner, Data.Guide);
            AddPeelToolSwitch();
            if (plannerClass == null)
            {
                PlannerClass("Warrior");
            }
            // Match the guide passage's compact inset. PW already stops before the favorites rail.
            int inset = 4;
            int top = inset, w = PW - inset * 2;
            var exportCode = new WowButton("EXPORT ASBUILDS", delegate
            {
                try
                {
                    TalentBuild talents = liveBuild != null && string.Equals(liveBuildCls, plannerClass, StringComparison.OrdinalIgnoreCase)
                        ? liveBuild : TalentBuild.Parse("", plannerClass);
                    Clipboard.SetText(AsBuildsInterop.ExportBuild(plannerClass, "Gear Plan", talents.Save(), CapturePlannerJewelry()));
                    Info("Gear Planner", "ASBuilds build code copied. It includes this jewelry and the current talent build.");
                }
                catch (Exception) { Warn("Gear Planner", "Could not copy the ASBuilds code."); }
            }, 144, 26, 8);
            exportCode.Location = new Point(20, top);
            if (SHOW_TEMPORARY_BUILD_MENUS) panel.Controls.Add(exportCode);
            var importCode = new WowButton("IMPORT ASBUILDS", delegate
            {
                string initial = "";
                try { if (Clipboard.ContainsText()) initial = Clipboard.GetText(); } catch (Exception) { }
                using (var prompt = new TalentBuildImportDialog(initial))
                {
                    if (prompt.ShowModal(panel.FindForm()) != DialogResult.OK || prompt.Code.Length == 0) return;
                    List<AsBuildsBuild> imported;
                    string error;
                    if (!AsBuildsInterop.TryRead(prompt.Code, out imported, out error)) { Warn("IMPORT BUILD", error); return; }
                    List<AsBuildsBuild> stored = StoreAsBuildsBuilds(Config.Load(), imported);
                    if (stored.Count == 0) { Warn("IMPORT BUILD", "The profile did not contain a supported build."); return; }
                    AsBuildsBuild first = stored[0];
                    ApplyPlannerJewelry(first.Class, first.Gear);
                    pendingTalentBuildKey = first.Class + "|" + first.Name;
                    BuildPanel("Guide");
                    Info("Gear Planner", stored.Count == 1
                        ? "Build imported. Its jewelry is loaded here; its talent build is saved in the Talent Builder."
                        : stored.Count + " builds imported. The first build's jewelry is loaded here; all talent builds are saved in the Talent Builder.");
                }
            }, 144, 26, 8);
            importCode.Location = new Point(170, top);
            if (SHOW_TEMPORARY_BUILD_MENUS) panel.Controls.Add(importCode);
            // the sheet fits the box: its slots are sized to the room there is, so nothing scrolls
            int sheetTop = top + (SHOW_TEMPORARY_BUILD_MENUS ? 32 : 0);
            int h = PH - inset - sheetTop;
            var sheet = new GearSheet(plannerClass, plannerSlots, GearIcons(), h);
            sheet.Bounds = new Rectangle(inset, sheetTop, w, h);
            panel.Controls.Add(sheet);
            var stats = AddTheoryStats(plannerClass);
            var insideStats = new GearSheet(plannerClass, plannerSlots, GearIcons());
            insideStats.StatsOnly = true;
            int mainWidth = w - TheoryStatsWidth - 6;
            insideStats.Bounds = new Rectangle(inset + 55, sheetTop + 12 + Theme.F(13, FontStyle.Bold).Height, Math.Max(90, mainWidth - 110), 29 + 6 * (Theme.F(7.5f).Height + 2));
            panel.Controls.Add(insideStats);
            insideStats.BringToFront();
            sheet.StatsChanged = delegate { insideStats.Invalidate(); };
            AddTheoryClassSelector(plannerClass);
        }

        int TheoryStatsHeight { get { return GearSheet.BuildControlsHeight; } }
        int TheoryStatsWidth { get { return Math.Max(110, (PW - 8) * 22 / 100); } }

        Control AddTheoryStats(string cls)
        {
            var box = new Bordered(Theme.GoldDim, 1);
            box.BackColor = Theme.Card;
            box.Bounds = new Rectangle(PW - 4 - TheoryStatsWidth, PH - 4 - TheoryStatsHeight, TheoryStatsWidth, TheoryStatsHeight);
            var saved = new ComboBox { DropDownStyle = ComboBoxStyle.DropDown, Font = Theme.F(8),
                BackColor = Theme.Card, ForeColor = Theme.Text, Bounds = new Rectangle(5, 5, box.Width - 10, 22),
                AccessibleName = "Saved builds: choose a save or type a name" };
            Action refresh = delegate
            {
                saved.Items.Clear();
                foreach (var choice in SavedTalentBuilds(Config.Load())) if (choice.Class == cls) saved.Items.Add(choice);
            };
            refresh();
            box.Controls.Add(saved);
            int gap = 4, bw = (box.Width - 10 - gap) / 2;
            int y = box.Height - 53;
            var save = new WowButton("SAVE", delegate
            {
                var selected = saved.SelectedItem as TalentBuildChoice;
                string name = selected != null && saved.Text == selected.ToString() ? selected.Name : saved.Text.Trim();
                if (name.Length == 0) { saved.Focus(); return; }
                var cfg = Config.Load();
                SavedTalentBuilds(cfg);
                var rows = Json.AsList(Json.Get(cfg.Data, "talentTreeBuildsShared")) ?? new List<object>();
                var entry = new Dictionary<string, object>();
                entry["name"] = name; entry["class"] = cls;
                entry["build"] = liveBuild != null && liveBuildCls == cls ? liveBuild.Save() : "";
                entry["gear"] = StoredBuildGear(CapturePlannerJewelry());
                int at = rows.FindIndex(delegate(object value)
                {
                    var row = Json.AsDict(value);
                    return row != null && string.Equals(Json.Str(row, "class"), cls, StringComparison.OrdinalIgnoreCase) &&
                        string.Equals(Json.Str(row, "name"), name, StringComparison.OrdinalIgnoreCase);
                });
                if (at < 0) rows.Add(entry); else rows[at] = entry;
                cfg.Set("talentTreeBuildsShared", rows); cfg.Save();
                refresh();
                foreach (TalentBuildChoice choice in saved.Items) if (choice.Class == cls && choice.Name == name) { saved.SelectedItem = choice; break; }
            }, bw, 22, 7.5f);
            save.Location = new Point(5, y); box.Controls.Add(save);
            var load = new WowButton("LOAD", delegate
            {
                var choice = saved.SelectedItem as TalentBuildChoice;
                if (choice == null) return;
                ApplyPlannerJewelry(choice.Class, choice.Gear);
                liveBuild = TalentBuild.Parse(choice.Code, choice.Class); liveBuildCls = choice.Class;
                if (gBuilder) gTalents = choice.Class;
                BuildPanel("Guide");
            }, bw, 22, 7.5f);
            load.Location = new Point(5 + bw + gap, y); box.Controls.Add(load);
            var export = new WowButton("EXPORT", delegate { ShowTheoryCodeOverlay(cls, true); }, bw, 22, 7.5f);
            export.Location = new Point(5, y + 26); box.Controls.Add(export);
            var import = new WowButton("IMPORT", delegate { ShowTheoryCodeOverlay(cls, false); }, bw, 22, 7.5f);
            import.Location = new Point(5 + bw + gap, y + 26); box.Controls.Add(import);
            panel.Controls.Add(box); box.BringToFront();
            return box;
        }

        void ShowTheoryCodeOverlay(string cls, bool exporting)
        {
            var overlay = new Bordered(Theme.GoldDim, 1) { BackColor = Theme.Card,
                Bounds = new Rectangle(8, 8, PW - 16, PH - 16) };
            var title = UI.Text(exporting ? "EXPORT BUILD" : "IMPORT BUILD", 11, FontStyle.Bold, Theme.Gold, Theme.Card, overlay.Width - 24);
            title.Location = new Point(12, 10); overlay.Controls.Add(title);
            var errorLabel = UI.Text("", 8, FontStyle.Regular, Theme.Gold, Theme.Card, overlay.Width - 24);
            errorLabel.Bounds = new Rectangle(12, 34, overlay.Width - 24, 32); overlay.Controls.Add(errorLabel);
            var input = new TextBox { Multiline = true, WordWrap = true, ScrollBars = ScrollBars.Vertical,
                ReadOnly = exporting, BackColor = Theme.Card, ForeColor = Theme.Text, Font = Theme.F(9),
                Bounds = new Rectangle(12, 70, overlay.Width - 24, Math.Max(30, overlay.Height - 122)),
                AccessibleName = exporting ? "ASBuilder compatible export code" : "Paste build code" };
            if (exporting)
            {
                string ranks = liveBuild != null && liveBuildCls == cls ? liveBuild.Save() : "";
                input.Text = AsBuildsInterop.ExportBuild(cls, "Build", ranks, CapturePlannerJewelry());
            }
            overlay.Controls.Add(input);
            int buttonWidth = Math.Min(125, (overlay.Width - 36) / 2);
            var close = new WowButton(exporting ? "CLOSE" : "CANCEL", delegate { overlay.Dispose(); }, buttonWidth, 28, 9);
            close.Location = new Point(12, overlay.Height - 40); overlay.Controls.Add(close);
            var action = new WowButton(exporting ? "COPY CODE" : "IMPORT", delegate
            {
                if (exporting)
                {
                    try { Clipboard.SetText(input.Text); errorLabel.Text = Lang.T("Code copied."); }
                    catch (Exception) { errorLabel.Text = Lang.T("Could not copy the build code."); }
                    return;
                }
                List<AsBuildsBuild> builds; string error;
                if (!AsBuildsInterop.TryRead(input.Text.Trim(), out builds, out error)) { errorLabel.Text = error; return; }
                var stored = StoreAsBuildsBuilds(Config.Load(), builds);
                if (stored.Count == 0) return;
                var first = stored[0];
                ApplyPlannerJewelry(first.Class, first.Gear);
                liveBuild = TalentBuild.Parse(first.Talent, first.Class); liveBuildCls = first.Class;
                if (gBuilder) gTalents = first.Class;
                overlay.Dispose();
                BuildPanel("Guide");
            }, buttonWidth, 28, 9);
            action.Location = new Point(overlay.Width - 12 - buttonWidth, overlay.Height - 40); overlay.Controls.Add(action);
            panel.Controls.Add(overlay); overlay.BringToFront(); input.Focus();
            if (exporting) input.SelectAll();
        }

        void AddTheorySelector(string cls, Control stats)
        {
            var selector = new GearSheet(cls, plannerSlots, GearIcons());
            selector.SelectorOnly = true;
            selector.Bounds = new Rectangle(PW - 4 - TheoryStatsWidth, 4, TheoryStatsWidth, PH - 8);
            selector.StatsChanged = delegate { stats.Invalidate(); };
            panel.Controls.Add(selector);
            selector.BringToFront();
            stats.BringToFront();
        }

        readonly Dictionary<string, Dictionary<string, object>> classGearDrafts = new Dictionary<string, Dictionary<string, object>>();
        readonly Dictionary<string, Dictionary<string, int>> classStarDrafts = new Dictionary<string, Dictionary<string, int>>();
        readonly Dictionary<string, string> classTalentDrafts = new Dictionary<string, string>();

        void AddTheoryClassSelector(string cls)
        {
            int mainWidth = PW - 8 - TheoryStatsWidth - 6;
            var menu = ThemeMenus.Create(true);
            var picker = new WowButton(Lang.T(cls) + "  ▾", null, Math.Min(145, mainWidth / 2), 25, 9);
            picker.AccessibleName = Lang.T("Build class");
            picker.Location = new Point(4 + (mainWidth - picker.Width) / 2, 7);
            foreach (string item in Items.Classes)
            {
                string selected = item;
                var option = new ToolStripMenuItem(Lang.T(item));
                option.Checked = item == cls;
                option.Click += delegate
                {
                    menu.Close();
                    if (selected == plannerClass) return;
                    PlannerClass(selected);
                    if (gBuilder) gTalents = selected;
                    BuildPanel("Guide");
                };
                menu.Items.Add(option);
            }
            picker.Pressed += delegate { if (menu.Visible) menu.Close(); else menu.Show(picker, new Point(0, picker.Height)); };
            var owner = panel.FindForm();
            EventHandler dismiss = delegate { if (!menu.IsDisposed) menu.Close(); };
            if (owner != null) owner.Deactivate += dismiss;
            picker.Disposed += delegate
            {
                if (owner != null) owner.Deactivate -= dismiss;
                menu.Dispose();
            };
            panel.Controls.Add(picker); picker.BringToFront();
            if (theoryPeel != null && !theoryPeel.IsDisposed) theoryPeel.BringToFront();
        }

        void PlannerClass(string cls)
        {
            if (plannerClass != cls)
            {
                if (plannerClass != null)
                {
                    classGearDrafts[plannerClass] = new Dictionary<string, object>(plannerSlots);
                    classStarDrafts[plannerClass] = new Dictionary<string, int>(GearSheet.Stars);
                }
                if (liveBuild != null && liveBuildCls != null) classTalentDrafts[liveBuildCls] = liveBuild.Save();
                plannerSlots.Clear(); GearSheet.Stars.Clear();
                Dictionary<string, object> gear;
                if (classGearDrafts.TryGetValue(cls, out gear)) foreach (var pair in gear) plannerSlots[pair.Key] = pair.Value;
                Dictionary<string, int> stars;
                if (classStarDrafts.TryGetValue(cls, out stars)) foreach (var pair in stars) GearSheet.Stars[pair.Key] = pair.Value;
                string talents;
                liveBuild = TalentBuild.Parse(classTalentDrafts.TryGetValue(cls, out talents) ? talents : "", cls);
                liveBuildCls = cls;
            }
            plannerClass = theorycraftClass = cls;
        }

        // ASBuilds codes can carry the jewelry shared by both tools. Armor and star ranks stay
        // local because the external code has no matching fields for them.
        Dictionary<string, string> CapturePlannerJewelry()
        {
            var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var slot in plannerSlots)
            {
                GearPiece armor = slot.Value as GearPiece;
                if (armor != null) result["$armor:" + slot.Key] = armor.Name;
                int stars;
                if (GearSheet.Stars.TryGetValue(slot.Key, out stars)) result["$star:" + slot.Key] = stars.ToString();
                Jewel jewel = slot.Value as Jewel;
                if (jewel != null && (slot.Key.StartsWith("Finger", StringComparison.OrdinalIgnoreCase) ||
                    slot.Key.StartsWith("Trinket", StringComparison.OrdinalIgnoreCase) || slot.Key == "Amulet"))
                    result[slot.Key] = jewel.Key;
            }
            return result;
        }

        void ApplyPlannerJewelry(string cls, Dictionary<string, string> gear)
        {
            PlannerClass(cls);
            foreach (string slot in new List<string>(plannerSlots.Keys))
                if (plannerSlots[slot] is Jewel) { plannerSlots.Remove(slot); GearSheet.Stars.Remove(slot); }
            if (gear == null) return;
            if (gear.Keys.Any(key => key.StartsWith("$armor:")))
                foreach (string slot in new List<string>(plannerSlots.Keys))
                    if (plannerSlots[slot] is GearPiece) { plannerSlots.Remove(slot); GearSheet.Stars.Remove(slot); }
            foreach (var pair in gear)
            {
                if (pair.Key.StartsWith("$armor:"))
                {
                    string slot = pair.Key.Substring(7);
                    foreach (var piece in Items.GearOf(cls, slot)) if (piece.Name == pair.Value) { plannerSlots[slot] = piece; break; }
                    continue;
                }
                if (pair.Key.StartsWith("$star:"))
                {
                    int rank;
                    if (int.TryParse(pair.Value, out rank)) GearSheet.Stars[pair.Key.Substring(6)] = Math.Max(0, Math.Min(5, rank));
                    continue;
                }
                Jewel jewel = Items.Jewelry.Find(delegate(Jewel j) { return string.Equals(j.Key, pair.Value, StringComparison.OrdinalIgnoreCase); });
                if (jewel == null || (jewel.IsCodex && !string.Equals(jewel.Class, cls, StringComparison.OrdinalIgnoreCase))) continue;
                if ((pair.Key.StartsWith("Finger", StringComparison.OrdinalIgnoreCase) && jewel.Kind == "Ring") ||
                    (pair.Key.StartsWith("Trinket", StringComparison.OrdinalIgnoreCase) && jewel.Kind == "Trinket") ||
                    (pair.Key == "Amulet" && (jewel.Kind == "Amulet" || jewel.IsCodex)))
                    plannerSlots[pair.Key] = jewel;
            }
        }

        // Clear every transient Gear Planner choice when the user leaves it, including jewelry, stars and the selected slot.
        void ResetPlannerState()
        {
            plannerSlots.Clear();
            classGearDrafts.Clear(); classStarDrafts.Clear(); classTalentDrafts.Clear();
            theorycraftClass = null;
            plannerClass = null;
            GearSheet.ResetView();
            PlannerTotals = null;
        }

        // the gear's real icons (assets\item-icons.txt); a piece that is not a real item shows "Image soon"
        Dictionary<string, string> GearIcons() { return Items.GearIcons; }

        // the gear in the planner, added up: the hover texts of spells and talents show their numbers with it
        public static Dictionary<string, double> PlannerTotals;
    }

    public class GearSheet : Control, IWheel
    {
        public const int BuildControlsHeight = 86;
        readonly RichTextBox bonusView = new RichTextBox {
            ReadOnly = true, BorderStyle = BorderStyle.None, WordWrap = true,
            ScrollBars = RichTextBoxScrollBars.Vertical, DetectUrls = false,
            BackColor = Theme.Hex("#0e0d14"), ForeColor = Theme.Gold,
            TabStop = false, Visible = false
        };
        readonly VScrollBar itemScroll = new VScrollBar();
        int itemOffset;
        string scrollSlot;

        public void Wheel(int delta)
        {
            if (StatsOnly || tierMode || !itemScroll.Visible) return;
            int limit = Math.Max(0, itemScroll.Maximum - itemScroll.LargeChange + 1);
            itemScroll.Value = Math.Max(0, Math.Min(limit, itemScroll.Value - Math.Sign(delta) * 2));
        }

        static Image dpsUpLabel;
        static readonly string[] Left = { "Head", "Neck", "Shoulder", "Back", "Chest", "Shirt", "Tabard", "Wrist" };
        static readonly string[] Right = { "Hands", "Waist", "Legs", "Feet", "Finger 1", "Finger 2", "Trinket 1", "Trinket 2" };
        static readonly string[] Bottom = { "Main Hand", "Off Hand", "Ranged" };
        static readonly HashSet<string> UnusedSlots = new HashSet<string> { "Back", "Shirt", "Tabard", "Off Hand", "Ranged" };
        static readonly HashSet<string> UnusedHunter = new HashSet<string> { "Back", "Shirt", "Tabard", "Off Hand", "Main Hand" };
        static readonly HashSet<string> UnusedDual = new HashSet<string> { "Back", "Shirt", "Tabard", "Ranged" };
        // a hunter's weapon is Ranged; a rogue (two daggers) and a shaman (a shield) also fill the Off Hand
        HashSet<string> Unused { get { return cls == "Hunter" ? UnusedHunter : (cls == "Rogue" || cls == "Shaman") ? UnusedDual : UnusedSlots; } }

        readonly string cls;
        readonly Dictionary<string, object> slots;
        readonly Dictionary<string, string> gearIcons;       // piece name -> icon captured from game data
        readonly Dictionary<string, Rectangle> boxes = new Dictionary<string, Rectangle>();
        readonly List<KeyValuePair<Rectangle, object>> rows = new List<KeyValuePair<Rectangle, object>>();   // the selector's lines
        readonly Font fTitle = Theme.F(13, FontStyle.Bold), fText = Theme.F(9.5f), fBold = Theme.F(9.5f, FontStyle.Bold),
                      fSmall = Theme.F(8.25f), fTier = Theme.F(7f, FontStyle.Bold), fHead = Theme.F(11, FontStyle.Bold);   // made with the section's zoom; cached
        readonly ThemedToolTip tip = new ThemedToolTip();
        int slotSize = Math.Max(26, (int)(44 * Theme.Zoom)), slotGap = Math.Max(4, (int)(7 * Theme.Zoom));
        static string chosenSlot = "Head";                   // the slot the selector shows (kept while the launcher runs)
        string hover;
        Rectangle helpBox;
        int unknownPieces;                                    // "How are gear stats calculated?" above Hands
        bool hoverHelp;
        int hoverRow = -1;
        static readonly object EmptyRow = new object();
        // each slot's stars, 0 to 5 (kept while the launcher runs, like the slots): a click on the piece adds one
        public static readonly Dictionary<string, int> Stars = new Dictionary<string, int>();
        // what Totals found: every set worn (rarity -> pieces worn, at 1+, 3+, 5 stars)
        readonly Dictionary<string, int[]> setsWorn = new Dictionary<string, int[]>();
        static readonly Color Active = Theme.Hex("#40d040");

        static int StarsOf(string slot) { int n; return Stars.TryGetValue(slot, out n) ? n : 0; }

        // the list beside the sheet: Single Item (what the chosen slot can hold) or Tier Bonus (a whole set at once)
        static bool tierMode;
        readonly List<KeyValuePair<Rectangle, bool>> modeBoxes = new List<KeyValuePair<Rectangle, bool>>();
        readonly List<KeyValuePair<Rectangle, string[]>> tierBoxes = new List<KeyValuePair<Rectangle, string[]>>();   // { rarity, "4" or "8" }
        int hoverMode = -1, hoverTier = -1;

        static readonly string[] ArmorSlots = { "Head", "Shoulder", "Chest", "Waist", "Legs", "Feet", "Wrist", "Hands" };

        public static void ResetView()
        {
            Stars.Clear();
            chosenSlot = "Head";
            tierMode = false;
        }

        // a set put in the planner at once: 4 pieces at 1 star (its 4-piece bonus) or all 8 at 3 stars (both bonuses). It is a
        // starting point, not a pile: the whole planner is emptied first (armor, weapons and jewelry, with their stars),
        // then filled with the set picked. Slimjim's call (2026-10-28): a Tier Bonus button replaces what was calculated.
        public static void FillSet(string cls, string rarity, int pieces, Dictionary<string, object> slots)
        {
            slots.Clear();
            Stars.Clear();
            int n = 0;
            foreach (string slot in ArmorSlots)
            {
                if (n >= pieces) break;
                foreach (var p in Items.GearOf(cls, slot))
                    if (p.Rarity == rarity && Items.IsArmor(p))
                    {
                        slots[slot] = p;
                        Stars[slot] = pieces >= 8 ? 3 : 1;
                        n++;
                        break;
                    }
            }
        }

        static readonly Color Page = Theme.Hex("#0e0d14"), Edge = Theme.Hex("#4a3a22"), SlotBg = Theme.Hex("#1b1a24"),
            SlotEdge = Theme.Hex("#45414f"), Gold = Theme.Hex("#f0c050"), Bright = Theme.Hex("#f5e9c8"), Dim = Theme.Hex("#9c8a64");

        // the height that shows every slot (the title, eight rows of slots and the weapons)
        public int NeededHeight { get { return 40 + (int)(fTitle.Height + fSmall.Height) + 9 * (slotSize + slotGap) + 14; } }

        // a sheet that fits a height: its slots (and the gaps between them) are sized so the whole sheet fits it
        public GearSheet(string cls, Dictionary<string, object> slots, Dictionary<string, string> gearIcons, int height)
            : this(cls, slots, gearIcons)
        {
            int fixedH = 40 + (int)(fTitle.Height + fSmall.Height) + 14;
            int row = Math.Max(8, (height - fixedH) / 9);    // reserve the shared stats footer before sizing the slots
            slotSize = Math.Max(6, Math.Min((int)(44 * Theme.Zoom), row - 2));
            slotGap = Math.Max(2, row - slotSize);
        }

        public GearSheet(string cls, Dictionary<string, object> slots, Dictionary<string, string> gearIcons)
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer |
                     ControlStyles.ResizeRedraw, true);
            this.cls = cls;
            this.slots = slots;
            this.gearIcons = gearIcons ?? new Dictionary<string, string>();
            itemScroll.Visible = false;
            itemScroll.SmallChange = 1;
            itemScroll.ValueChanged += delegate
            {
                itemOffset = itemScroll.Value;
                hoverRow = -1;
                tip.SetToolTip(this, null);
                GuideHover.Hide(this);
                Invalidate();
            };
            Controls.Add(itemScroll);
            bonusView.Font = Theme.F(8.25f);
            Controls.Add(bonusView);
            tip.InitialDelay = 150;
            tip.AutoPopDelay = 30000;
            if (Unused.Contains(chosenSlot)) chosenSlot = "Head";
            Publish();
        }

        public static Color RarityColor(string r)
        {
            switch (r)
            {
                case "Uncommon": return Theme.Hex("#1eff00");
                case "Rare": return Theme.Hex("#3b8ff0");
                case "Epic": case "PvP": return Theme.Hex("#b35cf5");
                case "Legendary": return Theme.Hex("#ff8000");
                case "Ring": case "Trinket": case "Amulet": return Theme.Hex("#3b8ff0");
            }
            return Color.White;
        }

        static string RarityOf(object o)
        {
            var p = o as GearPiece;
            if (p != null) return p.Rarity;
            var j = o as Jewel;
            return j != null ? j.Kind : "";
        }

        static string NameOf(object o)
        {
            var p = o as GearPiece;
            if (p != null) return p.Name;
            var j = o as Jewel;
            return j != null ? (j.IsCodex ? j.Name + " — " + j.Spell : j.Name) : "";
        }

        Image IconOf(object o)
        {
            var j = o as Jewel;
            if (j != null) return j.IsCodex ? SpellIcons.Spell(j.Spell, this) : SpellIcons.Icon(j.Icon.ToLowerInvariant(), this);
            var p = o as GearPiece;
            string ic;
            if (p != null && gearIcons.TryGetValue(p.Name, out ic)) return SpellIcons.Icon(ic, this);
            return null;
        }

        // ---- the totals: every bonus of every piece in a slot, added together
        Dictionary<string, double> Totals(out int health, out int unknown)
        {
            var t = new Dictionary<string, double>();
            health = 0;
            unknown = 0;
            setsWorn.Clear();
            foreach (var kv in slots)
            {
                var p = kv.Value as GearPiece;
                List<ItemBonus> list = null;
                if (p != null)
                {
                    int st = StarsOf(kv.Key);
                    if (Items.IsArmor(p))                    // the set it counts for: worn, at 1+, 3+ and 5 stars
                    {
                        int[] c;
                        if (!setsWorn.TryGetValue(p.Rarity, out c)) setsWorn[p.Rarity] = c = new int[4];
                        c[0]++;
                        if (st >= 1) c[1]++;
                        if (st >= 3) c[2]++;
                        if (st >= 5) c[3]++;
                    }
                    if (!p.Known) { unknown++; continue; }
                    health += p.Health;
                    list = p.Bonuses;
                }
                var j = kv.Value as Jewel;
                if (j != null) list = j.Bonuses;
                if (list == null) continue;
                foreach (var b in list) Add(t, b.Stat, b.Value);
            }
            // the 4-piece bonus of every set with 4 armor pieces at 1+ stars: two sets of four give both
            foreach (var kv in setsWorn)
            {
                var four = Items.FourPiece(kv.Key);
                if (kv.Value[1] >= 4 && four != null) foreach (var b in four.Bonuses) Add(t, b.Stat, b.Value);
            }
            return t;
        }

        static void Add(Dictionary<string, double> t, string stat, double value)
        {
            double v;
            t.TryGetValue(stat, out v);
            t[stat] = v + value;
        }

        // the totals for the hover texts of spells and talents (GearEffect)
        void Publish()
        {
            int h, u;
            var t = Totals(out h, out u);
            LauncherApp.PlannerTotals = t.Count > 0 ? t : null;
            if (StatsChanged != null) StatsChanged();
        }

        string Val(Dictionary<string, double> t, string stat, string unit)
        {
            double v;
            if (!t.TryGetValue(stat, out v) || v == 0) return unit == "%" ? "+0%" : "+0";
            return (v > 0 ? "+" : "") + Items.Pct(v) + unit;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.Clear(Parent != null ? Parent.BackColor : Theme.Panel);
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;
            if (StatsOnly)
            {
                DrawStatColumns(g, new Rectangle(0, 0, Width - 1, Height - 1));
                return;
            }
            if (SelectorOnly)
            {
                Selector(g, new Rectangle(0, 0, Width - 1, Height - 1 - BuildControlsHeight - 6));
                return;
            }
            boxes.Clear();
            // The character sheet owns roughly 78% of the canvas; the compact selector uses the rest.
            int pickW = Math.Max(110, Width * 22 / 100);
            var sheet = new Rectangle(0, 0, Width - pickW - 6, Height - 1);
            var pick = new Rectangle(sheet.Right + 6, 0, pickW - 1, Math.Max(40, Height - 1 - BuildControlsHeight - 6));
            Frame(g, sheet);
            Frame(g, pick);
            int health, unknown;
            var t = Totals(out health, out unknown);

            // the title
            string title = Lang.T(cls);
            SizeF ts = g.MeasureString(title, fTitle);
            // The shared class dropdown owns this title position.
            int top = sheet.Y + 16 + (int)ts.Height;

            // the slots: two columns and the weapons under them
            int s = slotSize, gap = slotGap;
            int tierW = Math.Max(6, s / 5) + 3;                // room for a piece's column of 5 small stars on its inner side
            int lx = sheet.X + 12, rx = sheet.Right - 8 - tierW - s;
            int by = Math.Max(top + s, sheet.Bottom - s - 10);
            // Spread both columns evenly down to the row above the weapons, instead of
            // leaving the spare height below tightly packed slots.
            int columnBottom = Math.Max(top, by - s - 8);
            for (int i = 0; i < Left.Length; i++)
                Slot(g, Left[i], new Rectangle(lx, top + (columnBottom - top) * i / Math.Max(1, Left.Length - 1), s, s));
            for (int i = 0; i < Right.Length; i++)
                Slot(g, Right[i], new Rectangle(rx, top + (columnBottom - top) * i / Math.Max(1, Right.Length - 1), s, s));
            int bgap = gap + tierW;                           // the weapons: room for their stars between them
            int bx = sheet.X + (sheet.Width - (3 * s + 2 * bgap)) / 2;
            for (int i = 0; i < Bottom.Length; i++) Slot(g, Bottom[i], new Rectangle(bx + i * (s + bgap), by, s, s));

            // the middle: the two stat boxes like the game's, then the note
            var mid = new Rectangle(lx + s + 6 + tierW, top, rx - lx - s - 18 - tierW, by - top - 8);
            // The gear-calculation explanation is a hover-only question mark, matching the global help button.
            unknownPieces = unknown;
            helpBox = new Rectangle(sheet.Right - 28, sheet.Bottom - 25, 22, 18);
            using (var b = new SolidBrush(hoverHelp ? Theme.Hex("#3a2c18") : Theme.Hex("#1c1a26"))) g.FillRectangle(b, helpBox);
            using (var p = new Pen(Theme.Hex("#d6a640"), 2)) g.DrawRectangle(p, helpBox);
            TextRenderer.DrawText(g, "?", Theme.F(10, FontStyle.Bold), helpBox, Theme.Hex("#f0c050"),
                                  TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);

            // under the totals, in the middle of the sheet (where the explanation was): every set worn, its 4-piece and
            // 8-piece bonuses (bright when reached), so two sets of four show and count both, and an 8-piece shows its 4-piece too
            int setsTop = 12 + fTitle.Height + 29 + 6 * (Theme.F(7.5f).Height + 2) + 6;
            DrawSets(g, new Rectangle(mid.X, setsTop, mid.Width, by - 8 - setsTop));   // above the weapons

            // the selector: what the chosen slot can hold, each with its picture
            Selector(g, pick);
        }

        public bool StatsOnly, SelectorOnly;
        public Action StatsChanged;

        void DrawStatColumns(Graphics g, Rectangle mid)
        {
            int health, unknown;
            var t = Totals(out health, out unknown);
            double hp;
            t.TryGetValue("health%", out hp);
            string healthText = "+" + health + (hp != 0 ? " +" + Items.Pct(hp) + "%" : "");
            string[][] main = {
                new[] { "Health", healthText }, new[] { "Damage", Val(t, "damage", "%") }, new[] { "Haste", Val(t, "cooldown", "%") },
                new[] { "Crit", Val(t, "crit", "%") }, new[] { "Crit Dmg", Val(t, "critdmg", "%") }, new[] { "Armor", Val(t, "armor", "%") }
            };
            string[][] misc = {
                new[] { "Area", Val(t, "area", "%") }, new[] { "Targets", Val(t, "targets", "") }, new[] { "Echo", Val(t, "echo", "%") },
                new[] { "Speed", Val(t, "speed", "%") }, new[] { "Regen", Val(t, "regen", "") }, new[] { "Pickup", Val(t, "pickup", "%") }
            };
            Font font = Theme.F(7.5f, FontStyle.Bold);
            int gap = 6, half = (Width - 1 - gap) / 2;
            for (int column = 0; column < 2; column++)
            {
                var box = new Rectangle(column * (half + gap), 0, column == 0 ? half : Width - 1 - (half + gap), Height - 1);
                using (var edge = new Pen(SlotEdge)) g.DrawRectangle(edge, box);
                var heading = new Rectangle(box.X + 3, 3, box.Width - 6, 18);
                TextRenderer.DrawText(g, Lang.T(column == 0 ? "Main Stats" : "Secondary Stats"), font, heading, Gold,
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);
                int y = 25, lh = font.Height + 2;
                foreach (var row in column == 0 ? main : misc)
                {
                    var line = new Rectangle(box.X + 5, y, box.Width - 10, lh);
                    TextRenderer.DrawText(g, Lang.T(row[0]), font, line, Gold, TextFormatFlags.Left | TextFormatFlags.NoPrefix);
                    TextRenderer.DrawText(g, row[1], font, line, Bright, TextFormatFlags.Right | TextFormatFlags.NoPrefix);
                    y += lh;
                }
            }
        }

        void DrawSets(Graphics g, Rectangle r)
        {
            var lines = new List<string>();
            foreach (string rarity in Items.Rarities)
            {
                int[] counts;
                if (!setsWorn.TryGetValue(rarity, out counts)) continue;
                var four = Items.FourPiece(rarity);
                string eight = Items.EightPiece(cls, rarity);
                lines.Add(Items.SetName(cls, rarity) + "  " + counts[0] + "/8");
                lines.Add("4p: " + (four != null ? four.Text : Lang.T("not entered yet")) +
                    " [" + counts[1] + "/4, 1+ " + Lang.T("stars") + "]");
                lines.Add("8p: " + (eight ?? Lang.T("not entered yet")) +
                    " [" + counts[2] + "/8, 3+ " + Lang.T("stars") + "]");
                lines.Add("");
            }
            string text = string.Join("\n", lines.ToArray());
            bonusView.Visible = r.Height > 12 && text.Length > 0;
            if (!bonusView.Visible) return;
            bonusView.Bounds = new Rectangle(r.X, r.Y, Math.Max(30, r.Width), r.Height);
            if (bonusView.Text != text) bonusView.Text = text;
        }

        // a line wrapped word by word, the spells and talents in it in gold (as the Guide links them); its height
        static float Rich(Graphics g, string text, Font f, Color baseColor, bool bright, float x, float y, float w, float bottom)
        {
            float lh = f.GetHeight(g);
            if (y + lh > bottom) return 0;
            using (var fmt = StringFormat.GenericTypographic)
                return RichIn(g, text, f, baseColor, bright, x, y, w, bottom, fmt);
        }

        static float RichIn(Graphics g, string text, Font f, Color baseColor, bool bright, float x, float y, float w, float bottom, StringFormat fmt)
        {
            float lh = f.GetHeight(g);
            float space = g.MeasureString("a b", f, 1000, fmt).Width - g.MeasureString("ab", f, 1000, fmt).Width;
            if (space <= 0) space = f.Size * 0.3f;
            float cx = x, cy = y;
            Color link = bright ? Gold : Color.FromArgb(200, 184, 154, 80);
            foreach (var run in GuideLinks.Split(text))
            {
                string[] words = run.Key.Split(' ');
                for (int i = 0; i < words.Length; i++)
                {
                    if (i > 0) cx += space;
                    if (words[i].Length == 0) continue;
                    float ww = g.MeasureString(words[i], f, 1000, fmt).Width;
                    if (cx > x && cx + ww > x + w) { cx = x; cy += lh; }
                    if (cy + lh > bottom) return cy - y;
                    using (var b = new SolidBrush(run.Value != null ? link : baseColor)) g.DrawString(words[i], f, b, cx, cy, fmt);
                    cx += ww;
                }
            }
            return cy + lh - y;
        }

        // a paragraph wrapped to a width (cut at the bottom); its height
        static float Para(Graphics g, string text, Font f, Color c, float x, float y, float w, float bottom)
        {
            if (y >= bottom) return 0;
            SizeF sz = g.MeasureString(text, f, (int)Math.Max(10, w));
            float h = Math.Min(sz.Height, bottom - y);
            using (var b = new SolidBrush(c)) g.DrawString(text, f, b, new RectangleF(x, y, w, h));
            return h;
        }

        List<object> Choices(string slot)
        {
            // Unequip through the character slot's right-click action; the picker lists equipment only.
            var l = new List<object>();
            if (slot == "Neck")
            {
                foreach (var j in Items.JewelryOf("Amulet")) if (!j.IsCodex || j.Class == cls) l.Add(j);
            }
            else if (slot.StartsWith("Finger")) foreach (var j in Items.JewelryOf("Ring")) l.Add(j);
            else if (slot.StartsWith("Trinket")) foreach (var j in Items.JewelryOf("Trinket")) l.Add(j);
            else foreach (var p in Items.GearOf(cls, slot)) l.Add(p);
            return l;
        }

        void Selector(Graphics g, Rectangle r)
        {
            rows.Clear();
            modeBoxes.Clear();
            tierBoxes.Clear();
            float lh = Math.Max(fText.GetHeight(g) + 6, 20);
            // where the header was: Single Item / Tier Bonus
            int bh = (int)lh + 4, half = (r.Width - 24 - 22) / 2;
            using (var sfc = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center })
                for (int i = 0; i < 2; i++)
                {
                    bool tier = i == 1, on = tier == tierMode;
                    var box = new Rectangle(r.X + 8 + i * (half + 8), r.Y + 8, half, bh);
                    using (var b = new SolidBrush(on ? Color.FromArgb(200, 240, 192, 80) : hoverMode == i ? Theme.Hex("#252331") : SlotBg)) g.FillRectangle(b, box);
                    using (var p = new Pen(on ? Gold : SlotEdge)) g.DrawRectangle(p, box);
                    using (var b = new SolidBrush(on ? Color.Black : Bright)) g.DrawString(Lang.T(tier ? "Tier Bonus" : "Single Item"), fBold, b, box, sfc);
                    modeBoxes.Add(new KeyValuePair<Rectangle, bool>(box, tier));
                }
            float y = r.Y + 8 + bh + 3;
            if (tierMode) { itemScroll.Visible = false; TierRows(g, r, y, lh); return; }
            int ic = (int)(lh - 4);                          // the picture: as tall as the line
            object worn;
            slots.TryGetValue(chosenSlot, out worn);
            string other = chosenSlot.EndsWith("1") ? chosenSlot.Replace("1", "2") : chosenSlot.EndsWith("2") ? chosenSlot.Replace("2", "1") : null;
            object inOther = null;
            if (other != null) slots.TryGetValue(other, out inOther);
            var list = Choices(chosenSlot);
            if (list.Count == 0) { itemScroll.Visible = false; return; }
            Font selectorFont = Theme.F(8.25f);
            ic = Math.Max(12, selectorFont.Height);
            int textWidth = Math.Max(20, r.Width - 4 - 14 - ic - 6);
            var names = new List<string>();
            var heights = new List<int>();
            foreach (object entry in list)
            {
                var piece = entry as GearPiece;
                string name = entry == EmptyRow ? Lang.T("Empty") : NameOf(entry) +
                    (piece != null && !piece.Known ? " *" : "") + (entry == inOther && entry != EmptyRow ? "  (" + Lang.T("worn") + ")" : "");
                names.Add(name);
                heights.Add(Math.Max(ic + 2, (int)Math.Ceiling(g.MeasureString(name, selectorFont, textWidth, StringFormat.GenericTypographic).Height) + 1));
            }
            int room = Math.Max(1, r.Bottom - 1 - (int)y);
            int maxOffset = list.Count - 1, tailHeight = heights[list.Count - 1];
            while (maxOffset > 0 && tailHeight + 1 + heights[maxOffset - 1] <= room)
                tailHeight += 1 + heights[--maxOffset];
            int visibleRows = Math.Max(1, list.Count - maxOffset);
            if (scrollSlot != chosenSlot) { itemOffset = 0; scrollSlot = chosenSlot; itemScroll.Value = 0; }
            itemOffset = Math.Min(itemOffset, maxOffset);
            bool scrollable = maxOffset > 0;
            itemScroll.Visible = scrollable;
            if (scrollable)
            {
                itemScroll.Bounds = new Rectangle(r.Right - 15, (int)y, 12, Math.Max(1, r.Bottom - 1 - (int)y));
                itemScroll.Maximum = Math.Max(0, list.Count - 1);
                itemScroll.LargeChange = visibleRows;
                itemScroll.Value = itemOffset;
            }

            using (var sf = new StringFormat(StringFormat.GenericTypographic) { LineAlignment = StringAlignment.Near })
                for (int i = itemOffset; i < list.Count; i++)
                {
                    int rowHeight = heights[i];
                    if (y + rowHeight > r.Bottom - 1) break;
                    object o = list[i];
                    var row = new Rectangle(r.X + 2, (int)y, r.Width - 4 - 14, rowHeight);
                    bool isWorn = o == worn || (o == EmptyRow && worn == null);
                    bool taken = o != EmptyRow && o == inOther;  // the same ring or trinket twice: no
                    if (isWorn || hoverRow == rows.Count)
                        using (var b = new SolidBrush(isWorn ? Color.FromArgb(60, 240, 192, 80) : Theme.Hex("#252331"))) g.FillRectangle(b, row);
                    var icon = new Rectangle(row.X + 1, row.Y + 1, ic, ic);
                    if (o == EmptyRow)
                    {
                        using (var p = new Pen(SlotEdge)) g.DrawRectangle(p, icon);
                        using (var b = new SolidBrush(Dim)) g.DrawString(Lang.T("Empty"), selectorFont, b, new RectangleF(icon.Right + 3, row.Y + 1, textWidth, row.Height), sf);
                    }
                    else
                    {
                        Image img = IconOf(o);
                        if (img != null) Theme.Draw(g, img, icon);
                        else if (o is GearPiece) Theme.ImageSoon(g, icon, RarityColor(RarityOf(o)));
                        else
                        {
                            using (var b = new SolidBrush(Color.FromArgb(70, RarityColor(RarityOf(o))))) g.FillRectangle(b, icon);
                            string ab = chosenSlot.Substring(0, Math.Min(2, chosenSlot.Length));
                            using (var f2 = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center })
                            using (var b = new SolidBrush(Bright)) g.DrawString(ab, fSmall, b, icon, f2);
                        }
                        using (var p = new Pen(RarityColor(RarityOf(o)))) g.DrawRectangle(p, icon);
                        var piece = o as GearPiece;
                        string name = names[i];
                        Color c = taken ? Dim : RarityColor(RarityOf(o));
                        using (var b = new SolidBrush(c)) g.DrawString(name, selectorFont, b, new RectangleF(icon.Right + 3, row.Y + 1, textWidth, row.Height), sf);
                    }
                    if (!taken) rows.Add(new KeyValuePair<Rectangle, object>(row, o));
                    else rows.Add(new KeyValuePair<Rectangle, object>(Rectangle.Empty, o));
                    y += rowHeight + 1;
                }
        }

        // Tier Bonus: one line per rarity, its name on the left and 4 piece / 8 piece side by side
        void TierRows(Graphics g, Rectangle r, float y, float lh)
        {
            int bw = Math.Max(54, (int)(r.Width * 0.27f)), bh = (int)lh + 2, i = 0;
            using (var sfc = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center })
            using (var sfl = new StringFormat(StringFormatFlags.NoWrap) { Trimming = StringTrimming.EllipsisCharacter, LineAlignment = StringAlignment.Center })
                foreach (string rar in Items.Rarities)
                {
                    if (y + bh > r.Bottom - 6) break;
                    int x8 = r.Right - 8 - bw, x4 = x8 - 6 - bw;
                    var label = new RectangleF(r.X + 10, y, x4 - r.X - 14, bh);
                    using (var b = new SolidBrush(RarityColor(rar))) g.DrawString(Lang.T(rar), fBold, b, label, sfl);
                    foreach (string n in new[] { "4", "8" })
                    {
                        var box = new Rectangle(n == "4" ? x4 : x8, (int)y, bw, bh);
                        using (var b = new SolidBrush(hoverTier == i ? Theme.Hex("#252331") : SlotBg)) g.FillRectangle(b, box);
                        using (var p = new Pen(RarityColor(rar))) g.DrawRectangle(p, box);
                        using (var b = new SolidBrush(Bright)) g.DrawString(Lang.T(n + " piece"), fSmall, b, box, sfc);
                        tierBoxes.Add(new KeyValuePair<Rectangle, string[]>(box, new[] { rar, n }));
                        i++;
                    }
                    y += bh + 6;
                }
            if (y + 2 * fSmall.GetHeight(g) < r.Bottom)
                Para(g, Lang.T("4 piece: four pieces at 1 star. 8 piece: all eight at 3 stars. Everything else in the planner is emptied."), fSmall, Dim, r.X + 10, y + 2, r.Width - 20, r.Bottom - 4);
        }

        // The Tier Bonus picker shows what each choice does before it replaces the current planner.
        void ShowTierBonus(string[] tier)
        {
            if (tier == null || tier.Length < 2) return;
            bool eight = tier[1] == "8";
            string name = Items.SetName(cls, tier[0]);
            string body;
            if (eight)
            {
                string effect = Items.EightPiece(cls, tier[0]);
                body = Lang.T("All 8 armor pieces at 3+ stars") + ": " + (effect ?? Lang.T("not entered yet"));
                body += "\n\n" + Lang.T("At 5 stars on all 8 pieces, this bonus is half again as strong.");
            }
            else
            {
                Items.SetBonus effect = Items.FourPiece(tier[0]);
                body = Lang.T("4 armor pieces at 1+ stars") + ": " + (effect != null ? effect.Text : Lang.T("not entered yet"));
            }
            GuideHover.ShowHelp(this, name + " — " + Lang.T(eight ? "8-piece bonus" : "4-piece bonus"), body);
        }

        void Frame(Graphics g, Rectangle r)
        {
            using (var path = Armory.Round(r, 8))
            {
                using (var b = new SolidBrush(Page)) g.FillPath(b, path);
                using (var p = new Pen(Edge, 2f)) g.DrawPath(p, path);
            }
        }

        void StatBox(Graphics g, Rectangle r, string[][] lines)
        {
            using (var b = new SolidBrush(Color.FromArgb(18, 17, 24))) g.FillRectangle(b, r);
            using (var p = new Pen(SlotEdge)) g.DrawRectangle(p, r);
            float lh = fText.GetHeight(g) + 2, y = r.Y + 6;
            foreach (var row in lines)
            {
                if (row[0] == "Damage")
                {
                    if (dpsUpLabel == null) dpsUpLabel = Theme.LoadImage("AS.gear.dps-up.png");
                    if (dpsUpLabel != null)
                    {
                        float labelHeight = Math.Min(lh - 2, 14f);
                        float labelWidth = dpsUpLabel.Width * labelHeight / dpsUpLabel.Height;
                        Theme.Draw(g, dpsUpLabel, r.X + 8, y + 1, labelWidth, labelHeight);
                    }
                }
                else
                {
                    string label = Lang.T(row[0]) + ":";
                    using (var b = new SolidBrush(Gold)) g.DrawString(label, fText, b, r.X + 8, y);
                }
                SizeF vs = g.MeasureString(row[1], fText);
                using (var b = new SolidBrush(Bright)) g.DrawString(row[1], fText, b, r.Right - 8 - vs.Width, y);
                y += lh;
            }
        }

        // a small five-pointed star: gold when earned, a dim outline when not
        static void DrawStar(Graphics g, RectangleF r, bool on)
        {
            var pts = new PointF[10];
            float cx = r.X + r.Width / 2, cy = r.Y + r.Height / 2 + r.Height * 0.04f, ro = r.Width / 2, ri = ro * 0.45f;
            for (int i = 0; i < 10; i++)
            {
                double a = -Math.PI / 2 + i * Math.PI / 5;
                float rad = i % 2 == 0 ? ro : ri;
                pts[i] = new PointF(cx + (float)(Math.Cos(a) * rad), cy + (float)(Math.Sin(a) * rad));
            }
            if (on) using (var b = new SolidBrush(Gold)) g.FillPolygon(b, pts);
            else using (var p = new Pen(Color.FromArgb(110, 156, 138, 100), 1f)) g.DrawPolygon(p, pts);
        }

        void Slot(Graphics g, string slot, Rectangle r)
        {
            boxes[slot] = r;
            object o;
            slots.TryGetValue(slot, out o);
            bool unused = Unused.Contains(slot);
            using (var b = new SolidBrush(unused ? Color.FromArgb(14, 13, 18) : (hover == slot ? Theme.Hex("#252331") : SlotBg))) g.FillRectangle(b, r);
            if (o != null)
            {
                Image img = IconOf(o);
                if (img != null)                              // at a sharp size, centred in the slot
                {
                    int ic = Theme.CrispIcon(r.Width - 4);
                    Theme.Draw(g, img, new Rectangle(r.X + (r.Width - ic) / 2, r.Y + (r.Height - ic) / 2, ic, ic));
                }
                else if (o is GearPiece) Theme.ImageSoon(g, Rectangle.Inflate(r, -3, -3), RarityColor(RarityOf(o)));
                else
                {
                    using (var b = new SolidBrush(Color.FromArgb(70, RarityColor(RarityOf(o))))) g.FillRectangle(b, Rectangle.Inflate(r, -3, -3));
                    string ab = slot.Substring(0, Math.Min(2, slot.Length));
                    SizeF a = g.MeasureString(ab, fBold);
                    using (var b = new SolidBrush(Bright)) g.DrawString(ab, fBold, b, r.X + (r.Width - a.Width) / 2, r.Y + (r.Height - a.Height) / 2);
                }
                using (var p = new Pen(RarityColor(RarityOf(o)), 2f)) g.DrawRectangle(p, r);
                int st = o is GearPiece ? StarsOf(slot) : 0;
                if (o is GearPiece)                           // its stars: a column of 5 small stars just outside the box, on its inner
                {                                             // side (left column: right of it; right column: left of it), filled
                    float ss = Math.Max(6, r.Height / 5f);    // from the top as a left click adds one, dim outlines for the rest
                    bool leftCol = r.X + r.Width / 2 < ClientSize.Width / 2;
                    float sx = leftCol ? r.Right + 2 : r.X - 2 - ss;
                    for (int i = 0; i < 5; i++) DrawStar(g, new RectangleF(sx, r.Y + i * (r.Height / 5f) + (r.Height / 5f - ss) / 2, ss, ss), i < st);
                }
            }
            else
            {
                using (var p = new Pen(unused ? Color.FromArgb(40, 38, 48) : SlotEdge)) g.DrawRectangle(p, r);
                if (!unused)
                    using (var f = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center })
                    using (var b = new SolidBrush(Color.FromArgb(110, 156, 138, 100)))
                        g.DrawString(Lang.T(slot), fSmall, b, r, f);
            }
            if (slot == chosenSlot && !unused)                // the slot the selector shows
                using (var p = new Pen(Gold, 2f)) g.DrawRectangle(p, Rectangle.Inflate(r, 2, 2));
        }

        string SlotAt(Point p)
        {
            foreach (var kv in boxes) if (kv.Value.Contains(p) && !Unused.Contains(kv.Key)) return kv.Key;
            return null;
        }

        int RowAt(Point p)
        {
            for (int i = 0; i < rows.Count; i++) if (rows[i].Key.Contains(p)) return i;
            return -1;
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            int oldTier = hoverTier;
            bool oldHelp = hoverHelp;
            string s = SlotAt(e.Location);
            int r = s == null ? RowAt(e.Location) : -1;
            int hm = -1, ht = -1;
            for (int i = 0; i < modeBoxes.Count; i++) if (modeBoxes[i].Key.Contains(e.Location)) hm = i;
            for (int i = 0; i < tierBoxes.Count; i++) if (tierBoxes[i].Key.Contains(e.Location)) ht = i;
            if (hm != hoverMode || ht != hoverTier) { hoverMode = hm; hoverTier = ht; Invalidate(); }
            bool hh = helpBox.Contains(e.Location);
            if (hh != hoverHelp)
            {
                hoverHelp = hh;
                Invalidate();
            }
            if (hh)
            {
                if (!oldHelp)
                {
                    string body = Lang.T("The bonuses of everything chosen, added together, with the set bonuses reached. Click a piece to add a star (up to 5, then back to 0); right-click to take it off.");
                    if (unknownPieces > 0) body = Lang.T("Pieces with bonuses not known yet") + ": " + unknownPieces + ". " + body;
                    GuideHover.ShowHelp(this, Lang.T("How are gear stats calculated?"), body);
                }
            }
            else if (ht >= 0 && (ht != oldTier || oldHelp)) ShowTierBonus(tierBoxes[ht].Value);
            else if (oldTier >= 0 || oldHelp) GuideHover.Hide(this);
            Cursor = hh ? Cursors.Help : (s != null || r >= 0 || hm >= 0 || ht >= 0 ? Cursors.Hand : Cursors.Default);
            if (s != hover || r != hoverRow)
            {
                hover = s;
                hoverRow = r;
                object o = null;
                if (s != null) slots.TryGetValue(s, out o);
                else if (r >= 0) o = rows[r].Value;
                int st = StarsOf(s ?? chosenSlot);
                string text = o is GearPiece ? Items.Card((GearPiece)o, st) : o is Jewel ? Items.Card((Jewel)o) : s != null ? Lang.T(s) : null;
                string itemName = o is GearPiece ? ((GearPiece)o).Name : o is Jewel ? ((Jewel)o).Name : null;
                if (itemName != null)
                {
                    int titleEnd = text == null ? -1 : text.IndexOf('\n');
                    string body = titleEnd > 0 ? text.Substring(titleEnd + 1) : text;
                    tip.SetItemToolTip(this, itemName, body, Items.IconFor(itemName));
                }
                else tip.SetToolTip(this, text);
                Invalidate();
            }
            base.OnMouseMove(e);
        }

        protected override void OnMouseLeave(EventArgs e) { hover = null; hoverRow = -1; hoverHelp = false; GuideHover.Hide(); tip.SetToolTip(this, null); Invalidate(); base.OnMouseLeave(e); }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            if (StatsOnly)
            {
                return;
            }

            base.OnMouseUp(e);
            if (e.Button == MouseButtons.Left)
            {
                foreach (var m in modeBoxes)
                    if (m.Key.Contains(e.Location)) { tierMode = m.Value; Invalidate(); return; }
                foreach (var t in tierBoxes)
                    if (t.Key.Contains(e.Location)) { FillSet(cls, t.Value[0], t.Value[1] == "8" ? 8 : 4, slots); Publish(); Invalidate(); return; }
            }
            string s = SlotAt(e.Location);
            if (s != null)
            {
                object worn;
                slots.TryGetValue(s, out worn);
                if (e.Button == MouseButtons.Right) { slots.Remove(s); Stars.Remove(s); }
                else
                {
                    if (worn is GearPiece) Stars[s] = (StarsOf(s) + 1) % 6;   // a click on a piece: one more star, after 5 back to 0
                    chosenSlot = s;                           // the selector shows what this slot can hold
                    tierMode = false;
                }
                hover = null;                                 // the hover card shows the new stars at once
                Publish();
                Invalidate();
                return;
            }
            int r = RowAt(e.Location);
            if (r < 0 || e.Button != MouseButtons.Left) return;
            object o = rows[r].Value;
            if (o == EmptyRow) slots.Remove(chosenSlot);
            else slots[chosenSlot] = o;
            Stars.Remove(chosenSlot);                         // a new piece starts at 0 stars
            Publish();
            Invalidate();
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing) tip.Dispose();
            base.Dispose(disposing);
        }
    }

    // The Gear Planner's set in the hover texts of spells and talents: their damage, their cooldowns and their reach
    // shown with the set's damage, cooldown and area bonuses (a plain estimate: the game may combine them otherwise).
    // The set is not saved, so this ends when the launcher closes.
    public static class GearEffect
    {
        static readonly System.Text.RegularExpressions.Regex DamageNum = new System.Text.RegularExpressions.Regex(
            @"\b(\d+(?:\.\d+)?)(?=(?: [A-Z][a-z]+)? damage\b)");
        static readonly System.Text.RegularExpressions.Regex SecNum = new System.Text.RegularExpressions.Regex(
            @"(?<=\b(?:every|cooldown of|cooldown:|Cooldown:) )(\d+(?:\.\d+)?)(?= ?sec)");
        static readonly System.Text.RegularExpressions.Regex YdNum = new System.Text.RegularExpressions.Regex(
            @"(?<=\bwithin )(\d+(?:\.\d+)?)(?= ?yd)");

        public static string Apply(string title, string body)
        {
            var t = LauncherApp.PlannerTotals;
            if (t == null || string.IsNullOrEmpty(body)) return body;
            foreach (var p in Items.Gear) if (p.Name == title) return body;     // an item's own card stays as it is
            foreach (var j in Items.Jewelry) if (j.Name == title) return body;
            double dmg, cd, area;
            t.TryGetValue("damage", out dmg);
            t.TryGetValue("cooldown", out cd);
            t.TryGetValue("area", out area);
            if (dmg == 0 && cd == 0 && area == 0) return body;
            bool changed = false;
            string s = body;
            if (dmg != 0)
                s = DamageNum.Replace(s, delegate(System.Text.RegularExpressions.Match m) { changed = true; return Scale(m.Value, 1 + dmg / 100); });
            if (cd != 0)
                s = SecNum.Replace(s, delegate(System.Text.RegularExpressions.Match m) { changed = true; return Scale(m.Value, Math.Max(0.05, 1 - cd / 100)); });
            if (area != 0)
                s = YdNum.Replace(s, delegate(System.Text.RegularExpressions.Match m) { changed = true; return Scale(m.Value, 1 + area / 100); });
            var parts = new List<string>();
            if (dmg != 0) parts.Add("+" + Items.Pct(dmg) + "% " + Lang.T("damage"));
            if (cd != 0) parts.Add("-" + Items.Pct(cd) + "% " + Lang.T("cooldowns"));
            if (area != 0) parts.Add("+" + Items.Pct(area) + "% " + Lang.T("area"));
            return s + "\n\n" + Lang.T(changed ? "Numbers with your Gear Planner set:" : "Your Gear Planner set:") + " " + string.Join(", ", parts.ToArray());
        }

        static string Scale(string num, double f)
        {
            double v;
            if (!double.TryParse(num, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out v)) return num;
            double r = v * f;
            return r >= 10 ? Math.Round(r).ToString(System.Globalization.CultureInfo.InvariantCulture)
                           : r.ToString("0.#", System.Globalization.CultureInfo.InvariantCulture);
        }
    }
}
