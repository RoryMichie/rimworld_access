using RimWorldAccess.Shell;

namespace RimWorldAccess.Tests.Shell;

public class ScreenModelTests
{
    private static ScreenModel Screen(params int[] counts)
    {
        var model = new ScreenModel();
        model.SetRegions(counts, itemWrap: false);
        return model;
    }

    [Fact]
    public void StartsEmpty_MovesReportEmpty()
    {
        var model = new ScreenModel();
        Assert.Equal(0, model.RegionCount);
        Assert.Equal(-1, model.RegionIndex);
        Assert.Null(model.CurrentRegion);
        Assert.Equal(MoveKind.Empty, model.NextRegion().Kind);
        Assert.Equal(MoveKind.Empty, model.PreviousRegion().Kind);
        Assert.Equal(MoveKind.Empty, model.MoveToRegion(0).Kind);
    }

    [Fact]
    public void RegionCycle_WrapsByDefault()
    {
        var model = Screen(3, 5, 2);
        Assert.Equal(0, model.RegionIndex);
        Assert.Equal(MoveKind.Moved, model.NextRegion().Kind);
        Assert.Equal(MoveKind.Moved, model.NextRegion().Kind);
        Assert.Equal(2, model.RegionIndex);
        Assert.Equal(MoveKind.Wrapped, model.NextRegion().Kind);
        Assert.Equal(0, model.RegionIndex);
        Assert.Equal(MoveKind.Wrapped, model.PreviousRegion().Kind);
        Assert.Equal(2, model.RegionIndex);
    }

    [Fact]
    public void RegionCycle_NoWrapStopsAtEdges()
    {
        var model = Screen(3, 5);
        model.WrapRegions = false;
        Assert.Equal(MoveKind.AtEdge, model.PreviousRegion().Kind);
        Assert.Equal(0, model.RegionIndex);
        Assert.Equal(MoveKind.Moved, model.NextRegion().Kind);
        Assert.Equal(MoveKind.AtEdge, model.NextRegion().Kind);
        Assert.Equal(1, model.RegionIndex);
    }

    [Fact]
    public void SingleRegion_TabIsAtEdgeRegardlessOfWrap()
    {
        var model = Screen(4);
        Assert.Equal(MoveKind.AtEdge, model.NextRegion().Kind);
        Assert.Equal(MoveKind.AtEdge, model.PreviousRegion().Kind);
    }

    [Fact]
    public void PositionMemory_KeptPerRegionAcrossSwitches()
    {
        var model = Screen(5, 5);
        model.CurrentRegion.MoveTo(3);
        model.NextRegion();
        model.CurrentRegion.MoveTo(1);
        model.PreviousRegion();
        Assert.Equal(3, model.CurrentRegion.Index);
        model.NextRegion();
        Assert.Equal(1, model.CurrentRegion.Index);
    }

    [Fact]
    public void PositionMemory_OffRewindsTheEnteredRegion()
    {
        var model = Screen(5, 5);
        model.RememberPositions = false;
        model.CurrentRegion.MoveTo(3);
        model.NextRegion();
        model.CurrentRegion.MoveTo(2);
        model.PreviousRegion();
        Assert.Equal(0, model.CurrentRegion.Index);
    }

    [Fact]
    public void PositionMemory_OffDoesNotRewindOnFailedMove()
    {
        var model = Screen(5, 5);
        model.RememberPositions = false;
        model.WrapRegions = false;
        model.CurrentRegion.MoveTo(3);
        Assert.Equal(MoveKind.AtEdge, model.PreviousRegion().Kind);
        Assert.Equal(3, model.CurrentRegion.Index);
    }

    [Fact]
    public void MoveToRegion_JumpsDirectly()
    {
        var model = Screen(2, 2, 2);
        Assert.Equal(MoveKind.Moved, model.MoveToRegion(2).Kind);
        Assert.Equal(2, model.RegionIndex);
        Assert.Equal(3, model.RegionPosition);
        Assert.Equal(MoveKind.AtEdge, model.MoveToRegion(2).Kind);
    }

