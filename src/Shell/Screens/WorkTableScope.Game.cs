using System;
using System.Collections.Generic;
using System.Linq;
using RimWorld;
using Verse;
using Verse.Sound;

namespace RimWorldAccess.Shell
{
    /// <summary>
    /// The Work menu's TABLE view (pawn rows by work-type columns) as one
    /// <see cref="ScreenScope"/> table region. The mod owns no window here — the surface is the
    /// windowless <see cref="WorkTableState"/> machine — so the scope rides the focus stack through
    /// <see cref="WorkTableScopeMirror"/>, which reconciles AFTER <see cref="GizmoScopeMirror"/> for
    /// the reason <see cref="WorkMenuScopeMirror"/> gives.
    ///
    /// Enter is claimed once, in <see cref="ScreenScope"/>'s own constructor, and no claim here can
    /// win against it (same-id claims are offered in registration order): the header row sorts, a
    /// data row goes to <see cref="ActivateContentCell"/> then <see cref="ActivateContentItem"/>.
    /// Priority editing therefore lives on number keys 0-4, `[`/`]`, and Space, where the legacy
    /// handler put it. <see cref="ActivateContentCell"/> claims Enter for the
    /// CopyPasteWorkPriorities column alone.
    ///
    /// <see cref="RefreshContent"/> is an idempotent one-time population: the base invokes it from
    /// effectively every action, including re-entrantly (the second RefreshModel inside
    /// ToggleSortCurrentColumn runs after ApplyContentSort has already reordered), so it must never
    /// re-shuffle data out from under an in-flight operation. The identity-preserving live sync that
    /// drops departed colonists is the separate <see cref="SyncLivePawns"/>, called only from the
    /// row arrows, Home, End, and the two typeahead entry points.
    ///
    /// The per-pawn ideology-opposition warning rides <see cref="ContentCellTip"/> because the
    /// base's ColumnTooltip is column-only and cannot carry row variance, so it speaks on row moves
    /// as well as column moves.
    ///
    /// The real <see cref="MainTabWindow_Work"/> stays open and rendered, shared with the mutually
    /// exclusive <see cref="WorkMenuScope"/>, and this scope implements
    /// <see cref="IPawnTableFocusSource"/> to supply the focused (pawn, column) cell to
    /// <see cref="PawnTableFocusDriver"/>. The Name and copy/paste columns have no work type, so
    /// <see cref="CurrentWorkType"/> returns null and the whole row is ringed.
    /// </summary>
    public sealed class WorkTableScope : ScreenScope, IPawnTableFocusSource
    {
        private readonly List<Pawn> pawnsList = new List<Pawn>();
        private List<Pawn> defaultOrder;
        private bool announcedOpen;

        public WorkTableScope()
        {
            Claim(SharedMenuGrammar.Cancel, delegate { OnCancel(); });

            Claim("workTable.swapToFocusedView", delegate { SwapToFocusedView(); });
            Claim("workTable.toggleMode", delegate { ToggleMode(); });

            Claim("workTable.paintColumn", delegate { PaintEntireColumn(); });
            Claim("workTable.paintToStart", delegate { PaintToFirst(); });
            Claim("workTable.paintToEnd", delegate { PaintToLast(); });
            Claim("workTable.paintDown", delegate { PaintDown(); });
            Claim("workTable.paintUp", delegate { PaintUp(); });

            Claim("workTable.cyclePriorityDown", delegate { CycleCurrentCellPriority(decrease: true); });
            Claim("workTable.cyclePriorityUp", delegate { CycleCurrentCellPriority(decrease: false); });
            Claim("workTable.cyclePriorityDownAll", delegate { CycleAllColonistsPriority(decrease: true); });
            Claim("workTable.cyclePriorityUpAll", delegate { CycleAllColonistsPriority(decrease: false); });

            Claim("workTable.setPriority0", delegate { SetCellIfManual(0); });
            Claim("workTable.setPriority1", delegate { SetCellIfManual(1); });
            Claim("workTable.setPriority2", delegate { SetCellIfManual(2); });
            Claim("workTable.setPriority3", delegate { SetCellIfManual(3); });
            Claim("workTable.setPriority4", delegate { SetCellIfManual(4); });
            Claim("workTable.setPriorityAll0", delegate { SetAllIfManual(0); });
            Claim("workTable.setPriorityAll1", delegate { SetAllIfManual(1); });
            Claim("workTable.setPriorityAll2", delegate { SetAllIfManual(2); });
            Claim("workTable.setPriorityAll3", delegate { SetAllIfManual(3); });
            Claim("workTable.setPriorityAll4", delegate { SetAllIfManual(4); });

            Claim("workTable.toggleCell", delegate { ToggleCurrentCell(); }, when: BasicMode);

            Claim("workTable.copyPriorities", delegate { CopyPriorities(); });
            Claim("workTable.pastePriorities", delegate { PastePriorities(); });
        }

        public override string Name
        {
            get { return "work-table"; }
        }

        /// <summary>
        /// Typeahead over pawn-name row labels. Digits stay commands (the 0-4 priority keys), and
        /// each search re-syncs the live pawn list first.
        /// </summary>
        protected override bool EnableTypeahead
        {
            get { return true; }
        }

        protected override bool TypeaheadAcceptsDigits
        {
            get { return false; }
        }

