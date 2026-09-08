using System;
using System.Text.RegularExpressions;

namespace RimWorldAccess
{
    /// <summary>
    /// Centralized text sanitization pipeline for screen reader output.
    /// Runs automatically in TolkHelper.Speak() before text reaches the screen reader.
    /// Handles tag stripping, punctuation cleanup, and whitespace normalization.
    /// </summary>
    public static class SpeechSanitizer
    {
        // This class is linked game-free into the test project, so it cannot call
        // TolkHelper directly; TolkHelper.Initialize() wires this to ReportScrubbedSpeech.
        internal static Action<string, string> ScrubReporter;

        // Model reasoning ("<think>" blocks) is deliberately NOT stripped here: speech mirrors
        // the screen, so reasoning a mod displays gets spoken and
        // reasoning a mod hides never reaches this pipeline in the first place. RimTalk Quests
        // always cleans its FINAL text itself (ThinkReasoningPostProcessor.ProcessFinal runs
        // unconditionally; its cleanThinkTagsDuringStreaming setting governs only the transient
        // stream, which the wait-for-settled rule keeps out of speech), and RimTalk core displays
        // whatever the model emitted -- both cases align by reading the displayed text as-is.
        // TagRegex below still drops the <think>/</think> wrapper like any other markup.
        private static readonly Regex TagRegex = new Regex(@"<[^>]+>", RegexOptions.Compiled);
        // Box-drawing runs used as section/stream-separator
        // dividers (RimTalk Quests' nine-glyph "───────────", ExpandMemory/PromptEnhance's "═══"/
        // "━━" headers) read as a wall of "box drawings light horizontal" noise. Three or more of the
        // same box-drawing character in a row collapses to a single sentence break; a lone glyph
        // (part of ordinary text) is left alone.
        private static readonly Regex BoxDrawingRunRegex = new Regex(@"[─-╿]{3,}", RegexOptions.Compiled);
        // A caption a caller ORNAMENTED with the same arrowhead at both ends ("▼ Show settings ▼",
        // HugsLib mod-settings section toggles) reads as "black down-pointing triangle, show
        // settings, black down-pointing triangle". The bracket is decoration by construction: a
        // glyph carrying state appears once, on the side it applies to, and the same caption here
        // is drawn identically whether the section is open or closed. A one-sided arrow ("Sort ▼")
        // and a mismatched pair ("▲ Up ▼") are left completely alone -- the arrow is the
        // information in both. Same shape and same reasoning as BoxDrawingRunRegex above: strip a
        // specific ornamental form, never a character class.
        private static readonly Regex OrnamentalGlyphBracketRegex = new Regex(
            @"^[ \t]*(?<g>[▲-◅←-⇿➡⬅-⬍])\k<g>*[ \t]+(?<text>.*?)[ \t]+\k<g>+[ \t]*$",
            RegexOptions.Compiled);
        private static readonly Regex MultiSpaceRegex = new Regex(@"[ \t]{2,}", RegexOptions.Compiled);
        private static readonly Regex NewlineDotRegex = new Regex(@"\n[ \t]*([\.,;:])", RegexOptions.Compiled);
        private static readonly Regex MultiNewlineRegex = new Regex(@"\n+", RegexOptions.Compiled);
        private static readonly Regex PeriodSpacePeriodRegex = new Regex(@"\.[ \t]+\.", RegexOptions.Compiled);
        // Whitespace before sentence punctuation is never correct in English and reads as a stray
        // "period"/"comma" (e.g. a row built from "{label} . {value}" where the label's own value was
        // empty). Collapse the space(s) onto the punctuation: "Blindness . Horrible" -> "Blindness. Horrible".
        private static readonly Regex SpaceBeforePunctuationRegex = new Regex(@"[ \t]+([\.,;:])", RegexOptions.Compiled);
        // Sentence punctuation at the very start of a line has nothing to terminate and reads as a stray
        // "period"/"comma" (e.g. ". Suppression: 50%" from a row built with a leading separator). Drop it.
        private static readonly Regex LeadingPunctuationRegex = new Regex(@"^[ \t]*[\.,;:]+[ \t]*", RegexOptions.Compiled);