    [Fact]
    public void SetRegions_PreservesCursorsOnRefresh()
    {
        var model = Screen(5, 5);
        model.CurrentRegion.MoveTo(4);
        model.NextRegion();
        model.CurrentRegion.MoveTo(2);
        model.SetRegions(new[] { 3, 5 }, itemWrap: false);
        Assert.Equal(1, model.RegionIndex);
        Assert.Equal(2, model.CurrentRegion.Index);
        Assert.Equal(2, model.Region(0).Index);
    }

    [Fact]
    public void SetRegions_ClampsRegionCursorWhenRegionsDisappear()
    {
        var model = Screen(2, 2, 2);
        model.MoveToRegion(2);
        model.SetRegions(new[] { 2, 2 }, itemWrap: false);
        Assert.Equal(1, model.RegionIndex);
    }

    [Fact]
    public void SetRegions_ToEmptyAndBack()
    {
        var model = Screen(2, 2);
        model.SetRegions(System.Array.Empty<int>(), itemWrap: false);
        Assert.Equal(0, model.RegionCount);
        Assert.Null(model.CurrentRegion);
        model.SetRegions(new[] { 3 }, itemWrap: false);
        Assert.Equal(0, model.RegionIndex);
        Assert.Equal(3, model.CurrentRegion.Count);
    }

    [Fact]
    public void SetRegions_AppliesItemWrapToEveryRegion()
    {
        var model = Screen(2, 2);
        model.SetRegions(new[] { 2, 2 }, itemWrap: true);
        Assert.True(model.Region(0).Wrap);
        Assert.True(model.Region(1).Wrap);
        model.SetRegions(new[] { 2, 2 }, itemWrap: false);
        Assert.False(model.Region(0).Wrap);
        Assert.False(model.Region(1).Wrap);
    }

    [Fact]
    public void EmptyRegion_IsSkippedByRegionCycling()
    {
        var model = Screen(3, 0, 2);
        Assert.Equal(MoveKind.Moved, model.NextRegion().Kind);
        Assert.Equal(2, model.RegionIndex);       // slot 1 holds nothing, so it does not exist
        Assert.Equal(MoveKind.Wrapped, model.NextRegion().Kind);
        Assert.Equal(0, model.RegionIndex);
        Assert.Equal(MoveKind.Wrapped, model.PreviousRegion().Kind);
        Assert.Equal(2, model.RegionIndex);
        Assert.Equal(MoveKind.Moved, model.PreviousRegion().Kind);
        Assert.Equal(0, model.RegionIndex);
    }

    [Fact]
    public void EmptyRegion_SkippedWithWrapOffReportsAtEdge()
    {
        var model = Screen(3, 0, 0);
        model.WrapRegions = false;
        Assert.Equal(MoveKind.AtEdge, model.NextRegion().Kind);
        Assert.Equal(0, model.RegionIndex);
    }

    [Fact]
    public void EmptyRegion_RefusesDirectJumps()
    {
        var model = Screen(3, 0, 2);
        Assert.Equal(MoveKind.Empty, model.MoveToRegion(1).Kind);
        Assert.Equal(0, model.RegionIndex);
        Assert.Equal(MoveKind.Moved, model.MoveToRegion(2).Kind);
    }

    [Fact]
    public void TableRegion_WithHeaderRowOnly_CountsAsEmpty()
    {
        // A table region's header row is not an item, so a data-less table is
        // as empty as a data-less list and is skipped the same way.
        var model = new ScreenModel();
        model.SetRegions(new[] { new RegionSpec(3), new RegionSpec(1, columnCount: 4), new RegionSpec(2) }, itemWrap: false);

        Assert.True(model.IsRegionEmpty(1));
        Assert.Equal(MoveKind.Moved, model.NextRegion().Kind);
        Assert.Equal(2, model.RegionIndex);
    }

