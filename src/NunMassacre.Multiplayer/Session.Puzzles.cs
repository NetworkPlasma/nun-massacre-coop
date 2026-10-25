using System;
using System.Collections.Generic;
using System.Linq;
using GrannyClone;
using UnityEngine;

namespace NunMassacre.Multiplayer
{
    // Puzzles and the game's state machines: who is acting in a graph, signals between games, a puzzle part held by
    // the player mid-animation, and things dragged together.
    internal sealed partial class Session
    {
        private readonly Dictionary<int, string> dragging = new Dictionary<int, string>();
        private readonly Dictionary<int, float> dragDistance = new Dictionary<int, float>();
        private int enemyContext;
        // Puzzle state machines run on every peer, often over several seconds after the interaction that
        // started them. Remember which player set each one going: steps acting on "the player" (freezing
        // them, a camera close-up, taking or giving the held item, screen effects) are for that player
        // only; every other peer just sees the world change.
        private readonly Dictionary<NodeCanvas.Framework.Graph, int> graphActors = new Dictionary<NodeCanvas.Framework.Graph, int>();

        // A puzzle machine that has frozen a player for its animation (pouring the alcohol on the rope, a nail, a vent screw,
        // the sink) runs to its end for that player. Anyone else using the same puzzle meanwhile is turned away, as the game's
        // own busy flag turns a lone player away: otherwise the machine carries on as theirs, and the first player is left
        // frozen in the animation for good (their "let go" step went to the other player).
        private sealed class Busy { public int Actor; public float Since; }
        internal static bool TestNoBusyLock; // Tests: let a second player in, to try the safety net.
        private readonly Dictionary<NodeCanvas.Framework.Graph, Busy> busyGraphs = new Dictionary<NodeCanvas.Framework.Graph, Busy>();
        private NodeCanvas.Framework.Graph mySequence; // The machine that froze this player, while it does.
        private PlayerStates.States mySequenceState;
        private float mySequenceLost = -1f;
        private const float BusyLimit = 60f;  // No machine holds a puzzle longer than this (a step lost, a player gone).
        private const float ClaimSeconds = 1f; // A puzzle part just used: the moment before its machine freezes the player.
        private readonly Dictionary<Transform, Busy> claims = new Dictionary<Transform, Busy>();

        /// <summary>A puzzle machine, not the game's own death and life-lost scenes, an ending everyone watches or the Nun.</summary>
        internal bool PuzzleMachine(NodeCanvas.Framework.Graph graph)
        {
            var agent = graph == null ? null : graph.agent as Component;
            return Active && agent && !enemy.Owns(graph) && !sharedEndings.Contains(graph)
                && !GraphDump.Path(agent.transform).StartsWith("Global/", StringComparison.Ordinal);
        }

        /// <summary>A puzzle machine's step sets "the player's" state: whom the machine holds, until it lets them go.</summary>
        internal void PuzzlePlayerStep(NodeCanvas.Framework.Graph graph, PlayerStates.States state)
        {
            if (!PuzzleMachine(graph)) return;
            int actor = EffectiveActor;
            bool frozen = state == PlayerStates.States.ItemCutscene || state == PlayerStates.States.Cutscene || state == PlayerStates.States.MousePuzzle;
            if (!frozen || actor <= 0)
            {
                // Let go: whatever held that player is done (a sequence can be let go of by another part of the puzzle).
                busyGraphs.Remove(graph);
                if (actor > 0) foreach (var held in busyGraphs.Where(x => x.Value.Actor == actor).Select(x => x.Key).ToList()) busyGraphs.Remove(held);
                return;
            }
            busyGraphs[graph] = new Busy { Actor = actor, Since = Time.time };
            if (actor == LocalId) { mySequence = graph; mySequenceState = state; mySequenceLost = -1f; }
        }

        /// <summary>This player is frozen by a puzzle's close-up or animation.</summary>
        internal bool FrozenByPuzzle { get { return mySequence != null; } }

        /// <summary>Host: the player (other than this one) mid-animation at this puzzle part, or 0.</summary>
        private int BusyWith(Component target, int peer)
        {
            if (!target || TestNoBusyLock) return 0;
            foreach (var pair in claims)
                if (pair.Key && pair.Value.Actor != peer && Time.time - pair.Value.Since < ClaimSeconds && Related(target.transform, pair.Key))
                    return pair.Value.Actor;
            foreach (var pair in busyGraphs)
            {
                if (pair.Value.Actor == peer || Time.time - pair.Value.Since > BusyLimit || departed.Contains(pair.Value.Actor)) continue;
                var agent = pair.Key.agent as Component;
                if (agent && Related(target.transform, agent.transform)) return pair.Value.Actor;
            }
            return 0;
        }

