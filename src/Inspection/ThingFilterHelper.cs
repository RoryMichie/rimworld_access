using System;
using System.Collections.Generic;
using System.Linq;
using Verse;

namespace RimWorldAccess
{
    /// <summary>
    /// Shared ThingFilter navigation utilities for the filter screens: tri-state allowance,
    /// visibility checks, and summary formatting.
    /// </summary>
    public static class ThingFilterHelper
    {
        /// <summary>Tri-state allowance, matching vanilla's MultiCheckboxState.</summary>
        public enum CategoryAllowanceState
        {
            NoneAllowed,
            SomeAllowed,
            AllAllowed
        }

        /// <summary>A category's allowance state in detail, for smart announcements.</summary>
        public struct CategorySummary
        {
            public CategoryAllowanceState State;
            public int TotalVisible;
            public int AllowedCount;
            public int DisallowedCount;
            public List<string> AllowedNames;
            public List<string> DisallowedNames;
        }

        /// <summary>
        /// The tri-state allowance for a category, matching vanilla's AllowanceStateOf(). Counts
        /// visible ThingDefs only, unless <paramref name="isVisibleSpecial"/> is supplied AND
        /// <paramref name="filter"/> is <see cref="ThingFilter.OnlySpecialFilters"/> — for a
        /// category made entirely of SpecialThingFilterDefs the state is driven by its visible
        /// descendant special filters instead, exactly as
        /// Listing_TreeThingFilter.AllowanceStateOf's own OnlySpecialFilters branch does.
        /// </summary>
        public static CategoryAllowanceState GetAllowanceState(
            ThingCategoryDef catDef, ThingFilter filter, Func<ThingDef, bool> isVisible,
            Func<SpecialThingFilterDef, bool> isVisibleSpecial = null)
        {
            if (filter.OnlySpecialFilters && isVisibleSpecial != null)
            {
                int visibleSpecialCount = 0;
                int allowedSpecialCount = 0;
                foreach (SpecialThingFilterDef sf in catDef.DescendantSpecialThingFilterDefs)
                {
                    if (isVisibleSpecial(sf))
                    {
                        visibleSpecialCount++;
                        if (filter.Allows(sf))
                            allowedSpecialCount++;
                    }
                }

                if (allowedSpecialCount == 0)
                    return CategoryAllowanceState.NoneAllowed;
                if (allowedSpecialCount == visibleSpecialCount)
                    return CategoryAllowanceState.AllAllowed;
                return CategoryAllowanceState.SomeAllowed;
            }

            int visibleCount = 0;
            int allowedCount = 0;

            foreach (ThingDef td in catDef.DescendantThingDefs)
            {
                if (isVisible(td))
                {
                    visibleCount++;
                    if (filter.Allows(td))
                        allowedCount++;
                }
            }

            // Vanilla also counts visible descendant special filters toward the all-allowed
            // decision: a category is fully On only when every visible special filter is allowed
            // too. Off stays decided by ThingDefs alone, as vanilla's own num2 == 0 check does.
            int visibleSpecial = 0;
            int allowedSpecial = 0;
            if (isVisibleSpecial != null)
            {
                foreach (SpecialThingFilterDef sf in catDef.DescendantSpecialThingFilterDefs)
                {
                    if (isVisibleSpecial(sf))
                    {
                        visibleSpecial++;
                        if (filter.Allows(sf))
                            allowedSpecial++;
                    }
                }
            }

            if (allowedCount == 0)
                return CategoryAllowanceState.NoneAllowed;
            if (allowedCount == visibleCount && allowedSpecial == visibleSpecial)
                return CategoryAllowanceState.AllAllowed;
            return CategoryAllowanceState.SomeAllowed;
        }

