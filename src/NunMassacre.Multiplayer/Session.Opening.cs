using System;
using System.Collections.Generic;
using System.Linq;
using GrannyClone;
using UnityEngine;

namespace NunMassacre.Multiplayer
{
    // The opening (the bathtub, the letter, the walk to the school) and the scares that wait for everyone.
    internal sealed partial class Session
    {
        /// <summary>The opening (waking in the bathtub, the letter, the walk to the school) is played by each
        /// player at their own pace: nothing in it is shared but the players themselves.</summary>
        internal bool PersonalScene { get { return Active && SceneName == OpeningScene; } }
        /// <summary>A guest still in the opening while the host has gone into the school: they follow by
        /// walking in themselves, not by being pulled out of it.</summary>
        private bool FinishingOpening { get { return SceneName == OpeningScene && hostScene == GameScene; } }
        private Transform bathStart;
        // Climbing out of the tub is a clumsy scripted slide: teammates keep seeing us lying in it until we are
        // out, then we appear beside it.
        private bool leavingBath;
        private Vector3 bathSpot;
        private Quaternion bathTurn;
        // Scares happen to the group: they fire for everyone at once, and only with everyone close by.
        private const float ScareRadius = 12f;
        private readonly Dictionary<string, float> scareFiredAt = new Dictionary<string, float>(); // Host: one firing for a pair walking in together.
        private float nextScareAsk;
        // The opening, played together: each player gets out of the bath on their own; the letter takes everyone
        // outside at once (when all are out of the bath), and the group goes into the school together.
        private readonly HashSet<string> openingDone = new HashSet<string>(); // Host: beats already played.
        /// <summary>In the opening with the host there too (otherwise, e.g. the host already in the school, alone).</summary>
        private bool OpeningTogether { get { return Active && Ready && SceneName == OpeningScene && (IsHost || hostScene == OpeningScene); } }
        /// <summary>Lying in the opening scene's bathtub, not yet up.</summary>
        private bool bathAwake; // Has lain in the tub with control: a cutscene after that is getting out of it.
        private float openingArrived;

        internal bool InBath()
        {
            if (!bathStart || !Local || !Local.firstPersonController || !Local.playerStates) return false;
            Vector3 offset = ((Component)Local).transform.position - bathStart.position; offset.y = 0f;
            if (offset.sqrMagnitude >= 0.36f) return false;
            var state = Local.playerStates.StateCheck();
            if (state == PlayerStates.States.MouseLook) { bathAwake = true; return true; }
            // Still waking up in it (the intro's cutscene): in the bath too, so the letter waits for them.
            return state == PlayerStates.States.Cutscene && !bathAwake;
        }

        // The Carnival, Backrooms and PDM scenarios open with their story scrolling up the screen while the player is held
        // in place (46 s). A guest still in it once the host is through (a game joined in progress, or a level that
        // finished loading a little later) skips the rest, as the game's own menu button does, rather than standing frozen
        // while the Nun is about.
        private float nextIntroCheck;

        private void CatchUpIntro(PlayerFrame host)
        {
            if (!Local || Time.unscaledTime < nextIntroCheck) return;
            nextIntroCheck = Time.unscaledTime + 1f;
            if (host.state == (int)PlayerStates.States.Cutscene) return; // The host is still reading it too.
            foreach (var owner in UnityEngine.Object.FindObjectsOfType<NodeCanvas.StateMachines.FSMOwner>())
            {
                if (!owner || owner.name != "Intro Outro" || !owner.isRunning) continue;
                var fsm = owner.graph as NodeCanvas.StateMachines.FSM;
                var isIntro = owner.blackboard != null ? owner.blackboard.GetVariable("isIntro") : null;
                // Not the ending's text.
                if (fsm == null || fsm.currentStateName != "Scroll" || isIntro == null || !(isIntro.value is bool) || !(bool)isIntro.value) continue;
                fsm.TriggerState("Finish");
                plugin.Log("Co-op: the host is past the opening text; skipping the rest of it");
            }
        }

        /// <summary>A scare's trigger (EventTrigger or ProbabilityTrigger): the local player walking in asks the
        /// host, which fires it for everyone if the whole group is close by. False skips the local trigger.</summary>
        /// <summary>The opening's school entrance: the group goes in together. False skips the local trigger.</summary>
        internal bool OpeningGate(global::EventTrigger gate, Collider other)
        {
            if (!OpeningTogether || Replaying || !gate || gate.name != "Next Scene Trigger" || !other || !Local) return true;
            var body = ((Component)Local).transform;
            if (other.transform != body && !other.transform.IsChildOf(body)) return true;
            if (Time.realtimeSinceStartup >= nextScareAsk)
            {
                nextScareAsk = Time.realtimeSinceStartup + 1f;
                Request(new Packet { action = "opening", text = "school", target = world.IdFor(gate) });
            }
            return false;
        }

