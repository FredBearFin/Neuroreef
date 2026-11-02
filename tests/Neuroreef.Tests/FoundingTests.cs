// Founding on the food that exists (2026-11-01): an ancestor that eats what animals make (meat, carrion, dung) is
// tested on the animals living in its home range, as a plant eater is on the plants. A meat-eater beside enough
// catchable prey can be founded; the same body with no prey cannot. Nothing is forced: the test is Life.RangeFor's
// capacity at FOUND_FILL against the smallest group that can breed (Life.BreedingGroup), the same for every ancestor
// (Simulation.FoundAncestor, FoundersFed), and it starts with as many founders as that food feeds.

using System;
using Xunit;

namespace Neuroreef.Tests;

public class FoundingTests
{
    /// <summary>A land walker of the given mass gene and size class that digests one food well and nothing else.</summary>
    static int Animal(World w, int clade, uint key, Pool food, sbyte mass, sbyte floor, sbyte strike, bool adult = true)
    {
        w.Sp[clade].Used = true; w.Sp[clade].Alive = true;
        int c = Life.Found(w, clade, ReadOnlySpan<sbyte>.Empty, true, key + 1);
        Assert.True(c >= 0);
        int g = c * G.LEN;
        w.Genome[g + G.B_LEGS] = 100; w.Genome[g + G.B_FINS] = -127; w.Genome[g + G.B_WINGS] = -127;   // a walker
        w.Genome[g + G.B_LUNGS] = 127; w.Genome[g + G.B_DIVE] = -127;                                  // breathes air
        w.Genome[g + G.B_MASS] = mass; w.Genome[g + G.B_FLOOR] = floor;
        w.Genome[g + G.B_BRAIN] = Body.UnitsGene(K.BRAIN_MIN);
        for (int p = 0; p < Pools.N; p++) w.Genome[g + G.B_DIET + p] = -127;
        w.Genome[g + G.B_DIET + (int)food] = 127;
        w.Genome[g + G.B_STRIKE] = strike; w.Genome[g + G.B_ARMOUR] = -127;
        w.Genome[g + G.B_SEX] = 40;                                                                    // sexual
        w.Genome[g + G.B_HERM] = (sbyte)(food == Pool.Grass ? 40 : -40);                              // every grazer bears young
        w.Genome[g + G.B_SPEED] = 0; w.Genome[g + G.B_SIGHT] = 0; w.Genome[g + G.B_EYEN] = -127; w.Genome[g + G.B_LIFE] = 0;
        w.Genome[g + G.B_IMMUNE] = -127; w.Genome[g + G.B_WATER] = -127; w.Genome[g + G.B_VOICE] = -127; w.Genome[g + G.B_INSUL] = -127; w.Genome[g + G.B_APPEND] = -127;
        Body.Express(w, c);
        w.Flags[c] = 0; w.StageOf[c] = adult ? St.Adult : St.Juvenile; w.Grown[c] = adult ? 1f : 0.5f;
        w.Age[c] = adult ? w.PMature[c] * 2 : w.PMature[c] * 0.5f;
        return c;
    }

    /// <summary>The middle of the largest stretch of land: a land cell whose whole home range (HOME_R) is land, or the most of it.</summary>
    static (float x, float y) LandHome(World w)
    {
        float r = K.HOME_R * Map.W, cs = Map.W / (float)Map.FW;
        int rc = (int)MathF.Ceiling(r / cs), best = -1, bestN = -1;
        for (int cell = 0; cell < Map.FW * Map.FW; cell += 97)
        {
            if (w.Cell[cell] != (byte)Realm.Land) continue;
            int cx = cell % Map.FW, cy = cell / Map.FW, n = 0;
            for (int dy = -rc; dy <= rc; dy += 4)
                for (int dx = -rc; dx <= rc; dx += 4)
                    if (w.Cell[World.WrapI(cy + dy, Map.FW) * Map.FW + World.WrapI(cx + dx, Map.FW)] == (byte)Realm.Land) n++;
            if (n > bestN) { bestN = n; best = cell; }
        }
        return (((best % Map.FW) + 0.5f) * cs, ((best / Map.FW) + 0.5f) * cs);
    }

