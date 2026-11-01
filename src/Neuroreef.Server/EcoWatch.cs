// Ecology census of a headless run (2026-11-01, NR_ECO=1; reporting only: reads the world, changes nothing).
// Every EVERY steps: animals and biomass by trophic group (from what each clade actually ate since the last sample),
// plant standing stock and net primary production (PlantTable.Production), the herbivores' offtake of it
// (Telemetry.EatenMass), production and consumption by trophic level, deaths by cause, pathogen prevalence.
// At the end, per clade: density against Damuth's rule, life table, mortality causes, lifespan against expected.

using System;
using System.Linq;
using System.Text;

namespace Neuroreef.Server;

public sealed class EcoWatch
{
    public const int EVERY = 300;
    readonly double _landKm2;
    readonly int _land;
    double[] _prod = new double[5], _eaten = new double[Pools.N], _intake = Array.Empty<double>(), _grow = Array.Empty<double>(), _repro = Array.Empty<double>();
    long[] _deaths = Array.Empty<long>(), _births = Array.Empty<long>();
    ulong _lastStep;
    public readonly StringBuilder Series = new();
    // pooled over the run after the first year (the founders' transient): energy flows, by trophic group
    readonly double[] _sEat = new double[Pools.N], _sProd = new double[5];
    double _sNpp, _sNppHerb, _sOff, _sIn0, _sIn1, _sIn2, _sP1, _sP2, _sSteps, _sBio1, _sBio2, _sN1, _sN2, _nS;

    void Line(string l) { Series.AppendLine(l); Console.WriteLine(l); }

    public EcoWatch(World w)
    {
        for (int i = 0; i < Map.FW * Map.FW; i++) if ((w.Cell[i] & (byte)Realm.Land) != 0) _land++;
        _landKm2 = _land * (K.GEO_CELL_M / 1000.0) * (K.GEO_CELL_M / 1000.0);
        Reset(w);
        Series.AppendLine($"ECO: land {_land} cells = {_landKm2:F0} km2 (map {Map.FW * K.GEO_CELL_M / 1000.0:F1} km); one energy unit = {K.BMR_W * K.STEP_S / K.C_BASAL / 1e6:F2} MJ");
        Series.AppendLine("ECO   step  alive  | herbiv n  kg/km2 | omniv n kg/km2 | carniv n kg/km2 | detr n | herb cover browse | NPP M/km2/yr  offtake% | P2/NPP  P3/P2 | infected% | deaths: starve thirst killed disease old other");
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
        if (w.Step % EVERY != 0 || w.Step == _lastStep) return;
        double years = (w.Step - _lastStep) / (double)K.T_SEASON;
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
        double herb = 0, browse = 0;
        for (int i = 0; i < Map.FW * Map.FW; i++) if ((w.Cell[i] & (byte)Realm.Land) != 0) { herb += w.Pool[(int)Pool.Grass][i]; browse += w.Pool[(int)Pool.Browse][i]; }
        double prod = 0; for (int k = 0; k < 5; k++) prod += t.Production[k] - _prod[k];
        double prodHerb = t.Production[0] - _prod[0];
        double off = 0; for (int p = 0; p <= (int)Pool.Root; p++) off += T.EatenMass[p] - _eaten[p];
        // energy assimilated and produced (growth + reproduction) by trophic group
        var inE = new double[4]; var prE = new double[4];
        for (int s = 0; s < w.NS; s++)
        {
            int g = grp[s]; if (g < 0) continue;
            for (int p = 0; p < Pools.N; p++) inE[g] += T.Intake[s * Pools.N + p] - _intake[s * Pools.N + p];
            prE[g] += T.SpendGrowth[s] - _grow[s] + T.SpendRepro[s] - _repro[s];
        }
        // NPP in energy: herb growth and reproduction at grass's density, woody growth at wood's
        double nppE = (t.Production[0] - _prod[0] + t.Production[4] - _prod[4]) * K.RHO_GRASS
                    + (t.Production[1] - _prod[1] + t.Production[2] - _prod[2] + t.Production[3] - _prod[3]) * K.RHO_WOOD;
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
        double P2 = prE[0] + prE[1] * 0.5, P3 = prE[2] + prE[1] * 0.5;
        Line($"ECO {w.Step,6} {alive,6}  | {n[0],7} {bio[0] / _landKm2,7:F1} | {n[1],6} {bio[1] / _landKm2,6:F1} | {n[2],7} {bio[2] / _landKm2,7:F2} | {n[3],5} | {herb / _land,6:F3} {browse / _land,6:F3} | {prod / years / _landKm2,9:F1}  {(prod > 0 ? 100 * off / prod : 0),8:F1} | {(nppE > 0 ? P2 / nppE : 0),6:P2} {(P2 > 0 ? P3 / P2 : 0),6:P1} | {(alive > 0 ? 100.0 * infected / alive : 0),8:F1} | {dc[Cause.Starve],6} {dc[Cause.Thirst],6} {dc[Cause.Killed],6} {dc[Cause.Disease],7} {dc[Cause.Old],4} {other,5}{topS}");
        if (w.Step > (ulong)K.T_SEASON)
        {
            _sNpp += nppE; _sNppHerb += prodHerb; _sOff += off; _sIn0 += inE[0] + 0.5 * inE[1]; _sIn2 += inE[2] + 0.5 * inE[1];
            _sP1 += P2; _sP2 += P3; _sSteps += w.Step - _lastStep; _sBio1 += bio[0] + 0.5 * bio[1]; _sBio2 += bio[2] + 0.5 * bio[1];
            _sN1 += prod; _sIn1 += 0; _nS++;
            for (int p = 0; p < Pools.N; p++) _sEat[p] += T.EatenMass[p] - _eaten[p];
            for (int k = 0; k < 5; k++) _sProd[k] += t.Production[k] - _prod[k];
        }
        Reset(w);
    }

