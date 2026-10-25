using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using GrannyClone;
using NodeCanvas.Framework;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.SceneManagement;

namespace NunMassacre.Multiplayer.World
{
    /// <summary>
    /// Host-owned scene state. Construct/Rebuild at sceneLoaded, before Start randomizes/reparents
    /// items. Network methods must run on Unity's main thread. Does not execute received methods,
    /// reconstruct prefabs, or replay game events that could affect the receiving player's health.
    /// </summary>
    public sealed class WorldReplicator : IDisposable
    {
        private const BindingFlags Fields = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly;
        private readonly Func<Transform, bool> exclude;
        private readonly Action<string> log;
        private readonly Dictionary<int, string> instanceIds = new Dictionary<int, string>();
        private readonly Dictionary<string, Component> components = new Dictionary<string, Component>(StringComparer.Ordinal);
        private readonly Dictionary<string, Entry> entries = new Dictionary<string, Entry>(StringComparer.Ordinal);
        private readonly Dictionary<string, string> lastSent = new Dictionary<string, string>(StringComparer.Ordinal);
        private readonly Dictionary<string, AudioClip> clips = new Dictionary<string, AudioClip>(StringComparer.Ordinal);
        private readonly Dictionary<int, SavedBody> bodies = new Dictionary<int, SavedBody>();
        private readonly Dictionary<int, SavedBehaviour> behaviours = new Dictionary<int, SavedBehaviour>();
        private readonly Dictionary<int, SavedGraph> graphs = new Dictionary<int, SavedGraph>();
        private readonly Dictionary<int, SavedAnimator> animators = new Dictionary<int, SavedAnimator>();
        private readonly HashSet<string> warnings = new HashSet<string>(StringComparer.Ordinal);
        private readonly HashSet<int> initializedScenes = new HashSet<int>();
        private readonly Dictionary<int, Transform> playerRoots = new Dictionary<int, Transform>();
        private readonly HashSet<int> aiGraphIds = new HashSet<int>();
        private readonly HashSet<int> enemyAudio = new HashSet<int>();
        private readonly Dictionary<Type, FieldInfo[]> fieldCache = new Dictionary<Type, FieldInfo[]>();
        private readonly Dictionary<int, bool> localNodeCache = new Dictionary<int, bool>();
        private readonly List<Entry> simulationEntries = new List<Entry>();
        private readonly List<Entry> synced = new List<Entry>();
        private bool isHost;
        private bool disposed;
        private bool secondPass = true;
        private string sceneKey = "";
        private int captureTick, sweepCursor;
        private float nextSuppress, nextClipScan;
        internal readonly Dictionary<string, int> DirtyCounts = new Dictionary<string, int>(StringComparer.Ordinal);
        private const int StateBuckets = 10;   // Each object's state is fingerprinted about once a second.
        private const int SweepPerTick = 12;   // Unconditional refresh: repairs any divergence over ~1-2 minutes.
        private const float HotSeconds = 2f;   // Recently changed objects are fingerprinted every tick.
        private const int RestlessTicks = 20;  // After 2 s of continuous small movement, poses go out at 2.5 Hz.

        /// <summary>Suppress an object and its descendants, e.g. a held item owned by another layer.</summary>
        public Func<Component, bool> SkipObject { get; set; }
        public bool Applying { get; private set; }
        public bool IsHost { get { return isHost; } set { SetRole(value); } }
        public int RegistryCount { get { return entries.Count; } }
        public string RegistryDigest { get; private set; }
        public long LastCaptureMilliseconds { get; private set; }
        public int LastSnapshotObjects { get; private set; }

        private sealed class Entry
        {
            public string Id;
            public Transform Transform;
            public Component[] Components;
            public bool Selected;
            public bool Pose;
            public bool Initial;
            public ItemInteractable Item;
            public int Bucket;
            public bool Excluded;        // Local player, cameras, UI or mod objects: never synced.
            public bool Animated;        // Below an Animator, or spun by a cosmetic Rotate script on every peer: not posed by the network.
            public int Restless;         // Consecutive ticks its pose changed (physics that never settles).
            public bool HasBaseline;     // Last state sent to established peers.
            public int StateHash;
            public Transform SentParent;
            public Vector3 SentPosition, SentScale;
            public Quaternion SentRotation;
            public float HotUntil;
            public bool ReportedDestroyed;
        }
        private sealed class SavedBody { public Rigidbody Body; public bool Kinematic; public bool Gravity; public CollisionDetectionMode Detection; }
        private sealed class SavedBehaviour { public Behaviour Behaviour; public bool Enabled; }
        private sealed class SavedGraph { public GraphOwner Graph; public bool Running; public bool Paused; public GraphOwner.EnableAction EnableAction; }
        private sealed class SavedAnimator { public Animator Animator; public bool RootMotion; }
        private enum SimKind { Body, Graph, Driver, Animator }
        private sealed class SimTarget { public Entry Entry; public Component Component; public SimKind Kind; }
        // Guests: the components whose simulation is suppressed, classified once per rebuild so the
        // periodic re-check stays cheap (classifying every component took ~50 ms per pass).
        private readonly List<SimTarget> simTargets = new List<SimTarget>();
        private bool simTargetsDirty = true;

        // Reviewed mutable fields only. Configuration, object references, inventory, health and
        // arbitrary plugin fields are never assigned through the network.
        private static readonly Dictionary<string, string[]> AllowedFields = new Dictionary<string, string[]>(StringComparer.Ordinal)
        {
            { "GrannyClone.Interactable", new[] { "InteractionString", "isMoveInteractable" } },
            { "GrannyClone.StateInteractable", new[] { "stateIndexCurrent", "IsBusy" } },
            { "GrannyClone.MoveInteractable", new[] { "canMove", "ismoving", "moveLimit" } },
            { "GrannyClone.Door", new[] { "isOpen", "isClosing" } },
            { "GrannyClone.CabinetsInteractable", new[] { "isOpen", "isBusy", "canClose" } },
            { "GrannyClone.ItemInteractable", new[] { "CantPickUp" } },
            { "GrannyClone.LightSwitchInteractable", new[] { "isOn" } },
            { "GrannyClone.CombinationLockCylinder", new[] { "Number", "isBusy" } },
            { "GrannyClone.BreakableGlass", new[] { "collisionBreak" } },
            { "GrannyClone.PickLock", new[] { "canExit", "lockpickHealth", "cachedRot" } },
            { "BulbPuzzle", new[] { "state" } },
            { "SafeLock", new[] { "state", "zeroCounter", "cachedCylinderZ" } },
            { "CylinderController", new[] { "CurrentPosition", "CurrentTarget", "Moving" } },
            { "Clock", new[] { "minutes", "hour", "seconds", "msecs" } },
            { "SpikeTrap", new[] { "damagePos" } },
            { "SawPathTrap", new[] { "index" } },
            { "AgentOld.aHealth", new[] { "HealthMeter", "Die" } },
            { "AgentNew.NavigationController", new[] { "CurrentSpeed", "isCrouching", "chasing" } },
            { "Humanoid.Motor2d", new[] { "isCrouching", "isGrounded", "ForwardAmount", "isSliding" } }
        };
        private static readonly HashSet<string> BlackboardNames = new HashSet<string>(StringComparer.Ordinal)
        {
            "Retreat", "PlayerInSight", "HeardSomething", "sightDelay", "sightDelay_Light",
            "Solved", "IsSolved", "Completed", "Complete", "State", "state", "Locked",
            "Unlocked", "isLocked", "isOpen", "isOn", "gasOn", "TrapsSpawned", "BearTrapsSpawned",
            "Nails", "Screws", "Placed Count", "candles", "woodboards", "Tapes", "Collected", "Opened", "isSolved", "solved", "IsBusy", "Poison Gas Status"
        };
        private static readonly HashSet<string> OtherRelevant = new HashSet<string>(StringComparer.Ordinal)
        {
            "DifficultyController", "NunVariables", "FuseboxPuzzle", "PadlockController", "SafeLock",
            "BulbPuzzle", "CylinderController", "Clock", "SpikeTrap", "SawPathTrap", "WallBlocksTrap",
            "PlatformFlipTrap", "NailgunTrap", "PuzzleScenario", "PushPuzzle", "BSBH.NailgunTrap"
        };
        private static readonly HashSet<string> AiDrivers = new HashSet<string>(StringComparer.Ordinal)
        {
            "Humanoid.Motor2d", "AgentNew.NavigationController", "AgentOld.NavigationController",
            "AgentNew.ThirdPersonCharacterCustom", "AgentNew.SightPlayer", "AgentNew.Hearing",
            "AgentNew.TouchSense", "AgentOld.Sight", "AgentOld.Hearing", "AgentOld.Touch", "GrannyClone.RaycastDamage"
        };

