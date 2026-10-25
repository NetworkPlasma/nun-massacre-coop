using System;
using System.Linq;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace NunMassacre.Multiplayer
{
    // Opt-in runtime diagnostics. Inert in ordinary play; enables repeatable two-process tests.
    internal sealed class RuntimeTestDriver : MonoBehaviour
    {
        internal static bool FreezePlayerInput;
        internal static bool TabHeld; // Test: show the team roster without a keyboard.
        private Plugin plugin;
        private float started;
        private float exitAfter;
        private string scene;
        private bool diagnostics, loaded, wrote;
        private string reportPath;
        private string testActions;
        private float nextReport;
        private int testStage;
        private GrannyClone.ItemInteractable picked;
        private bool previewSaved;
        private GrannyClone.CabinetsInteractable cabinet;
        private float lateJoinDelay;
        private bool lateJoinStarted;
        private string uiCapture;
        private bool perf;
        private string fingerprintDirectory;
        private double cpuMark, cpuTime; private int cpuFrames;
        internal static bool PressPending; // Tests: the next key check in our own puzzle succeeds.
        internal static string PressInGraph; // Tests: the next key check in a machine on an object with this name succeeds.
        private float nextPerf;
        private string capturedScene;
        private float sceneSeen;
        private int captureCount;
        private System.Collections.Generic.List<string[]> uiScript = new System.Collections.Generic.List<string[]>();
        private float scriptDelay; // How long a waitscene step held the script back.
        internal void Initialize(Plugin value)
        {
            plugin = value;
            started = Time.realtimeSinceStartup;
            string[] args = Environment.GetCommandLineArgs();
            diagnostics = args.Contains("-nmm-diagnostics");
            for (int i = 0; i < args.Length; i++)
            {
                if (args[i] == "-nmm-test-scene" && i + 1 < args.Length) scene = args[++i];
                if (args[i] == "-nmm-exit-after" && i + 1 < args.Length) float.TryParse(args[++i], out exitAfter);
                if (args[i] == "-nmm-report" && i + 1 < args.Length) reportPath = args[++i];
                if (args[i] == "-nmm-test-actions" && i + 1 < args.Length) testActions = args[++i];
                if (args[i] == "-nmm-test-latejoin" && i + 1 < args.Length) float.TryParse(args[++i], out lateJoinDelay);
                if (args[i] == "-nmm-ui-capture" && i + 1 < args.Length) uiCapture = args[++i];
                if (args[i] == "-nmm-perf") perf = Perf.Enabled = true;
                if (args[i] == "-nmm-fp-dir" && i + 1 < args.Length) fingerprintDirectory = args[++i];
                if (args[i] == "-nmm-test-loaddelay" && i + 1 < args.Length) float.TryParse(args[++i], out Session.TestLoadDelay);
                // "seconds:action:argument;..." with actions capture, click (button name), type (field=text), keys.
                if (args[i] == "-nmm-ui-script" && i + 1 < args.Length)
                    foreach (string step in args[++i].Split(';')) if (step.Trim().Length > 0) uiScript.Add(step.Trim().Split(new[] { ':' }, 3));
            }
            enabled = diagnostics || exitAfter > 0 || scene != null || reportPath != null || uiCapture != null || perf;
            FreezePlayerInput = testActions != null;
            if (enabled) Current = this;
        }

        /// <summary>The driver of an automated test game (null in a normal game).</summary>
        internal static RuntimeTestDriver Current { get; private set; }

        /// <summary>A step the host's test script sent this guest ("action:argument"), so both games act in step.</summary>
        internal void RunRemote(string step)
        {
            string[] parts = (step ?? "").Split(new[] { ':' }, 2);
            try { RunStep(parts[0], parts.Length > 1 ? parts[1] : ""); }
            catch (Exception ex) { plugin.Log("TEST remote step " + step + " failed: " + ex.Message); }
        }
        private void Update()
        {
            if (plugin == null || plugin.Session == null) return;
            float elapsed = Time.realtimeSinceStartup - started;
            if (lateJoinDelay > 0 && !lateJoinStarted && elapsed >= lateJoinDelay)
            {
                lateJoinStarted = true;
                plugin.Session.Join("127.0.0.1", plugin.Port.Value, Session.TestJoinKey);
            }
            if (scene != null && !loaded && elapsed > 12)
            {
                loaded = true;
                if (Application.CanStreamedLevelBeLoaded(scene)) SceneManager.LoadSceneAsync(scene);
                else plugin.Log("TEST scene unavailable: " + scene);
            }
            if (uiCapture != null) CaptureUi();
            if (holdNun) HoldNun();
            if (perf)
            {
                Perf.Tick();
                if (elapsed >= nextPerf)
                {
                    nextPerf = elapsed + 10f;
                    plugin.Log("PERF " + SceneManager.GetActiveScene().name + " " + Perf.Report() + " | " + plugin.Session.TopDirty());
                }
            }
            while (uiScript.Count > 0 && float.Parse(uiScript[0][0], System.Globalization.CultureInfo.InvariantCulture) + scriptDelay <= elapsed)
            {
                string[] step = uiScript[0];
                bool waiting = step[1] == "waitscene" ? !(SceneManager.GetActiveScene().name == (step.Length > 2 ? step[2] : "Nun scene") && plugin.Session.Local)
                    : step[1] == "waitpeer" ? !plugin.Session.Avatars.GetHead(int.Parse(step[2])).HasValue // A teammate shown in the level.
                    // A menu on screen.
                    : step[1] == "waitbutton" && !Resources.FindObjectsOfTypeAll<UnityEngine.UI.Selectable>().Any(b => b && b.gameObject.activeInHierarchy && b.name == step[2]);
                if (waiting)
                {
                    // Hold the script here until this level is loaded with the player in it; later steps move back as much.
                    scriptDelay = elapsed - float.Parse(step[0], System.Globalization.CultureInfo.InvariantCulture);
                    break;
                }
                uiScript.RemoveAt(0);
                if (step[1] == "waitscene" || step[1] == "waitpeer" || step[1] == "waitbutton")
                {
                    plugin.Log("TEST waited (" + step[1] + "): steps " + scriptDelay.ToString("0.0") + " s later");
                    continue;
                }
                try { RunStep(step[1], step.Length > 2 ? step[2] : ""); }
                catch (Exception ex) { plugin.Log("TEST UI step " + string.Join(":", step) + " failed: " + ex.Message); }
            }
            if (diagnostics && !wrote && elapsed > (scene == null ? 10 : 30))
            {
                wrote = true;
                plugin.Session.WriteDiagnostics();
            }
            if (exitAfter > 0 && elapsed > exitAfter)
            {
                plugin.Log("TEST exiting after " + exitAfter + " seconds.");
                Application.Quit();
            }
            if (reportPath != null && elapsed > nextReport)
            {
                nextReport = elapsed + 2;
                try { System.IO.File.WriteAllText(reportPath, plugin.Session.TestReport()); }
                catch (Exception ex) { plugin.Log("TEST report failed: " + ex.Message); }
            }
            if (reportPath != null && !previewSaved && elapsed > 35 && plugin.Session.Local)
            {
                previewSaved = true;
                try { plugin.Session.WriteModelPreview(System.IO.Path.ChangeExtension(reportPath, ".model.png")); }
                catch (Exception ex) { plugin.Log("TEST model preview failed: " + ex); }
            }
            if (testActions == "pickup" && plugin.Session.Ready && plugin.Session.Local)
            {
                if (testStage == 0 && elapsed > 40)
                {
                    // The nearest item not pinned in place by a follow script (e.g. a puzzle piece on its mount).
                    Vector3 from = ((Component)plugin.Session.Local).transform.position;
                    picked = Resources.FindObjectsOfTypeAll<GrannyClone.ItemInteractable>()
                        .Where(i => i && i.gameObject.scene.IsValid() && i.gameObject.activeInHierarchy && !i.CantPickUp && i.player == null && i.GetType() == typeof(GrannyClone.ItemInteractable)
                            && !(i.GetComponent<FollowPositionAndRotation>() && i.GetComponent<FollowPositionAndRotation>().enabled))
                        .OrderBy(i => Vector3.Distance(i.transform.position, from)).FirstOrDefault();
                    if (picked)
                    {
                        var player = plugin.Session.Local;
                        if (player.characterController) player.characterController.enabled = false;
                        ((Component)player).transform.position = picked.CenterPt + Vector3.back;
                        if (player.characterController) player.characterController.enabled = true;
                        plugin.Log("TEST approaching item " + picked.name);
                        testStage = 1;
                    }
                }
                if (testStage == 1 && elapsed > 43) { picked.Interact(); plugin.Log("TEST requested pickup " + picked.name); testStage = 2; }
                if (testStage == 2 && elapsed > 48)
                {
                    bool held = plugin.Session.Local.inventory.EquippedItem == picked;
                    plugin.Log("TEST pickup native inventory " + (held ? "PASS" : "FAIL"));
                    testStage = 3;
                }
                if (testStage == 3 && elapsed > 53)
                {
                    plugin.Session.Local.inventory.DropItem(); plugin.Log("TEST requested drop"); testStage = 4;
                }
                if (testStage == 4 && elapsed > 58)
                {
                    bool dropped = plugin.Session.Local.inventory.EquippedItem != picked && picked.transform.parent == null;
                    plugin.Log("TEST drop native inventory " + (dropped ? "PASS" : "FAIL")); testStage = 5;
                }
                if (testStage == 5 && elapsed > 62)
                {
                    cabinet = Resources.FindObjectsOfTypeAll<GrannyClone.CabinetsInteractable>()
                        .Where(c => c && c.gameObject.scene.IsValid() && c.gameObject.activeInHierarchy)
                        .OrderBy(c => Vector3.Distance(c.transform.position, ((Component)plugin.Session.Local).transform.position)).FirstOrDefault();
                    if (cabinet)
                    {
                        ((Component)plugin.Session.Local).transform.position = cabinet.CenterPt + Vector3.back;
                        plugin.Log("TEST approaching cabinet " + cabinet.name);
                        testStage = 6;
                    }
                }
                if (testStage == 6 && elapsed > 65) { cabinet.Interact(); plugin.Log("TEST requested cabinet interaction"); testStage = 7; }
                if (testStage == 7 && elapsed > 70)
                {
                    bool opened = (bool)HarmonyLib.AccessTools.Field(typeof(GrannyClone.CabinetsInteractable), "isOpen").GetValue(cabinet);
                    plugin.Log("TEST cabinet native state " + (opened ? "PASS" : "FAIL")); testStage = 8;
                }
            }
        }
        private void RunStep(string action, string argument)
        {
            if (action == "capture")
            {
                string prefix = uiCapture + "-" + argument;
                UiInspector.DumpHierarchy(prefix + ".ui.txt"); UiInspector.Screenshot(prefix + ".png", 1280, 720);
                plugin.Log("TEST UI captured " + prefix);
            }
            else if (action == "click")
            {
                var button = Resources.FindObjectsOfTypeAll<UnityEngine.UI.Button>().FirstOrDefault(b => b && b.gameObject.activeInHierarchy && b.name == argument);
                if (!button) throw new InvalidOperationException("no active button " + argument);
                button.onClick.Invoke();
                plugin.Log("TEST UI clicked " + argument);
            }
            else if (action == "toggle")
            {
                // Switch on an active toggle by name (a settings row, a menu entry made of a toggle).
                var toggle = Resources.FindObjectsOfTypeAll<UnityEngine.UI.Toggle>().FirstOrDefault(t => t && t.gameObject.activeInHierarchy && t.name == argument);
                if (!toggle) throw new InvalidOperationException("no active toggle " + argument);
                toggle.isOn = !toggle.isOn;
                plugin.Log("TEST UI toggled " + argument + " -> " + toggle.isOn);
            }
            else if (action == "type")
            {
                string[] parts = argument.Split(new[] { '=' }, 2);
                var field = Resources.FindObjectsOfTypeAll<UnityEngine.UI.InputField>().FirstOrDefault(f => f && f.gameObject.activeInHierarchy && f.transform.parent.name == parts[0]);
                if (!field) throw new InvalidOperationException("no active field " + parts[0]);
                field.text = parts[1]; field.onEndEdit.Invoke(parts[1]);
                plugin.Log("TEST UI typed " + argument);
            }
            else if (action == "activate")
            {
                var target = Resources.FindObjectsOfTypeAll<Transform>().FirstOrDefault(t => t && t.gameObject.scene.IsValid() && GraphDump.Path(t).EndsWith(argument, StringComparison.Ordinal));
                if (!target) throw new InvalidOperationException("no object " + argument);
                target.gameObject.SetActive(true);
                plugin.Log("TEST UI activated " + argument);
            }
            else if (action == "event")
            {
                foreach (var owner in Resources.FindObjectsOfTypeAll<NodeCanvas.Framework.GraphOwner>().Where(o => o && o.gameObject.scene.IsValid() && o.isRunning))
                    owner.SendEvent(argument);
                plugin.Log("TEST UI sent graph event " + argument);
            }
            else if (action == "watchnun")
            {
                // For "seconds", the player's view turned to the Nun's face each frame, just before it is drawn.
                watchNunUntil = Time.realtimeSinceStartup + float.Parse(argument, System.Globalization.CultureInfo.InvariantCulture);
                if (!watchingNun) { Camera.onPreCull += AimAtNun; watchingNun = true; }
                plugin.Log("TEST watching the Nun for " + argument + " s");
            }
            else if (action == "pausemenu")
            {
                // "1" opens the pause menu (as Escape does), "0" closes it.
                var states = plugin.Session.Local.playerStates;
                states.ChangeState(argument == "1" ? GrannyClone.PlayerStates.States.Menu : GrannyClone.PlayerStates.States.Normal);
                plugin.Log("TEST pause menu " + argument);
            }
            else if (action == "voicevol")
            {
                // "name|level": a teammate's voice volume, as the pause menu's slider sets it.
                var parts = argument.Split('|');
                plugin.Session.Voice.SetVolumeOf(parts[0], float.Parse(parts[1], System.Globalization.CultureInfo.InvariantCulture));
                plugin.Log("TEST voice volume " + parts[0] + " " + parts[1] + " -> " + plugin.Session.Voice.VolumeOf(parts[0]));
            }
            else if (action == "uitree")
            {
                // "path|depth": a UI object's children, their rects and components (to fit new UI into the game's).
                var parts = argument.Split('|');
                int depth = parts.Length > 1 ? int.Parse(parts[1]) : 3;
                var root = Resources.FindObjectsOfTypeAll<Transform>().FirstOrDefault(x => x && x.gameObject.scene.IsValid() && GraphDump.Path(x).EndsWith(parts[0], StringComparison.Ordinal));
                if (!root) throw new InvalidOperationException("no UI at " + parts[0]);
                var lines = new System.Collections.Generic.List<string>();
                Action<Transform, int> walk = null;
                walk = (x, d) =>
                {
                    var rt = x as RectTransform;
                    var text = x.GetComponent<UnityEngine.UI.Text>();
                    lines.Add(new string(' ', d * 2) + x.name + (x.gameObject.activeSelf ? "" : " (off)") + (rt ? " pos=" + rt.anchoredPosition.ToString("0") + " size=" + rt.rect.size.ToString("0") + " anchors=" + rt.anchorMin.ToString("0.00") + "-" + rt.anchorMax.ToString("0.00") : "")
                        + " [" + string.Join(",", x.GetComponents<Component>().Where(c => c && !(c is Transform)).Select(c => c.GetType().Name).ToArray()) + "]" + (text ? " \"" + text.text.Replace("\n", " ") + "\" font=" + (text.font ? text.font.name : "-") + " " + text.fontSize : ""));
                    if (d < depth) for (int i = 0; i < x.childCount; i++) walk(x.GetChild(i), d + 1);
                };
                walk(root, 0);
                plugin.Log("TEST UITREE " + argument + " ; " + string.Join(" ; ", lines.ToArray()));
            }
            else if (action == "kneeshare")
            {
                Players.RemotePlayers.TestKneeHelperShare = float.Parse(argument, System.Globalization.CultureInfo.InvariantCulture);
                plugin.Log("TEST knee share " + argument);
            }
            else if (action == "hinge")
            {
                Players.AvatarRig.TestHingeKnees = Players.AvatarRig.TestFlatFeet = argument == "1";
                plugin.Log("TEST hinge " + argument);
            }
            else if (action == "burst")
            {
                // "seconds|fps": the screen as the player sees it, several times a second, from now (an ending, a scare).
                var parts = argument.Split('|');
                float seconds = float.Parse(parts[0], System.Globalization.CultureInfo.InvariantCulture),
                    fps = parts.Length > 1 ? float.Parse(parts[1], System.Globalization.CultureInfo.InvariantCulture) : 5f;
                StartCoroutine(Burst(seconds, fps));
            }
            else if (action == "shot") { ScreenCapture.CaptureScreenshot(uiCapture + "-" + argument + ".png"); plugin.Log("TEST screenshot " + argument); }
            else if (action == "tab") TabHeld = argument == "1";
            else if (action == "preview") plugin.Session.WriteModelPreview(argument);
            else if (action == "probe") Probe();
            else if (action == "damage") plugin.Session.Local.health.Damage(float.Parse(argument, System.Globalization.CultureInfo.InvariantCulture));
            else if (action == "bleed") plugin.Session.Life.TestSetBleed(float.Parse(argument, System.Globalization.CultureInfo.InvariantCulture));
            else if (action == "character") { plugin.Character.Value = argument; plugin.Log("TEST character " + argument); }
            else if (action == "lives") LivesSystem.instance.currentLives = int.Parse(argument);
            else if (action == "hold") Gameplay.CoopLife.TestHoldAction = argument == "1";
            else if (action == "ventspots")
            {
                // The vents' enter/exit triggers, with their trigger boxes (where a vent is).
                var spots = Resources.FindObjectsOfTypeAll<GrannyClone.VentEnterExitTrigger>().Where(v => v && v.gameObject.scene.IsValid() && v.gameObject.activeInHierarchy)
                    .Select(v => { var c = v.GetComponent<Collider>(); return GraphDump.Path(v.transform) + " at=" + v.transform.position.ToString("0.00") + (c ? " bounds=" + c.bounds.min.ToString("0.00") + ".." + c.bounds.max.ToString("0.00") : ""); });
                plugin.Log("TEST VENTS ; " + string.Join(" ; ", spots.ToArray()));
            }
            else if (action == "console") plugin.Console.Run(argument, true);
            else if (action == "room")
            {
                // "x,y,z;x,y,z..." (spots separated by /): free distance each way at chest height, and the floor under it.
                foreach (string spot in argument.Split('/'))
                {
                    var v = spot.Split(',');
                    var at = new Vector3(float.Parse(v[0], System.Globalization.CultureInfo.InvariantCulture),
                        float.Parse(v[1], System.Globalization.CultureInfo.InvariantCulture),
                        float.Parse(v[2], System.Globalization.CultureInfo.InvariantCulture));
                    int mask = Physics.DefaultRaycastLayers & ~(1 << LayerMask.NameToLayer("Player"));
                    Func<Vector3,
                        string> free = d => { RaycastHit h; return Physics.Raycast(at + Vector3.up * 0.8f, d, out h, 30f, mask, QueryTriggerInteraction.Ignore) ? h.distance.ToString("0.0") : ">30"; };
                    RaycastHit floor;
                    string ground = Physics.Raycast(at + Vector3.up * 1f, Vector3.down, out floor, 4f, mask, QueryTriggerInteraction.Ignore)
                        ? floor.point.y.ToString("0.00") : "none";
                    plugin.Log("TEST ROOM " + spot + " +x=" + free(Vector3.right) + " -x=" + free(Vector3.left) + " +z="
                        + free(Vector3.forward) + " -z=" + free(Vector3.back) + " floor=" + ground);
                }
            }
            else if (action == "record")
            {
                // "peer|dx,dy,dz|name|seconds|fps": film a teammate from an offset that follows them (frames to fp dir/rec-name).
                string[] parts = argument.Split('|');
                var v = parts[1].Split(',');
                var offset = new Vector3(float.Parse(v[0], System.Globalization.CultureInfo.InvariantCulture),
                    float.Parse(v[1], System.Globalization.CultureInfo.InvariantCulture),
                    float.Parse(v[2], System.Globalization.CultureInfo.InvariantCulture));
                // The peer may instead be a point "@x,y,z": film that spot (a body left where someone bled out).
                if (parts[0].StartsWith("@", StringComparison.Ordinal))
                {
                    var at = parts[0].Substring(1).Split(',');
                    if (!recordSpot) recordSpot = new GameObject("NMM_RecordSpot").transform;
                    recordSpot.position = new Vector3(float.Parse(at[0], System.Globalization.CultureInfo.InvariantCulture),
                        float.Parse(at[1], System.Globalization.CultureInfo.InvariantCulture),
                        float.Parse(at[2], System.Globalization.CultureInfo.InvariantCulture));
                    parts[0] = "-1";
                }
                // Optional: aim this high above them.
                recordAim = parts.Length > 5 ? float.Parse(parts[5], System.Globalization.CultureInfo.InvariantCulture) : -1f;
                StartCoroutine(Record(int.Parse(parts[0]), offset, parts[2],
                    float.Parse(parts[3], System.Globalization.CultureInfo.InvariantCulture),
                    float.Parse(parts[4], System.Globalization.CultureInfo.InvariantCulture)));
            }
            else if (action == "apartlog") Gameplay.CoopLife.TestKeepApartLog = argument == "1";
            else if (action == "fsmapi")
            {
                var lines = new System.Collections.Generic.List<string>();
                foreach (var type in new[] { typeof(NodeCanvas.StateMachines.FSMOwner), typeof(NodeCanvas.StateMachines.FSM),
                    typeof(NodeCanvas.StateMachines.FSMState) })
                    foreach (var m in type.GetMethods(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.DeclaredOnly))
                        if (m.Name.IndexOf("State", StringComparison.OrdinalIgnoreCase) >= 0 || m.Name.IndexOf("Enter", StringComparison.Ordinal) >= 0)
                            lines.Add(type.Name + "." + m.Name + "("
                                + string.Join(",", m.GetParameters().Select(x => x.ParameterType.Name + " " + x.Name).ToArray())
                                + ")");
                plugin.Log("TEST FSMAPI ; " + string.Join(" ; ", lines.ToArray()));
            }
            else if (action == "find")
            {
                // Every object in the level whose path contains this text: where it is, its size, whether it is on.
                plugin.Log("TEST FIND " + argument + " ; " + string.Join(" ; ", Resources.FindObjectsOfTypeAll<Transform>()
                    .Where(x => x && x.gameObject.scene.IsValid() && GraphDump.Path(x).IndexOf(argument, StringComparison.OrdinalIgnoreCase) >= 0 && (x.parent == null || GraphDump.Path(x.parent).IndexOf(argument, StringComparison.OrdinalIgnoreCase) < 0))
                    .Take(30).Select(x => { var r = x.GetComponentInChildren<Renderer>(); return GraphDump.Path(x) + " at " + x.position.ToString("0.0") + (r ? " box " + r.bounds.min.ToString("0.0") + ".." + r.bounds.max.ToString("0.0") : "") + " on=" + x.gameObject.activeInHierarchy; }).ToArray()));
            }
            else if (action == "inspect")
            {
                // An object by path suffix: where it is (world and local), its body, and what is on it.
                var target = Resources.FindObjectsOfTypeAll<Transform>().FirstOrDefault(x => x && x.gameObject.scene.IsValid() && x.gameObject.activeInHierarchy && GraphDump.Path(x).EndsWith(argument, StringComparison.Ordinal));
                if (!target) throw new InvalidOperationException("nothing at " + argument);
                var body = target.GetComponent<Rigidbody>();
                plugin.Log("TEST INSPECT " + GraphDump.Path(target) + " world=" + target.position.ToString("0.000") + " " + target.eulerAngles.ToString("0.0") + " local=" + target.localPosition.ToString("0.000") + " " + target.localEulerAngles.ToString("0.0")
                    + " body=" + (body ? "kinematic=" + body.isKinematic + " gravity=" + body.useGravity + " sleeping=" + body.IsSleeping() + " v=" + body.velocity.magnitude.ToString("0.000") : "none")
                    + " comps=" + string.Join(",", target.GetComponents<Component>().Where(c => c).Select(c => c.GetType().Name).ToArray()));
                foreach (var animator in target.GetComponentsInChildren<Animator>(true).Take(3))
                {
                    var state = animator.isActiveAndEnabled && animator.runtimeAnimatorController
                        ? animator.GetCurrentAnimatorStateInfo(0) : default(AnimatorStateInfo);
                    var clips = animator.isActiveAndEnabled && animator.runtimeAnimatorController
                        ? animator.GetCurrentAnimatorClipInfo(0) : new AnimatorClipInfo[0];
                    plugin.Log("TEST INSPECT  animator " + GraphDump.Path(animator.transform) + " enabled=" + animator.enabled + " active=" + animator.isActiveAndEnabled + " speed=" + animator.speed + " controller=" + (animator.runtimeAnimatorController ? animator.runtimeAnimatorController.name : "-")
                        + " state=" + state.fullPathHash + " t=" + state.normalizedTime.ToString("0.00") + " clip=" + (clips.Length > 0 && clips[0].clip ? clips[0].clip.name : "-") + " culling=" + animator.cullingMode + " id=" + (plugin.Session.WorldIdFor(animator) != null));
                }
                var follow = target.GetComponent<FollowPositionAndRotation>();
                if (follow && follow.target)
                {
                    var goal = follow.target;
                    plugin.Log("TEST INSPECT  follows " + GraphDump.Path(goal) + " enabled=" + follow.enabled + " world=" + goal.position.ToString("0.000") + " " + goal.eulerAngles.ToString("0.0") + " id=" + (plugin.Session.WorldIdFor(goal) ?? "-")
                        + " comps=" + string.Join(",", goal.GetComponents<Component>().Where(c => c).Select(c => c.GetType().Name + (c is Behaviour ? (((Behaviour)c).enabled ? "" : "(off)") : "")).ToArray())
                        + " parent comps=" + (goal.parent ? string.Join(",", goal.parent.GetComponents<Component>().Where(c => c).Select(c => c.GetType().Name + (c is Behaviour ? (((Behaviour)c).enabled ? "" : "(off)") : "") + (c is Rigidbody ? "(kin=" + ((Rigidbody)c).isKinematic + ")" : "")).ToArray()) : "-"));
                }
            }
            else if (action == "whereis")
            {
                // Each active item with this name: where it is and the named things around it (to describe the room).
                foreach (var item in Resources.FindObjectsOfTypeAll<GrannyClone.ItemInteractable>().Where(i => i && i.gameObject.scene.IsValid() && i.gameObject.activeInHierarchy && i.name.StartsWith(argument, StringComparison.OrdinalIgnoreCase)))
                {
                    Vector3 at = item.transform.position;
                    var near = Physics.OverlapSphere(at, 3f, ~0, QueryTriggerInteraction.Collide)
                        .Select(c => c.transform).Where(t => t && !t.IsChildOf(item.transform))
                        .Select(t => t.name).Distinct().Take(25).ToArray();
                    var mesh = Resources.FindObjectsOfTypeAll<MeshRenderer>().Where(r => r && r.gameObject.scene.IsValid() && r.gameObject.activeInHierarchy && r.bounds.Contains(at) && r.bounds.size.magnitude > 4f)
                        .OrderBy(r => r.bounds.size.magnitude).Take(4).Select(r => GraphDump.Path(r.transform)).ToArray();
                    plugin.Log("TEST WHERE " + GraphDump.Path(item.transform) + " at=" + at.ToString("0.0") + " | near: "
                        + string.Join(", ", near) + " | inside: " + string.Join(" ; ", mesh));
                }
            }
            else if (action == "lowspot")
            {
                // "minx,miny,minz|maxx,maxy,maxz": a spot in that box with a floor below and a ceiling 0.7-1.3 m above it
                // (inside a vent); the player is moved there.
                string[] parts = argument.Split('|');
                Func<string,
                    Vector3> point = text => { var v = text.Split(','); return new Vector3(float.Parse(v[0], System.Globalization.CultureInfo.InvariantCulture), float.Parse(v[1], System.Globalization.CultureInfo.InvariantCulture), float.Parse(v[2], System.Globalization.CultureInfo.InvariantCulture)); };
                Vector3 min = point(parts[0]), max = point(parts[1]);
                int mask = Physics.DefaultRaycastLayers & ~(1 << LayerMask.NameToLayer("Player"));
                Vector3? found = null; float gap = 0f;
                for (float x = min.x; x <= max.x && found == null; x += 0.25f)
                    for (float z = min.z; z <= max.z && found == null; z += 0.25f)
                        for (float y = min.y; y <= max.y && found == null; y += 0.3f)
                        {
                            RaycastHit down, up;
                            var at = new Vector3(x, y, z);
                            if (Physics.CheckSphere(at, 0.1f, mask, QueryTriggerInteraction.Ignore)) continue; // Inside a wall.
                            if (!Physics.Raycast(at, Vector3.down, out down, 0.6f, mask, QueryTriggerInteraction.Ignore)) continue;
                            if (!Physics.Raycast(down.point + Vector3.up * 0.05f, Vector3.up, out up, 2f, mask, QueryTriggerInteraction.Ignore)) continue;
                            float height = up.point.y - down.point.y;
                            if (height < 0.7f || height > 1.3f) continue;
                            found = down.point; gap = height;
                        }
                if (found == null) throw new InvalidOperationException("no low spot in " + argument);
                Teleport(found.Value + Vector3.up * 0.05f, found.Value + Vector3.up * 0.5f + Vector3.right * 3f);
                plugin.Log("TEST lowspot " + found.Value.ToString("0.00") + " height " + gap.ToString("0.00"));
            }
            else if (action == "tp")
            {
                // "x,y,z|x,y,z": stand here, looking there.
                string[] parts = argument.Split('|');
                Func<string,
                    Vector3> point = text => { var v = text.Split(','); return new Vector3(float.Parse(v[0], System.Globalization.CultureInfo.InvariantCulture), float.Parse(v[1], System.Globalization.CultureInfo.InvariantCulture), float.Parse(v[2], System.Globalization.CultureInfo.InvariantCulture)); };
                Teleport(point(parts[0]), point(parts[1]));
                plugin.Log("TEST tp " + argument);
            }
            else if (action == "bindings")
            {
                // Which keys and buttons the game binds to each action (keyboard, mouse, gamepad).
                var player = Rewired.ReInput.players.GetPlayer(0);
                var lines = new System.Collections.Generic.List<string>();
                foreach (var map in player.controllers.maps.GetAllMaps())
                    foreach (var element in map.AllMaps)
                    {
                        var act = Rewired.ReInput.mapping.GetAction(element.actionId);
                        lines.Add(map.controllerType + ":" + (act != null ? act.name : "?") + "="
                            + element.elementIdentifierName
                            + (element.axisContribution == Rewired.Pole.Negative ? "(-)" : "")
                            + (map.enabled ? "" : "[off]"));
                    }
                plugin.Log("TEST BINDINGS " + string.Join(" ; ", lines.ToArray()));
            }
            else if (action == "maps")
            {
                var player = Rewired.ReInput.players.GetPlayer(0);
                plugin.Log("TEST MAPS " + string.Join(" ", player.controllers.maps.GetAllMaps().Select(m => m.controllerType + "/" + Rewired.ReInput.mapping.GetMapCategory(m.categoryId).name + "=" + (m.enabled ? "on" : "off")).ToArray())
                    + " action=" + player.GetButton("Action") + " jump=" + player.GetButton("Jump") + " moveV=" + player.GetAxis("Move Vertical") + " pstate=" + plugin.Session.Local.playerStates.StateCheck() + " life=" + plugin.Session.Life.State);
            }
            else if (action == "nextview")
            {
                plugin.Session.Life.TestNextView();
                plugin.Log("TEST nextview free=" + plugin.Session.Life.FreeCamera + " eyes="
                    + plugin.Session.Life.WatchingEyes + " watching=" + plugin.Session.Life.WatchingName);
            }
            else if (action == "freeze") { FreezePlayerInput = argument == "1"; plugin.Log("TEST freeze " + argument); }
            else if (action == "stand")
            {
                // Out of a crouch (if crouched), after the player is out of anywhere low.
                var controller = plugin.Session.Local.firstPersonController;
                if (controller.IsCrouching) HarmonyLib.AccessTools.Method(typeof(GrannyClone.FirstPersonController), "Crouch").Invoke(controller, null);
                plugin.Log("TEST stand crouching=" + controller.IsCrouching);
            }
            else if (action == "crouch")
            {
                // "crouch" toggles; "crouch:1" or "crouch:0" only changes it when it is not that already.
                var controller = plugin.Session.Local.firstPersonController;
                bool want = argument == "1", toggle = string.IsNullOrEmpty(argument) || (argument != "1" && argument != "0");
                bool was = controller.IsCrouching;
                if (toggle) want = !was;
                if (was != want) { if (want) controller.Crouch(); else controller.Stand(); } // The game's Crouch() only ever crouches.
                plugin.Log("TEST crouched (asked " + (toggle ? "toggle" : argument) + ", was " + was + ", now "
                    + controller.IsCrouching + ", mid " + controller.middleOfCrouch + ")");
            }
            else if (action == "crawl") { Gameplay.CoopLife.TestCrawl = argument == "1"; plugin.Log("TEST crawl " + argument); }
            else if (action == "invulnerable") { Gameplay.CoopLife.TestInvulnerable = argument == "1"; plugin.Log("TEST invulnerable " + argument); }
            else if (action == "goto")
            {
                Vector3? center = plugin.Session.Avatars.GetCenter(int.Parse(argument));
                if (!center.HasValue) throw new InvalidOperationException("no avatar " + argument);
                Teleport(new Vector3(center.Value.x + 0.8f, center.Value.y, center.Value.z), center.Value);
            }
            else if (action == "portrait")
            {
                // Render a teammate's avatar from a temporary camera 2.2 m in front of it.
                Transform body = plugin.Session.Avatars.GetTransform(int.Parse(argument));
                if (!body) throw new InvalidOperationException("no avatar " + argument);
                var go = new GameObject("NMM_TestPortrait");
                var cam = go.AddComponent<Camera>();
                cam.fieldOfView = 45f; cam.nearClipPlane = 0.05f;
                Vector3 chest = body.position + Vector3.up * 1.0f;
                int blockers = ~(1 << LayerMask.NameToLayer("Player")) & ~(1 << LayerMask.NameToLayer("CameraTrigger"));
                go.transform.position = body.position + body.forward * 2.2f + Vector3.up * 1.2f;
                for (int angle = 0; angle < 360; angle += 30)
                {
                    Vector3 spot = body.position + Quaternion.Euler(0f, angle, 0f) * body.forward * 2.2f + Vector3.up * 1.2f;
                    if (!Physics.Linecast(spot, chest, blockers, QueryTriggerInteraction.Ignore)
                        && !Physics.CheckSphere(spot, 0.2f, blockers, QueryTriggerInteraction.Ignore))
                    {
                        go.transform.position = spot;
                        break;
                    }
                }
                go.transform.LookAt(body.position + Vector3.up * 0.95f);
                var lamp = go.AddComponent<Light>(); lamp.type = LightType.Point; lamp.range = 6f; lamp.intensity = 1.5f;
                var rt = new RenderTexture(640, 720, 24);
                var tex = new Texture2D(640, 720, TextureFormat.RGB24, false);
                cam.targetTexture = rt; cam.Render();
                var prior = RenderTexture.active; RenderTexture.active = rt;
                tex.ReadPixels(new Rect(0, 0, 640, 720), 0, 0); tex.Apply(); RenderTexture.active = prior;
                System.IO.File.WriteAllBytes(uiCapture + "-portrait-" + argument + ".png", ImageConversion.EncodeToPNG(tex));
                cam.targetTexture = null; UnityEngine.Object.Destroy(go); UnityEngine.Object.Destroy(tex); rt.Release(); UnityEngine.Object.Destroy(rt);
                plugin.Log("TEST portrait of " + argument);
            }
            else if (action == "face")
            {
                // Stand 1.6 m in front of a teammate on their floor, looking at them.
                Transform body = plugin.Session.Avatars.GetTransform(int.Parse(argument));
                if (!body) throw new InvalidOperationException("no avatar " + argument);
                Teleport(body.position + body.forward * 1.6f + Vector3.up * 0.05f, body.position + Vector3.up * 1.0f);
                var local = plugin.Session.Local;
                if (local && local.camera) local.camera.transform.localRotation = Quaternion.Euler(12f, 0f, 0f);
            }
            else if (action == "bait")
            {
                var nun = Nun();
                Teleport(nun.position + nun.forward * 2.2f, nun.position + Vector3.up * 1.5f);
            }
            else if (action == "nun")
            {
                var nun = Nun();
                var owner = nun.GetComponent<NodeCanvas.Framework.GraphOwner>();
                var graph = owner.graph;
                var stateProperty = graph.GetType().GetProperty("currentStateName");
                var board = owner.blackboard;
                System.Func<string,
                    string> value = name => { var v = board.GetVariable(name); return v == null ? "?" : (v.value is UnityEngine.Object ? ((UnityEngine.Object)v.value ? ((UnityEngine.Object)v.value).name : "null") : Convert.ToString(v.value)); };
                var agent = nun.GetComponent<UnityEngine.AI.NavMeshAgent>();
                var animator = nun.GetComponentInChildren<Animator>();
                var player = board.GetVariable("Player") == null ? null : board.GetVariable("Player").value as Transform;
                plugin.Log("TEST NUN pos=" + nun.position + " yaw=" + nun.eulerAngles.y.ToString("0") + " target=" + plugin.Session.EnemyTarget + " state=" + (stateProperty == null ? "?" : stateProperty.GetValue(graph, null)) + " Player=" + value("Player") + " dist=" + (player ? Vector3.Distance(player.position, nun.position).ToString("0.0") : "-")
                    + " CanAttack=" + value("CanAttack") + " vent=" + value("In Vent Player") + " gas=" + value("Poison Gas Status") + " IgnoreSight=" + value("IgnoreSight") + " AttackDist=" + value("Attack Dist") + " Speed=" + value("Speed")
                    + " agent=" + (agent ? agent.enabled + "/" + agent.velocity.magnitude.ToString("0.0") + "/dest" + agent.destination : "none") + " attack=" + (animator && animator.runtimeAnimatorController ? animator.GetBool("Attack").ToString() : "?"));
            }
            else if (action == "nunplay")
            {
                // "state": stop her behaviour where she is and play that animator state (to see what an animation looks like).
                var owner = Nun().GetComponent<NodeCanvas.Framework.GraphOwner>();
                if (owner) owner.PauseBehaviour();
                var agent = Nun().GetComponent<UnityEngine.AI.NavMeshAgent>();
                if (agent && agent.enabled && agent.isOnNavMesh) agent.isStopped = true;
                var animator = Nun().GetComponentsInChildren<Animator>(true).First(a => a.runtimeAnimatorController);
                int layer = Enumerable.Range(0, animator.layerCount).FirstOrDefault(l => animator.HasState(l, Animator.StringToHash(argument)));
                bool found = animator.HasState(layer, Animator.StringToHash(argument));
                if (found) animator.SetLayerWeight(layer, 1f); // Cutscene layers sit at weight 0 until a step turns them up.
                animator.CrossFadeInFixedTime(argument, 0.2f, found ? layer : -1);
                plugin.Log("TEST Nun plays " + argument + " (found: " + found + ", layer " + layer + " " + animator.GetLayerName(layer) + ") at " + animator.transform.position.ToString("0.0")
                    + " layers=" + string.Join(",", Enumerable.Range(0, animator.layerCount).Select(l => animator.GetLayerName(l) + ":" + animator.GetLayerWeight(l).ToString("0.0")).ToArray()));
            }
            else if (action == "nunclips")
            {
                // The Nun's animations (clip names and lengths) and animator parameters: what she can be made to do.
                foreach (var animator in Nun().GetComponentsInChildren<Animator>(true).Where(a => a.runtimeAnimatorController))
                    plugin.Log("TEST NUNCLIPS " + animator.name + " layers=" + animator.layerCount + " params=" + string.Join(",", animator.parameters.Select(p => p.name).ToArray())
                        + " ; clips: " + string.Join(", ", animator.runtimeAnimatorController.animationClips.Select(c => c.name + "(" + c.length.ToString("0.0") + "s)").Distinct().ToArray()));
            }
            else if (action == "nunat")
            {
                // "x,y,z": put the Nun there (on her navigation mesh), as if she had walked there.
                var v = argument.Split(',');
                var at = new Vector3(float.Parse(v[0], System.Globalization.CultureInfo.InvariantCulture),
                    float.Parse(v[1], System.Globalization.CultureInfo.InvariantCulture),
                    float.Parse(v[2], System.Globalization.CultureInfo.InvariantCulture));
                var agent = Nun().GetComponent<UnityEngine.AI.NavMeshAgent>();
                UnityEngine.AI.NavMeshHit hit;
                if (agent && UnityEngine.AI.NavMesh.SamplePosition(at, out hit, 3f, agent.areaMask)) at = hit.position;
                if (agent && agent.enabled) agent.Warp(at); else Nun().position = at;
                plugin.Log("TEST Nun put at " + at.ToString("0.0"));
            }
            else if (action == "traps")
                plugin.Log("TEST TRAPS "
                    + string.Join(" ; ", Resources.FindObjectsOfTypeAll<GrannyClone.BearTrap>().Where(t => t && t.gameObject.scene.IsValid()).Select(t => GraphDump.Path(t.transform) + " active=" + t.gameObject.activeInHierarchy + " at " + t.transform.position).ToArray()));
            else if (action == "spawntrap")
            {
                var prefab = Resources.FindObjectsOfTypeAll<GameObject>().First(g => g && !g.scene.IsValid() && g.transform.parent == null && g.GetComponentInChildren<GrannyClone.BearTrap>(true));
                var body = ((Component)plugin.Session.Local).transform;
                UnityEngine.Object.Instantiate(prefab, body.position + body.forward * 3f + Vector3.up * 0.5f, Quaternion.identity);
                plugin.Log("TEST spawned " + prefab.name);
            }
            else if (action == "music")
            {
                var root = Nun().parent ? Nun().parent : Nun();
                plugin.Log("TEST MUSIC playing: " + string.Join(", ", root.GetComponentsInChildren<AudioSource>(true).Where(a => a.isPlaying).Select(a => a.name + (a.spatialBlend < 0.01f ? "(2D)" : "") + "@" + a.volume.ToString("0.0")).ToArray()));
            }
            else if (action == "voice")
            {
                var voice = plugin.Session.Voice;
                plugin.Log("TEST VOICE sent=" + voice.PacketsSent + " received=" + voice.PacketsReceived + " transmitting=" + voice.Transmitting + " level=" + voice.InputLevel.ToString("0.000")
                    + " hiding=" + plugin.Session.LocalHiding + " alerted=" + (Time.unscaledTime - voice.LastAlert).ToString("0.0") + "s ago"
                    + " audioRate=" + AudioSettings.outputSampleRate + " " + string.Join(" ", plugin.Session.Players.Keys.Where(id => id != plugin.Session.LocalId).Select(id => "peer" + id + ":talking=" + voice.IsTalking(id) + ",played=" + voice.PlayedSamples(id)).ToArray()));
            }
            else if (action == "hide")
            {
                // Crawl under the nearest bed through its own interaction.
                var body = ((Component)plugin.Session.Local).transform;
                var bed = Resources.FindObjectsOfTypeAll<GrannyClone.HideBedInteractable>().Where(b => b && b.gameObject.scene.IsValid() && b.gameObject.activeInHierarchy)
                    .OrderBy(b => Vector3.Distance(b.transform.position, body.position)).FirstOrDefault();
                if (!bed) throw new InvalidOperationException("no bed");
                bed.Interact();
                plugin.Log("TEST hiding under " + GraphDump.Path(bed.transform));
            }
            else if (action == "shout") { plugin.Session.Shout(0.3f); plugin.Log("TEST shout"); }
            else if (action == "call") { plugin.Session.TestShout(int.Parse(argument)); plugin.Log("TEST call from peer " + argument); }
            else if (action == "follow") { Players.EnemyDirector.TestFollow = argument == "1"; plugin.Log("TEST follow " + argument); }
            else if (action == "nunvent")
            {
                // The vent branch's condition: which variable it reads and what it holds now.
                var fsm = Nun().GetComponent<NodeCanvas.StateMachines.FSMOwner>();
                var lines = new System.Collections.Generic.List<string>();
                foreach (var node in fsm.graph.allNodes)
                    foreach (var connection in node.outConnections)
                    {
                        var condition = (connection as NodeCanvas.StateMachines.FSMConnection) != null
                            ? ((NodeCanvas.StateMachines.FSMConnection)connection).condition : null;
                        string info = condition != null ? condition.summaryInfo ?? "" : "";
                        bool vent = info.IndexOf("Vent", StringComparison.OrdinalIgnoreCase) >= 0
                            || (connection.targetNode != null && connection.targetNode.name.IndexOf("vent", StringComparison.OrdinalIgnoreCase) >= 0);
                        if (!vent) continue;
                        lines.Add(node.name + " -> " + connection.targetNode.name + " active=" + connection.isActive + ": "
                            + System.Text.RegularExpressions.Regex.Replace(info, "<.*?>", ""));
                    }
                var variable = fsm.blackboard.GetVariable("In Vent Player");
                plugin.Log("TEST NUNVENT owner var=" + (variable != null ? variable.value : "-") + " ; " + string.Join(" ; ", lines.Take(20).ToArray()));
            }
            else if (action == "nunstates") StartCoroutine(WatchNunStates(float.Parse(argument, System.Globalization.CultureInfo.InvariantCulture)));
            else if (action == "act")
            {
                // "peer|kind": play a teammate's one-off on their avatar here (throw, hit, pickup, take, open) to film it.
                var parts = argument.Split('|');
                int peer = int.Parse(parts[0]);
                var avatars = plugin.Session.Avatars;
                Transform body = avatars.GetTransform(peer);
                if (!body) throw new InvalidOperationException("no avatar " + peer);
                Vector3 ahead = body.position + body.forward * 0.6f;
                switch (parts[1])
                {
                    case "throw": avatars.Throw(peer); break;
                    case "hit": avatars.Hurt(peer); break;
                    case "pickup": avatars.Reach(peer, ahead + Vector3.up * 0.05f); break;
                    case "take": avatars.Reach(peer, ahead + Vector3.up * 1.1f); break;
                    case "open": avatars.Reach(peer, ahead + Vector3.up * 1.0f, true); break;
                }
                plugin.Log("TEST act " + argument + " playing=" + avatars.OneOffOf(peer));
            }
            else if (action == "peek")
            {
                // "-1..1": peek round a corner as Q/E would (the game's head offset; input is frozen in tests, so it holds).
                var local = plugin.Session.Local;
                var most = (float)HarmonyLib.AccessTools.Field(typeof(GrannyClone.FirstPersonController), "peakDist").GetValue(local.firstPersonController);
                var head = local.head.localPosition;
                local.head.localPosition = new Vector3(float.Parse(argument, System.Globalization.CultureInfo.InvariantCulture) * most, head.y, head.z);
                plugin.Log("TEST peek " + argument + " (peek distance " + most.ToString("0.00") + " m)");
            }
            else if (action == "posestate") plugin.Log("TEST POSE " + plugin.Session.Avatars.PoseOf(int.Parse(argument)));
            else if (action == "peekstate") plugin.Log("TEST PEEK " + plugin.Session.Avatars.PeekOf(int.Parse(argument)));
            else if (action == "nuncolliders")
            {
                // The Nun's colliders here (what a bullet can hit) and the revolver's hit layers.
                Transform root = Nun().parent ? Nun().parent : Nun();
                var gun = Resources.FindObjectsOfTypeAll<GrannyClone.RevolverInteractable>().FirstOrDefault(g => g && g.gameObject.scene.IsValid());
                var nunLayer = gun
                    ? (LayerMask)HarmonyLib.AccessTools.Field(typeof(GrannyClone.RevolverInteractable), "NunLayer").GetValue(gun)
                    : default(LayerMask);
                var damageLayers = gun
                    ? (LayerMask)HarmonyLib.AccessTools.Field(typeof(GrannyClone.RevolverInteractable), "damageLayers").GetValue(gun)
                    : default(LayerMask);
                plugin.Log("TEST NUNBODY " + plugin.Session.NunBodyInfo);
                plugin.Log("TEST NUNCOLLIDERS nunLayer=" + nunLayer.value + " damageLayers=" + damageLayers.value + " ; "
                    + string.Join(" ; ", root.GetComponentsInChildren<Collider>(true).Where(c => c.CompareTag("Enemy")).Select(c => (c is CapsuleCollider ? "capsule center=" + ((CapsuleCollider)c).center.ToString("0.00") + " height=" + ((CapsuleCollider)c).height.ToString("0.00") + " radius=" + ((CapsuleCollider)c).radius.ToString("0.00") + " " : "") + c.name + " " + c.GetType().Name + " enabled=" + c.enabled + " active=" + c.gameObject.activeInHierarchy + " layer=" + LayerMask.LayerToName(c.gameObject.layer) + " tag=" + c.tag + " trigger=" + c.isTrigger + " at " + c.bounds.center.ToString("0.0")).ToArray()));
            }
            else if (action == "huntinfo")
            {
                // Who the Nun is after, who has her chase effects, how loud her chase music is here, and her nearest search spots.
                var session = plugin.Session;
                var sorter = UnityEngine.Object.FindObjectOfType<NavDist.NavPtDistSorter>();
                string spots = sorter == null ? "-" : string.Join(", ", sorter.SearchPts.Take(3).Select(p => p.name + "@" + p.transform.position.ToString("0") + " " + p.DistancePlayer.DistanceNavMesh.ToString("0.0") + "m").ToArray());
                var blur = Resources.FindObjectsOfTypeAll<Transform>().FirstOrDefault(t => t && t.name == "Motion Blur" && t.gameObject.scene.IsValid());
                plugin.Log("TEST HUNT hunted=" + session.HuntedId + " fx=" + session.HuntFxPeer + " musicGain=" + session.NunAudioGain.ToString("0.00") + " blur=" + (blur ? blur.gameObject.activeSelf.ToString() : "-")
                    + " me=" + (session.Local ? ((Component)session.Local).transform.position.ToString("0") : "-") + " spots: " + spots);
            }
            else if (action == "nunevent")
            {
                Nun().GetComponent<NodeCanvas.Framework.GraphOwner>().SendEvent(argument);
                plugin.Log("TEST Nun event " + argument);
            }
            else if (action == "spot" || action == "hear")
            {
                // Stand on the navmesh about <argument> m from the Nun: in her sight (spot) or behind a wall (hear).
                var nun = Nun();
                string[] options = argument.Split(',');
                float wanted = float.Parse(options[0], System.Globalization.CultureInfo.InvariantCulture);
                string side = options.Length > 1 ? options[1] : ""; // "front" or "back" of where she faces.
                Vector3 eye = nun.position + Vector3.up * 1.4f;
                int walls = LayerMask.GetMask("Wall", "Ground", "Ceiling", "Furniture", "BlockSight", "Door");
                Vector3? best = null; float bestError = float.MaxValue;
                for (int i = 0; i < 400; i++)
                {
                    Vector3 probe = nun.position + Quaternion.Euler(0f, i * 37f, 0f) * Vector3.forward * (wanted * (0.6f + 0.8f * ((i * 7) % 11) / 10f));
                    UnityEngine.AI.NavMeshHit hit;
                    if (!UnityEngine.AI.NavMesh.SamplePosition(probe, out hit, 2f, UnityEngine.AI.NavMesh.AllAreas)
                        || Mathf.Abs(hit.position.y - nun.position.y) > 1.5f)
                        continue;
                    bool clear = !Physics.Linecast(eye, hit.position + Vector3.up * 1.5f, walls, QueryTriggerInteraction.Ignore);
                    if (clear != (action == "spot")) continue;
                    float angle = Vector3.Angle(nun.forward, Vector3.ProjectOnPlane(hit.position - nun.position, Vector3.up));
                    if ((side == "front" && angle > 50f) || (side == "back" && angle < 110f)) continue;
                    float error = Mathf.Abs(Vector3.Distance(hit.position, nun.position) - wanted);
                    if (error < bestError) { bestError = error; best = hit.position; }
                }
                if (!best.HasValue) throw new InvalidOperationException("no " + action + " position");
                Teleport(best.Value + Vector3.up * 0.1f, nun.position + Vector3.up * 1.5f);
                plugin.Log("TEST " + action + " " + Vector3.Distance(best.Value, nun.position).ToString("0.0") + "m from the Nun");
            }
            else if (action == "lights")
            {
                var local = plugin.Session.Local;
                plugin.Log("TEST LIGHTS "
                    + string.Join(" ; ", ((Component)local).GetComponentsInChildren<Light>(true).Select(l => GraphDump.Path(l.transform) + " type=" + l.type + " enabled=" + l.enabled + " active=" + l.gameObject.activeInHierarchy + " intensity=" + l.intensity.ToString("0.00") + " range=" + l.range.ToString("0.0") + " cull=" + l.cullingMask).ToArray()));
            }
            else if (action == "acoustics")
            {
                var voice = plugin.Session.Voice;
                plugin.Log("TEST ACOUSTICS " + string.Join(" ; ", plugin.Session.Players.Keys.Where(id => id != plugin.Session.LocalId).Select(id =>
                {
                    var room = voice.RoomOf(id); var playback = voice.PlaybackOf(id);
                    return "peer" + id + " vent=" + plugin.Session.Players[id].vent + (room == null ? " room=-" : " size=" + room.Size.ToString("0.0") + "m width=" + room.Width.ToString("0.0") + "m length=" + room.Length.ToString("0.0") + "m height=" + room.Height.ToString("0.0") + "m enclosed=" + room.Enclosure.ToString("0.00") + " nearest=" + room.Nearest.ToString("0.0") + "m farthest=" + room.Farthest.ToString("0.0") + "m sky=" + room.Sky)
                        + (playback == null ? "" : " reverb room=" + playback.Reverb.room.ToString("0") + "mB reflections=" + playback.Reverb.reflectionsLevel.ToString("0") + "mB tail=" + playback.Reverb.reverbLevel.ToString("0") + "mB decay=" + playback.Reverb.decayTime.ToString("0.0") + "s highpass=" + playback.HighPass.cutoffFrequency.ToString("0") + " lowpass=" + playback.LowPass.cutoffFrequency.ToString("0") + " echo=" + playback.Echo.enabled + (playback.Echo.enabled ? " delay=" + playback.Echo.delay.ToString("0") + "ms wet=" + playback.Echo.wetMix.ToString("0.00") + " feedback=" + playback.Echo.decayRatio.ToString("0.00") : "") + " gain=" + playback.Gain.ToString("0.000") + " volume=" + voice.VolumeOf(voice.NameOf(id)).ToString("0.00"));
                }).ToArray()));
            }
            else if (action == "speak") { plugin.Session.Voice.Speak(); plugin.Log("TEST speak"); }
            else if (action == "cpu")
            {
                // "label": processor time used so far (all threads) and the frame rate since the last "cpu".
                var process = System.Diagnostics.Process.GetCurrentProcess();
                double cpu = process.TotalProcessorTime.TotalSeconds, now = Time.realtimeSinceStartup;
                plugin.Log("TEST CPU " + argument + " "
                    + (cpuMark > 0 ? ((cpu - cpuMark) / (now - cpuTime) * 100.0).ToString("0") + "% of a core, " + ((Time.frameCount - cpuFrames) / (now - cpuTime)).ToString("0") + " fps" : "start"));
                cpuMark = cpu; cpuTime = now; cpuFrames = Time.frameCount;
            }
            else if (action == "oldvent") { Voice.VoiceChat.TestOldVent = argument == "1"; plugin.Log("TEST old vent sound " + argument); }
            else if (action == "ownecho") { Voice.VoiceChat.TestNoOwnEcho = argument != "1"; plugin.Log("TEST own echo " + argument); }
            else if (action == "transmission")
            {
                // "lf|hf": how much further than the library has it sound gets through walls (applies on the next build).
                var parts = argument.Split('|');
                Voice.RaytracedAcoustics.TransmissionLF = float.Parse(parts[0], System.Globalization.CultureInfo.InvariantCulture);
                Voice.RaytracedAcoustics.TransmissionHF = float.Parse(parts[1], System.Globalization.CultureInfo.InvariantCulture);
                plugin.Log("TEST transmission " + argument);
            }
            else if (action == "raytraced") { Voice.VoiceChat.TestPlainAcoustics = argument != "1"; plugin.Log("TEST raytraced audio " + argument); }
            else if (action == "quiet") { ListenerTap.Quiet = argument == "1"; plugin.Log("TEST quiet " + argument); }
            else if (action == "quietexcept")
            {
                // "a,b": everything muted but the voices and sounds whose path has one of these.
                ListenerTap.Quiet = true; ListenerTap.Keep = argument.Split(',');
                foreach (var source in UnityEngine.Object.FindObjectsOfType<AudioSource>())
                    if (source && ListenerTap.Keep.Any(k => GraphDump.Path(source.transform).Contains(k))) source.mute = false;
                plugin.Log("TEST quiet except " + argument);
            }
            else if (action == "listen")
            {
                // "name|seconds": what the listener hears (the whole mix) for that long, to a WAV beside the fingerprints.
                var parts = argument.Split('|');
                var ears = UnityEngine.Object.FindObjectOfType<AudioListener>();
                if (!ears) throw new InvalidOperationException("no audio listener");
                string directory = fingerprintDirectory ?? System.IO.Path.GetDirectoryName(reportPath ?? uiCapture ?? ".");
                var tap = ears.gameObject.AddComponent<ListenerTap>();
                tap.Begin(System.IO.Path.Combine(directory, "listen-" + parts[0] + ".wav"),
                    float.Parse(parts[1], System.Globalization.CultureInfo.InvariantCulture), plugin.Log);
            }
            else if (action == "playsound")
            {
                // "path suffix": the nearest active sound source whose path ends so, played (its clip, or one of the
                // others under the same object).
                Vector3 ear = Camera.main ? Camera.main.transform.position : Vector3.zero;
                var source = UnityEngine.Object.FindObjectsOfType<AudioSource>().Where(x => x && GraphDump.Path(x.transform).EndsWith(argument, StringComparison.Ordinal))
                    .OrderBy(x => Vector3.Distance(x.transform.position, ear)).FirstOrDefault();
                if (!source) throw new InvalidOperationException("no sound " + argument);
                var clip = source.clip ?? source.GetComponentsInParent<AudioSource>(true).Select(x => x.clip).FirstOrDefault(c => c);
                if (!clip) throw new InvalidOperationException("no clip for " + argument);
                source.PlayOneShot(clip, 1f);
                plugin.Log("TEST played " + GraphDump.Path(source.transform) + " (" + clip.name + ") "
                    + Vector3.Distance(source.transform.position, ear).ToString("0.0") + " m away");
            }
            else if (action == "interactables")
            {
                // "x,y,z|radius": the state-changing things (cabinets, drawers, lockers, doors) near a point.
                var parts = argument.Split('|'); var v = parts[0].Split(',');
                var at = new Vector3(float.Parse(v[0], System.Globalization.CultureInfo.InvariantCulture),
                    float.Parse(v[1], System.Globalization.CultureInfo.InvariantCulture),
                    float.Parse(v[2], System.Globalization.CultureInfo.InvariantCulture));
                float radius = float.Parse(parts[1], System.Globalization.CultureInfo.InvariantCulture);
                plugin.Log("TEST NEAR " + argument + " ; " + string.Join(" ; ", Resources.FindObjectsOfTypeAll<GrannyClone.Interactable>().Where(i => i && i.gameObject.scene.IsValid() && i.gameObject.activeInHierarchy && Vector3.Distance(i.transform.position, at) <= radius)
                    .OrderBy(i => Vector3.Distance(i.transform.position, at)).Take(25).Select(i => i.GetType().Name + " " + GraphDump.Path(i.transform) + " at " + i.transform.position.ToString("0.0")).ToArray()));
            }
            else if (action == "flatlist")
            {
                // Every flat (2D) sound source in the level, by name: how many, under what, which mixer, which clip.
                var flats = Resources.FindObjectsOfTypeAll<AudioSource>().Where(x => x && x.gameObject.scene.IsValid() && x.spatialBlend < 0.5f).ToList();
                plugin.Log("TEST FLAT " + flats.Count + " ; " + string.Join(" ; ", flats.GroupBy(x => x.name + " | under " + (x.transform.parent ? x.transform.parent.name : "-") + " | " + (x.outputAudioMixerGroup ? x.outputAudioMixerGroup.name : "-"))
                    .OrderByDescending(g => g.Count()).Select(g => g.Count() + "x " + g.Key + " | e.g. " + GraphDump.Path(g.First().transform) + " clip=" + (g.First().clip ? g.First().clip.name : "-")).ToArray()));
            }
            else if (action == "anatomy")
            {
                // An object's makeup: components down to a depth, its animators' parameters and layers, its navigation agent.
                var root = Resources.FindObjectsOfTypeAll<Transform>().FirstOrDefault(x => x && x.gameObject.scene.IsValid() && GraphDump.Path(x).EndsWith(argument, StringComparison.Ordinal));
                if (!root) throw new InvalidOperationException("nothing at " + argument);
                var lines = new System.Collections.Generic.List<string>();
                foreach (var animator in root.GetComponentsInChildren<Animator>(true))
                    lines.Add("ANIMATOR " + GraphDump.Path(animator.transform) + " controller=" + (animator.runtimeAnimatorController ? animator.runtimeAnimatorController.name : "-") + " root=" + animator.applyRootMotion + " params: " + string.Join(", ", animator.parameters.Select(x => x.name + ":" + x.type).ToArray())
                        + " layers: " + string.Join(", ", Enumerable.Range(0, animator.layerCount).Select(i => animator.GetLayerName(i) + "=" + animator.GetLayerWeight(i).ToString("0.0")).ToArray())
                        + " clips: " + (animator.runtimeAnimatorController ? string.Join(", ", animator.runtimeAnimatorController.animationClips.Select(c => c.name).Distinct().Take(60).ToArray()) : "-"));
                foreach (var agent in root.GetComponentsInChildren<UnityEngine.AI.NavMeshAgent>(true))
                    lines.Add("AGENT " + GraphDump.Path(agent.transform) + " speed=" + agent.speed + " accel="
                        + agent.acceleration + " angular=" + agent.angularSpeed + " radius=" + agent.radius + " height="
                        + agent.height + " enabled=" + agent.enabled);
                foreach (var t in root.GetComponentsInChildren<Transform>(true).Where(x => x.GetComponents<Component>().Length > 1).Take(80))
                    lines.Add("  " + GraphDump.Path(t).Replace(GraphDump.Path(root), "~") + " ["
                        + string.Join(",", t.GetComponents<Component>().Where(c => c && !(c is Transform)).Select(c => c.GetType().Name).ToArray())
                        + "]");
                plugin.Log("TEST ANATOMY " + argument + " ; " + string.Join(" ; ", lines.ToArray()));
            }
            else if (action == "soundinfo")
            {
                Vector3 ear = Camera.main ? Camera.main.transform.position : Vector3.zero;
                foreach (var source in UnityEngine.Object.FindObjectsOfType<AudioSource>().Where(x => x && GraphDump.Path(x.transform).EndsWith(argument, StringComparison.Ordinal)).OrderBy(x => Vector3.Distance(x.transform.position, ear)).Take(2))
                    plugin.Log("TEST SOUND " + plugin.Session.Voice.World.Inspect(source, ear));
            }
            else if (action == "audioscan")
            {
                // Every sound source in the level: how many, 3D or flat, playing, with the game's own filters and reverb zones.
                var sources = Resources.FindObjectsOfTypeAll<AudioSource>().Where(x => x && x.gameObject.scene.IsValid()).ToList();
                Vector3 ear = Camera.main ? Camera.main.transform.position : Vector3.zero;
                Func<AudioSource, string> one = x => GraphDump.Path(x.transform) + " clip=" + (x.clip ? x.clip.name : "-") + " blend=" + x.spatialBlend.ToString("0.0") + " vol=" + x.volume.ToString("0.00") + " max=" + x.maxDistance.ToString("0") + " roll=" + x.rolloffMode + " mixer=" + (x.outputAudioMixerGroup ? x.outputAudioMixerGroup.name : "-") + " zoneMix=" + x.reverbZoneMix.ToString("0.0")
                    + (x.GetComponent<AudioLowPassFilter>() ? " LPF" : "") + (x.GetComponent<AudioReverbFilter>() ? " REV" : "") + (x.GetComponent<AudioEchoFilter>() ? " ECHO" : "") + " on=" + x.isActiveAndEnabled + " playing=" + x.isPlaying + " d=" + Vector3.Distance(x.transform.position, ear).ToString("0");
                plugin.Log("TEST AUDIO " + sources.Count + " sources: " + sources.Count(x => x.spatialBlend >= 0.5f) + " 3D, " + sources.Count(x => x.isActiveAndEnabled) + " active, " + sources.Count(x => x.isPlaying) + " playing; zones " + Resources.FindObjectsOfTypeAll<AudioReverbZone>().Count(z => z && z.gameObject.scene.IsValid())
                    + "; mixers " + string.Join(",", sources.Where(x => x.outputAudioMixerGroup).Select(x => x.outputAudioMixerGroup.audioMixer.name + "/" + x.outputAudioMixerGroup.name).Distinct().ToArray())
                    + " ; " + string.Join(" ; ", sources.Where(x => x.isActiveAndEnabled && (argument.Length == 0 || char.IsDigit(argument[0]) || GraphDump.Path(x.transform).Contains(argument))).OrderBy(x => x.isPlaying ? 0 : 1).ThenBy(x => Vector3.Distance(x.transform.position, ear)).Take(argument.Length > 0 && char.IsDigit(argument[0]) ? int.Parse(argument) : 40).Select(one).ToArray()));
                foreach (var zone in Resources.FindObjectsOfTypeAll<AudioReverbZone>().Where(z => z && z.gameObject.scene.IsValid()).Take(10))
                    plugin.Log("TEST AUDIO zone " + GraphDump.Path(zone.transform) + " at " + zone.transform.position
                        + " min=" + zone.minDistance + " max=" + zone.maxDistance + " preset=" + zone.reverbPreset + " on="
                        + zone.isActiveAndEnabled);
            }
            else if (action == "raytrace") plugin.Log("TEST RAYTRACE " + argument + " ; " + plugin.Session.Voice.DescribeRaytraced());
            else if (action == "space")
            {
                // "label@x,y,z/label@x,y,z...": how a voice there (floor point; the mouth 1.6 m up) would sound from 4 m and 15 m.
                foreach (string spot in argument.Split('/'))
                {
                    string[] parts = spot.Split('@');
                    var v = parts[1].Split(',');
                    var mouth = new Vector3(float.Parse(v[0], System.Globalization.CultureInfo.InvariantCulture),
                        float.Parse(v[1], System.Globalization.CultureInfo.InvariantCulture) + 1.6f,
                        float.Parse(v[2], System.Globalization.CultureInfo.InvariantCulture));
                    plugin.Log("TEST SPACE " + parts[0] + " near: " + plugin.Session.Voice.DescribeSpace(mouth, 4f));
                    plugin.Log("TEST SPACE " + parts[0] + " far: " + plugin.Session.Voice.DescribeSpace(mouth, 15f));
                }
            }
            else if (action == "vent") { plugin.Session.TestVent = argument == "1"; plugin.Log("TEST vent " + argument); }
            else if (action == "items")
            {
                Vector3 from = ((Component)plugin.Session.Local).transform.position;
                plugin.Log("TEST ITEMS " + string.Join(" ; ", Resources.FindObjectsOfTypeAll<GrannyClone.ItemInteractable>()
                    .Where(i => i && i.gameObject.scene.IsValid() && i.gameObject.activeInHierarchy && !i.CantPickUp && i.player == null && i.GetType() == typeof(GrannyClone.ItemInteractable))
                    .OrderBy(i => Vector3.Distance(i.transform.position, from)).Take(12)
                    .Select(i => GraphDump.Path(i.transform) + " d=" + Vector3.Distance(i.transform.position, from).ToString("0") + " follow=" + (i.GetComponent<FollowPositionAndRotation>() ? i.GetComponent<FollowPositionAndRotation>().enabled.ToString() : "none") + " body=" + (bool)i.GetComponent<Rigidbody>()).ToArray()));
            }
            else if (action == "freecam")
            {
                Gameplay.CoopLife.TestFreeCam = argument == "1";
                plugin.Session.Life.TestToggleFreeCamera();
                plugin.Log("TEST free camera " + plugin.Session.Life.FreeCamera);
            }
            else if (action == "camera")
            {
                var cam = plugin.Session.Local ? plugin.Session.Local.camera : null;
                plugin.Log("TEST CAMERA "
                    + (cam ? cam.transform.position.ToString() + " yaw=" + cam.transform.eulerAngles.y.ToString("0") : "none")
                    + " state=" + plugin.Session.Life.State + " free=" + plugin.Session.Life.FreeCamera + " watching="
                    + plugin.Session.Life.WatchingName);
            }
            else if (action == "fp")
            {
                string directory = fingerprintDirectory ?? System.IO.Path.GetDirectoryName(reportPath ?? uiCapture ?? ".");
                string path = System.IO.Path.Combine(directory, "fp-" + argument + "-" + plugin.PlayerName.Value + ".txt");
                System.IO.File.WriteAllText(path, plugin.Session.WorldFingerprint());
                plugin.Log("TEST fingerprint " + path);
            }
            else if (action == "pick") PickNearest(argument, 1);
            else if (action == "use")
            {
                // Stand at a puzzle part (by path suffix) and interact with it, holding whatever is equipped.
                // "path#n": the n-th of several parts with the same path, ordered by position (the same on every peer).
                // "path@edge": stand on the highest floor around it rather than below it (a pit's rim, not its bottom).
                bool edge = argument.EndsWith("@edge", StringComparison.Ordinal);
                if (edge) argument = argument.Substring(0, argument.Length - 5);
                // "path@here": interact from where the player is (lying in the bathtub), without walking up to it.
                bool here = argument.EndsWith("@here", StringComparison.Ordinal);
                if (here) argument = argument.Substring(0, argument.Length - 5);
                string suffix = argument; int nth = 0;
                int hash = argument.LastIndexOf('#');
                if (hash > 0 && int.TryParse(argument.Substring(hash + 1), out nth)) suffix = argument.Substring(0, hash);
                var target = Resources.FindObjectsOfTypeAll<GrannyClone.Interactable>()
                    .Where(i => i && i.gameObject.scene.IsValid() && i.gameObject.activeInHierarchy && GraphDump.Path(i.transform).EndsWith(suffix, StringComparison.Ordinal))
                    .OrderBy(i => Mathf.Round(i.transform.position.x * 10f)).ThenBy(i => Mathf.Round(i.transform.position.z * 10f)).ThenBy(i => i.transform.position.y)
                    .Skip(nth).FirstOrDefault();
                if (!target) throw new InvalidOperationException("no interactable " + argument);
                if (here) { target.Interact(); plugin.Log("TEST use here " + GraphDump.Path(target.transform)); return; }
                Vector3 stand = edge ? RimSpot(target.CenterPt) : StandingSpot(target.CenterPt);
                Teleport(stand, target.CenterPt);
                StartCoroutine(Hold(stand, target.CenterPt, 0.8f)); // No floor in reach (a vent opening): hold the spot.
                StartCoroutine(Later(0.6f, () =>
                {
                    target.Interact();
                    plugin.Log("TEST use at " + ((Component)plugin.Session.Local).transform.position + " target "
                        + target.CenterPt + " " + GraphDump.Path(target.transform) + " holding "
                        + (plugin.Session.Local.inventory.EquippedItem ? plugin.Session.Local.inventory.EquippedItem.name : "nothing"));
                }));
            }
            else if (action == "useall")
            {
                // "radius|max": walk to each door, cabinet and light switch within the radius (nearest first) and use it, as
                // a player pressing interact at each: a lot of world state changed through the normal paths (sync audits).
                var parts = argument.Split('|');
                float radius = float.Parse(parts[0], System.Globalization.CultureInfo.InvariantCulture);
                int most = parts.Length > 1 ? int.Parse(parts[1]) : 20;
                Vector3 from = ((Component)plugin.Session.Local).transform.position;
                var targets = Resources.FindObjectsOfTypeAll<GrannyClone.Interactable>()
                    .Where(i => i && i.gameObject.scene.IsValid() && i.gameObject.activeInHierarchy && (i.GetComponentInParent<GrannyClone.Door>() || i is GrannyClone.CabinetsInteractable || i is GrannyClone.LightSwitchInteractable)
                        && Vector3.Distance(i.CenterPt, from) <= radius)
                    .OrderBy(i => Vector3.Distance(i.CenterPt, from)).Take(most).ToList();
                StartCoroutine(UseAll(targets));
            }
            else if (action == "inv")
            {
                var local = plugin.Session.Local;
                var items = HarmonyLib.AccessTools.Field(typeof(GrannyClone.Inventory),
                    "items").GetValue(local.inventory) as System.Collections.Generic.List<GrannyClone.ItemInteractable>;
                plugin.Log("TEST INV equipped=" + (local.inventory.EquippedItem ? local.inventory.EquippedItem.name : "-") + " items=[" + (items == null ? "" : string.Join(",", items.Where(i => i).Select(i => i.name).ToArray())) + "] pstate=" + local.playerStates.StateCheck()
                    + " hp=" + (local.health ? local.health.HealthMeter.ToString("0") : "-") + " clips=" + GrannyClone.LockpickItem.lockpickCount + " life=" + plugin.Session.Life.State + " move=" + local.firstPersonController.canMove + " look=" + local.firstPersonController.canMouseLook + " fov=" + (local.camera ? local.camera.fieldOfView.ToString("0") : "-")
                    + " at=" + ((Component)local).transform.position.ToString("0.0") + " yaw=" + ((Component)local).transform.eulerAngles.y.ToString("0") + (local.camera ? " cam=" + local.camera.transform.position.ToString("0.00") + " camrot=" + local.camera.transform.eulerAngles.ToString("0") : "") + " parent=" + (((Component)local).transform.parent ? ((Component)local).transform.parent.name : "-") + " scene=" + UnityEngine.SceneManagement.SceneManager.GetActiveScene().name);
            }
            else if (action == "allitems")
            {
                plugin.Log("TEST ALLITEMS " + string.Join(" ; ", Resources.FindObjectsOfTypeAll<GrannyClone.ItemInteractable>()
                    .Where(i => i && i.gameObject.scene.IsValid())
                    .Select(i => i.name + " @ " + GraphDump.Path(i.transform) + (i.gameObject.activeInHierarchy ? "" : " (inactive)") + (i.CantPickUp ? " (locked)" : "")).OrderBy(x => x).ToArray()));
            }
            else if (action == "si")
            {
                plugin.Log("TEST SI " + string.Join(" ; ", Resources.FindObjectsOfTypeAll<GrannyClone.StateInteractable>()
                    .Where(i => i && i.gameObject.scene.IsValid() && GraphDump.Path(i.transform).Contains(argument))
                    .Select(i => GraphDump.Path(i.transform) + " active=" + i.gameObject.activeInHierarchy + " index=" + HarmonyLib.AccessTools.Field(typeof(GrannyClone.StateInteractable), "stateIndexCurrent").GetValue(i) + " busy=" + i.IsBusy + " used=" + (i.ItemUsed ? i.ItemUsed.name : "-") + " needs=" + RequiredItem(i) + " text=" + i.InteractionString).ToArray()));
            }
            else if (action == "turn")
            {
                // As moving the mouse sideways: turn the view by this many degrees (the game turns the body).
                var local = plugin.Session.Local;
                var body = ((Component)local).transform;
                body.Rotate(0f, float.Parse(argument, System.Globalization.CultureInfo.InvariantCulture), 0f, Space.World);
                local.firstPersonController.mouseLook.Init(body, local.head ? local.head : local.camera.transform);
                plugin.Log("TEST turn " + argument + " yaw=" + body.eulerAngles.y.ToString("0"));
            }
            else if (action == "holders") plugin.Log("TEST HOLDERS " + plugin.Session.Holders());
            else if (action == "drop")
            {
                // Put down a carried item (by name) through the normal drop path, as a player with a full inventory would.
                var inventory = plugin.Session.Local.inventory;
                var items = HarmonyLib.AccessTools.Field(typeof(GrannyClone.Inventory),
                    "items").GetValue(inventory) as System.Collections.Generic.List<GrannyClone.ItemInteractable>;
                var item = items == null ? null : items.FirstOrDefault(i => i && i.name.StartsWith(argument, StringComparison.OrdinalIgnoreCase));
                if (!item) throw new InvalidOperationException("not carrying " + argument);
                var change = HarmonyLib.AccessTools.Method(typeof(GrannyClone.Inventory), "changeItemAnimation");
                MEC.Timing.RunCoroutine((System.Collections.Generic.IEnumerator<float>)change.Invoke(inventory, new object[] { item }));
                // Turn to face open space first: the game will not drop an item into a wall.
                var body = ((Component)plugin.Session.Local).transform;
                Vector3 eye = plugin.Session.Local.camera.transform.position;
                for (int turn = 0; turn < 8; turn++)
                {
                    Vector3 way = Quaternion.Euler(0f, body.eulerAngles.y + 180f + turn * 45f, 0f) * Vector3.forward;
                    if (Physics.Raycast(eye, way, 1.2f, -1, QueryTriggerInteraction.Ignore)) continue;
                    Teleport(body.position, eye + way * 3f);
                    break;
                }
                StartCoroutine(Later(0.8f,
                    () => { inventory.DropItem(); plugin.Log("TEST drop " + item.name + " carrying " + string.Join(",", items.Where(i => i).Select(i => i.name).ToArray())); }));
            }
            else if (action == "letgo")
            {
                // As releasing the interact button on something being dragged.
                var target = Resources.FindObjectsOfTypeAll<GrannyClone.Interactable>().FirstOrDefault(i => i && i.gameObject.scene.IsValid() && i.gameObject.activeInHierarchy && GraphDump.Path(i.transform).EndsWith(argument, StringComparison.Ordinal));
                if (!target) throw new InvalidOperationException("no interactable " + argument);
                target.Release();
                plugin.Log("TEST letgo " + GraphDump.Path(target.transform));
            }
            else if (action == "playset")
            {
                // The PLAY screen's choices: "random,scenario" (scenario 0 normal, 1 Carnival, 2 Backrooms, 3 PDM).
                var parts = argument.Split(',');
                RandomizeItemLocationsMode.RandomModeEnabled = parts[0] == "1";
                TextureSwapper.CurrentID = (TextureSwapID)int.Parse(parts[1]);
                SkinSwapper.CurrentID = (SkinID)int.Parse(parts[1]);
                plugin.Log("TEST playset random=" + RandomizeItemLocationsMode.RandomModeEnabled + " scenario=" + TextureSwapper.CurrentID);
            }
            else if (action == "beds")
            {
                // Every hiding bed: its size across the way you crawl in, and its hide/exit points.
                var hide = HarmonyLib.AccessTools.Field(typeof(GrannyClone.HideBedInteractable), "hidePt");
                var exit = HarmonyLib.AccessTools.Field(typeof(GrannyClone.HideBedInteractable), "exitPt");
                var pivot = HarmonyLib.AccessTools.Field(typeof(GrannyClone.HideBedInteractable), "pivotMine");
                foreach (var bed in Resources.FindObjectsOfTypeAll<GrannyClone.HideBedInteractable>().Where(b => b && b.gameObject.scene.IsValid() && b.gameObject.activeInHierarchy))
                {
                    var h = hide.GetValue(bed) as Transform; var e = exit.GetValue(bed) as Transform; var pv = pivot.GetValue(bed) as Transform;
                    Bounds box = new Bounds(bed.transform.position, Vector3.zero); bool any = false;
                    foreach (var r in (pv ? pv : bed.transform).GetComponentsInChildren<Renderer>())
                    {
                        if (!any) { box = r.bounds; any = true; } else box.Encapsulate(r.bounds);
                    }
                    Vector3 inward = h && e ? Vector3.ProjectOnPlane(h.position - e.position, Vector3.up) : Vector3.zero;
                    Vector3 across = Vector3.Cross(Vector3.up, inward.normalized);
                    float width = Mathf.Abs(across.x) * box.size.x + Mathf.Abs(across.z) * box.size.z,
                        depth = Mathf.Abs(inward.normalized.x) * box.size.x + Mathf.Abs(inward.normalized.z) * box.size.z;
                    plugin.Log("TEST BED " + GraphDump.Path(bed.transform) + " size=" + box.size.ToString("0.00")
                        + " across=" + width.ToString("0.00") + " deep=" + depth.ToString("0.00") + " hide="
                        + (h ? h.position.ToString("0.00") : "-") + " exit=" + (e ? e.position.ToString("0.00") : "-")
                        + " in=" + inward.magnitude.ToString("0.00") + " clear="
                        + (h ? (box.min.y - h.position.y).ToString("0.00") : "-"));
                }
            }
            else if (action == "inray")
            {
                // Stand where an object's forward ray (a damage ray) meets the floor.
                var source = Resources.FindObjectsOfTypeAll<Transform>().FirstOrDefault(t => t && t.gameObject.scene.IsValid() && GraphDump.Path(t).EndsWith(argument, StringComparison.Ordinal));
                if (!source) throw new InvalidOperationException("no object " + argument);
                RaycastHit hit;
                if (!Physics.Raycast(source.position, source.forward, out hit, 40f, ~0, QueryTriggerInteraction.Ignore))
                    throw new InvalidOperationException("ray hits nothing");
                Vector3 spot = hit.point + Vector3.up * 0.05f;
                Teleport(spot, source.position);
                StartCoroutine(Hold(spot, source.position, 8f));
                plugin.Log("TEST inray " + GraphDump.Path(source) + " active=" + source.gameObject.activeInHierarchy
                    + " hits " + (hit.collider ? GraphDump.Path(hit.collider.transform) : "-") + " at "
                    + hit.point.ToString("0.0"));
            }
            else if (action == "itemat")
            {
                // Where items with this name lie, and how (for placing spares next to them).
                plugin.Log("TEST ITEMAT " + string.Join(" ; ", Resources.FindObjectsOfTypeAll<GrannyClone.ItemInteractable>().Where(i => i && i.gameObject.scene.IsValid() && i.name.StartsWith(argument, StringComparison.OrdinalIgnoreCase)).Select(i =>
                {
                    var body = i.GetComponent<Rigidbody>(); var shape = i.GetComponent<Collider>();
                    return GraphDump.Path(i.transform) + (i.gameObject.activeInHierarchy ? "" : " (inactive)") + " id=" + plugin.Session.IdOf(i) + " at=" + i.transform.position.ToString("0.00") + " rot=" + i.transform.eulerAngles.ToString("0") + " parentRight=" + (i.transform.parent ? i.transform.parent.right.ToString("0.0") : "-")
                        + " kinematic=" + (body ? body.isKinematic.ToString() : "none") + " size=" + (shape ? shape.bounds.size.ToString("0.00") : "-");
                }).ToArray()));
            }
            else if (action == "find")
            {
                // Objects (also inactive) whose name, or UI text, contains the argument; with their machines.
                string needle = argument.ToLowerInvariant();
                var lines = new System.Collections.Generic.List<string>();
                foreach (var t in Resources.FindObjectsOfTypeAll<Transform>().Where(t => t && t.gameObject.scene.IsValid()))
                {
                    var text = t.GetComponent<UnityEngine.UI.Text>();
                    bool byName = t.name.ToLowerInvariant().Contains(needle),
                        byText = text && text.text != null && text.text.ToLowerInvariant().Contains(needle);
                    if (!byName && !byText) continue;
                    var owner = t.GetComponent<NodeCanvas.Framework.GraphOwner>();
                    var shape = t.GetComponent<Renderer>();
                    lines.Add(GraphDump.Path(t) + (t.gameObject.activeInHierarchy ? "" : " (inactive)")
                        + (byText ? " text='" + text.text.Replace('\n', ' ') + "'" : "") + " at="
                        + t.position.ToString("0.00") + " yaw=" + t.eulerAngles.y.ToString("0")
                        + (shape ? " bounds=" + shape.bounds.min.ToString("0.00") + ".." + shape.bounds.max.ToString("0.00") : "")
                        + " comps="
                        + string.Join(",", t.GetComponents<Component>().Where(c => c).Select(c => c.GetType().Name).ToArray())
                        + (owner && owner.graph ? " graph=" + owner.graph.name + " running=" + owner.isRunning : ""));
                }
                plugin.Log("TEST FIND " + argument + " (" + lines.Count + ") ; " + string.Join(" ; ", lines.Take(80).ToArray()));
            }
            else if (action == "camshot")
            {
                // "x,y,z|x,y,z|name": a picture of the live scene from a point, looking at another.
                string[] parts = argument.Split('|');
                Func<string,
                    Vector3> point = text => { var v = text.Split(','); return new Vector3(float.Parse(v[0], System.Globalization.CultureInfo.InvariantCulture), float.Parse(v[1], System.Globalization.CultureInfo.InvariantCulture), float.Parse(v[2], System.Globalization.CultureInfo.InvariantCulture)); };
                var holder = new GameObject("NMM_TestCamera");
                try
                {
                    var lamp = new GameObject("NMM_TestLamp").AddComponent<Light>(); // Readable pictures in the dark.
                    lamp.transform.SetParent(holder.transform, false);
                    lamp.type = LightType.Point; lamp.range = 8f; lamp.intensity = 1.6f;
                    var camera = holder.AddComponent<Camera>();
                    // What the player sees.
                    if (parts.Length > 3 && parts[3] == "eyes" && plugin.Session.Local && plugin.Session.Local.camera)
                        camera.CopyFrom(plugin.Session.Local.camera);
                    camera.fieldOfView = 60f; camera.nearClipPlane = 0.05f; camera.enabled = false;
                    holder.transform.position = point(parts[0]);
                    holder.transform.LookAt(point(parts[1]));
                    var target = new RenderTexture(640, 400, 24);
                    camera.targetTexture = target;
                    camera.Render();
                    var prior = RenderTexture.active;
                    RenderTexture.active = target;
                    var picture = new Texture2D(640, 400, TextureFormat.RGB24, false);
                    picture.ReadPixels(new Rect(0, 0, 640, 400), 0, 0);
                    RenderTexture.active = prior;
                    string file = System.IO.Path.Combine(fingerprintDirectory ?? ".", "shot-" + parts[2] + ".png");
                    System.IO.File.WriteAllBytes(file, picture.EncodeToPNG());
                    camera.targetTexture = null;
                    UnityEngine.Object.Destroy(target); UnityEngine.Object.Destroy(picture);
                    plugin.Log("TEST camshot " + file);
                }
                finally { UnityEngine.Object.Destroy(holder); }
            }
            else if (action == "itemshape")
            {
                // "name": an item's make-up: its scales, how the game holds it, and each mesh part's size in the world.
                foreach (var item in Resources.FindObjectsOfTypeAll<GrannyClone.ItemInteractable>().Where(i => i && i.gameObject.scene.IsValid() && i.name.StartsWith(argument, StringComparison.OrdinalIgnoreCase)).Take(1))
                {
                    plugin.Log("TEST ITEMSHAPE " + GraphDump.Path(item.transform) + " initialScale=" + item.InitialScale.ToString("0.000") + " lossy=" + item.transform.lossyScale.ToString("0.000")
                        + " holdRot=" + item.HoldingRotation.ToString("0") + " holdWith=" + item.HoldWith + " active=" + item.gameObject.activeInHierarchy);
                    foreach (var part in item.GetComponentsInChildren<Renderer>(true))
                    {
                        var filter = part.GetComponent<MeshFilter>();
                        var skinned = part as SkinnedMeshRenderer;
                        var mesh = filter ? filter.sharedMesh : skinned ? skinned.sharedMesh : null;
                        Vector3 size = mesh ? Vector3.Scale(mesh.bounds.size, part.transform.lossyScale) : Vector3.zero;
                        plugin.Log("TEST ITEMSHAPE   " + GraphDump.Path(part.transform) + " " + part.GetType().Name + " mesh=" + (mesh ? mesh.name : "-") + " size=" + size.ToString("0.000")
                            + " enabled=" + part.enabled + " layer=" + LayerMask.LayerToName(part.gameObject.layer) + " shader=" + (part.sharedMaterial ? part.sharedMaterial.shader.name : "-"));
                    }
                }
                Transform body; GameObject held;
                if (plugin.Session.Avatars.GetHand(2, out body, out held) && held)
                    foreach (var filter in held.GetComponentsInChildren<MeshFilter>())
                        plugin.Log("TEST ITEMSHAPE   held " + filter.name + " size="
                            + Vector3.Scale(filter.sharedMesh.bounds.size, filter.transform.lossyScale).ToString("0.000"));
            }
            else if (action == "handshot")
            {
                // "peer|name|view": a close picture of a teammate's right hand and what it holds. View "front" (from ahead and
                // to their right), "side" (from their right), "back" (over their shoulder), "far" (the whole body); "hand"
                // and "inside" look at the hand itself, from outside it and from in front of the body.
                string[] parts = argument.Split('|');
                Transform body; GameObject held;
                var hand = plugin.Session.Avatars.GetHand(int.Parse(parts[0]), out body, out held);
                if (!hand) throw new InvalidOperationException("no hand for peer " + parts[0]);
                string view = parts.Length > 2 ? parts[2] : "front";
                Vector3 look = view == "hand" || view == "inside" ? plugin.Session.Avatars.HandMiddle(int.Parse(parts[0]))
                    : held ? Bounds(held).center : hand.position;
                Vector3 from = view == "hand" ? look + body.forward * 0.45f + body.right * 0.3f + Vector3.up * 0.08f
                    : view == "inside" ? look + body.forward * 0.4f - body.right * 0.35f + Vector3.up * 0.08f
                    : view == "side" ? look + body.right * 0.7f + Vector3.up * 0.05f
                    : view == "back" ? look - body.forward * 0.55f + body.right * 0.2f + Vector3.up * 0.35f
                    : view == "far" ? body.position + body.forward * 2.2f + body.right * 0.6f + Vector3.up * 1.3f
                    : look + body.forward * 0.6f + body.right * 0.35f + Vector3.up * 0.12f;
                if (view == "far") look = body.position + Vector3.up * 1.0f;
                var holder = new GameObject("NMM_TestCamera");
                try
                {
                    var lamp = new GameObject("NMM_TestLamp").AddComponent<Light>();
                    lamp.transform.SetParent(holder.transform, false);
                    lamp.type = LightType.Point; lamp.range = 6f; lamp.intensity = 1.4f;
                    var camera = holder.AddComponent<Camera>();
                    camera.fieldOfView = view == "far" ? 55f : 38f; camera.nearClipPlane = 0.03f; camera.enabled = false;
                    holder.transform.position = from;
                    holder.transform.LookAt(look);
                    var target = new RenderTexture(640, 480, 24);
                    camera.targetTexture = target;
                    camera.Render();
                    var prior = RenderTexture.active;
                    RenderTexture.active = target;
                    var picture = new Texture2D(640, 480, TextureFormat.RGB24, false);
                    picture.ReadPixels(new Rect(0, 0, 640, 480), 0, 0);
                    RenderTexture.active = prior;
                    string file = System.IO.Path.Combine(fingerprintDirectory ?? ".", "hand-" + parts[1] + "-" + view + ".png");
                    System.IO.File.WriteAllBytes(file, picture.EncodeToPNG());
                    camera.targetTexture = null;
                    UnityEngine.Object.Destroy(target); UnityEngine.Object.Destroy(picture);
                    // In the hand: how far the hand's middle is from the item (0 inside it).
                    string gap = "";
                    if (held)
                    {
                        Vector3 middle = plugin.Session.Avatars.HandMiddle(int.Parse(parts[0]));
                        Bounds box = Bounds(held);
                        gap = " gap=" + Mathf.Sqrt(box.SqrDistance(middle)).ToString("0.000") + "m";
                    }
                    plugin.Log("TEST handshot " + file + (held ? " holding " + held.transform.childCount + " parts" + gap : " empty-handed"));
                }
                finally { UnityEngine.Object.Destroy(holder); }
            }
            else if (action == "avatars") plugin.Log("TEST AVATARS " + plugin.Session.Avatars.Describe());
            else if (action == "slopes")
            {
                // "minx,minz|maxx,maxz": sloping floors (ramps, stairs as ramps) in that area, every floor level, by 2 m cell.
                string[] parts = argument.Split('|');
                Func<string,
                    Vector2> point = text => { var v = text.Split(','); return new Vector2(float.Parse(v[0], System.Globalization.CultureInfo.InvariantCulture), float.Parse(v[1], System.Globalization.CultureInfo.InvariantCulture)); };
                Vector2 min = point(parts[0]), max = point(parts[1]);
                int mask = Physics.DefaultRaycastLayers & ~(1 << LayerMask.NameToLayer("Player"));
                var cells = new System.Collections.Generic.Dictionary<string, string>();
                for (float x = min.x; x <= max.x; x += 0.5f)
                    for (float z = min.y; z <= max.y; z += 0.5f)
                        foreach (var hit in Physics.RaycastAll(new Vector3(x, 60f, z), Vector3.down, 120f, mask, QueryTriggerInteraction.Ignore))
                        {
                            float angle = Vector3.Angle(hit.normal, Vector3.up);
                            if (angle < 8f || angle > 45f) continue;
                            string key = Mathf.Round(x / 2f) * 2f + "," + Mathf.Round(hit.point.y) + "," + Mathf.Round(z / 2f) * 2f;
                            if (!cells.ContainsKey(key)) cells[key] = hit.point.ToString("0.0") + " " + angle.ToString("0") + "deg " + hit.collider.name;
                        }
                plugin.Log("TEST SLOPES " + cells.Count + " ; " + string.Join(" ; ", cells.Values.Take(80).ToArray()));
            }
            else if (action == "cc")
            {
                var c = plugin.Session.Local ? plugin.Session.Local.characterController : null;
                plugin.Log("TEST CC "
                    + (c ? "height=" + c.height + " radius=" + c.radius + " center=" + c.center + " slope=" + c.slopeLimit + " step=" + c.stepOffset + " skin=" + c.skinWidth + " grounded=" + c.isGrounded + " flags=" + c.collisionFlags + " enabled=" + c.enabled + " crouching=" + plugin.Session.Local.firstPersonController.IsCrouching + " jumping=" + plugin.Session.Local.firstPersonController.isJumping + " canJump=" + plugin.Session.Local.firstPersonController.canJump + " jumpSpeed=" + HarmonyLib.AccessTools.Field(typeof(GrannyClone.FirstPersonController), "jumpSpeed").GetValue(plugin.Session.Local.firstPersonController) + " vy=" + c.velocity.y.ToString("0.00") + " pos=" + c.transform.position.ToString("0.00") : "none"));
            }
            else if (action == "point") { plugin.Session.Gestures.Point(); plugin.Log("TEST point " + Gameplay.Gestures.Points); }
            else if (action == "whistle") { plugin.Session.Gestures.Whistle(plugin.Session); plugin.Log("TEST whistle " + Gameplay.Gestures.Whistles); }
            else if (action == "ductprofile")
            {
                // "x0,x1,z,y": the duct's floor, roof and sides every 0.5 m along x (back faces counted, as the search does).
                var v = argument.Split(',').Select(x => float.Parse(x, System.Globalization.CultureInfo.InvariantCulture)).ToArray();
                int mask = LayerMask.GetMask("Wall", "Ground", "Ceiling", "Door");
                bool back = Physics.queriesHitBackfaces; Physics.queriesHitBackfaces = true;
                var rows = new System.Collections.Generic.List<string>();
                try
                {
                    Func<Vector3, Vector3,
                        string> cast = (at, way) => { RaycastHit hit; return Physics.Raycast(at, way, out hit, 3f, mask, QueryTriggerInteraction.Ignore) ? hit.distance.ToString("0.00") + ":" + hit.collider.name : "-"; };
                    for (float x = v[0]; x <= v[1] + 0.01f; x += 0.5f)
                    {
                        var at = new Vector3(x, v[3], v[2]);
                        rows.Add("x=" + x.ToString("0.0") + " down " + cast(at, Vector3.down) + " up " + cast(at, Vector3.up)
                            + " -z " + cast(at, Vector3.back) + " +z " + cast(at, Vector3.forward) + " +x "
                            + cast(at, Vector3.right));
                    }
                }
                finally { Physics.queriesHitBackfaces = back; }
                plugin.Log("TEST DUCTPROFILE " + argument + " ; " + string.Join(" ; ", rows.ToArray()));
            }
            else if (action == "ductsurvey")
            {
                // "x,y,z|x,y,z": a voice at the first point inside a duct, heard at the second.
                var parts = argument.Split('|');
                Func<string,
                    Vector3> point = text => { var v = text.Split(',').Select(x => float.Parse(x, System.Globalization.CultureInfo.InvariantCulture)).ToArray(); return new Vector3(v[0], v[1], v[2]); };
                plugin.Log("TEST DUCTSURVEY " + argument + " ; " + plugin.Session.Voice.DuctSurvey(point(parts[0]), point(parts[1])));
            }
            else if (action == "hearing") plugin.Log("TEST HEARING " + argument + " ; " + plugin.Session.Voice.DescribeHearing());
            else if (action == "look")
            {
                // "yaw,pitch" in degrees: turn the player's body by yaw and tilt their view to pitch (positive = up).
                var v = argument.Split(',');
                float yaw = float.Parse(v[0], System.Globalization.CultureInfo.InvariantCulture),
                    pitch = float.Parse(v[1], System.Globalization.CultureInfo.InvariantCulture);
                var local = plugin.Session.Local;
                var look = local.firstPersonController.mouseLook;
                var body = ((Component)local).transform;
                body.rotation = Quaternion.AngleAxis(yaw, Vector3.up) * body.rotation;
                HarmonyLib.AccessTools.Field(typeof(GrannyClone.MouseLook), "characterTargetRot").SetValue(look, body.localRotation);
                HarmonyLib.AccessTools.Field(typeof(GrannyClone.MouseLook), "cameraTargetRot").SetValue(look, Quaternion.Euler(-pitch, 0f, 0f));
                if (local.head) local.head.localRotation = Quaternion.Euler(-pitch, 0f, 0f);
                plugin.Log("TEST look " + argument);
            }
            else if (action == "jump")
            {
                // Jump as the game's controller would (its own movement is off in test games): up at its jump speed,
                // down under its gravity, flagged as jumping until landing.
                StartCoroutine(TestJump());
            }
            else if (action == "fire")
            {
                // Fire the equipped revolver (loaded first).
                var gun = Resources.FindObjectsOfTypeAll<GrannyClone.RevolverInteractable>().FirstOrDefault(g => g && g.gameObject.activeInHierarchy);
                if (!gun) throw new InvalidOperationException("no revolver in hand");
                if (GrannyClone.RevolverInteractable.Bullets <= 0) GrannyClone.RevolverInteractable.Bullets = 4;
                HarmonyLib.AccessTools.Method(typeof(GrannyClone.RevolverInteractable), "fire").Invoke(gun, null);
                plugin.Log("TEST fire shots=" + GameHooks.ShotsFired + " bullets=" + GrannyClone.RevolverInteractable.Bullets);
            }
            else if (action == "colliders")
            {
                // "x,y,z|radius": the scenery colliders there, with their objects and bounds.
                string[] parts = argument.Split('|');
                var v = parts[0].Split(',');
                var at = new Vector3(float.Parse(v[0], System.Globalization.CultureInfo.InvariantCulture),
                    float.Parse(v[1], System.Globalization.CultureInfo.InvariantCulture),
                    float.Parse(v[2], System.Globalization.CultureInfo.InvariantCulture));
                var hits = Physics.OverlapSphere(at, float.Parse(parts[1], System.Globalization.CultureInfo.InvariantCulture), ~0, QueryTriggerInteraction.Ignore)
                    .Select(c => GraphDump.Path(c.transform) + " [" + c.GetType().Name + " layer=" + LayerMask.LayerToName(c.gameObject.layer) + " active=" + c.gameObject.activeInHierarchy + " bounds=" + c.bounds.min.ToString("0.00") + ".." + c.bounds.max.ToString("0.00") + " comps=" + string.Join(",", c.GetComponents<Component>().Select(x => x.GetType().Name).ToArray()) + "]");
                plugin.Log("TEST COLLIDERS " + argument + " ; " + string.Join(" ; ", hits.ToArray()));
            }
            else if (action == "peer")
            {
                // "id|action:argument": the host has a guest's test game run a step now.
                string[] parts = argument.Split(new[] { '|' }, 2);
                plugin.Session.SendTestStep(int.Parse(parts[0]), parts[1]);
            }
            else if (action == "uistack")
            {
                // How the game draws its picture and its own UI: every camera (order, what it draws, where to, its effects),
                // every canvas (how it is drawn and by which camera).
                Func<int,
                    string> layers = mask => string.Join("+", Enumerable.Range(0, 32).Where(i => (mask & (1 << i)) != 0).Select(i => LayerMask.LayerToName(i)).Where(n => n.Length > 0).ToArray());
                foreach (var c in Camera.allCameras.OrderBy(c => c.depth))
                    plugin.Log("TEST UISTACK camera " + GraphDump.Path(c.transform) + " depth=" + c.depth + " clear="
                        + c.clearFlags + " layers=" + layers(c.cullingMask) + " target="
                        + (c.targetTexture ? c.targetTexture.name + " " + c.targetTexture.width + "x" + c.targetTexture.height : "screen")
                        + " fx="
                        + string.Join(",", c.GetComponents<MonoBehaviour>().Where(b => b).Select(b => b.GetType().Name + (b.enabled ? "" : "(off)")).ToArray()));
                foreach (var canvas in UnityEngine.Object.FindObjectsOfType<Canvas>().Where(cv => cv.isRootCanvas))
                    plugin.Log("TEST UISTACK canvas " + GraphDump.Path(canvas.transform) + " mode=" + canvas.renderMode
                        + " camera=" + (canvas.worldCamera ? canvas.worldCamera.name : "-") + " plane="
                        + canvas.planeDistance + " order=" + canvas.sortingOrder + " layer="
                        + LayerMask.LayerToName(canvas.gameObject.layer) + " active=" + canvas.gameObject.activeInHierarchy);
                foreach (var raw in UnityEngine.Object.FindObjectsOfType<UnityEngine.UI.RawImage>().Where(r => r.texture is RenderTexture))
                    plugin.Log("TEST UISTACK screen " + GraphDump.Path(raw.transform) + " shows " + raw.texture.name
                        + " material=" + (raw.material ? raw.material.shader.name : "-"));
            }
            else if (action == "unlocks")
            {
                // This game's save: where it is, the anomalies counted, the unlocks; and every anomaly in the level.
                var path = HarmonyLib.AccessTools.Field(typeof(MyPlayerPrefs), "filePath").GetValue(null) as string;
                Vector3 from = plugin.Session.Local ? ((Component)plugin.Session.Local).transform.position : Vector3.zero;
                plugin.Log("TEST UNLOCKS save=" + path + " anomalies=" + MyPlayerPrefs.GetInt("AnomaliesFound", 0) + " factory=" + MyPlayerPrefs.GetBool("FACTORY_unlocked", false) + " circus=" + MyPlayerPrefs.GetBool("CIRCUS_unlocked", false) + " backrooms=" + MyPlayerPrefs.GetBool("BACKROOMS_unlocked", false)
                    + " | " + string.Join(" ; ", Resources.FindObjectsOfTypeAll<AnomalyCollect>().Where(a => a && a.gameObject.scene.IsValid())
                        .OrderBy(a => Vector3.Distance(a.transform.position, from))
                        .Select(a => a.id + "@" + GraphDump.Path(a.transform) + " active=" + a.gameObject.activeInHierarchy + " found=" + MyPlayerPrefs.GetBool("Anomaly_" + a.id, false) + " id=" + (plugin.Session.WorldIdFor(a) ?? "-") + " d=" + Vector3.Distance(a.transform.position, from).ToString("0")).ToArray()));
            }
            else if (action == "anomaly")
            {
                // Walk up to an anomaly (by id, or the nearest active one) and pick it up, as the player would.
                Vector3 from = ((Component)plugin.Session.Local).transform.position;
                var target = Resources.FindObjectsOfTypeAll<AnomalyCollect>()
                    .Where(a => a && a.gameObject.scene.IsValid() && a.gameObject.activeInHierarchy && (string.IsNullOrEmpty(argument) || a.id == argument))
                    .OrderBy(a => Vector3.Distance(a.transform.position, from)).FirstOrDefault();
                if (!target) throw new InvalidOperationException("no anomaly " + argument);
                Vector3 stand = StandingSpot(target.CenterPt);
                Teleport(stand, target.CenterPt);
                StartCoroutine(Hold(stand, target.CenterPt, 0.8f));
                StartCoroutine(Later(0.6f, () => { target.Interact(); plugin.Log("TEST anomaly " + target.id + " " + GraphDump.Path(target.transform)); }));
            }
            else if (action == "markfound") { MyPlayerPrefs.SetBool("Anomaly_" + argument, true); plugin.Log("TEST markfound " + argument); }
            else if (action == "endunlock")
            {
                // What the game's ending runs to give its unlocks, here, now.
                var handler = UnityEngine.Object.FindObjectOfType<AchievementHandler>();
                var temporary = handler ? null : new GameObject("NMM_TestUnlocks");
                if (!handler) handler = temporary.AddComponent<AchievementHandler>();
                handler.UnlockDifficultyAchievement();
                if (temporary) UnityEngine.Object.Destroy(temporary);
                plugin.Log("TEST endunlock (handler in scene " + (temporary == null) + ")");
            }
            else if (action == "shaders")
            {
                // What the game draws its characters and rooms with (shader, texture filtering, properties), and ours.
                Func<Renderer, string> describe = r =>
                {
                    var m = r.sharedMaterial;
                    if (!m) return r.name + ": no material";
                    var tex = m.mainTexture;
                    var props = new System.Collections.Generic.List<string>();
                    for (int i = 0; i < m.shader.GetPropertyCount(); i++) props.Add(m.shader.GetPropertyName(i));
                    return r.name + ": " + m.shader.name + " tex="
                        + (tex ? tex.name + "/" + tex.filterMode + "/" + tex.width : "-") + " props="
                        + string.Join(",", props.Take(14).ToArray());
                };
                var nun = Nun();
                plugin.Log("TEST SHADERS nun: " + string.Join(" | ", nun.GetComponentsInChildren<Renderer>(true).Take(4).Select(describe).ToArray()));
                plugin.Log("TEST SHADERS player: "
                    + string.Join(" | ", ((Component)plugin.Session.Local).GetComponentsInChildren<Renderer>(true).Take(4).Select(describe).ToArray()));
                plugin.Log("TEST SHADERS world: "
                    + string.Join(" | ", UnityEngine.Object.FindObjectsOfType<MeshRenderer>().Select(r => r.sharedMaterial ? r.sharedMaterial.shader.name : "-").GroupBy(n => n).OrderByDescending(g => g.Count()).Take(6).Select(g => g.Key + " x" + g.Count()).ToArray()));
                plugin.Log("TEST SHADERS teammate: "
                    + string.Join(" | ", UnityEngine.Object.FindObjectsOfType<SkinnedMeshRenderer>().Where(r => r.name.StartsWith("NMM", StringComparison.Ordinal) || (r.transform.root && r.transform.root.name.StartsWith("NMM", StringComparison.Ordinal))).Take(2).Select(describe).ToArray()));
                plugin.Log("TEST SHADERS cameras: "
                    + string.Join(" | ", Camera.allCameras.Select(c => c.name + " target=" + (c.targetTexture ? c.targetTexture.width + "x" + c.targetTexture.height + "/" + c.targetTexture.filterMode : "screen") + " fx=" + string.Join(",", c.GetComponents<MonoBehaviour>().Select(b => b.GetType().Name).ToArray())).ToArray()));
            }
            else if (action == "noisekit")
            {
                // What on the player makes noise, and the radii: components with Foot/Noise/Alert/Sound in their type name.
                var local = (Component)plugin.Session.Local;
                var kinds = local.GetComponentsInChildren<MonoBehaviour>(true).Where(m => m && System.Text.RegularExpressions.Regex.IsMatch(m.GetType().Name, "Foot|Noise|Alert|Sound|Step")).Select(m => m.GetType().FullName + "@" + m.name).Distinct().ToArray();
                var steps = local.GetComponentsInChildren<FootstepsHumanoid>(true).SelectMany(f => f.StepCycles.Select(c => c.Ground + ":" + c.AlertRadius.ToString("0.0"))).ToArray();
                plugin.Log("TEST NOISEKIT on player: " + string.Join(", ", kinds) + " ; steps " + string.Join(" ", steps)
                    + " ; NoiseAlert x" + UnityEngine.Object.FindObjectsOfType<NoiseAlert>().Length + " NoiseAlertMaster x" + UnityEngine.Object.FindObjectsOfType<Detectable.NoiseAlertMaster>().Length);
                if (noiseWatch == null)
                {
                    noiseWatch = (origin, radius, sender,
                        high) => plugin.Log("TEST NOISE step/alert radius=" + radius.ToString("0.0") + " high=" + high + " from " + (origin ? origin.name : "?"));
                    MakeNoiseAlert.NoiseAlertEvent += noiseWatch;
                    Message.Detectable.NoiseMadeEvent += (position,
                        radius) => plugin.Log("TEST NOISE made radius=" + radius.ToString("0.0") + " at " + position.ToString("0.0"));
                }
            }
            else if (action == "feetfix") { Players.AvatarRig.TestFeetOnFloor = argument == "1"; plugin.Log("TEST feetfix " + argument); }
            else if (action == "clipwatch")
            {
                // 1: start watching teammates' bodies for going into the scenery; 0: stop and report the worst.
                if (argument == "1") { plugin.Session.Avatars.ClippingReport(); Players.RemotePlayers.TestWatchClipping = true; }
                else { Players.RemotePlayers.TestWatchClipping = false; plugin.Log("TEST CLIPPING " + plugin.Session.Avatars.ClippingReport()); }
            }
            else if (action == "hurtprobe")
            {
                // What the game does when the player is hurt: the damage event's listeners and the player's sounds.
                var health = plugin.Session.Local.health;
                var lines = new System.Collections.Generic.List<string>();
                foreach (string field in new[] { "OnDamageEvent", "OnDeathEvent" })
                {
                    var unityEvent = HarmonyLib.AccessTools.Field(typeof(GrannyClone.fpsHealth), field).GetValue(health) as UnityEngine.Events.UnityEventBase;
                    if (unityEvent == null) { lines.Add(field + " null"); continue; }
                    for (int i = 0; i < unityEvent.GetPersistentEventCount(); i++)
                    {
                        var target = unityEvent.GetPersistentTarget(i);
                        var source = target as AudioSource;
                        lines.Add(field + "[" + i + "] "
                            + (target ? target.GetType().Name + " " + (target is Component ? GraphDump.Path(((Component)target).transform) : target.name) : "null")
                            + "." + unityEvent.GetPersistentMethodName(i)
                            + (source && source.clip ? " clip=" + source.clip.name : ""));
                    }
                }
                foreach (var source in ((Component)plugin.Session.Local).GetComponentsInChildren<AudioSource>(true))
                    lines.Add("audio " + GraphDump.Path(source.transform) + " clip=" + (source.clip ? source.clip.name : "-")
                        + " spatial=" + source.spatialBlend + " vol=" + source.volume + " group="
                        + (source.outputAudioMixerGroup ? source.outputAudioMixerGroup.name : "-"));
                foreach (var random in ((Component)plugin.Session.Local).GetComponentsInChildren<AudioSourceRandom>(true))
                {
                    var clips = random.Audio == null ? null
                        : HarmonyLib.AccessTools.Field(typeof(AudioSourceRandomClip), "clips").GetValue(random.Audio) as AudioClip[];
                    lines.Add("random " + GraphDump.Path(random.transform) + " clips="
                        + (clips == null ? "-" : string.Join(",", clips.Select(c => c ? c.name + "(" + c.length.ToString("0.0") + "s)" : "null").ToArray())));
                }
                plugin.Log("TEST HURT ; " + string.Join(" ; ", lines.ToArray()));
            }
            else if (action == "view")
            {
                // What the player's own camera shows right now (spectator placement included), and its lights.
                var camera = plugin.Session.Local.camera;
                var target = new RenderTexture(640, 400, 24);
                var prior = RenderTexture.active; var priorTarget = camera.targetTexture;
                camera.targetTexture = target;
                camera.Render();
                RenderTexture.active = target;
                var picture = new Texture2D(640, 400, TextureFormat.RGB24, false);
                picture.ReadPixels(new Rect(0, 0, 640, 400), 0, 0);
                RenderTexture.active = prior; camera.targetTexture = priorTarget;
                string file = System.IO.Path.Combine(fingerprintDirectory ?? ".", "view-" + argument + ".png");
                System.IO.File.WriteAllBytes(file, picture.EncodeToPNG());
                UnityEngine.Object.Destroy(target); UnityEngine.Object.Destroy(picture);
                var blue = ((Component)plugin.Session.Local).GetComponentsInChildren<Light>(true).FirstOrDefault(l => l.name.StartsWith("Blue light"));
                plugin.Log("TEST view " + file + " camera=" + camera.transform.position.ToString("0.00") + " blue="
                    + (blue ? blue.transform.position.ToString("0.00") + " on=" + blue.isActiveAndEnabled : "none"));
            }
            else if (action == "eyeshot")
            {
                // "peer|name": a picture from this player's own eyes, turned towards a teammate.
                string[] parts = argument.Split('|');
                Vector3? center = plugin.Session.Avatars.GetCenter(int.Parse(parts[0]));
                var eye = plugin.Session.Local.camera.transform.position;
                if (!center.HasValue) throw new InvalidOperationException("no avatar " + parts[0]);
                RunStep("camshot", eye.x.ToString("R", System.Globalization.CultureInfo.InvariantCulture) + "," + eye.y.ToString("R", System.Globalization.CultureInfo.InvariantCulture) + "," + eye.z.ToString("R", System.Globalization.CultureInfo.InvariantCulture) + "|"
                    + center.Value.x.ToString("R", System.Globalization.CultureInfo.InvariantCulture) + "," + center.Value.y.ToString("R", System.Globalization.CultureInfo.InvariantCulture) + "," + center.Value.z.ToString("R", System.Globalization.CultureInfo.InvariantCulture) + "|" + parts[1] + "|eyes");
            }
            else if (action == "stroll")
            {
                // "seconds" or "seconds|metres per second".
                string[] parts = argument.Split('|');
                StartCoroutine(Stroll(float.Parse(parts[0], System.Globalization.CultureInfo.InvariantCulture),
                    parts.Length > 1 ? float.Parse(parts[1], System.Globalization.CultureInfo.InvariantCulture) : 1.6f));
            }
            else if (action == "walkinto")
            {
                // Walk straight at a teammate for two seconds through the character controller, as a player would.
                Vector3? center = plugin.Session.Avatars.GetCenter(int.Parse(argument));
                if (!center.HasValue) throw new InvalidOperationException("no avatar " + argument);
                var local = plugin.Session.Local;
                int layer = ((Component)local).gameObject.layer, player = LayerMask.NameToLayer("Player");
                plugin.Log("TEST walkinto layers local=" + LayerMask.LayerToName(layer) + " ignorePlayerPlayer=" + (player >= 0 && Physics.GetIgnoreLayerCollision(layer, player))
                    + " collidesWithPlayer=[" + string.Join(",", Enumerable.Range(0, 32).Where(l => LayerMask.LayerToName(l).Length > 0 && !Physics.GetIgnoreLayerCollision(l, layer)).Select(l => l + ":" + LayerMask.LayerToName(l) + ((Physics.DefaultRaycastLayers & (1 << l)) == 0 ? "(no default raycast)" : "")).ToArray()) + "]");
                StartCoroutine(WalkInto(center.Value));
            }
            else if (action == "unhide")
            {
                // As pressing Interact while hiding.
                var states = plugin.Session.Local.playerStates;
                MEC.Timing.RunCoroutine((System.Collections.Generic.IEnumerator<float>)HarmonyLib.AccessTools.Method(typeof(GrannyClone.PlayerStates),
                    "exitHiding").Invoke(states, null));
                plugin.Log("TEST unhide");
            }
            else if (action == "throwat")
            {
                // Face a window (by path suffix) from 1.6 m at eye level and throw what is held, as Fire1 would.
                var target = Resources.FindObjectsOfTypeAll<Transform>().FirstOrDefault(t => t && t.gameObject.scene.IsValid() && t.gameObject.activeInHierarchy && GraphDump.Path(t).EndsWith(argument, StringComparison.Ordinal));
                if (!target) throw new InvalidOperationException("no object " + argument);
                var local = plugin.Session.Local;
                var body = ((Component)local).transform;
                var shape = target.GetComponent<Collider>();
                Vector3 center = shape ? shape.bounds.center : target.position;
                Vector3 normal = shape && shape.bounds.extents.x < shape.bounds.extents.z ? Vector3.right : Vector3.forward; // A pane is thinnest across.
                if (Vector3.Dot(body.position - center, normal) < 0f) normal = -normal;
                float eye = local.camera ? local.camera.transform.position.y - body.position.y : 1.6f;
                Vector3 spot = center + normal * 1.6f; spot.y = center.y - eye;
                Teleport(spot, center);
                StartCoroutine(Hold(spot, center, 1.2f));
                StartCoroutine(Later(0.7f,
                    () => { var item = local.inventory.EquippedItem; plugin.Log("TEST throw " + (item ? item.name : "nothing") + " at " + GraphDump.Path(target)); if (item) item.Use(); }));
            }
            else if (action == "enter")
            {
                // Walk into a trigger zone (or onto any object) by path suffix.
                var zone = Resources.FindObjectsOfTypeAll<Transform>().FirstOrDefault(t => t && t.gameObject.scene.IsValid() && t.gameObject.activeInHierarchy && GraphDump.Path(t).EndsWith(argument, StringComparison.Ordinal));
                if (!zone) throw new InvalidOperationException("no object " + argument);
                var box = zone.GetComponent<Collider>();
                Vector3 spot = box ? box.bounds.center : zone.position;
                Teleport(spot, spot + zone.forward);
                StartCoroutine(Hold(spot, spot + zone.forward, 0.2f)); // Just long enough to trip it; scripted moves take over.
                plugin.Log("TEST enter " + GraphDump.Path(zone) + " at " + spot.ToString("0.0"));
            }
            else if (action == "watchactive")
            {
                // "path suffix|seconds": log each time that object turns on or off (and its parents' state).
                string[] parts = argument.Split('|');
                var t = Resources.FindObjectsOfTypeAll<Transform>().FirstOrDefault(x => x && x.gameObject.scene.IsValid() && GraphDump.Path(x).EndsWith(parts[0], StringComparison.Ordinal));
                if (!t) throw new InvalidOperationException("no object " + parts[0]);
                StartCoroutine(WatchActive(t, float.Parse(parts[1], System.Globalization.CultureInfo.InvariantCulture)));
            }
            else if (action == "noiseat")
            {
                // "x,y,z|radius": a noise there, as the game makes one (a thing dropped, a door), through the Nun's hearing.
                var parts = argument.Split('|'); var v = parts[0].Split(',');
                var at = new Vector3(float.Parse(v[0], System.Globalization.CultureInfo.InvariantCulture),
                    float.Parse(v[1], System.Globalization.CultureInfo.InvariantCulture),
                    float.Parse(v[2], System.Globalization.CultureInfo.InvariantCulture));
                float radius = parts.Length > 1 ? float.Parse(parts[1], System.Globalization.CultureInfo.InvariantCulture) : 10f;
                plugin.Log("TEST noise at " + at.ToString("0.0") + " radius " + radius.ToString("0.0") + ", "
                    + Vector3.Distance(at, plugin.Session.NunEar ?? at).ToString("0.0") + "m from her ears");
                if (Message.Detectable.NoiseMadeEvent != null) Message.Detectable.NoiseMadeEvent(at, radius);
            }
            else if (action == "hearinglog") { Session.TestHearingLog = argument == "1"; plugin.Log("TEST hearing log " + argument); }
            else if (action == "difficulty")
            {
                DifficultyController.CurrentDifficulty = (DifficultyLevel)Enum.Parse(typeof(DifficultyLevel), argument);
                plugin.Log("TEST difficulty " + argument + " ; " + plugin.Session.Ears.Describe());
            }
            else if (action == "nunhold") { holdNun = argument == "1"; Session.TestHearingLog = holdNun; plugin.Log("TEST nun held " + argument); }
            else if (action == "nunears") plugin.Log("TEST NUNEARS " + plugin.Session.Ears.Describe());
            else if (action == "busylock") { Session.TestNoBusyLock = argument == "0"; plugin.Log("TEST busy lock " + argument); }
            else if (action == "nungraph")
            {
                // The Nun's machine, written out: each state, its actions, and where it goes and on what (to a file).
                var owner = Nun().GetComponent<NodeCanvas.Framework.GraphOwner>();
                var lines = new System.Collections.Generic.List<string>();
                foreach (var graph in new[] { owner.graph }.Concat(owner.graph.GetAllInstancedNestedGraphs()).Distinct())
                {
                    lines.Add("=== " + graph.name + " (" + graph.GetType().Name + ")");
                    foreach (var node in graph.allNodes)
                    {
                        var state = node as NodeCanvas.StateMachines.ActionState;
                        string actions = state != null && state.actionList != null
                            ? string.Join(", ", state.actionList.actions.Select(a => a.GetType().Name + (a is NodeCanvas.Framework.ActionTask ? "[" + ((NodeCanvas.Framework.ActionTask)a).summaryInfo + "]" : "")).ToArray())
                            : node.GetType().Name;
                        lines.Add("[" + node.name + "] " + actions);
                        foreach (var connection in node.outConnections)
                        {
                            var fsmConnection = connection as NodeCanvas.StateMachines.FSMConnection;
                            string condition = fsmConnection != null && fsmConnection.condition != null
                                ? fsmConnection.condition.GetType().Name + ": " + fsmConnection.condition.summaryInfo
                                : "(when done)";
                            lines.Add("    -> " + (connection.targetNode != null ? connection.targetNode.name : "?") + "  if " + condition);
                        }
                    }
                }
                string file = System.IO.Path.Combine(fingerprintDirectory ?? ".", "nun-graph.txt");
                System.IO.File.WriteAllLines(file, lines.ToArray());
                plugin.Log("TEST nungraph " + file + " (" + lines.Count + " lines)");
            }
            else if (action == "nunhear")
            {
                // "x,y,z|strength|kind[|peer]": a sound of a known strength reaching the Nun, to test what she does about it.
                var parts = argument.Split('|'); var v = parts[0].Split(',');
                var at = new Vector3(float.Parse(v[0], System.Globalization.CultureInfo.InvariantCulture),
                    float.Parse(v[1], System.Globalization.CultureInfo.InvariantCulture),
                    float.Parse(v[2], System.Globalization.CultureInfo.InvariantCulture));
                var ear = plugin.Session.NunEar ?? at;
                plugin.Session.NunHeard(new Gameplay.HeardSound
                {
                    Origin = at, Strength = float.Parse(parts[1], System.Globalization.CultureInfo.InvariantCulture),
                        Distance = Vector3.Distance(at, ear), Through = 1f, 
                    Kind = parts.Length > 2 ? (Gameplay.SoundKind)Enum.Parse(typeof(Gameplay.SoundKind), parts[2]) : Gameplay.SoundKind.Noise,
                    Source = parts.Length > 3 ? int.Parse(parts[3]) : 0, Time = Time.time
                });
                plugin.Log("TEST nun hears " + argument);
            }
            else if (action == "bbfloat")
            {
                // "path suffix|variable|value": a number on a machine's blackboard, as the game's input would set it (the
                // barbed wire's struggle).
                var parts = argument.Split('|');
                var owner = Resources.FindObjectsOfTypeAll<NodeCanvas.Framework.GraphOwner>()
                    .Where(o => o && o.gameObject.scene.IsValid() && GraphDump.Path(o.transform).EndsWith(parts[0], StringComparison.Ordinal))
                    .OrderByDescending(o => o.isRunning).FirstOrDefault(); // The one in play, of the scenarios' copies.
                if (!owner || owner.blackboard == null) throw new InvalidOperationException("no blackboard " + parts[0]);
                owner.blackboard.SetValue(parts[1], float.Parse(parts[2], System.Globalization.CultureInfo.InvariantCulture));
                plugin.Log("TEST blackboard " + parts[0] + " " + parts[1] + " = " + parts[2]);
            }
            else if (action == "startending")
            {
                // "path suffix": an ending's machine started here, as walking into its trigger starts it.
                var owner = Resources.FindObjectsOfTypeAll<NodeCanvas.StateMachines.FSMOwner>().FirstOrDefault(o => o && o.gameObject.scene.IsValid() && GraphDump.Path(o.transform).EndsWith(argument, StringComparison.Ordinal));
                if (!owner) throw new InvalidOperationException("no ending " + argument);
                if (!owner.gameObject.activeSelf) owner.gameObject.SetActive(true);
                owner.StartBehaviour();
                plugin.Log("TEST started ending " + GraphDump.Path(owner.transform));
            }
            else if (action == "reviverequest")
            {
                // "peer|kit": a revive request sent straight away, with no hold (what a tampered client could send).
                var parts = argument.Split('|');
                plugin.Session.Request(new Packet { action = "revive", target = parts[0], text = parts.Length > 1 ? parts[1] : "" });
                plugin.Log("TEST revive request " + argument);
            }
            else if (action == "nunforget") { plugin.Session.TestForgetSounds(); plugin.Log("TEST nun forgets what she heard"); }
            else if (action == "nundiag") { Players.EnemyDirector.Diagnostics = argument == "1"; plugin.Log("TEST nun diagnostics " + argument); }
            else if (action == "nuntrack")
            {
                // "seconds|label|x,y,z": twice a second, where the Nun is, her state, which way she faces and how far she is
                // from the point.
                var parts = argument.Split('|'); var v = parts[2].Split(',');
                var point = new Vector3(float.Parse(v[0], System.Globalization.CultureInfo.InvariantCulture),
                    float.Parse(v[1], System.Globalization.CultureInfo.InvariantCulture),
                    float.Parse(v[2], System.Globalization.CultureInfo.InvariantCulture));
                StartCoroutine(TrackNun(float.Parse(parts[0], System.Globalization.CultureInfo.InvariantCulture), parts[1], point));
            }
            else if (action == "playersteps")
            {
                // Every machine that changes the player's state: each state node doing it, and to what.
                foreach (var o in Resources.FindObjectsOfTypeAll<NodeCanvas.Framework.GraphOwner>().Where(o => o && o.gameObject.scene.IsValid() && o.graph != null))
                {
                    var steps = new System.Collections.Generic.List<string>();
                    foreach (var node in o.graph.allNodes)
                    {
                        var state = node as NodeCanvas.StateMachines.ActionState;
                        if (state == null || state.actionList == null) continue;
                        foreach (var task in state.actionList.actions)
                        {
                            var change = task as GrannyClone.Tasks.PlayerStateChange;
                            if (change != null) steps.Add(state.name + "->" + change.newState);
                        }
                    }
                    if (steps.Count > 0) plugin.Log("TEST PLAYERSTEPS " + GraphDump.Path(o.transform) + " :: " + string.Join(", ", steps.ToArray()));
                }
            }
            else if (action == "fsmtasks")
            {
                // The running state's actions: which are still running (why a sequence is stuck).
                foreach (var o in Resources.FindObjectsOfTypeAll<NodeCanvas.StateMachines.FSMOwner>().Where(o => o && o.gameObject.scene.IsValid() && GraphDump.Path(o.transform).Contains(argument)))
                {
                    var fsm = o.graph as NodeCanvas.StateMachines.FSM;
                    var state = fsm == null ? null : fsm.currentState as NodeCanvas.StateMachines.ActionState;
                    plugin.Log("TEST FSMTASKS " + GraphDump.Path(o.transform) + " state=" + (state != null ? state.name : "-") + " mode=" + (state != null ? state.actionList.executionMode.ToString() : "-") + " :: "
                        + (state == null ? "" : string.Join(" | ", state.actionList.actions.Select(a => a.GetType().Name + (a.isRunning ? " RUNNING " + a.elapsedTime.ToString("0.0") + "s" : a.isPaused ? " paused" : " idle")).ToArray())));
                }
            }
            else if (action == "states")
            {
                var states = HarmonyLib.AccessTools.Field(typeof(GrannyClone.StateInteractable), "states");
                foreach (var i in Resources.FindObjectsOfTypeAll<GrannyClone.StateInteractable>().Where(i => i && i.gameObject.scene.IsValid() && GraphDump.Path(i.transform).Contains(argument)))
                {
                    var list = states.GetValue(i) as GrannyClone.StateInteractable.InteractionState[];
                    plugin.Log("TEST STATES " + GraphDump.Path(i.transform) + " at " + i.transform.position.ToString("0.0")
                        + " active=" + i.gameObject.activeInHierarchy + " :: "
                        + (list == null ? "-" : string.Join(" | ", list.Select((x, n) => n + ":'" + x.InteractionString + "' item=" + (x.ItemRequired ? x.ItemRequired.name : "-") + " next=" + x.NextState + " events=" + x.OnStartEvent.GetPersistentEventCount()).ToArray())));
                }
            }
            else if (action == "fsm")
            {
                plugin.Log("TEST FSM " + string.Join(" ; ", Resources.FindObjectsOfTypeAll<NodeCanvas.StateMachines.FSMOwner>()
                    .Where(o => o && o.gameObject.scene.IsValid() && GraphDump.Path(o.transform).Contains(argument))
                    .Select(o => { var fsm = o.graph as NodeCanvas.StateMachines.FSM; return GraphDump.Path(o.transform) + " running=" + o.isRunning + " state=" + (fsm != null && fsm.currentState != null ? fsm.allNodes.IndexOf(fsm.currentState) + ":" + fsm.currentState.name : "-")
                        + " bb[" + string.Join(",", o.blackboard.variables.Values.Where(v => v != null && v.value != null && (v.value is int || v.value is bool || v.value is float)).Select(v => v.name + "=" + v.value).ToArray()) + "]"; }).ToArray()));
            }
            else if (action == "skipcredits")
            {
                // The end credits rolling here skipped to their end, as Escape does: their machine's Quit state.
                var owner = Resources.FindObjectsOfTypeAll<NodeCanvas.StateMachines.FSMOwner>()
                    .FirstOrDefault(o => o && o.gameObject.scene.IsValid() && o.isRunning && GraphDump.Path(o.transform).EndsWith("Credits OFF", StringComparison.Ordinal));
                var fsm = owner ? owner.graph as NodeCanvas.StateMachines.FSM : null;
                var quit = fsm == null ? null : fsm.allNodes.OfType<NodeCanvas.StateMachines.ActionState>()
                    .FirstOrDefault(n => n.actionList != null && n.actionList.actions.Any(t => t is NodeCanvas.Tasks.Actions.Quit));
                var enter = typeof(NodeCanvas.StateMachines.FSM).GetMethods().FirstOrDefault(m => m.Name == "EnterState" && m.GetParameters().Length >= 1
                    && m.GetParameters()[0].ParameterType == typeof(NodeCanvas.StateMachines.FSMState));
                if (quit == null || enter == null) { plugin.Log("TEST skip credits: no credits rolling"); }
                else
                {
                    var arguments = enter.GetParameters().Select((q, i) => i == 0 ? (object)quit : (q.HasDefaultValue ? q.DefaultValue : Activator.CreateInstance(q.ParameterType))).ToArray();
                    enter.Invoke(fsm, arguments);
                    plugin.Log("TEST skip credits");
                }
            }
            else if (action == "press") { if (argument.Length > 0) PressInGraph = argument; else PressPending = true; plugin.Log("TEST press " + argument); }
            else if (action == "equip")
            {
                var inventory = plugin.Session.Local.inventory;
                var items = HarmonyLib.AccessTools.Field(typeof(GrannyClone.Inventory),
                    "items").GetValue(inventory) as System.Collections.Generic.List<GrannyClone.ItemInteractable>;
                var item = items == null ? null : items.FirstOrDefault(i => i && i.name.StartsWith(argument, StringComparison.OrdinalIgnoreCase));
                if (!item) throw new InvalidOperationException("not carrying " + argument);
                var change = HarmonyLib.AccessTools.Method(typeof(GrannyClone.Inventory), "changeItemAnimation");
                MEC.Timing.RunCoroutine((System.Collections.Generic.IEnumerator<float>)change.Invoke(inventory, new object[] { item }));
                plugin.Log("TEST equip " + item.name);
            }
            else if (action == "pickgame")
            {
                // As the player finishing the lockpicking minigame on screen: "solved" or "end" (gave up).
                int count = 0;
                foreach (var pick in Resources.FindObjectsOfTypeAll<GrannyClone.PickLock>().Where(x => x && x.gameObject.scene.IsValid() && x.gameObject.activeInHierarchy))
                {
                    var ui = HarmonyLib.Traverse.Create(pick).Field("uiCanvas").GetValue<GameObject>();
                    if (!ui || !ui.activeSelf) continue; // Only the one open on this screen.
                    HarmonyLib.AccessTools.Method(typeof(GrannyClone.PickLock), argument == "solved" ? "solved" : "end").Invoke(pick, null);
                    count++;
                }
                plugin.Log("TEST pickgame " + argument + " on " + count + " open lock(s)");
            }
            else if (action == "combo")
            {
                // As the player turning the padlock's cylinders, one click at a time, to its combination.
                var padlock = Resources.FindObjectsOfTypeAll<GrannyClone.CombinationLock>().FirstOrDefault(x => x && x.gameObject.scene.IsValid() && x.gameObject.activeInHierarchy && GraphDump.Path(x.transform).Contains(argument));
                if (!padlock) throw new InvalidOperationException("no padlock " + argument);
                StartCoroutine(TurnCombination(padlock));
            }
            else if (action == "clocksolve")
            {
                // As the player turning the hands to the right time.
                foreach (var clock in GameHooks.localClocks) { clock.hour = clock.SolvedHour.value; clock.minutes = clock.SolvedMinutes.value; }
                plugin.Log("TEST clock hands set on " + GameHooks.localClocks.Count + " open clock minigame(s)");
            }
            else if (action == "aiprobe")
            {
                var owner = Nun().GetComponent<NodeCanvas.Framework.GraphOwner>();
                var report = new System.Text.StringBuilder("TEST AIPROBE");
                foreach (var variable in owner.blackboard.variables.Values)
                    report.Append("\n  var " + variable.name + " : "
                        + (variable.varType == null ? "?" : variable.varType.Name) + " = "
                        + (variable.value is UnityEngine.Object ? ((UnityEngine.Object)variable.value ? ((UnityEngine.Object)variable.value).name : "null") : Convert.ToString(variable.value)));
                foreach (var task in owner.graph.GetAllTasksOfType<NodeCanvas.Framework.Task>())
                {
                    string type = task.GetType().Name;
                    if (argument.Length > 0 && !argument.Split(',').Contains(type)) continue;
                    report.Append("\n  task " + type + ":");
                    foreach (var field in task.GetType().GetFields(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance))
                    {
                        var parameter = field.GetValue(task) as NodeCanvas.Framework.BBParameter;
                        if (parameter == null) continue;
                        object value = parameter.value;
                        report.Append(" " + field.Name + "=" + (parameter.useBlackboard ? "$" + parameter.name : "") + "(" + (value is UnityEngine.Object ? ((UnityEngine.Object)value ? ((UnityEngine.Object)value).name : "null") : Convert.ToString(value)) + ")");
                    }
                }
                plugin.Log(report.ToString());
            }
            else if (action == "hunt")
            {
                var owner = Nun().GetComponent<NodeCanvas.Framework.GraphOwner>();
                owner.SendEvent("SightAlert");
                plugin.Log("TEST hunt sent");
            }
            else if (action == "life")
            {
                var local = plugin.Session.Local;
                plugin.Log("TEST LIFE state=" + plugin.Session.Life.State + " health=" + (local ? local.health.HealthMeter : -1) + " bleed=" + plugin.Session.Life.Bleed
                    + " lives=" + (LivesSystem.instance ? LivesSystem.instance.currentLives : -1) + " target=" + plugin.Session.EnemyTarget + " candidate=" + plugin.Session.Life.ReviveCandidate + " reviving=" + plugin.Session.Life.Reviving + "/" + plugin.Session.Life.ReviveProgress.ToString("0.00")
                    + " pstate=" + (local ? local.playerStates.StateCheck().ToString() : "") + " pos=" + (local ? ((Component)local).transform.position.ToString() : ""));
            }
            else if (action == "disconnect") plugin.Session.Disconnect("Test disconnect.");
            else if (action == "state")
                plugin.Log("TEST STATE " + plugin.Session.Status + " | players="
                    + string.Join(",", plugin.Session.PlayerNames()) + " | scene=" + SceneManager.GetActiveScene().name
                    + " | ready=" + plugin.Session.Ready + " | online=" + plugin.Session.OnlineStatus + " invite="
                    + plugin.Session.Invite);
        }

        // Where a player would stand to reach a point: the nearest walkable floor within reach, else
        // in front of it at eye level.
        private Vector3 StandingSpot(Vector3 point)
        {
            UnityEngine.AI.NavMeshHit hit;
            for (float radius = 1.5f; radius <= 4f; radius += 1.25f)
                if (UnityEngine.AI.NavMesh.SamplePosition(point + Vector3.down * 1.4f, out hit, radius, UnityEngine.AI.NavMesh.AllAreas)
                    && Vector3.Distance(hit.position, point) < 3.5f)
                    return hit.position + Vector3.up * 0.1f;
            Vector3 from = ((Component)plugin.Session.Local).transform.position;
            Vector3 away = from - point; away.y = 0f;
            return point + (away.sqrMagnitude > 0.01f ? away.normalized : Vector3.back) * 0.9f + Vector3.down * 1.5f;
        }

        private static Vector3 RimSpot(Vector3 point)
        {
            Vector3 best = point; float height = float.MinValue;
            for (int i = 0; i < 16; i++)
            {
                Vector3 around = point + Quaternion.Euler(0f, i * 22.5f, 0f) * Vector3.forward * 2f;
                UnityEngine.AI.NavMeshHit hit;
                if (UnityEngine.AI.NavMesh.SamplePosition(around, out hit, 1.5f, UnityEngine.AI.NavMesh.AllAreas) && hit.position.y > height)
                {
                    height = hit.position.y;
                    best = hit.position + Vector3.up * 0.1f;
                }
            }
            return best;
        }

        private System.Collections.IEnumerator Hold(Vector3 spot, Vector3 lookAt, float seconds)
        {
            float until = Time.realtimeSinceStartup + seconds;
            while (Time.realtimeSinceStartup < until)
            {
                var body = ((Component)plugin.Session.Local).transform;
                if (Vector3.Distance(body.position, spot) > 0.3f)
                {
                    var cc = plugin.Session.Local.characterController;
                    if (cc) cc.enabled = false;
                    body.position = spot;
                    if (cc) cc.enabled = true;
                }
                yield return null;
            }
        }

        private System.Collections.IEnumerator TurnCombination(GrannyClone.CombinationLock padlock)
        {
            var combination = HarmonyLib.Traverse.Create(padlock).Field("combination").GetValue<int[]>();
            var cylinders = HarmonyLib.Traverse.Create(padlock).Field("cylinder").GetValue<GrannyClone.CombinationLockCylinder[]>();
            plugin.Log("TEST combo " + string.Join("", combination.Select(d => d.ToString()).ToArray()) + " from "
                + string.Join("", cylinders.Select(c => c.Number.ToString()).ToArray()));
            for (int i = 0; i < cylinders.Length; i++)
                for (int tries = 0; tries < 12 && cylinders[i].Number != combination[i]; tries++)
                {
                    cylinders[i].SelectLock();
                    yield return new WaitForSecondsRealtime(0.6f);
                }
            plugin.Log("TEST combo now " + string.Join("", cylinders.Select(c => c.Number.ToString()).ToArray()));
        }

        private System.Collections.IEnumerator WalkInto(Vector3 target)
        {
            var local = plugin.Session.Local;
            var body = ((Component)local).transform;
            float until = Time.realtimeSinceStartup + 2f;
            while (Time.realtimeSinceStartup < until)
            {
                Vector3 toward = target - body.position; toward.y = 0f;
                if (local.characterController && toward.sqrMagnitude > 0.0001f)
                    local.characterController.Move(toward.normalized * 2.5f * Time.deltaTime + Vector3.down * 0.05f);
                yield return null;
            }
            Vector3 gap = target - body.position; gap.y = 0f;
            Transform mate = plugin.Session.Avatars.GetTransform(1);
            Vector3 apart = mate ? mate.position - body.position : Vector3.zero; apart.y = 0f;
            plugin.Log("TEST walkinto ended " + gap.magnitude.ToString("0.00") + " m from where the teammate was, "
                + apart.magnitude.ToString("0.00") + " m from the host now");
        }

        /// <summary>Walk ahead at a normal pace through the character controller (footsteps for the others).</summary>
        private System.Collections.IEnumerator Stroll(float seconds, float pace)
        {
            var local = plugin.Session.Local;
            var body = ((Component)local).transform;
            Vector3 start = body.position, ahead = Vector3.ProjectOnPlane(body.forward, Vector3.up).normalized;
            float until = Time.realtimeSinceStartup + seconds;
            while (Time.realtimeSinceStartup < until)
            {
                if (local.characterController) local.characterController.Move(ahead * pace * Time.deltaTime + Vector3.down * 0.05f);
                yield return null;
            }
            plugin.Log("TEST stroll moved " + Vector3.Distance(start, body.position).ToString("0.00") + " m");
        }

        private System.Collections.IEnumerator UseAll(System.Collections.Generic.List<GrannyClone.Interactable> targets)
        {
            var used = new System.Collections.Generic.List<string>();
            foreach (var target in targets)
            {
                if (!target || !target.gameObject.activeInHierarchy) continue;
                Vector3 stand = StandingSpot(target.CenterPt);
                Teleport(stand, target.CenterPt);
                yield return new WaitForSeconds(0.4f);
                if (!target) continue;
                target.Interact();
                used.Add(target.GetType().Name + " " + GraphDump.Path(target.transform));
                yield return new WaitForSeconds(0.6f);
            }
            plugin.Log("TEST USEALL " + used.Count + " ; " + string.Join(" ; ", used.ToArray()));
        }

        /// <summary>Every state the Nun's machine passes through for a while (tests).</summary>
        private System.Collections.IEnumerator WatchNunStates(float seconds)
        {
            var fsm = Nun().GetComponent<NodeCanvas.StateMachines.FSMOwner>();
            string last = null; float until = Time.time + seconds;
            var trail = new System.Collections.Generic.List<string>();
            while (Time.time < until && fsm)
            {
                var graph = fsm.graph as NodeCanvas.StateMachines.FSM;
                string state = graph != null ? graph.currentStateName : "?";
                if (state != last) { trail.Add(Time.time.ToString("0.00") + " " + state); last = state; }
                yield return null;
            }
            plugin.Log("TEST NUNSTATES " + string.Join(" > ", trail.ToArray()));
        }

        private Transform recordSpot;
        private float recordAim = -1f;
        /// <summary>Walk up to the nearest free item with this name and pick it up through the normal path. If a teammate
        /// takes it first (two scripted players after the same item), go for the next free one.</summary>
        private static Bounds Bounds(GameObject thing)
        {
            var renderers = thing.GetComponentsInChildren<Renderer>();
            if (renderers.Length == 0) return new Bounds(thing.transform.position, Vector3.zero);
            Bounds box = renderers[0].bounds;
            foreach (var r in renderers) box.Encapsulate(r.bounds);
            return box;
        }

        private void PickNearest(string name, int retries)
        {
            // "name@x,z": the one nearest that spot (two scripted players each taking their own), else nearest the player.
            Vector3 from = ((Component)plugin.Session.Local).transform.position;
            int at = name.IndexOf('@');
            if (at > 0)
            {
                var xz = name.Substring(at + 1).Split(',');
                from = new Vector3(float.Parse(xz[0], System.Globalization.CultureInfo.InvariantCulture), from.y,
                    float.Parse(xz[1], System.Globalization.CultureInfo.InvariantCulture));
                name = name.Substring(0, at);
                retries = 0;
            }
            var item = Resources.FindObjectsOfTypeAll<GrannyClone.ItemInteractable>()
                .Where(i => i && i.gameObject.scene.IsValid() && i.gameObject.activeInHierarchy && i.name.StartsWith(name, StringComparison.OrdinalIgnoreCase) && !i.CantPickUp && plugin.Session.OwnerOf(plugin.Session.IdOf(i)) == 0
                    && i.transform.position.y > -50f) // Not one the game has put away under the level (a tape already played).
                .OrderBy(i => Vector3.Distance(i.transform.position, from)).FirstOrDefault();
            if (!item) throw new InvalidOperationException("no free item " + name);
            Vector3 stand = StandingSpot(item.CenterPt);
            Teleport(stand, item.CenterPt);
            StartCoroutine(Hold(stand, item.CenterPt, 0.8f));
            // As a player would: arrive (so the host has our position), then interact.
            StartCoroutine(Later(0.6f, () =>
            {
                if (retries > 0 && (plugin.Session.OwnerOf(plugin.Session.IdOf(item)) != 0 || Vector3.Distance(item.CenterPt, stand) > 4f))
                {
                    plugin.Log("TEST pick " + GraphDump.Path(item.transform) + " was taken first (now at " + item.CenterPt.ToString("0.0") + "); the next one");
                    PickNearest(name, retries - 1);
                    return;
                }
                item.Interact(); plugin.Log("TEST pick " + GraphDump.Path(item.transform));
            }));
        }

        private Action<Transform, float, Collider, bool> noiseWatch;

        private float watchNunUntil;
        private bool watchingNun;
        private void AimAtNun(Camera camera)
        {
            if (Time.realtimeSinceStartup > watchNunUntil) { Camera.onPreCull -= AimAtNun; watchingNun = false; return; }
            var local = plugin.Session.Local;
            if (!local || !local.camera || camera != local.camera) return;
            Transform nun;
            try { nun = Nun(); } catch { return; }
            Vector3 face = nun.position + Vector3.up * 1.62f - camera.transform.position;
            if (face.sqrMagnitude > 0.01f) camera.transform.rotation = Quaternion.LookRotation(face);
        }

        private System.Collections.IEnumerator Burst(float seconds, float fps)
        {
            float start = Time.realtimeSinceStartup, next = start;
            int frame = 0;
            while (Time.realtimeSinceStartup - start < seconds)
            {
                if (Time.realtimeSinceStartup >= next)
                {
                    next += 1f / fps;
                    ScreenCapture.CaptureScreenshot(uiCapture + "-burst" + frame.ToString("0000") + ".png");
                    frame++;
                }
                yield return null;
            }
            plugin.Log("TEST burst " + frame + " frames");
        }

        private System.Collections.IEnumerator Record(int peer, Vector3 offset, string name, float seconds, float fps)
        {
            float aimHeight = recordAim;
            string directory = System.IO.Path.Combine(fingerprintDirectory ?? ".", "rec-" + name);
            System.IO.Directory.CreateDirectory(directory);
            foreach (var old in System.IO.Directory.GetFiles(directory, "f*.*")) System.IO.File.Delete(old);
            var holder = new GameObject("NMM_RecordCamera");
            var lamp = new GameObject("NMM_RecordLamp").AddComponent<Light>();
            lamp.transform.SetParent(holder.transform, false);
            lamp.type = LightType.Point; lamp.range = 9f; lamp.intensity = 1.5f;
            var camera = holder.AddComponent<Camera>();
            camera.fieldOfView = 55f; camera.nearClipPlane = 0.05f; camera.enabled = false;
            var target = new RenderTexture(960, 540, 24);
            var picture = new Texture2D(960, 540, TextureFormat.RGB24, false);
            camera.targetTexture = target;
            int frame = 0;
            float start = Time.realtimeSinceStartup, next = start;
            Vector3 aim = Vector3.zero; bool aimed = false;
            while (Time.realtimeSinceStartup - start < seconds)
            {
                if (Time.realtimeSinceStartup >= next)
                {
                    next += 1f / fps;
                    Transform body = peer < 0 ? recordSpot : peer == 0 ? Nun() : plugin.Session.Avatars.GetTransform(peer); // 0: film the Nun; -1: a spot.
                    if (body)
                    {
                        Vector3 point = body.position + Vector3.up * (aimHeight >= 0f ? aimHeight : peer == 0 ? 0.9f : 0.45f);
                        aim = aimed ? Vector3.Lerp(aim, point, 0.35f) : point; aimed = true; // A steady follow, not a jitter.
                        holder.transform.position = aim + offset;
                        holder.transform.LookAt(aim);
                    }
                    camera.Render();
                    var prior = RenderTexture.active;
                    RenderTexture.active = target;
                    picture.ReadPixels(new Rect(0, 0, 960, 540), 0, 0);
                    RenderTexture.active = prior;
                    bool video = name.StartsWith("v-", StringComparison.Ordinal); // Video frames: JPEG, quick to write at 20 a second.
                    System.IO.File.WriteAllBytes(System.IO.Path.Combine(directory, "f" + frame.ToString("0000") + (video ? ".jpg" : ".png")),
                        video ? picture.EncodeToJPG(92) : picture.EncodeToPNG());
                    frame++;
                }
                yield return null;
            }
            camera.targetTexture = null;
            UnityEngine.Object.Destroy(target); UnityEngine.Object.Destroy(picture); UnityEngine.Object.Destroy(holder);
            plugin.Log("TEST recorded " + frame + " frames to " + directory);
        }

        private static System.Collections.IEnumerator Later(float seconds, Action action)
        {
            yield return new WaitForSecondsRealtime(seconds);
            action();
        }

        private static string RequiredItem(GrannyClone.StateInteractable state)
        {
            var current = HarmonyLib.AccessTools.Field(typeof(GrannyClone.StateInteractable),
                "stateCurrent").GetValue(state) as GrannyClone.StateInteractable.InteractionState;
            return current == null || !current.ItemRequired ? "-" : current.ItemRequired.name;
        }

        private System.Collections.IEnumerator TrackNun(float seconds, string label, Vector3 point)
        {
            float end = Time.time + seconds, nearest = float.MaxValue;
            while (Time.time < end)
            {
                var nun = Nun();
                var fsm = nun.GetComponent<NodeCanvas.StateMachines.FSMOwner>();
                string state = fsm && fsm.graph is NodeCanvas.StateMachines.FSM ? ((NodeCanvas.StateMachines.FSM)fsm.graph).currentStateName : "-";
                Vector3 to = point - nun.position; to.y = 0f;
                float distance = Vector3.Distance(nun.position, point);
                nearest = Mathf.Min(nearest, distance);
                plugin.Log("TEST TRACK " + label + " t=" + (seconds - (end - Time.time)).ToString("0.0") + " at " + nun.position.ToString("0.0") + " state=" + state
                    + " facing-off=" + (to.sqrMagnitude > 0.01f ? Vector3.Angle(nun.forward, to).ToString("0") : "-") + " dist=" + distance.ToString("0.0"));
                yield return new WaitForSeconds(0.5f);
            }
            plugin.Log("TEST TRACK " + label + " closest " + nearest.ToString("0.0") + "m");
        }

        private bool holdNun;
        /// <summary>Tests: the Nun stays where she is, her mind paused (what she hears is still weighed and logged).</summary>
        private void HoldNun()
        {
            try
            {
                var nun = Nun();
                var mind = nun.GetComponent<NodeCanvas.Framework.GraphOwner>();
                if (mind && mind.isRunning && !mind.isPaused) mind.PauseBehaviour();
                var agent = nun.GetComponent<UnityEngine.AI.NavMeshAgent>();
                if (agent && agent.enabled && agent.isOnNavMesh) { agent.isStopped = true; agent.velocity = Vector3.zero; }
            }
            catch (InvalidOperationException) { }
        }

        private Transform Nun()
        {
            var difficulty = UnityEngine.Object.FindObjectOfType<DifficultyController>();
            var fsm = difficulty ? HarmonyLib.AccessTools.Field(typeof(DifficultyController), "NunFSM").GetValue(difficulty) as Component : null;
            if (!fsm) throw new InvalidOperationException("no Nun");
            return fsm.transform;
        }

        private System.Collections.IEnumerator WatchActive(Transform t, float seconds)
        {
            float until = Time.realtimeSinceStartup + seconds;
            bool? last = null;
            while (Time.realtimeSinceStartup < until && t)
            {
                bool now = t.gameObject.activeInHierarchy;
                if (last != now)
                {
                    Transform off = t; while (off && off.gameObject.activeSelf) off = off.parent;
                    plugin.Log("TEST ACTIVE " + GraphDump.Path(t) + " " + now + " self=" + t.gameObject.activeSelf
                        + (off ? " (off: " + GraphDump.Path(off) + ")" : "") + " frame " + Time.frameCount);
                    last = now;
                }
                yield return null;
            }
        }

        private System.Collections.IEnumerator TestJump()
        {
            var local = plugin.Session.Local;
            var controller = local.firstPersonController;
            var body = local.characterController;
            float speed = (float)HarmonyLib.AccessTools.Field(typeof(GrannyClone.FirstPersonController), "jumpSpeed").GetValue(controller);
            var gravityField = HarmonyLib.AccessTools.Field(typeof(GrannyClone.FirstPersonController), "gravityMultiplier");
            float gravity = Physics.gravity.y * (gravityField != null ? (float)gravityField.GetValue(controller) : 1f);
            float start = body.transform.position.y, rise = speed, top = start, time = 0f;
            controller.isJumping = true;
            yield return null;
            while (time < 3f)
            {
                float dt = Time.deltaTime;
                time += dt;
                rise += gravity * dt;
                body.Move(Vector3.up * rise * dt);
                top = Mathf.Max(top, body.transform.position.y);
                if (rise < 0f && (body.isGrounded || body.transform.position.y <= start)) break;
                yield return null;
            }
            controller.isJumping = false;
            plugin.Log("TEST jump " + (top - start).ToString("0.00") + " m high, " + time.ToString("0.00")
                + " s in the air (speed " + speed + ", gravity " + gravity.ToString("0.0") + ")");
        }

        private void Teleport(Vector3 position, Vector3 lookAt)
        {
            var local = plugin.Session.Local;
            var body = ((Component)local).transform;
            if (local.characterController) local.characterController.enabled = false;
            body.position = position;
            Vector3 flat = lookAt - position; flat.y = 0f;
            if (flat.sqrMagnitude > 0.01f) body.rotation = Quaternion.LookRotation(flat);
            if (local.characterController) local.characterController.enabled = true;
            plugin.Log("TEST teleported to " + position);
        }

        // Logs the physics setup that decides whether the Nun can see and hit a player.
        private void Probe()
        {
            System.Func<int,
                string> mask = m => string.Join(",", Enumerable.Range(0, 32).Where(i => (m & (1 << i)) != 0).Select(i => LayerMask.LayerToName(i) + "(" + i + ")").ToArray());
            var local = plugin.Session.Local;
            try { ProbeDetail(mask, local); } catch (Exception ex) { plugin.Log("PROBE failed " + ex); }
        }
        private void ProbeDetail(System.Func<int, string> mask, GrannyClone.pReferences local)
        {
            plugin.Log("PROBE queriesHitTriggers=" + Physics.queriesHitTriggers + " Player layer=" + LayerMask.NameToLayer("Player"));
            if (local)
            {
                var sight = ((Component)local).GetComponent<Detectable.SightDetectable>();
                if (sight)
                    foreach (var t in sight.SightColliders)
                        plugin.Log("PROBE sight collider "
                            + (t ? GraphDump.Path(t) + " layer=" + LayerMask.LayerToName(t.gameObject.layer) + " comps=" + string.Join(",", t.GetComponents<Component>().Where(c => c != null).Select(c => c.GetType().Name + (c is Collider ? "(trigger=" + ((Collider)c).isTrigger + ")" : "")).ToArray()) + " local=" + t.localPosition : "null"));
                foreach (var c in ((Component)local).GetComponentsInChildren<Collider>(true))
                {
                    try { plugin.Log("PROBE player collider " + GraphDump.Path(c.transform) + " " + c.GetType().Name
                        + " trigger=" + c.isTrigger + " layer=" + LayerMask.LayerToName(c.gameObject.layer) + " tag="
                        + c.gameObject.tag); }
                    catch (Exception ex) { plugin.Log("PROBE collider failed " + ex); }
                }
            }
            foreach (var ray in Resources.FindObjectsOfTypeAll<GrannyClone.RaycastDamage>().Where(r => r && r.gameObject.scene.IsValid()))
            {
                try { plugin.Log("PROBE RaycastDamage " + GraphDump.Path(ray.transform) + " layers="
                    + mask(((LayerMask)HarmonyLib.AccessTools.Field(typeof(GrannyClone.RaycastDamage), "layers").GetValue(ray)).value)
                    + " dmg="
                    + HarmonyLib.AccessTools.Field(typeof(GrannyClone.RaycastDamage), "damageAmount").GetValue(ray)); }
                catch (Exception ex) { plugin.Log("PROBE ray failed " + ex.Message); }
            }
            foreach (var owner in Resources.FindObjectsOfTypeAll<NodeCanvas.Framework.GraphOwner>().Where(o => o && o.gameObject.scene.IsValid() && o.graph && o.graph.name.StartsWith("Killer")))
                foreach (var task in owner.graph.GetAllTasksOfType<Humanoid.Agent.EnemyHunter.Tasks.CanSeeSightDetectable>())
                {
                    try { plugin.Log("PROBE sight task layers="
                        + (task.sightLayers == null ? "null" : mask(task.sightLayers.value.value)) + " ignore=" + task.Ignore
                        + " target=" + task.SightDetectableTarget + " offsetT=" + task.offsetTarget + " aware="
                        + (task.awarnessDistance == null ? "" : task.awarnessDistance.value.ToString())); }
                    catch (Exception ex) { plugin.Log("PROBE task failed " + ex.Message); }
                }
        }

        // Writes the UI hierarchy and two frames of each scene reached, for menu development.
        private void CaptureUi()
        {
            string name = SceneManager.GetActiveScene().name;
            if (name != capturedScene) { capturedScene = name; sceneSeen = Time.realtimeSinceStartup; captureCount = 0; }
            float age = Time.realtimeSinceStartup - sceneSeen;
            if ((captureCount == 0 && age > 4) || (captureCount == 1 && age > 10))
            {
                captureCount++;
                string prefix = uiCapture + "-" + name.Replace(' ', '_') + "-" + captureCount;
                try { UiInspector.DumpHierarchy(prefix + ".ui.txt"); UiInspector.Screenshot(prefix + ".png", 1280,
                    720); plugin.Log("TEST UI captured " + prefix); }
                catch (Exception ex) { plugin.Log("TEST UI capture failed: " + ex); }
            }
        }
    }
}
