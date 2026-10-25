using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using GrannyClone;
using HarmonyLib;
using UnityEngine;

namespace NunMassacre.Multiplayer
{
    internal static class GameHooks
    {
        private static Session Current { get { return Plugin.Instance == null ? null : Plugin.Instance.Session; } }
        internal static void Install(Harmony harmony)
        {
            int count = 0;
            foreach (Type type in typeof(Interactable).Assembly.GetTypes().Where(t => typeof(Interactable).IsAssignableFrom(t)))
            {
                foreach (var method in type.GetMethods(BindingFlags.DeclaredOnly | BindingFlags.Public | BindingFlags.Instance))
                {
                    if ((method.Name == "Interact" || method.Name == "Release") && method.GetParameters().Length == 0 && !method.IsAbstract)
                    {
                        harmony.Patch(method, prefix: new HarmonyMethod(typeof(GameHooks), nameof(InteractionPrefix)));
                        count++;
                    }
                }
            }
            Patch(harmony, typeof(Inventory), "DropItem", nameof(DropPrefix));
            var flip = AccessTools.Method(typeof(HideBedInteractable), "FlipBed");
            if (flip != null) harmony.Patch(flip, postfix: new HarmonyMethod(typeof(GameHooks), nameof(FlipBedPostfix)));
            foreach (var effect in new[] { typeof(NodeCanvas.Tasks.Actions.ExecuteFunction_Multiplatform),
                typeof(NodeCanvas.Tasks.Actions.ActivateGameObject) })
            {
                var method = AccessTools.Method(effect, "OnExecute");
                if (method != null) harmony.Patch(method, prefix: new HarmonyMethod(typeof(GameHooks), nameof(HuntEffectPrefix)));
            }
            var onCamera = AccessTools.Method(typeof(Humanoid.Agent.EnemyHunter.Tasks.IsOnCamera), "OnCheck");
            if (onCamera != null) harmony.Patch(onCamera, prefix: new HarmonyMethod(typeof(GameHooks), nameof(OnCameraPrefix)));
            else Plugin.Instance.Log("Hook unavailable: IsOnCamera.OnCheck");
            var flipStep = AccessTools.Method(typeof(GrannyClone.Tasks.FlipBed), "OnExecute");
            if (flipStep != null) harmony.Patch(flipStep, prefix: new HarmonyMethod(typeof(GameHooks), nameof(FlipStepPrefix)));
            else Plugin.Instance.Log("Hook unavailable: GrannyClone.Tasks.FlipBed.OnExecute");
            // Unlocks earned together. A teammate's anomaly find already runs on every game (as any interaction): counted
            // once per save. And the game's own end-of-game unlocks (Factory, Circus) are given on every game.
            var anomaly = AccessTools.Method(typeof(AnomalyCollect), "Interact");
            if (anomaly != null)
                harmony.Patch(anomaly, prefix: new HarmonyMethod(typeof(GameHooks), nameof(AnomalyPrefix)),
                    postfix: new HarmonyMethod(typeof(GameHooks), nameof(AnomalyPostfix)));
            else Plugin.Instance.Log("Hook unavailable: AnomalyCollect.Interact");
            var unlocks = AccessTools.Method(typeof(AchievementHandler), "UnlockDifficultyAchievement");
            if (unlocks != null) harmony.Patch(unlocks, postfix: new HarmonyMethod(typeof(GameHooks), nameof(UnlocksPostfix)));
            else Plugin.Instance.Log("Hook unavailable: AchievementHandler.UnlockDifficultyAchievement");
            // Automated test games keep their own save (a copy of the player's, made once): they never write to the real one.
            foreach (string name in new[] { "SavePC", "LoadPC" })
            {
                var save = AccessTools.Method(typeof(MyPlayerPrefs), name);
                if (save != null) harmony.Patch(save, prefix: new HarmonyMethod(typeof(GameHooks), nameof(TestSavePrefix)));
            }
            // The pause menu's player list: while one of its sliders is selected, the menu's own navigation steps aside.
            var menuNavigation = AccessTools.Method(typeof(MenuNavigation), "Update");
            if (menuNavigation != null) harmony.Patch(menuNavigation, prefix: new HarmonyMethod(typeof(GameHooks), nameof(MenuNavigationPrefix)));
            var noise = AccessTools.Method(typeof(Detectable.NoiseAlertMaster), "SendAlert");
            if (noise != null) harmony.Patch(noise, prefix: new HarmonyMethod(typeof(GameHooks), nameof(NoiseRadiusPrefix)));
            else Plugin.Instance.Log("Hook unavailable: Detectable.NoiseAlertMaster.SendAlert");
            var audioStep = AccessTools.Method(typeof(NodeCanvas.Tasks.Actions.PlayAudioSource), "OnExecute");
            if (audioStep != null) harmony.Patch(audioStep, prefix: new HarmonyMethod(typeof(GameHooks), nameof(BedSoundPrefix)));
            var exit = AccessTools.Method(typeof(PlayerStates), "exitPressed");
            if (exit != null) harmony.Patch(exit, prefix: new HarmonyMethod(typeof(GameHooks), nameof(ExitHidingPrefix)));
            var fire = AccessTools.Method(typeof(RevolverInteractable), "fire");
            if (fire != null)
                harmony.Patch(fire, prefix: new HarmonyMethod(typeof(GameHooks), nameof(FirePrefix)),
                    postfix: new HarmonyMethod(typeof(GameHooks), nameof(FirePostfix)));
            else Plugin.Instance.Log("Hook unavailable: RevolverInteractable.fire");
            Patch(harmony, typeof(Inventory), "ClearEquippedItem", nameof(ClearHeldPrefix));
            Patch(harmony, typeof(ThrowableItem), "Use", nameof(ThrowPrefix));
            Patch(harmony, typeof(PlayerStates), "Hide", nameof(HidePrefix));
            Patch(harmony, typeof(RandomizeItemLocationsMode), "SetupRandomMode", nameof(RandomItemsPrefix));
            Patch(harmony, typeof(GrannyClone.BreakableGlass), "BreakGlass", nameof(GlassPrefix));
            harmony.Patch(AccessTools.Method(typeof(Inventory), "ClearEquippedItem"),
                prefix: new HarmonyMethod(typeof(GameHooks), nameof(ClearPrefix)), postfix: new HarmonyMethod(typeof(GameHooks), nameof(ClearPostfix)));
            Patch(harmony, typeof(StateInteractable), "StateChange", nameof(StatePrefix));
            Patch(harmony, typeof(FirstPersonController), "Update", nameof(InputPrefix));
            Patch(harmony, typeof(FirstPersonController), "FixedUpdate", nameof(InputPrefix));
            Patch(harmony, typeof(InteractionSystem), "ActionPressed", nameof(ActionPrefix));
            Patch(harmony, typeof(DragRigidbody), "Update", nameof(DragPrefix));
            Patch(harmony, typeof(PlayerStates), "ChangeState", nameof(PlayerStatePrefix));
            Patch(harmony, typeof(Global.Manager), "FreezeTime", nameof(FreezePrefix));
            harmony.Patch(AccessTools.Method(typeof(PoisonGas), "GasSet"), postfix: new HarmonyMethod(typeof(GameHooks), nameof(GasPostfix)));
            harmony.Patch(AccessTools.Method(typeof(NodeCanvas.Framework.Graph), "UpdateGraph"),
                prefix: new HarmonyMethod(typeof(GameHooks), nameof(GraphPrefix)), finalizer: new HarmonyMethod(typeof(GameHooks), nameof(GraphFinalizer)));
            harmony.Patch(AccessTools.Method(typeof(fpsHealth), "Damage", new[] { typeof(float) }),
                prefix: new HarmonyMethod(typeof(GameHooks), nameof(DamagePrefix)));
            Patch(harmony, typeof(fpsHealth), "DamageLessShake", nameof(DamagePrefix));
            Patch(harmony, typeof(CombinationLockCylinder), "SelectLock", nameof(LockPrefix));
            Patch(harmony, typeof(BulbPuzzle), "Switch", nameof(BulbPrefix));
            Patch(harmony, typeof(CombinationLock), "CheckSolution", nameof(LockSolutionPrefix));
            harmony.Patch(AccessTools.Method(typeof(PickLock), "StartLockPicking"),
                prefix: new HarmonyMethod(typeof(GameHooks), nameof(LocalUiPrefix)),
                postfix: new HarmonyMethod(typeof(GameHooks), nameof(PickStartPostfix)));
            Patch(harmony, typeof(PickLock), "solved", nameof(PickSolvedPrefix));
            Patch(harmony, typeof(PickLock), "pickBreak", nameof(PickBreakPrefix));
            Patch(harmony, typeof(LivesSystem), "Die", nameof(DiePrefix));
            Patch(harmony, typeof(NodeCanvas.Tasks.Actions.Quit), "OnExecute", nameof(QuitPrefix));
            harmony.Patch(AccessTools.Method(typeof(BearTrap), "Start"), postfix: new HarmonyMethod(typeof(GameHooks), nameof(TrapStartPostfix)));
            harmony.Patch(AccessTools.Method(typeof(BearTrap), "Disabled_Enter"), postfix: new HarmonyMethod(typeof(GameHooks), nameof(TrapDisabledPostfix)));
            Patch(harmony, typeof(fpsHealth), "GameOver", nameof(GameOverPrefix));
            Patch(harmony, typeof(FirstPersonController), "falling", nameof(FallingPrefix));
            Patch(harmony, typeof(FirstPersonController), "Start", nameof(PlayerStartPrefix));
            harmony.Patch(AccessTools.Method(typeof(LivesSystem), "RegainControl"),
                postfix: new HarmonyMethod(typeof(GameHooks), nameof(RegainControlPostfix)));
            Patch(harmony, typeof(CombinationLock), "ActivatePuzzleMode", nameof(LocalUiPrefix));
            harmony.Patch(AccessTools.Method(typeof(Humanoid.Agent.EnemyHunter.Tasks.GetClosestNavPtByDistance), "OnExecute"),
                postfix: new HarmonyMethod(typeof(GameHooks), nameof(SearchStartPostfix)));
            Patch(harmony, typeof(Humanoid.Agent.EnemyHunter.Tasks.CreateSearchPtList), "OnExecute", null, nameof(SearchListPostfix));
            harmony.Patch(AccessTools.Method(typeof(NodeCanvas.Framework.GraphOwner), "SendEvent", new[] { typeof(string) }),
                prefix: new HarmonyMethod(typeof(GameHooks), nameof(MenuEventPrefix)));
            // Puzzle machines: remember who set each one going; player-facing steps are theirs only.
            harmony.Patch(AccessTools.Method(typeof(NodeCanvas.Framework.Graph), "SendEvent", new[] { typeof(ParadoxNotion.EventData) }),
                prefix: new HarmonyMethod(typeof(GameHooks), nameof(GraphSendPrefix)));
            // Machines started by a puzzle part.
            harmony.Patch(AccessTools.Method(typeof(NodeCanvas.Framework.Graph), "StartGraph"),
                prefix: new HarmonyMethod(typeof(GameHooks), nameof(GraphEventPrefix)));
            foreach (string name in PlayerSteps)
            {
                var step = AccessTools.TypeByName(name);
                var execute = step == null ? null : AccessTools.DeclaredMethod(step, "OnExecute");
                if (execute != null) harmony.Patch(execute, prefix: new HarmonyMethod(typeof(GameHooks), nameof(PlayerStepPrefix)));
                else Plugin.Instance.Log("Hook unavailable: " + name + ".OnExecute");
            }
            harmony.Patch(AccessTools.DeclaredMethod(typeof(NodeCanvas.Tasks.Actions.TweenShake), "OnExecute"),
                prefix: new HarmonyMethod(typeof(GameHooks), nameof(ShakePrefix)));
            harmony.Patch(AccessTools.DeclaredMethod(typeof(GrannyClone.Tasks.PlaceItemTask), "OnExecute"),
                prefix: new HarmonyMethod(typeof(GameHooks), nameof(PlaceItemPrefix)), finalizer: new HarmonyMethod(typeof(GameHooks), nameof(PlaceItemFinalizer)));
            harmony.Patch(AccessTools.DeclaredMethod(typeof(GrannyClone.Tasks.PickupItem), "OnExecute"),
                prefix: new HarmonyMethod(typeof(GameHooks), nameof(PickupItemPrefix)));
            harmony.Patch(AccessTools.Method(typeof(global::EventTrigger), "OnTriggerEnter"), prefix: new HarmonyMethod(typeof(GameHooks), nameof(ZonePrefix)));
            harmony.Patch(AccessTools.Method(typeof(ProbabilityTrigger), "OnTriggerEnter"), prefix: new HarmonyMethod(typeof(GameHooks), nameof(ScarePrefix)));
            foreach (Type step in new[] { typeof(NodeCanvas.Tasks.Actions.ExecuteFunction_Multiplatform),
                typeof(NodeCanvas.Tasks.Actions.SetProperty_Multiplatform),
                typeof(NodeCanvas.Tasks.Actions.ActivateGameObject), typeof(NodeCanvas.Tasks.Actions.WarpTo) })
                harmony.Patch(AccessTools.DeclaredMethod(step, "OnExecute"), prefix: new HarmonyMethod(typeof(GameHooks), nameof(PlayerTargetPrefix)));
            harmony.Patch(AccessTools.DeclaredMethod(typeof(NodeCanvas.Tasks.Actions.WarpTo), "OnExecute"),
                postfix: new HarmonyMethod(typeof(GameHooks), nameof(WarpPostfix)));
            foreach (string name in CameraTweens)
            {
                var tween = AccessTools.TypeByName(name);
                var execute = tween == null ? null : AccessTools.DeclaredMethod(tween, "OnExecute");
                if (execute != null) harmony.Patch(execute, prefix: new HarmonyMethod(typeof(GameHooks), nameof(CameraTweenPrefix)));
            }
            foreach (Type input in new[] { typeof(NodeCanvas.Tasks.Conditions.RewiredCheckButton),
                typeof(NodeCanvas.Tasks.Conditions.CheckButtonInput),
                typeof(NodeCanvas.Tasks.Conditions.CheckKeyboardInput) })
                harmony.Patch(AccessTools.DeclaredMethod(input, "OnCheck"),
                    prefix: new HarmonyMethod(typeof(GameHooks), nameof(InputCheckPrefix)),
                    postfix: new HarmonyMethod(typeof(GameHooks), nameof(InputCheckPostfix)));
            harmony.Patch(AccessTools.DeclaredMethod(typeof(GrannyClone.Tasks.ClockTurn), "OnExecute"),
                prefix: new HarmonyMethod(typeof(GameHooks), nameof(ClockStartPrefix)));
            harmony.Patch(AccessTools.DeclaredMethod(typeof(GrannyClone.Tasks.ClockTurn), "OnUpdate"),
                prefix: new HarmonyMethod(typeof(GameHooks), nameof(ClockUpdatePrefix)));
            harmony.Patch(AccessTools.DeclaredMethod(typeof(GrannyClone.Tasks.ClockTurn), "OnStop"),
                prefix: new HarmonyMethod(typeof(GameHooks), nameof(ClockStopPrefix)),
                postfix: new HarmonyMethod(typeof(GameHooks), nameof(ClockStopPostfix)));
            Patch(harmony, typeof(MousePuzzleUIHandler), "ShowPuzzleUI", nameof(LocalUiPrefix));
            harmony.Patch(AccessTools.Method(typeof(NodeCanvas.Tasks.Actions.LoadSceneAsync), "OnUpdate"),
                prefix: new HarmonyMethod(typeof(GameHooks), nameof(LoadUpdatePrefix)));
            harmony.Patch(AccessTools.Method(typeof(LoadingScreenDisplay), "OnEnable"),
                postfix: new HarmonyMethod(typeof(GameHooks), nameof(LoadingScreenPostfix)));
            Plugin.Instance.Log("Installed " + count + " interaction hooks plus inventory, world-state and input hooks.");
        }
        /// <summary>Her search after a sound covers where the sound was (EnemyDirector), not points picked by chance.</summary>
        private static void SearchListPostfix(Humanoid.Agent.EnemyHunter.Tasks.CreateSearchPtList __instance)
        {
            if (Current != null) Current.SearchListMade(__instance);
        }

