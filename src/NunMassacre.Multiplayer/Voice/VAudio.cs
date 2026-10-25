using System;
using System.Runtime.InteropServices;

namespace NunMassacre.Multiplayer.Voice
{
    /// <summary>
    /// The calls into Vercidium Audio (raytraced audio, vercidium.com), its native C library vaudionative.dll, that the
    /// voice acoustics use: a world of the level's surfaces, emitters (the listener and each voice), and what the rays
    /// work out (reverb, muffling). Only what is used is declared; vaudio.h in the SDK documents each call.
    /// Powered by Vercidium Audio.
    /// </summary>
    internal static class VAudio
    {
        internal const string Library = "vaudionative";

        // Results.
        internal const int Success = 0, ErrorInUse = 5, StillRunning = 21, Unchanged = 19;

        // Default materials (VAMaterialType).
        internal enum Material
        {
            Air = 0, Brick, Cloth, Concrete, ConcretePolished, Dirt, Glass, Grass, Gravel, Gyprock, Ice, Leaf, Marble, Metal, Mud,
            Rock, Sand, Snow, Tile, Tree, Water, WoodIndoor, WoodOutdoor,
        }

        internal enum CoordinateSystem { Default = 0, Blender, Godot, Unity, Unreal, OpenGL }

        [StructLayout(LayoutKind.Sequential)]
        internal struct Vector
        {
            public float X, Y, Z;
            public Vector(float x, float y, float z) { X = x; Y = y; Z = z; }
        }

        [StructLayout(LayoutKind.Sequential)]
        internal struct Matrix
        {
            public float M11, M12, M13, M14, M21, M22, M23, M24, M31, M32, M33, M34, M41, M42, M43, M44;
        }

        [StructLayout(LayoutKind.Sequential)]
        internal struct LowPass { public float GainLF, GainHF; }

        /// <summary>VAEAXReverb: the reverb the rays found around an emitter, as EAX/EFX reverb settings.</summary>
        [StructLayout(LayoutKind.Sequential)]
        internal struct EAXReverb
        {
            public float OutsidePercent, ReturnedPercent, MaterialAbsorptionLF, MaterialAbsorptionHF, MaterialRoughness;
            public IntPtr RelativeEmitters, RelativeDirections, RelativeGains;
            public int RelativeCount, RelativeCapacity;
            public float ReflectionsDelay;   // Seconds, 0-0.3.
            public float Density, Diffusion; // 0-1.
            public float GainLF, GainHF, Gain; // 0-1.
            public float DecayTime;          // Seconds, 0.1-20.
            public float DecayLFRatio, DecayHFRatio; // 0.1-2.
            public float ReflectionsGain;    // 0-3.16.
            public float LateReverbGain;     // 0-10.
            public float LateReverbDelay;    // Seconds, 0-0.1.
            public float EchoTime, EchoDepth, ModulationTime, ModulationDepth, AirAbsorptionGainHF, HFReference, LFReference, RoomRolloffFactor;
            public int DecayHFLimit;
        }

        [DllImport(Library)] internal static extern void vaGetVersion(out int major, out int minor, out int patch);
        [DllImport(Library)] [return: MarshalAs(UnmanagedType.I1)] internal static extern bool vaIsProduction();

        [DllImport(Library)] internal static extern Matrix vaMatrixCreateTranslation(float x, float y, float z);

        // World.
        [DllImport(Library)] internal static extern IntPtr vaWorldCreate();
        [DllImport(Library)] internal static extern int vaWorldDestroy(IntPtr world);
        [DllImport(Library)] internal static extern int vaWorldUpdate(IntPtr world);
        [DllImport(Library)] internal static extern int vaWorldWait(IntPtr world);
        [DllImport(Library)] internal static extern int vaWorldSetPendingShutdown(IntPtr world, [MarshalAs(UnmanagedType.I1)] bool value);
        [DllImport(Library)] [return: MarshalAs(UnmanagedType.I1)] internal static extern bool vaWorldGetThreadsRunning(IntPtr world);
        [DllImport(Library)] internal static extern int vaWorldSetPosition(IntPtr world, Vector position);
        [DllImport(Library)] internal static extern int vaWorldSetSize(IntPtr world, Vector size);
        [DllImport(Library)] internal static extern int vaWorldSetCoordinateSystem(IntPtr world, CoordinateSystem value);
        [DllImport(Library)] internal static extern int vaWorldSetMaximumConcurrencyLevel(IntPtr world, int value);
        [DllImport(Library)] internal static extern int vaWorldGetRaysCastThisFrame(IntPtr world);
        [DllImport(Library)] internal static extern int vaWorldSetMaterialAbsorptionLF(IntPtr world, int material, float value);
        [DllImport(Library)] internal static extern float vaWorldGetMaterialTransmissionLF(IntPtr world, int material);
        [DllImport(Library)] internal static extern float vaWorldGetMaterialTransmissionHF(IntPtr world, int material);
        [DllImport(Library)] internal static extern int vaWorldSetMaterialTransmissionLF(IntPtr world, int material, float meters);
        [DllImport(Library)] internal static extern int vaWorldSetMaterialTransmissionHF(IntPtr world, int material, float meters);
        [DllImport(Library)] internal static extern int vaWorldSetMaterialAbsorptionHF(IntPtr world, int material, float value);
        [DllImport(Library)] internal static extern double vaWorldGetRaytracingTime(IntPtr world);
        [DllImport(Library)] internal static extern double vaWorldGetMainThreadTime(IntPtr world);
        [DllImport(Library)] internal static extern int vaWorldAddPrimitive_(IntPtr world, IntPtr primitive);
        [DllImport(Library)] internal static extern int vaWorldRemovePrimitive_(IntPtr world, IntPtr primitive);
        [DllImport(Library)] internal static extern int vaWorldAddEmitter(IntPtr world, IntPtr emitter);
        [DllImport(Library)] internal static extern int vaWorldRemoveEmitter(IntPtr world, IntPtr emitter);

