using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Windows.Forms;

namespace AzerothLauncher
{
    sealed class AddonUpdateNavButton : WowButton
    {
        readonly PaintEventHandler badge;
        public AddonUpdateNavButton(string text,Action action,PaintEventHandler paint) : base(text,action,128,27,8.5f) {badge=paint;}
        protected override void OnPaint(PaintEventArgs e) {base.OnPaint(e);badge(this,e);}
    }
    public partial class LauncherApp
    {
        readonly Dictionary<string,string> addonUpdateProblems = new Dictionary<string,string>();
        readonly Dictionary<string,string> addonLastResults = new Dictionary<string,string>();
        readonly Dictionary<string,string> mirrorIdentities = new Dictionary<string,string>(StringComparer.OrdinalIgnoreCase);
        DateTime addonCheckedAt;
        bool autoAddonsBusy, addonRetryQueued;
        int addonUpdateCount;

        AddonEntry CatalogAddonForFolder(string folder)
        {
            foreach (var entry in Data.Addons)
                foreach (string installed in AddonFoldersOf(entry.Name))
                    if (string.Equals(installed, folder, StringComparison.OrdinalIgnoreCase)) return entry;
            return null;
        }
        string MirrorAddonIdentity(AddonEntry entry)
        {
            if (entry == null || IsDynamicGitHubAddonUrl(entry.Url)) return "";
            string identity;
            return mirrorIdentities.TryGetValue(Uri.UnescapeDataString(entry.Url.Substring(entry.Url.LastIndexOf('/')+1)), out identity) ? identity : "";
        }
        List<AddonEntry> InstalledManagedAddons()
        {
            var result = new List<AddonEntry>();
            foreach (var entry in Data.Addons)
                if (!entry.WorkInProgress && Net.IsWebAddress(entry.Url) && AddonFoldersOf(entry.Name).Count > 0) result.Add(entry);
            return result;
        }
        // Download and verify before touching an installed folder. Keep a rollback copy until all
        // incoming folders are in place, including disabled folders and multi-folder bundles.
        internal static List<string> InstallAddonUpdateSafely(string url,string dir,string archiveCopy,string digest,List<string> oldFolders,bool disabled,Func<bool> canCommit = null)
        {
            Directory.CreateDirectory(dir);
            string work=Path.Combine(Path.GetDirectoryName(Path.GetFullPath(dir)),".launcher-addon-"+Guid.NewGuid().ToString("N"));
            string stage=Path.Combine(work,"incoming"), backup=Path.Combine(work,"previous");
            var moved=new List<KeyValuePair<string,string>>(); var installed=new List<string>(); bool preserve=false;
            try
            {
                Directory.CreateDirectory(stage); Directory.CreateDirectory(backup);
                var names=Archive.InstallAddonZip(url,stage,archiveCopy,digest);
                if(canCommit!=null && !canCommit()) throw new UserError("The game or sync started while this addon downloaded. Its installed files were left unchanged; try again after it closes.");
                var folders=new HashSet<string>(names,StringComparer.OrdinalIgnoreCase);
                foreach(string folder in oldFolders) folders.Add(folder.EndsWith(Addons.DisabledSuffix)?folder.Substring(0,folder.Length-Addons.DisabledSuffix.Length):folder);
                foreach(string folder in folders)
                    foreach(string suffix in new[]{"",Addons.DisabledSuffix})
                    {
                        string original=Path.Combine(dir,folder+suffix);
                        if(!Directory.Exists(original)) continue;
                        string kept=Path.Combine(backup,folder+suffix); Directory.Move(original,kept);
                        moved.Add(new KeyValuePair<string,string>(kept,original));
                    }
                foreach(string folder in names)
                {
                    string target=Path.Combine(dir,folder+(disabled?Addons.DisabledSuffix:""));
                    Directory.Move(Path.Combine(stage,folder),target); installed.Add(target);
                }
                return names;
            }
            catch(Exception problem)
            {
                foreach(string target in installed) Util.DeleteTreeQuiet(target);
                foreach(var pair in moved)
                    try { Directory.Move(pair.Key,pair.Value); }
                    catch(Exception) { preserve=true; }
                if(preserve) throw new UserError("The addon update failed. Its previous files were kept for recovery in "+backup+". "+problem.Message,problem);
                throw;
            }
            finally { if(!preserve) Util.DeleteTreeQuiet(work); }
        }