        public WorldReplicator(bool isHost, Func<Transform, bool> exclude, Action<string> log)
        {
            this.isHost = isHost;
            this.exclude = exclude;
            this.log = log ?? delegate { };
            RegistryDigest = "";
        }

        public void SetRole(bool host)
        {
            if (isHost == host) return;
            isHost = host;
            if (host) RestoreSimulation();
            else SuppressSimulation();
            lastSent.Clear();
        }

        public void ResetScene()
        {
            RestoreSimulation();
            instanceIds.Clear(); components.Clear(); entries.Clear(); lastSent.Clear(); clips.Clear();
            initializedScenes.Clear(); playerRoots.Clear(); aiGraphIds.Clear(); warnings.Clear(); simulationEntries.Clear(); localNodeCache.Clear();
            simTargets.Clear(); simTargetsDirty = true;
            RegistryDigest = ""; sceneKey = ""; secondPass = true; synced.Clear();
        }

        /// <summary>Preserves each existing ID when an object has been moved or reparented.</summary>
        public void Rebuild()
        {
            if (disposed) return;
            var timer = System.Diagnostics.Stopwatch.StartNew();
            RefreshPlayers();
            var scenes = new List<string>();
            for (int i = 0; i < SceneManager.sceneCount; i++)
            {
                Scene scene = SceneManager.GetSceneAt(i);
                if (!scene.isLoaded) continue;
                string key = string.IsNullOrEmpty(scene.path) ? scene.name : scene.path;
                scenes.Add(key);
                bool initial = initializedScenes.Add(scene.handle);
                GameObject[] roots = scene.GetRootGameObjects();
                var occurrence = new Dictionary<string, int>(StringComparer.Ordinal);
                foreach (GameObject root in roots)
                {
                    if (root.name.StartsWith("NMM_", StringComparison.Ordinal)) continue;
                    int index; occurrence.TryGetValue(root.name, out index); occurrence[root.name] = index + 1;
                    RegisterTree(root.transform, Escape(key) + "/" + Escape(root.name) + "[" + index + "]", initial);
                }
            }
            scenes.Sort(StringComparer.Ordinal); sceneKey = string.Join("|", scenes.ToArray());
            RefreshAiGraphIds();
            foreach (Entry entry in entries.Values)
            {
                if (!entry.Transform || Excluded(entry.Transform)) continue;
                if (entry.Components.Any(IsRelevant)) SelectTree(entry.Transform, true);
            }
            // Persistent references in interaction scripts point at lights, movable doors, etc.
            // Include those transforms, without assigning the references themselves.
            foreach (Entry entry in entries.Values.ToArray())
            {
                if (!entry.Selected || !entry.Transform || Excluded(entry.Transform)) continue;
                foreach (Component component in entry.Components)
                {
                    if (!component || !IsGameScript(component)) continue;
                    foreach (FieldInfo field in InstanceFields(component.GetType()))
                    {
                        if (typeof(GameObject).IsAssignableFrom(field.FieldType) || typeof(Component).IsAssignableFrom(field.FieldType))
                            SelectReference(field.GetValue(component));
                        else if (field.FieldType.IsArray && typeof(UnityEngine.Object).IsAssignableFrom(field.FieldType.GetElementType()))
                        {
                            var references = field.GetValue(component) as IEnumerable;
                            if (references != null) foreach (object reference in references) SelectReference(reference);
                        }
                    }
                }
            }
            string registry = string.Join("\n",
                entries.Values.Where(e => e.Initial && e.Transform && !Excluded(e.Transform)).Select(e => e.Id).OrderBy(x => x, StringComparer.Ordinal).ToArray());
            using (var sha = SHA256.Create())
                RegistryDigest = BitConverter.ToString(sha.ComputeHash(Encoding.UTF8.GetBytes(registry))).Replace("-", "").ToLowerInvariant();
            simulationEntries.Clear();
            // Excluded objects (a scare's ghost child, an ending's Nun) are not driven by the host: they run here.
            simulationEntries.AddRange(entries.Values.Where(e => e.Selected && e.Transform && !Excluded(e.Transform) && e.Components.Any(c => c && (c is Rigidbody || c is GraphOwner || c is NavMeshAgent || c is Animator || AiDrivers.Any(t => IsType(c.GetType(), t))))));
            simTargetsDirty = true;
            synced.Clear();
            int ordinal = 0;
            foreach (Entry entry in entries.Values)
            {
                entry.Excluded = !entry.Transform || Excluded(entry.Transform);
                // Bare bones under an Animator follow the synced animator state; sending each bone
                // would cost far more than it is worth (the Nun alone has hundreds). Fans and similar
                // props spun by Rotate scripts move the same way on every peer.
                entry.Animated = !entry.Excluded
                    && (HasAnimatorAbove(entry.Transform) || entry.Components.Any(c => c is Rotate || c is Rotate2 || c is RotateAround));
                if (entry.Animated && entry.Components.Length == 1) entry.Selected = false;
                if (!entry.Selected || entry.Excluded) continue;
                if (entry.Animated) entry.Pose = false;
                entry.Bucket = ordinal++ % StateBuckets;
                if (!entry.HasBaseline) Remember(entry, StateHash(entry));
                synced.Add(entry);
            }
            CacheClips();
            if (!isHost) SuppressSimulation();
            Perf.Rebuild.Add(timer.ElapsedMilliseconds);
            if (timer.ElapsedMilliseconds > 100)
                log("World discovery: " + entries.Count + " objects (" + synced.Count + " synced) in " + timer.ElapsedMilliseconds + "ms");
        }

        private void RegisterTree(Transform transform, string proposedId, bool initial)
        {
            if (!transform || transform.name.StartsWith("NMM_", StringComparison.Ordinal)) return;
            string id;
            if (!instanceIds.TryGetValue(transform.GetInstanceID(), out id))
            {
                id = proposedId;
                if (components.ContainsKey(id))
                {
                    // Never silently alias a replacement or runtime clone to a dead scene object.
                    int suffix = 1; while (components.ContainsKey(id + "~spawn" + suffix)) suffix++;
                    id += "~spawn" + suffix;
                }
                instanceIds[transform.GetInstanceID()] = id;
                components[id] = transform;
                var entry = new Entry { Id = id, Transform = transform, Components = transform.GetComponents<Component>(),
                    Initial = initial, Item = ItemOf(transform), Bucket = entries.Count % 8 };
                entries[id] = entry;
                if (!initial && !Excluded(transform))
                    Warn("dynamic:" + transform.name,
                        "Runtime object discovered: " + transform.name + ". State can only match a corresponding object on the other peer; prefab creation is not replicated.");
            }
            Entry current = entries[id];
            current.Components = transform.GetComponents<Component>();
            var occurrences = new Dictionary<string, int>(StringComparer.Ordinal);
            foreach (Component component in current.Components)
            {
                if (!component || component is Transform) continue;
                string type = component.GetType().FullName;
                int occurrence; occurrences.TryGetValue(type, out occurrence); occurrences[type] = occurrence + 1;
                if (instanceIds.ContainsKey(component.GetInstanceID())) continue;
                string componentId = id + "::" + type + "#" + occurrence;
                if (components.ContainsKey(componentId)) continue;
                instanceIds[component.GetInstanceID()] = componentId; components[componentId] = component;
            }
            var names = new Dictionary<string, int>(StringComparer.Ordinal);
            for (int i = 0; i < transform.childCount; i++)
            {
                Transform child = transform.GetChild(i);
                if (child.name.StartsWith("NMM_", StringComparison.Ordinal)) continue;
                int occurrence; names.TryGetValue(child.name, out occurrence); names[child.name] = occurrence + 1;
                RegisterTree(child, id + "/" + Escape(child.name) + "[" + occurrence + "]", initial);
            }
        }

