// Climate and body heat (Revision O): temperature from latitude, season, height and water; plants, soil and ponds
// answering to it; an endotherm's heat budget from its body's physics; the senses and steering that go with it.

using System;
using Xunit;

namespace Neuroreef.Tests;

public class ClimateTests
{
    /// <summary>Run with another climate strength, restoring the default after.</summary>
    static T With<T>(float climate, Func<T> f)
    {
        float old = Nature.Climate;
        Nature.Climate = climate;
        try { return f(); } finally { Nature.Climate = old; }
    }
    static void With(float climate, Action f) => With(climate, () => { f(); return 0; });

    internal static ulong FieldsHash(World w)
    {
        ulong h = 14695981039346656037UL;
        void Mix(float v) { h ^= (uint)BitConverter.SingleToInt32Bits(v); h *= 1099511628211UL; }
        int n = Map.FW * Map.FW;
        for (int c = 0; c < n; c++)
        {
            for (int p = 0; p < Pools.NFIELD; p++) Mix(w.Pool[p][c]);
            Mix(w.Tree[c]); Mix(w.Shrub[c]); Mix(w.Sapling[c]); Mix(w.Nutrient[c]); Mix(w.Moisture[c]); Mix(w.Fire[c]); Mix(w.DistDrink[c]);
            h ^= w.Wet[c]; h *= 1099511628211UL;
        }
        return h;
    }

    [Fact] // Climate = 0 is the world without a climate, bit for bit: the hash of a year of fields made by Revision N's code
    [BigWorld]
    public void NoClimateIsBitIdenticalToBefore()
    {
        ulong h = With(0f, () =>
        {
            using var sim = new Simulation(64, 4, 1, empty: true);
            for (int k = 0; k < 3000; k++) sim.Step();
            for (int c = 0; c < Map.FW * Map.FW; c++) Assert.Equal(K.T_REF, sim.W.Temp[c]);
            return FieldsHash(sim.W);
        });
        // The water cycle (2026-10-30) re-pinned it: soil moisture is the soil's water over its capacity, ponds and floods
        // follow the water's stores, and the world spins up two years of weather before it starts. It was
        // 0x47BA1DC94894B8A4 at Revision R (geology).
        // Revision R (geology) re-pinned it: the land is now formed by plates and deep-time erosion instead of fBm noise,
        // with rock and parent-material fertility, so the same seed grows different fields; Climate = 0 still skips every
        // climate rule. It was 0x52A982E9B31E42C1 at Revision Q (hydrology, commit 4583355), 0x18289DE28D12409D at
        // Revision P with continuous water depth (commit dde4d60), and 0x3BF8C06B3FB53E61 at Revision N (commit 2baed5e);
        // same seed, map and steps.
        // Re-pinned when all simulation math moved to MathK (NR-104): it was 0x3EBEA675E685E2C7 on Linux and differed on
        // Windows, because MathF.Pow/Exp/Sin in the geology, water cycle and climate gave last-digit differences between them.
        Assert.Equal(0xD1A92250AB690304UL, h);
    }

