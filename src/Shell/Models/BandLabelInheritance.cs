using System.Collections.Generic;

namespace RimWorldAccess.Shell
{
    /// <summary>One row <see cref="BandLabelInheritance"/> judges, described without any GUI/game types.</summary>
    public struct BandLabelCandidate
    {
        /// <summary>
        /// True for a row this rule ever touches as either a donor or an inheritor — a Button,
        /// Checkbox, or RadioButton. A plain Label (or anything else) is never operable, so it
        /// is never in play here even when it sits in the same band.
        /// </summary>
        public bool Interactive;

        /// <summary>True when this row already carries a non-empty label of its own.</summary>
        public bool HasLabel;

        /// <summary>Opaque identity of the row's clipping context — only equality is used, exactly like <see cref="TabStripDetector"/>'s ClipId.</summary>
        public int ClipId;

        public float X;
        public float Y;
        public float Width;
        public float Height;
    }

    /// <summary>
    /// One row's inheritance verdict: <see cref="DonorIndex"/> is the index (into the caller's
    /// input list) of the labeled row it inherits from, or -1 for no inheritance;
    /// <see cref="Ordinal"/> is the 1-based suffix to speak with that label (2 for the first
    /// inheritor after the donor, 3 for the next, ...), 0 when there is no donor.
    /// </summary>
    public struct BandLabelVerdict
    {
        public int DonorIndex;
        public int Ordinal;
    }

    /// <summary>
    /// Recognises the "Label + checkbox + checkbox" TWIN-CONTROL shape (RimTalk Persona
    /// Director's "Select Data sent to AI (Left: Name, Right:
    /// Description)" header): a caller draws one caption beside SEVERAL controls on one visual
    /// row, and the generic reader's ordinary label fusion attaches that caption to only the
    /// nearest control — every other control on the row announces with no label at all
    /// ("checkbox, not checked. 18 of 44").
    ///
    /// Runs AFTER every other fusion rule has already decided every row's label (the caller
    /// applies it last, right before the visual-order sort — see
    /// <see cref="GenericWindowScope.BuildPresentationRows"/>): any INTERACTIVE row that is
    /// STILL unlabeled inherits the label of the NEAREST interactive row to its LEFT that
    /// shares its clip and vertically overlaps its rect — the same visual row a sighted player
    /// reads the shared caption as belonging to — suffixed with a 1-based ordinal, 2 for the
    /// first inheritor and 3 for the next, in left-to-right order, so "Ideology" and
    /// "Ideology 2" read as the same family instead of one control losing its name outright.
    /// Never fires for a row that already has its own label; never crosses a clip or a band
    /// with no donor in it; the donor's own label is never touched (it is implicitly "1").
    /// </summary>
    public static class BandLabelInheritance
    {
        /// <summary>
        /// Computes one verdict per row in <paramref name="rows"/>, index-aligned with the
        /// input. Never null; a row with no verdict reports <c>DonorIndex == -1</c>.
        /// </summary>
        public static IReadOnlyList<BandLabelVerdict> Compute(IReadOnlyList<BandLabelCandidate> rows)
        {
            int n = rows == null ? 0 : rows.Count;
            var verdicts = new BandLabelVerdict[n];
            for (int i = 0; i < n; i++)
            {
                verdicts[i] = new BandLabelVerdict { DonorIndex = -1, Ordinal = 0 };
            }
            if (n == 0)
            {
                return verdicts;
            }

            // Nearest labeled interactive row to the left, in the same clip and band, for
            // every still-unlabeled interactive row.
            int[] donorFor = new int[n];
            for (int i = 0; i < n; i++)
            {
                donorFor[i] = -1;
                BandLabelCandidate row = rows[i];
                if (!row.Interactive || row.HasLabel)
                {
                    continue;
                }
                int best = -1;
                float bestX = 0f;
                for (int j = 0; j < n; j++)
                {
                    if (j == i)
                    {
                        continue;
                    }
                    BandLabelCandidate candidate = rows[j];
                    if (!candidate.Interactive || !candidate.HasLabel)
                    {
                        continue;
                    }
                    if (candidate.ClipId != row.ClipId || candidate.X >= row.X)
                    {
                        continue;
                    }
                    if (!YOverlaps(row, candidate))
                    {
                        continue;
                    }
                    if (best < 0 || candidate.X > bestX)
                    {
                        best = j;
                        bestX = candidate.X;
                    }
                }
                donorFor[i] = best;
            }

            // Ordinals: group inheritors by donor, number 2, 3, ... in left-to-right order.
            for (int donor = 0; donor < n; donor++)
            {
                if (!rows[donor].Interactive || !rows[donor].HasLabel)
                {
                    continue;
                }
                List<int> inheritors = null;
                for (int i = 0; i < n; i++)
                {
                    if (donorFor[i] != donor)
                    {
                        continue;
                    }
                    if (inheritors == null)
                    {
                        inheritors = new List<int>();
                    }
                    inheritors.Add(i);
                }
                if (inheritors == null)
                {
                    continue;
                }
                inheritors.Sort((a, b) => rows[a].X.CompareTo(rows[b].X));
                for (int k = 0; k < inheritors.Count; k++)
                {
                    verdicts[inheritors[k]] = new BandLabelVerdict { DonorIndex = donor, Ordinal = k + 2 };
                }
            }

            return verdicts;
        }

        private static bool YOverlaps(BandLabelCandidate a, BandLabelCandidate b)
        {
            return a.Y < b.Y + b.Height && b.Y < a.Y + a.Height;
        }
    }
}
