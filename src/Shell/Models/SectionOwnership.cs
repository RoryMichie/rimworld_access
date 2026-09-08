using System.Collections.Generic;

namespace RimWorldAccess.Shell
{
    /// <summary>One captured row as <see cref="SectionOwnership"/> needs it, described without any GUI/game types.</summary>
    public struct SectionCandidate
    {
        /// <summary>True for a row that has been judged a section heading.</summary>
        public bool IsHeading;

        /// <summary>True for a row the window drew as its own chrome rather than as content — it belongs to no section and names none.</summary>
        public bool IsChrome;

        public float XMin;
        public float XMax;
        public float YMin;
    }

    /// <summary>
    /// Geometric section ownership: among every heading whose horizontal span overlaps a row's
    /// own and that sits above it, a row owns the NEAREST one — the same "share a column, stop at
    /// the next heading in that column" rule a sighted player applies. A heading in a column the
    /// row does not belong to never wins even if it is the last one drawn (Dubs Bad Hygiene draws
    /// its narrow "Quit to main menu to change" column entirely before its wide slider column, so
    /// sequential draw-order tracking handed that heading to the whole second column too). A row
    /// owns nothing (-1) when no overlapping heading sits above it — window-level/title context —
    /// which is exactly the case for a column that draws no heading of its own. A full-width
    /// heading overlaps every column beneath it, which degenerates to the old sequential result
    /// for a single-column window.
    ///
    /// A row the window drew as its own CHROME (the close-X, the bottom Close button,
    /// CommonSearchWidget — anything outside the group <c>DoWindowContents</c> runs inside) owns
    /// no section and, as a heading, names none: it is not part of the content a sighted player
    /// reads as sections at all.
    /// </summary>
    public static class SectionOwnership
    {
        /// <summary>
        /// One owning-heading index per row in <paramref name="rows"/>, index-aligned with the
        /// input; -1 for window-level context (no heading above the row shares its column).
        /// </summary>
        public static IReadOnlyList<int> ComputeOwners(IReadOnlyList<SectionCandidate> rows)
        {
            int n = rows == null ? 0 : rows.Count;
            var owners = new int[n];
            for (int i = 0; i < n; i++)
            {
                owners[i] = -1;
            }
            if (n == 0)
            {
                return owners;
            }

            for (int i = 0; i < n; i++)
            {
                SectionCandidate row = rows[i];
                if (row.IsHeading || row.IsChrome)
                {
                    continue;
                }
                int best = -1;
                float bestHeadingY = float.NegativeInfinity;
                for (int j = 0; j < n; j++)
                {
                    SectionCandidate candidate = rows[j];
                    if (!candidate.IsHeading || candidate.IsChrome)
                    {
                        continue;
                    }
                    if (candidate.YMin >= row.YMin)
                    {
                        continue; // not above this row at all
                    }
                    if (!XOverlaps(row, candidate))
                    {
                        continue; // a different column entirely
                    }
                    if (candidate.YMin > bestHeadingY)
                    {
                        bestHeadingY = candidate.YMin;
                        best = j;
                    }
                }
                owners[i] = best;
            }
            return owners;
        }

        private static bool XOverlaps(SectionCandidate a, SectionCandidate b)
        {
            return a.XMin < b.XMax && a.XMax > b.XMin;
        }
    }
}
