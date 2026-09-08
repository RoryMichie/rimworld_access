using System;
using System.Collections.Generic;
using System.Linq;
using Verse;
using RimWorld;
using UnityEngine;

namespace RimWorldAccess
{
    /// <summary>Shape types for building placement, mapped onto RimWorld's DrawStyle system.</summary>
    public enum ShapeType
    {
        /// <summary>Single-cell placement (no shape, place one tile at a time)</summary>
        Manual,
        /// <summary>Orthogonal line - horizontal or vertical only</summary>
        Line,
        /// <summary>Diagonal line using Bresenham algorithm</summary>
        AngledLine,
        /// <summary>Filled rectangle - all cells within the rectangle</summary>
        FilledRectangle,
        /// <summary>Empty rectangle - border cells only (for walls)</summary>
        EmptyRectangle,
        /// <summary>Filled oval/ellipse - all cells within the ellipse</summary>
        FilledOval,
        /// <summary>Empty oval/ellipse - border cells only</summary>
        EmptyOval
    }

    /// <summary>
    /// The disjoint top-level kind a designator falls into for placement/viewing-mode dispatch,
    /// computed once by <see cref="ShapeHelper.ClassifyDesignator"/>. "Other" covers anything
    /// outside the named kinds, such as a plain Designator_Cells or a non-Build Designator_Place.
    /// </summary>
    public enum DesignatorKind
    {
        Other,
        Build,
        Order,
        Zone,
        Area,
        BuiltInArea
    }

    /// <summary>
    /// One designator's classification. <see cref="IsDelete"/> applies only when
    /// <see cref="Kind"/> is <see cref="DesignatorKind.Zone"/>: Designator_ZoneDelete extends
    /// Designator_Zone, so delete is a sub-kind rather than a disjoint kind of its own.
    /// </summary>
    public readonly struct DesignatorClassification
    {
        public DesignatorKind Kind { get; }
        public bool IsDelete { get; }

        public DesignatorClassification(DesignatorKind kind, bool isDelete)
        {
            Kind = kind;
            IsDelete = isDelete;
        }

        public bool IsBuild => Kind == DesignatorKind.Build;
        public bool IsOrder => Kind == DesignatorKind.Order;
        public bool IsZone => Kind == DesignatorKind.Zone;
        public bool IsArea => Kind == DesignatorKind.Area;
        public bool IsBuiltInArea => Kind == DesignatorKind.BuiltInArea;
    }

    /// <summary>
    /// Shape-based building placement over RimWorld's own DrawStyle classes: cell calculation,
    /// obstacle detection and shape selection.
    /// </summary>
    public static class ShapeHelper
    {
        private static readonly DrawStyle_Line lineStyle = new DrawStyle_Line();
        private static readonly DrawStyle_AngledLine angledLineStyle = new DrawStyle_AngledLine();
        private static readonly DrawStyle_FilledRectangle filledRectangleStyle = new DrawStyle_FilledRectangle();
        private static readonly DrawStyle_EmptyRectangle emptyRectangleStyle = new DrawStyle_EmptyRectangle();
        private static readonly DrawStyle_FilledOval filledOvalStyle = new DrawStyle_FilledOval();
        private static readonly DrawStyle_EmptyOval emptyOvalStyle = new DrawStyle_EmptyOval();

        // Reused across calls; CalculateShapeCells returns a copy.
        private static readonly List<IntVec3> cellBuffer = new List<IntVec3>();

        /// <summary>DrawStyle type names to ShapeType, for reading designator.DrawStyleCategory.styles.</summary>
        private static readonly Dictionary<Type, ShapeType> drawStyleTypeToShapeType = new Dictionary<Type, ShapeType>
        {
            { typeof(DrawStyle_Line), ShapeType.Line },
            { typeof(DrawStyle_AngledLine), ShapeType.AngledLine },
            { typeof(DrawStyle_FilledRectangle), ShapeType.FilledRectangle },
            { typeof(DrawStyle_EmptyRectangle), ShapeType.EmptyRectangle },
            { typeof(DrawStyle_FilledOval), ShapeType.FilledOval },
            { typeof(DrawStyle_EmptyOval), ShapeType.EmptyOval }
        };

