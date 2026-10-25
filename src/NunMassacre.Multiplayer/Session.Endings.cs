using System;
using System.Collections.Generic;
using System.Linq;
using GrannyClone;
using UnityEngine;

namespace NunMassacre.Multiplayer
{
    // Endings, played together: the group sets one off only when everyone still playing is there, it plays on every
    // game at once, and the unlocks are earned together. The coffin's lid is kept for the player lying in it.
    internal sealed partial class Session
    {
        // One-player parts of a story moment: the coffin's lid is closed only by whoever lies in the coffin.
        private static readonly string[][] ReservedAfter = { new[] { "Coffin", "END Lid Scenario" } };
        private readonly Dictionary<string, int> reservedFor = new Dictionary<string, int>();
        private readonly HashSet<string> collected = new HashSet<string>(); // Paperclips already taken (host).

        // Every ending: the good ending (walking out once the Nun has burned), the coffin ending (the lid closing, in the
        // main game and in PDM), the Backrooms way out, a scenario's closing text (the Carnival's, after the clown doll is
        // set in the rocking chair), and the two that kill you (staying in the acid vent, falling into the barbed wire).
        // When one starts on anyone's game it starts on every game at once, run in full on each (the fade, the text, the
        // end screen, the credits; in a fatal one, each player's own death), with teammates' bodies hidden so nobody
        // stands in the shot.
        // Path, and the event that makes it the ending (null: starting at all). The scenarios' story machine also runs the
        // opening text, on "Intro": only its "Outro" is an ending.
        private static readonly string[][] TeamEndings =
        {
            new[] { "Doll puzzle/Ending Trigger", null },
            new[] { "Mary Statue Puzzle Ending/END Lid Scenario", null },
            new[] { "Locked Stairs Door/EndTrigger", null },
            new[] { "Alternate Scenarios/Intro Outro", "Outro" },
            new[] { "Vent Death Acid", null },
            new[] { "Barbed Wire Puzzle (1)/Ending trigger", null },
        };

        // Where each ending is set off, for the group check: a zone walked into, or a part used.
        private static readonly string[] EndingZones =
        {
            "Doll puzzle/Ending Trigger", "Locked Stairs Door/EndTrigger", "Vent Death Acid/Start trigger", "Barbed Wire Puzzle (1)/Ending trigger",
        };
        private static readonly string[] EndingParts = { "Mary Statue Puzzle Ending/END Lid Scenario", "Ending/RockingChair" };

        private readonly HashSet<NodeCanvas.Framework.Graph> sharedEndings = new HashSet<NodeCanvas.Framework.Graph>();
        internal bool TeamEndingRunning { get; private set; }
        private string committedEnding; // Host: the team ending everyone is watching (the first one asked for).
        internal bool SharedEnding(NodeCanvas.Framework.Graph graph) { return graph != null && sharedEndings.Contains(graph); }

        // A scenario's closing text comes after its own ending (PDM's lid, the Backrooms way out): the same ending going on,
        // never a rival to it.
        private const string ClosingText = "Alternate Scenarios/Intro Outro";

        /// <summary>Host: this ending may start, given the one everyone may already be watching.</summary>
        private bool EndingAllowed(Transform ending, string id)
        {
            return committedEnding == null || committedEnding == id || GraphDump.Path(ending).EndsWith(ClosingText, StringComparison.Ordinal);
        }

        private static bool IsTeamEnding(NodeCanvas.Framework.Graph graph, string eventName)
        {
            var owner = graph.agent as Component;
            if (!owner) return false;
            string path = GraphDump.Path(owner.transform);
            return TeamEndings.Any(e => path.EndsWith(e[0], StringComparison.Ordinal) && (e[1] == null || e[1] == eventName));
        }

        /// <summary>An ending's machine, by where it is (any event): for checking a relayed ending.</summary>
        private static bool IsTeamEnding(Transform t)
        {
            string path = GraphDump.Path(t);
            return TeamEndings.Any(e => path.EndsWith(e[0], StringComparison.Ordinal));
        }

