using System.Reflection;
using HarmonyLib;
using RimWorld;
using Verse;
using Verse.Sound;

namespace RimWorldAccess.Shell
{
    /// <summary>
    /// The keyboard focus scope for vanilla's <see cref="Dialog_ViewGenes"/> — the read-only gene
    /// viewer a pawn's "Xenotype" chip opens (the starting-pawn chip built in
    /// <c>StartingPawnHelper</c>, and the same chip on the in-game character card). Window-attached
    /// through <see cref="ScopeForWindow.Register"/> in <see cref="ShellBootstrap"/>, modeled on
    /// <see cref="RimWorldAccess.Shell.CharEditorXenoGenesScope"/>: one <see cref="TreeRegionScope"/>
    /// over the same gene tree, no prefix rows (nothing here is editable).
    ///
    /// A bespoke scope is required: the dialog never sets <c>absorbInputAroundWindow</c>, and
    /// <see cref="ScopeForWindow.GenericReaderEligible"/> refuses a non-absorbing vanilla-assembly
    /// window outright, so nothing would attach and the page scope underneath would keep the arrows.
    ///
    /// The gene tree is REUSED, not re-transcribed:
    /// <see cref="RimWorldAccess.GeneTreeBuilder.BuildAdultGeneTree"/> is the same reader the
    /// Inspection Genes tab and CharEditorXenoGenesScope use, so genes read identically everywhere.
    /// Vanilla's visual-only <c>GeneUIUtility.DrawGenesInfo</c> is never called here.
    ///
    /// ESCAPE IS THIS SCOPE'S. The dialog opens over a <see cref="Page"/>, whose
    /// <c>DoBottomButtons</c> Escape poll (Page.cs:58) runs in the page's own window pass and is
    /// guarded only for frames a scope stamped <see cref="ShellFrameStamps.MarkCancelConsumed"/>,
    /// while <c>StartingPawnScreenScopePatch_CanDoBack</c>'s guard requires that page scope to be
    /// TOP, which it is not while this one lives. So <see cref="OwnsCancel"/> is unconditionally
    /// true, blocking vanilla even at window-pass time where a stamp would arrive too late; the
    /// claim stamps and closes through the window's own <c>Close</c> (vehicle A). The base's
    /// typeahead-gated Cancel claim is registered first, so a search-clearing Escape never also
    /// closes the window.
    ///
    /// Buttons are captured rather than declared: the dialog draws its Close through a real
    /// <c>Widgets.ButtonText</c>, so Enter on the captured row injects the click into vanilla's own
    /// inline handler. The dev-mode "Devtool..." button is captured the same way.
    /// </summary>
    public sealed class ViewGenesScope : TreeRegionScope
    {
        /// <summary>Read-only access to the dialog's own pawn; the field is private.</summary>
        private static readonly FieldInfo TargetField = AccessTools.Field(typeof(Dialog_ViewGenes), "target");

        private readonly Dialog_ViewGenes dialog;
        private readonly Pawn pawn;

        private bool announcedOpen;

        /// <summary>
        /// One-shot: the next landing announcement carries the dialog's own header. Consumed inside
        /// <see cref="AnnouncePrefix"/> so the header and the first row are ONE utterance.
        /// </summary>
        private bool pendingOpeningHeader;

        /// <summary>
        /// The ScopeForWindow factory. Null leaves the window on its vanilla flow, the honest answer
        /// when the dialog's pawn cannot be read: a modal scope over an undescribable surface would
        /// only take the keyboard away.
        /// </summary>
        internal static FocusScope TryCreate(Window window)
        {
            var dialog = window as Dialog_ViewGenes;
            if (dialog == null)
            {
                return null;
            }
            Pawn pawn = TargetPawn(dialog);
            if (pawn == null || pawn.genes == null)
            {
                return null;
            }
            return new ViewGenesScope(dialog, pawn);
        }

        private ViewGenesScope(Dialog_ViewGenes dialog, Pawn pawn)
        {
            this.dialog = dialog;
            this.pawn = pawn;

            // Alt+I: the vanilla info card for the focused gene row's GeneDef.
            Claim(SharedMenuGrammar.Info, e => ActivateInfoCard());

            // Escape, once no search is active: the base's typeahead claim is registered ahead of
            // this one and clears the search first.
            Claim(SharedMenuGrammar.Cancel, e => PerformClose(), when: () => !TypeaheadHasActiveSearch);

            SetTreeRoot(GeneTreeBuilder.BuildAdultGeneTree(pawn));
            RefreshModel();
        }

        private static Pawn TargetPawn(Dialog_ViewGenes dialog)
        {
            if (dialog == null || TargetField == null)
            {
                return null;
            }
            return TargetField.GetValue(dialog) as Pawn;
        }

        public override string Name
        {
            get { return "view-genes"; }
        }