    [Fact] // temperature: periodic on the torus, symmetric about the equator with opposite seasons, 6.5 K per 1000 m, damped by water
    public void TemperatureFollowsLatitudeSeasonHeightAndWater()
    {
        With(1f, () =>
        {
            var w = new World(16, 3);
            Terrain.Build(w);
            int FW = Map.FW;
            // equator 27 °C, poles 27 - 40 at sea level (annual mean)
            Climate.RowParams(w, 0, out float a0, out _);
            Climate.RowParams(w, FW / 4, out float aN, out _);
            Assert.Equal(K.T_EQ, a0, 3);
            Assert.Equal(K.T_EQ - K.DT_EP, aN, 3);
            w.Step = (ulong)(Nature.SeasonLength / 4);   // the northern summer
            for (int d = 1; d < FW / 2; d += 7)
            {
                Climate.RowParams(w, d, out float an, out float bn);
                Climate.RowParams(w, FW - d, out float aS, out float bs);
                Assert.Equal(an, aS, 3);                        // symmetric about the equator
                Assert.Equal(bn, -bs, 3);                       // opposite seasons
                Assert.True(bn > 0f, $"row {d}: northern summer swing {bn}");
                Climate.RowParams(w, d + FW / 2, out float a2, out float b2);   // the other half of the torus: equator, south pole
                Assert.Equal(an, a2, 3);
            }
            // periodic: the row after the last is the first
            Climate.RowParams(w, FW, out float aw, out float bw); Climate.RowParams(w, 0, out float az, out float bz);
            Assert.Equal(az, aw, 3); Assert.Equal(bz, bw, 3);
            // height: 6.5 K per 1000 m
            int hi = -1, lo = -1;
            for (int i = 0; i < FW * FW; i++)
            {
                if (w.Cell[i] != (byte)Realm.Land) continue;
                if (hi < 0 || w.Elev[i] > w.Elev[hi]) hi = i;
                if (lo < 0 || w.Elev[i] < w.Elev[lo]) lo = i;
            }
            float m(int i) => (w.Elev[i] - w.SeaLevel) / (1f - w.SeaLevel) * K.H_RELIEF;
            Assert.Equal(K.LAPSE * (m(hi) - m(lo)) / 1000f, w.TLapse[hi] - w.TLapse[lo], 2);
            Assert.True(w.TLapse[hi] > 15f, $"the highest ground is only {w.TLapse[hi]:F1} K cooler");
            // the seasons swing less by the water: in the same row, a shore cell against one far inland
            int best = -1, bestRow = -1; float spread = 0;
            for (int y = FW / 8; y < FW / 4; y++)
            {
                float cmin = 2, cmax = -1;
                for (int x = 0; x < FW; x++) { int i = y * FW + x; if (w.Cell[i] != (byte)Realm.Land) continue; cmin = MathF.Min(cmin, w.Cont[i]); cmax = MathF.Max(cmax, w.Cont[i]); }
                if (cmax - cmin > spread) { spread = cmax - cmin; bestRow = y; }
            }
            Assert.True(spread > 0.4f, $"continentality hardly varies ({spread:F2})");
            int near = -1, far = -1;
            for (int x = 0; x < FW; x++)
            {
                int i = bestRow * FW + x; if (w.Cell[i] != (byte)Realm.Land) continue;
                if (near < 0 || w.Cont[i] < w.Cont[near]) near = i;
                if (far < 0 || w.Cont[i] > w.Cont[far]) far = i;
            }
            float Swing(int i)
            {
                w.Step = (ulong)(Nature.SeasonLength / 4); Climate.TempRow(w, bestRow); float s = w.Temp[i] + w.TLapse[i];
                w.Step = (ulong)(3 * Nature.SeasonLength / 4); Climate.TempRow(w, bestRow); return s - (w.Temp[i] + w.TLapse[i]);
            }
            float sn = Swing(near), sf = Swing(far);
            Assert.True(sf > 0f && sn > 0f && sn < 0.7f * sf, $"summer-winter swing by the water {sn:F1} K, inland {sf:F1} K");
            best = near; Assert.True(best >= 0);
        });
    }

