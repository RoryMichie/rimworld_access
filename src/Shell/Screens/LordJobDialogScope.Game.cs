using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using HarmonyLib;
using RimWorld;
using Verse;
using Verse.Sound;

namespace RimWorldAccess.Shell
{
    /// <summary>
    /// The keyboard focus scope for the whole <see cref="Dialog_BeginLordJob"/> family
    /// (Dialog_BeginRitual, Dialog_BeginPsychicRitual, Dialog_BeginGravshipLaunch, and any modded
    /// subclass), registered once through <see cref="ScopeForWindow.RegisterHierarchy"/>.
    ///
    /// Region 0 is vanilla's pawn list as a tree: one section per box the dialog draws (each role
    /// group, Spectators, Not participating) holding the portraits inside it, all read from the
    /// dialog's own role-selection widget through <see cref="ILordJobRoleWidgetDriver"/>. Enter
    /// on a portrait opens the move menu vanilla opens on it (the widget's own assignment path,
    /// so every gate, replacement rule and message is vanilla's); Space performs the portrait's
    /// click. After a move the cursor stays in the section it was walking, on the row that took
    /// the pawn's place, and follows the pawn only when that section emptied. A Gravship launch
    /// adds an Options region for its checkboxes; Quality is always the last content region,
    /// read-only rows rebuilt fresh on every entry. Dialog text comes from
    /// <see cref="LordJobDialogState.Adapter"/> (<see cref="ILordJobDialogAdapter"/>).
    ///
    /// Tab cycles all regions; <c>lordJobDialog.toggleQualityStats</c> (Alt+Q) is an
    /// unconditional jump-to/return-from-Quality toggle, and <see cref="AnnounceRegion"/>
    /// special-cases Quality so both entry paths funnel through
    /// <see cref="AnnounceQualityRegionEntry"/>.
    ///
    /// ESCAPE CONTRACT (load-bearing): <see cref="OwnsCancel"/> is
    /// <c>TypeaheadHasActiveSearch || Model.RegionIndex != TreeRegionIndex</c>, so search-clear
    /// wins over region-exit, every other region backs out to the tree, and the idle tree is
    /// unclaimed so vanilla's own Escape closes the dialog. The two Harmony blockers
    /// (<see cref="RitualPatch.Dialog_BeginLordJob_OnAcceptKeyPressed_Patch"/>, which stops
    /// vanilla's Enter-to-Start race, and <see cref="RitualPatch.Window_OnCancelKeyPressed_LordJob_Patch"/>)
    /// read <see cref="LordJobDialogState.CurrentNavigationMode"/> and
    /// <see cref="LordJobDialogState.HasActiveTypeahead"/>, backed here through
    /// <see cref="BridgeNavigationMode"/>/<see cref="BridgeHasActiveTypeahead"/> and the bridge
    /// registered in <see cref="OnPush"/>/<see cref="OnPop"/>.
    ///
    /// The Buttons region is DECLARED, never captured: vanilla's OK calls <c>Start()</c>, which
    /// <c>RitualPatch.LordJobStartPrefix()</c> blocks for this scope's whole lifetime, so a
    /// captured button would silently no-op. <see cref="DeclaredActions"/> lists the same two
    /// buttons under the dialog's own labels, OK through <see cref="LordJobDialogState.StartLordJob"/>
    /// and Cancel through the dialog's own <c>Cancel()</c>, Start disabled exactly when vanilla
    /// greys OK (both read <c>BlockingIssues()</c>, whose text becomes the disabled reason).
    /// </summary>
    public sealed class LordJobDialogScope : TreeRegionScope
    {
        private readonly List<LordJobRoleGroup> groups = new List<LordJobRoleGroup>();
        private readonly List<LordJobExtraToggle> extraToggles = new List<LordJobExtraToggle>();
        private readonly List<LordJobQualityRow> qualityRows = new List<LordJobQualityRow>();
        private readonly List<string> lastWarnings = new List<string>();
        private readonly List<ScreenAction> actions = new List<ScreenAction>();

        private static readonly MethodInfo cancelMethod = AccessTools.Method(typeof(Dialog_BeginLordJob), "Cancel");

        private ILordJobRoleWidgetDriver driver;
        private int previousRegionIndex;
        private int lastRegionBeforeQuality;
        private bool announcedOpen;

