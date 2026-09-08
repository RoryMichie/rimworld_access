using System;
using HarmonyLib;
using RimWorld;
using UnityEngine;

namespace RimWorldAccess.Shell
{
    /// <summary>
    /// Captures the sellable-items scroll view's outer geometry so <see cref="SellableItemsScope"/>
    /// can compute a row's rect by arithmetic, with no tap on the row-drawing widget itself.
    ///
    /// <c>Dialog_SellableItems.DoWindowContents</c> (decompiled RimWorld/Dialog_SellableItems.cs:81-109)
    /// opens one <c>BeginGroup(inRect)</c>, then calls <c>DoBottomButtons(rect2)</c> (:83) BEFORE
    /// <c>BeginScrollView</c> — the prefix runs inside that same group, so converting rect2's
    /// group-space origin to screen space gives the scroll view's own outRect origin (:84-92).
    /// rect2.height − 65f equals outRect.height (:85, DoBottomButtons receives the full group rect
    /// while outRect trims 65f for the buttons below it); outRect.width − 16f is the view width
    /// vanilla itself subtracts for the scrollbar (:91).
    /// </summary>
    [HarmonyPatch(typeof(Dialog_SellableItems), "DoBottomButtons")]
    internal static class SellableItemsDrawPatch
    {
        private static int recordedFrame = -1;
        private static Vector2 outRectOrigin;
        private static float outRectWidth;
        private static float outRectHeight;

        [HarmonyPrefix]
        public static void Prefix(Rect rect)
        {
            if (IsLayoutPass() || !(FocusStack.Top is SellableItemsScope))
            {
                return;
            }
            try
            {
                outRectHeight = rect.height - 65f;
                outRectWidth = rect.width - 16f;
                outRectOrigin = GuiSpace.ToScreen(new Rect(0f, 0f, outRectWidth, 0f)).position;
                recordedFrame = Time.frameCount;
            }
            catch (Exception ex)
            {
                ModLogger.LimitedError("Sellable items geometry capture error", ex);
            }
        }

        private static bool IsLayoutPass()
        {
            return Event.current != null && Event.current.type == EventType.Layout;
        }

        private static bool Fresh()
        {
            return recordedFrame >= 0 && Time.frameCount - recordedFrame <= 1;
        }

        /// <summary>The outer scroll view's height (outRect.height), or -1 when nothing was captured recently.</summary>
        internal static float OutRectHeight()
        {
            return Fresh() ? outRectHeight : -1f;
        }

        /// <summary>
        /// Row <paramref name="ord"/>'s absolute rect at the live scroll offset
        /// <paramref name="scrollY"/>, clipped to the captured outRect band, or empty when stale
        /// or scrolled fully out of view.
        /// </summary>
        internal static Rect RowRect(int ord, float scrollY)
        {
            if (!Fresh() || ord < 0)
            {
                return default(Rect);
            }
            float yMin = Mathf.Max(outRectOrigin.y + ord * 24f - scrollY, outRectOrigin.y);
            float yMax = Mathf.Min(outRectOrigin.y + ord * 24f - scrollY + 24f, outRectOrigin.y + outRectHeight);
            if (yMax <= yMin)
            {
                return default(Rect);
            }
            return new Rect(outRectOrigin.x, yMin, outRectWidth, yMax - yMin);
        }
    }
}
