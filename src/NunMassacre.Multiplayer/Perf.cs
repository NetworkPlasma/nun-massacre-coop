using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using UnityEngine;

namespace NunMassacre.Multiplayer
{
    /// <summary>Frame-time and sync-cost counters, logged by the test driver with -nmm-perf.</summary>
    internal static class Perf
    {
        internal sealed class Counter
        {
            public int Count; public double Total, Max;
            public void Add(double ms) { Count++; Total += ms; if (ms > Max) Max = ms; }
            public override string ToString() { return Count == 0 ? "-" : (Total / Count).ToString("0.0") + "/" + Max.ToString("0.0") + "ms x" + Count; }
            public void Reset() { Count = 0; Total = Max = 0; }
        }
        internal static readonly Counter Frame = new Counter(), Capture = new Counter(), Apply = new Counter(), Rebuild = new Counter();
        internal static readonly Counter Scan = new Counter(), Serialize = new Counter(), Parse = new Counter(), ApplyObjects = new Counter();
        internal static long SentBytes;

        // Per-section main-thread cost, summed per frame (only measured with -nmm-perf).
        internal static bool Enabled;
        internal static readonly string[] SectionNames = { "mod", "poll", "voice", "players", "worldTick", "life", "enemy", "avatars", "gui" };
        internal const int Mod = 0, Poll = 1, Voice = 2, Players = 3, WorldTick = 4, Life = 5, Enemy = 6, Avatars = 7, Gui = 8;
        private static readonly double[] sectionTotal = new double[SectionNames.Length], sectionMax = new double[SectionNames.Length];
        private static readonly List<double> frames = new List<double>(1024);
        private static int gcStart = -1; private static long heapStart;

        internal static long Begin() { return Enabled ? Stopwatch.GetTimestamp() : 0; }
        internal static void End(int section, long start)
        {
            if (!Enabled) return;
            double ms = (Stopwatch.GetTimestamp() - start) * 1000.0 / Stopwatch.Frequency;
            sectionTotal[section] += ms;
            if (ms > sectionMax[section]) sectionMax[section] = ms;
        }

        internal static double Ms(Stopwatch watch) { return watch.Elapsed.TotalMilliseconds; }
        internal static void Tick()
        {
            double ms = Time.unscaledDeltaTime * 1000.0;
            Frame.Add(ms); frames.Add(ms);
            if (gcStart < 0) { gcStart = GC.CollectionCount(0); heapStart = GC.GetTotalMemory(false); }
        }
        internal static string Report()
        {
            var sorted = frames.OrderBy(x => x).ToList();
            Func<double, string> pct = p => sorted.Count == 0 ? "-" : sorted[Math.Min(sorted.Count - 1, (int)(p * sorted.Count))].ToString("0.0");
            int count = Math.Max(1, frames.Count);
            string sections = string.Join(" ",
                SectionNames.Select((name, i) => name + "=" + (sectionTotal[i] / count).ToString("0.00") + "/" + sectionMax[i].ToString("0.0")).ToArray());
            int gc = GC.CollectionCount(0) - Math.Max(0, gcStart);
            string text = "frame " + Frame + " p50=" + pct(0.5) + " p95=" + pct(0.95) + " p99=" + pct(0.99) + " fps=" + (frames.Count == 0 ? 0 : 1000.0 / (frames.Sum() / frames.Count)).ToString("0")
                + " | gc " + gc + " heap " + (GC.GetTotalMemory(false) / 1048576.0).ToString("0") + "MB (" + ((GC.GetTotalMemory(false) - heapStart) / 1024) + "KB)"
                + " | " + sections
                + " | capture " + Capture + " (scan " + Scan + ", json " + Serialize + ") | apply " + Apply + " (parse " + Parse + ", objects " + ApplyObjects + ") | rebuild " + Rebuild + " | sent " + (SentBytes / 1024) + "KB";
            Frame.Reset();
            Capture.Reset();
            Apply.Reset();
            Rebuild.Reset();
            Scan.Reset();
            Serialize.Reset();
            Parse.Reset();
            ApplyObjects.Reset();
            SentBytes = 0;
            frames.Clear();
            Array.Clear(sectionTotal, 0, sectionTotal.Length); Array.Clear(sectionMax, 0, sectionMax.Length);
            gcStart = GC.CollectionCount(0); heapStart = GC.GetTotalMemory(false);
            return text;
        }
    }
}
