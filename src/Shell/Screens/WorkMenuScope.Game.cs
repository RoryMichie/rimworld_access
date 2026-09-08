using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using RimWorld;
using Verse;

namespace RimWorldAccess.Shell
{
    /// <summary>
    /// Focused-view work assignment grid on the <see cref="ScreenScope"/> chassis. Manual mode
    /// presents vanilla's five priority levels (Priority 1-4, then Disabled) as five content
    /// regions named with the game's own vocabulary (<see cref="WorkMenuState.GetColumnName"/>);
    /// basic mode presents one region of enable/disable checkboxes. Windowless — the surface is
    /// <see cref="WorkMenuState"/> — so the scope rides the stack via <see cref="WorkMenuScopeMirror"/>.
    ///
    /// <b>The 2D grammar is design law and must never be flattened.</b> Up/Down move between
    /// priority levels, Left/Right between work types on the current level, digits set the selected
    /// work type's priority, and Tab/Shift+Tab cycle colonists — each colonist IS the tab here,
    /// while the vanilla-faithful pawns-by-work-types grid lives behind Ctrl+Tab. Two chassis seams
    /// carry it: <see cref="ScreenScope.TransposeContentAxes"/> makes the priority-level regions the
    /// vertical axis, and <see cref="ScreenScope.TryHandleAcceptChord"/> keeps Enter a screen-level
    /// verb. <see cref="ScreenScope.EnableRegionCycling"/> is false because Tab belongs to colonist
    /// cycling.
    ///
    /// Escape confirms and closes, so <see cref="OwnsCancel"/> stays true rather than taking the
    /// chassis's typeahead-conditional posture: the real <see cref="MainTabWindow_Work"/> draws
    /// underneath and would otherwise close itself, skipping <see cref="WorkMenuState.Confirm"/>'s
    /// work-giver refresh. <see cref="OwnsAccept"/> stays true because that tab window inherits
    /// closeOnAccept and its own Accept pass can run BEFORE the dispatcher.
    ///
    /// The digit-priority claims are claimed UNCONDITIONALLY in both modes — Event.current.Use()
    /// must fire so vanilla's TimeSpeed_* KeyBindingDefs never see bare/Shift 0-4 — but each handler
    /// only mutates in manual mode, and digits are never search characters
    /// (<see cref="TypeaheadAcceptsDigits"/> false). Space reaches the row through the chassis
    /// activation alias: toggles in basic mode, does nothing in manual mode.
    ///
    /// No Alt+I claim: this screen has no Dialog_InfoCard path. The real
    /// <see cref="MainTabWindow_Work"/> stays open and rendered, so this scope implements
    /// <see cref="IPawnTableFocusSource"/> to feed <see cref="PawnTableFocusDriver"/>.
    /// </summary>
    public sealed class WorkMenuScope : ScreenScope, IPawnTableFocusSource
    {
        /// <summary>Priority levels, in region order: Priority 1-4 then Disabled.</summary>
        private const int PriorityLevelCount = 5;

        private const int DisabledLevel = 4;

        private Pawn currentPawn;
        private int currentPawnIndex;
        private readonly List<Pawn> allPawns = new List<Pawn>();

        /// <summary>Manual mode's five priority levels; one region each.</summary>
        private readonly List<List<WorkMenuState.WorkTypeEntry>> levels =
            new List<List<WorkMenuState.WorkTypeEntry>>();

        /// <summary>Basic mode's single flat list, and the pawn-switch/typeahead haystack in both modes.</summary>
        private readonly List<WorkMenuState.WorkTypeEntry> allEntries =
            new List<WorkMenuState.WorkTypeEntry>();

        private bool hasUnsavedChanges;
        private bool announcedOpen;

