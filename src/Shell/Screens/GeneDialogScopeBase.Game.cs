using System.Collections.Generic;
using RimWorld;
using Verse;
using Verse.Sound;

namespace RimWorldAccess.Shell
{
    /// <summary>
    /// Shared base for the two gene-editing dialogs (<see cref="XenogermScope"/>,
    /// <see cref="XenotypeEditorScope"/>). Three regions: Selected (0) and Library (1), each hosted
    /// by its own <see cref="GeneTreeRegion"/>, plus Controls (2), a flat list of element-role rows.
    ///
    /// Subclasses <c>ScreenScope</c> rather than <see cref="TreeRegionScope"/>, which hosts exactly
    /// one content region and one <c>TreeModel&lt;InspectionTreeItem&gt;</c> field and so cannot
    /// carry two independent trees plus a flat third; the region-parameterized content contract
    /// makes composition the working shape here.
    ///
    /// Enter/Space activate only: on a non-toggle-target node they re-announce, and expanding a
    /// gene's detail children is Right arrow, matching the chassis content-item contract.
    ///
    /// Escape needs an explicit unconditional claim even though this scope is window-attached:
    /// <c>XenogermPatch.Window_OnCancelKeyPressed_Patch</c> blocks vanilla's
    /// <c>Window.OnCancelKeyPressed</c> unconditionally while either state is active, so the
    /// window-attached default (search-clear only) would leave Escape dead with no search running.
    /// <see cref="OwnsCancel"/> is therefore true and <c>menus.cancel</c> is claimed a second time:
    /// search-clear first via the base ctor's claim, dialog-close second.
    /// </summary>
    public abstract class GeneDialogScopeBase : ScreenScope
    {
        protected const int SelectedRegionIndex = 0;
        protected const int LibraryRegionIndex = 1;
        protected const int ControlsRegionIndex = 2;

        protected readonly GeneTreeRegion SelectedTree;
        protected readonly GeneTreeRegion LibraryTree;

        protected GeneDialogScopeBase()
        {
            SelectedTree = new GeneTreeRegion(LazyLoadChildren, ShouldAutoExpandForSearch);
            LibraryTree = new GeneTreeRegion(LazyLoadChildren, ShouldAutoExpandForSearch);

            // Search-clear (base ctor) wins while a search is active; this wins otherwise.
            Claim(SharedMenuGrammar.Cancel, e => HandleDialogClose(), when: () => !TypeaheadHasActiveSearch);
        }

        public override bool OwnsCancel
        {
            get { return true; }
        }

        public override void OnPush()
        {
            base.OnPush();
            GeneRowDrawPatch.BeginRecording();
        }

        public override void OnPop()
        {
            GeneRowDrawPatch.EndRecording();
            base.OnPop();
        }

        protected internal override UnityEngine.Rect FocusedContentRect()
        {
            int region = Model.RegionIndex;
            if (region != SelectedRegionIndex && region != LibraryRegionIndex)
            {
                // Controls rows draw through no vanilla gene-row method; the Buttons region already
                // rings through the button capture.
                return default(UnityEngine.Rect);
            }
            InspectionTreeItem item = CurrentTreeItemOrNull();
            object gene = item != null ? item.Data : null;
            if (!(gene is GeneDef) && !(gene is Gene))
            {
                // Category/genepack headers and a gene's detail children have no vanilla row.
                return default(UnityEngine.Rect);
            }
            // One GeneDef legally draws twice — Dialog_CreateXenotype.DrawGenes draws the selected
            // section first and the library section second — so the focused region picks the rect.
            return region == SelectedRegionIndex
                ? GeneRowDrawPatch.Rows.FindFirst(gene)
                : GeneRowDrawPatch.Rows.FindLast(gene);
        }

        // The per-dialog contract.

        /// <summary>Localized, punctuation-free noun phrase for one of the three regions (0=Selected, 1=Library, 2=Controls).</summary>
        protected abstract string RegionLabel(int region);

        /// <summary>Whether a tree node is this dialog's toggle target (Xenogerm: Genepack at indent 0; XenotypeEditor: GeneDef at any indent).</summary>
        protected abstract bool IsToggleTarget(InspectionTreeItem item);

        /// <summary>
        /// Performs the toggle mutation and speaks its own one-shot feedback message.
        /// <paramref name="regionIndex"/> is whichever of Selected/Library was focused.
        /// </summary>
        protected abstract void PerformToggle(InspectionTreeItem item, int regionIndex);

