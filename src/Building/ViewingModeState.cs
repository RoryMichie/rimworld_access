using System.Collections.Generic;
using System.Linq;
using RimWorld;
using Verse;
using Verse.Sound;

namespace RimWorldAccess
{
    /// <summary>
    /// Review state entered after shape-based placement, for every designator kind: Build
    /// (blueprints), Orders, Zones, Areas and Cells. Placement accumulates as segments so the last
    /// one can be undone on its own.
    /// Obstacle navigation rides a temporary ScannerState category, removed on exit.
    /// </summary>
    public static class ViewingModeState
    {
        private static bool isActive = false;

        // Build designators fill segments; every other kind fills cellSegments, which undo through
        // DesignationManager rather than Thing.Destroy().
        private static List<List<Thing>> segments = new List<List<Thing>>();

        private static List<List<IntVec3>> cellSegments = new List<List<IntVec3>>();

        private static List<ShapeType> segmentShapeTypes = new List<ShapeType>();

        // Paired HashSet keeps accumulation dedup O(1); a list-only scan is O(n^2) and chokes on
        // full-map shape placements.
        private static List<IntVec3> obstacleCells = new List<IntVec3>();
        private static HashSet<IntVec3> obstacleCellsSet = new HashSet<IntVec3>();

        // Meditation focus / tree protection, accumulated across segments.
        private static int protectedCount = 0;
        private static HashSet<string> protectedByLabels = new HashSet<string>();

        private static List<Enclosure> detectedEnclosures = new List<Enclosure>();

        private static int detectedRegionCount = 0;

        private static HashSet<Zone> createdZones = new HashSet<Zone>();

        // The zone being edited; pins editing to it so no new zone is created.
        private static Zone targetZone = null;

        // targetZone's cells on entry, so shrink mode can re-add them.
        private static HashSet<IntVec3> originalZoneCells = new HashSet<IntVec3>();

        // Things an order designator targeted (Hunt, Haul, Tame).
        private static List<Thing> orderTargets = new List<Thing>();

        // Cells a cell-based order designated (Mine, Cancel).
        private static List<IntVec3> orderTargetCells = new List<IntVec3>();

        private static Designator activeDesignator = null;

        // Kept so undo can re-enter placement with the same shape.
        private static ShapeType usedShapeType = ShapeType.Manual;

        // Set while stepping out to place another shape, so re-entry keeps the segments.
        private static bool isAddingMore = false;

        // Computed once in Enter(); the isXDesignator properties below all read from it.
        private static DesignatorClassification designatorClassification = default;

        private static bool isBuildDesignator => designatorClassification.IsBuild;
        private static bool isOrderDesignator => designatorClassification.IsOrder;
        private static bool isZoneDesignator => designatorClassification.IsZone;
        private static bool isDeleteDesignator => designatorClassification.IsDelete;
        private static bool isAreaDesignator => designatorClassification.IsArea;
        private static bool isBuiltInAreaDesignator => designatorClassification.IsBuiltInArea;

        private static Area targetArea = null;

        #region Properties

        public static bool IsActive => isActive;

        /// <summary>
        /// Blueprints placed across all segments. Empty unless the designator is a Build one.
        /// </summary>
        public static List<Thing> PlacedBlueprints
        {
            get
            {
                return ViewingModeSegmentManager.GetAllPlacedBlueprints(segments);
            }
        }

        /// <summary>
        /// Cells designated across all segments, for non-Build designators.
        /// </summary>
        public static List<IntVec3> PlacedCells
        {
            get
            {
                return ViewingModeSegmentManager.GetAllPlacedCells(cellSegments);
            }
        }

        /// <summary>
        /// Blueprints for Build designators, cells for everything else.
        /// </summary>
        public static int PlacedCount
        {
            get
            {
                return ViewingModeSegmentManager.GetTotalPlacedCount(segments, cellSegments, isBuildDesignator);
            }
        }

        public static int SegmentCount => ViewingModeSegmentManager.GetSegmentCount(segments, cellSegments, isBuildDesignator);

        /// <summary>
        /// Cells where placement failed due to obstacles.
        /// </summary>
        public static List<IntVec3> ObstacleCells => obstacleCells;

        /// <summary>
        /// Things designated by order operations (Hunt, Haul).
        /// </summary>
        public static List<Thing> OrderTargets => orderTargets;

        /// <summary>
        /// Cells designated by cell-based order operations (Mine, Cancel).
        /// </summary>
        public static List<IntVec3> OrderTargetCells => orderTargetCells;

        public static bool IsOrderDesignator => isOrderDesignator;

        public static bool IsZoneDesignator => isZoneDesignator;

        /// <summary>
        /// Whether the designator is an Allowed Area expand/shrink.
        /// </summary>
        public static bool IsAreaDesignator => isAreaDesignator;

        /// <summary>
        /// Whether the designator targets a built-in area (Snow/Sand, Roof, Home).
        /// </summary>
        public static bool IsBuiltInAreaDesignator => isBuiltInAreaDesignator;