        public WorkMenuScope()
        {
            Claim(SharedMenuGrammar.Cancel, delegate
            {
                ShellFrameStamps.MarkCancelConsumed();
                WorkMenuState.Confirm();
            }, when: () => !TypeaheadHasActiveSearch);

            Claim("work.toggleMode", delegate { ToggleMode(); });
            Claim("work.swapToTableView", delegate { WorkMenuOpener.SwapToTable(); });
            Claim("work.nextPawn", delegate { SwitchToNextPawn(); });
            Claim("work.previousPawn", delegate { SwitchToPreviousPawn(); });

            Claim("work.cyclePriorityDown", delegate { CyclePriority(decrease: true); });
            Claim("work.cyclePriorityUp", delegate { CyclePriority(decrease: false); });
            Claim("work.cyclePriorityDownAll", delegate { CycleAllPawns(decrease: true); });
            Claim("work.cyclePriorityUpAll", delegate { CycleAllPawns(decrease: false); });

            Claim("work.setPriority0", delegate { SetPriorityIfManual(0); });
            Claim("work.setPriority1", delegate { SetPriorityIfManual(1); });
            Claim("work.setPriority2", delegate { SetPriorityIfManual(2); });
            Claim("work.setPriority3", delegate { SetPriorityIfManual(3); });
            Claim("work.setPriority4", delegate { SetPriorityIfManual(4); });
            Claim("work.setPriorityAll0", delegate { SetPriorityAllIfManual(0); });
            Claim("work.setPriorityAll1", delegate { SetPriorityAllIfManual(1); });
            Claim("work.setPriorityAll2", delegate { SetPriorityAllIfManual(2); });
            Claim("work.setPriorityAll3", delegate { SetPriorityAllIfManual(3); });
            Claim("work.setPriorityAll4", delegate { SetPriorityAllIfManual(4); });

            Claim("work.copyPriorities", delegate { CopyPriorities(); });
            Claim("work.pastePriorities", delegate { PastePriorities(); });
        }

        public override string Name
        {
            get { return "work"; }
        }

        /// <summary>Escape is this screen's own; the base's earlier claim still clears a live search first.</summary>
        public override bool OwnsCancel
        {
            get { return true; }
        }

        protected override bool EnableTypeahead
        {
            get { return true; }
        }

        /// <summary>The priority levels are the grid's vertical axis.</summary>
        protected override bool TransposeContentAxes
        {
            get { return true; }
        }

        /// <summary>"Priority 2" and "Disabled" place the user already; a section ordinal would not.</summary>
        protected override bool RegionNamesCarryPosition
        {
            get { return true; }
        }

        /// <summary>Tab/Shift+Tab cycle colonists here, so the region cycle stands down.</summary>
        protected override bool EnableRegionCycling
        {
            get { return false; }
        }

        /// <summary>Enter is a screen-level verb: confirm and close, or settle a live search.</summary>
        protected override bool TryHandleAcceptChord()
        {
            if (TypeaheadHasActiveSearch)
            {
                TypeaheadSettle();
            }
            else
            {
                WorkMenuState.Confirm();
            }
            return true;
        }

        /// <summary>Digits are the priority commands here, never search characters.</summary>
        protected override bool TypeaheadAcceptsDigits
        {
            get { return false; }
        }

        /// <summary>The work type's own name, so the long detail tail never joins the haystack.</summary>
        protected override string ContentRowSearchText(int region, int row)
        {
            WorkMenuState.WorkTypeEntry entry = EntryAt(region, row);
            return entry == null ? "" : entry.WorkType.labelShort;
        }

        /// <summary>Nothing vanilla draws for this windowless view is a button of ours — no Buttons region.</summary>
        protected override bool IncludeActionsRegion
        {
            get { return false; }
        }

        // The region contract, read live so a mode switch needs nothing but a refresh: manual mode
        // gives one region per priority level, basic mode one region of checkboxes.

        protected override int ContentRegionCount
        {
            get { return WorkMenuState.IsManualMode ? PriorityLevelCount : 1; }
        }

        protected override string ContentRegionName(int region)
        {
            if (WorkMenuState.IsManualMode)
                return WorkMenuState.GetColumnName(region);
            MainButtonDef def = DefDatabase<MainButtonDef>.GetNamedSilentFail("Work");
            return def != null ? def.LabelCap.Resolve() : "Work";
        }

        protected override int ContentItemCount(int region)
        {
            List<WorkMenuState.WorkTypeEntry> entries = EntriesIn(region);
            return entries == null ? 0 : entries.Count;
        }

        protected override void RefreshContent()
        {
            if (allPawns.Count == 0)
            {
                List<Pawn> eligible = WorkMenuState.BuildEligibleColonists();
                if (eligible != null)
                {
                    allPawns.AddRange(eligible);
                }
                currentPawnIndex = 0;
            }
            if (currentPawn == null && allPawns.Count > 0)
            {
                currentPawn = allPawns[currentPawnIndex];
            }
            LoadWorkTypesForCurrentPawn();
        }

