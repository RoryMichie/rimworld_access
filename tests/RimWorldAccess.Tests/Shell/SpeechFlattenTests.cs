using RimWorldAccess.Shell;
using Xunit;

namespace RimWorldAccess.Tests.Shell;

public class SpeechFlattenTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   \n\t\n  ")]
    public void NoSpeakableContent_ReturnsNull(string raw)
    {
        Assert.Null(SpeechFlatten.ToSentences(raw));
    }

    [Fact]
    public void TabIndentedMultiLineDescription_FlattensToPeriods()
    {
        string raw = "Casts a fireball\n\tRange: 20\n\nBurns the target";

        Assert.Equal("Casts a fireball. Range: 20. Burns the target.", SpeechFlatten.ToSentences(raw));
    }

    [Fact]
    public void LinesAlreadyEndingInPeriods_DoNotDouble()
    {
        string raw = "First sentence.\nSecond sentence.";

        Assert.Equal("First sentence. Second sentence.", SpeechFlatten.ToSentences(raw));
    }

    [Theory]
    [InlineData("Watch out!", "Watch out!")]
    [InlineData("Really?", "Really?")]
    [InlineData("No punctuation", "No punctuation.")]
    public void TerminalPunctuation_PreservedOrAdded(string raw, string expected)
    {
        Assert.Equal(expected, SpeechFlatten.ToSentences(raw));
    }

    [Fact]
    public void ColonTerminatedLine_IntroducesTheNextWithoutAPeriod()
    {
        string raw = "Click to jump to:\n\nBob, healthy";

        Assert.Equal("Click to jump to: Bob, healthy.", SpeechFlatten.ToSentences(raw));
    }

    [Fact]
    public void WindowsLineEndings_FlattenTheSameAsUnix()
    {
        string raw = "First line\r\nSecond line";

        Assert.Equal("First line. Second line.", SpeechFlatten.ToSentences(raw));
    }
}
