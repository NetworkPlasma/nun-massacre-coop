using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;

namespace NunMassacre.Multiplayer.Network
{
    public enum NetEventKind { Connected, Disconnected, Data, Error }

    public sealed class NetEvent
    {
        public NetEventKind Kind { get; private set; }
        public int PeerId { get; private set; }
        public byte[] Payload { get; private set; }
        public string Message { get; private set; }
        /// <summary>The result of <see cref="NetTransport.Preprocess"/> for data events, if any.</summary>
        public object Decoded { get; internal set; }

        internal NetEvent(NetEventKind kind, int peerId, byte[] payload, string message)
        {
            Kind = kind;
            PeerId = peerId;
            Payload = payload;
            Message = message ?? string.Empty;
        }
    }

    /// <summary>
    /// Reliable, ordered TCP transport. Call lifecycle methods and Poll from the game thread.
    /// Socket work runs asynchronously. Host peer ID is 1; the application assigns client IDs
    /// in its welcome message by calling SetLocalPeerId. All client sends target peer 1.
    /// Frames use a four-byte unsigned little-endian payload length, followed by that payload.
    /// </summary>
    public sealed class NetTransport : IDisposable
    {
        public const int MaxPayloadBytes = 8 * 1024 * 1024;
        public const int MaxQueuedSendBytes = 32 * 1024 * 1024;
        public const int MaxQueuedReceiveBytes = 64 * 1024 * 1024;
        public const int MaxQueuedEvents = 2048;
        private const int MaxQueuedSends = 128;
        private const int ControlEventReserve = 64;
        private const int ConnectTimeoutMilliseconds = 10000;
        private const int TransferTimeoutMilliseconds = 30000;

        private readonly object gate = new object();
        private readonly Dictionary<int, Peer> peers = new Dictionary<int, Peer>();
        private readonly Queue<NetEvent> events = new Queue<NetEvent>();
        private TcpListener listener;
        private TcpClient pendingConnection;
        private bool running;
        private bool host;
        private bool disposed;
        private bool eventOverflow;
        private int localPeerId;
        private int boundPort;
        private int nextPeerId;
        private int peerLimit;
        private long epoch;
        private int queuedReceiveBytes;

        public bool IsHost { get { lock (gate) return host; } }
        public bool IsRunning { get { lock (gate) return running; } }
        public int LocalPeerId { get { lock (gate) return localPeerId; } }
        public int BoundPort { get { lock (gate) return boundPort; } }
        public int RemotePeerCount { get { lock (gate) return peers.Count; } }

        /// <summary>The address a connected peer came from (null if unknown or gone).</summary>
        public IPAddress RemoteAddress(int peerId)
        {
            lock (gate)
            {
                Peer peer;
                if (!peers.TryGetValue(peerId, out peer)) return null;
                try { var endpoint = peer.Socket.Client.RemoteEndPoint as IPEndPoint; return endpoint == null ? null : endpoint.Address; }
                catch (Exception) { return null; }
            }
        }

        /// <param name="port">Listening port, or zero to let the OS choose one (see BoundPort).</param>
        /// <param name="maxPeers">Maximum remote connections, excluding the host.</param>
        public void StartHost(int port, int maxPeers = 4)
        {
            if (port < 0 || port > 65535) throw new ArgumentOutOfRangeException("port");
            if (maxPeers < 1 || maxPeers > 32) throw new ArgumentOutOfRangeException("maxPeers");
            ThrowIfDisposed();
            Stop();
            TcpListener newListener = new TcpListener(IPAddress.Any, port);
            newListener.ExclusiveAddressUse = true;
            long connectionEpoch;
            lock (gate)
            {
                connectionEpoch = epoch;
                try
                {
                    newListener.Start(maxPeers);
                    listener = newListener;
                    boundPort = ((IPEndPoint)newListener.LocalEndpoint).Port;
                    host = true;
                    running = true;
                    localPeerId = 1;
                    nextPeerId = 2;
                    peerLimit = maxPeers;
                }
                catch (Exception error)
                {
                    newListener.Stop();
                    QueueControlLocked(new NetEvent(NetEventKind.Error, 0, null, "Cannot host: " + error.Message));
                    return;
                }
            }
            Task.Run(() => AcceptLoop(newListener, connectionEpoch));
        }

        public void Connect(string address, int port)
        {
            if (string.IsNullOrWhiteSpace(address)) throw new ArgumentException("A host address is required.", "address");
            if (port < 1 || port > 65535) throw new ArgumentOutOfRangeException("port");
            ThrowIfDisposed();
            Stop();
            TcpClient socket = new TcpClient();
            long connectionEpoch;
            lock (gate)
            {
                running = true;
                host = false;
                connectionEpoch = epoch;
                pendingConnection = socket;
            }
            Task.Run(() => ConnectLoop(socket, address.Trim(), port, connectionEpoch));
        }

