using System;
using System.Collections.Generic;
using Verse;
using RimWorld;

namespace RimWorldAccess
{
    /// <summary>
    /// Facade for the reusable thing-filter menu: bill ingredient filters
    /// (<see cref="BillConfigState"/>), pen animal/auto-cut filters
    /// (PenAnimalsAdapter/PenAutoCutAdapter), and the mortar shells tab (ShellsAdapter).
    ///
    /// All cursor, announcement, and keyboard-claim logic
    /// that used to live here (TreeNavigationHelper-driven BuildTree/FormatItemAnnouncement/
    /// NavigatePrevious/ExpandOrToggleOn/ToggleCurrent/HandleCancel/typeahead routing, and the
    /// ClearAll/AllowAll/HitPointsRange/QualityRange nodes that used to live IN the
    /// InspectionTreeItem tree as synthetic root children) has moved to
    /// <see cref="RimWorldAccess.Shell.ThingFilterMenuScope"/>, a
    /// <see cref="RimWorldAccess.Shell.FilterTreeScopeBase"/> subclass riding
    /// <see cref="RimWorldAccess.Shell.TreeModel{T}"/> directly (the same base
    /// <see cref="StorageSettingsMenuState"/> rides). This class is a
    /// thin data/lifecycle facade the scope reads from; keep it that way — navigation and
    /// announcement logic belong on the scope. The scope builds its own category/thing-def
    /// tree from <see cref="BuildFilterContext"/> through
    /// <see cref="RimWorldAccess.Shell.FilterTreeScopeBase.ContextForBuild"/>;
    /// ClearAll/AllowAll/range rows are the scope's own prefix rows, not tree nodes (see
    /// FilterTreeScopeBase's header for why).
    ///
    /// EXTERNAL CONTRACT (do not rename without updating the listed callers, all outside this
    /// packet): <see cref="Open"/> — four call sites (BillConfigState.cs:2100,
    /// PenAnimalsAdapter.Game.cs:24, ShellsAdapter.Game.cs:28, PenAutoCutAdapter.Game.cs:107),
    /// signature UNCHANGED. <see cref="IsActive"/> — read by ShellGuards, MapNavigationPatch.
    /// </summary>
    public static class ThingFilterMenuState
    {
        private static bool isActive = false;
        private static ThingFilter currentFilter = null;
        private static ThingFilter parentFilter = null;
        private static TreeNode_ThingCategory rootNode = null;
        private static string menuTitle = "";
        private static bool forceHideHitPoints = false;
        private static bool forceHideQuality = false;
        private static List<SpecialThingFilterDef> forceHiddenFilters = null;

        public static bool IsActive
        {
            get { return isActive; }
        }

        /// <summary>The vanilla dialog title threaded through from the call site ("Pen Animals", "TabShells", the ingredient-filter title, the auto-cut title) — used as the scope's content region name.</summary>
        public static string MenuTitle
        {
            get { return menuTitle; }
        }

        public static bool HitPointsConfigurable
        {
            get { return (parentFilter?.allowedHitPointsConfigurable ?? true) && !forceHideHitPoints; }
        }

        public static bool QualityConfigurable
        {
            get { return (parentFilter?.allowedQualitiesConfigurable ?? true) && !forceHideQuality; }
        }

        /// <summary>
        /// Wired once by ThingFilterMenuScopeMirror to the live scope's own tree-rebuild method,
        /// called at the end of every <see cref="Open"/> (a genuine fresh session). Deliberately
        /// NOT triggered by the scope's own OnPush: ThingFilterMenuScopeMirror stands down (pops)
        /// while an info card is open over this menu and re-pushes on close, so OnPush ALSO fires
        /// on that round trip — rebuilding there would silently discard the player's cursor
        /// position (and the tree's expand/collapse state) on every info-card look-up. Firing
        /// this from Open() instead means the tree only rebuilds when the underlying filter data
        /// actually changes.
        /// </summary>
        public static Action RebuildCallback;

        /// <param name="filter">The filter being edited (what's currently selected)</param>
        /// <param name="fixedFilter">Optional parent filter that defines what's possible (tree structure)</param>
        /// <param name="title">Menu title for announcements</param>
        /// <param name="forceHiddenFilters">
        /// Call-site-supplied hidden special filters, mirroring vanilla's
        /// ThingFilterUI.DoThingFilterConfigWindow forceHiddenFilters argument
        /// (e.g. a recipe's forceHiddenSpecialFilters plus the Ideology diet filters
        /// Dialog_BillConfig hides).
        /// </param>
        public static void Open(ThingFilter filter, ThingFilter fixedFilter = null, string title = "Thing Filter",
            bool forceHideHitPointsConfig = false, bool forceHideQualityConfig = false,
            IEnumerable<SpecialThingFilterDef> forceHiddenFilters = null)
        {
            if (filter == null)
            {
                Log.Error("Cannot open thing filter menu: filter is null");
                return;
            }

            currentFilter = filter;
            parentFilter = fixedFilter;
            menuTitle = title;
            forceHideHitPoints = forceHideHitPointsConfig;
            forceHideQuality = forceHideQualityConfig;
            ThingFilterMenuState.forceHiddenFilters = forceHiddenFilters != null
                ? new List<SpecialThingFilterDef>(forceHiddenFilters)
                : null;

            // Match vanilla ThingFilterUI: touching DisplayRootCategory triggers
            // RecalculateSpecialFilterConfigurability, which is what populates
            // allowedHitPointsConfigurable/allowedQualitiesConfigurable on the parent filter.
            // Store the resolved root now so HitPointsConfigurable/QualityConfigurable (read by
            // the scope immediately on push) see the post-recalculation flags.
            rootNode = parentFilter?.DisplayRootCategory ?? currentFilter.DisplayRootCategory;
            isActive = true;

            RebuildCallback?.Invoke();
        }

        public static void Close()
        {
            isActive = false;
            currentFilter = null;
            parentFilter = null;
            rootNode = null;
            menuTitle = "";
            forceHideHitPoints = false;
            forceHideQuality = false;
            forceHiddenFilters = null;
        }

        /// <summary>
        /// Builds the shared per-session context threaded through
        /// <see cref="ThingFilterSessionCore"/>'s tree-build/allowance/toggle/announce logic.
        /// </summary>
        public static ThingFilterSessionCore.FilterContext BuildFilterContext()
        {
            return new ThingFilterSessionCore.FilterContext
            {
                CurrentFilter = currentFilter,
                ParentFilter = parentFilter,
                ForceHiddenFilters = forceHiddenFilters,
                DisplayRoot = rootNode
            };
        }
    }
}
