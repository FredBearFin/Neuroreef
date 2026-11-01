// The map's size (DESIGN §2): chosen once at startup (the setup screen's Valley, Region or Continent) before any
// world is built, and the same for every branch and the live world of a run. Field cells (FCELL) and spatial-hash
// cells (HCELL, HCELL_CALL) keep their size, so a smaller map has fewer of them. A unit is K.M_PER_UNIT metres. Saved in checkpoints and world saves.

using System;

namespace Neuroreef;

public static class Map
{
    public static int W { get; private set; } = K.WORLD;      // side of the toroidal world (units)
    public static int FW { get; private set; } = K.FW;        // field grid side (cells)
    public static int GW { get; private set; } = Math.Max(1, K.WORLD / K.HCELL);        // spatial-hash grid side (cells)
    public static int CGW { get; private set; } = Math.Max(1, K.WORLD / K.HCELL_CALL);   // callers' hash grid side (cells)

    public static readonly int[] Sizes = { 256, 512, 1024, 2048 };
    public static readonly string[] Names = { "valley", "region", "continent", "supercontinent" };

    /// <summary>Set the map side (a multiple of both hash cells). Only before any World is created.</summary>
    public static void Set(int side)
    {
        // (2026-11-01, the 1 km world: a hash cell may be the whole of a small map: one cell of the side)
        bool Fits(int cell) => side % cell == 0 || (side < cell && cell % side == 0);
        if (side < 128 || side > 2048 || !Fits(K.HCELL) || !Fits(K.HCELL_CALL) || side % K.FCELL != 0)
            throw new ArgumentException($"map side {side}: must be 128..2048, a power of two");
        W = side; FW = side / K.FCELL; GW = Math.Max(1, side / K.HCELL); CGW = Math.Max(1, side / K.HCELL_CALL);
        ReliefM = Relief(side);
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
