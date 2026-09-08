using System.Collections.Generic;
using System.Linq;
using Verse;
using RimWorld;

namespace RimWorldAccess
{
    /// <summary>The modes of the architect system.</summary>
    public enum ArchitectMode
    {
        Inactive,           // Not in architect mode
        CategorySelection,  // Selecting a category (Orders, Structure, etc.)
        ToolSelection,      // Selecting a tool within a category
        MaterialSelection,  // Selecting material for construction
        PlacementMode       // Placing designations on the map
    }

    /// <summary>The selection mode for architect placement.</summary>
    public enum ArchitectSelectionMode
    {
        BoxSelection,    // Space sets corners for rectangle selection
        SingleTile       // Space toggles individual tiles
    }

    /// <summary>State for the accessible architect system: mode, selected category, designator, and placement.</summary>
    public static class ArchitectState
    {
        private static ArchitectMode currentMode = ArchitectMode.Inactive;
        private static DesignationCategoryDef selectedCategory = null;
        private static Designator selectedDesignator = null;
        private static BuildableDef selectedBuildable = null;
        private static ThingDef selectedMaterial = null;
        private static List<IntVec3> selectedCells = new List<IntVec3>();
        private static Rot4 currentRotation = Rot4.North;
        private static ArchitectSelectionMode selectionMode = ArchitectSelectionMode.BoxSelection; // Default to box selection

        public static ArchitectMode CurrentMode => currentMode;

        public static DesignationCategoryDef SelectedCategory => selectedCategory;

        public static Designator SelectedDesignator => selectedDesignator;

        public static BuildableDef SelectedBuildable => selectedBuildable;

        public static ThingDef SelectedMaterial => selectedMaterial;

        /// <summary>The cells selected for placement, as a read-only view.</summary>
        public static IReadOnlyList<IntVec3> SelectedCells => selectedCells.AsReadOnly();

        /// <summary>Clears the selected cells, after placing a build designator at a single cell.</summary>
        public static void ClearSelectedCells()
        {
            selectedCells.Clear();
        }

        public static Rot4 CurrentRotation
        {
            get => currentRotation;
            set => currentRotation = value;
        }

        /// <summary>Whether architect mode is active (any mode except Inactive).</summary>
        public static bool IsActive => currentMode != ArchitectMode.Inactive;

        /// <summary>
        /// Whether placement mode is live on the map. Also verifies a designator really is selected
        /// in the game, so an externally deselected designator cannot leave stale state.
        /// </summary>
        public static bool IsInPlacementMode =>
            currentMode == ArchitectMode.PlacementMode &&
            Find.DesignatorManager?.SelectedDesignator != null;

        public static ArchitectSelectionMode SelectionMode => selectionMode;

        /// <summary>Toggles box versus single-tile selection; zone designators only.</summary>
        public static void ToggleSelectionMode()
        {
            if (!IsZoneDesignator())
            {
                TolkHelper.Speak("RimWorldAccess.Building.Architect.SelectionModeZoneOnly".Loc());
                return;
            }

            selectionMode = (selectionMode == ArchitectSelectionMode.BoxSelection)
                ? ArchitectSelectionMode.SingleTile
                : ArchitectSelectionMode.BoxSelection;

            string modeName = (selectionMode == ArchitectSelectionMode.BoxSelection)
                ? "RimWorldAccess.Building.Paint.ModeBox".Translate()
                : "RimWorldAccess.Building.Paint.ModeSingle".Translate();
            TolkHelper.SpeakData(modeName);
            ModLogger.Dev($"Architect placement: Switched to {modeName}");
        }

        /// <summary>Enters category selection mode.</summary>
        public static void EnterCategorySelection()
        {
            currentMode = ArchitectMode.CategorySelection;
            selectedCategory = null;
            selectedDesignator = null;
            selectedBuildable = null;
            selectedMaterial = null;
            selectedCells.Clear();

            ModLogger.Dev("Entered architect category selection");
        }

