using System;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Windows.Forms;

namespace NunMassacreCoop.Setup
{
    /// <summary>One copy of the game on this PC, and what is in it.</summary>
    internal sealed class GameInstall
    {
        public string Directory, Source;
        public bool Compatible;        // The build the mod is made for.
        public string Problem;         // Why the mod cannot go in (null when it can).
        public string LoaderProblem;   // Another loader, or the wrong BepInEx.
        public bool HasLoader;         // BepInEx 5 core files are here.
        public bool LoaderComplete;    // ...and the files that start it with the game.
        public string CoopVersion;     // The multiplayer mod installed here, or null.
        public bool CoopDisabled;      // Turned off by the old uninstall script.
        public bool CanInstall { get { return Compatible && LoaderProblem == null; } }
    }

    /// <summary>
    /// Puts the mod into a copy of the game and takes it out again. The mod and the BepInEx 5.4.23.3 loader travel inside
    /// this program; nothing is downloaded, and none of the game's own files are changed.
    /// </summary>
    internal static class Installer
    {
        public const string GameExe = "Nun Massacre.exe";
        public const string PluginName = "NunMassacre.Multiplayer.dll";
        public const string RaytracerName = "vaudionative.dll"; // Vercidium Audio's raytracer, for the voices' acoustics.
        public static readonly string PluginFolder = Path.Combine("BepInEx", "plugins", "NunMassacreCoop");
        // Assembly-CSharp.dll of the Windows Definitive Edition, build 2022.06.08: the game the mod is made for.
        private const string GameBuild = "0132E2E1556CDD8026EB42A08774CFEB94E313C4E29D40CFE640C8FC5FA0E871";
        // BepInEx_win_x64_5.4.23.3.zip as released by the BepInEx project.
        private const string LoaderHash = "41A089E5B1B1F0713B331346BAF6677B1184C69EABEBF51101097954E854C749";
        private static readonly string[] StartFiles = { "winhttp.dll", "doorstop_config.ini", ".doorstop_version" };

        public static string PayloadVersion
        {
            get { string v = Application.ProductVersion; int plus = v.IndexOf('+'); return plus > 0 ? v.Substring(0, plus) : v; }
        }

        public static GameInstall Inspect(string directory, string source)
        {
            var game = new GameInstall { Directory = directory, Source = source };
            string managed = Path.Combine(directory, "Nun Massacre_Data", "Managed", "Assembly-CSharp.dll");
            try
            {
                if (!File.Exists(Path.Combine(directory, GameExe))) game.Problem = "Nun Massacre.exe is not in this folder.";
                else if (!File.Exists(managed)) game.Problem = "The game's files are incomplete here.";
                else if (!string.Equals(HashFile(managed), GameBuild, StringComparison.OrdinalIgnoreCase))
                    game.Problem = "This is a different build of Nun Massacre. The multiplayer mod is made for the Windows Definitive Edition, build 2022.06.08.";
            }
            catch (Exception ex) { game.Problem = "The game's files could not be read: " + ex.Message; }
            game.Compatible = game.Problem == null;

            string core = Path.Combine(directory, "BepInEx", "core", "BepInEx.dll");
            if (File.Exists(core))
            {
                game.HasLoader = true;
                try
                {
                    var version = AssemblyName.GetAssemblyName(core).Version;
                    if (version.Major != 5)
                        game.LoaderProblem = "BepInEx " + version.ToString(3) + " is installed here; the multiplayer mod runs on BepInEx 5.";
                }
                catch { game.LoaderProblem = "The BepInEx loader here could not be read."; }
                game.LoaderComplete = File.Exists(Path.Combine(directory, "winhttp.dll")) && File.Exists(Path.Combine(directory, "doorstop_config.ini"));
            }
            else
            {
                foreach (string name in new[] { "winhttp.dll", "version.dll", "doorstop_config.ini" })
                    if (File.Exists(Path.Combine(directory, name)))
                    {
                        game.LoaderProblem = "Another mod loader is installed here (" + name + "). Remove it first, or install BepInEx 5 yourself.";
                        break;
                    }
            }

            string plugin = Path.Combine(directory, PluginFolder, PluginName);
            if (File.Exists(plugin)) game.CoopVersion = VersionOf(plugin);
            else if (File.Exists(plugin + ".disabled")) game.CoopDisabled = true;
            return game;
        }

