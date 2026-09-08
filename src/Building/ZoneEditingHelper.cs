using System.Collections.Generic;
using System.Linq;
using RimWorld;
using Verse;

namespace RimWorldAccess
{
    /// <summary>
    /// Zone manipulation and region detection for ViewingModeState: toggling zone cells, checking
    /// connectivity, detecting disconnected regions, and keeping zone references fresh.
    /// </summary>
    public static class ZoneEditingHelper
    {
        #region Zone Cell Operations

        /// <summary>
        /// Toggles the zone cell under the cursor: a cell in a zone is removed (refused if that
        /// would split the zone), a cell outside one is added to <paramref name="targetZone"/>.
        /// Adds go only adjacent to the target, so no new zone is ever created here.
        /// </summary>
        public static ZoneEditResult ToggleZoneCellAtCursor(
            ref Zone targetZone,
            Designator activeDesignator,
            HashSet<Zone> createdZones,
            HashSet<IntVec3> originalZoneCells,
            bool isDeleteDesignator)
        {
            Map map = Find.CurrentMap;
            if (map == null)
            {
                return new ZoneEditResult(false, "RimWorldAccess.Guard.NoMapLoaded".Translate(), SpeechPriority.High);
            }

            IntVec3 cursorPos = MapNavigationState.CurrentCursorPosition;
            Zone zoneAtCursor = map.zoneManager.ZoneAt(cursorPos);

            if (zoneAtCursor != null)
            {
                return TryRemoveZoneCell(zoneAtCursor, cursorPos, map, ref targetZone, createdZones, originalZoneCells, isDeleteDesignator);
            }
            else
            {
                return TryAddZoneCell(cursorPos, map, targetZone, activeDesignator, createdZones, originalZoneCells, isDeleteDesignator);
            }
        }

        /// <summary>Removes a cell from a zone, refusing a disconnect and, in expand mode, any cell that predates the expansion.</summary>
        private static ZoneEditResult TryRemoveZoneCell(
            Zone zoneAtCursor,
            IntVec3 cursorPos,
            Map map,
            ref Zone targetZone,
            HashSet<Zone> createdZones,
            HashSet<IntVec3> originalZoneCells,
            bool isDeleteDesignator)
        {
            // Expand mode may only take back cells added during this session.
            if (!isDeleteDesignator && originalZoneCells != null && originalZoneCells.Contains(cursorPos))
            {
                return new ZoneEditResult(false, "RimWorldAccess.Building.Zone.CannotRemoveOriginalCell".Translate(), SpeechPriority.Normal);
            }

            if (WouldDisconnectZone(zoneAtCursor, cursorPos, map))
            {
                return new ZoneEditResult(false, "RimWorldAccess.Building.Zone.WouldDisconnect".Translate(), SpeechPriority.Normal);
            }

            try
            {
                string zoneName = zoneAtCursor.label ?? (string)"RimWorldAccess.Building.Zone.FallbackName".Translate();
                zoneAtCursor.RemoveCell(cursorPos);

                // RimWorld deletes a zone once its last cell goes.
                bool zoneStillExists = map.zoneManager.AllZones.Contains(zoneAtCursor);

                if (zoneStillExists)
                {
                    if (!createdZones.Contains(zoneAtCursor))
                    {
                        createdZones.Add(zoneAtCursor);
                    }
                    return new ZoneEditResult(true, "RimWorldAccess.Building.Zone.CellRemoved".Translate(zoneName), SpeechPriority.Normal);
                }
                else
                {
                    createdZones.Remove(zoneAtCursor);

                    if (targetZone == zoneAtCursor)
                    {
                        targetZone = createdZones.FirstOrDefault();
                    }

                    return new ZoneEditResult(true, "RimWorldAccess.Building.Zone.ZoneDeleted".Translate(zoneName), SpeechPriority.Normal, zoneDeleted: true);
                }
            }
            catch (System.Exception ex)
            {
                Log.Error($"[ZoneEditingHelper] Error removing zone cell: {ex.Message}");
                return new ZoneEditResult(false, "RimWorldAccess.Building.Zone.FailedToRemoveCell".Translate(), SpeechPriority.Normal);
            }
        }

