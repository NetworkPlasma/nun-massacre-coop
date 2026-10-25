using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using DG.Tweening;
using GrannyClone;
using HarmonyLib;
using NunMassacre.Multiplayer.Players;
using Rewired;
using UnityEngine;

namespace NunMassacre.Multiplayer.Gameplay
{
    internal enum LifeState { Alive, Downed, Dying, Spectating, GameOver }

    /// <summary>
    /// Co-op life rules for the local player. A lethal hit knocks the player down instead of
    /// killing them; a teammate can revive them before they bleed out. Bleeding out (or the whole
    /// team going down) costs a life through the game's own death/respawn sequence. A player with
    /// no lives left spectates; when everyone is out, every peer gets the game over.
    /// </summary>
    internal sealed class CoopLife
    {
        internal const float ReviveSeconds = 4f, KitReviveSeconds = 1.5f, ReviveRange = 2.2f;
        internal const float RevivedHealth = 35f, KitRevivedHealth = 100f, InvulnerableSeconds = 3f;
        private static readonly FieldInfo FallTime = AccessTools.Field(typeof(FirstPersonController), "fallTime");
        private static readonly FieldInfo FallTimeDeath = AccessTools.Field(typeof(FirstPersonController), "fallTimeDeath");
        private static readonly FieldInfo HideExitPoint = AccessTools.Field(typeof(PlayerStates), "exitPt");

        private readonly Session session;
        private readonly Plugin plugin;
        private Player input;
        private float invulnerableUntil, nextGroan, giveUp, nextHostCheck;
        private bool allowDeath, allowGameOver, wipeIssued, headBob = true;
        private int watching;
        // Spectator free camera: fly anywhere (through walls) instead of following a teammate.
        private bool freeCam;
        private Vector3 freePosition;
        private float freeYaw, freePitch;
        internal bool FreeCamera { get { return freeCam; } }
        internal static bool TestFreeCam; // Automated tests: fly forward in the free camera.
        private Vector3 spectatorPosition;
        private Quaternion spectatorRotation = Quaternion.identity;
        private bool spectatorPlaced;
        // The game's blue moonlight hangs off the player's body; the parked body would take it away and leave the
        // spectator camera in flat, untinted light. It rides along with the camera instead.
        private Transform blueLight;
        private Vector3 blueLightHome, blueLightOffset;
        private string reviveName = "";

        internal LifeState State { get; private set; }
        internal float Bleed { get; private set; }
        /// <summary>Peer this player is reviving right now (0 = none) and the progress, 0..1.</summary>
        internal int Reviving { get; private set; }
        internal float ReviveProgress { get; private set; }
        /// <summary>A downed teammate within reach whom this player could revive.</summary>
        internal int ReviveCandidate { get; private set; }
        internal bool TeamOver { get; set; }
        private float normalFov = 70f; // The player's usual zoom, restored after a knockdown mid close-up.
        /// <summary>Automated tests: behave as if interact is held.</summary>
        internal static bool TestHoldAction;
        /// <summary>Automated tests: the Nun's hits do nothing (long scripted playthroughs).</summary>
        internal static bool TestInvulnerable;
        internal void TestSetBleed(float seconds) { if (State == LifeState.Downed) Bleed = seconds; }

        internal CoopLife(Session session, Plugin plugin)
        {
            this.session = session;
            this.plugin = plugin;
            Camera.onPreCull += PlaceSpectatorCamera;
        }

        private pReferences Local { get { return session.Local; } }
        private bool InCoopGame { get { return session.Active && session.Ready && Local && Local.health; } }
        internal bool Down { get { return State == LifeState.Downed || State == LifeState.Dying; } }
        internal bool Targetable { get { return State == LifeState.Alive; } }
        /// <summary>Interact presses go to reviving instead of the world while a revive is possible.</summary>
        internal bool BlocksInteraction { get { return InCoopGame && (State != LifeState.Alive || ReviveCandidate != 0); } }

        internal void Reset()
        {
            if (State == LifeState.Spectating) RestoreBody();
            State = LifeState.Alive; Bleed = 0; Reviving = 0; ReviveProgress = 0; ReviveCandidate = 0;
            allowDeath = allowGameOver = wipeIssued = false; spectatorPlaced = false; watching = 0; freeCam = false; watchEyes = false; safeKnown = false;
            BleedBehavior.minBloodAmount = 0f;
        }

        /// <summary>Five minutes on every difficulty: time to lure the Nun away and come back.</summary>
        internal static float BleedSeconds() { return 300f; }

        /// <summary>Bleed-out time left as m:ss.</summary>
        internal static string Clock(float seconds)
        {
            int total = Mathf.CeilToInt(Mathf.Max(0f, seconds));
            return (total / 60) + ":" + (total % 60).ToString("00");
        }

        // ---------- Harmony entry points ----------

        /// <summary>LivesSystem.Die prefix. False skips the single-player death.</summary>
        internal bool OnDie(LivesSystem lives)
        {
            if (allowDeath || !InCoopGame) return true;
            if ((bool)AccessTools.Field(typeof(LivesSystem), "isPukedOn").GetValue(lives))
            {
                // A scripted fatal ending (the acid vent, the barbed wire). Played together, it is everyone's end, as in
                // the game alone; caught in one alone (it should not happen), only this player's run ends.
                if (session.TeamEndingRunning || !session.OthersStillPlaying()) return true;
                EnterSpectate("You died - spectating your team");
                return false;
            }
            if (State == LifeState.Alive) EnterDowned();
            return false;
        }

