using System.Collections.Generic;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.Sound;

namespace RimWorldAccess.Shell
{
    /// <summary>
    /// Keyboard focus scope for the faction-relations dialog on the starting-site screen
    /// (Dialog_FactionDuringLanding, opened with F). Window-attached via ShellBootstrap;
    /// <see cref="Owns"/> keeps the per-dialog identity check.
    /// <see cref="FactionLandingState"/> owns the dialog lifecycle, the tree data and the dev
    /// "Show all" toggle.
    ///
    /// <c>src/Factions/FactionTreeNavigation.cs</c> still serves the in-game Factions tab, so
    /// the "walk up to the owning faction, then open its info card" logic exists twice — here in
    /// <see cref="PerformInfoCard"/> and there in <c>HandleInfoCard</c>.
    ///
    /// Escape: the base's typeahead-gated Cancel claim, registered first, clears an active
    /// search; otherwise this scope's claim closes the dialog. It stamps CancelConsumed because
    /// the site page's own Escape=Back raw poll runs in the same main pass after the dispatcher.
    ///
    /// <b>The Open()-time flag writes still matter.</b> <c>closeOnAccept</c>/<c>closeOnCancel</c>
    /// are set false by <see cref="FactionLandingState.Open"/>, making vanilla's own Enter/Escape
    /// close paths inert. OwnsCancel/OwnsAccept also block them, but the routers consult only the
    /// TOP live scope, so while a nested window sits above this one those two flags are the only
    /// thing stopping one Escape from closing that window AND this dialog beneath it
    /// (<c>Event.current.Use()</c> does not stop <c>Window.OnCancelKeyPressed</c>). Dropping or
    /// narrowing those writes would reopen vanilla's close path here.
    /// </summary>
    public sealed class FactionLandingScope : TreeRegionScope, IFactionRowFocusSource
    {
        /// <summary>
        /// The pushed scope, so <see cref="FactionLandingState"/>'s statics can reach the tree.
        /// Null between <c>Open</c> — which runs inside WindowStack.Add, before the Add postfix
        /// attaches this scope — and the push: the state writes its root first and
        /// <see cref="OnPush"/> seeds from there.
        /// </summary>
        private static FactionLandingScope live;

        private readonly Dialog_FactionDuringLanding dialog;

        public FactionLandingScope(Dialog_FactionDuringLanding dialog)
        {
            this.dialog = dialog;

            // TreeRegionScope's constructor deliberately leaves Page Up/Down unclaimed, so this
            // screen claims both. The faction tree flags no section boundaries, so both reject.
            Claim("tree.jumpToPreviousSection", e => PerformJumpToAdjacentSection(false));
            Claim("tree.jumpToNextSection", e => PerformJumpToAdjacentSection(true));

            Claim("factionLanding.inspect", e => PerformInfoCard());

            // The dev "Show all" toggle rides the shared RightBracket context-menu chord, gated
            // off outside dev mode so a non-dev RightBracket falls through.
            Claim("factionLanding.contextMenu", e => FactionLandingState.OpenDevContextMenu(),
                when: () => Prefs.DevMode);

            // Only once no search is active; the base's typeahead claim clears it first. The
            // class remarks explain why the stamp stays even though this scope owns cancel.
            Claim(SharedMenuGrammar.Cancel, e =>
            {
                ShellFrameStamps.MarkCancelConsumed();
                FactionLandingState.CloseDialog();
            }, when: () => !TypeaheadHasActiveSearch);

            // Delete is deliberately unclaimed: no faction node wires OnDelete, so the modal
            // backstop's silent consumption is identical to a generic DeleteCurrent().
        }

        /// <summary>The scope driving the open faction dialog, or null before its push / after its pop.</summary>
        internal static FactionLandingScope Live
        {
            get { return live; }
        }

        public override string Name
        {
            get { return "faction-landing"; }
        }

        /// <summary>
        /// The dialog ships closeOnCancel = false so this scope can own Escape (clear a search,
        /// then close); vanilla would otherwise never close it at all.
        /// </summary>
        public override bool OwnsCancel
        {
            get { return true; }
        }

        internal bool Owns(Window window)
        {
            return ReferenceEquals(window, dialog);
        }

        protected override string TreeRegionLabel
        {
            get { return "RimWorldAccess.Factions.Tree.Root".Translate(); }
        }

        public override void OnPush()
        {
            base.OnPush();
            live = this;
            LoadTree(FactionLandingState.CurrentTreeRoot);
        }

        public override void OnPop()
        {
            base.OnPop();
            if (ReferenceEquals(live, this))
            {
                live = null;
            }
            ResetTree();
        }

        /// <summary>
        /// Adopts the state's current tree root. The region cursor is moved explicitly: the
        /// region model does not track the tree's own selection.
        /// </summary>
        internal void LoadTree(InspectionTreeItem root)
        {
            TypeaheadReset();
            SetTreeRoot(root);
            RefreshModel();
            SyncRegionFromCurrentTree();
        }

