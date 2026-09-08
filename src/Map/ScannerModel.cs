using System;
using System.Collections.Generic;
using System.Linq;
using RimWorld;
using Verse;

namespace RimWorldAccess
{
    /// <summary>
    /// Represents a contiguous region of terrain tiles (e.g., a patch of rich soil).
    /// Used for adjacency-based grouping in the scanner.
    /// </summary>
    public class TerrainRegion
    {
        public IntVec3 CenterPosition { get; set; }
        public int TileCount { get; set; }
        public string Dimensions { get; set; } // "4x3" for rectangular shapes, null otherwise
        public List<IntVec3> AllPositions { get; set; }
        public float Distance { get; set; }
        public int? TotalQuantity { get; set; }  // For deep ore deposits

        /// <summary>
        /// Gets a human-readable size description ("4x3" or "12 tiles").
        /// </summary>
        public string SizeDescription => Dimensions ?? (string)"RimWorldAccess.Map.Scanner.Region.SizeTiles".Translate(TileCount);

        public TerrainRegion(List<IntVec3> positions, IntVec3 cursorPosition)
        {
            AllPositions = positions;
            TileCount = positions.Count;
            CenterPosition = CalculateCenter(positions);
            Dimensions = CalculateDimensions(positions);
            Distance = (CenterPosition - cursorPosition).LengthHorizontal;
        }

        /// <summary>
        /// Constructor for deep ore regions that tracks quantity per cell.
        /// </summary>
        public TerrainRegion(List<(IntVec3 position, int count)> positionsWithCounts, IntVec3 cursorPosition)
        {
            AllPositions = positionsWithCounts.Select(p => p.position).ToList();
            TileCount = AllPositions.Count;
            TotalQuantity = positionsWithCounts.Sum(p => p.count);
            CenterPosition = CalculateCenter(AllPositions);
            Dimensions = CalculateDimensions(AllPositions);
            Distance = (CenterPosition - cursorPosition).LengthHorizontal;
        }

        /// <summary>
        /// Calculates the center of a region, preferring a position that's actually in the region.
        /// </summary>
        private static IntVec3 CalculateCenter(List<IntVec3> positions)
        {
            if (positions.Count == 0)
                return IntVec3.Invalid;

            // Calculate centroid with proper rounding (not truncation)
            int sumX = 0, sumZ = 0;
            foreach (var pos in positions)
            {
                sumX += pos.x;
                sumZ += pos.z;
            }
            // Use Math.Round to avoid systematic bias from integer truncation
            int avgX = (int)Math.Round((double)sumX / positions.Count);
            int avgZ = (int)Math.Round((double)sumZ / positions.Count);
            var centroid = new IntVec3(avgX, 0, avgZ);

            // If centroid is in region, use it
            if (positions.Contains(centroid))
                return centroid;

            // Otherwise find the closest position to the centroid
            IntVec3 closest = positions[0];
            float closestDist = float.MaxValue;
            foreach (var pos in positions)
            {
                float dist = (pos - centroid).LengthHorizontal;
                if (dist < closestDist)
                {
                    closestDist = dist;
                    closest = pos;
                }
            }
            return closest;
        }

        /// <summary>
        /// Calculates dimensions if the region is rectangular ("4x3"), otherwise returns null.
        /// </summary>
        private static string CalculateDimensions(List<IntVec3> positions)
        {
            if (positions.Count == 0)
                return null;

            // Calculate bounding box
            int minX = int.MaxValue, maxX = int.MinValue;
            int minZ = int.MaxValue, maxZ = int.MinValue;

            foreach (var pos in positions)
            {
                if (pos.x < minX) minX = pos.x;
                if (pos.x > maxX) maxX = pos.x;
                if (pos.z < minZ) minZ = pos.z;
                if (pos.z > maxZ) maxZ = pos.z;
            }

            int width = maxX - minX + 1;
            int height = maxZ - minZ + 1;

            // If tile count equals area, it's rectangular
            if (positions.Count == width * height)
            {
                // Return dimensions with larger dimension first for consistency
                if (width >= height)
                    return $"{width}x{height}";
                else
                    return $"{height}x{width}";
            }

            return null; // Irregular shape
        }
    }

