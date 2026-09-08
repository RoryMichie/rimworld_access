using System.Collections.Generic;
using System.Linq;
using System.Text;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;

namespace RimWorldAccess.Shell
{
    /// <summary>
    /// Keyboard scope for <c>Dialog_ChooseMemes</c>, the structure and normal meme picker, over two
    /// regions: the meme tree (region 0) and a read-only Status row mirroring vanilla's
    /// validation/impact readout.
    ///
    /// The meme set mirrors <c>DoMemeSelector</c>'s own <c>CanUseMeme</c> filter, so a meme vanilla
    /// never draws is never presented. Structure mode is a flat list of RadioButton nodes in
    /// vanilla's sort order, with no group node since <c>MemeGroupDef</c> carries only layout
    /// offsets; Normal mode nests Checkbox nodes under one node per impact tier. Every meme expands
    /// into detail rows taken from vanilla's own hover tooltip, and a selected-but-locked meme folds
    /// in its <c>CanRemoveMeme</c> reason regardless of category, since vanilla's box-colouring gate
    /// never special-cases Structure. <see cref="CaptureWindowButtons"/> is false because the
    /// content draws its own ButtonTexts, which would miscount the meme grid.
    ///
    /// An impact tier speaks its bare name plus the expansion suffix carrying its child count, with
    /// Role/Check/Selected/Expanded unset so the state word is spoken once. A meme keeps a short
    /// Label, rides the composer's Radio/Checkbox channels with the live selection, and folds its
    /// detail text into Extras ONLY while collapsed.
    ///
    /// Never bake live state into a node: selection, cannot-remove reasons and the status line are
    /// read live in <see cref="DescribeTreeNode"/>, so the tree is built once in
    /// <see cref="OnPush"/> and a toggle never rebuilds it, leaving cursor and expansion state
    /// where the player put them. <c>CanUseMeme</c> depends on defs, dev mode, scenario and factions
    /// only, never on the selection, so the row set really is fixed for the dialog's life.
    ///
    /// Activating a Structure row only TOGGLES the selection, mirroring <c>DrawMeme</c>'s click
    /// branch, which never accepts; the player presses Done separately, as a sighted player must.
    ///
    /// <c>OwnsCancel</c> is unconditionally true rather than gated on <c>closeOnCancel</c> (which
    /// vanilla clears for the mandatory first structure pick): Escape always means Back here, and
    /// <see cref="IdeoMemeSelectionState.Back"/> already carries the structure-picker chain and the
    /// abandoned-ideo hub exit. <c>Dialog_ChooseMemes</c> overrides <c>OnAcceptKeyPressed</c>
    /// without calling base, so <c>MemeSelectionAcceptKeyRouterPatch</c> applies the accept rule to
    /// it.
    ///
    /// Same-frame guard: a fixed ideoligion's mandatory chain can open two or three of these dialogs
    /// off ONE Enter, all sharing a frame. The risk is not the dispatcher re-delivering that Enter —
    /// a claim resolves an event once — but vanilla's own per-frame
    /// <c>Window.OnAcceptKeyPressed</c> re-poll, which <c>Event.current.Use()</c> does NOT stop,
    /// calling straight into <c>TryAccept()</c>. <see cref="OnPush"/> therefore stamps
    /// <see cref="ShellFrameStamps.MarkAcceptConsumed"/> as each dialog is pushed.
    /// </summary>
    public sealed class IdeoMemeScreenScope : TreeRegionScope
    {
        private enum Region
        {
            Memes = 0,
            Status = 1,
        }

        private readonly Dialog_ChooseMemes dialog;
        private readonly MemeCategory category;
        private readonly bool structureMode;
        private readonly List<ScreenAction> actions = new List<ScreenAction>(3);
        private bool announcedOpen;