        /// <summary>The same puzzle part: one inside the other, or side by side under one parent.</summary>
        private static bool Related(Transform a, Transform b)
        {
            if (a == b || a.IsChildOf(b) || b.IsChildOf(a)) return true;
            Transform pa = a.parent, pb = b.parent;
            return pa && pb && (pa == pb || pa == pb.parent || pa.parent == pb);
        }

        /// <summary>Every frame: this player, frozen by a puzzle machine that no longer runs their animation (another
        /// player's step took it over, or it stopped), is let go: walking and looking again, the camera back on their head.
        /// (No time limit: some close-ups wait on the player, the clock's hands, in the same state as an animation.)</summary>
        private void WatchSequence()
        {
            if (mySequence == null) return;
            var r = Local;
            if (!r || !r.playerStates) { mySequence = null; return; }
            var state = r.playerStates.StateCheck();
            if (state != mySequenceState) { mySequence = null; return; } // Let go, or something else has them now (a knockdown, an ending).
            Busy busy;
            bool mine = mySequence.isRunning && busyGraphs.TryGetValue(mySequence, out busy) && busy.Actor == LocalId;
            if (mine) { mySequenceLost = -1f; return; }
            if (mySequenceLost < 0f) { mySequenceLost = Time.time; return; }
            if (Time.time - mySequenceLost < 1f) return; // A moment's grace: the step that lets them go may be on its way.
            var agent = mySequence.agent as Component;
            plugin.Log("Co-op: let go of " + state + " (" + (agent ? GraphDump.Path(agent.transform) : "?") + ": no longer runs it for this player)");
            mySequence = null;
            Life.OutOfCloseUp();
        }

        private readonly Stack<int> graphActorStack = new Stack<int>();
        private int graphActor;
        private readonly Dictionary<string, int> placedBy = new Dictionary<string, int>(); // Item id -> player who put it in a puzzle.
        // Steps of someone else's puzzle that wait on their keys (a close-up's exit, the clock hands):
        // signals from that player, by "machine id#step index#kind", consumed once by our copy.
        private readonly HashSet<string> graphSignals = new HashSet<string>();
        private string lastSignal;
 private float lastSignalAt;
        private string pendingGraphs; // Joining mid-level: the host's puzzle machine steps, applied once the world is.

        private void CatchUpGraphs()
        {
            if (pendingGraphs == null || !Ready || IsHost || world == null) return;
            string text = pendingGraphs;
            pendingGraphs = null;
            bool prior = Replaying; int priorActor = ReplayActor;
            Replaying = true; ReplayActor = 1; // As the host's own steps: nothing aimed at this player, no requests back.
            try { int moved = world.ApplyGraphStates(text, plugin.Log); plugin.Log("Caught up " + moved + " puzzle machine(s) with the host."); }
            finally { Replaying = prior; ReplayActor = priorActor; }
        }

        private void ReleaseHeldForPuzzle(int peer, string id)
        {
            if (string.IsNullOrEmpty(id)) return;
            owners[id] = 0;
            placedBy[id] = peer;
            var item = world.Find(id) as ItemInteractable;
            if (item) { item.gameObject.SetActive(true); WorldEquipEffects(item); } // Placed: the same tape plays for everyone.
            if (peer == LocalId && Local && Local.inventory && Local.inventory.EquippedItem == item) Local.inventory.ClearEquippedItem();
        }

        /// <summary>The machine updating now belongs to a player who has left.</summary>
        internal bool ActorGone { get { return Active && EffectiveActor > 0 && EffectiveActor != LocalId && departed.Contains(EffectiveActor); } }

        /// <summary>
        /// Runs an action as if that player did it (their puzzle machine follows), e.g. ending a departed player's
        /// lockpicking.
        /// </summary>
        internal void AsPlayer(int peer, Action action)
        {
            bool prior = Replaying; int priorActor = ReplayActor;
            Replaying = true; ReplayActor = peer;
            try { action(); } finally { Replaying = prior; ReplayActor = priorActor; }
        }

        /// <summary>Host: that player stops holding whatever door or prop they were dragging, for everyone.</summary>
        private void LetGo(int peer)
        {
            string held;
            if (!IsHost || !dragging.TryGetValue(peer, out held)) return;
            if (StillHeld(held, peer)) { dragging.Remove(peer); return; } // The others keep hold of it.
            var release = new Packet { type = "commit", action = "release", target = held, peer = peer, epoch = epoch };
            ApplyAction(release); Broadcast(release);
        }

