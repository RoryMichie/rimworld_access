using System.Collections.Generic;

namespace RimWorldAccess.Shell
{
    /// <summary>
    /// One contiguous run of identical hour assignments inside a 24-hour
    /// schedule row: hours <see cref="Start"/> through <see cref="End"/>
    /// (inclusive, 0-23) all carry <see cref="Label"/>.
    /// </summary>
    public struct HourRangeSegment
    {
        public int Start;
        public int End;
        public string Label;

        public HourRangeSegment(int start, int end, string label)
        {
            Start = start;
            End = end;
            Label = label;
        }
    }

    /// <summary>
    /// Run-length encodes a per-hour label sequence (a pawn timetable row) into
    /// contiguous segments, so 24 hourly values speak as a handful of ranges
    /// ("22 to 5: Sleep") instead of 24 repetitions. Pure — covered directly by
    /// tests; the game layer supplies the labels and the translated formatting.
    /// </summary>
    public static class HourRangeSegments
    {
        /// <summary>
        /// Splits <paramref name="hourLabels"/> into contiguous same-label
        /// segments, in order. Null labels group like any other value (as
        /// empty). An empty input yields no segments.
        /// </summary>
        public static List<HourRangeSegment> Compute(IReadOnlyList<string> hourLabels)
        {
            var segments = new List<HourRangeSegment>();
            if (hourLabels == null || hourLabels.Count == 0)
            {
                return segments;
            }
            int start = 0;
            string current = hourLabels[0] ?? "";
            for (int i = 1; i < hourLabels.Count; i++)
            {
                string label = hourLabels[i] ?? "";
                if (label != current)
                {
                    segments.Add(new HourRangeSegment(start, i - 1, current));
                    start = i;
                    current = label;
                }
            }
            segments.Add(new HourRangeSegment(start, hourLabels.Count - 1, current));
            return segments;
        }
    }
}