        /// <summary>
        /// <paramref name="dialog"/> is unused, kept only to match the
        /// <c>ScopeForWindow.RegisterHierarchy</c> factory signature; per-dialog data comes from
        /// <see cref="LordJobDialogState.Adapter"/>.
        /// </summary>
        public LordJobDialogScope(Window dialog)
        {
            Claim(SharedMenuGrammar.Cancel, delegate { HandleRegionCancel(); },
                when: () => Model.RegionIndex != TreeRegionIndex);

            Claim("lordJobDialog.toggleQualityStats", delegate { JumpToQualityStats(); });

            Claim("lordJobDialog.toggleExtra", delegate { ToggleCurrentExtraToggle(); },
                when: () => HasOptionsRegion && Model.RegionIndex == OptionsRegion);
            Claim("lordJobDialog.togglePawnAssignment", delegate { QuickMoveCurrentPawn(); },
                when: () => CurrentPawnOrNull() != null);

            Claim("lordJobDialog.start", delegate { LordJobDialogState.StartLordJob(); },
                when: () => Model.RegionIndex == TreeRegionIndex);
            Claim("lordJobDialog.inspect", delegate { HandleInspectOrBreakdown(); });
            Claim("lordJobDialog.showHealth", delegate { ShowPawnInfo(p => PawnInfoHelper.GetHealthInfo(p)); },
                when: () => CurrentPawnOrNull() != null);
            Claim("lordJobDialog.showMood", delegate { ShowPawnInfo(p => PawnInfoHelper.GetMoodInfo(p)); },
                when: () => CurrentPawnOrNull() != null);
            Claim("lordJobDialog.showNeeds", delegate { ShowPawnInfo(p => PawnInfoHelper.GetNeedsInfo(p)); },
                when: () => CurrentPawnOrNull() != null);
            Claim("lordJobDialog.showGear", delegate { ShowPawnInfo(p => PawnInfoHelper.GetGearInfo(p)); },
                when: () => CurrentPawnOrNull() != null);

            Claim("tree.jumpToPreviousSection", e => PerformJumpToAdjacentSection(false));
            Claim("tree.jumpToNextSection", e => PerformJumpToAdjacentSection(true));
        }

        public override string Name
        {
            get { return "lord-job-dialog"; }
        }

        /// <summary>
        /// Deliberately carries no <c>!WindowlessInspectionState.IsActive</c> term. Inspection
        /// stays active beneath a window-attached dialog so the tree can resume afterwards, so
        /// that term would deadlock this dialog whenever it was opened from the tree. Stack order
        /// alone decides precedence.
        /// </summary>
        public override bool IsLive
        {
            get { return LordJobDialogState.IsActive; }
        }

        /// <summary>See the class remarks' Escape contract section.</summary>
        public override bool OwnsCancel
        {
            get
            {
                RefreshModel();
                return TypeaheadHasActiveSearch || Model.RegionIndex != TreeRegionIndex;
            }
        }

        /// <summary>Alt+S means Start here, so the shared sort chord must never shadow it.</summary>
        protected override bool EnableSortChord
        {
            get { return false; }
        }

        /// <summary>See the class remarks' Buttons region section.</summary>
        protected override bool IncludeActionsRegion
        {
            get { return true; }
        }

        /// <summary>Capturing vanilla's OK would hand Enter to a handler RitualPatch blocks for this scope's whole lifetime.</summary>
        protected override bool CaptureWindowButtons
        {
            get { return false; }
        }

        /// <summary>Shift+Enter presses Start from anywhere; a refusal speaks the same blocking reason DeclaredActions attaches.</summary>
        protected override string DefaultAcceptActionId
        {
            get { return "lordJobDialog.start"; }
        }

        /// <summary>
        /// Vanilla's two bottom buttons, in <c>DoButtonRow</c>'s own draw order, under the dialog's
        /// own labels (a subclass that overrides either property is followed automatically).
        /// </summary>
        protected override IReadOnlyList<ScreenAction> DeclaredActions
        {
            get
            {
                actions.Clear();
                ILordJobDialogAdapter adapter = LordJobDialogState.Adapter;
                Dialog_BeginLordJob dialog = adapter != null ? adapter.Dialog : null;
                if (dialog == null) return actions;

                IReadOnlyList<string> blocking;
                bool canBegin = adapter.TryStart(out blocking);
                actions.Add(new ScreenAction(
                    dialog.OkButtonLabel.ToString(),
                    delegate { LordJobDialogState.StartLordJob(); },
                    "lordJobDialog.start",
                    disabled: !canBegin,
                    disabledReason: canBegin || blocking == null || blocking.Count == 0
                        ? null
                        : string.Join(". ", blocking)));
                actions.Add(new ScreenAction(
                    dialog.CancelButtonLabel.ToString(),
                    delegate { CancelDialog(dialog); },
                    SharedMenuGrammar.Cancel));
                return actions;
            }
        }

