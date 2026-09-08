using System;
using System.Collections.Generic;
using System.Reflection;
using System.Text;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.Sound;

namespace RimWorldAccess.Shell
{
    /// <summary>
    /// Keyboard scope for <c>MainTabWindow_Ideos</c> (the F12 Ideology tab), window-attached via
    /// <c>ScopeForWindow</c>. Three regions: Ideoligions (radio rows over <c>IdeosInViewOrder</c>;
    /// cursor movement live-syncs <see cref="IdeoUIUtility.selected"/> silently under the tutor gate,
    /// so each row's <see cref="ElementDescription.Selected"/> flag stays current without a
    /// hand-composed value announcement), Details (an <see cref="IdeoDetailsTreeRegion"/> over
    /// <see cref="TargetIdeo"/>, rebuilt SILENTLY on reference change so switching ideoligions never
    /// double-announces), and Editor (<see cref="IdeoEditorRegionCore"/>, revealed only while
    /// <see cref="IdeoUIUtility.DevEditMode"/>; its dev toggles and fluid-points row are off here
    /// because the Buttons region and the Details tree already carry them).
    /// Escape is claimed in every state: a live claiming scope masks everything beneath it and the
    /// modal boundary swallows unclaimed keys, so vanilla's <c>Window.OnCancelKeyPressed</c> never
    /// runs underneath whatever <see cref="OwnsCancel"/> reports.
    /// </summary>
    public sealed class IdeologyViewerScreenScope : ScreenScope
    {
        private enum RegionKind { Ideoligions, Details, Editor }

        private readonly MainTabWindow_Ideos window;
        private readonly IdeoDetailsTreeRegion detailsTree = new IdeoDetailsTreeRegion();
        private readonly IdeoEditorRegionCore editorCore;
        private readonly List<RegionKind> activeRegions = new List<RegionKind>(3);
        private readonly List<ScreenAction> actions = new List<ScreenAction>(4);
        private List<Ideo> ideologies = new List<Ideo>();
        private Ideo detailsIdeo;
        private bool announcedOpen;

        /// <summary>
        /// Set by the injected appearance-editor rows before they open the overlay; consumed once by
        /// <see cref="OnFocus"/>, which forces one <see cref="RebuildDetailsTreeForced"/> before
        /// reannouncing. Targeted, so an unrelated child window closing keeps the expansion state.
        /// </summary>
        private bool rebuildDetailsOnReturn;

        // MUTATION-C reflection into IdeoUIUtility's private dev-mode statics; each host keeps its
        // own FieldInfo pair rather than sharing one.
        private static readonly FieldInfo ShowAllField = AccessTools.Field(typeof(IdeoUIUtility), "showAll");
        private static readonly FieldInfo DevEditModeField = AccessTools.Field(typeof(IdeoUIUtility), "devEditMode");

