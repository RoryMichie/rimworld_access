using System;
using System.Collections;
using System.Reflection;
using HarmonyLib;
using RimWorld;
using RimWorld.Planet;
using Verse;

namespace RimWorldAccess
{
    /// <summary>
    /// Reflection-only compatibility surface for Vehicle Framework's
    /// <c>Vehicles.World.VehicleRoutePlanner</c> -- the vehicle-aware twin of vanilla
    /// <c>RimWorld.Planet.WorldRoutePlanner</c> -- combined with the accessibility-state logic
    /// itself, the VF analog of <see cref="RoutePlannerState"/>. <see cref="VfRoutePlannerScope"/>
    /// and <see cref="Shell.WorldScope"/>'s VF opener claim are the sole consumers and never touch
    /// reflection directly.
    ///
    /// Mutation vehicles: <c>TryAddWaypoint</c>/<c>TryRemoveWaypoint</c>/<c>Stop</c> are vehicle A
    /// (self-gating VF methods, invoked unconditionally -- rejects speak themselves through VF's
    /// own <c>Messages.Message</c> pipeline, never double-announced here). Opening the selector
    /// dialog (<see cref="OpenSelectorDialog"/>) is also vehicle A: it mirrors VF's own
    /// <c>DoRoutePlannerButton</c> inactive branch, constructing VF's real
    /// <c>Dialog_VehicleSelector</c> rather than reimplementing its Start button logic.
    /// </summary>
    internal static class VfRoutePlannerCompat
    {
        private static readonly Type plannerType;
        private static readonly Type waypointType;
        private static readonly Type selectorType;

        private static readonly MethodInfo isActiveGetter;
        private static readonly MethodInfo isCaravaningGetter;

        private static readonly FieldInfo waypointsField;
        private static readonly FieldInfo cachedTicksField;
        private static readonly FieldInfo onChooseRouteField;

        private static readonly MethodInfo tryAddMethod;
        private static readonly MethodInfo tryRemoveMethod;
        private static readonly MethodInfo mostRecentAtMethod;
        private static readonly MethodInfo stopMethod;

        private static readonly MethodInfo waypointTileGetter;

        private static readonly bool ready;

        public static bool Ready => ready;

        static VfRoutePlannerCompat()
        {
            var surface = new ReflectionSurface("VfRoutePlannerCompat");

            plannerType = surface.Type("Vehicles.World.VehicleRoutePlanner");
            selectorType = surface.Type("Vehicles.World.Dialog_VehicleSelector");

            // VF's VehicleRoutePlanner reuses vanilla's own RoutePlannerWaypoint WorldObject, not
            // a VF-namespaced type -- referenced directly so it can never drift or resolve to null.
            waypointType = typeof(RoutePlannerWaypoint);

            isActiveGetter = surface.Property(plannerType, "IsActive")?.GetGetMethod(true);
            isCaravaningGetter = surface.Property(plannerType, "IsCaravaning")?.GetGetMethod(true);

            waypointsField = surface.Field(plannerType, "waypoints");
            cachedTicksField = surface.Field(plannerType, "cachedTicksToWaypoint");
            onChooseRouteField = surface.Field(plannerType, "onChooseRouteCallback");

            tryAddMethod = surface.Method(plannerType, "TryAddWaypoint", new[] { typeof(PlanetTile), typeof(bool) });
            stopMethod = surface.Method(plannerType, "Stop");
            tryRemoveMethod = surface.Method(plannerType, "TryRemoveWaypoint", new[] { waypointType, typeof(bool) });
            mostRecentAtMethod = surface.Method(plannerType, "MostRecentWaypointAt", new[] { typeof(int) });
            waypointTileGetter = surface.Property(waypointType, "Tile")?.GetGetMethod(true);

            ready = surface.Ready;
        }

        // ------------------------------------------------------------------
        // The live planner instance.
        // ------------------------------------------------------------------

        private static object Planner()
        {
            if (!ready || Find.World == null)
                return null;
            return Find.World.GetComponent(plannerType);
        }

        // ------------------------------------------------------------------
        // Public accessibility surface.
        // ------------------------------------------------------------------

        public static bool IsActive
        {
            get
            {
                object planner = Planner();
                if (planner == null)
                    return false;
                try
                {
                    return (bool)isActiveGetter.Invoke(planner, null);
                }
                catch (Exception ex)
                {
                    ModLogger.Error($"VfRoutePlannerCompat.IsActive failed: {ex.Message}");
                    return false;
                }
            }
        }

        public static bool IsCaravaning
        {
            get
            {
                object planner = Planner();
                if (planner == null)
                    return false;
                try
                {
                    return (bool)isCaravaningGetter.Invoke(planner, null);
                }
                catch (Exception ex)
                {
                    ModLogger.Error($"VfRoutePlannerCompat.IsCaravaning failed: {ex.Message}");
                    return false;
                }
            }
        }

