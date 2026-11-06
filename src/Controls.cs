// The look: 2006 wow.com colours, the gold bevel buttons, small icon buttons and shared UI helpers.
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.IO;
using System.Windows.Forms;

namespace AzerothLauncher
{
    public static class Theme
    {
        public static readonly Color BgTop = Hex("#16151d"), BgBottom = Hex("#07070b"), Gold = Hex("#f2c14e"),    // night stone and gold, like the launcher art
            GoldDim = Hex("#b39b62"), Bronze = Hex("#34323f"), Text = Hex("#d7cea4"), Panel = Hex("#0e0d14"),
            LinkBlue = Hex("#e6cf8c"), Ink = Hex("#15121c"), Good = Hex("#8bc34a"), Bad = Hex("#ff6b5a"),
            Credit = Hex("#b39a5c"), Card = Hex("#1b1724"), CardEdge = Hex("#3d3647");   // LinkBlue: paths and links (pale gold); Ink: card backgrounds

        public static string FontName = "Times New Roman";
        static readonly Dictionary<string, Font> fonts = new Dictionary<string, Font>();
        static Image logo, portrait;
        static Icon icon;

        public static Color Hex(string h)
        {
            return Color.FromArgb(Convert.ToInt32(h.Substring(1, 2), 16), Convert.ToInt32(h.Substring(3, 2), 16),
                                  Convert.ToInt32(h.Substring(5, 2), 16));
        }

        public static Color Mix(Color a, Color b, double t)
        {
            return Color.FromArgb((int)(a.R + (b.R - a.R) * t), (int)(a.G + (b.G - a.G) * t), (int)(a.B + (b.B - a.B) * t));
        }

        // The launcher's text font: Source Serif 4 SmText (Adobe, SIL Open Font License), built into the exe and made
        // for reading small text on screens. It has Latin (with Vietnamese), Greek and Cyrillic; Japanese, Korean,
        // Chinese, Thai and Hindi use the Windows font their language file names (@font).
        // Only the big "Azeroth Survivors" title keeps a decorative font (TitleFontName).
        public static string TitleFontName = "Georgia";
        static FontFamily bundled;
        static PrivateFontCollection privateFonts;
        static readonly List<IntPtr> fontMemory = new List<IntPtr>();   // must stay alive while the fonts are used

        [System.Runtime.InteropServices.DllImport("gdi32.dll")]
        static extern IntPtr AddFontMemResourceEx(IntPtr pbFont, uint cbFont, IntPtr pdv, ref uint pcFonts);

        static void LoadBundledFonts()
        {
            if (privateFonts != null) return;
            privateFonts = new PrivateFontCollection();
            try
            {
                var asm = System.Reflection.Assembly.GetExecutingAssembly();
                foreach (string res in asm.GetManifestResourceNames())
                {
                    if (!res.StartsWith("AS.font.", StringComparison.Ordinal)) continue;
                    byte[] data;
                    using (Stream s = asm.GetManifestResourceStream(res))
                    using (var ms = new MemoryStream()) { s.CopyTo(ms); data = ms.ToArray(); }
                    IntPtr mem = System.Runtime.InteropServices.Marshal.AllocCoTaskMem(data.Length);
                    System.Runtime.InteropServices.Marshal.Copy(data, 0, mem, data.Length);
                    fontMemory.Add(mem);
                    privateFonts.AddMemoryFont(mem, data.Length);        // for text drawn by the launcher (GDI+)
                    uint n = 0;
                    AddFontMemResourceEx(mem, (uint)data.Length, IntPtr.Zero, ref n);   // and for labels and boxes (GDI)
                }
                if (privateFonts.Families.Length > 0) bundled = privateFonts.Families[0];
            }
            catch (Exception) { bundled = null; }
        }

        public static void PickFont()
        {
            LoadBundledFonts();
            var have = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            using (var fc = new InstalledFontCollection())
                foreach (FontFamily f in fc.Families) have.Add(f.Name);
            fonts.Clear();
            TitleFontName = "Georgia";
            foreach (string f in new[] { "Morpheus", "Cinzel", "Friz Quadrata TT", "Friz Quadrata Std", "Palatino Linotype", "Georgia" })
                if (have.Contains(f)) { TitleFontName = f; break; }
            // scripts the bundled font does not have: the language's own Windows font
            string c = Lang.Code ?? "en";
            bool ownScript = c == "ja" || c == "ko" || c.StartsWith("zh") || c == "th" || c == "hi";
            if (ownScript)
                foreach (string f in Lang.FontOverride.Split(','))
                    if (f.Trim().Length > 0 && have.Contains(f.Trim())) { FontName = f.Trim(); return; }
            if (bundled != null) { FontName = bundled.Name; return; }
            FontName = have.Contains("Georgia") ? "Georgia" : "Times New Roman";
        }

        // the title's decorative font, big sizes only
        public static FontFamily TitleFamily()
        {
            try { return new FontFamily(TitleFontName); }
            catch (Exception) { return FontFamily.GenericSerif; }
        }

        public static Font F(float size) { return F(size, FontStyle.Regular); }

        // the sections are drawn a little smaller than the launcher itself: fonts made while Zoom < 1 shrink with it
        public static float Zoom = 1f;
        public static float DisplayScale = 1f;
        public static int DisplayDpi = 96;
        public static int Px(float value) { return Math.Max(1, (int)Math.Round(value * DisplayScale)); }
        public static float SectionZoom { get { return 0.72f * DisplayScale; } }

        public static Font F(float size, FontStyle style) { return Make(size, style, Zoom / DisplayScale < 1f ? 8.25f : 8.75f); }

        // a label that must fit its box (a button, a small card): may go a little smaller than the usual floor
        public static Font FFit(float size, FontStyle style) { return Make(size, style, 6.5f); }

        static Font Make(float size, FontStyle style, float floor)
        {
            // never smaller than the floor: readable everywhere (the compact sections allow a little less)
            size = Math.Max(floor * DisplayScale, (float)Math.Round(size * Zoom * 4) / 4f) * 96f / 72f;
            string key = size + "|" + (int)style;
            Font f;
            if (!fonts.TryGetValue(key, out f))
            {
                try
                {
                    if (bundled != null && FontName == bundled.Name)
                    {
                        FontStyle st = style;
                        if (!bundled.IsStyleAvailable(st)) st = style & (FontStyle.Bold | FontStyle.Italic);
                        if (!bundled.IsStyleAvailable(st)) st = FontStyle.Regular;
                        f = new Font(bundled, size, st | (style & (FontStyle.Underline | FontStyle.Strikeout)), GraphicsUnit.Pixel);
                    }
                    else f = new Font(FontName, size, style, GraphicsUnit.Pixel);
                }
                catch (Exception) { f = new Font(FontFamily.GenericSerif, size, style, GraphicsUnit.Pixel); }
                fonts[key] = f;
            }
            return f;
        }

        public static Image Logo { get { if (logo == null) logo = LoadImage("AS.logo.png"); return logo; } }
        public static Image Portrait { get { if (portrait == null) portrait = LoadImage("AS.portrait.png"); return portrait; } }

        public static Icon AppIcon
        {
            get
            {
                if (icon == null)
                {
                    try
                    {
                        using (Stream s = Util.ResourceStream("AS.app.ico")) icon = new Icon(s);
                    }
                    catch (Exception) { icon = SystemIcons.Application; }
                }
                return icon;
            }
        }

        public static Image LoadImage(string name)
        {
            try
            {
                using (Stream s = Util.ResourceStream(name))
                using (var tmp = Image.FromStream(s))
                    return Fast(tmp);
            }
            catch (Exception) { return null; }
        }

        // ---- speed: pictures are kept premultiplied (GDI+ draws those several times faster) and, once drawn at a
        // size, kept at that size, so a repaint copies pixels instead of resampling the picture every time
        public static Bitmap Fast(Image src)
        {
            var b = new Bitmap(src.Width, src.Height, System.Drawing.Imaging.PixelFormat.Format32bppPArgb);
            b.SetResolution(96, 96);
            using (var g = Graphics.FromImage(b))
            {
                g.CompositingMode = CompositingMode.SourceCopy;
                g.DrawImage(src, new Rectangle(0, 0, src.Width, src.Height), 0, 0, src.Width, src.Height, GraphicsUnit.Pixel);
            }
            return b;
        }

        static readonly Dictionary<Image, Dictionary<long, Bitmap>> scaled = new Dictionary<Image, Dictionary<long, Bitmap>>();
        static int scaledCount;

        // the picture at w x h, made once (high quality) and kept
        public static Image Scaled(Image src, int w, int h)
        {
            if (src == null || w <= 0 || h <= 0) return src;
            lock (scaled)
            {
                Dictionary<long, Bitmap> sizes;
                if (!scaled.TryGetValue(src, out sizes)) scaled[src] = sizes = new Dictionary<long, Bitmap>();
                long key = ((long)w << 32) | (uint)h;
                Bitmap b;
                if (sizes.TryGetValue(key, out b)) return b;
                if (scaledCount > 1500)                      // a long session with many pictures: start over
                {
                    foreach (var d in scaled.Values) foreach (var bm in d.Values) bm.Dispose();
                    scaled.Clear();
                    scaledCount = 0;
                    scaled[src] = sizes = new Dictionary<long, Bitmap>();
                }
                try
                {
                    b = new Bitmap(w, h, System.Drawing.Imaging.PixelFormat.Format32bppPArgb);
                    b.SetResolution(96, 96);
                    using (var g = Graphics.FromImage(b))
                    {
                        g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                        g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                        g.CompositingQuality = CompositingQuality.HighQuality;
                        using (var ia = new System.Drawing.Imaging.ImageAttributes())
                        {
                            ia.SetWrapMode(WrapMode.TileFlipXY);   // no faded edge
                            g.DrawImage(src, new Rectangle(0, 0, w, h), 0, 0, src.Width, src.Height, GraphicsUnit.Pixel, ia);
                        }
                    }
                }
                catch (Exception) { return src; }           // a picture being replaced: drawn as it is
                sizes[key] = b;
                scaledCount++;
                return b;
            }
        }

        // the icons come 56 px wide: drawn at 56, 42 or 28 px (a clean share of that) they stay sharp, where any other
        // size in between looks soft. The largest of those that fits.
        // where a real item's picture will go but there is none yet: "Image soon" in small letters, in the rarity's colour
        public static void ImageSoon(Graphics g, Rectangle r, Color c)
        {
            using (var b = new SolidBrush(Color.FromArgb(50, c))) g.FillRectangle(b, r);
            Font f = F(r.Height >= 44 ? 8f : 6.5f, FontStyle.Bold);
            TextRenderer.DrawText(g, Lang.T("Image soon"), f, Rectangle.Inflate(r, -1, 0), Mix(c, Color.White, 0.55),
                                  TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.WordBreak | TextFormatFlags.NoPadding);
        }

