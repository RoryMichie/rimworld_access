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
    /// Focus scope for <c>Dialog_ConfigureIdeo(forArchonexusRestart: true)</c>, the Archonexus
    /// endgame's choose/build/edit-your-ideoligion screen.
    /// <see cref="RimWorldAccess.ArchonexusReformIdeoState"/> is its facade (reflection surface, row
    /// source, vanilla-vehicle actions).
    ///
    /// Two content regions. <b>Ideoligions</b>: Create new / Create fluid / one radio row per ideo /
    /// Assign colonists (vanilla's own visibility gate, decompiled Dialog_ConfigureIdeo.cs:113-114) /
    /// Make-or-remove-primary, which is ALWAYS one row — read-only law: navigable and saying why,
    /// where vanilla's own mutually-exclusive buttons appear and disappear.
    /// <see cref="ElementDescription.Selected"/> tracks which ideo is BROWSED
    /// (<see cref="IdeoUIUtility.selected"/>, live-synced on cursor rest by
    /// <see cref="SyncSelectedIdeoIfCursorHere"/>), a separate concept from which is primary
    /// (<see cref="RimWorldAccess.ArchonexusReformIdeoState.CurrentPrimaryIdeo"/>) — vanilla keeps the
    /// two as independent statics too.
    ///
    /// <b>Details</b> flips per <see cref="DetailsIsEditable"/> (vanilla's <c>onlyEditIdeo</c> gate,
    /// decompiled :274): an <see cref="IdeoEditorRegionCore"/> for the one editable ideo, the shared
    /// <see cref="IdeoDetailsTreeRegion"/> read tree for every other. The read tree still injects the
    /// appearance and dev-max-points interactives, because vanilla gates those by tutor/DevEditMode
    /// only, never by edit mode. Selecting a different ideo swaps the branch silently — no region-2
    /// re-announce, per the silent-content-rebuild law. Editor options: dev toggles and debug buttons
    /// FALSE because the Buttons region hosts them, and <see cref="editorCore"/>'s <c>DisplayIdeo</c>
    /// is bound to the BROWSED ideo so its <c>ActivateDebugXxx</c> entry points work in either branch
    /// (vanilla's <c>DoDebugButtons</c> acts on whatever <c>DoIdeoDetails</c> draws, decompiled :581);
    /// fluid dev points TRUE (<c>DoFluidIdeo</c> draws for any fluid ideo regardless of edit mode,
    /// decompiled :552-555); no section filter (this dialog's edit mode is unrestricted, unlike the
    /// reform dialog's); no validation text (decompiled-verified: "Next" has no validity gate).
    ///
    /// Save/Load and the DEV/Randomize/Next affordances live in the Buttons region.
    /// <see cref="CaptureWindowButtons"/> is false because every literal vanilla string the typed rows
    /// carry is vouched again in <see cref="AdditionalPresentedTexts"/>; raw capture would
    /// double-present them. Save acts on the browsed ideo, Load on the dialog. No Back or Cancel row:
    /// decompiled-verified, this dialog draws neither — the pick is mandatory.
    ///
    /// OwnsCancel/OwnsAccept are both true. The ctor sets <c>closeOnCancel = false</c> with
    /// <c>openMenuOnCancel = true</c> (unmitigated Escape would toggle the pause menu underneath) and
    /// leaves <c>closeOnAccept</c> at the base-TRUE default (unmitigated Enter/Alt+S would close the
    /// dialog, discarding every pick) — decompiled :50-58. Cancel is claimed in EVERY reachable state
    /// because a live claiming modal scope's swallow eats unclaimed keys before vanilla's
    /// <c>OnCancelKeyPressed</c> ever runs, so fallthrough never happens; idle Escape speaks
    /// <c>RimWorldAccess.Archonexus.Reform.CannotCancel</c> and does not close. Alt+S stamps
    /// <see cref="ShellFrameStamps.MarkAcceptConsumed"/> like Enter, or vanilla's <c>closeOnAccept</c>
    /// closes the dialog on the same frame this scope's claim commits the reform.
    ///
    /// <see cref="OnFocus"/>'s non-first call (any child closing) refreshes the row list and, while
    /// the browsed ideo is read-only, force-rebuilds its tree
    /// (<see cref="RebuildDetailsTreeForced"/>) before reannouncing: every return path can have
    /// changed content in place, and a forced rebuild is cheap here.
    /// </summary>
    public sealed class ArchonexusIdeoScreenScope : ScreenScope
    {
        private enum RegionKind { Ideoligions, Details }

        private readonly Dialog_ConfigureIdeo dialog;
        private readonly IdeoDetailsTreeRegion detailsTree = new IdeoDetailsTreeRegion();
        private readonly IdeoEditorRegionCore editorCore;
        private readonly List<ScreenAction> actions = new List<ScreenAction>(8);
        private Ideo detailsTreeBuiltFor;
        private bool announcedOpen;
        private bool randomizeConfirmOpen;

        // MUTATION-C reflection into IdeoUIUtility's private dev-mode statics.
        private static readonly FieldInfo ShowAllField = AccessTools.Field(typeof(IdeoUIUtility), "showAll");
        private static readonly FieldInfo DevEditModeField = AccessTools.Field(typeof(IdeoUIUtility), "devEditMode");

        public ArchonexusIdeoScreenScope(Dialog_ConfigureIdeo dialog)
        {
            this.dialog = dialog;

            editorCore = new IdeoEditorRegionCore(new IdeoEditorRegionCore.Options
            {
                DisplayIdeo = () => SelectedIdeo,
                RefreshModel = RefreshModel,
                AnnounceCurrentItem = AnnounceCurrentItem,
                IncludeDevToggles = false, // Buttons region hosts these.
                IncludeFluidDevPoints = true,
                IncludeDebugButtons = false, // Buttons region hosts these via editorCore's ActivateDebugXxx.
                IncludeSection = null,
                ValidationOrImpactText = null,
            });

            IdeoEditNotifyHub.Register(NotifyIdeoEdited);

            Claim(SharedMenuGrammar.Info, OnInfo);
            // Details-tree-only extras; gated out while Details hosts the editor.
            Claim("archonexusReformIdeo.jumpToFirstAbsolute", e => DetailsEdgeJump(true, true), when: TreeMode);
            Claim("archonexusReformIdeo.jumpToLastAbsolute", e => DetailsEdgeJump(false, true), when: TreeMode);
            Claim("archonexusReformIdeo.jumpToPreviousSection", e => DetailsJumpSection(false), when: TreeMode);
            Claim("archonexusReformIdeo.jumpToNextSection", e => DetailsJumpSection(true), when: TreeMode);
            Claim("archonexusReformIdeo.expandAllSiblings", e => DetailsExpandAllSiblings(), when: TreeMode);
            Claim("archonexusReformIdeo.randomizeAll", e => RandomizeAllAction());
            Claim("archonexusReformIdeo.confirmAndProceed", OnConfirmAndProceed);

            // Claimed unconditionally: a live claiming modal scope's swallow means OwnsCancel=false
            // would never actually let a vanilla cancel body run.
            Claim(SharedMenuGrammar.Cancel, e =>
            {
                ShellFrameStamps.MarkCancelConsumed();
                TolkHelper.Speak("RimWorldAccess.Archonexus.Reform.CannotCancel".Loc(), SpeechPriority.High);
            }, when: () => !TypeaheadHasActiveSearch);
        }

        public override string Name
        {
            get { return "archonexus-ideo"; }
        }

        protected internal override Window OwnedWindow
        {
            get { return dialog; }
        }

        protected override bool IncludeCapturedExtrasRegion
        {
            get { return true; }
        }

        /// <summary>False: the typed rows' vanilla strings are re-vouched in <see cref="AdditionalPresentedTexts"/>, so raw capture would double-present them.</summary>
        protected override bool CaptureWindowButtons
        {
            get { return false; }
        }

        protected override bool EnableTypeahead
        {
            get { return true; }
        }

        public override bool OwnsCancel
        {
            get { return true; }
        }

        // ------------------------------------------------------------------
        // Selection/editability.
        // ------------------------------------------------------------------

        private Ideo SelectedIdeo
        {
            get { return IdeoUIUtility.selected ?? IdeoUIUtility.FallbackSelectedIdeo; }
        }

        private bool DetailsIsEditable
        {
            get { return ArchonexusReformIdeoState.IsEditable(SelectedIdeo); }
        }

        private bool InDetailsRegion()
        {
            return Model.RegionIndex == (int)RegionKind.Details;
        }

        private bool TreeMode()
        {
            return InDetailsRegion() && !DetailsIsEditable;
        }

        // ------------------------------------------------------------------
        // Content model.
        // ------------------------------------------------------------------

        protected override void RefreshContent()
        {
            ArchonexusReformIdeoState.RebuildRows();

            Ideo target = SelectedIdeo;
            if (target != null && ArchonexusReformIdeoState.IsEditable(target))
            {
                editorCore.Rebuild();
            }
            else
            {
                RebuildDetailsTreeIfStale(target);
            }
        }

        /// <summary>Silent unless <paramref name="target"/>'s reference changed since the last build, so expansion and cursor state survive incidental refreshes.</summary>
        private void RebuildDetailsTreeIfStale(Ideo target)
        {
            if (ReferenceEquals(detailsTreeBuiltFor, target))
            {
                return;
            }
            detailsTreeBuiltFor = target;
            if (target == null)
            {
                return;
            }
            detailsTree.SetRoot(BuildDetailsTreeRoot(target), WrapItems);
        }

        /// <summary>Rebuilds for the SAME ideo reference, which the staleness check can never trigger, after an in-place mutation or a return from a child. <see cref="IdeoDetailsTreeRegion.SetRootPreservingState"/> keeps expanded branches and the logical cursor row; the numeric clamp is the fallback when the rebuild removed that row.</summary>
        private void RebuildDetailsTreeForced(Ideo target)
        {
            ListModel before = Model.Region((int)RegionKind.Details);
            int cursor = before != null && !before.IsEmpty ? before.Index : 0;
            detailsTreeBuiltFor = target;
            int restored = detailsTree.SetRootPreservingState(BuildDetailsTreeRoot(target), WrapItems, cursor);
            RefreshModel();
            ListModel region = Model.Region((int)RegionKind.Details);
            if (region != null && !region.IsEmpty)
            {
                int landing = restored >= 0 ? restored : cursor;
                region.MoveTo(Math.Max(0, Math.Min(landing, region.Count - 1)));
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
        /// "Hair and beard styles"/"Tattoos": both vanilla boxes open <c>Dialog_EditIdeoStyleItems</c>
        /// gated ONLY by tutor (IdeoUIUtility.cs:2032), never by edit mode, so a read-only ideo here
        /// gets them too.
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
            var row = new InspectionTreeItem
            {
                Label = label + ". " + (string)"RimWorldAccess.Ideology.Viewer.PressEnterToEdit".Translate(),
                IndentLevel = level,
                IsExpandable = false,
                Parent = parent,
            };
            row.OnActivate = delegate
            {
                // Vehicle A. These rows only ever describe a read-only ideo (the editable one goes
                // through IdeoEditorRegionCore), which is vanilla's IdeoEditMode.None (decompiled :274, :521).
                IdeoEditMode editMode = IdeoUIUtility.DevEditMode ? IdeoEditMode.Dev : IdeoEditMode.None;
                if (!IdeoUIUtility.TutorAllowsInteraction(editMode))
                {
                    SoundDefOf.ClickReject.PlayOneShotOnCamera();
                    return;
                }
                Find.WindowStack.Add(new Dialog_EditIdeoStyleItems(ideo, tab, editMode));
            };
            return row;
        }

        /// <summary>Visible only while DevEditMode and the browsed ideo is fluid and not already reformable (decompiled IdeoUIUtility.cs:983-986).</summary>
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
                Parent = root,
            };
            row.OnActivate = delegate
            {
                ideo.development.points = ideo.development.NextReformationDevelopmentPoints;
                RebuildDetailsTreeForced(ideo);
                AnnounceCurrentItem();
            };
            root.Children.Add(row);
        }

        protected override int ContentRegionCount
        {
            get { return 2; }
        }

        protected override string ContentRegionName(int region)
        {
            return region == (int)RegionKind.Ideoligions
                ? (string)"RimWorldAccess.Archonexus.Reform.ListRegion".Translate()
                : (string)"RimWorldAccess.Archonexus.Reform.DetailsRegion".Translate();
        }

        protected override int ContentItemCount(int region)
        {
            if (region == (int)RegionKind.Ideoligions)
            {
                return ArchonexusReformIdeoState.Rows.Count;
            }
            return DetailsIsEditable ? editorCore.RowCount : detailsTree.Tree.Count;
        }

        protected override ElementDescription DescribeContentItem(int region, int index)
        {
            if (region == (int)RegionKind.Ideoligions)
            {
                return DescribeIdeoligionRow(index);
            }
            return DetailsIsEditable ? editorCore.Describe(index) : DescribeDetailsTreeRow(index);
        }

        protected override string ContentRowSearchText(int region, int index)
        {
            // The details tree's state word rides its label channel; search the bare label so state
            // words stay out of the haystack.
            if (region != (int)RegionKind.Ideoligions && !DetailsIsEditable)
            {
                InspectionTreeItem item = detailsTree.ItemAt(index);
                if (item != null)
                    return detailsTree.SearchText(item);
            }
            return base.ContentRowSearchText(region, index);
        }

        protected override void ActivateContentItem(int region, int index)
        {
            if (region == (int)RegionKind.Ideoligions)
            {
                ActivateIdeoligionRow(index);
                return;
            }
            if (DetailsIsEditable)
            {
                editorCore.Activate(index);
            }
            else
            {
                ActivateDetailsTreeRow(index);
            }
        }

        protected override bool CanAdjustContentItem(int region, int index)
        {
            return TreeMode() && region == (int)RegionKind.Details;
        }

        protected override void AdjustContentItem(int region, int index, int direction)
        {
            if (region != (int)RegionKind.Details || DetailsIsEditable || detailsTree.ItemAt(index) == null)
            {
                return;
            }
            // Left/Right is never a match move: clear any active search first.
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

        /// <summary>Home/End: sibling-scoped inside the read-only Details tree, the plain flat jump everywhere else.</summary>
        protected override void MoveItemEdge(bool first)
        {
            if (TreeMode())
            {
                DetailsEdgeJump(first, false);
                return;
            }
            base.MoveItemEdge(first);
        }

        // ------------------------------------------------------------------
        // Details tree chords. Sound/announce dispatch mirrors TreeRegionScope's own, which is
        // private there and tied to its single Tree field, so not reusable from a multi-region host.
        // ------------------------------------------------------------------

        private void ActivateDetailsTreeRow(int index)
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
            // The one place Enter also COLLAPSES, deviating from the generic contract where Enter
            // never expands (Right arrow only ever expands).
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
            ListModel region = Model.Region((int)RegionKind.Details);
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

        // ------------------------------------------------------------------
        // Details-pane focus ring and scroll follow (see IdeoBoxDrawPatch).
        // ------------------------------------------------------------------

        /// <summary>The vanilla box the Details cursor is on, or null while Details hosts the editor rows or the cursor is on a row vanilla draws no box for.</summary>
        private object DetailsRingIdentity()
        {
            if (!TreeMode())
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

        /// <summary>Arms scroll-follow for the Details pane. Both landing paths call it: <see cref="OnCursorSettled"/>, and the tree jumps, which move the region model directly instead.</summary>
        private void RequestDetailsFollow()
        {
            if (TreeMode())
            {
                IdeoBoxDrawPatch.RequestFollow();
            }
        }

        /// <summary>Home/End (sibling-scoped) and Ctrl+Home/Ctrl+End (absolute); both clear an active search rather than jumping to its first or last match.</summary>
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

        // ------------------------------------------------------------------
        // Ideoligions region.
        // ------------------------------------------------------------------

        private ElementDescription DescribeIdeoligionRow(int index)
        {
            var d = new ElementDescription();
            IReadOnlyList<ArchonexusReformIdeoState.Row> rows = ArchonexusReformIdeoState.Rows;
            if (index < 0 || index >= rows.Count)
            {
                return d;
            }
            ArchonexusReformIdeoState.Row row = rows[index];
            switch (row.Kind)
            {
                case ArchonexusReformIdeoState.RowKind.CreateNew:
                    // Vanilla's own label for the forArchonexusRestart branch (decompiled
                    // IdeoUIUtility.cs:335); this dialog is always forArchonexusRestart.
                    d.Label = ((string)"CreateNew".Translate()) + "...";
                    d.Role = ElementRole.Button;
                    return d;
                case ArchonexusReformIdeoState.RowKind.CreateFluid:
                    d.Label = (string)"CreateFluid".Translate();
                    d.Role = ElementRole.Button;
                    return d;
                case ArchonexusReformIdeoState.RowKind.Ideo:
                    SyncSelectedIdeoIfCursorHere(index, row.Ideo);
                    d.Label = IdeologyHelper.BuildIdeoListAnnouncement(row.Ideo);
                    d.Role = ElementRole.RadioButton;
                    d.Selected = ReferenceEquals(row.Ideo, IdeoUIUtility.selected);
                    if (ReferenceEquals(row.Ideo, ArchonexusReformIdeoState.CurrentPrimaryIdeo))
                    {
                        d.Extras = (string)"RimWorldAccess.Archonexus.Reform.CurrentPrimaryFragment".Translate();
                    }
                    return d;
                case ArchonexusReformIdeoState.RowKind.AssignColonists:
                    d.Label = (string)"AssignColonists".Translate();
                    d.Role = ElementRole.Button;
                    return d;
                case ArchonexusReformIdeoState.RowKind.MakeOrRemovePrimary:
                    DescribeMakeOrRemovePrimary(d);
                    return d;
                default:
                    return d;
            }
        }

        /// <summary>Mirrors Dialog_ConfigureIdeo's own mutually-exclusive gates (decompiled :129,136) but stays navigable-and-disabled instead of disappearing in the third case — the read-only law.</summary>
        private void DescribeMakeOrRemovePrimary(ElementDescription d)
        {
            d.Role = ElementRole.Button;
            if (ArchonexusReformIdeoState.ShouldShowRemoveNewIdeoligion())
            {
                d.Label = (string)"RemoveNewIdeoligion".Translate();
                return;
            }
            if (ArchonexusReformIdeoState.CanMakeSelectedPrimary())
            {
                d.Label = (string)"MakeIdeoligionPrimary".Translate();
                return;
            }
            d.Label = (string)"MakeIdeoligionPrimary".Translate();
            d.Disabled = true;
            Ideo selected = IdeoUIUtility.selected;
            d.Extras = selected != null
                ? (string)"RimWorldAccess.Archonexus.Reform.AlreadyPrimary".Translate(selected.name)
                : (string)"RimWorldAccess.Ideology.Builder.Unavailable".Translate();
        }

        /// <summary>Mirrors <c>IdeoUIUtility.DrawIdeoRow</c>'s click branch: the same tutor gate, then the same public setter. Gated to the row the cursor rests on so a haystack describe pass can never sync to the wrong row.</summary>
        private void SyncSelectedIdeoIfCursorHere(int index, Ideo ideo)
        {
            ListModel region = Model.Region((int)RegionKind.Ideoligions);
            if (region == null || region.Index != index || ideo == null || IdeoUIUtility.selected == ideo)
            {
                return;
            }
            if (TutorSystem.AllowAction("ConfiguringIdeo"))
            {
                IdeoUIUtility.SetSelected(ideo);
            }
        }

        private void ActivateIdeoligionRow(int index)
        {
            IReadOnlyList<ArchonexusReformIdeoState.Row> rows = ArchonexusReformIdeoState.Rows;
            if (index < 0 || index >= rows.Count)
            {
                return;
            }
            ArchonexusReformIdeoState.Row row = rows[index];
            switch (row.Kind)
            {
                case ArchonexusReformIdeoState.RowKind.CreateNew:
                    // Vehicle A: opens Dialog_ChooseMemes, whose own opening announce takes over.
                    ArchonexusReformIdeoState.CreateNew(fluid: false);
                    return;
                case ArchonexusReformIdeoState.RowKind.CreateFluid:
                    ArchonexusReformIdeoState.CreateNew(fluid: true);
                    return;
                case ArchonexusReformIdeoState.RowKind.Ideo:
                    // Selection is already live-synced on cursor rest (DescribeIdeoligionRow), so
                    // this is a value-only announce, not a full row re-announce.
                    TolkHelper.SpeakData(AnnouncementComposer.ComposeStateChange(
                        new ElementDescription { Selected = true }, TranslatedShellVocabulary.Instance));
                    return;
                case ArchonexusReformIdeoState.RowKind.AssignColonists:
                    // Vehicle A: opens Dialog_ChooseColonistsForIdeo; the facade speaks its own
                    // NoColonistsNeedConverting guard when there is nothing to do.
                    ArchonexusReformIdeoState.AssignColonists();
                    return;
                case ArchonexusReformIdeoState.RowKind.MakeOrRemovePrimary:
                    ActivateMakeOrRemovePrimary();
                    return;
            }
        }

        private void ActivateMakeOrRemovePrimary()
        {
            if (ArchonexusReformIdeoState.ShouldShowRemoveNewIdeoligion())
            {
                ArchonexusReformIdeoState.RemoveNewIdeoligion();
            }
            else if (ArchonexusReformIdeoState.CanMakeSelectedPrimary())
            {
                ArchonexusReformIdeoState.MakeSelectedPrimary();
            }
            else
            {
                SoundDefOf.ClickReject.PlayOneShotOnCamera();
                AnnounceCurrentItem();
                return;
            }
            // Refresh first so every OTHER ideo row's "current primary" fragment is fresh, then one
            // utterance for the value change.
            RefreshModel();
            Ideo primary = ArchonexusReformIdeoState.CurrentPrimaryIdeo;
            if (primary != null)
            {
                TolkHelper.SpeakData((string)"RimWorldAccess.Archonexus.Reform.SetAsPrimary".Translate(primary.name), SpeechPriority.High);
            }
            else
            {
                AnnounceCurrentItem();
            }
        }

        // ------------------------------------------------------------------
        // Details region.
        // ------------------------------------------------------------------

        private ElementDescription DescribeDetailsTreeRow(int index)
        {
            return detailsTree.Describe(detailsTree.ItemAt(index));
        }

        // ------------------------------------------------------------------
        // Info card drill-in (the unified Alt+I picker).
        // ------------------------------------------------------------------

        private void OnInfo(KeyEventSnapshot e)
        {
            RefreshModel();
            if (InDetailsRegion())
            {
                ListModel region = Model.CurrentRegion;
                if (!DetailsIsEditable)
                {
                    detailsTree.OpenInfoCard(region != null && !region.IsEmpty ? detailsTree.ItemAt(region.Index) : null);
                    return;
                }
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

        // ------------------------------------------------------------------
        // Buttons region: Save / Load / DEV toggles+debug buttons / Randomize all / Next.
        // ------------------------------------------------------------------

        /// <summary>Shift+Enter presses Next from anywhere on this screen: the one-chord proceed.</summary>
        protected override string DefaultAcceptActionId
        {
            get { return "archonexusReformIdeo.confirmAndProceed"; }
        }

        protected override IReadOnlyList<ScreenAction> DeclaredActions
        {
            get
            {
                actions.Clear();
                actions.Add(new ScreenAction((string)"Save".Translate(), SaveAction));
                actions.Add(new ScreenAction((string)"Load".Translate(), LoadAction));
                if (Prefs.DevMode)
                {
                    actions.Add(new ScreenAction(
                        "DEV: Show all", // l10n-exempt: dev-only diagnostic label, matches IdeoUIUtility's own unlocalized dev checkbox text verbatim.
                        ToggleShowAll,
                        check: CurrentShowAllCheckState()));
                    actions.Add(new ScreenAction(
                        "DEV: Edit mode", // l10n-exempt: same precedent.
                        ToggleEditMode,
                        check: CurrentEditModeCheckState()));
                    actions.Add(new ScreenAction("DEV: Single precept", () => editorCore.ActivateDebugSinglePrecept())); // l10n-exempt: mirrors IdeoUIUtility.DoDebugButtons' own unlocalized text verbatim.
                    actions.Add(new ScreenAction("DEV: test descriptions...", () => editorCore.ActivateDebugTestDescriptions())); // l10n-exempt: same precedent.
                    actions.Add(new ScreenAction("DEV: Test names...", () => editorCore.ActivateDebugTestNames())); // l10n-exempt: same precedent.
                }
                if (ArchonexusReformIdeoState.CustomOrLoadedIdeo != null)
                {
                    actions.Add(new ScreenAction((string)"RandomizeAll".Translate(), RandomizeAllAction, "archonexusReformIdeo.randomizeAll"));
                }
                actions.Add(new ScreenAction((string)"Next".Translate(), ConfirmAndProceedAction, "archonexusReformIdeo.confirmAndProceed"));
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
            // Vehicle A: vanilla's own Save button (decompiled DoIdeoSaveLoad :439-445), acting on
            // the browsed ideo.
            IdeoEditorCommands.SaveIdeoligion(SelectedIdeo);
        }

        private void LoadAction()
        {
            // Vehicle A: the dialog's own archonexus load callback (Dialog_ConfigureIdeo.cs:89-92),
            // dialog-scoped rather than tied to the browsed ideo.
            ArchonexusReformIdeoState.LoadSaved();
        }

        private void ToggleShowAll()
        {
            bool next = !(ShowAllField != null && (bool)ShowAllField.GetValue(null));
            // MUTATION-C: mirrors IdeoUIUtility's "DEV: Show all" CheckboxLabeled write (decompiled :254).
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
            RefreshModel();
            var d = new ElementDescription { Check = next ? CheckState.Checked : CheckState.Unchecked };
            TolkHelper.SpeakData(AnnouncementComposer.ComposeStateChange(d, TranslatedShellVocabulary.Instance));
        }

        /// <summary>
        /// MOD-ADDED convenience: decompiled-verified, <c>Dialog_ConfigureIdeo</c> draws no
        /// "Randomize all" button of its own, unlike worldgen's <c>Page_ConfigureIdeo.Randomize</c>.
        /// The vehicle is <see cref="IdeoEditorCommands.RandomizeAll"/>, the same foundation re-init
        /// vanilla's worldgen button uses. Confirmation-gated (randomizing replaces the entire
        /// ideoligion, and an accidental Alt+R must not wipe unsaved work), with a re-entrancy guard.
        /// </summary>
        private void RandomizeAllAction()
        {
            Ideo ideo = ArchonexusReformIdeoState.CustomOrLoadedIdeo;
            if (ideo == null || randomizeConfirmOpen)
            {
                return;
            }
            randomizeConfirmOpen = true;
            Action confirm = delegate
            {
                randomizeConfirmOpen = false;
                if (IdeoEditorCommands.RandomizeAll(ideo))
                {
                    RefreshModel();
                    AnnounceCurrentItem();
                }
            };
            Action cancel = delegate { randomizeConfirmOpen = false; };
            Find.WindowStack.Add(new Dialog_MessageBox(
                (string)"RimWorldAccess.Ideology.Builder.RandomizeAllConfirm".Translate(),
                buttonAText: (string)"RimWorldAccess.Ideology.Builder.RandomizeAllContinue".Translate(),
                buttonAAction: confirm,
                buttonBText: (string)"RimWorldAccess.Ideology.Builder.RandomizeAllCancel".Translate(),
                buttonBAction: cancel,
                title: null,
                buttonADestructive: true,
                acceptAction: confirm,
                cancelAction: cancel));
        }

        private void OnConfirmAndProceed(KeyEventSnapshot e)
        {
            // Without the stamp, vanilla's closeOnAccept closes the dialog on the same frame this
            // commits.
            ShellFrameStamps.MarkAcceptConsumed();
            ConfirmAndProceedAction();
        }

        private void ConfirmAndProceedAction()
        {
            // Vehicle A: the dialog's own "Next" body. No "closed" announcement — the questline
            // flows directly onward into its own screens.
            ArchonexusReformIdeoState.ConfirmAndProceed();
        }

        // ------------------------------------------------------------------
        // Extras haystack.
        // ------------------------------------------------------------------

        protected override IEnumerable<string> AdditionalPresentedTexts
        {
            get
            {
                // A typed row's Label is not automatically fed into the presented haystack, so every
                // vanilla-matching string the rows and declared actions carry is vouched again here
                // or the captured-extras diff mistakes it for an unmirrored mod widget.
                yield return ((string)"CreateNew".Translate()) + "...";
                yield return (string)"CreateFluid".Translate();
                yield return (string)"AssignColonists".Translate();
                yield return (string)"MakeIdeoligionPrimary".Translate();
                yield return (string)"RemoveNewIdeoligion".Translate();
                yield return (string)"Next".Translate();
                yield return (string)"Save".Translate();
                yield return (string)"Load".Translate();
                if (ArchonexusReformIdeoState.CustomOrLoadedIdeo != null)
                {
                    yield return (string)"RandomizeAll".Translate();
                }
                if (Prefs.DevMode)
                {
                    yield return "DEV: Show all"; // l10n-exempt: see DeclaredActions.
                    yield return "DEV: Edit mode"; // l10n-exempt: see DeclaredActions.
                }

                Ideo target = SelectedIdeo;
                bool editable = target != null && ArchonexusReformIdeoState.IsEditable(target);
                foreach (string text in IdeoDetailsPresentedTexts.DescriptionLockIconStates(editAffordancesVisible: editable))
                {
                    yield return text;
                }
                foreach (string text in IdeoDetailsPresentedTexts.EditModeCaption(editAffordancesVisible: editable))
                {
                    yield return text;
                }
                foreach (string text in IdeoDetailsPresentedTexts.AddPreceptButtonLabels(editAffordancesVisible: editable))
                {
                    yield return text;
                }
                foreach (string text in IdeoDetailsPresentedTexts.DeitySectionButtonLabels(editAffordancesVisible: editable))
                {
                    yield return text;
                }
                if (target != null)
                {
                    foreach (string text in IdeoDetailsPresentedTexts.ForIdeo(target))
                    {
                        yield return text;
                    }
                }
            }
        }

        // ------------------------------------------------------------------
        // Lifecycle.
        // ------------------------------------------------------------------

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
            // The tree's ritual-sound Sustainer is a class-level static shared by every host; the
            // read-only branch can start one via the ritual-sound-preview node.
            IdeoDetailsTreeRegion.StopRitualSound();
        }

        /// <summary>Called by IdeoSymbolEditState's shared AfterEdit dispatcher while this scope is the top-of-stack registrant, after a symbol edit opened from an editable-ideo Section row.</summary>
        private void NotifyIdeoEdited(bool announce)
        {
            RefreshModel();
            if (announce)
            {
                AnnounceCurrentItem();
            }
        }

        public override void OnFocus()
        {
            base.OnFocus();
            // An overlay editor pushed over this dialog registers its own follow target and clears
            // it on pop, so regaining focus is where this screen takes it back.
            IdeoBoxDrawPatch.FollowTarget = FollowTargetRect;
            if (announcedOpen)
            {
                RefreshModel();
                Ideo target = SelectedIdeo;
                if (target != null && !ArchonexusReformIdeoState.IsEditable(target))
                {
                    RebuildDetailsTreeForced(target);
                }
                AnnounceCurrentItem();
                return;
            }
            announcedOpen = true;

            RefreshModel();
            Model.MoveToRegion((int)RegionKind.Ideoligions);
            ListModel region = Model.Region((int)RegionKind.Ideoligions);
            if (region != null && !region.IsEmpty)
            {
                int idx = FindRowIndexForIdeo(IdeoUIUtility.selected);
                region.MoveTo(idx >= 0 ? idx : 0);
            }
            AnnounceOpening();
        }

        private static int FindRowIndexForIdeo(Ideo ideo)
        {
            if (ideo == null)
            {
                return -1;
            }
            IReadOnlyList<ArchonexusReformIdeoState.Row> rows = ArchonexusReformIdeoState.Rows;
            for (int i = 0; i < rows.Count; i++)
            {
                if (rows[i].Kind == ArchonexusReformIdeoState.RowKind.Ideo && ReferenceEquals(rows[i].Ideo, ideo))
                {
                    return i;
                }
            }
            return -1;
        }

        private void AnnounceOpening()
        {
            var sb = new StringBuilder();
            sb.Append((string)"ConfigureIdeoligion".Translate());
            ListModel region = Model.Region((int)RegionKind.Ideoligions);
            if (region != null && !region.IsEmpty)
            {
                ElementDescription item = DescribeContentItem((int)RegionKind.Ideoligions, region.Index);
                sb.Append(". ").Append(AnnouncementComposer.ComposeFocus(item, TranslatedShellVocabulary.Instance, TextDialogShared.StandardComposeOptions()));
                string position = MenuHelper.FormatPosition(region.Index, ArchonexusReformIdeoState.Rows.Count);
                if (!string.IsNullOrEmpty(position))
                {
                    sb.Append(". ").Append(position);
                }
            }
            TolkHelper.SpeakData(sb.ToString(), SpeechPriority.High);
        }
    }
}
