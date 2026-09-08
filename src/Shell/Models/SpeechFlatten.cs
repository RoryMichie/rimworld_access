using System.Text;

namespace RimWorldAccess.Shell
{
    /// <summary>
    /// Flattens multi-line game text into one period-separated line for announcement. Def
    /// descriptions arrive with tab-indented sub-lines and blank-line separated sections
    /// (AbilityDef.GetDescription and kin build them with StringBuilder.AppendLine); spoken
    /// text never carries newlines as separators.
    /// </summary>
    public static class SpeechFlatten
    {
        /// <summary>Null when the input has no speakable content.</summary>
        public static string ToSentences(string raw)
        {
            if (string.IsNullOrEmpty(raw))
            {
                return null;
            }

            var builder = new StringBuilder();
            foreach (string line in raw.Replace("\r", "").Replace("\t", " ").Split('\n'))
            {
                string trimmed = line.Trim();
                if (trimmed.Length == 0)
                {
                    continue;
                }
                if (builder.Length > 0)
                {
                    // A colon-terminated line introduces the next one, so a period between
                    // them would be read as a false sentence break ("Click to jump to:.").
                    char last = builder[builder.Length - 1];
                    builder.Append(IsTerminal(last) || last == ':' ? " " : ". ");
                }
                builder.Append(trimmed);
            }

            string joined = builder.ToString();
            if (joined.Length == 0)
            {
                return null;
            }

            while (joined.Contains(". ."))
            {
                joined = joined.Replace(". .", ".");
            }

            if (!IsTerminal(joined[joined.Length - 1]))
            {
                joined += ".";
            }
            return joined;
        }

        private static bool IsTerminal(char c)
        {
            return c == '.' || c == '!' || c == '?';
        }
    }
}
