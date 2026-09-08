using System;
using System.Collections.Generic;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;

namespace RimWorldAccess
{
    /// <summary>
    /// Speaks what a mouse click on the map selected, and moves the keyboard cursor to the
    /// clicked cell so arrow navigation continues from there.
    ///
    /// Pure observation. Bracketing vanilla's own click handler is what makes origination
    /// structural: any selection change seen inside the bracket came from a real click, so our
    /// own keyboard selection call sites (which speak for themselves) need no suppression flag.
    /// </summary>
    [HarmonyPatch(typeof(Selector), "HandleMapClicks")]
    internal static class MapClickAnnouncementPatch
    {
        private static readonly List<object> selectionBefore = new List<object>();
        private static bool armed;
        private static IntVec3 clickedCell;

        [HarmonyPrefix]
        public static void Prefix(Selector __instance)
        {
            armed = false;
            try
            {
                Event ev = Event.current;
                if (__instance == null || ev == null)
                    return;
                // Vanilla changes the selection on MouseUp (drag box or click) and on the
                // double-click MouseDown; every other event leaves it alone.
                bool canChangeSelection =
                    (ev.rawType == EventType.MouseUp && ev.button == 0)
                    || (ev.type == EventType.MouseDown && ev.button == 0 && ev.clickCount >= 2);
                if (!canChangeSelection)
                    return;

                selectionBefore.Clear();
                selectionBefore.AddRange(__instance.SelectedObjects);
                clickedCell = UI.MouseCell();
                armed = true;
            }
            catch (Exception ex)
            {
                armed = false;
                ModLogger.LimitedError("Map click announcement prefix error", ex);
            }
        }

        /// <summary>
        /// A finalizer, not a postfix: an exception escaping vanilla's body must still clear the
        /// arm and release the snapshot. The game's exception is returned unchanged.
        /// </summary>
        [HarmonyFinalizer]
        public static Exception Finalizer(Selector __instance, Exception __exception)
        {
            if (!armed)
                return __exception;
            armed = false;
            try
            {
                if (__exception == null && __instance != null)
                {
                    MoveCursor();
                    AnnounceIfChanged(__instance);
                }
            }
            catch (Exception ex)
            {
                ModLogger.LimitedError("Map click announcement error", ex);
            }
            selectionBefore.Clear();
            return __exception;
        }

        private static void MoveCursor()
        {
            Map map = Find.CurrentMap;
            if (map != null && clickedCell.InBounds(map))
                MapNavigationState.CurrentCursorPosition = clickedCell;
        }

        private static void AnnounceIfChanged(Selector selector)
        {
            List<object> now = selector.SelectedObjects;
            if (now == null || Unchanged(now))
                return;

            string text = MapSelectionAnnouncer.DescribeSelection(now);
            if (!string.IsNullOrEmpty(text))
                TolkHelper.SpeakData(text);
        }

        private static bool Unchanged(List<object> now)
        {
            if (now.Count != selectionBefore.Count)
                return false;
            for (int i = 0; i < now.Count; i++)
            {
                if (!ReferenceEquals(now[i], selectionBefore[i]))
                    return false;
            }
            return true;
        }
    }
}
