using System;
using Xunit;
using Xunit.Abstractions;

namespace Neuroreef.Tests;

/// <summary>
/// The energy budget of one plankton-eating fish (2026-11-02, the ocean audit): a 1 kg cold-blooded, finned, gilled,
/// streamlined plankton specialist in a sea cell at an ordinary standing stock of plankton, eating every step for three
/// days in RESOLVE itself. Every term per day, in kJ and in energy units, against a real ~1 kg planktivorous fish.
/// </summary>
public class FeedingBudgetTests
{
    readonly ITestOutputHelper _out;
    public FeedingBudgetTests(ITestOutputHelper o) { _out = o; }

    public sealed class Budget
    {
        public float Mass, LengthM, Fineness, CruiseMps, CoreC, Level, RateT;
        public float BasalDay, BodyDay, GutDay, BrainDay, FinDay, EyeDay, GillDay, WingDay;   // E/day, as RESOLVE charges basal
        public float BreathDay, ThermoDay, MoveDay, CruiseDay, DiveDay;
        public float SweptM3Day, MgCPerM3, KgPerM3, EatenKgDay, EatenEDay, AbsorbedDay, NetDay, AbsorbCapDay, DietEff;
        public float GapeM2;
    }

    /// <summary>Phytoplankton held in a unit of the pool, mg C per m2 (WATER_KG_M2 of dry matter at ~45% carbon:
    /// Redfield, N_ALGAE's note).</summary>
    public const float MgCPerUnitM2 = K.WATER_KG_M2 * 1e6f * 0.45f;

    /// <summary>The cheapest plankton-eating fish the genes make: 1 kg (floor class 1), fins, gills, cold-blooded, bare, long
    /// and slim, no weapons, armour, voice or spare organs, a plankton specialist (by default with the smallest brain).</summary>
    public static void MakeFish(Span<sbyte> gn, sbyte brainGene = -127, sbyte lengthGene = 127)
    {
        gn[G.B_MASS] = -127; gn[G.B_FLOOR] = 1;
        gn[G.B_FINS] = 127; gn[G.B_FINN] = -127; gn[G.B_WINGS] = -127; gn[G.B_LEGS] = -127;
        gn[G.B_LUNGS] = -127; gn[G.B_ENDO] = -127; gn[G.B_INSUL] = -127; gn[G.B_APPEND] = -127;
        gn[G.B_ARMOUR] = -127; gn[G.B_STRIKE] = -127; gn[G.B_VOICE] = -127; gn[G.B_IMMUNE] = -127;
        gn[G.B_WATER] = -127; gn[G.B_EYEN] = -127; gn[G.B_DIVE] = -127;
        gn[G.B_LENGTH] = lengthGene; gn[G.B_WIDTH] = -30; gn[G.B_HEIGHT] = -30;
        gn[G.B_BRAIN] = brainGene;
        for (int p = 0; p < Pools.N; p++) gn[G.B_DIET + p] = -127;
        gn[G.B_DIET + (int)Pool.Phyto] = 127;
        // phase 4 (2026-11-03): a fish described by its labelled genes: the module block is emptied, so the body is the
        // translator's reading of them (Morph.Codec.FromLabelled), whatever block the genome held
        if (gn.Length >= G.LEN) gn.Slice(G.MORPH, G.MORPHN).Clear();
    }

