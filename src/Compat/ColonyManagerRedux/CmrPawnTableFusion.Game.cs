using System;
using System.Collections.Generic;
using RimWorld;
using Verse;

namespace RimWorldAccess.Shell
{
    /// <summary>
    /// Safe reads over a live vanilla <c>PawnTable</c>: rows, visible columns, per-cell text/tip/
    /// activation through the column handler registry, each guarded so a mod column's throw costs
    /// that value rather than the screen. Shared by every Colony Manager Redux provider that
    /// presents one of the mod's own animal/pawn tables (Livestock's tame and wild tables,
    /// Overview's worker table — see <see cref="CmrDetailPawnTable"/>), so the read and guard
    /// behavior cannot drift between them.
    /// </summary>
    internal static class CmrPawnTableFusion
    {
        private static readonly HashSet<string> loggedFailures = new HashSet<string>();

        /// <summary>
        /// The table's own live rows. The table's own list, so the mod's column-injection hijack
        /// (which rides its pawn getter) has already run and the sort order is the one on screen.
        /// </summary>
        public static List<Pawn> Rows(PawnTable table)
        {
            var rows = new List<Pawn>();
            if (table == null)
            {
                return rows;
            }
            try
            {
                List<Pawn> live = table.PawnsListForReading;
                for (int i = 0; i < live.Count; i++)
                {
                    if (live[i] != null)
                    {
                        rows.Add(live[i]);
                    }
                }
            }
            catch (Exception ex)
            {
                LogOnce("PawnsListForReading", ex);
                rows.Clear();
            }
            return rows;
        }

        /// <summary>
        /// The columns this table currently shows. A mod column can decide its own visibility from
        /// state that is not ready yet, so a throw here costs the tail rather than the screen.
        /// </summary>
        public static List<PawnColumnDef> VisibleColumns(PawnTable table)
        {
            var columns = new List<PawnColumnDef>();
            if (table == null)
            {
                return columns;
            }
            try
            {
                List<PawnColumnDef> visible = table.Columns;
                for (int i = 0; i < visible.Count; i++)
                {
                    PawnColumnDef def = visible[i];
                    if (def != null && !PawnColumnHandlerRegistry.Resolve(def).SkipColumn(def))
                    {
                        columns.Add(def);
                    }
                }
            }
            catch (Exception ex)
            {
                LogOnce("PawnTable.Columns", ex);
                columns.Clear();
            }
            return columns;
        }

        public static int LabelColumnIndex(List<PawnColumnDef> columns)
        {
            for (int i = 0; i < columns.Count; i++)
            {
                try
                {
                    if (columns[i].Worker is PawnColumnWorker_Label)
                    {
                        return i;
                    }
                }
                catch (Exception ex)
                {
                    LogOnce("LabelColumnIndex", ex);
                }
            }
            return -1;
        }

        public static string RowLabel(Pawn pawn, List<PawnColumnDef> columns, int labelColumn)
        {
            if (labelColumn >= 0 && labelColumn < columns.Count)
            {
                string label = CellText(columns[labelColumn], pawn);
                if (!string.IsNullOrEmpty(label))
                {
                    return CompatText.Flatten(label);
                }
            }
            return pawn.LabelShortCap.StripTags();
        }

        /// <summary>
        /// The column's own name: its label, then the first line of its header tooltip for the
        /// icon-headed columns that carry no label, then a handler's own name, then the bare defName
        /// -- the same order the generic pawn-table tier resolves it in.
        /// </summary>
        public static string ColumnHeader(PawnColumnDef def)
        {
            if (!def.label.NullOrEmpty())
            {
                return def.LabelCap.ToString();
            }
            string tipName = FirstLine(def.headerTip);
            if (tipName != null)
            {
                return tipName;
            }
            string handlerLabel = null;
            try
            {
                handlerLabel = PawnColumnHandlerRegistry.Resolve(def).HeaderLabel(def);
            }
            catch (Exception ex)
            {
                LogOnce("HeaderLabel." + def.defName, ex);
            }
            return !string.IsNullOrEmpty(handlerLabel) ? handlerLabel : def.defName;
        }