        /// <summary>Adds a cell to the target zone, honoring the mode's own eligibility rules.</summary>
        private static ZoneEditResult TryAddZoneCell(
            IntVec3 cursorPos,
            Map map,
            Zone targetZone,
            Designator activeDesignator,
            HashSet<Zone> createdZones,
            HashSet<IntVec3> originalZoneCells,
            bool isDeleteDesignator)
        {
            if (targetZone == null)
            {
                return new ZoneEditResult(false, "RimWorldAccess.Building.Zone.NoZoneSelectedForEditing".Translate(), SpeechPriority.Normal);
            }

            Zone existingZone = map.zoneManager.ZoneAt(cursorPos);
            if (existingZone != null)
            {
                if (existingZone == targetZone)
                    return new ZoneEditResult(false, "RimWorldAccess.Building.Zone.CellAlreadyInThisZone".Translate(), SpeechPriority.Normal);
                else
                    return new ZoneEditResult(false, "RimWorldAccess.Building.Zone.CellInOtherZone".Translate(existingZone.label), SpeechPriority.Normal);
            }

            // Shrink mode may only re-add cells the zone originally held, and its designator is
            // Designator_ZoneDelete, so Designator_ZoneAdd's validation is unavailable here.
            if (isDeleteDesignator)
            {
                if (!originalZoneCells.Contains(cursorPos))
                {
                    return new ZoneEditResult(false, "RimWorldAccess.Building.Zone.CellNotPartOfOriginal".Translate(), SpeechPriority.Normal);
                }

                // Adjacency stops a disconnected original cell coming back on its own.
                bool isAdjacentForShrink = false;
                for (int i = 0; i < 4; i++)
                {
                    IntVec3 neighbor = cursorPos + GenAdj.CardinalDirections[i];
                    if (neighbor.InBounds(map) && map.zoneManager.ZoneAt(neighbor) == targetZone)
                    {
                        isAdjacentForShrink = true;
                        break;
                    }
                }
                if (!isAdjacentForShrink)
                {
                    return new ZoneEditResult(false, "RimWorldAccess.Building.Zone.CellMustBeAdjacent".Translate(), SpeechPriority.Normal);
                }

                try
                {
                    targetZone.AddCell(cursorPos);

                    if (!createdZones.Contains(targetZone))
                    {
                        createdZones.Add(targetZone);
                    }

                    string zoneName = targetZone.label ?? (string)"RimWorldAccess.Building.Zone.FallbackName".Translate();
                    return new ZoneEditResult(true, "RimWorldAccess.Building.Zone.CellAdded".Translate(zoneName), SpeechPriority.Normal);
                }
                catch (System.Exception ex)
                {
                    Log.Error($"[ZoneEditingHelper] Error adding zone cell in shrink mode: {ex.Message}");
                    return new ZoneEditResult(false, "RimWorldAccess.Building.Zone.FailedToAddCell".Translate(), SpeechPriority.Normal);
                }
            }

            var zoneDesignator = activeDesignator as Designator_ZoneAdd;
            if (zoneDesignator == null)
            {
                return new ZoneEditResult(false, "RimWorldAccess.Building.Zone.NoZoneAtLocation".Translate(), SpeechPriority.Normal);
            }

            // Zone-type requirements: soil fertility for growing zones, and so on.
            AcceptanceReport report = zoneDesignator.CanDesignateCell(cursorPos);
            if (!report.Accepted)
            {
                string reason = !string.IsNullOrEmpty(report.Reason) ? report.Reason : (string)"RimWorldAccess.Building.Zone.CannotAddCellHere".Translate();
                return new ZoneEditResult(false, reason, SpeechPriority.Normal);
            }

            // Contiguity: the cell must be cardinally adjacent to targetZone.
            bool isAdjacent = false;
            for (int i = 0; i < 4; i++)
            {
                IntVec3 neighbor = cursorPos + GenAdj.CardinalDirections[i];
                if (neighbor.InBounds(map) && map.zoneManager.ZoneAt(neighbor) == targetZone)
                {
                    isAdjacent = true;
                    break;
                }
            }

            if (!isAdjacent)
            {
                return new ZoneEditResult(false, "RimWorldAccess.Building.Zone.CellMustBeAdjacent".Translate(), SpeechPriority.Normal);
            }

            try
            {
                targetZone.AddCell(cursorPos);

                if (!createdZones.Contains(targetZone))
                {
                    createdZones.Add(targetZone);
                }

                string zoneName = targetZone.label ?? (string)"RimWorldAccess.Building.Zone.FallbackName".Translate();
                return new ZoneEditResult(true, "RimWorldAccess.Building.Zone.CellAdded".Translate(zoneName), SpeechPriority.Normal);
            }
            catch (System.Exception ex)
            {
                Log.Error($"[ZoneEditingHelper] Error adding zone cell: {ex.Message}");
                return new ZoneEditResult(false, "RimWorldAccess.Building.Zone.FailedToAddCell".Translate(), SpeechPriority.Normal);
            }
        }