    [Fact] // the thermoneutral zone: no heat cost between the critical temperatures; bigger and furrier bodies tolerate colder,
           // appendages let a body shed heat in warmer air; water conducts 4x; a bare M=1 body is neutral down to 22 °C
    public void HeatBudgetFollowsTheBody()
    {
        float h1 = K.C_BASAL;   // a body of M=1 making its basal heat
        var (bareLo, _) = Heat.Neutral(1f, 0f, 0f, false, h1);
        Assert.Equal(22f, bareLo, 3);
        var (fLo, fHi) = Heat.Neutral(1f, K.FOUND_INS, K.FOUND_APP, false, h1);
        Assert.InRange(fLo, 12f, 17f); Assert.InRange(fHi, 28f, 33f);     // founders: thermoneutral about 15-32 °C
        float H(float m) => K.C_BASAL * MathF.Pow(m, K.EXP_BASAL);
        Assert.True(Heat.Neutral(8f, 0.15f, 0.3f, false, H(8f)).lower < Heat.Neutral(1f, 0.15f, 0.3f, false, H(1f)).lower, "a bigger body does not tolerate more cold");
        Assert.True(Heat.Neutral(1f, 0.8f, 0.3f, false, h1).lower < Heat.Neutral(1f, 0.1f, 0.3f, false, h1).lower, "fur does not keep heat in");
        Assert.True(Heat.Neutral(1f, 0.15f, 0.9f, false, h1).upper > Heat.Neutral(1f, 0.15f, 0.1f, false, h1).upper, "appendages do not let a body shed heat in warmer air");
        Heat.Conductance(1f, 0f, 0.3f, false, out float dmin, out float dmax);
        Heat.Conductance(1f, 0f, 0.3f, true, out float wmin, out float wmax);
        Assert.Equal(4f, wmin / dmin, 4); Assert.Equal(4f, wmax / dmax, 4);
        // inside the zone nothing is spent; below it, heat is made; above it, water is evaporated
        float mid = 0.5f * (fLo + fHi);
        Heat.Balance(1f, K.FOUND_INS, K.FOUND_APP, mid, false, h1, h1, out float th, out float ev, out float c, out float hot);
        Assert.Equal(0f, th); Assert.Equal(0f, ev); Assert.Equal(0f, c); Assert.Equal(0f, hot);
        Heat.Balance(1f, K.FOUND_INS, K.FOUND_APP, fLo - 2f, false, h1, h1, out th, out ev, out _, out _);
        Assert.True(th > 0f && ev == 0f);
        Heat.Balance(1f, K.FOUND_INS, K.FOUND_APP, fHi + 2f, false, h1, h1, out th, out ev, out _, out _);
        Assert.True(ev > 0f && th == 0f);
        // beyond the caps: a shortfall
        Heat.Balance(1f, 0f, 0f, -60f, false, h1, h1, out th, out _, out c, out _);
        Assert.Equal(K.SUMMIT * h1, th, 6); Assert.True(c > 0f);
        // immersed: no sweating under water
        Heat.Balance(1f, 0f, 1f, 45f, true, h1, h1, out _, out ev, out _, out hot);
        Assert.Equal(0f, ev); Assert.True(hot > 0f);
    }

    /// <summary>One adult walker standing still in cell 0 of a bare world with this air temperature.</summary>
    static (World w, int i) Animal(float temp, sbyte waterGene = -127, sbyte fur = -89, sbyte mass = 0, sbyte endo = 127)
    {
        var w = new World(8, 1);
        Array.Fill(w.Temp, temp);
        Array.Fill(w.Moisture, 1f);
        int i = Life.NewSlot(w);
        Brain.RandomGenome(w.GenomeOf(i), 1, 0, 1);
        int g = i * G.LEN;
        w.Genome[g + G.B_LEGS] = 100; w.Genome[g + G.B_FINS] = -127; w.Genome[g + G.B_WINGS] = -127;
        w.Genome[g + G.B_WATER] = waterGene; w.Genome[g + G.B_INSUL] = fur; w.Genome[g + G.B_APPEND] = -51; w.Genome[g + G.B_MASS] = mass; w.Genome[g + G.B_ENDO] = endo;   // warm-blooded unless asked
        w.Spc[i] = 0; w.StageOf[i] = St.Adult; w.Sp[0].Used = true; w.Sp[0].Alive = true;
        Body.Express(w, i); Brain.InitLife(w, i);
        w.X[i] = 1f; w.Y[i] = 1f; w.Energy[i] = w.EmaxOf(i); w.Age[i] = 1f;
        w.IActs[i] = 0; w.ITarget[i] = -1; w.IMate[i] = -1; w.IHeading[i] = 0; w.ISpeed[i] = 0;
        return (w, i);
    }

