// Ecology census of a headless run (2026-11-01, NR_ECO=1; reporting only: reads the world, changes nothing).
// Every EVERY steps: animals and biomass by trophic group (from what each clade actually ate since the last sample),
// plant standing stock and net primary production (PlantTable.Production), the herbivores' offtake of it
// (Telemetry.EatenMass), production and consumption by trophic level, deaths by cause, pathogen prevalence.
// At the end, per clade: density against Damuth's rule, life table, mortality causes, lifespan against expected.
// An ocean world (2026-11-02) has no land: its areas are the sea's, its production the benthic algae's and the plankton's
// (World.SeaProdA, SeaProdP, by depth zone), its offtake what grazers took of them, and each line counts the swimmers,
// waders and fliers and how many of the animals in water stand on the bottom. Before, it divided by the land's zero area
// and printed NaN and Infinity. Every ratio here goes through Div, so neither can be printed.

using System;
using System.Linq;
using System.Text;

namespace Neuroreef.Server;

public sealed class EcoWatch
{
    public const int EVERY = 300;
    // the step after which flows are pooled: the first year by default (the founders' transient), NR_ECO_FROM for short runs
    static readonly ulong FromEnv = ulong.TryParse(Environment.GetEnvironmentVariable("NR_ECO_FROM"), out var f) ? f : 0;
    // step-aware (2026-11-02): a year is Nature.SeasonLength reference steps, SeasonLength / Dt of this world's steps
    double _yr = K.T_SEASON, _perDay = K.STEPS_PER_DAY; ulong From = FromEnv > 0 ? FromEnv : (ulong)K.T_SEASON;
    readonly double _landKm2;
    readonly int _land;
    readonly bool _ocean;
    // an ocean world's production since the last sample and pooled (pool units), and its depth zones (Zone)
    double[] _zProdA = new double[4], _zProdP = new double[4], _sZA = new double[4], _sZP = new double[4];
    readonly int[] _zCells = new int[4];
    static readonly string[] ZoneName = { "<5 m", "5-30 m", "30-130 m", ">130 m" };
    static int Zone(float d) => d < 5f ? 0 : d <= K.OCEAN_SHELF ? 1 : d <= K.OCEAN_BREAK ? 2 : 3;
    /// <summary>a / b, or 0 where b is not positive or the quotient is not finite.</summary>
    static double Div(double a, double b) { if (!(b > 0)) return 0; double q = a / b; return double.IsFinite(q) ? q : 0; }
    double[] _prod = new double[5], _eaten = new double[Pools.N], _intake = Array.Empty<double>(), _grow = Array.Empty<double>(), _repro = Array.Empty<double>();
    long[] _deaths = Array.Empty<long>(), _births = Array.Empty<long>();
    ulong _lastStep;
    public readonly StringBuilder Series = new();
    // pooled over the run after the first year (the founders' transient): energy flows, by trophic group
    readonly double[] _sEat = new double[Pools.N], _sProd = new double[5];
    // NPP by biome (2026-11-01): production per cell from the first year's end, and each land cell's mean moisture and tree cover
    float[]? _cellProd0; readonly double[] _moist, _tree;
    double _sNpp, _sNppHerb, _sOff, _sIn0, _sIn1, _sIn2, _sP1, _sP2, _sSteps, _sBio1, _sBio2, _sN1, _sN2, _nS, _sMeat, _sCarrion;

    void Line(string l) { Series.AppendLine(l); Console.WriteLine(l); }
    // clades founded as eaters of animal food (most of their founders get the most energy per kg from meat, blood,
    // carrion or dung: Simulation.EatsAnimalFood), and the feeding census's juvenile strikes and kills at the last sample
    readonly bool[] _animalFounded = new bool[K.CLADES];
    double _jStr, _jSteps; long _jKilled;

