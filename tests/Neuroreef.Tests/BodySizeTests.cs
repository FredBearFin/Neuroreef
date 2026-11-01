// The world's body sizes (BodySize, 2026-11-01): the default is the old constants exactly, a world of giants or of tiny
// bodies founds and runs, a giant's senses reach as far as its hash neighbourhood, and saves keep the sizes.

using System;
using System.IO;
using System.Linq;
using Xunit;

namespace Neuroreef.Tests;

public class BodySizeTests
{
    /// <summary>Run f with these body sizes, then put the defaults (and the tests' map) back.</summary>
    static T With<T>(float min, float max, Func<T> f, float[]? floors = null)
    {
        try { BodySize.Set(min, max, floors); Map.Set(TestWorld.Side); return f(); }
        finally { BodySize.Reset(); Map.Set(TestWorld.Side); }
    }

    static void Report(string line)
    {
        // what a run of a giants or tiny world did, for whoever reads the test log (no assertion depends on it)
        Console.WriteLine(line);
        try { File.AppendAllText(Path.Combine(Path.GetTempPath(), "neuroreef-bodysize.txt"), $"{DateTime.Now:HH:mm:ss} {line}\n"); } catch (IOException) { }
    }

    [Fact] // the default range is the old constants, bit for bit: the floors, the senses' maxima and the hash cells
    public void DefaultIsTheOldConstants()
    {
        Assert.True(BodySize.IsDefault);
        Assert.Equal(new[] { 0.1f, 1f, 3f, 10f }, BodySize.Floors);
        Assert.Equal(new[] { 0.1f, 1f, 3f, 10f }, BodySize.ProportionalFloors(K.MASS_LO, K.MASS_HI));
        Assert.Equal(K.MAX_SIGHT, BodySize.MaxSight);
        Assert.Equal(K.MAX_HEAR, BodySize.MaxHear);
        Assert.Equal(K.NEWBORN_MIN, BodySize.NewbornMin);
        Assert.Equal(K.HCELL, Map.HCell);
        Assert.Equal(K.HCELL_CALL, Map.HCellCall);
        Assert.Equal(K.MAX_PERCEIVE, Map.MaxPerceive);
        foreach (int side in Map.Sizes)
        {
            Map.Set(side);
            Assert.Equal(side / K.HCELL, Map.GW); Assert.Equal(side / K.HCELL_CALL, Map.CGW);
        }
        Map.Set(TestWorld.Side);
        // the default walker's perception fits the default cells, as UnitsTests checks with its own formula
        Assert.True(BodySize.WalkerPerceive(K.MASS_HI) <= K.MAX_PERCEIVE);
    }

    [Fact] // a range scales the floors in the default's log proportions, the newborn floor with the smallest body, the senses with the largest
    public void ARangeScalesItsFloorsSensesAndCells()
    {
        With(100f, 20000f, () =>
        {
            float[] f = BodySize.Floors;
            Assert.Equal(100f, f[0]);
            for (int k = 1; k < K.FLOOR_CLASSES; k++)
            {
                float share = MathK.Log(new[] { 0.1f, 1f, 3f, 10f }[k] / 0.1f) / MathK.Log(400f);
                Assert.Equal(share, MathK.Log(f[k] / 100f) / MathK.Log(200f), 4);
            }
            Assert.Equal(10f, BodySize.NewbornMin, 3);
            Assert.Equal(K.MAX_SIGHT * MathK.Pow(500f, K.EXP_SIGHT), BodySize.MaxSight, 3);
            Assert.Equal(K.MAX_HEAR * MathK.Cbrt(500f), BodySize.MaxHear, 2);
            // the cells hold the largest perception and the loudest call, and divide the side
            Assert.True(Map.MaxPerceive >= BodySize.WalkerPerceive(20000f));
            Assert.True(2 * Map.HCellCall >= BodySize.MaxHear);
            Assert.True(Map.HCellCall >= Map.HCell);
            Assert.Equal(0, Map.W % Map.HCell); Assert.Equal(0, Map.W % Map.HCellCall);
            return 0;
        });
        // floors given are kept (inside the range, none below the one before); a range is clamped to its limits
        With(1f, 1000f, () => { Assert.Equal(new[] { 1f, 7f, 7f, 900f }, BodySize.Floors); return 0; }, floors: new[] { 1f, 7f, 5f, 900f });
        With(1e-6f, 1e9f, () => { Assert.Equal(BodySize.LIMIT_LO, BodySize.Min); Assert.Equal(BodySize.LIMIT_HI, BodySize.Max); return 0; });
        With(10f, 11f, () => { Assert.Equal(10f * BodySize.MIN_RATIO, BodySize.Max); return 0; });
        Assert.True(BodySize.IsDefault);
    }

