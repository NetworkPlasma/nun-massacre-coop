using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text;
using System.Xml;

namespace NunMassacre.Multiplayer.Network
{
    /// <summary>What happened when the host asked its router to open the game's port.</summary>
    public enum RouterOutcome { Open, NoRouter, Refused, SharedAddress, Failed }

    /// <summary>A port the router forwards to this PC: enough to renew it, close it, and invite friends.</summary>
    public sealed class RouterMapping
    {
        public RouterOutcome Outcome;
        public string Detail = "";
        public IPAddress PublicAddress;   // The router's internet address (null if unknown).
        public int ExternalPort;          // The port friends connect to (usually the game's own).
        public int InternalPort;
        public IPAddress LocalAddress;    // This PC on the router's network.
        public int LeaseSeconds;          // 0 = until removed.
        internal Uri ControlUrl;
        internal string ServiceType;
    }

    /// <summary>
    /// Opens the host's TCP port on the home router with UPnP (an Internet Gateway Device), so friends can
    /// join over the internet without a VPN or manual port forwarding. Plain .NET, blocking: run it off the
    /// game thread. Routers with UPnP switched off, or connections whose provider shares one public address
    /// between customers (carrier-grade NAT), cannot be opened this way; the outcome says which.
    /// </summary>
    public static class RouterPort
    {
        public const string Description = "Nun Massacre Co-op";
        private const int LeaseSeconds = 3600; // Expires on its own if the game is killed; renewed while hosting.
        private static readonly string[] SearchTargets =
        {
            "urn:schemas-upnp-org:device:InternetGatewayDevice:1",
            "urn:schemas-upnp-org:device:InternetGatewayDevice:2",
            "urn:schemas-upnp-org:service:WANIPConnection:1",
            "urn:schemas-upnp-org:service:WANIPConnection:2",
            "urn:schemas-upnp-org:service:WANPPPConnection:1",
        };

        /// <summary>Finds the router and forwards the port to this PC. Blocks for up to a few seconds.</summary>
        public static RouterMapping Open(int port, int discoverMilliseconds = 3000)
        {
            List<Uri> routers;
            try { routers = Discover(discoverMilliseconds); }
            catch (Exception error)
            {
                return new RouterMapping { Outcome = RouterOutcome.Failed, Detail = "Router search failed: " + error.Message, InternalPort = port };
            }
            if (routers.Count == 0)
                return new RouterMapping { Outcome = RouterOutcome.NoRouter, Detail = "No router answered (UPnP may be switched off).", InternalPort = port };
            RouterMapping best = null;
            foreach (Uri description in routers)
            {
                RouterMapping attempt = OpenAt(description, port);
                if (attempt.Outcome == RouterOutcome.Open) return attempt;
                if (best == null || attempt.Outcome == RouterOutcome.SharedAddress
                    || (best.Outcome == RouterOutcome.Failed && attempt.Outcome == RouterOutcome.Refused))
                    best = attempt;
            }
            return best;
        }

        /// <summary>Forwards the port through the router described at this URL (found by Open, or given by tests).</summary>
        public static RouterMapping OpenAt(Uri descriptionUrl, int port)
        {
            var mapping = new RouterMapping { InternalPort = port, Outcome = RouterOutcome.Failed };
            try
            {
                string serviceType; Uri control;
                if (!FindService(descriptionUrl, out serviceType, out control))
                {
                    mapping.Detail = "The router has no port forwarding service.";
                    return mapping;
                }
                mapping.ServiceType = serviceType; mapping.ControlUrl = control;
                mapping.LocalAddress = LocalAddressFor(control.Host);
                if (mapping.LocalAddress == null) { mapping.Detail = "Cannot tell which network address reaches the router."; return mapping; }

                // The game's own port first; if another PC already has it, the next few.
                string error = null;
                for (int external = port; external < port + 8 && external <= 65535; external++)
                {
                    error = Add(mapping, external, LeaseSeconds);
                    if (error == "725") { error = Add(mapping, external, 0); } // Only permanent mappings supported.
                    if (error == null) { mapping.ExternalPort = external; break; }
                    if (error != "718") break; // 718: that external port is taken by another computer.
                }
                if (error != null)
                {
                    mapping.Outcome = RouterOutcome.Refused;
                    mapping.Detail = "The router refused to open the port (" + ErrorText(error) + ").";
                    return mapping;
                }
                mapping.Outcome = RouterOutcome.Open;
                mapping.PublicAddress = ExternalAddress(mapping);
                if (mapping.PublicAddress == null)
                {
                    mapping.Outcome = RouterOutcome.Failed;
                    mapping.Detail = "The router did not report its internet address.";
                    Close(mapping);
                }
                else if (!IsPublic(mapping.PublicAddress))
                {
                    // Behind a second router, or the provider shares one address between customers: the opened
                    // port is not reachable from the internet.
                    mapping.Outcome = RouterOutcome.SharedAddress;
                    mapping.Detail = "The router's internet address (" + mapping.PublicAddress + ") is not public.";
                    Close(mapping);
                }
                return mapping;
            }
            catch (Exception error)
            {
                mapping.Detail = "Router error: " + error.Message;
                return mapping;
            }
        }

