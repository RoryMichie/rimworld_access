using System.Collections.Generic;
using RimWorldAccess.Shell;
using Xunit;

namespace RimWorldAccess.Tests.Shell;

/// <summary>
/// Tests for <see cref="SectionOwnership"/>, the geometry-only rule that attributes a row to the
/// nearest overlapping heading above it in the same column, and exempts window chrome (the
/// RWQoLTweaks Close button announcing "Ideology. Close. Button." — the bottom Close button
/// inheriting the last content heading in front of it) from owning or naming a section at all.
/// </summary>
public class SectionOwnershipTests
{
    private static SectionCandidate Row(bool isHeading, float yMin, float xMin = 0f, float xMax = 100f, bool isChrome = false)
    {
        return new SectionCandidate { IsHeading = isHeading, IsChrome = isChrome, XMin = xMin, XMax = xMax, YMin = yMin };
    }

    [Fact]
    public void SingleColumn_OneHeadingAboveTwoRows_BothOwnIt()
    {
        var rows = new List<SectionCandidate>
        {
            Row(isHeading: true, yMin: 0f),
            Row(isHeading: false, yMin: 60f),
            Row(isHeading: false, yMin: 120f),
        };

        IReadOnlyList<int> owners = SectionOwnership.ComputeOwners(rows);

        Assert.Equal(0, owners[1]);
        Assert.Equal(0, owners[2]);
    }

    [Fact]
    public void TwoHeadingsInOneColumn_EachRowOwnsTheNearestAbove()
    {
        var rows = new List<SectionCandidate>
        {
            Row(isHeading: true, yMin: 0f),
            Row(isHeading: false, yMin: 60f),
            Row(isHeading: true, yMin: 120f),
            Row(isHeading: false, yMin: 180f),
        };

        IReadOnlyList<int> owners = SectionOwnership.ComputeOwners(rows);

        Assert.Equal(0, owners[1]);
        Assert.Equal(2, owners[3]);
    }

    [Fact]
    public void TwoColumns_HeadingOnlyInTheLeftOne_RightColumnRowOwnsNothing()
    {
        // The Dubs case: a narrow left column's heading never reaches the wide right column.
        var rows = new List<SectionCandidate>
        {
            Row(isHeading: true, yMin: 0f, xMin: 0f, xMax: 100f),
            Row(isHeading: false, yMin: 60f, xMin: 200f, xMax: 400f),
        };

        IReadOnlyList<int> owners = SectionOwnership.ComputeOwners(rows);

        Assert.Equal(-1, owners[1]);
    }

    [Fact]
    public void HeadingDrawnBelowARow_DoesNotOwnIt()
    {
        var rows = new List<SectionCandidate>
        {
            Row(isHeading: false, yMin: 0f),
            Row(isHeading: true, yMin: 60f),
        };

        IReadOnlyList<int> owners = SectionOwnership.ComputeOwners(rows);

        Assert.Equal(-1, owners[0]);
    }

    [Fact]
    public void ChromeRow_BelowAndInsideAHeadingsColumn_OwnsNothing()
    {
        // The RWQoLTweaks Close button: drawn last, below the "Ideology" heading, horizontally
        // inside its column.
        var rows = new List<SectionCandidate>
        {
            Row(isHeading: true, yMin: 0f),
            Row(isHeading: false, yMin: 60f, isChrome: true),
        };

        IReadOnlyList<int> owners = SectionOwnership.ComputeOwners(rows);

        Assert.Equal(-1, owners[1]);
    }

    [Fact]
    public void ChromeHeading_NamesNothing_ContentFallsThroughToTheNextRealHeadingOrNone()
    {
        var rows = new List<SectionCandidate>
        {
            Row(isHeading: true, yMin: 0f, isChrome: true),
            Row(isHeading: false, yMin: 60f),
        };

        IReadOnlyList<int> owners = SectionOwnership.ComputeOwners(rows);

        Assert.Equal(-1, owners[1]);

        var withRealHeadingBehind = new List<SectionCandidate>
        {
            Row(isHeading: true, yMin: 0f),
            Row(isHeading: true, yMin: 30f, isChrome: true),
            Row(isHeading: false, yMin: 60f),
        };

        owners = SectionOwnership.ComputeOwners(withRealHeadingBehind);

        Assert.Equal(0, owners[2]);
    }

    [Fact]
    public void HeadingRowItself_GetsNoOwner()
    {
        var rows = new List<SectionCandidate>
        {
            Row(isHeading: true, yMin: 0f),
            Row(isHeading: true, yMin: 60f),
        };

        IReadOnlyList<int> owners = SectionOwnership.ComputeOwners(rows);

        Assert.Equal(-1, owners[1]);
    }

    [Fact]
    public void EmptyAndNullInput_ReturnNoOwners()
    {
        Assert.Empty(SectionOwnership.ComputeOwners(new List<SectionCandidate>()));
        Assert.Empty(SectionOwnership.ComputeOwners(null));
    }
}