        public IdeologyViewerScreenScope(MainTabWindow_Ideos window)
        {
            this.window = window;

            editorCore = new IdeoEditorRegionCore(new IdeoEditorRegionCore.Options
            {
                DisplayIdeo = () => TargetIdeo,
                RefreshModel = RefreshModel,
                AnnounceCurrentItem = AnnounceCurrentItem,
                IncludeDevToggles = false, // the Buttons region hosts these on this tab (see class remarks).
                IncludeFluidDevPoints = false, // the Details tree already carries the fluid node.
                // Buttons-region hosted instead: vanilla draws them whenever Prefs.DevMode, but
                // this region exists only while DevEditMode, which would hide them incorrectly.
                IncludeDebugButtons = false,
                IncludeSection = null,
                ValidationOrImpactText = null, // a viewer has nothing to submit.
            });

            // IdeoSymbolEditState's shared AfterEdit dispatcher calls this while the scope is
            // pushed, so a symbol edit from an Editor-region Section row refreshes and re-announces.
            IdeoEditNotifyHub.Register(NotifyIdeoEdited);

            Claim(SharedMenuGrammar.Info, OnInfo);
            // Details-only tree extras (Ctrl+Home/End, Page Up/Down, '*'). ideologyTab.reannounce,
            // nextPanel, previousPanel and contextMenu are dormant: Space rides the shared
            // activateAlias claim, Tab/Shift+Tab region cycling is base grammar, and the dev toggles
            // live in the Buttons region.
            Claim("ideologyTab.firstAbsolute", e => DetailsEdgeJump(true, true), when: InDetailsRegion);
            Claim("ideologyTab.lastAbsolute", e => DetailsEdgeJump(false, true), when: InDetailsRegion);
            Claim("ideologyTab.jumpToPreviousSection", e => DetailsJumpSection(false), when: InDetailsRegion);
            Claim("ideologyTab.jumpToNextSection", e => DetailsJumpSection(true), when: InDetailsRegion);
            Claim("ideologyTab.expandAllSiblings", e => DetailsExpandAllSiblings(), when: InDetailsRegion);
            // First-letter mnemonic on "Save".Translate().
            Claim("ideologyTab.save", e => SaveAction());
            // Idle Escape: the modal boundary swallows any key nothing claims, so vanilla's own
            // Window.OnCancelKeyPressed never runs underneath. Ride CloseAction's vehicle directly;
            // OnPop still provides the single close utterance.
            Claim(SharedMenuGrammar.Cancel, e =>
            {
                ShellFrameStamps.MarkCancelConsumed();
                CloseAction();
            }, when: () => !TypeaheadHasActiveSearch);
        }

        public override string Name
        {
            get { return "ideology-viewer"; }
        }

        protected internal override Window OwnedWindow
        {
            get { return window; }
        }

        protected override bool IncludeCapturedExtrasRegion
        {
            get { return true; }
        }

        /// <summary>
        /// Off: the Editor-region Section rows and the Buttons-region actions already mirror the
        /// window's ButtonTexts under different literal text, so capturing them double-presents.
        /// The broader captured-extras region (<see cref="IncludeCapturedExtrasRegion"/>, with its own
        /// dedup) surfaces anything genuinely unmirrored instead.
        /// </summary>
        protected override bool CaptureWindowButtons
        {
            get { return false; }
        }

        protected override bool EnableTypeahead
        {
            get { return true; }
        }

        /// <summary>
        /// Always true: Cancel is claimed and handled in every reachable state — clearing an active
        /// search, or (idle) closing the tab. See the constructor on why relying on vanilla's
        /// OnCancelKeyPressed underneath a live modal scope does not work.
        /// </summary>
        public override bool OwnsCancel
        {
            get { return true; }
        }

        // Target-ideo resolution (IdeoUIUtility.cs:147,268).

        private Ideo TargetIdeo
        {
            get { return IdeoUIUtility.selected ?? IdeoUIUtility.FallbackSelectedIdeo; }
        }

        // Content model.

        protected override void RefreshContent()
        {
            ideologies = IdeologyHelper.BuildIdeologyList();

            activeRegions.Clear();
            if (ideologies.Count > 0)
            {
                activeRegions.Add(RegionKind.Ideoligions);
                activeRegions.Add(RegionKind.Details);
                if (IdeoUIUtility.DevEditMode)
                {
                    activeRegions.Add(RegionKind.Editor);
                }
            }

            RebuildDetailsTreeIfStale();

            if (activeRegions.Contains(RegionKind.Editor))
            {
                editorCore.Rebuild();
            }
        }

        /// <summary>Rebuilds the Details tree SILENTLY when <see cref="TargetIdeo"/>'s reference changes; a no-op otherwise, so an incidental <see cref="RefreshModel"/> never resets expansion state or cursor on the same ideo.</summary>
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
            detailsTree.SetRoot(BuildDetailsTreeRoot(target), WrapItems);
        }