        public static Area TargetArea => targetArea;

        #endregion

        #region State Management

        /// <summary>
        /// Enters viewing mode with a shape-placement result. Called again per extra segment.
        /// </summary>
        public static void Enter(PlacementResult result, Designator designator = null, ShapeType shapeType = ShapeType.Manual)
        {
            if (result == null)
            {
                Log.Warning("[ViewingModeState] Enter called with null result");
                return;
            }

            // Reclassify on every entry so announcements stay correct while adding more shapes.
            designatorClassification = ShapeHelper.ClassifyDesignator(designator);
            if (isAreaDesignator)
            {
                targetArea = Designator_AreaAllowed.selectedArea;
            }
            else if (isBuiltInAreaDesignator)
            {
                targetArea = ShapeHelper.GetBuiltInAreaForDesignator(designator, Find.CurrentMap);
            }

            if (!isActive && !isAddingMore)
            {
                ScannerState.SaveFocus();
                segments.Clear();
                cellSegments.Clear();
                segmentShapeTypes.Clear();
                obstacleCells.Clear();
                obstacleCellsSet.Clear();
                protectedCount = 0;
                protectedByLabels.Clear();
                createdZones.Clear();
                orderTargets.Clear();
                orderTargetCells.Clear();
            }

            // Clearing targetZone lets CollectCreatedZones point it at the new segment's zone;
            // createdZones deliberately keeps accumulating, and stale entries are pruned at confirm.
            if (isAddingMore && isZoneDesignator)
            {
                targetZone = null;
                originalZoneCells.Clear();
            }

            isAddingMore = false;

            if (isBuildDesignator)
            {
                var newSegment = new List<Thing>(result.PlacedBlueprints ?? new List<Thing>());
                segments.Add(newSegment);
            }
            else
            {
                var newCellSegment = new List<IntVec3>(result.PlacedCells ?? new List<IntVec3>());
                cellSegments.Add(newCellSegment);

                if (isOrderDesignator && result.PlacedCells != null)
                {
                    CollectOrderTargets(result.PlacedCells, designator);
                }

                if (isOrderDesignator && OrderUndoTracker.HasPendingRecord)
                {
                    OrderUndoTracker.AddSegment();
                }
            }

            segmentShapeTypes.Add(shapeType);

            // Removing cells can't hit an obstacle, so delete designators skip this.
            if (!isDeleteDesignator && result.ObstacleCells != null)
            {
                foreach (var cell in result.ObstacleCells)
                {
                    if (obstacleCellsSet.Add(cell))
                        obstacleCells.Add(cell);
                }
            }

            // Protected cells join the obstacle list so the scanner can navigate to them.
            if (!isDeleteDesignator && result.ProtectedCells != null)
            {
                foreach (var cell in result.ProtectedCells)
                {
                    if (obstacleCellsSet.Add(cell))
                        obstacleCells.Add(cell);
                }
                protectedCount += result.ProtectedCount;
                if (result.ProtectedByLabels != null)
                {
                    foreach (string label in result.ProtectedByLabels)
                    {
                        protectedByLabels.Add(label);
                    }
                }
            }

            activeDesignator = designator;
            usedShapeType = shapeType;
            isActive = true;

            // Must run before UpdateObstacleCategory so interior obstacles reach the scanner;
            // obstacleCells goes in so corner gaps in the perimeter are detected.
            detectedEnclosures.Clear();
            if (isBuildDesignator)
            {
                detectedEnclosures = EnclosureDetector.DetectEnclosures(PlacedBlueprints, Find.CurrentMap, obstacleCells);
            }

            detectedRegionCount = 0;
            if (isZoneDesignator && result.PlacedCells != null && result.PlacedCells.Count > 0)
            {
                detectedRegionCount = CountDisconnectedRegions(result.PlacedCells);

                CollectCreatedZones(result.PlacedCells);

                ZoneUndoTracker.AddSegment();

                // CollectCreatedZones can't find the zone for a shrink (its cells are gone), so the
                // tracker's segment-independent PreShrinkOriginalCells supplies it.
                if (isDeleteDesignator)
                {
                    targetZone = ZoneUndoTracker.LastSegmentTargetZone;
                    var origCells = ZoneUndoTracker.PreShrinkOriginalCells;
                    if (origCells != null)
                    {
                        originalZoneCells = new HashSet<IntVec3>(origCells);
                    }
                }
                else
                {
                    // Only cells this expansion added may be removed again; the pre-expand set marks
                    // the rest off limits.
                    var origCells = ZoneUndoTracker.PreExpandOriginalCells;
                    if (origCells != null)
                    {
                        originalZoneCells = new HashSet<IntVec3>(origCells);
                    }
                }
            }

            if (isOrderDesignator)
            {
                ViewingModeScannerHelper.UpdateTargetsCategory(orderTargets, orderTargetCells, activeDesignator);
            }
            else if (!isDeleteDesignator)
            {
                ViewingModeScannerHelper.UpdateObstacleCategory(obstacleCells, detectedEnclosures, isZoneDesignator);
            }

            // Drops zones that undo already deleted, so the announcement counts only live ones.
            if (isZoneDesignator)
            {
                CleanupStaleZoneReferences();
            }

            // Zone expansion announces only the newly added shape's dimensions.
            List<IntVec3> lastSegmentCells = null;
            if (!isBuildDesignator && cellSegments.Count > 0)
            {
                lastSegmentCells = cellSegments[cellSegments.Count - 1];
            }

            string announcement = ViewingModeAnnouncer.BuildEntryAnnouncement(
                designator,
                PlacedCount,
                segmentShapeTypes,
                isOrderDesignator,
                isZoneDesignator,
                isBuildDesignator,
                isDeleteDesignator,
                obstacleCells,
                orderTargets,
                orderTargetCells,
                detectedRegionCount,
                detectedEnclosures,
                PlacedCells,
                lastSegmentCells,
                PlacedBlueprints,
                ZoneUndoTracker.WasZoneExpansion,
                targetZone,
                createdZones,
                isAreaDesignator,
                targetArea,
                isBuiltInAreaDesignator,
                protectedCount,
                protectedByLabels);
            TolkHelper.SpeakData(announcement, SpeechPriority.Normal);

            int totalPlaced = PlacedCount;
            int segCount = SegmentCount;
            string itemType = isBuildDesignator ? "blueprints" : "designations";
        }

