using UnityEngine;
using Verse;
using RimWorldAccess.Platform;

namespace RimWorldAccess.Shell
{
    /// <summary>
    /// The UI half of the pointer-follows-keyboard convention: while a screen scope
    /// publishes a focused content rect, the OS pointer shadows that rect's centre
    /// instead of the map cursor, so a sighted viewer sees where the keyboard is even
    /// on screens with no map behind them.
    ///
    /// Rides <see cref="RimWorldAccessSettings.PointerFollowsKeyboard"/> (Alt+Shift+K) and
    /// reuses <see cref="PointerWarpState"/>'s closed-loop settle, so a UI warp suppresses
    /// hover speech exactly like a map warp; always silent, since the follow is not an event.
    /// </summary>
    internal static class UiPointerFollow
    {
        /// <summary>Passes a published rect stays authoritative — a scope that stopped
        /// drawing hands the pointer back to the map follow.</summary>
        private const int FreshFrames = 2;

        /// <summary>UI points the target may drift without re-warping (no jitter loops).</summary>
        private const float SettledPoints = 2f;

        private static Vector2 target;
        private static int notifiedFrame = -1;
        private static bool targetStable;
        private static Vector2 warpedCenter;
        private static bool hasWarped;

        /// <summary>Published by the ring arm each draw pass a focused rect is live.</summary>
        internal static void NotifyFocusedRect(Rect screenPoints)
        {
            // An empty rect is the "nothing visible" sentinel every rect source shares (a
            // scrolled-out row, a degraded clip binding in GuiSpace.VisibleScreenRect); parking
            // the pointer on its (0,0) centre would be a warp to the screen corner.
            if (screenPoints.width <= 0f || screenPoints.height <= 0f)
            {
                return;
            }
            // A target must repeat across two frames before the pointer chases it: a rect moving
            // every frame is a layout still settling or a publisher reacting to the pointer's hop.
            if (Time.frameCount != notifiedFrame)
            {
                targetStable = notifiedFrame >= 0
                    && Vector2.Distance(screenPoints.center, target) <= SettledPoints;
            }
            target = screenPoints.center;
            notifiedFrame = Time.frameCount;
        }

        /// <summary>True while a scope's rect is recent enough to outrank the map follow.</summary>
        internal static bool HasFreshTarget
        {
            get { return notifiedFrame >= 0 && Time.frameCount - notifiedFrame <= FreshFrames; }
        }

        /// <summary>
        /// Called once per OnGUI pass from the dispatcher's per-frame step, which — unlike
        /// the map's own tick site — also runs on menus with no map loaded.
        /// </summary>
        internal static void Tick()
        {
            if (Event.current == null || Event.current.type != EventType.Repaint)
                return;
            // This site runs on every screen, map or not, so it is where the refocus
            // clock reliably sees the focus flip.
            RefocusClock.NotePass();
            PointerWarpState.TickUiWarpSettle();

            RimWorldAccessSettings settings = RimWorldAccessMod_Settings.Settings;
            if (settings == null || !settings.PointerFollowsKeyboard)
                return;
            if (!SystemPointer.HostFocused || !SystemPointer.Current.Available)
                return;
            // Same turbulence hold as the refocus snap — see PointerWarpState.TickPendingSnap.
            if (!RefocusClock.SettledFor(RefocusClock.WarpQuietSeconds))
                return;
            if (Input.GetMouseButton(0) || Input.GetMouseButton(1) || Input.GetMouseButton(2))
                return;
            if (PointerWarpState.SuppressingHover)
                return;
            if (!HasFreshTarget || !targetStable)
                return;
            if (hasWarped && Vector2.Distance(target, warpedCenter) <= SettledPoints)
                return;

            // UI points to Unity screen pixels: the same scale factor everywhere, and
            // Unity's y grows upward where UI points grow downward.
            float scale = Prefs.UIScale;
            Vector2 pixels = new Vector2(target.x * scale, Screen.height - target.y * scale);
            if (PointerWarpState.BeginUiWarp(pixels))
            {
                warpedCenter = target;
                hasWarped = true;
            }
        }
    }
}
