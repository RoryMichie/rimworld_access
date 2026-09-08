using RimWorld;
using Verse;

namespace RimWorldAccess.Shell
{
    /// <summary>
    /// The keyboard focus scope for the targeting/cursor family: a stateless router over ten
    /// targeting state machines. NON-MODAL, unlike most scopes: targeting, placement and cursor
    /// modes need arrows and every unclaimed key to fall through to live map/world navigation, so
    /// no member joins <see cref="ShellGuards.MenuOwnsInput"/>, MapScope's folded MenuOverlayGuard
    /// gates, or the camera-pan suppression list. This scope must never join those master
    /// switches, and claims only the keys each member owns.
    ///
    /// Three underlying targeter kinds:
    /// <list type="bullet">
    /// <item>World-tile pick (<see cref="RimWorld.Planet.WorldTargeter"/>):
    /// <see cref="TransportPodLaunchState"/>, <see cref="WorldAbilityTargetingState"/>, and
    /// <see cref="ExternalWorldTargeting"/> — the same shape driven through the external-provider
    /// seam (Vehicle Framework's SmashTools targeter is the first provider), which adds a
    /// Backspace waypoint-pop claim for its multi-leg flight path.</item>
    /// <item>Tile picker (<see cref="RimWorld.Planet.TilePicker"/>):
    /// <see cref="GravshipDestinationState"/>, <see cref="NewColonyTilePickState"/>.</item>
    /// <item>Map <see cref="Targeter"/>: <see cref="JumpTargetingState"/>,
    /// <see cref="AbilityTargetingState"/>, <see cref="GenericTargetingState"/>,
    /// <see cref="ItemTargetingState"/>, and the Command_Target range-check context in
    /// <see cref="TargetingPatch"/>.</item>
    /// </list>
    /// <see cref="PlantTargetingState"/> needs no claim here: it rides the generic Command_Target
    /// path inside <see cref="TargetingPatch"/>.Prefix and has no R/T key. Only its per-frame
    /// confirmation-dialog poll lives in <see cref="TargetingScopeMirror"/>.Reconcile.
    ///
    /// <b>Enter/Escape ownership per variant — the dangerous edge.</b> For the five
    /// map-<see cref="Targeter"/> variants, Enter is converted into a target selection by
    /// <see cref="TargetingPatch"/>.Prefix, a UIRootOnGUI side-door prefix reading the KeyDown
    /// directly, and Escape is handled by vanilla's own <c>Targeter.ProcessInputEvents</c>, whose
    /// unconditional Cancel branch runs because the Prefix passes every non-Enter key through.
    /// Claiming either key for those five would steal Enter from the side door, which then never
    /// runs. This scope claims ONLY R/T for them. The world-tile and tile-picker variants have no
    /// side door — vanilla's WorldTargeter/TilePicker have no keyboard confirm — so this scope
    /// owns Enter/Escape/F for those outright.
    ///
    /// <b>WindowlessFloatMenuState coexistence.</b> No per-claim yield is needed: the dispatcher's
    /// <see cref="ShellDispatcherPatch.LegacyKeyboardOverlayActive"/> stand-down returns before any
    /// shell scope is dispatched while a windowless float menu is active, the same blanket
    /// coverage <see cref="GizmoScope"/> and <see cref="QuestMenuScope"/> rely on.
    ///
    /// <b>R-precedence over the ambient draft claim</b> comes from stack position, not code:
    /// <see cref="TargetingScopeMirror"/> pushes this scope above the always-present
    /// <see cref="MapScope"/>, and <see cref="FocusStackCore.Dispatch"/> walks top-down, so R
    /// reaches these when:-gated claims before MapScope's map.draft.toggle.
    ///
    /// Every claim delegates straight to the owning state's public method; no key-comparison logic
    /// is duplicated here.
    /// </summary>
    public sealed class TargetingScope : FocusScope
    {
        public TargetingScope()
        {
            // World-tile pick: pod launch.
            Claim("podLaunch.confirm", delegate { TransportPodLaunchState.ConfirmCurrentDestination(); },
                when: TransportPodLaunchLive);
            Claim("podLaunch.cancel", delegate { TransportPodLaunchState.CancelTargeting(); },
                when: TransportPodLaunchLive);
            Claim("podLaunch.fuelStatus", delegate { TransportPodLaunchState.AnnounceFuelStatus(); },
                when: TransportPodLaunchLive);

            // Tile picker: gravship destination.
            Claim("gravshipDest.confirm", delegate { GravshipDestinationState.HandleConfirm(); },
                when: GravshipDestLive);
            Claim("gravshipDest.cancel", delegate { GravshipDestinationState.CancelTargeting(); },
                when: GravshipDestLive);
            Claim("gravshipDest.fuelStatus", delegate { GravshipDestinationState.AnnounceFuelStatus(); },
                when: GravshipDestLive);

            // Tile picker: Archonexus new-colony pick. allowEscape is false — Escape announces
            // rather than cancels.
            Claim("newColonyTile.confirm", delegate { NewColonyTilePickState.ConfirmCurrentTile(); },
                when: NewColonyTileLive);
            Claim("newColonyTile.cancel", delegate { NewColonyTilePickState.AnnounceCannotCancel(); },
                when: NewColonyTileLive);

            // World-tile pick: world ability targeting (Farskip).
            Claim("worldAbilityTargeting.confirm", delegate { WorldAbilityTargetingState.ConfirmCurrentDestination(); },
                when: WorldAbilityTargetingLive);
            Claim("worldAbilityTargeting.cancel", delegate { WorldAbilityTargetingState.CancelTargeting(); },
                when: WorldAbilityTargetingLive);

            // World-tile pick through the external-provider seam; see
            // <see cref="ExternalWorldTargeting"/>.
            Claim("externalWorldTargeting.confirm", delegate { ExternalWorldTargeting.ActiveProvider?.Confirm(); },
                when: ExternalWorldTargetingLive);
            Claim("externalWorldTargeting.cancel", delegate { ExternalWorldTargeting.ActiveProvider?.Cancel(); },
                when: ExternalWorldTargetingLive);
            Claim("externalWorldTargeting.popWaypoint", delegate
            {
                bool popped = ExternalWorldTargeting.ActiveProvider?.PopWaypoint() ?? false;
                if (!popped)
                    TolkHelper.Speak("RimWorldAccess.Compat.Vf.WorldNoWaypoint".Loc());
            }, when: ExternalWorldTargetingLive);
            Claim("externalWorldTargeting.status", delegate
            {
                string info = ExternalWorldTargeting.ActiveProvider?.DestinationInfo(WorldNavigationState.CurrentSelectedTile);
                if (!string.IsNullOrEmpty(info))
                    TolkHelper.SpeakData(info);
                else
                    TolkHelper.Speak("RimWorldAccess.Compat.Vf.WorldNoDestinationInfo".Loc());
            }, when: ExternalWorldTargetingLive);

            // Map Targeter: R/T only. Enter/Escape stay with the TargetingPatch.Prefix side door
            // and vanilla's Targeter.ProcessInputEvents.
            Claim("jumpTargeting.range", delegate { JumpTargetingState.AnnounceRangeInfo(); },
                when: JumpTargetingLive);

            Claim("abilityTargeting.range", delegate { AbilityTargetingState.AnnounceRangeInfo(); },
                when: AbilityTargetingLive);
            Claim("abilityTargeting.affectedTargets", delegate { AbilityTargetingState.AnnounceAffectedTargets(); },
                when: AbilityTargetingLive);

            Claim("genericTargeting.range", delegate { GenericTargetingState.AnnounceRangeInfo(); },
                when: GenericTargetingLive);

            Claim("itemTargeting.range", delegate { ItemTargetingState.AnnounceRangeInfo(); },
                when: ItemTargetingLive);
            // T is consumed silently to block the game's time/weather shortcut; see
            // ItemTargetingState.AnnounceRangeInfo.
            Claim("itemTargeting.affectedTargets", delegate { }, when: ItemTargetingLive);

            Claim("commandTargeting.range", delegate { TargetingPatch.HandleRangeCheck(); },
                when: CommandTargetingLive);
        }

