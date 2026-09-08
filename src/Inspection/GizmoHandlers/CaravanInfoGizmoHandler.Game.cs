using System.Collections.Generic;
using System.Reflection;
using RimWorld.Planet;
using Verse;

namespace RimWorldAccess
{
    /// <summary>
    /// Port of the original GetGizmoLabel ladder's "Gizmo_CaravanInfo" branch
    /// (static Type.CaravanInfo label). The status facet is new relative to the
    /// legacy ladder: vanilla draws the full caravan stat block via
    /// CaravanUIUtility.DrawCaravanInfo — mass usage/capacity, movement speed
    /// (tiles per day, or Immobile when overloaded), days worth of food (with
    /// spoilage), foraged food per day (with food type), and visibility — all
    /// computed from public members of the gizmo's caravan using the game's own
    /// calculators (decompiled RimWorld.Planet.Gizmo_CaravanInfo.GizmoOnGUI).
    /// The ticksToArrive value vanilla computes there only tints the food
    /// label's color (CaravanUIUtility.GetDaysWorthOfFoodColor); it is never
    /// drawn as text, so no ETA figure belongs in the status. Formatting reuses
    /// the shared CaravanStatFormatter (same keys the caravan-formation and
    /// pod-loading screens speak), which mirrors DrawCaravanInfo's own value
    /// branches: overloaded mass, Immobile speed, Infinite/spoiling food, and
    /// foraged food type suffix. Execution has no branch here — the gizmo is
    /// display-only (vanilla absorbs clicks; the legacy gizmo list skipped it
    /// for activation for the same reason).
    /// </summary>
    internal sealed class CaravanInfoGizmoHandler : GizmoHandlerBase
    {
        /// <summary>
        /// Gizmo_CaravanInfo keeps its caravan in a private instance field; cache
        /// the FieldInfo once rather than re-reflecting per announcement.
        /// </summary>
        private static readonly FieldInfo CaravanField = typeof(Gizmo_CaravanInfo)
            .GetField("caravan", BindingFlags.Instance | BindingFlags.NonPublic);

        public override bool TryGetLabel(Gizmo gizmo, out string label)
        {
            label = null;
            if (!(gizmo is Gizmo_CaravanInfo))
                return false;

            label = "RimWorldAccess.Inspection.Gizmo.Type.CaravanInfo".Translate();
            return true;
        }

        public override bool TryGetStatus(Gizmo gizmo, out string status)
        {
            status = null;
            if (!(gizmo is Gizmo_CaravanInfo))
                return false;

            try
            {
                var caravan = CaravanField?.GetValue(gizmo) as Caravan;
                // Vanilla draws nothing for a despawned caravan (GizmoOnGUI's
                // early-out), so faithfully report no status.
                if (caravan == null || !caravan.Spawned)
                    return false;

                float massUsage = caravan.MassUsage;
                float massCapacity = caravan.MassCapacity;
                // Vanilla's immobile branch keys off usage exceeding capacity
                // (CaravanUIUtility.GetMovementSpeedLabel call site).
                bool immobile = massUsage > massCapacity;
                float tilesPerDay = TilesPerDayCalculator.ApproxTilesPerDay(caravan);
                (float days, float tillRot) food = caravan.DaysWorthOfFood;
                (ThingDef foodType, float perDay) foraged = caravan.forage.ForagedFoodPerDay;
                float visibility = caravan.Visibility;

                var parts = new List<string>
                {
                    CaravanStatFormatter.FormatMass(massUsage, massCapacity),
                    CaravanStatFormatter.FormatSpeed(tilesPerDay, immobile, includeDescription: false),
                    CaravanStatFormatter.FormatFood(food.days, food.tillRot, includeDescription: false),
                    CaravanStatFormatter.FormatForaging(foraged.foodType, foraged.perDay, includeDescription: false),
                    CaravanStatFormatter.FormatVisibility(visibility, includeDescription: false)
                };

                // Single spoken line; period separators per announcement rules.
                status = string.Join(". ", parts);
                return true;
            }
            catch (System.Exception ex)
            {
                ModLogger.Error($"Exception reading Gizmo_CaravanInfo: {ex.Message}");
                return false;
            }
        }
    }
}
