using System;
using System.Collections.Generic;
using GrannyClone;
using NunMassacre.Multiplayer.Players;
using UnityEngine;

namespace NunMassacre.Multiplayer
{
    // The Nun on the host: what she hears (voices, shouts, footsteps, whistles, gunshots) and who she is after.
    internal sealed partial class Session
    {
        internal Gameplay.NunEars Ears { get; private set; }
        /// <summary>Host: where the Nun's ears are (her head), if she is in the level.</summary>
        internal Vector3? NunEar { get { var at = enemy.NunPosition; return at.HasValue ? at.Value + Vector3.up * 1.6f : (Vector3?)null; } }
        /// <summary>Host: a sound reached the Nun; what she does about it is the director's.</summary>
        internal void NunHeard(Gameplay.HeardSound sound) { if (IsHost && Ready) enemy.Heard(sound); }
        internal void SearchListMade(Humanoid.Agent.EnemyHunter.Tasks.CreateSearchPtList task) { if (Active && IsHost && Ready) enemy.SearchListMade(task); }
        internal void TestForgetSounds() { if (IsHost) enemy.ForgetSounds(); }

        private float nextNoise;

        private readonly Players.NunPresence nunAudio = new Players.NunPresence();

        /// <summary>The player the Nun is after (her target on the host; the host's word for it on a guest).</summary>
        internal int HuntedId
        {
            get
            {
                if (IsHost) return enemy.TargetId;
                PlayerFrame host;
                return players.TryGetValue(1, out host) ? host.hunting : 0;
            }
        }

        internal float NunAudioGain { get { return nunAudio.Gain; } }

        internal string NunBodyInfo
        {
            get
            {
                PlayerFrame host;
                return "host frame nunBody=" + (players.TryGetValue(1, out host) ? host.nunBody.ToString("0.00") : "-")
                    + " mine=" + (IsHost ? enemy.BodyShape.ToString("0.00") : "-") + " " + nunAudio.BodyInfo;
            }
        }

        // Her jump scare jolts the screen of the one she spotted (the VHS damage effect, motion blur, a controller rumble)
        // and losing them calms it. In the game that is always the local player; here it is whoever she is after, and it
        // moves with her if she turns on someone else mid-chase.
        private int huntFxPeer;
        internal int HuntFxPeer { get { return huntFxPeer; } }
        internal bool? OnHuntedScreen(Humanoid.Agent.EnemyHunter.Tasks.IsOnCamera task)
        {
            return Active && IsHost && Ready ? enemy.OnHuntedScreen(task) : null;
        }

        /// <summary>Host: one of her chase-effect steps (true: done here, the game's own step is skipped).</summary>
        internal bool HuntEffect(NodeCanvas.Framework.ActionTask task)
        {
            if (!Active || !IsHost || !Ready || !enemy.Owns(task.ownerSystem as NodeCanvas.Framework.Graph)) return false;
            string info = task.summaryInfo ?? "";
            if (task is NodeCanvas.Tasks.Actions.ExecuteFunction_Multiplatform)
            {
                if (info.IndexOf("DamageCamFsm", StringComparison.Ordinal) >= 0)
                {
                    MoveHuntFx(info.IndexOf("Chase", StringComparison.Ordinal) >= 0 ? enemy.TargetId : 0);
                    return true;
                }
                if (info.IndexOf("SetVibration_Jumpscare", StringComparison.Ordinal) >= 0) { HuntFxTo(enemy.TargetId, 2); return true; }
                return false;
            }
            // Motion blur comes and goes with the jolt (CoopLife.HuntedEffects), on the hunted player's screen.
            return task is NodeCanvas.Tasks.Actions.ActivateGameObject && info.IndexOf("Motion Blur", StringComparison.Ordinal) >= 0;
        }

        private void MoveHuntFx(int peer)
        {
            if (peer == huntFxPeer) return;
            if (huntFxPeer != 0) HuntFxTo(huntFxPeer, 0);
            huntFxPeer = peer;
            if (peer != 0) HuntFxTo(peer, 1);
            plugin.Log("Nun chase effects: " + (peer == 0 ? "calm" : "on peer " + peer));
        }

