using RimWorld;
using Verse;
using Verse.Sound;

namespace RimWorldAccess.Shell
{
    /// <summary>
    /// Keyboard focus scope for gene inspection (ITab_GenesPregnancy / ITab_Genes, Biotech): the
    /// pregnancy-gene / embryo-gene / genepack-gene tree on the inspect pane. The mod owns no
    /// window here — the surface is the windowless <see cref="GeneInspectionState"/> — so the
    /// scope rides the focus stack through <see cref="GeneInspectionScopeMirror"/>. The state
    /// keeps the open/close lifecycle, the pawn/holder identity and the opening announcement, and
    /// hands the built root over through <see cref="OpenTree"/>; <see cref="EnsureTree"/> is the
    /// fallback for a tree built before the mirror ever constructed this scope.
    ///
    /// <b>Three deviations from the shared tree grammar.</b> Page Up/Down are not the shared
    /// section jump but a scan to the next/previous GeneDef row or sub-category, wrap-gated on
    /// WrapNavigation. Left arrow restores an expanded gene row's rich label from
    /// <see cref="InspectionTreeItem.Description"/> before the collapse announcement reads it,
    /// because <see cref="RimWorldAccess.GeneTreeBuilder"/> shortens the label on expand and
    /// stashes the rich form there. And Enter on an already-expanded node rejects rather than
    /// collapsing.
    ///
    /// <b>Alt+I stays unclaimed</b> — no <c>OnInfo</c> is wired on this tree, so the modal tail
    /// swallows it silently rather than announcing "no info card available". Do not add a claim.
    /// </summary>
    public sealed class GeneInspectionScope : TreeRegionScope
    {
        /// <summary>
        /// The one mirror-held instance, so <see cref="GeneInspectionState"/>'s statics can
        /// present a tree and read its first row. Set at construction and never cleared: the
        /// openers run inside a Harmony patch that can land either side of the mirror's
        /// reconcile, and a push-scoped reference would be null for whichever ordering lost.
        /// </summary>
        internal static GeneInspectionScope Live { get; private set; }

        public GeneInspectionScope()
        {
            Live = this;

            // Bespoke ids: this screen jumps between genes and sub-categories, not between
            // IsSectionBoundary nodes, so the shared tree.jump* ids stay unclaimed here.
            Claim("geneInspection.jumpToNextGene", e => PerformJumpToAdjacentGene(true));
            Claim("geneInspection.jumpToPreviousGene", e => PerformJumpToAdjacentGene(false));

            // The base's typeahead-gated Cancel claim, registered first, clears an active
            // search; this one fires only once no search is active and then ALWAYS closes —
            // never a second search-clear.
            Claim(SharedMenuGrammar.Cancel, e => GeneInspectionState.CloseInspection(),
                when: () => !TypeaheadHasActiveSearch);
        }

        public override string Name
        {
            get { return "gene-inspection"; }
        }

        /// <summary>No mod-owned <c>Window</c> exists for this inspect-pane overlay — self-claim Escape.</summary>
        public override bool OwnsCancel
        {
            get { return true; }
        }

        /// <summary>Windowless overlay: no captured buttons and no declared actions — no Buttons region.</summary>
        protected override bool IncludeActionsRegion
        {
            get { return false; }
        }

        /// <summary>
        /// The tree's own root label, already type-aware and built with the gene count, so the
        /// region needs no key of its own.
        /// </summary>
        protected override string TreeRegionLabel
        {
            get { return Tree.Root != null ? Tree.Root.Label.StripTags() : ""; }
        }

        /// <summary>The row the openers announce alongside the root label, or null before a tree exists.</summary>
        internal InspectionTreeItem FirstVisibleItem
        {
            get { return Tree.Count > 0 ? Tree.Visible[0] : null; }
        }

        /// <summary>Presents a freshly built tree; the opener speaks the opening announcement itself.</summary>
        internal void OpenTree(InspectionTreeItem root)
        {
            TypeaheadReset();
            SetTreeRoot(root);
        }

        /// <summary>Adopts a tree built before this scope existed (see <see cref="Live"/>); silent.</summary>
        internal void EnsureTree(InspectionTreeItem root)
        {
            if (!ReferenceEquals(Tree.Root, root))
            {
                OpenTree(root);
            }
        }

        internal void ClearTree()
        {
            TypeaheadReset();
            ResetTree();
        }

        public override void OnPush()
        {
            base.OnPush();
            GeneInspectionState.NotifyScopeAttached(this);
            GeneRowDrawPatch.BeginRecording();
            // No window pass brackets this surface, so the ring rides the gene-row postfix
            // itself rather than FocusedContentRect.
            GeneRowDrawPatch.WindowlessRingProvider = FocusedGene;
        }

        public override void OnPop()
        {
            GeneRowDrawPatch.WindowlessRingProvider = null;
            GeneRowDrawPatch.EndRecording();
            base.OnPop();
        }

        /// <summary>The focused row's Gene (a pawn's tracker) or GeneDef (an embryo/genepack gene set); null on group headers, detail children and the biostats summary.</summary>
        private object FocusedGene()
        {
            InspectionTreeItem item = CurrentTreeItem();
            object data = item != null ? item.Data : null;
            return data is Gene || data is GeneDef ? data : null;
        }

        /// <summary>
        /// Lazy child population: gene rows and biostat rows each carry a builder closure and
        /// start with no children.
        /// </summary>
        protected override void OnBeforeExpandNode(InspectionTreeItem item)
        {
            if (item.OnActivate != null && item.Children.Count == 0)
            {
                item.OnActivate();
            }
        }