    [Fact] // the heat made in the cold is paid from energy, exactly; in the zone nothing is
    public void ThermogenesisIsChargedToEnergy()
    {
        foreach (float t in new[] { -20f, 20f })
        {
            var (w, i) = Animal(t);
            float e0 = w.Energy[i];
            Resolve.Run(w, 1, 0);
            double spent = w.T.SpendBasal[0] + w.T.SpendMove[0] + w.T.SpendGrowth[0] + w.T.SpendHeat[0];
            Assert.Equal(e0 - spent, w.Energy[i], 4);
            if (t < 0) Assert.True(w.T.SpendHeat[0] > 0.1 * w.T.SpendBasal[0], $"heat {w.T.SpendHeat[0]} against basal {w.T.SpendBasal[0]}");
            else Assert.Equal(0.0, w.T.SpendHeat[0]);
            Assert.True(t < 0 ? w.HeatNeed[i] > 0f : w.HeatNeed[i] == 0f);
        }
    }

    [Fact] // Revision P: a cold-blooded body pays nothing to keep warm, but in the cold it burns less, digests less and is
           // slower (metabolic theory: x exp(E/k (1/T_BODY - 1/T))); a warm-blooded one pays heat; at T_BODY both are alike
    public void ColdBloodedBodiesFollowTheirSurroundings()
    {
        With(K.CLIMATE, () =>
        {
            Assert.Equal(1f, Heat.Rate(K.T_BODY), 5);
            Assert.InRange(Heat.Rate(20f), 0.18f, 0.28f);                    // ~0.23 at 20 °C (E = 0.65 eV)
            Assert.Equal(10f, Heat.Core(10f, 0f)); Assert.Equal(K.T_BODY, Heat.Core(10f, 1f)); Assert.Equal(K.T_BODY, Heat.Core(45f, 0f));
            (double basal, double heat, float gut) Run(sbyte endo)
            {
                var (w, i) = Animal(-20f, endo: endo);
                w.Gut[i] = 1f; w.GutEnergy[i] = 1f;
                Resolve.Run(w, 1, 0);
                return (w.T.SpendBasal[0], w.T.SpendHeat[0], w.Gut[i]);
            }
            var cold = Run(-127); var warm = Run(127);
            Assert.Equal(0.0, cold.heat);
            Assert.True(warm.heat > 0.0, "a warm-blooded body at -20 °C made no heat");
            Assert.InRange(cold.basal / warm.basal, 0.02, 0.15);                  // ~0.05 near freezing
            Assert.True(cold.gut > warm.gut, $"the cold gut digested as fast ({cold.gut} vs {warm.gut})");
        });
    }

    [Fact] // sweating makes an animal thirsty only above its upper critical temperature, and the water-economy gene does not save it
    public void EvaporationCostsWaterWhateverTheKidneys()
    {
        (float extra, float expect) Extra(float temp, sbyte water)
        {
            var (w, i) = Animal(temp, water);
            w.Thirst[i] = 0f;
            float dry = MathF.Min(1f, K.C_THIRST * Nature.Thirst * w.PThirstK[i] * MathF.Max(0f, 1f - K.SOIL_WATER * w.Moisture[0]));
            float b = w.PBasal[i], top = Heat.Operative(w, 0, w.PInsul[i], false);
            Heat.Balance(w.MassOf(i), w.PInsul[i], w.PAppend[i], top, false, b, b, out _, out float x, out _, out _);
            Resolve.Run(w, 1, 0);
            return (w.Thirst[i] - dry, K.EVAP_REL * K.C_THIRST * Nature.Thirst * x / (K.C_BASAL * w.MassOf(i)));
        }
        Assert.Equal(0f, Extra(20f, -127).extra, 6);
        var lean = Extra(45f, -127); var econ = Extra(45f, 127);
        Assert.True(lean.extra > 0f, $"no evaporative thirst at 45 °C ({lean.extra})");
        Assert.Equal(lean.expect, lean.extra, 6); Assert.Equal(econ.expect, econ.extra, 6);
        Assert.True(econ.extra >= lean.extra * 0.999f, $"the water-economy gene saved sweat: {econ.extra} against {lean.extra}");
    }

    [Fact] // heat strain kills by cold when the body cannot make enough heat
    public void ColdKills()
    {
        var (w, i) = Animal(-250f, -127, -127, -127);
        for (int k = 0; k < 40 && w.StageOf[i] != St.Dead; k++) { w.Energy[i] = w.EmaxOf(i); w.IActs[i] = 0; w.ISpeed[i] = 0; Resolve.Run(w, 1, 0); }
        Assert.Equal(St.Dead, w.StageOf[i]);
        Assert.Equal(1, w.T.Deaths[0 * Cause.N + Cause.Cold]);
    }

