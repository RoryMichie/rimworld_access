using System;
using System.Collections.Generic;
using System.Linq;
using LudeonTK;
using Verse;

namespace RimWorldAccess.Shell
{
    /// <summary>
    /// Shared outcome announcer for keyboard-initiated dev-mode debug actions
    /// (the F12 &gt; Development leaves, the pinned-palette actions, the debug
    /// option-list rows, and the menu's window openers). A debug action can
    /// open a window, arm a map tool, show a floating Message, write only to the
    /// log, do nothing observable, or throw — and every one of those but the
    /// throw was, before this wrapper, either announced by some other surface or
    /// swallowed in silence. <see cref="RunAndAnnounce"/> gives the silent and
    /// throwing cases a voice while deferring to the self-announcing ones, so a
    /// keyboard user always hears exactly one result.
    ///
    /// Precedence after a successful invoke (first match wins, one announcement):
    /// a self-announcing surface took over — a new window whose scope speaks on
    /// attach, or an on-demand scope attach to an already-open window (the debug
    /// log the user opened to read an auto-opened error) — stays silent; an armed
    /// <see cref="DebugTools.curTool"/> is left to the DevToolTargeting mirror; a
    /// fresh vanilla Message is left to the notification announcer; new log
    /// entries are summarized; and a no-op consults the caller-supplied
    /// <c>quietOutcome</c> (a resolution known only to the caller, such as a
    /// captured toggle's new state) and otherwise says "done". A throw preserves
    /// vanilla's error path (a red log entry, honoring the auto-open-log pref)
    /// then speaks a short failure instead of leaving pure silence.
    ///
    /// "A self-announcing scope took over" is read off the focus stack, not the
    /// window stack alone: an on-demand attach pushes a scope WITHOUT adding a
    /// window, so a window-only check would miss it and double-speak over the
    /// scope's own open announcement. A scope that merely POPPED (a leaf that
    /// closed its dialog then wrote to the log) must NOT suppress the log
    /// summary, which is why the focus-stack test requires the depth to have held
    /// or grown, not shrunk.
    /// </summary>
    internal static class DevActionOutcome
    {
        private const int HeadlineMaxLength = 200;

        internal static void RunAndAnnounce(string label, Action action, Func<string> quietOutcome = null)
        {
            if (action == null)
            {
                return;
            }

            // Snapshot every observable surface BEFORE invoking: many actions
            // close the current dialog and open a submenu or arm a tool inside
            // the same call, so the baseline must be taken first.
            int logCountBefore = Log.Messages.Count();
            long messagesBefore = NotificationAccessibilityPatch.MessageEmissionCount;
            DebugTool toolBefore = DebugTools.curTool;
            HashSet<Window> windowsBefore = SnapshotWindows();
            FocusScope focusTopBefore = FocusStack.Top;
            int focusCountBefore = FocusStack.Count;
            bool floatMenuBefore = WindowlessFloatMenuState.IsActive;

            try
            {
                action();
            }
            catch (Exception ex)
            {
                // Preserve vanilla's error path (the red log entry and the
                // auto-open-log pref) so the detail is recoverable, then speak a
                // short failure so the action does not fail in silence.
                Log.Error(label + " threw: " + ex);
                TolkHelper.SpeakData("RimWorldAccess.Dev.Outcome.Failed".Translate(
                    label, FirstLine(ex.Message)).ToString());
                return;
            }

            // Precedence a: a self-announcing surface took over. A new window's
            // scope announces on attach; an on-demand attach pushes a scope onto
            // the focus stack with no new window (the depth grows or holds).
            if (NewWindowAppeared(windowsBefore) || SelfAnnouncingScopeTookOver(focusTopBefore, focusCountBefore))
            {
                return;
            }
            // Precedence a2: a windowless float menu opened and announces itself.
            // Its FocusScope is pushed by mirror reconcile a frame later, so the
            // focus-stack test above alone misses it.
            if (!floatMenuBefore && WindowlessFloatMenuState.IsActive)
            {
                return;
            }
            // Precedence b: a debug tool was armed — the DevToolTargeting mirror
            // speaks the arming plus the keyboard usage hint.
            if (!ReferenceEquals(DebugTools.curTool, toolBefore))
            {
                return;
            }
            // Precedence c: a fresh vanilla Message — the notification announcer
            // already spoke it (its emission counter moved).
            if (NotificationAccessibilityPatch.MessageEmissionCount != messagesBefore)
            {
                return;
            }
            // Precedence d: the action only wrote to the log.
            List<LogMessage> newEntries = NewLogEntries(logCountBefore);
            if (newEntries.Count == 1)
            {
                TolkHelper.SpeakData("RimWorldAccess.Dev.Outcome.WroteLogOne".Translate(
                    Headline(newEntries[0])).ToString());
                return;
            }
            if (newEntries.Count > 1)
            {
                TolkHelper.SpeakData("RimWorldAccess.Dev.Outcome.WroteLog".Translate(
                    newEntries.Count, Headline(newEntries[0])).ToString());
                return;
            }
            // Precedence e: nothing self-announcing happened. Consult the
            // caller-supplied resolution (a captured toggle's new state, say)
            // and speak it; otherwise fall back to the generic "done".
            string quiet = quietOutcome?.Invoke();
            if (!string.IsNullOrEmpty(quiet))
            {
                TolkHelper.SpeakData(quiet);
                return;
            }
            TolkHelper.SpeakData("RimWorldAccess.Dev.Outcome.Done".Translate(label).ToString());
        }

