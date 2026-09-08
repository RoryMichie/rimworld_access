namespace RimWorldAccess.Shell
{
    /// <summary>
    /// Non-modal map-cursor toggle mode for grouping transport pods (a legacy handler),
    /// sibling to <see cref="ShelfLinkingScope"/>. Space toggles the pod under the map cursor, Enter
    /// confirms, Escape cancels; arrows stay unclaimed so they keep driving the map cursor
    /// (MapNavigationPatch excludes both states from arrow suppression).
    ///
    /// Not a targeter and not a menu: it touches no Find.Targeter, is absent from
    /// <see cref="ShellGuards.MenuOwnsInput"/> and the camera-pan suppression list, and has no
    /// typeahead. There is no focusable row list — every announcement reports the live cursor
    /// position, so the scope carries no element descriptions.
    /// </summary>
    public sealed class TransportPodSelectionScope : FocusScope
    {
        public TransportPodSelectionScope()
        {
            Claim("transportPodSelection.toggleAtCursor", delegate { TransportPodSelectionState.TogglePodAtCursor(); });
            Claim("transportPodSelection.confirm", delegate { TransportPodSelectionState.ConfirmSelection(); });
            Claim("transportPodSelection.cancel", delegate { TransportPodSelectionState.Close(); });
        }

        public override string Name
        {
            get { return "transport-pod-selection"; }
        }

        public override bool IsModal
        {
            get { return false; }
        }

        public override bool IsLive
        {
            get { return true; }
        }
    }

    /// <summary>
    /// Keeps <see cref="TransportPodSelectionScope"/> in lockstep with
    /// <see cref="TransportPodSelectionState.IsActive"/>. No ordering dependency on any other
    /// mirror: ConfirmSelection clears IsActive before invoking the real
    /// Command_LoadToTransporter gizmo, so this scope is popped before
    /// Dialog_LoadTransporters wants the keyboard.
    /// </summary>
    internal static class TransportPodSelectionScopeMirror
    {
        private static readonly TransportPodSelectionScope scope = new TransportPodSelectionScope();

        public static void Reconcile()
        {
            if (TransportPodSelectionState.IsActive)
            {
                FocusStack.Push(scope);
            }
            else
            {
                FocusStack.Pop(scope);
            }
        }
    }

    /// <summary>
    /// Non-modal scope for manual storage-group linking (a legacy handler); shares its
    /// cursor-toggle shape with <see cref="TransportPodSelectionScope"/>.
    ///
    /// ConfirmSelection stays active while the already-linked-items warning raises a real
    /// <see cref="Dialog_MessageBox"/>. Stack order alone does not protect that dialog —
    /// <c>FocusStackCore.Push</c> re-floats an already-stacked scope, so the per-frame mirror
    /// would hoist this scope back above MessageBoxScope and eat the Enter/Escape it claims.
    /// The mirror stands down behind <c>ShellGuards.ForeignInputOwningWindowAbove()</c> instead.
    /// </summary>
    public sealed class ShelfLinkingScope : FocusScope
    {
        public ShelfLinkingScope()
        {
            Claim("shelfLinking.toggleAtCursor", delegate { ShelfLinkingState.ToggleStorageAtCursor(); });
            Claim("shelfLinking.confirm", delegate { ShelfLinkingState.ConfirmSelection(); });
            Claim("shelfLinking.cancel", delegate { ShelfLinkingState.Close(); });
        }

        public override string Name
        {
            get { return "shelf-linking"; }
        }

        public override bool IsModal
        {
            get { return false; }
        }

        public override bool IsLive
        {
            get { return true; }
        }
    }

    /// <summary>
    /// Keeps <see cref="ShelfLinkingScope"/> in lockstep with
    /// <see cref="ShelfLinkingState.IsActive"/>. The stand-down term is load-bearing: an
    /// unconditional per-frame Push would re-float this scope above the confirm dialog.
    /// </summary>
    internal static class ShelfLinkingScopeMirror
    {
        private static readonly ShelfLinkingScope scope = new ShelfLinkingScope();

        public static void Reconcile()
        {
            if (ShelfLinkingState.IsActive
                && !ShellGuards.ForeignInputOwningWindowAbove())
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