        public IdeoMemeScreenScope(Dialog_ChooseMemes dialog)
        {
            this.dialog = dialog;
            category = IdeoMemeSelectionHelper.GetMemeCategory(dialog);
            structureMode = category == MemeCategory.Structure;

            // The base's search-clear claim wins Escape first while a typeahead search is active.
            Claim(SharedMenuGrammar.Cancel, e => EscapeBack(), when: () => !TypeaheadHasActiveSearch);
            Claim(SharedMenuGrammar.Info, OnInfo);
            // Randomize and accept are claimed globally, not just from the Buttons region.
            Claim("ideoMemeSelection.randomize", e => RandomizeAction());
            Claim("ideoMemeSelection.accept", e => DoneAction());
            // Page Up/Down between impact tiers: the base leaves this pair unclaimed, so opt in on
            // the globally-registered tree.* ids.
            Claim("tree.jumpToPreviousSection", e => PerformJumpToAdjacentSection(false));
            Claim("tree.jumpToNextSection", e => PerformJumpToAdjacentSection(true));
        }

        public override string Name
        {
            get { return "ideo-meme-selection"; }
        }

        protected internal override Window OwnedWindow
        {
            get { return dialog; }
        }

        protected override bool ContentRegionSearchable(int region)
        {
            return (Region)region == Region.Memes;
        }

        /// <summary>The dialog draws its own Back/Randomize/Done ButtonTexts; declaring them keeps the meme grid from being miscounted.</summary>
        protected override bool CaptureWindowButtons
        {
            get { return false; }
        }

        /// <summary>Surface anything vanilla or a mod draws that the typed regions do not present.</summary>
        protected override bool IncludeCapturedExtrasRegion
        {
            get { return true; }
        }

        /// <summary>Alt+S is this screen's Done chord, and there is no table region to sort anyway.</summary>
        protected override bool EnableSortChord
        {
            get { return false; }
        }

        public override bool OwnsCancel
        {
            get { return true; }
        }

        public override void OnPush()
        {
            base.OnPush();
            MemeRowDrawPatch.Recording = true;
            ShellFrameStamps.MarkAcceptConsumed();
            SetTreeRoot(IdeoMemeSelectionHelper.BuildTree(dialog));
        }

        // The tree is region 0 and the status readout region 1. TreeRegionScope's implementations
        // ignore their region argument, so region-0 members pass 0 straight through to the base.

        protected override string TreeRegionLabel
        {
            get { return (string)"RimWorldAccess.Ideology.Memes.MemesRegion".Translate(); }
        }

        protected override int ContentRegionCount
        {
            get { return 2; }
        }

        protected override string ContentRegionName(int region)
        {
            return (Region)region == Region.Memes
                ? base.ContentRegionName(0)
                : (string)"RimWorldAccess.Ideology.Memes.StatusRegion".Translate();
        }

        protected override int ContentItemCount(int region)
        {
            return (Region)region == Region.Memes ? base.ContentItemCount(0) : 1;
        }

        protected override ElementDescription DescribeContentItem(int region, int index)
        {
            return (Region)region == Region.Memes
                ? base.DescribeContentItem(0, index)
                : DescribeStatusRow();
        }

        protected override void ActivateContentItem(int region, int index)
        {
            if ((Region)region == Region.Memes)
            {
                base.ActivateContentItem(0, index);
            }
            else
            {
                AnnounceCurrentItem();
            }
        }

        protected override bool CanAdjustContentItem(int region, int index)
        {
            return (Region)region == Region.Memes && base.CanAdjustContentItem(0, index);
        }

        protected override void AdjustContentItem(int region, int index, int direction)
        {
            if ((Region)region == Region.Memes)
            {
                base.AdjustContentItem(0, index, direction);
            }
        }

        /// <summary>
        /// Rebuilding is a no-op by design: the row set is fixed for the dialog's life and every
        /// selection change is read live. The base call is non-negotiable — it restores the
        /// pre-search expansion state.
        /// </summary>
        protected override void RefreshContent()
        {
            base.RefreshContent();
            if (Tree.Root == null)
            {
                SetTreeRoot(IdeoMemeSelectionHelper.BuildTree(dialog));
            }
        }

        private ElementDescription DescribeStatusRow()
        {
            return new ElementDescription
            {
                Label = (string)"RimWorldAccess.Ideology.Memes.StatusLabel".Translate(),
                Extras = IdeoMemeSelectionHelper.BuildStatusLine(dialog),
            };
        }

