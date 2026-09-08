namespace RimWorldAccess.Shell
{
    /// <summary>
    /// Removes a supplementary string's opening repetition of the label it accompanies. A
    /// row that speaks its name and then a description opening with that same name says it
    /// twice; a sighted player reads the name once, on the control, and the tooltip's own
    /// opening is a convention of writing tooltips that can stand alone. Whole-sentence
    /// only, so a description that genuinely begins with the same word ("Hotkeys are
    /// disabled while...") keeps every character.
    /// </summary>
    public static class RedundantLabelPrefix
    {
        private static readonly char[] SentenceBreaks = { '.', ':', '!', '?', '\n' };

        /// <summary>
        /// <paramref name="extras"/> with a leading occurrence of <paramref name="label"/>
        /// removed when it is followed by a sentence break ('.', ':', '!', '?', a newline,
        /// or the end of the string) -- and with that break and any following whitespace
        /// removed too. Returns the input unchanged in every other case, including a null
        /// or empty label.
        /// </summary>
        public static string Strip(string extras, string label)
        {
            if (string.IsNullOrEmpty(extras) || string.IsNullOrEmpty(label))
            {
                return extras;
            }

            string trimmed = extras.TrimStart();
            if (!trimmed.StartsWith(label, System.StringComparison.Ordinal))
            {
                return extras;
            }

            int afterLabel = label.Length;
            if (afterLabel == trimmed.Length)
            {
                return "";
            }

            char next = trimmed[afterLabel];
            bool isBreak = false;
            foreach (char c in SentenceBreaks)
            {
                if (next == c)
                {
                    isBreak = true;
                    break;
                }
            }
            if (!isBreak)
            {
                return extras;
            }

            string remainder = trimmed.Substring(afterLabel + 1).TrimStart();
            return remainder;
        }
    }
}
