using System.Collections.Generic;
using System.Linq;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.Sound;

namespace RimWorldAccess.Shell
{
    /// <summary>
    /// Windowless <see cref="TreeRegionScope"/> for editing one typed precept list (roles, rituals,
    /// buildings, relics, weapons, venerated animals, preferred xenotypes, apparel). One region:
    /// row 0 is the "Add {type}" prefix row, deliberately NOT a tree node so it never pretends to be
    /// a level of the tree; below it, <see cref="IdeoTypedPreceptState.BuildTree"/> gives one node
    /// per precept expanding into one detail row each. The whole tree grammar comes off the base.
    ///
    /// A precept node carries <see cref="ElementRole.Button"/>, sets
    /// <see cref="ElementDescription.Expanded"/> only when it really has detail rows, and folds its
    /// detail text into <see cref="ElementDescription.Extras"/> ONLY while collapsed — expanded, the
    /// child rows carry it. Detail rows speak a plain Label with no role.
    ///
    /// Enter and "]" both open the edit-actions menu, resolving the target precept by climbing from
    /// a focused detail row to its parent. That menu stays open until Escape: every entry returns
    /// through <see cref="ReturnToEditMenu"/>, so an inline weapon or apparel edit does not drop the
    /// player back onto the tree. <c>ideoOverlayEditor.expandAllSiblings</c> is not claimed — the
    /// base's <c>tree.expandAllSiblings</c> carries the identical chords and claiming both would
    /// double-claim a chord.
    ///
    /// Every mutation path (menu edit, dialog edit, add, delete) goes through
    /// <see cref="RebuildTreePreservingState"/>, which keeps other precepts' expansion state and
    /// lands the cursor back on the same logical node, clamping numerically when that node was the
    /// one deleted. After the dialog closes that rebuild runs in <see cref="OnFocus"/> and the
    /// re-announcement reads the precept's new name — the confirmation, so nothing suppresses it.
    /// </summary>
    public sealed class IdeoTypedPreceptScreenScope : TreeRegionScope
    {
        private readonly List<ScreenAction> actions = new List<ScreenAction>();
        private bool everFocused;

        /// <summary>Set only across <see cref="AnnounceOpening"/>, so the screen name rides into the first landing as one utterance.</summary>
        private bool pendingOpeningHeader;

        /// <summary>Section-crossing tracker: the base's parent-boundary tracking applied to a row's
        /// owning section instead. The first landing after a reset stays silent because a rebuild lands
        /// the cursor back on the same logical row.</summary>
        private readonly SectionPrefixTracker sectionPrefix = new SectionPrefixTracker(trackSilentRows: true, speakFirstLanding: false);

        public IdeoTypedPreceptScreenScope()
        {
            Claim(SharedMenuGrammar.Cancel, e =>
            {
                ShellFrameStamps.MarkCancelConsumed();
                CloseAndReturn();
            }, when: () => !TypeaheadHasActiveSearch);
            Claim(SharedMenuGrammar.Info, OnInfo);
            Claim("ideoOverlayEditor.delete", OnDelete);
            Claim("ideoOverlayEditor.contextMenu", e => OpenEditMenuFor(CurrentPrecept()));
            // The base deliberately leaves this pair unclaimed, so opt in here.
            Claim("tree.jumpToPreviousSection", e => PerformJumpToAdjacentSection(false));
            Claim("tree.jumpToNextSection", e => PerformJumpToAdjacentSection(true));
        }

        public override string Name
        {
            get { return "ideo-typed-precept"; }
        }

        public override bool OwnsCancel
        {
            get { return true; }
        }

        /// <summary>No draw pass of its own.</summary>
        protected override bool IncludeCapturedExtrasRegion
        {
            get { return false; }
        }

        protected override bool CaptureWindowButtons
        {
            get { return false; }
        }

        protected override string TreeRegionLabel
        {
            get { return IdeoTypedPreceptState.SectionLabel; }
        }

        public override void OnPush()
        {
            base.OnPush();
            everFocused = false;
            // A long-lived singleton reused across every SectionKind, so last session's tree must
            // never survive into this one; RefreshContent rebuilds before the first announcement.
            ResetTree();
            ResetSectionAnnounceTracking();
            // Windowless: the ring rides the host page's precept boxes as vanilla draws them, never
            // FocusedContentRect.
            IdeoBoxDrawPatch.AddInterest();
            IdeoBoxDrawPatch.PreceptRingProvider = RingTargetPrecept;
            IdeoBoxDrawPatch.FollowTarget = FollowTargetRect;
        }

