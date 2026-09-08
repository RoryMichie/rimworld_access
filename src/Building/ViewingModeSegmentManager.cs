using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using RimWorld;
using UnityEngine;
using Verse;

namespace RimWorldAccess
{
    /// <summary>Segment-stack management and undo for ViewingModeState, across every designator type.</summary>
    public static class ViewingModeSegmentManager
    {
        #region Segment Removal

        /// <summary>
        /// Removes one segment's items — blueprints, zone cells, area cells or designations — or
        /// every segment when <paramref name="segmentIndex"/> is -1. Returns the number removed.
        /// </summary>
        public static int RemoveSegmentItems(
            int segmentIndex,
            List<List<Thing>> segments,
            List<List<IntVec3>> cellSegments,
            bool isBuildDesignator,
            bool isZoneDesignator,
            bool isAreaDesignator,
            bool isBuiltInAreaDesignator,
            Designator activeDesignator,
            Zone targetZone)
        {
            int removedCount = 0;
            Map map = Find.CurrentMap;

            // Areas first: they undo through AreaUndoTracker rather than segments, allowed and
            // built-in areas alike.
            if (isAreaDesignator || isBuiltInAreaDesignator)
            {
                removedCount = RemoveAreaSegmentItems(map);
                return removedCount;
            }

            if (isBuildDesignator)
            {
                removedCount = RemoveBuildSegmentItems(segmentIndex, segments);
            }
            else if (isZoneDesignator)
            {
                removedCount = RemoveZoneSegmentItems(segmentIndex, cellSegments, map, targetZone);
            }
            else if (activeDesignator is Designator_Plan_Add)
            {
                // Plan cells live in the PlanManager, not the DesignationManager, so undo takes the
                // placed cells out of whatever plan now occupies them; Plan.RemoveCell deregisters
                // a plan once its last cell is gone.
                removedCount = RemovePlanSegmentItems(segmentIndex, cellSegments, map);
            }
            else
            {
                removedCount = RemoveDesignationSegmentItems(segmentIndex, cellSegments, map, activeDesignator);
            }

            return removedCount;
        }

        /// <summary>Removes blueprint items from Build designator segments.</summary>
        private static int RemoveBuildSegmentItems(int segmentIndex, List<List<Thing>> segments)
        {
            int removedCount = 0;

            if (segmentIndex >= 0 && segmentIndex < segments.Count)
            {
                var segment = segments[segmentIndex];
                foreach (Thing blueprint in segment)
                {
                    if (blueprint != null && !blueprint.Destroyed)
                    {
                        blueprint.Destroy(DestroyMode.Cancel);
                        removedCount++;
                    }
                }
            }
            else if (segmentIndex == -1)
            {
                foreach (var segment in segments)
                {
                    foreach (Thing blueprint in segment)
                    {
                        if (blueprint != null && !blueprint.Destroyed)
                        {
                            blueprint.Destroy(DestroyMode.Cancel);
                            removedCount++;
                        }
                    }
                }
            }

            return removedCount;
        }

        /// <summary>Removes zone cells through ZoneUndoTracker, restoring the previous state.</summary>
        private static int RemoveZoneSegmentItems(
            int segmentIndex,
            List<List<IntVec3>> cellSegments,
            Map map,
            Zone targetZone)
        {
            int removedCount = 0;

            if (map != null)
            {
                if (segmentIndex >= 0 && segmentIndex < cellSegments.Count)
                {
                    // The cell segment's own count is what was actually placed, which covers both a
                    // new zone and an expansion, where a zone diff would be wrong.
                    removedCount = cellSegments[segmentIndex].Count;

                    ZoneUndoTracker.UndoLastSegment(map);
                }
                else if (segmentIndex == -1)
                {
                    foreach (var segment in cellSegments)
                    {
                        removedCount += segment.Count;
                    }

                    ZoneUndoTracker.UndoAll(map);
                }
            }

            return removedCount;
        }

        /// <summary>
        /// Takes this placement's plan cells back out of the PlanManager, as the plan-remove tool
        /// does but scoped to exactly the cells it added.
        /// </summary>
        private static int RemovePlanSegmentItems(
            int segmentIndex,
            List<List<IntVec3>> cellSegments,
            Map map)
        {
            if (map?.planManager == null)
                return 0;

            int removedCount = 0;
            if (segmentIndex >= 0 && segmentIndex < cellSegments.Count)
            {
                removedCount = RemovePlanCells(cellSegments[segmentIndex], map);
            }
            else if (segmentIndex == -1)
            {
                foreach (var segment in cellSegments)
                    removedCount += RemovePlanCells(segment, map);
            }
            return removedCount;
        }

