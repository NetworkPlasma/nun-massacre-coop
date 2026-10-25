using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace NunMassacre.Multiplayer.Players
{
    internal enum AvatarPose { Standing, Prone, Downed, Kneeling, Bathing, Crawling, Dead }

    /// <summary>What the rig should show this frame, derived from a teammate's network frame.</summary>
    internal struct AvatarInput
    {
        public float ForwardSpeed;   // m/s along the facing direction (negative = backwards)
        public float SideSpeed;      // m/s to the right
        public bool Crouching;
        public AvatarPose Pose;
        public bool Holding;
        public float LookPitch;      // degrees, positive = looking up
        public float LookYaw;        // degrees the eyes are turned from the body's facing, positive = right
        public float Headroom;       // metres from the floor to whatever is over them (0 = plenty); a crouch ducks under it
        public bool Jumping;         // In the air from a jump.
        public float VerticalSpeed;  // m/s up (negative = falling).
        public bool Gun;             // The item held is the revolver.
        public float Lean;           // Peeking round a corner: -1 fully left .. 1 fully right.
    }

    /// <summary>
    /// Procedural animation for the supplied 25-bone rig (it has no clips). Bones use axis-aligned
    /// frames in the model's native space: +X is the character's left, +Y up, -Z forward. A positive
    /// rotation about +X swings a hanging limb forward and tips an upright bone backward.
    /// </summary>
    internal sealed class AvatarRig
    {
        private sealed class Joint
        {
            public Transform Bone;
            public Vector3 RestPosition;
            public Vector3 Angles;        // current, degrees about native X, Y, Z
            public Vector3 Target;
            public Vector3 Offset, OffsetTarget;
            public Vector3 Rest;          // Per-model correction to a common rest pose.
        }

        private readonly Dictionary<string, Joint> joints = new Dictionary<string, Joint>(StringComparer.Ordinal);
        private readonly Transform body;
        private float phase;
        private float breath;
        private Vector3 bodyAngles, bodyAnglesTarget, bodyOffset, bodyOffsetTarget;
        // Brief gestures over the pose: recoiling from a hit, reaching for something they use.
        private float flinch, reach, reachPitch;
        private const float ReachSeconds = 0.7f;
        /// <summary>A robed character (the Ghost): shorter strides so the robe stays closed, and it leans
        /// rather than folding its legs to crouch or kneel.</summary>
        private readonly bool robed;
        private static readonly string[] Legs = { "thigh_left", "thigh_right", "shin.L", "shin.R", "foot_left", "foot_right" };
        /// <summary>The crown's height in a plain crouch, for ducking under lower ceilings.</summary>
        private const float CrouchTop = 1.34f;

        internal AvatarRig(Transform[] bones, string[] names, Transform body, bool robed = false, bool female = false)
        {
            this.body = body;
            this.robed = robed;
            walkClip = female ? "NMM.Anim.WalkFemale" : "NMM.Anim.WalkMale";
            runClip = female ? "NMM.Anim.RunFemale" : "NMM.Anim.RunMale";
            jumpClip = female ? "NMM.Anim.JumpFemale" : "NMM.Anim.JumpMale";
            crouchClip = female ? "NMM.Anim.CrouchWalkFemale" : "NMM.Anim.CrouchWalk";
            for (int i = 0; i < bones.Length; i++)
                joints[names[i]] = new Joint { Bone = bones[i], RestPosition = bones[i].localPosition };
            // The crawl clip is sized to the legs: hip to knee to ankle at rest, in metres.
            Transform thigh = Bone("thigh_left"), shin = Bone("shin.L"), foot = Bone("foot_left");
            if (thigh && shin && foot) legLength = Vector3.Distance(thigh.position, shin.position) + Vector3.Distance(shin.position, foot.position);
            if (foot) ankleHeight = foot.position.y - (body.parent ? body.parent.position.y : body.position.y); // Standing at rest: ankle over the sole.
            // Each knee's hinge, at rest: the body's sideways axis, in the thigh's and the shin's own frames.
            Transform restRoot = body.parent ? body.parent : body;
            Hinge(0, "thigh_left", "shin.L", restRoot.right);
            Hinge(1, "shin.L", "foot_left", restRoot.right);
            Hinge(2, "thigh_right", "shin.R", restRoot.right);
            Hinge(3, "shin.R", "foot_right", restRoot.right);
            // Each foot flat as the model stands at rest, against the idle clip's flat-footed stance.
            Flat(0, "foot_left", true, restRoot);
            Flat(1, "foot_right", false, restRoot);
            // The tip of the toes and the back of the heel, on the sole under each ankle, in that foot's own frame (from the rest
            // pose: a foot about 22 cm long for these proportions).
            Transform rest = body.parent ? body.parent : body, footR = Bone("foot_right");
            float scale = legLength > 0.1f ? legLength / 0.85f : 1f;
            if (foot)
            {
                toeLeft = Quaternion.Inverse(foot.rotation) * (rest.forward * 0.16f * scale + Vector3.down * ankleHeight);
                heelLeft = Quaternion.Inverse(foot.rotation) * (-rest.forward * 0.05f * scale + Vector3.down * ankleHeight);
            }
            if (footR)
            {
                toeRight = Quaternion.Inverse(footR.rotation) * (rest.forward * 0.16f * scale + Vector3.down * ankleHeight);
                heelRight = Quaternion.Inverse(footR.rotation) * (-rest.forward * 0.05f * scale + Vector3.down * ankleHeight);
            }
            if (robed)
            {
                // A robe is wider than the shoulders: keep the model's own outward arm angle, barely wider.
                SpreadArm("upper_arm_left", 1f, 2f);
                SpreadArm("upper_arm_right", -1f, 2f);
                return;
            }
            // Models differ in rest pose (arms hanging vs an A-pose). Rotate each upper arm so it
            // hangs straight down before animating, measured from the elbow's position.
            HangArm("upper_arm_left", "forearm_left", 1f);
            HangArm("upper_arm_right", "forearm_right", -1f);
        }

        // ---------- Knees: a hinge ----------
        // A clip gives each thigh and shin only a direction. Aimed one by one, by the least turn each, nothing keeps the knee
        // a hinge: in a deep bend (a crouch, a long stride) the shin rolls against the thigh, and the skin at the knee and up
        // the thigh twists and bulges out. Set together instead, both bones turn about one knee axis: across the bend the
        // clip makes (the body's sideways axis when the leg is near straight), so the knee only folds.
        private readonly Vector3[] hingeAlong = new Vector3[4], hingeAxis = new Vector3[4]; // Thigh L, shin L, thigh R, shin R.
        private readonly bool[] hinged = new bool[4];
        internal static bool TestHingeKnees = true, TestFlatFeet = true; // Tests: off films the old way, for before and after.

        private void Hinge(int index, string bone, string child, Vector3 sideways)
        {
            Transform from = Bone(bone), to = Bone(child);
            if (!from || !to) return;
            Vector3 along = to.position - from.position;
            if (along.sqrMagnitude < 1e-6f) return;
            hingeAlong[index] = Quaternion.Inverse(from.rotation) * along.normalized;
            hingeAxis[index] = Quaternion.Inverse(from.rotation) * sideways.normalized;
            hinged[index] = Vector3.Cross(hingeAlong[index], hingeAxis[index]).sqrMagnitude > 0.25f; // Not along the bone.
        }

        /// <summary>A leg from the clip: thigh and shin along their directions, turned about one knee axis.</summary>
        private void Leg(bool l, Vector3 thighDirection, Vector3 shinDirection, Vector3 sideways, float weight)
        {
            int t = l ? 0 : 2, s = t + 1;
            string thighName = l ? "thigh_left" : "thigh_right", shinName = l ? "shin.L" : "shin.R", footName = l ? "foot_left" : "foot_right";
            Transform thigh = Bone(thighName), shin = Bone(shinName);
            if (!thigh || !shin || !hinged[t] || !hinged[s] || thighDirection.sqrMagnitude < 0.5f || shinDirection.sqrMagnitude < 0.5f)
            {
                Aim(thighName, shinName, thighDirection, weight);
                Aim(shinName, footName, shinDirection, weight);
                return;
            }
            Vector3 upper = thighDirection.normalized, lower = shinDirection.normalized;
            Vector3 bend = Vector3.Cross(upper, lower), straight = Vector3.ProjectOnPlane(sideways, upper);
            if (straight.sqrMagnitude < 1e-4f) straight = Vector3.ProjectOnPlane(Vector3.Cross(Vector3.up, upper), upper);
            if (Vector3.Dot(bend, sideways) < 0f) bend = -bend; // Same way round as at rest, even bent back a little.
            float folded = Mathf.Clamp01((bend.magnitude - 0.05f) / 0.2f);
            Vector3 axis = folded > 0f ? Vector3.Slerp(straight.normalized, bend.normalized, folded) : straight.normalized;
            thigh.rotation = Quaternion.Slerp(thigh.rotation, Frame(upper, axis) * Quaternion.Inverse(Frame(hingeAlong[t], hingeAxis[t])), weight);
            shin.rotation = Quaternion.Slerp(shin.rotation, Frame(lower, axis) * Quaternion.Inverse(Frame(hingeAlong[s], hingeAxis[s])), weight);
        }

        // ---------- Feet: flat when the clip's are ----------
        // A clip's foot is its direction from ankle to the ball of the foot, which slopes down about 40 degrees even standing
        // flat, and its sole. Laid straight onto the model's foot, that put everyone on tiptoe. Measured once instead: the
        // turn from the idle clip's flat foot to this model's foot at rest, applied to every pose the clip gives.
        private readonly Quaternion[] footFit = { Quaternion.identity, Quaternion.identity };

        private void Flat(int index, string bone, bool left, Transform restRoot)
        {
            Transform foot = Bone(bone);
            var idle = AnimClip.Get(IdleResource);
            if (!foot || idle == null || idle.frames == null || idle.frames.Length == 0) return;
            var frame = idle.frames[0];
            float[] t = left ? frame.toesL : frame.toesR, s = left ? frame.soleL : frame.soleR;
            if (t == null || s == null) return;
            Vector3 toes = new Vector3(t[0], t[1], t[2]), sole = new Vector3(s[0], s[1], s[2]);
            if (Vector3.Cross(toes, sole).sqrMagnitude < 0.01f) return;
            footFit[index] = Quaternion.Inverse(Quaternion.LookRotation(-toes, -sole)) * (Quaternion.Inverse(restRoot.rotation) * foot.rotation);
        }

        /// <summary>A frame along a bone with its hinge axis square to it.</summary>
        private static Quaternion Frame(Vector3 along, Vector3 axis)
        {
            Vector3 square = Vector3.ProjectOnPlane(axis, along);
            if (square.sqrMagnitude < 1e-6f) square = Vector3.ProjectOnPlane(Vector3.up, along);
            return Quaternion.LookRotation(along, square);
        }

        private void SpreadArm(string upper, float side, float degrees)
        {
            Joint arm;
            if (joints.TryGetValue(upper, out arm)) arm.Rest = new Vector3(0f, 0f, degrees * side);
        }

        private void HangArm(string upper, string lower, float side)
        {
            Joint arm, elbow;
            if (!joints.TryGetValue(upper, out arm) || !joints.TryGetValue(lower, out elbow)) return;
            Vector3 down = elbow.RestPosition; // Elbow relative to the shoulder joint, in model axes.
            if (down.y >= -1e-4f) return;
            float outward = Mathf.Atan2(down.x * side, -down.y) * Mathf.Rad2Deg; // 0 = hanging straight down.
            arm.Rest = new Vector3(0f, 0f, -outward * side);
        }

        internal Transform Bone(string name) { Joint joint; return joints.TryGetValue(name, out joint) ? joint.Bone : null; }

        /// <summary>Hit: the body jolts back and the arms come up for a moment.</summary>
        /// <summary>Hit: standing, Mixamo's hit reaction on the upper body (the legs keep going); down or crawling, a flinch.</summary>
        internal void Flinch() { flinch = 1f; PlayOnce(HitResource, 1.15f, false); }
        /// <summary>They threw something: Mixamo's overarm throw, on the upper body.</summary>
        internal void Throw() { PlayOnce(ThrowResource, 1.25f, false); }

        /// <summary>Reaching for something they use (a door, an item): pitch is how far below (negative) or above
        /// the shoulder it is, in degrees.</summary>
        internal void Reach(float pitch) { Reach(pitch, false); }
        /// <summary>Their hand goes to something they used: low down, Mixamo's pick-up from the floor (the whole body bends);
        /// a door, cabinet or drawer, the opening pull; anything else, the reach to take it. Holding an item (the free hand
        /// reaches) or not standing: the hand-made reach.</summary>
        internal void Reach(float pitch, bool opening)
        {
            reach = ReachSeconds;
            reachPitch = Mathf.Clamp(pitch, -80f, 40f);
            reachOpening = opening;
            reachStart = true;
        }
        private bool reachOpening, reachStart;

        private void Gestures(AvatarInput input, float dt)
        {
            bool upright = input.Pose == AvatarPose.Standing;
            if (reachStart)
            {
                reachStart = false;
                if (upright && !input.Holding && !robed)
                {
                    bool played = reachPitch < -40f ? PlayOnce(PickUpResource, 2.6f, true) : reachOpening
                        ? PlayOnce(OpenResource, 1.5f, false) : PlayOnce(TakeResource, 1.6f, false);
                    if (played) reach = 0f; // The clip does it.
                }
            }
            if (upright && oneOff != null && oneOff.Resource == HitResource) flinch = 0f; // The clip's recoil instead.
            if (flinch > 0f)
            {
                flinch = Mathf.Max(0f, flinch - dt * 2.5f);
                float f = Mathf.Sin(flinch * Mathf.PI * 0.5f); // Sharp in, eases out.
                if (upright || input.Pose == AvatarPose.Crawling || input.Pose == AvatarPose.Downed || input.Pose == AvatarPose.Kneeling)
                {
                    Add("spine", 10f * f, 0, 0); Add("chest", 12f * f, 0, 0); Add("neck", 10f * f, 0, 0); Add("head", 12f * f, 0, 0);
                    if (upright)
                    {
                        Add("upper_arm_left", 35f * f, 0, 10f * f);
                        Add("upper_arm_right", 35f * f, 0, -10f * f);
                        Add("forearm_left", 40f * f, 0, 0);
                        Add("forearm_right", 40f * f, 0, 0);
                    }
                }
            }
            if (reach > 0f)
            {
                reach = Mathf.Max(0f, reach - dt);
                if (!upright) return;
                float w = Mathf.Sin((1f - reach / ReachSeconds) * Mathf.PI); // Out and back.
                // The free hand reaches (the left one while the right holds an item), bending at the waist for low things.
                string upper = input.Holding ? "upper_arm_left" : "upper_arm_right", lower = input.Holding ? "forearm_left" : "forearm_right";
                float side = input.Holding ? 1f : -1f;
                float low = Mathf.Clamp01(-reachPitch / 70f);
                Blend(upper, new Vector3(Mathf.Clamp(90f + reachPitch + low * 25f, 20f, 140f), 0f, 6f * side), w);
                Blend(lower, new Vector3(12f, 0f, 0f), w);
                Add("spine", -38f * low * w, 0, 0); Add("chest", -14f * low * w, 0, 0);
                if (low > 0.3f)
                {
                    Add("thigh_left", 45f * low * w, 0, 2f);
                    Add("thigh_right", 40f * low * w, 0, -2f);
                    Add("shin.L", -70f * low * w, 0, 0);
                    Add("shin.R", -62f * low * w, 0, 0);
                    Add("foot_left", 25f * low * w, 0, 0);
                    Add("foot_right", 22f * low * w, 0, 0);
                    Offset("pelvis", 0f, -0.17f * low * w, 0f);
                }
            }
        }

        private void Blend(string name, Vector3 target, float weight)
        {
            Joint joint;
            if (joints.TryGetValue(name, out joint)) joint.Target = Vector3.Lerp(joint.Target, target, weight);
        }

        internal void Animate(AvatarInput input, float deltaTime) { Animate(input, deltaTime, false); }

        private void Animate(AvatarInput input, float deltaTime, bool instant)
        {
            Pose(input, deltaTime, instant);
            KeepOffWalls(input, Mathf.Clamp(deltaTime, 0f, 0.1f), instant);
            FaceLook(input, Mathf.Clamp(deltaTime, 0f, 0.1f), instant);
            StepPose(Mathf.Clamp(deltaTime, 0f, 0.1f), instant);
            FeetOnFloor(input);
            KneeHelpers();
        }

        /// <summary>Each knee's helper joint halfway between the thigh and the shin (see RemotePlayers.AddKneeHelpers).</summary>
        private void KneeHelpers()
        {
            Halfway(RemotePlayers.KneeHelperLeft, "thigh_left", "shin.L");
            Halfway(RemotePlayers.KneeHelperRight, "thigh_right", "shin.R");
        }

        private void Halfway(string helper, string upper, string lower)
        {
            Transform middle = Bone(helper), from = Bone(upper), to = Bone(lower);
            if (middle && from && to) middle.rotation = Quaternion.Slerp(from.rotation, to.rotation, 0.5f);
        }

        // ---------- The soles on the floor ----------
        internal static bool TestFeetOnFloor = true;
        /// <summary>
        /// Last of all, every frame: no toe or heel of a standing (or crouched) teammate is left under the floor beneath it.
        /// The clips' feet are only as low as the flat floor at the player's spot, so a toe pointed down, a stair edge, a
        /// slope or the body moving on down a stair while the stepped pose holds (12 a second) could put a foot into it; such
        /// a foot comes up onto the floor (the knee bends), keeping its angle.
        /// </summary>
        private void FeetOnFloor(AvatarInput input)
        {
            if (!TestFeetOnFloor || input.Pose != AvatarPose.Standing || flying || legLength < 0.1f) return;
            Transform root = body.parent ? body.parent : body, pelvis = Bone("pelvis");
            float stuckLeft = FootOnFloor(true, root), stuckRight = FootOnFloor(false, root);
            // A leg bent as far as it goes on a steep stair, still in the step: the hips come up by what is left, the other
            // foot staying where it was.
            float stuck = Mathf.Max(stuckLeft, stuckRight);
            if (stuck <= 0.005f || !pelvis) return;
            Transform keep = Bone(stuckLeft >= stuckRight ? "foot_right" : "foot_left");
            Vector3 spot = keep ? keep.position : Vector3.zero;
            Quaternion turn = keep ? keep.rotation : Quaternion.identity;
            pelvis.position += Vector3.up * Mathf.Min(stuck, 0.12f);
            if (!keep) return;
            bool l = keep == Bone("foot_left");
            Reach(l ? "thigh_left" : "thigh_right", l ? "shin.L" : "shin.R", l ? "foot_left" : "foot_right", spot, root.forward);
            keep.rotation = turn;
        }

        /// <summary>One foot out of the floor: up onto it (the knee bending more, the foot keeping its angle), then tipped up off
        /// a stair's edge about the ankle if the toe or heel is still in it. How far it is still in, after that.</summary>
        private float FootOnFloor(bool l, Transform root)
        {
            Transform foot = Bone(l ? "foot_left" : "foot_right");
            if (!foot) return 0f;
            Quaternion sole = foot.rotation;
            Vector3 toe = sole * (l ? toeLeft : toeRight), heel = sole * (l ? heelLeft : heelRight);
            for (int pass = 0; pass < 4; pass++) // A raised toe can meet the next stair up: look again.
            {
                float need = Mathf.Max(Below(foot.position + toe), Below(foot.position + heel));
                if (need <= 0.005f) return 0f;
                Reach(l ? "thigh_left" : "thigh_right", l ? "shin.L" : "shin.R", l ? "foot_left" : "foot_right",
                    foot.position + Vector3.up * need, root.forward);
                foot.rotation = sole;
            }
            float toeIn = Below(foot.position + toe), heelIn = Below(foot.position + heel);
            if (Mathf.Max(toeIn, heelIn) <= 0.005f) return 0f;
            bool front = toeIn >= heelIn;
            float arm = Mathf.Max(0.05f, Vector3.ProjectOnPlane(front ? toe : heel, root.right).magnitude);
            float tip = Mathf.Min(40f, Mathf.Asin(Mathf.Clamp01((front ? toeIn : heelIn) / arm)) * Mathf.Rad2Deg + 3f);
            foot.rotation = Quaternion.AngleAxis(front ? -tip : tip, root.right) * sole;
            toe = foot.rotation * (l ? toeLeft : toeRight); heel = foot.rotation * (l ? heelLeft : heelRight);
            return Mathf.Max(Below(foot.position + toe), Below(foot.position + heel));
        }

        /// <summary>How far a point is under the floor beneath it (0 above it). A floor more than 25 cm over the point is the
        /// next stair's tread or a ledge it is under, not the floor it stands on.</summary>
        private static float Below(Vector3 point)
        {
            RaycastHit hit;
            if (!Physics.Raycast(point + Vector3.up * 0.3f, Vector3.down, out hit, 0.45f, SceneryMask(), QueryTriggerInteraction.Ignore)) return 0f;
            float depth = hit.point.y - point.y;
            return depth > 0f && depth < 0.25f ? depth : 0f;
        }

        internal bool Airborne { get { return flying; } } // Tests.
        private float Elbow(string side)
        {
            Transform upper = Bone("upper_arm_" + side), lower = Bone("forearm_" + side), hand = Bone("hand_" + side);
            return upper && lower && hand ? Vector3.Angle(lower.position - upper.position, hand.position - lower.position) : -1f;
        }
        /// <summary>Tests: the posture as shown: hip and crown heights over the spot, the back's and thighs' tilt.</summary>
        internal string PoseState
        {
            get
            {
                Transform root = body.parent ? body.parent : body, pelvis = Bone("pelvis"), chest = Bone("chest"),
                    neck = Bone("neck"), head = Bone("head"), thigh = Bone("thigh_left"), knee = Bone("shin.L"),
                    foot = Bone("foot_left");
                if (!pelvis || !neck || !head || !thigh || !knee || !foot || !chest) return "-";
                Func<Vector3, float> tilt = v => Mathf.Atan2(Vector3.Dot(v, root.forward), Vector3.Dot(v, Vector3.up)) * Mathf.Rad2Deg;
                float size = legLength / 0.8f;
                return "hips=" + (pelvis.position.y - root.position.y).ToString("0.00") + " crown=" + (head.position.y + 0.2f * size - root.position.y).ToString("0.00")
                    + " back=" + tilt(neck.position - pelvis.position).ToString("0") + " lower=" + tilt(chest.position - pelvis.position).ToString("0") + " upper=" + tilt(neck.position - chest.position).ToString("0")
                    + " thigh=" + tilt(knee.position - thigh.position).ToString("0") + " shin=" + tilt(foot.position - knee.position).ToString("0") + " drop=" + crouchDrop.ToString("0.00") + " lean=" + crouchLean.ToString("0") + " slope=" + crouchSlope.ToString("0") + " elbows=" + Elbow("left").ToString("0") + "/" + Elbow("right").ToString("0") + " walkW=" + walkState.Weight.ToString("0.00") + " idleW=" + idleState.Weight.ToString("0.00") + " leg=" + legLength.ToString("0.00");
            }
        }
        /// <summary>Tests: the deepest any toe or heel is under the floor now.</summary>
        internal float SoleDepth
        {
            get
            {
                Transform left = Bone("foot_left"), right = Bone("foot_right");
                float most = 0f;
                if (left) most = Mathf.Max(most, Mathf.Max(Below(left.position + left.rotation * toeLeft), Below(left.position + left.rotation * heelLeft)));
                if (right)
                    most = Mathf.Max(most, Mathf.Max(Below(right.position + right.rotation * toeRight), Below(right.position + right.rotation * heelRight)));
                return most;
            }
        }

        // The game's PS1 look for everything a teammate does, not only the clips: the whole pose (the hand-made poses, the
        // head turning to look, the arm on an item, a gesture) is shown in steps, twelve a second, never in between. The
        // body still moves over the floor smoothly; only its pose snaps.
        private Transform[] stepBones;
        private Quaternion[] heldRotations;
        private Vector3[] heldPositions;
        private float stepHold;
        private void StepPose(float dt, bool instant)
        {
            if (stepBones == null)
            {
                stepBones = joints.Values.Select(j => j.Bone).Where(b => b).Concat(new[] { body }).Distinct().ToArray();
                heldRotations = new Quaternion[stepBones.Length];
                heldPositions = new Vector3[stepBones.Length];
                instant = true;
            }
            stepHold += dt;
            if (instant || stepHold >= 1f / PoseRate)
            {
                stepHold = instant ? 0f : stepHold % (1f / PoseRate);
                for (int i = 0; i < stepBones.Length; i++) { heldRotations[i] = stepBones[i].localRotation; heldPositions[i] = stepBones[i].localPosition; }
                return;
            }
            for (int i = 0; i < stepBones.Length; i++) { stepBones[i].localRotation = heldRotations[i]; stepBones[i].localPosition = heldPositions[i]; }
        }

        private float lookWeight;
        /// <summary>The head looks where the player looks, whatever the body is doing (hunched in a crouch, on all fours in a
        /// vent, leaning into a run): what is still off after the pose, the neck takes some and the head the rest.</summary>
        private void FaceLook(AvatarInput input, float dt, bool instant)
        {
            Transform root = body.parent ? body.parent : body, neck = Bone("neck"), head = Bone("head");
            bool looking = head && input.Pose != AvatarPose.Dead && input.Pose != AvatarPose.Bathing;
            lookWeight = instant ? (looking ? 1f : 0f) : Mathf.MoveTowards(lookWeight, looking ? 1f : 0f, dt * 3f);
            if (!head || lookWeight < 0.001f) return;
            bool low = input.Pose == AvatarPose.Crawling || input.Pose == AvatarPose.Downed || input.Pose == AvatarPose.Prone;
            float lift = input.Pose == AvatarPose.Prone ? 15f : low ? 40f : 65f; // Under a bed the head has no room to come up.
            float yaw = Mathf.Clamp(input.LookYaw, -80f, 80f), pitch = Mathf.Clamp(input.LookPitch, low ? -40f : -65f, lift);
            Vector3 ahead = Vector3.ProjectOnPlane(root.forward, Vector3.up).normalized;
            if (ahead.sqrMagnitude < 0.5f) return;
            Vector3 want = Quaternion.AngleAxis(yaw, Vector3.up) * Quaternion.AngleAxis(-pitch, Vector3.Cross(Vector3.up, ahead)) * ahead;
            // The face is the head bone's -Z (the model's front).
            if (neck)
            {
                Quaternion part = Quaternion.Slerp(Quaternion.identity, Quaternion.FromToRotation(-head.forward, want), 0.4f * lookWeight);
                neck.rotation = part * neck.rotation;
            }
            head.rotation = Quaternion.Slerp(Quaternion.identity, Quaternion.FromToRotation(-head.forward, want), lookWeight) * head.rotation;
        }

        private void Pose(AvatarInput input, float deltaTime, bool instant)
        {
            float dt = Mathf.Clamp(deltaTime, 0f, 0.1f);
            foreach (var joint in joints.Values) { joint.Target = Vector3.zero; joint.OffsetTarget = Vector3.zero; }
            bodyAnglesTarget = Vector3.zero; bodyOffsetTarget = Vector3.zero;
            breath += dt * 1.6f;
            flatLeft = flatRight = 0f;
            switch (input.Pose)
            {
                case AvatarPose.Prone: Prone(input, dt); break;
                case AvatarPose.Downed: Crawl(input, dt, true); break;
                case AvatarPose.Kneeling: Kneel(input); break;
                case AvatarPose.Bathing: Bathe(input); break;
                case AvatarPose.Crawling: Crawl(input, dt, false); break;
                case AvatarPose.Dead: Dead(); break;
                default: Stand(input, dt); break;
            }
            if (robed) Robe(input);
            Gestures(input, dt);
            float blend = instant ? 1f : 1f - Mathf.Exp(-12f * dt);
            foreach (var joint in joints.Values)
            {
                joint.Angles = Vector3.Lerp(joint.Angles, joint.Target, blend);
                joint.Offset = Vector3.Lerp(joint.Offset, joint.OffsetTarget, blend);
                Vector3 angles = joint.Angles + joint.Rest;
                joint.Bone.localRotation = Quaternion.AngleAxis(angles.y, Vector3.up) * Quaternion.AngleAxis(angles.x,
                    Vector3.right) * Quaternion.AngleAxis(angles.z, Vector3.forward);
                joint.Bone.localPosition = joint.RestPosition + joint.Offset;
            }
            float bodyBlend = instant ? 1f : 1f - Mathf.Exp(-6f * dt);
            bodyAngles = Vector3.Lerp(bodyAngles, bodyAnglesTarget, bodyBlend);
            bodyOffset = Vector3.Lerp(bodyOffset, bodyOffsetTarget, bodyBlend);
            body.localRotation = Quaternion.Euler(bodyAngles);
            body.localPosition = bodyOffset;
            // Downed or in a vent: the crawl clip over the procedural pose, faded in and out. Under a bed: lying still on the
            // stomach, propped on the elbows (Mixamo's stomach pose with flat legs: scripts/make-prone.py), on the same
            // floor, ceiling and wall fitting as the crawl.
            // A body: lying as Mixamo's Falling Back Death ends, sprawled on the back.
            var crawlClip = robed || legLength < 0.1f ? null : AnimClip.Get(CrawlResource);
            var proneClip = robed || legLength < 0.1f ? null : AnimClip.Get(ProneResource);
            var deadClip = robed || legLength < 0.1f ? null : AnimClip.Get(DeadResource);
            bool prone = proneClip != null && input.Pose == AvatarPose.Prone, dead = deadClip != null && input.Pose == AvatarPose.Dead;
            bool crawling = crawlClip != null && (input.Pose == AvatarPose.Downed || input.Pose == AvatarPose.Crawling);
            if (prone || crawling || dead) lowMode = prone ? ClipMode.Prone : dead ? ClipMode.Dead : ClipMode.Crawl; // Fading out: still the one it was.
            bool low = crawling || prone || dead;
            crawlState.Weight = instant ? (low ? 1f : 0f) : Mathf.MoveTowards(crawlState.Weight, low ? 1f : 0f, dt * 4f);
            var lowClip = lowMode == ClipMode.Prone ? proneClip : lowMode == ClipMode.Dead ? deadClip : crawlClip;
            if (crawlState.Weight > 0f && lowClip != null)
            {
                oneOff = null;
                PlayClip(lowClip, crawlState, lowMode, input, dt, instant);
                GroundFeet(false, dt, instant);
                return;
            }
            crawlState.Hold = 1f; // Next time: straight to the current pose.
            // Crawling: a planted hand lies flat on the floor, palm down and fingers ahead, however the arm is angled.
            FlattenHand("hand_left", 1f, flatLeft, instant ? 1f : 1f - Mathf.Exp(-14f * dt));
            FlattenHand("hand_right", -1f, flatRight, instant ? 1f : 1f - Mathf.Exp(-14f * dt));
            // Walking and running: the character's walk clip from a slow walk up to a jog, its run clip beyond. Crouched:
            // the crouched walk, brought down to the game's crouch. Standing still keeps the hand-made idle poses.
            bool clips = legLength >= 0.1f, upright = input.Pose == AvatarPose.Standing;
            var walk = clips ? AnimClip.Get(walkClip) : null;
            var run = clips ? AnimClip.Get(runClip) : null;
            var sneak = clips && !robed ? AnimClip.Get(crouchClip) : null; // A robe crouches by leaning (Robe).
            float pace = Mathf.Sqrt(input.ForwardSpeed * input.ForwardSpeed + input.SideSpeed * input.SideSpeed);
            float moving = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.15f, 0.6f, pace)), fast = Mathf.InverseLerp(3.4f, 4.4f, pace);
            float stride = robed ? 0.6f : 1f; // A robe: shorter steps.
            // Standing still: breathing and shifting weight (Mixamo's Breathing Idle); crouched and still, its Crouching Idle.
            float still = 1f - moving;
            Blend(clips ? AnimClip.Get(IdleResource) : null, idleState, ClipMode.Idle,
                upright && !input.Crouching ? still * (robed ? 0.7f : 1f) : 0f, input, dt, instant);
            Blend(clips && !robed ? AnimClip.Get(CrouchIdleResource) : null, crouchIdleState, ClipMode.Idle,
                upright && input.Crouching ? still : 0f, input, dt, instant);
            Blend(walk, walkState, ClipMode.Walk, upright && !input.Crouching ? moving * (run != null ? 1f - fast : 1f) * stride : 0f, input, dt, instant);
            Blend(run, runState, ClipMode.Walk, upright && !input.Crouching ? fast * stride : 0f, input, dt, instant);
            // Crouched and moving: the crouched walk's legs and steps, with the hand-made crouch's straighter back and arms.
            Blend(sneak, sneakState, ClipMode.Sneak,
                upright && input.Crouching ? Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.1f, 0.5f, pace)) : 0f, input, dt,
                instant);
            UpdateOneOff(upright, input, dt, instant);
            float crouched = Mathf.Max(sneakState.Weight, crouchIdleState.Weight);
            if (crouched > 0.001f) CrouchDown(input, crouched, dt, instant);
            Jump(input, dt, instant);
            GroundFeet(upright && !flying, dt, instant);
            AimGun(input, dt, instant);
            GripHand(input, dt, instant);
            Gesture(input, dt);
            Peek(input, dt, instant);
        }

        // ---------- Peeking round a corner (Q and E) ----------
        private float lean, peekHand;
        private Vector3 peekGrip, peekWall; // Tests: where the hand last went, and the wall's facing there.
        private float peekTwist;            // Tests: how far the palm-on-the-wall pose would turn the hand about the forearm.
        private static readonly string[] PeekBones = { "spine", "chest", "neck", "head" };
        private static readonly float[] PeekRoll = { 12f, 14f, 10f, 8f }; // Degrees each, fully leant.
        /// <summary>
        /// The player peeks round a corner: the upper body and head lean out to that side over the hips (the game slides the
        /// eyes up to its peek distance sideways), and the hand on that side reaches round the corner they peek round and
        /// goes flat on its far side, at face height with the forearm upright beside the face (a wall beside them ending just
        /// ahead, or one ahead ending to that side); with no edge in reach, flat on the wall. A hand holding an item keeps
        /// hold of it.
        /// </summary>
        private void Peek(AvatarInput input, float dt, bool instant)
        {
            float target = input.Pose == AvatarPose.Standing && !flying ? Mathf.Clamp(input.Lean, -1f, 1f) : 0f;
            lean = instant ? target : Mathf.MoveTowards(lean, target, dt * 5f);
            float amount = Mathf.Abs(lean);
            if (amount < 0.02f) { peekHand = 0f; return; }
            Transform root = body.parent ? body.parent : body, pelvis = Bone("pelvis");
            Vector3 ahead = root.forward, side = root.right * Mathf.Sign(lean);
            // The walls are found from where the shoulder is standing straight: leant out, it may be past the corner already.
            Transform upright = Bone(lean < 0f ? "upper_arm_left" : "upper_arm_right");
            Vector3 from = upright ? upright.position : root.position + Vector3.up * 1.35f;
            if (pelvis) pelvis.position += side * 0.08f * amount; // The weight goes onto that foot.
            // Rolled about the facing direction (a positive angle tips the top to the left): the back bends out, the head
            // tilts round the corner.
            for (int i = 0; i < PeekBones.Length; i++)
            {
                Transform bone = Bone(PeekBones[i]);
                if (bone) bone.rotation = Quaternion.AngleAxis(-lean * PeekRoll[i], ahead) * bone.rotation;
            }
            // The right hand holding an item goes to the corner too, still holding it (a fist by the edge); the revolver
            // stays where it aims.
            bool left = lean < 0f, fist = !left && input.Holding;
            if (fist && input.Gun) { peekHand = 0f; return; }
            string arm = left ? "left" : "right";
            Transform shoulder = Bone("upper_arm_" + arm), head = Bone("head");
            if (!shoulder) return;
            int mask = SceneryMask();
            RaycastHit best = default(RaycastHit);
            bool found = false;
            // Beside them, diagonally ahead, and ahead (straight, and a little to the other side: a corner may be just ahead).
            foreach (var way in new[] { side, (side + ahead * 0.8f).normalized, (ahead + side * 0.35f).normalized, ahead, (ahead - side * 0.3f).normalized })
            {
                RaycastHit hit;
                // A wall facing the ray (not one grazed side-on, as a ray just catching a corner does).
                bool hitSomething = Physics.Raycast(from, way, out hit, 0.8f, mask, QueryTriggerInteraction.Ignore) && Vector3.Dot(hit.normal, -way) > 0.3f;
                if (hitSomething && (!found || hit.distance < best.distance))
                { best = hit; found = true; }
            }
            peekHand = instant ? (found ? 1f : 0f) : Mathf.MoveTowards(peekHand, found ? 1f : 0f, dt * 6f);
            if (!found || peekHand < 0.01f) return;
            // Along the wall towards the way they peek (forward for a wall beside them, sideways for one ahead): where it
            // ends is the corner. The hand reaches round it and goes flat on the far side: the side of the corner facing
            // where they look, or the back of a thin wall (a door's edge). With no edge in reach, flat on the near side.
            Vector3 normal = best.normal, grip = best.point;
            Vector3 along = Vector3.ProjectOnPlane(ahead + side, normal);
            along.y = 0f;
            if (along.sqrMagnitude > 0.05f)
            {
                along.Normalize();
                Func<float,
                    bool> wall = d => { RaycastHit probe; return Physics.Raycast(best.point + normal * 0.3f + along * d, -normal, out probe, 0.45f, mask, QueryTriggerInteraction.Ignore) && Vector3.Dot(probe.normal, normal) > 0.7f; };
                for (float d = 0.06f; d <= 0.6f; d += 0.06f)
                {
                    if (wall(d)) continue;
                    float lo = d - 0.06f, hi = d;
                    for (int i = 0; i < 4; i++) { float mid = (lo + hi) * 0.5f; if (wall(mid)) lo = mid; else hi = mid; }
                    Vector3 edge = best.point + along * lo;
                    RaycastHit far;
                    if (Physics.Raycast(edge + along * 0.3f - normal * 0.07f, -along, out far, 0.35f, mask, QueryTriggerInteraction.Ignore)
                        && Vector3.Dot(far.normal, along) > 0.6f)
                    { grip = far.point; normal = far.normal; }             // The corner's side.
                    else if (Physics.Raycast(edge - along * 0.07f - normal * 0.45f, normal, out far, 0.44f, mask, QueryTriggerInteraction.Ignore)
                        && Vector3.Dot(far.normal, -normal) > 0.6f)
                    { grip = far.point; normal = far.normal; }             // The back of a thin wall.
                    else grip = edge - along * 0.07f;                        // Just short of the edge.
                    break;
                }
            }
            // Beside the face: the wrist level with the chin, the fingers up to the eyes.
            float face = head ? head.position.y - 0.1f : shoulder.position.y + 0.2f;
            grip = new Vector3(grip.x, face, grip.z) + normal * (fist ? 0.07f : 0.03f);
            peekGrip = grip; peekWall = normal;
            float w = peekHand * amount;
            Transform hand = Bone("hand_" + arm);
            if (!hand) return;
            // The elbow hangs below the hand, so the forearm stands upright beside the face.
            Reach("upper_arm_" + arm, "forearm_" + arm, "hand_" + arm, Vector3.Lerp(hand.position, grip, w), Vector3.down + side * 0.25f + ahead * 0.2f);
            if (fist) return;
            Vector3 palm = -normal, fingers = Vector3.ProjectOnPlane(Vector3.up, palm);
            if (fingers.sqrMagnitude < 1e-4f) return;
            Vector3 palmAxis = left ? new Vector3(-RightPalm.x, RightPalm.y, RightPalm.z) : RightPalm;
            Quaternion flat = Quaternion.LookRotation(palm, -fingers.normalized) * Quaternion.Inverse(Quaternion.LookRotation(palmAxis, Vector3.up));
            // Flat on the wall without wringing the wrist: if the palm (as measured from this model's hand) would turn the
            // hand more than a wrist can about the forearm, the hand goes flat the other way round (Mark's curled hand).
            Transform forearm = Bone("forearm_" + arm);
            peekTwist = 0f;
            if (forearm)
            {
                Quaternion rel = Quaternion.Inverse(forearm.rotation) * flat;
                peekTwist = Mathf.Abs(Mathf.DeltaAngle(0f, 2f * Mathf.Atan2(rel.y, rel.w) * Mathf.Rad2Deg));
                if (peekTwist > 110f)
                    flat = Quaternion.LookRotation(palm, -fingers.normalized) * Quaternion.Inverse(Quaternion.LookRotation(-palmAxis, Vector3.up));
            }
            hand.rotation = Quaternion.Slerp(hand.rotation, flat, w);
        }
        /// <summary>Tests: how far the avatar leans and whether a hand is on a wall.</summary>
        internal string PeekState { get { return "lean=" + lean.ToString("0.00") + " hand=" + peekHand.ToString("0.00")
            + " grip=" + peekGrip.ToString("F2") + " wall=" + peekWall.ToString("F1") + " palm=" + RightPalm.ToString("F0")
            + " twist=" + peekTwist.ToString("0"); } }

        // ---------- Holding something ----------
        private float gripWeight;
        /// <summary>Which way this model's right palm faces, in its own axes (measured from the hand's mesh).</summary>
        internal Vector3 RightPalm = Vector3.right;
        /// <summary>The middle of the right hand's mesh (palm and fingers), in the model's own units and axes, from the hand's
        /// joint: where a held thing's grip goes, so it is always in the hand.</summary>
        internal Vector3 HandCenter = new Vector3(0f, -0.05f, 0f);
        /// <summary>The right hand closes on what it holds: the wrist straight along the forearm, the palm facing the body's
        /// middle and the thumb up, as a hand grips a handle (the item is then put in it: RemotePlayers.HoldInHand).</summary>
        private void GripHand(AvatarInput input, float dt, bool instant)
        {
            bool gripping = input.Holding && (input.Pose == AvatarPose.Standing || input.Pose == AvatarPose.Kneeling);
            gripWeight = instant ? (gripping ? 1f : 0f) : Mathf.MoveTowards(gripWeight, gripping ? 1f : 0f, dt * 6f);
            float w = gripWeight * (1f - gunWeight); // The revolver's two-handed grip comes from its own clip.
            Transform root = body.parent ? body.parent : body, hand = Bone("hand_right"), forearm = Bone("forearm_right");
            if (w < 0.001f || !hand || !forearm) return;
            Vector3 fingers = (hand.position - forearm.position).normalized;
            Vector3 palm = Vector3.ProjectOnPlane(-root.right, fingers); // The right palm faces left, towards the middle.
            if (palm.sqrMagnitude < 1e-4f) return;
            // The fingers are the bone's -Y; the palm is whichever axis this model's palm faces along.
            Quaternion grip = Quaternion.LookRotation(palm.normalized, -fingers) * Quaternion.Inverse(Quaternion.LookRotation(RightPalm, Vector3.up));
            hand.rotation = Quaternion.Slerp(hand.rotation, grip, w);
        }

        private void Blend(AnimClip clip, ClipState state, ClipMode mode, float weight, AvatarInput input, float dt, bool instant)
        {
            if (clip == null) weight = 0f;
            state.Weight = instant ? weight : Mathf.MoveTowards(state.Weight, weight, dt * 4f);
            if (state.Weight > 0.001f) PlayClip(clip, state, mode, input, dt, instant);
            else state.Hold = 1f; // Next time: straight to the current pose.
        }

        // ---------- The crouched walk, at the game's crouch height ----------
        private const string IdleResource = "NMM.Anim.Idle", CrouchIdleResource = "NMM.Anim.CrouchIdle";
        private readonly string crouchClip;
        private readonly ClipState idleState = new ClipState(), crouchIdleState = new ClipState();
        private const float SneakStep = 0.9f; // Crouched, the steps are this much of the clip's (the crouched walk is already short-stepped: make-crouch.py).
        private float crouchDrop, crouchLean, crouchSlope;
        /// <summary>
        /// The crouched clips are a sneak: hips down, the back leant out over the knees, the head up (scripts/make-crouch.py).
        /// On the game's crouch the head comes down to about the player's crouched eyes (lower under something low): half of
        /// that from the hips, never deeper than a crouch (64% of the leg over the floor; under something low, as deep as it
        /// takes), the rest from leaning further over (up to 25 degrees more). A tall character leans over rather than
        /// squatting on their heels; a short one hardly needs to do either. On stairs or a ramp the back comes up by part of
        /// the slope (the steps ahead are already higher, or lower), so a crouched climb is not on all fours.
        /// </summary>
        private void CrouchDown(AvatarInput input, float w, float dt, bool instant)
        {
            Transform root = body.parent ? body.parent : body, pelvis = Bone("pelvis"), spine = Bone("spine"),
                head = Bone("head"), left = Bone("foot_left"), right = Bone("foot_right");
            if (!pelvis || !spine || !head || !left || !right) return;
            float size = legLength / 0.8f;
            Vector3 crownPoint = head.position + Vector3.up * 0.2f * size;
            float crown = crownPoint.y - root.position.y, wanted = CrouchTop;
            bool low = input.Headroom > 0f && input.Headroom - 0.08f < wanted;
            if (low) wanted = input.Headroom - 0.08f;
            float over = Mathf.Clamp(crown - wanted, 0f, 0.6f);
            float hips = pelvis.position.y - root.position.y, deepest = legLength * (low ? 0.45f : 0.64f);
            float drop = Mathf.Clamp(over * 0.5f, 0f, Mathf.Max(0f, hips - deepest));
            // Leaning further: the crown, at a distance from the back's root and an angle from upright, comes down by
            // distance x (cos angle - cos(angle + lean)).
            Vector3 back = crownPoint - spine.position;
            float length = back.magnitude, angle = Mathf.Atan2(Vector3.Dot(back, root.forward), Vector3.Dot(back, Vector3.up));
            float rest = over - drop, lean = 0f;
            if (rest > 0.005f && length > 0.1f)
            {
                float most = 25f * Mathf.Deg2Rad;
                lean = Mathf.Clamp(Mathf.Acos(Mathf.Clamp(Mathf.Cos(angle) - rest / length, -1f, 1f)) - angle, 0f, most);
                rest -= length * (Mathf.Cos(angle) - Mathf.Cos(angle + lean));
                lean *= Mathf.Rad2Deg;
            }
            if (rest > 0.005f) drop = Mathf.Min(drop + rest, Mathf.Max(0f, hips - legLength * 0.45f)); // What leaning cannot take.
            // The slope underfoot: the floor half a step ahead against just behind.
            float front, rear, slope = 0f;
            if (FloorAt(root.position + root.forward * 0.45f, root, 0.6f, out front) && FloorAt(root.position - root.forward * 0.35f, root, 0.6f, out rear))
                slope = Mathf.Atan2(front - rear, 0.8f) * Mathf.Rad2Deg;
            float follow = instant ? 1f : 1f - Mathf.Exp(-10f * dt);
            crouchDrop = Mathf.Lerp(crouchDrop, drop, follow);
            crouchLean = Mathf.Lerp(crouchLean, lean, follow);
            crouchSlope = Mathf.Lerp(crouchSlope, Mathf.Min(Mathf.Abs(slope), 35f) * (slope > 0f ? 0.65f : 0.45f), instant ? 1f : 1f - Mathf.Exp(-6f * dt));
            if (w < 0.001f) return;
            // Short steps: each foot comes in towards under the hips (fore and aft), so the knees bend rather than lunge.
            Vector3 ahead = root.forward;
            Vector3 footL = left.position - ahead * Vector3.Dot(left.position - root.position, ahead) * (1f - SneakStep) * w;
            Vector3 footR = right.position - ahead * Vector3.Dot(right.position - root.position, ahead) * (1f - SneakStep) * w;
            Quaternion soleL = left.rotation, soleR = right.rotation;
            pelvis.position -= Vector3.up * crouchDrop * w;
            // The crouch clip tips the pelvis forward (about 28 degrees), which the hips' bend adds to: the thighs come up past
            // square to it, and the coarse skin there pulls into a slab. Most of that tip goes to the lower back instead (the
            // chest and head stay put): the hips bend about 20 degrees less, as in a real sneak.
            if (TestHingeKnees)
            {
                Vector3 pelvisUp = pelvis.up;
                float tipped = Mathf.Atan2(Vector3.Dot(pelvisUp, root.forward), Vector3.Dot(pelvisUp, Vector3.up)) * Mathf.Rad2Deg;
                if (tipped > 0f)
                {
                    float untip = Mathf.Min(tipped, 35f) * 0.75f * w;
                    pelvis.rotation = Quaternion.AngleAxis(-untip, root.right) * pelvis.rotation;
                    spine.rotation = Quaternion.AngleAxis(untip, root.right) * spine.rotation;
                }
            }
            spine.rotation = Quaternion.AngleAxis((crouchLean - crouchSlope) * w, root.right) * spine.rotation; // The head looks up again (FaceLook).
            Reach("thigh_left", "shin.L", "foot_left", footL, root.forward);
            Reach("thigh_right", "shin.R", "foot_right", footR, root.forward);
            left.rotation = soleL; right.rotation = soleR;
        }

        // ---------- Jumping ----------
        private readonly string runClip, jumpClip;
        private readonly ClipState runState = new ClipState(), sneakState = new ClipState(), jumpState = new ClipState();
        private bool flying;
        private float jumpRise = 1f, landing = -1f;
        /// <summary>A jump: in the air the clip follows the body's own rise and fall (the game moves it; the clip only poses it),
        /// from leaving the floor to the top and down to landing; on the floor again it plays the landing through.</summary>
        private void Jump(AvatarInput input, float dt, bool instant)
        {
            var clip = legLength >= 0.1f ? AnimClip.Get(jumpClip) : null;
            if (clip == null || input.Pose != AvatarPose.Standing)
            {
                flying = false;
                landing = -1f;
                Blend(null, jumpState, ClipMode.Jump, 0f, input, dt, instant);
                return;
            }
            int[] marks = clip.JumpMarks();
            if (input.Jumping)
            {
                if (!flying) jumpRise = 1f;
                flying = true; landing = -1f;
                jumpRise = Mathf.Max(jumpRise, input.VerticalSpeed);
            }
            else if (flying) { flying = false; landing = 0f; }
            float frame = -1f;
            if (flying)
            {
                float rise = Mathf.Clamp(input.VerticalSpeed / jumpRise, -1f, 1f);
                frame = rise >= 0f ? Mathf.Lerp(marks[1], marks[0], rise) : Mathf.Lerp(marks[1], marks[2], -rise);
            }
            else if (landing >= 0f)
            {
                landing += dt;
                frame = marks[2] + landing * clip.fps;
                if (frame >= marks[3]) landing = -1f;
            }
            if (frame >= 0f) jumpState.Phase = Mathf.Clamp01(frame / (clip.frames.Length - 1));
            float weight = flying || landing >= 0f ? 1f : 0f;
            jumpState.Weight = instant ? weight : Mathf.MoveTowards(jumpState.Weight, weight, dt * (weight > 0f ? 12f : 4f));
            if (jumpState.Weight > 0.001f) PlayClip(clip, jumpState, ClipMode.Jump, input, dt, instant);
            else jumpState.Hold = 1f;
        }

        // ---------- The revolver ----------
        private const string ShootResource = "NMM.Anim.Shoot";
        private const float RecoilSeconds = 0.8f;
        private float gunWeight, recoil = -1f, recoilShown, recoilHold = 1f;
        /// <summary>The teammate fired: the arms kick up and settle back.</summary>
        internal void Fire() { recoil = 0f; }

        // ---------- Gestures (number keys) ----------
        private const float PointSeconds = 1.6f, WhistleSeconds = 1.1f;
        private float pointing = -1f, whistling = -1f;
        /// <summary>They pointed: the right arm goes straight out to where they look, holds, comes back.</summary>
        internal void Point() { pointing = 0f; }
        /// <summary>They whistled: two fingers of the left hand go to the mouth.</summary>
        internal void Whistle() { whistling = 0f; }

        private void Gesture(AvatarInput input, float dt)
        {
            Transform root = body.parent ? body.parent : body;
            bool able = input.Pose == AvatarPose.Standing || input.Pose == AvatarPose.Kneeling;
            if (pointing >= 0f)
            {
                pointing += dt;
                if (pointing > PointSeconds) pointing = -1f;
                else if (able)
                {
                    float w = Mathf.Clamp01(pointing / 0.15f) * Mathf.Clamp01((PointSeconds - pointing) / 0.3f);
                    Transform shoulder = Bone("upper_arm_right"), forearm = Bone("forearm_right"), hand = Bone("hand_right");
                    if (shoulder && forearm && hand)
                    {
                        Vector3 ahead = Vector3.ProjectOnPlane(root.forward, Vector3.up).normalized;
                        Vector3 look = Quaternion.AngleAxis(Mathf.Clamp(input.LookYaw, -70f, 70f),
                            Vector3.up) * Quaternion.AngleAxis(-Mathf.Clamp(input.LookPitch, -45f, 60f),
                            Vector3.Cross(Vector3.up, ahead)) * ahead;
                        float arm = Vector3.Distance(shoulder.position, forearm.position) + Vector3.Distance(forearm.position, hand.position);
                        Vector3 at = shoulder.position + look * arm * 0.98f;
                        Reach("upper_arm_right", "forearm_right", "hand_right", Vector3.Lerp(hand.position, at, w), Vector3.down - root.right * 0.3f);
                        // The hand along the arm, palm down: a pointing hand.
                        Vector3 palm = Vector3.ProjectOnPlane(Vector3.down, look);
                        if (palm.sqrMagnitude > 1e-4f)
                        {
                            Quaternion flat = Quaternion.LookRotation(palm.normalized,
                                -look) * Quaternion.Inverse(Quaternion.LookRotation(RightPalm, Vector3.up));
                            hand.rotation = Quaternion.Slerp(hand.rotation, flat, w);
                        }
                    }
                }
            }
            if (whistling >= 0f)
            {
                whistling += dt;
                if (whistling > WhistleSeconds) whistling = -1f;
                else if (able && !Reaching)
                {
                    float w = Mathf.Clamp01(whistling / 0.15f) * Mathf.Clamp01((WhistleSeconds - whistling) / 0.25f);
                    Transform head = Bone("head"), hand = Bone("hand_left");
                    if (head && hand)
                    {
                        Vector3 face = -head.forward;
                        Vector3 mouth = head.position + face * 0.11f - Vector3.up * 0.02f;
                        Vector3 wrist = mouth + face * 0.03f - Vector3.up * 0.09f; // The fingers reach up to the lips.
                        Reach("upper_arm_left", "forearm_left", "hand_left", Vector3.Lerp(hand.position, wrist, w), Vector3.down - root.forward * 0.4f);
                    }
                }
            }
        }

        /// <summary>Reaching for something this moment (a door, an item): the free hand is busy.</summary>
        internal bool Reaching { get { return reach > 0f; } }

        /// <summary>A hand takes hold of something at this spot (both hands on a board): the arm reaches there, elbow out and
        /// down, and the hand closes on it, palm towards the body's middle. Applied after the pose, where the item is.</summary>
        internal void HandTo(bool left, Vector3 target, float weight)
        {
            string side = left ? "left" : "right";
            Transform root = body.parent ? body.parent : body, hand = Bone("hand_" + side), forearm = Bone("forearm_" + side);
            if (!hand || !forearm || weight < 0.001f) return;
            Vector3 outward = left ? -root.right : root.right;
            Reach("upper_arm_" + side, "forearm_" + side, "hand_" + side, Vector3.Lerp(hand.position, target, weight), outward - root.forward * 0.5f);
            Vector3 fingers = (hand.position - forearm.position).normalized;
            Vector3 palm = Vector3.ProjectOnPlane(-outward, fingers);
            if (palm.sqrMagnitude < 1e-4f) return;
            Vector3 palmAxis = left ? new Vector3(-RightPalm.x, RightPalm.y, RightPalm.z) : RightPalm; // The left is the right's mirror image.
            Quaternion grip = Quaternion.LookRotation(palm.normalized, -fingers) * Quaternion.Inverse(Quaternion.LookRotation(palmAxis, Vector3.up));
            hand.rotation = Quaternion.Slerp(hand.rotation, grip, weight);
        }

        /// <summary>Holding the revolver: held out in both hands where they look; each shot plays the clip's recoil.</summary>
        private void AimGun(AvatarInput input, float dt, bool instant)
        {
            var clip = legLength >= 0.1f ? AnimClip.Get(ShootResource) : null;
            bool aiming = clip != null && input.Gun && input.Holding && input.Pose == AvatarPose.Standing && reach <= 0f;
            gunWeight = instant ? (aiming ? 1f : 0f) : Mathf.MoveTowards(gunWeight, aiming ? 1f : 0f, dt * 5f);
            if (recoil >= 0f) { recoil += dt; if (recoil >= RecoilSeconds) recoil = -1f; }
            if (gunWeight < 0.001f) return;
            // Frames 6 to 30 are the shot (the kick at 12); frame 0 is the steady aim. Stepped like the other clips.
            float at = recoil >= 0f ? Mathf.Lerp(6f, 30f, recoil / RecoilSeconds) : 0f;
            recoilHold += dt;
            if (instant || recoilHold >= 1f / PoseRate || (recoil >= 0f && recoilShown == 0f)) { recoilHold = 0f; recoilShown = at; }
            int i = Mathf.Clamp((int)recoilShown, 0, clip.frames.Length - 1), j = Mathf.Min(i + 1, clip.frames.Length - 1);
            float t = recoilShown - i;
            ClipFrame a = clip.frames[i], b = clip.frames[j];
            Transform root = body.parent ? body.parent : body;
            Quaternion aim = root.rotation * Quaternion.AngleAxis(-Mathf.Clamp(input.LookPitch, -60f, 60f), Vector3.right), turn = root.rotation;
            Func<float[], float[], Vector3> lerp = (x, y) => Vector3.Lerp(new Vector3(x[0], x[1], x[2]), new Vector3(y[0], y[1], y[2]), t);
            Func<float[], float[], Vector3> along = (x, y) => (aim * lerp(x, y)).normalized;
            float w = gunWeight;
            Orient("chest", (turn * lerp(a.chestUp, b.chestUp)).normalized, (turn * lerp(a.chestLeft, b.chestLeft)).normalized, w * 0.3f);
            foreach (bool l in new[] { true, false })
            {
                string side = l ? "left" : "right";
                Aim("shoulder_" + side, "upper_arm_" + side, along(l ? a.shoulderL : a.shoulderR, l ? b.shoulderL : b.shoulderR), w);
                Aim("upper_arm_" + side, "forearm_" + side, along(l ? a.upperArmL : a.upperArmR, l ? b.upperArmL : b.upperArmR), w);
                Aim("forearm_" + side, "hand_" + side, along(l ? a.forearmL : a.forearmR, l ? b.forearmL : b.forearmR), w);
                Transform hand = Bone("hand_" + side);
                Vector3 fingers = along(l ? a.fingersL : a.fingersR, l ? b.fingersL : b.fingersR), palm = along(l ? a.palmL : a.palmR, l ? b.palmL : b.palmR);
                Vector3 x = l ? -palm : palm, y = -fingers;
                if (hand && Vector3.Cross(x, y).sqrMagnitude > 0.01f)
                    hand.rotation = Quaternion.Slerp(hand.rotation, Quaternion.LookRotation(Vector3.Cross(x, y), y), w);
            }
        }

        // How flat each hand lies this frame (0 = as posed), and the blended amount actually shown.
        private float flatLeft, flatRight, shownLeft, shownRight;
        private void FlattenHand(string name, float side, float target, float blend)
        {
            Joint joint;
            if (!joints.TryGetValue(name, out joint)) return;
            float shown = side > 0f ? (shownLeft = Mathf.Lerp(shownLeft, target, blend)) : (shownRight = Mathf.Lerp(shownRight, target, blend));
            if (shown < 0.01f) return;
            // The bones' rest frames are the model's own axes: fingers along -Y, palms facing in (-X on the left hand,
            // +X on the right). Turn the hand so the fingers point where the body faces and the palm faces the floor.
            Transform root = body.parent ? body.parent : body;
            Vector3 ahead = Vector3.ProjectOnPlane(root.forward, Vector3.up);
            if (ahead.sqrMagnitude < 0.01f) return;
            Vector3 x = side > 0f ? Vector3.up : Vector3.down, y = -ahead.normalized;
            Quaternion flat = Quaternion.LookRotation(Vector3.Cross(x, y), y);
            joint.Bone.rotation = Quaternion.Slerp(joint.Bone.rotation, flat, shown);
        }

        private void Stand(AvatarInput input, float dt)
        {
            float speed = Mathf.Sqrt(input.ForwardSpeed * input.ForwardSpeed + input.SideSpeed * input.SideSpeed);
            float walk = Mathf.Clamp01(speed / 1.2f);
            float run = Mathf.Clamp01((speed - 2.6f) / 1.8f);
            float stride = Mathf.Lerp(1.35f, 2.3f, run) * (input.Crouching ? 0.7f : 1f);
            float direction = input.ForwardSpeed < -0.2f ? -1f : 1f;
            phase = Mathf.Repeat(phase + direction * speed * dt / stride, 1f);
            float s = Mathf.Sin(phase * Mathf.PI * 2f);
            float swing = walk * Mathf.Lerp(24f, 42f, run);
            // Arms counter-swing the legs, trailing them slightly, and swing wider when running.
            float arm = Mathf.Sin(phase * Mathf.PI * 2f - 0.35f);
            float armSwing = walk * Mathf.Lerp(30f, 58f, run) * (input.Crouching ? 0.55f : 1f);
            float elbow = 8f + walk * Mathf.Lerp(8f, 45f, run);
            float elbowForward = armSwing * Mathf.Lerp(0.25f, 0.35f, run);
            float idleSway = (1f - walk) * Mathf.Sin(breath * 0.5f) * 2f;
            // Knees bend while each leg swings through.
            float kneeL = walk * Mathf.Lerp(30f, 75f, run) * Mathf.Max(0f, Mathf.Sin(phase * Mathf.PI * 2f + 1.3f));
            float kneeR = walk * Mathf.Lerp(30f, 75f, run) * Mathf.Max(0f, Mathf.Sin(phase * Mathf.PI * 2f + 1.3f + Mathf.PI));

            Set("thigh_left", s * swing, 0, 0); Set("thigh_right", -s * swing, 0, 0);
            Set("shin.L", -kneeL - 4f * walk, 0, 0); Set("shin.R", -kneeR - 4f * walk, 0, 0);
            Set("foot_left", kneeL * 0.25f, 0, 0); Set("foot_right", kneeR * 0.25f, 0, 0);
            Set("upper_arm_left", -arm * armSwing + idleSway, 0, 7f + 4f * run); Set("upper_arm_right", arm * armSwing + idleSway, 0, -7f - 4f * run);
            // Elbows bend further as each arm comes forward; hands follow loosely.
            Set("forearm_left", elbow + Mathf.Max(0f, -arm) * elbowForward, 0, 0); Set("forearm_right", elbow + Mathf.Max(0f, arm) * elbowForward, 0, 0);
            Set("hand_left", 6f - arm * 6f * walk, 0, 0); Set("hand_right", 6f + arm * 6f * walk, 0, 0);
            Set("spine", -(3f * walk + 12f * run), -s * 5f * walk, 0);
            Set("chest", Mathf.Sin(breath) * 1.2f, s * 8f * walk, 0);
            Offset("pelvis", 0f, -Mathf.Abs(Mathf.Cos(phase * Mathf.PI * 2f)) * 0.012f * walk, 0f);
            if (input.Crouching)
            {
                // Under something low (a vent, a table) the squat deepens, the back hunches and the head tucks by
                // however much it takes for the crown to stay under it.
                float duck = input.Headroom > 0f ? Mathf.Clamp01((CrouchTop + 0.05f - input.Headroom) / 0.4f) : 0f;
                Add("thigh_left", 70f + 20f * duck, 0, 2f); Add("thigh_right", 70f + 20f * duck, 0, -2f);
                Add("shin.L", -105f - 18f * duck, 0, 0); Add("shin.R", -105f - 18f * duck, 0, 0);
                Add("foot_left", 35f, 0, 0); Add("foot_right", 35f, 0, 0);
                Add("spine", -22f - 26f * duck, 0, 0); Add("chest", -8f - 14f * duck, 0, 0);
                Add("neck", -8f * duck, 0, 0); Add("head", -14f * duck, 0, 0);
                Offset("pelvis", 0f, -0.2f - 0.12f * duck, 0.02f + 0.04f * duck);
            }
            Look(input.LookPitch, input.LookYaw);
            if (input.Holding) HoldItem(walk * Mathf.Sin(phase * Mathf.PI * 4f));
        }

        private void Kneel(AvatarInput input)
        {
            // Reviving a teammate: one knee down, reaching forward.
            Set("thigh_left", 85f, 0, 4f); Set("shin.L", -95f, 0, 0); Set("foot_left", 10f, 0, 0);
            Set("thigh_right", 5f, 0, -4f); Set("shin.R", -100f, 0, 0); Set("foot_right", 60f, 0, 0);
            Set("spine", -25f, 0, 0); Set("chest", -12f + Mathf.Sin(breath * 3f) * 3f, 0, 0);
            Set("upper_arm_left", 55f, 0, 6f); Set("forearm_left", 30f, 0, 0);
            Set("upper_arm_right", 55f + Mathf.Sin(breath * 6f) * 6f, 0, -6f); Set("forearm_right", 30f, 0, 0);
            Offset("pelvis", 0f, -0.22f, 0f);
            Look(Mathf.Min(input.LookPitch, -25f), input.LookYaw);
        }

        private void Prone(AvatarInput input, float dt)
        {
            // Hiding under a bed: on the stomach, propped on the elbows with the hands under the chin and the
            // head up, turning to look around. Lying still, legs flat (the Ghost's; the others lie in the prone clip).
            const float s = 0f;
            float calm = Mathf.Sin(breath * 0.9f);
            bodyAnglesTarget = new Vector3(90f, 0f, 0f);
            bodyOffsetTarget = new Vector3(0f, 0.12f, -0.9f);
            Set("upper_arm_left", 100f + s * 12f, 0, 14f); Set("upper_arm_right", 100f - s * 12f, 0, -14f); // Elbows on the floor.
            Set("forearm_left", 75f, 0, 0); Set("forearm_right", 75f, 0, 0);                                 // Forearms ahead, hands near the chin.
            Set("hand_left", 10f, 0, 0); Set("hand_right", 10f, 0, 0);
            Set("spine", -6f, 0, 0); Set("chest", -8f + calm * 1.2f, 0, 0);                                   // Chest raised off the floor.
            Set("thigh_left", s * 10f, 0, 6f); Set("thigh_right", -s * 10f, 0, -6f);
            Set("shin.L", -8f, 0, 0); Set("shin.R", -12f, 0, 0);
            Set("foot_left", -40f, 0, 0); Set("foot_right", -40f, 0, 0);
            Set("neck", 25f, 0, 0); Set("head", 20f, 0, 0);
            Look(Mathf.Clamp(input.LookPitch, -20f, 30f), input.LookYaw);
        }

        private void Crawl(AvatarInput input, float dt, bool downed)
        {
            // Hands and knees. Each knee slides forward along the floor in turn, the lower leg folding under as it
            // comes through and lying flat again once planted; each hand lifts and reaches with the opposite knee.
            // Knocked down it is the same crawl, slower, head hanging and breathing hard.
            float speed = Mathf.Sqrt(input.ForwardSpeed * input.ForwardSpeed + input.SideSpeed * input.SideSpeed);
            float move = Mathf.Clamp01(speed / (downed ? 0.3f : 0.8f));
            float direction = input.ForwardSpeed < -0.2f ? -1f : 1f;
            phase = Mathf.Repeat(phase + direction * speed * dt / 0.6f, 1f);
            float a = phase * Mathf.PI * 2f, b = a + Mathf.PI; // The right knee moves with the left hand, the left knee with the right.
            float breathing = Mathf.Sin(breath * (downed ? 2.6f : 1.2f));
            if (robed)
            {
                // A robe on hands and knees reads as a box: the Ghost drags itself along on its arms instead, chest
                // up and the robe trailing flat behind.
                float s = Mathf.Sin(a) * move, lift = Mathf.Max(0f, Mathf.Sin(a)) * move, liftOther = Mathf.Max(0f, -Mathf.Sin(a)) * move;
                bodyAnglesTarget = new Vector3(78f, 0f, 0f);
                bodyOffsetTarget = new Vector3(0f, 0.05f, -1.48f);
                Set("thigh_left", 8f + s * 4f, 0, 3f); Set("thigh_right", 8f - s * 4f, 0, -3f);
                Set("shin.L", -12f, 0, 0); Set("shin.R", -12f, 0, 0);
                Set("upper_arm_left", 128f - s * 16f, 0, 10f); Set("upper_arm_right", 128f + s * 16f, 0, -10f);
                Set("forearm_left", 10f + liftOther * 25f, 0, 0); Set("forearm_right", 10f + lift * 25f, 0, 0);
                Set("hand_left", -60f, 0, 0); Set("hand_right", -60f, 0, 0);
                Set("spine", -4f + breathing * 1.5f, s * 6f, 0); Set("chest", -4f, -s * 5f, 0);
                Set("neck", downed ? 18f : 25f, 0, 0); Set("head", downed ? 12f : 20f, 0, 0);
                Look(Mathf.Clamp(input.LookPitch, -25f, 25f), input.LookYaw);
                return;
            }
            bodyAnglesTarget = new Vector3(90f, 0f, 0f);
            bodyOffsetTarget = new Vector3(0f, 0.5f, -1.45f); // Shoulders over the player's position, so the head stays out of walls ahead.
            CrawlLeg("thigh_right", "shin.R", "foot_right", a, move, -5f);
            CrawlLeg("thigh_left", "shin.L", "foot_left", b, move, 5f);
            CrawlArm("upper_arm_left", "forearm_left", "hand_left", a, move, 6f);
            CrawlArm("upper_arm_right", "forearm_right", "hand_right", b, move, -6f);
            float sway = Mathf.Sin(a) * move; // The weight shifts over whichever knee is planted.
            Set("spine", breathing * 1.5f, sway * 5f, 0);
            Set("chest", (downed ? -4f + breathing * 3f : 4f), -sway * 4f, sway * 3f);
            if (downed) { Set("neck", 24f, 0, 0); Set("head", 14f + Mathf.Sin(a * 2f) * 3f * move, 0, 0); } // Head hanging lower, nodding with the effort.
            else { Set("neck", 35f, 0, 0); Set("head", 28f, 0, 0); }                                            // Looking ahead, not at the floor.
            Look(Mathf.Clamp(input.LookPitch, -25f, 25f), input.LookYaw);
        }

        /// <summary>A crawling leg. 90 on the thigh puts the knee under the hip; the shin lies flat on the floor behind it
        /// whatever the thigh does (shin = -thigh), and folds under while the knee comes forward.</summary>
        private void CrawlLeg(string thigh, string shin, string foot, float angle, float move, float spread)
        {
            float through = Mathf.Max(0f, Mathf.Cos(angle)) * move; // Coming forward.
            float t = 90f + 18f * Mathf.Sin(angle) * move;
            Set(thigh, t, 0, spread);
            Set(shin, -t - 30f * through, 0, 0);
            Set(foot, -12f + 30f * through, 0, 0);
        }
        /// <summary>A crawling arm: planted straight down, lifting at the elbow and reaching as it comes forward.</summary>
        private void CrawlArm(string upper, string lower, string hand, float angle, float move, float spread)
        {
            float through = Mathf.Max(0f, Mathf.Cos(angle)) * move;
            Set(upper, 86f + 16f * Mathf.Sin(angle) * move, 0, spread);
            Set(lower, 8f + 32f * through, 0, 0);
            Set(hand, -40f + 25f * through, 0, 0); // Lifted, the hand hangs a little as it comes forward.
            float flat = 1f - Mathf.Clamp01(through * 1.6f); // Planted: flat on the floor.
            if (hand == "hand_left") flatLeft = flat; else flatRight = flat;
        }

        private void Bathe(AvatarInput input)
        {
            // The opening scene: lying back in the bathtub, head against the end, legs out under the water.
            // Reclined about the feet, then moved so the hips sit on the tub floor below the player's spot.
            // A robe drapes along the body rather than the legs: lie flatter so its hem stays in the tub.
            // The player's spot is 10 cm off the tub's centre line, towards their left.
            bodyAnglesTarget = new Vector3(robed ? -62f : -47f, 0f, 0f);
            bodyOffsetTarget = robed ? new Vector3(0.1f, 0.6f, 0.88f) : new Vector3(0.1f, 0.35f, 0.73f);
            float calm = Mathf.Sin(breath * 0.8f);
            // Knees up and toes raised so the feet stay inside the tub; hands in the lap, under the water.
            float knees = robed ? 0.38f : 1f; // Flatter on its back, a robe's legs need less lift.
            Set("thigh_left", 82f * knees, 0, 2f); Set("thigh_right", 78f * knees, 0, -2f);
            Set("shin.L", -34f * knees, 0, 0); Set("shin.R", -28f * knees, 0, 0);
            Set("foot_left", 45f, 0, 0); Set("foot_right", 45f, 0, 0);
            Set("upper_arm_left", 15f, 0, -6f); Set("upper_arm_right", 15f, 0, 6f);
            Set("forearm_left", 35f, 0, 0); Set("forearm_right", 35f, 0, 0);
            Set("chest", calm * 1.5f, 0, 0);
            Look(input.LookPitch, input.LookYaw); // Lying still; only the head turns to look around.
        }

        private void Dead()
        {
            // Out for good: on the back where they fell, limbs slack, head rolled to one side. No breath.
            bodyAnglesTarget = new Vector3(-90f, 0f, 0f);
            bodyOffsetTarget = new Vector3(0f, 0.14f, 0.85f);
            // On the back a limb swung forward (+X) lifts off the floor: arms and legs stay at or just below flat.
            Set("thigh_left", 8f, 0, 10f); Set("shin.L", -12f, 0, 0); Set("foot_left", -20f, 0, 0);
            Set("thigh_right", 2f, 0, -6f); Set("shin.R", -3f, 0, 0); Set("foot_right", -25f, 0, 0);
            Set("upper_arm_left", -6f, 0, 40f); Set("forearm_left", 6f, 0, 0); Set("hand_left", 0f, 0, 0);
            Set("upper_arm_right", -4f, 0, -55f); Set("forearm_right", 12f, 0, 0); Set("hand_right", 0f, 0, 0);
            Set("neck", -8f, 25f, 6f); Set("head", -6f, 22f, 8f);
        }

        private void Robe(AvatarInput input)
        {
            // Legs posed in full, robe or not.
            if (input.Pose == AvatarPose.Bathing || input.Pose == AvatarPose.Crawling || input.Pose == AvatarPose.Dead) return;
            bool lowered = input.Pose == AvatarPose.Kneeling || (input.Pose == AvatarPose.Standing && input.Crouching);
            float legScale = lowered ? 0.35f : 0.6f;
            foreach (string leg in Legs) { Joint joint; if (joints.TryGetValue(leg, out joint)) joint.Target *= legScale; }
            if (!lowered) return;
            Joint pelvis;
            if (joints.TryGetValue("pelvis", out pelvis)) pelvis.OffsetTarget = new Vector3(0f, -0.06f, 0f);
            bodyAnglesTarget = new Vector3(12f, 0f, 0f);
        }

        /// <summary>The item arm stays raised, bobbing with each step.</summary>
        private void HoldItem(float bob)
        {
            Set("upper_arm_right", 24f + bob * 4f, 0, -10f);
            Set("forearm_right", 66f - bob * 4f, 0, 0);
            Set("hand_right", bob * 3f, 0, 0);
        }

        private void Look(float pitch, float yaw)
        {
            pitch = Mathf.Clamp(pitch, -65f, 65f);
            yaw = Mathf.Clamp(yaw, -80f, 80f);
            Add("neck", pitch * 0.35f, yaw * 0.4f, 0);
            Add("head", pitch * 0.55f, yaw * 0.6f, 0);
        }

        private void Set(string name, float x, float y, float z) { Joint joint; if (joints.TryGetValue(name, out joint)) joint.Target = new Vector3(x, y, z); }
        private void Add(string name, float x, float y, float z) { Joint joint; if (joints.TryGetValue(name, out joint)) joint.Target += new Vector3(x, y, z); }
        private void Offset(string name, float x, float y, float z)
        {
            Joint joint;
            if (joints.TryGetValue(name, out joint)) joint.OffsetTarget += new Vector3(x, y, z);
        }

        // ---------- Motion-captured clips: the crawl (downed) and each character's walk ----------
        private const string CrawlResource = "NMM.Anim.Crawl";
        private readonly float legLength, ankleHeight;
        private readonly Vector3 toeLeft, heelLeft, toeRight, heelRight;
        private readonly string walkClip;
        /// <summary>A clip playing: how much it shows, where in its cycle it is, the pose on screen (held between steps), and
        /// how far the body is raised to rest on the floor.</summary>
        private sealed class ClipState { public float Weight, Phase, Shown, Hold = 1f, Lift; }
        private readonly ClipState crawlState = new ClipState(), walkState = new ClipState();
        private const float PoseRate = 12f; // Clip poses per second: stepped, no in-betweens, like the PS1 games this one copies.
        // Action: a one-off on the whole body; Overlay: a one-off on the upper body (the legs keep walking); Dead: a body.
        private enum ClipMode { Crawl, Walk, Sneak, Jump, Idle, Prone, Action, Overlay, Dead }
        private const string ProneResource = "NMM.Anim.Prone", DeadResource = "NMM.Anim.Dead";
        private const string HitResource = "NMM.Anim.Hit", ThrowResource = "NMM.Anim.Throw",
            OpenResource = "NMM.Anim.OpenDoor", TakeResource = "NMM.Anim.Take", PickUpResource = "NMM.Anim.PickUpLow";
        private ClipMode lowMode = ClipMode.Crawl;

        // ---------- One-off motions (Mixamo): a hit, a throw, opening, taking, picking up ----------
        private sealed class OneOff { public string Resource; public float Speed, Time; public bool FullBody; public readonly ClipState State = new ClipState(); }
        private OneOff oneOff;

        private bool PlayOnce(string resource, float speed, bool fullBody)
        {
            if (robed || legLength < 0.1f || AnimClip.Get(resource) == null) return false;
            oneOff = new OneOff { Resource = resource, Speed = speed, FullBody = fullBody };
            return true;
        }

        /// <summary>The one-off playing, start to end once, faded in and out; only while standing.</summary>
        private void UpdateOneOff(bool upright, AvatarInput input, float dt, bool instant)
        {
            if (oneOff == null) return;
            var clip = AnimClip.Get(oneOff.Resource);
            float length = clip != null ? (clip.frames.Length - 1) / clip.fps : 0f;
            oneOff.Time += dt * oneOff.Speed;
            if (!upright || clip == null || oneOff.Time >= length) { oneOff = null; return; }
            oneOff.State.Weight = Mathf.Clamp01(oneOff.Time / 0.12f) * Mathf.Clamp01((length - oneOff.Time) / 0.3f);
            oneOff.State.Phase = oneOff.Time / length;
            PlayClip(clip, oneOff.State, oneOff.FullBody ? ClipMode.Action : ClipMode.Overlay, input, dt, instant);
        }
        /// <summary>Tests: the one-off playing, if any.</summary>
        internal string OneOffPlaying { get { return oneOff == null ? null : oneOff.Resource; } }
        // The crawl fitted to where it is: slid back from a wall ahead, tilted with a sloping floor, and lowered under a low
        // ceiling (a duct). Where the hands and knees reach (ahead of and behind the player's spot) is where the slope is felt.
        private float clipBack, crawlDuck, crawlFront = 0.35f, crawlRear = -0.45f, crawlHalfWidth = 1f;
        private Quaternion crawlTilt = Quaternion.identity;

        /// <summary>Plays a clip over the procedural pose at this weight. The crawl keeps knees and hands on the floor and the
        /// head out of walls ahead; the walk keeps the feet on the floor and leaves the head (looking around) and an arm
        /// that holds something or reaches to the procedural pose.</summary>
        private void PlayClip(AnimClip clip, ClipState state, ClipMode mode, AvatarInput input, float dt, bool instant)
        {
            Transform root = body.parent ? body.parent : body;
            float size = legLength / clip.legLength, w = state.Weight;
            float speed = Mathf.Sqrt(input.ForwardSpeed * input.ForwardSpeed + input.SideSpeed * input.SideSpeed);
            if (!instant && mode == ClipMode.Idle) state.Phase = Mathf.Repeat(state.Phase + dt * clip.fps / (clip.frames.Length - 1), 1f); // Idles run on time.
            // A jump is timed by the body's rise and fall (Jump); prone and bodies are still; one-offs run on their own
            // time.
            else if (!instant && speed > 0.05f && mode != ClipMode.Jump && mode != ClipMode.Prone && mode != ClipMode.Dead
                && mode != ClipMode.Action && mode != ClipMode.Overlay)
            {
                float steps = mode == ClipMode.Sneak ? SneakStep : 1f; // A sneak's steps are shortened (CrouchDown).
                float rate = speed / Mathf.Max(0.2f, clip.cycleDistance * size * steps); // Feet, hands and knees keep pace with the ground...
                // ...up to a brisk step: a fast walker glides a little rather than scurries; so does a crawler going at
                // crouching pace in a vent.
                rate = Mathf.Min(rate, (mode == ClipMode.Crawl ? 2.5f : 1.9f) * clip.fps / (clip.frames.Length - 1));
                state.Phase = Mathf.Repeat(state.Phase + (input.ForwardSpeed < -0.2f ? -1f : 1f) * rate * dt, 1f);
            }
            // The game's PS1 look: the clip steps from pose to pose twelve times a second, no in-betweens (the body itself
            // still moves over the floor smoothly).
            state.Hold += dt;
            if (instant || state.Hold >= 1f / PoseRate) { state.Hold = 0f; state.Shown = state.Phase; }
            float at = state.Shown * (clip.frames.Length - 1);
            int i = Mathf.Clamp((int)at, 0, clip.frames.Length - 1), j = Mathf.Min(i + 1, clip.frames.Length - 1);
            ClipFrame a = clip.frames[i], b = clip.frames[j];
            float t = at - i;
            // Prone and a body: lying still, fitted to the floor as a crawl.
            bool crawl = mode == ClipMode.Crawl || mode == ClipMode.Prone || mode == ClipMode.Dead;
            Quaternion turn = root.rotation;
            float top = crawl ? Mathf.Clamp(RoomAbove(root) - 0.05f, 0.1f, 0.6f) : 0.6f; // Floors are looked for from under any ceiling.
            if (crawl)
            {
                // On a slope (a ramp, a sloping duct, stairs) the crawl tilts with the floor under the hands and the knees.
                // A ledge is no slope: half in a raised vent's mouth, the legs over the room's lower floor, it stays level.
                Vector3 forward = root.forward, side = root.right;
                float front, rear, toLeft, toRight, here;
                if (!FloorAt(root.position, root, top, out here)) here = 0f;
                bool ahead = FloorAt(root.position + forward * crawlFront, root, top, out front),
                    behind = FloorAt(root.position + forward * crawlRear, root, top, out rear);
                bool leftFloor = FloorAt(root.position - side * 0.22f, root, top, out toLeft),
                    rightFloor = FloorAt(root.position + side * 0.22f, root, top, out toRight);
                float pitch = Incline(ahead, front - here, crawlFront, behind, here - rear, -crawlRear, 35f);
                float roll = Incline(rightFloor, toRight - here, 0.22f, leftFloor, here - toLeft, 0.22f, 20f);
                Quaternion tilt = Quaternion.AngleAxis(-pitch, side) * Quaternion.AngleAxis(roll, forward);
                crawlTilt = instant ? tilt : Quaternion.Slerp(crawlTilt, tilt, 1f - Mathf.Exp(-8f * dt));
                turn = crawlTilt * root.rotation;
            }
            Func<float[], float[], Vector3> lerp = (x, y) => Vector3.Lerp(new Vector3(x[0], x[1], x[2]), new Vector3(y[0], y[1], y[2]), t);
            Func<float[], float[], Vector3> dir = (x, y) => (turn * lerp(x, y)).normalized;
            // In a narrow duct the elbows come in (they flare out about 30 cm to each side) so the arms stay inside it.
            float spread = crawl ? Mathf.Clamp01((crawlHalfWidth - 0.25f) / 0.2f) : 1f;
            Func<float[], float[], Vector3> armDir = spread > 0.999f ? dir : (x, y) => { Vector3 v = lerp(x, y); v.x *= spread; return (turn * v).normalized; };
            float lift = state.Lift, back = crawl ? clipBack : 0f;

            Transform pelvis = Bone("pelvis");
            if (!pelvis) return;
            Vector3 hips = lerp(a.hips, b.hips) * size;
            if (mode == ClipMode.Jump && flying) hips.y = clip.frames[0].hips[1] * size; // The game lifts the body itself.
            Vector3 up = dir(a.pelvisUp, b.pelvisUp), left = Vector3.ProjectOnPlane(dir(a.pelvisLeft, b.pelvisLeft), up).normalized;
            float torso = 1f; // A sneak: the crouched walk's own lean (scripts/make-crouch.py).
            bool overlay = mode == ClipMode.Overlay, oneOffMode = overlay || mode == ClipMode.Action;
            if (!overlay) // An upper-body one-off leaves the hips and legs to the walk.
            {
                pelvis.rotation = Quaternion.Slerp(pelvis.rotation, Quaternion.LookRotation(Vector3.Cross(left, up), up),
                    w * (mode == ClipMode.Sneak ? 0.9f : 1f));
                pelvis.position = Vector3.Lerp(pelvis.position, root.position + turn * new Vector3(hips.x, hips.y + lift, hips.z - back), w);
            }

            Aim("spine", "chest", dir(a.spine, b.spine), w * torso);
            Orient("chest", dir(a.chestUp, b.chestUp), dir(a.chestLeft, b.chestLeft), w * torso);
            if (crawl)
            {
                Aim("neck", "head", dir(a.neck, b.neck), w);
                Vector3 headUp = dir(a.headUp, b.headUp), face = dir(a.headFace, b.headFace);
                Orient("head", headUp, Vector3.Cross(face, headUp), w);
            }
            bool reaching = reach > 0f;
            bool sneak = false; // The crouched walk's own arms (calm ones: scripts/swap-arms.py gives it the walk's, elbows bent).
            Limbs("left", "L", a, b, dir, armDir, w, root, turn, crawl, !oneOffMode && (sneak || (!crawl && reaching && input.Holding)), !overlay);
            Limbs("right", "R", a, b, dir, armDir, w, root, turn, crawl, !oneOffMode && (sneak || (!crawl && (input.Holding || reaching))), !overlay);
            if (mode == ClipMode.Crawl) Duck(root, crawlDuck * w); // Prone is already flat: nothing to bring lower.

            // On the floor (each character's own proportions): the knee or hand sunk deepest into the floor under it when
            // crawling (a slope, a step), the lower foot when walking (each foot is then put on its own floor: GroundFeet).
            float sink = float.MinValue;
            if (crawl)
                foreach (var contact in CrawlContacts)
                {
                    Transform bone = Bone(contact);
                    float floor;
                    if (!bone) continue;
                    if (!FloorAt(bone.position, root, top, out floor)) floor = 0f;
                    sink = Mathf.Max(sink,
                        root.position.y + floor - (bone.position.y - (contact.StartsWith("shin") ? 0.07f : contact.StartsWith("foot") ? 0.03f : 0.035f)));
                }
            // In the air nothing rests on the floor; an upper-body one-off leaves the feet to the walk.
            else if (!(mode == ClipMode.Jump && flying) && !overlay)
                foreach (var contact in new[] { "foot_left", "foot_right" })
                {
                    Transform bone = Bone(contact);
                    if (bone) sink = Mathf.Max(sink, root.position.y - (bone.position.y - ankleHeight));
                }
            // Up out of the floor at once, down onto it smoothly.
            float settle = instant ? 1f : 1f - Mathf.Exp(-8f * dt);
            if (sink > float.MinValue) lift = sink > 0f ? lift + sink : Mathf.Lerp(lift, lift + sink, settle);
            state.Lift = lift;
            if (crawl)
            {
                // Where the hands and knees are, for the slope under them next time.
                Transform handL = Bone("hand_left"), handR = Bone("hand_right"), kneeL = Bone("shin.L"), kneeR = Bone("shin.R");
                float follow = instant ? 1f : 1f - Mathf.Exp(-2f * dt);
                if (handL && handR)
                    crawlFront = Mathf.Lerp(crawlFront,
                        Mathf.Clamp(Vector3.Dot((handL.position + handR.position) * 0.5f - root.position, root.forward), 0.1f, 0.8f),
                        follow);
                if (kneeL && kneeR)
                    crawlRear = Mathf.Lerp(crawlRear,
                        Mathf.Clamp(Vector3.Dot((kneeL.position + kneeR.position) * 0.5f - root.position, root.forward), -1f, -0.1f),
                        follow);
                // Under a low ceiling (a duct): lower until the back and head clear it, by however much they poke through now.
                float poke = mode == ClipMode.Prone || mode == ClipMode.Dead ? -1f : Poke(root, turn, size);
                crawlDuck = Mathf.Lerp(crawlDuck, Mathf.Clamp(crawlDuck + poke + 0.02f, 0f, 0.3f * size), instant ? 1f : 1f - Mathf.Exp(-10f * dt));
                // Nothing past what is ahead or behind: the head and hands stop short of a wall in front (the body slides
                // back), the feet short of one behind (forward again). With no room for both, it splits the difference.
                Vector3 along = turn * Vector3.forward, over = turn * Vector3.up;
                float reachAhead = 0f, reachBehind = 0f;
                foreach (var front in new[] { "head", "hand_left", "hand_right" })
                {
                    Transform bone = Bone(front);
                    if (bone) reachAhead = Mathf.Max(reachAhead, Vector3.Dot(bone.position - root.position, along) + (front == "head" ? 0.12f : 0.06f));
                }
                foreach (var rear in new[] { "foot_left", "foot_right", "shin.L", "shin.R" })
                {
                    Transform bone = Bone(rear);
                    if (bone) reachBehind = Mathf.Max(reachBehind, -Vector3.Dot(bone.position - root.position, along) + 0.08f);
                }
                float ahead = float.MaxValue, behind = float.MaxValue;
                for (int k = 0; k < 3; k++)
                {
                    ahead = Mathf.Min(ahead, Room(root.position + over * (0.3f + 0.15f * k), along, 1.5f) - 0.03f);   // Back and head height.
                    behind = Mathf.Min(behind, Room(root.position + over * (0.15f + 0.2f * k), -along, 1.5f) - 0.03f); // Feet, knees, hips.
                }
                // Walls are hard limits; keeping the head within 45 cm of the player's spot (the shoulders over it) is only
                // preferred, and gives way to a wall close behind (a duct's dead end, a corner).
                float unslidAhead = reachAhead + clipBack, unslidBehind = reachBehind - clipBack;
                float least = unslidAhead - ahead, most = behind - unslidBehind;
                float headAhead = mode == ClipMode.Prone ? 0.05f : 0.45f; // Under a bed the eyes are the player's own (the camera).
                float slide = least <= most ? Mathf.Clamp(Mathf.Max(0f, unslidAhead - headAhead), least, most) : (least + most) * 0.5f;
                clipBack = Mathf.Lerp(clipBack, Mathf.Clamp(slide, -0.75f, 0.8f), instant ? 1f : 1f - Mathf.Exp(-6f * dt));
            }
            // Out of the floor this frame (a still frame: settled at once).
            if (sink > float.MinValue && (instant || sink > 0f)) pelvis.position += Vector3.up * sink * w;
        }

        private static readonly string[] CrawlContacts = { "shin.L", "shin.R", "hand_left", "hand_right", "foot_left", "foot_right" };

        /// <summary>How steeply the floor rises (degrees) from the rise on each side of the spot: the average where they agree
        /// (a slope), the gentler one where they do not (a ledge or step on one side), none without a floor both ways.</summary>
        private static float Incline(bool hasA, float riseA, float runA, bool hasB, float riseB, float runB, float most)
        {
            if (!hasA || !hasB) return 0f;
            float a = Mathf.Atan2(riseA, Mathf.Max(0.05f, runA)) * Mathf.Rad2Deg, b = Mathf.Atan2(riseB, Mathf.Max(0.05f, runB)) * Mathf.Rad2Deg;
            float angle = Mathf.Abs(a - b) < 12f ? (a + b) * 0.5f : Mathf.Abs(a) < Mathf.Abs(b) ? a : b;
            return Mathf.Clamp(angle, -most, most);
        }

        /// <summary>Free distance from a point along a direction, up to <paramref name="most"/>.</summary>
        private static float Room(Vector3 from, Vector3 way, float most)
        {
            RaycastHit hit;
            return Physics.Raycast(from, way, out hit, most, SceneryMask(), QueryTriggerInteraction.Ignore) ? hit.distance : most;
        }

        // Off the walls: the player's own collision is far thinner than a body (10 cm across), so they can stand or crawl right
        // against a wall or a duct's side. The body moves off it (sideways; back from a wall ahead when upright) so shoulders,
        // arms and knees stay out of it, and keeps to the middle between two close walls.
        private Vector3 shove;
        private static readonly float[] AtSpot = { 0f };
        private static readonly KeyValuePair<string, float>[] Outermost =
        {
            new KeyValuePair<string, float>("upper_arm_left", 0.07f), new KeyValuePair<string, float>("upper_arm_right", 0.07f),
            new KeyValuePair<string, float>("forearm_left", 0.06f), new KeyValuePair<string, float>("forearm_right", 0.06f),
            new KeyValuePair<string, float>("hand_left", 0.06f), new KeyValuePair<string, float>("hand_right", 0.06f),
            new KeyValuePair<string, float>("shin.L", 0.07f), new KeyValuePair<string, float>("shin.R", 0.07f),
            new KeyValuePair<string, float>("head", 0.12f),
        };
        private void KeepOffWalls(AvatarInput input, float dt, bool instant)
        {
            Transform root = body.parent ? body.parent : body, pelvis = Bone("pelvis");
            bool low = crawlState.Weight > 0.5f, upright = input.Pose == AvatarPose.Standing || input.Pose == AvatarPose.Kneeling;
            Vector3 target = Vector3.zero;
            if (pelvis && (low || upright))
            {
                Vector3 side = root.right, ahead = root.forward;
                // As much room each way as the pose reaches out (elbows, hands, knees, head), plus their thickness.
                float right = 0.15f, left = 0.15f, front = 0.12f;
                foreach (var part in Outermost)
                {
                    Transform bone = Bone(part.Key);
                    if (!bone) continue;
                    Vector3 offset = bone.position - root.position;
                    right = Mathf.Max(right, Vector3.Dot(offset, side) + part.Value);
                    left = Mathf.Max(left, part.Value - Vector3.Dot(offset, side));
                    front = Mathf.Max(front, Vector3.Dot(offset, ahead) + part.Value);
                }
                // Crawling, along the body's length at the hands, the spot and the knees; upright, at the shoulders.
                float width;
                target = side * Shift(root, side, low ? new[] { crawlFront, 0f, crawlRear } : AtSpot, low ? 0.3f : 1.2f,
                    Mathf.Min(right, 0.45f), Mathf.Min(left, 0.45f), out width);
                if (upright) target += ahead * Shift(root, ahead, AtSpot, 1.2f, Mathf.Min(front, 0.35f), 0.15f, out width);
                else crawlHalfWidth = instant ? width * 0.5f : Mathf.Lerp(crawlHalfWidth, width * 0.5f, 1f - Mathf.Exp(-6f * dt));
            }
            if (!low) crawlHalfWidth = 1f;
            shove = instant ? target : Vector3.Lerp(shove, target, 1f - Mathf.Exp(-10f * dt));
            if (pelvis && shove.sqrMagnitude > 1e-6f) pelvis.position += shove;
        }

        /// <summary>How far to move along <paramref name="way"/> to have <paramref name="needAhead"/> free that way and
        /// <paramref name="needBehind"/> the other, at these points along the body (the middle when both cannot be had).</summary>
        private static float Shift(Transform root, Vector3 way, float[] along, float height, float needAhead, float needBehind, out float width)
        {
            float ahead = float.MaxValue, behind = float.MaxValue;
            foreach (float at in along)
            {
                Vector3 from = root.position + root.forward * at + Vector3.up * height;
                ahead = Mathf.Min(ahead, Room(from, way, 0.8f));
                behind = Mathf.Min(behind, Room(from, -way, 0.8f));
            }
            width = ahead + behind;
            float least = needBehind - behind, most = ahead - needAhead;
            return least <= most ? Mathf.Clamp(0f, least, most) : (least + most) * 0.5f;
        }

        /// <summary>The floor's height under a point, relative to the player's spot: looking down from <paramref name="top"/>
        /// over the spot's floor (under any ceiling there) to 0.6 m below it.</summary>
        private static bool FloorAt(Vector3 point, Transform root, float top, out float height)
        {
            RaycastHit hit;
            if (Physics.Raycast(new Vector3(point.x, root.position.y + top, point.z), Vector3.down, out hit, top + 0.6f,
                SceneryMask(), QueryTriggerInteraction.Ignore))
            {
                height = hit.point.y - root.position.y;
                return true;
            }
            height = 0f;
            return false;
        }

        /// <summary>Free height over the player's spot (up to 2 m).</summary>
        private static float RoomAbove(Transform root)
        {
            RaycastHit hit;
            return Physics.Raycast(root.position + Vector3.up * 0.05f, Vector3.up, out hit, 2f, SceneryMask(), QueryTriggerInteraction.Ignore)
                ? hit.distance + 0.05f : 2f;
        }

        /// <summary>How far the crawler's back or head pokes into the ceiling over it (negative: the room to spare).</summary>
        private float Poke(Transform root, Quaternion turn, float size)
        {
            float worst = -1f;
            Quaternion back = Quaternion.Inverse(turn);
            foreach (var part in new[] { "head", "chest", "spine", "pelvis" })
            {
                Transform bone = Bone(part);
                if (!bone) continue;
                // The top of it: the head is a ball over the head joint, the back a hand's breadth over the spine.
                Vector3 centre = part == "head" ? bone.position + bone.up * 0.1f * size : bone.position;
                float top = centre.y + (part == "head" ? 0.12f : part == "pelvis" ? 0.11f : 0.13f) * size;
                // Looking up from just over the floor under it (along the slope), so a head already through the ceiling still finds it.
                Vector3 local = back * (centre - root.position);
                RaycastHit hit;
                if (Physics.Raycast(root.position + turn * new Vector3(local.x, 0.15f, local.z), Vector3.up, out hit, 1.6f,
                    SceneryMask(), QueryTriggerInteraction.Ignore))
                    worst = Mathf.Max(worst, top - hit.point.y);
            }
            return worst;
        }

        /// <summary>The crawl brought lower (under a duct's ceiling): the body comes down while each hand and knee stays where
        /// it was on the floor, the elbows bending out and the hips folding.</summary>
        private void Duck(Transform root, float drop)
        {
            Transform pelvis = Bone("pelvis");
            if (!pelvis || drop < 0.003f) return;
            Transform handL = Bone("hand_left"), handR = Bone("hand_right"), kneeL = Bone("shin.L"), kneeR = Bone("shin.R"),
                footL = Bone("foot_left"), footR = Bone("foot_right");
            if (!handL || !handR || !kneeL || !kneeR || !footL || !footR) return;
            Vector3 wristL = handL.position, wristR = handR.position, atKneeL = kneeL.position, atKneeR = kneeR.position;
            Vector3 shinL = footL.position - kneeL.position, shinR = footR.position - kneeR.position;
            Quaternion palmL = handL.rotation, palmR = handR.rotation, soleL = footL.rotation, soleR = footR.rotation;
            pelvis.position -= Vector3.up * drop;
            Vector3 outward = root.right, behind = -root.forward;
            Reach("upper_arm_left", "forearm_left", "hand_left", wristL, behind - outward);
            Reach("upper_arm_right", "forearm_right", "hand_right", wristR, behind + outward);
            KneeAt("thigh_left", "shin.L", atKneeL, behind);
            KneeAt("thigh_right", "shin.R", atKneeR, behind);
            Aim("shin.L", "foot_left", shinL, 1f);
            Aim("shin.R", "foot_right", shinR, 1f);
            handL.rotation = palmL; handR.rotation = palmR; footL.rotation = soleL; footR.rotation = soleR;
        }

        /// <summary>A thigh swung so its knee is back down at this height, as near to where it was as the thigh's length allows.</summary>
        private void KneeAt(string thigh, string shin, Vector3 knee, Vector3 behind)
        {
            Transform hip = Bone(thigh), joint = Bone(shin);
            if (!hip || !joint) return;
            float length = Vector3.Distance(hip.position, joint.position), below = Mathf.Clamp(hip.position.y - knee.y, 0.05f, length);
            Vector3 away = Vector3.ProjectOnPlane(knee - hip.position, Vector3.up);
            if (away.sqrMagnitude < 1e-4f) away = behind;
            float across = Mathf.Sqrt(Mathf.Max(0f, length * length - below * below));
            Aim(thigh, shin, away.normalized * across - Vector3.up * below, 1f);
        }

        /// <summary>Two-bone reach: the upper and lower bone turned so the end joint lands on the target (or as near as the
        /// limb allows), bending towards <paramref name="pole"/> when it was straight.</summary>
        private void Reach(string upper, string lower, string end, Vector3 target, Vector3 pole)
        {
            Transform first = Bone(upper), middle = Bone(lower), tip = Bone(end);
            if (!first || !middle || !tip) return;
            Vector3 start = first.position, bend = middle.position;
            float a = Vector3.Distance(start, bend), b = Vector3.Distance(bend, tip.position);
            Vector3 toward = target - start;
            if (toward.sqrMagnitude < 1e-6f || a < 1e-4f || b < 1e-4f) return;
            float d = Mathf.Clamp(toward.magnitude, Mathf.Abs(a - b) + 0.01f, a + b - 0.001f);
            Vector3 n = toward.normalized;
            Vector3 side = Vector3.ProjectOnPlane(bend - start, n);
            if (side.sqrMagnitude < 1e-4f) side = Vector3.ProjectOnPlane(pole, n);
            if (side.sqrMagnitude < 1e-6f) return;
            float cos = Mathf.Clamp((a * a + d * d - b * b) / (2f * a * d), -1f, 1f);
            Vector3 elbow = start + n * (a * cos) + side.normalized * (a * Mathf.Sqrt(1f - cos * cos));
            first.rotation = Quaternion.FromToRotation(bend - start, elbow - start) * first.rotation;
            Vector3 reachNow = tip.position - middle.position, reachWanted = start + n * d - middle.position;
            if (reachNow.sqrMagnitude > 1e-8f && reachWanted.sqrMagnitude > 1e-8f)
                middle.rotation = Quaternion.FromToRotation(reachNow, reachWanted) * middle.rotation;
        }

        // Standing and walking: each foot on the floor under it (a slope, a stair), not where a flat floor would be.
        private float raiseLeft, raiseRight;
        private void GroundFeet(bool standing, float dt, bool instant)
        {
            Transform root = body.parent ? body.parent : body, pelvis = Bone("pelvis"), left = Bone("foot_left"), right = Bone("foot_right");
            float floorL = 0f, floorR = 0f;
            if (standing && pelvis && left && right && legLength > 0.1f)
            {
                float top = Mathf.Clamp(RoomAbove(root) - 0.05f, 0.1f, 0.6f);
                if (!FloorAt(left.position, root, top, out floorL)) floorL = 0f;
                if (!FloorAt(right.position, root, top, out floorR)) floorR = 0f;
            }
            // Up onto a higher step at once (a foot never sinks in), down onto a lower one smoothly.
            float settle = instant ? 1f : 1f - Mathf.Exp(-14f * dt);
            floorL = Mathf.Clamp(floorL, -0.4f, 0.4f); floorR = Mathf.Clamp(floorR, -0.4f, 0.4f);
            raiseLeft = floorL > raiseLeft ? floorL : Mathf.Lerp(raiseLeft, floorL, settle);
            raiseRight = floorR > raiseRight ? floorR : Mathf.Lerp(raiseRight, floorR, settle);
            if (!standing || !pelvis || !left || !right || (Mathf.Abs(raiseLeft) < 0.004f && Mathf.Abs(raiseRight) < 0.004f)) return;
            // The hips come down for a foot on lower ground (the other knee bends), never up; each foot then goes onto its floor.
            float drop = Mathf.Max(-0.3f, Mathf.Min(0f, Mathf.Min(raiseLeft, raiseRight)));
            pelvis.position += Vector3.up * drop;
            foreach (var foot in new[] { left, right })
            {
                bool l = foot == left;
                Quaternion sole = foot.rotation;
                Reach(l ? "thigh_left" : "thigh_right", l ? "shin.L" : "shin.R", l ? "foot_left" : "foot_right",
                    foot.position + Vector3.up * ((l ? raiseLeft : raiseRight) - drop), root.forward);
                foot.rotation = sole;
            }
        }

        private void Limbs(string side, string s, ClipFrame a, ClipFrame b, Func<float[], float[], Vector3> dir,
            Func<float[], float[], Vector3> armDir, float w, Transform root, Quaternion turn, bool crawl, bool keepArm,
            bool legs = true)
        {
            bool l = s == "L";
            if (!keepArm)
            {
                Aim("shoulder_" + side, "upper_arm_" + side, dir(l ? a.shoulderL : a.shoulderR, l ? b.shoulderL : b.shoulderR), w);
                Aim("upper_arm_" + side, "forearm_" + side, armDir(l ? a.upperArmL : a.upperArmR, l ? b.upperArmL : b.upperArmR), w);
                Aim("forearm_" + side, "hand_" + side, armDir(l ? a.forearmL : a.forearmR, l ? b.forearmL : b.forearmR), w);
            }
            if (legs)
            {
                if (TestHingeKnees)
                    Leg(l, dir(l ? a.thighL : a.thighR, l ? b.thighL : b.thighR), dir(l ? a.shinL : a.shinR, l ? b.shinL : b.shinR), root.right, w);
                else { Aim("thigh_" + side, "shin." + s, dir(l ? a.thighL : a.thighR, l ? b.thighL : b.thighR),
                    w); Aim("shin." + s, "foot_" + side, dir(l ? a.shinL : a.shinR, l ? b.shinL : b.shinR), w); }
                // Feet: toes along the clip's foot, sole as it turns (native -Z toes, -Y sole).
                Transform foot = Bone("foot_" + side);
                Vector3 toes = dir(l ? a.toesL : a.toesR, l ? b.toesL : b.toesR), sole = dir(l ? a.soleL : a.soleR, l ? b.soleL : b.soleR);
                if (foot && Vector3.Cross(toes, sole).sqrMagnitude > 0.01f)
                    foot.rotation = Quaternion.Slerp(foot.rotation,
                        Quaternion.LookRotation(-toes, -sole) * (TestFlatFeet ? footFit[l ? 0 : 1] : Quaternion.identity),
                        w);
            }
            if (keepArm) return;
            // Hands: fingers and palm from the clip (native -Y fingers, palms facing in); crawling, planted hands lie flat.
            Transform hand = Bone("hand_" + side);
            if (!hand) return;
            Vector3 fingers = dir(l ? a.fingersL : a.fingersR, l ? b.fingersL : b.fingersR), palm = dir(l ? a.palmL : a.palmR, l ? b.palmL : b.palmR);
            if (crawl)
            {
                // Flat on the floor, tilted with it on a slope.
                Vector3 floorUp = turn * Vector3.up;
                float planted = Mathf.Clamp01(1f - (Vector3.Dot(hand.position - root.position, floorUp) - 0.05f) / 0.12f);
                Vector3 ahead = Vector3.ProjectOnPlane(root.forward, floorUp).normalized;
                fingers = Vector3.Slerp(fingers, ahead, planted); palm = Vector3.Slerp(palm, -floorUp, planted);
            }
            Vector3 x = l ? -palm : palm, y = -fingers;
            if (Vector3.Cross(x, y).sqrMagnitude > 0.01f) hand.rotation = Quaternion.Slerp(hand.rotation, Quaternion.LookRotation(Vector3.Cross(x, y), y), w);
        }

        private static int sceneryMask;
        /// <summary>Walls and floors: everything but players, teammates and camera triggers.</summary>
        private static int SceneryMask()
        {
            if (sceneryMask != 0) return sceneryMask;
            sceneryMask = Physics.DefaultRaycastLayers;
            foreach (string layer in new[] { "Player", "CameraTrigger", "DontCollideEnvironment" })
            {
                int index = LayerMask.NameToLayer(layer);
                if (index >= 0) sceneryMask &= ~(1 << index);
            }
            return sceneryMask;
        }

        /// <summary>Swings a bone so it points (from its joint to its child's) along a direction.</summary>
        private void Aim(string bone, string child, Vector3 direction, float weight)
        {
            Transform from = Bone(bone), to = Bone(child);
            if (!from || !to || direction.sqrMagnitude < 0.5f) return;
            Vector3 now = to.position - from.position;
            if (now.sqrMagnitude < 1e-8f) return;
            from.rotation = Quaternion.Slerp(from.rotation, Quaternion.FromToRotation(now, direction) * from.rotation, weight);
        }

        /// <summary>Turns a bone fully: its native up (+Y) and left (+X) onto these directions.</summary>
        private void Orient(string bone, Vector3 up, Vector3 left, float weight)
        {
            Transform target = Bone(bone);
            if (!target || up.sqrMagnitude < 0.5f) return;
            left = Vector3.ProjectOnPlane(left, up);
            if (left.sqrMagnitude < 1e-4f) return;
            target.rotation = Quaternion.Slerp(target.rotation, Quaternion.LookRotation(Vector3.Cross(left.normalized, up), up), weight);
        }

        /// <summary>Pose instantly at a given gait phase (used for preview renders).</summary>
        internal void Snap(AvatarInput input, float gaitPhase)
        {
            phase = gaitPhase;
            crawlState.Phase = walkState.Phase = runState.Phase = sneakState.Phase = gaitPhase;
            Animate(input, 0f, true);
        }
    }
}
