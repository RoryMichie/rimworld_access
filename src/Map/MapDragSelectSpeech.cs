using System;
using RimWorld;
using RimWorldAccess.Platform;
using RimWorldAccess.Shell;
using UnityEngine;
using Verse;

namespace RimWorldAccess
{
    /// <summary>
    /// Speaks the extent of the map drag-select box while the button is held — the only thing a
    /// sighted player is shown during that gesture (<c>DragBox.DragBoxOnGUI</c> draws a rectangle
    /// and nothing else). What the release actually selects is announced by
    /// <see cref="MapClickAnnouncementPatch"/>.
    ///
    /// Deliberately no live count of the contents: <c>Selector.SelectInsideDragBox</c> is a
    /// category cascade whose winning category is only known once it runs, so a mid-drag count
    /// would not be the number the release selects.
    ///
    /// Announce-only, and not gated by the hover-speech setting.
    /// </summary>
    internal static class MapDragSelectSpeech
    {
        private static bool spokenValid;
        private static int spokenWidth;
        private static int spokenHeight;

        /// <summary>Called once per map OnGUI pass, alongside <see cref="MapHoverSpeech.Evaluate"/>.</summary>
        internal static void Evaluate()
        {
            try
            {
                if (Event.current == null || Event.current.type != EventType.Repaint)
                {
                    return;
                }
                // The box's far corner is the live pointer (decompiled RimWorld/DragBox.cs:15-21),
                // so a drag left open by an app switch would keep growing off another app's mouse.
                if (!SystemPointer.HostFocused)
                {
                    return;
                }
                Selector selector = Find.Selector;
                DragBox box = selector != null ? selector.dragBox : null;
                if (box == null || !box.IsValidAndActive || !MapScope.MapSurfaceLive())
                {
                    spokenValid = false;
                    return;
                }

                // The box edges are map coordinates; a cell is covered when its centre falls
                // inside, which is the same half-cell test DragBox.Contains applies.
                int width = Mathf.RoundToInt(box.RightX - box.LeftX) + 1;
                int height = Mathf.RoundToInt(box.TopZ - box.BotZ) + 1;
                if (spokenValid && width == spokenWidth && height == spokenHeight)
                {
                    return;
                }
                spokenValid = true;
                spokenWidth = width;
                spokenHeight = height;

                TolkHelper.Speak(
                    "RimWorldAccess.Map.Drag.SelectExtent".Loc(width, height), SpeechPriority.High);
            }
            catch (Exception ex)
            {
                ModLogger.LimitedError("Map drag select speech error", ex);
            }
        }
    }
}
