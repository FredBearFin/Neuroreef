// How a grown body rests on the ground, for drawing (Body Lab and the console; display only). The live sim's standing
// height (Morph.Live.StandPosture) weights the upright height and the width by the chance the body tips on rough ground
// (Physics.Topple: a centre of mass at height z over a base of half-width b tips past slopes steeper than atan(b / z));
// this is that chance, with the same z, b and ground, so a renderer can lay a body that would topple on its side.

using System;
using Neuroreef.Morph;

namespace Neuroreef.Server;

public static class BodyPose
{
    /// <summary>The chance (0..1) the body tips over on rough ground, as Morph.Live.StandPosture weights it; above 0.5 it
    /// is drawn lying on its side.</summary>
    public static float Topple(Morphology m)
    {
        var wp = Physics.Walk(m, Phys.RHO_A);
        float ms = 0f, zs = 0f, bs = 0f;
        foreach (var s in m.Segs) { float mm = s.TissueMass + s.FatMass + s.IntMass; ms += mm; zs += mm * 0.5f * s.H; bs += mm * 0.5f * s.W; }
        if (ms <= 0f) return 0f;
        float z = wp.Clear + zs / ms, b = bs / ms + wp.Span;
        return Physics.Topple(z, b, Live.GROUND_ROUGH);
    }
}
