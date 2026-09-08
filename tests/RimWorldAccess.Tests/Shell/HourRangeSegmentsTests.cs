using System.Collections.Generic;
using RimWorldAccess.Shell;

namespace RimWorldAccess.Tests.Shell;

public class HourRangeSegmentsTests
{
    [Fact]
    public void Compute_EmptyInput_YieldsNoSegments()
    {
        Assert.Empty(HourRangeSegments.Compute(new List<string>()));
        Assert.Empty(HourRangeSegments.Compute(null));
    }

    [Fact]
    public void Compute_UniformDay_IsOneSegment()
    {
        var labels = new string[24];
        for (int i = 0; i < 24; i++) labels[i] = "Anything";

        var segments = HourRangeSegments.Compute(labels);

        var segment = Assert.Single(segments);
        Assert.Equal(0, segment.Start);
        Assert.Equal(23, segment.End);
        Assert.Equal("Anything", segment.Label);
    }

    [Fact]
    public void Compute_TypicalSchedule_SplitsOnEveryChange()
    {
        // Sleep 0-5, Work 6-15, Anything 16-21, Sleep 22-23.
        var labels = new List<string>();
        for (int i = 0; i < 6; i++) labels.Add("Sleep");
        for (int i = 6; i < 16; i++) labels.Add("Work");
        for (int i = 16; i < 22; i++) labels.Add("Anything");
        for (int i = 22; i < 24; i++) labels.Add("Sleep");

        var segments = HourRangeSegments.Compute(labels);

        Assert.Equal(4, segments.Count);
        Assert.Equal((0, 5, "Sleep"), (segments[0].Start, segments[0].End, segments[0].Label));
        Assert.Equal((6, 15, "Work"), (segments[1].Start, segments[1].End, segments[1].Label));
        Assert.Equal((16, 21, "Anything"), (segments[2].Start, segments[2].End, segments[2].Label));
        Assert.Equal((22, 23, "Sleep"), (segments[3].Start, segments[3].End, segments[3].Label));
    }

    [Fact]
    public void Compute_SingleHourRun_KeepsStartEqualEnd()
    {
        var segments = HourRangeSegments.Compute(new[] { "Sleep", "Joy", "Sleep" });

        Assert.Equal(3, segments.Count);
        Assert.Equal(segments[1].Start, segments[1].End);
        Assert.Equal(1, segments[1].Start);
        Assert.Equal("Joy", segments[1].Label);
    }

    [Fact]
    public void Compute_NullLabels_GroupAsEmpty()
    {
        var segments = HourRangeSegments.Compute(new[] { null, "", "Work" });

        Assert.Equal(2, segments.Count);
        Assert.Equal((0, 1, ""), (segments[0].Start, segments[0].End, segments[0].Label));
        Assert.Equal((2, 2, "Work"), (segments[1].Start, segments[1].End, segments[1].Label));
    }
}
