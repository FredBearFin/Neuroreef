// Usage: FoundProbe <map> <bigBrains> <seed>...   (2026-11-02) Founding only, no stepping: builds each world (terrain,
// water, plant spin-up), founds its random ancestors (Simulation.FoundAncestors) and prints per seed the land and water
// clades founded, the big-brained ones, their founders, and the share of random ancestor bodies (all ANCESTOR_TRIES draws
// of every ancestor) whose gut can absorb FORAGE_MARGIN days' need a step (Life.RangeCapacity's absorption test, at the
// reference temperature: no heat or breathing costs). Worlds are not freed between seeds: give one seed per process on a
// shared machine (seven 256-map worlds ran out of memory in one).
using System;
using System.Linq;
using Neuroreef;

int map = int.Parse(args[0]); int big = int.Parse(args[1]);
Map.Set(map);
var evo = new EvoSettings { BigBrains = big };
int nL = evo.AncestorsLand, nW = evo.AncestorsWater, bigL = (big + 1) / 2, bigW = big / 2;
Console.WriteLine("seed  land   water  bigLand(founders)  bigWater(founders)  gutOK land/water/bigL/bigW  water founders");
int tl = 0, tw = 0, tbl = 0, tbw = 0;
for (int a = 2; a < args.Length; a++)
{
    uint seed = uint.Parse(args[a]);
    var sim = new Simulation(4096, seed, 2, evo: evo);
    var w = sim.W;
    int[] n = new int[16];
    for (int i = 0; i < w.Cap; i++) if (w.StageOf[i] != St.Dead && w.Spc[i] >= 0 && w.Spc[i] < 16) n[w.Spc[i]]++;
    int land = Enumerable.Range(0, nL).Count(c => n[c] > 0), water = Enumerable.Range(nL, nW).Count(c => n[c] > 0);
    int bl = Enumerable.Range(0, bigL).Count(c => n[c] > 0), bw = Enumerable.Range(nL, bigW).Count(c => n[c] > 0);
    tl += land; tw += water; tbl += bl; tbw += bw;
    // the gut test over every draw: absorb >= FORAGE_MARGIN x FIELD_METAB x basal (as RangeCapacity without climate)
    var g = new sbyte[G.LEN]; double[] ok = new double[4], all = new double[4];
    int slot = Life.Found(w, 15, ReadOnlySpan<sbyte>.Empty, true, 999u);
    for (int anc = 0; anc < nL + nW; anc++)
        for (int t = 0; t < K.ANCESTOR_TRIES; t++)
        {
            bool isLand = anc < nL, isBig = isLand ? anc < bigL : anc - nL < bigW;
            int draw = anc + t * 0x10000;
            Brain.RandomGenome(g, seed, 0, 0x700000u + (uint)draw);
            Clades.AncestorBody(new Span<sbyte>(g, G.BODY, G.NBODY), seed, draw, isLand, isBig ? evo.BigBrainUnits : 0, voiced: isBig && isLand, floorClass: anc % K.FLOOR_CLASSES);
            g.CopyTo(w.Genome.Slot(slot)); Body.Express(w, slot);
            int k = isBig ? (isLand ? 2 : 3) : (isLand ? 0 : 1);
            all[k]++; if (w.PAbsorb[slot] >= K.FORAGE_MARGIN * K.FIELD_METAB * w.PBasal[slot]) ok[k]++;
        }
    Life.FreeSlot(w, slot);
    string P(int k) => all[k] > 0 ? $"{ok[k] / all[k]:P0}" : "-";
    string F(int c0, int cnt) => string.Join(",", Enumerable.Range(c0, cnt).Select(c => n[c].ToString()));
    Console.WriteLine($"{seed,4}  {land,2}/{nL}  {water,2}/{nW}  {bl}/{bigL} ({F(0, bigL)})  {bw}/{bigW} ({F(nL, bigW)})  {P(0)}/{P(1)}/{P(2)}/{P(3)}  [{F(nL, nW)}]");
}
Console.WriteLine($"total land {tl} water {tw} bigLand {tbl} bigWater {tbw} over {args.Length - 2} seeds");
