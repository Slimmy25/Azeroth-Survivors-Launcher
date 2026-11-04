using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Collections.Generic;
using System.Reflection;
using System.Linq;
using System.Windows.Forms;

namespace AzerothLauncher
{
    public partial class LauncherApp
    {
        // Developer-only renderer. Runs off-screen, never creates setup markers or updates game data.
        public static void RenderScalingPreview(string[] args)
        {
            PreviewMode = true;
            int languageAt=Array.IndexOf(args,"--language");
            if(languageAt>=0) Lang.Load(args[languageAt+1]);
            string output = args[Array.IndexOf(args, "--preview-scaling") + 1];
            int width = 1920, height = 1040, dpi = 144;
            int at = Array.IndexOf(args, "--display");
            if (at >= 0) { width=int.Parse(args[at+1]); height=int.Parse(args[at+2]); dpi=int.Parse(args[at+3]); }
            float preference=0;
            at=Array.IndexOf(args, "--scale");
            if(at>=0) preference=float.Parse(args[at+1], System.Globalization.CultureInfo.InvariantCulture);
            bool full=Array.IndexOf(args, "--full")>=0;
            var checks=new List<string>();
            Action<bool,string> check=delegate(bool condition,string label)
            {
                checks.Add((condition?"PASS ":"FAIL ")+label);
                File.WriteAllLines(Path.Combine(output,"checks.txt"),checks.ToArray());
                if(!condition) throw new InvalidOperationException(label);
            };
            Directory.CreateDirectory(output);
            using (var app = new LauncherApp())
            {
                app.StartPosition = FormStartPosition.Manual;
                app.ShowInTaskbar = false;
                app.Location = new Point(-30000, -30000);
                app.setupAsked = true;
                app.Show();
                // This renderer pumps with DoEvents rather than Application.Run. Install
                // the UI context explicitly so deferred button actions stay on this thread.
                System.Threading.SynchronizationContext.SetSynchronizationContext(new WindowsFormsSynchronizationContext());
                app.ShowSide();
                app.changingDisplay=true;
                app.Cfg.Set("interface_scale",preference.ToString(System.Globalization.CultureInfo.InvariantCulture));
                app.displayLayout=new DisplayLayout(new Rectangle(0,0,width,height), dpi, preference);
                Theme.DisplayScale=app.displayLayout.Scale; Theme.DisplayDpi=dpi; Theme.Zoom=Theme.DisplayScale;
                app.MinimumSize=Size.Empty;
                app.ClientSize=app.displayLayout.InitialClientSize;
                app.changingDisplay=false;
                app.LayoutResponsiveDisplay(true);
                File.WriteAllText(Path.Combine(output,"metrics.txt"),"Native awareness="+NativeDisplay.Awareness+"; host DPI="+NativeDisplay.Dpi(app)+"; "+ "Simulated work area: "+width+"x"+height+"; DPI="+dpi+"; scale="+Theme.DisplayScale+"; client="+app.ClientSize);

                Action<string> capture = delegate(string name)
                {
                    app.displayResizeTimer.Stop();
                    Application.DoEvents();
                    app.Refresh(); app.side.Refresh();
                    Application.DoEvents();
                    app.Refresh(); app.side.Refresh();
                    using (var bitmap = new Bitmap(app.ClientSize.Width, app.ClientSize.Height))
                    {
                        bitmap.SetResolution(Theme.DisplayDpi, Theme.DisplayDpi);
                        using (var graphics = Graphics.FromImage(bitmap))
                            app.OnPaint(new PaintEventArgs(graphics, new Rectangle(Point.Empty, bitmap.Size)));
                        for (int i = app.Controls.Count - 1; i >= 0; i--)
                        {
                            Control child = app.Controls[i];
                            if (child.Visible) DrawScalingTree(child, bitmap, child.Left, child.Top, new Rectangle(Point.Empty, bitmap.Size));
                        }
                        bitmap.Save(Path.Combine(output, name + ".png"), ImageFormat.Png);
                    }
                };
                if(Array.IndexOf(args,"--addon-updates")>=0) { app.RenderAddonUpdateChecks(output,capture,check); app.Close(); return; }
                check(app.ClientRectangle.Contains(app.side.Bounds),"secondary pane inside client");
                check(app.ClientRectangle.Contains(app.playBtn.Bounds),"Play action inside client");
                check(app.ResponsiveSideRect.Bottom<app.FooterTop,"secondary pane above footer");
                var negativeMonitor=new DisplayLayout(new Rectangle(-3840,-2160,3840,2120),192,0);
                check(negativeMonitor.WorkArea.Contains(negativeMonitor.Clamp(new Rectangle(500,500,2200,1400))),"negative monitor coordinates clamp correctly");
                app.RunScalingInteractions(check);
                app.ValidateCommunitySkipQueue(check);
                check(!ShouldShowSetupAddonHandoff(true, true), "existing-client setup never shows the game-download handoff");
                check(ShouldShowSetupAddonHandoff(false, false), "new-client setup keeps the download handoff");
                check(!ShouldShowSetupAddonHandoff(false, true), "an already-present game skips the download handoff");
                if(Array.IndexOf(args,"--stress")>=0) app.RunScalingStress(check);
                app.OpenGuideTab(-3);
                capture("gear-warrior");
                foreach (string cls in full ? Items.Classes : new[] {"Warrior", "Priest"})
                {
                    app.PlannerClass(cls);
                    app.gTalents = null; app.gBuilder = false; app.gPlanner = true;
                    app.BuildPanel("Guide");
                    capture("gear-" + cls.ToLowerInvariant());
                    CheckScalingControls(app.panel,check);
                    check(app.plannerClass==cls,"shared gear class: "+cls);
                    app.SwitchTheoryTool(false);
                    capture("talents-" + cls.ToLowerInvariant());
                    check(app.liveBuildCls==cls,"shared talent class: "+cls);
                    CheckScalingControls(app.panel,check);
                }
                foreach (string section in new[] { "Guide", "Options", "Addons", "Stats", "Changelog", "About Launcher", "Community" })
                {
                    app.gBuilder = app.gPlanner = false; app.gTalents = null;
                    app.current = section; app.guideWikiIndex = true;
                    app.BuildPanel(section);
                    capture(section.ToLowerInvariant().Replace(" ", "-"));
                }
                string addonFixture=Path.Combine(Path.GetFullPath(output),"fixture-addons");
                foreach(string addon in new[]{"AzerothStats","TestAddon","AnotherAddon"})
                {
                    string dir=Path.Combine(addonFixture,addon); Directory.CreateDirectory(dir);
                    File.WriteAllText(Path.Combine(dir,addon+".toc"),"## Interface: 11200\n## Title: QA "+addon+"\n");
                }
                app.Cfg.Set("addons_dir",addonFixture); app.addonMode="installed"; app.current="Addons";
                app.BuildPanel("Addons"); capture("addons-installed"); CheckScalingControls(app.panel,check);
                app.Cfg.Set("addons_advanced",true); app.BuildPanel("Addons"); capture("addons-advanced"); CheckScalingControls(app.panel,check);
                app.Cfg.Set("addons_advanced",false);
                foreach(int option in full?new[]{0,1,2,3,4}:new[]{0,4})
                {
                    app.optionsTab=option; app.current="Options";app.BuildPanel("Options");capture("options-tab-"+option);CheckScalingControls(app.panel,check);
                }
                app.addonMode="download"; app.current="Addons"; app.BuildPanel("Addons"); capture("addon-downloads");
                app.addonMode="installed";
                int classes=Data.Guide.FindIndex(delegate(GuideTopic topic){return topic.Title=="Classes";});
                if(classes>=0)
                {
                    int paladin=Data.Guide[classes].Pages.FindIndex(delegate(GuidePage page){return page.Title=="Paladin";});
                    if(full)foreach(string cls in Items.Classes)
                    {
                        int page=Data.Guide[classes].Pages.FindIndex(delegate(GuidePage value){return value.Title==cls;});
                        app.current="Guide";app.SetGuideView(classes,page);capture("class-"+cls.ToLowerInvariant());
                        CheckClassArticleLinkTargets(app,classes,page,cls,check);
                    }
                    app.current="Guide"; app.SetGuideView(classes,paladin); capture("class-paladin");
                    foreach(var pair in app.classNavLinks)
                    {
                        var label=pair.Value as GuideContentsLabel;
                        if(label!=null)check(label.LinesFit&&label.Height>=label.WrappedHeight,"sidebar wraps complete text: "+pair.Key);
                    }
                    Control codexTarget;
                    if(app.classSectionTargets.TryGetValue("Codex (Necklaces)",out codexTarget))
                    {
                        app.NavigateClassArticleToControl(codexTarget);
                        Label activeLink=app.classNavLinks["Codex (Necklaces)"];
                        check(activeLink.Top>=0&&activeLink.Top<activeLink.Parent.ClientSize.Height,"active tree section stays in view");
                        capture("class-sidebar-codex");
                    }
                    app.classArticleScroll.ScrollTo(Theme.Px(240));
                    int prior=-app.classArticleScroll.AutoScrollPosition.Y;
                    app.NavigateClassArticleTo(app.classArticleScroll,prior+Theme.Px(100));
                    app.LayoutResponsiveDisplay(true); Application.DoEvents();
                    check(app.BackWithinClassArticle(),"in-article Back history survives layout rebuild");
                    check(Math.Abs(-app.classArticleScroll.AutoScrollPosition.Y-prior)<3,"in-article Back restores prior reading position");
                    var favorite=app.CurrentGuideFavoriteRoute();
                    app.classArticleScroll.ScrollTo(0);app.RestoreGuideFavoriteLocation(favorite);
                    check(Math.Abs(-app.classArticleScroll.AutoScrollPosition.Y-prior)<3,"favorite restores logical reading position");
                    app.classArticleScroll.ScrollTo(0);
                }
                for(int i=0;i<GuideFavoriteCount;i++) app.guideFavorites[i]=new GuideFavoriteRoute("guide","Classes",Items.Classes[i%Items.Classes.Length]);
                app.RefreshStrip(); app.LayoutStrip(); capture("ten-favorites");
                check(app.favoriteBtns[9].Bottom<=app.strip.ClientSize.Height,"all ten favorite buttons remain reachable");
                app.OpenGuideTab(-3); app.PlannerClass("Warrior"); GearSheet.FillSet("Warrior","Legendary",8,app.plannerSlots);
                app.BuildPanel("Guide"); capture("gear-set-bonuses");
                app.ShowTheoryCodeOverlay("Warrior",true); capture("export-overlay"); CheckScalingControls(app.panel,check);
                app.BuildPanel("Guide"); app.ShowTheoryCodeOverlay("Warrior",false); capture("import-overlay"); CheckScalingControls(app.panel,check);
                string fixture=Path.Combine(Path.GetFullPath(output),"fixture-saved"); Directory.CreateDirectory(fixture);
                File.WriteAllText(Path.Combine(fixture,"AzerothStats.lua"),
                    "AzerothStatsDB={schema=1,totals={runs=12,wins=5,coverage={duration=12,kills=12,damage=12},sum={duration=7200,kills=40000,damage=1800000}},classes={warrior={name=\"Warrior\",runs=12,wins=5,sum={duration=7200,kills=40000,damage=1800000},coverage={duration=12,kills=12,damage=12}}},runs={{id=\"qa-1\",class=\"warrior\",hero=\"QA Warrior\",startedAt=\"2026-10-31 12:00:00\",result=\"victory\",values={duration=600,kills=3000,damage=100000,level=80}}}}");
                app.Cfg.Set("saved_dir",fixture); app.Cfg.Set("stats_enabled",true);
                foreach(int tab in full ? new[]{0,1,2,3,4,5,6,7,8,9} : new[]{0,6})
                {
                    app.statsTab=tab; app.current="Stats"; app.BuildPanel("Stats"); capture("stats-fixture-"+tab);
                }
                app.current="Guide"; app.gPlanner=true; app.gTalents=null; app.BuildPanel("Guide");
                app.lostHelpCard.Bounds=new Rectangle(8,8,Math.Min(Theme.Px(360),app.PW-16),Math.Min(Theme.Px(400),app.PH-16));
                app.lostHelpCard.Visible=true; app.lostHelpCard.BringToFront(); capture("lost-help"); app.lostHelpCard.Visible=false;
                GuideHover.ShowHelp(app.playBtn,"Scaling QA", "This hover belongs to the launcher and must disappear when its owner loses focus.");
                var hover=(Control)typeof(GuideHover).GetField("box",BindingFlags.NonPublic|BindingFlags.Static).GetValue(null);
                check(hover!=null&&hover.Parent==app&&app.ClientRectangle.Contains(hover.Bounds),"hover remains inside launcher client");
                capture("tooltip-owned");
                app.OnDeactivate(EventArgs.Empty);
                check(!hover.Visible,"hover hides on launcher deactivation");
                using(var dialog=new MsgDialog("This is a long message used to check that the launcher keeps its questions inside the secondary menu. Buttons must remain reachable, and text should wrap instead of being clipped.","Display check",MessageBoxButtons.YesNoCancel))
                {
                    check(dialog.PutInLauncher(),"message dialog uses secondary container");
                    check(dialog.Parent.ClientRectangle.Contains(dialog.Bounds),"message dialog stays inside secondary container");
                    capture("message-dialog");
                    CheckScalingControls(dialog,check);
                    var heading=FindScalingControl<Label>(dialog);float fontSize=heading.Font.Size;
                    DisplayLayout before=app.displayLayout;Size beforeClient=app.ClientSize;
                    app.ChangeDisplay(new Rectangle(-30000,-30000,3840,2120),before.Dpi+48,true);
                    Application.DoEvents();
                    check(dialog.Parent.ClientRectangle.Contains(dialog.Bounds),"open dialog remains contained after DPI change");
                    check(Math.Abs(heading.Font.Size-fontSize*Theme.DisplayScale/before.Scale)<1,"open dialog fonts follow changed density");
                    capture("message-dialog-dpi-change");
                    dialog.Close();
                    app.changingDisplay=true;app.displayLayout=before;
                    Theme.DisplayScale=before.Scale;Theme.DisplayDpi=before.Dpi;Theme.Zoom=before.Scale;
                    app.MinimumSize=Size.Empty;app.ClientSize=beforeClient;app.changingDisplay=false;
                    app.LayoutResponsiveDisplay(true);
                }
                app.Installing=true;app.dlActive=true; app.LayoutDownloadClosePrompt();
                app.closeDownloadCard.Visible=true;app.closeDownloadCard.BringToFront();capture("close-download");
                app.dlActive=false;app.LayoutDownloadClosePrompt();capture("close-extraction");
                app.closeDownloadCard.Visible=false;app.Installing=false;app.RefreshPlayLabel();
                app.optionsTab=4; app.current="Options"; app.BuildPanel("Options"); capture("options-launcher");
                app.gPlanner=app.gBuilder=false; app.gTalents=null;
                app.RefreshPlayLabel();
                app.setupOpen=true;
                app.ShowSetupDialog();
                foreach(int page in full ? new[]{0,1,2,3,4,5,6,7} : new[]{0,1,4})
                {
                    app.setupPage=page; app.current="Setup"; app.BuildPanel("Setup"); capture("setup-"+page);
                    CheckScalingControls(app.panel,check);
                    check(app.setupCloud==false,"setup cloud remains opt-in at page "+page);
                }
                File.WriteAllLines(Path.Combine(output,"checks.txt"),checks.ToArray());
                File.WriteAllText(Path.Combine(output,"completed.txt"),"Display scaling preview completed successfully.");
                app.quitting = true;
            }
        }

