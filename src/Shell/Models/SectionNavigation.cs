using System;

namespace RimWorldAccess.Shell
{
    /// <summary>
    /// Shared find-target logic for the flat-list section-jump grammar
    /// (PageUp/PageDown land on the first row of the adjacent section). Pure:
    /// callers own typeahead, cursor movement, sounds and announcements, so
    /// each screen keeps its exact side effects.
    ///
    /// PURE: links into the test project.
    /// </summary>
    public static class SectionNavigation
    {
        /// <summary>
        /// The first row of the section adjacent to <paramref name="index"/>'s,
        /// or -1 when there is none. Null section names compare as empty.
        /// Forward lands on the first row whose section differs; backward walks
        /// past the nearest differing row to that section's first contiguous
        /// row. With <paramref name="wrap"/>, a failed scan continues from the
        /// far end (forward: rows 0..index; backward: last row down to
        /// index+1), still landing on the found section's first row.
        /// </summary>
        public static int FindAdjacentSectionStart(
            int count, int index, Func<int, string> sectionOf, bool forward, bool wrap = false)
        {
            if (count == 0 || index < 0 || index >= count)
                return -1;

            string current = SectionAt(sectionOf, index);

            if (forward)
            {
                for (int i = index + 1; i < count; i++)
                {
                    if (!string.Equals(SectionAt(sectionOf, i), current, StringComparison.Ordinal))
                        return i;
                }
                if (wrap)
                {
                    for (int i = 0; i <= index; i++)
                    {
                        if (!string.Equals(SectionAt(sectionOf, i), current, StringComparison.Ordinal))
                            return i;
                    }
                }
                return -1;
            }

            for (int i = index - 1; i >= 0; i--)
            {
                if (!string.Equals(SectionAt(sectionOf, i), current, StringComparison.Ordinal))
                    return FirstOfSectionAt(sectionOf, i);
            }
            if (wrap)
            {
                for (int i = count - 1; i > index; i--)
                {
                    if (!string.Equals(SectionAt(sectionOf, i), current, StringComparison.Ordinal))
                        return FirstOfSectionAt(sectionOf, i);
                }
            }
            return -1;
        }

        private static int FirstOfSectionAt(Func<int, string> sectionOf, int i)
        {
            string section = SectionAt(sectionOf, i);
            int first = i;
            for (int j = i - 1; j >= 0; j--)
            {
                if (!string.Equals(SectionAt(sectionOf, j), section, StringComparison.Ordinal))
                    break;
                first = j;
            }
            return first;
        }

        private static string SectionAt(Func<int, string> sectionOf, int index)
        {
            return sectionOf(index) ?? "";
        }
    }
}