    /// <summary>Runs the fish for `days` in a 40 m sea cell held at `phyto` units of plankton and `waterC` °C.</summary>
    public static Budget Run(float phyto = 0.3f, float waterC = 15f, float days = 3f, sbyte brainGene = -127, sbyte lengthGene = 127)
    {
        return OceanTests.Sea(() =>
        {
            var w = new World(16, 3);
            Terrain.Build(w);
            int g = 0;
            MakeFish(w.Genome.Slot(0), brainGene, lengthGene);
            w.Spc[0] = 0; w.StageOf[0] = St.Adult; Body.Express(w, 0);
            w.Grown[0] = 1f; w.Age[0] = w.PMature[0] * 2f; w.Flags[0] = 0;
            int cell = -1;
            for (int c = 0; c < Map.FW * Map.FW && cell < 0; c++) if ((w.Cell[c] & (byte)Realm.Water) != 0) cell = c;
            w.Depth[cell] = 40f * w.ElevPerM;
            var b = new Budget { Mass = w.PM[0], LengthM = Body.LengthM(w, 0), CruiseMps = Respiration.CruiseMps(w, 0), DietEff = w.DietOf(0)[(int)Pool.Phyto] };
            Body.Dimensions(w.Genome[g + G.B_LENGTH], w.Genome[g + G.B_WIDTH], w.Genome[g + G.B_HEIGHT], out float L, out float Wd, out float H);
            b.Fineness = L / MathF.Sqrt(Wd * H);
            b.Level = Heat.Level(w.PEndo[0]);
            // the parts of its basal cost (Body.Express): Kleiber x level x factor, the gut built to it, the brain apart
            float m = w.PM[0], kle = K.C_BASAL * MathK.Pow(m, K.EXP_BASAL) * b.Level * w.Dt;
            float brain = K.COST_BRAIN * K.C_BASAL * w.PBrain[0] / K.BRAIN_REF * w.Dt;
            float fin = kle * K.COST_LIMB * w.PFins[0] * w.PFinPairs[0] * 0.5f, eye = kle * K.COST_EYE * w.PEyePairs[0];
            float gill = kle * Respiration.OrganUpkeep(w.PLungs[0]), wing = kle * K.COST_WING * w.PWings[0];
            float gutCost = K.GUT_SHARE * kle * (w.PAbsorb[0] / (K.C_ABSORB * MathK.Pow(m, K.EXP_BASAL) * b.Level * w.Dt));
            float body = w.PBasal[0] - brain - fin - eye - gill - wing - gutCost;
            float perDay = K.STEPS_PER_DAY / w.Dt;
            int steps = (int)MathF.Round(days * perDay);
            var dg = new Diag(); var d0 = Diag.D; Diag.D = dg;
            uint eat = 1u << (P.EAT - P.FIRST_ACTION);
            double net = 0, eatenKg = 0, swept = 0, cruiseCost = 0, diveCost = 0, rate = 0, core = 0, capE = 0;
            try
            {
                for (int s = 0; s < steps; s++)
                {
                    w.X[0] = (cell % Map.FW + 0.5f) * K.FCELL; w.Y[0] = (cell / Map.FW + 0.5f) * K.FCELL;
                    w.Pool[(int)Pool.Phyto][cell] = phyto; w.Temp[cell] = waterC;   // an ordinary standing stock, held
                    w.Energy[0] = 0.5f * w.EmaxOf(0); w.Thirst[0] = 0f; w.Fatigue[0] = 0f; w.O2Debt[0] = 0f;
                    w.IActs[0] = eat; w.ISpeed[0] = 0f; w.IHeading[0] = 0f; w.ITarget[0] = -1; w.IMate[0] = -1; w.IScanning[0] = 0; w.IFoodHere[0] = 1f;
                    var br = Respiration.Uptake(w, 0, cell, false, 0f, out _, out _);
                    float cruise = Respiration.FeedCruiseM(w, 0, 0f);
                    swept += (cruise + br.DiveM) * K.GAPE * MathF.Pow(K.BODY_L_M * MathK.Cbrt(m), 2f);
                    cruiseCost += Respiration.CruiseCost(w, 0, cruise); diveCost += Respiration.CruiseCost(w, 0, br.DiveM);
                    float p0 = w.Pool[(int)Pool.Phyto][cell], e0 = w.Energy[0];
                    Resolve.Run(w, 1, 0);
                    Assert.NotEqual(St.Dead, w.StageOf[0]);
                    eatenKg += (p0 - w.Pool[(int)Pool.Phyto][cell]) * Pools.Kg[(int)Pool.Phyto];
                    net += w.Energy[0] - e0;
                    float top = Climate.Water(waterC), c = Heat.Core(top, w.PEndo[0]);
                    core += c; rate += Heat.Activity(c, w.PEndo[0]);
                    capE += w.PAbsorb[0] * Heat.Rate(c);
                }
            }
            finally { Diag.D = d0; }
            float days1 = steps / perDay;
            b.CoreC = (float)(core / steps); b.RateT = (float)(rate / steps);
            float r = b.RateT * perDay;   // RESOLVE paces the whole basal cost by the core's rate
            b.BasalDay = (float)(dg.Basal[0] / days1); b.BodyDay = body * r; b.GutDay = gutCost * r; b.BrainDay = brain * r;
            b.FinDay = fin * r; b.EyeDay = eye * r; b.GillDay = gill * r; b.WingDay = wing * r;
            b.BreathDay = (float)(dg.Breath[0] / days1); b.ThermoDay = (float)(dg.Thermo[0] / days1); b.MoveDay = (float)(dg.Move[0] / days1);
            b.CruiseDay = (float)(cruiseCost / days1); b.DiveDay = (float)(diveCost / days1);
            b.SweptM3Day = (float)(swept / days1);
            b.KgPerM3 = phyto * Pools.Kg[(int)Pool.Phyto] / (Pools.CellM2 * Ocean.MixedM(40f));
            b.MgCPerM3 = phyto * MgCPerUnitM2 / Ocean.MixedM(40f);
            b.EatenKgDay = (float)(eatenKg / days1); b.EatenEDay = b.EatenKgDay * Pools.Rho[(int)Pool.Phyto];
            b.AbsorbedDay = (float)(dg.Absorbed[0] / days1); b.NetDay = (float)(net / days1); b.AbsorbCapDay = (float)(capE / days1);
            b.GapeM2 = K.GAPE * MathF.Pow(K.BODY_L_M * MathK.Cbrt(m), 2f);
            return b;
        });
    }