        // A sound the Nun went to: her search starts at the patrol point nearest it. Her own pick measures paths, and
        // where those fail it falls back to the first point in the scene.
        private static void SearchStartPostfix(Humanoid.Agent.EnemyHunter.Tasks.GetClosestNavPtByDistance __instance)
        {
            Vector3? sound = Players.EnemyDirector.TakeSoundSearch();
            if (!sound.HasValue || __instance.NavPts == null || __instance.NavPts.value == null) return;
            NavDist.NavPt best = null; float bestScore = float.MaxValue;
            foreach (var point in __instance.NavPts.value)
            {
                if (!point) continue;
                Vector3 offset = point.transform.position - sound.Value;
                float score = new Vector2(offset.x, offset.z).magnitude + Mathf.Abs(offset.y) * 4f; // Same floor first.
                if (score < bestScore) { bestScore = score; best = point; }
            }
            if (!best) return;
            __instance.Result.value = best;
            Plugin.Instance.Log("Nun searches from " + best.name + ", "
                + Vector3.Distance(best.transform.position, sound.Value).ToString("0.0") + "m from the sound");
        }
        // Steps of a puzzle machine that act on the local player, camera or screen.
        private static readonly string[] PlayerSteps =
        {
            "GrannyClone.Tasks.PlayerStateChange", "GrannyClone.Tasks.CameraMoveToPt", "GrannyClone.Tasks.PlayerInteractionUiToggle",
            "NodeCanvasTasks.BleedUi", "NodeCanvasTasks.Fade", "NodeCanvas.Tasks.Actions.FadeOut",
            "NodeCanvas.Tasks.Actions.TweenFov", "NodeCanvas.Tasks.Actions.AudioMixerSnapshotTask"
        };
        private static bool GraphEventPrefix(NodeCanvas.Framework.Graph __instance) { return Current == null || Current.GraphEvent(__instance); }
        private static bool GraphSendPrefix(NodeCanvas.Framework.Graph __instance, ParadoxNotion.EventData __0)
        {
            return Current == null || Current.GraphEventAllowed(__instance, __0 != null ? __0.name : null);
        }
        private static bool PlayerStepPrefix(NodeCanvas.Framework.ActionTask __instance)
        {
            // An ending everyone watches: each game runs it on its own player (the fade, the end screen).
            if (Current != null && Current.SharedEnding(__instance.ownerSystem as NodeCanvas.Framework.Graph)) return true;
            // The Nun's bed check pulling "the player" out: everyone under that bed is dragged out instead.
            if (Current != null && __instance is GrannyClone.Tasks.PlayerStateChange && Current.PullFromBed(__instance))
            {
                __instance.EndAction(true);
                return false;
            }
            var change = __instance as GrannyClone.Tasks.PlayerStateChange;
            if (Current != null && change != null)
            {
                var graph = __instance.ownerSystem as NodeCanvas.Framework.Graph;
                Current.PuzzlePlayerStep(graph, change.newState); // Whom the puzzle holds, so nobody else can take it over mid-animation.
                // Knocked down (or out) mid-animation: the knockdown has them now; the puzzle must not stand them back up.
                if (!Current.RemoteGraphStep && Current.Active && Current.Life.State != Gameplay.LifeState.Alive && Current.PuzzleMachine(graph))
                {
                    __instance.EndAction(true);
                    return false;
                }
            }
            if (Current == null || !Current.RemoteGraphStep) return true;
            __instance.EndAction(true); // Someone else's close-up: the machine carries on without us.
            return false;
        }
        private static bool ShakePrefix(NodeCanvas.Tasks.Actions.TweenShake __instance)
        {
            var local = Current == null ? null : Current.Local;
            if (!local || !Current.RemoteGraphStep || !__instance.agent || !__instance.agent.IsChildOf(((Component)local).transform)) return true;
            __instance.EndAction(true); // A camera shake for whoever is doing the puzzle; objects still shake for all.
            return false;
        }
        /// <summary>The held item leaves the inventory (used up, thrown, placed): logged with its caller, since a
        /// stray call from another player's puzzle would silently take this player's item.</summary>
        private static void ClearHeldPrefix(Inventory __instance)
        {
            if (Current == null || !__instance || !__instance.EquippedItem) return;
            var frames = new System.Diagnostics.StackTrace(2, false).GetFrames() ?? new System.Diagnostics.StackFrame[0];
            Plugin.Instance.Log("Held item cleared: " + __instance.EquippedItem.name + " via "
                + string.Join(" < ", frames.Take(6).Select(f => f.GetMethod() == null ? "?" : f.GetMethod().DeclaringType?.Name + "." + f.GetMethod().Name).ToArray()));
        }
        private static void PlaceItemPrefix(GrannyClone.Tasks.PlaceItemTask __instance, out Action __state)
        {
            __state = MessagesGranny.ClearHoldingItemPlayerEvent;
            var placing = Current == null || !__instance.agent ? null : Current.PuzzlePlacement(__instance.agent.ItemUsed);
            if (placing != null) MessagesGranny.ClearHoldingItemPlayerEvent = placing;
        }
        private static Exception PlaceItemFinalizer(Exception __exception, Action __state)
        {
            MessagesGranny.ClearHoldingItemPlayerEvent = __state;
            return __exception;
        }
        private static bool PickupItemPrefix(GrannyClone.Tasks.PickupItem __instance)
        {
            if (Current == null || !Current.Active || !Current.Ready) return true;
            // The puzzle lets go of the item; only the player who used it takes it back.
            var state = __instance.agent;
            var item = state ? state.ItemUsed : null;
            if (item) { item.PlacedInPuzzle = null; item.CantPickUp = false; }
            if (item && !Current.RemoteGraphStep) Current.Reclaim(item);
            if (state) state.ItemUsed = null;
            __instance.EndAction(true);
            return false;
        }
        private static bool ZonePrefix(global::EventTrigger __instance, Collider other)
        {
            if (Current == null) return true;
            if (!Current.ScareEntered(__instance, other)) return false; // A scare: the host fires it for the group.
            if (!Current.OpeningGate(__instance, other)) return false;  // The school entrance: the group goes in together.
            if (!Current.EndingZone(__instance, other)) return false;   // An ending: the group finishes together.
            Current.ZoneEntered(__instance, other);
            return true;
        }
        private static bool ScarePrefix(ProbabilityTrigger __instance, Collider you) { return Current == null || Current.ScareEntered(__instance, you); }

