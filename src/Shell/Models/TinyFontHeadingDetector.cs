using System.Collections.Generic;

namespace RimWorldAccess.Shell
{
    /// <summary>One captured row, in DRAW order, as <see cref="TinyFontHeadingDetector"/> needs it — described without any GUI/game types.</summary>
    public struct TinyHeadingCandidateRow
    {
        public bool IsLabel;

        /// <summary>Only meaningful when <see cref="IsLabel"/>: this Label was drawn in the tiny font tier.</summary>
        public bool IsTinyFont;

        /// <summary>Only meaningful when NOT <see cref="IsLabel"/>: a real operable control (never a read-only display like a FillableBar).</summary>
        public bool IsInteractive;

        public int ClipId;
        public float Y;
        public float Height;
    }

    /// <summary>
    /// Recognises a panel heading drawn in <c>GameFont.Tiny</c> above a
    /// background panel (Colony Manager Redux's <c>Widgets_Section.Section</c> draws "Target
    /// resource"/"Allowed animals"/"Threshold" this way) — the reader's existing heading tiers
    /// are <c>GameFont.Medium</c> (strong) and a Label immediately after a
    /// <c>Listing.GapLine</c> (weak), neither of which Tiny matches, so these headers read as
    /// plain content rows and every control under them announced with no section context.
    ///
    /// A Tiny-font Label qualifies as a heading only when BOTH:
    /// <list type="bullet">
    /// <item>Tiny is a STRICT MINORITY of this pass's Label rows — when most labels in the pass
    /// are Tiny, the font carries no heading signal at all (a mod that simply draws everything in
    /// Tiny is not using it to mark section breaks).</item>
    /// <item>the first row that follows it IN THE SAME CLIP is a real operable control (never
    /// another Label, never a read-only display), starting within roughly one row height below
    /// it — a Tiny label with nothing actionable directly beneath it is not a heading over
    /// anything.</item>
    /// </list>
    /// Qualifying rows are treated exactly like the existing weak (GapLine) tier by the caller:
    /// same revocation when a control overlaps the guess, same section-ownership and fusion-
    /// boundary rules.
    /// </summary>
    public static class TinyFontHeadingDetector
    {
        /// <summary>One verdict per row in <paramref name="rows"/>, index-aligned with the input; false for every row that is not a qualifying Tiny-font heading.</summary>
        public static IReadOnlyList<bool> FindHeadings(IReadOnlyList<TinyHeadingCandidateRow> rows)
        {
            int n = rows == null ? 0 : rows.Count;
            var result = new bool[n];
            if (n == 0)
            {
                return result;
            }

            int totalLabels = 0;
            int tinyLabels = 0;
            for (int i = 0; i < n; i++)
            {
                if (rows[i].IsLabel)
                {
                    totalLabels++;
                    if (rows[i].IsTinyFont)
                    {
                        tinyLabels++;
                    }
                }
            }
            if (totalLabels == 0 || tinyLabels * 2 >= totalLabels)
            {
                // Tiny is not a strict minority (or is tied/predominant) -- carries no signal.
                return result;
            }

            for (int i = 0; i < n; i++)
            {
                TinyHeadingCandidateRow row = rows[i];
                if (!row.IsLabel || !row.IsTinyFont)
                {
                    continue;
                }
                for (int j = i + 1; j < n; j++)
                {
                    TinyHeadingCandidateRow next = rows[j];
                    if (next.ClipId != row.ClipId)
                    {
                        continue; // an interleaved row from a different pane -- keep looking
                    }
                    if (next.IsLabel && next.IsTinyFont)
                    {
                        // Another TINY label is the very next thing in this clip:
                        // the candidate is one of a run of tiny siblings (an area
                        // picker's row of tiny names), not a header over content.
                        break;
                    }
                    // Section content is an operable control OR a non-tiny label:
                    // this runs BEFORE caption fusion, so a checkbox's caption is
                    // still a raw Label row of its own and must qualify just like
                    // the control it will fuse into -- breaking on any label is
                    // what rejected every real Colony Manager Redux header (caught
                    // live). A read-only display (FillableBar) still never
                    // qualifies: a tiny label directly above one is that bar's own
                    // caption, not a header over a section.
                    if (next.IsInteractive || (next.IsLabel && !next.IsTinyFont))
                    {
                        float gap = next.Y - (row.Y + row.Height);
                        result[i] = gap < row.Height;
                    }
                    break;
                }
            }
            return result;
        }
    }
}