        /// <summary>
        /// Forces a full Details-tree rebuild for the SAME ideo reference — the reference check in
        /// <see cref="RebuildDetailsTreeIfStale"/> can never see an in-place mutation, so an injected
        /// row that mutates must call this or keep speaking pre-mutation content. Rides
        /// <see cref="IdeoDetailsTreeRegion.SetRootPreservingState"/> (re-expands open branches,
        /// returns the cursor to the same logical row); the numeric clamp is the fallback for a row
        /// that no longer exists. Silent: the caller announces the outcome once.
        /// </summary>
        private void RebuildDetailsTreeForced(Ideo ideo)
        {
            ListModel before = DetailsRegionModel();
            int cursor = before != null && !before.IsEmpty ? before.Index : 0;
            detailsIdeo = ideo;
            int restored = detailsTree.SetRootPreservingState(BuildDetailsTreeRoot(ideo), WrapItems, cursor);
            RefreshModel();
            ListModel region = DetailsRegionModel();
            if (region != null && !region.IsEmpty)
            {
                int target = restored >= 0 ? restored : cursor;
                region.MoveTo(Math.Max(0, Math.Min(target, region.Count - 1)));
            }
        }

        private InspectionTreeItem BuildDetailsTreeRoot(Ideo ideo)
        {
            InspectionTreeItem root = IdeologyHelper.BuildIdeologyTree(ideo);
            InjectAppearanceEditorRows(root, ideo);
            InjectDevMaxPointsRow(root, ideo);
            return root;
        }

        /// <summary>
        /// "Hair and beard styles" / "Tattoo styles": both vanilla boxes open
        /// <c>Dialog_EditIdeoStyleItems</c> gated ONLY by the tutor (IdeoUIUtility.cs:2032), not by
        /// edit mode, so both rows are live in the plain viewer. Nested under the Appearance section,
        /// which <see cref="IdeologyHelper.BuildIdeologyTree"/> adds unconditionally last
        /// (IdeologyHelper.cs:114); the top-level fallback covers that invariant breaking.
        /// </summary>
        private void InjectAppearanceEditorRows(InspectionTreeItem root, Ideo ideo)
        {
            InspectionTreeItem appearanceSection = root.Children.Count > 0 ? root.Children[root.Children.Count - 1] : null;
            InspectionTreeItem parent = appearanceSection ?? root;
            int level = appearanceSection != null ? appearanceSection.IndentLevel + 1 : 0;

            parent.Children.Add(BuildAppearanceEditorRow(
                (string)"HairAndBeards".Translate(), StyleItemTab.HairAndBeard, ideo, level, parent));
            parent.Children.Add(BuildAppearanceEditorRow(
                (string)"Tattoos".Translate(), StyleItemTab.Tattoo, ideo, level, parent));
            if (appearanceSection != null)
            {
                appearanceSection.IsExpandable = true;
            }
        }

        private InspectionTreeItem BuildAppearanceEditorRow(string label, StyleItemTab tab, Ideo ideo, int level, InspectionTreeItem parent)
        {
            // Parent set explicitly: the tree's sibling-position math walks Parent.Children, and an
            // unparented node reports its position against the whole tree. The hint suffix mirrors
            // how the Fluid section's own Label bakes in "Press Enter to reform"
            // (IdeologyHelper.cs:347); this tree's formatter has no spoken-role mechanism.
            var row = new InspectionTreeItem
            {
                Label = label + ". " + (string)"RimWorldAccess.Ideology.Viewer.PressEnterToEdit".Translate(),
                IndentLevel = level,
                IsExpandable = false,
                Parent = parent,
            };
            row.OnActivate = delegate
            {
                // Vehicle A: vanilla's own box body (IdeoUIUtility.cs:2032-2034), tutor gate
                // included. The mode is vanilla's own for a non-edit host (:521): Dev while the dev
                // edit-mode checkbox is on, otherwise None — a read-only dialog with a Back button.
                IdeoEditMode editMode = IdeoUIUtility.DevEditMode ? IdeoEditMode.Dev : IdeoEditMode.None;
                if (!IdeoUIUtility.TutorAllowsInteraction(editMode))
                {
                    SoundDefOf.ClickReject.PlayOneShotOnCamera();
                    return;
                }
                // A visit to the appearance editor likely changed the counts this tree speaks; defer
                // the rebuild to OnFocus so a no-op cancel does not cost expansion state.
                rebuildDetailsOnReturn = true;
                Find.WindowStack.Add(new Dialog_EditIdeoStyleItems(ideo, tab, editMode));
            };
            return row;
        }

