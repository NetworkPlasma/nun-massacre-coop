using System;
using System.ComponentModel;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Threading;
using System.Windows.Forms;

namespace NunMassacreCoop.Setup
{
    /// <summary>
    /// The setup window: the tape playing at the top, the copies of the game found on this PC (or one picked by hand, or
    /// dropped on the window), and INSTALL, PLAY and UNINSTALL. Put beside Nun Massacre.exe, it picks that copy itself.
    /// </summary>
    internal sealed class SetupForm : Form
    {
        private const int Width0 = 760, Height0 = 668;
        private readonly Options options;
        private readonly float s;
        private readonly VhsScreen tape = new VhsScreen();
        private readonly ListBox games = new ListBox();
        private readonly Label heading = new Label(), searching = new Label(), status = new Label(), footer = new Label();
        private readonly VhsButton browse = new VhsButton(), uninstall = new VhsButton(), play = new VhsButton(), install = new VhsButton { Accent = true };
        private readonly System.Windows.Forms.Timer clock = new System.Windows.Forms.Timer { Interval = 83 }; // Twelve frames a second.
        private Font rowFont, rowSmall;
        private volatile bool closing;
        private bool busy, searchDone, artFromGame, ranStartAction;
        private int dots;

        public SetupForm(Options options)
        {
            this.options = options;
            Text = "Nun Massacre Multiplayer Mod Setup";
            FormBorderStyle = FormBorderStyle.None;
            StartPosition = FormStartPosition.CenterScreen;
            AutoScaleMode = AutoScaleMode.None;
            BackColor = Art.Ink;
            DoubleBuffered = true;
            KeyPreview = true;
            AllowDrop = true;
            try { Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath); } catch { }

            float dpi;
            using (var g = Graphics.FromHwnd(IntPtr.Zero)) dpi = g.DpiX;
            var area = Screen.PrimaryScreen.WorkingArea;
            s = Math.Min(dpi / 96f, Math.Min(area.Width / (Width0 + 20f), area.Height / (Height0 + 20f)));
            ClientSize = new Size(Px(Width0), Px(Height0));
            if (options.Screenshot != null) { StartPosition = FormStartPosition.Manual; Location = new Point(-4000, -4000); ShowInTaskbar = false; }

            tape.Bounds = R(0, 0, Width0, 380);
            tape.Build(s);
            tape.CloseClicked += (o, e) => Close();
            tape.MinimiseClicked += (o, e) => WindowState = FormWindowState.Minimized;

            heading.Text = "WHERE IS NUN MASSACRE?";
            Style(heading, R(24, 392, 420, 20), Art.Dim, ContentAlignment.MiddleLeft);
            Style(searching, R(444, 392, 292, 20), Art.Dim, ContentAlignment.MiddleRight);

            games.Bounds = R(24, 416, 712, 122);
            games.DrawMode = DrawMode.OwnerDrawFixed;
            games.ItemHeight = Px(40);
            games.IntegralHeight = false;
            games.BorderStyle = BorderStyle.None;
            games.BackColor = Color.FromArgb(18, 14, 13);
            games.ForeColor = Art.Cream;
            games.DrawItem += DrawGame;
            games.SelectedIndexChanged += (o, e) => Selected();

            Style(status, R(24, 544, 712, 42), Art.Cream, ContentAlignment.MiddleLeft);

            Button(browse, "BROWSE...", R(24, 594, 160, 42), (o, e) => Browse());
            Button(uninstall, "UNINSTALL", R(326, 594, 150, 42), (o, e) => Run(false));
            Button(play, "PLAY", R(486, 594, 100, 42), (o, e) => Play());
            Button(install, "INSTALL", R(596, 594, 140, 42), (o, e) => Run(true));

            footer.Text = "Nun Massacre © Puppet Combo. Fan-made mod, not endorsed by Puppet Combo. Includes BepInEx 5 (LGPL-2.1).";
            Style(footer, R(24, 644, 712, 16), Art.Faint, ContentAlignment.MiddleLeft);
            Refont();

            Controls.AddRange(new Control[] { tape, heading, searching, games, status, browse, uninstall, play, install, footer });
            foreach (Control c in new Control[] { this, tape, games })
            {
                c.AllowDrop = true;
                c.DragEnter += (o, e) => { e.Effect = e.Data.GetDataPresent(DataFormats.FileDrop) ? DragDropEffects.Link : DragDropEffects.None; };
                c.DragDrop += (o, e) => Dropped(e.Data.GetData(DataFormats.FileDrop) as string[]);
            }

