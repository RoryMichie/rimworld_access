using System;
using System.Collections.Generic;
using System.Linq;
using RimWorld;
using Verse;

namespace RimWorldAccess
{
    public static partial class ScannerState
    {
        // ExtraIndex on the shared cursor is the bulk-group index.
        private static readonly ScannerCursor<ScannerCategory, ScannerSubcategory, ScannerItem> scannerCursor =
            new ScannerCursor<ScannerCategory, ScannerSubcategory, ScannerItem>();

        // Within a sequence of Page Up/Down presses the sort order is frozen, so sequential
        // navigation is stable and needs no per-press re-sort. A session ends when the cursor moves
        // externally, the subcategory changes, the map state changes, or search state changes.
        private static readonly ScannerNavSession<IntVec3> navSession = new ScannerNavSession<IntVec3>();

        // The cursor position the current subcategory's items were sorted from; a mismatch drives
        // EnsureSortedForCurrentCursor's re-sort by live distance.
        private static IntVec3 lastSortedCursorPosition = IntVec3.Invalid;

        private static bool autoJumpMode
        {
            get
            {
                RimWorldAccessSettings settings = RimWorldAccessMod_Settings.Settings;
                return settings != null && settings.ScannerAutoJump;
            }
        }

        // Cache for CollectMapItems, which is too expensive to re-run on every keystroke.
        private static List<ScannerCategory> cachedCategories = null;
        private static int lastThingStateHash = 0;
        private static int lastDesignationCount = 0;
        private static int lastZoneCount = 0;
        private static int lastZoneCellHash = 0;
        private static int lastPlanHash = 0;

        // Distance metric on the square map grid, used by the shared clump-jump logic.
        private static readonly Func<IntVec3, IntVec3, float> CellMetric =
            (a, b) => (a - b).LengthHorizontal;

        /// <summary>Toggles auto-jump mode, where the cursor follows items as you navigate. Persisted immediately.</summary>
        public static void ToggleAutoJumpMode()
        {
            RimWorldAccessSettings settings = RimWorldAccessMod_Settings.Settings;
            if (settings == null)
                return;
            settings.ScannerAutoJump = !settings.ScannerAutoJump;
            LoadedModManager.GetMod<RimWorldAccessMod_Settings>()?.WriteSettings();
            TolkHelper.Speak((settings.ScannerAutoJump
                ? "RimWorldAccess.Map.Scanner.AutoJumpEnabled"
                : "RimWorldAccess.Map.Scanner.AutoJumpDisabled").Loc(), SpeechPriority.High);
        }

        /// <summary>Ends the navigation session, so the next Page Up/Down re-sorts from the current cursor.</summary>
        public static void InvalidateNavigationSession()
        {
            navSession.Invalidate();
        }

        /// <summary>
        /// Called from the cursor-position setter on every write. Invalidates the session unless the
        /// write came from this scanner's own JumpToCurrent.
        /// </summary>
        public static void NotifyCursorWritten()
        {
            navSession.NotifyPositionWritten();
        }

        /// <summary>Sorts the current subcategory from the cursor and anchors a fresh session there.</summary>
        private static void BeginNavigationSession()
        {
            EnsureSortedForCurrentCursor();
            navSession.Begin(MapNavigationState.CurrentCursorPosition);
        }

        /// <summary>Saves the scanner focus before a temporary switch to another category.</summary>
        public static void SaveFocus()
        {
            scannerCursor.SaveFocus();
        }

        /// <summary>Restores the saved focus; call after removing a temporary category.</summary>
        public static void RestoreFocus()
        {
            scannerCursor.RestoreFocus();
        }

        /// <summary>
        /// Creates the single temporary category and selects it. It is a real category — it joins the
        /// category cycle and survives RefreshItems — and <see cref="Invalidate"/> clears it.
        /// </summary>
        public static void CreateTemporaryCategory(string name, List<ScannerItem> items)
        {
            var subcategory = new ScannerSubcategory($"{name}-All");
            subcategory.Items.AddRange(items);
            CreateTemporaryCategory(name, new List<ScannerSubcategory> { subcategory });
        }