    /// <summary>A world with no plants (land and water only, no spin-up), so whatever feeds a founder is animal food.</summary>
    static World Bare(uint seed, int slots)
    {
        var w = new World(slots, seed); Terrain.Build(w);
        for (int p = 0; p < Pools.NFIELD; p++) Array.Clear(w.Pool[p]);
        return w;
    }

    /// <summary>Place n plant eaters of clade 1 on land within the home range around (hx, hy), a third of them young.</summary>
    static void Prey(World w, int n, float hx, float hy, sbyte strike)
    {
        float r = 0.8f * K.HOME_R * Map.W;
        for (int k = 0; k < n; k++)
        {
            int v = Animal(w, 1, 100u + (uint)k, Pool.Grass, 0, 1, strike, adult: k % 3 != 0);
            Assert.True(Life.HomeSite(w, Realm.Land, hx, hy, 5000u + (uint)k, out float x, out float y, r));
            w.X[v] = x; w.Y[v] = y;
        }
    }

    [Fact] // a small meat-eater with prey around it that cannot strike back: its range feeds a founding group on them (their meat, and the carrion they leave)
    public void AMeatEaterBesideEnoughPreyIsFounded()
    {
        float climate = Nature.Climate; Nature.Climate = 0f;
        try
        {
            var w = Bare(41, 2048);
            var (hx, hy) = LandHome(w);
            Prey(w, 3000, hx, hy, strike: -127);
            int a = Animal(w, 0, 1, Pool.Meat, -40, 0, 0);
            w.X[a] = hx; w.Y[a] = hy;
            Assert.True(Simulation.EatsAnimalFood(w, a));
            Life.RangeFor(w, a, hx, hy, out float cap);
            Assert.True(Simulation.FoundersFed(cap, Life.BreedingGroup(w, a), w.Evo.Founders) >= Life.BreedingGroup(w, a), $"capacity {cap} for a {w.PM[a]:F2} kg meat-eater beside 3000 prey");
            // and its food is the prey: the same range with the prey gone feeds none
            for (int i = 0; i < w.Cap; i++) if (w.StageOf[i] != St.Dead && w.Spc[i] == 1) Life.FreeSlot(w, i);
            Life.RangeFor(w, a, hx, hy, out float none);
            Assert.Equal(0f, none);
        }
        finally { Nature.Climate = climate; }
    }

    [Fact] // the same meat-eater alone is not founded; among prey whose counterblow outweighs its strike only their young feed it
    public void AMeatEaterWithNoPreyIsNotFounded()
    {
        float climate = Nature.Climate; Nature.Climate = 0f;
        try
        {
            var w = Bare(43, 2048);
            var (hx, hy) = LandHome(w);
            int a = Animal(w, 0, 1, Pool.Meat, -40, 0, 0);
            w.X[a] = hx; w.Y[a] = hy;
            Life.RangeFor(w, a, hx, hy, out float cap);
            Assert.Equal(0f, cap);
            // prey it cannot handle as adults: big-weaponed bodies whose blow back outweighs its strike, so only their
            // young (and the carrion they leave) are its food
            Prey(w, 3000, hx, hy, strike: 127);
            int v = FirstOf(w, 1);
            Assert.True(w.PStrike[a] * w.PM[a] < w.PStrike[v] * w.PM[v]);
            Life.RangeFor(w, a, hx, hy, out float armed);
            // the same number of prey that cannot strike back feed it more
            for (int i = 0; i < w.Cap; i++) if (w.StageOf[i] != St.Dead && w.Spc[i] == 1) Life.FreeSlot(w, i);
            Prey(w, 3000, hx, hy, strike: -127);
            Life.RangeFor(w, a, hx, hy, out float unarmed);
            Assert.True(armed < 0.75f * unarmed, $"capacity {armed} among prey that out-strike it, {unarmed} among prey that do not");
        }
        finally { Nature.Climate = climate; }
    }

