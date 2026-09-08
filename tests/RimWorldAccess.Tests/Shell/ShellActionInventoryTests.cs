using RimWorldAccess.Shell;

namespace RimWorldAccess.Tests.Shell;

public class ShellActionInventoryTests
{
    private static ActionCatalog Build()
    {
        var catalog = new ActionCatalog();
        ShellActionInventory.RegisterAll(catalog);
        return catalog;
    }

    [Fact]
    public void RegisterAll_Succeeds_AndIsSubstantial()
    {
        var catalog = Build();
        // ~130 ladder rungs + 33 side doors produced 500+ actions; a hard floor
        // guards against a part silently registering nothing.
        Assert.True(catalog.Count > 400, "Only " + catalog.Count + " actions registered");
    }

    [Fact]
    public void AllIds_AreDottedAndScreenIdsMatchTheirScope()
    {
        var catalog = Build();
        foreach (var action in catalog.All)
        {
            Assert.Contains(".", action.Id);
            if (action.Category == ActionCategory.Screen)
            {
                Assert.Equal(action.ScopeKey, action.Id.Split('.')[0]);
            }
        }
    }

    /// <summary>
    /// Same-chord pairs within one screen scope where the key's behavior is
    /// SUB-MODE dependent today (grid vs areas section, list vs section
    /// cursor, tab-specific toggles). Both actions are real and correctly
    /// transcribed; the scope-level conflict checker just cannot see
    /// sub-modes. These are expressed as conditional claims
    /// (Claim(..., when: ...)) on one scope. Reviewed 2026-07-03.
    /// </summary>
    private static readonly string[] KnownModeDependentPairs =
    {
        "Ctrl+Shift+End: schedule.areas.paintToAllTowardLast vs schedule.grid.paintToLastPawn",
        "Ctrl+Shift+Home: schedule.areas.paintToAllTowardFirst vs schedule.grid.paintToFirstPawn",
        // Character Editor scope: Space cycles passion on skill rows and trains
        // one step on animal-training rows — disjoint when: gates on one scope
        // (a pawn's tree shows skill rows or training rows, and the guards test
        // the row kind under the cursor). Reviewed 2026-07-28.
        "Space: charEditor.cyclePassion vs charEditor.trainStep",
        // partEdit's three sub-modes (dropdown/quantity/text) share physical keys behind disjoint when:
        // gates on one scope.
        "Delete: partEdit.textCursor vs partEdit.blockNav",
        "DownArrow: partEdit.quantityDecrease vs partEdit.textLineDown",
        "End: partEdit.quantityMax vs partEdit.textCursor",
        "Home: partEdit.quantityMin vs partEdit.textCursor",
        "LeftArrow: partEdit.textCursor vs partEdit.blockNav",
        "RightArrow: partEdit.textCursor vs partEdit.blockNav",
        // Generic reader: the focused row is either a slider or a stepper, never both, and each pair is
        // claimed behind that disjoint when: gate on the one scope. The stepper ids exist separately
        // only because they additionally bind the modifier chords vanilla's
        // GenUI.CurrentAdjustmentMultiplier scales by.
        "LeftArrow: genericWindow.setting.decrease vs genericWindow.stepper.decrease",
        "RightArrow: genericWindow.setting.increase vs genericWindow.stepper.increase",
        "Shift+DownArrow: partEdit.quantityDecrease vs partEdit.textLineDown",
        "Shift+DownArrow: schedule.areas.applyBelow vs schedule.grid.paintDown",
        "Shift+End: schedule.areas.paintToLastPawn vs schedule.grid.paintToLastHour",
        "Shift+Home: schedule.areas.paintToFirstPawn vs schedule.grid.paintToFirstHour",
        "Shift+UpArrow: partEdit.quantityIncrease vs partEdit.textLineUp",
        "Shift+UpArrow: schedule.areas.applyAbove vs schedule.grid.paintUp",
        // Deliberate surface split: focusByCursor is claimed only on MapScope; the remapped
        // learning-helper opener (the non-US '?' key arriving as a remapped bare Slash) only on
        // WorldScope and StartingSiteScreenScope, additionally gated when: on
        // KeyboardHelper.WasCharacterRemapped — mutually exclusive by scope, matching the retired
        // ladder's 7.14-before-7.15 positional outcome.
        "Slash: map.colonistBar.focusByCursor vs map.menu.learningHelperRemapped",
        // Dev-mode debug tool targeting (DevToolTargeting.Game.cs): apply/cancel
        // are ambient MapScope/WorldScope claims gated when: DebugTools.curTool
        // != null, sharing Enter with the inspection opener and Escape with the
        // pause menu behind that disjoint gate. Registered ahead of both so they
        // win only while a tool is armed; otherwise the openers keep the keys.
        "Escape: map.pause.open vs map.devtool.cancel",
        "KeypadEnter: map.inspect.open vs map.devtool.apply",
        "Return: map.inspect.open vs map.devtool.apply",
        // Styling station: RightBracket is "reset all" normally; in dev mode the same key opens the dev
        // context menu instead (styling.contextMenu is claimed first, guarded when: Prefs.DevMode, and
        // its menu leads with Reset so nothing is lost). Disjoint by dev mode.
        "RightBracket: styling.resetAll vs styling.contextMenu",
        // ideoBuilder.reannounceListItem/reannounceSection is RETIRED (SPACE-RETIRE): Space rides
        // the shared menus.activateAlias claim now, so this pair no longer exists.
        // Shift+Enter is the one-chord proceed normally and "settle on the search
        // match" while a typeahead search is live (menus.searchSettle is claimed
        // first, guarded on the search). Disjoint by search state.
        "Shift+KeypadEnter: menus.searchSettle vs menus.activateDefault",
        "Shift+Return: menus.searchSettle vs menus.activateDefault",
        "Space: lordJobDialog.toggleExtra vs lordJobDialog.togglePawnAssignment",
        "UpArrow: partEdit.quantityIncrease vs partEdit.textLineUp",
    };

    [Fact]
    public void DefaultBindings_HaveNoUnexpectedCollisions()
    {
        var catalog = Build();
        // Shared-grammar chords shadowed by screen actions (slider arrows,
        // quantity Home/End, per-screen Escape) are the designed override
        // mechanism, resolved by claim order — not defaults mistakes.
        var conflicts = catalog.FindConflicts()
            .Where(c => !ActionCatalog.IsMenusScreenShadowing(c.First, c.Second))
            .Select(c => c.ToString())
            .Where(c => !KnownModeDependentPairs.Contains(c))
            .OrderBy(s => s)
            .ToList();
        Assert.True(conflicts.Count == 0,
            "Unexpected default-binding collisions (either dedupe the inventory or, for a real sub-mode pair, extend KnownModeDependentPairs):\n"
            + string.Join("\n", conflicts));
    }

    [Fact]
    public void KnownModeDependentPairs_StillExist()
    {
        var catalog = Build();
        var conflicts = catalog.FindConflicts().Select(c => c.ToString()).ToHashSet();
        foreach (var pair in KnownModeDependentPairs)
        {
            Assert.Contains(pair, conflicts);
        }
    }
}