        protected override ElementDescription DescribeContentItem(int region, int index)
        {
            var d = new ElementDescription();
            WorkMenuState.WorkTypeEntry entry = EntryAt(region, index);
            if (entry == null)
                return d;
            d.Label = entry.WorkType.labelShort.CapitalizeFirst();
            d.Disabled = entry.IsPermanentlyDisabled;
            // The detail keys carry their own leading separator; the composer supplies one too.
            d.Extras = BuildEntryDetails(entry).TrimStart(' ', '.');
            if (!WorkMenuState.IsManualMode)
            {
                d.Role = ElementRole.Checkbox;
                d.Check = entry.CurrentPriority > 0 ? CheckState.Checked : CheckState.Unchecked;
            }
            return d;
        }

        /// <summary>
        /// Space on a work type: basic mode toggles it on <see cref="WorkMenuState"/>'s own setter,
        /// manual mode does nothing. Enter never arrives here — see <see cref="TryHandleAcceptChord"/>.
        /// </summary>
        protected override void ActivateContentItem(int region, int index)
        {
            if (WorkMenuState.IsManualMode)
                return;
            ToggleSelected();
        }

        private void LoadWorkTypesForCurrentPawn()
        {
            while (levels.Count < PriorityLevelCount)
            {
                levels.Add(new List<WorkMenuState.WorkTypeEntry>());
            }
            for (int i = 0; i < PriorityLevelCount; i++)
            {
                levels[i].Clear();
            }
            allEntries.Clear();
            if (currentPawn == null || currentPawn.workSettings == null)
                return;

            foreach (WorkTypeDef workType in WorkTableHelper.WorkTypes)
            {
                bool isPermanentlyDisabled = currentPawn.WorkTypeIsDisabled(workType);
                int priority = isPermanentlyDisabled ? 0 : currentPawn.workSettings.GetPriority(workType);

                var entry = new WorkMenuState.WorkTypeEntry
                {
                    WorkType = workType,
                    IsPermanentlyDisabled = isPermanentlyDisabled,
                    CurrentPriority = priority
                };

                allEntries.Add(entry);
                levels[WorkMenuState.PriorityToColumnIndex(priority)].Add(entry);
            }

            List<WorkMenuState.WorkTypeEntry> disabled = levels[DisabledLevel]
                .OrderBy(e => e.IsPermanentlyDisabled ? 1 : 0)
                .ThenByDescending(e => e.WorkType.naturalPriority)
                .ToList();
            levels[DisabledLevel].Clear();
            levels[DisabledLevel].AddRange(disabled);
        }

        private List<WorkMenuState.WorkTypeEntry> EntriesIn(int region)
        {
            if (!WorkMenuState.IsManualMode)
                return region == 0 ? allEntries : null;
            return region >= 0 && region < levels.Count ? levels[region] : null;
        }

        private WorkMenuState.WorkTypeEntry EntryAt(int region, int index)
        {
            List<WorkMenuState.WorkTypeEntry> entries = EntriesIn(region);
            if (entries == null || index < 0 || index >= entries.Count)
                return null;
            return entries[index];
        }

        /// <summary>
        /// The entry under the cursor, read straight off the model with no refresh, so the
        /// every-frame focus-ring driver can call it safely. Handlers refresh first themselves.
        /// </summary>
        private WorkMenuState.WorkTypeEntry CurrentEntry()
        {
            ListModel region = Model.CurrentRegion;
            if (region == null || region.IsEmpty)
                return null;
            return EntryAt(Model.RegionIndex, region.Index);
        }

        private bool FindWorkType(WorkTypeDef workType, out int region, out int index)
        {
            int regions = ContentRegionCount;
            for (int r = 0; r < regions; r++)
            {
                List<WorkMenuState.WorkTypeEntry> entries = EntriesIn(r);
                if (entries == null)
                    continue;
                for (int i = 0; i < entries.Count; i++)
                {
                    if (entries[i].WorkType == workType)
                    {
                        region = r;
                        index = i;
                        return true;
                    }
                }
            }
            region = 0;
            index = 0;
            return false;
        }

        private int FirstPopulatedRegion()
        {
            int regions = ContentRegionCount;
            for (int r = 0; r < regions; r++)
            {
                if (ContentItemCount(r) > 0)
                    return r;
            }
            return 0;
        }

