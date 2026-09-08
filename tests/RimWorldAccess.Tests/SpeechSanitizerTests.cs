using RimWorldAccess;

namespace RimWorldAccess.Tests;

/// <summary>
/// Tests for <see cref="SpeechSanitizer.Sanitize"/>, the pipeline every screen
/// reader announcement passes through. A regression here would degrade output
/// for every user, in every language, so the rules are pinned explicitly.
/// </summary>
public class SpeechSanitizerTests
{
    [Fact]
    public void Null_PassesThrough()
    {
        Assert.Null(SpeechSanitizer.Sanitize(null!));
    }

    [Fact]
    public void Empty_PassesThrough()
    {
        Assert.Equal("", SpeechSanitizer.Sanitize(""));
    }

    [Fact]
    public void PlainText_Unchanged()
    {
        Assert.Equal("Build wall", SpeechSanitizer.Sanitize("Build wall"));
    }

    [Theory]
    [InlineData("<b>bold</b>", "bold")]
    [InlineData("<color=red>danger</color>", "danger")]
    [InlineData("a<b>b</b>c", "abc")]
    public void StripsMarkupTags(string input, string expected)
    {
        Assert.Equal(expected, SpeechSanitizer.Sanitize(input));
    }

    [Fact]
    public void CollapsesRepeatedSpaces()
    {
        Assert.Equal("a b", SpeechSanitizer.Sanitize("a    b"));
    }

    [Theory]
    [InlineData("line1\nline2", "line1. line2")]
    [InlineData("a\r\nb", "a. b")]
    [InlineData("a\n\n\nb", "a. b")]
    public void FoldsNewlinesIntoSentenceBreaks(string input, string expected)
    {
        Assert.Equal(expected, SpeechSanitizer.Sanitize(input));
    }

    [Fact]
    public void DropsRedundantPunctuationAfterNewline()
    {
        // A newline already acts as a sentence break; the following "." is dropped
        // rather than producing a stray spoken "period".
        Assert.Equal("a. b", SpeechSanitizer.Sanitize("a\n. b"));
    }

    [Fact]
    public void CollapsesSpaceBeforePunctuation()
    {
        // Built from "{label} . {value}" where the label resolved empty.
        Assert.Equal("Blindness. Horrible", SpeechSanitizer.Sanitize("Blindness . Horrible"));
    }

    [Fact]
    public void StripsLeadingOrphanPunctuation()
    {
        // Row built with a leading separator: ". Suppression: 50%".
        Assert.Equal("Suppression: 50%", SpeechSanitizer.Sanitize(". Suppression: 50%"));
    }

    [Fact]
    public void PreservesTrailingEllipsis()
    {
        Assert.Equal("Loading...", SpeechSanitizer.Sanitize("Loading..."));
    }

    [Fact]
    public void PreservesLeadingEllipsis()
    {
        // The ellipsis is masked before orphan-punctuation stripping precisely so
        // a legitimate leading "..." survives.
        Assert.Equal("...and more", SpeechSanitizer.Sanitize("...and more"));
    }

    [Theory]
    [InlineData("End..", "End.")]
    [InlineData("Done. . Next", "Done. Next")]
    public void CollapsesDoublePeriods(string input, string expected)
    {
        Assert.Equal(expected, SpeechSanitizer.Sanitize(input));
    }

    [Theory]
    [InlineData("Label:. value", "Label. value")]
    [InlineData("a,. b", "a. b")]
    [InlineData("a,: b", "a: b")]
    public void FixesAdjacentPunctuation(string input, string expected)
    {
        Assert.Equal(expected, SpeechSanitizer.Sanitize(input));
    }

    [Fact]
    public void TrimsSurroundingWhitespace()
    {
        Assert.Equal("hi", SpeechSanitizer.Sanitize("   hi   "));
    }

    [Theory]
    [InlineData("<think>reasoning here</think>Final answer", "reasoning hereFinal answer")]
    [InlineData("Before<think>visible\nreasoning</think>After", "Beforevisible. reasoningAfter")]
    [InlineData("<think>only reasoning</think>", "only reasoning")]
    public void PreservesThinkBlockContent(string input, string expected)
    {
        // Speech mirrors the screen: reasoning text a mod
        // displays must be spoken, never editorialized away. Only the <think>/</think>
        // wrapper goes, via the same markup stripping every tag gets.
        Assert.Equal(expected, SpeechSanitizer.Sanitize(input));
    }