        // A puzzle close-up moves the camera of the player doing it, not everyone's.
        private static readonly string[] CameraTweens =
        {
            "NodeCanvas.Tasks.Actions.TweenTransform", "NodeCanvas.Tasks.Actions.TweenPositionLocal", "NodeCanvas.Tasks.Actions.TweenRotationLocal",
            "NodeCanvas.Tasks.Actions.TweenPosition", "NodeCanvas.Tasks.Actions.TweenRotation"
        };
        private static readonly PropertyInfo TaskAgent = AccessTools.Property(typeof(NodeCanvas.Framework.Task), "agent");
        private static bool CameraTweenPrefix(NodeCanvas.Framework.ActionTask __instance)
        {
            var local = Current == null ? null : Current.Local;
            if (!local || !Current.RemoteGraphStep) return true;
            Component target = TaskAgent == null ? null : TaskAgent.GetValue(__instance, null) as Component;
            if (!target || !target.transform.IsChildOf(((Component)local).transform)) return true;
            __instance.EndAction(true);
            return false;
        }

        // Someone else's puzzle or ending acting on "the player": laying them in the coffin, damaging them,
        // their end screen and interaction prompts, their lives. Those are theirs; world objects change for all.
        private static void WarpPostfix(NodeCanvas.Framework.ActionTask __instance)
        {
            var target = TaskAgent == null ? null : TaskAgent.GetValue(__instance, null) as Transform;
            if (Current != null) Current.SpreadOut(target);
        }

