using System.Collections.Generic;
using RimWorld;
using Verse;
using Verse.Sound;

namespace RimWorldAccess.Shell
{
    /// <summary>
    /// The keyboard focus scope for the Character Editor mod's <c>DialogViewXenoGenes</c>,
    /// registered through <see cref="ScopeForWindow.Register"/> in <see cref="CharEditorDialogCompat"/>,
    /// gated on <see cref="RimWorldAccess.CharEditorXenoGenesCompat.Ready"/>. All reflection lives in
    /// that facade; this scope only reads its typed methods. A compact <see cref="TreeRegionScope"/>
    /// (ONE prefix row, the endo/xeno target toggle, over the read-only gene tree) rather than the
    /// shared <see cref="CharEditorBrowserScope"/> -- there is no list-with-filters shape here.
    ///
    /// THE GENE GRID IS REUSED, not re-transcribed: the
    /// tree is <see cref="RimWorldAccess.GeneTreeBuilder.BuildAdultGeneTree"/>, the SAME reader the
    /// Inspection Genes tab (<c>PawnGenesAdapter</c>) already uses for a pawn's gene tracker --
    /// Endogenes and Xenogenes as two groups, each gene expandable into its own biostat/description
    /// detail children, plus a biostats-total summary. This is strictly richer than the mod's own
    /// visual-only <c>GeneUIUtility.DrawGenesInfo</c>, which this scope does not call at all. The tree
    /// shows BOTH gene groups regardless of the endo/xeno toggle -- that toggle only decides which
    /// set Add/Remove/Clear operate on (mirroring the mod's own dialog, which draws the full grid
    /// unconditionally too), so toggling it re-announces the prefix row only, never rebuilds the tree.
    ///
    /// LIVE, NO OK (class remarks on <see cref="RimWorldAccess.CharEditorXenoGenesCompat"/>): Escape
    /// already closes the window through vanilla's own <c>Window.OnCancelKeyPressed</c>
    /// (<c>closeOnCancel = true</c>, no override) with no <see cref="ScreenScope.OwnsCancel"/>
    /// override needed here. A Buttons-region Close row is still declared for discoverability.
    ///
    /// CLEAR gets OUR OWN confirm (the mod's own "breset" applies instantly with none, and can wipe
    /// an entire gene set) via the SAME <c>Dialog_MessageBox.CreateConfirmation</c> vehicle
    /// <see cref="CharacterEditorScope"/>'s own <c>ConfirmDeletePawn</c> uses -- see the facade's own
    /// class remarks for why <c>Event.current.control</c> is never read here; Clear and "Clear,
    /// keeping hair and skin" are two discrete rows instead.
    /// </summary>
    internal sealed class CharEditorXenoGenesScope : TreeRegionScope
    {
        private const int TargetToggleRow = 0;

        private readonly Window dialog;
        private readonly List<ScreenAction> actions = new List<ScreenAction>();

        private bool announcedOpen;

        public CharEditorXenoGenesScope(Window dialog)
        {
            this.dialog = dialog;

            // Unified Alt+I drill-in (feedback_unified_alt_i_drill_in): opens the vanilla info card
            // for the focused gene row's GeneDef, matching every other tree screen's convention.
            Claim(SharedMenuGrammar.Info, e => ActivateInfoCard());

            RebuildTree();
            RefreshModel();
        }

        public override string Name => "char-editor-xeno-genes";

        protected internal override Window OwnedWindow => dialog;

        /// <summary>Gene names are worth searching by name (table-model T4), matching every sibling gene tree screen.</summary>
        protected override bool EnableTypeahead => true;

        /// <summary>The dialog draws its own real icon buttons (SZWidgets.ButtonImage calls) that are not real Widgets.ButtonText -- blanket capture would still duplicate the declared actions below, matching the birthday/head-addons/browser scopes' own reasoning.</summary>
        protected override bool CaptureWindowButtons => false;

        protected override string TreeRegionLabel => Tree.Root != null ? Tree.Root.Label.StripTags() : "";

        public override void OnFocus()
        {
            base.OnFocus();
            if (announcedOpen)
            {
                // Regained focus after a child dialog (Add gene, Apply xenotype): the gene set
                // may have changed under the tree -- the silent-refresh-on-return
                // convention (RefreshSectionInPlace's reasoning; this scope's whole tree is
                // small enough to rebuild outright).
                RebuildTree();
                RefreshModel();
                return;
            }
            announcedOpen = true;
            TolkHelper.SpeakData(Tree.Root != null ? Tree.Root.Label.StripTags() + "." : "");
        }

        private void RebuildTree()
        {
            Pawn pawn = CharEditorXenoGenesCompat.TargetPawn(dialog);
            InspectionTreeItem root = GeneTreeBuilder.BuildAdultGeneTree(pawn);
            SetTreeRoot(root);
        }

        // ------------------------------------------------------------------
        // Prefix row: the endo/xeno target toggle.
        // ------------------------------------------------------------------

        protected override int PrefixRowCount => 1;

        protected override ElementDescription DescribePrefixRow(int index)
        {
            var d = new ElementDescription();
            d.Label = "RimWorldAccess.CharEd.XenoGenes.TargetXenogenes".Translate();
            d.Role = ElementRole.Checkbox;
            d.Check = CharEditorXenoGenesCompat.IsXeno(dialog) ? CheckState.Checked : CheckState.Unchecked;
            return d;
        }

        // The endo/xeno toggle is a checkbox: no direction-sensitive adjust
        // (vanilla checkboxes have no arrow-adjust either) -- CanAdjustPrefixRow
        // stays at the base TreeRegionScope default (false), so Left/Right is
        // a no-op here. Enter/Space toggle it via ActivatePrefixRow below.