        protected override ElementDescription DescribeTreeNode(InspectionTreeItem item)
        {
            var d = new ElementDescription();
            d.Label = item.Label.StripTags().TrimEnd('.', '!', '?');
            if (item.IsExpandable)
            {
                d.Role = ElementRole.TreeItem;
                d.Expanded = item.IsExpanded;
            }
            // Level and sibling position are left unset; the base fills both.
            return d;
        }

        /// <summary>
        /// Enter/Space on a row: an expanded node rejects, a collapsed one expands down the same
        /// path Right arrow takes, a GeneDef leaf opens the vanilla info card, anything else rejects.
        /// </summary>
        protected override void ActivateTreeNode(InspectionTreeItem item)
        {
            if (item.IsExpandable)
            {
                if (item.IsExpanded)
                {
                    SoundDefOf.ClickReject.PlayOneShotOnCamera();
                    TolkHelper.Speak("RimWorldAccess.Biotech.GeneInspection.AlreadyExpanded".Loc());
                    return;
                }
                PerformActivateExpand();
                return;
            }

            if (item.Data is GeneDef gene)
            {
                Find.WindowStack.Add(new Dialog_InfoCard(gene));
                SoundDefOf.Click.PlayOneShotOnCamera();
                return;
            }

            SoundDefOf.ClickReject.PlayOneShotOnCamera();
            TolkHelper.Speak("RimWorldAccess.Biotech.GeneInspection.NoAction".Loc());
        }

        /// <summary>
        /// Enter's expand branch, mirroring the base's Right-arrow dispatch so the sound and
        /// announcement cannot drift from it.
        /// </summary>
        private void PerformActivateExpand()
        {
            TreeActionResult<InspectionTreeItem> result = Tree.ExpandOrDrillDown();
            switch (result.Kind)
            {
                case TreeActionKind.Rejected:
                    SoundDefOf.ClickReject.PlayOneShotOnCamera();
                    break;
                case TreeActionKind.Expanded:
                case TreeActionKind.ExpandedSubmenu:
                    SoundDefOf.FloatMenu_Open.PlayOneShotOnCamera();
                    SyncRegionFromCurrentTree();
                    AnnounceCurrentItem();
                    break;
                case TreeActionKind.DrilledToChild:
                    SoundDefOf.Tick_Tiny.PlayOneShotOnCamera();
                    SyncRegionFromCurrentTree();
                    AnnounceCurrentItem();
                    break;
                    // TreeActionKind.None: silent no-op, matching the base's PerformExpand.
            }
        }

        /// <summary>
        /// Page Down/Page Up: the next/previous visible GeneDef item or sub-category header,
        /// with a wrap pass gated on the WrapNavigation setting. The search clears first, an
        /// exhausted scan click-rejects, and an empty tree returns silently.
        /// </summary>
        private void PerformJumpToAdjacentGene(bool forward)
        {
            TypeaheadReset();
            RefreshModel();
            ListModel region = Model.CurrentRegion;
            if (region == null || region.IsEmpty || Tree.Count == 0)
            {
                return;
            }
            int from = region.Index - PrefixRowCount;
            if (from < 0 || from >= Tree.Count)
            {
                return;
            }

            int step = forward ? 1 : -1;
            for (int i = from + step; i >= 0 && i < Tree.Count; i += step)
            {
                if (IsGeneJumpTarget(Tree.Visible[i]))
                {
                    LandOnGeneJumpTarget(i);
                    return;
                }
            }

            if (RimWorldAccessMod_Settings.Settings != null
                && RimWorldAccessMod_Settings.Settings.WrapNavigation)
            {
                // The wrap pass sweeps from the far edge back through the starting row
                // inclusive, so a lone target under the cursor is found again.
                int wrapStart = forward ? 0 : Tree.Count - 1;
                for (int i = wrapStart; forward ? i <= from : i >= from; i += step)
                {
                    if (IsGeneJumpTarget(Tree.Visible[i]))
                    {
                        MenuHelper.PlayWrapTone();
                        LandOnGeneJumpTarget(i);
                        return;
                    }
                }
            }

            SoundDefOf.ClickReject.PlayOneShotOnCamera();
        }

        private static bool IsGeneJumpTarget(InspectionTreeItem item)
        {
            return (item.Type == InspectionTreeItem.ItemType.Item && item.Data is GeneDef)
                || item.Type == InspectionTreeItem.ItemType.SubCategory;
        }

        private void LandOnGeneJumpTarget(int treeIndex)
        {
            Tree.SetSelectedIndex(treeIndex);
            SyncRegionFromCurrentTree();
            SoundDefOf.Tick_Tiny.PlayOneShotOnCamera();
            AnnounceCurrentItem();
        }
    }

    /// <summary>
    /// Keeps <see cref="GeneInspectionScope"/> in lockstep with
    /// <see cref="GeneInspectionState.IsActive"/>, reconciled every OnGUI pass alongside the
    /// other inspect-pane tab mirrors.
    ///
    /// Stands down while an info card is open over the tab: activating a GeneDef leaf row opens a
    /// real Dialog_InfoCard, and without the gate the mirror's per-frame Push would re-float this
    /// scope above the card every pass. The scope keeps its tree and cursor across that pop and
    /// re-push; only <see cref="GeneInspectionState.Close"/> discards the tree.
    /// </summary>
    internal static class GeneInspectionScopeMirror
    {
        private static readonly GeneInspectionScope scope = new GeneInspectionScope();

        public static void Reconcile()
        {
            if (GeneInspectionState.IsActive && !InfoCardState.IsActive)
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
