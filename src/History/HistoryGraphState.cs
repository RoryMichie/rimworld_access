using System;
using System.Collections.Generic;
using RimWorld;
using RimWorldAccess.Shell;
using UnityEngine;
using Verse;

namespace RimWorldAccess
{
    /// <summary>
    /// Data and mutations for the History window's Graph sub-tab
    /// (MainTabWindow_History.DoGraphPage:299-363). Vanilla draws one curve per
    /// <see cref="HistoryAutoRecorder"/> in the selected <see cref="HistoryAutoRecorderGroup"/>, a
    /// "Select graph" button, and four date-range buttons narrowing the curve's X window. This class
    /// gives the same three controls without the curve: it summarizes each recorder against the
    /// selected range (current value, min/max, trend) and buckets raw records into readable
    /// intervals (<see cref="BuildBuckets"/>).
    ///
    /// Navigation, typeahead and announcements live on <see cref="HistoryScope"/>; this is a thin
    /// static facade for the Harmony-patched, scope-unaware call sites that still need static entry
    /// points, plus the summarization the scope's rows are composed from.
    ///
    /// The recorder group and date-range window are read and written through
    /// <see cref="HistoryHelper"/>, which keeps the History window's own instance fields as the
    /// single source of truth, so this selection and the drawn graph never disagree.
    /// </summary>
    public static class HistoryGraphState
    {
        /// <summary>Mirrors vanilla's four date-range buttons (no "custom" state).</summary>
        public enum DateRangePreset
        {
            Last30Days,
            Last100Days,
            Last300Days,
            AllDays
        }

        // A bucket delta smaller than this reads as "steady" rather than floating-point noise.
        private const float TrendEpsilon = 0.0001f;

        // Summarized intervals shown in the detail view, however many days the range spans; a raw
        // per-record dump is deliberately avoided (see BuildBuckets).
        private const int TargetBucketCount = 24;

        private static readonly List<HistoryAutoRecorder> NoRecorders = new List<HistoryAutoRecorder>();

        private static bool isActive;
        private static DateRangePreset currentRangePreset = DateRangePreset.AllDays;

        /// <summary>
        /// The window-attached Graph scope while one is pushed, else null, set from its own
        /// OnPush/OnPop. Unlike the sibling tabs' mirrored singletons, this tab's scope IS the
        /// window's scope and so exists per window.
        /// </summary>
        internal static HistoryScope Scope;

        public static bool IsActive => isActive;

        /// <summary>Read by <see cref="HistoryState.HasActiveTypeahead"/>, which <c>HistoryPatch</c>'s ad hoc Cancel blocker consults transitively.</summary>
        public static bool HasActiveSearch => isActive && Scope != null && Scope.HasActiveSearch;

        /// <summary>
        /// Whether the graph's detail region has focus. Read directly by <c>HistoryPatch</c>'s Cancel
        /// blocker, which needs this exact signature to decide whether Escape exits the detail view
        /// before vanilla closes the window.
        /// </summary>
        public static bool IsInDetailView => isActive && Scope != null && Scope.IsDetailFocused;

        /// <summary>
        /// The group vanilla is drawing, read live off the window's own field rather than cached:
        /// the range buttons and the "Select graph" picker are real, clickable vanilla buttons, and
        /// a cached copy would silently disagree with the drawn graph the moment one is used.
        /// </summary>
        private static HistoryAutoRecorderGroup CurrentGroup => HistoryHelper.GetCurrentGraphGroup();

        internal static string GroupLabel => CurrentGroup?.def.LabelCap ?? "";

        internal static IReadOnlyList<HistoryAutoRecorder> Recorders => CurrentGroup?.recorders ?? NoRecorders;

