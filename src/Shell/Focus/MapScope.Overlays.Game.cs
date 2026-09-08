using Verse;

namespace RimWorldAccess.Shell
{
    /// <summary>
    /// Wave-C ambient openers for the mod-owned map overlays. The overlay
    /// itself (its typing/navigation) lives in a dedicated scope pushed by
    /// <see cref="MapOverlayScopeMirror"/>; the KEY that opens it is an ambient
    /// map action, so it is a claim here on <see cref="MapScope"/>, guarded by
    /// the retired opener rung's gate verbatim.
    /// </summary>
    public sealed partial class MapScope
    {
        private void RegisterMapOverlayOpenerClaims()
        {
            Claim("map.goTo", delegate (KeyEventSnapshot e) { GoToState.Activate(); }, when: GoToOpenerLive);
        }

        /// <summary>The retired Ctrl+G opener rung's gate (PRIORITY 4.746). Local map
        /// only; yields to an accessibility menu unless the player is choosing a cell
        /// (<see cref="RimWorldAccess.ArchitectPlacementInputPatch.InCellSelectionMode"/>,
        /// which replaced this rung's own hand-copied mode list); mutually exclusive
        /// with scanner search.</summary>
        private static bool GoToOpenerLive()
        {
            return Current.ProgramState == ProgramState.Playing
                && MapNavigationState.IsInitialized
                && !WorldNavigationState.IsActive
                && !ScannerSearchState.IsActive
                && !GoToState.IsActive
                && (Find.WindowStack == null || !Find.WindowStack.WindowsPreventCameraMotion)
                && (!ShellGuards.MenuOwnsInput() || ArchitectPlacementInputPatch.InCellSelectionMode());
        }
    }
}
