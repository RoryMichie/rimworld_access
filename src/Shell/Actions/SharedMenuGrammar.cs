using System.Collections.Generic;
using UnityEngine;

namespace RimWorldAccess.Shell
{
    /// <summary>
    /// The navigation actions every menu-like scope claims instead of registering its own copies of
    /// the arrow keys. Registered once in the Menus category, so one rebind applies to every menu
    /// and the conflict checker can reason about the grammar as a single scope.
    /// Typed typeahead characters are deliberately not actions: they reach scopes through
    /// <see cref="ICharSink"/> ahead of chord dispatch. Backspace needs a chord because it arrives
    /// as a key event rather than a printable character, and scopes claim it behind a
    /// "search is active" guard so an idle Backspace still falls through.
    ///
    /// PURE: links into the test project.
    /// </summary>
    public static class SharedMenuGrammar
    {
        public const string Next = "menus.next";
        public const string Previous = "menus.previous";

        /// <summary>
        /// The horizontal flavor of Next/Previous, for scopes whose items also read as a
        /// left-to-right strip. Separate actions rather than extra chords on Next/Previous, so a
        /// scope giving Left/Right a different meaning (schedule hours, sliders) simply does not
        /// claim them.
        /// </summary>
        public const string NextHorizontal = "menus.nextHorizontal";
        public const string PreviousHorizontal = "menus.previousHorizontal";

        public const string First = "menus.first";
        public const string Last = "menus.last";
        public const string Activate = "menus.activate";
        public const string Cancel = "menus.cancel";
        public const string SearchBackspace = "menus.searchBackspace";

        /// <summary>
        /// Space, the second activation key: it does everything Enter's <see cref="Activate"/> does,
        /// so a player reaching for Space never has to know which control kind is focused. A
        /// separate action rather than a second chord because Space activates the focused row and
        /// nothing else, deliberately bypassing the Enter-on-an-inert-row proceed routing, so it can
        /// never fire a screen's Next button by surprise. A scope binding Space to a meaning of its
        /// own claims its own action and the shared alias stands down.
        /// </summary>
        public const string ActivateAlias = "menus.activateAlias";

        /// <summary>
        /// Shift+Enter while a typeahead search is live: land on the match without running it, so
        /// the search ends and the row is re-read. Shares its chord with
        /// <see cref="ActivateDefault"/> on purpose — the two are disjoint by search state, and
        /// scopes claim this one first.
        /// </summary>
        public const string SearchSettle = "menus.searchSettle";

        /// <summary>
        /// Shift+Enter, the one-chord proceed: activates the screen's
        /// <c>ScreenScope.DefaultAcceptActionId</c> action from anywhere, bypassing whatever the
        /// cursor is on, and announces the pressed button's name, since a player reaching for this
        /// chord chose not to look the name up first. It complements rather than replaces the
        /// Enter-on-an-inert-row two-step confirm and each screen's per-button Alt+ shortcut. A
        /// screen with no proceed button never claims the chord, so it falls through. While a
        /// typeahead search is live the same chord means <see cref="SearchSettle"/>, which scopes
        /// claim first.
        /// </summary>
        public const string ActivateDefault = "menus.activateDefault";

        /// <summary>
        /// Tab and Shift+Tab move between a screen's regions. One shared pair, so a rebind applies
        /// to every regioned screen; scopes giving Tab a different meaning simply do not claim these.
        /// </summary>
        public const string NextRegion = "menus.nextRegion";
        public const string PreviousRegion = "menus.previousRegion";

        /// <summary>The unified Alt+I drill-in: open the info card for the focused item. Claimed by scopes whose items can carry a Def.</summary>
        public const string Info = "menus.info";

        /// <summary>
        /// Alt+Shift+J: land the cursor on whatever the mouse pointer is over and read it as an arrow
        /// key would — the screen twin of map.cursor.pullFromPointer, which keeps the same chord.
        /// The two never dispatch at once (a live modal screen scope masks the map's ambient claims)
        /// and ActionCatalog.CanCoexist treats a Menus and a Map action as unable to collide, so the
        /// shared chord is correct rather than tolerated. A scope with no way to find the pointed-at
        /// row does not claim this.
        /// </summary>
        public const string RouteToPointer = "menus.routeToPointer";

        /// <summary>
        /// Alt+Shift+M: toggle mouse tracking. A pure settings flip plus announcement, so it is valid
        /// on every screen and ScreenScope claims it unconditionally. Shares its chord with the
        /// map's own twin under the same coexistence rule as <see cref="RouteToPointer"/>.
        /// </summary>
        public const string ToggleMouseTracking = "menus.toggleMouseTracking";

        /// <summary>
        /// Sort a table region by the current column through vanilla's 3-state cycle, from anywhere
        /// in the table — the chord alias for Enter on the header row. One shared action; screens
        /// whose Alt+S means something else suppress it via ScreenScope.EnableSortChord.
        /// </summary>
        public const string SortColumn = "menus.sortColumn";