        /// <summary>
        /// Queries the map at each placed cell for what the order designator actually designated,
        /// appending to <see cref="orderTargets"/> and <see cref="orderTargetCells"/>.
        /// </summary>
        private static void CollectOrderTargets(List<IntVec3> placedCells, Designator designator)
        {
            Map map = Find.CurrentMap;
            if (map == null || designator == null)
                return;

            foreach (IntVec3 cell in placedCells)
            {
                List<Thing> things = cell.GetThingList(map);
                bool foundThingTarget = false;

                foreach (Thing thing in things)
                {
                    if (map.designationManager.DesignationOn(thing) != null)
                    {
                        if (!orderTargets.Contains(thing))
                        {
                            orderTargets.Add(thing);
                            foundThingTarget = true;
                        }
                    }
                }

                // Nothing thing-based here means a cell-based designation (Mine, Cancel).
                if (!foundThingTarget)
                {
                    if (map.designationManager.AllDesignationsAt(cell).Any())
                    {
                        if (!orderTargetCells.Contains(cell))
                        {
                            orderTargetCells.Add(cell);
                        }
                    }
                }
            }
        }

        #region Zone Helper Methods

        /// <summary>
        /// The cells belonging to a zone.
        /// </summary>
        private static List<IntVec3> GetZoneCells(Zone zone)
        {
            return ZoneEditingHelper.GetZoneCells(zone);
        }

        #endregion

