// Real units (2026-11-01): a step is STEP_H hours, a unit M_PER_UNIT metres, masses kg; the animals' senses, moves and
// reach are set in metres from allometries (Constants.cs §1b), and the spatial hash covers the largest of them.

using System;
using System.Linq;
using System.Text.Json;
using Neuroreef.Server;
using Xunit;

namespace Neuroreef.Tests;

public class UnitsTests
{
    [Fact] // one clock: T_SEASON steps are a year of 365.25 days; the derived literals in Constants.cs match their formulas
    public void TheClockAndTheDerivedConstantsAgree()
    {
        Assert.Equal(365.25f * 24f, K.STEP_H * K.T_SEASON, 2);
        Assert.InRange(K.STEP_H, 2.9f, 2.95f);
        Assert.Equal(54.5f, K.M_PER_UNIT, 3);
        Assert.Equal(2.0913f, MathK.Pow(K.MASS_HI, K.EXP_SIGHT), 3);   // MAX_SIGHT's literal
        Assert.Equal(3.41995f, MathK.Cbrt(K.MASS_HI), 4);              // MAX_HEAR's literal
        // the cost of moving a metre is Taylor et al.'s on Kleiber's energy scale
        Assert.Equal(K.COT_J, K.C_MOVE / K.M_PER_UNIT / K.C_BASAL * K.BMR_W * K.STEP_S, 3);
        // a step's top travel is the day range over the share of top speed the world's animals use
        Assert.Equal(K.DMD_M / K.STEPS_PER_DAY / K.TRAVEL_SHARE, K.C_SPEED * K.M_PER_UNIT, 1);
    }

    [Fact] // the hashes hold everything an animal can perceive or hear: the neighbour search must not miss any
    public void TheSpatialHashesCoverTheLargestRanges()
    {
        Assert.True(K.MAX_SIGHT <= K.MAX_PERCEIVE);
        Assert.True(K.MAX_PERCEIVE <= 3 * K.HCELL);
        Assert.True(K.MAX_HEAR <= 2 * K.HCELL_CALL);
        Assert.Equal(49, Decide.Offsets(K.MAX_PERCEIVE));
        Assert.Equal(9, Decide.Offsets(K.HCELL));
        foreach (int side in Map.Sizes) { Assert.Equal(0, side % K.HCELL); Assert.Equal(0, side % K.HCELL_CALL); }
        // the fastest walker (the largest, fastest-legged body) perceiving along its way stays inside the neighbourhood
        float vMax = K.C_SPEED * MathK.Pow(K.MASS_HI, K.EXP_SPEED) * K.BODY_RANGE_TRAIT * 1.3f * (1f + K.GLIDE);
        Assert.True(MathF.Sqrt(K.MAX_SIGHT * K.MAX_SIGHT + 2f * K.MAX_SIGHT * vMax / MathF.PI) < K.MAX_PERCEIVE, $"walker {vMax:F1} units a step");
        // perception grows with the way travelled as the band an eye sweeps: area pi R^2 + 2 R d
        float r = 1.2f, d = 10f, p = Decide.Perceive(r, false, d);
        Assert.Equal(MathF.PI * r * r + 2f * r * d, MathF.PI * p * p, 2);
        Assert.Equal(r, Decide.Perceive(r, false, 0f), 5);
    }

    [Fact] // bodies, sprint and reach at 1 kg, from the cited allometries
    public void BodiesSprintAndReachInMetres()
    {
        Assert.Equal(0.1f, Body.LengthM(1f, 1f), 5);
        Assert.Equal(0.1f * MathK.Cbrt(40f), Body.LengthM(40f, 1f), 5);
        Assert.InRange(Body.SprintMps(1f), 8.3f, 8.4f);        // 30 km/h (Garland 1983)
        Assert.InRange(Body.SprintMps(0.1f), 3.9f, 4.1f);      // 14 km/h
        Assert.InRange(Body.SprintMps(40f), 14.8f, 15.2f);     // 54 km/h
        Assert.InRange(Body.ReachM(1f, 1f, 0.1f), 120f, 130f);
        Assert.Equal(0.1f, Body.ReachM(1f, 0f, 0.1f), 5);      // a body that cannot move reaches its own length
        Assert.InRange(Life.HomeRangeR(1f) * K.M_PER_UNIT, 95f, 100f);   // 3 ha
    }

    [Fact] // an expressed body's senses and moves are in metres: sight ~60 m and a step's travel ~630 m at 1 kg
    public void AnExpressedBodyIsInMetres()
    {
        var w = new World(16, 3);
        for (int g = -127; g <= 127; g += 32)
        {
            w.Genome[G.B_MASS] = (sbyte)g; w.Genome[G.B_FLOOR] = 0; w.Genome[G.B_LEGS] = 127; w.Genome[G.B_WINGS] = -127;
            w.Genome[G.B_SIGHT] = 0; w.Genome[G.B_SPEED] = 0;
            Body.Express(w, 0);
            float m = w.PM[0], sightM = w.PSight[0] * K.M_PER_UNIT;
            Assert.InRange(sightM, 0.6f * K.C_SIGHT_M * MathK.Pow(m, 0.2f) * 0.6f, 1.3f * K.C_SIGHT_M * MathK.Pow(m, 0.2f));
            Assert.True(w.PSight[0] <= K.MAX_SIGHT);
            Assert.True(w.PLen[0] > 0.04f * MathK.Cbrt(m) && w.PLen[0] < 0.26f * MathK.Cbrt(m), $"M {m}: length {w.PLen[0]} m");
            Assert.True(w.PReach[0] * K.M_PER_UNIT > w.PLen[0]);
        }
    }

    [Fact] // the console gets the units and every organism's body length in metres (ICD §2 status: units, bodyLenM)
    public void TheConsoleGetsUnitsAndBodyLengths()
    {
        using var sim = new Simulation(8192, 21, 1);
        sim.Step();
        var w = sim.W;
        var json = JsonSerializer.Serialize(Wire.Units());
        using var doc = JsonDocument.Parse(json);
        Assert.Equal(K.M_PER_UNIT, doc.RootElement.GetProperty("metresPerUnit").GetSingle(), 3);
        Assert.Equal(K.STEP_H, doc.RootElement.GetProperty("stepHours").GetSingle(), 3);
        var b = Wire.BodyLengths(w);
        Assert.Equal(0, b.Length % 2);
        int alive = 0; for (int i = 0; i < w.Cap; i++) if (w.StageOf[i] != St.Dead && w.StageOf[i] != St.Embryo) alive++;
        Assert.Equal(2 * alive, b.Length);
        for (int k = 0; k < b.Length; k += 2)
        {
            int i = sim.FindSerial((uint)b[k]);
            Assert.True(i >= 0);
            Assert.Equal(Body.LengthM(w, i), b[k + 1], 3);
            Assert.InRange(b[k + 1], 0.001, 1.0);
        }
    }
}
