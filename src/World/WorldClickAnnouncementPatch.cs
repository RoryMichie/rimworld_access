using System;
using System.Collections.Generic;
using HarmonyLib;
using RimWorld;
using RimWorld.Planet;
using UnityEngine;
using Verse;

namespace RimWorldAccess
{
    /// <summary>
    /// Speaks what a mouse click or drag-box release on the planet view selected, and moves the
    /// keyboard cursor to the resulting tile so arrow navigation continues from there. The world
    /// twin of <see cref="MapClickAnnouncementPatch"/>.
    ///
    /// Pure observation. Bracketing vanilla's own click handler is what makes origination
    /// structural: any selection change seen inside the bracket came from a real click, so our
    /// own keyboard selection call sites (which speak for themselves) need no suppression flag.
    /// </summary>
    [HarmonyPatch(typeof(WorldSelector), "HandleWorldClicks")]
    internal static class WorldClickAnnouncementPatch
    {
        private static readonly List<WorldObject> selectionBefore = new List<WorldObject>();
        private static bool armed;
        private static PlanetTile tileBefore;
        private static PlanetTile clickedTile;

        [HarmonyPrefix]
        public static void Prefix(WorldSelector __instance)
        {
            armed = false;
            try
            {
                Event ev = Event.current;
                if (__instance == null || ev == null)
                    return;
                // Vanilla changes the selection on the MouseUp that ends an active drag box (either
                // SelectUnderMouse or SelectInsideDragBox) and on the double-click MouseDown; the
                // right-button branch only issues caravan orders.
                bool canChangeSelection =
                    (ev.rawType == EventType.MouseUp && ev.button == 0 && __instance.dragBox.active)
                    || (ev.type == EventType.MouseDown && ev.button == 0 && ev.clickCount >= 2);
                if (!canChangeSelection)
                    return;

                selectionBefore.Clear();
                selectionBefore.AddRange(__instance.SelectedObjects);
                tileBefore = __instance.SelectedTile;
                clickedTile = GenWorld.MouseTile();
                armed = true;
            }
            catch (Exception ex)
            {
                armed = false;
                ModLogger.LimitedError("World click announcement prefix error", ex);
            }
        }

        /// <summary>
        /// A finalizer, not a postfix: an exception escaping vanilla's body must still clear the
        /// arm and release the snapshot. The game's exception is returned unchanged.
        /// </summary>
        [HarmonyFinalizer]
        public static Exception Finalizer(WorldSelector __instance, Exception __exception)
        {
            if (!armed)
                return __exception;
            armed = false;
            try
            {
                if (__exception == null && __instance != null)
                    AnnounceIfChanged(__instance);
            }
            catch (Exception ex)
            {
                ModLogger.LimitedError("World click announcement error", ex);
            }
            selectionBefore.Clear();
            return __exception;
        }

        private static void AnnounceIfChanged(WorldSelector selector)
        {
            List<WorldObject> now = selector.SelectedObjects;
            PlanetTile tileNow = selector.SelectedTile;
            if (now == null || (tileNow == tileBefore && Unchanged(now)))
                return;

            MoveCursor(now, tileNow);

            // A click that lands on empty planet selects a tile rather than an object
            // (WorldSelector.SelectUnderMouse), which the world's own tile grammar already reads.
            if (now.Count == 0 && tileNow.Valid && WorldNavigationState.IsInitialized)
            {
                WorldNavigationState.AnnounceTile();
                return;
            }

            string text = WorldSelectionAnnouncer.DescribeSelection(now);
            if (!string.IsNullOrEmpty(text))
                TolkHelper.SpeakData(text);
        }

        /// <summary>
        /// Vanilla leaves <c>SelectedTile</c> invalid whenever it selects an object, so the cursor
        /// follows the selected object's own tile first, then the selected tile, then the tile the
        /// click landed on.
        /// </summary>
        private static void MoveCursor(List<WorldObject> now, PlanetTile tileNow)
        {
            if (!WorldNavigationState.IsActive)
                return;
            PlanetTile dest = now.Count > 0 ? now[0].Tile : (tileNow.Valid ? tileNow : clickedTile);
            if (dest.Valid)
                WorldNavigationState.CurrentSelectedTile = dest;
        }

        private static bool Unchanged(List<WorldObject> now)
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