        void ValidateCommunitySkipQueue(Action<bool,string> check)
        {
            var oldQueue = new List<GuideVideo>(communityQueue);
            int oldFilter = communityQueueFilter, oldCommunityFilter = communityFilter;
            GuideVideo oldCurrent = communityCurrent, oldChoice = communityChoice;
            DateTime oldPicked = communityPicked;
            try
            {
                var first = new GuideVideo { Url = "https://www.youtube.com/watch?v=queue-first" };
                var second = new GuideVideo { Url = "https://www.youtube.com/watch?v=queue-second" };
                var third = new GuideVideo { Url = "https://www.youtube.com/watch?v=queue-third" };
                var candidates = new List<GuideVideo> { first, second, third };
                communityFilter = communityQueueFilter = 1;
                communityQueue.Clear();
                communityQueue.Add(second);
                communityQueue.Add(third);
                communityCurrent = first;
                communityPicked = DateTime.Now;

                PrepareCommunityQueue(candidates);
                ReturnCommunityVideoToQueue(first, candidates);
                check(communityQueue.Count == 3 && communityQueue[0] == second && communityQueue[2] == first,
                    "skipped community video returns behind the other videos in its active queue");

                RemoveCommunityVideoFromQueue(second);
                communityCurrent = second;
                ReturnCommunityVideoToQueue(second, candidates);
                check(communityQueue.Count == 3 && communityQueue[0] == third && communityQueue[2] == second,
                    "skipping again advances to another video and keeps the skipped one available later");

                communityFilter = 2;
                PrepareCommunityQueue(new List<GuideVideo> { third });
                check(communityQueueFilter == 2 && communityCurrent == null && communityQueue.Count == 1 && communityQueue[0] == third,
                    "community filter changes start a separate queue");
            }
            finally
            {
                communityQueue.Clear(); communityQueue.AddRange(oldQueue);
                communityQueueFilter = oldFilter; communityFilter = oldCommunityFilter;
                communityCurrent = oldCurrent; communityChoice = oldChoice; communityPicked = oldPicked;
            }
        }

