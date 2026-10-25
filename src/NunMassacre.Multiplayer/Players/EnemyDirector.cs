using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using GrannyClone;
using HarmonyLib;
using NodeCanvas.Framework;
using NodeCanvas.StateMachines;
using UnityEngine;

namespace NunMassacre.Multiplayer.Players
{
    /// <summary>
    /// Host only. Keeps the game's own Nun graph, navigation and attacks, but points them at the
    /// chosen player: graph references that cached the single local player are redirected to a
    /// teammate's avatar, avatars get the same sight target and lighting test as the real player,
    /// and the Nun's weapon swings are traced against avatars. Teammates can also call her: a shout
    /// she can see draws her off a chase, and a shout while she closes in on a hidden player makes her
    /// stop and maybe follow the voice.
    /// </summary>
    internal sealed partial class EnemyDirector : IDisposable
    {
        private enum Reaction { None, Turning, BedPause, Following, Dragging, Looming, Listening }
        private const float LoomSeconds = 3.5f;
        /// <summary>How far a shout carries: her own hearing reacts within this radius.</summary>
        internal const float ShoutRadius = 20f;
        private const float CallRange = 25f, TurnSeconds = 1.1f, BedPauseSeconds = 1.4f, SwitchCooldown = 8f, CommittedDistance = 1.6f, TurnSpeed = 220f;
        private static readonly HashSet<string> ChaseStates = new HashSet<string> { "Setup", "Chase", "Jump Scare",
            "Music 1", "Music 2", "Reset Can't See Timer" };
        private IDictionary<int, PlayerFrame> lastFrames;
        private Func<string, Transform> lastFind;
        private Func<int, Transform> lastBodyOf;
        private string lastScene;
        private Reaction reaction;
        private int caller;
        private Vector3 voice;
        private float reactionEnds, lockedUntil, committedAt, nextSwitch;
        private bool followVoice, bedPaused, sendSightAlert;
        internal static bool? TestFollow; // Automated tests: force the follow-the-voice decision.
        private static Vector3? soundSearch; private static float soundSearchUntil;
        /// <summary>The sound her next search should start from, once (see GameHooks.SearchStartPostfix).</summary>
        internal static Vector3? TakeSoundSearch()
        {
            var value = Time.time <= soundSearchUntil ? soundSearch : null;
            soundSearch = null;
            return value;
        }
        private sealed class Binding { public object Original; public Action<object> Set; public string Kind; }
        private sealed class Weapon { public MeleeWeapon Component; public Transform Start, End; public float Damage; public bool Swinging; public float Closest; }
        private static readonly FieldInfo CauseDamageField = AccessTools.Field(typeof(MeleeWeapon), "causeDamage");
        private static readonly FieldInfo StartRayField = AccessTools.Field(typeof(MeleeWeapon), "startRay");
        private static readonly FieldInfo EndRayField = AccessTools.Field(typeof(MeleeWeapon), "endRay");
        private static readonly FieldInfo DamageField = AccessTools.Field(typeof(MeleeWeapon), "damageAmount");
        private readonly List<Binding> bindings = new List<Binding>();
        private readonly HashSet<object> bound = new HashSet<object>();
        private readonly HashSet<Graph> graphs = new HashSet<Graph>();
        private readonly HashSet<NodeCanvas.Framework.Task> tasks = new HashSet<NodeCanvas.Framework.Task>();
        private readonly Dictionary<int, Transform> heads = new Dictionary<int, Transform>();
        private readonly List<Weapon> weapons = new List<Weapon>();
        private readonly Action<string> log;
        private FSMOwner nun;
        private pReferences local;
        private float nextScan, nextLight, distractedUntil, nextSwingLog;
        private bool distracted;
        internal int TargetId { get; private set; } = 1;
        private const float HitRadius = 0.5f; // Avatar body radius plus blade reach.
        /// <summary>Whether a player may be hunted (alive, standing, in this scene).</summary>
        internal Func<int, bool> Targetable = id => true;
        /// <summary>Routes a weapon hit on a teammate's avatar to that peer.</summary>
        internal Action<int, float> DamagePeer;
        internal EnemyDirector(Action<string> logger) { log = logger; }