    public EcoWatch(Simulation sim)
    {
        var w = sim.W;
        _ocean = w.Ocean;
        // the area animals live on: the land, or in an ocean world the sea (2026-11-02)
        for (int i = 0; i < Map.FW * Map.FW; i++)
        {
            if ((w.Cell[i] & (byte)Realm.Land) != 0) { if (!_ocean) _land++; }
            else if (_ocean && (w.Cell[i] & (byte)Realm.Water) != 0) { _land++; _zCells[Zone(Respiration.ColumnM(w, i))]++; }
        }
        if (_ocean) { w.SeaProdA = new double[Map.FW * Map.FW]; w.SeaProdP = new double[Map.FW * Map.FW]; }
        _moist = new double[Map.FW * Map.FW]; _tree = new double[Map.FW * Map.FW];
        _landKm2 = _land * (K.GEO_CELL_M / 1000.0) * (K.GEO_CELL_M / 1000.0);
        Reset(w);
        Series.AppendLine($"ECO: {(_ocean ? "sea" : "land")} {_land} cells = {_landKm2:F0} km2 (map {Map.FW * K.GEO_CELL_M / 1000.0:F1} km); one energy unit = {K.BMR_W * K.STEP_S / K.C_BASAL / 1e6:F2} MJ");
        if (_ocean)
        {
            int nc = Math.Max(1, _zCells.Sum());
            Series.AppendLine("ECO SEA FLOOR: " + string.Join("  ", Enumerable.Range(0, 4).Select(z => $"{ZoneName[z]} {100.0 * _zCells[z] / nc:F1}%")) + $" of {nc} sea cells");
        }
        Series.AppendLine($"ECO: a unit of plant cover is {K.PLANT_KG_M2} kg dry/m2 at {K.PLANT_KJ_G} kJ/g ({Pools.Kg[(int)Pool.Grass]:F0} kg of food per unit in a {K.GEO_CELL_M} m cell)");
        // the founding (2026-11-01): each clade's founders, their mean mass, the food they get most energy from, and
        // whether its ancestor was tested after the others, on the animals living in its range
        var nF = new int[K.CLADES]; var nA = new int[K.CLADES]; var mF = new double[K.CLADES]; var best = new double[K.CLADES * Pools.N];
        for (int i = 0; i < w.Cap; i++)
        {
            if (w.StageOf[i] == St.Dead) continue;
            int s = w.Spc[i]; nF[s]++; mF[s] += w.PM[i]; if (Simulation.EatsAnimalFood(w, i)) nA[s]++;
            var d = w.DietOf(i); for (int p = 0; p < Pools.N; p++) best[s * Pools.N + p] += d[p] * Pools.Rho[p];
        }
        int fa = 0, fc = 0;
        var fl = new StringBuilder("ECO FOUNDING: clade founders mean-mass best-food(E/kg) [A = most get the most from animal food, W = tested after the others]:");
        for (int s = 0; s < K.CLADES; s++)
        {
            if (nF[s] == 0) continue;
            fc++; _animalFounded[s] = 2 * nA[s] > nF[s]; if (_animalFounded[s]) fa++;
            int bp = 0; for (int p = 1; p < Pools.N; p++) if (best[s * Pools.N + p] > best[s * Pools.N + bp]) bp = p;
            fl.Append($"  {w.Sp[s].Name} {nF[s]} {mF[s] / nF[s]:F2}kg {Pools.Names[bp]}({best[s * Pools.N + bp] / nF[s]:F2}){(_animalFounded[s] ? " A" : "")}{(sim.WaitedClades.Contains(s) ? " W" : "")}");
        }
        Series.AppendLine(fl.ToString());
        Series.AppendLine($"ECO FOUNDING: {fc} clades founded, {fa} of them animal-food eaters; {sim.WaitedClades.Count} ancestors tested after the others");
        Console.WriteLine(fl.ToString()); Console.WriteLine($"ECO FOUNDING: {fc} clades founded, {fa} of them animal-food eaters; {sim.WaitedClades.Count} ancestors tested after the others");
        if (_ocean) Series.AppendLine("ECO   step  alive  | herbiv n  kg/km2 | omniv n kg/km2 | carniv n kg/km2 | detr n | water NPP gC/m2/yr plankton benthic grazed% | P2/NPP  P3/P2 | infected% | deaths: starve thirst killed disease old other | clades alive (carn by intake, animal-founded) | juvenile strikes/step, killed | swim wade fly (airborne%) walk, in water standing% | top");
        else Series.AppendLine("ECO   step  alive  | herbiv n  kg/km2 | omniv n kg/km2 | carniv n kg/km2 | detr n | green kg/m2 herb woody | NPP kgdry/m2/yr offtake% | P2/NPP  P3/P2 | infected% | deaths: starve thirst killed disease old other | clades alive (carn by intake, animal-founded) | juvenile strikes/step, killed");
    }

