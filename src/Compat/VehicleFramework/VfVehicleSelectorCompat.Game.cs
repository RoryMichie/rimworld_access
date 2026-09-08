using System;
using System.Collections;
using System.Reflection;
using HarmonyLib;
using Verse;

namespace RimWorldAccess
{
    /// <summary>
    /// Reflection-only compatibility surface for Vehicle Framework's
    /// <c>Vehicles.World.Dialog_VehicleSelector</c>, the vehicle-picker window opened by the
    /// standalone route planner's world-map button (see
    /// <see cref="VfRoutePlannerCompat.OpenSelectorDialog"/>). <see cref="Shell.VfVehicleSelectorScope"/>
    /// is the sole consumer and never touches reflection directly.
    ///
    /// The mode toggle (<see cref="ToggleMode"/>) and the per-item selection toggles
    /// (<see cref="ToggleDefSelection"/>/<see cref="ToggleVehicleSelection"/>) are both
    /// MUTATION-C: the dialog's mode switch is a SmashTools <c>UIElements.ClickableLabel</c> and
    /// its per-row selection is an inline <c>Widgets.Checkbox</c> draw call, neither of which
    /// exposes an invocable vanilla delegate. The dialog's own Start/Cancel buttons need no compat
    /// surface: they are real <c>Widgets.ButtonText</c> calls, captured and clicked through the
    /// scope's automatic Buttons region (vehicle A).
    /// </summary>
    internal static class VfVehicleSelectorCompat
    {
        private static readonly Type selectorType;
        private static readonly Type vehicleDefType;
        private static readonly Type vehiclePawnType;
        private static readonly Type vehicleTypeEnumType;

        private static readonly FieldInfo showVehicleDefsField;
        private static readonly FieldInfo availableVehiclesField;
        private static readonly FieldInfo availableVehicleDefsField;
        private static readonly FieldInfo selectedDefsField;
        private static readonly FieldInfo selectedVehiclesField;

        private static readonly MethodInfo selectedTypeGetter;

        private static readonly MethodInfo recalcHeightMethod;
        private static readonly MethodInfo addDefMethod;
        private static readonly MethodInfo removeDefMethod;
        private static readonly MethodInfo containsDefMethod;
        private static readonly MethodInfo clearDefMethod;
        private static readonly MethodInfo addVehicleMethod;
        private static readonly MethodInfo removeVehicleMethod;
        private static readonly MethodInfo containsVehicleMethod;
        private static readonly MethodInfo clearVehicleMethod;

        private static readonly FieldInfo typeField;
        private static readonly object universalValue;

        private static readonly bool ready;

        public static bool Ready => ready;
        public static Type SelectorType => selectorType;