        // Tree hooks. Level and PositionIndex are deliberately left unset — the base fills both,
        // sibling-relative.

        protected override ElementDescription DescribeTreeNode(InspectionTreeItem item)
        {
            if (item.Data is int)
            {
                // An impact tier: the expansion suffix is the ONE channel carrying the child count,
                // so Role/Check/Selected/Expanded all stay unset.
                return new ElementDescription
                {
                    Label = item.Label + TreeNavigationHelper.FormatExpansionSuffix(item, includeChildCount: true),
                };
            }

            var meme = item.Data as MemeDef;
            if (meme == null)
            {
                // A detail line: plain text, no role and no ReadOnly flag.
                return new ElementDescription { Label = item.Label };
            }

            List<MemeDef> newMemes = IdeoMemeSelectionHelper.GetNewMemes(dialog);
            bool selected = newMemes != null && newMemes.Contains(meme);

            var d = new ElementDescription { Label = item.Label };
            if (structureMode)
            {
                d.Role = ElementRole.RadioButton;
                d.Selected = selected;
            }
            else
            {
                d.Role = ElementRole.Checkbox;
                d.Check = selected ? CheckState.Checked : CheckState.Unchecked;
            }
            if (item.IsExpandable)
            {
                d.Expanded = item.IsExpanded;
            }

            var extrasParts = new List<string>();
            if (!item.IsExpanded)
            {
                // Expanded, the child rows carry these lines, so folding them in would double-speak.
                foreach (InspectionTreeItem child in item.Children)
                {
                    extrasParts.Add(child.Label);
                }
            }
            if (selected)
            {
                // Vanilla's "can't remove" box is driven by CanRemoveMeme alone, independent of
                // category: a locked structure meme gets the same box a Normal meme would.
                AcceptanceReport report = IdeoMemeSelectionHelper.CanRemoveMeme(dialog, meme);
                if (!report.Accepted && !string.IsNullOrEmpty(report.Reason))
                {
                    extrasParts.Add(report.Reason);
                }
            }
            // The hint text carries its own leading space and trailing period.
            d.Extras = string.Join(". ", extrasParts) + (string)"RimWorldAccess.InfoCard.Inspectable".Translate();
            return d;
        }

        protected override void ActivateTreeNode(InspectionTreeItem item)
        {
            var meme = item.Data as MemeDef;
            if (meme == null)
            {
                // An impact tier or detail line has nothing to toggle; Left/Right handle expansion.
                AnnounceCurrentItem();
                return;
            }
            IdeoMemeSelectionState.ToggleOutcome outcome = IdeoMemeSelectionState.ToggleMeme(meme);
            if (!outcome.Accepted)
            {
                if (!string.IsNullOrEmpty(outcome.RejectReason))
                {
                    TolkHelper.SpeakData(outcome.RejectReason, outcome.RejectPriority ?? SpeechPriority.Normal);
                }
                return;
            }
            RefreshModel();
            AnnounceToggle(outcome);
        }

        /// <summary>
        /// STRUCTURE MODE ONLY, the radio-group contract: landing on a structure meme selects it
        /// silently through the same <see cref="IdeoMemeSelectionState.ToggleMeme"/> vehicle Enter
        /// rides. Normal mode is deliberately untouched — those memes are checkboxes, and a checkbox
        /// must never toggle just because the cursor arrived. A rejected toggle stays silent here
        /// (Enter is where a reject reason is spoken), and an already-selected meme is skipped, which
        /// keeps the "can't deselect" reject out of the browsing path.
        /// </summary>
        protected override void OnCursorSettled(int region, int index)
        {
            if (!structureMode || (Region)region != Region.Memes)
            {
                return;
            }
            int treeIndex = index - PrefixRowCount;
            if (treeIndex < 0 || treeIndex >= Tree.Count)
            {
                return;
            }
            var meme = Tree.Visible[treeIndex].Data as MemeDef;
            if (meme == null)
            {
                return;
            }
            List<MemeDef> newMemes = IdeoMemeSelectionHelper.GetNewMemes(dialog);
            if (newMemes != null && newMemes.Contains(meme))
            {
                return;
            }
            IdeoMemeSelectionState.ToggleMeme(meme, playSounds: false);
        }