        private static HashSet<Window> SnapshotWindows()
        {
            var set = new HashSet<Window>();
            if (Find.WindowStack != null)
            {
                foreach (Window w in Find.WindowStack.Windows)
                {
                    set.Add(w);
                }
            }
            return set;
        }

        private static bool NewWindowAppeared(HashSet<Window> before)
        {
            if (Find.WindowStack == null)
            {
                return false;
            }
            foreach (Window w in Find.WindowStack.Windows)
            {
                if (!before.Contains(w))
                {
                    return true;
                }
            }
            return false;
        }

        /// <summary>
        /// True when a scope was pushed on top of the focus stack — a bespoke
        /// scope attached to a newly opened OR already-open window, both of which
        /// announce themselves. A scope that only popped (its dialog closed)
        /// leaves the depth smaller and must not suppress the log summary, so the
        /// depth is required to have held or grown.
        /// </summary>
        private static bool SelfAnnouncingScopeTookOver(FocusScope topBefore, int countBefore)
        {
            return !ReferenceEquals(FocusStack.Top, topBefore) && FocusStack.Count >= countBefore;
        }

        /// <summary>
        /// The log entries appended since the snapshot, newest at the tail (the
        /// same <c>Log.Messages</c> collection EditWindow_Log enumerates). The
        /// queue only drops old entries above its 1000-message cap, which one
        /// action never reaches, so the count delta is a safe tail slice.
        /// </summary>
        private static List<LogMessage> NewLogEntries(int countBefore)
        {
            List<LogMessage> all = Log.Messages.ToList();
            if (countBefore < 0)
            {
                countBefore = 0;
            }
            if (countBefore >= all.Count)
            {
                return new List<LogMessage>();
            }
            return all.GetRange(countBefore, all.Count - countBefore);
        }

        private static string Headline(LogMessage message)
        {
            string text = message == null ? "" : message.text;
            if (string.IsNullOrEmpty(text))
            {
                return "";
            }
            text = FirstLine(text.StripTags());
            if (text.Length > HeadlineMaxLength)
            {
                text = text.Substring(0, HeadlineMaxLength)
                    + "RimWorldAccess.Dev.Outcome.Ellipsis".Translate().ToString();
            }
            return text;
        }

        private static string FirstLine(string text)
        {
            if (string.IsNullOrEmpty(text))
            {
                return "";
            }
            int newline = text.IndexOfAny(new[] { '\r', '\n' });
            return newline < 0 ? text : text.Substring(0, newline);
        }
    }
}