        /// <summary>Lands the cursor on one row without announcing — the caller owns the utterance.</summary>
        private void MoveCursorTo(int region, int index)
        {
            if (region != Model.RegionIndex)
            {
                MoveResult moved = Model.MoveToRegion(region);
                if (moved.Kind == MoveKind.Empty)
                    return;
            }
            ListModel list = Model.CurrentRegion;
            if (list == null)
                return;
            list.MoveTo(index);
            NotifyCursorSettled();
        }

        // Singleton instance (see WorkMenuScopeMirror), so every per-session field is wiped at push
        // time and the next OnFocus repopulates it.

        public override void OnPush()
        {
            base.OnPush();
            currentPawn = null;
            currentPawnIndex = 0;
            allPawns.Clear();
            allEntries.Clear();
            for (int i = 0; i < levels.Count; i++)
            {
                levels[i].Clear();
            }
            TypeaheadReset();
            hasUnsavedChanges = false;
            announcedOpen = false;
            ResetOpenAnnouncement();
        }

        /// <summary>
        /// First focus after <see cref="WorkMenuState.Open"/>: resolves the pending pawn against the
        /// live eligible-colonist list, lands on the first populated priority level, and speaks the
        /// opening announcement plus the first row.
        /// </summary>
        public override void OnFocus()
        {
            bool firstFocus = !announcedOpen;
            if (firstFocus)
            {
                ResolveInitialPawn();
            }
            base.OnFocus();
            if (!firstFocus)
                return;
            announcedOpen = true;

            if (currentPawn == null)
            {
                // Open()'s eligibility check passed, but the roster can empty in the
                // one-dispatcher-pass window before this OnFocus. Mirror Open()'s decline rather
                // than sitting here live with nothing to show.
                TolkHelper.Speak("RimWorldAccess.Input.WorkMenu.NoColonistsAvailable".Loc());
                WorkMenuState.AbortEmptyFocus();
                return;
            }

            MoveCursorTo(FirstPopulatedRegion(), 0);
            TolkHelper.SpeakData((string)"RimWorldAccess.Work.FocusedViewOpening".Translate(currentPawn.LabelShort));
            AnnounceCurrentItem();
        }

        private void ResolveInitialPawn()
        {
            List<Pawn> eligible = WorkMenuState.BuildEligibleColonists();
            allPawns.Clear();
            if (eligible != null)
            {
                allPawns.AddRange(eligible);
            }
            if (allPawns.Count == 0)
            {
                currentPawn = null;
                currentPawnIndex = 0;
                return;
            }
            Pawn pending = WorkMenuState.PendingInitialPawn;
            int index = pending != null ? allPawns.IndexOf(pending) : -1;
            currentPawnIndex = index >= 0 ? index : 0;
            currentPawn = allPawns[currentPawnIndex];
            hasUnsavedChanges = false;
        }

        // Priority mutations. Every write goes through WorkMenuState's own vanilla-riding setters;
        // this class only picks the row, keeps the cursor on its level, and speaks.

        private string pendingWriteReport;

        protected override string AnnouncePrefix(int region, int index)
        {
            string prefix = base.AnnouncePrefix(region, index);
            if (pendingWriteReport == null)
                return prefix;
            string report = pendingWriteReport;
            pendingWriteReport = null;
            return CombinePrefixFragments(prefix, report);
        }

        private void SetPriorityIfManual(int priority)
        {
            if (!WorkMenuState.IsManualMode)
                return;
            RefreshModel();
            WorkMenuState.WorkTypeEntry entry = CurrentEntry();
            if (entry == null)
                return;
            ApplyWrite(entry.WorkType, WorkMenuState.SetPriority(currentPawn, entry, priority));
        }

        private void SetPriorityAllIfManual(int priority)
        {
            if (!WorkMenuState.IsManualMode)
                return;
            RefreshModel();
            WorkMenuState.WorkTypeEntry entry = CurrentEntry();
            if (entry == null)
            {
                TolkHelper.Speak("RimWorldAccess.Work.NoWorkTypeSelected".Loc());
                return;
            }
            WorkTypeDef workType = entry.WorkType;
            int region = Model.RegionIndex;
            int index = Model.CurrentRegion.Index;
            if (!WorkMenuState.SetPriorityForAllPawns(allPawns, currentPawn, entry, priority))
                return;
            hasUnsavedChanges = true;
            StayAndAnnounce(workType, region, index);
        }