        /// <summary>Impact tiers open on the first typed character so memes inside them stay reachable.</summary>
        protected override bool ShouldAutoExpandForSearch(InspectionTreeItem item)
        {
            return item.Data is int;
        }

        /// <summary>
        /// Detail rows stay navigable and spoken but out of the match set, keeping typeahead at the
        /// tier/meme level rather than burying targets under tooltip lines.
        /// </summary>
        protected override bool ContentRowSearchable(int region, int row)
        {
            if ((Region)region != Region.Memes)
            {
                return base.ContentRowSearchable(region, row);
            }
            int treeIndex = row - PrefixRowCount;
            if (treeIndex < 0 || treeIndex >= Tree.Count)
            {
                return false;
            }
            return Tree.Visible[treeIndex].Type != InspectionTreeItem.ItemType.DetailText;
        }

        /// <summary>
        /// Typeahead matches a node's label rather than its composed announcement: a tier's spoken
        /// label carries the expansion suffix and child count, which must never join the haystack.
        /// </summary>
        protected override string ContentRowSearchText(int region, int row)
        {
            if ((Region)region == Region.Memes)
            {
                int treeIndex = row - PrefixRowCount;
                if (treeIndex >= 0 && treeIndex < Tree.Count)
                {
                    return Tree.Visible[treeIndex].Label ?? "";
                }
            }
            return base.ContentRowSearchText(region, row);
        }

        /// <summary>Page Up/Down jump between impact tiers; structure mode's flat list has no sections, so it rejects.</summary>
        protected override bool IsSectionBoundary(InspectionTreeItem item)
        {
            return !structureMode && item.IndentLevel == 0;
        }

        /// <summary>
        /// One utterance carrying only changed state: the toggled row's new state, any memes a
        /// single-select swap displaced, and the fresh validation/impact status line.
        /// </summary>
        private void AnnounceToggle(IdeoMemeSelectionState.ToggleOutcome outcome)
        {
            var d = new ElementDescription();
            if (structureMode)
            {
                d.Selected = outcome.NowSelected;
            }
            else
            {
                d.Check = outcome.NowSelected ? CheckState.Checked : CheckState.Unchecked;
            }
            var sb = new StringBuilder(AnnouncementComposer.ComposeStateChange(d, TranslatedShellVocabulary.Instance));
            foreach (MemeDef displaced in outcome.Displaced)
            {
                var dd = new ElementDescription();
                if (structureMode)
                {
                    dd.Selected = false;
                }
                else
                {
                    dd.Check = CheckState.Unchecked;
                }
                sb.Append(". ").Append(displaced.LabelCap).Append(", ")
                    .Append(AnnouncementComposer.ComposeStateChange(dd, TranslatedShellVocabulary.Instance));
            }
            string status = IdeoMemeSelectionHelper.BuildStatusLine(dialog);
            if (!string.IsNullOrEmpty(status))
            {
                sb.Append(". ").Append(status);
            }
            TolkHelper.SpeakData(sb.ToString());
        }

        /// <summary>Done is this page's proceed button.</summary>
        protected override string DefaultAcceptActionId
        {
            get { return "ideoMemeSelection.accept"; }
        }

        protected override IReadOnlyList<ScreenAction> DeclaredActions
        {
            get
            {
                actions.Clear();
                actions.Add(new ScreenAction("Back".Translate(), BackAction, SharedMenuGrammar.Cancel));
                actions.Add(new ScreenAction("Randomize".Translate(), RandomizeAction, "ideoMemeSelection.randomize"));
                actions.Add(new ScreenAction("DoneButton".Translate(), DoneAction, "ideoMemeSelection.accept"));
                return actions;
            }
        }

        private void EscapeBack()
        {
            ShellFrameStamps.MarkCancelConsumed();
            BackAction();
        }

        private void BackAction()
        {
            IdeoMemeSelectionState.Back();
        }

