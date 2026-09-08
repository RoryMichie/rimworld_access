using Verse;

namespace RimWorldAccess.Shell
{
    /// <summary>
    /// The Shift+R key that opens Vehicle Framework's standalone vehicle route planner selector
    /// (<see cref="VfRoutePlannerCompat.OpenSelectorDialog"/>), the vehicle-aware sibling of the bare-R
    /// vanilla opener (see WorldScope.RoutePlanner.Game.cs). Gate mirrors
    /// <c>RoutePlannerOpenerLive</c>'s own terms verbatim, with both planners' own IsActive checked
    /// (mutual exclusion is symmetric -- see the matching <c>!VfRoutePlannerCompat.IsActive</c> term
    /// added to <c>RoutePlannerOpenerLive</c> itself) plus <see cref="VfRoutePlannerCompat.Ready"/>,
    /// since this opener has no game-side fallback when Vehicle Framework is absent.
    /// </summary>
    public sealed partial class WorldScope
    {
        private void RegisterVfRoutePlannerClaims()
        {
            Claim("world.vfRoutePlanner.open",
                delegate (KeyEventSnapshot e) { VfRoutePlannerCompat.OpenSelectorDialog(); },
                when: VfRoutePlannerOpenerLive);
        }

        private static bool VfRoutePlannerOpenerLive()
        {
            return Current.ProgramState == ProgramState.Playing
                && WorldNavigationState.IsActive
                && !ShellGuards.MenuOwnsInput()
                && !CaravanFormationState.IsActive
                && !RoutePlannerState.IsActive
                && !VfRoutePlannerCompat.IsActive
                && VfRoutePlannerCompat.Ready;
        }
    }
}
