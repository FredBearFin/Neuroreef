using System;
using Xunit;
using Xunit.Abstractions;

namespace Neuroreef.Tests;

/// <summary>
/// The nutrient budget (Nutrients.cs, 2026-11-02): every flow moves nutrient from one store to another, so a closed world
/// keeps its total, and an open one changes by its inputs less its outputs. (Until 2026-11-02 a unit of growth took 0.6
/// and its rot gave back 0.7, carrion and dung gave 0.7 per land unit's kg, and the clamps at 0 and N_MAX made and
/// destroyed nutrient unseen.)
/// </summary>
public class NutrientBalanceTests
{
    readonly ITestOutputHelper _out;
    public NutrientBalanceTests(ITestOutputHelper o) { _out = o; }

    const int Steps = 300;

    static (Nutrients.Totals t0, Nutrients.Totals t1, Nutrients.Book book) Run(bool open, uint seed)
    {
        using var sim = new Simulation(64, seed, 1);
        var w = sim.W;
        w.NutBook.Open = open;
        w.NutBook.Clear();
        var t0 = Nutrients.Total(w);
        for (int k = 0; k < Steps; k++) sim.Step();
        return (t0, Nutrients.Total(w), w.NutBook);
    }

    // float stores of a few thousand cells, a step's flows rounded in each: the sum drifts by rounding alone (measured
    // ~1e-7 of the total over 300 steps)
    static double Tol(Nutrients.Totals t) => 1e-6 * t.All;
    // debt: nutrient made because a newborn's or a growing body's need exceeded its mother's gut and the soil or water of
    // its cell (measured 2026-11-02: ~1.5e-5 of the total over 300 steps on this world, all from animals). It is counted,
    // not hidden; this bounds it
    static double DebtMax(Nutrients.Totals t) => 1e-4 * t.All;

    [Fact]
    public void AClosedWorldKeepsItsNutrient()
    {
        var (t0, t1, book) = Run(open: false, seed: 11);
        _out.WriteLine($"before {t0}");
        _out.WriteLine($"after  {t1}");
        _out.WriteLine($"in {book.In:E3} out {book.Out:E3} debt {book.Debt:E3} (animals {book.DebtAnimals:E3}) founders {book.Founders:E3}");
        Assert.Equal(0.0, book.In);
        Assert.Equal(0.0, book.Out);
        // the total changed only by the founders brought in and what had to be made for want of a store (counted)
        Assert.True(book.Debt <= DebtMax(t0), $"debt {book.Debt:E3}");
        Assert.True(Math.Abs(t1.All - t0.All - book.Founders - book.Debt) <= Tol(t0), $"total {t0.All:F5} -> {t1.All:F5} ({t1.All - t0.All:E3})");
        Assert.True(t1.Animals > 0.0, "animals hold nutrient");
    }

    [Fact]
    public void AnOpenWorldChangesByItsInputsLessItsOutputs()
    {
        var (t0, t1, book) = Run(open: true, seed: 11);
        double d = t1.All - t0.All, net = book.In - book.Out + book.Founders + book.Debt;
        _out.WriteLine($"before {t0}");
        _out.WriteLine($"after  {t1}");
        _out.WriteLine($"change {d:E4}; in {book.In:E4} out {book.Out:E4} founders {book.Founders:E4} debt {book.Debt:E4} (animals {book.DebtAnimals:E4})");
        Assert.True(book.In > 0.0, "weathering adds nutrient");
        Assert.True(book.Debt <= DebtMax(t0), $"debt {book.Debt:E3}");
        Assert.True(book.Out > 0.0, "the soil and water lose what they cannot hold, and fires");
        Assert.True(Math.Abs(d - net) <= Tol(t0), $"change {d:E4} against inputs less outputs {net:E4}");
    }

    [Fact]
    public void RotGivesBackWhatGrowthTook()
    {
        // a unit of leafy growth takes Leaf from the soil; as litter it gives Leaf back (it gave C_NUTGAIN 0.7 against 0.6)
        using var sim = new Simulation(4, 3, 1, empty: true);
        var w = sim.W;
        int c = -1;
        for (int i = 0; i < Map.FW * Map.FW && c < 0; i++) if (w.Cell[i] == (byte)Realm.Land) c = i;
        w.NutBook.Open = false;
        for (int p = 0; p < Pools.NFIELD; p++) w.Pool[p][c] = 0f;
        w.WoodN[c] = w.DetN[c] = w.CarN[c] = w.DungN[c] = 0f; w.Peat[c] = w.PeatN[c] = 0f; w.WetDur[c] = 0f;
        w.Pool[(int)Pool.Litter][c] = 1f;
        w.Nutrient[c] = 0.2f;
        float before = w.Nutrient[c] + Nutrients.Leaf;
        Fields.Row(w, c / Map.FW);
        float after = w.NutrientN[c] + Nutrients.Leaf * w.Pool[(int)Pool.Litter][c];
        // the cell's own nutrient also moves by run-off to water beside it; this cell is checked only if it has none
        bool shore = false;
        int x = c % Map.FW, y = c / Map.FW;
        foreach (var (dx, dy) in new[] { (1, 0), (-1, 0), (0, 1), (0, -1) })
            if (w.Cell[World.WrapI(y + dy, Map.FW) * Map.FW + World.WrapI(x + dx, Map.FW)] != (byte)Realm.Land) shore = true;
        if (!shore) Assert.Equal(before, after, 5);
        Assert.Equal(0.6f, Nutrients.Leaf);
        Assert.InRange(Nutrients.Water, 0.29f, 0.31f);   // Redfield algae against 2% N leaves
    }
}
