using RimWorldAccess.Shell;
using UnityEngine;

namespace RimWorldAccess.Tests.Shell;

public class SharedMenuGrammarTests
{
    private static ActionCatalog Registered()
    {
        var catalog = new ActionCatalog();
        SharedMenuGrammar.Register(catalog);
        return catalog;
    }

    [Fact]
    public void Register_AddsEveryGrammarAction_InMenusCategory()
    {
        var catalog = Registered();
        var ids = new[]
        {
            SharedMenuGrammar.Next,
            SharedMenuGrammar.Previous,
            SharedMenuGrammar.NextHorizontal,
            SharedMenuGrammar.PreviousHorizontal,
            SharedMenuGrammar.First,
            SharedMenuGrammar.Last,
            SharedMenuGrammar.Activate,
            SharedMenuGrammar.ActivateAlias,
            SharedMenuGrammar.SearchSettle,
            SharedMenuGrammar.ActivateDefault,
            SharedMenuGrammar.Cancel,
            SharedMenuGrammar.SearchBackspace,
            SharedMenuGrammar.NextRegion,
            SharedMenuGrammar.PreviousRegion,
            SharedMenuGrammar.Info,
            SharedMenuGrammar.SortColumn,
            SharedMenuGrammar.ReorderUp,
            SharedMenuGrammar.ReorderDown,
            SharedMenuGrammar.ReorderLeft,
            SharedMenuGrammar.ReorderRight,
            SharedMenuGrammar.RouteToPointer,
            SharedMenuGrammar.ToggleMouseTracking,
        };

        Assert.Equal(ids.Length, catalog.Count);
        foreach (var id in ids)
        {
            Assert.True(catalog.TryGet(id, out var action), "missing grammar action: " + id);
            Assert.Equal(ActionCategory.Menus, action.Category);
        }
    }

    [Fact]
    public void Register_DefaultChords_AreTodaysMenuKeys()
    {
        var catalog = Registered();

        AssertSingleDefault(catalog, SharedMenuGrammar.Next, KeyCode.DownArrow);
        AssertSingleDefault(catalog, SharedMenuGrammar.Previous, KeyCode.UpArrow);
        AssertSingleDefault(catalog, SharedMenuGrammar.NextHorizontal, KeyCode.RightArrow);
        AssertSingleDefault(catalog, SharedMenuGrammar.PreviousHorizontal, KeyCode.LeftArrow);
        AssertSingleDefault(catalog, SharedMenuGrammar.First, KeyCode.Home);
        AssertSingleDefault(catalog, SharedMenuGrammar.Last, KeyCode.End);
        AssertSingleDefault(catalog, SharedMenuGrammar.ActivateAlias, KeyCode.Space);
        AssertSingleDefault(catalog, SharedMenuGrammar.Cancel, KeyCode.Escape);
        AssertSingleDefault(catalog, SharedMenuGrammar.SearchBackspace, KeyCode.Backspace);

        Assert.True(catalog.TryGet(SharedMenuGrammar.Info, out var info));
        Assert.Equal(1, info.Defaults.Count);
        Assert.Equal(KeyChord.Of(KeyCode.I, alt: true), info.Defaults[0]);

        Assert.True(catalog.TryGet(SharedMenuGrammar.Activate, out var activate));
        Assert.Equal(2, activate.Defaults.Count);
        Assert.Equal(KeyChord.Of(KeyCode.Return), activate.Defaults[0]);
        Assert.Equal(KeyChord.Of(KeyCode.KeypadEnter), activate.Defaults[1]);

        Assert.True(catalog.TryGet(SharedMenuGrammar.SearchSettle, out var searchSettle));
        Assert.Equal(2, searchSettle.Defaults.Count);
        Assert.Equal(KeyChord.Of(KeyCode.Return, shift: true), searchSettle.Defaults[0]);
        Assert.Equal(KeyChord.Of(KeyCode.KeypadEnter, shift: true), searchSettle.Defaults[1]);

        Assert.True(catalog.TryGet(SharedMenuGrammar.ActivateDefault, out var activateDefault));
        Assert.Equal(2, activateDefault.Defaults.Count);
        Assert.Equal(KeyChord.Of(KeyCode.Return, shift: true), activateDefault.Defaults[0]);
        Assert.Equal(KeyChord.Of(KeyCode.KeypadEnter, shift: true), activateDefault.Defaults[1]);

        Assert.True(catalog.TryGet(SharedMenuGrammar.SortColumn, out var sortColumn));
        Assert.Equal(1, sortColumn.Defaults.Count);
        Assert.Equal(KeyChord.Of(KeyCode.S, alt: true), sortColumn.Defaults[0]);

        AssertSingleDefault(catalog, SharedMenuGrammar.NextRegion, KeyCode.Tab);
        Assert.True(catalog.TryGet(SharedMenuGrammar.PreviousRegion, out var previousRegion));
        Assert.Equal(1, previousRegion.Defaults.Count);
        Assert.Equal(KeyChord.Of(KeyCode.Tab, shift: true), previousRegion.Defaults[0]);

        Assert.True(catalog.TryGet(SharedMenuGrammar.ReorderUp, out var reorderUp));
        Assert.Equal(1, reorderUp.Defaults.Count);
        Assert.Equal(KeyChord.Of(KeyCode.UpArrow, ctrl: true), reorderUp.Defaults[0]);

        Assert.True(catalog.TryGet(SharedMenuGrammar.ReorderDown, out var reorderDown));
        Assert.Equal(1, reorderDown.Defaults.Count);
        Assert.Equal(KeyChord.Of(KeyCode.DownArrow, ctrl: true), reorderDown.Defaults[0]);

        Assert.True(catalog.TryGet(SharedMenuGrammar.ReorderLeft, out var reorderLeft));
        Assert.Equal(1, reorderLeft.Defaults.Count);
        Assert.Equal(KeyChord.Of(KeyCode.LeftArrow, ctrl: true), reorderLeft.Defaults[0]);

        Assert.True(catalog.TryGet(SharedMenuGrammar.ReorderRight, out var reorderRight));
        Assert.Equal(1, reorderRight.Defaults.Count);
        Assert.Equal(KeyChord.Of(KeyCode.RightArrow, ctrl: true), reorderRight.Defaults[0]);

        Assert.True(catalog.TryGet(SharedMenuGrammar.ToggleMouseTracking, out var toggleMouseTracking));
        Assert.Equal(1, toggleMouseTracking.Defaults.Count);
        Assert.Equal(KeyChord.Of(KeyCode.M, shift: true, alt: true), toggleMouseTracking.Defaults[0]);
    }

