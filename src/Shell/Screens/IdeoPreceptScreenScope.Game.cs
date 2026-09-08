using System.Collections.Generic;
using System.Linq;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.Sound;

namespace RimWorldAccess.Shell
{
    /// <summary>
    /// Windowless overlay for editing an ideoligion's issue-based precepts, a
    /// <see cref="TreeRegionScope"/>. Opened from the Custom-creation hub, the in-game reform
    /// dialog and the Archonexus reform screen, all through
    /// <see cref="IdeoPreceptSelectionState.Open"/>; pushed and popped by
    /// <see cref="IdeoOverlayScopesMirror"/>.
    ///
    /// <b>Windowless.</b> The scope draws nothing of its own and sits on top of whichever host
    /// page is still rendering beneath it, so the WidgetCapture bracket never arms for it.
    /// <see cref="IncludeCapturedExtrasRegion"/> and <see cref="CaptureWindowButtons"/> are
    /// explicitly false to say why: the host owns its own capture pass, and scraping its buttons
    /// while this modal overlay masks it would be meaningless. <see cref="OwnsCancel"/> is
    /// unconditional (the base default is search-only) so Escape closes THIS overlay and never
    /// also reaches the host window beneath.
    ///
    /// <b>The tree.</b> One region holding <see cref="IdeoPreceptSelectionHelper.BuildTree"/>: a
    /// collapsible node per non-empty section (Active precepts expanded, Not set collapsed), each
    /// holding its issues, each issue expanding into its detail lines. Navigation, section jumps
    /// and sibling expansion are all the shared tree grammar off the base.
    ///
    /// <b>Announcement grammar.</b> A section speaks its bare name plus the shared expansion
    /// suffix carrying its child count, with Role/Check/Selected/Expanded unset so the state word
    /// is spoken once. An issue rides <see cref="ElementRole.ComboBox"/> with its current
    /// precept(s) as <see cref="ElementDescription.Value"/>, sets
    /// <see cref="ElementDescription.Expanded"/> only when it really has detail rows, and folds
    /// its detail text into <see cref="ElementDescription.Extras"/> ONLY while collapsed. A detail
    /// row speaks its plain Label with no role and no ReadOnly flag.
    ///
    /// Enter on an issue opens the value picker and is the ONLY way the value changes — Enter
    /// opening a list of choices IS this shell's combo-box grammar, which is why the row keeps the
    /// ComboBox role even though Left/Right belong to the tree. Left/Right never step an issue to
    /// another precept, and Delete is the dedicated removal path, matching a picker that offers no
    /// bare removal option either.
    ///
    /// <b>Rebuild preservation.</b> A precept edit restructures the tree. Within a section the
    /// cursor follows the issue's NEW node, re-found by Data reference after
    /// <see cref="TreeRegionScope.SetTreeRootPreservingState"/> (the path preserver would park it
    /// on the section header). Across sections the cursor stays put, on the row that took the
    /// issue's place, so a run of adds or removes walks the list.
    /// </summary>
    public sealed class IdeoPreceptScreenScope : TreeRegionScope
    {
        private readonly List<ScreenAction> actions = new List<ScreenAction>();
        private bool announcedOpen;

        /// <summary>Set only across <see cref="AnnounceOpening"/>, so the screen name rides into the first landing as one utterance.</summary>
        private bool pendingOpeningHeader;

        public IdeoPreceptScreenScope()
        {
            // Escape closes the overlay outright — no chained "back a stage" here. The base's
            // typeahead-clear claim, registered first, wins while a search is active.
            Claim(SharedMenuGrammar.Cancel, e =>
            {
                ShellFrameStamps.MarkCancelConsumed();
                CloseAndReturn();
            }, when: () => !TypeaheadHasActiveSearch);
            Claim(SharedMenuGrammar.Info, OnInfo);
            // This screen's reserved id and the shared overlay-editor family's id carry the same
            // Delete chord; both are claimed so neither rebind entry is orphaned.
            Claim("ideoPreceptSelection.removePrecept", OnDelete);
            Claim("ideoOverlayEditor.delete", OnDelete);
            // The base deliberately leaves Page Up/Down unclaimed, so opt in here.
            Claim("tree.jumpToPreviousSection", e => PerformJumpToAdjacentSection(false));
            Claim("tree.jumpToNextSection", e => PerformJumpToAdjacentSection(true));
        }

