using RimWorld;
using Verse;
using Verse.Sound;

namespace RimWorldAccess.Shell
{
    /// <summary>
    /// The architect tree menu (Tab): a two-level category-to-designator treeview for picking
    /// a build/order/zone/area designator. The mod owns no window, so the scope rides the
    /// focus stack through <see cref="ArchitectTreeScopeMirror"/> over the windowless
    /// <see cref="ArchitectTreeState"/>, which holds only tree data, the activation callback,
    /// and <c>IsActive</c>.
    ///
    /// Two keys bypass the shared tree grammar: ']' opens the selected designator's
    /// right-click options, and Alt+I opens the info card for its <c>PlacingDef</c> rather
    /// than walking LinkedDef hyperlinks. Neither is a base ScreenScope chord, so the base's
    /// claims cannot shadow them. This tree declares no section boundaries, so Page Up/Down
    /// always play the reject click.
    ///
    /// The designator handoff is close-before-open:
    /// <see cref="ArchitectTreeState.CompleteDesignatorActivation"/> clears <c>IsActive</c>
    /// before invoking the placement callback, so the still-legacy placement side door never
    /// contends with a live scope. The mirror's pop lags that by a frame, which is what
    /// <see cref="ActivateContentItem"/> guards against.
    /// </summary>
    public sealed class ArchitectTreeScope : TreeRegionScope
    {
        public ArchitectTreeScope()
        {
            Claim("architectTree.designatorOptions", e => ArchitectMenuPatch.OpenDesignatorRightClickOptions());
            Claim("architectTree.infoCard", e => ArchitectMenuPatch.OpenDesignatorInfoCard());

            // TreeRegionScope deliberately leaves the shared tree.jumpTo*Section ids unclaimed,
            // so this screen claims them; with no section boundaries both reject.
            Claim("tree.jumpToPreviousSection", e => PerformJumpToAdjacentSection(false));
            Claim("tree.jumpToNextSection", e => PerformJumpToAdjacentSection(true));

            // The base ScreenScope's guarded Escape claim, registered first, clears an active
            // search; this one fires only once no search is active.
            Claim(SharedMenuGrammar.Cancel, e => PerformClose(), when: () => !TypeaheadHasActiveSearch);
        }

        public override string Name
        {
            get { return "architect-tree"; }
        }

        /// <summary>
        /// The real architect window sits on screen behind this tree; owning Cancel is what
        /// keeps its own <c>Window.OnCancelKeyPressed</c> pass from closing it independently,
        /// leaving this scope's close to take the window down with the tree.
        /// </summary>
        public override bool OwnsCancel
        {
            get { return true; }
        }

        /// <summary>Windowless: there is no owned window to capture buttons from.</summary>
        protected override bool IncludeActionsRegion
        {
            get { return false; }
        }

        protected override string TreeRegionLabel
        {
            get { return "RimWorldAccess.Building.ArchitectTree.RegionName".Translate(); }
        }

        /// <summary>
        /// Typeahead reaches every designator without pressing '*' first.
        /// </summary>
        protected override bool ShouldAutoExpandForSearch(InspectionTreeItem item)
        {
            return true;
        }

        /// <summary>
        /// A build row ranks by its def's own label, because vanilla rewrites the displayed
        /// one the first time the player opens the row's material menu ("Wall..." becomes
        /// "Wooden wall" for the session) and never clears it. Narrowed to
        /// <see cref="Designator_Build"/> deliberately: it is the type whose label moves, and
        /// the only <see cref="Designator_Place"/> whose <c>PlacingDef</c> is safe to read off
        /// the selection — <c>Designator_Install.PlacingDef</c> throws when nothing is selected.
        /// </summary>
        protected override string TreeNodeSearchIdentity(InspectionTreeItem item)
        {
            return item.Data is Designator_Build build && build.PlacingDef != null
                ? build.PlacingDef.label
                : null;
        }

