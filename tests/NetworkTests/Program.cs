using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using NunMassacre.Multiplayer.Network;

internal static class Program
{
    private static int Main()
    {
        Action[] tests = { HostAndTwoClients, PartialFramesAndCoalescedFrames, RejectOversizedAndTruncatedFrames, StopReconnectAndCancel, RejectExcessPeers, BoundReceiveQueue, BoundSlowPeerSendQueue, ConnectionErrors, InvalidArguments,
            RouterTests.InviteCodes, RouterTests.RouterOpensAndCloses, RouterTests.RouterPortTakenAndPermanentOnly, RouterTests.RouterSharedAddressAndRefusal, PeerAddress };
        try
        {
            foreach (Action test in tests)
            {
                test();
                Console.WriteLine("PASS " + test.Method.Name);
            }
            Console.WriteLine("All " + tests.Length + " network integration tests passed.");
            return 0;
        }
        catch (Exception error)
        {
            Console.Error.WriteLine(error);
            return 1;
        }
    }

    private static void HostAndTwoClients()
    {
        using (NetTransport host = new NetTransport())
        using (NetTransport first = new NetTransport())
        using (NetTransport second = new NetTransport())
        {
            host.StartHost(0);
            Assert(host.IsHost && host.IsRunning && host.LocalPeerId == 1, "Host state");
            first.Connect("127.0.0.1", host.BoundPort);
            int firstId = WaitEvent(host, NetEventKind.Connected).PeerId;
            Assert(WaitEvent(first, NetEventKind.Connected).PeerId == 1, "Client server ID");
            second.Connect("127.0.0.1", host.BoundPort);
            int secondId = WaitEvent(host, NetEventKind.Connected).PeerId;
            WaitEvent(second, NetEventKind.Connected);
            Assert(firstId == 2 && secondId == 3, "Assigned IDs");
            first.SetLocalPeerId(firstId);
            second.SetLocalPeerId(secondId);
            Assert(first.LocalPeerId == firstId, "Welcome ID");
            first.Send(1, Bytes("from-first"));
            NetEvent incoming = WaitEvent(host, NetEventKind.Data);
            Assert(incoming.PeerId == firstId && Text(incoming) == "from-first", "Host receives client data");
            host.Broadcast(Bytes("everyone"));
            Assert(Text(WaitEvent(first, NetEventKind.Data)) == "everyone", "Broadcast first");
            Assert(Text(WaitEvent(second, NetEventKind.Data)) == "everyone", "Broadcast second");
            host.Broadcast(Bytes("second-only"), firstId);
            Assert(Text(WaitEvent(second, NetEventKind.Data)) == "second-only", "Except-peer broadcast");
            Thread.Sleep(40);
            Assert(!first.Poll().Any(e => e.Kind == NetEventKind.Data), "Excluded peer receives nothing");
            byte[] data = new byte[1024 * 1024];
            for (int i = 0; i < data.Length; i++) data[i] = (byte)(i % 251);
            host.Send(firstId, data);
            data[0] = 99;
            byte[] received = WaitEvent(first, NetEventKind.Data).Payload;
            Assert(received.Length == data.Length && received[0] == 0 && received[777777] == (byte)(777777 % 251),
                "Large payload and caller mutation isolation");
            host.Send(firstId, new byte[0]);
            Assert(WaitEvent(first, NetEventKind.Data).Payload.Length == 0, "Empty frame");
            first.Stop();
            Assert(WaitEvent(host, NetEventKind.Disconnected).PeerId == firstId, "Disconnect identity");
            Assert(host.IsRunning && host.RemotePeerCount == 1, "Remaining host stays active");
            host.Disconnect(secondId, "Test kick");
            Assert(WaitEvent(host, NetEventKind.Disconnected).Message == "Test kick", "Application disconnect reason");
            WaitEvent(second, NetEventKind.Disconnected);
            Assert(!second.IsRunning && second.LocalPeerId == 0, "Disconnected client state");
        }
    }

    private static void PartialFramesAndCoalescedFrames()
    {
        using (NetTransport host = new NetTransport())
        using (TcpClient raw = new TcpClient())
        {
            host.StartHost(0);
            raw.Connect("127.0.0.1", host.BoundPort);
            WaitEvent(host, NetEventKind.Connected);
            byte[] frame = Frame(Bytes("a split frame"));
            for (int i = 0; i < frame.Length; i++)
            {
                raw.GetStream().Write(frame, i, 1);
                Thread.Sleep(2);
            }
            Assert(Text(WaitEvent(host, NetEventKind.Data)) == "a split frame", "Split header and payload reassembled");
            byte[] combined = Frame(Bytes("one")).Concat(Frame(Bytes("two"))).Concat(Frame(Bytes("three"))).ToArray();
            raw.GetStream().Write(combined, 0, combined.Length);
            List<NetEvent> received = WaitEvents(host, NetEventKind.Data, 3);
            Assert(string.Join(",", received.Select(Text)) == "one,two,three", "Coalesced frames preserve order");
        }
    }