        public static void Install(GameInstall game, Action<string> log)
        {
            if (!game.Compatible) throw new InvalidOperationException(game.Problem);
            if (game.LoaderProblem != null) throw new InvalidOperationException(game.LoaderProblem);
            if (IsRunning(game.Directory)) throw new InvalidOperationException("Close Nun Massacre first, then try again.");
            string root = Path.GetFullPath(game.Directory);
            if (!game.HasLoader || !game.LoaderComplete)
            {
                log(game.HasLoader ? "Restoring the files that start BepInEx with the game" : "Installing the BepInEx 5.4.23.3 mod loader");
                byte[] zip = Payload("Payload.Loader");
                if (!string.Equals(Hash(zip), LoaderHash, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException("This setup's copy of the mod loader is damaged. Get the setup again.");
                using (var archive = new ZipArchive(new MemoryStream(zip), ZipArchiveMode.Read))
                {
                    // The files that make the game start BepInEx go in last: if anything stops part way, the game still
                    // starts as it came, and running the setup again finishes the job.
                    var entries = archive.Entries.Where(e => !string.IsNullOrEmpty(e.Name) && e.FullName != "changelog.txt") // Folders; BepInEx's own notes.
                        .Where(e => !game.HasLoader || StartFiles.Contains(e.FullName)) // BepInEx is here: only what starts it.
                        .OrderBy(e => StartFiles.Contains(e.FullName) ? 1 : 0).ToList();
                    foreach (var entry in entries)
                    {
                        string target = Path.GetFullPath(Path.Combine(root, entry.FullName));
                        if (!target.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)) continue;
                        System.IO.Directory.CreateDirectory(Path.GetDirectoryName(target));
                        entry.ExtractToFile(target, true);
                    }
                }
            }
            log("Copying the multiplayer mod " + PayloadVersion);
            PlaceModFolder(Path.Combine(root, PluginFolder), log);
            TidyOldPackage(root, log);
            var after = Inspect(root, game.Source);
            if (after.CoopVersion != PayloadVersion || !after.LoaderComplete) throw new IOException("The files did not end up in place in " + root + ".");
            log("Installed. Start the game and choose MULTIPLAYER on the main menu.");
        }

        private static readonly string[] ModFiles = { PluginName, RaytracerName, "README.md", "CHANGELOG.md" };

        /// <summary>
        /// The mod's folder replaced as a whole. The new files are written beside it and checked byte for byte; then the
        /// old folder is moved aside and the new one moved into its place, so a full disk, a locked file or a crash part
        /// way leaves either the old version or the new one, never a mix. If the swap fails, the old folder comes back.
        /// </summary>
        private static void PlaceModFolder(string folder, Action<string> log)
        {
            string staging = folder + ".new", backup = folder + ".old";
            DeleteFolder(staging);
            DeleteFolder(backup); // Left by an earlier setup that was stopped part way.
            try
            {
                System.IO.Directory.CreateDirectory(staging);
                foreach (string name in ModFiles)
                {
                    byte[] data = Payload(PayloadName(name));
                    string path = Path.Combine(staging, name);
                    File.WriteAllBytes(path, data);
                    if (!string.Equals(HashFile(path), Hash(data), StringComparison.OrdinalIgnoreCase))
                        throw new IOException(name + " was not written correctly.");
                }
            }
            catch
            {
                DeleteFolder(staging);
                throw;
            }

            bool hadFolder = System.IO.Directory.Exists(folder);
            try
            {
                if (hadFolder) System.IO.Directory.Move(folder, backup);
                System.IO.Directory.Move(staging, folder);
            }
            catch (Exception ex)
            {
                if (!System.IO.Directory.Exists(folder) && System.IO.Directory.Exists(backup)) System.IO.Directory.Move(backup, folder);
                DeleteFolder(staging);
                throw new IOException("The mod's files could not be swapped in (" + ex.Message + "). Nothing was changed.", ex);
            }

            if (hadFolder)
            {
                // Anything else in the old folder (a player's own notes) moves across; ours are replaced.
                foreach (string path in System.IO.Directory.GetFiles(backup))
                {
                    string name = Path.GetFileName(path);
                    if (ModFiles.Contains(name, StringComparer.OrdinalIgnoreCase) || name.EndsWith(".disabled", StringComparison.OrdinalIgnoreCase)) continue;
                    try { File.Move(path, Path.Combine(folder, name)); } catch (IOException) { }
                }
                DeleteFolder(backup);
            }
        }

        private static string PayloadName(string file)
        {
            switch (file)
            {
                case PluginName: return "Payload.Plugin";
                case RaytracerName: return "Payload.Raytracer";
                case "README.md": return "Payload.Readme";
                default: return "Payload.Changelog";
            }
        }

        private static void DeleteFolder(string path)
        {
            try { if (System.IO.Directory.Exists(path)) System.IO.Directory.Delete(path, true); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }

        public static void Uninstall(GameInstall game, bool turnOffLoader, Action<string> log)
        {
            if (IsRunning(game.Directory)) throw new InvalidOperationException("Close Nun Massacre first, then try again.");
            string folder = Path.Combine(game.Directory, PluginFolder);
            foreach (string name in new[] { PluginName, PluginName + ".disabled", RaytracerName, "README.md", "CHANGELOG.md" })
            {
                string path = Path.Combine(folder, name);
                if (File.Exists(path)) File.Delete(path);
            }
            if (System.IO.Directory.Exists(folder) && !System.IO.Directory.EnumerateFileSystemEntries(folder).Any()) System.IO.Directory.Delete(folder);
            log("Removed the multiplayer mod");
            if (!turnOffLoader) return;
            if (OtherMods(game.Directory)) { log("Kept the BepInEx loader: other mods use it"); return; }
            foreach (string name in StartFiles)
            {
                string path = Path.Combine(game.Directory, name);
                if (File.Exists(path)) File.Delete(path);
            }
            log("Turned off the BepInEx loader: the game starts as it came (its settings stay in the BepInEx folder)");
        }

        /// <summary>Other mods in BepInEx's plugin folder (then the loader stays).</summary>
        public static bool OtherMods(string directory)
        {
            string plugins = Path.Combine(directory, "BepInEx", "plugins");
            if (!System.IO.Directory.Exists(plugins)) return false;
            return System.IO.Directory.EnumerateFileSystemEntries(plugins)
                .Any(p => !string.Equals(Path.GetFileName(p), "NunMassacreCoop", StringComparison.OrdinalIgnoreCase));
        }

        public static bool CanWrite(string directory)
        {
            string probe = Path.Combine(directory, ".nmm-setup-" + Guid.NewGuid().ToString("N").Substring(0, 8));
            try { File.WriteAllText(probe, ""); File.Delete(probe); return true; }
            catch (UnauthorizedAccessException) { return false; }
            catch (IOException) { return false; }
        }

        public static bool IsRunning(string directory)
        {
            foreach (var process in Process.GetProcessesByName("Nun Massacre"))
            {
                try
                {
                    string exe = process.MainModule.FileName;
                    if (string.Equals(Path.GetDirectoryName(exe), Path.GetFullPath(directory).TrimEnd('\\'), StringComparison.OrdinalIgnoreCase)) return true;
                }
                catch { } // Another user's, or not readable: not this copy.
                finally { process.Dispose(); }
            }
            return false;
        }

        /// <summary>The scripts and notes the old zip release left beside the game (only ours, recognised by their text).</summary>
        private static void TidyOldPackage(string root, Action<string> log)
        {
            int removed = 0;
            foreach (string name in new[] { "Install-Coop.ps1", "Install-Coop.cmd", "Uninstall-Coop.ps1", "README.md", "CHANGELOG.md" })
            {
                string path = Path.Combine(root, name);
                try
                {
                    if (!File.Exists(path) || new FileInfo(path).Length > 512 * 1024) continue;
                    string text = File.ReadAllText(path);
                    bool ours = text.IndexOf("Nun Massacre Co-op", StringComparison.Ordinal) >= 0 || text.IndexOf("NunMassacreCoop", StringComparison.Ordinal) >= 0
                        || text.IndexOf("Install-Coop.ps1", StringComparison.Ordinal) >= 0 || (name == "CHANGELOG.md" && text.IndexOf("(protocol 1", StringComparison.Ordinal) >= 0);
                    if (!ours) continue;
                    File.Delete(path);
                    removed++;
                }
                catch { }
            }
            if (removed > 0) log("Tidied away the old install scripts beside the game");
        }

        private static string VersionOf(string path)
        {
            try
            {
                string v = FileVersionInfo.GetVersionInfo(path).ProductVersion ?? "";
                int plus = v.IndexOf('+');
                return plus > 0 ? v.Substring(0, plus) : (v.Length > 0 ? v : "?");
            }
            catch { return "?"; }
        }

        private static byte[] Payload(string name)
        {
            using (var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(name))
            {
                if (stream == null) throw new InvalidDataException("This setup is missing " + name + ".");
                var memory = new MemoryStream();
                stream.CopyTo(memory);
                return memory.ToArray();
            }
        }

        private static string Hash(byte[] data) { using (var sha = SHA256.Create()) return BitConverter.ToString(sha.ComputeHash(data)).Replace("-", ""); }
        private static string HashFile(string path)
        {
            using (var sha = SHA256.Create())
            using (var stream = File.OpenRead(path))
                return BitConverter.ToString(sha.ComputeHash(stream)).Replace("-", "");
        }
    }
}
