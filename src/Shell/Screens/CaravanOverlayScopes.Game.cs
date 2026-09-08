using System.Collections.Generic;
using RimWorld;
using Verse;
using Verse.Sound;

namespace RimWorldAccess.Shell
{
    /// <summary>
    /// The keyboard focus scope for the windowless stat-breakdown tree viewer
    /// (<see cref="StatBreakdownState"/>). Modal, and gated on
    /// <c>StatBreakdownState.IsActive &amp;&amp; !InfoCardState.IsActive</c>: without the explicit
    /// InfoCardState term this scope's own per-pass Push re-floats it above an open InfoCardScope
    /// every frame.
    /// A <see cref="TreeRegionScope"/> riding <see cref="TreeModel{T}"/> directly.
    /// <see cref="StatBreakdownState"/> owns the explanation parser and the open/close lifecycle
    /// and hands the parsed root over through <c>TakePendingRoot</c>, drained in
    /// <see cref="OnPush"/> — the mirror does not push until the reconcile pass after <c>Open</c>
    /// returns, so the tree cannot be seeded synchronously. Draining once per open, rather than
    /// rebuilding on every push, keeps the cursor where the player left it across the pop/re-push
    /// the InfoCardState term causes.
    /// Alt+I is claimed and speaks <c>InfoCardState.SpeakNoInfoCardAvailable()</c>: this
    /// parsed-text tree's nodes never carry a LinkedDef. Delete is left unclaimed, a true no-op
    /// the modal boundary swallows silently.
    /// Enter toggles expand/collapse on a section row (drilling in submenu mode) and is silent on
    /// a leaf; the toggle is deliberately NOT the <see cref="FilterTreeScopeBase"/> convention of
    /// "Enter never expands".
    /// NON-member of <see cref="ShellGuards.MenuOwnsInput"/>: this scope's modality reproduces the
    /// blackout structurally.
    /// </summary>
    public sealed class StatBreakdownScope : TreeRegionScope
    {
        private bool announcedOpen;

        public StatBreakdownScope()
        {
            Claim("statBreakdown.close", e => StatBreakdownState.CloseFromTab());

            // Claimed but always rejecting: this tree never flags a section boundary.
            Claim("tree.jumpToPreviousSection", e => PerformJumpToAdjacentSection(false));
            Claim("tree.jumpToNextSection", e => PerformJumpToAdjacentSection(true));

            Claim(SharedMenuGrammar.Info, e => InfoCardState.SpeakNoInfoCardAvailable());

            // The base ScreenScope's typeahead-gated Cancel claim, registered ahead of this one,
            // clears an active search first.
            Claim(SharedMenuGrammar.Cancel, e => PerformClose(), when: () => !TypeaheadHasActiveSearch);

            twinMount = new TreeRegionTwinMount(this, () => StatBreakdownState.StatName);
        }

        /// <summary>The visible body: the tree twin draws this screen's rows while it is the top scope.</summary>
        private readonly ITreeTwinMount twinMount;

        public override string Name
        {
            get { return "stat-breakdown"; }
        }

        /// <summary>No owning <c>Window</c> exists for this windowless overlay — self-claim Escape.</summary>
        public override bool OwnsCancel
        {
            get { return true; }
        }

        /// <summary>Windowless overlay: no captured buttons and no declared actions — no Buttons region.</summary>
        protected override bool IncludeActionsRegion
        {
            get { return false; }
        }

        /// <summary>The stat being broken down, so the region reads as e.g. "Move speed".</summary>
        protected override string TreeRegionLabel
        {
            get { return StatBreakdownState.StatName; }
        }

        public override void OnPush()
        {
            InspectionTreeItem root = StatBreakdownState.TakePendingRoot();
            if (root != null)
            {
                SetTreeRoot(root);
                announcedOpen = false;
            }
            base.OnPush();
            TreeTwinWindow.Mount(twinMount);
        }

        public override void OnPop()
        {
            base.OnPop();
            TreeTwinWindow.Unmount(twinMount);
        }

        /// <summary>
        /// Second half of the open announcement pair (<see cref="StatBreakdownState.Open"/> speaks
        /// the header line): the first row, exactly once per open, so a re-push after an
        /// interposed info card stays silent.
        /// </summary>
        public override void OnFocus()
        {
            base.OnFocus();
            if (announcedOpen)
            {
                return;
            }
            announcedOpen = true;
            AnnounceCurrentItem();
        }

        protected override ElementDescription DescribeTreeNode(InspectionTreeItem item)
        {
            var d = new ElementDescription();
            d.Label = item.Label;
            if (item.IsExpandable)
            {
                d.Role = ElementRole.TreeItem;
                d.Expanded = item.IsExpanded;
            }
            // The parsed result value is verbose, so it rides the late Extras slot, not the label.
            d.Extras = item.Tooltip;
            return d;
        }