        internal void Tick(pReferences player, IDictionary<int, PlayerFrame> frames, RemotePlayers avatars, Func<string, Transform> find)
        {
            if (!player || !player.camera) return;
            local = player;
            if (!nun)
            {
                var difficulty = UnityEngine.Object.FindObjectOfType<DifficultyController>();
                if (difficulty) nun = AccessTools.Field(typeof(DifficultyController), "NunFSM").GetValue(difficulty) as FSMOwner;
                if (!nun) return;
            }
            if (Time.unscaledTime >= nextScan)
            {
                nextScan = Time.unscaledTime + 3f;
                Discover(nun.graph);
                ScanWeapons();
                TakeSearchPoints();
            }
            string scene = UnityEngine.SceneManagement.SceneManager.GetActiveScene().name;
            Transform localRoot = ((Component)local).transform;
            Vector3 eye = nun.transform.position + Vector3.up * 1.4f;
            int wallMask = LayerMask.GetMask("Wall", "Ground", "Ceiling", "Furniture", "BlockSight", "Door");
            Func<int, Transform> bodyOf = id => id == 1 ? localRoot : avatars.GetTransform(id);
            lastFrames = frames; lastBodyOf = bodyOf; lastScene = scene; lastFind = find;
            TuneForGroup(frames.Values.Count(f => f.scene == scene));

            // A call in progress: she stands still and turns to the voice, then acts.
            if (reaction != Reaction.None)
            {
                UpdateReaction(frames, bodyOf, scene);
                if (reaction != Reaction.None) { TraceWeapons(avatars, frames); return; }
            }
            if (!ApproachingBed()) bedPaused = false;

            // Prefer the closest player she can see; otherwise keep chasing the current target while the chase lasts;
            // otherwise whoever is off on their own.
            int selected = 0; float nearest = float.MaxValue;
            int fallback = 0; float fallbackDistance = float.MaxValue;
            bool currentValid = false;
            foreach (var frame in frames.Values)
            {
                if (frame.scene != scene || !Targetable(frame.id)) continue;
                Transform target = frame.id == 1 ? localRoot : avatars.GetTransform(frame.id);
                if (!target) continue;
                if (frame.id == TargetId) currentValid = true;
                Vector3 point = frame.position + Vector3.up * Mathf.Clamp(frame.height * .8f, .25f, 1.6f);
                float distance = Vector3.Distance(eye, point);
                // Out of her sight she goes after whoever is on their own: distance off by how far they are from the
                // nearest teammate (up to 20 m), and a little stickiness so she does not flit between them.
                float alone = 0f;
                foreach (var other in frames.Values)
                    if (other.id != frame.id && other.scene == scene && Targetable(other.id))
                        alone = alone == 0f ? Vector3.Distance(frame.position, other.position)
                            : Mathf.Min(alone, Vector3.Distance(frame.position, other.position));
                float score = distance - Mathf.Clamp(alone - 4f, 0f, 20f) - (frame.id == TargetId ? 3f : 0f);
                if (score < fallbackDistance) { fallbackDistance = score; fallback = frame.id; }
                if (frame.state == (int)PlayerStates.States.Hiding) continue;
                if (distance > 40f || (wallMask != 0 && Physics.Linecast(eye, point, wallMask, QueryTriggerInteraction.Ignore))) continue;
                float weighted = distance + (frame.id == TargetId ? -2f : 0f);
                if (weighted < nearest) { nearest = weighted; selected = frame.id; }
            }
            // Nobody in sight: mid-chase or mid-attack she stays on the one she was after; otherwise she goes for the one
            // off on their own (the score above).
            if (selected == 0)
                selected = currentValid && (Chasing() || Committed() || Pursuing()) ? TargetId : fallback != 0 ? fallback : currentValid ? TargetId : 1;
            // After answering a call she stays on the caller while the chase lasts, so the players
            // cannot spin her between them.
            if (Time.time < lockedUntil && Time.time > committedAt + 2f && !Chasing()) lockedUntil = 0f;
            if (Time.time < lockedUntil)
            {
                PlayerFrame callerFrame;
                if (frames.TryGetValue(caller, out callerFrame) && callerFrame.scene == scene && Targetable(caller) && bodyOf(caller)) selected = caller;
                else lockedUntil = 0f;
            }
            // Mid grab, swing or bed drag she keeps her victim: camera effects and damage stay on that player.
            if (currentValid && selected != TargetId && Committed()) selected = TargetId;
            TargetId = selected;
            Transform selectedRoot = selected == 1 ? localRoot : avatars.GetTransform(selected);
            if (!selectedRoot) { TargetId = 1; selectedRoot = localRoot; }

            PlayerFrame selectedFrame;
            frames.TryGetValue(TargetId, out selectedFrame);
            var head = local.camera.transform;
            var sight = local.GetComponent<Detectable.SightDetectable>();
            if (TargetId != 1)
            {
                if (!heads.TryGetValue(TargetId, out head) || !head)
                {
                    head = new GameObject("NMM_RemoteHead").transform;
                    head.SetParent(selectedRoot, false); heads[TargetId] = head;
                }
                if (selectedFrame != null)
                {
                    head.position = selectedFrame.position + Vector3.up * Mathf.Clamp(selectedFrame.height * .88f, .25f, 1.7f);
                    if (selectedFrame.look.sqrMagnitude > .01f) head.rotation = Quaternion.LookRotation(selectedFrame.look);
                }
                sight = SightFor(selectedRoot, selectedFrame);
            }
            if (Time.unscaledTime >= nextLight)
            {
                // The same lighting test the game runs for the local player (LightDetectCast).
                nextLight = Time.unscaledTime + 0.25f;
                foreach (var frame in frames.Values)
                {
                    if (frame.id == 1 || frame.scene != scene) continue;
                    Transform root = avatars.GetTransform(frame.id);
                    if (!root) continue;
                    var avatarSight = SightFor(root, frame);
                    avatarSight.Illumination = 0f;
                    if (Message.Detectable.IlluminationScanEvent != null) Message.Detectable.IlluminationScanEvent(avatarSight);
                }
            }
            foreach (var binding in bindings)
            {
                object value = binding.Original;
                if (TargetId != 1)
                {
                    switch (binding.Kind)
                    {
                        case "body": value = selectedRoot; break;
                        case "head": value = head; break;
                        case "object": value = selectedRoot.gameObject; break;
                        case "sight": value = sight; break;
                        case "collider": value = selectedRoot.GetComponent<Collider>(); break;
                    }
                }
                try { binding.Set(value); } catch (Exception ex) { log("Nun target binding: " + ex.Message); }
            }
            if (selectedFrame != null)
            {
                // Her "player in a vent" flag (what sends her to taunt them at their vent and gas it) follows her quarry.
                var inVent = nun.blackboard != null ? nun.blackboard.GetVariable("In Vent Player") : null;
                if (inVent != null && inVent.value is bool && (bool)inVent.value != selectedFrame.vent) inVent.value = selectedFrame.vent;
                foreach (var task in tasks)
                {
                    if (task is NodeCanvasTasks.EnemyCanSeeTarget)
                        AccessTools.Field(task.GetType(), "targetCollider").SetValue(task, selectedRoot.GetComponent<Collider>());
                    else if (task is GrannyClone.Tasks.PlayerCrouchingCheck)
                        AccessTools.Field(task.GetType(), "crouchCached").SetValue(task, selectedFrame.crouching);
                    else if (task is GrannyClone.Tasks.PlayerInVentCheck)
                        AccessTools.Field(task.GetType(), "toggle").SetValue(task, selectedFrame.vent);
                    else if (task is GrannyClone.Tasks.LighterCheck)
                    {
                        AccessTools.Field(task.GetType(), "lighterCached").SetValue(task, selectedFrame.light);
                        AccessTools.Field(task.GetType(), "flashlightCached").SetValue(task, selectedFrame.light);
                    }
                    else if (task is GrannyClone.Tasks.UnderBedHidingCheck)
                    {
                        var hiding = (GrannyClone.Tasks.UnderBedHidingCheck)task;
                        AccessTools.Field(task.GetType(), "isUnderBedCached").SetValue(task, selectedFrame.state == (int)PlayerStates.States.Hiding);
                        if (!string.IsNullOrEmpty(selectedFrame.bed)) hiding.BedPlayerHidingUnder.value = find(selectedFrame.bed);
                        if (!string.IsNullOrEmpty(selectedFrame.bedKillPoint)) hiding.KillerPt.value = find(selectedFrame.bedKillPoint);
                    }
                }
            }
            TraceWeapons(avatars, frames);
            UpdateDistraction();
            UpdateInvestigation();
            UpdateSearchPoints(SearchFocus(selectedRoot));
            CheckOnTheDowned(frames, scene);
            if (sendSightAlert)
            {
                // Committing to the caller: her own chase start, now aimed at them.
                sendSightAlert = false;
                var marker = Variable<Transform>("Target");
                if (marker && selectedRoot) marker.position = selectedRoot.position; // Her chase start turns toward the marker first.
                nun.SendEvent("SightAlert");
            }
        }