        /// <summary>
        /// The Cancel row's vehicle: the dialog's own protected <c>Cancel()</c>, reachable only by
        /// reflection but still dispatched virtually, so a modded override runs its own body.
        /// Vanilla's base implementation is <c>Close()</c>, the fallback if the method is renamed.
        /// </summary>
        private static void CancelDialog(Dialog_BeginLordJob dialog)
        {
            if (cancelMethod != null)
            {
                cancelMethod.Invoke(dialog, null);
                return;
            }
            dialog.Close();
        }

        protected override bool EnableTypeahead
        {
            get { return true; }
        }

        /// <summary>Bridge target for RitualPatch's untouched Cancel blocker — see LordJobDialogState's remarks.</summary>
        internal LordJobDialogState.NavigationMode BridgeNavigationMode
        {
            get
            {
                return Model.RegionIndex == QualityRegion
                    ? LordJobDialogState.NavigationMode.QualityStats
                    : LordJobDialogState.NavigationMode.RoleList;
            }
        }

        /// <summary>Bridge target for RitualPatch's untouched Cancel blocker — see LordJobDialogState's remarks.</summary>
        internal bool BridgeHasActiveTypeahead
        {
            get { return TypeaheadHasActiveSearch; }
        }

        public override void OnPush()
        {
            base.OnPush();
            LordJobDialogState.NotifyScopeAttached(this);
            ILordJobDialogAdapter adapter = LordJobDialogState.Adapter;
            driver = adapter != null ? LordJobRoleWidgetDriver.TryCreate(adapter.Dialog) : null;
            ResetTree();
            RebuildParticipantTree();
        }

        public override void OnPop()
        {
            LordJobDialogState.NotifyScopeDetached(this);
            base.OnPop();
        }

        public override void OnFocus()
        {
            base.OnFocus();
            if (announcedOpen) return;
            announcedOpen = true;

            ILordJobDialogAdapter adapter = LordJobDialogState.Adapter;
            if (adapter == null) return;

            lastWarnings.Clear();
            lastWarnings.AddRange(adapter.ComputeWarnings() ?? Enumerable.Empty<string>());

            TolkHelper.SpeakData(BuildOpeningAnnouncement(adapter));
            if (Tree.Count > 0)
            {
                AnnounceCurrentItem();
            }
        }

        /// <summary>
        /// The dialog's opening utterance: up to eight independently null-checked fragments
        /// composed into one sentence.
        /// </summary>
        private string BuildOpeningAnnouncement(ILordJobDialogAdapter adapter)
        {
            var sb = new StringBuilder();
            sb.Append((string)"RimWorldAccess.Rituals.Open.Begin".Translate(adapter.LocalizedDialogName));

            string description = adapter.DescriptionText;
            if (!string.IsNullOrEmpty(description)) sb.Append($" {description}");

            string extraExplanation = adapter.ExtraExplanationText;
            if (!string.IsNullOrEmpty(extraExplanation)) sb.Append($" {extraExplanation}");

            string qualitySentence = adapter.ExpectedQualitySentence;
            if (!string.IsNullOrEmpty(qualitySentence)) sb.Append($" {qualitySentence}");

            string duration = adapter.ExpectedDurationText;
            if (!string.IsNullOrEmpty(duration)) sb.Append($" {duration}");

            string outcomeDesc = adapter.OutcomeDescriptionText;
            if (!string.IsNullOrEmpty(outcomeDesc)) sb.Append($" {outcomeDesc}");

            if (lastWarnings.Count > 0)
            {
                sb.Append($" {(string)"RimWorldAccess.Rituals.Open.Warning".Translate(string.Join(" ", lastWarnings))}");
            }

            int roleSlots = groups.Count(g => g.Type == LordJobRoleGroup.Kind.Role);
            sb.Append($" {(string)(roleSlots == 1 ? "RimWorldAccess.Rituals.Open.RoleCountOne".Translate(roleSlots.ToString()) : "RimWorldAccess.Rituals.Open.RoleCountMany".Translate(roleSlots.ToString()))}");
            sb.Append($" {(string)"RimWorldAccess.Rituals.Open.Instructions".Translate()}");

            return sb.ToString();
        }

        // ------------------------------------------------------------------
        // Regions: the tree, an Options region only when the dialog draws checkboxes, Quality last.
        // ------------------------------------------------------------------

        private bool HasOptionsRegion
        {
            get { return extraToggles.Count > 0; }
        }

        private const int OptionsRegion = 1;

        private int QualityRegion
        {
            get { return HasOptionsRegion ? 2 : 1; }
        }

        protected override int ContentRegionCount
        {
            get { return HasOptionsRegion ? 3 : 2; }
        }

        protected override string TreeRegionLabel
        {
            get { return "RimWorldAccess.Rituals.Region.Roles".Translate().ToString(); }
        }