        /// <summary>fpsHealth damage prefix: downed, dying, spectating and just-revived players are not hurt.</summary>
        internal bool AcceptDamage()
        {
            if (TestInvulnerable) return false;
            if (!InCoopGame) return true;
            return State == LifeState.Alive && Time.time >= invulnerableUntil;
        }

        /// <summary>fpsHealth.GameOver prefix: with no lives left, spectate until the whole team is out.</summary>
        internal bool OnGameOver()
        {
            if (allowGameOver || !InCoopGame) return true;
            EnterSpectate();
            return false;
        }

        internal void OnRegainControl()
        {
            if (State != LifeState.Dying) return;
            State = LifeState.Alive;
            invulnerableUntil = Time.time + InvulnerableSeconds;
            if (Local && Local.firstPersonController) Local.firstPersonController.useHeadBob = headBob;
        }

        /// <summary>Falling out of the level would be an instant single-player game over; in co-op it costs a life.</summary>
        internal bool OnFalling(FirstPersonController controller)
        {
            if (!InCoopGame || State != LifeState.Alive || FallTime == null || FallTimeDeath == null) return true;
            float limit = (float)FallTimeDeath.GetValue(controller);
            float time = (float)FallTime.GetValue(controller);
            if (limit <= 0f || time + Time.deltaTime <= limit) return true;
            FallTime.SetValue(controller, 0f);
            if (safeKnown && Local)
            {
                // Out of the world (through a floor, off the map): back where you last stood, no life lost.
                MoveTo(safeSpot + Vector3.up * 0.1f);
                plugin.Log("Co-op: fell out of the world; put back where you last stood.");
                session.Tell("You fell out of the world and were put back");
                return false;
            }
            if (!LivesSystem.instance) return true; // No lives here and nowhere known to go back to: the game's own fall.
            plugin.Log("Co-op: fall death converted to a lost life.");
            Die("fall");
            return false;
        }

        // ---------- State changes ----------

        private void EnterDowned()
        {
            var r = Local;
            State = LifeState.Downed;
            Bleed = BleedSeconds();
            giveUp = 0f;
            nextGroan = Time.time + 1f;
            r.health.HealthMeter = 1f;
            headBob = r.firstPersonController.useHeadBob;
            r.playerStates.ChangeState(PlayerStates.States.MouseLook);
            Lock(true);
            r.camera.transform.DOKill(); r.camera.DOKill();
            r.camera.transform.DOMove(DownedEye(r), 0.6f).SetEase(Ease.OutBounce); // Down to the floor, standing or crouched.
            r.camera.transform.DOLocalRotate(Vector3.zero, 0.4f); // Out of any close-up.
            r.camera.DOFieldOfView(normalFov, 0.4f);
            BleedBehavior.BloodAmount = 1f;
            BleedBehavior.minBloodAmount = 0.55f;
            Groan();
            plugin.Log("Co-op: knocked down. Bleeding out in " + Bleed + "s.");
        }

        internal bool Revived(float health, string by)
        {
            if (State != LifeState.Downed || !Local) return false;
            var r = Local;
            State = LifeState.Alive;
            invulnerableUntil = Time.time + InvulnerableSeconds;
            r.camera.transform.DOKill(); r.camera.DOKill();
            r.playerStates.ChangeState(PlayerStates.States.Normal);
            // Knocked down mid close-up: the camera back on the head, at normal zoom.
            r.camera.transform.localPosition = Vector3.zero; r.camera.transform.localRotation = Quaternion.identity;
            r.camera.fieldOfView = normalFov;
            if (r.head) { r.head.DOKill(); r.head.localRotation = Quaternion.identity; }
            r.firstPersonController.mouseLook.Init(((Component)r).transform, r.head ? r.head : r.camera.transform); // Its remembered pitch too.
            Lock(false);
            r.firstPersonController.useHeadBob = headBob;
            r.health.SetHealth(Mathf.Clamp(health, 1f, 100f));
            BleedBehavior.minBloodAmount = 0f;
            BleedBehavior.BloodAmount = 0.6f;
            session.Tell("Revived by " + by);
            plugin.Log("Co-op: revived by " + by + " with " + health + " health.");
            return true;
        }

        /// <summary>The host confirmed our revive with this first aid kit: now it is used up.</summary>
        internal void KitUsed(string id)
        {
            var r = Local;
            var item = r && r.inventory && r.inventory.EquippedItem ? r.inventory.EquippedItem : null;
            if (!item || session.IdOf(item) != id) return;
            r.inventory.ClearEquippedItem(); // Reported to the host as a consumed item.
            item.gameObject.SetActive(false);
            plugin.Log("Co-op: first aid kit used.");
        }

