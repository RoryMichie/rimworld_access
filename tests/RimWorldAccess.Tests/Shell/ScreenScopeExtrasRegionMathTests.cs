using RimWorldAccess.Shell;
using Xunit;

namespace RimWorldAccess.Tests.Shell;

/// <summary>
/// ScreenScope itself is Window/Verse-coupled and cannot link into this
/// project, so these tests exercise the underlying ScreenModel exactly the
/// way ScreenScope.RefreshModel assembles its regionSpecs for a screen with
/// N content regions, an optional captured-extras region, and the automatic
/// Buttons region — the region-index math the new captured-extras region
/// slots itself into (see ScreenScope.Game.cs's ActionsRegionIndex/
/// InExtrasRegion/InActionsRegion).
/// </summary>
public class ScreenScopeExtrasRegionMathTests
{
    [Fact]
    public void WithExtrasRegion_TabWalksContentThenExtrasThenButtonsInOrder()
    {
        // 2 content regions (3, 4 items), 1 extras region (2 rows), 1 buttons region (5 actions).
        var model = new ScreenModel();
        model.SetRegions(new[] { 3, 4, 2, 5 }, itemWrap: false);

        Assert.Equal(4, model.RegionCount);
        Assert.Equal(0, model.RegionIndex);

        model.NextRegion();
        Assert.Equal(1, model.RegionIndex); // second content region

        model.NextRegion();
        Assert.Equal(2, model.RegionIndex); // extras region (index == ContentRegionCount)
        Assert.Equal(2, model.CurrentRegion.Count);

        model.NextRegion();
        Assert.Equal(3, model.RegionIndex); // buttons region, shifted by 1
        Assert.Equal(5, model.CurrentRegion.Count);
    }

    [Fact]
    public void WithoutExtrasRegion_ButtonsRegionSitsRightAfterContent()
    {
        var model = new ScreenModel();
        model.SetRegions(new[] { 3, 4, 5 }, itemWrap: false); // content, content, buttons — no extras

        model.MoveToRegion(2);
        Assert.Equal(2, model.RegionIndex); // buttons at ContentRegionCount (no +1 shift)
        Assert.Equal(5, model.CurrentRegion.Count);
    }

    /// <summary>
    /// ScreenModel.SetRegions matches regions by POSITIONAL SLOT, not by
    /// identity — a pre-existing characteristic shared by every region kind,
    /// not something the captured-extras region changes. When the extras
    /// region (a middle slot) drops out, the slot it occupied is repurposed
    /// for whatever now falls at that position (here, the buttons spec), and
    /// any list beyond the new count is discarded from the TAIL. So a
    /// screen's own RefreshModel must re-derive InActionsRegion()/
    /// InExtrasRegion() fresh from the CURRENT counts every call — exactly
    /// what ScreenScope already does — rather than assume a region's cursor
    /// tracks "the buttons region" across a spec-count change.
    /// </summary>
    [Fact]
    public void ExtrasRegionDisappearing_ButtonsSlotIsRepurposedPositionally()
    {
        var model = new ScreenModel();
        model.SetRegions(new[] { 3, 4, 2, 5 }, itemWrap: false);
        model.MoveToRegion(3); // buttons region (slot 3)
        model.CurrentRegion.MoveTo(4);

        // Extras rows drop to zero (e.g. every captured widget became
        // mirrored) — ScreenScope stops emitting that RegionSpec entirely,
        // exactly like HasExtrasRegion() flipping false mid-session.
        model.SetRegions(new[] { 3, 4, 5 }, itemWrap: false);

        Assert.Equal(3, model.RegionCount);
        Assert.Equal(2, model.RegionIndex); // clamped down; buttons now sits at ContentRegionCount directly
        Assert.Equal(5, model.CurrentRegion.Count); // slot 2 now carries the buttons spec...
        Assert.Equal(0, model.CurrentRegion.Index); // ...but inherited slot 2's OWN prior cursor (extras', never moved), not buttons' discarded one
    }

    [Fact]
    public void ExtrasRegionAppearing_InsertsBetweenContentAndButtonsAtANewSlot()
    {
        var model = new ScreenModel();
        model.SetRegions(new[] { 3, 4, 5 }, itemWrap: false); // no extras yet
        model.MoveToRegion(2); // buttons (slot 2)
        model.CurrentRegion.MoveTo(4);

        // A mod injects an extra widget this pass — HasExtrasRegion() flips true.
        model.SetRegions(new[] { 3, 4, 2, 5 }, itemWrap: false);

        Assert.Equal(4, model.RegionCount);
        // RegionIndex is a plain slot number, unmoved by the insert: it now
        // (correctly) points at the newly-inserted extras region, not
        // buttons — a screen re-reading InActionsRegion() fresh (as
        // ScreenScope's own RefreshModel does on every navigation) sees this
        // immediately rather than assuming the cursor followed "the buttons
        // region" by identity.
        Assert.Equal(2, model.RegionIndex);
        Assert.Equal(2, model.Region(2).Count); // slot 2 is now extras (2 rows), clamping the old buttons cursor (4) down...
        Assert.Equal(1, model.Region(2).Index); // ...to its own max valid index
        Assert.Equal(5, model.Region(3).Count); // buttons is a brand-new slot at 3, with a fresh cursor
        Assert.Equal(0, model.Region(3).Index);
    }

    [Fact]
    public void SingleContentRegion_ExtrasAndButtons_AllThreeRegionsCycle()
    {
        var model = new ScreenModel();
        model.SetRegions(new[] { 3, 1, 2 }, itemWrap: false); // content, extras(1 row), buttons(2)

        Assert.Equal(MoveKind.Moved, model.NextRegion().Kind);
        Assert.Equal(1, model.RegionIndex);
        Assert.Equal(MoveKind.Moved, model.NextRegion().Kind);
        Assert.Equal(2, model.RegionIndex);
        Assert.Equal(MoveKind.Wrapped, model.NextRegion().Kind);
        Assert.Equal(0, model.RegionIndex);
    }
}