        /// <summary>Rebuilds both trees from live dialog state via <see cref="GeneTreeRegion.SetRoot"/>.</summary>
        protected abstract void RebuildTrees();

        /// <summary>Escape with no active search: close the dialog.</summary>
        protected abstract void HandleDialogClose();

        /// <summary>Non-Def Alt+I target (Xenogerm's Genepack rows); false where every toggleable node already carries a LinkedDef.</summary>
        protected virtual bool TryOpenInfoCardForNonDefData(object data)
        {
            return false;
        }

        /// <summary>Which nodes typeahead auto-expands on the first search character (Genepack for Xenogerm; GeneCategoryDef header for XenotypeEditor's Library).</summary>
        protected virtual bool ShouldAutoExpandForSearch(InspectionTreeItem item)
        {
            return false;
        }

        /// <summary>
        /// Vanilla draws the name-suggestions picker as a bare "...", which a sighted player decodes
        /// from its position beside Randomize; name it for its function instead.
        /// </summary>
        protected override string CapturedButtonLabel(int captureIndex, string rawLabel)
        {
            return rawLabel == "..."
                ? "RimWorldAccess.Biotech.GeneDialogs.NameSuggestions".Translate().ToString()
                : base.CapturedButtonLabel(captureIndex, rawLabel);
        }

        protected abstract int ControlsRowCount { get; }
        protected abstract ElementDescription DescribeControlsRow(int index);
        protected abstract void ActivateControlsRow(int index);

        protected virtual bool CanAdjustControlsRow(int index)
        {
            return false;
        }

        protected virtual void AdjustControlsRow(int index, int direction)
        {
        }

        private static void LazyLoadChildren(InspectionTreeItem item)
        {
            if (item.OnActivate != null && item.Children.Count == 0)
            {
                item.OnActivate();
            }
        }

        // Shared chord bodies subclasses wire under their own action ids; ShellActionInventory has
        // no cross-screen id sharing.

        /// <summary>PageUp/PageDown: jump to the adjacent top-level sibling; a no-op on Controls.</summary>
        protected void JumpSection(bool forward)
        {
            RefreshModel();
            GeneTreeRegion tree = TreeRegionFor(Model.RegionIndex);
            if (tree == null)
            {
                return;
            }
            bool moved = tree.JumpToAdjacentSection(forward, WrapNavigationSetting);
            if (moved)
            {
                SoundDefOf.Tick_Tiny.PlayOneShotOnCamera();
                ListModel region = Model.CurrentRegion;
                if (region != null)
                {
                    SyncRegionFromTree(tree, region);
                }
                AnnounceCurrentItem();
            }
            else
            {
                SoundDefOf.ClickReject.PlayOneShotOnCamera();
            }
        }

        /// <summary>Ctrl+Home/Ctrl+End: absolute tree jump, against plain Home/End's sibling-scoped one; identical to Home/End on Controls.</summary>
        protected void JumpAbsoluteEdge(bool first)
        {
            TypeaheadReset();
            RefreshModel();
            ListModel region = Model.CurrentRegion;
            if (region == null || region.IsEmpty)
            {
                return;
            }
            GeneTreeRegion tree = TreeRegionFor(Model.RegionIndex);
            if (tree == null)
            {
                if (first) region.MoveFirst(); else region.MoveLast();
                AnnounceCurrent(CellAxis.Row);
                return;
            }
            MoveResult result = first ? tree.Tree.HomeKey(true) : tree.Tree.EndKey(true);
            if (result.Kind == MoveKind.Empty)
            {
                return;
            }
            SyncRegionFromTree(tree, region);
            SoundDefOf.Tick_Tiny.PlayOneShotOnCamera();
            AnnounceCurrent(CellAxis.Row);
        }

        /// <summary>Alt+I: info card for the focused tree row's linked Def, or the dialog's own non-Def target (Xenogerm's Genepack).</summary>
        protected void ActivateGeneDialogInfoCard()
        {
            InspectionTreeItem item = CurrentTreeItemOrNull();
            if (item != null)
            {
                if (item.LinkedDef != null)
                {
                    Find.WindowStack.Add(new Dialog_InfoCard(item.LinkedDef));
                    SoundDefOf.Click.PlayOneShotOnCamera();
                    return;
                }
                if (TryOpenInfoCardForNonDefData(item.Data))
                {
                    SoundDefOf.Click.PlayOneShotOnCamera();
                    return;
                }
            }
            SoundDefOf.ClickReject.PlayOneShotOnCamera();
        }

