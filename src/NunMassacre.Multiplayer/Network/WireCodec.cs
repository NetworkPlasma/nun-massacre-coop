using System;
using System.IO;
using System.IO.Compression;
using System.Text;

namespace NunMassacre.Multiplayer.Network
{
    internal static class WireCodec
    {
        internal const int MaxDecodedBytes = 32 * 1024 * 1024;
        internal static byte[] Encode(string json)
        {
            byte[] raw = Encoding.UTF8.GetBytes(json);
            if (raw.Length > MaxDecodedBytes) throw new InvalidDataException("World message exceeds decoded size limit.");
            using (var output = new MemoryStream())
            {
                bool compress = raw.Length > 1024;
                output.WriteByte(compress ? (byte)1 : (byte)0);
                if (compress)
                {
                    using (var deflate = new DeflateStream(output, CompressionLevel.Fastest, true)) deflate.Write(raw, 0, raw.Length);
                }
                else output.Write(raw, 0, raw.Length);
                return output.ToArray();
            }
        }
        internal static string Decode(byte[] payload)
        {
            if (payload == null || payload.Length < 1) throw new InvalidDataException("Empty message.");
            if (payload[0] == 0)
            {
                if (payload.Length - 1 > MaxDecodedBytes) throw new InvalidDataException("Message too large.");
                return Encoding.UTF8.GetString(payload, 1, payload.Length - 1);
            }
            if (payload[0] != 1) throw new InvalidDataException("Unknown message encoding.");
            using (var input = new MemoryStream(payload, 1, payload.Length - 1))
            using (var deflate = new DeflateStream(input, CompressionMode.Decompress))
            using (var output = new MemoryStream())
            {
                byte[] buffer = new byte[16384];
                int count;
                while ((count = deflate.Read(buffer, 0, buffer.Length)) > 0)
                {
                    if (output.Length + count > MaxDecodedBytes) throw new InvalidDataException("Decoded message limit exceeded.");
                    output.Write(buffer, 0, count);
                }
                return Encoding.UTF8.GetString(output.ToArray());
            }
        }
    }
}
