using System;
using System.Collections.Generic;
using System.Linq;
using GrannyClone;
using NunMassacre.Multiplayer.Gameplay;
using UnityEngine;

namespace NunMassacre.Multiplayer
{
    // Knocked down, revived, bleeding out, and the safe footing a player falls back to.
    internal sealed partial class Session
    {
        private readonly Dictionary<int, Vector3> safeSpots = new Dictionary<int, Vector3>(); // Host: last spot on walkable floor.
        private readonly Dictionary<int, float> nextSafeSample = new Dictionary<int, float>();

        private void RememberSafeSpot(PlayerFrame frame)
        {
            float due; float now = Time.realtimeSinceStartup;
            if (nextSafeSample.TryGetValue(frame.id, out due) && now < due) return;
            nextSafeSample[frame.id] = now + 0.5f;
            if (frame.vent || frame.downed || frame.spectating || frame.state == (int)PlayerStates.States.Hiding || !Finite(frame.position)) return;
            UnityEngine.AI.NavMeshHit hit;
            if (!UnityEngine.AI.NavMesh.SamplePosition(frame.position, out hit, 0.5f, UnityEngine.AI.NavMesh.AllAreas)) return;
            // Walkable but only by setting off an ending (the barbed wire pit, the acid vent): not safe either.
            foreach (var zone in Physics.OverlapSphere(hit.position + Vector3.up * 0.9f, 0.5f, ~0, QueryTriggerInteraction.Collide))
                if (zone && zone.isTrigger && zone.GetComponent<global::EventTrigger>() && PersonalSequence(zone.transform)
                    && !zone.name.StartsWith("Scar", StringComparison.OrdinalIgnoreCase)
                    && zone.name.IndexOf("Scare", StringComparison.OrdinalIgnoreCase) < 0)
                    return;
            safeSpots[frame.id] = hit.position;
        }

        /// <summary>Other players still in this level and not out: a fatal personal ending leaves them playing.</summary>
        internal bool OthersStillPlaying()
        {
            return players.Values.Any(p => p.id != LocalId && p.body && p.scene == SceneName && !p.spectating);
        }

        private void Shot(Collider target, float amount, Vector3 point)
        {
            if (Active && Ready && !Replaying && target) Request(new Packet { action = "shot", target = world.IdFor(target) });
        }

        private void DamageRemote(Transform hit, Vector3 point, Vector3 normal, float amount)
        {
            if (!Active || !IsHost || !Ready || !hit) return;
            int peer = avatars.GetPeer(hit);
            if (peer > 1) Send(peer, new Packet { type = "damage", epoch = epoch, amount = Mathf.Clamp(amount, 0, 1000) });
        }

        internal int ReviverOf(int peer)
        {
            foreach (var frame in players.Values) if (frame.id != peer && frame.reviving == peer && !frame.downed) return frame.id;
            return 0;
        }

        internal float ReviveProgressFor(int peer)
        {
            float best = 0f;
            foreach (var frame in players.Values) if (frame.id != peer && frame.reviving == peer && !frame.downed) best = Mathf.Max(best, frame.revive);
            return best;
        }

        internal void BleedOut(int peer)
        {
            if (peer == LocalId) Life.Die("everyone is down");
            else Send(peer, new Packet { type = "bleedout", epoch = epoch });
        }

        // Host: when each guest started reviving whom (from their frames), and when each player was last revived.
        private readonly Dictionary<int, KeyValuePair<int, float>> reviveStarted = new Dictionary<int, KeyValuePair<int, float>>();
        private readonly Dictionary<int, float> revivedAt = new Dictionary<int, float>();