    [Fact] // plants: each layer grows only between its lowest and highest temperature; at T_REF the curves are 1
    public void PlantsGrowOnlyAboveTheirLowestTemperature()
    {
        int r = Climate.Idx(K.T_REF);
        Assert.Equal(1f, Climate.FGrass[r]); Assert.Equal(1f, Climate.FShrub[r]); Assert.Equal(1f, Climate.FTree[r]);
        Assert.Equal(1f, Climate.Rot[r]); Assert.Equal(1f, Climate.Evap[r], 6); Assert.Equal(1f, Climate.Algae[r], 6);
        Assert.Equal(0f, Climate.FTree[Climate.Idx(K.TREE_TMIN - 0.5f)]);
        Assert.Equal(0f, Climate.FGrass[Climate.Idx(K.GRASS_TMIN - 0.5f)]);
        Assert.Equal(0f, Climate.FShrub[Climate.Idx(K.SHRUB_TMAX + 1f)]);
        for (int k = 0; k < K.TT_N; k++) Assert.True(Climate.FGrass[k] <= K.PLANT_CAP && Climate.FTree[k] <= K.PLANT_CAP);
        Assert.True(Climate.Evap[Climate.Idx(0f)] > 1.5f && Climate.Evap[Climate.Idx(35f)] < 0.7f, "evaporation does not follow vapour pressure");
        // in the field: a land cell made very cold grows no grass
        With(K.CLIMATE, () =>
        {
            var w = new World(8, 2);
            Terrain.Build(w);
            int c = -1;
            for (int i = 0; i < Map.FW * Map.FW && c < 0; i++) if (w.Cell[i] == (byte)Realm.Land && w.Pool[(int)Pool.Grass][i] > 0.1f && w.Pool[(int)Pool.Grass][i] < 0.6f) c = i;
            Assert.True(c >= 0);
            w.TLapse[c] = 60f;   // far below freezing
            float g0 = w.Pool[(int)Pool.Grass][c];
            Fields.Row(w, c / Map.FW);
            Assert.True(w.Temp[c] < K.GRASS_TMIN, $"cell at {w.Temp[c]:F1} °C");
            Assert.True(w.GrassN[c] <= g0, $"grass grew from {g0} to {w.GrassN[c]} at {w.Temp[c]:F1} °C");
        });
    }