        private static bool WrapNavigationSetting
        {
            get { return RimWorldAccessMod_Settings.Settings != null && RimWorldAccessMod_Settings.Settings.WrapNavigation; }
        }

        /// <summary>Shared submenu-vs-standard tree navigation mode read for both trees' <see cref="GeneTreeRegion.SetRoot"/> calls.</summary>
        protected static bool SubmenuTreeNavigationSetting
        {
            get { return RimWorldAccessMod_Settings.Settings != null && RimWorldAccessMod_Settings.Settings.SubmenuTreeNavigation; }
        }

        private GeneTreeRegion TreeRegionFor(int region)
        {
            if (region == SelectedRegionIndex) return SelectedTree;
            if (region == LibraryRegionIndex) return LibraryTree;
            return null;
        }

        private void SyncRegionFromTree(GeneTreeRegion tree, ListModel region)
        {
            int target = tree.Tree.SelectedIndex;
            if (target >= 0 && target < region.Count)
            {
                region.MoveTo(target);
            }
        }

        /// <summary>The tree item under the region cursor right now, or null while resting on Controls.</summary>
        protected InspectionTreeItem CurrentTreeItemOrNull()
        {
            GeneTreeRegion tree = TreeRegionFor(Model.RegionIndex);
            if (tree == null)
            {
                return null;
            }
            ListModel region = Model.CurrentRegion;
            if (region == null || region.IsEmpty)
            {
                return null;
            }
            int idx = region.Index;
            if (idx < 0 || idx >= tree.Tree.Count)
            {
                return null;
            }
            return tree.Tree.Visible[idx];
        }

        protected override int ContentRegionCount
        {
            get { return 3; }
        }

        protected override string ContentRegionName(int region)
        {
            return RegionLabel(region);
        }

        protected override int ContentItemCount(int region)
        {
            GeneTreeRegion tree = TreeRegionFor(region);
            return tree != null ? tree.Tree.Count : ControlsRowCount;
        }

        protected override ElementDescription DescribeContentItem(int region, int index)
        {
            GeneTreeRegion tree = TreeRegionFor(region);
            if (tree == null)
            {
                return DescribeControlsRow(index) ?? new ElementDescription();
            }
            if (index < 0 || index >= tree.Tree.Count)
            {
                return new ElementDescription();
            }
            InspectionTreeItem item = tree.Tree.Visible[index];
            var d = new ElementDescription();
            d.Role = ElementRole.TreeItem;
            d.Label = item.Label.StripTags();
            d.Selected = item.Selected;
            if (!item.IsExpanded)
            {
                d.Extras = item.Detail;
            }
            if (item.IsExpandable)
            {
                d.Expanded = item.IsExpanded;
            }
            var siblingPosition = tree.Tree.GetSiblingPosition(item);
            d.PositionIndex = siblingPosition.position;
            d.PositionCount = siblingPosition.total;
            // Unconditional: DescribeContentItem is reachable from the typeahead haystack build,
            // which would corrupt a change-gated "last level spoken" tracker before the real
            // announcement ever consults it.
            d.Level = item.IndentLevel + 1;
            return d;
        }

        /// <summary>Matched against the detail too, keeping genes findable by effect and biostat.</summary>
        protected override string ContentRowSearchText(int region, int row)
        {
            GeneTreeRegion tree = TreeRegionFor(region);
            if (tree == null || row < 0 || row >= tree.Tree.Count)
            {
                return base.ContentRowSearchText(region, row);
            }
            return tree.Tree.Visible[row].SearchText.StripTags();
        }

        protected override void ActivateContentItem(int region, int index)
        {
            GeneTreeRegion tree = TreeRegionFor(region);
            if (tree == null)
            {
                ActivateControlsRow(index);
                return;
            }
            if (index < 0 || index >= tree.Tree.Count)
            {
                return;
            }
            tree.Tree.SetSelectedIndex(index);
            InspectionTreeItem item = tree.Tree.Visible[index];
            if (!IsToggleTarget(item))
            {
                AnnounceCurrentItem();
                return;
            }
            object cursorData = item.Data;
            PerformToggle(item, region);
            RebuildTreesSatisfyingSignal();
            RestoreCursorAfterToggle(region, cursorData);
        }

