using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;

namespace RimWorldAccess
{
    /// <summary>
    /// Gizmo handler for Vehicle Framework's Vehicles.Rendering.Gizmo_RefuelableFuelTravel (every
    /// VF vehicle's fuel/charge gizmo). The gizmo extends vanilla Gizmo_Slider, so label and the
    /// bar-drag adjustment already work through the byType chain's SliderGizmoHandler fallback
    /// (its BarLabel override supplies the bar text, and GetTooltip() returns an intentionally
    /// blank string — VF draws its own tooltips on the two header buttons instead). This handler
    /// closes the two hover-only gaps DrawHeader adds: the auto-refuel/charging checkbox (toggled
    /// by ToggleSwitch()) and, for chemfuel vehicles, the refuel-from-cargo button (opens a
    /// vanilla Dialog_Slider whose accept calls CompFueledTravel.ConsumeFuelFromInventory).
    /// </summary>
    internal sealed class VfRefuelGizmoHandler : GizmoHandlerBase
    {
        private readonly Type gizmoType;

        // Gizmo_RefuelableFuelTravel members (all private on the gizmo itself).
        private readonly FieldInfo refuelableField;
        private readonly MethodInfo toggleSwitchMethod;
        private readonly MethodInfo updateDisableStatusMethod;
        private readonly FieldInfo fuelAvailableField;
        private readonly FieldInfo refuelFromInventoryDisabledField;
        private readonly FieldInfo refuelFromInventoryDisabledReasonField;
        private readonly MethodInfo refuelTipMethod;
        private readonly MethodInfo powerNetTipMethod;
        private readonly MethodInfo refuelFromInventoryTipMethod;

        // CompFueledTravel members (all public, but resolved via reflection since
        // this file takes no compile-time reference to the Vehicles assembly).
        private readonly PropertyInfo fuelProperty;
        private readonly PropertyInfo fuelCapacityProperty;
        private readonly FieldInfo allowAutoRefuelField;
        private readonly PropertyInfo chargingProperty;
        private readonly PropertyInfo propsProperty;
        private readonly MethodInfo consumeFuelFromInventoryMethod;

        // CompProperties_FueledTravel members, resolved from Props' own runtime type.
        private readonly PropertyInfo electricPoweredProperty;
        private readonly FieldInfo fuelTypeField;

        private readonly bool ready;

        internal bool Ready => ready;

        public VfRefuelGizmoHandler(Type gizmoType)
        {
            this.gizmoType = gizmoType;

            var surface = new ReflectionSurface("VfRefuelGizmoHandler");

            refuelableField = surface.Field(gizmoType, "refuelable");
            toggleSwitchMethod = surface.Method(gizmoType, "ToggleSwitch");
            updateDisableStatusMethod = surface.Method(gizmoType, "UpdateDisableStatus");
            fuelAvailableField = surface.Field(gizmoType, "fuelAvailable");
            refuelFromInventoryDisabledField = surface.Field(gizmoType, "refuelFromInventoryDisabled");
            refuelFromInventoryDisabledReasonField = surface.Field(gizmoType, "refuelFromInventoryDisabledReason");
            refuelTipMethod = surface.Method(gizmoType, "RefuelTip");
            powerNetTipMethod = surface.Method(gizmoType, "PowerNetTip");
            refuelFromInventoryTipMethod = surface.Method(gizmoType, "RefuelFromInventoryTip");

            Type compType = refuelableField?.FieldType;
            fuelProperty = surface.Property(compType, "Fuel");
            fuelCapacityProperty = surface.Property(compType, "FuelCapacity");
            allowAutoRefuelField = surface.Field(compType, "allowAutoRefuel");
            chargingProperty = surface.Property(compType, "Charging");
            propsProperty = surface.Property(compType, "Props");
            consumeFuelFromInventoryMethod = surface.Method(compType, "ConsumeFuelFromInventory", new[] { typeof(int) });

            Type propsType = propsProperty?.PropertyType;
            electricPoweredProperty = surface.Property(propsType, "ElectricPowered");
            fuelTypeField = surface.Field(propsType, "fuelType");

            ready = surface.Ready;
        }

        /// <summary>
        /// Shared gate + lookup: resolves the gizmo's comp, its Props, and whether
        /// the vehicle is electric-powered. False when this handler isn't ready or
        /// the gizmo isn't our type.
        /// </summary>
        private bool Applies(Gizmo gizmo, out object comp, out object props, out bool electric)
        {
            comp = null;
            props = null;
            electric = false;

            if (!ready || !gizmoType.IsInstanceOfType(gizmo))
                return false;

            comp = refuelableField.GetValue(gizmo);
            if (comp == null)
                return false;

            props = propsProperty.GetValue(comp);
            if (props == null)
                return false;

            electric = (bool)electricPoweredProperty.GetValue(props);
            return true;
        }

        // No TryGetStatus override: the chain's SliderGizmoHandler already speaks the drawn
        // BarLabel ("fuel / capacity"), and claiming the status facet here would shadow those
        // numbers. The auto-refuel / charging state is already carried in words by VF's own
        // tooltip builders, which TryGetDescription surfaces verbatim.

        public override bool TryGetDescription(Gizmo gizmo, out string description)
        {
            description = null;
            if (!Applies(gizmo, out object _, out object _, out bool electric))
                return false;

            try
            {
                // VF's own tooltip builders already carry its translated words and hotkey line.
                string mainTip = electric
                    ? powerNetTipMethod.Invoke(gizmo, null) as string
                    : refuelTipMethod.Invoke(gizmo, null) as string;

                string combined = mainTip ?? "";
                if (!electric)
                {
                    string cargoTip = refuelFromInventoryTipMethod.Invoke(gizmo, null) as string;
                    if (!string.IsNullOrEmpty(cargoTip))
                        combined += "\n" + cargoTip;
                }

                description = GizmoTextUtility.FlattenNewlines(combined.StripTags());
                return !string.IsNullOrEmpty(description);
            }
            catch (Exception ex)
            {
                ModLogger.Error($"VfRefuelGizmoHandler.TryGetDescription failed: {ex.Message}");
                description = null;
                return false;
            }
        }

