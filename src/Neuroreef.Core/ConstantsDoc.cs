// Generates docs/CONSTANTS.md from Constants.cs and the roster (NR-106). A test compares the output with
// the committed file, so the document cannot drift from the code.

using System;
using System.Globalization;
using System.Linq;
using System.Reflection;
using System.Text;

namespace Neuroreef;

public static class ConstantsDoc
{
    public static string Generate()
    {
        var ci = CultureInfo.InvariantCulture;
        var sb = new StringBuilder();
        sb.Append("# NEUROREEF 2 — CONSTANTS TABLE\n\n");
        sb.Append("Document NR-CT-001, Revision B. **Generated** from `src/Neuroreef.Core/Constants.cs` and `Species.cs` by\n");
        sb.Append("`Neuroreef.exe --print-constants`; do not edit by hand. A test fails if this file and the code disagree (NR-106).\n\n");
        sb.Append("Origin codes: **V1** taken from Neuroreef v1; **ECO** ecological rule or exponent from the literature;\n");
        sb.Append("**HT** hand-tuned in headless runs; **-** derived.\n\n");
        var fields = typeof(K).GetFields(BindingFlags.Public | BindingFlags.Static).Where(f => f.IsLiteral).ToArray();
        string? group = null;
        int gi = 0;
        foreach (var f in fields)
        {
            var a = f.GetCustomAttribute<KAttribute>();
            if (a == null) continue;
            if (a.Group != group)
            {
                group = a.Group; gi++;
                sb.Append($"\n## {gi}. {group}\n\n| Name | Value | Unit | Meaning | Origin |\n|---|---|---|---|---|\n");
            }
            object v = f.GetRawConstantValue()!;
            string vs = v is float fl ? fl.ToString("G6", ci) : Convert.ToString(v, ci)!;
            sb.Append($"| {f.Name} | {vs} | {a.Unit} | {a.Meaning} | {a.Origin} |\n");
        }

        gi++;
        sb.Append($"\n## {gi}. Species body plans\n\nDerived values follow the allometry rules of DESIGN §9.1. S = super-individual.\n\n");
        var R = Roster.All;
        sb.Append("| # | Species | Niche | Mass | Unit mass | Locomotion | Armour | Strike | Mode | Eggs | Litter | Gestation | Cooldown | Initial |\n");
        sb.Append("|---|---|---|---|---|---|---|---|---|---|---|---|---|---|\n");
        foreach (var s in R)
            sb.Append($"| {s.Id} | {s.Name}{(s.Swarm ? " (S)" : "")} | {s.Niche} | {s.Mass.ToString(ci)} | {s.UnitMass.ToString(ci)} | " +
                      $"{(s.TwoStage ? s.Juv.Loco + " → " : "")}{s.Adult.Loco} | {s.Armour.ToString(ci)} | {s.Strike.ToString(ci)} | {s.Mode} | {s.Medium} | {s.Litter} | {s.Gestation} | {s.Cooldown} | {s.InitCount} |\n");

        sb.Append("\n### Diet efficiencies (adult stage; juvenile stage in brackets where it differs)\n\n| Species |");
        foreach (var p in Pools.Names) sb.Append($" {p} |");
        sb.Append("\n|---|"); foreach (var _ in Pools.Names) sb.Append("---|"); sb.Append('\n');
        foreach (var s in R)
        {
            sb.Append($"| {s.Name} |");
            for (int p = 0; p < Pools.N; p++)
            {
                float a = s.Adult.Diet[p], j = s.Juv.Diet[p];
                string cell = a > 0 ? a.ToString("0.##", ci) : "";
                if (s.TwoStage && j > 0) cell += $" ({j.ToString("0.##", ci)})";
                sb.Append($" {cell} |");
            }
            sb.Append('\n');
        }

        sb.Append("\n### Derived values\n\n| Species | Basal E/step | Emax | Gut | Vmax | Lifespan | Maturity | Sight | FoV ° |\n|---|---|---|---|---|---|---|---|---|\n");
        foreach (var s in R)
            sb.Append($"| {s.Name} | {s.Basal.ToString("0.00000", ci)} | {s.Emax.ToString("0.##", ci)} | {s.GutCap.ToString("0.###", ci)} | " +
                      $"{s.Vmax(s.Adult.Loco).ToString("0.##", ci)} | {s.Life.ToString("0", ci)} | {s.Mature.ToString("0", ci)} | {s.Sight.ToString("0.#", ci)} | {s.Fov.ToString(ci)} |\n");
        return sb.ToString();
    }
}
