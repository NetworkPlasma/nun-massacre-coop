using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using BepInEx;
using GrannyClone;
using HarmonyLib;
using NunMassacre.Multiplayer.Gameplay;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace NunMassacre.Multiplayer
{
    // The host's console and the test and diagnostic reports.
    internal sealed partial class Session
    {
        private readonly Dictionary<int, float> kicks = new Dictionary<int, float>();
        private float nextDiagnostic;

        internal IEnumerable<PlayerFrame> ConsolePlayers()
        {
            var self = CapturePlayer(); // Our own, as of now (the stored one is only updated with actions).
            return players.Values.Where(f => f.id != LocalId).Concat(self != null ? new[] { self } : new PlayerFrame[0]).OrderBy(f => f.id);
        }

        /// <summary>Host: move a player ("pos" to a spot, "safe" to their last footing, "spawn" to the respawn point).</summary>
        /// <summary>Automated tests: a step for a guest's test game to run (ignored by normal games).</summary>
        internal void SendTestStep(int peer, string step) { if (IsHost) Send(peer, new Packet { type = "teststep", text = step }); }

        internal void ConsoleTeleport(int peer, string mode, Vector3 position)
        {
            if (peer == LocalId) { Life.Teleported(mode, position); return; }
            if (IsHost) Send(peer, new Packet { type = "teleport", epoch = epoch, scene = SceneName, text = mode, position = position });
        }

        internal void ConsoleRevive(int peer)
        {
            var frame = FrameOf(peer);
            if (!IsHost || frame == null || !frame.downed) return;
            if (peer == LocalId) Life.Revived(CoopLife.KitRevivedHealth, "the host");
            else Send(peer, new Packet { type = "revived", epoch = epoch, amount = CoopLife.KitRevivedHealth, text = "the host", number = LocalId, item = "" });
        }

        internal void ConsoleKick(int peer)
        {
            if (!IsHost || peer == LocalId || !authenticated.Contains(peer)) return;
            Send(peer, new Packet { type = "reject", text = "The host removed you from the game." });
            kicks[peer] = Time.realtimeSinceStartup + 1f;
        }

        internal void WriteDiagnostics()
        {
            var report = new StringBuilder();
            report.AppendLine("NMM " + Plugin.Version + " " + DateTime.UtcNow.ToString("O"));
            report.AppendLine("Build: " + plugin.BuildHash + " Scene: " + SceneName + " Registry: " + WorldCount);
            report.AppendLine(Status); report.AppendLine(RosterText());
            report.AppendLine("Build scenes:");
            for (int i = 0; i < SceneManager.sceneCountInBuildSettings; i++) report.AppendLine(i + ": " + SceneUtility.GetScenePathByBuildIndex(i));
            foreach (var m in Object.FindObjectsOfType<MonoBehaviour>())
                if (m && (m.GetType().Assembly == typeof(Inventory).Assembly) && !(m is UnityEngine.EventSystems.UIBehaviour))
                    report.AppendLine(m.name + " | " + m.GetType().FullName);
            foreach (var board in Object.FindObjectsOfType<NodeCanvas.Framework.Blackboard>())
            {
                report.AppendLine("BLACKBOARD " + board.gameObject.name);
                foreach (var pair in board.variables) report.AppendLine("  " + pair.Key + " | " + pair.Value.varType + " | " + pair.Value.value);
            }
            GraphDump.Write(report);
            var path = Path.Combine(Paths.GameRootPath, "BepInEx", "NMM-diagnostics.txt");
            File.WriteAllText(path, report.ToString());
            plugin.Log("Diagnostics: " + path);
        }

        [Serializable] private class TestState
        {
            public bool active, ready, host;
            public string scene, status, registry, localComponents;
            public int peer, remoteCount, worldCount, snapshotObjects;
            public long captureMilliseconds;
            public PlayerFrame local;
            public PlayerFrame[] players;
            public Ownership[] owners;
            public string[] inventory;
            public TestObject[] objects;
        }

        [Serializable] private class TestObject { public string id, name; public bool active, open; public Vector3 position; public Quaternion rotation; }

        internal string TestReport()
        {
            var inventory = Local && Local.inventory ? AccessTools.Field(typeof(Inventory), "items").GetValue(Local.inventory) as List<ItemInteractable> : null;
            return JsonCodec.ToJson(new TestState { active = Active, ready = Ready, host = IsHost, scene = SceneName, status = Status,
                registry = world == null ? "" : world.RegistryDigest, peer = LocalId, remoteCount = avatars.RemoteCount, worldCount = WorldCount,
                localComponents = "time=" + Time.realtimeSinceStartup + " local=" + (bool)Local + " health=" + (Local && Local.health) + " motor=" + (Local && Local.firstPersonController),
                snapshotObjects = world.LastSnapshotObjects, captureMilliseconds = world.LastCaptureMilliseconds,
                local = CapturePlayer(), players = players.Values.ToArray(), owners = OwnershipList(), inventory = inventory == null ? new string[0] : inventory.Where(i => i).Select(i => i.name).ToArray(),
                objects = Resources.FindObjectsOfTypeAll<Interactable>().Where(c => c && c.gameObject.scene.IsValid() && (c is ItemInteractable || c is CabinetsInteractable)).Select(c => new TestObject {
                    id = world.IdFor(c), name = c.name, active = c.gameObject.activeSelf, position = c.transform.position, rotation = c.transform.rotation,
                    open = c is CabinetsInteractable && (bool)AccessTools.Field(typeof(CabinetsInteractable), "isOpen").GetValue(c)
                }).OrderBy(c => c.id).ToArray()
                }, true);
        }

        internal string TopDirty()
        {
            if (world == null || world.DirtyCounts.Count == 0) return "";
            string text = string.Join(", ",
                world.DirtyCounts.OrderByDescending(x => x.Value).Take(6).Select(x => x.Value + "x " + x.Key.Substring(Math.Max(0, x.Key.Length - 60))).ToArray());
            world.DirtyCounts.Clear();
            return text;
        }

        internal void WriteModelPreview(string path) { avatars.WriteModelPreview(path); }
    }
}