        /// <summary>The Nun's own graph (her bed check is told apart from puzzle machines by it).</summary>
        internal bool IsNunGraph(Graph graph) { return nun && graph != null && nun.graph == graph; }
        internal bool IsChasing { get { return Chasing(); } }
        internal Vector3 BodyShape { get { return nun ? NunPresence.BodyShape(nun.transform) : Vector3.zero; } }

        private static readonly FieldInfo OnCameraCounter = AccessTools.Field(typeof(Humanoid.Agent.EnemyHunter.Tasks.IsOnCamera), "counter");
        /// <summary>
        /// Her jump scare waits until she is on the hunted player's screen. The game checks Camera.main, which is the host's;
        /// for a teammate it is their view (their eyes and where they look), with the game's own hold-for-a-moment. Null:
        /// the game's own check (she is after the host, or it is not her graph).
        /// </summary>
        internal bool? OnHuntedScreen(Humanoid.Agent.EnemyHunter.Tasks.IsOnCamera task)
        {
            if (!nun || TargetId == 1 || !graphs.Contains(task.ownerSystem as Graph)) return null;
            Transform head;
            var checkCollider = task.Collider != null ? task.Collider.value : null;
            if (!heads.TryGetValue(TargetId, out head) || !head || !checkCollider || !task.agent) return false;
            Camera main = Camera.main;
            var projection = Matrix4x4.Perspective(main ? main.fieldOfView : 60f, main ? main.aspect : 16f / 9f, 0.05f, 200f);
            var view = Matrix4x4.TRS(head.position, head.rotation, new Vector3(1f, 1f, -1f)).inverse;
            bool seen = false;
            if (GeometryUtility.TestPlanesAABB(GeometryUtility.CalculateFrustumPlanes(projection * view), checkCollider.bounds))
            {
                RaycastHit hit;
                Vector3 point = task.agent.position + (task.Offset != null ? task.Offset.value : Vector3.zero);
                if (Physics.Linecast(head.position, point, out hit, task.layers))
                    seen = hit.collider == checkCollider
                        || (task.tagToCheck != null && !string.IsNullOrEmpty(task.tagToCheck.value) && hit.collider.CompareTag(task.tagToCheck.value));
            }
            if (OnCameraCounter == null) return seen;
            float counter = seen ? (float)OnCameraCounter.GetValue(task) + Time.deltaTime : 0f;
            bool held = seen && counter > (task.Delay != null ? task.Delay.value : 0f);
            OnCameraCounter.SetValue(task, held ? 0f : counter);
            return held;
        }
        internal Vector3? NunPosition { get { return nun ? nun.transform.position : (Vector3?)null; } }
        /// <summary>The bed she is about to lift (her graph's own variable).</summary>
        internal Transform BedBeingSearched { get { return Variable<Transform>("Bed Player Hiding Under"); } }

        /// <summary>
        /// Someone gave themselves away under a bed (too loud, or whistled): she goes to that bed and lifts it, her own
        /// bed check. Not while she is already busy with someone (mid-swing, mid-drag), or too far to have heard.
        /// </summary>
        internal bool BedSearch(int peer, string why)
        {
            if (!nun || lastFrames == null || lastBodyOf == null) return false;
            PlayerFrame frame;
            if (!lastFrames.TryGetValue(peer, out frame) || frame.state != (int)PlayerStates.States.Hiding
                || string.IsNullOrEmpty(frame.bed) || !Targetable(peer))
                return false;
            if (Committed() || reaction != Reaction.None) { log("Nun did not go to peer " + peer + "'s bed: she is busy"); return false; }
            float distance = Vector3.Distance(nun.transform.position, frame.position);
            TargetId = peer; caller = peer;
            committedAt = Time.time; lockedUntil = Time.time + 25f; // Hers until the bed is lifted.
            UpdateTargetBed(frame);
            nun.SendEvent("BedKill");
            Scream();
            log("Nun heard peer " + peer + " " + why + " under a bed " + distance.ToString("0.0") + "m away: she goes to lift it");
            return true;
        }

        private void UpdateTargetBed(PlayerFrame frame)
        {
            foreach (var task in tasks)
            {
                var hiding = task as GrannyClone.Tasks.UnderBedHidingCheck;
                if (hiding == null) continue;
                AccessTools.Field(task.GetType(), "isUnderBedCached").SetValue(task, true);
                var bed = lastFind != null ? lastFind(frame.bed) : null;
                var stand = lastFind != null && !string.IsNullOrEmpty(frame.bedKillPoint) ? lastFind(frame.bedKillPoint) : null;
                if (bed) hiding.BedPlayerHidingUnder.value = bed;
                if (stand) hiding.KillerPt.value = stand;
            }
        }

        /// <summary>She has lifted the bed and is pulling them out: she stays put, facing them, until they are up.</summary>
        internal void HoldForDrag(float seconds, Vector3 at)
        {
            if (!nun) return;
            voice = at;
            Freeze(Reaction.Dragging, seconds);
            log("Nun drags them out from under the bed (" + seconds.ToString("0.0") + "s)");
        }

