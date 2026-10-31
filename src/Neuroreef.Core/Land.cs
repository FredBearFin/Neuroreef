// A world's land made once and given to other worlds (startup time). Building a world's land is most of its startup:
// the relief (Terrain.Shape: geology and erosion), then rivers, ponds and soil, the water cycle's spin-up and a year of
// plants (Simulation.SpinUp). What a world built from a Land holds is exactly what it would have built itself (the
// tests compare them), so a Land is only used where it was made for the same seeds, map and nature:
//   Shape   the relief, made by the terrain seed alone: shared by every branch of a prehistory (same land, own run seeds)
//   Grown   the whole land after the spin-up, before any animal: it also depends on the run seed (ponds, plants), so it
//           is kept for the live world that begins on the land of prehistory branch 0 (same seed, same land)

using System;
using System.IO;

namespace Neuroreef;

public sealed class Land
{
    public enum Stage : byte { Shape, Grown }

    public readonly Stage Is;
    public readonly uint TerrainSeed, Seed;
    readonly int _side;
    readonly byte[] _nature;
    readonly byte[] _data;
    readonly float[]? _rain;   // Shape: the rain the relief was worked with (Terrain.Finish routes it again)

    Land(Stage stage, World w, float[]? rain)
    {
        Is = stage; TerrainSeed = w.TerrainSeed; Seed = w.Seed; _side = Map.W; _nature = NatureNow(); _rain = rain;
        _data = Checkpoint.SaveLand(w);
    }

    static byte[] NatureNow()
    {
        var ms = new MemoryStream();
        using (var bw = new BinaryWriter(ms)) Nature.Write(bw);
        return ms.ToArray();
    }

    /// <summary>The relief of a terrain seed on the current map and nature (Terrain.Shape), in a world with no animal slots.</summary>
    public static Land Shape(uint terrainSeed)
    {
        var w = new World(1, terrainSeed, terrainSeed);
        var rain = Terrain.Shape(w);
        return new Land(Stage.Shape, w, rain);
    }

    /// <summary>The whole land of w as it is now (call after the spin-up, before founders).</summary>
    internal static Land Grown(World w) => new(Stage.Grown, w, null);

    /// <summary>Was this made for w's land on the current map and nature (and, grown, for w's run seed)?</summary>
    public bool Fits(World w) =>
        _side == Map.W && TerrainSeed == w.TerrainSeed && (Is == Stage.Shape || Seed == w.Seed) && _nature.AsSpan().SequenceEqual(NatureNow());

    /// <summary>Copy the land into w. For a Shape, returns the rain to finish it with (Terrain.Finish).</summary>
    internal float[]? Into(World w)
    {
        Checkpoint.LoadLand(w, _data);
        return _rain == null ? null : (float[])_rain.Clone();
    }

    /// <summary>Bytes held (the console's memory estimate, and tests).</summary>
    public long Bytes => _data.Length + (_rain?.Length ?? 0) * 4L;
}
