using System.Reflection;
using Verse;

namespace RimWorldAccess
{
    /// <summary>
    /// Announcement handler for Verse.Gizmo_Slider and every subclass, vanilla's and other mods'
    /// alike.
    ///
    /// Reflect once against the BASE type and dispatch virtually: Title, BarLabel, ValuePercent and
    /// GetTooltip are all protected members DECLARED on Gizmo_Slider, and a MemberInfo resolved on
    /// the declaring type still virtual-dispatches to the most-derived override, so one cached
    /// handle per member serves every subclass — including ones from mods never seen — with no
    /// per-runtime-type lookups. A subclass with a richer bespoke handler registers its own exact
    /// type, and the registry's chain resolution falls back here when that handler declines.
    ///
    /// Status reads BarLabel, the text vanilla actually draws on the bar, whose base implementation
    /// is the same percentage; a subclass that overrides it then reads exactly what is on screen.
    /// ValuePercent is the fallback when BarLabel is unreadable.
    ///
    /// No TryExecute override: activation falls through to the generic ProcessInput fallback.
    /// </summary>
    internal sealed class SliderGizmoHandler : GizmoHandlerBase
    {
        // These members are protected on the declaring type; Public is included in case a future
        // game version widens access.
        private const BindingFlags DeclaredMemberFlags =
            BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;

        private static readonly PropertyInfo TitleProperty =
            typeof(Verse.Gizmo_Slider).GetProperty("Title", DeclaredMemberFlags);

        /// <summary>The text drawn on the bar; its default implementation is ValuePercent as a percentage.</summary>
        private static readonly PropertyInfo BarLabelProperty =
            typeof(Verse.Gizmo_Slider).GetProperty("BarLabel", DeclaredMemberFlags);

        /// <summary>The bar's fill fraction.</summary>
        private static readonly PropertyInfo ValuePercentProperty =
            typeof(Verse.Gizmo_Slider).GetProperty("ValuePercent", DeclaredMemberFlags);

        /// <summary>The hover tooltip body.</summary>
        private static readonly MethodInfo GetTooltipMethod =
            typeof(Verse.Gizmo_Slider).GetMethod("GetTooltip", DeclaredMemberFlags);

        /// <summary>The drag-target store.</summary>
        private static readonly PropertyInfo TargetProperty =
            typeof(Verse.Gizmo_Slider).GetProperty("Target", DeclaredMemberFlags);

        /// <summary>The private drag cache GizmoOnGUI copies into Target every rendered frame.</summary>
        private static readonly FieldInfo TargetValuePctField =
            typeof(Verse.Gizmo_Slider).GetField("targetValuePct",
                BindingFlags.Instance | BindingFlags.NonPublic);

        private static readonly PropertyInfo DragRangeProperty =
            typeof(Verse.Gizmo_Slider).GetProperty("DragRange", DeclaredMemberFlags);

        private static readonly PropertyInfo IncrementsProperty =
            typeof(Verse.Gizmo_Slider).GetProperty("Increments", DeclaredMemberFlags);

        /// <summary>Vanilla's own adjustability gate.</summary>
        private static readonly PropertyInfo IsDraggableProperty =
            typeof(Verse.Gizmo_Slider).GetProperty("IsDraggable", DeclaredMemberFlags);

        public override bool TryGetLabel(Gizmo gizmo, out string label)
        {
            label = null;

            if (!(gizmo is Verse.Gizmo_Slider) || TitleProperty == null)
                return false;

            try
            {
                string title = TitleProperty.GetValue(gizmo) as string;
                if (string.IsNullOrEmpty(title))
                    return false;

                // StripTags is a no-op for plain vanilla titles and protects against modded ones.
                label = title.StripTags();
                return !string.IsNullOrEmpty(label);
            }
            catch
            {
                return false;
            }
        }

        public override bool TryGetStatus(Gizmo gizmo, out string status)
        {
            status = null;

            if (!(gizmo is Verse.Gizmo_Slider))
                return false;

            // The text vanilla actually draws on the bar.
            try
            {
                if (BarLabelProperty != null
                    && BarLabelProperty.GetValue(gizmo) is string barLabel
                    && !string.IsNullOrEmpty(barLabel))
                {
                    status = barLabel.StripTags();
                    if (!string.IsNullOrEmpty(status))
                        return true;
                }
            }
            catch
            {
                // A broken BarLabel override falls through to the ValuePercent readout below.
            }

            // Fallback: ValuePercent as a whole-number percentage.
            try
            {
                if (ValuePercentProperty != null)
                {
                    float value = (float)ValuePercentProperty.GetValue(gizmo);
                    status = "RimWorldAccess.Inspection.Gizmo.Status.Percent"
                        .Translate((value * 100).ToString("F0"));
                    return true;
                }
            }
            catch
            {
            }

            status = null;
            return false;
        }

