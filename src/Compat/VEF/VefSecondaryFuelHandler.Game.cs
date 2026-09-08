using System;
using System.Reflection;
using HarmonyLib;
using RimWorld;
using Verse;
using Verse.Sound;

namespace RimWorldAccess
{
    /// <summary>
    /// Gizmo handler for VEF.Buildings.Gizmo_SetSecondaryFuelLevel (dual-fuel
    /// buildings' secondary tank, VEF/Buildings/Dialogs/
    /// Gizmo_SetSecondaryFuelLevel.cs + Comps/CompRefuelable_DualFuel.cs). The
    /// gizmo extends vanilla Gizmo_Slider, so label, bar text, tooltip, and
    /// slider adjustment all come free through the byType chain's
    /// SliderGizmoHandler fallback — this handler only closes the hidden
    /// header checkbox DrawHeader draws when
    /// CompProperties_Refuelable_DualFuel.showAllowAutoRefuelSecondaryToggle is
    /// set. Mirrors FuelLevelGizmoHandler's shape for the equivalent primary-tank
    /// gap.
    /// </summary>
    internal sealed class VefSecondaryFuelHandler : GizmoHandlerBase
    {
        private readonly Type gizmoType;
        private readonly FieldInfo refuelableField;
        private readonly FieldInfo allowAutoRefuelSecondaryField;
        private readonly bool ready;

        // showAllowAutoRefuelSecondaryToggle is declared on
        // CompProperties_Refuelable_DualFuel, which we never reference by name —
        // resolved once against the comp's live Props runtime type, mirroring
        // VefShieldBubbleGizmoHandler's tooltipKey caching.
        private FieldInfo showToggleField;
        private Type showToggleFieldOwnerType;

        public VefSecondaryFuelHandler(Type gizmoType)
        {
            this.gizmoType = gizmoType;

            // Gizmo_SetSecondaryFuelLevel.refuelable is private; its declared
            // FieldType is CompRefuelable_DualFuel, which we reuse below to
            // resolve allowAutoRefuelSecondary without a second TypeByName call.
            refuelableField = AccessTools.Field(gizmoType, "refuelable");
            Type compType = refuelableField?.FieldType;
            if (compType == null)
            {
                ModLogger.Error("VefSecondaryFuelHandler: could not resolve Gizmo_SetSecondaryFuelLevel.refuelable field; declining all facets.");
                ready = false;
                return;
            }

            allowAutoRefuelSecondaryField = AccessTools.Field(compType, "allowAutoRefuelSecondary");
            ready = allowAutoRefuelSecondaryField != null;
            if (!ready)
                ModLogger.Error("VefSecondaryFuelHandler: could not resolve CompRefuelable_DualFuel.allowAutoRefuelSecondary; declining all facets.");
        }

        private ThingComp GetRefuelable(Gizmo gizmo)
        {
            if (!ready || !gizmoType.IsInstanceOfType(gizmo))
                return null;
            return refuelableField.GetValue(gizmo) as ThingComp;
        }

        private bool ShowsAutoRefuelToggle(ThingComp comp)
        {
            if (comp?.props == null)
                return false;

            Type propsType = comp.props.GetType();
            if (showToggleField == null || showToggleFieldOwnerType != propsType)
            {
                showToggleField = AccessTools.Field(propsType, "showAllowAutoRefuelSecondaryToggle");
                showToggleFieldOwnerType = propsType;
            }
            return showToggleField != null && (bool)showToggleField.GetValue(comp.props);
        }

        public override bool TryExecute(Gizmo gizmo, GizmoHandlerContext ctx, out bool runEpilogue)
        {
            runEpilogue = false;

            ThingComp comp = GetRefuelable(gizmo);
            if (!ShowsAutoRefuelToggle(comp))
                return false;

            bool newState = !(bool)allowAutoRefuelSecondaryField.GetValue(comp);
            // MUTATION-C: mirrors VEF Gizmo_SetSecondaryFuelLevel.DrawHeader
            // checkbox body (raw bool flip); no gated vanilla method exists.
            allowAutoRefuelSecondaryField.SetValue(comp, newState);

            if (newState)
                SoundDefOf.Tick_High.PlayOneShotOnCamera();
            else
                SoundDefOf.Tick_Low.PlayOneShotOnCamera();

            string stateStr = (newState
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