        /// <summary>The cells of a shape between two points, via RimWorld's own DrawStyle classes.</summary>
        public static List<IntVec3> CalculateCells(ShapeType shape, IntVec3 origin, IntVec3 target)
        {
            cellBuffer.Clear();

            switch (shape)
            {
                case ShapeType.Manual:
                    cellBuffer.Add(target);
                    break;

                case ShapeType.Line:
                    lineStyle.Update(origin, target, cellBuffer);
                    break;

                case ShapeType.AngledLine:
                    angledLineStyle.Update(origin, target, cellBuffer);
                    break;

                case ShapeType.FilledRectangle:
                    filledRectangleStyle.Update(origin, target, cellBuffer);
                    break;

                case ShapeType.EmptyRectangle:
                    emptyRectangleStyle.Update(origin, target, cellBuffer);
                    break;

                case ShapeType.FilledOval:
                    filledOvalStyle.Update(origin, target, cellBuffer);
                    break;

                case ShapeType.EmptyOval:
                    emptyOvalStyle.Update(origin, target, cellBuffer);
                    break;

                default:
                    cellBuffer.Add(target);
                    break;
            }

            // A copy: the buffer is reused.
            return new List<IntVec3>(cellBuffer);
        }

        /// <summary>
        /// The shapes a designator offers, read from its DrawStyleCategory. Manual is always
        /// included: single-cell placement is valid for every designator.
        /// </summary>
        public static List<ShapeType> GetAvailableShapes(Designator designator)
        {
            var shapes = new List<ShapeType>();

            if (designator != null)
            {
                DrawStyleCategoryDef category = designator.DrawStyleCategory;
                if (category != null && category.styles != null && category.styles.Count > 0)
                {
                    foreach (DrawStyleDef styleDef in category.styles)
                    {
                        if (styleDef?.drawStyleType == null)
                            continue;

                        if (drawStyleTypeToShapeType.TryGetValue(styleDef.drawStyleType, out ShapeType shapeType))
                        {
                            if (!shapes.Contains(shapeType))
                            {
                                shapes.Add(shapeType);
                            }
                        }
                    }
                }
            }

            shapes.Add(ShapeType.Manual);

            return shapes;
        }

        /// <summary>Whether a designator has a DrawStyleCategory and designates single or multiple cells.</summary>
        public static bool SupportsShapes(Designator designator)
        {
            if (designator == null)
                return false;

            var shapes = GetAvailableShapes(designator);
            return shapes.Count > 1; // More than just Manual
        }

        /// <summary>Whether the designator places blueprints.</summary>
        public static bool IsBuildDesignator(Designator designator)
        {
            return designator is Designator_Build;
        }

        /// <summary>Whether the designator is a Place designator (Build or Install).</summary>
        public static bool IsPlaceDesignator(Designator designator)
        {
            return designator is Designator_Place;
        }

        /// <summary>Whether the designator is a Zone designator.</summary>
        public static bool IsZoneDesignator(Designator designator)
        {
            return designator is Designator_Zone;
        }

        /// <summary>
        /// Whether the designator removes cells rather than adding them (Designator_ZoneDelete and
        /// its shrink variant). Removal cannot hit obstacles, so callers skip obstacle detection.
        /// </summary>
        public static bool IsDeleteDesignator(Designator designator)
        {
            return designator is Designator_ZoneDelete;
        }

        /// <summary>
        /// Whether the designator expands or clears an allowed area. These need an area selected
        /// before placement can begin.
        /// </summary>
        public static bool IsAreaDesignator(Designator designator)
        {
            return designator is Designator_AreaAllowed;
        }

        /// <summary>Whether the designator operates on a fixed Area from the map's AreaManager (Snow/Sand, Roof, Home).</summary>
        public static bool IsBuiltInAreaDesignator(Designator designator)
        {
            if (designator == null)
                return false;

            return designator is Designator_AreaSnowClear ||
                   designator is Designator_AreaHome ||
                   designator is Designator_AreaBuildRoof ||
                   designator is Designator_AreaNoRoof ||
                   designator is Designator_AreaIgnoreRoof ||
                   designator is Designator_AreaPollutionClear;
        }

        /// <summary>Whether the designator is an area designator of either kind.</summary>
        public static bool IsAnyAreaDesignator(Designator designator)
        {
            return IsAreaDesignator(designator) || IsBuiltInAreaDesignator(designator);
        }

        /// <summary>
        /// Whether a built-in area designator adds cells rather than removing them. Classified by
        /// concrete vanilla type, never by substring: an area family name like "SnowClear" carries
        /// "Clear" in both its Expand and its Clear variant.
        /// </summary>
        public static bool IsBuiltInAreaExpanding(Designator designator)
        {
            if (designator == null)
                return true;

            // Designator_AreaBuildRoof and Designator_AreaNoRoof have no Expand/Clear subclasses;
            // each always adds.
            if (designator is Designator_AreaSnowClearExpand ||
                designator is Designator_AreaHomeExpand ||
                designator is Designator_AreaPollutionClearExpand ||
                designator is Designator_AreaBuildRoof ||
                designator is Designator_AreaNoRoof)
                return true;

            // Everything else removes cells, Designator_AreaIgnoreRoof included (it clears both
            // the BuildRoof and NoRoof grids).
            return false;
        }