        private void SelectReference(object value)
        {
            var gameObject = value as GameObject;
            var component = value as Component;
            Transform transform = gameObject ? gameObject.transform : component ? component.transform : null;
            if (transform && transform.gameObject.scene.IsValid()) SelectTree(transform, true);
        }

        private void SelectTree(Transform root, bool pose)
        {
            if (!root || Excluded(root)) return;
            string id; Entry entry;
            if (!instanceIds.TryGetValue(root.GetInstanceID(), out id) || !entries.TryGetValue(id, out entry)) return;
            if (entry.Selected && (!pose || entry.Pose)) return;
            entry.Selected = true; entry.Pose |= pose;
            for (Transform parent = root.parent; parent; parent = parent.parent)
            {
                if (Excluded(parent)) break;
                string parentId; Entry parentEntry;
                if (instanceIds.TryGetValue(parent.GetInstanceID(), out parentId) && entries.TryGetValue(parentId, out parentEntry))
                    parentEntry.Selected = true;
            }
            for (int i = 0; i < root.childCount; i++) SelectTree(root.GetChild(i), pose);
        }

        /// <summary>Registers a runtime-created object under a host-assigned ID (root pose and state only).</summary>
        public void RegisterDynamic(Transform root, string id)
        {
            if (disposed || !root || string.IsNullOrEmpty(id) || components.ContainsKey(id)) return;
            instanceIds[root.GetInstanceID()] = id;
            components[id] = root;
            var entry = new Entry { Id = id, Transform = root, Components = root.GetComponents<Component>(), Selected = true,
                Pose = true, Bucket = synced.Count % StateBuckets, Item = ItemOf(root) };
            var occurrences = new Dictionary<string, int>(StringComparer.Ordinal);
            foreach (Component component in entry.Components)
            {
                if (!component || component is Transform) continue;
                string type = component.GetType().FullName;
                int occurrence; occurrences.TryGetValue(type, out occurrence); occurrences[type] = occurrence + 1;
                string componentId = id + "::" + type + "#" + occurrence;
                instanceIds[component.GetInstanceID()] = componentId; components[componentId] = component;
            }
            entries[id] = entry;
            Remember(entry, StateHash(entry));
            synced.Add(entry);
            // Peers follow the host's physics for it.
            if (!isHost) foreach (var body in root.GetComponentsInChildren<Rigidbody>(true)) { body.isKinematic = true; body.useGravity = false; }
        }

        /// <summary>Automated tests: this peer's view of the progression-relevant world, one line per
        /// object (activity, parent, rounded position, replicated fields and blackboard values) plus every
        /// state machine's current state, for comparing peers.</summary>
        internal string Fingerprint()
        {
            var lines = new List<string>();
            foreach (Entry entry in synced)
            {
                Transform t = entry.Transform;
                if (!t || entry.Excluded || (entry.Item && Skipped(entry.Transform))) continue; // Held items are hidden for others by design.
                var line = new StringBuilder(entry.Id);
                line.Append(" | active=").Append(t.gameObject.activeInHierarchy ? 1 : 0);
                if (entry.Item || t.GetComponent<Rigidbody>() || t.GetComponent<Interactable>())
                {
                    Vector3 p = t.position;
                    line.Append(" parent=").Append(t.parent ? t.parent.name : "-");
                    line.Append(" pos=").Append(Mathf.RoundToInt(p.x * 5f)).Append(',').Append(Mathf.RoundToInt(p.y * 5f)).Append(',').Append(Mathf.RoundToInt(p.z * 5f));
                    Vector3 r = t.eulerAngles; // Doors, lids and secret walls swing; compared with a tolerance.
                    line.Append(" rot=").Append(Mathf.RoundToInt(r.x)).Append(',').Append(Mathf.RoundToInt(r.y)).Append(',').Append(Mathf.RoundToInt(r.z));
                    // Free physics (ragdolls, knocked props) and animated bodies settle differently per peer.
                    var body = t.GetComponentInParent<Rigidbody>();
                    var animator = t.GetComponentInParent<Animator>();
                    if ((body && !body.isKinematic) || (animator && animator.isActiveAndEnabled && animator.runtimeAnimatorController)) line.Append(" loose=1");
                }
                foreach (Component component in entry.Components)
                {
                    if (!component) continue;
                    var collider = component as Collider;
                    if (collider) { line.Append(" col=").Append(collider.enabled ? 1 : 0); continue; }
                    var renderer = component as Renderer;
                    if (renderer) { line.Append(" ren=").Append(renderer.enabled ? 1 : 0); continue; }
                    var light = component as Light;
                    if (light) { line.Append(" light=").Append(light.enabled ? 1 : 0); continue; }
                    var blackboard = component as Blackboard;
                    if (blackboard)
                    {
                        var ai = blackboard.GetComponent<GraphOwner>();
                        if (ai && aiGraphIds.Contains(ai.GetInstanceID())) continue; // The Nun's brain runs on the host only.
                        foreach (var pair in blackboard.variables.OrderBy(v => v.Key, StringComparer.Ordinal))
                            if (pair.Value != null && pair.Value.value != null && Primitive(pair.Value.varType))
                                line.Append(" bb.").Append(pair.Key.Replace(' ', '_')).Append('=').Append(Encode(pair.Value.value, pair.Value.varType));
                        continue;
                    }
                    foreach (FieldInfo field in GetAllowedFields(component.GetType()))
                    {
                        object value = field.GetValue(component);
                        if (value != null && Primitive(field.FieldType)) line.Append(' ').Append(field.Name).Append('=').Append(Encode(value, field.FieldType));
                    }
                    var state = component as StateInteractable;
                    if (state)
                    {
                        var used = ReadField(state, "ItemUsed") as UnityEngine.Object;
                        line.Append(" used=").Append(used ? used.name : "-");
                    }
                    var item = component as ItemInteractable;
                    if (item) line.Append(" placed=").Append(item.PlacedInPuzzle ? item.PlacedInPuzzle.name : "-");
                }
                lines.Add(line.ToString());
            }
            foreach (var owner in Resources.FindObjectsOfTypeAll<NodeCanvas.StateMachines.FSMOwner>())
            {
                if (!owner || !owner.gameObject.scene.IsValid()) continue;
                string id = IdFor(owner.transform);
                if (string.IsNullOrEmpty(id) || aiGraphIds.Contains(owner.GetInstanceID())) continue;
                ItemInteractable heldBy = null; // GetComponentInParent skips the inactive (hidden, held) items.
                for (Transform cursor = owner.transform; cursor && !heldBy; cursor = cursor.parent) heldBy = cursor.GetComponent<ItemInteractable>();
                if (heldBy && Skipped(heldBy.transform)) continue;
                var fsm = owner.graph as NodeCanvas.StateMachines.FSM;
                string current = fsm != null && fsm.currentState != null ? fsm.allNodes.IndexOf(fsm.currentState) + ":" + fsm.currentState.name : "-";
                lines.Add("FSM " + id + " | running=" + (owner.isRunning ? 1 : 0) + " paused=" + (owner.isPaused ? 1 : 0)
                    + " state=" + current.Replace(' ', '_'));
            }
            lines.Sort(StringComparer.Ordinal);
            return string.Join("\n", lines.ToArray());
        }

        /// <summary>Host: the step each puzzle machine has reached, for a player joining mid-level ("id|state" lines).
        /// Machines still at their first step, the Nun's AI and each player's own sequences are left out.</summary>
        internal string CaptureGraphStates()
        {
            var lines = new List<string>();
            foreach (var owner in Resources.FindObjectsOfTypeAll<NodeCanvas.StateMachines.FSMOwner>())
            {
                if (!owner || !owner.gameObject.scene.IsValid() || !owner.isRunning || aiGraphIds.Contains(owner.GetInstanceID())) continue;
                string id = IdFor(owner.transform);
                if (string.IsNullOrEmpty(id) || Excluded(owner.transform)) continue;
                var fsm = owner.graph as NodeCanvas.StateMachines.FSM;
                if (fsm == null || fsm.currentState == null || fsm.currentState == fsm.primeNode) continue;
                int index = fsm.allNodes.IndexOf(fsm.currentState);
                if (index >= 0) lines.Add(id + "|" + index);
            }
            return string.Join("\n", lines.ToArray());
        }

