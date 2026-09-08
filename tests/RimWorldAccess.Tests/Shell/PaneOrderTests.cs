using System.Collections.Generic;
using RimWorldAccess.Shell;
using Xunit;

namespace RimWorldAccess.Tests.Shell;

/// <summary>
/// Tests for <see cref="PaneOrder"/>, the geometry-only rule that groups presentation rows by
/// their capture clip so a multi-pane window (Colony Manager Redux's job list / settings /
/// allowed-animals columns) reads pane by pane instead of zipper-interleaved by raw Y-then-X.
/// </summary>
public class PaneOrderTests
{
    private static PaneOrderRow Row(int clip, float x, float y)
    {
        return new PaneOrderRow { ClipId = clip, X = x, Y = y };
    }

    [Fact]
    public void ThreePanes_RankedTopToBottomThenLeftToRight()
    {
        // Three side-by-side panes whose topmost rows all start at the same Y: clip 1 (x=18) is
        // the job list, clip 2 (x=342) is the settings pane, clip 3 (x=743) is the
        // allowed-animals column — left to right, exactly as a sighted player reads them.
        var rows = new List<PaneOrderRow>
        {
            Row(clip: 1, x: 18f, y: 68f),
            Row(clip: 2, x: 342f, y: 68f),
            Row(clip: 3, x: 743f, y: 68f),
            Row(clip: 2, x: 342f, y: 136f),
            Row(clip: 3, x: 743f, y: 115f),
        };

        IReadOnlyList<int> ranks = PaneOrder.ComputePaneRanks(rows);

        Assert.Equal(0, ranks[0]); // clip 1, leftmost pane
        Assert.Equal(1, ranks[1]); // clip 2
        Assert.Equal(1, ranks[3]);
        Assert.Equal(2, ranks[2]); // clip 3, rightmost pane
        Assert.Equal(2, ranks[4]);
    }

    [Fact]
    public void ThreePanes_WithDifferingTopmostY_RanksByTheEarliestRowInEachPane()
    {
        // The real dump geometry (S1 blueprint excerpt): the allowed-animals pane's first
        // captured row (y=55) actually starts slightly ABOVE the job list's (y=68) and the
        // settings pane's (y=74) — panes are ranked by their own minimum Y, not by column order,
        // so the animals pane legitimately sorts first here.
        var rows = new List<PaneOrderRow>
        {
            Row(clip: 1, x: 18f, y: 68f),
            Row(clip: 2, x: 342f, y: 74f),
            Row(clip: 3, x: 743f, y: 55f),
            Row(clip: 2, x: 342f, y: 136f),
            Row(clip: 3, x: 743f, y: 115f),
        };

        IReadOnlyList<int> ranks = PaneOrder.ComputePaneRanks(rows);

        Assert.Equal(1, ranks[0]); // clip 1
        Assert.Equal(2, ranks[1]); // clip 2
        Assert.Equal(2, ranks[3]);
        Assert.Equal(0, ranks[2]); // clip 3 -- its earliest row (y=55) is the topmost of all
        Assert.Equal(0, ranks[4]);
    }

    [Fact]
    public void SinglePane_EveryRowRanksTheSame()
    {
        var rows = new List<PaneOrderRow>
        {
            Row(clip: 7, x: 0f, y: 0f),
            Row(clip: 7, x: 40f, y: 60f),
            Row(clip: 7, x: 10f, y: 120f),
        };

        IReadOnlyList<int> ranks = PaneOrder.ComputePaneRanks(rows);

        Assert.All(ranks, rank => Assert.Equal(0, rank));
    }

    [Fact]
    public void PanesSharingTheirTopmostY_TieBrokenByX()
    {
        var rows = new List<PaneOrderRow>
        {
            Row(clip: 2, x: 400f, y: 20f),
            Row(clip: 1, x: 0f, y: 20f),
        };

        IReadOnlyList<int> ranks = PaneOrder.ComputePaneRanks(rows);

        Assert.Equal(1, ranks[0]); // clip 2 sits to the right
        Assert.Equal(0, ranks[1]); // clip 1 sits to the left, wins the tie
    }

    [Fact]
    public void NoClipInfo_EveryRowSharesOneImplicitClip_FallsBackToCurrentBehavior()
    {
        // A caller with no clip metadata at all assigns every row the same ClipId (e.g. 0) —
        // this must degenerate to rank 0 for everyone, so a caller's own secondary sort is the
        // only thing that ever moves a row, exactly like before pane grouping existed.
        var rows = new List<PaneOrderRow>
        {
            Row(clip: 0, x: 50f, y: 10f),
            Row(clip: 0, x: 5f, y: 90f),
        };

        IReadOnlyList<int> ranks = PaneOrder.ComputePaneRanks(rows);

        Assert.Equal(0, ranks[0]);
        Assert.Equal(0, ranks[1]);
    }

    [Fact]
    public void EmptyInput_ReturnsNoRanks()
    {
        Assert.Empty(PaneOrder.ComputePaneRanks(new List<PaneOrderRow>()));
        Assert.Empty(PaneOrder.ComputePaneRanks(null));
    }
}
