using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Verse;

namespace RimWorldAccess
{
    /// <summary>
    /// Typeahead search filtering scanner items by name, shared by the map and world scanners.
    /// </summary>
    public static class ScannerSearchState
    {
        private static string searchBuffer = "";
        private static bool isOnWorldMap = false;
        private static bool isSearchModeActive = false;

        // Survives an Enter-confirmed search.
        private static string activeFilterQuery = "";
        private static bool activeFilterIsWorldMap = false;

        // Restored on cancel.
        private static string savedFilterQuery = "";
        private static bool savedFilterIsWorldMap = false;


        /// <summary>Whether search input mode is active.</summary>
        public static bool IsActive => isSearchModeActive;

        /// <summary>
        /// True while the session belongs to the world scanner, letting the colony overlay
        /// mirror gate on colony sessions alone.
        /// </summary>
        public static bool IsOnWorldMap => isOnWorldMap;

        /// <summary>Whether a confirmed filter is in force.</summary>
        public static bool HasActiveFilter => !string.IsNullOrEmpty(activeFilterQuery);

        /// <summary>The current search string.</summary>
        public static string SearchBuffer => searchBuffer;

        /// <summary>Opens search mode, saving any existing filter for restoration on cancel.</summary>
        public static void Activate(bool onWorldMap)
        {
            savedFilterQuery = activeFilterQuery;
            savedFilterIsWorldMap = activeFilterIsWorldMap;

            if (HasActiveFilter)
            {
                activeFilterQuery = "";
                if (activeFilterIsWorldMap)
                {
                    WorldScannerState.RemoveTemporaryCategory();
                }
                else
                {
                    ScannerState.RemoveTemporaryCategory();
                }
            }

            isOnWorldMap = onWorldMap;
            searchBuffer = "";
            isSearchModeActive = true;

            // Only with no previous filter: that filter already carries its own position.
            if (string.IsNullOrEmpty(savedFilterQuery))
            {
                if (onWorldMap)
                {
                    if (!WorldScannerState.IsInTemporaryCategory())
                    {
                        WorldScannerState.SaveFocus();
                    }
                }
                else
                {
                    if (!ScannerState.IsInTemporaryCategory())
                    {
                        ScannerState.SaveFocus();
                    }
                }
            }

            TolkHelper.Speak("RimWorldAccess.Map.Search.Activated".Loc(), SpeechPriority.Normal);
        }

        /// <summary>Appends a typed character to the search buffer.</summary>
        public static void HandleCharacter(char c)
        {
            searchBuffer += c;
            TolkHelper.SpeakData(c.ToString(), SpeechPriority.Low);
            UpdateSearchResults();
        }

        /// <summary>Removes the last character; an empty buffer cancels the search.</summary>
        public static void HandleBackspace()
        {
            if (string.IsNullOrEmpty(searchBuffer))
                return;

            char deleted = searchBuffer[searchBuffer.Length - 1];
            searchBuffer = searchBuffer.Substring(0, searchBuffer.Length - 1);
            TolkHelper.Speak("RimWorldAccess.Map.Input.Deleted".Loc(deleted), SpeechPriority.Low);

            if (string.IsNullOrEmpty(searchBuffer))
            {
                CancelSearch();
            }
            else
            {
                UpdateSearchResults();
            }
        }

        /// <summary>
        /// Enter: leaves input mode keeping the filter and its category, which refreshes with the
        /// scanner. The saved filter is dropped — the new one replaces it permanently.
        /// </summary>
        public static void ConfirmSearch()
        {
            ConfirmSearch(announce: true);
        }

        /// <summary>
        /// Ends a search silently: keeps the typed filter like Enter, or unwinds an empty session
        /// like Escape. Used by the jump-to-result path, so the jump is the only utterance.
        /// </summary>
        public static void DismissSilently()
        {
            if (!isSearchModeActive)
                return;
            if (string.IsNullOrEmpty(searchBuffer))
                CancelSearch(announce: false);
            else
                ConfirmSearch(announce: false);
        }

