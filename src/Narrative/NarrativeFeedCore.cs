using System;
using System.Collections.Generic;

namespace RimWorldAccess
{
    /// <summary>
    /// Where a <see cref="NarrativeRecord"/> came from. Drives the per-source
    /// toggle in <see cref="NarrativeFeedCore.ShouldAnnounce"/>.
    /// </summary>
    public enum NarrativeSource
    {
        Interaction,
        RimTalkLine,
        PlayerLine,
        Extension
    }

    /// <summary>
    /// Result of <see cref="NarrativeFeedCore.Add"/>: whether the record was
    /// appended to the ring buffer or dropped as a repeat of a still-retained
    /// <see cref="NarrativeRecord.DedupeKey"/>.
    /// </summary>
    public enum NarrativeAddResult
    {
        Added,
        Duplicate
    }

    /// <summary>
    /// One line of narrative text, already resolved to plain display strings
    /// (no LogEntry/Pawn references — those live only in the game-facing
    /// funnel that builds this record) so it can be retained across a save
    /// and read by a pure, game-free ring buffer.
    /// </summary>
    public sealed class NarrativeRecord
    {
        public string SpeakerName;
        public string RecipientName;
        public string Text;
        public NarrativeSource Source;
        public string DedupeKey;
        public int ConversationId;
        public int Tick;
        public bool VocalizedByTts;
        public bool AnnounceEligible;

        /// <summary>
        /// Weak reference to the speaker Pawn, for the Dialogue Log's "Jump to speaker" action.
        /// Object-typed (a plain <see cref="WeakReference"/>, not a Pawn-typed one) so this
        /// pure, game-free class never references Verse; the game-facing funnel
        /// (<c>NarrativeFeed.Publish</c>, <c>.Game.cs</c>) sets it, and the Dialogue Log scope
        /// resolves it back to a Pawn via <c>SpeakerRef.Target as Pawn</c>. Null for system/
        /// speakerless lines.
        /// </summary>
        public WeakReference SpeakerRef;
    }

    /// <summary>
    /// Pure decision inputs for <see cref="NarrativeFeedCore.ShouldAnnounce"/> —
    /// a snapshot of the player's settings, kept separate from
    /// <see cref="RimWorldAccessSettings"/> so this class stays game-free.
    /// </summary>
    public readonly struct NarrativeAnnounceOptions
    {
        public readonly bool MasterEnabled;
        public readonly bool VanillaInteractionsEnabled;
        public readonly bool TtsBackoffEnabled;

        public NarrativeAnnounceOptions(bool masterEnabled, bool vanillaInteractionsEnabled, bool ttsBackoffEnabled)
        {
            MasterEnabled = masterEnabled;
            VanillaInteractionsEnabled = vanillaInteractionsEnabled;
            TtsBackoffEnabled = ttsBackoffEnabled;
        }
    }

    /// <summary>
    /// Game-free core of the Narrative Feed: a capped ring buffer of
    /// <see cref="NarrativeRecord"/> plus the
    /// pure announce-decision matrix. Kept static-free so unit tests
    /// construct a fresh instance per test; the game-facing funnel
    /// (<c>NarrativeFeed</c>, <c>.Game.cs</c>) owns the single live instance
    /// and everything that touches Verse/RimWorld/Unity types.
    /// </summary>
    public sealed class NarrativeFeedCore
    {
        public const int Capacity = 500;

        private readonly NarrativeRecord[] buffer = new NarrativeRecord[Capacity];
        private int count;
        private int nextSlot;
        private readonly HashSet<string> retainedKeys = new HashSet<string>();

        /// <summary>
        /// Every record currently retained, oldest first (newest last) —
        /// matches the speech ring / capture-buffer convention elsewhere in
        /// the mod (see <c>TolkHelper.SpeechRingSnapshot</c>).
        /// </summary>
        public IReadOnlyList<NarrativeRecord> Snapshot()
        {
            var result = new NarrativeRecord[count];
            int start = count < Capacity ? 0 : nextSlot;
            for (int i = 0; i < count; i++)
            {
                result[i] = buffer[(start + i) % Capacity];
            }
            return result;
        }

        /// <summary>
        /// Appends <paramref name="record"/> unless its
        /// <see cref="NarrativeRecord.DedupeKey"/> is already held by a
        /// retained record, in which case it is dropped entirely (no double entry). Eviction on a full
        /// buffer frees the evicted record's key for reuse — dedupe keys are unique per line in
        /// practice, so this never collides.
        /// </summary>
        public NarrativeAddResult Add(NarrativeRecord record)
        {
            if (record.DedupeKey != null && retainedKeys.Contains(record.DedupeKey))
            {
                return NarrativeAddResult.Duplicate;
            }

            if (count == Capacity)
            {
                NarrativeRecord evicted = buffer[nextSlot];
                if (evicted?.DedupeKey != null)
                {
                    retainedKeys.Remove(evicted.DedupeKey);
                }
            }
            else
            {
                count++;
            }

            buffer[nextSlot] = record;
            nextSlot = (nextSlot + 1) % Capacity;
            if (record.DedupeKey != null)
            {
                retainedKeys.Add(record.DedupeKey);
            }

            return NarrativeAddResult.Added;
        }

        /// <summary>
        /// Pure announce-decision matrix. A duplicate never reaches this — it was dropped at
        /// <see cref="Add"/>. Rules apply in order; the first match wins.
        /// </summary>
        public bool ShouldAnnounce(NarrativeRecord record, NarrativeAnnounceOptions options)
        {
            if (!record.AnnounceEligible)
            {
                return false;
            }
            if (!options.MasterEnabled)
            {
                return false;
            }
            if (record.Source == NarrativeSource.Interaction && !options.VanillaInteractionsEnabled)
            {
                return false;
            }
            if (record.VocalizedByTts && options.TtsBackoffEnabled)
            {
                return false;
            }
            return true;
        }

        /// <summary>Clears the buffer and dedupe keys — the session boundary reset.</summary>
        public void Reset()
        {
            for (int i = 0; i < buffer.Length; i++)
            {
                buffer[i] = null;
            }
            count = 0;
            nextSlot = 0;
            retainedKeys.Clear();
        }
    }
}
