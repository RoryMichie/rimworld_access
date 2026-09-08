using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using RimWorld;
using RimWorld.Planet;
using UnityEngine;
using Verse;

namespace RimWorldAccess
{
    /// <summary>A contiguous area of one biome.</summary>
    public class BiomeRegion
    {
        public PlanetTile CenterTile { get; set; }
        public int TileCount { get; set; }
        public string SizeDescription { get; set; }
        public float Distance { get; set; } // From the cursor to the center tile.
        internal int[] TileIds { get; set; } // Every tile in the region, for launch reachability checks.

        public BiomeRegion(PlanetTile centerTile, int tileCount)
        {
            CenterTile = centerTile;
            TileCount = tileCount;
            SizeDescription = "RimWorldAccess.WorldScanner.SizeApprox".Translate(tileCount);
        }

        public BiomeRegion(PlanetTile centerTile, HashSet<int> regionTiles)
        {
            CenterTile = centerTile;
            TileCount = regionTiles.Count;
            TileIds = regionTiles.ToArray();
            SizeDescription = "RimWorldAccess.WorldScanner.SizeApprox".Translate(TileCount);
        }
    }

    /// <summary>A contiguous run of road tiles.</summary>
    public class RoadSegment
    {
        public PlanetTile CenterTile { get; set; }
        public int TileCount { get; set; }
        public float Distance { get; set; }
        public float Length { get; set; } // Maximum extent of the segment.
        public string SizeDescription { get; set; }
        internal int[] TileIds { get; set; } // Every tile in the segment, for launch reachability checks.

        public RoadSegment(PlanetTile centerTile, int tileCount)
        {
            CenterTile = centerTile;
            TileCount = tileCount;
            SizeDescription = "RimWorldAccess.WorldScanner.SizeTiles".Translate(tileCount);
        }

        public RoadSegment(PlanetTile centerTile, HashSet<int> segmentTiles)
        {
            CenterTile = centerTile;
            TileCount = segmentTiles.Count;
            TileIds = segmentTiles.ToArray();
            Length = CalculateLength(segmentTiles);

            if (Length >= 3)
            {
                SizeDescription = "RimWorldAccess.WorldScanner.SizeTilesLong".Translate(TileCount, Length.ToString("F0"));
            }
            else
            {
                SizeDescription = "RimWorldAccess.WorldScanner.SizeTiles".Translate(TileCount);
            }
        }

        /// <summary>The segment's greatest tile-to-tile extent; 0 for a single tile.</summary>
        private static float CalculateLength(HashSet<int> segmentTiles)
        {
            if (segmentTiles.Count <= 1 || Find.WorldGrid == null)
                return 0f;

            var tilesList = segmentTiles.ToList();
            float maxDist = 0f;

            // The pairwise scan is quadratic, so long segments are sampled.
            var tilesToCheck = tilesList;
            if (tilesList.Count > 30)
            {
                var sampled = new List<int>();
                sampled.Add(tilesList[0]);
                sampled.Add(tilesList[tilesList.Count - 1]);
                for (int i = 0; i < tilesList.Count; i += tilesList.Count / 8)
                {
                    sampled.Add(tilesList[i]);
                }
                tilesToCheck = sampled.Distinct().ToList();
            }

            for (int i = 0; i < tilesToCheck.Count; i++)
            {
                for (int j = i + 1; j < tilesToCheck.Count; j++)
                {
                    var tileA = new PlanetTile(tilesToCheck[i], -1);
                    var tileB = new PlanetTile(tilesToCheck[j], -1);
                    float dist = Find.WorldGrid.ApproxDistanceInTiles(tileA, tileB);
                    if (dist > maxDist)
                        maxDist = dist;
                }
            }

            return maxDist;
        }
    }

    /// <summary>
    /// A scanner row: either a single world object or a type carrying several instances
    /// (biome regions, road segments).
    /// </summary>
    public class WorldScannerItem
    {
        public WorldObject WorldObject { get; set; }
        public PlanetTile Tile { get; set; }
        public string Label { get; set; }
        public string QuestName { get; set; }
        public Faction Faction { get; set; }

        public List<BiomeRegion> BiomeRegions { get; set; }
        public List<RoadSegment> RoadSegments { get; set; }

        public bool HasInstances => (BiomeRegions != null && BiomeRegions.Count > 1) ||
                                    (RoadSegments != null && RoadSegments.Count > 1);
        public int InstanceCount => BiomeRegions?.Count ?? RoadSegments?.Count ?? 1;

