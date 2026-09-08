using System.Collections.Generic;
using System.Linq;
using Verse;
using RimWorld;

namespace RimWorldAccess
{
    /// <summary>Zone state before/after one modification, for undo.</summary>
    public class ZoneUndoRecord
    {
        public Zone TargetZone;

        /// <summary>Cells in the zone BEFORE the modification</summary>
        public HashSet<IntVec3> OriginalCells;

        /// <summary>All zones that existed BEFORE the operation (to detect splits)</summary>
        public HashSet<Zone> ZonesBeforeOperation;

        /// <summary>Zones created by CheckContiguous splits (populated after CaptureAfterState)</summary>
        public HashSet<Zone> SplitCreatedZones;

        public bool IsShrinkOperation;

        public ZoneUndoRecord()
        {
            OriginalCells = new HashSet<IntVec3>();
            ZonesBeforeOperation = new HashSet<Zone>();
            SplitCreatedZones = new HashSet<Zone>();
        }
    }

    /// <summary>
    /// Tracks zone state so ViewingModeState can undo expand/shrink operations with Escape.
    /// Order of use: CaptureBeforeState before DesignateMultiCell, CaptureAfterState after it,
    /// AddSegment to commit the record as a segment, UndoLastSegment/UndoAll to restore, and Clear to
    /// discard everything on confirm.
    /// </summary>
    public static class ZoneUndoTracker
    {
        // Current working record (being built during placement)
        private static ZoneUndoRecord currentRecord = null;

        // Stack of completed segments for multi-step undo
        private static List<ZoneUndoRecord> segments = new List<ZoneUndoRecord>();

        // Pre-shrink cells, kept independently of segments (set in CaptureBeforeState, cleared only
        // by Clear) so cells can always be re-added in viewing mode.
        private static HashSet<IntVec3> preShrinkOriginalCells = null;

        // Pre-expand cells, kept the same way so cells that existed before an expansion are never
        // removed.
        private static HashSet<IntVec3> preExpandOriginalCells = null;

        /// <summary>Whether a record is being built right now.</summary>
        public static bool HasPendingRecord => currentRecord != null;

        /// <summary>Number of stored segments available for undo.</summary>
        public static int SegmentCount => segments.Count;

        /// <summary>The original cells of the last recorded segment, or null when there are none.</summary>
        public static HashSet<IntVec3> LastSegmentOriginalCells
        {
            get
            {
                if (segments.Count == 0)
                    return null;
                return segments[segments.Count - 1].OriginalCells;
            }
        }

        /// <summary>The target zone of the last recorded segment, or null when there are none.</summary>
        public static Zone LastSegmentTargetZone
        {
            get
            {
                if (segments.Count == 0)
                    return null;
                return segments[segments.Count - 1].TargetZone;
            }
        }

        /// <summary>
        /// The cells captured before a shrink, kept independently of segments so they stay re-addable
        /// in viewing mode. Null outside a shrink.
        /// </summary>
        public static HashSet<IntVec3> PreShrinkOriginalCells => preShrinkOriginalCells;

        /// <summary>
        /// The cells captured before an expand, kept independently of segments so pre-existing cells
        /// are never removed. Null outside an expand.
        /// </summary>
        public static HashSet<IntVec3> PreExpandOriginalCells => preExpandOriginalCells;

        /// <summary>Whether the most recent segment expanded an existing zone rather than creating one — true only when its cell count actually grew.</summary>
        public static bool WasZoneExpansion
        {
            get
            {
                if (segments.Count == 0)
                    return false;

                ZoneUndoRecord lastSegment = segments[segments.Count - 1];

                if (lastSegment.TargetZone == null)
                    return false;

                if (!Find.CurrentMap?.zoneManager?.AllZones?.Contains(lastSegment.TargetZone) ?? true)
                    return false;

                // Compare current cell count to original - expansion means cells were added
                int originalCount = lastSegment.OriginalCells.Count;
                int currentCount = lastSegment.TargetZone.Cells.Count();

                return currentCount > originalCount;
            }
        }

        /// <summary>Captures the zone state immediately BEFORE a DesignateMultiCell call.</summary>
        public static void CaptureBeforeState(Zone targetZone, Map map, bool isShrink)
        {
            currentRecord = new ZoneUndoRecord
            {
                TargetZone = targetZone,
                IsShrinkOperation = isShrink
            };

            if (targetZone != null)
            {
                foreach (IntVec3 cell in targetZone.Cells)
                {
                    currentRecord.OriginalCells.Add(cell);
                }

                // Shrink: kept independently of segments for re-adding cells in viewing mode.
                if (isShrink)
                {
                    preShrinkOriginalCells = new HashSet<IntVec3>(targetZone.Cells);
                }
                else
                {
                    // Expand: kept so cells that existed before the expansion are never removed.
                    preExpandOriginalCells = new HashSet<IntVec3>(targetZone.Cells);
                }
            }

            if (map?.zoneManager != null)
            {
                foreach (Zone zone in map.zoneManager.AllZones)
                {
                    currentRecord.ZonesBeforeOperation.Add(zone);
                }
            }

            ModLogger.Dev($"[ZoneUndoTracker] CaptureBeforeState: zone={targetZone?.label ?? "null"}, " +
                       $"originalCells={currentRecord.OriginalCells.Count}, " +
                       $"existingZones={currentRecord.ZonesBeforeOperation.Count}, " +
                       $"isShrink={isShrink}");
        }