        /// <summary>Walls and doors between two points (reviving needs a clear reach).</summary>
        internal static bool Blocked(Vector3 from, Vector3 to)
        {
            int mask = LayerMask.GetMask("Wall", "Door", "Ceiling", "BlockSight");
            return mask != 0 && Physics.Linecast(from, to, mask, QueryTriggerInteraction.Ignore);
        }
        private static string BlockedBy(Vector3 from, Vector3 to)
        {
            RaycastHit hit;
            return Physics.Linecast(from, to, out hit, LayerMask.GetMask("Wall", "Door", "Ceiling", "BlockSight"), QueryTriggerInteraction.Ignore) && hit.collider
                ? hit.collider.name + " (" + LayerMask.LayerToName(hit.collider.gameObject.layer) + ")" : "?";
        }

        /// <summary>Bleed out now (timer expired, gave up, or the whole team is down).</summary>
        internal void Die(string why = "")
        {
            if (!Local || State == LifeState.Dying || State == LifeState.Spectating || State == LifeState.GameOver) return;
            plugin.Log("Co-op: bleeding out now" + (why.Length > 0 ? " (" + why + ")" : "") + ".");
            var r = Local;
            State = LifeState.Dying;
            Bleed = 0f;
            Lock(false);
            BleedBehavior.minBloodAmount = 0f;
            r.camera.transform.DOKill();
            r.playerStates.ChangeState(PlayerStates.States.Normal);
            var lives = LivesSystem.instance;
            if (!lives) { EnterSpectate(); return; }
            allowDeath = true;
            try { lives.Die(); } // Loses a life: the game's own fade-out and respawn, or GameOver -> spectate.
            finally { allowDeath = false; }
            plugin.Log("Co-op: bled out. Lives left: " + lives.currentLives);
        }

        private void EnterSpectate() { EnterSpectate("Out of lives - spectating"); }
        private void EnterSpectate(string notice)
        {
            var r = Local;
            State = LifeState.Spectating;
            Bleed = 0f;
            BleedBehavior.minBloodAmount = 0f; BleedBehavior.BloodAmount = 0f;
            if (r)
            {
                r.camera.transform.DOKill();
                r.playerStates.ChangeState(PlayerStates.States.Cutscene);
                r.firstPersonController.enabled = false;
                if (r.characterController) r.characterController.enabled = false;
                var self = ((Component)r).transform;
                // Your own body, seen from the spectator camera.
                session.Avatars.LeaveBody(session.LocalId, plugin.Character.Value, self.position, self.rotation);
                blueLight = BlueLight(r);
                if (blueLight) { blueLightHome = blueLight.localPosition; blueLightOffset = blueLight.position - r.camera.transform.position; }
                // Park the body far below the level so the Nun, triggers and teammates ignore it.
                ((Component)r).transform.position += Vector3.down * 2500f;
            }
            spectatorPlaced = false; freeCam = false;
            session.Tell(notice);
            plugin.Log("Co-op: " + notice + ".");
        }

        private void RestoreBody()
        {
            var r = Local;
            if (!r) return;
            ((Component)r).transform.position += Vector3.up * 2500f;
            if (blueLight) blueLight.localPosition = blueLightHome;
            blueLight = null;
            if (r.characterController) r.characterController.enabled = true;
            r.firstPersonController.enabled = true;
        }

        /// <summary>Everyone is out of lives: the game's own death scene and credits for every player.</summary>
        internal void TeamGameOver()
        {
            TeamOver = true;
            if (State == LifeState.GameOver || !Local || !Local.health) return;
            if (State == LifeState.Spectating) RestoreBody();
            State = LifeState.GameOver;
            allowGameOver = true;
            try { Local.health.GameOver(); }
            finally { allowGameOver = false; }
            plugin.Log("Co-op: team game over.");
        }

        // Where the player last stood on solid ground, for a fall out of the world where there are no lives.
        private Vector3 safeSpot;
        private bool safeKnown;
        private float nextFooting;
        private void RememberFooting()
        {
            if (Time.unscaledTime < nextFooting) return;
            nextFooting = Time.unscaledTime + 0.25f;
            var characterController = Local.characterController;
            if (characterController && characterController.enabled && characterController.isGrounded
                && Local.playerStates.StateCheck() == PlayerStates.States.Normal)
            { safeSpot = ((Component)Local).transform.position; safeKnown = true; }
        }

        internal static bool TestKeepApartLog;
        private float nextApartLog;
        private Vector3 apartLast;
        private bool apartKnown;
        /// <summary>Teammates take up room: step clear of anyone standing in you, by moving (never through walls).</summary>
        private void KeepApart(float dt)
        {
            var r = Local;
            var characterController = r.characterController;
            if (!characterController || !characterController.enabled || r.playerStates.StateCheck() != PlayerStates.States.Normal) return;
            Vector3 feet = ((Component)r).transform.position;
            Vector3 moving = dt > 0f && apartKnown ? (feet - apartLast) / dt : Vector3.zero; moving.y = 0f; // How we moved since last frame.
            apartLast = feet; apartKnown = true;
            Vector3 push = session.Avatars.Separation(feet, session.LocalId);
            float amount = push.magnitude;
            if (amount < 0.002f) return;
            // Whoever walks into the other stops; someone standing still is not shoved. Only two players deep inside each
            // other (same spawn, a teleport) drift apart, slowly.
            bool approaching = Vector3.Dot(moving, push / amount) < -0.05f;
            float step = approaching ? Mathf.Min(amount, 8f * dt) : amount > 0.25f ? Mathf.Min(amount, 1.5f * dt) : 0f;
            if (step > 0f) { characterController.Move(push / amount * step); apartLast = ((Component)r).transform.position; }
            if (TestKeepApartLog && Time.unscaledTime >= nextApartLog)
            {
                nextApartLog = Time.unscaledTime + 0.25f;
                plugin.Log("TEST apart push=" + amount.ToString("0.00") + " vel=" + moving.ToString("0.00") + " approaching="
                    + approaching + " step=" + step.ToString("0.000"));
            }
        }

