using RimWorldAccess.Shell;
using Xunit;

namespace RimWorldAccess.Tests.Shell;

/// <summary>
/// Tests for <see cref="TextureButtonLabels"/>: the
/// texture-asset-name fallback that used to speak raw filenames ("ReorderDown. button.").
/// </summary>
public class TextureButtonLabelsTests
{
    [Theory]
    [InlineData("ReorderUp", "ReorderUp")]
    [InlineData("ReorderDown", "ReorderDown")]
    [InlineData("Plus", "Plus")]
    [InlineData("Minus", "Minus")]
    [InlineData("DeleteX", "Delete")]
    [InlineData("Delete", "Delete")]
    [InlineData("CloseXSmall", "Close")]
    [InlineData("CloseXBig", "Close")]
    [InlineData("Copy", "Copy")]
    [InlineData("Paste", "Paste")]
    [InlineData("Rename", "Rename")]
    [InlineData("Info", "Info")]
    [InlineData("InfoButton", "Info")]
    [InlineData("Drag", "Drag")]
    [InlineData("DragHash", "Drag")]
    [InlineData("OpenInspector", "OpenInspector")]
    [InlineData("ToggleLog", "ToggleLog")]
    public void KnownVanillaTextureNames_ResolveToTheirGlyphKeySuffix(string textureName, string expectedSuffix)
    {
        Assert.Equal(expectedSuffix, TextureButtonLabels.TryGetGlyphKeySuffix(textureName));
    }

    [Fact]
    public void AnUnrecognisedTextureName_HasNoGlyphKey()
    {
        Assert.Null(TextureButtonLabels.TryGetGlyphKeySuffix("bastronaut1"));
    }

    [Fact]
    public void EmptyAndNullTextureNames_HaveNoGlyphKey()
    {
        Assert.Null(TextureButtonLabels.TryGetGlyphKeySuffix(""));
        Assert.Null(TextureButtonLabels.TryGetGlyphKeySuffix(null));
    }

    [Fact]
    public void Humanize_SplitsCamelCaseIntoWords()
    {
        Assert.Equal("Some Odd Icon", TextureButtonLabels.Humanize("SomeOddIcon"));
    }

    [Fact]
    public void Humanize_ReplacesUnderscoresWithSpaces()
    {
        Assert.Equal("some odd icon", TextureButtonLabels.Humanize("some_odd_icon"));
    }

    [Fact]
    public void Humanize_HandlesMixedUnderscoresAndCamelCase()
    {
        Assert.Equal("CMR padlock Closed", TextureButtonLabels.Humanize("CMR_padlockClosed"));
    }

    [Fact]
    public void Humanize_PlainWord_IsUnchanged()
    {
        Assert.Equal("bastronaut1", TextureButtonLabels.Humanize("bastronaut1"));
    }

    [Fact]
    public void Humanize_EmptyAndNull_ReturnEmptyString()
    {
        Assert.Equal("", TextureButtonLabels.Humanize(""));
        Assert.Equal("", TextureButtonLabels.Humanize(null));
    }
}