        /// <summary>A teammate shouted (host only). True when she answers the call herself; otherwise
        /// the shout is left to her own hearing.</summary>
        internal bool Shout(int peer)
        {
            if (!nun || lastFrames == null || reaction != Reaction.None) return false;
            try { return Answer(peer, lastFrames, lastBodyOf, lastScene); }
            catch (Exception ex) { log("Nun call: " + ex.Message); return false; }
        }

        private bool Answer(int peer, IDictionary<int, PlayerFrame> frames, Func<int, Transform> bodyOf, string scene)
        {
            Vector3 eye = nun.transform.position + Vector3.up * 1.4f;
            int wallMask = LayerMask.GetMask("Wall", "Ground", "Ceiling", "Furniture", "BlockSight", "Door");
            PlayerFrame frame;
            Transform body = bodyOf(peer);
            if (!body || !frames.TryGetValue(peer, out frame) || frame.scene != scene || !Targetable(peer) || frame.state == (int)PlayerStates.States.Hiding)
                return false;
            Vector3 mouth = body.position + Vector3.up * (frame.crouching ? 1.0f : 1.5f);
            float distance = Vector3.Distance(eye, mouth);
            bool inView = distance <= CallRange && !Physics.Linecast(eye, mouth, wallMask, QueryTriggerInteraction.Ignore);
            if (ApproachingBed())
            {
                // She is closing in on a hidden player (hiders' own shouts were skipped above).
                if (inView && Time.time >= nextSwitch) { BeginTurn(peer, body.position, "called off the bed check"); return true; }
                if (bedPaused || distance > ShoutRadius) return false;
                bedPaused = true;
                followVoice = TestFollow ?? UnityEngine.Random.value < Mathf.Lerp(0.9f, 0.55f, Mathf.InverseLerp(6f, ShoutRadius, distance));
                voice = body.position;
                caller = peer;
                Freeze(Reaction.BedPause, BedPauseSeconds);
                log("Nun heard peer " + peer + " " + distance.ToString("0.0") + "m away while closing in on a hiding spot; "
                    + (followVoice ? "she will follow the voice" : "she will go back to the bed"));
                return true;
            }
            if (!Chasing() || peer == TargetId) return false; // Out of a chase her own hearing handles it.
            if (!inView) { log("Nun did not see peer " + peer + " call (" + distance.ToString("0.0") + "m, out of sight)"); return false; }
            if (Time.time < nextSwitch) { log("Nun ignored peer " + peer + "'s call: she only just switched"); return false; }
            Transform current = bodyOf(TargetId);
            if (current && Vector3.Distance(nun.transform.position, current.position) <= CommittedDistance)
            {
                log("Nun ignored peer " + peer + "'s call: too close to peer " + TargetId);
                return false;
            }
            BeginTurn(peer, body.position, "called off peer " + TargetId + " from " + distance.ToString("0.0") + "m");
            return true;
        }

        private void BeginTurn(int peer, Vector3 position, string why)
        {
            caller = peer; voice = position;
            Freeze(Reaction.Turning, TurnSeconds);
            log("Nun: peer " + peer + " " + why + "; she turns to them");
        }

        private void UpdateReaction(IDictionary<int, PlayerFrame> frames, Func<int, Transform> bodyOf, string scene)
        {
            if (!nun) { reaction = Reaction.None; return; }
            if (reaction == Reaction.Following) { UpdateFollow(); return; }
            if (reaction == Reaction.Listening && SeesSomeone(frames, bodyOf, scene)) { Unfreeze(); return; }
            Transform body = bodyOf(caller);
            if (reaction == Reaction.Turning && body) voice = body.position; // Follow the caller while turning.
            Vector3 to = voice - nun.transform.position; to.y = 0f;
            if (to.sqrMagnitude > 0.01f)
                nun.transform.rotation = Quaternion.RotateTowards(nun.transform.rotation, Quaternion.LookRotation(to), TurnSpeed * Time.deltaTime);
            var ik = Variable<Transform>("IkTarget");
            if (ik) ik.position = voice + Vector3.up * (reaction == Reaction.Looming ? 0.2f : 1.5f); // Her head leads the turn; over a body, looks down at it.
            if (Time.time < reactionEnds) return;
            if (reaction == Reaction.Listening) { Unfreeze(); return; }
            if (reaction == Reaction.BedPause && followVoice) { BeginFollow(); return; }
            if (reaction == Reaction.Dragging) { Unfreeze(); log("Nun: they are up; she attacks peer " + TargetId); return; }
            if (reaction == Reaction.Looming) { Unfreeze(); nun.SendEvent("StopChase"); log("Nun leaves the body"); return; }

            var done = reaction;
            Unfreeze();
            if (done == Reaction.Turning)
            {
                PlayerFrame frame;
                if (!body || !frames.TryGetValue(caller, out frame) || frame.scene != scene || !Targetable(caller))
                {
                    log("Nun: peer " + caller + " is gone; she resumes");
                    return;
                }
                TargetId = caller;
                committedAt = Time.time; lockedUntil = nextSwitch = Time.time + SwitchCooldown;
                sendSightAlert = true;
                Scream();
                log("Nun: now chasing peer " + caller);
            }
            else log("Nun went back to the hiding spot");
        }

        // She abandons the hidden player and walks to where the voice came from (her graph stays paused,
        // so nothing steers her back), then searches there through her own hearing reaction.
        private void BeginFollow()
        {
            reaction = Reaction.Following;
            var agent = nun.GetComponent<UnityEngine.AI.NavMeshAgent>();
            UnityEngine.AI.NavMeshHit hit;
            if (agent && UnityEngine.AI.NavMesh.SamplePosition(voice, out hit, 3f, agent.areaMask)) voice = hit.position;
            if (agent && agent.enabled && agent.isOnNavMesh) { agent.isStopped = false; agent.SetDestination(voice); }
            reactionEnds = Time.time + Mathf.Clamp(Vector3.Distance(nun.transform.position, voice) * 0.8f + 3f, 4f, 15f);
            log("Nun left the hiding spot to follow peer " + caller + "'s voice");
        }