        /// <summary>
        /// Creates a temporary category from pre-built subcategories and selects it. subcategories[0]
        /// must represent the whole category — the "All" slot is positional — and
        /// subcategory names are announced verbatim, so pass localized display strings.
        /// </summary>
        public static void CreateTemporaryCategory(string name, List<ScannerSubcategory> subcategories)
        {
            RemoveTemporaryCategory();
            // A replaced slot cannot keep the previous owner's refresh hook.
            TemporaryCategoryRefreshHook = null;

            var category = new ScannerCategory(name);
            category.Subcategories.AddRange(subcategories);

            scannerCursor.SelectTemporaryCategory(category);

            // A new subcategory context; the next Page Down must re-sort within it.
            InvalidateNavigationSession();
        }

        /// <summary>
        /// Creates or replaces the temporary category WITHOUT stealing focus: the offer parks at the
        /// front of the category cycle until the player visits it, and a player already inside keeps
        /// their place on its fresh first item.
        /// </summary>
        public static void OfferTemporaryCategory(string name, List<ScannerItem> items)
        {
            bool wasInside = IsInTemporaryCategory();
            RemoveTemporaryCategory();
            TemporaryCategoryRefreshHook = null;

            var subcategory = new ScannerSubcategory($"{name}-All");
            subcategory.Items.AddRange(items);
            var category = new ScannerCategory(name);
            category.Subcategories.Add(subcategory);

            if (wasInside)
            {
                scannerCursor.SelectTemporaryCategory(category);
            }
            else
            {
                bool hadCategories = scannerCursor.Categories.Count > 0;
                scannerCursor.AttachTemporaryCategory(category);
                // The front insert shifted every real category up one slot; follow the one the
                // cursor was resting on. An empty ring has nothing to follow.
                if (hadCategories)
                    scannerCursor.CategoryIndex++;
            }

            InvalidateNavigationSession();
        }

        /// <summary>
        /// Invoked when category cycling lands on the temporary category, so its owner can rebuild
        /// stale content before the landing is announced. Returns false when the rebuild removed the
        /// category and the cycle should keep walking. Owned by whoever last published the temporary
        /// category, which clears it on takeover.
        /// </summary>
        public static Func<bool> TemporaryCategoryRefreshHook;

        /// <summary>Removes the temporary category; call RestoreFocus afterwards to return to the previous position.</summary>
        public static void RemoveTemporaryCategory()
        {
            if (scannerCursor.RemoveTemporaryCategory())
            {
                InvalidateNavigationSession();
            }
        }

        /// <summary>
        /// Live distance from the cursor to an item. For area-backed items (terrain regions, zones,
        /// rooms) this is the distance to the NEAREST cell, so a cursor inside the area reads 0
        /// rather than the misleading distance to its geometric center.
        /// </summary>
        private static float ComputeLiveDistance(ScannerItem item, IntVec3 cursor)
        {
            if (item == null) return float.MaxValue;

            if (item.HasTerrainRegions && item.TerrainRegions.Count > 0)
            {
                float minDist = float.MaxValue;
                foreach (var region in item.TerrainRegions)
                {
                    if (region.AllPositions == null) continue;
                    foreach (var cell in region.AllPositions)
                    {
                        if (cell == cursor) return 0f;
                        float d = (cell - cursor).LengthHorizontal;
                        if (d < minDist) minDist = d;
                    }
                }
                return minDist == float.MaxValue ? (item.Position - cursor).LengthHorizontal : minDist;
            }

            if (item.IsZone && item.Zone != null && item.Zone.cells != null && item.Zone.cells.Count > 0)
            {
                float minDist = float.MaxValue;
                foreach (var cell in item.Zone.cells)
                {
                    if (cell == cursor) return 0f;
                    float d = (cell - cursor).LengthHorizontal;
                    if (d < minDist) minDist = d;
                }
                return minDist;
            }

            if (item.IsRoom && item.Room != null)
            {
                float minDist = float.MaxValue;
                foreach (var cell in item.Room.Cells)
                {
                    if (cell == cursor) return 0f;
                    float d = (cell - cursor).LengthHorizontal;
                    if (d < minDist) minDist = d;
                }
                return minDist == float.MaxValue ? (item.Position - cursor).LengthHorizontal : minDist;
            }

            // Bulk terrain: a flat list of positions with no region structure.
            if (item.IsTerrain && item.BulkTerrainPositions != null && item.BulkTerrainPositions.Count > 0)
            {
                float minDist = float.MaxValue;
                foreach (var cell in item.BulkTerrainPositions)
                {
                    if (cell == cursor) return 0f;
                    float d = (cell - cursor).LengthHorizontal;
                    if (d < minDist) minDist = d;
                }
                return minDist;
            }

            IntVec3 pos;
            if (item.Thing != null && item.Thing.Spawned && !item.Thing.Destroyed)
                pos = item.Thing.Position;
            else if (item.IsDesignation && item.Designation != null)
                pos = item.Designation.target.Cell;
            else
                pos = item.Position;

            return (pos - cursor).LengthHorizontal;
        }