    [Fact]
    public void IsCursorMove_TrueForTheMovesThatOnlyRelocateTheCursor()
    {
        Assert.True(SharedMenuGrammar.IsCursorMove(SharedMenuGrammar.Next));
        Assert.True(SharedMenuGrammar.IsCursorMove(SharedMenuGrammar.Previous));
        Assert.True(SharedMenuGrammar.IsCursorMove(SharedMenuGrammar.NextHorizontal));
        Assert.True(SharedMenuGrammar.IsCursorMove(SharedMenuGrammar.PreviousHorizontal));
        Assert.True(SharedMenuGrammar.IsCursorMove(SharedMenuGrammar.First));
        Assert.True(SharedMenuGrammar.IsCursorMove(SharedMenuGrammar.Last));
        Assert.True(SharedMenuGrammar.IsCursorMove(SharedMenuGrammar.NextRegion));
        Assert.True(SharedMenuGrammar.IsCursorMove(SharedMenuGrammar.PreviousRegion));
        Assert.True(SharedMenuGrammar.IsCursorMove(SharedMenuGrammar.SearchSettle));
    }

    [Fact]
    public void IsCursorMove_FalseForActionsThatSpeakTheirOwnResult()
    {
        Assert.False(SharedMenuGrammar.IsCursorMove(SharedMenuGrammar.Activate));
        Assert.False(SharedMenuGrammar.IsCursorMove(SharedMenuGrammar.ActivateAlias));
        Assert.False(SharedMenuGrammar.IsCursorMove(SharedMenuGrammar.ActivateDefault));
        Assert.False(SharedMenuGrammar.IsCursorMove(SharedMenuGrammar.Cancel));
        Assert.False(SharedMenuGrammar.IsCursorMove(SharedMenuGrammar.SearchBackspace));
        Assert.False(SharedMenuGrammar.IsCursorMove(SharedMenuGrammar.SortColumn));
        Assert.False(SharedMenuGrammar.IsCursorMove(SharedMenuGrammar.Info));
        Assert.False(SharedMenuGrammar.IsCursorMove(SharedMenuGrammar.ReorderUp));
        Assert.False(SharedMenuGrammar.IsCursorMove(SharedMenuGrammar.ReorderDown));
        Assert.False(SharedMenuGrammar.IsCursorMove(SharedMenuGrammar.ReorderLeft));
        Assert.False(SharedMenuGrammar.IsCursorMove(SharedMenuGrammar.ReorderRight));
    }

    [Fact]
    public void IsCursorMove_FalseForNullOrUnknownId()
    {
        Assert.False(SharedMenuGrammar.IsCursorMove(null));
        Assert.False(SharedMenuGrammar.IsCursorMove(string.Empty));
        Assert.False(SharedMenuGrammar.IsCursorMove("someScreen.next"));
    }

    private static void AssertSingleDefault(ActionCatalog catalog, string id, KeyCode key)
    {
        Assert.True(catalog.TryGet(id, out var action), "missing grammar action: " + id);
        Assert.Equal(1, action.Defaults.Count);
        Assert.Equal(KeyChord.Of(key), action.Defaults[0]);
    }
}
