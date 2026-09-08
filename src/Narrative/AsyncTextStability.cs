using System;
using System.Collections.Generic;

namespace RimWorldAccess
{
    /// <summary>
    /// Pure wait-for-settled state machine behind the stable-read rule:
    /// an extension mod rewrites an
    /// already-displayed string after an async (HTTP/LLM) round-trip, and a
    /// reader positioned on that surface must never speak the in-flight text.
    /// NEVER announce partial streamed text; a
    /// generating surface announces only a "still being written" marker, and
    /// the real announcement fires ONCE, complete, when the signal settles —
    /// silently dropped if the player has since navigated away.
    ///
    /// This class carries no Unity/Verse dependency (every "is it still
    /// generating" and "is it still relevant" check is a caller-supplied
    /// delegate), so it is fully unit-testable. <see cref="AsyncTextStability"/>
    /// (its <c>.Game.cs</c> half) is the single static instance every reader in
    /// the mod shares, plus the localized marker text and the per-frame poll.
    /// </summary>
    public sealed class AsyncTextStabilityCore
    {
        private sealed class PendingInterest
        {
            public Func<bool> IsGenerating;
            public Func<bool> IsStillRelevant;
            public Func<string> FinalTextGetter;
            public Action<string> OnSettledAnnounce;
        }

        private readonly Dictionary<string, PendingInterest> pending = new Dictionary<string, PendingInterest>(StringComparer.Ordinal);

        /// <summary>Pending-interest count, for tests and diagnostics.</summary>
        public int PendingCount => pending.Count;

        public bool IsPending(string key)
        {
            return !string.IsNullOrEmpty(key) && pending.ContainsKey(key);
        }

        /// <summary>
        /// Registers (or replaces) a pending interest under <paramref name="key"/>: the next
        /// <see cref="Tick"/> at which <paramref name="isGenerating"/> reports false fires
        /// <paramref name="onSettledAnnounce"/> with <paramref name="finalTextGetter"/>'s result exactly
        /// once, PROVIDED <paramref name="isStillRelevant"/> (evaluated at settle time, not now) still
        /// holds — the caller's way of saying "the player has since navigated away, don't fire." A caller
        /// re-<see cref="Defer"/>-ing the same key before it settles simply replaces the earlier interest
        /// (the most recent read wins); this is expected when a still-generating surface is revisited on
        /// a later frame with a freshened closure over the same logical row.
        /// </summary>
        public void Defer(string key, Func<bool> isGenerating, Func<bool> isStillRelevant, Func<string> finalTextGetter, Action<string> onSettledAnnounce)
        {
            if (string.IsNullOrEmpty(key) || isGenerating == null || onSettledAnnounce == null)
            {
                return;
            }
            pending[key] = new PendingInterest
            {
                IsGenerating = isGenerating,
                IsStillRelevant = isStillRelevant,
                FinalTextGetter = finalTextGetter,
                OnSettledAnnounce = onSettledAnnounce,
            };
        }

        /// <summary>Drops a pending interest without firing it (e.g. the caller's own screen is tearing down).</summary>
        public void Cancel(string key)
        {
            if (!string.IsNullOrEmpty(key))
            {
                pending.Remove(key);
            }
        }

        /// <summary>Drops every pending interest without firing any of them (session-boundary reset).</summary>
        public void Clear()
        {
            pending.Clear();
        }

        /// <summary>
        /// One poll pass. For every pending interest whose signal has settled
        /// (<c>IsGenerating()</c> now false), removes it and — only if
        /// <c>IsStillRelevant()</c> still holds — fires its callback with the final text. An interest
        /// still generating is left untouched. Safe to call every frame; a no-op when nothing is pending.
        /// Removal happens before any callback runs, so a callback that re-<see cref="Defer"/>s the same
        /// key (the surface immediately began generating again) is safe.
        /// </summary>
        public void Tick()
        {
            if (pending.Count == 0)
            {
                return;
            }

            List<string> settledKeys = null;
            foreach (KeyValuePair<string, PendingInterest> kvp in pending)
            {
                if (kvp.Value.IsGenerating())
                {
                    continue;
                }
                if (settledKeys == null)
                {
                    settledKeys = new List<string>();
                }
                settledKeys.Add(kvp.Key);
            }
            if (settledKeys == null)
            {
                return;
            }

            var toFire = new List<PendingInterest>(settledKeys.Count);
            for (int i = 0; i < settledKeys.Count; i++)
            {
                string key = settledKeys[i];
                toFire.Add(pending[key]);
                pending.Remove(key);
            }

            for (int i = 0; i < toFire.Count; i++)
            {
                PendingInterest interest = toFire[i];
                bool stillRelevant = interest.IsStillRelevant == null || interest.IsStillRelevant();
                if (stillRelevant)
                {
                    interest.OnSettledAnnounce(interest.FinalTextGetter != null ? interest.FinalTextGetter() : null);
                }
            }
        }
    }
}