        /// <summary>The index of the region whose nearest cell is closest to the cursor, or 0 when the item has no regions.</summary>
        private static int FindNearestRegionIndex(ScannerItem item, IntVec3 cursor)
        {
            if (!item.HasTerrainRegions || item.TerrainRegions.Count == 0)
                return 0;

            int bestIdx = 0;
            float bestDist = float.MaxValue;
            for (int i = 0; i < item.TerrainRegions.Count; i++)
            {
                var region = item.TerrainRegions[i];
                if (region.AllPositions == null) continue;
                foreach (var cell in region.AllPositions)
                {
                    if (cell == cursor) return i;
                    float d = (cell - cursor).LengthHorizontal;
                    if (d < bestDist)
                    {
                        bestDist = d;
                        bestIdx = i;
                    }
                }
            }
            return bestIdx;
        }

        /// <summary>Distance to a region's nearest tile: 0 on it, MaxValue for an empty region.</summary>
        private static float RegionNearestDistance(TerrainRegion region, IntVec3 cursor)
        {
            if (region?.AllPositions == null) return float.MaxValue;
            float best = float.MaxValue;
            foreach (var cell in region.AllPositions)
            {
                if (cell == cursor) return 0f;
                float d = (cell - cursor).LengthHorizontal;
                if (d < best) best = d;
            }
            return best;
        }

        /// <summary>
        /// A region's 1-based proximity rank among its siblings. Computed on the fly so the region
        /// list keeps its stable build-time order — bulk navigation and auto-jump rely on that order
        /// not shifting — while the announced "area N of M" still tracks the player's position. Ties
        /// break by list index, giving a stable gap-free sequence.
        /// </summary>
        private static int ComputeRegionRank(ScannerItem item, int regionIndex, IntVec3 cursor)
        {
            if (!item.HasTerrainRegions || regionIndex < 0 || regionIndex >= item.TerrainRegions.Count)
                return regionIndex + 1;

            float targetDist = RegionNearestDistance(item.TerrainRegions[regionIndex], cursor);
            int rank = 1;
            for (int i = 0; i < item.TerrainRegions.Count; i++)
            {
                if (i == regionIndex) continue;
                float d = RegionNearestDistance(item.TerrainRegions[i], cursor);
                if (d < targetDist || (d == targetDist && i < regionIndex))
                    rank++;
            }
            return rank;
        }