        /// <summary>
        /// Confirms all placements, then exits viewing mode and architect/placement mode.
        /// The cursor stays where the player left it.
        /// </summary>
        public static void Confirm()
        {
            if (!isActive)
                return;

            int totalPlaced = PlacedCount;
            string announcement;

            if (isZoneDesignator)
            {
                if (isDeleteDesignator)
                {
                    announcement = (totalPlaced == 1
                        ? "RimWorldAccess.Building.View.ConfirmZoneCellsRemovedOne"
                        : "RimWorldAccess.Building.View.ConfirmZoneCellsRemovedMany").Translate(totalPlaced);
                }
                else
                {
                    CleanupStaleZoneReferences();
                    int zoneCount = createdZones.Count;
                    string zoneName = ViewingModeAnnouncer.GetZoneTypeName(activeDesignator, targetZone, createdZones);

                    bool wasExpansion = ZoneUndoTracker.WasZoneExpansion;

                    if (zoneCount == 1)
                    {
                        Zone theZone = createdZones.First();
                        int actualCellCount = theZone.Cells.Count();

                        // Zero for a new zone, positive for an expansion.
                        int originalCellCount = ZoneUndoTracker.LastSegmentOriginalCells?.Count ?? 0;

                        int cellsAdded = actualCellCount - originalCellCount;

                        int expectedCellCount = PlacedCells.Count;

                        // A mismatch means cells were toggled by hand with Space.
                        bool wasModified = cellsAdded != expectedCellCount;

                        if (wasModified)
                        {
                            // The shape no longer holds, so report a count rather than dimensions.
                            if (wasExpansion)
                            {
                                announcement = (cellsAdded == 1
                                    ? "RimWorldAccess.Building.View.ConfirmCellsAddedToZoneOne"
                                    : "RimWorldAccess.Building.View.ConfirmCellsAddedToZoneMany").Translate(cellsAdded, zoneName);
                            }
                            else
                            {
                                announcement = (actualCellCount == 1
                                    ? "RimWorldAccess.Building.View.ConfirmZoneCreatedFromCellsOne"
                                    : "RimWorldAccess.Building.View.ConfirmZoneCreatedFromCellsMany").Translate(actualCellCount, zoneName);
                            }
                        }
                        else
                        {
                            string sizeString = ShapeHelper.FormatShapeSize(PlacedCells);
                            announcement = (wasExpansion
                                ? "RimWorldAccess.Building.View.ConfirmZoneExpanded"
                                : "RimWorldAccess.Building.View.ConfirmZoneCreated").Translate(sizeString, zoneName);
                        }
                    }
                    else
                    {
                        // Several zones only arise from a split, so the wording is always "created".
                        var zoneSizes = new List<(Zone zone, int cellCount, string sizeStr)>();
                        int totalCells = 0;

                        foreach (Zone zone in createdZones)
                        {
                            var zoneCells = GetZoneCells(zone);
                            int cellCount = zoneCells.Count;
                            totalCells += cellCount;
                            string sizeStr = ShapeHelper.FormatShapeSize(zoneCells);
                            zoneSizes.Add((zone, cellCount, sizeStr));
                        }

                        zoneSizes.Sort((a, b) => b.cellCount.CompareTo(a.cellCount));

                        // Zones under 1% of the total are summarised rather than listed.
                        int threshold = totalCells / 100;
                        if (threshold < 1)
                            threshold = 1;

                        var significantSizes = new List<string>();
                        int smallZoneCount = 0;

                        foreach (var (zone, cellCount, sizeStr) in zoneSizes)
                        {
                            if (cellCount >= threshold)
                            {
                                significantSizes.Add(sizeStr);
                            }
                            else
                            {
                                smallZoneCount++;
                            }
                        }

                        string sizesPart = string.Join(", ", significantSizes);
                        if (smallZoneCount > 0)
                        {
                            sizesPart += (smallZoneCount == 1
                                ? "RimWorldAccess.Building.View.SmallerZonesOne"
                                : "RimWorldAccess.Building.View.SmallerZonesMany").Translate(smallZoneCount);
                        }

                        announcement = (zoneCount == 1
                            ? "RimWorldAccess.Building.View.ConfirmMultipleZonesCreatedOne"
                            : "RimWorldAccess.Building.View.ConfirmMultipleZonesCreatedMany").Translate(zoneCount, zoneName, sizesPart);
                    }
                }
            }
            else if (isBuildDesignator)
            {
                announcement = "RimWorldAccess.Building.View.OrdersConfirmedBlueprints".Translate(totalPlaced);
            }
            else
            {
                announcement = "RimWorldAccess.Building.View.OrdersConfirmedDesignations".Translate(totalPlaced);
            }

            TolkHelper.SpeakData(announcement, SpeechPriority.Normal);

            // Clear zone undo tracker data since changes are confirmed
            if (isZoneDesignator)
            {
                ZoneUndoTracker.Clear();
            }

            Reset();

            // Also exit architect/placement mode entirely. ArchitectState.Reset() returns early
            // outside architect mode, so the gizmo/dialog flow needs the explicit Deselect —
            // without it the designator stays armed and placement silently re-enters.
            ShapePlacementState.Reset();
            if (ArchitectState.CurrentMode == ArchitectMode.Inactive)
            {
                Find.DesignatorManager?.Deselect();
            }
            else
            {
                ArchitectState.Reset();
            }

        }

