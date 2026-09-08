using System.Collections.Generic;
using RimWorldAccess.Shell;

namespace RimWorldAccess.Tests.Shell;

/// <summary>
/// The region arithmetic every ScreenScope consumer classifies indices with
/// (ScreenScope's ActionsRegionIndex/InExtrasRegion/RegionNameFor and the
/// typeahead engine's region walk). The typeahead defect these lock down:
/// the extras region was missing from the region TOTAL, and a catch-all
/// "region >= contentRegions" branch built TOOLBAR entries for it — so typing a
/// toolbar button's name jumped into the extras list and the real toolbar was
/// never searched.
/// </summary>
public class ScreenRegionLayoutTests
{
    [Fact]
    public void ContentExtrasActions_EveryIndexClassifies()
    {
        var layout = new ScreenRegionLayout(2, hasExtras: true, hasActions: true);

        Assert.Equal(4, layout.TotalRegions);
        Assert.Equal(2, layout.ExtrasRegionIndex);
        Assert.Equal(3, layout.ActionsRegionIndex);

        Assert.Equal(ScreenRegionKind.Content, layout.KindOf(0));
        Assert.Equal(ScreenRegionKind.Content, layout.KindOf(1));
        Assert.Equal(ScreenRegionKind.Extras, layout.KindOf(2));
        Assert.Equal(ScreenRegionKind.Actions, layout.KindOf(3));
        Assert.Equal(ScreenRegionKind.None, layout.KindOf(4));
        Assert.Equal(ScreenRegionKind.None, layout.KindOf(-1));
    }

    /// <summary>
    /// The exact shape of the bug: with an extras region present, a walk over
    /// content + actions only (3 regions) stops one short of the toolbar, and
    /// the index it does reach (2) is the EXTRAS region, not the toolbar.
    /// </summary>
    [Fact]
    public void ExtrasRegionPresent_ToolbarIsTheLastIndex_NotTheExtrasIndex()
    {
        var layout = new ScreenRegionLayout(2, hasExtras: true, hasActions: true);

        Assert.Equal(4, layout.TotalRegions);
        Assert.NotEqual(layout.ExtrasRegionIndex, layout.ActionsRegionIndex);
        Assert.Equal(ScreenRegionKind.Extras, layout.KindOf(layout.ContentRegions));
        Assert.Equal(ScreenRegionKind.Actions, layout.KindOf(layout.TotalRegions - 1));
    }

    [Fact]
    public void NoExtras_ToolbarSitsRightAfterContent()
    {
        var layout = new ScreenRegionLayout(2, hasExtras: false, hasActions: true);

        Assert.Equal(3, layout.TotalRegions);
        Assert.Equal(-1, layout.ExtrasRegionIndex);
        Assert.Equal(2, layout.ActionsRegionIndex);
        Assert.Equal(ScreenRegionKind.Actions, layout.KindOf(2));
        Assert.Equal(ScreenRegionKind.None, layout.KindOf(3));
    }

    [Fact]
    public void ExtrasWithoutActions_ExtrasIsTheLastRegion()
    {
        var layout = new ScreenRegionLayout(1, hasExtras: true, hasActions: false);

        Assert.Equal(2, layout.TotalRegions);
        Assert.Equal(1, layout.ExtrasRegionIndex);
        Assert.Equal(ScreenRegionKind.Extras, layout.KindOf(1));
        // The index the toolbar WOULD occupy is still reported (ScreenScope's
        // long-standing ActionsRegionIndex semantics: content + the extras
        // shift), but nothing lives there, so it classifies as nothing.
        Assert.Equal(2, layout.ActionsRegionIndex);
        Assert.Equal(ScreenRegionKind.None, layout.KindOf(2));
    }

    [Fact]
    public void ContentOnly_NothingAutomaticClassifies()
    {
        var layout = new ScreenRegionLayout(3, hasExtras: false, hasActions: false);

        Assert.Equal(3, layout.TotalRegions);
        Assert.Equal(ScreenRegionKind.Content, layout.KindOf(2));
        Assert.Equal(ScreenRegionKind.None, layout.KindOf(3));
    }

    [Fact]
    public void NoContentRegions_AutomaticRegionsStillLandInOrder()
    {
        // A pure-toolbar screen, and the same screen once a mod injects a widget.
        var toolbarOnly = new ScreenRegionLayout(0, hasExtras: false, hasActions: true);
        Assert.Equal(1, toolbarOnly.TotalRegions);
        Assert.Equal(ScreenRegionKind.Actions, toolbarOnly.KindOf(0));

        var withExtras = new ScreenRegionLayout(0, hasExtras: true, hasActions: true);
        Assert.Equal(2, withExtras.TotalRegions);
        Assert.Equal(ScreenRegionKind.Extras, withExtras.KindOf(0));
        Assert.Equal(ScreenRegionKind.Actions, withExtras.KindOf(1));
    }

    /// <summary>
    /// The walk ScreenScope.BuildTypeaheadEntries performs (0 .. TotalRegions-1,
    /// classifying each index): with an extras region present it must reach the
    /// toolbar and visit each automatic region exactly once. The old walk stopped
    /// at content + actions, so with extras present it never reached the toolbar
    /// at all — the reason typing a button's name could not find it.
    /// </summary>
    [Fact]
    public void RegionWalk_VisitsContentThenExtrasThenActions_ExactlyOnceEach()
    {
        var layout = new ScreenRegionLayout(2, hasExtras: true, hasActions: true);

        var visited = new List<ScreenRegionKind>();
        for (int r = 0; r < layout.TotalRegions; r++)
        {
            visited.Add(layout.KindOf(r));
        }

        Assert.Equal(
            new[]
            {
                ScreenRegionKind.Content, ScreenRegionKind.Content,
                ScreenRegionKind.Extras, ScreenRegionKind.Actions,
            },
            visited);
    }

    [Fact]
    public void NegativeContentCount_ClampsToZero()
    {
        var layout = new ScreenRegionLayout(-3, hasExtras: false, hasActions: true);

        Assert.Equal(0, layout.ContentRegions);
        Assert.Equal(1, layout.TotalRegions);
        Assert.Equal(ScreenRegionKind.Actions, layout.KindOf(0));
    }
}
