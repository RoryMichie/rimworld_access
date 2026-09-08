using System;
using System.Collections.Generic;
using HarmonyLib;
using RimWorldAccess.Platform;
using Verse;

namespace RimWorldAccess
{
    /// <summary>
    /// Speaks the paint-drag a sighted player watches grow: the extent and the number of cells
    /// the designator will actually act on, plus the dragger's own rejection reason. Same numbers
    /// vanilla paints over the box in <c>DesignationDragger.DraggerOnGUI</c>.
    ///
    /// Announce-only. It reads the dragger's already-built buffer and its per-frame memoised
    /// <c>DragCells</c>; it never starts, ends or steers a drag, and never touches the event.
    /// Not gated by the hover-speech setting: a drag is an explicit held-button gesture, and this is
    /// its only feedback channel.
    /// </summary>
    [HarmonyPatch(typeof(DesignationDragger), nameof(DesignationDragger.DraggerUpdate))]
    internal static class DesignationDragSpeech
    {
        private static bool spokenValid;
        private static int spokenCount;
        private static CellRect spokenBounds;
        private static IntVec3 spokenCell = IntVec3.Invalid;

        [HarmonyPostfix]
        public static void Postfix(DesignationDragger __instance)
        {
            try
            {
                // The buffer is rebuilt each Update from the live pointer
                // (decompiled Verse/DesignationDragger.cs:217), so a drag the player left open
                // by switching apps must not keep reporting a mouse that is no longer here.
                if (!SystemPointer.HostFocused)
                {
                    return;
                }
                if (__instance == null || !__instance.Dragging)
                {
                    spokenValid = false;
                    spokenCell = IntVec3.Invalid;
                    return;
                }

                List<IntVec3> buffer = __instance.CellBuffer;
                if (buffer == null || buffer.Count == 0)
                {
                    spokenValid = false;
                    spokenCell = IntVec3.Invalid;
                    return;
                }

                CellRect bounds = Bounds(buffer);
                Map map = Find.CurrentMap;
                // UI.UIToMapPosition rather than UI.MouseCell, which a dev-tool override patches.
                IntVec3 cell = UI.UIToMapPosition(UI.MousePositionOnUI).ToIntVec3();
                bool cellChanged = cell != spokenCell;
                // Buffer length pairs with the box: a style worker can reshape without moving the extent.
                if (spokenValid && bounds == spokenBounds && buffer.Count == spokenCount && !cellChanged)
                {
                    return;
                }
                spokenValid = true;
                spokenBounds = bounds;
                spokenCount = buffer.Count;
                spokenCell = cell;

                bool inBounds = map != null && cell.InBounds(map);
                if (cellChanged && inBounds)
                {
                    TerrainAudioHelper.PlayCellAudio(cell, map, 0.5f);
                }

                AnnouncementBuilder builder = new AnnouncementBuilder();
                builder.Add("RimWorldAccess.Map.Drag.PaintExtent"
                    .Loc(bounds.Width, bounds.Height, __instance.DragCells.Count).ToString());
                builder.Add(__instance.FailureReason);
                if (inBounds)
                {
                    builder.Add(MapArrowKeyHandler.ComposePositionAnnouncement(cell, map, null, allowStateWrites: false));
                }
                string text = builder.Build();
                if (!string.IsNullOrEmpty(text))
                {
                    TolkHelper.SpeakData(text, SpeechPriority.High);
                }
            }
            catch (Exception ex)
            {
                ModLogger.LimitedError("Designation drag speech error", ex);
            }
        }

        private static CellRect Bounds(List<IntVec3> cells)
        {
            IntVec3 first = cells[0];
            int minX = first.x, maxX = first.x, minZ = first.z, maxZ = first.z;
            for (int i = 1; i < cells.Count; i++)
            {
                IntVec3 c = cells[i];
                if (c.x < minX) minX = c.x;
                if (c.x > maxX) maxX = c.x;
                if (c.z < minZ) minZ = c.z;
                if (c.z > maxZ) maxZ = c.z;
            }
            return CellRect.FromLimits(minX, minZ, maxX, maxZ);
        }
    }
}