        /// <summary>
        /// Rebuild both trees and mark any pending external-change signal satisfied. Not usable from
        /// a subclass constructor: <see cref="ScreenScope.OwnedWindow"/> resolves through
        /// <c>ScopeForWindow</c>, which records the attachment only after the factory returns, so a
        /// ctor-time call would clear nothing; the first post-attach
        /// <see cref="RefreshContent"/> consumes the signal vanilla's PostOpen raises.
        /// </summary>
        protected void RebuildTreesSatisfyingSignal()
        {
            RebuildTrees();
            GeneDialogGenesChangedPatch.ClearFor(OwnedWindow);
        }

        private void RestoreCursorAfterToggle(int region, object cursorData)
        {
            RefreshModel();
            GeneTreeRegion tree = TreeRegionFor(region);
            ListModel regionModel = Model.CurrentRegion;
            if (tree == null || regionModel == null)
            {
                return;
            }
            if (cursorData != null)
            {
                IReadOnlyList<InspectionTreeItem> visible = tree.Tree.Visible;
                for (int i = 0; i < visible.Count; i++)
                {
                    if (Equals(visible[i].Data, cursorData))
                    {
                        tree.Tree.SetSelectedIndex(i);
                        break;
                    }
                }
            }
            SyncRegionFromTree(tree, regionModel);
        }

        protected override bool CanAdjustContentItem(int region, int index)
        {
            // Every tree row participates in Left/Right: expand/collapse, or a silent reject.
            if (TreeRegionFor(region) != null)
            {
                return true;
            }
            return CanAdjustControlsRow(index);
        }

        protected override void AdjustContentItem(int region, int index, int direction)
        {
            GeneTreeRegion tree = TreeRegionFor(region);
            if (tree == null)
            {
                AdjustControlsRow(index, direction);
                return;
            }
            if (index < 0 || index >= tree.Tree.Count)
            {
                return;
            }
            TypeaheadReset();
            tree.Tree.SetSelectedIndex(index);
            if (direction > 0)
            {
                PerformExpand(tree, region);
            }
            else
            {
                PerformCollapse(tree, region);
            }
        }

        /// <summary>Our own Left/Right just expanded or collapsed <paramref name="node"/>; dialogs
        /// whose expansion state vanilla owns mirror the flip into vanilla's map here.</summary>
        protected virtual void OnNodeExpansionToggled(int region, InspectionTreeItem node)
        {
        }

        // TreeRegionScope's own expand/collapse is private and tied to its single Tree field, so it
        // is not reusable across two independent tree instances.
        private void PerformExpand(GeneTreeRegion tree, int region)
        {
            TreeActionResult<InspectionTreeItem> result = tree.Tree.ExpandOrDrillDown();
            switch (result.Kind)
            {
                case TreeActionKind.Rejected:
                    SoundDefOf.ClickReject.PlayOneShotOnCamera();
                    break;
                case TreeActionKind.Expanded:
                case TreeActionKind.ExpandedSubmenu:
                    SoundDefOf.FloatMenu_Open.PlayOneShotOnCamera();
                    OnNodeExpansionToggled(region, result.Node);
                    SyncAndAnnounce(tree);
                    break;
                case TreeActionKind.DrilledToChild:
                    SoundDefOf.Tick_Tiny.PlayOneShotOnCamera();
                    SyncAndAnnounce(tree);
                    break;
            }
        }

        private void PerformCollapse(GeneTreeRegion tree, int region)
        {
            TreeActionResult<InspectionTreeItem> result = tree.Tree.CollapseOrDrillUp();
            switch (result.Kind)
            {
                case TreeActionKind.Rejected:
                    SoundDefOf.ClickReject.PlayOneShotOnCamera();
                    break;
                case TreeActionKind.Collapsed:
                case TreeActionKind.CollapsedToParent:
                    SoundDefOf.FloatMenu_Cancel.PlayOneShotOnCamera();
                    OnNodeExpansionToggled(region, result.Node);
                    SyncAndAnnounce(tree);
                    break;
                case TreeActionKind.DrilledToParent:
                    SoundDefOf.Tick_Tiny.PlayOneShotOnCamera();
                    SyncAndAnnounce(tree);
                    break;
            }
        }