        protected override void OnTypeaheadWillSearch()
        {
            SyncLivePawns();
        }

        /// <summary>Enter during a live search settles on the match (mirroring the focused view) instead of the row default's save-and-close.</summary>
        protected override bool TryHandleAcceptChord()
        {
            if (!TypeaheadHasActiveSearch)
                return false;
            TypeaheadSettle();
            return true;
        }

        /// <summary>No owning Window for this windowless overlay — self-claim Escape.</summary>
        public override bool OwnsCancel
        {
            get { return true; }
        }

        private static bool BasicMode()
        {
            return !WorkTableState.IsManualMode;
        }

        // One region, one table: rows are eligible colonists, columns are Name plus every visible
        // WorkTypeDef in vanilla priority order.

        protected override int ContentRegionCount
        {
            get { return 1; }
        }

        /// <summary>Vanilla's own Work tab label, looked up by name.</summary>
        protected override string ContentRegionName(int region)
        {
            MainButtonDef def = DefDatabase<MainButtonDef>.GetNamedSilentFail("Work");
            return def != null ? def.LabelCap.Resolve() : "Work";
        }

        protected override int ContentColumnCount(int region)
        {
            return WorkTableHelper.TotalColumnCount;
        }

        protected override int ContentItemCount(int region)
        {
            return pawnsList.Count;
        }

        /// <summary>
        /// One-time population; see the class remarks for why it stays idempotent. Dropping
        /// departed colonists is <see cref="SyncLivePawns"/>' job.
        /// </summary>
        protected override void RefreshContent()
        {
            if (pawnsList.Count > 0)
                return;
            List<Pawn> initial = WorkTableHelper.GetEligibleColonists();
            if (initial.Count == 0)
                return;
            pawnsList.AddRange(initial);
            defaultOrder = new List<Pawn>(pawnsList);
        }

        protected override ElementDescription DescribeContentItem(int region, int index)
        {
            var d = new ElementDescription();
            if (index >= 0 && index < pawnsList.Count)
            {
                d.Label = WorkTableHelper.GetPawnLabel(pawnsList[index]);
            }
            return d;
        }

        protected override TableColumnInfo ContentColumnInfo(int region, int column)
        {
            return new TableColumnInfo(
                WorkTableHelper.GetColumnName(column),
                WorkTableHelper.GetColumnTooltip(null, column),
                WorkTableHelper.IsColumnSortable(column));
        }

        protected override string ContentCellText(int region, int row, int column)
        {
            if (row < 0 || row >= pawnsList.Count)
                return "";
            return WorkTableHelper.GetColumnValue(pawnsList[row], column);
        }

        /// <summary>The per-pawn ideology-opposition addendum; always spoken, row and column moves alike.</summary>
        protected override string ContentCellTip(int region, int row, int column)
        {
            if (row < 0 || row >= pawnsList.Count)
                return null;
            return WorkTableHelper.GetIdeologyWarning(pawnsList[row], column);
        }

        /// <summary>Row default for Enter on a data cell: confirm and close, matching the legacy handler.</summary>
        protected override void ActivateContentItem(int region, int index)
        {
            Confirm();
        }

        /// <summary>
        /// Enter on vanilla's CopyPasteWorkPriorities column: opens the same Copy/Paste float menu
        /// the sighted table's icon buttons open, through PawnColumnHandlerRegistry. Every other
        /// column returns false and falls to <see cref="ActivateContentItem"/>.
        /// </summary>
        protected override bool ActivateContentCell(int region, int row, int column)
        {
            if (row < 0 || row >= pawnsList.Count || !WorkTableHelper.IsCopyPasteColumn(column))
                return false;

            Pawn pawn = pawnsList[row];
            PawnColumnDef def = WorkTableHelper.CopyPasteWorkPrioritiesDef;
            if (def == null || !WorkTableHelper.IsCopyPasteEligible(pawn))
            {
                RejectAndAnnounce("RimWorldAccess.Work.Table.CopyPasteUnavailable".Translate(pawn.LabelShort).ToString());
                return true;
            }

            PawnColumnCellReader.ActivateCellTableless(def, pawn);
            return true;
        }

        /// <summary>
        /// Re-orders via the game-wired comparer in <see cref="WorkTableHelper"/>, never display
        /// strings; "cleared" restores the vanilla display order captured at open. The tick sounds
        /// play here because the base sort cycle has no home for them.
        /// </summary>
        protected override int ApplyContentSort(int region, int column, SortCycleResult cycle, int currentRow)
        {
            Pawn tracked = currentRow >= 0 && currentRow < pawnsList.Count ? pawnsList[currentRow] : null;
            List<Pawn> reordered;
            if (cycle == SortCycleResult.Cleared)
            {
                reordered = new List<Pawn>(defaultOrder ?? pawnsList);
                SoundDefOf.Tick_Low.PlayOneShotOnCamera();
            }
            else
            {
                reordered = WorkTableHelper.SortPawnsByColumn(pawnsList, column, cycle == SortCycleResult.SortedDescending);
                SoundDefOf.Tick_High.PlayOneShotOnCamera();
            }
            pawnsList.Clear();
            pawnsList.AddRange(reordered);
            return tracked == null ? -1 : pawnsList.IndexOf(tracked);
        }

