using Verse;

namespace RimWorldAccess.Shell
{
    /// <summary>
    /// Keeps <see cref="VfRoutePlannerScope"/> in lockstep with
    /// <see cref="VfRoutePlannerCompat.IsActive"/>. The Playing/Entry program-state disjunction is
    /// evaluated BEFORE the IsActive read so a throwing getter chain is never touched during Entry.
    /// Registered immediately after RoutePlannerScopeMirror in MirrorReconcileOrder.Game.cs: both
    /// must reconcile before MapOverlayScopeMirror so ScannerSearchScope's own Enter/Escape claims
    /// win when a scanner search is active over an open route planner.
    /// </summary>
    internal static class VfRoutePlannerScopeMirror
    {
        private static readonly VfRoutePlannerScope scope = new VfRoutePlannerScope();

        public static void Reconcile()
        {
            bool stateSafe = Current.ProgramState == ProgramState.Playing
                || (Current.ProgramState == ProgramState.Entry && Current.Game?.World != null);
            if (stateSafe && VfRoutePlannerCompat.IsActive)
            {
                FocusStack.Push(scope);
            }
            else
            {
                FocusStack.Pop(scope);
            }
        }
    }
}
