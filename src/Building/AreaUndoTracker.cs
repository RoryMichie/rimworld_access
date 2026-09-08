using System.Collections.Generic;
using System.Linq;
using RimWorld;
using Verse;

namespace RimWorldAccess
{
    /// <summary>
    /// Tracks area cell changes for undo support.
    /// Captures state before painting, then allows reverting to that state.
    /// A single stroke can touch more than one area (Designator_AreaIgnoreRoof clears both
    /// the BuildRoof and NoRoof grids), so every tracked area gets its own snapshot pair.
    /// </summary>
    public static class AreaUndoTracker
    {
        private sealed class AreaSnapshot
        {
            public readonly Area Area;
            public readonly HashSet<IntVec3> CellsBeforePaint = new HashSet<IntVec3>();
            public readonly HashSet<IntVec3> CellsAfterPaint = new HashSet<IntVec3>();

            public AreaSnapshot(Area area)
            {
                Area = area;
            }
        }

        private static readonly List<AreaSnapshot> snapshots = new List<AreaSnapshot>();
        private static bool hasUndoData = false;
        private static bool wasExpanding = true;  // true = expand, false = clear/shrink

        public static bool HasUndoData => hasUndoData;

        /// <summary>
        /// Captures the area state before painting begins.
        /// Call this BEFORE applying any cell changes.
        /// </summary>
        public static void CaptureBeforeState(Area area, bool expanding)
        {
            if (area == null)
                return;

            CaptureBeforeState(new List<Area> { area }, expanding);
        }

        /// <summary>
        /// Captures the state of every area a stroke will touch, before painting begins.
        /// Call this BEFORE applying any cell changes.
        /// </summary>
        public static void CaptureBeforeState(IReadOnlyList<Area> areas, bool expanding)
        {
            if (areas == null)
                return;

            var captured = new List<AreaSnapshot>();
            foreach (Area area in areas)
            {
                if (area != null)
                    captured.Add(new AreaSnapshot(area));
            }

            if (captured.Count == 0)
                return;

            snapshots.Clear();
            snapshots.AddRange(captured);
            wasExpanding = expanding;

            // Capture all cells currently in each area
            foreach (AreaSnapshot snapshot in snapshots)
            {
                Map map = snapshot.Area.Map;
                if (map == null)
                    continue;

                foreach (IntVec3 cell in map.AllCells)
                {
                    if (snapshot.Area[cell])
                        snapshot.CellsBeforePaint.Add(cell);
                }
            }

            hasUndoData = false;
        }

        /// <summary>
        /// Captures the area state after painting is complete.
        /// Call this AFTER applying cell changes.
        /// </summary>
        public static void CaptureAfterState()
        {
            if (snapshots.Count == 0)
                return;

            bool anyChanged = false;

            foreach (AreaSnapshot snapshot in snapshots)
            {
                snapshot.CellsAfterPaint.Clear();

                Map map = snapshot.Area.Map;
                if (map != null)
                {
                    foreach (IntVec3 cell in map.AllCells)
                    {
                        if (snapshot.Area[cell])
                            snapshot.CellsAfterPaint.Add(cell);
                    }
                }

                if (!snapshot.CellsBeforePaint.SetEquals(snapshot.CellsAfterPaint))
                    anyChanged = true;
            }

            // Only mark as having undo data if something actually changed
            hasUndoData = anyChanged;
        }

        /// <summary>
        /// Undoes the last area change by restoring the before state.
        /// </summary>
        /// <returns>Number of cells restored across every tracked area</returns>
        public static int Undo()
        {
            if (!hasUndoData || snapshots.Count == 0)
                return 0;

            int changedCount = 0;

            foreach (AreaSnapshot snapshot in snapshots)
            {
                if (wasExpanding)
                {
                    // We were expanding - find cells that were added and remove them
                    var addedCells = snapshot.CellsAfterPaint.Except(snapshot.CellsBeforePaint).ToList();
                    foreach (IntVec3 cell in addedCells)
                    {
                        snapshot.Area[cell] = false;
                        changedCount++;
                    }
                }
                else
                {
                    // We were clearing/shrinking - find cells that were removed and add them back
                    var removedCells = snapshot.CellsBeforePaint.Except(snapshot.CellsAfterPaint).ToList();
                    foreach (IntVec3 cell in removedCells)
                    {
                        snapshot.Area[cell] = true;
                        changedCount++;
                    }
                }
            }

            hasUndoData = false;
            return changedCount;
        }

        /// <summary>
        /// Gets a description of what was changed for announcements.
        /// </summary>
        public static string GetChangeDescription()
        {
            if (snapshots.Count == 0)
                return "area";

            if (snapshots.Count == 1)
                return snapshots[0].Area.Label;

            List<string> labels = snapshots.Select(snapshot => snapshot.Area.Label).ToList();
            return (string)"RimWorldAccess.Building.AreaUndo.MultipleAreas".Translate(labels.ToCommaList(true));
        }

        /// <summary>
        /// Clears all undo data.
        /// </summary>
        public static void Clear()
        {
            snapshots.Clear();
            hasUndoData = false;
        }
    }
}
