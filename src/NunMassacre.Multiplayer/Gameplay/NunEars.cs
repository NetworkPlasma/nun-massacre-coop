using System.Collections.Generic;
using UnityEngine;

namespace NunMassacre.Multiplayer.Gameplay
{
    internal enum SoundKind { Noise, Footstep, Voice, Shout, Whistle, Gunshot }

    /// <summary>
    /// A sound as the Nun heard it. Everything is fixed at the moment it was made: she goes to where a voice was, not to
    /// where its speaker is now.
    /// </summary>
    internal sealed class HeardSound
    {
        public SoundKind Kind;
        public int Source;      // The player who made it; 0 when unknown (a thrown object, a door).
        public Vector3 Origin;  // Where it was made.
        public float Distance;  // Straight line from her ears.
        public float Through;   // How much of it she made out through the building (0..1).
        public bool Traced;     // Through came from her raytraced ears rather than the fallback.
        public float Strength;  // How far over the edge of her hearing it was: 1 is barely, 2 is twice what she needs.
        public bool UnderBed;   // Made by a player hiding under a bed.
        public float Time;

        public override string ToString()
        {
            return Kind + (Source > 0 ? " from peer " + Source : "") + " " + Distance.ToString("0.0") + "m away, through "
                + Through.ToString("0.00") + (Traced ? " (rays)" : " (walls)") + ", strength " + Strength.ToString("0.00") + (UnderBed ? ", under a bed" : "");
        }
    }

    /// <summary>
    /// The Nun's hearing, on the host. Sounds reach her through the building on the same raytraced acoustics the players
    /// hear with: her own emitter in the raytracer sends rays to every player's mouth and to wherever a noise is made, and
    /// what comes back is how much of the sound got to her. Noises wait a moment for those rays; voices use the probe that
    /// already follows each player. Without the raytracer the mod's simpler wall model stands in. With no co-op game
    /// running her hearing is the game's own. What she then does about a sound is up to EnemyDirector.
    /// </summary>
    internal sealed class NunEars
    {
        private readonly Session session;
        internal NunEars(Session session) { this.session = session; }

        // Noises are weighed against reach (metres); voices against level (RMS reaching her ears).
        internal static float Sharp = 0.4f, SharpVoice = 0.6f; // Exponents: how much she makes out of what gets through.
        private const float RayWait = 0.6f;       // How long a noise waits for her rays before the fallback decides.
        private const float RightBeside = 1.5f;    // Closer than this nothing is in the way.
        internal const float Whisper = 0.02f;      // The quietest voice she can ever hear.
        private const float SpeechSeconds = 0.25f; // How long a sound has to last to count as speech.

        /// <summary>Set around a call into the game's noise system to say what the noise is.</summary>
        internal static SoundKind NextKind = SoundKind.Noise;
        internal static int NextSource;

        private sealed class Pending { public HeardSound Sound; public float Reach, Since; public int Probe; }
        private readonly List<Pending> pending = new List<Pending>();
        private int nextProbe = -1; // Noise probes count down from -1; a player's probe is their id.

        private sealed class Talker { public float Floor = -1f, Sustained, LastPacket; }
        private readonly Dictionary<int, Talker> talkers = new Dictionary<int, Talker>();

        /// <summary>How much further than in the game alone a noise carries to her in the open.</summary>
        internal static float Reach(DifficultyLevel level)
        {
            switch (level)
            {
                case DifficultyLevel.Explore: return 1f;
                case DifficultyLevel.Easy: return 1.25f;
                case DifficultyLevel.Hard: return 2f;
                case DifficultyLevel.Extreme: return 2.5f;
                default: return 1.6f;
            }
        }

        /// <summary>The quietest voice (RMS at her ears, after distance and walls) she notices.</summary>
        internal static float VoiceThreshold(DifficultyLevel level)
        {
            switch (level)
            {
                case DifficultyLevel.Explore:
                case DifficultyLevel.Easy: return 0.03f;
                case DifficultyLevel.Hard: return 0.008f;
                case DifficultyLevel.Extreme: return 0.005f;
                default: return 0.012f;
            }
        }

