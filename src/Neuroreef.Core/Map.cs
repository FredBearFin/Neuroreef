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
    public static int GW { get; private set; } = Math.Max(1, K.WORLD / K.HCELL);        // spatial-hash grid side (cells)
    public static int CGW { get; private set; } = Math.Max(1, K.WORLD / K.HCELL_CALL);   // callers' hash grid side (cells)
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
        // (2026-11-01, the 1 km world: a hash cell may be the whole of a small map: one cell of the side)
        bool Fits(int cell) => side % cell == 0 || (side < cell && cell % side == 0);
        if (side < 128 || side > 2048 || !Fits(K.HCELL) || !Fits(K.HCELL_CALL) || side % K.FCELL != 0)
            throw new ArgumentException($"map side {side}: must be 128..2048, a power of two");
        int hc = Divisor(side, BodySize.CellNeed), cc = Divisor(side, Math.Max(BodySize.CallCellNeed, hc));
        W = side; FW = side / K.FCELL; HCell = hc; HCellCall = cc; GW = side / hc; CGW = side / cc;
        MaxPerceive = 3 * hc;
        ReliefM = Relief(side);
    }

    /// <summary>The smallest divisor of side that is at least need (side itself if none is smaller).</summary>
    static int Divisor(int side, int need)
    {
        for (int d = Math.Max(1, need); d < side; d++) if (side % d == 0) return d;
        return side;
    }

    /// <summary>Metres across the map (K.M_PER_UNIT a unit).</summary>
    public static float WidthM => W * K.M_PER_UNIT;

    /// <summary>The vertical scale of this map (m, 2026-11-01, the 1 km world): Elev (1 - SeaLevel) above the lakes is
    /// this many metres, and the highest ground stands at most this high above them (Geology). It is the relief a
    /// landscape holds over a window of the map's width: relief grows with the window as width^RELIEF_HURST
    /// (self-affine topography), reaching an orogen's H_RELIEF over RELIEF_L_KM. 153 m on the 256 map (1 km), 257 m on
    /// 512, 431 m on 1024 and 725 m on 2048 (8 km). Climate's lapse rate, Hydrology, the caves, Explore and the 3D
    /// view (RELIEF = ReliefM x FCELL / GEO_CELL_M units, from meta.hRelief) all read heights through it.</summary>
    public static float ReliefM { get; private set; } = Relief(K.WORLD);

    /// <summary>The vertical scale of a map of side units (ReliefM).</summary>
    public static float Relief(int side) =>
        K.H_RELIEF * MathF.Min(1f, MathK.Pow(side * K.M_PER_UNIT * 1e-3f / K.RELIEF_L_KM, K.RELIEF_HURST));

    /// <summary>A field cell's area (km2): drainage areas in km2 are this many cells (GEO_CELL_M a side).</summary>
    public const float CellKm2 = K.GEO_CELL_M * K.GEO_CELL_M * 1e-6f;

    /// <summary>The span of latitude across the map (degrees): its true north-south extent, 111.2 km a degree (0.01° on
    /// the 256 map, 0.07° on 2048), so a map sits at one latitude (Climate.Lat) unless Nature.LatSpan asks for more.</summary>
    public static float LatSpanDeg => Nature.LatSpan > 0f ? Nature.LatSpan : WidthM / 111195f;

    public static string Name => Array.IndexOf(Sizes, W) is int i && i >= 0 ? Names[i] : $"{W}-unit";
}
