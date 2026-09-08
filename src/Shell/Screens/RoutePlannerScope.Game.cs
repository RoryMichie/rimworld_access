using Verse;

namespace RimWorldAccess.Shell
{
    /// <summary>
    /// Focus scope for the world route planner overlay, wrapping the game's real
    /// <c>WorldRoutePlanner</c> through <see cref="RoutePlannerState"/>, whose handlers
    /// speak every word — the scope has no content region of its own.
    /// Non-modal by explicit override: only five keys are claimed, so the world cursor
    /// keeps moving beneath it via <see cref="WorldScope"/>.
    /// The <c>NotGizmoActive</c> gate covers all five claims, not just the two stack
    /// order protects: <see cref="GizmoScope"/> never claims Space.
    /// Escape closing here makes vanilla's own <c>WorldRoutePlannerOnGUI</c> Escape
    /// branch unreachable the same frame; see
    /// <see cref="RoutePlannerState.HandleCancel"/>.
    /// </summary>
    public sealed class RoutePlannerScope : FocusScope
    {
        public RoutePlannerScope()
        {
            Claim("route.addWaypoint", delegate { RoutePlannerState.HandleAddWaypoint(); }, when: NotGizmoActive);
            Claim("route.removeWaypoint", delegate { RoutePlannerState.HandleRemoveWaypoint(); }, when: NotGizmoActive);
            Claim("route.eta", delegate { RoutePlannerState.HandleAnnounceETA(); }, when: NotGizmoActive);
            Claim("route.confirm", delegate { RoutePlannerState.HandleEnterKey(); }, when: NotGizmoActive);
            Claim(SharedMenuGrammar.Cancel, delegate { RoutePlannerState.HandleCancel(); }, when: NotGizmoActive);
        }

        public override string Name
        {
            get { return "route-planner"; }
        }

        public override bool IsModal
        {
            get { return false; }
        }

        public override bool IsLive
        {
            get { return true; }
        }

        private static bool NotGizmoActive()
        {
            return !GizmoNavigationState.IsActive;
        }
    }

    /// <summary>
    /// Keeps <see cref="RoutePlannerScope"/> in lockstep with
    /// <see cref="RoutePlannerState.IsActive"/>.
    /// Must reconcile BEFORE <c>MapOverlayScopeMirror</c> (ShellDispatcher.Game.cs):
    /// <c>FocusStackCore.Push</c> re-floats to the true top, so ScannerSearchScope's
    /// later push lands above this scope and wins Enter/Escape.
    /// </summary>
    internal static class RoutePlannerScopeMirror
    {
        private static readonly RoutePlannerScope scope = new RoutePlannerScope();

        public static void Reconcile()
        {
            // Must short-circuit before RoutePlannerState.IsActive, which dereferences
            // Find.WorldRoutePlanner and throws during Entry while no world exists. The
            // planner is reachable in Entry from the world-gen starting-site screen, where
            // Current.Game?.World != null means "a generated world is loaded".
            bool stateSafe = Current.ProgramState == ProgramState.Playing
                || (Current.ProgramState == ProgramState.Entry && Current.Game?.World != null);
            if (stateSafe && RoutePlannerState.IsActive)
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
