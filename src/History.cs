// The back arrow of the section window goes back along the way you came (Guide > a class > Options > back lands on
// that class again), not just one level up. Every view shown is remembered (the last 50); a view that only redraws
// itself (a toggle, a language change, typing in the search) is not a new step. The home screen starts the way anew.
using System;
using System.Collections.Generic;
using System.Windows.Forms;

namespace AzerothLauncher
{
    public partial class LauncherApp
    {
        class ViewState
        {
            public string Section, GTalents, GuideQuery, AddonMode, AskUninstallName, AskUninstallFolder;
            public int GTopic, GPage, GuideGroupPage, GuideGroup, GuideLastTopic, ClView, OptionsTab, StatsTab, AddonCategory;
            public int[] ScrollPositions;
            public bool GBuilder, GPlanner, GuideMain, OptionsLang, OptionsAbout, CommunityMore, RollbackOpen;

            // the same place (the search text aside: typing is not a new step)
            public bool SamePlace(ViewState o)
            {
                return o != null && Section == o.Section && GTalents == o.GTalents && AddonMode == o.AddonMode &&
                       AddonCategory == o.AddonCategory && RollbackOpen == o.RollbackOpen &&
                       AskUninstallName == o.AskUninstallName && AskUninstallFolder == o.AskUninstallFolder &&
                       GTopic == o.GTopic && GPage == o.GPage && GuideGroupPage == o.GuideGroupPage && GuideGroup == o.GuideGroup &&
                       ClView == o.ClView && OptionsTab == o.OptionsTab && StatsTab == o.StatsTab && GBuilder == o.GBuilder &&
                       GPlanner == o.GPlanner && GuideMain == o.GuideMain && OptionsLang == o.OptionsLang && OptionsAbout == o.OptionsAbout &&
                       CommunityMore == o.CommunityMore && (GuideQuery.Length > 0) == (o.GuideQuery.Length > 0);
            }
        }

        readonly List<ViewState> history = new List<ViewState>();
        bool restoringView;
        ViewState displayedView;
        int[] restoreScrollPositions;
        float panelDensity=1f;

        void CaptureDisplayedScrollPositions()
        {
            if (displayedView == null) return;
            var panels = new List<ScrollPanel>();
            CollectScrollPanels(panel, panels);
            var positions = new int[panels.Count];
            for (int i = 0; i < panels.Count; i++) positions[i] = Math.Max(0, (int)Math.Round(-panels[i].AutoScrollPosition.Y / panelDensity));
            displayedView.ScrollPositions = positions;
            if (history.Count > 0 && object.ReferenceEquals(history[history.Count - 1], displayedView))
                history[history.Count - 1].ScrollPositions = positions;
        }

        static void CollectScrollPanels(Control parent, List<ScrollPanel> found)
        {
            if (parent == null) return;
            foreach (Control child in parent.Controls)
            {
                ScrollPanel scroll = child as ScrollPanel;
                if (scroll != null) found.Add(scroll);
                CollectScrollPanels(child, found);
            }
        }

        void RestoreDisplayedScrollPositions(int[] positions)
        {
            if (positions == null) return;
            int expectedGeneration=panelGeneration;
            Action restore = delegate
            {
                if (IsDisposed || panel == null || panel.IsDisposed || panelGeneration!=expectedGeneration) return;
                var panels = new List<ScrollPanel>();
                CollectScrollPanels(panel, panels);
                for (int i = 0; i < panels.Count && i < positions.Length; i++) panels[i].ScrollTo((int)Math.Round(positions[i]*Theme.DisplayScale));
            };
            if (IsHandleCreated) BeginInvoke(restore);
            else restore();
        }

        ViewState CurrentView(string section)
        {
            return new ViewState
            {
                Section = section, GTalents = gTalents, GuideQuery = guideQuery ?? "", AddonMode = addonMode,
                AddonCategory = addonCategory, RollbackOpen = rollbackOpen,
                AskUninstallName = askUninstallName, AskUninstallFolder = askUninstallFolder,
                GTopic = gTopic, GPage = gPage, GuideGroupPage = guideGroupPage, GuideGroup = guideGroup, GuideLastTopic = guideLastTopic,
                ClView = clView, OptionsTab = optionsTab, StatsTab = statsTab,
                GBuilder = gBuilder, GPlanner = gPlanner, GuideMain = guideMain, OptionsLang = optionsLang, OptionsAbout = optionsAbout, CommunityMore = communityMore
            };
        }

        // called each time a section is built
        void RememberView(string section)
        {
            if (restoringView || section == "Setup") return;
            if (section == "Community" && !communityMore) { history.Clear(); return; }   // the home screen: a new way starts
            var v = CurrentView(section);
            if (history.Count > 0 && history[history.Count - 1].SamePlace(v))
            {
                v.ScrollPositions = history[history.Count - 1].ScrollPositions;
                history[history.Count - 1] = v;
                return;
            }
            history.Add(v);
            if (history.Count > 50) history.RemoveAt(0);
        }

        // the back arrow: the view before this one, as it was; false when there is none (then one level up, as before)
        bool BackInHistory()
        {
            if (history.Count < 2) return false;
            // Resolve a valid destination before changing history. A disabled Stats view
            // must not consume the current view if there is nowhere valid to return.
            int target = history.Count - 2;
            while (target >= 0 && history[target].Section == "Stats" && !StatsAddonActive()) target--;
            if (target < 0) return false;
            ViewState v = history[target];
            history.RemoveRange(target + 1, history.Count - target - 1);
            bool leavingPlanner = gPlanner && !v.GPlanner;
            gTalents = v.GTalents; guideQuery = v.GuideQuery; addonMode = v.AddonMode;
            addonCategory = v.AddonCategory; rollbackOpen = v.RollbackOpen;
            askUninstallName = v.AskUninstallName; askUninstallFolder = v.AskUninstallFolder;
            gTopic = v.GTopic; gPage = v.GPage; guideGroupPage = v.GuideGroupPage; guideGroup = v.GuideGroup; guideLastTopic = v.GuideLastTopic;
            clView = v.ClView; optionsTab = v.OptionsTab; statsTab = v.StatsTab;
            gBuilder = v.GBuilder; gPlanner = v.GPlanner; guideMain = v.GuideMain; optionsLang = v.OptionsLang; optionsAbout = v.OptionsAbout; communityMore = v.CommunityMore;
            if (leavingPlanner) ResetPlannerState();
            gVideo = null;
            restoreScrollPositions = v.ScrollPositions;
            current = v.Section;
            foreach (var kv in nav) kv.Value.Active = kv.Key == v.Section;
            RefreshStrip();
            ShowSide();
            restoringView = true;
            try { BuildPanel(v.Section); }
            finally { restoringView = false; }
            return true;
        }
    }
}
