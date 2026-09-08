using System.Collections.Generic;
using RimWorld;
using Verse;
using Verse.Sound;

namespace RimWorldAccess.Shell
{
    /// <summary>
    /// Keyboard scope for the real <see cref="Verse.Dialog_InfoCard"/> window, attached through
    /// <see cref="ScopeForWindow"/> rather than a windowless-state mirror. Cursor, search and
    /// announcement composition live here or in the base; <see cref="InfoCardState"/> keeps the card
    /// DATA — tree root, nested-card stack, float-menu ownership — and the statics other files call.
    ///
    /// Nested cards each get their OWN scope instance, since ScopeForWindow is instance-keyed per
    /// window, while InfoCardState's tree operations are static. They reach the right instance
    /// through <see cref="Live"/>, so a nested close restores the OUTER card's tree rather than
    /// whichever scope was pushed last. <see cref="InfoCardState.Open"/> runs from the Window.PostOpen
    /// patch, which vanilla calls as the last line of WindowStack.Add and therefore BEFORE the Add
    /// postfix attaches this scope, so the state writes the tree root to its own field first and
    /// <see cref="OnPush"/> seeds from there.
    ///
    /// The card's flat stat lists convey their category ONLY through
    /// <see cref="InspectionTreeItem.Description"/>, spoken as a leading section fragment when it
    /// differs from the last announced one. That gate is stateful, and the typeahead haystack calls
    /// <see cref="DescribeContentItem"/> once per visible row, so two rules keep it safe: the gate is
    /// evaluated ONLY for the row the region cursor is on, and <see cref="describingSearchSpace"/>
    /// suppresses the spoken prefix — but not the state commit — while a search space is built.
    /// </summary>
    public sealed class InfoCardScope : TreeRegionScope
    {
        /// <summary>Live scope per open card, so <see cref="InfoCardState"/>'s statics can reach the right instance while cards are nested.</summary>
        private static readonly Dictionary<Verse.Dialog_InfoCard, InfoCardScope> scopes =
            new Dictionary<Verse.Dialog_InfoCard, InfoCardScope>();

        private readonly Verse.Dialog_InfoCard dialog;

        /// <summary>Section name of the last row announced, for the leading section prefix (see the class remarks).</summary>
        private string lastAnnouncedSection;

        /// <summary>True while the typeahead search space is being built, so describes commit the section without speaking it.</summary>
        private bool describingSearchSpace;

        public InfoCardScope(Verse.Dialog_InfoCard dialog)
        {
            this.dialog = dialog;

            // The card's own section jumps, not TreeRegionScope's IsSectionBoundary walk: a section
            // here is a string field on the node, and the backward jump lands on the FIRST row of the
            // previous section rather than the nearest boundary.
            Claim("infoCard.jumpToNextCategory", e => PerformJumpToCategory(true));
            Claim("infoCard.jumpToPreviousCategory", e => PerformJumpToCategory(false));

            Claim("infoCard.delete", e => PerformDelete());
            Claim(SharedMenuGrammar.Info, e => PerformInfoCard());

            // The base's guarded claim, registered first, clears an active search; this fires only
            // once none is active. The card ships closeOnCancel = false, so vanilla never closes it.
            Claim(SharedMenuGrammar.Cancel, e => PerformCancel(), when: () => !TypeaheadHasActiveSearch);
        }

        /// <summary>The scope driving the card <see cref="InfoCardState"/> currently tracks, or null before its Add postfix attached one.</summary>
        internal static InfoCardScope Live
        {
            get
            {
                Verse.Dialog_InfoCard current = InfoCardState.CurrentDialog;
                InfoCardScope scope;
                if (current != null && scopes.TryGetValue(current, out scope))
                {
                    return scope;
                }
                return null;
            }
        }

        public override string Name
        {
            get { return "info-card"; }
        }

        /// <summary>The card sets closeOnCancel = false so this scope owns Escape: clear a search, then close.</summary>
        public override bool OwnsCancel
        {
            get { return true; }
        }

        public bool Owns(Verse.Window window)
        {
            return ReferenceEquals(window, dialog);
        }