        /// <summary>
        /// The dev "+" max-development-points row, visible only when
        /// <see cref="IdeoUIUtility.DevEditMode"/> && the ideo is Fluid && not already reformable.
        /// A per-item <see cref="InspectionTreeItem.OnActivate"/> hook rather than a change to
        /// <see cref="IdeologyHelper"/>, which is shared with the other ideo hosts.
        /// </summary>
        private void InjectDevMaxPointsRow(InspectionTreeItem root, Ideo ideo)
        {
            if (!(IdeoUIUtility.DevEditMode && ideo.Fluid && ideo.development != null && !ideo.development.CanReformNow))
            {
                return;
            }
            var row = new InspectionTreeItem
            {
                Label = (string)"RimWorldAccess.Ideology.Builder.DevMaxPoints".Translate(),
                IndentLevel = 0,
                IsExpandable = false,
                Parent = root, // the tree's sibling-position math walks Parent.Children.
            };
            row.OnActivate = delegate
            {
                // Mirrors IdeoUIUtility.DoFluidIdeo's dev "+" button body verbatim (:983-986): a raw
                // public field write, dev-mode only, exactly as vanilla's own button performs it.
                // Forced rebuild — an in-tree mutation on the same ideo reference is invisible to
                // RebuildDetailsTreeIfStale, and vanilla hides this row once CanReformNow flips.
                ideo.development.points = ideo.development.NextReformationDevelopmentPoints;
                RebuildDetailsTreeForced(ideo);
                AnnounceCurrentItem();
            };
            root.Children.Add(row);
        }

        protected override int ContentRegionCount
        {
            get { return activeRegions.Count; }
        }

        protected override string ContentRegionName(int region)
        {
            switch (activeRegions[region])
            {
                case RegionKind.Ideoligions:
                    return (string)"RimWorldAccess.Ideology.Viewer.ListRegion".Translate();
                case RegionKind.Details:
                    return (string)"RimWorldAccess.Ideology.Viewer.DetailsRegion".Translate();
                default:
                    return (string)"RimWorldAccess.Ideology.Viewer.EditorRegion".Translate();
            }
        }

        protected override int ContentItemCount(int region)
        {
            switch (activeRegions[region])
            {
                case RegionKind.Ideoligions:
                    return ideologies.Count;
                case RegionKind.Details:
                    return detailsTree.Tree.Count;
                default:
                    return editorCore.RowCount;
            }
        }

        protected override ElementDescription DescribeContentItem(int region, int index)
        {
            switch (activeRegions[region])
            {
                case RegionKind.Ideoligions:
                    return DescribeIdeoRow(index);
                case RegionKind.Details:
                    return DescribeDetailsRow(index);
                default:
                    return editorCore.Describe(index);
            }
        }

        protected override string ContentRowSearchText(int region, int index)
        {
            // The details tree's state word rides its label channel; search the bare label so state
            // words stay out of the haystack.
            if (activeRegions[region] == RegionKind.Details)
            {
                InspectionTreeItem item = detailsTree.ItemAt(index);
                if (item != null)
                    return detailsTree.SearchText(item);
            }
            return base.ContentRowSearchText(region, index);
        }

        protected override void ActivateContentItem(int region, int index)
        {
            switch (activeRegions[region])
            {
                case RegionKind.Ideoligions:
                    // Selection is already live-synced on cursor move, so this is a value change,
                    // not a fresh focus: value-only announce, no full row re-announce.
                    TolkHelper.SpeakData(AnnouncementComposer.ComposeStateChange(
                        new ElementDescription { Selected = true }, TranslatedShellVocabulary.Instance));
                    return;
                case RegionKind.Details:
                    ActivateDetailsRow(index);
                    return;
                default:
                    editorCore.Activate(index);
                    return;
            }
        }

