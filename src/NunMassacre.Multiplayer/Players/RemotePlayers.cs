using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace NunMassacre.Multiplayer.Players
{
    /// <summary>
    /// Teammates as the supplied skinned characters (each player's choice), animated procedurally
    /// from their network frames. These are explicit proxies: no original player scripts run on them.
    /// </summary>
    public sealed class RemotePlayers : IDisposable
    {
        // ---------- Character catalogue (assets embedded from Players/Assets) ----------
        internal static readonly string[] CharacterIds = { "male_casual", "secretary", "scientist" };
        internal static readonly string[] CharacterNames = { "MIKE", "MADDY", "MARK" };
        /// <summary>Who made each character's model (credited in the menus and the README).</summary>
        internal static readonly string[] CharacterArtists = { "VINRAX", "VINRAX", "VINRAX" };
        internal static string ValidCharacter(string id) { return CharacterIds.Contains(id) ? id : CharacterIds[0]; }

        [Serializable]
#pragma warning disable 0649 // Fields are populated by JsonCodec.
        private sealed class MeshData
        {
            public string name;
            public float[] vertices, normals, uv;
            public int[] triangles;
            public string[] boneNames;
            public int[] boneParents;
            public float[] bonePositions;
            public int[] boneIndex;
            public float[] boneWeight;
        }
#pragma warning restore 0649

        private sealed class CharacterModel
        {
            public Mesh Mesh;
            public Material Material;
            public Texture2D Texture;
            public string[] BoneNames;
            public int[] BoneParents;
            public Vector3[] Joints;
            public Vector3 RightPalm = Vector3.right; // Which way the right hand's palm faces, in the model's own axes.
            public Vector3 HandCenter = new Vector3(0f, -0.05f, 0f); // The middle of the right hand's mesh, from its joint.
        }

        private sealed class Avatar
        {
            public int Id;
            public string Character;
            public PlayerFrame Frame;
            public GameObject Root;
            public Transform Body, Model;
            public AvatarRig Rig;
            public Transform Hand;
            public CapsuleCollider Collider;
            public CapsuleCollider Solid;   // Blocks the Nun like a real body.
            public CapsuleCollider Blocker; // Blocks players: the game ignores Player-Player collisions.
            public bool Snap;               // Next pose shown at once instead of blended (stepping out of the tub).
            public Light Light;
            public Material Material;
            public bool First = true;
            public string Equipped;
            public GameObject Held;
            public Quaternion HeldTurn = Quaternion.identity; // How the game holds this item, relative to the view.
            public bool HeldWithHead;
            public Vector3 HeldGrip;        // Where the item is gripped, in its own units (low on it, as the game holds it).
            public bool HeldLong;           // Something long (a board): carried in both hands, across the body.
            public Vector3 HeldCentre, HeldLength, HeldFace; // Its middle, long axis and flat side, in its own units.
            public float TwoHands;          // How much it is carried in both hands (eases in and out).
            public Vector3 Velocity;
            public AudioSource Steps;       // Footsteps, heard from where they walk.
            public AudioClip[] Floor;       // The clips for the floor under them.
            public float StepCycle, NextFloorCheck;
            public Vector3 LastSpot;        // Where the body was last tick: the pace comes from how far it really moved.
            public float Headroom = 3f, HeadroomMeasured = 3f; // Metres from the floor to whatever is over them; a crouch ducks under it.
            public bool LowCrawl;           // Crouched somewhere too low for a crouch (a vent): crawling instead.
            public Vector3 CrawlWay;        // Crawling in a vent: the way the body lies (along the duct), zero otherwise.
            public bool HoldsGun;           // The item in hand is the revolver.
            public int Shots = -1;          // Their shot count as last seen (-1: not yet).
            public int Points = -1, Whistles = -1; // Their gesture counts as last seen.
            public AudioSource Call;        // Their whistle, from their mouth.
            public AudioSource Shot;        // Their revolver going off, from their hand.
            public AudioLowPassFilter ShotFilter;
            public AudioLowPassFilter CallFilter;
            public float NextCeilingCheck;
            public Vector3 DownSpot; public Quaternion DownTurn; public float DownSeen = -999f; // Where they last lay downed.
            public AudioSource Pain;        // Cries of pain, from the head.
            public ParticleSystem Blood;    // The game's blood burst, as the player's own body has.
            public float Pace;
        }

        private const float ModelHeight = 1.8f;
        // Teammates' footsteps: the game's own clips for the floor under them (wood, carpet, tile, or the scene's
        // default), at the same pace as the player's own. Crouching is silent, as it is for the player.
        private static readonly FieldInfo WoodSteps = AccessTools.Field(typeof(GrannyClone.FirstPersonController), "woodSteps");
        private static readonly FieldInfo CarpetSteps = AccessTools.Field(typeof(GrannyClone.FirstPersonController), "rubbleSteps");
        private static readonly FieldInfo TileSteps = AccessTools.Field(typeof(GrannyClone.FirstPersonController), "tileSteps");
        private static readonly FieldInfo CurrentSteps = AccessTools.Field(typeof(GrannyClone.FirstPersonController), "footstepSounds");
        private static readonly FieldInfo StepSource = AccessTools.Field(typeof(GrannyClone.FirstPersonController), "audioSource");
        private static readonly FieldInfo StepInterval = AccessTools.Field(typeof(GrannyClone.FirstPersonController), "stepInterval");
        private static readonly FieldInfo WalkSpeed = AccessTools.Field(typeof(GrannyClone.FirstPersonController), "walkSpeed");
        private static readonly FieldInfo RunStepLength = AccessTools.Field(typeof(GrannyClone.FirstPersonController), "runStepLength");
        private static readonly System.Random stepDice = new System.Random(); // Not UnityEngine.Random: that one is seeded for shared rolls.
        /// <summary>The scene's default footsteps, taken as the player spawns (the ground check swaps them later).</summary>
        internal static AudioClip[] DefaultSteps;
        internal static void PlayerStarting(GrannyClone.FirstPersonController player)
        {
            var clips = player && CurrentSteps != null ? CurrentSteps.GetValue(player) as AudioClip[] : null;
            DefaultSteps = clips != null && clips.Length > 0 ? (AudioClip[])clips.Clone() : null;
        }
        // Hurt: the game's own reactions on the teammate's body. Its pain cries are a woman's voice, so only the
        // female characters cry out; Mike and Mark bleed and flinch silently (their player has a microphone).
        private static readonly FieldInfo RandomClips = AccessTools.Field(typeof(AudioSourceRandomClip), "clips");
        private static bool FemaleVoice(string character) { return character == "secretary"; }
        // Players who are out for good (no lives left, or a fatal ending) leave their body where they fell, until
        // the level ends. Static copies of their character: no colliders, sounds or light.
        private readonly List<Avatar> bodies = new List<Avatar>();
        /// <summary>Teammates and bodies out of sight (an ending everyone watches together).</summary>
        internal static bool HideAll;
        private string bodiesScene;
        internal static int BodiesLeft;

        internal void LeaveBody(int id, string character, Vector3 position, Quaternion rotation)
        {
            if (disposed || !Finite(position) || !Finite(rotation)) return;
            string scene = SceneManager.GetActiveScene().name;
            if (bodiesScene != scene) { ClearBodies(); bodiesScene = scene; }
            var body = Create(-1000 - Math.Abs(id), character); // Negative: no footsteps, voice or nameplate.
            body.Root.name = "NMM_Body_" + id;
            foreach (var shape in body.Root.GetComponentsInChildren<Collider>(true)) UnityEngine.Object.Destroy(shape);
            var rigidbody = body.Root.GetComponent<Rigidbody>();
            if (rigidbody) UnityEngine.Object.Destroy(rigidbody);
            if (body.Light) body.Light.enabled = false;
            // Down on the floor below, wherever the last frame left them (mid-stride, a scripted fall).
            int player = LayerMask.NameToLayer("Player");
            int mask = Physics.DefaultRaycastLayers & ~(player >= 0 ? 1 << player : 0) & ~(BlockerLayer() >= 0 ? 1 << BlockerLayer() : 0);
            RaycastHit hit;
            if (Physics.Raycast(position + Vector3.up * 0.5f, Vector3.down, out hit, 4f, mask, QueryTriggerInteraction.Ignore)) position = hit.point;
            body.Root.transform.SetPositionAndRotation(position, Yaw(rotation));
            if (body.Rig != null) body.Rig.Snap(new AvatarInput { Pose = AvatarPose.Dead }, 0f);
            bodies.Add(body);
            BodiesLeft++;
            log("Body left for peer " + id + " at " + position.ToString("0.00"));
        }

        private void ClearBodies()
        {
            foreach (var body in bodies) Destroy(body);
            bodies.Clear();
        }

        /// <summary>Automated tests: hurt reactions and reaches shown for teammates.</summary>
        internal static int Hurts, Reaches;

        /// <summary>A teammate took damage: blood, a flinch, and a cry of pain from them.</summary>
        internal void Hurt(int id)
        {
            Avatar avatar;
            if (!players.TryGetValue(id, out avatar) || avatar.Root == null || !avatar.Root.activeInHierarchy) return;
            Hurts++;
            if (avatar.Rig != null) avatar.Rig.Flinch();
            var blood = BloodFor(avatar);
            if (blood) { blood.Clear(); blood.Play(); }
            Cry(avatar);
        }

        /// <summary>Reaching for something they use at this point.</summary>
        // Tests.
        internal string PoseOf(int id) { Avatar avatar; return players.TryGetValue(id, out avatar) && avatar.Rig != null ? avatar.Rig.PoseState : "-"; }
        // Tests.
        internal string PeekOf(int id) { Avatar avatar; return players.TryGetValue(id, out avatar) && avatar.Rig != null ? avatar.Rig.PeekState : "-"; }
        // Tests.
        internal string OneOffOf(int id) { Avatar avatar; return players.TryGetValue(id, out avatar) && avatar.Rig != null ? avatar.Rig.OneOffPlaying : null; }

        /// <summary>A teammate threw something: their arm throws it.</summary>
        internal void Throw(int id)
        {
            Avatar avatar;
            if (players.TryGetValue(id, out avatar) && avatar.Rig != null) avatar.Rig.Throw();
        }

        internal void Reach(int id, Vector3 at, bool opening = false)
        {
            Avatar avatar;
            if (!players.TryGetValue(id, out avatar) || avatar.Root == null || avatar.Rig == null || !Finite(at)) return;
            Vector3 shoulder = avatar.Root.transform.position + Vector3.up * (avatar.Frame != null && avatar.Frame.crouching ? 0.9f : 1.4f);
            Vector3 gap = at - shoulder;
            float across = new Vector2(gap.x, gap.z).magnitude;
            if (across > 4f) return; // Something far off (a remote switch): no reach.
            avatar.Rig.Reach(Mathf.Atan2(gap.y, Mathf.Max(0.2f, across)) * Mathf.Rad2Deg, opening);
            Reaches++;
        }

        private void Cry(Avatar avatar)
        {
            if (!avatar.Pain || !FemaleVoice(avatar.Character)) return;
            var own = LocalController ? LocalController.transform.Find("Audio/Hurt Audio") : null;
            var random = own ? own.GetComponent<AudioSourceRandom>() : null;
            var clips = random != null && random.Audio != null && RandomClips != null ? RandomClips.GetValue(random.Audio) as AudioClip[] : null;
            if (clips == null || clips.Length == 0) return;
            var clip = clips[stepDice.Next(clips.Length)];
            if (!clip) return;
            var source = own.GetComponent<AudioSource>();
            if (source) avatar.Pain.outputAudioMixerGroup = source.outputAudioMixerGroup;
            avatar.Pain.pitch = 0.95f + (float)stepDice.NextDouble() * 0.1f;
            avatar.Pain.PlayOneShot(clip);
        }

        private ParticleSystem BloodFor(Avatar avatar)
        {
            if (avatar.Blood) return avatar.Blood;
            var own = LocalController ? LocalController.transform.Find("PS_Blood_Burst_Omni") : null;
            var burst = own ? own.GetComponent<ParticleSystem>() : null;
            if (!burst) return null;
            var copy = UnityEngine.Object.Instantiate(burst.gameObject, avatar.Root.transform, false);
            copy.name = "NMM_Blood";
            copy.transform.localPosition = Vector3.up * 1.25f; copy.transform.localRotation = Quaternion.identity;
            avatar.Blood = copy.GetComponent<ParticleSystem>();
            avatar.Blood.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            return avatar.Blood;
        }

        /// <summary>Automated tests: footsteps played for teammates.</summary>
        internal static int StepsPlayed;
        internal static string StepPace = "";
        /// <summary>The local player is still lying in the opening scene's bathtub.</summary>
        internal static bool LocalBathing;
        /// <summary>The local player's controller, so teammates' bodies never trap it.</summary>
        internal static CharacterController LocalController;
        private const float BlockerRadius = 0.28f, PersonalSpace = 0.55f; // Centre to centre, standing shoulder to shoulder.
        private static int blockerLayer = -2;
        /// <summary>A layer the local player collides with but the game's aiming and interaction checks do not use
        /// by name. The game turns off Player-Player collisions, so teammates would otherwise walk through each other.</summary>
        private int BlockerLayer()
        {
            if (blockerLayer != -2) return blockerLayer;
            int player = LayerMask.NameToLayer("Player");
            blockerLayer = -1;
            foreach (string name in new[] { "DontCollideEnvironment", "HerdSim" })
            {
                int layer = LayerMask.NameToLayer(name);
                if (layer >= 0 && player >= 0 && !Physics.GetIgnoreLayerCollision(layer, player)) { blockerLayer = layer; break; }
            }
            log("Teammate bodies block players on layer " + (blockerLayer >= 0 ? LayerMask.LayerToName(blockerLayer) : "(none available)"));
            return blockerLayer;
        }
        private readonly Dictionary<int, Avatar> players = new Dictionary<int, Avatar>();
        private readonly Dictionary<string, CharacterModel> models = new Dictionary<string, CharacterModel>(StringComparer.Ordinal);
        private readonly Action<string> log;
        private Material fallbackMaterial;
        private bool disposed;

        public RemotePlayers(Action<string> log) { this.log = log ?? delegate { }; }
        public int RemoteCount { get { return players.Count; } }
        public IEnumerable<int> Ids { get { return new List<int>(players.Keys); } }
        internal Func<string, GrannyClone.ItemInteractable> ItemResolver;
        /// <summary>Under a bed, a teammate lies facing out of it (towards the room), not the way they look.</summary>
        internal Func<string, Vector3?> HideFacing;
        /// <summary>The way the opening's bathtub faces: a teammate lying in it stays put and only turns the head.</summary>
        internal Func<Vector3?> BathFacing;

        public void Update(PlayerFrame frame)
        {
            if (disposed || frame == null || frame.id < 0 || !Finite(frame.position) || !Finite(frame.rotation)) return;
            string character = ValidCharacter(frame.character);
            Avatar avatar;
            if (players.TryGetValue(frame.id, out avatar) && avatar.Character != character) { Remove(frame.id); avatar = null; } // Changed character.
            if (avatar == null)
            {
                avatar = Create(frame.id, character);
                players.Add(frame.id, avatar);
            }
            bool leftBath = avatar.Frame != null && avatar.Frame.bath && !frame.bath;
            if (frame.downed && frame.bleed > 0f && avatar.Root.activeSelf)
            {
                avatar.DownSpot = avatar.Root.transform.position;
                avatar.DownTurn = avatar.Root.transform.rotation;
                avatar.DownSeen = Time.unscaledTime;
            }
            if (avatar.Frame != null && avatar.Frame.body && !avatar.Frame.spectating && frame.spectating && frame.scene == avatar.Frame.scene)
            {
                // Out for good: their body stays where they lay bleeding (the game's death sequence may have moved them since).
                bool fell = Time.unscaledTime - avatar.DownSeen < 30f;
                if (fell || avatar.Root.activeSelf)
                    LeaveBody(frame.id, character, fell ? avatar.DownSpot : avatar.Root.transform.position,
                        fell ? avatar.DownTurn : avatar.Root.transform.rotation);
            }
            avatar.Frame = frame;
            if (leftBath) avatar.Snap = true; // Out of the tub: appear beside it, no climbing animation.
            if (avatar.First || leftBath || (avatar.Root.transform.position - frame.position).sqrMagnitude > 100f)
            {
                avatar.Root.transform.position = frame.position;
                avatar.Root.transform.rotation = Yaw(frame.rotation);
                avatar.First = false;
            }
            SetVisibility(avatar);
        }

        public void Tick()
        {
            if (disposed) return;
            if (bodies.Count > 0 && SceneManager.GetActiveScene().name != bodiesScene) ClearBodies(); // A new level.
            foreach (var body in bodies) if (body.Root && body.Root.activeSelf == HideAll) body.Root.SetActive(!HideAll);
            float dt = Mathf.Clamp(Time.unscaledDeltaTime, 0f, 0.25f);
            float blend = 1f - Mathf.Exp(-14f * dt);
            foreach (var avatar in players.Values)
            {
                if (avatar.Root == null || avatar.Frame == null) continue;
                SetVisibility(avatar);
                if (!avatar.Root.activeSelf) continue;
                var frame = avatar.Frame;
                Transform root = avatar.Root.transform;
                root.position = Vector3.Lerp(root.position, frame.position, blend);
                Vector3 moved = root.position - avatar.LastSpot; moved.y = 0f;
                float pace = dt > 0f ? moved.magnitude / dt : 0f;
                avatar.LastSpot = root.position;
                avatar.Pace = pace > 8f ? 0f : Mathf.Lerp(avatar.Pace, pace, 1f - Mathf.Exp(-10f * dt)); // A jump across the map is no walk.
                // Lying in the tub or under a bed the body keeps still, facing the tub's length or out of the bed;
                // looking around turns only the head (LookYaw below).
                Vector3? fixedFacing = frame.state == (int)GrannyClone.PlayerStates.States.Hiding && HideFacing != null ? HideFacing(frame.bed)
                    : frame.bath && BathFacing != null ? BathFacing() : null;
                if (frame.downed && Finite(frame.look) && Vector3.ProjectOnPlane(frame.look, Vector3.up).sqrMagnitude > 0.01f)
                    fixedFacing = frame.look; // Downed: lying towards where they look, which is where they crawl.
                if (!fixedFacing.HasValue && !frame.downed && (frame.vent || avatar.LowCrawl)) fixedFacing = CrawlWay(avatar, frame);
                else avatar.CrawlWay = Vector3.zero;
                Quaternion facing = fixedFacing.HasValue && fixedFacing.Value.sqrMagnitude > 0.001f
                    ? Quaternion.LookRotation(Vector3.ProjectOnPlane(fixedFacing.Value, Vector3.up)) : Yaw(frame.rotation);
                root.rotation = Quaternion.Slerp(root.rotation, facing, blend);
                Vector3 flatLook = Finite(frame.look) ? Vector3.ProjectOnPlane(frame.look, Vector3.up) : Vector3.zero;
                Vector3 velocity = Finite(frame.velocity) ? Vector3.ClampMagnitude(new Vector3(frame.velocity.x, 0f, frame.velocity.z), 8f) : Vector3.zero;
                avatar.Velocity = Vector3.Lerp(avatar.Velocity, velocity, 1f - Mathf.Exp(-10f * dt));
                bool walking = frame.health > 0f && !frame.downed && !frame.bath && !frame.crouching && !frame.spectating
                    && frame.state != (int)GrannyClone.PlayerStates.States.Hiding && frame.reviving <= 0 && Finite(frame.velocity) && Mathf.Abs(frame.velocity.y) < 2.5f;
                Footsteps(avatar, walking ? avatar.Pace : 0f, dt);

                bool alive = frame.health > 0f || frame.downed;
                if (Time.unscaledTime >= avatar.NextCeilingCheck)
                {
                    // Crouching under something low (every vent, a table): how much room there is, so the head ducks under it.
                    avatar.NextCeilingCheck = Time.unscaledTime + 0.2f;
                    avatar.HeadroomMeasured = frame.crouching || frame.vent ? HeadroomAt(root.position) : 3f;
                }
                avatar.Headroom = Mathf.Lerp(avatar.Headroom, avatar.HeadroomMeasured, 1f - Mathf.Exp(-8f * dt));
                // Too low to crouch (a vent, under a table): on hands and knees with the crawl. Back up once there is room.
                if (!(frame.crouching || frame.vent)) avatar.LowCrawl = false;
                else if (avatar.HeadroomMeasured < 1.3f) avatar.LowCrawl = true;
                else if (avatar.HeadroomMeasured > 1.45f) avatar.LowCrawl = false;
                var input = new AvatarInput
                {
                    ForwardSpeed = Vector3.Dot(avatar.Velocity, root.forward),
                    SideSpeed = Vector3.Dot(avatar.Velocity, root.right),
                    Crouching = frame.crouching || frame.vent,
                    Headroom = avatar.Headroom,
                    Jumping = frame.jumping,
                    VerticalSpeed = Finite(frame.velocity) ? frame.velocity.y : 0f,
                    Gun = avatar.HoldsGun,
                    Lean = Finite(frame.lean) ? Mathf.Clamp(frame.lean, -1f, 1f) : 0f,
                    Holding = !string.IsNullOrEmpty(frame.equipped),
                    LookPitch = Finite(frame.look) && frame.look.sqrMagnitude > 0.0001f ? Mathf.Asin(Mathf.Clamp(frame.look.normalized.y, -1f, 1f)) * Mathf.Rad2Deg : 0f,
                    LookYaw = flatLook.sqrMagnitude > 0.0001f ? Vector3.SignedAngle(facing * Vector3.forward, flatLook, Vector3.up) : 0f,
                    Pose = frame.downed ? AvatarPose.Downed
                        : frame.bath ? AvatarPose.Bathing
                        : frame.state == (int)GrannyClone.PlayerStates.States.Hiding ? AvatarPose.Prone
                        : frame.reviving > 0 ? AvatarPose.Kneeling
                        : frame.vent || avatar.LowCrawl ? AvatarPose.Crawling
                        : AvatarPose.Standing
                };
                if (avatar.Rig != null)
                {
                    if (avatar.Shots >= 0 && frame.shots > avatar.Shots) { avatar.Rig.Fire(); GunshotFrom(avatar); } // They fired since the last frame.
                    avatar.Shots = frame.shots;
                    if (avatar.Points >= 0 && frame.points > avatar.Points) avatar.Rig.Point();
                    avatar.Points = frame.points;
                    if (avatar.Whistles >= 0 && frame.whistles > avatar.Whistles) { avatar.Rig.Whistle(); WhistleFrom(avatar); }
                    avatar.Whistles = frame.whistles;
                    if (avatar.Snap) { avatar.Rig.Snap(input, 0f); avatar.Snap = false; } // Straight into the new pose.
                    else avatar.Rig.Animate(input, dt);
                    if (TestWatchClipping) WatchClipping(avatar, input.Pose);
                }

                // The collider is what the Nun's attacks hit; downed players are out of reach.
                float height = input.Pose == AvatarPose.Standing ? (frame.crouching ? 1.15f : ModelHeight) : 0.6f;
                avatar.Collider.height = Mathf.Lerp(avatar.Collider.height, height, blend);
                avatar.Collider.center = new Vector3(0f, avatar.Collider.height * 0.5f, 0f);
                avatar.Collider.enabled = alive && !frame.downed;
                avatar.Solid.enabled = alive && input.Pose == AvatarPose.Standing;
                avatar.Solid.height = avatar.Collider.height; avatar.Solid.center = avatar.Collider.center;
                avatar.Light.enabled = frame.light && alive;
                bool inHand = input.Holding && avatar.Hand;
                avatar.Light.transform.position = inHand ? avatar.Hand.position : root.position + Vector3.up * 1.55f + root.forward * 0.2f;
                if (Finite(frame.look) && frame.look.sqrMagnitude > 0.0001f) avatar.Light.transform.rotation = Quaternion.LookRotation(frame.look.normalized);
                UpdateHeldItem(avatar);
                // Something long (a board) is carried in both hands across the body, unless a hand is busy (reaching for a door).
                bool twoHands = avatar.Held && avatar.Hand && avatar.Rig != null && avatar.HeldLong
                    && (input.Pose == AvatarPose.Standing || input.Pose == AvatarPose.Kneeling) && !avatar.Rig.Reaching;
                avatar.TwoHands = avatar.Held ? Mathf.MoveTowards(avatar.TwoHands, twoHands ? 1f : 0f, dt * 5f) : 0f;
                if (avatar.Held && avatar.Hand) HoldInHand(avatar);
                if (avatar.TwoHands > 0.001f && avatar.Held && avatar.Rig != null) CarryAcross(avatar, root);
            }
        }

        private static void Footsteps(Avatar avatar, float speed, float dt)
        {
            var controller = LocalController;
            var player = controller ? controller.GetComponent<GrannyClone.FirstPersonController>() : null;
            if (!avatar.Steps || !player || speed < 0.4f) { avatar.StepCycle = 0f; return; }
            Transform root = avatar.Root.transform;
            if (Time.unscaledTime >= avatar.NextFloorCheck)
            {
                avatar.NextFloorCheck = Time.unscaledTime + 0.5f;
                RaycastHit hit;
                if (Physics.Raycast(root.position + Vector3.up * 0.5f, Vector3.down, out hit, 2f, LayerMask.GetMask("Ground"), QueryTriggerInteraction.Ignore))
                {
                    FieldInfo kind = hit.collider.CompareTag("GroundWood") ? WoodSteps
                        : hit.collider.CompareTag("GroundCarpet") ? CarpetSteps : hit.collider.CompareTag("GroundTile")
                        ? TileSteps : null;
                    var clips = kind != null ? kind.GetValue(player) as AudioClip[] : DefaultSteps;
                    if (clips != null && clips.Length > 0) avatar.Floor = clips;
                }
                if (avatar.Floor == null) avatar.Floor = DefaultSteps ?? (CurrentSteps != null ? CurrentSteps.GetValue(player) as AudioClip[] : null);
            }
            // The player's own pace: the step cycle grows by the speed plus the walking speed (scaled by the run
            // stride when running), a step every stepInterval.
            float interval = StepInterval != null ? Mathf.Max(0.5f, (float)StepInterval.GetValue(player)) : 5f;
            float walkSpeed = WalkSpeed != null ? (float)WalkSpeed.GetValue(player) : 0f;
            float stride = RunStepLength != null && walkSpeed > 0f && speed > walkSpeed * 1.25f ? (float)RunStepLength.GetValue(player) : 1f;
            avatar.StepCycle += (speed + speed * stride) * dt;
            if (StepPace.Length == 0)
                StepPace = "interval=" + interval.ToString("0.##") + " walk=" + walkSpeed.ToString("0.##") + " run stride="
                    + (RunStepLength != null ? ((float)RunStepLength.GetValue(player)).ToString("0.##") : "-");
            if (avatar.StepCycle < interval) return;
            avatar.StepCycle -= interval;
            if (avatar.Floor == null || avatar.Floor.Length == 0) return;
            var clip = avatar.Floor[stepDice.Next(avatar.Floor.Length)];
            if (!clip) return;
            var own = StepSource != null ? StepSource.GetValue(player) as AudioSource : null;
            if (own) { avatar.Steps.outputAudioMixerGroup = own.outputAudioMixerGroup; avatar.Steps.volume = own.volume; }
            avatar.Steps.PlayOneShot(clip, 0.85f);
            StepsPlayed++;
        }

        private static int ceilingMask;
        /// <summary>Metres from the feet up to whatever is over them, 3 when nothing is within reach of a crouch.</summary>
        /// <summary>On hands and knees in a vent the body lies along the way they crawl, so it stays inside the duct while they
        /// look around (only the head turns). Backing up keeps it facing ahead; standing still keeps it as it was.</summary>
        private static Vector3 CrawlWay(Avatar avatar, PlayerFrame frame)
        {
            Vector3 view = Yaw(frame.rotation) * Vector3.forward;
            if (avatar.CrawlWay.sqrMagnitude < 0.01f) avatar.CrawlWay = view;
            Vector3 way = Finite(frame.velocity) ? new Vector3(frame.velocity.x, 0f, frame.velocity.z) : Vector3.zero;
            // Really crawling along (not a teleport's jump or being pushed out of a wall).
            if (way.sqrMagnitude > 0.04f && way.sqrMagnitude < 6.25f && avatar.Pace > 0.1f)
            {
                way.Normalize();
                float along = Vector3.Dot(way, view);
                // Mostly where they look: head first; mostly away from it: backing up; sideways: whichever end leads now.
                avatar.CrawlWay = along > 0.4f ? way : along < -0.4f ? -way : Vector3.Dot(way, avatar.CrawlWay) >= 0f ? way : -way;
            }
            return avatar.CrawlWay;
        }

        private static float HeadroomAt(Vector3 feet)
        {
            if (ceilingMask == 0)
            {
                ceilingMask = Physics.DefaultRaycastLayers;
                foreach (string layer in new[] { "Player", "CameraTrigger", "DontCollideEnvironment" })
                {
                    int index = LayerMask.NameToLayer(layer);
                    if (index >= 0) ceilingMask &= ~(1 << index);
                }
            }
            RaycastHit hit;
            return Physics.Raycast(feet + Vector3.up * 0.3f, Vector3.up, out hit, 1.5f, ceilingMask, QueryTriggerInteraction.Ignore) ? 0.3f + hit.distance : 3f;
        }

        /// <summary>Teammates take up room: which way, and how far, the local player must step to stand clear of
        /// every teammate. The caller moves with CharacterController.Move, which never passes through a wall (a teammate
        /// collider pushing into the player could shove them through a wall or the floor).</summary>
        internal Vector3 Separation(Vector3 feet, int localId)
        {
            Vector3 push = Vector3.zero;
            foreach (var avatar in players.Values)
            {
                if (avatar.Root == null || !avatar.Root.activeSelf || avatar.Frame == null) continue;
                var f = avatar.Frame;
                if (f.downed || f.bath || f.spectating || f.health <= 0f || f.state == (int)GrannyClone.PlayerStates.States.Hiding) continue;
                Vector3 gap = feet - avatar.Root.transform.position;
                if (Mathf.Abs(gap.y) > 1.6f) continue; // Another floor, or up the stairs.
                gap.y = 0f;
                float apart = gap.magnitude;
                if (apart >= PersonalSpace) continue;
                Vector3 away = apart > 0.01f ? gap / apart : (localId < avatar.Id ? Vector3.right : Vector3.left); // On the same spot: one each way.
                push += away * (PersonalSpace - apart);
            }
            return push;
        }

        public Transform GetTransform(int id)
        {
            Avatar avatar;
            return players.TryGetValue(id, out avatar) && avatar.Root != null ? avatar.Root.transform : null;
        }

        /// <summary>Automated tests: where each teammate's avatar is and how it is posed.</summary>
        internal string Describe()
        {
            return "steps=" + StepsPlayed + " " + StepPace + " hurts=" + Hurts + " reaches=" + Reaches + " bodies=" + BodiesLeft + " ; " + string.Join(" ; ", players.Values.Select(a =>
            {
                Transform head = a.Rig != null ? a.Rig.Bone("head") : null, pelvis = a.Rig != null ? a.Rig.Bone("pelvis") : null;
                return a.Id + " visible=" + (a.Root != null && a.Root.activeInHierarchy) + " bath=" + (a.Frame != null && a.Frame.bath)
                    + " root=" + (a.Root != null ? a.Root.transform.position.ToString("0.00") : "-") + " head=" + (head ? head.position.ToString("0.00") : "-") + " pelvis=" + (pelvis ? pelvis.position.ToString("0.00") : "-")
                    + string.Concat(new[] { "shin.L", "shin.R", "foot_left", "foot_right", "forearm_left", "forearm_right", "hand_left", "hand_right" }.Select(n => { Transform b = a.Rig != null ? a.Rig.Bone(n) : null; return b ? " " + n + "=" + b.position.ToString("0.00") : ""; }).ToArray())
                    + (head ? " headYaw=" + Vector3.SignedAngle(a.Root.transform.forward, Vector3.ProjectOnPlane(-head.forward, Vector3.up), Vector3.up).ToString("0") + " headPitch=" + (Mathf.Asin(Mathf.Clamp(-head.forward.y, -1f, 1f)) * Mathf.Rad2Deg).ToString("0") + " lookYaw=" + (a.Frame != null ? Vector3.SignedAngle(a.Root.transform.forward, Vector3.ProjectOnPlane(a.Frame.look, Vector3.up), Vector3.up).ToString("0") : "-") + " lookPitch=" + (a.Frame != null && a.Frame.look.sqrMagnitude > 0.01f ? (Mathf.Asin(Mathf.Clamp(a.Frame.look.normalized.y, -1f, 1f)) * Mathf.Rad2Deg).ToString("0") : "-") : "")
                    + (a.Hand ? " handR right=" + a.Hand.right.ToString("0.00") + " up=" + a.Hand.up.ToString("0.00") + " fwd=" + a.Hand.forward.ToString("0.00") + " bodyRight=" + a.Root.transform.right.ToString("0.00") + " holding=" + (a.Held != null) : "");
            }).ToArray());
        }

        // Tests: how far teammates' bodies went into the scenery while watched. Each joint is a ball about as wide as the body
        // there; how deep the scenery cuts into it is how far the skin goes through (worst per joint, with the push-out way).
        internal static bool TestWatchClipping;
        private int watchFrames, watchClipped;
        private readonly Dictionary<string, KeyValuePair<float, string>> watchWorst = new Dictionary<string,
            KeyValuePair<float, string>>(StringComparer.Ordinal);
        private static readonly KeyValuePair<string, float>[] WatchedJoints =
        {
            new KeyValuePair<string, float>("pelvis", 0.1f), new KeyValuePair<string, float>("spine", 0.1f), new KeyValuePair<string, float>("chest", 0.11f),
            new KeyValuePair<string, float>("neck", 0.05f), new KeyValuePair<string, float>("head", 0.1f),
            new KeyValuePair<string, float>("upper_arm_left", 0.05f), new KeyValuePair<string, float>("upper_arm_right", 0.05f),
            new KeyValuePair<string, float>("forearm_left", 0.04f), new KeyValuePair<string, float>("forearm_right", 0.04f),
            new KeyValuePair<string, float>("hand_left", 0.03f), new KeyValuePair<string, float>("hand_right", 0.03f),
            new KeyValuePair<string, float>("thigh_left", 0.07f), new KeyValuePair<string, float>("thigh_right", 0.07f),
            new KeyValuePair<string, float>("shin.L", 0.05f), new KeyValuePair<string, float>("shin.R", 0.05f),
            new KeyValuePair<string, float>("foot_left", 0.05f), new KeyValuePair<string, float>("foot_right", 0.05f),
        };
        private SphereCollider watchBall;

        private void WatchClipping(Avatar avatar, AvatarPose pose)
        {
            var rig = avatar.Rig;
            int mask = Physics.DefaultRaycastLayers;
            foreach (string layer in new[] { "Player", "CameraTrigger", "DontCollideEnvironment" })
            {
                int index = LayerMask.NameToLayer(layer);
                if (index >= 0) mask &= ~(1 << index);
            }
            if (!watchBall)
            {
                var probe = new GameObject("NMM_ClipProbe");
                UnityEngine.Object.DontDestroyOnLoad(probe);
                probe.layer = LayerMask.NameToLayer("Player");
                watchBall = probe.AddComponent<SphereCollider>();
                watchBall.isTrigger = true;
            }
            watchFrames++;
            bool clipped = false;
            foreach (var joint in WatchedJoints)
            {
                Transform bone = rig.Bone(joint.Key);
                if (!bone) continue;
                Vector3 centre = joint.Key == "head" ? bone.position + bone.up * 0.1f : bone.position;
                watchBall.radius = joint.Value;
                foreach (var wall in Physics.OverlapSphere(centre, joint.Value, mask, QueryTriggerInteraction.Ignore))
                {
                    Vector3 way; float depth;
                    if (!Physics.ComputePenetration(watchBall, centre, Quaternion.identity, wall, wall.transform.position, wall.transform.rotation, out way, out depth)
                        || depth < 0.015f)
                        continue;
                    clipped = true;
                    KeyValuePair<float, string> worst;
                    if (!watchWorst.TryGetValue(joint.Key, out worst) || depth > worst.Key)
                        watchWorst[joint.Key] = new KeyValuePair<float,
                            string>(depth, pose + " " + wall.name + " out=" + way.ToString("0.0") + " at " + centre.ToString("0.00"));
                }
            }
            // The soles: toe or heel under the floor (the ankle spheres above only catch a foot sunk past the ankle).
            float sole = rig.SoleDepth;
            if (sole > 0.015f)
            {
                clipped = true;
                KeyValuePair<float, string> worst;
                if (!watchWorst.TryGetValue("sole", out worst) || sole > worst.Key)
                    watchWorst["sole"] = new KeyValuePair<float,
                        string>(sole, pose + (rig.Airborne ? " airborne" : "") + " at " + rig.Bone("pelvis").position.ToString("0.00") );
            }
            if (clipped) watchClipped++;
        }

        internal string ClippingReport()
        {
            string text = "frames=" + watchFrames + " clipped=" + watchClipped + " ; "
                + string.Join(" ; ", watchWorst.OrderByDescending(p => p.Value.Key).Select(p => p.Key + " " + p.Value.Key.ToString("0.000") + " " + p.Value.Value).ToArray());
            watchFrames = watchClipped = 0; watchWorst.Clear();
            return text;
        }

        /// <summary>
        /// A teammate's revolver going off, heard where they are: the gun's own report, deafening close by, carrying through
        /// the whole building (muffled through walls, a thud through a floor), as their voice would but far louder.
        /// </summary>
        private void GunshotFrom(Avatar avatar)
        {
            AudioClip clip = GunshotClip();
            if (!clip) return;
            Transform hand = avatar.Rig != null ? avatar.Rig.Bone("hand_right") : null;
            Vector3 at = hand ? hand.position : avatar.Root.transform.position + Vector3.up * 1.4f;
            if (!avatar.Shot)
            {
                var go = new GameObject("NMM_Gunshot");
                go.transform.SetParent(avatar.Root.transform, false);
                avatar.Shot = go.AddComponent<AudioSource>();
                avatar.Shot.spatialBlend = 1f; avatar.Shot.playOnAwake = false; avatar.Shot.dopplerLevel = 0f;
                avatar.Shot.rolloffMode = AudioRolloffMode.Logarithmic; avatar.Shot.minDistance = 6f; avatar.Shot.maxDistance = 90f;
                avatar.ShotFilter = go.AddComponent<AudioLowPassFilter>();
                var game = UnityEngine.Object.FindObjectsOfType<AudioSource>().FirstOrDefault(s => s && s.outputAudioMixerGroup && s.gameObject.name.IndexOf("Footstep", StringComparison.OrdinalIgnoreCase) >= 0);
                if (game) avatar.Shot.outputAudioMixerGroup = game.outputAudioMixerGroup; // The game's own sound settings apply.
            }
            avatar.Shot.transform.position = at;
            Vector3 facing = avatar.Frame != null && Finite(avatar.Frame.look) && avatar.Frame.look.sqrMagnitude > 0.01f
                ? avatar.Frame.look.normalized : avatar.Root.transform.forward;
            var voice = Plugin.Instance != null && Plugin.Instance.Session != null ? Plugin.Instance.Session.Voice : null;
            var heard = voice != null ? voice.HearFrom(avatar.Id, at, facing) : new Voice.VoiceChat.Heard { Gain = 1f, Cutoff = 16000f };
            avatar.Shot.volume = Mathf.Clamp01(0.35f + 0.65f * heard.Gain); // A gunshot is never a murmur.
            avatar.ShotFilter.cutoffFrequency = Mathf.Max(700f, heard.Cutoff);
            avatar.Shot.PlayOneShot(clip);
            log("Heard peer " + avatar.Id + "'s gunshot (volume " + avatar.Shot.volume.ToString("0.00") + ", cutoff "
                + avatar.ShotFilter.cutoffFrequency.ToString("0") + " Hz)");
        }

        private static AudioClip gunshotClip;
        private static readonly System.Reflection.FieldInfo GunshotAudioField = HarmonyLib.AccessTools.Field(typeof(GrannyClone.RevolverInteractable),
            "gunshotAudio");
        /// <summary>The revolver's own gunshot (any copy of the gun in the level has it).</summary>
        private static AudioClip GunshotClip()
        {
            if (gunshotClip) return gunshotClip;
            if (GunshotAudioField == null) return null;
            foreach (var gun in Resources.FindObjectsOfTypeAll<GrannyClone.RevolverInteractable>())
            {
                var source = gun ? GunshotAudioField.GetValue(gun) as AudioSource : null;
                if (source && source.clip) return gunshotClip = source.clip;
            }
            return null;
        }

        /// <summary>A teammate's whistle, from their mouth, through the building as their voice would come: loud enough to
        /// carry down a corridor (45 m in the open), muffled through walls, a murmur through a floor.</summary>
        private void WhistleFrom(Avatar avatar)
        {
            Transform head = avatar.Rig != null ? avatar.Rig.Bone("head") : null;
            Vector3 mouth = head ? head.position : avatar.Root.transform.position + Vector3.up * 1.6f;
            if (!avatar.Call)
            {
                var go = new GameObject("NMM_Whistle");
                go.transform.SetParent(avatar.Root.transform, false);
                avatar.Call = go.AddComponent<AudioSource>();
                avatar.Call.spatialBlend = 1f; avatar.Call.playOnAwake = false; avatar.Call.dopplerLevel = 0f;
                avatar.Call.rolloffMode = AudioRolloffMode.Logarithmic; avatar.Call.minDistance = 3f; avatar.Call.maxDistance = 45f;
                avatar.CallFilter = go.AddComponent<AudioLowPassFilter>();
                var game = UnityEngine.Object.FindObjectsOfType<AudioSource>().FirstOrDefault(s => s && s.outputAudioMixerGroup && s.gameObject.name.IndexOf("Footstep", StringComparison.OrdinalIgnoreCase) >= 0);
                if (game) avatar.Call.outputAudioMixerGroup = game.outputAudioMixerGroup; // The game's own sound settings apply.
            }
            avatar.Call.transform.position = mouth;
            Vector3 facing = avatar.Frame != null && Finite(avatar.Frame.look) && avatar.Frame.look.sqrMagnitude > 0.01f
                ? avatar.Frame.look.normalized : avatar.Root.transform.forward;
            var voice = Plugin.Instance != null && Plugin.Instance.Session != null ? Plugin.Instance.Session.Voice : null;
            var heard = voice != null ? voice.HearFrom(avatar.Id, mouth, facing) : new Voice.VoiceChat.Heard { Gain = 1f, Cutoff = 16000f };
            avatar.Call.volume = Mathf.Clamp01(0.9f * heard.Gain);
            avatar.CallFilter.cutoffFrequency = heard.Cutoff;
            avatar.Call.PlayOneShot(Gameplay.Gestures.Clip());
        }

        /// <summary>Where a teammate's eyes are (their model's head, whatever the pose). Null if not shown.</summary>
        internal Vector3? GetHead(int id)
        {
            Avatar avatar;
            if (!players.TryGetValue(id, out avatar) || avatar.Root == null || !avatar.Root.activeInHierarchy || avatar.Rig == null) return null;
            Transform head = avatar.Rig.Bone("head");
            return head ? head.position + Vector3.up * 0.06f : (Vector3?)null;
        }

        /// <summary>Tests: where the middle of a teammate's right hand is in the world.</summary>
        internal Vector3 HandMiddle(int id)
        {
            Avatar avatar;
            if (!players.TryGetValue(id, out avatar) || !avatar.Hand) return Vector3.zero;
            return avatar.Rig != null ? avatar.Hand.TransformPoint(avatar.Rig.HandCenter) : avatar.Hand.position;
        }

        /// <summary>Tests: a teammate's right hand, their body, and what they hold (null if not shown).</summary>
        internal Transform GetHand(int id, out Transform body, out GameObject held)
        {
            Avatar avatar; body = null; held = null;
            if (!players.TryGetValue(id, out avatar) || avatar.Root == null || !avatar.Root.activeInHierarchy || !avatar.Hand) return null;
            body = avatar.Root.transform; held = avatar.Held;
            return avatar.Hand;
        }

        /// <summary>Approximate chest position, for revive targeting and markers.</summary>
        internal Vector3? GetCenter(int id)
        {
            Avatar avatar;
            if (!players.TryGetValue(id, out avatar) || avatar.Root == null || !avatar.Root.activeInHierarchy || avatar.Frame == null) return null;
            Transform chest = avatar.Rig != null ? avatar.Rig.Bone("chest") : null;
            return chest ? chest.position : avatar.Root.transform.position + Vector3.up * (avatar.Frame.downed ? 0.3f : 1.2f);
        }

        /// <summary>Maps a hit collider's transform (or visual descendant) to a peer. -1 means no peer.</summary>
        public int GetPeer(Transform transform)
        {
            while (transform != null)
            {
                foreach (var avatar in players.Values)
                    if (avatar.Root != null && avatar.Root.transform == transform) return avatar.Id;
                transform = transform.parent;
            }
            return -1;
        }

        public void Remove(int id)
        {
            Avatar avatar;
            if (!players.TryGetValue(id, out avatar)) return;
            players.Remove(id);
            Destroy(avatar);
        }

        public void Clear()
        {
            foreach (var avatar in players.Values) Destroy(avatar);
            players.Clear();
            ClearBodies();
        }

        private static void Destroy(Avatar avatar)
        {
            if (avatar.Root != null) UnityEngine.Object.Destroy(avatar.Root);
            if (avatar.Material != null) UnityEngine.Object.Destroy(avatar.Material);
        }

        // ---------- Lobby customisation preview ----------

        /// <summary>A stand-alone character for the customisation screen, far from the level.</summary>
        internal sealed class Preview
        {
            internal readonly RemotePlayers Owner;
            internal readonly GameObject Stage;
            internal readonly Camera Camera;
            internal readonly RenderTexture Texture;
            private Avatar avatar;
            private float clock;
            internal Preview(RemotePlayers owner, int width, int height)
            {
                Owner = owner;
                Stage = new GameObject("NMM_CustomizeStage");
                UnityEngine.Object.DontDestroyOnLoad(Stage);
                Stage.transform.position = new Vector3(0f, -6000f, 0f);
                var cameraObject = new GameObject("Camera");
                cameraObject.transform.SetParent(Stage.transform, false);
                Camera = cameraObject.AddComponent<Camera>();
                Camera.clearFlags = CameraClearFlags.SolidColor;
                Camera.backgroundColor = new Color(0.05f, 0.04f, 0.09f, 1f);
                Camera.fieldOfView = 32f; Camera.nearClipPlane = 0.1f; Camera.farClipPlane = 15f;
                Camera.transform.localPosition = new Vector3(0f, 1.1f, 4.4f);
                Camera.transform.LookAt(Stage.transform.position + Vector3.up * 0.95f);
                Texture = new RenderTexture(width, height, 24) { name = "NMM_CustomizePreview" };
                Camera.targetTexture = Texture;
                foreach (var spot in new[] { new Vector3(1.6f, 2.6f, 2.4f), new Vector3(-2f, 1.4f, -1.5f) })
                {
                    var lamp = new GameObject("Light").AddComponent<Light>();
                    lamp.transform.SetParent(Stage.transform, false);
                    lamp.transform.localPosition = spot;
                    lamp.type = LightType.Point; lamp.range = 10f; lamp.intensity = spot.x > 0 ? 2.2f : 0.9f;
                    lamp.color = spot.x > 0 ? new Color(1f, 0.92f, 0.85f) : new Color(0.6f, 0.65f, 1f);
                }
            }

            internal void Show(string character)
            {
                character = ValidCharacter(character);
                if (avatar != null && avatar.Character != character) { RemotePlayers.Destroy(avatar); avatar = null; }
                if (avatar == null)
                {
                    avatar = Owner.Create(-1, character);
                    avatar.Root.transform.SetParent(Stage.transform, false);
                    avatar.Root.transform.localPosition = Vector3.zero;
                    avatar.Collider.enabled = false; avatar.Solid.enabled = false;
                }
                if (avatar.Rig != null) avatar.Rig.Snap(new AvatarInput(), 0f); // A still, neutral pose.
            }

            /// <summary>Turntable: the posed character slowly turns so every side can be seen.</summary>
            internal void Animate(float dt)
            {
                if (avatar == null) return;
                clock += dt;
                avatar.Root.transform.localRotation = Quaternion.Euler(0f, Mathf.Sin(clock * 0.45f) * 70f, 0f); // Faces the camera.
                if (Application.isBatchMode) Camera.Render(); // Headless test runs do not render cameras on their own.
            }

            internal void Dispose()
            {
                if (avatar != null) RemotePlayers.Destroy(avatar);
                Camera.targetTexture = null;
                Texture.Release();
                UnityEngine.Object.Destroy(Texture);
                UnityEngine.Object.Destroy(Stage);
            }
        }

        internal Preview CreatePreview(int width, int height) { return new Preview(this, width, height); }

        /// <summary>Renders each character in each animation pose, one row per character, to a PNG.</summary>
        internal void WriteModelPreview(string path)
        {
            // Walk and run cycles at quarter phases, then the other poses.
            var poses = new[]
            {
                new AvatarInput(),
                new AvatarInput { ForwardSpeed = 1.6f }, new AvatarInput { ForwardSpeed = 1.6f }, new AvatarInput { ForwardSpeed = 1.6f }, new AvatarInput { ForwardSpeed = 1.6f },
                new AvatarInput { ForwardSpeed = 4.5f }, new AvatarInput { ForwardSpeed = 4.5f },
                new AvatarInput { Crouching = true, ForwardSpeed = 0.8f },
                new AvatarInput { Crouching = true, Headroom = 1.15f },
                new AvatarInput { Holding = true, LookPitch = -20f, ForwardSpeed = 1.6f },
                new AvatarInput { Pose = AvatarPose.Kneeling },
                new AvatarInput { Pose = AvatarPose.Downed },
                new AvatarInput { Pose = AvatarPose.Prone },
                new AvatarInput { Pose = AvatarPose.Bathing, LookPitch = 20f },
                new AvatarInput { Pose = AvatarPose.Downed, ForwardSpeed = 0.5f }, new AvatarInput { Pose = AvatarPose.Downed, ForwardSpeed = 0.5f },
                new AvatarInput { Pose = AvatarPose.Downed, ForwardSpeed = 0.5f }, new AvatarInput { Pose = AvatarPose.Downed, ForwardSpeed = 0.5f },
                new AvatarInput { Pose = AvatarPose.Dead },
                new AvatarInput(), new AvatarInput(), new AvatarInput(),
            };
            var phases = new[] { 0f, 0f, 0.25f, 0.5f, 0.75f, 0.25f, 0.75f, 0.3f, 0f, 0.3f, 0f, 0f, 0f, 0f, 0f, 0.25f, 0.5f, 0.75f, 0f, 0f, 0f, 0f };
            // Gestures, caught mid-way: reaching for a door handle, for something on the floor, and flinching from a hit.
            var gestures = new Action<AvatarRig, AvatarInput>[poses.Length];
            gestures[poses.Length - 3] = (rig, input) => { rig.Reach(-12f); for (int k = 0; k < 7; k++) rig.Animate(input, 0.05f); };
            gestures[poses.Length - 2] = (rig, input) => { rig.Reach(-65f); for (int k = 0; k < 7; k++) rig.Animate(input, 0.05f); };
            // The reach before it has ended.
            gestures[poses.Length - 1] = (rig,
                input) => { for (int k = 0; k < 10; k++) rig.Animate(input, 0.05f); rig.Flinch(); for (int k = 0; k < 3; k++) rig.Animate(input, 0.05f); };
            const int width = 240, height = 400;
            var cameraObject = new GameObject("NMM_PreviewCamera");
            var camera = cameraObject.AddComponent<Camera>();
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color(.09f, .1f, .12f);
            camera.fieldOfView = 40; camera.nearClipPlane = .05f; camera.farClipPlane = 20f;
            var lampObject = new GameObject("NMM_PreviewLight");
            var lamp = lampObject.AddComponent<Light>();
            lamp.type = LightType.Point; lamp.range = 12f; lamp.intensity = 2.2f;
            var target = new RenderTexture(width, height, 24);
            var output = new Texture2D(width * poses.Length, height * CharacterIds.Length, TextureFormat.RGB24, false);
            var prior = RenderTexture.active;
            var baked = new Mesh();
            try
            {
                camera.targetTexture = target;
                for (int row = 0; row < CharacterIds.Length; row++)
                {
                    var avatar = Create(-99, CharacterIds[row]);
                    var root = avatar.Root;
                    root.transform.position = new Vector3(-10000, -10000, -10000);
                    root.transform.rotation = Quaternion.identity;
                    // Skinning normally runs once per frame; bake each pose so all render in one frame.
                    var skin = avatar.Model.GetComponent<SkinnedMeshRenderer>();
                    var still = new GameObject("NMM_PreviewBaked");
                    still.transform.SetParent(avatar.Model, false);
                    still.transform.localScale = Vector3.one / ModelHeight; // BakeMesh already applies the model's scale.
                    still.AddComponent<MeshFilter>().sharedMesh = baked;
                    still.AddComponent<MeshRenderer>().sharedMaterial = avatar.Material;
                    if (skin) skin.enabled = false;
                    for (int i = 0; i < poses.Length; i++)
                    {
                        if (avatar.Rig != null) { avatar.Rig.Snap(poses[i], phases[i]); if (gestures[i] != null) gestures[i](avatar.Rig, poses[i]); }
                        if (skin) skin.BakeMesh(baked);
                        bool lying = poses[i].Pose == AvatarPose.Prone || poses[i].Pose == AvatarPose.Bathing || poses[i].Pose == AvatarPose.Dead;
                        bool crawling = poses[i].Pose == AvatarPose.Crawling || poses[i].Pose == AvatarPose.Downed;
                        Vector3 focus = root.transform.position + Vector3.up * (lying ? 0.25f : crawling ? 0.4f : 0.9f);
                        // Near side view shows the gait.
                        camera.transform.position = root.transform.position
                            + (lying ? new Vector3(2.4f, 2.4f, 1.2f) : crawling ? new Vector3(2.6f, 0.7f, 0.5f) : new Vector3(3.4f, 1.2f, 0.9f));
                        camera.transform.LookAt(focus);
                        lamp.transform.position = root.transform.position + new Vector3(1.5f, 3f, 2.5f);
                        camera.Render();
                        RenderTexture.active = target;
                        output.ReadPixels(new Rect(0, 0, width, height), i * width, (CharacterIds.Length - 1 - row) * height);
                    }
                    avatar.Root.SetActive(false); // Destroy is deferred; keep it out of the next row.
                    Destroy(avatar);
                }
                output.Apply();
                File.WriteAllBytes(path, ImageConversion.EncodeToPNG(output));
                log("Player model poses rendered to " + path);
            }
            finally
            {
                RenderTexture.active = prior; camera.targetTexture = null;
                UnityEngine.Object.Destroy(cameraObject); UnityEngine.Object.Destroy(lampObject);
                UnityEngine.Object.Destroy(output); target.Release(); UnityEngine.Object.Destroy(target); UnityEngine.Object.Destroy(baked);
            }
        }

        public void Dispose()
        {
            if (disposed) return;
            disposed = true;
            Clear();
            foreach (var model in models.Values)
            {
                if (model == null) continue;
                if (model.Mesh != null) UnityEngine.Object.Destroy(model.Mesh);
                if (model.Material != null) UnityEngine.Object.Destroy(model.Material);
                if (model.Texture != null) UnityEngine.Object.Destroy(model.Texture);
            }
            if (fallbackMaterial != null) UnityEngine.Object.Destroy(fallbackMaterial);
        }

        private Avatar Create(int id, string character)
        {
            character = ValidCharacter(character);
            var source = Model(character);
            var root = new GameObject("NMM_Player_" + id);
            UnityEngine.Object.DontDestroyOnLoad(root);
            // Untagged: original game FindGameObjectWithTag("Player") must keep finding its local player.
            int playerLayer = LayerMask.NameToLayer("Player");
            if (playerLayer >= 0) root.layer = playerLayer;
            var collider = root.AddComponent<CapsuleCollider>();
            collider.radius = 0.25f;
            collider.height = ModelHeight;
            collider.center = new Vector3(0f, ModelHeight * 0.5f, 0f);
            collider.isTrigger = true;
            var body = root.AddComponent<Rigidbody>();
            body.isKinematic = true;
            body.useGravity = false;
            var solidObject = new GameObject("NMM_Body");
            solidObject.transform.SetParent(root.transform, false);
            if (playerLayer >= 0) solidObject.layer = playerLayer;
            var solid = solidObject.AddComponent<CapsuleCollider>();
            solid.radius = 0.3f; solid.height = ModelHeight; solid.center = new Vector3(0f, ModelHeight * 0.5f, 0f);
            CapsuleCollider blocker = null;
            var pose = new GameObject("Body").transform;
            pose.SetParent(root.transform, false);
            var model = new GameObject("Character_" + character).transform;
            model.SetParent(pose, false);
            model.localScale = Vector3.one * ModelHeight;
            model.localRotation = Quaternion.Euler(0, 180, 0); // The models face -Z natively.
            var avatar = new Avatar { Id = id, Character = character, Root = root, Body = pose, Model = model,
                Collider = collider, Solid = solid, Blocker = blocker };
            if (source != null && source.Joints != null)
            {
                avatar.Material = Ps1(source.Material);
                var bones = new Transform[source.Joints.Length];
                for (int i = 0; i < bones.Length; i++) bones[i] = new GameObject(source.BoneNames[i]).transform;
                for (int i = 0; i < bones.Length; i++)
                {
                    int parent = source.BoneParents[i];
                    bones[i].SetParent(parent >= 0 ? bones[parent] : model, false);
                    bones[i].localPosition = parent >= 0 ? source.Joints[i] - source.Joints[parent] : source.Joints[i];
                    bones[i].localRotation = Quaternion.identity;
                }
                var skin = model.gameObject.AddComponent<SkinnedMeshRenderer>();
                skin.sharedMesh = source.Mesh;
                skin.bones = bones;
                int pelvis = Array.IndexOf(source.BoneNames, "pelvis");
                skin.rootBone = bones[pelvis < 0 ? 0 : pelvis];
                skin.sharedMaterial = avatar.Material;
                skin.updateWhenOffscreen = true;
                avatar.Rig = new AvatarRig(bones, source.BoneNames, pose, female: character == "secretary");
                avatar.Rig.RightPalm = source.RightPalm;
                avatar.Rig.HandCenter = source.HandCenter;
                log("Character " + character + ": right palm faces " + source.RightPalm + " (model axes); hand's middle "
                    + source.HandCenter.ToString("0.000"));
                avatar.Hand = avatar.Rig.Bone("hand_right");
            }
            else if (source != null)
            {
                avatar.Material = Ps1(source.Material);
                model.gameObject.AddComponent<MeshFilter>().sharedMesh = source.Mesh;
                model.gameObject.AddComponent<MeshRenderer>().sharedMaterial = avatar.Material;
            }
            else CreateFallback(model);
            var lightObject = new GameObject("CarriedLight");
            lightObject.transform.SetParent(root.transform, false);
            var carriedLight = lightObject.AddComponent<Light>();
            carriedLight.type = LightType.Spot;
            carriedLight.range = 9f;
            carriedLight.spotAngle = 55f;
            carriedLight.intensity = 1.2f;
            carriedLight.shadows = LightShadows.None;
            carriedLight.enabled = false;
            avatar.Light = carriedLight;
            if (id >= 0)
            {
                var steps = root.AddComponent<AudioSource>();
                steps.playOnAwake = false; steps.loop = false;
                steps.spatialBlend = 1f; steps.dopplerLevel = 0f;
                steps.rolloffMode = AudioRolloffMode.Linear; steps.minDistance = 1.5f; steps.maxDistance = 16f;
                avatar.Steps = steps;
                var mouth = new GameObject("NMM_Pain");
                mouth.transform.SetParent(root.transform, false);
                mouth.transform.localPosition = Vector3.up * 1.5f;
                var pain = mouth.AddComponent<AudioSource>();
                pain.playOnAwake = false; pain.spatialBlend = 1f; pain.dopplerLevel = 0f;
                pain.rolloffMode = AudioRolloffMode.Linear; pain.minDistance = 2f; pain.maxDistance = 30f;
                avatar.Pain = pain;
            }
            return avatar;
        }

        private void SetVisibility(Avatar avatar)
        {
            var frame = avatar.Frame;
            bool visible = frame != null && frame.body && !frame.spectating && string.Equals(frame.scene, SceneManager.GetActiveScene().name, StringComparison.Ordinal)
                && !(frame.bath && LocalBathing) // Everyone wakes in the same tub: shown once you are out of it.
                && !HideAll; // An ending everyone watches: nobody stands in the shot.
            if (avatar.Root.activeSelf != visible) avatar.Root.SetActive(visible);
        }

        private void UpdateHeldItem(Avatar avatar)
        {
            string id = avatar.Frame == null ? "" : avatar.Frame.equipped ?? "";
            if (id == avatar.Equipped) return;
            avatar.HoldsGun = false;
            if (avatar.Held) UnityEngine.Object.Destroy(avatar.Held);
            avatar.Held = null; avatar.Equipped = id;
            if (id.Length == 0 || ItemResolver == null) return;
            var item = ItemResolver(id);
            if (!item) { avatar.Equipped = null; return; }
            avatar.HeldTurn = Quaternion.Euler(item.HoldingRotation);
            avatar.HoldsGun = item is GrannyClone.RevolverInteractable || item.GetComponent<GrannyClone.RevolverInteractable>();
            avatar.HeldWithHead = item.HoldWith == GrannyClone.ItemInteractable.HoldTypes.Head;
            var holder = new GameObject("NMM_HeldItem");
            Transform anchor = avatar.Hand ? avatar.Hand : avatar.Root.transform;
            holder.transform.SetParent(anchor, false);
            holder.transform.localPosition = avatar.Hand ? new Vector3(0f, -0.04f, -0.03f) : new Vector3(.32f, 1.0f, .22f);
            avatar.Held = holder;
            foreach (var source in item.GetComponentsInChildren<MeshFilter>(true))
            {
                var renderer = source.GetComponent<MeshRenderer>();
                if (!renderer || !source.sharedMesh || source.name.IndexOf("Flare", StringComparison.OrdinalIgnoreCase) >= 0) continue;
                var part = new GameObject(source.name);
                part.transform.SetParent(holder.transform, false);
                part.transform.localPosition = item.transform.InverseTransformPoint(source.transform.position);
                part.transform.localRotation = Quaternion.Inverse(item.transform.rotation) * source.transform.rotation;
                Vector3 parentScale = item.transform.lossyScale, childScale = source.transform.lossyScale;
                part.transform.localScale = new Vector3(SafeRatio(childScale.x, parentScale.x),
                    SafeRatio(childScale.y, parentScale.y), SafeRatio(childScale.z, parentScale.z));
                part.AddComponent<MeshFilter>().sharedMesh = source.sharedMesh;
                part.AddComponent<MeshRenderer>().sharedMaterials = renderer.sharedMaterials;
                Vector3 size = source.sharedMesh.bounds.size;
                float thinnest = Mathf.Min(size.x, Mathf.Min(size.y, size.z)), widest = Mathf.Max(size.x, Mathf.Max(size.y, size.z));
                if (widest > 0f && thinnest < widest * 0.1f && source.sharedMesh.isReadable)
                {
                    var back = new GameObject(source.name + " (back)");
                    back.transform.SetParent(part.transform, false);
                    back.AddComponent<MeshFilter>().sharedMesh = BackFaces(source.sharedMesh);
                    back.AddComponent<MeshRenderer>().sharedMaterials = renderer.sharedMaterials;
                }
            }
            // The hand sits inside the scaled model; keep the item at its real-world size.
            Vector3 scale = item.InitialScale.sqrMagnitude > 0 ? item.InitialScale : Vector3.one;
            holder.transform.localScale = avatar.Hand ? scale / ModelHeight : scale;
            avatar.HeldGrip = GripOf(holder.transform, avatar.HeldTurn);
            avatar.HeldLong = Shape(holder.transform, out avatar.HeldCentre, out avatar.HeldLength, out avatar.HeldFace);
        }
        /// <summary>The held item in the palm, turning with the hand: the game holds it in front of the player's eyes turned
        /// by HoldingRotation, so here "in front" is where the fingers point and "up" is the thumb side (a radar's screen
        /// towards its holder, a hammer upright in a raised fist), and its grip sits in the palm.</summary>
        private static void HoldInHand(Avatar avatar)
        {
            Transform hand = avatar.Hand, held = avatar.Held.transform;
            // The rig's bones keep the model's axes: fingers along -Y, thumb along -Z (forward with the arm hanging),
            // the right palm facing +X.
            Vector3 fingers = -hand.up, palm = hand.rotation * (avatar.Rig != null ? avatar.Rig.RightPalm : Vector3.right);
            Vector3 thumb = Vector3.Cross(palm, fingers); // Fingers ahead and palm in: the thumb is up.
            if (thumb.sqrMagnitude < 1e-4f) return;
            Quaternion turn = Quaternion.LookRotation(fingers, thumb) * avatar.HeldTurn;
            Vector3 grip = avatar.Rig != null ? hand.TransformPoint(avatar.Rig.HandCenter) : hand.position + fingers * 0.05f; // In the hand.
            held.rotation = turn;
            held.position = grip - turn * Vector3.Scale(avatar.HeldGrip, held.lossyScale);
        }

        /// <summary>A board carried across the body in both hands, low on the right and high on the left, its flat side
        /// forward, in front of the chest; each hand takes it a hand's breadth either side of its middle.</summary>
        private static void CarryAcross(Avatar avatar, Transform root)
        {
            Transform held = avatar.Held.transform, chest = avatar.Rig.Bone("chest") ?? avatar.Rig.Bone("spine");
            if (!chest) return;
            Vector3 across = (-root.right * 0.8f + Vector3.up * 0.6f).normalized; // From the right hip up to the left shoulder.
            Quaternion carried = Quaternion.LookRotation(root.forward,
                across) * Quaternion.Inverse(Quaternion.LookRotation(avatar.HeldFace, avatar.HeldLength));
            Vector3 middle = chest.position + root.forward * 0.32f - Vector3.up * 0.12f;
            float w = avatar.TwoHands;
            Quaternion inHand = held.rotation;
            Vector3 inHandAt = held.position;
            held.rotation = Quaternion.Slerp(inHand, carried, w);
            held.position = Vector3.Lerp(inHandAt, middle - held.rotation * Vector3.Scale(avatar.HeldCentre, held.lossyScale), w);
            Vector3 along = held.rotation * avatar.HeldLength;
            avatar.Rig.HandTo(false, middle - along * 0.22f, w);
            avatar.Rig.HandTo(true, middle + along * 0.22f, w);

        }

        /// <summary>
        /// Where a hand closes on an item (its own units), as the game holds it: a fist's width up from its bottom (round a
        /// hammer's handle with its end showing below the fist, round a radar's lower half), not on the very end, where the
        /// item would seem to stand on the fist. Across the hand it sits on the palm side: the side towards the back of the
        /// hand just past the hand's middle, so a broad thing lies in the palm with the fingers round it instead of reaching
        /// out past the knuckles; a handle goes through the middle of the fist. Measured on the mesh's own corners: a tilted
        /// thing's box is mostly empty space, so its middle would leave the item floating beside the hand.
        /// </summary>
        private static Vector3 GripOf(Transform holder, Quaternion heldTurn)
        {
            var points = new List<Vector3>();
            foreach (var filter in holder.GetComponentsInChildren<MeshFilter>())
            {
                if (!filter.sharedMesh || filter.name.EndsWith("(back)", StringComparison.Ordinal)) continue;
                Transform part = filter.transform;
                IEnumerable<Vector3> corners;
                if (filter.sharedMesh.isReadable) corners = filter.sharedMesh.vertices;
                else
                {
                    Bounds b = filter.sharedMesh.bounds;
                    var box = new List<Vector3>();
                    for (int i = 0; i < 8; i++)
                        box.Add(b.center + Vector3.Scale(b.extents, new Vector3((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1)));
                    corners = box;
                }
                // As held.
                foreach (var corner in corners) points.Add(heldTurn * (part.localPosition + part.localRotation * Vector3.Scale(corner, part.localScale)));
            }
            if (points.Count == 0) return Vector3.zero;
            // As held: X towards the back of the hand, Y along the thumb, Z along the fingers (see HoldInHand).
            Vector3 size = holder.lossyScale;
            float across = (Mathf.Abs(size.x) + Mathf.Abs(size.y) + Mathf.Abs(size.z)) / 3f;
            float metre = across > 1e-6f ? 1f / across : 1f; // The item's own units in a metre.
            float bottom = points.Min(p => p.y), top = points.Max(p => p.y);
            float up = Mathf.Max(Mathf.Min((top - bottom) * 0.45f, 0.06f * metre), 0.005f * metre);
            var low = points.Where(p => p.y <= bottom + up * 2f).ToList(); // The part the fist closes round.
            float left = low.Min(p => p.x), right = low.Max(p => p.x);
            var seen = new Vector3(Mathf.Max((left + right) * 0.5f, right - 0.01f * metre), bottom + up,
                (low.Min(p => p.z) + low.Max(p => p.z)) * 0.5f);
            return Quaternion.Inverse(heldTurn) * seen;
        }

        /// <summary>The way a hand's palm faces, from its mesh: the hand is a flat paddle along the fingers (-Y), so its
        /// thinnest side-to-side axis (X or Z) is through the palm, and the fingertips curl towards the palm side.
        /// Models differ: some have the palms facing in at rest, some forward.</summary>
        /// <summary>
        /// The supplied models are weighted coarsely where the legs meet the body and at the knees (on Mike most of the hip
        /// follows only the pelvis or only a thigh): as a thigh swings forward, in a stride or a crouch, the buttock stays
        /// put and the thigh slides out of it as a flat slab, and the knee folds to a point. Here each hip and knee vertex's
        /// weights are spread over its neighbours' a few times, so the skin over the joint bends as one surface. Vertices at
        /// the same place (split along texture seams) are treated as one, so the mesh never opens up.
        /// </summary>
        internal const int JointSoftening = 3;
        internal const string KneeHelperLeft = "knee_mid.L", KneeHelperRight = "knee_mid.R";
        // How much of the skin at the knee follows the halfway joint: all of it squares off the back of the knee; half keeps
        // a soft crease.
        internal static float TestKneeHelperShare = 0.5f;

        /// <summary>
        /// A helper joint at each knee, on the thigh, that the rig turns halfway between the thigh and the shin each frame.
        /// Skin at a joint weighted between two bones is placed partway between where each would put it, and at a deep bend
        /// that cuts the corner: the back of the knee pinches to a point and the thigh just above it pokes out. The skin
        /// right at the knee follows the halfway joint instead, so it keeps its shape however far the knee bends.
        /// </summary>
        private static void AddKneeHelpers(ref string[] names, ref int[] parents, ref Vector3[] joints, ref Matrix4x4[] bindposes)
        {
            var addNames = new List<string>(names); var addParents = new List<int>(parents);
            var addJoints = new List<Vector3>(joints); var addBinds = new List<Matrix4x4>(bindposes);
            foreach (var leg in new[] { new[] { "thigh_left", "shin.L", KneeHelperLeft }, new[] { "thigh_right", "shin.R", KneeHelperRight } })
            {
                int thigh = Array.IndexOf(names, leg[0]), shin = Array.IndexOf(names, leg[1]);
                if (thigh < 0 || shin < 0 || Array.IndexOf(names, leg[2]) >= 0) continue;
                addNames.Add(leg[2]); addParents.Add(thigh); addJoints.Add(joints[shin]);
                addBinds.Add(Matrix4x4.Translate(-joints[shin]));
            }
            names = addNames.ToArray(); parents = addParents.ToArray(); joints = addJoints.ToArray(); bindposes = addBinds.ToArray();
        }
        private static BoneWeight[] SoftenJoints(Vector3[] vertices, int[] triangles, BoneWeight[] weights, string[] boneNames, Vector3[] joints0)
        {
            int count = vertices.Length, bones = boneNames.Length;
            var joints = new HashSet<int>();
            foreach (string name in new[] { "pelvis", "thigh_left", "thigh_right", "shin.L", "shin.R" })
            {
                int index = Array.IndexOf(boneNames, name);
                if (index >= 0) joints.Add(index);
            }
            if (joints.Count < 5 || triangles == null || weights.Length != count || JointSoftening <= 0 || !AvatarRig.TestHingeKnees) return weights;
            // One node per place.
            var nodeOf = new int[count];
            var places = new Dictionary<Vector3Int, int>();
            var firstVertex = new List<int>();
            for (int i = 0; i < count; i++)
            {
                var key = new Vector3Int(Mathf.RoundToInt(vertices[i].x * 10000f), Mathf.RoundToInt(vertices[i].y * 10000f),
                    Mathf.RoundToInt(vertices[i].z * 10000f));
                int node;
                if (!places.TryGetValue(key, out node)) { node = firstVertex.Count; places[key] = node; firstVertex.Add(i); }
                nodeOf[i] = node;
            }
            int nodes = firstVertex.Count;
            var neighbours = new HashSet<int>[nodes];
            for (int n = 0; n < nodes; n++) neighbours[n] = new HashSet<int>();
            for (int t = 0; t + 2 < triangles.Length; t += 3)
                for (int e = 0; e < 3; e++)
                {
                    int a = nodeOf[triangles[t + e]], b = nodeOf[triangles[t + (e + 1) % 3]];
                    if (a != b) { neighbours[a].Add(b); neighbours[b].Add(a); }
                }
            var w = new float[nodes, bones];
            var soften = new bool[nodes];
            for (int n = 0; n < nodes; n++)
            {
                var weight = weights[firstVertex[n]];
                w[n, weight.boneIndex0] += weight.weight0; w[n, weight.boneIndex1] += weight.weight1;
                w[n, weight.boneIndex2] += weight.weight2; w[n, weight.boneIndex3] += weight.weight3;
                int main = 0;
                for (int b = 1; b < bones; b++) if (w[n, b] > w[n, main]) main = b;
                soften[n] = joints.Contains(main); // The hips and legs; the waist and chest above keep theirs.
            }
            for (int pass = 0; pass < JointSoftening; pass++)
            {
                var next = (float[,])w.Clone();
                for (int n = 0; n < nodes; n++)
                {
                    if (!soften[n] || neighbours[n].Count == 0) continue;
                    for (int b = 0; b < bones; b++)
                    {
                        float sum = 0f;
                        foreach (int m in neighbours[n]) sum += w[m, b];
                        next[n, b] = 0.5f * w[n, b] + 0.5f * sum / neighbours[n].Count;
                    }
                }
                w = next;
            }
            // Each knee: from the thigh to the shin over a quarter of the thigh's length above and below the joint, as Mark's
            // and Maddy's are made (Mike's changed over one ring of vertices at the knee, and folded into a fin there).
            KneeRamp(vertices, nodeOf, firstVertex, w, soften, boneNames, joints0, "thigh_left", "shin.L", KneeHelperLeft);
            KneeRamp(vertices, nodeOf, firstVertex, w, soften, boneNames, joints0, "thigh_right", "shin.R", KneeHelperRight);
            var result = (BoneWeight[])weights.Clone();
            var order = new int[bones];
            for (int i = 0; i < count; i++)
            {
                int n = nodeOf[i];
                if (!soften[n]) continue;
                for (int b = 0; b < bones; b++) order[b] = b;
                Array.Sort(order, (x, y) => w[n, y].CompareTo(w[n, x]));
                float total = 0f;
                for (int k = 0; k < 4; k++) total += w[n, order[k]];
                if (total <= 1e-6f) continue;
                result[i] = new BoneWeight
                {
                    boneIndex0 = order[0], weight0 = w[n, order[0]] / total,
                    boneIndex1 = order[1], weight1 = w[n, order[1]] / total,
                    boneIndex2 = order[2], weight2 = w[n, order[2]] / total,
                    boneIndex3 = order[3], weight3 = w[n, order[3]] / total,
                };
            }
            return result;
        }

        private static void KneeRamp(Vector3[] vertices, int[] nodeOf, List<int> firstVertex, float[,] w, bool[] soften,
            string[] boneNames, Vector3[] joints, string thighName, string shinName, string helperName)
        {
            int thigh = Array.IndexOf(boneNames, thighName), shin = Array.IndexOf(boneNames, shinName), helper = Array.IndexOf(boneNames, helperName);
            if (thigh < 0 || shin < 0 || joints == null || thigh >= joints.Length || shin >= joints.Length) return;
            Vector3 hip = joints[thigh], knee = joints[shin], down = knee - hip;
            float length = down.magnitude, band = length * (helper >= 0 ? 0.3f : 0.25f);
            if (length < 1e-4f) return;
            down /= length;
            int bones = boneNames.Length;
            for (int n = 0; n < firstVertex.Count; n++)
            {
                float leg = w[n, thigh] + w[n, shin], all = 0f;
                for (int b = 0; b < bones; b++) all += w[n, b];
                if (all <= 1e-6f || leg < 0.9f * all) continue; // Mostly this leg's: not the hip or the other leg.
                float along = Vector3.Dot(vertices[firstVertex[n]] - knee, down); // Negative above the knee.
                if (Mathf.Abs(along) > band) continue;
                if (helper >= 0)
                {
                    // Part the halfway joint's at the knee (KneeHelperShare of it at the joint itself, fading out above and
                    // below), the rest from the thigh to the shin across the knee.
                    float a = along / band, share = Mathf.Clamp01(0.5f + a * 0.5f);
                    float middle = TestKneeHelperShare * (1f - Mathf.Abs(a)), rest = 1f - middle;
                    w[n, thigh] = leg * rest * (1f - share);
                    w[n, helper] = leg * middle;
                    w[n, shin] = leg * rest * share;
                }
                else
                {
                    float share = Mathf.Clamp01(0.5f + along / (2f * band));
                    w[n, thigh] = leg * (1f - share);
                    w[n, shin] = leg * share;
                }
                soften[n] = true;
            }
        }

        private static readonly Dictionary<Mesh, Mesh> backs = new Dictionary<Mesh, Mesh>();
        /// <summary>A sheet's other side: the same mesh with its triangles turned round and its normals reversed.</summary>
        private static Mesh BackFaces(Mesh front)
        {
            Mesh back;
            if (backs.TryGetValue(front, out back) && back) return back;
            back = UnityEngine.Object.Instantiate(front);
            back.name = front.name + " (back)";
            var normals = back.normals;
            for (int i = 0; i < normals.Length; i++) normals[i] = -normals[i];
            back.normals = normals;
            for (int m = 0; m < back.subMeshCount; m++)
            {
                var triangles = back.GetTriangles(m);
                for (int t = 0; t + 2 < triangles.Length; t += 3) { int x = triangles[t]; triangles[t] = triangles[t + 1]; triangles[t + 1] = x; }
                back.SetTriangles(triangles, m);
            }
            backs[front] = back;
            return back;
        }

        /// <summary>The middle of the right hand's mesh, palm, fingers and thumb (every corner that mostly follows the hand or
        /// a finger joint on it), from the hand's joint, in the model's axes.</summary>
        private static Vector3 HandCenterOf(Vector3[] vertices, BoneWeight[] weights, string[] names, int[] parents, Vector3[] joints)
        {
            int hand = Array.IndexOf(names, "hand_right");
            if (hand < 0) return new Vector3(0f, -0.05f, 0f);
            Vector3 sum = Vector3.zero; int count = 0;
            for (int i = 0; i < vertices.Length; i++)
            {
                int bone = Dominant(weights[i]);
                while (bone >= 0 && bone != hand) bone = parents[bone];
                if (bone != hand) continue;
                sum += vertices[i] - joints[hand]; count++;
            }
            return count < 8 ? new Vector3(0f, -0.05f, 0f) : sum / count;
        }

        private static int Dominant(BoneWeight w)
        {
            int bone = w.boneIndex0; float most = w.weight0;
            if (w.weight1 > most) { bone = w.boneIndex1; most = w.weight1; }
            if (w.weight2 > most) { bone = w.boneIndex2; most = w.weight2; }
            if (w.weight3 > most) { bone = w.boneIndex3; }
            return bone;
        }

        private static Vector3 PalmOf(Vector3[] vertices, BoneWeight[] weights, int hand, Vector3[] joints)
        {
            if (hand < 0) return Vector3.right;
            var points = new List<Vector3>();
            for (int i = 0; i < vertices.Length; i++)
                if (weights[i].boneIndex0 == hand && weights[i].weight0 > 0.6f) points.Add(vertices[i] - joints[hand]);
            if (points.Count < 8) return Vector3.right;
            Vector3 mean = Vector3.zero;
            foreach (var p in points) mean += p;
            mean /= points.Count;
            float spreadX = 0f, spreadZ = 0f;
            foreach (var p in points) { spreadX += (p.x - mean.x) * (p.x - mean.x); spreadZ += (p.z - mean.z) * (p.z - mean.z); }
            Vector3 axis = spreadX <= spreadZ ? Vector3.right : Vector3.forward;
            // The fingertips (the lowest fifth along the fingers) lean towards the palm.
            var tips = points.OrderBy(p => p.y).Take(Math.Max(2, points.Count / 5)).ToList();
            Vector3 tip = Vector3.zero;
            foreach (var p in tips) tip += p;
            tip /= tips.Count;
            float lean = Vector3.Dot(tip - mean, axis);
            if (Mathf.Abs(lean) < 0.002f) return axis == Vector3.right ? Vector3.right : Vector3.back; // Palms in, or forward.
            return lean > 0f ? axis : -axis;
        }

        /// <summary>An item's middle, longest axis and flattest side (its own axes); true if it is over 90 cm long (a board).</summary>
        private static bool Shape(Transform holder, out Vector3 centre, out Vector3 length, out Vector3 face)
        {
            centre = Vector3.zero; length = Vector3.up; face = Vector3.forward;
            bool any = false;
            Vector3 min = Vector3.zero, max = Vector3.zero;
            foreach (var filter in holder.GetComponentsInChildren<MeshFilter>())
            {
                if (!filter.sharedMesh || filter.name.EndsWith("(back)", StringComparison.Ordinal)) continue;
                Bounds b = filter.sharedMesh.bounds;
                Transform part = filter.transform;
                for (int i = 0; i < 8; i++)
                {
                    Vector3 corner = b.center + Vector3.Scale(b.extents, new Vector3((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1));
                    Vector3 p = part.localPosition + part.localRotation * Vector3.Scale(corner, part.localScale);
                    if (!any) { min = max = p; any = true; } else { min = Vector3.Min(min, p); max = Vector3.Max(max, p); }
                }
            }
            if (!any) return false;
            centre = (min + max) * 0.5f;
            Vector3 size = Vector3.Scale(max - min, holder.lossyScale);
            Vector3[] axes = { Vector3.right, Vector3.up, Vector3.forward };
            float[] extent = { size.x, size.y, size.z };
            int longest = 0, thinnest = 0;
            for (int i = 1; i < 3; i++) { if (extent[i] > extent[longest]) longest = i; if (extent[i] < extent[thinnest]) thinnest = i; }
            if (thinnest == longest) thinnest = (longest + 1) % 3;
            length = axes[longest]; face = axes[thinnest];
            return extent[longest] > 0.9f;
        }

        private static float SafeRatio(float value, float denominator) { return Mathf.Abs(denominator) < .00001f ? 1f : value / denominator; }

        /// <summary>Loads a character's embedded mesh, rig and texture once. Null if unavailable.</summary>
        private CharacterModel Model(string id)
        {
            CharacterModel cached;
            if (models.TryGetValue(id, out cached)) return cached;
            CharacterModel model = null;
            try { model = LoadModel(id); }
            catch (Exception ex) { log("Character " + id + " failed to load; using a visible fallback: " + ex.Message); }
            models[id] = model;
            return model;
        }

        private CharacterModel LoadModel(string id)
        {
            var assembly = typeof(RemotePlayers).Assembly;
            MeshData data;
            using (var stream = assembly.GetManifestResourceStream("NMM.Character." + id + ".Mesh"))
            {
                if (stream == null) throw new InvalidDataException("Embedded mesh is missing.");
                using (var reader = new StreamReader(stream)) data = JsonCodec.FromJson<MeshData>(reader.ReadToEnd());
            }
            if (data == null || data.vertices == null || data.vertices.Length % 3 != 0 || data.vertices.Length < 9
                || data.triangles == null || data.triangles.Length % 3 != 0) throw new InvalidDataException("Invalid mesh.");
            int count = data.vertices.Length / 3;
            if (data.normals == null || data.normals.Length != count * 3 || data.uv == null || data.uv.Length != count * 2)
                throw new InvalidDataException("Mesh attribute count does not match vertices.");
            var vertices = new Vector3[count];
            var normals = new Vector3[count];
            var uv = new Vector2[count];
            for (int i = 0; i < count; i++)
            {
                vertices[i] = new Vector3(data.vertices[i * 3], data.vertices[i * 3 + 1], data.vertices[i * 3 + 2]);
                normals[i] = new Vector3(data.normals[i * 3], data.normals[i * 3 + 1], data.normals[i * 3 + 2]);
                uv[i] = new Vector2(data.uv[i * 2], data.uv[i * 2 + 1]);
            }
            foreach (int triangle in data.triangles)
                if (triangle < 0 || triangle >= count) throw new InvalidDataException("Mesh triangle out of bounds.");
            var model = new CharacterModel();
            model.Mesh = new Mesh { name = id + " (supplied model)" };
            model.Mesh.vertices = vertices;
            model.Mesh.normals = normals;
            model.Mesh.uv = uv;
            model.Mesh.triangles = data.triangles;
            int bones = data.boneNames == null ? 0 : data.boneNames.Length;
            int stride = data.boneIndex == null || count == 0 ? 0 : data.boneIndex.Length / count;
            if (bones > 0 && data.boneParents != null && data.boneParents.Length == bones && data.bonePositions != null && data.bonePositions.Length == bones * 3
                && (stride == 2 || stride == 4) && data.boneIndex.Length == count * stride && data.boneWeight != null && data.boneWeight.Length == count * stride)
            {
                var positions = new Vector3[bones];
                var bindposes = new Matrix4x4[bones];
                for (int i = 0; i < bones; i++)
                {
                    if (data.boneParents[i] >= i) throw new InvalidDataException("Rig bones are not parent-first.");
                    positions[i] = new Vector3(data.bonePositions[i * 3], data.bonePositions[i * 3 + 1], data.bonePositions[i * 3 + 2]);
                    bindposes[i] = Matrix4x4.Translate(-positions[i]); // Axis-aligned bone frames at the joints.
                }
                var weights = new BoneWeight[count];
                for (int i = 0; i < count; i++)
                {
                    int b = i * stride;
                    for (int k = 0; k < stride; k++)
                        if (data.boneIndex[b + k] < 0 || data.boneIndex[b + k] >= bones) throw new InvalidDataException("Rig weight out of range.");
                    weights[i] = new BoneWeight
                    {
                        boneIndex0 = data.boneIndex[b], weight0 = data.boneWeight[b],
                        boneIndex1 = data.boneIndex[b + 1], weight1 = data.boneWeight[b + 1],
                        boneIndex2 = stride > 2 ? data.boneIndex[b + 2] : 0, weight2 = stride > 2 ? data.boneWeight[b + 2] : 0f,
                        boneIndex3 = stride > 3 ? data.boneIndex[b + 3] : 0, weight3 = stride > 3 ? data.boneWeight[b + 3] : 0f
                    };
                }
                string[] boneNames = data.boneNames; int[] boneParents = data.boneParents;
                if (AvatarRig.TestHingeKnees) AddKneeHelpers(ref boneNames, ref boneParents, ref positions, ref bindposes);
                weights = SoftenJoints(vertices, data.triangles, weights, boneNames, positions);
                model.RightPalm = PalmOf(vertices, weights, Array.IndexOf(data.boneNames, "hand_right"), positions);
                model.HandCenter = HandCenterOf(vertices, weights, boneNames, boneParents, positions);
                model.Mesh.boneWeights = weights;
                model.Mesh.bindposes = bindposes;
                model.BoneNames = boneNames; model.BoneParents = boneParents; model.Joints = positions;
            }
            model.Mesh.RecalculateBounds();
            model.Texture = new Texture2D(2, 2, TextureFormat.RGBA32, false) { name = id + " (supplied texture)", filterMode = FilterMode.Point };
            using (var stream = assembly.GetManifestResourceStream("NMM.Character." + id + ".Texture"))
            {
                if (stream == null) throw new InvalidDataException("Embedded texture is missing.");
                using (var bytes = new MemoryStream())
                {
                    stream.CopyTo(bytes);
                    if (!ImageConversion.LoadImage(model.Texture, bytes.ToArray())) throw new InvalidDataException("Invalid texture.");
                }
            }
            var shader = Shader.Find("Standard") ?? Shader.Find("Legacy Shaders/Diffuse") ?? Shader.Find("Unlit/Texture");
            if (shader == null) throw new InvalidOperationException("No supported material shader found.");
            model.Material = new Material(shader) { name = id + " material", mainTexture = model.Texture };
            if (model.Material.HasProperty("_Glossiness")) model.Material.SetFloat("_Glossiness", 0f);
            log("Loaded character " + id + ": " + count + " mesh corners, " + (data.triangles.Length / 3) + " triangles, "
                + bones + " bones (procedural animation).");
            return model;
        }

        private static Shader psx;
        /// <summary>A teammate's material drawn as the game draws everything else (the Nun, the rooms): its own PS1 shader,
        /// vertex-lit with the wobble of the era, the texture unfiltered. The plain Unity shader if the game's is not loaded.</summary>
        private static Material Ps1(Material source)
        {
            var copy = new Material(source);
            if (!psx) psx = Shader.Find("psx/vertexlit");
            if (!psx) return copy;
            copy.shader = psx;
            copy.mainTexture = source.mainTexture;
            if (copy.HasProperty("_Tint")) copy.SetColor("_Tint", Color.white);
            if (copy.HasProperty("_Color")) copy.SetColor("_Color", Color.white);
            return copy;
        }

        private void CreateFallback(Transform parent)
        {
            if (fallbackMaterial == null)
            {
                var shader = Shader.Find("Standard") ?? Shader.Find("Legacy Shaders/Diffuse") ?? Shader.Find("Unlit/Color");
                if (shader != null) fallbackMaterial = new Material(shader) { color = new Color(0.35f, 0.48f, 0.6f) };
            }
            FallbackPart(parent, "Body", PrimitiveType.Capsule, new Vector3(0f, 0.5f, 0f), new Vector3(0.22f, 0.24f, 0.15f));
            FallbackPart(parent, "Head", PrimitiveType.Sphere, new Vector3(0f, 0.9f, 0f), Vector3.one * 0.17f);
            FallbackPart(parent, "LeftLeg", PrimitiveType.Capsule, new Vector3(-0.075f, 0.19f, 0f), new Vector3(0.075f, 0.18f, 0.08f));
            FallbackPart(parent, "RightLeg", PrimitiveType.Capsule, new Vector3(0.075f, 0.19f, 0f), new Vector3(0.075f, 0.18f, 0.08f));
        }

        private void FallbackPart(Transform parent, string name, PrimitiveType primitive, Vector3 position, Vector3 scale)
        {
            var part = GameObject.CreatePrimitive(primitive);
            part.name = name;
            part.transform.SetParent(parent, false);
            part.transform.localPosition = position;
            part.transform.localScale = scale;
            var collider = part.GetComponent<Collider>();
            if (collider != null) { collider.enabled = false; UnityEngine.Object.Destroy(collider); }
            if (fallbackMaterial != null) part.GetComponent<Renderer>().sharedMaterial = fallbackMaterial;
        }

        private static bool Finite(float value) { return !float.IsNaN(value) && !float.IsInfinity(value); }
        private static bool Finite(Vector3 value) { return Finite(value.x) && Finite(value.y) && Finite(value.z); }
        private static bool Finite(Quaternion value) { return Finite(value.x) && Finite(value.y) && Finite(value.z) && Finite(value.w); }
        private static Quaternion Yaw(Quaternion rotation)
        {
            float magnitude = Mathf.Sqrt(rotation.x * rotation.x + rotation.y * rotation.y + rotation.z * rotation.z + rotation.w * rotation.w);
            if (magnitude < 0.001f) return Quaternion.identity;
            var normalized = new Quaternion(rotation.x / magnitude, rotation.y / magnitude, rotation.z / magnitude, rotation.w / magnitude);
            Vector3 forward = normalized * Vector3.forward; forward.y = 0f;
            return forward.sqrMagnitude < 0.0001f ? Quaternion.identity : Quaternion.LookRotation(forward.normalized, Vector3.up);
        }
    }
}