        /// <summary>Settlements, caravans, quest sites and other world objects.</summary>
        public WorldScannerItem(WorldObject worldObject)
        {
            WorldObject = worldObject;
            Tile = worldObject.Tile;
            Faction = worldObject.Faction;
            Label = worldObject.LabelShort ?? worldObject.Label ?? "Unknown".Translate().ToString();
        }

        public WorldScannerItem(string biomeName, List<BiomeRegion> regions)
        {
            Label = biomeName;
            BiomeRegions = regions;
            if (regions.Count > 0)
            {
                Tile = regions[0].CenterTile;
            }
        }

        public WorldScannerItem(string roadName, List<RoadSegment> segments)
        {
            Label = roadName;
            RoadSegments = segments;
            if (segments.Count > 0)
            {
                Tile = segments[0].CenterTile;
            }
        }

        /// <summary>Landmark tiles, which are not WorldObjects.</summary>
        public WorldScannerItem(string label, PlanetTile tile)
        {
            Label = label;
            Tile = tile;
        }

        /// <summary>The instance's center tile, the target navigation jumps to.</summary>
        public PlanetTile GetTileAtInstance(int instanceIndex)
        {
            if (BiomeRegions != null && instanceIndex < BiomeRegions.Count)
                return BiomeRegions[instanceIndex].CenterTile;
            if (RoadSegments != null && instanceIndex < RoadSegments.Count)
                return RoadSegments[instanceIndex].CenterTile;
            return Tile;
        }

        /// <summary>
        /// Every tile id in a region/segment instance, or null for single-tile items.
        /// </summary>
        internal int[] GetMemberTileIdsAtInstance(int instanceIndex)
        {
            if (BiomeRegions != null && instanceIndex >= 0 && instanceIndex < BiomeRegions.Count)
                return BiomeRegions[instanceIndex].TileIds;
            if (RoadSegments != null && instanceIndex >= 0 && instanceIndex < RoadSegments.Count)
                return RoadSegments[instanceIndex].TileIds;
            return null;
        }

        /// <summary>
        /// Approximate tile distance to the nearest of a set of tile ids: 0 when <paramref name="from"/>
        /// is in the set, <see cref="float.MaxValue"/> for an empty or null set.
        /// </summary>
        internal static float NearestTileDistance(int[] tileIds, PlanetTile from)
        {
            if (tileIds == null || tileIds.Length == 0 || !from.Valid || Find.WorldGrid == null)
                return float.MaxValue;

            float min = float.MaxValue;
            foreach (int t in tileIds)
            {
                if (t == from.tileId) return 0f;
                float d = Find.WorldGrid.ApproxDistanceInTiles(from, new PlanetTile(t, -1));
                if (d < min) min = d;
            }
            return min;
        }

        /// <summary>
        /// Distance to one instance: to a region/segment's nearest tile (its closest edge), or to a
        /// single-tile item's own tile.
        /// </summary>
        public float GetDistance(PlanetTile fromTile, int instanceIndex = 0)
        {
            if (!fromTile.Valid || Find.WorldGrid == null)
                return 0f;

            int[] members = GetMemberTileIdsAtInstance(instanceIndex);
            if (members != null && members.Length > 0)
                return NearestTileDistance(members, fromTile);

            PlanetTile targetTile = GetTileAtInstance(instanceIndex);
            if (!targetTile.Valid)
                return 0f;

            return Find.WorldGrid.ApproxDistanceInTiles(fromTile, targetTile);
        }

        /// <summary>
        /// Distance to the nearest tile across ALL instances, so a biome the player stands in sorts
        /// ahead of a distant patch whose center merely happens to be closer.
        /// </summary>
        public float NearestDistance(PlanetTile from)
        {
            if (!from.Valid || Find.WorldGrid == null)
                return 0f;

            if (BiomeRegions != null || RoadSegments != null)
            {
                float best = float.MaxValue;
                int count = InstanceCount;
                for (int i = 0; i < count; i++)
                {
                    float d = NearestTileDistance(GetMemberTileIdsAtInstance(i), from);
                    if (d < best) best = d;
                }
                return best == float.MaxValue ? 0f : best;
            }

            PlanetTile tile = GetTileAtInstance(0);
            return tile.Valid ? Find.WorldGrid.ApproxDistanceInTiles(from, tile) : 0f;
        }

        /// <summary>
        /// One-based proximity rank of an instance among its siblings, nearest first. Computed on the
        /// fly so the instance list keeps its stable build-time order (navigation relies on it not
        /// shifting) while the announced "Region N of M" tracks the player's position. Ties break by
        /// instance index, keeping ranks gap-free.
        /// </summary>
        public int InstanceRank(PlanetTile from, int instanceIndex)
        {
            int count = InstanceCount;
            if (count <= 1 || instanceIndex < 0 || instanceIndex >= count || !from.Valid)
                return instanceIndex + 1;

            float targetDist = NearestTileDistance(GetMemberTileIdsAtInstance(instanceIndex), from);
            int rank = 1;
            for (int i = 0; i < count; i++)
            {
                if (i == instanceIndex) continue;
                float d = NearestTileDistance(GetMemberTileIdsAtInstance(i), from);
                if (d < targetDist || (d == targetDist && i < instanceIndex))
                    rank++;
            }
            return rank;
        }