        /// <summary>Enters tool selection mode for one category.</summary>
        public static void EnterToolSelection(DesignationCategoryDef category)
        {
            currentMode = ArchitectMode.ToolSelection;
            selectedCategory = category;
            selectedDesignator = null;
            selectedBuildable = null;
            selectedMaterial = null;
            selectedCells.Clear();

            TolkHelper.Speak("RimWorldAccess.Building.Architect.CategorySelected".Loc(category.LabelCap));
            ModLogger.Dev($"Entered tool selection for category: {category.defName}");
        }

        /// <summary>Enters material selection mode for a buildable that requires stuff.</summary>
        public static void EnterMaterialSelection(BuildableDef buildable, Designator designator)
        {
            currentMode = ArchitectMode.MaterialSelection;
            selectedBuildable = buildable;
            selectedDesignator = designator;
            selectedMaterial = null;
            selectedCells.Clear();

            TolkHelper.Speak("RimWorldAccess.Building.Architect.SelectMaterialFor".Loc(buildable.label));
            ModLogger.Dev($"Entered material selection for: {buildable.defName}");
        }

        /// <summary>
        /// Enters placement mode with the selected designator; DesignatorManagerPatch then enters
        /// ShapePlacementState or announces manual mode.
        /// </summary>
        /// <param name="selectAction">A harvested vanilla option delegate that performs the Select
        /// itself along with the mod's own side effects; when null, Select() is called directly.</param>
        public static void EnterPlacementMode(Designator designator, ThingDef material = null, System.Action selectAction = null)
        {
            GizmoNavigationState.Close();

            currentMode = ArchitectMode.PlacementMode;
            selectedDesignator = designator;
            selectedMaterial = material;
            selectedCells.Clear();

            currentRotation = Rot4.North;

            selectionMode = ArchitectSelectionMode.BoxSelection;

            if (designator is Designator_Place placeDesignator)
            {
                BuildingReflection.SetPlacingRot(placeDesignator, currentRotation);
            }

            string toolName = designator.Label;
            ModLogger.Dev($"Entered placement mode with designator: {toolName}");

            // Taught once on first reaching placement mode; the session and knowledge guards keep
            // it quiet after.
            DocsTeacher.Teach("RWA_PlacementMode");

            if (Find.DesignatorManager != null)
            {
                if (selectAction != null)
                    selectAction();
                else
                    Find.DesignatorManager.Select(designator);

                // Selected() sets placingRot to PlacingDef.defaultPlacingRot, so re-read it.
                if (designator is Designator_Place placeDesignatorForSync)
                {
                    currentRotation = BuildingReflection.GetPlacingRot(placeDesignatorForSync);
                }
            }
        }

        /// <summary>Rotates the building being placed; works for Designator_Build and Designator_Install alike.</summary>
        public static void RotateBuilding(RotationDirection direction = RotationDirection.Clockwise)
        {
            if (!IsInPlacementMode || !(selectedDesignator is Designator_Place placeDesignator))
                return;

            Rot4 rotated = currentRotation;
            rotated.Rotate(direction);
            ApplyRotation(placeDesignator, rotated);
        }

        /// <summary>
        /// Points the building being placed at an absolute rotation, announcement included. Reads
        /// the live selected designator rather than <see cref="selectedDesignator"/>, so
        /// gizmo-driven placement, which never enters architect placement mode, is covered too.
        /// </summary>
        public static void SetBuildingRotation(Rot4 rotation)
        {
            if (Find.DesignatorManager?.SelectedDesignator is Designator_Place placeDesignator)
                ApplyRotation(placeDesignator, rotation);
        }

        private static void ApplyRotation(Designator_Place placeDesignator, Rot4 rotation)
        {
            currentRotation = rotation;

            BuildingReflection.SetPlacingRot(placeDesignator, currentRotation);

            string announcement = GetRotationAnnouncementForDef(placeDesignator.PlacingDef, currentRotation);
            TolkHelper.SpeakData(announcement);
            ModLogger.Dev($"Rotated building to: {currentRotation}");
        }

