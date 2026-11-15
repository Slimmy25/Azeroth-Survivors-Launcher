// The video player for Steam Deck and Linux (Wine / Proton): Chromium, through CEF (the Chromium Embedded Framework).
// On Windows the launcher shows YouTube in Edge (Community.cs); Proton has no Edge, and Wine's own web view is far too old
// for YouTube, so there the launcher carries its own Chromium. It is never used on Windows.
//
// One time only, the first video asks to download the player (about the size of a game patch) from the launcher's own
// GitHub release "video-player-cef150"; nothing else is installed and no other program or website is involved. The
// browser draws inside the launcher's window. Chromium's helper processes are the launcher's own exe started again with
// Chromium's --type=... arguments (Program.Main hands those to RunSubprocess before anything else happens).
//
// This talks to CEF's C API directly (no wrapper library), pinned to CEF 150's API version: the structs below follow
// CEF 150's headers (include/internal/cef_types.h, cef_types_win.h and the method order of cef_app.h, cef_client.h,
// cef_life_span_handler.h, cef_browser.h, cef_frame.h, cef_command_line.h). A different CEF build needs them checked.
// CEF is BSD-licensed; its license travels in the downloaded package (LICENSE.txt).
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;

namespace AzerothLauncher
{
    public static class CefPlayer
    {
        public const int ApiVersion = 15000;                  // CEF 150 (cef_api_versions.json "last")
        public const string ReleaseTag = "video-player-cef150";
        public const string AssetName = "video-player-win64.zip";
        public const string Repo = "Slimmy25/Azeroth-Survivors-Launcher";

        public static string PackageUrl
        {
            get { return "https://github.com/" + Repo + "/releases/download/" + ReleaseTag + "/" + AssetName; }
        }

        // where the player lives: beside the launcher's other data, one folder per CEF version
        public static string Dir { get { return Path.Combine(Path.Combine(Util.DataDir, "video-player"), "cef150"); } }

        // only for Wine / Proton, and only as a 64-bit process (CEF's Windows builds used here are 64-bit)
        public static bool Supported { get { return Handheld.Wine && IntPtr.Size == 8; } }

        // downloaded and unpacked completely (the marker is written last)
        public static bool Ready
        {
            get
            {
                try { return File.Exists(Path.Combine(Dir, "libcef.dll")) && File.Exists(Path.Combine(Dir, "ready.txt")); }
                catch (Exception) { return false; }
            }
        }

        // ---- the one-time download

        // the package's size in bytes from its release, or 0 when it can't be told
        public static long PackageSize()
        {
            try
            {
                var release = Archive.ReadGitHubReleaseMetadata("https://api.github.com/repos/" + Repo + "/releases/tags/" + ReleaseTag);
                object assets;
                if (release != null && release.TryGetValue("assets", out assets))
                    foreach (object a in (System.Collections.IEnumerable)assets)
                    {
                        var d = a as Dictionary<string, object>;
                        if (d == null || Json.Str(d, "name") != AssetName) continue;
                        object size;
                        if (d.TryGetValue("size", out size)) return Convert.ToInt64(size);
                    }
            }
            catch (Exception) { }
            return 0;
        }

        // downloads and unpacks the player; progress(done, total). Throws UserError on failure, and leaves nothing half-done.
        public static void Download(Action<long, long> progress, ManualResetEvent cancel)
        {
            string parent = Path.GetDirectoryName(Dir);
            Directory.CreateDirectory(parent);
            string zip = Path.Combine(parent, AssetName);
            string unpack = Dir + ".unpack";
            try
            {
                Net.Download(PackageUrl, zip, true, "video player", progress, cancel, 0);
                Util.DeleteTreeQuiet(unpack);
                Directory.CreateDirectory(unpack);
                Archive.ExtractZip(zip, unpack);
                if (!File.Exists(Path.Combine(unpack, "libcef.dll")))
                    throw new UserError("The video player download was incomplete. Please try again.");
                Util.DeleteTreeQuiet(Dir);
                Directory.Move(unpack, Dir);
                File.WriteAllText(Path.Combine(Dir, "ready.txt"), ReleaseTag);
            }
            finally
            {
                try { File.Delete(zip); } catch (Exception) { }
                Util.DeleteTreeQuiet(unpack);
            }
        }

        // ---- Chromium's helper processes: the launcher's exe started again by Chromium

