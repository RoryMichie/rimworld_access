using System.Collections.Generic;
using RimWorldAccess.Shell;
using UnityEngine;

namespace RimWorldAccess.Tests.Shell;

public class ActionCatalogTests
{
    private static InputAction Global(string id, params string[] chords) =>
        Make(id, ActionCategory.Global, null, chords);

    private static InputAction Map(string id, params string[] chords) =>
        Make(id, ActionCategory.Map, null, chords);

    private static InputAction World(string id, params string[] chords) =>
        Make(id, ActionCategory.World, null, chords);

    private static InputAction Menus(string id, params string[] chords) =>
        Make(id, ActionCategory.Menus, null, chords);

    private static InputAction Screen(string scope, string id, params string[] chords) =>
        Make(id, ActionCategory.Screen, scope, chords);

    private static InputAction Make(string id, ActionCategory category, string scope, string[] chords)
    {
        var defaults = new List<KeyChord>();
        foreach (var c in chords)
            defaults.Add(KeyChord.Parse(c));
        return new InputAction(id, category, defaults, screenScopeKey: scope);
    }

    [Fact]
    public void RegisterAndGet_Work()
    {
        var catalog = new ActionCatalog();
        var action = Global("speech.silence", "Ctrl+Space");
        catalog.Register(action);

        Assert.Same(action, catalog.Get("speech.silence"));
        Assert.True(catalog.TryGet("speech.silence", out var found));
        Assert.Same(action, found);
        Assert.Equal(1, catalog.Count);
    }

    [Fact]
    public void Register_RejectsDuplicateId()
    {
        var catalog = new ActionCatalog();
        catalog.Register(Global("a.b", "F1"));
        Assert.Throws<System.InvalidOperationException>(() => catalog.Register(Global("a.b", "F2")));
    }

    [Fact]
    public void Get_UnknownIdThrows_TryGetReturnsFalse()
    {
        var catalog = new ActionCatalog();
        Assert.Throws<KeyNotFoundException>(() => catalog.Get("no.such"));
        Assert.False(catalog.TryGet("no.such", out _));
    }

    [Theory]
    [InlineData("nodots")]
    [InlineData("has space.x")]
    [InlineData("")]
    public void Constructor_RejectsBadIds(string id)
    {
        Assert.Throws<System.ArgumentException>(() =>
            new InputAction(id, ActionCategory.Global, new List<KeyChord> { KeyChord.Parse("F1") }));
    }

    [Fact]
    public void Constructor_ScreenCategoryRequiresScopeKey_OthersForbidIt()
    {
        var defaults = new List<KeyChord> { KeyChord.Parse("F1") };
        Assert.Throws<System.ArgumentException>(() =>
            new InputAction("trade.confirm", ActionCategory.Screen, defaults));
        Assert.Throws<System.ArgumentException>(() =>
            new InputAction("map.thing", ActionCategory.Map, defaults, screenScopeKey: "trade"));
    }

    [Fact]
    public void Constructor_RejectsDuplicateDefaultChords()
    {
        Assert.Throws<System.ArgumentException>(() => Global("a.b", "F1", "F1"));
    }

    [Fact]
    public void Bindings_FallBackToDefaultsUntilRebound()
    {
        var catalog = new ActionCatalog();
        var action = Map("map.cursor.north", "UpArrow");
        catalog.Register(action);

        Assert.False(action.IsRebound);
        Assert.Equal(new[] { KeyChord.Parse("UpArrow") }, action.Bindings);

        Assert.True(catalog.SetBinding("map.cursor.north", new[] { KeyChord.Parse("Keypad8") }));
        Assert.True(action.IsRebound);
        Assert.Equal(new[] { KeyChord.Parse("Keypad8") }, action.Bindings);
    }

    [Fact]
    public void SetBinding_EqualToDefaults_NormalizesToNotRebound()
    {
        var catalog = new ActionCatalog();
        var action = Map("map.cursor.north", "UpArrow");
        catalog.Register(action);

        catalog.SetBinding("map.cursor.north", new[] { KeyChord.Parse("UpArrow") });
        Assert.False(action.IsRebound);
    }

    [Fact]
    public void SetBinding_NullResetsToDefaults_UnknownIdReturnsFalse()
    {
        var catalog = new ActionCatalog();
        var action = Map("map.cursor.north", "UpArrow");
        catalog.Register(action);

        catalog.SetBinding("map.cursor.north", new[] { KeyChord.Parse("Keypad8") });
        Assert.True(catalog.SetBinding("map.cursor.north", null));
        Assert.False(action.IsRebound);

        Assert.False(catalog.SetBinding("no.such", new[] { KeyChord.Parse("F1") }));
    }