        private void HuntFxTo(int peer, int mode)
        {
            if (peer == LocalId) Life.HuntedEffects(mode);
            else if (peer > 0) Send(peer, new Packet { type = "huntfx", epoch = epoch, scene = SceneName, number = mode });
        }

        /// <summary>Host: the jolt follows her if she turns on someone else mid-chase, and ends for a player she has put down.</summary>
        private void UpdateHuntFx()
        {
            if (huntFxPeer == 0) return;
            bool present = huntFxPeer == LocalId || players.ContainsKey(huntFxPeer);
            if (!present) { huntFxPeer = 0; return; }
            if (!enemy.Targetable(huntFxPeer)) { MoveHuntFx(0); return; }
            int target = enemy.TargetId;
            if (target != huntFxPeer && enemy.IsChasing && (target == LocalId || players.ContainsKey(target))) MoveHuntFx(target);
        }

        // The revolver raises the Nun's alarm (she goes to where it went off), but only on the shooter's own game, and only
        // the host's Nun hunts. The host hears each guest's shots (their count is in their frame) and raises it there.
        private readonly Dictionary<int, int> shotsHeard = new Dictionary<int, int>();
        private Transform gunshotOrigin;

        private void HearGunshots()
        {
            foreach (var frame in players.Values)
            {
                if (frame.id == LocalId || frame.scene != SceneName) continue;
                int before;
                bool known = shotsHeard.TryGetValue(frame.id, out before);
                shotsHeard[frame.id] = frame.shots;
                if (!known || frame.shots <= before || !Finite(frame.position)) continue;
                if (!gunshotOrigin) gunshotOrigin = new GameObject("NMM_GunshotOrigin").transform;
                gunshotOrigin.position = frame.position + Vector3.up * 1.4f;
                if (MessagesGranny.AlarmAlertEvent != null) MessagesGranny.AlarmAlertEvent(gunshotOrigin);
                enemy.SearchAround(gunshotOrigin.position, "a gunshot", null);
                plugin.Log("Peer " + frame.id + " fired at " + gunshotOrigin.position.ToString("0.0") + ": the Nun hears the gunshot");
            }
        }

        internal void WhistleNoise(Vector3 position)
        {
            if (Active && Ready && Finite(position)) Request(new Packet { action = "whistle", position = position });
        }

        private void Noise(Vector3 position, float radius)
        {
            if (!Active || !Ready || Replaying || Time.realtimeSinceStartup < nextNoise) return;
            nextNoise = Time.realtimeSinceStartup + .25f;
            Request(new Packet { action = "noise", position = position, amount = radius }); // Its own reach (older guests: none).
        }

        /// <summary>A loud voice from this player: noise the Nun can hear, and a call she may answer.</summary>
        internal void Shout(float level) { Request(new Packet { action = "shout", amount = level }); }
        internal void TestShout(int peer) { if (IsHost && players.ContainsKey(peer)) HearShout(peer, ShoutLevel); } // Automated tests.

        /// <summary>
        /// Host: a player's voice went loud (or any voice at all, from under a bed). She hears it at the level it was
        /// measured at: a sharp peak in ordinary speech counts as the speech it is, not as a shout. Heard from under a
        /// bed, she comes for that bed (EnemyDirector.Heard).
        /// </summary>
        // ---------- The host's choice: how the Nun hears ----------
        private bool hostOriginalHearing;
        /// <summary>The Nun hears as in co-op (voices, footsteps, every sound through the building on the raytraced acoustics,
        /// sharper on harder difficulties) unless the host chose ORIGINAL: as in the game alone.</summary>
        internal bool CoopHearing { get { return IsHost || !Active ? !plugin.OriginalHearing.Value : !hostOriginalHearing; } }

        /// <summary>Host: the hearing choice changed in the lobby; every guest is told.</summary>
        internal void HearingChanged()
        {
            if (IsHost && Active) Broadcast(new Packet { type = "hearing", epoch = epoch, originalHearing = plugin.OriginalHearing.Value });
        }

        private void HearShout(int peer, float level)
        {
            if (!CoopHearing) return; // As in the game alone: she does not hear voices.
            if (enemy.Shout(peer)) return; // A call she can see: she answers it herself.
            PlayerFrame frame;
            if (!players.TryGetValue(peer, out frame)) return;
            var sound = Ears.HearVoice(frame, Mathf.Clamp(level, 0f, MaxShout), Gameplay.SoundKind.Shout);
            if (sound == null) return;
            voiceHeardAt[peer] = Time.time;
            enemy.Heard(sound);
        }