        /// <summary>
        /// Removes the last segment only and stays in viewing mode.
        /// For Build designators: Destroys blueprints
        /// For Orders/Zones/Cells: Removes designations from DesignationManager
        /// If no segments left, exits viewing mode and returns to placement.
        /// </summary>
        public static void RemoveLastSegment()
        {
            if (!isActive)
                return;

            int currentSegCount = SegmentCount;
            if (currentSegCount == 0)
                return;

            // Capture the shape type before removing (for announcement)
            ShapeType removedShapeType = ShapeType.Manual;
            if (segmentShapeTypes.Count > 0)
            {
                removedShapeType = segmentShapeTypes[segmentShapeTypes.Count - 1];
                segmentShapeTypes.RemoveAt(segmentShapeTypes.Count - 1);
            }

            // Capture the segment cells before removing (for shape-aware size formatting)
            List<IntVec3> removedCells = null;
            if (!isBuildDesignator && cellSegments.Count > 0)
            {
                removedCells = new List<IntVec3>(cellSegments[cellSegments.Count - 1]);
            }

            // Remove the last segment using centralized helper
            int lastIndex = isBuildDesignator ? segments.Count - 1 : cellSegments.Count - 1;
            int removedCount = ViewingModeSegmentManager.RemoveSegmentItems(
                lastIndex, segments, cellSegments, isBuildDesignator, isZoneDesignator, isAreaDesignator, isBuiltInAreaDesignator, activeDesignator, targetZone);

            // Remove the segment from the list
            if (isBuildDesignator)
                segments.RemoveAt(segments.Count - 1);
            else
                cellSegments.RemoveAt(cellSegments.Count - 1);

            // Build the size string - use shape-aware formatting for zones
            string sizeString;
            if (isZoneDesignator && removedCells != null && removedCells.Count > 0)
            {
                sizeString = ViewingModeAnnouncer.FormatZoneCellsSize(removedCells);
            }
            else
            {
                string itemType = ViewingModeSegmentManager.GetItemTypeForCount(removedCount, isBuildDesignator, isZoneDesignator);
                sizeString = $"{removedCount} {itemType}";
            }

            // Get shape display name for the removed segment
            string removedShapeName = ViewingModeAnnouncer.GetShapeDisplayName(removedShapeType);

            // Announce what happened
            // For zone shrink (delete designator), undoing RE-ADDS cells, so say "Restored" not "Removed"
            string action = isDeleteDesignator
                ? (string)"RimWorldAccess.Building.View.ActionRestored".Translate()
                : (string)"RimWorldAccess.Building.View.ActionRemoved".Translate();
            int remainingSegments = SegmentCount;

            if (remainingSegments > 0)
            {
                // Use shape type counts for remaining segments
                string shapeInfo = ViewingModeAnnouncer.FormatShapeTypeCounts(segmentShapeTypes);
                string remainingInfo = !string.IsNullOrEmpty(shapeInfo)
                    ? (string)"RimWorldAccess.Building.View.RemainingInShapeSuffix".Translate(shapeInfo)
                    : (string)"RimWorldAccess.Building.View.RemainingInSegmentsSuffix".Translate(remainingSegments);

                // Format remaining size using shape-aware formatting (dimensions when possible)
                string remainingSizeString;
                if (isZoneDesignator)
                {
                    var remainingCells = PlacedCells;
                    remainingSizeString = ShapeHelper.FormatShapeSize(remainingCells);
                }
                else
                {
                    int remainingCount = PlacedCount;
                    string itemType = ViewingModeSegmentManager.GetItemTypeForCount(remainingCount, isBuildDesignator, isZoneDesignator);
                    remainingSizeString = $"{remainingCount} {itemType}";
                }

                TolkHelper.Speak("RimWorldAccess.Building.View.SegmentRemovedSomeRemain".Loc(action, sizeString, removedShapeName, remainingSizeString, remainingInfo), SpeechPriority.Normal);
            }
            else
            {
                // No segments left - stay in viewing mode, user presses = to add more
                TolkHelper.Speak("RimWorldAccess.Building.View.SegmentRemovedNoneRemain".Loc(action, sizeString, removedShapeName), SpeechPriority.Normal);
            }

        }

        /// <summary>
        /// Reactivates viewing mode without resetting state.
        /// Used when returning from shape placement via Escape.
        /// Keeps all segments intact and simply re-enables viewing mode.
        /// </summary>
        public static void Reactivate()
        {
            int segCount = SegmentCount;
            if (segCount == 0)
            {
                Log.Warning("[ViewingModeState] Reactivate called but no segments exist");
                return;
            }

            // Re-activate viewing mode without resetting anything
            isActive = true;
            isAddingMore = false;

            TolkHelper.Speak("RimWorldAccess.Building.View.ReturnedToPreview".Loc(), SpeechPriority.Normal);

        }

        /// <summary>
        /// Goes back to shape placement mode to add another segment.
        /// Keeps existing blueprints in place.
        /// </summary>
        public static void AddAnotherShape()
        {
            if (!isActive)
                return;

            Designator savedDesignator = activeDesignator;
            ShapeType savedShape = usedShapeType;

            // Mark that we're adding more - don't clear segments on re-entry
            isAddingMore = true;

            // Exit viewing mode but keep segments intact
            isActive = false;

            TolkHelper.Speak("RimWorldAccess.Building.View.AddAnotherShape".Loc(), SpeechPriority.Normal);

            // Enter shape placement mode with viewing mode on stack (so Escape can return here)
            if (savedDesignator != null && savedShape != ShapeType.Manual)
            {
                ShapePlacementState.Enter(savedDesignator, savedShape, fromViewingMode: true);
            }

        }