            using (var card = Art.TitleCard(457, 228)) tape.Show(card); // The game's art comes in when a copy is found.
            clock.Tick += (o, e) => Tick();
            UpdateButtons();
            SetStatus("LOOKING FOR NUN MASSACRE ON THIS PC...", Art.Dim);
        }

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            clock.Start();
            if (!string.IsNullOrEmpty(options.Directory))
            {
                string dir = DirectoryOf(options.Directory);
                if (dir != null) Add(Installer.Inspect(dir, GameLocator.Chosen), true);
            }
            var finder = new Thread(() =>
            {
                try
                {
                    GameLocator.Find((dir, source) =>
                    {
                        if (closing) return;
                        var game = Installer.Inspect(dir, source);
                        Later(() => Add(game, false));
                    }, () => closing);
                }
                catch { }
                Later(SearchFinished);
            }) { IsBackground = true, Name = "Find Nun Massacre" };
            finder.Start();
            // Restarted with permission to change a protected folder: carry on with what was asked.
            if (!string.IsNullOrEmpty(options.Directory) && (options.Install || options.Uninstall) && Current != null && !ranStartAction)
            {
                ranStartAction = true;
                Run(options.Install, true);
            }
        }

        protected override void OnFormClosing(FormClosingEventArgs e) { closing = true; clock.Stop(); base.OnFormClosing(e); }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            using (var pen = new Pen(Art.DarkRed, Math.Max(1f, s))) e.Graphics.DrawRectangle(pen, 0, 0, ClientSize.Width - 1, ClientSize.Height - 1);
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Escape) Close();
            base.OnKeyDown(e);
        }

        /// <summary>Every font in the window: the game's VCR OSD Mono once a copy of the game is found, Courier New until then.</summary>
        private void Refont()
        {
            bool game = GameFonts.Loaded;
            float k = game ? 1.12f : 1f; // VCR OSD Mono runs small for its size.
            Swap(heading, GameFonts.UiFont(14f * s * k, true));
            Swap(searching, GameFonts.UiFont(14f * s * k, true));
            Swap(status, GameFonts.UiFont(15f * s * k, true));
            Swap(footer, game ? GameFonts.UiFont(10f * s * k) : new Font("Consolas", 10.5f * s, FontStyle.Regular, GraphicsUnit.Pixel));
            foreach (var button in new[] { browse, uninstall, play, install }) Swap(button, GameFonts.UiFont(18f * s * k, true));
            var oldRow = rowFont; var oldSmall = rowSmall;
            rowFont = game ? GameFonts.UiFont(14f * s * k) : new Font("Consolas", 13.5f * s, FontStyle.Regular, GraphicsUnit.Pixel);
            rowSmall = GameFonts.UiFont(12.5f * s * k, true);
            if (oldRow != null) oldRow.Dispose();
            if (oldSmall != null) oldSmall.Dispose();
            games.Invalidate();
            tape.Refont();
        }

        private static void Swap(Control control, Font font)
        {
            var old = control.Font;
            control.Font = font;
            if (old != null && old != SystemFonts.DefaultFont && old != Control.DefaultFont) old.Dispose();
        }

        private void Tick()
        {
            tape.Tick();
            if (!searchDone && ++dots % 6 == 0) searching.Text = "SEARCHING" + new string('.', dots / 6 % 4);
        }

        // ---------- The copies of the game ----------
        private GameInstall Current { get { return games.SelectedItem as GameInstall; } }

        private void Add(GameInstall game, bool select)
        {
            for (int i = 0; i < games.Items.Count; i++)
            {
                var had = (GameInstall)games.Items[i];
                if (!string.Equals(had.Directory, game.Directory, StringComparison.OrdinalIgnoreCase)) continue;
                if (select) games.SelectedIndex = i;
                return;
            }
            int index = games.Items.Add(game);
            // The game's own fonts, from the first copy of the game that has them.
            if (!GameFonts.Loaded && GameFonts.Load(game.Directory))
            {
                Refont();
                if (!artFromGame) using (var card = Art.TitleCard(457, 228)) tape.Show(card);
            }
            var current = Current;
            // Picked: the one chosen, or the one this setup sits in; otherwise the first copy the mod can go into.
            if (select || current == null
                || (game.CanInstall && !current.CanInstall && current.Source != GameLocator.Chosen && current.Source != GameLocator.HereSource))
                games.SelectedIndex = index;
            if (!artFromGame && !options.NoArt) ShowArt(game);
        }

        private void Replace(GameInstall game)
        {
            for (int i = 0; i < games.Items.Count; i++)
                if (string.Equals(((GameInstall)games.Items[i]).Directory, game.Directory, StringComparison.OrdinalIgnoreCase))
                {
                    games.Items[i] = game;
                    games.SelectedIndex = i;
                }
            games.Invalidate();
            Selected();
        }

        private void ShowArt(GameInstall game)
        {
            var art = Art.FromGame(game.Directory);
            if (art == null) return;
            using (art) tape.Show(art);
            artFromGame = true;
        }

        private void SearchFinished()
        {
            searchDone = true;
            int count = games.Items.Count;
            searching.Text = count == 0 ? "NOT FOUND" : count == 1 ? "FOUND 1 COPY" : "FOUND " + count + " COPIES";
            if (count == 0)
                SetStatus("NUN MASSACRE WASN'T FOUND. PRESS BROWSE AND PICK Nun Massacre.exe, OR DROP THE GAME'S FOLDER ON THIS WINDOW.", Art.Amber);
            else if (!busy) Selected();
            if (options.Screenshot != null)
            {
                var wait = new System.Windows.Forms.Timer { Interval = 1200 };
                wait.Tick += (o, e) => { wait.Stop(); Picture(options.Screenshot); Close(); };
                wait.Start();
            }
        }

        private void Selected()
        {
            UpdateButtons();
            var game = Current;
            if (game == null || busy) return;
            Color colour; string text = Describe(game, out colour);
            SetStatus(text, colour);
        }

        private static string Describe(GameInstall game, out Color colour)
        {
            string version = Installer.PayloadVersion;
            colour = Art.Red;
            if (!game.Compatible) return game.Problem.ToUpperInvariant();
            if (game.LoaderProblem != null) return game.LoaderProblem.ToUpperInvariant();
            if (game.CoopVersion == version)
            {
                colour = Art.Green;
                return "MULTIPLAYER MOD " + version + " IS INSTALLED. PRESS PLAY, THEN CHOOSE MULTIPLAYER ON THE MAIN MENU.";
            }
            colour = Art.Amber;
            if (game.CoopVersion != null) return "MULTIPLAYER MOD " + game.CoopVersion + " IS INSTALLED HERE. PRESS UPDATE FOR " + version + ".";
            if (game.CoopDisabled) return "THE MULTIPLAYER MOD IS TURNED OFF HERE. PRESS INSTALL TO PUT " + version + " BACK.";
            colour = Art.Cream;
            return "READY. PRESS INSTALL TO PUT THE MULTIPLAYER MOD " + version + " INTO THIS COPY OF THE GAME.";
        }

        private static string Short(GameInstall game, out Color colour)
        {
            colour = Art.Red;
            if (!game.Compatible) return "WRONG GAME BUILD";
            if (game.LoaderProblem != null) return "OTHER MOD LOADER";
            colour = Art.Green;
            if (game.CoopVersion == Installer.PayloadVersion) return "MOD " + game.CoopVersion + " INSTALLED";
            colour = Art.Amber;
            if (game.CoopVersion != null) return "MOD " + game.CoopVersion + ": UPDATE";
            colour = Art.Cream;
            return game.CoopDisabled ? "MOD TURNED OFF" : "READY TO INSTALL";
        }

        private void DrawGame(object sender, DrawItemEventArgs e)
        {
            if (e.Index < 0) return;
            var game = (GameInstall)games.Items[e.Index];
            var g = e.Graphics;
            bool selected = (e.State & DrawItemState.Selected) != 0;
            var r = e.Bounds;
            using (var back = new SolidBrush(selected ? Color.FromArgb(70, 12, 14) : games.BackColor)) g.FillRectangle(back, r);
            using (var line = new Pen(Color.FromArgb(36, 30, 28))) g.DrawLine(line, r.Left, r.Bottom - 1, r.Right, r.Bottom - 1);
            Color colour; string state = Short(game, out colour);
            int pad = Px(10), mark = Px(10);
            using (var dot = new SolidBrush(colour)) g.FillRectangle(dot, r.Left + pad, r.Top + (r.Height - mark) / 2, mark, mark);
            int left = r.Left + pad * 2 + mark;
            g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAlias; // The game's font is drawn by GDI+.
            var stateSize = g.MeasureString(state, rowSmall);
            var pathBox = new RectangleF(left, r.Top + Px(3), r.Right - left - stateSize.Width - pad * 2, r.Height / 2f);
            // Where it is, from the start (the drive, the library, the folders that tell two copies apart): ~ for your user folder.
            using (var path = new StringFormat { LineAlignment = StringAlignment.Center,
                Trimming = StringTrimming.EllipsisCharacter, FormatFlags = StringFormatFlags.NoWrap })
            using (var plain = new StringFormat { LineAlignment = StringAlignment.Center,
                Trimming = StringTrimming.EllipsisCharacter, FormatFlags = StringFormatFlags.NoWrap })
            using (var right = new StringFormat { Alignment = StringAlignment.Far, LineAlignment = StringAlignment.Center,
                FormatFlags = StringFormatFlags.NoWrap })
            using (var main = new SolidBrush(selected ? Color.White : Art.Cream))
            using (var dim = new SolidBrush(Art.Dim))
            using (var mood = new SolidBrush(colour))
            {
                g.DrawString(Friendly(game.Directory), rowFont, main, pathBox, path);
                g.DrawString(game.Source, rowSmall, dim, new RectangleF(left, r.Top + r.Height / 2f, pathBox.Width, r.Height / 2f - Px(3)), plain);
                g.DrawString(state, rowSmall, mood, new RectangleF(r.Left, r.Top, r.Width - pad, r.Height), right);
            }
        }

        private static readonly string Profile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile).TrimEnd('\\');
        private static string Friendly(string directory)
        {
            return Profile.Length > 0 && directory.StartsWith(Profile + "\\", StringComparison.OrdinalIgnoreCase)
                ? "~" + directory.Substring(Profile.Length) : directory;
        }

        private void UpdateButtons()
        {
            var game = Current;
            install.Enabled = !busy && game != null && game.CanInstall;
            install.Text = game == null || game.CoopVersion == null ? "INSTALL" : game.CoopVersion == Installer.PayloadVersion ? "REINSTALL" : "UPDATE";
            uninstall.Enabled = !busy && game != null && (game.CoopVersion != null || game.CoopDisabled);
            play.Enabled = !busy && game != null && game.CoopVersion != null && File.Exists(Path.Combine(game.Directory, Installer.GameExe));
            browse.Enabled = !busy;
        }

        // ---------- Choosing a copy by hand ----------
        private void Browse()
        {
            using (var dialog = new OpenFileDialog { Title = "Find Nun Massacre.exe",
                Filter = "Nun Massacre|Nun Massacre.exe|Programs (*.exe)|*.exe", FileName = Installer.GameExe,
                CheckFileExists = true })
            {
                if (dialog.ShowDialog(this) != DialogResult.OK) return;
                Choose(dialog.FileName);
            }
        }

        private void Dropped(string[] paths)
        {
            if (paths == null || paths.Length == 0) return;
            Choose(paths[0]);
        }

        private void Choose(string path)
        {
            string dir = DirectoryOf(path);
            if (dir == null) { SetStatus("THAT ISN'T NUN MASSACRE: PICK Nun Massacre.exe, OR THE FOLDER IT IS IN.", Art.Red); return; }
            Add(Installer.Inspect(dir, GameLocator.Chosen), true);
        }

        /// <summary>The game's folder from the exe, its folder, or a folder inside it (null when none of those).</summary>
        private static string DirectoryOf(string path)
        {
            try
            {
                path = Path.GetFullPath(path);
                string dir = File.Exists(path) ? Path.GetDirectoryName(path) : path;
                for (var d = new DirectoryInfo(dir); d != null; d = d.Parent)
                    if (File.Exists(Path.Combine(d.FullName, Installer.GameExe))) return d.FullName.TrimEnd('\\');
            }
            catch { }
            return null;
        }

        // ---------- Installing, removing, playing ----------
        private void Run(bool installing, bool confirmed = false)
        {
            var game = Current;
            if (game == null || busy) return;
            bool turnOffLoader = options.TurnOffLoader;
            if (!installing && !confirmed)
            {
                if (MessageBox.Show(this, "Remove the multiplayer mod from\n" + game.Directory + "?", Text,
                    MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes)
                    return;
                if (!Installer.OtherMods(game.Directory))
                    turnOffLoader = MessageBox.Show(this,
                        "Also turn off the BepInEx mod loader, so the game starts exactly as it came?\n\n(Your multiplayer settings stay in the BepInEx folder.)",
                        Text, MessageBoxButtons.YesNo, MessageBoxIcon.Question) == DialogResult.Yes;
            }
            if (Installer.IsRunning(game.Directory)) { SetStatus("CLOSE NUN MASSACRE FIRST, THEN TRY AGAIN.", Art.Red); return; }
            if (!Installer.CanWrite(game.Directory)) { WithPermission(game.Directory, installing, turnOffLoader); return; }
            busy = true;
            UpdateButtons();
            tape.Static(300);
            var worker = new Thread(() =>
            {
                string error = null; bool permission = false;
                try
                {
                    Action<string> log = line => Later(() => SetStatus(line.ToUpperInvariant(), Art.Cream));
                    if (installing) Installer.Install(game, log); else Installer.Uninstall(game, turnOffLoader, log);
                }
                catch (UnauthorizedAccessException) { permission = true; }
                catch (Exception ex) { error = ex.Message; }
                Later(() => Finished(game, installing, turnOffLoader, error, permission));
            }) { IsBackground = true, Name = installing ? "Install" : "Uninstall" };
            worker.Start();
        }

        private void Finished(GameInstall game, bool installing, bool turnOffLoader, string error, bool permission)
        {
            busy = false;
            Replace(Installer.Inspect(game.Directory, game.Source));
            if (permission) { WithPermission(game.Directory, installing, turnOffLoader); return; }
            if (error != null) { SetStatus("COULDN'T FINISH: " + error.ToUpperInvariant(), Art.Red); return; }
            tape.Static(500);
            if (installing) SetStatus("INSTALLED. PRESS PLAY, THEN CHOOSE MULTIPLAYER ON THE MAIN MENU.", Art.Green);
            else SetStatus(turnOffLoader ? "REMOVED. THE GAME NOW STARTS AS IT CAME." : "REMOVED. THE MULTIPLAYER MOD IS GONE FROM THIS COPY.", Art.Green);
        }

        /// <summary>The game is somewhere only an administrator can change (Program Files): restart with Windows' permission.</summary>
        private void WithPermission(string directory, bool installing, bool turnOffLoader)
        {
            string arguments = "--dir \"" + directory + "\" " + (installing ? "--install" : "--uninstall") + (turnOffLoader ? " --turn-off-loader" : "");
            try
            {
                Process.Start(new ProcessStartInfo(Application.ExecutablePath, arguments) { Verb = "runas", UseShellExecute = true });
                Close();
            }
            catch (Win32Exception) { SetStatus("WINDOWS DIDN'T GIVE PERMISSION TO CHANGE THAT FOLDER, SO NOTHING WAS CHANGED.", Art.Red); }
        }

        private void Play()
        {
            var game = Current;
            if (game == null) return;
            try
            {
                Process.Start(new ProcessStartInfo(Path.Combine(game.Directory, Installer.GameExe)) { WorkingDirectory = game.Directory,
                    UseShellExecute = true });
                SetStatus("STARTING NUN MASSACRE... CHOOSE MULTIPLAYER ON THE MAIN MENU.", Art.Green);
                WindowState = FormWindowState.Minimized;
            }
            catch (Exception ex) { SetStatus("COULDN'T START THE GAME: " + ex.Message.ToUpperInvariant(), Art.Red); }
        }

        // ---------- Bits ----------
        private void SetStatus(string text, Color colour) { status.Text = text; status.ForeColor = colour; }

        private void Later(Action action)
        {
            if (closing || IsDisposed) return;
            try { BeginInvoke(action); } catch (InvalidOperationException) { } // Closing.
        }

        private void Picture(string path)
        {
            using (var shot = new Bitmap(ClientSize.Width, ClientSize.Height))
            {
                DrawToBitmap(shot, new Rectangle(Point.Empty, ClientSize));
                shot.Save(path, ImageFormat.Png);
            }
        }

        private int Px(float value) { return (int)Math.Round(value * s); }
        private Rectangle R(float x, float y, float w, float h) { return new Rectangle(Px(x), Px(y), Px(w), Px(h)); }

        private static void Style(Label label, Rectangle bounds, Color colour, ContentAlignment align)
        {
            label.Bounds = bounds; label.ForeColor = colour; label.BackColor = Color.Transparent;
            label.TextAlign = align; label.UseMnemonic = false; label.AutoEllipsis = true;
            label.UseCompatibleTextRendering = true; // GDI+, which draws the game's font from memory.
        }

        private static void Button(VhsButton button, string text, Rectangle bounds, EventHandler click)
        {
            button.Text = text; button.Bounds = bounds; button.Click += click;
        }
    }
}