        void RunScalingInteractions(Action<bool,string> check)
        {
            OpenGuideTab(-3); PlannerClass("Warrior");
            int routeCount;
            bool routesValid = ValidateGuideLinkRoutes(out routeCount);
            check(routesValid,"all guide hyperlinks resolve to a valid page or intentional hover-only target ("+routeCount+" routes)");
            Rectangle monitorBounds=Rectangle.FromLTRB(-1920,0,0,1080);
            Rectangle workingBounds=Rectangle.FromLTRB(-1920,32,0,1040);
            int nativeFrameWidth=Math.Max(0,Width-ClientSize.Width),nativeFrameHeight=Math.Max(0,Height-ClientSize.Height);
            Rectangle maximized=DisplayLayout.MaximizedWindowBounds(workingBounds,nativeFrameWidth,nativeFrameHeight);
            double maximizedRatio=(double)(maximized.Width-nativeFrameWidth)/(maximized.Height-nativeFrameHeight);
            check(workingBounds.Contains(maximized)&&Math.Abs(maximizedRatio-DisplayLayout.WindowAspectRatio)<.01,
                "maximize fits the aspect ratio within a negative-origin monitor work area");
            check(Math.Abs((maximized.Left-workingBounds.Left)-(workingBounds.Right-maximized.Right))<=1 &&
                  Math.Abs((maximized.Top-workingBounds.Top)-(workingBounds.Bottom-maximized.Bottom))<=1,
                "ratio-fitted maximize is centered in the monitor work area");
            IntPtr sizingRect=System.Runtime.InteropServices.Marshal.AllocHGlobal(16);
            try
            {
                Rectangle initial=Rectangle.FromLTRB(120,140,1020,760);
                WriteScalingRect(sizingRect,initial);
                ConstrainSizingRectangle(8,sizingRect); // bottom-right corner
                Rectangle corner=ParseSuggestedDisplayBounds(sizingRect);
                int frameWidth=Math.Max(0,Width-ClientSize.Width),frameHeight=Math.Max(0,Height-ClientSize.Height);
                double clientRatio=(double)(corner.Width-frameWidth)/(corner.Height-frameHeight);
                check(Math.Abs(clientRatio-DisplayLayout.WindowAspectRatio)<.01,"interactive corner resize preserves the launcher design ratio");
                WriteScalingRect(sizingRect,initial);
                ConstrainSizingRectangle(1,sizingRect); // left edge: right side stays fixed
                Rectangle leftEdge=ParseSuggestedDisplayBounds(sizingRect);
                check(leftEdge.Right==initial.Right,"horizontal resize keeps the opposite edge anchored");
                WriteScalingRect(sizingRect,initial);
                ConstrainSizingRectangle(6,sizingRect); // bottom edge: top stays fixed
                Rectangle bottomEdge=ParseSuggestedDisplayBounds(sizingRect);
                check(bottomEdge.Top==initial.Top,"vertical resize keeps the opposite edge anchored");
            }
            finally{System.Runtime.InteropServices.Marshal.FreeHGlobal(sizingRect);}
            GearSheet.FillSet("Warrior","Legendary",8,plannerSlots);
            var jewel=Items.Jewelry.Find(delegate(Jewel value){ return value.Kind=="Amulet"&&!value.IsCodex; });
            if(jewel!=null) plannerSlots["Neck"]=jewel;
            SwitchTheoryTool(false);
            foreach(Talent talent in Talents.Of("Warrior")[0].Talents) if(liveBuild.Add(talent)) break;
            string ranks=liveBuild.Save();
            SwitchTheoryTool(true);
            check(plannerSlots.Count>=8 && liveBuild.Save()==ranks,"peel retains gear and talent points");
            PlannerClass("Priest"); PlannerClass("Warrior");
            check(plannerSlots.Count>=8&&liveBuild.Save()==ranks,"class switch restores per-class gear/stars/talents");
            List<AsBuildsBuild> builds; string error;
            string code=AsBuildsInterop.ExportBuild("Warrior","Scaling QA",ranks,CapturePlannerJewelry());
            check(AsBuildsInterop.TryRead(code,out builds,out error),"ASBuilder export/import converter parses its code");
            check(builds.Count==1&&builds[0].Class=="Warrior","ASBuilder code retains class");
            plannerSlots.Clear(); ApplyPlannerJewelry("Warrior",builds[0].Gear);
            check(plannerSlots.Count>=8,"ASBuilder code restores selected gear");
            if(jewel!=null) check(plannerSlots.ContainsKey("Neck"),"neck slot maps to ASBuilder amulet and back");
            gPlanner=true;gBuilder=false;gTalents=null;current="Guide";PlannerClass("Warrior");BuildPanel("Guide");
            RunScalingGearRows(check);
            ComboBox saved=FindScalingControl<ComboBox>(panel);
            check(saved!=null,"shared save index exists");
            saved.Text="Scaling QA";
            InvokeScalingButton(FindScalingButton(panel,"SAVE"));
            check(SavedTalentBuilds(Config.Load()).Exists(delegate(TalentBuildChoice value){return value.Class=="Warrior"&&value.Name=="Scaling QA";}),"Save action records class-scoped build in isolated memory");
            var savedChoice=saved.SelectedItem as TalentBuildChoice;
            check(savedChoice!=null&&savedChoice.Class=="Warrior"&&savedChoice.Gear.Count>=8,"saved profile index retains the selected class gear");
            plannerSlots.Clear();
            InvokeScalingButton(FindScalingButton(saved.Parent,"LOAD"));
            check(plannerSlots.Count>=8,"Load action restores saved equipment");
            ShowTheoryCodeOverlay("Warrior",false);
            TextBox input=FindScalingControl<TextBox>(panel);
            check(input!=null,"import textbox exists");
            input.Text="unfinished draft";
            int originalWidth=ClientSize.Width;
            changingDisplay=true; ClientSize=new Size(originalWidth-20,ClientSize.Height); changingDisplay=false;
            LayoutResponsiveDisplay(true);
            check(!input.IsDisposed&&input.Text=="unfinished draft","resize retains unsubmitted import text");
            changingDisplay=true; ClientSize=new Size(originalWidth,ClientSize.Height); changingDisplay=false;
            BuildPanel("Guide"); LayoutResponsiveDisplay(false);
            DisplayLayout original=displayLayout; Size originalClient=ClientSize;
            float oldScale=Theme.DisplayScale;
            ChangeDisplay(new Rectangle(-30000,-30000,3840,2120),192,true);
            check(displayLayout.Dpi==192&&Math.Abs(Theme.DisplayScale-new DisplayLayout(new Rectangle(-30000,-30000,3840,2120),192,ReadInterfaceScale()).Scale)<.001f,"DPI change rebuilds display density");
            check(plannerSlots.Count>=8,"DPI change preserves equipped gear");
            check(Left<0&&Top<0,"off-screen DPI test stays off-screen");
            string oldPreference=Cfg.Str("interface_scale");
            Cfg.Set("interface_scale","1");
            var wide=new Rectangle(-30000,-30000,3840,2120);
            changingDisplay=true;MinimumSize=Size.Empty;displayLayout=new DisplayLayout(wide,96,1);
            Theme.DisplayScale=Theme.Zoom=displayLayout.Scale;Theme.DisplayDpi=96;
            Bounds=new Rectangle(-29000,-29000,1100,690);changingDisplay=false;
            Rectangle beforeBounds=Bounds;
            var targetLayout=new DisplayLayout(wide,192,1);
            var expectedBounds=targetLayout.Clamp(new Rectangle(beforeBounds.Location,new Size(beforeBounds.Width*2,beforeBounds.Height*2)));
            ChangeDisplay(wide,192,true,beforeBounds.Location);
            check(Bounds==expectedBounds,"DPI increase scales captured bounds once before MinimumSize changes");
            IntPtr nativeRect=System.Runtime.InteropServices.Marshal.AllocHGlobal(16);
            try
            {
                int[][] samples={new[]{-2200,-1200,-200,-100},new[]{1500,100,3700,1480}};
                foreach(int[] coordinates in samples)
                {
                    for(int i=0;i<4;i++)System.Runtime.InteropServices.Marshal.WriteInt32(nativeRect,i*4,coordinates[i]);
                    check(ParseSuggestedDisplayBounds(nativeRect)==Rectangle.FromLTRB(coordinates[0],coordinates[1],coordinates[2],coordinates[3]),"native DPI RECT retains full bounds including negative origins");
                }
            }
            finally{System.Runtime.InteropServices.Marshal.FreeHGlobal(nativeRect);}
            Cfg.Set("interface_scale",oldPreference);
            changingDisplay=true; displayLayout=original; Theme.DisplayScale=oldScale; Theme.DisplayDpi=original.Dpi; Theme.Zoom=oldScale;
            MinimumSize=Size.Empty; ClientSize=originalClient; changingDisplay=false;
            LayoutResponsiveDisplay(true);
            RunScalingVideoLayout(check);
        }