    void Reset(World w)
    {
        var T = w.T; var t = w.Flora;
        _prod = (double[])t.Production.Clone(); _eaten = (double[])T.EatenMass.Clone(); _intake = (double[])T.Intake.Clone();
        _grow = (double[])T.SpendGrowth.Clone(); _repro = (double[])T.SpendRepro.Clone();
        _deaths = (long[])T.Deaths.Clone(); _births = (long[])T.Births.Clone(); _lastStep = w.Step;
    }

    /// <summary>The trophic group of clade s from the energy it assimilated (Intake, E) since the last sample:
    /// 0 herbivore (>= 70% from live plants and algae), 2 carnivore (>= 50% from meat, blood and carrion),
    /// 3 detritivore (>= 50% from dung, litter, wood, detritus), else 1 omnivore; -1 ate nothing.</summary>
    public static int Group(double[] now, double[] before, int s)
    {
        double pl = 0, an = 0, de = 0;
        for (int p = 0; p < Pools.N; p++)
        {
            double v = now[s * Pools.N + p] - (before.Length > 0 ? before[s * Pools.N + p] : 0);
            if (p <= (int)Pool.Phyto) pl += v;
            else if (p == (int)Pool.Meat || p == (int)Pool.Blood || p == (int)Pool.Carrion) an += v;
            else de += v;
        }
        double tot = pl + an + de;
        if (tot <= 0) return -1;
        return pl >= 0.7 * tot ? 0 : an >= 0.5 * tot ? 2 : de >= 0.5 * tot ? 3 : 1;
    }