        /// <summary>Puts the local player somewhere at once (a rescue, or the host's console).</summary>
        internal void MoveTo(Vector3 position)
        {
            var r = Local;
            if (!r) return;
            var characterController = r.characterController;
            bool was = characterController && characterController.enabled;
            if (characterController) characterController.enabled = false;
            ((Component)r).transform.position = position;
            if (characterController) characterController.enabled = was;
            if (FallTime != null && r.firstPersonController) FallTime.SetValue(r.firstPersonController, 0f);
            apartKnown = false; // A jump, not a walk.
        }

        /// <summary>The host's console moved this player: "safe" (their last solid footing), "spawn" (the level's respawn
        /// point) or "pos" (the given spot).</summary>
        internal void Teleported(string mode, Vector3 position)
        {
            if (!Local) return;
            Vector3? to = mode == "pos" ? position
                : mode == "safe" && safeKnown ? safeSpot + Vector3.up * 0.1f
                : LivesSystem.instance && LivesSystem.instance.respawnLocation ? LivesSystem.instance.respawnLocation.position + Vector3.up * 0.1f
                : safeKnown ? safeSpot + Vector3.up * 0.1f : (Vector3?)null;
            if (!to.HasValue) return;
            // Unstuck: frozen in a puzzle's close-up or animation too, they are let go of it.
            var state = Local.playerStates ? Local.playerStates.StateCheck() : PlayerStates.States.Normal;
            if (mode == "safe" && (session.FrozenByPuzzle || state == PlayerStates.States.ItemCutscene || state == PlayerStates.States.MousePuzzle))
                OutOfCloseUp();
            MoveTo(to.Value);
            plugin.Log("Co-op: moved by the console (" + mode + ") to " + to.Value.ToString("0.0"));
        }

        // ---------- Dragged out from under a bed ----------
        internal const float DragSeconds = 1.0f, StandSeconds = 0.6f;
        internal bool DraggedOut { get; private set; }

        /// <summary>
        /// The Nun has lifted the bed: you are pulled out from under it towards her, flat on the floor and facing her, then
        /// stood up in front of her (a moment before you can move).
        /// </summary>
        internal void DragOutOfBed(Vector3 nun, float delay)
        {
            var r = Local;
            if (!r || r.playerStates.StateCheck() != PlayerStates.States.Hiding || DraggedOut) return;
            DraggedOut = true;
            MEC.Timing.RunCoroutine(DragOut(r, nun, delay));
        }

        /// <summary>
        /// The Nun's chase effects on this player's screen, as her jump scare gives the game's one player: 1 she spotted
        /// them (the VHS damage effect and motion blur), 0 she lost them, 2 her jump scare's controller rumble.
        /// </summary>
        internal void HuntedEffects(int mode)
        {
            var r = Local;
            if (!r) return;
            if (mode == 2) { if (VibrationController.instance) VibrationController.instance.SetVibration_Jumpscare(); return; }
            Transform root = ((Component)r).transform;
            Transform damage = PlayerPart(root, "Vhs Damage Cam Fsm"), blur = PlayerPart(root, "Motion Blur");
            var fsm = damage ? damage.GetComponent<NodeCanvas.Framework.GraphOwner>() : null;
            if (fsm) fsm.SendEvent(mode == 1 ? "Chase" : "Normal");
            if (blur) blur.gameObject.SetActive(mode == 1);
            plugin.Log("Co-op: the Nun's chase effects " + (mode == 1 ? "on" : "off") + (fsm ? "" : " (no damage camera)") + (blur ? "" : " (no motion blur)"));
        }

        private static Transform PlayerPart(Transform root, string name)
        {
            var part = root.GetComponentsInChildren<Transform>(true).FirstOrDefault(t => t.name == name);
            if (part) return part;
            return Resources.FindObjectsOfTypeAll<Transform>().FirstOrDefault(t => t && t.name == name && t.gameObject.scene.IsValid());
        }

        /// <summary>Out of a puzzle's close-up or animation that its machine never finished for this player: walking and
        /// looking again, the camera back on the head at its usual zoom, the interaction prompt back.</summary>
        internal void OutOfCloseUp()
        {
            var r = Local;
            if (!r || State != LifeState.Alive) return;
            r.camera.transform.DOKill(); r.camera.DOKill();
            if (MessagesGranny.PlayerStateChangeEvent != null) MessagesGranny.PlayerStateChangeEvent(PlayerStates.States.Normal);
            r.playerStates.ChangeState(PlayerStates.States.Normal);
            r.camera.transform.localPosition = Vector3.zero; r.camera.transform.localRotation = Quaternion.identity;
            r.camera.fieldOfView = normalFov;
            if (r.head) { r.head.DOKill(); r.head.localRotation = Quaternion.identity; }
            r.firstPersonController.mouseLook.Init(((Component)r).transform, r.head ? r.head : r.camera.transform);
            if (MessagesGranny.InteractionUiToggleEvent != null) MessagesGranny.InteractionUiToggleEvent(true);
        }

