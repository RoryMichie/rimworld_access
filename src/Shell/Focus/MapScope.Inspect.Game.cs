using Verse;

namespace RimWorldAccess.Shell
{
    /// <summary>
    /// The Enter-opens-inspection claim. The legacy handler's contract: its
    /// real gate was its POSITION at the bottom of the ladder — every open
    /// menu's handler consumed Enter before it fired. That ladder-bottom
    /// position is reproduced structurally rather than by position in a list:
    ///
    /// <list type="bullet">
    /// <item>Modal masking — a live modal scope stops
    /// <see cref="FocusStackCore"/>'s dispatch walk before it ever reaches this
    /// ambient base, so every still-open menu beats this claim exactly as it
    /// beat the ladder-bottom rung before. This is why the legacy handler's own
    /// <c>AnyLiveModal</c> step-out drops here (structurally subsumed, not
    /// merely redundant) — the same reasoning documented for every other
    /// ambient claim.</item>
    /// <item>Stack order — the non-modal scopes that own Enter for their own
    /// purposes sit above <see cref="MapScope"/> on the focus stack and win
    /// first: <see cref="PlacementScope"/>'s <c>architectPlacement.confirmPlacement</c>
    /// and <c>targetingPodLanding.confirm</c>, <see cref="ViewingModeScope"/>'s
    /// <c>viewingMode.confirm</c>, <see cref="TargetingScope"/>'s
    /// <c>podLaunch.confirm</c>/<c>gravshipDest.confirm</c>/<c>newColonyTile.confirm</c>/
    /// <c>worldAbilityTargeting.confirm</c>, <see cref="RoutePlannerScope"/>'s
    /// <c>route.confirm</c>, and <see cref="ShelfLinkingScope"/>'s/
    /// <see cref="TransportPodSelectionScope"/>'s <c>shelfLinking.confirm</c>/
    /// <c>transportPodSelection.confirm</c> — all seven grep-verified to bind
    /// Return + KeypadEnter chords (ShellActionInventory.Part1/Part2/Part7).
    /// Each of these reproduces the ladder-bottom loss to that scope exactly
    /// as before.</item>
    /// </list>
    ///
    /// No <c>ShellGuards.MenuOwnsInput()</c> term is added to the gate below —
    /// deliberately: modal masking and stack order already cover every case
    /// that term would, and adding it would change the StatBreakdown/edge-state
    /// truth table the legacy handler never had (it carried no such term
    /// itself).
    /// </summary>
    public sealed partial class MapScope
    {
        private void RegisterInspectClaims()
        {
            Claim("map.inspect.open", OnOpenInspection, when: InspectOpenLive);
        }

        /// <summary>
        /// R31's guards, transcribed verbatim in order, minus the
        /// <c>AnyLiveModal</c> step-out (dropped structurally — see the class
        /// remarks) and minus the cursor-validity check, which stays in the
        /// handler: the legacy handler ANNOUNCED and consumed on an invalid
        /// cursor rather than falling through, so it cannot live in a
        /// when-gate (a false gate here would leave the key unclaimed instead
        /// of consumed-with-announcement).
        /// </summary>
        private static bool InspectOpenLive()
        {
            // Don't open inspection if HealthTabState is active (safety net).
            return !HealthTabState.IsActive
                // Only process during normal gameplay with a valid map.
                && Find.CurrentMap != null
                // Don't process if any dialog or window that prevents camera motion is open.
                // The legacy handler bailed on WindowsPreventCameraMotion; as a when-gate term
                // the polarity inverts to this matching form (see JumpToSelectedPawnLive in
                // MapScope.QuickInfo.Game.cs for the identical inversion).
                && (Find.WindowStack == null || !Find.WindowStack.WindowsPreventCameraMotion)
                // IMPORTANT: Don't intercept Enter if targeting mode is active (vanilla
                // OR an external map targeter, e.g. Vehicle Framework's TurretTargeter).
                // This allows the targeting system to handle target selection.
                && !ExternalMapTargeting.MapTargetingActive
                // Check if map navigation is initialized.
                && MapNavigationState.IsInitialized;
        }

        /// <summary>The legacy handler's body, verbatim: invalid cursor announces and consumes; otherwise opens the windowless inspection menu at the cursor.</summary>
        private static void OnOpenInspection(KeyEventSnapshot e)
        {
            // Get the cursor position.
            IntVec3 cursorPosition = MapNavigationState.CurrentCursorPosition;

            // Validate cursor position. The claim consuming below IS the retired
            // rung's Event.current.Use() on this branch.
            if (!cursorPosition.IsValid || !cursorPosition.InBounds(Find.CurrentMap))
            {
                TolkHelper.Speak("RimWorldAccess.Input.Cursor.InvalidPosition".Loc());
                return;
            }

            // Open the windowless inspection menu at the current cursor position.
            WindowlessInspectionState.Open(cursorPosition);
        }
    }
}