    [Fact] // carrion and dung are tested on what exists too: a carrion-eater among plant eaters is fed by their carcasses,
           // alone it cannot; the dung they leave is counted for a dung-eater, and none without them
    public void CarrionAndDungEatersAreTestedOnWhatAnimalsLeave()
    {
        float climate = Nature.Climate; Nature.Climate = 0f;
        try
        {
            var w = Bare(47, 2048);
            var (hx, hy) = LandHome(w);
            int a = Animal(w, 0, 1, Pool.Carrion, -40, 0, -127);
            int d = Animal(w, 0, 2, Pool.Dung, -40, 0, -127);
            w.X[a] = w.X[d] = hx; w.Y[a] = w.Y[d] = hy;
            Assert.True(Simulation.EatsAnimalFood(w, a) && Simulation.EatsAnimalFood(w, d));
            float r = K.HOME_R * Map.W;
            Assert.Equal(0f, Life.RangeCapacity(w, a, hx, hy, r));
            Assert.Equal(0.0, Life.AnimalFood(w, d, hx, hy, r, Realm.Land, w.DietOf(d), K.FIELD_METAB * w.PBasal[d]).Dung);
            Prey(w, 3000, hx, hy, strike: -127);
            Life.RangeFor(w, a, hx, hy, out float cap);
            Assert.True(cap > 0f, "a carrion-eater among 3000 plant eaters is fed by none");
            var dung = Life.AnimalFood(w, d, hx, hy, r, Realm.Land, w.DietOf(d), K.FIELD_METAB * w.PBasal[d]);
            Assert.True(dung.Dung > 0.0 && dung.DungKg > 0.0, "the dung of 3000 plant eaters is not counted");
        }
        finally { Nature.Climate = climate; }
    }

    [Fact] // no fixed founding minimum (2026-11-01): a meat-eater whose prey feed about ten of it starts with about ten,
           // and with fewer prey, too few to feed a breeding pair, it is not founded
    public void AMeatEaterStartsWithAsManyFoundersAsItsPreyFeed()
    {
        float climate = Nature.Climate; Nature.Climate = 0f;
        try
        {
            var w = Bare(53, 2048);
            var (hx, hy) = LandHome(w);
            int a = Animal(w, 0, 1, Pool.Meat, -40, 0, 0);
            w.X[a] = hx; w.Y[a] = hy;
            Assert.Equal(2, Life.BreedingGroup(w, a));   // separate sexes: a pair
            Prey(w, PreyForTen, hx, hy, strike: -127);
            int n = Simulation.FedFounders(w, a, hx, hy, w.Evo.Founders, out _);
            Assert.InRange(n, 6, 15);
            // take prey away until its range feeds less than a pair at FOUND_FILL: it is not founded
            int left = PreyForTen;
            for (int i = 0; i < w.Cap && left > PreyForTen / 10; i++)
                if (w.StageOf[i] != St.Dead && w.Spc[i] == 1) { Life.FreeSlot(w, i); left--; }
            Life.RangeFor(w, a, hx, hy, out float cap);
            Assert.True(cap > 0f && K.FOUND_FILL * cap < 1.5f, $"capacity {cap} with {left} prey");
            Assert.Equal(0, Simulation.FedFounders(w, a, hx, hy, w.Evo.Founders, out _));
        }
        finally { Nature.Climate = climate; }
    }

    const int PreyForTen = 600;

    [Fact] // the smallest breeding group follows the sex system: one for a body that reproduces alone, two otherwise
    public void TheSmallestFoundingGroupIsABreedingGroup()
    {
        var w = Bare(59, 1024);
        int a = Animal(w, 0, 1, Pool.Grass, 0, 1, -127);
        w.Genome[a * G.LEN + G.B_SEX] = -40; Body.Express(w, a);
        Assert.Equal(0, w.PSexual[a]);
        Assert.Equal(1, Life.BreedingGroup(w, a));
        w.Genome[a * G.LEN + G.B_SEX] = 40; Body.Express(w, a);
        Assert.Equal(2, Life.BreedingGroup(w, a));
        Assert.Equal(0, Simulation.FoundersFed(4f, 2, 600));   // a quarter of 4 is one: no pair
        Assert.Equal(1, Simulation.FoundersFed(4f, 1, 600));   // one that breeds alone can start a lineage
        Assert.Equal(600, Simulation.FoundersFed(1e6f, 2, 600));
    }

    static int FirstOf(World w, int clade)
    {
        for (int i = 0; i < w.Cap; i++) if (w.StageOf[i] != St.Dead && w.Spc[i] == clade) return i;
        return -1;
    }
}
