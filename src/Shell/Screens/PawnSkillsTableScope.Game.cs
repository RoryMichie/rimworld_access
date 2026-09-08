using System.Collections.Generic;
using RimWorld;
using Verse;
using Verse.Sound;

namespace RimWorldAccess.Shell
{
    /// <summary>
    /// The keyboard focus scope for the read-only pawn skills table, tabular by pawn/skill
    /// columns. The mod owns no window here — the surface is the windowless
    /// <see cref="PawnSkillsTableState"/> — so the scope rides the focus stack through
    /// <see cref="PawnSkillsTableScopeMirror"/>.
    ///
    /// A single-region <see cref="ScreenScope"/> TABLE region: one content region, no Buttons
    /// region, since the screen is read-only. Row/column moves, Home/End, the header row and the
    /// vanilla 3-state sort cycle are all base behavior; this class supplies only the data hooks
    /// (<see cref="ContentColumnInfo"/>, <see cref="ContentCellText"/>,
    /// <see cref="ApplyContentSort"/>) plus typeahead/close plumbing.
    ///
    /// Its own claims are three: <c>menus.cancel</c> (no owning <c>Window</c> exists, so
    /// <see cref="OwnsCancel"/> is true and Escape must be self-claimed),
    /// <c>menus.searchBackspace</c>, and <c>pawnSkillsTable.close</c> (Alt+P toggle-close,
    /// mirroring the opener). Row/column/Home/End/Enter and Alt+S are claimed once by
    /// <see cref="ScreenScope"/>'s constructor — re-claiming them here would shadow the base's
    /// table-aware handling for nothing.
    /// </summary>
    public sealed class PawnSkillsTableScope : ScreenScope
    {
        private bool announcedOpen;

        public PawnSkillsTableScope()
        {
            Claim(SharedMenuGrammar.Cancel, delegate { OnCancel(); });

            Claim("pawnSkillsTable.close", delegate { PawnSkillsTableState.Close(); });
        }

        public override string Name
        {
            get { return "pawn-skills-table"; }
        }

        /// <summary>Shared cross-region typeahead over the pawn-name row labels.</summary>
        protected override bool EnableTypeahead
        {
            get { return true; }
        }

        /// <summary>No owning <c>Window</c> exists for this windowless overlay — self-claim Escape.</summary>
        public override bool OwnsCancel
        {
            get { return true; }
        }

        // One region, one table: rows are colonists (PawnSkillsTableState.Pawns order), columns
        // are Name + every SkillDef.

        protected override int ContentRegionCount
        {
            get { return 1; }
        }

        protected override string ContentRegionName(int region)
        {
            return "Skills".Translate().ToString();
        }

        protected override int ContentItemCount(int region)
        {
            return PawnSkillsTableState.Pawns.Count;
        }

        protected override int ContentColumnCount(int region)
        {
            return PawnSkillsTableHelper.TotalColumnCount;
        }

        protected override TableColumnInfo ContentColumnInfo(int region, int column)
        {
            return new TableColumnInfo(
                PawnSkillsTableHelper.GetColumnName(column),
                PawnSkillsTableHelper.GetColumnTooltip(column),
                PawnSkillsTableHelper.IsColumnSortable(column));
        }

        protected override ElementDescription DescribeContentItem(int region, int index)
        {
            ElementDescription d = new ElementDescription();
            d.Label = PawnSkillsTableHelper.GetPawnLabel(PawnSkillsTableState.PawnAt(index));
            return d;
        }

        protected override string ContentCellText(int region, int row, int column)
        {
            return PawnSkillsTableHelper.GetColumnValue(PawnSkillsTableState.PawnAt(row), column);
        }

        /// <summary>
        /// The screen is read-only: Enter closes the table. Enter during an active search never
        /// reaches here — the base settles the search first.
        /// </summary>
        protected override void ActivateContentItem(int region, int index)
        {
            PawnSkillsTableState.Close();
        }

