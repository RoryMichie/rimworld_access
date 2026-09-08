using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.Sound;

namespace RimWorldAccess.Shell
{
    /// <summary>
    /// Keyboard scope for vanilla's <see cref="Dialog_IdeosDuringLanding"/> (the Odyssey landing-time
    /// ideoligion reconcile dialog). Its body is byte-identical to the F12 Ideology tab's — both call
    /// <c>IdeoUIUtility.DoIdeoListAndDetails</c> with the same arguments — so this scope is the twin
    /// of <see cref="IdeologyViewerScreenScope"/>, built from the same <see cref="IdeologyHelper"/>
    /// and <see cref="IdeoDetailsTreeRegion"/> components. The ideology hosts stay parallel rather
    /// than sharing a base: each differs in list rows, buttons and lifecycle, and the shared parts
    /// already live in those two components.
    /// Two content regions — List (one row per <c>IdeosInViewOrder</c> entry) and Details (the browsed
    /// ideo's tree) — plus a Buttons region with Close and, in dev mode, vanilla's two embedded DEV
    /// checkboxes. The only interactive widget is the list row click, which writes
    /// <c>IdeoUIUtility.selected</c> behind <c>TutorSystem.AllowAction("ConfiguringIdeo")</c>; every
    /// list move re-selects immediately, as the click does, rather than adding a confirm step vanilla
    /// never had. The details tree is read-only here (editMode and allowLoad both false) and is
    /// presented in full rather than reduced to a name list.
    /// The dialog's ctor leaves <c>closeOnCancel</c>/<c>closeOnAccept</c> at Window's TRUE default, so
    /// vanilla's own <c>OnCancelKeyPressed</c> is live: <see cref="OwnsCancel"/> stays at the chassis
    /// default (false except during a search, when Escape clears it) and an idle Escape rides
    /// vanilla's close path. <see cref="OwnsAccept"/> stays true, or vanilla's live Enter would close
    /// the dialog out from under list/tree navigation.
    /// <c>ideosLanding.nextPanel</c>/<c>previousPanel</c>/<c>contextMenu</c> are dormant: region
    /// cycling is base grammar and the dev toggles moved into the Buttons region.
    /// </summary>
    public sealed class IdeosDuringLandingScope : ScreenScope
    {
        private const int ListRegion = 0;
        private const int DetailsRegion = 1;

        private readonly Dialog_IdeosDuringLanding dialog;
        private readonly IdeoDetailsTreeRegion detailsTree = new IdeoDetailsTreeRegion();
        private readonly List<ScreenAction> actions = new List<ScreenAction>(3);
        private List<Ideo> ideologies = new List<Ideo>();
        private Ideo detailsIdeo;
        private bool positionedOnOpen;

        // IdeoUIUtility's static "DEV: Show all" / "DEV: Edit mode" flags: they persist across window
        // instances, so they are read live rather than reset per open.
        private static readonly FieldInfo ShowAllField = AccessTools.Field(typeof(IdeoUIUtility), "showAll");
        private static readonly FieldInfo DevEditModeField = AccessTools.Field(typeof(IdeoUIUtility), "devEditMode");

        public IdeosDuringLandingScope(Dialog_IdeosDuringLanding dialog)
        {
            this.dialog = dialog;

            Claim(SharedMenuGrammar.Info, OnInfo);
            Claim("ideosLanding.firstAbsolute", e => DetailsEdgeJump(true, true), when: InDetailsRegion);
            Claim("ideosLanding.lastAbsolute", e => DetailsEdgeJump(false, true), when: InDetailsRegion);
            Claim("ideosLanding.jumpToPreviousSection", e => DetailsJumpSection(false), when: InDetailsRegion);
            Claim("ideosLanding.jumpToNextSection", e => DetailsJumpSection(true), when: InDetailsRegion);
            Claim("ideosLanding.expandAllSiblings", e => DetailsExpandAllSiblings(), when: InDetailsRegion);
        }

        public override string Name
        {
            get { return "ideos-during-landing"; }
        }

        protected internal override Window OwnedWindow
        {
            get { return dialog; }
        }

        protected override bool EnableTypeahead
        {
            get { return true; }
        }

        /// <summary>
        /// The details pane draws its own vanilla ButtonTexts under labels this scope's typed tree rows
        /// already present, so capturing them would double-present. The Buttons region is
        /// <see cref="DeclaredActions"/> alone.
        /// </summary>
        protected override bool CaptureWindowButtons
        {
            get { return false; }
        }