        /// <summary>
        /// The dev "Show all" toggle's rebuild: unlike <see cref="LoadTree"/>, it preserves
        /// expansion state and lands the cursor back on the same logical faction row rather than
        /// resetting to row 0. Runs from an action handler, not inside RefreshContent, so there
        /// is no stale-count hazard.
        /// </summary>
        internal void ReloadTreePreservingState(InspectionTreeItem newRoot)
        {
            TypeaheadReset();
            RefreshModel();
            ListModel region = Model.Region(0);
            int before = region != null && !region.IsEmpty ? region.Index : 0;
            int restored = SetTreeRootPreservingState(newRoot, before);
            RefreshModel();
            ListModel after = Model.Region(0);
            if (after != null && !after.IsEmpty)
            {
                after.MoveTo(Mathf.Clamp(restored >= 0 ? restored : before, 0, after.Count - 1));
            }
        }

        /// <summary>Drops the tree on close.</summary>
        internal void ClearTree()
        {
            TypeaheadReset();
            ResetTree();
        }

        /// <summary>Re-speak the focused row (the dev "Show all" toggle's own re-announce).</summary>
        internal void AnnounceCurrentRow()
        {
            AnnounceCurrentItem();
        }

        protected override ElementDescription DescribeTreeNode(InspectionTreeItem item)
        {
            ElementDescription d = new ElementDescription();

            // The short faction name once expanded: the collapsed label carries the whole summary.
            string label = item.IsExpandable && item.IsExpanded && !string.IsNullOrEmpty(item.ExpandedLabel)
                ? item.ExpandedLabel
                : item.Label.TrimEnd('.', '!', '?');

            // Expansion rides the LABEL channel because this screen speaks the child count with
            // it and the composer's Expanded field carries the bare word only; both stay unset
            // so it is spoken once.
            d.Label = label + TreeNavigationHelper.FormatExpansionSuffix(item, includeChildCount: true);

            return d;
        }

        /// <summary>
        /// Matches a node's own label rather than its composed announcement, since the expansion
        /// suffix rides the label channel here (see <see cref="DescribeTreeNode"/>).
        /// </summary>
        protected override string ContentRowSearchText(int region, int row)
        {
            if (region == 0)
            {
                int treeIndex = row - PrefixRowCount;
                if (treeIndex >= 0 && treeIndex < Tree.Count)
                {
                    return Tree.Visible[treeIndex].Label ?? "";
                }
            }
            return base.ContentRowSearchText(region, row);
        }

        /// <summary>
        /// Enter/Space on a tree row: a node's own callback if it has one, else an expandable
        /// node toggles expand/collapse (the one place Enter also collapses), else silence.
        /// </summary>
        protected override void ActivateTreeNode(InspectionTreeItem item)
        {
            // No faction node sets OnActivate today; the branch keeps one working if it gains one.
            if (item.OnActivate != null)
            {
                item.OnActivate();
                return;
            }

            if (item.IsExpandable)
            {
                PerformActivateExpandToggle(item);
            }
        }

        /// <summary>
        /// Alt+I: vanilla's own Dialog_InfoCard for the row's faction, walking up from the
        /// focused row since a goodwill or relations child belongs to the faction above it.
        /// </summary>
        private void PerformInfoCard()
        {
            Faction faction = FactionOfCurrentRow();
            if (faction == null)
            {
                SoundDefOf.ClickReject.PlayOneShotOnCamera();
                TolkHelper.Speak("RimWorldAccess.Factions.Tab.NoFactionSelected".Loc());
                return;
            }
            Find.WindowStack.Add(new Verse.Dialog_InfoCard(faction));
        }

        /// <summary>
        /// The faction under the keyboard cursor, walking up exactly as Alt+I does so
        /// <c>FactionRowFocusRingPatch</c> rings the same row on the real dialog underneath.
        /// </summary>
        Faction IFactionRowFocusSource.FocusedFaction
        {
            get { return FactionOfCurrentRow(); }
        }

        private protected override void CollectRouteCandidates(
            List<PointerHitCandidate> candidates, List<RouteTarget> targets)
        {
            TreePanel panel = PanelFor(TreeRegionIndex);
            if (panel != null)
            {
                FactionRowFocusRingPatch.AddTreeRouteCandidates(panel.Tree.Visible,
                    TreeRegionIndex, PrefixRowCountFor(TreeRegionIndex), candidates, targets);
            }
        }

        private Faction FactionOfCurrentRow()
        {
            for (InspectionTreeItem item = CurrentTreeItem(); item != null; item = item.Parent)
            {
                Faction faction = item.Data as Faction;
                if (faction != null)
                {
                    return faction;
                }
            }
            return null;
        }
    }
}