        static VfVehicleSelectorCompat()
        {
            var surface = new ReflectionSurface("VfVehicleSelectorCompat");

            selectorType = surface.Type("Vehicles.World.Dialog_VehicleSelector");
            vehicleDefType = surface.Type("Vehicles.VehicleDef");
            vehiclePawnType = surface.Supplied("Vehicles.VehiclePawn", VfVehiclePawn.VehiclePawnType);
            vehicleTypeEnumType = surface.Type("Vehicles.VehicleType");

            showVehicleDefsField = surface.Field(selectorType, "showVehicleDefs");
            availableVehiclesField = surface.Field(selectorType, "availableVehicles");
            availableVehicleDefsField = surface.Field(selectorType, "availableVehicleDefs");
            selectedDefsField = surface.Field(selectorType, "selectedDefs");
            selectedVehiclesField = surface.Field(selectorType, "selectedVehicles");

            selectedTypeGetter = surface.Property(selectorType, "SelectedType")?.GetGetMethod(true);
            recalcHeightMethod = surface.Method(selectorType, "RecalculateHeight");

            typeField = surface.Field(vehicleDefType, "type");
            if (vehicleTypeEnumType != null)
            {
                try
                {
                    universalValue = Enum.Parse(vehicleTypeEnumType, "Universal");
                }
                catch (Exception ex)
                {
                    ModLogger.Error($"VfVehicleSelectorCompat: could not resolve VehicleType.Universal: {ex.Message}");
                }
            }

            // The set members resolve off the fields' own runtime collection types.
            if (selectedDefsField != null && vehicleDefType != null)
            {
                Type[] defArg = { vehicleDefType };
                addDefMethod = surface.Method(selectedDefsField.FieldType, "Add", defArg);
                removeDefMethod = surface.Method(selectedDefsField.FieldType, "Remove", defArg);
                containsDefMethod = surface.Method(selectedDefsField.FieldType, "Contains", defArg);
                clearDefMethod = surface.Method(selectedDefsField.FieldType, "Clear");
            }
            if (selectedVehiclesField != null && vehiclePawnType != null)
            {
                Type[] pawnArg = { vehiclePawnType };
                addVehicleMethod = surface.Method(selectedVehiclesField.FieldType, "Add", pawnArg);
                removeVehicleMethod = surface.Method(selectedVehiclesField.FieldType, "Remove", pawnArg);
                containsVehicleMethod = surface.Method(selectedVehiclesField.FieldType, "Contains", pawnArg);
                clearVehicleMethod = surface.Method(selectedVehiclesField.FieldType, "Clear");
            }

            ready = surface.Ready && universalValue != null
                && addDefMethod != null && removeDefMethod != null && containsDefMethod != null && clearDefMethod != null
                && addVehicleMethod != null && removeVehicleMethod != null && containsVehicleMethod != null && clearVehicleMethod != null;
        }

        public static bool ShowVehicleDefs(Window w)
        {
            try
            {
                return (bool)showVehicleDefsField.GetValue(w);
            }
            catch (Exception ex)
            {
                ModLogger.Error($"VfVehicleSelectorCompat.ShowVehicleDefs failed: {ex.Message}");
                return false;
            }
        }

        public static IList AvailableVehicleDefs(Window w)
        {
            try
            {
                return availableVehicleDefsField.GetValue(w) as IList;
            }
            catch (Exception ex)
            {
                ModLogger.Error($"VfVehicleSelectorCompat.AvailableVehicleDefs failed: {ex.Message}");
                return null;
            }
        }

        public static IList AvailableVehicles(Window w)
        {
            try
            {
                return availableVehiclesField.GetValue(w) as IList;
            }
            catch (Exception ex)
            {
                ModLogger.Error($"VfVehicleSelectorCompat.AvailableVehicles failed: {ex.Message}");
                return null;
            }
        }

        /// <summary>The dialog's own SelectedType getter -- the type every selection must currently match, or VehicleType.Universal when nothing is selected.</summary>
        public static object CurrentSelectedType(Window w)
        {
            try
            {
                return selectedTypeGetter.Invoke(w, null);
            }
            catch (Exception ex)
            {
                ModLogger.Error($"VfVehicleSelectorCompat.CurrentSelectedType failed: {ex.Message}");
                return universalValue;
            }
        }

        /// <summary>A row item's own VehicleType -- item is a VehiclePawn (its .def is the VehicleDef instance) or a VehicleDef itself.</summary>
        public static object TypeOf(object item)
        {
            try
            {
                Def def = item is Pawn pawn ? pawn.def : item as Def;
                return def != null ? typeField.GetValue(def) : universalValue;
            }
            catch (Exception ex)
            {
                ModLogger.Error($"VfVehicleSelectorCompat.TypeOf failed: {ex.Message}");
                return universalValue;
            }
        }

        /// <summary>Mirrors DrawVehicle/DrawVehicleDef's own disabled gate: not Universal, and not a match.</summary>
        public static bool IsIncompatibleType(object selectedType, object itemType)
        {
            if (selectedType == null || itemType == null)
                return false;
            return !Equals(selectedType, universalValue) && !Equals(selectedType, itemType);
        }