        private void UpdateFollow()
        {
            var agent = nun.GetComponent<UnityEngine.AI.NavMeshAgent>();
            Vector3 gap = voice - nun.transform.position; gap.y = 0f;
            bool arrived = !agent || !agent.isOnNavMesh || gap.magnitude <= 1.5f || (!agent.pathPending && agent.hasPath && agent.remainingDistance <= 1.5f);
            if (!arrived && Time.time < reactionEnds) return;
            Unfreeze();
            nun.blackboard.SetValue("IgnoreSight", false);
            nun.blackboard.SetValue("IgnoreHearing", false);
            nun.blackboard.SetValue("CanAttack", false);
            var marker = Variable<Transform>("Target");
            if (marker) marker.position = voice;
            soundSearch = voice; soundSearchUntil = Time.time + 6f;
            nun.SendEvent("NoiseAlert");
            log("Nun " + (arrived ? "reached" : "gave up walking to") + " the voice " + gap.magnitude.ToString("0.0") + "m away and searches around it");
        }

        // She stands still: her graph is paused (it would otherwise keep steering her) and her agent stopped.
        private void Freeze(Reaction kind, float seconds)
        {
            reaction = kind;
            reactionEnds = Time.time + seconds;
            nun.PauseBehaviour();
            var agent = nun.GetComponent<UnityEngine.AI.NavMeshAgent>();
            if (agent && agent.enabled && agent.isOnNavMesh) { agent.isStopped = true; agent.velocity = Vector3.zero; }
            foreach (var animator in nun.GetComponentsInChildren<Animator>())
                if (animator.runtimeAnimatorController && animator.parameters.Any(p => p.name == "Attack")) animator.SetBool("Attack", false);
        }

        private void Unfreeze()
        {
            reaction = Reaction.None;
            if (!nun) return;
            var agent = nun.GetComponent<UnityEngine.AI.NavMeshAgent>();
            if (agent && agent.enabled && agent.isOnNavMesh) agent.isStopped = false;
            if (nun.isPaused) nun.StartBehaviour();
        }

        private void Scream()
        {
            var scream = nun.transform.root.GetComponentsInChildren<AudioSourceRandom>(true).FirstOrDefault(a => a.name == "Scream");
            if (scream) scream.PlayRandom();
        }

        /// <summary>Busy with one player: dragging them from under a bed, swinging, or a scripted grab.</summary>
        private bool Committed()
        {
            var fsm = nun ? nun.graph as FSM : null;
            var state = fsm == null ? null : fsm.currentState as ActionState;
            if (state != null && state.actionList != null
                && state.actionList.actions.Any(a => a is GrannyClone.Tasks.FlipBed || a is GrannyClone.Tasks.PlayerStateChange))
                return true;
            if (!attackAnimator && nun)
            {
                attackAnimator = nun.GetComponentInChildren<Animator>();
                hasAttack = attackAnimator && attackAnimator.runtimeAnimatorController && attackAnimator.parameters.Any(p => p.name == "Attack");
            }
            return hasAttack && attackAnimator && attackAnimator.GetBool("Attack");
        }
        private Animator attackAnimator;
        private bool hasAttack;

        // Still after the one she lost: taunting them at their vent, gassing it, or running to where she last saw them.
        private static readonly HashSet<string> PursuitStates = new HashSet<string> { "Taunt Player at current vent",
            "Turn on the Gas", "Goto Last Visible Pt" };
        private bool Pursuing()
        {
            var fsm = nun ? nun.graph as FSM : null;
            return fsm != null && fsm.currentState != null && PursuitStates.Contains(fsm.currentStateName);
        }

        /// <summary>A player standing in front of her, close and in plain view.</summary>
        private bool SeesSomeone(IDictionary<int, PlayerFrame> frames, Func<int, Transform> bodyOf, string scene)
        {
            Vector3 eye = nun.transform.position + Vector3.up * 1.4f;
            int walls = LayerMask.GetMask("Wall", "Ground", "Ceiling", "Furniture", "BlockSight", "Door");
            foreach (var frame in frames.Values)
            {
                if (frame.scene != scene || !Targetable(frame.id) || frame.state == (int)PlayerStates.States.Hiding || !bodyOf(frame.id)) continue;
                Vector3 point = frame.position + Vector3.up * Mathf.Clamp(frame.height * .8f, .25f, 1.6f), to = point - eye;
                if (to.magnitude > 15f || Vector3.Angle(nun.transform.forward, to) > 60f) continue;
                if (!Physics.Linecast(eye, point, walls, QueryTriggerInteraction.Ignore)) return true;
            }
            return false;
        }

        private bool Chasing()
        {
            var fsm = nun ? nun.graph as FSM : null;
            return fsm != null && fsm.currentState != null && ChaseStates.Contains(fsm.currentStateName);
        }

        /// <summary>Running to a bed to drag out the player hiding under it, and not there yet.</summary>
        private bool ApproachingBed()
        {
            var fsm = nun ? nun.graph as FSM : null;
            var state = fsm == null ? null : fsm.currentState as ActionState;
            if (state == null || state.actionList == null || !state.actionList.actions.Any(a => a is GrannyClone.Tasks.FlipBed)) return false;
            var stand = Variable<Transform>("Bed Killer Stand Pt");
            if (!stand) return false;
            Vector3 gap = stand.position - nun.transform.position; gap.y = 0f;
            return gap.magnitude > 0.6f;
        }

        private T Variable<T>(string name) where T : class
        {
            if (!nun || nun.blackboard == null) return null;
            var variable = nun.blackboard.GetVariable(name);
            return variable == null ? null : variable.value as T;
        }