        protected override ElementDescription DescribeTreeNode(InspectionTreeItem item)
        {
            var d = new ElementDescription();
            d.Label = item.Label;
            if (item.Type == InspectionTreeItem.ItemType.Category)
            {
                d.Role = ElementRole.TreeItem;
                d.Expanded = item.IsExpanded;
            }
            // Designator rows carry no role: their label already folds cost, skill, description
            // and the ']' hint.
            return d;
        }

        /// <summary>
        /// Enter on a tree row: categories toggle expand/collapse, a designator hands off to
        /// the placement callback and empties the tree behind it.
        /// </summary>
        protected override void ActivateTreeNode(InspectionTreeItem item)
        {
            if (item.Type == InspectionTreeItem.ItemType.Category)
            {
                PerformActivateExpandToggle(item);
                return;
            }

            if (item.Data is Designator designator && ArchitectTreeState.HasPendingActivation)
            {
                ClearTree();
                ArchitectTreeState.CompleteDesignatorActivation(designator);
                return;
            }

            // A non-expandable node with no pending callback has nothing to toggle.
        }

        /// <summary>
        /// The designator handoff clears <see cref="ArchitectTreeState.IsActive"/> and empties
        /// the tree before the placement callback runs, but the mirror only pops this scope on
        /// the next reconcile — a key arriving in that window must not reach the emptied model.
        /// </summary>
        protected override void ActivateContentItem(int region, int index)
        {
            if (!ArchitectTreeState.IsActive)
            {
                return;
            }
            base.ActivateContentItem(region, index);
        }

        private void PerformClose()
        {
            ArchitectTreeState.Close();
            ArchitectState.Reset();
            TolkHelper.Speak("RimWorldAccess.Building.Architect.MenuClosed".Loc());
        }

        // Bridge targets wired once by ArchitectTreeScopeMirror's static constructor, so
        // ArchitectTreeState drives the tree synchronously whether or not the scope is pushed.

        /// <summary>
        /// Bridge target for <see cref="ArchitectTreeState.Open"/>: takes the freshly-built
        /// root and returns the visible row count. Deliberately silent — the mirror pushes the
        /// scope the same frame and the push's focus announcement already speaks the row.
        /// </summary>
        public int LoadTree(InspectionTreeItem root)
        {
            // Clear the search with the tree so none leaks into the next open.
            TypeaheadReset();
            SetTreeRoot(root);
            RefreshModel();
            SyncRegionFromCurrentTree();
            return Tree.Count;
        }

        /// <summary>
        /// Bridge target for <see cref="ArchitectTreeState.Close"/> and the designator
        /// handoff's teardown; drops search and tree together.
        /// </summary>
        public void ClearTree()
        {
            TypeaheadReset();
            ResetTree();
        }

        /// <summary>Bridge target for <see cref="ArchitectTreeState.GetSelectedDesignator"/>.</summary>
        public InspectionTreeItem SelectedTreeItem()
        {
            RefreshModel();
            return CurrentTreeItem();
        }
    }

    /// <summary>
    /// Keeps <see cref="ArchitectTreeScope"/> in lockstep with
    /// <see cref="ArchitectTreeState.IsActive"/>, standing down while an info card is open,
    /// and carries the per-pass stale-tree cleanup for a player who opened the architect menu
    /// and then switched to world view without closing it. Order relative to the other mirrors
    /// never matters: the sole opener never nests this screen inside another menu, and the
    /// tree-to-placement handoff always closes this scope first.
    /// </summary>
    internal static class ArchitectTreeScopeMirror
    {
        private static readonly ArchitectTreeScope scope = new ArchitectTreeScope();

        static ArchitectTreeScopeMirror()
        {
            ArchitectTreeState.LoadTreeCallback = scope.LoadTree;
            ArchitectTreeState.ClearTreeCallback = scope.ClearTree;
            ArchitectTreeState.SelectedItemCallback = scope.SelectedTreeItem;
        }

        public static void Reconcile()
        {
            if (ArchitectTreeState.IsActive && WorldNavigationState.IsActive)
            {
                ArchitectTreeState.Close();
                ArchitectState.Reset();
            }

            if (ArchitectTreeState.IsActive && !InfoCardState.IsActive)
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
