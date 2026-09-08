using System.Collections.Generic;
using RimWorldAccess.Shell;
using Xunit;

namespace RimWorldAccess.Tests.Shell;

/// <summary>
/// Tests for <see cref="VerticalLabelInheritance"/>, the geometry-only rule that gives a
/// blank-labeled Slider the caption of the labeled row directly above it (Colony Manager
/// Redux's "Active if: ..." trigger button sits one row above its unlabeled
/// threshold slider).
/// </summary>
public class VerticalLabelInheritanceTests
{
    private static VerticalLabelCandidate Row(bool eligible, bool hasLabel, float y, float x = 342f, int clip = 1, float width = 716f, float height = 60f)
    {
        return new VerticalLabelCandidate { Eligible = eligible, HasLabel = hasLabel, ClipId = clip, X = x, Y = y, Width = width, Height = height };
    }

    [Fact]
    public void LabeledRowDirectlyAbove_DonatesToTheSliderBelow()
    {
        // The CMR shape: "Active if: ..." button at y=282..342, the slider directly below at
        // y=342, same X and width.
        var rows = new List<VerticalLabelCandidate>
        {
            Row(eligible: false, hasLabel: true, y: 282f),
            Row(eligible: true, hasLabel: false, y: 342f),
        };

        IReadOnlyList<int> donors = VerticalLabelInheritance.ComputeDonors(rows);

        Assert.Equal(0, donors[1]);
    }

    [Fact]
    public void NonEligibleRow_NeverReceivesADonor()
    {
        var rows = new List<VerticalLabelCandidate>
        {
            Row(eligible: false, hasLabel: true, y: 0f),
            Row(eligible: false, hasLabel: false, y: 60f), // a plain Label, not a Slider
        };

        IReadOnlyList<int> donors = VerticalLabelInheritance.ComputeDonors(rows);

        Assert.Equal(-1, donors[1]);
    }

    [Fact]
    public void AlreadyLabeledSlider_NeverInherits()
    {
        var rows = new List<VerticalLabelCandidate>
        {
            Row(eligible: false, hasLabel: true, y: 0f),
            Row(eligible: true, hasLabel: true, y: 60f), // "Price for guests: 100%" -- already labeled
        };

        IReadOnlyList<int> donors = VerticalLabelInheritance.ComputeDonors(rows);

        Assert.Equal(-1, donors[1]);
    }

    [Fact]
    public void GapWiderThanOneRowHeight_NeverInherits()
    {
        var rows = new List<VerticalLabelCandidate>
        {
            Row(eligible: false, hasLabel: true, y: 0f, height: 60f), // bottom at y=60
            // Slider starts at y=200 -- a gap of 140, far more than one row height (60).
            Row(eligible: true, hasLabel: false, y: 200f, height: 40f),
        };

        IReadOnlyList<int> donors = VerticalLabelInheritance.ComputeDonors(rows);

        Assert.Equal(-1, donors[1]);
    }

    [Fact]
    public void DifferentClip_NeverInherits()
    {
        var rows = new List<VerticalLabelCandidate>
        {
            Row(eligible: false, hasLabel: true, y: 282f, clip: 1),
            Row(eligible: true, hasLabel: false, y: 342f, clip: 2),
        };

        IReadOnlyList<int> donors = VerticalLabelInheritance.ComputeDonors(rows);

        Assert.Equal(-1, donors[1]);
    }

    [Fact]
    public void DifferentColumn_NeverInherits()
    {
        var rows = new List<VerticalLabelCandidate>
        {
            Row(eligible: false, hasLabel: true, y: 282f, x: 18f, width: 200f), // a different, non-overlapping column
            Row(eligible: true, hasLabel: false, y: 342f, x: 342f, width: 716f),
        };

        IReadOnlyList<int> donors = VerticalLabelInheritance.ComputeDonors(rows);

        Assert.Equal(-1, donors[1]);
    }

    [Fact]
    public void NearestAboveRowWins_OverAFartherOne()
    {
        var rows = new List<VerticalLabelCandidate>
        {
            Row(eligible: false, hasLabel: true, y: 250f, height: 60f), // farther donor (gap 32), still qualifies
            Row(eligible: false, hasLabel: true, y: 282f, height: 60f), // nearer donor (gap 0), directly above
            Row(eligible: true, hasLabel: false, y: 342f),
        };

        IReadOnlyList<int> donors = VerticalLabelInheritance.ComputeDonors(rows);

        Assert.Equal(1, donors[2]);
    }

    [Fact]
    public void EmptyAndNullInput_ReturnNoDonors()
    {
        Assert.Empty(VerticalLabelInheritance.ComputeDonors(new List<VerticalLabelCandidate>()));
        Assert.Empty(VerticalLabelInheritance.ComputeDonors(null));
    }
}
