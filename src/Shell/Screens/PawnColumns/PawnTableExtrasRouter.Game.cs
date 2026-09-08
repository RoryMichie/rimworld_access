using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;

namespace RimWorldAccess.Shell
{
    /// <summary>
    /// Per-frame absolute-screen geometry for the ONE live <see cref="PawnTable"/> a pawn-table
    /// scope owns: every column's header rect and body X-range, every visible row's body Y-range,
    /// and the body's viewport. It feeds <see cref="GenericPawnTableScope"/>'s geometry-scoped
    /// captured-extras router — an extra whose rect center falls inside a column X-range AND a row
    /// Y-range is that cell's extra — and every pawn-table scope's pointer routing through
    /// <see cref="AddRouteCandidates"/>.
    ///
    /// Read-only, mirroring vanilla's own <c>PawnTableOnGUI</c> math through two Harmony taps.
    /// The prefix on <see cref="PawnTable.PawnTableOnGUI"/> — a plain non-virtual method every
    /// mod's table draw funnels through — computes the header rects in screen space immediately
    /// and the body's local, pre-scroll ranges for the second tap. The postfix on the same
    /// <see cref="Widgets.BeginScrollView"/> overload the body uses fires the instant after
    /// vanilla's scroll view pushes its clip, so <see cref="GuiSpace.ToScreen"/> resolves a
    /// body-local rect to the same absolute rect the real per-cell widgets get, with no need to
    /// reproduce BeginScrollView's offset math. A one-shot armed flag keeps it reacting only to the
    /// table's own next scroll view.
    ///
    /// Gated to one table at a time via <see cref="SetActiveTable"/>; only one pawn-table tab is
    /// ever open. Any failure clears state and degrades to "no geometry this frame": callers treat
    /// a missing rect as "route this extra to the flat region", never as license to guess a cell.
    /// </summary>
    internal static class PawnTableExtrasRouter
    {
        private static readonly AccessTools.FieldRef<PawnTable, List<float>> columnWidthsField =
            AccessTools.FieldRefAccess<PawnTable, List<float>>("cachedColumnWidths");
        private static readonly AccessTools.FieldRef<PawnTable, List<float>> rowHeightsField =
            AccessTools.FieldRefAccess<PawnTable, List<float>>("cachedRowHeights");

        private static PawnTable activeTable;
        private static readonly Dictionary<PawnColumnDef, Rect> headerScreenRects = new Dictionary<PawnColumnDef, Rect>();
        private static readonly Dictionary<PawnColumnDef, Vector2> columnScreenXRange = new Dictionary<PawnColumnDef, Vector2>();
        private static readonly Dictionary<Pawn, Vector2> rowScreenYRange = new Dictionary<Pawn, Vector2>();

        // The clip contexts the two halves draw under: headers outside the scroll view, body cells
        // inside it. Kept for pointer routing, whose hit test breaks ties on clip depth.
        private static int headerClipDepth;
        private static Rect bodyViewportScreenRect;
        private static int bodyClipDepth;

        // Armed by the PawnTableOnGUI prefix, consumed by the very next BeginScrollView postfix.
        private static bool bodyArmed;
        private static (PawnColumnDef def, float xMin, float width)[] pendingColumns;
        private static (Pawn pawn, float yMin, float height)[] pendingRows;

        private const int MaxLoggedErrors = 5;
        private static int loggedErrors;

        /// <summary>Called by GenericPawnTableScope on every refresh; at most one table's geometry is tracked at a time.</summary>
        internal static void SetActiveTable(PawnTable table)
        {
            if (ReferenceEquals(activeTable, table))
            {
                return;
            }
            activeTable = table;
            ClearComputed();
        }

        private static void ClearComputed()
        {
            headerScreenRects.Clear();
            columnScreenXRange.Clear();
            rowScreenYRange.Clear();
            bodyViewportScreenRect = default(Rect);
            bodyArmed = false;
            pendingColumns = null;
            pendingRows = null;
        }

        /// <summary>The active table's column header screen rect, or false when geometry wasn't computed this frame.</summary>
        internal static bool TryGetHeaderRect(PawnColumnDef def, out Rect rect)
        {
            return headerScreenRects.TryGetValue(def, out rect);
        }

