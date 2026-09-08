using System.Collections.Generic;
using UnityEngine;
using Verse;

namespace RimWorldAccess.Shell
{
    /// <summary>
    /// Vanilla's own float-menu sizing arithmetic, transcribed from decompiled Verse/FloatMenu.cs so
    /// the twin's container matches a real float menu without instantiating one (which would play
    /// SoundDefOf.FloatMenu_Open and be intercepted by DialogInterceptionPatch). This is transcription,
    /// not estimation: there is no vanilla-drawn artefact to align with, because the twin owns its own
    /// container and every pixel inside a row is painted by FloatMenuOption.DoGUI.
    /// </summary>
    internal struct FloatMenuTwinLayout
    {
        internal FloatMenuSizeMode SizeMode;
        internal float ColumnWidth;
        internal int ColumnCount;
        internal bool UsingScrollbar;
        internal float MaxViewHeight;
        internal float TotalViewHeight;
        internal float TotalWidth;
        internal float TotalWindowHeight;

        internal static FloatMenuTwinLayout Compute(List<FloatMenuOption> options)
        {
            FloatMenuTwinLayout layout = default(FloatMenuTwinLayout);

            // Decompiled Verse/FloatMenu.cs:48.
            float maxWindowHeight = (float)UI.screenHeight * 0.9f;

            // Decompiled :173-183.
            layout.SizeMode = options.Count > 60 ? FloatMenuSizeMode.Tiny : FloatMenuSizeMode.Normal;

            // ColumnWidth. Decompiled :117-136.
            float columnWidth = 70f;
            for (int i = 0; i < options.Count; i++)
            {
                float rw = options[i].RequiredWidth;
                if (rw >= 300f)
                {
                    columnWidth = 300f;
                    break;
                }
                if (rw > columnWidth)
                {
                    columnWidth = rw;
                }
            }
            layout.ColumnWidth = Mathf.Round(columnWidth);

            // ColumnCountIfNoScrollbar. Decompiled :144-171. Vanilla sets
            // Text.Font first because RequiredHeight was cached under that font.
            Text.Font = GameFont.Small;
            int columnCountIfNoScrollbar = 1;
            float rowY = 0f;
            for (int i = 0; i < options.Count; i++)
            {
                float rh = options[i].RequiredHeight;
                if (rowY + rh + -1f > maxWindowHeight)
                {
                    rowY = rh;
                    columnCountIfNoScrollbar++;
                }
                else
                {
                    rowY += rh + -1f;
                }
            }

            // MaxColumns. Decompiled :138. Mathf.Max(1, ...) is a deliberate
            // deviation from vanilla: at a degenerate resolution vanilla can
            // divide by a zero column count, and we must not.
            int maxColumns = Mathf.Max(1, Mathf.FloorToInt(((float)UI.screenWidth - 16f) / layout.ColumnWidth));

            // Decompiled :140, :142.
            layout.UsingScrollbar = columnCountIfNoScrollbar > maxColumns;
            layout.ColumnCount = Mathf.Min(columnCountIfNoScrollbar, maxColumns);

            // MaxViewHeight. Decompiled :52-75.
            if (layout.UsingScrollbar)
            {
                float tallestRow = 0f;
                float sumHeights = 0f;
                for (int i = 0; i < options.Count; i++)
                {
                    float rh = options[i].RequiredHeight;
                    if (rh > tallestRow)
                    {
                        tallestRow = rh;
                    }
                    sumHeights += rh + -1f;
                }
                sumHeights += (float)layout.ColumnCount * tallestRow;
                layout.MaxViewHeight = sumHeights / (float)layout.ColumnCount;
            }
            else
            {
                layout.MaxViewHeight = maxWindowHeight;
            }

            // TotalViewHeight. Decompiled :77-102.
            float tallestColumn = 0f;
            float columnHeight = 0f;
            for (int i = 0; i < options.Count; i++)
            {
                float rh = options[i].RequiredHeight;
                if (columnHeight + rh + -1f > layout.MaxViewHeight)
                {
                    if (columnHeight > tallestColumn)
                    {
                        tallestColumn = columnHeight;
                    }
                    columnHeight = rh;
                }
                else
                {
                    columnHeight += rh + -1f;
                }
            }
            layout.TotalViewHeight = Mathf.Max(tallestColumn, columnHeight);

            // TotalWidth. Decompiled :104-115.
            layout.TotalWidth = (float)layout.ColumnCount * layout.ColumnWidth + (layout.UsingScrollbar ? 16f : 0f);

            // TotalWindowHeight. Decompiled :50.
            layout.TotalWindowHeight = Mathf.Min(layout.TotalViewHeight, maxWindowHeight) + 1f;

            return layout;
        }
    }
}