        void AskMirrorDates(Action after = null, bool refresh = false, AddonEntry only = null)
        {
            if (mirrorAsking) { SetStatus("Checking addon releases..."); return; }
            if (!refresh && mirrorDates != null && DateTime.UtcNow-addonCheckedAt < TimeSpan.FromMinutes(10))
            { if(after != null) after(); return; }
            if (PreviewMode) return;
            mirrorAsking = true;
            var entries = only == null ? InstalledManagedAddons() : new List<AddonEntry> { only };
            Bg(delegate
            {
                var dates = new Dictionary<string,DateTime>(StringComparer.OrdinalIgnoreCase);
                var identities = new Dictionary<string,string>(StringComparer.OrdinalIgnoreCase);
                var live = new Dictionary<string,GitHubAddonRelease>(StringComparer.OrdinalIgnoreCase);
                var errors = new Dictionary<string,string>();
                bool mirrors = false;
                foreach(var entry in entries) if(!IsDynamicGitHubAddonUrl(entry.Url)) mirrors=true;
                if (mirrors) try
                {
                    var release=ReadGitHubRelease("https://api.github.com/repos/Slimmy25/Azeroth-Survivors-Launcher/releases/tags/addons",refresh);
                    foreach(object value in Json.AsList(Json.Get(release,"assets")) ?? new List<object>())
                    {
                        var asset=Json.AsDict(value); string name=Json.Str(asset,"name"); DateTime updated;
                        if(DateTime.TryParse(Json.Str(asset,"updated_at"),CultureInfo.InvariantCulture,DateTimeStyles.AdjustToUniversal|DateTimeStyles.AssumeUniversal,out updated)) dates[name]=updated;
                        identities[name]="mirror|"+Json.Str(asset,"id")+"|"+Json.Str(asset,"updated_at")+"|"+Json.Str(asset,"digest");
                    }
                }
                catch(Exception e) { foreach(var entry in entries) if(!IsDynamicGitHubAddonUrl(entry.Url)) errors[entry.Name]=e.Message; }
                foreach(var entry in entries)
                {
                    if(!IsDynamicGitHubAddonUrl(entry.Url))
                    {
                        string filename=Uri.UnescapeDataString(entry.Url.Substring(entry.Url.LastIndexOf('/')+1));
                        if(!errors.ContainsKey(entry.Name) && !identities.ContainsKey(filename)) errors[entry.Name]="This ZIP is missing from the launcher's mirror release.";
                        continue;
                    }
                    try { if(!live.ContainsKey(entry.Url)) live[entry.Url]=ResolveGitHubAddonRelease(entry.Url,refresh); }
                    catch(Exception e) { errors[entry.Name]=e.Message; }
                }
                Ui(delegate
                {
                    if(mirrorDates==null) mirrorDates=new Dictionary<string,DateTime>(StringComparer.OrdinalIgnoreCase);
                    foreach(var pair in dates) mirrorDates[pair.Key]=pair.Value;
                    foreach(var pair in identities) mirrorIdentities[pair.Key]=pair.Value;
                    foreach(var pair in live) { dynamicAddonReleases[pair.Key.TrimEnd('/')]=pair.Value; dynamicAddonReleaseChecked.Add(pair.Key.TrimEnd('/')); }
                    foreach(var entry in entries) { addonUpdateProblems.Remove(entry.Name); if(errors.ContainsKey(entry.Name)) addonUpdateProblems[entry.Name]=errors[entry.Name]; }
                    addonCheckedAt=DateTime.UtcNow; mirrorAsking=false;
                    RefreshAddonUpdateBadge();
                    if(after!=null) after();
                    else if(current=="Addons") BuildPanel("Addons");
                });
            });
        }
        void QueueAddonUpdateCheck()
        {
            if(addonRetryQueued || Program.DisplayPreviewBuild || PreviewMode) return;
            addonRetryQueued=true;
            After(30000,delegate { addonRetryQueued=false; AutoUpdateAddons(); });
        }
        void AutoUpdateAddons()
        {
            if(PreviewMode || Program.DisplayPreviewBuild || autoAddonsBusy) return;
            if(setupOpen || !GateOpen() || GameRunningFlag || Util.GameRunning() || SyncBusy || addonDlBusy || startupLauncherCheck || Updating)
            { QueueAddonUpdateCheck(); return; }
            AskMirrorDates(delegate { RunAddonUpdates(null,false); });
        }
        void CheckAddonUpdates(AddonEntry only, bool install)
        {
            if(addonDlBusy || autoAddonsBusy || mirrorAsking) { SetStatus("An addon operation is already running."); return; }
            if(install && (setupOpen || AddonsLocked() || Util.GameRunning())) return;
            if(only!=null && AddonSetting(AddonPins(),only.Name)!=null)
            {
                if(Msg.Show(this,"This addon is pinned to an older copy. Replace it with the newest release?","Addon updates",MessageBoxButtons.YesNo,MessageBoxIcon.Question)!=DialogResult.Yes) return;
                AddonPins().Remove(only.Name); string legacy=LegacyAddonName(only.Name);
                if(!string.IsNullOrEmpty(legacy)) AddonPins().Remove(legacy); Cfg.Save();
            }
            SetStatus("Checking addon releases...");
            AskMirrorDates(delegate
            {
                if(install) RunAddonUpdates(only,true);
                else { addonMode="updates"; SetStatus("Addon release check complete."); if(current=="Addons") BuildPanel("Addons"); }
            },true,only);
        }
        void RunAddonUpdates(AddonEntry only, bool manual)
        {
            if(autoAddonsBusy || addonDlBusy) return;
            var todo=new List<AddonEntry>();
            foreach(var entry in InstalledManagedAddons()) if((only==null || entry.Name==only.Name) && AddonHasUpdate(entry)) todo.Add(entry);
            // A single addon's UPDATE button: say what its own repo reported, so a click never looks like nothing happened.
            if(only!=null && todo.Count==0 && !addonUpdateProblems.ContainsKey(only.Name) && IsDynamicGitHubAddonUrl(only.Url))
            {
                GitHubAddonRelease latest=DynamicGitHubRelease(only.Url);
                SetStatus(only.Name+" is up to date"+(latest!=null && latest.Tag!=null ? " ("+latest.Tag+")" : "")+".");
            }
            autoAddonsBusy=true;
            RunAddonUpdateQueue(todo,delegate {return !GameRunningFlag && !Util.GameRunning() && !SyncBusy && !setupOpen;},
                delegate(AddonEntry entry,Action<bool> done)
                {
                    addonLastResults.Remove(entry.Name);
                    DownloadAddon(entry.Name,entry.Url,delegate
                    {
                        string error; done(addonLastResults.TryGetValue(entry.Name,out error) && error==null);
                    },true);
                },delegate(int succeeded,int failed)
                {
                    autoAddonsBusy=false; RefreshAddonUpdateBadge();
                    SetStatus("Addons updated: "+succeeded+". Failed: "+failed+". Check errors: "+addonUpdateProblems.Count+".");
                    if(manual) { addonMode="updates"; if(current=="Addons") BuildPanel("Addons"); }
                    QueueAddonUpdateCheck();
                });
        }
        internal static void RunAddonUpdateQueue(List<AddonEntry> entries,Func<bool> canContinue,
            Action<AddonEntry,Action<bool>> download,Action<int,int> complete)
        {
            int index=0,succeeded=0,failed=0; Action next=null;
            next=delegate
            {
                if(index>=entries.Count || !canContinue()) {complete(succeeded,failed);return;}
                var entry=entries[index++];bool finished=false;
                Action<bool> done=delegate(bool success)
                {
                    if(finished) return; finished=true;
                    if(success) succeeded++;else failed++;
                    next();
                };
                try {download(entry,done);} catch(Exception) {done(false);}
            };
            next();
        }
        void OpenAddonUpdates()
        {
            addonMode="updates"; OpenMain("Addons");
            AskMirrorDates();
        }
        void AddonUpdatesView()
        {
            SetCrumbs("Addons",(Action)delegate {SetAddonMode("installed");},"Updates",null);
            var all=Put(new WowButton("UPDATE ALL",delegate {CheckAddonUpdates(null,true);},120,27,9),20,8);
            var check=Put(new WowButton("CHECK AGAIN",delegate {CheckAddonUpdates(null,false);},120,27,9),all.Right+Theme.Px(8),8);
            all.Grayed=check.Grayed=mirrorAsking||autoAddonsBusy||addonDlBusy;
            var sp=MakeThinScroll(panel,new Rectangle(20,all.Bottom+Theme.Px(8),PW-40,Math.Max(30,PH-all.Bottom-Theme.Px(20))),Theme.Ink,Theme.Bronze,Theme.GoldDim,Theme.Gold);
            int y=Theme.Px(6), width=Math.Max(50,sp.ClientSize.Width-Theme.Px(18));
            foreach(var entry in InstalledManagedAddons())
            {
                var item=entry; string error; string status=AddonSetting(AddonPins(),item.Name)!=null?"Pinned to an older copy; automatic updates paused.":
                    addonUpdateProblems.TryGetValue(item.Name,out error)?"Could not check/update: "+error:
                    mirrorAsking?"Checking release...":addonCheckedAt==DateTime.MinValue?"Not checked yet.":AddonHasUpdate(item)?"Update available.":"Up to date with its configured source.";
                string source=IsDynamicGitHubAddonUrl(item.Url)?"Creator release":"Launcher mirror (updates when its ZIP is refreshed)";
                var label=UI.Text(item.Name+"\n"+status+"\n"+source,9,FontStyle.Regular,Theme.Text,Theme.Ink,Math.Max(40,width-Theme.Px(90)),false);
                label.Size=VStack.Measure(label);label.Location=new Point(Theme.Px(5),y);sp.Controls.Add(label);
                var button=new WowButton("UPDATE",delegate {CheckAddonUpdates(item,true);},78,25,8);
                button.Location=new Point(width-button.Width,y);button.Grayed=mirrorAsking||autoAddonsBusy||addonDlBusy;sp.Controls.Add(button);
                y+=Math.Max(label.Height,button.Height)+Theme.Px(14);
            }
            if(y==Theme.Px(6)) {var text=UI.Text("No installed addons with a managed release source.",10,FontStyle.Regular,Theme.Text,Theme.Ink,width);text.Location=new Point(5,y);sp.Controls.Add(text);}
        }
        void RefreshAddonUpdateBadge()
        {
            addonUpdateCount=0;
            foreach(var entry in InstalledManagedAddons()) if(AddonHasUpdate(entry)) addonUpdateCount++;
            WowButton button;
            if(nav.TryGetValue("Addons",out button))
            {
                button.Label=addonUpdateCount>0 ? "" : "ADDONS";
                button.AccessibleName=Lang.T("Addons")+(addonUpdateCount>0?" — Updates Available!":"");
                button.Invalidate();
            }
        }
        void PaintAddonUpdateBadge(object sender, PaintEventArgs e)
        {
            if(addonUpdateCount==0) return;
            var control=(Control)sender; var saved=e.Graphics.Save();
            e.Graphics.SetClip(control.ClientRectangle);
            float flagWidth=control.Width*.49f;
            using(var labelInk=new SolidBrush(Theme.Gold))
            using(var format=new StringFormat {Alignment=StringAlignment.Center,LineAlignment=StringAlignment.Center})
                e.Graphics.DrawString(Lang.T("ADDONS"),Theme.FFit(8,FontStyle.Bold),labelInk,
                    new RectangleF(flagWidth,0,control.Width-flagWidth,control.Height),format);
            e.Graphics.TranslateTransform(flagWidth/2f,control.Height/2f);e.Graphics.RotateTransform(-12);
            var box=new RectangleF(-flagWidth/2f,-control.Height*.46f,flagWidth,control.Height*.92f);
            using(var fill=new SolidBrush(Theme.Hex("#8b2821"))) e.Graphics.FillRectangle(fill,box);
            using(var font=new Font(control.Font.FontFamily,Math.Max(7,7*Theme.DisplayScale),FontStyle.Bold,GraphicsUnit.Pixel))
            using(var ink=new SolidBrush(Theme.Gold))
            using(var format=new StringFormat {Alignment=StringAlignment.Center,LineAlignment=StringAlignment.Center})
                e.Graphics.DrawString("Updates\nAvailable!",font,ink,box,format);
            e.Graphics.Restore(saved);
        }
    }
}
