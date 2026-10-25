using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using UnityEngine;

namespace NunMassacre.Multiplayer.Players
{
    /// <summary>
    /// Every game: the Nun's chase music and stingers (her 2D sounds, which in the game mean "she is after you") as loud as
    /// they should be for this player: full for the one she is hunting and for anyone near her, fading to a faint sting
    /// for the rest of the team further away (she is after someone, somewhere). On a guest, also her body's collider at
    /// the size it has on the host (only the host's Nun scripts size it), so guests' bullets can hit her.
    /// </summary>
    internal sealed class NunPresence
    {
        private const float Near = 10f, Far = 32f, Faint = 0.1f;
        private readonly List<HuntGain> gains = new List<HuntGain>();
        private Transform nun;
        private float nextScan;

        private CapsuleCollider body;
        internal string BodyInfo { get { return "nun=" + (nun ? nun.name : "-") + " body=" + (body ? body.name + " " + body.height.ToString("0.00") : "-"); } }
        internal void Reset() { gains.Clear(); nun = null; nextScan = 0f; body = null; motor = null; standingKnown = false; }

        /// <summary>The host's Nun's body collider: (height, centre height, radius); zero when there is none.</summary>
        internal static Vector3 BodyShape(Transform nunRoot)
        {
            var capsule = Capsule(nunRoot);
            return capsule ? new Vector3(capsule.height, capsule.center.y, capsule.radius) : Vector3.zero;
        }

        /// <summary>Guest: her body collider sized as on the host.</summary>
        internal void ApplyBody(Vector3 shape)
        {
            if (shape.x <= 0.01f || !nun) return;
            if (!body) body = Capsule(nun);
            if (!body) return;
            body.height = shape.x; body.center = new Vector3(body.center.x, shape.y, body.center.z); body.radius = shape.z;
            // Her movement script keeps restoring the standing size it read at the start (zero on a guest): give it the real one.
            if (!motor) motor = nun.GetComponentsInChildren<Component>(true).FirstOrDefault(c => c && c.GetType().FullName == "Humanoid.Motor2d");
            if (motor && MotorHeight != null && MotorCenter != null && !standingKnown && !Crouched())
            {
                MotorHeight.SetValue(motor, shape.x);
                MotorCenter.SetValue(motor, new Vector3(body.center.x, shape.y, body.center.z));
                standingKnown = true;
            }
        }
        private Component motor;
        private bool standingKnown;
        private static readonly System.Reflection.FieldInfo MotorHeight = HarmonyLib.AccessTools.Field(HarmonyLib.AccessTools.TypeByName("Humanoid.Motor2d"),
            "capsuleHeight");
        private static readonly System.Reflection.FieldInfo MotorCenter = HarmonyLib.AccessTools.Field(HarmonyLib.AccessTools.TypeByName("Humanoid.Motor2d"),
            "capsuleCenter");
        private static readonly System.Reflection.FieldInfo MotorCrouching = HarmonyLib.AccessTools.Field(HarmonyLib.AccessTools.TypeByName("Humanoid.Motor2d"),
            "isCrouching");
        private bool Crouched() { return motor && MotorCrouching != null && MotorCrouching.GetValue(motor) is bool && (bool)MotorCrouching.GetValue(motor); }

        private static CapsuleCollider Capsule(Transform nunRoot)
        {
            if (!nunRoot) return null;
            foreach (var capsule in nunRoot.GetComponentsInChildren<CapsuleCollider>(true))
                if (capsule && !capsule.isTrigger && capsule.CompareTag("Enemy")) return capsule;
            return null;
        }

        internal void Tick(Vector3 listener, bool hunted)
        {
            if (Time.unscaledTime >= nextScan) { nextScan = Time.unscaledTime + 3f; Scan(); }
            if (!nun) return;
            float gain = hunted ? 1f : Mathf.Lerp(1f, Faint, Mathf.InverseLerp(Near, Far, Vector3.Distance(listener, nun.position)));
            foreach (var g in gains) if (g) g.Target = gain;
        }

        /// <summary>Gain the Nun's 2D sounds have here now (tests).</summary>
        internal float Gain { get { foreach (var g in gains) if (g) return g.Target; return 1f; } }

        private void Scan()
        {
            var difficulty = Object.FindObjectOfType<DifficultyController>();
            var fsm = difficulty ? AccessTools.Field(typeof(DifficultyController), "NunFSM").GetValue(difficulty) as Component : null;
            if (!fsm) { nun = null; return; }
            nun = fsm.transform;
            Transform root = fsm.transform.parent ? fsm.transform.parent : fsm.transform;
            foreach (var source in root.GetComponentsInChildren<AudioSource>(true))
                if (source.spatialBlend < 0.01f && !source.GetComponent<HuntGain>()) gains.Add(source.gameObject.AddComponent<HuntGain>());
        }
    }

    /// <summary>A volume on top of the source's own (which the game's fades and the world sync keep setting).</summary>
    internal sealed class HuntGain : MonoBehaviour
    {
        internal volatile float Target = 1f;
        private float current = 1f;

        private void OnAudioFilterRead(float[] data, int channels)
        {
            float target = Target;
            int frames = data.Length / Mathf.Max(1, channels);
            float step = frames > 0 ? (target - current) / frames : 0f; // Ramped across the block: no clicks.
            for (int i = 0; i < data.Length; i += channels)
            {
                current += step;
                for (int c = 0; c < channels; c++) data[i + c] *= current;
            }
            current = target;
        }
    }
}