        private static bool PlayerTargetPrefix(NodeCanvas.Framework.ActionTask __instance)
        {
            if (Current == null || !Current.RemoteGraphStep) return true;
            Component target = TaskAgent == null ? null : TaskAgent.GetValue(__instance, null) as Component;
            if (!target || !LocalOnly(target)) return true;
            __instance.EndAction(true);
            return false;
        }
        private static bool LocalOnly(Component target)
        {
            var local = Current.Local;
            if (local && target.transform.IsChildOf(((Component)local).transform)) return true;
            if (target is LivesSystem) return true;
            for (Transform t = target.transform; t; t = t.parent)
            {
                var canvas = t.GetComponent<Canvas>();
                if (canvas && canvas.renderMode != RenderMode.WorldSpace) return true; // Screen UI.
            }
            return false;
        }

        // Keys only drive your own puzzle; someone else's follows their key presses.
        private static bool InputCheckPrefix(NodeCanvas.Framework.ConditionTask __instance, ref bool __result)
        {
            // Tests: a key press.
            if (Current != null && RuntimeTestDriver.PressPending && Current.LocalGraphOwned)
            {
                RuntimeTestDriver.PressPending = false;
                __result = true;
                return false;
            }
            var pressIn = RuntimeTestDriver.PressInGraph; // Tests: a key press in a named machine (credits, intro).
            if (pressIn != null && __instance.ownerSystem is NodeCanvas.Framework.Graph && ((NodeCanvas.Framework.Graph)__instance.ownerSystem).agent
                && ((NodeCanvas.Framework.Graph)__instance.ownerSystem).agent.name.IndexOf(pressIn, StringComparison.OrdinalIgnoreCase) >= 0)
            { RuntimeTestDriver.PressInGraph = null; __result = true; return false; }
            if (Current == null || !Current.RemoteGraphStep) return true;
            __result = Current.TakeGraphSignal(__instance, "input") || Current.ActorGone; // They left: as if they stepped away.
            return false;
        }
        private static void InputCheckPostfix(NodeCanvas.Framework.ConditionTask __instance, bool __result)
        {
            if (__result && Current != null && Current.LocalGraphOwned) Current.SignalGraph(__instance, "input");
        }

