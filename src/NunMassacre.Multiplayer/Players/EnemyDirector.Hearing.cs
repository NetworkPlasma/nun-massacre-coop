using NunMassacre.Multiplayer.Gameplay;
using UnityEngine;
using UnityEngine.AI;

namespace NunMassacre.Multiplayer.Players
{
    /// <summary>
    /// What the Nun does about what she hears (host only). This drives her own state machine rather than running beside
    /// it. Her "NoiseAlert" state already stops her, turns her head and body to a point and then starts her search, which
    /// walks her through the level's search points nearest a focus, looking side to side at each, until she gives up. Left
    /// alone, that focus is the hunted player's live position, so every search was quietly omniscient. Here:
    ///
    ///  - a faint sound makes her stop and listen towards it for a moment, and adds to her suspicion;
    ///  - a clear sound, or enough faint ones close together, starts an investigation: she faces where it reached her from
    ///    (the doorway or corner it came round) and searches the area it was made in, measured from that spot as it was;
    ///  - while she investigates she sticks with it for a few seconds unless something clearly louder comes along, so two
    ///    players cannot spin her between them;
    ///  - a chase, an attack, a bed check or anything she is told to ignore (her hiding spot, off the map) comes first.
    /// </summary>
    internal sealed partial class EnemyDirector
    {
        private const float ClearSound = 1.6f;          // Strength at which one sound is enough to investigate.
        private const float SuspicionPerSound = 0.45f;  // Added by a just-audible sound (scaled up to twice that).
        private const float SuspicionFade = 0.08f;      // Lost each second.
        private const float ListenSeconds = 1.6f, ListenEvery = 3f;
        private const float CommitSeconds = 5f;         // An investigation is only replaced by a clearly louder sound before this.
        private const float InvestigationLimit = 75f;   // Never longer than this, whatever her machine is doing.
        private const float NearOrigin = 6f;            // A partial route that ends this close to the sound still counts.
        private const float SamePlace = 4f;             // Sounds this close together are one place.
        private static readonly string[] SearchOver = { "What do do now", "Set Idle Vars", "Normal Search", "GoTo MeatPt", "Hiding" };

        /// <summary>Writes every sound she hears and what she does about it to the log.</summary>
        internal static bool Diagnostics;

        private sealed class Investigation { public HeardSound Sound; public Vector3 Facing; public float Started; }
        private Investigation investigation;
        private Transform soundFocus;
        private float suspicion, suspicionAt, nextListen;
        private readonly NavMeshPath soundPath = new NavMeshPath();
        private readonly Vector3[] soundCorners = new Vector3[32];


        /// <summary>A sound reached her (NunEars).</summary>
        internal void Heard(HeardSound sound)
        {
            if (!nun) return;
            suspicion = Mathf.Max(0f, suspicion - (Time.time - suspicionAt) * SuspicionFade);
            suspicionAt = Time.time;
            string busy = Busy();
            if (busy != null) { Note(sound, "ignored, " + busy); return; }

            bool reachable;
            Vector3 facing = Facing(sound.Origin, out reachable);
            suspicion += SuspicionPerSound * Mathf.Clamp(sound.Strength, 1f, 2f);
            bool convinced = sound.Strength >= ClearSound || suspicion >= 1f;

            if (sound.UnderBed && sound.Source > 0 && convinced)
            {
                if (reaction == Reaction.Listening) Unfreeze();
                if (BedSearch(sound.Source, sound.Kind == SoundKind.Whistle ? "whistling" : "talking"))
                {
                    suspicion = 0f;
                    Note(sound, "goes for the bed");
                    return;
                }
            }
            if (convinced && reachable) { Investigate(sound, facing); return; }
            Listen(sound, facing, convinced ? "can't get there" : "suspicion " + suspicion.ToString("0.00"));
        }

        /// <summary>
        /// Somewhere her next search should be around (a gunshot she is already reacting to, a downed player she goes back
        /// to). Sends her the given event; null when her own reaction is already under way.
        /// </summary>
        internal void SearchAround(Vector3 origin, string why, string eventName)
        {
            if (!nun) return;
            StartInvestigation(new HeardSound { Kind = SoundKind.Noise, Origin = origin, Strength = ClearSound, Time = Time.time }, origin, eventName);
            log("Nun searches around " + origin.ToString("0.0") + " (" + why + ")");
        }

