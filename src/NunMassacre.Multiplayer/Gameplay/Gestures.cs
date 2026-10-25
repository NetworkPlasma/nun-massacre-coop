using System;
using UnityEngine;

namespace NunMassacre.Multiplayer.Gameplay
{
    /// <summary>
    /// Wordless signals for teammates, on the number keys: 1 points (your character's arm goes out to where you look; no
    /// marker anywhere) and 2 whistles (heard in 3D through the building like a voice, and by the Nun, so it is a risk).
    /// Each is counted in the player's frame, so teammates see and hear it even if a frame is lost.
    /// </summary>
    internal sealed class Gestures
    {
        internal const float WhistleRadius = 30f; // How far the Nun hears a whistle.
        internal static int Points, Whistles;
        private float nextPoint, nextWhistle;
        private AudioSource own;
        private static AudioClip clip;

        internal void Update(Session session)
        {
            if (!session.Active || !session.Local || RuntimeTestDriver.FreezePlayerInput || Time.timeScale <= 0f) return;
            if (Plugin.Instance.Console != null && Plugin.Instance.Console.IsOpen) return;
            var selected = UnityEngine.EventSystems.EventSystem.current ? UnityEngine.EventSystems.EventSystem.current.currentSelectedGameObject : null;
            if (selected && selected.GetComponent<UnityEngine.UI.InputField>()) return; // Typing somewhere.
            var life = session.Life.State;
            if (life == LifeState.Spectating || life == LifeState.GameOver) return;
            if (Input.GetKeyDown(KeyCode.Alpha1) && life != LifeState.Downed) Point();
            if (Input.GetKeyDown(KeyCode.Alpha2)) Whistle(session);
        }

        internal void Point()
        {
            if (Time.unscaledTime < nextPoint) return;
            nextPoint = Time.unscaledTime + 1.2f;
            Points++;
        }

        internal void Whistle(Session session)
        {
            if (Time.unscaledTime < nextWhistle) return;
            nextWhistle = Time.unscaledTime + 1.6f;
            Whistles++;
            // You hear your own whistle, close and dry.
            if (!own)
            {
                var go = new GameObject("NMM_OwnWhistle");
                UnityEngine.Object.DontDestroyOnLoad(go);
                own = go.AddComponent<AudioSource>();
                own.spatialBlend = 0f; own.playOnAwake = false; own.volume = 0.55f;
            }
            own.PlayOneShot(Clip());
            var local = session.Local;
            if (local) session.WhistleNoise(((Component)local).transform.position);
        }

        /// <summary>A two-note call whistle (up, then down), made once: a little breathy, with a slight wobble.</summary>
        internal static AudioClip Clip()
        {
            if (clip) return clip;
            const int rate = 44100;
            float length = 0.72f;
            var samples = new float[(int)(rate * length)];
            var noise = new System.Random(7);
            double phase = 0;
            for (int i = 0; i < samples.Length; i++)
            {
                float t = i / (float)rate;
                float pitch, amp;
                if (t < 0.2f) { pitch = Mathf.Lerp(1850f, 2550f, t / 0.2f); amp = Mathf.Clamp01(t / 0.02f) * Mathf.Clamp01((0.2f - t) / 0.03f); }
                else if (t < 0.27f) { pitch = 2550f; amp = 0f; }
                else { float u = (t - 0.27f) / (length - 0.27f); pitch = Mathf.Lerp(2650f, 1700f,
                    u * u); amp = Mathf.Clamp01((t - 0.27f) / 0.02f) * Mathf.Clamp01((length - t) / 0.08f); }
                pitch += Mathf.Sin(t * 2f * Mathf.PI * 6f) * 35f; // A human wobble.
                phase += 2.0 * Math.PI * pitch / rate;
                float breath = (float)(noise.NextDouble() * 2.0 - 1.0) * 0.05f;
                samples[i] = (float)Math.Sin(phase) * 0.8f * amp + breath * amp;
            }
            clip = AudioClip.Create("NMM_Whistle", samples.Length, 1, rate, false);
            clip.SetData(samples, 0);
            return clip;
        }
    }
}
