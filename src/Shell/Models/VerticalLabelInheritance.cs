using System.Collections.Generic;

namespace RimWorldAccess.Shell
{
    /// <summary>One row's geometry as <see cref="VerticalLabelInheritance"/> needs it, described without any GUI/game types.</summary>
    public struct VerticalLabelCandidate
    {
        /// <summary>
        /// True for a row this rule will ever give a label to (a blank-labeled Slider, S6) — every
        /// other kind is never a RECIPIENT, though any row can still be a DONOR via
        /// <see cref="HasLabel"/> below.
        /// </summary>
        public bool Eligible;

        /// <summary>True when this row already carries a non-empty label of its own.</summary>
        public bool HasLabel;

        /// <summary>Opaque identity of the row's clipping context — only equality is used, exactly like <see cref="BandLabelCandidate.ClipId"/>.</summary>
        public int ClipId;

        public float X;
        public float Y;
        public float Width;
        public float Height;
    }

    /// <summary>
    /// Gives a blank-labeled Slider the caption of the labeled row
    /// directly ABOVE it in the same clip — Colony Manager Redux's threshold slider draws no
    /// caption of its own directly under its "Active if: 0 &lt; 500 (+0+0 expected)" trigger
    /// button, one row above at the same X and width, so the slider announced as bare
    /// "slider, 500" with nothing to attach the number to.
    ///
    /// A SEPARATE rule from <see cref="BandLabelInheritance"/> on purpose — that rule only ever
    /// looks LEFT within the same visual band (a caption shared by several controls on one row);
    /// this one looks straight UP a single column, and the donor's label is spoken AS-IS with no
    /// ordinal suffix, since there is exactly one recipient here, never a family of numbered
    /// twins. <see cref="RimWorldAccess.Shell.GenericWindowScope"/>'s own "own-label slider value
    /// rule" still speaks the slider's raw captured value alongside the inherited caption, since
    /// this rule only ever changes <c>PresentationRow.Label</c>, never the row's own captured
    /// (still blank) <c>CapturedWidget.Label</c> that rule reads.
    /// </summary>
    public static class VerticalLabelInheritance
    {
        /// <summary>
        /// One donor index per row in <paramref name="rows"/>, index-aligned with the input; -1
        /// for no donor (not an eligible recipient, already labeled, or no qualifying row sits
        /// directly above it).
        /// </summary>
        public static IReadOnlyList<int> ComputeDonors(IReadOnlyList<VerticalLabelCandidate> rows)
        {
            int n = rows == null ? 0 : rows.Count;
            var donors = new int[n];
            for (int i = 0; i < n; i++)
            {
                donors[i] = -1;
            }
            if (n == 0)
            {
                return donors;
            }

            for (int i = 0; i < n; i++)
            {
                VerticalLabelCandidate row = rows[i];
                if (!row.Eligible || row.HasLabel)
                {
                    continue;
                }
                int best = -1;
                float bestGap = float.PositiveInfinity;
                for (int j = 0; j < n; j++)
                {
                    if (j == i)
                    {
                        continue;
                    }
                    VerticalLabelCandidate candidate = rows[j];
                    if (!candidate.HasLabel || candidate.ClipId != row.ClipId)
                    {
                        continue;
                    }
                    float candidateBottom = candidate.Y + candidate.Height;
                    if (candidateBottom > row.Y)
                    {
                        continue; // not above this row at all
                    }
                    float gap = row.Y - candidateBottom;
                    if (gap >= row.Height)
                    {
                        continue; // further above than one row height -- not "directly above"
                    }
                    if (!XOverlaps(row, candidate))
                    {
                        continue; // a different column entirely
                    }
                    if (gap < bestGap)
                    {
                        bestGap = gap;
                        best = j;
                    }
                }
                donors[i] = best;
            }
            return donors;
        }

        private static bool XOverlaps(VerticalLabelCandidate a, VerticalLabelCandidate b)
        {
            return a.X < b.X + b.Width && b.X < a.X + a.Width;
        }
    }
}
