using System;
using System.Collections.Generic;
using System.Linq;
using GrannyClone;
using UnityEngine;
using Object = UnityEngine.Object;

namespace NunMassacre.Multiplayer.World
{
    /// <summary>
    /// Objects the host's game creates at runtime: the Nun's thrown bear traps, and the tools made for a player who came
    /// in after the level was built. The host gives each a network ID and tells peers to create the same thing; the world
    /// replicator then keeps its pose and active state in step. Each peer simulates being caught by its own copy of a
    /// trap, like the single-player game. Only trap prefabs (<see cref="Allowed"/>) and the session's tool templates
    /// ("item:" and the tool's name) can be created remotely.
    /// </summary>
    internal sealed class DynamicSpawns
    {
        private sealed class Spawn { public string Id, Prefab; public Transform Root; }
        private readonly Dictionary<string, Spawn> spawns = new Dictionary<string, Spawn>(StringComparer.Ordinal);
        private readonly Action<string> log;
        private int counter;
        internal Func<WorldReplicator> World;
        internal Func<string, GameObject> Template; // A tool's inactive template, by item name.

        internal DynamicSpawns(Action<string> log) { this.log = log ?? delegate { }; }

        internal void Reset() { spawns.Clear(); counter = 0; }

        /// <summary>Host: a bear trap started. Returns a spawn packet if it is a new runtime object.</summary>
        internal Packet HostObserve(BearTrap trap)
        {
            var world = World();
            Transform root = InstanceRoot(trap.transform);
            if (world == null || !root || world.IdFor(root) != null) return null;
            string prefab = PrefabName(root);
            string id = "dyn/" + (++counter) + "/" + Uri.EscapeDataString(prefab);
            world.RegisterDynamic(root, id);
            spawns[id] = new Spawn { Id = id, Prefab = prefab, Root = root };
            log("Replicating runtime object " + prefab + " as " + id);
            return Packet(spawns[id]);
        }

        /// <summary>Host: a tool made for a late player (already placed and active).</summary>
        internal Packet HostCopy(Transform root, string itemName)
        {
            var world = World();
            if (world == null || !root) return null;
            string id = "dyn/" + (++counter) + "/item:" + Uri.EscapeDataString(itemName);
            world.RegisterDynamic(root, id);
            spawns[id] = new Spawn { Id = id, Prefab = "item:" + itemName, Root = root };
            log("Replicating a late player's " + itemName + " as " + id);
            return Packet(spawns[id]);
        }

        /// <summary>Host: spawn packets for a peer that joins after objects were created.</summary>
        internal IEnumerable<Packet> Existing()
        {
            return spawns.Values.Where(s => s.Root && s.Root.gameObject.activeSelf).Select(Packet).ToList();
        }

        /// <summary>Peer: create the host's runtime object locally.</summary>
        internal void Create(Packet p)
        {
            var world = World();
            if (world == null || string.IsNullOrEmpty(p.target) || p.target.Length > 256 || world.Find(p.target) || spawns.ContainsKey(p.target)) return;
            bool tool = p.text != null && p.text.StartsWith("item:", StringComparison.Ordinal);
            var prefab = tool ? (Template != null ? Template(p.text.Substring(5)) : null) : FindPrefab(p.text);
            if (!prefab) { log("Runtime object prefab unavailable: " + p.text); return; }
            var instance = Object.Instantiate(prefab, p.position, p.rotation);
            if (tool) { instance.name = p.text.Substring(5); instance.SetActive(true); } // From an inactive template.
            world.RegisterDynamic(instance.transform, p.target);
            spawns[p.target] = new Spawn { Id = p.target, Prefab = p.text, Root = instance.transform };
        }

        internal string IdFor(Component component)
        {
            Transform root = component ? InstanceRoot(component.transform) : null;
            foreach (var spawn in spawns.Values) if (spawn.Root == root) return spawn.Id;
            return null;
        }

        internal void Deactivate(string id)
        {
            Spawn spawn;
            if (spawns.TryGetValue(id ?? "", out spawn) && spawn.Root && spawn.Root.gameObject.activeSelf) spawn.Root.gameObject.SetActive(false);
        }

        private static Packet Packet(Spawn spawn)
        {
            return new Packet { type = "spawn", target = spawn.Id, text = spawn.Prefab, position = spawn.Root.position, rotation = spawn.Root.rotation };
        }

        private static Transform InstanceRoot(Transform transform)
        {
            for (Transform cursor = transform; cursor; cursor = cursor.parent)
                if (cursor.name.EndsWith("(Clone)", StringComparison.Ordinal)) return cursor;
            return null;
        }

        private static string PrefabName(Transform root) { return root.name.Substring(0, root.name.Length - "(Clone)".Length).Trim(); }

        private static bool Allowed(GameObject prefab) { return prefab.GetComponentInChildren<BearTrap>(true) != null; }

        private static GameObject FindPrefab(string name)
        {
            if (string.IsNullOrEmpty(name)) return null;
            // Prefab assets are loaded but belong to no scene.
            return Resources.FindObjectsOfTypeAll<GameObject>().FirstOrDefault(g => g && g.name == name && !g.scene.IsValid() && g.transform.parent == null && Allowed(g));
        }
    }
}