        /// <summary>No vanilla-equivalent button on this screen — no Buttons region.</summary>
        protected override bool IncludeActionsRegion
        {
            get { return false; }
        }

        // Same-assembly read surface only, never part of the accessibility contract.

        internal int OverlayPawnCount
        {
            get { return pawnsList.Count; }
        }

        internal int OverlayCurrentRow
        {
            get
            {
                TableModel table = Model.CurrentTable;
                return table != null ? table.Rows.Index - 1 : -1;
            }
        }

        internal string OverlayCurrentColumnName
        {
            get
            {
                TableModel table = Model.CurrentTable;
                if (table == null)
                    return "";
                TableColumnInfo info = ContentColumnInfo(0, table.ColumnIndex);
                return info != null ? info.Label : "";
            }
        }

        // Lifecycle.

        public override void OnPush()
        {
            base.OnPush();
            pawnsList.Clear();
            defaultOrder = null;
            TypeaheadReset();
            announcedOpen = false;
        }

        /// <summary>
        /// First focus after <see cref="WorkTableState.Open"/>: parks the row cursor on the pending
        /// initial pawn and the column cursor on the first WORK TYPE column, not Name, then speaks
        /// the opening summary and the first cell.
        /// </summary>
        public override void OnFocus()
        {
            base.OnFocus();
            if (announcedOpen)
                return;
            announcedOpen = true;
            if (pawnsList.Count == 0)
                return;

            TableModel table = Model.CurrentTable;
            if (table != null)
            {
                Pawn initial = WorkTableState.PendingInitialPawn;
                if (initial != null)
                {
                    int idx = pawnsList.IndexOf(initial);
                    if (idx >= 0)
                    {
                        table.Rows.MoveTo(idx + 1);
                    }
                }
                if (WorkTableHelper.WorkTypes.Count > 0)
                {
                    table.MoveToColumn(WorkTableHelper.CopyPasteColumnIndex + 1);
                }
            }

            string mode = WorkTableState.IsManualMode
                ? "RimWorldAccess.Work.Mode.Manual".Translate().ToString()
                : "RimWorldAccess.Work.Mode.Basic".Translate().ToString();
            TolkHelper.SpeakData("RimWorldAccess.Work.Table.OpeningAnnouncement".Translate(
                pawnsList.Count, WorkTableHelper.WorkTypes.Count, mode).ToString());
            AnnounceCurrentItem();
        }

        // Live-pawn sync (see the class remarks) and row navigation.

        /// <summary>
        /// Re-reads live eligible colonists, re-applies any active sort, and preserves the cursor on
        /// the SAME PAWN when possible, clamping otherwise.
        /// </summary>
        private void SyncLivePawns()
        {
            TableModel table = Model.CurrentTable;
            int oldModelRow = table != null ? table.Rows.Index : -1;
            Pawn preserved = (oldModelRow > 0 && oldModelRow - 1 < pawnsList.Count)
                ? pawnsList[oldModelRow - 1]
                : null;

            List<Pawn> fresh = WorkTableHelper.GetEligibleColonists();
            if (table != null && table.HasActiveSort)
            {
                fresh = WorkTableHelper.SortPawnsByColumn(fresh, table.SortColumnIndex, table.SortDescending).ToList();
            }
            pawnsList.Clear();
            pawnsList.AddRange(fresh);

            RefreshModel();
            table = Model.CurrentTable;
            if (table == null || pawnsList.Count == 0)
                return;
            int newIndex = preserved != null ? pawnsList.IndexOf(preserved) : -1;
            if (newIndex >= 0)
            {
                table.Rows.MoveTo(newIndex + 1);
            }
            // Else RefreshModel's SetCount clamp already parked the cursor at a valid position.
        }

        protected override void MoveItem(int delta)
        {
            SyncLivePawns();
            base.MoveItem(delta);
        }

        protected override void MoveItemEdge(bool first)
        {
            SyncLivePawns();
            base.MoveItemEdge(first);
        }

        private void OnCancel()
        {
            // Escape-clears-search is the base typeahead claim, registered ahead of this one, so
            // reaching here means no search is active.
            ShellFrameStamps.MarkCancelConsumed();
            Confirm();
        }

        // Current cell helpers.

        private Pawn CurrentPawn
        {
            get
            {
                TableModel table = Model.CurrentTable;
                if (table == null) return null;
                int row = table.Rows.Index - 1;
                return row >= 0 && row < pawnsList.Count ? pawnsList[row] : null;
            }
        }

        private WorkTypeDef CurrentWorkType
        {
            get
            {
                TableModel table = Model.CurrentTable;
                return table != null ? WorkTableHelper.WorkTypeForColumn(table.ColumnIndex) : null;
            }
        }

        // Priority editing: no base equivalent, so number keys, `[`/`]`, and Space stay per-screen
        // claims (verbatim business logic from the legacy handler).

        private void SetCellIfManual(int priority)
        {
            if (WorkTableState.IsManualMode) SetPriorityForCurrentCell(priority);
        }

        private void SetAllIfManual(int priority)
        {
            if (WorkTableState.IsManualMode) SetPriorityForAllColonists(priority);
        }

