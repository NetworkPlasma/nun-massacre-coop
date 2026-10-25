using System;
using System.Collections.Generic;
using System.Linq;
using GrannyClone;
using HarmonyLib;
using NunMassacre.Multiplayer.Gameplay;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace NunMassacre.Multiplayer
{
    // Actions: a player's request goes to the host, which checks it and sends it to every game to apply.
    internal sealed partial class Session
    {
        private readonly Dictionary<int, float> actionWindow = new Dictionary<int, float>();
        private readonly Dictionary<int, int> actionCount = new Dictionary<int, int>();
        private readonly Dictionary<int, int> lastAction = new Dictionary<int, int>();

        internal bool Interact(Interactable target, bool release)
        {
            if (!Active || Replaying || PersonalScene) return true;
            if (!Ready) return false;
            // Reading is a private camera/UI action. Hiding asks the host for a place under the bed.
            if (target is DocumentInteractable) return true;
            if (target is HideBedInteractable) { Request(new Packet { action = "hide", target = world.IdFor(target) }); return false; }
            var id = world.IdFor(target);
            if (string.IsNullOrEmpty(id)) { plugin.Log("Unregistered interaction: " + target.name); return false; }
            Request(new Packet { action = release ? "release" : (target is ItemInteractable ? "pickup" : "interact"), target = id });
            return false;
        }

        internal bool WorldCommand(Component target, string action, string argument = null)
        {
            if (!Active || Replaying || PersonalScene) return true;
            if (Ready) Request(new Packet { action = action, target = world.IdFor(target), text = argument });
            return false;
        }

        internal void Request(Packet p)
        {
            if (!Active || !Ready || Replaying) return;
            p.type = "action"; p.peer = LocalId; p.epoch = epoch; p.number = ++actionSequence;
            var frame = CapturePlayer();
            if (frame != null) { players[LocalId] = frame; p.item = frame.equipped; p.position = frame.position; p.direction = frame.look; }
            if (IsHost) ProcessAction(p); else Send(1, p);
        }

        private void ProcessAction(Packet p)
        {
            // Unlocks are each player's own save, not the level: passed on to everyone at once, even as the scene ends.
            if (IsHost && p.action == "unlocks")
            {
                p.epoch = epoch; // The host's scene now: what everyone it is sent to has (or will have) when it arrives.
                ApplyAction(p);
                p.type = "commit";
                Broadcast(p);
                return;
            }
            if (!Ready || !IsHost) return;
            PlayerFrame actor;
            if (!players.TryGetValue(p.peer, out actor) || actor.scene != SceneName) return;
            if (p.action == "revive") { ProcessRevive(p, actor); return; }
            if (p.action == "ending") { AnnounceEnding(actor.name, true); return; }
            if (p.action == "trapoff") { spawns.Deactivate(p.target); return; }
            if (p.action == "shout") { HearShout(p.peer, p.amount); return; }
            if (p.action == "reclaim")
            {
                // A puzzle hands its tool back to the player who just used it (screws, nails...).
                int placer, owner;
                if (string.IsNullOrEmpty(p.target) || !placedBy.TryGetValue(p.target, out placer) || placer != p.peer
                    || (owners.TryGetValue(p.target, out owner) && owner != 0))
                {
                    plugin.Log("Rejected reclaim from peer " + p.peer + ": " + p.target);
                    return;
                }
                p.action = "pickup";
                p.item = actor.equipped; p.position = actor.position; p.direction = actor.look;
                ApplyAction(p);
                p.type = "commit";
                Broadcast(p);
                return;
            }
            var component = string.IsNullOrEmpty(p.target) ? null : world.Find(p.target);
            var interactable = component as Interactable;
            if (p.action == "interact" || p.action == "state")
            {
                int busyPeer = BusyWith(component as Component, p.peer);
                if (busyPeer != 0)
                {
                    PlayerFrame other;
                    string who = players.TryGetValue(busyPeer, out other) && !string.IsNullOrEmpty(other.name) ? other.name : "Someone";
                    TellPeer(p.peer, who + " is using that.");
                    plugin.Log("Rejected " + p.action + " from peer " + p.peer + ": " + p.target + " is mid-animation for peer " + busyPeer);
                    return;
                }
                // An ending's part used by a player (the coffin's lid, the rocking chair): only with everyone there. Its
                // machine's own state changes (the coffin readying the lid) go ahead.
                if (p.action == "interact" && component && IsEndingPart(component.transform)
                    && !EndingTogether(GraphDump.Path(component.transform), component.transform.position, p.peer))
                {
                    plugin.Log("Rejected " + p.action + " from peer " + p.peer + ": " + p.target + " is an ending and not everyone is there");
                    return;
                }
                var part = component as StateInteractable;
                if (part) claims[part.transform] = new Busy { Actor = p.peer, Since = Time.time };
            }
            if (p.action == "pickup" || p.action == "interact" || p.action == "release" || p.action == "state" || p.action == "hide")
            {
                if (!interactable || Vector3.Distance(actor.position, interactable.CenterPt) > Mathf.Max(5f, interactable.InteractionDist + 2f))
                {
                    plugin.Log("Rejected " + p.action + " from peer " + p.peer + ": "
                        + (interactable ? "out of range (" + Vector3.Distance(actor.position, interactable.CenterPt).ToString("0.0") + "m, limit " + Mathf.Max(5f, interactable.InteractionDist + 2f).ToString("0.0") + "m, player at " + actor.position + ")" : "missing")
                        + " target " + p.target);
                    return;
                }
                if (actor.health <= 0) return;
            }
            if ((p.action == "interact" || p.action == "state") && Reserved(p.target, p.peer))
            {
                TellPeer(p.peer, "Only the player in the coffin can close it.");
                return;
            }
            // Several players can hold a door or prop together. Letting go only stops it once nobody holds it:
            // otherwise one player's release would freeze it in the others' hands.
            string holding;
            if (p.action == "release" && component is MoveInteractable)
            {
                if (!dragging.TryGetValue(p.peer, out holding) || holding != p.target) return;
                if (StillHeld(p.target, p.peer)) { dragging.Remove(p.peer); return; }
            }
            // Taken already: counts once.
            if (p.action == "interact" && component is LockpickItem && (!component.gameObject.activeInHierarchy || !collected.Add(p.target))) return;
            if (p.action == "pickup")
            {
                var item = component as ItemInteractable;
                int owner;
                if (!item || item.CantPickUp || (owners.TryGetValue(p.target, out owner) && owner != 0) || owners.Count(x => x.Value == p.peer) >= 3)
                {
                    TellPeer(p.peer, "That item is unavailable or your inventory is full.");
                    return;
                }
            }
            else if (p.action == "hide")
            {
                var bed = component as HideBedInteractable;
                if (!bed || (bool)BedFlipped.GetValue(bed) || actor.downed || actor.spectating) return;
                int slot = FreeHideSlot(bed, p.peer);
                if (slot < 0)
                {
                    if (p.peer == LocalId) NoRoom(bed); else Send(p.peer, new Packet { type = "hidefull", epoch = epoch, target = p.target });
                    return;
                }
                p.text = slot.ToString();
            }
            else if (p.action == "teamending")
            {
                var ending = EndingOwner(p.target);
                if (!ending || !IsTeamEnding(ending.transform)) return;
                if (!EndingAllowed(ending.transform, p.target))
                {
                    plugin.Log("Rejected team ending from peer " + p.peer + ": another ending is already playing.");
                    return;
                }
                if (committedEnding == null) committedEnding = p.target;
            }
            else if (p.action == "glass")
            {
                // A rock, a bullet or a falling cross broke a window on that player's screen.
                if (!(component is GrannyClone.BreakableGlass) || !component.gameObject.activeInHierarchy
                    || Vector3.Distance(actor.position, component.transform.position) > 25f)
                    return;
            }
            else if (p.action == "drop" || p.action == "consume" || p.action == "throw")
            {
                int owner;
                if (!owners.TryGetValue(p.target ?? "", out owner) || owner != p.peer) return;
            }
            else if (p.action == "state")
            {
                var state = component as StateInteractable;
                if (!state) return;
                var states = AccessTools.Field(typeof(StateInteractable), "states").GetValue(state) as StateInteractable.InteractionState[];
                int index;
                if (!int.TryParse(p.text, out index) || states == null || index < 0 || index >= states.Length) return;
                // State progression requested by the actor must correspond to the current puzzle edge.
                int current = (int)AccessTools.Field(typeof(StateInteractable), "stateIndexCurrent").GetValue(state);
                if (p.peer != LocalId && (current < 0 || current >= states.Length || states[current].NextState != index)) return;
            }
            else if (p.action == "shot")
            {
                int owner;
                if (!component || !owners.TryGetValue(actor.equipped ?? "", out owner) || owner != p.peer
                    || !(world.Find(actor.equipped) is RevolverInteractable))
                    return;
                if (Vector3.Distance(actor.position, component.transform.position) > 100f) return;
            }
            else if (p.action == "noise") { if (!Finite(p.position) || Vector3.Distance(actor.position, p.position) > 12f) return; }
            else if (p.action == "whistle") { if (!Finite(p.position) || Vector3.Distance(actor.position, p.position) > 4f) return; }
            else if (p.action == "minigame")
            {
                bool known = component is PickLock ? (p.text == "solved" || p.text == "end" || p.text == "break")
                    : component is CombinationLock && p.text == "solved" && p.peer == LocalId;
                if (!known) return;
            }
            else if (p.action == "graphsync")
            {
                if (!(component is NodeCanvas.Framework.GraphOwner) || string.IsNullOrEmpty(p.text) || p.text.Length > 64) return;
            }
            else if (p.action == "endingzone")
            {
                var zone = InLevel<global::EventTrigger>(p.text);
                if (!zone || !IsEndingZone(zone.transform) || committedEnding != null || TeamEndingRunning) return;
                if (Vector3.Distance(actor.position, ScareCenter(zone)) > 15f) return;
                if (!EndingTogether(p.text, ScareCenter(zone), p.peer)) return;
            }
            else if (p.action == "opening")
            {
                if (SceneName != OpeningScene || openingDone.Contains(p.text)) return;
                if (p.text == "letter")
                {
                    var self = CapturePlayer();
                    var bathing = players.Values.Where(f => f.id != LocalId).Concat(self != null ? new[] { self } : new PlayerFrame[0])
                        // Still loading or spawning in counts as in the bath.
                        .Where(f => f.scene != SceneName || f.bath || !f.body).Select(f => f.name).ToArray();
                    if (bathing.Length > 0 && !GateWaitedTooLong("letter", bathing))
                    {
                        TellPeer(p.peer, "Wait until everyone is out of the bath (" + string.Join(", ", bathing) + ").");
                        return;
                    }
                }
                else if (p.text == "school")
                {
                    var gate = component as global::EventTrigger;
                    if (!gate || gate.name != "Next Scene Trigger") return;
                    string[] away = AwayFrom(ScareCenter(gate));
                    if (away.Length > 0 && !GateWaitedTooLong("school", away))
                    {
                        TellPeer(p.peer, "Wait for everyone: you go into the school together (" + string.Join(", ", away) + ").");
                        return;
                    }
                }
                else return;
                openingDone.Add(p.text);
                gateWaitSince.Remove(p.text);
            }
            else if (p.action == "scare")
            {
                if (!(component is global::EventTrigger || component is ProbabilityTrigger) || !IsScare(component)) return;
                Vector3 at = ScareCenter(component);
                // It stays armed.
                if (Vector3.Distance(actor.position, at) > 8f || !TogetherNear(at))
                {
                    plugin.Log("Scare held back (not everyone is close): " + component.name);
                    return;
                }
                float last; float now = Time.realtimeSinceStartup;
                if (scareFiredAt.TryGetValue(p.target, out last) && now - last < 4f) return; // The partner stepped in with them.
                scareFiredAt[p.target] = now;
                p.text = UnityEngine.Random.Range(1, int.MaxValue).ToString(); // The same roll of its dice on every peer.
            }
            else if (p.action == "trigger")
            {
                var zone = component as global::EventTrigger;
                if (!zone || !WorldTriggers.Any(t => GraphDump.Path(zone.transform).EndsWith(t, StringComparison.Ordinal))
                    || Vector3.Distance(actor.position, zone.transform.position) > 15f)
                    return;
            }
            else if (p.action == "lockturn" || p.action == "bulb")
            {
                if (!component || Vector3.Distance(actor.position, component.transform.position) > 6f) return;
                if (p.action == "lockturn" && !(component is CombinationLockCylinder)) return;
                if (p.action == "bulb" && (!(component is BulbPuzzle) || (p.text != "blue" && p.text != "yellow" && p.text != "pink" && p.text != "red")))
                    return;
            }
            else if (p.action != "interact" && p.action != "release") return;
            p.item = HeldItem(p, actor);
            p.position = actor.position;
            p.direction = actor.look;
            ApplyAction(p);
            p.type = "commit";
            Broadcast(p);
        }

        private void ApplyAction(Packet p)
        {
            if (p.action == "unlocks") { ApplyUnlocks(p); return; }
            if (p.epoch != epoch) return;
            // These originate after the local game's damage/noise event has already fired.
            if (p.peer == LocalId && (p.action == "shot" || p.action == "noise")) return;
            var component = string.IsNullOrEmpty(p.target) ? null : world.Find(p.target);
            if (component && p.peer != LocalId
                && (p.action == "pickup" || p.action == "interact" || p.action == "state" || p.action == "consume" || p.action == "bulb"))
                // Their hand goes to it:
                avatars.Reach(p.peer, component.GetComponent<Collider>() ? component.GetComponent<Collider>().bounds.center : component.transform.position,
                    // pulling a door or drawer open.
                    p.action != "pickup" && (component.GetComponentInParent<GrannyClone.Door>() || component.GetComponentInParent<GrannyClone.CabinetsInteractable>()));
            if (p.peer != LocalId && p.action == "throw") avatars.Throw(p.peer); // Their arm throws it.
            bool prior = Replaying; int priorActor = ReplayActor;
            Replaying = true; ReplayActor = p.peer;
            var holding = MessagesGranny.ItemPlayerIsHoldingCallbackEvent;
            var clearing = MessagesGranny.ClearHoldingItemPlayerEvent;
            try
            {
                var held = string.IsNullOrEmpty(p.item) ? null : world.Find(p.item) as ItemInteractable;
                MessagesGranny.ItemPlayerIsHoldingCallbackEvent = callback => callback(held);
                MessagesGranny.ClearHoldingItemPlayerEvent = () => ReleaseHeldForPuzzle(p.peer, p.item);
                switch (p.action)
                {
                    case "pickup":
                        SetOwner(p.target, p.peer);
                        plugin.Log("Item claimed by peer " + p.peer + ": " + p.target);
                        break;
                    case "drop":
                    case "throw":
                        var dropped = component as ItemInteractable;
                        if (!dropped) break;
                        if (p.peer == LocalId && Local && Local.inventory && Local.inventory.EquippedItem == dropped) Local.inventory.ClearEquippedItem();
                        owners[p.target] = 0;
                        // The game's "fell through the floor" rescue puts an item back at its holder's feet: only the
                        // player who let go of it is its holder.
                        dropped.player = p.peer == LocalId && Local ? Local.inventory : null;
                        dropped.transform.SetParent(null);
                        dropped.gameObject.SetActive(true);
                        var rock = p.action == "throw" ? dropped as ThrowableItem : null;
                        dropped.transform.position = p.position
                            + (rock ? Vector3.up * 1.5f + p.direction.normalized * 0.5f : Vector3.up * 1.2f + p.direction.normalized * 0.6f);
                        dropped.ResetToPickupable();
                        if (rock)
                        {
                            // As ThrowableItem.Use: a hard throw, and a landing loud enough to draw the Nun.
                            var thrown = Traverse.Create(rock);
                            thrown.Field("throwForce").SetValue(thrown.Field("throwForceNow").GetValue<float>());
                            var impact = rock.GetComponent<PhysicsAudioImpact>();
                            if (impact)
                            {
                                impact.AlertRange = thrown.Field("alertRange").GetValue<float>();
                                if (impact.audioSourceRandom != null) impact.audioSourceRandom.Volume = new MinMaxPair(1f, 1f);
                            }
                        }
                        dropped.Throw(p.direction.normalized);
                        break;
                    case "hide":
                        var bedToHide = component as HideBedInteractable;
                        int place;
                        if (!bedToHide || !int.TryParse(p.text, out place)) break;
                        hideClaims[p.peer] = new HideClaim { Bed = world.IdFor(bedToHide.transform), Slot = place, At = Time.realtimeSinceStartup };
                        if (p.peer != LocalId || !Local || !Local.playerStates || Local.playerStates.StateCheck() != PlayerStates.States.Normal) break;
                        pendingHideBed = bedToHide; pendingHideSlot = place;
                        try { bedToHide.Interact(); }
                        finally { pendingHideBed = null; pendingHideSlot = -1; }
                        break;
                    case "glass":
                        var glass = component as GrannyClone.BreakableGlass;
                        if (glass && glass.gameObject.activeInHierarchy) glass.BreakGlass();
                        break;
                    case "consume":
                        owners[p.target] = -1;
                        if (component && p.peer != LocalId) component.gameObject.SetActive(false);
                        break;
                    case "interact":
                        var puzzlePart = component as StateInteractable;
                        if (puzzlePart)
                        {
                            // Accepted by the host already: this peer's own inventory being busy (switching items)
                            // must not make its copy refuse what the others do.
                            var inventory = Traverse.Create(puzzlePart).Field("invRef");
                            object own = inventory.GetValue();
                            inventory.SetValue(null);
                            try { puzzlePart.Interact(); }
                            finally { inventory.SetValue(own); }
                        }
                        else if (component is Interactable) ((Interactable)component).Interact();
                        if (component is MoveInteractable && IsHost)
                        {
                            dragging[p.peer] = p.target;
                            dragDistance[p.peer] = Mathf.Clamp(Vector3.Distance(p.position + Vector3.up * 1.4f, component.transform.position), 0.5f, 3f);
                        }
                        break;
                    case "release":
                        if (component is Interactable) ((Interactable)component).Release();
                        dragging.Remove(p.peer);
                        break;
                    case "state":
                        int index;
                        if (component is StateInteractable && int.TryParse(p.text, out index))
                        {
                            ((StateInteractable)component).StateChange(index);
                            Reserve((StateInteractable)component, index, p.peer);
                        }
                        break;
                    case "lockturn":
                        if (component is CombinationLockCylinder) ((CombinationLockCylinder)component).SelectLock();
                        break;
                    case "bulb":
                        if (component is BulbPuzzle) ((BulbPuzzle)component).Switch(p.text);
                        break;
                    case "shot":
                        var collider = component as Collider;
                        if (!collider && component) collider = component.GetComponent<Collider>();
                        if (collider && MessagesGranny.GunDamageEvent != null) MessagesGranny.GunDamageEvent(collider, 100, collider.bounds.center);
                        break;
                    case "whistle":
                        Gameplay.NunEars.NextKind = Gameplay.SoundKind.Whistle; Gameplay.NunEars.NextSource = p.peer;
                        try { if (Message.Detectable.NoiseMadeEvent != null) Message.Detectable.NoiseMadeEvent(p.position, Gestures.WhistleRadius); }
                        finally { Gameplay.NunEars.NextKind = Gameplay.SoundKind.Noise; Gameplay.NunEars.NextSource = 0; }
                        if (IsHost) plugin.Log("Whistle from " + p.peer + " at " + p.position.ToString("0.0") + ": the Nun can hear it");
                        break;
                    case "noise":
                        if (Message.Detectable.NoiseMadeEvent != null) Message.Detectable.NoiseMadeEvent(p.position, p.amount > 0.1f ? Mathf.Min(p.amount, 40f) : 20f);
                        break;
                    case "teamending":
                        StartSharedEnding(EndingOwner(p.target));
                        break;
                    case "endingzone":
                        if (p.peer == LocalId) FireEndingZone(p.text); // Their own way into it; the ending itself is shared.
                        break;
                    case "graphsync":
                        if (p.peer != LocalId) graphSignals.Add(p.target + "#" + p.text);
                        break;
                    case "minigame":
                        var pick = component as PickLock;
                        if (pick && p.peer != LocalId)
                        {
                            // The lockpicking ran on that player's screen; the keyhole machine follows its outcome here.
                            if (p.text == "solved") pick.solvedEvent.Invoke();
                            else if (p.text == "end") { GameHooks.PickFinished(pick); pick.endEvent.Invoke(); }
                            else if (p.text == "break") LockpickItem.lockpickCount = Mathf.Max(0, LockpickItem.lockpickCount - 1);
                        }
                        var padlock = component as CombinationLock;
                        if (padlock)
                        {
                            var solvedEvent = Traverse.Create(padlock).Field("solvedEvent").GetValue<UnityEngine.Events.UnityEvent>();
                            if (solvedEvent != null) solvedEvent.Invoke();
                        }
                        break;
                    case "trigger":
                        var zone = component as global::EventTrigger;
                        if (zone && zone._on_enter != null) zone._on_enter.Invoke();
                        break;
                    case "opening":
                        if (SceneName != OpeningScene || !Local) break;
                        int opener = ReplayActor; ReplayActor = LocalId; // Each player's own fade, warp and camera.
                        try
                        {
                            if (p.text == "letter")
                            {
                                var opening = SceneManager.GetActiveScene().GetRootGameObjects().FirstOrDefault(g => g.name == "Opening Scene new");
                                var machine = opening ? opening.GetComponent<NodeCanvas.Framework.GraphOwner>() : null;
                                if (machine) machine.SendEvent("State3");
                                if (p.peer != LocalId) Tell(CleanName(players.ContainsKey(p.peer) ? players[p.peer].name : "A teammate") + " read the letter");
                            }
                            else if (p.text == "school" && component is global::EventTrigger)
                                ((global::EventTrigger)component).OnTriggerEnter(((Component)Local).GetComponent<Collider>());
                        }
                        finally { ReplayActor = opener; }
                        break;
                    case "scare":
                        int seed;
                        var me = Local ? ((Component)Local).GetComponent<Collider>() : null;
                        if (!me || !int.TryParse(p.text, out seed)) break;
                        plugin.Log("Scare: " + GraphDump.Path(component.transform)
                            + (component is ProbabilityTrigger ? " (chance " + Traverse.Create(component).Field("Prob").GetValue() + ", delay " + ((ProbabilityTrigger)component).Delay + "s)" : ""));
                        // As if this player walked in themselves: the full scare, camera effects included, for everyone.
                        int scaredBy = ReplayActor; ReplayActor = LocalId;
                        UnityEngine.Random.InitState(seed);
                        try
                        {
                            if (component is global::EventTrigger) ((global::EventTrigger)component).OnTriggerEnter(me);
                            else if (component is ProbabilityTrigger) ((ProbabilityTrigger)component).OnTriggerEnter(me);
                        }
                        finally { ReplayActor = scaredBy; }
                        break;
                }
            }
            catch (Exception ex) { plugin.Log("Action " + p.action + " failed: " + ex); }
            finally
            {
                MessagesGranny.ItemPlayerIsHoldingCallbackEvent = holding;
                MessagesGranny.ClearHoldingItemPlayerEvent = clearing;
                Replaying = prior; ReplayActor = priorActor;
            }
        }

        /// <summary>The tool a request is made with: the one the player held when asking (the host's copy of
        /// their frame may still show the previous one), if it is theirs; otherwise nothing.</summary>
        private string HeldItem(Packet p, PlayerFrame actor)
        {
            int owner;
            if (string.IsNullOrEmpty(p.item)) return "";
            return owners.TryGetValue(p.item, out owner) && owner == p.peer ? p.item : "";
        }

        private bool AllowActionRate(int peer, float now)
        {
            float start;
            if (!actionWindow.TryGetValue(peer, out start) || now - start > 1) { actionWindow[peer] = now; actionCount[peer] = 0; }
            return ++actionCount[peer] <= 60;
        }
    }
}
