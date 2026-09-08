using System.Reflection;
using RimWorld;
using Verse;
using Verse.Sound;

namespace RimWorldAccess
{
    /// <summary>
    /// Closes the auto-refuel keyboard gap (rework plan §A.2 item 7): with a
    /// single refuelable selected, CompRefuelable yields only Gizmo_SetFuelLevel
    /// — the separate auto-refuel Command_Toggle exists only in the multi-select
    /// branch (CompRefuelable.CompGetGizmosExtra), so sighted players click the
    /// slider's header checkbox while keyboard users had no path to it at all.
    /// Enter now toggles allowAutoRefuel (when the comp shows that checkbox,
    /// mirroring vanilla's Props.showAllowAutoRefuelToggle gate) and right
    /// bracket adjusts the target fuel level; announcement facets stay with the
    /// Gizmo_Slider base handler via chain resolution.
    /// </summary>
    internal sealed class FuelLevelGizmoHandler : GizmoHandlerBase
    {
        /// <summary>Gizmo_SetFuelLevel.refuelable is private; resolve once.</summary>
        private static readonly FieldInfo RefuelableField = typeof(Gizmo_SetFuelLevel)
            .GetField("refuelable", BindingFlags.Instance | BindingFlags.NonPublic);

        private static CompRefuelable GetRefuelable(Gizmo gizmo)
        {
            if (!(gizmo is Gizmo_SetFuelLevel) || RefuelableField == null)
                return null;
            return RefuelableField.GetValue(gizmo) as CompRefuelable;
        }

        private static bool ShowsAutoRefuelToggle(CompRefuelable refuelable)
        {
            return refuelable?.Props != null && refuelable.Props.showAllowAutoRefuelToggle;
        }

        public override bool TryExecute(Gizmo gizmo, GizmoHandlerContext ctx, out bool runEpilogue)
        {
            runEpilogue = false;

            var refuelable = GetRefuelable(gizmo);
            if (!ShowsAutoRefuelToggle(refuelable))
                return false;

            // Same flip the multi-select Command_Toggle performs, with the
            // vanilla toggle sounds; the menu stays open like the other
            // Enter-toggle sliders (limiter, hemogen packs, suppression).
            refuelable.allowAutoRefuel = !refuelable.allowAutoRefuel;
            if (refuelable.allowAutoRefuel)
                SoundDefOf.Tick_High.PlayOneShotOnCamera();
            else
                SoundDefOf.Tick_Low.PlayOneShotOnCamera();

            string stateStr = (refuelable.allowAutoRefuel
                ? "RimWorldAccess.Inspection.Gizmo.LimiterStateOn"
                : "RimWorldAccess.Inspection.Gizmo.LimiterStateOff").Translate();
            TolkHelper.Speak("RimWorldAccess.Inspection.Gizmo.AutoRefuelToggle".Loc(stateStr));
            return true;
        }

        public override bool TryGetSliderAdapter(Gizmo gizmo, out GizmoSliderAdapter adapter)
        {
            adapter = SliderGizmoHandler.BuildBaseAdapter(gizmo);
            if (adapter == null)
                return false;

            // When the comp shows the auto-refuel checkbox, Enter toggles it
            // (TryExecute above) and the arrows adjust the target directly.
            if (ShowsAutoRefuelToggle(GetRefuelable(gizmo)))
            {
                adapter.InteractionHint = "RimWorldAccess.Inspection.Gizmo.HintFuelAutoRefuelArrows".Translate();
            }
            return true;
        }
    }
}