        /// <summary>Automated tests: who holds what, and whether each held thing can still move.</summary>
        internal string Holders()
        {
            return string.Join(", ",
                dragging.Select(d => d.Key + "->" + d.Value.Substring(Math.Max(0, d.Value.Length - 50)) + " canMove=" + ((world.Find(d.Value) as MoveInteractable) ? ((MoveInteractable)world.Find(d.Value)).CanMove.ToString() : "?")).ToArray());
        }

        /// <summary>Host: someone besides this player is holding that door or prop.</summary>
        private bool StillHeld(string target, int peer) { return dragging.Any(d => d.Key != peer && d.Value == target); }

        internal bool StateChange(StateInteractable state, int index)
        {
            // Also when replayed: whoever lies in the coffin.
            if (Active && Ready && index >= 0) Reserve(state, index, EffectiveActor > 0 ? EffectiveActor : LocalId);
            if (!Active || !Ready || Replaying || index < 0 || PersonalScene) return true;
            if (IsHost)
            {
                int actor = EffectiveActor > 0 ? EffectiveActor : LocalId; // A state change inside someone's puzzle machine is still theirs.
                PlayerFrame frame;
                string held = actor == LocalId ? (CapturePlayer() ?? new PlayerFrame()).equipped : players.TryGetValue(actor, out frame) ? frame.equipped : "";
                Broadcast(new Packet { type = "commit", action = "state", target = world.IdFor(state),
                    text = index.ToString(), epoch = epoch, peer = actor, item = held ?? "" });
                return true; // Autonomous host puzzle/trigger transitions do not require a nearby player.
            }
            // Someone else's puzzle machine: theirs (or the host's copy) moves it on; ours follows the commit.
            if (!RemoteGraphStep) Request(new Packet { action = "state", target = world.IdFor(state), text = index.ToString() });
            return false;
        }

        private void UpdateDrag()
        {
            foreach (var entry in dragging.ToArray())
            {
                PlayerFrame actor;
                var move = world.Find(entry.Value) as MoveInteractable;
                if (!move || !move.CanMove || !players.TryGetValue(entry.Key, out actor) || actor.scene != SceneName
                    || Vector3.Distance(actor.position, move.transform.position) > 5f)
                { dragging.Remove(entry.Key); continue; }
                var body = move.GetComponent<Rigidbody>();
                if (!body || body.isKinematic || entry.Key == LocalId) continue;
                float distance = dragDistance[entry.Key];
                var target = actor.position + Vector3.up * Mathf.Clamp(actor.height * .85f, .2f, 1.7f) + actor.look.normalized * distance;
                body.AddForceAtPosition(Vector3.ClampMagnitude((target - body.worldCenterOfMass) * 35f - body.velocity * 5f, 100), body.worldCenterOfMass);
            }
        }

        /// <summary>The player whose action is running right now: the machine's starter, or the action being replayed.</summary>
        internal int EffectiveActor { get { return graphActor != 0 ? graphActor : (Replaying ? ReplayActor : 0); } }
        /// <summary>A puzzle step about to act on "the player" belongs to someone else's action.</summary>
        internal bool RemoteGraphStep { get { return Active && Ready && EffectiveActor > 0 && EffectiveActor != LocalId; } }

        /// <summary>The machine updating right now was set going by this player's own action.</summary>
        internal bool LocalGraphOwned { get { return Active && Ready && graphActor == LocalId; } }

        private string GraphKey(NodeCanvas.Framework.Task task, out NodeCanvas.Framework.Graph graph, out int index)
        {
            graph = task == null ? null : task.ownerSystem as NodeCanvas.Framework.Graph;
            index = graph == null ? -1 : graph.GetAllTasksOfType<NodeCanvas.Framework.Task>().IndexOf(task);
            if (graph == null || index < 0 || world == null || !graph.agent) return null;
            var owner = graph.agent.GetComponent<NodeCanvas.Framework.GraphOwner>();
            return owner ? world.IdFor(owner) : null;
        }

        /// <summary>This player's key press (or minigame result) moved their own puzzle on: the same step
        /// happens for everyone.</summary>
        internal void SignalGraph(NodeCanvas.Framework.Task task, string kind)
        {
            NodeCanvas.Framework.Graph graph; int index;
            string id = GraphKey(task, out graph, out index);
            if (id == null) return;
            string text = index + "#" + kind;
            if (text == lastSignal && Time.unscaledTime - lastSignalAt < 0.5f) return; // A held key is one press.
            lastSignal = text; lastSignalAt = Time.unscaledTime;
            Request(new Packet { action = "graphsync", target = id, text = text });
        }

