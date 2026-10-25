using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;

namespace NunMassacreCoop.Setup
{
    /// <summary>
    /// The pictures. Once a copy of the game is found, its own key art (the Nun with her knife, the red title), read from
    /// that copy and never shipped here; before that, a title card drawn here. Either way MULTIPLAYER MOD is lettered under
    /// the title in the logo's leaning blood red, at the picture's own low resolution so it sits in the art like the
    /// title does, and the whole thing is shown blown up pixel for pixel, as off a worn tape.
    /// </summary>
    internal static class Art
    {
        public static readonly Color Red = Color.FromArgb(214, 38, 38);
        public static readonly Color DarkRed = Color.FromArgb(96, 10, 12);
        public static readonly Color Cream = Color.FromArgb(232, 222, 196);
        public static readonly Color Dim = Color.FromArgb(138, 126, 106);
        public static readonly Color Faint = Color.FromArgb(70, 64, 56);
        public static readonly Color Ink = Color.FromArgb(11, 9, 9);
        public static readonly Color Green = Color.FromArgb(120, 190, 110);
        public static readonly Color Amber = Color.FromArgb(226, 168, 70);

        /// <summary>Where the picture's bottom edge falls when it is cut to the window's shape (the lettering stays in).</summary>
        public const float KeepBottom = 0.955f;

        /// <summary>The game's key art from a copy of the game, lettered; null if it has none.</summary>
        public static Bitmap FromGame(string gameDirectory)
        {
            foreach (string name in new[] { "PS4SessionImage.jpg", "SaveIcon_PS5.png", "SaveIcon_PS4.png" })
            {
                Bitmap art = Load(Path.Combine(gameDirectory, "Nun Massacre_Data", "StreamingAssets", name));
                if (art == null) continue;
                float w = art.Width, h = art.Height;
                TitleLine line;
                using (var g = Graphics.FromImage(art))
                    if (Measure(art, out line)) Hug(g, art, "MULTIPLAYER MOD", line);
                    else Letter(g, "MULTIPLAYER MOD", new RectangleF(w * 0.30f, h * 0.795f, w * 0.57f, h * 0.12f), true, 7, 0.45f);
                return art;
            }
            return null;
        }

        /// <summary>MASSACRE as the art has it: where it starts and ends, the curve of its lower edge, its letters' height
        /// and its red.</summary>
        private struct TitleLine
        {
            public float Left, Right, Tall, A, B, C; // Lower edge: y = A + B t + C t^2, t from 0 (left end) to 1 (right end).
            public Color Red;
            public float Bottom(float x) { float t = (x - Left) / (Right - Left); return A + B * t + C * t * t; }
        }

