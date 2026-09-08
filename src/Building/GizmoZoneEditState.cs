using System.Collections.Generic;
using RimWorld;
using Verse;

namespace RimWorldAccess
{
    /// <summary>
    /// State management for zone editing when working from gizmos (not architect menu).
    /// Tracks the target zone, original cells, and modifications to enable proper
    /// cell toggling with connectivity checks and adjacency enforcement.
    ///
    /// This is used when:
    /// 1. User selects expand/shrink from a zone's gizmo
    /// 2. User enters manual mode (Shift+Tab) for cell-by-cell editing
    /// 3. User presses Space to toggle cells
    /// </summary>
    public static class GizmoZoneEditState
    {
        private static bool isActive = false;
        private static Zone targetZone = null;
        private static HashSet<IntVec3> originalZoneCells = new HashSet<IntVec3>();
        private static HashSet<Zone> createdZones = new HashSet<Zone>();
        private static bool isDeleteDesignator = false;
        private static Designator activeDesignator = null;

        #region Properties

        /// <summary>
        /// Whether gizmo zone edit state is active.
        /// </summary>
        public static bool IsActive => isActive;

        /// <summary>
        /// The zone being edited.
        /// </summary>
        public static Zone TargetZone => targetZone;

        #endregion

        #region State Management

        /// <summary>How the zone under edit was found, which decides what the first Space does.</summary>
        private enum ZoneTargetSource
        {
            None,
            Ambiguous,
            Selection,
            Cursor,
            Adjacent,
        }

        /// <summary>
        /// Finds the zone this designator acts on: the selected zone (a gizmo's own zone), else the
        /// zone under the cursor, else the single zone the cursor cell touches. Zone growth is
        /// adjacency-bound, so a lone neighbour is the only zone an empty cell can join.
        /// </summary>
        private static ZoneTargetSource ResolveTargetZone(Designator designator, out Zone zone)
        {
            zone = Find.Selector?.SelectedZone;
            if (zone != null)
                return ZoneTargetSource.Selection;

            Map map = Find.CurrentMap;
            if (map?.zoneManager == null)
                return ZoneTargetSource.None;

            IntVec3 cursorPos = MapNavigationState.CurrentCursorPosition;
            System.Type zoneTypeToPlace = ZoneSelectionHelper.GetZoneTypeToPlace(designator);

            zone = map.zoneManager.ZoneAt(cursorPos);
            if (zone != null)
            {
                // A zone of another type cannot take this cell, so it is no target.
                if (zoneTypeToPlace != null && zone.GetType() != zoneTypeToPlace)
                {
                    zone = null;
                    return ZoneTargetSource.None;
                }
                return ZoneTargetSource.Cursor;
            }

            bool ambiguous;
            zone = ZoneSelectionHelper.FindAdjacentZone(designator, cursorPos, out ambiguous);
            if (zone != null)
                return ZoneTargetSource.Adjacent;

            return ambiguous ? ZoneTargetSource.Ambiguous : ZoneTargetSource.None;
        }

        /// <summary>
        /// Initializes state for editing <paramref name="zone"/> with this designator.
        /// </summary>
        private static void Initialize(Designator designator, Zone zone)
        {
            activeDesignator = designator;
            isDeleteDesignator = ShapeHelper.IsDeleteDesignator(designator);
            targetZone = zone;

            // Original cells are what expansion may not take back and what shrinking may restore.
            originalZoneCells.Clear();
            foreach (IntVec3 cell in zone.Cells)
            {
                originalZoneCells.Add(cell);
            }

            createdZones.Clear();
            createdZones.Add(zone);

            isActive = true;

            ModLogger.Dev($"[GizmoZoneEditState] Initialized for {zone.label}, {originalZoneCells.Count} original cells, isDelete={isDeleteDesignator}");
        }

        /// <summary>
        /// Resets all state.
        /// </summary>
        public static void Reset()
        {
            isActive = false;
            targetZone = null;
            originalZoneCells.Clear();
            createdZones.Clear();
            isDeleteDesignator = false;
            activeDesignator = null;
        }

        #endregion

        #region Zone Cell Operations

        /// <summary>
        /// Toggles a zone cell at the current cursor position, adopting a target zone first when
        /// none is being edited yet. Uses the same logic as ViewingModeState via ZoneEditingHelper.
        /// </summary>
        public static void ToggleZoneCellAtCursor(Designator designator)
        {
            if (!isActive || activeDesignator != designator)
            {
                Zone resolved;
                ZoneTargetSource source = ResolveTargetZone(designator, out resolved);

                if (source == ZoneTargetSource.Ambiguous)
                {
                    TolkHelper.Speak("RimWorldAccess.Building.Zone.AdjacentZonesAmbiguous".Loc(), SpeechPriority.Normal);
                    return;
                }

                if (source == ZoneTargetSource.None)
                {
                    TolkHelper.Speak("RimWorldAccess.Building.Zone.NoZoneBeingEdited".Loc(), SpeechPriority.Normal);
                    return;
                }

                Initialize(designator, resolved);

                // Vanilla's own single-cell click on a zone cell only targets that zone
                // (Designator_ZoneAdd.DesignateMultiCell), and an expansion may not take that cell
                // back anyway, so the press stops at naming its new target.
                if (source == ZoneTargetSource.Cursor && !isDeleteDesignator)
                {
                    string adopted = resolved.label ?? (string)"RimWorldAccess.Building.Zone.FallbackName".Translate();
                    TolkHelper.SpeakData("RimWorldAccess.Building.Zone.NowExpanding".Translate(adopted), SpeechPriority.Normal);
                    return;
                }
            }

            // Use ZoneEditingHelper for consistent behavior with viewing mode
            var result = ZoneEditingHelper.ToggleZoneCellAtCursor(
                ref targetZone,
                activeDesignator,
                createdZones,
                originalZoneCells,
                isDeleteDesignator);

            TolkHelper.SpeakData(result.Message, result.Priority);

            if (result.Success)
            {
                IntVec3 cursorPos = MapNavigationState.CurrentCursorPosition;
                if (result.ZoneDeleted)
                {
                    ModLogger.Dev($"[GizmoZoneEditState] Zone cell operation at {cursorPos}: zone was deleted");
                    // Zone was deleted - reset state
                    Reset();
                }
                else
                {
                    ModLogger.Dev($"[GizmoZoneEditState] Zone cell operation at {cursorPos}: {result.Message}");
                }
            }
        }

        #endregion
    }
}