        /// <summary>The Nun flipped the bed this player is under: the game's own state change for it (what the host's
        /// game does to the host).</summary>
        internal void TurnedOutOfBed(PlayerStates.States state)
        {
            var r = Local;
            if (!r || r.playerStates.StateCheck() != PlayerStates.States.Hiding || DraggedOut) return;
            plugin.Log("Co-op: the Nun flipped the bed; out from under it (" + state + ")");
            if (MessagesGranny.PlayerStateChangeEvent != null) MessagesGranny.PlayerStateChangeEvent(state);
            r.playerStates.ChangeState(state);
        }

        private IEnumerator<float> DragOut(pReferences r, Vector3 nun, float delay)
        {
            if (delay > 0f) yield return MEC.Timing.WaitForSeconds(delay);
            Transform body = ((Component)r).transform;
            body.DOKill(); r.head.DOKill();
            Vector3 from = body.position, toward = nun - from;
            toward.y = 0f;
            Vector3 way = toward.sqrMagnitude > 0.01f ? toward.normalized : body.forward;
            // Hauled out to the spot the game stands them up on when they crawl out themselves (clear of the bed, theirs
            // alone with two under it); without one, towards her feet.
            var exit = HideExitPoint != null ? HideExitPoint.GetValue(r.playerStates) as Transform : null;
            Vector3 to = exit ? exit.position : from + way * Mathf.Max(0f, toward.magnitude - 0.9f);
            Vector3 clear = to - nun; clear.y = 0f; // Never into her: at least a step from her.
            if (clear.magnitude < 0.75f)
                to = new Vector3(nun.x, to.y, nun.z) + (clear.sqrMagnitude > 0.0025f ? clear.normalized : Vector3.Cross(Vector3.up, way)) * 0.75f;
            body.rotation = Quaternion.LookRotation(way); // Looking up at her.
            float t = 0f;
            plugin.Log("Co-op: dragged out from under the bed (" + Vector3.Distance(from, to).ToString("0.00") + "m, ending "
                + Vector3.Distance(new Vector3(to.x, nun.y, to.z), nun).ToString("0.00") + "m from her)");
            while (t < DragSeconds && r)
            {
                t += Time.deltaTime;
                float k = Mathf.Clamp01(t / DragSeconds);
                body.position = Vector3.Lerp(from, to, k * k * (3f - 2f * k)); // Jerked out, slowing at her feet.
                yield return MEC.Timing.WaitForOneFrame;
            }
            if (!r) { DraggedOut = false; yield break; }
            // Got up in front of her.
            r.playerStates.ChangeState(PlayerStates.States.StandFromHiding);
            r.firstPersonController.canMove = false;
            yield return MEC.Timing.WaitForSeconds(StandSeconds);
            if (r) r.firstPersonController.canMove = true;
            DraggedOut = false;
        }

        // ---------- Downed: crawling along the floor ----------
        internal const float CrawlSpeed = 0.55f;
        private float crawlFall;
        internal static bool TestCrawl; // Automated tests: crawl forward.

        /// <summary>Where a downed player's eyes are: just above the floor under them.</summary>
        private Vector3 DownedEye(pReferences r)
        {
            Transform body = ((Component)r).transform;
            Vector3 head = r.head ? r.head.position : body.position + Vector3.up * 1.6f;
            int mask = Physics.DefaultRaycastLayers;
            foreach (string layer in new[] { "Player", "CameraTrigger", "DontCollideEnvironment" })
            {
                int index = LayerMask.NameToLayer(layer);
                if (index >= 0) mask &= ~(1 << index);
            }
            RaycastHit floor;
            float floorY = Physics.Raycast(body.position + Vector3.up * 0.6f, Vector3.down, out floor, 3f, mask, QueryTriggerInteraction.Ignore)
                ? floor.point.y : body.position.y;
            Vector3 ahead = Vector3.ProjectOnPlane(body.forward, Vector3.up).normalized * 0.1f;
            return new Vector3(head.x, floorY + 0.62f, head.z) + ahead; // On hands and knees.
        }

        /// <summary>Downed: drag yourself along the floor, slowly, towards where you look. Still while a teammate
        /// revives you. The eyes stay just above the floor (stairs, slopes).</summary>
        private void Crawl(float dt)
        {
            var r = Local;
            var controller = r ? r.characterController : null;
            if (!controller || !controller.enabled || r.playerStates.StateCheck() != PlayerStates.States.MouseLook) return;
            Vector2 stick = TestCrawl ? Vector2.up : new Vector2(Axis("Move Horizontal"), Axis("Move Vertical"));
            if (session.ReviveProgressFor(session.LocalId) > 0f) stick = Vector2.zero; // Hold still for the reviver.
            if (stick.sqrMagnitude > 1f) stick.Normalize();
            Transform view = r.camera.transform;
            Vector3 forward = Vector3.ProjectOnPlane(view.forward, Vector3.up), right = Vector3.ProjectOnPlane(view.right, Vector3.up);
            if (forward.sqrMagnitude < 0.01f) forward = Vector3.ProjectOnPlane(((Component)r).transform.forward, Vector3.up);
            Vector3 move = (forward.normalized * stick.y + right.normalized * stick.x) * CrawlSpeed;
            crawlFall = controller.isGrounded ? -1f : Mathf.Max(crawlFall - 9.8f * dt, -10f);
            controller.Move((move + Vector3.up * crawlFall) * dt);
            if (!DOTween.IsTweening(view)) view.position = Vector3.Lerp(view.position, DownedEye(r), 1f - Mathf.Exp(-12f * dt));
        }