    public void Sample(Simulation sim)
    {
        var w = sim.W; var T = w.T; var t = w.Flora;
        _yr = Nature.SeasonLength / (double)w.Dt; _perDay = w.StepsPerDay; if (FromEnv == 0) From = (ulong)_yr;
        if (w.Step % EVERY != 0 || w.Step == _lastStep) return;
        double years = (w.Step - _lastStep) / _yr;
        var grp = new int[w.NS];
        for (int s = 0; s < w.NS; s++) grp[s] = Group(T.Intake, _intake, s);
        var n = new int[4]; var bio = new double[4]; int infected = 0, alive = 0;
        for (int i = 0; i < w.Cap; i++)
        {
            if (w.StageOf[i] < St.Juvenile) continue;
            alive++; if (w.PgLoad[i] > 0f) infected++;
            int g = grp[w.Spc[i]]; if (g < 0) g = 1;
            n[g]++; bio[g] += w.MassOf(i);
        }
        // standing green (leafy) biomass, kg dry per m2 of land: cover x PLANT_KG_M2 (2026-11-01)
        double herb = 0, browse = 0;
        for (int i = 0; i < Map.FW * Map.FW; i++) if ((w.Cell[i] & (byte)Realm.Land) != 0) { herb += w.Pool[(int)Pool.Grass][i]; browse += w.Shrub[i] + w.Tree[i] + w.Sapling[i]; }
        herb *= K.PLANT_KG_M2; browse *= K.PLANT_KG_M2;
        double prod = 0; for (int k = 0; k < 5; k++) prod += t.Production[k] - _prod[k];
        double prodHerb = t.Production[0] - _prod[0];
        double off = 0; for (int p = 0; p <= (int)Pool.Root; p++) off += T.EatenMass[p] - _eaten[p];
        off /= Pools.Kg[(int)Pool.Grass];   // kg eaten into units of cover (2026-11-01), to compare with production
        // energy assimilated and produced (growth + reproduction) by trophic group
        var inE = new double[4]; var prE = new double[4];
        // animal food eaten by any clade, whatever its group (2026-11-01): kills and blood (meat), and carrion
        double meatE = 0, carrionE = 0;
        for (int s = 0; s < w.NS; s++)
        {
            meatE += T.Intake[s * Pools.N + (int)Pool.Meat] - _intake[s * Pools.N + (int)Pool.Meat] + T.Intake[s * Pools.N + (int)Pool.Blood] - _intake[s * Pools.N + (int)Pool.Blood];
            carrionE += T.Intake[s * Pools.N + (int)Pool.Carrion] - _intake[s * Pools.N + (int)Pool.Carrion];
        }
        for (int s = 0; s < w.NS; s++)
        {
            int g = grp[s]; if (g < 0) continue;
            for (int p = 0; p < Pools.N; p++) inE[g] += T.Intake[s * Pools.N + p] - _intake[s * Pools.N + p];
            prE[g] += T.SpendGrowth[s] - _grow[s] + T.SpendRepro[s] - _repro[s];
        }
        // NPP in energy (2026-11-01): a unit of cover is PLANT_KG_M2 of dry matter per m2 at PLANT_KJ_G, Pools.ECell in a
        // cell, whatever stratum made it (before: herb at RHO_GRASS and woody at RHO_WOOD per unit read as 1 kg per cell)
        double nppE = prod * Pools.ECell[(int)Pool.Grass];
        // an ocean world: the water producers' production since the last sample, by depth zone (2026-11-02)
        double sea = 0, seaA = 0, seaP = 0, seaOff = 0;
        if (_ocean && w.SeaProdA != null)
        {
            Array.Clear(_zProdA); Array.Clear(_zProdP);
            for (int i = 0; i < Map.FW * Map.FW; i++)
            {
                if ((w.Cell[i] & (byte)Realm.Water) == 0) continue;
                int z = Zone(Respiration.ColumnM(w, i));
                _zProdA[z] += w.SeaProdA[i]; _zProdP[z] += w.SeaProdP![i];
            }
            Array.Clear(w.SeaProdA); Array.Clear(w.SeaProdP!);
            seaA = _zProdA.Sum(); seaP = _zProdP.Sum(); sea = seaA + seaP;
            nppE = sea * Pools.ECell[(int)Pool.Algae];
            for (int p = (int)Pool.Algae; p <= (int)Pool.Phyto; p++) seaOff += (T.EatenMass[p] - _eaten[p]) / Pools.Kg[p];   // kg eaten into units
        }
        // the most numerous clade: its count, mean mass, births and deaths since the last sample, its main food
        int top = -1; for (int s = 0; s < w.NS; s++) if (sim.ActiveCount[s] > 0 && (top < 0 || sim.ActiveCount[s] > sim.ActiveCount[top])) top = s;
        string topS = "";
        if (top >= 0)
        {
            double tm = 0; int tn = 0; for (int i = 0; i < w.Cap; i++) if (w.StageOf[i] == St.Adult && w.Spc[i] == top) { tm += w.PM[i]; tn++; }
            long tb = T.Births[top] - _births[top], td = 0; for (int c = 0; c < Cause.N; c++) td += T.Deaths[top * Cause.N + c] - _deaths[top * Cause.N + c];
            int bp = 0; double bvv = -1; for (int p = 0; p < Pools.N; p++) { double v = T.Intake[top * Pools.N + p] - _intake[top * Pools.N + p]; if (v > bvv) { bvv = v; bp = p; } }
            topS = $" | top {w.Sp[top].Name} {sim.ActiveCount[top]} M {(tn > 0 ? tm / tn : 0):F2} born {tb} died {td} eats {Pools.Names[bp]}";
        }
        var dc = new long[Cause.N];
        for (int s = 0; s < w.NS; s++) for (int c = 0; c < Cause.N; c++) dc[c] += T.Deaths[s * Cause.N + c] - _deaths[s * Cause.N + c];
        long other = dc.Sum() - dc[Cause.Starve] - dc[Cause.Thirst] - dc[Cause.Killed] - dc[Cause.Disease] - dc[Cause.Old];
        int cAlive = 0, cCarn = 0, cAnimal = 0;
        for (int s = 0; s < w.NS; s++) { if (sim.ActiveCount[s] <= 0) continue; cAlive++; if (grp[s] == 2) cCarn++; if (s < K.CLADES && _animalFounded[s]) cAnimal++; }
        string juv = "";
        if (Diag.D is { } dg)
        {
            double str = dg.StrTry[2] + dg.StrTry[3], st = dg.Steps[2] + dg.Steps[3];
            long kd = dg.Deaths[2 * Cause.N + Cause.Killed] + dg.Deaths[3 * Cause.N + Cause.Killed];
            juv = $" | {(st - _jSteps > 0 ? (str - _jStr) / (st - _jSteps) : 0),6:P2} {kd - _jKilled,5}";
            _jStr = str; _jSteps = st; _jKilled = kd;
        }
        double P2 = prE[0] + prE[1] * 0.5, P3 = prE[2] + prE[1] * 0.5;
        string tail = $"{(alive > 0 ? 100.0 * infected / alive : 0),8:F1} | {dc[Cause.Starve],6} {dc[Cause.Thirst],6} {dc[Cause.Killed],6} {dc[Cause.Disease],7} {dc[Cause.Old],4} {other,5} | {cAlive,3} ({cCarn}, {cAnimal}){juv}";
        string head = $"ECO {w.Step,6} {alive,6}  | {n[0],7} {Div(bio[0], _landKm2),7:F1} | {n[1],6} {Div(bio[1], _landKm2),6:F1} | {n[2],7} {Div(bio[2], _landKm2),7:F2} | {n[3],5} | ";
        if (_ocean)
        {
            double gc = K.WATER_KG_M2 * 1000.0 * 0.45;   // gC per m2 of a unit of a water producer (0.45 gC per g dry, Wetzel 2001)
            Line(head + $"{Div(sea, years * _land) * gc,9:F0} {Div(seaP, years * _land) * gc,8:F0} {Div(seaA, years * _land) * gc,7:F0} {100 * Div(seaOff, sea),8:F2} | {Div(P2, nppE),6:P2} {Div(P3, P2),6:P1} | " + tail + " | " + Census(w) + topS);
            Line($"ECO SEA  {w.Step,6}  NPP gC/m2/yr by depth (plankton + benthic): " + string.Join("  ", Enumerable.Range(0, 4).Where(z => _zCells[z] > 0)
                .Select(z => $"{ZoneName[z]} {Div(_zProdP[z], years * _zCells[z]) * gc:F0} + {Div(_zProdA[z], years * _zCells[z]) * gc:F0}")));
            if (w.Step > From) for (int z = 0; z < 4; z++) { _sZA[z] += _zProdA[z]; _sZP[z] += _zProdP[z]; }
        }
        else
            Line(head + $"{Div(herb, _land),6:F3} {Div(browse, _land),6:F3} | {Div(prod, years * _land) * K.PLANT_KG_M2,9:F3}  {100 * Div(off, prod),8:F2} | {Div(P2, nppE),6:P2} {Div(P3, P2),6:P1} | " + tail + topS);
        if (_ocean) { prod = sea; off = seaOff; }   // pooled below as the sea's
        if (w.Step <= From) _cellProd0 = (float[])t.CellProd.Clone();
        else for (int i = 0; i < Map.FW * Map.FW; i++) { _moist[i] += w.Moisture[i]; _tree[i] += w.Tree[i]; }
        if (w.Step > From)
        {
            _sNpp += nppE; _sNppHerb += prodHerb; _sOff += off; _sIn0 += inE[0] + 0.5 * inE[1]; _sIn2 += inE[2] + 0.5 * inE[1];
            _sP1 += P2; _sP2 += P3; _sSteps += w.Step - _lastStep; _sBio1 += bio[0] + 0.5 * bio[1]; _sBio2 += bio[2] + 0.5 * bio[1];
            _sN1 += prod; _sIn1 += 0; _nS++; _sMeat += meatE; _sCarrion += carrionE;
            for (int p = 0; p < Pools.N; p++) _sEat[p] += T.EatenMass[p] - _eaten[p];
            for (int k = 0; k < 5; k++) _sProd[k] += t.Production[k] - _prod[k];
        }
        Reset(w);
    }

