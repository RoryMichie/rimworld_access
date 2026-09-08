namespace RimWorldAccess.Shell
{
    /// <summary>Which way a glyph-only button points — see <see cref="GlyphButtonNames"/>.</summary>
    public enum GlyphDirection
    {
        None,
        Up,
        Down,
        Left,
        Right,
    }

    /// <summary>
    /// Recognises a button whose entire caption is a DIRECTIONAL GLYPH.
    ///
    /// A caller that needs a small "move this up" or "go back" control has two
    /// ways to draw it: an icon, or a triangle/arrow character used as one.
    /// The icon route already ends up named, because a texture has an asset
    /// name and usually a tooltip. The character route ends up named too —
    /// with the character itself, which a screen reader dutifully reads out as
    /// its Unicode name. "Black up-pointing triangle, button" tells a player
    /// nothing about what the button does, and a column of them tells them
    /// nothing about which is which.
    ///
    /// So the glyph is treated as what it is: a picture of a direction, given
    /// the name of that direction. This is the same thing the reader already
    /// does for a button drawn as a texture, and it is not interpretation of
    /// the caller's INTENT — nothing here claims the button reorders anything,
    /// only that it points up. A caption with any real text in it is left
    /// completely alone; only a caption that is nothing but arrowheads
    /// qualifies, so a label that merely contains an arrow ("Sort ▼") keeps
    /// every character the caller wrote.
    ///
    /// The glyph sets are Unicode only, deliberately: an ASCII caret or "v"
    /// is a character a caller might mean literally, while nothing writes a
    /// lone U+25B2 except as an arrowhead.
    /// </summary>
    public static class GlyphButtonNames
    {
        private const string UpGlyphs = "▲△▴▵↑⬆⇑";
        private const string DownGlyphs = "▼▽▾▿↓⬇⇓";
        private const string LeftGlyphs = "◀◁◂◃←⬅⇐‹«";
        private const string RightGlyphs = "▶▷▸▹→➡⇒›»";

        /// <summary>
        /// The direction <paramref name="label"/> points, or
        /// <see cref="GlyphDirection.None"/> when it is not a glyph-only
        /// caption. Whitespace and Unicode variation selectors are ignored
        /// (a caller may pad a glyph for centring, and an emoji-presentation
        /// arrow carries a trailing selector); everything that remains must
        /// belong to one direction's set.
        /// </summary>
        public static GlyphDirection Classify(string label)
        {
            if (string.IsNullOrEmpty(label))
            {
                return GlyphDirection.None;
            }
            GlyphDirection found = GlyphDirection.None;
            bool sawGlyph = false;
            for (int i = 0; i < label.Length; i++)
            {
                char c = label[i];
                if (char.IsWhiteSpace(c) || IsIgnorable(c))
                {
                    continue;
                }
                GlyphDirection here = DirectionOf(c);
                if (here == GlyphDirection.None || (sawGlyph && here != found))
                {
                    return GlyphDirection.None;
                }
                found = here;
                sawGlyph = true;
            }
            return sawGlyph ? found : GlyphDirection.None;
        }

        private static bool IsIgnorable(char c)
        {
            // Variation selectors 15/16 (text vs emoji presentation) and the
            // zero-width joiner ride along with arrow characters without
            // changing which way they point.
            return c == '︎' || c == '️' || c == '‍' || c == '​';
        }

        private static GlyphDirection DirectionOf(char c)
        {
            if (UpGlyphs.IndexOf(c) >= 0)
            {
                return GlyphDirection.Up;
            }
            if (DownGlyphs.IndexOf(c) >= 0)
            {
                return GlyphDirection.Down;
            }
            if (LeftGlyphs.IndexOf(c) >= 0)
            {
                return GlyphDirection.Left;
            }
            if (RightGlyphs.IndexOf(c) >= 0)
            {
                return GlyphDirection.Right;
            }
            return GlyphDirection.None;
        }
    }
}