        /// <summary>
        /// The index into <paramref name="candidates"/> whose body X-range contains
        /// <paramref name="screenX"/>, or -1 when geometry is unavailable or nothing matches.
        /// Callers pass their own column list rather than the router exposing its internal map, so
        /// a match is always scoped to columns the caller cares about.
        /// </summary>
        internal static int FindColumnAt(IReadOnlyList<PawnColumnDef> candidates, float screenX)
        {
            for (int i = 0; i < candidates.Count; i++)
            {
                Vector2 range;
                if (columnScreenXRange.TryGetValue(candidates[i], out range) && screenX >= range.x && screenX <= range.y)
                {
                    return i;
                }
            }
            return -1;
        }

        /// <summary>The row-list index whose body Y-range contains <paramref name="screenY"/>, or -1. See <see cref="FindColumnAt"/>'s remarks.</summary>
        internal static int FindRowAt(IReadOnlyList<Pawn> candidates, float screenY)
        {
            for (int k = 0; k < candidates.Count; k++)
            {
                Vector2 range;
                if (rowScreenYRange.TryGetValue(candidates[k], out range) && screenY >= range.x && screenY <= range.y)
                {
                    return k;
                }
            }
            return -1;
        }

        /// <summary>
        /// Appends one pointer-routing candidate per header cell and per visible body cell of the
        /// active table, aimed at content region <paramref name="region"/>. A table region carries
        /// its header as model row 0, so body row <c>r</c> is index <c>r + 1</c>. Columns
        /// <paramref name="columnAt"/> maps to null contribute nothing.
        /// </summary>
        internal static void AddRouteCandidates(int region, IReadOnlyList<Pawn> rows, int columnCount,
            Func<int, PawnColumnDef> columnAt, List<PointerHitCandidate> candidates, List<ScreenScope.RouteTarget> targets)
        {
            for (int c = 0; c < columnCount; c++)
            {
                PawnColumnDef def = columnAt(c);
                Rect header;
                if (def == null || !headerScreenRects.TryGetValue(def, out header))
                {
                    continue;
                }
                candidates.Add(new PointerHitCandidate { Primary = header, ClipDepth = headerClipDepth });
                targets.Add(new ScreenScope.RouteTarget { Region = region, Index = 0, Column = c });
            }
            if (bodyViewportScreenRect.width <= 0f || bodyViewportScreenRect.height <= 0f)
            {
                return;
            }
            for (int c = 0; c < columnCount; c++)
            {
                PawnColumnDef def = columnAt(c);
                Vector2 xRange;
                if (def == null || !columnScreenXRange.TryGetValue(def, out xRange))
                {
                    continue;
                }
                for (int r = 0; r < rows.Count; r++)
                {
                    Vector2 yRange;
                    if (!rowScreenYRange.TryGetValue(rows[r], out yRange))
                    {
                        continue;
                    }
                    Rect cell = Rect.MinMaxRect(
                        Mathf.Max(xRange.x, bodyViewportScreenRect.xMin),
                        Mathf.Max(yRange.x, bodyViewportScreenRect.yMin),
                        Mathf.Min(xRange.y, bodyViewportScreenRect.xMax),
                        Mathf.Min(yRange.y, bodyViewportScreenRect.yMax));
                    if (cell.width <= 0f || cell.height <= 0f)
                    {
                        continue;
                    }
                    candidates.Add(new PointerHitCandidate { Primary = cell, ClipDepth = bodyClipDepth });
                    targets.Add(new ScreenScope.RouteTarget { Region = region, Index = r + 1, Column = c });
                }
            }
        }

        // Harmony tap bodies; the attributed patch classes below forward here.

