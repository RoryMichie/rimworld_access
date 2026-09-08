using System;
using System.Collections.Generic;
using HarmonyLib;
using RimWorld;
using Verse;
using Verse.Sound;

namespace RimWorldAccess.Shell
{
    /// <summary>
    /// The <see cref="PolicyDialogScope"/> for the three vanilla policy dialogs whose
    /// <c>DoContentsRect</c> draws <c>ThingFilterUI.DoThingFilterConfigWindow</c> panels:
    /// apparel and food draw one, reading draws two side by side
    /// (Dialog_ManageReadingPolicies:75-80). Each panel becomes one content region, so Tab
    /// walks policy list, filter, [second filter], buttons.
    ///
    /// Every panel's configuration is READ FROM VANILLA rather than mirrored. The previous
    /// wave's <c>PolicyEditorState</c> rebuilt the apparel global filter by hand and
    /// hand-copied both <c>HiddenSpecialThingFilters</c> lists; those three mirrors are gone.
    /// <c>ApparelGlobalFilter</c> and <c>PolicyGlobalFilter</c> are private static PROPERTIES
    /// (both lazy-initialize, so the getter is the entry point, never the backing field) and
    /// <c>HiddenSpecialThingFilters</c> is a private instance method — all reached through
    /// AccessTools, all cached per concrete dialog type.
    /// </summary>
    public sealed class FilterPolicyDialogScope : PolicyDialogScope
    {
        /// <summary>
        /// One <c>DoThingFilterConfigWindow</c> call, as data. The two range-visibility flags
        /// are vanilla's own <c>forceHide*Config</c> arguments; the resolved
        /// <see cref="hitPointsVisible"/>/<see cref="qualityVisible"/> combine them with the
        /// parent filter's own configurability at rebuild time, exactly as vanilla's
        /// <c>flag &amp;&amp; !forceHideHitPointsConfig</c> does.
        /// </summary>
        private sealed class FilterPanel
        {
            public TreePanel Panel;
            public Func<Policy, ThingFilter> Filter;
            public Func<ThingFilter> ParentFilter;
            public Func<Policy, TreeNode_ThingCategory> Root;
            public bool HideHitPoints;
            public bool HideQuality;
            public bool ShowMentalBreakChance;
            public Func<string> RegionName;

            public bool hitPointsVisible;
            public bool qualityVisible;
            public bool mentalBreakVisible;
        }

        private readonly List<FilterPanel> panels = new List<FilterPanel>();
        private readonly Func<IEnumerable<SpecialThingFilterDef>> hiddenFilters;

        /// <summary>Whether this dialog type is one this scope can present at all — see <see cref="Handles"/>.</summary>
        public static bool Handles(Window dialog)
        {
            return dialog is Dialog_ManageApparelPolicies
                || dialog is Dialog_ManageFoodPolicies
                || dialog is Dialog_ManageReadingPolicies;
        }

        public FilterPolicyDialogScope(Window dialog) : base(dialog)
        {
            hiddenFilters = ResolveHiddenFilters(dialog);
            BuildPanels(dialog);

            // Commands, not rows — pre-existing muscle memory kept as shortcuts to the same
            // activation logic the automatic ClearAll/AllowAll prefix rows use, now acting on
            // whichever filter region the cursor is in.
            Claim("thingFilter.allowAll", e => ActivateFilterShortcut(true));
            Claim("thingFilter.disallowAll", e => ActivateFilterShortcut(false));
        }

        // ------------------------------------------------------------------
        // Vanilla reads.
        // ------------------------------------------------------------------

        /// <summary>Vanilla's own private static global-filter property, invoked as the getter it is (it lazy-initializes).</summary>
        private static Func<ThingFilter> GlobalFilterOf(Type dialogType, string propertyName)
        {
            System.Reflection.MethodInfo getter =
                AccessTools.Property(dialogType, propertyName)?.GetGetMethod(true);
            return getter == null ? (Func<ThingFilter>)(() => null)
                : () => getter.Invoke(null, null) as ThingFilter;
        }