        private void SetPriorityForCurrentCell(int priority)
        {
            if (priority < 0 || priority > 4) return;
            RefreshModel();
            Pawn pawn = CurrentPawn;
            WorkTypeDef workType = CurrentWorkType;
            if (pawn == null || workType == null)
            {
                RejectAndAnnounce("RimWorldAccess.Work.Table.SelectWorkTypeFirst".Translate().ToString());
                return;
            }
            if (pawn.WorkTypeIsDisabled(workType))
            {
                AnnounceIncapable(pawn, workType);
                return;
            }
            if (ApplyPriority(pawn, workType, priority))
            {
                AfterCellEdit();
            }
        }

        private void ToggleCurrentCell()
        {
            RefreshModel();
            Pawn pawn = CurrentPawn;
            WorkTypeDef workType = CurrentWorkType;
            if (pawn == null || workType == null) return;
            if (pawn.WorkTypeIsDisabled(workType))
            {
                AnnounceIncapable(pawn, workType);
                return;
            }
            int current = pawn.workSettings.GetPriority(workType);
            int next = current > 0 ? 0 : 3;
            if (ApplyPriority(pawn, workType, next))
            {
                AfterCellEdit();
            }
        }

        private void CycleCurrentCellPriority(bool decrease)
        {
            RefreshModel();
            Pawn pawn = CurrentPawn;
            WorkTypeDef workType = CurrentWorkType;
            if (pawn == null || workType == null)
            {
                RejectAndAnnounce("RimWorldAccess.Work.Table.SelectWorkTypeFirst".Translate().ToString());
                return;
            }
            if (pawn.WorkTypeIsDisabled(workType))
            {
                AnnounceIncapable(pawn, workType);
                return;
            }
            int current = pawn.workSettings.GetPriority(workType);
            if (!WorkPriorityColumnHandler.TryComputeCycleTarget(current, decrease, out int next))
            {
                // Already at a bound: re-announce the cell in full, column context.
                AnnounceCurrent(CellAxis.Column);
                return;
            }
            if (ApplyPriority(pawn, workType, next))
            {
                AfterCellEdit();
            }
        }

        private void SetPriorityForAllColonists(int priority)
        {
            if (priority < 0 || priority > 4) return;
            if (!WorkTableState.IsManualMode) return;
            RefreshModel();

            WorkTypeDef workType = CurrentWorkType;
            if (workType == null)
            {
                RejectAndAnnounce("RimWorldAccess.Work.Table.SelectWorkTypeFirst".Translate().ToString());
                return;
            }

            var changed = new List<string>();
            bool anyLowSkillActivated = false;
            bool anyIdeoOpposedActivated = false;
            var ideoOpposedPawns = new List<Pawn>();

            foreach (var pawn in pawnsList)
            {
                if (pawn.workSettings == null || !pawn.workSettings.EverWork) continue;
                if (pawn.WorkTypeIsDisabled(workType)) continue;

                int current = pawn.workSettings.GetPriority(workType);
                if (current == priority) continue;

                bool wasActive = pawn.workSettings.WorkIsActive(workType);
                pawn.workSettings.SetPriority(workType, priority);
                changed.Add(pawn.LabelShort);

                if (!wasActive && pawn.workSettings.WorkIsActive(workType))
                {
                    if (workType.relevantSkills.Any() && pawn.skills.AverageOfRelevantSkillsFor(workType) <= 2f)
                        anyLowSkillActivated = true;
                    if (pawn.Ideo != null && pawn.Ideo.IsWorkTypeConsideredDangerous(workType))
                    {
                        anyIdeoOpposedActivated = true;
                        ideoOpposedPawns.Add(pawn);
                    }
                }
            }

            if (changed.Count == 0)
            {
                RejectAndAnnounce("RimWorldAccess.Work.Table.NoChangeFor".Translate(workType.labelShort).ToString());
                return;
            }

            PlayPriorityChangeSound();
            if (anyLowSkillActivated) SoundDefOf.Crunch.PlayOneShotOnCamera();
            if (anyIdeoOpposedActivated)
            {
                SoundDefOf.DislikedWorkTypeActivated.PlayOneShotOnCamera();
                foreach (var p in ideoOpposedPawns)
                    Messages.Message(
                        "MessageIdeoOpposedWorkTypeSelected".Translate(p, workType.gerundLabel),
                        p, MessageTypeDefOf.CautionInput, historical: false);
            }

            string stateLabel = StateLabel(priority);
            TolkHelper.SpeakData(
                "RimWorldAccess.Work.Table.SetStateForList".Translate(
                    workType.labelShort, stateLabel, MenuHelper.FormatNameList(changed)).ToString());
            SilentResortIfNeeded();
        }