        /// <summary>
        /// Reorder the focused item: the mod-wide keyboard equivalent of the player's drag-and-drop
        /// gesture, one shared pair per axis. Claimed by ScreenScope itself through
        /// <see cref="ScreenScope.CanReorderContentItem"/>/<see cref="ScreenScope.ReorderContentItem"/>,
        /// so a scope that cannot reorder never claims them and the chords fall through. Vertical is
        /// claimed by the base seam; horizontal is registered here for a 2D screen to claim itself.
        /// No per-screen scope may register a private Ctrl+Arrow reorder action.
        /// </summary>
        public const string ReorderUp = "menus.reorderUp";
        public const string ReorderDown = "menus.reorderDown";
        public const string ReorderLeft = "menus.reorderLeft";
        public const string ReorderRight = "menus.reorderRight";

        /// <summary>
        /// True for the grammar actions whose ONLY effect is landing the cursor on a different
        /// element — the moves a player expects a navigation tick for. Anything that activates,
        /// cancels, edits a search, sorts, reorders or opens a card is out, because it speaks a
        /// result of its own. A scope with navigation ids outside this shared grammar does not tick.
        /// </summary>
        public static bool IsCursorMove(string actionId)
        {
            return actionId == Next
                || actionId == Previous
                || actionId == NextHorizontal
                || actionId == PreviousHorizontal
                || actionId == First
                || actionId == Last
                || actionId == NextRegion
                || actionId == PreviousRegion
                || actionId == SearchSettle;
        }

        public static void Register(ActionCatalog catalog)
        {
            catalog.Register(new InputAction(Next,
                ActionCategory.Menus,
                new List<KeyChord> { KeyChord.Of(KeyCode.DownArrow) }));
            catalog.Register(new InputAction(Previous,
                ActionCategory.Menus,
                new List<KeyChord> { KeyChord.Of(KeyCode.UpArrow) }));
            catalog.Register(new InputAction(NextHorizontal,
                ActionCategory.Menus,
                new List<KeyChord> { KeyChord.Of(KeyCode.RightArrow) }));
            catalog.Register(new InputAction(PreviousHorizontal,
                ActionCategory.Menus,
                new List<KeyChord> { KeyChord.Of(KeyCode.LeftArrow) }));
            catalog.Register(new InputAction(First,
                ActionCategory.Menus,
                new List<KeyChord> { KeyChord.Of(KeyCode.Home) }));
            catalog.Register(new InputAction(Last,
                ActionCategory.Menus,
                new List<KeyChord> { KeyChord.Of(KeyCode.End) }));
            catalog.Register(new InputAction(Activate,
                ActionCategory.Menus,
                new List<KeyChord> { KeyChord.Of(KeyCode.Return), KeyChord.Of(KeyCode.KeypadEnter) }));
            catalog.Register(new InputAction(ActivateAlias,
                ActionCategory.Menus,
                new List<KeyChord> { KeyChord.Of(KeyCode.Space) }));
            catalog.Register(new InputAction(SearchSettle,
                ActionCategory.Menus,
                new List<KeyChord> { KeyChord.Of(KeyCode.Return, shift: true), KeyChord.Of(KeyCode.KeypadEnter, shift: true) }));
            catalog.Register(new InputAction(ActivateDefault,
                ActionCategory.Menus,
                new List<KeyChord> { KeyChord.Of(KeyCode.Return, shift: true), KeyChord.Of(KeyCode.KeypadEnter, shift: true) }));
            catalog.Register(new InputAction(Cancel,
                ActionCategory.Menus,
                new List<KeyChord> { KeyChord.Of(KeyCode.Escape) }));
            catalog.Register(new InputAction(SearchBackspace,
                ActionCategory.Menus,
                new List<KeyChord> { KeyChord.Of(KeyCode.Backspace) }));
            catalog.Register(new InputAction(NextRegion,
                ActionCategory.Menus,
                new List<KeyChord> { KeyChord.Of(KeyCode.Tab) }));
            catalog.Register(new InputAction(PreviousRegion,
                ActionCategory.Menus,
                new List<KeyChord> { KeyChord.Of(KeyCode.Tab, shift: true) }));
            catalog.Register(new InputAction(Info,
                ActionCategory.Menus,
                new List<KeyChord> { KeyChord.Of(KeyCode.I, alt: true) }));
            catalog.Register(new InputAction(SortColumn,
                ActionCategory.Menus,
                new List<KeyChord> { KeyChord.Of(KeyCode.S, alt: true) }));
            catalog.Register(new InputAction(ReorderUp,
                ActionCategory.Menus,
                new List<KeyChord> { KeyChord.Of(KeyCode.UpArrow, ctrl: true) }));
            catalog.Register(new InputAction(ReorderDown,
                ActionCategory.Menus,
                new List<KeyChord> { KeyChord.Of(KeyCode.DownArrow, ctrl: true) }));
            catalog.Register(new InputAction(ReorderLeft,
                ActionCategory.Menus,
                new List<KeyChord> { KeyChord.Of(KeyCode.LeftArrow, ctrl: true) }));
            catalog.Register(new InputAction(ReorderRight,
                ActionCategory.Menus,
                new List<KeyChord> { KeyChord.Of(KeyCode.RightArrow, ctrl: true) }));
            catalog.Register(new InputAction(RouteToPointer,
                ActionCategory.Menus,
                new List<KeyChord> { KeyChord.Of(KeyCode.J, shift: true, alt: true) }));
            catalog.Register(new InputAction(ToggleMouseTracking,
                ActionCategory.Menus,
                new List<KeyChord> { KeyChord.Of(KeyCode.M, shift: true, alt: true) }));
        }
    }
}