        /// <summary>A player joining mid-level: each puzzle machine that is behind the host's jumps to the host's step
        /// (its actions run, so screws drop, covers open...). Returns how many moved.</summary>
        internal int ApplyGraphStates(string text, Action<string> report)
        {
            int moved = 0;
            foreach (string line in (text ?? "").Split('\n'))
            {
                int bar = line.LastIndexOf('|'), index;
                if (bar <= 0 || !int.TryParse(line.Substring(bar + 1), out index)) continue;
                Transform target = FindTransform(line.Substring(0, bar));
                var owner = target ? target.GetComponent<NodeCanvas.StateMachines.FSMOwner>() : null;
                var fsm = owner ? owner.graph as NodeCanvas.StateMachines.FSM : null;
                if (fsm == null || !owner.isRunning || index < 0 || index >= fsm.allNodes.Count) continue;
                var state = fsm.allNodes[index] as NodeCanvas.StateMachines.FSMState;
                if (state == null || fsm.currentState == state) continue;
                try { fsm.EnterState(state); moved++; }
                catch (Exception error) { report("Could not catch up " + line + ": " + error.Message); }
            }
            return moved;
        }

        public string IdFor(Component component)
        {
            if (!component) return null;
            string id; return instanceIds.TryGetValue(component.GetInstanceID(), out id) ? id : null;
        }

        public Component Find(string id)
        {
            if (string.IsNullOrEmpty(id)) return null;
            Component component; return components.TryGetValue(id, out component) && component ? component : null;
        }

        public Transform FindTransform(string id)
        {
            Component component = Find(id); return component ? component.transform : null;
        }

        public string Capture(bool full)
        {
            var snapshot = CaptureSnapshot(full);
            if (snapshot == null) return null;
            long serializeStart = Perf.Begin();
            string json = Serialize(snapshot);
            if (Perf.Enabled)
                Perf.Serialize.Add((System.Diagnostics.Stopwatch.GetTimestamp() - serializeStart) * 1000.0 / System.Diagnostics.Stopwatch.Frequency);
            return json;
        }

        /// <summary>Thread-safe: snapshots are plain data once captured.</summary>
        internal static string Serialize(WorldSnapshot snapshot) { return JsonCodec.ToJson(snapshot); }
        /// <summary>Thread-safe parse (throws on invalid JSON).</summary>
        internal static WorldSnapshot Parse(string json) { return JsonCodec.FromJson<WorldSnapshot>(json); }

        /// <summary>The changed (or, when full, all) synced objects; null when a delta has nothing.</summary>
        internal WorldSnapshot CaptureSnapshot(bool full)
        {
            if (disposed || !isHost) return null;
            var timer = System.Diagnostics.Stopwatch.StartNew();
            if (entries.Count == 0) Rebuild();
            var changed = new List<ObjectSnapshot>();
            if (full)
            {
                // A full snapshot may be unicast to a joining peer. It must not consume changes
                // which established peers still need from the next broadcast delta.
                foreach (Entry entry in synced)
                {
                    if (entry.Transform && entry.Item && Skipped(entry.Transform)) continue;
                    ObjectSnapshot snapshot = TryCapture(entry);
                    if (snapshot != null) changed.Add(snapshot);
                }
            }
            else
            {
                // Deltas: movement is detected through Transform.hasChanged (cheap), other state by a
                // fingerprint checked in rotating buckets, every tick while an object is changing.
                float now = Time.unscaledTime;
                int bucket = captureTick++ % StateBuckets;
                int sweepFrom = sweepCursor, sweepTo = sweepCursor + SweepPerTick;
                sweepCursor = synced.Count == 0 ? 0 : sweepTo % synced.Count;
                for (int i = 0; i < synced.Count; i++)
                {
                    Entry entry = synced[i];
                    Transform transform = entry.Transform;
                    if (!transform)
                    {
                        if (!entry.ReportedDestroyed) { entry.ReportedDestroyed = true; changed.Add(new ObjectSnapshot { id = entry.Id, destroyed = true }); }
                        continue;
                    }
                    if (entry.Item && Skipped(transform)) continue;
                    bool dirty = false;
                    if (transform.hasChanged)
                    {
                        transform.hasChanged = false;
                        if (!entry.Animated && PoseChanged(entry))
                        {
                            // A swinging chain or a body part that never settles would go out every tick.
                            if (entry.Restless < RestlessTicks) entry.Restless++;
                            // Re-checked next tick.
                            if (entry.Restless >= RestlessTicks && (captureTick & 3) != 0 && !MovedFar(entry)) transform.hasChanged = true;
                            else { dirty = true; entry.Pose = true; }
                        }
                        else entry.Restless = 0;
                    }
                    else entry.Restless = 0;
                    int hash = 0; bool hashed = false;
                    if (!dirty && (entry.Bucket == bucket || now < entry.HotUntil))
                    {
                        hash = StateHash(entry); hashed = true;
                        dirty = hash != entry.StateHash;
                    }
                    bool sweep = synced.Count > 0 && ((i >= sweepFrom && i < sweepTo) || (sweepTo > synced.Count && i < sweepTo - synced.Count));
                    if (!dirty && !sweep) continue;
                    ObjectSnapshot snapshot = TryCapture(entry);
                    if (snapshot == null) continue;
                    changed.Add(snapshot);
                    Remember(entry, hashed ? hash : StateHash(entry));
                    if (dirty)
                    {
                        entry.HotUntil = now + HotSeconds;
                        int count; DirtyCounts.TryGetValue(entry.Id, out count); DirtyCounts[entry.Id] = count + 1;
                    }
                }
            }
            LastSnapshotObjects = changed.Count;
            Perf.Scan.Add(timer.Elapsed.TotalMilliseconds);
            LastCaptureMilliseconds = timer.ElapsedMilliseconds;
            if (!full && changed.Count == 0) return null;
            return new WorldSnapshot { scene = sceneKey, full = full, registryCount = RegistryCount,
                registryDigest = full ? RegistryDigest : null, objects = changed.ToArray() };
        }

        private ObjectSnapshot TryCapture(Entry entry)
        {
            try { return CaptureObject(entry); }
            catch (Exception ex) { Warn("capture:" + entry.Id, "Cannot capture " + entry.Id + ": " + ex.Message); return null; }
        }

        /// <summary>Under an animator that animates (it has a controller): its bones follow the synced animator state. An
        /// animator with none moves nothing: bones under it (a ragdoll corpse hanging on the PDM cross) move by physics,
        /// on the host, and are sent like any other moving object.</summary>
        private static bool HasAnimatorAbove(Transform transform)
        {
            for (Transform cursor = transform.parent; cursor; cursor = cursor.parent)
            {
                var animator = cursor.GetComponent<Animator>();
                if (animator && animator.runtimeAnimatorController) return true;
            }
            return false;
        }

        private static bool PoseChanged(Entry entry)
        {
            Transform t = entry.Transform;
            return t.parent != entry.SentParent || (t.localPosition - entry.SentPosition).sqrMagnitude > 1e-6f
                || Quaternion.Angle(t.localRotation, entry.SentRotation) > 0.05f || (t.localScale - entry.SentScale).sqrMagnitude > 1e-6f;
        }

        private static bool MovedFar(Entry entry)
        {
            Transform t = entry.Transform;
            return t.parent != entry.SentParent || (t.localPosition - entry.SentPosition).sqrMagnitude > 0.0625f
                || Quaternion.Angle(t.localRotation, entry.SentRotation) > 20f;
        }

        private static void Remember(Entry entry, int hash)
        {
            Transform t = entry.Transform;
            if (!t) return;
            entry.SentParent = t.parent; entry.SentPosition = t.localPosition; entry.SentRotation = t.localRotation; entry.SentScale = t.localScale;
            entry.StateHash = hash; entry.HasBaseline = true;
            t.hasChanged = false;
        }

