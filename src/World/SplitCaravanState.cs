namespace RimWorldAccess
{
    /// <summary>
    /// Thin static bridge between the split-caravan Harmony patches
    /// (<see cref="SplitCaravanPatch"/>, plus the shared
    /// <see cref="CaravanFormationPatch"/> cancel/accept blockers) and the live
    /// <see cref="Shell.SplitCaravanScope"/> instance that now owns all
    /// tab/cursor/search/announce state (screen-model refactor — the
    /// pre-migration state this file used to be lives in git history at this
    /// commit's parent). Split has no destination sub-mode and no
    /// auto-provision, so unlike <see cref="CaravanFormationState"/> there is no
    /// state left that outlives the scope's own lifetime — this bridge is as
    /// thin as the pilot's.
    /// </summary>
    public static class SplitCaravanState
    {
        internal static Shell.SplitCaravanScope ActiveScope;

        private static bool splitAttempted;

        /// <summary>A split-caravan scope is driving the dialog.</summary>
        public static bool IsActive => ActiveScope != null && ActiveScope.IsLive;

        /// <summary>Gets whether a split was attempted (used by PostClose to decide announcement).</summary>
        public static bool SplitAttempted => splitAttempted;

        /// <summary>Gets whether typeahead search is currently active. Used by Window.OnCancelKeyPressed patch to block dialog close.</summary>
        public static bool HasActiveTypeahead => ActiveScope != null && ActiveScope.HasActiveTypeahead;

        internal static void NoteSplitAttempted()
        {
            splitAttempted = true;
        }

        /// <summary>Called when TrySplitCaravan() itself reports validation failure (the dialog stays open) so a later plain Escape still announces "cancelled".</summary>
        internal static void ResetSplitAttempted()
        {
            splitAttempted = false;
        }

        internal static void NotifyScopeAttached(Shell.SplitCaravanScope scope)
        {
            ActiveScope = scope;
        }

        internal static void NotifyScopeDetached(Shell.SplitCaravanScope scope)
        {
            if (ActiveScope == scope)
            {
                ActiveScope = null;
            }
        }

        /// <summary>Called by the PostClose patch once the dialog is really gone.</summary>
        public static void Close()
        {
            splitAttempted = false;
        }
    }
}