        static bool ValidateGuideLinkRoutes(out int routeCount)
        {
            GuideLinks.Reset();
            GuideLinks.Matching("shadow", Int32.MaxValue); // force the same route table used by live guide search
            var field = typeof(GuideLinks).GetField("links", BindingFlags.NonPublic | BindingFlags.Static);
            var routes = field == null ? null : field.GetValue(null) as System.Collections.IDictionary;
            routeCount = 0;
            if (routes == null) return false;
            var seen = new HashSet<GuideLink>();
            bool valid = true;
            List<GuideTopic> topics = Data.Guide;
            foreach (System.Collections.DictionaryEntry entry in routes)
            {
                GuideLink link = entry.Value as GuideLink;
                if (link == null || !seen.Add(link)) continue;
                routeCount++;
                if (link.Topic == -2 || link.Topic == -3) continue; // the two builder destinations
                if (link.Topic == -1 && link.Class == null) continue; // hover-only terms intentionally have no route
                if (link.Topic < 0 || link.Topic >= topics.Count) { valid = false; continue; }
                GuideTopic topic = topics[link.Topic];
                if (string.Equals(topic.Title, "Archive", StringComparison.OrdinalIgnoreCase)) valid = false;
                if (link.Page >= topic.Pages.Count) valid = false;
                if (link.Page < -1) valid = false;
            }
            return valid;
        }