    /// <summary>A world of these sizes: founded, then run; every body inside its class floor and the range, nothing NaN.</summary>
    static void RunWorld(string name, float min, float max, int steps, int founders)
    {
        With(min, max, () =>
        {
            var evo = new EvoSettings { Founders = founders };
            var t0 = DateTime.UtcNow;
            using var sim = new Simulation(2048, 7, 2, evo: evo);
            var w = sim.W;
            double build = (DateTime.UtcNow - t0).TotalSeconds;
            int founded = w.AliveCount;
            Assert.True(founded > 0, $"{name}: no animal founded");
            var masses = Enumerable.Range(0, w.Cap).Where(i => w.StageOf[i] != St.Dead).Select(i => w.PM[i]).OrderBy(m => m).ToArray();
            for (int i = 0; i < w.Cap; i++)
            {
                if (w.StageOf[i] == St.Dead) continue;
                Assert.Contains(w.PFloor[i], BodySize.Floors);
                Assert.InRange(w.PM[i], w.PFloor[i], BodySize.Max * 1.0001f);
                Assert.True(w.PM[i] >= BodySize.Min);
            }
            int clades = Enumerable.Range(0, w.Cap).Where(i => w.StageOf[i] != St.Dead).Select(i => w.Spc[i]).Distinct().Count();
            var t1 = DateTime.UtcNow;
            for (int k = 0; k < steps; k++)
            {
                sim.Step();
                if (k % 50 == 49 || k == steps - 1)
                    for (int i = 0; i < w.Cap; i++)
                    {
                        if (w.StageOf[i] == St.Dead) continue;
                        Assert.True(float.IsFinite(w.X[i]) && float.IsFinite(w.Y[i]) && float.IsFinite(w.Energy[i]) && float.IsFinite(w.Heading[i]),
                            $"{name}: slot {i} not finite at step {w.Step}");
                        Assert.True(float.IsFinite(w.PVmax[i]) && w.PVmax[i] >= 0f, $"{name}: slot {i} speed {w.PVmax[i]}");
                    }
            }
            double secs = (DateTime.UtcNow - t1).TotalSeconds;
            int adults = Enumerable.Range(0, w.Cap).Count(i => w.StageOf[i] == St.Adult);
            Report($"{name} ({BodySize.Describe()}; cells {Map.HCell}/{Map.HCellCall} on a {Map.W} map): founded {founded} animals in {clades} lineages " +
                $"(masses {BodySize.Kg(masses[0])}, median {BodySize.Kg(masses[masses.Length / 2])}, max {BodySize.Kg(masses[^1])}); after {steps} steps " +
                $"{w.AliveCount} alive ({adults} adults) in {Enumerable.Range(0, w.Cap).Where(i => w.StageOf[i] != St.Dead).Select(i => w.Spc[i]).Distinct().Count()} lineages; " +
                $"{steps / secs:F1} steps/s; world built in {build:F0} s");
            return 0;
        });
    }

    [Fact] // a world of giants (100 kg - 20 t) founds bodies inside its range and runs without NaN or a crash
    public void AGiantsWorldFoundsAndRuns() => RunWorld("giants", 100f, 20000f, 300, 200);

    [Fact] // a world of tiny bodies (1 g - 1 kg) founds bodies inside its range and runs without NaN or a crash
    public void ATinyWorldFoundsAndRuns() => RunWorld("tiny", 0.001f, 1f, 120, 60);

