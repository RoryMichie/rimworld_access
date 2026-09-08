namespace RimWorldAccess.Shell
{
    /// <summary>
    /// World scanner browse plus the in-game-only caravan-cycle cluster: Period/Comma cycle,
    /// Ctrl+Space toggle-selection, Alt+C jump-to-selected, I caravan-inspect, Enter world-object
    /// open. Every action id is pre-registered in the catalog, so this file adds none.
    ///
    /// The thirteen <c>world.scanner.*</c> claims live on <see cref="WorldMapElement"/>, the
    /// definition this scope shares with the starting-site screen; the caravan cluster and the two
    /// openers stay here because they are in-game world VIEW behavior, not map ELEMENT behavior.
    /// <see cref="WorldScannerBrowseLive"/> also stays here and is handed to the element through
    /// its profile.
    ///
    /// <b>Per-key gating.</b> The gates below are layered per key group rather than blanket, because
    /// the states that can sit above this rung each consume a different subset:
    /// WorldObjectSelectionState and StatBreakdownState are TreeNavigationHelper-backed and consume
    /// every real key, so every claim here needs both terms; ScannerSearchState consumes only
    /// Enter/Escape/Backspace/bare letters and digits, so it belongs on the I and Enter claims ONLY.
    /// Adding ScannerSearchState to the scanner-browse claims would be a regression: PageUp while a
    /// world scanner search is active must keep navigating the scanner, and nothing else would pick
    /// the key up.
    ///
    /// I unconditionally calls WorldNavigationState.ShowCaravanInspect(), which announces
    /// RimWorldAccess.World.NoCaravanSelected when none is selected; there is no tile-info fallback
    /// despite what the catalog registration comment says.
    /// </summary>
    public sealed partial class WorldScope
    {
        private void RegisterScannerClaims()
        {
            // ---- In-game-only caravan-cycle cluster (Period/Comma/Ctrl+Space/Alt+C) ----
            // Same term set as the PageUp/PageDown group, plus Context==InGame.
            Claim("world.caravan.cycleNext", delegate (KeyEventSnapshot e) { WorldNavigationState.CycleToNextCaravan(); }, when: WorldCaravanKeysLive);
            Claim("world.caravan.cyclePrevious", delegate (KeyEventSnapshot e) { WorldNavigationState.CycleToPreviousCaravan(); }, when: WorldCaravanKeysLive);
            Claim("world.caravan.toggleSelection", delegate (KeyEventSnapshot e) { WorldNavigationState.ToggleCaravanSelection(); }, when: WorldCaravanKeysLive);
            Claim("world.caravan.jumpToSelected", delegate (KeyEventSnapshot e) { WorldNavigationState.JumpToSelectedCaravans(); }, when: WorldCaravanKeysLive);

            // I: caravan inspect opener. A bare letter, so ScannerSearchState's blockChar applies
            // here unlike the caravan-cycle cluster above. The !GizmoNavigationState.IsActive term
            // is redundant (already a MenuOwnsInput member) but kept.
            Claim("world.caravan.inspect", delegate (KeyEventSnapshot e) { WorldNavigationState.ShowCaravanInspect(); }, when: CaravanInspectOpenerLive);

            // Enter: open world-object selection at the current tile. Adds ScannerSearchState (it
            // has an explicit Enter branch), !RoutePlannerState.IsActive (the route planner claims
            // Enter first), and the currentTile.Valid check — folded into the GATE rather than the
            // handler so an invalid cursor position falls through unclaimed.
            Claim("world.object.select", OnOpenWorldObjectSelection, when: WorldObjectSelectLive);
        }

        /// <summary>
        /// Base gate for the PageUp/PageDown/Alt+J and Home/End groups: the two
        /// TreeNavigationHelper-backed terms (WorldObjectSelectionState, redundant-but-harmless
        /// since it is a ShellGuards.MenuOwnsInput member; StatBreakdownState, a genuine non-member).
        /// ScannerSearchState is deliberately absent — see the class remarks.
        /// </summary>
        private static bool WorldScannerBrowseLive()
        {
            return WorldNavigationState.IsActive
                && !ShellGuards.MenuOwnsInput()
                && !WorldObjectSelectionState.IsActive
                && !StatBreakdownState.IsActive;
        }

        /// <summary>
        /// WorldScannerBrowseLive plus the in-game Context check: the caravan-cycle cluster's gate.
        /// ScannerSearchState is deliberately absent here too.
        /// </summary>
        private static bool WorldCaravanKeysLive()
        {
            return WorldScannerBrowseLive()
                && WorldNavigationState.Context == WorldNavContext.InGame;
        }

        /// <summary>
        /// The I-key branch's extra terms: ScannerSearchState (consumes bare letters) and
        /// !GizmoNavigationState.IsActive.
        /// </summary>
        private static bool CaravanInspectOpenerLive()
        {
            return WorldCaravanKeysLive()
                && !ScannerSearchState.IsActive
                && !GizmoNavigationState.IsActive;
        }

        /// <summary>
        /// The Enter branch's extra terms: ScannerSearchState, !RoutePlannerState.IsActive, and the
        /// currentTile.Valid check.
        /// </summary>
        private static bool WorldObjectSelectLive()
        {
            return WorldCaravanKeysLive()
                && !ScannerSearchState.IsActive
                && !RoutePlannerState.IsActive
                && WorldNavigationState.CurrentSelectedTile.Valid;
        }

        private static void OnOpenWorldObjectSelection(KeyEventSnapshot e)
        {
            WorldObjectSelectionState.Open(WorldNavigationState.CurrentSelectedTile);
        }
    }
}