        static void CheckClassArticleLinkTargets(LauncherApp app,int classTopic,int page,string cls,Action<bool,string> check)
        {
            if(app.classSpellTargets.Count>0)
            {
                string spell=null;
                foreach(string name in app.classSpellTargets.Keys){spell=name;break;}
                GuideLink link=new GuideLink{Term=spell,FindText=spell,Topic=classTopic,Page=page};
                check(app.NavigateClassInline(link),cls+" spell link lands on its in-article spell row");
            }
            Talent talent=null;
            foreach(var tree in Talents.Of(cls)) if(tree.Talents.Count>0){talent=tree.Talents[0];break;}
            if(talent!=null)
            {
                GuideLink link=new GuideLink{Term=talent.Name,FindText=talent.Name,Class=cls,Topic=classTopic,Page=page};
                check(app.NavigateClassInline(link),cls+" talent link lands on its in-article talent description");
            }
            Jewel codex=Items.Jewelry.Find(delegate(Jewel item){return item.IsCodex&&string.Equals(item.Class,cls,StringComparison.OrdinalIgnoreCase);});
            if(codex!=null)
            {
                GuideLink link=new GuideLink{Term=codex.Name,FindText=codex.Spell,Topic=classTopic,Page=page};
                check(app.NavigateClassInline(link),cls+" Codex link lands on its in-article Codex entry");
            }
            GearPiece gear=Items.Gear.Find(delegate(GearPiece item){return string.Equals(item.Class,cls,StringComparison.OrdinalIgnoreCase);});
            int itemsTopic=Data.Guide.FindIndex(delegate(GuideTopic topic){return string.Equals(topic.Title,"Items",StringComparison.OrdinalIgnoreCase);});
            int gearPage=itemsTopic<0?-1:Data.Guide[itemsTopic].Pages.FindIndex(delegate(GuidePage value){return string.Equals(value.Title,cls+" Gear",StringComparison.OrdinalIgnoreCase);});
            if(gear!=null&&itemsTopic>=0&&gearPage>=0)
            {
                GuideLink link=new GuideLink{Term=gear.Name,FindText=gear.Name,Topic=itemsTopic,Page=gearPage};
                check(app.NavigateClassInline(link),cls+" gear link lands on its in-article item section");
            }
            if(app.classArticleScroll!=null)app.classArticleScroll.ScrollTo(0);
        }