    static string KJ(float e) => $"{e * Pools.EnergyJ / 1000f,9:F2} kJ ({e,10:E3} E)";

    public static string Table(Budget b)
    {
        var s = new System.Text.StringBuilder();
        s.AppendLine($"fish {b.Mass:F2} kg, {b.LengthM:F3} m long (fineness {b.Fineness:F1}), cruise {b.CruiseMps:F2} m/s; core {b.CoreC:F1} C, level {b.Level:F2}, pace {b.RateT:F3}");
        s.AppendLine($"basal (charged)   {KJ(b.BasalDay)} /day");
        s.AppendLine($"  body+traits     {KJ(b.BodyDay)}");
        s.AppendLine($"  gut             {KJ(b.GutDay)}");
        s.AppendLine($"  brain           {KJ(b.BrainDay)}");
        s.AppendLine($"  fins            {KJ(b.FinDay)}");
        s.AppendLine($"  eyes            {KJ(b.EyeDay)}");
        s.AppendLine($"  gills           {KJ(b.GillDay)}");
        s.AppendLine($"  wings           {KJ(b.WingDay)}");
        s.AppendLine($"breathing         {KJ(b.BreathDay)}");
        s.AppendLine($"heat              {KJ(b.ThermoDay)}");
        s.AppendLine($"moving (charged)  {KJ(b.MoveDay)}  of it cruise {KJ(b.CruiseDay)}, dives {KJ(b.DiveDay)}");
        s.AppendLine($"gape {b.GapeM2 * 1e4f:F1} cm2; swept {b.SweptM3Day:F1} m3/day = {b.SweptM3Day * 1000f / 1440f:F1} L/min; plankton {b.MgCPerM3:F0} mg C/m3 ({b.KgPerM3 * 1e3f:F3} g sim/m3)");
        s.AppendLine($"eaten {b.EatenKgDay * 1000f:F1} g sim/day = {KJ(b.EatenEDay)}; absorbed {KJ(b.AbsorbedDay)} (eff {b.DietEff:F2}; gut's cap {KJ(b.AbsorbCapDay)})");
        s.AppendLine($"net               {KJ(b.NetDay)} /day; absorbed / costs {b.AbsorbedDay / MathF.Max(1e-12f, b.BasalDay + b.BreathDay + b.ThermoDay + b.MoveDay):F2}");
        return s.ToString();
    }

