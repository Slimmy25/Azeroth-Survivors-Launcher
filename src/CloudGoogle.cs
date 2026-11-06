// Cloud Save through Google Drive, the launcher's side (GoogleDrive.cs talks to Drive). Behind STEAM DECK EXPERIMENTAL.
// Signing in makes Cloud Save use Google Drive: the sync folder becomes the launcher's local copy of the Drive folder
// (<launcher data>\google-drive), which the unchanged cloud engine syncs with as before. Around every sync the copy is
// brought up to date from Drive first and what changed is sent back after (DriveDown / DriveUp).
// Choosing a sync folder again (CHANGE FOLDER) goes back to that folder; USE GOOGLE DRIVE returns without signing in.
using System;
using System.Drawing;
using System.IO;
using System.Windows.Forms;

namespace AzerothLauncher
{
    public partial class LauncherApp
    {
        static string GoogleCopyDir { get { return Path.Combine(Util.StateDir, "google-drive"); } }

        public bool UsingGoogleDrive()
        {
            return Handheld.Experimental && Cfg.Str("cloud_provider") == "gdrive" && Cfg.Str("gdrive_token").Length > 0;
        }

        // Google Drive chosen but STEAM DECK EXPERIMENTAL off: cloud saving pauses rather than syncing a lone local copy
        bool GoogleDrivePaused() { return Cfg.Str("cloud_provider") == "gdrive" && !UsingGoogleDrive(); }

        void LoadGoogleSignIn() { GoogleDrive.RefreshToken = GoogleDrive.Unprotect(Cfg.Str("gdrive_token")); }

        Func<string, bool> DriveQuestion()
        {
            return delegate(string question)
            {
                return UiCall<bool>(delegate { ShowWindowFront(); return Ask("Google Drive", question); });
            };
        }

        // before the engine reads the sync folder: the local copy made to match Google Drive. Null when done (or Drive
        // is not in use), else what went wrong (logged).
        string DriveDown()
        {
            if (!UsingGoogleDrive()) return null;
            try
            {
                LoadGoogleSignIn();
                GoogleDrive.Down(GoogleCopyDir, DriveQuestion(), delegate(string line) { SyncLog(line, Level.Info); });
                return null;
            }
            catch (Exception e)
            {
                SyncLog("Google Drive: " + e.Message, Level.Error);
                return e.Message;
            }
        }

        // after the engine: what it changed in the local copy, sent to Google Drive. Null when done, else what went wrong.
        string DriveUp()
        {
            if (!UsingGoogleDrive()) return null;
            try
            {
                LoadGoogleSignIn();
                GoogleDrive.Up(GoogleCopyDir, delegate(string line) { SyncLog(line, Level.Info); });
                return null;
            }
            catch (Exception e)
            {
                SyncLog("Google Drive: " + e.Message + " The upload is sent at the next sync.", Level.Warn);
                return e.Message;
            }
        }

        // SIGN IN WITH GOOGLE: a code to enter at google.com/device on a phone or PC; the launcher waits for it
        void ConnectGoogleDrive()
        {
            if (!GoogleDrive.Available)
            {
                Warn("Google Drive", "This launcher was built without its Google sign-in. Use a sync folder for now.");
                return;
            }
            if (SyncBusy || GameRunningFlag || Installing)
            {
                SetStatus("Another task is still running - wait for it to finish.");
                return;
            }
            SetStatus("Contacting Google...");
            bool cancelled = false;
            Bg(delegate
            {
                GoogleDrive.SignIn signIn;
                try { signIn = GoogleDrive.StartSignIn(); }
                catch (Exception e)
                {
                    Ui(delegate { SetStatus("Ready."); Warn("Google Drive", e.Message); });
                    return;
                }
                GoogleSignInDialog dialog = null;
                Ui(delegate
                {
                    SetStatus("Waiting for the Google sign-in...");
                    dialog = new GoogleSignInDialog(signIn.Url, signIn.UserCode, delegate { cancelled = true; });
                    dialog.ShowInside();
                });
                string refresh = null, problem = null;
                try { refresh = GoogleDrive.WaitForSignIn(signIn, delegate { return cancelled; }); }
                catch (Exception e) { problem = e.Message; }
                Ui(delegate
                {
                    if (dialog != null && !dialog.IsDisposed) dialog.Close();
                    if (refresh == null)
                    {
                        SetStatus(cancelled ? "Google sign-in cancelled." : "Ready.");
                        if (!cancelled && problem != null) Warn("Google Drive", problem);
                        return;
                    }
                    StartUsingGoogleDrive(refresh);
                });
            });
        }

