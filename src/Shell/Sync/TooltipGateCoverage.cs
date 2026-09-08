using System.Collections.Generic;

namespace RimWorldAccess.Shell
{
    /// <summary>
    /// One installer's tally of what <see cref="TooltipGateAnalysis"/> decided:
    /// certified sites, patched methods, and every refusal by verdict. The
    /// vanilla registry sweep and the mod scan each own one, so the DEBUG
    /// coverage report can state their coverage side by side.
    /// </summary>
    internal sealed class TooltipGateCoverage
    {
        private readonly Dictionary<GateVerdict, int> refusals = new Dictionary<GateVerdict, int>();
#if DEBUG
        private readonly List<string> siteLines = new List<string>();
#endif

        public int MethodsPatched { get; private set; }
        public int SitesCertified { get; private set; }

        /// <summary>
        /// Files one method's verdicts and returns how many of its sites were
        /// certified; the caller patches only when that comes back non-zero.
        /// </summary>
        public int Record(IReadOnlyList<GateSiteResult> sites, string method)
        {
            int eligible = 0;
            for (int i = 0; i < sites.Count; i++)
            {
                if (sites[i].Verdict == GateVerdict.Eligible)
                {
                    eligible++;
                }
                else
                {
                    refusals.TryGetValue(sites[i].Verdict, out int count);
                    refusals[sites[i].Verdict] = count + 1;
                }
#if DEBUG
                siteLines.Add(method + " #" + sites[i].SiteIndex + " " + sites[i].Verdict
                    + (sites[i].Reason != null ? " (" + sites[i].Reason + ")" : string.Empty));
#endif
            }
            return eligible;
        }

        public void NotePatched(int certified)
        {
            MethodsPatched++;
            SitesCertified += certified;
        }

        public int RefusedTotal
        {
            get
            {
                int total = 0;
                foreach (int count in refusals.Values)
                {
                    total += count;
                }
                return total;
            }
        }

        public IEnumerable<KeyValuePair<GateVerdict, int>> Refusals
        {
            get { return refusals; }
        }

#if DEBUG
        public IReadOnlyList<string> SiteLines
        {
            get { return siteLines; }
        }
#endif
    }
}