        protected override string ContentRegionName(int region)
        {
            if (region == TreeRegionIndex) return TreeRegionLabel;
            if (HasOptionsRegion && region == OptionsRegion) return "RimWorldAccess.Rituals.Region.Options".Translate().ToString();
            return "RimWorldAccess.Rituals.Region.QualityStats".Translate().ToString();
        }

        protected override int PrefixRowCountFor(int region)
        {
            return PanelFor(region) != null ? base.PrefixRowCountFor(region) : 0;
        }

        protected override int ContentItemCount(int region)
        {
            if (region == TreeRegionIndex) return base.ContentItemCount(region);
            if (HasOptionsRegion && region == OptionsRegion) return extraToggles.Count;
            return qualityRows.Count;
        }

        /// <summary>The base restores search expansion; the toggles are re-read every pass since vanilla writes them on click.</summary>
        protected override void RefreshContent()
        {
            base.RefreshContent();
            ILordJobDialogAdapter adapter = LordJobDialogState.Adapter;
            if (adapter == null) return;
            extraToggles.Clear();
            var toggles = adapter.BuildExtraToggles();
            if (toggles != null) extraToggles.AddRange(toggles);
        }

        protected override ElementDescription DescribeContentItem(int region, int index)
        {
            if (region == TreeRegionIndex) return base.DescribeContentItem(region, index);

            var d = new ElementDescription();
            if (HasOptionsRegion && region == OptionsRegion)
            {
                if (index >= 0 && index < extraToggles.Count)
                {
                    // The row text embeds the checked state (role stays None); KeepsAccept keeps Enter toggling.
                    d.Label = RitualStatFormatter.FormatExtraToggle(extraToggles[index]);
                    d.KeepsAccept = true;
                }
                return d;
            }

            if (index >= 0 && index < qualityRows.Count)
            {
                d.Label = RitualStatFormatter.FormatQualityRow(qualityRows[index]);
                d.ReadOnly = true;
            }
            return d;
        }

        protected override void ActivateContentItem(int region, int index)
        {
            TypeaheadReset();
            if (region == TreeRegionIndex)
            {
                base.ActivateContentItem(region, index);
            }
            else if (HasOptionsRegion && region == OptionsRegion)
            {
                ToggleCurrentExtraToggle();
            }
        }

        private void ToggleCurrentExtraToggle()
        {
            ListModel region = Model.CurrentRegion;
            if (region == null || region.IsEmpty) return;
            int idx = region.Index;
            if (idx < 0 || idx >= extraToggles.Count) return;
            LordJobExtraToggle toggle = extraToggles[idx];
            ILordJobDialogAdapter adapter = LordJobDialogState.Adapter;
            if (adapter == null) return;

            if (adapter.ApplyExtraToggle(toggle))
            {
                string state = toggle.Checked
                    ? (string)"RimWorldAccess.Rituals.Checkbox.Checked".Translate()
                    : (string)"RimWorldAccess.Rituals.Checkbox.Unchecked".Translate();
                TolkHelper.SpeakData((string)"RimWorldAccess.Rituals.Checkbox.Toggled".Translate(toggle.Label, state));
            }
        }

        // ------------------------------------------------------------------
        // The participant tree: one section per box vanilla draws, portraits as its children.
        // ------------------------------------------------------------------

        private void RebuildParticipantTree()
        {
            groups.Clear();
            if (driver != null) groups.AddRange(driver.BuildGroups());
            SetTreeRoot(BuildParticipantRoot());
        }

        private InspectionTreeItem BuildParticipantRoot()
        {
            var root = new InspectionTreeItem
            {
                Label = "Root",
                IndentLevel = -1,
                IsExpandable = true,
                IsExpanded = true,
                Type = InspectionTreeItem.ItemType.Category,
            };
            foreach (LordJobRoleGroup group in groups)
            {
                var section = new InspectionTreeItem
                {
                    Label = group.Headline,
                    IndentLevel = 0,
                    IsExpandable = true,
                    Type = InspectionTreeItem.ItemType.Category,
                    Data = group,
                    Parent = root,
                };
                foreach (Pawn pawn in group.Pawns)
                {
                    section.Children.Add(new InspectionTreeItem
                    {
                        Label = pawn.LabelShortCap,
                        IndentLevel = 1,
                        Type = InspectionTreeItem.ItemType.Item,
                        Data = pawn,
                        Parent = section,
                    });
                }
                root.Children.Add(section);
            }
            return root;
        }