        private Voice.RaytracedAcoustics Rays { get { return session.Voice == null ? null : session.Voice.Raytraced; } }
        internal bool Raytraced { get { var rays = Rays; return rays != null && rays.Running; } }
        internal float Sharpness { get { return Reach(DifficultyController.CurrentDifficulty); } }
        internal bool Active { get { return session.Active && session.IsHost && session.Ready && session.CoopHearing; } }

        internal void Update()
        {
            var ear = session.NunEar;
            var rays = Rays;
            if (!Active || !ear.HasValue)
            {
                foreach (var noise in pending) Forget(noise);
                pending.Clear();
                return;
            }
            if (rays != null && rays.Running)
            {
                rays.NunEar(ear.Value);
                foreach (var frame in session.Players.Values)
                    if (frame != null && frame.body && !frame.spectating && frame.scene == session.SceneName) rays.Probe(frame.id, Mouth(frame));
            }
            for (int i = pending.Count - 1; i >= 0; i--)
            {
                var noise = pending[i];
                float through; bool traced;
                if (!Through(noise.Probe, noise.Sound.Origin, ear.Value, Time.time - noise.Since > RayWait, Sharp, out through, out traced)) continue;
                pending.RemoveAt(i);
                Forget(noise);
                var sound = noise.Sound;
                sound.Distance = Vector3.Distance(sound.Origin, ear.Value);
                sound.Through = through; sound.Traced = traced;
                sound.Strength = noise.Reach * through / Mathf.Max(0.5f, sound.Distance);
                Deliver(sound);
            }
        }

        /// <summary>
        /// The game's noise system calls this for every noise (GameHooks). True lets it through as it is (no co-op game
        /// running here); false holds it while her rays go out, after which it reaches EnemyDirector if she heard it.
        /// </summary>
        internal bool Judge(Vector3 at, ref float radius)
        {
            if (!Active) return true;
            var ear = session.NunEar;
            var sound = new HeardSound { Kind = NextKind, Source = NextSource, Origin = at, Time = Time.time };
            float reach = radius * Sharpness;
            if (!ear.HasValue) { radius = reach; return true; }
            float distance = Vector3.Distance(at, ear.Value);
            if (distance > reach) return false; // Out of reach even in the open.
            if (distance < RightBeside)
            {
                sound.Distance = distance; sound.Through = 1f; sound.Strength = reach / Mathf.Max(0.5f, distance);
                Deliver(sound);
                return false;
            }
            if (pending.Count >= 16) { Forget(pending[0]); pending.RemoveAt(0); }
            int probe = nextProbe--;
            if (nextProbe < -1000000) nextProbe = -1;
            var rays = Rays;
            if (rays != null && rays.Running) rays.Probe(probe, at + Vector3.up * 0.3f);
            pending.Add(new Pending { Sound = sound, Reach = reach, Since = Time.time, Probe = probe });
            return false;
        }

        /// <summary>
        /// The level a player's voice counts at. Steady background (a fan, mains hum) is tracked as a floor and taken off,
        /// and only sound lasting a quarter of a second counts, so a key click or a knock on the microphone does not.
        /// </summary>
        internal float Speech(int peer, float rms)
        {
            Talker talker;
            if (!talkers.TryGetValue(peer, out talker)) talkers[peer] = talker = new Talker();
            float now = Time.unscaledTime, dt = Mathf.Clamp(now - talker.LastPacket, 0f, 0.5f);
            talker.LastPacket = now;
            // The floor drops straight to anything quieter and creeps up over about ten seconds of steady sound.
            if (talker.Floor < 0f || rms < talker.Floor) talker.Floor = rms;
            else talker.Floor += (rms - talker.Floor) * Mathf.Min(1f, dt * 0.1f);
            float level = Mathf.Max(0f, rms - talker.Floor * 1.5f);
            talker.Sustained = level >= Whisper ? talker.Sustained + dt : Mathf.Max(0f, talker.Sustained - dt * 2f);
            return talker.Sustained >= SpeechSeconds ? level : 0f;
        }