        private const float ShoutLevel = 0.3f;   // A shout, for tests.
        private const float MaxShout = 0.5f;     // The loudest a voice can count, whatever a packet claims.
        private const float VoiceInterval = 1f;  // At most one sound a second from someone talking.
        internal static bool TestHearingLog;
        private readonly Dictionary<int, float> voiceHeardAt = new Dictionary<int, float>();

        /// <summary>
        /// The Nun hears a player's voice as a player would (host only, and only for players who let her: SETTINGS >
        /// MULTIPLAYER > NUN HEARS YOU): how loud they really speak, how far she is and what is in between (NunEars). A
        /// player talking for a while is one sound a second, so a conversation builds her suspicion rather than restarting
        /// her search with every word.
        /// </summary>
        internal void HearVoice(int peer, float rms)
        {
            if (!IsHost || !Ready || enemy == null || !CoopHearing) return;
            float level = Ears.Speech(peer, rms);
            float last;
            if (level <= 0f || (voiceHeardAt.TryGetValue(peer, out last) && Time.time - last < VoiceInterval)) return;
            PlayerFrame frame;
            if (!players.TryGetValue(peer, out frame)) return;
            var sound = Ears.HearVoice(frame, level, level >= ShoutLevel ? Gameplay.SoundKind.Shout : Gameplay.SoundKind.Voice);
            if (sound == null) return;
            voiceHeardAt[peer] = Time.time;
            enemy.Heard(sound);
        }

        private readonly Dictionary<int, float> nextStep = new Dictionary<int, float>();

        /// <summary>
        /// Footsteps make noise she hears (host only), every player's: running, a loud step about three times a second
        /// (7 m, then her sharper hearing); walking, a soft one twice a second she hears only close by (2 m); crouching,
        /// none. In the air, hiding, down, in a vent, in the bath or out of lives: none.
        /// </summary>
        private void UpdateFootsteps()
        {
            if (Message.Detectable.NoiseMadeEvent == null || !CoopHearing) return; // As in the game alone: no footsteps.
            foreach (var pair in players)
            {
                var frame = pair.Value;
                if (frame == null || !frame.body || frame.scene != SceneName || frame.spectating || frame.downed || frame.crouching || frame.vent || frame.jumping
                    || frame.bath || frame.state == (int)GrannyClone.PlayerStates.States.Hiding) continue;
                float speed = new Vector2(frame.velocity.x, frame.velocity.z).magnitude;
                if (speed < 1.2f) continue;
                bool running = speed > 3.4f;
                float next;
                if (nextStep.TryGetValue(pair.Key, out next) && Time.time < next) continue;
                nextStep[pair.Key] = Time.time + (running ? 0.33f : 0.5f);
                bool prior = Replaying; Replaying = true; // Not relayed as a guest's noise.
                Gameplay.NunEars.NextKind = Gameplay.SoundKind.Footstep; Gameplay.NunEars.NextSource = pair.Key;
                try { Message.Detectable.NoiseMadeEvent(frame.position, running ? 7f : 2f); }
                finally { Replaying = prior; Gameplay.NunEars.NextKind = Gameplay.SoundKind.Noise; Gameplay.NunEars.NextSource = 0; }
            }
        }

        internal bool RouteEnemyDamage(float amount)
        {
            if (!Active || !IsHost || enemyContext <= 1) return true;
            Send(enemyContext, new Packet { type = "damage", epoch = epoch, amount = Mathf.Clamp(amount, 0, 1000) });
            return false;
        }

        internal bool RouteEnemyState(PlayerStates.States state)
        {
            if (!Active || !IsHost || enemyContext <= 1) return true;
            Send(enemyContext, new Packet { type = "playerState", epoch = epoch, number = (int)state });
            return false;
        }

        internal int EnemyTarget { get { return enemy.TargetId; } }

        private bool IsTargetable(int peer)
        {
            if (peer == LocalId) return Life.Targetable;
            PlayerFrame frame;
            return players.TryGetValue(peer, out frame) && frame.body && !frame.downed && !frame.spectating && frame.health > 0 && frame.scene == SceneName;
        }
    }
}
