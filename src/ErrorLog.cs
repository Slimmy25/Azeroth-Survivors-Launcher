using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Text;
using System.Windows.Forms;

namespace AzerothLauncher
{
    // Error log: reads the launcher's own log file and writes every line in plain words. Errors and warnings get
    // what happened and what to try next. The log file itself is never changed.
    public partial class LauncherApp
    {
        Panel errorBubble;

        string ErrorLogFile { get { return Path.Combine(Util.StateDir, "log.txt"); } }
        string OldErrorLogFile { get { return Path.Combine(Util.StateDir, "log.old.txt"); } }

        // Each rule: every part must appear in the logged text (case-insensitive). First match wins.
        static readonly List<KeyValuePair<string[], string>> ErrorExplanations = new List<KeyValuePair<string[], string>>
        {
            new KeyValuePair<string[], string>(new[] { "cloud save was not loaded" },
                "Your saved progress could not be loaded from the cloud, so the game was not started. Your files on this PC were not changed. Check the sync folder in Options, then try again."),
            new KeyValuePair<string[], string>(new[] { "sync folder does not exist" },
                "The cloud sync folder does not exist yet. It is created on the first upload, so nothing is wrong."),
            new KeyValuePair<string[], string>(new[] { "addons can't be changed" },
                "Addons cannot be changed while the game or a sync is running. Close the game and try again."),
            new KeyValuePair<string[], string>(new[] { "not found, skipped" },
                "A game settings folder was not found, so it was skipped. Set the game's config folder in Options."),
            new KeyValuePair<string[], string>(new[] { "sync memory could not be read" },
                "The launcher's sync memory could not be read. A copy was kept, and a fresh one is made on the next sync."),
            new KeyValuePair<string[], string>(new[] { "could not remove" },
                "A file was in use and could not be removed. It will be cleaned up next time."),
            new KeyValuePair<string[], string>(new[] { "no published", "zip" },
                "The author has not published a release with a matching ZIP yet, so the addon was not installed."),
            new KeyValuePair<string[], string>(new[] { "did not publish", "sha-256" },
                "GitHub did not publish a checksum for this download, so it could not be checked against one."),
            new KeyValuePair<string[], string>(new[] { "mismatch" },
                "The downloaded file did not match its published checksum, so it was not installed."),
            new KeyValuePair<string[], string>(new[] { "missing from the launcher's mirror" },
                "This addon's file is missing from the launcher's download server, so it could not be installed yet."),
            new KeyValuePair<string[], string>(new[] { "403" },
                "GitHub is limiting requests right now. Try again in a little while."),
            new KeyValuePair<string[], string>(new[] { "rate limit" },
                "GitHub is limiting requests right now. Try again in a little while."),
            new KeyValuePair<string[], string>(new[] { "hung up" },
                "The launcher could not reach the internet or GitHub. Check your connection, then try again."),
            new KeyValuePair<string[], string>(new[] { "timed out" },
                "The launcher could not reach the internet or GitHub in time. Check your connection, then try again."),
            new KeyValuePair<string[], string>(new[] { "could not be resolved" },
                "The launcher could not find the internet address it needed. Check your connection, then try again."),
            new KeyValuePair<string[], string>(new[] { "work in progress" },
                "This addon is not ready for release yet. Install it with its Work in Progress button in Download More and accept the warning."),
            new KeyValuePair<string[], string>(new[] { "pinned to an older copy" },
                "This addon is pinned to an older copy, so its automatic updates are paused. Use UPDATE to replace it with the newest release."),
            new KeyValuePair<string[], string>(new[] { "conflicts with another addon" },
                "Two addons do the same job, so this one was switched off. Turn off the other one to use it."),
            new KeyValuePair<string[], string>(new[] { "could not keep a local copy" },
                "A backup copy of this download could not be kept. The download itself is fine."),
            new KeyValuePair<string[], string>(new[] { "interrupted sync" },
                "A sync was interrupted earlier. Your files were restored from the copy kept before it."),
            new KeyValuePair<string[], string>(new[] { "cancelled, nothing changed" },
                "You cancelled this step, so nothing changed."),
            new KeyValuePair<string[], string>(new[] { "no longer part of the launcher" },
                "This addon was removed because the launcher no longer offers it."),
        };

