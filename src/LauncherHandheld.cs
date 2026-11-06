// Steam Deck and handhelds, the launcher's side (Handheld.cs says what it runs on). Opt-in: none of this happens until
// STEAM DECK EXPERIMENTAL is pressed (Options > Launcher, or setup's first page on a Steam Deck); off, the launcher
// behaves as before.
//   Game Mode: the launcher fills the screen with no frame, and its drop-down lists open as a list inside the launcher.
//   Playing:   the launcher steps aside once the game starts (hidden in Game Mode, minimized on Windows) and comes back
//              when the game closes, so the cloud upload is seen. On by default for Steam Deck / Handheld, off on a PC
//              (a Windows PC keeps the launcher in its window, as since v3.05). Options > Launcher changes both.
//   After:     optionally the launcher closes once the game has closed and the cloud save is uploaded (back to Steam).
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;

namespace AzerothLauncher
{
    public partial class LauncherApp
    {
        bool gameModeScreen;                                  // borderless and full screen (Steam Game Mode)
        bool steppedAsideForGame;                             // hidden or minimized while the game runs

        bool DeckFeaturesOn { get { return Handheld.Experimental || Handheld.ForceGameMode; } }

        public bool HideWhilePlaying()
        {
            return DeckFeaturesOn && Cfg.Bool("hide_while_playing", Handheld.GameMode || Handheld.HandheldChosen);
        }

        public bool CloseAfterPlaying() { return DeckFeaturesOn && Cfg.Bool("close_after_playing", false); }

        // STEAM DECK EXPERIMENTAL: on or off at once (full screen in Game Mode follows), remembered
        void ToggleSteamDeckExperimental()
        {
            bool on = !Handheld.Experimental;
            Handheld.Experimental = on;
            Cfg.Set("steam_deck_experimental", on);
            Cfg.Save();
            if (on) ApplyGameModeScreen(); else LeaveGameModeScreen();
            LogStatusOnly(Handheld.Describe());
            SetStatus(on ? "Steam Deck experimental is on." : "Steam Deck experimental is off.");
            if (setupOpen) BuildPanel("Setup"); else RebuildCurrent();
        }

        // STEAM DECK EXPERIMENTAL turned off in Game Mode: back to the normal window
        void LeaveGameModeScreen()
        {
            if (!gameModeScreen) return;
            gameModeScreen = false;
            FormBorderStyle = FormBorderStyle.Sizable;
            MaximizeBox = true;
            ClientSize = displayLayout.InitialClientSize;
            MinimumSize = MinimumWindowSize();
            CenterToScreen();
        }

        // a line in the Status Log only (the status line keeps what it shows)
        void LogStatusOnly(string text)
        {
            lock (statusLog)
            {
                statusLog.Add(new KeyValuePair<DateTime, string>(DateTime.Now, text));
                if (statusLog.Count > StatusLogMax) statusLog.RemoveRange(0, statusLog.Count - StatusLogMax);
            }
        }

        // Game Mode: no frame, the whole screen. Called once the window's normal display handling is set up.
        void ApplyGameModeScreen()
        {
            if (PreviewMode || !Handheld.GameMode) return;
            gameModeScreen = true;
            FormBorderStyle = FormBorderStyle.None;
            MaximizeBox = false;
            MinimumSize = Size.Empty;
            StartPosition = FormStartPosition.Manual;
            Bounds = Screen.PrimaryScreen.Bounds;
        }

        // after a display change: stay on the whole screen (instead of the normal window's size and minimum)
        void FitGameModeScreen()
        {
            MinimumSize = Size.Empty;
            Rectangle screen = Screen.FromControl(this).Bounds;
            if (WindowState != FormWindowState.Normal) WindowState = FormWindowState.Normal;
            if (Bounds != screen) Bounds = screen;
        }

        // the game is running: out of its way
        void StepAsideForGame()
        {
            if (steppedAsideForGame || !HideWhilePlaying() || !Visible) return;
            steppedAsideForGame = true;
            LogStatusOnly(gameModeScreen ? "The launcher is hidden while the game runs." : "The launcher is minimized while the game runs.");
            if (gameModeScreen) Hide();                       // Game Mode has no taskbar to minimize to
            else WindowState = FormWindowState.Minimized;
        }

        // the game has closed: back on screen, so the cloud upload and anything it says are seen
        void ReturnAfterGame()
        {
            if (!steppedAsideForGame) return;
            steppedAsideForGame = false;
            ShowWindowFront();
            if (gameModeScreen) FitGameModeScreen();
            After(1, RepaintAfterRestore);
        }

        // a question while the launcher is out of the game's way: show the launcher first, or it is asked out of sight
        // (only when the launcher put itself away: a launcher the player minimized keeps its questions, as before)
        void RevealForQuestion()
        {
            if (!steppedAsideForGame && !(gameModeScreen && !Visible)) return;
            if (Visible && WindowState != FormWindowState.Minimized) return;
            steppedAsideForGame = false;
            ShowWindowFront();
            if (gameModeScreen) FitGameModeScreen();
        }

        // after the session: close back to Steam when that is chosen and nothing needs saying
        bool CloseAfterSession(List<string> problems)
        {
            if (!CloseAfterPlaying() || problems.Count > 0 || PreviewMode) return false;
            SetStatus("The game has closed and the cloud save is uploaded. Closing the launcher...");
            After(1500, QuitApp);
            return true;
        }
    }

    // A drop-down list shown as a list of buttons inside the launcher (Steam Game Mode draws one window at a time, so a
    // separate drop-down window may not show). Built from the items of the drop-down it stands in for.
    public class MenuChooser : WowDialog
    {
        public MenuChooser(string title, List<KeyValuePair<string, Action>> items) : base(title)
        {
            BackAction = Close;
            Begin();
            Heading(title, null, 26);
            foreach (var kv in items)
            {
                Action act = kv.Value;
                Body.Add(B(kv.Key, delegate { Close(); WowButton.Fire(act); }, 340, 38, 11), Align.Center, 26, 3, 26, 3);
            }
            Body.Add(B("CANCEL", Close, 160, 34, 10), Align.Center, 26, 12, 26, 16);
            Finish();
        }

        public static void Open(string title, List<KeyValuePair<string, Action>> items)
        {
            if (items == null || items.Count == 0) return;
            new MenuChooser(title, items).ShowInside();
        }

        // the items of a themed drop-down (ThemeMenus.Create), its checked ones marked with a tick
        public static void OpenFor(ContextMenuStrip menu)
        {
            if (menu == null || menu.IsDisposed) return;
            var items = new List<KeyValuePair<string, Action>>();
            foreach (ToolStripItem item in menu.Items) AddItem(items, item as ToolStripMenuItem, "");
            Open("Choose", items);
        }

        static void AddItem(List<KeyValuePair<string, Action>> items, ToolStripMenuItem item, string parent)
        {
            if (item == null || !item.Available || !item.Enabled) return;
            string text = (item.Text ?? "").Replace("&&", "\u0001").Replace("&", "").Replace("\u0001", "&");
            if (item.HasDropDownItems)
            {
                foreach (ToolStripItem child in item.DropDownItems) AddItem(items, child as ToolStripMenuItem, parent + text + " › ");
                return;
            }
            ToolStripMenuItem target = item;
            items.Add(new KeyValuePair<string, Action>((item.Checked ? "✓ " : "") + parent + text, delegate { target.PerformClick(); }));
        }
    }
}