        protected override ElementDescription DescribeTreeNode(InspectionTreeItem item)
        {
            if (item.Data is LordJobRoleGroup group)
            {
                // The expansion suffix is the one channel carrying the portrait count.
                var d = new ElementDescription
                {
                    Label = item.Label + TreeNavigationHelper.FormatExpansionSuffix(item, includeChildCount: true),
                };
                var extras = new List<string>();
                if (group.Type == LordJobRoleGroup.Kind.Role && group.MaxCount > 0)
                    extras.Add((string)"RimWorldAccess.Rituals.Role.MaxCount".Translate(group.MaxCount.ToString()));
                if (group.Locked) extras.Add((string)"Required".Translate());
                if (!string.IsNullOrEmpty(group.ExtraInfo)) extras.Add(SpeechText(group.ExtraInfo));
                if (extras.Count > 0) d.Extras = string.Join(". ", extras);
                return d;
            }

            var pawn = item.Data as Pawn;
            if (pawn == null || driver == null)
            {
                return new ElementDescription { Label = item.Label };
            }

            var pd = new ElementDescription { Label = item.Label, Role = ElementRole.Button };
            var parts = new List<string>();
            if (driver.Required(pawn)) parts.Add((string)"Required".Translate());
            string grayed = driver.GrayOutReason(pawn);
            if (!string.IsNullOrEmpty(grayed)) parts.Add(SpeechText(grayed));
            string tip = driver.ExtraTipContents(pawn);
            if (!string.IsNullOrEmpty(tip)) parts.Add(SpeechText(tip));
            if (parts.Count > 0) pd.Extras = string.Join(". ", parts);
            return pd;
        }

        /// <summary>Vanilla tooltip text as one sentence: bullet lines become clauses.</summary>
        private static string SpeechText(string text)
        {
            var sb = new StringBuilder();
            foreach (string raw in text.Replace("\r\n", "\n").Split('\n'))
            {
                string line = raw.Trim().TrimStart('-').Trim();
                if (line.Length == 0) continue;
                if (sb.Length > 0) sb.Append(". ");
                sb.Append(line);
            }
            return sb.ToString();
        }

        /// <summary>Sections open on the first typed character so portraits inside stay reachable.</summary>
        protected override bool ShouldAutoExpandForSearch(InspectionTreeItem item)
        {
            return item.IndentLevel == 0;
        }

        /// <summary>A section's spoken label carries the suffix and count, which must never enter the haystack.</summary>
        protected override string ContentRowSearchText(int region, int row)
        {
            int treeIndex = row - PrefixRowCountFor(region);
            if (region == TreeRegionIndex && treeIndex >= 0 && treeIndex < Tree.Count)
            {
                return Tree.Visible[treeIndex].Label ?? "";
            }
            return base.ContentRowSearchText(region, row);
        }

        /// <summary>Page Up/Down jump between the boxes.</summary>
        protected override bool IsSectionBoundary(InspectionTreeItem item)
        {
            return item.IndentLevel == 0;
        }

        /// <summary>Enter on a portrait opens vanilla's move menu; on a section it toggles the box.</summary>
        protected override void ActivateTreeNode(InspectionTreeItem item)
        {
            var pawn = item.Data as Pawn;
            if (pawn == null)
            {
                PerformActivateExpandToggle(item);
                return;
            }
            if (driver == null) return;
            if (driver.Required(pawn))
            {
                SoundDefOf.ClickReject.PlayOneShotOnCamera();
                TolkHelper.SpeakData((string)"RimWorldAccess.Rituals.Pawn.IsForced".Translate(pawn.LabelShort));
                return;
            }

            LordJobRoleGroup from = item.Parent != null ? item.Parent.Data as LordJobRoleGroup : null;
            int siblingIndex = item.Parent != null ? item.Parent.Children.IndexOf(item) : -1;
            // The menu pops right after the option runs; the move already announced the landing row.
            List<FloatMenuOption> options = driver.BuildMoveOptions(pawn, () =>
            {
                SuppressNextEntryAnnouncement();
                AfterAssignmentChange(pawn, from, siblingIndex);
            });
            KeyboardFloatMenu.Open(options, givesColonistOrders: false);
        }

        /// <summary>Space: vanilla's click on the portrait (lordJobDialog.togglePawnAssignment).</summary>
        private void QuickMoveCurrentPawn()
        {
            InspectionTreeItem item = CurrentTreeItem();
            var pawn = item != null ? item.Data as Pawn : null;
            if (pawn == null || driver == null) return;
            if (driver.Required(pawn))
            {
                SoundDefOf.ClickReject.PlayOneShotOnCamera();
                TolkHelper.SpeakData((string)"RimWorldAccess.Rituals.Pawn.IsForced".Translate(pawn.LabelShort));
                return;
            }
            LordJobRoleGroup from = item.Parent != null ? item.Parent.Data as LordJobRoleGroup : null;
            int siblingIndex = item.Parent != null ? item.Parent.Children.IndexOf(item) : -1;
            if (driver.QuickMove(pawn, from))
            {
                AfterAssignmentChange(pawn, from, siblingIndex);
            }
        }