        /// <summary>
        /// Enter/Space on a row: a section toggles expand/collapse (drills in submenu mode), a
        /// leaf does nothing at all.
        /// </summary>
        protected override void ActivateTreeNode(InspectionTreeItem item)
        {
            if (!item.IsExpandable)
            {
                return;
            }

            if (Tree.SubmenuMode)
            {
                TreeActionResult<InspectionTreeItem> result = Tree.ExpandOrDrillDown();
                switch (result.Kind)
                {
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
                        // Rejected/None: silent, as in the base's own PerformExpand.
                }
                return;
            }

            if (!item.IsExpanded)
            {
                OnBeforeExpandNode(item);
            }
            item.IsExpanded = !item.IsExpanded;
            Tree.Reflatten();
            (item.IsExpanded ? SoundDefOf.FloatMenu_Open : SoundDefOf.FloatMenu_Cancel).PlayOneShotOnCamera();
            SyncRegionFromCurrentTree();
            AnnounceCurrentItem();
        }

        private void PerformClose()
        {
            ShellFrameStamps.MarkCancelConsumed();
            StatBreakdownState.HandleEscape();
        }
    }

    /// <summary>
    /// The keyboard focus scope for the windowless numeric quantity picker
    /// (<see cref="QuantityMenuState"/>). Modal, and gated on
    /// <c>QuantityMenuState.IsActive &amp;&amp; !InfoCardState.IsActive &amp;&amp;
    /// Find.WindowStack.WindowOfType&lt;Dialog_MessageBox&gt;() == null</c>: the two window-type
    /// checks detect a real dialog above this overlay language-independently. A broad
    /// "no live modal" term would be self-referential — this scope becomes a live modal the
    /// instant it is pushed — and would stand itself down.
    /// Home/End are deliberately not match-aware and always clear the search first, unlike
    /// Up/Down. Escape and Enter both resolve to <see cref="QuantityMenuState.Dismiss"/>, since
    /// the menu writes through on every step. Digits reach
    /// <see cref="QuantityMenuState.HandleTypeahead"/> through this scope's
    /// <see cref="CharSink"/> as already-layout-resolved characters; TypeaheadDispatcher registry
    /// 0.25 is the CJK/IME channel twin of that sink.
    /// </summary>
    public sealed class QuantityMenuScope : FocusScope, ICharSink
    {
        public QuantityMenuScope()
        {
            Claim(SharedMenuGrammar.Cancel, OnCancel);
            Claim(SharedMenuGrammar.Activate, OnActivate);
            Claim("quantityMenu.increaseQuantity", delegate { QuantityMenuState.SelectNext(); });
            Claim("quantityMenu.decreaseQuantity", delegate { QuantityMenuState.SelectPrevious(); });
            Claim("quantityMenu.jumpToMin", delegate { QuantityMenuState.JumpToMin(); });
            Claim("quantityMenu.jumpToMax", delegate { QuantityMenuState.JumpToMax(); });
            Claim("quantityMenu.increaseTen", delegate { QuantityMenuState.StepBy(10); });
            Claim("quantityMenu.decreaseTen", delegate { QuantityMenuState.StepBy(-10); });
            Claim("quantityMenu.increaseHundred", delegate { QuantityMenuState.StepBy(100); });
            Claim("quantityMenu.decreaseHundred", delegate { QuantityMenuState.StepBy(-100); });
            Claim(SharedMenuGrammar.SearchBackspace, delegate { QuantityMenuState.HandleBackspace(); },
                when: () => QuantityMenuState.HasActiveNumericInput);
        }

        public override string Name
        {
            get { return "quantity-menu"; }
        }

        public override bool IsLive
        {
            get { return true; }
        }

        public override ICharSink CharSink
        {
            get { return this; }
        }

        private static void OnActivate(KeyEventSnapshot e)
        {
            ShellFrameStamps.MarkAcceptConsumed();
            QuantityMenuState.Dismiss();
        }

        private static void OnCancel(KeyEventSnapshot e)
        {
            ShellFrameStamps.MarkCancelConsumed();
            QuantityMenuState.Dismiss();
        }

        public bool HandleChar(char c)
        {
            bool sign = (c == '-' || c == '+') && QuantityMenuState.AcceptsSign;
            if (!char.IsDigit(c) && !sign)
            {
                return false;
            }
            QuantityMenuState.HandleTypeahead(c);
            return true;
        }
    }

    /// <summary>
    /// Reconciles the caravan-overlay family, <see cref="StatBreakdownScope"/> and
    /// <see cref="QuantityMenuScope"/>, AFTER the caravan-cluster mirrors in
    /// <see cref="ShellDispatcherPatch"/>'s mirror pass. Both overlays open from within the
    /// caravan-cluster dialogs, so reconciling them later makes their Push re-float them above
    /// whichever parent dialog opened them, with neither side knowing about the other. The gates
    /// still need their InfoCardState/Dialog_MessageBox terms; the ordering alone is not enough.
    /// The two are mutually exclusive by construction — neither Open() call site is reachable from
    /// inside the other's active session — so push order between them is arbitrary.
    /// </summary>
    internal static class CaravanOverlayScopeMirror
    {
        private static readonly StatBreakdownScope statBreakdown = new StatBreakdownScope();
        private static readonly QuantityMenuScope quantityMenu = new QuantityMenuScope();

        public static void Reconcile()
        {
            bool infoCard = InfoCardState.IsActive;
            bool messageBox = Find.WindowStack.WindowOfType<Dialog_MessageBox>() != null;

            ReconcileOne(statBreakdown, StatBreakdownState.IsActive && !infoCard);
            ReconcileOne(quantityMenu, QuantityMenuState.IsActive && !infoCard && !messageBox);
        }

        private static void ReconcileOne(FocusScope scope, bool live)
        {
            if (live)
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