        /// <summary>The rotation announcement for any BuildableDef, shared by architect and gizmo placement.</summary>
        internal static string GetRotationAnnouncementForDef(BuildableDef def, Rot4 rotation)
        {
            string facing = "RimWorldAccess.Building.Architect.Facing".Translate(GetRotationName(rotation));

            if (def == null)
                return facing;

            IntVec2 size = def.Size;
            string sizeInfo = GetSizeDescription(size, rotation);
            string specialRequirements = GetSpecialSpatialRequirements(def, rotation);

            return new AnnouncementBuilder()
                .Add(facing)
                .Add(sizeInfo)
                .Add(specialRequirements)
                .Build();
        }

        /// <summary>Special spatial requirements: wind turbines, coolers, beds, fuel ports, TVs, interaction cells.</summary>
        private static string GetSpecialSpatialRequirements(BuildableDef def, Rot4 rotation)
        {
            if (def == null || !(def is ThingDef thingDef))
                return null;

            if (IsWindTurbine(thingDef))
            {
                return GetWindTurbineRequirements(thingDef, rotation);
            }

            return BuildingCellHelper.GetPlacementPositionInfo(thingDef, rotation);
        }

        /// <summary>Identifies a wind turbine by its PlaceWorker_WindTurbine, the game's own marker.</summary>
        private static bool IsWindTurbine(ThingDef def)
        {
            if (def?.placeWorkers == null)
                return false;

            foreach (var workerType in def.placeWorkers)
            {
                if (workerType == typeof(PlaceWorker_WindTurbine))
                    return true;
            }
            return false;
        }

        /// <summary>
        /// Wind-turbine clear space, from the game's own
        /// <see cref="WindTurbineUtility.CalculateWindCells"/> rather than a hand-maintained
        /// per-rotation table that could drift. Cells are requested relative to the origin, so the
        /// result is direction and depth only, independent of any map placement.
        /// </summary>
        private static string GetWindTurbineRequirements(ThingDef def, Rot4 rotation)
        {
            List<IntVec3> cells = WindTurbineUtility.CalculateWindCells(IntVec3.Zero, rotation, def.Size).ToList();
            if (cells.Count == 0)
                return "RimWorldAccess.Building.Architect.WindTurbineClearSpaceGeneric".Translate();

            // The two clear-space zones fall either side of the turbine along its facing axis.
            // Depth is the number of distinct coordinates, not the raw cell count, since each depth
            // also spans several cells across the axis.
            bool horizontal = rotation.IsHorizontal;
            var positiveDepths = new HashSet<int>();
            var negativeDepths = new HashSet<int>();
            foreach (IntVec3 cell in cells)
            {
                int coord = horizontal ? cell.x : cell.z;
                if (coord > 0) positiveDepths.Add(coord);
                else if (coord < 0) negativeDepths.Add(coord);
            }

            IntVec3 positiveOffset = horizontal ? new IntVec3(1, 0, 0) : new IntVec3(0, 0, 1);
            IntVec3 negativeOffset = horizontal ? new IntVec3(-1, 0, 0) : new IntVec3(0, 0, -1);

            string positiveSide = "RimWorldAccess.Building.Architect.TilesDirection".Translate(positiveDepths.Count, GetDirectionName(positiveOffset));
            string negativeSide = "RimWorldAccess.Building.Architect.TilesDirection".Translate(negativeDepths.Count, GetDirectionName(negativeOffset));

            return "RimWorldAccess.Building.Architect.WindTurbineClearSpace".Translate(positiveSide, negativeSide);
        }

        /// <summary>The direction name for an IntVec3 offset.</summary>
        private static string GetDirectionName(IntVec3 offset)
        {
            return BuildingCellHelper.GetCardinalDirection(offset) ?? "unknown";
        }