        // Cheap fingerprint of the replicated, non-pose state. Continuously varying cosmetics
        // (light flicker, audio playback time/volume, animation time) are left out: they are still
        // captured when an object is sent, but do not keep it permanently dirty.
        private int StateHash(Entry entry)
        {
            unchecked
            {
                Transform t = entry.Transform;
                int h = t.gameObject.activeSelf ? 17 : 19;
                h = h * 31 + (t.parent ? t.parent.GetInstanceID() : 0);
                foreach (Component component in entry.Components)
                {
                    if (!component) { h = h * 31 + 7; continue; }
                    var collider = component as Collider;
                    if (collider) { h = h * 31 + (collider.enabled ? 3 : 5) + (collider.isTrigger ? 11 : 13); continue; }
                    var renderer = component as Renderer;
                    if (renderer) { h = h * 31 + (renderer.enabled ? 3 : 5); continue; }
                    var light = component as Light;
                    if (light) { h = h * 31 + (light.enabled ? 3 : 5) + light.color.GetHashCode(); continue; }
                    var animator = component as Animator;
                    if (animator) { h = h * 31 + AnimatorHash(animator); continue; }
                    var audio = component as AudioSource;
                    if (audio)
                    {
                        if (Synced(audio)) h = h * 31 + (audio.enabled ? 3 : 5) + (audio.isPlaying ? 11 : 13) + (audio.clip ? audio.clip.GetInstanceID() : 0);
                        // Chase music fades rather than stops, so its volume matters.
                        if (enemyAudio.Contains(audio.GetInstanceID())) h = h * 31 + Mathf.RoundToInt(audio.volume * 10f);
                        continue;
                    }
                    var particle = component as ParticleSystem;
                    if (particle) { h = h * 31 + (particle.isPlaying ? 3 : 5); continue; }
                    var blackboard = component as Blackboard;
                    if (blackboard)
                    {
                        foreach (var pair in blackboard.variables)
                            if (pair.Value != null && BlackboardNames.Contains(pair.Key) && pair.Value.value != null)
                                h = h * 31 + pair.Value.value.GetHashCode();
                        continue;
                    }
                    if (component is Transform) continue;
                    FieldInfo[] fields = GetAllowedFields(component.GetType());
                    if (fields.Length == 0) continue;
                    var behaviour = component as Behaviour;
                    h = h * 31 + (!behaviour || behaviour.enabled ? 3 : 5);
                    foreach (FieldInfo field in fields) { object value = field.GetValue(component); h = h * 31 + (value == null ? 0 : value.GetHashCode()); }
                }
                return h;
            }
        }

        private static int AnimatorHash(Animator animator)
        {
            unchecked
            {
                if (!animator.runtimeAnimatorController) return 1;
                int h = animator.enabled ? 3 : 5;
                h = h * 31 + Mathf.RoundToInt(animator.speed * 10f);
                for (int i = 0; i < animator.layerCount; i++)
                {
                    AnimatorStateInfo state = animator.IsInTransition(i) ? animator.GetNextAnimatorStateInfo(i) : animator.GetCurrentAnimatorStateInfo(i);
                    h = h * 31 + state.fullPathHash;
                    h = h * 31 + Mathf.RoundToInt(animator.GetLayerWeight(i) * 10f);
                }
                foreach (AnimatorControllerParameter parameter in animator.parameters)
                {
                    switch (parameter.type)
                    {
                        case AnimatorControllerParameterType.Float: h = h * 31 + Mathf.RoundToInt(animator.GetFloat(parameter.nameHash) * 10f); break;
                        case AnimatorControllerParameterType.Int: h = h * 31 + animator.GetInteger(parameter.nameHash); break;
                        case AnimatorControllerParameterType.Bool: h = h * 31 + (animator.GetBool(parameter.nameHash) ? 3 : 5); break;
                    }
                }
                return h;
            }
        }

        private ObjectSnapshot CaptureObject(Entry entry)
        {
            var state = new ObjectSnapshot { id = entry.Id, destroyed = !entry.Transform };
            if (!entry.Transform) return state;
            Transform transform = entry.Transform;
            state.active = transform.gameObject.activeSelf; state.pose = entry.Pose;
            if (entry.Pose)
            {
                state.parent = IdFor(transform.parent);
                state.position = Round(transform.position); state.rotation = Round(transform.rotation); state.scale = Round(transform.localScale);
            }
            var values = new List<ComponentSnapshot>();
            foreach (Component component in entry.Components)
            {
                if (!component || component is Transform || Skipped(component)) continue;
                ComponentSnapshot value = CaptureComponent(component);
                if (value != null) { value.id = IdFor(component); if (value.id != null) values.Add(value); }
            }
            state.components = values.ToArray(); return state;
        }

        private ComponentSnapshot CaptureComponent(Component component)
        {
            var collider = component as Collider;
            if (collider) return new ComponentSnapshot { kind = "collider", enabled = collider.enabled, flag = collider.isTrigger };
            var renderer = component as Renderer;
            if (renderer) return new ComponentSnapshot { kind = "renderer", enabled = renderer.enabled };
            var light = component as Light;
            if (light) return new ComponentSnapshot { kind = "light", enabled = light.enabled, number = Round(light.intensity), color = light.color };
            var animator = component as Animator;
            if (animator) return CaptureAnimator(animator);
            var audio = component as AudioSource;
            if (audio && Synced(audio))
                return new ComponentSnapshot { kind = "audio", enabled = audio.enabled, flag = audio.isPlaying,
                    integer = audio.loop ? 1 : 0, text = ClipId(audio.clip), number = Round(audio.volume),
                    number2 = Round(audio.pitch), number3 = audio.isPlaying ? Mathf.Round(audio.time * 5f) / 5f : 0f };
            var particle = component as ParticleSystem;
            if (particle) return new ComponentSnapshot { kind = "particle", flag = particle.isPlaying };
            var blackboard = component as Blackboard;
            if (blackboard) return CaptureBlackboard(blackboard);
            FieldInfo[] fields = GetAllowedFields(component.GetType());
            if (fields.Length == 0) return null;
            var fieldValues = new List<ValueSnapshot>();
            foreach (FieldInfo field in fields)
                fieldValues.Add(new ValueSnapshot { name = field.DeclaringType.FullName + ":" + field.Name,
                    value = Encode(field.GetValue(component), field.FieldType) });
            var behaviour = component as Behaviour;
            return new ComponentSnapshot { kind = "fields", enabled = !behaviour || behaviour.enabled, values = fieldValues.ToArray() };
        }

        private ComponentSnapshot CaptureAnimator(Animator animator)
        {
            if (!animator.runtimeAnimatorController) return null;
            var layers = new AnimatorLayerSnapshot[animator.layerCount];
            for (int i = 0; i < layers.Length; i++)
            {
                AnimatorStateInfo state = animator.IsInTransition(i) ? animator.GetNextAnimatorStateInfo(i) : animator.GetCurrentAnimatorStateInfo(i);
                layers[i] = new AnimatorLayerSnapshot { state = state.fullPathHash,
                    time = Mathf.Round(state.normalizedTime * 20f) / 20f, weight = Round(animator.GetLayerWeight(i)) };
            }
            var parameters = new List<AnimatorParameterSnapshot>();
            foreach (AnimatorControllerParameter parameter in animator.parameters)
            {
                var value = new AnimatorParameterSnapshot { hash = parameter.nameHash, type = (int)parameter.type };
                switch (parameter.type)
                {
                    case AnimatorControllerParameterType.Float: value.number = Round(animator.GetFloat(parameter.nameHash)); break;
                    case AnimatorControllerParameterType.Int: value.integer = animator.GetInteger(parameter.nameHash); break;
                    case AnimatorControllerParameterType.Bool: value.flag = animator.GetBool(parameter.nameHash); break;
                    default: continue; // Transient triggers are represented by the resulting state, not retriggered.
                }
                parameters.Add(value);
            }
            return new ComponentSnapshot { kind = "animator", enabled = animator.enabled, number = Round(animator.speed),
                layers = layers, parameters = parameters.ToArray() };
        }

        private ComponentSnapshot CaptureBlackboard(Blackboard blackboard)
        {
            var values = new List<ValueSnapshot>();
            foreach (var pair in blackboard.variables)
            {
                Variable variable = pair.Value;
                if (variable == null || variable.hasBinding || !BlackboardNames.Contains(pair.Key) || !Primitive(variable.varType)) continue;
                values.Add(new ValueSnapshot { name = pair.Key, value = Encode(variable.value, variable.varType) });
            }
            return values.Count == 0 ? null : new ComponentSnapshot { kind = "blackboard", values = values.ToArray() };
        }

