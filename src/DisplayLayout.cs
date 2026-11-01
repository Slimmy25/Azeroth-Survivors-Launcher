using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace AzerothLauncher
{
    // One source for the relationship between physical pixels, monitor DPI and user density.
    public sealed class DisplayLayout
    {
        public const float WindowAspectRatio = 1100f / 690f;
        public static Rectangle MaximizedWindowBounds(Rectangle workArea, int frameWidth, int frameHeight)
        {
            frameWidth = Math.Max(0, frameWidth);
            frameHeight = Math.Max(0, frameHeight);
            int maxClientWidth = Math.Max(1, workArea.Width - frameWidth);
            int maxClientHeight = Math.Max(1, workArea.Height - frameHeight);
            int clientWidth = Math.Min(maxClientWidth, (int)Math.Floor(maxClientHeight * WindowAspectRatio));
            int clientHeight = Math.Min(maxClientHeight, (int)Math.Floor(clientWidth / WindowAspectRatio));
            int outerWidth = clientWidth + frameWidth, outerHeight = clientHeight + frameHeight;
            return new Rectangle(workArea.Left + (workArea.Width - outerWidth) / 2,
                workArea.Top + (workArea.Height - outerHeight) / 2, outerWidth, outerHeight);
        }
        public readonly Rectangle WorkArea;
        public readonly int Dpi;
        public readonly float Scale, RequestedScale;
        public DisplayLayout(Rectangle workArea, int dpi, float preference)
        {
            WorkArea = workArea;
            Dpi = Math.Max(96, Math.Min(480, dpi));
            float dpiScale = Dpi / 96f;
            float logicalWidth = workArea.Width / dpiScale;
            float automatic = Math.Max(1f, Math.Min(1.75f, (float)Math.Sqrt(logicalWidth / 1920f)));
            RequestedScale = dpiScale * (preference > 0 ? Math.Max(.8f, Math.Min(1.6f, preference)) : automatic);
            // Preserve room for the fixed class/picker/save actions on very short displays.
            // Larger content scrolls; density itself never makes those actions impossible to reach.
            Scale = Math.Min(RequestedScale, Math.Max(.8f, Math.Min(workArea.Width/650f,workArea.Height/360f)));
        }
        public int Px(float value) { return Math.Max(1, (int)Math.Round(value * Scale)); }
        public Size InitialClientSize
        {
            get
            {
                int availableWidth = Math.Max(1, WorkArea.Width - Px(24));
                int availableHeight = Math.Max(1, WorkArea.Height - Px(65));
                int width = Math.Min(Px(1100), availableWidth);
                int height = (int)Math.Round(width / WindowAspectRatio);
                if (height > Math.Min(Px(690), availableHeight))
                {
                    height = Math.Min(Px(690), availableHeight);
                    width = (int)Math.Round(height * WindowAspectRatio);
                }
                return new Size(Math.Max(1, width), Math.Max(1, height));
            }
        }
        public Rectangle Clamp(Rectangle rectangle)
        {
            rectangle.Width = Math.Min(rectangle.Width, WorkArea.Width);
            rectangle.Height = Math.Min(rectangle.Height, WorkArea.Height);
            rectangle.X = Math.Max(WorkArea.Left, Math.Min(WorkArea.Right - rectangle.Width, rectangle.X));
            rectangle.Y = Math.Max(WorkArea.Top, Math.Min(WorkArea.Bottom - rectangle.Height, rectangle.Y));
            return rectangle;
        }
    }

    internal static class NativeDisplay
    {
        [DllImport("user32.dll")] static extern bool SetProcessDpiAwarenessContext(IntPtr value);
        [DllImport("shcore.dll")] static extern int SetProcessDpiAwareness(int value);
        [DllImport("user32.dll")] static extern bool SetProcessDPIAware();
        [DllImport("user32.dll")] static extern uint GetDpiForWindow(IntPtr window);
        [DllImport("user32.dll")] static extern IntPtr GetThreadDpiAwarenessContext();
        [DllImport("user32.dll")] static extern bool AreDpiAwarenessContextsEqual(IntPtr first,IntPtr second);
        public static string Awareness
        {
            get {try{return AreDpiAwarenessContextsEqual(GetThreadDpiAwarenessContext(),new IntPtr(-4))?"PerMonitorV2":"fallback";}catch(EntryPointNotFoundException){return "legacy fallback";}}
        }
        public static void Enable()
        {
            try { if (SetProcessDpiAwarenessContext(new IntPtr(-4))) return; } catch (EntryPointNotFoundException) { }
            try { SetProcessDpiAwareness(2); return; } catch (DllNotFoundException) { } catch (EntryPointNotFoundException) { }
            try { SetProcessDPIAware(); } catch (EntryPointNotFoundException) { }
        }
        public static int Dpi(Control window)
        {
            try { if (window != null && window.IsHandleCreated) return (int)GetDpiForWindow(window.Handle); } catch (EntryPointNotFoundException) { }
            using (var graphics = Graphics.FromHwnd(IntPtr.Zero)) return (int)Math.Round(graphics.DpiX);
        }
    }

    public partial class LauncherApp
    {
        protected override bool ShowWithoutActivation { get { return PreviewMode || base.ShowWithoutActivation; } }
        DisplayLayout displayLayout;
        bool displayReady, changingDisplay;
        Timer displayResizeTimer;
        Size lastPanelLayoutClientSize;
        int U(float value) { return displayLayout == null ? (int)Math.Round(value) : displayLayout.Px(value); }
        bool CompactDisplay { get { return W < U(980); } }
        int HeaderBottom { get { return CompactDisplay ? Math.Max(50, Math.Min(U(80), (int)(H * .15f))) : Math.Max(U(74), Math.Min(U(196), (int)(H * .26f))); } }
        int FooterTop { get { return H - Math.Min(U(108), (int)(H * .17f)); } }
        int NavHeight { get { return CompactDisplay ? 0 : U(118); } }
        Rectangle ResponsiveSideRect
        {
            get
            {
                int bottom = CompactDisplay ? FooterTop - NavHeight - U(4) : FooterTop - U(4);
                int width = CompactDisplay ? W - U(12) : (int)(W * .67f) - U(10);
                return new Rectangle(U(6), HeaderBottom, width, Math.Max(100, bottom - HeaderBottom));
            }
        }
        Rectangle ResponsiveNewsRect
        {
            get
            {
                int x = ResponsiveSideRect.Right + U(16);
                return new Rectangle(x, HeaderBottom + U(38), Math.Max(U(150), W - x - U(20)),
                    Math.Max(U(70), FooterTop - NavHeight - U(30) - HeaderBottom - U(38)));
            }
        }

        float ReadInterfaceScale()
        {
            float value;
            if (!float.TryParse(Cfg.Str("interface_scale"), System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out value) || float.IsNaN(value) || float.IsInfinity(value)) return 0;
            if(value>10) value/=100f; // migrate percentage values from earlier experimental builds
            return value>=.8f&&value<=1.6f?value:0;
        }

        void InitializeDisplay()
        {
            var screen = Screen.FromPoint(Cursor.Position);
            float preference = ReadInterfaceScale();
            displayLayout = new DisplayLayout(screen.WorkingArea, NativeDisplay.Dpi(null), preference);
            Theme.DisplayScale = displayLayout.Scale;
            Theme.DisplayDpi = displayLayout.Dpi;
            Theme.Zoom = Theme.DisplayScale;
            AutoScaleMode = AutoScaleMode.None;
        }

        void EnableResponsiveDisplay()
        {
            FormBorderStyle = FormBorderStyle.Sizable;
            MaximizeBox = true;
            MinimumSize = MinimumWindowSize();
            displayReady = true;
            lastPanelLayoutClientSize = ClientSize;
            LayoutResponsiveDisplay(false);
            displayResizeTimer = new Timer { Interval = 130 };
            displayResizeTimer.Tick += delegate
            {
                displayResizeTimer.Stop();
                if (!Visible || WindowState == FormWindowState.Minimized) return;
                Screen screen = Screen.FromControl(this);
                Rectangle workArea = screen.WorkingArea;
                int dpi = NativeDisplay.Dpi(this);
                if (!PreviewMode && (workArea != displayLayout.WorkArea || dpi != displayLayout.Dpi))
                {
                    ChangeDisplay(workArea, dpi, true);
                    return;
                }

                // Moving the window within the same display does not change the panel layout.
                // Rebuild only after its client size changed; minimize/restore alone needs repaint,
                // not a destructive view reconstruction.
                if (ClientSize != lastPanelLayoutClientSize) LayoutResponsiveDisplay(true);
            };
            Resize += delegate
            {
                if (changingDisplay || WindowState == FormWindowState.Minimized) return;
                LayoutResponsiveDisplay(false);
                displayResizeTimer.Stop(); displayResizeTimer.Start();
            };
            Move += delegate { if (!changingDisplay && !PreviewMode) { displayResizeTimer.Stop(); displayResizeTimer.Start(); } };
            Shown += delegate
            {
                if (!PreviewMode) ChangeDisplay(Screen.FromControl(this).WorkingArea, NativeDisplay.Dpi(this), true);
            };
            StartPosition = FormStartPosition.Manual;
            Bounds = displayLayout.Clamp(new Rectangle(displayLayout.WorkArea.Left + (displayLayout.WorkArea.Width-Width)/2,
                displayLayout.WorkArea.Top + (displayLayout.WorkArea.Height-Height)/2, Width, Height));
            FormClosed += delegate { displayResizeTimer.Dispose(); };
        }

        Size MinimumWindowSize()
        {
            // Keep a small but usable minimum while preserving the same design ratio as
            // the normal window. MinimumSize includes native title-bar/frame dimensions.
            int frameWidth = Math.Max(0, Width - ClientSize.Width);
            int frameHeight = Math.Max(0, Height - ClientSize.Height);
            int clientWidth = Math.Min(U(760), Math.Max(1, displayLayout.WorkArea.Width - frameWidth));
            int clientHeight = (int)Math.Round(clientWidth / DisplayLayout.WindowAspectRatio);
            int maxClientHeight = Math.Max(1, displayLayout.WorkArea.Height - frameHeight);
            if (clientHeight > maxClientHeight)
            {
                clientHeight = maxClientHeight;
                clientWidth = Math.Max(1, (int)Math.Round(clientHeight * DisplayLayout.WindowAspectRatio));
            }
            return new Size(clientWidth + frameWidth, clientHeight + frameHeight);
        }

        void LayoutResponsiveDisplay(bool rebuild)
        {
            if (!displayReady || changingDisplay || W < 100 || H < 100) return;
            changingDisplay = true;
            try
            {
                GuideHover.Hide(); HideLostHelpInline();
                sideHead.HideTransientUi();
                Theme.Zoom = Theme.DisplayScale;
                LayoutNav();
                PlaceSide();
                sideHead.Height = U(38);
                strip.Width = U(32);
                LayoutStrip();
                supportHeart.Bounds = new Rectangle(ResponsiveNewsRect.X, HeaderBottom + U(5), U(26), U(22));
                discordIcon.Bounds = new Rectangle(supportHeart.Right + U(3), supportHeart.Top, U(26), U(22));
                playBtn.SetDisplayZoom(Theme.DisplayScale);
                int playHeight = Math.Max(24, Math.Min(U(49), H - FooterTop - U(32)));
                playBtn.Bounds = new Rectangle(W - U(123), H - U(8) - playHeight, U(88), playHeight);
                var change = nav["Changelog"];
                change.SetDisplayZoom(Theme.DisplayScale);
                change.Bounds = new Rectangle(W - U(126), HeaderBottom + U(5), U(110), U(24));
                supportHeart.Visible = discordIcon.Visible = !CompactDisplay;
                change.Visible = !CompactDisplay;
                if (rebuild && edgeHost != null && !edgeHost.IsDisposed)
                {
                    // A live video is reparented only once. Resize its existing host instead of restarting Edge.
                    edgeHost.Bounds=new Rectangle(0,0,PW,PH); FitEdge(); PlaceVideoClose();
                }
                else if(rebuild && current=="Guide" && gVideo!=null)
                {
                    // The embedded HTML player keeps its playhead while only its enclosing viewport changes.
                    LayoutGuideVideo();
                }
                else if (rebuild && current != null)
                {
                    CaptureDisplayedScrollPositions();
                    int[] positions = displayedView == null ? null : displayedView.ScrollPositions;
                    var overlays=new System.Collections.Generic.List<Control>();
                    foreach(Control control in panel.Controls) if((control.Tag as string)=="theory-code-overlay") overlays.Add(control);
                    foreach(Control control in overlays) panel.Controls.Remove(control);
                    BuildPanel(current);
                    foreach(Control control in overlays)
                    {
                        panel.Controls.Add(control); control.Bounds=new Rectangle(8,8,PW-16,PH-16); control.BringToFront();
                    }
                    RestoreDisplayedScrollPositions(positions);
                }
                if (rebuild) lastPanelLayoutClientSize = ClientSize;
                Invalidate(true);
            }
            finally { Theme.Zoom = Theme.DisplayScale; changingDisplay = false; }
        }

        void ChangeDisplay(Rectangle workArea, int dpi, bool resize, Point? suggestedOrigin = null)
        {
            float oldScale = displayLayout.Scale;
            Rectangle previousBounds = Bounds;
            DisplayLayout next = new DisplayLayout(workArea, dpi, ReadInterfaceScale());
            bool changed = next.Scale != oldScale || dpi != displayLayout.Dpi || workArea != displayLayout.WorkArea ||
                           (suggestedOrigin.HasValue && suggestedOrigin.Value != previousBounds.Location);
            if (!changed) return;
            changingDisplay = true;
            try
            {
                displayLayout = next;
                Theme.DisplayScale = next.Scale; Theme.DisplayDpi = next.Dpi; Theme.Zoom = next.Scale;
                MinimumSize = MinimumWindowSize();
                if (WindowState == FormWindowState.Normal)
                {
                    // Setting MinimumSize can resize the native window immediately. Scale the
                    // captured bounds, not that already-grown size, or a DPI increase scales twice.
                    Size size = resize ? new Size((int)Math.Round(previousBounds.Width*next.Scale/oldScale), (int)Math.Round(previousBounds.Height*next.Scale/oldScale)) : previousBounds.Size;
                    Bounds = next.Clamp(new Rectangle(suggestedOrigin ?? previousBounds.Location, size));
                }
            }
            finally { changingDisplay=false; }
            LayoutResponsiveDisplay(true);
        }

        void SetInterfaceScale(float preference)
        {
            Cfg.Set("interface_scale", preference.ToString(System.Globalization.CultureInfo.InvariantCulture));
            Cfg.Save();
            float previousScale=Theme.DisplayScale;
            ChangeDisplay(displayLayout.WorkArea, displayLayout.Dpi, true);
            if(previousScale==Theme.DisplayScale) LayoutResponsiveDisplay(true);
        }

        internal static Rectangle ParseSuggestedDisplayBounds(IntPtr nativeRect)
        {
            // A Win32 RECT contains four coordinates, not X/Y/Width/Height.
            return Rectangle.FromLTRB(Marshal.ReadInt32(nativeRect), Marshal.ReadInt32(nativeRect, 4),
                                      Marshal.ReadInt32(nativeRect, 8), Marshal.ReadInt32(nativeRect, 12));
        }

        protected override void WndProc(ref Message message)
        {
            if (message.Msg == 0x0214 && displayReady) // WM_SIZING: resize around the design ratio
            {
                ConstrainSizingRectangle(message.WParam.ToInt32(), message.LParam);
                message.Result = new IntPtr(1);
                return;
            }
            if (message.Msg == 0x0024 && displayReady) // WM_GETMINMAXINFO: ratio-fit the maximized window
            {
                base.WndProc(ref message);
                ConstrainMaximizedWindow(message.LParam, message.HWnd);
                return;
            }
            if (message.Msg == 0x02E0 && displayReady && !PreviewMode) // WM_DPICHANGED
            {
                int dpi = message.WParam.ToInt32() & 0xffff;
                Rectangle suggested = ParseSuggestedDisplayBounds(message.LParam);
                // A crossing window may still begin on the old monitor. Select by the
                // complete suggested rectangle, and keep its origin while applying our density.
                Rectangle work = Screen.FromRectangle(suggested).WorkingArea;
                ChangeDisplay(work, dpi, true, suggested.Location);
                message.Result=IntPtr.Zero;
                return; // AutoScaleMode.None: never let a second scaler resize native child controls.
            }
            base.WndProc(ref message);
        }

        void ConstrainSizingRectangle(int edge, IntPtr pointer)
        {
            if (pointer == IntPtr.Zero || IsDisposed) return;
            // WM_SIZING supplies an outer-window RECT. Derive its nonclient frame from
            // the live form, then constrain the client area (where the launcher is laid out).
            int frameWidth = Math.Max(0, Width - ClientSize.Width);
            int frameHeight = Math.Max(0, Height - ClientSize.Height);
            Rectangle r = ParseSuggestedDisplayBounds(pointer);
            int minWidth = Math.Max(MinimumSize.Width, 1);
            int minHeight = Math.Max(MinimumSize.Height, 1);
            bool horizontalEdge = edge == 1 || edge == 2 || edge == 4 || edge == 5 || edge == 7 || edge == 8;
            bool verticalEdge = edge == 3 || edge == 4 || edge == 5 || edge == 6 || edge == 7 || edge == 8;
            bool corner = horizontalEdge && verticalEdge;
            int width = Math.Max(minWidth, r.Width);
            int height = Math.Max(minHeight, r.Height);
            double aspect = DisplayLayout.WindowAspectRatio;
            double byWidth = (width - frameWidth) / aspect + frameHeight;
            double byHeight = (height - frameHeight) * aspect + frameWidth;
            bool widthDrives = !verticalEdge || (corner && Math.Abs(byWidth - height) <= Math.Abs(byHeight - width));
            if (widthDrives) height = Math.Max(minHeight, (int)Math.Round(byWidth));
            else width = Math.Max(minWidth, (int)Math.Round(byHeight));

            int left = r.Left, right = r.Right, top = r.Top, bottom = r.Bottom;
            if (edge == 1 || edge == 4 || edge == 7) left = right - width;
            else if (edge == 2 || edge == 5 || edge == 8) right = left + width;
            else { int centerX = left + r.Width / 2; left = centerX - width / 2; right = left + width; }
            if (edge == 3 || edge == 4 || edge == 5) top = bottom - height;
            else if (edge == 6 || edge == 7 || edge == 8) bottom = top + height;
            else { int centerY = top + r.Height / 2; top = centerY - height / 2; bottom = top + height; }
            r = Rectangle.FromLTRB(left, top, right, bottom);
            Marshal.WriteInt32(pointer, r.Left);
            Marshal.WriteInt32(pointer, 4, r.Top);
            Marshal.WriteInt32(pointer, 8, r.Right);
            Marshal.WriteInt32(pointer, 12, r.Bottom);
        }

        void ConstrainMaximizedWindow(IntPtr pointer, IntPtr window)
        {
            if (pointer == IntPtr.Zero || IsDisposed) return;
            Screen screen;
            try { screen = Screen.FromHandle(window); }
            catch (InvalidOperationException) { return; }
            Rectangle work = screen.WorkingArea, monitor = screen.Bounds;
            int frameWidth = Math.Max(0, Width - ClientSize.Width);
            int frameHeight = Math.Max(0, Height - ClientSize.Height);
            Rectangle bounds = DisplayLayout.MaximizedWindowBounds(work, frameWidth, frameHeight);
            Marshal.WriteInt32(pointer, 8, bounds.Width);    // MINMAXINFO.ptMaxSize
            Marshal.WriteInt32(pointer, 12, bounds.Height);
            Marshal.WriteInt32(pointer, 16, bounds.Left - monitor.Left); // ptMaxPosition is monitor-relative
            Marshal.WriteInt32(pointer, 20, bounds.Top - monitor.Top);
        }

        void DrawResponsiveLauncher(Graphics graphics)
        {
            if (W < 1 || H < 1) return;
            float oldZoom = Theme.Zoom;
            Theme.Zoom = Theme.DisplayScale;
            try
            {
                Theme.Smooth(graphics);
                graphics.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;
                Image background = Retro.Get("background");
                if (background == null) graphics.Clear(Theme.BgBottom);
                else
                {
                    // Reuse artwork, not its baked-in coordinates/Play frame. Text and controls stay native resolution.
                    int stone = Math.Min(HeaderBottom / 3, U(55));
                    graphics.DrawImage(background, new Rectangle(0, 0, W, stone), new Rectangle(0, 0, 1100, 55), GraphicsUnit.Pixel);
                    graphics.DrawImage(background, new Rectangle(0, stone, W, Math.Max(1, FooterTop-stone)), new Rectangle(0, 60, 680, 515), GraphicsUnit.Pixel);
                    graphics.DrawImage(background, new Rectangle(0, FooterTop, W, H-FooterTop), new Rectangle(0, 582, 900, 108), GraphicsUnit.Pixel);
                    using (var pen = new Pen(Theme.GoldDim, U(2))) graphics.DrawLine(pen, 0, FooterTop, W, FooterTop);
                }
                int titleWidth = CompactDisplay ? W - U(140) : ResponsiveSideRect.Right;
                float titleSize = Math.Min(U(48), HeaderBottom * (CompactDisplay ? .34f : .31f));
                using (var path = new GraphicsPath())
                {
                    path.AddString("Azeroth Survivors", Theme.TitleFamily(), (int)FontStyle.Bold, titleSize,
                        new PointF(U(18), HeaderBottom * (CompactDisplay ? .10f : .40f)), StringFormat.GenericDefault);
                    RectangleF bounds = path.GetBounds();
                    if (bounds.Width > titleWidth - U(150))
                        using (var transform = new Matrix())
                        {
                            float scale = (titleWidth - U(150)) / bounds.Width;
                            transform.Translate(bounds.X, bounds.Y);
                            transform.Scale(scale, scale);
                            transform.Translate(-bounds.X, -bounds.Y);
                            path.Transform(transform); bounds = path.GetBounds();
                        }
                    using (var shadow = new Pen(Color.FromArgb(150, Color.Black), U(4))) graphics.DrawPath(shadow, path);
                    if (bounds.Height > 0)
                        using (var fill = new LinearGradientBrush(bounds, Theme.Hex("#fff1b8"), Theme.Hex("#c9821a"), 90f)) graphics.FillPath(fill, path);
                    if (Theme.Logo != null && !CompactDisplay)
                    {
                        int size = Math.Min(U(100), HeaderBottom - U(30));
                        Theme.Draw(graphics, Theme.Logo, new Rectangle((int)bounds.Right + U(12), Math.Max(U(8), (int)bounds.Top - U(8)), size, size));
                    }
                    if (!CompactDisplay) using (var gold = new SolidBrush(Theme.Gold))
                        graphics.DrawString(Lang.T("A Game Made By Aethus"), Theme.F(11, FontStyle.Italic), gold, bounds.Left + U(70), Math.Min(HeaderBottom - U(23), bounds.Bottom + U(8)));
                }
                using (var gold = new SolidBrush(Theme.Gold))
                {
                    if (!CompactDisplay)
                    {
                        graphics.DrawString(Lang.T("News"), Theme.F(11), gold, discordIcon.Right + U(6), HeaderBottom + U(6));
                        graphics.DrawString(Lang.T("Launcher"), Theme.F(11), gold, ResponsiveNewsRect.Left, FooterTop - NavHeight - U(3));
                        // Divider under the heart/Discord/News and Changelog header row.
                        // Keep it just above the scrolling patch-note area at every scale.
                        using (var divider = new Pen(Theme.GoldDim, Math.Max(1, U(1))))
                            graphics.DrawLine(divider, ResponsiveNewsRect.Left, HeaderBottom + U(34),
                                              ResponsiveNewsRect.Right, HeaderBottom + U(34));
                    }
                    graphics.DrawString(Lang.T("Game version") + ": " + Data.GameVersion, Theme.F(9), gold, U(10), H - U(23));
                }
                if (!CompactDisplay && Data.Changelog.Count > 0)
                {
                    string[] lines = Data.Changelog[0].Trim().Split('\n');
                    var state = graphics.Save(); graphics.SetClip(NewsArea);
                    float y = NewsArea.Top - newsScroll;
                    using (var gold = new SolidBrush(Theme.Gold))
                    using (var text = new SolidBrush(Theme.Text))
                    {
                        Font titleFont = Theme.F(11, FontStyle.Bold), bodyFont = Theme.F(9);
                        graphics.DrawString(Lang.T("Patch") + " " + lines[0].Trim(), titleFont, gold, NewsArea.Left, y);
                        y += titleFont.GetHeight(graphics) + U(5);
                        for (int i = 1; i < lines.Length; i++)
                        {
                            string line = lines[i].Trim();
                            if (line.Length == 0 || line.Trim('-', '=', '_').Length == 0) continue;
                            if (line.StartsWith("- ") || line.StartsWith("* ")) line = "• " + line.Substring(2);
                            SizeF size = graphics.MeasureString(line, bodyFont, NewsTextW);
                            graphics.DrawString(line, bodyFont, text, new RectangleF(NewsArea.Left, y, NewsTextW, size.Height + 1));
                            y += size.Height + U(2);
                        }
                        newsHeight = (int)(y + newsScroll - NewsArea.Top) + U(4);
                    }
                    graphics.Restore(state);
                    Rectangle thumb = NewsThumb();
                    if (!thumb.IsEmpty) using (var fill = new SolidBrush(Theme.GoldDim)) graphics.FillRectangle(fill, thumb);
                }
                if (!CompactDisplay) WeeklyRecap(graphics);
                if (dlActive && dlTotal > 0)
                {
                    var bar = new Rectangle(U(16), FooterTop+U(8), Math.Max(20, W/3-U(32)), U(10));
                    using (var track = new SolidBrush(Theme.Panel)) graphics.FillRectangle(track, bar);
                    using (var fill = new SolidBrush(Theme.GoldDim)) graphics.FillRectangle(fill, bar.X, bar.Y, (int)(bar.Width*Math.Min(1, dlDone/(double)dlTotal)), bar.Height);
                }
                string message = Lang.T(setupOpen ? "First Time Setup" : status=="Ready."&&!ClientPresentPaint()
                    ? (RequireLauncherInstall ? "Game client not found. Press INSTALL to download it first." : "Game client not found. Press INSTALL, or set the game folder in Options.") : status);
                if (dlActive) message = dlTotal > 0
                    ? Lang.T("Downloading the game") + ": " + Math.Min(100, dlDone * 100 / dlTotal) + "% (" + Util.FmtSize(dlDone) + " / " + Util.FmtSize(dlTotal) + ")"
                    : Lang.T("Downloading the game") + ": " + Util.FmtSize(dlDone);
                using (var color = new SolidBrush(Theme.Hex("#fff2b0")))
                using (var format = new StringFormat { Alignment = StringAlignment.Far, Trimming = StringTrimming.EllipsisWord, FormatFlags=StringFormatFlags.NoWrap })
                    graphics.DrawString(message, Theme.F(9, FontStyle.Italic), color,
                        new RectangleF(dlActive?W/3+U(16):U(12), FooterTop + U(5), dlActive?W-W/3-U(28):W-U(24), U(25)), format);
            }
            finally { Theme.Zoom = oldZoom; }
        }
    }
}