        /// <summary>The Nun's weapons only damage colliders tagged Player, which avatars are not (the
        /// game finds its local player by that tag). Trace each active swing against avatars instead.</summary>
        private void TraceWeapons(RemotePlayers avatars, IDictionary<int, PlayerFrame> frames)
        {
            if (DamagePeer == null || CauseDamageField == null) return;
            foreach (var weapon in weapons)
            {
                bool active = weapon.Component && weapon.Start && weapon.End && weapon.Component.isActiveAndEnabled
                    && (bool)CauseDamageField.GetValue(weapon.Component);
                if (!active)
                {
                    if (weapon.Swinging && weapon.Closest < 10f && Time.unscaledTime >= nextSwingLog)
                    {
                        nextSwingLog = Time.unscaledTime + 1f;
                        log("Nun swing missed avatars; closest " + weapon.Closest.ToString("0.00") + "m");
                    }
                    weapon.Swinging = false; continue;
                }
                if (!weapon.Swinging) { weapon.Swinging = true; weapon.Closest = float.MaxValue; }
                // The Nun can stand inside an avatar's (trigger) capsule, where a linecast would never
                // register, so measure the blade against each body's centre line instead.
                foreach (var frame in frames.Values)
                {
                    if (frame.id <= 1 || !Targetable(frame.id)) continue;
                    Transform body = avatars.GetTransform(frame.id);
                    if (!body) continue;
                    float top = frame.crouching ? 1.0f : 1.6f;
                    float distance = SegmentDistance(weapon.Start.position, weapon.End.position,
                        body.position + Vector3.up * 0.35f, body.position + Vector3.up * top);
                    weapon.Closest = Mathf.Min(weapon.Closest, distance);
                    if (distance > HitRadius) continue;
                    weapon.Swinging = false;
                    weapon.Component.CauseDamage = false; // One hit per swing, as the game does.
                    DamagePeer(frame.id, weapon.Damage);
                    log("Nun hit peer " + frame.id + " for " + weapon.Damage);
                    break;
                }
            }
        }

        /// <summary>Closest distance between segments p1-q1 and p2-q2.</summary>
        private static float SegmentDistance(Vector3 p1, Vector3 q1, Vector3 p2, Vector3 q2)
        {
            Vector3 d1 = q1 - p1, d2 = q2 - p2, r = p1 - p2;
            float a = Vector3.Dot(d1, d1), e = Vector3.Dot(d2, d2), f = Vector3.Dot(d2, r);
            float s, t;
            if (a <= 1e-6f && e <= 1e-6f) return r.magnitude;
            if (a <= 1e-6f) { s = 0f; t = Mathf.Clamp01(f / e); }
            else
            {
                float c = Vector3.Dot(d1, r);
                if (e <= 1e-6f) { t = 0f; s = Mathf.Clamp01(-c / a); }
                else
                {
                    float b = Vector3.Dot(d1, d2), denominator = a * e - b * b;
                    s = denominator != 0f ? Mathf.Clamp01((b * f - c * e) / denominator) : 0f;
                    t = (b * s + f) / e;
                    if (t < 0f) { t = 0f; s = Mathf.Clamp01(-c / a); }
                    else if (t > 1f) { t = 1f; s = Mathf.Clamp01((b - c) / a); }
                }
            }
            return ((p1 + d1 * s) - (p2 + d2 * t)).magnitude;
        }

        private void ScanWeapons()
        {
            weapons.RemoveAll(w => !w.Component);
            if (!nun) return;
            // Animators skip bone updates when no camera sees them. The host often cannot see the
            // Nun while she attacks a teammate, and her weapon would never swing.
            foreach (var animator in nun.transform.root.GetComponentsInChildren<Animator>(true))
                if (animator.cullingMode != AnimatorCullingMode.AlwaysAnimate && animator.transform.IsChildOf(nun.transform))
                    animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
            foreach (var component in nun.transform.root.GetComponentsInChildren<MeleeWeapon>(true))
            {
                if (weapons.Any(w => w.Component == component)) continue;
                weapons.Add(new Weapon
                {
                    Component = component,
                    Start = StartRayField == null ? null : StartRayField.GetValue(component) as Transform,
                    End = EndRayField == null ? null : EndRayField.GetValue(component) as Transform,
                    Damage = DamageField == null ? 20f : (float)DamageField.GetValue(component)
                });
                log("Nun weapon: " + component.name + " damage " + weapons[weapons.Count - 1].Damage);
            }
        }

        /// <summary>A sight target on the avatar matching the local player's "Detectable Middle".</summary>
        private Detectable.SightDetectable SightFor(Transform root, PlayerFrame frame)
        {
            Transform child = root.Find("NMM_Sight");
            Detectable.SightDetectable sight;
            if (!child)
            {
                var go = new GameObject("NMM_Sight");
                go.transform.SetParent(root, false);
                int layer = LayerMask.NameToLayer("CameraTrigger");
                if (layer >= 0) go.layer = layer;
                go.tag = "EditorOnly"; // LightDetectCast counts hits on colliders sharing the detectable's tag.
                var sphere = go.AddComponent<SphereCollider>();
                sphere.isTrigger = true; sphere.radius = 0.3f;
                sight = go.AddComponent<Detectable.SightDetectable>();
                sight.enabled = false; // Its Start assumes a CharacterController; the host drives it instead.
                sight.SightColliders = new[] { go.transform };
                var reference = local ? local.GetComponent<Detectable.SightDetectable>() : null;
                if (reference) sight.IlluminationCurve = reference.IlluminationCurve;
                child = go.transform;
            }
            else sight = child.GetComponent<Detectable.SightDetectable>();
            float height = frame == null ? 0.9f : frame.downed ? 0.3f : frame.state == (int)PlayerStates.States.Hiding ? 0.25f : frame.crouching ? 0.55f : 0.9f;
            child.localPosition = new Vector3(0f, height, 0f);
            return sight;
        }

        /// <summary>After a knockdown: stop the chase and blind her briefly so teammates can react.</summary>
        /// <summary>
        /// She has put a player down: she stands over them a moment, looking down at them, then walks off blind and deaf for
        /// the rest of the window before she searches again (a chance to regroup, and to revive right under her nose).
        /// While they lie there she keeps coming back to search round the body: a downed teammate is bait.
        /// </summary>
        internal void KnockedDown(int peer, Vector3 at, float seconds)
        {
            if (!nun) return;
            if (reaction != Reaction.None) Unfreeze(); // A knockdown overrides a call in progress.
            lockedUntil = 0f;
            distractedUntil = Time.time + seconds;
            distracted = true;
            nun.SendEvent("StopChase");
            voice = at;
            Freeze(Reaction.Looming, Mathf.Min(LoomSeconds, seconds));
            nextBodyCheck = Time.time + UnityEngine.Random.Range(25f, 40f);
            log("Nun stands over peer " + peer + " (" + LoomSeconds.ToString("0.0") + "s)");
        }

