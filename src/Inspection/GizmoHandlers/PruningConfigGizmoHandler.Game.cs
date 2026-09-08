using System.Reflection;
using RimWorld;
using Verse;

namespace RimWorldAccess
{
    /// <summary>
    /// Announcement handler for Gizmo_PruningConfig (Ideology, the Gauranlen
    /// tree's pruning target — one of the three "rogue draggables"). Ports the
    /// legacy label case and upgrades the status/description facets to full
    /// vanilla faithfulness: the legacy ladder only ever spoke this gizmo's
    /// value while slider-adjusting, dropping everything the drawn gizmo shows
    /// at rest. Status replicates the three readouts Gizmo_PruningConfig.
    /// GizmoOnGUI draws (current strength bar fill, desired strength line,
    /// pruning hours line — decompiled lines 67-71 and 179). Description
    /// rebuilds the hover tooltip from Gizmo_PruningConfig.GetTip verbatim (the
    /// tooltip only exists render-side via TooltipHandler.TipRegion; see the
    /// lazy-populate rule) and appends the max-dryad thresholds the bar's tick
    /// marks encode, using vanilla's own textual rendering of that same curve
    /// (CompTreeConnection.ConnectionStrengthToMaxDryadsDesc, replicated
    /// because it is private). All CompTreeConnection members used here are
    /// public — only the gizmo's `connection` field is private, read via one
    /// cached FieldInfo and then typed. Execution has no branch here: slider
    /// adjustment stays with GizmoNavigationState's shared slider mode.
    /// </summary>
    internal sealed class PruningConfigGizmoHandler : GizmoHandlerBase
    {
        /// <summary>
        /// Gizmo_PruningConfig.connection (private CompTreeConnection).
        /// </summary>
        private static readonly FieldInfo ConnectionField =
            typeof(Gizmo_PruningConfig).GetField("connection",
                BindingFlags.Instance | BindingFlags.NonPublic);

        public override bool TryGetSliderAdapter(Gizmo gizmo, out GizmoSliderAdapter adapter)
        {
            adapter = null;

            if (!(gizmo is Gizmo_PruningConfig)
                || !(ConnectionField?.GetValue(gizmo) is CompTreeConnection connection))
            {
                return false;
            }

            try
            {
                // Vanilla's hand-rolled drag runs in 20 steps over 0..1
                // (Gizmo_PruningConfig.cs:148); the readout after each step is
                // the new desired strength plus the pruning hours/day the game's
                // own calculator says it takes to maintain.
                adapter = new GizmoSliderAdapter
                {
                    Title = "ConnectionStrength".Translate(),
                    Value = connection.DesiredConnectionStrength,
                    Min = 0f,
                    Max = 1f,
                    Step = 1f / 20f,
                    Write = value => connection.DesiredConnectionStrength = value,
                    DescribeValue = value => DescribeDesiredStrength(connection, value),
                };
                return true;
            }
            catch
            {
                adapter = null;
                return false;
            }
        }

        private static string DescribeDesiredStrength(CompTreeConnection connection, float value)
        {
            try
            {
                float hours = connection.PruningHoursToMaintain(value);
                // "PruningHoursToMaintain" is a sentence template; its leading
                // segment before the colon is the closest vanilla has to a bare
                // "pruning hours" phrase (legacy behavior, ported verbatim).
                string hoursLabel = "PruningHoursToMaintain".Translate().ToString().Split(':')[0].Trim();
                string trailing = "RimWorldAccess.Inspection.Gizmo.Status.PruningHoursValue".Translate(
                    hours.ToString("F1"), hoursLabel);
                return "RimWorldAccess.Inspection.Gizmo.Status.SliderTargetWithLabel".Translate(
                    (value * 100).ToString("F0"), trailing);
            }
            catch
            {
                return null;
            }
        }

        public override bool TryGetLabel(Gizmo gizmo, out string label)
        {
            label = null;
            if (!(gizmo is Gizmo_PruningConfig))
                return false;

            label = "RimWorldAccess.Inspection.Gizmo.Type.PruningConfig".Translate();
            return true;
        }

