// Path integration (Revision P). An animal no longer knows the way to water from anywhere on the map: it senses water
// only within SENSE_WATER cells. What it can do, as real animals do (desert ants, rodents, birds; Mueller & Wehner 1988;
// Etienne & Jeffery 2004), is keep a running vector back to the last place it drank and the last place it fed well,
// adding up its own movement. Its sense of its own movement is not exact: each step's estimate is off by a random
// vector of SD PI_ERR x the distance moved, so the remembered place drifts the further it goes. Nothing is told where
// water is: the memory is only of where this animal itself has been.

using System;

namespace Neuroreef;

public static class PathMemory
{
    /// <summary>Forget everything (a newborn knows no place yet).</summary>
    public static void Clear(World w, int i)
    {
        w.MemHas[i] = 0; w.MemDrinkX[i] = w.MemDrinkY[i] = w.MemFedX[i] = w.MemFedY[i] = 0f;
    }

    /// <summary>
    /// Update the remembered vectors after the animal moved (dx, dy) world units this step. Its estimate of that move is
    /// off by N(0, (PI_ERR x |move|)^2) on each axis, the same estimate for both memories (it is one sense of self-motion).
    /// </summary>
    public static void Integrate(World w, int i, float dx, float dy)
    {
        if (w.MemHas[i] == 0) return;
        float d = MathF.Sqrt(dx * dx + dy * dy);
        if (d <= 0f) return;
        uint seed = w.Seed, step = (uint)w.Step;
        float sd = K.PI_ERR * d;
        float ex = dx + sd * Rng.N(seed, step, (uint)i, Rs.PathX), ey = dy + sd * Rng.N(seed, step, (uint)i, Rs.PathY);
        // on the torus the remembered place is kept as the nearest copy of itself
        if ((w.MemHas[i] & Mem.Drink) != 0) { w.MemDrinkX[i] = World.Delta1(w.MemDrinkX[i] - ex); w.MemDrinkY[i] = World.Delta1(w.MemDrinkY[i] - ey); }
        if ((w.MemHas[i] & Mem.Fed) != 0) { w.MemFedX[i] = World.Delta1(w.MemFedX[i] - ex); w.MemFedY[i] = World.Delta1(w.MemFedY[i] - ey); }
    }

    /// <summary>It drank here: the drink vector starts again from zero.</summary>
    public static void Drank(World w, int i) { w.MemHas[i] |= Mem.Drink; w.MemDrinkX[i] = 0f; w.MemDrinkY[i] = 0f; }

    /// <summary>It fed well here: the feeding vector starts again from zero.</summary>
    public static void Fed(World w, int i) { w.MemHas[i] |= Mem.Fed; w.MemFedX[i] = 0f; w.MemFedY[i] = 0f; }

    /// <summary>
    /// The three senses of one remembered place, written from feature index k: how far ahead it lies (cosine of its
    /// bearing against the heading, -1..1), how far to the left (sine), and how near (1 / (1 + d / MEM_NEAR), 0 when
    /// nothing is remembered). At the place itself the direction reads 0.
    /// </summary>
    public static void Senses(bool known, float vx, float vy, float fx, float fy, Span<float> f, int k)
    {
        f[k] = f[k + 1] = f[k + 2] = 0f;
        if (!known) return;
        float d = MathF.Sqrt(vx * vx + vy * vy);
        f[k + 2] = 1f / (1f + d / K.MEM_NEAR);
        if (d < 1e-3f) return;
        f[k] = (vx * fx + vy * fy) / d;
        f[k + 1] = (fx * vy - fy * vx) / d;
    }

    /// <summary>
    /// The way to water this animal can know: down the distance to drinkable water when water lies within SENSE_WATER
    /// cells (it senses it); beyond that, toward where it last drank, if it remembers; else none (0, 0).
    /// </summary>
    public static void WaterWay(World w, int i, int cell, out float vx, out float vy)
    {
        var dist = w.DistDrink;
        if (dist[cell] <= K.SENSE_WATER)
        {
            vx = dist[Neighbour(cell, -1, 0)] - dist[Neighbour(cell, 1, 0)];
            vy = dist[Neighbour(cell, 0, -1)] - dist[Neighbour(cell, 0, 1)];
            return;
        }
        if ((w.MemHas[i] & Mem.Drink) != 0) { vx = w.MemDrinkX[i]; vy = w.MemDrinkY[i]; return; }
        vx = vy = 0f;
    }

    /// <summary>The way to land when land lies within SENSE_WATER cells, else none.</summary>
    public static void LandWay(World w, int cell, out float vx, out float vy)
    {
        var dist = w.DistLand;
        if (dist[cell] > K.SENSE_WATER) { vx = vy = 0f; return; }
        vx = dist[Neighbour(cell, -1, 0)] - dist[Neighbour(cell, 1, 0)];
        vy = dist[Neighbour(cell, 0, -1)] - dist[Neighbour(cell, 0, 1)];
    }

    static int Neighbour(int c, int dx, int dy)
        => World.WrapI(c / Map.FW + dy, Map.FW) * Map.FW + World.WrapI(c % Map.FW + dx, Map.FW);
}