        private static string EndingEvent(Transform t)
        {
            string path = GraphDump.Path(t);
            var ending = TeamEndings.FirstOrDefault(e => path.EndsWith(e[0], StringComparison.Ordinal));
            return ending == null ? null : ending[1];
        }

        private static bool IsEndingZone(Transform t)
        {
            string path = GraphDump.Path(t);
            return EndingZones.Any(z => path.EndsWith(z, StringComparison.Ordinal));
        }

        private static bool IsEndingPart(Transform t)
        {
            string path = GraphDump.Path(t);
            return EndingParts.Any(z => path.EndsWith(z, StringComparison.Ordinal));
        }

        // How an ending is named between games: its world id, or (the fatal endings, which each game runs as its own and
        // so are not in the world's registry) its path in the level.
        private string EndingKey(Transform t)
        {
            string id = world.IdFor(t);
            return !string.IsNullOrEmpty(id) ? id : "path:" + GraphDump.Path(t);
        }

        private Component EndingOwner(string key)
        {
            if (string.IsNullOrEmpty(key)) return null;
            if (!key.StartsWith("path:", StringComparison.Ordinal)) return world.Find(key);
            var owner = InLevel<NodeCanvas.StateMachines.FSMOwner>(key.Substring(5));
            return owner ? (Component)owner : null;
        }

        /// <summary>A component of this level by its path (one of the pieces each game keeps as its own).</summary>
        private static T InLevel<T>(string path) where T : Component
        {
            if (string.IsNullOrEmpty(path)) return null;
            return Resources.FindObjectsOfTypeAll<T>().FirstOrDefault(c => c && c.gameObject.scene.IsValid() && GraphDump.Path(c.transform) == path);
        }

        // An ending's zone the local player is waiting in: asked about again every couple of seconds while they stay in it,
        // so it goes off once the others are near, or once the wait is over, without stepping out and back in.
        private global::EventTrigger waitingZone;
        private float nextEndingAsk;
        private readonly Dictionary<string, float> endingWaitTold = new Dictionary<string, float>();

        /// <summary>
        /// The local player walking into an ending's zone asks the host, which sets it off once everyone still playing is
        /// there. False skips the local trigger: it goes off from the host's word (FireEndingZone).
        /// </summary>
        internal bool EndingZone(global::EventTrigger zone, Collider other)
        {
            if (!Active || !Ready || Replaying || !zone || !other || !Local || !IsEndingZone(zone.transform)) return true;
            var body = ((Component)Local).transform;
            if (other.transform != body && !other.transform.IsChildOf(body)) return true;
            if (!TeamEndingRunning) { waitingZone = zone; AskEndingZone(); }
            return false;
        }

        private void AskEndingZone()
        {
            if (Time.realtimeSinceStartup < nextEndingAsk) return;
            nextEndingAsk = Time.realtimeSinceStartup + 2f;
            Request(new Packet { action = "endingzone", text = GraphDump.Path(waitingZone.transform) });
        }

        private void UpdateEndingWait()
        {
            if (!waitingZone) return;
            var shape = waitingZone.GetComponent<Collider>();
            bool inside = Local && shape && shape.bounds.Contains(((Component)Local).transform.position + Vector3.up * 0.5f);
            if (TeamEndingRunning || !inside) { waitingZone = null; return; }
            AskEndingZone();
        }

        /// <summary>The host's word for an ending zone: it goes off here, as the local player's own, as if just walked into.</summary>
        private void FireEndingZone(string path)
        {
            var zone = InLevel<global::EventTrigger>(path);
            waitingZone = null;
            if (!zone || zone._on_enter == null) return;
            bool prior = Replaying; int priorActor = ReplayActor;
            Replaying = false; ReplayActor = 0;
            try { zone._on_enter.Invoke(); }
            finally { Replaying = prior; ReplayActor = priorActor; }
            plugin.Log("Co-op: everyone is here; " + path + " goes off");
        }