        public override void OnPop()
        {
            IdeoBoxDrawPatch.PreceptRingProvider = null;
            IdeoBoxDrawPatch.FollowTarget = null;
            IdeoBoxDrawPatch.RemoveInterest();
            base.OnPop();
        }

        /// <summary>The focused precept's box in the host pane's own scroll space, for scroll-follow.</summary>
        private Rect? FollowTargetRect()
        {
            return IdeoBoxDrawPatch.RawRectFor(RingTargetPrecept());
        }

        protected override void OnCursorSettled(int region, int index)
        {
            base.OnCursorSettled(region, index);
            IdeoBoxDrawPatch.RequestFollow();
        }

        public override void OnFocus()
        {
            base.OnFocus();
            if (!everFocused)
            {
                everFocused = true;
                // The Model persists across every open/close cycle, unlike a window-attached scope's,
                // so the cursor must be forced back to the first content row: a region cursor parked
                // on the toolbar would otherwise poison every later open.
                Model.MoveToRegion(0);
                ListModel firstRegion = Model.CurrentRegion;
                if (firstRegion != null)
                {
                    firstRegion.MoveFirst();
                }
                AnnounceOpening();
                return;
            }
            // Inline edits, dialog edits and added precepts all come back through here, so the
            // rebuild must run BEFORE the announcement decision. IdeoTypedPreceptState suppresses the
            // announcement for one frame when an edit already spoke or is about to open the dialog.
            RebuildTreePreservingState();
            if (IdeoTypedPreceptState.ShouldReannounceOnReturn())
            {
                AnnounceCurrentItem();
            }
        }

        /// <summary>The base call is where pre-search expansion state is restored, so it is not
        /// optional; the lazy build covers the first refresh after <see cref="OnPush"/> cleared the
        /// tree.</summary>
        protected override void RefreshContent()
        {
            base.RefreshContent();
            if (IdeoTypedPreceptState.IsActive && Tree.Root == null)
            {
                SetTreeRoot(IdeoTypedPreceptState.BuildTree());
            }
        }

        /// <summary>Rebuilds after a mutation, keeping expansion state and landing the cursor on the
        /// same logical node (a numeric clamp when the preserver returns -1 for a deleted node). Never
        /// announces: the caller owns the one utterance.</summary>
        private void RebuildTreePreservingState()
        {
            RefreshModel();
            ListModel region = Model.CurrentRegion;
            int before = region != null && !region.IsEmpty ? region.Index : 0;
            int restored = SetTreeRootPreservingState(IdeoTypedPreceptState.BuildTree(), before);
            // A rebuild's nodes are new references even when the data is unchanged, so a surviving
            // section tracker would compare against titles whose nodes no longer exist.
            ResetSectionAnnounceTracking();
            RefreshModel();
            ListModel after = Model.CurrentRegion;
            if (after != null && !after.IsEmpty)
            {
                after.MoveTo(Mathf.Clamp(restored >= 0 ? restored : before, 0, after.Count - 1));
            }
        }

        /// <summary>Clears the section-crossing tracker; call alongside every tree rebuild.</summary>
        private void ResetSectionAnnounceTracking()
        {
            sectionPrefix.Reset();
        }

        protected override int PrefixRowCount
        {
            get { return 1; }
        }

        protected override ElementDescription DescribePrefixRow(int index)
        {
            return new ElementDescription
            {
                Label = (string)"Add".Translate() + " " + IdeoTypedPreceptState.SectionLabel,
                Role = ElementRole.Button,
            };
        }

        protected override void ActivatePrefixRow(int index)
        {
            IdeoTypedPreceptState.InvokeAddPrecept();
        }

        // Tree hooks. Level and PositionIndex are deliberately left unset: the base fills both,
        // sibling-relative.

        protected override ElementDescription DescribeTreeNode(InspectionTreeItem item)
        {
            var precept = item.Data as Precept;
            if (precept == null)
            {
                return new ElementDescription { Label = item.Label };
            }
            var d = new ElementDescription
            {
                Label = item.Label,
                Role = ElementRole.Button,
            };
            if (item.IsExpandable)
            {
                d.Expanded = item.IsExpanded;
                if (!item.IsExpanded)
                {
                    // Built from BuildPreceptDetailLines rather than item.Children because header
                    // lines are not children: children alone would drop every section title. Expanded,
                    // the child rows carry these lines, so folding them in again would double-speak.
                    d.Extras = string.Join(". ", IdeoTypedPreceptState.BuildPreceptDetailLines(precept).Select(l => l.Text));
                }
            }
            AppendInspectableHint(d, InspectableDefsForPrecept(precept));
            return d;
        }