    public class ScannerItem
    {
        public Thing Thing { get; set; }
        public List<Thing> BulkThings { get; set; } // For grouped items of the same type
        public List<IntVec3> BulkTerrainPositions { get; set; } // For grouped terrain tiles
        public Designation Designation { get; set; } // For designation items
        public List<Designation> BulkDesignations { get; set; } // For grouped designations of the same type
        public List<TerrainRegion> TerrainRegions { get; set; } // For adjacency-grouped terrain regions
        public float Distance { get; set; }
        public string Label { get; set; }
        public IntVec3 Position { get; set; }
        public bool IsTerrain { get; set; } // True if this represents terrain instead of a Thing
        public bool IsDesignation => Designation != null; // True if this represents a designation
        public Zone Zone { get; set; } // For zone items
        public bool IsZone => Zone != null; // True if this represents a zone
        public Room Room { get; set; } // For room items
        public bool IsRoom => Room != null; // True if this represents a room
        public Plan Plan { get; set; } // For plan-marker items
        public bool IsPlan => Plan != null; // True if this represents a plan

        // Already-localized fragment appended after the item's distance and direction, for
        // entries that carry a state the distance alone doesn't convey (e.g. the facing a
        // building must be turned to for a placement spot to work).
        public string DetailSuffix { get; set; }

        // Holding platform reference for captured Anomaly entities. When set, Thing is the
        // held pawn (not spawned on the map) and Position is the platform's position so
        // navigation/jump behavior works.
        public Building_HoldingPlatform HoldingPlatform { get; set; }
        public bool IsCapturedEntity => HoldingPlatform != null;
        public bool HasTerrainRegions => TerrainRegions != null && TerrainRegions.Count > 0;
        public int RegionCount => TerrainRegions?.Count ?? 0;
        public int TotalTileCount => TerrainRegions?.Sum(r => r.TileCount) ?? BulkTerrainPositions?.Count ?? 1;
        public int BulkCount => BulkThings?.Count ?? (BulkTerrainPositions?.Count ?? (BulkDesignations?.Count ?? (TerrainRegions?.Count ?? 1)));
        public int LiveCount
        {
            get
            {
                if (BulkThings != null)
                    return LiveBulkThings.Count();
                if (Thing != null)
                    return Thing.Destroyed ? 0 : 1;
                return BulkCount;
            }
        }
        public bool IsBulkGroup => (BulkThings != null && BulkThings.Count > 1) ||
                                   (BulkTerrainPositions != null && BulkTerrainPositions.Count > 1) ||
                                   (BulkDesignations != null && BulkDesignations.Count > 1) ||
                                   (TerrainRegions != null && TerrainRegions.Count > 1);

        // Deep ore deposit properties
        public ThingDef DeepOreDef { get; set; }
        public int TotalQuantityAcrossRegions => TerrainRegions?
            .Where(r => r.TotalQuantity.HasValue)
            .Sum(r => r.TotalQuantity.Value) ?? 0;
        public bool HasQuantityInfo => TerrainRegions?.Any(r => r.TotalQuantity.HasValue) ?? false;

        // Set to true by RefreshLabel when the underlying Thing has been destroyed or despawned.
        // Callers should check this after RefreshLabel() and skip/remove stale items.
        public bool IsStale { get; private set; }

        // Live view over BulkThings that filters out destroyed/despawned entries.
        // Returns an empty enumerable when BulkThings is null.
        public IEnumerable<Thing> LiveBulkThings =>
            BulkThings?.Where(t => t != null && !t.Destroyed && t.Spawned) ?? Enumerable.Empty<Thing>();