        private void RandomizeAction()
        {
            MemeDef chosen = IdeoMemeSelectionState.Randomize();
            RefreshModel();
            if (chosen == null)
            {
                return;
            }
            InspectionTreeItem node = FindMemeNode(chosen);
            if (node == null)
            {
                return;
            }
            // Randomize already spoke the full result, so move the cursor silently — through
            // TryRevealAndSelect, since the rolled meme may sit inside a collapsed impact tier.
            Model.MoveToRegion((int)Region.Memes);
            if (TryRevealAndSelect(node))
            {
                SyncRegionFromCurrentTree();
            }
        }

        private void DoneAction()
        {
            IdeoMemeSelectionState.Accept();
        }

        /// <summary>Every meme node in the built tree, in draw order (flat in structure mode, tier by tier otherwise).</summary>
        private IEnumerable<InspectionTreeItem> MemeNodes()
        {
            if (Tree.Root == null)
            {
                yield break;
            }
            foreach (InspectionTreeItem child in Tree.Root.Children)
            {
                if (child.Data is MemeDef)
                {
                    yield return child;
                    continue;
                }
                foreach (InspectionTreeItem grandChild in child.Children)
                {
                    if (grandChild.Data is MemeDef)
                    {
                        yield return grandChild;
                    }
                }
            }
        }

        private InspectionTreeItem FindMemeNode(MemeDef meme)
        {
            foreach (InspectionTreeItem node in MemeNodes())
            {
                if (ReferenceEquals(node.Data, meme))
                {
                    return node;
                }
            }
            return null;
        }

        /// <summary>
        /// Vanilla never opens Dialog_InfoCard for a MemeDef, so Alt+I always speaks the standard
        /// refusal rather than fabricating a card. Nothing is lost: the full hover-tip content is
        /// already read on the row itself, expanded into its detail children.
        /// </summary>
        private void OnInfo(KeyEventSnapshot e)
        {
            RefreshModel();
            InfoCardState.SpeakNoInfoCardAvailable();
        }

        protected override IEnumerable<string> AdditionalPresentedTexts
        {
            get
            {
                bool configuringNewFluid = IdeoMemeSelectionHelper.GetConfiguringNewFluidIdeo(dialog);
                bool reformingFluid = IdeoMemeSelectionHelper.GetReformingFluidIdeo(dialog);

                // Title + info paragraphs are spoken by the opening announcement; deliberate mirrors.
                yield return structureMode
                    ? (string)"ChooseStructure".Translate()
                    : (configuringNewFluid ? (string)"ChooseStartingMeme".Translate() : (string)"ChooseMemes".Translate());
                if (structureMode)
                {
                    yield return (string)"ChooseStructureMemesInfo".Translate();
                }
                else if (configuringNewFluid)
                {
                    yield return (string)"ChooseNormalMemesFluidIdeoInfo".Translate(IdeoMemeSelectionHelper.GetMemeCountRangeAbsolute(dialog).min);
                }
                else if (reformingFluid)
                {
                    yield return (string)"ChooseOrRemoveMeme".Translate();
                }
                else
                {
                    IntRange range = IdeoMemeSelectionHelper.GetMemeCountRangeAbsolute(dialog);
                    yield return (string)"ChooseNormalMemesInfo".Translate(range.min, range.max);
                }
                yield return (string)"SomeMemesHaveMoreImpact".Translate();

                // The Status region already speaks this, but vanilla also draws it as its own
                // bottom-right label, whose capture would otherwise surface as an extra.
                string status = IdeoMemeSelectionHelper.BuildStatusLine(dialog);
                if (!string.IsNullOrEmpty(status))
                {
                    yield return status;
                }

                // The meme grid fuses several cards onto one captured line per visual row, so these
                // are bare-name composites in draw order per tier: any fused subset then normalizes
                // to a substring. CaptureTextNormalization strips punctuation and whitespace, so only
                // draw order matters, not the separator. Detail children add nothing — vanilla draws
                // the tip only on hover.
                foreach (string composite in PresentedMemeComposites())
                {
                    yield return composite;
                }

                // The generic reader labels an image button with its texture's own name.
                foreach (InspectionTreeItem node in MemeNodes())
                {
                    var meme = (MemeDef)node.Data;
                    if (meme.Icon != null && !string.IsNullOrEmpty(meme.Icon.name))
                    {
                        yield return meme.Icon.name;
                    }
                }
            }
        }