        /// <summary>Captures the zone state immediately after DesignateMultiCell, detecting zones created by CheckContiguous splits.</summary>
        public static void CaptureAfterState(Map map)
        {
            if (currentRecord == null)
            {
                Log.Warning("[ZoneUndoTracker] CaptureAfterState called without CaptureBeforeState");
                return;
            }

            // Detect split-created zones by comparing before/after zone lists
            if (map?.zoneManager != null)
            {
                foreach (Zone zone in map.zoneManager.AllZones)
                {
                    if (!currentRecord.ZonesBeforeOperation.Contains(zone))
                    {
                        currentRecord.SplitCreatedZones.Add(zone);
                    }
                }
            }

            ModLogger.Dev($"[ZoneUndoTracker] CaptureAfterState: splitCreatedZones={currentRecord.SplitCreatedZones.Count}");
        }

        /// <summary>Stores the current record as a completed segment and starts fresh; called when entering ViewingModeState.</summary>
        public static void AddSegment()
        {
            if (currentRecord == null)
            {
                ModLogger.Dev("[ZoneUndoTracker] AddSegment called but no pending record");
                return;
            }

            segments.Add(currentRecord);
            ModLogger.Dev($"[ZoneUndoTracker] AddSegment: now have {segments.Count} segments");
            currentRecord = null;
        }

        /// <summary>Undoes the most recent segment; false when there are none.</summary>
        public static bool UndoLastSegment(Map map)
        {
            if (segments.Count == 0)
            {
                ModLogger.Dev("[ZoneUndoTracker] UndoLastSegment: no segments to undo");
                return false;
            }

            ZoneUndoRecord record = segments[segments.Count - 1];
            segments.RemoveAt(segments.Count - 1);

            RestoreFromRecord(record, map);
            return true;
        }

        /// <summary>Undoes all segments in reverse order and returns how many.</summary>
        public static int UndoAll(Map map)
        {
            int count = segments.Count;

            for (int i = segments.Count - 1; i >= 0; i--)
            {
                RestoreFromRecord(segments[i], map);
            }

            segments.Clear();
            ModLogger.Dev($"[ZoneUndoTracker] UndoAll: undid {count} segments");
            return count;
        }

        /// <summary>Clears all undo data; called when confirming changes.</summary>
        public static void Clear()
        {
            currentRecord = null;
            segments.Clear();
            preShrinkOriginalCells = null;
            preExpandOriginalCells = null;
            ModLogger.Dev("[ZoneUndoTracker] Cleared all undo data");
        }

        /// <summary>Whether a shrink would delete every cell of the zone — checked before applying, to warn the user.</summary>
        public static bool WouldDeleteEntireZone(Zone targetZone, IEnumerable<IntVec3> cellsToRemove)
        {
            if (targetZone == null)
                return false;

            HashSet<IntVec3> zoneCells = new HashSet<IntVec3>(targetZone.Cells);
            HashSet<IntVec3> removeSet = new HashSet<IntVec3>(cellsToRemove);

            foreach (IntVec3 cell in zoneCells)
            {
                if (!removeSet.Contains(cell))
                {
                    return false;
                }
            }

            return true;
        }

        /// <summary>Restores a zone to the state captured in the record.</summary>
        private static void RestoreFromRecord(ZoneUndoRecord record, Map map)
        {
            if (record == null || map == null)
                return;

            ModLogger.Dev($"[ZoneUndoTracker] RestoreFromRecord: zone={record.TargetZone?.label ?? "null"}, " +
                       $"originalCells={record.OriginalCells.Count}, " +
                       $"splitZones={record.SplitCreatedZones.Count}");

            // Step 1: Delete all zones created by splits
            foreach (Zone splitZone in record.SplitCreatedZones)
            {
                if (splitZone != null && map.zoneManager.AllZones.Contains(splitZone))
                {
                    ModLogger.Dev($"[ZoneUndoTracker] Deleting split zone: {splitZone.label}");
                    splitZone.Delete();
                }
            }

            // Step 2: Restore the target zone's cells
            if (record.TargetZone != null && map.zoneManager.AllZones.Contains(record.TargetZone))
            {
                Zone zone = record.TargetZone;
                HashSet<IntVec3> currentCells = new HashSet<IntVec3>(zone.Cells);

                // Remove cells that weren't in the original set
                foreach (IntVec3 cell in currentCells)
                {
                    if (!record.OriginalCells.Contains(cell))
                    {
                        zone.RemoveCell(cell);
                    }
                }

                // Add back cells that were in the original set but aren't now
                foreach (IntVec3 cell in record.OriginalCells)
                {
                    if (!zone.ContainsCell(cell) && cell.InBounds(map))
                    {
                        zone.AddCell(cell);
                    }
                }

                // Do NOT call CheckContiguous - we want exact restoration without splits
                ModLogger.Dev($"[ZoneUndoTracker] Restored zone {zone.label} to {zone.Cells.Count()} cells");
            }
            else if (record.TargetZone != null && !map.zoneManager.AllZones.Contains(record.TargetZone))
            {
                // The target zone was deleted; the full-deletion warning covers this case.
                Log.Warning($"[ZoneUndoTracker] Target zone was deleted and cannot be restored");
            }
        }
    }
}
