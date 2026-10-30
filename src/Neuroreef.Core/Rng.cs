// Counter-based random numbers (NR-104). Every draw is a pure function of (seed, step, slot, stream),
// so results never depend on thread scheduling or on the order phases are run in.
// The mixing function is the PCG hash used in the v1 shaders.

using System.Runtime.CompilerServices;

namespace Neuroreef;

public static class Rng
{
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static uint Pcg(uint v)
    {
        uint s = v * 747796405u + 2891336453u;
        uint w = ((s >> (int)((s >> 28) + 4u)) ^ s) * 277803737u;
        return (w >> 22) ^ w;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static uint Hash(uint seed, uint step, uint slot, uint stream)
        => Pcg(Pcg(Pcg(Pcg(seed ^ 0x9E3779B9u) ^ step) ^ slot) + stream * 0x85EBCA6Bu);

    /// <summary>Uniform in [0,1).</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static float U(uint seed, uint step, uint slot, uint stream)
        => (Hash(seed, step, slot, stream) >> 8) * (1f / 16777216f);

    /// <summary>Approximately N(0,1): sum of three uniforms, centred and scaled (as in v1 mutateWord).</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static float N(uint seed, uint step, uint slot, uint stream)
        => (U(seed, step, slot, stream) + U(seed, step, slot, stream + 7919u) + U(seed, step, slot, stream + 15485863u) - 1.5f) * 2f;

    /// <summary>A small sequential generator for initialisation code (terrain), seeded once.</summary>
    public struct Seq
    {
        uint _s;
        public Seq(uint seed) { _s = Pcg(seed ^ 0xA511E9B3u); }
        public float Next() { _s = Pcg(_s + 0x6D2B79F5u); return (_s >> 8) * (1f / 16777216f); }
    }
}

/// <summary>Stream identifiers, so that every use of randomness has its own sequence.</summary>
public static class Rs
{
    public const uint Wander = 1, Explore = 2, Fire = 3, Strike = 4, Death = 5, Turn = 6, Sex = 7,
        Place = 8, Mutate = 9, Cross = 10, Founder = 11, Immigrant = 12, Spore = 13, Order = 14, Act = 15,
        Archive = 16, Bank = 17, Disease = 18, Weather = 19, Wind = 20, Compat = 21, Pond = 22,
        PathX = 23, PathY = 24,   // path-integration error (Revision P)
        PlantMut = 25, PlantCross = 26, PlantFound = 27, PlantPick = 40;   // plants (Revision S); PlantPick + 0..15 are its draws
}
