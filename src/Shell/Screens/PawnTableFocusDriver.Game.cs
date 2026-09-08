using System;
using System.Collections.Generic;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;

namespace RimWorldAccess.Shell
{
    /// <summary>
    /// The pinned inter-agent contract a windowless pawn-table scope
    /// implements so <see cref="PawnTableFocusDriver"/> can ring the exact
    /// (pawn, column) cell its own cursor corresponds to, on the REAL vanilla
    /// table now drawing underneath it.
    /// </summary>
    internal interface IPawnTableFocusSource
    {
        /// <summary>The exact MainTabWindow_PawnTable subtype this scope drives (e.g. typeof(MainTabWindow_Wildlife)).</summary>
        Type TabWindowType { get; }

        /// <summary>The pawn under the keyboard cursor right now; null draws nothing.</summary>
        Pawn FocusedTablePawn { get; }

        /// <summary>The column under the keyboard cursor; null rings the whole row.</summary>
        PawnColumnDef FocusedTableColumn { get; }

        /// <summary>
        /// True when the cursor rests on the sortable column-header row above the pawns,
        /// which every table scope models as row index 0. The header draws outside the
        /// table's scroll view, so it is rung from its own branch rather than through
        /// <see cref="FocusedTablePawn"/>, which is null here.
        /// </summary>
        bool FocusedOnHeaderRow { get; }
    }

    /// <summary>
    /// Rings the vanilla <see cref="PawnTable"/> cell a windowless scope's cursor corresponds to, for
    /// every pawn-table tab at once. The un-suppression makes five separate
    /// <see cref="MainTabWindow_PawnTable"/>
    /// subclasses draw for real, but the geometry <see cref="PawnTable.PawnTableOnGUI"/>
    /// computes — column x as the running sum of the table's cached column
    /// widths, row y as the running sum of its cached row heights, both inside
    /// a scroll view — is identical for all of them, so one shared postfix
    /// serves every tab rather than a bespoke patch per screen.
    ///
    /// Cells are drawn inside a scroll view that has already ended by the
    /// time this postfix runs, which is why the ring rect is converted into
    /// window space here instead of reusing the raw cell rect, and why a
    /// fully-offscreen focused row auto-scrolls into view on the next repaint
    /// instead of being drawn clipped. A `groupable` column that merges
    /// several rows into one tall cell still gets a ring around only the
    /// focused row's slice of that cell, which is deliberate: it shows the
    /// row, not the whole group.
    /// </summary>
    internal static class PawnTableFocusDriver
    {
        private static readonly AccessTools.FieldRef<MainTabWindow_PawnTable, PawnTable> tableField =
            AccessTools.FieldRefAccess<MainTabWindow_PawnTable, PawnTable>("table");
        private static readonly AccessTools.FieldRef<PawnTable, Vector2> scrollPositionField =
            AccessTools.FieldRefAccess<PawnTable, Vector2>("scrollPosition");
        private static readonly AccessTools.FieldRef<PawnTable, List<float>> cachedColumnWidthsField =
            AccessTools.FieldRefAccess<PawnTable, List<float>>("cachedColumnWidths");
        private static readonly AccessTools.FieldRef<PawnTable, List<float>> cachedRowHeightsField =
            AccessTools.FieldRefAccess<PawnTable, List<float>>("cachedRowHeights");

        /// <summary>The open pawn-table tab window this source drives, or null when its tab is not the open one.</summary>
        internal static Window OpenTabWindowFor(IPawnTableFocusSource source)
        {
            MainButtonDef openTab = Find.MainTabsRoot != null ? Find.MainTabsRoot.OpenTab : null;
            Window window = openTab != null ? openTab.TabWindow : null;
            return window != null && window.GetType() == source.TabWindowType ? window : null;
        }

        internal static void OnTableDrawn(PawnTable table, Vector2 position)
        {
            if (Event.current.type != EventType.Repaint)
            {
                return;
            }
            IPawnTableFocusSource source = FocusStackLookup.TopmostOfType<IPawnTableFocusSource>();
            if (source == null)
            {
                return;
            }

            var window = OpenTabWindowFor(source) as MainTabWindow_PawnTable;
            if (window == null || !ReferenceEquals(tableField(window), table))
            {
                return;
            }
            // Arms the shared geometry taps for the NEXT frame, which is what gives
            // these bespoke tabs their pointer-routing cells; the generic tier never
            // attaches here, so nothing else is claiming the router.
            PawnTableExtrasRouter.SetActiveTable(table);

            Rect cell;
            float scrollToShow;
            if (!TryGetCellRect(table, position, source.FocusedTablePawn, source.FocusedTableColumn,
                    source.FocusedOnHeaderRow, out cell, out scrollToShow))
            {
                if (scrollToShow >= 0f)
                {
                    // Off-screen: scroll it into view and let the next repaint draw it,
                    // the same auto-scroll contract ScheduleScope uses (ScheduleScope.Game.cs:392-416).
                    SetScrollY(table, scrollToShow);
                }
                return;
            }
            FocusRing.Draw(cell.ContractedBy(1f));
        }

