using RimWorldAccess.Shell;

namespace RimWorldAccess
{
    /// <summary>
    /// Thin static facade over <see cref="Shell.HistoryStatsScope"/>.
    /// All navigation/announcement/typeahead state that
    /// used to live here as static fields (a <see cref="FlatListCursor"/> plus the
    /// "Shell Focus-Scope Router" region) now lives on the scope instance itself —
    /// this class only survives because Harmony-patched, scope-unaware code still
    /// needs static entry points: <see cref="HistoryState"/>'s tab-switch machinery
    /// calls <see cref="Open"/>/<see cref="Close"/>, and <c>HistoryPatch</c>'s ad hoc
    /// Cancel/Accept blockers read <see cref="IsActive"/>/<see cref="HasActiveSearch"/>
    /// directly (both untouched by this migration; see HistorySubTabScopes.Game.cs's
    /// class remarks for the mirror that keeps the scope in lockstep with
    /// <see cref="IsActive"/>).
    /// </summary>
    public static class HistoryStatisticsState
    {
        private static bool isActive;

        /// <summary>
        /// The singleton scope instance. Reused across every open/close cycle —
        /// see <see cref="HistoryStatsScope"/>'s own remarks for why session
        /// state resets in <c>OnPush</c> rather than here.
        /// </summary>
        internal static readonly HistoryStatsScope Scope = new HistoryStatsScope();

        /// <summary>
        /// Gets whether the Statistics tab navigation is active. Read directly by
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
        /// Opens the Statistics tab navigation. Called by
        /// <see cref="HistoryState.OpenCurrentTabState"/> on tab-switch into
        /// Statistics. The scope itself collects a fresh snapshot and speaks the
        /// opening announcement from its own <c>OnPush</c>/<c>OnFocus</c> —
        /// this method only flips the facade flag the mirror reconciles against.
        /// </summary>
        public static void Open()
        {
            isActive = true;
        }

        /// <summary>
        /// Closes the Statistics tab navigation. Called by
        /// <see cref="HistoryState.CloseCurrentTabState"/>/<see cref="HistoryState.Close"/>.
        /// </summary>
        public static void Close()
        {
            isActive = false;
        }
    }
}
