using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Collections.Generic;
using System.Reflection;
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
                check(app.ClientRectangle.Contains(app.side.Bounds),"secondary pane inside client");
                check(app.ClientRectangle.Contains(app.playBtn.Bounds),"Play action inside client");
                check(app.ResponsiveSideRect.Bottom<app.FooterTop,"secondary pane above footer");
                var negativeMonitor=new DisplayLayout(new Rectangle(-3840,-2160,3840,2120),192,0);
                check(negativeMonitor.WorkArea.Contains(negativeMonitor.Clamp(new Rectangle(500,500,2200,1400))),"negative monitor coordinates clamp correctly");
                app.RunScalingInteractions(check);
                if(Array.IndexOf(args,"--stress")>=0) app.RunScalingStress(check);
                app.GuideGroupPage(2);
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
                    }
                    app.current="Guide"; app.SetGuideView(classes,paladin); capture("class-paladin");
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
                app.GuideGroupPage(2); app.PlannerClass("Warrior"); GearSheet.FillSet("Warrior","Legendary",8,app.plannerSlots);
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
                app.quitting = true;
            }
        }

        void RunScalingInteractions(Action<bool,string> check)
        {
            GuideGroupPage(2); PlannerClass("Warrior");
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
            BuildPanel("Guide");
            ComboBox saved=FindScalingControl<ComboBox>(panel);
            check(saved!=null,"shared save index exists");
            saved.Text="Scaling QA";
            InvokeScalingButton(FindScalingButton(panel,"SAVE"));
            check(SavedTalentBuilds(Config.Load()).Exists(delegate(TalentBuildChoice value){return value.Class=="Warrior"&&value.Name=="Scaling QA";}),"Save action records class-scoped build in isolated memory");
            plannerSlots.Clear();
            InvokeScalingButton(FindScalingButton(panel,"LOAD"));
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
            changingDisplay=true; displayLayout=original; Theme.DisplayScale=oldScale; Theme.DisplayDpi=original.Dpi; Theme.Zoom=oldScale;
            MinimumSize=Size.Empty; ClientSize=originalClient; changingDisplay=false;
            LayoutResponsiveDisplay(true);
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
        static void InvokeScalingButton(WowButton button)
        {
            if(button==null)throw new InvalidOperationException("Missing test action");
            Action action=(Action)typeof(WowButton).GetField("Pressed",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(button);
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