        private static void ConfirmSearch(bool announce)
        {
            if (IsActive && !string.IsNullOrEmpty(searchBuffer))
            {
                activeFilterQuery = searchBuffer;
                activeFilterIsWorldMap = isOnWorldMap;

                searchBuffer = "";
                isSearchModeActive = false;

                savedFilterQuery = "";
                savedFilterIsWorldMap = false;

                if (announce)
                    TolkHelper.Speak("RimWorldAccess.Map.Search.NowFiltering".Loc(activeFilterQuery), SpeechPriority.Normal);
            }
            else
            {
                CancelSearch(announce);
            }
        }

        /// <summary>
        /// Escape: restores the previous filter if there was one, else the pre-search position.
        /// </summary>
        public static void CancelSearch()
        {
            CancelSearch(announce: true);
        }

        private static void CancelSearch(bool announce)
        {
            searchBuffer = "";
            isSearchModeActive = false;

            if (isOnWorldMap)
            {
                WorldScannerState.RemoveTemporaryCategory();
            }
            else
            {
                ScannerState.RemoveTemporaryCategory();
            }

            if (!string.IsNullOrEmpty(savedFilterQuery))
            {
                activeFilterQuery = savedFilterQuery;
                activeFilterIsWorldMap = savedFilterIsWorldMap;

                if (savedFilterIsWorldMap)
                {
                    var matching = RefreshWorldFilter();
                    if (matching != null && matching.Count > 0)
                    {
                        WorldScannerState.CreateTemporaryCategory("RimWorldAccess.Map.Search.CategoryName".Translate(activeFilterQuery), matching);
                    }
                }
                else
                {
                    var map = Find.CurrentMap;
                    var cursor = MapNavigationState.CurrentCursorPosition;
                    var matching = RefreshMapFilter(map, cursor);
                    if (matching != null && matching.Count > 0)
                    {
                        ScannerState.CreateTemporaryCategory("RimWorldAccess.Map.Search.CategoryName".Translate(activeFilterQuery), matching);
                    }
                }

                if (announce)
                    TolkHelper.Speak("RimWorldAccess.Map.Search.RestoredFilter".Loc(savedFilterQuery), SpeechPriority.Normal);
            }
            else
            {
                activeFilterQuery = "";
                if (isOnWorldMap)
                {
                    WorldScannerState.RestoreFocus();
                }
                else
                {
                    ScannerState.RestoreFocus();
                }
                if (announce)
                    TolkHelper.Speak("RimWorldAccess.Map.Search.Cancelled".Loc(), SpeechPriority.Normal);
            }

            savedFilterQuery = "";
            savedFilterIsWorldMap = false;
        }

        /// <summary>
        /// Ctrl+Z outside a search: drops the active filter and its category, restoring the
        /// pre-search position if the cursor is inside that category. No-op without a filter.
        /// </summary>
        public static void ClearActiveFilter()
        {
            if (!HasActiveFilter || IsActive)
                return;

            if (activeFilterIsWorldMap)
            {
                bool wasInFilter = WorldScannerState.IsInTemporaryCategory();
                WorldScannerState.RemoveTemporaryCategory();
                if (wasInFilter)
                    WorldScannerState.RestoreFocus();
            }
            else
            {
                bool wasInFilter = ScannerState.IsInTemporaryCategory();
                ScannerState.RemoveTemporaryCategory();
                if (wasInFilter)
                    ScannerState.RestoreFocus();
            }

            activeFilterQuery = "";
            activeFilterIsWorldMap = false;
            savedFilterQuery = "";
            savedFilterIsWorldMap = false;

            TolkHelper.Speak("RimWorldAccess.Map.Search.FilterCleared".Loc(), SpeechPriority.Normal);
        }

