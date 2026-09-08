using System.Collections.Generic;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.Sound;

namespace RimWorldAccess.Shell
{
    /// <summary>
    /// The keyboard's half of the pointer bridge (NVDA's "route review cursor to mouse"):
    /// the shared gate and the shared refusal for menus.routeToPointer, so every scope
    /// that claims the chord asks the same question and fails the same way. The match
    /// itself belongs to each scope — only the scope knows its own rows.
    ///
    /// All geometry is absolute UI points, the single GuiSpace space, read through
    /// UI.MousePositionOnUIInverted. Never UI.MousePosUIInvertedUseEventIfCan (a
    /// clip-stack-dependent derivation) and never GUIClip.Unclip, whose size/position
    /// mismatch above 100% UI scale is a wrong-click bug.
    /// </summary>
    internal static class PointerRouting
    {
        /// <summary>The pointer, in the one space every captured ScreenRect lives in.</summary>
        internal static Vector2 Pointer
        {
            get { return UI.MousePositionOnUIInverted; }
        }

        /// <summary>
        /// Whether <paramref name="window"/> is the surface the pointer is actually on:
        /// inside its own rect, with nothing above it in the stack covering that point.
        /// A scope must never route its cursor from a window it does not own.
        /// </summary>
        internal static bool PointerOwnedBy(Window window)
        {
            if (window == null)
            {
                return false;
            }
            // A surface named from recorded geometry (RowGeometryCache.HostWindow) can have
            // closed since it drew, and a window off the stack owns no pixels.
            IList<Window> windows = Find.WindowStack != null ? Find.WindowStack.Windows : null;
            if (windows == null || !windows.Contains(window))
            {
                return false;
            }
            if (!window.windowRect.Contains(Pointer))
            {
                return false;
            }
            return !ShellGuards.NonImmediateWindowUnderPointer(window);
        }

        /// <summary>
        /// The one refusal, sounded wherever a route resolves to nothing: vanilla's own
        /// rejected-click, the sound it plays for a FloatMenuOption that will not take a
        /// click (decompiled Verse/FloatMenuOption.cs:310).
        /// </summary>
        internal static void RejectNoTarget()
        {
            if (SoundDefOf.ClickReject == null)
                return;
            SoundDefOf.ClickReject.PlayOneShotOnCamera();
        }

        /// <summary>
        /// Picks the innermost of the candidate rects containing the pointer, by the same
        /// rule HoverSpeech.HitTest applies: deepest clip wins because the deepest
        /// container IS the innermost control under the pointer, ties broken by latest
        /// draw order. An empty visible rect is scrolled out and can never be hit.
        /// Returns -1 for no hit.
        /// </summary>
        internal static int HitTest(IReadOnlyList<PointerHitCandidate> candidates)
        {
            Vector2 pointer = Pointer;
            int best = -1;
            int bestDepth = int.MinValue;
            for (int i = 0; i < candidates.Count; i++)
            {
                PointerHitCandidate candidate = candidates[i];
                if (!candidate.Contains(pointer))
                {
                    continue;
                }
                if (best < 0 || candidate.ClipDepth >= bestDepth)
                {
                    best = i;
                    bestDepth = candidate.ClipDepth;
                }
            }
            return best;
        }
    }

    /// <summary>
    /// A per-frame map from a row's own identity object to the candidate it drew, filled
    /// by a bespoke row tap so a scope can route to ANY row rather than only the one its
    /// focus ring already marks. Geometry is converted at the tap, where the clip stack
    /// is still live; the map is dropped on the first record of a new frame, which is all
    /// the lifetime a pointer chord needs.
    ///
    /// The host window is recorded with the rows: vanilla holds
    /// <see cref="WindowStack.currentlyDrawnWindow"/> for the whole of
    /// <c>Window.InnerWindowOnGUI</c> (decompiled Verse/Window.cs:211-307), which encloses
    /// every one of these taps, so a mirror-pushed scope with no <c>OwnedWindow</c> can name
    /// the surface it is drawn on without a bespoke per-screen lookup — and keeps naming the
    /// right one when a surface moves between hosts.
    /// </summary>
    internal sealed class RowGeometryCache
    {
        private readonly Dictionary<object, PointerHitCandidate> rows = new Dictionary<object, PointerHitCandidate>();
        private int frame = -1;
        private Window host;

        /// <summary>The window whose GUI pass last drew these rows, for <c>ScreenScope.PointerSurface</c>.</summary>
        internal Window HostWindow
        {
            get { return host; }
        }

        internal void Record(object row, Rect guiRect)
        {
            if (row == null)
            {
                return;
            }
            if (frame != Time.frameCount)
            {
                rows.Clear();
                frame = Time.frameCount;
            }
            host = Find.WindowStack != null ? Find.WindowStack.currentlyDrawnWindow : null;
            rows[row] = new PointerHitCandidate
            {
                Primary = GuiSpace.VisibleScreenRect(guiRect),
                ClipDepth = GuiSpace.CurrentClip().Depth,
            };
        }

        /// <summary>Appends <paramref name="row"/>'s candidate, aimed at (region, index), when the last pass drew it.</summary>
        internal void AddCandidate(object row, int region, int index,
            List<PointerHitCandidate> candidates, List<ScreenScope.RouteTarget> targets)
        {
            PointerHitCandidate candidate;
            if (row == null || !rows.TryGetValue(row, out candidate))
            {
                return;
            }
            candidates.Add(candidate);
            targets.Add(new ScreenScope.RouteTarget { Region = region, Index = index });
        }
    }

    /// <summary>One routable target's geometry: up to two visible rects (a control and the caption fused into it) and the clip depth that breaks ties.</summary>
    internal struct PointerHitCandidate
    {
        public Rect Primary;
        public Rect Secondary;
        public int ClipDepth;

        internal bool Contains(Vector2 pointer)
        {
            return (Primary.width > 0f && Primary.height > 0f && Primary.Contains(pointer))
                || (Secondary.width > 0f && Secondary.height > 0f && Secondary.Contains(pointer));
        }
    }
}
