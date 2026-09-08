namespace RimWorldAccess.Shell
{
    /// <summary>
    /// Keyboard focus scope for viewing mode (legacy handler): the post-placement
    /// review step where segments are added or removed, cells nudged at the cursor,
    /// and the placement confirmed or cancelled. Windowless — the surface is
    /// <see cref="ViewingModeState"/>, so the scope rides the focus stack through
    /// <see cref="ViewingModeScopeMirror"/>.
    ///
    /// Non-modal: scanner keys and every other unclaimed key fall through to the map.
    /// The gizmo-navigation term in the mirror's gate is load-bearing because
    /// <see cref="GizmoScope"/> is itself non-modal, so stack masking alone would not
    /// mask it; the float-menu case is covered by the dispatcher's blanket stand-down
    /// instead. Precedence against <see cref="GoToScope"/> (+/−) and
    /// <see cref="PlacementScope"/> (shape placement launched from a viewing-mode
    /// gizmo) is resolved by reconcile order in ShellDispatcher.Game.cs rather than by
    /// gate terms: both re-float above this scope and win the keys they claim, while
    /// every other key still reaches here.
    ///
    /// Tab is claimed as an explicit no-op: unclaimed it falls through to
    /// <see cref="MapScope"/>'s architect toggle and opens the architect menu over an
    /// active review. Space and Shift+Space are two chords over one router — shift is
    /// data (zone designators toggle either way, others add on Space and remove on
    /// Shift+Space).
    /// </summary>
    public sealed class ViewingModeScope : FocusScope
    {
        public ViewingModeScope()
        {
            Claim("viewingMode.blockTab", delegate { });
            Claim("viewingMode.placeAtCursor", delegate { ViewingModeState.HandleSpaceAtCursor(false); });
            Claim("viewingMode.removeAtCursor", delegate { ViewingModeState.HandleSpaceAtCursor(true); });
            Claim("viewingMode.addAnotherShape", delegate { ViewingModeState.AddAnotherShape(); });
            Claim("viewingMode.removeLastSegment", delegate { ViewingModeState.RemoveLastSegment(); });
            Claim("viewingMode.confirm", delegate { ViewingModeState.Confirm(); });
            Claim("viewingMode.exit", delegate { ViewingModeState.ShowExitConfirmation(); });
        }

        public override string Name
        {
            get { return "viewing-mode"; }
        }

        public override bool IsModal
        {
            get { return false; }
        }

        /// <summary>
        /// Non-modal (arrows and ambient map keys fall through) but still an input
        /// owner, so unclaimed keys are swallowed rather than reaching the game.
        /// </summary>
        public override bool OwnsGameInput
        {
            get { return true; }
        }

        public override bool IsLive
        {
            get { return true; }
        }
    }

    /// <summary>
    /// Keeps <see cref="ViewingModeScope"/> in lockstep with
    /// <see cref="ViewingModeState.IsActive"/> and the overlay gate, reconciled every
    /// OnGUI pass. Reconcile placement matters — see the scope's doc comment.
    /// </summary>
    internal static class ViewingModeScopeMirror
    {
        private static readonly ViewingModeScope scope = new ViewingModeScope();

        public static void Reconcile()
        {
            // ShowExitConfirmation raises a Dialog_MessageBox while isActive stays true
            // (Reset only runs in the Leave callback); without the stand-down term the
            // per-frame Push re-floats this scope above the dialog's own and buries it.
            if (ViewingModeState.IsActive && !OverlayActive()
                && !ShellGuards.ForeignInputOwningWindowAbove())
            {
                FocusStack.Push(scope);
            }
            else
            {
                FocusStack.Pop(scope);
            }
        }

        /// <summary>WindowlessFloatMenuState is omitted — the blanket stand-down covers it.</summary>
        private static bool OverlayActive()
        {
            return WindowlessInventoryState.IsActive
                || GizmoNavigationState.IsActive
                || WindowlessInspectionState.IsActive;
        }
    }
}