        public static bool IsSubprocess(string[] args)
        {
            foreach (string a in args) if (a.StartsWith("--type=", StringComparison.Ordinal)) return true;
            return false;
        }

        // runs one of Chromium's helper processes and returns its exit code
        public static int RunSubprocess()
        {
            if (!LoadLibcef()) return 1;
            cef_api_hash(ApiVersion, 0);
            IntPtr args = MainArgs();
            return cef_execute_process(args, App, IntPtr.Zero);
        }

        // ---- the browser process (the launcher itself)

        static bool initialized, failed;

        // starts Chromium inside the launcher, once; false when it can't (it says why in the launcher's log)
        public static bool Start()
        {
            if (initialized) return true;
            if (failed || !Supported || !Ready) return false;
            try
            {
                if (!LoadLibcef()) { failed = true; return false; }
                cef_api_hash(ApiVersion, 0);
                string exe = Process.GetCurrentProcess().MainModule.FileName;
                string cache = Path.Combine(Path.GetDirectoryName(Dir), "cache");
                Directory.CreateDirectory(cache);
                var s = new NativeStruct();
                s.Size();                                     // size
                s.Int(1);                                     // no_sandbox: Wine can't run Chromium's sandbox
                s.Str(exe);                                   // browser_subprocess_path: the launcher's own exe
                s.Str(null);                                  // framework_dir_path (macOS)
                s.Str(null);                                  // main_bundle_path (macOS)
                s.Int(1);                                     // multi_threaded_message_loop: Chromium runs its own UI thread
                s.Int(0);                                     // external_message_pump
                s.Int(0);                                     // windowless_rendering_enabled
                s.Int(1);                                     // command_line_args_disabled: the launcher's own arguments aren't Chromium's
                s.Str(cache);                                 // cache_path
                s.Str(cache);                                 // root_cache_path
                s.Int(1);                                     // persist_session_cookies (YouTube's consent choice is kept)
                s.Str(null);                                  // user_agent
                s.Str(null);                                  // user_agent_product
                s.Str(null);                                  // locale
                s.Str(Path.Combine(Path.GetDirectoryName(Dir), "player-log.txt"));   // log_file
                s.Int(4);                                     // log_severity: LOGSEVERITY_ERROR (DEFAULT=0, VERBOSE, INFO, WARNING, ERROR)
                s.Int(0);                                     // log_items
                s.Str(null);                                  // javascript_flags
                s.Str(Dir);                                   // resources_dir_path
                s.Str(Path.Combine(Dir, "locales"));          // locales_dir_path
                s.Int(0);                                     // remote_debugging_port
                s.Int(0);                                     // uncaught_exception_stack_size
                s.UInt(0xFF000000);                           // background_color: black
                s.Str(null);                                  // accept_language_list
                s.Str(null);                                  // cookieable_schemes_list
                s.Int(0);                                     // cookieable_schemes_exclude_defaults
                s.Str(null);                                  // chrome_policy_id
                s.Int(0);                                     // chrome_app_icon_id
                s.Int(0);                                     // disable_signal_handlers
                s.Int(0);                                     // use_views_default_popup (14600)
                IntPtr settings = s.Build();
                initialized = cef_initialize(MainArgs(), settings, App, IntPtr.Zero) == 1;
                if (!initialized) failed = true;
                return initialized;
            }
            catch (Exception ex) { failed = true; Program.LogCrash("video player", ex); return false; }
        }

        // on the launcher's way out (the thread that started it)
        public static void Shutdown()
        {
            if (!initialized) return;
            try { CloseBrowser(); cef_shutdown(); } catch (Exception) { }
            initialized = false;
        }

        // ---- one browser at a time, inside a launcher control

        static IntPtr browser;                                // cef_browser_t*, while one is open
        static Action<IntPtr> created;                        // told the browser's own window once it exists