        // Content model.

        protected override void RefreshContent()
        {
            ideologies = IdeologyHelper.BuildIdeologyList();
            RebuildDetailsTreeIfStale();
        }

        private Ideo TargetIdeo
        {
            get { return IdeoUIUtility.selected ?? IdeoUIUtility.FallbackSelectedIdeo; }
        }

        /// <summary>
        /// Rebuilds the Details tree SILENTLY when the browsed ideo's reference changes; a no-op
        /// otherwise, so the incidental <see cref="ScreenScope.RefreshModel"/> calls nearly every claim
        /// makes never reset expansion state or cursor position on the SAME ideo.
        /// </summary>
        private void RebuildDetailsTreeIfStale()
        {
            Ideo target = ideologies.Count > 0 ? TargetIdeo : null;
            if (ReferenceEquals(detailsIdeo, target))
            {
                return;
            }
            detailsIdeo = target;
            if (target == null)
            {
                return;
            }
            detailsTree.SetRoot(IdeologyHelper.BuildIdeologyTree(target), WrapItems);
        }

        protected override int ContentRegionCount
        {
            get { return 2; }
        }

        protected override string ContentRegionName(int region)
        {
            return region == ListRegion
                ? (string)"RimWorldAccess.Ideology.Viewer.ListRegion".Translate()
                : (string)"RimWorldAccess.Ideology.Viewer.DetailsRegion".Translate();
        }

        protected override int ContentItemCount(int region)
        {
            return region == ListRegion ? ideologies.Count : detailsTree.Tree.Count;
        }

        protected override ElementDescription DescribeContentItem(int region, int index)
        {
            return region == ListRegion
                ? DescribeIdeoRow(index)
                : detailsTree.Describe(detailsTree.ItemAt(index));
        }

        protected override string ContentRowSearchText(int region, int index)
        {
            // The details tree's state word rides its label channel, so search the bare label.
            if (region == DetailsRegion)
            {
                InspectionTreeItem item = detailsTree.ItemAt(index);
                if (item != null)
                {
                    return detailsTree.SearchText(item);
                }
            }
            return base.ContentRowSearchText(region, index);
        }

        protected override void ActivateContentItem(int region, int index)
        {
            if (region == ListRegion)
            {
                // Selection is already live-synced on cursor move, so Enter is a value change rather
                // than a fresh focus: value-only announce.
                TolkHelper.SpeakData(AnnouncementComposer.ComposeStateChange(
                    new ElementDescription { Selected = true }, TranslatedShellVocabulary.Instance));
                return;
            }
            ActivateDetailsRow(index);
        }

        private ElementDescription DescribeIdeoRow(int index)
        {
            var d = new ElementDescription();
            if (index < 0 || index >= ideologies.Count)
            {
                return d;
            }
            // Sync BEFORE reading TargetIdeo, gated to the row the cursor rests on, so a fresh
            // announce's Selected flag reflects the row it is describing.
            ListModel cursor = Model.Region(ListRegion);
            if (cursor != null && cursor.Index == index)
            {
                SelectCurrentIdeoSilently();
            }
            Ideo ideo = ideologies[index];
            d.Label = IdeologyHelper.BuildIdeoListAnnouncement(ideo);
            d.Role = ElementRole.RadioButton;
            d.Selected = ReferenceEquals(ideo, TargetIdeo);
            return d;
        }

        /// <summary>
        /// Mirrors <c>IdeoUIUtility.DrawIdeoRow</c>'s click branch (:502-509): honor the same tutor
        /// gate, then call the same public setter. Silent — the caller speaks the row.
        /// </summary>
        private void SelectCurrentIdeoSilently()
        {
            ListModel region = Model.Region(ListRegion);
            if (region == null || region.IsEmpty || region.Index < 0 || region.Index >= ideologies.Count)
            {
                return;
            }
            Ideo ideo = ideologies[region.Index];
            if (IdeoUIUtility.selected == ideo)
            {
                return;
            }
            if (TutorSystem.AllowAction("ConfiguringIdeo"))
            {
                IdeoUIUtility.SetSelected(ideo);
            }
        }

        // Details region: the tree's own chords.

        private bool InDetailsRegion()
        {
            return Model.RegionIndex == DetailsRegion;
        }

        protected override bool CanAdjustContentItem(int region, int index)
        {
            return region == DetailsRegion;
        }