        private void CycleAllColonistsPriority(bool decrease)
        {
            RefreshModel();
            WorkTypeDef workType = CurrentWorkType;
            if (workType == null)
            {
                RejectAndAnnounce("RimWorldAccess.Work.Table.SelectWorkTypeFirst".Translate().ToString());
                return;
            }

            var changed = new List<string>();
            bool anyLowSkillActivated = false;
            bool anyIdeoOpposedActivated = false;
            var ideoOpposedPawns = new List<Pawn>();

            foreach (var pawn in pawnsList)
            {
                if (pawn.workSettings == null || !pawn.workSettings.EverWork) continue;
                if (pawn.WorkTypeIsDisabled(workType)) continue;

                int current = pawn.workSettings.GetPriority(workType);
                if (!WorkPriorityColumnHandler.TryComputeCycleTarget(current, decrease, out int next)) continue;
                if (next == current) continue;

                bool wasActive = pawn.workSettings.WorkIsActive(workType);
                pawn.workSettings.SetPriority(workType, next);
                changed.Add(pawn.LabelShort);

                if (!wasActive && pawn.workSettings.WorkIsActive(workType))
                {
                    if (workType.relevantSkills.Any() && pawn.skills.AverageOfRelevantSkillsFor(workType) <= 2f)
                        anyLowSkillActivated = true;
                    if (pawn.Ideo != null && pawn.Ideo.IsWorkTypeConsideredDangerous(workType))
                    {
                        anyIdeoOpposedActivated = true;
                        ideoOpposedPawns.Add(pawn);
                    }
                }
            }

            if (changed.Count == 0)
            {
                RejectAndAnnounce("RimWorldAccess.Work.Table.NoChangeFor".Translate(workType.labelShort).ToString());
                return;
            }

            PlayPriorityChangeSound();
            if (anyLowSkillActivated) SoundDefOf.Crunch.PlayOneShotOnCamera();
            if (anyIdeoOpposedActivated)
            {
                SoundDefOf.DislikedWorkTypeActivated.PlayOneShotOnCamera();
                foreach (var p in ideoOpposedPawns)
                    Messages.Message(
                        "MessageIdeoOpposedWorkTypeSelected".Translate(p, workType.gerundLabel),
                        p, MessageTypeDefOf.CautionInput, historical: false);
            }

            string nameList = MenuHelper.FormatNameList(changed);
            string actionAnnouncement = decrease
                ? "RimWorldAccess.Work.Table.RaisedForList".Translate(workType.labelShort, nameList).ToString()
                : "RimWorldAccess.Work.Table.LoweredForList".Translate(workType.labelShort, nameList).ToString();
            TolkHelper.SpeakData(actionAnnouncement);
            SilentResortIfNeeded();
        }

        /// <summary>Applies a single-cell priority change; returns false (already announced) when the value didn't change.</summary>
        private static bool ApplyPriority(Pawn pawn, WorkTypeDef workType, int newPriority)
        {
            int current = pawn.workSettings.GetPriority(workType);
            if (current == newPriority)
            {
                TolkHelper.SpeakData(
                    "RimWorldAccess.Work.Table.AlreadyState".Translate(
                        pawn.LabelShort, workType.labelShort, StateLabel(newPriority)).ToString());
                return false;
            }
            bool wasActive = pawn.workSettings.WorkIsActive(workType);
            pawn.workSettings.SetPriority(workType, newPriority);
            PlayPriorityChangeSound();
            PlayActivationSounds(pawn, workType, wasActive);
            return true;
        }

        /// <summary>
        /// Re-sorts in place when the table is actively sorted by the just-edited column, keeping
        /// the cursor at the same ROW POSITION rather than following the edited item, then speaks
        /// either the changed state alone or a full re-orientation if the resort moved a different
        /// pawn under the cursor.
        /// </summary>
        private void AfterCellEdit()
        {
            TableModel table = Model.CurrentTable;
            if (table != null && table.HasActiveSort && table.SortColumnIndex == table.ColumnIndex)
            {
                List<Pawn> resorted = WorkTableHelper.SortPawnsByColumn(pawnsList, table.ColumnIndex, table.SortDescending);
                pawnsList.Clear();
                pawnsList.AddRange(resorted);
                int currentRow = table.Rows.Index - 1;
                int clamped = Math.Min(currentRow, pawnsList.Count - 1);
                if (clamped >= 0)
                {
                    table.Rows.MoveTo(clamped + 1);
                }
                // Row axis: the resort changed the pawn under the cursor but not the column, and
                // re-reading the column tooltip would repeat the work description on every edit.
                AnnounceCurrent(CellAxis.Row);
                return;
            }
            // The cursor did not move, so speak only the new state.
            AnnounceCurrentCellStateChange();
        }

        /// <summary>Silent resort for bulk column edits: the bulk message already said what changed, so nothing re-orients here.</summary>
        private void SilentResortIfNeeded()
        {
            TableModel table = Model.CurrentTable;
            if (table == null || !table.HasActiveSort || table.SortColumnIndex != table.ColumnIndex)
                return;
            List<Pawn> resorted = WorkTableHelper.SortPawnsByColumn(pawnsList, table.ColumnIndex, table.SortDescending);
            pawnsList.Clear();
            pawnsList.AddRange(resorted);
        }

        private static string StateLabel(int priority)
        {
            if (!Find.PlaySettings.useWorkPriorities)
                return (priority > 0
                    ? "RimWorldAccess.Work.Status.Enabled".Translate()
                    : "RimWorldAccess.Work.Status.Disabled".Translate()).ToString();
            return "RimWorldAccess.Work.PriorityLabel".Translate(priority).ToString();
        }

        private static void PlayPriorityChangeSound()
        {
            if (Find.PlaySettings.useWorkPriorities)
                SoundDefOf.DragSlider.PlayOneShotOnCamera();
            else
                SoundDefOf.Checkbox_TurnedOn.PlayOneShotOnCamera();
        }