        // opens url inside the window parent (a launcher Panel's handle); whenCreated gets the browser's window
        // on Chromium's thread
        public static bool Open(IntPtr parent, Rectangle bounds, string url, Action<IntPtr> whenCreated)
        {
            if (!Start()) return false;
            CloseBrowser();
            created = whenCreated;
            var w = new NativeStruct();
            w.Size();                                         // size
            w.UInt(0);                                        // ex_style
            w.Str(null);                                      // window_name
            w.UInt(0x40000000 | 0x10000000 | 0x02000000 | 0x04000000 | 0x00010000);   // WS_CHILD|VISIBLE|CLIPCHILDREN|CLIPSIBLINGS|TABSTOP
            w.Int(bounds.X); w.Int(bounds.Y); w.Int(bounds.Width); w.Int(bounds.Height);   // bounds
            w.Ptr(parent);                                    // parent_window
            w.Ptr(IntPtr.Zero);                               // menu
            w.Int(0);                                         // windowless_rendering_enabled
            w.Int(0);                                         // shared_texture_enabled
            w.Int(0);                                         // external_begin_frame_enabled
            w.Ptr(IntPtr.Zero);                               // window (set by CEF)
            w.Int(2);                                         // runtime_style: CEF_RUNTIME_STYLE_ALLOY (no Chrome toolbar)
            IntPtr windowInfo = w.Build();
            var b = new NativeStruct();
            b.SizeOfBrowserSettings();
            IntPtr browserSettings = b.Build();
            IntPtr cefUrl = CefString(url);
            return cef_browser_host_create_browser(windowInfo, Client, cefUrl, browserSettings, IntPtr.Zero, IntPtr.Zero) == 1;
        }

        public static void CloseBrowser()
        {
            IntPtr b = browser;
            browser = IntPtr.Zero;
            created = null;
            if (b == IntPtr.Zero) return;
            try
            {
                IntPtr host = Call<GetPtr>(b, BrowserGetHost)(b);
                if (host != IntPtr.Zero) Call<CloseBrowserFn>(host, HostCloseBrowser)(host, 1);
            }
            catch (Exception) { }
        }

        // ---- CEF's C API

        // method slots: a ref-counted struct starts with its size and four ref-count functions (40 bytes on 64-bit),
        // then its methods in the order of the C++ class
        const int BaseSize = 40;
        const int AppOnBeforeCommandLineProcessing = 0, AppMethods = 5;
        const int ClientGetLifeSpanHandler = 13, ClientMethods = 19;
        const int LifeOnBeforePopup = 0, LifeOnAfterCreated = 3, LifeDoClose = 4, LifeOnBeforeClose = 5, LifeMethods = 6;
        const int BrowserGetHost = 1, BrowserGetMainFrame = 14;
        const int HostCloseBrowser = 1, HostGetWindowHandle = 5;
        const int CommandLineAppendSwitch = 14, CommandLineAppendSwitchWithValue = 15;

        [UnmanagedFunctionPointer(CallingConvention.StdCall)] delegate void RefVoid(IntPtr self);
        [UnmanagedFunctionPointer(CallingConvention.StdCall)] delegate int RefInt(IntPtr self);
        [UnmanagedFunctionPointer(CallingConvention.StdCall)] delegate IntPtr GetPtr(IntPtr self);
        [UnmanagedFunctionPointer(CallingConvention.StdCall)] delegate void CommandLineFn(IntPtr self, IntPtr processType, IntPtr commandLine);
        [UnmanagedFunctionPointer(CallingConvention.StdCall)] delegate void AppendSwitchFn(IntPtr self, IntPtr name);
        [UnmanagedFunctionPointer(CallingConvention.StdCall)] delegate void AppendSwitchValueFn(IntPtr self, IntPtr name, IntPtr value);
        [UnmanagedFunctionPointer(CallingConvention.StdCall)] delegate void BrowserFn(IntPtr self, IntPtr browser);
        [UnmanagedFunctionPointer(CallingConvention.StdCall)] delegate int BrowserIntFn(IntPtr self, IntPtr browser);
        [UnmanagedFunctionPointer(CallingConvention.StdCall)] delegate void CloseBrowserFn(IntPtr self, int force);

        // the structs the launcher provides (kept for the life of the process, so their ref counts never matter)
        static readonly List<Delegate> keep = new List<Delegate>();
        static IntPtr app, client, lifeSpan;

        static IntPtr App { get { if (app == IntPtr.Zero) BuildHandlers(); return app; } }
        static IntPtr Client { get { if (client == IntPtr.Zero) BuildHandlers(); return client; } }