        static void WriteScalingRect(IntPtr pointer,Rectangle rectangle)
        {
            System.Runtime.InteropServices.Marshal.WriteInt32(pointer,rectangle.Left);
            System.Runtime.InteropServices.Marshal.WriteInt32(pointer,4,rectangle.Top);
            System.Runtime.InteropServices.Marshal.WriteInt32(pointer,8,rectangle.Right);
            System.Runtime.InteropServices.Marshal.WriteInt32(pointer,12,rectangle.Bottom);
        }

        void RunScalingVideoLayout(Action<bool,string> check)
        {
            // A blank local browser exercises native hosting/retention without contacting
            // a video provider or starting visible playback during off-screen QA.
            gPlanner=gBuilder=false;gTalents=null;current="Guide";
            gVideo=new GuideVideo {Title="Scaling video fixture",Url="about:blank"};
            ClearPanel("Video");
            guideVideoFrame=new Bordered(Theme.CardEdge,1);panel.Controls.Add(guideVideoFrame);
            guideVideoBrowser=new WebBrowser {Dock=DockStyle.Fill};guideVideoFrame.Controls.Add(guideVideoBrowser);
            guideVideoOpen=Put(new WowButton("OPEN IN BROWSER ›",delegate{},190,32,9),0,0);
            LayoutGuideVideo();
            var browser=guideVideoBrowser;Size previous=ClientSize;
            changingDisplay=true;ClientSize=new Size(Math.Max(650,previous.Width-200),Math.Max(450,previous.Height-80));changingDisplay=false;
            LayoutResponsiveDisplay(true);
            check(browser==guideVideoBrowser&&!browser.IsDisposed,"video resize keeps the same native browser instance");
            check(panel.ClientRectangle.Contains(guideVideoOpen.Bounds),"video controls stay inside resized pane");
            check(panel.ClientRectangle.Contains(guideVideoFrame.Bounds),"video viewport stays inside resized pane");
            changingDisplay=true;ClientSize=previous;changingDisplay=false;
            gVideo=null;BuildPanel("Guide");LayoutResponsiveDisplay(true);
        }

        [System.Runtime.InteropServices.DllImport("user32.dll")]
        static extern int GetGuiResources(IntPtr process,int flag);
        void RunScalingStress(Action<bool,string> check)
        {
            for(int pass=0;pass<110;pass++)
            {
                PlannerClass(Items.Classes[pass%Items.Classes.Length]);
                gPlanner=pass%2==0;gBuilder=!gPlanner;gTalents=gBuilder?plannerClass:null;
                BuildPanel("Guide");
                displayResizeTimer.Stop(); Application.DoEvents();
                using(var bitmap=new Bitmap(panel.Width,panel.Height))panel.DrawToBitmap(bitmap,new Rectangle(Point.Empty,bitmap.Size));
                Application.DoEvents();
                if(pass==9) {GC.Collect();GC.WaitForPendingFinalizers();}
            }
            GC.Collect();GC.WaitForPendingFinalizers();
            int resources=GetGuiResources(System.Diagnostics.Process.GetCurrentProcess().Handle,0);
            check(resources<1500,"110 tool rebuilds leave bounded GDI resources ("+resources+")");
            check(Application.OpenForms.Count<=2,"tool rebuilds do not leave extra Forms open");
        }