        // The plain explanation for one logged message, or null when none applies.
        internal static string ExplainLogLine(string text)
        {
            foreach (var rule in ErrorExplanations)
            {
                bool all = true;
                foreach (string part in rule.Key)
                    if (text.IndexOf(part, StringComparison.OrdinalIgnoreCase) < 0) { all = false; break; }
                if (all) return rule.Value;
            }
            return null;
        }

        // The last part of the log (older file first), each line written in plain words.
        internal string BuildErrorReport()
        {
            var lines = new List<string>();
            foreach (string file in new[] { OldErrorLogFile, ErrorLogFile })
                try { if (File.Exists(file)) lines.AddRange(File.ReadAllLines(file, Encoding.UTF8)); } catch (Exception) { }
            int start = Math.Max(0, lines.Count - 400);
            var sb = new StringBuilder();
            sb.AppendLine("Azeroth Survivors Launcher " + typeof(LauncherApp).Assembly.GetName().Version.ToString(3) + " - error log");
            sb.AppendLine("Made " + DateTime.Now.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture));
            sb.AppendLine(new string('-', 60));
            int shown = 0;
            for (int i = start; i < lines.Count; i++)
            {
                string raw = lines[i].Trim();
                if (raw.Length == 0) continue;
                string stamp = "";
                string text = raw;
                if (raw.Length > 20 && raw[4] == '-' && raw[7] == '-' && raw[19] == ' ')
                {
                    stamp = raw.Substring(0, 19);
                    text = raw.Substring(20).Trim();
                }
                string label = "INFO";
                if (text.StartsWith("[Error] ", StringComparison.Ordinal)) { label = "ERROR"; text = text.Substring(8).Trim(); }
                else if (text.StartsWith("[Warn] ", StringComparison.Ordinal)) { label = "WARNING"; text = text.Substring(7).Trim(); }
                string explanation = ExplainLogLine(text);
                string head = (stamp.Length > 0 ? stamp + "  " : "") + label + ": ";
                if (explanation != null)
                {
                    sb.AppendLine(head + explanation);
                    sb.AppendLine("    Recorded: " + text);
                }
                else if (label == "INFO")
                {
                    sb.AppendLine(head + text);
                }
                else
                {
                    sb.AppendLine(head + text);
                    sb.AppendLine("    No plain explanation yet. Send this log to the launcher's support.");
                }
                shown++;
            }
            if (shown == 0) sb.AppendLine("Nothing has been logged yet.");
            return sb.ToString();
        }

        internal void ShowErrorLogBubble()
        {
            CloseErrorBubble();
            string report = BuildErrorReport();
            int bw = Math.Min(760, W - 40), bh = Math.Min(520, H - 60);
            errorBubble = new Panel();
            errorBubble.Bounds = new Rectangle((W - bw) / 2, (H - bh) / 2, bw, bh);
            errorBubble.BackColor = Theme.Panel;
            var box = new TextBox();
            box.Multiline = true;
            box.ReadOnly = true;
            box.WordWrap = true;
            box.ScrollBars = ScrollBars.Vertical;
            box.BorderStyle = BorderStyle.None;
            box.BackColor = Theme.Ink;
            box.ForeColor = Theme.Text;
            box.Font = Theme.F(9);
            box.Bounds = new Rectangle(12, 12, bw - 24, bh - 66);
            box.Text = report;
            box.SelectionStart = 0;
            var copy = new WowButton("COPY", delegate
            {
                try { Clipboard.SetText(box.Text); SetStatus("Error log copied."); } catch (Exception) { }
            }, 110, 26, 8);
            copy.Location = new Point(12, bh - 42);
            var close = new WowButton("CLOSE", delegate { CloseErrorBubble(); }, 110, 26, 8);
            close.Location = new Point(bw - 122, bh - 42);
            errorBubble.Controls.Add(box);
            errorBubble.Controls.Add(copy);
            errorBubble.Controls.Add(close);
            Controls.Add(errorBubble);
            errorBubble.BringToFront();
        }

        void CloseErrorBubble()
        {
            if (errorBubble == null) return;
            Controls.Remove(errorBubble);
            errorBubble.Dispose();
            errorBubble = null;
        }
    }
}