        /// <summary>
        /// The announcement for one terrain region, shared by the primary and bulk-navigation
        /// announcements so both read "label: size, distance direction[, area N of M]".
        /// </summary>
        private static string BuildRegionAnnouncement(ScannerItem item, int regionIndex)
        {
            if (regionIndex < 0 || regionIndex >= item.TerrainRegions.Count)
                return item.Label;

            var region = item.TerrainRegions[regionIndex];
            var cursorPos = MapNavigationState.CurrentCursorPosition;

            float distance = float.MaxValue;
            IntVec3 nearestCell = IntVec3.Invalid;
            if (region.AllPositions != null)
            {
                foreach (var cell in region.AllPositions)
                {
                    if (cell == cursorPos) { distance = 0f; nearestCell = cell; break; }
                    float d = (cell - cursorPos).LengthHorizontal;
                    if (d < distance) { distance = d; nearestCell = cell; }
                }
            }
            if (!nearestCell.IsValid)
            {
                nearestCell = region.CenterPosition;
                distance = (region.CenterPosition - cursorPos).LengthHorizontal;
            }

            var direction = GetDirectionFromCursor(nearestCell);

            // Deep ore deposits carry a quantity alongside the size.
            string sizeAndQuantity;
            if (region.TotalQuantity.HasValue && item.DeepOreDef != null)
            {
                sizeAndQuantity = "RimWorldAccess.Map.Scanner.Region.SizeWithQuantity".Translate(region.TileCount, region.TotalQuantity.Value, item.DeepOreDef.label);
            }
            else
            {
                sizeAndQuantity = region.SizeDescription;
            }

            string announcement;
            if (direction != null)
            {
                announcement = "RimWorldAccess.Map.Scanner.Region.WithDirection".Translate(item.Label, sizeAndQuantity, distance.ToString("F1"), direction);
            }
            else
            {
                announcement = "RimWorldAccess.Map.Scanner.Region.Here".Translate(item.Label, sizeAndQuantity);
            }

            if (item.RegionCount > 1)
            {
                // Live proximity rank, not the list slot, so the nearest patch always reads "area 1".
                int regionPosition = ComputeRegionRank(item, regionIndex, cursorPos);
                announcement += "RimWorldAccess.Map.Scanner.Region.AreaSuffix".Translate(regionPosition, item.RegionCount);
            }

            // Standing on a patch with a distinct center offers the second Home press that jumps there.
            if (ClumpNav.OffersCenter(region.AllPositions, region.CenterPosition, cursorPos))
                announcement += ". " + "RimWorldAccess.Map.Scanner.PressHomeForCenter".Translate();

            return announcement;
        }

        /// <summary>
        /// The cell of an area-backed item nearest the cursor, so announcements point at the patch's
        /// near edge rather than its center. Invalid when the item is not area-backed or has no cells.
        /// </summary>
        private static IntVec3 FindNearestCell(ScannerItem item, IntVec3 cursor)
        {
            if (item == null) return IntVec3.Invalid;

            IntVec3 best = IntVec3.Invalid;
            float bestDist = float.MaxValue;

            void consider(IntVec3 cell)
            {
                float d = (cell - cursor).LengthHorizontal;
                if (d < bestDist)
                {
                    bestDist = d;
                    best = cell;
                }
            }

            if (item.HasTerrainRegions)
            {
                foreach (var region in item.TerrainRegions)
                {
                    if (region.AllPositions == null) continue;
                    foreach (var cell in region.AllPositions)
                    {
                        if (cell == cursor) return cell;
                        consider(cell);
                    }
                }
                return best;
            }

            if (item.IsZone && item.Zone?.cells != null)
            {
                foreach (var cell in item.Zone.cells)
                {
                    if (cell == cursor) return cell;
                    consider(cell);
                }
                return best;
            }

            if (item.IsRoom && item.Room != null)
            {
                foreach (var cell in item.Room.Cells)
                {
                    if (cell == cursor) return cell;
                    consider(cell);
                }
                return best;
            }

            if (item.IsTerrain && item.BulkTerrainPositions != null)
            {
                foreach (var cell in item.BulkTerrainPositions)
                {
                    if (cell == cursor) return cell;
                    consider(cell);
                }
                return best;
            }

            return IntVec3.Invalid;
        }

