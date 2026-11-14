// Controller support (XInput: Xbox pads, and Steam Input on a Steam Deck, which shows its controls as an Xbox pad).
// The mapping decided for the launcher:
//   Start      PLAY
//   A          confirm: a click where the cursor ball is
//   B          back (the launcher's own back: one step back inside a section, then out of it)
//   X          favorite the current spot (the Quick Access star's choice)
//   Y          open favorites: the cursor ball jumps to the Quick Access strip
//   Left stick   moves a glowing blue cursor ball (the Windows pointer is hidden meanwhile); over something clickable the
//                ball grows a little and glows beneath. In a Wiki class article or a Handbook page it scrolls the passage.
//   Right stick  up/down moves a blue highlight through the side tree (A goes there, sliding up or down); left/right
//                closes and opens the highlighted entry's branch (a class's sections, a Handbook topic's pages). Stats works the same way, with its pages in the tree.
// Only while the launcher is the active window: a controller used in the game never reaches it. The ball is the mouse
// pointer itself moved by the stick, so hovering, tooltips and clicks behave exactly as with a mouse.
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace AzerothLauncher
{
    static class XInput
    {
        [StructLayout(LayoutKind.Sequential)]
        public struct Gamepad
        {
            public ushort Buttons;
            public byte LeftTrigger, RightTrigger;
            public short ThumbLX, ThumbLY, ThumbRX, ThumbRY;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct State
        {
            public uint PacketNumber;
            public Gamepad Pad;
        }

        public const ushort DpadUp = 0x0001, DpadDown = 0x0002, DpadLeft = 0x0004, DpadRight = 0x0008,
                            Start = 0x0010, Back = 0x0020, A = 0x1000, B = 0x2000, X = 0x4000, Y = 0x8000;

        [DllImport("xinput1_4.dll", EntryPoint = "XInputGetState")] static extern uint Get14(uint user, out State state);
        [DllImport("xinput9_1_0.dll", EntryPoint = "XInputGetState")] static extern uint Get910(uint user, out State state);

        static int library;                                   // 0 untried, 1 xinput1_4, 2 xinput9_1_0, -1 none

        // the first connected pad's state; false when there is none (or no XInput on this PC)
        public static bool Read(out State state)
        {
            state = new State();
            if (library < 0) return false;
            for (uint user = 0; user < 4; user++)
            {
                uint result;
                try
                {
                    if (library == 0 || library == 1) { result = Get14(user, out state); library = 1; }
                    else result = Get910(user, out state);
                }
                catch (DllNotFoundException)
                {
                    if (library == 1 || library == 0) { library = 2; user--; continue; }
                    library = -1; return false;
                }
                catch (EntryPointNotFoundException) { library = -1; return false; }
                if (result == 0) return true;               // ERROR_SUCCESS: connected
            }
            return false;
        }
    }

    // the glowing blue ball that stands in for the pointer: a click-through layered window with soft edges. Over something
    // clickable it grows a little and a glow spreads out beneath it; it eases between the two so the change reads as a reaction.
    sealed class CursorBall : Form
    {
        const int Box = 76;                                   // the window: room for the grown ball and its glow below
        const float Rest = 22f, Grown = 28f;                  // the ball's diameter at rest and over a clickable
        float grow;                                           // 0 at rest .. 1 fully grown
        bool target;                                          // over a clickable now
        Point at;

        public CursorBall()
        {
            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.Manual;
            TopMost = true;
            Width = Height = Box;
        }

        protected override bool ShowWithoutActivation { get { return true; } }

        protected override CreateParams CreateParams
        {
            get
            {
                CreateParams cp = base.CreateParams;
                cp.ExStyle |= 0x00080000 | 0x00000020 | 0x08000000 | 0x00000080;   // layered, click-through, no activation, no Alt+Tab
                return cp;
            }
        }

        // called every controller tick: follows the pointer and eases the hover reaction
        public void MoveTo(Point screen, bool overClickable)
        {
            if (!IsHandleCreated) CreateHandle();
            target = overClickable;
            float before = grow;
            grow += (target ? 1f : -1f) * 0.2f;               // about 5 ticks (80 ms) to grow or settle
            grow = Math.Max(0f, Math.Min(1f, grow));
            bool moved = screen != at;
            at = screen;
            if (grow != before || !Visible) Redraw();
            else if (moved) SetWindowPos(Handle, (IntPtr)(-1), at.X - Box / 2, at.Y - Box / 2, 0, 0, 0x0001 | 0x0010 | 0x0040);
        }

        void Redraw()
        {
            using (var bmp = new Bitmap(Box, Box, PixelFormat.Format32bppPArgb))
            {
                using (var g = Graphics.FromImage(bmp))
                {
                    g.SmoothingMode = SmoothingMode.AntiAlias;
                    g.Clear(Color.Transparent);
                    float c = Box / 2f;
                    // the glow beneath: a wide soft oval under the ball, only while hovering something clickable
                    if (grow > 0)
                    {
                        float gw = 30 + 30 * grow, gh = 12 + 10 * grow, gy = c + Rest / 2f - 2;
                        using (var path = new GraphicsPath())
                        {
                            path.AddEllipse(c - gw / 2, gy - gh / 2, gw, gh);
                            using (var glow = new PathGradientBrush(path))
                            {
                                glow.CenterColor = Color.FromArgb((int)(190 * grow), 90, 180, 255);
                                glow.SurroundColors = new[] { Color.FromArgb(0, 40, 120, 255) };
                                g.FillPath(glow, path);
                            }
                        }
                    }
                    // the ball's own halo, then its body and a bright core
                    float d = Rest + (Grown - Rest) * grow, halo = d + 14 + 6 * grow;
                    using (var path = new GraphicsPath())
                    {
                        path.AddEllipse(c - halo / 2, c - halo / 2, halo, halo);
                        using (var glow = new PathGradientBrush(path))
                        {
                            glow.CenterColor = Color.FromArgb(150 + (int)(80 * grow), 110, 195, 255);
                            glow.SurroundColors = new[] { Color.FromArgb(0, 40, 120, 255) };
                            g.FillPath(glow, path);
                        }
                    }
                    using (var body = new SolidBrush(Color.FromArgb(200 + (int)(40 * grow), 70, 150, 245)))
                        g.FillEllipse(body, c - d / 2, c - d / 2, d, d);
                    float core = d * 0.45f;
                    using (var b = new SolidBrush(Color.FromArgb(255, 190, 230, 255)))
                        g.FillEllipse(b, c - core / 2, c - core / 2, core, core);
                }
                Present(bmp, new Point(at.X - Box / 2, at.Y - Box / 2));
            }
        }

        void Present(Bitmap bmp, Point topLeft)
        {
            IntPtr screenDc = GetDC(IntPtr.Zero), memDc = CreateCompatibleDC(screenDc), hBitmap = IntPtr.Zero, old = IntPtr.Zero;
            try
            {
                hBitmap = bmp.GetHbitmap(Color.FromArgb(0));
                old = SelectObject(memDc, hBitmap);
                var size = new SIZE { cx = bmp.Width, cy = bmp.Height };
                var source = new POINT { x = 0, y = 0 };
                var top = new POINT { x = topLeft.X, y = topLeft.Y };
                var blend = new BLENDFUNCTION { BlendOp = 0, BlendFlags = 0, SourceConstantAlpha = 255, AlphaFormat = 1 };
                UpdateLayeredWindow(Handle, screenDc, ref top, ref size, memDc, ref source, 0, ref blend, 2);
                if (!Visible) base.Visible = true;
                SetWindowPos(Handle, (IntPtr)(-1), topLeft.X, topLeft.Y, 0, 0, 0x0001 | 0x0010 | 0x0040);
            }
            finally
            {
                if (old != IntPtr.Zero) SelectObject(memDc, old);
                if (hBitmap != IntPtr.Zero) DeleteObject(hBitmap);
                DeleteDC(memDc);
                ReleaseDC(IntPtr.Zero, screenDc);
            }
        }

        public void Rest0() { grow = 0; }

        [StructLayout(LayoutKind.Sequential)] struct POINT { public int x, y; }
        [StructLayout(LayoutKind.Sequential)] struct SIZE { public int cx, cy; }
        [StructLayout(LayoutKind.Sequential, Pack = 1)] struct BLENDFUNCTION { public byte BlendOp, BlendFlags, SourceConstantAlpha, AlphaFormat; }

        [DllImport("user32.dll")] static extern bool UpdateLayeredWindow(IntPtr hwnd, IntPtr hdcDst, ref POINT pptDst, ref SIZE psize, IntPtr hdcSrc,
                                                                         ref POINT pprSrc, int crKey, ref BLENDFUNCTION pblend, int dwFlags);
        [DllImport("user32.dll")] static extern bool SetWindowPos(IntPtr hWnd, IntPtr after, int x, int y, int cx, int cy, uint flags);
        [DllImport("user32.dll")] static extern IntPtr GetDC(IntPtr hWnd);
        [DllImport("user32.dll")] static extern int ReleaseDC(IntPtr hWnd, IntPtr hDC);
        [DllImport("gdi32.dll")] static extern IntPtr CreateCompatibleDC(IntPtr hDC);
        [DllImport("gdi32.dll")] static extern bool DeleteDC(IntPtr hdc);
        [DllImport("gdi32.dll")] static extern IntPtr SelectObject(IntPtr hDC, IntPtr obj);
        [DllImport("gdi32.dll")] static extern bool DeleteObject(IntPtr obj);
    }

    // the page-turn animation (Stats pages, Wiki and Handbook moves): the old view slides out as the new one slides in
    // (about a fifth of a second, easing out), then the real page underneath is uncovered. Vertical: the next page comes
    // up from below (direction 1) or down from above (-1).
    sealed class PageSlide : Control
    {
        readonly Bitmap from, to;
        readonly int direction;
        readonly bool vertical;
        readonly Timer timer = new Timer { Interval = 15 };
        DateTime started;
        const double Duration = 220;

        public PageSlide(Bitmap from, Bitmap to, int direction, bool vertical)
        {
            this.from = from; this.to = to; this.direction = direction; this.vertical = vertical;
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer, true);
            timer.Tick += delegate { if ((DateTime.Now - started).TotalMilliseconds >= Duration) Finish(); else Invalidate(); };
        }

        public void Start() { started = DateTime.Now; timer.Start(); }

        public void Finish()
        {
            timer.Stop();
            if (Parent != null) Parent.Controls.Remove(this);
            Dispose();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            double t = Math.Min(1, (DateTime.Now - started).TotalMilliseconds / Duration);
            double eased = 1 - Math.Pow(1 - t, 3);
            int size = vertical ? Height : Width, shift = (int)(eased * size);
            int fromAt = -direction * shift, toAt = direction * (size - shift);
            if (vertical) { e.Graphics.DrawImageUnscaled(from, 0, fromAt); e.Graphics.DrawImageUnscaled(to, 0, toAt); }
            else { e.Graphics.DrawImageUnscaled(from, fromAt, 0); e.Graphics.DrawImageUnscaled(to, toAt, 0); }
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing) { timer.Dispose(); from.Dispose(); to.Dispose(); }
            base.Dispose(disposing);
        }
    }

    public partial class LauncherApp
    {
        Timer controllerTimer;
        CursorBall cursorBall;
        ushort padButtonsBefore;
        int rightStickHeld, rightStickRepeat, sideStickHeld;   // one tree step / fold per push, repeating while held
        GuideContentsPanel controllerTree;                    // the Wiki or Handbook tree on screen (Guide.cs sets it)
        Action<bool> controllerTreeFold;                      // opens (true) or closes (false) that tree's branches
        PointF ballExact;
        bool ballShown, pointerHidden;
        float scrollCarry;

        const int StickDead = 7800;                           // the stick's resting wobble is ignored

        void StartController()
        {
            if (controllerTimer != null) return;
            controllerTimer = new Timer { Interval = 16 };
            controllerTimer.Tick += delegate { ControllerTick(); };
            controllerTimer.Start();
            FormClosed += delegate
            {
                controllerTimer.Stop();
                ShowPointer();
                if (cursorBall != null && !cursorBall.IsDisposed) cursorBall.Dispose();
            };
        }

        // a Wiki class article or a Handbook page with its side tree: the sticks read it instead of moving the ball
        bool ControllerReading()
        {
            ScrollPanel page = ReadingPage();
            return page != null && controllerTree != null && !controllerTree.IsDisposed && controllerTree.Visible;
        }

        // the page the left stick scrolls: a Wiki or Handbook passage, or a Stats page (both have the side tree)
        ScrollPanel ReadingPage()
        {
            ScrollPanel page = current == "Guide" ? guideArticleScroll : current == "Stats" ? statsScroll : null;
            return page != null && !page.IsDisposed && page.Visible ? page : null;
        }

        void ControllerTick()
        {
            XInput.State s;
            bool active = Form.ActiveForm != null && !IsDisposed && Visible && WindowState != FormWindowState.Minimized;
            if (!XInput.Read(out s) || !active) { HideBall(); padButtonsBefore = 0; return; }
            ushort now = s.Pad.Buttons, pressed = (ushort)(now & ~padButtonsBefore);
            padButtonsBefore = now;
            double lx = Stick(s.Pad.ThumbLX), ly = Stick(s.Pad.ThumbLY), rx = Stick(s.Pad.ThumbRX), ry = Stick(s.Pad.ThumbRY);
            bool reading = ControllerReading() && Form.ActiveForm == this;

            if (reading) ShowTreePick();                     // the tree recolours itself as the passage scrolls: keep the highlight
            else if (treePick != null) ClearTreePick();
            if (reading)
            {
                // left stick: the passage scrolls up and down, faster the further it is pushed
                if (ly != 0) ScrollWithStick(ReadingPage(), ly);
                if (ballShown && (lx != 0 || ly != 0)) HideBall();
                // right stick left and right: close and open the tree's branches
                int side = rx > 0.6 ? 1 : rx < -0.6 ? -1 : 0;
                if (side != sideStickHeld)
                {
                    sideStickHeld = side;
                    if (side != 0 && !FoldTreePick(side > 0) && controllerTreeFold != null) controllerTreeFold(side > 0);
                }
            }
            else if (lx != 0 || ly != 0)
            {
                // left stick: the cursor ball
                if (!ballShown) ballExact = Cursor.Position;
                double speed = 14 * Theme.DisplayScale;       // pixels a tick at full tilt; gentle tilt moves slowly
                ballExact.X += (float)(lx * Math.Abs(lx) * speed);
                ballExact.Y -= (float)(ly * Math.Abs(ly) * speed);
                Rectangle bounds = Screen.FromControl(this).Bounds;
                ballExact.X = Math.Max(bounds.Left, Math.Min(bounds.Right - 1, ballExact.X));
                ballExact.Y = Math.Max(bounds.Top, Math.Min(bounds.Bottom - 1, ballExact.Y));
                Cursor.Position = Point.Round(ballExact);
                ShowBall();
            }
            else if (ballShown)
            {
                if (Cursor.Position != Point.Round(ballExact)) HideBall();   // the mouse moved: the Windows pointer is back
                else ShowBall();                              // keeps the hover reaction right as things change under it
            }

            // right stick up and down: one tree step per push, repeating while held
            int dir = ry > 0.5 ? -1 : ry < -0.5 ? 1 : 0;
            if (dir == 0) { rightStickHeld = 0; rightStickRepeat = 0; }
            else if (dir != rightStickHeld || ++rightStickRepeat > 18)
            {
                if (dir == rightStickHeld) rightStickRepeat = 12;   // held: about 4 steps a second after the first
                rightStickHeld = dir;
                StepTree(dir);
            }

            if ((pressed & XInput.Start) != 0) { if (playBtn != null && playBtn.Enabled) Play(); }
            if ((pressed & XInput.A) != 0) { if (!(reading && ConfirmTreePick())) ClickAtCursor(); }
            if ((pressed & XInput.B) != 0) ControllerBack();
            if ((pressed & XInput.X) != 0) { if (current == "Guide" || favTrail != null) ChooseFavoriteLocation(); }
            if ((pressed & XInput.Y) != 0) JumpToFavorites();
        }

        void ScrollWithStick(ScrollPanel sp, double ly)
        {
            scrollCarry += (float)(-ly * Math.Abs(ly) * 26 * Theme.DisplayScale);
            int step = (int)scrollCarry;
            if (step == 0) return;
            scrollCarry -= step;
            sp.ScrollTo(Math.Max(0, -sp.AutoScrollPosition.Y + step));
        }

        // Stats: the next (1) or previous (-1) page, coming up from below going forward and down from above going back
        PageSlide pageSlide;

        void TurnStatsPage(int direction)
        {
            int next = statsTab + direction;
            if (next < 0 || next >= StatTabs.Length) return;   // the first and last pages stay put
            SlidePanel(direction, delegate { SetStatsView(statsClass, next); }, false);
        }

        // runs change (which rebuilds or moves the page), then slides from the old picture of the page to the new one.
        // queued: the change itself finishes in a later message (it was posted with BeginInvoke), so the new picture is
        // taken after it.
        void SlidePanel(int direction, Action change, bool queued)
        {
            if (pageSlide != null && !pageSlide.IsDisposed) pageSlide.Finish();
            Bitmap before = Snapshot(panel);
            change();
            Action play = delegate
            {
                if (IsDisposed) { if (before != null) before.Dispose(); return; }
                Bitmap after = Snapshot(panel);
                if (before == null || after == null) { if (before != null) before.Dispose(); if (after != null) after.Dispose(); return; }
                pageSlide = new PageSlide(before, after, direction, true) { Bounds = new Rectangle(Point.Empty, panel.ClientSize) };
                panel.Controls.Add(pageSlide);
                pageSlide.BringToFront();
                pageSlide.Start();
            };
            if (queued) BeginInvoke((MethodInvoker)delegate { play(); });
            else play();
        }

        static Bitmap Snapshot(Control c)
        {
            if (c == null || c.IsDisposed || c.ClientSize.Width < 1 || c.ClientSize.Height < 1) return null;
            try
            {
                var bmp = new Bitmap(c.ClientSize.Width, c.ClientSize.Height);
                c.DrawToBitmap(bmp, new Rectangle(Point.Empty, c.ClientSize));
                return bmp;
            }
            catch (Exception) { return null; }
        }

        static double Stick(short v)
        {
            if (Math.Abs((int)v) < StickDead) return 0;
            double n = (Math.Abs((int)v) - StickDead) / (32767.0 - StickDead);
            return Math.Sign(v) * Math.Min(1, n);
        }

        void ShowBall()
        {
            if (cursorBall == null || cursorBall.IsDisposed) cursorBall = new CursorBall();
            if (!ballShown) ballExact = Cursor.Position;
            Point at = Cursor.Position;
            cursorBall.MoveTo(at, ClickableAt(at));
            ballShown = true;
            HidePointer();
        }

        void HideBall()
        {
            ShowPointer();
            if (!ballShown) return;
            ballShown = false;
            if (cursorBall != null && !cursorBall.IsDisposed) { cursorBall.Rest0(); cursorBall.Hide(); }
        }

        // the Windows pointer is hidden while the ball stands in for it, and back as soon as the ball goes (or a mouse moves it)
        void HidePointer() { if (!pointerHidden) { Cursor.Hide(); pointerHidden = true; } }
        void ShowPointer() { if (pointerHidden) { Cursor.Show(); pointerHidden = false; } }

        // something under the pointer that reacts to a click: a button, a link, a hand cursor
        bool ClickableAt(Point screen)
        {
            Control c = ControlAt(screen);
            for (; c != null; c = c.Parent)
            {
                if (c is WowButton || c is ButtonBase || c is LinkLabel || c is CheckBox) return true;
                if (c.Cursor == Cursors.Hand) return true;
                if (c is Form) break;
            }
            return Cursor.Current == Cursors.Hand;
        }

        static Control ControlAt(Point screen)
        {
            IntPtr h = WindowFromPoint(new POINTSTRUCT { X = screen.X, Y = screen.Y });
            return h == IntPtr.Zero ? null : Control.FromChildHandle(h);
        }

        void ClickAtCursor()
        {
            if (!ballShown && ControllerReading()) return;    // reading: no ball on screen, so nothing is aimed at
            if (!ballShown) ShowBall();
            var down = new INPUT { type = 0, mi = new MOUSEINPUT { dwFlags = 0x0002 } };
            var up = new INPUT { type = 0, mi = new MOUSEINPUT { dwFlags = 0x0004 } };
            SendInput(2, new[] { down, up }, Marshal.SizeOf(typeof(INPUT)));
        }

        // B: the launcher's own back; on the home screen with nothing to go back to, it does nothing
        void ControllerBack()
        {
            Form top = Form.ActiveForm;
            if (top != null && top != this) { top.Close(); return; }   // a dialog: B closes it, as Escape does
            if (current != null && current != "Home") GoBack();
        }

        // ---- the side tree's controller selection: a blue highlight the right stick moves; A goes there
        static readonly Color TreePickBack = Color.FromArgb(0x1d, 0x3a, 0x66), TreePickText = Color.FromArgb(0xb8, 0xdc, 0xff);
        Label treePick;                                       // the entry the highlight stands on
        Color treePickBack, treePickText;                     // its own colours, put back when the highlight moves on

        List<Label> TreeEntries()
        {
            var list = new List<Label>();
            foreach (Control c in controllerTree.Controls)
            {
                var l = c as GuideContentsLabel;              // the entries, not their fold arrows
                if (l != null && !l.IsDisposed && l.Visible) list.Add(l);
            }
            list.Sort(delegate(Label a, Label b) { return a.Top.CompareTo(b.Top); });
            return list;
        }

        void StepTree(int direction)
        {
            if (controllerTree == null || controllerTree.IsDisposed || !controllerTree.Visible) return;
            List<Label> entries = TreeEntries();
            if (entries.Count == 0) return;
            int at = treePick != null && !treePick.IsDisposed ? entries.IndexOf(treePick) : -1;
            if (at < 0)
            {
                // the highlight starts where the tree says you are (the open entry), else at the top
                at = entries.FindIndex(delegate(Label l) { return l.ForeColor == Theme.Hex("#fff2b0") && l.BackColor != Color.Transparent; });
                if (at < 0) at = 0;
                else at = Math.Max(0, Math.Min(entries.Count - 1, at + direction));
            }
            else at = Math.Max(0, Math.Min(entries.Count - 1, at + direction));
            PickTreeEntry(entries[at]);
        }

        void PickTreeEntry(Label entry)
        {
            ClearTreePick();
            treePick = entry;
            treePickBack = entry.BackColor;
            treePickText = entry.ForeColor;
            ShowTreePick();
            var tree = entry.Parent as ScrollPanel;
            if (tree != null)
            {
                int offset = -tree.AutoScrollPosition.Y;
                if (entry.Top < 0) tree.ScrollTo(offset + entry.Top - 4);
                else if (entry.Bottom > tree.ClientSize.Height) tree.ScrollTo(offset + entry.Bottom - tree.ClientSize.Height + 4);
            }
        }

        void ShowTreePick()
        {
            if (treePick == null || treePick.IsDisposed) { treePick = null; return; }
            if (treePick.BackColor != TreePickBack) treePick.BackColor = TreePickBack;
            if (treePick.ForeColor != TreePickText) treePick.ForeColor = TreePickText;
        }

        void ClearTreePick()
        {
            if (treePick != null && !treePick.IsDisposed) { treePick.BackColor = treePickBack; treePick.ForeColor = treePickText; }
            treePick = null;
        }

        // right stick right / left on the highlighted entry: open or close that entry's own branch (a class's Spells, Talents
        // and Gear; a Handbook topic's pages), as its arrow would. Left on an entry with nothing open moves up to its parent.
        // False when nothing is highlighted, so the whole tree folds instead.
        bool FoldTreePick(bool open)
        {
            if (treePick == null || treePick.IsDisposed || !treePick.Visible) return false;
            Label entry = treePick;
            Label arrow = null;                                // a separate arrow on the entry's row (Classes, Handbook topics)
            foreach (Control c in controllerTree.Controls)
            {
                var l = c as Label;
                if (l == null || l is GuideContentsLabel || l.IsDisposed || !l.Visible) continue;
                if (Math.Abs(l.Top - entry.Top) <= 2 && (l.Text == "\u25b8" || l.Text == "\u25be")) { arrow = l; break; }
            }
            if (arrow == null && TreeLabelKey(entry) == "Gear") return true;   // Gear's arrow only shows its Items and Codex; it never folds
            string marker = arrow != null ? arrow.Text : entry.Text.StartsWith("\u25b8") ? "\u25b8" : entry.Text.StartsWith("\u25be") ? "\u25be" : "";
            bool isOpen = marker == "\u25be", canOpen = marker == "\u25b8";
            if (open ? canOpen : isOpen)
            {
                string key = TreeLabelKey(entry);
                ClearTreePick();
                InvokeClick(arrow ?? entry);
                // the tree is drawn again: the highlight returns to the same entry once it is
                BeginInvoke((MethodInvoker)delegate
                {
                    BeginInvoke((MethodInvoker)delegate
                    {
                        if (controllerTree == null || controllerTree.IsDisposed) return;
                        Label again = TreeEntries().Find(delegate(Label l) { return TreeLabelKey(l) == key; });
                        if (again != null) PickTreeEntry(again);
                    });
                });
                return true;
            }
            if (!open)
            {
                // nothing open here: up to the entry it sits under
                List<Label> entries = TreeEntries();
                for (int i = entries.IndexOf(entry) - 1; i >= 0; i--)
                    if (entries[i].Left < entry.Left) { PickTreeEntry(entries[i]); return true; }
            }
            return true;                                      // a leaf: nothing to open, and the highlight stays
        }

        static string TreeLabelKey(Label l) { return (l.Text ?? "").TrimStart('\u25b8', '\u25be', ' '); }

        static void InvokeClick(Control c)
        {
            var onClick = typeof(Control).GetMethod("OnClick", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
            if (onClick != null) onClick.Invoke(c, new object[] { EventArgs.Empty });
        }

        // A on a highlighted entry: the same as clicking it
        bool ConfirmTreePick()
        {
            if (treePick == null || treePick.IsDisposed || !treePick.Visible) return false;
            Label entry = treePick;
            // the move slides up when the entry is below where you are, down when it is above
            List<Label> entries = TreeEntries();
            int from = entries.FindIndex(delegate(Label l) { return l != entry && l.BackColor == Theme.Hex("#382914"); });
            int to = entries.IndexOf(entry);
            int direction = from < 0 || to >= from ? 1 : -1;
            ClearTreePick();
            var onClick = typeof(Control).GetMethod("OnClick", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
            if (onClick == null) return true;
            // a Handbook move posts its page change; a Wiki section jump scrolls at once: the queued picture covers both
            SlidePanel(direction, delegate { onClick.Invoke(entry, new object[] { EventArgs.Empty }); }, true);
            return true;
        }

        // Y: the cursor ball goes to the Quick Access strip's first favorite
        void JumpToFavorites()
        {
            foreach (var b in favoriteBtns)
            {
                if (b == null || b.IsDisposed || !b.Visible) continue;
                Point center = b.PointToScreen(new Point(b.Width / 2, b.Height / 2));
                Cursor.Position = center;
                ballExact = center;
                ShowBall();
                return;
            }
        }

        [StructLayout(LayoutKind.Sequential)] struct POINTSTRUCT { public int X, Y; }
        [StructLayout(LayoutKind.Sequential)]
        struct MOUSEINPUT { public int dx, dy; public uint mouseData, dwFlags, time; public IntPtr dwExtraInfo; }
        [StructLayout(LayoutKind.Sequential)]
        struct INPUT { public uint type; public MOUSEINPUT mi; }   // the mouse part is the union's largest member: the size Windows expects

        [DllImport("user32.dll")] static extern IntPtr WindowFromPoint(POINTSTRUCT p);
        [DllImport("user32.dll")] static extern uint SendInput(uint count, INPUT[] inputs, int size);
    }
}
