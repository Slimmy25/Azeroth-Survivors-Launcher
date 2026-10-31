// The look of the STATS section, after the WoW armory sites: a character header with a framed portrait and stat
// chips, a tab bar, and rounded cards holding tiles, item-style rows (a framed "icon", a name in its quality color,
// a green line under it, the value on the right), progress bars and the monthly class chart.
// Every block paints itself and works out its own height for a given width (Fit).
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace AzerothLauncher
{
    public static class Armory
    {
        public static readonly Color Page = Theme.Hex("#0e0d14"), Card = Theme.Hex("#16151e"), CardEdge = Theme.Hex("#34323f"),
            Slot = Theme.Hex("#1b1a24"), SlotHover = Theme.Hex("#252331"), SlotEdge = Theme.Hex("#45414f"),
            Dim = Theme.Hex("#9c8a64"), Bright = Theme.Hex("#f5e9c8"), Green = Theme.Hex("#5fd35f"),
            Poor = Theme.Hex("#9d9d9d"), Common = Theme.Hex("#ffffff"), Uncommon = Theme.Hex("#1eff00"),
            Rare = Theme.Hex("#3b8ff0"), Epic = Theme.Hex("#b35cf5"), Legendary = Theme.Hex("#ff8000");

        // a share (0..1) as an item quality: the more often, the rarer the color
        public static Color Quality(double share)
        {
            if (share >= 0.5) return Legendary;
            if (share >= 0.3) return Epic;
            if (share >= 0.15) return Rare;
            if (share >= 0.05) return Uncommon;
            return Common;
        }

        public static GraphicsPath Round(RectangleF r, float rad)
        {
            var p = new GraphicsPath();
            float d = Math.Min(rad * 2, Math.Min(r.Width, r.Height));
            if (d <= 1) { p.AddRectangle(r); return p; }
            p.AddArc(r.X, r.Y, d, d, 180, 90);
            p.AddArc(r.Right - d, r.Y, d, d, 270, 90);
            p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
            p.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
            p.CloseFigure();
            return p;
        }

        public static void Box(Graphics g, RectangleF r, float rad, Color fill, Color edge)
        {
            using (var path = Round(r, rad))
            {
                using (var b = new SolidBrush(fill)) g.FillPath(b, path);
                if (edge != Color.Empty) using (var p = new Pen(edge, 1)) g.DrawPath(p, path);
            }
        }

        public static float Z = 1f;                          // the block being painted is drawn at this scale

        public static void Text(Graphics g, string text, Font f, Color c, Rectangle r, TextFormatFlags flags)
        {
            if (Z != 1f)                                     // TextRenderer ignores the scale transform: scale font and box here
            {
                float old = Theme.Zoom;
                Theme.Zoom = 1f;
                f = Theme.F(f.SizeInPoints * Z, f.Style);
                Theme.Zoom = old;
                r = new Rectangle((int)(r.X * Z), (int)(r.Y * Z), (int)Math.Ceiling(r.Width * Z) + 1, (int)Math.Ceiling(r.Height * Z) + 1);
            }
            TextRenderer.DrawText(g, text ?? "", f, r, c, flags | TextFormatFlags.NoPrefix);
        }

        // measured as Text() will draw it (the scaled font has a minimum size, so it is not simply size * Z),
        // given back in the block's own, unscaled units
        static Font Drawn(Font f)
        {
            if (Z == 1f) return f;
            float old = Theme.Zoom;
            Theme.Zoom = 1f;
            try { return Theme.F(f.SizeInPoints * Z, f.Style); }
            finally { Theme.Zoom = old; }
        }

        public static int TextWidth(string text, Font f)
        {
            int w = TextRenderer.MeasureText(text ?? "", Drawn(f), new Size(int.MaxValue, int.MaxValue), TextFormatFlags.NoPrefix | TextFormatFlags.NoPadding).Width;
            return Z == 1f ? w : (int)Math.Ceiling(w / Z);
        }

        public static int TextHeight(string text, Font f, int width)
        {
            int h = TextRenderer.MeasureText(text ?? "", Drawn(f), new Size(Math.Max(10, (int)(width * Z)), int.MaxValue),
                                             TextFormatFlags.NoPrefix | TextFormatFlags.WordBreak).Height;
            return Z == 1f ? h : (int)Math.Ceiling(h / Z);
        }

        // a picture in an icon's place, clipped to its rounded (or round) frame; a creature's model picture is cut to
        // its upper middle (the head and shoulders)
        public static void Picture(Graphics g, Rectangle r, Image pic, bool portrait, bool round, Color frame)
        {
            using (var path = round ? Ellipse(r) : Round(r, 5))
            {
                var clip = g.Clip;
                g.SetClip(path, CombineMode.Intersect);
                g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                using (var bg = new SolidBrush(Theme.Hex("#0b0a10"))) g.FillRectangle(bg, r);
                if (portrait)
                {
                    int s = (int)(Math.Min(pic.Width, pic.Height) * 0.62);
                    var src = new Rectangle((pic.Width - s) / 2, (int)(pic.Height * 0.04), s, s);
                    g.DrawImage(pic, r, src, GraphicsUnit.Pixel);
                }
                else Theme.Draw(g, pic, r);
                g.Clip = clip;
                using (var p = new Pen(frame, 1.5f)) g.DrawPath(p, path);
            }
        }

        // the stand-in for an item icon: a framed square (or round) badge with a gradient and the initials
        public static void Icon(Graphics g, Rectangle r, Color color, string letters, bool round)
        {
            using (var path = round ? Ellipse(r) : Round(r, 5))
            {
                using (var lg = new LinearGradientBrush(r, Theme.Mix(color, Color.Black, 0.35), Theme.Mix(color, Color.Black, 0.8),
                                                        LinearGradientMode.ForwardDiagonal))
                    g.FillPath(lg, path);
                using (var p = new Pen(color, 2)) g.DrawPath(p, path);
            }
            if (letters == Lang.T("Image soon"))                 // no real picture yet: the words, small, on two lines
                Text(g, letters, Theme.F(r.Height >= 40 ? 7.5f : 6.5f, FontStyle.Bold), Bright, Rectangle.Inflate(r, -2, 0),
                     TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.WordBreak);
            else
                Text(g, letters, Theme.F(r.Height >= 40 ? 13 : 10, FontStyle.Bold), Bright, r,
                     TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine);
        }

        static GraphicsPath Ellipse(Rectangle r)
        {
            var p = new GraphicsPath();
            p.AddEllipse(r);
            return p;
        }

        // "Shadow Bolt" -> "SB", "Fireball" -> "Fi"
        public static string Initials(string name)
        {
            var parts = (name ?? "").Split(new[] { ' ', '_', '-' }, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length == 0) return "?";
            if (parts.Length == 1) return parts[0].Length > 1 ? parts[0].Substring(0, 2) : parts[0].ToUpperInvariant();
            return (parts[0].Substring(0, 1) + parts[1].Substring(0, 1)).ToUpperInvariant();
        }

        // small vector glyphs for the header chips
        public static void Glyph(Graphics g, string kind, float x, float y, Color c)
        {
            using (var p = new Pen(c, 1.8f))
            using (var b = new SolidBrush(c))
            {
                if (kind == "kills")                          // crossed swords
                {
                    g.DrawLine(p, x - 6, y - 6, x + 6, y + 6);
                    g.DrawLine(p, x + 6, y - 6, x - 6, y + 6);
                    g.DrawLine(p, x - 7, y + 3, x - 3, y + 7);
                    g.DrawLine(p, x + 7, y + 3, x + 3, y + 7);
                }
                else if (kind == "bosses")                    // a crown
                    g.DrawLines(p, new[] { new PointF(x - 7, y + 5), new PointF(x - 7, y - 4), new PointF(x - 3, y),
                                           new PointF(x, y - 6), new PointF(x + 3, y), new PointF(x + 7, y - 4),
                                           new PointF(x + 7, y + 5), new PointF(x - 7, y + 5) });
                else if (kind == "gold")                      // a coin
                {
                    g.FillEllipse(b, x - 6, y - 6, 12, 12);
                    using (var dp = new Pen(Theme.Mix(c, Color.Black, 0.5), 1.4f)) g.DrawEllipse(dp, x - 3.5f, y - 3.5f, 7, 7);
                }
                else if (kind == "time")                      // a clock
                {
                    g.DrawEllipse(p, x - 6.5f, y - 6.5f, 13, 13);
                    g.DrawLine(p, x, y, x, y - 4);
                    g.DrawLine(p, x, y, x + 3, y + 2);
                }
                else if (kind == "win")                       // a flag
                {
                    g.DrawLine(p, x - 5, y - 7, x - 5, y + 7);
                    g.FillPolygon(b, new[] { new PointF(x - 4, y - 7), new PointF(x + 7, y - 3.5f), new PointF(x - 4, y) });
                }
                else g.FillEllipse(b, x - 4, y - 4, 8, 8);
            }
        }
    }

    // a rounded card with an optional title; subclasses paint the rest
    public abstract class ArmoryBlock : Control
    {
        public string Title;                                 // already translated (or null: no title)
        protected const int Pad = 14, TitleH = 36;
        public bool Framed = true;

        protected readonly float Zm = Theme.Zoom;            // made inside a section: everything drawn smaller
        protected new int Width { get { return (int)(base.Width / Zm); } }
        protected new int Height { get { return (int)(base.Height / Zm); } }
        protected Point P(Point p) { return new Point((int)(p.X / Zm), (int)(p.Y / Zm)); }

        protected ArmoryBlock()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer |
                     ControlStyles.ResizeRedraw, true);
            BackColor = Armory.Page;
        }

        protected int Top0 { get { return Title != null ? TitleH : Pad; } }

        public void Fit(int width)
        {
            float old = Theme.Zoom;
            Theme.Zoom = 1f;                                 // laid out at full size, painted scaled
            Armory.Z = Zm;                                   // measured as painted
            try
            {
                base.Width = width;
                base.Height = (int)Math.Ceiling((Top0 + ContentHeight(Width - 2 * Pad) + Pad) * Zm);
                Layout2();
            }
            finally { Theme.Zoom = old; Armory.Z = 1f; }
        }

        protected virtual void Layout2() { }
        protected abstract int ContentHeight(int w);
        protected abstract void PaintContent(Graphics g, Rectangle r);

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.Clear(BackColor);
            Theme.Smooth(g);
            g.ScaleTransform(Zm, Zm);
            Armory.Z = Zm;
            float oldZoom = Theme.Zoom;
            Theme.Zoom = 1f;
            try { PaintAll(g); }
            finally { Armory.Z = 1f; Theme.Zoom = oldZoom; }
        }

        void PaintAll(Graphics g)
        {
            if (Framed) Armory.Box(g, new RectangleF(0.5f, 0.5f, Width - 1.5f, Height - 1.5f), 8, Armory.Card, Armory.CardEdge);
            if (Title != null)
            {
                Armory.Text(g, Title, Theme.F(12.5f, FontStyle.Bold), Theme.Gold, new Rectangle(Pad, 9, Width - 2 * Pad, 22),
                            TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis);
                using (var lg = new LinearGradientBrush(new Rectangle(Pad, 31, Math.Max(2, Width - 2 * Pad), 1), Theme.GoldDim,
                                                        Armory.Card, LinearGradientMode.Horizontal))
                    g.FillRectangle(lg, Pad, 31, Width - 2 * Pad, 1);
            }
            PaintContent(g, new Rectangle(Pad, Top0, Width - 2 * Pad, Height - Top0 - Pad));
        }
    }

    // the character header: framed portrait, name, a line under it and stat chips
    public class HeroBlock : ArmoryBlock
    {
        public string Heading = "", Line = "", Note = "";
        public Color Accent = Theme.Gold;
        public string Letters;                               // shown in the frame instead of the emblem
        public string ClassPic;                              // or the class's icon (from Wowhead), when it has arrived
        public readonly List<string[]> Chips = new List<string[]>();     // { glyph, value, label }
        int chipRows = 1;
        const int Pic = 76;

        List<Rectangle> ChipRects(int x0, int y0, int right)
        {
            var list = new List<Rectangle>();
            int x = x0, y = y0;
            Font fv = Theme.F(10, FontStyle.Bold), fl = Theme.F(8.5f);
            foreach (var c in Chips)
            {
                int w = 30 + Armory.TextWidth(c[1], fv) + 6 + Armory.TextWidth(c[2], fl) + 12;
                if (x > x0 && x + w > right) { x = x0; y += 32; }
                list.Add(new Rectangle(x, y, w, 26));
                x += w + 8;
            }
            return list;
        }

        protected override int ContentHeight(int w)
        {
            var r = ChipRects(0, 0, w);                      // the chips run under the portrait, the whole width
            chipRows = r.Count == 0 ? 0 : (r[r.Count - 1].Y / 32) + 1;
            return Pic + (chipRows > 0 ? 12 + chipRows * 32 - 6 : 0);
        }

        protected override void PaintContent(Graphics g, Rectangle r)
        {
            var frame = new Rectangle(r.X, r.Y, Pic, Pic);
            Armory.Box(g, frame, 6, Theme.Hex("#0b0704"), Color.Empty);
            Image cp = ClassPic != null ? SpellIcons.Class(ClassPic, this) : null;
            if (cp != null) Armory.Picture(g, Rectangle.Inflate(frame, -5, -5), cp, false, false, Accent);
            else if (Letters != null) Armory.Icon(g, Rectangle.Inflate(frame, -6, -6), Accent, Letters, true);
            else if (Theme.Portrait != null) Theme.Draw(g, Theme.Portrait, Rectangle.Inflate(frame, -4, -4));
            using (var p = new Pen(Accent, 2)) using (var path = Armory.Round(frame, 6)) g.DrawPath(p, path);
            int x = r.X + Pic + 16;
            Armory.Text(g, Heading, Theme.F(18, FontStyle.Bold), Accent, new Rectangle(x, r.Y + 8, r.Right - x, 30),
                        TextFormatFlags.Left | TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis);
            Armory.Text(g, Line, Theme.F(10), Armory.Bright, new Rectangle(x, r.Y + 42, r.Right - x, 20),
                        TextFormatFlags.Left | TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis);
            if (Note.Length > 0)
            {
                int nw = r.Right - x;                    // right-aligned: the whole width is free above the heading
                Armory.Text(g, Note, Theme.F(8.5f, FontStyle.Italic), Armory.Dim, new Rectangle(r.Right - nw, r.Y + 2, nw, 18),
                            TextFormatFlags.Right | TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis);
            }
            var rects = ChipRects(r.X, r.Y + Pic + 12, r.Right);
            for (int i = 0; i < rects.Count; i++)
            {
                Rectangle c = rects[i];
                Armory.Box(g, c, 6, Armory.Slot, Armory.SlotEdge);
                Armory.Glyph(g, Chips[i][0], c.X + 15, c.Y + 13, Chips[i][0] == "gold" ? Theme.Gold : Armory.Bright);
                Font fv = Theme.F(10, FontStyle.Bold);
                int vw = Armory.TextWidth(Chips[i][1], fv);
                Armory.Text(g, Chips[i][1], fv, Armory.Bright, new Rectangle(c.X + 28, c.Y, vw + 4, c.Height),
                            TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine);
                Armory.Text(g, Chips[i][2], Theme.F(8.5f), Armory.Dim, new Rectangle(c.X + 28 + vw + 6, c.Y, c.Right - (c.X + 28 + vw + 6), c.Height),
                            TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine);
            }
        }
    }

    // the tab bar under the header
    public class TabsBlock : ArmoryBlock
    {
        public readonly List<string> Tabs = new List<string>();      // translated
        public int Active, hover = -1;
        public Action<int> Picked;
        readonly List<Rectangle> rects = new List<Rectangle>();

        public TabsBlock() { Cursor = Cursors.Hand; }

        protected override int ContentHeight(int w) { return 30; }

        protected override void Layout2()
        {
            rects.Clear();
            int x = Pad - 6, avail = Width - 2 * (Pad - 6);
            Font f = Theme.F(10.5f, FontStyle.Bold);
            int total = 0;
            foreach (string t in Tabs) total += Armory.TextWidth(t, f) + 28;
            double scale = total > avail ? (double)avail / total : 1;
            foreach (string t in Tabs)
            {
                int w = (int)((Armory.TextWidth(t, f) + 28) * scale);
                rects.Add(new Rectangle(x, 8, w, Height - 16));
                x += w + (scale < 1 ? 0 : 4);
            }
        }

        protected override void PaintContent(Graphics g, Rectangle r)
        {
            Font f = Theme.F(10.5f, FontStyle.Bold);
            for (int i = 0; i < Tabs.Count && i < rects.Count; i++)
            {
                Rectangle t = rects[i];
                if (i == Active) Armory.Box(g, t, 6, Theme.Hex("#2a2236"), Theme.Gold);   // the open tab: gold frame, like the buttons
                else if (i == hover) Armory.Box(g, t, 6, Armory.SlotHover, Color.Empty);
                Armory.Text(g, Tabs[i], f, i == Active ? Theme.Gold : Armory.Bright, t,
                            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis);
            }
        }

        int At(Point p)
        {
            for (int i = 0; i < rects.Count; i++) if (rects[i].Contains(p)) return i;
            return -1;
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            int h = At(P(e.Location));
            if (h != hover) { hover = h; Invalidate(); }
            base.OnMouseMove(e);
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            hover = -1;
            Invalidate();
            base.OnMouseLeave(e);
        }

        protected override void OnMouseClick(MouseEventArgs e)
        {
            int i = At(P(e.Location));
            if (i >= 0 && i != Active && Picked != null) Picked(i);
            base.OnMouseClick(e);
        }
    }

    // stat tiles: a big value over its name
    public class TilesBlock : ArmoryBlock
    {
        public readonly List<string[]> Items = new List<string[]>();     // { value, translated label }
        public int Cols = 4;
        const int Th = 62, Gap = 8;

        protected override int ContentHeight(int w)
        {
            int rows = (Items.Count + Cols - 1) / Cols;
            return rows * (Th + Gap) - Gap;
        }

        protected override void PaintContent(Graphics g, Rectangle r)
        {
            int tw = (r.Width - (Cols - 1) * Gap) / Cols;
            for (int i = 0; i < Items.Count; i++)
            {
                var t = new Rectangle(r.X + (i % Cols) * (tw + Gap), r.Y + (i / Cols) * (Th + Gap), tw, Th);
                Armory.Box(g, t, 6, Armory.Slot, Armory.SlotEdge);
                using (var b = new SolidBrush(Theme.Mix(Theme.Gold, Armory.Slot, 0.35))) g.FillRectangle(b, t.X + 10, t.Y + 1, t.Width - 20, 2);
                Armory.Text(g, Items[i][0], Theme.F(15, FontStyle.Bold), Armory.Bright, new Rectangle(t.X + 2, t.Y + 8, t.Width - 4, 28),
                            TextFormatFlags.HorizontalCenter | TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis);
                Armory.Text(g, Items[i][1], Theme.F(8.5f), Armory.Dim, new Rectangle(t.X + 2, t.Y + 37, t.Width - 4, 18),
                            TextFormatFlags.HorizontalCenter | TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis);
            }
        }
    }

    // one item-style line
    public class ArmoryRow
    {
        public string Icon = "?", Name = "", Sub = "", Sub2 = "", Value = "", ValueSub = "";     // Sub2: a dim third line
        public Color IconColor = Theme.Gold, NameColor = Armory.Bright, SubColor = Armory.Green, ValueColor = Armory.Bright;
        public bool RoundIcon;
        public string Spell, ClassPic, Npc, RacePic, ZonePic;   // a picture from Wowhead instead of the badge (SpellIcons)
        public string IconName;                              // an icon by its own name (an item's), from Wowhead
        public bool Help;                                    // Click opens the guide about it: a small "?" on the icon
        public double Bar = -1;                              // 0..1 draws a thin bar under the line
        public Action Click;
    }

    // item-style lines in one or two columns, like the armory's gear slots
    public class RowsBlock : ArmoryBlock
    {
        public readonly List<ArmoryRow> Rows = new List<ArmoryRow>();
        public int Cols = 1;
        public string Empty = "";                            // translated text when there are no rows
        const int Gap = 8;
        int Rh { get { return Rows.Exists(delegate(ArmoryRow x) { return x.Sub2.Length > 0; }) ? 72 : 54; } }
        int hover = -1;
        readonly List<Rectangle> rects = new List<Rectangle>();

        protected override int ContentHeight(int w)
        {
            if (Rows.Count == 0) return Armory.TextHeight(Empty, Theme.F(10, FontStyle.Italic), w);
            int rows = (Rows.Count + Cols - 1) / Cols;
            return rows * (Rh + Gap) - Gap;
        }

        protected override void Layout2()
        {
            rects.Clear();
            int w = Width - 2 * Pad, cw = (w - (Cols - 1) * Gap) / Cols;
            for (int i = 0; i < Rows.Count; i++)
                rects.Add(new Rectangle(Pad + (i % Cols) * (cw + Gap), Top0 + (i / Cols) * (Rh + Gap), cw, Rh));
        }

        protected override void PaintContent(Graphics g, Rectangle r)
        {
            if (Rows.Count == 0)
            {
                Armory.Text(g, Empty, Theme.F(10, FontStyle.Italic), Armory.Dim, r, TextFormatFlags.Left | TextFormatFlags.WordBreak);
                return;
            }
            for (int i = 0; i < Rows.Count && i < rects.Count; i++)
            {
                ArmoryRow row = Rows[i];
                Rectangle s = rects[i];
                Armory.Box(g, s, 6, i == hover && row.Click != null ? Armory.SlotHover : Armory.Slot, Armory.SlotEdge);
                var icon = new Rectangle(s.X + 7, s.Y + 7, 40, 40);
                // only a row that is on screen asks for its icon: scrolling down fetches the rest as they come into view
                Image pic = null;
                if (g.VisibleClipBounds.IntersectsWith(icon))
                    pic = row.Spell != null ? SpellIcons.Spell(row.Spell, this) : row.ClassPic != null ? SpellIcons.Class(row.ClassPic, this)
                        : row.Npc != null ? SpellIcons.Npc(row.Npc, this) : row.RacePic != null ? SpellIcons.Race(row.RacePic, this)
                        : row.ZonePic != null ? SpellIcons.Zone(row.ZonePic, this)
                        : !string.IsNullOrEmpty(row.IconName) ? SpellIcons.Icon(row.IconName, this) : null;
                if (pic != null) Armory.Picture(g, icon, pic, false, row.RoundIcon, row.IconColor);   // creatures come cut to the face
                else Armory.Icon(g, icon, row.IconColor, row.Icon, row.RoundIcon);
                if (row.Help && row.Click != null)
                {
                    var q = new Rectangle(icon.Right - 13, icon.Y - 3, 16, 16);
                    using (var b = new SolidBrush(i == hover ? Theme.Gold : Theme.Hex("#2a2236"))) g.FillEllipse(b, q);
                    using (var p = new Pen(Theme.Gold, 1)) g.DrawEllipse(p, q);
                    Armory.Text(g, "?", Theme.F(8, FontStyle.Bold), i == hover ? Theme.Hex("#1a1006") : Theme.Gold, q,
                                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine);
                }
                Font fv = Theme.F(11, FontStyle.Bold), fvs = Theme.F(8.5f);
                int vw = Math.Max(Armory.TextWidth(row.Value, fv), Armory.TextWidth(row.ValueSub, fvs));
                vw = Math.Min(vw + 4, s.Width / 3);
                int tx = icon.Right + 10, tw = s.Right - 10 - vw - 8 - tx;
                Armory.Text(g, row.Name, Theme.F(10.5f, FontStyle.Bold), row.NameColor, new Rectangle(tx, s.Y + 8, tw, 20),
                            TextFormatFlags.Left | TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis);
                Armory.Text(g, row.Sub, Theme.F(8.5f), row.SubColor, new Rectangle(tx, s.Y + 28, tw, 18),
                            TextFormatFlags.Left | TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis);
                var vr = new Rectangle(s.Right - 10 - vw, s.Y + 8, vw, 20);
                Armory.Text(g, row.Value, fv, row.ValueColor, vr, TextFormatFlags.Right | TextFormatFlags.SingleLine);
                if (row.Sub2.Length > 0)
                    Armory.Text(g, row.Sub2, Theme.F(8.5f, FontStyle.Italic), Armory.Dim, new Rectangle(tx, s.Y + 46, s.Right - 10 - tx, 18),
                                TextFormatFlags.Left | TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis);
                if (row.ValueSub.Length > 0)
                    Armory.Text(g, row.ValueSub, fvs, Armory.Dim, new Rectangle(vr.X, s.Y + 28, vw, 18), TextFormatFlags.Right | TextFormatFlags.SingleLine);
                if (row.Bar >= 0)
                {
                    var track = new Rectangle(tx, s.Bottom - 7, s.Right - 10 - tx, 3);
                    using (var b = new SolidBrush(Armory.SlotEdge)) g.FillRectangle(b, track);
                    using (var b = new SolidBrush(row.IconColor))
                        g.FillRectangle(b, track.X, track.Y, (int)(track.Width * Math.Max(0, Math.Min(1, row.Bar))), track.Height);
                }
            }
        }

        int At(Point p)
        {
            for (int i = 0; i < rects.Count; i++) if (rects[i].Contains(p)) return i;
            return -1;
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            int h = At(P(e.Location));
            Cursor = h >= 0 && Rows[h].Click != null ? Cursors.Hand : Cursors.Default;
            if (h != hover) { hover = h; Invalidate(); }
            base.OnMouseMove(e);
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            hover = -1;
            Invalidate();
            base.OnMouseLeave(e);
        }

        protected override void OnMouseClick(MouseEventArgs e)
        {
            int i = At(P(e.Location));
            if (i >= 0 && Rows[i].Click != null) Rows[i].Click();
            base.OnMouseClick(e);
        }
    }

    // progress bars: a name, "x of y" on the right, the bar under them
    public class BarsBlock : ArmoryBlock
    {
        public readonly List<object[]> Bars = new List<object[]>();      // { translated name, translated right text, double 0..1, Color }
        const int Bh = 34;

        protected override int ContentHeight(int w) { return Bars.Count * Bh - 4; }

        protected override void PaintContent(Graphics g, Rectangle r)
        {
            for (int i = 0; i < Bars.Count; i++)
            {
                int y = r.Y + i * Bh;
                string right = (string)Bars[i][1];
                Font fr = Theme.F(8.5f, FontStyle.Italic);
                int rw = Armory.TextWidth(right, fr) + 4;
                Armory.Text(g, (string)Bars[i][0], Theme.F(10, FontStyle.Bold), Armory.Bright, new Rectangle(r.X, y, r.Width - rw - 8, 18),
                            TextFormatFlags.Left | TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis);
                Armory.Text(g, right, fr, Armory.Dim, new Rectangle(r.Right - rw, y + 1, rw, 18), TextFormatFlags.Right | TextFormatFlags.SingleLine);
                var track = new RectangleF(r.X, y + 20, r.Width, 8);
                Armory.Box(g, track, 4, Armory.Slot, Armory.SlotEdge);
                double frac = Math.Max(0, Math.Min(1, (double)Bars[i][2]));
                if (frac > 0)
                {
                    var fill = new RectangleF(track.X, track.Y, Math.Max(8, (float)(track.Width * frac)), track.Height);
                    Color c = (Color)Bars[i][3];
                    using (var path = Armory.Round(fill, 4))
                    using (var lg = new LinearGradientBrush(fill, Theme.Mix(c, Color.Black, 0.35), c, LinearGradientMode.Horizontal))
                        g.FillPath(lg, path);
                }
            }
        }
    }

    // text paragraphs, and an optional button under them
    public class TextBlock : ArmoryBlock
    {
        public readonly List<object[]> Lines = new List<object[]>();     // { translated text, Color, float size, bool italic }
        public WowButton Button;

        protected override int ContentHeight(int w)
        {
            int h = 0;
            foreach (var l in Lines) h += Armory.TextHeight((string)l[0], LineFont(l), w) + 6;
            if (Button != null) h += Button.Height + 8;
            return Math.Max(0, h - 6);
        }

        static Font LineFont(object[] l) { return Theme.F((float)l[2], (bool)l[3] ? FontStyle.Italic : FontStyle.Regular); }

        protected override void Layout2()
        {
            if (Button == null) return;
            if (Button.Parent != this) Controls.Add(Button);
            Button.Location = new Point(Pad, Height - Pad - Button.Height);
        }

        protected override void PaintContent(Graphics g, Rectangle r)
        {
            int y = r.Y;
            foreach (var l in Lines)
            {
                Font f = LineFont(l);
                int h = Armory.TextHeight((string)l[0], f, r.Width);
                Armory.Text(g, (string)l[0], f, (Color)l[1], new Rectangle(r.X, y, r.Width, h), TextFormatFlags.Left | TextFormatFlags.WordBreak);
                y += h + 6;
            }
        }
    }

    // class picks month by month: one bar a month (the last 12), split into the classes, with a legend
    public class MonthsBlock : ArmoryBlock
    {
        readonly List<MonthStat> months;
        readonly List<string> order = new List<string>();
        readonly Dictionary<string, string> names;
        public string Only;                                  // one class: its share of each month instead
        const int RowH = 24, LabelW = 70, LegendH = 20;
        int legendRows;

        public MonthsBlock(List<MonthStat> all, Dictionary<string, string> names)
        {
            this.names = names;
            months = all.Count > 12 ? all.GetRange(all.Count - 12, 12) : new List<MonthStat>(all);
            var totals = new Dictionary<string, double>();
            foreach (var m in months)
                foreach (var kv in m.Classes)
                {
                    double t;
                    totals.TryGetValue(kv.Key, out t);
                    totals[kv.Key] = t + kv.Value;
                }
            order.AddRange(totals.Keys);
            order.Sort(delegate(string a, string b) { return totals[b].CompareTo(totals[a]); });
        }

        string Name(string key)
        {
            string n;
            return names != null && names.TryGetValue(key, out n) && n.Length > 0 ? n : Stats.Pretty(key);
        }

        protected override int ContentHeight(int w)
        {
            legendRows = 0;
            if (Only == null && order.Count > 0)
            {
                legendRows = 1;
                int x = 0;
                Font f = Theme.F(9);
                foreach (string k in order)
                {
                    int iw = 16 + Armory.TextWidth(Name(k), f) + 14;
                    if (x > 0 && x + iw > w) { legendRows++; x = 0; }
                    x += iw;
                }
            }
            return months.Count * RowH + (legendRows > 0 ? 8 + legendRows * LegendH : 0);
        }

        protected override void PaintContent(Graphics g, Rectangle r)
        {
            Font f = Theme.F(9);
            int barW = r.Width - LabelW - (Only != null ? 120 : 0);
            for (int i = 0; i < months.Count; i++)
            {
                MonthStat m = months[i];
                int top = r.Y + i * RowH;
                Armory.Text(g, m.Month == "unknown" ? "?" : m.Month, f, Armory.Dim, new Rectangle(r.X, top + 2, LabelW, 18),
                            TextFormatFlags.Left | TextFormatFlags.SingleLine);
                var track = new RectangleF(r.X + LabelW, top + 4, barW, RowH - 10);
                Armory.Box(g, track, 4, Armory.Slot, Armory.SlotEdge);
                if (m.Runs <= 0) continue;
                using (var clip = Armory.Round(track, 4))
                {
                    Region old = g.Clip;
                    g.SetClip(clip, CombineMode.Intersect);
                    float x = track.X;
                    foreach (string k in Only != null ? new List<string> { Only } : order)
                    {
                        double n;
                        if (!m.Classes.TryGetValue(k, out n) || n <= 0) continue;
                        float segW = (float)(barW * n / m.Runs);
                        using (var b = new SolidBrush(LauncherApp.ClassColorOf(k))) g.FillRectangle(b, x, track.Y, segW, track.Height);
                        x += segW;
                    }
                    g.Clip = old;
                }
                if (Only != null)
                {
                    double n;
                    m.Classes.TryGetValue(Only, out n);
                    string t = Math.Floor(n) + " / " + Math.Floor(m.Runs) + "   " + Math.Round(100 * n / m.Runs) + "%";
                    Armory.Text(g, t, f, Armory.Bright, new Rectangle(r.Right - 116, top + 2, 116, 18), TextFormatFlags.Right | TextFormatFlags.SingleLine);
                }
            }
            if (legendRows == 0) return;
            int lx = r.X, ly = r.Y + months.Count * RowH + 8;
            foreach (string k in order)
            {
                string n = Name(k);
                int iw = 16 + Armory.TextWidth(n, f) + 14;
                if (lx > r.X && lx + iw > r.Right) { lx = r.X; ly += LegendH; }
                using (var b = new SolidBrush(LauncherApp.ClassColorOf(k))) g.FillEllipse(b, lx, ly + 4, 10, 10);
                Armory.Text(g, n, f, Armory.Bright, new Rectangle(lx + 15, ly + 1, iw - 15, 18), TextFormatFlags.Left | TextFormatFlags.SingleLine);
                lx += iw;
            }
        }
    }
}