        /// <summary>
        /// Removes last segment AND returns to placement mode.
        /// This is the Escape key behavior.
        /// Works for all designator types: Build (blueprints), Orders, Zones, and Cells.
        /// </summary>
        public static void UndoLastAndReturn()
        {
            if (!isActive)
                return;

            int removedCount = 0;
            int currentSegCount = SegmentCount;

            // Remove the last segment if there is one
            if (currentSegCount > 0)
            {
                // Remove the last segment using centralized helper
                int lastIndex = isBuildDesignator ? segments.Count - 1 : cellSegments.Count - 1;
                removedCount = ViewingModeSegmentManager.RemoveSegmentItems(
                    lastIndex, segments, cellSegments, isBuildDesignator, isZoneDesignator, isAreaDesignator, isBuiltInAreaDesignator, activeDesignator, targetZone);

                // Remove the segment from the list
                if (isBuildDesignator)
                    segments.RemoveAt(segments.Count - 1);
                else
                    cellSegments.RemoveAt(cellSegments.Count - 1);

                string itemType = ViewingModeSegmentManager.GetItemTypeForCount(removedCount, isBuildDesignator, isZoneDesignator);
                TolkHelper.Speak("RimWorldAccess.Building.View.RemovedItemsReturning".Loc(removedCount, itemType), SpeechPriority.Normal);
            }
            else
            {
                TolkHelper.Speak("RimWorldAccess.Building.View.ReturningToPlacement".Loc(), SpeechPriority.Normal);
            }

            // Save designator and shape before leaving
            Designator savedDesignator = activeDesignator;
            ShapeType savedShape = usedShapeType;

            // If there are still segments, just temporarily exit viewing mode
            // When they come back via Enter, we'll add to existing segments
            int remainingSegments = SegmentCount;
            if (remainingSegments > 0)
            {
                isAddingMore = true; // Keep segments on re-entry
                isActive = false;
            }
            else
            {
                // No segments left, fully reset
                // Note: We do NOT restore cursor position - keep it where the user left it
                Reset();
            }

            // Return to shape placement mode with viewing mode on stack if segments remain
            if (savedDesignator != null && savedShape != ShapeType.Manual)
            {
                bool hasRemainingSegments = SegmentCount > 0;
                ShapePlacementState.Enter(savedDesignator, savedShape, fromViewingMode: hasRemainingSegments);
            }

        }

        /// <summary>
        /// Removes ALL placed items and exits completely.
        /// For Build designators: Destroys blueprints
        /// For Orders/Zones/Cells: Removes designations
        /// </summary>
        public static void UndoAll()
        {
            if (!isActive)
                return;

            // Remove all segments using centralized helper (-1 = all segments)
            int removedCount = ViewingModeSegmentManager.RemoveSegmentItems(
                -1, segments, cellSegments, isBuildDesignator, isZoneDesignator, isAreaDesignator, isBuiltInAreaDesignator, activeDesignator, targetZone);

            string itemType = ViewingModeSegmentManager.GetItemTypeForCount(removedCount, isBuildDesignator, isZoneDesignator);
            TolkHelper.Speak((removedCount == 1
                ? "RimWorldAccess.Building.View.RemovedItems"
                : "RimWorldAccess.Building.View.RemovedAllItems").Loc(removedCount, itemType), SpeechPriority.Normal);

            // Note: We do NOT restore cursor position - keep it where the user left it

            // Save the designator and shape before reset
            Designator savedDesignator = activeDesignator;
            ShapeType savedShape = usedShapeType;

            Reset();

            // Re-enter shape placement mode with the same shape
            if (savedDesignator != null && savedShape != ShapeType.Manual)
            {
                ShapePlacementState.Enter(savedDesignator, savedShape);
            }

        }

        /// <summary>
        /// Shows a confirmation dialog before exiting viewing mode.
        /// "Leave" removes all blueprints/designations/zone changes and exits to game map.
        /// "Stay" closes the dialog and stays in preview mode.
        /// </summary>
        public static void ShowExitConfirmation()
        {
            if (!isActive)
                return;

            // If no segments remain, just exit immediately without showing dialog. Nothing
            // pending is discarded here, so the wording is "exited", never "cancelled" —
            // already-confirmed work stays.
            if (SegmentCount == 0)
            {
                if (isZoneDesignator)
                {
                    TolkHelper.Speak("RimWorldAccess.Building.ArchitectPlace.ExitedZoneEditing".Loc(), SpeechPriority.Normal);
                }
                else
                {
                    TolkHelper.Speak("RimWorldAccess.Building.ArchitectPlace.ExitedPlacement".Loc(), SpeechPriority.Normal);
                }

                Reset();
                ShapePlacementState.Reset();
                GizmoZoneEditState.Reset();

                // ArchitectState.Reset() returns early if not in architect mode,
                // so explicitly deselect the designator for gizmo mode
                if (ArchitectState.CurrentMode == ArchitectMode.Inactive)
                {
                    Find.DesignatorManager?.Deselect();
                }
                else
                {
                    ArchitectState.Reset();
                }

                return;
            }

            // Determine dialog message based on designator type
            string dialogMessage;
            if (isBuildDesignator)
            {
                dialogMessage = "RimWorldAccess.Building.View.LeavePreviewBlueprints".Translate();
            }
            else if (isZoneDesignator)
            {
                dialogMessage = "RimWorldAccess.Building.View.LeavePreviewZones".Translate();
            }
            else
            {
                dialogMessage = "RimWorldAccess.Building.View.LeavePreviewDesignations".Translate();
            }

            Find.WindowStack.Add(new Dialog_MessageBox(
                dialogMessage,
                "RimWorldAccess.Building.View.DialogLeave".Translate(),
                () =>
                {
                    // Remove all segments using centralized helper (-1 = all segments)
                    int removedCount = ViewingModeSegmentManager.RemoveSegmentItems(
                        -1, segments, cellSegments, isBuildDesignator, isZoneDesignator, isAreaDesignator, isBuiltInAreaDesignator, activeDesignator, targetZone);

                    string itemType = ViewingModeSegmentManager.GetItemTypeForCount(removedCount, isBuildDesignator, isZoneDesignator);
                    // For zone shrink (delete designator), undoing RE-ADDS cells, so say "Restored" not "Removed"
                    string action = isDeleteDesignator
                        ? (string)"RimWorldAccess.Building.View.ActionRestored".Translate()
                        : (string)"RimWorldAccess.Building.View.ActionRemoved".Translate();

                    // Use appropriate exit message based on designator type
                    string exitMessage = isZoneDesignator
                        ? (string)"RimWorldAccess.Building.View.ZoneEditingCancelled".Translate()
                        : (string)"RimWorldAccess.Building.View.PlacementCancelled".Translate();
                    string exitKey = removedCount == 1
                        ? "RimWorldAccess.Building.View.ExitWithRemoval"
                        : "RimWorldAccess.Building.View.ExitWithRemovalAll";
                    TolkHelper.Speak(exitKey.Loc(action, removedCount, itemType, exitMessage), SpeechPriority.Normal);

                    // Exit completely (don't restore cursor - keep it where user left it)
                    Reset();

                    // Also exit architect/placement mode entirely
                    ShapePlacementState.Reset();
                    GizmoZoneEditState.Reset();

                    // ArchitectState.Reset() returns early if not in architect mode,
                    // so explicitly deselect the designator for gizmo mode
                    if (ArchitectState.CurrentMode == ArchitectMode.Inactive)
                    {
                        Find.DesignatorManager?.Deselect();
                    }
                    else
                    {
                        ArchitectState.Reset();
                    }

                },
                "RimWorldAccess.Building.View.DialogStay".Translate(),
                null,
                "RimWorldAccess.Building.View.DialogConfirmExit".Translate(),
                false
            ));
        }