        public void Apply(string json)
        {
            if (disposed || isHost || string.IsNullOrEmpty(json)) return;
            WorldSnapshot snapshot;
            long parseStart = Perf.Begin();
            try { snapshot = Parse(json); }
            catch (Exception ex) { Warn("invalid-json", "Invalid world snapshot: " + ex.Message); return; }
            if (Perf.Enabled) Perf.Parse.Add((Perf.Begin() - parseStart) * 1000.0 / System.Diagnostics.Stopwatch.Frequency);
            Apply(snapshot);
        }

        internal void Apply(WorldSnapshot snapshot)
        {
            if (disposed || isHost) return;
            long applyStart = Perf.Begin();
            if (snapshot == null || snapshot.version != 1 || snapshot.objects == null || snapshot.objects.Length > 100000) return;
            if (snapshot.scene != sceneKey)
            {
                Warn("scene:" + snapshot.scene, "World snapshot belongs to another scene; waiting for the matching scene.");
                return;
            }
            if (snapshot.full && snapshot.registryDigest != RegistryDigest)
                Warn("registry:" + snapshot.registryDigest,
                    "Scene registry differs from host (host " + snapshot.registryCount + ", local " + RegistryCount + "). Missing objects will be reported; dynamic prefab creation is not supported.");
            Applying = true;
            try
            {
                var matched = new List<KeyValuePair<Entry, ObjectSnapshot>>();
                foreach (ObjectSnapshot state in snapshot.objects)
                {
                    if (state == null) continue;
                    Entry entry;
                    if (!entries.TryGetValue(state.id ?? "", out entry))
                    {
                        Warn("missing:" + state.id, "No local scene object for host ID " + state.id);
                        continue;
                    }
                    Transform transform = entry.Transform;
                    if (!transform || entry.Excluded || (entry.Item && Skipped(transform))) continue;
                    if (state.destroyed) { transform.gameObject.SetActive(false); continue; }
                    matched.Add(new KeyValuePair<Entry, ObjectSnapshot>(entry, state));
                    if (state.pose)
                    {
                        Transform parent = FindTransform(state.parent);
                        if (parent && (Excluded(parent) || Skipped(parent) || parent == transform || parent.IsChildOf(transform))) parent = transform.parent;
                        if ((string.IsNullOrEmpty(state.parent) || parent) && parent != transform.parent) transform.SetParent(parent, true);
                    }
                    if (transform.gameObject.activeSelf != state.active) transform.gameObject.SetActive(state.active);
                }
                // Parent poses must precede child world poses, including after puzzle reparenting.
                foreach (var pair in matched.OrderBy(pair => Depth(pair.Key.Transform)))
                {
                    Transform transform = pair.Key.Transform; ObjectSnapshot state = pair.Value;
                    if (transform && state.pose && Finite(state.position) && Finite(state.rotation) && Finite(state.scale))
                    {
                        transform.SetPositionAndRotation(state.position, state.rotation); transform.localScale = state.scale;
                    }
                }
                foreach (ObjectSnapshot state in snapshot.objects)
                {
                    if (state == null || state.destroyed || state.components == null || state.components.Length > 256) continue;
                    Entry entry;
                    if (!entries.TryGetValue(state.id ?? "", out entry) || !entry.Transform || entry.Excluded || (entry.Item && Skipped(entry.Transform)))
                        continue;
                    foreach (ComponentSnapshot value in state.components)
                    {
                        if (value == null) continue;
                        Component component = Find(value.id);
                        if (!component || component.transform != entry.Transform || Skipped(component)) continue;
                        try { ApplyComponent(component, value); }
                        catch (Exception ex) { Warn("apply:" + value.id, "Cannot apply " + value.id + ": " + ex.Message); }
                    }
                }
            }
            finally
            {
                Applying = false;
                if (Perf.Enabled)
                    Perf.ApplyObjects.Add((System.Diagnostics.Stopwatch.GetTimestamp() - applyStart) * 1000.0 / System.Diagnostics.Stopwatch.Frequency);
            }
        }

        private void ApplyComponent(Component component, ComponentSnapshot state)
        {
            var collider = component as Collider;
            if (state.kind == "collider" && collider) { collider.enabled = state.enabled; collider.isTrigger = state.flag; return; }
            var renderer = component as Renderer;
            if (state.kind == "renderer" && renderer) { renderer.enabled = state.enabled; return; }
            var light = component as Light;
            if (state.kind == "light" && light)
            {
                light.enabled = state.enabled;
                if (Finite(state.number)) light.intensity = Mathf.Clamp(state.number, 0, 100);
                light.color = state.color;
                return;
            }
            var animator = component as Animator;
            if (state.kind == "animator" && animator) { ApplyAnimator(animator, state); return; }
            var audio = component as AudioSource;
            if (state.kind == "audio" && audio && Synced(audio)) { ApplyAudio(audio, state); return; }
            var particle = component as ParticleSystem;
            if (state.kind == "particle" && particle)
            {
                if (state.flag && !particle.isPlaying) particle.Play(false);
                else if (!state.flag && particle.isPlaying) particle.Stop(false);
                return;
            }
            var blackboard = component as Blackboard;
            if (state.kind == "blackboard" && blackboard)
            {
                if (state.values == null || state.values.Length > 256) return;
                foreach (ValueSnapshot value in state.values)
                {
                    if (value == null || !BlackboardNames.Contains(value.name ?? "")) continue;
                    Variable variable;
                    if (!blackboard.variables.TryGetValue(value.name, out variable) || variable == null || variable.hasBinding || !Primitive(variable.varType))
                        continue;
                    object decoded; if (TryDecode(value.value, variable.varType, out decoded)) variable.value = decoded;
                }
                return;
            }
            if (state.kind != "fields" || state.values == null || state.values.Length > 128) return;
            FieldInfo[] fields = GetAllowedFields(component.GetType());
            foreach (ValueSnapshot value in state.values)
            {
                if (value == null) continue;
                FieldInfo field = fields.FirstOrDefault(f => f.DeclaringType.FullName + ":" + f.Name == value.name);
                object decoded;
                if (field == null || !TryDecode(value.value, field.FieldType, out decoded)) continue;
                if (field.Name == "stateIndexCurrent" && component is StateInteractable)
                {
                    int index = (int)decoded;
                    FieldInfo statesField = FindField(component.GetType(), "states");
                    Array states = statesField == null ? null : statesField.GetValue(component) as Array;
                    if (states != null && index >= 0 && index < states.Length
                        && (!Equals(field.GetValue(component), decoded) || ReadField(component, "stateCurrent") == null))
                        ((StateInteractable)component).StateSetNoEvent(index);
                }
                else field.SetValue(component, decoded);
            }
            // The bulb puzzle swaps materials; this visual update does not replay its solved event.
            if (component.GetType().FullName == "BulbPuzzle") ApplyBulbVisuals(component);
            // Deliberately do not assign arbitrary Behaviour.enabled: many OnEnable callbacks reset
            // puzzles or modify player/camera state. World visuals have explicit handlers above.
        }

        private static void ApplyBulbVisuals(Component puzzle)
        {
            var bulbs = ReadField(puzzle, "bulbs") as Renderer[];
            var materials = ReadField(puzzle, "bulbMats") as Material[];
            var off = ReadField(puzzle, "offMat") as Material;
            object stateValue = ReadField(puzzle, "state");
            if (bulbs == null || materials == null || !off || !(stateValue is int)) return;
            int state = (int)stateValue;
            for (int i = 0; i < bulbs.Length && i < materials.Length; i++) if (bulbs[i]) bulbs[i].sharedMaterial = i < state ? off : materials[i];
        }