        public override string Name
        {
            get { return "ideo-precept-selection"; }
        }

        /// <summary>Unconditional: this overlay always blocks the host window's own Escape while it's the top live scope (see class remarks).</summary>
        public override bool OwnsCancel
        {
            get { return true; }
        }

        /// <summary>No draw pass of its own — see class remarks.</summary>
        protected override bool IncludeCapturedExtrasRegion
        {
            get { return false; }
        }

        /// <summary>No window to scrape buttons from.</summary>
        protected override bool CaptureWindowButtons
        {
            get { return false; }
        }

        protected override string TreeRegionLabel
        {
            get { return (string)"Precepts".Translate(); }
        }

        public override void OnPush()
        {
            base.OnPush();
            announcedOpen = false;
            // A long-lived singleton whose Model and tree survive every open/close cycle, so last
            // session's tree must never persist into this one.
            ResetTree();
            Ideo ideo = IdeoPreceptSelectionState.Ideo;
            if (ideo != null)
            {
                SetTreeRoot(IdeoPreceptSelectionHelper.BuildTree(ideo));
            }
            // The ring rides the host page's own precept boxes as vanilla draws them, never
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

        /// <summary>The focused issue's precept box in the host pane's own scroll space, for scroll-follow.</summary>
        private Rect? FollowTargetRect()
        {
            return IdeoBoxDrawPatch.RawRectFor(RingTargetPrecept());
        }

        protected override void OnCursorSettled(int region, int index)
        {
            base.OnCursorSettled(region, index);
            IdeoBoxDrawPatch.RequestFollow();
        }

        /// <summary>
        /// The precept box the ring belongs on: the focused issue's current precept, climbing
        /// from a detail row to its issue. An issue with several precepts rings the first, and a
        /// "Not set" issue has no box on the host page at all, so it rings nothing.
        /// </summary>
        private Precept RingTargetPrecept()
        {
            IssueDef issue = IssueFor(CurrentTreeItem());
            if (issue == null)
            {
                return null;
            }
            List<Precept> current = IdeoPreceptSelectionHelper.CurrentPreceptsForIssue(
                IdeoPreceptSelectionState.Ideo, issue);
            return current.Count > 0 ? current[0] : null;
        }

        public override void OnFocus()
        {
            base.OnFocus();
            if (announcedOpen)
            {
                return;
            }
            announcedOpen = true;
            // Force the cursor back to the first content row every time. Runs strictly BEFORE
            // AnnounceOpening, so it never fights the cursor restores elsewhere in this file,
            // which only run in response to an in-session mutation.
            Model.MoveToRegion(0);
            ListModel firstRegion = Model.CurrentRegion;
            if (firstRegion != null)
            {
                firstRegion.MoveFirst();
            }
            AnnounceOpening();
        }

        /// <summary>
        /// The base call is non-negotiable — it is where the pre-search expansion state is
        /// restored. The lazy build covers a Reconcile that beats OnPush's own build.
        /// </summary>
        protected override void RefreshContent()
        {
            base.RefreshContent();
            Ideo ideo = IdeoPreceptSelectionState.Ideo;
            if (Tree.Root == null && ideo != null)
            {
                SetTreeRoot(IdeoPreceptSelectionHelper.BuildTree(ideo));
            }
        }

        // Level and PositionIndex are deliberately left unset — the base fills both,
        // sibling-relative.

        protected override ElementDescription DescribeTreeNode(InspectionTreeItem item)
        {
            if (item.Data is string)
            {
                // A section (Data is the English const token). The expansion suffix is the ONE
                // channel carrying the child count, so the state fields stay unset.
                return new ElementDescription
                {
                    Label = item.Label + TreeNavigationHelper.FormatExpansionSuffix(item, includeChildCount: true),
                };
            }

            var issue = item.Data as IssueDef;
            if (issue == null)
            {
                return new ElementDescription { Label = item.Label };
            }

            Ideo ideo = IdeoPreceptSelectionState.Ideo;
            List<Precept> current = IdeoPreceptSelectionHelper.CurrentPreceptsForIssue(ideo, issue);
            var d = new ElementDescription
            {
                Label = issue.LabelCap,
                Role = ElementRole.ComboBox,
                Value = current.Count == 0
                    ? (string)"None".Translate()
                    : string.Join(", ", current.Select(p => (string)p.def.LabelCap)),
            };
            // Gated on real children, not IsExpandable: the builder marks every issue expandable,
            // but an unset issue has no detail lines and must never say "collapsed".
            if (item.Children.Count > 0)
            {
                d.Expanded = item.IsExpanded;
                if (!item.IsExpanded)
                {
                    // Expanded, the child rows carry these same lines already.
                    d.Extras = string.Join(". ", item.Children.Select(c => c.Label));
                }
            }
            AppendInspectableHint(d, InspectableDefsForIssue(ideo, issue));
            return d;
        }

        protected override void ActivateTreeNode(InspectionTreeItem item)
        {
            var issue = item.Data as IssueDef;
            if (issue == null)
            {
                AnnounceCurrentItem();
                return;
            }
            OpenValuePicker(issue);
        }

        /// <summary>The two sections open on the first typed character so issues inside them stay reachable.</summary>
        protected override bool ShouldAutoExpandForSearch(InspectionTreeItem item)
        {
            return item.IndentLevel == 0;
        }

        /// <summary>
        /// Detail rows stay navigable and spoken but out of the match set, keeping typeahead at the
        /// section/issue level people search by instead of burying real targets under tip lines.
        /// </summary>
        protected override bool ContentRowSearchable(int region, int row)
        {
            int treeIndex = row - PrefixRowCount;
            if (treeIndex < 0 || treeIndex >= Tree.Count)
            {
                return false;
            }
            return Tree.Visible[treeIndex].Type != InspectionTreeItem.ItemType.DetailText;
        }

        /// <summary>
        /// Matches a node's own label rather than its composed announcement: a section's spoken
        /// label carries the expansion suffix and child count, which must never enter the
        /// haystack. An issue's label is "Corpses: Acceptable", so its value is searchable too.
        /// </summary>
        protected override string ContentRowSearchText(int region, int row)
        {
            int treeIndex = row - PrefixRowCount;
            if (treeIndex >= 0 && treeIndex < Tree.Count)
            {
                return Tree.Visible[treeIndex].Label ?? "";
            }
            return base.ContentRowSearchText(region, row);
        }

        /// <summary>Page Up/Down jump between the Active precepts and Not set sections.</summary>
        protected override bool IsSectionBoundary(InspectionTreeItem item)
        {
            return item.IndentLevel == 0;
        }

        private void OpenValuePicker(IssueDef issue)
        {
            Ideo ideo = IdeoPreceptSelectionState.Ideo;
            List<FloatMenuOption> options = IdeoPreceptSelectionHelper.BuildValuePickerOptions(
                ideo, issue, () => HandleIssueChanged(issue));
            TolkHelper.SpeakData(issue.LabelCap);
            // Suppress the float menu's generic echo: the option text (value, impact, description)
            // is too much on commit, and AnnouncePreceptSet gives a concise confirmation instead.
            WindowlessFloatMenuState.Open(options, colonistOrders: false, announceSelection: false);
        }

        /// <summary>Delete: removes the focused issue's current precept(s).</summary>
        private void OnDelete(KeyEventSnapshot e)
        {
            RefreshModel();
            IssueDef issue = IssueFor(CurrentTreeItem());
            if (issue == null)
            {
                return;
            }
            if (IdeoPreceptSelectionState.TryRemovePrecept(issue))
            {
                HandleIssueChanged(issue);
            }
        }

        /// <summary>The issue a row belongs to: the issue node itself, or the issue owning a detail child.</summary>
        private static IssueDef IssueFor(InspectionTreeItem item)
        {
            if (item == null)
            {
                return null;
            }
            var issue = item.Data as IssueDef;
            if (issue != null)
            {
                return issue;
            }
            return item.Parent != null ? item.Parent.Data as IssueDef : null;
        }

        /// <summary>Rebuilds preserving state, lands per the header's rule, announces once (plus the landing row when the cursor stayed).</summary>
        private void HandleIssueChanged(IssueDef issue)
        {
            RefreshModel();
            // A cursor outside the tree region has no tree index to preserve; -1 says so, rather
            // than letting a Buttons row masquerade as a node.
            bool cursorInTree = Model.RegionIndex == 0;
            ListModel region = Model.CurrentRegion;
            int cursorBefore = cursorInTree && region != null && !region.IsEmpty ? region.Index : -1;
            InspectionTreeItem before = FindNodeByData(issue);
            bool wasExpanded = before != null && before.IsExpanded;
            InspectionTreeItem sectionNodeBefore = before != null ? before.Parent : null;
            string sectionBefore = sectionNodeBefore != null ? sectionNodeBefore.Label : null;
            object sectionTokenBefore = sectionNodeBefore != null ? sectionNodeBefore.Data : null;
            int siblingIndexBefore = sectionNodeBefore != null ? sectionNodeBefore.Children.IndexOf(before) : -1;

            int restored = SetTreeRootPreservingState(
                IdeoPreceptSelectionHelper.BuildTree(IdeoPreceptSelectionState.Ideo), cursorBefore);

            // A section move is the one case the path-matching preserver cannot follow.
            InspectionTreeItem moved = FindNodeByData(issue);
            bool movedSection = moved != null && moved.Parent != null
                && !Equals(moved.Parent.Data, sectionTokenBefore);
            InspectionTreeItem stayTarget = movedSection && cursorInTree
                ? RowTakingPlaceOf(sectionTokenBefore, siblingIndexBefore)
                : null;
            bool landed = false;
            if (stayTarget != null)
            {
                landed = TryRevealAndSelect(stayTarget);
            }
            else if (moved != null)
            {
                moved.IsExpanded = wasExpanded && moved.Children.Count > 0;
                landed = TryRevealAndSelect(moved);
            }
            RefreshModel();
            if (landed)
            {
                SyncRegionFromCurrentTree();
            }
            else if (cursorInTree)
            {
                ListModel after = Model.CurrentRegion;
                if (after != null && !after.IsEmpty)
                {
                    int target = restored >= 0 ? restored : cursorBefore;
                    if (target < 0)
                    {
                        target = 0;
                    }
                    if (target > after.Count - 1)
                    {
                        target = after.Count - 1;
                    }
                    after.MoveTo(target);
                }
            }
            AnnouncePreceptSet(issue, moved, sectionBefore);
            if (stayTarget != null && landed)
            {
                AnnounceCurrentItem();
            }
        }

        /// <summary>The row now at <paramref name="siblingIndex"/> (clamped) in the section with this token; null when it has no rows.</summary>
        private InspectionTreeItem RowTakingPlaceOf(object sectionToken, int siblingIndex)
        {
            if (sectionToken == null || siblingIndex < 0 || Tree.Root == null)
            {
                return null;
            }
            InspectionTreeItem section = Tree.Root.Children
                .FirstOrDefault(c => Equals(c.Data, sectionToken));
            if (section == null || section.Children.Count == 0)
            {
                return null;
            }
            return section.Children[Mathf.Min(siblingIndex, section.Children.Count - 1)];
        }

        private InspectionTreeItem FindNodeByData(object data)
        {
            return Tree.Root != null ? FindNodeByData(Tree.Root, data) : null;
        }

        private static InspectionTreeItem FindNodeByData(InspectionTreeItem parent, object data)
        {
            for (int i = 0; i < parent.Children.Count; i++)
            {
                InspectionTreeItem child = parent.Children[i];
                if (ReferenceEquals(child.Data, data))
                {
                    return child;
                }
                InspectionTreeItem found = FindNodeByData(child, data);
                if (found != null)
                {
                    return found;
                }
            }
            return null;
        }

        private void AnnouncePreceptSet(IssueDef issue, InspectionTreeItem node, string sectionBefore)
        {
            Ideo ideo = IdeoPreceptSelectionState.Ideo;
            List<Precept> current = IdeoPreceptSelectionHelper.CurrentPreceptsForIssue(ideo, issue);
            string label = IdeoPreceptSelectionHelper.BuildIssueLabel(issue, current);
            string status = (current.Count > 0
                ? "RimWorldAccess.Ideology.Builder.Status.Selected"
                : "RimWorldAccess.Ideology.Builder.Status.Removed").Translate();
            var parts = new List<string> { label + ", " + status };
            // Supplementary context, spoken only when it CHANGED — the edit moved the issue
            // between Active and Not-set. A value change within a section stays one utterance.
            string sectionNow = node != null && node.Parent != null ? node.Parent.Label : null;
            if (!string.IsNullOrEmpty(sectionNow) && sectionNow != sectionBefore)
            {
                parts.Add(sectionNow);
            }
            TolkHelper.SpeakData(string.Join(". ", parts), SpeechPriority.High);
        }

        // Alt+I offers only a precept's linked Def, never the precept itself: vanilla never opens
        // Dialog_InfoCard for a PreceptDef. An issue with no linked Def yields no target.

        private void OnInfo(KeyEventSnapshot e)
        {
            RefreshModel();
            InspectionTreeItem item = CurrentTreeItem();
            IssueDef issue = item != null ? item.Data as IssueDef : null;
            List<Def> defs = issue != null
                ? InspectableDefsForIssue(IdeoPreceptSelectionState.Ideo, issue)
                : new List<Def>();
            PreceptInspectionHelper.ShowPicker(defs);
        }

        /// <summary>An issue row's inspectable defs: its current precept(s)' linked Def(s), where the game gives one; none while unset or linkless.</summary>
        private static List<Def> InspectableDefsForIssue(Ideo ideo, IssueDef issue)
        {
            var result = new List<Def>();
            foreach (Precept p in IdeoPreceptSelectionHelper.CurrentPreceptsForIssue(ideo, issue))
            {
                Def linked = IdeoTypedPreceptState.LinkedDefFor(p);
                if (linked != null)
                {
                    result.Add(linked);
                }
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
            // Speaks nothing of its own: the overlay is reachable from several hosts and must not
            // name any of them; the host beneath re-announces on regaining focus.
            IdeoPreceptSelectionState.Close();
            SoundDefOf.TabClose.PlayOneShotOnCamera();
        }

        /// <summary>
        /// ONE utterance: the screen name, its active/not-set summary and the section the cursor
        /// lands in all fold into the first row's own landing announcement through
        /// <see cref="AnnouncePrefix"/>.
        /// </summary>
        private void AnnounceOpening()
        {
            pendingOpeningHeader = true;
            AnnounceCurrentItem();
            // Never left pending for a later landing: either the compose path above consumed it
            // synchronously, or there was no row to carry it and it is spoken alone.
            if (pendingOpeningHeader)
            {
                pendingOpeningHeader = false;
                TolkHelper.SpeakData(OpeningHeader(), SpeechPriority.High);
            }
        }

        /// <summary>
        /// The screen's own name — the same label the hub row that opened it carries — plus the
        /// active/not-set summary and the section the landing row belongs to. The section belongs
        /// here because the tree runs in submenu mode, where an expanded section's own row is
        /// hidden and the base suppresses its boundary prefix on the first landing after a
        /// rebuild; without it the cursor opens inside Active precepts on a row that never says so.
        /// </summary>
        private string OpeningHeader()
        {
            Ideo ideo = IdeoPreceptSelectionState.Ideo;
            List<IssueDef> issues = IdeoPreceptSelectionHelper.ConfigurableIssues(ideo);
            int activeCount = issues.Count(i => IdeoPreceptSelectionHelper.CurrentPreceptsForIssue(ideo, i).Count > 0);
            string tabCount = TabCountFragment();
            string title = IdeoBuilderHelper.GetLocalizedSectionLabel(IdeoBuilderHelper.SectionKind.Precepts);
            string summary = (string)"RimWorldAccess.Ideology.Builder.PreceptSummary".Translate(activeCount, issues.Count - activeCount);
            string opening = string.IsNullOrEmpty(tabCount) ? title : tabCount + ". " + title;
            opening += ". " + summary;
            string section = LandingSectionName();
            return string.IsNullOrEmpty(section) ? opening : opening + ". " + section;
        }

        /// <summary>The name of the section the cursor currently sits in, or null on a top-level row (which speaks its own name).</summary>
        private string LandingSectionName()
        {
            InspectionTreeItem item = CurrentTreeItem();
            InspectionTreeItem parent = item != null ? item.Parent : null;
            if (parent == null || parent.IndentLevel < 0 || ReferenceEquals(parent, Tree.Root))
            {
                return null;
            }
            return parent.ExpandedLabel ?? parent.Label;
        }

        /// <summary>
        /// Folds the opening header into the first landing announcement. The base's
        /// submenu-boundary prefix is computed unconditionally first, because it tracks state.
        /// </summary>
        protected override string AnnouncePrefix(int region, int index)
        {
            string boundary = base.AnnouncePrefix(region, index);
            if (!pendingOpeningHeader)
            {
                return boundary;
            }
            pendingOpeningHeader = false;
            string header = OpeningHeader();
            if (string.IsNullOrEmpty(header))
            {
                return boundary;
            }
            return string.IsNullOrEmpty(boundary) ? header : header + ". " + boundary;
        }
    }
}