        /// <summary>
        /// Resets all state to inactive.
        /// </summary>
        private static void Reset()
        {
            // Clear zone undo tracker data
            ZoneUndoTracker.Clear();

            // Clear order undo tracker data
            OrderUndoTracker.Clear();

            isActive = false;
            isAddingMore = false;
            designatorClassification = default;
            targetArea = null;
            segments.Clear();
            cellSegments.Clear();
            segmentShapeTypes.Clear();
            obstacleCells.Clear();
            obstacleCellsSet.Clear();
            detectedEnclosures.Clear();
            detectedRegionCount = 0;
            createdZones.Clear();
            targetZone = null;
            originalZoneCells.Clear();
            orderTargets.Clear();
            orderTargetCells.Clear();
            activeDesignator = null;
            usedShapeType = ShapeType.Manual;

            // Clean up scanner temporary category
            ScannerState.RemoveTemporaryCategory();
            ScannerState.RestoreFocus();
        }

        #endregion

        #region Blueprint Management

        /// <summary>
        /// Adds a blueprint at the current map cursor position.
        /// </summary>
        public static void AddBlueprintAtCursor()
        {
            if (!isActive)
                return;

            if (activeDesignator == null)
            {
                TolkHelper.Speak("RimWorldAccess.Building.View.NoDesignatorAvailable".Loc(), SpeechPriority.High);
                return;
            }

            if (!GuardHelper.RequireMap(out Map map, SpeechPriority.High)) return;

            IntVec3 cursorPos = MapNavigationState.CurrentCursorPosition;

            // Check if we can place here
            AcceptanceReport report = activeDesignator.CanDesignateCell(cursorPos);
            if (!report.Accepted)
            {
                TolkHelper.SpeakData(PlacementDescriber.ReasonOrFallback(report), SpeechPriority.Normal);
                return;
            }

            if (!GodModeWipeWarning.WarnBeforeInstantPlace(activeDesignator, cursorPos, map))
                return;

            try
            {
                // Track things before placement
                List<Thing> thingsBefore = new List<Thing>(cursorPos.GetThingList(map));

                // Place the blueprint
                activeDesignator.DesignateSingleCell(cursorPos);

                // Call Finalize to play the placement sound (like manual placement does)
                activeDesignator.Finalize(true);

                // Find the newly placed blueprint (or the finished thing in god mode)
                ThingDef placingDef = MeditationProtectionHelper.GetPlacementInfo(activeDesignator).def;
                List<Thing> thingsAfter = cursorPos.GetThingList(map);
                foreach (Thing thing in thingsAfter)
                {
                    if (!thingsBefore.Contains(thing) &&
                        (thing.def.IsBlueprint || thing.def.IsFrame ||
                            (placingDef != null && thing.def == placingDef)))
                    {
                        // Add to the last segment (or create one if needed)
                        if (segments.Count == 0)
                        {
                            segments.Add(new List<Thing>());
                        }
                        segments[segments.Count - 1].Add(thing);
                        break;
                    }
                }

                // Remove from obstacle list if it was there and update scanner category
                if (obstacleCellsSet.Remove(cursorPos))
                {
                    obstacleCells.Remove(cursorPos);
                    ViewingModeScannerHelper.UpdateObstacleCategory(obstacleCells, detectedEnclosures, isZoneDesignator);
                }

                // Announce like manual placement: "{label} placed at x, z"
                string label = activeDesignator.Label ?? (string)"RimWorldAccess.Building.View.BlueprintFallback".Translate();
                TolkHelper.Speak("RimWorldAccess.Building.View.PlacedAt".Loc(label, cursorPos.x, cursorPos.z), SpeechPriority.Normal);
            }
            catch (System.Exception ex)
            {
                Log.Error($"[ViewingModeState] Error adding blueprint: {ex.Message}");
                TolkHelper.Speak("RimWorldAccess.Building.View.FailedToAddBlueprint".Loc(), SpeechPriority.High);
            }
        }

