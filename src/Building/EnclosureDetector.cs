using System;
using System.Collections.Generic;
using Verse;
using RimWorld;

namespace RimWorldAccess
{
    /// <summary>An enclosure formed by wall blueprints.</summary>
    public class Enclosure
    {
        /// <summary>The interior cells.</summary>
        public List<IntVec3> InteriorCells { get; set; }

        /// <summary>Obstacles found inside.</summary>
        public List<ScannerItem> Obstacles { get; set; }

        /// <summary>Failed wall placements on this perimeter: the gaps that stop the room sealing.</summary>
        public List<IntVec3> GapCells { get; set; }

        public int CellCount => InteriorCells?.Count ?? 0;

        public int ObstacleCount => Obstacles?.Count ?? 0;

        public int GapCount => GapCells?.Count ?? 0;

        public bool HasGaps => GapCount > 0;
    }

    /// <summary>Detects enclosed areas formed by wall blueprints, flood-filling for interior cells bounded by walls.</summary>
    public static class EnclosureDetector
    {
        // Performance limit: detection is skipped for very large areas.
        private const int MAX_ENCLOSURE_CELLS = 10000;

        /// <summary>
        /// Detects enclosures formed by wall blueprints together with existing walls and mountains,
        /// including the perimeter gaps that failed placements leave behind.
        /// </summary>
        public static List<Enclosure> DetectEnclosures(List<Thing> blueprints, Map map, List<IntVec3> failedCells = null)
        {
            if (map == null || blueprints == null || blueprints.Count == 0)
                return new List<Enclosure>();

            var wallCells = new HashSet<IntVec3>();
            foreach (Thing blueprint in blueprints)
            {
                if (IsWallBlueprint(blueprint))
                    wallCells.Add(blueprint.Position);
            }

            if (wallCells.Count == 0)
                return new List<Enclosure>();

            var failedCellSet = new HashSet<IntVec3>();
            if (failedCells != null)
            {
                foreach (var cell in failedCells)
                    failedCellSet.Add(cell);
            }

            var candidates = FindCandidateInteriorCells(wallCells, map);

            var enclosures = new List<Enclosure>();
            var processedCells = new HashSet<IntVec3>();
            var cursorPos = MapNavigationState.CurrentCursorPosition;

            foreach (IntVec3 candidate in candidates)
            {
                if (processedCells.Contains(candidate))
                    continue;

                var (isEnclosed, interiorCells) = TryFloodFill(candidate, wallCells, map);

                foreach (var cell in interiorCells)
                    processedCells.Add(cell);

                if (isEnclosed && interiorCells.Count > 0)
                {
                    var obstacles = ObstacleDetector.FindObstacles(map, interiorCells, cursorPos);

                    var gapCells = FindPerimeterGaps(interiorCells, wallCells, failedCellSet, map);

                    enclosures.Add(new Enclosure
                    {
                        InteriorCells = interiorCells,
                        Obstacles = obstacles,
                        GapCells = gapCells
                    });
                }
            }

            return enclosures;
        }

        /// <summary>Whether a thing is a wall blueprint or frame.</summary>
        private static bool IsWallBlueprint(Thing thing)
        {
            if (thing?.def == null)
                return false;

            if (!thing.def.IsBlueprint && !thing.def.IsFrame)
                return false;

            if (thing.def.entityDefToBuild is ThingDef thingDef)
            {
                if (thingDef.building?.isWall == true)
                    return true;

                if (thingDef.passability == Traversability.Impassable)
                    return true;
            }

            return false;
        }

        /// <summary>Cells adjacent to wall blueprints but not walls themselves: the interior-cell candidates.</summary>
        private static HashSet<IntVec3> FindCandidateInteriorCells(HashSet<IntVec3> wallCells, Map map)
        {
            var candidates = new HashSet<IntVec3>();

            foreach (IntVec3 wallCell in wallCells)
            {
                foreach (IntVec3 offset in GenAdj.CardinalDirections)
                {
                    IntVec3 neighbor = wallCell + offset;

                    if (!neighbor.InBounds(map))
                        continue;

                    if (wallCells.Contains(neighbor))
                        continue;

                    if (neighbor.Impassable(map))
                        continue;

                    candidates.Add(neighbor);
                }
            }

            return candidates;
        }