        /// <summary>Finds MASSACRE's red letters in the lower half of the title and fits a curve to their lower edge.</summary>
        private static bool Measure(Bitmap art, out TitleLine line)
        {
            line = new TitleLine();
            int w = art.Width, h = art.Height, x0 = (int)(w * 0.25f), x1 = (int)(w * 0.95f), y0 = (int)(h * 0.55f), y1 = (int)(h * 0.90f);
            var lows = new System.Collections.Generic.List<PointF>();
            var heights = new System.Collections.Generic.List<float>();
            var reds = new System.Collections.Generic.List<Color>();
            int left = int.MaxValue, right = -1;
            for (int x = x0; x < x1; x++)
            {
                int top = -1, bottom = -1;
                for (int y = y0; y < y1; y++)
                {
                    var c = art.GetPixel(x, y);
                    if (c.R < 140 || c.R < c.G * 2.2f || c.R < c.B * 2.2f) continue;
                    if (top < 0) top = y;
                    bottom = y;
                    reds.Add(c);
                }
                if (bottom - top < 4) continue;
                lows.Add(new PointF(x, bottom));
                heights.Add(bottom - top);
                left = Math.Min(left, x); right = Math.Max(right, x);
            }
            if (lows.Count < 20 || right - left < w * 0.2f) return false;
            // Least squares for the quadratic through the lowest red of each column.
            double s0 = 0, s1 = 0, s2 = 0, s3 = 0, s4 = 0, t0 = 0, t1 = 0, t2 = 0;
            foreach (var p in lows)
            {
                double t = (p.X - left) / (double)(right - left), y = p.Y;
                s0 += 1; s1 += t; s2 += t * t; s3 += t * t * t; s4 += t * t * t * t;
                t0 += y; t1 += y * t; t2 += y * t * t;
            }
            double det = s0 * (s2 * s4 - s3 * s3) - s1 * (s1 * s4 - s3 * s2) + s2 * (s1 * s3 - s2 * s2);
            if (Math.Abs(det) < 1e-9) return false;
            double a = (t0 * (s2 * s4 - s3 * s3) - s1 * (t1 * s4 - s3 * t2) + s2 * (t1 * s3 - s2 * t2)) / det;
            double bb = (s0 * (t1 * s4 - t2 * s3) - t0 * (s1 * s4 - s3 * s2) + s2 * (s1 * t2 - t1 * s2)) / det;
            double cc = (s0 * (s2 * t2 - s3 * t1) - s1 * (s1 * t2 - s2 * t1) + t0 * (s1 * s3 - s2 * s2)) / det;
            // Lowered to clear the lowest letter feet (the curve runs through the middle of them).
            double clear = 0;
            foreach (var p in lows)
            {
                double t = (p.X - left) / (double)(right - left);
                clear = Math.Max(clear, p.Y - (a + bb * t + cc * t * t));
            }
            // The title's red: the body of its letters (the brighter half), not their darker edges.
            reds.Sort((x, y) => y.R.CompareTo(x.R));
            long r = 0, g = 0, b = 0; int n = Math.Max(1, reds.Count / 2);
            for (int i = 0; i < n; i++) { r += reds[i].R; g += reds[i].G; b += reds[i].B; }
            heights.Sort();
            line = new TitleLine { Left = left, Right = right, Tall = heights[heights.Count * 3 / 4],
                A = (float)(a + clear * 0.8), B = (float)bb, C = (float)cc,
                Red = Color.FromArgb((int)(r / n), (int)(g / n), (int)(b / n)) };
            return true;
        }

        /// <summary>
        /// MULTIPLAYER MOD under MASSACRE, hugging its lower edge: the words' tops follow that curve the whole way along, the
        /// letters swell a little to the right as MASSACRE's do, in the title's own red, soft-edged and haloed as the title
        /// sits in the picture (not stamped on hard).
        /// </summary>
        private static void Hug(Graphics target, Bitmap art, string text, TitleLine line)
        {
            using (var path = new GraphicsPath())
            {
                path.AddString(text, GameFonts.TitleFamily, (int)FontStyle.Regular, 100f, PointF.Empty, StringFormat.GenericTypographic);
                if (!GameFonts.GameTitle) using (var lean = new Matrix(1f, 0f, -0.2f, 1f, 0f, 0f)) path.Transform(lean);
                var bounds = path.GetBounds();
                // As wide as MASSACRE, set tall like the title, but no more than half MASSACRE's height.
                float span = line.Right - line.Left;
                float scaleX = span / bounds.Width, scaleY = scaleX * (GameFonts.GameTitle ? 1.5f : 1f);
                float tall = Math.Min(bounds.Height * scaleY, line.Tall * 0.62f);
                scaleY = tall / bounds.Height;
                scaleX = Math.Min(scaleX, scaleY / (GameFonts.GameTitle ? 1.5f : 1f));
                float width = bounds.Width * scaleX, left = line.Left + (span - width) * 0.5f, gap = line.Tall * 0.06f;
                var points = path.PathPoints;
                float low = float.MaxValue, high = float.MinValue;
                for (int i = 0; i < points.Length; i++)
                {
                    float x = left + (points[i].X - bounds.X) * scaleX, t = (x - left) / width;
                    float y = (points[i].Y - bounds.Y) * scaleY * (1f + 0.15f * t); // Swelling to the right, as MASSACRE does.
                    points[i] = new PointF(x, line.Bottom(x) + gap + y);
                    low = Math.Min(low, points[i].Y); high = Math.Max(high, points[i].Y);
                }
                // Drawn on its own layer a little larger than the words, softened, then laid into the picture.
                int ox = (int)Math.Floor(left) - 4, oy = (int)Math.Floor(low) - 4;
                int lw = (int)Math.Ceiling(width) + 9, lh = (int)Math.Ceiling(high - low) + 9;
                for (int i = 0; i < points.Length; i++) points[i] = new PointF(points[i].X - ox, points[i].Y - oy);
                using (var words = new GraphicsPath(points, path.PathTypes, path.FillMode))
                using (var layer = new Bitmap(lw, lh, PixelFormat.Format32bppArgb))
                {
                    using (var g = Graphics.FromImage(layer))
                    {
                        g.SmoothingMode = SmoothingMode.AntiAlias;
                        using (var brush = new SolidBrush(line.Red)) g.FillPath(brush, words);
                    }
                    Soften(layer);
                    var place = new Rectangle(ox, oy, lw, lh);
                    using (var halo = new Bitmap(layer))
                    {
                        Soften(halo);
                        Tinted(target, halo, Offset(place, 1, 1), 0f, 0f, 0f, 0.6f);      // The dark halo the title has.
                    }
                    Tinted(target, layer, Offset(place, -1, 0), 1f, 0.85f, 0.3f, 0.22f);  // The tape's colour fringe.
                    Tinted(target, layer, Offset(place, 1, 0), 0.25f, 0.45f, 1f, 0.18f);
                    target.DrawImage(layer, place, 0, 0, lw, lh, GraphicsUnit.Pixel);
                }
            }
        }