        // signed in: Cloud Save switches to Google Drive. An existing sync folder's saves move into an empty Drive folder;
        // otherwise Drive's saves are what this device syncs with from now on.
        void StartUsingGoogleDrive(string refresh)
        {
            string oldFolder = Cfg.Str("cloud_provider") == "gdrive" ? "" : Cfg.Str("cloud_dir").Trim();   // a sync folder until now
            Cfg.Set("gdrive_token", GoogleDrive.Protect(refresh));
            Cfg.Set("cloud_provider", "gdrive");
            Cfg.Set("cloud_dir", GoogleCopyDir);
            Cfg.Set("cloud_enabled", true);
            Cfg.Save();
            GoogleDrive.RefreshToken = refresh;
            SyncBusy = true;
            SetStatus("Connecting Google Drive...");
            Bg(delegate
            {
                string email = "", note, problem = null;
                try
                {
                    email = GoogleDrive.Email();
                    GoogleDrive.ResetCopy(GoogleCopyDir);
                    bool empty = GoogleDrive.IsEmpty();
                    bool moveOver = empty && oldFolder.Length > 0 && !Util.SamePath(oldFolder, GoogleCopyDir) &&
                                    File.Exists(Path.Combine(oldFolder, "manifest.json"));
                    if (moveOver)
                    {
                        GoogleDrive.CopyFolder(oldFolder, GoogleCopyDir);
                        GoogleDrive.Up(GoogleCopyDir, delegate(string line) { SyncLog(line, Level.Info); });
                        note = "Your sync folder's saves and history were copied into Google Drive.";
                    }
                    else
                    {
                        GoogleDrive.Down(GoogleCopyDir, DriveQuestion(), delegate(string line) { SyncLog(line, Level.Info); });
                        note = empty ? "Google Drive is ready. Your progress is uploaded after you play." : "Google Drive's saves are ready to use.";
                    }
                }
                catch (Exception e) { note = null; problem = e.Message; }
                Ui(delegate
                {
                    SyncBusy = false;
                    if (email.Length > 0) { Cfg.Set("gdrive_email", email); Cfg.Save(); }
                    SetStatus("Google Drive connected" + (email.Length > 0 ? " (" + email + ")" : "") + ".");
                    if (note != null) SyncLog(note, Level.Good);
                    ApplyCloudVisibility();
                    if (problem != null)
                        Warn("Google Drive", "Signed in, but the first sync with Google Drive did not finish:\n" + problem +
                                             "\n\nIt is tried again at the next sync.");
                });
            });
        }

        // signed in before, using a sync folder now: back to Google Drive without signing in again
        void UseGoogleDriveAgain()
        {
            Cfg.Set("cloud_provider", "gdrive");
            Cfg.Set("cloud_dir", GoogleCopyDir);
            Cfg.Set("cloud_enabled", true);
            Cfg.Save();
            SetStatus("Cloud Save uses Google Drive again.");
            ApplyCloudVisibility();
        }