        /// <summary>
        /// A category summary with exception names, collecting maxNames+1 per side so the caller can
        /// tell listing from counting. With <paramref name="isVisibleSpecial"/> supplied and an
        /// <see cref="ThingFilter.OnlySpecialFilters"/> filter, the summary is built from the
        /// category's visible descendant SpecialThingFilterDefs; see <see cref="GetAllowanceState"/>.
        /// </summary>
        public static CategorySummary GetCategorySummary(
            ThingCategoryDef catDef, ThingFilter filter, Func<ThingDef, bool> isVisible,
            int maxNames = 10, Func<SpecialThingFilterDef, bool> isVisibleSpecial = null)
        {
            var summary = new CategorySummary
            {
                AllowedNames = new List<string>(),
                DisallowedNames = new List<string>()
            };

            if (filter.OnlySpecialFilters && isVisibleSpecial != null)
            {
                foreach (SpecialThingFilterDef sf in catDef.DescendantSpecialThingFilterDefs)
                {
                    if (!isVisibleSpecial(sf))
                        continue;

                    summary.TotalVisible++;

                    if (filter.Allows(sf))
                    {
                        summary.AllowedCount++;
                        if (summary.AllowedNames.Count < maxNames)
                            summary.AllowedNames.Add(sf.LabelCap);
                    }
                    else
                    {
                        summary.DisallowedCount++;
                        if (summary.DisallowedNames.Count < maxNames)
                            summary.DisallowedNames.Add(sf.LabelCap);
                    }
                }
            }
            else
            {
                foreach (ThingDef td in catDef.DescendantThingDefs)
                {
                    if (!isVisible(td))
                        continue;

                    summary.TotalVisible++;

                    if (filter.Allows(td))
                    {
                        summary.AllowedCount++;
                        if (summary.AllowedNames.Count < maxNames)
                            summary.AllowedNames.Add(td.LabelCap);
                    }
                    else
                    {
                        summary.DisallowedCount++;
                        if (summary.DisallowedNames.Count < maxNames)
                            summary.DisallowedNames.Add(td.LabelCap);
                    }
                }
            }

            if (summary.AllowedCount == 0)
                summary.State = CategoryAllowanceState.NoneAllowed;
            else if (summary.DisallowedCount == 0)
                summary.State = CategoryAllowanceState.AllAllowed;
            else
                summary.State = CategoryAllowanceState.SomeAllowed;

            return summary;
        }

        /// <summary>
        /// A category summary phrased from the minority perspective: "disallowed, except for: item1,
        /// item2", "allowed, except for: item1, item2", or "disallowed, except for 15 items" when
        /// there are too many to list.
        /// </summary>
        public static string FormatCategorySummary(CategorySummary summary)
        {
            // The only caller invokes this solely for a PartiallyChecked category, so both counts
            // are nonzero in practice; the empty guard covers the impossible zero/zero case.
            if (summary.AllowedCount == 0 || summary.DisallowedCount == 0)
                return string.Empty;

            if (summary.AllowedCount <= summary.DisallowedCount)
            {
                if (summary.AllowedCount <= 10)
                    return "RimWorldAccess.Inspection.Storage.Summary.DisallowedExceptList".Translate(
                        string.Join(", ", summary.AllowedNames));
                else
                    return "RimWorldAccess.Inspection.Storage.Summary.DisallowedExceptCount".Translate(
                        summary.AllowedCount);
            }
            else
            {
                if (summary.DisallowedCount <= 10)
                    return "RimWorldAccess.Inspection.Storage.Summary.AllowedExceptList".Translate(
                        string.Join(", ", summary.DisallowedNames));
                else
                    return "RimWorldAccess.Inspection.Storage.Summary.AllowedExceptCount".Translate(
                        summary.DisallowedCount);
            }
        }

        /// <summary>Vanilla-matching ThingDef visibility, mirroring Listing_TreeThingFilter.Visible(ThingDef).</summary>
        public static bool IsVisible(ThingDef td, ThingFilter parentFilter)
        {
            if (!td.PlayerAcquirable)
                return false;
            if (td.virtualDefParent != null)
                return false;
            if (Find.HiddenItemsManager.Hidden(td))
                return false;
            if (parentFilter != null)
            {
                if (!parentFilter.Allows(td))
                    return false;
                if (parentFilter.IsAlwaysDisallowedDueToSpecialFilters(td))
                    return false;
            }
            return true;
        }

        /// <summary>
        /// Whether a category has visible descendants, mirroring
        /// Listing_TreeThingFilter.Visible(TreeNode_ThingCategory): with
        /// <paramref name="currentFilter"/> set to <see cref="ThingFilter.OnlySpecialFilters"/>,
        /// existence is decided by descendant SpecialThingFilterDefs rather than ThingDefs. Without
        /// that branch a category built entirely of special filters always reports invisible and
        /// its whole subtree becomes unreachable, though vanilla shows and lets players toggle it.
        /// <paramref name="currentFilter"/> defaults to null so callers whose filter is never
        /// OnlySpecialFilters keep their prior behavior.
        /// </summary>
        public static bool IsVisibleCategory(TreeNode_ThingCategory node, ThingFilter parentFilter,
            ThingFilter currentFilter = null)
        {
            if (currentFilter != null && currentFilter.OnlySpecialFilters)
            {
                foreach (SpecialThingFilterDef sf in node.catDef.DescendantSpecialThingFilterDefs)
                {
                    if (IsVisibleForCategoryExistence(sf, parentFilter))
                        return true;
                }
                return false;
            }

            foreach (ThingDef td in node.catDef.DescendantThingDefs)
            {
                if (IsVisible(td, parentFilter))
                    return true;
            }
            return false;
        }