        /// <summary>The designator's Area from the map's AreaManager, or null if it has none or there is no map.</summary>
        public static Area GetBuiltInAreaForDesignator(Designator designator, Map map)
        {
            if (designator == null || map?.areaManager == null)
                return null;

            if (designator is Designator_AreaSnowClear)
                return map.areaManager.SnowOrSandClear;
            if (designator is Designator_AreaHome)
                return map.areaManager.Home;
            if (designator is Designator_AreaBuildRoof)
                return map.areaManager.BuildRoof;
            if (designator is Designator_AreaNoRoof || designator is Designator_AreaIgnoreRoof)
                return map.areaManager.NoRoof;
            if (designator is Designator_AreaPollutionClear)
                return map.areaManager.PollutionClear; // Returns null if Biotech DLC not active

            return null;
        }

        /// <summary>
        /// Every built-in Area a designator writes to. Designator_AreaIgnoreRoof clears both the
        /// BuildRoof and NoRoof grids; every other built-in area designator touches exactly one.
        /// </summary>
        public static List<Area> GetBuiltInAreasForDesignator(Designator designator, Map map)
        {
            var areas = new List<Area>();

            if (designator == null || map?.areaManager == null)
                return areas;

            if (designator is Designator_AreaIgnoreRoof)
            {
                if (map.areaManager.BuildRoof != null)
                    areas.Add(map.areaManager.BuildRoof);
                if (map.areaManager.NoRoof != null)
                    areas.Add(map.areaManager.NoRoof);
                return areas;
            }

            Area area = GetBuiltInAreaForDesignator(designator, map);
            if (area != null)
                areas.Add(area);

            return areas;
        }

        /// <summary>Whether the designator is a Cells designator (multi-cell selection like Mine).</summary>
        public static bool IsCellsDesignator(Designator designator)
        {
            return designator is Designator_Cells;
        }

        /// <summary>Whether the designator targets Things at cells rather than the cells themselves (Hunt, Haul, Tame).</summary>
        public static bool IsOrderDesignator(Designator designator)
        {
            if (designator == null)
                return false;

            if (designator is Designator_Place)
                return false;
            if (IsCellsDesignator(designator))
                return false;
            if (IsZoneDesignator(designator))
                return false;
            if (IsAreaDesignator(designator))
                return false;
            if (IsBuiltInAreaDesignator(designator))
                return false;

            return designator.DrawStyleCategory != null;
        }

        /// <summary>
        /// Classifies a designator in one pass. Delete folds into
        /// <see cref="DesignatorClassification.IsDelete"/> rather than becoming a kind of its own,
        /// since Designator_ZoneDelete extends Designator_Zone. Priority order matches
        /// IsOrderDesignator's own exclusions: Build, Zone, Area, BuiltInArea and Cells/Place are
        /// all checked before falling through to Order.
        /// </summary>
        public static DesignatorClassification ClassifyDesignator(Designator designator)
        {
            if (designator == null)
                return new DesignatorClassification(DesignatorKind.Other, false);

            if (designator is Designator_Build)
                return new DesignatorClassification(DesignatorKind.Build, false);

            if (designator is Designator_Zone)
            {
                bool isDelete = designator is Designator_ZoneDelete;
                return new DesignatorClassification(DesignatorKind.Zone, isDelete);
            }

            if (designator is Designator_AreaAllowed)
                return new DesignatorClassification(DesignatorKind.Area, false);

            if (IsBuiltInAreaDesignator(designator))
                return new DesignatorClassification(DesignatorKind.BuiltInArea, false);

            if (designator is Designator_Place)
                return new DesignatorClassification(DesignatorKind.Other, false);

            if (IsCellsDesignator(designator))
                return new DesignatorClassification(DesignatorKind.Other, false);

            if (designator.DrawStyleCategory != null)
                return new DesignatorClassification(DesignatorKind.Order, false);

            return new DesignatorClassification(DesignatorKind.Other, false);
        }

        /// <summary>The designator's default shape, or Manual when it defines no DrawStyleCategory.</summary>
        public static ShapeType GetDefaultShape(Designator designator)
        {
            if (designator == null)
                return ShapeType.Manual;

            DrawStyleCategoryDef category = designator.DrawStyleCategory;
            if (category == null || category.styles == null || category.styles.Count == 0)
                return ShapeType.Manual;

            DrawStyleDef firstStyle = category.styles[0];
            if (firstStyle?.drawStyleType == null)
                return ShapeType.Manual;

            if (drawStyleTypeToShapeType.TryGetValue(firstStyle.drawStyleType, out ShapeType shapeType))
            {
                return shapeType;
            }

            return ShapeType.Manual;
        }

