using System;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace NunMassacreCoop.Setup
{
    /// <summary>
    /// The picture at the top, as a worn tape plays it: the art blown up pixel for pixel, snow, scanlines, now and then a
    /// tracking band rolling down, a burst of static when the picture changes, and the deck's PLAY and counter in the
    /// corners. It is also the window's title bar: drag it to move the window; the corner marks close and minimise.
    /// </summary>
    internal sealed class VhsScreen : Control
    {
        private Bitmap picture, screen;
        private Bitmap[] snow, burst;
        private readonly Stopwatch clock = Stopwatch.StartNew();
        private readonly Random random = new Random();
        private long burstUntil;
        private int frame, hover;
        private float scale = 1f;
        private Font osd;
        private Rectangle closeBox, minimiseBox;
        public event EventHandler CloseClicked, MinimiseClicked;

        public VhsScreen()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            BackColor = Color.Black;
        }

        public void Build(float scale)
        {
            this.scale = scale;
            Refont();
            int cell = Math.Max(2, (int)Math.Round(2 * scale));
            snow = Art.Snow(Size, 6, 46, cell, 11);
            burst = Art.Snow(Size, 3, 255, cell * 2, 12);
            screen = Art.Screen(Size, scale);
            int box = (int)(30 * scale);
            closeBox = new Rectangle(Width - box - (int)(10 * scale), (int)(8 * scale), box, box);
            minimiseBox = new Rectangle(closeBox.X - box - (int)(4 * scale), closeBox.Y, box, box);
        }

        /// <summary>The deck's lettering in the game's own font, once it is found.</summary>
        public void Refont()
        {
            var old = osd;
            osd = GameFonts.UiFont(20f * scale, true);
            if (old != null) old.Dispose();
            Invalidate();
        }

        /// <summary>A new picture (low resolution, lettered): it comes in after a burst of static, like a channel change.</summary>
        public void Show(Bitmap lowResolution)
        {
            var old = picture;
            picture = Art.Fit(lowResolution, Size, Art.KeepBottom);
            if (old != null) old.Dispose();
            Static(450);
        }

        public void Static(int milliseconds) { burstUntil = clock.ElapsedMilliseconds + milliseconds; }

        public void Tick() { frame++; Invalidate(); }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.InterpolationMode = InterpolationMode.NearestNeighbor;
            g.PixelOffsetMode = PixelOffsetMode.Half;
            g.CompositingQuality = CompositingQuality.HighSpeed;
            long now = clock.ElapsedMilliseconds;
            var whole = new Rectangle(0, 0, Width, Height);
            int jitter = random.Next(100) < 5 ? (random.Next(2) * 2 - 1) * Math.Max(1, (int)scale) : 0;
            if (picture != null) g.DrawImage(picture, new Rectangle(jitter, 0, Width, Height), 0, 0, Width, Height, GraphicsUnit.Pixel);
            else g.Clear(Color.Black);
            if (snow != null)
            {
                // Tracking: now and then a band rolls down the picture, torn sideways and full of snow.
                long cycle = now % 8000;
                if (picture != null && cycle < 1300)
                {
                    int band = (int)(26 * scale), y = (int)(cycle / 1300f * (Height + band)) - band, shift = (int)(7 * scale);
                    var strip = Rectangle.Intersect(new Rectangle(0, y, Width, band), whole);
                    if (strip.Height > 0)
                    {
                        g.DrawImage(picture, new Rectangle(shift, strip.Y, Width, strip.Height), 0, strip.Y, Width, strip.Height, GraphicsUnit.Pixel);
                        g.DrawImage(snow[(frame + 3) % snow.Length], strip, strip, GraphicsUnit.Pixel);
                        g.DrawImage(snow[(frame + 1) % snow.Length], strip, strip, GraphicsUnit.Pixel);
                    }
                }
                g.DrawImage(snow[frame % snow.Length], whole, whole, GraphicsUnit.Pixel);
                if (now < burstUntil || picture == null) g.DrawImage(burst[frame % burst.Length], whole, whole, GraphicsUnit.Pixel);
                g.DrawImage(screen, whole, whole, GraphicsUnit.Pixel);
            }
            if (osd == null) return;
            g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
            int margin = (int)(18 * scale);
            Osd(g, "PLAY ►", new Point(margin, margin), Art.Cream, false);
            var t = TimeSpan.FromMilliseconds(now);
            // Left: the title's lettering is on the right.
            Osd(g, string.Format("SP {0}:{1:00}:{2:00}", (int)t.TotalHours, t.Minutes, t.Seconds),
                new Point(margin, Height - margin - osd.Height), Art.Cream, false);
            Osd(g, "—",
                new Point(minimiseBox.X + minimiseBox.Width / 2, minimiseBox.Y + (minimiseBox.Height - osd.Height) / 2),
                hover == 2 ? Art.Red : Art.Cream, false, true);
            Osd(g, "X", new Point(closeBox.X + closeBox.Width / 2, closeBox.Y + (closeBox.Height - osd.Height) / 2),
                hover == 1 ? Art.Red : Art.Cream, false, true);
        }

        private void Osd(Graphics g, string text, Point at, Color colour, bool alignRight, bool centred = false)
        {
            var size = g.MeasureString(text, osd, PointF.Empty, StringFormat.GenericTypographic);
            float x = centred ? at.X - size.Width / 2 : alignRight ? at.X - size.Width : at.X;
            float shadow = Math.Max(1f, 2f * scale);
            using (var dark = new SolidBrush(Color.FromArgb(200, 0, 0, 0)))
                g.DrawString(text, osd, dark, x + shadow, at.Y + shadow, StringFormat.GenericTypographic);
            using (var fringe = new SolidBrush(Color.FromArgb(90, 80, 140, 255))) g.DrawString(text, osd, fringe, x - 1, at.Y, StringFormat.GenericTypographic);
            using (var brush = new SolidBrush(colour)) g.DrawString(text, osd, brush, x, at.Y, StringFormat.GenericTypographic);
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            int was = hover;
            hover = closeBox.Contains(e.Location) ? 1 : minimiseBox.Contains(e.Location) ? 2 : 0;
            Cursor = hover != 0 ? Cursors.Hand : Cursors.Default;
            if (was != hover) Invalidate();
            base.OnMouseMove(e);
        }

        protected override void OnMouseLeave(EventArgs e) { hover = 0; Invalidate(); base.OnMouseLeave(e); }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            if (e.Button != MouseButtons.Left) return;
            if (closeBox.Contains(e.Location)) { if (CloseClicked != null) CloseClicked(this, EventArgs.Empty); return; }
            if (minimiseBox.Contains(e.Location)) { if (MinimiseClicked != null) MinimiseClicked(this, EventArgs.Empty); return; }
            var form = FindForm();
            if (form == null) return;
            ReleaseCapture();
            SendMessage(form.Handle, 0xA1, (IntPtr)2, IntPtr.Zero); // Drag the window by its "title bar".
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                if (picture != null) picture.Dispose();
                if (screen != null) screen.Dispose();
                if (osd != null) osd.Dispose();
                foreach (var set in new[] { snow, burst }) if (set != null) foreach (var b in set) b.Dispose();
            }
            base.Dispose(disposing);
        }

        [DllImport("user32.dll")] private static extern bool ReleaseCapture();
        [DllImport("user32.dll")] private static extern IntPtr SendMessage(IntPtr window, int message, IntPtr w, IntPtr l);
    }

    /// <summary>A button in the deck's manner: square, monospaced capitals; the main one in blood red.</summary>
    internal sealed class VhsButton : Control
    {
        public bool Accent;
        private bool hover, down;

        public VhsButton()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw | ControlStyles.Selectable,
                true);
            Cursor = Cursors.Hand;
            BackColor = Art.Ink;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
            var r = new Rectangle(0, 0, Width - 1, Height - 1);
            Color fill = !Enabled ? Art.Ink : Accent ? (down ? Art.DarkRed : hover ? Color.FromArgb(236, 58, 52) : Art.Red)
                : (hover ? Color.FromArgb(44, 32, 30) : Art.Ink);
            Color edge = !Enabled ? Art.Faint : Accent ? Art.Red : hover ? Art.Cream : Art.Dim;
            Color text = !Enabled ? Art.Faint : Accent ? Color.White : Art.Cream;
            using (var brush = new SolidBrush(fill)) g.FillRectangle(brush, r);
            using (var pen = new Pen(edge, Math.Max(1f, Height / 40f))) g.DrawRectangle(pen, r);
            g.TextRenderingHint = TextRenderingHint.AntiAlias;
            using (var brush = new SolidBrush(text))
            using (var centre = new StringFormat { Alignment = StringAlignment.Center,
                LineAlignment = StringAlignment.Center, FormatFlags = StringFormatFlags.NoWrap })
                g.DrawString(Text, Font, brush, new RectangleF(0, 1, Width, Height), centre);
            if (Focused && ShowFocusCues)
                using (var pen = new Pen(Color.FromArgb(120, Art.Cream)) { DashStyle = DashStyle.Dot })
                    g.DrawRectangle(pen, Rectangle.Inflate(r, -4, -4));
        }

        protected override void OnEnabledChanged(EventArgs e) { Cursor = Enabled ? Cursors.Hand : Cursors.Default; Invalidate(); base.OnEnabledChanged(e); }
        protected override void OnTextChanged(EventArgs e) { Invalidate(); base.OnTextChanged(e); }
        protected override void OnMouseEnter(EventArgs e) { hover = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { hover = false; down = false; Invalidate(); base.OnMouseLeave(e); }
        protected override void OnMouseDown(MouseEventArgs e) { down = true; Focus(); Invalidate(); base.OnMouseDown(e); }
        protected override void OnMouseUp(MouseEventArgs e) { down = false; Invalidate(); base.OnMouseUp(e); }
        protected override void OnKeyUp(KeyEventArgs e) { if (e.KeyCode == Keys.Space || e.KeyCode == Keys.Enter) OnClick(EventArgs.Empty); base.OnKeyUp(e); }
        protected override void OnClick(EventArgs e) { if (Enabled) base.OnClick(e); }
    }
}