        /// <summary>Vanilla's own private instance HiddenSpecialThingFilters(), or an empty list for a dialog that has none (reading passes null).</summary>
        private static Func<IEnumerable<SpecialThingFilterDef>> ResolveHiddenFilters(Window dialog)
        {
            System.Reflection.MethodInfo method =
                AccessTools.Method(dialog.GetType(), "HiddenSpecialThingFilters");
            return method == null ? (Func<IEnumerable<SpecialThingFilterDef>>)(() => null)
                : () => method.Invoke(dialog, null) as IEnumerable<SpecialThingFilterDef>;
        }

        private void BuildPanels(Window dialog)
        {
            if (dialog is Dialog_ManageReadingPolicies)
            {
                Func<ThingFilter> bookTypes = GlobalFilterOf(dialog.GetType(), "PolicyGlobalFilter");
                panels.Add(new FilterPanel
                {
                    Panel = CreatePanel(),
                    Filter = p => ((ReadingPolicy)p).defFilter,
                    ParentFilter = bookTypes,
                    Root = p => bookTypes()?.DisplayRootCategory,
                    HideHitPoints = true,
                    // Vanilla's own category label for this panel's root, not a coined name.
                    RegionName = () => bookTypes()?.DisplayRootCategory?.catDef?.LabelCap ?? "",
                });
                panels.Add(new FilterPanel
                {
                    Panel = CreatePanel(),
                    Filter = p => ((ReadingPolicy)p).effectFilter,
                    // Vanilla passes a null parent here, so ThingFilterUI falls back to the
                    // filter's own RootNode (ThingFilterUI.cs:49) — derived, not assumed, so it
                    // stays correct if ReadingPolicy's construction of effectFilter changes.
                    ParentFilter = () => null,
                    Root = p => ((ReadingPolicy)p).effectFilter?.RootNode,
                    HideHitPoints = true,
                    HideQuality = true,
                    ShowMentalBreakChance = true,
                    RegionName = () => ThingCategoryDefOf.BookEffects.LabelCap,
                });
                return;
            }

            bool food = dialog is Dialog_ManageFoodPolicies;
            Func<ThingFilter> global = food
                ? () => Dialog_ManageFoodPolicies.FoodGlobalFilter
                : GlobalFilterOf(dialog.GetType(), "ApparelGlobalFilter");
            // ApparelPolicy and FoodPolicy each declare their own `filter` field; Policy itself
            // has none, so the two cases cannot share one accessor.
            Func<Policy, ThingFilter> filterOf = food
                ? (Func<Policy, ThingFilter>)(p => ((FoodPolicy)p).filter)
                : p => ((ApparelPolicy)p).filter;
            panels.Add(new FilterPanel
            {
                Panel = CreatePanel(),
                Filter = filterOf,
                ParentFilter = global,
                Root = p => global()?.DisplayRootCategory,
                // Dialog_ManageFoodPolicies:74 passes forceHideHitPointsConfig: true; the
                // apparel dialog (:69) passes neither flag.
                HideHitPoints = food,
                RegionName = () => "RimWorldAccess.Shell.FilterTree.PolicyRegionName".Translate(),
            });
        }

        // ------------------------------------------------------------------
        // Contents contract.
        // ------------------------------------------------------------------

        protected override int ContentsRegionCount
        {
            get { return panels.Count; }
        }

        protected override string ContentsRegionName(int region)
        {
            FilterPanel panel = PanelAt(region);
            return panel != null ? panel.RegionName() : "";
        }

        protected override string TreeRegionLabel
        {
            get { return ContentsRegionName(TreeRegionIndex); }
        }

        private FilterPanel PanelAt(int region)
        {
            int i = region - FirstContentsRegion;
            return i >= 0 && i < panels.Count ? panels[i] : null;
        }