        /// <summary>A player's voice at this level, as it reaches her; null if she cannot hear it.</summary>
        internal HeardSound HearVoice(PlayerFrame frame, float level, SoundKind kind)
        {
            var ear = session.NunEar;
            if (!ear.HasValue || frame == null || frame.scene != session.SceneName || frame.spectating || level < Whisper) return null;
            Vector3 mouth = Mouth(frame);
            float distance = Vector3.Distance(mouth, ear.Value);
            if (distance > Voice.VoicePlayback.Hearing) return null;
            float through; bool traced;
            Through(frame.id, mouth, ear.Value, true, SharpVoice, out through, out traced);
            float reaching = level * Voice.VoicePlayback.Loudness(distance) * through;
            float threshold = VoiceThreshold(DifficultyController.CurrentDifficulty);
            if (Session.TestHearingLog)
                session.Log("Nun ears: peer " + frame.id + " " + kind + " " + distance.ToString("0.0") + "m level " + level.ToString("0.000") + " through " + through.ToString("0.00")
                    + (traced ? " (rays)" : " (walls)") + " => " + reaching.ToString("0.0000") + " (needs " + threshold.ToString("0.000") + ")");
            if (reaching < threshold) return null;
            return new HeardSound
            {
                Kind = kind, Source = frame.id, Origin = frame.position, Distance = distance, Through = through, Traced = traced,
                Strength = reaching / threshold, UnderBed = frame.state == (int)GrannyClone.PlayerStates.States.Hiding, Time = Time.time
            };
        }

        private void Deliver(HeardSound sound)
        {
            if (sound.Strength < 1f) { if (Session.TestHearingLog) session.Log("Nun ears: missed " + sound); return; }
            PlayerFrame frame;
            if (sound.Source > 0 && session.Players.TryGetValue(sound.Source, out frame))
                sound.UnderBed = frame.state == (int)GrannyClone.PlayerStates.States.Hiding;
            session.NunHeard(sound);
        }

        /// <summary>
        /// What gets from a place to her ears: her rays' answer once it is back, else (when it is too late to wait) the
        /// fallback. She makes out more than gets through: the share raised to Sharp (0.4 for noises, so a floor that lets a
        /// tenth through still gives her two fifths of a thump) or SharpVoice (0.6, since words carry worse than thumps).
        /// </summary>
        private bool Through(int probe, Vector3 at, Vector3 ear, bool late, float sharp, out float through, out bool traced)
        {
            through = 0f; traced = false;
            var rays = Rays;
            Voice.VAudio.LowPass filter;
            if (rays != null && rays.Running && rays.HeardByNun(probe, out filter))
            {
                traced = true;
                through = Mathf.Pow(Mathf.Clamp01(Mathf.Max(filter.GainLF, filter.GainHF)), sharp);
                return true;
            }
            if (!late && rays != null && rays.Running) return false;
            through = Mathf.Pow(Mathf.Clamp01(Voice.VoiceChat.Through(at + Vector3.up * 0.3f, ear)), sharp);
            return true;
        }

        private void Forget(Pending noise) { var rays = Rays; if (rays != null) rays.ForgetProbe(noise.Probe); }

        private static Vector3 Mouth(PlayerFrame frame)
        {
            return frame.position + Vector3.up * (frame.downed ? 0.6f : frame.crouching || frame.vent ? 1.15f : 1.6f);
        }

        internal void Reset() { talkers.Clear(); pending.Clear(); }

        internal string Describe()
        {
            return "nun ears: " + (Active ? "listening" : "off") + ", " + DifficultyController.CurrentDifficulty + " reach x" + Sharpness.ToString("0.00")
                + ", voice threshold " + VoiceThreshold(DifficultyController.CurrentDifficulty).ToString("0.000") + ", " + (Raytraced ? "raytraced" : "fallback acoustics") + ", " + pending.Count + " waiting";
        }
    }
}