        // Surfaces: a triangle mesh (3 vertices a triangle), or a box.
        [DllImport(Library)] internal static extern int vaMeshPrimitiveCreate(Material material, Vector[] vertices, int vertexCount, Vector minBounds, Vector maxBounds, ref Matrix transform, out IntPtr primitive);
        [DllImport(Library)] internal static extern int vaMeshPrimitiveSetTransform(IntPtr primitive, ref Matrix transform);
        [DllImport(Library)] internal static extern int vaMeshPrimitiveDestroy(IntPtr primitive);
        [DllImport(Library)] internal static extern IntPtr vaPrismPrimitiveCreate();
        [DllImport(Library)] internal static extern int vaPrismPrimitiveSetMaterial(IntPtr prism, Material material);
        [DllImport(Library)] internal static extern int vaPrismPrimitiveSetSize(IntPtr prism, Vector size);
        [DllImport(Library)] internal static extern int vaPrismPrimitiveSetTransform(IntPtr prism, ref Matrix transform);
        [DllImport(Library)] internal static extern int vaPrismPrimitiveDestroy(IntPtr prism);

        // Emitters.
        [DllImport(Library)] internal static extern IntPtr vaEmitterCreate();
        [DllImport(Library)] internal static extern int vaEmitterDestroy(IntPtr emitter);
        [DllImport(Library)] internal static extern int vaEmitterSetPosition(IntPtr emitter, Vector position);
        [DllImport(Library)] internal static extern int vaEmitterSetReverbRayCount(IntPtr emitter, int value);
        [DllImport(Library)] internal static extern int vaEmitterSetReverbBounceCount(IntPtr emitter, int value);
        [DllImport(Library)] internal static extern int vaEmitterSetOcclusionRayCount(IntPtr emitter, int value);
        [DllImport(Library)] internal static extern int vaEmitterSetOcclusionBounceCount(IntPtr emitter, int value);
        [DllImport(Library)] internal static extern int vaEmitterSetPermeationRayCount(IntPtr emitter, int value);
        [DllImport(Library)] internal static extern int vaEmitterSetPermeationBounceCount(IntPtr emitter, int value);
        [DllImport(Library)] internal static extern int vaEmitterSetAmbientOcclusionRayCount(IntPtr emitter, int value);
        [DllImport(Library)] internal static extern int vaEmitterSetAmbientPermeationRayCount(IntPtr emitter, int value);
        [DllImport(Library)] internal static extern int vaEmitterAddTarget(IntPtr emitter, IntPtr target);
        [DllImport(Library)] internal static extern int vaEmitterRemoveTarget(IntPtr emitter, IntPtr target);
        [DllImport(Library)] internal static extern IntPtr vaEmitterGetTargetFilter(IntPtr emitter, IntPtr target);
        [DllImport(Library)] internal static extern IntPtr vaEmitterGetEAX(IntPtr emitter);
        [DllImport(Library)] internal static extern IntPtr vaEmitterGetAmbientFilter(IntPtr emitter);
        [DllImport(Library)] internal static extern float vaEmitterGetOutsidePercent(IntPtr emitter);
        [DllImport(Library)] [return: MarshalAs(UnmanagedType.I1)] internal static extern bool vaEmitterGetInitialising(IntPtr emitter);
        [DllImport(Library)] [return: MarshalAs(UnmanagedType.I1)] internal static extern bool vaEmitterGetWithinWorldBounds(IntPtr emitter);
        [DllImport(Library)] internal static extern int vaEmitterSetName(IntPtr emitter, [MarshalAs(UnmanagedType.LPStr)] string name);

        // Loading the library from the plugin's own folder (not on Windows' search path).
        [DllImport("kernel32", SetLastError = true, CharSet = CharSet.Unicode)] private static extern IntPtr LoadLibraryW(string path);

        /// <summary>Loads vaudionative.dll from this path, so the calls above find it; false (and why) if it cannot.</summary>
        internal static bool Load(string path, out string why)
        {
            why = null;
            try
            {
                if (!System.IO.File.Exists(path)) { why = "not found: " + path; return false; }
                if (LoadLibraryW(path) == IntPtr.Zero) { why = "LoadLibrary failed (" + Marshal.GetLastWin32Error() + ")"; return false; }
                int major, minor, patch;
                vaGetVersion(out major, out minor, out patch);
                Version = major + "." + minor + "." + patch + (vaIsProduction() ? "" : " dev");
                return true;
            }
            catch (Exception e) { why = e.GetType().Name + ": " + e.Message; return false; }
        }
        internal static string Version { get; private set; }

        internal static EAXReverb? EAX(IntPtr emitter)
        {
            IntPtr data = vaEmitterGetEAX(emitter);
            return data == IntPtr.Zero ? (EAXReverb?)null : (EAXReverb)Marshal.PtrToStructure(data, typeof(EAXReverb));
        }

        internal static LowPass? Filter(IntPtr data) { return data == IntPtr.Zero ? (LowPass?)null : (LowPass)Marshal.PtrToStructure(data, typeof(LowPass)); }
    }
}
