using UnityEngine;
using Verse;

namespace RimWorldAccess.Shell
{
    /// <summary>
    /// Whether the OS pointer is on the game's own drawing surface.
    ///
    /// Unity keeps answering <c>Input.mousePosition</c> for a pointer that has left the window —
    /// negative, or past the far edge — and the game window keeps rendering while it holds focus,
    /// so "focused" is not the same fact as "the pointer is over the game". It matters because the
    /// map reader turns a screen point into a cell through the camera ray, which happily
    /// extrapolates past the viewport into cells that are still in bounds: an off-window pointer
    /// then reads out a convincing stream of cells nobody is looking at, while the mouse presses
    /// that go with it are delivered to whatever the pointer is really over and never reach the
    /// game at all (e.g. a drag made above the window's top edge).
    /// </summary>
    internal static class PointerSurface
    {
        /// <summary>
        /// True while the pointer is inside the game's surface. Measured in the absolute UI points
        /// <c>UI.screenWidth</c>/<c>screenHeight</c> describe, the same space
        /// <see cref="ShellGuards.NonImmediateWindowUnderPointer"/> hit-tests window rects in.
        /// </summary>
        internal static bool PointerInside
        {
            get
            {
                Vector2 pos = UI.MousePositionOnUIInverted;
                return pos.x >= 0f && pos.y >= 0f
                    && pos.x <= UI.screenWidth && pos.y <= UI.screenHeight;
            }
        }
    }
}
