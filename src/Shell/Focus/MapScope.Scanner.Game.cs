using System;
using Verse;

namespace RimWorldAccess.Shell
{
    /// <summary>
    /// The colony-map scanner's ambient browse keys: PageUp/PageDown navigation, Home to jump,
    /// Alt+Home to toggle auto-jump, End to read distance and direction. The scanner has no mode of
    /// its own, so its claims live directly on <see cref="MapScope"/> like the bookmark and
    /// cursor-hotkey claims.
    ///
    /// Scanner search TEXT ENTRY is a separate slice, driven by <see cref="ScannerSearchScope"/> via
    /// <see cref="MapOverlayScopeMirror"/>. The Z / Ctrl+Z OPENERS below are a shared registrar
    /// called once from MapScope's constructor and once from WorldScope's, since
    /// ScannerSearchState.Activate resolves map-versus-world itself.
    /// </summary>
    public sealed partial class MapScope
    {
        private void RegisterScannerBrowseClaims()
        {
            Claim("map.scanner.nextItem", ScannerHandler(ScannerState.NextItem), when: ScannerBrowseLive);
            Claim("map.scanner.nextBulk", ScannerHandler(ScannerState.NextBulkItem), when: ScannerBrowseLive);
            Claim("map.scanner.nextCategory", ScannerHandler(ScannerState.NextCategory), when: ScannerBrowseLive);
            Claim("map.scanner.nextSubcategory", ScannerHandler(ScannerState.NextSubcategory), when: ScannerBrowseLive);
            Claim("map.scanner.previousItem", ScannerHandler(ScannerState.PreviousItem), when: ScannerBrowseLive);
            Claim("map.scanner.previousBulk", ScannerHandler(ScannerState.PreviousBulkItem), when: ScannerBrowseLive);
            Claim("map.scanner.previousCategory", ScannerHandler(ScannerState.PreviousCategory), when: ScannerBrowseLive);
            Claim("map.scanner.previousSubcategory", ScannerHandler(ScannerState.PreviousSubcategory), when: ScannerBrowseLive);
            Claim("map.scanner.toggleAutoJump", ScannerHandler(ScannerState.ToggleAutoJumpMode), when: ScannerBrowseLive);
            Claim("map.scanner.readDistance", ScannerHandler(ScannerState.ReadDistanceAndDirection), when: ScannerBrowseLive);
            Claim("map.scanner.jumpToCurrent",
                delegate (KeyEventSnapshot e)
                {
                    // A Home jump ends the Z search field silently: the jump's own announcement is
                    // the only utterance.
                    ScannerSearchState.DismissSilently();
                    SubstructureOverlayState.CheckOverlayState();
                    ScannerState.JumpToCurrent(manual: true);

                    // A jump onto a placement spot the building only fits in sideways turns the
                    // ghost, so Space places as normal. No-op for every other scanner category.
                    PlacementSpotScanner.ApplyFacingAtCursor();
                }, when: ScannerBrowseLive);

            RegisterScannerSearchOpenerClaims(this);
        }

        /// <summary>
        /// The shared registrar for the Z / Ctrl+Z scanner-search openers, called from both
        /// MapScope's and WorldScope's constructors. ScannerSearchState.Activate resolves onWorldMap
        /// itself from live state, so the handler needs no world-specific branch.
        /// </summary>
        internal static void RegisterScannerSearchOpenerClaims(FocusScope scope)
        {
            scope.RegisterExternalClaim("map.scanner.search", OnActivateScannerSearch, when: ScannerSearchOpenerLive);
            scope.RegisterExternalClaim("map.scanner.clearFilter", OnClearScannerSearchFilter, when: ScannerSearchClearFilterLive);
        }

        /// <summary>
        /// The outer preconditions both search claims share: Playing, a live map or world cursor,
        /// no menu owning input unless the player is choosing a cell (search coexists with cell
        /// selection), and no window blocking camera motion. The last two terms exclude the screens
        /// that consume bare Z and Ctrl+Z unconditionally through their own consume-all tails;
        /// StatBreakdownState is a genuine non-member of ShellGuards.MenuOwnsInput, while
        /// WorldObjectSelectionState is already covered by it and kept for symmetry.
        /// </summary>
        private static bool ScannerSearchBaseLive()
        {
            return Current.ProgramState == ProgramState.Playing
                && (WorldNavigationState.IsActive || MapNavigationState.IsInitialized)
                && (!ShellGuards.MenuOwnsInput() || ArchitectPlacementInputPatch.InCellSelectionMode())
                && (Find.WindowStack == null || !Find.WindowStack.WindowsPreventCameraMotion)
                && !WorldObjectSelectionState.IsActive
                && !StatBreakdownState.IsActive;
        }

        /// <summary>Z: the legacy handler's own mutual-exclusion terms against an active search and GoToState.</summary>
        private static bool ScannerSearchOpenerLive()
        {
            return ScannerSearchBaseLive()
                && !ScannerSearchState.IsActive
                && !GoToState.IsActive;
        }

        /// <summary>Ctrl+Z: the legacy handler's own terms — no active search, and a filter to clear.</summary>
        private static bool ScannerSearchClearFilterLive()
        {
            return ScannerSearchBaseLive()
                && !ScannerSearchState.IsActive
                && ScannerSearchState.HasActiveFilter;
        }

        /// <summary>
        /// Activates a new scanner search. onWorldMap is resolved from live state rather than
        /// hardcoded per scope, so this one handler is correct on MapScope and WorldScope alike.
        /// </summary>
        private static void OnActivateScannerSearch(KeyEventSnapshot e)
        {
            ScannerSearchState.Activate(WorldNavigationState.IsActive);
        }

        private static void OnClearScannerSearchFilter(KeyEventSnapshot e)
        {
            ScannerSearchState.ClearActiveFilter();
        }

        /// <summary>
        /// Wraps a bare ScannerState method as an ActionHandler, keeping the legacy handler's
        /// substructure-overlay category sync in front of every scanner action.
        /// </summary>
        private static ActionHandler ScannerHandler(Action navigate)
        {
            return delegate (KeyEventSnapshot e)
            {
                SubstructureOverlayState.CheckOverlayState();
                navigate();
            };
        }

        /// <summary>
        /// Accessibility menus block scanner navigation, except while the player is choosing a cell
        /// (<see cref="RimWorldAccess.ArchitectPlacementInputPatch.InCellSelectionMode"/>).
        /// </summary>
        private static bool ScannerBrowseLive()
        {
            return Current.ProgramState == ProgramState.Playing
                && Find.CurrentMap != null
                && MapNavigationState.IsInitialized
                && (Find.WindowStack == null || !Find.WindowStack.WindowsPreventCameraMotion)
                && (!ShellGuards.MenuOwnsInput() || ArchitectPlacementInputPatch.InCellSelectionMode());
        }
    }
}
