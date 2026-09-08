using RimWorldAccess.Shell;

namespace RimWorldAccess.Tests;

/// <summary>
/// Tests for <see cref="CmrSubsetToggle"/>, the tri-state arithmetic behind Colony Manager Redux's
/// group shortcuts. The interesting cases are the two the mod's own <c>All(...)</c> predicates
/// produce and a hand-written "checked when allowed == count" would get wrong: a partially allowed
/// subset fills up rather than emptying, and an EMPTY subset reads as fully checked (a ticked
/// "Predators" box on a map with no predators, whose click does nothing).
/// </summary>
public class CmrSubsetToggleTests
{
    [Fact]
    public void FullyAllowedSubsetIsChecked()
    {
        Assert.Equal(CheckState.Checked, CmrSubsetToggle.StateOf(4, 4));
        Assert.False(CmrSubsetToggle.ValueForNextClick(4, 4));
    }

    [Fact]
    public void UnallowedSubsetIsUnchecked()
    {
        Assert.Equal(CheckState.Unchecked, CmrSubsetToggle.StateOf(4, 0));
        Assert.True(CmrSubsetToggle.ValueForNextClick(4, 0));
    }

    [Fact]
    public void PartiallyAllowedSubsetIsPartialAndFillsUp()
    {
        Assert.Equal(CheckState.PartiallyChecked, CmrSubsetToggle.StateOf(4, 1));
        Assert.Equal(CheckState.PartiallyChecked, CmrSubsetToggle.StateOf(4, 3));
        Assert.True(CmrSubsetToggle.ValueForNextClick(4, 3));
    }

    [Fact]
    public void EmptySubsetReadsAsCheckedAndClicksToNothing()
    {
        Assert.Equal(CheckState.Checked, CmrSubsetToggle.StateOf(0, 0));
        Assert.False(CmrSubsetToggle.ValueForNextClick(0, 0));
    }
}