        static T FindScalingControl<T>(Control parent) where T:Control
        {
            foreach(Control child in parent.Controls)
            {
                T match=child as T; if(match!=null) return match;
                match=FindScalingControl<T>(child); if(match!=null)return match;
            }
            return null;
        }
        static WowButton FindScalingButton(Control parent,string label)
        {
            foreach(Control child in parent.Controls)
            {
                WowButton button=child as WowButton; if(button!=null&&button.Label==label) return button;
                button=FindScalingButton(child,label); if(button!=null)return button;
            }
            return null;
        }
        void InvokeScalingButton(WowButton button)
        {
            if(button==null)throw new InvalidOperationException("Missing test action");
            Point center=new Point(button.Width/2,button.Height/2);
            if(button.Grayed||!button.Visible||!button.Enabled||button.Parent.GetChildAtPoint(new Point(button.Left+center.X,button.Top+center.Y))!=button)
                throw new InvalidOperationException("Button is not reachable: "+button.Label);
            // The native hit rectangle is checked above. Invoke the exact attached action
            // synchronously here; gear row selection separately exercises real mouse-up hit testing.
            var eventField=typeof(WowButton).GetField("Pressed",BindingFlags.Instance|BindingFlags.NonPublic);
            var action=eventField==null?null:eventField.GetValue(button) as Action;
            if(action==null)throw new InvalidOperationException("Button has no action: "+button.Label);
            action();
        }

        static string ScalingControlSnapshot(Control control,int depth)
        {
            string state="";
            foreach(FieldInfo field in typeof(Control).GetFields(BindingFlags.Instance|BindingFlags.NonPublic))
                if(field.Name.ToLowerInvariant().Contains("state")) state+=" "+field.Name+"="+field.GetValue(control);
            string line=new string(' ',depth*2)+control.GetType().Name+" handle="+control.IsHandleCreated+" created="+control.Created+" "+state+"\n";
            foreach(Control child in control.Controls)line+=ScalingControlSnapshot(child,depth+1);
            return line;
        }