        /// <summary>The DrawStyleDef for a ShapeType, used to set the game's SelectedStyle on a pick.</summary>
        public static DrawStyleDef GetDrawStyleDef(Designator designator, ShapeType shape)
        {
            if (designator == null || shape == ShapeType.Manual)
                return null;

            DrawStyleCategoryDef category = designator.DrawStyleCategory;
            if (category == null || category.styles == null)
                return null;

            Type targetType = null;
            foreach (var kvp in drawStyleTypeToShapeType)
            {
                if (kvp.Value == shape)
                {
                    targetType = kvp.Key;
                    break;
                }
            }

            if (targetType == null)
                return null;

            return category.styles.FirstOrDefault(s => s?.drawStyleType == targetType);
        }

        /// <summary>A spoken name for a shape type.</summary>
        public static string GetShapeName(ShapeType shape)
        {
            switch (shape)
            {
                case ShapeType.Manual:          return "RimWorldAccess.Building.Shape.Name.Manual".Translate();
                case ShapeType.Line:            return "RimWorldAccess.Building.Shape.Name.Line".Translate();
                case ShapeType.AngledLine:      return "RimWorldAccess.Building.Shape.Name.AngledLine".Translate();
                case ShapeType.FilledRectangle: return "RimWorldAccess.Building.Shape.Name.FilledRectangle".Translate();
                case ShapeType.EmptyRectangle:  return "RimWorldAccess.Building.Shape.Name.EmptyRectangle".Translate();
                case ShapeType.FilledOval:      return "RimWorldAccess.Building.Shape.Name.FilledOval".Translate();
                case ShapeType.EmptyOval:       return "RimWorldAccess.Building.Shape.Name.EmptyOval".Translate();
                default:                        return shape.ToString();
            }
        }

        /// <summary>A spoken description of how a shape works.</summary>
        public static string GetShapeDescription(ShapeType shape)
        {
            switch (shape)
            {
                case ShapeType.Manual:          return "RimWorldAccess.Building.Shape.Desc.Manual".Translate();
                case ShapeType.Line:            return "RimWorldAccess.Building.Shape.Desc.Line".Translate();
                case ShapeType.AngledLine:      return "RimWorldAccess.Building.Shape.Desc.AngledLine".Translate();
                case ShapeType.FilledRectangle: return "RimWorldAccess.Building.Shape.Desc.FilledRectangle".Translate();
                case ShapeType.EmptyRectangle:  return "RimWorldAccess.Building.Shape.Desc.EmptyRectangle".Translate();
                case ShapeType.FilledOval:      return "RimWorldAccess.Building.Shape.Desc.FilledOval".Translate();
                case ShapeType.EmptyOval:       return "RimWorldAccess.Building.Shape.Desc.EmptyOval".Translate();
                default:                        return string.Empty;
            }
        }

        /// <summary>The DrawStyleDef's own localized label, falling back to the shape type name.</summary>
        public static string GetShapeName(DrawStyleDef styleDef)
        {
            if (styleDef == null)
                return "RimWorldAccess.Building.Shape.Name.Manual".Translate();

            if (!string.IsNullOrEmpty(styleDef.label))
                return styleDef.LabelCap;

            if (styleDef.drawStyleType != null &&
                drawStyleTypeToShapeType.TryGetValue(styleDef.drawStyleType, out ShapeType shapeType))
            {
                return GetShapeName(shapeType);
            }

            return styleDef.defName ?? (string)"RimWorldAccess.Building.Shape.Name.Unknown".Translate();
        }

        /// <summary>The ShapeType for a DrawStyleDef, or Manual when it maps to none.</summary>
        public static ShapeType DrawStyleDefToShapeType(DrawStyleDef styleDef)
        {
            if (styleDef?.drawStyleType == null)
                return ShapeType.Manual;

            if (drawStyleTypeToShapeType.TryGetValue(styleDef.drawStyleType, out ShapeType shapeType))
            {
                return shapeType;
            }

            return ShapeType.Manual;
        }