        protected override void AdjustContentItem(int region, int index, int direction)
        {
            if (region != DetailsRegion || detailsTree.ItemAt(index) == null)
            {
                return;
            }
            // Left/Right expand or collapse; never a match move, matching the tree family.
            TypeaheadReset();
            detailsTree.Tree.SetSelectedIndex(index);
            if (direction > 0)
            {
                PerformDetailsExpand();
            }
            else
            {
                PerformDetailsCollapse();
            }
        }

        /// <summary>Home/End: sibling-scoped inside the Details tree (the tree family's own shape), the plain flat jump everywhere else.</summary>
        protected override void MoveItemEdge(bool first)
        {
            if (InDetailsRegion())
            {
                DetailsEdgeJump(first, false);
                return;
            }
            base.MoveItemEdge(first);
        }

        /// <summary>
        /// Enter on a Details row, in the tree's precedence order: the ideology-specific branches
        /// (ritual-sound toggle, reform hand-off) first, then expand/collapse for an expandable row —
        /// the one place Enter also COLLAPSES, where Right only ever expands.
        /// </summary>
        private void ActivateDetailsRow(int index)
        {
            InspectionTreeItem item = detailsTree.ItemAt(index);
            if (item == null)
            {
                return;
            }
            detailsTree.Tree.SetSelectedIndex(index);
            if (detailsTree.TryActivateSpecial(item))
            {
                return;
            }
            if (item.OnActivate != null)
            {
                item.OnActivate();
                return;
            }
            if (!item.IsExpandable)
            {
                return;
            }
            if (detailsTree.Tree.SubmenuMode)
            {
                PerformDetailsExpand();
                return;
            }
            item.IsExpanded = !item.IsExpanded;
            detailsTree.Tree.Reflatten();
            (item.IsExpanded ? SoundDefOf.FloatMenu_Open : SoundDefOf.FloatMenu_Cancel).PlayOneShotOnCamera();
            SyncDetailsAndAnnounce();
        }

        private void PerformDetailsExpand()
        {
            TreeActionResult<InspectionTreeItem> result = detailsTree.Tree.ExpandOrDrillDown();
            switch (result.Kind)
            {
                case TreeActionKind.Rejected:
                    SoundDefOf.ClickReject.PlayOneShotOnCamera();
                    break;
                case TreeActionKind.Expanded:
                case TreeActionKind.ExpandedSubmenu:
                    SoundDefOf.FloatMenu_Open.PlayOneShotOnCamera();
                    SyncDetailsAndAnnounce();
                    break;
                case TreeActionKind.DrilledToChild:
                    SoundDefOf.Tick_Tiny.PlayOneShotOnCamera();
                    SyncDetailsAndAnnounce();
                    break;
            }
        }

        private void PerformDetailsCollapse()
        {
            TreeActionResult<InspectionTreeItem> result = detailsTree.Tree.CollapseOrDrillUp();
            switch (result.Kind)
            {
                case TreeActionKind.Rejected:
                    SoundDefOf.ClickReject.PlayOneShotOnCamera();
                    break;
                case TreeActionKind.Collapsed:
                case TreeActionKind.CollapsedToParent:
                    SoundDefOf.FloatMenu_Cancel.PlayOneShotOnCamera();
                    SyncDetailsAndAnnounce();
                    break;
                case TreeActionKind.DrilledToParent:
                    SoundDefOf.Tick_Tiny.PlayOneShotOnCamera();
                    SyncDetailsAndAnnounce();
                    break;
            }
        }

        /// <summary>Refresh first: an expand/collapse changes the row count, so the region's own count must be current before its cursor can be moved onto the tree's.</summary>
        private void SyncDetailsAndAnnounce()
        {
            RefreshModel();
            SyncDetailsRegionFromTree();
            AnnounceCurrentItem();
        }

        private void SyncDetailsRegionFromTree()
        {
            ListModel region = Model.Region(DetailsRegion);
            if (region == null || region.IsEmpty)
            {
                return;
            }
            int target = detailsTree.Tree.SelectedIndex;
            if (target >= 0 && target < region.Count)
            {
                region.MoveTo(target);
            }
            RequestDetailsFollow();
        }

