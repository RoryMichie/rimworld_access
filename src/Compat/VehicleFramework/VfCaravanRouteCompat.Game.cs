using System;
using System.Reflection;
using HarmonyLib;
using RimWorld;
using RimWorld.Planet;
using Verse;

namespace RimWorldAccess
{
    /// <summary>
    /// Reflection-only compatibility surface for Vehicle Framework's
    /// <c>Vehicles.Patch_FormCaravanDialog.WorldRoutePannerReroute</c> (VF's own spelling --
    /// "Panner", not "Planner"). VF's transpiler replaces vanilla's own
    /// <c>WorldRoutePlanner.Start(Dialog_FormCaravan)</c> call inside
    /// <c>Dialog_FormCaravan.DoBottomButtons</c> with a call to this method, which decides for
    /// itself: a caravan with vehicles selected starts VF's own <c>VehicleRoutePlanner</c>
    /// (caravaning mode, with VF's own onFinish/onChooseRoute callbacks); otherwise it starts the
    /// vanilla planner exactly as before. <see cref="CaravanFormationScope.ChangeRoute"/>'s own
    /// button body bypasses that transpile (it calls <c>Find.WorldRoutePlanner.Start(dialog)</c>
    /// directly, never going through <c>DoBottomButtons</c>), so a keyboard-formed vehicle caravan
    /// would otherwise get the wrong, non-vehicle-aware planner. <see cref="StartRoutePlanning"/>
    /// is vehicle A: it invokes VF's own real decision method rather than re-implementing it.
    /// </summary>
    internal static class VfCaravanRouteCompat
    {
        private static readonly Type patchType;

        private static readonly MethodInfo rerouteMethod;
        private static readonly FieldInfo mapField;
        private static readonly FieldInfo autoSuppliesField;

        private static readonly bool ready;

        public static bool Ready => ready;

        static VfCaravanRouteCompat()
        {
            var surface = new ReflectionSurface("VfCaravanRouteCompat");

            patchType = surface.Type("Vehicles.Patch_FormCaravanDialog");

            rerouteMethod = surface.Method(patchType, "WorldRoutePannerReroute",
                new[] { typeof(WorldRoutePlanner), typeof(Dialog_FormCaravan), typeof(Map), typeof(bool) });
            mapField = surface.Field(typeof(Dialog_FormCaravan), "map");
            autoSuppliesField = surface.Field(typeof(Dialog_FormCaravan), "autoSelectTravelSupplies");

            ready = surface.Ready;
        }

        /// <summary>
        /// Returns true when VF handled it (VF present), false when VF is absent and the caller
        /// must fall back to the vanilla planner. Deliberately returns true whenever VF is present
        /// even if the invoke itself throws: the caller must never also run the vanilla
        /// Start(dialog) once VF is installed, or a vehicle caravan double-handles the route button.
        /// </summary>
        public static bool StartRoutePlanning(Dialog_FormCaravan dialog)
        {
            if (!ready || dialog == null)
                return false;
            try
            {
                Map map = mapField.GetValue(dialog) as Map;
                bool autoSupplies = (bool)autoSuppliesField.GetValue(dialog);
                rerouteMethod.Invoke(null, new object[] { Find.WorldRoutePlanner, dialog, map, autoSupplies });
            }
            catch (Exception ex)
            {
                ModLogger.Error($"VfCaravanRouteCompat.StartRoutePlanning failed: {ex.Message}");
            }
            return true;
        }
    }
}
