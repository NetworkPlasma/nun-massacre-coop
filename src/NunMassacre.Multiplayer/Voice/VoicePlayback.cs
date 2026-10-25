using UnityEngine;

namespace NunMassacre.Multiplayer.Voice
{
    /// <summary>
    /// One teammate's voice. The AudioSource plays a constant clip of 1.0 so Unity's 3D attenuation
    /// and panning apply; OnAudioFilterRead multiplies that by the decoded voice (resampled from
    /// 16 kHz to the output rate). A short jitter buffer absorbs network timing; latency is capped.
    /// </summary>
    internal sealed class VoicePlayback : MonoBehaviour
    {
        private const int Capacity = VoiceCodec.SampleRate * 2;
        /// <summary>The farthest a voice carries, in metres (open space, shouting).</summary>
        internal const float Hearing = 45f;
        /// <summary>Loudness by distance (as a share of <see cref="Hearing"/>).</summary>
        internal static readonly AnimationCurve Rolloff = new AnimationCurve(
            new Keyframe(0f, 1f), new Keyframe(2f / Hearing, 1f), new Keyframe(4f / Hearing, 0.62f), new Keyframe(8f / Hearing, 0.4f),
            new Keyframe(16f / Hearing, 0.21f), new Keyframe(30f / Hearing, 0.08f), new Keyframe(38f / Hearing, 0.035f), new Keyframe(1f, 0f));
        /// <summary>How loud a voice is from this far away (metres), before walls and floors.</summary>
        internal static float Loudness(float metres) { return Mathf.Max(0f, Rolloff.Evaluate(Mathf.Clamp01(metres / Hearing))); }
        private int StartLatency = VoiceCodec.SampleRate * 70 / 1000;  // Buffer before playing.
        private int MaxLatency = VoiceCodec.SampleRate * 350 / 1000;   // Skip ahead beyond this.
        /// <summary>The player's own voice, straight from the microphone: no network to wait for.</summary>
        internal void Prompt() { lock (gate) { StartLatency = VoiceCodec.SampleRate * 30 / 1000; MaxLatency = VoiceCodec.SampleRate * 120 / 1000; } }
        /// <summary>How long the player's own voice waits here, in milliseconds (an echo of it comes this much later).</summary>
        internal const float PromptDelay = 30f;
        private readonly object gate = new object();
        private readonly float[] ring = new float[Capacity];
        private int read, count;
        private bool primed;
        private double fraction;
        private int outputRate = 48000;
        private float level;
        internal float Gain = 1f;
        /// <summary>How loud this voice arrives at the listener's ears for every unit it was spoken at (distance, walls, the
        /// way round), whatever the player's own volume setting: what decides whether it is above the threshold of hearing.</summary>
        internal float Audibility = 1f;
        private float envelope, expand = 1f;
        /// <summary>The threshold of hearing in a quiet, creaking building: a voice arriving quieter than this sinks away
        /// (twice as fast as it fades), so a whisper through a wall or down a corridor is lost while a shout still carries.</summary>
        private const float Threshold = 0.0035f;
        internal AudioSource Source { get; private set; }
        internal AudioLowPassFilter LowPass { get; private set; }
        internal AudioHighPassFilter HighPass { get; private set; } // Thins a voice in a vent.
        internal AudioEchoFilter Echo { get; private set; }         // Metal slap-back in a vent.
        internal AudioReverbFilter Reverb { get; private set; }     // The room around the speaker.
        /// <summary>Smoothed output level, for the talking indicator.</summary>
        internal float Level { get { return level; } }
        internal long PlayedSamples;