        private void ApplyAnimator(Animator animator, ComponentSnapshot state)
        {
            if (!animator.runtimeAnimatorController) return;
            animator.enabled = state.enabled;
            if (Finite(state.number)) animator.speed = Mathf.Clamp(state.number, 0, 10);
            if (state.parameters != null && state.parameters.Length <= 256)
            {
                AnimatorControllerParameter[] known = animator.parameters;
                foreach (AnimatorParameterSnapshot parameter in state.parameters)
                {
                    if (parameter == null || !known.Any(p => p.nameHash == parameter.hash && (int)p.type == parameter.type)) continue;
                    switch ((AnimatorControllerParameterType)parameter.type)
                    {
                        case AnimatorControllerParameterType.Float: if (Finite(parameter.number)) animator.SetFloat(parameter.hash, parameter.number); break;
                        case AnimatorControllerParameterType.Int: animator.SetInteger(parameter.hash, parameter.integer); break;
                        case AnimatorControllerParameterType.Bool: animator.SetBool(parameter.hash, parameter.flag); break;
                    }
                }
            }
            if (state.layers == null) return;
            for (int i = 0; i < state.layers.Length && i < animator.layerCount; i++)
            {
                AnimatorLayerSnapshot layer = state.layers[i];
                if (layer == null || !Finite(layer.time) || !Finite(layer.weight) || !animator.HasState(i, layer.state)) continue;
                animator.SetLayerWeight(i, Mathf.Clamp01(layer.weight));
                AnimatorStateInfo current = animator.GetCurrentAnimatorStateInfo(i);
                if (current.fullPathHash != layer.state || Mathf.Abs(current.normalizedTime - layer.time) > 0.25f) animator.Play(layer.state, i, layer.time);
            }
        }

        private void ApplyAudio(AudioSource source, ComponentSnapshot state)
        {
            source.enabled = state.enabled; source.loop = state.integer != 0;
            if (Finite(state.number)) source.volume = Mathf.Clamp01(state.number);
            if (Finite(state.number2)) source.pitch = Mathf.Clamp(state.number2, -3, 3);
            string current = ClipId(source.clip);
            if (current != state.text)
            {
                AudioClip clip;
                if (string.IsNullOrEmpty(state.text)) { source.Stop(); source.clip = null; return; }
                if ((!clips.TryGetValue(state.text, out clip) || !clip) && Time.unscaledTime >= nextClipScan)
                {
                    nextClipScan = Time.unscaledTime + 10f;
                    CacheClips();
                    clips.TryGetValue(state.text, out clip);
                }
                if (!clip) { Warn("audio:" + state.text, "Audio clip not loaded locally: " + state.text); return; }
                source.clip = clip;
            }
            if (!state.flag) { if (source.isPlaying) source.Stop(); return; }
            if (!source.isActiveAndEnabled || !source.clip) return;
            if (!source.isPlaying) source.Play();
            if (Finite(state.number3) && Mathf.Abs(source.time - state.number3) > 0.4f)
                source.time = Mathf.Clamp(state.number3, 0, Mathf.Max(0, source.clip.length - 0.01f));
        }

        public void Tick()
        {
            if (disposed) return;
            // Constructed at sceneLoaded (before Start). One more pass on the next frame picks up
            // objects created or reparented by Start, while the load hitch still hides the cost.
            if (secondPass) { secondPass = false; Rebuild(); }
            if (!isHost && Time.unscaledTime >= nextSuppress) { nextSuppress = Time.unscaledTime + 0.5f; SuppressSimulation(); }
        }

        private void RefreshPlayers()
        {
            playerRoots.Clear();
            foreach (pReferences player in Resources.FindObjectsOfTypeAll<pReferences>())
                if (player && ((Component)player).gameObject.scene.IsValid()) playerRoots[player.GetInstanceID()] = ((Component)player).transform;
            foreach (FirstPersonController player in Resources.FindObjectsOfTypeAll<FirstPersonController>())
                if (player && player.gameObject.scene.IsValid()) playerRoots[player.GetInstanceID()] = player.transform;
        }

        private bool Excluded(Transform transform)
        {
            if (!transform || !transform.gameObject.scene.IsValid()) return true;
            for (Transform cursor = transform; cursor; cursor = cursor.parent)
            {
                bool localNode;
                if (!localNodeCache.TryGetValue(cursor.GetInstanceID(), out localNode))
                {
                    localNode = cursor.name.StartsWith("NMM_", StringComparison.Ordinal) || cursor is RectTransform || cursor.GetComponent<Camera>();
                    localNodeCache[cursor.GetInstanceID()] = localNode;
                }
                if (localNode) return true;
            }
            foreach (Transform player in playerRoots.Values)
                if (player && (transform == player || transform.IsChildOf(player) || player.IsChildOf(transform))) return true;
            return exclude != null && exclude(transform);
        }

        /// <summary>The item this object belongs to, also for items that start hidden (GetComponentInParent skips those).</summary>
        private static ItemInteractable ItemOf(Transform t)
        {
            for (; t; t = t.parent) { var item = t.GetComponent<ItemInteractable>(); if (item) return item; }
            return null;
        }

        private bool Skipped(Component component)
        {
            if (SkipObject == null || !component) return false;
            string id; Entry entry;
            if (instanceIds.TryGetValue(component.transform.GetInstanceID(), out id) && entries.TryGetValue(id, out entry) && entry.Item)
                return SkipObject(entry.Item);
            return SkipObject(component);
        }

        private bool IsRelevant(Component component)
        {
            if (!component) return false;
            if (component is Interactable || component is Rigidbody || component is Blackboard || component is GraphOwner
                || component is NavMeshAgent || component is Light || component is ParticleSystem || component is Animator)
                return true;
            var source = component as AudioSource;
            if (source && Synced(source)) return true;
            string type = component.GetType().FullName;
            return IsGameScript(component) || OtherRelevant.Contains(type) || AllowedFields.ContainsKey(type);
        }

        private static bool IsGameScript(Component component)
        {
            string type = component.GetType().FullName;
            return type.StartsWith("GrannyClone.", StringComparison.Ordinal)
                || type.StartsWith("AgentNew.", StringComparison.Ordinal)
                || type.StartsWith("AgentOld.", StringComparison.Ordinal)
                || type.StartsWith("Humanoid.", StringComparison.Ordinal) || OtherRelevant.Contains(type);
        }

        // 3D sounds, plus the Nun's own 2D chase music and stingers (guests' Nun graph does not run).
        private bool Synced(AudioSource source) { return source.spatialBlend > 0.01f || enemyAudio.Contains(source.GetInstanceID()); }

        private void RefreshAiGraphIds()
        {
            aiGraphIds.Clear();
            enemyAudio.Clear();
            foreach (Component component in components.Values)
            {
                if (!component) continue;
                if (component.GetType().FullName == "DifficultyController")
                {
                    var graph = ReadField(component, "NunFSM") as GraphOwner;
                    if (graph)
                    {
                        aiGraphIds.Add(graph.GetInstanceID());
                        Transform enemyRoot = graph.transform.parent ? graph.transform.parent : graph.transform;
                        foreach (var source in enemyRoot.GetComponentsInChildren<AudioSource>(true)) enemyAudio.Add(source.GetInstanceID());
                    }
                }
                var owner = component as GraphOwner;
                if (!owner || Excluded(owner.transform)) continue;
                for (Transform ancestor = owner.transform; ancestor; ancestor = ancestor.parent)
                {
                    if (ancestor.GetComponent<NavMeshAgent>() || ancestor.GetComponents<Component>().Any(c => c && IsType(c.GetType(), "Humanoid.Motor2d")))
                    {
                        aiGraphIds.Add(owner.GetInstanceID());
                        break;
                    }
                }
            }
        }

        private void BuildSimTargets()
        {
            simTargets.Clear();
            simTargetsDirty = false;
            foreach (Entry entry in simulationEntries)
            {
                if (!entry.Selected || !entry.Transform || entry.Excluded) continue;
                foreach (Component component in entry.Components)
                {
                    if (!component) continue;
                    if (component is Rigidbody) { simTargets.Add(new SimTarget { Entry = entry, Component = component, Kind = SimKind.Body }); continue; }
                    if (component is GraphOwner && aiGraphIds.Contains(component.GetInstanceID()))
                    {
                        simTargets.Add(new SimTarget { Entry = entry, Component = component, Kind = SimKind.Graph });
                        continue;
                    }
                    if (component is Behaviour && (component is NavMeshAgent || AiDrivers.Any(type => IsType(component.GetType(), type))))
                        simTargets.Add(new SimTarget { Entry = entry, Component = component, Kind = SimKind.Driver });
                    if (component is Animator) simTargets.Add(new SimTarget { Entry = entry, Component = component, Kind = SimKind.Animator });
                }
            }
        }

