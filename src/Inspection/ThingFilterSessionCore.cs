using System;
using System.Collections.Generic;
using Verse;

namespace RimWorldAccess
{
    /// <summary>
    /// Shared tree-build, allowance-refresh, toggle and announcement engine for the ThingFilter
    /// screens (the policy window, <see cref="ThingFilterMenuState"/>,
    /// <see cref="StorageSettingsMenuState"/>). Each screen keeps its own top-of-tree rows, its own
    /// Escape/Enter router, and its own behavioral asymmetries.
    /// </summary>
    public static class ThingFilterSessionCore
    {
        /// <summary>Union of node kinds across the screens; each screen aliases its NodeType onto it.</summary>
        public enum NodeKind
        {
            Slider,             // no producer: the inline min/max sub-mode is retired
            SpecialFilter,
            Category,
            ThingDef,
            ClearAll,           // ThingFilterMenuState / StorageSettingsMenuState
            AllowAll,
            HitPointsRange,     // opens RangeEditMenuState
            QualityRange,
            Priority,           // StorageSettingsMenuState only
            UndiscoveredGroup   // Aggregated hidden-item row, mirrors Listing_TreeThingFilter.DoUndiscoveredEntry
        }

        /// <summary>Per-node payload stashed in <see cref="InspectionTreeItem.Data"/>.</summary>
        public class FilterNodeData
        {
            public NodeKind Type;
            public Shell.CheckState State;
            public object Reference;

            /// <summary>
            /// Last expansion state written into vanilla's TreeNode open bits by
            /// <see cref="Shell.ThingFilterTreeSync"/>. Null until the node is first drawn, which
            /// keeps a branch the player left open in vanilla from slamming shut on open.
            /// </summary>
            public bool? LastSyncedOpen;
        }

        /// <summary>Maps the tri-state allowance result onto the shared element vocabulary's CheckState.</summary>
        internal static Shell.CheckState ToCheckState(ThingFilterHelper.CategoryAllowanceState state)
        {
            switch (state)
            {
                case ThingFilterHelper.CategoryAllowanceState.AllAllowed: return Shell.CheckState.Checked;
                case ThingFilterHelper.CategoryAllowanceState.SomeAllowed: return Shell.CheckState.PartiallyChecked;
                default: return Shell.CheckState.Unchecked;
            }
        }

        /// <summary>
        /// Per-session parameters threaded through the shared logic. <see cref="ForceHiddenFilters"/>
        /// is always null for StorageSettingsMenuState — vanilla's StorageSettings has no such
        /// call-site parameter, and null matches the helper's optional-parameter default.
        /// </summary>
        public struct FilterContext
        {
            public ThingFilter CurrentFilter;
            public ThingFilter ParentFilter;
            public ICollection<SpecialThingFilterDef> ForceHiddenFilters;

            /// <summary>
            /// The session's display root category — the node vanilla's hidden-special-filter cache
            /// is keyed against (decompiled Verse/Listing_TreeThingFilter.cs:56-62). Null is
            /// tolerated by every consumer.
            /// </summary>
            public TreeNode_ThingCategory DisplayRoot;

            /// <summary>
            /// The openMask the vanilla panel passes <c>ThingFilterUI.DoThingFilterConfigWindow</c>,
            /// or 0 when no panel has been seen. Its bit in each <see cref="TreeNode_ThingCategory"/>
            /// is the game's own record of which branches are open, so a tree built with a known
            /// mask agrees with the panel on screen instead of starting collapsed.
            /// </summary>
            public int OpenMask;
        }

