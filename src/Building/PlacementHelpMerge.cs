namespace RimWorldAccess
{
    /// <summary>
    /// The decision half of the placement announcement merge: which single utterance carries both
    /// the placement help vanilla drew and the tile description the cursor step composed, and what
    /// the help's changed-since-last-time test remembers afterwards.
    ///
    /// The two used to be separate utterances in the wrong order — the tile description is spoken
    /// from the key dispatch, the help from vanilla's own draw pass, which runs after it — so a
    /// player heard a long tile description before "links to Field Smithy". Merging them puts the
    /// help first without touching either side's own dedup: the help still speaks only when it
    /// changes (entering and leaving range, which is the whole value of the channel), and the tile
    /// description still speaks on every step.
    /// </summary>
    internal static class PlacementHelpMerge
    {
        /// <summary>
        /// How many frames a held tile description waits for its draw pass before it is spoken
        /// alone. The ghost pass and the attachment pass can land on either side of the key event
        /// within a frame, so only a gap of two or more frames proves no pass is coming.
        /// </summary>
        private const int HoldFrames = 2;

        /// <summary>
        /// Whether a tile description held at <paramref name="heldFrame"/> has waited long enough
        /// that its draw pass is never going to arrive (the designator was dropped, or placement
        /// was cancelled in the same frame). Frame INEQUALITY is the only signal — a same-frame
        /// test would pre-empt the normal merge.
        /// </summary>
        internal static bool ShouldFlushHeld(int heldFrame, int currentFrame)
        {
            return currentFrame - heldFrame >= HoldFrames;
        }

        /// <summary>What <see cref="Decide"/> resolved to. A null <see cref="Utterance"/> says nothing.</summary>
        internal struct Decision
        {
            public string Utterance;

            /// <summary>The new value of the help channel's changed-since-last-time state.</summary>
            public string LastSpoken;

            /// <summary>True when the utterance carries a tile description, which sets its priority.</summary>
            public bool CarriesTile;
        }

        /// <param name="help">This pass's collected placement help, or null when it collected none.</param>
        /// <param name="heldTile">The tile description the cursor step handed over, or null.</param>
        /// <param name="lastSpoken">The help text spoken last time.</param>
        internal static Decision Decide(string help, string heldTile, string lastSpoken)
        {
            bool hasHelp = !string.IsNullOrEmpty(help);
            bool hasTile = !string.IsNullOrEmpty(heldTile);

            Decision decision;
            decision.CarriesTile = hasTile;

            if (!hasHelp)
            {
                // Nothing collected forgets the last help, so stepping out of range and back in
                // says the help again rather than deduping against a pass that no longer applies.
                decision.Utterance = hasTile ? heldTile : null;
                decision.LastSpoken = null;
                return decision;
            }

            if (help == lastSpoken)
            {
                decision.Utterance = hasTile ? heldTile : null;
                decision.LastSpoken = lastSpoken;
                return decision;
            }

            decision.Utterance = hasTile ? help + ". " + heldTile : help;
            decision.LastSpoken = help;
            return decision;
        }
    }
}