namespace AzerothLauncher
{
    // DPS over time: one dot per run (oldest left, newest right) in its class's color, a line through them, the best
    // as a dashed line, and a trend line that shows whether you are getting better
    public class DpsChartBlock : ArmoryBlock
    {
        readonly List<StatRun> runs;                         // the oldest first
        const int ChartH = 150, AxisW = 52;

        public DpsChartBlock(List<StatRun> newestFirst, int max)
        {
            runs = new List<StatRun>();
            foreach (var r in newestFirst) if (r.Dps > 0) runs.Add(r);
            if (runs.Count > max) runs = runs.GetRange(0, max);
            runs.Reverse();
        }

        protected override int ContentHeight(int w) { return runs.Count < 2 ? 24 : ChartH + 22; }

        protected override void PaintContent(Graphics g, Rectangle r)
        {
            if (runs.Count < 2)
            {
                Armory.Text(g, Lang.T("Play a few runs with Azeroth Stats to see your DPS over time."), Theme.F(10, FontStyle.Italic),
                            Armory.Dim, r, TextFormatFlags.Left | TextFormatFlags.WordBreak);
                return;
            }
            double max = 0, best = 0;
            foreach (var x in runs) { max = Math.Max(max, x.Dps); best = Math.Max(best, x.Dps); }
            max = Math.Ceiling(max * 1.1 / 100) * 100;
            var plot = new Rectangle(r.X + AxisW, r.Y + 6, r.Width - AxisW - 6, ChartH - 12);
            Font f = Theme.F(8.5f);
            // grid: four steps
            for (int i = 0; i <= 4; i++)
            {
                float y = plot.Bottom - plot.Height * i / 4f;
                using (var p = new Pen(i == 0 ? Armory.SlotEdge : Armory.Slot, 1)) g.DrawLine(p, plot.X, y, plot.Right, y);
                Armory.Text(g, LauncherApp.Big(max * i / 4), f, Armory.Dim, new Rectangle(r.X, (int)y - 9, AxisW - 6, 18),
                            TextFormatFlags.Right | TextFormatFlags.SingleLine);
            }
            Func<int, PointF> at = delegate(int i)
            {
                return new PointF(plot.X + plot.Width * i / (float)(runs.Count - 1),
                                  plot.Bottom - (float)(plot.Height * runs[i].Dps / Math.Max(1, max)));
            };
            // best so far
            float by = plot.Bottom - (float)(plot.Height * best / Math.Max(1, max));
            using (var p = new Pen(Armory.Legendary, 1)) { p.DashStyle = DashStyle.Dash; g.DrawLine(p, plot.X, by, plot.Right, by); }
            Armory.Text(g, Lang.T("Best of these") + " " + LauncherApp.Big(best), f, Armory.Legendary, new Rectangle(plot.Right - 140, (int)by - 18, 140, 16),
                        TextFormatFlags.Right | TextFormatFlags.SingleLine);
            // the runs
            var pts = new PointF[runs.Count];
            for (int i = 0; i < runs.Count; i++) pts[i] = at(i);
            using (var p = new Pen(Color.FromArgb(150, Theme.GoldDim), 1.5f)) g.DrawLines(p, pts);
            // trend: least squares
            double n = runs.Count, sx = 0, sy = 0, sxy = 0, sxx = 0;
            for (int i = 0; i < runs.Count; i++) { sx += i; sy += runs[i].Dps; sxy += i * runs[i].Dps; sxx += i * i; }
            double slope = (n * sxy - sx * sy) / Math.Max(1e-9, n * sxx - sx * sx), icpt = (sy - slope * sx) / n;
            Func<double, float> ty = delegate(double v) { return plot.Bottom - (float)(plot.Height * Math.Max(0, v) / Math.Max(1, max)); };
            Color trend = slope >= 0 ? Armory.Green : Theme.Bad;
            using (var p = new Pen(Color.FromArgb(200, trend), 2)) g.DrawLine(p, plot.X, ty(icpt), plot.Right, ty(icpt + slope * (n - 1)));
            for (int i = 0; i < runs.Count; i++)
            {
                Color c = StatsColors.Class(runs[i].ClassKey);
                using (var b = new SolidBrush(c)) g.FillEllipse(b, pts[i].X - 3.5f, pts[i].Y - 3.5f, 7, 7);
                if (runs[i].Kind == "victory") using (var p = new Pen(Armory.Bright, 1)) g.DrawEllipse(p, pts[i].X - 4.5f, pts[i].Y - 4.5f, 9, 9);
            }
            double first = icpt, last = icpt + slope * (n - 1);
            string word = Lang.T(slope >= 0 ? "Getting better" : "Dropping off") + ": " + (first > 0 ? (last >= first ? "+" : "") +
                          Math.Round((last - first) / first * 100) + "%" : "") + "  ·  " + Lang.T(runs.Count + " runs, oldest left") +
                          "  ·  " + Lang.T("ringed: victories");
            Armory.Text(g, word, f, trend, new Rectangle(plot.X, r.Y + ChartH + 2, plot.Width, 18), TextFormatFlags.Left | TextFormatFlags.SingleLine);
        }
    }

