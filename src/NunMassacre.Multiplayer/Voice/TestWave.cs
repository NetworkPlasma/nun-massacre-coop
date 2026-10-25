using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;

namespace NunMassacre.Multiplayer
{
    /// <summary>Tests: reading a phrase from a WAV (PCM 16-bit), and recording what the listener hears to one.</summary>
    internal static class TestWave
    {
        /// <summary>A 16-bit PCM WAV, its channels averaged, resampled to this rate.</summary>
        internal static float[] Read(string path, int rate)
        {
            using (var reader = new BinaryReader(File.OpenRead(path)))
            {
                reader.ReadBytes(12);
                int channels = 1, sourceRate = rate, bits = 16;
                byte[] data = null;
                while (reader.BaseStream.Position + 8 <= reader.BaseStream.Length)
                {
                    string id = new string(reader.ReadChars(4)); int size = reader.ReadInt32();
                    if (id == "fmt ")
                    {
                        reader.ReadInt16();
                        channels = reader.ReadInt16();
                        sourceRate = reader.ReadInt32();
                        reader.ReadInt32();
                        reader.ReadInt16();
                        bits = reader.ReadInt16();
                        reader.ReadBytes(size - 16);
                    }
                    else if (id == "data") { data = reader.ReadBytes(size); break; }
                    else reader.ReadBytes(size + (size & 1));
                }
                if (data == null || bits != 16) throw new InvalidDataException("not a 16-bit PCM WAV: " + path);
                int frames = data.Length / 2 / channels;
                var mono = new float[frames];
                for (int f = 0; f < frames; f++)
                {
                    float sum = 0f;
                    for (int c = 0; c < channels; c++) sum += BitConverter.ToInt16(data, (f * channels + c) * 2) / 32768f;
                    mono[f] = sum / channels;
                }
                if (sourceRate == rate) return mono;
                var output = new float[(int)((long)frames * rate / sourceRate)];
                for (int i = 0; i < output.Length; i++)
                {
                    double at = (double)i * sourceRate / rate; int a = (int)at; float t = (float)(at - a);
                    output[i] = Mathf.Lerp(mono[Math.Min(a, frames - 1)], mono[Math.Min(a + 1, frames - 1)], t);
                }
                return output;
            }
        }

        internal static void Write(string path, List<float> samples, int channels, int rate)
        {
            using (var writer = new BinaryWriter(File.Create(path)))
            {
                int bytes = samples.Count * 2;
                writer.Write("RIFF".ToCharArray()); writer.Write(36 + bytes); writer.Write("WAVE".ToCharArray());
                writer.Write("fmt ".ToCharArray());
                writer.Write(16);
                writer.Write((short)1);
                writer.Write((short)channels);
                writer.Write(rate);
                writer.Write(rate * channels * 2);
                writer.Write((short)(channels * 2));
                writer.Write((short)16);
                writer.Write("data".ToCharArray()); writer.Write(bytes);
                foreach (float v in samples) writer.Write((short)Mathf.Clamp(Mathf.RoundToInt(v * 32767f), -32768, 32767));
            }
        }
    }

    /// <summary>Tests: on the audio listener, the whole mix as heard, for a set time, then to a WAV. With Quiet, every
    /// sound but the voices is muted meanwhile (the level's hum, footsteps), so only the voice and its room are heard.</summary>
    internal sealed class ListenerTap : MonoBehaviour
    {
        internal static bool Quiet;
        internal static string[] Keep = new string[0]; // With Quiet, sounds whose path has one of these play on (the one under test).
        private readonly List<float> samples = new List<float>(48000 * 2 * 10);
        private readonly object gate = new object();
        private string path; private float until; private int channels = 2, rate; private Action<string> log; private bool done;

        internal void Begin(string file, float seconds, Action<string> logger)
        {
            path = file;
            until = Time.unscaledTime + seconds;
            log = logger;
            rate = AudioSettings.outputSampleRate;
        }

        private void OnAudioFilterRead(float[] data, int count)
        {
            lock (gate) { if (done) return; channels = count; samples.AddRange(data); }
        }

        private void Update()
        {
            if (Quiet)
                foreach (var source in FindObjectsOfType<AudioSource>())
                    if (source && !source.mute && !source.GetComponent<Voice.VoicePlayback>() && !Keep.Any(k => GraphDump.Path(source.transform).Contains(k)))
                        source.mute = true;
            if (done || Time.unscaledTime < until) return;
            lock (gate) { done = true; TestWave.Write(path, samples, channels, rate); }
            log("TEST listened " + path + " " + (samples.Count / Math.Max(1, channels) / (float)rate).ToString("0.0") + " s");
            Destroy(this);
        }
    }
}
