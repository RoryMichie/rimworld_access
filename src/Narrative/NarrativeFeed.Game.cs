using System;
using Verse;

namespace RimWorldAccess
{
    /// <summary>
    /// One narrative line as a producer sees it — Pawn references and raw
    /// game data, not yet resolved to the plain strings <see cref="NarrativeRecord"/>
    /// retains.
    /// </summary>
    public struct NarrativeEvent
    {
        public Pawn Speaker;
        public Pawn Recipient;
        public string Text;
        public NarrativeSource Source;
        public string DedupeKey;
        public int ConversationId;
        public bool VocalizedByTts;
        public bool AnnounceEligible;

        public NarrativeEvent(Pawn speaker, Pawn recipient, string text, NarrativeSource source, string dedupeKey, bool vocalizedByTts)
        {
            Speaker = speaker;
            Recipient = recipient;
            Text = text;
            Source = source;
            DedupeKey = dedupeKey;
            ConversationId = -1;
            VocalizedByTts = vocalizedByTts;
            AnnounceEligible = true;
        }
    }

    /// <summary>
    /// The game-facing funnel for the Narrative Feed: the single entry point every producer (Bubbles
    /// postfix, RimTalk overlay reader, player-line watcher, extension channels — waves 2+) calls to
    /// publish a line of ambient narrative text. Wraps the pure
    /// <see cref="NarrativeFeedCore"/> ring buffer/dedupe/announce-decision
    /// engine with the game types (Pawn labels, ticks, settings, speech) it
    /// cannot depend on.
    /// </summary>
    public static class NarrativeFeed
    {
        private static readonly NarrativeFeedCore core = new NarrativeFeedCore();

        /// <summary>The live core, for the Dialogue Log screen to read.</summary>
        internal static NarrativeFeedCore Core => core;

        /// <summary>Fires after any successful (non-duplicate) <see cref="Publish"/>, so an open Dialogue Log scope can refresh in place.</summary>
        public static event Action FeedChanged;

        /// <summary>
        /// Publishes one narrative line: builds the plain-string
        /// <see cref="NarrativeRecord"/>, dedupes/appends it via the core,
        /// and — when the announce-decision matrix says yes — speaks it. Silent no-op on a duplicate.
        /// </summary>
        public static void Publish(NarrativeEvent e)
        {
            var record = new NarrativeRecord
            {
                SpeakerName = e.Speaker?.LabelShort,
                // RimTalk monologues target the speaker itself (CreateInteraction falls back to
                // the initiator when a line has no target); "Vlad to Vlad" reads wrong, so a
                // self-recipient collapses to the speakerless grammar.
                RecipientName = e.Recipient == e.Speaker ? null : e.Recipient?.LabelShort,
                Text = e.Text,
                Source = e.Source,
                DedupeKey = e.DedupeKey,
                ConversationId = e.ConversationId,
                Tick = GenTicks.TicksGame,
                VocalizedByTts = e.VocalizedByTts,
                AnnounceEligible = e.AnnounceEligible,
                SpeakerRef = e.Speaker != null ? new WeakReference(e.Speaker) : null,
            };

            if (core.Add(record) == NarrativeAddResult.Duplicate)
            {
                return;
            }

            RimWorldAccessSettings settings = RimWorldAccessMod_Settings.Settings;
            if (settings != null)
            {
                var options = new NarrativeAnnounceOptions(
                    settings.AnnouncePawnDialogue,
                    settings.AnnounceVanillaInteractionBubbles,
                    !settings.TtsAnnounceAnyway);

                if (core.ShouldAnnounce(record, options))
                {
                    // Low priority: navigation speech always wins over dialogue lines.
                    TolkHelper.SpeakData(ComposeLine(record), SpeechPriority.Low);
                }
            }

            FeedChanged?.Invoke();
        }

        /// <summary>
        /// The grammar core: picks the LineTo/Line/System variant from which of speaker/recipient
        /// resolved. Shared by the auto-announcer above and the Dialogue Log screen, so the two can
        /// never drift apart on wording.
        /// </summary>
        public static string ComposeLine(NarrativeRecord record)
        {
            if (SpeakerPrefixCarriesInformation(record))
            {
                if (record.SpeakerName != null && record.RecipientName != null)
                {
                    return "RimWorldAccess.Narrative.LineTo".Translate(record.SpeakerName, record.RecipientName, record.Text);
                }
                if (record.SpeakerName != null)
                {
                    return "RimWorldAccess.Narrative.Line".Translate(record.SpeakerName, record.Text);
                }
            }
            return "RimWorldAccess.Narrative.System".Translate(record.Text);
        }

        /// <summary>
        /// Whether the "{speaker} to {recipient}" prefix says anything the line itself does not.
        /// Only <see cref="NarrativeSource.Interaction"/> fails this: vanilla interaction text is
        /// generated from the pawns' own names ("Devin discusses food with Bob"), so the prefix
        /// merely repeats them. No RimTalk gate is needed — a line RimTalk rewrote arrives as
        /// <see cref="NarrativeSource.RimTalkLine"/> from its own
        /// <c>PlayLogEntry_RimTalkInteraction</c>, never as Interaction, so free LLM prose that
        /// names nobody keeps its attribution through the source test alone.
        /// </summary>
        private static bool SpeakerPrefixCarriesInformation(NarrativeRecord record)
        {
            return record.Source != NarrativeSource.Interaction;
        }

        /// <summary>Session-boundary reset — registered in <see cref="StateResetRegistry"/>.</summary>
        public static void Reset()
        {
            core.Reset();
        }
    }
}