        /// <summary>
        /// Enter on a Details row, plus a generic post-activation staleness check: if the activation
        /// opened anything above this scope, <see cref="rebuildDetailsOnReturn"/> is armed.
        /// Compares the count of non-<see cref="ImmediateWindow"/> stack entries (a newly-opened
        /// dialog lands BELOW the persistent ImmediateWindow by <c>WindowLayer</c> ordering, so a
        /// top-of-stack reference comparison misses it) plus <see cref="IdeoBuilderOverlays.AnyActive"/>,
        /// which is the independent arm for the windowless precept/deity/appearance overlays.
        /// An activation that opens nothing changes neither, so expansion state survives.
        /// </summary>
        private void ActivateDetailsRow(int index)
        {
            InspectionTreeItem item = detailsTree.ItemAt(index);
            if (item == null)
            {
                return;
            }
            detailsTree.Tree.SetSelectedIndex(index);
            int countBefore = NonImmediateWindowCount();
            bool overlayBefore = IdeoBuilderOverlays.AnyActive;
            PerformDetailsActivate(item);
            int countAfter = NonImmediateWindowCount();
            bool overlayAfter = IdeoBuilderOverlays.AnyActive;
            if (countAfter > countBefore || (!overlayBefore && overlayAfter))
            {
                rebuildDetailsOnReturn = true;
            }
        }