        /// <summary>
        /// Re-orders the instances nearest-first by each one's closest tile, so within-item
        /// navigation matches the item list's own distance sort. No-op below two instances.
        /// </summary>
        public void SortInstancesByDistance(PlanetTile from)
        {
            if (!from.Valid) return;
            if (BiomeRegions != null && BiomeRegions.Count > 1)
                BiomeRegions = BiomeRegions.OrderBy(r => NearestTileDistance(r.TileIds, from)).ToList();
            else if (RoadSegments != null && RoadSegments.Count > 1)
                RoadSegments = RoadSegments.OrderBy(s => NearestTileDistance(s.TileIds, from)).ToList();
        }

        /// <summary>Compass direction from the origin tile to one instance's center.</summary>
        public string GetDirectionFrom(PlanetTile fromTile, int instanceIndex = 0)
        {
            return GetDirectionFromTile(fromTile, GetTileAtInstance(instanceIndex));
        }

        /// <summary>
        /// Compass direction between two tiles, or a pole-relative one inside pole territory where
        /// compass bearings stop meaning anything.
        /// </summary>
        public static string GetDirectionFromTile(PlanetTile fromTile, PlanetTile targetTile)
        {
            if (!fromTile.Valid || !targetTile.Valid || Find.WorldGrid == null)
                return "";

            Vector3 fromPos = Find.WorldGrid.GetTileCenter(fromTile);
            Vector3 toPos = Find.WorldGrid.GetTileCenter(targetTile);
            Vector3 direction = (toPos - fromPos).normalized;

            if (WorldNavigationState.IsInPoleTerritory)
            {
                return GetRelativeDirection(fromPos, direction);
            }

            return GetCompassDirection(fromPos, direction);
        }

        // Clockwise from "ahead", one per 45-degree bucket, in
        // ScannerDirectionHelper.GetCompassBucketIndex's bucket order.
        private static readonly string[] RelativeDirectionKeys =
        {
            "RimWorldAccess.WorldScanner.RelDir.Ahead",
            "RimWorldAccess.WorldScanner.RelDir.AheadRight",
            "RimWorldAccess.WorldScanner.RelDir.Right",
            "RimWorldAccess.WorldScanner.RelDir.BehindRight",
            "RimWorldAccess.WorldScanner.RelDir.Behind",
            "RimWorldAccess.WorldScanner.RelDir.BehindLeft",
            "RimWorldAccess.WorldScanner.RelDir.Left",
            "RimWorldAccess.WorldScanner.RelDir.AheadLeft",
        };

        private static string GetCompassDirection(Vector3 fromPos, Vector3 direction)
        {
            double angle = ScannerDirectionHelper.GetSphericalAngleDegrees(fromPos, direction);
            return ScannerDirectionHelper.GetCompassDirection(angle);
        }

        private static string GetRelativeDirection(Vector3 fromPos, Vector3 direction)
        {
            double angle = ScannerDirectionHelper.GetSphericalAngleDegrees(fromPos, direction);
            int bucket = ScannerDirectionHelper.GetCompassBucketIndex(angle);
            return RelativeDirectionKeys[bucket].Translate();
        }
    }

    public class WorldScannerSubcategory : IScannerSubcategory<WorldScannerItem>
    {
        public string Name { get; set; }
        public List<WorldScannerItem> Items { get; set; }

        public WorldScannerSubcategory(string name)
        {
            Name = name;
            Items = new List<WorldScannerItem>();
        }

        public bool IsEmpty => Items == null || Items.Count == 0;

        public int InstanceCount => Items?.Sum(i => i.InstanceCount) ?? 0;
    }

    public class WorldScannerCategory : IScannerCategory<WorldScannerSubcategory>
    {
        public string Name { get; set; }
        public List<WorldScannerSubcategory> Subcategories { get; set; }

        public WorldScannerCategory(string name)
        {
            Name = name;
            Subcategories = new List<WorldScannerSubcategory>();
        }

        public bool IsEmpty => Subcategories == null || Subcategories.All(sc => sc.IsEmpty);

        public int InstanceCount =>
            Subcategories != null && Subcategories.Count > 0 ? Subcategories[0].InstanceCount : 0;
    }
}
