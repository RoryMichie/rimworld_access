using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using RimWorld;
using RimWorldAccess.Shell;
using UnityEngine;
using Verse;

namespace RimWorldAccess
{
    /// <summary>
    /// Rings the row the keyboard is reading on the REAL inspect tab, while the
    /// windowless inspection tree drives (InspectPaneLink rule 4: the pane is open,
    /// the row's category came from a real <see cref="InspectTabBase"/>, and that
    /// tab's contents draw live). Rows with no vanilla surface — the synthetic
    /// aggregations — stay unringed because there is nothing on screen to ring.
    ///
    /// The trust boundary: the capture cache's rects come from the harness's OFFSCREEN pass, which
    /// draws FillTab far off screen with the tab's scroll positions zeroed, so they are synthetic and
    /// can never place a ring. Only the tab's own live draw knows where a row really is, so this
    /// brackets that draw with a geometry-only detached pass and bands the result exactly as the
    /// capture layer bands its own stream. Band N of the live pass is the captured row whose
    /// <see cref="FoldedRow.BandIndex"/> is N, and only while both passes band to the same count; a
    /// tab that reshaped since the tree was built goes unringed until the tree refreshes.
    ///
    /// <see cref="InspectionScope"/> is mirror-pushed with a null OwnedWindow, so
    /// FocusedContentRect is unavailable here — this is a draw-hook ring
    /// (QuantityMenuRingPatch's shape).
    /// </summary>
    internal static class InspectTabRowRing
    {
        /// <summary>Vanilla's immediate-window id for the open inspect tab, as WindowStack stores it: negated.</summary>
        private const int TabWindowID = -235086;

        /// <summary>The row the ring wants, resolved from the tree before the tab draws.</summary>
        private struct RingTarget
        {
            public object Target;
            public InspectTabBase Tab;
            public int BandIndex;
            public int CapturedBandCount;
        }

        // Reused: this runs on every Repaint of the open tab and must not allocate per frame.
        private static readonly List<CapturedWidget> sink = new List<CapturedWidget>();
        private static bool passOpen;
        private static int lastPassFrame = -1;

        private static RingTarget armed;
        private static bool isArmed;

        // The bander is the expensive half, so it re-runs only when the live stream's geometry
        // changes, never merely because the cursor moved.
        private static object bandedTarget;
        private static InspectTabBase bandedTab;
        private static int bandedHash;
        private static int bandedCount;
        private static Rect[] bandRects = Array.Empty<Rect>();

        internal static void BeginTabDraw(Window window)
        {
            isArmed = false;
            if (window == null || window.ID != TabWindowID
                || Event.current == null || Event.current.type != EventType.Repaint)
            {
                return;
            }
            if (!TryResolveFocusedRow(out armed))
            {
                Forget();
                return;
            }
            isArmed = true;
            if (Time.frameCount == lastPassFrame || WidgetCapture.IsPassOpen || HoverWantsThisWindow(window))
            {
                // Hover's own pass has this window's stream this frame, and only one of us can own
                // it. Hover would lose an utterance where the ring only goes a frame stale, so yield;
                // the cached band rects still ring.
                return;
            }
            lastPassFrame = Time.frameCount;
            sink.Clear();
            WidgetCapture.BeginDetachedPass(sink);
            passOpen = true;
        }

        internal static void EndTabDraw(bool drew)
        {
            bool captured = false;
            if (passOpen)
            {
                passOpen = false;
                // A live pass nested inside this bracket already ended it, so the sink holds a
                // truncated remnant.
                if (WidgetCapture.DetachedPassOpen)
                {
                    WidgetCapture.EndDetachedPass();
                    captured = drew;
                }
                if (!captured)
                {
                    sink.Clear();
                }
            }
            if (!isArmed)
            {
                return;
            }
            isArmed = false;
            if (captured)
            {
                Reband(armed);
            }
            if (drew)
            {
                Draw(armed);
            }
        }

        /// <summary>
        /// Whether <see cref="HoverCapturePass"/> will claim this window's draw — its own gate,
        /// restated here so the outcome does not depend on how Harmony orders the two prefixes.
        /// </summary>
        private static bool HoverWantsThisWindow(Window window)
        {
            return HoverSpeech.Enabled && PointerMotion.MovedThisFrame()
                && window.windowRect.Contains(UI.MousePositionOnUIInverted);
        }

        private static bool TryResolveFocusedRow(out RingTarget target)
        {
            target = default(RingTarget);
            if (!WindowlessInspectionState.IsActive)
            {
                return false;
            }
            InspectionScope scope = InspectionScope.Live;
            InspectionTreeItem row = scope?.FocusedRow();
            CapturedRowRef captured = NearestCapturedRow(row);
            if (captured == null)
            {
                return false;
            }
            // A captured row only ever hangs under the category it was captured for, so this is
            // InspectPaneLink rule 4's test: that tab is the open one.
            if (!(MainButtonDefOf.Inspect.TabWindow is MainTabWindow_Inspect pane)
                || pane.OpenTabType != captured.Tab.GetType())
            {
                return false;
            }
            if (!InspectTabCaptureService.TryGetBandCount(captured.Target, captured.Tab, out int bandCount))
            {
                return false;
            }
            target = new RingTarget
            {
                Target = captured.Target,
                Tab = captured.Tab,
                BandIndex = captured.BandIndex,
                CapturedBandCount = bandCount,
            };
            return true;
        }

