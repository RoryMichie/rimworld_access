using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using LudeonTK;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.Sound;

namespace RimWorldAccess.Shell
{
    /// <summary>
    /// Focus scope for the dev-mode data table (<see cref="LudeonTK.Window_DebugTable"/>), one content
    /// region on the <see cref="ScreenScope"/> table contract fed by the window's <c>tableSorted</c>
    /// grid. Every column is presented, including ones vanilla's right-click <c>colVisible</c> toggle
    /// hides; <c>rowsVisible</c> search filtering is ignored for presentation. The window force-focuses
    /// its search box on open, stealing every keystroke; <see cref="OnPush"/> clears that latch first.
    /// </summary>
    internal sealed class DevTableScope : ScreenScope
    {
        private const int TableRegion = 0;

        // SortMode is private on Window_DebugTable; index its values by declaration order.
        private const int SortModeOff = 0;
        private const int SortModeAscending = 1;
        private const int SortModeDescending = 2;

        private static readonly AccessTools.FieldRef<Window_DebugTable, string[,]> TableSortedRef =
            AccessTools.FieldRefAccess<Window_DebugTable, string[,]>("tableSorted");
        private static readonly AccessTools.FieldRef<Window_DebugTable, int> SortColumnRef =
            AccessTools.FieldRefAccess<Window_DebugTable, int>("sortColumn");
        private static readonly AccessTools.FieldRef<Window_DebugTable, bool> FocusFilterRef =
            AccessTools.FieldRefAccess<Window_DebugTable, bool>("focusFilter");
        private static readonly FieldInfo SortModeField =
            AccessTools.Field(typeof(Window_DebugTable), "sortMode");
        private static readonly MethodInfo BuildTableSortedMethod =
            AccessTools.Method(typeof(Window_DebugTable), "BuildTableSorted");
        private static readonly MethodInfo CopyCsvMethod =
            AccessTools.Method(typeof(Window_DebugTable), "CopyCSVToClipboard");
        private static readonly AccessTools.FieldRef<Window_DebugTable, List<float>> ColWidthsRef =
            AccessTools.FieldRefAccess<Window_DebugTable, List<float>>("colWidths");
        private static readonly AccessTools.FieldRef<Window_DebugTable, List<float>> RowHeightsRef =
            AccessTools.FieldRefAccess<Window_DebugTable, List<float>>("rowHeights");
        private static readonly AccessTools.FieldRef<Window_DebugTable, bool[]> RowsVisibleRef =
            AccessTools.FieldRefAccess<Window_DebugTable, bool[]>("rowsVisible");
        private static readonly AccessTools.FieldRef<Window_DebugTable, Vector2> ScrollPositionRef =
            AccessTools.FieldRefAccess<Window_DebugTable, Vector2>("scrollPosition");
        private static readonly Func<Window, float> MarginOf =
            AccessTools.MethodDelegate<Func<Window, float>>(AccessTools.PropertyGetter(typeof(Window), "Margin"));

        private readonly Window_DebugTable window;
        private string[,] sorted;
        private bool announcedOpen;

        public DevTableScope(Window_DebugTable window)
        {
            this.window = window;
        }

        public override string Name => "dev-table";

        protected override bool EnableTypeahead => true;

        /// <summary>Window_DebugTable draws its grid via DevGUI, not Widgets.ButtonText, so there is nothing to scrape.</summary>
        protected override bool CaptureWindowButtons => false;

        public override void OnPush()
        {
            base.OnPush();

            // MUTATION-C: mirrors Window_DebugTable's one-shot focusFilter latch.
            // No public accessor exists; this is UI focus state, not game state.
            FocusFilterRef(window) = false;
            UI.UnfocusCurrentControl();
        }

        public override void OnFocus()
        {
            base.OnFocus();
            if (!announcedOpen)
            {
                announcedOpen = true;
                AnnounceRegion();
                return;
            }
            AnnounceCurrentItem();
        }

