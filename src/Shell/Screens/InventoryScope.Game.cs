using System;
using System.Collections.Generic;
using RimWorld;
using Verse;
using Verse.Sound;

namespace RimWorldAccess.Shell
{
    /// <summary>
    /// Keyboard screen scope for the windowless colony-wide inventory menu (the 'I'
    /// category/item/stack tree): an aggregation of every stored and pawn-carried item on the
    /// map. The mod owns no window here — the surface is the windowless
    /// <see cref="WindowlessInventoryState"/> data facade — so the scope rides the focus stack
    /// through <see cref="InventoryScopeMirror"/>. It is modal, so the shell backstop swallows
    /// any key it does not claim; the 'I' that OPENS the menu stays on its legacy handler and
    /// falls through the shell while the menu is closed.
    ///
    /// <b>Instance reach.</b> <see cref="Live"/> is set in the constructor and never cleared:
    /// the mirror pops this scope whenever an info card opens above the menu, and the state must
    /// still hand over a rebuilt tree or draw its highlight while that is true. The tree data
    /// likewise survives a pop/re-push, so the user's row is restored when the card or context
    /// menu closes — <see cref="OnPop"/> does not reset it, only the state's Close does.
    ///
    /// '*' is deliberately NOT the shared tree sibling-expand: it expands every Category node in
    /// the whole tree instead, so item-level rows stay tucked away.
    /// <c>tree.expandAllSiblings</c> is therefore not claimed.
    ///
    /// The context menu (']' or Enter on a stack) opens a windowless float menu over this
    /// screen; the dispatcher's blanket LegacyKeyboardOverlayActive stand-down hands it the
    /// keyboard.
    /// </summary>
    public sealed class InventoryScope : TreeRegionScope
    {
        public InventoryScope()
        {
            // One instance for the session, reachable even while the mirror has this scope
            // popped for an info card above the menu — see the class remarks.
            Live = this;

            // End-key/collapse "return to last visited child" memory.
            Tree.TrackLastChild = true;

            // TreeRegionScope's constructor deliberately leaves Page Up/Down unclaimed, so this
            // screen claims them. The tree never sets IsSectionBoundary, so both always reject.
            Claim("tree.jumpToPreviousSection", e => PerformJumpToAdjacentSection(false));
            Claim("tree.jumpToNextSection", e => PerformJumpToAdjacentSection(true));

            // '*': whole-tree category expand — see the class remarks.
            Claim("inventory.expandAll", e => PerformExpandAllCategories());

            Claim("inventory.announce", e => AnnounceCurrentItem());
            Claim("inventory.contextMenu", e => WindowlessInventoryState.OpenContextMenuFor(CurrentTreeItem()));
            Claim("inventory.jumpTo", e => WindowlessInventoryState.JumpToNode(CurrentTreeItem()));
            Claim("inventory.delete", e => WindowlessInventoryState.DeleteNode(CurrentTreeItem()));
            Claim(SharedMenuGrammar.Info, e => PerformInfoCard());

            // The base's guarded claim, registered first, clears an active search and restores
            // the pre-search expansion; this one fires only once no search is active.
            Claim(SharedMenuGrammar.Cancel, e => WindowlessInventoryState.Close(),
                when: () => !TypeaheadHasActiveSearch);

            twinMount = new InventoryTwinMount(this, () => TreeRegionLabel);
        }

        /// <summary>The session's single scope instance — see the class remarks on instance reach.</summary>
        internal static InventoryScope Live { get; private set; }

        /// <summary>The visible body: the tree twin draws this screen's rows while it is the top scope.</summary>
        private readonly ITreeTwinMount twinMount;

        public override string Name
        {
            get { return "inventory"; }
        }

        /// <summary>Windowless: no vanilla window will ever close this menu, so Escape is this scope's own claim.</summary>
        public override bool OwnsCancel
        {
            get { return true; }
        }

        /// <summary>The menu draws no window of its own, so there are no buttons to capture.</summary>
        protected override bool IncludeActionsRegion
        {
            get { return false; }
        }

        protected override string TreeRegionLabel
        {
            get { return "RimWorldAccess.Inspection.Inventory.RegionName".Translate(); }
        }