        public ScannerItem(Thing thing, IntVec3 cursorPosition)
        {
            Thing = thing;
            Position = thing.Position;
            Distance = (thing.Position - cursorPosition).LengthHorizontal;
            IsTerrain = false;
            Label = ScannerLabelBuilder.BuildThingLabel(thing);
        }

        // Constructor for bulk groups
        public ScannerItem(List<Thing> things, IntVec3 cursorPosition)
        {
            if (things == null || things.Count == 0)
                throw new ArgumentException("Bulk group must contain at least one thing");

            BulkThings = things;
            Thing = things[0]; // Primary thing (closest)
            Position = Thing.Position;
            Distance = (Thing.Position - cursorPosition).LengthHorizontal;
            IsTerrain = false;
            Label = ScannerLabelBuilder.BuildThingLabel(Thing);
        }

        // Constructor for terrain tiles (no actual Thing object)
        public ScannerItem(IntVec3 cell, string label, IntVec3 cursorPosition)
        {
            Thing = null;
            Position = cell;
            Distance = (cell - cursorPosition).LengthHorizontal;
            Label = label;
            IsTerrain = true;
        }

        // Constructor for grouped terrain tiles (legacy - non-adjacent grouping)
        public ScannerItem(List<IntVec3> positions, string label, IntVec3 cursorPosition)
        {
            if (positions == null || positions.Count == 0)
                throw new ArgumentException("Terrain group must contain at least one position");

            Thing = null;
            BulkTerrainPositions = positions;
            Position = positions[0]; // Primary position (closest)
            Distance = (positions[0] - cursorPosition).LengthHorizontal;
            Label = label;
            IsTerrain = true;
        }

        // Constructor for adjacency-grouped terrain regions (e.g., separate patches of rich soil)
        public ScannerItem(List<TerrainRegion> regions, string label, IntVec3 cursorPosition)
        {
            if (regions == null || regions.Count == 0)
                throw new ArgumentException("Terrain regions list must contain at least one region");

            Thing = null;
            TerrainRegions = regions;
            // Position is the center of the closest region
            Position = regions[0].CenterPosition;
            Distance = regions[0].Distance;
            Label = label;
            IsTerrain = true;
        }

        // Constructor for adjacency-grouped mineable regions (ore/rock with Thing reference)
        public ScannerItem(List<TerrainRegion> regions, string label, IntVec3 cursorPosition, Thing primaryThing)
        {
            if (regions == null || regions.Count == 0)
                throw new ArgumentException("Mineable regions list must contain at least one region");

            Thing = primaryThing; // Keep reference for def info
            TerrainRegions = regions;
            // Position is the center of the closest region
            Position = regions[0].CenterPosition;
            Distance = regions[0].Distance;
            Label = label;
            IsTerrain = false; // Mineables are Things, not terrain
        }

        // Constructor for deep ore deposit regions with quantity tracking
        public ScannerItem(List<TerrainRegion> regions, ThingDef oreDef, IntVec3 cursorPosition)
        {
            if (regions == null || regions.Count == 0)
                throw new ArgumentException("Deep ore regions list must contain at least one region");

            Thing = null;
            DeepOreDef = oreDef;
            TerrainRegions = regions;
            Position = regions[0].CenterPosition;
            Distance = regions[0].Distance;
            Label = "RimWorldAccess.Map.Scanner.DeepOre.Deposit".Translate(oreDef.label);
            IsTerrain = true; // Treat as terrain-like for navigation
        }

        // Constructor for captured Anomaly entities held on a holding platform. The held pawn
        // is not spawned on the map, so we take the platform's position for navigation while
        // keeping the pawn as Thing for label/announcement purposes.
        public ScannerItem(Pawn heldPawn, Building_HoldingPlatform platform, IntVec3 cursorPosition)
        {
            Thing = heldPawn;
            HoldingPlatform = platform;
            Position = platform.Position;
            Distance = (Position - cursorPosition).LengthHorizontal;
            IsTerrain = false;
            Label = ScannerLabelBuilder.BuildThingLabel(heldPawn);
        }

