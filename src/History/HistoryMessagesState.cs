using RimWorldAccess.Shell;

namespace RimWorldAccess
{
    /// <summary>
    /// Thin static facade over <see cref="Shell.HistoryMessagesScope"/>.
    /// All navigation/announcement/typeahead/filter/detail-
    /// view state that used to live here as static fields (<c>items</c>,
    /// <c>selectedIndex</c>, a <see cref="TypeaheadSearchHelper"/>, a
    /// <see cref="TwoLevelMenuHelper"/>, plus the "Shell Focus-Scope Router" region)
    /// now lives on the scope instance itself — this class only survives because
    /// Harmony-patched, scope-unaware code still needs static entry points:
    /// <see cref="HistoryState"/>'s tab-switch machinery calls
    /// <see cref="Open"/>/<see cref="Close"/>, and <c>HistoryPatch</c>'s ad hoc
    /// Cancel/Accept blockers read <see cref="IsActive"/>/<see cref="HasActiveSearch"/>/
    /// <see cref="IsInDetailView"/> directly (all three untouched signatures; see
    /// HistorySubTabScopes.Game.cs's class remarks for the mirror that keeps the
    /// scope in lockstep with <see cref="IsActive"/>, including its foreign-window
    /// stand-down for the "Open"/"Jump to Location" child dialogs).
    /// </summary>
    public static class HistoryMessagesState
    {
        private static bool isActive;

        /// <summary>
        /// The singleton scope instance. Reused across every open/close cycle AND
        /// across the mirror's transient foreign-window stand-down — see
        /// <see cref="HistoryMessagesScope"/>'s own remarks for why real session
        /// resets happen only in <see cref="Open"/> (via
        /// <see cref="HistoryMessagesScope.ResetForOpen"/>), never in <c>OnPush</c>.
        /// </summary>
        internal static readonly HistoryMessagesScope Scope = new HistoryMessagesScope();

        /// <summary>
        /// Gets whether the Messages tab navigation is active. Read directly by
        /// <c>HistoryPatch</c>'s ad hoc Cancel/Accept blockers and by
        /// <see cref="MapNavigationPatch"/>'s accessibility-menu OR-list.
        /// </summary>
        public static bool IsActive => isActive;

        /// <summary>
        /// Gets whether there is an active typeahead search. Read directly by
        /// <see cref="HistoryState.HasActiveTypeahead"/>, which
        /// <c>HistoryPatch</c>'s ad hoc Cancel blocker consults transitively.
        /// </summary>
        public static bool HasActiveSearch => isActive && Scope.HasActiveSearch;

        /// <summary>
        /// Gets whether we are in detail view. Now means "the detail region
        /// (message content lines, or its Buttons region) has focus" — the
        /// ScreenScope-native replacement for the retired
        /// <see cref="TwoLevelMenuHelper"/>'s IsInDetailView. Read directly by
        /// <c>HistoryPatch</c>'s ad hoc Cancel blocker, which still needs this
        /// exact signature to decide whether Escape should exit detail view
        /// before it lets vanilla close the window.
        /// </summary>
        public static bool IsInDetailView => isActive && Scope.IsDetailFocused;

        /// <summary>
        /// Opens the Messages tab navigation. Called by
        /// <see cref="HistoryState.OpenCurrentTabState"/> on tab-switch into
        /// Messages. Delegates the actual reset (filters, item collection,
        /// cursor, opening announcement) to the scope — see
        /// <see cref="HistoryMessagesScope.ResetForOpen"/> for why this must NOT
        /// happen in the scope's <c>OnPush</c> instead.
        /// </summary>
        public static void Open()
        {
            isActive = true;
            Scope.ResetForOpen();
        }

        /// <summary>
        /// Closes the Messages tab navigation. Called by
        /// <see cref="HistoryState.CloseCurrentTabState"/>/<see cref="HistoryState.Close"/>.
        /// </summary>
        public static void Close()
        {
            isActive = false;
            Scope.ResetForClose();
        }
    }
}