        protected override string TreeRegionLabel
        {
            get
            {
                InspectionTreeItem root = Tree.Root;
                if (root != null && !string.IsNullOrEmpty(root.Label))
                {
                    return root.Label.StripTags();
                }
                return "RimWorldAccess.Inspection.InfoCard.RegionName".Translate();
            }
        }

        public override void OnPush()
        {
            base.OnPush();
            scopes[dialog] = this;
            LoadTree(InfoCardState.CurrentTreeRoot, InfoCardState.CurrentTreeIndex);
            // On a fresh open the tree seeded above is premature — its Stats tab still carries the
            // placeholder until vanilla's first render pass populates the cached draw entries — so
            // the base entry announcement must not read it. Nested-card restores arrive by refocus
            // rather than push and still rely on that announcement, hence OnPush and not OnFocus.
            SuppressNextEntryAnnouncement();
        }

        public override void OnPop()
        {
            base.OnPop();
            InfoCardScope attached;
            if (scopes.TryGetValue(dialog, out attached) && ReferenceEquals(attached, this))
            {
                scopes.Remove(dialog);
            }
            ResetTree();
            lastAnnouncedSection = null;
        }

        /// <summary>
        /// Seeds the tree from the state's current root. The region cursor must be moved onto the
        /// seeded index explicitly: restoring an outer card lands deep in its tree, and the region
        /// model does not track the tree's own selection.
        /// </summary>
        internal void LoadTree(InspectionTreeItem root, int selectedIndex)
        {
            SetTreeRoot(root, selectedIndex);
            lastAnnouncedSection = null;
            RefreshModel();
            SyncRegionFromCurrentTree();
        }

        /// <summary>The card's current tree root, for the nested-card save stack.</summary>
        internal InspectionTreeItem TreeRoot
        {
            get { return Tree.Root; }
        }

        /// <summary>
        /// The current cursor index, for the nested-card save stack. Derived from the region cursor,
        /// since plain Up/Down moves only that and Tree.SelectedIndex can be stale.
        /// </summary>
        internal int TreeSelectedIndex
        {
            get
            {
                ListModel current = Model.CurrentRegion;
                if (current != null && !current.IsEmpty)
                {
                    int treeIndex = current.Index - PrefixRowCount;
                    if (treeIndex >= 0 && treeIndex < Tree.Count)
                    {
                        return treeIndex;
                    }
                }
                return Tree.SelectedIndex;
            }
        }

        /// <summary>
        /// The row the region cursor rests on, as a PURE READ: the visual driver runs from the card's
        /// draw pass and must never move the tree's selection anchor, which expand/collapse owns.
        /// </summary>
        internal InspectionTreeItem FocusedRow()
        {
            ListModel current = Model.CurrentRegion;
            if (current != null && !current.IsEmpty)
            {
                int treeIndex = current.Index - PrefixRowCount;
                if (treeIndex >= 0 && treeIndex < Tree.Count)
                {
                    return Tree.Visible[treeIndex];
                }
            }
            return null;
        }

        /// <summary>Re-speak the focused row (nested-card restore, permit rebuild).</summary>
        internal void AnnounceCurrentRow()
        {
            AnnounceCurrentItem();
        }

        /// <summary>
        /// Moves the region cursor onto <paramref name="tabNode"/> silently, for a mouse click on the
        /// vanilla tab strip: the generic tab-strip capture already announced that click.
        /// </summary>
        internal void SyncCursorToTabNode(InspectionTreeItem tabNode)
        {
            if (tabNode == null)
            {
                return;
            }
            IReadOnlyList<InspectionTreeItem> visible = Tree.Visible;
            for (int i = 0; i < visible.Count; i++)
            {
                if (ReferenceEquals(visible[i], tabNode))
                {
                    Tree.SetSelectedIndex(i);
                    SyncRegionFromCurrentTree();
                    return;
                }
            }
        }

