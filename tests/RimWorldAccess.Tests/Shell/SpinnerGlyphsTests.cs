using RimWorldAccess.Shell;

namespace RimWorldAccess.Tests.Shell;

public class SpinnerGlyphsTests
{
    [Theory]
    [InlineData("-")]
    [InlineData("−")] // U+2212 minus sign
    [InlineData("  -  ")]
    public void Decrement_Accepted(string label)
    {
        Assert.True(SpinnerGlyphs.IsDecrement(label));
    }

    [Theory]
    [InlineData("--")]
    [InlineData("+")]
    [InlineData("")]
    [InlineData(null)]
    [InlineData("Remove -")]
    public void Decrement_Rejected(string label)
    {
        Assert.False(SpinnerGlyphs.IsDecrement(label));
    }

    [Theory]
    [InlineData("+")]
    [InlineData("  +  ")]
    public void Increment_Accepted(string label)
    {
        Assert.True(SpinnerGlyphs.IsIncrement(label));
    }

    [Theory]
    [InlineData("++")]
    [InlineData("Add +5")]
    [InlineData("")]
    [InlineData(null)]
    public void Increment_Rejected(string label)
    {
        Assert.False(SpinnerGlyphs.IsIncrement(label));
    }

    [Theory]
    [InlineData("0")]
    [InlineData("3")]
    [InlineData("-2")]
    [InlineData("50%")]
    [InlineData("1.5")]
    [InlineData("1,5")]
    [InlineData("  7  ")]
    public void BareNumber_Accepted(string text)
    {
        Assert.True(SpinnerGlyphs.IsBareNumber(text));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("3 days")]
    [InlineData("1-5")]
    [InlineData("1.2.3")]
    [InlineData("abc")]
    [InlineData("%")]
    [InlineData("+")]
    [InlineData("-")]
    [InlineData(null)]
    public void BareNumber_Rejected(string text)
    {
        Assert.False(SpinnerGlyphs.IsBareNumber(text));
    }
}
