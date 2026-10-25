using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows.Forms;

namespace NunMassacreCoop.Setup
{
    /// <summary>What the setup was started with.</summary>
    internal sealed class Options
    {
        public string Directory;     // --dir: the game's folder, chosen beforehand (also how the setup restarts itself with permission).
        public bool Install;         // --install: install there straight away.
        public bool Uninstall;       // --uninstall: remove it from there straight away (--turn-off-loader: and stop BepInEx).
        public bool TurnOffLoader;
        public bool Quiet;           // --quiet: no window (with --install or --uninstall; --log for the outcome).
        public string Log;
        public string List;          // --list FILE: write the copies of the game found, then exit.
        public string Screenshot;    // --screenshot FILE: picture the window once the search ends, then exit (for tests).
        public bool NoArt;           // --no-art: leave the game's art out (tests of the title card).

        public static Options Parse(string[] args)
        {
            var o = new Options();
            for (int i = 0; i < args.Length; i++)
            {
                Func<string> next = () => i + 1 < args.Length ? args[++i] : null;
                switch (args[i].ToLowerInvariant())
                {
                    case "--dir": o.Directory = next(); break;
                    case "--install": o.Install = true; break;
                    case "--uninstall": o.Uninstall = true; break;
                    case "--turn-off-loader": o.TurnOffLoader = true; break;
                    case "--quiet": o.Quiet = true; break;
                    case "--log": o.Log = next(); break;
                    case "--list": o.List = next(); break;
                    case "--screenshot": o.Screenshot = next(); break;
                    case "--no-art": o.NoArt = true; break;
                }
            }
            return o;
        }
    }

    internal static class Program
    {
        [STAThread]
        private static int Main(string[] args)
        {
            var options = Options.Parse(args);
            if (options.Quiet || options.List != null) return Quietly(options);
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.Run(new SetupForm(options));
            return 0;
        }

        private static int Quietly(Options o)
        {
            var lines = new List<string>();
            Action<string> log = line => lines.Add(line);
            int code = 0;
            try
            {
                if (o.List != null)
                {
                    var found = new List<string>();
                    GameLocator.Find((dir, source) =>
                    {
                        var game = Installer.Inspect(dir, source);
                        found.Add(source + "\t" + dir + "\t" + (game.Problem ?? game.LoaderProblem ?? "ready") + "\t" + (game.CoopVersion ?? "-"));
                    }, () => false);
                    File.WriteAllLines(o.List, found);
                }
                if (o.Install || o.Uninstall)
                {
                    if (string.IsNullOrEmpty(o.Directory)) throw new ArgumentException("--dir is needed with --quiet.");
                    var game = Installer.Inspect(Path.GetFullPath(o.Directory), GameLocator.Chosen);
                    if (o.Install) Installer.Install(game, log);
                    else Installer.Uninstall(game, o.TurnOffLoader, log);
                }
            }
            catch (Exception ex) { log("FAILED: " + ex.Message); code = 1; }
            if (o.Log != null) File.WriteAllLines(o.Log, lines);
            return code;
        }
    }
}