        private void SyncAndAnnounce(GeneTreeRegion tree)
        {
            ListModel region = Model.CurrentRegion;
            if (region != null)
            {
                SyncRegionFromTree(tree, region);
            }
            AnnounceCurrentItem();
        }

        // Home/End: sibling-scoped tree jump on a tree region, plain flat jump on Controls. Always
        // clears an active search first rather than jumping to a match, uniformly in all regions.

        protected override void MoveItemEdge(bool first)
        {
            TypeaheadReset();
            RefreshModel();
            ListModel region = Model.CurrentRegion;
            if (region == null || region.IsEmpty)
            {
                return;
            }
            GeneTreeRegion tree = TreeRegionFor(Model.RegionIndex);
            if (tree == null)
            {
                if (first) region.MoveFirst(); else region.MoveLast();
                AnnounceCurrent(CellAxis.Row);
                return;
            }
            MoveResult result = first ? tree.Tree.HomeKey(false) : tree.Tree.EndKey(false);
            if (result.Kind == MoveKind.Empty)
            {
                return;
            }
            SyncRegionFromTree(tree, region);
            SoundDefOf.Tick_Tiny.PlayOneShotOnCamera();
            AnnounceCurrent(CellAxis.Row);
        }

        protected override bool EnableTypeahead
        {
            get { return true; }
        }

        protected override void OnTypeaheadWillSearch()
        {
            GeneTreeRegion tree = TreeRegionFor(Model.RegionIndex);
            if (tree != null)
            {
                tree.EnsureSearchExpansion();
            }
        }

        protected override void RefreshContent()
        {
            if (TypeaheadHasActiveSearch)
            {
                // A rebuild would fight the search-expansion snapshot; the signal stays pending.
                return;
            }
            if (GeneDialogGenesChangedPatch.ConsumeIfPending(OwnedWindow))
            {
                SyncFocusedTreeToRegionCursor();
                RebuildTrees();
            }
            SyncExternalCollapseState();
            if (SelectedTree.HasPendingSearchExpansion)
            {
                SelectedTree.RestoreSearchExpansionIfPending(RegionCursorItem(SelectedRegionIndex, SelectedTree));
                SyncRegionAfterRestore(SelectedRegionIndex, SelectedTree);
            }
            if (LibraryTree.HasPendingSearchExpansion)
            {
                LibraryTree.RestoreSearchExpansionIfPending(RegionCursorItem(LibraryRegionIndex, LibraryTree));
                SyncRegionAfterRestore(LibraryRegionIndex, LibraryTree);
            }
        }

        /// <summary>Per-dialog hook for expansion state vanilla owns; runs after the gene-set signal so it sees post-rebuild trees.</summary>
        protected virtual void SyncExternalCollapseState()
        {
        }

        /// <summary>
        /// The row the region cursor perceives in <paramref name="tree"/>'s region, or null when
        /// that region is not focused. The keep-path restore must anchor here, not on the tree's own
        /// SelectedIndex: plain Up/Down and the typeahead match walk move only the region cursor.
        /// </summary>
        protected InspectionTreeItem RegionCursorItem(int regionIndex, GeneTreeRegion tree)
        {
            if (Model.RegionIndex != regionIndex)
            {
                return null;
            }
            ListModel region = Model.CurrentRegion;
            if (region == null || region.IsEmpty)
            {
                return null;
            }
            int index = region.Index;
            if (index < 0 || index >= tree.Tree.Count)
            {
                return null;
            }
            return tree.Tree.Visible[index];
        }

        /// <summary>
        /// Pushes the region cursor into the focused tree's own selection: an externally triggered
        /// rebuild anchors state preservation on <c>Tree.SelectedIndex</c>, which plain Up/Down
        /// never move. The non-focused tree keeps its own last selection, the right anchor for it.
        /// </summary>
        private void SyncFocusedTreeToRegionCursor()
        {
            GeneTreeRegion tree = TreeRegionFor(Model.RegionIndex);
            if (tree == null)
            {
                return;
            }
            ListModel region = Model.CurrentRegion;
            if (region == null || region.IsEmpty)
            {
                return;
            }
            int index = region.Index;
            if (index >= 0 && index < tree.Tree.Count)
            {
                tree.Tree.SetSelectedIndex(index);
            }
        }

