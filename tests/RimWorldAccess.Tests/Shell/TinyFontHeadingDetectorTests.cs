using System.Collections.Generic;
using RimWorldAccess.Shell;
using Xunit;

namespace RimWorldAccess.Tests.Shell;

/// <summary>
/// Tests for <see cref="TinyFontHeadingDetector"/>, the geometry/font-mix rule that recognises a
/// GameFont.Tiny panel heading (Colony Manager Redux's
/// "Target resource"/"Allowed animals"/"Threshold" section headers).
/// </summary>
public class TinyFontHeadingDetectorTests
{
    private static TinyHeadingCandidateRow Label(bool tiny, float y, int clip = 1, float height = 24f)
    {
        return new TinyHeadingCandidateRow { IsLabel = true, IsTinyFont = tiny, ClipId = clip, Y = y, Height = height };
    }

    private static TinyHeadingCandidateRow Control(bool interactive, float y, int clip = 1, float height = 24f)
    {
        return new TinyHeadingCandidateRow { IsLabel = false, IsInteractive = interactive, ClipId = clip, Y = y, Height = height };
    }

    [Fact]
    public void TinyMinority_WithControlDirectlyBelow_QualifiesAsHeading()
    {
        var rows = new List<TinyHeadingCandidateRow>
        {
            Label(tiny: false, y: 0f),
            Label(tiny: false, y: 24f),
            Label(tiny: false, y: 48f),
            Label(tiny: true, y: 100f),   // "Target resource" -- the lone tiny header
            Control(interactive: true, y: 124f),
        };

        IReadOnlyList<bool> headings = TinyFontHeadingDetector.FindHeadings(rows);

        Assert.True(headings[3]);
    }

    [Fact]
    public void TinyIsMajority_NeverQualifies()
    {
        // A mod that simply draws everything in Tiny is not using the font to mark sections.
        var rows = new List<TinyHeadingCandidateRow>
        {
            Label(tiny: true, y: 0f),
            Label(tiny: true, y: 24f),
            Label(tiny: false, y: 48f),
            Control(interactive: true, y: 72f),
        };

        IReadOnlyList<bool> headings = TinyFontHeadingDetector.FindHeadings(rows);

        Assert.All(headings, h => Assert.False(h));
    }

    [Fact]
    public void SmallLabelFollowsClosely_Qualifies()
    {
        // Detection runs BEFORE caption fusion, so a checkbox's caption is still
        // a raw small Label of its own at this point -- a tiny label directly
        // above one is a header over section content (the Colony Manager Redux
        // shape, caught live: "Target resource" over the "Leather" caption).
        var rows = new List<TinyHeadingCandidateRow>
        {
            Label(tiny: false, y: 0f),
            Label(tiny: false, y: 24f),
            Label(tiny: true, y: 100f),
            Label(tiny: false, y: 124f),
        };

        IReadOnlyList<bool> headings = TinyFontHeadingDetector.FindHeadings(rows);

        Assert.True(headings[2]);
    }

    [Fact]
    public void NothingFollowsInTheClip_NeverQualifies()
    {
        var rows = new List<TinyHeadingCandidateRow>
        {
            Label(tiny: false, y: 0f),
            Label(tiny: false, y: 24f),
            Label(tiny: true, y: 100f), // last row of its clip
        };

        IReadOnlyList<bool> headings = TinyFontHeadingDetector.FindHeadings(rows);

        Assert.False(headings[2]);
    }

    [Fact]
    public void FollowingRowTooFarBelow_NeverQualifies()
    {
        var rows = new List<TinyHeadingCandidateRow>
        {
            Label(tiny: false, y: 0f),
            Label(tiny: false, y: 24f),
            Label(tiny: true, y: 100f, height: 24f),
            // gap of 100 -- far more than one row height (24)
            Control(interactive: true, y: 224f),
        };

        IReadOnlyList<bool> headings = TinyFontHeadingDetector.FindHeadings(rows);

        Assert.False(headings[2]);
    }

    [Fact]
    public void ReadOnlyDisplayFollows_NeverQualifies()
    {
        var rows = new List<TinyHeadingCandidateRow>
        {
            Label(tiny: false, y: 0f),
            Label(tiny: false, y: 24f),
            Label(tiny: true, y: 100f),
            Control(interactive: false, y: 124f), // a FillableBar or similar read-only display
        };

        IReadOnlyList<bool> headings = TinyFontHeadingDetector.FindHeadings(rows);

        Assert.False(headings[2]);
    }

    [Fact]
    public void InterleavedRowFromADifferentClip_IsSkippedOver()
    {
        var rows = new List<TinyHeadingCandidateRow>
        {
            Label(tiny: false, y: 0f),
            Label(tiny: false, y: 24f),
            Label(tiny: true, y: 100f, clip: 1),
            Control(interactive: true, y: 105f, clip: 2), // a different pane's row drawn in between
            Control(interactive: true, y: 124f, clip: 1),
        };

        IReadOnlyList<bool> headings = TinyFontHeadingDetector.FindHeadings(rows);

        Assert.True(headings[2]);
    }

    [Fact]
    public void EmptyOrNullInput_ReturnsNoHeadings()
    {
        Assert.Empty(TinyFontHeadingDetector.FindHeadings(new List<TinyHeadingCandidateRow>()));
        Assert.Empty(TinyFontHeadingDetector.FindHeadings(null));
    }
}
