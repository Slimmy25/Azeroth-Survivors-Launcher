using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace AzerothLauncher
{
    public partial class LauncherApp
    {
        sealed class LauncherWhisper { public DateTimeOffset At; public string Author, Message; }
        static List<LauncherWhisper> launcherWhispers;
        static Image whisperAvatar;
        static List<LauncherWhisper> ReadLauncherWhispers()
        {
            if(launcherWhispers!=null) return launcherWhispers;
            using (var stream = typeof(Program).Assembly.GetManifestResourceStream("AS.launcher-announcements.txt"))
            {
                if (stream == null) return launcherWhispers = new List<LauncherWhisper>();
                using (var reader = new StreamReader(stream))
                    return launcherWhispers = ParseLauncherWhispers(reader.ReadToEnd());
            }
        }

        static List<LauncherWhisper> ParseLauncherWhispers(string contents)
        {
            var posts = new List<LauncherWhisper>();
            using (var reader = new StringReader(contents))
            {
                string line;
                while ((line = reader.ReadLine()) != null)
                {
                    if (line.TrimStart().StartsWith("#")) continue;
                    var parts = line.Split(new[] { '|' }, 3);
                    DateTimeOffset at;
                    if (parts.Length != 3 || !DateTimeOffset.TryParse(parts[0].Trim(), CultureInfo.InvariantCulture,
                            DateTimeStyles.None, out at)) continue;
                    if (parts[1].Trim().Length == 0 || parts[2].Trim().Length == 0) continue;
                    posts.Add(new LauncherWhisper { At = at, Author = parts[1].Trim(), Message = parts[2].Trim() });
                }
            }
            // Order newest first, keeping the file order for posts with identical timestamps.
            return System.Linq.Enumerable.ToList(System.Linq.Enumerable.OrderByDescending(posts, post => post.At));
        }

        const string WhisperHistoryUrl = "https://raw.githubusercontent.com/Slimmy25/Azeroth-Survivors-Launcher/launcher-announcements/assets/launcher-announcements.txt";

        void LoadPublishedWhispers()
        {
            Bg(delegate
            {
                try
                {
                    const int limit = 256 * 1024;
                    List<LauncherWhisper> published;
                    using (var response = Net.Open(WhisperHistoryUrl, 8000, true, "launcher announcements"))
                    using (var reader = new StreamReader(response.GetResponseStream()))
                    {
                        var buffer = new char[limit + 1];
                        int count = reader.ReadBlock(buffer, 0, buffer.Length);
                        if (count > limit) return;
                        published = ParseLauncherWhispers(new string(buffer, 0, count));
                    }
                    if (published.Count == 0) return;
                    Ui(delegate
                    {
                        launcherWhispers = published;
                        whisperScroll = 0;
                        Invalidate(WhisperArea);
                    });
                }
                catch (IOException) { }
                catch (System.Net.WebException) { }
                catch (UserError)
                {
                    // The bundled announcement history is available offline.
                }
            });
        }

        const string FeedbackThreadUrl = "https://discord.com/channels/1557821819032051852/1566579228785246369/1566579228785246369";
        const string FeedbackReply = "Thank you for wanting to contribute to this community project!\n\n"
            + "I genuinely want to help this community grow, and build a strong and passionate fanbase. "
            + "Please message me on discord @slimjimdrums or in my Discord Thread";
        TextBox feedbackField;
        Panel feedbackReplyPanel;
        LinkLabel feedbackReplyText;
        Button copyFeedback, closeFeedback;
        bool feedbackReplyOpen;

        [DllImport("user32.dll", CharSet = CharSet.Unicode, EntryPoint = "SendMessageW")]
        static extern IntPtr SetFeedbackCue(IntPtr window, int message, IntPtr showOnFocus, string text);

        Rectangle FeedbackFieldBounds
        {
            get { return new Rectangle(NewsArea.Left, HeaderBottom - U(25), NewsTextW, U(20)); }
        }

        void LayoutWhisperFeedback()
        {
            if (CompactDisplay)
            {
                if (feedbackField != null) feedbackField.Hide();
                if (feedbackReplyPanel != null) feedbackReplyPanel.Hide();
                return;
            }
            if (feedbackField == null)
            {
                feedbackField = new TextBox
                {
                    AccessibleName = "Want to share feedback", MaxLength = 2000, AutoSize = false,
                    BackColor = Theme.Ink, ForeColor = Theme.Text, BorderStyle = BorderStyle.FixedSingle
                };
                feedbackField.HandleCreated += delegate
                {
                    SetFeedbackCue(feedbackField.Handle, 0x1501, new IntPtr(1), "Want to share feedback");
                };
                feedbackField.TextChanged += delegate
                {
                    feedbackReplyOpen = !string.IsNullOrWhiteSpace(feedbackField.Text);
                    LayoutWhisperFeedback();
                };
                Controls.Add(feedbackField);
            }
            feedbackField.Font = Theme.F(9);
            feedbackField.Bounds = FeedbackFieldBounds;
            feedbackField.Show();
            if (!feedbackReplyOpen)
            {
                if (feedbackReplyPanel != null) feedbackReplyPanel.Hide();
                return;
            }
            if (feedbackReplyPanel == null) CreateFeedbackReply();
            feedbackReplyPanel.Bounds = NewsArea;
            feedbackReplyText.Font = Theme.F(9);
            feedbackReplyText.MaximumSize = new Size(Math.Max(1, NewsArea.Width - U(30)), 0);
            feedbackReplyText.Location = new Point(U(8), U(7));
            copyFeedback.Font = closeFeedback.Font = Theme.F(8);
            copyFeedback.Bounds = new Rectangle(U(8), feedbackReplyText.Bottom + U(6), U(108), U(25));
            closeFeedback.Bounds = new Rectangle(copyFeedback.Right + U(6), copyFeedback.Top, U(60), U(25));
            feedbackReplyPanel.Show();
            feedbackReplyPanel.BringToFront();
        }

        void CreateFeedbackReply()
        {
            feedbackReplyPanel = new Panel { BackColor = Theme.Ink, BorderStyle = BorderStyle.FixedSingle, AutoScroll = true };
            feedbackReplyText = new LinkLabel
            {
                Text = FeedbackReply, AutoSize = true, ForeColor = Theme.Text,
                LinkColor = Theme.Gold, ActiveLinkColor = Theme.Gold, VisitedLinkColor = Theme.Gold
            };
            const string link = "Discord Thread";
            feedbackReplyText.Links.Add(FeedbackReply.LastIndexOf(link, StringComparison.Ordinal), link.Length);
            feedbackReplyText.LinkClicked += delegate { Util.OpenInShell(FeedbackThreadUrl); };
            copyFeedback = new Button { Text = "Copy message", BackColor = Theme.Ink, ForeColor = Theme.Gold, FlatStyle = FlatStyle.Flat };
            copyFeedback.Click += delegate
            {
                try { Clipboard.SetText(feedbackField.Text); SetStatus("Feedback copied."); }
                catch (ExternalException) { SetStatus("The clipboard is busy. Try copying again."); }
            };
            closeFeedback = new Button { Text = "Close", BackColor = Theme.Ink, ForeColor = Theme.Gold, FlatStyle = FlatStyle.Flat };
            closeFeedback.Click += delegate { feedbackReplyOpen = false; feedbackReplyPanel.Hide(); };
            feedbackReplyPanel.Controls.AddRange(new Control[] { feedbackReplyText, copyFeedback, closeFeedback });
            Controls.Add(feedbackReplyPanel);
        }

        int whisperScroll, whisperScrollLimit, whisperWheelRemainder;

        Rectangle WhisperArea
        {
            get
            {
                int top = Math.Min(HeaderBottom / 3, U(55)) + U(5);
                // runs to the end of the news column, so its right edge sits snug against the launcher's right side
                return new Rectangle(NewsArea.Left, top, NewsArea.Width, Math.Max(0, FeedbackFieldBounds.Top - U(4) - top));
            }
        }

        bool WheelWhispers(Point point, int delta)
        {
            if (CompactDisplay || ReadLauncherWhispers().Count == 0 || !WhisperArea.Contains(point))
                return false;
            // Small touchpad deltas add up until they make a whole notch, so they scroll too.
            whisperWheelRemainder += delta;
            int notches = whisperWheelRemainder / 120;
            whisperWheelRemainder -= notches * 120;
            whisperScroll = Math.Max(0, Math.Min(whisperScrollLimit, whisperScroll + notches * U(30)));
            Invalidate(WhisperArea);
            return true;
        }

        void DrawLauncherWhispers(Graphics graphics)
        {
            Rectangle area = WhisperArea;
            // oldest at the top, newest at the bottom; the posts grow upward from the bottom edge
            var posts = new List<LauncherWhisper>(ReadLauncherWhispers());
            posts.Sort((first, second) => first.At.CompareTo(second.At));
            if (CompactDisplay || posts.Count == 0 || area.Width < U(60) || area.Height < U(28)) return;

            Font font = Theme.F(6);
            int bodyWidth = Math.Max(20, area.Width - U(20));
            // No header strip any more: the portrait sits outside the box, so the text starts at the top.
            int headerHeight = Math.Min(U(2), area.Height / 2);
            int bodyHeight = area.Height - headerHeight - U(4);
            var lines = new List<string>();
            var heights = new List<float>();
            float contentHeight = 0;
            foreach (var post in posts)
            {
                string line = post.At.ToLocalTime().ToString("M/d", CultureInfo.InvariantCulture)
                    + "  Whisper From [" + post.Author + "]: " + post.Message;
                float height = graphics.MeasureString(line, font, bodyWidth).Height + U(6);
                lines.Add(line);
                heights.Add(height);
                contentHeight += height;
            }
            whisperScrollLimit = Math.Max(0, (int)Math.Ceiling(contentHeight) - bodyHeight);
            whisperScroll = Math.Min(whisperScroll, whisperScrollLimit);

            using (var purple = new SolidBrush(Theme.Hex("#e58bdf")))
            using (var gold = new SolidBrush(Theme.Gold))
            using (var track = new SolidBrush(Color.FromArgb(90, Theme.CardEdge)))
            using (var circleFill = new SolidBrush(Theme.Hex("#0b0a10")))
            // a very slight see-through: the launcher art shows faintly behind the box
            using (var backdrop = new System.Drawing.Drawing2D.LinearGradientBrush(area, Color.FromArgb(225, Theme.Card), Color.FromArgb(225, Theme.Hex("#08070c")), 90f))
            using (var bronze = new Pen(Theme.Bronze))
            using (var innerGold = new Pen(Color.FromArgb(70, Theme.Gold)))
            using (var rule = new Pen(Color.FromArgb(150, Theme.Gold)))
            {
                graphics.FillRectangle(backdrop, area);
                graphics.DrawRectangle(bronze, area.X, area.Y, area.Width - 1, area.Height - 1);
                graphics.DrawRectangle(innerGold, area.X + U(2), area.Y + U(2), area.Width - U(5), area.Height - U(5));

                // Your portrait fills a circle; the image is cropped square and clipped to the circle.
                Image avatar = whisperAvatar ?? (whisperAvatar = Theme.LoadImage("AS.slimjim-avatar.png"));
                // Bigger, and set just outside the box's top-left corner so it overlaps the corner slightly.
                int d = U(40);
                var circle = new Rectangle(area.X - U(16), area.Y - U(14), d, d);
                graphics.FillEllipse(circleFill, circle);
                if (avatar != null)
                {
                    int side = Math.Min(avatar.Width, avatar.Height);
                    var source = new Rectangle((avatar.Width - side) / 2, (avatar.Height - side) / 2, side, side);
                    using (var clip = new System.Drawing.Drawing2D.GraphicsPath())
                    {
                        clip.AddEllipse(circle);
                        var portraitState = graphics.Save();
                        graphics.SetClip(clip);
                        graphics.DrawImage(avatar, circle, source, GraphicsUnit.Pixel);
                        graphics.Restore(portraitState);
                    }
                }
                graphics.DrawEllipse(rule, circle);

                var body = new Rectangle(area.X + U(6), area.Y + headerHeight, bodyWidth, area.Height - headerHeight - U(4));
                var state = graphics.Save();
                graphics.SetClip(body, System.Drawing.Drawing2D.CombineMode.Intersect);
                float y = body.Bottom - contentHeight + whisperScroll;
                for (int i = 0; i < lines.Count && y < body.Bottom; i++)
                {
                    if (y + heights[i] > body.Top)
                        graphics.DrawString(lines[i], font, purple, new RectangleF(body.X, y, body.Width, heights[i]));
                    y += heights[i];
                }
                graphics.Restore(state);

                if (whisperScrollLimit > 0)
                {
                    int thumbHeight = Math.Min(body.Height, Math.Max(U(8), (int)(body.Height * body.Height / contentHeight)));
                    int thumbTop = body.Top + (body.Height - thumbHeight) * (whisperScrollLimit - whisperScroll) / whisperScrollLimit;
                    graphics.FillRectangle(track, area.Right - U(9), body.Top, U(5), body.Height);
                    graphics.FillRectangle(gold, area.Right - U(9), thumbTop, U(5), thumbHeight);
                }
            }
        }
    }
}
