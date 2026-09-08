using System;
using RimWorldAccess.Platform;
using UnityEngine;
using Verse;

namespace RimWorldAccess.Shell
{
    /// <summary>
    /// Whether the mouse pointer moved this frame — the single fact every
    /// hover channel gates on, and the reason a capture is worth taking at
    /// all. Answered once per frame and cached: several passes ask within one
    /// frame, and a per-asker comparison would leave everyone after the first
    /// seeing "not moved".
    ///
    /// Unfocused the answer is always no, which is what makes every hover
    /// channel inert while the pointer belongs to another application, and the
    /// answer stays no across the whole re-seed that follows a refocus.
    /// </summary>
    internal static class PointerMotion
    {
        private static Vector2 lastPos = NoPointer;
        private static int answeredFrame = -1;
        private static bool moved;
        private static bool reseeding = true;
        private static int reseedFrames;
        private static bool postWarpBaseline;
        private static int postWarpFrames;

        /// <summary>
        /// How many focused frames the re-seed always absorbs before agreement may end it.
        /// Unity's walk to the real pointer can HOLD its screen-extreme placeholder for several
        /// frames, and two agreeing placeholder readings used to end the re-seed early — the
        /// jumps to the real position that followed then spoke as hand movement, twice within
        /// 150ms of a refocus (QA trace 2026-08-13). The refocus snap's own warp and its settle
        /// corrections also land inside this window.
        /// </summary>
        private const int ReseedFrameFloor = 6;

        /// <summary>
        /// How many focused frames the re-seed may run before it trusts its reading anyway. A
        /// pointer still travelling when focus returns never produces two agreeing readings, and
        /// without a ceiling that would leave every hover channel inert for as long as the hand
        /// keeps moving. When the ceiling ends it, only motion AFTER that frame speaks — which
        /// is genuine hand movement by then.
        /// </summary>
        private const int ReseedFrameCeiling = 30;

        /// <summary>
        /// How many focused frames the post-warp re-baseline may run before it trusts
        /// its reading anyway. The settle pass ends at the dispatcher step, but the
        /// final warp's movement can register in Input.mousePosition one or more polls
        /// later (a warp is applied before the NEXT input poll — see
        /// PointerWarpState's remarks), so the first free polls after a suppression
        /// must re-baseline, not speak. Two agreeing readings end it early; the
        /// ceiling keeps a hand already moving when the warp ends from muting hover
        /// for good.
        /// </summary>
        private const int PostWarpFrameCeiling = 10;

        /// <summary>
        /// How many frames an OS-pointer movement attests Unity movement for. Unity's
        /// Input.mousePosition registers a physical move one or two polls after the OS
        /// does, so same-frame agreement would swallow short genuine moves.
        /// </summary>
        private const int OsMoveAttestFrames = 3;

        /// <summary>OS points below which two ground-truth readings count as the same position.</summary>
        private const double OsMoveEpsilonPoints = 0.25;

        private static double lastOsX = double.NaN;
        private static double lastOsY = double.NaN;
        private static int osMovedFrame = -1;

        /// <summary>A pointer position that no live pointer can equal, so the re-seed needs two agreeing readings before it trusts one and a resting pointer stays silent.</summary>
        private static Vector2 NoPointer
        {
            get { return new Vector2(float.NaN, float.NaN); }
        }

        /// <summary>
        /// Forget the baseline and re-seed from the pointer's current position. Called when
        /// hover tracking turns ON: nothing polls this while tracking is off, so the stale
        /// baseline would read the resting pointer as hand movement on the first poll, and
        /// that utterance interrupts the toggle's own announcement. Re-seeding makes the
        /// next genuine hand movement the first thing that speaks.
        /// </summary>
        internal static void Rebase()
        {
            lastPos = NoPointer;
            reseeding = true;
            reseedFrames = 0;
            postWarpBaseline = false;
            postWarpFrames = 0;
            lastOsX = double.NaN;
            lastOsY = double.NaN;
            osMovedFrame = -1;
            moved = false;
        }