        private float nextBlockedLog;
        // The player's buttons. Automated tests freeze them: hidden test games would otherwise also hear this PC's keys.
        private bool Pressed(string action) { return input != null && !RuntimeTestDriver.FreezePlayerInput && input.GetButtonDown(action); }
        private bool Held(string action) { return input != null && !RuntimeTestDriver.FreezePlayerInput && input.GetButton(action); }
        private float Axis(string action) { return input == null || RuntimeTestDriver.FreezePlayerInput ? 0f : input.GetAxis(action); }

        private void Lock(bool locked)
        {
            var r = Local;
            if (!r) return;
            r.firstPersonController.canMove = !locked;
            r.firstPersonController.canJump = !locked;
            r.firstPersonController.canCrouch = !locked;
            r.firstPersonController.CanLookBehind = !locked;
            r.firstPersonController.useHeadBob = !locked && headBob;
            r.inventory.inventoryInteraction = !locked;
            r.interactionSystem.Fsm.ChangeState(locked ? InteractionSystem.States.CanNotInteract : InteractionSystem.States.CanInteract);
        }

        private void Groan()
        {
            var r = Local;
            if (!r) return;
            Transform hurt = ((Component)r).transform.Find("Audio/Hurt Audio");
            var audio = hurt ? hurt.GetComponent<AudioSourceRandom>() : null;
            if (audio) audio.PlayRandom();
        }

        // ---------- Per frame ----------

        internal void Update()
        {
            ReviveCandidate = 0;
            if (!InCoopGame) { if (Reviving != 0) StopReviving(); return; }
            // Fetched every frame: the game rebuilds Rewired with each level, and a kept Player from the previous
            // level fails every read (an error per frame, and no button ever counts as pressed).
            input = ReInput.isReady ? ReInput.players.GetPlayer(0) : null; // Null for a moment while it is rebuilt: no buttons then.
            float dt = Time.deltaTime;
            switch (State)
            {
                case LifeState.Downed:
                    Lock(true);
                    Crawl(dt);
                    // Paused while a teammate works, or once the run is ending.
                    if (session.ReviveProgressFor(session.LocalId) <= 0f && !TeamOver && !session.TeamEndingRunning) Bleed -= dt;
                    giveUp = Held("Jump") ? giveUp + dt : 0f;
                    if (Bleed <= 0f || giveUp >= 2f) Die(Bleed <= 0f ? "timer" : "gave up");
                    break;
                case LifeState.Spectating:
                    if (Pressed("Jump")) ToggleFreeCamera();
                    else if (Pressed("Action")) NextView();
                    if (!freeCam && NextWatchTarget(0) == 0) ToggleFreeCamera(); // Nobody left to follow.
                    if (freeCam) Fly(Time.unscaledDeltaTime);
                    break;
                case LifeState.Alive:
                    RememberFooting();
                    KeepApart(dt);
                    if (safeKnown && ((Component)Local).transform.position.y < safeSpot.y - 40f)
                    {
                        MoveTo(safeSpot + Vector3.up * 0.1f);
                        session.Tell("You fell out of the world and were put back");
                        plugin.Log("Co-op: below the world; put back where you last stood.");
                    }
                    UpdateReviving(dt);
                    var camera = Local.camera;
                    if (camera && Local.playerStates.StateCheck() == PlayerStates.States.Normal && !DOTween.IsTweening(camera)) normalFov = camera.fieldOfView;
                    break;
            }
            if (session.IsHost && Time.unscaledTime >= nextHostCheck) { nextHostCheck = Time.unscaledTime + 0.5f; HostCheck(); }
        }