        // The clock hands minigame: played by whoever opened it; everyone else gets the outcome.
        private static readonly HashSet<GrannyClone.Tasks.ClockTurn> remoteClocks = new HashSet<GrannyClone.Tasks.ClockTurn>();
        internal static readonly HashSet<GrannyClone.Tasks.ClockTurn> localClocks = new HashSet<GrannyClone.Tasks.ClockTurn>();
        private static bool ClockStartPrefix(GrannyClone.Tasks.ClockTurn __instance)
        {
            remoteClocks.Remove(__instance); localClocks.Remove(__instance);
            if (Current != null && Current.RemoteGraphStep) { remoteClocks.Add(__instance); return false; } // No UI or input for us.
            if (Current != null && Current.LocalGraphOwned) localClocks.Add(__instance);
            return true;
        }
        private static bool ClockUpdatePrefix(GrannyClone.Tasks.ClockTurn __instance)
        {
            if (!remoteClocks.Contains(__instance)) return true;
            if (Current.TakeGraphSignal(__instance, "solved"))
            {
                __instance.hour = __instance.SolvedHour.value; __instance.minutes = __instance.SolvedMinutes.value;
                if (__instance.MinutesHand.value) __instance.MinutesHand.value.localEulerAngles = new Vector3(0f, 0f, 6f * __instance.minutes);
                if (__instance.HoursHand.value)
                    __instance.HoursHand.value.localEulerAngles = new Vector3(0f, 0f, 30f * __instance.hour + 0.5f * __instance.minutes);
                __instance.isSolved.value = true;
                __instance.EndAction();
            }
            else if (Current.TakeGraphSignal(__instance, "exit") || Current.ActorGone) __instance.EndAction(true);
            return false;
        }
        private static bool ClockStopPrefix(GrannyClone.Tasks.ClockTurn __instance) { return !remoteClocks.Remove(__instance); } // Nothing was registered.
        private static void ClockStopPostfix(GrannyClone.Tasks.ClockTurn __instance)
        {
            if (!localClocks.Remove(__instance) || Current == null) return;
            Current.SignalGraph(__instance, __instance.isSolved.value ? "solved" : "exit");
        }