        void DisconnectGoogleDrive()
        {
            if (!Ask("Google Drive", "Sign out of Google Drive on this device?\n\nYour saves stay in Google Drive and on this device. " +
                                     "Choose a sync folder, or sign in again, to keep syncing."))
                return;
            string refresh = GoogleDrive.Unprotect(Cfg.Str("gdrive_token"));
            bool wasInUse = Cfg.Str("cloud_provider") == "gdrive";
            Cfg.Remove("gdrive_token");
            Cfg.Remove("gdrive_email");
            Cfg.Remove("cloud_provider");
            if (wasInUse) Cfg.Set("cloud_dir", "");
            Cfg.Save();
            GoogleDrive.RefreshToken = null;
            Bg(delegate { GoogleDrive.Revoke(refresh); });
            SetStatus("Signed out of Google Drive.");
            ApplyCloudVisibility();
        }

        // Options > Cloud Save, with STEAM DECK EXPERIMENTAL on: Google Drive's part of the page
        int OptionsGoogleDrive(int PW, int y)
        {
            bool signedIn = Cfg.Str("gdrive_token").Length > 0;
            bool inUse = UsingGoogleDrive();
            string email = Cfg.Str("gdrive_email");
            string who = email.Length > 0 ? " (" + email + ")" : "";
            var title = PutText("Google Drive (Steam Deck experimental)", 11, FontStyle.Bold, Theme.Gold, 20, y, PW - 40);
            y = BottomOf(title) + 4;
            string text = !GoogleDrive.Available ? "This launcher was built without its Google sign-in, so Google Drive is not offered yet."
                : inUse ? "Cloud Save uses Google Drive" + who + ", in the folder " + GoogleDrive.FolderName +
                          ". No sync app is needed, also in Steam's Game Mode."
                : signedIn ? "Signed in" + who + ", but Cloud Save uses a sync folder right now."
                : "Sync straight with Google Drive: no sync app needed, also in Steam's Game Mode. You sign in on your phone or PC with a short code.";
            var note = PutText(text, 9.5f, FontStyle.Italic, Theme.GoldDim, 20, y, PW - 40);
            y = BottomOf(note) + 6;
            if (!GoogleDrive.Available) return y + 6;
            if (!signedIn)
            {
                var signIn = Put(new WowButton("SIGN IN WITH GOOGLE", ConnectGoogleDrive, 220, 30, 9), 20, y);
                return signIn.Bottom + 12;
            }
            int x = 20;
            if (!inUse)
            {
                var use = Put(new WowButton("USE GOOGLE DRIVE", UseGoogleDriveAgain, 190, 30, 9), x, y);
                x = use.Right + 10;
            }
            var signOut = Put(new WowButton("SIGN OUT", DisconnectGoogleDrive, 140, 30, 9), x, y);
            return signOut.Bottom + 12;
        }
    }

    // The sign-in code, inside the launcher: open the address on a phone or PC and enter the code. Closes by itself once
    // Google says the player signed in; CANCEL stops waiting.
    public class GoogleSignInDialog : WowDialog
    {
        public GoogleSignInDialog(string url, string code, Action cancelled) : base("Google Drive")
        {
            BackAction = delegate { if (cancelled != null) cancelled(); Close(); };
            Begin();
            Heading("Sign in to Google Drive", null, 26);
            Para("On your phone or computer, open", 11, FontStyle.Regular, Theme.Text, 26, 0, 4, 460);
            Para(url, 15, FontStyle.Bold, Theme.Gold, 26, 0, 10, 460);
            Para("and enter this code:", 11, FontStyle.Regular, Theme.Text, 26, 0, 4, 460);
            Para(code, 24, FontStyle.Bold, Theme.Gold, 26, 0, 12, 460);
            Para("Then allow Azeroth Survivors Launcher to use its own folder in your Google Drive. This page closes by itself once you have signed in.",
                 10, FontStyle.Italic, Theme.GoldDim, 26, 0, 8, 460);
            Action stop = BackAction;
            Body.Add(B("CANCEL", delegate { stop(); }, 160, 34, 10), Align.Center, 26, 8, 26, 16);
            Finish();
        }
    }
}