        public static string FirstLine(string text)
        {
            if (text.NullOrEmpty())
            {
                return null;
            }
            int newline = text.IndexOf('\n');
            string line = (newline >= 0 ? text.Substring(0, newline) : text).Trim();
            return line.Length > 0 ? line : null;
        }

        public static string CellText(PawnColumnDef def, Pawn pawn)
        {
            try
            {
                return PawnColumnHandlerRegistry.Resolve(def).CellText(def, pawn) ?? "";
            }
            catch (Exception ex)
            {
                LogOnce("CellText." + def.defName, ex);
                return "";
            }
        }

        public static string CellTip(PawnColumnDef def, Pawn pawn)
        {
            try
            {
                return PawnColumnHandlerRegistry.Resolve(def).CellTip(def, pawn) ?? "";
            }
            catch (Exception ex)
            {
                LogOnce("CellTip." + def.defName, ex);
                return "";
            }
        }

        /// <summary>The cell's own Enter behavior, routed the same way a captured cell click would run it. Pass the live table where the caller holds one, so a worker's sort-dirty hint lands.</summary>
        public static PawnColumnActivation ActivateCell(PawnColumnDef def, Pawn pawn, PawnTable table = null)
        {
            try
            {
                return PawnColumnHandlerRegistry.Resolve(def).ActivateCell(def, pawn, table);
            }
            catch (Exception ex)
            {
                LogOnce("ActivateCell." + def.defName, ex);
                return PawnColumnActivation.NotHandled;
            }
        }

        /// <summary>Whether the bracket chords do anything for this column — the claim guard.</summary>
        public static bool CanAdjustCell(PawnColumnDef def)
        {
            try
            {
                return PawnColumnHandlerRegistry.Resolve(def).CanAdjustCell(def);
            }
            catch (Exception ex)
            {
                LogOnce("CanAdjustCell." + def.defName, ex);
                return false;
            }
        }

        /// <summary>Left/Right bracket on the cell, routed the same way a captured cell click would run it.</summary>
        public static PawnColumnActivation AdjustCell(PawnColumnDef def, Pawn pawn, int direction, PawnTable table = null)
        {
            try
            {
                return PawnColumnHandlerRegistry.Resolve(def).AdjustCell(def, pawn, direction, table);
            }
            catch (Exception ex)
            {
                LogOnce("AdjustCell." + def.defName, ex);
                return PawnColumnActivation.NotHandled;
            }
        }

        private static void LogOnce(string member, Exception ex)
        {
            if (loggedFailures.Add(member))
            {
                ModLogger.Error("CmrPawnTableFusion." + member + " failed: " + ex.Message);
            }
        }
    }

    /// <summary>
    /// One of the mod's embedded pawn tables read once per refresh: its live rows, visible
    /// columns and label column, in the on-screen order. <see cref="Read"/> is the one
    /// construction path because the read order is load-bearing: <c>PawnsListForReading</c> is
    /// the mod's injection point for the column workers' instance/jobGetter state, and a
    /// column's own <c>VisibleCurrently</c> can dereference that state
    /// (ManagerTab_Livestock_AnimalsTable.cs:234, 301-334), so Rows MUST be read before Columns.
    /// </summary>
    internal sealed class CmrDetailPawnTable
    {
        public readonly PawnTable Table;
        public readonly List<Pawn> Rows;
        public readonly List<PawnColumnDef> Columns;
        public readonly int LabelColumn;

        private CmrDetailPawnTable(PawnTable table, List<Pawn> rows, List<PawnColumnDef> columns,
            int labelColumn)
        {
            Table = table;
            Rows = rows;
            Columns = columns;
            LabelColumn = labelColumn;
        }

        public static CmrDetailPawnTable Read(PawnTable table)
        {
            if (table == null)
            {
                return null;
            }
            List<Pawn> rows = CmrPawnTableFusion.Rows(table);
            List<PawnColumnDef> columns = CmrPawnTableFusion.VisibleColumns(table);
            return new CmrDetailPawnTable(table, rows, columns,
                CmrPawnTableFusion.LabelColumnIndex(columns));
        }

        public string RowLabel(int row)
        {
            if (row < 0 || row >= Rows.Count)
            {
                return "";
            }
            return CmrPawnTableFusion.RowLabel(Rows[row], Columns, LabelColumn);
        }
    }
}
