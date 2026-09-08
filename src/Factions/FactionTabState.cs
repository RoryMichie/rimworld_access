using System.Collections.Generic;
using System.Text;
using RimWorld;
using Verse;

namespace RimWorldAccess
{
    /// <summary>
    /// Data and lifecycle facade for the in-game Factions tab (windowless),
    /// opened via F12 &gt; Factions or any other path that activates
    /// MainTabWindow_Factions (FactionTabPatch hijacks its DoWindowContents).
    ///
    /// Navigation, typeahead, expand/collapse and per-row announcements live on
    /// <see cref="RimWorldAccess.Shell.FactionTabScope"/> (a
    /// <c>TreeRegionScope</c>, i.e. a real ScreenScope). What stays here is the
    /// lifecycle (<see cref="Open"/>/<see cref="Close"/>/<see cref="ResetHard"/>),
    /// the tree DATA — one <c>FactionHelper.BuildFactionTree</c> root the scope
    /// adopts — the opening announcement, and the dev-mode "Show all" toggle.
    /// This mirrors <see cref="FactionLandingState"/> member for member: the two
    /// faction surfaces present the same tree and now share one shape.
    ///
    /// This state no longer uses the <c>FactionTreeNavigation</c> component (the
    /// <see cref="TreeNavigationHelper"/> wrapper it drove through
    /// <c>HandleInput(Event)</c>): with the landing dialog already off it too, that
    /// component now has no callers left at all. It is left in place rather than
    /// deleted; <c>src/Factions/FactionTreeNavigation.cs</c> is a removal candidate.
    /// </summary>
    public static class FactionTabState
    {
        public static bool IsActive { get; private set; }

        private static InspectionTreeItem treeRoot;

        /// <summary>
        /// The tree the scope presents. Read by the scope's own OnPush:
        /// <see cref="Open"/> runs from FactionTabPatch's DoWindowContents prefix,
        /// a full dispatcher pass before the windowless mirror pushes the scope,
        /// so the root is always written here first.
        /// </summary>
        internal static InspectionTreeItem CurrentTreeRoot
        {
            get { return treeRoot; }
        }

        /// <summary>
        /// Opens the faction tab state and builds the faction tree.
        /// Called from FactionTabPatch when MainTabWindow_Factions opens.
        /// </summary>
        public static void Open()
        {
            if (IsActive)
                return;

            IsActive = true;
            BuildTree(FactionHelper.BuildFactionList(FactionHelper.DevShowAll));
        }

        /// <summary>
        /// Builds the faction tree, hands it to the scope if one is already attached, and
        /// speaks the opening announcement — the retired
        /// <c>FactionTreeNavigation.Initialize</c>'s own three steps, in its order.
        /// <paramref name="preserveCursor"/> routes the hand-off through the scope's
        /// cursor-and-expansion-preserving reload (<see cref="ToggleShowAll"/>'s rebuild) instead
        /// of the plain reset-to-row-0 <c>LoadTree</c> (the initial <see cref="Open"/>, which has
        /// no prior cursor to keep).
        /// </summary>
        private static void BuildTree(List<Faction> factions, bool preserveCursor = false)
        {
            treeRoot = FactionHelper.BuildFactionTree(factions);
            if (preserveCursor)
            {
                Shell.FactionTabScope.Live?.ReloadTreePreservingState(treeRoot);
            }
            else
            {
                Shell.FactionTabScope.Live?.LoadTree(treeRoot);
            }
            AnnounceOpening(factions.Count);
        }

        /// <summary>
        /// The dev-mode context menu (RightBracket) mirroring MainTabWindow_Factions'
        /// embedded "DEV: Show all" checkbox (FactionUIUtility.cs:49), which reveals
        /// hidden and player factions. Gated on <see cref="Prefs.DevMode"/> exactly
        /// like vanilla. Dev mode = full parity.
        /// </summary>
        internal static void OpenDevContextMenu()
        {
            if (!IsActive || !Prefs.DevMode)
                return;

            var options = new List<FloatMenuOption>
            {
                new FloatMenuOption("DEV: Show all", ToggleShowAll),
            };
            WindowlessFloatMenuState.Open(options, colonistOrders: false);
        }

