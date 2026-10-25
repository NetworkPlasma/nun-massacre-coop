using System;
using System.IO;
using System.IO.Compression;
using NunMassacre.Multiplayer;
using NunMassacre.Multiplayer.Network;
using NunMassacre.Multiplayer.World;
using UnityEngine;

internal static class Program
{
    private static int Main()
    {
        try
        {
            var packet = new Packet { type = "players", epoch = 7,
                players = new[] { new PlayerFrame { id = 2, name = "Zoë", health = 53, position = new Vector3(1, 2, 3), rotation = Quaternion.identity } },
                owners = new[] { new Ownership { id = "scene/item", peer = 2 } } };
            string encoded = JsonCodec.ToJson(packet);
            var result = JsonCodec.FromJson<Packet>(WireCodec.Decode(WireCodec.Encode(encoded)));
            Assert(result.players.Length == 1 && result.players[0].position.y == 2 && result.players[0].health == 53 && result.players[0].name == "Zoë" && result.owners[0].peer == 2,
                "Nested player and ownership data round trip");
            var world = new WorldSnapshot { scene = "school",
                objects = new[] { new ObjectSnapshot { id = "door", active = false, rotation = Quaternion.identity, components = new[] { new ComponentSnapshot { id = "door::collider", kind = "collider", enabled = false } } } } };
            var restored = JsonCodec.FromJson<WorldSnapshot>(JsonCodec.ToJson(world));
            Assert(restored.version == 1 && restored.objects.Length == 1 && !restored.objects[0].active && !restored.objects[0].components[0].enabled,
                "Nested world state and false/default fields preserved");
            string large = new string('x', 12 * 1024 * 1024);
            byte[] compressed = WireCodec.Encode(large);
            Assert(compressed.Length < 1024 * 1024 && WireCodec.Decode(compressed) == large, "Full-level-sized message compressed and restored");
            Throws(() => WireCodec.Decode(new byte[] { 3, 1, 2 }), "Unknown wire encoding rejected");
            Throws(() => WireCodec.Encode(new string('x', WireCodec.MaxDecodedBytes + 1)), "Oversized raw message rejected");
            using (var payload = new MemoryStream())
            {
                payload.WriteByte(1);
                using (var deflate = new DeflateStream(payload, CompressionLevel.Fastest, true))
                {
                    var block = new byte[1024 * 1024];
                    for (int i = 0; i < 33; i++) deflate.Write(block, 0, block.Length);
                }
                Throws(() => WireCodec.Decode(payload.ToArray()), "Decompression expansion limit enforced");
            }
            var injected = JsonCodec.FromJson<Packet>("{\"$type\":\"System.IO.FileInfo, mscorlib\",\"type\":\"ping\"}");
            Assert(injected.type == "ping", "Network type metadata cannot instantiate arbitrary types");
            var life = new Packet { type = "player",
                player = new PlayerFrame { id = 3, name = "B", downed = true, bleed = 31.5f, spectating = false, reviving = 2, revive = 0.75f, body = true, velocity = new Vector3(1.5f, 0, -2), rotation = Quaternion.identity } };
            var lifeBack = JsonCodec.FromJson<Packet>(WireCodec.Decode(WireCodec.Encode(JsonCodec.ToJson(life)))).player;
            Assert(lifeBack.downed && lifeBack.bleed == 31.5f && !lifeBack.spectating && lifeBack.reviving == 2 && lifeBack.revive == 0.75f && lifeBack.body && lifeBack.velocity.z == -2,
                "Knockdown, revive and movement state round trip");
            var older = JsonCodec.FromJson<Packet>("{\"type\":\"hello\",\"protocol\":1}");
            Assert(older.protocol == 1 && older.protocol != Packet.Protocol, "Older protocol versions are detectable for rejection");
            // Voice: ADPCM keeps speech-band audio intelligible (SNR) at 4 bits per sample.
            var tone = new float[NunMassacre.Multiplayer.Voice.VoiceCodec.FrameSamples * 2];
            for (int i = 0; i < tone.Length; i++)
                tone[i] = 0.4f * (float)(Math.Sin(i * 2 * Math.PI * 220 / 16000) * 0.7 + Math.Sin(i * 2 * Math.PI * 470 / 16000) * 0.3);
            var state = new NunMassacre.Multiplayer.Voice.VoiceCodec.State();
            byte[] voice = NunMassacre.Multiplayer.Voice.VoiceCodec.Encode(tone, tone.Length, ref state, 5, NunMassacre.Multiplayer.Voice.VoiceCodec.FlagLoud);
            NunMassacre.Multiplayer.Voice.VoiceCodec.SetSpeaker(voice, 300);
            var heard = new float[NunMassacre.Multiplayer.Voice.VoiceCodec.MaxSamples];
            byte voiceFlags;
            int samples = NunMassacre.Multiplayer.Voice.VoiceCodec.Decode(voice, heard, out voiceFlags);
            double signal = 0, noise = 0;
            for (int i = 64; i < samples; i++) { signal += tone[i] * tone[i]; noise += (tone[i] - heard[i]) * (tone[i] - heard[i]); }
            double snr = 10 * Math.Log10(signal / Math.Max(noise, 1e-12));
            Assert(samples == tone.Length && voice.Length == NunMassacre.Multiplayer.Voice.VoiceCodec.HeaderBytes + tone.Length / 2 && voiceFlags == NunMassacre.Multiplayer.Voice.VoiceCodec.FlagLoud
                && NunMassacre.Multiplayer.Voice.VoiceCodec.Speaker(voice) == 300 && snr > 20, "Voice codec round trip, 4:1 size, SNR " + snr.ToString("0.0") + " dB");
            var hostile = new byte[NunMassacre.Multiplayer.Voice.VoiceCodec.HeaderBytes + 10];
            hostile[0] = NunMassacre.Multiplayer.Voice.VoiceCodec.PacketType;
            hostile[8] = 200;
            Assert(NunMassacre.Multiplayer.Voice.VoiceCodec.Decode(hostile, heard, out voiceFlags) == -1, "Corrupt voice codec state rejected");
            Console.WriteLine("PASS: 11 protocol tests");
            return 0;
        }
        catch (Exception ex) { Console.Error.WriteLine(ex); return 1; }
    }
    private static void Assert(bool condition, string message) { if (!condition) throw new Exception(message); Console.WriteLine("PASS " + message); }
    private static void Throws(Action action, string message)
    {
        try { action(); } catch (InvalidDataException) { Console.WriteLine("PASS " + message); return; }
        throw new Exception(message);
    }
}