    /// <summary>The end-of-run tables (per clade and pooled flows).</summary>
    public string Report(Simulation sim)
    {
        var w = sim.W; var T = w.T; var o = new StringBuilder();
        o.Append(Series);
        o.AppendLine();
        o.AppendLine($"ECO POOLED after the first year ({_nS:F0} samples): NPP {_sN1 / Math.Max(1, _sSteps) * K.T_SEASON / _landKm2:F1} M/km2/yr " +
            $"= {_sNpp / Math.Max(1, _sSteps) * K.T_SEASON * K.BMR_W * K.STEP_S / K.C_BASAL / 1e6 / (_landKm2 * 1e6) * 1000:F2} kJ/m2/yr; herbivore offtake {(_sN1 > 0 ? 100 * _sOff / _sN1 : 0):F1}% of NPP (mass); " +
            $"assimilated by herbivores {(_sNpp > 0 ? 100 * _sIn0 / _sNpp : 0):F2}% of NPP energy, by carnivores {(_sIn0 > 0 ? 100 * _sIn2 / _sIn0 : 0):F2}% of the herbivores' intake; " +
            $"production P2/NPP {(_sNpp > 0 ? 100 * _sP1 / _sNpp : 0):F2}%, P3/P2 {(_sP1 > 0 ? 100 * _sP2 / _sP1 : 0):F2}%; mean biomass herbivores {_sBio1 / Math.Max(1, _nS) / _landKm2:F1} kg/km2, carnivores {_sBio2 / Math.Max(1, _nS) / _landKm2:F2} kg/km2 (1 : {(_sBio2 > 0 ? _sBio1 / _sBio2 : 0):F0})");
        o.AppendLine("ECO PRODUCTION (pooled, M/km2/yr): " + string.Join("  ", new[] { "herb", "shrub", "tree", "sapling", "repro" }.Select((n, k) => $"{n} {_sProd[k] / Math.Max(1, _sSteps) * K.T_SEASON / _landKm2:F1}")));
        o.AppendLine("ECO EATEN (pooled, M/km2/yr; energy E/km2/yr at the pool's density): " + string.Join("  ", Enumerable.Range(0, Pools.N).Where(p => _sEat[p] > 0).Select(p => $"{(Pool)p} {_sEat[p] / Math.Max(1, _sSteps) * K.T_SEASON / _landKm2:F1} ({_sEat[p] * Pools.Rho[p] / Math.Max(1, _sSteps) * K.T_SEASON / _landKm2:F0})")));
        o.AppendLine("ECO CLADES (living): name, group (whole run), alive, adults, juveniles%, mean adult mass kg, density /km2 land, Damuth 4e4 M(g)^-0.75 /km2, ratio, infected%, mean age at death / expected lifespan, deaths: starve thirst killed disease old other");
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
            double mm = adults > 0 ? m / adults : 0, dens = alive / _landKm2, damuth = mm > 0 ? 4e4 * Math.Pow(mm * 1000, -0.75) : 0;
            long hatched = T.DeathsByAge[s * 4 + 1] + T.DeathsByAge[s * 4 + 2] + T.DeathsByAge[s * 4 + 3];
            double ageD = hatched > 0 ? T.AgeAtDeath[s] / hatched : 0;
            long st = T.Deaths[s * Cause.N + Cause.Starve], th = T.Deaths[s * Cause.N + Cause.Thirst], ki = T.Deaths[s * Cause.N + Cause.Killed],
                 di = T.Deaths[s * Cause.N + Cause.Disease], ol = T.Deaths[s * Cause.N + Cause.Old];
            long all = 0; for (int c = 0; c < Cause.N; c++) all += T.Deaths[s * Cause.N + c];
            o.AppendLine($"  {w.Sp[s].Name,-12} {gn} {alive,6} {adults,6} {100.0 * (alive - adults) / alive,5:F0}% {mm,7:F2} {dens,8:F2} {damuth,8:F0} {(damuth > 0 ? dens / damuth : 0),7:F3} {100.0 * inf / alive,5:F0}% {ageD / K.STEPS_PER_DAY,6:F0} d / {life / alive / K.STEPS_PER_DAY,5:F0} d   {st,6} {th,6} {ki,6} {di,6} {ol,6} {all - st - th - ki - di - ol,6}");
        }
        return o.ToString();
    }
}
