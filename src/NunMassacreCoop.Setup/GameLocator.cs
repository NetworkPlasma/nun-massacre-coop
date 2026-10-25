using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using Microsoft.Win32;

namespace NunMassacreCoop.Setup
{
    /// <summary>
    /// Finds copies of Nun Massacre on this PC: beside this program (dropped into the game's folder), in the
    /// installed-programs list, then by looking through the usual folders and drives (where a download from itch.io or
    /// Patreon was unzipped). Each copy is reported as soon as it is found.
    /// </summary>
    internal static class GameLocator
    {
        public const string HereSource = "THIS FOLDER", Chosen = "CHOSEN";
        private static readonly HashSet<string> Skip = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "Windows", "$Recycle.Bin", "System Volume Information", "ProgramData", "AppData", "Recovery", "PerfLogs", "MSOCache",
            "Config.Msi", "WindowsApps", "Windows Defender", "Windows Kits", "Microsoft", "Microsoft SDKs", "Microsoft Visual Studio",
            "dotnet", "Common Files", "node_modules", ".git", "obj", "bin", "test-results", "artifacts", "Nun Massacre_Data", "MonoBleedingEdge", "BepInEx",
        };

        public static void Find(Action<string, string> found, Func<bool> cancelled)
        {
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            Action<string, string> report = (directory, source) =>
            {
                if (string.IsNullOrWhiteSpace(directory) || cancelled()) return;
                try
                {
                    directory = Path.GetFullPath(directory.Trim().Trim('"')).TrimEnd('\\', '/');
                    if (File.Exists(Path.Combine(directory, Installer.GameExe)) && seen.Add(directory)) found(directory, source);
                }
                catch { }
            };

            // Dropped into the game's folder (or a folder inside it).
            var here = new DirectoryInfo(AppDomain.CurrentDomain.BaseDirectory);
            for (int up = 0; here != null && up < 3; up++, here = here.Parent) report(here.FullName, HereSource);

            Safe(() => Registered(report));

            // Then by looking: the usual places first, then every fixed drive. About twenty seconds at most.
            var clock = Stopwatch.StartNew();
            string profile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            var places = new List<KeyValuePair<string, int>>
            {
                new KeyValuePair<string, int>(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), 4),
                new KeyValuePair<string, int>(Path.Combine(profile, "Downloads"), 4),
                new KeyValuePair<string, int>(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), 4),
                new KeyValuePair<string, int>(profile, 3),
                new KeyValuePair<string, int>(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), 4),
                new KeyValuePair<string, int>(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), 4),
            };
            foreach (var drive in DriveInfo.GetDrives())
                if (drive.DriveType == DriveType.Fixed && drive.IsReady) places.Add(new KeyValuePair<string, int>(drive.RootDirectory.FullName, 4));
            foreach (var place in places)
            {
                if (cancelled() || clock.ElapsedMilliseconds > 20000) break;
                Scan(place.Key, place.Value, report, "ON THIS PC", cancelled, clock, 20000);
            }
        }

        private static void Scan(string root, int depth, Action<string, string> report, string source, Func<bool> cancelled, Stopwatch clock, long budget)
        {
            if (string.IsNullOrEmpty(root) || !Directory.Exists(root)) return;
            var level = new List<string> { root };
            for (int d = 0; d <= depth && level.Count > 0; d++)
            {
                var next = new List<string>();
                foreach (string folder in level)
                {
                    if (cancelled() || clock.ElapsedMilliseconds > budget) return;
                    if (File.Exists(Path.Combine(folder, Installer.GameExe))) { report(folder, source); continue; }
                    if (d == depth) continue;
                    try
                    {
                        foreach (var child in new DirectoryInfo(folder).EnumerateDirectories())
                        {
                            var attributes = child.Attributes;
                            if ((attributes & (FileAttributes.ReparsePoint | FileAttributes.System)) != 0) continue;
                            if (Skip.Contains(child.Name) || child.Name.StartsWith(".", StringComparison.Ordinal) || child.Name.StartsWith("$", StringComparison.Ordinal)) continue;
                            next.Add(child.FullName);
                        }
                    }
                    catch { } // No access: skip it.
                }
                level = next;
            }
        }

        /// <summary>The installed-programs list.</summary>
        private static void Registered(Action<string, string> report)
        {
            foreach (var hive in new[] { RegistryHive.LocalMachine, RegistryHive.CurrentUser })
                foreach (var view in new[] { RegistryView.Registry32, RegistryView.Registry64 })
                    using (var root = RegistryKey.OpenBaseKey(hive, view))
                    using (var uninstall = root.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall"))
                        if (uninstall != null)
                            foreach (string name in uninstall.GetSubKeyNames())
                                using (var entry = uninstall.OpenSubKey(name))
                                {
                                    string title = entry == null ? null : entry.GetValue("DisplayName") as string;
                                    if (title == null || title.IndexOf("Nun Massacre", StringComparison.OrdinalIgnoreCase) < 0) continue;
                                    report(entry.GetValue("InstallLocation") as string, "INSTALLED");
                                    string icon = entry.GetValue("DisplayIcon") as string;
                                    if (!string.IsNullOrEmpty(icon)) Safe(() => report(Path.GetDirectoryName(icon.Split(',')[0].Trim('"')), "INSTALLED"));
                                }
        }

        private static void Safe(Action action) { try { action(); } catch { } }
    }
}
