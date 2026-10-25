using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace NunMassacre.Multiplayer.Voice
{
    /// <summary>
    /// Raytraced voice acoustics, powered by Vercidium Audio: the level's walls, floors, ceilings, doors and furniture go
    /// into its world, each with what it is made of (tile, painted plaster, metal ducts, wood, glass, dirt). Rays from
    /// the listener and from each teammate's mouth bounce about the real building on background threads: what comes
    /// back gives the reverb of the space each is in, and what gets from the listener to a voice (round corners, through
    /// doorways, through walls) how muffled that voice is. Doors that swing are moved in it as they do; scenario rooms
    /// that appear and vanish are added and taken away. If the library cannot be loaded, voices fall back to the mod's
    /// own probes.
    /// </summary>
    internal sealed class RaytracedAcoustics : IDisposable
    {
        private sealed class Piece { public IntPtr Primitive; public bool Mesh, Thickened; public VAudio.Material Material; }
        private sealed class Surface { public readonly List<Piece> Pieces = new List<Piece>(); public Vector3 Position; public Quaternion Rotation; public bool Moves; }
        private sealed class Speaker { public IntPtr Emitter; public float LastUsed; public int Born; }

        private static bool loaded, tried;
        private readonly Action<string> log;
        private IntPtr world, listener;
        private string scene;
        private readonly Dictionary<Collider, Surface> surfaces = new Dictionary<Collider, Surface>();
        private readonly Dictionary<int, Speaker> speakers = new Dictionary<int, Speaker>();
        private readonly Dictionary<AudioSource, Speaker> sounds = new Dictionary<AudioSource, Speaker>(); // The game's own sounds.
        private readonly List<IntPtr> dying = new List<IntPtr>(); // Emitters taken out, freed once the raytracer lets go.
        private readonly List<Piece> retired = new List<Piece>(); // Surfaces taken out, likewise.
        private readonly List<Collider> movers = new List<Collider>();
        private float nextSweep;
        private float nextScan;
        private readonly HashSet<Collider> skipped = new HashSet<Collider>(); // Could not go in (a mesh the game cannot read): not tried again.
        private readonly Stopwatch watch = new Stopwatch();
        internal double MainThreadMs; // Tests: what the last update cost this thread.

        internal RaytracedAcoustics(Action<string> log) { this.log = log; }

        internal bool Running { get { return world != IntPtr.Zero; } }
        internal static bool Loaded { get { return loaded; } }

        /// <summary>Loads vaudionative.dll from beside the plugin, once.</summary>
        internal static bool Load(Action<string> log)
        {
            if (tried) return loaded;
            tried = true;
            string why;
            string path = System.IO.Path.Combine(System.IO.Path.GetDirectoryName(typeof(RaytracedAcoustics).Assembly.Location) ?? ".", VAudio.Library + ".dll");
            loaded = VAudio.Load(path, out why);
            log(loaded ? "Raytraced audio: Vercidium Audio " + VAudio.Version + " (Powered by Vercidium Audio)" : "Raytraced audio off: " + why);
            return loaded;
        }

        /// <summary>Each frame: the world for this level (built on entering it), the listener at the ears.</summary>
        internal void Update(bool wanted, Vector3 ear)
        {
            if (!wanted || !Load(log)) { if (Running) Shutdown(); return; }
            string active = SceneManager.GetActiveScene().name;
            if (Running && active != scene) Shutdown();
            if (!Running) { Build(active); if (!Running) return; }
            watch.Reset(); watch.Start();
            if (Time.unscaledTime >= nextScan) { nextScan = Time.unscaledTime + 3f; Scan(false); }
            Move();
            VAudio.vaEmitterSetPosition(listener, Point(ear));
            foreach (int id in speakers.Where(p => Time.unscaledTime - p.Value.LastUsed > 3f).Select(p => p.Key).ToList()) Forget(id);
            foreach (var source in sounds.Where(p => !p.Key || Time.unscaledTime - p.Value.LastUsed > 1f).Select(p => p.Key).ToList()) ForgetSound(source);
            foreach (int key in probes.Where(p => Time.unscaledTime - p.Value.LastUsed > 2f).Select(p => p.Key).ToList()) ForgetProbe(key);
            if (nunEar != IntPtr.Zero && Time.unscaledTime - nunEarUsed > 3f) ForgetNunEar();
            VAudio.vaWorldUpdate(world);
            updates++;
            for (int i = dying.Count - 1; i >= 0; i--)
                if (VAudio.vaEmitterDestroy(dying[i]) != VAudio.ErrorInUse) dying.RemoveAt(i);
            for (int i = retired.Count - 1; i >= 0; i--)
                if (Destroy(retired[i]) != VAudio.ErrorInUse) retired.RemoveAt(i);
            watch.Stop();
            MainThreadMs = watch.Elapsed.TotalMilliseconds;
        }

        /// <summary>A teammate's mouth this frame: their own reverb rays, and the listener's rays sent after them.</summary>
        internal void Voice(int id, Vector3 mouth)
        {
            if (!Running) return;
            Speaker speaker;
            if (!speakers.TryGetValue(id, out speaker))
            {
                speaker = new Speaker { Emitter = VAudio.vaEmitterCreate() };
                VAudio.vaEmitterSetName(speaker.Emitter, "voice " + id);
                VAudio.vaEmitterSetReverbRayCount(speaker.Emitter, 64);
                VAudio.vaEmitterSetReverbBounceCount(speaker.Emitter, 128); // Enough to hear a small hard room die away.
                VAudio.vaEmitterSetPosition(speaker.Emitter, Point(mouth));
                VAudio.vaWorldAddEmitter(world, speaker.Emitter);
                VAudio.vaEmitterAddTarget(listener, speaker.Emitter);
                speakers[id] = speaker;
            }
            speaker.LastUsed = Time.unscaledTime;
            VAudio.vaEmitterSetPosition(speaker.Emitter, Point(mouth));
        }

        /// <summary>A game sound where it is this frame: the listener's rays sent after it (no reverb rays of its own: the
        /// listener's reverb is the one these sounds get).</summary>
        internal void Sound(AudioSource source, Vector3 at)
        {
            if (!Running || !source) return;
            Speaker sound;
            if (!sounds.TryGetValue(source, out sound))
            {
                sound = new Speaker { Emitter = VAudio.vaEmitterCreate() };
                VAudio.vaEmitterSetPosition(sound.Emitter, Point(at));
                VAudio.vaWorldAddEmitter(world, sound.Emitter);
                VAudio.vaEmitterAddTarget(listener, sound.Emitter);
                sounds[source] = sound;
            }
            sound.LastUsed = Time.unscaledTime;
            VAudio.vaEmitterSetPosition(sound.Emitter, Point(at));
        }

        internal bool SoundMuffling(AudioSource source, out VAudio.LowPass filter)
        {
            filter = default(VAudio.LowPass);
            Speaker sound;
            if (!Running || !source || !sounds.TryGetValue(source, out sound)) return false;
            var found = VAudio.Filter(VAudio.vaEmitterGetTargetFilter(listener, sound.Emitter));
            if (!found.HasValue) return false;
            filter = found.Value;
            return true;
        }

        internal void ForgetSound(AudioSource source)
        {
            Speaker sound;
            if (!Running || source == null || !sounds.TryGetValue(source, out sound)) return;
            VAudio.vaEmitterRemoveTarget(listener, sound.Emitter);
            VAudio.vaWorldRemoveEmitter(world, sound.Emitter);
            dying.Add(sound.Emitter);
            sounds.Remove(source);
        }

        /// <summary>How much of a teammate's voice gets to the listener, low and high frequencies (1: clear); false until
        /// the rays have been after them.</summary>
        internal bool Muffling(int id, out VAudio.LowPass filter)
        {
            filter = default(VAudio.LowPass);
            Speaker speaker;
            if (!Running || !speakers.TryGetValue(id, out speaker)) return false;
            var found = VAudio.Filter(VAudio.vaEmitterGetTargetFilter(listener, speaker.Emitter));
            if (!found.HasValue) return false;
            filter = found.Value;
            return true;
        }

        // ---------- The Nun's ears (on the host's game) ----------
        // Her own emitter where her head is, its occlusion and permeation rays sent after probes: each player's mouth, and
        // the place of a noise she might hear. What comes back is how much of a sound made there reaches her through the
        // building (1: nothing in the way): the same rays the players' own hearing uses.
        private IntPtr nunEar;
        private float nunEarUsed;
        private int updates;
        private readonly Dictionary<int, Speaker> probes = new Dictionary<int, Speaker>();

        /// <summary>Each frame on the host: where the Nun's ears are.</summary>
        internal void NunEar(Vector3 ear)
        {
            if (!Running) return;
            if (nunEar == IntPtr.Zero)
            {
                nunEar = VAudio.vaEmitterCreate();
                VAudio.vaEmitterSetName(nunEar, "nun");
                VAudio.vaEmitterSetOcclusionRayCount(nunEar, 256);
                VAudio.vaEmitterSetOcclusionBounceCount(nunEar, 6);
                VAudio.vaEmitterSetPermeationRayCount(nunEar, 64);
                VAudio.vaEmitterSetPermeationBounceCount(nunEar, 3);
                VAudio.vaEmitterSetPosition(nunEar, Point(ear));
                VAudio.vaWorldAddEmitter(world, nunEar);
            }
            nunEarUsed = Time.unscaledTime;
            VAudio.vaEmitterSetPosition(nunEar, Point(ear));
        }

        /// <summary>A place whose sound the Nun's rays go after: a player's mouth (kept while used), or a noise's spot.</summary>
        internal void Probe(int key, Vector3 at)
        {
            if (!Running || nunEar == IntPtr.Zero) return;
            Speaker probe;
            if (!probes.TryGetValue(key, out probe))
            {
                probe = new Speaker { Emitter = VAudio.vaEmitterCreate(), Born = updates };
                VAudio.vaEmitterSetName(probe.Emitter, "heard " + key);
                VAudio.vaEmitterSetPosition(probe.Emitter, Point(at));
                VAudio.vaWorldAddEmitter(world, probe.Emitter);
                VAudio.vaEmitterAddTarget(nunEar, probe.Emitter);
                probes[key] = probe;
            }
            probe.LastUsed = Time.unscaledTime;
            VAudio.vaEmitterSetPosition(probe.Emitter, Point(at));
        }

        /// <summary>How much of a sound at a probe reaches the Nun, low and high (1: in the open); false until her rays have
        /// been after it.</summary>
        internal bool HeardByNun(int key, out VAudio.LowPass filter)
        {
            filter = default(VAudio.LowPass);
            Speaker probe;
            if (!Running || nunEar == IntPtr.Zero || !probes.TryGetValue(key, out probe) || updates - probe.Born < 3) return false;
            var found = VAudio.Filter(VAudio.vaEmitterGetTargetFilter(nunEar, probe.Emitter));
            if (!found.HasValue) return false;
            filter = found.Value;
            return true;
        }

        internal void ForgetProbe(int key)
        {
            Speaker probe;
            if (!probes.TryGetValue(key, out probe)) return;
            if (Running)
            {
                if (nunEar != IntPtr.Zero) VAudio.vaEmitterRemoveTarget(nunEar, probe.Emitter);
                VAudio.vaWorldRemoveEmitter(world, probe.Emitter);
                dying.Add(probe.Emitter);
            }
            probes.Remove(key);
        }

        private void ForgetNunEar()
        {
            foreach (int key in probes.Keys.ToList()) ForgetProbe(key);
            if (nunEar == IntPtr.Zero) return;
            if (Running) { VAudio.vaWorldRemoveEmitter(world, nunEar); dying.Add(nunEar); }
            nunEar = IntPtr.Zero;
        }

        internal VAudio.EAXReverb? ListenerReverb { get { return Running ? VAudio.EAX(listener) : null; } }

        internal VAudio.EAXReverb? ReverbOf(int id)
        {
            Speaker speaker;
            return Running && speakers.TryGetValue(id, out speaker) ? VAudio.EAX(speaker.Emitter) : null;
        }

        internal float ListenerOutside { get { return Running ? VAudio.vaEmitterGetOutsidePercent(listener) : 0f; } }

        internal string Describe()
        {
            if (!Running) return loaded ? "not running" : "not loaded";
            var pieces = surfaces.Values.SelectMany(s => s.Pieces).ToList();
            return scene + ": " + surfaces.Count + " surfaces in " + pieces.Count + " pieces (" + pieces.Count(p => p.Mesh) + " mesh, " + pieces.Count(p => p.Thickened) + " open panels given thickness; "
                + string.Join(" ", pieces.GroupBy(p => p.Material).OrderByDescending(g => g.Count()).Select(g => g.Key + "=" + g.Count()).ToArray()) + "; " + movers.Count + " moving, " + skipped.Count + " left out), "
                + speakers.Count + " voices, " + sounds.Count + " game sounds, main thread " + MainThreadMs.ToString("0.000") + " ms";
        }

        private void Forget(int id)
        {
            Speaker speaker;
            if (!speakers.TryGetValue(id, out speaker)) return;
            VAudio.vaEmitterRemoveTarget(listener, speaker.Emitter);
            VAudio.vaWorldRemoveEmitter(world, speaker.Emitter);
            dying.Add(speaker.Emitter);
            speakers.Remove(id);
        }

        // ---------- The level, in the raytracer ----------

        private void Build(string name)
        {
            var timer = Stopwatch.StartNew();
            world = VAudio.vaWorldCreate();
            if (world == IntPtr.Zero) { log("Raytraced audio: no world"); return; }
            scene = name;
            VAudio.vaWorldSetCoordinateSystem(world, VAudio.CoordinateSystem.Unity);
            // Leave the game a core or two: the rays run on background threads.
            VAudio.vaWorldSetMaximumConcurrencyLevel(world, Mathf.Clamp(SystemInfo.processorCount - 2, 1, 6));
            Materials();
            skipped.Clear();
            Scan(true);
            if (skipped.Count > 0)
                log("Raytraced audio: left out " + skipped.Count + " surfaces: "
                    + string.Join(", ", skipped.GroupBy(c => Why(c)).Select(g => g.Count() + " " + g.Key + " (" + string.Join(", ", g.Take(4).Select(c => c.name).ToArray()) + ")").ToArray()));
            listener = VAudio.vaEmitterCreate();
            VAudio.vaEmitterSetName(listener, "listener");
            VAudio.vaEmitterSetReverbRayCount(listener, 128);
            VAudio.vaEmitterSetReverbBounceCount(listener, 128);
            VAudio.vaEmitterSetOcclusionRayCount(listener, 512); // Vercidium's recommended counts.
            VAudio.vaEmitterSetOcclusionBounceCount(listener, 8);
            VAudio.vaEmitterSetPermeationRayCount(listener, 128);
            VAudio.vaEmitterSetPermeationBounceCount(listener, 3);
            VAudio.vaEmitterSetAmbientOcclusionRayCount(listener, 32);
            VAudio.vaWorldAddEmitter(world, listener);
            nextScan = Time.unscaledTime + 3f;
            log("Raytraced audio: built " + Describe() + " in " + timer.ElapsedMilliseconds + " ms");
        }

        /// <summary>The school as built: painted plaster and masonry that is quite hard (the library's own concrete and
        /// brick are rough and soak up sound like an outdoor wall), so those are made smoother here.</summary>
        private void Materials()
        {
            SetMaterial(VAudio.Material.Concrete, 0.03f, 0.06f);
            SetMaterial(VAudio.Material.Brick, 0.04f, 0.07f);
            // How far sound gets through each material: a little further than the library has it, so a voice through a
            // plaster wall and a shut door is a muffled murmur rather than nothing (as it is in a real building).
            for (int m = 1; m < (int)VAudio.Material.WoodOutdoor + 1; m++)
            {
                VAudio.vaWorldSetMaterialTransmissionLF(world, m, VAudio.vaWorldGetMaterialTransmissionLF(world, m) * TransmissionLF);
                VAudio.vaWorldSetMaterialTransmissionHF(world, m, VAudio.vaWorldGetMaterialTransmissionHF(world, m) * TransmissionHF);
            }
        }
        internal static float TransmissionLF = 1.6f, TransmissionHF = 1.25f; // Tests may change them (before entering a level).

        private void SetMaterial(VAudio.Material material, float lf, float hf)
        {
            VAudio.vaWorldSetMaterialAbsorptionLF(world, (int)material, lf);
            VAudio.vaWorldSetMaterialAbsorptionHF(world, (int)material, hf);
        }

        /// <summary>Adds the level's solid colliders that are not in yet, takes out those gone or switched off.</summary>
        private void Scan(bool first)
        {
            int mask = LayerMask.GetMask("Wall", "Ground", "Ceiling", "Door", "Furniture");
            var seen = new HashSet<Collider>();
            Bounds? all = null;
            foreach (var collider in UnityEngine.Object.FindObjectsOfType<Collider>())
            {
                if (!collider || !collider.enabled || collider.isTrigger || (mask & (1 << collider.gameObject.layer)) == 0) continue;
                if (!(collider is BoxCollider) && !(collider is MeshCollider)) continue;
                seen.Add(collider);
                if (first) all = all.HasValue ? Encapsulate(all.Value, collider.bounds) : collider.bounds;
                if (surfaces.ContainsKey(collider) || skipped.Contains(collider)) continue;
                var surface = Add(collider);
                if (surface == null) { skipped.Add(collider); continue; }
                surfaces[collider] = surface;
                if (surface.Moves) movers.Add(collider);
            }
            foreach (var gone in surfaces.Keys.Where(c => !seen.Contains(c)).ToList())
            {
                foreach (var piece in surfaces[gone].Pieces) { VAudio.vaWorldRemovePrimitive_(world, piece.Primitive); retired.Add(piece); }
                surfaces.Remove(gone);
                movers.Remove(gone);
            }
            if (first && all.HasValue)
            {
                // The building and its grounds, a little more; open sky above.
                Vector3 min = all.Value.min - new Vector3(5f, 5f, 5f), size = Vector3.Min(all.Value.size + new Vector3(10f, 40f, 10f), Vector3.one * 2000f);
                VAudio.vaWorldSetPosition(world, Point(min));
                VAudio.vaWorldSetSize(world, Point(size));
            }
        }

        private static Bounds Encapsulate(Bounds a, Bounds b) { a.Encapsulate(b); return a; }

        private Surface Add(Collider collider)
        {
            var t = collider.transform;
            var surface = new Surface { Position = t.position, Rotation = t.rotation,
                Moves = collider.gameObject.layer == LayerMask.NameToLayer("Door") || collider.attachedRigidbody };
            var box = collider as BoxCollider;
            if (box)
            {
                var material = MaterialOf(collider);
                Vector3 size = Vector3.Max(Vector3.Scale(box.size, Abs(t.lossyScale)), Vector3.one * 0.02f);
                var prism = VAudio.vaPrismPrimitiveCreate();
                VAudio.vaPrismPrimitiveSetMaterial(prism, material);
                VAudio.vaPrismPrimitiveSetSize(prism, Point(size));
                var m = Matrix(t.TransformPoint(box.center), t.rotation);
                VAudio.vaPrismPrimitiveSetTransform(prism, ref m);
                if (VAudio.vaWorldAddPrimitive_(world, prism) != VAudio.Success) { VAudio.vaPrismPrimitiveDestroy(prism); return null; }
                surface.Pieces.Add(new Piece { Primitive = prism, Material = material });
                return surface;
            }
            var mesh = ((MeshCollider)collider).sharedMesh;
            if (!mesh || !mesh.isReadable) return null;
            // What each part of the mesh is made of: where the collider is the drawn mesh itself, each submesh's own
            // material (a merged wall is painted plaster on one side, tiles on the other); otherwise the look as a whole.
            var filter = collider.GetComponent<MeshFilter>(); var look = collider.GetComponent<Renderer>();
            Material[] drawn = filter && look && filter.sharedMesh == mesh ? look.sharedMaterials : null;
            var whole = MaterialOf(collider);
            Vector3 signed = t.lossyScale;
            var vertices = mesh.vertices;
            var corners = new List<Vector3>(); var kinds = new List<VAudio.Material>();
            for (int sub = 0; sub < mesh.subMeshCount; sub++)
            {
                var kind = drawn != null && sub < drawn.Length && drawn[sub]
                    ? MaterialNamed(drawn[sub].name + " " + collider.name, collider.gameObject.layer) : whole;
                foreach (int index in mesh.GetTriangles(sub)) corners.Add(Vector3.Scale(vertices[index], signed));
                for (int k = 0; k < mesh.GetTriangles(sub).Length / 3; k++) kinds.Add(kind);
            }
            if (corners.Count < 3) return null;
            // A merged mesh is many separate pieces (each wall, each pillar): each goes in on its own, as the solid it is,
            // made of what covers most of it.
            bool turn = signed.x * signed.y * signed.z > 0f;
            var transform = Matrix(t.position, t.rotation);
            foreach (var piece in Pieces(corners))
            {
                var area = new Dictionary<VAudio.Material, float>();
                var triangles = new Vector3[piece.Count * 3];
                for (int k = 0; k < piece.Count; k++)
                {
                    int i = piece[k] * 3;
                    triangles[k * 3] = corners[i]; triangles[k * 3 + 1] = corners[i + 1]; triangles[k * 3 + 2] = corners[i + 2];
                    float size = Vector3.Cross(corners[i + 1] - corners[i], corners[i + 2] - corners[i]).magnitude;
                    float sum; area.TryGetValue(kinds[piece[k]], out sum); area[kinds[piece[k]]] = sum + size;
                }
                var material = area.OrderByDescending(p => p.Value).First().Key;
                bool closed = Closed(triangles);
                List<Vector3> solid;
                if (closed)
                {
                    // Rays tell a surface's outside from its inside by which way its triangles wind: anticlockwise seen
                    // from outside, the other way from Unity (clockwise). Wound Unity's way a solid wall is inside out to
                    // them and every ray goes straight through it (a closed room had no reverb and no wall muffled
                    // anything), so each triangle is turned round, except on a mirrored object (scaled by a negative),
                    // already wound the other way.
                    solid = new List<Vector3>(triangles.Length);
                    for (int i = 0; i < triangles.Length; i += 3)
                    {
                        solid.Add(triangles[i]);
                        solid.Add(triangles[turn ? i + 2 : i + 1]);
                        solid.Add(triangles[turn ? i + 1 : i + 2]);
                    }
                }
                // An open surface (a wall that is one plane, a duct's sheet metal, a stall partition): rays only stop at,
                // or pass through, something with an inside, so each triangle becomes a slab as thick as such a thing
                // made of that is.
                else solid = Thicken(triangles, Thickness(material) * 0.5f);
                if (solid.Count < 3) continue;
                var points = new VAudio.Vector[solid.Count];
                var min = Vector3.one * float.MaxValue; var max = Vector3.one * float.MinValue;
                for (int i = 0; i < solid.Count; i++)
                {
                    points[i] = Point(solid[i]);
                    min = Vector3.Min(min, solid[i]); max = Vector3.Max(max, solid[i]);
                }
                IntPtr primitive;
                if (VAudio.vaMeshPrimitiveCreate(material, points, points.Length, Point(min), Point(max), ref transform, out primitive) != VAudio.Success)
                    continue;
                if (VAudio.vaWorldAddPrimitive_(world, primitive) != VAudio.Success) { VAudio.vaMeshPrimitiveDestroy(primitive); continue; }
                surface.Pieces.Add(new Piece { Primitive = primitive, Mesh = true, Thickened = !closed, Material = material });
            }
            return surface.Pieces.Count > 0 ? surface : null;
        }

        /// <summary>How thick a single-sided surface of this is, in metres: a plastered or tiled wall, a door, sheet metal.</summary>
        private static float Thickness(VAudio.Material material)
        {
            switch (material)
            {
                case VAudio.Material.Metal: return 0.02f;
                case VAudio.Material.Glass: return 0.01f;
                case VAudio.Material.WoodIndoor: case VAudio.Material.WoodOutdoor: return 0.04f;
                case VAudio.Material.Cloth: return 0.05f;
                default: return 0.15f;
            }
        }

        private static long Weld(Vector3 v)
        {
            return ((long)Mathf.RoundToInt(v.x * 1000f) * 73856093L) ^ ((long)Mathf.RoundToInt(v.y * 1000f) * 19349663L) ^ ((long)Mathf.RoundToInt(v.z * 1000f) * 83492791L);
        }

        /// <summary>The mesh's separate pieces: triangles joined through shared corners (welded by position), as lists
        /// of triangle numbers.</summary>
        private static List<List<int>> Pieces(List<Vector3> corners)
        {
            int count = corners.Count / 3;
            var parent = new int[count];
            for (int i = 0; i < count; i++) parent[i] = i;
            Func<int, int> root = null;
            root = i => { while (parent[i] != i) { parent[i] = parent[parent[i]]; i = parent[i]; } return i; };
            var owner = new Dictionary<long, int>();
            for (int i = 0; i < corners.Count; i++)
            {
                long key = Weld(corners[i]); int triangle = i / 3, other;
                if (owner.TryGetValue(key, out other)) { int a = root(triangle), b = root(other); if (a != b) parent[a] = b; }
                else owner[key] = triangle;
            }
            var groups = new Dictionary<int, List<int>>();
            for (int i = 0; i < count; i++)
            {
                List<int> group; int r = root(i);
                if (!groups.TryGetValue(r, out group)) groups[r] = group = new List<int>();
                group.Add(i);
            }
            return groups.Values.ToList();
        }

        private static string Why(Collider collider)
        {
            var mesh = collider is MeshCollider ? ((MeshCollider)collider).sharedMesh : null;
            return !(collider is MeshCollider) ? "other" : !mesh ? "no mesh" : !mesh.isReadable ? "unreadable"
                : mesh.triangles.Length < 3 ? "empty" : "refused";
        }

        /// <summary>Whether the triangles close round a solid (every edge shared by two of them; corners welded to the
        /// millimetre, as Unity splits them at seams).</summary>
        private static bool Closed(Vector3[] corners)
        {
            var edges = new Dictionary<long, int>();
            Func<Vector3, long> key = Weld;
            for (int i = 0; i < corners.Length; i += 3)
                for (int e = 0; e < 3; e++)
                {
                    long a = key(corners[i + e]), b = key(corners[i + (e + 1) % 3]);
                    if (a == b) continue;
                    long edge = a < b ? a * 31 + b : b * 31 + a;
                    int count; edges.TryGetValue(edge, out count); edges[edge] = count + 1;
                }
            int open = edges.Values.Count(c => c == 1);
            return edges.Count > 0 && open <= edges.Count / 50; // A few stray edges (a seam that does not quite meet) still count as closed.
        }

        /// <summary>Each triangle as a slab this thick either side of it: a closed prism, wound the rays' way (outward
        /// faces anticlockwise seen from outside).</summary>
        private static List<Vector3> Thicken(Vector3[] corners, float half)
        {
            var solid = new List<Vector3>(corners.Length * 8);
            for (int i = 0; i < corners.Length; i += 3)
            {
                Vector3 a = corners[i], b = corners[i + 1], c = corners[i + 2];
                Vector3 n = Vector3.Cross(b - a, c - a);
                if (n.sqrMagnitude < 1e-10f) continue;
                n = n.normalized * half;
                Vector3 centre = (a + b + c) / 3f;
                Vector3[] top = { a + n, b + n, c + n }, bottom = { a - n, b - n, c - n };
                Face(solid, top[0], top[1], top[2], centre);
                Face(solid, bottom[0], bottom[1], bottom[2], centre);
                for (int e = 0; e < 3; e++)
                {
                    int f = (e + 1) % 3;
                    Face(solid, top[e], top[f], bottom[f], centre);
                    Face(solid, top[e], bottom[f], bottom[e], centre);
                }
            }
            return solid;
        }

        /// <summary>A triangle of a convex solid, wound so its (right-handed) normal points in, away from the outside:
        /// how the rays read a face that looks outward.</summary>
        private static void Face(List<Vector3> solid, Vector3 p, Vector3 q, Vector3 r, Vector3 centre)
        {
            bool outward = Vector3.Dot(Vector3.Cross(q - p, r - p), (p + q + r) / 3f - centre) > 0f;
            solid.Add(p);
            solid.Add(outward ? r : q);
            solid.Add(outward ? q : r);
        }

        /// <summary>Doors as they swing, and anything else that moves (a cabinet's door, a drawer, a toilet's lid: found by
        /// looking over everything five times a second, then followed every frame): where they are now. A cabinet left
        /// shut in the rays put its own opening sound inside it.</summary>
        private void Move()
        {
            if (Time.unscaledTime >= nextSweep)
            {
                nextSweep = Time.unscaledTime + 0.2f;
                foreach (var pair in surfaces)
                {
                    if (pair.Value.Moves || !pair.Key) continue;
                    var t = pair.Key.transform;
                    if ((t.position - pair.Value.Position).sqrMagnitude < 0.0001f && Quaternion.Angle(t.rotation, pair.Value.Rotation) < 0.5f) continue;
                    pair.Value.Moves = true;
                    movers.Add(pair.Key);
                }
            }
            foreach (var collider in movers)
            {
                if (!collider) continue;
                var surface = surfaces[collider];
                var t = collider.transform;
                if ((t.position - surface.Position).sqrMagnitude < 0.0001f && Quaternion.Angle(t.rotation, surface.Rotation) < 0.5f) continue;
                surface.Position = t.position; surface.Rotation = t.rotation;
                foreach (var piece in surface.Pieces)
                {
                    if (piece.Mesh) { var m = Matrix(t.position, t.rotation); VAudio.vaMeshPrimitiveSetTransform(piece.Primitive, ref m); }
                    else { var m = Matrix(t.TransformPoint(((BoxCollider)collider).center),
                        t.rotation); VAudio.vaPrismPrimitiveSetTransform(piece.Primitive, ref m); }
                }
            }
        }

        /// <summary>What a surface is made of, from its look (the material's name) and what it is.</summary>
        internal static VAudio.Material MaterialOf(Collider collider)
        {
            var look = collider.GetComponent<Renderer>() ?? collider.GetComponentInParent<Renderer>() ?? collider.GetComponentInChildren<Renderer>();
            return MaterialNamed((look && look.sharedMaterial ? look.sharedMaterial.name : "") + " " + collider.name, collider.gameObject.layer);
        }

        internal static VAudio.Material MaterialNamed(string text, int layer)
        {
            string name = text.ToLowerInvariant();
            if (Has(name, "tile", "ceramic", "porcelain")) return VAudio.Material.Tile;
            if (Has(name, "metal", "duct", "vent", "locker", "steel", "iron", "pipe", "radiator", "grate", "fence")) return VAudio.Material.Metal;
            if (Has(name, "plaster", "stucco", "paint", "wallpaper", "corridor", "stripe")) return VAudio.Material.Gyprock;
            if (Has(name, "brick")) return VAudio.Material.Brick;
            if (Has(name, "concrete", "cement")) return VAudio.Material.Concrete;
            if (Has(name, "stone", "rock", "cobble")) return VAudio.Material.Rock;
            if (Has(name, "marble")) return VAudio.Material.Marble;
            if (Has(name, "cloth", "fabric", "curtain", "carpet", "rug", "bed", "mattress", "couch", "sofa", "pillow", "cushion")) return VAudio.Material.Cloth;
            if (Has(name, "wood", "door", "plank", "floor", "table", "desk", "chair", "shelf", "cabinet", "crate", "pew", "bench"))
                return VAudio.Material.WoodIndoor;
            if (Has(name, "glass", "window", "mirror")) return VAudio.Material.Glass;
            if (Has(name, "dirt", "soil", "mud")) return VAudio.Material.Dirt;
            if (Has(name, "grass")) return VAudio.Material.Grass;
            if (Has(name, "gravel")) return VAudio.Material.Gravel;
            if (Has(name, "leaf", "leaves", "bush", "hedge")) return VAudio.Material.Leaf;
            if (Has(name, "tree", "bark", "trunk")) return VAudio.Material.Tree;
            if (layer == LayerMask.NameToLayer("Furniture")) return VAudio.Material.WoodIndoor;
            return VAudio.Material.Gyprock; // Painted plaster: most of the school's walls and ceilings.
        }

        private static bool Has(string text, params string[] words) { foreach (var w in words) if (text.Contains(w)) return true; return false; }
        private static Vector3 Abs(Vector3 v) { return new Vector3(Mathf.Abs(v.x), Mathf.Abs(v.y), Mathf.Abs(v.z)); }
        private static VAudio.Vector Point(Vector3 v) { return new VAudio.Vector(v.x, v.y, v.z); }

        /// <summary>Position and rotation as the library's matrix (System.Numerics' layout: rows are the turned axes,
        /// the translation in the last row).</summary>
        private static VAudio.Matrix Matrix(Vector3 p, Quaternion q)
        {
            var m = UnityEngine.Matrix4x4.Rotate(q);
            return new VAudio.Matrix
            {
                M11 = m.m00, M12 = m.m10, M13 = m.m20,
                M21 = m.m01, M22 = m.m11, M23 = m.m21,
                M31 = m.m02, M32 = m.m12, M33 = m.m22,
                M41 = p.x, M42 = p.y, M43 = p.z, M44 = 1f,
            };
        }

        // ---------- Leaving ----------

        private void Shutdown()
        {
            if (!Running) return;
            VAudio.vaWorldSetPendingShutdown(world, true);
            VAudio.vaWorldWait(world);
            foreach (var speaker in speakers.Values.Concat(sounds.Values).Concat(probes.Values))
            {
                VAudio.vaWorldRemoveEmitter(world, speaker.Emitter);
                dying.Add(speaker.Emitter);
            }
            if (nunEar != IntPtr.Zero) { VAudio.vaWorldRemoveEmitter(world, nunEar); dying.Add(nunEar); nunEar = IntPtr.Zero; }
            probes.Clear();
            VAudio.vaWorldRemoveEmitter(world, listener); dying.Add(listener);
            foreach (var piece in surfaces.Values.SelectMany(x => x.Pieces)) VAudio.vaWorldRemovePrimitive_(world, piece.Primitive);
            VAudio.vaWorldUpdate(world);
            VAudio.vaWorldWait(world);
            foreach (var piece in surfaces.Values.SelectMany(x => x.Pieces).Concat(retired)) Destroy(piece);
            foreach (var emitter in dying) VAudio.vaEmitterDestroy(emitter);
            dying.Clear(); retired.Clear();
            int result = VAudio.vaWorldDestroy(world);
            log("Raytraced audio: left " + scene + (result == VAudio.Success ? "" : " (destroy " + result + ")"));
            world = listener = IntPtr.Zero;
            surfaces.Clear(); speakers.Clear(); sounds.Clear(); movers.Clear();
            scene = null;
        }

        private static int Destroy(Piece piece)
        {
            return piece.Mesh ? VAudio.vaMeshPrimitiveDestroy(piece.Primitive) : VAudio.vaPrismPrimitiveDestroy(piece.Primitive);
        }

        public void Dispose() { if (loaded) Shutdown(); }
    }
}