    [Fact]
    public void ResetToDefaults_ClearsAllOverrides()
    {
        var catalog = new ActionCatalog();
        catalog.Register(Map("map.a", "F1"));
        catalog.Register(Map("map.b", "F2"));
        catalog.SetBinding("map.a", new[] { KeyChord.Parse("F3") });
        catalog.SetBinding("map.b", new[] { KeyChord.Parse("F4") });

        catalog.ResetToDefaults();

        Assert.False(catalog.Get("map.a").IsRebound);
        Assert.False(catalog.Get("map.b").IsRebound);
    }

    [Fact]
    public void CanCoexist_GlobalSeesEverything()
    {
        var global = Global("g.x", "F1");
        Assert.True(ActionCatalog.CanCoexist(global, Map("map.x", "F1")));
        Assert.True(ActionCatalog.CanCoexist(global, World("world.x", "F1")));
        Assert.True(ActionCatalog.CanCoexist(global, Screen("trade", "trade.x", "F1")));
        Assert.True(ActionCatalog.CanCoexist(Menus("menu.x", "F1"), global));
    }

    [Fact]
    public void CanCoexist_AmbientContextsAreMutuallyExclusive()
    {
        Assert.False(ActionCatalog.CanCoexist(Map("map.x", "F1"), World("world.x", "F1")));
        Assert.False(ActionCatalog.CanCoexist(Map("map.x", "F1"), Screen("trade", "trade.x", "F1")));
        Assert.False(ActionCatalog.CanCoexist(World("world.x", "F1"), Menus("menu.x", "F1")));
        Assert.True(ActionCatalog.CanCoexist(Map("map.x", "F1"), Map("map.y", "F2")));
    }

    [Fact]
    public void CanCoexist_MenusShareVisibilityWithScreens()
    {
        var menuUp = Menus("menu.up", "UpArrow");
        Assert.True(ActionCatalog.CanCoexist(menuUp, Screen("trade", "trade.x", "UpArrow")));
        Assert.True(ActionCatalog.CanCoexist(menuUp, Menus("menu.down", "DownArrow")));
    }

    [Fact]
    public void CanCoexist_DifferentScreensNeverCollide()
    {
        Assert.False(ActionCatalog.CanCoexist(
            Screen("trade", "trade.x", "F1"),
            Screen("work", "work.x", "F1")));
        Assert.True(ActionCatalog.CanCoexist(
            Screen("trade", "trade.x", "F1"),
            Screen("trade", "trade.y", "F2")));
    }

    [Fact]
    public void CheckConflict_ReportsOnlyCoexistingActionsOnThatChord()
    {
        var catalog = new ActionCatalog();
        var global = Global("g.info", "Alt+M");
        var map = Map("map.mood", "Alt+M");
        var world = World("world.mood", "Alt+M");
        catalog.Register(global);
        catalog.Register(map);
        catalog.Register(world);

        var report = catalog.CheckConflict(map, KeyChord.Parse("Alt+M"));
        Assert.True(report.HasConflict);
        Assert.Contains(global, report.ConflictingActions);
        Assert.DoesNotContain(world, report.ConflictingActions);

        var clean = catalog.CheckConflict(map, KeyChord.Parse("Alt+N"));
        Assert.False(clean.HasConflict);
    }

    [Fact]
    public void CheckConflict_HonorsRebinds()
    {
        var catalog = new ActionCatalog();
        var a = Map("map.a", "F1");
        var b = Map("map.b", "F2");
        catalog.Register(a);
        catalog.Register(b);

        // F2 belongs to map.b by default...
        Assert.True(catalog.CheckConflict(a, KeyChord.Parse("F2")).HasConflict);

        // ...but once map.b is rebound away, F2 is free.
        catalog.SetBinding("map.b", new[] { KeyChord.Parse("F3") });
        Assert.False(catalog.CheckConflict(a, KeyChord.Parse("F2")).HasConflict);
    }

    [Fact]
    public void FindConflicts_ReportsEachCollidingPairOnce()
    {
        var catalog = new ActionCatalog();
        catalog.Register(Global("g.x", "Alt+M"));
        catalog.Register(Map("map.x", "Alt+M"));
        catalog.Register(World("world.x", "Alt+M")); // collides with global only
        catalog.Register(Map("map.clean", "Alt+N"));

        var conflicts = catalog.FindConflicts();

        Assert.Equal(2, conflicts.Count); // g/map and g/world, but NOT map/world; none for Alt+N
        Assert.All(conflicts, c => Assert.Equal(KeyChord.Parse("Alt+M"), c.Chord));
    }

    [Fact]
    public void FindConflicts_EmptyOnCleanCatalog()
    {
        var catalog = new ActionCatalog();
        catalog.Register(Map("map.x", "F1"));
        catalog.Register(World("world.x", "F1"));
        catalog.Register(Screen("trade", "trade.x", "F1"));
        catalog.Register(Screen("work", "work.x", "F1"));

        Assert.Empty(catalog.FindConflicts());
    }
}