        private void CyclePriority(bool decrease)
        {
            RefreshModel();
            WorkMenuState.WorkTypeEntry entry = CurrentEntry();
            if (entry == null)
                return;
            ApplyWrite(entry.WorkType, WorkMenuState.CyclePriority(currentPawn, entry, decrease));
        }

        private void ToggleSelected()
        {
            RefreshModel();
            WorkMenuState.WorkTypeEntry entry = CurrentEntry();
            if (entry == null)
                return;
            ApplyWrite(entry.WorkType, WorkMenuState.ToggleSelected(currentPawn, entry));
        }

        private void CycleAllPawns(bool decrease)
        {
            RefreshModel();
            WorkMenuState.WorkTypeEntry entry = CurrentEntry();
            if (entry == null)
            {
                TolkHelper.Speak("RimWorldAccess.Work.NoWorkTypeSelected".Loc());
                return;
            }
            int region = Model.RegionIndex;
            int index = Model.CurrentRegion.Index;
            if (!WorkMenuState.CycleAllPawnsPriorityForCurrent(allPawns, currentPawn, entry, decrease))
                return;
            // The bulk report is the announcement; the cursor silently keeps its slot.
            RefreshModel();
            int count = ContentItemCount(region);
            if (count > 0)
                MoveCursorTo(region, Math.Min(index, count - 1));
        }

        /// <summary>A refusal already spoke; an unchanged priority re-reads the row; a real write reports the move while the cursor keeps its level.</summary>
        private void ApplyWrite(WorkTypeDef workType, WorkMenuState.PriorityWriteResult result)
        {
            switch (result)
            {
                case WorkMenuState.PriorityWriteResult.Written:
                    hasUnsavedChanges = true;
                    StayAndAnnounce(workType, Model.RegionIndex, Model.CurrentRegion.Index);
                    return;
                case WorkMenuState.PriorityWriteResult.Unchanged:
                    AnnounceCurrentItem();
                    return;
                default:
                    return;
            }
        }

        /// <summary>The cursor stays on its level, landing on the job now filling the vacated slot, so digit presses walk a level. One utterance: the move report, then the row or the emptied level's name.</summary>
        private void StayAndAnnounce(WorkTypeDef workType, int region, int index)
        {
            RefreshModel();
            if (!WorkMenuState.IsManualMode)
            {
                if (FindWorkType(workType, out int basicRegion, out int basicIndex))
                    MoveCursorTo(basicRegion, basicIndex);
                AnnounceCurrentItem();
                return;
            }

            string report = MoveReport(workType);
            int count = ContentItemCount(region);
            if (count == 0)
            {
                string levelName = ContentRegionName(region);
                string empty = "RimWorldAccess.Shell.Screen.EmptyRegion".Translate().ToString();
                TolkHelper.SpeakData(CombinePrefixFragments(report, levelName + ", " + empty));
                return;
            }
            pendingWriteReport = report;
            MoveCursorTo(region, Math.Min(index, count - 1));
            AnnounceCurrentItem();
        }

        private string MoveReport(WorkTypeDef workType)
        {
            if (!FindWorkType(workType, out int region, out int index))
                return null;
            string label = workType.labelShort;
            if (region == DisabledLevel)
                return "RimWorldAccess.Work.SetPriority.MovedToDisabled".Loc(label).SpokenText;
            string placement = PlacementContext(levels[region], index);
            string priorityWord = WorkMenuState.PriorityWord(region);
            return string.IsNullOrEmpty(placement)
                ? "RimWorldAccess.Work.SetPriority.MovedToPriority".Loc(label, priorityWord).SpokenText
                : "RimWorldAccess.Work.SetPriority.MovedToPriorityWithContext".Loc(label, priorityWord, placement).SpokenText;
        }