        public override string Name
        {
            get { return "targeting"; }
        }

        public override bool IsModal
        {
            get { return false; }
        }

        public override bool IsLive
        {
            get { return true; }
        }

        private static bool TransportPodLaunchLive()
        {
            return TransportPodLaunchState.IsActive;
        }

        private static bool GravshipDestLive()
        {
            return GravshipDestinationState.IsActive;
        }

        private static bool NewColonyTileLive()
        {
            return NewColonyTilePickState.IsActive;
        }

        private static bool WorldAbilityTargetingLive()
        {
            return WorldAbilityTargetingState.IsActive;
        }

        private static bool ExternalWorldTargetingLive()
        {
            return ExternalWorldTargeting.Active;
        }

        private static bool JumpTargetingLive()
        {
            return JumpTargetingState.IsActive;
        }

        private static bool AbilityTargetingLive()
        {
            return AbilityTargetingState.IsActive;
        }

        private static bool GenericTargetingLive()
        {
            return GenericTargetingState.IsActive;
        }

        private static bool ItemTargetingLive()
        {
            return ItemTargetingState.IsActive;
        }

        private static bool CommandTargetingLive()
        {
            return TargetingPatch.HasTargetingContext && Find.Targeter.IsTargeting;
        }
    }

    /// <summary>
    /// Keeps <see cref="TargetingScope"/> in lockstep with the family members' IsActive flags,
    /// reconciled every OnGUI pass by the shell dispatcher, and carries two pieces of per-frame
    /// housekeeping:
    ///
    /// <list type="bullet">
    /// <item>Lazy self-close for <see cref="TransportPodLaunchState"/> and
    /// <see cref="NewColonyTilePickState"/>, the two members with no dedicated close hook. Running
    /// the staleness check here rather than on keypress catches the targeter stopping via a mouse
    /// click or a quest script. <see cref="GravshipDestinationState"/> and
    /// <see cref="WorldAbilityTargetingState"/> each have their own closing postfix and need
    /// none.</item>
    /// <item><see cref="PlantTargetingState"/>'s confirmation-dialog watch, which must run on
    /// every OnGUI pass rather than only on keypresses. It has no push/pop membership here, so the
    /// call is pure housekeeping, independent of the push/pop decision below.</item>
    /// </list>
    ///
    /// <b>Reconcile order:</b> wired into ShellDispatcherPatch.Prefix BEFORE MapOverlayScopeMirror.
    /// Push always re-floats a scope to the top of the stack, so whichever mirror reconciles LAST
    /// wins that slot; going first here lets GoTo and colony scanner-search re-float above this
    /// scope whenever both are somehow active. The two families are not expected to coexist.
    /// </summary>
    internal static class TargetingScopeMirror
    {
        private static readonly TargetingScope scope = new TargetingScope();

