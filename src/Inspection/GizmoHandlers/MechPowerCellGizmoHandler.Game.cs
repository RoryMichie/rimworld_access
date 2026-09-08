using System.Reflection;
using RimWorld;
using Verse;

namespace RimWorldAccess
{
    /// <summary>
    /// Announcement handler for MechPowerCellGizmo (Biotech war urchins and
    /// similar one-shot power-cell mechs). The legacy ladder's label branch
    /// ("MechPowerCellGizmo" → Type.MechPowerCell) is preserved as the
    /// fallback, but the primary label now mirrors what vanilla actually draws
    /// (MechPowerCellGizmo.GizmoOnGUI): Props.labelOverride when set, else the
    /// game's own MechPowerCell string. The legacy GetMechPowerCellStatus
    /// helper was a dead branch — it reflected a "mech" field this gizmo never
    /// had, so it always returned empty; the status is rebuilt from the comp
    /// the gizmo really holds (percent full plus the "Nh" hours-left figure
    /// vanilla prints on the bar). The description surfaces the vanilla
    /// tooltip (Props.tooltipOverride, else MechPowerCellTip), which is
    /// computed purely from Props — no render-time state involved. Execution
    /// has no branch here: the gizmo is display-only.
    /// </summary>
    internal sealed class MechPowerCellGizmoHandler : GizmoHandlerBase
    {
        /// <summary>
        /// MechPowerCellGizmo keeps its CompMechPowerCell in a private field
        /// (decompiled: `private CompMechPowerCell powerCell;`), so reflection
        /// is required for the fetch; all reads after the cast are typed.
        /// </summary>
        private static readonly FieldInfo PowerCellField =
            typeof(MechPowerCellGizmo).GetField("powerCell",
                BindingFlags.Instance | BindingFlags.NonPublic);

        /// <summary>
        /// Resolves the gizmo's comp, or null when the field or its value is
        /// unavailable (e.g. the field was renamed by a game update).
        /// </summary>
        private static CompMechPowerCell GetPowerCell(Gizmo gizmo)
        {
            if (!(gizmo is MechPowerCellGizmo) || PowerCellField == null)
                return null;
            return PowerCellField.GetValue(gizmo) as CompMechPowerCell;
        }

        public override bool TryGetLabel(Gizmo gizmo, out string label)
        {
            label = null;

            if (!(gizmo is MechPowerCellGizmo))
                return false;

            CompMechPowerCell powerCell = GetPowerCell(gizmo);
            CompProperties_MechPowerCell compProps = powerCell?.Props;
            if (compProps == null)
            {
                // Legacy fallback: comp unreachable → our generic type label.
                label = "RimWorldAccess.Inspection.Gizmo.Type.MechPowerCell".Translate();
                return true;
            }

            // Vanilla label priority (MechPowerCellGizmo.GizmoOnGUI): the def's
            // labelOverride when present, else the MechPowerCell key ("Power
            // cell"), verified in Biotech Keyed/Misc_Gameplay.xml.
            label = compProps.labelOverride.NullOrEmpty()
                ? (string)"MechPowerCell".Translate()
                : compProps.labelOverride;
            return true;
        }

        public override bool TryGetStatus(Gizmo gizmo, out string status)
        {
            status = null;

            CompMechPowerCell powerCell = GetPowerCell(gizmo);
            CompProperties_MechPowerCell compProps = powerCell?.Props;
            if (compProps == null || compProps.totalPowerTicks <= 0)
                return false;

            // Bar fill percentage plus the hours-left figure vanilla prints
            // over the bar: CeilToInt(PowerTicksLeft / 2500f) + LetterHour
            // (MechPowerCellGizmo.GizmoOnGUI; 2500 = GenDate.TicksPerHour).
            float percent = powerCell.PercentFull * 100f;
            int hoursLeft = UnityEngine.Mathf.CeilToInt(
                (float)powerCell.PowerTicksLeft / GenDate.TicksPerHour);
            status = "RimWorldAccess.Inspection.Gizmo.Status.PowerCellPercentHours".Translate(
                percent.ToString("F0"), hoursLeft, "LetterHour".Translate());
            return true;
        }

        public override bool TryGetDescription(Gizmo gizmo, out string description)
        {
            description = null;

            if (!(gizmo is MechPowerCellGizmo))
                return false;

            // Vanilla tooltip priority (MechPowerCellGizmo.GizmoOnGUI): the
            // def's tooltipOverride when present, else the MechPowerCellTip key
            // (verified in Biotech Keyed/Misc_Gameplay.xml). Both are pure
            // Props/Keyed data — no render-time state needed. If the comp is
            // unreachable we still surface the generic vanilla tip rather than
            // dropping the facet.
            CompProperties_MechPowerCell compProps = GetPowerCell(gizmo)?.Props;
            string tooltip = (compProps != null && !compProps.tooltipOverride.NullOrEmpty())
                ? compProps.tooltipOverride
                : (string)"MechPowerCellTip".Translate();
            if (tooltip.NullOrEmpty())
                return false;

            description = tooltip.StripTags();
            return true;
        }
    }
}