    [Fact] // a fields-only world: trees thin out with height where it is cold; the climate moves no water in or out of the land
    public void VegetationFollowsTemperatureAndWaterIsConserved()
    {
        (double moist, double rain, double et, double residual) Run(float climate) => With(climate, () =>
        {
            using var sim = new Simulation(64, 9, 2, empty: true);
            var w = sim.W; double m = 0; int samples = 0;
            double store0 = WaterCycle.LandStore(w);
            for (int k = 0; k < 6000; k++)
            {
                sim.Step();
                if (k >= 3000 && k % 50 == 0)
                {
                    double s = 0; int n = 0;
                    for (int i = 0; i < Map.FW * Map.FW; i++) if (w.Cell[i] == (byte)Realm.Land) { s += w.Moisture[i]; n++; }
                    m += s / n; samples++;
                }
            }
            double res = w.WbRain - w.WbET - w.WbPondEvap - w.WbToLakes - (WaterCycle.LandStore(w) - store0);
            return (m / samples, w.WbRain, (w.WbET + w.WbPondEvap) / w.WbRain, res / w.WbRain);
        });
        // the water cycle (2026-10-30) replaced the normaliser that kept the soil as wet with a climate as without one: now
        // the same rain falls with and without a climate (where it falls follows the winds, not the temperature), every
        // drop of it is accounted for (rain = evapotranspiration + flow to the lakes + what the land holds), and what the
        // climate changes is how much of it warm air takes back: at the default strength the world is cooler than T_REF
        // on average, so less evaporates and the soil is wetter
        var off = Run(0f); var on = Run(K.CLIMATE);
        Assert.Equal(1.0, on.rain / off.rain, 6);
        Assert.True(Math.Abs(on.residual) < 1e-5 && Math.Abs(off.residual) < 1e-5, $"budget off by {on.residual:E1} / {off.residual:E1} of the rain");
        Assert.True(on.et < off.et, $"evaporated share {on.et:P1} with the (cooler) climate, {off.et:P1} at T_REF everywhere");
        Assert.True(on.moist > off.moist, $"moisture {on.moist:F3} with the climate, {off.moist:F3} without");
        // a treeline: on land made alike everywhere (the same wet soil and sparse young woodland, no fire and no drought
        // years), at fixed latitude, trees spread less on high ground with a climate, and alike without one
        double Treeline(float climate) => With(climate, () =>
        {
            float fire = Nature.Fire, drought = Nature.Drought; Nature.Fire = 0f; Nature.Drought = 0f;
            try
            {
                using var sim = new Simulation(64, 9, 2, empty: true);
                var w = sim.W; int FW = Map.FW;
                for (int i = 0; i < FW * FW; i++)
                {
                    if (w.Cell[i] != (byte)Realm.Land) continue;
                    w.Rain[i] = 4f; w.SoilWater[i] = w.SoilCap[i]; w.Fert[i] = 0.7f; w.Nutrient[i] = 0.55f;   // wet soil: rain enough to keep it full
                    w.Tree[i] = 0.05f; w.Sapling[i] = 0.05f; w.Shrub[i] = 0.2f; w.Pool[(int)Pool.Grass][i] = 0.3f;
                }
                for (int k = 0; k < 6000; k++) sim.Step();
                var cells = new System.Collections.Generic.List<int>();
                for (int y = 0; y < FW; y++)
                {
                    float s = MathF.Abs(MathF.Sin(2f * MathF.PI * y / FW));
                    if (s < 0.5f || s > 0.7f) continue;
                    for (int x = 0; x < FW; x++) { int i = y * FW + x; if (w.Cell[i] == (byte)Realm.Land) cells.Add(i); }
                }
                Assert.True(cells.Count > 300, $"{cells.Count} land cells in the band");
                cells.Sort((a, b) => w.Elev[a].CompareTo(w.Elev[b]));
                int third = cells.Count / 3; double lo = 0, hi = 0;
                for (int k = 0; k < third; k++) { lo += w.Tree[cells[k]]; hi += w.Tree[cells[cells.Count - 1 - k]]; }
                return hi / Math.Max(1e-9, lo);
            }
            finally { Nature.Fire = fire; Nature.Drought = drought; }
        });
        double flat = Treeline(0f), line = Treeline(1f);   // Earth-like strength: the physics, not the default setting
        Assert.InRange(flat, 0.9, 1.1);
        Assert.True(line < 0.7, $"high/low tree cover with a climate {line:F3} (without {flat:F3})");
    }

    [Fact] // a frozen pond cannot be drunk
    [BigWorld]
    public void FrozenPondsAreNotDrinkable()
    {
        With(K.CLIMATE, () =>
        {
            using var sim = new Simulation(64, 4, 1, empty: true);
            var w = sim.W;
            // the ponds the spin-up years filled (WaterCycle.Init)
            Array.Fill(w.Temp, 10f);
            Ponds.Update(w);
            int full = Ponds.Full(w);
            Assert.True(full > 0, "no full ponds in a mild year");
            Array.Fill(w.Temp, -5f);
            Ponds.Update(w);
            Assert.Equal(0, Ponds.Full(w));
            for (int c = 0; c < Map.FW * Map.FW; c++) Assert.Equal(w.DistWater[c], w.DistDrink[c]);
        });
    }