        public void SetLocalPeerId(int peerId)
        {
            if (peerId < 2) throw new ArgumentOutOfRangeException("peerId");
            lock (gate)
            {
                if (host || !running || !peers.ContainsKey(1)) throw new InvalidOperationException("Only a connected client can receive a client peer ID.");
                localPeerId = peerId;
            }
        }

        /// <summary>Copies the payload. Missing or disconnected peers are ignored.</summary>
        public void Send(int peerId, byte[] payload)
        {
            ValidatePayload(payload);
            Peer peer;
            lock (gate)
            {
                if (!running || !peers.TryGetValue(peerId, out peer)) return;
            }
            EnqueueSend(peer, payload);
        }

        /// <summary>Sends to all connected remote peers, with an optional excluded peer.</summary>
        public void Broadcast(byte[] payload, int exceptPeerId = 0)
        {
            ValidatePayload(payload);
            List<Peer> targets;
            lock (gate) targets = new List<Peer>(peers.Values);
            foreach (Peer peer in targets)
                if (peer.Id != exceptPeerId) EnqueueSend(peer, payload);
        }

        public void Disconnect(int peerId, string reason = "Disconnected by the application.")
        {
            Peer peer;
            lock (gate)
                if (!peers.TryGetValue(peerId, out peer)) return;
            RemovePeer(peer, reason);
        }

        /// <summary>Optional, thread-safe decoder run on the socket threads for each data payload, so
        /// the game thread receives ready objects (NetEvent.Decoded). It must not touch Unity objects;
        /// a throw just leaves Decoded null.</summary>
        public Func<byte[], object> Preprocess { get; set; }

        /// <summary>Drains events in connection order. Only Preprocess runs on socket threads.</summary>
        public List<NetEvent> Poll()
        {
            lock (gate)
            {
                List<NetEvent> result = new List<NetEvent>(events);
                events.Clear();
                queuedReceiveBytes = 0;
                if (eventOverflow)
                {
                    result.Add(new NetEvent(NetEventKind.Error, 0, null, "Network event queue overflowed; excess connections were refused."));
                    eventOverflow = false;
                }
                return result;
            }
        }

        /// <summary>Closes sockets immediately and invalidates old callbacks and pending events.</summary>
        public void Stop()
        {
            List<Peer> oldPeers;
            TcpListener oldListener;
            TcpClient oldPending;
            lock (gate)
            {
                epoch++;
                running = false;
                host = false;
                localPeerId = 0;
                boundPort = 0;
                oldListener = listener;
                listener = null;
                oldPending = pendingConnection;
                pendingConnection = null;
                oldPeers = new List<Peer>(peers.Values);
                peers.Clear();
                events.Clear();
                queuedReceiveBytes = 0;
                eventOverflow = false;
            }
            if (oldListener != null) oldListener.Stop();
            if (oldPending != null) oldPending.Close();
            foreach (Peer peer in oldPeers) peer.Close();
        }

        public void Dispose()
        {
            lock (gate) disposed = true;
            Stop();
        }

        private async Task AcceptLoop(TcpListener activeListener, long connectionEpoch)
        {
            try
            {
                while (true)
                {
                    TcpClient socket = await activeListener.AcceptTcpClientAsync().ConfigureAwait(false);
                    Peer peer = null;
                    try
                    {
                        lock (gate)
                        {
                            if (running && host && connectionEpoch == epoch && peers.Count < peerLimit && nextPeerId < int.MaxValue)
                                peer = AddPeerLocked(socket, nextPeerId++, connectionEpoch);
                        }
                    }
                    catch (Exception error)
                    {
                        socket.Close();
                        lock (gate)
                            if (running && connectionEpoch == epoch)
                                QueueControlLocked(new NetEvent(NetEventKind.Error, 0, null, "Cannot initialize a peer connection: " + error.Message));
                        continue;
                    }
                    if (peer == null) socket.Close();
                    else RunPeer(peer);
                }
            }
            catch (Exception error)
            {
                lock (gate)
                {
                    if (!running || connectionEpoch != epoch) return;
                    QueueControlLocked(new NetEvent(NetEventKind.Error, 0, null, "Listener stopped: " + error.Message));
                    running = false;
                }
            }
        }