        /// <summary>A light blur of a layer (its colour kept, its edges eased), like the title's soft JPEG edges.</summary>
        private static void Soften(Bitmap layer)
        {
            int w = layer.Width, h = layer.Height;
            var alpha = new float[w, h];
            Color colour = Color.Black;
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++) { var c = layer.GetPixel(x, y); alpha[x, y] = c.A; if (c.A > 200) colour = c; }
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                {
                    float sum = 0, weight = 0;
                    for (int dy = -1; dy <= 1; dy++)
                        for (int dx = -1; dx <= 1; dx++)
                        {
                            int px = x + dx, py = y + dy;
                            if (px < 0 || py < 0 || px >= w || py >= h) continue;
                            float k = dx == 0 && dy == 0 ? 4f : dx == 0 || dy == 0 ? 2f : 1f;
                            sum += alpha[px, py] * k; weight += k;
                        }
                    layer.SetPixel(x, y, Color.FromArgb((int)(sum / weight), colour.R, colour.G, colour.B));
                }
        }

        /// <summary>Before the game is found: a tape between recordings, the Nun over the barbed wire pit (the frame from
        /// the game's barbed wire ending that the icon is made from), the title.</summary>
        public static Bitmap TitleCard(int width, int height)
        {
            var card = new Bitmap(width, height, PixelFormat.Format32bppArgb);
            var random = new Random(1981);
            Pixels(card, (x, y) =>
            {
                float dx = (x - width * 0.5f) / width, dy = (y - height * 0.5f) / height;
                float light = Math.Max(0f, 1f - (dx * dx + dy * dy) * 2.2f);
                int v = (int)((8 + random.Next(22)) * (0.45f + light));
                return Color.FromArgb(255, v + 3, v, v);
            });
            using (var g = Graphics.FromImage(card))
            {
                using (var nun = Resource("Art.Nun"))
                    if (nun != null)
                    {
                        // Her frame on the left, melting into the snow on its right.
                        int side = height;
                        using (var faded = new Bitmap(side, side, PixelFormat.Format32bppArgb))
                        {
                            using (var f = Graphics.FromImage(faded))
                            {
                                f.InterpolationMode = InterpolationMode.NearestNeighbor;
                                f.PixelOffsetMode = PixelOffsetMode.Half;
                                f.DrawImage(nun, new Rectangle(0, 0, side, side), 0, 0, nun.Width, nun.Height, GraphicsUnit.Pixel);
                            }
                            Pixels(faded, (x, y, c) =>
                            {
                                float edge = Math.Min(1f, Math.Min((side - 1 - x) / (side * 0.35f), y / (side * 0.12f) + 0.2f));
                                return Color.FromArgb((int)(255 * Math.Max(0f, Math.Min(1f, edge))), c.R, c.G, c.B);
                            });
                            g.DrawImage(faded, new Rectangle(0, 0, side, side), 0, 0, side, side, GraphicsUnit.Pixel);
                        }
                    }
                Letter(g, "NUN", new RectangleF(width * 0.56f, height * 0.12f, width * 0.22f, height * 0.25f), false, 3, 0.3f);
                Letter(g, "MASSACRE", new RectangleF(width * 0.50f, height * 0.37f, width * 0.46f, height * 0.32f), false, 5, 0.55f);
                Letter(g, "MULTIPLAYER MOD", new RectangleF(width * 0.50f, height * 0.72f, width * 0.46f, height * 0.15f), true, 7, 0.5f);
            }
            return card;
        }

