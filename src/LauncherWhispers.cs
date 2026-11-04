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
        float DrawLauncherWhispers(Graphics graphics,float y)
        {
            int width=NewsTextW; var posts=ReadLauncherWhispers();
            if(posts.Count==0 || width<U(60)) return y;
            Font font=Theme.F(9), heading=Theme.F(9,FontStyle.Bold);
            using(var purple=new SolidBrush(Theme.Hex("#e58bdf")))
            using(var gold=new SolidBrush(Theme.Gold))
            using(var bg=new SolidBrush(Theme.Hex("#100e17")))
            using(var border=new Pen(Theme.Bronze))
            {
                float x=NewsArea.Left, bodyY=y+U(34), content=bodyY;
                foreach(var post in posts)
                {
                    string text=post.At.ToLocalTime().ToString("yyyy-MM-dd HH:mm zzz",CultureInfo.InvariantCulture)+"  Whisper From ["+post.Author+"]: "+post.Message;
                    var size=graphics.MeasureString(text,font,Math.Max(20,width-U(12)));
                    content+=size.Height+U(8);
                }
                graphics.FillRectangle(bg,x,y,width,content-y+U(5));
                graphics.DrawRectangle(border,x,y,width,content-y+U(5));
                Image avatar=whisperAvatar ?? (whisperAvatar=Theme.LoadImage("AS.slimjim-avatar.png"));
                if(avatar!=null) graphics.DrawImage(avatar,new Rectangle((int)x+U(5),(int)y+U(4),U(28),U(25)));
                graphics.DrawString("Slimjim",heading,gold,x+U(39),y+U(8));
                foreach(var post in posts)
                {
                    string text=post.At.ToLocalTime().ToString("yyyy-MM-dd HH:mm zzz",CultureInfo.InvariantCulture)+"  Whisper From ["+post.Author+"]: "+post.Message;
                    var size=graphics.MeasureString(text,font,Math.Max(20,width-U(12)));
                    graphics.DrawString(text,font,purple,new RectangleF(x+U(6),bodyY,width-U(12),size.Height+1));
                    bodyY+=size.Height+U(8);
                }
                return content+U(16);
            }
        }
    }
}