    [Theory]
    [InlineData("\0Make vehicle", "Make vehicle")]
    [InlineData("Chemfuel\0 need", "Chemfuel need")]
    [InlineData("keep\ttabs and\nnewlines", "keep\ttabs and. newlines")]
    public void StripsControlCharacters(string input, string expected)
    {
        // The Prism layer reads the utterance as a C string: an interior NUL truncates
        // speech mid-utterance and a leading NUL erases it entirely (NVDA's backend then
        // misreports the empty conversion as InvalidUtf8 and the announcement is lost).
        // Tab/newline survive to the whitespace/newline normalization stages.
        Assert.Equal(expected, SpeechSanitizer.Sanitize(input));
    }

    [Fact]
    public void ControlCharacter_InvokesScrubReporterOnceWithOriginal()
    {
        // Built at runtime, not InlineData: xUnit theory-data serialization mangles a raw NUL.
        string withNul = "Chemfuel" + '\0' + " need";
        var reports = new System.Collections.Generic.List<(string reason, string original)>();
        SpeechSanitizer.ScrubReporter = (reason, original) => reports.Add((reason, original));
        try
        {
            SpeechSanitizer.Sanitize(withNul);
        }
        finally
        {
            SpeechSanitizer.ScrubReporter = null;
        }

        Assert.Single(reports);
        Assert.Equal("control character(s)", reports[0].reason);
        Assert.Equal(withNul, reports[0].original);
    }

    [Fact]
    public void CleanText_NeverInvokesScrubReporter()
    {
        var reports = new System.Collections.Generic.List<(string reason, string original)>();
        SpeechSanitizer.ScrubReporter = (reason, original) => reports.Add((reason, original));
        try
        {
            SpeechSanitizer.Sanitize("Build wall");
        }
        finally
        {
            SpeechSanitizer.ScrubReporter = null;
        }

        Assert.Empty(reports);
    }

    [Theory]
    [InlineData("▼ Show settings ▼", "Show settings")]
    [InlineData("▲ Hide settings ▲", "Hide settings")]
    [InlineData("▼▼ Show ▼▼", "Show")]
    [InlineData("◀ Back ◀", "Back")]
    [InlineData("Sort ▼", "Sort ▼")]
    [InlineData("▶ Play", "▶ Play")]
    [InlineData("▲ Up ▼", "▲ Up ▼")]
    [InlineData("▼ ▼", "▼ ▼")]
    [InlineData("▼", "▼")]
    [InlineData("▼ 5 ▼", "5")]
    [InlineData("Set the ▼ marker ▼", "Set the ▼ marker ▼")]
    public void StripsSymmetricalArrowheadBracket(string input, string expected)
    {
        Assert.Equal(expected, SpeechSanitizer.Sanitize(input));
    }

    [Fact]
    public void ReplaceLoneSurrogates_ProducesValidScalars()
    {
        // Belt-and-braces below the sanitizer: whatever reaches the native marshaler
        // must encode to valid UTF-8, or Prism rejects the whole utterance. Inputs are
        // built at runtime because xUnit theory-data serialization cannot round-trip a
        // lone surrogate.
        const char high = '\uD83D';
        const char low = '\uDE00';
        const string emoji = "\U0001F600";

        Assert.Equal("plain ascii", TextNormalization.ReplaceLoneSurrogates("plain ascii"));
        Assert.Equal($"emoji {emoji} intact", TextNormalization.ReplaceLoneSurrogates($"emoji {emoji} intact"));
        Assert.Equal("lone high \uFFFD end", TextNormalization.ReplaceLoneSurrogates($"lone high {high} end"));
        Assert.Equal("\uFFFD lone low", TextNormalization.ReplaceLoneSurrogates($"{low} lone low"));
        Assert.Equal($"split pair \uFFFD{emoji}", TextNormalization.ReplaceLoneSurrogates($"split pair {high}{emoji}"));
        Assert.Equal($"trailing high {emoji}\uFFFD", TextNormalization.ReplaceLoneSurrogates($"trailing high {emoji}{high}"));
    }
}