    private static void RejectOversizedAndTruncatedFrames()
    {
        using (NetTransport host = new NetTransport())
        {
            host.StartHost(0);
            using (TcpClient raw = new TcpClient())
            {
                raw.Connect("127.0.0.1", host.BoundPort);
                WaitEvent(host, NetEventKind.Connected);
                byte[] header = { 255, 255, 255, 255 };
                raw.GetStream().Write(header, 0, 4);
                Assert(WaitEvent(host, NetEventKind.Disconnected).Message.Contains("8 MiB"), "Unsigned oversized frame rejected");
            }
            using (TcpClient raw = new TcpClient())
            {
                raw.Connect("127.0.0.1", host.BoundPort);
                WaitEvent(host, NetEventKind.Connected);
                byte[] partial = { 10, 0, 0, 0, 1, 2 };
                raw.GetStream().Write(partial, 0, partial.Length);
                raw.Client.Shutdown(SocketShutdown.Send);
                Assert(WaitEvent(host, NetEventKind.Disconnected).Message.Contains("incomplete"), "Truncated frame rejected");
            }
            Assert(host.IsRunning, "Malformed peer cannot stop listener");
        }
    }

    private static void StopReconnectAndCancel()
    {
        using (NetTransport host = new NetTransport())
        using (NetTransport client = new NetTransport())
        {
            host.StartHost(0);
            int port = host.BoundPort;
            for (int i = 0; i < 8; i++)
            {
                client.Connect("127.0.0.1", port);
                WaitEvent(client, NetEventKind.Connected);
                WaitEvent(host, NetEventKind.Connected);
                client.Stop();
                WaitEvent(host, NetEventKind.Disconnected);
                Thread.Sleep(10);
                Assert(client.Poll().Count == 0, "No stale callbacks after Stop");
            }
            Stopwatch watch = Stopwatch.StartNew();
            client.Connect("192.0.2.1", port);
            Assert(watch.ElapsedMilliseconds < 500, "Connection attempt is nonblocking");
            client.Stop();
            client.Connect("127.0.0.1", port);
            WaitEvent(client, NetEventKind.Connected);
            WaitEvent(host, NetEventKind.Connected);
            host.Stop();
            WaitEvent(client, NetEventKind.Disconnected);
            host.StartHost(port);
            client.Connect("127.0.0.1", port);
            WaitEvent(client, NetEventKind.Connected);
            WaitEvent(host, NetEventKind.Connected);
            client.Send(1, Bytes("restarted"));
            Assert(Text(WaitEvent(host, NetEventKind.Data)) == "restarted", "Reconnect after listener restart");
        }
    }

    private static void RejectExcessPeers()
    {
        using (NetTransport host = new NetTransport())
        using (NetTransport first = new NetTransport())
        using (NetTransport second = new NetTransport())
        {
            host.StartHost(0, 1);
            first.Connect("127.0.0.1", host.BoundPort);
            WaitEvent(first, NetEventKind.Connected);
            WaitEvent(host, NetEventKind.Connected);
            second.Connect("127.0.0.1", host.BoundPort);
            WaitEvent(second, NetEventKind.Disconnected);
            Assert(host.RemotePeerCount == 1 && host.IsRunning, "Peer cap enforced");
        }
    }

    private static void BoundReceiveQueue()
    {
        using (NetTransport host = new NetTransport())
        using (TcpClient raw = new TcpClient())
        {
            host.StartHost(0);
            raw.Connect("127.0.0.1", host.BoundPort);
            WaitEvent(host, NetEventKind.Connected);
            byte[] frames = new byte[(NetTransport.MaxQueuedEvents + 100) * 4];
            raw.GetStream().Write(frames, 0, frames.Length);
            WaitUntil(() => host.RemotePeerCount == 0, "Receive overload disconnect");
            List<NetEvent> result = host.Poll();
            Assert(result.Count <= NetTransport.MaxQueuedEvents, "Bounded inbound event count");
            Assert(result.Any(e => e.Kind == NetEventKind.Disconnected && e.Message.Contains("Receive queue")), "Receive overload is observable");
        }
    }

