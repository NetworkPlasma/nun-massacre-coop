using System;
using System.Drawing;
using System.Drawing.Text;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Security.Cryptography;

namespace NunMassacreCoop.Setup
{
    /// <summary>
    /// The game's own fonts, read from the player's copy of the game (never shipped here): VCR OSD Mono, the tape-deck
    /// lettering of its on-screen text, and Glass Houses, the leaning streaked capitals of its NUN MASSACRE title. The
    /// build the mod is made for keeps each one's file whole inside its Font asset; it is read from there and checked.
    /// Until a copy of the game is found, Courier New and Impact stand in.
    /// </summary>
    internal static class GameFonts
    {
        private sealed class Source
        {
            public string File; public long Offset; public int Length; public string Hash, Family;
        }
        private static readonly Source Screen = new Source { File = "sharedassets0.assets", Offset = 81964, Length = 24684,
            Hash = "A8C4AC57E131C416FD5C3DE2DEBE281FCA70AEE2D60E90F61FCF5DE7F1F870EE", Family = "VCR OSD Mono" };
        private static readonly Source Title = new Source { File = "sharedassets1.assets", Offset = 7566552, Length = 24384,
            Hash = "6B8C1634A128AAA49857251BF89663C047DA85DDBF6448C215BBA16B696FDDDF", Family = "Glass Houses" };
        private static readonly PrivateFontCollection fonts = new PrivateFontCollection();
        private static FontFamily ui, title;

        /// <summary>True once the game's fonts are in use.</summary>
        public static bool Loaded { get { return ui != null; } }
        /// <summary>The logo's lettering is the game's own (then it is already slanted, and set tall like the title).</summary>
        public static bool GameTitle { get { return title != null; } }

        public static FontFamily Ui { get { return ui ?? new FontFamily("Courier New"); } }
        public static FontFamily TitleFamily { get { return title ?? Art.Family(); } }

        public static Font UiFont(float pixels, bool bold = false)
        {
            // VCR OSD Mono has one weight: it is bold enough as it is.
            return new Font(Ui, pixels, ui != null ? FontStyle.Regular : bold ? FontStyle.Bold : FontStyle.Regular, GraphicsUnit.Pixel);
        }

        /// <summary>Reads the fonts from this copy of the game; false (and nothing changes) if they are not where expected.</summary>
        public static bool Load(string gameDirectory)
        {
            if (Loaded) return true;
            byte[] screen = Read(gameDirectory, Screen), logo = Read(gameDirectory, Title);
            if (screen == null) return false;
            Add(screen);
            if (logo != null) Add(logo);
            ui = fonts.Families.FirstOrDefault(f => f.Name == Screen.Family);
            title = fonts.Families.FirstOrDefault(f => f.Name == Title.Family);
            return ui != null;
        }

        private static byte[] Read(string gameDirectory, Source source)
        {
            try
            {
                string path = Path.Combine(gameDirectory, "Nun Massacre_Data", source.File);
                if (!File.Exists(path)) return null;
                var data = new byte[source.Length];
                using (var stream = File.OpenRead(path))
                {
                    if (stream.Length < source.Offset + source.Length) return null;
                    stream.Position = source.Offset;
                    int read = 0;
                    while (read < data.Length) { int n = stream.Read(data, read, data.Length - read); if (n <= 0) return null; read += n; }
                }
                using (var sha = SHA256.Create())
                    return BitConverter.ToString(sha.ComputeHash(data)).Replace("-", "") == source.Hash ? data : null;
            }
            catch { return null; }
        }

        /// <summary>For GDI+ (drawn text, the lettering) and for GDI (labels): the memory stays for the program's life.</summary>
        private static void Add(byte[] data)
        {
            IntPtr memory = Marshal.AllocCoTaskMem(data.Length);
            Marshal.Copy(data, 0, memory, data.Length);
            fonts.AddMemoryFont(memory, data.Length);
            uint count = 0;
            AddFontMemResourceEx(memory, (uint)data.Length, IntPtr.Zero, ref count);
        }

        [DllImport("gdi32.dll")] private static extern IntPtr AddFontMemResourceEx(IntPtr font, uint length, IntPtr reserved, ref uint fonts);
    }
}
