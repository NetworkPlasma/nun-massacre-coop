using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace NunMassacre.Multiplayer
{
    // Loading levels together: everyone starts on the same loading screen and goes in when all are ready.
    internal sealed partial class Session
    {
        private string aheadScene; // A scene the guest's own game moved on to before the host did.
        private string requestedScene; // The scene this guest last loaded because the host was in it.
        // Starting together: the host's PLAY INTRO / SKIP AHEAD starts every guest's own loading screen
        // (the same variant); each game holds its loaded level until all have it, then all enter at once.
        internal int LoadingVariant = -1;
        private bool syncLoad, syncReleased, syncReported, startingFromHost;
        private float syncStarted;
        private string syncScene;
        private readonly HashSet<int> syncLoaded = new HashSet<int>();
        private const float SyncHostTimeout = 60f, SyncGuestTimeout = 120f;
        internal static float TestLoadDelay; // Automated tests: report the loaded level this late.
        internal bool WaitingForPlayers { get { return syncLoad && syncReported && !syncReleased; } }
        private AsyncOperation sceneLoadOperation;

        /// <summary>GraphOwner.SendEvent: the main menu's PLAY INTRO / SKIP AHEAD. False blocks it.</summary>
        internal bool MenuEvent(NodeCanvas.Framework.GraphOwner owner, string name)
        {
            if (owner && owner.name == "Opening Scene new" && name == "State3" && OpeningTogether && !Replaying)
            {
                // Read the letter: the host takes everyone outside together.
                Request(new Packet { action = "opening", text = "letter" });
                return false;
            }
            if (!Active || (name != "Intro" && name != "Skip") || !owner || owner.name != "Main menu") return true;
            if (!IsHost) return startingFromHost; // Guests start when the host does.
            if (authenticated.Count == 0) return true;
            BeginSyncLoad(UnityEngine.Random.Range(0, 10) <= 5 ? 0 : 1); // The game's own odds for its two screens.
            Broadcast(new Packet { type = "loadstart", epoch = epoch, text = name, number = LoadingVariant,
                amount = (int)DifficultyController.CurrentDifficulty, settings = GameSettings() });
            plugin.Log("Starting together (" + name + ") with " + authenticated.Count + " guest(s)");
            return true;
        }

        private void BeginSyncLoad(int variant)
        {
            syncLoad = true; syncReleased = false; syncReported = false; syncScene = null;
            syncLoaded.Clear(); syncStarted = Time.realtimeSinceStartup; LoadingVariant = variant;
        }

        private void StartWithHost(Packet p)
        {
            var menu = SceneName != MainMenuScene ? null
                : SceneManager.GetActiveScene().GetRootGameObjects().Select(g => g.GetComponent<NodeCanvas.StateMachines.FSMOwner>()).FirstOrDefault(o => o && o.name == "Main menu");
            if (!menu || (p.text != "Intro" && p.text != "Skip"))
            {
                // Not in the lobby menu: do not hold the others up; follow into the level as usual.
                Send(1, new Packet { type = "loaded", epoch = epoch });
                plugin.Log("Starting together: not in the main menu, following later");
                return;
            }
            BeginSyncLoad(Mathf.Clamp(p.number, 0, 1));
            ApplyGameSettings(p.settings);
            DifficultyController.CurrentDifficulty = (DifficultyLevel)Mathf.Clamp((int)p.amount, 0, 4);
            loading = true; // Do not start a second load of the host's scene meanwhile.
            startingFromHost = true;
            try { menu.SendEvent(p.text); }
            finally { startingFromHost = false; }
            plugin.Log("Starting together with the host (" + p.text + ")");
        }

        /// <summary>LoadSceneAsync's update: true keeps the loaded level waiting for the other players.</summary>
        internal bool HoldActivation(bool loaded, string scene)
        {
            if (!syncLoad || syncReleased) return false;
            float waited = Time.realtimeSinceStartup - syncStarted;
            if (loaded && !syncReported && waited >= TestLoadDelay)
            {
                syncReported = true; syncScene = scene;
                if (IsHost) syncLoaded.Add(LocalId);
                else Send(1, new Packet { type = "loaded", epoch = epoch, scene = scene });
                plugin.Log("Level loaded (loading screen " + LoadingVariant + "); waiting for the other players");
            }
            if (IsHost && syncReported && (authenticated.All(syncLoaded.Contains) || waited > SyncHostTimeout))
            {
                if (!authenticated.All(syncLoaded.Contains)) plugin.Log("Starting without players still loading after " + SyncHostTimeout + "s");
                syncReleased = true;
                Broadcast(new Packet { type = "go", epoch = epoch, scene = syncScene });
                plugin.Log("Everyone has the level: entering");
            }
            if (!IsHost && (waited > SyncGuestTimeout || !Connected)) syncReleased = true; // Never wait forever.
            return !syncReleased;
        }

        /// <summary>The PLAY screen's choices that shape the level as it loads: everyone must build the same one.</summary>
        private string GameSettings()
        {
            return (int)TextureSwapper.CurrentID + "|" + (int)SkinSwapper.CurrentID + "|"
                + (RandomizeItemLocationsMode.RandomModeEnabled ? 1 : 0) + "|" + WorldSeed + "|" + toolCopies;
        }

        private void ApplyGameSettings(string text)
        {
            var parts = (text ?? "").Split('|');
            int scenario, skin, random, seed, copies;
            if (parts.Length != 5 || !int.TryParse(parts[0], out scenario) || !int.TryParse(parts[1], out skin)
                || !int.TryParse(parts[2], out random) || !int.TryParse(parts[3], out seed)
                || !int.TryParse(parts[4], out copies))
                return;
            toolCopies = Mathf.Clamp(copies, 1, 4);
            if (Enum.IsDefined(typeof(TextureSwapID), scenario)) TextureSwapper.CurrentID = (TextureSwapID)scenario;
            if (Enum.IsDefined(typeof(SkinID), skin)) SkinSwapper.CurrentID = (SkinID)skin;
            RandomizeItemLocationsMode.RandomModeEnabled = random == 1;
            WorldSeed = seed;
        }

        private Packet ScenePacket(string type)
        {
            return new Packet { type = type, scene = SceneName, epoch = epoch, build = plugin.BuildHash,
                number = (int)DifficultyController.CurrentDifficulty, settings = GameSettings(), originalHearing = plugin.OriginalHearing.Value };
        }

        private void LoadHostScene()
        {
            if (string.IsNullOrEmpty(hostScene)) return;
            if (!Application.CanStreamedLevelBeLoaded(hostScene)) { Drop("Host scene is unavailable: " + hostScene); return; }
            loading = true; Ready = false;
            requestedScene = hostScene;
            sceneLoadOperation = SceneManager.LoadSceneAsync(hostScene, LoadSceneMode.Single);
        }
    }
}
