using System.Collections.Generic;
using RimWorld;
using Verse;

namespace RimWorldAccess.Shell
{
    /// <summary>
    /// Game-layer companion to the pure <see cref="HourRangeSegments"/> model:
    /// reads a pawn's 24-hour timetable, run-length encodes it, and formats the
    /// runs with the shared <c>Shell.Generic.HourRange</c>/<c>HourSingle</c>
    /// translation keys ("0 to 5: Sleep, 6 to 21: Anything, 22 to 23: Sleep").
    /// Both the generic pawn-table reader (<c>TimetableColumnHandler</c>) and the
    /// hand-built Schedule screen (<c>ScheduleScope</c>) call this so the day
    /// overview reads identically wherever a timetable is spoken.
    /// </summary>
    internal static class TimetableReadout
    {
        public static string Describe(Pawn pawn)
        {
            if (pawn == null || pawn.timetable == null)
            {
                return "";
            }
            var hours = new List<string>(24);
            for (int hour = 0; hour < 24; hour++)
            {
                TimeAssignmentDef assignment = pawn.timetable.GetAssignment(hour);
                hours.Add(assignment != null ? assignment.LabelCap.ToString() : "");
            }
            List<HourRangeSegment> segments = HourRangeSegments.Compute(hours);
            var parts = new List<string>(segments.Count);
            for (int i = 0; i < segments.Count; i++)
            {
                HourRangeSegment segment = segments[i];
                parts.Add(segment.Start == segment.End
                    ? (string)"RimWorldAccess.Shell.Generic.HourSingle".Translate(segment.Start, segment.Label)
                    : (string)"RimWorldAccess.Shell.Generic.HourRange".Translate(segment.Start, segment.End, segment.Label));
            }
            return string.Join(", ", parts);
        }
    }
}