        /// <summary>A minigame played on this screen (lockpicking) ended a certain way, or the host found the
        /// padlock's combination: every peer's puzzle machine follows.</summary>
        internal void MinigameResult(Component game, string result)
        {
            if (!Active || !Ready || world == null || !game) return;
            string id = world.IdFor(game);
            if (!string.IsNullOrEmpty(id)) Request(new Packet { action = "minigame", target = id, text = result });
        }

        /// <summary>Someone else's puzzle step that waits on their input: true once their signal arrived.</summary>
        internal bool TakeGraphSignal(NodeCanvas.Framework.Task task, string kind)
        {
            NodeCanvas.Framework.Graph graph; int index;
            string id = GraphKey(task, out graph, out index);
            return id != null && graphSignals.Remove(id + "#" + index + "#" + kind);
        }

        /// <summary>Graph.SendEvent: the machine now runs for whoever caused the event.</summary>
        /// <summary>A shared ending's machine already on its way through (the text scrolling, the fade): its event again
        /// (the same puzzle finishing here a moment after the relay started it) would only start it over.</summary>
        private static bool Showing(NodeCanvas.Framework.Graph graph)
        {
            var fsm = graph as NodeCanvas.StateMachines.FSM;
            string state = fsm != null ? fsm.currentStateName : null;
            return state == "Scroll" || state == "Finish" || state == "Outro Finish" || state == "Outro Set";
        }

        /// <summary>Graph.SendEvent: false for a repeat of a shared ending's event while it is showing.</summary>
        internal bool GraphEventAllowed(NodeCanvas.Framework.Graph graph, string eventName)
        {
            if (Active && graph != null && eventName != null && sharedEndings.Contains(graph))
            {
                var owner = graph.agent as Component;
                if (owner && eventName == EndingEvent(owner.transform) && Showing(graph)) return false;
            }
            return GraphEvent(graph, eventName);
        }

        /// <summary>
        /// A machine starts, or gets an event. Puzzle machines remember who set them going. A team ending is held back
        /// until it is the one the team plays: the host's own starts at once (unless another is already playing), a
        /// guest's waits for the host to agree and then starts on every game together. False holds it back.
        /// </summary>
        internal bool GraphEvent(NodeCanvas.Framework.Graph graph, string eventName = null)
        {
            if (!Active || graph == null || enemy.Owns(graph)) return true;
            if (Ready && !sharedEndings.Contains(graph) && IsTeamEnding(graph, eventName) && !ClaimEnding(graph)) return false;
            if (sharedEndings.Contains(graph)) { graphActors.Remove(graph); return true; } // Everyone's own: every step runs on every game.
            int actor = EffectiveActor;
            if (actor > 0) graphActors[graph] = actor; else graphActors.Remove(graph);
            return true;
        }

        internal int EnterGraph(NodeCanvas.Framework.Graph graph)
        {
            graphActorStack.Push(graphActor);
            int actor; graphActor = graph != null && graphActors.TryGetValue(graph, out actor) ? actor : 0;
            int previous = enemyContext;
            if (Active && IsHost && Ready && enemy.Owns(graph)) enemyContext = enemy.TargetId;
            return previous;
        }

        internal void ExitGraph(int previous) { enemyContext = previous; graphActor = graphActorStack.Count > 0 ? graphActorStack.Pop() : 0; }

        /// <summary>PlaceItemTask: the held item goes into a puzzle. Rather than the game's "clear the local
        /// player's held item" (which would read as the item being used up, and would take whatever the
        /// local player holds), release it from the player who used it.</summary>
        internal Action PuzzlePlacement(ItemInteractable item)
        {
            if (!Active || !Ready || !item || world == null) return null;
            string id = world.IdFor(item);
            if (string.IsNullOrEmpty(id)) return null;
            int actor = EffectiveActor > 0 ? EffectiveActor : LocalId;
            return () =>
            {
                bool prior = Replaying; Replaying = true;
                try { ReleaseHeldForPuzzle(actor, id); }
                finally { Replaying = prior; }
            };
        }

        /// <summary>PickupItem on the machine of the player who used the item: the puzzle hands it back.
        /// Asked of the host by name rather than as a ranged pickup (the host's copy may not be in reach).</summary>
        internal void Reclaim(ItemInteractable item)
        {
            if (!item || world == null) return;
            Request(new Packet { action = "reclaim", target = world.IdFor(item) });
        }
    }
}
