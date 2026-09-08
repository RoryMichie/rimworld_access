using System.Collections.Generic;
using Verse;

namespace RimWorldAccess
{
    /// <summary>
    /// Data facade for the main menu's captured vanilla option lists.
    /// Cursor, typeahead, and per-move
    /// announcement arithmetic now live entirely in
    /// <see cref="RimWorldAccess.Shell.MainMenuScope"/>'s ScreenModel — this
    /// class keeps only what that scope reads to rebuild its regions
    /// (<see cref="Initialize"/>/<see cref="ColumnOptions"/>), the
    /// activation wrapper (<see cref="ActivateSelected"/>/
    /// <see cref="IsExecutingOption"/>), the frame-recency liveness flag
    /// (<see cref="IsActive"/>/<see cref="MarkRendered"/>), and a handful of
    /// forwarders (<see cref="CurrentColumn"/>/<see cref="SelectedIndex"/>/
    /// <see cref="CurrentColumnOptions"/>/<see cref="GetCurrentSelection"/>/
    /// <see cref="GetCurrentColumnLabels"/>) kept ALIVE deliberately: grep
    /// proved external callers still depend on them —
    /// MainMenuAccessibilityPatch's highlight draw, and the documented
    /// "GoBack contract" in OptionsScope.OnPop/FileListScope.OnPop (both
    /// re-announce the current main-menu selection directly on close,
    /// because MainMenuScope's OnFocus does not itself speak).
    /// </summary>
    public static class MenuNavigationState
    {
        private static List<ListableOption> column0Options = new List<ListableOption>();
        private static List<ListableOption> column1Options = new List<ListableOption>();

        /// <summary>
        /// True while ActivateSelected is running the selected option's own vanilla
        /// delegate. DialogInterceptionPatch checks this so a menu option that opens a
        /// game FloatMenu (e.g. BuySoundtrack, decompiled RimWorld/MainMenuDrawer.cs:283)
        /// rides the windowless path instead of an unreachable mouse-only FloatMenu.
        /// </summary>
        public static bool IsExecutingOption { get; private set; }

        /// <summary>
        /// True when the main menu is currently being rendered. Set by
        /// <see cref="MainMenuAccessibilityPatch"/>'s Postfix each frame.
        /// <see cref="RimWorldAccess.Shell.MainMenuScope"/> overrides its own
        /// IsLive to this flag, so every claim (including ScreenScope's own
        /// ungated Up/Down/Home/End/Enter) goes dark the instant vanilla
        /// stops drawing the menu — the single gate that replaces the
        /// retired handler's per-claim MenuLive checks.
        /// </summary>
        private static int lastRenderedFrame = -1;
        public static bool IsActive => UnityEngine.Time.frameCount - lastRenderedFrame <= 1;
        public static void MarkRendered() { lastRenderedFrame = UnityEngine.Time.frameCount; }

        /// <summary>
        /// Rebuilds the captured lists (called unconditionally every Postfix
        /// by MainMenuAccessibilityPatch, per its own header comment) — the
        /// scope re-syncs its ScreenModel region counts against these the
        /// next time a claim calls RefreshContent.
        /// </summary>
        public static void Initialize(List<ListableOption> col0, List<ListableOption> col1)
        {
            column0Options = col0;
            column1Options = col1;
        }

        /// <summary>The captured option list for a region (0 = Menu, 1 = Links).</summary>
        public static List<ListableOption> ColumnOptions(int column)
        {
            return column == 0 ? column0Options : column1Options;
        }

        /// <summary>
        /// Forwarders reading the scope's live cursor back through its
        /// singleton instance — see the class remarks for why these three
        /// (plus GetCurrentSelection/GetCurrentColumnLabels below) survive
        /// the slim-down.
        /// </summary>
        public static int CurrentColumn => Shell.MainMenuScope.Instance?.CurrentRegionIndex ?? 0;
        public static int SelectedIndex => Shell.MainMenuScope.Instance?.CurrentRowIndex ?? 0;
        public static List<ListableOption> CurrentColumnOptions => ColumnOptions(CurrentColumn);

        /// <summary>Labels for the CURRENT column only — kept for OptionsScope's GoBack-contract position readout.</summary>
        public static List<string> GetCurrentColumnLabels()
        {
            var labels = new List<string>();
            foreach (ListableOption option in CurrentColumnOptions)
            {
                labels.Add(option.label);
            }
            return labels;
        }

        /// <summary>The option at the scope's current cursor position, or null when out of range.</summary>
        public static ListableOption GetCurrentSelection()
        {
            List<ListableOption> options = CurrentColumnOptions;
            int index = SelectedIndex;
            return index >= 0 && index < options.Count ? options[index] : null;
        }

        /// <summary>Runs the currently-selected option's own vanilla delegate — vehicle A, unchanged.</summary>
        public static void ActivateSelected()
        {
            ListableOption selected = GetCurrentSelection();
            if (selected?.action != null)
            {
                IsExecutingOption = true;
                try
                {
                    selected.action();
                }
                finally
                {
                    IsExecutingOption = false;
                }
            }
        }
    }
}
