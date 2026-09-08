using RimWorld;

namespace RimWorldAccess
{
    /// <summary>
    /// Which column in the mod list is currently focused.
    /// </summary>
    public enum ModListColumn
    {
        Inactive,
        Active
    }

    /// <summary>
    /// Shared lifecycle state (isActive/currentPage) for the Page_ModsConfig dialog's
    /// keyboard-accessible surface. Split out of a single class into:
    ///   - ModListNavigation — column state and the mod-list/downloading-item
    ///     data-access helpers
    ///   - ModListDetailView — the selected mod's Details content lines and
    ///     requirement-row classification
    ///   - ModListActions — per-mod business logic, reordering, save/load,
    ///     bottom-bar float menus
    ///   - ModListAnnouncements — the row-description builders and the
    ///     column-switch/column-empty announcements
    ///   - ModListVanillaBridge — named accessors for Page_ModsConfig's private
    ///     members, routed through VanillaAccess's shared reflection cache
    ///
    /// Trimmed for the region-grammar rework (ModListScreenScope): this class used to be the sole
    /// facade every ModListScope claim called through (list/detail-view entry points, mod operations,
    /// bottom-bar affordances). ModListScreenScope, a ScreenScope subclass, calls
    /// ModListNavigation/ModListActions/ModListDetailView directly instead — the pass-through methods
    /// added no value once the scope itself owns navigation. Only the two fields every one of those
    /// classes reads (IsActive/CurrentPage) and the lifecycle Open/Close remain here.
    /// </summary>
    public static class ModListState
    {
        private static bool isActive = false;
        private static Page_ModsConfig currentPage = null;

        public static bool IsActive => isActive;

        /// <summary>The live Page_ModsConfig instance, shared across the ModList* split.</summary>
        internal static Page_ModsConfig CurrentPage => currentPage;

        /// <summary>
        /// Opens the mod list state when Page_ModsConfig is opened.
        /// </summary>
        public static void Open(Page_ModsConfig page)
        {
            currentPage = page;
            isActive = true;
            ModListNavigation.ResetForOpen();
        }

        /// <summary>
        /// Closes the mod list state when Page_ModsConfig is closed.
        /// </summary>
        public static void Close()
        {
            isActive = false;
            currentPage = null;
            ModListNavigation.ResetForClose();
            ModListDetailView.Reset();
        }
    }
}