        /// <summary>
        /// Mirrors Listing_TreeThingFilter's one-arg Visible(SpecialThingFilterDef), used only by
        /// the category overload's OnlySpecialFilters branch to decide whether a node exists at all.
        /// Deliberately simpler than the per-checkbox <see cref="IsVisibleSpecialFilter"/>: no
        /// CanEverMatch or hiddenSpecialFilters scoping, just the parent filter's Allows check.
        /// </summary>
        private static bool IsVisibleForCategoryExistence(SpecialThingFilterDef f, ThingFilter parentFilter)
        {
            if (parentFilter != null && !parentFilter.Allows(f))
                return false;
            return true;
        }

        /// <summary>
        /// Vanilla-matching visibility for a special filter against a containing category and a
        /// parent filter, mirroring Listing_TreeThingFilter.Visible plus
        /// CalculateHiddenSpecialFilters: a special filter is hidden when no descendant ThingDef the
        /// parent filter allows can ever be matched by its Worker. The current filter is passed too,
        /// because vanilla short-circuits to visible when <c>filter.OnlySpecialFilters</c> is true.
        /// <paramref name="forceHiddenFilters"/> mirrors the call-site list vanilla folds into its
        /// own hiddenSpecialFilters cache.
        /// </summary>
        public static bool IsVisibleSpecialFilter(SpecialThingFilterDef f, TreeNode_ThingCategory node,
            ThingFilter currentFilter, ThingFilter parentFilter,
            ICollection<SpecialThingFilterDef> forceHiddenFilters = null)
        {
            if (parentFilter != null && !parentFilter.Allows(f))
                return false;
            if (currentFilter != null && currentFilter.OnlySpecialFilters)
                return true;
            if (forceHiddenFilters != null && forceHiddenFilters.Contains(f))
                return false;
            if (parentFilter != null && parentFilter.hiddenSpecialFilters != null
                && parentFilter.hiddenSpecialFilters.Contains(f))
                return false;
            if (f.Worker == null || node == null)
                return true;

            // Ask the worker whether it could ever match any descendant ThingDef, restricted to the
            // defs the parent filter allows, the same scoping vanilla uses.
            foreach (ThingDef td in node.catDef.DescendantThingDefs)
            {
                if (parentFilter != null && !parentFilter.Allows(td))
                    continue;
                if (f.Worker.CanEverMatch(td))
                    return true;
            }
            return false;
        }

        /// <summary>Resolves the category to the parent filter's DisplayRootCategory, or null when there is no parent filter.</summary>
        public static bool IsVisibleSpecialFilter(SpecialThingFilterDef f, ThingFilter parentFilter)
        {
            var node = parentFilter?.DisplayRootCategory;
            return IsVisibleSpecialFilter(f, node, currentFilter: null, parentFilter);
        }

        /// <summary>
        /// Mirrors Listing_TreeThingFilter.CalculateHiddenSpecialFilters plus its instance merge of
        /// the call-site force-hidden list: every special filter under or above the node whose
        /// Worker can never match a parent-allowed descendant ThingDef, or which the parent filter's
        /// own hiddenSpecialFilters names, or which the call site force-hides.
        /// </summary>
        public static List<SpecialThingFilterDef> ComputeHiddenSpecialFilters(
            TreeNode_ThingCategory node, ThingFilter parentFilter,
            ICollection<SpecialThingFilterDef> forceHidden)
        {
            var list = new List<SpecialThingFilterDef>();
            IEnumerable<SpecialThingFilterDef> candidates =
                node.catDef.ParentsSpecialThingFilterDefs.Concat(node.catDef.DescendantSpecialThingFilterDefs);
            IEnumerable<ThingDef> defs = node.catDef.DescendantThingDefs;
            if (parentFilter != null)
                defs = defs.Where(parentFilter.Allows);
            foreach (SpecialThingFilterDef f in candidates)
            {
                bool matched = false;
                foreach (ThingDef td in defs)
                {
                    if (f.Worker.CanEverMatch(td))
                    {
                        matched = true;
                        break;
                    }
                }
                // Vanilla omits the inner null check; guard it as IsVisibleSpecialFilter does.
                if (parentFilter != null && parentFilter.hiddenSpecialFilters != null
                    && parentFilter.hiddenSpecialFilters.Contains(f))
                    matched = false;
                if (!matched)
                    list.Add(f);
            }
            if (forceHidden != null)
                list.AddRange(forceHidden);
            return list;
        }
    }
}
