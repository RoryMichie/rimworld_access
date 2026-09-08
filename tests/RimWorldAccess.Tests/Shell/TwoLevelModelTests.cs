using RimWorldAccess.Shell;

namespace RimWorldAccess.Tests.Shell;

public class TwoLevelModelTests
{
    [Fact]
    public void StartsInList_EnterDetailLandsOnHeader()
    {
        var model = new TwoLevelModel(itemCount: 3);
        Assert.Equal(TwoLevelArea.List, model.Area);

        model.EnterDetail(contentLineCount: 2, itemButtonCount: 2);
        Assert.True(model.InDetail);
        Assert.Equal(TwoLevelArea.Header, model.Area);
        Assert.Equal(0, model.DetailPosition);
    }

    [Fact]
    public void Next_WalksHeaderLinesThenEntersButtons_AndStaysThere()
    {
        var model = new TwoLevelModel(1);
        model.EnterDetail(2, 2);

        Assert.Equal(MoveKind.Moved, model.NextDetailPosition().Kind);
        Assert.Equal(TwoLevelArea.ContentLine, model.Area);
        Assert.Equal(0, model.ContentLineIndex);

        model.NextDetailPosition();
        Assert.Equal(1, model.ContentLineIndex);

        Assert.Equal(MoveKind.Moved, model.NextDetailPosition().Kind);
        Assert.Equal(TwoLevelArea.Buttons, model.Area);
        Assert.Equal(0, model.ButtonIndex);

        // Down inside buttons stays put — Left/Right navigate buttons.
        Assert.Equal(MoveKind.AtEdge, model.NextDetailPosition().Kind);
        Assert.Equal(TwoLevelArea.Buttons, model.Area);
    }

    [Fact]
    public void Next_WithoutButtons_StopsAtLastLine()
    {
        var model = new TwoLevelModel(1);
        model.EnterDetail(2, 0);

        model.NextDetailPosition();
        model.NextDetailPosition();
        Assert.Equal(1, model.ContentLineIndex);
        Assert.Equal(MoveKind.AtEdge, model.NextDetailPosition().Kind);
        Assert.Equal(TwoLevelArea.ContentLine, model.Area);
    }

    [Fact]
    public void Previous_FromButtons_ReturnsToLastContentLine()
    {
        var model = new TwoLevelModel(1);
        model.EnterDetail(2, 1);
        model.NextDetailPosition();
        model.NextDetailPosition();
        model.NextDetailPosition();
        Assert.Equal(TwoLevelArea.Buttons, model.Area);

        Assert.Equal(MoveKind.Moved, model.PreviousDetailPosition().Kind);
        Assert.Equal(TwoLevelArea.ContentLine, model.Area);
        Assert.Equal(1, model.ContentLineIndex);
    }

    [Fact]
    public void Previous_FromButtons_WithNoContentLines_ReturnsToHeader()
    {
        var model = new TwoLevelModel(1);
        model.EnterDetail(0, 1);
        model.NextDetailPosition();
        Assert.Equal(TwoLevelArea.Buttons, model.Area);

        model.PreviousDetailPosition();
        Assert.Equal(TwoLevelArea.Header, model.Area);
        Assert.Equal(MoveKind.AtEdge, model.PreviousDetailPosition().Kind);
    }

    [Fact]
    public void Buttons_NavigateWithOptionalWrap()
    {
        var model = new TwoLevelModel(1) { WrapButtons = false };
        model.EnterDetail(0, 3);
        model.NextDetailPosition();

        Assert.Equal(MoveKind.Moved, model.NextButton().Kind);
        Assert.Equal(MoveKind.Moved, model.NextButton().Kind);
        Assert.Equal(2, model.ButtonIndex);
        Assert.Equal(MoveKind.AtEdge, model.NextButton().Kind);

        model.WrapButtons = true;
        Assert.Equal(MoveKind.Wrapped, model.NextButton().Kind);
        Assert.Equal(0, model.ButtonIndex);
        Assert.Equal(MoveKind.Wrapped, model.PreviousButton().Kind);
        Assert.Equal(2, model.ButtonIndex);
    }

    [Fact]
    public void Buttons_OutsideButtonsSection_ReportEmpty()
    {
        var model = new TwoLevelModel(1);
        model.EnterDetail(2, 2);
        Assert.Equal(MoveKind.Empty, model.NextButton().Kind);
    }

    [Fact]
    public void HomeEnd_JumpWithinContentOnly()
    {
        var model = new TwoLevelModel(1);
        model.EnterDetail(3, 2);

        Assert.Equal(MoveKind.Moved, model.LastDetailPosition().Kind);
        Assert.Equal(2, model.ContentLineIndex); // last line, not buttons
        Assert.Equal(MoveKind.AtEdge, model.LastDetailPosition().Kind);

        Assert.Equal(MoveKind.Moved, model.FirstDetailPosition().Kind);
        Assert.Equal(TwoLevelArea.Header, model.Area);
        Assert.Equal(MoveKind.AtEdge, model.FirstDetailPosition().Kind);
    }

    [Fact]
    public void ExitDetail_ReturnsToList_FalseWhenAlreadyThere()
    {
        var model = new TwoLevelModel(2);
        model.EnterDetail(1, 1);
        Assert.True(model.ExitDetail());
        Assert.Equal(TwoLevelArea.List, model.Area);
        Assert.False(model.ExitDetail());
    }

    [Fact]
    public void SetShape_ClampsCursorIntoNewShape()
    {
        var model = new TwoLevelModel(1);
        model.EnterDetail(3, 2);
        model.LastDetailPosition();
        model.NextDetailPosition(); // into buttons
        model.NextButton();
        Assert.Equal(1, model.ButtonIndex);

        model.SetShape(1, 1);
        Assert.Equal(0, model.ButtonIndex);
        Assert.True(model.DetailPosition <= 2);

        model.SetShape(1, 0);
        Assert.NotEqual(TwoLevelArea.Buttons, model.Area);
    }

    [Fact]
    public void MovesOutsideDetail_ReportEmpty()
    {
        var model = new TwoLevelModel(2);
        Assert.Equal(MoveKind.Empty, model.NextDetailPosition().Kind);
        Assert.Equal(MoveKind.Empty, model.PreviousDetailPosition().Kind);
        Assert.Equal(MoveKind.Empty, model.FirstDetailPosition().Kind);
        Assert.Equal(MoveKind.Empty, model.LastDetailPosition().Kind);
    }
}