        /// <summary>Home/End (sibling-scoped) and Ctrl+Home/Ctrl+End (absolute) on the Details tree; both clear an active search rather than jumping to its first/last match, matching the tree family's established behaviour.</summary>
        private void DetailsEdgeJump(bool first, bool absolute)
        {
            TypeaheadReset();
            RefreshModel();
            ListModel region = Model.CurrentRegion;
            if (region == null || region.IsEmpty)
            {
                return;
            }
            detailsTree.Tree.SetSelectedIndex(region.Index);
            MoveResult result = first ? detailsTree.Tree.HomeKey(absolute) : detailsTree.Tree.EndKey(absolute);
            if (result.Kind == MoveKind.Empty)
            {
                return;
            }
            SyncDetailsRegionFromTree();
            SoundDefOf.Tick_Tiny.PlayOneShotOnCamera();
            AnnounceCurrent(CellAxis.Row);
        }

        /// <summary>Page Up/Down between the tree's level-0 section headers; rejects during a search, exactly as the tree family does.</summary>
        private void DetailsJumpSection(bool forward)
        {
            RefreshModel();
            ListModel region = Model.CurrentRegion;
            if (TypeaheadHasActiveSearch || region == null || region.IsEmpty)
            {
                SoundDefOf.ClickReject.PlayOneShotOnCamera();
                return;
            }
            detailsTree.Tree.SetSelectedIndex(region.Index);
            if (!detailsTree.JumpToAdjacentSection(forward))
            {
                SoundDefOf.ClickReject.PlayOneShotOnCamera();
                return;
            }
            SyncDetailsRegionFromTree();
            SoundDefOf.Tick_Tiny.PlayOneShotOnCamera();
            AnnounceCurrentItem();
        }

        /// <summary>'*': expands every expandable sibling at the focused row's level.</summary>
        private void DetailsExpandAllSiblings()
        {
            RefreshModel();
            ListModel region = Model.CurrentRegion;
            if (region == null || region.IsEmpty)
            {
                SoundDefOf.ClickReject.PlayOneShotOnCamera();
                return;
            }
            detailsTree.Tree.SetSelectedIndex(region.Index);
            ExpandSiblingsResult result = detailsTree.Tree.ExpandAllSiblings();
            if (result.ExpandedCount > 0)
            {
                TypeaheadReset();
                EmbeddedAudioHelper.PlaySoundDefWithReverb(SoundDefOf.FloatMenu_Open);
                TolkHelper.Speak((result.ExpandedCount == 1
                    ? "RimWorldAccess.Tree.ExpandedCountOne"
                    : "RimWorldAccess.Tree.ExpandedCountMany").Loc(result.ExpandedCount));
                RefreshModel();
                SyncDetailsRegionFromTree();
                if (detailsTree.Tree.SubmenuMode)
                {
                    AnnounceCurrentItem();
                }
            }
            else
            {
                SoundDefOf.ClickReject.PlayOneShotOnCamera();
                TolkHelper.Speak((result.AnyExpandable
                    ? "RimWorldAccess.Tree.AllAlreadyExpanded"
                    : "RimWorldAccess.Tree.NoneToExpand").Loc());
            }
        }

        private void OnInfo(KeyEventSnapshot e)
        {
            RefreshModel();
            if (!InDetailsRegion())
            {
                InfoCardState.SpeakNoInfoCardAvailable();
                return;
            }
            ListModel region = Model.CurrentRegion;
            detailsTree.OpenInfoCard(region != null && !region.IsEmpty ? detailsTree.ItemAt(region.Index) : null);
        }

        // Details-pane focus ring and scroll follow (see IdeoBoxDrawPatch).

        /// <summary>The vanilla box the Details cursor is on, or null when the List panel is focused or the row has no box.</summary>
        private object DetailsRingIdentity()
        {
            if (!InDetailsRegion())
            {
                return null;
            }
            ListModel region = Model.CurrentRegion;
            if (region == null || region.IsEmpty)
            {
                return null;
            }
            return IdeoBoxDrawPatch.RingIdentityFor(detailsTree.ItemAt(region.Index), detailsTree.Tree.Root);
        }

        protected internal override Rect FocusedContentRect()
        {
            return IdeoBoxDrawPatch.ScreenRectFor(DetailsRingIdentity());
        }

        private Rect? FollowTargetRect()
        {
            return IdeoBoxDrawPatch.RawRectFor(DetailsRingIdentity());
        }

        protected override void OnCursorSettled(int region, int index)
        {
            base.OnCursorSettled(region, index);
            RequestDetailsFollow();
        }

        private void RequestDetailsFollow()
        {
            if (InDetailsRegion())
            {
                IdeoBoxDrawPatch.RequestFollow();
            }
        }

