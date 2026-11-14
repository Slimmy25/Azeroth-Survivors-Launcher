// Controller support (XInput: Xbox pads, and Steam Input on a Steam Deck, which shows its controls as an Xbox pad).
// The mapping decided for the launcher:
//   Start      PLAY
//   A          confirm: a click where the cursor ball is
//   B          back (the launcher's own back: one step back inside a section, then out of it)
//   X          favorite the current spot (the Quick Access star's choice)
//   Y          open favorites: the cursor ball jumps to the Quick Access strip
//   Right stick  steps through the Wiki's link tree (one entry per push, like one wheel notch)
//   Left stick   moves a glowing blue cursor ball; it glows brighter over something that can be clicked
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

    // the glowing blue ball drawn around the pointer: a click-through layered window with soft edges
    sealed class CursorBall : Form
    {
        const int Diameter = 36;
        bool bright;

        public CursorBall()
        {
            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.Manual;
            TopMost = true;
            Width = Height = Diameter;
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

        public void MoveTo(Point screen, bool overClickable)
        {
            if (!IsHandleCreated) CreateHandle();
            if (overClickable != bright || !Visible) { bright = overClickable; Redraw(screen); }
            else
            {
                Point at = new Point(screen.X - Diameter / 2, screen.Y - Diameter / 2);
                SetWindowPos(Handle, (IntPtr)(-1), at.X, at.Y, 0, 0, 0x0001 | 0x0010 | 0x0040);   // NOSIZE | NOACTIVATE | SHOWWINDOW
            }
        }

        void Redraw(Point screen)
        {
            using (var bmp = new Bitmap(Diameter, Diameter, PixelFormat.Format32bppPArgb))
            {
                using (var g = Graphics.FromImage(bmp))
                {
                    g.SmoothingMode = SmoothingMode.AntiAlias;
                    g.Clear(Color.Transparent);
                    using (var path = new GraphicsPath())
                    {
                        path.AddEllipse(1, 1, Diameter - 2, Diameter - 2);
                        using (var glow = new PathGradientBrush(path))
                        {
                            glow.CenterColor = Color.FromArgb(bright ? 235 : 170, 120, 200, 255);
                            glow.SurroundColors = new[] { Color.FromArgb(0, 40, 120, 255) };
                            g.FillPath(glow, path);
                        }
                    }
                    int core = bright ? 12 : 9;
                    using (var b = new SolidBrush(Color.FromArgb(bright ? 255 : 220, 170, 225, 255)))
                        g.FillEllipse(b, (Diameter - core) / 2f, (Diameter - core) / 2f, core, core);
                }
                Present(bmp, new Point(screen.X - Diameter / 2, screen.Y - Diameter / 2));
            }
        }

        void Present(Bitmap bmp, Point at)
        {
            IntPtr screenDc = GetDC(IntPtr.Zero), memDc = CreateCompatibleDC(screenDc), hBitmap = IntPtr.Zero, old = IntPtr.Zero;
            try
            {
                hBitmap = bmp.GetHbitmap(Color.FromArgb(0));
                old = SelectObject(memDc, hBitmap);
                var size = new SIZE { cx = bmp.Width, cy = bmp.Height };
                var source = new POINT { x = 0, y = 0 };
                var top = new POINT { x = at.X, y = at.Y };
                var blend = new BLENDFUNCTION { BlendOp = 0, BlendFlags = 0, SourceConstantAlpha = 255, AlphaFormat = 1 };
                UpdateLayeredWindow(Handle, screenDc, ref top, ref size, memDc, ref source, 0, ref blend, 2);
                if (!Visible) { base.Visible = true; }
                SetWindowPos(Handle, (IntPtr)(-1), at.X, at.Y, 0, 0, 0x0001 | 0x0010 | 0x0040);
            }
            finally
            {
                if (old != IntPtr.Zero) SelectObject(memDc, old);
                if (hBitmap != IntPtr.Zero) DeleteObject(hBitmap);
                DeleteDC(memDc);
                ReleaseDC(IntPtr.Zero, screenDc);
            }
        }

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

    public partial class LauncherApp
    {
        Timer controllerTimer;
        CursorBall cursorBall;
        ushort padButtonsBefore;
        int rightStickHeld;                                   // -1 up, 1 down, 0 centred: one tree step per push
        int rightStickRepeat;
        GuideContentsPanel controllerTree;                    // the Wiki tree on screen (Guide.cs sets it)
        PointF ballExact;
        bool ballShown;

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
                if (cursorBall != null && !cursorBall.IsDisposed) cursorBall.Dispose();
            };
        }

        void ControllerTick()
        {
            XInput.State s;
            bool active = Form.ActiveForm != null && !IsDisposed && Visible && WindowState != FormWindowState.Minimized;
            if (!XInput.Read(out s) || !active) { HideBall(); padButtonsBefore = 0; return; }
            ushort now = s.Pad.Buttons, pressed = (ushort)(now & ~padButtonsBefore);
            padButtonsBefore = now;

            // left stick: the cursor ball
            double lx = Stick(s.Pad.ThumbLX), ly = Stick(s.Pad.ThumbLY);
            if (lx != 0 || ly != 0)
            {
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
            else if (ballShown) ShowBall();                   // keeps the glow right as things change under it

            // right stick: one tree step per push, repeating while held
            double ry = Stick(s.Pad.ThumbRY);
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

        static double Stick(short v)
        {
            if (Math.Abs((int)v) < StickDead) return 0;
            double n = (Math.Abs((int)v) - StickDead) / (32767.0 - StickDead);
            return Math.Sign(v) * Math.Min(1, n);
        }

        void ShowBall()
        {
            if (cursorBall == null || cursorBall.IsDisposed) cursorBall = new CursorBall();
            Point at = Cursor.Position;
            cursorBall.MoveTo(at, ClickableAt(at));
            ballShown = true;
        }

        void HideBall()
        {
            if (!ballShown) return;
            ballShown = false;
            if (cursorBall != null && !cursorBall.IsDisposed) cursorBall.Hide();
        }

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
            controllerTree.Wheel(direction > 0 ? -120 : 120);   // one wheel notch: one section, as GuideContentsPanel steps
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