        /// <summary>
        /// Where in its new priority level the work type landed, relative to the neighbours a sighted
        /// player sees it between. Empty when it is the only movable entry there, or when it sits in
        /// the permanently disabled tail, where left/right position carries no meaning.
        /// </summary>
        private static string PlacementContext(List<WorkMenuState.WorkTypeEntry> level, int index)
        {
            int movableCount = level.Count(e => !e.IsPermanentlyDisabled);
            if (movableCount <= 1)
                return "";

            int lastMovableIndex = level.FindIndex(e => e.IsPermanentlyDisabled) - 1;
            if (lastMovableIndex < 0)
                lastMovableIndex = level.Count - 1;
            if (index > lastMovableIndex)
                return "";

            bool atLeftEdge = index == 0;
            bool atRightEdge = index == lastMovableIndex;

            if (movableCount == 2)
            {
                return atLeftEdge
                    ? "RimWorldAccess.Work.PlacementContext.LeftOf".Translate(level[1].WorkType.labelShort)
                    : "RimWorldAccess.Work.PlacementContext.RightOf".Translate(level[0].WorkType.labelShort);
            }
            if (atLeftEdge)
                return "RimWorldAccess.Work.PlacementContext.FirstLeftOf".Translate(level[1].WorkType.labelShort);
            if (atRightEdge)
                return "RimWorldAccess.Work.PlacementContext.LastRightOf".Translate(level[index - 1].WorkType.labelShort);
            return "RimWorldAccess.Work.PlacementContext.Between".Translate(
                level[index - 1].WorkType.labelShort, level[index + 1].WorkType.labelShort);
        }

        /// <summary>
        /// Manual/basic mode flip: <see cref="WorkMenuState.ToggleMode"/> owns the vanilla side, the
        /// region layout follows from the mode, and the cursor lands on the first populated level.
        /// </summary>
        private void ToggleMode()
        {
            WorkMenuState.ToggleMode();
            TypeaheadReset();
            RefreshModel();
            MoveCursorTo(FirstPopulatedRegion(), 0);
            TolkHelper.Speak(WorkMenuState.IsManualMode
                ? "RimWorldAccess.Work.Mode.Manual".Loc()
                : "RimWorldAccess.Work.Mode.Basic".Loc());
            AnnounceRegion();
        }

        // Copy/paste rides vanilla's own shared clipboard, so the table view's icon buttons and
        // these chords are the same gesture.

        private void CopyPriorities()
        {
            RefreshModel();
            if (!WorkTableHelper.IsCopyPasteEligible(currentPawn))
            {
                TolkHelper.Speak("RimWorldAccess.Work.Table.CopyPasteUnavailable".Loc(CurrentPawnName()));
                return;
            }
            WorkTableHelper.CopyPriorities(currentPawn);
            TolkHelper.Speak("RimWorldAccess.Work.CopyPasteAnnouncement".Loc("Copy".Translate(), currentPawn.LabelShort));
        }

        private void PastePriorities()
        {
            RefreshModel();
            if (!WorkTableHelper.CanPastePriorities)
            {
                TolkHelper.Speak("RimWorldAccess.Work.NoPrioritiesCopied".Loc());
                return;
            }
            if (!WorkTableHelper.IsCopyPasteEligible(currentPawn))
            {
                TolkHelper.Speak("RimWorldAccess.Work.Table.CopyPasteUnavailable".Loc(CurrentPawnName()));
                return;
            }
            WorkTypeDef preservedWorkType = CurrentEntry()?.WorkType;
            WorkTableHelper.PastePriorities(currentPawn);
            hasUnsavedChanges = true;
            RefreshModel();
            if (preservedWorkType != null && FindWorkType(preservedWorkType, out int region, out int index))
            {
                MoveCursorTo(region, index);
            }
            TolkHelper.Speak("RimWorldAccess.Work.CopyPasteAnnouncement".Loc("Paste".Translate(), currentPawn.LabelShort));
        }

        private string CurrentPawnName()
        {
            return currentPawn?.LabelShort ?? "RimWorldAccess.Work.UnknownPawn".Translate().ToString();
        }

        private void SwitchToNextPawn()
        {
            Pawn beforeRefresh = currentPawn;
            RefreshPawnList();
            if (allPawns.Count == 0)
                return;

            if (beforeRefresh != null && !allPawns.Contains(beforeRefresh))
            {
                SaveAndSwitchPawn(currentPawnIndex);
                return;
            }

            int newIndex = MenuHelper.SelectNext(currentPawnIndex, allPawns.Count, out bool wrapped);
            if (wrapped)
                MenuHelper.PlayWrapTone();
            if (newIndex == currentPawnIndex)
            {
                MenuHelper.PlayEdgeTone();
                return;
            }
            SaveAndSwitchPawn(newIndex);
        }