        protected void SyncRegionAfterRestore(int regionIndex, GeneTreeRegion tree)
        {
            if (Model.RegionIndex != regionIndex)
            {
                return;
            }
            ListModel region = Model.CurrentRegion;
            if (region != null)
            {
                SyncRegionFromTree(tree, region);
            }
        }
    }

    /// <summary>
    /// The per-region tree host <see cref="GeneDialogScopeBase"/> consumes twice (Selected,
    /// Library). Owns only the <see cref="TreeModel{T}"/> and pure navigation/search-expansion
    /// state; sound, announcement and ScreenModel sync stay in the owning scope.
    /// </summary>
    public sealed class GeneTreeRegion
    {
        public readonly TreeModel<InspectionTreeItem> Tree =
            new TreeModel<InspectionTreeItem>(new InspectionTreeItemShape());

        private readonly System.Action<InspectionTreeItem> onBeforeExpand;
        private readonly System.Func<InspectionTreeItem, bool> shouldAutoExpandForSearch;

        /// <summary>Snapshot of pre-search expansion state, non-null only while an auto-expansion is outstanding.</summary>
        private Dictionary<InspectionTreeItem, bool> searchExpansionSnapshot;

        public GeneTreeRegion(System.Action<InspectionTreeItem> onBeforeExpand,
            System.Func<InspectionTreeItem, bool> shouldAutoExpandForSearch)
        {
            this.onBeforeExpand = onBeforeExpand;
            this.shouldAutoExpandForSearch = shouldAutoExpandForSearch;
            Tree.OnBeforeExpand = onBeforeExpand;
        }

        public bool HasPendingSearchExpansion
        {
            get { return searchExpansionSnapshot != null; }
        }

        /// <summary>
        /// Installs a freshly built root while preserving expansion and the cursor's logical row:
        /// every rebuild swaps the whole root, so without this a gene toggle would collapse every
        /// category and drop the cursor to the top.
        /// </summary>
        public void SetRoot(InspectionTreeItem root, bool wrap, bool submenuMode)
        {
            ClearSearchExpansionState();
            TreeStatePreserve.SetRootPreservingState(
                Tree,
                root,
                delegate (InspectionTreeItem newRoot)
                {
                    Tree.Wrap = wrap;
                    Tree.SubmenuMode = submenuMode;
                    Tree.SetRoot(newRoot);
                },
                Tree.SelectedIndex,
                onBeforeExpand);
        }

        /// <summary>Ends the search-expansion window without restoring node states. Clearing the
        /// snapshot must always be paired with turning off
        /// <see cref="TreeModel{T}.SuppressSubmenuHiding"/>.</summary>
        private void ClearSearchExpansionState()
        {
            searchExpansionSnapshot = null;
            Tree.SuppressSubmenuHiding = false;
        }

        /// <summary>
        /// PageUp/PageDown section jump across IndentLevel==0 boundaries. Pure cursor move; the
        /// caller plays the sound and announces.
        /// </summary>
        public bool JumpToAdjacentSection(bool forward, bool wrap)
        {
            IReadOnlyList<InspectionTreeItem> visible = Tree.Visible;
            if (visible.Count == 0)
            {
                return false;
            }
            int start = Tree.SelectedIndex;
            int direction = forward ? 1 : -1;
            int current = start + direction;
            while (current >= 0 && current < visible.Count)
            {
                if (visible[current].IndentLevel == 0)
                {
                    Tree.SetSelectedIndex(current);
                    return true;
                }
                current += direction;
            }
            if (wrap)
            {
                current = forward ? 0 : visible.Count - 1;
                while (current != start)
                {
                    if (current >= 0 && current < visible.Count && visible[current].IndentLevel == 0)
                    {
                        Tree.SetSelectedIndex(current);
                        return true;
                    }
                    current += direction;
                    if (current < 0 || current >= visible.Count)
                    {
                        break;
                    }
                }
            }
            return false;
        }