        /// <summary>
        /// Clears the search and any active filter silently, for a map switch or invalidation.
        /// </summary>
        public static void ClearSearchSilent()
        {
            searchBuffer = "";
            isSearchModeActive = false;
            activeFilterQuery = "";
            // The temporary category is removed by ScannerState.Invalidate().
        }

        /// <summary>
        /// Re-runs the active filter over fresh map items, or null when no filter is active.
        /// </summary>
        public static List<ScannerItem> RefreshMapFilter(Map map, IntVec3 cursorPosition)
        {
            if (string.IsNullOrEmpty(activeFilterQuery) || activeFilterIsWorldMap)
                return null;

            var categories = ScannerHelper.CollectMapItems(map, cursorPosition);
            return RefreshMapFilter(categories);
        }

        /// <summary>
        /// Re-runs the active filter over already-collected categories, avoiding a second
        /// collection pass. Null when no filter is active.
        /// </summary>
        public static List<ScannerItem> RefreshMapFilter(List<ScannerCategory> preCollectedCategories)
        {
            if (string.IsNullOrEmpty(activeFilterQuery) || activeFilterIsWorldMap)
                return null;

            var allItems = FlattenFromAllCategory(preCollectedCategories)
                .Where(item => item.Label != ScannerHelper.UnexploredAreaLabel)
                .ToList();

            return ScannerSearchEngine.FilterAndRank(
                allItems, activeFilterQuery, item => item.Label, item => item.Distance);
        }

        /// <summary>
        /// The "All"/"All-All" subcategory: the reference-deduped, grouped-once view of every item
        /// on the map. Empty when no "All" category was produced.
        /// </summary>
        private static List<ScannerItem> FlattenFromAllCategory(List<ScannerCategory> categories)
        {
            if (categories == null)
                return new List<ScannerItem>();

            foreach (var category in categories)
            {
                if (category.Name == "All")
                    return category.AllSubcategory?.Items.ToList() ?? new List<ScannerItem>();
            }

            return new List<ScannerItem>();
        }

        /// <summary>
        /// Re-runs the active filter over fresh world items, or null when no filter is active.
        /// </summary>
        public static List<WorldScannerItem> RefreshWorldFilter()
        {
            if (string.IsNullOrEmpty(activeFilterQuery) || !activeFilterIsWorldMap)
                return null;

            var originTile = WorldNavigationState.CurrentSelectedTile;
            var allItems = CollectAllWorldItemsFlat();

            return ScannerSearchEngine.FilterAndRank(
                allItems, activeFilterQuery,
                item => item.Label,
                item => item.GetDistance(originTile, 0));
        }

        /// <summary>The current filter category's name.</summary>
        public static string GetFilterCategoryName()
        {
            return "RimWorldAccess.Map.Search.CategoryName".Translate(activeFilterQuery);
        }

        /// <summary>Re-runs the search for the current buffer.</summary>
        private static void UpdateSearchResults()
        {
            if (string.IsNullOrEmpty(searchBuffer))
                return;

            if (isOnWorldMap)
            {
                UpdateWorldSearchResults();
            }
            else
            {
                UpdateMapSearchResults();
            }
        }

        private static void UpdateMapSearchResults()
        {
            var map = Find.CurrentMap;
            if (map == null || !MapNavigationState.IsInitialized)
            {
                TolkHelper.Speak("RimWorldAccess.Map.Search.NoMap".Loc(), SpeechPriority.High);
                return;
            }

            var cursor = MapNavigationState.CurrentCursorPosition;

            // Fog-of-war regions all share one label, so searching them is noise.
            var allItems = CollectAllMapItemsFlat(map, cursor)
                .Where(item => item.Label != ScannerHelper.UnexploredAreaLabel)
                .ToList();

            var matching = ScannerSearchEngine.FilterAndRank(
                allItems, searchBuffer, item => item.Label, item => item.Distance);

            matching = GroupIdenticalItems(matching, cursor);

            if (matching.Count == 0)
            {
                // Clear the buffer so the player can type again.
                TolkHelper.Speak("RimWorldAccess.Search.NoMatches".Loc(searchBuffer), SpeechPriority.Normal);
                searchBuffer = "";
                return;
            }

            ScannerState.CreateTemporaryCategory("RimWorldAccess.Map.Search.CategoryName".Translate(searchBuffer), matching);

            AnnounceSearchResults(matching);
        }

