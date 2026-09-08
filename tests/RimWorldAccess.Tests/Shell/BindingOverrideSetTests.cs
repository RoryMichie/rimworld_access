using System.Collections.Generic;
using RimWorldAccess.Shell;

namespace RimWorldAccess.Tests.Shell;

public class BindingOverrideSetTests
{
    private static InputAction MapAction(string id, string chord) =>
        new(id, ActionCategory.Map, new List<KeyChord> { KeyChord.Parse(chord) });

    [Fact]
    public void ToLines_FromLines_RoundTrips()
    {
        var set = new BindingOverrideSet();
        set.Set("map.cursor.north", new[] { KeyChord.Parse("Keypad8"), KeyChord.Parse("Ctrl+UpArrow") });
        set.Set("pawn.mood", new[] { KeyChord.Parse("Alt+M") });
        set.Set("map.unbound", new KeyChord[0]); // explicit unbind

        var lines = set.ToLines();
        Assert.Equal(new[]
        {
            "map.cursor.north=Keypad8;Ctrl+UpArrow",
            "map.unbound=",
            "pawn.mood=Alt+M",
        }, lines);

        var parsed = BindingOverrideSet.FromLines(lines);
        Assert.Equal(lines, parsed.ToLines());
    }

    [Fact]
    public void FromLines_DropsWholeLineOnAnyBadChord()
    {
        var parsed = BindingOverrideSet.FromLines(new[]
        {
            "map.good=F1",
            "map.halfbad=F2;NotAKey",
            "noequalsign",
            "=F3",
            "   ",
            null,
        });

        Assert.Equal(1, parsed.Count);
        Assert.True(parsed.TryGet("map.good", out var chords));
        Assert.Equal(new[] { KeyChord.Parse("F1") }, chords);
        Assert.False(parsed.TryGet("map.halfbad", out _));
    }

    [Fact]
    public void FromLines_LaterDuplicateWins()
    {
        var parsed = BindingOverrideSet.FromLines(new[]
        {
            "map.a=F1",
            "map.a=F2",
        });

        Assert.True(parsed.TryGet("map.a", out var chords));
        Assert.Equal(new[] { KeyChord.Parse("F2") }, chords);
    }

    [Fact]
    public void FromLines_NullInputYieldsEmptySet()
    {
        Assert.Equal(0, BindingOverrideSet.FromLines(null).Count);
    }

    [Fact]
    public void ApplyTo_SetsOverridesAndReportsUnknownIds()
    {
        var catalog = new ActionCatalog();
        var action = MapAction("map.a", "F1");
        catalog.Register(action);

        var set = BindingOverrideSet.FromLines(new[]
        {
            "map.a=F9",
            "gone.action=F5",
        });
        var unknown = set.ApplyTo(catalog);

        Assert.True(action.IsRebound);
        Assert.Equal(new[] { KeyChord.Parse("F9") }, action.Bindings);
        Assert.Equal(new[] { "gone.action" }, unknown);
    }

    [Fact]
    public void CaptureFrom_TakesOnlyDeltas()
    {
        var catalog = new ActionCatalog();
        catalog.Register(MapAction("map.a", "F1"));
        catalog.Register(MapAction("map.b", "F2"));
        catalog.SetBinding("map.a", new[] { KeyChord.Parse("F9") });

        var captured = BindingOverrideSet.CaptureFrom(catalog);

        Assert.Equal(new[] { "map.a=F9" }, captured.ToLines());
    }

    [Fact]
    public void CaptureFrom_CarryOverPreservesUnknownIdsButNotStaleKnownOnes()
    {
        var catalog = new ActionCatalog();
        catalog.Register(MapAction("map.a", "F1"));

        var carryOver = BindingOverrideSet.FromLines(new[]
        {
            "gone.action=F5",  // unknown to the catalog: preserved
            "map.a=F7",        // known to the catalog and no longer rebound: dropped
        });

        var captured = BindingOverrideSet.CaptureFrom(catalog, carryOver);

        Assert.Equal(new[] { "gone.action=F5" }, captured.ToLines());
    }

    [Fact]
    public void FullRoundTrip_ThroughLinesAndFreshCatalog()
    {
        var catalog = new ActionCatalog();
        catalog.Register(MapAction("map.a", "F1"));
        catalog.Register(MapAction("map.b", "F2"));
        catalog.SetBinding("map.a", new[] { KeyChord.Parse("Ctrl+Shift+F9") });

        var lines = BindingOverrideSet.CaptureFrom(catalog).ToLines();

        var freshCatalog = new ActionCatalog();
        freshCatalog.Register(MapAction("map.a", "F1"));
        freshCatalog.Register(MapAction("map.b", "F2"));
        BindingOverrideSet.FromLines(lines).ApplyTo(freshCatalog);

        Assert.Equal(new[] { KeyChord.Parse("Ctrl+Shift+F9") }, freshCatalog.Get("map.a").Bindings);
        Assert.False(freshCatalog.Get("map.b").IsRebound);
    }
}
