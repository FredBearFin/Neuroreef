using System;
using Xunit;
using Xunit.Abstractions;

namespace Neuroreef.Tests;

/// <summary>
/// The metabolic level of cold- and warm-blooded bodies (2026-11-02, Heat.Level, in every world), the temperature pace on
/// top of it (Heat.Activity), and body shape against drag (Body.Streamlining).
/// </summary>
public class MetabolismTests
{
    readonly ITestOutputHelper _out;
    public MetabolismTests(ITestOutputHelper o) { _out = o; }

    static World Body0(sbyte endo, sbyte length = 0, sbyte width = 0, sbyte height = 0, sbyte fins = 0)
    {
        var w = new World(16, 3);
        Terrain.Build(w);
        w.Genome[G.B_MASS] = 0; w.Genome[G.B_ENDO] = endo; w.Genome[G.B_BRAIN] = -127; w.Genome[G.B_FINS] = fins;
        w.Genome[G.B_LENGTH] = length; w.Genome[G.B_WIDTH] = width; w.Genome[G.B_HEIGHT] = height;
        w.Spc[0] = 0; w.StageOf[0] = St.Adult; Body.Express(w, 0);
        return w;
    }

    static float BodyBasal(World w) => w.PBasal[0] - K.COST_BRAIN * K.C_BASAL * w.PBrain[0] / K.BRAIN_REF * w.Dt;

    [Fact] // a cold-blooded body rests at ~1/5 of a warm-blooded one of its mass at the same body temperature (Bennett & Ruben 1979; White et al. 2006)
    public void AnEctothermRestsAtAFifthOfAnEndotherm()
    {
        var cold = Body0(-127); var warm = Body0(127); var mid = Body0(0);
        float r = BodyBasal(cold) / BodyBasal(warm), rm = BodyBasal(mid) / BodyBasal(warm);
        _out.WriteLine($"{warm.PM[0]:F2} kg: resting cost cold/warm {r:F3}, half-way {rm:F3}; digestion cold/warm {cold.PDig[0] / warm.PDig[0]:F3}");
        Assert.InRange(r, 0.15f, 0.25f);
        Assert.InRange(rm, MathF.Sqrt(K.ECTO_LEVEL) * 0.9f, MathF.Sqrt(K.ECTO_LEVEL) * 1.1f);   // on a log scale between
        Assert.True(cold.PDig[0] < 0.3f * warm.PDig[0], "its gut runs at its tissues' pace");
        Assert.Equal(1f, Heat.Level(1f), 5);
    }

    [Fact] // the temperature pace on top: an ectotherm at 20 °C rests at ~1/20 of a mammal at 38 °C, near field rates (Nagy 2005)
    public void ThePaceOfTheCoreIsOnTop()
    {
        float c0 = Nature.Climate; Nature.Climate = 1f;
        try
        {
            float rate20 = Heat.Activity(20f, 0f);
            Assert.Equal(Heat.Rate(20f), rate20, 5);   // the level is not applied twice
            Assert.Equal(1f, Heat.Activity(K.T_BODY, 1f), 5);
            float total = Heat.Level(0f) * rate20;
            _out.WriteLine($"Rate(20 °C) {rate20:F3}; a cold-blooded body at 20 °C burns {total:F3} of a warm-blooded one at {K.T_BODY} °C (1/{1f / total:F0})");
            Assert.InRange(1f / total, 10f, 30f);
        }
        finally { Nature.Climate = c0; }
    }

    [Fact] // a long, slender body swims faster than a cube of its mass: Hoerner's drag at a given volume is least near fineness 4.5
    public void ALongBodySwimsFasterThanACube()
    {
        var cube = Body0(0, fins: 127); var fish = Body0(0, length: 127, width: -100, height: -100, fins: 127);
        float f = Body.LengthM(fish, 0);
        _out.WriteLine($"{fish.PM[0]:F2} kg: cube {Body.LengthM(cube, 0):F3} m swims {cube.PVWater[0]:F1}; long body {f:F3} m swims {fish.PVWater[0]:F1} units a step");
        Assert.True(fish.PVWater[0] > 1.2f * cube.PVWater[0]);
        Assert.InRange(f, 0.4f, 0.6f);   // a 2 kg fish is ~0.5 m
        Assert.True(Body.VolumeDrag(4.5f) < Body.VolumeDrag(1f) && Body.VolumeDrag(4.5f) < Body.VolumeDrag(15f));
        // the reach is there: the longest body the genes make
        var eel = Body0(0, length: 127, width: -127, height: -127);
        Assert.True(Body.LengthM(eel, 0) > 0.5f);
    }
}