        protected override TreePanel PanelFor(int region)
        {
            FilterPanel panel = PanelAt(region);
            return panel != null ? panel.Panel : null;
        }

        protected override ThingFilterSessionCore.FilterContext BuildFilterContext(int region)
        {
            FilterPanel panel = PanelAt(region);
            Policy selected = Selected;
            if (panel == null || selected == null)
            {
                return default(ThingFilterSessionCore.FilterContext);
            }
            return new ThingFilterSessionCore.FilterContext
            {
                CurrentFilter = panel.Filter(selected),
                ParentFilter = panel.ParentFilter(),
                ForceHiddenFilters = HiddenFiltersList(),
                DisplayRoot = panel.Root(selected),
            };
        }

        protected override ThingFilter FilterForRegion(int region)
        {
            FilterPanel panel = PanelAt(region);
            Policy selected = Selected;
            return panel != null && selected != null ? panel.Filter(selected) : null;
        }

        private List<SpecialThingFilterDef> HiddenFiltersList()
        {
            IEnumerable<SpecialThingFilterDef> hidden = hiddenFilters();
            return hidden != null ? new List<SpecialThingFilterDef>(hidden) : null;
        }

        protected override bool ShowHitPointsRangeFor(int region)
        {
            FilterPanel panel = PanelAt(region);
            return panel != null && panel.hitPointsVisible;
        }

        protected override bool ShowQualityRangeFor(int region)
        {
            FilterPanel panel = PanelAt(region);
            return panel != null && panel.qualityVisible;
        }

        protected override bool ShowMentalBreakChanceRangeFor(int region)
        {
            FilterPanel panel = PanelAt(region);
            return panel != null && panel.mentalBreakVisible;
        }

        /// <summary>
        /// Rebuilds every panel's tree against the newly selected policy. Range-row visibility
        /// is resolved here rather than per describe call because reading
        /// <c>DisplayRootCategory</c> is what recalculates the parent filter's
        /// <c>allowedHitPointsConfigurable</c>/<c>allowedQualitiesConfigurable</c> flags
        /// (ThingFilter.RecalculateSpecialFilterConfigurability), exactly as vanilla's own
        /// DoThingFilterConfigWindow does before reading them.
        /// </summary>
        protected override void RebuildContents()
        {
            Policy selected = Selected;
            for (int i = 0; i < panels.Count; i++)
            {
                FilterPanel panel = panels[i];
                int region = FirstContentsRegion + i;

                bool hitPointsConfigurable = true;
                bool qualityConfigurable = true;
                ThingFilter parent = panel.ParentFilter();
                if (parent != null)
                {
                    var _ = parent.DisplayRootCategory;
                    hitPointsConfigurable = parent.allowedHitPointsConfigurable;
                    qualityConfigurable = parent.allowedQualitiesConfigurable;
                }
                panel.hitPointsVisible = hitPointsConfigurable && !panel.HideHitPoints;
                panel.qualityVisible = qualityConfigurable && !panel.HideQuality;
                panel.mentalBreakVisible = ModsConfig.AnomalyActive && panel.ShowMentalBreakChance;

                SetTreeRoot(panel.Panel, selected == null
                    ? ThingFilterSessionCore.EmptyTreeRoot()
                    : ThingFilterSessionCore.BuildCategoryTreeRoot(ContextForBuild(region)));
            }
        }

        // ------------------------------------------------------------------
        // The two muscle-memory shortcut chords.
        // ------------------------------------------------------------------

        private void ActivateFilterShortcut(bool allow)
        {
            int region = Model.RegionIndex;
            if (PanelAt(region) == null || Selected == null)
            {
                SoundDefOf.ClickReject.PlayOneShotOnCamera();
                return;
            }
            if (allow)
            {
                ActivateAllowAllRow(region);
            }
            else
            {
                ActivateClearAllRow(region);
            }
        }
    }
}