        /// <summary>
        /// Re-sorts the current subcategory by live distance if the cursor moved since the last sort;
        /// never touches the item index. Only session starts and category/subcategory switches call
        /// it — re-sorting on every press would ping-pong the order after each scanner-driven jump.
        /// </summary>
        private static bool EnsureSortedForCurrentCursor()
        {
            if (!MapNavigationState.IsInitialized)
                return false;

            var cursor = MapNavigationState.CurrentCursorPosition;
            if (cursor == lastSortedCursorPosition)
                return false;

            var subcat = GetCurrentSubcategory();
            if (subcat == null || subcat.Items.Count == 0)
            {
                lastSortedCursorPosition = cursor;
                return false;
            }

            foreach (var item in subcat.Items)
            {
                item.Distance = ComputeLiveDistance(item, cursor);

                // Sort the item's own regions nearest-first from the same cursor, so within-item
                // navigation follows live proximity. Living inside this session-boundary re-sort is
                // what keeps it anchored to manual cursor moves and never to a scanner-driven jump.
                if (item.HasTerrainRegions && item.TerrainRegions.Count > 1)
                {
                    foreach (var region in item.TerrainRegions)
                        region.Distance = RegionNearestDistance(region, cursor);
                    item.TerrainRegions = item.TerrainRegions.OrderBy(r => r.Distance).ToList();
                }
            }

            // OrderBy is stable, so tied distances preserve input order.
            subcat.Items = subcat.Items.OrderBy(i => i.Distance).ToList();
            lastSortedCursorPosition = cursor;
            return true;
        }

        /// <summary>Refreshes the selected item's Distance without re-sorting, for bulk navigation where order must hold.</summary>
        private static void UpdateCurrentItemDistance()
        {
            var cursor = MapNavigationState.CurrentCursorPosition;
            var item = GetCurrentItem();
            if (item == null) return;

            item.Distance = ComputeLiveDistance(item, cursor);
        }

        /// <summary>
        /// Drops the whole scanner cache, the temporary category, the saved focus and any active
        /// search. Call on a map switch or a large change in map contents.
        /// </summary>
        public static void Invalidate()
        {
            scannerCursor.Categories.Clear();
            scannerCursor.ClearTemporaryCategoryReference();
            scannerCursor.CategoryIndex = 0;
            scannerCursor.SubcategoryIndex = 0;
            scannerCursor.ItemIndex = 0;
            scannerCursor.ExtraIndex = 0;
            lastSortedCursorPosition = IntVec3.Invalid;

            cachedCategories = null;
            lastThingStateHash = 0;
            lastDesignationCount = 0;
            lastZoneCount = 0;
            lastZoneCellHash = 0;
            lastPlanHash = 0;
            ScannerHelper.InvalidateCache();
            InvalidateNavigationSession();
            scannerCursor.ClearSavedFocus();
            ScannerSearchState.ClearSearchSilent();
        }