        // ---------- Back to the body ----------
        private float nextBodyCheck;
        /// <summary>Every 35 to 55 s while someone lies downed (and she is not after anyone she can see), she goes back
        /// and searches round them.</summary>
        private void CheckOnTheDowned(IDictionary<int, PlayerFrame> frames, string scene)
        {
            if (Time.time < nextBodyCheck || reaction != Reaction.None || distracted || Chasing() || Committed() || Pursuing() || ApproachingBed()) return;
            PlayerFrame body = null; float nearest = float.MaxValue;
            foreach (var frame in frames.Values)
            {
                if (frame.scene != scene || !frame.downed || frame.bleed <= 0f) continue;
                float distance = Vector3.Distance(nun.transform.position, frame.position);
                if (distance < nearest) { nearest = distance; body = frame; }
            }
            if (body == null) { nextBodyCheck = Time.time + 5f; return; }
            nextBodyCheck = Time.time + UnityEngine.Random.Range(35f, 55f);
            if (nearest < 6f) return; // Already there.
            SearchAround(body.position, "checking on peer " + body.id + ", " + nearest.ToString("0") + "m away", "AlertedSearch");
        }

        private void UpdateDistraction()
        {
            if (!distracted || !nun || nun.blackboard == null) return;
            if (Time.time < distractedUntil)
            {
                nun.blackboard.SetValue("IgnoreSight", true);
                nun.blackboard.SetValue("IgnoreHearing", true);
                nun.blackboard.SetValue("CanAttack", false);
                return;
            }
            distracted = false;
            nun.blackboard.SetValue("IgnoreSight", false);
            nun.blackboard.SetValue("IgnoreHearing", false);
            nun.SendEvent("AlertedSearch"); // Resume hunting nearby.
        }

        private void Discover(Graph graph)
        {
            if (!graph) return;
            bool first = graphs.Add(graph);
            if (graph.blackboard != null)
                foreach (var variable in graph.blackboard.variables.Values)
                    if (variable != null && !variable.hasBinding && !bound.Contains(variable))
                    {
                        var captured = variable;
                        Add(variable, variable.value, value => captured.value = value);
                    }
            foreach (var task in graph.GetAllTasksOfType<NodeCanvas.Framework.Task>())
            {
                tasks.Add(task);
                foreach (FieldInfo field in task.GetType().GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance))
                {
                    if (!typeof(BBParameter).IsAssignableFrom(field.FieldType)) continue;
                    var parameter = field.GetValue(task) as BBParameter;
                    if (parameter != null && !parameter.useBlackboard && !bound.Contains(parameter))
                    {
                        var captured = parameter;
                        Add(parameter, parameter.value, value => captured.value = value);
                    }
                }
            }
            if (first) log("Nun graph discovered: " + graph.name + "; player target bindings=" + bindings.Count);
            foreach (Graph child in graph.GetAllInstancedNestedGraphs()) if (child != graph) DiscoverChild(child);
        }
        private void DiscoverChild(Graph graph) { if (!graphs.Contains(graph)) Discover(graph); }
        private void Add(object key, object original, Action<object> setter)
        {
            var unityObject = original as UnityEngine.Object;
            if (!unityObject || !local) return;
            string kind = null;
            var root = ((Component)local).transform;
            if (unityObject.name == "Vhs Damage Cam Fsm" || unityObject.name == "Motion Blur") return; // Her chase effects: Session.HuntEffect.
            if (unityObject == root) kind = "body";
            else if (unityObject == root.gameObject) kind = "object";
            else if (unityObject is Transform && ((Transform)unityObject).IsChildOf(root)) kind = "head";
            else if (unityObject is Detectable.SightDetectable && ((Component)unityObject).transform == root) kind = "sight";
            else if (unityObject is Collider && ((Component)unityObject).transform == root) kind = "collider";
            if (kind == null) return;
            bound.Add(key); bindings.Add(new Binding { Original = original, Set = setter, Kind = kind });
        }
        internal bool Owns(Graph graph) { return graph && graphs.Contains(graph); }

        // ---------- Tuned for a group ----------
        // With teammates she takes fewer breaks off the map (chopping meat: 30% of her idle picks, not 50%) and lies in
        // wait near whoever she is hunting (one of the three hiding spots closest to them, which with the search spots
        // measured from her quarry means the one off on their own) rather than anywhere on the map. Alone, the game's own.
        private int tunedFor;
        private object hideTypeOriginal;
        private readonly List<KeyValuePair<NodeCanvas.Tasks.Conditions.Probability, KeyValuePair<string,
            float>>> idleOdds = new List<KeyValuePair<NodeCanvas.Tasks.Conditions.Probability, KeyValuePair<string,
            float>>>();

        private void TuneForGroup(int count)
        {
            bool group = count >= 2;
            if (tunedFor != 0 && (tunedFor >= 2) == group) return;
            tunedFor = Mathf.Max(1, count);
            var hide = nun.graph.blackboard != null ? nun.graph.blackboard.GetVariable("Hide Type") : null;
            if (hide != null)
            {
                if (hideTypeOriginal == null) hideTypeOriginal = hide.value;
                hide.value = group ? (object)Humanoid.Agent.EnemyHunter.Tasks.HideType.CloseToPlayer : hideTypeOriginal;
            }
            if (idleOdds.Count == 0)
                foreach (var node in nun.graph.allNodes)
                {
                    if (node == null || node.name != "Set Idle Vars") continue;
                    foreach (var connection in node.outConnections)
                    {
                        var fsmConnection = connection as FSMConnection;
                        var odds = fsmConnection != null ? fsmConnection.condition as NodeCanvas.Tasks.Conditions.Probability : null;
                        if (odds != null && connection.targetNode != null)
                            idleOdds.Add(new KeyValuePair<NodeCanvas.Tasks.Conditions.Probability, KeyValuePair<string,
                                float>>(odds, new KeyValuePair<string, float>(connection.targetNode.name, odds.probability.value)));
                    }
                }
            foreach (var entry in idleOdds)
            {
                float original = entry.Value.Value, odds = entry.Value.Key == "GoTo Chop" ? 0.3f : entry.Value.Key == "GoTo Hide" ? 0.6f : original;
                entry.Key.probability.value = group ? odds : original;
            }
            log("Nun tuned for "
                + (group ? count + " players: lies in wait near her quarry, fewer breaks" : "one player: the game's own")
                + " (idle choices " + idleOdds.Count + ")");
        }