        /// <summary>Why she cannot take notice of a sound right now, or null.</summary>
        private string Busy()
        {
            if (reaction != Reaction.None && reaction != Reaction.Listening) return "reacting (" + reaction + ")";
            if (distracted) return "just put someone down";
            if (Chasing()) return "chasing";
            if (Committed()) return "attacking";
            if (ApproachingBed()) return "going for a bed";
            if (Pursuing()) return "after someone she lost";
            var ignore = nun.blackboard != null ? nun.blackboard.GetVariable("IgnoreHearing") : null;
            if (ignore != null && ignore.value is bool && (bool)ignore.value && reaction != Reaction.Listening) return "not listening";
            return null;
        }

        private void Listen(HeardSound sound, Vector3 facing, string why)
        {
            bool justStarted = investigation != null && Time.time < investigation.Started + CommitSeconds;
            if (Time.time < nextListen || reaction == Reaction.Listening || justStarted) { Note(sound, "noted (" + why + ")"); return; }
            nextListen = Time.time + ListenEvery;
            voice = facing;
            Freeze(Reaction.Listening, ListenSeconds);
            Note(sound, "stops to listen (" + why + ")");
        }

        private void Investigate(HeardSound sound, Vector3 facing)
        {
            if (investigation != null)
            {
                // More of the same (someone still talking there): she is already on it, nothing restarts.
                if (Vector3.Distance(sound.Origin, investigation.Sound.Origin) <= SamePlace) { Note(sound, "already investigating it"); return; }
                if (Time.time < investigation.Started + CommitSeconds && sound.Strength < investigation.Sound.Strength * 1.3f)
                {
                    Note(sound, "ignored, still on the last sound");
                    return;
                }
            }
            StartInvestigation(sound, facing, "NoiseAlert");
            suspicion = 0f;
            Note(sound, "investigates");
        }

        private void StartInvestigation(HeardSound sound, Vector3 facing, string eventName)
        {
            if (reaction == Reaction.Listening) Unfreeze();
            investigation = new Investigation { Sound = sound, Facing = facing, Started = Time.time };
            if (!soundFocus) soundFocus = new GameObject("NMM_SoundFocus").transform;
            soundFocus.position = sound.Origin;
            MeasureSearchPoints(soundFocus); // Before her search picks its first point.
            var marker = Variable<Transform>("Target");
            if (marker) marker.position = facing;
            soundSearch = sound.Origin; soundSearchUntil = Time.time + 8f;
            if (eventName != null) nun.SendEvent(eventName);
        }

        /// <summary>
        /// Her search list has just been made. Left to itself it starts near the focus but keeps each point only three
        /// times in four and wanders off to neighbours she has not just searched, so after a sound she could end up
        /// anywhere. For an investigation the list is the search points nearest the sound on its floor, nearest first by
        /// the walk there, as many as her machine asked for.
        /// </summary>
        internal void SearchListMade(Humanoid.Agent.EnemyHunter.Tasks.CreateSearchPtList task)
        {
            if (investigation == null || Time.time - investigation.Started > 15f || !graphs.Contains(task.ownerSystem as NodeCanvas.Framework.Graph)) return;
            var all = task.NavPtSceneOrdered_READONLY.value;
            var agenda = task.SearchAgendaPts.value;
            if (all == null || agenda == null) return;
            Vector3 origin = investigation.Sound.Origin;
            int wanted = Mathf.Clamp(task.PtsToSearchCount.value, 1, 6);
            var nearest = new System.Collections.Generic.List<NavDist.NavPt>();
            foreach (var point in all)
                if (point && Mathf.Abs(((Component)point).transform.position.y - origin.y) < 2.5f) nearest.Add(point);
            nearest.Sort((a, b) => WalkFromSound(a, origin).CompareTo(WalkFromSound(b, origin)));
            if (nearest.Count == 0) return;
            agenda.Clear();
            for (int i = 0; i < nearest.Count && i < wanted; i++) agenda.Add(nearest[i]);
            task.SearchAgendaCount.value = agenda.Count;
            task.SearchAgendaIndex.value = 0;
            if (Diagnostics)
                log("Nun searches round the sound: "
                    + string.Join(", ", agenda.ConvertAll(p => p.name + " " + WalkFromSound(p, origin).ToString("0") + "m").ToArray()));
        }