        public static bool IsDefSelected(Window w, object def)
        {
            try
            {
                object set = selectedDefsField.GetValue(w);
                return set != null && (bool)containsDefMethod.Invoke(set, new object[] { def });
            }
            catch (Exception ex)
            {
                ModLogger.Error($"VfVehicleSelectorCompat.IsDefSelected failed: {ex.Message}");
                return false;
            }
        }

        public static bool IsVehicleSelected(Window w, Pawn vehicle)
        {
            try
            {
                object set = selectedVehiclesField.GetValue(w);
                return set != null && (bool)containsVehicleMethod.Invoke(set, new object[] { vehicle });
            }
            catch (Exception ex)
            {
                ModLogger.Error($"VfVehicleSelectorCompat.IsVehicleSelected failed: {ex.Message}");
                return false;
            }
        }

        /// <summary>Mode switch: flips showVehicleDefs, clears both selection sets, and recalculates the scroll height -- verbatim body of Dialog_VehicleSelector's own ClickableLabel branch.</summary>
        public static void ToggleMode(Window w)
        {
            try
            {
                bool current = ShowVehicleDefs(w);
                object selectedDefs = selectedDefsField.GetValue(w);
                object selectedVehicles = selectedVehiclesField.GetValue(w);
                // MUTATION-C: mirrors Dialog_VehicleSelector.DoWindowContents' mode-toggle
                // UIElements.ClickableLabel branch (showVehicleDefs = !showVehicleDefs; both
                // selection sets cleared; RecalculateHeight()); the SmashTools ClickableLabel has
                // no invokable vanilla delegate for a keyboard caller to invoke instead.
                showVehicleDefsField.SetValue(w, !current);
                clearDefMethod.Invoke(selectedDefs, null);
                clearVehicleMethod.Invoke(selectedVehicles, null);
                recalcHeightMethod.Invoke(w, null);
            }
            catch (Exception ex)
            {
                ModLogger.Error($"VfVehicleSelectorCompat.ToggleMode failed: {ex.Message}");
            }
        }

        /// <summary>Returns the new selection state (true = now selected).</summary>
        public static bool ToggleDefSelection(Window w, object def)
        {
            try
            {
                object set = selectedDefsField.GetValue(w);
                bool currentlySelected = (bool)containsDefMethod.Invoke(set, new object[] { def });
                // MUTATION-C: mirrors Dialog_VehicleSelector.DrawVehicleDef's inline
                // checkbox-sync branch (selectedDefs.Add/Remove(vehicleDef)); the
                // Widgets.Checkbox draw call has no invokable vanilla delegate for a keyboard
                // caller to invoke instead.
                if (currentlySelected)
                    removeDefMethod.Invoke(set, new object[] { def });
                else
                    addDefMethod.Invoke(set, new object[] { def });
                return !currentlySelected;
            }
            catch (Exception ex)
            {
                ModLogger.Error($"VfVehicleSelectorCompat.ToggleDefSelection failed: {ex.Message}");
                return false;
            }
        }

        /// <summary>Returns the new selection state (true = now selected).</summary>
        public static bool ToggleVehicleSelection(Window w, Pawn vehicle)
        {
            try
            {
                object set = selectedVehiclesField.GetValue(w);
                bool currentlySelected = (bool)containsVehicleMethod.Invoke(set, new object[] { vehicle });
                // MUTATION-C: mirrors Dialog_VehicleSelector.DrawVehicle's inline checkbox-sync
                // branch (selectedVehicles.Add/Remove(vehicle)); the Widgets.Checkbox draw call
                // has no invokable vanilla delegate for a keyboard caller to invoke instead.
                if (currentlySelected)
                    removeVehicleMethod.Invoke(set, new object[] { vehicle });
                else
                    addVehicleMethod.Invoke(set, new object[] { vehicle });
                return !currentlySelected;
            }
            catch (Exception ex)
            {
                ModLogger.Error($"VfVehicleSelectorCompat.ToggleVehicleSelection failed: {ex.Message}");
                return false;
            }
        }
    }
}