        /// <summary>
        /// Opens the Graph tab navigation. PreOpen already seeded the window's group and the
        /// full-history range, so the first announcement matches what a sighted player sees; the
        /// fallback below covers only a window that opened with no group at all.
        /// </summary>
        public static void Open()
        {
            if (CurrentGroup == null)
            {
                var groups = HistoryHelper.GetGraphGroups();
                if (groups.Count > 0)
                {
                    HistoryHelper.SetCurrentGraphGroup(groups[0]);
                }
            }

            currentRangePreset = DateRangePreset.AllDays;
            isActive = true;
            Scope?.ResetForOpen();
            if (Recorders.Count == 0)
            {
                // The one line the scope cannot speak for itself: with no recorders its rows are
                // empty, and the chassis would announce the captured range buttons instead.
                TolkHelper.Speak("RimWorldAccess.History.Graph.None".Loc());
            }
        }

        public static void Close()
        {
            isActive = false;
        }

        /// <summary>
        /// Opens the same recorder-group picker as vanilla's "Select graph" button
        /// (MainTabWindow_History.cs:343-361): the same devModeOnly-filtered options and the same
        /// bare-field action, as a real <see cref="FloatMenu"/> so the shell's float-menu support
        /// handles it. <paramref name="afterPick"/> runs once the group is switched, so the caller
        /// owns the announcement.
        /// </summary>
        internal static void OpenGroupPicker(Action afterPick)
        {
            var options = new List<FloatMenuOption>();
            foreach (HistoryAutoRecorderGroup group in HistoryHelper.GetGraphGroups())
            {
                HistoryAutoRecorderGroup captured = group;
                options.Add(new FloatMenuOption(captured.def.LabelCap, delegate
                {
                    HistoryHelper.SetCurrentGraphGroup(captured);
                    afterPick?.Invoke();
                }));
            }

            Find.WindowStack.Add(new FloatMenu(options, "SelectGraph".Translate()));
        }

        /// <summary>
        /// Advances through vanilla's four date-range presets (MainTabWindow_History.cs:323-341),
        /// writing the exact <see cref="FloatRange"/> each button's lambda computes so the drawn
        /// curve matches the summary. Returns the new range's own vanilla label.
        /// </summary>
        internal static Localized CycleDateRange()
        {
            currentRangePreset = NextPreset(currentRangePreset);
            float ticksInDays = (float)Find.TickManager.TicksGame / 60000f;
            FloatRange section;
            switch (currentRangePreset)
            {
                case DateRangePreset.Last30Days:
                    section = new FloatRange(Mathf.Max(0f, ticksInDays - 30f), ticksInDays);
                    break;
                case DateRangePreset.Last100Days:
                    section = new FloatRange(Mathf.Max(0f, ticksInDays - 100f), ticksInDays);
                    break;
                case DateRangePreset.Last300Days:
                    section = new FloatRange(Mathf.Max(0f, ticksInDays - 300f), ticksInDays);
                    break;
                default:
                    section = new FloatRange(0f, ticksInDays);
                    break;
            }
            HistoryHelper.SetGraphSection(section);
            return RangeVanillaKey(currentRangePreset).Loc();
        }

        private static DateRangePreset NextPreset(DateRangePreset preset)
        {
            switch (preset)
            {
                case DateRangePreset.Last30Days: return DateRangePreset.Last100Days;
                case DateRangePreset.Last100Days: return DateRangePreset.Last300Days;
                case DateRangePreset.Last300Days: return DateRangePreset.AllDays;
                default: return DateRangePreset.Last30Days;
            }
        }

        private static string RangeVanillaKey(DateRangePreset preset)
        {
            switch (preset)
            {
                case DateRangePreset.Last30Days: return "Last30Days";
                case DateRangePreset.Last100Days: return "Last100Days";
                case DateRangePreset.Last300Days: return "Last300Days";
                default: return "AllDays";
            }
        }

        #region Data summarization

        /// <summary>One recorder's spoken value and range fragments, for the scope's row description.</summary>
        internal struct RecorderSummary
        {
            public string Value;
            public string Range;
        }

        private struct WindowStats
        {
            public float Latest;
            public float Min;
            public float Max;
            public int TrendSign;
            public bool HasData;
        }