        protected override void ActivateTreeNode(InspectionTreeItem item)
        {
            var precept = item.Data as Precept;
            if (precept == null)
            {
                // A detail line has nothing to open; Left/Right already expand and collapse.
                AnnounceCurrentItem();
                return;
            }
            OpenEditMenuFor(precept);
        }

        /// <summary>A precept's children are tip lines typeahead excludes anyway, so there is nothing
        /// worth auto-expanding.</summary>
        protected override bool ShouldAutoExpandForSearch(InspectionTreeItem item)
        {
            return false;
        }

        /// <summary>Detail rows stay navigable and spoken but out of the match set, keeping typeahead
        /// at the precept level; the Add row keeps the base's default and stays searchable.</summary>
        protected override bool ContentRowSearchable(int region, int row)
        {
            int treeIndex = row - PrefixRowCount;
            if (treeIndex < 0)
            {
                return base.ContentRowSearchable(region, row);
            }
            if (treeIndex >= Tree.Count)
            {
                return false;
            }
            return Tree.Visible[treeIndex].Type != InspectionTreeItem.ItemType.DetailText;
        }

        /// <summary>Matches a node's own label rather than its composed announcement, keeping expansion
        /// words and folded-in detail text out of the haystack.</summary>
        protected override string ContentRowSearchText(int region, int row)
        {
            int treeIndex = row - PrefixRowCount;
            if (treeIndex >= 0 && treeIndex < Tree.Count)
            {
                return Tree.Visible[treeIndex].Label ?? "";
            }
            return base.ContentRowSearchText(region, row);
        }

        /// <summary>Section boundaries for the Page Up/Down jump, reachable only while a precept is
        /// expanded. Section headers are not rows of their own, so a boundary is each section's first
        /// REAL row: one with a SectionTitle whose previous sibling has none or a different one. A
        /// sectionless row is never a boundary.</summary>
        protected override bool IsSectionBoundary(InspectionTreeItem item)
        {
            if (string.IsNullOrEmpty(item.SectionTitle))
            {
                return false;
            }
            var siblings = item.Parent != null ? item.Parent.Children : null;
            if (siblings == null)
            {
                return false;
            }
            int idx = siblings.IndexOf(item);
            if (idx <= 0)
            {
                return true;
            }
            return siblings[idx - 1].SectionTitle != item.SectionTitle;
        }

        // The "]" / Enter edit-actions menu.

        /// <summary>The precept the cursor rests on, climbing from a focused detail row to its parent.
        /// Null on the Add row or when nothing is focused.</summary>
        private Precept CurrentPrecept()
        {
            RefreshModel();
            return RingTargetPrecept();
        }

        /// <summary>The same climb without the model refresh — this runs inside vanilla's draw
        /// pass, where rebuilding the tree per precept box is not an option.</summary>
        private Precept RingTargetPrecept()
        {
            InspectionTreeItem item = CurrentTreeItem();
            if (item == null)
            {
                return null;
            }
            var precept = item.Data as Precept;
            if (precept != null)
            {
                return precept;
            }
            return item.Parent != null ? item.Parent.Data as Precept : null;
        }

        private void OpenEditMenuFor(Precept precept)
        {
            OpenEditMenuFor(precept, 0, (string)"Edit".Translate() + " " + IdeoBuilderHelper.PreceptLabel(precept));
        }

        /// <summary>Opens or re-opens the edit-actions menu. Every entry calls back into
        /// <see cref="ReturnToEditMenu"/> with its own index, so only Escape closes the menu.
        /// <paramref name="heading"/> is folded into the landing entry's announcement as ONE
        /// utterance.</summary>
        private void OpenEditMenuFor(Precept precept, int startIndex, string heading)
        {
            if (precept == null)
            {
                SoundDefOf.ClickReject.PlayOneShotOnCamera();
                return;
            }
            List<FloatMenuOption> options = IdeoTypedPreceptState.BuildEditOptions(
                precept, (entry, entryHeading) => ReturnToEditMenu(precept, entry, entryHeading));
            if (options.Count == 0)
            {
                SoundDefOf.ClickReject.PlayOneShotOnCamera();
                TolkHelper.Speak("RimWorldAccess.Ideology.Builder.NoEditOptions".Loc());
                return;
            }
            // announceSelection: false — an entry's own action, or what it opens, owns the utterance.
            WindowlessFloatMenuState.Open(options, colonistOrders: false, startIndex: startIndex,
                announceSelection: false, titleText: heading);
        }