        static void BuildHandlers()
        {
            app = Implement(AppMethods);
            SetMethod(app, AppOnBeforeCommandLineProcessing, new CommandLineFn(OnBeforeCommandLineProcessing));
            lifeSpan = Implement(LifeMethods);
            SetMethod(lifeSpan, LifeOnBeforePopup, new RefInt(delegate(IntPtr self) { return 1; }));   // popups are cancelled
            SetMethod(lifeSpan, LifeOnAfterCreated, new BrowserFn(OnAfterCreated));
            SetMethod(lifeSpan, LifeDoClose, new BrowserIntFn(delegate(IntPtr self, IntPtr b) { return 0; }));
            SetMethod(lifeSpan, LifeOnBeforeClose, new BrowserFn(delegate(IntPtr self, IntPtr b) { }));
            client = Implement(ClientMethods);
            SetMethod(client, ClientGetLifeSpanHandler, new GetPtr(delegate(IntPtr self) { return lifeSpan; }));
        }

        // a struct of n methods (all empty: CEF treats a missing method as "use the default"), with no-op ref counting
        static IntPtr Implement(int methods)
        {
            int size = BaseSize + IntPtr.Size * methods;
            IntPtr p = Marshal.AllocHGlobal(size);
            for (int i = 0; i < size; i++) Marshal.WriteByte(p, i, 0);
            Marshal.WriteIntPtr(p, 0, (IntPtr)size);
            WriteFn(p, 8, new RefVoid(delegate(IntPtr self) { }));                 // add_ref
            WriteFn(p, 16, new RefInt(delegate(IntPtr self) { return 0; }));       // release: never reaches zero
            WriteFn(p, 24, new RefInt(delegate(IntPtr self) { return 1; }));       // has_one_ref
            WriteFn(p, 32, new RefInt(delegate(IntPtr self) { return 1; }));       // has_at_least_one_ref
            return p;
        }

        static void SetMethod(IntPtr s, int index, Delegate d) { WriteFn(s, BaseSize + IntPtr.Size * index, d); }

        static void WriteFn(IntPtr s, int offset, Delegate d)
        {
            keep.Add(d);
            Marshal.WriteIntPtr(s, offset, Marshal.GetFunctionPointerForDelegate(d));
        }

        static T Call<T>(IntPtr s, int index) where T : class
        {
            IntPtr fn = Marshal.ReadIntPtr(s, BaseSize + IntPtr.Size * index);
            if (fn == IntPtr.Zero) throw new InvalidOperationException("CEF method missing");
            return Marshal.GetDelegateForFunctionPointer(fn, typeof(T)) as T;
        }

        // Chromium's switches for Wine: software drawing (no GPU process), videos may start by themselves
        static void OnBeforeCommandLineProcessing(IntPtr self, IntPtr processType, IntPtr commandLine)
        {
            try
            {
                var append = Call<AppendSwitchFn>(commandLine, CommandLineAppendSwitch);
                var appendValue = Call<AppendSwitchValueFn>(commandLine, CommandLineAppendSwitchWithValue);
                foreach (string sw in new[] { "no-sandbox", "disable-gpu", "disable-gpu-compositing", "disable-gpu-sandbox",
                                              "disable-features=CalculateNativeWinOcclusion", "no-first-run" })
                {
                    int eq = sw.IndexOf('=');
                    if (eq < 0) append(commandLine, CefString(sw));
                    else appendValue(commandLine, CefString(sw.Substring(0, eq)), CefString(sw.Substring(eq + 1)));
                }
                appendValue(commandLine, CefString("autoplay-policy"), CefString("no-user-gesture-required"));
            }
            catch (Exception) { }
        }

        static void OnAfterCreated(IntPtr self, IntPtr b)
        {
            browser = b;
            Action<IntPtr> tell = created;
            if (tell == null) return;
            try
            {
                IntPtr host = Call<GetPtr>(b, BrowserGetHost)(b);
                IntPtr hwnd = host == IntPtr.Zero ? IntPtr.Zero : Call<GetPtr>(host, HostGetWindowHandle)(host);
                tell(hwnd);
            }
            catch (Exception) { }
        }

        // cef_string_t (UTF-16): str, length, dtor. Kept alive for the process (a handful of short strings).
        static IntPtr CefString(string text)
        {
            IntPtr s = Marshal.AllocHGlobal(IntPtr.Size * 3);
            IntPtr chars = text == null ? IntPtr.Zero : Marshal.StringToHGlobalUni(text);
            Marshal.WriteIntPtr(s, 0, chars);
            Marshal.WriteIntPtr(s, IntPtr.Size, (IntPtr)(text == null ? 0 : text.Length));
            Marshal.WriteIntPtr(s, IntPtr.Size * 2, IntPtr.Zero);
            return s;
        }