        private static void UpdateWorldSearchResults()
        {
            if (!WorldNavigationState.IsActive || !WorldNavigationState.IsInitialized)
            {
                TolkHelper.Speak("RimWorldAccess.Map.Search.WorldNavInactive".Loc(), SpeechPriority.High);
                return;
            }

            var originTile = WorldNavigationState.CurrentSelectedTile;

            var allItems = CollectAllWorldItemsFlat();

            var matching = ScannerSearchEngine.FilterAndRank(
                allItems, searchBuffer,
                item => item.Label,
                item => item.GetDistance(originTile, 0));

            if (matching.Count == 0)
            {
                // Clear the buffer so the player can type again.
                TolkHelper.Speak("RimWorldAccess.Search.NoMatches".Loc(searchBuffer), SpeechPriority.Normal);
                searchBuffer = "";
                return;
            }

            WorldScannerState.CreateTemporaryCategory("RimWorldAccess.Map.Search.CategoryName".Translate(searchBuffer), matching);

            AnnounceWorldSearchResults(matching);
        }

        /// <summary>
        /// Every scanner item, flattened from the "All"/"All-All" subcategory. Do NOT flatten
        /// every subcategory instead: each one's own GroupIdenticalItems pass mints a distinct
        /// bulk item over the same Things, which reference dedup cannot catch, so the count would
        /// come out too high.
        /// </summary>
        private static List<ScannerItem> CollectAllMapItemsFlat(Map map, IntVec3 cursorPosition)
        {
            var categories = ScannerHelper.CollectMapItems(map, cursorPosition);
            return FlattenFromAllCategory(categories);
        }

        /// <summary>
        /// Every world scanner item, flattened.
        /// </summary>
        private static List<WorldScannerItem> CollectAllWorldItemsFlat()
        {
            return WorldScannerState.CollectAllItemsFlat();
        }

        /// <summary>Search results keep their relevance order; grouping by type would lose it.</summary>
        private static List<ScannerItem> GroupIdenticalItems(List<ScannerItem> items, IntVec3 cursorPosition)
        {
            var grouped = new List<ScannerItem>();
            var processedLabels = new HashSet<string>();

            foreach (var item in items)
            {
                grouped.Add(item);
            }

            return grouped;
        }

        private static void AnnounceSearchResults(List<ScannerItem> results)
        {
            if (results.Count == 0)
            {
                TolkHelper.Speak("RimWorldAccess.Map.Search.NoResults".Loc(), SpeechPriority.Normal);
                return;
            }

            string firstResult = results[0].Label;
            int count = results.Count;

            if (count == 1)
            {
                TolkHelper.Speak("RimWorldAccess.Map.Search.Result.One".Loc(firstResult), SpeechPriority.Normal);
            }
            else
            {
                TolkHelper.Speak("RimWorldAccess.Map.Search.Result.Many".Loc(firstResult, count), SpeechPriority.Normal);
            }
        }

        private static void AnnounceWorldSearchResults(List<WorldScannerItem> results)
        {
            if (results.Count == 0)
            {
                TolkHelper.Speak("RimWorldAccess.Map.Search.NoResults".Loc(), SpeechPriority.Normal);
                return;
            }

            string firstResult = results[0].Label;
            int count = results.Count;

            if (count == 1)
            {
                TolkHelper.Speak("RimWorldAccess.Map.Search.Result.One".Loc(firstResult), SpeechPriority.Normal);
            }
            else
            {
                TolkHelper.Speak("RimWorldAccess.Map.Search.Result.Many".Loc(firstResult, count), SpeechPriority.Normal);
            }
        }

    }
}
