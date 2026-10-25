using System;
using System.Collections.Generic;
using System.Linq;
using GrannyClone;
using HarmonyLib;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace NunMassacre.Multiplayer
{
    // Items: a tool for every player, ownership, throwing, glass, traps and the gas.
    internal sealed partial class Session
    {
        private bool gas;
        private int paperclips;
        // Tools each player needs their own of: one per player. The radar and the revolver stay one for the team (one
        // player carries the radar and calls out the Nun; one has the gun), and puzzle pieces stay unique.
        private static readonly string[] SpareItems = { "Lighter Item", "Map Item", "Bandages Item" };
        /// <summary>Copies of each tool in the level: the number of players when it was entered, 1 to 4. The host
        /// sets it while everyone is still outside the school and every peer builds the level with it.</summary>
        private int toolCopies = 1;

        /// <summary>Per tool: its originals as the level was built (the main game's and each scenario's: where a late
        /// player's copy goes is beside the one in play) and an inactive copy that later copies are made from (an
        /// original may be in someone's hand by then).</summary>
        private sealed class SpareSource
        {
            public readonly List<ItemInteractable> Originals = new List<ItemInteractable>();
            public readonly List<Transform> Homes = new List<Transform>();
            public readonly List<Pose> Places = new List<Pose>();
            public GameObject Template;
        }

        private readonly Dictionary<string, SpareSource> spareSources = new Dictionary<string, SpareSource>(StringComparer.Ordinal);
        private int lateSpareSets; // Host: sets of tools added for players who came in after the level was built.

        /// <summary>Host, before the school is entered: a player joined or left, so each tool's copies follow.</summary>
        private void RecountTools()
        {
            if (!IsHost || SceneName == GameScene) return; // Inside the school the level is already built.
            int copies = Mathf.Clamp(authenticated.Count + 1, 1, 4);
            if (copies == toolCopies) return;
            toolCopies = copies;
            Broadcast(new Packet { type = "settings", epoch = epoch, settings = GameSettings() });
            plugin.Log("Tools for " + copies + " players.");
        }

        /// <summary>Puts a spare of each tool next to the original, as its next same-named sibling, so the
        /// copy has the same world id on every peer. Puzzles match items by name, so a spare lighter lights
        /// candles like the original.</summary>
        private void AddSpareItems(Scene scene)
        {
            spareSources.Clear(); lateSpareSets = 0;
            var originals = scene.GetRootGameObjects().SelectMany(root => root.GetComponentsInChildren<ItemInteractable>(true))
                .Where(item => item && SpareItems.Any(name => item.name == name || item.name.StartsWith(name + " (", StringComparison.Ordinal))).ToList();
            if (originals.Count == 0) return;
            Physics.SyncTransforms();
            // Templates for players who join later: inactive, under an NMM_ root the world never registers.
            var templates = new GameObject("NMM_SpareTemplates");
            SceneManager.MoveGameObjectToScene(templates, scene);
            templates.SetActive(false);
            foreach (var group in originals.GroupBy(item => item.name))
            {
                var source = new SpareSource();
                foreach (var item in group)
                {
                    source.Originals.Add(item); source.Homes.Add(item.transform.parent);
                    source.Places.Add(new Pose(item.transform.position, item.transform.rotation));
                }
                var model = group.FirstOrDefault(item => item.gameObject.activeInHierarchy) ?? group.First();
                source.Template = Object.Instantiate(model.gameObject, templates.transform);
                spareSources[group.Key] = source;
            }
            int copies = Mathf.Clamp(toolCopies, 1, 4);
            foreach (var item in originals)
                for (int copy = 1; copy < copies; copy++)
                {
                    var spare = Object.Instantiate(item.gameObject, item.transform.parent);
                    spare.name = item.name;
                    spare.transform.position = SpareSpot(item, spare);
                    Physics.SyncTransforms(); // The next copy finds this one in the way.
                }
            plugin.Log("Co-op spares: " + originals.Count(i => i.gameObject.activeInHierarchy) + " tools in play, " + copies
                + " of each (" + string.Join(", ", SpareItems) + ")");
        }

        /// <summary>
        /// Host, a player came into the school after it was built: a lighter, map and bandages of their own, made from the
        /// templates beside where each original lay, and created on every peer (and on later joiners) like the Nun's traps.
        /// Players who leave keep theirs in the level; a set is only made when the count of players goes past it.
        /// </summary>
        private void AddLateSpares()
        {
            int present = Mathf.Min(4, 1 + readyPeers.Count);
            while (toolCopies + lateSpareSets < present)
            {
                lateSpareSets++;
                foreach (var pair in spareSources)
                {
                    var source = pair.Value;
                    if (!source.Template) continue;
                    // Beside the original in play and still where it lay; else where one in play lay; else the first.
                    int at = -1;
                    for (int i = 0; i < source.Originals.Count && at < 0; i++)
                        if (source.Originals[i] && source.Originals[i].gameObject.activeInHierarchy && source.Originals[i].transform.parent == source.Homes[i])
                            at = i;
                    bool inPlace = at >= 0;
                    for (int i = 0; i < source.Originals.Count && at < 0; i++)
                        if (source.Homes[i] && source.Homes[i].gameObject.activeInHierarchy) at = i;
                    if (at < 0) continue; // Only another scenario's (its part of the school is switched off): not in this game.
                    var spare = Object.Instantiate(source.Template, source.Places[at].position, source.Places[at].rotation); // Inactive, like its template.
                    spare.name = pair.Key; // Puzzles know items by name.
                    if (inPlace)
                    {
                        spare.transform.rotation = source.Originals[at].transform.rotation;
                        spare.transform.position = SpareSpot(source.Originals[at], spare);
                    }
                    spare.SetActive(true);
                    var packet = spawns.HostCopy(spare.transform, pair.Key);
                    if (packet != null) { packet.epoch = epoch; Broadcast(packet); }
                }
                plugin.Log("Tools for a player who came in late: set " + (toolCopies + lateSpareSets) + " of "
                    + string.Join(", ", spareSources.Keys.ToArray()));
            }
        }

        private GameObject SpareTemplate(string name)
        {
            SpareSource source;
            return spareSources.TryGetValue(name ?? "", out source) ? source.Template : null;
        }

        /// <summary>A free spot beside the item (sideways in its drawer or on its table), else on top of it.</summary>
        private static Vector3 SpareSpot(ItemInteractable item, GameObject spare)
        {
            var shape = item.GetComponent<Collider>();
            // Settles beside it if that scenario is used.
            if (!shape || !item.gameObject.activeInHierarchy) return item.transform.position + Vector3.up * 0.12f;
            Bounds bounds = shape.bounds;
            Transform frame = item.transform.parent ? item.transform.parent : item.transform;
            Vector3 right = Vector3.ProjectOnPlane(frame.right, Vector3.up).normalized, forward = Vector3.ProjectOnPlane(frame.forward, Vector3.up).normalized;
            if (right.sqrMagnitude < 0.5f) right = Vector3.right;
            if (forward.sqrMagnitude < 0.5f) forward = Vector3.forward;
            var own = new HashSet<Collider>(item.GetComponentsInChildren<Collider>(true).Concat(spare.GetComponentsInChildren<Collider>(true)));
            foreach (Vector3 direction in new[] { right, -right, forward, -forward, (right + forward).normalized,
                (right - forward).normalized, (forward - right).normalized, -(right + forward).normalized })
            {
                float step = Mathf.Abs(Vector3.Dot(bounds.size, new Vector3(Mathf.Abs(direction.x), Mathf.Abs(direction.y), Mathf.Abs(direction.z)))) + 0.03f;
                Vector3 offset = direction * step, center = bounds.center + offset;
                // Lifted a little and shrunk so the surface it rests on does not count as in the way...
                if (!Physics.OverlapBox(center + Vector3.up * 0.02f, bounds.extents * 0.9f, Quaternion.identity, ~0,
                    QueryTriggerInteraction.Ignore).All(c => own.Contains(c)))
                    continue;
                // ...and that surface goes on under it: not off the edge of a table or shelf.
                RaycastHit below;
                if (!Physics.Raycast(center, Vector3.down, out below, bounds.extents.y + 0.1f, ~0, QueryTriggerInteraction.Ignore)
                    || own.Contains(below.collider))
                    continue;
                return item.transform.position + offset;
            }
            return item.transform.position + Vector3.up * (bounds.size.y + 0.02f);
        }

        private void AdoptLocalInventory()
        {
            if (!Local || !Local.inventory) return;
            var items = AccessTools.Field(typeof(Inventory), "items").GetValue(Local.inventory) as List<ItemInteractable>;
            if (items != null) foreach (var item in items) if (item) owners[world.IdFor(item)] = LocalId;
        }

        private void SetOwner(string id, int peer)
        {
            if (string.IsNullOrEmpty(id)) return;
            int existing;
            bool changed = !owners.TryGetValue(id, out existing) || existing != peer;
            owners[id] = peer;
            var item = world.Find(id) as ItemInteractable;
            if (!item) return;
            if (peer == LocalId && changed && Local && Local.inventory)
            {
                bool before = Replaying; Replaying = true;
                try { item.gameObject.SetActive(true); item.Interact(); }
                finally { Replaying = before; }
            }
            else if (peer != 0 && peer != LocalId)
            {
                // As the game does on pickup (here only the holder runs it): no glint, no collision, no physics.
                var flare = item.GetComponentInChildren<LensFlare>(true);
                if (flare) flare.gameObject.SetActive(false);
                var collider = item.GetComponent<Collider>();
                if (collider) collider.enabled = false;
                var body = item.GetComponent<Rigidbody>();
                if (body) body.isKinematic = true;
                if (changed) WorldEquipEffects(item);
                item.gameObject.SetActive(false);
            }
        }

        /// <summary>The parts of an item's equip event that change the item itself, for a player holding it
        /// elsewhere: the statue head unpins from the corpse, a body part breaks off its hook, a tape becomes
        /// what the TVs play. Its screen UI, sounds and hand effects (radar, lighter flame) stay the holder's.</summary>
        private static void WorldEquipEffects(ItemInteractable item)
        {
            var events = item ? item.onEquipEvent : null;
            if (events == null) return;
            try
            {
                var calls = Traverse.Create(events).Field("m_PersistentCalls").Field("m_Calls").GetValue<System.Collections.IList>();
                if (calls == null || calls.Count != events.GetPersistentEventCount()) return;
                var states = new UnityEngine.Events.UnityEventCallState[calls.Count];
                bool any = false;
                for (int i = 0; i < calls.Count; i++)
                {
                    states[i] = Traverse.Create(calls[i]).Field("m_CallState").GetValue<UnityEngine.Events.UnityEventCallState>();
                    var target = events.GetPersistentTarget(i) as Component;
                    bool own = target && !(target is AudioSource) && !(target is ParticleSystem) && target.transform.IsChildOf(item.transform);
                    any |= own && states[i] != UnityEngine.Events.UnityEventCallState.Off;
                    if (!own) events.SetPersistentListenerState(i, UnityEngine.Events.UnityEventCallState.Off);
                }
                try { if (any) events.Invoke(); }
                finally { for (int i = 0; i < states.Length; i++) events.SetPersistentListenerState(i, states[i]); }
            }
            catch (Exception e) { Plugin.Instance.Log("Item equip effects failed for " + item.name + ": " + e.Message); }
        }

        /// <summary>ThrowableItem.Use (a rock): thrown for everyone through the host, not used up.</summary>
        internal bool ThrowItem(ThrowableItem item)
        {
            if (!Active || !Ready || Replaying || !item || !Local || PersonalScene) return true;
            var camera = Camera.main;
            if (!camera || !item.CanDropItem(camera.transform.position, camera.transform.forward)) return true; // The game shows its "can't".
            RaycastHit hit; // Point-blank at a window, as the game checks; the break itself goes through the host.
            if (Physics.Raycast(camera.transform.position, camera.transform.forward, out hit, 2.5f, -1, QueryTriggerInteraction.Ignore)
                && MessagesGranny.RockHitEvent != null)
                MessagesGranny.RockHitEvent(hit.collider);
            Request(new Packet { action = "throw", target = world.IdFor(item) });
            return false;
        }

        /// <summary>BreakableGlass.BreakGlass: a window breaks for everyone or no one.</summary>
        internal bool GlassBreak(GrannyClone.BreakableGlass glass) { return !Active || Replaying || !glass || WorldCommand(glass, "glass"); }

        internal void Consumed(ItemInteractable item)
        {
            if (Active && Ready && !Replaying && item && !PersonalScene) Request(new Packet { action = "consume", target = world.IdFor(item) });
        }

        internal void GasChanged(bool on)
        {
            if (!Active || !Ready || !IsHost || Replaying) return;
            gas = on; SendGlobals();
        }

        /// <summary>
        /// The game's Quit task ran: an ending (or the final credits) on this peer. In co-op that ends
        /// the run for the whole team, who return to the lobby together.
        /// </summary>
        /// <summary>A bear trap started: on the host, a Nun-thrown trap is replicated to every peer.</summary>
        internal void TrapStarted(BearTrap trap)
        {
            if (!Active || !IsHost || !Ready || !trap) return;
            var packet = spawns.HostObserve(trap);
            if (packet != null) { packet.epoch = epoch; Broadcast(packet); }
        }

        /// <summary>A trap was escaped or disarmed here; it is gone for everyone.</summary>
        internal void TrapDisabled(BearTrap trap)
        {
            if (!Active || !Ready || IsHost || !trap) return;
            string id = spawns.IdFor(trap);
            if (id != null) Request(new Packet { action = "trapoff", target = id });
        }
    }
}