        // Constructor for designation items
        public ScannerItem(Designation designation, IntVec3 cursorPosition)
        {
            Designation = designation;
            Position = designation.target.Cell;
            Distance = (Position - cursorPosition).LengthHorizontal;
            IsTerrain = false;
            Thing = designation.target.HasThing ? designation.target.Thing : null;
            Label = ScannerLabelBuilder.BuildDesignationLabel(designation, Find.CurrentMap);
        }

        // Constructor for grouped designations (same type)
        public ScannerItem(List<Designation> designations, IntVec3 cursorPosition)
        {
            if (designations == null || designations.Count == 0)
                throw new ArgumentException("Designation group must contain at least one designation");

            BulkDesignations = designations;
            Designation = designations[0]; // Primary designation (closest)
            Position = Designation.target.Cell;
            Distance = (Position - cursorPosition).LengthHorizontal;
            IsTerrain = false;
            Thing = Designation.target.HasThing ? Designation.target.Thing : null;

            // Get localized label from the Designator
            Label = ScannerHelper.GetLocalizedDesignationLabel(Designation.def);
        }

        // Constructor for zone items
        public ScannerItem(Zone zone, IntVec3 cursorPosition)
        {
            Zone = zone;
            IsTerrain = false;

            // Calculate center position of zone, ensuring it's within the zone for irregular shapes
            if (zone.cells != null && zone.cells.Count > 0)
            {
                int sumX = 0, sumZ = 0;
                foreach (var c in zone.cells) { sumX += c.x; sumZ += c.z; }
                int avgX = (int)Math.Round((double)sumX / zone.cells.Count);
                int avgZ = (int)Math.Round((double)sumZ / zone.cells.Count);
                var centerCandidate = new IntVec3(avgX, 0, avgZ);
                if (zone.cells.Contains(centerCandidate))
                {
                    Position = centerCandidate;
                }
                else
                {
                    IntVec3 closest = zone.cells[0];
                    float closestDist = float.MaxValue;
                    foreach (var c in zone.cells)
                    {
                        float dist = (c - centerCandidate).LengthHorizontal;
                        if (dist < closestDist) { closestDist = dist; closest = c; }
                    }
                    Position = closest;
                }
            }
            else
            {
                Position = zone.Position; // Fallback to first cell
            }

            Distance = (Position - cursorPosition).LengthHorizontal;
            Label = ScannerLabelBuilder.BuildZoneLabel(zone);
        }

        // Constructor for room items
        public ScannerItem(Room room, IntVec3 cursorPosition)
        {
            Room = room;
            IsTerrain = false;

            // Calculate center position of room, ensuring it's within the room for irregular shapes
            var cells = room.Cells.ToList();
            if (cells.Count > 0)
            {
                int sumX = 0, sumZ = 0;
                foreach (var c in cells) { sumX += c.x; sumZ += c.z; }
                int avgX = (int)Math.Round((double)sumX / cells.Count);
                int avgZ = (int)Math.Round((double)sumZ / cells.Count);
                var centerCandidate = new IntVec3(avgX, 0, avgZ);
                if (cells.Contains(centerCandidate))
                {
                    Position = centerCandidate;
                }
                else
                {
                    IntVec3 closest = cells[0];
                    float closestDist = float.MaxValue;
                    foreach (var c in cells)
                    {
                        float dist = (c - centerCandidate).LengthHorizontal;
                        if (dist < closestDist) { closestDist = dist; closest = c; }
                    }
                    Position = closest;
                }
            }
            else
            {
                Position = IntVec3.Zero;
            }

            Distance = (Position - cursorPosition).LengthHorizontal;
            Label = ScannerLabelBuilder.BuildRoomLabel(room);
        }

