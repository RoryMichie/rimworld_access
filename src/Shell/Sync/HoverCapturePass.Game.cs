using System;
using System.Collections.Generic;
using UnityEngine;
using Verse;

namespace RimWorldAccess.Shell
{
    /// <summary>
    /// A throwaway capture of whatever surface the pointer is over, so hover
    /// reads a window whether or not anything else brackets it. Detached by
    /// construction (see <see cref="WidgetCapture.BeginDetachedPass"/>): it
    /// records into its own sink, leaves Items and the pending activate/adjust
    /// posts alone, and arms no injection path, so it can never mutate the
    /// surface it reads.
    ///
    /// Cost: one extra capture of one window per Repaint, only while hover is
    /// enabled, only for the window under the pointer, discarded immediately.
    ///
    /// It never opens a <see cref="TooltipCapture"/> pass of any kind —
    /// diverting a window's tip registrations into the detached index would rob
    /// whatever else reads them. Hover's tooltips come from
    /// <see cref="LiveTooltips"/>, which reads vanilla's own live tips and is
    /// authoritative for a real pointer.
    /// </summary>
    internal static class HoverCapturePass
    {
        // Reused across frames: this runs on every Repaint of the hovered
        // window and must not allocate per frame.
        private static readonly List<CapturedWidget> sink = new List<CapturedWidget>();
        private static bool open;

        /// <summary>
        /// Arms a pass over <paramref name="surfaceRect"/> when hover wants one
        /// and nobody else is capturing. Returns whether the caller now owes an
        /// <see cref="End"/>.
        /// </summary>
        internal static bool TryBegin(Rect surfaceRect)
        {
            try
            {
                if (!HoverSpeech.Enabled)
                {
                    return false;
                }
                if (Event.current == null || Event.current.type != EventType.Repaint)
                {
                    return false;
                }
                // Nothing a still pointer can be told, so nothing worth
                // capturing: hover would discard the stream at its own gate.
                if (!PointerMotion.MovedThisFrame())
                {
                    return false;
                }
                // Order-independent safety net behind the callers' own
                // "somebody else brackets this window" checks, and what keeps
                // this out of the inspect-tab capture harness's way.
                if (WidgetCapture.IsPassOpen)
                {
                    return false;
                }
                // The pointer space the hit test uses, so at most one window a
                // frame is ever captured.
                if (!surfaceRect.Contains(UI.MousePositionOnUIInverted))
                {
                    return false;
                }
                sink.Clear();
                WidgetCapture.BeginDetachedPass(sink);
                LiveTooltips.BeginBracket();
                open = true;
                return true;
            }
            catch (Exception ex)
            {
                ModLogger.LimitedError("Hover capture pass error", ex);
                return false;
            }
        }

        /// <summary>
        /// Closes the pass. <paramref name="evaluate"/> is false where the
        /// window's draw threw: the stream is a truncated remnant of the
        /// surface. The pass ends either way — a leaked detached pass would
        /// make the next <see cref="WidgetCapture.BeginDetachedPass"/> throw.
        /// </summary>
        internal static void End(bool evaluate)
        {
            if (!open)
            {
                return;
            }
            open = false;
            // A live pass nested inside this bracket ended it (see
            // WidgetCapture.BeginPass): the sink holds a truncated remnant and
            // the pass is no longer this one's to close.
            if (!WidgetCapture.DetachedPassOpen)
            {
                sink.Clear();
                return;
            }
            try
            {
                if (evaluate)
                {
                    HoverSpeech.EvaluatePass(sink, detachedTips: false);
                }
            }
            catch (Exception ex)
            {
                ModLogger.LimitedError("Hover capture pass error", ex);
            }
            finally
            {
                try
                {
                    WidgetCapture.EndDetachedPass();
                }
                catch (Exception ex)
                {
                    ModLogger.LimitedError("Hover capture pass error", ex);
                }
                sink.Clear();
            }
        }
    }
}