    [Theory] // a giant scanning on the run perceives another at the far edge of its hash neighbourhood (49 cells of a wider
    // grid); on a 128 map the grid is narrower than the neighbourhood (4 cells), and the other is still counted once
    [InlineData(256, 0.97f)]
    [InlineData(128, 0.6f)]
    public void AGiantSensesItsFarthestNeighbour(int side, float share)
    {
        With(100f, 20000f, () =>
        {
            Map.Set(side);
            var w = new World(64, 17);
            Terrain.Build(w);
            Array.Fill(w.Cell, (byte)Realm.Land);
            w.Sp[0].Used = true; w.Sp[0].Alive = true;
            int i = Life.Found(w, 0, ReadOnlySpan<sbyte>.Empty, true, 1);
            Assert.True(i >= 0);
            var gi = w.GenomeOf(i);
            gi[G.B_MASS] = 127; gi[G.B_FLOOR] = 3; gi[G.B_SIGHT] = 127; gi[G.B_FOV] = -127; gi[G.B_LEGS] = 127; gi[G.B_ARMOUR] = -127;
            Body.Express(w, i);
            Assert.True(w.PM[i] > 15000f);
            int j = Life.Found(w, 0, ReadOnlySpan<sbyte>.Empty, true, 2);
            Assert.True(j >= 0);
            var gj = w.GenomeOf(j);
            gj[G.B_ARMOUR] = 127; gj[G.B_STRIKE] = 127; gj[G.B_MASS] = -127;   // another kind
            Body.Express(w, j);
            Assert.False(Body.LooksAlikeCached(w, i, j));
            w.Flags[i] = 0; w.Flags[j] = 0;
            w.IScanning[i] = 1;
            w.Speed[i] = 3f * w.PVmax[i];   // as far as it may go this step: the perception the neighbourhood must hold at its largest
            float R = Decide.Perceive(w.PSight[i], true, w.Speed[i]);
            Assert.True(R > 2 * Map.HCell, $"R {R} within 5x5 cells of {Map.HCell}");
            Assert.True(R > K.MAX_PERCEIVE, $"R {R}: no farther than a default world's");
            // the other at 97% of that, diagonally (across cells in both axes), from a giant near a cell corner
            w.X[i] = 3.5f * Map.HCell + 0.2f; w.Y[i] = 2.5f * Map.HCell - 0.3f; w.Heading[i] = 0.7f;
            float d = share * R;
            w.X[j] = World.Wrap(w.X[i] + d * MathK.Cos(0.7f)); w.Y[j] = World.Wrap(w.Y[i] + d * MathK.Sin(0.7f));
            Simulation.BuildFor(w);
            Decide.Run(w, i, new Decide.ProbeBuf());
            float close = w.Feat[i * F.N + F.O1_CLOSE];
            Assert.Equal(1f - share, close, 3);
            Assert.Equal(MathK.Tanh(close), w.Feat[i * F.N + F.OTHERS], 5);   // one other, counted once
            return 0;
        });
    }

    [Fact] // the body sizes round-trip through a world save; a save before format 23 loads with the default sizes
    public void SavesKeepTheBodySizes()
    {
        string path = Path.GetTempFileName(), old = Path.GetTempFileName();
        try
        {
            var floors = new[] { 100f, 400f, 2500f, 9000f };
            With(100f, 20000f, () =>
            {
                var w = new World(16, 5, 5);
                Checkpoint.SaveWorld(w, path);
                return 0;
            }, floors);
            Assert.True(BodySize.IsDefault);
            Checkpoint.WorldHeader(path);   // applies the sizes it was made with, as the map and nature
            Assert.Equal(100f, BodySize.Min); Assert.Equal(20000f, BodySize.Max);
            Assert.Equal(floors, BodySize.Floors);
            Assert.True(Map.HCell > K.HCELL);
            // a format-22 header: the nature, then the object tree (no body sizes)
            using (var bw = new BinaryWriter(File.Create(old)))
            {
                bw.Write("NRWK"u8.ToArray()); bw.Write((ushort)22);
                bw.Write(G.LEN); bw.Write(16); bw.Write(5u); bw.Write(5u); Nature.Write(bw);
            }
            Checkpoint.WorldHeader(old, out int v);
            Assert.Equal(22, v);
            Assert.True(BodySize.IsDefault);
            Assert.Equal(K.HCELL, Map.HCell);
        }
        finally
        {
            BodySize.Reset(); Map.Set(TestWorld.Side);
            File.Delete(path); File.Delete(old);
        }
    }
}