        private void SwitchToPreviousPawn()
        {
            Pawn beforeRefresh = currentPawn;
            RefreshPawnList();
            if (allPawns.Count == 0)
                return;

            if (beforeRefresh != null && !allPawns.Contains(beforeRefresh))
            {
                SaveAndSwitchPawn(currentPawnIndex);
                return;
            }

            int newIndex = MenuHelper.SelectPrevious(currentPawnIndex, allPawns.Count, out bool wrapped);
            if (wrapped)
                MenuHelper.PlayWrapTone();
            if (newIndex == currentPawnIndex)
            {
                MenuHelper.PlayEdgeTone();
                return;
            }
            SaveAndSwitchPawn(newIndex);
        }

        private void RefreshPawnList()
        {
            List<Pawn> fresh = WorkMenuState.BuildEligibleColonists();
            if (fresh == null)
                return;

            allPawns.Clear();
            allPawns.AddRange(fresh);

            if (currentPawn != null)
            {
                int idx = allPawns.IndexOf(currentPawn);
                if (idx >= 0)
                {
                    currentPawnIndex = idx;
                    return;
                }
            }

            if (allPawns.Count == 0)
            {
                currentPawnIndex = 0;
                return;
            }
            if (currentPawnIndex >= allPawns.Count)
                currentPawnIndex = allPawns.Count - 1;
            if (currentPawnIndex < 0)
                currentPawnIndex = 0;
        }

        private void SaveAndSwitchPawn(int newPawnIndex)
        {
            string previousPawnName = currentPawn?.LabelShort ?? "RimWorldAccess.Work.UnknownPawn".Translate().ToString();
            bool hadChanges = hasUnsavedChanges;

            if (currentPawn != null && currentPawn.workSettings != null)
            {
                currentPawn.workSettings.Notify_UseWorkPrioritiesChanged();
            }

            WorkTypeDef preservedWorkType = CurrentEntry()?.WorkType;

            currentPawnIndex = newPawnIndex;
            currentPawn = allPawns[currentPawnIndex];
            TypeaheadReset();
            hasUnsavedChanges = false;

            RefreshModel();
            if (preservedWorkType != null && FindWorkType(preservedWorkType, out int region, out int index))
            {
                MoveCursorTo(region, index);
            }
            else
            {
                MoveCursorTo(FirstPopulatedRegion(), 0);
            }

            string briefAnnouncement = BuildPawnSwitchAnnouncement();
            if (hadChanges)
            {
                TolkHelper.Speak("RimWorldAccess.Work.PawnSwitch.SavedPrefix".Loc(previousPawnName, briefAnnouncement));
            }
            else
            {
                TolkHelper.SpeakData(briefAnnouncement);
            }
        }

        private string BuildPawnSwitchAnnouncement()
        {
            string pawnName = currentPawn?.LabelShort ?? "RimWorldAccess.Work.UnknownPawn".Translate().ToString();
            WorkMenuState.WorkTypeEntry entry = CurrentEntry();
            string pawnPosition = MenuHelper.FormatPosition(currentPawnIndex, allPawns.Count);

            if (entry == null)
            {
                return "RimWorldAccess.Work.PawnSwitch.NoEntry".Translate(pawnName, pawnPosition);
            }

            string labelShort = entry.WorkType.labelShort;
            string detailsSuffix = BuildEntryDetails(entry).TrimEnd('.');

            if (entry.IsPermanentlyDisabled)
            {
                return "RimWorldAccess.Work.PawnSwitch.PermanentlyDisabled".Translate(pawnName, labelShort, detailsSuffix, pawnPosition);
            }

            string priorityLabel = WorkMenuState.GetColumnName(WorkMenuState.PriorityToColumnIndex(entry.CurrentPriority));

            return "RimWorldAccess.Work.PawnSwitch.WithPriority".Translate(pawnName, labelShort, priorityLabel, detailsSuffix, pawnPosition);
        }