        /// <summary>
        /// Rebuilds after a move and announces once: the pawn's new box, then the row the cursor
        /// landed on. The cursor stays in the box it was walking, on the row that took the pawn's
        /// place; when that box emptied it rests on the box's own row.
        /// </summary>
        private void AfterAssignmentChange(Pawn pawn, LordJobRoleGroup from, int siblingIndex)
        {
            driver.Notify_AssignmentsChanged();
            RefreshModel();
            int cursorBefore = Model.RegionIndex == TreeRegionIndex && Model.CurrentRegion != null && !Model.CurrentRegion.IsEmpty
                ? Model.CurrentRegion.Index : 0;

            groups.Clear();
            groups.AddRange(driver.BuildGroups());
            SetTreeRootPreservingState(BuildParticipantRoot(), cursorBefore);
            ResetBoundaryTracking();

            InspectionTreeItem landing = null;
            if (from != null)
            {
                InspectionTreeItem section = Tree.Root.Children
                    .FirstOrDefault(c => c.Data is LordJobRoleGroup g && g.Type == from.Type && g.Headline == from.Headline);
                if (section != null)
                {
                    landing = section.Children.Count > 0
                        ? section.Children[UnityEngine.Mathf.Clamp(siblingIndex, 0, section.Children.Count - 1)]
                        : section;
                }
            }
            if (landing == null)
            {
                landing = Tree.Root.Children.SelectMany(s => s.Children).FirstOrDefault(c => ReferenceEquals(c.Data, pawn));
            }
            if (landing != null && TryRevealAndSelect(landing))
            {
                RefreshModel();
                SyncRegionFromCurrentTree();
            }

            LordJobRoleGroup now = groups.FirstOrDefault(g => g.Pawns.Contains(pawn));
            if (now != null)
            {
                TolkHelper.SpeakData((string)"RimWorldAccess.Rituals.Pawn.MovedTo".Translate(pawn.LabelShort, now.Headline));
            }
            AnnounceCurrentItem();

            ILordJobDialogAdapter adapter = LordJobDialogState.Adapter;
            if (adapter != null) AnnounceWarningDiff(adapter);
        }

        private void AnnounceWarningDiff(ILordJobDialogAdapter adapter)
        {
            IReadOnlyList<string> current = adapter.ComputeWarnings() ?? (IReadOnlyList<string>)Array.Empty<string>();
            // Only newly-added warnings are announced; removals are less useful and chattier.
            var newOnes = current.Where(w => !lastWarnings.Contains(w)).ToList();
            lastWarnings.Clear();
            lastWarnings.AddRange(current);
            if (newOnes.Count > 0)
            {
                TolkHelper.SpeakData((string)"RimWorldAccess.Rituals.Open.Warning".Translate(string.Join(" ", newOnes)));
            }
        }

        private Pawn CurrentPawnOrNull()
        {
            if (Model.RegionIndex != TreeRegionIndex) return null;
            InspectionTreeItem item = CurrentTreeItem();
            return item != null ? item.Data as Pawn : null;
        }

        /// <summary>The focused portrait tile as vanilla's own draw recorded it (<see cref="RitualPawnRowDrawPatch"/>).</summary>
        protected internal override UnityEngine.Rect FocusedContentRect()
        {
            Pawn pawn = CurrentPawnOrNull();
            return pawn != null ? RitualPawnRowDrawPatch.Rows.FindLast(pawn) : default(UnityEngine.Rect);
        }

        private void InspectCurrentPawn()
        {
            Pawn pawn = CurrentPawnOrNull();
            if (pawn == null) return;
            Find.WindowStack.Add(new Dialog_InfoCard(pawn));
        }

        private void ShowPawnInfo(Func<Pawn, string> info)
        {
            Pawn pawn = CurrentPawnOrNull();
            if (pawn == null) return;
            string text = info != null ? info(pawn) : null;
            if (!string.IsNullOrEmpty(text)) TolkHelper.SpeakData(text);
        }

        /// <summary>Alt+I: inspects the focused pawn, opens the row's stat breakdown in Quality, otherwise a silent no-op.</summary>
        private void HandleInspectOrBreakdown()
        {
            if (Model.RegionIndex == QualityRegion)
            {
                OpenQualityRowBreakdown();
            }
            else
            {
                InspectCurrentPawn();
            }
        }

