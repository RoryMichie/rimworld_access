#if DEBUG
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using HarmonyLib;
using RimWorldAccess.Shell;
using UnityEngine;
using Verse;

namespace RimWorldAccess
{
    /// <summary>
    /// DEBUG-only bridge driver for <see cref="InspectTabCaptureHarness"/>
    /// (rework plan Part C spike; compiled out of Release like the dev
    /// bridge). The bridge drains eval work inside UIRootOnGUI on whatever
    /// event happens to be current, and the harness requires a Repaint event,
    /// so captures are two-step: an eval calls <see cref="Request"/>, the
    /// pump patch below runs one pass per subsequent Repaint frame (cross-
    /// frame on purpose — the stability criterion is across repaints, not
    /// within one), and a later eval reads <see cref="Poll"/> for the
    /// formatted dumps plus a stability verdict.
    /// </summary>
    public static class InspectTabCaptureProbe
    {
        private static Thing target;
        private static Type tabType;
        private static int passesWanted;
        private static readonly List<string> dumps = new List<string>();

        /// <summary>Result of the last completed request, or null while one is pending.</summary>
        public static string LastResult;

        /// <summary>Queue a capture of <paramref name="tab"/> for <paramref name="thing"/> over the next <paramref name="passes"/> Repaint frames.</summary>
        public static string Request(Thing thing, Type tab, int passes = 3)
        {
            target = thing;
            tabType = tab;
            passesWanted = Mathf.Max(1, passes);
            dumps.Clear();
            LastResult = null;
            return $"queued {tab?.Name} x{passesWanted} for {thing?.LabelCap}";
        }

        /// <summary>Bridge-friendly read of the pending/complete state.</summary>
        public static string Poll()
        {
            if (LastResult != null)
            {
                return LastResult;
            }
            return target != null ? $"(pending, {dumps.Count}/{passesWanted} passes)" : "(no request)";
        }

        internal static void PumpOnRepaint()
        {
            if (target == null || Event.current == null || Event.current.type != EventType.Repaint)
            {
                return;
            }
            try
            {
                InspectTabBase tab = InspectTabManager.GetSharedInstance(tabType);
                var rows = new List<CapturedWidget>();
                if (!InspectTabCaptureHarness.TryCapturePass(tab, target, rows, out string error))
                {
                    LastResult = "ERROR: " + error;
                    target = null;
                    return;
                }
                dumps.Add(FormatDump(rows));
                if (dumps.Count >= passesWanted)
                {
                    LastResult = BuildVerdict();
                    target = null;
                }
            }
            catch (Exception ex)
            {
                LastResult = "ERROR: pump threw: " + ex;
                target = null;
            }
        }

        private static string FormatDump(List<CapturedWidget> rows)
        {
            var sb = new StringBuilder();
            sb.Append(rows.Count).Append(" rows");
            for (int i = 0; i < rows.Count; i++)
            {
                CapturedWidget row = rows[i];
                sb.Append('\n').Append(i).Append('|').Append(row.Kind).Append('|').Append(row.Label);
                if (row.Kind == WidgetKind.TextField)
                {
                    sb.Append("|text=").Append(row.Text);
                }
                if (row.Kind == WidgetKind.Checkbox)
                {
                    sb.Append("|checked=").Append(row.Checked);
                }
                if (row.Kind == WidgetKind.RadioButton || row.Kind == WidgetKind.Tab)
                {
                    sb.Append("|selected=").Append(row.Selected);
                }
                sb.Append('|').Append(FormatRect(row.Rect));
            }
            return sb.ToString();
        }

        private static string FormatRect(Rect rect)
        {
            return string.Format(CultureInfo.InvariantCulture, "({0:0.#},{1:0.#},{2:0.#},{3:0.#})",
                rect.x, rect.y, rect.width, rect.height);
        }

        private static string BuildVerdict()
        {
            bool stable = true;
            for (int i = 1; i < dumps.Count; i++)
            {
                if (!string.Equals(dumps[i], dumps[0], StringComparison.Ordinal))
                {
                    stable = false;
                    break;
                }
            }
            if (stable)
            {
                return $"STABLE across {dumps.Count} repaint passes\n{dumps[0]}";
            }
            var sb = new StringBuilder("UNSTABLE across repaint passes");
            for (int i = 0; i < dumps.Count; i++)
            {
                sb.Append("\n--- pass ").Append(i).Append('\n').Append(dumps[i]);
            }
            return sb.ToString();
        }
    }

    /// <summary>
    /// Runs the probe's pending capture on Repaint frames. Postfix so every
    /// real window (and any focus-scope capture pass) has fully drawn and
    /// closed before the detached pass opens — the ordering the detached-pass
    /// contract in <see cref="WidgetCapture.BeginDetachedPass"/> relies on.
    /// </summary>
    [HarmonyPatch(typeof(UIRoot), "UIRootOnGUI")]
    internal static class InspectTabCaptureProbePumpPatch
    {
        [HarmonyPostfix]
        public static void Postfix()
        {
            InspectTabCaptureProbe.PumpOnRepaint();
            InspectTabCaptureOracle.PumpOnRepaint();
            ActionParityOracle.PumpOnRepaint();
        }
    }
}
#endif