        /// <summary>Describes the building's size and occupied tiles, via GenAdj.OccupiedRect.</summary>
        internal static string GetSizeDescription(IntVec2 size, Rot4 rotation)
        {
            IntVec3 cursorPosition = MapNavigationState.CurrentCursorPosition;

            CellRect occupiedRect = GenAdj.OccupiedRect(cursorPosition, rotation, size);

            int width = occupiedRect.Width;
            int depth = occupiedRect.Height;

            if (width == 1 && depth == 1)
            {
                return "RimWorldAccess.Building.Place.SizeOneTile".Translate();
            }

            var builder = new AnnouncementBuilder();
            builder.Add("RimWorldAccess.Building.Architect.SizeWxH".Translate(width, depth));

            if (width > 1 || depth > 1)
            {
                List<string> directions = new List<string>();

                int northTiles = occupiedRect.maxZ - cursorPosition.z;
                int southTiles = cursorPosition.z - occupiedRect.minZ;
                int eastTiles = occupiedRect.maxX - cursorPosition.x;
                int westTiles = cursorPosition.x - occupiedRect.minX;

                if (northTiles > 0)
                    directions.Add("RimWorldAccess.Building.Architect.ExtendDirection".Translate(northTiles, "RimWorldAccess.Map.Direction.Lower.North".Translate()));
                if (southTiles > 0)
                    directions.Add("RimWorldAccess.Building.Architect.ExtendDirection".Translate(southTiles, "RimWorldAccess.Map.Direction.Lower.South".Translate()));
                if (eastTiles > 0)
                    directions.Add("RimWorldAccess.Building.Architect.ExtendDirection".Translate(eastTiles, "RimWorldAccess.Map.Direction.Lower.East".Translate()));
                if (westTiles > 0)
                    directions.Add("RimWorldAccess.Building.Architect.ExtendDirection".Translate(westTiles, "RimWorldAccess.Map.Direction.Lower.West".Translate()));

                if (directions.Count > 0)
                    builder.Add("RimWorldAccess.Building.Architect.Extends".Translate(string.Join(", ", directions)));
            }

            return builder.Build();
        }

        internal static string GetRotationName(Rot4 rotation)
        {
            if (rotation == Rot4.North) return "RimWorldAccess.Map.Direction.North".Translate();
            if (rotation == Rot4.East) return "RimWorldAccess.Map.Direction.East".Translate();
            if (rotation == Rot4.South) return "RimWorldAccess.Map.Direction.South".Translate();
            if (rotation == Rot4.West) return "RimWorldAccess.Map.Direction.West".Translate();
            return rotation.ToString();
        }

        /// <summary>Adds a cell to the selection when the designator allows it; zone designators must stay adjacent.</summary>
        public static void ToggleCell(IntVec3 cell)
        {
            if (selectedDesignator == null)
                return;

            if (RectDesignationRouter.IsRectDesignator(selectedDesignator))
            {
                // This designator consumes a rectangle, and Space already routed to the single-cell
                // rect path.
                return;
            }

            AcceptanceReport report = selectedDesignator.CanDesignateCell(cell);
            bool isZone = ShapeHelper.IsZoneDesignator(selectedDesignator);

            if (selectedCells.Contains(cell))
            {
                if (isZone && selectedCells.Count > 1 && WouldDisconnectSelection(cell))
                {
                    TolkHelper.Speak("RimWorldAccess.Building.Create.CannotRemoveDisconnect".Loc());
                    return;
                }
                selectedCells.Remove(cell);
                TolkHelper.Speak("RimWorldAccess.Building.Paint.Deselected".Loc(cell.x, cell.z));
            }
            else if (report.Accepted)
            {
                if (isZone)
                {
                    Map map = Find.CurrentMap;
                    if (map != null)
                    {
                        // CanDesignateCell allows same-type zones, so check every zone.
                        Zone existingZone = map.zoneManager.ZoneAt(cell);
                        if (existingZone != null)
                        {
                            TolkHelper.Speak("RimWorldAccess.Building.Create.CellAlreadyInZone".Loc(existingZone.label));
                            return;
                        }
                    }

                    if (selectedCells.Count > 0 && !IsAdjacentToSelection(cell))
                    {
                        TolkHelper.Speak("RimWorldAccess.Building.Create.MustBeAdjacentToSelection".Loc());
                        return;
                    }
                }
                selectedCells.Add(cell);
                TolkHelper.SpeakData(MapNavigationState.FormatSelectedCell(cell));
            }
            else
            {
                string reason = report.Reason ?? "RimWorldAccess.Building.Architect.CannotDesignateHere".Translate();
                TolkHelper.Speak("RimWorldAccess.Building.Architect.Invalid".Loc(reason));
            }
        }