        /// <summary>
        /// Removes a blueprint at the current map cursor position.
        /// </summary>
        public static void RemoveBlueprintAtCursor()
        {
            if (!isActive)
                return;

            if (!GuardHelper.RequireMap(out Map map, SpeechPriority.High)) return;

            IntVec3 cursorPos = MapNavigationState.CurrentCursorPosition;

            // Find blueprints at cursor position that we placed
            List<Thing> things = cursorPos.GetThingList(map);
            Thing blueprintToRemove = null;
            var allPlaced = PlacedBlueprints;

            foreach (Thing thing in things)
            {
                if (allPlaced.Contains(thing))
                {
                    blueprintToRemove = thing;
                    break;
                }
            }

            if (blueprintToRemove == null)
            {
                TolkHelper.Speak("RimWorldAccess.Building.View.NoBlueprintHere".Loc(), SpeechPriority.Normal);
                return;
            }

            // Get the label before destroying
            string thingLabel = blueprintToRemove.LabelShort;

            // Remove the blueprint from whichever segment contains it
            foreach (var segment in segments)
            {
                if (segment.Remove(blueprintToRemove))
                    break;
            }
            // Cancel is only valid for blueprints/frames; god-mode placements are finished things.
            bool isBlueprintLike = blueprintToRemove.def.IsBlueprint || blueprintToRemove.def.IsFrame;
            blueprintToRemove.Destroy(isBlueprintLike ? DestroyMode.Cancel : DestroyMode.Vanish);

            // Play cancel sound and announce like manual placement
            SoundDefOf.Designate_Cancel.PlayOneShotOnCamera();
            TolkHelper.Speak("RimWorldAccess.Building.View.CancelledBlueprint".Loc(thingLabel), SpeechPriority.Normal);
        }

        /// <summary>
        /// Toggles a zone cell at the current cursor position.
        /// Delegates to ZoneEditingHelper for the actual logic.
        /// </summary>
        public static void ToggleZoneCellAtCursor()
        {
            if (!isActive)
                return;

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
                }
                else
                {
                }
            }
        }

        #endregion

        #region Zone Region Detection

        /// <summary>
        /// Counts the number of disconnected regions among a set of cells using flood fill.
        /// Delegates to ZoneEditingHelper.
        /// </summary>
        /// <param name="cells">The cells to analyze for contiguity</param>
        /// <returns>The number of disconnected regions (1 = fully contiguous, 2+ = will be split)</returns>
        private static int CountDisconnectedRegions(List<IntVec3> cells)
        {
            return ZoneEditingHelper.CountDisconnectedRegions(cells);
        }

        /// <summary>
        /// Collects the unique zones that were created/expanded by zone placement.
        /// Delegates to ZoneEditingHelper.
        /// </summary>
        /// <param name="placedCells">The cells that were designated for zoning</param>
        private static void CollectCreatedZones(List<IntVec3> placedCells)
        {
            ZoneEditingHelper.CollectCreatedZones(placedCells, createdZones, ref targetZone);
        }

        /// <summary>
        /// Removes stale zone references from createdZones.
        /// Delegates to ZoneEditingHelper.
        /// </summary>
        private static void CleanupStaleZoneReferences()
        {
            ZoneEditingHelper.CleanupStaleZoneReferences(createdZones);
        }

        #endregion

        #region Input Handling

        /// <summary>
        /// Space handler: folded in verbatim from the
        /// legacy handler's Space case. Zone designators use a true toggle based
        /// on actual zone membership regardless of shift; other designator
        /// types use the standard blueprint add/remove methods, chosen by
        /// <paramref name="shift"/> — the shell registers this as two separate
        /// chord claims (bare Space and Shift+Space), each passing its own
        /// literal shift value, since <c>ToggleZoneCellAtCursor</c> must run
        /// identically for both chords when a zone designator is active.
        /// </summary>
        public static void HandleSpaceAtCursor(bool shift)
        {
            if (!isActive)
                return;

            if (isZoneDesignator)
            {
                ToggleZoneCellAtCursor();
            }
            else if (shift)
            {
                RemoveBlueprintAtCursor();
            }
            else
            {
                AddBlueprintAtCursor();
            }
        }

        #endregion
    }
}