        /// <summary>Recursively adds special-filter, category and thing-def children to the tree.</summary>
        public static void AddCategoryChildren(FilterContext ctx, TreeNode_ThingCategory node,
            InspectionTreeItem parent, int indentLevel, bool isRoot = false)
        {
            if (isRoot)
            {
                foreach (var specialFilter in node.catDef.ParentsSpecialThingFilterDefs)
                {
                    if (specialFilter.configurable && ThingFilterHelper.IsVisibleSpecialFilter(
                        specialFilter, node, ctx.CurrentFilter, ctx.ParentFilter, ctx.ForceHiddenFilters))
                    {
                        parent.Children.Add(new InspectionTreeItem
                        {
                            Type = InspectionTreeItem.ItemType.Item,
                            Label = specialFilter.LabelCap,
                            Description = specialFilter.description,
                            IndentLevel = indentLevel,
                            IsExpandable = false,
                            Parent = parent,
                            Data = new FilterNodeData
                            {
                                Type = NodeKind.SpecialFilter,
                                State = ctx.CurrentFilter.Allows(specialFilter) ? Shell.CheckState.Checked : Shell.CheckState.Unchecked,
                                Reference = specialFilter
                            }
                        });
                    }
                }
            }

            foreach (var specialFilter in node.catDef.childSpecialFilters)
            {
                if (specialFilter.configurable && ThingFilterHelper.IsVisibleSpecialFilter(
                    specialFilter, node, ctx.CurrentFilter, ctx.ParentFilter, ctx.ForceHiddenFilters))
                {
                    parent.Children.Add(new InspectionTreeItem
                    {
                        Type = InspectionTreeItem.ItemType.Item,
                        Label = specialFilter.LabelCap,
                        Description = specialFilter.description,
                        IndentLevel = indentLevel,
                        IsExpandable = false,
                        Parent = parent,
                        Data = new FilterNodeData
                        {
                            Type = NodeKind.SpecialFilter,
                            State = ctx.CurrentFilter.Allows(specialFilter) ? Shell.CheckState.Checked : Shell.CheckState.Unchecked,
                            Reference = specialFilter
                        }
                    });
                }
            }

            // Recurse fully so TreeNavigationHelper drives expand/collapse natively. Expansion
            // starts from vanilla's open bit for the drawing panel's mask, collapsed when no panel
            // has been seen.
            foreach (var childCategory in node.ChildCategoryNodes)
            {
                if (!ThingFilterHelper.IsVisibleCategory(childCategory, ctx.ParentFilter, ctx.CurrentFilter))
                    continue;

                var allowanceState = ThingFilterHelper.GetAllowanceState(
                    childCategory.catDef, ctx.CurrentFilter, td => ThingFilterHelper.IsVisible(td, ctx.ParentFilter),
                    sf => ThingFilterHelper.IsVisibleSpecialFilter(sf, childCategory, ctx.CurrentFilter, ctx.ParentFilter, ctx.ForceHiddenFilters));

                var catNode = new InspectionTreeItem
                {
                    Type = InspectionTreeItem.ItemType.Category,
                    Label = childCategory.LabelCap,
                    Description = childCategory.catDef.description,
                    IndentLevel = indentLevel,
                    IsExpandable = true,
                    IsExpanded = ctx.OpenMask != 0 && childCategory.IsOpen(ctx.OpenMask),
                    Parent = parent,
                    Data = new FilterNodeData
                    {
                        Type = NodeKind.Category,
                        State = ToCheckState(allowanceState),
                        Reference = childCategory
                    }
                };
                parent.Children.Add(catNode);

                AddCategoryChildren(ctx, childCategory, catNode, indentLevel + 1);
            }

            // Vanilla checks Hidden BEFORE Visible (Listing_TreeThingFilter.cs:83-98), so a
            // hidden-but-parent-disallowed def still joins the undiscovered row below.
            List<ThingDef> undiscovered = null;
            foreach (var thingDef in node.catDef.SortedChildThingDefs)
            {
                if (Find.HiddenItemsManager.Hidden(thingDef))
                {
                    (undiscovered ?? (undiscovered = new List<ThingDef>())).Add(thingDef);
                }
                else if (ThingFilterHelper.IsVisible(thingDef, ctx.ParentFilter))
                {
                    parent.Children.Add(new InspectionTreeItem
                    {
                        Type = InspectionTreeItem.ItemType.Item,
                        Label = thingDef.LabelCap,
                        Description = thingDef.DescriptionDetailed,
                        IndentLevel = indentLevel,
                        IsExpandable = false,
                        Parent = parent,
                        LinkedDef = thingDef,
                        Data = new FilterNodeData
                        {
                            Type = NodeKind.ThingDef,
                            State = ctx.CurrentFilter.Allows(thingDef) ? Shell.CheckState.Checked : Shell.CheckState.Unchecked,
                            Reference = thingDef
                        }
                    });
                }
            }
            if (undiscovered != null)
            {
                parent.Children.Add(new InspectionTreeItem
                {
                    Type = InspectionTreeItem.ItemType.Item,
                    Label = "UndiscoveredItemLabel".Translate(),
                    Description = "UndiscoveredItemDesc".Translate().Resolve(),
                    IndentLevel = indentLevel,
                    IsExpandable = false,
                    Parent = parent,
                    Data = new FilterNodeData
                    {
                        Type = NodeKind.UndiscoveredGroup,
                        State = ctx.CurrentFilter.Allows(undiscovered[0]) ? Shell.CheckState.Checked : Shell.CheckState.Unchecked,
                        Reference = undiscovered
                    }
                });
            }
        }