    // the builds behind your best runs: the class, how it went, the DPS, and every spell taken (icon and rank)
    public class BuildsBlock : ArmoryBlock
    {
        readonly List<StatRun> builds;
        const int RowH = 78, Icon = 30;

        public BuildsBlock(List<StatRun> builds) { this.builds = builds; }

        protected override int ContentHeight(int w) { return builds.Count == 0 ? 24 : builds.Count * (RowH + 8) - 8; }

        protected override void PaintContent(Graphics g, Rectangle r)
        {
            if (builds.Count == 0)
            {
                Armory.Text(g, Lang.T("Your best builds show up here after a DPS high or a victory."), Theme.F(10, FontStyle.Italic),
                            Armory.Dim, r, TextFormatFlags.Left | TextFormatFlags.WordBreak);
                return;
            }
            for (int i = 0; i < builds.Count; i++)
            {
                StatRun b = builds[i];
                var s = new Rectangle(r.X, r.Y + i * (RowH + 8), r.Width, RowH);
                if (!g.VisibleClipBounds.IntersectsWith(s)) continue;
                Armory.Box(g, s, 6, Armory.Slot, Armory.SlotEdge);
                Color cc = StatsColors.Class(b.ClassKey);
                var ci = new Rectangle(s.X + 8, s.Y + 8, 26, 26);
                Image cp = SpellIcons.Class(b.ClassKey, this);
                if (cp != null) Armory.Picture(g, ci, cp, false, true, cc); else Armory.Icon(g, ci, cc, Armory.Initials(b.Class), true);
                string head = b.Class + " · " + Lang.T(b.Kind == "victory" ? "Victory" : b.Result) + (b.Zone.Length > 0 ? " · " + b.Zone : "");
                Armory.Text(g, head, Theme.F(10.5f, FontStyle.Bold), cc, new Rectangle(ci.Right + 8, s.Y + 8, s.Width - 200, 18),
                            TextFormatFlags.Left | TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis);
                Armory.Text(g, (b.Date.Length >= 10 ? b.Date.Substring(0, 10) + " · " : "") + LauncherApp.Clock(b.Time) + " · " + Lang.T("Level " + LauncherApp.Big(b.Level)),
                            Theme.F(8.5f), Armory.Dim, new Rectangle(ci.Right + 8, s.Y + 24, s.Width - 200, 16), TextFormatFlags.Left | TextFormatFlags.SingleLine);
                Armory.Text(g, LauncherApp.Big(b.Dps) + " DPS", Theme.F(12, FontStyle.Bold), Armory.Legendary, new Rectangle(s.Right - 160, s.Y + 8, 150, 20),
                            TextFormatFlags.Right | TextFormatFlags.SingleLine);
                int x = s.X + 10, y = s.Y + 42;
                foreach (var a in b.Abilities)
                {
                    if (x + Icon > s.Right - 8) break;
                    var ir = new Rectangle(x, y, Icon, Icon);
                    Image pic = SpellIcons.Spell(a.Name, this);
                    if (pic != null) Armory.Picture(g, ir, pic, false, false, Armory.SlotEdge);
                    else Armory.Icon(g, ir, Theme.GoldDim, Armory.Initials(a.Name), false);
                    if (a.Picks > 0)
                        Armory.Text(g, LauncherApp.Big(a.Picks), Theme.F(8, FontStyle.Bold), Armory.Bright, new Rectangle(ir.Right - 14, ir.Bottom - 13, 14, 13),
                                    TextFormatFlags.Right | TextFormatFlags.SingleLine);
                    x += Icon + 5;
                }
            }
        }
    }

    public static class StatsColors
    {
        public static Color Class(string key) { return LauncherApp.ClassColorOf(key); }
    }
}
