using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using RimWorld;
using Verse;

namespace RimWorldAccess
{
    /// <summary>
    /// Accessibility state for CompPlantable seed planting (Gauranlen seeds and any modded
    /// plantable item), replacing the ghost overlay and rings sighted players use to find a
    /// spot: a terse "Can plant" / "Blocked" prefix per tile during arrow navigation, plus a
    /// temporary scanner category of every plantable spot, adjacency-grouped and nearest first.
    ///
    /// Opened by a prefix on CompPlantable.BeginTargeting (see PlantTargetingPatch), closed by
    /// Targeter.StopTargeting. All validation defers to CompPlantable.CanPlantAt so wording and
    /// rules match vanilla and any mods that extend planting.
    /// </summary>
    public static class PlantTargetingState
    {
        private static bool isActive = false;
        private static CompPlantable comp = null;
        private static Map map = null;

        // Tracks a building-proximity confirmation dialog so cancelling it re-opens placement
        // instead of kicking the user out. Independent of the session fields above, which
        // Close() clears when the dialog opens.
        private static CompPlantable pendingDialogComp = null;
        private static int pendingDialogPlantCellCount = 0;
        private static Window pendingDialog = null;

        private static readonly MethodInfo BeginTargetingMethod =
            AccessTools.Method(typeof(CompPlantable), "BeginTargeting");

        private static readonly MethodInfo ConnectionStrengthReducedMethod =
            AccessTools.Method(typeof(CompPlantable), "ConnectionStrengthReducedByNearbyBuilding");

        public static bool IsActive => isActive;

        /// <summary>
        /// Opens (or refreshes) planting placement state for the given plantable comp. An
        /// invalid-cell retry re-enters with the same comp: keep the session and stay silent so
        /// the start announcement isn't repeated.
        /// </summary>
        public static void Open(CompPlantable plantable)
        {
            if (plantable == null)
                return;

            bool sameSession = isActive && comp == plantable;

            comp = plantable;
            map = plantable.parent?.MapHeld;
            isActive = true;

            if (sameSession || map == null)
                return;

            string treeLabel = plantable.Props?.plantDefToSpawn?.LabelCap
                ?? "RimWorldAccess.Abilities.Label.Location".Translate().ToString();

            // Build the scanner category first so the start announcement can report its area count.
            int areaCount = BuildScannerCategory(treeLabel, out int clearAreaCount, out bool buildingsReduceStrength);

            string start = areaCount > 0
                ? "RimWorldAccess.Abilities.Plant.Start".Translate(treeLabel, areaCount)
                : "RimWorldAccess.Abilities.Plant.StartNoAreas".Translate(treeLabel);

            // Where building proximity matters (Gauranlen connection strength), the red rings a
            // sighted player reads become the clear-of-buildings suffix.
            if (areaCount > 0 && buildingsReduceStrength)
            {
                start += " " + (clearAreaCount > 0
                    ? (string)"RimWorldAccess.Abilities.Plant.StartClearSuffix".Translate(clearAreaCount)
                    : (string)"RimWorldAccess.Abilities.Plant.StartNoneClearSuffix".Translate());
            }

            TolkHelper.SpeakData(start, SpeechPriority.Normal);
        }

        /// <summary>Closes planting placement state and restores the user's scanner focus.</summary>
        public static void Close()
        {
            isActive = false;
            comp = null;
            map = null;

            ScannerState.RemoveTemporaryCategory();
            ScannerState.RestoreFocus();
        }

        /// <summary>
        /// Clears all session and pending-dialog state without side effects. Load fires no Close
        /// path, and a stale pending dialog would make WatchConfirmationDialog re-open targeting
        /// on an orphaned Thing from the previous session (which still reports Spawned == true).
        /// </summary>
        public static void Reset()
        {
            isActive = false;
            comp = null;
            map = null;
            pendingDialog = null;
            pendingDialogComp = null;
            pendingDialogPlantCellCount = 0;
        }