        /// <summary>
        /// Re-flattens after an outside structural change to the tree, clamps the cursor and
        /// re-announces.
        /// </summary>
        internal void RefreshTreeAndAnnounce()
        {
            // Re-anchor from the region cursor first: Tree.SelectedIndex can be stale, and the sync
            // below would otherwise drag the cursor back to it.
            ListModel current = Model.CurrentRegion;
            if (current != null && !current.IsEmpty)
            {
                int treeIndex = current.Index - PrefixRowCount;
                if (treeIndex >= 0 && treeIndex < Tree.Count)
                {
                    Tree.SetSelectedIndex(treeIndex);
                }
            }
            Tree.Reflatten();
            if (Tree.Count == 0)
            {
                Tree.SetSelectedIndex(0);
                RefreshModel();
                return;
            }
            if (Tree.SelectedIndex >= Tree.Count)
            {
                Tree.SetSelectedIndex(Tree.Count - 1);
            }
            RefreshModel();
            SyncRegionFromCurrentTree();
            AnnounceCurrentItem();
        }

        protected override ElementDescription DescribeTreeNode(InspectionTreeItem item)
        {
            ElementDescription d = new ElementDescription();

            // The short form once expanded, since the collapsed label carries the whole explanation.
            string rawLabel = item.IsExpandable && item.IsExpanded && !string.IsNullOrEmpty(item.ExpandedLabel)
                ? item.ExpandedLabel
                : item.Label;
            d.Label = rawLabel.StripTags().TrimEnd('.', '!', '?');

            if (item.IsExpandable)
            {
                d.Role = ElementRole.TreeItem;
                d.Expanded = item.IsExpanded;
            }

            // Direct hyperlinks only (no parent walk), matching vanilla's own stat rows.
            if (InfoCardState.HasInspectableHyperlink(item))
            {
                d.Extras = "RimWorldAccess.InfoCard.Inspectable".Translate();
            }

            return d;
        }

        /// <summary>
        /// Adds the leading section fragment on the cursor's row when its section differs from the
        /// last announced one; evaluated only for that row, so the haystack sweep stays a pure read.
        /// </summary>
        protected override ElementDescription DescribeContentItem(int region, int index)
        {
            ElementDescription d = base.DescribeContentItem(region, index);
            if (!IsCursorRow(region, index))
            {
                return d;
            }
            InspectionTreeItem item = CurrentTreeItem();
            string section = item == null ? null : item.Description;
            if (string.IsNullOrEmpty(section))
            {
                lastAnnouncedSection = null;
                return d;
            }
            bool changed = section != lastAnnouncedSection;
            lastAnnouncedSection = section;
            // Search announcements never carry the prefix; the state commit above keeps the gate
            // honest for the first plain announcement after the search settles.
            if (changed && !describingSearchSpace && !TypeaheadHasActiveSearch)
            {
                d.Label = "RimWorldAccess.Inspection.InfoCard.SectionPrefix".Translate(section, d.Label);
            }
            return d;
        }

        private bool IsCursorRow(int region, int index)
        {
            if (region != Model.RegionIndex)
            {
                return false;
            }
            ListModel current = Model.CurrentRegion;
            return current != null && !current.IsEmpty && current.Index == index;
        }

        /// <summary>
        /// Brackets the search-space build so a typed character neither speaks a section prefix into
        /// a haystack label nor loses the state commit.
        /// </summary>
        public override bool HandleChar(char c)
        {
            describingSearchSpace = true;
            try
            {
                return base.HandleChar(c);
            }
            finally
            {
                describingSearchSpace = false;
            }
        }

        protected override void ActivateTreeNode(InspectionTreeItem item)
        {
            if (item.IsExpandable)
            {
                if (item.IsExpanded)
                {
                    SoundDefOf.ClickReject.PlayOneShotOnCamera();
                    TolkHelper.Speak("RimWorldAccess.Inspection.InfoCard.AlreadyExpanded".Loc());
                    return;
                }
                PerformExpandNode(item);
                return;
            }

            if (item.OnActivate != null)
            {
                item.OnActivate();
                SoundDefOf.Click.PlayOneShotOnCamera();
                return;
            }

            SoundDefOf.ClickReject.PlayOneShotOnCamera();
            TolkHelper.Speak("RimWorldAccess.Inspection.InfoCard.NoAction".Loc());
        }

        /// <summary>
        /// Left is the base's plain collapse/drill-up; Right carries the card's own
        /// lazy-load empty correction, so it routes through <see cref="PerformExpandNode"/>.
        /// </summary>
        protected override void AdjustContentItem(int region, int index, int direction)
        {
            int treeIndex = index - PrefixRowCount;
            if (direction < 0 || treeIndex < 0 || treeIndex >= Tree.Count)
            {
                base.AdjustContentItem(region, index, direction);
                return;
            }
            TypeaheadReset();
            Tree.SetSelectedIndex(treeIndex);
            PerformExpandNode(Tree.Visible[treeIndex]);
        }