        /// <summary>
        /// Lettering in the logo's manner: the title's own font (Glass Houses, from the game) set tall and narrow like the
        /// title, blood red, with the tape's colour fringe; Impact leaned forward until the game is found. Drawn hard-edged
        /// at the picture's resolution, so blowing it up keeps it as chunky as the title.
        /// </summary>
        /// <param name="bend">How far the word sinks towards its right end, in letter heights, as MASSACRE does in the title:
        /// each letter a little lower and a little bigger than the one before.</param>
        public static void Letter(Graphics target, string text, RectangleF box, bool alignRight, int seed, float bend = 0f)
        {
            int width = (int)Math.Ceiling(box.Width) + 6, height = (int)Math.Ceiling(box.Height) + 6;
            using (var layer = new Bitmap(width, height, PixelFormat.Format32bppArgb))
            {
                bool game = GameFonts.GameTitle;
                using (var g = Graphics.FromImage(layer))
                using (var path = new GraphicsPath())
                {
                    g.SmoothingMode = SmoothingMode.AntiAlias;
                    path.AddString(text, GameFonts.TitleFamily, (int)FontStyle.Regular, 100f, PointF.Empty, StringFormat.GenericTypographic);
                    if (!game) using (var lean = new Matrix(1f, 0f, -0.2f, 1f, 0f, 0f)) path.Transform(lean); // Glass Houses leans already.
                    var bounds = path.GetBounds();
                    float scaleY = box.Height / ((1f + bend) * bounds.Height), scaleX; // Room in the box for the bend.
                    if (game) scaleX = Math.Min(scaleY / 1.5f, box.Width / bounds.Width); // Tall and narrow, as the title is set.
                    else scaleX = scaleY = Math.Min(scaleY, box.Width / bounds.Width);
                    float x = alignRight ? 3f + box.Width - bounds.Width * scaleX : 3f;
                    float y = 3f + (bend > 0f ? 0f : (box.Height - bounds.Height * scaleY) * 0.5f);
                    using (var fit = new Matrix())
                    {
                        fit.Translate(-bounds.X, -bounds.Y, MatrixOrder.Append);
                        fit.Scale(scaleX, scaleY, MatrixOrder.Append);
                        fit.Translate(x, y, MatrixOrder.Append);
                        path.Transform(fit);
                    }
                    if (bend > 0f) Bend(path, x, bounds.Width * scaleX, y, bounds.Height * scaleY, bend);
                    using (var brush = new LinearGradientBrush(new RectangleF(0, y, width, Math.Max(1f, bounds.Height * scaleY)),
                        Color.FromArgb(255, 236, 52, 44), Color.FromArgb(255, 196, 30, 30), LinearGradientMode.Vertical))
                        g.FillPath(brush, path);
                }
                // Hard edges, and worn: a few pixels gone, a scratch or two across.
                var random = new Random(seed);
                int scratchA = random.Next(height), scratchB = random.Next(height);
                Pixels(layer, (px, py, c) =>
                {
                    if (c.A < 110) return Color.Transparent;
                    // Glass Houses is worn already.
                    if (!game && (random.NextDouble() < 0.05 || ((py == scratchA || py == scratchB) && random.NextDouble() < 0.45))) return Color.Transparent;
                    return Color.FromArgb(255, c.R, c.G, c.B);
                });
                int left = (int)Math.Round(box.X) - 3, top = (int)Math.Round(box.Y) - 3;
                var place = new Rectangle(left, top, width, height);
                var oldMode = target.InterpolationMode; var oldOffset = target.PixelOffsetMode;
                target.InterpolationMode = InterpolationMode.NearestNeighbor;
                target.PixelOffsetMode = PixelOffsetMode.Half;
                Tinted(target, layer, Offset(place, 1, 1), 0f, 0f, 0f, 0.85f);       // Shadow.
                Tinted(target, layer, Offset(place, -1, 0), 0.25f, 0.55f, 1f, 0.35f); // The tape's colour fringe.
                Tinted(target, layer, Offset(place, 1, 0), 1f, 0.85f, 0.3f, 0.30f);
                target.DrawImage(layer, place, 0, 0, width, height, GraphicsUnit.Pixel);
                target.InterpolationMode = oldMode; target.PixelOffsetMode = oldOffset;
            }
        }

