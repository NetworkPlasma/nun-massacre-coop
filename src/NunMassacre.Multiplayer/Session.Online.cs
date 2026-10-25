using System;
using System.Collections.Generic;
using NunMassacre.Multiplayer.Network;
using UnityEngine;

namespace NunMassacre.Multiplayer
{
    // Online play: the router opens the port, and friends join with an invite code.
    internal sealed partial class Session
    {
        private RouterMapping router;                       // The open forwarding while hosting (game thread).
        private volatile RouterMapping routerResult;        // Handed over by the worker that asked the router.
        private int routerEpoch, inviteKey, joinKey, hostingRun; // hostingRun: one per HOST GAME (the epoch changes with every level).
        private float nextRouterRenew;
        private readonly Dictionary<string, int> wrongKeys = new Dictionary<string, int>();
        /// <summary>The code friends paste to join over the internet (null until the router has opened the port).</summary>
        internal string Invite { get; private set; }
        /// <summary>Hosting: how online play stands, for the lobby ("" when not hosting).</summary>
        internal string OnlineStatus { get; private set; }

        /// <summary>Automated tests: "off" (the default for test runs), "live", or a fake router's description URL.</summary>
        internal static string TestRouter;
        /// <summary>Automated tests: treat every guest as coming from the internet (they connect over loopback).</summary>
        internal static bool TestAllRemote;
        internal static int TestInviteKey, TestJoinKey;

        private void OpenRouter(int port)
        {
            Invite = null; wrongKeys.Clear();
            inviteKey = TestInviteKey > 0 ? TestInviteKey : UnityEngine.Random.Range(1, 1 << 24);
            if (TestRouter == "off") { OnlineStatus = "Online play is skipped in automated tests."; return; }
            if (!plugin.OpenRouterPort.Value) { OnlineStatus = "Online play is off (OpenRouterPort in the settings file)."; return; }
            Uri fake = TestRouter != null && TestRouter.StartsWith("http", StringComparison.Ordinal) ? new Uri(TestRouter) : null;
            OnlineStatus = "Asking your router to open port " + port + " for online play...";
            routerResult = null; routerEpoch = hostingRun;
            System.Threading.ThreadPool.QueueUserWorkItem(delegate
            {
                RouterMapping result;
                try { result = fake != null ? RouterPort.OpenAt(fake, port) : RouterPort.Open(port); }
                catch (Exception error) { result = new RouterMapping { Outcome = RouterOutcome.Failed, Detail = error.Message, InternalPort = port }; }
                routerResult = result;
            });
        }

        private void UpdateRouter(float now)
        {
            var result = routerResult;
            if (result != null)
            {
                routerResult = null;
                // Stopped hosting meanwhile.
                if (!Active || !IsHost || routerEpoch != hostingRun) { System.Threading.ThreadPool.QueueUserWorkItem(delegate { RouterPort.Close(result); }); }
                else
                {
                    if (result.Outcome == RouterOutcome.Open)
                    {
                        router = result;
                        Invite = InviteCode.Create(result.PublicAddress, result.ExternalPort, inviteKey);
                        nextRouterRenew = now + Mathf.Max(60f, result.LeaseSeconds * 0.5f);
                        OnlineStatus = "Online: friends anywhere can join with the invite code.";
                        plugin.Log("Router opened external port " + result.ExternalPort + " for online play"
                            + (result.LeaseSeconds > 0 ? " (renewed every " + result.LeaseSeconds / 2 + "s)." : "."));
                        if (TestRouter != null) plugin.Log("TEST invite " + Invite + " key " + inviteKey);
                    }
                    else
                    {
                        OnlineStatus = result.Outcome == RouterOutcome.SharedAddress
                            ? "Online hosting is blocked: your internet connection shares its address (or sits behind a second router). Have a friend host, or use a VPN."
                            : result.Outcome == RouterOutcome.NoRouter ? "Your router did not answer (UPnP may be off in its settings). Turn UPnP on, forward TCP port " + result.InternalPort + ", or use a VPN."
                            : "Your router did not open the port. " + result.Detail + " Forward TCP port " + result.InternalPort + " or use a VPN.";
                        plugin.Log("Online play unavailable: " + result.Outcome + ": " + result.Detail);
                    }
                }
            }
            if (router != null && router.LeaseSeconds > 0 && now >= nextRouterRenew)
            {
                nextRouterRenew = now + Mathf.Max(60f, router.LeaseSeconds * 0.5f);
                var mapping = router;
                System.Threading.ThreadPool.QueueUserWorkItem(delegate { if (!RouterPort.Renew(mapping)) plugin.Log("Router did not renew the forwarded port."); });
            }
        }

        private void CloseRouter(bool wait)
        {
            var mapping = router;
            router = null; Invite = null; OnlineStatus = "";
            if (mapping == null) return;
            if (wait) RouterPort.Close(mapping); // Quitting: the worker thread could be cut off.
            else System.Threading.ThreadPool.QueueUserWorkItem(delegate { RouterPort.Close(mapping); });
            plugin.Log("Router port closed.");
        }

        /// <summary>Host: may this new peer join? Players on the same network or VPN always can; anyone coming
        /// from the internet needs the invite code's key once the router has opened the port.</summary>
        private bool InviteAccepted(int peer, string key)
        {
            if (router == null) return true;
            var remote = transport.RemoteAddress(peer);
            if (remote == null || (!TestAllRemote && RouterPort.IsOnLocalNetwork(remote))) return true;
            string from = remote.ToString();
            int wrong;
            wrongKeys.TryGetValue(from, out wrong);
            if (wrong < 10 && key == inviteKey.ToString()) return true;
            wrongKeys[from] = wrong + 1; // Ten wrong keys and that address is turned away for this session.
            plugin.Log("Rejected a join from the internet without the invite code (" + (wrong + 1) + ").");
            return false;
        }
    }
}