        public override void OnPush()
        {
            base.OnPush();
            // Safety net: if the state built a tree while no scope existed to receive it, seed now.
            WindowlessInventoryState.NotifyScopeAttached(this);
            TreeTwinWindow.Mount(twinMount);
        }

        public override void OnPop()
        {
            base.OnPop();
            TreeTwinWindow.Unmount(twinMount);
        }

        /// <summary>
        /// Presents a freshly built tree. The opening summary and the tab sound are the
        /// state's own (they precede this call); <paramref name="announceRow"/> is false for
        /// the empty-inventory open, whose summary is the whole announcement.
        /// </summary>
        internal void OpenTree(InspectionTreeItem root, bool announceRow)
        {
            // Runs before the mirror's push, so the coming focus must stand down or the opening
            // row doubles; the not-top gate lets a drill-in refocus still re-announce.
            if (!ReferenceEquals(FocusStack.Top, this))
            {
                SuppressNextEntryAnnouncement();
            }
            SetTreeRoot(root);
            RefreshModel();
            SyncRegionFromCurrentTree();
            if (announceRow && Tree.Count > 0)
            {
                AnnounceCurrentItem();
            }
        }

        /// <summary>Adopts a tree the state already holds, silently (a re-push after the tree drifted).</summary>
        internal void EnsureTree(InspectionTreeItem root)
        {
            if (ReferenceEquals(Tree.Root, root))
            {
                return;
            }
            SetTreeRoot(root);
            RefreshModel();
            SyncRegionFromCurrentTree();
        }

        /// <summary>Drops the tree when the menu closes.</summary>
        internal void ClearTree()
        {
            ResetTree();
        }

        /// <summary>
        /// Swaps in a rebuilt tree after an item action, restoring the cursor onto the row with
        /// the same label and otherwise clamping the old index. Silent: the action speaks for itself.
        /// </summary>
        internal void ReplaceTree(InspectionTreeItem root)
        {
            SyncTreeFromRegion();
            string oldLabel = Tree.SelectedItem != null ? Tree.SelectedItem.Label : null;
            int oldIndex = Tree.SelectedIndex;

            SetTreeRoot(root);

            int target = -1;
            if (!string.IsNullOrEmpty(oldLabel))
            {
                for (int i = 0; i < Tree.Count; i++)
                {
                    if (Tree.Visible[i].Label == oldLabel)
                    {
                        target = i;
                        break;
                    }
                }
            }
            if (target < 0)
            {
                target = oldIndex < Tree.Count ? oldIndex : Tree.Count - 1;
            }
            Tree.SetSelectedIndex(target);

            RefreshModel();
            SyncRegionFromCurrentTree();
        }

        /// <summary>
        /// Pulls the tree's own cursor up to the region cursor. Plain Up/Down move the region
        /// model only, so anything driven from OUTSIDE the key handlers must re-anchor before
        /// reading Tree.SelectedItem or it drags the cursor back to whichever row last went
        /// through an expand/collapse.
        /// </summary>
        private void SyncTreeFromRegion()
        {
            ListModel region = Model.CurrentRegion;
            if (region == null || region.IsEmpty)
            {
                return;
            }
            int treeIndex = region.Index - PrefixRowCount;
            if (treeIndex >= 0 && treeIndex < Tree.Count)
            {
                Tree.SetSelectedIndex(treeIndex);
            }
        }

        /// <summary>
        /// The focused row and its flat index, for the sighted-user highlight the state draws.
        /// Region-derived rather than read off <c>Tree.SelectedIndex</c>, which plain Up/Down
        /// do not move (see <see cref="SyncTreeFromRegion"/>).
        /// </summary>
        internal bool TryGetFocusedRow(out InspectionTreeItem item, out int index)
        {
            item = null;
            index = 0;
            ListModel region = Model.CurrentRegion;
            if (region == null || region.IsEmpty)
            {
                return false;
            }
            int treeIndex = region.Index - PrefixRowCount;
            if (treeIndex < 0 || treeIndex >= Tree.Count)
            {
                return false;
            }
            item = Tree.Visible[treeIndex];
            index = treeIndex;
            return true;
        }

