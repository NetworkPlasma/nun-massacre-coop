using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace NunMassacre.Multiplayer.Players
{
    /// <summary>
    /// A motion-captured clip (the crawl, the walks), converted from Mixamo by scripts/convert-animation.py: per frame,
    /// where each limb points and how the hips, chest, head, hands and feet are turned, in a character frame (facing +Z,
    /// up +Y, right +X). Played in place; the distance the original covers per cycle sets the pace.
    /// </summary>
    [Serializable]
#pragma warning disable 0649 // Fields are populated by JsonCodec.
    internal sealed class AnimClip
    {
        public string name;
        public float fps, legLength, cycleDistance;
        public ClipFrame[] frames;

        [NonSerialized] private int[] marks;
        /// <summary>A jump's key frames, from how high the hips are: leaving the floor, the top, landing, standing again.</summary>
        internal int[] JumpMarks()
        {
            if (marks != null) return marks;
            int n = frames.Length, low = 0, top = 0;
            float stand = frames[0].hips[1];
            for (int i = 0; i < n / 2; i++) if (frames[i].hips[1] < frames[low].hips[1]) low = i;     // Crouching to spring.
            for (int i = 0; i < n; i++) if (frames[i].hips[1] > frames[top].hips[1]) top = i;         // The top of the jump.
            int off = top, down = n - 1, settled = n - 1;
            for (int i = low; i < top; i++) if (frames[i].hips[1] >= stand) { off = i; break; }
            for (int i = top; i < n; i++) if (frames[i].hips[1] <= stand) { down = i; break; }
            int dip = down;
            for (int i = down; i < n; i++) if (frames[i].hips[1] < frames[dip].hips[1]) dip = i;      // Absorbing the landing.
            for (int i = dip; i < n; i++) if (frames[i].hips[1] >= stand - 0.02f) { settled = i; break; }
            return marks = new[] { off, top, down, settled };
        }

        private static readonly Dictionary<string, AnimClip> loaded = new Dictionary<string, AnimClip>();
        /// <summary>An embedded clip by resource name, or null if it is missing or unreadable (the procedural pose is used then).</summary>
        internal static AnimClip Get(string resource)
        {
            AnimClip clip;
            if (loaded.TryGetValue(resource, out clip)) return clip;
            try
            {
                using (var stream = typeof(AnimClip).Assembly.GetManifestResourceStream(resource))
                {
                    if (stream != null)
                        using (var reader = new StreamReader(stream)) clip = JsonCodec.FromJson<AnimClip>(reader.ReadToEnd());
                }
                if (clip == null || clip.frames == null || clip.frames.Length < 2 || clip.legLength <= 0f || clip.fps <= 0f) clip = null;
            }
            catch (Exception) { clip = null; }
            loaded[resource] = clip;
            return clip;
        }
    }

    [Serializable]
    internal sealed class ClipFrame
    {
        public float[] hips, pelvisUp, pelvisLeft, spine, chestUp, chestLeft, neck, headUp, headFace;
        public float[] shoulderL, upperArmL, forearmL, fingersL, palmL, thighL, shinL, toesL, soleL;
        public float[] shoulderR, upperArmR, forearmR, fingersR, palmR, thighR, shinR, toesR, soleR;
    }
#pragma warning restore 0649
}