        private async Task ConnectLoop(TcpClient socket, string address, int port, long connectionEpoch)
        {
            try
            {
                await WithTimeout(socket.ConnectAsync(address, port), ConnectTimeoutMilliseconds, "Connection timed out.").ConfigureAwait(false);
                Peer peer = null;
                lock (gate)
                {
                    if (running && !host && connectionEpoch == epoch && pendingConnection == socket)
                    {
                        pendingConnection = null;
                        peer = AddPeerLocked(socket, 1, connectionEpoch);
                    }
                }
                if (peer == null) socket.Close();
                else RunPeer(peer);
            }
            catch (Exception error)
            {
                socket.Close();
                lock (gate)
                {
                    if (connectionEpoch != epoch || !running) return;
                    pendingConnection = null;
                    running = false;
                    QueueControlLocked(new NetEvent(NetEventKind.Error, 1, null, "Cannot connect: " + error.Message));
                }
            }
        }

        private Peer AddPeerLocked(TcpClient socket, int id, long connectionEpoch)
        {
            if (events.Count >= MaxQueuedEvents)
            {
                eventOverflow = true;
                return null;
            }
            socket.NoDelay = true;
            socket.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.KeepAlive, true);
            Peer peer = new Peer(id, connectionEpoch, socket);
            peers.Add(id, peer);
            QueueControlLocked(new NetEvent(NetEventKind.Connected, id, null, string.Empty));
            return peer;
        }

        private void RunPeer(Peer peer)
        {
            Task.Run(() => ReadLoop(peer));
            Task.Run(() => WriteLoop(peer));
        }

        private async Task ReadLoop(Peer peer)
        {
            try
            {
                byte[] header = new byte[4];
                while (true)
                {
                    if (!await ReadExactly(peer.Stream, header, true).ConfigureAwait(false))
                    {
                        RemovePeer(peer, "Remote peer closed the connection.");
                        return;
                    }
                    uint size = (uint)header[0] | ((uint)header[1] << 8) | ((uint)header[2] << 16) | ((uint)header[3] << 24);
                    if (size > MaxPayloadBytes) throw new InvalidDataException("Frame exceeds the 8 MiB payload limit.");
                    byte[] payload = new byte[(int)size];
                    if (payload.Length != 0)
                        await WithTimeout(ReadExactly(peer.Stream, payload, false), TransferTimeoutMilliseconds,
                            "Payload receive timed out.").ConfigureAwait(false);
                    object decoded = null;
                    var preprocess = Preprocess;
                    if (preprocess != null) { try { decoded = preprocess(payload); } catch { decoded = null; } }
                    bool accepted;
                    lock (gate)
                    {
                        if (peer.Epoch != epoch || !peers.ContainsKey(peer.Id)) return;
                        accepted = events.Count < MaxQueuedEvents - ControlEventReserve && queuedReceiveBytes <= MaxQueuedReceiveBytes - payload.Length;
                        if (accepted)
                        {
                            events.Enqueue(new NetEvent(NetEventKind.Data, peer.Id, payload, string.Empty) { Decoded = decoded });
                            queuedReceiveBytes += payload.Length;
                        }
                    }
                    if (!accepted)
                    {
                        RemovePeer(peer, "Receive queue limit exceeded; the game is not consuming network data quickly enough.");
                        return;
                    }
                }
            }
            catch (Exception error) { RemovePeer(peer, error.Message); }
        }

        private void EnqueueSend(Peer peer, byte[] payload)
        {
            bool overloaded;
            lock (peer.SendGate)
            {
                if (peer.Closed) return;
                overloaded = peer.PendingMessages >= MaxQueuedSends || peer.PendingBytes > MaxQueuedSendBytes - payload.Length;
                if (!overloaded)
                {
                    byte[] copy = new byte[payload.Length];
                    Buffer.BlockCopy(payload, 0, copy, 0, copy.Length);
                    peer.SendQueue.Enqueue(copy);
                    peer.PendingMessages++;
                    peer.PendingBytes += copy.Length;
                    peer.SendReady.Release();
                }
            }
            if (overloaded) RemovePeer(peer, "Send queue limit exceeded; the remote peer is too slow.");
        }

        private async Task WriteLoop(Peer peer)
        {
            try
            {
                byte[] header = new byte[4];
                while (true)
                {
                    await peer.SendReady.WaitAsync(peer.Cancellation.Token).ConfigureAwait(false);
                    byte[] payload;
                    lock (peer.SendGate)
                    {
                        if (peer.Closed) return;
                        payload = peer.SendQueue.Dequeue();
                    }
                    int size = payload.Length;
                    header[0] = (byte)size;
                    header[1] = (byte)(size >> 8);
                    header[2] = (byte)(size >> 16);
                    header[3] = (byte)(size >> 24);
                    await WithTimeout(WriteFrame(peer.Stream, header, payload), TransferTimeoutMilliseconds, "Payload send timed out.").ConfigureAwait(false);
                    lock (peer.SendGate)
                    {
                        peer.PendingMessages--;
                        peer.PendingBytes -= payload.Length;
                    }
                }
            }
            catch (Exception error) { RemovePeer(peer, error.Message); }
        }