        /// <summary>
        /// The synthetic, non-navigable root every filter screen hangs its category tree under.
        /// HitPoints/Quality/MentalBreakChance are not tree nodes but the scope's own prefix rows.
        /// </summary>
        public static InspectionTreeItem EmptyTreeRoot()
        {
            return new InspectionTreeItem
            {
                Label = "Root",
                IndentLevel = -1,
                IsExpanded = true,
                IsExpandable = false
            };
        }

        /// <summary><see cref="EmptyTreeRoot"/> populated from a context's display root.</summary>
        public static InspectionTreeItem BuildCategoryTreeRoot(FilterContext ctx)
        {
            InspectionTreeItem root = EmptyTreeRoot();
            if (ctx.DisplayRoot != null)
            {
                AddCategoryChildren(ctx, ctx.DisplayRoot, root, 0, isRoot: true);
            }
            return root;
        }

        /// <summary>
        /// Refreshes every leaf and category node's check state from the underlying filter,
        /// leaving tree structure and cursor untouched.
        /// </summary>
        public static void RefreshAllowanceStates(FilterContext ctx, InspectionTreeItem rootItem)
        {
            if (rootItem == null) return;
            RefreshAllowanceStatesRecursive(ctx, rootItem);
        }

        private static void RefreshAllowanceStatesRecursive(FilterContext ctx, InspectionTreeItem node)
        {
            var data = node.Data as FilterNodeData;
            if (data != null)
            {
                switch (data.Type)
                {
                    case NodeKind.SpecialFilter:
                        if (data.Reference is SpecialThingFilterDef sf)
                            data.State = ctx.CurrentFilter.Allows(sf) ? Shell.CheckState.Checked : Shell.CheckState.Unchecked;
                        break;
                    case NodeKind.ThingDef:
                        if (data.Reference is ThingDef td)
                            data.State = ctx.CurrentFilter.Allows(td) ? Shell.CheckState.Checked : Shell.CheckState.Unchecked;
                        break;
                    case NodeKind.Category:
                        if (data.Reference is TreeNode_ThingCategory cat)
                        {
                            var state = ThingFilterHelper.GetAllowanceState(
                                cat.catDef, ctx.CurrentFilter,
                                t => ThingFilterHelper.IsVisible(t, ctx.ParentFilter),
                                catSf => ThingFilterHelper.IsVisibleSpecialFilter(catSf, cat, ctx.CurrentFilter, ctx.ParentFilter, ctx.ForceHiddenFilters));
                            data.State = ToCheckState(state);
                        }
                        break;
                    case NodeKind.UndiscoveredGroup:
                        if (data.Reference is List<ThingDef> group && group.Count > 0)
                            data.State = ctx.CurrentFilter.Allows(group[0]) ? Shell.CheckState.Checked : Shell.CheckState.Unchecked;
                        break;
                }
            }

            foreach (var child in node.Children)
                RefreshAllowanceStatesRecursive(ctx, child);
        }