        /// <summary>
        /// Rebuilds the item list from the current cursor position, refreshing an active search
        /// filter with the fresh items. Called automatically by the navigation methods.
        /// </summary>
        private static void RefreshItems()
        {
            if (!MapNavigationState.IsInitialized)
            {
                TolkHelper.Speak("RimWorldAccess.Map.Scanner.NotInitialized".Loc(), SpeechPriority.High);
                return;
            }

            var map = Find.CurrentMap;
            if (map == null)
            {
                TolkHelper.Speak("RimWorldAccess.Map.Scanner.NoActiveMap".Loc(), SpeechPriority.High);
                return;
            }

            var cursorPos = MapNavigationState.CurrentCursorPosition;

            // Capture whether the temporary category is being navigated BEFORE any rebuild: a rebuild
            // re-adds it at the END of the list without moving the selection with it, which would
            // silently drop the player into the real categories. Rebuilds are frequent, since pawn
            // movement changes the thing-state hash every step.
            bool wasInTemporaryCategory = IsInTemporaryCategory();
            int temporarySubcategoryIndexBeforeRefresh = scannerCursor.SubcategoryIndex;
            int temporaryItemIndexBeforeRefresh = scannerCursor.ItemIndex;

            int currentThingHash = map.listerThings.StateHashOfGroup(ThingRequestGroup.Everything);
            int currentDesignationCount = map.designationManager.AllDesignations.Count;
            int currentZoneCount = map.zoneManager.AllZones.Count;
            // Zone count alone misses cell-level edits like dragging a stockpile larger or smaller.
            int currentZoneCellHash = 0;
            foreach (var zone in map.zoneManager.AllZones)
            {
                int cellCount = zone?.cells?.Count ?? 0;
                currentZoneCellHash = unchecked(currentZoneCellHash * 31 + cellCount);
            }

            // Plans are neither Things nor zones, so hash count, colour and cell count: a recolour
            // moves a plan between colour subcategories and must invalidate the cache too.
            int currentPlanHash = 0;
            foreach (var plan in map.planManager.AllPlans)
            {
                int planFactor = (plan?.Color?.shortHash ?? 0) * 31 + (plan?.CellCount ?? 0);
                currentPlanHash = unchecked(currentPlanHash * 31 + planFactor);
            }

            bool cacheValid = cachedCategories != null
                && cachedCategories.Count > 0
                && currentThingHash == lastThingStateHash
                && currentDesignationCount == lastDesignationCount
                && currentZoneCount == lastZoneCount
                && currentZoneCellHash == lastZoneCellHash
                && currentPlanHash == lastPlanHash;

            if (cacheValid)
            {
                scannerCursor.Categories = cachedCategories;

                scannerCursor.ReattachTemporaryCategoryIfMissing();
                if (scannerCursor.TemporaryCategory != null && wasInTemporaryCategory)
                    FocusTemporaryCategory(temporarySubcategoryIndexBeforeRefresh, temporaryItemIndexBeforeRefresh);

                // Cached items may have been sorted at an older cursor; force the next re-sort.
                lastSortedCursorPosition = IntVec3.Invalid;

                ValidateIndices();
                return;
            }

            if (ScannerSearchState.HasActiveFilter)
            {
                bool wasInFilterCategory = IsInTemporaryCategory();
                int previousItemIndex = scannerCursor.ItemIndex;

                scannerCursor.Categories = ScannerHelper.CollectMapItems(map, cursorPos);
                cachedCategories = scannerCursor.Categories;
                lastThingStateHash = currentThingHash;
                lastDesignationCount = currentDesignationCount;
                lastZoneCount = currentZoneCount;
                lastZoneCellHash = currentZoneCellHash;
                lastPlanHash = currentPlanHash;
                lastSortedCursorPosition = cursorPos;
                // A cache miss means the map changed, so the navigation snapshot is stale.
                InvalidateNavigationSession();

                var filteredItems = ScannerSearchState.RefreshMapFilter(scannerCursor.Categories);
                if (filteredItems != null)
                {
                    if (filteredItems.Count > 0)
                    {
                        var filterCategory = new ScannerCategory(ScannerSearchState.GetFilterCategoryName());
                        var subcategory = new ScannerSubcategory($"{ScannerSearchState.GetFilterCategoryName()}-All");
                        subcategory.Items.AddRange(filteredItems);
                        filterCategory.Subcategories.Add(subcategory);
                        scannerCursor.AttachTemporaryCategory(filterCategory);

                        if (wasInFilterCategory)
                        {
                            scannerCursor.FocusTemporaryCategory(0, previousItemIndex);
                        }
                    }
                    else
                    {
                        scannerCursor.ClearTemporaryCategoryReference();

                        if (wasInFilterCategory)
                        {
                            RestoreFocus();
                        }
                    }

                    if (scannerCursor.Categories.Count == 0)
                    {
                        TolkHelper.Speak("RimWorldAccess.Map.Scanner.NoItems".Loc(), SpeechPriority.High);
                        return;
                    }

                    ValidateIndices();
                    return;
                }
            }

            var savedTemporaryCategory = scannerCursor.TemporaryCategory;

            scannerCursor.Categories = ScannerHelper.CollectMapItems(map, cursorPos);
            cachedCategories = scannerCursor.Categories;
            lastThingStateHash = currentThingHash;
            lastDesignationCount = currentDesignationCount;
            lastZoneCount = currentZoneCount;
            lastZoneCellHash = currentZoneCellHash;
            lastPlanHash = currentPlanHash;
            // CollectMapItems already sorted by distance from cursorPos, so skip the next re-sort.
            lastSortedCursorPosition = cursorPos;
            InvalidateNavigationSession();

            if (savedTemporaryCategory != null)
            {
                scannerCursor.AttachTemporaryCategory(savedTemporaryCategory);

                // Follow the temporary category, now at the end of the list.
                if (wasInTemporaryCategory)
                    FocusTemporaryCategory(temporarySubcategoryIndexBeforeRefresh, temporaryItemIndexBeforeRefresh);
            }

            if (scannerCursor.Categories.Count == 0)
            {
                TolkHelper.Speak("RimWorldAccess.Map.Scanner.NoItems".Loc(), SpeechPriority.High);
                return;
            }

            ValidateIndices();
        }

