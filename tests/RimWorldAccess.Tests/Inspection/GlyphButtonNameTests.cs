using RimWorldAccess;

namespace RimWorldAccess.Tests.Inspection;

public class GlyphButtonNameTests
{
    [Theory]
    [InlineData("×")]
    [InlineData("+")]
    [InlineData("-")]
    [InlineData("−")]
    [InlineData("..")]
    [InlineData(" × ")]
    public void IsGlyphOnlyName_PunctuationOnlyShortName_ReturnsTrue(string name)
    {
        Assert.True(GlyphButtonName.IsGlyphOnlyName(name));
    }

    [Theory]
    [InlineData("Assign")]
    [InlineData("...")] // longer than 2 characters after trim
    [InlineData("A")]
    [InlineData("1")]
    [InlineData("×1")]
    public void IsGlyphOnlyName_WordOrTooLongOrAlphanumeric_ReturnsFalse(string name)
    {
        Assert.False(GlyphButtonName.IsGlyphOnlyName(name));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void IsGlyphOnlyName_NullOrBlank_ReturnsFalse(string name)
    {
        Assert.False(GlyphButtonName.IsGlyphOnlyName(name));
    }
}