    /// <summary>Resting oxygen consumption of a teleost of m kg at tC, kJ/day: Clarke &amp; Johnston (1999), ln R = 15.7 -
    /// 5.02 (1000 / T) mmol O2/h at 50 g, scaled by M^0.80, at 434 kJ per mol O2 (Elliott &amp; Davison 1975).</summary>
    public static float ClarkeJohnstonKJ(float m, float tC)
        => MathK.Exp(15.7f - 5.02f * 1000f / (tC + 273.15f)) * MathK.Pow(m / 0.05f, 0.80f) * 24f * 1e-3f * 434f;

    [Fact] // the probe: the budget of a 1 kg plankton-eating fish, term by term, against a real one
    public void APlanktonEatingFishsBudget()
    {
        foreach (var (ph, t) in new[] { (0.3f, 15f), (0.1f, 15f), (0.3f, 20f) })
        {
            var b = Run(ph, t);
            float smr = ClarkeJohnstonKJ(b.Mass, t), kJ = Pools.EnergyJ / 1000f;
            float rest = (b.BasalDay + b.BreathDay) * kJ;   // its standard metabolism: tissues, organs and breathing
            float cruiseM = b.CruiseMps * K.FEED_DAY * 86400f, cot = b.CruiseDay * Pools.EnergyJ / cruiseM / b.Mass;
            float lpm = b.SweptM3Day * 1000f / 1440f;
            _out.WriteLine($"--- plankton {ph} units, water {t} C");
            _out.WriteLine(Table(b));
            _out.WriteLine($"literature: standard metabolism {smr:F1} kJ/day (Clarke & Johnston 1999) against {rest:F1}; swimming {cot:F2} J/(kg m) (fish ~1: Schmidt-Nielsen 1972; Videler & Nolet 1990)");
            // standard metabolism within a factor of two of a teleost's (Clarke & Johnston 1999: 21 kJ/day at 1 kg, 15 C)
            Assert.InRange(rest, 0.5f * smr, 2f * smr);
            // cruising at a fish's cost of transport, not running's 10.7 J/(kg m) (Taylor et al. 1982)
            Assert.InRange(cot, 0.3f, 2.5f);
            // clearance: menhaden of ~0.3 kg sweep 5.8-24.8 L/min (Durbin & Durbin 1975); x (1 / 0.3) for 1 kg by gape x speed ~ M
            Assert.InRange(lpm, 5.8f, 24.8f / 0.3f);
            // coastal plankton, 10-200 mg C/m3 (Chl 0.2-4 mg/m3 at C:Chl ~50; Geider 1987)
            Assert.InRange(b.MgCPerM3, 10f, 200f);
            // a specialist assimilates ~0.7-0.8 of plankton (Pools.DigMax 0.7; fishes on zooplankton 0.7-0.9: Brett & Groves 1979)
            Assert.InRange(b.DietEff, 0.6f, 0.85f);
            // and the budget closes: what it absorbs pays for what it spends
            Assert.True(b.NetDay > 0f, $"net {b.NetDay * kJ:F1} kJ/day");
        }
        _out.WriteLine("--- default brain gene (0), plankton 0.3, 15 C");
        _out.WriteLine(Table(Run(0.3f, 15f, 3f, 0)));
    }
}
