using System.Globalization;
using System.Text;

namespace RimWorldAccess
{
    /// <summary>
    /// Unicode-aware text normalization helpers. Used by typeahead matching so that
    /// "cafe" matches "café" — a French/German/Spanish player can type without
    /// typing accented characters and still find items.
    /// </summary>
    public static class TextNormalization
    {
        /// <summary>
        /// Strip combining diacritics (NFD decomposition + drop NonSpacingMark code points)
        /// and expand the few Latin ligatures that don't decompose cleanly. Pattern from
        /// OniAccess's StringUtil.RemoveDiacritics.
        /// </summary>
        public static string RemoveDiacritics(string text)
        {
            if (string.IsNullOrEmpty(text)) return text ?? string.Empty;

            string decomposed = text.Normalize(NormalizationForm.FormD);
            var sb = new StringBuilder(decomposed.Length);
            for (int i = 0; i < decomposed.Length; i++)
            {
                char c = decomposed[i];
                switch (c)
                {
                    case 'œ':
                    case 'Œ':
                        sb.Append("oe");
                        break;
                    case 'æ':
                    case 'Æ':
                        sb.Append("ae");
                        break;
                    case 'ß':
                        sb.Append("ss");
                        break;
                    default:
                        if (CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark)
                            sb.Append(c);
                        break;
                }
            }
            return sb.ToString().Normalize(NormalizationForm.FormC);
        }

        /// <summary>
        /// Replaces unpaired UTF-16 surrogates with U+FFFD. Game/mod code that
        /// truncates strings can split an emoji's surrogate pair; a lone surrogate
        /// cannot encode to valid UTF-8, and the Prism native layer rejects such an
        /// utterance outright (InvalidUtf8), silently dropping the speech.
        /// </summary>
        public static string ReplaceLoneSurrogates(string text)
        {
            int firstBad = -1;
            for (int i = 0; i < text.Length; i++)
            {
                char c = text[i];
                if (char.IsHighSurrogate(c) && i + 1 < text.Length && char.IsLowSurrogate(text[i + 1]))
                {
                    i++;
                    continue;
                }
                if (char.IsSurrogate(c))
                {
                    firstBad = i;
                    break;
                }
            }

            if (firstBad < 0)
            {
                return text;
            }

            var chars = text.ToCharArray();
            for (int i = firstBad; i < chars.Length; i++)
            {
                char c = chars[i];
                if (char.IsHighSurrogate(c) && i + 1 < chars.Length && char.IsLowSurrogate(chars[i + 1]))
                {
                    i++;
                    continue;
                }
                if (char.IsSurrogate(c))
                {
                    chars[i] = '\uFFFD';
                }
            }
            return new string(chars);
        }
    }
}