        public static int CrispIcon(int room)
        {
            if (room >= 56) return 56;
            if (room >= 42) return 42;
            if (room >= 28) return 28;
            return room;
        }

        // draws the picture into r from its kept copy at that size: a plain copy of pixels
        public static void Draw(Graphics g, Image img, Rectangle r)
        {
            if (img == null || r.Width <= 0 || r.Height <= 0) return;
            Image s = Scaled(img, r.Width, r.Height);
            InterpolationMode im = g.InterpolationMode;
            PixelOffsetMode pm = g.PixelOffsetMode;
            g.InterpolationMode = InterpolationMode.NearestNeighbor;
            g.PixelOffsetMode = PixelOffsetMode.Half;
            try { g.DrawImage(s, r.X, r.Y, r.Width, r.Height); }
            catch (Exception) { }
            g.InterpolationMode = im;
            g.PixelOffsetMode = pm;
        }

        public static void Draw(Graphics g, Image img, float x, float y, float w, float h)
        {
            Draw(g, img, new Rectangle((int)Math.Round(x), (int)Math.Round(y), (int)Math.Round(w), (int)Math.Round(h)));
        }

        public static void Smooth(Graphics g)
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;   // the sharpest text Windows can draw
            g.InterpolationMode = InterpolationMode.HighQualityBicubic;
        }