        internal bool ScareEntered(Component trigger, Collider other)
        {
            if (!Active || !Ready || Replaying || !trigger || !other || !Local || !IsScare(trigger)) return true;
            var body = ((Component)Local).transform;
            if (other.transform != body && !other.transform.IsChildOf(body)) return true; // Not us: its own tag check decides.
            if (Time.realtimeSinceStartup >= nextScareAsk)
            {
                nextScareAsk = Time.realtimeSinceStartup + 0.25f;
                Request(new Packet { action = "scare", target = world.IdFor(trigger) });
            }
            return false;
        }

        private bool IsScare(Component trigger)
        {
            if (!trigger) return false;
            string scene = trigger.gameObject.scene.name;
            if (scene == OpeningScene ? !OpeningTogether : scene != GameScene) return false;
            if (scene == OpeningScene && trigger.name == "Title trigger") return true; // The title card, together.
            for (Transform t = trigger.transform; t; t = t.parent)
                if (t.name.IndexOf("Scare", StringComparison.OrdinalIgnoreCase) >= 0 || t.name.IndexOf("Scary", StringComparison.OrdinalIgnoreCase) >= 0)
                    return true;
            return false;
        }

        private static Vector3 ScareCenter(Component trigger)
        {
            var shape = trigger.GetComponent<Collider>();
            return shape ? shape.bounds.center : trigger.transform.position;
        }

        // Host: when each of the opening's group moments first held the team back. Someone away from the keyboard must not
        // keep everyone waiting for ever: after a minute the team goes ahead without them, and everyone is told who.
        private readonly Dictionary<string, float> gateWaitSince = new Dictionary<string, float>();
        private const float GateGrace = 60f;

        private bool GateWaitedTooLong(string gate, string[] waitingFor)
        {
            float since;
            if (!gateWaitSince.TryGetValue(gate, out since)) { gateWaitSince[gate] = Time.realtimeSinceStartup; return false; }
            if (Time.realtimeSinceStartup - since < GateGrace) return false;
            string text = "Going ahead without " + string.Join(", ", waitingFor);
            Broadcast(new Packet { type = "tell", epoch = epoch, text = text });
            Tell(text);
            plugin.Log("Co-op: " + text + " (waited " + GateGrace.ToString("0") + "s at " + gate + ")");
            return true;
        }

        /// <summary>Host: names of the players still in the game (downed included, spectators not) who are not near this spot.</summary>
        private string[] AwayFrom(Vector3 at)
        {
            var self = CapturePlayer();
            return players.Values.Where(f => f.id != LocalId).Concat(self != null ? new[] { self } : new PlayerFrame[0])
                .Where(f => f.body && f.scene == SceneName && !f.spectating && (Mathf.Abs(f.position.y - at.y) > 3f || new Vector2(f.position.x - at.x, f.position.z - at.z).magnitude > ScareRadius))
                .Select(f => f.name).ToArray();
        }

        /// <summary>Host: every player still in the game (downed included, spectators not) is near this spot.</summary>
        private bool TogetherNear(Vector3 at)
        {
            var self = CapturePlayer(); // Our own spot as of now, not as of our last request.
            foreach (var frame in players.Values.Where(f => f.id != LocalId).Concat(self != null ? new[] { self } : new PlayerFrame[0]))
            {
                if (!frame.body || frame.scene != SceneName || frame.spectating) continue;
                Vector3 gap = frame.position - at;
                if (Mathf.Abs(gap.y) > 3f || new Vector2(gap.x, gap.z).magnitude > ScareRadius) return false;
            }
            return true;
        }

        internal void ZoneEntered(global::EventTrigger zone, Collider other)
        {
            if (!Active || !Ready || IsHost || Replaying || !zone || !other || !other.CompareTag(zone.tagToTrigger.ToString())) return;
            string path = GraphDump.Path(zone.transform);
            if (!WorldTriggers.Any(t => path.EndsWith(t, StringComparison.Ordinal))) return;
            Request(new Packet { action = "trigger", target = world.IdFor(zone) });
        }
    }
}