        #endregion

        #region Connectivity Detection

        /// <summary>
        /// True when removing <paramref name="cellToRemove"/> would split the zone into
        /// unreachable parts, decided by flood-filling the remaining cells.
        /// </summary>
        public static bool WouldDisconnectZone(Zone zone, IntVec3 cellToRemove, Map map)
        {
            var remainingCells = new HashSet<IntVec3>(zone.Cells);
            remainingCells.Remove(cellToRemove);

            if (remainingCells.Count == 0)
                return false; // Removing the last cell just deletes the zone.

            var visited = new HashSet<IntVec3>();
            var queue = new Queue<IntVec3>();
            var startCell = remainingCells.First();

            queue.Enqueue(startCell);
            visited.Add(startCell);

            while (queue.Count > 0)
            {
                var current = queue.Dequeue();
                foreach (var dir in GenAdj.CardinalDirections)
                {
                    var neighbor = current + dir;
                    if (remainingCells.Contains(neighbor) && !visited.Contains(neighbor))
                    {
                        visited.Add(neighbor);
                        queue.Enqueue(neighbor);
                    }
                }
            }

            return visited.Count < remainingCells.Count;
        }

        /// <summary>
        /// Disconnected regions among a set of cells, by flood fill: 1 means a placement stays one
        /// zone, 2 or more means it will be split.
        /// </summary>
        public static int CountDisconnectedRegions(List<IntVec3> cells)
        {
            if (cells == null || cells.Count == 0)
                return 0;

            var remainingCells = new HashSet<IntVec3>(cells);
            int regionCount = 0;

            while (remainingCells.Count > 0)
            {
                regionCount++;

                IntVec3 startCell = default;
                foreach (var cell in remainingCells)
                {
                    startCell = cell;
                    break;
                }

                var queue = new Queue<IntVec3>();
                queue.Enqueue(startCell);
                remainingCells.Remove(startCell);

                while (queue.Count > 0)
                {
                    IntVec3 current = queue.Dequeue();

                    foreach (IntVec3 offset in GenAdj.CardinalDirections)
                    {
                        IntVec3 neighbor = current + offset;

                        if (remainingCells.Contains(neighbor))
                        {
                            remainingCells.Remove(neighbor);
                            queue.Enqueue(neighbor);
                        }
                    }
                }
            }

            return regionCount;
        }

        #endregion

        #region Zone Collection and Cleanup

        /// <summary>
        /// Collects the unique zones a placement created or expanded, for calling after
        /// DesignateMultiCell, and points <paramref name="targetZone"/> at the first one found.
        /// </summary>
        public static void CollectCreatedZones(List<IntVec3> placedCells, HashSet<Zone> createdZones, ref Zone targetZone)
        {
            Map map = Find.CurrentMap;
            if (map?.zoneManager == null || placedCells == null)
                return;

            foreach (IntVec3 cell in placedCells)
            {
                Zone zone = map.zoneManager.ZoneAt(cell);
                if (zone != null)
                {
                    createdZones.Add(zone);
                    if (targetZone == null)
                    {
                        targetZone = zone;
                    }
                }
            }
        }

        /// <summary>Drops references to zones RimWorld already deleted when their last cell went.</summary>
        public static void CleanupStaleZoneReferences(HashSet<Zone> createdZones)
        {
            Map map = Find.CurrentMap;
            if (map?.zoneManager == null)
                return;

            createdZones.RemoveWhere(zone => !map.zoneManager.AllZones.Contains(zone));
        }

        /// <summary>The zone at a cell, or null when there is none.</summary>
        public static Zone GetZoneAtCell(IntVec3 cell)
        {
            Map map = Find.CurrentMap;
            return map?.zoneManager?.ZoneAt(cell);
        }

        /// <summary>The cells belonging to a zone; empty for null.</summary>
        public static List<IntVec3> GetZoneCells(Zone zone)
        {
            if (zone == null)
                return new List<IntVec3>();

            return zone.Cells.ToList();
        }

        #endregion
    }

    /// <summary>Result of a zone edit operation.</summary>
    public class ZoneEditResult
    {
        public bool Success { get; }

        /// <summary>Already-resolved text to announce.</summary>
        public string Message { get; }

        public SpeechPriority Priority { get; }

        /// <summary>Whether the operation left the zone with no cells, deleting it.</summary>
        public bool ZoneDeleted { get; }

        public ZoneEditResult(bool success, string message, SpeechPriority priority, bool zoneDeleted = false)
        {
            Success = success;
            Message = message;
            Priority = priority;
            ZoneDeleted = zoneDeleted;
        }
    }
}