        /// <summary>
        /// The screen rect of one drawn (row, column) cell, in the space
        /// <see cref="PawnTable.PawnTableOnGUI"/> just drew it in: column x as the running
        /// sum of the table's cached column widths, row y as the running sum of its cached
        /// row heights less the scroll offset. False means nothing to ring — either the
        /// layout could not be read, or the row is scrolled out of view, in which case
        /// <paramref name="scrollToShow"/> is the scroll offset that would bring it in
        /// (-1 otherwise).
        /// </summary>
        internal static bool TryGetCellRect(PawnTable table, Vector2 position, Pawn pawn,
            PawnColumnDef focusedColumn, bool headerRow, out Rect rect, out float scrollToShow)
        {
            rect = default(Rect);
            scrollToShow = -1f;

            List<float> widths = cachedColumnWidthsField(table);
            List<PawnColumnDef> columns = table.Columns;
            Vector2 size = table.Size;
            float headerHeight = table.HeaderHeight;
            // A recache can land mid-frame; bail rather than read stale/short lists.
            if (widths == null || widths.Count != columns.Count)
            {
                return false;
            }

            // Geometry mirrors PawnTableOnGUI exactly (decompiled RimWorld/PawnTable.cs:131-190).
            float totalColumnWidth = size.x - 16f;
            float cellX = 0f;
            float cellWidth = (int)size.x - 16f;
            if (focusedColumn != null)
            {
                int used = 0;
                for (int i = 0; i < columns.Count; i++)
                {
                    int w = (i != columns.Count - 1) ? (int)widths[i] : (int)(totalColumnWidth - used);
                    if (columns[i] == focusedColumn)
                    {
                        cellX = used;
                        cellWidth = w;
                        break;
                    }
                    used += w;
                }
                // If the focused column is not visible right now, fall through
                // with the whole-row default above rather than returning.
            }

            if (headerRow)
            {
                rect = new Rect((int)position.x + cellX, (int)position.y, cellWidth, (int)headerHeight);
                return true;
            }

            if (pawn == null)
            {
                return false;
            }
            int rowIndex = table.PawnsListForReading.IndexOf(pawn);
            List<float> heights = cachedRowHeightsField(table);
            if (rowIndex < 0 || heights == null || rowIndex >= heights.Count)
            {
                return false;
            }

            float rowY = 0f;
            for (int i = 0; i < rowIndex; i++)
            {
                rowY += (int)heights[i];
            }
            float rowHeight = (int)heights[rowIndex];

            Rect outRect = new Rect((int)position.x, (int)position.y + (int)headerHeight,
                (int)size.x, (int)size.y - (int)headerHeight);
            float screenY = outRect.y + rowY - scrollPositionField(table).y;

            if (screenY < outRect.y || screenY + rowHeight > outRect.yMax)
            {
                scrollToShow = rowY;
                return false;
            }

            rect = new Rect(outRect.x + cellX, screenY, cellWidth, rowHeight);
            return true;
        }

        /// <summary>Presentation state only: where the table's own scroll view is parked.</summary>
        internal static void SetScrollY(PawnTable table, float y)
        {
            ref Vector2 scroll = ref scrollPositionField(table);
            scroll.y = y;
        }
    }

    /// <summary>
    /// Brackets the shared focus ring to the table's own draw. Patches the
    /// DECLARING <see cref="PawnTable.PawnTableOnGUI"/>, which runs for every
    /// pawn-table tab regardless of which <see cref="MainTabWindow_PawnTable"/>
    /// subclass hosts it.
    /// </summary>
    [HarmonyPatch(typeof(PawnTable), "PawnTableOnGUI")]
    internal static class PawnTableFocusRingPatch
    {
        [HarmonyPostfix]
        public static void Postfix(PawnTable __instance, Vector2 position)
        {
            try
            {
                PawnTableFocusDriver.OnTableDrawn(__instance, position);
            }
            catch (Exception ex)
            {
                ModLogger.LimitedError("PawnTable focus ring error", ex);
            }
        }
    }
}
