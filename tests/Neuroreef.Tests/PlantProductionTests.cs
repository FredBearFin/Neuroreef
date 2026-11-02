// Plant production against measured ecosystems (2026-11-01): a column world, a 64 x 64 field of land cells with no
// relief, water or animals, whose soil moisture and air temperature are imposed in bands (moisture by column, annual
// mean temperature by row, with a seasonal swing), runs the plants alone with the litter and wood rotting into
// nutrient (Fields.Row) for some years; then the net primary production and the standing biomass of each band are read.
// Targets (g C/m2/yr; Krebs, Ecology 6th ed., after Whittaker & Likens 1975 and Saugier, Roy & Mooney 2001): desert
// ~100-200 at most, temperate grassland 200-400, temperate forest 300-700 (to ~700), tropical evergreen ~1,000.

using System;
using System.Text;
using Xunit;
using Xunit.Abstractions;

namespace Neuroreef.Tests;

public class PlantProductionTests
{
    readonly ITestOutputHelper _out;
    public PlantProductionTests(ITestOutputHelper o) => _out = o;

    /// <summary>The bands' results: NPP (g C/m2/yr), tree cover, live wood (kg dry/m2), leafy cover (kg dry/m2).</summary>
    public sealed record Band(float W, float T, double Npp, double Tree, double Wood, double Leaf, double Herb);

    const int NW = 8, NT = 4, YEARS_SPIN = 6, YEARS_MEASURE = 2;
    static readonly float[] Ws = { 0.08f, 0.12f, 0.18f, 0.25f, 0.35f, 0.5f, 0.7f, 0.9f };
    static readonly float[] Ts = { 2f, 8f, 15f, 25f };
    // the seasonal half-range of the air temperature (K) of each band: wide in the cold (boreal and continental), narrow in
    // the tropics, as on Earth (round 2, 2026-11-01; was 6 K everywhere, which gave the 15 °C band no winter)
    static readonly float[] Swing = { 14f, 12f, 9f, 3f };

    /// <summary>Run the column world and read each band (the band's edge columns and rows, where seed crosses from the
    /// next band, are left out).</summary>
    public static Band[] Run(uint seed = 7, int spin = YEARS_SPIN, int measure = YEARS_MEASURE)
    {
        var w = new World(64, seed);
        Terrain.Build(w);
        int FW = Map.FW, n = FW * FW, bw = FW / NW, bh = FW / NT;
        float[] G = w.Pool[(int)Pool.Grass];
        for (int i = 0; i < n; i++)
        {
            int x = i % FW, y = i / FW;
            w.Cell[i] = (byte)Realm.Land; w.WetDur[i] = 0f; w.Peat[i] = 0f; w.Depth[i] = 0f;
            w.Moisture[i] = Ws[x / bw]; w.Temp[i] = Ts[y / bh];
            G[i] = 0.3f; w.Shrub[i] = 0.1f; w.Tree[i] = 0.1f; w.Sapling[i] = 0.05f; w.Nutrient[i] = 0.5f; w.NutrientN[i] = 0.5f;
        }
        Plants.Found(w);
        int L = Nature.SeasonLength;
        float[] prod0 = null!;
        double[] tree = new double[n], wood = new double[n], leaf = new double[n], herb = new double[n]; int samples = 0;
        long total = (long)(spin + measure) * L;
        for (long s = 0; s < total; s++)
        {
            for (int y = 0; y < FW; y++) Fields.Row(w, y);
            Fields.Swap(w);
            float sw = MathK.Sin(2f * MathF.PI * (float)(w.Step % (ulong)L) / L);
            for (int i = 0; i < n; i++) { int bt = i / FW / bh; w.Temp[i] = Ts[bt] + Swing[bt] * sw; w.Moisture[i] = Ws[i % FW / bw]; }
            Plants.BeginStep(w);
            for (int k = 0; k < Plants.StripeRows; k++) Plants.StripeRow(w, k);
            Plants.Commit(w);
            w.Step++;
            if (s == (long)spin * L - 1) prod0 = (float[])w.Flora.CellProd.Clone();
            if (s >= (long)spin * L && s % 100 == 0)
            {
                samples++;
                for (int i = 0; i < n; i++) { tree[i] += w.Tree[i]; wood[i] += LiveWoodOf(w, i); leaf[i] += G[i] + w.Shrub[i] + w.Tree[i] + w.Sapling[i]; herb[i] += G[i]; }
            }
        }
        var res = new Band[NW * NT];
        for (int bt = 0; bt < NT; bt++)
            for (int bwi = 0; bwi < NW; bwi++)
            {
                double npp = 0, tr = 0, wo = 0, le = 0, he = 0; int c = 0;
                for (int y = bt * bh + 2; y < (bt + 1) * bh - 2; y++)
                    for (int x = bwi * bw + 2; x < (bwi + 1) * bw - 2; x++)
                    {
                        int i = y * FW + x; c++;
                        npp += (w.Flora.CellProd[i] - prod0[i]) / measure * K.PLANT_KG_M2 * 1000 * 0.45;
                        tr += tree[i] / samples; wo += wood[i] / samples * K.PLANT_KG_M2; le += leaf[i] / samples * K.PLANT_KG_M2; he += herb[i] / samples;
                    }
                res[bt * NW + bwi] = new Band(Ws[bwi], Ts[bt], npp / c, tr / c, wo / c, le / c, he / c);
            }
        return res;
    }

    static float LiveWoodOf(World w, int i) => w.LiveWood[i];

    public static string Table(Band[] r)
    {
        var sb = new StringBuilder("NPP gC/m2/yr [tree cover | live wood kg/m2 | leafy kg/m2] by annual T (rows; seasonal swing 14, 12, 9, 3 K) and soil moisture (columns)\n  T\\W  ");
        foreach (var x in Ws) sb.Append($"{x,22:F2}");
        sb.AppendLine();
        for (int bt = 0; bt < NT; bt++)
        {
            sb.Append($"{Ts[bt],5:F0}  ");
            for (int bwi = 0; bwi < NW; bwi++) { var b = r[bt * NW + bwi]; sb.Append($"{b.Npp,6:F0} [{b.Tree,4:F2}|{b.Wood,4:F1}|{b.Leaf,4:F2}]".PadLeft(22)); }
            sb.AppendLine();
        }
        return sb.ToString();
    }

    [Fact, Trait("Category", "Slow")]
    public void ProbeProductionByClimate()
    {
        int spin = int.TryParse(Environment.GetEnvironmentVariable("NR_PROBE_YEARS"), out var y) ? y : YEARS_SPIN;
        var r = Run(spin: spin);
        _out.WriteLine(Table(r));
        Assert.Equal(NW * NT, r.Length);
    }
}