        // Starting a co-op game together (see Session.MenuEvent / HoldActivation).
        private static bool MenuEventPrefix(NodeCanvas.Framework.GraphOwner __instance, string eventName)
        {
            return Current == null || Current.MenuEvent(__instance, eventName);
        }
        private static readonly FieldInfo LoadOperations = AccessTools.Field(typeof(NodeCanvas.Tasks.Actions.LoadSceneAsync), "asyncs");
        private static bool LoadUpdatePrefix(NodeCanvas.Tasks.Actions.LoadSceneAsync __instance)
        {
            if (Current == null) return true;
            var operations = LoadOperations == null ? null : LoadOperations.GetValue(__instance) as System.Collections.Generic.List<AsyncOperation>;
            bool loaded = operations != null && operations.Count > 0 && operations.All(o => o != null && o.progress >= 0.9f);
            return !Current.HoldActivation(loaded, __instance.SceneName.value);
        }
        private static void LoadingScreenPostfix(LoadingScreenDisplay __instance)
        {
            int variant = Current == null ? -1 : Current.LoadingVariant;
            var screens = __instance.LoadingScreens;
            if (variant < 0 || screens == null || variant >= screens.Length || !screens[variant]) return;
            for (int i = 0; i < screens.Length; i++) if (screens[i]) screens[i].SetActive(i == variant); // Everyone sees the same one.
        }
        /// <summary>The host's Nun lifted a bed: every player sees it lift.</summary>
        private static void FlipBedPostfix(HideBedInteractable __instance, Transform bed, float delay, float duration)
        {
            if (Current != null && bed == __instance.transform) Current.BedLifted(__instance, delay, duration);
        }
        /// <summary>Her jump scare: on the hunted player's own screen, not the host's.</summary>
        private static bool MenuNavigationPrefix() { return !Ui.PausePlayers.OwnsSelection; }
        private static bool AnomalyPrefix(AnomalyCollect __instance) { return Current == null || Current.AnomalyFound(__instance); }
        // The game saves the count only at the tenth: in co-op each find is saved at once (a session can end any time).
        private static void AnomalyPostfix() { if (Current != null && Current.Active) MyPlayerPrefs.ScheduleSave(); }
        private static void UnlocksPostfix() { if (Current != null) Current.UnlocksEarned(); }
        private static void TestSavePrefix(ref string curFilePath)
        {
            if (RuntimeTestDriver.Current == null || string.IsNullOrEmpty(curFilePath)) return;
            try
            {
                string own = System.IO.Path.Combine(System.IO.Path.GetDirectoryName(Application.dataPath), "nmm_test_save.dat");
                if (!System.IO.File.Exists(own) && System.IO.File.Exists(curFilePath)) System.IO.File.Copy(curFilePath, own);
                curFilePath = own;
            }
            catch (Exception ex) { Plugin.Instance.Log("Test save: " + ex.Message); }
        }