        /// <summary>
        /// Host: everyone still playing (downed included, spectators not) is at this ending. If not, whoever tried it is told
        /// who they are waiting for; after a minute the team goes ahead without anyone who has not come.
        /// </summary>
        private bool EndingTogether(string key, Vector3 at, int asker)
        {
            string[] away = AwayFrom(at);
            if (away.Length == 0 || GateWaitedTooLong(key, away)) { gateWaitSince.Remove(key); return true; }
            float told;
            string toldKey = asker + "|" + key;
            if (!endingWaitTold.TryGetValue(toldKey, out told) || Time.realtimeSinceStartup - told > 6f)
            {
                endingWaitTold[toldKey] = Time.realtimeSinceStartup;
                TellPeer(asker, "Wait for everyone: you finish together (" + string.Join(", ", away) + ").");
            }
            return false;
        }

        private void ShareEnding(NodeCanvas.Framework.Graph graph)
        {
            sharedEndings.Add(graph);
            TeamEndingRunning = true;
            var owner = graph.agent as Component;
            string id = owner ? EndingKey(owner.transform) : null;
            plugin.Log("Co-op: team ending started here (" + (owner ? GraphDump.Path(owner.transform) : "?") + "); everyone watches it");
            if (!Replaying && !string.IsNullOrEmpty(id)) Request(new Packet { action = "teamending", target = id });
        }

        /// <summary>Every game: start the team ending the host relayed, unless it is already running here.</summary>
        private void StartSharedEnding(Component component)
        {
            var owner = component ? component.GetComponent<NodeCanvas.StateMachines.FSMOwner>() : null;
            if (!owner || owner.graph == null) return;
            bool fresh = sharedEndings.Add(owner.graph); // False: it began on this game.
            TeamEndingRunning = true;
            string ending = EndingEvent(owner.transform);
            if (owner.isRunning)
            {
                // A machine that runs all along (the scenarios' story text): the ending is its event. Usually it has
                // arrived here already (the same puzzle ran here too); if not, it is sent now.
                if (ending != null && fresh && !Showing(owner.graph))
                {
                    bool was = Replaying; int wasActor = ReplayActor;
                    Replaying = false; ReplayActor = 0;
                    try { owner.SendEvent(ending); }
                    finally { Replaying = was; ReplayActor = wasActor; }
                    plugin.Log("Co-op: watching the team ending (" + GraphDump.Path(owner.transform) + ", " + ending + ")");
                }
                return; // Otherwise: started here, this is the game it began on.
            }
            if (!owner.gameObject.activeSelf) owner.gameObject.SetActive(true);
            // Started as this game's own (not inside the relayed action, where its first steps would count as the other
            // player's and skip the end screen here).
            bool prior = Replaying; int priorActor = ReplayActor;
            Replaying = false; ReplayActor = 0;
            try { owner.StartBehaviour(); }
            finally { Replaying = prior; ReplayActor = priorActor; }
            plugin.Log("Co-op: watching the team ending (" + GraphDump.Path(owner.transform) + ")");
        }

        private bool endingAnnounced;
        private float endingReturnAt;

        private void Reserve(StateInteractable part, int index, int peer)
        {
            if (!part || index != 1 || world == null || !part.transform.parent) return;
            foreach (var pair in ReservedAfter)
            {
                if (part.name != pair[0]) continue;
                var reserved = part.transform.parent.Find(pair[1]);
                var reservedPart = reserved ? reserved.GetComponent<StateInteractable>() : null;
                if (reservedPart) reservedFor[world.IdFor(reservedPart)] = peer;
            }
        }

        private bool Reserved(string target, int peer)
        {
            int holder;
            return !string.IsNullOrEmpty(target) && reservedFor.TryGetValue(target, out holder) && holder != peer
                && players.ContainsKey(holder) && !departed.Contains(holder);
        }

        private int unlocksGiven = -1; // The scene (epoch) whose end-of-game unlocks this game has given.

        /// <summary>An anomaly (ten unlock the Backrooms) found by anyone runs on every game, as any interaction does, and
        /// counts in every player's own save. One already in this player's save only hides again, not counted twice.</summary>
        internal bool AnomalyFound(AnomalyCollect anomaly)
        {
            if (!Active || !Replaying || !anomaly || !MyPlayerPrefs.GetBool("Anomaly_" + anomaly.id, false)) return true;
            anomaly.gameObject.SetActive(false);
            return false;
        }