        /// <summary>
        /// The title's warp: along the word the baseline sinks, slowly then faster (bend letter heights by the end), and the
        /// letters grow a little from their foot, so the word droops and swells to the right like MASSACRE.
        /// </summary>
        private static void Bend(GraphicsPath path, float left, float width, float top, float tall, float bend)
        {
            var points = path.PathPoints;
            var types = path.PathTypes;
            float baseline = top + tall;
            for (int i = 0; i < points.Length; i++)
            {
                float t = Math.Max(0f, Math.Min(1f, (points[i].X - left) / width));
                float sink = bend * tall * (0.6f * t + 0.4f * t * t), grow = 1f + 0.12f * t;
                points[i].Y = baseline - (baseline - points[i].Y) * grow + sink;
            }
            using (var bent = new GraphicsPath(points, types, path.FillMode))
            {
                path.Reset();
                path.AddPath(bent, false);
            }
        }

        /// <summary>A picture cut to the given shape (bottom at keepBottom of its height) and blown up pixel for pixel.</summary>
        public static Bitmap Fit(Bitmap source, Size size, float keepBottom)
        {
            float aspect = size.Width / (float)size.Height;
            var crop = new RectangleF(0, 0, source.Width, source.Height);
            if (source.Width / (float)source.Height > aspect) { crop.Width = source.Height * aspect; crop.X = (source.Width - crop.Width) * 0.5f; }
            else
            {
                crop.Height = source.Width / aspect;
                crop.Y = Math.Max(0f, Math.Min(source.Height - crop.Height, source.Height * keepBottom - crop.Height));
            }
            var shown = new Bitmap(size.Width, size.Height, PixelFormat.Format32bppPArgb);
            using (var g = Graphics.FromImage(shown))
            {
                g.InterpolationMode = InterpolationMode.NearestNeighbor;
                g.PixelOffsetMode = PixelOffsetMode.Half;
                g.DrawImage(source, new Rectangle(Point.Empty, size), crop.X, crop.Y, crop.Width, crop.Height, GraphicsUnit.Pixel);
            }
            return shown;
        }

        /// <summary>Snow: frames of grey grain, drawn chunky (cell pixels each), translucent or (for a burst) solid.</summary>
        public static Bitmap[] Snow(Size size, int count, int alpha, int cell, int seed)
        {
            var random = new Random(seed);
            var frames = new Bitmap[count];
            int w = Math.Max(1, (size.Width + cell - 1) / cell), h = Math.Max(1, (size.Height + cell - 1) / cell);
            for (int i = 0; i < count; i++)
                using (var small = new Bitmap(w, h, PixelFormat.Format32bppArgb))
                {
                    int streak = random.Next(h), streakLength = 1 + random.Next(3);
                    Pixels(small, (x, y) =>
                    {
                        int v = random.Next(256);
                        if (y >= streak && y < streak + streakLength) v = Math.Min(255, v + 90); // A bright tear across.
                        int a = alpha >= 255 ? 255 : random.Next(alpha);
                        return Color.FromArgb(a, v, v, v);
                    });
                    frames[i] = Fit(small, size, 1f);
                }
            return frames;
        }

