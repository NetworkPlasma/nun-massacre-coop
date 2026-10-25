using System;

namespace NunMassacre.Multiplayer.Voice
{
    /// <summary>
    /// Voice packets: 16 kHz mono speech compressed 4:1 with IMA ADPCM (64 kbit/s), plain managed
    /// code with no native dependency. Each packet carries the codec state it starts from, so
    /// packets decode independently.
    ///
    /// Layout: [0] PacketType, [1-2] speaker peer (little endian, set by the host when relaying),
    /// [3-4] sequence, [5] flags, [6-7] predictor, [8] step index, then two 4-bit samples per byte.
    /// </summary>
    internal static class VoiceCodec
    {
        internal const byte PacketType = 2; // Distinct from WireCodec's 0 (raw JSON) and 1 (deflated JSON).
        internal const int SampleRate = 16000;
        internal const int FrameSamples = 320; // 20 ms
        internal const int HeaderBytes = 9;
        internal const int MaxSamples = FrameSamples * 4;
        internal const byte FlagLoud = 1, FlagEnd = 2;

        private static readonly int[] IndexTable = { -1, -1, -1, -1, 2, 4, 6, 8, -1, -1, -1, -1, 2, 4, 6, 8 };
        private static readonly int[] StepTable =
        {
            7, 8, 9, 10, 11, 12, 13, 14, 16, 17, 19, 21, 23, 25, 28, 31, 34, 37, 41, 45, 50, 55, 60, 66, 73, 80, 88, 97, 107, 118,
            130, 143, 157, 173, 190, 209, 230, 253, 279, 307, 337, 371, 408, 449, 494, 544, 598, 658, 724, 796, 876, 963, 1060,
            1166, 1282, 1411, 1552, 1707, 1878, 2066, 2272, 2499, 2749, 3024, 3327, 3660, 4026, 4428, 4871, 5358, 5894, 6484,
            7132, 7845, 8630, 9493, 10442, 11487, 12635, 13899, 15289, 16818, 18500, 20350, 22385, 24623, 27086, 29794, 32767
        };

        internal struct State { public int Predictor, Index; }

        /// <summary>Encodes samples (-1..1, even count) into a packet; the encoder state carries on.</summary>
        internal static byte[] Encode(float[] samples, int count, ref State state, int sequence, byte flags)
        {
            count &= ~1;
            var packet = new byte[HeaderBytes + count / 2];
            packet[0] = PacketType;
            packet[3] = (byte)sequence; packet[4] = (byte)(sequence >> 8);
            packet[5] = flags;
            packet[6] = (byte)state.Predictor; packet[7] = (byte)(state.Predictor >> 8);
            packet[8] = (byte)state.Index;
            for (int i = 0; i < count; i += 2)
            {
                int low = EncodeSample(ToShort(samples[i]), ref state);
                int high = EncodeSample(ToShort(samples[i + 1]), ref state);
                packet[HeaderBytes + i / 2] = (byte)(low | (high << 4));
            }
            return packet;
        }

        /// <summary>Decodes a packet into output (-1..1). Returns the sample count, or -1 if invalid.</summary>
        internal static int Decode(byte[] packet, float[] output, out byte flags)
        {
            flags = 0;
            if (packet == null || packet.Length < HeaderBytes || packet[0] != PacketType) return -1;
            int count = (packet.Length - HeaderBytes) * 2;
            if (count > output.Length || count > MaxSamples) return -1;
            flags = packet[5];
            var state = new State { Predictor = (short)(packet[6] | (packet[7] << 8)), Index = packet[8] };
            if (state.Index > 88) return -1;
            for (int i = 0; i < count; i += 2)
            {
                int value = packet[HeaderBytes + i / 2];
                output[i] = DecodeSample(value & 15, ref state) / 32768f;
                output[i + 1] = DecodeSample(value >> 4, ref state) / 32768f;
            }
            return count;
        }

        internal static int Speaker(byte[] packet) { return packet[1] | (packet[2] << 8); }
        internal static void SetSpeaker(byte[] packet, int peer) { packet[1] = (byte)peer; packet[2] = (byte)(peer >> 8); }

        private static short ToShort(float value)
        {
            return (short)Math.Max(-32768, Math.Min(32767, (int)Math.Round(value * 32767f)));
        }

        private static int EncodeSample(int sample, ref State state)
        {
            int step = StepTable[state.Index];
            int difference = sample - state.Predictor;
            int code = 0;
            if (difference < 0) { code = 8; difference = -difference; }
            int delta = step >> 3;
            if (difference >= step) { code |= 4; difference -= step; delta += step; }
            step >>= 1;
            if (difference >= step) { code |= 2; difference -= step; delta += step; }
            step >>= 1;
            if (difference >= step) { code |= 1; delta += step; }
            Advance(code, delta, ref state);
            return code;
        }

        private static int DecodeSample(int code, ref State state)
        {
            int step = StepTable[state.Index];
            int delta = step >> 3;
            if ((code & 4) != 0) delta += step;
            if ((code & 2) != 0) delta += step >> 1;
            if ((code & 1) != 0) delta += step >> 2;
            Advance(code, delta, ref state);
            return state.Predictor;
        }

        private static void Advance(int code, int delta, ref State state)
        {
            state.Predictor += (code & 8) != 0 ? -delta : delta;
            if (state.Predictor > 32767) state.Predictor = 32767; else if (state.Predictor < -32768) state.Predictor = -32768;
            state.Index += IndexTable[code];
            if (state.Index < 0) state.Index = 0; else if (state.Index > 88) state.Index = 88;
        }
    }
}
