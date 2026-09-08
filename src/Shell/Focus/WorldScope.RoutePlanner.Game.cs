using Verse;

namespace RimWorldAccess.Shell
{
    /// <summary>
    /// The R key that opens the world route planner. <see cref="RoutePlannerScope"/> drives the
    /// planner's OWN keys once it is open.
    ///
    /// Most of the gate collapses into the single <c>!ShellGuards.MenuOwnsInput()</c> call every
    /// ambient World/Map opener uses; the terms below are the ones that predicate does not cover.
    /// RoutePlannerState is NOT a member of it, so it keeps an explicit term: R never re-toggles the
    /// planner through this claim, since closing belongs to RoutePlannerScope's own Escape claim.
    /// CaravanFormationState's term is redundant-but-harmless — its liveness is a live scope on the
    /// stack — and the ProgramState term is likewise kept for clarity, WorldScope only ever existing
    /// as the ambient base during Playing. No AnyLiveModal term is needed: a live modal scope masks
    /// ambient claims structurally, before the walk reaches WorldScope.
    ///
    /// R needs no ScannerSearchState term either. R's KEYDOWN is consumed at chord level by
    /// <see cref="ScannerSearchScope"/>'s own <c>scannerSearch.blockChar</c> claim, not by its
    /// CharSink, which only ever sees the separate character event; and ScannerSearchScope, whenever
    /// live, always sits above the ambient base, so its chord match is tried first regardless of
    /// reconcile order.
    /// </summary>
    public sealed partial class WorldScope
    {
        private void RegisterRoutePlannerClaims()
        {
            Claim("world.routePlanner.open", delegate (KeyEventSnapshot e) { RoutePlannerState.Open(); }, when: RoutePlannerOpenerLive);
        }

        private static bool RoutePlannerOpenerLive()
        {
            return Current.ProgramState == ProgramState.Playing
                && WorldNavigationState.IsActive
                && !ShellGuards.MenuOwnsInput()
                && !CaravanFormationState.IsActive
                && !RoutePlannerState.IsActive
                // Mutual exclusion with VF's own standalone route planner: bare R must not open the
                // vanilla one while VF's is active.
                && !VfRoutePlannerCompat.IsActive;
        }
    }
}