        private IEnumerable<string> PresentedMemeComposites()
        {
            if (Tree.Root == null)
            {
                yield break;
            }
            if (structureMode)
            {
                string flat = JoinMemeNames(Tree.Root.Children);
                if (!string.IsNullOrEmpty(flat))
                {
                    yield return flat;
                }
                yield break;
            }
            foreach (InspectionTreeItem tier in Tree.Root.Children)
            {
                string composite = JoinMemeNames(tier.Children);
                if (!string.IsNullOrEmpty(composite))
                {
                    yield return composite;
                }
            }
        }

        private static string JoinMemeNames(List<InspectionTreeItem> nodes)
        {
            return string.Join(", ", nodes
                .Where(n => n.Data is MemeDef)
                .Select(n => ((MemeDef)n.Data).LabelCap.ToString()));
        }

        public override void OnFocus()
        {
            base.OnFocus();
            if (announcedOpen)
            {
                return;
            }
            announcedOpen = true;

            bool configuringNewFluid = IdeoMemeSelectionHelper.GetConfiguringNewFluidIdeo(dialog);
            bool reformingFluid = IdeoMemeSelectionHelper.GetReformingFluidIdeo(dialog);

            string title = structureMode
                ? (string)"ChooseStructure".Translate()
                : (configuringNewFluid ? (string)"ChooseStartingMeme".Translate() : (string)"ChooseMemes".Translate());

            string info;
            if (structureMode)
            {
                info = (string)"ChooseStructureMemesInfo".Translate();
            }
            else if (configuringNewFluid)
            {
                info = (string)"ChooseNormalMemesFluidIdeoInfo".Translate(IdeoMemeSelectionHelper.GetMemeCountRangeAbsolute(dialog).min);
            }
            else if (reformingFluid)
            {
                info = (string)"ChooseOrRemoveMeme".Translate() + " " + (string)"SomeMemesHaveMoreImpact".Translate();
            }
            else
            {
                IntRange range = IdeoMemeSelectionHelper.GetMemeCountRangeAbsolute(dialog);
                info = (string)"ChooseNormalMemesInfo".Translate(range.min, range.max) + " " + (string)"SomeMemesHaveMoreImpact".Translate();
            }

            string tabCount = TabCountFragment();
            string opening = string.IsNullOrEmpty(tabCount) ? title : tabCount + ". " + title;
            opening += ". " + info;
            string status = IdeoMemeSelectionHelper.BuildStatusLine(dialog);
            if (!string.IsNullOrEmpty(status))
            {
                opening += ". " + status;
            }
            TolkHelper.SpeakData(opening, SpeechPriority.High);
            AnnounceCurrentItem();
        }

        public override void OnPop()
        {
            MemeRowDrawPatch.Recording = false;
            base.OnPop();
        }

        protected internal override Rect FocusedContentRect()
        {
            if ((Region)Model.RegionIndex != Region.Memes)
            {
                return default(Rect);
            }
            InspectionTreeItem item = CurrentTreeItem();
            MemeDef meme = item?.Data as MemeDef;
            // Impact-tier and detail rows have no vanilla row method, so no rect.
            return meme == null ? default(Rect) : MemeRowDrawPatch.Rows.FindLast(meme);
        }
    }

    /// <summary>Records each meme box's rect; the MemeDef vanilla hands its own row method is the identity.</summary>
    [HarmonyPatch(typeof(Dialog_ChooseMemes), "DrawMeme")]
    internal static class MemeRowDrawPatch
    {
        internal static readonly RowDrawCapture Rows = new RowDrawCapture();

        /// <summary>Set while an <see cref="IdeoMemeScreenScope"/> drives; otherwise the postfix is one static read.</summary>
        internal static bool Recording;

        [HarmonyPostfix]
        public static void Postfix(MemeDef meme, Rect memeBox)
        {
            if (Recording)
            {
                Rows.Record(meme, memeBox);
            }
        }
    }
}