        /// <summary>The nearest captured row at or above <paramref name="row"/>: a control's children belong to their control's band.</summary>
        private static CapturedRowRef NearestCapturedRow(InspectionTreeItem row)
        {
            for (InspectionTreeItem node = row; node != null; node = node.Parent)
            {
                if (node.CapturedRow != null)
                {
                    return node.CapturedRow;
                }
            }
            return null;
        }

        private static void Reband(RingTarget target)
        {
            // Vanilla draws the tab's close button before FillTab and the captured stream is FillTab
            // alone, so the bands line up only once it is dropped.
            if (sink.Count > 0 && sink[0].CloseX)
            {
                sink.RemoveAt(0);
            }
            int hash = StreamHash(sink);
            if (hash == bandedHash && ReferenceEquals(bandedTarget, target.Target)
                && ReferenceEquals(bandedTab, target.Tab))
            {
                sink.Clear();
                return;
            }
            bandedHash = hash;
            bandedTarget = target.Target;
            bandedTab = target.Tab;
            List<List<int>> bands = CapturedRowFolder.BandsFor(sink);
            bandedCount = bands.Count;
            bandRects = new Rect[bands.Count];
            for (int i = 0; i < bands.Count; i++)
            {
                bandRects[i] = VisibleRectOf(bands[i]);
            }
            sink.Clear();
        }

        /// <summary>The visible part of a band: the union of its members' own visible rects, skipping any scrolled out of view.</summary>
        private static Rect VisibleRectOf(List<int> band)
        {
            Rect union = default(Rect);
            foreach (int index in band)
            {
                Rect rect = sink[index].VisibleScreenRect;
                if (rect.width <= 0f || rect.height <= 0f)
                {
                    continue;
                }
                union = union.width <= 0f || union.height <= 0f
                    ? rect
                    : Rect.MinMaxRect(
                        Mathf.Min(union.xMin, rect.xMin), Mathf.Min(union.yMin, rect.yMin),
                        Mathf.Max(union.xMax, rect.xMax), Mathf.Max(union.yMax, rect.yMax));
            }
            return union;
        }

        /// <summary>The banding inputs hashed: banding reads nothing but geometry, so an unchanged hash means unchanged bands.</summary>
        private static int StreamHash(List<CapturedWidget> widgets)
        {
            unchecked
            {
                int hash = 17 + widgets.Count;
                foreach (CapturedWidget widget in widgets)
                {
                    hash = hash * 31 + (int)widget.Kind;
                    hash = hash * 31 + widget.Rect.GetHashCode();
                    hash = hash * 31 + widget.VisibleScreenRect.GetHashCode();
                }
                return hash;
            }
        }

        private static void Draw(RingTarget target)
        {
            if (!ReferenceEquals(bandedTarget, target.Target) || !ReferenceEquals(bandedTab, target.Tab))
            {
                return;
            }
            // The tab drew a different number of rows than the tree was built from, so band N is no
            // longer row N: no ring this pass.
            if (bandedCount != target.CapturedBandCount
                || target.BandIndex < 0 || target.BandIndex >= bandRects.Length)
            {
                return;
            }
            Rect rect = bandRects[target.BandIndex];
            if (rect.width <= 0f || rect.height <= 0f)
            {
                return;
            }
            FocusRing.Draw(GuiSpace.FromScreen(rect));
            UiPointerFollow.NotifyFocusedRect(rect);
        }

        /// <summary>
        /// Drops the cached banding once no row wants a ring, and at a session boundary with the
        /// capture cache it views. Every read is gated on reference equality with the row being rung,
        /// so a stale cache can only suppress a ring, never misplace one.
        /// </summary>
        internal static void Forget()
        {
            bandedTarget = null;
            bandedTab = null;
            bandedHash = 0;
            bandedCount = 0;
            bandRects = Array.Empty<Rect>();
        }
    }

    /// <summary>
    /// Brackets the open inspect tab's own live draw for <see cref="InspectTabRowRing"/>, on the
    /// universal Window.InnerWindowOnGUI hook: the tab draws inside an ImmediateWindow vanilla
    /// creates internally, so no scope owns it and a bespoke FillTab bracket cannot reach a mod's
    /// tab. Pure observation — it records, rings, and never touches the event.
    /// </summary>
    [HarmonyPatch]
    internal static class InspectTabRowRingPatch
    {
        static MethodBase TargetMethod()
        {
            return AccessTools.Method(typeof(Window), "InnerWindowOnGUI", new Type[] { typeof(int) });
        }

        [HarmonyPrefix]
        [HarmonyPriority(Priority.Last)]
        public static void Prefix(Window __instance)
        {
            try
            {
                InspectTabRowRing.BeginTabDraw(__instance);
            }
            catch (Exception ex)
            {
                ModLogger.LimitedError("Inspect tab row ring pass error", ex);
            }
        }

        /// <summary>
        /// A finalizer, not a postfix: a postfix does not run when the tab's body throws, and the
        /// leaked detached pass would then throw out of the next BeginDetachedPass.
        /// </summary>
        [HarmonyFinalizer]
        public static Exception Finalizer(Exception __exception)
        {
            try
            {
                InspectTabRowRing.EndTabDraw(drew: __exception == null);
            }
            catch (Exception ex)
            {
                ModLogger.LimitedError("Inspect tab row ring pass error", ex);
            }
            return __exception;
        }
    }
}
