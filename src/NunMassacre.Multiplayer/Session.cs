using System;
using System.Collections.Generic;
using System.Linq;
using GrannyClone;
using NunMassacre.Multiplayer.Gameplay;
using NunMassacre.Multiplayer.Network;
using NunMassacre.Multiplayer.Players;
using NunMassacre.Multiplayer.Voice;
using NunMassacre.Multiplayer.World;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace NunMassacre.Multiplayer
{
    /// <summary>
    /// One multiplayer session, host or guest: the connection, the players, and keeping every game the same. The rest of
    /// the class is in the Session.*.cs files beside this one, one topic each.
    /// </summary>
    internal sealed partial class Session : IDisposable
    {
        internal const string MainMenuScene = "Nun Bootup";
        internal const string OpeningScene = "Outside Final", GameScene = "Nun scene";
        // Players who left mid-game: their half-done puzzles and reservations are released, not waited on.
        private readonly HashSet<int> departed = new HashSet<int>();
        private readonly List<Packet> pendingSpawns = new List<Packet>();   // Traps sent before we were ready.
        /// <summary>Item randomisation's seed: the same random layout on every peer (so the same world ids).</summary>
        internal int WorldSeed { get; private set; }
        private readonly Plugin plugin;
        private readonly NetTransport transport = new NetTransport();
        private readonly Dictionary<int, PlayerFrame> players = new Dictionary<int, PlayerFrame>();
        private readonly Dictionary<string, int> owners = new Dictionary<string, int>();
        private readonly HashSet<int> authenticated = new HashSet<int>();
        private readonly HashSet<int> readyPeers = new HashSet<int>();
        private readonly Dictionary<int, float> lastSeen = new Dictionary<int, float>();
        private readonly RemotePlayers avatars;
        private readonly EnemyDirector enemy;
        internal readonly CoopLife Life;
        private readonly DynamicSpawns spawns;
        internal readonly LanDiscovery Discovery = new LanDiscovery();
        internal readonly VoiceChat Voice;
        private readonly Dictionary<int, float> voiceWindow = new Dictionary<int, float>();
        private readonly Dictionary<int, int> voiceCount = new Dictionary<int, int>();
        private float nextAdvertise;
        // Trigger zones that change the world for everyone (the rest start a player's own ending or scare).
        private static readonly string[] WorldTriggers = { "_Cage Blockers/Open gate trigger" };
        private WorldReplicator world;
        internal pReferences Local;
        internal void Log(string text) { plugin.Log(text); }
        internal bool LocalHiding { get { return Local && Local.playerStates && Local.playerStates.StateCheck() == PlayerStates.States.Hiding; } }
        internal bool Active { get; private set; }
        internal bool IsHost { get; private set; }
        internal int LocalId { get; private set; }
        internal bool Ready { get; private set; }
        internal int ReplayActor { get; private set; }
        internal bool Replaying { get; private set; }
        internal string Status { get; private set; } = "Offline";
        internal string SceneName { get { return SceneManager.GetActiveScene().name; } }
        private string lastScene;
        /// <summary>A playable level (not the splash, language or menu scenes).</summary>
        private static bool IsLevel(string scene)
        {
            return scene == OpeningScene || scene == GameScene
                || (scene != null && scene != MainMenuScene && scene != "Ratalaika_Games" && scene != "LanguageScene");
        }
        internal int WorldCount { get { return world == null ? 0 : world.RegistryCount; } }
        internal string WorldIdFor(Component component) { return world == null || !component ? null : world.IdFor(component); }
        private string hostScene;
        private int epoch;
        private int actionSequence;
        private float sceneReadyAt, nextPlayers, nextWorld, nextFull, nextSearch, nextHeartbeat;
        private bool announcedReady, loading;
        internal bool TestVent;               // Automated tests: report being in a vent.
        private Transform localBed, localBedKillPoint;
        private bool localVent;
        private float connectionStarted;
        private float lastHostMessage;
        private Packet pendingSnapshot;
        // World deltas are encoded and sent in order on one background thread (JSON is the costly part).
        private readonly System.Collections.Concurrent.BlockingCollection<Action> outbox = new System.Collections.Concurrent.BlockingCollection<Action>();
        private System.Threading.Thread encoder;
        /// <summary>Short player-facing messages: joins, leaves, lost connections.</summary>
        internal event Action<string> Notice;

        internal Session(Plugin owner)
        {
            plugin = owner;
            transport.Preprocess = DecodePayload;
            avatars = new RemotePlayers(plugin.Log);
            avatars.ItemResolver = id => world == null ? null : world.Find(id) as ItemInteractable;
            avatars.HideFacing = HideFacing;
            avatars.BathFacing = () => bathStart ? bathStart.forward : (Vector3?)null;
            enemy = new EnemyDirector(plugin.Log);
            enemy.Targetable = IsTargetable;
            enemy.DamagePeer = (peer, amount) => Send(peer, new Packet { type = "damage", epoch = epoch, amount = Mathf.Clamp(amount, 0, 1000) });
            Life = new CoopLife(this, plugin);
            Gestures = new Gestures();
            spawns = new DynamicSpawns(plugin.Log) { World = () => world, Template = SpareTemplate };
            Voice = new VoiceChat(plugin.Config, this, plugin.Log);
            Ears = new Gameplay.NunEars(this);
            Voice.TestTone = Environment.GetCommandLineArgs().Contains("-nmm-voice-test");
            var commandLine = Environment.GetCommandLineArgs();
            int clipArg = Array.IndexOf(commandLine, "-nmm-voice-file");
            if (clipArg >= 0 && clipArg + 1 < commandLine.Length)
            {
                Voice.TestClip = TestWave.Read(commandLine[clipArg + 1], VoiceCodec.SampleRate);
                Voice.TestTone = true;
            }
            Voice.NoMicrophone = Application.isBatchMode && !Voice.TestTone;
            SceneManager.sceneLoaded += SceneLoaded;
            Message.HumanoidEvents.DamageEvent += DamageRemote;
            MessagesGranny.GunDamageEvent += Shot;
            Message.Detectable.NoiseMadeEvent += Noise;
            MessagesGranny.HidingPlayerEvent += Hiding;
            MessagesGranny.HideExitEvent += HideExit;
            MessagesGranny.VentEnterExitEvent += Vent;
        }

        internal void SceneLoaded(Scene scene, LoadSceneMode mode)
        {
            if (!scene.IsValid()) return;
            // After a team game over every peer's credits end in the menu; guests wait there for the host.
            bool teamOver = Life.TeamOver;
            Life.Reset(); Life.TeamOver = false;
            // A guest choosing "exit to main menu" leaves the host's game (not a guest whose game is still starting up,
            // reaching the menu for the first time after joining a host who is already playing).
            string cameFrom = lastScene; lastScene = scene.name;
            if (Active && !IsHost && hostScene != null && scene.name == MainMenuScene && hostScene != MainMenuScene && !teamOver && IsLevel(cameFrom))
                Disconnect("You left the co-op game.");
            Local = null;
            // A level loaded at the host's word that the host has since left (it went back to the lobby mid-load) is not
            // this game being ahead: it follows the host again (Update).
            bool stale = scene.name == requestedScene && scene.name != hostScene;
            loading = Active && !IsHost && !stale && scene.name != hostScene && sceneLoadOperation != null && !sceneLoadOperation.isDone;
            // E.g. a guest walks into the church first: stay there and wait rather than bouncing back.
            aheadScene = Active && !IsHost && hostScene != null && !loading && !stale && scene.name != hostScene && (scene.name != MainMenuScene || teamOver)
                ? scene.name : null;
            if (stale) plugin.Log("Loaded " + scene.name + " but the host has moved on to " + hostScene + ": following");
            if (aheadScene != null) Tell("Waiting for the host...");
            localBed = null; localBedKillPoint = null; localVent = false;
            bathStart = null; leavingBath = false; bathAwake = false;
            nunAudio.Reset(); huntFxPeer = 0; shotsHeard.Clear();
            if (scene.name == OpeningScene)
            {
                openingArrived = Time.realtimeSinceStartup;
                var characters = scene.GetRootGameObjects().FirstOrDefault(g => g.name == "Characters");
                bathStart = characters ? characters.transform.Find("Player Start Bath") : null;
            }
            endingAnnounced = false; endingReturnAt = 0f;
            syncLoad = false; LoadingVariant = -1;
            graphActors.Clear(); graphActorStack.Clear(); graphActor = 0; placedBy.Clear(); graphSignals.Clear();
            busyGraphs.Clear(); claims.Clear(); mySequence = null;
            sharedEndings.Clear(); TeamEndingRunning = false; committedEnding = null; endingsAsked.Clear();
            spawns.Reset();
            Ready = false;
            announcedReady = false;
            pendingSnapshot = null; pendingGraphs = null;
            owners.Clear(); dragging.Clear(); dragDistance.Clear();
            avatars.Clear();
            enemy.Dispose();
            if (world != null) world.Dispose();
            if (Active && scene.name == GameScene) AddSpareItems(scene); // Before discovery: every peer's copy gets the same id.
            hideClaims.Clear(); hideSpots.Clear();
            departed.Clear();
            reservedFor.Clear();
            collected.Clear();
            scareFiredAt.Clear();
            openingDone.Clear();
            gateWaitSince.Clear(); endingWaitTold.Clear(); waitingZone = null;
            pendingSpawns.Clear();
            safeSpots.Clear();
            nextSafeSample.Clear();
            if (Active && scene.name == GameScene) PrepareHideSpots(scene); // Before anyone stands in the way.
            world = new WorldReplicator(!Active || IsHost, Exclude, plugin.Log);
            world.SkipObject = SkipOwned;
            world.Rebuild(); // SceneLoaded runs before Start/random placements.
            sceneReadyAt = Time.realtimeSinceStartup + 2f;
            nextSearch = 0;
            if (Active && IsHost)
            {
                epoch++;
                hostScene = scene.name;
                if (scene.name == MainMenuScene) WorldSeed = UnityEngine.Random.Range(1, int.MaxValue); // A fresh layout next game.
                RecountTools(); // Back in the lobby or the opening: tools for everyone now here.
                readyPeers.Clear();
                Broadcast(ScenePacket("scene"));
            }
            plugin.Log("Scene loaded: " + scene.name + " (" + scene.buildIndex + ")");
        }

        private PlayerFrame Appearance(PlayerFrame frame)
        {
            frame.character = RemotePlayers.ValidCharacter(plugin.Character.Value);
            return frame;
        }

        private bool Exclude(Transform t)
        {
            if (!t || t.name.StartsWith("NMM_", StringComparison.Ordinal)) return true;
            if (t.gameObject.scene.name == OpeningScene) return true; // Each player's own opening.
            return PersonalSequence(t); // WorldReplicator caches and excludes all local player roots.
        }

        // Never driven by the host's copy, which would undo them (re-arming a scare, switching off the acid vent's
        // Nun mid-ending): scares and the fatal endings run on every peer, each game its own copy.
        private static readonly string[] PersonalRoots = { "Vent Death Acid", "Barbed Wire Puzzle (1)/Ending trigger", "Barbed Wire Puzzle (1)/Nun" };

        private static bool PersonalSequence(Transform t)
        {
            for (; t; t = t.parent)
            {
                if (t.name.IndexOf("Scare", StringComparison.OrdinalIgnoreCase) >= 0 || t.name.IndexOf("Scary", StringComparison.OrdinalIgnoreCase) >= 0)
                    return true;
                string named = t.parent ? t.parent.name + "/" + t.name : t.name;
                foreach (string root in PersonalRoots) if (root == t.name || root == named) return true;
            }
            return false;
        }

        private bool SkipOwned(Component c)
        {
            if (!c || world == null) return false;
            var item = c as ItemInteractable;
            if (!item) return false;
            int id;
            return owners.TryGetValue(world.IdFor(item), out id) && id != 0;
        }

        internal void Host(int port)
        {
            Disconnect(null);
            IsHost = true; Active = true; LocalId = 1; hostScene = SceneName; toolCopies = 1;
            WorldSeed = UnityEngine.Random.Range(1, int.MaxValue);
            connectionStarted = Time.realtimeSinceStartup;
            epoch++;
            if (world == null) SceneLoaded(SceneManager.GetActiveScene(), LoadSceneMode.Single);
            world.SetRole(true);
            transport.StartHost(port, 3);
            hostingRun++;
            OpenRouter(port);
            Status = "Hosting on port " + port;
            plugin.Log(Status);
            sceneReadyAt = Time.realtimeSinceStartup + 1f;
        }

        internal void Join(string address, int port, int key = 0)
        {
            Disconnect(null);
            IsHost = false; Active = true; LocalId = 0; joinKey = key;
            connectionStarted = lastHostMessage = Time.realtimeSinceStartup;
            if (world == null) SceneLoaded(SceneManager.GetActiveScene(), LoadSceneMode.Single);
            world.SetRole(false);
            transport.Connect(address, port);
            Status = "Connecting to " + address + ":" + port;
        }

        internal void Disconnect(string reason)
        {
            if (Active) { plugin.Log("Disconnect: " + reason); if (!string.IsNullOrEmpty(reason)) Tell(reason); }
            transport.Stop();
            Discovery.StopAdvertising();
            CloseRouter(false);
            hostingRun++;
            Active = false; Ready = false; IsHost = false; Replaying = false; LocalId = 0;
            avatars.Clear(); players.Clear(); authenticated.Clear(); readyPeers.Clear(); owners.Clear(); kicks.Clear(); Voice.Clear();
            enemy.Dispose(); enemyContext = 0;
            lastSeen.Clear(); lastAction.Clear(); actionCount.Clear(); actionWindow.Clear(); dragging.Clear();
            pendingSnapshot = null; hostScene = null; announcedReady = false;
            sceneLoadOperation = null;
            syncLoad = false; LoadingVariant = -1; // A held level load continues offline.
            if (world != null) world.SetRole(true);
            Status = reason ?? "Offline";
            Time.timeScale = 1;
        }

        internal PlayerFrame FrameOf(int peer)
        {
            if (peer == LocalId) return CapturePlayer();
            PlayerFrame frame;
            return players.TryGetValue(peer, out frame) ? frame : null;
        }

        internal void Update()
        {
            float now = Time.realtimeSinceStartup;
            UpdateRouter(now);
            CatchUpGraphs();
            if (now >= nextSearch)
            {
                nextSearch = now + 2f;
                if (!Local) Local = Object.FindObjectOfType<pReferences>();
            }
            long section = Perf.Begin();
            Voice.Update(); // Also offline, for the microphone test on the voice settings page.
            Perf.End(Perf.Voice, section);
            if (!Active) return;
            section = Perf.Begin();
            foreach (var e in transport.Poll())
            {
                if (!Active) break;
                if (e.Kind == NetEventKind.Connected)
                {
                    plugin.Log("Socket connected to peer " + e.PeerId);
                    lastSeen[e.PeerId] = now;
                    if (!IsHost)
                        Send(1,
                            new Packet { type = "hello", build = plugin.BuildHash, text = CleanName(plugin.PlayerName.Value), target = joinKey != 0 ? joinKey.ToString() : null });
                }
                else if (e.Kind == NetEventKind.Disconnected)
                {
                    if (!IsHost) { Drop("The host left or the connection was lost."); break; }
                    RemovePeer(e.PeerId);
                }
                else if (e.Kind == NetEventKind.Error)
                {
                    plugin.Log("Network: " + e.Message);
                    Status = e.Message;
                    if (!transport.IsRunning) { if (IsHost) Disconnect(e.Message); else Drop(e.Message); }
                }
                else if (e.Kind == NetEventKind.Data)
                {
                    if (e.Payload != null && e.Payload.Length > 0 && e.Payload[0] == VoiceCodec.PacketType) { ReceiveVoice(e.PeerId, e.Payload); continue; }
                    try
                    {
                        var p = e.Decoded as Packet ?? JsonCodec.FromJson<Packet>(WireCodec.Decode(e.Payload));
                        if (p != null) Receive(e.PeerId, p);
                    }
                    catch (Exception ex) { plugin.Log("Rejected packet from " + e.PeerId + ": " + ex.Message); }
                }
            }
            Perf.End(Perf.Poll, section);
            if (!Active) return;
            if (!IsHost && now - lastHostMessage > (loading || !Ready ? 90f : 30f)) { Drop("Connection to the host timed out."); return; }
            foreach (int peer in kicks.Where(x => now >= x.Value).Select(x => x.Key).ToArray()) { kicks.Remove(peer); transport.Disconnect(peer, "Rejected."); }
            if (IsHost)
                foreach (int peer in lastSeen.Where(x => now - x.Value > (readyPeers.Contains(x.Key) ? 30f : 90f)).Select(x => x.Key).ToArray())
                    RemovePeer(peer);
            if (!IsHost && hostScene != null && SceneName != hostScene && !loading && SceneName != aheadScene && !FinishingOpening) LoadHostScene();
            if (now >= sceneReadyAt && !loading)
            {
                if (IsHost && !Ready)
                {
                    Ready = true;
                    AdoptLocalInventory();
                    plugin.Log("Host ready; registered " + WorldCount + " world objects.");
                    SendFull();
                }
                if (!IsHost && LocalId > 0 && SceneName == hostScene && !announcedReady)
                {
                    announcedReady = true;
                    Send(1, new Packet { type = "ready", scene = SceneName, epoch = epoch });
                }
            }
            if (pendingSnapshot != null && now >= sceneReadyAt)
            {
                Replaying = true;
                try { var applyWatch = System.Diagnostics.Stopwatch.StartNew(); ApplyWorld(pendingSnapshot); Perf.Apply.Add(Perf.Ms(applyWatch)); Ready = true; }
                finally { Replaying = false; pendingSnapshot = null; }
            }
            if (IsHost && now >= nextAdvertise)
            {
                nextAdvertise = now + 1f;
                Discovery.Advertise(CleanName(plugin.PlayerName.Value), plugin.Port.Value, authenticated.Count + 1, 4,
                    SceneName != MainMenuScene, plugin.BuildHash);
            }
            if (now >= nextHeartbeat)
            {
                nextHeartbeat = now + 3f;
                if (IsHost) Broadcast(new Packet { type = "ping" }); else Send(1, new Packet { type = "ping" });
            }
            section = Perf.Begin();
            if (Ready && now >= nextPlayers)
            {
                nextPlayers = now + 0.05f;
                var frame = CapturePlayer();
                if (frame != null)
                {
                    Track(frame);
                    if (!IsHost) Send(1, new Packet { type = "player", epoch = epoch, player = frame });
                }
                if (IsHost) Broadcast(new Packet { type = "players", epoch = epoch, players = players.Values.ToArray() });
            }
            if (Ready && IsHost && now >= nextWorld)
            {
                nextWorld = now + 0.1f;
                // Deltas only: peers get a full snapshot when they become ready, then the delta
                // stream plus the replicator's slow refresh sweep keep them in step.
                bool ownership = now >= nextFull;
                if (ownership) nextFull = now + 5f;
                if (readyPeers.Count > 0)
                {
                    var watch = System.Diagnostics.Stopwatch.StartNew();
                    var delta = world.CaptureSnapshot(false);
                    Perf.Capture.Add(Perf.Ms(watch));
                    if (delta != null) SendWorldInBackground(delta, readyPeers.ToArray());
                    if (ownership) SendOwnership();
                }
            }
            Perf.End(Perf.Players, section);
            section = Perf.Begin(); world.Tick(); Perf.End(Perf.WorldTick, section);
            section = Perf.Begin(); Life.Update(); Perf.End(Perf.Life, section);
            Gestures.Update(this);
            NunMassacre.Multiplayer.Players.RemotePlayers.HideAll = Active && TeamEndingRunning;
            if (IsHost && endingReturnAt > 0f && now >= endingReturnAt && SceneName != MainMenuScene)
            {
                endingReturnAt = 0f;
                SceneManager.LoadSceneAsync(MainMenuScene); // Everyone follows the host back to the lobby.
            }
            if (IsHost && Ready) UpdateDrag();
            if (Ready) { WatchSequence(); UpdateEndingWait(); }
            section = Perf.Begin();
            if (IsHost && Ready)
            {
                enemy.Tick(Local, players, avatars, id => world.FindTransform(id));
                UpdateHuntFx();
                HearGunshots();
                Ears.Update();
                UpdateFootsteps();
            }
            if (Ready && Local && Local.camera) nunAudio.Tick(Local.camera.transform.position, HuntedId == LocalId);
            PlayerFrame hostFrame;
            if (!IsHost && Ready && players.TryGetValue(1, out hostFrame) && hostFrame.scene == SceneName)
            {
                nunAudio.ApplyBody(hostFrame.nunBody);
                CatchUpIntro(hostFrame);
            }
            Perf.End(Perf.Enemy, section);
            RemotePlayers.LocalBathing = InBath();
            RemotePlayers.LocalController = Local ? Local.characterController : null;
            section = Perf.Begin(); avatars.Tick(); Perf.End(Perf.Avatars, section);
            if (Ready && now > nextDiagnostic)
            {
                nextDiagnostic = now + 60;
                plugin.Log("NMM session: " + RosterText().Replace('\n', ' ') + "; world=" + WorldCount + "; scene=" + SceneName);
            }
        }

        internal void LateUpdate() { Life.LateUpdate(); }

        private void Receive(int from, Packet p)
        {
            float now = Time.realtimeSinceStartup;
            if (!IsHost) lastHostMessage = now;
            if (IsHost)
            {
                if (p.type == "hello")
                {
                    if (authenticated.Contains(from) || kicks.ContainsKey(from)) return;
                    if (p.protocol != Packet.Protocol || p.build != plugin.BuildHash)
                    {
                        Send(from, new Packet { type = "reject", text = "The host has a different game build or co-op mod version." });
                        kicks[from] = now + 1f; return; // Closed after the reject message is flushed.
                    }
                    if (!InviteAccepted(from, p.target))
                    {
                        Send(from, new Packet { type = "reject", text = "This game is online: join with the host's invite code." });
                        kicks[from] = now + 1f; return;
                    }
                    authenticated.Add(from); lastSeen[from] = now;
                    players[from] = new PlayerFrame { id = from, name = CleanName(p.text), scene = "", rotation = Quaternion.identity };
                    RecountTools();
                    var welcome = ScenePacket("welcome"); welcome.peer = from;
                    bool endingPlays = (TeamEndingRunning || Life.TeamOver) && SceneName != MainMenuScene;
                    if (endingPlays) welcome.scene = MainMenuScene; // The team is on its way back to the lobby.
                    Send(from, welcome);
                    if (endingPlays) TellPeer(from, "The team is finishing an ending. You will join them in the lobby.");
                    plugin.Log("Peer " + from + " authenticated: " + CleanName(p.text));
                    Tell(CleanName(p.text) + " joined");
                    return;
                }
                if (!authenticated.Contains(from)) return;
                lastSeen[from] = now;
                if (p.type == "ping") return;
                if (p.type == "loaded" && p.epoch == epoch) { syncLoaded.Add(from); plugin.Log("Peer " + from + " has the level loaded"); return; }
                if (p.type == "ready" && p.epoch == epoch && p.scene == SceneName)
                {
                    readyPeers.Add(from);
                    if (!Ready) return;
                    if (SceneName == GameScene) AddLateSpares(); // Before Existing(): their own tools come with the level.
                    Send(from, new Packet { type = "ownership", epoch = epoch, owners = OwnershipList() });
                    foreach (var spawn in spawns.Existing()) { spawn.epoch = epoch; Send(from, spawn); }
                    Send(from, new Packet { type = "world", scene = SceneName, epoch = epoch, text = world.Capture(true) });
                    // Puzzle machines' progress.
                    Send(from, new Packet { type = "graphs", scene = SceneName, epoch = epoch, text = world.CaptureGraphStates() });
                    SendGlobals();
                    plugin.Log("Peer " + from + " ready for " + SceneName);
                    return;
                }
                if (!readyPeers.Contains(from) || p.epoch != epoch) return;
                if (p.type == "player" && ValidFrame(p.player))
                {
                    p.player.id = from; p.player.name = players[from].name;
                    if (p.player.scene != SceneName) return;
                    int equippedOwner;
                    if (!string.IsNullOrEmpty(p.player.equipped) && (!owners.TryGetValue(p.player.equipped, out equippedOwner) || equippedOwner != from))
                        p.player.equipped = "";
                    Track(p.player); avatars.Update(p.player);
                }
                else if (p.type == "reviveok")
                {
                    // Relayed to the reviver: the revive took, so their first aid kit is used up.
                    if (p.number == LocalId) Life.KitUsed(p.item);
                    else if (readyPeers.Contains(p.number)) Send(p.number, new Packet { type = "reviveok", epoch = epoch, item = p.item });
                }
                else if (p.type == "action")
                {
                    int last;
                    if (lastAction.TryGetValue(from, out last) && p.number <= last) return;
                    if (!AllowActionRate(from, now)) return;
                    lastAction[from] = p.number;
                    p.peer = from;
                    ProcessAction(p);
                }
                return;
            }
            if (from != 1) return;
            if (p.type == "reject") { Drop(p.text); return; }
            if (p.type == "settings") { ApplyGameSettings(p.settings); return; } // E.g. a third player joined: three of each tool.
            if (p.type == "welcome")
            {
                if (p.protocol != Packet.Protocol || p.build != plugin.BuildHash || p.peer < 2)
                {
                    Drop("The host has a different game build or co-op mod version.");
                    return;
                }
                LocalId = p.peer; transport.SetLocalPeerId(LocalId);
                Status = "Connected as " + CleanName(plugin.PlayerName.Value);
                Tell("Connected to host");
                plugin.Log("Assigned peer " + LocalId);
            }
            if (p.type == "welcome" || p.type == "scene")
            {
                hostScene = p.scene; epoch = p.epoch;
                DifficultyController.CurrentDifficulty = (DifficultyLevel)Mathf.Clamp(p.number, 0, 4);
                ApplyGameSettings(p.settings);
                Ready = false; announcedReady = false;
                if (SceneName != hostScene && !syncLoad && !FinishingOpening) LoadHostScene(); // A synced load is already bringing it.
                else if (SceneName == hostScene) { aheadScene = null; sceneReadyAt = now + 1f; }
                return;
            }
            if (p.type == "loadstart" && p.epoch == epoch) { StartWithHost(p); return; }
            if (p.type == "go" && p.epoch == epoch)
            {
                syncReleased = true;
                if (!string.IsNullOrEmpty(p.scene)) hostScene = p.scene;
                plugin.Log("Everyone has the level: entering");
                return;
            }
            if (p.type == "ping") return;
            if (p.type == "teststep") { if (RuntimeTestDriver.Current != null) RuntimeTestDriver.Current.RunRemote(p.text); return; } // Automated tests only.
            if (p.epoch != epoch) return;
            if (p.type == "graphs" && p.scene == SceneName) { pendingGraphs = p.text ?? ""; return; }
            if (p.type == "pullout") { if (p.scene == SceneName) Life.DragOutOfBed(p.position, p.amount); return; }
            if (p.type == "bedstate") { if (p.scene == SceneName) Life.TurnedOutOfBed((PlayerStates.States)p.number); return; }
            if (p.type == "huntfx") { if (p.scene == SceneName) Life.HuntedEffects(p.number); return; }
            if (p.type == "bedflip")
            {
                var lifted = world.Find(p.target);
                if (lifted && p.scene == SceneName && MessagesGranny.FlipBedEvent != null)
                {
                    bool prior = Replaying; Replaying = true;
                    try { MessagesGranny.FlipBedEvent(lifted.transform, p.amount, p.number / 1000f); } finally { Replaying = prior; }
                }
                return;
            }
            if (p.type == "teleport") { if (p.scene == SceneName) { Life.Teleported(p.text, p.position); Tell("The host moved you"); } return; }
            if (p.type == "world" && p.scene == SceneName)
            {
                if (now < sceneReadyAt) pendingSnapshot = p;
                else
                {
                    Replaying = true;
                    try { var applyWatch = System.Diagnostics.Stopwatch.StartNew(); ApplyWorld(p); Perf.Apply.Add(Perf.Ms(applyWatch)); Ready = true; }
                    finally { Replaying = false; }
                }
            }
            else if (p.type == "players" && p.players != null && p.players.Length <= 4)
            {
                var keep = new HashSet<int>();
                foreach (var frame in p.players)
                {
                    if (!ValidFrame(frame)) continue;
                    keep.Add(frame.id);
                    if (!players.ContainsKey(frame.id) && frame.id != LocalId && frame.id != 1) Tell(frame.name + " joined");
                    if (frame.id == LocalId) continue; // Our own state is authoritative locally.
                    Track(frame);
                    if (frame.id != LocalId) avatars.Update(frame);
                }
                foreach (int id in players.Keys.Where(x => !keep.Contains(x)).ToArray())
                {
                    if (id != LocalId) Tell(players[id].name + " left");
                    PlayerLeft(id);
                    players.Remove(id); avatars.Remove(id); Voice.Remove(id);
                }
            }
            else if (p.type == "commit") ApplyAction(p);
            else if (p.type == "ownership" && p.owners != null && p.owners.Length < 5000)
            {
                foreach (var own in p.owners) SetOwner(own.id, own.peer);
            }
            else if (p.type == "damage" && Ready && Local && Local.health && Finite(p.amount)) Local.health.Damage(Mathf.Clamp(p.amount, 0, 1000));
            else if (p.type == "playerState" && Ready && Local && Local.playerStates && Enum.IsDefined(typeof(PlayerStates.States), p.number))
                Local.playerStates.ChangeState((PlayerStates.States)p.number);
            else if (p.type == "notice") Status = p.text;
            else if (p.type == "globals") ApplyGlobals(p);
            else if (p.type == "revived" && Ready)
            {
                // The revive only counts (and only uses up a first aid kit) if we had not bled out meanwhile.
                if (Life.Revived(Finite(p.amount) ? p.amount : CoopLife.RevivedHealth, CleanName(p.text)) && !string.IsNullOrEmpty(p.item))
                    Send(1, new Packet { type = "reviveok", epoch = epoch, number = p.number, item = p.item });
            }
            else if (p.type == "bleedout" && Ready) Life.Die("everyone is down");
            else if (p.type == "gameover" && Ready) Life.TeamGameOver();
            else if (p.type == "spawn") { if (Ready) spawns.Create(p); else if (pendingSpawns.Count < 64) pendingSpawns.Add(p); }
            else if (p.type == "tell") Tell(CleanText(p.text));
            else if (p.type == "reviveok") Life.KitUsed(p.item);
            else if (p.type == "ending") { Life.TeamOver = true; Tell(CleanName(p.text) + " reached the end"); }
            else if (p.type == "hidefull" && Ready && world != null) NoRoom(world.Find(p.target) as HideBedInteractable);
        }

        // How fast this player is really moving, from where they were at the last measure: teammates walk or run their
        // avatar by it. (The character controller's own velocity is from its last move and can linger after the player
        // has stopped, which had teammates walking on the spot instead of standing in their idle.)
        private Vector3 lastSpot, measuredVelocity;
        private float lastSpotTime = -1f;

        private Vector3 MeasuredVelocity(Vector3 position)
        {
            float now = Time.time, gap = now - lastSpotTime;
            if (lastSpotTime < 0f || gap > 0.5f) { lastSpot = position; lastSpotTime = now; measuredVelocity = Vector3.zero; return measuredVelocity; }
            if (gap < 0.03f) return measuredVelocity; // Asked again within the frame: the same answer.
            Vector3 moved = (position - lastSpot) / gap;
            measuredVelocity = moved.magnitude > 12f ? Vector3.zero : moved; // A teleport is no run.
            lastSpot = position; lastSpotTime = now;
            return measuredVelocity;
        }

        // Peeking (Q and E): the game slides the eyes sideways up to its peek distance (less with a wall in the way).
        private static readonly System.Reflection.FieldInfo PeekDistance = HarmonyLib.AccessTools.Field(typeof(FirstPersonController), "peakDist");

        private float PeekLean()
        {
            if (!Local || !Local.head || !Local.firstPersonController || PeekDistance == null) return 0f;
            float most = (float)PeekDistance.GetValue(Local.firstPersonController);
            return most > 0.01f ? Mathf.Clamp(Local.head.localPosition.x / most, -1f, 1f) : 0f;
        }

        private PlayerFrame CapturePlayer()
        {
            if (LocalId < 1) return null;
            if (!Local || !Local.health || !Local.firstPersonController)
                return Appearance(new PlayerFrame { id = LocalId, name = CleanName(plugin.PlayerName.Value),
                    scene = SceneName, rotation = Quaternion.identity, health = 100, height = 1.8f, equipped = "" });
            var t = ((Component)Local).transform;
            var c = Local.camera;
            var frame = new PlayerFrame
            {
                id = LocalId, name = CleanName(plugin.PlayerName.Value), scene = SceneName,
                position = t.position, rotation = t.rotation, look = c ? c.transform.forward : t.forward,
                health = Local.health.HealthMeter, height = Local.characterController ? Local.characterController.height : 1.8f,
                state = Local.playerStates ? (int)Local.playerStates.StateCheck() : 0,
                lives = LivesSystem.instance ? LivesSystem.instance.currentLives : 0,
                equipped = Local.inventory && Local.inventory.EquippedItem ? world.IdFor(Local.inventory.EquippedItem) : "",
                light = CarryingLight(),
                crouching = Local.firstPersonController.IsCrouching, vent = localVent || TestVent || InDuct(t.position), bath = InBath(),
                jumping = Local.firstPersonController.isJumping, shots = GameHooks.ShotsFired, points = Gestures.Points, whistles = Gestures.Whistles,
                hunting = IsHost ? enemy.TargetId : 0, nunBody = IsHost ? enemy.BodyShape : Vector3.zero, lean = PeekLean(),
                bed = world.IdFor(localBed), bedKillPoint = world.IdFor(localBedKillPoint), body = true,
                velocity = MeasuredVelocity(t.position)
            };
            Life.FillFrame(frame);
            if (frame.bath) { leavingBath = true; bathSpot = frame.position; bathTurn = frame.rotation; }
            else if (leavingBath && Local.playerStates && Local.playerStates.StateCheck() == PlayerStates.States.Cutscene && bathStart)
            {
                frame.bath = true; frame.position = bathSpot; frame.rotation = bathTurn; // Still "in the tub" for the others.
            }
            else if (bathStart && !bathAwake && Time.realtimeSinceStartup - openingArrived < 30f)
            {
                // Just arrived, still in the intro before waking in the tub: in the bath already (the letter waits).
                frame.bath = true; frame.position = bathStart.position; frame.rotation = bathStart.rotation;
                leavingBath = false;
            }
            else leavingBath = false;
            return Appearance(frame);
        }

        internal bool Drop(Inventory inventory)
        {
            if (!Active || Replaying || PersonalScene) return true;
            if (Ready && inventory.EquippedItem && !inventory.isBusy && inventory.inventoryInteraction && !inventory.isOpen()
                && inventory.EquippedItem.CanDropItem(Local.camera.transform.position, Local.camera.transform.forward))
                Request(new Packet { action = "drop", target = world.IdFor(inventory.EquippedItem) });
            return false;
        }

        /// <summary>A player left mid-game: release what waited on them (their close-up, the clock, the lock
        /// they were picking, the coffin), so the others can use it.</summary>
        private void PlayerLeft(int peer)
        {
            if (peer == LocalId || peer <= 0) return;
            departed.Add(peer);
            hideClaims.Remove(peer);
            GameHooks.AbandonMinigames(peer, this);
            foreach (var graph in busyGraphs.Where(x => x.Value.Actor == peer).Select(x => x.Key).ToList()) busyGraphs.Remove(graph);
        }

        /// <summary>Host: a message for one player (the host itself included).</summary>
        private void TellPeer(int peer, string text)
        {
            if (peer == LocalId) Tell(text); else Send(peer, new Packet { type = "tell", epoch = epoch, text = text });
        }

        private void Vent(bool value) { localVent = value; }

        /// <summary>In a duct by its shape: the floor underfoot and the ceiling just overhead are both the vent's (the main
        /// school's ducts have no enter and exit triggers of their own).</summary>
        private static bool InDuct(Vector3 feet)
        {
            RaycastHit below, above;
            int mask = LayerMask.GetMask("Wall", "Ground", "Ceiling");
            return Physics.Raycast(feet + Vector3.up * 0.3f, Vector3.down, out below, 0.8f, mask, QueryTriggerInteraction.Ignore) && below.collider.name.IndexOf("Vent", StringComparison.Ordinal) >= 0
                && Physics.Raycast(feet + Vector3.up * 0.3f, Vector3.up, out above, 1.6f, mask, QueryTriggerInteraction.Ignore) && above.collider.name.IndexOf("Vent", StringComparison.Ordinal) >= 0;
        }

        internal Gestures Gestures { get; private set; }

        // A lit flashlight, lighter or held lamp: world-lighting lights in the camera rig. The body also
        // carries a scenario fill light ("Blue light - Nun Massacre") that is always on; it is not carried.
        private bool CarryingLight()
        {
            if (!Local || !Local.camera) return false;
            Transform rig = Local.camera.transform.parent ? Local.camera.transform.parent : Local.camera.transform;
            foreach (var light in rig.GetComponentsInChildren<Light>())
                if (light.enabled && light.intensity > 0f && (light.cullingMask & 1) != 0) return true; // Not the hands-only viewmodel light.
            return false;
        }

        /// <summary>EventTrigger: a zone that changes the world for everyone is opened through the host.</summary>
        /// <summary>WarpTo moved the local player (the letter's walk to the hill): teammates warped to the same spot
        /// stand side by side instead of inside each other.</summary>
        internal void SpreadOut(Transform warped)
        {
            if (!OpeningTogether || !warped || !Local || warped != ((Component)Local).transform) return;
            var ids = players.Keys.Concat(new[] { LocalId }).Distinct().OrderBy(id => id).ToList();
            int place = ids.IndexOf(LocalId);
            if (place <= 0) return;
            var controller = Local.characterController;
            if (controller) controller.enabled = false;
            warped.position += warped.right * (place % 2 == 1 ? 0.9f : -0.9f) * ((place + 1) / 2);
            if (controller) controller.enabled = true;
        }

        private void SendGlobals()
        {
            Broadcast(new Packet { type = "globals", epoch = epoch, number = gas ? 1 : 0, text = LockpickItem.lockpickCount.ToString() });
        }

        private void ApplyGlobals(Packet p)
        {
            Replaying = true;
            try
            {
                bool nextGas = p.number == 1;
                if (gas != nextGas) { gas = nextGas; if (MessagesGranny.PoisonGasSetEvent != null) MessagesGranny.PoisonGasSetEvent(gas); }
                if (int.TryParse(p.text, out paperclips)) LockpickItem.lockpickCount = Mathf.Max(0, paperclips);
            }
            finally { Replaying = false; }
        }

        private void RemovePeer(int peer)
        {
            foreach (var item in owners.Where(x => x.Value == peer).Select(x => x.Key).ToArray())
            {
                PlayerFrame frame;
                if (!players.TryGetValue(peer, out frame)) continue;
                Vector3 safe; // Not at the bottom of a pit or inside a vent: their last spot on walkable floor.
                var drop = new Packet { type = "commit", action = "drop", target = item, peer = peer, epoch = epoch,
                    position = safeSpots.TryGetValue(peer, out safe) ? safe : frame.position, direction = Vector3.forward };
                ApplyAction(drop); Broadcast(drop);
            }
            LetGo(peer);
            PlayerLeft(peer);
            PlayerFrame leaving;
            if (players.TryGetValue(peer, out leaving)) Tell(leaving.name + " left");
            authenticated.Remove(peer);
            readyPeers.Remove(peer);
            lastSeen.Remove(peer);
            players.Remove(peer);
            avatars.Remove(peer);
            dragging.Remove(peer);
            Voice.Remove(peer);
            RecountTools();
            transport.Disconnect(peer, "Peer left or timed out.");
            plugin.Log("Peer removed: " + peer);
        }

        /// <summary>
        /// A guest lost the host. Its level is only partly synchronised, so return it to the main
        /// menu, where the multiplayer panel shows the reason.
        /// </summary>
        private void Drop(string reason)
        {
            Disconnect(reason);
            ShowLobbyOnMenu = true;
            if (SceneName != MainMenuScene && Application.CanStreamedLevelBeLoaded(MainMenuScene)) SceneManager.LoadSceneAsync(MainMenuScene);
        }

        /// <summary>Set when a guest was dropped; the main menu opens the multiplayer panel once.</summary>
        internal bool ShowLobbyOnMenu;

        /// <summary>Sends this player's voice: guests to the host, the host to every guest.</summary>
        internal void SendVoice(byte[] packet)
        {
            if (!Connected) return;
            VoiceCodec.SetSpeaker(packet, LocalId);
            Perf.SentBytes += packet.Length;
            if (IsHost) { foreach (int peer in authenticated) transport.Send(peer, packet); }
            else transport.Send(1, packet);
        }

        /// <summary>Voice bypasses the JSON path. The host stamps the sender and relays it to the others.</summary>
        private void ReceiveVoice(int from, byte[] packet)
        {
            if (packet.Length < VoiceCodec.HeaderBytes || packet.Length > VoiceCodec.HeaderBytes + VoiceCodec.MaxSamples / 2) return;
            if (IsHost)
            {
                if (!authenticated.Contains(from)) return;
                float now = Time.realtimeSinceStartup, start;
                if (!voiceWindow.TryGetValue(from, out start) || now - start > 1f) { voiceWindow[from] = now; voiceCount[from] = 0; }
                if (++voiceCount[from] > 60) return; // Far more than real speech produces.
                lastSeen[from] = now;
                VoiceCodec.SetSpeaker(packet, from);
                foreach (int peer in authenticated) if (peer != from) transport.Send(peer, packet);
                Voice.Receive(from, packet);
            }
            else
            {
                if (from != 1) return;
                lastHostMessage = Time.realtimeSinceStartup;
                int speaker = VoiceCodec.Speaker(packet);
                if (speaker != LocalId && speaker > 0) Voice.Receive(speaker, packet);
            }
        }

        internal void Tell(string message) { plugin.Log("NOTICE " + message); if (Notice != null) Notice(message); }
        internal string WorldFingerprint() { return world == null ? "" : world.Fingerprint(); } // Automated tests.
        internal string IdOf(Component component) { return world == null ? null : world.IdFor(component); }
        internal int OwnerOf(string id) { int peer; return id != null && owners.TryGetValue(id, out peer) ? peer : 0; }
        internal IDictionary<int, PlayerFrame> Players { get { return players; } }
        internal RemotePlayers Avatars { get { return avatars; } }
        internal int Epoch { get { return epoch; } }
        internal string NameOf(int peer) { PlayerFrame frame; return players.TryGetValue(peer, out frame) && frame.name != null ? frame.name : "teammate"; }

        /// <summary>Names in join order; includes this player once the session is established.</summary>
        /// <summary>Everyone in the session for the lobby: id, name, character id, and whether this is the local player.</summary>
        internal List<KeyValuePair<int, string[]>> Roster()
        {
            var list = players.Values.OrderBy(p => p.id).Select(p => new KeyValuePair<int,
                string[]>(p.id, new[] { p.name, p.character, p.id == LocalId ? "1" : "0" })).ToList();
            if (Active && LocalId > 0 && !players.ContainsKey(LocalId))
                list.Add(new KeyValuePair<int, string[]>(LocalId, new[] { CleanName(plugin.PlayerName.Value), plugin.Character.Value, "1" }));
            return list.OrderBy(p => p.Key).ToList();
        }

        internal string[] PlayerNames()
        {
            System.Func<string, string> look = id => RemotePlayers.CharacterNames[Array.IndexOf(RemotePlayers.CharacterIds, RemotePlayers.ValidCharacter(id))];
            var names = players.Values.OrderBy(p => p.id).Select(p => p.name + (p.id == 1 ? " (HOST)" : "") + " - " + look(p.character)).ToList();
            if (Active && LocalId > 0 && !players.ContainsKey(LocalId))
                names.Insert(IsHost ? 0 : names.Count, CleanName(plugin.PlayerName.Value) + (IsHost ? " (HOST)" : "") + " - " + look(plugin.Character.Value));
            return names.ToArray();
        }

        internal bool Connected { get { return Active && LocalId > 0; } }
        private Ownership[] OwnershipList() { return owners.Select(x => new Ownership { id = x.Key, peer = x.Value }).ToArray(); }
        private void SendOwnership() { Broadcast(new Packet { type = "ownership", epoch = epoch, owners = OwnershipList() }); SendGlobals(); }

        internal void SendFull()
        {
            if (!IsHost || !Ready) return;
            SendOwnership();
            Broadcast(new Packet { type = "world", epoch = epoch, scene = SceneName, text = world.Capture(true) });
        }

        private void Send(int peer, Packet p)
        {
            if (peer == LocalId || !Active) return;
            byte[] payload = WireCodec.Encode(JsonCodec.ToJson(p));
            Perf.SentBytes += payload.Length;
            transport.Send(peer, payload);
        }

        internal void Broadcast(Packet p)
        {
            if (!Active || authenticated.Count == 0) return;
            byte[] payload = WireCodec.Encode(JsonCodec.ToJson(p)); // Once for everyone.
            foreach (int peer in authenticated)
            {
                if (peer == LocalId) continue;
                Perf.SentBytes += payload.Length;
                transport.Send(peer, payload);
            }
        }

        // Network threads: packets arrive already parsed, world snapshots included.
        private static object DecodePayload(byte[] payload)
        {
            if (payload == null || payload.Length == 0 || payload[0] == VoiceCodec.PacketType) return null;
            var p = JsonCodec.FromJson<Packet>(WireCodec.Decode(payload));
            if (p != null && p.type == "world" && !string.IsNullOrEmpty(p.text)) p.Parsed = WorldReplicator.Parse(p.text);
            return p;
        }

        private void ApplyWorld(Packet p)
        {
            // Traps the Nun threw before we joined: create them first, then the world gives their state.
            foreach (var spawn in pendingSpawns) if (spawn.epoch == epoch) spawns.Create(spawn);
            pendingSpawns.Clear();
            if (p.Parsed != null) world.Apply(p.Parsed);
            else world.Apply(p.text);
        }

        private void SendWorldInBackground(WorldSnapshot delta, int[] peers)
        {
            if (encoder == null)
            {
                encoder = new System.Threading.Thread(() =>
                {
                    foreach (var job in outbox.GetConsumingEnumerable())
                        try { job(); } catch (Exception ex) { plugin.Log("World send failed: " + ex.Message); }
                }) { IsBackground = true, Name = "NMM world encoder" };
                encoder.Start();
            }
            string scene = SceneName; int sendEpoch = epoch;
            try
            {
                outbox.Add(() =>
                {
                    byte[] payload = WireCodec.Encode(JsonCodec.ToJson(new Packet { type = "world", scene = scene,
                        epoch = sendEpoch, text = WorldReplicator.Serialize(delta) }));
                    System.Threading.Interlocked.Add(ref Perf.SentBytes, payload.Length);
                    foreach (int peer in peers) transport.Send(peer, payload);
                });
            }
            catch (InvalidOperationException) { } // Shutting down.
        }

        internal string RosterText()
        {
            return string.Join("\n",
                players.Values.OrderBy(p => p.id).Select(p => p.name + (p.id == 1 ? " (host)" : "") + " â€¢ " + Mathf.RoundToInt(p.health) + " HP").ToArray());
        }

        private static bool Finite(Vector3 v) { return Finite(v.x) && Finite(v.y) && Finite(v.z); }
        private static bool Finite(float v) { return !float.IsNaN(v) && !float.IsInfinity(v) && Mathf.Abs(v) < 100000; }

        private static bool ValidFrame(PlayerFrame p)
        {
            return p != null && p.id > 0 && p.id < 100000 && p.name != null && p.name.Length <= 32 && p.scene != null
                && p.scene.Length < 128 && Finite(p.position) && Finite(p.look) && Finite(p.health) && Finite(p.height)
                && Finite(p.rotation.x) && Finite(p.rotation.y) && Finite(p.rotation.z) && Finite(p.rotation.w);
        }

        private static string CleanName(string name)
        {
            if (string.IsNullOrWhiteSpace(name)) return "Player";
            return new string(name.Where(c => !char.IsControl(c) && c != '<' && c != '>').Take(32).ToArray());
        }

        /// <summary>A message from the host, shown as a notice: no markup, and no longer than a notice line holds.</summary>
        private static string CleanText(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return "";
            return new string(text.Where(c => !char.IsControl(c) && c != '<' && c != '>').Take(160).ToArray());
        }

        public void Dispose()
        {
            CloseRouter(true);
            Disconnect(null);
            outbox.CompleteAdding(); // Ends the world encoder thread.
            SceneManager.sceneLoaded -= SceneLoaded;
            Message.HumanoidEvents.DamageEvent -= DamageRemote;
            MessagesGranny.GunDamageEvent -= Shot;
            Message.Detectable.NoiseMadeEvent -= Noise;
            MessagesGranny.HidingPlayerEvent -= Hiding;
            MessagesGranny.HideExitEvent -= HideExit;
            MessagesGranny.VentEnterExitEvent -= Vent;
            if (world != null) world.Dispose(); avatars.Dispose(); transport.Dispose(); Life.Dispose(); Discovery.Dispose(); Voice.Dispose();
        }
    }
}
