using System;
using System.Collections.Generic;

namespace RimWorldAccess
{
    /// <summary>
    /// Pure text-splitting helpers shared by the inspection tree adapters
    /// (rework Part B). Callers strip rich-text tags before splitting; this
    /// layer stays free of game dependencies so it can be unit tested.
    /// </summary>
    internal static class InspectTextUtility
    {
        /// <summary>
        /// Splits a multi-line blob on \n / \r into trimmed, non-empty lines.
        /// When <paramref name="redundantWithLabel"/> is set, lines that repeat
        /// the label (ignoring case and trailing ':', '.', ' ') are dropped —
        /// the shared form of the redundancy-stripping the tree builders each
        /// hand-rolled around explanation blobs.
        /// </summary>
        public static List<string> SplitLines(string text, string redundantWithLabel = null)
        {
            var result = new List<string>();
            if (string.IsNullOrWhiteSpace(text))
                return result;

            string normLabel = NormalizeForRedundancy(redundantWithLabel);
            foreach (string raw in text.Split(new[] { '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries))
            {
                string line = raw.Trim();
                if (line.Length == 0)
                    continue;
                if (normLabel != null && NormalizeForRedundancy(line) == normLabel)
                    continue;
                result.Add(line);
            }
            return result;
        }

        /// <summary>
        /// True when <paramref name="line"/> merely repeats <paramref name="label"/>
        /// (ignoring case and trailing ':', '.', ' ').
        /// </summary>
        public static bool IsRedundantWith(string line, string label)
        {
            string a = NormalizeForRedundancy(line);
            string b = NormalizeForRedundancy(label);
            return a != null && b != null && a == b;
        }

        private static string NormalizeForRedundancy(string s)
        {
            if (string.IsNullOrEmpty(s))
                return null;
            string trimmed = s.TrimEnd(':', '.', ' ');
            return trimmed.Length == 0 ? null : trimmed.ToLowerInvariant();
        }
    }
}