        /// <summary>Scanlines and the dark corners of an old screen, in one layer.</summary>
        public static Bitmap Screen(Size size, float scale)
        {
            var layer = new Bitmap(size.Width, size.Height, PixelFormat.Format32bppPArgb);
            using (var g = Graphics.FromImage(layer))
            {
                int period = Math.Max(2, (int)Math.Round(3 * scale)), thick = Math.Max(1, period / 2);
                using (var line = new SolidBrush(Color.FromArgb(70, 0, 0, 0)))
                    for (int y = 0; y < size.Height; y += period) g.FillRectangle(line, 0, y, size.Width, thick);
                using (var path = new GraphicsPath())
                {
                    var oval = new RectangleF(-size.Width * 0.25f, -size.Height * 0.35f, size.Width * 1.5f, size.Height * 1.7f);
                    path.AddEllipse(oval);
                    using (var corners = new PathGradientBrush(path) { CenterColor = Color.FromArgb(0, 0, 0, 0),
                        SurroundColors = new[] { Color.FromArgb(200, 0, 0, 0) } })
                    {
                        corners.FocusScales = new PointF(0.55f, 0.45f);
                        g.FillRectangle(corners, 0, 0, size.Width, size.Height);
                    }
                }
                int edge = (int)(12 * scale) + 1; // Just the last few lines melt into the window below.
                using (var fade = new LinearGradientBrush(new Rectangle(0, size.Height - edge, size.Width, edge),
                    Color.FromArgb(0, Ink), Color.FromArgb(255, Ink), LinearGradientMode.Vertical))
                    g.FillRectangle(fade, 0, size.Height - edge, size.Width, edge);
            }
            return layer;
        }

        public static FontFamily Family()
        {
            foreach (string name in new[] { "Impact", "Arial Black", "Arial" })
                try { return new FontFamily(name); } catch (ArgumentException) { }
            return new FontFamily(System.Drawing.Text.GenericFontFamilies.SansSerif);
        }

        private static Bitmap Resource(string name)
        {
            try
            {
                using (var stream = System.Reflection.Assembly.GetExecutingAssembly().GetManifestResourceStream(name))
                    if (stream != null)
                        using (var image = Image.FromStream(stream)) return new Bitmap(image);
            }
            catch { }
            return null;
        }

        private static Bitmap Load(string path)
        {
            try
            {
                if (!File.Exists(path)) return null;
                using (var stream = new MemoryStream(File.ReadAllBytes(path)))
                using (var image = Image.FromStream(stream))
                {
                    var copy = new Bitmap(image.Width, image.Height, PixelFormat.Format32bppArgb);
                    using (var g = Graphics.FromImage(copy)) g.DrawImage(image, new Rectangle(0, 0, image.Width, image.Height));
                    return copy;
                }
            }
            catch { return null; }
        }

        private static void Tinted(Graphics g, Bitmap layer, Rectangle place, float r, float gr, float b, float alpha)
        {
            var matrix = new ColorMatrix(new[]
            {
                new[] { 0f, 0f, 0f, 0f, 0f },
                new[] { 0f, 0f, 0f, 0f, 0f },
                new[] { 0f, 0f, 0f, 0f, 0f },
                new[] { 0f, 0f, 0f, alpha, 0f },
                new[] { r, gr, b, 0f, 1f },
            });
            using (var attributes = new ImageAttributes())
            {
                attributes.SetColorMatrix(matrix);
                g.DrawImage(layer, place, 0, 0, layer.Width, layer.Height, GraphicsUnit.Pixel, attributes);
            }
        }

        private static Rectangle Offset(Rectangle r, int x, int y) { r.Offset(x, y); return r; }

        private static void Pixels(Bitmap bitmap, Func<int, int, Color> paint) { Pixels(bitmap, (x, y, c) => paint(x, y)); }

        private static void Pixels(Bitmap bitmap, Func<int, int, Color, Color> paint)
        {
            var area = new Rectangle(0, 0, bitmap.Width, bitmap.Height);
            var data = bitmap.LockBits(area, ImageLockMode.ReadWrite, PixelFormat.Format32bppArgb);
            try
            {
                int[] row = new int[bitmap.Width];
                for (int y = 0; y < bitmap.Height; y++)
                {
                    IntPtr line = data.Scan0 + y * data.Stride;
                    Marshal.Copy(line, row, 0, row.Length);
                    for (int x = 0; x < row.Length; x++) row[x] = paint(x, y, Color.FromArgb(row[x])).ToArgb();
                    Marshal.Copy(row, 0, line, row.Length);
                }
            }
            finally { bitmap.UnlockBits(data); }
        }
    }
}