        /// <summary>
        /// Called by TargetingPatch, while planting is still active, when confirming a cell opened
        /// a confirmation dialog. Records what WatchConfirmationDialog needs to tell confirm (a
        /// plant cell was added) from cancel. Deliberately leaves the active-session fields alone;
        /// Close() is about to clear them.
        /// </summary>
        public static void NotifyConfirmationDialogOpened()
        {
            if (comp == null)
                return;

            // Message boxes are real windows, so the dialog is on the real WindowStack.
            Window dialog = Find.WindowStack?.WindowOfType<Dialog_MessageBox>();
            if (dialog == null)
                return;

            pendingDialogComp = comp;
            pendingDialogPlantCellCount = comp.PlantCells?.Count ?? 0;
            pendingDialog = dialog;
        }

        /// <summary>
        /// Per-frame check from TargetingScope's claim handling. Once a tracked confirmation
        /// dialog closes, re-opens placement if the user cancelled so they can try another spot.
        /// </summary>
        public static void WatchConfirmationDialog()
        {
            if (pendingDialog == null)
                return;

            if (Find.WindowStack != null && Find.WindowStack.IsOpen(pendingDialog))
                return;

            CompPlantable reopenComp = pendingDialogComp;
            int beforeCount = pendingDialogPlantCellCount;

            // Clear before any re-open so this never double-fires.
            pendingDialog = null;
            pendingDialogComp = null;
            pendingDialogPlantCellCount = 0;

            if (reopenComp == null)
                return;

            // A queued plant cell means confirmed: placement ended as normal.
            if ((reopenComp.PlantCells?.Count ?? 0) > beforeCount)
                return;

            // Cancelled — re-open only if the seed is still around to plant.
            if (reopenComp.parent == null || reopenComp.parent.Destroyed || reopenComp.parent.MapHeld == null)
                return;

            BeginTargetingMethod?.Invoke(reopenComp, null);
        }

        /// <summary>
        /// Terse prefix for per-tile announcements, called on every cursor move. Kept short so
        /// sweeping is fast; the R key gives the full reason for a blocked cell.
        /// </summary>
        public static string GetPlantValidityPrefix(IntVec3 position)
        {
            if (!isActive || comp == null || map == null || !position.IsValid || !position.InBounds(map))
                return "";

            if (!comp.CanPlantAt(position, map).Accepted)
                return "RimWorldAccess.Abilities.Plant.PrefixBlocked".Translate();

            // The red lines a sighted player sees while hovering: Enter here raises the
            // reduced-connection-strength dialog.
            return IsNearArtificialBuilding(position)
                ? "RimWorldAccess.Abilities.Plant.PrefixCanPlantNearBuildings".Translate()
                : "RimWorldAccess.Abilities.Plant.PrefixCanPlant".Translate();
        }

        /// <summary>
        /// Whether planting here would trigger the game's building-proximity warning. Invokes
        /// CompPlantable's own private check so the answer matches the dialog the game raises.
        /// Single-cell queries only — this caches per queried cell permanently; a map-wide sweep
        /// goes through FilterCellsClearOfArtificialBuildings instead.
        /// </summary>
        private static bool IsNearArtificialBuilding(IntVec3 cell)
        {
            if (comp == null || ConnectionStrengthReducedMethod == null)
                return false;

            var args = new object[] { cell, null }; // args[1] is the out buildings parameter
            return ConnectionStrengthReducedMethod.Invoke(comp, args) is bool reduced && reduced;
        }

