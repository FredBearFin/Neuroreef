// The map's size (DESIGN §2): chosen once at startup (the setup screen's Valley, Region or Continent) before any
// world is built, and the same for every branch and the live world of a run. Field cells (FCELL) and spatial-hash
// cells (HCELL) keep their size, so a smaller map has fewer of them. Saved in checkpoints and world saves.

using System;

namespace Neuroreef;

public static class Map
{
    public static int W { get; private set; } = K.WORLD;      // side of the toroidal world (units)
    public static int FW { get; private set; } = K.FW;        // field grid side (cells)
    public static int GW { get; private set; } = K.GW;        // spatial-hash grid side (cells)

    public static readonly int[] Sizes = { 256, 512, 1024 };
    public static readonly string[] Names = { "valley", "region", "continent" };

    /// <summary>Set the map side (a multiple of HCELL). Only before any World is created.</summary>
    public static void Set(int side)
    {
        if (side < 128 || side > 2048 || side % K.HCELL != 0 || side % K.FCELL != 0)
            throw new ArgumentException($"map side {side}: must be 128..2048 and a multiple of {K.HCELL}");
        W = side; FW = side / K.FCELL; GW = side / K.HCELL;
    }

    public static string Name => Array.IndexOf(Sizes, W) is int i && i >= 0 ? Names[i] : $"{W}-unit";
}
