using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using NunMassacre.Multiplayer.Network;

/// <summary>Online play without a VPN: the invite code, and the UPnP router client against a fake router.</summary>
internal static class RouterTests
{
    internal static void InviteCodes()
    {
        string code = InviteCode.Create(IPAddress.Parse("203.0.113.7"), 27845, 0xABCDEF);
        Check(code.Length == 19 && code.Count(c => c == '-') == 3, "Invite code is XXXX-XXXX-XXXX-XXXX: " + code);
        IPAddress address; int port, key;
        Check(InviteCode.TryParse(code, out address, out port, out key) && address.ToString() == "203.0.113.7" && port == 27845 && key == 0xABCDEF,
            "Invite code round trip");
        string sloppy = " " + code.ToLowerInvariant().Replace("-", "").Replace('0', 'o').Replace('1', 'l') + " ";
        Check(InviteCode.TryParse(sloppy, out address, out port, out key) && port == 27845 && key == 0xABCDEF,
            "Pasted lower case, without dashes, with O/L mix-ups");
        char[] typo = code.ToCharArray(); typo[5] = typo[5] == 'Z' ? 'Y' : 'Z';
        Check(!InviteCode.TryParse(new string(typo), out address, out port, out key), "A mistyped code is rejected by its check");
        Check(!InviteCode.TryParse("192.168.1.20", out address, out port, out key) && !InviteCode.TryParse("MYGAMINGPC", out address, out port, out key),
            "Addresses and host names are not codes");
        Check(RouterPort.IsPublic(IPAddress.Parse("203.0.113.7")) && !RouterPort.IsPublic(IPAddress.Parse("192.168.1.1")) && !RouterPort.IsPublic(IPAddress.Parse("100.72.3.4"))
            && !RouterPort.IsPublic(IPAddress.Parse("10.0.0.2")) && !RouterPort.IsPublic(IPAddress.Parse("172.20.0.1")), "Public and shared address ranges");
        Check(RouterPort.IsOnLocalNetwork(IPAddress.Loopback) && RouterPort.IsOnLocalNetwork(IPAddress.Parse("192.168.50.9")) && !RouterPort.IsOnLocalNetwork(IPAddress.Parse("203.0.113.7")),
            "Local network and VPN peers need no code");
    }

    internal static void RouterOpensAndCloses()
    {
        using (var router = new FakeRouter("203.0.113.7"))
        {
            RouterMapping mapping = RouterPort.OpenAt(router.Description, 27845);
            Check(mapping.Outcome == RouterOutcome.Open, "Router opened the port: " + mapping.Detail);
            Check(mapping.ExternalPort == 27845 && mapping.LeaseSeconds == 3600 && mapping.PublicAddress.ToString() == "203.0.113.7",
                "Same port, timed lease, public address");
            Check(router.Mapped.Contains(27845) && router.LastClient == "127.0.0.1", "Forwarded to this PC");
            Check(RouterPort.Renew(mapping), "Renewed");
            RouterPort.Close(mapping);
            Check(!router.Mapped.Contains(27845), "Closed again");
        }
    }

    internal static void RouterPortTakenAndPermanentOnly()
    {
        using (var router = new FakeRouter("198.51.100.20") { Taken = { 27845 }, PermanentOnly = true })
        {
            RouterMapping mapping = RouterPort.OpenAt(router.Description, 27845);
            Check(mapping.Outcome == RouterOutcome.Open && mapping.ExternalPort == 27846 && mapping.InternalPort == 27845, "Next port when another PC has it");
            Check(mapping.LeaseSeconds == 0, "Permanent forwarding when the router allows no other");
            IPAddress address; int port, key;
            Check(InviteCode.TryParse(InviteCode.Create(mapping.PublicAddress, mapping.ExternalPort, 77), out address, out port, out key) && port == 27846,
                "The invite carries the outside port");
            RouterPort.Close(mapping);
            Check(router.Mapped.Count == 0, "Closed again");
        }
    }

    internal static void RouterSharedAddressAndRefusal()
    {
        using (var router = new FakeRouter("100.70.1.2"))
        {
            RouterMapping mapping = RouterPort.OpenAt(router.Description, 27845);
            Check(mapping.Outcome == RouterOutcome.SharedAddress && router.Mapped.Count == 0,
                "A shared (carrier) address is reported, and the useless forwarding removed");
        }
        using (var router = new FakeRouter("203.0.113.7") { Refuse = "606" })
        {
            RouterMapping mapping = RouterPort.OpenAt(router.Description, 27845);
            Check(mapping.Outcome == RouterOutcome.Refused && mapping.Detail.Contains("not allowed"), "A router that refuses says so: " + mapping.Detail);
        }
        Check(RouterPort.OpenAt(new Uri("http://127.0.0.1:1/missing.xml"), 27845).Outcome == RouterOutcome.Failed, "No router at that address");
    }

    private static void Check(bool condition, string name) { if (!condition) throw new Exception("FAILED: " + name); }

