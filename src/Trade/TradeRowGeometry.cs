using System;
using System.Collections.Generic;
using HarmonyLib;
using RimWorld;
using RimWorldAccess.Shell;
using UnityEngine;
using Verse;

namespace RimWorldAccess
{
    /// <summary>
    /// Dialog_Trade's own row layout, shared by both trade views for the focus ring and the
    /// auto-scroll: DoWindowContents pins the currency row 30 points above the list
    /// (decompiled RimWorld/Dialog_Trade.cs, rect5) and FillMainRect lays the list out at 6 + 30 per
    /// row inside mainRect. The FillMainRect prefix runs inside the dialog's own group, so
    /// converting mainRect there gives its screen origin without re-deriving the window margins.
    /// </summary>
    [HarmonyPatch(typeof(Dialog_Trade), "FillMainRect")]
    internal static class TradeRowGeometry
    {
        private const float RowHeight = 30f;
        private const float FirstRowY = 6f;
        private const float ScrollbarWidth = 16f;

        private static readonly AccessTools.FieldRef<Dialog_Trade, Vector2> scrollPositionField =
            AccessTools.FieldRefAccess<Dialog_Trade, Vector2>("scrollPosition");
        private static readonly AccessTools.FieldRef<Dialog_Trade, List<Tradeable>> cachedTradeablesField =
            AccessTools.FieldRefAccess<Dialog_Trade, List<Tradeable>>("cachedTradeables");
        private static readonly AccessTools.FieldRef<Dialog_Trade, Tradeable> cachedCurrencyField =
            AccessTools.FieldRefAccess<Dialog_Trade, Tradeable>("cachedCurrencyTradeable");

        private static int recordedFrame = -1;
        private static Rect mainRectScreen;

        [HarmonyPrefix]
        public static void Prefix(Rect mainRect)
        {
            if (Event.current != null && Event.current.type == EventType.Layout)
            {
                return;
            }
            try
            {
                mainRectScreen = GuiSpace.ToScreen(mainRect);
                recordedFrame = Time.frameCount;
            }
            catch (Exception ex)
            {
                ModLogger.LimitedError("Trade row geometry capture error", ex);
            }
        }

        private static bool Fresh()
        {
            return recordedFrame >= 0 && Time.frameCount - recordedFrame <= 1;
        }

        /// <summary>The row's index in vanilla's own list (the dialog's cachedTradeables), or -1 for the pinned currency row and unknown rows.</summary>
        internal static int Ordinal(Dialog_Trade dialog, Tradeable t)
        {
            List<Tradeable> list = dialog == null ? null : cachedTradeablesField(dialog);
            return list == null || t == null ? -1 : list.IndexOf(t);
        }

        /// <summary>
        /// The row's absolute rect, clipped to the scroll view; empty when no draw pass has
        /// recorded the view yet, the row is unknown, or it is scrolled out. The currency row is
        /// pinned above the list and never scrolls.
        /// </summary>
        internal static Rect RowRect(Dialog_Trade dialog, Tradeable t)
        {
            if (!Fresh() || dialog == null || t == null)
            {
                return default(Rect);
            }
            float width = mainRectScreen.width - ScrollbarWidth;
            if (ReferenceEquals(t, cachedCurrencyField(dialog)))
            {
                return new Rect(mainRectScreen.x, mainRectScreen.y - RowHeight, width, RowHeight);
            }
            int ord = Ordinal(dialog, t);
            if (ord < 0)
            {
                return default(Rect);
            }
            float top = mainRectScreen.y + FirstRowY + ord * RowHeight - scrollPositionField(dialog).y;
            float yMin = Mathf.Max(top, mainRectScreen.y);
            float yMax = Mathf.Min(top + RowHeight, mainRectScreen.yMax);
            if (yMax <= yMin)
            {
                return default(Rect);
            }
            return new Rect(mainRectScreen.x, yMin, width, yMax - yMin);
        }

        /// <summary>
        /// Writes the dialog's scroll offset so the row sits fully inside the scroll view. A no-op
        /// before the first draw pass; the next cursor settle self-heals.
        /// </summary>
        internal static void ScrollIntoView(Dialog_Trade dialog, Tradeable t)
        {
            if (!Fresh())
            {
                return;
            }
            int ord = Ordinal(dialog, t);
            if (ord < 0)
            {
                return;
            }
            float rowTop = FirstRowY + ord * RowHeight;
            Vector2 scroll = scrollPositionField(dialog);
            scroll.y = Mathf.Max(0f, Mathf.Clamp(scroll.y, rowTop + RowHeight - mainRectScreen.height, rowTop));
            scrollPositionField(dialog) = scroll;
        }
    }
}