    [Fact]
    public void NearestNonEmptyRegion_PrefersThePreviousRegionThenTheNext()
    {
        var model = Screen(3, 4, 0, 2);
        model.MoveToRegion(1);
        model.SetRegions(new[] { 3, 0, 0, 2 }, itemWrap: false); // the region under the cursor empties out
        Assert.True(model.CurrentRegionIsEmpty);

        Assert.Equal(MoveKind.Moved, model.MoveToNearestNonEmptyRegion().Kind);
        Assert.Equal(0, model.RegionIndex);

        model.SetRegions(new[] { 0, 0, 0, 2 }, itemWrap: false); // nothing behind the cursor now
        Assert.Equal(MoveKind.Moved, model.MoveToNearestNonEmptyRegion().Kind);
        Assert.Equal(3, model.RegionIndex);
    }

    [Fact]
    public void EveryRegionEmpty_LeavesTheCursorWhereItIs()
    {
        var model = Screen(0, 0);
        Assert.True(model.CurrentRegionIsEmpty);
        Assert.Equal(MoveKind.Empty, model.MoveToNearestNonEmptyRegion().Kind);
        Assert.Equal(0, model.RegionIndex);
        Assert.Equal(MoveKind.AtEdge, model.NextRegion().Kind);
        Assert.Equal(0, model.NonEmptyRegionCount);
        Assert.Equal(0, model.NonEmptyRegionPosition);
    }

    [Fact]
    public void AnnouncedRegionPosition_CountsOnlyRegionsWithItems()
    {
        var model = Screen(3, 0, 2, 0, 5);
        Assert.Equal(5, model.RegionCount);          // slots never renumber
        Assert.Equal(3, model.NonEmptyRegionCount);
        Assert.Equal(1, model.NonEmptyRegionPosition);

        model.NextRegion();
        Assert.Equal(2, model.RegionIndex);
        Assert.Equal(2, model.NonEmptyRegionPosition);

        model.NextRegion();
        Assert.Equal(4, model.RegionIndex);
        Assert.Equal(3, model.NonEmptyRegionPosition);
    }

    // ------------------------------------------------------------------
    // Always-navigable regions (a switch-style screen's panes, which only
    // populate once the pane is selected).
    // ------------------------------------------------------------------

    [Fact]
    public void AlwaysNavigableRegion_IsTabReachableAndCountedWhileEmpty()
    {
        var model = new ScreenModel();
        model.SetRegions(
            new[] { new RegionSpec(3), new RegionSpec(0, alwaysNavigable: true), new RegionSpec(2) },
            itemWrap: false);

        Assert.True(model.IsRegionEmpty(1));
        Assert.True(model.IsRegionNavigable(1));
        Assert.Equal(3, model.NonEmptyRegionCount);

        Assert.Equal(MoveKind.Moved, model.NextRegion().Kind);
        Assert.Equal(1, model.RegionIndex);
        Assert.Equal(2, model.NonEmptyRegionPosition);
    }

    [Fact]
    public void AlwaysNavigableRegion_IsReachableByDirectJumpWhileEmpty()
    {
        var model = new ScreenModel();
        model.SetRegions(
            new[] { new RegionSpec(3), new RegionSpec(0, alwaysNavigable: true) },
            itemWrap: false);

        Assert.Equal(MoveKind.Moved, model.MoveToRegion(1).Kind);
        Assert.Equal(1, model.RegionIndex);
    }

    [Fact]
    public void EmptyRegion_WithoutTheFlag_StaysUnreachable()
    {
        var model = new ScreenModel();
        model.SetRegions(
            new[] { new RegionSpec(3), new RegionSpec(0), new RegionSpec(2) },
            itemWrap: false);

        Assert.False(model.IsRegionNavigable(1));
        Assert.Equal(2, model.NonEmptyRegionCount);
        Assert.Equal(MoveKind.Empty, model.MoveToRegion(1).Kind);
        Assert.Equal(MoveKind.Moved, model.NextRegion().Kind);
        Assert.Equal(2, model.RegionIndex);
    }