        /// <summary>
        /// The cell one tile BEFORE the next obstacle in a direction, so the cursor lands inside;
        /// null when the obstacle is adjacent.
        /// </summary>
        public static IntVec3? FindNextObstacle(IntVec3 start, Rot4 direction, Map map)
        {
            if (map == null)
                return null;

            IntVec3 offset = direction.FacingCell;

            IntVec3 current = start;
            IntVec3? lastValidCell = null;

            int maxDistance = Mathf.Max(map.Size.x, map.Size.z);

            for (int i = 1; i <= maxDistance; i++)
            {
                IntVec3 nextCell = start + (offset * i);

                if (!nextCell.InBounds(map))
                {
                    return lastValidCell;
                }

                if (IsObstacle(nextCell, map))
                {
                    if (i == 1)
                        return null;

                    return start + (offset * (i - 1));
                }

                lastValidCell = nextCell;
            }

            return lastValidCell;
        }

        /// <summary>Whether a cell holds a wall, blueprint, frame, or impassable terrain.</summary>
        public static bool IsObstacle(IntVec3 cell, Map map)
        {
            if (map == null || !cell.InBounds(map))
                return true;

            if (cell.Impassable(map))
                return true;

            List<Thing> things = map.thingGrid.ThingsListAtFast(cell);
            for (int i = 0; i < things.Count; i++)
            {
                Thing thing = things[i];
                if (thing == null)
                    continue;

                if (thing.def.IsBlueprint)
                    return true;

                if (thing.def.IsFrame)
                    return true;
            }

            TerrainDef terrain = cell.GetTerrain(map);
            if (terrain != null && terrain.passability == Traversability.Impassable)
                return true;

            return false;
        }

        /// <summary>The (width, height) of the box between two points.</summary>
        public static (int width, int height) GetDimensions(IntVec3 origin, IntVec3 target)
        {
            CellRect rect = CellRect.FromLimits(origin, target);
            return (rect.Width, rect.Height);
        }

        /// <summary>The dimensions between two points as "W by H".</summary>
        public static string FormatDimensions(IntVec3 origin, IntVec3 target)
        {
            var (width, height) = GetDimensions(origin, target);
            return $"{width} by {height}";
        }

        /// <summary>Whether the cells fill their bounding box exactly, with no holes or gaps.</summary>
        public static bool IsRegularRectangle(IEnumerable<IntVec3> cells)
        {
            if (cells == null || !cells.Any())
                return false;

            int minX = cells.Min(c => c.x);
            int maxX = cells.Max(c => c.x);
            int minZ = cells.Min(c => c.z);
            int maxZ = cells.Max(c => c.z);

            int width = maxX - minX + 1;
            int height = maxZ - minZ + 1;
            int expectedCount = width * height;
            int actualCount = cells.Count();

            return actualCount == expectedCount;
        }

        /// <summary>Shape size as "W by H" for a regular rectangle, "N cells" otherwise.</summary>
        public static string FormatShapeSize(IEnumerable<IntVec3> cells)
        {
            if (cells == null || !cells.Any())
                return "RimWorldAccess.Building.Shape.SizeZero".Translate();

            int count = cells.Count();

            if (count == 1)
                return "RimWorldAccess.Building.Shape.SizeOne".Translate();

            if (IsRegularRectangle(cells))
            {
                int minX = cells.Min(c => c.x);
                int maxX = cells.Max(c => c.x);
                int minZ = cells.Min(c => c.z);
                int maxZ = cells.Max(c => c.z);

                int width = maxX - minX + 1;
                int height = maxZ - minZ + 1;

                return "RimWorldAccess.Building.Shape.SizeWxH".Translate(width, height);
            }

            return "RimWorldAccess.Building.Shape.SizeMany".Translate(count);
        }

        /// <summary>Shape size from two corners; always dimensions, since the cells are not known mid-drag.</summary>
        public static string FormatShapeSizeFromCorners(IntVec3 corner1, IntVec3 corner2)
        {
            var (width, height) = GetDimensions(corner1, corner2);
            return "RimWorldAccess.Building.Shape.SizeWxH".Translate(width, height);
        }

        /// <summary>Whether the shape needs an origin and a target rather than a single point.</summary>
        public static bool RequiresTwoPoints(ShapeType shape)
        {
            return shape != ShapeType.Manual;
        }

        /// <summary>Whether the shape places cells on its perimeter only.</summary>
        public static bool IsBorderShape(ShapeType shape)
        {
            return shape == ShapeType.EmptyRectangle || shape == ShapeType.EmptyOval;
        }

        /// <summary>Whether the shape fills all its interior cells.</summary>
        public static bool IsFilledShape(ShapeType shape)
        {
            return shape == ShapeType.FilledRectangle || shape == ShapeType.FilledOval;
        }

        /// <summary>Whether the shape is a line type.</summary>
        public static bool IsLineShape(ShapeType shape)
        {
            return shape == ShapeType.Line || shape == ShapeType.AngledLine;
        }
    }
}
