using System;
using System.Collections.Generic;

namespace RimWorldAccess.Shell
{
    /// <summary>
    /// Folds the tooltips a real pointer triggered (see
    /// <c>LiveTooltips</c>) into an element description's Extras fragment,
    /// without repeating anything the row already says.
    /// </summary>
    public static class TooltipTextJoin
    {
        /// <summary>Appends tips not already spoken by the row, in order, as sentences.</summary>
        public static string Append(string existing, IReadOnlyList<string> tips)
        {
            if (tips == null || tips.Count == 0)
            {
                return existing;
            }

            string joined = existing ?? "";
            for (int i = 0; i < tips.Count; i++)
            {
                string tip = tips[i] == null ? null : tips[i].Trim();
                // Containment covers both an earlier tip in this call and the
                // index-resolved tip the row already carries.
                if (string.IsNullOrEmpty(tip) || joined.IndexOf(tip, StringComparison.Ordinal) >= 0)
                {
                    continue;
                }
                if (joined.Length > 0)
                {
                    char last = joined[joined.Length - 1];
                    joined += last == '.' || last == '!' || last == '?' ? " " : ". ";
                }
                joined += tip;
            }
            return joined.Length == 0 ? existing : joined;
        }
    }
}