        // Buttons region: Close / dev toggles.

        protected override IReadOnlyList<ScreenAction> DeclaredActions
        {
            get
            {
                actions.Clear();
                if (Prefs.DevMode)
                {
                    // Vanilla's own two embedded checkboxes (IdeoUIUtility.cs:254-255, drawn by the
                    // same DoIdeoListAndDetails this scope reuses), presented as genuine checkbox rows
                    // so their live state is audible on arrival. "Show all" does NOT reveal hidden
                    // ideos in the list (IdeosInViewOrder filters !hidden unconditionally), and "Edit
                    // mode" enables an editor surface this dialog does not model.
                    actions.Add(new ScreenAction(
                        "DEV: Show all", // l10n-exempt: dev-only diagnostic label, matches IdeoUIUtility's own unlocalized dev checkbox text verbatim.
                        ToggleShowAll,
                        check: CurrentShowAllCheckState()));
                    actions.Add(new ScreenAction(
                        "DEV: Edit mode", // l10n-exempt: same precedent.
                        ToggleEditMode,
                        check: CurrentEditModeCheckState()));
                }
                actions.Add(new ScreenAction((string)"CloseButton".Translate(), CloseAction));
                return actions;
            }
        }

        private CheckState CurrentShowAllCheckState()
        {
            return ShowAllField != null && (bool)ShowAllField.GetValue(null) ? CheckState.Checked : CheckState.Unchecked;
        }

        private CheckState CurrentEditModeCheckState()
        {
            return DevEditModeField != null && (bool)DevEditModeField.GetValue(null) ? CheckState.Checked : CheckState.Unchecked;
        }

        private void ToggleShowAll()
        {
            bool next = !(ShowAllField != null && (bool)ShowAllField.GetValue(null));
            // MUTATION-C: mirrors IdeoUIUtility's "DEV: Show all" CheckboxLabeled,
            // which writes the private static showAll by ref (decompiled :254); no
            // gated setter exists.
            ShowAllField?.SetValue(null, next);
            AnnounceDevToggle(next);
        }

        private void ToggleEditMode()
        {
            bool next = !(DevEditModeField != null && (bool)DevEditModeField.GetValue(null));
            // MUTATION-C: mirrors IdeoUIUtility's "DEV: Edit mode" CheckboxLabeled
            // write (decompiled :255).
            DevEditModeField?.SetValue(null, next);
            AnnounceDevToggle(next);
        }

        /// <summary>
        /// Both toggles change what vanilla's draw path — and so this tree — reports for the ideo
        /// already being browsed, which the staleness gate cannot see; dropping the remembered
        /// reference makes the next refresh rebuild it. One utterance for the state change.
        /// </summary>
        private void AnnounceDevToggle(bool on)
        {
            detailsIdeo = null;
            RefreshModel();
            var d = new ElementDescription { Check = on ? CheckState.Checked : CheckState.Unchecked };
            TolkHelper.SpeakData(AnnouncementComposer.ComposeStateChange(d, TranslatedShellVocabulary.Instance));
        }

        /// <summary>Vehicle A: the body of vanilla's own <c>doCloseButton</c> (Window.Close).</summary>
        private void CloseAction()
        {
            dialog.Close();
        }

        // Lifecycle.

        public override void OnPush()
        {
            base.OnPush();
            IdeoBoxDrawPatch.AddInterest();
            IdeoBoxDrawPatch.FollowTarget = FollowTargetRect;
        }

        public override void OnPop()
        {
            IdeoBoxDrawPatch.FollowTarget = null;
            IdeoBoxDrawPatch.RemoveInterest();
            base.OnPop();
        }

        public override void OnFocus()
        {
            base.OnFocus();
            // Re-arm: a child scope pushed over this dialog registers its own follow target and clears
            // it on pop.
            IdeoBoxDrawPatch.FollowTarget = FollowTargetRect;
            if (positionedOnOpen || ideologies.Count == 0)
            {
                return;
            }
            positionedOnOpen = true;
            // Open on whichever ideo vanilla already has selected, so the chassis's entry announcement
            // names the row a sighted player is looking at.
            Ideo target = TargetIdeo;
            int index = target != null ? ideologies.IndexOf(target) : -1;
            ListModel region = Model.Region(ListRegion);
            if (region != null && !region.IsEmpty)
            {
                region.MoveTo(index >= 0 ? index : 0);
            }
            SelectCurrentIdeoSilently();
        }
    }
}