        private void SuppressSimulation()
        {
            if (isHost || disposed) return;
            if (simTargetsDirty) BuildSimTargets();
            foreach (SimTarget target in simTargets)
            {
                Entry entry = target.Entry;
                Component component = target.Component;
                if (!component || !entry.Transform || (entry.Item && Skipped(entry.Transform))) continue;
                switch (target.Kind)
                {
                    case SimKind.Body:
                    {
                        var body = (Rigidbody)component;
                        int id = body.GetInstanceID();
                        if (!bodies.ContainsKey(id))
                            bodies[id] = new SavedBody { Body = body, Kinematic = body.isKinematic,
                                Gravity = body.useGravity, Detection = body.collisionDetectionMode };
                        if (!body.isKinematic)
                        {
                            body.velocity = Vector3.zero;
                            body.angularVelocity = Vector3.zero;
                            body.collisionDetectionMode = CollisionDetectionMode.Discrete;
                            body.isKinematic = true;
                        }
                        if (body.useGravity) body.useGravity = false;
                        break;
                    }
                    case SimKind.Graph:
                    {
                        var graph = (GraphOwner)component;
                        int id = graph.GetInstanceID();
                        if (!graphs.ContainsKey(id))
                            graphs[id] = new SavedGraph { Graph = graph, Running = graph.isRunning, Paused = graph.isPaused,
                                EnableAction = graph.enableAction };
                        graph.enableAction = GraphOwner.EnableAction.DoNothing;
                        if (graph.isRunning || graph.isPaused) graph.StopBehaviour();
                        Disable(graph);
                        break;
                    }
                    case SimKind.Driver: Disable((Behaviour)component); break;
                    case SimKind.Animator:
                    {
                        var animator = (Animator)component;
                        int id = animator.GetInstanceID();
                        if (!animators.ContainsKey(id)) animators[id] = new SavedAnimator { Animator = animator, RootMotion = animator.applyRootMotion };
                        if (animator.applyRootMotion) animator.applyRootMotion = false;
                        break;
                    }
                }
            }
        }

        private void Disable(Behaviour behaviour)
        {
            int id = behaviour.GetInstanceID();
            if (!behaviours.ContainsKey(id)) behaviours[id] = new SavedBehaviour { Behaviour = behaviour, Enabled = behaviour.enabled };
            if (behaviour.enabled) behaviour.enabled = false;
        }

        private void RestoreSimulation()
        {
            foreach (SavedBody state in bodies.Values)
                if (state.Body)
                {
                    state.Body.isKinematic = state.Kinematic;
                    state.Body.useGravity = state.Gravity;
                    state.Body.collisionDetectionMode = state.Detection;
                }
            foreach (SavedAnimator state in animators.Values) if (state.Animator) state.Animator.applyRootMotion = state.RootMotion;
            foreach (SavedGraph state in graphs.Values) if (state.Graph) state.Graph.enableAction = GraphOwner.EnableAction.DoNothing;
            foreach (SavedBehaviour state in behaviours.Values) if (state.Behaviour) state.Behaviour.enabled = state.Enabled;
            foreach (SavedGraph state in graphs.Values)
            {
                if (!state.Graph) continue;
                state.Graph.enableAction = state.EnableAction;
                if (state.Running || state.Paused) { state.Graph.StartBehaviour(); if (state.Paused) state.Graph.PauseBehaviour(); }
            }
            bodies.Clear(); animators.Clear(); behaviours.Clear(); graphs.Clear();
        }

        private FieldInfo[] GetAllowedFields(Type type)
        {
            FieldInfo[] fields;
            if (fieldCache.TryGetValue(type, out fields)) return fields;
            var result = new List<FieldInfo>();
            for (Type cursor = type; cursor != null && cursor != typeof(MonoBehaviour); cursor = cursor.BaseType)
            {
                string[] names;
                if (!AllowedFields.TryGetValue(cursor.FullName, out names)) continue;
                foreach (string name in names)
                {
                    FieldInfo field = cursor.GetField(name, Fields);
                    if (field != null && !field.IsInitOnly && !field.IsStatic && Primitive(field.FieldType)) result.Add(field);
                }
            }
            fields = result.ToArray(); fieldCache[type] = fields; return fields;
        }

        private static IEnumerable<FieldInfo> InstanceFields(Type type)
        {
            for (Type cursor = type; cursor != null && cursor != typeof(MonoBehaviour); cursor = cursor.BaseType)
                foreach (FieldInfo field in cursor.GetFields(Fields)) if (!field.IsStatic) yield return field;
        }
        private static FieldInfo FindField(Type type, string name)
        {
            for (Type cursor = type; cursor != null; cursor = cursor.BaseType)
            {
                FieldInfo field = cursor.GetField(name, Fields);
                if (field != null) return field;
            }
            return null;
        }
        private static object ReadField(object target, string name)
        {
            FieldInfo field = FindField(target.GetType(), name);
            return field == null ? null : field.GetValue(target);
        }
        private static bool IsType(Type type, string name)
        {
            for (Type cursor = type; cursor != null; cursor = cursor.BaseType) if (cursor.FullName == name) return true;
            return false;
        }
        private static bool Primitive(Type type)
        {
            return type == typeof(bool) || type == typeof(int) || type == typeof(float) || type == typeof(double) || type == typeof(string) || type.IsEnum;
        }
        private static string Encode(object value, Type type)
        {
            if (value == null) return "";
            if (type.IsEnum) return Convert.ToInt64(value, CultureInfo.InvariantCulture).ToString(CultureInfo.InvariantCulture);
            if (type == typeof(float)) return Round((float)value).ToString("R", CultureInfo.InvariantCulture);
            return Convert.ToString(value, CultureInfo.InvariantCulture);
        }
        private static bool TryDecode(string value, Type type, out object result)
        {
            result = null;
            if (value == null || value.Length > 4096 || !Primitive(type)) return false;
            try
            {
                if (type.IsEnum)
                {
                    long number;
                    if (!long.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out number)
                        || !Enum.IsDefined(type, Enum.ToObject(type, number)))
                        return false;
                    result = Enum.ToObject(type, number);
                }
                else result = Convert.ChangeType(value, type, CultureInfo.InvariantCulture);
                return !(result is float) || Finite((float)result);
            }
            catch (FormatException) { return false; }
            catch (OverflowException) { return false; }
        }
        private void CacheClips() { foreach (AudioClip clip in Resources.FindObjectsOfTypeAll<AudioClip>()) if (clip) clips[ClipId(clip)] = clip; }
        private static string ClipId(AudioClip clip) { return clip ? clip.name + "|" + clip.samples + "|" + clip.frequency + "|" + clip.channels : null; }
        private static string Escape(string text) { return Uri.EscapeDataString(text ?? ""); }
        private static int Depth(Transform transform)
        {
            int depth = 0;
            for (Transform parent = transform.parent; parent; parent = parent.parent) depth++;
            return depth;
        }
        private static float Round(float number) { return Mathf.Round(number * 1000f) / 1000f; }
        private static Vector3 Round(Vector3 value) { return new Vector3(Round(value.x), Round(value.y), Round(value.z)); }
        private static Quaternion Round(Quaternion value) { return new Quaternion(Round(value.x), Round(value.y), Round(value.z), Round(value.w)); }
        private static bool Finite(float value) { return !float.IsNaN(value) && !float.IsInfinity(value); }
        private static bool Finite(Vector3 value) { return Finite(value.x) && Finite(value.y) && Finite(value.z) && value.sqrMagnitude < 1e12f; }
        private static bool Finite(Quaternion value)
        {
            float size = value.x * value.x + value.y * value.y + value.z * value.z + value.w * value.w;
            return Finite(size) && size > 0.5f && size < 1.5f;
        }
        private void Warn(string key, string message) { if (warnings.Add(key)) log(message); }
        public void Dispose()
        {
            if (disposed) return;
            RestoreSimulation();
            disposed = true;
            components.Clear();
            instanceIds.Clear();
            entries.Clear();
            lastSent.Clear();
        }
    }
}
