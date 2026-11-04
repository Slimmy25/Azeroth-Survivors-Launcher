using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.IO;

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
            launcherWhispers=new List<LauncherWhisper>();
            using(var stream=typeof(Program).Assembly.GetManifestResourceStream("AS.launcher-announcements.txt"))
            {
                if(stream==null) return launcherWhispers;
                using(var reader=new StreamReader(stream))
                {
                    string line;
                    while((line=reader.ReadLine())!=null)
                    {
                        if(line.TrimStart().StartsWith("#")) continue;
                        var parts=line.Split(new[]{'|'},3); DateTimeOffset at;
                        if(parts.Length!=3 || !DateTimeOffset.TryParse(parts[0].Trim(),CultureInfo.InvariantCulture,DateTimeStyles.None,out at)) continue;
                        launcherWhispers.Add(new LauncherWhisper {At=at, Author=parts[1].Trim(), Message=parts[2].Trim()});
                    }
                }
            }
            launcherWhispers.Sort(delegate(LauncherWhisper a,LauncherWhisper b) {return b.At.CompareTo(a.At);});
            return launcherWhispers;
        }
        int whisperScroll, whisperScrollLimit;

        Rectangle WhisperArea
        {
            get
            {
                int top = Math.Min(HeaderBottom / 3, U(55)) + U(5);
                return new Rectangle(NewsArea.Left, top, NewsTextW, Math.Max(0, HeaderBottom - U(10) - top));
            }
        }

        bool WheelWhispers(Point point, int delta)
        {
            if (CompactDisplay || ReadLauncherWhispers().Count == 0 || !WhisperArea.Contains(point))
                return false;
            whisperScroll = Math.Max(0, Math.Min(whisperScrollLimit, whisperScroll - delta * U(30) / 120));
            Invalidate(WhisperArea);
            return true;
        }

        void DrawLauncherWhispers(Graphics graphics)
        {
            Rectangle area = WhisperArea;
            var posts = ReadLauncherWhispers();
            if (CompactDisplay || posts.Count == 0 || area.Width < U(60) || area.Height < U(40)) return;

            Font font = Theme.F(9), heading = Theme.F(9, FontStyle.Bold);
            int bodyWidth = Math.Max(20, area.Width - U(20));
            int headerHeight = U(34);
            int bodyHeight = area.Height - headerHeight - U(4);
            var lines = new List<string>();
            var heights = new List<float>();
            float contentHeight = 0;
            foreach (var post in posts)
            {
                string line = post.At.ToLocalTime().ToString("yyyy-MM-dd HH:mm zzz", CultureInfo.InvariantCulture)
                    + "  Whisper From [" + post.Author + "]: " + post.Message;
                float height = graphics.MeasureString(line, font, bodyWidth).Height + U(6);
                lines.Add(line);
                heights.Add(height);
                contentHeight += height;
            }
            whisperScrollLimit = Math.Max(0, (int)Math.Ceiling(contentHeight) - bodyHeight);
            whisperScroll = Math.Min(whisperScroll, whisperScrollLimit);
            area.Height = Math.Min(area.Height, headerHeight + (int)Math.Ceiling(contentHeight) + U(4));

            using (var purple = new SolidBrush(Theme.Hex("#e58bdf")))
            using (var gold = new SolidBrush(Theme.Gold))
            using (var background = new SolidBrush(Theme.Hex("#100e17")))
            using (var border = new Pen(Theme.Bronze))
            {
                graphics.FillRectangle(background, area);
                graphics.DrawRectangle(border, area.X, area.Y, area.Width - 1, area.Height - 1);
                Image avatar = whisperAvatar ?? (whisperAvatar = Theme.LoadImage("AS.slimjim-avatar.png"));
                if (avatar != null)
                    graphics.DrawImage(avatar, new Rectangle(area.X + U(5), area.Y + U(4), U(28), U(25)));
                graphics.DrawString("Slimjim", heading, gold, area.X + U(39), area.Y + U(8));

                var body = new Rectangle(area.X + U(6), area.Y + headerHeight, bodyWidth, area.Height - headerHeight - U(4));
                var state = graphics.Save();
                graphics.SetClip(body, System.Drawing.Drawing2D.CombineMode.Intersect);
                float y = body.Top - whisperScroll;
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
                    int thumbTop = body.Top + (body.Height - thumbHeight) * whisperScroll / whisperScrollLimit;
                    graphics.FillRectangle(gold, area.Right - U(7), thumbTop, U(3), thumbHeight);
                }
            }
        }
    }
}