        /// <summary>
        /// The state's own builders fully compose the node labels (counts, carried-by
        /// parentheticals, quality, location), so they pass through untouched — no tag stripping,
        /// no punctuation trimming. Expansion rides the structured field; position and level are
        /// the base's.
        /// </summary>
        protected override ElementDescription DescribeTreeNode(InspectionTreeItem item)
        {
            ElementDescription d = new ElementDescription();
            d.Label = item.Label;
            if (item.IsExpandable)
            {
                d.Role = ElementRole.TreeItem;
                d.Expanded = item.IsExpanded;
            }
            return d;
        }

        /// <summary>
        /// Search behaves as if '*' had been pressed first: only Category nodes auto-expand,
        /// so item-level matches across the whole tree are reachable while the item groups'
        /// own stack children stay folded away.
        /// </summary>
        protected override bool ShouldAutoExpandForSearch(InspectionTreeItem item)
        {
            return IsCategoryNode(item);
        }

        /// <summary>
        /// '*': expands every Category node in the whole tree, with this screen's own wording.
        /// Deliberately not <see cref="TreeRegionScope"/>'s sibling expand. An active search is
        /// left alone.
        /// </summary>
        private void PerformExpandAllCategories()
        {
            if (Tree.Root == null || Tree.Root.Children.Count == 0)
            {
                TolkHelper.Speak("RimWorldAccess.Inspection.Inventory.Expand.NoneToExpand".Loc());
                return;
            }

            SyncTreeFromRegion();
            int expandedCount = ExpandCategoriesRecursively(Tree.Root.Children);
            if (expandedCount == 0)
            {
                TolkHelper.Speak("RimWorldAccess.Inspection.Inventory.Expand.AllAlreadyExpanded".Loc());
                return;
            }

            Tree.Reflatten();
            RefreshModel();
            EmbeddedAudioHelper.PlaySoundDefWithReverb(SoundDefOf.FloatMenu_Open);
            TolkHelper.Speak((expandedCount == 1
                ? "RimWorldAccess.Tree.ExpandedCountOne"
                : "RimWorldAccess.Tree.ExpandedCountMany").Loc(expandedCount));
        }

        /// <summary>
        /// Expands Category nodes only, recursing through Category children alone — no other
        /// node type can contain a category.
        /// </summary>
        private static int ExpandCategoriesRecursively(List<InspectionTreeItem> nodes)
        {
            int expandedCount = 0;
            for (int i = 0; i < nodes.Count; i++)
            {
                InspectionTreeItem node = nodes[i];
                if (!IsCategoryNode(node))
                {
                    continue;
                }
                if (node.IsExpandable && !node.IsExpanded)
                {
                    node.IsExpanded = true;
                    expandedCount++;
                }
                if (node.Children.Count > 0)
                {
                    expandedCount += ExpandCategoriesRecursively(node.Children);
                }
            }
            return expandedCount;
        }

        private static bool IsCategoryNode(InspectionTreeItem item)
        {
            var data = item.Data as WindowlessInventoryState.InventoryNodeData;
            return data != null && data.Type == WindowlessInventoryState.NodeType.Category;
        }

        /// <summary>
        /// Enter on a tree row: a Stack opens its context menu, anything else expandable toggles
        /// (the one place Enter also collapses), and a leaf is a silent no-op.
        /// </summary>
        protected override void ActivateTreeNode(InspectionTreeItem item)
        {
            var data = item.Data as WindowlessInventoryState.InventoryNodeData;
            if (data != null && data.Type == WindowlessInventoryState.NodeType.Stack)
            {
                WindowlessInventoryState.OpenContextMenuFor(item);
                return;
            }

            if (item.IsExpandable)
            {
                PerformActivateExpandToggle(item);
            }
        }