        /// <summary>
        /// Whether the OS's own pointer position — the ground truth CoreGraphics and its
        /// platform twins report — moved within the last few frames. Unity's
        /// Input.mousePosition fabricates motion the hand never made: around a refocus it
        /// walks through placeholder readings toward the real position for over a second
        /// (QA trace 2026-08-18), and a warp registers a poll or two late. The OS position
        /// never lies about whether the pointer physically moved, so no Unity movement
        /// counts without it. A platform with no readable pointer keeps the old
        /// Unity-only behavior.
        /// </summary>
        private static bool OsPointerAttestsMotion()
        {
            ISystemPointer pointer = SystemPointer.Current;
            double x, y;
            if (!pointer.Available || !pointer.TryGetPosition(out x, out y))
                return true;
            bool movedNow = !double.IsNaN(lastOsX)
                && (Math.Abs(x - lastOsX) > OsMoveEpsilonPoints || Math.Abs(y - lastOsY) > OsMoveEpsilonPoints);
            lastOsX = x;
            lastOsY = y;
            if (movedNow)
                osMovedFrame = Time.frameCount;
            return osMovedFrame >= 0 && Time.frameCount - osMovedFrame <= OsMoveAttestFrames;
        }

        internal static bool MovedThisFrame()
        {
            if (Time.frameCount != answeredFrame)
            {
                answeredFrame = Time.frameCount;
                // A capture-bracket caller can run before the dispatcher's NotePass site on the refocus frame.
                RefocusClock.NotePass();
                if (!SystemPointer.HostFocused)
                {
                    lastPos = NoPointer;
                    reseeding = true;
                    reseedFrames = 0;
                    postWarpBaseline = false;
                    postWarpFrames = 0;
                    lastOsX = double.NaN;
                    lastOsY = double.NaN;
                    osMovedFrame = -1;
                    moved = false;
                    return false;
                }
                // Polled on every focused frame, not only the speaking path, so the
                // ground-truth baseline stays fresh across the suppressed branches below.
                bool osAttests = OsPointerAttestsMotion();
                Vector2 pos = UI.MousePositionOnUIInverted;
                // A warp in flight is the mod moving the pointer, not the hand: swallow the
                // motion for exactly as long as the warp takes to settle, however long that
                // is. The frame-window heuristic below cannot do this — a slow settle used
                // to outlive the re-seed and speak as hand movement (QA trace 2026-08-14).
                if (PointerWarpState.SuppressingHover)
                {
                    lastPos = pos;
                    moved = false;
                    postWarpBaseline = true;
                    postWarpFrames = 0;
                    return false;
                }
                // The settle's own last movement registers AFTER suppression ends (the
                // reading lags the warp), so the first free polls re-baseline until the
                // pointer has held still once — hand movement from then on speaks.
                if (postWarpBaseline)
                {
                    postWarpFrames++;
                    postWarpBaseline = pos != lastPos && postWarpFrames < PostWarpFrameCeiling;
                    lastPos = pos;
                    moved = false;
                    return false;
                }
                if (reseeding)
                {
                    // Unity walks Input.mousePosition to the real pointer over the first frames
                    // back: a screen-extreme placeholder first, then the pointer's own position,
                    // each step a pixel jump this would otherwise read as a hand moving the
                    // mouse. Application.isFocused flips a frame or two before that, so the
                    // re-seed cannot be counted in frames or measured against the reading taken
                    // when it flipped — it ends when two consecutive readings agree, but only
                    // after the floor of frames the placeholder itself can sit still for, or
                    // when the ceiling runs out.
                    reseedFrames++;
                    // Floor and agreement alone are fps-relative: a fast menu satisfies both
                    // while Unity still holds its placeholder, and the later jump to the real
                    // position speaks. Only the wall-clock refocus hold survives every framerate.
                    reseeding = !RefocusClock.SettledFor(RefocusClock.WarpQuietSeconds)
                        || ((pos != lastPos || reseedFrames < ReseedFrameFloor)
                            && reseedFrames < ReseedFrameCeiling);
                    lastPos = pos;
                    moved = false;
                    return false;
                }
                moved = pos != lastPos && osAttests;
                lastPos = pos;
            }
            return moved;
        }
    }
}