        /// <summary>How far a search point is from the sound: the walk (as measured when the investigation began), else straight.</summary>
        private static float WalkFromSound(NavDist.NavPt point, Vector3 origin)
        {
            var measured = point.DistancePlayer;
            return measured && !measured.OutOfRange && measured.DistanceNavMesh > 0f ? measured.DistanceNavMesh
                : Vector3.Distance(((Component)point).transform.position, origin);
        }

        /// <summary>Each frame: an investigation lasts until her search is over or she finds someone.</summary>
        private void UpdateInvestigation()
        {
            if (investigation == null) return;
            var fsm = nun ? nun.graph as NodeCanvas.StateMachines.FSM : null;
            string state = fsm != null ? fsm.currentStateName : null;
            string over = Chasing() ? "she found someone" : Time.time - investigation.Started > InvestigationLimit ? "too long"
                : Time.time - investigation.Started > 2f && System.Array.IndexOf(SearchOver, state) >= 0 ? "searched the area" : null;
            if (over == null) return;
            if (Diagnostics) log("Nun investigation over after " + (Time.time - investigation.Started).ToString("0") + "s: " + over);
            investigation = null;
        }

        /// <summary>What her search points are measured from: the sound she is investigating, else the hunted player.</summary>
        private Transform SearchFocus(Transform hunted) { return investigation != null && soundFocus ? soundFocus : hunted; }

        /// <summary>
        /// Where a sound reaches her from: straight from where it was made when she can see that spot, otherwise the last
        /// corner of the walk there that she can still see (the doorway it comes through, the end of the corridor). False
        /// reachable when she cannot walk to it, or not to within a few metres of it.
        /// </summary>
        private Vector3 Facing(Vector3 origin, out bool reachable)
        {
            reachable = false;
            Vector3 eye = nun.transform.position + Vector3.up * 1.5f;
            int walls = LayerMask.GetMask("Wall", "Ground", "Ceiling", "Door");
            NavMeshHit from, to;
            if (!NavMesh.SamplePosition(nun.transform.position, out from, 1.5f, NavMesh.AllAreas)
                || !NavMesh.SamplePosition(origin, out to, 2.5f, NavMesh.AllAreas))
                return origin;
            if (!NavMesh.CalculatePath(from.position, to.position, NavMesh.AllAreas, soundPath) || soundPath.status == NavMeshPathStatus.PathInvalid)
                return origin;
            int count = soundPath.GetCornersNonAlloc(soundCorners);
            if (count < 2) { reachable = true; return origin; }
            reachable = soundPath.status == NavMeshPathStatus.PathComplete || Vector3.Distance(soundCorners[count - 1], origin) <= NearOrigin;
            if (!Physics.Linecast(eye, origin + Vector3.up * 1.2f, walls, QueryTriggerInteraction.Ignore)) return origin;
            int seen = 0;
            for (int i = 1; i < count; i++)
            {
                if (Physics.Linecast(eye, soundCorners[i] + Vector3.up * 1.3f, walls, QueryTriggerInteraction.Ignore)) break;
                seen = i;
            }
            return soundCorners[seen] + Vector3.up * 1.3f;
        }

        private void Note(HeardSound sound, string decision)
        {
            bool notable = decision == "investigates" || decision == "goes for the bed";
            if (Diagnostics || notable) log("Nun heard " + sound + "; " + decision);
        }

        /// <summary>Tests: start from nothing heard.</summary>
        internal void ForgetSounds() { ResetHearing(); }

        private void ResetHearing()
        {
            investigation = null; suspicion = 0f; nextListen = 0f;
            if (soundFocus) Object.Destroy(soundFocus.gameObject);
            soundFocus = null;
        }
    }
}