        /// <summary>
        /// Rebuilds the category the cursor just landed on, in place, from the live map. Returns false
        /// when that category came back empty and was dropped from the ring, so the caller keeps
        /// cycling. Whole-map aggregates cannot be built in isolation and take the full RefreshItems
        /// path; the temporary category is not map-derived, so its owner refreshes it through
        /// <see cref="TemporaryCategoryRefreshHook"/>, or it is left as it is.
        /// </summary>
        private static bool RebuildLandedCategory()
        {
            ScannerCategory category = GetCurrentCategory();
            if (category == null)
                return false;

            if (IsInTemporaryCategory())
                return TemporaryCategoryRefreshHook == null || TemporaryCategoryRefreshHook();

            if (category.Name == "All" || category.Name == "Uncategorized")
            {
                string name = category.Name;
                RefreshItems();
                return RepointToCategoryNamed(name);
            }

            var map = Find.CurrentMap;
            if (map == null)
                return true;

            int index = scannerCursor.CategoryIndex;
            List<ScannerCategory> rebuilt = ScannerHelper.CollectMapItems(
                map, MapNavigationState.CurrentCursorPosition, category.Name);

            if (rebuilt.Count == 0)
            {
                scannerCursor.Categories.RemoveAt(index);
                return false;
            }

            scannerCursor.Categories[index] = rebuilt[0];
            // The ring the cache hands back next hit must carry the fresh category too.
            if (cachedCategories != null && !ReferenceEquals(cachedCategories, scannerCursor.Categories))
            {
                int cachedIndex = cachedCategories.FindIndex(c => c.Name == rebuilt[0].Name);
                if (cachedIndex >= 0)
                    cachedCategories[cachedIndex] = rebuilt[0];
            }
            // The rebuild sorted from the current cursor, but leave the shared re-sort armed so a
            // later cursor move is still detected.
            lastSortedCursorPosition = IntVec3.Invalid;
            return true;
        }

        /// <summary>Puts the cursor back on the named category after a refresh reshaped the ring; false when it is gone.</summary>
        private static bool RepointToCategoryNamed(string name)
        {
            for (int i = 0; i < scannerCursor.Categories.Count; i++)
            {
                if (scannerCursor.Categories[i].Name == name)
                {
                    scannerCursor.CategoryIndex = i;
                    scannerCursor.SubcategoryIndex = 0;
                    scannerCursor.ItemIndex = 0;
                    scannerCursor.ExtraIndex = 0;
                    return true;
                }
            }
            return false;
        }

        public static void NextItem()
        {
            if (WorldNavigationState.IsActive) return;

            // Always refresh: it is cheap on a cache hit, and a miss catches pawn death, new
            // designations and zone edits while invalidating the session.
            bool wasEmpty = scannerCursor.Categories.Count == 0;
            RefreshItems();
            if (scannerCursor.Categories.Count == 0) return;
            if (wasEmpty) AnnounceCurrentCategory();

            var currentSubcat = GetCurrentSubcategory();
            if (currentSubcat == null || currentSubcat.Items.Count == 0) return;

            AdvanceInSession(forward: true, currentSubcat);
        }

