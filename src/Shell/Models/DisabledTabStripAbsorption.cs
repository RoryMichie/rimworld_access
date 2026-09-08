using System.Collections.Generic;

namespace RimWorldAccess.Shell
{
    /// <summary>One row's geometry as <see cref="DisabledTabStripAbsorption"/> needs it, described without any GUI/game types.</summary>
    public struct DisabledStripCandidate
    {
        /// <summary>Opaque identity of the row's clipping context — only equality is used, exactly like <see cref="BandLabelCandidate.ClipId"/>.</summary>
        public int ClipId;

        public float X;
        public float Y;
        public float Width;
        public float Height;
    }

    /// <summary>
    /// Recognises a strip's DISABLED members (Colony Manager Redux's
    /// research-gated "Power" tab draws as a plain textured Label, not through the widget the
    /// strip probe recognizes, so it stayed an ordinary content row and re-announced as the
    /// first row of EVERY page: "Power (disabled: Power management has not been researched).
    /// 1 of N"). A candidate row qualifies exactly when it shares a real strip member's clip,
    /// sits within the strip's own vertical band, and matches a strip member's cell size within
    /// a small epsilon — the same "looks like one of the tabs, just greyed out" test a sighted
    /// player applies without reading a single pixel of text.
    /// </summary>
    public static class DisabledTabStripAbsorption
    {
        private const float SizeEpsilon = 2f;

        /// <summary>
        /// One verdict per row in <paramref name="candidates"/>, index-aligned with the input:
        /// true when that row qualifies as an absorbed disabled stop. Never null; an empty or
        /// null <paramref name="stripMembers"/>/<paramref name="candidates"/> yields no matches.
        /// </summary>
        public static IReadOnlyList<bool> FindAbsorbedRows(
            IReadOnlyList<DisabledStripCandidate> stripMembers,
            IReadOnlyList<DisabledStripCandidate> candidates)
        {
            int n = candidates == null ? 0 : candidates.Count;
            var result = new bool[n];
            int m = stripMembers == null ? 0 : stripMembers.Count;
            if (m == 0 || n == 0)
            {
                return result;
            }

            float bandTop = float.PositiveInfinity;
            float bandBottom = float.NegativeInfinity;
            for (int i = 0; i < m; i++)
            {
                DisabledStripCandidate s = stripMembers[i];
                if (s.Y < bandTop)
                {
                    bandTop = s.Y;
                }
                if (s.Y + s.Height > bandBottom)
                {
                    bandBottom = s.Y + s.Height;
                }
            }

            for (int i = 0; i < n; i++)
            {
                DisabledStripCandidate c = candidates[i];
                if (!(c.Y < bandBottom && c.Y + c.Height > bandTop))
                {
                    // No vertical overlap with the strip's own band at all.
                    continue;
                }
                for (int j = 0; j < m; j++)
                {
                    DisabledStripCandidate s = stripMembers[j];
                    if (s.ClipId != c.ClipId)
                    {
                        continue;
                    }
                    if (WithinEpsilon(c.Width, s.Width) && WithinEpsilon(c.Height, s.Height))
                    {
                        result[i] = true;
                        break;
                    }
                }
            }
            return result;
        }

        private static bool WithinEpsilon(float a, float b)
        {
            float delta = a - b;
            return delta <= SizeEpsilon && delta >= -SizeEpsilon;
        }
    }
}