        /// <summary>Host: a teammate finished a revive. Validated against the latest frames.</summary>
        private void ProcessRevive(Packet p, PlayerFrame actor)
        {
            PlayerFrame target; int targetId;
            if (!int.TryParse(p.target, out targetId)) return; // "number" carries the action sequence.
            if (actor.downed || actor.spectating || !players.TryGetValue(targetId, out target) || !target.downed
                || target.bleed <= 0f || target.scene != SceneName)
                return;
            float last;
            if (revivedAt.TryGetValue(targetId, out last) && Time.time - last < 3f)
            {
                plugin.Log("Revive from peer " + p.peer + " ignored: peer " + targetId + " was just revived.");
                return;
            }
            if (Vector3.Distance(actor.position, target.position) > CoopLife.ReviveRange + 2f)
            {
                plugin.Log("Rejected revive from peer " + p.peer + ": too far.");
                return;
            }
            if (CoopLife.Blocked(actor.position + Vector3.up * 1.2f, target.position + Vector3.up * 0.6f))
            {
                plugin.Log("Rejected revive from peer " + p.peer + ": a wall or door in between.");
                return;
            }
            // A first aid kit only counts if the reviver really holds one of their own.
            int kitOwner;
            bool kit = p.text == "kit" && !string.IsNullOrEmpty(p.item) && world.Find(p.item) is FirstAidInteractable
                && owners.TryGetValue(p.item, out kitOwner) && kitOwner == p.peer;
            if (p.text == "kit" && !kit) plugin.Log("Revive from peer " + p.peer + ": no first aid kit of theirs in hand, an ordinary revive.");
            // A guest must have been seen reviving them for (most of) the time it takes.
            KeyValuePair<int, float> started;
            float needed = (kit ? CoopLife.KitReviveSeconds : CoopLife.ReviveSeconds) * 0.75f;
            if (p.peer != LocalId && (!reviveStarted.TryGetValue(p.peer, out started) || started.Key != targetId || Time.time - started.Value < needed))
            {
                plugin.Log("Rejected revive from peer " + p.peer + ": not held long enough.");
                return;
            }
            float health = kit ? CoopLife.KitRevivedHealth : CoopLife.RevivedHealth;
            string kitId = kit ? p.item : "";
            revivedAt[targetId] = Time.time;
            if (target.id == LocalId)
            {
                if (Life.Revived(health, actor.name) && kitId.Length > 0)
                {
                    if (p.peer == LocalId) Life.KitUsed(kitId);
                    else Send(p.peer, new Packet { type = "reviveok", epoch = epoch, item = kitId });
                }
            }
            else Send(target.id, new Packet { type = "revived", epoch = epoch, amount = health, text = actor.name, number = p.peer, item = kitId });
        }

        /// <summary>Records a player's latest frame and announces knockdowns, revives and deaths.</summary>
        private void Track(PlayerFrame frame)
        {
            PlayerFrame previous;
            players.TryGetValue(frame.id, out previous);
            players[frame.id] = frame;
            departed.Remove(frame.id);
            if (IsHost && frame.id != LocalId)
            {
                KeyValuePair<int, float> started;
                if (frame.reviving == 0) reviveStarted.Remove(frame.id);
                else if (!reviveStarted.TryGetValue(frame.id, out started) || started.Key != frame.reviving)
                    reviveStarted[frame.id] = new KeyValuePair<int, float>(frame.reviving, Time.time);
            }
            if (IsHost) RememberSafeSpot(frame);
            if (IsHost && (frame.downed || frame.spectating)) LetGo(frame.id); // Knocked down while holding a door or prop.
            if (previous == null || !frame.body || previous.scene != frame.scene) return;
            bool me = frame.id == LocalId;
            if (!me && !previous.downed && !previous.spectating && frame.health < previous.health - 0.5f) avatars.Hurt(frame.id); // Hit: they bleed and flinch.
            if (frame.downed && !previous.downed && frame.bleed > 0f)
            {
                if (!me) Tell(frame.name + " is down - revive them!");
                if (IsHost) enemy.KnockedDown(frame.id, frame.position, 6f); // A short window to regroup instead of an instant second knockdown.
            }
            else if (previous.downed && previous.bleed > 0f && frame.downed && frame.bleed <= 0f) { if (!me) Tell(frame.name + " bled out"); }
            else if (previous.downed && !frame.downed && !frame.spectating && previous.bleed > 0f) { if (!me) Tell(frame.name + " is back up"); }
            if (frame.spectating && !previous.spectating && !me) Tell(frame.name + " is out - spectating");
        }
    }
}
