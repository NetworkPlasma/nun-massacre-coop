using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace NunMassacre.Multiplayer.Voice
{
    /// <summary>
    /// The game's own sounds, raytraced like the voices: doors, cabinets, lockers, drops, footsteps, the Nun's breathing,
    /// the radio. One reverb follows the listener (Vercidium's way: the space the player is in rings with every sound in it),
    /// set from what the listener's rays find, through a reverb zone that only 3D sounds feed (music, stingers, the rain
    /// and the menus stay dry). The nearest sound sources each get the listener's rays sent after them, before they play,
    /// so a door slammed behind a wall is muffled from its first instant: a low-pass and a level on each, as the rays found.
    /// Sounds the game already muffles itself (the Nun's footsteps and screams) keep the game's own filter.
    /// The game plays many things that happen in the room flat, as if at the player's ears (a door unlocked, glass
    /// broken, a lock picked, a vent's screws, a locker or a cabinet opened: these last from one shared source for the
    /// whole school). In co-op that put a teammate's locker across the building right in your ear. Those are now played
    /// where they happen (the shared ones moved to the cabinet or locker just touched, by anyone), and raytraced like
    /// the rest; music, the rain, cutscenes, the tape's noise, your own body and the items in your hands stay flat.
    /// </summary>
    internal sealed class WorldSounds
    {
        private sealed class Shaped { public AudioLowPassFilter Filter; public SoundShaper Shaper; }
        private sealed class Flat { public float Blend, MaxDistance, ZoneMix; public AudioRolloffMode Rolloff; public AnimationCurve Curve; public Vector3 Position; }

        private const int Most = 32;          // Sources followed by the rays at once: the nearest.
        private const float Reach = 40f;      // And no further than this (or than the source carries).
        private readonly RaytracedAcoustics rays;
        private readonly Dictionary<AudioSource, Shaped> shaped = new Dictionary<AudioSource, Shaped>();
        private readonly Dictionary<AudioSource, float> flat = new Dictionary<AudioSource, float>(); // Flat sounds kept out of the reverb: their own mix.
        private readonly HashSet<AudioSource> followed = new HashSet<AudioSource>();
        // Flat world sounds now played where they happen: as they were.
        private readonly Dictionary<AudioSource, Flat> placed = new Dictionary<AudioSource, Flat>();
        private readonly HashSet<AudioSource> shared = new HashSet<AudioSource>(); // The cabinets' and lockers' one source each: moved to what was touched.
        // What each sound belongs to (a door, a cabinet).
        private readonly Dictionary<AudioSource, Transform> owners = new Dictionary<AudioSource, Transform>();
        private static WorldSounds current;
        private static readonly string[] WorldNames =
        {
            "Unlock Audio Source", "Glass Break Audio Source", "Screwing Audio", "Lock Turn", "Lockpick Break", "Lock Can't Turn", "Insert Pick", "Unlock",
            "Open Audio Source", "Close Audio Source", "Take Off Audio", "Lock Click Audio Source", "Water Pour Audio Source", "Sink Fall Audio Source",
            "Scale Break Audio Source", "Sparks Audio Source", "Clip Wire Audio Source", "Key Unlock Audio Source", "Climb Audio Source", "Electric Audio Source",
            "Lighter Close", "Lighter Strike", "Flame Crakle", "Sizzle", "Creak Audio Source", "Close door Audio Source",
        };
        /// <summary>How a sound played where it happens carries: full within a couple of metres, a third at 8 m, gone by 25 m
        /// (as the game's own door and drop sounds).</summary>
        private static readonly AnimationCurve Carry = new AnimationCurve(new Keyframe(0f, 1f), new Keyframe(0.08f, 1f),
            new Keyframe(0.2f, 0.55f), new Keyframe(0.35f, 0.3f), new Keyframe(0.65f, 0.1f), new Keyframe(1f, 0f));
        private GameObject zoneObject;
        private AudioReverbZone zone;
        private float nextScan;

        internal WorldSounds(RaytracedAcoustics rays) { this.rays = rays; current = this; }

        /// <summary>Something was used (a cabinet, a drawer, a locker; by this player or a teammate): the shared sound
        /// sources play from it.</summary>
        internal static void Touched(Component thing) { if (current != null && thing) current.Touch(thing.transform); }

        private void Touch(Transform thing)
        {
            if (!zone || shared.Count == 0) return;
            var shape = thing.GetComponentInChildren<Renderer>();
            Vector3 at = shape ? shape.bounds.center : thing.position;
            Transform ears = Camera.main ? Camera.main.transform : null;
            foreach (var source in shared)
            {
                if (!source) continue;
                source.transform.position = at;
                owners[source] = thing.root == thing ? thing : Owner(thing);
                followed.Add(source);
                rays.Sound(source, ears ? Outside(at, ears.position, owners[source]) : at);
                Shaped shaping;
                if (!shaped.TryGetValue(source, out shaping) || !shaping.Shaper) continue;
                // Until the rays have been after it at its new place: straight through a wall or not.
                bool walled = ears
                    && Physics.Linecast(ears.position, at, LayerMask.GetMask("Wall", "Ground", "Ceiling", "Door"), QueryTriggerInteraction.Ignore);
                shaping.Shaper.Gain = walled ? 0.25f : 1f;
                shaping.Filter.cutoffFrequency = walled ? 900f : 22000f;
                shaping.Filter.enabled = true; shaping.Shaper.enabled = true;
            }
        }

        internal int Followed { get { return followed.Count; } }
        internal AudioReverbZone Zone { get { return zone; } }

        /// <summary>Each frame: the reverb where the listener is, and how each nearby sound reaches them.</summary>
        internal void Update(bool on, Transform ears, VoiceChat.ReverbParams? here)
        {
            if (!on || !ears || !rays.Running || !here.HasValue) { Off(); return; }
            if (!zone)
            {
                zoneObject = new GameObject("NMM_RaytracedReverb");
                UnityEngine.Object.DontDestroyOnLoad(zoneObject);
                zone = zoneObject.AddComponent<AudioReverbZone>();
                zone.reverbPreset = AudioReverbPreset.User;
                zone.minDistance = 5000f; zone.maxDistance = 5001f; // The listener is always well inside it.
            }
            zoneObject.transform.position = ears.position;
            Apply(here.Value);
            if (Time.unscaledTime >= nextScan) { nextScan = Time.unscaledTime + 0.4f; Scan(ears.position); }
            foreach (var source in followed)
            {
                if (!source) continue;
                Transform owner;
                if (!owners.TryGetValue(source, out owner) || !owner) owners[source] = owner = Owner(source.transform);
                rays.Sound(source, Outside(source.transform.position, ears.position, owner));
                Shaped shape;
                if (!shaped.TryGetValue(source, out shape) || !shape.Shaper) continue;
                VAudio.LowPass filter;
                bool heard = rays.SoundMuffling(source, out filter);
                shape.Shaper.Gain = heard ? Mathf.Clamp01(filter.GainLF) : 1f;
                float cutoff = heard ? VoiceChat.RayCutoff(filter) : 22000f;
                shape.Filter.cutoffFrequency = Mathf.Lerp(shape.Filter.cutoffFrequency, cutoff, 0.25f);
            }
        }

        private void Apply(VoiceChat.ReverbParams p)
        {
            const float k = 0.15f;
            zone.room = Mathf.RoundToInt(Mathf.Lerp(zone.room, p.Room, k));
            zone.roomHF = Mathf.RoundToInt(Mathf.Lerp(zone.roomHF, p.RoomHF, k));
            zone.roomLF = Mathf.RoundToInt(Mathf.Lerp(zone.roomLF, p.RoomLF, k));
            zone.decayTime = Mathf.Lerp(zone.decayTime, p.Decay, k);
            zone.decayHFRatio = Mathf.Lerp(zone.decayHFRatio, p.DecayHFRatio, k);
            zone.reflections = Mathf.RoundToInt(Mathf.Lerp(zone.reflections, p.Reflections, k));
            zone.reflectionsDelay = Mathf.Lerp(zone.reflectionsDelay, p.ReflectionsDelay, k);
            zone.reverb = Mathf.RoundToInt(Mathf.Lerp(zone.reverb, p.Reverb, k));
            zone.reverbDelay = Mathf.Lerp(zone.reverbDelay, p.ReverbDelay, k);
            zone.diffusion = p.Diffusion; zone.density = p.Density;
            zone.HFReference = p.HFReference; zone.LFReference = p.LFReference;
        }

        /// <summary>Flat sounds out of the reverb; the nearest 3D sounds (playing ones first) followed by the rays.</summary>
        private void Scan(Vector3 ear)
        {
            var candidates = new List<KeyValuePair<AudioSource, float>>();
            foreach (var source in UnityEngine.Object.FindObjectsOfType<AudioSource>())
            {
                if (!source || !source.isActiveAndEnabled || source.GetComponent<VoicePlayback>()) continue;
                if (source.spatialBlend < 0.5f && !placed.ContainsKey(source) && InTheWorld(source)) Place(source);
                if (source.spatialBlend < 0.5f)
                {
                    if (!flat.ContainsKey(source)) { flat[source] = source.reverbZoneMix; source.reverbZoneMix = 0f; }
                    continue;
                }
                Shaped shape;
                bool ours = shaped.TryGetValue(source, out shape);
                if (!ours && source.GetComponent<AudioLowPassFilter>()) continue; // The game muffles this one itself.
                float carries = source.rolloffMode == AudioRolloffMode.Logarithmic ? Reach : Mathf.Min(Reach, source.maxDistance + 2f);
                float distance = Vector3.Distance(source.transform.position, ear);
                if (distance > carries) continue;
                if (shared.Contains(source)) distance = -2000f; // Always ready: they move to whatever is touched.
                candidates.Add(new KeyValuePair<AudioSource, float>(source, source.isPlaying ? distance - 1000f : distance));
            }
            var chosen = new HashSet<AudioSource>(candidates.OrderBy(c => c.Value).Take(Most).Select(c => c.Key));
            foreach (var gone in followed.Where(s => !chosen.Contains(s)).ToList())
            {
                followed.Remove(gone);
                rays.ForgetSound(gone);
                Shaped shape;
                if (gone && shaped.TryGetValue(gone, out shape) && shape.Shaper) { shape.Shaper.Gain = 1f; shape.Filter.cutoffFrequency = 22000f; }
            }
            foreach (var source in chosen)
            {
                if (followed.Contains(source)) continue;
                followed.Add(source);
                Shaped shape;
                if (!shaped.TryGetValue(source, out shape) || !shape.Shaper)
                {
                    var filter = source.GetComponent<AudioLowPassFilter>() ?? source.gameObject.AddComponent<AudioLowPassFilter>();
                    filter.cutoffFrequency = 22000f;
                    shaped[source] = new Shaped { Filter = filter, Shaper = source.gameObject.AddComponent<SoundShaper>() };
                }
                else { shape.Filter.enabled = true; shape.Shaper.enabled = true; }
            }
            foreach (var dead in shaped.Keys.Where(s => !s).ToList()) shaped.Remove(dead);
            foreach (var dead in flat.Keys.Where(s => !s).ToList()) flat.Remove(dead);
        }

        /// <summary>
        /// Where the rays should look for a sound: a sound source usually sits inside what makes it (a door's in the door,
        /// a cabinet's in the cabinet), and inside a solid the rays find it walled in (a door 4 m away was silent). So:
        /// just in front of that thing's own surface on the listener's side, where the sound leaves it; a wall between is
        /// still a wall (only the thing's own surfaces are stepped past).
        /// </summary>
        private Vector3 Outside(Vector3 at, Vector3 ear, Transform owner)
        {
            Vector3 line = at - ear;
            float length = line.magnitude;
            if (length < 0.2f) return at;
            Vector3 way = line / length;
            int count = Physics.RaycastNonAlloc(ear, way, hits, length + 0.05f, Mask, QueryTriggerInteraction.Ignore);
            float nearest = length;
            for (int i = 0; i < count; i++)
            {
                var hit = hits[i];
                if (hit.distance < length - 1.2f || !owner || !hit.collider.transform.IsChildOf(owner)) continue; // Only its own, close by.
                if (hit.distance < nearest) nearest = hit.distance;
            }
            return nearest < length ? ear + way * Mathf.Max(0.1f, nearest - 0.12f) : at;
        }
        private readonly RaycastHit[] hits = new RaycastHit[16];
        private static readonly int Mask = LayerMask.GetMask("Wall", "Ground", "Ceiling", "Door", "Furniture");

        /// <summary>The thing a sound belongs to: up from the source to the first level that holds solid parts (a door's
        /// leaf and frame, a cabinet).</summary>
        private static Transform Owner(Transform source)
        {
            Transform owner = source;
            for (int up = 0; up < 3 && owner.parent; up++)
            {
                if (owner.GetComponentsInChildren<Collider>().Any(c => !c.isTrigger && (Mask & (1 << c.gameObject.layer)) != 0)) return owner;
                owner = owner.parent;
            }
            return owner;
        }

        /// <summary>Everything back as the game had it.</summary>
        private void Off()
        {
            if (zoneObject) UnityEngine.Object.Destroy(zoneObject);
            zoneObject = null; zone = null;
            foreach (var source in followed) rays.ForgetSound(source);
            followed.Clear();
            foreach (var pair in shaped)
                if (pair.Key && pair.Value.Shaper)
                {
                    pair.Value.Shaper.Gain = 1f; pair.Value.Shaper.enabled = false;
                    pair.Value.Filter.cutoffFrequency = 22000f; pair.Value.Filter.enabled = false;
                }
            foreach (var pair in flat) if (pair.Key) pair.Key.reverbZoneMix = pair.Value;
            flat.Clear();
            foreach (var pair in placed)
            {
                var source = pair.Key; var was = pair.Value;
                if (!source) continue;
                source.spatialBlend = was.Blend; source.rolloffMode = was.Rolloff; source.maxDistance = was.MaxDistance; source.reverbZoneMix = was.ZoneMix;
                if (was.Curve != null) source.SetCustomCurve(AudioSourceCurveType.CustomRolloff, was.Curve);
                if (shared.Contains(source)) source.transform.position = was.Position;
            }
            placed.Clear(); shared.Clear(); owners.Clear();
        }

        /// <summary>A flat sound of something happening in the room (not music, the weather, a cutscene, the player's own
        /// body or what they hold).</summary>
        private static bool InTheWorld(AudioSource source)
        {
            string path = GraphDump.Path(source.transform);
            if (path.StartsWith("Global/Audio/Furniture/", StringComparison.Ordinal)
                || path.StartsWith("Global/Audio/Audio lockers/", StringComparison.Ordinal))
                return true;
            if (path.StartsWith("Global/", StringComparison.Ordinal) || path.StartsWith("Characters/", StringComparison.Ordinal) || path.Contains(" Item"))
                return false;
            // A door slammed by itself.
            if (source.transform.parent && source.transform.parent.name.StartsWith("Lock Door Scare", StringComparison.Ordinal)) return true;
            return Array.IndexOf(WorldNames, source.name) >= 0;
        }

        private void Place(AudioSource source)
        {
            var was = new Flat { Blend = source.spatialBlend, Rolloff = source.rolloffMode, MaxDistance = source.maxDistance,
                ZoneMix = source.reverbZoneMix, Position = source.transform.position };
            if (source.rolloffMode == AudioRolloffMode.Custom) was.Curve = source.GetCustomCurve(AudioSourceCurveType.CustomRolloff);
            placed[source] = was;
            string path = GraphDump.Path(source.transform);
            if (path.StartsWith("Global/Audio/Furniture/", StringComparison.Ordinal)
                || path.StartsWith("Global/Audio/Audio lockers/", StringComparison.Ordinal))
                shared.Add(source);
            source.spatialBlend = 1f;
            source.rolloffMode = AudioRolloffMode.Custom;
            source.minDistance = 1f; source.maxDistance = 25f;
            source.SetCustomCurve(AudioSourceCurveType.CustomRolloff, Carry);
            source.reverbZoneMix = 1f;
            source.dopplerLevel = 0f;
        }

        /// <summary>Tests: one sound source as the rays and its filters have it.</summary>
        internal string Inspect(AudioSource source, Vector3 ear)
        {
            Shaped shape; shaped.TryGetValue(source, out shape);
            Transform owner; owners.TryGetValue(source, out owner);
            VAudio.LowPass filter;
            bool heard = rays.SoundMuffling(source, out filter);
            return GraphDump.Path(source.transform) + " playing=" + source.isPlaying + " mute=" + source.mute + " vol=" + source.volume.ToString("0.00") + " blend=" + source.spatialBlend.ToString("0.0")
                + " followed=" + followed.Contains(source) + " placed=" + placed.ContainsKey(source) + " d=" + Vector3.Distance(source.transform.position, ear).ToString("0.0")
                + " owner=" + (owner ? GraphDump.Path(owner) : "-") + " target=" + Outside(source.transform.position, ear, owner).ToString("0.00") + " source=" + source.transform.position.ToString("0.00")
                + " rays=" + (heard ? filter.GainLF.ToString("0.000") + "/" + filter.GainHF.ToString("0.000") : "none")
                + (shape != null && shape.Shaper ? " shaper=" + shape.Shaper.Gain.ToString("0.000") + (shape.Shaper.enabled ? "" : "(off)") + " lowpass=" + shape.Filter.cutoffFrequency.ToString("0") + (shape.Filter.enabled ? "" : "(off)") : " not shaped")
                + " filters=" + string.Join(",", source.GetComponents<Behaviour>().Where(c => c is AudioLowPassFilter || c is SoundShaper || c is AudioReverbFilter || c is AudioHighPassFilter || c is AudioEchoFilter || c is AudioDistortionFilter || c is AudioChorusFilter).Select(c => c.GetType().Name + (c.enabled ? "" : "-off")).ToArray());
        }

        internal string Describe()
        {
            return "world sounds: " + followed.Count + " followed (" + followed.Count(s => s && s.isPlaying) + " playing), " + flat.Count + " flat kept dry, " + placed.Count + " flat ones placed in the world (" + shared.Count + " shared)"
                + (zone ? ", reverb room=" + zone.room + " hf=" + zone.roomHF + " decay=" + zone.decayTime.ToString("0.00") + " refl=" + zone.reflections + " late=" + zone.reverb : ", no reverb")
                + " ; " + string.Join(" ; ", followed.Where(s => s && s.isPlaying).Select(s =>
                {
                    Shaped shape; shaped.TryGetValue(s, out shape);
                    return GraphDump.Path(s.transform) + " gain="
                        + (shape != null ? shape.Shaper.Gain.ToString("0.000") + " cutoff=" + shape.Filter.cutoffFrequency.ToString("0") : "-");
                }).ToArray());
        }
    }

    /// <summary>A game sound's level as the rays found it, eased from one buffer to the next (no clicks).</summary>
    internal sealed class SoundShaper : MonoBehaviour
    {
        internal float Gain = 1f;
        private float current = 1f;

        private void OnAudioFilterRead(float[] data, int channels)
        {
            float target = Gain, from = current;
            int frames = data.Length / channels;
            if (frames == 0) return;
            for (int i = 0; i < frames; i++)
            {
                float g = from + (target - from) * (i + 1) / frames;
                for (int c = 0; c < channels; c++) data[i * channels + c] *= g;
            }
            current = target;
        }
    }
}