        public static int WaypointCount
        {
            get
            {
                object planner = Planner();
                if (planner == null)
                    return 0;
                try
                {
                    return (waypointsField.GetValue(planner) as IList)?.Count ?? 0;
                }
                catch (Exception ex)
                {
                    ModLogger.Error($"VfRoutePlannerCompat.WaypointCount failed: {ex.Message}");
                    return 0;
                }
            }
        }

        /// <summary>Current-tile guard, then TryAddWaypoint (vehicle A -- self-gates and speaks rejects); a before/after count comparison drives the success announcement.</summary>
        public static void AddWaypoint()
        {
            object planner = Planner();
            if (planner == null)
                return;
            try
            {
                PlanetTile currentTile = WorldNavigationState.CurrentSelectedTile;
                if (!GuardHelper.RequireValidTile(currentTile, SpeechPriority.High))
                    return;

                int countBefore = WaypointCount;
                tryAddMethod.Invoke(planner, new object[] { currentTile, true });
                int countAfter = WaypointCount;

                if (countAfter > countBefore)
                {
                    if (countAfter >= 2)
                    {
                        string timeString = EtaTicksToLastWaypoint(planner).ToStringTicksToDays("0.#");
                        TolkHelper.Speak("RimWorldAccess.Compat.Vf.RoutePlannerWaypointAddedWithEta".Loc(countAfter, timeString), SpeechPriority.Normal);
                    }
                    else
                    {
                        TolkHelper.Speak("RimWorldAccess.Route.WaypointAddedStarting".Loc(countAfter), SpeechPriority.Normal);
                    }
                }
                // If the count didn't change, TryAddWaypoint already spoke the reject via VF's own Messages.Message pipeline.
            }
            catch (Exception ex)
            {
                ModLogger.Error($"VfRoutePlannerCompat.AddWaypoint failed: {ex.Message}");
            }
        }

        /// <summary>Finds the waypoint at the cursor tile via VF's own MostRecentWaypointAt, then TryRemoveWaypoint (vehicle A -- self-gates and speaks rejects).</summary>
        public static void RemoveWaypointAtCursor()
        {
            object planner = Planner();
            if (planner == null)
                return;
            try
            {
                PlanetTile currentTile = WorldNavigationState.CurrentSelectedTile;
                if (!GuardHelper.RequireValidTile(currentTile, SpeechPriority.High))
                    return;

                object wp = mostRecentAtMethod.Invoke(planner, new object[] { currentTile.tileId });
                if (wp == null)
                {
                    TolkHelper.Speak("RimWorldAccess.Route.NoWaypointToRemove".Loc(), SpeechPriority.Normal);
                    return;
                }

                int waypointIndex = IndexOfWaypoint(planner, wp);
                int countBefore = WaypointCount;
                tryRemoveMethod.Invoke(planner, new object[] { wp, true });
                int countAfter = WaypointCount;

                if (countAfter < countBefore)
                {
                    if (countAfter >= 2)
                    {
                        string timeString = EtaTicksToLastWaypoint(planner).ToStringTicksToDays("0.#");
                        TolkHelper.Speak("RimWorldAccess.Compat.Vf.RoutePlannerWaypointRemovedWithEta".Loc(waypointIndex + 1, countAfter, timeString), SpeechPriority.Normal);
                    }
                    else
                    {
                        TolkHelper.Speak("RimWorldAccess.Route.WaypointRemoved".Loc(waypointIndex + 1, countAfter), SpeechPriority.Normal);
                    }
                }
                // If the count didn't change, TryRemoveWaypoint already spoke the reject via VF's own Messages.Message pipeline.
            }
            catch (Exception ex)
            {
                ModLogger.Error($"VfRoutePlannerCompat.RemoveWaypointAtCursor failed: {ex.Message}");
            }
        }

        public static void AnnounceETA()
        {
            object planner = Planner();
            if (planner == null)
                return;
            try
            {
                if (WaypointCount < 2)
                {
                    TolkHelper.SpeakData("RoutePlannerAddTwoOrMoreWaypoints".Translate(), SpeechPriority.Normal);
                    return;
                }

                string timeString = EtaTicksToLastWaypoint(planner).ToStringTicksToDays("0.#");
                TolkHelper.Speak("RimWorldAccess.Compat.Vf.RoutePlannerEta".Loc(timeString), SpeechPriority.Normal);
            }
            catch (Exception ex)
            {
                ModLogger.Error($"VfRoutePlannerCompat.AnnounceETA failed: {ex.Message}");
            }
        }