        /// <summary>
        /// Flood-fills from <paramref name="startCell"/> treating existing walls, impassable
        /// terrain, wall blueprints and wall frames as boundaries — how Ctrl+A picks up rooms
        /// enclosed by blueprint walls, which RimWorld's Room system ignores until they are built.
        /// isEnclosed is true when the fill stayed bounded: no map edge, no size-cap overrun.
        /// </summary>
        public static (bool isEnclosed, List<IntVec3> interiorCells) TryFloodFillFromCell(IntVec3 startCell, Map map)
        {
            if (map == null)
                return (false, new List<IntVec3>());

            return TryFloodFill(startCell, new HashSet<IntVec3>(), map);
        }

        /// <summary>Flood-fills from a start cell, reporting whether the area is enclosed and which cells it holds.</summary>
        private static (bool isEnclosed, List<IntVec3> cells) TryFloodFill(
            IntVec3 startCell,
            HashSet<IntVec3> wallBlueprintCells,
            Map map)
        {
            var foundCells = new List<IntVec3>();
            bool reachedOpenArea = false;

            // Whether a cell can be entered during the fill.
            Predicate<IntVec3> passCheck = (IntVec3 c) =>
            {
                if (!c.InBounds(map))
                    return false;

                if (wallBlueprintCells.Contains(c))
                    return false;

                if (c.Impassable(map))
                    return false;

                // Doors are wall segments for room detection even though pawns pass through them,
                // so they and their blueprints/frames count as boundaries.
                foreach (Thing thing in c.GetThingList(map))
                {
                    if (thing is Building && thing.def.building?.isWall == true)
                        return false;
                    if (thing.def.IsDoor)
                        return false;

                    if ((thing.def.IsBlueprint || thing.def.IsFrame) &&
                        thing.def.entityDefToBuild is ThingDef td &&
                        (td.building?.isWall == true || td.IsDoor || td.passability == Traversability.Impassable))
                        return false;
                }

                return true;
            };

            // Collects cells and watches for an escape into open area.
            Func<IntVec3, bool> processor = (IntVec3 c) =>
            {
                foundCells.Add(c);

                if (foundCells.Count >= MAX_ENCLOSURE_CELLS)
                {
                    reachedOpenArea = true;
                    return true; // Stop the flood fill
                }

                if (c.x == 0 || c.z == 0 || c.x == map.Size.x - 1 || c.z == map.Size.z - 1)
                {
                    reachedOpenArea = true;
                    return true; // Stop the flood fill
                }

                return false; // Continue filling
            };

            map.floodFiller.FloodFill(startCell, passCheck, processor);

            bool isEnclosed = !reachedOpenArea && foundCells.Count > 0;
            return (isEnclosed, foundCells);
        }

        /// <summary>
        /// The failed cells that would have been part of this enclosure's perimeter: a failed cell
        /// counts as a gap when 2+ cardinal neighbours are wall blueprints, meaning it was meant to
        /// connect walls at a corner or along the run.
        /// </summary>
        private static List<IntVec3> FindPerimeterGaps(
            List<IntVec3> interiorCells,
            HashSet<IntVec3> wallCells,
            HashSet<IntVec3> failedCells,
            Map map)
        {
            var gaps = new List<IntVec3>();

            if (failedCells == null || failedCells.Count == 0)
                return gaps;

            var interiorSet = new HashSet<IntVec3>(interiorCells);

            foreach (IntVec3 failedCell in failedCells)
            {
                if (!failedCell.InBounds(map))
                    continue;

                int adjacentWallCount = 0;
                bool isAdjacentToInterior = false;

                foreach (IntVec3 offset in GenAdj.CardinalDirections)
                {
                    IntVec3 neighbor = failedCell + offset;

                    if (wallCells.Contains(neighbor))
                        adjacentWallCount++;

                    if (interiorSet.Contains(neighbor))
                        isAdjacentToInterior = true;
                }

                // A gap is either 2+ adjacent wall blueprints (corner or along-wall) or one
                // adjacent wall plus adjacency to the interior (mid-wall).
                if (adjacentWallCount >= 2 || (adjacentWallCount >= 1 && isAdjacentToInterior))
                {
                    gaps.Add(failedCell);
                }
            }

            return gaps;
        }
    }
}
