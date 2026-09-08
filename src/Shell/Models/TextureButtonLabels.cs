using System.Collections.Generic;
using System.Text;

namespace RimWorldAccess.Shell
{
    /// <summary>
    /// Names an icon-only button by its TEXTURE ASSET NAME:
    /// the last-resort tier of <c>WidgetCapture.ImageButtonLabel</c>, reached when nothing —
    /// not a tooltip, not a registered tip, not the shared close/delete special cases — named
    /// the button synchronously. Before this, that tier spoke the raw asset name verbatim
    /// ("ReorderDown. button. 32 of 32"), which is either a common vanilla glyph a player has
    /// no reason to know the internal name of, or an arbitrary mod asset filename.
    ///
    /// Two tiers, tried in order by the caller: <see cref="TryGetGlyphKeySuffix"/> recognises a
    /// SHORT LIST of well-known vanilla TexButton names (mods routinely draw these same
    /// textures for their own reorder/delete/copy rows) and hands back the Keyed suffix
    /// (<c>RimWorldAccess.Shell.Glyph.&lt;suffix&gt;</c>) for the caller to translate; anything
    /// else falls to <see cref="Humanize"/>, a pure CamelCase/underscore word-splitter
    /// ("SomeOddIcon" -&gt; "Some Odd Icon") so even an unrecognised mod asset name reads as
    /// words instead of a filename.
    ///
    /// PURE: no game APIs, no translation — the caller resolves the Keyed key.
    /// </summary>
    public static class TextureButtonLabels
    {
        // Texture asset name -> RimWorldAccess.Shell.Glyph.<suffix> key suffix. Several vanilla
        // TexButton fields share an output (DeleteX/Delete both read as "Delete"; Info/
        // InfoButton both read as "Info" — TexButton.Info's own asset is literally named
        // "InfoButton") so this maps by the STRING a mod's own asset might use just as much as
        // by vanilla's own field names, matching the generic-first mandate: a mod drawing its
        // own "ReorderDown.png" gets the same real label vanilla's own reorder rail does.
        private static readonly Dictionary<string, string> KnownGlyphs = new Dictionary<string, string>
        {
            { "ReorderUp", "ReorderUp" },
            { "ReorderDown", "ReorderDown" },
            { "Plus", "Plus" },
            { "Minus", "Minus" },
            { "DeleteX", "Delete" },
            { "Delete", "Delete" },
            { "CloseXSmall", "Close" },
            { "CloseXBig", "Close" },
            { "Copy", "Copy" },
            { "Paste", "Paste" },
            { "Rename", "Rename" },
            { "Info", "Info" },
            { "InfoButton", "Info" },
            { "Drag", "Drag" },
            { "DragHash", "Drag" },
            { "OpenInspector", "OpenInspector" },
            { "ToggleLog", "ToggleLog" },
        };

        /// <summary>
        /// The <c>RimWorldAccess.Shell.Glyph.&lt;suffix&gt;</c> key suffix for
        /// <paramref name="textureName"/>, or null when it names none of the well-known glyphs.
        /// </summary>
        public static string TryGetGlyphKeySuffix(string textureName)
        {
            if (string.IsNullOrEmpty(textureName))
            {
                return null;
            }
            string suffix;
            return KnownGlyphs.TryGetValue(textureName, out suffix) ? suffix : null;
        }

        /// <summary>
        /// Splits an unrecognised texture asset name into words for speech
        /// ("SomeOddIcon" -&gt; "Some Odd Icon", "some_odd_icon" -&gt; "some odd icon"): every
        /// underscore becomes a space and every lower-to-upper boundary gets one inserted, the
        /// same convention a filename-cased mod asset follows. Returns the input unchanged
        /// (never null) when it is null, empty, or carries neither an underscore nor a case
        /// boundary to split.
        /// </summary>
        public static string Humanize(string textureName)
        {
            if (string.IsNullOrEmpty(textureName))
            {
                return textureName ?? "";
            }
            string text = textureName.Replace('_', ' ');
            var sb = new StringBuilder(text.Length + 4);
            for (int i = 0; i < text.Length; i++)
            {
                char c = text[i];
                if (i > 0 && char.IsUpper(c) && char.IsLower(text[i - 1]))
                {
                    sb.Append(' ');
                }
                sb.Append(c);
            }
            return sb.ToString();
        }
    }
}