        private void UpdateReviving(float dt)
        {
            var r = Local;
            Transform me = ((Component)r).transform;
            bool free = r.playerStates.StateCheck() == PlayerStates.States.Normal && !r.inventory.isOpen();
            int best = 0; float bestDistance = ReviveRange;
            if (free)
            {
                foreach (var frame in session.Players.Values)
                {
                    if (frame.id == session.LocalId || !frame.downed || frame.bleed <= 0f || frame.scene != session.SceneName) continue;
                    Vector3? center = session.Avatars.GetCenter(frame.id);
                    if (!center.HasValue) continue;
                    Vector3 flat = center.Value - me.position; flat.y = 0f;
                    float distance = flat.magnitude;
                    if (distance > bestDistance || Mathf.Abs(center.Value.y - me.position.y) > 1.6f) continue;
                    if (Blocked(r.camera.transform.position, center.Value + Vector3.up * 0.45f))
                    {
                        // Not through a wall or a closed door (aimed above the body: floors double as the ceiling below).
                        if (Time.unscaledTime >= nextBlockedLog)
                        {
                            nextBlockedLog = Time.unscaledTime + 5f;
                            plugin.Log("Co-op: revive of " + frame.name + " blocked by "
                                + BlockedBy(r.camera.transform.position, center.Value + Vector3.up * 0.45f) + ".");
                        }
                        continue;
                    }
                    if (distance > 1.0f && Vector3.Dot(r.camera.transform.forward, (center.Value - r.camera.transform.position).normalized) < 0.25f) continue;
                    best = frame.id; bestDistance = distance;
                }
            }
            ReviveCandidate = best;
            bool holding = best != 0 && (Held("Action") || TestHoldAction);
            if (!holding || (Reviving != 0 && Reviving != best)) { if (Reviving != 0) StopReviving(); if (!holding) return; }
            if (Reviving == 0)
            {
                Reviving = best; ReviveProgress = 0f;
                PlayerFrame target;
                reviveName = session.Players.TryGetValue(best, out target) ? target.name : "teammate";
                r.firstPersonController.canMove = false;
            }
            bool kit = r.inventory.EquippedItem is FirstAidInteractable;
            ReviveProgress += dt / (kit ? KitReviveSeconds : ReviveSeconds);
            if (ReviveProgress < 1f) return;
            // The kit is used up only once the host confirms the revive took (see KitUsed).
            session.Request(new Packet { action = "revive",
                target = Reviving.ToString(System.Globalization.CultureInfo.InvariantCulture), text = kit ? "kit" : "" });
            plugin.Log("Co-op: revived " + reviveName + (kit ? " with a first aid kit." : "."));
            StopReviving();
        }

        private void StopReviving()
        {
            Reviving = 0; ReviveProgress = 0f;
            if (Local && State == LifeState.Alive && Local.playerStates.StateCheck() == PlayerStates.States.Normal) Local.firstPersonController.canMove = true;
        }

        /// <summary>Host: start a team wipe when nobody is left standing; game over when everyone is out.</summary>
        private void HostCheck()
        {
            if (TeamOver || session.TeamEndingRunning) return; // An ending is already bringing everyone back.
            var inGame = session.Players.Values.Where(p => p.body && p.scene == session.SceneName).ToList();
            if (inGame.Count == 0 || !Local) return;
            bool standing = inGame.Any(p => !p.downed && !p.spectating && p.health > 0f);
            if (standing) { wipeIssued = false; return; }
            var bleeding = inGame.Where(p => p.downed && p.bleed > 0f).ToList();
            if (bleeding.Count > 0 && !wipeIssued)
            {
                wipeIssued = true;
                session.Broadcast(new Packet { type = "notice", epoch = session.Epoch, text = "Everyone is down" });
                session.Tell("Everyone is down");
                foreach (var frame in bleeding) session.BleedOut(frame.id);
                plugin.Log("Co-op: team wipe, " + bleeding.Count + " bleeding out.");
            }
            if (!TeamOver && inGame.All(p => p.spectating))
            {
                TeamOver = true;
                session.Broadcast(new Packet { type = "gameover", epoch = session.Epoch });
                TeamGameOver();
                session.ReturnToLobbyIn(20f); // The full credits take minutes; regroup in the lobby.
            }
        }

        internal void FillFrame(PlayerFrame frame)
        {
            frame.downed = Down;
            frame.bleed = State == LifeState.Downed ? Mathf.Max(0.01f, Bleed) : 0f;
            frame.spectating = State == LifeState.Spectating || State == LifeState.GameOver;
            frame.reviving = Reviving;
            frame.revive = ReviveProgress;
            if (Down) frame.health = Mathf.Max(frame.health, 1f);
        }

        // ---------- Spectator camera ----------

        /// <summary>Spectating, Interact: behind a teammate, then through their eyes, then the next teammate, and
        /// after the last one the free camera. Even with a single teammate every press changes the view.</summary>
        private void NextView()
        {
            if (freeCam) { freeCam = false; watchEyes = false; watching = NextWatchTarget(0); return; }
            if (!watchEyes && watching != 0) { watchEyes = true; return; }
            watchEyes = false;
            var order = WatchOrder();
            int index = order.IndexOf(watching);
            if (order.Count == 0 || index == order.Count - 1) { ToggleFreeCamera(); return; } // Past the last teammate.
            watching = order[index + 1];
        }
        private List<int> WatchOrder()
        {
            return session.Players.Values.Where(p => p.id != session.LocalId && p.body && !p.spectating && p.scene == session.SceneName).OrderBy(p => p.id).Select(p => p.id).ToList();
        }
        internal void TestNextView() { if (State == LifeState.Spectating) NextView(); }
        /// <summary>Spectating through the watched teammate's eyes rather than from behind them.</summary>
        internal bool WatchingEyes { get { return watchEyes && !freeCam; } }
        private bool watchEyes;

        private int NextWatchTarget(int current)
        {
            var candidates = session.Players.Values.Where(p => p.id != session.LocalId && p.body && !p.spectating && p.scene == session.SceneName).OrderBy(p => p.id).Select(p => p.id).ToList();
            if (candidates.Count == 0) return 0;
            int index = candidates.IndexOf(current);
            return candidates[(index + 1) % candidates.Count];
        }

        internal void LateUpdate() { if (State == LifeState.Spectating && Local && Local.camera) PlaceSpectatorCamera(Local.camera); }

        internal void TestToggleFreeCamera() { if (State == LifeState.Spectating) ToggleFreeCamera(); }