        private static void ToggleShowAll()
        {
            bool next = !FactionHelper.DevShowAll;
            FactionHelper.SetDevShowAll(next);

            // Rebuild so hidden (and player) factions appear or disappear.
            BuildTree(FactionHelper.BuildFactionList(next), preserveCursor: true);

            string state = (next
                ? "RimWorldAccess.Shell.State.Checked"
                : "RimWorldAccess.Shell.State.Unchecked").Translate().ToString();
            TolkHelper.SpeakData("DEV: Show all. " + state + "."); // l10n-exempt: verbatim vanilla dev label (FactionUIUtility.cs:49), itself unlocalized
            Shell.FactionTabScope.Live?.AnnounceCurrentRow();
        }

        /// <summary>
        /// Closes the faction tab state and resets all fields.
        /// </summary>
        public static void Close()
        {
            IsActive = false;
            treeRoot = null;
            Shell.FactionTabScope.Live?.ClearTree();
            TolkHelper.Speak("RimWorldAccess.Factions.Tab.Closed".Loc());
            Shell.MainTabWindowLink.CloseTab(Shell.MainTabWindowLink.Factions);
        }

        /// <summary>
        /// Silent hard reset for session-boundary hygiene — unlike
        /// <see cref="Close"/>, does not announce anything. Used
        /// by GameStartPatch and MainMenuAccessibilityPatch's Entry-gated
        /// reset block; this state has no deterministic close otherwise
        /// (FactionTabPatch opens + TryRemoves the vanilla window but never
        /// closes the state; switching main tabs leaks the flag).
        /// </summary>
        internal static void ResetHard()
        {
            IsActive = false;
            treeRoot = null;
            Shell.FactionTabScope.Live?.ClearTree();
        }

        #region Announcements

        /// <summary>
        /// The opening announcement, reproducing the retired
        /// <c>FactionTreeNavigation.AnnounceOpening</c> word for word: the faction count,
        /// then the first row's label with its child-count expansion suffix and its sibling
        /// position. The first visible row is the root's first child — the root is expanded
        /// and skipped in the visible list, and every faction node starts collapsed — so it
        /// needs no flattened tree to find.
        ///
        /// This is the one utterance on this screen the composer does not build, and it is
        /// deliberate: it is the SCREEN's opening sentence (a count plus a preview of the
        /// landing row), not a focusable row's announcement, and it is byte-identical to the
        /// already-shipped <c>FactionLandingState.AnnounceOpening</c> for the very same
        /// tree. Composing one of the pair and not the other would split the two faction
        /// surfaces apart again. Every actual ROW announcement on this screen goes through
        /// AnnouncementComposer via the scope.
        /// </summary>
        private static void AnnounceOpening(int factionCount)
        {
            if (factionCount > 0 && treeRoot != null && treeRoot.Children.Count > 0)
            {
                var sb = new StringBuilder(
                    "RimWorldAccess.Factions.Tab.OpeningWithCount".Translate(factionCount).ToString());
                InspectionTreeItem firstItem = treeRoot.Children[0];
                FactionHelper.AppendSentence(sb, firstItem.Label);

                sb.Append(TreeNavigationHelper.FormatExpansionSuffix(firstItem, includeChildCount: true));

                string position = MenuHelper.FormatPosition(0, treeRoot.Children.Count);
                if (!string.IsNullOrEmpty(position))
                    FactionHelper.AppendSentence(sb, position);

                TolkHelper.SpeakData(sb.ToString());
            }
            else
            {
                TolkHelper.Speak("RimWorldAccess.Factions.Tab.OpeningNoFactions".Loc());
            }
        }

        #endregion
    }
}