        // C0 control characters (minus tab/newline/carriage-return, which the newline
        // normalization below folds into sentence breaks) and DEL. A NUL is fatal on the
        // Prism path: the native layer reads the buffer as a C string, so an interior NUL
        // silently truncates the utterance and a leading NUL erases it entirely (NVDA's
        // backend then reports the empty conversion as InvalidUtf8 and drops the speech).
        private static readonly Regex ControlCharRegex = new Regex(@"[\x00-\x08\x0B\x0C\x0E-\x1F\x7F]", RegexOptions.Compiled);

        private const string EllipsisPlaceholder = "\x01ELLIPSIS\x01";

        /// <summary>
        /// Sanitizes text for screen reader output. Called automatically by TolkHelper.Speak().
        /// </summary>
        public static string Sanitize(string text)
        {
            if (string.IsNullOrEmpty(text))
                return text;

            if (ControlCharRegex.IsMatch(text))
            {
                ScrubReporter?.Invoke("control character(s)", text);
            }
            text = ControlCharRegex.Replace(text, "");
            text = BoxDrawingRunRegex.Replace(text, ". ");
            text = StripOrnamentalGlyphBracket(text);
            text = StripTags(text);
            text = text.Replace("<", "").Replace(">", "");
            text = NormalizeNewlines(text);
            text = CollapseSpaces(text);

            text = text.Replace("...", EllipsisPlaceholder);
            text = FixBadPunctuation(text);
            text = FixDoublePeriods(text);
            // Strip leading orphan punctuation while the ellipsis is still masked, so a legitimate
            // leading "..." is preserved.
            text = LeadingPunctuationRegex.Replace(text, "");
            text = text.Replace(EllipsisPlaceholder, "...");

            text = text.Replace("....", "...");

            return text.Trim();
        }

        /// <summary>
        /// Normalizes line endings and folds stray newlines into sentence breaks,
        /// dropping any redundant punctuation that follows a newline.
        /// </summary>
        private static string NormalizeNewlines(string text)
        {
            text = text.Replace("\r\n", "\n").Replace("\r", "\n");
            // A newline acts as a sentence break already; drop the redundant
            // punctuation that immediately follows so we don't get "+2%[break]. text"
            text = NewlineDotRegex.Replace(text, "$1");
            text = MultiNewlineRegex.Replace(text, "\n");
            text = text.Replace("\n", ". ");
            return text;
        }

        private static string StripTags(string text)
        {
            return TagRegex.Replace(text, "");
        }

        /// <summary>
        /// Removes a same-glyph arrowhead bracket around a caption that still reads as words
        /// without it. Returns the input unchanged when the match leaves nothing pronounceable,
        /// so a caption that is only glyphs still reaches the callers that name it by direction
        /// (GlyphButtonNames) or qualify it by context (GlyphButtonName).
        /// </summary>
        private static string StripOrnamentalGlyphBracket(string text)
        {
            Match match = OrnamentalGlyphBracketRegex.Match(text);
            if (!match.Success)
            {
                return text;
            }
            string inner = match.Groups["text"].Value;
            bool pronounceable = false;
            foreach (char c in inner)
            {
                if (char.IsLetterOrDigit(c))
                {
                    pronounceable = true;
                    break;
                }
            }
            if (!pronounceable)
            {
                return text;
            }
            ScrubReporter?.Invoke("ornamental glyph bracket", text);
            return inner;
        }

        private static string CollapseSpaces(string text)
        {
            return MultiSpaceRegex.Replace(text, " ");
        }

        private static string FixBadPunctuation(string text)
        {
            text = SpaceBeforePunctuationRegex.Replace(text, "$1");
            text = text.Replace(":.", ".");
            text = text.Replace(".,", ".");
            text = text.Replace(",.", ".");
            text = text.Replace(":,", ":");
            text = text.Replace(",:", ":");
            return text;
        }

        private static string FixDoublePeriods(string text)
        {
            // A run of 3+ adjacent periods (two adjacent newlines each turned into ". " next to a
            // box-drawing separator collapsed the same way, see BoxDrawingRunRegex) needs a REGEX here,
            // not the plain two-dot string.Replace below: string.Replace scans its matches against the
            // ORIGINAL string in one left-to-right pass, so "...".Replace("..", ".") leaves "..", not
            // ".", -- it never re-scans its own output. \.{2,} collapses any run in one pass regardless
            // of length. Real ellipses are already masked out by the caller before this runs, so this
            // never touches a legitimate "...".
            text = Regex.Replace(text, @"\.{2,}", ".");
            text = PeriodSpacePeriodRegex.Replace(text, ".");
            return text;
        }
    }
}
