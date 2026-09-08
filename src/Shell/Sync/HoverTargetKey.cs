using System;

namespace RimWorldAccess.Shell
{
    /// <summary>
    /// Identifies the thing the pointer is resting on, so
    /// <c>HoverSpeech</c> can announce on TARGET change rather than on text
    /// change. Two different rows that happen to read identically produce
    /// different keys and therefore both speak — the no-dedupe law. The band
    /// and member ordinals locate the target within the pass; the kind and raw
    /// label keep the key stable when a surface adds or removes a row above
    /// the hovered one.
    /// </summary>
    public readonly struct HoverTargetKey : IEquatable<HoverTargetKey>
    {
        /// <summary>Index of the hit widget's band within this pass's banding.</summary>
        public readonly int BandOrdinal;

        /// <summary>Index within the folded row's expansion; -1 for the row's plain text row.</summary>
        public readonly int MemberOrdinal;

        /// <summary>
        /// Index of the hit widget within the pass when the fold was narrowed to
        /// that one cell of its band (an unlabeled click target that draws its
        /// own caption inside itself — see <c>HoverSpeech.NarrowToHitCell</c>);
        /// -1 whenever the whole band was folded. Without it every cell of such
        /// a band shares one key, and sweeping along a row of them would speak
        /// only the first.
        /// </summary>
        public readonly int CellOrdinal;

        public readonly CapturedExtraKind Kind;
        public readonly string RawLabel;

        public HoverTargetKey(int bandOrdinal, int memberOrdinal, CapturedExtraKind kind, string rawLabel)
            : this(bandOrdinal, memberOrdinal, -1, kind, rawLabel)
        {
        }

        public HoverTargetKey(int bandOrdinal, int memberOrdinal, int cellOrdinal, CapturedExtraKind kind, string rawLabel)
        {
            BandOrdinal = bandOrdinal;
            MemberOrdinal = memberOrdinal;
            CellOrdinal = cellOrdinal;
            Kind = kind;
            RawLabel = rawLabel ?? "";
        }

        /// <summary>The "pointing at nothing" key — never equal to any real target.</summary>
        public static HoverTargetKey None
        {
            get { return new HoverTargetKey(-1, -1, CapturedExtraKind.Label, null); }
        }

        public bool Equals(HoverTargetKey other)
        {
            return BandOrdinal == other.BandOrdinal
                && MemberOrdinal == other.MemberOrdinal
                && CellOrdinal == other.CellOrdinal
                && Kind == other.Kind
                && string.Equals(RawLabel, other.RawLabel, StringComparison.Ordinal);
        }

        public override bool Equals(object obj)
        {
            return obj is HoverTargetKey && Equals((HoverTargetKey)obj);
        }

        public override int GetHashCode()
        {
            int hash = BandOrdinal;
            hash = (hash * 397) ^ MemberOrdinal;
            hash = (hash * 397) ^ CellOrdinal;
            hash = (hash * 397) ^ (int)Kind;
            return (hash * 397) ^ RawLabel.GetHashCode();
        }
    }
}
