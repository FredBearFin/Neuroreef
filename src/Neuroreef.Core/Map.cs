// The map's size (DESIGN §2): chosen once at startup (the setup screen's Valley, Region or Continent) before any
// world is built, and the same for every branch and the live world of a run. Field cells (FCELL) keep their size, so a
// smaller map has fewer of them. The spatial-hash cells (HCell, HCellCall) are K.HCELL and K.HCELL_CALL at the default
// body sizes; a world of bigger bodies (BodySize) sees and calls farther, and its cells are the smallest divisors of the
// side that hold the largest perception and the loudest call (2026-11-01). A unit is K.M_PER_UNIT metres. Saved in
// checkpoints and world saves.

using System;

namespace Neuroreef;

public static class Map
{
    public static int W { get; private set; } = K.WORLD;      // side of the toroidal world (units)
    public static int FW { get; private set; } = K.FW;        // field grid side (cells)
    public static int GW { get; private set; } = K.GW;        // spatial-hash grid side (cells)
    public static int CGW { get; private set; } = K.WORLD / K.HCELL_CALL;   // callers' hash grid side (cells)
    /// <summary>Bodies' hash cell side (units): its 7x7 cells hold a step's perception (MaxPerceive). K.HCELL at the default sizes.</summary>
    public static int HCell { get; private set; } = K.HCELL;
    /// <summary>Callers' hash cell side (units): its 5x5 cells hold the loudest call (BodySize.MaxHear). K.HCELL_CALL at the default sizes.</summary>
    public static int HCellCall { get; private set; } = K.HCELL_CALL;
    /// <summary>The farthest a step's perception reaches (Decide.Perceive): the 7x7 hash neighbourhood, 3 x HCell (K.MAX_PERCEIVE at the default sizes).</summary>
    public static float MaxPerceive { get; private set; } = K.MAX_PERCEIVE;

    public static readonly int[] Sizes = { 256, 512, 1024, 2048 };
    public static readonly string[] Names = { "valley", "region", "continent", "supercontinent" };

    /// <summary>Set the map side (a multiple of K.HCELL_CALL). Only before any World is created. The hash cells follow
    /// the body sizes (BodySize): each is the smallest divisor of the side at least as big as the sizes need (the whole
    /// side if they need more: then every animal is a neighbour of every other).</summary>
    public static void Set(int side)
    {
        if (side < 128 || side > 2048 || side % K.HCELL != 0 || side % K.HCELL_CALL != 0 || side % K.FCELL != 0)
            throw new ArgumentException($"map side {side}: must be 128..2048 and a multiple of {K.HCELL_CALL}");
        int hc = Divisor(side, BodySize.CellNeed), cc = Divisor(side, Math.Max(BodySize.CallCellNeed, hc));
        W = side; FW = side / K.FCELL; HCell = hc; HCellCall = cc; GW = side / hc; CGW = side / cc;
        MaxPerceive = 3 * hc;
    }

    /// <summary>The smallest divisor of side that is at least need (side itself if none is smaller).</summary>
    static int Divisor(int side, int need)
    {
        for (int d = Math.Max(1, need); d < side; d++) if (side % d == 0) return d;
        return side;
    }

    /// <summary>Metres across the map (K.M_PER_UNIT a unit).</summary>
    public static float WidthM => W * K.M_PER_UNIT;

    public static string Name => Array.IndexOf(Sizes, W) is int i && i >= 0 ? Names[i] : $"{W}-unit";
}