    [Fact]
    public void AlwaysNavigableRegion_IsNeverRelocatedInto_NorOutOf()
    {
        var model = new ScreenModel();
        model.SetRegions(
            new[] { new RegionSpec(3), new RegionSpec(0, alwaysNavigable: true) },
            itemWrap: false);

        // Relocation off an emptied region wants a region that holds something.
        model.SetRegions(
            new[] { new RegionSpec(0), new RegionSpec(0, alwaysNavigable: true) },
            itemWrap: false);
        Assert.Equal(MoveKind.Empty, model.MoveToNearestNonEmptyRegion().Kind);
        Assert.Equal(0, model.RegionIndex);

        // Standing in the flagged region while it is empty is a legitimate
        // resting place, so nothing moves the cursor off it either.
        model.SetRegions(
            new[] { new RegionSpec(3), new RegionSpec(0, alwaysNavigable: true) },
            itemWrap: false);
        model.MoveToRegion(1);
        Assert.True(model.CurrentRegionIsEmpty);
        Assert.True(model.IsRegionNavigable(model.RegionIndex));
    }

    // ------------------------------------------------------------------
    // Table regions (the table-model doctrine).
    // ------------------------------------------------------------------

    [Fact]
    public void TableRegions_GetGridState_FlatRegionsDoNot()
    {
        var model = new ScreenModel();
        model.SetRegions(new[] { new RegionSpec(4), new RegionSpec(6, columnCount: 3) }, itemWrap: false);

        Assert.Null(model.Table(0));
        Assert.NotNull(model.Table(1));
        Assert.Equal(3, model.Table(1).ColumnCount);
        Assert.Null(model.CurrentTable);           // cursor starts on the flat region
        model.NextRegion();
        Assert.Same(model.Table(1), model.CurrentTable);
        Assert.Same(model.CurrentRegion, model.CurrentTable.Rows); // one row cursor, two views
    }

    [Fact]
    public void TableRegion_RefreshKeepsColumnCursorAndSort()
    {
        var model = new ScreenModel();
        var specs = new[] { new RegionSpec(6, 3) };
        model.SetRegions(specs, itemWrap: false);
        model.Table(0).MoveToColumn(2);
        model.Table(0).ToggleSortCycle(true);

        model.SetRegions(specs, itemWrap: false); // the every-frame refresh
        Assert.Equal(2, model.Table(0).ColumnIndex);
        Assert.True(model.Table(0).HasActiveSort);
    }

    [Fact]
    public void TableRegion_KindFlipParksAndRevivesGridState()
    {
        // The submenu pattern: a table region flips to a flat option list and
        // back; the table the user returns to must still be the table they
        // left — column cursor and active sort included.
        var model = new ScreenModel();
        model.SetRegions(new[] { new RegionSpec(6, 3) }, itemWrap: false);
        model.CurrentRegion.MoveTo(4);
        model.Table(0).MoveToColumn(2);
        model.Table(0).ToggleSortCycle(true); // sorted by column 2, descending

        model.SetRegions(new[] { new RegionSpec(6) }, itemWrap: false); // table → flat (submenu)
        Assert.Null(model.Table(0));
        Assert.Equal(4, model.CurrentRegion.Index); // row cursor survives

        model.SetRegions(new[] { new RegionSpec(6, 3) }, itemWrap: false); // flat → table (return)
        Assert.NotNull(model.Table(0));
        Assert.Equal(4, model.CurrentRegion.Index);
        Assert.Equal(2, model.Table(0).ColumnIndex);      // column cursor revived
        Assert.Equal(2, model.Table(0).SortColumnIndex);  // sort revived
        Assert.True(model.Table(0).SortDescending);
    }

