using System;
using System.Collections.Generic;
using UnityEngine;
using Verse;

namespace RimWorldAccess
{
    /// <summary>
    /// Game-facing static facade over <see cref="AsyncTextStabilityCore"/> (the stable-read rule): the
    /// ONE shared instance every reader that touches an extension mod's async-rewritten text calls
    /// into. Riders: Literature (book/art descriptions, keyed off
    /// <c>PendingBookQueue</c>/<c>PendingArtQueue</c> membership — see <c>RimTalkLiteratureCompat</c>),
    /// with Quests (<c>RimTalkQuestsCompat.GetDescriptionOrMarker</c>, folding Quests' own per-quest
    /// <c>_processingQuests</c> signal together with Literature's separate quest-addendum queue at one
    /// shared read site) and any future streaming-LLM mod expected to ride the same two entry points
    /// below.
    ///
    /// Two ways to gate a read, matching the brief's "RegisterSignal" shape
    /// while also covering the more precise case Literature's own per-object
    /// pending queues make possible:
    /// <list type="bullet">
    /// <item>A NAMED, coarse signal via <see cref="RegisterSignal"/> (Quests'
    /// mod-wide <c>ProcessingCount &gt; 0</c> is the precedent the report
    /// itself names) — imprecise (every read of that mod's surfaces shows the
    /// marker while ANYTHING of that kind is generating, not just the one the
    /// player is looking at), but the only option when the mod exposes no
    /// per-object handle.</item>
    /// <item>A PRECISE, ad-hoc <c>Func&lt;bool&gt;</c> passed directly to the
    /// <see cref="GetTextOrMarker(Func{bool},string,Func{string},Func{bool},Action{string})"/>
    /// overload — used when, like Literature's <c>PendingBookQueue.Contains(key)</c>,
    /// the mod's own state lets the caller ask about THIS specific object.</item>
    /// </list>
    /// </summary>
    public static class AsyncTextStability
    {
        private static readonly AsyncTextStabilityCore core = new AsyncTextStabilityCore();
        private static readonly Dictionary<string, Func<bool>> signals = new Dictionary<string, Func<bool>>(StringComparer.Ordinal);
        private static int lastTickFrame = -1;

        /// <summary>The live core, for screens/tests to inspect pending-interest counts.</summary>
        internal static AsyncTextStabilityCore Core => core;

        /// <summary>
        /// Registers (or replaces) a named, reusable "is this mod's kind of surface still generating"
        /// probe. Call once per signal, typically from a Compat class's static constructor or
        /// <c>Register()</c> — the probe itself should re-resolve any live game state on every call (a
        /// WorldComponent, a static counter) rather than caching a reference that could go stale across a
        /// save load.
        /// </summary>
        public static void RegisterSignal(string signalKey, Func<bool> isGenerating)
        {
            if (string.IsNullOrEmpty(signalKey) || isGenerating == null)
            {
                return;
            }
            signals[signalKey] = isGenerating;
        }

        /// <summary>Evaluates a previously-registered named signal. False (never generating) for an unknown key.</summary>
        public static bool IsGenerating(string signalKey)
        {
            return !string.IsNullOrEmpty(signalKey) && signals.TryGetValue(signalKey, out Func<bool> probe) && probe != null && probe();
        }

        /// <summary>
        /// The call a reader makes at the moment it would otherwise announce
        /// <paramref name="finalTextGetter"/>'s result, gated on a NAMED signal registered via
        /// <see cref="RegisterSignal"/>. See the precise overload below for the per-object case.
        /// </summary>
        public static string GetTextOrMarker(string signalKey, string interestKey, Func<string> finalTextGetter, Func<bool> isStillRelevant, Action<string> onSettledAnnounce)
        {
            return GetTextOrMarker(() => IsGenerating(signalKey), interestKey, finalTextGetter, isStillRelevant, onSettledAnnounce);
        }

        /// <summary>
        /// The call a reader makes at the moment it would otherwise announce
        /// <paramref name="finalTextGetter"/>'s result, gated on a caller-supplied precise predicate.
        /// Returns the final text immediately (deferring nothing) when
        /// <paramref name="isGeneratingNow"/> is false; otherwise returns the localized
        /// "still being written" marker and arms <paramref name="onSettledAnnounce"/> to fire — at most
        /// once, and only if <paramref name="isStillRelevant"/> still holds at settle time — the moment
        /// generation finishes. <paramref name="interestKey"/> identifies the pending interest so a later
        /// call for the SAME logical surface (the row is still on screen next frame) replaces rather than
        /// duplicates it.
        /// </summary>
        public static string GetTextOrMarker(Func<bool> isGeneratingNow, string interestKey, Func<string> finalTextGetter, Func<bool> isStillRelevant, Action<string> onSettledAnnounce)
        {
            if (isGeneratingNow == null || !isGeneratingNow())
            {
                return finalTextGetter != null ? finalTextGetter() : null;
            }
            if (!string.IsNullOrEmpty(interestKey) && onSettledAnnounce != null)
            {
                core.Defer(interestKey, isGeneratingNow, isStillRelevant, finalTextGetter, onSettledAnnounce);
            }
            return StillBeingWrittenMarker();
        }

        /// <summary>Drops a pending interest without firing it (a screen tearing down its own state early).</summary>
        public static void Cancel(string interestKey)
        {
            core.Cancel(interestKey);
        }

        public static string StillBeingWrittenMarker()
        {
            return "RimWorldAccess.Narrative.StillBeingWritten".Translate();
        }

        /// <summary>
        /// Per-frame settle poll. Called from <c>ShellDispatcherPatch.Prefix</c>'s top every OnGUI pass
        /// (same cadence as <see cref="BulkSoundQueue.Update"/>), guarded here to run the actual core Tick
        /// at most once per real engine frame since Layout and Repaint both reach that call site and firing
        /// a settled announcement twice in one frame would double-speak it.
        /// </summary>
        public static void Tick()
        {
            int frame = Time.frameCount;
            if (frame == lastTickFrame)
            {
                return;
            }
            lastTickFrame = frame;
            core.Tick();
        }

        /// <summary>
        /// Session-boundary reset (<see cref="StateResetRegistry"/>): drops every pending interest
        /// unfired. Registered SIGNALS are deliberately NOT cleared here — each is a process-lifetime
        /// probe installed once by a Compat class (matching <c>ScopeForWindow.factories</c>, never reset
        /// either), and every signal implementation re-resolves live state on each call rather than
        /// caching anything that could go stale across a save load.
        /// </summary>
        internal static void Reset()
        {
            core.Clear();
            lastTickFrame = -1;
        }
    }
}
