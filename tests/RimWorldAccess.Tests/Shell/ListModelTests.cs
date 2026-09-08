using RimWorldAccess.Shell;

namespace RimWorldAccess.Tests.Shell;

public class ListModelTests
{
    [Fact]
    public void EmptyList_ReportsEmptyOnEveryMove()
    {
        var list = new ListModel(0);
        Assert.True(list.IsEmpty);
        Assert.Equal(-1, list.Index);
        Assert.Equal(MoveKind.Empty, list.MoveNext().Kind);
        Assert.Equal(MoveKind.Empty, list.MovePrevious().Kind);
        Assert.Equal(MoveKind.Empty, list.MoveFirst().Kind);
        Assert.Equal(MoveKind.Empty, list.MoveLast().Kind);
    }

    [Fact]
    public void MoveNext_StopsAtEdgeWithoutWrap()
    {
        var list = new ListModel(3);

        Assert.Equal(MoveKind.Moved, list.MoveNext().Kind);
        Assert.Equal(MoveKind.Moved, list.MoveNext().Kind);
        Assert.Equal(2, list.Index);

        var atEdge = list.MoveNext();
        Assert.Equal(MoveKind.AtEdge, atEdge.Kind);
        Assert.Equal(2, list.Index);
        Assert.False(atEdge.Changed);
    }

    [Fact]
    public void MoveNext_WrapsWhenEnabled()
    {
        var list = new ListModel(3, wrap: true);
        list.MoveLast();

        var wrapped = list.MoveNext();
        Assert.Equal(MoveKind.Wrapped, wrapped.Kind);
        Assert.Equal(0, list.Index);
        Assert.True(wrapped.Changed);

        var wrappedBack = list.MovePrevious();
        Assert.Equal(MoveKind.Wrapped, wrappedBack.Kind);
        Assert.Equal(2, list.Index);
    }

    [Fact]
    public void MoveBy_PageJumpsClampToEnds_AndNeverWrap()
    {
        var list = new ListModel(10, wrap: true);

        Assert.Equal(MoveKind.Moved, list.MoveBy(5).Kind);
        Assert.Equal(5, list.Index);

        // Page past the end clamps to the last item (no wrap on multi-step moves).
        Assert.Equal(MoveKind.Moved, list.MoveBy(50).Kind);
        Assert.Equal(9, list.Index);
        Assert.Equal(MoveKind.AtEdge, list.MoveBy(5).Kind);

        Assert.Equal(MoveKind.Moved, list.MoveBy(-50).Kind);
        Assert.Equal(0, list.Index);
        Assert.Equal(MoveKind.AtEdge, list.MoveBy(-5).Kind);
    }

    [Fact]
    public void FirstLast_ReportAtEdgeWhenAlreadyThere()
    {
        var list = new ListModel(4);
        Assert.Equal(MoveKind.AtEdge, list.MoveFirst().Kind);
        Assert.Equal(MoveKind.Moved, list.MoveLast().Kind);
        Assert.Equal(MoveKind.AtEdge, list.MoveLast().Kind);
        Assert.Equal(3, list.Index);
    }

    [Fact]
    public void MoveTo_JumpsAndValidates()
    {
        var list = new ListModel(5);
        Assert.Equal(MoveKind.Moved, list.MoveTo(3).Kind);
        Assert.Equal(4, list.Position);
        Assert.Equal(MoveKind.AtEdge, list.MoveTo(3).Kind);
        Assert.Throws<System.ArgumentOutOfRangeException>(() => list.MoveTo(5));
    }

    [Fact]
    public void SetCount_ClampsCursor()
    {
        var list = new ListModel(5);
        list.MoveLast();

        list.SetCount(3);
        Assert.Equal(2, list.Index);

        list.SetCount(0);
        Assert.Equal(-1, list.Index);
        Assert.True(list.IsEmpty);

        list.SetCount(2);
        Assert.Equal(0, list.Index);
    }
}