        /// <summary>Every noise she can react to (a knocked or dropped thing, a door, glass, a footstep, a whistle) reaches the
        /// Nun through the building, on her raytraced ears, and further the harder the game (NunEars).</summary>
        private static bool NoiseRadiusPrefix(Vector3 alertPos, ref float alertRadius)
        {
            if (Current == null || !Current.Active) return true;
            return Current.Ears.Judge(alertPos, ref alertRadius);
        }

        private static bool OnCameraPrefix(Humanoid.Agent.EnemyHunter.Tasks.IsOnCamera __instance, ref bool __result)
        {
            bool? seen = Current != null ? Current.OnHuntedScreen(__instance) : null;
            if (!seen.HasValue) return true;
            __result = seen.Value;
            return false;
        }
        /// <summary>The Nun's chase effects (screen jolt, motion blur, rumble) go to whoever she is after.</summary>
        private static bool HuntEffectPrefix(NodeCanvas.Framework.ActionTask __instance)
        {
            if (Current == null || !Current.HuntEffect(__instance)) return true;
            __instance.EndAction(true);
            return false;
        }
        /// <summary>Two under the bed she checks: she leaves the bed where it is and grabs them instead.</summary>
        private static bool FlipStepPrefix(GrannyClone.Tasks.FlipBed __instance)
        {
            if (Current == null || !Current.GrabFromBed(__instance)) return true;
            __instance.EndAction(true);
            return false;
        }
        /// <summary>Mid-grab, the bed's lift sound stays quiet: the bed does not move.</summary>
        private static bool BedSoundPrefix(NodeCanvas.Tasks.Actions.PlayAudioSource __instance)
        {
            if (Current == null || !__instance.agent || __instance.agent.name.IndexOf("Bed Lift", StringComparison.OrdinalIgnoreCase) < 0
                || !Current.Grabbing(__instance.ownerSystem as NodeCanvas.Framework.Graph)) return true;
            __instance.EndAction(true);
            return false;
        }
        /// <summary>Being dragged out from under a bed, the exit button does nothing.</summary>
        private static bool ExitHidingPrefix() { return Current == null || !Current.Life.DraggedOut; }

        /// <summary>Revolver shots fired by this player (a bullet used, not a click on an empty chamber), for teammates' view.</summary>
        internal static int ShotsFired;
        private static void FirePrefix(out int __state) { __state = RevolverInteractable.Bullets; }
        private static void FirePostfix(int __state) { if (RevolverInteractable.Bullets < __state) ShotsFired++; }

        private static void Patch(Harmony h, Type type, string name, string prefix, string postfix = null)
        {
            var method = AccessTools.Method(type, name);
            if (method == null) { Plugin.Instance.Log("Hook unavailable: " + type.FullName + "." + name); return; }
            h.Patch(method, prefix: prefix == null ? null : new HarmonyMethod(typeof(GameHooks), prefix),
                postfix: postfix == null ? null : new HarmonyMethod(typeof(GameHooks), postfix));
        }
        private static bool InteractionPrefix(Interactable __instance, MethodBase __originalMethod)
        {
            Voice.WorldSounds.Touched(__instance); // A cabinet's or locker's sound comes from it.
            return Current == null || Current.Interact(__instance, __originalMethod.Name == "Release");
        }
        private static bool DropPrefix(Inventory __instance) { return Current == null || Current.Drop(__instance); }
        private static bool ThrowPrefix(ThrowableItem __instance) { return Current == null || Current.ThrowItem(__instance); }
        private static void HidePrefix(ref Transform hidePt, ref Transform exitPt) { if (Current != null) Current.HidePlace(ref hidePt, ref exitPt); }
        // Item randomisation: the host's seed, so every peer scatters the items to the same places.
        private static void RandomItemsPrefix()
        {
            if (Current != null && Current.Active && Current.WorldSeed != 0) UnityEngine.Random.InitState(Current.WorldSeed);
        }
        private static bool GlassPrefix(GrannyClone.BreakableGlass __instance) { return Current == null || Current.GlassBreak(__instance); }
        private static void ClearPrefix(Inventory __instance, out ItemInteractable __state) { __state = __instance.EquippedItem; }
        private static void ClearPostfix(ItemInteractable __state) { if (Current != null) Current.Consumed(__state); }
        private static bool StatePrefix(StateInteractable __instance, int index)
        {
            Voice.WorldSounds.Touched(__instance);
            return Current == null || Current.StateChange(__instance, index);
        }
        private static bool InputPrefix()
        {
            return !RuntimeTestDriver.FreezePlayerInput;
        }
        private static bool ActionPrefix()
        {
            return InputPrefix() && (Current == null || !Current.Active || Current.PersonalScene || (Current.Ready && !Current.Life.BlocksInteraction));
        }
        private static void TrapStartPostfix(BearTrap __instance) { if (Current != null) Current.TrapStarted(__instance); }
        private static void TrapDisabledPostfix(BearTrap __instance) { if (Current != null) Current.TrapDisabled(__instance); }
        private static bool QuitPrefix(NodeCanvas.Framework.ActionTask __instance)
        {
            // Someone else's ending: the host brings everyone back.
            if (Current != null && Current.RemoteGraphStep) { __instance.EndAction(true); return false; }
            // Out of lives, watching: the team plays on (but an ending everyone is in ends it for them too).
            if (Current != null && Current.Active && Current.Life.State == Gameplay.LifeState.Spectating && !Current.TeamEndingRunning)
            {
                __instance.EndAction(true);
                return false;
            }
            if (Current != null) Current.GameEnding();
            return true;
        }
        private static bool DiePrefix(LivesSystem __instance) { return Current == null || Current.Life.OnDie(__instance); }
        private static bool GameOverPrefix() { return Current == null || Current.Life.OnGameOver(); }
        private static void PlayerStartPrefix(FirstPersonController __instance) { Players.RemotePlayers.PlayerStarting(__instance); }
        private static bool FallingPrefix(FirstPersonController __instance) { return Current == null || Current.Life.OnFalling(__instance); }
        private static void RegainControlPostfix() { if (Current != null) Current.Life.OnRegainControl(); }
        private static bool DragPrefix() { return InputPrefix() && (Current == null || !Current.Active || Current.IsHost); }
        private static bool PlayerStatePrefix(PlayerStates.States newState)
        {
            if (Current != null && !Current.RouteEnemyState(newState)) return false;
            return Current == null || !Current.Active || !Current.Replaying || Current.ReplayActor == Current.LocalId || Current.ReplayActor == 0;
        }
        private static bool FreezePrefix()
        {
            if (Current == null || !Current.Active) return true;
            Time.timeScale = 1f;
            return false;
        }
        private static void GasPostfix(bool toggle) { if (Current != null) Current.GasChanged(toggle); }
        private static void GraphPrefix(NodeCanvas.Framework.Graph __instance, out int __state)
        {
            __state = Current == null ? 0 : Current.EnterGraph(__instance);
        }
        private static Exception GraphFinalizer(Exception __exception, int __state) { if (Current != null) Current.ExitGraph(__state); return __exception; }
        private static bool DamagePrefix(float amount) { return Current == null || (Current.Life.AcceptDamage() && Current.RouteEnemyDamage(amount)); }
        private static bool LockPrefix(CombinationLockCylinder __instance) { return Current == null || Current.WorldCommand(__instance, "lockturn"); }
        private static bool BulbPrefix(BulbPuzzle __instance, string switchName)
        {
            return Current == null || Current.WorldCommand(__instance, "bulb", switchName);
        }
        private static bool HostOnlyPrefix() { return Current == null || !Current.Active || Current.IsHost; }

