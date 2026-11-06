// Steam Deck and other handhelds: what the launcher runs on, so it can behave well there.
//   Wine / Proton: Wine's ntdll exports wine_get_version, and Wine keeps its own registry key.
//   Steam Deck:    Steam sets SteamDeck=1 on a Deck, in Desktop Mode and Game Mode alike.
//   Game Mode:     Steam sets SteamGamepadUI=1 there, and gamescope (Game Mode's screen) sets XDG_CURRENT_DESKTOP=gamescope.
//                  One window is on screen at a time and there is no taskbar.
// Wine hands the Linux environment to Windows programs, so these read the same way from inside Proton.
// All of it is opt-in for now: nothing changes until the player presses STEAM DECK EXPERIMENTAL (Options > Launcher,
// or setup's first page on a Steam Deck). --game-mode on the command line tries Game Mode's behaviour on a PC.
using System;
using System.Runtime.InteropServices;
using Microsoft.Win32;

namespace AzerothLauncher
{
    public static class Handheld
    {
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
        static extern IntPtr GetModuleHandle(string name);

        [DllImport("kernel32.dll", CharSet = CharSet.Ansi, ExactSpelling = true)]
        static extern IntPtr GetProcAddress(IntPtr module, string name);

        public static bool ForceGameMode;                     // --game-mode
        public static bool Experimental;                      // STEAM DECK EXPERIMENTAL is on
        public static string Device = "";                     // setup's answer: "handheld", "pc", or "" (not asked yet)

        static int wine = -1, steamDeck = -1, gameMode = -1;

        static string Env(string name)
        {
            try { return Environment.GetEnvironmentVariable(name) ?? ""; }
            catch (Exception) { return ""; }
        }

        // running through Wine or Proton (a Steam Deck, or Linux)
        public static bool Wine
        {
            get
            {
                if (wine < 0)
                {
                    bool found = false;
                    try
                    {
                        IntPtr ntdll = GetModuleHandle("ntdll.dll");
                        found = ntdll != IntPtr.Zero && GetProcAddress(ntdll, "wine_get_version") != IntPtr.Zero;
                    }
                    catch (Exception) { }
                    if (!found)
                        try { using (RegistryKey key = Registry.CurrentUser.OpenSubKey(@"Software\Wine")) found = key != null; }
                        catch (Exception) { }
                    wine = found ? 1 : 0;
                }
                return wine == 1;
            }
        }

        public static bool SteamDeck
        {
            get
            {
                if (steamDeck < 0) steamDeck = Env("SteamDeck") == "1" ? 1 : 0;
                return steamDeck == 1;
            }
        }

        // Game Mode's behaviour is in use: STEAM DECK EXPERIMENTAL is on and Steam's Game Mode is found (or --game-mode)
        public static bool GameMode { get { return ForceGameMode || (Experimental && DetectedGameMode); } }

        // Steam's Game Mode (gamescope) found: full screen, one window at a time
        public static bool DetectedGameMode
        {
            get
            {
                if (gameMode < 0)
                    gameMode = Env("SteamGamepadUI") == "1" ||
                               Env("XDG_CURRENT_DESKTOP").IndexOf("gamescope", StringComparison.OrdinalIgnoreCase) >= 0 ||
                               Env("GAMESCOPE_WAYLAND_DISPLAY").Length > 0 ? 1 : 0;
                return gameMode == 1;
            }
        }

        // what setup's first page suggests before the player picks
        public static bool LooksHandheld { get { return DetectedGameMode || SteamDeck; } }

        // worth offering STEAM DECK EXPERIMENTAL during setup
        public static bool OfferExperimental { get { return Wine || SteamDeck || DetectedGameMode; } }

        // the player chose Steam Deck / Handheld (or has not been asked and this looks like one)
        public static bool HandheldChosen
        {
            get { return Device == "handheld" || (Device.Length == 0 && LooksHandheld); }
        }

        // for the Status Log: where the launcher found itself
        public static string Describe()
        {
            string where = Wine ? (SteamDeck ? "Steam Deck (Proton)" : "Wine or Proton") : "Windows";
            if (DetectedGameMode) where += ", Steam Game Mode";
            if (ForceGameMode) where += ", Game Mode forced with --game-mode";
            if (Device.Length > 0) where += "; set up as " + (Device == "handheld" ? "Steam Deck / Handheld" : "Windows PC");
            return "Running on " + where + ". Steam Deck experimental is " + (Experimental ? "on." : "off.");
        }
    }
}
