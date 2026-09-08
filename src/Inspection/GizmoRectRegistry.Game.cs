using System.Collections.Generic;
using UnityEngine;
using Verse;

namespace RimWorldAccess
{
    /// <summary>
    /// Records the rect vanilla itself drew for each gizmo on the current gizmo
    /// bar, keyed by <see cref="Gizmo"/> instance. Exists because the bar's
    /// layout cannot be reproduced by arithmetic: <c>startX</c> is
    /// <c>14f + InspectPaneUtility.PaneWidthFor(pane)</c> and
    /// <c>PaneWidthFor</c> is at least 432f (decompiled
    /// Verse/GizmoGridDrawer.cs:118-124, RimWorld/InspectPaneUtility.cs:36-66);
    /// gizmo widths are per-gizmo (<c>GetWidth(maxWidth)</c>,
    /// Verse/GizmoGridDrawer.cs:283); rows wrap on accumulated width
    /// (:251-255); and shrinkable commands move to their own shrunk band once
    /// the grid needs more than one row (:216-224, :288-320). All of that is
    /// free if the rect is recorded where vanilla draws it instead.
    /// </summary>
    internal static class GizmoRectRegistry
    {
        private static readonly Dictionary<Gizmo, Rect> rects = new Dictionary<Gizmo, Rect>();

        /// <summary>Draw order of <see cref="rects"/>' keys — the stable ordinal hover identifies a hit gizmo by.</summary>
        private static readonly List<Gizmo> drawOrder = new List<Gizmo>();

        private static bool recording;

        /// <summary>Frame of the most recent bar draw — see <see cref="TryHitTest"/>.</summary>
        private static int lastDrawFrame = -1;

        /// <summary>
        /// Opens the recording window and clears every rect from the previous
        /// bar draw. Clearing on every open is what guarantees a stale rect
        /// from a bar that is no longer on screen can never be looked up and
        /// rung — a miss this frame reads as "no rect", never a leftover one.
        /// </summary>
        internal static void BeginFrame()
        {
            rects.Clear();
            drawOrder.Clear();
            lastDrawFrame = Time.frameCount;
            recording = true;
        }

        /// <summary>Closes the recording window. Called from a <c>finally</c> so a thrown draw pass cannot leave it open.</summary>
        internal static void EndFrame()
        {
            recording = false;
        }

        /// <summary>No-op unless called between <see cref="BeginFrame"/> and <see cref="EndFrame"/>.</summary>
        internal static void Record(Gizmo gizmo, Rect rect)
        {
            if (!recording || gizmo == null)
            {
                return;
            }
            if (!rects.ContainsKey(gizmo))
            {
                drawOrder.Add(gizmo);
            }
            rects[gizmo] = rect;
        }

        /// <summary>
        /// The gizmo whose drawn rect contains <paramref name="point"/> (UI
        /// space), with its draw ordinal — the identity hover reads a hovered
        /// gizmo by, since two gizmos on one bar can carry the same label.
        /// Last drawn wins an overlap, matching the topmost thing a pointer
        /// would hit.
        ///
        /// A bar that has not drawn since the previous frame owns nothing: the
        /// last selection's rects sit in the dictionary until some bar draws
        /// again, and callers outside the draw bracket would read them as live.
        /// </summary>
        internal static bool TryHitTest(Vector2 point, out Gizmo gizmo, out int ordinal)
        {
            if (Time.frameCount - lastDrawFrame > 1)
            {
                gizmo = null;
                ordinal = -1;
                return false;
            }
            for (int i = drawOrder.Count - 1; i >= 0; i--)
            {
                if (rects.TryGetValue(drawOrder[i], out Rect rect) && rect.Contains(point))
                {
                    gizmo = drawOrder[i];
                    ordinal = i;
                    return true;
                }
            }
            gizmo = null;
            ordinal = -1;
            return false;
        }

        /// <summary>
        /// Looks up the rect vanilla drew for <paramref name="gizmo"/> on the
        /// most recently completed bar draw. Returns false when vanilla drew
        /// a different group representative than the caller has in hand, or
        /// when no bar has drawn this frame at all.
        /// </summary>
        internal static bool TryGet(Gizmo gizmo, out Rect rect)
        {
            if (gizmo == null)
            {
                rect = default(Rect);
                return false;
            }
            if (rects.TryGetValue(gizmo, out rect))
            {
                return true;
            }

            // Identity almost never matches: GetGizmos() builds fresh Gizmo
            // instances every enumeration, so the menu's snapshot and the
            // instances vanilla drew this frame are different objects for the
            // same command. Fall back to vanilla's own sameness test — the
            // GroupsWith the grid drawer itself groups gizmos with — so the
            // ring lands on the drawn representative of the browsed command.
            foreach (KeyValuePair<Gizmo, Rect> entry in rects)
            {
                bool same;
                try
                {
                    same = entry.Key.GroupsWith(gizmo) || gizmo.GroupsWith(entry.Key);
                }
                catch
                {
                    continue; // A modded GroupsWith that throws just skips its entry.
                }
                if (same)
                {
                    rect = entry.Value;
                    return true;
                }
            }
            rect = default(Rect);
            return false;
        }
    }
}