        /// <summary>
        /// Enter on a Details row, in precedence order: the tree's ideology-specific branches
        /// (ritual-sound toggle, reform hand-off), then the row's own
        /// <see cref="InspectionTreeItem.OnActivate"/>, then expand/collapse. That last branch is the
        /// one place Enter also COLLAPSES, against the generic tree contract, so it is explicit here.
        /// </summary>
        private void PerformDetailsActivate(InspectionTreeItem item)
        {
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

        private static int NonImmediateWindowCount()
        {
            IList<Window> windows = Find.WindowStack.Windows;
            int count = 0;
            for (int i = 0; i < windows.Count; i++)
            {
                if (!(windows[i] is ImmediateWindow))
                {
                    count++;
                }
            }
            return count;
        }

        protected override bool CanAdjustContentItem(int region, int index)
        {
            return region >= 0 && region < activeRegions.Count && activeRegions[region] == RegionKind.Details;
        }

        protected override void AdjustContentItem(int region, int index, int direction)
        {
            if (region < 0 || region >= activeRegions.Count || activeRegions[region] != RegionKind.Details)
            {
                return;
            }
            if (detailsTree.ItemAt(index) == null)
            {
                return;
            }
            // Left/Right always clear an active search rather than acting as a match move.
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

        // Sound/announce dispatch for the Details tree, mirroring TreeRegionScope's own
        // PerformExpand/PerformCollapse switch (private there, and tied to its single Tree field).
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

        private int DetailsRegionIndex()
        {
            return activeRegions.IndexOf(RegionKind.Details);
        }

        private ListModel DetailsRegionModel()
        {
            int region = DetailsRegionIndex();
            return region >= 0 ? Model.Region(region) : null;
        }

        private void SyncDetailsRegionFromTree()
        {
            ListModel region = DetailsRegionModel();
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

        /// <summary>Home/End (sibling-scoped) and Ctrl+Home/Ctrl+End (absolute) on the Details tree; both clear an active search rather than jumping to its first/last match, matching the tree family.</summary>
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

        /// <summary>Page Up/Down between the tree's level-0 section headers; rejects during a search.</summary>
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

        private bool InDetailsRegion()
        {
            int r = Model.RegionIndex;
            return r >= 0 && r < activeRegions.Count && activeRegions[r] == RegionKind.Details;
        }

        private bool InEditorRegion()
        {
            int r = Model.RegionIndex;
            return r >= 0 && r < activeRegions.Count && activeRegions[r] == RegionKind.Editor;
        }

        // Details-pane focus ring and scroll follow (see IdeoBoxDrawPatch).

        /// <summary>The vanilla box the Details cursor is on, or null when it is elsewhere or on a row vanilla draws no box for.</summary>
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

        /// <summary>Arms scroll-follow for the Details pane. Called from both landing paths: the shared <see cref="OnCursorSettled"/> hook, and this screen's own tree jumps, which move the region model directly.</summary>
        private void RequestDetailsFollow()
        {
            if (InDetailsRegion())
            {
                IdeoBoxDrawPatch.RequestFollow();
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

        // Ideoligions region.

        private ElementDescription DescribeIdeoRow(int index)
        {
            var d = new ElementDescription();
            if (index < 0 || index >= ideologies.Count)
            {
                return d;
            }
            // Sync BEFORE reading TargetIdeo, gated to the row the cursor rests on, so the Selected
            // flag describes this row rather than lagging one step behind. The gate means a haystack
            // pass over every row can never sync to the wrong one; a redundant sync is a no-op.
            ListModel cursor = Model.Region((int)RegionKind.Ideoligions);
            if (cursor != null && cursor.Index == index)
            {
                SelectCurrentListIdeoSilently();
            }
            Ideo ideo = ideologies[index];
            d.Label = IdeologyHelper.BuildIdeoListAnnouncement(ideo);
            d.Role = ElementRole.RadioButton;
            d.Selected = ReferenceEquals(ideo, TargetIdeo);
            if (ideo.initialPlayerIdeo)
            {
                d.Extras = (string)"InitialPlayerIdeo".Translate();
            }
            return d;
        }

        /// <summary>Mirrors <c>IdeoUIUtility.DrawIdeoRow</c>'s click branch: honor the same tutor gate, then call the same public setter. Silent — the caller decides when to speak.</summary>
        private void SelectCurrentListIdeoSilently()
        {
            ListModel region = Model.Region((int)RegionKind.Ideoligions);
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

        // Details region.

        private ElementDescription DescribeDetailsRow(int index)
        {
            return detailsTree.Describe(detailsTree.ItemAt(index));
        }

        // Info card drill-in (the unified Alt+I picker).

        private void OnInfo(KeyEventSnapshot e)
        {
            RefreshModel();
            if (InDetailsRegion())
            {
                ListModel details = Model.CurrentRegion;
                detailsTree.OpenInfoCard(details != null && !details.IsEmpty ? detailsTree.ItemAt(details.Index) : null);
                return;
            }
            if (InEditorRegion())
            {
                ListModel region = Model.CurrentRegion;
                List<Def> defs = region != null && !region.IsEmpty ? editorCore.InspectableDefs(region.Index) : new List<Def>();
                if (defs.Count == 0)
                {
                    InfoCardState.SpeakNoInfoCardAvailable();
                    return;
                }
                if (defs.Count == 1)
                {
                    InfoCardState.OpenInfoCardForDef(defs[0]);
                    return;
                }
                var options = new List<FloatMenuOption>();
                foreach (Def def in defs)
                {
                    Def captured = def;
                    string label = def.label != null ? def.label.CapitalizeFirst() : def.defName;
                    options.Add(new FloatMenuOption(label, delegate { InfoCardState.OpenInfoCardForDef(captured); }));
                }
                TolkHelper.Speak("RimWorldAccess.InfoCard.ChooseItemToInspect".Loc());
                WindowlessFloatMenuState.Open(options, false);
                return;
            }
            InfoCardState.SpeakNoInfoCardAvailable();
        }

        // Buttons region: Save / dev toggles / Close.

        /// <summary>Shift+Enter presses Save from anywhere on this screen: the one-chord proceed.</summary>
        protected override string DefaultAcceptActionId
        {
            get { return "ideologyTab.save"; }
        }

        protected override IReadOnlyList<ScreenAction> DeclaredActions
        {
            get
            {
                actions.Clear();
                actions.Add(new ScreenAction((string)"Save".Translate(), SaveAction, "ideologyTab.save"));
                if (Prefs.DevMode)
                {
                    actions.Add(new ScreenAction(
                        "DEV: Show all", // l10n-exempt: dev-only, matches IdeoUIUtility's own unlocalized checkbox text.
                        ToggleShowAll,
                        check: CurrentShowAllCheckState()));
                    actions.Add(new ScreenAction(
                        "DEV: Edit mode", // l10n-exempt: same precedent.
                        ToggleEditMode,
                        check: CurrentEditModeCheckState()));
                    // Prefs.DevMode-gated only, independent of edit mode, so they live here rather
                    // than in the Editor region; delegate to IdeoEditorRegionCore's entry points.
                    actions.Add(new ScreenAction("DEV: Single precept", () => editorCore.ActivateDebugSinglePrecept())); // l10n-exempt: dev-only, mirrors IdeoUIUtility.DoDebugButtons' own unlocalized text verbatim.
                    actions.Add(new ScreenAction("DEV: test descriptions...", () => editorCore.ActivateDebugTestDescriptions())); // l10n-exempt: same precedent.
                    actions.Add(new ScreenAction("DEV: Test names...", () => editorCore.ActivateDebugTestNames())); // l10n-exempt: same precedent.
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

        private void SaveAction()
        {
            // Vehicle A: opens Dialog_IdeoList_Save exactly as vanilla's own Save button does.
            // Acts on TargetIdeo, matching every other action on this tab.
            IdeoEditorCommands.SaveIdeoligion(TargetIdeo);
        }

        private void ToggleShowAll()
        {
            bool next = !(ShowAllField != null && (bool)ShowAllField.GetValue(null));
            // MUTATION-C: mirrors IdeoUIUtility's "DEV: Show all" CheckboxLabeled, which writes the
            // private static showAll by ref (decompiled :254); no gated setter exists.
            ShowAllField?.SetValue(null, next);
            RefreshModel();
            var d = new ElementDescription { Check = next ? CheckState.Checked : CheckState.Unchecked };
            TolkHelper.SpeakData(AnnouncementComposer.ComposeStateChange(d, TranslatedShellVocabulary.Instance));
        }

        private void ToggleEditMode()
        {
            bool next = !(DevEditModeField != null && (bool)DevEditModeField.GetValue(null));
            // MUTATION-C: mirrors IdeoUIUtility's "DEV: Edit mode" CheckboxLabeled write (decompiled :255).
            DevEditModeField?.SetValue(null, next);
            // Structural: the Editor region appears/disappears with this flag, so RefreshModel
            // recomputes activeRegions before the single state-change utterance below.
            RefreshModel();
            var d = new ElementDescription { Check = next ? CheckState.Checked : CheckState.Unchecked };
            TolkHelper.SpeakData(AnnouncementComposer.ComposeStateChange(d, TranslatedShellVocabulary.Instance));
        }

        private void CloseAction()
        {
            // Vehicle A: the same call vanilla's tab toggle/click path makes (MainTabsRoot.cs:32-43);
            // OpenTab is computed live from the window stack, so nothing can desync. Removing the
            // window fires OnPop, which speaks Tab.Closed.
            Find.MainTabsRoot.EscapeCurrentTab();
        }

        // Extras haystack.

        protected override IEnumerable<string> AdditionalPresentedTexts
        {
            get
            {
                foreach (string text in IdeoDetailsPresentedTexts.DescriptionLockIconStates(editAffordancesVisible: IdeoUIUtility.DevEditMode))
                {
                    yield return text;
                }
                foreach (string text in IdeoDetailsPresentedTexts.EditModeCaption(editAffordancesVisible: IdeoUIUtility.DevEditMode))
                {
                    yield return text;
                }
                foreach (string text in IdeoDetailsPresentedTexts.AddPreceptButtonLabels(editAffordancesVisible: IdeoUIUtility.DevEditMode))
                {
                    yield return text;
                }
                foreach (string text in IdeoDetailsPresentedTexts.DeitySectionButtonLabels(editAffordancesVisible: IdeoUIUtility.DevEditMode))
                {
                    yield return text;
                }
                Ideo target = ideologies.Count > 0 ? TargetIdeo : null;
                if (target != null)
                {
                    foreach (string text in IdeoDetailsPresentedTexts.ForIdeo(target))
                    {
                        yield return text;
                    }
                }
                if (Prefs.DevMode)
                {
                    // Presented so the captured-extras diff never mistakes vanilla's own drawn
                    // checkboxes for an unmirrored mod widget.
                    yield return "DEV: Show all"; // l10n-exempt: dev-only diagnostic label, the IdeoEditorRegionCore/EntityCodexState/FactionTabState precedent.
                    yield return "DEV: Edit mode"; // l10n-exempt: same precedent.
                }
            }
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
            IdeoEditNotifyHub.Unregister(NotifyIdeoEdited);
            IdeoDetailsTreeRegion.StopRitualSound();
            TolkHelper.Speak("RimWorldAccess.Ideology.Tab.Closed".Loc(MainButtonDefOf.Ideos.LabelCap));
        }

        /// <summary>
        /// Called by IdeoSymbolEditState's shared AfterEdit dispatcher while this scope is the
        /// top-of-stack registrant. Forces a Details-tree rebuild first: the edit changed content on
        /// the SAME ideo reference the ordinary staleness check cannot see, and the hub fires only on
        /// a committed edit, never on a cancel.
        /// </summary>
        private void NotifyIdeoEdited(bool announce)
        {
            if (ideologies.Count > 0)
            {
                RebuildDetailsTreeForced(TargetIdeo);
            }
            RefreshModel();
            if (announce)
            {
                AnnounceCurrentItem();
            }
        }

        public override void OnFocus()
        {
            base.OnFocus();
            // Re-arm: an overlay editor pushed over this tab registers its own follow target and
            // clears it on pop.
            IdeoBoxDrawPatch.FollowTarget = FollowTargetRect;
            if (announcedOpen)
            {
                if (rebuildDetailsOnReturn)
                {
                    rebuildDetailsOnReturn = false;
                    if (ideologies.Count > 0)
                    {
                        RebuildDetailsTreeForced(TargetIdeo);
                    }
                }
                AnnounceCurrentItem();
                return;
            }
            announcedOpen = true;

            if (ideologies.Count == 0)
            {
                TolkHelper.Speak("RimWorldAccess.Ideology.Tab.Empty".Loc(MainButtonDefOf.Ideos.LabelCap, "NoneLower".Translate()));
                return;
            }

            Ideo target = TargetIdeo;
            int idx = target != null ? ideologies.IndexOf(target) : -1;
            ListModel region = Model.Region((int)RegionKind.Ideoligions);
            if (region != null && !region.IsEmpty)
            {
                region.MoveTo(idx >= 0 ? idx : 0);
            }
            SelectCurrentListIdeoSilently();

            string tabCount = TabCountFragment();
            var sb = new StringBuilder();
            if (!string.IsNullOrEmpty(tabCount))
            {
                sb.Append(tabCount).Append(". ");
            }
            string tabLabel = MainButtonDefOf.Ideos.LabelCap;
            sb.Append(tabLabel);
            IdeologyHelper.AppendSentence(sb, "RimWorldAccess.Ideology.Tab.ItemCount".Translate(ideologies.Count, tabLabel.ToString().ToLower()));
            IdeologyHelper.AppendSentence(sb, IdeologyHelper.BuildIdeoListAnnouncement(TargetIdeo));
            string position = MenuHelper.FormatPosition(idx >= 0 ? idx : 0, ideologies.Count);
            if (!string.IsNullOrEmpty(position))
            {
                IdeologyHelper.AppendSentence(sb, position);
            }
            TolkHelper.SpeakData(sb.ToString(), SpeechPriority.High);
        }
    }
}