        public static void PreviousItem()
        {
            if (WorldNavigationState.IsActive) return;

            bool wasEmpty = scannerCursor.Categories.Count == 0;
            RefreshItems();
            if (scannerCursor.Categories.Count == 0) return;
            if (wasEmpty) AnnounceCurrentCategory();

            var currentSubcat = GetCurrentSubcategory();
            if (currentSubcat == null || currentSubcat.Items.Count == 0) return;

            AdvanceInSession(forward: false, currentSubcat);
        }

        /// <summary>
        /// Shared navigation step for NextItem/PreviousItem: starts a session when none is active,
        /// then advances the index through the frozen sort order.
        /// </summary>
        private static void AdvanceInSession(bool forward, ScannerSubcategory currentSubcat)
        {
            // Safety net for a code path that moved the cursor without the setter hook.
            if (navSession.HasDrifted(MapNavigationState.CurrentCursorPosition))
            {
                InvalidateNavigationSession();
            }

            if (!navSession.Active)
            {
                BeginNavigationSession();
            }

            if (forward)
            {
                scannerCursor.ItemIndex++;
                if (scannerCursor.ItemIndex >= currentSubcat.Items.Count)
                {
                    scannerCursor.ItemIndex = 0;
                    if (currentSubcat.Items.Count > 1) MenuHelper.PlayWrapTone();
                }
            }
            else
            {
                scannerCursor.ItemIndex--;
                if (scannerCursor.ItemIndex < 0)
                {
                    scannerCursor.ItemIndex = currentSubcat.Items.Count - 1;
                    if (currentSubcat.Items.Count > 1) MenuHelper.PlayWrapTone();
                }
            }
            scannerCursor.ExtraIndex = 0;

            if (autoJumpMode)
            {
                // Auto-jump skips AnnounceCurrentItem, which is what normally picks the nearest
                // region, so select it here or the jump targets region 0 instead.
                var itemForJump = GetCurrentItem();
                if (itemForJump != null && itemForJump.HasTerrainRegions)
                    scannerCursor.ExtraIndex = FindNearestRegionIndex(itemForJump, MapNavigationState.CurrentCursorPosition);
                JumpToCurrent();
            }
            else
            {
                AnnounceCurrentItem();
            }

            // Record where the cursor ended up so the next press can detect drift.
            navSession.RecordPosition(MapNavigationState.CurrentCursorPosition);
        }

        public static void NextBulkItem()
        {
            if (WorldNavigationState.IsActive) return;

            if (scannerCursor.Categories.Count == 0)
            {
                RefreshItems();
                if (scannerCursor.Categories.Count == 0) return;
                AnnounceCurrentCategory();
            }

            var currentItem = GetCurrentItem();
            if (currentItem == null || !currentItem.IsBulkGroup) return;

            scannerCursor.ExtraIndex++;
            if (scannerCursor.ExtraIndex >= currentItem.BulkCount)
            {
                scannerCursor.ExtraIndex = 0;
                MenuHelper.PlayWrapTone();
            }

            // Distance only; bulk order must stay stable, so never re-sort here.
            UpdateCurrentItemDistance();

            if (autoJumpMode)
            {
                JumpToCurrent();
            }
            else
            {
                AnnounceCurrentBulkItem();
            }
        }

        public static void PreviousBulkItem()
        {
            if (WorldNavigationState.IsActive) return;

            if (scannerCursor.Categories.Count == 0)
            {
                RefreshItems();
                if (scannerCursor.Categories.Count == 0) return;
                AnnounceCurrentCategory();
            }

            var currentItem = GetCurrentItem();
            if (currentItem == null || !currentItem.IsBulkGroup) return;

            scannerCursor.ExtraIndex--;
            if (scannerCursor.ExtraIndex < 0)
            {
                scannerCursor.ExtraIndex = currentItem.BulkCount - 1;
                MenuHelper.PlayWrapTone();
            }

            // Distance only; bulk order must stay stable, so never re-sort here.
            UpdateCurrentItemDistance();

            if (autoJumpMode)
            {
                JumpToCurrent();
            }
            else
            {
                AnnounceCurrentBulkItem();
            }
        }

    }
}