        /// <summary>Keeps a timed mapping alive. False if the router no longer accepts it.</summary>
        public static bool Renew(RouterMapping mapping)
        {
            if (mapping == null || mapping.Outcome != RouterOutcome.Open || mapping.ControlUrl == null) return false;
            try { return Add(mapping, mapping.ExternalPort, mapping.LeaseSeconds) == null; }
            catch (Exception) { return false; }
        }

        /// <summary>Removes the forwarding (stopping hosting, quitting). Never throws.</summary>
        public static void Close(RouterMapping mapping)
        {
            if (mapping == null || mapping.ControlUrl == null || mapping.ExternalPort == 0) return;
            try
            {
                Soap(mapping.ControlUrl, mapping.ServiceType, "DeletePortMapping",
                    "<NewRemoteHost></NewRemoteHost><NewExternalPort>" + mapping.ExternalPort + "</NewExternalPort><NewProtocol>TCP</NewProtocol>");
            }
            catch (Exception) { }
        }

        /// <summary>Addresses the internet can reach directly: not private, loopback, link-local or shared (100.64/10).</summary>
        public static bool IsPublic(IPAddress address)
        {
            if (address == null || address.AddressFamily != AddressFamily.InterNetwork) return false;
            byte[] b = address.GetAddressBytes();
            if (b[0] == 0 || b[0] == 10 || b[0] == 127 || b[0] >= 224) return false;
            if (b[0] == 172 && b[1] >= 16 && b[1] <= 31) return false;
            if (b[0] == 192 && b[1] == 168) return false;
            if (b[0] == 169 && b[1] == 254) return false;
            if (b[0] == 100 && b[1] >= 64 && b[1] <= 127) return false;
            return true;
        }

        /// <summary>The same network as this PC, or a VPN it is on (Hamachi and Radmin use public-looking
        /// 25.x and 26.x addresses): players there join without an invite code.</summary>
        public static bool IsOnLocalNetwork(IPAddress remote)
        {
            if (remote == null) return false;
            if (remote.IsIPv4MappedToIPv6) remote = remote.MapToIPv4();
            if (IPAddress.IsLoopback(remote) || !IsPublic(remote)) return true;
            try
            {
                byte[] r = remote.GetAddressBytes();
                foreach (NetworkInterface adapter in NetworkInterface.GetAllNetworkInterfaces())
                {
                    if (adapter.OperationalStatus != OperationalStatus.Up) continue;
                    foreach (UnicastIPAddressInformation unicast in adapter.GetIPProperties().UnicastAddresses)
                    {
                        if (unicast.Address.AddressFamily != AddressFamily.InterNetwork || unicast.IPv4Mask == null) continue;
                        byte[] l = unicast.Address.GetAddressBytes(), m = unicast.IPv4Mask.GetAddressBytes();
                        if (m[0] == 0) continue; // A missing mask would match everything.
                        bool same = true;
                        for (int i = 0; i < 4; i++) if ((l[i] & m[i]) != (r[i] & m[i])) { same = false; break; }
                        if (same) return true;
                    }
                }
            }
            catch (Exception) { }
            return false;
        }

        // ---------- Discovery (SSDP) ----------