    [Fact]
    public void TableRegion_ReviveClampsToNewColumnCount()
    {
        var model = new ScreenModel();
        model.SetRegions(new[] { new RegionSpec(6, 4) }, itemWrap: false);
        model.Table(0).MoveToColumn(3);
        model.Table(0).ToggleSortCycle(true); // sorted by column 3

        model.SetRegions(new[] { new RegionSpec(6) }, itemWrap: false);
        model.SetRegions(new[] { new RegionSpec(6, 2) }, itemWrap: false); // revived narrower

        Assert.Equal(1, model.Table(0).ColumnIndex);   // clamped
        Assert.False(model.Table(0).HasActiveSort);    // orphaned sort reset
    }

    [Fact]
    public void PositionPolicyOff_RewindsRowAndColumnOnRegionEntry()
    {
        var model = new ScreenModel { RememberPositions = false };
        model.SetRegions(new[] { new RegionSpec(3), new RegionSpec(5, 4) }, itemWrap: false);
        model.NextRegion();
        model.CurrentRegion.MoveTo(3);
        model.CurrentTable.MoveToColumn(2);

        model.PreviousRegion();
        model.NextRegion(); // re-enter the table region
        Assert.Equal(0, model.CurrentRegion.Index);
        Assert.Equal(0, model.CurrentTable.ColumnIndex);
    }

    [Fact]
    public void IntOverload_KeepsEveryRegionFlat()
    {
        var model = Screen(3, 5);
        Assert.Null(model.Table(0));
        Assert.Null(model.Table(1));
    }

    [Fact]
    public void WrapOn_IsTheDefaultAndWraps()
    {
        var tabs = new TabSetModel(3);
        Assert.True(tabs.Wrap);
        tabs.MoveTo(2);
        Assert.Equal(MoveKind.Wrapped, tabs.Next().Kind);
        Assert.Equal(0, tabs.Index);
    }

    [Fact]
    public void WrapOff_ReportsAtEdgeAndHoldsPosition()
    {
        var tabs = new TabSetModel(3) { Wrap = false };
        Assert.Equal(MoveKind.AtEdge, tabs.Previous().Kind);
        Assert.Equal(0, tabs.Index);
        tabs.MoveTo(2);
        Assert.Equal(MoveKind.AtEdge, tabs.Next().Kind);
        Assert.Equal(2, tabs.Index);
    }

    [Fact]
    public void CycleSkipRegion_IsSteppedOverInBothDirections()
    {
        var model = Screen(3, 5, 2);
        model.CycleSkipRegion = 1;

        Assert.Equal(MoveKind.Moved, model.NextRegion().Kind);
        Assert.Equal(2, model.RegionIndex);
        Assert.Equal(MoveKind.Moved, model.PreviousRegion().Kind);
        Assert.Equal(0, model.RegionIndex);
    }

    /// <summary>
    /// Reader flow reaches the skipped region by direct jump, which is the
    /// whole point of skipping it in the cycle rather than removing it.
    /// </summary>
    [Fact]
    public void CycleSkipRegion_IsStillReachableByDirectJump()
    {
        var model = Screen(3, 5, 2);
        model.CycleSkipRegion = 2;

        Assert.Equal(MoveKind.Moved, model.MoveToRegion(2).Kind);
        Assert.Equal(2, model.RegionIndex);
    }

    [Fact]
    public void CycleSkipRegion_IsNotNumberedAmongTheTabs()
    {
        var model = Screen(3, 5, 2);
        model.CycleSkipRegion = 2;

        Assert.Equal(2, model.NonEmptyRegionCount);
        Assert.Equal(1, model.NonEmptyRegionPosition);

        model.MoveToRegion(1);
        Assert.Equal(2, model.NonEmptyRegionPosition);

        // Resting on the skipped region there is no honest ordinal to speak.
        model.MoveToRegion(2);
        Assert.Equal(0, model.NonEmptyRegionPosition);
    }
}