        internal static VoicePlayback Create(string name)
        {
            var go = new GameObject(name);
            Object.DontDestroyOnLoad(go);
            var source = go.AddComponent<AudioSource>();
            var playback = go.AddComponent<VoicePlayback>(); // Must follow the source and precede the low-pass.
            playback.LowPass = go.AddComponent<AudioLowPassFilter>();
            playback.LowPass.cutoffFrequency = 22000f;
            playback.HighPass = go.AddComponent<AudioHighPassFilter>();
            playback.HighPass.cutoffFrequency = 10f;
            playback.Echo = go.AddComponent<AudioEchoFilter>();
            playback.Echo.delay = 28f; playback.Echo.decayRatio = 0.5f; playback.Echo.wetMix = 0.55f; playback.Echo.dryMix = 0.9f;
            playback.Echo.enabled = false;
            playback.Reverb = go.AddComponent<AudioReverbFilter>();
            playback.Reverb.reverbPreset = AudioReverbPreset.User;
            playback.Reverb.room = -10000f; // Dry until the room is measured.
            playback.Reverb.diffusion = 100f; playback.Reverb.density = 100f; playback.Reverb.decayHFRatio = 0.6f;
            playback.Source = source;
            playback.outputRate = AudioSettings.outputSampleRate > 0 ? AudioSettings.outputSampleRate : 48000;
            var carrier = AudioClip.Create("NMM_VoiceCarrier", playback.outputRate, 1, playback.outputRate, false);
            var ones = new float[playback.outputRate];
            for (int i = 0; i < ones.Length; i++) ones[i] = 1f;
            carrier.SetData(ones, 0);
            source.clip = carrier;
            source.loop = true;
            source.playOnAwake = false;
            source.spatialBlend = 1f;
            source.reverbZoneMix = 0f; // A voice has its own reverb (the space it is in); not the game sounds' one as well.
            // A voice carries as it does in a real building, as loud as it sounds rather than as the sound pressure falls:
            // full within a couple of metres, about two thirds at 4 m, 40% at 8 m, a fifth across a big room (16 m), still
            // there down a long corridor (30 m: 8%), gone by about 45 m. Walls and floors take more (VoiceChat.Propagation).
            source.rolloffMode = AudioRolloffMode.Custom;
            source.minDistance = 1f;
            source.maxDistance = Hearing;
            source.SetCustomCurve(AudioSourceCurveType.CustomRolloff, Rolloff);
            source.dopplerLevel = 0f;
            source.spread = 30f;
            source.priority = 32;
            source.Play();
            return playback;
        }

        internal void Push(float[] samples, int count)
        {
            lock (gate)
            {
                for (int i = 0; i < count; i++)
                {
                    if (this.count >= Capacity) { read = (read + 1) % Capacity; this.count--; }
                    ring[(read + this.count) % Capacity] = samples[i];
                    this.count++;
                }
                if (this.count > MaxLatency)
                {
                    int skip = this.count - StartLatency;
                    read = (read + skip) % Capacity; this.count -= skip;
                }
            }
        }

        private void OnAudioFilterRead(float[] data, int channels)
        {
            double step = (double)VoiceCodec.SampleRate / outputRate;
            float gain = Gain, peak = 0f, audibility = Audibility;
            float release = (float)System.Math.Exp(-1.0 / (0.08 * outputRate)), follow = 1f - (float)System.Math.Exp(-1.0 / (0.01 * outputRate));
            int frames = data.Length / channels;
            lock (gate)
            {
                if (!primed && count >= StartLatency) primed = true;
                for (int i = 0; i < frames; i++)
                {
                    float sample = 0f;
                    if (primed && count >= 2)
                    {
                        float a = ring[read], b = ring[(read + 1) % Capacity];
                        sample = (a + (b - a) * (float)fraction) * gain;
                        fraction += step;
                        while (fraction >= 1.0 && count > 1) { fraction -= 1.0; read = (read + 1) % Capacity; count--; }
                    }
                    else primed = false; // Ran dry: re-buffer before speaking again.
                    // Below the threshold of hearing the voice sinks away (a downward expander on what reaches the ear).
                    float arriving = (sample < 0 ? -sample : sample) * audibility / System.Math.Max(gain, 1e-4f);
                    envelope = arriving > envelope ? arriving : envelope * release;
                    float wanted = envelope >= Threshold ? 1f : envelope / Threshold;
                    expand += (wanted - expand) * follow;
                    sample *= expand;
                    // A shout through a raised volume eases off instead of clipping.
                    if (sample > 0.85f) sample = 0.85f + 0.15f * (float)System.Math.Tanh((sample - 0.85f) / 0.15f);
                    else if (sample < -0.85f) sample = -0.85f - 0.15f * (float)System.Math.Tanh((-sample - 0.85f) / 0.15f);
                    float magnitude = sample < 0 ? -sample : sample;
                    if (magnitude > peak) peak = magnitude;
                    for (int c = 0; c < channels; c++) data[i * channels + c] *= sample;
                }
                if (primed) PlayedSamples += frames;
            }
            level = Mathf.Max(peak, level * 0.85f);
        }

        internal void Clear() { lock (gate) { read = 0; count = 0; primed = false; } }
    }
}