    /// <summary>How the animals move (2026-11-02, the ocean): swimmers, waders, fliers (the share of fliers in the air) and
    /// walkers, and of the animals in water how many stand on the bottom (World.StandsIn) rather than swim or float.</summary>
    static string Census(World w)
    {
        var by = new int[5]; int air = 0, inWater = 0, standing = 0;
        for (int i = 0; i < w.Cap; i++)
        {
            if (w.StageOf[i] < St.Juvenile) continue;
            by[w.PLoco[i]]++;
            if (w.PLoco[i] == (byte)Loco.Fly && w.Airborne(i)) air++;
            int c = World.FIdx(w.X[i], w.Y[i]);
            if ((w.Cell[c] & (byte)Realm.Water) != 0 && !w.Airborne(i)) { inWater++; if (w.StandsIn(i, c)) standing++; }
        }
        return $"{by[(int)Loco.Swim]} {by[(int)Loco.Wade]} {by[(int)Loco.Fly]} ({100 * Div(air, by[(int)Loco.Fly]):F0}%) {by[(int)Loco.Walk]}, {100 * Div(standing, inWater):F1}%";
    }

    /// <summary>NPP by biome after the first year, in g C/m2/yr (dry matter x 0.45 C). The biome of a land cell is read
    /// from its annual mean temperature, mean soil moisture and mean tree cover (a reporting classification; nothing in the
    /// world uses it): polar &lt; -5 °C; forest where tree cover >= 0.3 (boreal &lt; 3 °C, temperate &lt; 18 °C, warm);
    /// otherwise desert and dry scrub where moisture &lt; 0.25, grassland and shrubland &lt; 18 °C, savanna.</summary>
    string Biomes(World w)
    {
        if (_cellProd0 == null || _nS == 0) return "";
        string[] name = { "polar/tundra", "boreal forest", "temperate forest", "warm forest", "desert/dry scrub", "temperate grass/shrub", "savanna/warm grass" };
        int nb = name.Length; var cnt = new int[nb]; var npp = new double[nb]; var tt = new double[nb]; var mm = new double[nb]; var tc = new double[nb]; var wd = new double[nb];
        double years = _sSteps / _yr;
        for (int i = 0; i < Map.FW * Map.FW; i++)
        {
            if ((w.Cell[i] & (byte)Realm.Land) == 0) continue;
            double T = Climate.On ? Climate.AnnualMean(w, i) : K.T_REF, m = _moist[i] / _nS, tr = _tree[i] / _nS;
            int b = T < -5 ? 0 : tr >= 0.3 ? (T < 3 ? 1 : T < 18 ? 2 : 3) : m < 0.25 ? 4 : T < 18 ? 5 : 6;
            cnt[b]++; npp[b] += (w.Flora.CellProd[i] - _cellProd0[i]) / years * K.PLANT_KG_M2 * 1000 * 0.45; tt[b] += T; mm[b] += m; tc[b] += tr;
            wd[b] += w.LiveWood[i] * K.PLANT_KG_M2;   // standing live wood at the end, kg dry/m2 (2026-11-01)
        }
        var o = new StringBuilder("ECO NPP BY BIOME (after the first year; g C/m2/yr = g dry x 0.45): biome, land cells, mean NPP, mean annual T, moisture, tree cover, live wood kg dry/m2 at the end\n");
        for (int b = 0; b < nb; b++)
            if (cnt[b] > 0) o.AppendLine($"  {name[b],-22} {cnt[b],6} {npp[b] / cnt[b],8:F0} gC  {tt[b] / cnt[b],6:F1} C  {mm[b] / cnt[b],5:F2}  {tc[b] / cnt[b],5:F2}  {wd[b] / cnt[b],5:F1}");
        return o.ToString();
    }