        /// <summary>
        /// Builds a temporary scanner category of every plantable cell, grouped into contiguous
        /// areas by adjacency and sorted nearest first. Carries a map-wide CanPlantAt sweep, so
        /// it runs once per session (Open() short-circuits invalid-cell retries before here).
        ///
        /// Plants with CompProperties_TreeConnection get a second "clear of buildings"
        /// subcategory, omitted when it would be redundant (no spot is affected) or empty (every
        /// spot is; the start announcement warns instead).
        ///
        /// Returns the number of plantable areas, 0 if none. buildingsReduceStrength reports
        /// whether the split matters at all.
        /// </summary>
        private static int BuildScannerCategory(string treeLabel, out int clearAreaCount, out bool buildingsReduceStrength)
        {
            clearAreaCount = 0;
            buildingsReduceStrength = false;

            if (comp == null || map == null)
                return 0;

            IntVec3 cursor = MapNavigationState.CurrentCursorPosition;
            if (!cursor.IsValid)
                cursor = comp.parent?.PositionHeld ?? map.Center;

            var validCells = new List<IntVec3>();
            foreach (IntVec3 cell in map.AllCells)
            {
                if (cell.Fogged(map))
                    continue;
                if (comp.CanPlantAt(cell, map).Accepted)
                    validCells.Add(cell);
            }

            if (validCells.Count == 0)
                return 0;

            List<TerrainRegion> regions = ScannerHelper.GroupTerrainByAdjacency(validCells, cursor);
            if (regions == null || regions.Count == 0)
                return 0;

            string itemLabel = "RimWorldAccess.Abilities.Plant.ScannerItemLabel".Translate();
            var allSub = new ScannerSubcategory("RimWorldAccess.Abilities.Plant.SubcatAll".Translate());
            foreach (TerrainRegion region in regions)
                allSub.Items.Add(new ScannerItem(new List<TerrainRegion> { region }, itemLabel, cursor));
            var subcategories = new List<ScannerSubcategory> { allSub };

            // Only CompProperties_TreeConnection plants can trigger the warning, and the radius
            // comes from that comp — the same source the game's own check reads.
            var treeConnection = comp.Props?.plantDefToSpawn?.GetCompProperties<CompProperties_TreeConnection>();
            if (treeConnection != null)
            {
                List<IntVec3> clearCells = FilterCellsClearOfArtificialBuildings(
                    validCells, treeConnection.radiusToBuildingForConnectionStrengthLoss);

                // If every plantable cell is clear the split just duplicates "All".
                if (clearCells.Count < validCells.Count)
                {
                    buildingsReduceStrength = true;

                    if (clearCells.Count > 0)
                    {
                        // Grouped independently so a field straddling a building's radius splits
                        // into separate clear patches.
                        List<TerrainRegion> clearRegions = ScannerHelper.GroupTerrainByAdjacency(clearCells, cursor);
                        if (clearRegions != null && clearRegions.Count > 0)
                        {
                            var clearSub = new ScannerSubcategory("RimWorldAccess.Abilities.Plant.SubcatClear".Translate());
                            foreach (TerrainRegion region in clearRegions)
                                clearSub.Items.Add(new ScannerItem(new List<TerrainRegion> { region }, itemLabel, cursor));
                            subcategories.Add(clearSub);
                            clearAreaCount = clearRegions.Count;
                        }
                    }
                }
            }

            string categoryName = "RimWorldAccess.Abilities.Plant.ScannerCategory".Translate(treeLabel);

            // The scanner has one temporary-category slot, and an active search filter owns it
            // AND rebuilds it on every navigation — which would clobber this category on the
            // first Page Down. Clear it silently; the start announcement follows.
            if (ScannerSearchState.HasActiveFilter)
                ScannerSearchState.ClearSearchSilent();

            ScannerState.SaveFocus();
            ScannerState.CreateTemporaryCategory(categoryName, subcategories);
            return regions.Count;
        }

        /// <summary>
        /// The subset of cells NOT within the given radius of any spawned artificial building.
        /// Mirrors CompPlantable.ConnectionStrengthReducedByNearbyBuilding, but that path goes
        /// through ListerArtificialBuildingsForMeditation.GetForCell, which caches permanently
        /// PER QUERIED CELL — a map-wide sweep would bloat the game's cache. Instead collect the
        /// building set once via the lister's own predicate and stamp the radius around each
        /// occupied cell, matching the radial-distance semantics of the game's check.
        /// </summary>
        private static List<IntVec3> FilterCellsClearOfArtificialBuildings(List<IntVec3> cells, float radius)
        {
            var nearBuilding = new bool[map.cellIndices.NumGridCells];
            foreach (Thing thing in map.listerThings.AllThings)
            {
                if (!MeditationUtility.CountsAsArtificialBuilding(thing))
                    continue;

                foreach (IntVec3 occupied in thing.OccupiedRect())
                {
                    foreach (IntVec3 cell in GenRadial.RadialCellsAround(occupied, radius, useCenter: true))
                    {
                        if (cell.InBounds(map))
                            nearBuilding[map.cellIndices.CellToIndex(cell)] = true;
                    }
                }
            }

            var clear = new List<IntVec3>();
            foreach (IntVec3 cell in cells)
            {
                if (!nearBuilding[map.cellIndices.CellToIndex(cell)])
                    clear.Add(cell);
            }
            return clear;
        }
    }
}
