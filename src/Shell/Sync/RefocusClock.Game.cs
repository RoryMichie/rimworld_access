using RimWorldAccess.Platform;
using UnityEngine;

namespace RimWorldAccess.Shell
{
    /// <summary>
    /// Wall-clock time since the game window last regained OS focus.
    ///
    /// The frames after a refocus are turbulent in ways frame counting cannot bound:
    /// macOS is still completing the app activation for a while after
    /// Application.isFocused flips true (a pointer warp issued inside that window can
    /// cancel the Command+Tab switch outright), and Unity walks Input.mousePosition
    /// through fabricated placeholder readings before the real pointer position
    /// arrives — over a second on occasion (QA trace 2026-08-18, 13:17). Frame-counted
    /// guards sized for 60fps shrink to a few dozen milliseconds on a fast map, so the
    /// only wait that holds at every framerate is a wall-clock one.
    /// </summary>
    internal static class RefocusClock
    {
        /// <summary>
        /// How long every mod-issued pointer warp holds after a refocus. Long enough to
        /// outlive both the macOS activation window and Unity's longest observed
        /// placeholder walk (~1.2s), with margin.
        /// </summary>
        internal const float WarpQuietSeconds = 2f;

        private static int notedFrame = -1;
        private static bool wasFocused = true;
        private static float regainedAt = float.NegativeInfinity;

        /// <summary>
        /// Records a focus regain. Safe to call from several per-frame sites; only the
        /// first call of a frame reads the flag.
        /// </summary>
        internal static void NotePass()
        {
            if (Time.frameCount == notedFrame)
                return;
            notedFrame = Time.frameCount;
            bool focused = SystemPointer.HostFocused;
            if (focused && !wasFocused)
                regainedAt = Time.realtimeSinceStartup;
            wasFocused = focused;
        }

        /// <summary>True once the window has held OS focus for at least <paramref name="seconds"/>.</summary>
        internal static bool SettledFor(float seconds)
        {
            return SystemPointer.HostFocused && Time.realtimeSinceStartup - regainedAt >= seconds;
        }
    }
}