    /// <summary>The end-of-run tables (per clade and pooled flows).</summary>
    public string Report(Simulation sim)
    {
        var w = sim.W; var T = w.T; var o = new StringBuilder();
        o.Append(Series);
        o.AppendLine();
        double unitKg = _ocean ? K.WATER_KG_M2 : K.PLANT_KG_M2;   // a unit of the producers' cover, kg dry/m2
        o.AppendLine($"ECO POOLED after step {From} ({_nS:F0} samples): {(_ocean ? "water " : "")}NPP {Div(_sN1 * _yr, Math.Max(1, _sSteps) * _land) * unitKg:F3} kg dry/m2/yr " +
            $"= {Div(_sNpp * _yr * Pools.EnergyJ / 1e6, Math.Max(1, _sSteps) * _landKm2 * 1e6) * 1000:F0} kJ/m2/yr; {(_ocean ? "grazing" : "herbivore offtake")} {100 * Div(_sOff, _sN1):F2}% of NPP (mass); " +
            $"assimilated by herbivores {100 * Div(_sIn0, _sNpp):F2}% of NPP energy, by carnivores {100 * Div(_sIn2, _sIn0):F2}% of the herbivores' intake ({100 * Div(_sIn2, _sP1):F1}% of their production); " +
            $"production P2/NPP {100 * Div(_sP1, _sNpp):F2}%, P3/P2 {100 * Div(_sP2, _sP1):F2}%; mean biomass herbivores {Div(_sBio1, Math.Max(1, _nS) * _landKm2):F1} kg/km2, carnivores {Div(_sBio2, Math.Max(1, _nS) * _landKm2):F2} kg/km2 (1 : {Div(_sBio1, _sBio2):F0})");
        if (_ocean)
        {
            double gc = K.WATER_KG_M2 * 1000.0 * 0.45, yrs = Math.Max(1, _sSteps) / _yr;
            o.AppendLine("ECO SEA POOLED NPP gC/m2/yr by depth (plankton + benthic): " + string.Join("  ", Enumerable.Range(0, 4).Where(z => _zCells[z] > 0)
                .Select(z => $"{ZoneName[z]} ({_zCells[z]} cells) {Div(_sZP[z], yrs * _zCells[z]) * gc:F0} + {Div(_sZA[z], yrs * _zCells[z]) * gc:F0}")));
        }
        o.AppendLine($"ECO ANIMAL FOOD (pooled, any clade whatever its group): meat and blood assimilated {100 * Div(_sMeat, _sP1):F2}% of herbivore production ({100 * Div(_sMeat, _sIn0):F3}% of herbivore intake), carrion {100 * Div(_sCarrion, _sP1):F2}% of herbivore production");
        if (!_ocean) o.AppendLine("ECO PRODUCTION (pooled, kg dry/m2 of land/yr): " + string.Join("  ", new[] { "herb", "shrub", "tree", "sapling", "repro" }.Select((n, k) => $"{n} {Div(_sProd[k] * _yr, Math.Max(1, _sSteps) * _land) * K.PLANT_KG_M2:F3}")));
        o.Append(Biomes(w));
        o.AppendLine("ECO EATEN (pooled, M/km2/yr; energy E/km2/yr at the pool's density): " + string.Join("  ", Enumerable.Range(0, Pools.N).Where(p => _sEat[p] > 0).Select(p => $"{(Pool)p} {Div(_sEat[p] * _yr, Math.Max(1, _sSteps) * _landKm2):F1} ({Div(_sEat[p] * Pools.Rho[p] * _yr, Math.Max(1, _sSteps) * _landKm2):F0})")));
        o.AppendLine($"ECO CLADES (living): name, group (whole run), alive, adults, juveniles%, mean adult mass kg, density /km2 {(_ocean ? "sea" : "land")}, Damuth 4e4 M(g)^-0.75 /km2, ratio, infected%, mean age at death / expected lifespan, deaths: starve thirst killed disease old other");
        for (int s = 0; s < w.NS; s++)
        {
            if (!w.Sp[s].Used) continue;
            int alive = 0, adults = 0, inf = 0; double m = 0, life = 0;
            for (int i = 0; i < w.Cap; i++)
            {
                if (w.StageOf[i] < St.Juvenile || w.Spc[i] != s) continue;
                alive++; if (w.PgLoad[i] > 0f) inf++;
                life += w.PLife[i];
                if (w.StageOf[i] == St.Adult) { adults++; m += w.PM[i]; }
            }
            if (alive == 0) continue;
            int g = Group(T.Intake, Array.Empty<double>(), s);
            string gn = g switch { 0 => "herb", 1 => "omni", 2 => "carn", 3 => "detr", _ => "-" };
            double mm = adults > 0 ? m / adults : 0, dens = Div(alive, _landKm2), damuth = mm > 0 ? 4e4 * Math.Pow(mm * 1000, -0.75) : 0;
            long hatched = T.DeathsByAge[s * 4 + 1] + T.DeathsByAge[s * 4 + 2] + T.DeathsByAge[s * 4 + 3];
            double ageD = hatched > 0 ? T.AgeAtDeath[s] / hatched : 0;
            long st = T.Deaths[s * Cause.N + Cause.Starve], th = T.Deaths[s * Cause.N + Cause.Thirst], ki = T.Deaths[s * Cause.N + Cause.Killed],
                 di = T.Deaths[s * Cause.N + Cause.Disease], ol = T.Deaths[s * Cause.N + Cause.Old];
            long all = 0; for (int c = 0; c < Cause.N; c++) all += T.Deaths[s * Cause.N + c];
            o.AppendLine($"  {w.Sp[s].Name,-12} {gn} {alive,6} {adults,6} {100.0 * (alive - adults) / alive,5:F0}% {mm,7:F2} {dens,8:F2} {damuth,8:F0} {Div(dens, damuth),7:F3} {100.0 * inf / alive,5:F0}% {Div(ageD, _perDay),6:F0} d / {Div(life, alive * (double)K.STEPS_PER_DAY),5:F0} d   {st,6} {th,6} {ki,6} {di,6} {ol,6} {all - st - th - ki - di - ol,6}");
        }
        return o.ToString();
    }
}
