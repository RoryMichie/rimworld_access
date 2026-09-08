using RimWorldAccess.Shell;

namespace RimWorldAccess.Tests.Shell;

public class GlyphButtonNamesTests
{
    [Theory]
    [InlineData("▲")] // black up-pointing triangle, RimTalk's reorder button
    [InlineData("△")]
    [InlineData("↑")]
    [InlineData("⬆")]
    [InlineData(" ▲ ")] // padded for centring
    public void UpwardGlyphs_ReadAsUp(string label)
    {
        Assert.Equal(GlyphDirection.Up, GlyphButtonNames.Classify(label));
    }

    [Theory]
    [InlineData("▼")]
    [InlineData("↓")]
    [InlineData("⬇")]
    public void DownwardGlyphs_ReadAsDown(string label)
    {
        Assert.Equal(GlyphDirection.Down, GlyphButtonNames.Classify(label));
    }

    [Fact]
    public void SidewaysGlyphs_ReadAsLeftAndRight()
    {
        Assert.Equal(GlyphDirection.Left, GlyphButtonNames.Classify("◀"));
        Assert.Equal(GlyphDirection.Right, GlyphButtonNames.Classify("▶"));
    }

    [Fact]
    public void AGlyphWithAnEmojiVariationSelector_StillReads()
    {
        Assert.Equal(GlyphDirection.Up, GlyphButtonNames.Classify("⬆️"));
    }

    [Fact]
    public void ACaptionWithRealText_IsLeftAlone()
    {
        // The whole point of the rule: only a caption that is NOTHING but an
        // arrowhead gets renamed, so a mod's own wording always survives.
        Assert.Equal(GlyphDirection.None, GlyphButtonNames.Classify("Sort ▼"));
        Assert.Equal(GlyphDirection.None, GlyphButtonNames.Classify("Move up"));
    }

    [Fact]
    public void MixedDirections_AreNotADirection()
    {
        Assert.Equal(GlyphDirection.None, GlyphButtonNames.Classify("▲▼"));
    }

    [Fact]
    public void RepeatedSameDirectionGlyphs_StillReadAsThatDirection()
    {
        Assert.Equal(GlyphDirection.Right, GlyphButtonNames.Classify("▶▶"));
    }

    [Fact]
    public void AsciiLookalikes_AreLeftAlone()
    {
        // A caret or a "v" is a character a caller may well mean literally.
        Assert.Equal(GlyphDirection.None, GlyphButtonNames.Classify("^"));
        Assert.Equal(GlyphDirection.None, GlyphButtonNames.Classify("v"));
        Assert.Equal(GlyphDirection.None, GlyphButtonNames.Classify("->"));
    }

    [Fact]
    public void EmptyAndWhitespaceCaptions_AreNotDirections()
    {
        Assert.Equal(GlyphDirection.None, GlyphButtonNames.Classify(null));
        Assert.Equal(GlyphDirection.None, GlyphButtonNames.Classify(""));
        Assert.Equal(GlyphDirection.None, GlyphButtonNames.Classify("   "));
    }
}