        public override bool TryGetDescription(Gizmo gizmo, out string description)
        {
            description = null;

            if (!(gizmo is Verse.Gizmo_Slider) || GetTooltipMethod == null)
                return false;

            try
            {
                // Surfaces the protected tooltip a sighted player gets on hover: strip tags, drop
                // the redundant title line, flatten newlines.
                string tooltip = GetTooltipMethod.Invoke(gizmo, null) as string;
                description = GizmoTextUtility.FlattenNewlines(
                    StripRedundantSliderTitle(gizmo, (tooltip ?? "").StripTags()));
                if (!string.IsNullOrEmpty(description))
                    return true;

                description = null;
                return false;
            }
            catch
            {
                description = null;
                return false;
            }
        }

        public override bool TryGetSliderAdapter(Gizmo gizmo, out GizmoSliderAdapter adapter)
        {
            adapter = BuildBaseAdapter(gizmo);
            return adapter != null;
        }

        /// <summary>
        /// The standard adjustment adapter: Target within DragRange, stepped by DragRange over
        /// Increments, spoken via BarLabel, gated on vanilla's own IsDraggable. Shared with the
        /// exact-type slider handlers, which override only the Enter semantics and hint. Null when
        /// the gizmo is not a draggable Gizmo_Slider or a member read fails.
        /// </summary>
        internal static GizmoSliderAdapter BuildBaseAdapter(Gizmo gizmo)
        {
            if (!(gizmo is Verse.Gizmo_Slider)
                || TargetProperty == null || IsDraggableProperty == null)
            {
                return null;
            }

            try
            {
                if (!(bool)IsDraggableProperty.GetValue(gizmo))
                    return null;

                FloatRange range = DragRangeProperty != null
                    ? (FloatRange)DragRangeProperty.GetValue(gizmo)
                    : FloatRange.ZeroToOne;
                int increments = IncrementsProperty != null
                    ? (int)IncrementsProperty.GetValue(gizmo)
                    : 20;
                if (increments <= 0)
                    increments = 20;

                string title = null;
                if (TitleProperty != null)
                    title = (TitleProperty.GetValue(gizmo) as string).StripTags();

                return new GizmoSliderAdapter
                {
                    Title = title ?? "",
                    Value = (float)TargetProperty.GetValue(gizmo),
                    Min = range.min,
                    Max = range.max,
                    Step = (range.max - range.min) / increments,
                    Write = value => WriteTarget(gizmo, value),
                    DescribeValue = value => DescribeTargetWithBarLabel(gizmo, value),
                };
            }
            catch
            {
                return null;
            }
        }

        /// <summary>
        /// Writes into BOTH stores vanilla keeps. GizmoOnGUI reassigns Target from the drag cache
        /// every rendered frame, so a Target-only write survives less than a frame on screen, while
        /// the cache alone would miss a gizmo that has not rendered yet, since Initialize seeds the
        /// cache FROM Target. A missing field handle leaves the Target write standing alone.
        /// </summary>
        private static void WriteTarget(Gizmo gizmo, float value)
        {
            TargetValuePctField?.SetValue(gizmo, value);
            TargetProperty.SetValue(gizmo, value);
        }

        /// <summary>The readout after each adjustment step: the new target percent plus the live BarLabel where available.</summary>
        private static string DescribeTargetWithBarLabel(Gizmo gizmo, float value)
        {
            try
            {
                if (BarLabelProperty != null
                    && BarLabelProperty.GetValue(gizmo) is string barLabel
                    && !string.IsNullOrEmpty(barLabel))
                {
                    return "RimWorldAccess.Inspection.Gizmo.Status.SliderTargetWithLabel".Translate(
                        (value * 100).ToString("F0"), barLabel.StripTags());
                }
            }
            catch
            {
            }
            return null;
        }

        /// <summary>
        /// Drops a tooltip's leading line when it just repeats the title: the announcement already
        /// speaks title and status ahead of the description, so keeping it would echo both back to
        /// back. The rest of the tooltip is preserved.
        /// </summary>
        private static string StripRedundantSliderTitle(Gizmo gizmo, string tooltip)
        {
            if (string.IsNullOrEmpty(tooltip)) return tooltip;

            string title = null;
            try
            {
                if (TitleProperty != null)
                    title = TitleProperty.GetValue(gizmo) as string;
            }
            catch { }

            if (string.IsNullOrEmpty(title)) return tooltip;

            // Matched leniently, case-insensitively and allowing either separator, because the
            // tooltip renders the title with the same capitalization pass the header uses.
            int firstBreak = tooltip.IndexOf('\n');
            string firstLine = firstBreak >= 0 ? tooltip.Substring(0, firstBreak) : tooltip;
            string trimmed = firstLine.TrimEnd();
            if (trimmed.StartsWith(title, System.StringComparison.OrdinalIgnoreCase))
            {
                int afterTitle = title.Length;
                if (afterTitle >= trimmed.Length
                    || trimmed[afterTitle] == ':'
                    || trimmed[afterTitle] == ' ')
                {
                    return firstBreak >= 0 ? tooltip.Substring(firstBreak + 1) : "";
                }
            }
            return tooltip;
        }
    }
}
