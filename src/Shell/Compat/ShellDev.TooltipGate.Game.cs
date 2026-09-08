#if DEBUG
using System.Collections.Generic;
using System.Text;

namespace RimWorldAccess.Shell
{
    /// <summary>
    /// ShellDev's tooltip-gate surface: the per-site breakdown behind the two
    /// summary lines the installers log, so the coverage fraction can be
    /// re-measured over the bridge without a rebuild. Vanilla (the generated
    /// registry) and mods (the startup scan) are tallied separately.
    /// </summary>
    public static partial class ShellDev
    {
        public static string TooltipGateReport(bool perSite = false)
        {
            var sb = new StringBuilder();
            sb.Append("vanilla: ").Append(TooltipGateInstaller.SitesCertified).Append(" certified, ")
                .Append(TooltipGateInstaller.MethodsPatched).Append(" methods patched, ")
                .Append(TooltipGateInstaller.TypesResolved).Append(" types resolved\n");
            Breakdown(sb, "vanilla", TooltipGateInstaller.Coverage, perSite);

            sb.Append("mods: ").Append(TooltipGateModScan.Coverage.SitesCertified).Append(" certified, ")
                .Append(TooltipGateModScan.Coverage.MethodsPatched).Append(" methods patched, ")
                .Append(TooltipGateModScan.AssembliesScanned).Append(" assemblies scanned, ")
                .Append(TooltipGateModScan.MethodsAnalyzed).Append(" methods analyzed, ")
                .Append(TooltipGateModScan.AssembliesFailed).Append(" unreadable")
                .Append(TooltipGateModScan.BudgetExhausted ? ", BUDGET EXHAUSTED" : string.Empty).Append('\n');
            Breakdown(sb, "mods", TooltipGateModScan.Coverage, perSite);
            return sb.ToString();
        }

        private static void Breakdown(StringBuilder sb, string scope, TooltipGateCoverage coverage, bool perSite)
        {
            foreach (KeyValuePair<GateVerdict, int> refusal in coverage.Refusals)
            {
                sb.Append(scope).Append(" refused ").Append(refusal.Key).Append(": ").Append(refusal.Value).Append('\n');
            }
            if (!perSite)
            {
                return;
            }
            IReadOnlyList<string> lines = coverage.SiteLines;
            for (int i = 0; i < lines.Count; i++)
            {
                sb.Append(lines[i]).Append('\n');
            }
        }
    }
}
#endif