        /// <summary>
        /// Alt+I: the info card for the focused item's own def, else for the nearest ancestor
        /// carrying a linked def (never the root), else the spoken "no info card" fallback.
        /// </summary>
        private void PerformInfoCard()
        {
            InspectionTreeItem item = CurrentTreeItem();
            if (item != null)
            {
                if (WindowlessInventoryState.TryOpenInfoCardFor(item))
                {
                    return;
                }
                if (item.LinkedDef != null)
                {
                    InfoCardState.OpenInfoCardForDef(item.LinkedDef);
                    return;
                }
                for (InspectionTreeItem parent = item.Parent;
                     parent != null && !ReferenceEquals(parent, Tree.Root);
                     parent = parent.Parent)
                {
                    if (parent.LinkedDef != null)
                    {
                        InfoCardState.OpenInfoCardForDef(parent.LinkedDef);
                        return;
                    }
                }
            }
            InfoCardState.SpeakNoInfoCardAvailable();
        }
    }

    /// <summary>
    /// The inventory's rows for <see cref="TreeTwinWindow"/>: the shared tree mount plus
    /// the icon and the count each node's own <see cref="WindowlessInventoryState.InventoryNodeData"/>
    /// already carries. Nothing is read back out of the composed row labels.
    /// </summary>
    internal sealed class InventoryTwinMount : TreeRegionTwinMount
    {
        internal InventoryTwinMount(InventoryScope scope, Func<string> title)
            : base(scope, title)
        {
        }

        protected override void Decorate(InspectionTreeItem item, ref TreeTwinRow row)
        {
            var data = item.Data as WindowlessInventoryState.InventoryNodeData;
            if (data == null)
            {
                return;
            }
            switch (data.Type)
            {
                case WindowlessInventoryState.NodeType.Category:
                    if (data.CategoryData != null)
                    {
                        row.RightText = data.CategoryData.TotalItemCount.ToStringCached();
                    }
                    break;
                case WindowlessInventoryState.NodeType.DefGroup:
                    row.Icon = data.DefGroupDef;
                    row.RightText = TotalOf(data.DefGroupItems);
                    break;
                case WindowlessInventoryState.NodeType.MaterialGroup:
                    row.Icon = data.DefGroupDef;
                    row.IconStuff = data.DefGroupStuff;
                    // A material group stores only def and stuff, so its total is the sum of the
                    // stack rows hanging off it.
                    row.RightText = TotalOfStackChildren(item);
                    break;
                case WindowlessInventoryState.NodeType.ItemGroup:
                    if (data.ItemData != null)
                    {
                        row.Icon = data.ItemData.Def;
                        row.IconStuff = data.ItemData.Stuff;
                        row.RightText = data.ItemData.TotalQuantity.ToStringCached();
                    }
                    break;
                case WindowlessInventoryState.NodeType.Stack:
                    if (data.ItemData != null)
                    {
                        row.Icon = data.ItemData.Def;
                        row.IconStuff = data.ItemData.Stuff;
                    }
                    if (data.StackData != null)
                    {
                        row.RightText = data.StackData.Quantity.ToStringCached();
                    }
                    break;
            }
        }

        private static string TotalOf(List<InventoryHelper.InventoryItem> items)
        {
            if (items == null)
            {
                return null;
            }
            int total = 0;
            for (int i = 0; i < items.Count; i++)
            {
                total += items[i].TotalQuantity;
            }
            return total.ToStringCached();
        }

        private static string TotalOfStackChildren(InspectionTreeItem parent)
        {
            int total = 0;
            for (int i = 0; i < parent.Children.Count; i++)
            {
                var child = parent.Children[i].Data as WindowlessInventoryState.InventoryNodeData;
                if (child != null && child.StackData != null)
                {
                    total += child.StackData.Quantity;
                }
            }
            return total.ToStringCached();
        }
    }

    /// <summary>
    /// Keeps <see cref="InventoryScope"/> in lockstep with
    /// <see cref="WindowlessInventoryState.IsActive"/>, reconciled every OnGUI pass and standing
    /// down while an info card is open over the menu. The inventory never coexists with the
    /// bills/storage scope chains (each side's modal swallow blocks the other's opener), so no
    /// sibling gates are needed. The scope keeps its tree across a pop/re-push here.
    /// </summary>
    internal static class InventoryScopeMirror
    {
        private static readonly InventoryScope scope = new InventoryScope();

        public static void Reconcile()
        {
            if (WindowlessInventoryState.IsActive && !InfoCardState.IsActive)
            {
                FocusStack.Push(scope);
            }
            else
            {
                FocusStack.Pop(scope);
            }
        }
    }
}