        /// <summary>
        /// The summary a sighted player reads off the curve within the selected range: the latest
        /// reading as the value, then min/max and the first-to-last trend as the detail tail.
        /// </summary>
        internal static RecorderSummary Summarize(HistoryAutoRecorder recorder)
        {
            var summary = new RecorderSummary();
            if (recorder == null)
            {
                return summary;
            }
            WindowStats stats = ComputeWindowStats(recorder, HistoryHelper.GetGraphSection());
            summary.Value = "RimWorldAccess.History.Graph.CurrentValue".Translate(
                FormatValue(recorder, stats.HasData ? stats.Latest : 0f)).ToString();
            if (!stats.HasData)
            {
                summary.Range = "RimWorldAccess.History.Graph.NoDataInRange".Translate().ToString();
                return summary;
            }
            string minMax = "RimWorldAccess.History.Graph.MinMax".Translate(
                FormatValue(recorder, stats.Min), FormatValue(recorder, stats.Max)).ToString();
            summary.Range = minMax + ". " + TrendPhrase(stats.TrendSign);
            return summary;
        }

        /// <summary>Computes the latest reading, the range's min/max, and its first-to-last trend.</summary>
        private static WindowStats ComputeWindowStats(HistoryAutoRecorder recorder, FloatRange section)
        {
            var stats = new WindowStats();
            var records = recorder.records;
            if (records == null || records.Count == 0)
                return stats;

            int freq = recorder.def.recordTicksFrequency;
            int firstIdx = -1, lastIdx = -1;
            float min = float.MaxValue, max = float.MinValue;
            for (int i = 0; i < records.Count; i++)
            {
                float day = (float)(i * freq) / 60000f;
                if (day < section.min || day > section.max)
                    continue;
                if (firstIdx < 0) firstIdx = i;
                lastIdx = i;
                if (records[i] < min) min = records[i];
                if (records[i] > max) max = records[i];
            }
            if (firstIdx < 0)
            {
                // The selected window overlaps no recorded day: fall back to the latest reading
                // rather than reporting nothing.
                lastIdx = records.Count - 1;
                firstIdx = lastIdx;
                min = max = records[lastIdx];
            }

            stats.Latest = records[lastIdx];
            stats.Min = min;
            stats.Max = max;
            float delta = records[lastIdx] - records[firstIdx];
            stats.TrendSign = delta > TrendEpsilon ? 1 : (delta < -TrendEpsilon ? -1 : 0);
            stats.HasData = true;
            return stats;
        }

        /// <summary>
        /// Formats a value exactly as SimpleCurveDrawer's own hover tooltip does
        /// (Verse/SimpleCurveDrawer.cs:380): the recorder's valueFormat wraps a 2-decimal rounding
        /// when set, otherwise the rounding alone.
        /// </summary>
        private static string FormatValue(HistoryAutoRecorder recorder, float value)
        {
            string format = recorder?.def?.valueFormat;
            return string.IsNullOrEmpty(format)
                ? value.ToString("0.##")
                : string.Format(format, value.ToString("0.##"));
        }

        private static string TrendPhrase(int trendSign)
        {
            if (trendSign > 0) return "RimWorldAccess.History.Graph.TrendUp".Translate();
            if (trendSign < 0) return "RimWorldAccess.History.Graph.TrendDown".Translate();
            return "RimWorldAccess.History.Graph.TrendFlat".Translate();
        }

        /// <summary>The detail region's header row: which group and recorder its data points belong to.</summary>
        internal static string BuildDetailHeader(HistoryAutoRecorder recorder)
        {
            if (recorder == null)
            {
                return "";
            }
            return "RimWorldAccess.History.Graph.DetailHeader"
                .Translate(GroupLabel, recorder.def.LabelCap)
                .ToString();
        }