        void RenderAddonUpdateChecks(string output,Action<string> capture,Action<bool,string> check)
        {
            setupOpen=false;
            string fixture=Path.Combine(Path.GetFullPath(output),"addon-fixture"); Directory.CreateDirectory(fixture);
            foreach(string folder in new[]{"ASTinyPlates","BlackwellUI","KeijinUI","SurvivorUI","SurvivorsDragonUi","UnknownAddon"})
            {
                string path=Path.Combine(fixture,folder); Directory.CreateDirectory(path);
                File.WriteAllText(Path.Combine(path,folder+".toc"),"## Interface: 11200\n## Title: "+folder+"\n");
            }
            Cfg.Set("addons_dir",fixture); Cfg.Set("addons_advanced",false);
            addonCheckedAt=DateTime.UtcNow; mirrorDates=new Dictionary<string,DateTime>(StringComparer.OrdinalIgnoreCase);
            foreach(var entry in InstalledManagedAddons())
            {
                if(IsDynamicGitHubAddonUrl(entry.Url))
                {
                    var release=new GitHubAddonRelease {Tag="v-test",Sha256=new string('a',64),Url=entry.Url};
                    dynamicAddonReleases[entry.Url]=release; dynamicAddonReleaseChecked.Add(entry.Url);
                    if(entry.Folders.Contains("KeijinUI")) AddonInstalledRelease()[entry.Name]=release.Identity;
                    if(entry.Folders.Contains("SurvivorsDragonUi")) AddonPins()[entry.Name]="kept.zip";
                }
                else
                {
                    string file=Uri.UnescapeDataString(entry.Url.Substring(entry.Url.LastIndexOf('/')+1));
                    mirrorDates[file]=DateTime.UtcNow; mirrorIdentities[file]="fixture-mirror";
                }
                if(entry.Folders.Contains("BlackwellUI")) addonUpdateProblems[entry.Name]="Example: GitHub request limit reached. Try again later.";
            }
            var tiny=CatalogAddonForFolder("ASTinyPlates"); var keijin=CatalogAddonForFolder("KeijinUI"); var dragon=CatalogAddonForFolder("SurvivorsDragonUi");
            check(AddonHasUpdate(tiny),"changed creator digest is detected");
            check(!AddonHasUpdate(keijin),"unchanged creator digest is current");
            check(!AddonHasUpdate(dragon),"pinned addon is excluded from automatic updates");
            check(!AddonHasUpdate(CatalogAddonForFolder("BlackwellUI")),"failed checks are not claimed as confirmed updates");
            RefreshAddonUpdateBadge(); check(addonUpdateCount>0,"confirmed updates activate badge");
            current="Addons";addonMode="installed";BuildPanel("Addons");capture("addons-installed");
            check(CatalogAddonForFolder("UnknownAddon")==null,"unmanaged addon has no invented release source");
            foreach(Control parent in panel.Controls)
                foreach(Control child in parent.Controls)
                    if(child is ScrollPanel)
                        foreach(Control row in child.Controls)
                        {
                            var checkbox=row as CheckBox;
                            if(checkbox!=null)
                            {
                                int textWidth=Math.Max(1,checkbox.Width-(checkbox.Font.Height-2)-8);
                                Size needed=TextRenderer.MeasureText(checkbox.Text,checkbox.Font,new Size(textWidth,int.MaxValue),TextFormatFlags.WordBreak|TextFormatFlags.NoPrefix);
                                check(checkbox.Height>=needed.Height,"installed addon name wraps without clipping");
                            }
                            var update=row as WowButton;
                            if(update==null || update.Label!="UPDATE") continue;
                            foreach(Control other in child.Controls)
                            {
                                var remove=other as WowButton;
                                if(remove!=null && remove.Label=="X" && Math.Abs(remove.Top-update.Top)<2)
                                    check(update.Right<=remove.Left,"individual update and uninstall buttons do not overlap");
                            }
                        }
            addonMode="updates";BuildPanel("Addons");capture("addon-update-details");
            check(ReadLauncherWhispers().Count>0,"timestamped whisper posts load");
            var post=ReadLauncherWhispers()[0];check(post.Author=="Slimjim" && post.Message.Length>0,"whisper author and message retained");
            if (!CompactDisplay)
            {
                check(feedbackField.Visible && feedbackField.Top >= WhisperArea.Bottom, "feedback field sits below whispers");
                feedbackField.Text = "Sample feedback";
                check(feedbackReplyPanel.Visible && feedbackReplyText.Text == FeedbackReply, "typing opens the requested feedback response");
                var threadLink = feedbackReplyText.Links[0];
                check(FeedbackReply.Substring(threadLink.Start, threadLink.Length) == "Discord Thread", "only Discord Thread is linked");
                capture("whisper-feedback");
                closeFeedback.PerformClick();
                check(!feedbackReplyPanel.Visible && feedbackField.Text == "Sample feedback", "closing preserves the feedback draft");
                feedbackField.Text = "";
            }
            string package=Path.Combine(Path.GetFullPath(output),"package"), folderPath=Path.Combine(package,"FixtureAddon");
            Directory.CreateDirectory(folderPath);File.WriteAllText(Path.Combine(folderPath,"FixtureAddon.toc"),"## Interface: 11200\n");
            File.WriteAllText(Path.Combine(folderPath,"version.txt"),"new");
            string zip=Path.Combine(Path.GetFullPath(output),"fixture-update.zip");System.IO.Compression.ZipFile.CreateFromDirectory(package,zip);
            string disabled=Path.Combine(fixture,"FixtureAddon.disabled");Directory.CreateDirectory(disabled);File.WriteAllText(Path.Combine(disabled,"version.txt"),"old");
            InstallAddonUpdateSafely(zip,fixture,null,null,new List<string>{"FixtureAddon"},true);
            check(!Directory.Exists(Path.Combine(fixture,"FixtureAddon")) && File.ReadAllText(Path.Combine(disabled,"version.txt"))=="new","disabled addon updates without being enabled or colliding with its old folder");
            bool rejected=false;
            try {InstallAddonUpdateSafely(zip,fixture,null,new string('0',64),new List<string>{"FixtureAddon"},true);} catch(UserError) {rejected=true;}
            check(rejected && File.ReadAllText(Path.Combine(disabled,"version.txt"))=="new","digest failure preserves installed addon");
            int visited=0, successes=-1, failures=-1;
            RunAddonUpdateQueue(new List<AddonEntry>{tiny,keijin,dragon},delegate{return true;},
                delegate(AddonEntry entry,Action<bool> done){visited++;done(visited>1);done(true);},
                delegate(int passed,int failed){successes=passed;failures=failed;});
            check(visited==3 && successes==2 && failures==1,"batch continues after failure and counts each callback once");
            visited=0;
            RunAddonUpdateQueue(new List<AddonEntry>{tiny},delegate{return false;},
                delegate(AddonEntry entry,Action<bool> done){visited++;done(true);},delegate(int passed,int failed){});
            check(visited==0,"batch pauses before downloads when game/sync gate closes");
            File.WriteAllText(Path.Combine(output,"success.txt"),"Addon update checks passed");
        }

        static void CheckScalingControls(Control parent,Action<bool,string> check)
        {
            foreach(Control child in parent.Controls)
            {
                if(!child.Visible) continue;
                if(child is WowButton && !(parent is ScrollPanel))
                    check(parent.ClientRectangle.Contains(child.Bounds),"button fits "+child.AccessibleName+" in "+parent.GetType().Name);
                CheckScalingControls(child,check);
            }
        }

        static void DrawScalingTree(Control control, Bitmap bitmap, int x, int y, Rectangle clip)
        {
            if (!control.Visible || control.Width < 1 || control.Height < 1) return;
            Rectangle area = Rectangle.Intersect(clip, new Rectangle(x, y, control.Width, control.Height));
            if (area.Width < 1 || area.Height < 1) return;
            using (var own = new Bitmap(control.Width, control.Height))
            {
                own.SetResolution(Theme.DisplayDpi, Theme.DisplayDpi);
                control.DrawToBitmap(own, new Rectangle(Point.Empty, own.Size));
                using (var graphics = Graphics.FromImage(bitmap))
                {
                    graphics.SetClip(area);
                    graphics.DrawImageUnscaled(own, x, y);
                }
            }
            for (int i = control.Controls.Count - 1; i >= 0; i--)
            {
                Control child = control.Controls[i];
                DrawScalingTree(child, bitmap, x + child.Left, y + child.Top, area);
            }
        }
    }
}