        private static List<Uri> Discover(int milliseconds)
        {
            var found = new List<Uri>();
            var sockets = new List<UdpClient>();
            try
            {
                // One search per network adapter with a gateway: a VPN adapter must not swallow the multicast.
                foreach (IPAddress local in GatewayAdapters().DefaultIfEmpty(IPAddress.Any))
                {
                    UdpClient socket;
                    try { socket = new UdpClient(new IPEndPoint(local, 0)); }
                    catch (SocketException) { continue; }
                    sockets.Add(socket);
                    var multicast = new IPEndPoint(IPAddress.Parse("239.255.255.250"), 1900);
                    foreach (string target in SearchTargets)
                    {
                        byte[] search = Encoding.ASCII.GetBytes("M-SEARCH * HTTP/1.1\r\nHOST: 239.255.255.250:1900\r\nMAN: \"ssdp:discover\"\r\nMX: 2\r\nST: "
                            + target + "\r\n\r\n");
                        try { socket.Send(search, search.Length, multicast); } catch (SocketException) { }
                    }
                }
                DateTime until = DateTime.UtcNow.AddMilliseconds(milliseconds);
                while (DateTime.UtcNow < until && sockets.Count > 0)
                {
                    bool any = false;
                    foreach (UdpClient socket in sockets)
                    {
                        while (socket.Available > 0)
                        {
                            any = true;
                            var from = new IPEndPoint(IPAddress.Any, 0);
                            byte[] reply;
                            try { reply = socket.Receive(ref from); } catch (SocketException) { break; }
                            Uri location = Location(Encoding.ASCII.GetString(reply));
                            if (location != null && !found.Contains(location)) found.Add(location);
                        }
                    }
                    if (!any) System.Threading.Thread.Sleep(50);
                    if (found.Count > 0 && DateTime.UtcNow > until.AddMilliseconds(-milliseconds / 2)) break; // Answers come quickly.
                }
            }
            finally { foreach (UdpClient socket in sockets) socket.Close(); }
            return found;
        }

        private static IEnumerable<IPAddress> GatewayAdapters()
        {
            try
            {
                return NetworkInterface.GetAllNetworkInterfaces()
                    .Where(n => n.OperationalStatus == OperationalStatus.Up && n.NetworkInterfaceType != NetworkInterfaceType.Loopback
                        && n.GetIPProperties().GatewayAddresses.Any(g => g.Address.AddressFamily == AddressFamily.InterNetwork && !g.Address.Equals(IPAddress.Any)))
                    .SelectMany(n => n.GetIPProperties().UnicastAddresses)
                    .Where(a => a.Address.AddressFamily == AddressFamily.InterNetwork && !IPAddress.IsLoopback(a.Address))
                    .Select(a => a.Address).Distinct().ToList();
            }
            catch (Exception) { return new IPAddress[0]; }
        }

        internal static Uri Location(string reply)
        {
            foreach (string line in reply.Split(new[] { "\r\n", "\n" }, StringSplitOptions.None))
            {
                int colon = line.IndexOf(':');
                if (colon <= 0 || !line.Substring(0, colon).Trim().Equals("LOCATION", StringComparison.OrdinalIgnoreCase)) continue;
                Uri uri;
                if (Uri.TryCreate(line.Substring(colon + 1).Trim(), UriKind.Absolute, out uri) && uri.Scheme == Uri.UriSchemeHttp) return uri;
            }
            return null;
        }

        // ---------- Device description and control (SOAP) ----------

        private static bool FindService(Uri descriptionUrl, out string serviceType, out Uri control)
        {
            serviceType = null; control = null;
            var document = new XmlDocument { XmlResolver = null };
            using (var reader = XmlReader.Create(new StringReader(Http("GET", descriptionUrl, null, null)),
                new XmlReaderSettings { DtdProcessing = DtdProcessing.Ignore, XmlResolver = null }))
                document.Load(reader);
            Uri baseUrl = descriptionUrl;
            foreach (XmlNode node in document.GetElementsByTagName("*"))
            {
                Uri declared;
                if (node.LocalName == "URLBase" && Uri.TryCreate(node.InnerText.Trim(), UriKind.Absolute, out declared)) baseUrl = declared;
            }
            // WANIPConnection (cable/fibre routers) before WANPPPConnection (DSL), newest version first.
            var services = new List<KeyValuePair<string, string>>();
            foreach (XmlNode node in document.GetElementsByTagName("*"))
            {
                if (node.LocalName != "service") continue;
                string type = null, url = null;
                foreach (XmlNode child in node.ChildNodes)
                {
                    if (child.LocalName == "serviceType") type = child.InnerText.Trim();
                    else if (child.LocalName == "controlURL") url = child.InnerText.Trim();
                }
                if (type != null && url != null && (type.Contains(":WANIPConnection:") || type.Contains(":WANPPPConnection:")))
                    services.Add(new KeyValuePair<string, string>(type, url));
            }
            var chosen = services.OrderBy(s => s.Key.Contains(":WANIPConnection:") ? 0 : 1).ThenByDescending(s => s.Key).FirstOrDefault();
            if (chosen.Key == null) return false;
            serviceType = chosen.Key;
            control = new Uri(baseUrl, chosen.Value);
            return true;
        }

