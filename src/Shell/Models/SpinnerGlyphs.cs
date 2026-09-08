namespace RimWorldAccess.Shell
{
    /// <summary>
    /// Recognises the parts of a HAND-ROLLED spinner. A caller wanting a small numeric
    /// up/down control has two routes: Widgets.IntEntry and Listing_Standard.IntAdjuster
    /// are bracketed composites the capture pass marks, so those fold by role; a caller
    /// that instead draws two one-character text buttons around its own field or number
    /// produces unrelated rows, and the generic value-button rule then pairs each arrow
    /// with whatever text sits nearest. This is the geometry-free half of telling that run
    /// apart: which captions are signs, and which text is a bare number.
    ///
    /// Every test is exact-match on the whole trimmed caption, never "contains": a button
    /// labelled "Add +5" is a real button with real words and keeps every one of them.
    /// Deliberately NOT folded into GlyphButtonNames -- that class answers "which direction
    /// does this arrowhead point", a different question over a different vocabulary, and
    /// merging them would let a triangle pair masquerade as a numeric spinner.
    /// </summary>
    public static class SpinnerGlyphs
    {
        /// <summary>True when the whole trimmed caption is a single decrement sign: '-' (U+002D) or '−' (U+2212).</summary>
        public static bool IsDecrement(string label)
        {
            if (string.IsNullOrEmpty(label))
            {
                return false;
            }
            string trimmed = label.Trim();
            return trimmed == "-" || trimmed == "−";
        }

        /// <summary>True when the whole trimmed caption is a single increment sign: '+' (U+002B).</summary>
        public static bool IsIncrement(string label)
        {
            if (string.IsNullOrEmpty(label))
            {
                return false;
            }
            return label.Trim() == "+";
        }

        /// <summary>
        /// True when the whole trimmed text is a bare number a spinner would show: an
        /// optional leading sign, digits, an optional single decimal separator ('.' or
        /// ','), and an optional trailing '%'. No letters, no words, no ranges. Scans
        /// characters directly -- never double.Parse, whose behaviour depends on the
        /// ambient culture.
        /// </summary>
        public static bool IsBareNumber(string text)
        {
            if (string.IsNullOrEmpty(text))
            {
                return false;
            }
            string trimmed = text.Trim();
            if (trimmed.Length == 0)
            {
                return false;
            }
            int i = 0;
            if (trimmed[i] == '+' || trimmed[i] == '-')
            {
                i++;
            }
            bool sawDigit = false;
            bool sawSeparator = false;
            for (; i < trimmed.Length; i++)
            {
                char c = trimmed[i];
                if (c >= '0' && c <= '9')
                {
                    sawDigit = true;
                    continue;
                }
                if ((c == '.' || c == ',') && !sawSeparator)
                {
                    sawSeparator = true;
                    continue;
                }
                if (c == '%' && i == trimmed.Length - 1)
                {
                    continue;
                }
                return false;
            }
            return sawDigit;
        }
    }
}