        private void OpenQualityRowBreakdown()
        {
            ListModel region = Model.CurrentRegion;
            if (region == null || region.IsEmpty) return;
            int index = region.Index;
            if (index < 0 || index >= qualityRows.Count) return;
            LordJobQualityRow row = qualityRows[index];

            if (!string.IsNullOrEmpty(row.Explanation))
                StatBreakdownState.Open(row.Label, row.Explanation);
            else if (!string.IsNullOrEmpty(row.Tooltip))
                TolkHelper.SpeakData((string)"RimWorldAccess.Rituals.QualityStats.BreakdownWithTooltip".Translate(row.Label, row.Tooltip));
            else
                TolkHelper.Speak("RimWorldAccess.Rituals.QualityStats.NoBreakdown".Loc());
        }

        /// <summary>The Alt+Q jump-to/return-from-Quality shortcut: a single unconditional toggle.</summary>
        private void JumpToQualityStats()
        {
            if (Model.RegionIndex == QualityRegion)
            {
                ReturnFromQualityStats();
                return;
            }

            RefreshModel();
            MoveResult result = Model.MoveToRegion(QualityRegion);
            if (result.Changed) OnRegionChanged(result);
            TypeaheadReset();
            AnnounceRegion();
        }

        private void ReturnFromQualityStats()
        {
            RefreshModel();
            MoveResult result = Model.MoveToRegion(lastRegionBeforeQuality);
            if (result.Changed) OnRegionChanged(result);
            TypeaheadReset();
            AnnounceRegion();
        }

        /// <summary>
        /// Tracks the focused region so <see cref="lastRegionBeforeQuality"/> is correct however
        /// Quality was entered: it is captured the moment a transition lands on Quality, and the
        /// last-known region is then updated unconditionally for next time.
        /// </summary>
        protected override void OnRegionChanged(MoveResult result)
        {
            if (Model.RegionIndex == QualityRegion && previousRegionIndex != QualityRegion)
            {
                lastRegionBeforeQuality = previousRegionIndex;
            }
            previousRegionIndex = Model.RegionIndex;
        }

        /// <summary>
        /// Special-cases landing on Quality — rebuild the rows, speak the once-per-process
        /// instructions or the header — whatever the entry path; other regions use the base.
        /// </summary>
        protected override void AnnounceRegion()
        {
            if (Model.RegionIndex == QualityRegion)
            {
                AnnounceQualityRegionEntry();
                return;
            }
            base.AnnounceRegion();
        }

        private void AnnounceQualityRegionEntry()
        {
            BuildQualityRows();
            RefreshModel();

            if (!LordJobDialogState.QualityInstructionsShown)
            {
                TolkHelper.SpeakData(qualityRows.Count == 1
                    ? (string)"RimWorldAccess.Rituals.QualityStats.InstructionsOne".Translate(qualityRows.Count.ToString())
                    : (string)"RimWorldAccess.Rituals.QualityStats.InstructionsMany".Translate(qualityRows.Count.ToString()));
                LordJobDialogState.QualityInstructionsShown = true;
            }
            else
            {
                TolkHelper.SpeakData(qualityRows.Count == 1
                    ? (string)"RimWorldAccess.Rituals.QualityStats.HeaderOne".Translate(qualityRows.Count.ToString())
                    : (string)"RimWorldAccess.Rituals.QualityStats.HeaderMany".Translate(qualityRows.Count.ToString()));
            }

            if (qualityRows.Count > 0)
            {
                AnnounceCurrentItem();
            }
            else
            {
                TolkHelper.Speak("RimWorldAccess.Rituals.Empty.NoQualityFactorsAvailable".Loc());
            }
        }