        private static IPAddress LocalAddressFor(string routerHost)
        {
            IPAddress router;
            if (!IPAddress.TryParse(routerHost, out router))
                router = Dns.GetHostAddresses(routerHost).FirstOrDefault(a => a.AddressFamily == AddressFamily.InterNetwork);
            if (router == null) return null;
            using (var probe = new UdpClient(AddressFamily.InterNetwork))
            {
                probe.Connect(router, 1900); // No packet is sent: this only picks the route.
                return ((IPEndPoint)probe.Client.LocalEndPoint).Address;
            }
        }

        /// <summary>Null on success, else the router's UPnP error code (or a short reason).</summary>
        private static string Add(RouterMapping mapping, int externalPort, int lease)
        {
            try
            {
                Soap(mapping.ControlUrl, mapping.ServiceType, "AddPortMapping",
                    "<NewRemoteHost></NewRemoteHost><NewExternalPort>" + externalPort + "</NewExternalPort><NewProtocol>TCP</NewProtocol>"
                    + "<NewInternalPort>" + mapping.InternalPort + "</NewInternalPort><NewInternalClient>" + mapping.LocalAddress + "</NewInternalClient>"
                    + "<NewEnabled>1</NewEnabled><NewPortMappingDescription>" + Description + "</NewPortMappingDescription><NewLeaseDuration>" + lease + "</NewLeaseDuration>");
                mapping.LeaseSeconds = lease;
                return null;
            }
            catch (UpnpException error) { return error.Code; }
        }

        private static IPAddress ExternalAddress(RouterMapping mapping)
        {
            string reply = Soap(mapping.ControlUrl, mapping.ServiceType, "GetExternalIPAddress", "");
            string text = Element(reply, "NewExternalIPAddress");
            IPAddress address;
            return text != null && IPAddress.TryParse(text.Trim(), out address) && address.AddressFamily == AddressFamily.InterNetwork ? address : null;
        }

        private sealed class UpnpException : Exception
        {
            internal readonly string Code;
            internal UpnpException(string code, string message) : base(message) { Code = code; }
        }

        private static string Soap(Uri control, string serviceType, string action, string arguments)
        {
            string body = "<?xml version=\"1.0\"?><s:Envelope xmlns:s=\"http://schemas.xmlsoap.org/soap/envelope/\" s:encodingStyle=\"http://schemas.xmlsoap.org/soap/encoding/\">"
                + "<s:Body><u:" + action + " xmlns:u=\"" + serviceType + "\">" + arguments + "</u:" + action + "></s:Body></s:Envelope>";
            return Http("POST", control, body, "\"" + serviceType + "#" + action + "\"");
        }

        private static string Http(string method, Uri url, string body, string soapAction)
        {
#pragma warning disable SYSLIB0014 // HttpWebRequest: what Unity's Mono offers; plain HTTP on the home network.
            var request = (HttpWebRequest)WebRequest.Create(url);
#pragma warning restore SYSLIB0014
            request.Method = method;
            request.Timeout = request.ReadWriteTimeout = 4000;
            request.Proxy = null; // The router is on the local network.
            request.KeepAlive = false;
            if (body != null)
            {
                byte[] bytes = Encoding.UTF8.GetBytes(body);
                request.ContentType = "text/xml; charset=\"utf-8\"";
                request.Headers.Add("SOAPACTION", soapAction);
                request.ContentLength = bytes.Length;
                using (Stream stream = request.GetRequestStream()) stream.Write(bytes, 0, bytes.Length);
            }
            try
            {
                using (var response = (HttpWebResponse)request.GetResponse())
                using (var reader = new StreamReader(response.GetResponseStream(), Encoding.UTF8))
                    return Limit(reader);
            }
            catch (WebException error) when (error.Response != null)
            {
                string reply;
                using (var reader = new StreamReader(error.Response.GetResponseStream(), Encoding.UTF8)) reply = Limit(reader);
                string code = Element(reply, "errorCode");
                throw new UpnpException(code != null ? code.Trim() : "http " + (int)((HttpWebResponse)error.Response).StatusCode,
                    Element(reply, "errorDescription") ?? error.Message);
            }
        }

