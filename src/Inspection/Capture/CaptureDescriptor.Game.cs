using System.Collections.Generic;
using RimWorldAccess.Shell;

namespace RimWorldAccess
{
    /// <summary>
    /// The stable identity of a captured widget within a capture pass's stream:
    /// (kind, raw label, ordinal among widgets of the same kind+label in draw
    /// order). It is what re-finds a widget in a FRESH pass — the presentation
    /// side (which control rows are operable) and the armed matcher (which
    /// widget to fire) MUST count identically, so both go through this one
    /// descriptor. Ordinals are always computed against the FULL captured
    /// stream, matching <see cref="WidgetCapture"/>'s armed matcher.
    /// </summary>
    internal static class CaptureDescriptor
    {
        /// <summary>
        /// Kinds a single action row activates directly on Enter — the armed pass
        /// fires the vanilla handler in place. Sliders and text fields are
        /// interactive too (see <see cref="IsInteractiveKind"/>) but need a richer
        /// presentation (a stepper node, an edit session), not a plain Enter fire.
        /// </summary>
        internal static bool IsOperableKind(WidgetKind kind)
        {
            return kind == WidgetKind.Button
                || kind == WidgetKind.Checkbox
                || kind == WidgetKind.RadioButton
                || kind == WidgetKind.Tab;
        }

        /// <summary>
        /// Kinds a captured row can drive at all — the operable kinds plus sliders
        /// and text fields, which the presentation layer renders as a stepper node
        /// and an edit-session row respectively. Every one is collected as an
        /// <c>InteractiveMember</c> during folding.
        /// </summary>
        internal static bool IsInteractiveKind(WidgetKind kind)
        {
            return kind == WidgetKind.Button
                || kind == WidgetKind.Checkbox
                || kind == WidgetKind.RadioButton
                || kind == WidgetKind.Slider
                || kind == WidgetKind.TextField
                || kind == WidgetKind.Tab;
        }

        /// <summary>The ordinal of the widget at <paramref name="index"/>: how many earlier widgets share its kind and label.</summary>
        internal static int OrdinalOf(List<CapturedWidget> widgets, int index)
        {
            CapturedWidget target = widgets[index];
            string label = target.Label ?? "";
            int ordinal = 0;
            for (int j = 0; j < index; j++)
            {
                if (widgets[j].Kind == target.Kind && (widgets[j].Label ?? "") == label)
                {
                    ordinal++;
                }
            }
            return ordinal;
        }

        /// <summary>The stream index of the <paramref name="ordinal"/>-th widget matching <paramref name="kind"/>+<paramref name="label"/>, or -1.</summary>
        internal static int FindIndex(List<CapturedWidget> widgets, WidgetKind kind, string label, int ordinal)
        {
            string target = label ?? "";
            int seen = 0;
            for (int i = 0; i < widgets.Count; i++)
            {
                if (widgets[i].Kind == kind && (widgets[i].Label ?? "") == target)
                {
                    if (seen == ordinal)
                    {
                        return i;
                    }
                    seen++;
                }
            }
            return -1;
        }
    }
}
