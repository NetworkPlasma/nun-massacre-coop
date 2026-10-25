using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using GrannyClone;
using HarmonyLib;
using NunMassacre.Multiplayer.Gameplay;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace NunMassacre.Multiplayer
{
    // Hiding under beds: two to a bed, and the Nun's check of a bed with players under it.
    internal sealed partial class Session
    {
        // Hiding under a bed: room for two side by side where the bed allows; the host hands out the places.
        private const float HideSpacing = 0.75f;
        private sealed class HideClaim { public string Bed; public int Slot; public float At; }
        private readonly Dictionary<int, HideClaim> hideClaims = new Dictionary<int, HideClaim>();
        private readonly Dictionary<HideBedInteractable, Transform[]> hideSpots = new Dictionary<HideBedInteractable, Transform[]>();
        private HideBedInteractable pendingHideBed;
 private int pendingHideSlot = -1;

        private static readonly FieldInfo BedHidePoint = AccessTools.Field(typeof(HideBedInteractable), "hidePt"),
            BedExitPoint = AccessTools.Field(typeof(HideBedInteractable), "exitPt"), 
            BedPivot = AccessTools.Field(typeof(HideBedInteractable), "pivotMine"), BedFlipped = AccessTools.Field(typeof(HideBedInteractable), "flipped");

        /// <summary>The second place under each bed: beside the first along the bed, if the bed is long enough
        /// there and there is room beside it to crawl in and stand up again. Same result on every peer.</summary>
        private void PrepareHideSpots(Scene scene)
        {
            Physics.SyncTransforms();
            foreach (var bed in scene.GetRootGameObjects().SelectMany(root => root.GetComponentsInChildren<HideBedInteractable>(true)))
            {
                var hide = BedHidePoint.GetValue(bed) as Transform;
                var exit = BedExitPoint.GetValue(bed) as Transform;
                var pivot = BedPivot.GetValue(bed) as Transform;
                hideSpots[bed] = null;
                if (!hide || !exit) continue;
                Vector3 inward = Vector3.ProjectOnPlane(hide.position - exit.position, Vector3.up);
                if (inward.sqrMagnitude < 0.01f) continue;
                Vector3 along = Vector3.Cross(Vector3.up, inward.normalized);
                var renderers = (pivot ? pivot : bed.transform).GetComponentsInChildren<Renderer>(true);
                if (renderers.Length == 0) continue;
                Bounds box = renderers[0].bounds;
                foreach (var part in renderers) box.Encapsulate(part.bounds);
                float halfLength = Mathf.Abs(along.x) * box.extents.x + Mathf.Abs(along.z) * box.extents.z;
                float towardMiddle = Vector3.Dot(box.center - hide.position, along) >= 0f ? 1f : -1f;
                foreach (float side in new[] { towardMiddle, -towardMiddle })
                {
                    Vector3 shift = along * side * HideSpacing;
                    if (Mathf.Abs(Vector3.Dot(hide.position + shift - box.center, along)) > halfLength - 0.35f) continue; // Past the end of the bed.
                    Vector3 stand = exit.position + shift;
                    // A wall or furniture.
                    if (Physics.CheckCapsule(stand + Vector3.up * 0.45f, stand + Vector3.up * 1.5f, 0.25f,
                        Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore))
                        continue;
                    var spot = new GameObject("NMM_HideSpot").transform;
                    spot.SetParent(bed.transform, false);
                    spot.SetPositionAndRotation(hide.position + shift, hide.rotation);
                    var way = new GameObject("NMM_HideExit").transform; way.SetParent(bed.transform, false); way.SetPositionAndRotation(stand, exit.rotation);
                    hideSpots[bed] = new[] { spot, way };
                    break;
                }
            }
            plugin.Log("Hiding beds: " + hideSpots.Count(x => x.Value != null) + " of " + hideSpots.Count + " have room for two");
        }

        /// <summary>Host: the first free place under a bed for this player, or -1 when it is full.</summary>
        private int FreeHideSlot(HideBedInteractable bed, int peer)
        {
            Transform[] second;
            int capacity = hideSpots.TryGetValue(bed, out second) && second != null ? 2 : 1;
            string key = world.IdFor(bed.transform);
            float now = Time.realtimeSinceStartup;
            var taken = new HashSet<int>();
            foreach (var claim in hideClaims)
            {
                if (claim.Key == peer || claim.Value.Bed != key) continue;
                PlayerFrame frame = claim.Key == LocalId ? CapturePlayer() : null;
                if (frame == null) players.TryGetValue(claim.Key, out frame);
                bool there = frame != null && frame.bed == key && frame.state == (int)PlayerStates.States.Hiding;
                if (there || now - claim.Value.At < 4f) taken.Add(claim.Value.Slot); // Still crawling in, or under it.
            }
            for (int slot = 0; slot < capacity; slot++) if (!taken.Contains(slot)) return slot;
            return -1;
        }

        private void NoRoom(HideBedInteractable bed)
        {
            Tell(CapacityText(bed));
            if (!bed) return;
            string before = bed.InteractionString;
            bed.InteractionString = "No room";
            MEC.Timing.CallDelayed(2f, () => { if (bed && bed.InteractionString == "No room") bed.InteractionString = before; });
        }

        private string CapacityText(HideBedInteractable bed)
        {
            Transform[] second;
            return bed && hideSpots.TryGetValue(bed, out second) && second != null
                ? "No room under this bed: two are already hiding there." : "No room under this bed: it only fits one.";
        }

        /// <summary>PlayerStates.Hide: the second player under a bed lies beside the first.</summary>
        internal void HidePlace(ref Transform hidePt, ref Transform exitPt)
        {
            Transform[] second;
            if (pendingHideSlot != 1 || !pendingHideBed || !hideSpots.TryGetValue(pendingHideBed, out second) || second == null) return;
            hidePt = second[0]; exitPt = second[1];
        }

        /// <summary>Which way a teammate hiding under this bed faces: out, towards the room.</summary>
        internal Vector3? HideFacing(string bedId)
        {
            var bed = world == null || string.IsNullOrEmpty(bedId) ? null : world.Find(bedId) as Transform;
            var hiding = bed ? bed.GetComponent<HideBedInteractable>() : null;
            var hide = hiding ? BedHidePoint.GetValue(hiding) as Transform : null; var exit = hiding ? BedExitPoint.GetValue(hiding) as Transform : null;
            if (!hide || !exit) return null;
            Vector3 outward = Vector3.ProjectOnPlane(exit.position - hide.position, Vector3.up);
            return outward.sqrMagnitude > 0.01f ? outward.normalized : (Vector3?)null;
        }

        private void Hiding(Transform killPoint, Transform bed) { localBed = bed; localBedKillPoint = killPoint; }
        private void HideExit() { localBed = null; localBedKillPoint = null; }

        // One under the bed: the game's own check, the bed flipped over and that player turned out from under it (a guest
        // gets the state change the game would give the host). Two under it: the bed stays where it is; she stoops, reaches
        // under (her bed-lift animation, without the bed) and hauls them both out, stands them up in front of her, and then
        // goes for the one she came for.
        private const float GrabReach = 0.85f; // From her stoop to the haul.
        private const float GrabGap = 0.35f;   // The second comes out just behind the first.
        private NodeCanvas.Framework.Graph grabGraph;
        private float grabUntil;
        private Vector3 grabFace;

        /// <summary>Everyone hiding under that bed (any bed when it is not known), the one she came for first.</summary>
        private List<PlayerFrame> HidersUnder(Transform bed)
        {
            string key = bed ? world.IdFor(bed) : null;
            var hiders = new List<PlayerFrame>();
            foreach (var frame in players.Values.Concat(new[] { CapturePlayer() }))
                if (frame != null && frame.state == (int)PlayerStates.States.Hiding && frame.scene == SceneName
                    && (key == null || frame.bed == key) && hiders.All(h => h.id != frame.id))
                    hiders.Add(frame);
            return hiders.OrderBy(h => h.id == enemy.TargetId ? 0 : 1).ToList();
        }

        /// <summary>Host: her bed check is about to flip the bed. With two under it she grabs them instead (true: the flip
        /// is skipped).</summary>
        internal bool GrabFromBed(GrannyClone.Tasks.FlipBed step)
        {
            var graph = step.ownerSystem as NodeCanvas.Framework.Graph;
            if (!Active || !IsHost || !Ready || !enemy.IsNunGraph(graph)) return false;
            Transform bed = step.BedToFlip.value ? step.BedToFlip.value : enemy.BedBeingSearched;
            var hiders = HidersUnder(bed);
            if (hiders.Count < 2) return false;
            Vector3 nun = enemy.NunPosition ?? Vector3.zero;
            for (int i = 0; i < hiders.Count; i++)
            {
                float delay = GrabReach + i * GrabGap;
                if (hiders[i].id == LocalId) Life.DragOutOfBed(nun, delay);
                else Send(hiders[i].id, new Packet { type = "pullout", epoch = epoch, scene = SceneName, position = nun, amount = delay });
            }
            grabGraph = graph;
            grabUntil = Time.time + GrabReach + GrabGap * (hiders.Count - 1) + CoopLife.DragSeconds + CoopLife.StandSeconds + 0.4f;
            grabFace = hiders[0].position;
            plugin.Log("Nun reaches under " + (bed ? bed.name : "the bed") + " and drags " + hiders.Count + " out: "
                + string.Join(", ", hiders.Select(h => "peer " + h.id).ToArray()));
            return true;
        }

        /// <summary>Her bed check is mid-grab (the bed-lift sound is skipped: the bed does not move).</summary>
        internal bool Grabbing(NodeCanvas.Framework.Graph graph) { return graph != null && graph == grabGraph && Time.time < grabUntil; }

        /// <summary>
        /// Host: the Nun's bed check reached "turn the player out". Mid-grab: she holds still, facing them, until both are
        /// up. Otherwise the game's state change goes to whoever is under that bed: a guest gets it sent, and the game's own
        /// step runs only when the host is the one under it. True when the game's step is skipped.
        /// </summary>
        internal bool PullFromBed(NodeCanvas.Framework.ActionTask step)
        {
            var graph = step.ownerSystem as NodeCanvas.Framework.Graph;
            if (!Active || !IsHost || !Ready || !enemy.IsNunGraph(graph)) return false;
            if (Grabbing(graph))
            {
                grabGraph = null;
                enemy.HoldForDrag(Mathf.Max(0.3f, grabUntil - Time.time), grabFace);
                return true;
            }
            var change = step as GrannyClone.Tasks.PlayerStateChange;
            if (change == null) return false;
            var hiders = HidersUnder(enemy.BedBeingSearched);
            foreach (var hider in hiders.Where(h => h.id != LocalId))
                Send(hider.id, new Packet { type = "bedstate", epoch = epoch, scene = SceneName, number = (int)change.newState });
            plugin.Log("Nun turns "
                + (hiders.Count == 0 ? "nobody" : string.Join(", ", hiders.Select(h => "peer " + h.id).ToArray()))
                + " out from under the bed (" + change.newState + ")");
            return hiders.All(h => h.id != LocalId); // The host under it: the game's own step does it.
        }

        /// <summary>Host: a bed was lifted; every guest sees it lift too.</summary>
        internal void BedLifted(HideBedInteractable bed, float delay, float duration)
        {
            if (!Active || !IsHost || !Ready || Replaying || !bed) return;
            foreach (var peer in players.Keys.Where(id => id != LocalId).ToList())
                Send(peer,
                    new Packet { type = "bedflip", epoch = epoch, scene = SceneName, target = world.IdFor(bed.transform), amount = delay, number = Mathf.RoundToInt(duration * 1000f) });
        }
    }
}
