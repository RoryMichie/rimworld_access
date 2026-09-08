using System.Collections.Generic;
using RimWorldAccess.Shell;
using Xunit;

namespace RimWorldAccess.Tests.Shell;

/// <summary>
/// Tests for <see cref="BandLabelInheritance"/>, the geometry-only rule that gives an
/// unlabeled twin control the caption it visually shares with a labeled neighbour
/// (RimTalk Persona Director's "Label + checkbox + checkbox" rows).
/// </summary>
public class BandLabelInheritanceTests
{
    private static BandLabelCandidate Row(bool interactive, bool hasLabel, float x, int clip = 1, float y = 0f, float width = 24f, float height = 24f)
    {
        return new BandLabelCandidate { Interactive = interactive, HasLabel = hasLabel, ClipId = clip, X = x, Y = y, Width = width, Height = height };
    }

    [Fact]
    public void DonorWithTwoUnlabeledTwinsToItsRight_BothInherit()
    {
        // The trace geometry: a labeled checkbox ("Ideology") at x=0, two unlabeled twin
        // checkboxes to its right on the same row.
        var rows = new List<BandLabelCandidate>
        {
            Row(interactive: true, hasLabel: true, x: 0f),
            Row(interactive: true, hasLabel: false, x: 40f),
            Row(interactive: true, hasLabel: false, x: 80f),
        };

        IReadOnlyList<BandLabelVerdict> verdicts = BandLabelInheritance.Compute(rows);

        Assert.Equal(-1, verdicts[0].DonorIndex);
        Assert.Equal(0, verdicts[1].DonorIndex);
        Assert.Equal(2, verdicts[1].Ordinal);
        Assert.Equal(0, verdicts[2].DonorIndex);
        Assert.Equal(3, verdicts[2].Ordinal);
    }

    [Fact]
    public void AnOffBandRow_NeverInherits()
    {
        // A third unlabeled checkbox sits well below the donor's row (a different band
        // entirely) and must not pick up the label meant for the row above it.
        var rows = new List<BandLabelCandidate>
        {
            Row(interactive: true, hasLabel: true, x: 0f, y: 0f),
            Row(interactive: true, hasLabel: false, x: 40f, y: 0f),
            Row(interactive: true, hasLabel: false, x: 40f, y: 200f),
        };

        IReadOnlyList<BandLabelVerdict> verdicts = BandLabelInheritance.Compute(rows);

        Assert.Equal(0, verdicts[1].DonorIndex);
        Assert.Equal(-1, verdicts[2].DonorIndex);
    }

    [Fact]
    public void ARowWithItsOwnLabel_NeverInherits()
    {
        var rows = new List<BandLabelCandidate>
        {
            Row(interactive: true, hasLabel: true, x: 0f),
            Row(interactive: true, hasLabel: true, x: 40f),
        };

        IReadOnlyList<BandLabelVerdict> verdicts = BandLabelInheritance.Compute(rows);

        Assert.Equal(-1, verdicts[0].DonorIndex);
        Assert.Equal(-1, verdicts[1].DonorIndex);
    }

    [Fact]
    public void ADifferentClip_NeverInherits()
    {
        var rows = new List<BandLabelCandidate>
        {
            Row(interactive: true, hasLabel: true, x: 0f, clip: 1),
            Row(interactive: true, hasLabel: false, x: 40f, clip: 2),
        };

        IReadOnlyList<BandLabelVerdict> verdicts = BandLabelInheritance.Compute(rows);

        Assert.Equal(-1, verdicts[1].DonorIndex);
    }

    [Fact]
    public void APlainLabelRow_IsNeverADonorOrAnInheritor()
    {
        var rows = new List<BandLabelCandidate>
        {
            Row(interactive: false, hasLabel: true, x: 0f), // a plain, non-interactive Label
            Row(interactive: true, hasLabel: false, x: 40f),
        };

        IReadOnlyList<BandLabelVerdict> verdicts = BandLabelInheritance.Compute(rows);

        Assert.Equal(-1, verdicts[1].DonorIndex);
    }

    [Fact]
    public void ANearerDonorInTheSameBand_WinsOverAFartherOne()
    {
        var rows = new List<BandLabelCandidate>
        {
            Row(interactive: true, hasLabel: true, x: 0f),   // "Name"
            Row(interactive: true, hasLabel: true, x: 100f), // "Ideology" — the nearer donor
            Row(interactive: true, hasLabel: false, x: 140f),
        };

        IReadOnlyList<BandLabelVerdict> verdicts = BandLabelInheritance.Compute(rows);

        Assert.Equal(1, verdicts[2].DonorIndex);
        Assert.Equal(2, verdicts[2].Ordinal);
    }

    [Fact]
    public void NoDonorToTheLeft_NoInheritance()
    {
        var rows = new List<BandLabelCandidate>
        {
            Row(interactive: true, hasLabel: false, x: 0f),
            Row(interactive: true, hasLabel: true, x: 40f),
        };

        IReadOnlyList<BandLabelVerdict> verdicts = BandLabelInheritance.Compute(rows);

        Assert.Equal(-1, verdicts[0].DonorIndex);
    }

    [Fact]
    public void EmptyAndNullInput_ReturnNoVerdicts()
    {
        Assert.Empty(BandLabelInheritance.Compute(new List<BandLabelCandidate>()));
        Assert.Empty(BandLabelInheritance.Compute(null));
    }
}
