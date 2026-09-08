using RimWorldAccess.Shell;

namespace RimWorldAccess.Tests.Shell;

public class TabSetModelTests
{
    [Fact]
    public void NextAndPrevious_CycleWithWrap()
    {
        var tabs = new TabSetModel(3);

        Assert.Equal(MoveKind.Moved, tabs.Next().Kind);
        Assert.Equal(MoveKind.Moved, tabs.Next().Kind);
        Assert.Equal(3, tabs.Position);

        Assert.Equal(MoveKind.Wrapped, tabs.Next().Kind);
        Assert.Equal(0, tabs.Index);

        Assert.Equal(MoveKind.Wrapped, tabs.Previous().Kind);
        Assert.Equal(2, tabs.Index);
    }

    [Fact]
    public void SingleTab_ReportsAtEdgeInsteadOfSpinning()
    {
        var tabs = new TabSetModel(1);
        Assert.Equal(MoveKind.AtEdge, tabs.Next().Kind);
        Assert.Equal(MoveKind.AtEdge, tabs.Previous().Kind);
        Assert.Equal(0, tabs.Index);
    }

    [Fact]
    public void MoveTo_JumpsAndValidates()
    {
        var tabs = new TabSetModel(4);
        Assert.Equal(MoveKind.Moved, tabs.MoveTo(2).Kind);
        Assert.Equal(MoveKind.AtEdge, tabs.MoveTo(2).Kind);
        Assert.Throws<System.ArgumentOutOfRangeException>(() => tabs.MoveTo(4));
    }

    [Fact]
    public void SetCount_ClampsCursor()
    {
        var tabs = new TabSetModel(4);
        tabs.MoveTo(3);
        tabs.SetCount(2);
        Assert.Equal(1, tabs.Index);
    }

    [Fact]
    public void Constructor_RequiresAtLeastOneTab()
    {
        Assert.Throws<System.ArgumentOutOfRangeException>(() => new TabSetModel(0));
    }
}
