using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;

namespace NunMassacre.Multiplayer.Network
{
    /// <summary>
    /// LAN game browser. Hosts broadcast a small UDP beacon once a second; the multiplayer menu
    /// listens while it is open and lists the games it hears. Beacons carry no secrets and are
    /// only used to fill in an address to join.
    /// </summary>
    internal sealed class LanDiscovery : IDisposable
    {
        internal const int Port = 27846;
        private const string Magic = "NMM2";

        internal sealed class Game
        {
            public string Address, Name, Build;
            public int Port, Players, Max;
            public bool InGame;
            public DateTime Seen;
        }

        private readonly object gate = new object();
        private readonly Dictionary<string, Game> games = new Dictionary<string, Game>(StringComparer.Ordinal);
        private UdpClient sender, listener;
        private bool disposed;

        /// <summary>Host: announce the game. Called about once a second.</summary>
        internal void Advertise(string name, int port, int players, int max, bool inGame, string build)
        {
            try
            {
                if (sender == null) sender = new UdpClient { EnableBroadcast = true };
                string text = string.Join("|",
                    new[] { Magic, port.ToString(CultureInfo.InvariantCulture), players.ToString(CultureInfo.InvariantCulture), max.ToString(CultureInfo.InvariantCulture), inGame ? "1" : "0", build.Substring(0, Math.Min(12, build.Length)), Clean(name) });
                byte[] data = Encoding.UTF8.GetBytes(text);
                sender.Send(data, data.Length, new IPEndPoint(IPAddress.Broadcast, Port));
                sender.Send(data, data.Length, new IPEndPoint(IPAddress.Loopback, Port)); // Second copy on this PC.
            }
            catch (Exception) { CloseSender(); } // No network: nothing to announce to.
        }

        internal void StopAdvertising() { CloseSender(); }

        internal void Listen()
        {
            if (listener != null || disposed) return;
            try
            {
                var socket = new UdpClient();
                socket.ExclusiveAddressUse = false;
                socket.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
                socket.Client.Bind(new IPEndPoint(IPAddress.Any, Port));
                listener = socket;
                listener.BeginReceive(Received, socket);
            }
            catch (Exception) { StopListening(); }
        }

        internal void StopListening()
        {
            var socket = listener;
            listener = null;
            if (socket != null) try { socket.Close(); } catch (Exception) { }
            lock (gate) games.Clear();
        }

        /// <summary>Games heard in the last few seconds that run the same game build.</summary>
        internal List<Game> Games(string build)
        {
            string prefix = build.Substring(0, Math.Min(12, build.Length));
            lock (gate)
            {
                foreach (var stale in games.Where(g => (DateTime.UtcNow - g.Value.Seen).TotalSeconds > 4).Select(g => g.Key).ToList()) games.Remove(stale);
                return games.Values.Where(g => g.Build == prefix).OrderBy(g => g.Name, StringComparer.OrdinalIgnoreCase).ToList();
            }
        }

        private void Received(IAsyncResult result)
        {
            var socket = (UdpClient)result.AsyncState;
            try
            {
                IPEndPoint from = null;
                byte[] data = socket.EndReceive(result, ref from);
                Parse(data, from);
            }
            catch (Exception) { }
            if (socket != listener) return; // Stopped.
            try { socket.BeginReceive(Received, socket); } catch (Exception) { }
        }

        private void Parse(byte[] data, IPEndPoint from)
        {
            if (data == null || data.Length > 512 || from == null) return;
            string[] parts = Encoding.UTF8.GetString(data).Split('|');
            int port, players, max;
            if (parts.Length != 7 || parts[0] != Magic || !int.TryParse(parts[1], out port) || port < 1024 || port > 65535
                || !int.TryParse(parts[2], out players) || !int.TryParse(parts[3], out max)) return;
            string address = from.Address.ToString();
            // The loopback copy and the broadcast copy of a local game are the same game.
            if (IPAddress.IsLoopback(from.Address)) address = "127.0.0.1";
            lock (gate)
            {
                if (address != "127.0.0.1" && games.Values.Any(g => g.Address == "127.0.0.1" && g.Port == port && g.Name == Clean(parts[6]))) return;
                if (address == "127.0.0.1")
                    foreach (var key in games.Where(g => g.Value.Port == port && g.Value.Name == Clean(parts[6]) && g.Value.Address != address).Select(g => g.Key).ToList())
                        games.Remove(key);
                games[address + ":" + port] = new Game
                {
                    Address = address, Port = port, Players = Math.Max(0, Math.Min(players, 8)), Max = Math.Max(1, Math.Min(max, 8)),
                    InGame = parts[4] == "1", Build = parts[5], Name = Clean(parts[6]), Seen = DateTime.UtcNow
                };
            }
        }

        private static string Clean(string name)
        {
            string text = new string((name ?? "").Where(c => !char.IsControl(c) && c != '|').Take(24).ToArray()).Trim();
            return text.Length == 0 ? "Player" : text;
        }

        private void CloseSender()
        {
            var socket = sender;
            sender = null;
            if (socket != null) try { socket.Close(); } catch (Exception) { }
        }

        public void Dispose()
        {
            disposed = true;
            CloseSender();
            StopListening();
        }
    }
}