        private static string Limit(StreamReader reader)
        {
            var buffer = new char[256 * 1024]; // A router's description is a few kilobytes.
            int read, total = 0;
            var text = new StringBuilder();
            while ((read = reader.Read(buffer, 0, buffer.Length)) > 0 && total < buffer.Length) { text.Append(buffer, 0, read); total += read; }
            return text.ToString();
        }

        private static string Element(string xml, string name)
        {
            if (string.IsNullOrEmpty(xml)) return null;
            int start = xml.IndexOf("<" + name, StringComparison.Ordinal);
            if (start < 0) { start = xml.IndexOf(":" + name + ">", StringComparison.Ordinal); if (start < 0) return null; }
            int open = xml.IndexOf('>', start);
            int close = xml.IndexOf("</", open + 1, StringComparison.Ordinal);
            return open < 0 || close < 0 ? null : xml.Substring(open + 1, close - open - 1);
        }

        private static string ErrorText(string code)
        {
            switch (code)
            {
                case "606": return "UPnP changes are not allowed";
                case "718": return "the port is already forwarded to another computer";
                case "724": return "it needs the same port inside and out";
                case "725": return "only permanent forwarding is supported";
                default: return "error " + code;
            }
        }
    }

    /// <summary>
    /// A short code friends paste to join over the internet: the host's public address, the port and a
    /// one-time key, in 16 easy-to-read characters (XXXX-XXXX-XXXX-XXXX, no I, L, O or U).
    /// </summary>
    public static class InviteCode
    {
        private const string Alphabet = "0123456789ABCDEFGHJKMNPQRSTVWXYZ"; // Crockford base 32.

        public static string Create(IPAddress address, int port, int key)
        {
            if (address == null || address.AddressFamily != AddressFamily.InterNetwork) throw new ArgumentException("An IPv4 address is required.", "address");
            if (port < 1 || port > 65535) throw new ArgumentOutOfRangeException("port");
            var bytes = new byte[10];
            Array.Copy(address.GetAddressBytes(), bytes, 4);
            bytes[4] = (byte)(port >> 8); bytes[5] = (byte)port;
            bytes[6] = (byte)(key >> 16); bytes[7] = (byte)(key >> 8); bytes[8] = (byte)key;
            bytes[9] = Check(bytes);
            var text = new StringBuilder();
            for (int bit = 0; bit < 80; bit += 5)
            {
                int value = 0;
                for (int i = 0; i < 5; i++) value = (value << 1) | ((bytes[(bit + i) / 8] >> (7 - (bit + i) % 8)) & 1);
                if (bit > 0 && bit % 20 == 0) text.Append('-');
                text.Append(Alphabet[value]);
            }
            return text.ToString();
        }

        /// <summary>Reads a pasted code: spaces, dashes and lower case are fine, as are O/0 and I/L/1 mix-ups.</summary>
        public static bool TryParse(string text, out IPAddress address, out int port, out int key)
        {
            address = null; port = 0; key = 0;
            if (string.IsNullOrEmpty(text) || text.IndexOf('.') >= 0 || text.IndexOf(':') >= 0) return false; // An address, not a code.
            var symbols = new List<int>();
            foreach (char raw in text.ToUpperInvariant())
            {
                if (raw == '-' || char.IsWhiteSpace(raw)) continue;
                char c = raw == 'O' ? '0' : raw == 'I' || raw == 'L' ? '1' : raw;
                int value = Alphabet.IndexOf(c);
                if (value < 0) return false;
                symbols.Add(value);
            }
            if (symbols.Count != 16) return false;
            var bytes = new byte[10];
            for (int bit = 0; bit < 80; bit++)
                if (((symbols[bit / 5] >> (4 - bit % 5)) & 1) != 0) bytes[bit / 8] |= (byte)(1 << (7 - bit % 8));
            if (bytes[9] != Check(bytes)) return false;
            address = new IPAddress(new[] { bytes[0], bytes[1], bytes[2], bytes[3] });
            port = (bytes[4] << 8) | bytes[5];
            key = (bytes[6] << 16) | (bytes[7] << 8) | bytes[8];
            return port > 0 && !address.Equals(IPAddress.Any);
        }

        private static byte Check(byte[] bytes)
        {
            int sum = 0x5A;
            for (int i = 0; i < 9; i++) sum = ((sum * 31) + bytes[i]) & 0xFF;
            return (byte)sum;
        }
    }
}
