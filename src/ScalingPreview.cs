using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Windows.Forms;

namespace AzerothLauncher
{
    public partial class LauncherApp
    {
        // Developer-only renderer. Runs off-screen, never creates setup markers or updates game data.
        public static void RenderScalingPreview(string output)
        {
            PreviewMode = true;
            Directory.CreateDirectory(output);
            using (var app = new LauncherApp())
            {
                app.StartPosition = FormStartPosition.Manual;
                app.ShowInTaskbar = false;
                app.Location = new Point(-30000, -30000);
                app.setupAsked = true;
                app.Show();
                app.ShowSide();
                Action<string> capture = delegate(string name)
                {
                    app.Refresh();
                    app.side.Refresh();
                    using (var bitmap = new Bitmap(app.ClientSize.Width, app.ClientSize.Height))
                    {
                        app.DrawToBitmap(bitmap, new Rectangle(Point.Empty, bitmap.Size));
                        bitmap.Save(Path.Combine(output, name + ".png"), ImageFormat.Png);
                    }
                };
                app.GuideGroupPage(2);
                capture("gear-warrior");
                foreach (string cls in Items.Classes)
                {
                    app.PlannerClass(cls);
                    app.gTalents = null; app.gBuilder = false; app.gPlanner = true;
                    app.BuildPanel("Guide");
                    capture("gear-" + cls.ToLowerInvariant());
                    app.SwitchTheoryTool(false);
                    capture("talents-" + cls.ToLowerInvariant());
                }
                foreach (string section in new[] { "Guide", "Options", "Addons", "Stats", "Changelog", "About Launcher" })
                {
                    app.gBuilder = app.gPlanner = false; app.gTalents = null;
                    app.current = section; app.guideWikiIndex = true;
                    app.BuildPanel(section);
                    capture(section.ToLowerInvariant().Replace(" ", "-"));
                }
                app.quitting = true;
            }
        }
    }
}
