using System;

namespace RimWorldAccess
{
    /// <summary>
    /// The cursor arithmetic every text buffer in the mod is read with: word
    /// boundaries, line bounds, the word under the cursor, and the column-preserving
    /// line step. <see cref="TextInputController"/> moves its caret with these, so a
    /// field the player types into and a read-only one they only read (a
    /// <see cref="TextFieldSpec.ReadOnlyText"/> buffer, e.g. the dev log's message
    /// details) agree on what "one word left" means.
    ///
    /// A word is a run of letters or digits; everything else separates words. Positions
    /// are caret positions, 0..length, and every entry point clamps rather than throws.
    ///
    /// PURE: links into the test project.
    /// </summary>
    public static class TextCursor
    {
        /// <summary>Caret after the word the caret is in (or the next one), the far side of the separators that follow it.</summary>
        public static int NextWordBoundary(string text, int from)
        {
            text = text ?? string.Empty;
            int i = Clamp(text, from);
            while (i < text.Length && char.IsLetterOrDigit(text[i])) i++;
            while (i < text.Length && !char.IsLetterOrDigit(text[i])) i++;
            return i;
        }

        /// <summary>Caret at the start of the word before the caret, skipping the separators in between.</summary>
        public static int PreviousWordBoundary(string text, int from)
        {
            text = text ?? string.Empty;
            int i = Clamp(text, from);
            while (i > 0 && !char.IsLetterOrDigit(text[i - 1])) i--;
            while (i > 0 && char.IsLetterOrDigit(text[i - 1])) i--;
            return i;
        }

        public static int StartOfLine(string text, int pos)
        {
            text = text ?? string.Empty;
            if (pos <= 0) return 0;
            int i = Math.Min(pos, text.Length) - 1;
            while (i >= 0 && text[i] != '\n') i--;
            return i + 1;
        }

        /// <summary>Caret at the line's newline (or the end of the text), so the line is [StartOfLine, EndOfLine).</summary>
        public static int EndOfLine(string text, int pos)
        {
            text = text ?? string.Empty;
            int i = Clamp(text, pos);
            while (i < text.Length && text[i] != '\n') i++;
            return i;
        }

        /// <summary>The line the caret is on, without its newline.</summary>
        public static string LineAt(string text, int pos)
        {
            text = text ?? string.Empty;
            int start = StartOfLine(text, pos);
            int end = EndOfLine(text, start);
            return start >= end ? string.Empty : text.Substring(start, end - start);
        }

        /// <summary>
        /// One line up, keeping the column where the shorter line allows it. On the first
        /// line the caret snaps to the start of the text, mirroring what a text field does
        /// when Up has nowhere left to go.
        /// </summary>
        public static int LineUp(string text, int pos)
        {
            text = text ?? string.Empty;
            int lineStart = StartOfLine(text, pos);
            if (lineStart == 0) return 0;
            int col = Clamp(text, pos) - lineStart;
            int previousStart = StartOfLine(text, lineStart - 1);
            int previousLength = (lineStart - 1) - previousStart; // excludes the newline
            return previousStart + Math.Min(col, previousLength);
        }

        /// <summary>One line down, keeping the column; on the last line the caret snaps to the end of the text.</summary>
        public static int LineDown(string text, int pos)
        {
            text = text ?? string.Empty;
            int lineStart = StartOfLine(text, pos);
            int lineEnd = EndOfLine(text, pos);
            if (lineEnd >= text.Length) return text.Length;
            int col = Clamp(text, pos) - lineStart;
            int nextStart = lineEnd + 1;
            int nextLength = EndOfLine(text, nextStart) - nextStart;
            return nextStart + Math.Min(col, nextLength);
        }

        /// <summary>The word starting at this caret, empty when the caret is not on a word character.</summary>
        public static string WordAt(string text, int pos)
        {
            text = text ?? string.Empty;
            if (pos < 0 || pos >= text.Length) return string.Empty;
            if (!char.IsLetterOrDigit(text[pos])) return string.Empty;
            int end = pos;
            while (end < text.Length && char.IsLetterOrDigit(text[end])) end++;
            return text.Substring(pos, end - pos);
        }

        public static string FirstWord(string text)
        {
            text = text ?? string.Empty;
            int i = 0;
            while (i < text.Length && !char.IsLetterOrDigit(text[i])) i++;
            return WordAt(text, i);
        }

        public static string LastWord(string text)
        {
            text = text ?? string.Empty;
            int i = text.Length - 1;
            while (i >= 0 && !char.IsLetterOrDigit(text[i])) i--;
            if (i < 0) return string.Empty;
            int end = i + 1;
            while (i >= 0 && char.IsLetterOrDigit(text[i])) i--;
            return text.Substring(i + 1, end - i - 1);
        }

        private static int Clamp(string text, int pos)
        {
            return Math.Max(0, Math.Min(pos, text.Length));
        }
    }
}