        public static void AnnounceRouteSummary()
        {
            object planner = Planner();
            if (planner == null)
                return;
            try
            {
                int count = WaypointCount;
                if (count == 0)
                {
                    TolkHelper.Speak("RimWorldAccess.Route.NoWaypointsSet".Loc(), SpeechPriority.Normal);
                    return;
                }

                if (count == 1)
                {
                    IList list = waypointsField.GetValue(planner) as IList;
                    object firstWaypoint = list != null && list.Count > 0 ? list[0] : null;
                    PlanetTile firstTile = firstWaypoint != null ? (PlanetTile)waypointTileGetter.Invoke(firstWaypoint, null) : PlanetTile.Invalid;
                    string tileName = WorldInfoHelper.GetTileSummary(firstTile);
                    TolkHelper.Speak("RimWorldAccess.Route.StartingPoint".Loc(tileName), SpeechPriority.Normal);
                    return;
                }

                string timeString = EtaTicksToLastWaypoint(planner).ToStringTicksToDays("0.#");
                string totalEta = (string)"RoutePlannerEstTimeToFinalDest".Translate(timeString);
                TolkHelper.Speak("RimWorldAccess.Compat.Vf.RoutePlannerSummary".Loc(count, totalEta), SpeechPriority.Normal);
            }
            catch (Exception ex)
            {
                ModLogger.Error($"VfRoutePlannerCompat.AnnounceRouteSummary failed: {ex.Message}");
            }
        }

        /// <summary>Mirrors the caravaning branch of VF's own DoChooseRouteButton: fires the onChooseRouteCallback with the second waypoint's tile, then stops the planner. Standalone (non-caravaning) Enter just reads the route summary.</summary>
        public static void ConfirmRoute()
        {
            object planner = Planner();
            if (planner == null)
                return;
            try
            {
                if (!IsCaravaning)
                {
                    AnnounceRouteSummary();
                    return;
                }

                if (WaypointCount < 2)
                {
                    TolkHelper.SpeakData("RoutePlannerAddOneOrMoreWaypoints".Translate(), SpeechPriority.High);
                    return;
                }

                IList list = waypointsField.GetValue(planner) as IList;
                object secondWaypoint = list != null && list.Count > 1 ? list[1] : null;
                PlanetTile destination = secondWaypoint != null ? (PlanetTile)waypointTileGetter.Invoke(secondWaypoint, null) : PlanetTile.Invalid;

                object callback = onChooseRouteField.GetValue(planner);
                (callback as Action<PlanetTile>)?.Invoke(destination);

                string destName = WorldInfoHelper.GetTileSummary(destination, includeRouteInfo: false);
                TolkHelper.Speak("RimWorldAccess.Route.RouteConfirmed".Loc(destName), SpeechPriority.Normal);

                stopMethod.Invoke(planner, null);
            }
            catch (Exception ex)
            {
                ModLogger.Error($"VfRoutePlannerCompat.ConfirmRoute failed: {ex.Message}");
            }
        }

        public static void Cancel()
        {
            object planner = Planner();
            if (planner == null)
                return;
            try
            {
                stopMethod.Invoke(planner, null);
                TolkHelper.Speak("RimWorldAccess.Route.Closed".Loc(), SpeechPriority.Normal);
            }
            catch (Exception ex)
            {
                ModLogger.Error($"VfRoutePlannerCompat.Cancel failed: {ex.Message}");
            }
        }

        /// <summary>Vehicle A: constructs and opens VF's own Dialog_VehicleSelector, mirroring DoRoutePlannerButton's inactive branch. The selector's own Start button starts the planner (self-gated on NoVehiclesSelected).</summary>
        public static void OpenSelectorDialog()
        {
            if (!ready)
                return;
            try
            {
                Window window = Activator.CreateInstance(selectorType) as Window;
                if (window == null)
                    return;
                Find.WindowStack.Add(window);
            }
            catch (Exception ex)
            {
                ModLogger.Error($"VfRoutePlannerCompat.OpenSelectorDialog failed: {ex.Message}");
            }
        }

        // ------------------------------------------------------------------
        // Shell focus-scope routers.
        // ------------------------------------------------------------------

        private static bool EnsureLive()
        {
            if (!IsActive)
                return false;
            if (CaravanFormationState.IsActive || SplitCaravanState.IsActive)
                return false;
            return true;
        }

        public static void HandleAddWaypoint()
        {
            if (!EnsureLive())
                return;
            AddWaypoint();
        }

        public static void HandleRemoveWaypoint()
        {
            if (!EnsureLive())
                return;
            RemoveWaypointAtCursor();
        }

        public static void HandleAnnounceETA()
        {
            if (!EnsureLive())
                return;
            AnnounceETA();
        }

        public static void HandleEnterKey()
        {
            if (!EnsureLive())
                return;
            ConfirmRoute();
        }

        public static void HandleCancel()
        {
            if (!EnsureLive())
                return;
            Cancel();
        }

        // ------------------------------------------------------------------
        // Helpers.
        // ------------------------------------------------------------------

        private static int IndexOfWaypoint(object planner, object waypoint)
        {
            IList list = waypointsField.GetValue(planner) as IList;
            if (list == null)
                return -1;
            for (int i = 0; i < list.Count; i++)
            {
                if (ReferenceEquals(list[i], waypoint))
                    return i;
            }
            return -1;
        }

        /// <summary>The VF equivalent of vanilla's GetTicksToWaypoint(count - 1): the last entry of cachedTicksToWaypoint.</summary>
        private static int EtaTicksToLastWaypoint(object planner)
        {
            IList list = cachedTicksField.GetValue(planner) as IList;
            if (list == null || list.Count == 0)
                return 0;
            return (int)list[list.Count - 1];
        }
    }
}