        private static bool IsAdjacentToSelection(IntVec3 cell)
        {
            for (int i = 0; i < 4; i++)
            {
                IntVec3 neighbor = cell + GenAdj.CardinalDirections[i];
                if (selectedCells.Contains(neighbor))
                    return true;
            }
            return false;
        }

        /// <summary>Whether removing a cell would disconnect the remaining selection.</summary>
        private static bool WouldDisconnectSelection(IntVec3 cellToRemove)
        {
            var remaining = new HashSet<IntVec3>(selectedCells);
            remaining.Remove(cellToRemove);

            if (remaining.Count == 0)
                return false;

            var visited = new HashSet<IntVec3>();
            var queue = new Queue<IntVec3>();
            var startCell = remaining.First();

            queue.Enqueue(startCell);
            visited.Add(startCell);

            while (queue.Count > 0)
            {
                var current = queue.Dequeue();
                for (int i = 0; i < 4; i++)
                {
                    var neighbor = current + GenAdj.CardinalDirections[i];
                    if (remaining.Contains(neighbor) && !visited.Contains(neighbor))
                    {
                        visited.Add(neighbor);
                        queue.Enqueue(neighbor);
                    }
                }
            }

            return visited.Count < remaining.Count;
        }

        /// <summary>Designates every selected cell.</summary>
        public static void ExecutePlacement(Map map)
        {
            if (selectedDesignator == null || selectedCells.Count == 0)
            {
                TolkHelper.Speak("RimWorldAccess.Building.Place.NoCellsSelected".Loc());
                Cancel();
                return;
            }

            try
            {
                selectedDesignator.DesignateMultiCell(selectedCells);

                string toolName = selectedDesignator.Label;
                TolkHelper.Speak("RimWorldAccess.Building.Architect.PlacedOnCells".Loc(toolName, selectedCells.Count));
                ModLogger.Dev($"Executed placement: {toolName} on {selectedCells.Count} cells");
            }
            catch (System.Exception ex)
            {
                TolkHelper.Speak("RimWorldAccess.Building.Architect.ErrorPlacing".Loc(ex.Message), SpeechPriority.High);
                Log.Error($"Error in ExecutePlacement: {ex}");
            }
            finally
            {
                Reset();
            }
        }

        /// <summary>Cancels the current operation and fully exits architect mode.</summary>
        public static void Cancel()
        {
            TolkHelper.Speak("RimWorldAccess.Building.Architect.MenuClosed".Loc());

            Reset();
        }

        /// <summary>Whether the current designator is zone-, area-, or otherwise cell-based.</summary>
        public static bool IsZoneDesignator()
        {
            return ShapeHelper.IsCellsDesignator(selectedDesignator) || ShapeHelper.IsZoneDesignator(selectedDesignator);
        }

        /// <summary>Resets the architect state; idempotent.</summary>
        public static void Reset()
        {
            if (currentMode == ArchitectMode.Inactive)
            {
                return;
            }

            // Inactive BEFORE Deselect(), or DesignatorManagerDeselectPatch loops.
            currentMode = ArchitectMode.Inactive;
            selectedCategory = null;
            selectedDesignator = null;
            selectedBuildable = null;
            selectedMaterial = null;
            selectedCells.Clear();
            currentRotation = Rot4.North;
            selectionMode = ArchitectSelectionMode.BoxSelection; // Reset to default mode

            if (Find.DesignatorManager != null)
            {
                Find.DesignatorManager.Deselect();
            }

            // A stale MinifiedThing selection from an inventory install would skew gizmo visibility.
            if (Find.Selector?.SingleSelectedThing is MinifiedThing)
            {
                Find.Selector.ClearSelection();
            }

            ModLogger.Dev("Architect state reset");
        }
    }
}