        // text centred in a box, with an optional 1 px shadow like the Tk version
        public static void CenterText(Graphics g, string text, Font f, Color fg, RectangleF box, Color? shadow)
        {
            using (var sf = new StringFormat())
            {
                sf.Alignment = StringAlignment.Center;
                sf.LineAlignment = StringAlignment.Center;
                if (shadow.HasValue)
                    using (var b = new SolidBrush(shadow.Value))
                        g.DrawString(text, f, b, new RectangleF(box.X + 1, box.Y + 1, box.Width, box.Height), sf);
                using (var b = new SolidBrush(fg)) g.DrawString(text, f, b, box, sf);
            }
        }
    }

    public delegate void IconPainter(Graphics g, float x, float y, float size, Color color);

    // Shared colors, text and hover states for every launcher context menu.
    public static class ThemeMenus
    {
        sealed class Colors : ProfessionalColorTable
        {
            readonly Color dark = Theme.Hex("#120f18"), hover = Theme.Hex("#302516"), edge = Theme.Hex("#8a7040");
            public override Color ToolStripDropDownBackground { get { return dark; } }
            public override Color MenuBorder { get { return edge; } }
            public override Color MenuItemSelected { get { return hover; } }
            public override Color MenuItemBorder { get { return Theme.GoldDim; } }
            public override Color MenuItemPressedGradientBegin { get { return hover; } }
            public override Color MenuItemPressedGradientEnd { get { return Theme.Hex("#17120b"); } }
            public override Color MenuItemSelectedGradientBegin { get { return hover; } }
            public override Color MenuItemSelectedGradientEnd { get { return hover; } }
            public override Color ImageMarginGradientBegin { get { return dark; } }
            public override Color ImageMarginGradientMiddle { get { return dark; } }
            public override Color ImageMarginGradientEnd { get { return dark; } }
            public override Color SeparatorDark { get { return Theme.Hex("#51442e"); } }
            public override Color SeparatorLight { get { return dark; } }
        }

        public static ContextMenuStrip Create(bool showCheckMargin = true)
        {
            var menu = new ContextMenuStrip();
            menu.Renderer = new ToolStripProfessionalRenderer(new Colors());
            menu.BackColor = Theme.Hex("#120f18");
            menu.ForeColor = Theme.Text;
            menu.Font = Theme.F(9.5f);
            menu.ShowImageMargin = false;
            menu.ShowCheckMargin = showCheckMargin;
            // A dropdown belongs to the launcher even when its SourceControl is in a nested section Form.
            Form host=LauncherApp.App;
            EventHandler dismiss=delegate { if(!menu.IsDisposed) menu.Close(); };
            Action detach=delegate
            {
                if(host==null)return;
                host.Deactivate-=dismiss; host.Resize-=dismiss; host.Move-=dismiss; host.VisibleChanged-=dismiss;
            };
            menu.Opening+=delegate
            {
                detach();
                if(host!=null) { host.Deactivate+=dismiss; host.Resize+=dismiss; host.Move+=dismiss; host.VisibleChanged+=dismiss; }
            };
            menu.Opened+=delegate
            {
                if(host==null||host.IsDisposed)return;
                Rectangle area=host.RectangleToScreen(host.ClientRectangle);
                menu.MaximumSize=new Size(Math.Max(40,area.Width-12),Math.Max(40,area.Height-12));
                menu.Location=new Point(Math.Max(area.Left+6,Math.Min(menu.Left,area.Right-menu.Width-6)),
                    Math.Max(area.Top+6,Math.Min(menu.Top,area.Bottom-menu.Height-6)));
            };
            menu.Closed+=delegate { detach(); };
            menu.Disposed+=delegate { detach(); };
            // Steam Game Mode draws one window at a time: the list opens inside the launcher instead (after every
            // Opening handler has filled it)
            menu.Opening+=delegate(object sender,System.ComponentModel.CancelEventArgs e)
            {
                if(!Handheld.GameMode||host==null||host.IsDisposed)return;
                e.Cancel=true;
                detach();
                host.BeginInvoke(new Action(delegate { MenuChooser.OpenFor(menu); }));
            };
            return menu;
        }
    }

    // ---------- Custom gold-bevel button ----------
    public class WowButton : Control
    {
        string text;
        float size;
        bool hover, down, keyDown, active, selected, grayed;
        public IconPainter Icon;
        public Image[] Skin;
        float zoom;                                  // launcher art {normal, hover, down, grayed}
        public event Action Pressed;
        public string GrayedTip;                              // shown on hover, only while the button is grayed out
        public string Tip;                                    // themed hover help while the button is available

        public WowButton(string text, Action command, int width, int height, float size)
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer |
                     ControlStyles.ResizeRedraw, true);
            SetStyle(ControlStyles.Selectable, true);
            this.text = text;
            AccessibleRole = AccessibleRole.PushButton;
            AccessibleName = Lang.T(text);
            TabStop = true;
            this.size = size;
            zoom = Theme.Zoom;                                 // made inside a section: smaller
            Size = new Size((int)(width * zoom), (int)(height * zoom));
            Cursor = Cursors.Hand;
            if (command != null) Pressed += command;
        }

        public void SetDisplayZoom(float value) { zoom = value; Invalidate(); }

        public string Label { get { return text; } set { text = value; AccessibleName = Lang.T(value); Invalidate(); } }
        public float FontSize { get { return size; } set { size = value; Invalidate(); } }
        public bool Active { get { return active; } set { active = value; Invalidate(); } }
        public bool Selected { get { return selected; } set { selected = value; Invalidate(); } }   // filled gold: the current choice

        // Grayed: drawn dimmed, does nothing when clicked, and explains itself with GrayedTip on hover.
        // (Not Enabled = false: a disabled control gets no mouse events, so it could not show its tip.)
        public bool Grayed
        {
            get { return grayed; }
            set
            {
                if (grayed == value) return;
                grayed = value;
                down = false;
                AccessibleDescription = grayed ? Lang.T(GrayedTip) : "";
                Cursor = grayed ? Cursors.Default : Cursors.Hand;
                if (!grayed) Tips.Hide(this);
                Invalidate();
            }
        }

        internal static readonly ThemedToolTip Tips = new ThemedToolTip();

        protected override void OnMouseEnter(EventArgs e)
        {
            hover = true;
            Invalidate();
            Tips.SetToolTip(this, grayed && !string.IsNullOrEmpty(GrayedTip) ? Lang.T(GrayedTip) :
                                   !string.IsNullOrEmpty(Tip) ? Lang.T(Tip) : null);
            base.OnMouseEnter(e);
        }

        protected override void OnMouseLeave(EventArgs e) { hover = false; down = false; if (!Focused) Tips.Hide(this); Invalidate(); base.OnMouseLeave(e); }

        protected override void OnGotFocus(EventArgs e)
        {
            if (grayed && !string.IsNullOrEmpty(GrayedTip)) GuideHover.ShowTip(this, Lang.T(GrayedTip));
            else if (!string.IsNullOrEmpty(Tip)) GuideHover.ShowTip(this, Lang.T(Tip));
            Invalidate();
            base.OnGotFocus(e);
        }
        protected override void OnLostFocus(EventArgs e) { keyDown = false; GuideHover.Hide(this); Invalidate(); base.OnLostFocus(e); }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            if (!grayed && (e.KeyCode == Keys.Space || e.KeyCode == Keys.Enter))
            {
                keyDown = true;
                Invalidate();
                e.Handled = e.SuppressKeyPress = true;
                return;
            }
            base.OnKeyDown(e);
        }

        protected override void OnKeyUp(KeyEventArgs e)
        {
            bool fire = keyDown && (e.KeyCode == Keys.Space || e.KeyCode == Keys.Enter) && !grayed;
            keyDown = false;
            Invalidate();
            base.OnKeyUp(e);
            if (fire) Fire(Pressed);
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Left && !grayed) { down = true; Invalidate(); }
            base.OnMouseDown(e);
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            bool was = down;
            down = false;
            Invalidate();
            base.OnMouseUp(e);
            if (was && !grayed && e.Button == MouseButtons.Left && ClientRectangle.Contains(e.Location)) Fire(Pressed);
        }

        // runs the button's action after the click has been fully handled, so the action may rebuild the window
        public static void Fire(Action a)
        {
            if (a == null) return;
            var ctx = System.Threading.SynchronizationContext.Current;
            if (ctx != null) ctx.Post(delegate { a(); }, null);
            else a();
        }

        // the biggest font size (from size down) at which the label fits the button: on one line, or on two when a
        // translation is long, but never cut off and never with a word broken in the middle
        static readonly Dictionary<string, float> fitted = new Dictionary<string, float>();

        // the label's font size that fits, worked out once per label, size and room (a repaint only looks it up)
        public static float FitLabel(Graphics g, string label, float size, float width, float height)
        {
            string key = label + "\u0001" + size + "|" + width + "|" + height + "|" + Theme.Zoom + "|" + Theme.FontName;
            float r;
            lock (fitted) if (fitted.TryGetValue(key, out r)) return r;
            r = FitLabelNow(g, label, size, width, height);
            lock (fitted) { if (fitted.Count > 4000) fitted.Clear(); fitted[key] = r; }
            return r;
        }

        static float FitLabelNow(Graphics g, string label, float size, float width, float height)
        {
            float fs = size;
            for (; fs > 5.5f; fs -= 0.5f)
            {
                Font f = Theme.FFit(fs, FontStyle.Bold);
                if (g.MeasureString(label, f).Width <= width) return fs;            // one line
                if (fs > size * 0.8f) continue;                                    // try smaller on one line first
                bool wordFits = true;
                foreach (string word in label.Split(' '))
                    if (g.MeasureString(word, f).Width > width) { wordFits = false; break; }
                if (wordFits && g.MeasureString(label, f, (int)width).Height <= height) return fs;   // two lines
            }
            return fs;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            float old = Theme.Zoom;
            Theme.Zoom = zoom;
            try { PaintButton(e.Graphics); }
            finally { Theme.Zoom = old; }
            if (Focused)
                using (var p = new Pen(Theme.Hex("#fff2b0"), 1) { DashStyle = DashStyle.Dot })
                    e.Graphics.DrawRectangle(p, 5, 5, Math.Max(1, Width - 11), Math.Max(1, Height - 11));
        }

        void PaintButton(Graphics g)
        {
            int w = Width, h = Height;
            if (Skin != null) { PaintSkin(g, w, h); return; }
            if (Retro.Get("btn") != null)                     // every other button: the launcher's blue stone too
            {
                Skin = w > 120 ? Retro.Long : Retro.Button;
                PaintSkin(g, w, h);
                Skin = null;
                return;
            }
            string state = grayed ? "grayed" : (down || keyDown) ? "down" : (hover || Focused) ? "hover" : active ? "active" : "normal";
            Color top, bot, border, fg, shadow = Color.Black;
            if (state == "grayed") { top = Theme.Hex("#2a2620"); bot = Theme.Hex("#12100d"); border = Theme.Hex("#5a5246"); fg = Theme.Hex("#7d7466"); }
            else if (state == "hover") { top = Theme.Hex("#5a4118"); bot = Theme.Hex("#241808"); border = Theme.Gold; fg = Theme.Hex("#fff2b0"); }
            else if (state == "down") { top = Theme.Hex("#120b04"); bot = Theme.Hex("#2e200c"); border = Theme.Gold; fg = Theme.Gold; }
            else if (state == "active") { top = Theme.Hex("#4a3414"); bot = Theme.Hex("#1c1208"); border = Theme.Gold; fg = Theme.Hex("#fff2b0"); }
            else { top = Theme.Hex("#3a2a12"); bot = Theme.Hex("#150e06"); border = Theme.GoldDim; fg = Theme.Gold; }
            if (selected && !grayed)
            {
                top = Theme.Hex("#ffd966"); bot = Theme.Hex("#d48f16"); border = Theme.Hex("#fff6c8");
                fg = Theme.Hex("#2a1706"); shadow = Theme.Hex("#ffe9a0");
            }
            using (var lg = new LinearGradientBrush(new Rectangle(0, 0, w, h), top, bot, LinearGradientMode.Vertical))
                g.FillRectangle(lg, 0, 0, w, h);
            Theme.Smooth(g);
            using (var p = new Pen(border, 2)) g.DrawRectangle(p, 1, 1, w - 3, h - 3);
            g.SmoothingMode = SmoothingMode.None;
            using (var p = new Pen(grayed ? Theme.Hex("#3a352d") : Theme.Bronze, 1)) g.DrawRectangle(p, 5, 5, w - 11, h - 11);
            Theme.Smooth(g);
            using (var b = new SolidBrush(border))
                foreach (var pt in new[] { new PointF(3, 3), new PointF(w - 4, 3), new PointF(3, h - 4), new PointF(w - 4, h - 4) })
                    g.FillEllipse(b, pt.X - 2, pt.Y - 2, 4, 4);
            float tx = 0, tw = w;
            if (Icon != null)                                  // icon at the beginning, text shifted right
            {
                float isz = h - 12;
                Icon(g, 10 + isz / 2, h / 2f, isz, fg);
                tx = 10 + isz + 4;
                tw = w - 8 - tx;
            }
            string label = Lang.T(text);
            float fs = size;                                  // a longer translation gets a smaller font so it fits
            fs = FitLabel(g, label, fs, tw - 10, h - 4);
            Theme.CenterText(g, label, Theme.FFit(fs, FontStyle.Bold), fg, new RectangleF(tx, 0, tw, h), shadow);
        }

        // the button is a picture from the launcher art, the label drawn on it
        void PaintSkin(Graphics g, int w, int h)
        {
            int i = grayed ? 3 : (down || keyDown) ? 2 : (hover || active || selected || Focused) ? 1 : 0;
            Image img = Skin.Length > i && Skin[i] != null ? Skin[i] : Skin[0];
            Theme.Draw(g, img, new Rectangle(0, 0, w, h));
            Theme.Smooth(g);
            Color fg = grayed ? Theme.Hex("#9a948a") : (hover || selected) ? Theme.Hex("#fff2b0") : Theme.Hex("#f2c14e");
            float tx = 0, tw = w;
            if (Icon != null)
            {
                float isz = h - 12;
                Icon(g, 10 + isz / 2, h / 2f, isz, fg);
                tx = 10 + isz + 4;
                tw = w - 8 - tx;
            }
            string label = Lang.T(text);
            float fs = size;
            fs = FitLabel(g, label, fs, tw - 10, h - 4);
            Theme.CenterText(g, label, Theme.FFit(fs, FontStyle.Bold), fg, new RectangleF(tx, down ? 1 : 0, tw, h), Color.Black);
            if (selected && !grayed)                           // the current choice: a gold frame around the stone
            {
                using (var p = new Pen(Color.FromArgb(90, Theme.Gold), 4)) g.DrawRectangle(p, 2, 2, w - 5, h - 5);
                using (var p = new Pen(Theme.Gold, 1.6f)) g.DrawRectangle(p, 1, 1, w - 3, h - 3);
            }
        }
    }

    // a check box in the launcher's colors: a night-stone box with a gold tick
    public class NightCheck : CheckBox
    {
        bool hover;

        public NightCheck()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer, true);
        }

        protected override void OnMouseEnter(EventArgs e) { hover = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { hover = false; WowButton.Tips.Hide(this); Invalidate(); base.OnMouseLeave(e); }

        public override Size GetPreferredSize(Size proposed)
        {
            int box = Font.Height;
            Size t = TextRenderer.MeasureText(Text, Font);
            return new Size(box + 8 + t.Width, Math.Max(box + 2, t.Height));
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.Clear(BackColor);
            Theme.Smooth(g);
            int box = Math.Min(Font.Height - 2, Height - 2);
            int top = Math.Max(1, Math.Min((Height - box) / 2, (Font.Height - box) / 2 + 2));
            var r = new RectangleF(1, top, box, box);
            using (var path = new GraphicsPath())
            {
                float d = 6;
                path.AddArc(r.X, r.Y, d, d, 180, 90); path.AddArc(r.Right - d, r.Y, d, d, 270, 90);
                path.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90); path.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
                path.CloseFigure();
                using (var b = new SolidBrush(Checked ? Theme.Hex("#2a2236") : Theme.Card)) g.FillPath(b, path);
                using (var p = new Pen(hover || Checked ? Theme.Gold : Theme.GoldDim, 1.3f)) g.DrawPath(p, path);
            }
            if (Checked)
                using (var p = new Pen(Theme.Gold, 2f))
                {
                    p.StartCap = p.EndCap = LineCap.Round;
                    g.DrawLines(p, new[] { new PointF(r.X + box * 0.22f, r.Y + box * 0.52f), new PointF(r.X + box * 0.42f, r.Y + box * 0.72f),
                                           new PointF(r.X + box * 0.78f, r.Y + box * 0.28f) });
                }
            var tr = new Rectangle(box + 8, 0, Width - box - 8, Height);
            TextRenderer.DrawText(g, Text, Font, tr, hover ? Theme.Hex("#fff2b0") : ForeColor,
                                  TextFormatFlags.Left | TextFormatFlags.WordBreak | TextFormatFlags.NoPrefix);
        }
    }

    // the slim gold scroll bar for a read-only text box (UI.Log), in place of Windows' own wide white one
    public class LogBar : Control
    {
        [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
        struct POINT { public int X, Y; }
        [System.Runtime.InteropServices.DllImport("user32.dll")]
        static extern IntPtr SendMessage(IntPtr h, int msg, IntPtr w, ref POINT p);
        const int EM_GETSCROLLPOS = 0x04DD, EM_SETSCROLLPOS = 0x04DE, Wide = 8;

        readonly RichTextBox box;
        bool hover, dragging;
        int dragOffset;

        public static void Attach(RichTextBox r)
        {
            var bar = new LogBar(r);
            r.ParentChanged += delegate
            {
                if (r.Parent == null) return;
                if (r.Dock == DockStyle.Fill)
                {   // the box fills its parent: keep a gutter on the right for the bar
                    r.Parent.Padding = new Padding(r.Parent.Padding.Left, r.Parent.Padding.Top, Wide + 2, r.Parent.Padding.Bottom);
                }
                else if (!bar.shrunk) { bar.shrunk = true; r.Width -= Wide + 2; }
                r.Parent.Controls.Add(bar);
                bar.BackColor = r.BackColor;
                bar.Place();
                bar.BringToFront();
            };
            r.Resize += delegate { bar.Place(); bar.Invalidate(); };
            r.LocationChanged += delegate { bar.Place(); };
            r.VScroll += delegate { bar.Invalidate(); };
            r.TextChanged += delegate { bar.Invalidate(); };
            r.MouseWheel += delegate { bar.Invalidate(); };
            r.VisibleChanged += delegate { bar.Visible = r.Visible; };
        }

        bool shrunk;

        LogBar(RichTextBox r)
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer |
                     ControlStyles.ResizeRedraw, true);
            SetStyle(ControlStyles.Selectable, false);
            box = r;
            Width = Wide;
        }

        void Place()
        {
            if (Parent == null) return;
            if (box.Dock == DockStyle.Fill) Bounds = new Rectangle(Parent.ClientSize.Width - Wide - 1, 2, Wide, Parent.ClientSize.Height - 4);
            else Bounds = new Rectangle(box.Right + 2, box.Top + 2, Wide, Math.Max(10, box.Height - 4));
            Anchor = box.Dock == DockStyle.Fill ? AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Right : box.Anchor;
        }

        int Pos { get { var p = new POINT(); try { SendMessage(box.Handle, EM_GETSCROLLPOS, IntPtr.Zero, ref p); } catch (Exception) { } return p.Y; } }
        int Content
        {
            get
            {
                if (box.TextLength == 0) return 1;
                try { return Math.Max(1, box.GetPositionFromCharIndex(box.TextLength - 1).Y + Pos + box.Font.Height + 6); }
                catch (Exception) { return 1; }
            }
        }

        Rectangle Thumb()
        {
            int content = Content, view = Math.Max(1, box.ClientSize.Height);
            if (content <= view) return Rectangle.Empty;
            int h = Math.Max(26, Height * view / content);
            int y = (int)((long)(Height - h) * Math.Min(Pos, content - view) / Math.Max(1, content - view));
            return new Rectangle(1, y, Width - 2, h);
        }

        void ScrollTo(int thumbTop)
        {
            Rectangle t = Thumb();
            if (t.IsEmpty) return;
            int content = Content, view = box.ClientSize.Height;
            var p = new POINT();
            p.Y = (int)((long)Math.Max(0, Math.Min(Height - t.Height, thumbTop)) * (content - view) / Math.Max(1, Height - t.Height));
            try { SendMessage(box.Handle, EM_SETSCROLLPOS, IntPtr.Zero, ref p); } catch (Exception) { }
            Invalidate();
        }

        protected override void OnMouseEnter(EventArgs e) { hover = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { hover = false; WowButton.Tips.Hide(this); Invalidate(); base.OnMouseLeave(e); }
        protected override void OnMouseDown(MouseEventArgs e)
        {
            Rectangle t = Thumb();
            if (t.IsEmpty) return;
            dragging = true;
            dragOffset = t.Contains(e.Location) ? e.Y - t.Y : t.Height / 2;
            ScrollTo(e.Y - dragOffset);
            base.OnMouseDown(e);
        }
        protected override void OnMouseMove(MouseEventArgs e) { if (dragging) ScrollTo(e.Y - dragOffset); base.OnMouseMove(e); }
        protected override void OnMouseUp(MouseEventArgs e) { dragging = false; Invalidate(); base.OnMouseUp(e); }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.Clear(BackColor);
            Theme.Smooth(g);
            Rectangle t = Thumb();
            if (t.IsEmpty) return;
            using (var b = new SolidBrush(Theme.Mix(BackColor, Theme.CardEdge, 0.6))) g.FillRectangle(b, 2, 0, Width - 4, Height);
            using (var b = new SolidBrush(hover || dragging ? Theme.Gold : Theme.GoldDim))
                g.FillRectangle(b, t.X, t.Y, t.Width, t.Height);
        }
    }

    // the launcher art (assets/retro, drawn by tools/make_launcher_art.py); layout after the WoW Retro Launcher (Parquelle)
    public static class Retro
    {
        static readonly Dictionary<string, Image> cache = new Dictionary<string, Image>();
        public static Image Get(string name)
        {
            Image img;
            if (!cache.TryGetValue(name, out img)) cache[name] = img = Theme.LoadImage("AS.retro." + name + ".png");
            return img;
        }
        public static Image[] Play { get { return new[] { Get("play"), Get("play_hover"), Get("play_down"), Get("play_off") }; } }
        public static Image[] Button { get { return new[] { Get("btn"), Get("btn_hover"), Get("btn_down"), Get("btn") }; } }
        public static Image[] Long { get { return new[] { Get("long"), Get("long_hover"), Get("long_down"), Get("long") }; } }
        public static readonly Color Gold = Color.FromArgb(238, 190, 83);
    }

    // Small square bronze button with a drawn icon (the section window's side strip).
    public class IconButton : Control
    {
        readonly Action<Graphics, float, float, Color> icon;
        bool hover, active, grayed, keyDown, dropHighlighted;
        public event Action Pressed;
        public event Action<Control, Point> ContextClicked;
        public string GrayedTip;
        public bool SuppressNextClick;
        string tip;
        public string Tip
        {
            get { return tip; }
            set
            {
                tip = value;
                AccessibleDescription = Lang.T(value);
                WowButton.Tips.SetToolTip(this, string.IsNullOrEmpty(value) ? null : Lang.T(value));
            }
        }

        public IconButton(Action<Graphics, float, float, Color> icon, Action command)
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer, true);
            SetStyle(ControlStyles.Selectable, true);
            this.icon = icon;
            AccessibleRole = AccessibleRole.PushButton;
            TabStop = true;
            Size = new Size(26, 26);                         // the slim strip beside the sections
            Cursor = Cursors.Hand;
            if (command != null) Pressed += command;
        }

        public bool Active { get { return active; } set { active = value; Invalidate(); } }

        // A soft gold landing marker used while rearranging Quick Access favorites.
        public bool DropHighlighted
        {
            get { return dropHighlighted; }
            set { if (dropHighlighted != value) { dropHighlighted = value; Invalidate(); } }
        }

        public bool Grayed
        {
            get { return grayed; }
            set
            {
                if (grayed == value) return;
                grayed = value;
                if (grayed && !string.IsNullOrEmpty(GrayedTip)) AccessibleDescription = Lang.T(GrayedTip);
                Cursor = grayed ? Cursors.Default : Cursors.Hand;
                if (!grayed) WowButton.Tips.Hide(this);
                Invalidate();
            }
        }

        protected override void OnMouseEnter(EventArgs e)
        {
            hover = true;
            Invalidate();
            WowButton.Tips.SetToolTip(this, !string.IsNullOrEmpty(Tip) ? Lang.T(Tip) : grayed && !string.IsNullOrEmpty(GrayedTip) ? Lang.T(GrayedTip) : null);
            base.OnMouseEnter(e);
        }

        protected override void OnMouseLeave(EventArgs e) { hover = false; if (!Focused) WowButton.Tips.Hide(this); Invalidate(); base.OnMouseLeave(e); }

        protected override void OnGotFocus(EventArgs e)
        {
            if (!string.IsNullOrEmpty(Tip)) GuideHover.ShowTip(this, Lang.T(Tip));
            Invalidate();
            base.OnGotFocus(e);
        }
        protected override void OnLostFocus(EventArgs e) { keyDown = false; GuideHover.Hide(this); Invalidate(); base.OnLostFocus(e); }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Apps || (e.KeyCode == Keys.F10 && e.Shift))
            {
                if (ContextClicked != null)
                {
                    Point at = PointToScreen(new Point(Width / 2, Height));
                    WowButton.Fire(delegate { ContextClicked(this, at); });
                }
                e.Handled = e.SuppressKeyPress = true;
                return;
            }
            if (!grayed && (e.KeyCode == Keys.Space || e.KeyCode == Keys.Enter))
            {
                keyDown = true;
                Invalidate();
                e.Handled = e.SuppressKeyPress = true;
                return;
            }
            base.OnKeyDown(e);
        }

        protected override void OnKeyUp(KeyEventArgs e)
        {
            bool fire = keyDown && (e.KeyCode == Keys.Space || e.KeyCode == Keys.Enter) && !grayed;
            keyDown = false;
            Invalidate();
            base.OnKeyUp(e);
            if (fire) WowButton.Fire(Pressed);
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            if (grayed) return;
            if (e.Button == MouseButtons.Left && SuppressNextClick) { SuppressNextClick = false; return; }
            if (!ClientRectangle.Contains(e.Location)) return;
            if (e.Button == MouseButtons.Right && ContextClicked != null)
            {
                Point at = PointToScreen(e.Location);
                WowButton.Fire(delegate { ContextClicked(this, at); });
            }
            else if (e.Button == MouseButtons.Left) WowButton.Fire(Pressed);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            int s = Width;
            g.Clear(Theme.Hex("#0e0d14"));
            Theme.Smooth(g);
            // modern: a soft rounded tile, a gold bar on the left for the open section, a gentle lift on hover
            var tile = new RectangleF(2, 2, s - 4, s - 4);
            if (dropHighlighted)
            {
                using (var glow = new SolidBrush(Color.FromArgb(34, 240, 192, 80)))
                    Armory.Box(g, new RectangleF(0, 0, s, s), 9, glow.Color, glow.Color);
                using (var p = new Pen(Color.FromArgb(70, 240, 192, 80), 2))
                using (var path = Armory.Round(new RectangleF(1, 1, s - 2, s - 2), 8))
                    g.DrawPath(p, path);
            }
            Color fill = dropHighlighted ? Theme.Hex("#393025") : grayed ? Theme.Hex("#121118") : active ? Theme.Hex("#2a2636") : hover ? Theme.Hex("#221f2c") : Theme.Hex("#17161f");
            Armory.Box(g, tile, 7, fill, dropHighlighted ? Theme.Hex("#f0c050") : active ? Theme.Hex("#6b5a32") : hover ? Theme.Hex("#3d3a4a") : Theme.Hex("#26242f"));
            if (active)
                using (var b = new SolidBrush(Theme.Gold)) g.FillRectangle(b, 0, 9, 3, s - 18);
            icon(g, s / 2f, s / 2f, active ? Theme.Gold : hover || keyDown || Focused ? Theme.Hex("#f5e9c8") : grayed ? Theme.Hex("#55524c") : Theme.Hex("#a99c80"));
            if (Focused)
                using (var p = new Pen(Theme.Hex("#fff2b0"), 1) { DashStyle = DashStyle.Dot })
                    g.DrawRectangle(p, 3, 3, Math.Max(1, Width - 7), Math.Max(1, Height - 7));
        }
    }

    public static class Icons
    {
        static readonly Color Hole = Theme.Hex("#1c130a");

        public static void Home(Graphics g, float x, float y, Color c)
        {
            using (var b = new SolidBrush(c))
            {
                g.FillPolygon(b, new[] { new PointF(x - 11, y), new PointF(x, y - 10), new PointF(x + 11, y) });
                g.FillRectangle(b, x - 7, y, 14, 10);
            }
            using (var b = new SolidBrush(Hole)) g.FillRectangle(b, x - 2, y + 4, 4, 6);
        }

        public static void Cloud(Graphics g, float x, float y, Color c)
        {
            using (var b = new SolidBrush(c))
            {
                g.FillEllipse(b, x - 10, y - 2, 9, 9);
                g.FillEllipse(b, x - 5, y - 9, 11, 11);
                g.FillEllipse(b, x + 1, y - 4, 9, 11);
                g.FillRectangle(b, x - 6, y, 12, 7);
            }
        }

        public static void AddonsIcon(Graphics g, float x, float y, Color c)
        {
            using (var b = new SolidBrush(c))
                foreach (int dx in new[] { -9, 1 })
                    foreach (int dy in new[] { -9, 1 })
                        g.FillRectangle(b, x + dx, y + dy, 8, 8);
        }

        public static void Gear(Graphics g, float x, float y, Color c)
        {
            using (var p = new Pen(c, 4))
                foreach (var d in new[] { new PointF(-11, 0), new PointF(11, 0), new PointF(0, -11), new PointF(0, 11),
                                          new PointF(-8, -8), new PointF(8, 8), new PointF(-8, 8), new PointF(8, -8) })
                    g.DrawLine(p, x, y, x + d.X, y + d.Y);
            using (var b = new SolidBrush(c)) g.FillEllipse(b, x - 7, y - 7, 14, 14);
            using (var b = new SolidBrush(Hole)) g.FillEllipse(b, x - 3, y - 3, 6, 6);
        }

        public static void Scroll(Graphics g, float x, float y, Color c)
        {
            using (var p = new Pen(c, 2))
            {
                g.DrawRectangle(p, x - 8, y - 10, 16, 20);
                foreach (int dy in new[] { -5, 0, 5 }) g.DrawLine(p, x - 4, y + dy, x + 4, y + dy);
            }
        }

        // an open book (the guide)
        public static void Book(Graphics g, float x, float y, Color c)
        {
            using (var p = new Pen(c, 2))
            {
                g.DrawLine(p, x, y - 7, x, y + 9);
                g.DrawLines(p, new[] { new PointF(x, y - 7), new PointF(x - 4, y - 9), new PointF(x - 11, y - 8),
                                       new PointF(x - 11, y + 7), new PointF(x - 4, y + 6), new PointF(x, y + 9) });
                g.DrawLines(p, new[] { new PointF(x, y - 7), new PointF(x + 4, y - 9), new PointF(x + 11, y - 8),
                                       new PointF(x + 11, y + 7), new PointF(x + 4, y + 6), new PointF(x, y + 9) });
            }
        }

        // a trophy cup, for the STATS section
        public static void Trophy(Graphics g, float x, float y, Color c)
        {
            using (var p = new Pen(c, 2))
            {
                g.DrawLines(p, new[] { new PointF(x - 7, y - 9), new PointF(x + 7, y - 9), new PointF(x + 6, y - 1),
                                       new PointF(x + 2, y + 2), new PointF(x - 2, y + 2), new PointF(x - 6, y - 1),
                                       new PointF(x - 7, y - 9) });
                g.DrawArc(p, x - 12, y - 8, 7, 7, 90, 180);      // the handles
                g.DrawArc(p, x + 5, y - 8, 7, 7, 270, 180);
                g.DrawLine(p, x, y + 2, x, y + 6);               // stem and base
                g.DrawLine(p, x - 6, y + 8, x + 6, y + 8);
            }
        }

        public static void Info(Graphics g, float x, float y, Color c)
        {
            using (var p = new Pen(c, 2)) g.DrawEllipse(p, x - 10, y - 10, 20, 20);
            using (var b = new SolidBrush(c))
            {
                g.FillEllipse(b, x - 1.5f, y - 6, 3, 3);
                g.FillRectangle(b, x - 1.5f, y - 1, 3, 8);
            }
        }

        // a small speech bubble (generic chat symbol) for the DISCORD button
        public static void Chat(Graphics g, float x, float y, float size, Color c)
        {
            float r = size / 2;
            using (var p = new Pen(c, 2)) g.DrawRectangle(p, x - r, y - r * 0.8f, 2 * r, r * 1.25f);
            using (var b = new SolidBrush(c))
            {
                g.FillPolygon(b, new[] { new PointF(x - r * 0.5f, y + r * 0.45f), new PointF(x - r * 0.5f, y + r * 0.95f),
                                         new PointF(x, y + r * 0.45f) });
                foreach (float dx in new[] { -r * 0.45f, 0f, r * 0.45f })
                    g.FillEllipse(b, x + dx - 1.5f, y - r * 0.2f - 1.5f, 3, 3);
            }
        }
    }

    // ---------- small helpers to build the sections and dialogs ----------
    public static class UI
    {
        public static Label Text(string text, float size, FontStyle style, Color fg, Color bg, int wrapWidth, bool translate = true)
        {
            var l = new Label();
            l.Text = translate ? Lang.T(text) : text;
            l.Font = Theme.F(size, style);
            l.ForeColor = fg;
            l.BackColor = bg;
            l.AutoSize = true;
            l.UseMnemonic = false;
            if (wrapWidth > 0) l.MaximumSize = new Size(wrapWidth, 0);
            return l;
        }

        public static Panel Line(Color color, int width, int height)
        {
            var p = new Panel();
            p.BackColor = color;
            p.Size = new Size(width, height);
            return p;
        }

        public static CheckBox Check(string text, bool value, Color fg, Color bg, float size, bool translate = true)
        {
            var c = new NightCheck();
            c.Text = translate ? Lang.T(text) : text;
            c.Checked = value;
            c.ForeColor = fg;
            c.BackColor = bg;
            c.Font = Theme.F(size);
            c.FlatStyle = FlatStyle.Flat;
            c.FlatAppearance.BorderColor = Theme.GoldDim;
            c.FlatAppearance.CheckedBackColor = Theme.Ink;
            c.FlatAppearance.MouseOverBackColor = bg;
            c.FlatAppearance.MouseDownBackColor = bg;
            c.AutoSize = true;
            c.UseMnemonic = false;
            c.Cursor = Cursors.Hand;
            return c;
        }

        public static RichTextBox Log(Color bg, float size)
        {
            var r = new RichTextBox();
            r.ReadOnly = true;
            r.BorderStyle = BorderStyle.None;
            r.BackColor = bg;
            r.ForeColor = Theme.Text;
            r.Font = Theme.F(size);
            r.DetectUrls = false;
            r.ScrollBars = RichTextBoxScrollBars.None;       // the slim gold bar instead (LogBar), added with the box
            r.WordWrap = true;
            LogBar.Attach(r);
            return r;
        }

        public static void Append(RichTextBox box, string text, Color color, Font font)
        {
            box.SelectionStart = box.TextLength;
            box.SelectionLength = 0;
            box.SelectionColor = color;
            if (font != null) box.SelectionFont = font;
            box.AppendText(text);
        }

        // the back arrow, top left, same spot and look in every window
        public static WowButton BackArrow(Action action)
        {
            return new WowButton("‹", action, 52, 32, 18);
        }

        public static Color LevelColor(Level lv)
        {
            switch (lv)
            {
                case Level.Good: return Theme.Good;
                case Level.Warn: return Theme.Gold;
                case Level.Error: return Theme.Bad;
                case Level.Dim: return Theme.GoldDim;
                default: return Theme.Text;
            }
        }
    }

    // A panel that scrolls with the mouse wheel; Overscroll(-1 / +1) is called when the wheel turns further
    // while it is already at its top / bottom.
    public interface IWheel { void Wheel(int delta); }

    public class ScrollPanel : Panel, IWheel
    {
        public Action<int> Overscroll;
        public event Action Scrolled;                         // any change of the scroll position (wheel, bar, code)

        void Moved() { if (Scrolled != null) Scrolled(); }

        // HideBars: Windows' own scroll bars are never drawn (a ThinScrollBar shows the position instead); the
        // panel still scrolls the same way. The styles are taken off each time Windows sizes the window frame.
        public bool HideBars;

        [System.Runtime.InteropServices.DllImport("user32.dll")]
        static extern int GetWindowLong(IntPtr hWnd, int index);

        [System.Runtime.InteropServices.DllImport("user32.dll")]
        static extern int SetWindowLong(IntPtr hWnd, int index, int value);

        protected override void WndProc(ref Message m)
        {
            if (HideBars && m.Msg == 0x83)                    // WM_NCCALCSIZE
            {
                const int GWL_STYLE = -16, WS_VSCROLL = 0x00200000, WS_HSCROLL = 0x00100000;
                // WM_NCCALCSIZE can run before Control.Handle is assigned. Use the message HWND to avoid recursively creating it.
                int style = GetWindowLong(m.HWnd, GWL_STYLE);
                if ((style & (WS_VSCROLL | WS_HSCROLL)) != 0) SetWindowLong(m.HWnd, GWL_STYLE, style & ~(WS_VSCROLL | WS_HSCROLL));
            }
            base.WndProc(ref m);
        }

        protected override void OnScroll(ScrollEventArgs se) { base.OnScroll(se); Moved(); }
        protected override void OnLayout(LayoutEventArgs levent) { base.OnLayout(levent); Moved(); }

        // scrolls to y (0 = top), within the content
        public void ScrollTo(int y)
        {
            int max = Math.Max(0, DisplayRectangle.Height - ClientSize.Height);
            AutoScrollPosition = new Point(0, Math.Max(0, Math.Min(max, y)));
            Moved();
        }

        public ScrollPanel()
        {
            AutoScroll = true;
            DoubleBuffered = true;
        }

        public void Wheel(int delta)
        {
            int d = delta > 0 ? -1 : 1;
            int max = Math.Max(0, DisplayRectangle.Height - ClientSize.Height);
            int pos = -AutoScrollPosition.Y;
            if (Overscroll != null && ((d < 0 && pos <= 0) || (d > 0 && pos >= max)))
            {
                Overscroll(d);
                return;
            }
            AutoScrollPosition = new Point(0, Math.Max(0, Math.Min(max, pos + d * Theme.Px(40))));
            Moved();
        }

        protected override void OnMouseWheel(MouseEventArgs e)
        {
            Wheel(e.Delta);
            var h = e as HandledMouseEventArgs;
            if (h != null) h.Handled = true;
        }

        public void ScrollToEnd()
        {
            AutoScrollPosition = new Point(0, Math.Max(0, DisplayRectangle.Height - ClientSize.Height));
            Moved();
        }
    }

    // A slim painted scroll bar for a ScrollPanel whose own (white Windows) bar is hidden: a dark track with a
    // rounded thumb; drag the thumb, click the track to page, the wheel works over it too.
    public class ThinScrollBar : Control, IWheel
    {
        readonly ScrollPanel target;
        readonly Color track, thumb, thumbHot;
        bool hover, dragging;
        int dragOffset;

        public ThinScrollBar(ScrollPanel target, Color track, Color thumb, Color thumbHot)
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer |
                     ControlStyles.ResizeRedraw, true);
            SetStyle(ControlStyles.Selectable, false);
            this.target = target;
            this.track = track;
            this.thumb = thumb;
            this.thumbHot = thumbHot;
            BackColor = track;
            target.Scrolled += delegate { Invalidate(); };
        }

        int Content { get { return Math.Max(1, target.DisplayRectangle.Height); } }
        int View { get { return Math.Max(1, target.ClientSize.Height); } }
        int Pos { get { return -target.AutoScrollPosition.Y; } }

        Rectangle Thumb()
        {
            if (Content <= View) return Rectangle.Empty;
            int h = Math.Max(30, Height * View / Content);
            int y = (int)((long)(Height - h) * Pos / Math.Max(1, Content - View));
            return new Rectangle(1, y, Width - 2, h);
        }

        public void Wheel(int delta) { target.Wheel(delta); }

        protected override void OnMouseEnter(EventArgs e) { hover = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { hover = false; WowButton.Tips.Hide(this); Invalidate(); base.OnMouseLeave(e); }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            if (e.Button != MouseButtons.Left) return;
            Rectangle t = Thumb();
            if (t.IsEmpty) return;
            if (t.Contains(e.Location)) { dragging = true; dragOffset = e.Y - t.Y; Capture = true; }
            else target.ScrollTo(Pos + (e.Y < t.Y ? -View : View) * 9 / 10);
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            if (!dragging) return;
            Rectangle t = Thumb();
            int room = Math.Max(1, Height - t.Height);
            target.ScrollTo((int)((long)(e.Y - dragOffset) * (Content - View) / room));
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            dragging = false;
            Capture = false;
            Invalidate();
        }

        protected override void OnMouseWheel(MouseEventArgs e)
        {
            target.Wheel(e.Delta);
            var h = e as HandledMouseEventArgs;
            if (h != null) h.Handled = true;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.Clear(track);
            Rectangle t = Thumb();
            if (t.IsEmpty) return;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            float r = Math.Min(t.Width, 8) / 2f;
            using (var path = new GraphicsPath())
            {
                path.AddArc(t.X, t.Y, 2 * r, 2 * r, 180, 180);
                path.AddArc(t.X, t.Bottom - 2 * r, 2 * r, 2 * r, 0, 180);
                path.CloseFigure();
                using (var b = new SolidBrush(hover || dragging ? thumbHot : thumb)) g.FillPath(b, path);
            }
        }
    }

    public enum Align { Left, Center, Right, Fill }

    // Lays its children out top to bottom (like Tk's pack): each keeps its own size, placed left, centred or right.
    public class VStack : Panel
    {
        class Item { public Control C; public Align A; public Padding M; public int NaturalWrap; }
        readonly List<Item> items = new List<Item>();
        public int MinWidth, MaxWidth;

        public VStack()
        {
            BackColor = Theme.Panel;
        }

        public T Add<T>(T c, Align a, int left, int top, int right, int bottom) where T : Control
        {
            var it = new Item();
            it.C = c;
            it.A = a;
            it.M = new Padding(left, top, right, bottom);
            it.NaturalWrap=c.MaximumSize.Width;
            items.Add(it);
            Controls.Add(c);
            return c;
        }

        public void RescaleSpacing(float ratio)
        {
            MinWidth=(int)Math.Round(MinWidth*ratio);
            foreach(var item in items)
            {
                Padding margin=item.M;
                item.M=new Padding((int)Math.Round(margin.Left*ratio),(int)Math.Round(margin.Top*ratio),(int)Math.Round(margin.Right*ratio),(int)Math.Round(margin.Bottom*ratio));
                item.NaturalWrap=(int)Math.Round(item.NaturalWrap*ratio);
            }
        }

        public void ClearItems()
        {
            SuspendLayout();
            foreach (var it in items)
            {
                Controls.Remove(it.C);
                it.C.Dispose();
            }
            items.Clear();
            ResumeLayout();
        }

        public static Size Measure(Control c)
        {
            var l = c as Label;
            if (l != null && l.AutoSize)
            {
                Size p = l.MaximumSize.Width > 0 ? l.GetPreferredSize(new Size(l.MaximumSize.Width, 0)) : l.GetPreferredSize(Size.Empty);
                if (l.MaximumSize.Width > 0 && p.Width > l.MaximumSize.Width) p.Width = l.MaximumSize.Width;
                return p;
            }
            var cb = c as CheckBox;
            if (cb != null && cb.AutoSize) return cb.GetPreferredSize(Size.Empty);
            return c.Size;
        }

        // positions everything and takes the size it needs (at least width, when given)
        public void Arrange(int width)
        {
            int w = Math.Max(MinWidth, width);
            foreach (var it in items)
                if (it.A != Align.Fill) w = Math.Max(w, Measure(it.C).Width + it.M.Horizontal);
            if(MaxWidth>0)w=Math.Min(w,MaxWidth);
            int y = 0;
            foreach (var it in items)
            {
                int available=Math.Max(20,w-it.M.Horizontal);
                Label label=it.C as Label;
                if(label!=null&&label.AutoSize&&MaxWidth>0)
                    label.MaximumSize=new Size(it.A==Align.Fill?available:it.NaturalWrap>0?Math.Min(it.NaturalWrap,available):available,0);
                HRow row=it.C as HRow;
                if(row!=null&&MaxWidth>0) row.WrapAt(available);
                Size s = Measure(it.C);
                if(MaxWidth>0)s.Width=Math.Min(s.Width,available);
                if (it.A == Align.Fill) s.Width = w - it.M.Horizontal;
                int x = it.A == Align.Center ? it.M.Left + (w - it.M.Horizontal - s.Width) / 2
                      : it.A == Align.Right ? w - it.M.Right - s.Width : it.M.Left;
                it.C.Bounds = new Rectangle(x, y + it.M.Top, s.Width, s.Height);
                y += it.M.Top + s.Height + it.M.Bottom;
            }
            Size = new Size(w, y);
        }
    }

    // Children left to right, centred on one line.
    public class HRow : Panel
    {
        int rowGap;
        public void RescaleSpacing(float ratio) {rowGap=(int)Math.Round(rowGap*ratio);}
        public void WrapAt(int width)
        {
            int x=0,y=0,rowHeight=0,used=0;
            foreach(Control child in Controls)
            {
                if(x>0&&x+child.Width>width) {y+=rowHeight+rowGap;x=0;rowHeight=0;}
                child.Location=new Point(x,y);rowHeight=Math.Max(rowHeight,child.Height);
                used=Math.Max(used,x+child.Width);x+=child.Width+rowGap;
            }
            Size=new Size(Math.Min(width,used),y+rowHeight);
        }
        public HRow(int gap, params Control[] children)
        {
            rowGap=gap;
            BackColor = Theme.Panel;
            int x = 0, h = 0;
            foreach (Control c in children) h = Math.Max(h, VStack.Measure(c).Height);
            foreach (Control c in children)
            {
                Size s = VStack.Measure(c);
                c.Bounds = new Rectangle(x, (h - s.Height) / 2, s.Width, s.Height);
                Controls.Add(c);
                x += s.Width + gap;
            }
            Size = new Size(Math.Max(0, x - gap), h);
        }
    }

    // Base for the pop-up windows: dark frame with a gold border, the back arrow top left, content stacked below.
    // the darkened picture of the launcher behind a dialog shown inside it: drawn from a kept picture, only the part
    // that needs it (a plain Panel with a BackgroundImage redraws the whole picture, slowly, whenever the dialog changes)
    public class ShadePanel : Panel
    {
        Bitmap pic;

        public ShadePanel(Bitmap pic)
        {
            this.pic = pic;
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer, true);
            BackColor = Color.Black;
        }

        protected override void OnPaintBackground(PaintEventArgs e) { }

        protected override void OnPaint(PaintEventArgs e)
        {
            if(pic==null){e.Graphics.Clear(Color.Black);return;}
            // This is only the dimmed, inactive backdrop; the dialog itself still draws native-resolution text.
            e.Graphics.InterpolationMode=InterpolationMode.HighQualityBicubic;
            e.Graphics.DrawImage(pic,ClientRectangle);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing && pic != null) { pic.Dispose(); pic = null; }
            base.Dispose(disposing);
        }
    }

    public class WowDialog : Form
    {
        protected readonly VStack Body;
        readonly Panel frame;
        public Action BackAction;
        float dialogDensity=Theme.DisplayScale;
        readonly List<Font> ownedScaledFonts=new List<Font>();

        void UpdateDialogDensity()
        {
            if(Math.Abs(dialogDensity-Theme.DisplayScale)<.001f)return;
            float ratio=Theme.DisplayScale/dialogDensity;
            dialogDensity=Theme.DisplayScale;
            var controls=new List<Control>();
            Action<Control> collect=null;
            collect=delegate(Control parent){foreach(Control child in parent.Controls){controls.Add(child);collect(child);}};
            collect(Body);
            Body.SuspendLayout();
            try
            {
                Body.RescaleSpacing(ratio);
                foreach(Control child in controls)
                {
                    child.Bounds=new Rectangle((int)Math.Round(child.Left*ratio),(int)Math.Round(child.Top*ratio),Math.Max(1,(int)Math.Round(child.Width*ratio)),Math.Max(1,(int)Math.Round(child.Height*ratio)));
                    if(child.MaximumSize.Width>0)child.MaximumSize=new Size((int)Math.Round(child.MaximumSize.Width*ratio),0);
                    WowButton button=child as WowButton;
                    if(button!=null)button.SetDisplayZoom(Theme.SectionZoom);
                    else if(child is Label||child is TextBoxBase||child is ComboBox||child is CheckBox)
                    {
                        Font old=child.Font;
                        float pixels=old.Unit==GraphicsUnit.Pixel?old.Size:old.Size*96f/72f;
                        var font=new Font(old.FontFamily,pixels*ratio,old.Style,GraphicsUnit.Pixel);
                        ownedScaledFonts.Add(font);child.Font=font;
                    }
                    var row=child as HRow;if(row!=null)row.RescaleSpacing(ratio);
                }
            }
            finally{Body.ResumeLayout();}
        }

        protected override void Dispose(bool disposing)
        {
            base.Dispose(disposing);
            if(disposing){foreach(Font font in ownedScaledFonts)font.Dispose();ownedScaledFonts.Clear();}
        }

        public WowDialog(string title)
        {
            Text = Lang.T(title);
            AutoScaleMode=AutoScaleMode.None;
            Icon = Theme.AppIcon;
            BackColor = Theme.BgBottom;
            DoubleBuffered = true;
            FormBorderStyle = FormBorderStyle.FixedSingle;
            MaximizeBox = false;
            ShowInTaskbar = true;          // its own taskbar button, so it does not depend on the launcher being open
            StartPosition = FormStartPosition.Manual;
            KeyPreview = true;
            frame = new Panel();
            frame.BackColor = Theme.Panel;
            frame.AutoScroll = true;
            frame.Location = new Point(10, 10);
            frame.Paint += delegate(object s, PaintEventArgs e)
            {
                using (var p = new Pen(Theme.GoldDim, 2)) e.Graphics.DrawRectangle(p, 1, 1, frame.Width - 2, frame.Height - 2);
            };
            Controls.Add(frame);
            Body = new VStack();
            Body.Location = new Point(2, 2);
            frame.Controls.Add(Body);
            BackAction = Close;
        }

        // starts a page: empties the window and puts the back arrow in its corner
        [System.Runtime.InteropServices.DllImport("user32.dll")]
        static extern IntPtr SendMessage(IntPtr h, int msg, IntPtr w, IntPtr l);
        [System.Runtime.InteropServices.DllImport("user32.dll")]
        static extern bool RedrawWindow(IntPtr h, IntPtr rect, IntPtr rgn, uint flags);
        bool pageFrozen;
        float previousPageZoom;
        bool pageZoomScope;

        // a new page is put together out of sight and shown at once (not drawn piece by piece as it is built)
        void FreezePage(bool on)
        {
            if (!frame.IsHandleCreated) { pageFrozen = false; return; }
            if (on && !pageFrozen) { SendMessage(frame.Handle, 0x000B, IntPtr.Zero, IntPtr.Zero); pageFrozen = true; }
            else if (!on && pageFrozen)
            {
                pageFrozen = false;
                SendMessage(frame.Handle, 0x000B, new IntPtr(1), IntPtr.Zero);
                RedrawWindow(frame.Handle, IntPtr.Zero, IntPtr.Zero, 0x0004 | 0x0001 | 0x0080 | 0x0100 | 0x0400);
            }
        }

        protected void Begin()
        {
            if(!pageZoomScope) { previousPageZoom=Theme.Zoom; pageZoomScope=true; }
            Theme.Zoom=Theme.SectionZoom;
            FreezePage(true);
            Body.ClearItems();
            Body.Add(UI.BackArrow(delegate { if (BackAction != null) BackAction(); }), Align.Left, 20, 12, 0, 0);
        }

        // ends a page: sizes the window to its content
        protected void Finish()
        {
            Body.Arrange(0);
            frame.Size = new Size(Body.Width + 4, Body.Height + 4);
            ClientSize = new Size(frame.Width + 2 * margin, frame.Height + 2 * margin);
            if (pageFrozen) FreezePage(false);
            else frame.Invalidate();
            if(pageZoomScope) { Theme.Zoom=previousPageZoom; pageZoomScope=false; }
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Escape && BackAction != null) { e.Handled = true; BackAction(); }
            base.OnKeyDown(e);
        }

        protected void Heading(string title, string right, int pad)
        {
            var t = UI.Text(title, 18, FontStyle.Bold, Theme.Gold, Theme.Panel, 0);
            if (right == null)
                Body.Add(t, Align.Center, pad, 4, pad, 4);
            else
            {
                var row = new Panel();
                row.BackColor = Theme.Panel;
                var r = UI.Text(right, 10, FontStyle.Italic, Theme.GoldDim, Theme.Panel, 0);
                Size ts = VStack.Measure(t), rs = VStack.Measure(r);
                int w = Math.Max(420, ts.Width + rs.Width + 30);
                t.Location = new Point(0, 0);
                r.Location = new Point(w - rs.Width, ts.Height - rs.Height - 4);
                r.Anchor = AnchorStyles.Top | AnchorStyles.Right;     // stays at the right edge when the row widens
                row.Controls.Add(t);
                row.Controls.Add(r);
                row.Size = new Size(w, ts.Height);
                Body.Add(row, Align.Fill, pad, 4, pad, 4);
            }
            Body.Add(UI.Line(Theme.GoldDim, 10, 2), Align.Fill, pad, 0, pad, 10);
        }

        protected Label Para(string text, float size, FontStyle style, Color fg, int pad, int top, int bottom, int wrap)
        {
            return Body.Add(UI.Text(text, size, style, fg, Theme.Panel, wrap), Align.Fill, pad, top, pad, bottom);
        }

        protected static WowButton B(string text, Action a, int w, int h, float size)
        {
            return new WowButton(text, a, w, h, size);
        }

        // ---- shown inside the launcher: over a darkened picture of it, and nothing behind it can be clicked until it
        // is answered. No window of its own (when the launcher is hidden or minimized, it opens as a window instead).
        public static Form Host;                              // the launcher, set when it starts
        public static Action RevealHost;                      // brings back a launcher that stepped aside for the game
        public static Form SecondaryHost;                     // section window, without changing its contents
        ShadePanel shade;
        bool closedInside;
        int margin = 10;                                      // the dark edge around the frame: only as a window of its own

        [System.Runtime.InteropServices.DllImport("user32.dll")]
        static extern uint MsgWaitForMultipleObjectsEx(uint count, IntPtr handles, uint ms, uint wake, uint flags);

        static bool HostUsable()
        {
            Form h = ActiveHost();
            // An inactive/minimized launcher still owns its questions. Never fall back to an unrelated top-level popup.
            return h != null && !h.IsDisposed && h.ClientSize.Width>0 && !h.InvokeRequired;
        }

        public static Form ActiveHost()
        {
            Form s = SecondaryHost;
            if (s != null && !s.IsDisposed && s.Visible && s.ClientSize.Width > 0 && s.ClientSize.Height > 0) return s;
            return Host;
        }

        public bool PutInLauncher()
        {
            if (RevealHost != null) try { RevealHost(); } catch (Exception) { }
            if (!HostUsable()) return false;
            Form h = ActiveHost();
            var bmp = new Bitmap(Math.Max(1, h.ClientSize.Width), Math.Max(1, h.ClientSize.Height), System.Drawing.Imaging.PixelFormat.Format32bppPArgb);
            // the launcher as it is on screen (DrawToBitmap leaves the section window and its scrolled lists black, and
            // is slow enough to flash): copied from the screen, then dimmed
            bool copied = false;
            try
            {
                if(LauncherApp.PreviewMode) throw new InvalidOperationException("Off-screen preview uses its own rendered client.");
                using (var g = Graphics.FromImage(bmp))
                    g.CopyFromScreen(h.PointToScreen(Point.Empty), Point.Empty, bmp.Size);
                copied = true;
            }
            catch (Exception) { }
            if (!copied) try { h.DrawToBitmap(bmp, new Rectangle(Point.Empty, bmp.Size)); } catch (Exception) { }
            using (var g = Graphics.FromImage(bmp))
            using (var dim = new SolidBrush(Color.FromArgb(175, 0, 0, 0))) g.FillRectangle(dim, 0, 0, bmp.Width, bmp.Height);
            shade = new ShadePanel(bmp);
            shade.Size=h.ClientSize; // set before clamping the dialog; a new Panel otherwise starts at 200x100
            shade.Dock = DockStyle.Fill;
            TopMost = false;
            ShowInTaskbar = false;
            TopLevel = false;
            FormBorderStyle = FormBorderStyle.None;
            // inside the launcher the frame is all there is: no dark edge around it (it showed as a black box over the
            // dimmed launcher)
            margin = 0;
            frame.Location = Point.Empty;
            // The dialog's content can scroll, but the window itself must never extend
            // beyond the section. Preserve the section controls beneath the shade.
            int maxW = Math.Max(1, shade.ClientSize.Width - 16);
            int maxH = Math.Max(1, shade.ClientSize.Height - 16);
            Body.MaxWidth=Math.Max(30,maxW-4);
            Body.Arrange(0);
            frame.Size = new Size(Math.Min(Body.Width+4, maxW), Math.Min(Body.Height+4, maxH));
            ClientSize = frame.Size;
            shade.Controls.Add(this);
            h.Controls.Add(shade);
            shade.BringToFront();
            SizeChanged += delegate { CenterInShade(); };
            shade.SizeChanged += delegate
            {
                if (shade == null) return;
                int w = Math.Max(1, shade.ClientSize.Width - 16), ht = Math.Max(1, shade.ClientSize.Height - 16);
                UpdateDialogDensity();
                Body.MaxWidth=Math.Max(30,w-4); Body.Arrange(0);
                frame.Size = new Size(Math.Min(Body.Width + 4, w), Math.Min(Body.Height + 4, ht));
                ClientSize = frame.Size;
                CenterInShade();
            };
            FormClosed += delegate { closedInside = true; RemoveShade(); };
            CenterInShade();
            Show();
            BringToFront();
            if(!LauncherApp.PreviewMode&&Host!=null&&(Host.ContainsFocus||Form.ActiveForm==Host)) Select();
            return true;
        }

        void CenterInShade()
        {
            if (shade == null) return;
            Location = new Point(Math.Max(0, (shade.Width - Width) / 2), Math.Max(0, (shade.Height - Height) / 2));
        }

        void RemoveShade()
        {
            ShadePanel s = shade;
            shade = null;
            if (s == null) return;
            if (s.Controls.Contains(this)) s.Controls.Remove(this);
            if (s.Parent != null) s.Parent.Controls.Remove(s);
            s.Dispose();                                     // and its picture
        }

        // like ShowDialog, but inside the launcher: returns when it is closed
        public DialogResult ShowModal(IWin32Window owner)
        {
            if (!PutInLauncher()) { PlaceOver(owner as Form); return ShowDialog(owner); }
            while (!closedInside && !IsDisposed && Host != null && !Host.IsDisposed)
            {
                Application.DoEvents();
                // sleeps until the next click, key or paint arrives (not a fixed pause: the dialog answers at once)
                if (!closedInside) MsgWaitForMultipleObjectsEx(0, IntPtr.Zero, 100, 0x04FF, 0x0004);
            }
            RemoveShade();
            return DialogResult;
        }

        // like Show (it stays open while the launcher goes on), but inside the launcher
        public void ShowInside()
        {
            if (!PutInLauncher()) { PlaceOver(Host); Show(); }
        }

        // centred over the launcher when that is on screen, otherwise centred on the screen
        public void PlaceOver(Form parent)
        {
            Rectangle area = Screen.FromPoint(Cursor.Position).WorkingArea;
            int x, y;
            if (parent != null && parent.Visible && parent.WindowState == FormWindowState.Normal)
            {
                area = Screen.FromControl(parent).WorkingArea;
                x = parent.Left + (parent.Width - Width) / 2;
                y = parent.Top + (parent.Height - Height) / 2;
            }
            else
            {
                x = area.Left + (area.Width - Width) / 2;
                y = area.Top + (area.Height - Height) / 3;
            }
            Location = new Point(Math.Max(area.Left, x), Math.Max(area.Top, y));
        }
    }

    // A question or a message, inside the launcher (Msg.Show uses it): the text and its buttons
    public class MsgDialog : WowDialog
    {
        public MsgDialog(string text, string caption, MessageBoxButtons buttons) : base(caption)
        {
            DialogResult no = buttons == MessageBoxButtons.OK ? DialogResult.OK
                            : buttons == MessageBoxButtons.YesNo ? DialogResult.No : DialogResult.Cancel;
            BackAction = delegate { Done(no); };
            Begin();
            Heading(caption, null, 26);
            Para(text, 11, FontStyle.Regular, Theme.Text, 26, 0, 6, 520);
            var bs = new List<Control>();
            if (buttons == MessageBoxButtons.OK) bs.Add(B("OK", delegate { Done(DialogResult.OK); }, 140, 36, 11));
            else if (buttons == MessageBoxButtons.OKCancel)
            {
                bs.Add(B("OK", delegate { Done(DialogResult.OK); }, 140, 36, 11));
                bs.Add(B("CANCEL", delegate { Done(DialogResult.Cancel); }, 140, 36, 11));
            }
            else
            {
                bs.Add(B("YES", delegate { Done(DialogResult.Yes); }, 140, 36, 11));
                bs.Add(B("NO", delegate { Done(DialogResult.No); }, 140, 36, 11));
                if (buttons == MessageBoxButtons.YesNoCancel) bs.Add(B("CANCEL", delegate { Done(DialogResult.Cancel); }, 140, 36, 11));
            }
            Body.Add(new HRow(12, bs.ToArray()), Align.Center, 26, 14, 26, 16);
            Finish();
        }

        void Done(DialogResult r)
        {
            DialogResult = r;
            Close();
        }
    }

    // A brand's own symbol as a link (Discord): drawn as it is, never recoloured or reshaped; the hover shows the
    // white version of the same symbol (the brand's own white), and a tooltip shows its link.
    public class BrandIcon : Control
    {
        readonly Image normal, hot;
        bool hover;
        bool keyDown;
        readonly string tip;

        public BrandIcon(Image normal, Image hot, string tip)
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer |
                     ControlStyles.SupportsTransparentBackColor, true);
            BackColor = Color.Transparent;
            this.normal = normal;
            this.hot = hot ?? normal;
            this.tip = tip ?? "";
            Cursor = Cursors.Hand;
            SetStyle(ControlStyles.Selectable, true);
            TabStop = true;
            AccessibleRole = AccessibleRole.PushButton;
            AccessibleName = Lang.T(this.tip.Split('\n')[0]);
            AccessibleDescription = Lang.T(this.tip);
            WowButton.Tips.SetToolTip(this, this.tip);
        }

        protected override void OnMouseEnter(EventArgs e) { hover = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { hover = false; if (!Focused) WowButton.Tips.Hide(this); Invalidate(); base.OnMouseLeave(e); }
        protected override void OnGotFocus(EventArgs e) { GuideHover.ShowTip(this, Lang.T(tip)); Invalidate(); base.OnGotFocus(e); }
        protected override void OnLostFocus(EventArgs e) { keyDown = false; GuideHover.Hide(this); Invalidate(); base.OnLostFocus(e); }
        protected override void OnKeyDown(KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Enter || e.KeyCode == Keys.Space) { keyDown = true; e.Handled = e.SuppressKeyPress = true; return; }
            base.OnKeyDown(e);
        }
        protected override void OnKeyUp(KeyEventArgs e)
        {
            bool click = keyDown && (e.KeyCode == Keys.Enter || e.KeyCode == Keys.Space);
            keyDown = false;
            base.OnKeyUp(e);
            Invalidate();
            if (click) OnClick(EventArgs.Empty);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Image img = hover ? hot : normal;
            if (img == null) return;
            // the symbol keeps its own proportions (65 x 48), centred
            float s = Math.Min((float)Width / img.Width, (float)Height / img.Height);
            float w = img.Width * s, h = img.Height * s;
            Theme.Draw(e.Graphics, img, (Width - w) / 2, (Height - h) / 2, w, h);
            if (!Enabled)
                using (var dim = new SolidBrush(Color.FromArgb(145, 12, 12, 16))) e.Graphics.FillRectangle(dim, ClientRectangle);
            if (Focused) using (var p = new Pen(Theme.Hex("#fff2b0"), 1) { DashStyle = DashStyle.Dot }) e.Graphics.DrawRectangle(p, 2, 2, Math.Max(1, Width - 5), Math.Max(1, Height - 5));
        }
    }

    // A small, quiet heart (left of the Discord symbol on the News line): a link to support the game's developer.
    // Drawn, not an image: dim red, brighter under the pointer.
    public class HeartIcon : Control
    {
        bool hover;
        bool keyDown;
        readonly string tip;

        public HeartIcon(string tip)
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer |
                     ControlStyles.SupportsTransparentBackColor, true);
            BackColor = Color.Transparent;
            this.tip = tip ?? "";
            Cursor = Cursors.Hand;
            SetStyle(ControlStyles.Selectable, true);
            TabStop = true;
            AccessibleRole = AccessibleRole.PushButton;
            AccessibleName = Lang.T(this.tip.Split('\n')[0]);
            AccessibleDescription = Lang.T(this.tip);
            WowButton.Tips.SetToolTip(this, this.tip);
        }

        protected override void OnMouseEnter(EventArgs e) { hover = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { hover = false; if (!Focused) WowButton.Tips.Hide(this); Invalidate(); base.OnMouseLeave(e); }
        protected override void OnGotFocus(EventArgs e) { GuideHover.ShowTip(this, Lang.T(tip)); Invalidate(); base.OnGotFocus(e); }
        protected override void OnLostFocus(EventArgs e) { keyDown = false; GuideHover.Hide(this); Invalidate(); base.OnLostFocus(e); }
        protected override void OnKeyDown(KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Enter || e.KeyCode == Keys.Space) { keyDown = true; e.Handled = e.SuppressKeyPress = true; return; }
            base.OnKeyDown(e);
        }
        protected override void OnKeyUp(KeyEventArgs e)
        {
            bool click = keyDown && (e.KeyCode == Keys.Enter || e.KeyCode == Keys.Space);
            keyDown = false;
            base.OnKeyUp(e);
            Invalidate();
            if (click) OnClick(EventArgs.Empty);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            // Scale the drawn heart to the Discord mark's visible height while keeping its own proportions.
            const float heartWidth = 0.7853f, heartHeight = 0.8185f, heartLeft = 0.1073f, heartTop = 0.1315f;
            float s = Math.Min((Width - 2f) / heartWidth, (Height - 0.8f) / heartHeight);
            float x = (Width - heartWidth * s) / 2f - heartLeft * s;
            float y = (Height - heartHeight * s) / 2f - heartTop * s;
            using (var p = new System.Drawing.Drawing2D.GraphicsPath())
            {
                p.AddBezier(x + s / 2, y + s * 0.95f, x - s * 0.15f, y + s * 0.45f, x + s * 0.12f, y - s * 0.12f, x + s / 2, y + s * 0.25f);
                p.AddBezier(x + s / 2, y + s * 0.25f, x + s * 0.88f, y - s * 0.12f, x + s * 1.15f, y + s * 0.45f, x + s / 2, y + s * 0.95f);
                p.CloseFigure();
                Color color = !Enabled ? Color.FromArgb(100, 116, 111, 102) :
                              hover || keyDown || Focused ? Color.FromArgb(230, 92, 92) : Color.FromArgb(150, 170, 70, 70);
                using (var b = new SolidBrush(color)) g.FillPath(b, p);
            }
            if (Focused) using (var p = new Pen(Theme.Hex("#fff2b0"), 1) { DashStyle = DashStyle.Dot }) g.DrawRectangle(p, 2, 2, Math.Max(1, Width - 5), Math.Max(1, Height - 5));
        }
    }

    // Two addons that can't be on together (Addons > My Addons): a red bracket from one's row to the other's, with
    // CONFLICT written down it.
    public class ConflictLink : Control
    {
        static readonly Color Red = Color.FromArgb(200, 68, 47);

        readonly Font font = Theme.F(7.5f, FontStyle.Bold);   // made with the section's zoom; cached, not disposed

        public ConflictLink()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer |
                     ControlStyles.ResizeRedraw, true);
        }

        public int Tick1, Tick2;                             // where the two rows' middles are, inside this control

        // the height the word needs (the bracket is made at least this tall, round the middle of the two rows)
        public int WordHeight()
        {
            return TextRenderer.MeasureText(Lang.T("CONFLICT"), font).Width + 10;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.Clear(BackColor);
            g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAliasGridFit;
            int x = Width / 2, t1 = Math.Max(1, Tick1), t2 = Math.Min(Height - 2, Tick2);
            string word = Lang.T("CONFLICT");
            Font f = font;
            SizeF ts = g.MeasureString(word, f);
            float mid = Height / 2f, half = ts.Width / 2f + 3;
            using (var pen = new Pen(Red, 2f))
            {
                // the ticks out to each row, and the line between them (open where the word is)
                g.DrawLine(pen, 0, t1, x, t1);
                g.DrawLine(pen, 0, t2, x, t2);
                g.DrawLine(pen, x, Math.Min(t1, mid - half), x, mid - half);
                g.DrawLine(pen, x, mid + half, x, Math.Max(t2, mid + half));
            }
            var state = g.Save();
            g.TranslateTransform(x, mid);
            g.RotateTransform(-90);
            using (var b = new SolidBrush(Red)) g.DrawString(word, f, b, -ts.Width / 2f, -ts.Height / 2f);
            g.Restore(state);
        }
    }
}