        /// <summary>This game just gave the unlocks for beating it (the game's own rules: Factory; Circus without losing a
        /// life or on Extreme): every teammate's game gives them too, by the same rules for that player.</summary>
        internal void UnlocksEarned()
        {
            if (!Active || Replaying || unlocksGiven == epoch) return;
            unlocksGiven = epoch;
            plugin.Log("Unlocks: the game gave its end-of-game unlocks here; so does everyone else's");
            var p = new Packet { type = "action", action = "unlocks", peer = LocalId, epoch = epoch, number = ++actionSequence };
            if (IsHost) ProcessAction(p); else Send(1, p);
        }

        private void ApplyUnlocks(Packet p)
        {
            if (p.peer == LocalId || unlocksGiven == epoch) return; // Already given in this player's save.
            unlocksGiven = epoch;
            bool prior = Replaying; Replaying = true;
            GameObject temporary = null;
            try
            {
                var handler = UnityEngine.Object.FindObjectOfType<AchievementHandler>();
                if (!handler) { temporary = new GameObject("NMM_Unlocks"); handler = temporary.AddComponent<AchievementHandler>(); }
                handler.UnlockDifficultyAchievement();
                plugin.Log("Unlocks: end-of-game unlocks given here too (peer " + p.peer + " beat it): Factory "
                    + MyPlayerPrefs.GetBool("FACTORY_unlocked", false) + ", Circus "
                    + MyPlayerPrefs.GetBool("CIRCUS_unlocked", false));
            }
            catch (Exception ex) { plugin.Log("Unlocks: " + ex.Message); }
            finally { if (temporary) UnityEngine.Object.Destroy(temporary); Replaying = prior; }
        }

        private bool ClaimEnding(NodeCanvas.Framework.Graph graph)
        {
            var owner = graph.agent as Component;
            if (!owner) return true;
            string id = EndingKey(owner.transform);
            if (IsHost)
            {
                if (!EndingAllowed(owner.transform, id))
                {
                    plugin.Log("Co-op: " + GraphDump.Path(owner.transform) + " not started: another ending is already playing.");
                    return false;
                }
                if (committedEnding == null) committedEnding = id;
                ShareEnding(graph);
                return true;
            }
            if (!Replaying && endingsAsked.Add(id))
            {
                Request(new Packet { action = "teamending", target = id });
                plugin.Log("Co-op: asked the host to start the team ending (" + GraphDump.Path(owner.transform) + ")");
            }
            return false;
        }

        private readonly HashSet<string> endingsAsked = new HashSet<string>(); // A guest's endings waiting on the host.

        internal void GameEnding()
        {
            if (!Active || !Ready || SceneName == MainMenuScene) return;
            bool teamGameOver = Life.State == Gameplay.LifeState.GameOver;
            Life.TeamOver = true; // Arriving in the menu must not count as leaving the session.
            if (teamGameOver || endingAnnounced) return;
            if (IsHost) AnnounceEnding(CleanName(plugin.PlayerName.Value), false);
            else { endingAnnounced = true; Request(new Packet { action = "ending" }); }
        }

        /// <summary>Host: after a team game over, return everyone to the lobby once the death scene has played.</summary>
        internal void ReturnToLobbyIn(float seconds)
        {
            if (!IsHost) return;
            float at = Time.realtimeSinceStartup + seconds;
            endingReturnAt = endingReturnAt > 0f ? Mathf.Min(endingReturnAt, at) : at; // One return to the lobby, the earliest.
        }

        private void AnnounceEnding(string name, bool returnToMenu)
        {
            if (endingAnnounced) return;
            endingAnnounced = true;
            Life.TeamOver = true;
            Broadcast(new Packet { type = "ending", epoch = epoch, text = name });
            Tell(name + " reached the end");
            plugin.Log("Co-op: " + name + " reached an ending; the team returns to the lobby.");
            if (returnToMenu) endingReturnAt = Time.realtimeSinceStartup + 12f;
        }
    }
}