        protected override void ActivatePrefixRow(int index)
        {
            if (index != TargetToggleRow)
                return;
            ToggleTargetAndAnnounce();
        }

        private void ToggleTargetAndAnnounce()
        {
            CharEditorXenoGenesCompat.ToggleTarget(dialog);
            SoundDefOf.Checkbox_TurnedOn.PlayOneShotOnCamera();
            RefreshModel();
            TolkHelper.SpeakData(AnnouncementComposer.ComposeStateChange(
                DescribePrefixRow(TargetToggleRow), TranslatedShellVocabulary.Instance));
        }

        // ------------------------------------------------------------------
        // Tree rows: read-only, expand/collapse only (mirrors GeneInspectionScope exactly).
        // ------------------------------------------------------------------

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
            ListModel region = Model.CurrentRegion;
            int treeIndex = region != null && !region.IsEmpty ? region.Index - PrefixRowCount : -1;
            if (treeIndex >= 0 && treeIndex < Tree.Count)
            {
                InspectionTreeItem item = Tree.Visible[treeIndex];
                if (item.LinkedDef is GeneDef gene)
                {
                    Find.WindowStack.Add(new Dialog_InfoCard(gene));
                    SoundDefOf.Click.PlayOneShotOnCamera();
                    return;
                }
            }
            SoundDefOf.ClickReject.PlayOneShotOnCamera();
        }

        // ------------------------------------------------------------------
        // Buttons region.
        // ------------------------------------------------------------------

        protected override IReadOnlyList<ScreenAction> DeclaredActions
        {
            get
            {
                actions.Clear();
                actions.Add(new ScreenAction("RimWorldAccess.CharEd.XenoGenes.AddGene".Translate(), PerformAddGene));
                actions.Add(new ScreenAction("RimWorldAccess.CharEd.XenoGenes.RemoveGene".Translate(), PerformRemoveGene));
                actions.Add(new ScreenAction("RimWorldAccess.CharEd.XenoGenes.Clear".Translate(), PerformClear));
                actions.Add(new ScreenAction("RimWorldAccess.CharEd.XenoGenes.ClearKeepHairSkin".Translate(), PerformClearKeepHairSkin));
                actions.Add(new ScreenAction("RimWorldAccess.CharEd.XenoGenes.ApplyXenotype".Translate(), PerformApplyXenotype));
                actions.Add(new ScreenAction("CloseButton".Translate(), () => dialog.Close()));
                return actions;
            }
        }

        private void PerformAddGene()
        {
            CharEditorXenoGenesCompat.OpenAddGeneDialog(dialog);
        }

        private void PerformRemoveGene()
        {
            List<Gene> candidates = CharEditorXenoGenesCompat.RemoveGeneCandidates(dialog);
            var options = new List<FloatMenuOption>();
            foreach (Gene gene in candidates)
            {
                Gene captured = gene;
                options.Add(new FloatMenuOption(captured.LabelCap, delegate
                {
                    CharEditorXenoGenesCompat.RemoveGene(dialog, captured);
                    RebuildTree();
                    RefreshModel();
                    TolkHelper.SpeakData("RimWorldAccess.CharEd.XenoGenes.GeneRemoved".Translate(captured.LabelCap).ToString());
                }));
            }
            WindowlessFloatMenuState.OpenTitled("RimWorldAccess.CharEd.XenoGenes.RemoveGene".Translate(), options);
        }

        private void PerformClear()
        {
            ConfirmClear(keepHairAndSkin: false);
        }

        private void PerformClearKeepHairSkin()
        {
            ConfirmClear(keepHairAndSkin: true);
        }

        /// <summary>Our own confirm -- see class remarks and CharEditorXenoGenesCompat's own.</summary>
        private void ConfirmClear(bool keepHairAndSkin)
        {
            string text = "RimWorldAccess.CharEd.XenoGenes.ConfirmClear".Translate().ToString();
            Find.WindowStack.Add(Dialog_MessageBox.CreateConfirmation(text, delegate
            {
                CharEditorXenoGenesCompat.ClearGenes(dialog, keepHairAndSkin);
                RebuildTree();
                RefreshModel();
                TolkHelper.SpeakData("RimWorldAccess.CharEd.XenoGenes.Cleared".Translate().ToString());
            }, destructive: true));
        }

        private void PerformApplyXenotype()
        {
            List<XenotypeDef> defs = CharEditorXenoGenesCompat.XenotypeCandidates(dialog);
            List<CustomXenotype> customs = CharEditorXenoGenesCompat.CustomXenotypeCandidates(dialog);
            var options = new List<FloatMenuOption>();
            foreach (XenotypeDef def in defs)
            {
                XenotypeDef captured = def;
                options.Add(new FloatMenuOption(captured.LabelCap, delegate
                {
                    CharEditorXenoGenesCompat.ApplyXenotypeDef(dialog, captured);
                    RebuildTree();
                    RefreshModel();
                    TolkHelper.SpeakData("RimWorldAccess.CharEd.XenoGenes.XenotypeApplied".Translate(captured.LabelCap).ToString());
                }));
            }
            foreach (CustomXenotype custom in customs)
            {
                CustomXenotype captured = custom;
                options.Add(new FloatMenuOption(captured.name, delegate
                {
                    CharEditorXenoGenesCompat.ApplyCustomXenotype(dialog, captured);
                    RebuildTree();
                    RefreshModel();
                    TolkHelper.SpeakData("RimWorldAccess.CharEd.XenoGenes.XenotypeApplied".Translate(captured.name).ToString());
                }));
            }
            WindowlessFloatMenuState.OpenTitled("RimWorldAccess.CharEd.XenoGenes.ApplyXenotype".Translate(), options);
        }
    }
}
