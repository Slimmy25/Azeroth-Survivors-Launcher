// Controller support (XInput: Xbox pads, and Steam Input on a Steam Deck, which shows its controls as an Xbox pad).
// The mapping decided for the launcher:
//   Start      PLAY
//   A          confirm: a click where the cursor ball is
//   B          back (the launcher's own back: one step back inside a section, then out of it)
//   X          favorite the current spot (the Quick Access star's choice)
//   Y          open favorites: the cursor ball jumps to the Quick Access strip
//   Left stick   moves a glowing blue cursor ball (the Windows pointer is hidden meanwhile); over something clickable the
//                ball grows a little and glows beneath. In a Wiki class article or a Handbook page it scrolls the passage.
//   Right stick  up/down steps through the side tree (one entry per push); left/right closes and opens its branches
// Only while the launcher is the active window: a controller used in the game never reaches it. The ball is the mouse
// pointer itself moved by the stick, so hovering, tooltips and clicks behave exactly as with a mouse.
using System;
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

    // the page-turn animation over the Stats page: the old page slides out as the new one slides in (about a fifth of a
    // second, easing out), then the real page underneath is uncovered
    sealed class StatsSlide : Control
    {
        readonly Bitmap from, to;
        readonly int direction;
        readonly Timer timer = new Timer { Interval = 15 };
        DateTime started;
        const double Duration = 220;

        public StatsSlide(Bitmap from, Bitmap to, int direction)
        {
            this.from = from; this.to = to; this.direction = direction;
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
            int w = Width, shift = (int)(eased * w);
            // forward (1): the new page comes in from the right; back (-1): from the left
            e.Graphics.DrawImageUnscaled(from, -direction * shift, 0);
            e.Graphics.DrawImageUnscaled(to, direction * (w - shift), 0);
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
            return current == "Guide" && controllerTree != null && !controllerTree.IsDisposed && controllerTree.Visible &&
                   guideArticleScroll != null && !guideArticleScroll.IsDisposed && guideArticleScroll.Visible;
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
            bool stats = current == "Stats" && Form.ActiveForm == this && statsScroll != null && !statsScroll.IsDisposed;

            if (stats)
            {
                // Stats: the left stick scrolls the page; the right stick left and right turns to the previous or next page
                if (ly != 0) ScrollWithStick(statsScroll, ly);
                if (ballShown && ly != 0) HideBall();
                int turn = rx > 0.6 ? 1 : rx < -0.6 ? -1 : 0;
                if (turn != sideStickHeld)
                {
                    sideStickHeld = turn;
                    if (turn != 0) TurnStatsPage(turn);
                }
            }

            if (stats) { }                                   // handled above
            else if (reading)
            {
                // left stick: the passage scrolls up and down, faster the further it is pushed
                if (ly != 0) ScrollWithStick(guideArticleScroll, ly);
                if (ballShown && (lx != 0 || ly != 0)) HideBall();
                // right stick left and right: close and open the tree's branches
                int side = rx > 0.6 ? 1 : rx < -0.6 ? -1 : 0;
                if (side != sideStickHeld)
                {
                    sideStickHeld = side;
                    if (side != 0 && controllerTreeFold != null) controllerTreeFold(side > 0);
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
            if ((pressed & XInput.A) != 0) ClickAtCursor();
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

        // Stats: the next (1) or previous (-1) page, sliding in from the right going forward and from the left going back
        StatsSlide statsSlide;

        void TurnStatsPage(int direction)
        {
            int next = statsTab + direction;
            if (next < 0 || next >= StatTabs.Length) return;   // the first and last pages stay put
            if (statsSlide != null && !statsSlide.IsDisposed) statsSlide.Finish();
            Bitmap before = Snapshot(panel);
            SetStatsView(statsClass, next);
            Bitmap after = Snapshot(panel);
            if (before == null || after == null) { if (before != null) before.Dispose(); if (after != null) after.Dispose(); return; }
            statsSlide = new StatsSlide(before, after, direction) { Bounds = new Rectangle(Point.Empty, panel.ClientSize) };
            panel.Controls.Add(statsSlide);
            statsSlide.BringToFront();
            statsSlide.Start();
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

        void StepTree(int direction)
        {
            if (controllerTree == null || controllerTree.IsDisposed || !controllerTree.Visible) return;
            controllerTree.Wheel(direction > 0 ? -120 : 120);   // one wheel notch: one entry, as GuideContentsPanel steps
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