        private void BuildQualityRows()
        {
            qualityRows.Clear();
            ILordJobDialogAdapter adapter = LordJobDialogState.Adapter;
            if (adapter == null) return;

            TargetInfo target = adapter.Target;
            if (target.IsValid)
            {
                string loc = FormatLocation(target);
                if (!string.IsNullOrEmpty(loc))
                {
                    qualityRows.Add(new LordJobQualityRow
                    {
                        Label = (string)"RimWorldAccess.Rituals.Quality.LocationLabel".Translate(),
                        Change = loc,
                        IsInformational = true,
                    });
                }
            }

            try
            {
                var factors = adapter.GetQualityFactors(out var range);
                if (factors != null && factors.Count > 0)
                {
                    foreach (var f in factors.OrderByDescending(qf => qf.priority))
                    {
                        if (f == null) continue;
                        string label = f.label ?? (string)"RimWorldAccess.Rituals.Quality.UnknownFactor".Translate();
                        if (!string.IsNullOrEmpty(f.count)) label += $" ({f.count})";
                        string change = f.qualityChange ?? "0%";
                        qualityRows.Add(new LordJobQualityRow
                        {
                            Label = label,
                            Change = change,
                            Quality = f.quality,
                            IsPresent = f.present,
                            IsPositive = f.positive,
                            IsUncertain = f.uncertainOutcome,
                            // Several vanilla comps bake a range or fraction into qualityChange
                            // with no boolean on QualityFactor marking it, so the detection has to
                            // key off punctuation, which is language-stable, not English words.
                            HasCountContext = change.Contains("(") || change.Contains("/"),
                            Tooltip = f.toolTip,
                        });
                    }
                }

                qualityRows.Add(new LordJobQualityRow
                {
                    Label = (string)"RimWorldAccess.Rituals.Quality.ExpectedQualityLabel".Translate(),
                    Change = FormatRange(range),
                    IsInformational = true,
                });

                foreach (var row in adapter.BuildExtraQualityRows())
                {
                    qualityRows.Add(row);
                }

                var outcomes = adapter.BuildOutcomeChances(range);
                if (outcomes != null && outcomes.Count > 0)
                {
                    // Vanilla's own header, quality number included: "Outcome chances (63%)".
                    qualityRows.Add(new LordJobQualityRow
                    {
                        Label = adapter.Dialog.OutcomeChancesLabel(
                            Dialog_BeginLordJob.QualityNumberToString(range)),
                        Change = "",
                        IsInformational = true,
                    });
                    foreach (var oc in outcomes)
                    {
                        qualityRows.Add(new LordJobQualityRow
                        {
                            Label = $"  {oc.Label}",
                            Change = oc.Percentage.ToStringPercent("F0"),
                            IsInformational = true,
                            Tooltip = oc.Tooltip,
                        });
                    }
                }

                string outcomeDesc = adapter.OutcomeDescriptionText;
                if (!string.IsNullOrEmpty(outcomeDesc))
                {
                    qualityRows.Add(new LordJobQualityRow
                    {
                        Label = (string)"RimWorldAccess.Rituals.Quality.OutcomeDescriptionLabel".Translate(),
                        Change = "",
                        IsInformational = true,
                        Tooltip = outcomeDesc,
                    });
                }
            }
            catch (Exception ex)
            {
                Log.Warning($"[LordJobDialogScope] Error building quality rows: {ex.Message}");
                qualityRows.Add(new LordJobQualityRow
                {
                    Label = (string)"RimWorldAccess.Rituals.Quality.Unavailable".Translate(),
                    Change = "",
                    Tooltip = ex.Message,
                });
            }
        }

        private static string FormatRange(FloatRange range)
        {
            if (Math.Abs(range.min - range.max) < 0.01f) return range.min.ToStringPercent("F0");
            return (string)"RimWorldAccess.Rituals.Quality.RangeFormat".Translate(range.min.ToStringPercent("F0"), range.max.ToStringPercent("F0"));
        }

        private static string FormatLocation(TargetInfo target)
        {
            var parts = new List<string>();
            if (target.Thing != null) parts.Add(target.Thing.LabelShortCap);

            if (target.HasThing || target.Cell.IsValid)
            {
                try
                {
                    Map map = target.Map;
                    IntVec3 cell = target.Cell;
                    if (map != null && cell.IsValid && cell.InBounds(map))
                    {
                        Room room = cell.GetRoom(map);
                        if (room != null && room.ProperRoom && !room.PsychologicallyOutdoors)
                        {
                            string label = room.GetRoomRoleLabel();
                            if (!string.IsNullOrEmpty(label)) parts.Add((string)"RimWorldAccess.Rituals.Location.InRoom".Translate(label));
                        }
                        else if (room != null && room.PsychologicallyOutdoors)
                        {
                            parts.Add((string)"RimWorldAccess.Rituals.Location.Outdoors".Translate());
                        }
                    }
                }
                catch { /* tolerate */ }
            }

            if (parts.Count == 0 && target.Cell.IsValid)
                return (string)"RimWorldAccess.Rituals.Location.Cell".Translate(target.Cell.x.ToString(), target.Cell.z.ToString());

            return parts.Count > 0 ? string.Join(", ", parts) : null;
        }

        /// <summary>The model index of the automatic Buttons region: after the content regions, no captured extras.</summary>
        private int ButtonsRegionIndex
        {
            get { return ContentRegionCount; }
        }

        private void HandleRegionCancel()
        {
            if (Model.RegionIndex == QualityRegion)
            {
                ReturnFromQualityStats();
            }
            else if (Model.RegionIndex == ButtonsRegionIndex || (HasOptionsRegion && Model.RegionIndex == OptionsRegion))
            {
                ReturnToTreeRegion();
            }
        }

        /// <summary>Escape out of Options or Buttons: same shape as <see cref="ReturnFromQualityStats"/>, landing on the tree.</summary>
        private void ReturnToTreeRegion()
        {
            RefreshModel();
            MoveResult result = Model.MoveToRegion(TreeRegionIndex);
            if (result.Changed) OnRegionChanged(result);
            TypeaheadReset();
            AnnounceRegion();
        }
    }
}