        protected override int ContentRegionCount => 1;

        protected override string ContentRegionName(int region)
        {
            return "RimWorldAccess.Dev.TableRegion".Translate().ToString();
        }

        protected override int ContentColumnCount(int region)
        {
            return sorted != null ? sorted.GetLength(0) : 0;
        }

        /// <summary>Data rows only; the grid's row 0 is the header row the base draws itself.</summary>
        protected override int ContentItemCount(int region)
        {
            return sorted != null ? Math.Max(0, sorted.GetLength(1) - 1) : 0;
        }

        /// <summary>Re-snapshot the sorted grid each cycle — sorting rebuilds the array.</summary>
        protected override void RefreshContent()
        {
            sorted = TableSortedRef(window);
        }

        protected override TableColumnInfo ContentColumnInfo(int region, int column)
        {
            string header = CellAt(column, 0);
            return new TableColumnInfo(header, null, sortable: true);
        }

        protected override ElementDescription DescribeContentItem(int region, int index)
        {
            var d = new ElementDescription();
            d.Label = CellAt(0, index + 1);
            return d;
        }

        protected override string ContentCellText(int region, int row, int column)
        {
            return CellAt(column, row + 1);
        }

        /// <summary>The grid is read-only: Enter on a data row just re-announces it.</summary>
        protected override void ActivateContentItem(int region, int index)
        {
            AnnounceCurrentItem();
        }

        /// <summary>Maps the shared model's sort cycle onto vanilla's private sort fields, plays the
        /// matching sound and rebuilds. Returns -1: string cells carry no identity to follow.</summary>
        protected override int ApplyContentSort(int region, int column, SortCycleResult cycle, int currentRow)
        {
            int newSortColumn;
            int newSortMode;
            SoundDef sound;
            switch (cycle)
            {
                case SortCycleResult.SortedDescending:
                    newSortColumn = column;
                    newSortMode = SortModeDescending;
                    sound = SoundDefOf.Tick_High;
                    break;
                case SortCycleResult.SortedAscending:
                    newSortColumn = column;
                    newSortMode = SortModeAscending;
                    sound = SoundDefOf.Tick_Low;
                    break;
                case SortCycleResult.Cleared:
                    newSortColumn = -1;
                    newSortMode = SortModeOff;
                    sound = SoundDefOf.Tick_Tiny;
                    break;
                default:
                    return -1;
            }

            // MUTATION-C: mirrors Window_DebugTable.DoWindowContents header-click
            // sort cycle (Off->Descending->Ascending->Off); the logic is inline in
            // the draw pass, no invocable vanilla method exists.
            SortColumnRef(window) = newSortColumn;
            SortModeField.SetValue(window, Enum.ToObject(SortModeField.FieldType, newSortMode));
            sound.PlayOneShotOnCamera();
            BuildTableSortedMethod.Invoke(window, null);
            sorted = TableSortedRef(window);
            return -1;
        }

        // Focus ring geometry: the grid is running sums of the window's cached colWidths/rowHeights,
        // header row pinned at the top, data rows in a scroll view below, hidden rows leaving no gap.

        private const float HeaderGap = 2f;
        private const float CopyButtonBand = 40f;

