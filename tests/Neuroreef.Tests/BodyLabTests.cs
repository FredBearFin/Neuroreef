// The Body Lab's record of a body-plan run (Server/BodyLabData.cs, BodyLab.cs): a file written from a real (short) run
// reads back with the same genomes, which grow into bodies of the same fitness, and every part has a physics role.

using System;
using System.IO;
using System.Linq;
using Neuroreef.Morph;
using Neuroreef.Server;
using Xunit;

namespace Neuroreef.Tests;

public class BodyLabTests
{
    [Fact]
    public void ALabFileRoundTripsToTheSameBodies()
    {
        string dir = Path.Combine(Path.GetTempPath(), "nr-bodylab-test-" + Guid.NewGuid().ToString("N"));
        try
        {
            var (res, run) = BodyLab.RunOne("sea-floor", 5, 30, 40, 7, dir, false);
            Assert.True(res != null, run.Error);
            Assert.True(run.Done);
            Assert.Equal(Path.Combine(dir, BodyLab.FileName("sea-floor", 5)), run.Path);
            var f = LabReport.Deserialize(File.ReadAllText(run.Path!));

            Assert.Equal("neuroreef-bodylab", f.Format);
            Assert.Equal("sea-floor", f.EnvKey); Assert.Equal("sea floor", f.Env);
            Assert.Equal(5u, f.Seed); Assert.Equal(40, f.Population); Assert.Equal(30, f.Generations);
            Assert.True(f.Done);
            // generations 0, 7, 14, 21, 28 and the last (29)
            Assert.Equal(new[] { 0, 7, 14, 21, 28, 29 }, f.Snapshots.Select(s => s.Gen).ToArray());
            Assert.Equal(0, f.Snapshots[0].Champion.PairCount);       // the founders are blobs (no appendage gene)
            Assert.Single(f.Snapshots[0].Champion.Segs);
            Assert.NotNull(f.Final); Assert.NotNull(f.Table); Assert.Contains("sea floor", f.Table);

            // the genome is exact: it grows into the same body with the same fitness
            var env = MorphEnv.SeaFloor();
            var g = f.Final!.Champion.Genome.ToGenome();
            var e = Physics.Evaluate(Development.Grow(g), env);
            Assert.Equal(res!.BestEval.Fitness, e.Fitness);
            Assert.Equal(res.Best.Segs.Count, g.Segs.Count);
            Assert.Equal(res.Best.MassG, g.MassG);
            for (int i = 0; i < g.Segs.Count; i++)
            {
                Assert.Equal(res.Best.Segs[i].Innov, g.Segs[i].Innov);
                Assert.Equal(res.Best.Segs[i].P, g.Segs[i].P);
                Assert.Equal(res.Best.Segs[i].Apps.Count, g.Segs[i].Apps.Count);
            }
            Assert.Equal(res.Species - 1, f.Final.Reps.Count);   // the champion and every other species' best
            foreach (var s in f.Snapshots)
            {
                var body = s.Champion;
                var again = Physics.Evaluate(Development.Grow(body.Genome.ToGenome()), env);
                Assert.Equal(again.Fitness, body.Fitness, 3);      // written to 4 significant figures
                Assert.Equal(body.Segments, body.Segs.Count);
                Assert.Equal(body.PairCount, body.Pairs.Count);
                Assert.All(body.Segs, x => { Assert.NotEmpty(x.Roles); Assert.False(string.IsNullOrEmpty(x.Primary)); });
                Assert.All(body.Pairs, x => { Assert.NotEmpty(x.Roles); Assert.False(string.IsNullOrEmpty(x.Use)); });
            }
            // and it writes back the same
            Assert.Equal(LabReport.Serialize(f), File.ReadAllText(run.Path!));
        }
        finally { try { Directory.Delete(dir, true); } catch { } }
    }

    [Fact] // a part's role comes from the physics' numbers: a body whose trunk alone swims gives its segments the thrust
    public void RolesComeFromThePhysics()
    {
        var g = MorphGenome.Blob();
        g.Segs[0].P[SP.LEN] = 1f; g.Segs[0].P[SP.AXMUS] = 1f;   // a long, muscular trunk
        var b = LabReport.Body(g, MorphEnv.OpenWater());
        Assert.Equal("swims", b.Mode);
        Assert.Contains(b.Segs[0].Roles, r => r.Role == "thrust" && r.Share > 0.9f);
        Assert.Empty(b.Pairs);
    }
}
