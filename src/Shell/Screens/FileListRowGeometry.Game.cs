using System;
using System.Reflection;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;

namespace RimWorldAccess.Shell
{
    /// <summary>
    /// Dialog_FileList's own row layout (decompiled RimWorld/Dialog_FileList.cs:66-143),
    /// shared by every scope that paints a focus ring over one of its lists — the save/load
    /// menus and the saved-ideoligion picker, which is a Dialog_FileList too. Kept in one
    /// place so the ring and the auto-scroll land on the exact pixels vanilla draws.
    ///
    /// <see cref="EntryHeight"/> is the one named vanilla constant in the formula, so it is
    /// read by reflection with a literal fallback; the rest (search-bar height, close-button
    /// gap, the save-mode field reservation) are hardcoded exactly as vanilla itself hardcodes
    /// them at the cited lines — a future vanilla layout change to those literals would need a
    /// matching update here.
    /// </summary>
    internal static class FileListRowGeometry
    {
        internal static readonly float EntryHeight = ResolveEntryHeight();

        private const float SearchBarHeightPlusGap = 34f; // :73 rect.height=24f, :81 "+10f"
        private const float BottomGap = 10f; // :82 "+10f" (bottomAreaHeight is 0 for every subclass)
        private const float SaveFieldReservation = 53f; // :85, ShouldDoTypeInField only

        private static float ResolveEntryHeight()
        {
            try
            {
                FieldInfo field = AccessTools.Field(typeof(Dialog_FileList), "EntryHeight");
                object value = field == null ? null : field.GetValue(null);
                if (value != null)
                {
                    return (float)value;
                }
            }
            catch (Exception ex)
            {
                Log.Error("[RimWorld Access] FileListRowGeometry: failed to read Dialog_FileList.EntryHeight: " + ex);
            }
            return 40f;
        }

        /// <summary>The scroll view's outer rect. <paramref name="reservesTypeInField"/> is the dialog's own ShouldDoTypeInField (save mode).</summary>
        internal static Rect OuterRect(Rect inRect, bool reservesTypeInField)
        {
            Rect outRect = inRect;
            outRect.yMin = inRect.y + SearchBarHeightPlusGap;
            outRect.yMax -= Window.CloseButSize.y + BottomGap;
            if (reservesTypeInField)
            {
                outRect.yMax -= SaveFieldReservation;
            }
            return outRect;
        }

        /// <summary><paramref name="index"/> counts the rows vanilla actually draws — those its own search filter keeps.</summary>
        internal static bool TryGetRowRect(Rect outRect, Vector2 scroll, int index, int visibleCount, out Rect rect)
        {
            if (index < 0 || index >= visibleCount)
            {
                rect = default(Rect);
                return false;
            }
            float rowWidth = outRect.width - 16f; // vector.x, decompiled :68
            rect = new Rect(outRect.x, outRect.y + (index * EntryHeight - scroll.y), rowWidth, EntryHeight);
            return true;
        }

        /// <summary>
        /// The scrolled-band test a ring must apply itself: vanilla's own BeginScrollView clips
        /// the real rows, but nothing clips a ring drawn from a DoWindowContents postfix, which
        /// runs after EndScrollView.
        /// </summary>
        internal static bool WithinBand(Rect rowRect, Rect outRect)
        {
            return rowRect.yMax > outRect.y && rowRect.y < outRect.yMax;
        }
    }
}