        private static int RemovePlanCells(List<IntVec3> cells, Map map)
        {
            int removedCount = 0;
            foreach (var cell in cells)
            {
                var plan = map.planManager.PlanAt(cell);
                if (plan != null)
                {
                    plan.RemoveCell(cell);
                    removedCount++;
                }
            }
            return removedCount;
        }

        /// <summary>Removes the last segment of area cell changes through AreaUndoTracker.</summary>
        private static int RemoveAreaSegmentItems(Map map)
        {
            if (!AreaUndoTracker.HasUndoData)
                return 0;

            return AreaUndoTracker.Undo();
        }

        /// <summary>
        /// Removes Orders/Cells designations through OrderUndoTracker, which removes real
        /// Designation objects and so handles thing-based and cell-based designators alike.
        /// </summary>
        private static int RemoveDesignationSegmentItems(
            int segmentIndex,
            List<List<IntVec3>> cellSegments,
            Map map,
            Designator activeDesignator)
        {
            if (map == null)
                return 0;

            if (segmentIndex >= 0)
            {
                return OrderUndoTracker.UndoLastSegment(map);
            }
            else if (segmentIndex == -1)
            {
                return OrderUndoTracker.UndoAll(map);
            }

            return 0;
        }

        /// <summary>Removes the designation at one cell, for undoing Orders/Cells designations.</summary>
        public static bool RemoveDesignationAtCell(IntVec3 cell, Map map, Designator activeDesignator)
        {
            if (map?.designationManager == null)
                return false;

            DesignationDef designationDef = null;

            // Most designator types expose their def only through a protected property.
            var designationProperty = activeDesignator?.GetType().GetProperty("Designation",
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (designationProperty != null)
            {
                designationDef = designationProperty.GetValue(activeDesignator) as DesignationDef;
            }

            if (designationDef != null)
            {
                Designation existing = map.designationManager.DesignationAt(cell, designationDef);
                if (existing != null)
                {
                    map.designationManager.RemoveDesignation(existing);
                    return true;
                }
            }
            else
            {
                // Fallback for a designator with no readable def: less precise, but not nothing.
                List<Designation> designations = new List<Designation>(map.designationManager.AllDesignationsAt(cell));
                foreach (var designation in designations)
                {
                    map.designationManager.RemoveDesignation(designation);
                }
                return designations.Count > 0;
            }

            return false;
        }

        #endregion

        #region Item Type Helpers

        /// <summary>The item-type word for a count, singular at one and plural otherwise.</summary>
        public static string GetItemTypeForCount(int count, bool isBuildDesignator, bool isZoneDesignator)
        {
            if (isBuildDesignator)
                return (count == 1 ? "RimWorldAccess.Building.View.ItemTypeBlueprintOne" : "RimWorldAccess.Building.View.ItemTypeBlueprintMany").Translate();
            else if (isZoneDesignator)
                return (count == 1 ? "RimWorldAccess.Building.View.ItemTypeZoneCellOne" : "RimWorldAccess.Building.View.ItemTypeZoneCellMany").Translate();
            else
                return (count == 1 ? "RimWorldAccess.Building.View.ItemTypeDesignationOne" : "RimWorldAccess.Building.View.ItemTypeDesignationMany").Translate();
        }

        #endregion

        #region Segment Stack Queries

        /// <summary>The total number of placed items across all segments.</summary>
        public static int GetTotalPlacedCount(
            List<List<Thing>> segments,
            List<List<IntVec3>> cellSegments,
            bool isBuildDesignator)
        {
            if (isBuildDesignator)
            {
                int count = 0;
                foreach (var segment in segments)
                {
                    count += segment.Count;
                }
                return count;
            }
            else
            {
                int count = 0;
                foreach (var segment in cellSegments)
                {
                    count += segment.Count;
                }
                return count;
            }
        }

        /// <summary>Every placed blueprint across all segments; meaningful only for Build designators.</summary>
        public static List<Thing> GetAllPlacedBlueprints(List<List<Thing>> segments)
        {
            var all = new List<Thing>();
            foreach (var segment in segments)
            {
                all.AddRange(segment);
            }
            return all;
        }

        /// <summary>Every placed cell across all segments, for the non-Build designators.</summary>
        public static List<IntVec3> GetAllPlacedCells(List<List<IntVec3>> cellSegments)
        {
            var all = new List<IntVec3>();
            foreach (var segment in cellSegments)
            {
                all.AddRange(segment);
            }
            return all;
        }

        /// <summary>The number of segments in the stack.</summary>
        public static int GetSegmentCount(
            List<List<Thing>> segments,
            List<List<IntVec3>> cellSegments,
            bool isBuildDesignator)
        {
            return isBuildDesignator ? segments.Count : cellSegments.Count;
        }

        #endregion
    }
}