        /// <summary>
        /// Toggles a SpecialFilter/Category/ThingDef/UndiscoveredGroup node and reports the
        /// resulting state; speech and the checkbox sound are the caller's job. Returns false
        /// without acting for every other NodeKind, so each screen's own switch can handle its
        /// unique rows afterward.
        /// </summary>
        public static bool TryToggleCommonNode(FilterContext ctx, InspectionTreeItem item,
            FilterNodeData data, InspectionTreeItem rootItem, out Shell.CheckState resultState)
        {
            switch (data.Type)
            {
                case NodeKind.SpecialFilter:
                    if (data.Reference is SpecialThingFilterDef specialFilter)
                    {
                        ctx.CurrentFilter.SetAllow(specialFilter, !ctx.CurrentFilter.Allows(specialFilter));
                    }
                    break;

                case NodeKind.Category:
                    if (data.Reference is TreeNode_ThingCategory category)
                    {
                        // Tri-state toggle matching vanilla: Off->On, Partial->On, On->Off.
                        var state = ThingFilterHelper.GetAllowanceState(
                            category.catDef, ctx.CurrentFilter, td => ThingFilterHelper.IsVisible(td, ctx.ParentFilter),
                            sf => ThingFilterHelper.IsVisibleSpecialFilter(sf, category, ctx.CurrentFilter, ctx.ParentFilter, ctx.ForceHiddenFilters));
                        bool desiredCat = state != ThingFilterHelper.CategoryAllowanceState.AllAllowed;
                        // Vanilla computes hiddenSpecialFilters against the session's display root,
                        // not the toggled node (Verse/Listing_TreeThingFilter.cs:177).
                        List<SpecialThingFilterDef> hidden = ctx.DisplayRoot != null
                            ? ThingFilterHelper.ComputeHiddenSpecialFilters(ctx.DisplayRoot, ctx.ParentFilter, ctx.ForceHiddenFilters)
                            : (ctx.ForceHiddenFilters != null ? new List<SpecialThingFilterDef>(ctx.ForceHiddenFilters) : null);
                        ctx.CurrentFilter.SetAllow(category.catDef, desiredCat, null, hidden);
                    }
                    break;

                case NodeKind.ThingDef:
                    if (data.Reference is ThingDef thingDef)
                    {
                        ctx.CurrentFilter.SetAllow(thingDef, !ctx.CurrentFilter.Allows(thingDef));
                    }
                    break;

                case NodeKind.UndiscoveredGroup:
                    // Mirrors DoUndiscoveredEntry:260-268: one checkbox drives the whole
                    // hidden-item list, keyed off the first def's state.
                    if (data.Reference is List<ThingDef> group && group.Count > 0)
                    {
                        bool desiredGroup = !ctx.CurrentFilter.Allows(group[0]);
                        foreach (ThingDef td in group)
                            ctx.CurrentFilter.SetAllow(td, desiredGroup);
                    }
                    break;

                default:
                    resultState = Shell.CheckState.Unchecked;
                    return false;
            }

            // The whole tree is re-read because one toggle changes other rows: SetAllow(ThingDef)
            // cascades through virtualDefs (Verse/ThingFilter.cs:669-691) and one special filter is
            // a descendant of many categories, so ancestors-only invalidation goes stale. A null
            // root means the tree was never built, leaving only the toggled node to refresh.
            RefreshAllowanceStates(ctx, rootItem ?? item);
            resultState = data.State;
#if DEBUG
            TraceToggle(item, data);
#endif
            return true;
        }