        public override bool TryExecute(Gizmo gizmo, GizmoHandlerContext ctx, out bool runEpilogue)
        {
            runEpilogue = false;
            if (!Applies(gizmo, out object comp, out object _, out bool electric))
                return false;

            try
            {
                bool wasCharging = electric && (bool)chargingProperty.GetValue(comp);

                // Mutation A: the exact private method both the drawn header button and the
                // gizmo's own hotkey branch call.
                toggleSwitchMethod.Invoke(gizmo, null);

                if (electric)
                {
                    // ToggleCharging can fail to connect (TryConnectPower returns false, VF plays
                    // ClickReject) — read the resulting state rather than assume the flip took.
                    bool nowCharging = (bool)chargingProperty.GetValue(comp);
                    string stateStr = nowCharging ? "On".Translate() : "Off".Translate();
                    string announcement = "RimWorldAccess.Compat.Vf.ChargingToggle".Loc(stateStr).ToString();
                    if (!wasCharging && !nowCharging)
                        announcement += " " + "RimWorldAccess.Compat.Vf.NoPowerNet".Translate();
                    TolkHelper.SpeakData(announcement);
                }
                else
                {
                    bool allowAutoRefuel = (bool)allowAutoRefuelField.GetValue(comp);
                    string stateStr = (allowAutoRefuel
                        ? "RimWorldAccess.Inspection.Gizmo.LimiterStateOn"
                        : "RimWorldAccess.Inspection.Gizmo.LimiterStateOff").Translate();
                    TolkHelper.Speak("RimWorldAccess.Inspection.Gizmo.AutoRefuelToggle".Loc(stateStr));
                }
                return true;
            }
            catch (Exception ex)
            {
                ModLogger.Error($"VfRefuelGizmoHandler.TryExecute failed: {ex.Message}");
                return false;
            }
        }

        public override bool TryGetSliderAdapter(Gizmo gizmo, out GizmoSliderAdapter adapter)
        {
            adapter = SliderGizmoHandler.BuildBaseAdapter(gizmo);
            if (adapter == null)
                return false;

            if (!Applies(gizmo, out object _, out object _, out bool _))
                return true;

            // Enter toggles auto-refuel (TryExecute above); the arrows adjust the target. VF gates
            // dragging with IsDraggable => !ElectricPowered, so BuildBaseAdapter already returned
            // null for electric vehicles and only the chemfuel case reaches here.
            adapter.InteractionHint = "RimWorldAccess.Compat.Vf.HintAutoRefuelToggleArrows".Translate();
            return true;
        }

        public override bool TryGetExtraOptions(Gizmo gizmo, List<FloatMenuOption> options)
        {
            if (!Applies(gizmo, out object comp, out object props, out bool electric))
                return false;

            // Command.GizmoOnGUIInt rejects every click on a disabled command before
            // ProcessInput/DrawHeader's button handlers run, so a sighted player cannot reach the
            // refuel-from-cargo button while the gizmo is disabled either.
            if (gizmo.Disabled || electric)
                return false;

            try
            {
                string label = "RimWorldAccess.Compat.Vf.RefuelFromCargo".Translate();
                options.Add(new FloatMenuOption(label, () => OpenRefuelFromCargo(gizmo, comp, props)));
                return true;
            }
            catch (Exception ex)
            {
                ModLogger.Error($"VfRefuelGizmoHandler.TryGetExtraOptions failed: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// MUTATION-C: mirrors Gizmo_RefuelableFuelTravel.DrawHeader refuel-from-cargo
        /// branch; body is inline IMGUI, no invocable vehicle. Reproduces its exact
        /// disable check, max-refuel math, and Dialog_Slider construction, so the
        /// keyboard path opens the same vanilla-adapted dialog sighted players use.
        /// </summary>
        private void OpenRefuelFromCargo(Gizmo gizmo, object comp, object props)
        {
            try
            {
                updateDisableStatusMethod.Invoke(gizmo, null);

                bool disabled = (bool)refuelFromInventoryDisabledField.GetValue(gizmo);
                if (disabled)
                {
                    string reason = refuelFromInventoryDisabledReasonField.GetValue(gizmo) as string;
                    Messages.Message(reason, MessageTypeDefOf.RejectInput);
                    return;
                }

                float fuelAvailable = (float)fuelAvailableField.GetValue(gizmo);
                float fuel = (float)fuelProperty.GetValue(comp);
                float fuelCapacity = (float)fuelCapacityProperty.GetValue(comp);
                int maxRefuel = Mathf.FloorToInt(Mathf.Min(fuelAvailable, fuelCapacity - fuel));
                if (maxRefuel < 1)
                    maxRefuel = 1;

                string fuelLabel = (fuelTypeField.GetValue(props) as ThingDef)?.label ?? "";
                Action<int> consume = AccessTools.MethodDelegate<Action<int>>(consumeFuelFromInventoryMethod, comp);

                var slider = new Dialog_Slider(
                    count => "RimWorldAccess.Compat.Vf.RefuelFromInventoryCount".Translate(count, fuelLabel),
                    1, maxRefuel, consume);
                Find.WindowStack.Add(slider);
            }
            catch (Exception ex)
            {
                ModLogger.Error($"VfRefuelGizmoHandler refuel-from-cargo failed: {ex.Message}");
            }
        }
    }
}