        internal static void HandlePawnTableOnGuiPrefix(PawnTable instance, Vector2 position)
        {
            try
            {
                if (!ReferenceEquals(instance, activeTable))
                {
                    return;
                }
                if (Event.current == null || Event.current.type == EventType.Layout)
                {
                    // Vanilla itself draws nothing this pass: no headers, no scroll view.
                    return;
                }

                List<PawnColumnDef> cols = instance.Columns;
                float headerHeight = instance.HeaderHeight;
                Vector2 size = instance.Size; // triggers RecacheIfDirty for the fields read below.
                List<Pawn> pawns = instance.PawnsListForReading;
                List<float> colWidths = columnWidthsField(instance);
                List<float> rowHeights = rowHeightsField(instance);

                if (cols.Count != colWidths.Count || pawns.Count != rowHeights.Count)
                {
                    // The cached geometry arrays do not match this frame's column/row set — bail
                    // rather than index out of range or attach an extra to the wrong cell.
                    ClearComputed();
                    return;
                }

                headerScreenRects.Clear();
                float totalWidth = size.x - 16f;
                float x = 0f;
                var localColumns = new (PawnColumnDef def, float xMin, float width)[cols.Count];
                for (int i = 0; i < cols.Count; i++)
                {
                    float w = (i != cols.Count - 1) ? (int)colWidths[i] : (int)(totalWidth - x);
                    Rect localHeaderRect = new Rect((int)position.x + x, (int)position.y, w, (int)headerHeight);
                    headerScreenRects[cols[i]] = GuiSpace.ToScreen(localHeaderRect);
                    headerClipDepth = GuiSpace.CurrentClip().Depth;
                    localColumns[i] = (cols[i], x, w);
                    x += w;
                }

                float y = 0f;
                var localRows = new (Pawn pawn, float yMin, float height)[pawns.Count];
                for (int k = 0; k < pawns.Count; k++)
                {
                    float h = (int)rowHeights[k];
                    localRows[k] = (pawns[k], y, h);
                    y += h;
                }

                pendingColumns = localColumns;
                pendingRows = localRows;
                bodyArmed = true;
            }
            catch (Exception ex)
            {
                ClearComputed();
                LogLimited(ex);
            }
        }

        internal static void HandleBeginScrollViewPostfix()
        {
            if (!bodyArmed)
            {
                return;
            }
            bodyArmed = false;
            var cols = pendingColumns;
            var rows = pendingRows;
            pendingColumns = null;
            pendingRows = null;
            if (cols == null || rows == null)
            {
                return;
            }
            try
            {
                columnScreenXRange.Clear();
                rowScreenYRange.Clear();
                GuiSpace.ClipKey clip = GuiSpace.CurrentClip();
                // The scroll view's own clip IS the body's viewport, so a row scrolled out of it
                // can be excluded without reproducing the scroll math.
                bodyViewportScreenRect = GuiSpace.ToScreen(clip.VisibleRect);
                bodyClipDepth = clip.Depth;
                for (int i = 0; i < cols.Length; i++)
                {
                    Rect screen = GuiSpace.ToScreen(new Rect(cols[i].xMin, 0f, cols[i].width, 1f));
                    columnScreenXRange[cols[i].def] = new Vector2(screen.xMin, screen.xMax);
                }
                for (int k = 0; k < rows.Length; k++)
                {
                    Rect screen = GuiSpace.ToScreen(new Rect(0f, rows[k].yMin, 1f, rows[k].height));
                    rowScreenYRange[rows[k].pawn] = new Vector2(screen.yMin, screen.yMax);
                }
            }
            catch (Exception ex)
            {
                columnScreenXRange.Clear();
                rowScreenYRange.Clear();
                bodyViewportScreenRect = default(Rect);
                LogLimited(ex);
            }
        }

        private static void LogLimited(Exception ex)
        {
            if (loggedErrors >= MaxLoggedErrors)
            {
                return;
            }
            loggedErrors++;
            Log.Warning("[RimWorld Access] Pawn-table cell geometry failed; captured extras fall back to the flat region this frame. " + ex);
        }
    }

    /// <summary>Header half of <see cref="PawnTableExtrasRouter"/>'s geometry — see its class remarks.</summary>
    [HarmonyPatch(typeof(PawnTable), nameof(PawnTable.PawnTableOnGUI))]
    internal static class PawnTableHeaderGeometryPatch
    {
        [HarmonyPrefix]
        public static void Prefix(PawnTable __instance, Vector2 position)
        {
            PawnTableExtrasRouter.HandlePawnTableOnGuiPrefix(__instance, position);
        }
    }

    /// <summary>
    /// Body half of <see cref="PawnTableExtrasRouter"/>'s geometry. TargetMethod because the by-ref
    /// Vector2 parameter type is not a compile-time constant (CS0182).
    /// </summary>
    [HarmonyPatch]
    internal static class PawnTableBodyGeometryPatch
    {
        static MethodBase TargetMethod()
        {
            return AccessTools.Method(typeof(Widgets), "BeginScrollView",
                new Type[] { typeof(Rect), typeof(Vector2).MakeByRefType(), typeof(Rect), typeof(bool) });
        }

        [HarmonyPostfix]
        public static void Postfix()
        {
            PawnTableExtrasRouter.HandleBeginScrollViewPostfix();
        }
    }
}