        /// <summary>
        /// Everything a row says after its own name: the reasons it is permanently disabled, or its
        /// relevant skills, level and passion, then the description and the jobs it covers. Each
        /// fragment carries its own leading separator.
        /// </summary>
        private string BuildEntryDetails(WorkMenuState.WorkTypeEntry entry)
        {
            WorkTypeDef workType = entry.WorkType;
            var sb = new StringBuilder();

            if (entry.IsPermanentlyDisabled)
            {
                sb.Append("RimWorldAccess.Work.Task.PermanentlyDisabledPrefix".Translate().ToString());
                var reasons = currentPawn.GetReasonsForDisabledWorkType(workType);
                sb.Append(string.Join(", ", reasons.Select(r => r.ToString())));
            }
            else
            {
                var relevantSkills = workType.relevantSkills;
                if (relevantSkills == null || relevantSkills.Count == 0)
                {
                    sb.Append("RimWorldAccess.Work.Task.UnskilledLabor".Translate().ToString());
                }
                else
                {
                    bool skillNamesRedundant = relevantSkills.Count == 1 &&
                        string.Equals(relevantSkills[0].skillLabel, workType.labelShort, StringComparison.OrdinalIgnoreCase);

                    float avgSkill = currentPawn.skills.AverageOfRelevantSkillsFor(workType);
                    int skillLevel = Math.Min(20, Math.Max(0, (int)Math.Round(avgSkill)));

                    if (skillNamesRedundant)
                    {
                        sb.Append("RimWorldAccess.Work.Task.LevelSuffix".Translate(skillLevel).ToString());
                    }
                    else
                    {
                        sb.Append(". ");
                        string skillAndSeparator = "RimWorldAccess.Work.Task.SkillListAndSeparator".Translate();
                        for (int i = 0; i < relevantSkills.Count; i++)
                        {
                            if (i > 0)
                                sb.Append(i == relevantSkills.Count - 1 ? skillAndSeparator : ", ");
                            sb.Append(relevantSkills[i].skillLabel.CapitalizeFirst());
                        }
                        sb.Append("RimWorldAccess.Work.Task.SkillsAndLevelSuffix".Translate(skillLevel).ToString());
                    }
                }

                string passionLabel = WorkTableHelper.PassionLabel(
                    currentPawn.skills.MaxPassionOfRelevantSkillsFor(workType));
                if (!string.IsNullOrEmpty(passionLabel))
                {
                    sb.Append(". ");
                    sb.Append(passionLabel);
                }
            }

            if (!string.IsNullOrEmpty(workType.description))
            {
                sb.Append(". ");
                sb.Append(workType.description);
            }

            string workList = WorkTableHelper.BuildSpecificWorkList(workType);
            if (!string.IsNullOrEmpty(workList))
            {
                sb.Append("RimWorldAccess.Work.Task.IncludesPrefix".Translate().ToString());
                sb.Append(workList);
            }

            return sb.ToString();
        }

        /// <summary>The pawn whose work givers Confirm/CloseForSwap finalize.</summary>
        internal Pawn CurrentPawn
        {
            get { return currentPawn; }
        }

        // IPawnTableFocusSource. Both manual mode's five priority levels and basic mode's flat
        // on/off list resolve to exactly one WorkTypeDef per row, so WorkColumnFor's mapping onto
        // vanilla's own column def is correct in either mode.

        Type IPawnTableFocusSource.TabWindowType
        {
            get { return typeof(MainTabWindow_Work); }
        }

        Pawn IPawnTableFocusSource.FocusedTablePawn
        {
            get { return currentPawn; }
        }

        PawnColumnDef IPawnTableFocusSource.FocusedTableColumn
        {
            get
            {
                WorkMenuState.WorkTypeEntry entry = CurrentEntry();
                return entry != null ? WorkColumnFor(entry.WorkType) : null;
            }
        }

        /// <summary>This view walks one pawn's work types, not the grid, so it never sits on the table header.</summary>
        bool IPawnTableFocusSource.FocusedOnHeaderRow
        {
            get { return false; }
        }

        /// <summary>Vanilla's own work column for a WorkTypeDef, or null if none is currently visible.</summary>
        private static PawnColumnDef WorkColumnFor(WorkTypeDef workType)
        {
            if (workType == null) return null;
            return PawnTableDefOf.Work.columns.Find(c => c.workType == workType);
        }
    }

    /// <summary>
    /// Keeps <see cref="WorkMenuScope"/> in lockstep with <see cref="WorkMenuState.IsActive"/>,
    /// reconciled every OnGUI pass AFTER <see cref="GizmoScopeMirror"/> so a gizmo above keeps
    /// precedence. No yield gate: this screen has no Alt+I path, and WorkMenuState.IsActive is
    /// mutually exclusive with WorkTableState.IsActive by construction.
    /// </summary>
    internal static class WorkMenuScopeMirror
    {
        private static readonly WorkMenuScope scope = new WorkMenuScope();

        /// <summary>The live scope instance; WorkMenuState reaches through here for the current pawn.</summary>
        internal static WorkMenuScope Instance
        {
            get { return scope; }
        }

        public static void Reconcile()
        {
            if (WorkMenuState.IsActive)
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
