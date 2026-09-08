namespace RimWorldAccess.Shell
{
    /// <summary>
    /// The keyboard focus scope for Vehicle Framework's standalone vehicle route planner
    /// (<c>Vehicles.World.VehicleRoutePlanner</c>), opened via
    /// <see cref="VfRoutePlannerCompat.OpenSelectorDialog"/> through the world-map Shift+R opener.
    /// REUSES the vanilla planner's own five route.* action ids: the vanilla and VF planners are
    /// mutually exclusive (each opener's own gate excludes the other), so no new action ids are
    /// registered for this scope.
    ///
    /// Non-modal (an explicit override -- the FocusScope default is modal): this scope claims only
    /// the five route keys below and lets everything else, including arrows and the world
    /// scanner's PageUp/PageDown, fall through to WorldScope's ambient claims beneath it, so the
    /// cursor keeps moving while a route is being planned.
    ///
    /// The NotGizmoActive when-gate is what stops GizmoScope's own unconditional Activate/Cancel
    /// claims from winning Enter/Escape by stack order alone whenever GizmoScope is live above
    /// this scope, its blockChar block from winning E, and Space/Shift+Space from incorrectly
    /// reaching these claims.
    ///
    /// Opener instructions fire from OnPush, not per-refocus. There is no mod-owned opener call
    /// site here (the VF planner starts from Dialog_VehicleSelector's own real Start button,
    /// vehicle A), so this scope speaks its own instructions. OnPush fires exactly once per new
    /// planner session -- FocusStackCore.Push only calls it on the actual push transition, not on
    /// every reconcile-driven re-push -- so resetting the flag there rather than in the
    /// constructor (this is a persistent singleton reused across sessions) gives one instruction
    /// announcement per open without re-speaking it whenever an overlay above this scope pops.
    /// </summary>
    internal sealed class VfRoutePlannerScope : FocusScope
    {
        private bool announcedOpen;

        public VfRoutePlannerScope()
        {
            Claim("route.addWaypoint", delegate { VfRoutePlannerCompat.HandleAddWaypoint(); }, when: NotGizmoActive);
            Claim("route.removeWaypoint", delegate { VfRoutePlannerCompat.HandleRemoveWaypoint(); }, when: NotGizmoActive);
            Claim("route.eta", delegate { VfRoutePlannerCompat.HandleAnnounceETA(); }, when: NotGizmoActive);
            Claim("route.confirm", delegate { VfRoutePlannerCompat.HandleEnterKey(); }, when: NotGizmoActive);
            Claim(SharedMenuGrammar.Cancel, delegate { VfRoutePlannerCompat.HandleCancel(); }, when: NotGizmoActive);
        }

        public override string Name
        {
            get { return "vf-route-planner"; }
        }

        public override bool IsModal
        {
            get { return false; }
        }

        public override bool IsLive
        {
            get { return true; }
        }

        public override void OnPush()
        {
            base.OnPush();
            announcedOpen = false;
        }

        public override void OnFocus()
        {
            base.OnFocus();
            if (announcedOpen)
                return;
            announcedOpen = true;
            TolkHelper.Speak("RimWorldAccess.Compat.Vf.RoutePlannerOpenerInstructions".Loc(), SpeechPriority.Normal);
        }

        private static bool NotGizmoActive()
        {
            return !GizmoNavigationState.IsActive;
        }
    }
}
