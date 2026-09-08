using RimWorldAccess.Shell;

namespace RimWorldAccess.Tests.Shell;

public class SectionNavigationTests
{
    // Sections of unequal size: A at 0-1, B at 2, C at 3-6.
    private static readonly string[] Rows = { "A", "A", "B", "C", "C", "C", "C" };

    private static int Find(int index, bool forward, bool wrap = false, string[] rows = null)
    {
        var source = rows ?? Rows;
        return SectionNavigation.FindAdjacentSectionStart(
            source.Length, index, i => source[i], forward, wrap);
    }

    [Fact]
    public void Forward_LandsOnEachSectionsFirstRow()
    {
        Assert.Equal(2, Find(0, forward: true));
        Assert.Equal(2, Find(1, forward: true));
        Assert.Equal(3, Find(2, forward: true));
    }

    [Fact]
    public void Backward_SkipsToPreviousSectionsFirstRow()
    {
        Assert.Equal(2, Find(3, forward: false));
        Assert.Equal(2, Find(6, forward: false));
        Assert.Equal(0, Find(2, forward: false));
    }

    [Fact]
    public void WithoutWrap_EdgesReportNoTarget()
    {
        Assert.Equal(-1, Find(6, forward: true));
        Assert.Equal(-1, Find(3, forward: true));
        Assert.Equal(-1, Find(0, forward: false));
        Assert.Equal(-1, Find(1, forward: false));
    }

    [Fact]
    public void WithWrap_EdgesContinueFromTheFarEnd()
    {
        Assert.Equal(0, Find(3, forward: true, wrap: true));
        Assert.Equal(3, Find(0, forward: false, wrap: true));
    }

    [Fact]
    public void SingleSection_ReportsNoTargetEvenWithWrap()
    {
        var rows = new[] { "A", "A", "A" };
        Assert.Equal(-1, Find(1, forward: true, wrap: true, rows: rows));
        Assert.Equal(-1, Find(1, forward: false, wrap: true, rows: rows));
    }

    [Fact]
    public void NullAndEmptySectionNamesCompareEqual()
    {
        var rows = new[] { null, "", "B" };
        Assert.Equal(2, Find(0, forward: true, rows: rows));
        Assert.Equal(-1, Find(1, forward: false, rows: rows));
        Assert.Equal(0, Find(2, forward: false, rows: rows));
    }

    [Fact]
    public void OutOfRangeIndexAndEmptyListReportNoTarget()
    {
        Assert.Equal(-1, Find(-1, forward: true));
        Assert.Equal(-1, Find(Rows.Length, forward: true));
        Assert.Equal(-1, SectionNavigation.FindAdjacentSectionStart(
            0, 0, i => "A", forward: true, wrap: true));
    }
}