    [Fact] // the recognition genes stay the last body genes; fur and appendages come before them
    public void MhcStaysLast()
    {
        Assert.Equal(G.BODY + G.NBODY, G.B_MHC + G.NMHC);
        Assert.True(G.B_INSUL < G.B_MHC && G.B_APPEND < G.B_MHC && G.B_APPEND == G.B_INSUL + 1 && G.B_ENDO < G.B_MHC);
        Assert.Equal(F.N, F.Names.Length); Assert.Equal(P.N, P.Names.Length);
        Assert.Equal("WARMER", P.Names[P.WARMER]); Assert.Equal("COOLER", P.Names[P.COOLER]);
        Assert.True(P.COOLER < P.NSTEER && P.HOLD >= P.NSTEER && P.EAT == P.FIRST_ACTION);
        for (int f = 1; f < F.N; f++) Assert.False(string.IsNullOrEmpty(Meaning.High[f]), $"no phrase for {F.Names[f]}");
        for (int p = 0; p < P.N; p++) Assert.False(string.IsNullOrEmpty(Meaning.Verbs[p]), $"no verb for {P.Names[p]}");
    }

    [Fact] // the heat senses stay in -1..1 in a living world with a strong climate
    public void HeatSensesAreBounded()
    {
        With(1.5f, () =>
        {
            using var sim = new Simulation(2048, 13, 2);
            for (int k = 0; k < 60; k++) sim.Step();
            var w = sim.W; int n = 0;
            for (int i = 0; i < w.Cap; i++)
            {
                if (w.StageOf[i] < St.Juvenile) continue;
                float hn = w.Feat[i * F.N + F.HEAT_NEED], at = w.Feat[i * F.N + F.AIR_T];
                Assert.InRange(hn, -1f, 1f); Assert.InRange(at, -1f, 1f); n++;
            }
            Assert.True(n > 0);
        });
    }

    [Fact] // COOLER turns a hot animal toward shade and toward water; WARMER away from them
    public void CoolerSteersTowardShadeAndWater()
    {
        float Turn(int prim, bool water)
        {
            var w = new World(8, 1);
            Terrain.Build(w);
            int FW = Map.FW;
            int cell = -1;
            for (int i = 0; i < FW * FW && cell < 0; i++) if (w.Cell[i] == (byte)Realm.Land) cell = i;
            int x = cell % FW, y = cell / FW;
            for (int i = 0; i < FW * FW; i++) { w.Temp[i] = 32f; w.Tree[i] = 0f; w.Shrub[i] = 0f; w.Cell[i] = (byte)Realm.Land; }
            int right = y * FW + (x + 1) % FW;
            if (water) w.Cell[right] = (byte)Realm.Shallow; else w.Tree[right] = 1f;
            int a = 0;
            Brain.RandomGenome(w.GenomeOf(a), 1, 0, 3);
            int g = a * G.LEN;
            w.Genome[g + G.B_LEGS] = 100; w.Genome[g + G.B_FINS] = -127; w.Genome[g + G.B_WINGS] = -127; w.Genome[g + G.SIGMA] = -128;
            for (int p = 0; p < P.N; p++)
            {
                int row = g + G.W2 + p * G.NIN;
                for (int k = 0; k < G.NIN; k++) w.Genome[row + k] = 0;
                w.Genome[row + G.NH + F.BIAS] = (sbyte)(p == prim ? 127 : -127);
            }
            for (int k = G.REFLEX; k < G.LEN; k++) w.Genome[g + k] = 0;
            w.Spc[a] = 0; w.StageOf[a] = St.Adult; w.Sp[0].Used = true; w.Sp[0].Alive = true;
            Body.Express(w, a); Brain.InitLife(w, a);
            w.X[a] = x * K.FCELL + 2f; w.Y[a] = y * K.FCELL + 2f; w.Heading[a] = MathF.PI / 2f;   // facing +y; the shade or water is at +x
            w.Energy[a] = w.EmaxOf(a); w.Age[a] = 1f;
            Simulation.BuildFor(w);
            Decide.Run(w, a, new Decide.ProbeBuf());
            float d = w.IHeading[a] - MathF.PI / 2f;
            return d;   // < 0: turned toward +x
        }
        Assert.True(Turn(P.COOLER, false) < 0f, "COOLER does not turn toward shade");
        Assert.True(Turn(P.COOLER, true) < 0f, "COOLER does not turn toward water");
        Assert.True(Turn(P.WARMER, false) > 0f, "WARMER does not turn away from shade");
    }
}