        /// <summary>
        /// Re-orders through the twin's vanilla <see cref="RimWorld.PawnTable"/>, the sort
        /// authority while the window exists, so keyboard rows and drawn rows cannot diverge; the
        /// state's own list is re-sorted too as the fallback for an open with no live table. Both
        /// paths run the one comparer in <see cref="PawnSkillsTableHelper"/>, never display
        /// strings. The tracked row's new index goes back to the base so the cursor keeps its
        /// colonist. The vanilla sort-feedback sounds have no home in the base sort cycle, so they
        /// play here, the one place that knows which happened.
        /// </summary>
        protected override int ApplyContentSort(int region, int column, SortCycleResult cycle, int currentRow)
        {
            Pawn tracked = currentRow >= 0 ? PawnSkillsTableState.PawnAt(currentRow) : null;
            if (cycle == SortCycleResult.Cleared)
            {
                PawnSkillsTableTwin.SortBy(-1, false);
                PawnSkillsTableState.RestoreDefaultOrder();
                SoundDefOf.Tick_Low.PlayOneShotOnCamera();
            }
            else
            {
                bool descending = cycle == SortCycleResult.SortedDescending;
                PawnSkillsTableTwin.SortBy(column, descending);
                PawnSkillsTableState.SortByColumn(column, descending);
                SoundDefOf.Tick_High.PlayOneShotOnCamera();
            }
            return tracked == null ? -1 : PawnSkillsTableState.IndexOf(tracked);
        }

        /// <summary>Read-only screen, no actions beyond the chords above — no Buttons region.</summary>
        protected override bool IncludeActionsRegion
        {
            get { return false; }
        }

        // ------------------------------------------------------------------
        // Lifecycle.
        // ------------------------------------------------------------------

        public override void OnPush()
        {
            base.OnPush();
            announcedOpen = false;
            TypeaheadReset();
            PawnSkillsTableTwin.Mount(this);
        }

        public override void OnPop()
        {
            base.OnPop();
            PawnSkillsTableTwin.Unmount(this);
        }

        /// <summary>
        /// The cursor as the twin needs it: the data row (-1 while the cursor rests on the
        /// column-header row, which the base models as row 0) and the column index. False
        /// means there is nothing to ring.
        /// </summary>
        internal bool TryGetTwinFocus(out int row, out int column)
        {
            row = -1;
            column = -1;
            TableModel table = Model.CurrentTable;
            if (table == null || table.Rows.IsEmpty)
            {
                return false;
            }
            row = table.Rows.Index - 1;
            column = table.ColumnIndex;
            return true;
        }

        /// <summary>
        /// First focus after <see cref="PawnSkillsTableState.Open"/>: speaks the opening summary
        /// then the first cell, and pre-positions the column cursor on the first skill rather than
        /// Name so Alt+S sorts by a skill immediately. Two utterances because the composer exposes
        /// no string-returning cell text to subclasses, only the speak-directly
        /// <see cref="AnnounceCurrentItem"/>.
        /// </summary>
        public override void OnFocus()
        {
            base.OnFocus();
            if (announcedOpen) return;
            announcedOpen = true;

            TableModel table = Model.CurrentTable;
            if (table != null && PawnSkillsTableHelper.Skills.Count > 0)
            {
                table.MoveToColumn(1);
            }

            TolkHelper.SpeakData("RimWorldAccess.Pawns.SkillsTable.Opened".Translate(
                PawnSkillsTableState.Pawns.Count.ToString(), PawnSkillsTableHelper.Skills.Count.ToString()).ToString());
            AnnounceCurrentItem();
        }

        // Typeahead is the base ScreenScope engine.

        private void OnCancel()
        {
            // Escape-clears-search is the base typeahead claim, registered ahead of this one, so
            // reaching here means no search is active.
            ShellFrameStamps.MarkCancelConsumed();
            PawnSkillsTableState.Close();
        }
    }

    /// <summary>
    /// Keeps <see cref="PawnSkillsTableScope"/> in lockstep with
    /// <see cref="PawnSkillsTableState.IsActive"/>, reconciled every OnGUI pass in the
    /// chain-independent group: this screen never coexists with the inspection/bills/gizmo
    /// families, so reconcile order relative to those never matters. Pops while an info card is
    /// open, the universal defensive yield.
    /// </summary>
    internal static class PawnSkillsTableScopeMirror
    {
        private static readonly PawnSkillsTableScope scope = new PawnSkillsTableScope();

        public static void Reconcile()
        {
            if (PawnSkillsTableState.IsActive && !InfoCardState.IsActive)
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