        // Lockpicking: played by whoever opened the keyhole; its outcome reaches everyone's keyhole machine.
        private static readonly HashSet<PickLock> localPicks = new HashSet<PickLock>();
        private static void PickStartPostfix(PickLock __instance)
        {
            var pick = __instance;
            if (Current != null && Current.Active && Current.RemoteGraphStep) { remotePicks[pick] = Current.EffectiveActor; return; }
            if (Current == null || !Current.Active || !localPicks.Add(pick)) return;
            // "End" goes out when the game raises it (after the pick slides out), not with "solved": the
            // keyhole machine misses one of two events that land in the same frame.
            UnityEngine.Events.UnityAction ended = null;
            ended = () =>
            {
                pick.endEvent.RemoveListener(ended);
                if (localPicks.Remove(pick) && Current != null) Current.MinigameResult(pick, "end");
            };
            pick.endEvent.AddListener(ended);
        }
        private static readonly Dictionary<PickLock, int> remotePicks = new Dictionary<PickLock, int>();
        internal static void PickFinished(PickLock pick) { remotePicks.Remove(pick); }
        /// <summary>That player left while picking a lock: end it unsolved here, so the keyhole frees up and the
        /// wrench stays in it for someone else.</summary>
        internal static void AbandonMinigames(int peer, Session session)
        {
            foreach (var pair in remotePicks.Where(x => x.Value == peer).ToArray())
            {
                remotePicks.Remove(pair.Key);
                if (pair.Key) session.AsPlayer(peer, () => pair.Key.endEvent.Invoke());
            }
        }
        private static void PickSolvedPrefix(PickLock __instance)
        {
            if (Current != null && localPicks.Contains(__instance)) Current.MinigameResult(__instance, "solved");
        }
        private static void PickBreakPrefix(PickLock __instance)
        {
            if (Current != null && localPicks.Contains(__instance)) Current.MinigameResult(__instance, "break");
        }

        // The padlock: cylinders turn through the host, which checks the combination and opens it for all.
        private static readonly FieldInfo LockCombination = AccessTools.Field(typeof(CombinationLock), "combination");
        private static readonly FieldInfo LockCylinders = AccessTools.Field(typeof(CombinationLock), "cylinder");
        private static bool LockSolutionPrefix(CombinationLock __instance, int id)
        {
            if (Current == null || !Current.Active) return true;
            if (!Current.IsHost || id != __instance.comboID) return false;
            var combination = LockCombination.GetValue(__instance) as int[];
            var cylinders = LockCylinders.GetValue(__instance) as CombinationLockCylinder[];
            if (combination == null || cylinders == null) return false;
            for (int i = 0; i < cylinders.Length; i++) if (!cylinders[i] || i >= combination.Length || cylinders[i].Number != combination[i]) return false;
            Current.MinigameResult(__instance, "solved");
            return false;
        }
        private static bool LocalUiPrefix() { return Current == null || !Current.Active || !Current.RemoteGraphStep; } // Someone else's puzzle UI is theirs.
    }
}