    private static void BoundSlowPeerSendQueue()
    {
        using (NetTransport host = new NetTransport())
        using (TcpClient raw = new TcpClient())
        {
            host.StartHost(0);
            raw.ReceiveBufferSize = 1024;
            raw.Connect("127.0.0.1", host.BoundPort);
            int id = WaitEvent(host, NetEventKind.Connected).PeerId;
            byte[] large = new byte[NetTransport.MaxPayloadBytes];
            for (int i = 0; i < 12 && host.RemotePeerCount != 0; i++) host.Send(id, large);
            NetEvent disconnected = WaitEvent(host, NetEventKind.Disconnected);
            Assert(disconnected.Message.Contains("Send queue"), "Non-reading peer cannot grow memory without bound");
        }
    }

    private static void InvalidArguments()
    {
        using (NetTransport transport = new NetTransport())
        {
            Throws<ArgumentOutOfRangeException>(() => transport.StartHost(-1));
            Throws<ArgumentOutOfRangeException>(() => transport.StartHost(0, 0));
            Throws<ArgumentException>(() => transport.Connect(" ", 1234));
            Throws<ArgumentOutOfRangeException>(() => transport.Connect("localhost", 0));
            Throws<ArgumentNullException>(() => transport.Send(1, null));
            Throws<ArgumentException>(() => transport.Broadcast(new byte[NetTransport.MaxPayloadBytes + 1]));
            transport.Dispose();
            Throws<ObjectDisposedException>(() => transport.StartHost(0));
        }
    }

    private static void ConnectionErrors()
    {
        using (NetTransport host = new NetTransport())
        using (NetTransport second = new NetTransport())
        {
            host.StartHost(0);
            int port = host.BoundPort;
            second.StartHost(port);
            Assert(WaitEvent(second, NetEventKind.Error).Message.Contains("Cannot host"), "Bind errors are delivered to the main thread");
            Assert(!second.IsRunning, "Failed host state");
            host.Stop();
            second.Connect("127.0.0.1", port);
            Assert(WaitEvent(second, NetEventKind.Error).Message.Contains("Cannot connect"), "Connect errors are delivered to the main thread");
            Assert(!second.IsRunning, "Failed client state");
        }
    }

    private static List<NetEvent> WaitEvents(NetTransport transport, NetEventKind kind, int count)
    {
        List<NetEvent> matches = new List<NetEvent>();
        List<NetEvent> all = new List<NetEvent>();
        Stopwatch timer = Stopwatch.StartNew();
        while (timer.ElapsedMilliseconds < 6000)
        {
            List<NetEvent> polled = transport.Poll();
            all.AddRange(polled);
            matches.AddRange(polled.Where(e => e.Kind == kind));
            if (matches.Count >= count) return matches;
            Thread.Sleep(5);
        }
        throw new Exception("Timed out waiting for " + count + " " + kind + "; saw " + string.Join("; ", all.Select(e => e.Kind + ": " + e.Message)));
    }

    private static void PeerAddress()
    {
        using (NetTransport host = new NetTransport())
        using (NetTransport guest = new NetTransport())
        {
            host.StartHost(0);
            guest.Connect("127.0.0.1", host.BoundPort);
            NetEvent joined = WaitEvent(host, NetEventKind.Connected);
            Assert(System.Net.IPAddress.IsLoopback(host.RemoteAddress(joined.PeerId)), "The host sees where a guest connects from");
            Assert(host.RemoteAddress(99) == null, "Unknown peers have no address");
        }
    }

    private static NetEvent WaitEvent(NetTransport transport, NetEventKind kind) { return WaitEvents(transport, kind, 1)[0]; }
    private static void WaitUntil(Func<bool> condition, string name)
    {
        Stopwatch timer = Stopwatch.StartNew();
        while (!condition() && timer.ElapsedMilliseconds < 6000) Thread.Sleep(5);
        Assert(condition(), name);
    }
    private static byte[] Frame(byte[] payload) { return BitConverter.GetBytes(payload.Length).Concat(payload).ToArray(); }
    private static byte[] Bytes(string value) { return Encoding.UTF8.GetBytes(value); }
    private static string Text(NetEvent value) { return Encoding.UTF8.GetString(value.Payload); }
    private static void Assert(bool condition, string name) { if (!condition) throw new Exception("FAILED: " + name); }
    private static void Throws<T>(Action action) where T : Exception
    {
        try { action(); }
        catch (T) { return; }
        throw new Exception("Expected " + typeof(T).Name);
    }
}