        public override bool TryGetStatus(Gizmo gizmo, out string status)
        {
            status = null;
            CompTreeConnection connection = GetConnection(gizmo);
            if (connection == null)
                return false;

            try
            {
                // The three values the drawn gizmo shows, each built exactly as
                // GizmoOnGUI builds its label lines (game key + ": " + value).
                // The current-strength segment pairs the gizmo's title key with
                // the percentage vanilla renders centered on the bar fill; the
                // desired/hours lines match decompiled lines 70-71 verbatim.
                // connection.DesiredConnectionStrength is the gizmo property's
                // own non-dragging branch — the drag override is render-only.
                float strength = connection.ConnectionStrength;
                float desired = connection.DesiredConnectionStrength;
                float hours = connection.PruningHoursToMaintain(desired);

                string[] parts =
                {
                    ("ConnectionStrength".Translate() + ": " + strength.ToStringPercent()).Resolve(),
                    ("DesiredConnectionStrength".Translate() + ": " + desired.ToStringPercent()).Resolve(),
                    ("PruningHoursToMaintain".Translate() + ": " + hours.ToString("F1")).Resolve(),
                };
                status = string.Join(", ", parts).StripTags();
                return true;
            }
            catch (System.Exception ex)
            {
                ModLogger.Error($"Exception building pruning config status: {ex.Message}");
                return false;
            }
        }

        public override bool TryGetDescription(Gizmo gizmo, out string description)
        {
            description = null;
            CompTreeConnection connection = GetConnection(gizmo);
            // The tooltip text needs the tree and the connected pawn as named
            // arguments; without either, the vanilla sentence cannot resolve.
            if (connection?.parent == null || connection.ConnectedPawn == null)
                return false;

            try
            {
                // Mirror of Gizmo_PruningConfig.GetTip.
                string text = "DesiredConnectionStrengthDesc".Translate(
                    connection.parent.Named("TREE"),
                    connection.ConnectedPawn.Named("CONNECTEDPAWN"),
                    connection.ConnectionStrengthLossPerDay.ToStringPercent().Named("FALL")).Resolve();
                string affectedBy = connection.AffectingBuildingsDescription("CurrentlyAffectedBy");
                if (!affectedBy.NullOrEmpty())
                    text = text + "\n\n" + affectedBy;

                // The bar's tick marks encode the max-dryad thresholds of
                // maxDryadsPerConnectionStrengthCurve (DrawBar, decompiled
                // lines 141-147); speak them via vanilla's own textual form.
                string thresholds = BuildDryadThresholdDesc(connection);
                if (!thresholds.NullOrEmpty())
                    text = text + "\n\n" + thresholds;

                description = GizmoTextUtility.FlattenNewlines(text.StripTags());
                return !string.IsNullOrEmpty(description);
            }
            catch (System.Exception ex)
            {
                ModLogger.Error($"Exception building pruning config description: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// Replica of the private CompTreeConnection.ConnectionStrengthToMaxDryadsDesc
        /// (decompiled line 625): the wild count, then one entry per curve point.
        /// Returns null when the def carries no threshold curve.
        /// </summary>
        private static string BuildDryadThresholdDesc(CompTreeConnection connection)
        {
            CompProperties_TreeConnection props = connection.Props;
            if (props?.maxDryadsPerConnectionStrengthCurve == null)
                return null;

            string text = string.Concat(
                "MaxDryadsBasedOnConnectionStrength".Translate() + ":\n -  " + "Unconnected".Translate() + ": ",
                props.maxDryadsWild.ToString());
            foreach (CurvePoint item in props.maxDryadsPerConnectionStrengthCurve)
            {
                text = string.Concat(text,
                    "\n -  " + "ConnectionStrengthDisplay".Translate(item.x.ToStringPercent()) + ": ",
                    item.y.ToString());
            }
            return text;
        }

        /// <summary>
        /// Reads the gizmo's private connection field and returns it typed, or
        /// null when the gizmo is not a Gizmo_PruningConfig or the field is
        /// missing/unset (each facet then defers to the generic fallback).
        /// </summary>
        private static CompTreeConnection GetConnection(Gizmo gizmo)
        {
            if (!(gizmo is Gizmo_PruningConfig) || ConnectionField == null)
                return null;
            return ConnectionField.GetValue(gizmo) as CompTreeConnection;
        }
    }
}
