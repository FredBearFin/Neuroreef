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
        sb.Append($"\n## {gi}. Organisms\n\nThere are no designed species (Revision F). Life starts from {K.ANCESTORS_LAND} land and {K.ANCESTORS_WATER} water ancestors with random bodies " +
                  "(Clades.AncestorBody), and every organism's body is expressed from its own genes on the ranges above (DESIGN §6.3).\n");
        return sb.ToString();
    }
}