        public static void Reconcile()
        {
            if (TransportPodLaunchState.IsActive
                && (Find.WorldTargeter == null || !Find.WorldTargeter.IsTargeting))
            {
                TransportPodLaunchState.Close();
            }

            if (NewColonyTilePickState.IsActive
                && (Find.TilePicker == null || !Find.TilePicker.Active))
            {
                NewColonyTilePickState.Close();
            }

            PlantTargetingState.WatchConfirmationDialog();

            // Real-dialog stand-down: TransportPodLaunchState deliberately keeps its targeting
            // session alive under the hostile-settlement Dialog_MessageBox, so an unconditional
            // Push here would re-float above the dialog's own scope and eat its Enter/Escape.
            if (AnyFamilyMemberActive() && !ShellGuards.ForeignInputOwningWindowAbove())
            {
                FocusStack.Push(scope);
            }
            else
            {
                FocusStack.Pop(scope);
            }
        }

        private static bool AnyFamilyMemberActive()
        {
            return TransportPodLaunchState.IsActive
                || GravshipDestinationState.IsActive
                || NewColonyTilePickState.IsActive
                || JumpTargetingState.IsActive
                || AbilityTargetingState.IsActive
                || GenericTargetingState.IsActive
                || ItemTargetingState.IsActive
                || (TargetingPatch.HasTargetingContext && Find.Targeter.IsTargeting)
                || WorldAbilityTargetingState.IsActive
                || ExternalWorldTargeting.Active;
        }
    }
}