        private void ToggleFreeCamera()
        {
            freeCam = !freeCam;
            if (!freeCam || !Local || !Local.camera) return;
            // Take off from wherever the spectator camera is now.
            Transform view = Local.camera.transform;
            freePosition = view.position;
            Vector3 angles = view.rotation.eulerAngles;
            freeYaw = angles.y;
            freePitch = Mathf.Clamp(Mathf.DeltaAngle(0f, angles.x), -85f, 85f);
        }

        private void Fly(float dt)
        {
            var mouse = Local && Local.firstPersonController ? Local.firstPersonController.mouseLook : null;
            float sensitivityX = mouse != null ? mouse.XSensitivity : 2f, sensitivityY = mouse != null ? mouse.YSensitivity : 2f;
            // The same look scaling as the game's own MouseLook.
            freeYaw += Axis("Look Horizontal") * sensitivityX * 30f * dt;
            freePitch = Mathf.Clamp(freePitch - Axis("Look Vertical") * sensitivityY * 30f * dt, -85f, 85f);
            Quaternion rotation = Quaternion.Euler(freePitch, freeYaw, 0f);
            Vector3 move = rotation * new Vector3(Axis("Move Horizontal"), 0f, Axis("Move Vertical") + (TestFreeCam ? 1f : 0f));
            if (Held("Crouch")) move += Vector3.down;
            float speed = Held("Run") ? 12f : 4.5f;
            freePosition += Vector3.ClampMagnitude(move, 1f) * speed * dt;
        }

        private void PlaceSpectatorCamera(Camera camera)
        {
            if (State != LifeState.Spectating || !Local || camera != Local.camera) return;
            if (freeCam)
            {
                Quaternion view = Quaternion.Euler(freePitch, freeYaw, 0f);
                camera.transform.SetPositionAndRotation(freePosition, view);
                spectatorPosition = freePosition; spectatorRotation = view; spectatorPlaced = true; // Glide back when following again.
                FollowLight(camera);
                return;
            }
            PlayerFrame frame;
            if (watching == 0 || !session.Players.TryGetValue(watching, out frame) || frame.spectating || !frame.body)
            {
                watching = NextWatchTarget(0);
                if (watching == 0) return;
                frame = session.Players[watching];
            }
            Transform body = session.Avatars.GetTransform(watching);
            if (!body) return;
            Vector3 head = body.position + Vector3.up * (frame.downed ? 0.65f : frame.vent || frame.crouching ? 1.1f : 1.65f);
            Vector3 look = frame.look.sqrMagnitude > 0.01f ? frame.look.normalized : body.forward;
            if (watchEyes)
            {
                // Their own view: from their eyes, where they look (just ahead of the face, so the head stays out of shot).
                Vector3 eye = (session.Avatars.GetHead(watching) ?? head) + look * 0.18f;
                float follow = spectatorPlaced ? 1f - Mathf.Exp(-20f * Time.unscaledDeltaTime) : 1f;
                spectatorPosition = Vector3.Lerp(spectatorPosition, eye, follow);
                spectatorRotation = Quaternion.Slerp(spectatorRotation, Quaternion.LookRotation(look), follow);
                spectatorPlaced = true;
                camera.transform.SetPositionAndRotation(spectatorPosition, spectatorRotation);
                FollowLight(camera);
                return;
            }
            Vector3 flat = new Vector3(look.x, 0f, look.z);
            if (flat.sqrMagnitude < 0.01f) flat = body.forward;
            flat.Normalize();
            Vector3 desired = head - flat * 2.4f + Vector3.up * 0.5f;
            RaycastHit hit;
            int mask = Physics.DefaultRaycastLayers & ~(1 << LayerMask.NameToLayer("Player")) & ~(1 << LayerMask.NameToLayer("CameraTrigger"));
            Vector3 toCamera = desired - head;
            if (Physics.SphereCast(head, 0.2f, toCamera.normalized, out hit, toCamera.magnitude, mask, QueryTriggerInteraction.Ignore))
                desired = head + toCamera.normalized * Mathf.Max(0.3f, hit.distance - 0.1f);
            Quaternion rotation = Quaternion.LookRotation((head + flat * 2f) - desired);
            float blend = spectatorPlaced ? 1f - Mathf.Exp(-8f * Time.unscaledDeltaTime) : 1f;
            spectatorPosition = Vector3.Lerp(spectatorPosition, desired, blend);
            spectatorRotation = Quaternion.Slerp(spectatorRotation, rotation, blend);
            spectatorPlaced = true;
            camera.transform.SetPositionAndRotation(spectatorPosition, spectatorRotation);
            FollowLight(camera);
        }

        private void FollowLight(Camera camera) { if (blueLight) blueLight.position = camera.transform.position + blueLightOffset; }

        private static Transform BlueLight(pReferences r)
        {
            foreach (var light in ((Component)r).GetComponentsInChildren<Light>(true))
                if (light.name.StartsWith("Blue light", StringComparison.OrdinalIgnoreCase) && !light.transform.IsChildOf(r.camera.transform))
                    return light.transform;
            return null;
        }

        internal string WatchingName
        {
            get { PlayerFrame frame; return watching != 0 && session.Players.TryGetValue(watching, out frame) ? frame.name : ""; }
        }

        internal void Dispose() { Camera.onPreCull -= PlaceSpectatorCamera; }
    }
}