        /// <summary>Re-opens the edit menu on the entry an inline edit was just made in, guarded so an
        /// entry that opened a window of its own keeps it on top.</summary>
        private void ReturnToEditMenu(Precept precept, int entryIndex, string heading)
        {
            if (WindowlessFloatMenuState.IsActive)
            {
                return;
            }
            OpenEditMenuFor(precept, entryIndex, heading);
        }

        private void OnDelete(KeyEventSnapshot e)
        {
            Precept precept = CurrentPrecept();
            if (precept == null)
            {
                return;
            }
            if (IdeoTypedPreceptState.TryDeletePrecept(precept))
            {
                // TryDeletePrecept already spoke the "removed" confirmation.
                RebuildTreePreservingState();
            }
        }

        // Info card drill-in. Vanilla never opens Dialog_InfoCard for a PreceptDef itself, so only a
        // precept's linked def (a ThingDef, a xenotype) is ever offered; no linked def, no target.

        private void OnInfo(KeyEventSnapshot e)
        {
            RefreshModel();
            InspectionTreeItem item = CurrentTreeItem();
            Precept precept = item != null ? item.Data as Precept : null;
            List<Def> defs = precept != null
                ? InspectableDefsForPrecept(precept)
                : new List<Def>();
            PreceptInspectionHelper.ShowPicker(defs);
        }

        private static List<Def> InspectableDefsForPrecept(Precept precept)
        {
            var result = new List<Def>();
            Def linked = IdeoTypedPreceptState.LinkedDefFor(precept);
            if (linked != null)
            {
                result.Add(linked);
            }
            return result;
        }

        private static void AppendInspectableHint(ElementDescription d, List<Def> defs)
        {
            if (defs.Count > 0)
            {
                d.Extras = (d.Extras ?? "") + (string)"RimWorldAccess.InfoCard.Inspectable".Translate();
            }
        }

        protected override IReadOnlyList<ScreenAction> DeclaredActions
        {
            get
            {
                actions.Clear();
                actions.Add(new ScreenAction("Back".Translate(), CloseAndReturn, SharedMenuGrammar.Cancel));
                return actions;
            }
        }

        private void CloseAndReturn()
        {
            // Speaks nothing of its own: the overlay is reachable from more than one host, so it must
            // not name either; the host beneath re-announces on regaining focus.
            IdeoTypedPreceptState.Close();
            SoundDefOf.TabClose.PlayOneShotOnCamera();
        }

        /// <summary>ONE utterance: the screen name and precept count fold into the first row's landing
        /// announcement through <see cref="AnnouncePrefix"/> rather than being spoken separately.</summary>
        private void AnnounceOpening()
        {
            pendingOpeningHeader = true;
            AnnounceCurrentItem();
            // Never left pending for a later landing: either the compose path above consumed it, or
            // there was no row to carry it and it is spoken alone.
            if (pendingOpeningHeader)
            {
                pendingOpeningHeader = false;
                TolkHelper.SpeakData(OpeningHeader(), SpeechPriority.High);
            }
        }

        /// <summary>The screen's name — the same label the hub row that opened it carries — plus how
        /// many precepts of this kind the ideoligion has.</summary>
        private string OpeningHeader()
        {
            string tabCount = TabCountFragment();
            string title = IdeoTypedPreceptState.SectionLabel;
            string opening = string.IsNullOrEmpty(tabCount) ? title : tabCount + ". " + title;
            return opening + ". " + IdeoTypedPreceptState.CurrentPrecepts().Count;
        }

        /// <summary>Folds the opening header into the first landing announcement. Both the base's
        /// boundary prefix and this scope's section prefix are computed unconditionally first, because
        /// both track state.</summary>
        protected override string AnnouncePrefix(int region, int index)
        {
            string boundary = base.AnnouncePrefix(region, index);
            string sectionPrefix = SectionBoundaryPrefix();
            string combined = CombinePrefixFragments(boundary, sectionPrefix);
            if (!pendingOpeningHeader)
            {
                return combined;
            }
            pendingOpeningHeader = false;
            string header = OpeningHeader();
            if (string.IsNullOrEmpty(header))
            {
                return combined;
            }
            return string.IsNullOrEmpty(combined) ? header : header + ". " + combined;
        }

        /// <summary>The section title, spoken once, when the cursor lands on a row whose SectionTitle
        /// differs from the previously-landed one's. Reannouncing in place and the first landing after
        /// a rebuild stay silent: neither is a genuine crossing.</summary>
        private string SectionBoundaryPrefix()
        {
            InspectionTreeItem item = CurrentTreeItem();
            if (item == null)
            {
                return null;
            }
            return sectionPrefix.Cross(item.SectionTitle);
        }
    }
}
