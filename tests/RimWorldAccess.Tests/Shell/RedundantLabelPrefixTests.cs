using RimWorldAccess.Shell;

namespace RimWorldAccess.Tests.Shell;

/// <summary>Tests for <see cref="RedundantLabelPrefix.Strip"/>.</summary>
public class RedundantLabelPrefixTests
{
    [Theory]
    [InlineData("Use global hotkeys. When enabled, tools...", "Use global hotkeys", "When enabled, tools...")]
    [InlineData("Use global hotkeys", "Use global hotkeys", "")]
    [InlineData("Volume: how loud", "Volume", "how loud")]
    [InlineData("Hotkeys are disabled while paused", "Hotkeys", "Hotkeys are disabled while paused")]
    [InlineData("Use global hotkeys, when enabled", "Use global hotkeys", "Use global hotkeys, when enabled")]
    [InlineData("use global hotkeys. Something", "Use global hotkeys", "use global hotkeys. Something")]
    [InlineData("  Volume. Loud", "Volume", "Loud")]
    [InlineData("Volume.\nLoud", "Volume", "Loud")]
    public void Strip_MatchesExpected(string extras, string label, string expected)
    {
        Assert.Equal(expected, RedundantLabelPrefix.Strip(extras, label));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void Strip_NullOrEmptyLabel_ReturnsExtrasUnchanged(string label)
    {
        Assert.Equal("anything", RedundantLabelPrefix.Strip("anything", label!));
    }

    [Fact]
    public void Strip_NullExtras_ReturnsNull()
    {
        Assert.Null(RedundantLabelPrefix.Strip(null!, "anything"));
    }
}