        /// <summary>
        /// Expands with the lazy-load empty correction: a node the builder marked expandable can turn
        /// out to have no content once its children are built, and is then corrected to
        /// non-expandable and re-announced instead of expanding into nothing.
        /// </summary>
        private void PerformExpandNode(InspectionTreeItem item)
        {
            if (!item.IsExpandable)
            {
                SoundDefOf.ClickReject.PlayOneShotOnCamera();
                TolkHelper.Speak("RimWorldAccess.Inspection.InfoCard.CannotExpand".Loc(), SpeechPriority.High);
                return;
            }

            if (item.IsExpanded)
            {
                // The user chose to open this node, so the row they land on must not re-announce
                // the section.
                if (item.Children.Count > 0)
                {
                    lastAnnouncedSection = item.Children[0].Description;
                }
                ExpandThroughBase();
                return;
            }

            OnBeforeExpandNode(item);
            if (item.Children.Count == 0)
            {
                item.IsExpandable = false;
                SoundDefOf.Click.PlayOneShotOnCamera();
                AnnounceCurrentItem();
                return;
            }

            // Submenu mode replaces the parent row with its children, so the same
            // section suppression applies to the child the cursor lands on.
            if (Tree.SubmenuMode)
            {
                lastAnnouncedSection = item.Children[0].Description;
            }
            ExpandThroughBase();
        }

        private void ExpandThroughBase()
        {
            ListModel region = Model.CurrentRegion;
            if (region == null || region.IsEmpty)
            {
                return;
            }
            base.AdjustContentItem(Model.RegionIndex, region.Index, 1);
        }

        protected override void OnBeforeExpandNode(InspectionTreeItem item)
        {
            if (item.OnActivate != null && item.Children.Count == 0)
            {
                item.OnActivate();
            }
        }

        /// <summary>
        /// Forward lands on the next row whose section differs from the current one; backward lands
        /// on the FIRST row of the previous section. Both wrap only when WrapNavigation allows it.
        /// </summary>
        private void PerformJumpToCategory(bool forward)
        {
            TypeaheadReset();
            RefreshModel();
            ListModel region = Model.CurrentRegion;
            if (region == null || region.IsEmpty)
            {
                return;
            }
            int treeIndex = region.Index - PrefixRowCount;
            if (treeIndex < 0 || treeIndex >= Tree.Count)
            {
                SoundDefOf.ClickReject.PlayOneShotOnCamera();
                return;
            }

            IReadOnlyList<InspectionTreeItem> visible = Tree.Visible;
            bool wrap = RimWorldAccessMod_Settings.Settings != null
                && RimWorldAccessMod_Settings.Settings.WrapNavigation;
            int target = SectionNavigation.FindAdjacentSectionStart(
                visible.Count, treeIndex, i => SectionOf(visible[i]), forward, wrap);
            if (target < 0)
            {
                SoundDefOf.ClickReject.PlayOneShotOnCamera();
                return;
            }
            if (forward ? target < treeIndex : target > treeIndex)
                MenuHelper.PlayWrapTone();
            LandOnCategory(target);
        }

        private static string SectionOf(InspectionTreeItem item)
        {
            return item.Description ?? "";
        }

        private void LandOnCategory(int treeIndex)
        {
            Tree.SetSelectedIndex(treeIndex);
            SyncRegionFromCurrentTree();
            SoundDefOf.Tick_Tiny.PlayOneShotOnCamera();
            AnnounceCurrentItem();
        }

        private void PerformDelete()
        {
            InspectionTreeItem item = CurrentTreeItem();
            if (item != null && item.OnDelete != null)
            {
                item.OnDelete();
            }
        }

        private void PerformInfoCard()
        {
            InfoCardState.OpenNestedCardFor(CurrentTreeItem());
        }

        private void PerformCancel()
        {
            ShellFrameStamps.MarkCancelConsumed();
            InfoCardState.CloseInfoCard();
        }
    }
}