        protected internal override Window OwnedWindow
        {
            get { return dialog; }
        }

        /// <summary>See the class remarks: a coexisting window over a Page cannot leave Escape to vanilla.</summary>
        public override bool OwnsCancel
        {
            get { return true; }
        }

        /// <summary>The tree's own root label, already built with the xenotype and the gene count.</summary>
        protected override string TreeRegionLabel
        {
            get { return Tree.Root != null ? Tree.Root.Label.StripTags() : ""; }
        }

        // Lifecycle and the opening announcement.

        public override void OnPush()
        {
            base.OnPush();
            announcedOpen = false;
            GeneRowDrawPatch.BeginRecording();
        }

        public override void OnPop()
        {
            GeneRowDrawPatch.EndRecording();
            base.OnPop();
        }

        /// <summary>The focused gene's own row, as vanilla's <c>DrawGene</c> just drew it; detail children and the biostats summary have no row of their own.</summary>
        protected internal override UnityEngine.Rect FocusedContentRect()
        {
            InspectionTreeItem item = CurrentTreeItem();
            var gene = item != null ? item.Data as Gene : null;
            return gene != null
                ? GeneRowDrawPatch.Rows.FindLast(gene)
                : default(UnityEngine.Rect);
        }

        public override void OnFocus()
        {
            base.OnFocus();
            if (announcedOpen)
            {
                // Regained focus after the info card closed: the viewer is read-only, so nothing can
                // have changed and this is silent.
                return;
            }
            announcedOpen = true;

            if (Tree.Count > 0)
            {
                pendingOpeningHeader = true;
                AnnounceCurrentItem();
                // Never left pending for a later landing: either the compose path consumed it above,
                // or this screen has no row to carry it.
                pendingOpeningHeader = false;
                return;
            }

            // No gene rows at all: the header plus the tree's own "no genes" line, still one
            // utterance.
            string label = TreeRegionLabel;
            string header = OpeningHeader();
            TolkHelper.SpeakData(string.IsNullOrEmpty(label) ? header : header + ". " + label);
        }

        /// <summary>The dialog's own header, exactly as it draws it (Dialog_ViewGenes.cs:40).</summary>
        private string OpeningHeader()
        {
            string title = "ViewGenes".Translate().ToString();
            string xenotype = pawn != null && pawn.genes != null ? pawn.genes.XenotypeLabelCap : null;
            return string.IsNullOrEmpty(xenotype) ? title : title + ": " + xenotype;
        }

        /// <summary>
        /// Folds the opening header into the first landing announcement. The base's submenu-boundary
        /// prefix is computed unconditionally first, because it tracks state.
        /// </summary>
        protected override string AnnouncePrefix(int region, int index)
        {
            string boundary = base.AnnouncePrefix(region, index);
            if (!pendingOpeningHeader)
            {
                return boundary;
            }
            string header = OpeningHeader();
            if (string.IsNullOrEmpty(header))
            {
                return boundary;
            }
            return string.IsNullOrEmpty(boundary) ? header : header + ". " + boundary;
        }

        // Tree rows: read-only, expand and collapse only.

        protected override ElementDescription DescribeTreeNode(InspectionTreeItem item)
        {
            var d = new ElementDescription();
            d.Label = item.Label.StripTags();
            if (item.IsExpandable)
            {
                d.Role = ElementRole.TreeItem;
                d.Expanded = item.IsExpanded;
            }
            return d;
        }

        protected override void OnBeforeExpandNode(InspectionTreeItem item)
        {
            if (item.OnActivate != null && item.Children.Count == 0)
            {
                item.OnActivate();
            }
        }

        protected override void ActivateTreeNode(InspectionTreeItem item)
        {
            if (item.IsExpandable)
            {
                if (item.IsExpanded)
                {
                    SoundDefOf.ClickReject.PlayOneShotOnCamera();
                    return;
                }
                PerformActivateExpand();
                return;
            }
            AnnounceCurrentItem();
        }

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
            }
        }

        /// <summary>Alt+I: the vanilla info card for the focused row's gene, when it has one.</summary>
        private void ActivateInfoCard()
        {
            InspectionTreeItem item = CurrentTreeItem();
            if (item != null && item.LinkedDef is GeneDef gene)
            {
                Find.WindowStack.Add(new Dialog_InfoCard(gene));
                SoundDefOf.Click.PlayOneShotOnCamera();
                return;
            }
            SoundDefOf.ClickReject.PlayOneShotOnCamera();
        }

        /// <summary>Escape: close through the window's own <c>Close</c> (vehicle A) and stamp the frame so the page underneath cannot also read it as Back.</summary>
        private void PerformClose()
        {
            ShellFrameStamps.MarkCancelConsumed();
            if (dialog != null)
            {
                dialog.Close();
            }
        }
    }
}