        /// <summary>One spoken line per summarized interval of the recorder's data, within the selected date range.</summary>
        internal static List<string> BuildDetailLines(HistoryAutoRecorder recorder)
        {
            var lines = new List<string>();
            if (recorder == null)
            {
                return lines;
            }
            foreach (GraphDataPoint point in BuildBuckets(recorder, HistoryHelper.GetGraphSection()))
            {
                lines.Add(point.ToAnnouncement(recorder));
            }
            return lines;
        }

        /// <summary>One summarized interval in a recorder's detail view.</summary>
        private class GraphDataPoint
        {
            public int StartDay;
            public int EndDay;
            public float Value;
            public float Min;
            public float Max;
            public int TrendVsPrevious;
            public bool HasPreviousComparison;

            public string ToAnnouncement(HistoryAutoRecorder recorder)
            {
                string dayLabel = StartDay == EndDay
                    ? "RimWorldAccess.History.Graph.DaySingle".Translate("Day".Translate(), StartDay).ToString()
                    : "RimWorldAccess.History.Graph.DayRange".Translate("Day".Translate(), StartDay, EndDay).ToString();

                var parts = new List<string> { dayLabel + ": " + FormatValue(recorder, Value) };
                if (!Mathf.Approximately(Min, Max))
                {
                    parts.Add("RimWorldAccess.History.Graph.MinMax".Translate(
                        FormatValue(recorder, Min), FormatValue(recorder, Max)).ToString());
                }
                if (HasPreviousComparison)
                    parts.Add(TrendPhrase(TrendVsPrevious));

                return string.Join(". ", parts);
            }
        }

        /// <summary>
        /// Buckets a recorder's raw records into at most <see cref="TargetBucketCount"/> intervals
        /// covering the selected range, so a long "All days" range reads as chunks rather than a
        /// dump of every half-day reading. Each bucket reports its LAST reading — matching how the
        /// curve reads at the bucket's right edge — its min/max, and its trend against the previous
        /// bucket.
        /// </summary>
        private static List<GraphDataPoint> BuildBuckets(HistoryAutoRecorder recorder, FloatRange section)
        {
            var result = new List<GraphDataPoint>();
            var records = recorder?.records;
            if (records == null || records.Count == 0)
                return result;

            int freq = recorder.def.recordTicksFrequency;
            int startDay = Mathf.Max(0, Mathf.FloorToInt(section.min));
            int endDay = Mathf.Max(startDay, Mathf.CeilToInt(section.max));
            int windowDays = Mathf.Max(1, endDay - startDay + 1);
            int bucketDays = Mathf.Max(1, Mathf.CeilToInt((float)windowDays / TargetBucketCount));

            float? previousValue = null;
            for (int bucketStart = startDay; bucketStart <= endDay; bucketStart += bucketDays)
            {
                int bucketEndExclusive = bucketStart + bucketDays;
                float min = float.MaxValue, max = float.MinValue, lastValue = 0f;
                bool any = false;
                for (int i = 0; i < records.Count; i++)
                {
                    float day = (float)(i * freq) / 60000f;
                    if (day < bucketStart || day >= bucketEndExclusive)
                        continue;
                    any = true;
                    lastValue = records[i];
                    if (records[i] < min) min = records[i];
                    if (records[i] > max) max = records[i];
                }
                if (!any)
                    continue;

                var point = new GraphDataPoint
                {
                    // 1-based day numbers, matching the in-game calendar's own "Day 1".
                    StartDay = bucketStart + 1,
                    EndDay = Mathf.Min(endDay, bucketEndExclusive - 1) + 1,
                    Value = lastValue,
                    Min = min,
                    Max = max,
                };
                if (previousValue.HasValue)
                {
                    float delta = lastValue - previousValue.Value;
                    point.TrendVsPrevious = delta > TrendEpsilon ? 1 : (delta < -TrendEpsilon ? -1 : 0);
                    point.HasPreviousComparison = true;
                }
                previousValue = lastValue;
                result.Add(point);
            }
            return result;
        }

        #endregion
    }
}