        /// <summary>The focused cell's band. A column hidden by vanilla's right-click toggle is 10f wide
        /// and unlabelled but still occupies the grid, so the cursor rings that sliver.</summary>
        protected internal override Rect FocusedContentRect()
        {
            ListModel region = Model.CurrentRegion;
            TableModel table = Model.CurrentTable;
            if (Model.RegionIndex != TableRegion || region == null || region.IsEmpty || table == null)
            {
                return default(Rect);
            }
            List<float> colWidths = ColWidthsRef(window);
            List<float> rowHeights = RowHeightsRef(window);
            bool[] rowsVisible = RowsVisibleRef(window);
            int column = table.ColumnIndex;
            int row = region.Index;
            if (colWidths == null || rowHeights == null || rowHeights.Count == 0
                || column < 0 || column >= colWidths.Count
                || row < 0 || row >= rowHeights.Count)
            {
                return default(Rect);
            }

            Rect inRect = WindowContentRect();
            Vector2 scroll = ScrollPositionRef(window);
            float x = inRect.x + PrefixSum(colWidths, column) - scroll.x;
            float width = colWidths[column];

            Rect cell;
            Rect band;
            if (row == 0)
            {
                band = new Rect(inRect.x, inRect.y, inRect.width, rowHeights[0]);
                cell = new Rect(x, inRect.y, width, rowHeights[0]);
            }
            else
            {
                if (rowsVisible != null && row < rowsVisible.Length && !rowsVisible[row])
                {
                    return default(Rect);
                }
                float bandY = inRect.y + rowHeights[0] + HeaderGap;
                band = new Rect(inRect.x, bandY, inRect.width,
                    inRect.height - CopyButtonBand - rowHeights[0] - HeaderGap);
                cell = new Rect(x, bandY + VisibleRowOffset(rowHeights, rowsVisible, row) - scroll.y,
                    width, rowHeights[row]);
            }

            float xMin = Mathf.Max(cell.xMin, band.xMin);
            float yMin = Mathf.Max(cell.yMin, band.yMin);
            float xMax = Mathf.Min(cell.xMax, band.xMax);
            float yMax = Mathf.Min(cell.yMax, band.yMax);
            if (xMax <= xMin || yMax <= yMin)
            {
                return default(Rect);
            }
            return GuiSpace.ToScreen(Rect.MinMaxRect(xMin, yMin, xMax, yMax));
        }

        /// <summary>The rect Window.InnerWindowOnGUI hands DoWindowContents, in window-local space. The
        /// contents group has closed when the ring draws, so it is recomputed, not read off the clip stack.</summary>
        private Rect WindowContentRect()
        {
            float margin = MarginOf(window);
            return new Rect(margin, margin,
                window.windowRect.width - margin * 2f,
                window.windowRect.height - margin * 2f);
        }

        internal static float PrefixSum(IList<float> widths, int column)
        {
            float sum = 0f;
            for (int i = 0; i < column && i < widths.Count; i++)
            {
                sum += widths[i];
            }
            return sum;
        }

        /// <summary>Where a data row starts inside the scroll view: heights of the visible data rows before it; row 0 is the pinned header.</summary>
        internal static float VisibleRowOffset(IList<float> heights, IList<bool> visible, int row)
        {
            float sum = 0f;
            for (int i = 1; i < row && i < heights.Count; i++)
            {
                if (visible == null || i >= visible.Count || visible[i])
                {
                    sum += heights[i];
                }
            }
            return sum;
        }

        protected override IReadOnlyList<ScreenAction> DeclaredActions =>
            new List<ScreenAction>
            {
                new ScreenAction("RimWorldAccess.Dev.CopyCsv".Translate().ToString(), CopyCsv)
            };

        private void CopyCsv()
        {
            // MUTATION-C: mirrors Window_DebugTable.DoWindowContents' inline
            // "Copy CSV" button handler — the window's own CopyCSVToClipboard,
            // then the same confirmation Message vanilla posts (the shell speaks
            // game messages, so no extra announcement). The handler is inline in
            // the draw pass with no invocable vanilla method, and the string is
            // vanilla's own untranslated dev-tool literal.
            CopyCsvMethod.Invoke(window, null);
            Messages.Message("Copied table data to clipboard in CSV format.", MessageTypeDefOf.PositiveEvent);
        }

        private string CellAt(int column, int row)
        {
            if (sorted == null || column < 0 || row < 0 ||
                column >= sorted.GetLength(0) || row >= sorted.GetLength(1))
            {
                return "";
            }
            return sorted[column, row] ?? "";
        }
    }
}
