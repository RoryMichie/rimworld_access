using System.Collections.Generic;
using System.Text;
using Verse;
using Verse.AI;

namespace RimWorldAccess
{
    /// <summary>
    /// Off-by-default ring buffer of autopilot decisions for dev-bridge diagnosis, separate
    /// from the flight recorder (which stays a UI trace). Enable and read over the bridge:
    /// <c>RimWorldAccess.CombatAutopilotDebug.Enabled = true</c>, act, then <c>Dump()</c>.
    /// Each entry is one think-tree poll result: tick, pawn, job def, report and target.
    /// </summary>
    public static class CombatAutopilotDebug
    {
        private const int Capacity = 400;

        public static bool Enabled;

        private static readonly Queue<string> entries = new Queue<string>(Capacity);

        public static void Record(Pawn pawn, Job job)
        {
            if (!Enabled)
            {
                return;
            }
            var sb = new StringBuilder();
            sb.Append(Find.TickManager?.TicksGame ?? 0).Append(' ');
            sb.Append(pawn?.LabelShort ?? "null").Append(": ");
            if (job == null)
            {
                sb.Append("null (vanilla drafted wait)");
            }
            else
            {
                sb.Append(job.def?.defName ?? "?");
                if (!string.IsNullOrEmpty(job.reportStringOverride))
                {
                    sb.Append(" | ").Append(job.reportStringOverride);
                }
                if (job.targetA.IsValid)
                {
                    sb.Append(" | A=").Append(job.targetA.ToString());
                }
            }
            if (entries.Count >= Capacity)
            {
                entries.Dequeue();
            }
            entries.Enqueue(sb.ToString());
        }

        public static string Dump()
        {
            return entries.Count == 0 ? "(empty)" : string.Join("\n", entries);
        }

        public static void Clear()
        {
            entries.Clear();
        }
    }
}