        // Constructor for plan-marker items. Each Plan is already a single contiguous, single-color
        // region (the game splits non-contiguous pieces into separate Plan objects on edit), so we
        // wrap its cells in one TerrainRegion. That lets a plan ride the same Home edge/center clump
        // navigation and "NxM / N tiles" announcements as natural terrain patches.
        public ScannerItem(Plan plan, IntVec3 cursorPosition)
        {
            Plan = plan;
            IsTerrain = false;
            var region = new TerrainRegion(plan.Cells.ToList(), cursorPosition);
            TerrainRegions = new List<TerrainRegion> { region };
            Position = region.CenterPosition;
            Distance = region.Distance;
            Label = ScannerLabelBuilder.BuildPlanLabel(plan);
        }

        /// <summary>
        /// Re-derives the label from the live game object.
        /// Call before announcing to get fresh labels without a full scanner refresh.
        /// Sets IsStale = true if the underlying Thing has been destroyed or despawned.
        /// </summary>
        public void RefreshLabel()
        {
            IsStale = false;

            if (IsCapturedEntity)
            {
                // Captured entity is not Spawned itself — its liveness is tied to the platform
                // still existing on the map and still holding this same pawn.
                if (HoldingPlatform == null || HoldingPlatform.Destroyed || !HoldingPlatform.Spawned
                    || HoldingPlatform.HeldPawn != Thing)
                {
                    IsStale = true;
                    return;
                }

                Label = ScannerLabelBuilder.BuildThingLabel(Thing);
            }
            else if (Thing != null)
            {
                if (Thing.Destroyed || !Thing.Spawned)
                {
                    IsStale = true;
                    return;
                }

                Label = ScannerLabelBuilder.BuildThingLabel(Thing);
            }
            else if (Zone != null)
            {
                Label = ScannerLabelBuilder.BuildZoneLabel(Zone);
            }
            else if (Room != null)
            {
                Label = ScannerLabelBuilder.BuildRoomLabel(Room);
            }
            else if (Plan != null)
            {
                Label = ScannerLabelBuilder.BuildPlanLabel(Plan);
            }
            // Terrain and designations: labels derived from defs, don't change
        }

    }

    public class ScannerSubcategory : IScannerSubcategory<ScannerItem>
    {
        public string Name { get; set; }
        public List<ScannerItem> Items { get; set; }

        public ScannerSubcategory(string name)
        {
            Name = name;
            Items = new List<ScannerItem>();
        }

        public bool IsEmpty => Items == null || Items.Count == 0;

        public int LiveCount => Items?.Sum(i => i.LiveCount) ?? 0;
    }

    public class ScannerCategory : IScannerCategory<ScannerSubcategory>
    {
        public string Name { get; set; }
        public List<ScannerSubcategory> Subcategories { get; set; }

        public ScannerCategory(string name)
        {
            Name = name;
            Subcategories = new List<ScannerSubcategory>();
        }

        /// <summary>
        /// Creates a ScannerCategory with an "All" subcategory pre-inserted at index 0.
        /// Items added to any specialized subcategory should also be added to Subcategories[0]
        /// (via the AddTo helper in ScannerHelper) so the "All" subcategory mirrors the whole category.
        /// </summary>
        public static ScannerCategory Create(string name)
        {
            var cat = new ScannerCategory(name);
            cat.Subcategories.Add(new ScannerSubcategory($"{name}-All"));
            return cat;
        }

        /// <summary>
        /// The "All" subcategory (convention: Subcategories[0]). Returns null if the category was
        /// built without Create() and has no All subcategory yet.
        /// </summary>
        public ScannerSubcategory AllSubcategory =>
            Subcategories != null && Subcategories.Count > 0 ? Subcategories[0] : null;

        public bool IsEmpty => Subcategories == null || Subcategories.All(sc => sc.IsEmpty);

        public int LiveCount => AllSubcategory?.LiveCount ?? 0;
    }
}
