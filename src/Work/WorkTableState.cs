using Verse;

namespace RimWorldAccess
{
    /// <summary>
    /// Thin bridge for the Work menu's TABLE view (table-model T2 migration,
    /// the table-model doctrine): <see cref="IsActive"/> is the flag
    /// RimWorldAccess.Shell.WorkTableScopeMirror watches to push/pop
    /// RimWorldAccess.Shell.WorkTableScope (the table's entire
    /// keyboard/row/column/priority/paint/typeahead implementation now lives
    /// on that scope, replacing the retired <c>TabularMenuHelper</c>-backed
    /// design). This class only owns what code OUTSIDE the scope reads:
    /// <see cref="WorkWindowInterceptPatch"/>'s window interception, the
    /// map-ambient guards (ShellGuards, MapNavigationPatch, MapScope's F1
    /// gate), <see cref="WorkMenuOpener"/>'s view-swap plumbing, and the
    /// pre-open empty-roster check (kept here, not on the scope, so the
    /// scope never gets pushed for an empty table — matching the
    /// Wildlife/Animals precedent's refuse-to-open behavior exactly).
    ///
    /// The Focused view (<see cref="WorkMenuState"/> / RimWorldAccess.Shell.WorkMenuScope)
    /// is untouched by this migration — the two views remain mutually
    /// exclusive by construction (<see cref="WorkMenuOpener"/> always closes
    /// one before opening the other), selected by the DefaultWorkMenuView
    /// setting and switched live with Ctrl+Tab (Option+Tab on macOS).
    /// </summary>
    public static class WorkTableState
    {
        public static bool IsActive { get; private set; }

        /// <summary>
        /// The pawn to land the row cursor on, set by <see cref="Open"/> and
        /// consumed once by the scope's first OnFocus after each open
        /// (re-set on every <see cref="Open"/> call, so a stale value never
        /// survives to a later session).
        /// </summary>
        public static Pawn PendingInitialPawn { get; private set; }

        /// <summary>Basic vs. manual priority mode — pure passthrough to the vanilla setting, unrelated to any table state.</summary>
        public static bool IsManualMode => Find.PlaySettings.useWorkPriorities;

        public static void Open(Pawn initialPawn = null)
        {
            if (IsActive) return;
            if (!GuardHelper.RequireMap()) return;

            if (WorkTableHelper.GetEligibleColonists().Count == 0)
            {
                TolkHelper.Speak("RimWorldAccess.Input.WorkMenu.NoColonistsAvailable".Loc());
                return;
            }

            WorkTableHelper.RefreshWorkTypes();
            PendingInitialPawn = initialPawn;
            IsActive = true;
        }

        /// <summary>
        /// Closes the table view. Callers (the scope's own Escape/Enter
        /// confirm path, or WorkMenuOpener.SwapToFocused when swapping to
        /// the other view) have already applied any real-time work-setting
        /// changes and refreshed work givers before calling this — it only
        /// flips the flag the mirror watches.
        /// </summary>
        public static void Close()
        {
            IsActive = false;
        }
    }
}