        /// <summary>
        /// Extras fragment for a Category row: the exception summary followed by the category
        /// description. The summary is spoken only when the tri-state is partial, since
        /// "checked"/"not checked" already covers the total states, and is derived from vanilla's
        /// live Allows()/visibility decisions.
        /// </summary>
        public static string CategoryExtras(FilterContext ctx, InspectionTreeItem item, FilterNodeData data)
        {
            string desc = item.Description ?? "";
            if (data.State != Shell.CheckState.PartiallyChecked || !(data.Reference is TreeNode_ThingCategory catNode))
                return desc;
            var summary = ThingFilterHelper.GetCategorySummary(
                catNode.catDef, ctx.CurrentFilter, td => ThingFilterHelper.IsVisible(td, ctx.ParentFilter),
                isVisibleSpecial: sf => ThingFilterHelper.IsVisibleSpecialFilter(sf, catNode, ctx.CurrentFilter, ctx.ParentFilter, ctx.ForceHiddenFilters));
            string summaryText = ThingFilterHelper.FormatCategorySummary(summary);
            return string.IsNullOrEmpty(desc) ? summaryText : summaryText + ". " + desc;
        }

#if DEBUG
        /// <summary>
        /// QA flight recorder line for one toggle: the toggled row's post-refresh state plus every
        /// ancestor category's recomputed state, so a stale ancestor is visible in one line.
        /// </summary>
        private static void TraceToggle(InspectionTreeItem item, FilterNodeData data)
        {
            var line = new System.Text.StringBuilder();
            line.Append("toggle ").Append(data.Type).Append(" \"").Append(item.Label)
                .Append("\" -> ").Append(data.State);
            string separator = "; ancestors: ";
            for (InspectionTreeItem parent = item.Parent; parent != null; parent = parent.Parent)
            {
                if (parent.Data is FilterNodeData parentData && parentData.Type == NodeKind.Category)
                {
                    line.Append(separator).Append(parent.Label).Append('=').Append(parentData.State);
                    separator = ", ";
                }
            }
            RimWorldAccess.Shell.ShellDev.QARecord("filter", line.ToString());
        }
#endif

        /// <summary>
        /// Reuses the caller's regular item formatter and appends the typeahead match suffix.
        /// </summary>
        public static string FormatSearchAnnouncement(InspectionTreeItem item, TypeaheadSearchHelper typeahead,
            Func<InspectionTreeItem, string> formatItem)
        {
            string baseAnnouncement = formatItem(item);
            if (!typeahead.HasActiveSearch)
                return baseAnnouncement;
            return baseAnnouncement + typeahead.BuildSearchContextSuffix();
        }

        public static void ProcessTypeaheadCharacter(bool isActive, TreeNavigationHelper treeNav, char c)
        {
            if (!isActive || treeNav.Count == 0) return;
            treeNav.HandleTypeaheadCharacter(c);
        }

        public static void ProcessBackspace(bool isActive, TreeNavigationHelper treeNav)
        {
            if (!isActive || treeNav.Count == 0) return;
            treeNav.HandleTypeaheadBackspace();
        }

        public static void SelectNextMatch(TreeNavigationHelper treeNav, Action announceWithSearch)
        {
            if (treeNav.Count == 0) return;
            int nextIndex = treeNav.Typeahead.GetNextMatch(treeNav.SelectedIndex);
            if (nextIndex >= 0)
            {
                treeNav.SetSelectedIndex(nextIndex);
                announceWithSearch();
            }
        }

        public static void SelectPreviousMatch(TreeNavigationHelper treeNav, Action announceWithSearch)
        {
            if (treeNav.Count == 0) return;
            int prevIndex = treeNav.Typeahead.GetPreviousMatch(treeNav.SelectedIndex);
            if (prevIndex >= 0)
            {
                treeNav.SetSelectedIndex(prevIndex);
                announceWithSearch();
            }
        }

        public static void AnnounceWithSearch(TreeNavigationHelper treeNav,
            Func<InspectionTreeItem, TypeaheadSearchHelper, string> formatSearchAnnouncement)
        {
            if (treeNav.SelectedItem == null) return;

            if (treeNav.HasActiveSearch)
            {
                TolkHelper.SpeakData(formatSearchAnnouncement(treeNav.SelectedItem, treeNav.Typeahead));
            }
            else
            {
                treeNav.ReannounceCurrentItem();
            }
        }
    }
}
