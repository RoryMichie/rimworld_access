using System;
using System.Text;

namespace RimWorldAccess
{
    /// <summary>
    /// Shared text folding for capture-vs-tree comparison. The DEBUG
    /// completeness oracle and the production parity-diff
    /// engine (<see cref="CaptureParityDiff"/>) both reduce captured fragments
    /// and tree text to the same canonical form through these two methods, so
    /// an oracle finding and a production "Also shown" row can never disagree.
    /// Pure BCL: callers strip rich-text tags (a Verse extension) before
    /// handing text in.
    /// </summary>
    internal static class CaptureTextNormalization
    {
        /// <summary>
        /// Case-, punctuation- and whitespace-insensitive form: keeps only
        /// letters and digits, lowercased. Tags must already be stripped by the
        /// caller (this method touches no game types).
        /// </summary>
        internal static string NormalizeForContainment(string text)
        {
            if (string.IsNullOrEmpty(text))
            {
                return "";
            }
            var sb = new StringBuilder(text.Length);
            foreach (char c in text)
            {
                if (char.IsLetterOrDigit(c))
                {
                    sb.Append(char.ToLowerInvariant(c));
                }
            }
            return sb.ToString();
        }

        /// <summary>
        /// The per-line half of the capture fragment extractor: trims, drops a
        /// trailing truncation ellipsis (Text.Truncate clips to the drawn rect;
        /// the tree presents the full string), and rejects a result shorter
        /// than three characters. Returns null when the line carries no
        /// comparable content.
        /// </summary>
        internal static string NormalizeFragmentLine(string rawLine)
        {
            if (rawLine == null)
            {
                return null;
            }
            string line = rawLine.Trim();
            if (line.EndsWith("..."))
            {
                line = line.Substring(0, line.Length - 3);
            }
            if (line.Length < 3)
            {
                return null;
            }
            return line;
        }

        /// <summary>
        /// True when <paramref name="text"/> carries at least one line whose
        /// normalized form is absent from <paramref name="normalizedHaystack"/>
        /// (itself already run through <see cref="NormalizeForContainment"/>).
        /// The shared "is this unmirrored" test — <see cref="CaptureParityDiff"/>'s
        /// own <c>WidgetIsUnmirrored</c> and ScreenScope's captured-extras diff
        /// both call this rather than each re-deriving the per-line split/trim/
        /// tag-strip/normalize sequence. <paramref name="stripTags"/> is a
        /// callback rather than a direct call because <c>StripTags()</c> is a
        /// Verse extension method this pure class cannot reference.
        /// </summary>
        internal static bool IsUnmirrored(string text, string normalizedHaystack, Func<string, string> stripTags)
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                return false;
            }
            foreach (string rawLine in text.Split('\n'))
            {
                string line = NormalizeFragmentLine(rawLine);
                if (line == null)
                {
                    continue;
                }
                string normalized = NormalizeForContainment(stripTags(line));
                if (normalized.Length > 0 && !normalizedHaystack.Contains(normalized))
                {
                    return true;
                }
            }
            return false;
        }
    }
}