        private static void PlayActivationSounds(Pawn pawn, WorkTypeDef workType, bool wasActive)
        {
            if (wasActive) return;
            if (!pawn.workSettings.WorkIsActive(workType)) return;

            if (workType.relevantSkills.Any() && pawn.skills.AverageOfRelevantSkillsFor(workType) <= 2f)
                SoundDefOf.Crunch.PlayOneShotOnCamera();
            if (pawn.Ideo != null && pawn.Ideo.IsWorkTypeConsideredDangerous(workType))
            {
                Messages.Message(
                    "MessageIdeoOpposedWorkTypeSelected".Translate(pawn, workType.gerundLabel),
                    pawn, MessageTypeDefOf.CautionInput, historical: false);
                SoundDefOf.DislikedWorkTypeActivated.PlayOneShotOnCamera();
            }
        }

        private static void AnnounceIncapable(Pawn pawn, WorkTypeDef workType)
        {
            SoundDefOf.ClickReject.PlayOneShotOnCamera();
            string reasons = WorkTableHelper.BuildDisabledReasons(pawn, workType);
            string detail = string.IsNullOrEmpty(reasons) ? "" : ": " + reasons;
            TolkHelper.SpeakData(
                "RimWorldAccess.Work.Table.CannotDo".Translate(
                    pawn.LabelShort, workType.labelShort, detail).ToString(),
                SpeechPriority.High);
        }

        private static void RejectAndAnnounce(string message)
        {
            SoundDefOf.ClickReject.PlayOneShotOnCamera();
            TolkHelper.SpeakData(message);
        }

        // Row copy/paste: the same clipboard and vehicle as the copy/paste column's icon buttons.
        // The cells read their values live, so a paste needs no rebuild and moves no cursor.

        private void CopyPriorities()
        {
            RefreshModel();
            Pawn pawn = CurrentPawn;
            if (!WorkTableHelper.IsCopyPasteEligible(pawn))
            {
                RejectAndAnnounce("RimWorldAccess.Work.Table.CopyPasteUnavailable".Translate(PawnNameOrUnknown(pawn)).ToString());
                return;
            }
            WorkTableHelper.CopyPriorities(pawn);
            TolkHelper.Speak("RimWorldAccess.Work.CopyPasteAnnouncement".Loc("Copy".Translate(), pawn.LabelShort));
        }

        private void PastePriorities()
        {
            if (!WorkTableHelper.CanPastePriorities)
            {
                TolkHelper.Speak("RimWorldAccess.Work.NoPrioritiesCopied".Loc());
                return;
            }
            RefreshModel();
            Pawn pawn = CurrentPawn;
            if (!WorkTableHelper.IsCopyPasteEligible(pawn))
            {
                RejectAndAnnounce("RimWorldAccess.Work.Table.CopyPasteUnavailable".Translate(PawnNameOrUnknown(pawn)).ToString());
                return;
            }
            WorkTableHelper.PastePriorities(pawn);
            TolkHelper.Speak("RimWorldAccess.Work.CopyPasteAnnouncement".Loc("Paste".Translate(), pawn.LabelShort));
        }

        private static string PawnNameOrUnknown(Pawn pawn)
        {
            return pawn?.LabelShort ?? "RimWorldAccess.Work.UnknownPawn".Translate().ToString();
        }

        // Mode toggle.

        private void ToggleMode()
        {
            RefreshModel();
            // MUTATION-C: mirrors MainTabWindow_Work.DoManualPrioritiesCheckbox's bare ref-flip
            // (RimWorld/MainTabWindow_Work.cs:42) — no CanX/TryX gate exists on this checkbox.
            Find.PlaySettings.useWorkPriorities = !Find.PlaySettings.useWorkPriorities;
            // Vanilla's flip-time sweep touches every player pawn across every map, caravan, and
            // temporary faction map, not just the rows this table is showing; RefreshAllWorkGivers
            // is scoped to pawnsList on purpose for its own callers.
            RefreshUseWorkPrioritiesForAllPlayerPawns();
            TolkHelper.Speak(Find.PlaySettings.useWorkPriorities
                ? "RimWorldAccess.Work.Mode.Manual".Loc()
                : "RimWorldAccess.Work.Mode.Basic".Loc());
            AnnounceCurrent(CellAxis.Column);
        }

        /// <summary>Vanilla's flip-time sweep: every player pawn with a workSettings tracker anywhere, not just this table's pawnsList.</summary>
        private static void RefreshUseWorkPrioritiesForAllPlayerPawns()
        {
            foreach (Pawn pawn in PawnsFinder.AllMapsWorldAndTemporary_Alive)
            {
                if (pawn.Faction == Faction.OfPlayer && pawn.workSettings != null)
                {
                    pawn.workSettings.Notify_UseWorkPrioritiesChanged();
                }
            }
        }

        // Painting (verbatim behavior from the legacy handler).

        /// <summary>Next data-row index for painting: search-match-aware like row navigation, else a plain wrap.</summary>
        private int NextPaintRow(int currentRow, int delta)
        {
            int count = pawnsList.Count;
            if (TypeaheadHasActiveSearch)
            {
                int matchRow = TypeaheadMatchRowInRegion(0, currentRow, delta);
                if (matchRow >= 0 && matchRow < count)
                    return matchRow;
            }
            return ((currentRow + delta) % count + count) % count;
        }

        private void PaintDown()
        {
            PaintSingle(1);
        }

        private void PaintUp()
        {
            PaintSingle(-1);
        }