        static IntPtr MainArgs()
        {
            IntPtr a = Marshal.AllocHGlobal(IntPtr.Size);
            Marshal.WriteIntPtr(a, 0, GetModuleHandle(null));
            return a;
        }

        static bool LoadLibcef()
        {
            try
            {
                SetDllDirectory(Dir);
                return LoadLibrary(Path.Combine(Dir, "libcef.dll")) != IntPtr.Zero;
            }
            catch (Exception) { return false; }
        }

        // a C struct written field by field with 64-bit alignment: size_t, int, cef_string_t, pointers
        sealed class NativeStruct
        {
            readonly List<byte> bytes = new List<byte>();
            readonly List<KeyValuePair<int, IntPtr>> pointers = new List<KeyValuePair<int, IntPtr>>();
            int sizeAt = -1;

            void Align(int n) { while (bytes.Count % n != 0) bytes.Add(0); }
            void Put(byte[] b) { bytes.AddRange(b); }

            public void Size() { Align(8); sizeAt = bytes.Count; Put(new byte[8]); }
            public void Int(int v) { Align(4); Put(BitConverter.GetBytes(v)); }
            public void UInt(uint v) { Align(4); Put(BitConverter.GetBytes(v)); }
            public void Ptr(IntPtr v) { Align(8); pointers.Add(new KeyValuePair<int, IntPtr>(bytes.Count, v)); Put(new byte[8]); }

            // a cef_string_t held inline: its characters are allocated and kept
            public void Str(string text)
            {
                Align(8);
                IntPtr chars = text == null ? IntPtr.Zero : Marshal.StringToHGlobalUni(text);
                pointers.Add(new KeyValuePair<int, IntPtr>(bytes.Count, chars));
                Put(new byte[8]);
                Put(BitConverter.GetBytes((long)(text == null ? 0 : text.Length)));
                Put(new byte[8]);                             // dtor: none
            }

            // cef_browser_settings_t with every value at its default: its size (CEF 150) and zeros
            public void SizeOfBrowserSettings()
            {
                Size();
                Int(0);                                       // windowless_frame_rate
                for (int i = 0; i < 6; i++) Str(null);        // font families
                for (int i = 0; i < 4; i++) Int(0);           // font sizes
                Str(null);                                    // default_encoding
                for (int i = 0; i < 12; i++) Int(0);          // remote_fonts ... webgl (cef_state_t)
                UInt(0);                                      // background_color
                Int(0); Int(0);                               // chrome_status_bubble, chrome_zoom_bubble
            }

            public IntPtr Build()
            {
                Align(8);
                byte[] all = bytes.ToArray();
                if (sizeAt >= 0) Array.Copy(BitConverter.GetBytes((long)all.Length), 0, all, sizeAt, 8);
                IntPtr p = Marshal.AllocHGlobal(all.Length);
                Marshal.Copy(all, 0, p, all.Length);
                foreach (var kv in pointers) Marshal.WriteIntPtr(p, kv.Key, kv.Value);
                return p;
            }
        }

        [DllImport("libcef.dll", CallingConvention = CallingConvention.Cdecl)] static extern IntPtr cef_api_hash(int version, int entry);
        [DllImport("libcef.dll", CallingConvention = CallingConvention.Cdecl)] static extern int cef_execute_process(IntPtr args, IntPtr app, IntPtr sandbox);
        [DllImport("libcef.dll", CallingConvention = CallingConvention.Cdecl)] static extern int cef_initialize(IntPtr args, IntPtr settings, IntPtr app, IntPtr sandbox);
        [DllImport("libcef.dll", CallingConvention = CallingConvention.Cdecl)] static extern void cef_shutdown();
        [DllImport("libcef.dll", CallingConvention = CallingConvention.Cdecl)]
        static extern int cef_browser_host_create_browser(IntPtr windowInfo, IntPtr client, IntPtr url, IntPtr settings, IntPtr extraInfo, IntPtr requestContext);

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)] static extern bool SetDllDirectory(string path);
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)] static extern IntPtr LoadLibrary(string path);
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] static extern IntPtr GetModuleHandle(string name);
    }
}