        private void RemovePeer(Peer peer, string reason)
        {
            lock (gate)
            {
                Peer current;
                if (peer.Epoch == epoch && peers.TryGetValue(peer.Id, out current) && ReferenceEquals(current, peer))
                {
                    peers.Remove(peer.Id);
                    if (!host)
                    {
                        running = false;
                        localPeerId = 0;
                    }
                    QueueControlLocked(new NetEvent(NetEventKind.Disconnected, peer.Id, null, reason));
                }
            }
            peer.Close();
        }

        private bool QueueControlLocked(NetEvent value)
        {
            if (events.Count >= MaxQueuedEvents)
            {
                eventOverflow = true;
                return false;
            }
            events.Enqueue(value);
            return true;
        }

        private static async Task<bool> ReadExactly(NetworkStream stream, byte[] destination, bool allowCleanEof)
        {
            int offset = 0;
            while (offset < destination.Length)
            {
                int count = await stream.ReadAsync(destination, offset, destination.Length - offset).ConfigureAwait(false);
                if (count == 0)
                {
                    if (offset == 0 && allowCleanEof) return false;
                    throw new EndOfStreamException("Remote peer closed an incomplete frame.");
                }
                offset += count;
            }
            return true;
        }

        private static async Task WriteFrame(NetworkStream stream, byte[] header, byte[] payload)
        {
            await stream.WriteAsync(header, 0, header.Length).ConfigureAwait(false);
            if (payload.Length != 0) await stream.WriteAsync(payload, 0, payload.Length).ConfigureAwait(false);
        }

        private static async Task WithTimeout(Task operation, int milliseconds, string timeoutMessage)
        {
            using (CancellationTokenSource timer = new CancellationTokenSource())
            {
                Task timeout = Task.Delay(milliseconds, timer.Token);
                if (await Task.WhenAny(operation, timeout).ConfigureAwait(false) != operation)
                {
                    // Observe any eventual socket failure after its owner closes the connection.
                    _ = operation.ContinueWith(t => { var ignored = t.Exception; }, TaskContinuationOptions.OnlyOnFaulted);
                    throw new TimeoutException(timeoutMessage);
                }
                timer.Cancel();
                await operation.ConfigureAwait(false);
            }
        }

        private static void ValidatePayload(byte[] payload)
        {
            if (payload == null) throw new ArgumentNullException("payload");
            if (payload.Length > MaxPayloadBytes) throw new ArgumentException("Payload exceeds the 8 MiB limit.", "payload");
        }

        private void ThrowIfDisposed()
        {
            lock (gate)
                if (disposed) throw new ObjectDisposedException("NetTransport");
        }

        private sealed class Peer
        {
            internal readonly int Id;
            internal readonly long Epoch;
            internal readonly TcpClient Socket;
            internal readonly NetworkStream Stream;
            internal readonly object SendGate = new object();
            internal readonly Queue<byte[]> SendQueue = new Queue<byte[]>();
            internal readonly SemaphoreSlim SendReady = new SemaphoreSlim(0);
            internal readonly CancellationTokenSource Cancellation = new CancellationTokenSource();
            internal int PendingBytes;
            internal int PendingMessages;
            private int closed;
            internal bool Closed { get { return Volatile.Read(ref closed) != 0; } }

            internal Peer(int id, long epoch, TcpClient socket)
            {
                Id = id;
                Epoch = epoch;
                Socket = socket;
                Stream = socket.GetStream();
            }

            internal void Close()
            {
                if (Interlocked.Exchange(ref closed, 1) != 0) return;
                Cancellation.Cancel();
                lock (SendGate) SendQueue.Clear();
                // Mono's Socket.Close waits for any thread blocked in a receive on that socket, which
                // froze the game thread (and quitting) while a peer was connected. Shut the connection
                // down first so pending reads return, and never close on the caller's thread.
                ThreadPool.QueueUserWorkItem(delegate
                {
                    try { Socket.Client.Shutdown(SocketShutdown.Both); } catch (Exception) { }
                    try { Socket.Close(); } catch (Exception) { }
                });
            }
        }
    }
}
