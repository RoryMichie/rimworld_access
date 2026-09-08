namespace RimWorldAccess
{
    /// <summary>
    /// Pure predicate shared by <c>CapturedRowFolder</c>'s glyph-button qualifier: mod UI is full of
    /// single-glyph icon buttons ("×", "+", "−") named only by a tooltip that IS the glyph itself
    /// (Languages/English/Keyed/CashRegister.xml's TabRegisterShiftsAdd/Remove keys). A screen reader
    /// renders such a name as noise or nothing, so a captured row qualifies it with context instead of
    /// speaking it bare. No game dependencies, so it is covered directly by tests.
    /// </summary>
    public static class GlyphButtonName
    {
        /// <summary>
        /// True when <paramref name="name"/>, trimmed, is 1-2 characters long
        /// and contains no letter or digit — i.e. it is punctuation/symbol
        /// only and carries no word a screen reader could pronounce.
        /// </summary>
        public static bool IsGlyphOnlyName(string name)
        {
            if (string.IsNullOrEmpty(name))
                return false;

            string trimmed = name.Trim();
            if (trimmed.Length == 0 || trimmed.Length > 2)
                return false;

            foreach (char c in trimmed)
            {
                if (char.IsLetterOrDigit(c))
                    return false;
            }
            return true;
        }
    }
}