        private void PaintSingle(int delta)
        {
            if (pawnsList.Count < 2) return;
            RefreshModel();
            WorkTypeDef workType = CurrentWorkType;
            if (workType == null)
            {
                RejectAndAnnounce("RimWorldAccess.Work.Table.CannotPaintNameColumn".Translate().ToString());
                return;
            }
            Pawn source = CurrentPawn;
            if (source == null) return;
            int brush = source.WorkTypeIsDisabled(workType)
                ? -1
                : source.workSettings.GetPriority(workType);
            if (brush < 0)
            {
                RejectAndAnnounce("RimWorldAccess.Work.Table.SourceIncapableOf".Translate(source.LabelShort, workType.labelShort).ToString());
                return;
            }

            TableModel table = Model.CurrentTable;
            if (table == null) return;
            int sourceRow = table.Rows.Index - 1;
            int targetRow = NextPaintRow(sourceRow, delta);
            if (targetRow < 0 || targetRow >= pawnsList.Count) return;
            table.Rows.MoveTo(targetRow + 1);
            Pawn target = pawnsList[targetRow];
            PaintApplyAndAnnounce(target, workType, brush, targetRow);
        }

        private void PaintApplyAndAnnounce(Pawn target, WorkTypeDef workType, int brush, int targetRow)
        {
            string position = MenuHelper.FormatPosition(targetRow, pawnsList.Count);
            if (target.WorkTypeIsDisabled(workType))
            {
                TolkHelper.SpeakData(
                    "RimWorldAccess.Work.Table.PaintIncapable".Translate(target.LabelShort, position).ToString());
                return;
            }
            int current = target.workSettings.GetPriority(workType);
            if (current == brush)
            {
                TolkHelper.SpeakData(
                    "RimWorldAccess.Work.Table.PaintAlreadyState".Translate(
                        target.LabelShort, StateLabel(brush), position).ToString());
                return;
            }
            bool wasActive = target.workSettings.WorkIsActive(workType);
            target.workSettings.SetPriority(workType, brush);
            PlayPriorityChangeSound();
            PlayActivationSounds(target, workType, wasActive);
            TolkHelper.SpeakData(
                "RimWorldAccess.Work.Table.PaintedState".Translate(
                    target.LabelShort, StateLabel(brush), position).ToString());
        }

        private void PaintToFirst() => PaintRange(toward: -1);
        private void PaintToLast() => PaintRange(toward: 1);

        private void PaintRange(int toward)
        {
            if (pawnsList.Count == 0) return;
            RefreshModel();
            WorkTypeDef workType = CurrentWorkType;
            if (workType == null)
            {
                RejectAndAnnounce("RimWorldAccess.Work.Table.CannotPaintNameColumn".Translate().ToString());
                return;
            }
            Pawn source = CurrentPawn;
            if (source == null) return;
            if (source.WorkTypeIsDisabled(workType))
            {
                RejectAndAnnounce("RimWorldAccess.Work.Table.SourceIncapableOf".Translate(source.LabelShort, workType.labelShort).ToString());
                return;
            }
            int brush = source.workSettings.GetPriority(workType);
            TableModel table = Model.CurrentTable;
            if (table == null) return;
            int currentRow = table.Rows.Index - 1;
            int startRow, endRow;
            if (toward < 0) { startRow = 0; endRow = currentRow; }
            else { startRow = currentRow; endRow = pawnsList.Count - 1; }

            PaintBulk(workType, brush, startRow, endRow, toward < 0);
        }

        private void PaintEntireColumn()
        {
            if (pawnsList.Count == 0) return;
            RefreshModel();
            WorkTypeDef workType = CurrentWorkType;
            if (workType == null)
            {
                RejectAndAnnounce("RimWorldAccess.Work.Table.CannotPaintNameColumn".Translate().ToString());
                return;
            }
            Pawn source = CurrentPawn;
            if (source == null) return;
            if (source.WorkTypeIsDisabled(workType))
            {
                RejectAndAnnounce("RimWorldAccess.Work.Table.SourceIncapableOf".Translate(source.LabelShort, workType.labelShort).ToString());
                return;
            }
            int brush = source.workSettings.GetPriority(workType);
            PaintBulk(workType, brush, 0, pawnsList.Count - 1, moveToStart: false);
        }