        private void UntuneForGroup()
        {
            if (tunedFor >= 2 && nun) { tunedFor = 1; try { TuneForGroupReset(); } catch (Exception ex) { log("Nun untune: " + ex.Message); } }
            tunedFor = 0; idleOdds.Clear(); hideTypeOriginal = null;
        }
        private void TuneForGroupReset()
        {
            var hide = nun.graph.blackboard != null ? nun.graph.blackboard.GetVariable("Hide Type") : null;
            if (hide != null && hideTypeOriginal != null) hide.value = hideTypeOriginal;
            foreach (var entry in idleOdds) entry.Key.probability.value = entry.Value.Value;
        }

        // ---------- Where she searches and lies in wait ----------
        // Her search spots and hiding spots are ranked by how far each is (along the floor) from "the player", which the
        // game finds once by tag: always the host. These spots are measured from the player she is after instead, so she
        // searches round them and lies in wait near them. (The same tracker also sets how loud her muffled scream is for
        // the host; that part is left alone.)
        private static readonly FieldInfo RegisteredField = AccessTools.Field(typeof(NavDist.DistancePlayerMaster), "registered");
        private static readonly FieldInfo PathLengthField = AccessTools.Field(typeof(NavDist.DistancePlayerMaster), "calculatePathLength");
        private NavDist.DistancePlayerMaster distanceMaster;
        private NavDist.CalculatePathLength pathLength;
        private readonly List<NavDist.DistancePlayerMine> searchPoints = new List<NavDist.DistancePlayerMine>();
        private float nextSearchUpdate;

        private void TakeSearchPoints()
        {
            if (!distanceMaster) distanceMaster = UnityEngine.Object.FindObjectOfType<NavDist.DistancePlayerMaster>();
            if (!distanceMaster || RegisteredField == null) return;
            if (!pathLength && PathLengthField != null) pathLength = PathLengthField.GetValue(distanceMaster) as NavDist.CalculatePathLength;
            var registered = RegisteredField.GetValue(distanceMaster) as List<NavDist.DistancePlayerMine>;
            if (registered == null || !pathLength) return;
            int taken = 0;
            foreach (var point in UnityEngine.Object.FindObjectsOfType<NavDist.NavPt>())
            {
                var mine = point ? point.DistancePlayer : null;
                if (!mine || !registered.Remove(mine)) continue;
                searchPoints.Add(mine); taken++;
            }
            if (taken > 0) log("Nun search and hiding spots now measured from the player she is after (" + searchPoints.Count + ")");
        }

        private void ReturnSearchPoints()
        {
            var registered = distanceMaster && RegisteredField != null ? RegisteredField.GetValue(distanceMaster) as List<NavDist.DistancePlayerMine> : null;
            if (registered != null) foreach (var mine in searchPoints) if (mine && !registered.Contains(mine)) registered.Add(mine);
            searchPoints.Clear(); distanceMaster = null; pathLength = null;
        }

        /// <summary>Every search point measured at once from a spot, so her next search starts from it.</summary>
        private void MeasureSearchPoints(Transform focus)
        {
            if (!focus || !pathLength) return;
            foreach (var mine in searchPoints)
            {
                if (!mine || !mine.TransformToMonitor) continue;
                mine.DistanceVector3 = Vector3.Distance(mine.TransformToMonitor.position, focus.position);
                mine.OutOfRange = mine.DistanceVector3 > mine.MaxDistance;
                mine.DistanceNavMesh = mine.OutOfRange ? mine.MaxDistance : pathLength.Calculate(mine.TransformToMonitor.position, focus.position, 2f);
                mine.UpdateCount = 0;
            }
            nextSearchUpdate = Time.time + 0.1f;
        }

        /// <summary>The game's own measuring (DistancePlayerMaster.update), from the hunted player.</summary>
        private void UpdateSearchPoints(Transform focus)
        {
            if (!focus || !pathLength || searchPoints.Count == 0 || Time.time < nextSearchUpdate) return;
            nextSearchUpdate = Time.time + 0.1f;
            Vector3 at = focus.position;
            foreach (var mine in searchPoints)
            {
                if (!mine || !mine.TransformToMonitor) continue;
                if (mine.UpdateCount >= mine.UpdateRate)
                {
                    mine.UpdateCount = 0;
                    mine.DistanceVector3 = Vector3.Distance(mine.TransformToMonitor.position, at);
                    if (mine.DistanceVector3 > mine.MaxDistance) { mine.OutOfRange = true; mine.DistanceNavMesh = mine.MaxDistance; }
                    else
                    {
                        mine.OutOfRange = false;
                        mine.DistanceNavMesh = pathLength.Calculate(mine.TransformToMonitor.position, at, 2f);
                        mine.Updated();
                    }
                }
                mine.UpdateCount++;
            }
        }
        public void Dispose()
        {
            if (reaction != Reaction.None) try { Unfreeze(); } catch { }
            reaction = Reaction.None; lockedUntil = nextSwitch = 0f; bedPaused = sendSightAlert = false;
            ResetHearing();
            lastFrames = null; lastBodyOf = null; soundSearch = null;
            foreach (var binding in bindings) try { binding.Set(binding.Original); } catch { }
            ReturnSearchPoints();
            UntuneForGroup();
            bindings.Clear(); bound.Clear(); graphs.Clear(); tasks.Clear(); heads.Clear(); weapons.Clear();
            nun = null; local = null; TargetId = 1; distracted = false;
        }
    }
}