    /// <summary>A minimal UPnP Internet Gateway Device on loopback: a description and a WANIPConnection control point.</summary>
    private sealed class FakeRouter : IDisposable
    {
        private readonly TcpListener listener = new TcpListener(IPAddress.Loopback, 0);
        private readonly string publicAddress;
        private volatile bool stopped;
        internal readonly HashSet<int> Mapped = new HashSet<int>();
        internal readonly HashSet<int> Taken = new HashSet<int>();
        internal bool PermanentOnly;
        internal string Refuse;
        internal string LastClient;
        internal Uri Description { get { return new Uri("http://127.0.0.1:" + ((IPEndPoint)listener.LocalEndpoint).Port + "/rootDesc.xml"); } }

        internal FakeRouter(string publicAddress)
        {
            this.publicAddress = publicAddress;
            listener.Start();
            new Thread(Serve) { IsBackground = true }.Start();
        }

        private void Serve()
        {
            while (!stopped)
            {
                TcpClient client;
                try { client = listener.AcceptTcpClient(); } catch (Exception) { return; }
                using (client)
                using (NetworkStream stream = client.GetStream())
                {
                    string head = ReadHead(stream);
                    string length = Header(head, "Content-Length");
                    var body = new byte[length == null ? 0 : int.Parse(length)];
                    for (int read = 0; read < body.Length;) read += stream.Read(body, read, body.Length - read);
                    string request = Encoding.UTF8.GetString(body);
                    string first = head.Split('\n')[0];
                    int status = 200; string reply;
                    if (first.StartsWith("GET /rootDesc.xml"))
                        reply = "<?xml version=\"1.0\"?><root xmlns=\"urn:schemas-upnp-org:device-1-0\"><device><deviceType>urn:schemas-upnp-org:device:InternetGatewayDevice:1</deviceType><deviceList><device><deviceList><device><serviceList>"
                            + "<service><serviceType>urn:schemas-upnp-org:service:WANCommonInterfaceConfig:1</serviceType><controlURL>/common</controlURL></service>"
                            + "<service><serviceType>urn:schemas-upnp-org:service:WANIPConnection:1</serviceType><controlURL>/ctl/IPConn</controlURL></service>"
                            + "</serviceList></device></deviceList></device></deviceList></device></root>";
                    else
                    {
                        string action = (Header(head, "SOAPACTION") ?? "").Trim('"').Split('#').Last();
                        int external = Value(request, "NewExternalPort");
                        string error = null;
                        if (action == "AddPortMapping")
                        {
                            LastClient = Text(request, "NewInternalClient");
                            if (Refuse != null) error = Refuse;
                            else if (Taken.Contains(external)) error = "718";
                            else if (PermanentOnly && Value(request, "NewLeaseDuration") != 0) error = "725";
                            else lock (Mapped) Mapped.Add(external);
                        }
                        else if (action == "DeletePortMapping") lock (Mapped) Mapped.Remove(external);
                        reply = error != null
                            ? "<?xml version=\"1.0\"?><s:Envelope xmlns:s=\"http://schemas.xmlsoap.org/soap/envelope/\"><s:Body><s:Fault><faultcode>s:Client</faultcode><faultstring>UPnPError</faultstring><detail><UPnPError xmlns=\"urn:schemas-upnp-org:control-1-0\"><errorCode>" + error + "</errorCode><errorDescription>Refused</errorDescription></UPnPError></detail></s:Fault></s:Body></s:Envelope>"
                            : "<?xml version=\"1.0\"?><s:Envelope xmlns:s=\"http://schemas.xmlsoap.org/soap/envelope/\"><s:Body><u:" + action + "Response xmlns:u=\"urn:schemas-upnp-org:service:WANIPConnection:1\">"
                                + (action == "GetExternalIPAddress" ? "<NewExternalIPAddress>" + publicAddress + "</NewExternalIPAddress>" : "") + "</u:" + action + "Response></s:Body></s:Envelope>";
                        if (error != null) status = 500;
                    }
                    byte[] bytes = Encoding.UTF8.GetBytes(reply);
                    byte[] header = Encoding.ASCII.GetBytes("HTTP/1.1 " + status
                        + (status == 200 ? " OK" : " Internal Server Error")
                        + "\r\nContent-Type: text/xml\r\nContent-Length: " + bytes.Length + "\r\nConnection: close\r\n\r\n");
                    stream.Write(header, 0, header.Length);
                    stream.Write(bytes, 0, bytes.Length);
                }
            }
        }

        private static string ReadHead(NetworkStream stream)
        {
            var head = new StringBuilder();
            while (!head.ToString().EndsWith("\r\n\r\n"))
            {
                int b = stream.ReadByte();
                if (b < 0) break;
                head.Append((char)b);
            }
            return head.ToString();
        }
        private static string Header(string head, string name)
        {
            foreach (string line in head.Split(new[] { "\r\n" }, StringSplitOptions.None))
                if (line.StartsWith(name + ":", StringComparison.OrdinalIgnoreCase)) return line.Substring(name.Length + 1).Trim();
            return null;
        }
        private static string Text(string xml, string name)
        {
            int start = xml.IndexOf("<" + name + ">", StringComparison.Ordinal);
            if (start < 0) return null;
            start += name.Length + 2;
            return xml.Substring(start, xml.IndexOf("</" + name + ">", start, StringComparison.Ordinal) - start);
        }
        private static int Value(string xml, string name) { int value; return int.TryParse(Text(xml, name), out value) ? value : -1; }

        public void Dispose() { stopped = true; listener.Stop(); }
    }
}