        private void PaintBulk(WorkTypeDef workType, int brush, int startRow, int endRow, bool moveToStart)
        {
            var changed = new List<string>();
            bool anyLowSkill = false;
            bool anyIdeoOpposed = false;
            var ideoOpposedPawns = new List<Pawn>();

            for (int i = startRow; i <= endRow; i++)
            {
                Pawn p = pawnsList[i];
                if (p.WorkTypeIsDisabled(workType)) continue;
                int current = p.workSettings.GetPriority(workType);
                if (current == brush) continue;
                bool wasActive = p.workSettings.WorkIsActive(workType);
                p.workSettings.SetPriority(workType, brush);
                changed.Add(p.LabelShort);
                if (!wasActive && p.workSettings.WorkIsActive(workType))
                {
                    if (workType.relevantSkills.Any() && p.skills.AverageOfRelevantSkillsFor(workType) <= 2f)
                        anyLowSkill = true;
                    if (p.Ideo != null && p.Ideo.IsWorkTypeConsideredDangerous(workType))
                    {
                        anyIdeoOpposed = true;
                        ideoOpposedPawns.Add(p);
                    }
                }
            }

            TableModel table = Model.CurrentTable;
            if (table != null)
            {
                table.Rows.MoveTo((moveToStart ? startRow : endRow) + 1);
            }

            if (changed.Count == 0)
            {
                TolkHelper.SpeakData(
                    "RimWorldAccess.Work.Table.RangeAlreadyState".Translate(
                        workType.labelShort, StateLabel(brush)).ToString());
                return;
            }

            BulkSoundQueue.Queue(changed.Count, Find.PlaySettings.useWorkPriorities
                ? SoundDefOf.DragSlider
                : SoundDefOf.Checkbox_TurnedOn);
            if (anyLowSkill) SoundDefOf.Crunch.PlayOneShotOnCamera();
            if (anyIdeoOpposed)
            {
                SoundDefOf.DislikedWorkTypeActivated.PlayOneShotOnCamera();
                foreach (var p in ideoOpposedPawns)
                    Messages.Message(
                        "MessageIdeoOpposedWorkTypeSelected".Translate(p, workType.gerundLabel),
                        p, MessageTypeDefOf.CautionInput, historical: false);
            }
            TolkHelper.SpeakData(
                "RimWorldAccess.Work.Table.PaintedForList".Translate(
                    workType.labelShort, StateLabel(brush), MenuHelper.FormatNameList(changed)).ToString());
            SilentResortIfNeeded();
        }

        // Close / confirm / swap.

        private void RefreshAllWorkGivers()
        {
            for (int i = 0; i < pawnsList.Count; i++)
            {
                pawnsList[i].workSettings?.Notify_UseWorkPrioritiesChanged();
            }
        }

        private void Confirm()
        {
            RefreshAllWorkGivers();
            WorkTableState.Close();
            TolkHelper.Speak("RimWorldAccess.Work.Saved".Loc());
            MainTabWindowLink.CloseTab(MainTabWindowLink.Work);
        }

        private void SwapToFocusedView()
        {
            RefreshModel();
            RefreshAllWorkGivers();
            Pawn current = CurrentPawn;
            // SwapToFocused closes the table state itself; closing it here first trips its
            // IsActive guard, the focused view never opens, and the player is stranded on the map.
            WorkMenuOpener.SwapToFocused(current);
        }

        // IPawnTableFocusSource: the focused (pawn, column) cell for PawnTableFocusDriver.
        // WorkColumnFor is duplicated from WorkMenuScope rather than shared — the two scopes are
        // mutually exclusive siblings, not a hierarchy.

        /// <summary>Alt+Shift+J routes on the real vanilla table drawing underneath, which this scope reads but was never attached to.</summary>
        protected override Window PointerSurface
        {
            get { return PawnTableFocusDriver.OpenTabWindowFor(this); }
        }

        private protected override void CollectRouteCandidates(
            List<PointerHitCandidate> candidates, List<RouteTarget> targets)
        {
            PawnTableExtrasRouter.AddRouteCandidates(
                0, pawnsList, ContentColumnCount(0), ColumnDefFor, candidates, targets);
        }

        Type IPawnTableFocusSource.TabWindowType
        {
            get { return typeof(MainTabWindow_Work); }
        }

        Pawn IPawnTableFocusSource.FocusedTablePawn
        {
            get { return CurrentPawn; }
        }

        PawnColumnDef IPawnTableFocusSource.FocusedTableColumn
        {
            get { return WorkColumnFor(CurrentWorkType); }
        }

        bool IPawnTableFocusSource.FocusedOnHeaderRow
        {
            get
            {
                TableModel table = Model.CurrentTable;
                return table != null && table.Rows.Index == 0;
            }
        }

        /// <summary>Vanilla's own column behind one of this table's column indices, or null when it has none.</summary>
        private static PawnColumnDef ColumnDefFor(int column)
        {
            if (column == WorkTableHelper.NameColumnIndex)
                return PawnTableDefOf.Work.columns.Find(c => c.Worker is PawnColumnWorker_Label);
            if (column == WorkTableHelper.CopyPasteColumnIndex)
                return WorkTableHelper.CopyPasteWorkPrioritiesDef;
            return WorkColumnFor(WorkTableHelper.WorkTypeForColumn(column));
        }

        /// <summary>Vanilla's own work column for a WorkTypeDef, or null when none is visible.</summary>
        private static PawnColumnDef WorkColumnFor(WorkTypeDef workType)
        {
            if (workType == null) return null;
            return PawnTableDefOf.Work.columns.Find(c => c.workType == workType);
        }
    }

    /// <summary>
    /// Keeps <see cref="WorkTableScope"/> in lockstep with <see cref="WorkTableState.IsActive"/>,
    /// reconciled every OnGUI pass AFTER <see cref="GizmoScopeMirror"/> for the reason
    /// <see cref="WorkMenuScopeMirror"/> gives. No yield gate is needed: this screen has no Alt+I
    /// path and WorkMenuOpener always closes one view before opening the other.
    /// </summary>
    internal static class WorkTableScopeMirror
    {
        private static readonly WorkTableScope scope = new WorkTableScope();

        /// <summary>The live scope instance, for the same-assembly overlay reads.</summary>
        internal static WorkTableScope Instance
        {
            get { return scope; }
        }

        public static void Reconcile()
        {
            if (WorkTableState.IsActive)
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
