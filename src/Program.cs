// Azeroth Survivors Launcher (2006-style look), C# version.
// Built by build.bat with the C# compiler that is part of Windows (.NET Framework 4.8), so the exe is a small,
// plain .NET program: no packer, no self-extracting runtime, nothing unpacked to a temp folder at start.
using System;
using System.Diagnostics;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;

[assembly: AssemblyTitle("Azeroth Survivors Launcher")]
[assembly: AssemblyDescription("Azeroth Survivors Launcher")]
[assembly: AssemblyCompany("Azeroth Survivors")]
[assembly: AssemblyProduct("Azeroth Survivors Launcher")]
[assembly: AssemblyCopyright("Copyright (c) 2026 Azeroth Survivors")]
[assembly: AssemblyVersion("3.22.0.0")]
[assembly: AssemblyFileVersion("3.20.0.0")]
[assembly: AssemblyInformationalVersion("3.20")]
[assembly: ComVisible(false)]
[assembly: System.Runtime.Versioning.TargetFramework(".NETFramework,Version=v4.8", FrameworkDisplayName = ".NET Framework 4.8")]

namespace AzerothLauncher
{
    static class Program
    {
#if AZS_RELEASE_BUILD
        public const bool LocalDevelopmentBuild = false;
#else
        public const bool LocalDevelopmentBuild = true;
#endif

#if AZS_DISPLAY_PREVIEW
        public const bool DisplayPreviewBuild=true;
        const string MutexName="Local\\AzerothSurvivorsDisplayPreview";
        const string EventName="Local\\AzerothSurvivorsDisplayPreviewShow";
#else
        public const bool DisplayPreviewBuild=false;
        const string MutexName="Local\\AzerothSurvivorsLauncher";
        const string EventName="Local\\AzerothSurvivorsLauncherShow";
#endif

        // Preview and public launcher use separate instance identities. Nothing listens on the network.
        [DllImport("user32.dll")]
        static extern bool AllowSetForegroundWindow(int processId);

        [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
        static extern int SetCurrentProcessExplicitAppUserModelID(string appId);

        [STAThread]
        static void Main(string[] args)
        {
            EmbeddedLibraries.Register();
            NativeDisplay.Enable();
            int scalingPreview = Array.IndexOf(args, "--preview-scaling");
            if (scalingPreview >= 0 && args.Length > scalingPreview + 1)
            {
                Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
                Application.ThreadException += delegate(object sender,ThreadExceptionEventArgs failure)
                {
                    System.IO.Directory.CreateDirectory(args[scalingPreview+1]);
                    System.IO.File.WriteAllText(System.IO.Path.Combine(args[scalingPreview+1],"native-error.txt"),failure.Exception.ToString());
                    Environment.Exit(1);
                };
                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);
                try { LauncherApp.RenderScalingPreview(args); }
                catch(Exception error) { System.IO.Directory.CreateDirectory(args[scalingPreview+1]); System.IO.File.WriteAllText(System.IO.Path.Combine(args[scalingPreview+1],"error.txt"),error.ToString()); Environment.Exit(1); }
                return;
            }
            int preview = Array.IndexOf(args, "--preview-stats");     // developer tool, see StatsPreview.cs
            if (preview >= 0 && args.Length > preview + 2)
            {
                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);
                LauncherApp.RenderStatsPreview(args[preview + 1], args[preview + 2]);
                Environment.Exit(0);
            }
            // Put the visible first-run file beside the exe before settings or update checks. It stays here through
            // self-update; the running, updated launcher moves it into its per-install data folder before setup.
            try { Util.CreateSetupBootstrapMarker(); }
            catch (Exception ex)
            {
                MessageBox.Show("The launcher could not create First-Time-Setup.txt beside its executable.\n\n" +
                                "Move the launcher to a folder where you can write files, then try again.\n\n" + ex.Message,
                                "Azeroth Survivors Launcher", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }
            Lang.Load(Lang.Default(Config.Load().Str("language")));     // the player's language (Options > Language)
            int au = Array.IndexOf(args, "--after-update");
            bool afterUpdate = au >= 0;
            if (afterUpdate)
            {
                int oldPid;
                if (args.Length > au + 1 && int.TryParse(args[au + 1], out oldPid))
                {
                    try { Process.GetProcessById(oldPid).WaitForExit(30000); }
                    catch (Exception) { }                    // already exited
                }
            }
            bool first;
            using (var mutex = new Mutex(true, MutexName, out first))
            {
                if (!first && afterUpdate)                   // started by a launcher update: wait for the old one to close
                {
                    try { first = mutex.WaitOne(20000); }
                    catch (AbandonedMutexException) { first = true; }
                }
                if (!first)
                {
                    // already running: bring that window forward and quit
                    if (!WakeExisting()) Msg.Show("The launcher is already running. Look for its window on the taskbar.",
                                                   "Azeroth Survivors Launcher");
                    return;
                }
                SelfUpdate.CleanUp();                        // the previous exe after a launcher update
                try { SetCurrentProcessExplicitAppUserModelID(DisplayPreviewBuild ? "Aethus.AzerothSurvivors.DisplayPreview" : "Aethus.AzerothSurvivors.Launcher"); }   // own taskbar identity
                catch (Exception) { }
                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);
                Application.AddMessageFilter(new WheelRouter());
                using (var show = new EventWaitHandle(false, EventResetMode.AutoReset, EventName))
                {
                    var app = new LauncherApp();
                    SurvivorsForge.Start();                  // optional metadata; failure never blocks launcher startup
                    var listener = new Thread(delegate()
                    {
                        while (true)
                        {
                            try
                            {
                                show.WaitOne();
                                app.Ui(app.ShowWindowFront);
                            }
                            catch (Exception) { return; }
                        }
                    });
                    listener.IsBackground = true;
                    listener.Start();
                    Application.Run(app);
                }
                try { mutex.ReleaseMutex(); } catch (Exception) { }   // lets a just-updated launcher start at once
                GC.KeepAlive(mutex);
            }
        }

        // asks the running launcher to show its window; true if it answered
        static bool WakeExisting()
        {
            try { AllowSetForegroundWindow(-1); } catch (Exception) { }
            for (int i = 0; i < 3; i++)                      // the first copy may still be starting up
            {
                EventWaitHandle h;
                if (EventWaitHandle.TryOpenExisting(EventName, out h))
                {
                    using (h) h.Set();
                    return true;
                }
                Thread.Sleep(400);
            }
            return false;
        }
    }

    // The mouse wheel scrolls the list under the pointer (also when the pointer is over a label inside it),
    // like the Tk version did, instead of only the list that has the keyboard focus.
    class WheelRouter : IMessageFilter
    {
        [DllImport("user32.dll")]
        static extern IntPtr WindowFromPoint(System.Drawing.Point p);

        public bool PreFilterMessage(ref Message m)
        {
            if (m.Msg != 0x020A) return false;               // WM_MOUSEWHEEL
            try
            {
                int delta = (short)((m.WParam.ToInt64() >> 16) & 0xFFFF);
                Control c = Control.FromChildHandle(WindowFromPoint(Cursor.Position));
                while (c != null)
                {
                    if (c is TextBoxBase || c is ComboBox) return false;     // these scroll themselves
                    var w = c as IWheel;
                    if (w != null)
                    {
                        w.Wheel(delta);
                        return true;
                    }
                    c = c.Parent;
                }
            }
            catch (Exception) { }
            return false;
        }
    }
}