        /// <summary>Applies an externally-changed category collapse map to this tree's top-level
        /// category nodes and lands on the anchor row if still visible, else its nearest visible
        /// ancestor. Category children are built eagerly, so flipping IsExpanded directly is
        /// safe.</summary>
        public void ApplyExternalCategoryCollapse(Dictionary<GeneCategoryDef, bool> collapsed,
            InspectionTreeItem anchor)
        {
            if (Tree.Root == null || collapsed == null)
            {
                return;
            }
            bool changed = false;
            for (int i = 0; i < Tree.Root.Children.Count; i++)
            {
                InspectionTreeItem node = Tree.Root.Children[i];
                var cat = node.Data as GeneCategoryDef;
                bool isCollapsed;
                // Uncategorized-genes headers carry null Data; vanilla's buttons only touch real defs.
                if (cat == null || !node.IsExpandable || !collapsed.TryGetValue(cat, out isCollapsed))
                {
                    continue;
                }
                bool wantExpanded = !isCollapsed;
                if (node.IsExpanded != wantExpanded)
                {
                    node.IsExpanded = wantExpanded;
                    changed = true;
                }
            }
            if (!changed)
            {
                return;
            }
            Tree.Reflatten();
            // Reflatten leaves SelectedIndex unclamped by contract; re-set it so a shrunken Visible
            // can't strand it out of range when no anchor lands.
            Tree.SetSelectedIndex(Tree.SelectedIndex);
            InspectionTreeItem land = anchor;
            while (land != null && Tree.IndexOf(land) < 0)
            {
                land = land.Parent;
            }
            if (land != null)
            {
                Tree.SetSelectedIndex(Tree.IndexOf(land));
            }
        }

        // Typeahead search-time auto-expansion. The restore is timed off RefreshContent because the
        // settle/clear hooks are not virtual. SuppressSubmenuHiding is required throughout:
        // SubmenuMode otherwise hides an expanded header behind its children, making any header
        // expanded during the search permanently unmatchable and unreachable by typeahead.

        public void EnsureSearchExpansion()
        {
            if (searchExpansionSnapshot != null || Tree.Root == null)
            {
                return;
            }
            var snapshot = new Dictionary<InspectionTreeItem, bool>();
            bool changed = SnapshotAndExpand(Tree.Root, snapshot);
            // Track the snapshot even when empty, and suspend submenu hiding for the whole window,
            // not only when SnapshotAndExpand changed something.
            searchExpansionSnapshot = snapshot;
            Tree.SuppressSubmenuHiding = true;
            if (changed || Tree.SubmenuMode)
            {
                Tree.Reflatten();
            }
        }

        private bool SnapshotAndExpand(InspectionTreeItem node, Dictionary<InspectionTreeItem, bool> snapshot)
        {
            bool changed = false;
            for (int i = 0; i < node.Children.Count; i++)
            {
                InspectionTreeItem child = node.Children[i];
                if (child.IsExpandable)
                {
                    snapshot[child] = child.IsExpanded;
                    if (!child.IsExpanded && shouldAutoExpandForSearch != null && shouldAutoExpandForSearch(child))
                    {
                        if (onBeforeExpand != null)
                        {
                            onBeforeExpand(child);
                        }
                        child.IsExpanded = true;
                        changed = true;
                    }
                }
                if (SnapshotAndExpand(child, snapshot))
                {
                    changed = true;
                }
            }
            return changed;
        }

        /// <param name="keepVisible">The row the player perceives the cursor on (the
        /// focused region's cursor row), whose ancestor path stays expanded through the
        /// restore; null (cursor elsewhere) runs the plain full restore.</param>
        public void RestoreSearchExpansionIfPending(InspectionTreeItem keepVisible)
        {
            Dictionary<InspectionTreeItem, bool> snapshot = searchExpansionSnapshot;
            searchExpansionSnapshot = null;
            // The search window is over either way, even if there is nothing below to restore.
            Tree.SuppressSubmenuHiding = false;
            if (snapshot == null)
            {
                return;
            }
            InspectionTreeItem current = keepVisible;
            foreach (KeyValuePair<InspectionTreeItem, bool> kv in snapshot)
            {
                kv.Key.IsExpanded = kv.Value;
            }
            // Keep the settled row reachable: re-expand its ancestor chain.
            for (InspectionTreeItem ancestor = current != null ? current.Parent : null;
                 ancestor != null;
                 ancestor = ancestor.Parent)
            {
                ancestor.IsExpanded = true;
            }
            Tree.Reflatten();
            int idx = current != null ? Tree.IndexOf(current) : -1;
            if (idx >= 0)
            {
                Tree.SetSelectedIndex(idx);
            }
        }
    }
}
