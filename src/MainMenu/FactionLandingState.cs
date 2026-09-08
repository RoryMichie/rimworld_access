using System.Collections.Generic;
using System.Text;
using RimWorld;
using Verse;

namespace RimWorldAccess
{
    /// <summary>
    /// Data and lifecycle facade for Dialog_FactionDuringLanding, the faction-relations
    /// dialog opened with F during starting-site selection.
    ///
    /// Navigation, typeahead, expand/collapse and
    /// per-row announcements moved to <see cref="RimWorldAccess.Shell.FactionLandingScope"/>
    /// (a <c>TreeRegionScope</c>). What stays here is the dialog lifecycle
    /// (<see cref="Open"/>/<see cref="Close"/>, driven by FactionLandingPatch's
    /// PostOpen/PostClose postfixes), the tree DATA — one
    /// <c>FactionHelper.BuildFactionTree</c> root the scope adopts — the opening
    /// announcement, and the dev-mode "Show all" toggle.
    ///
    /// This state no longer uses the shared <c>FactionTreeNavigation</c> component
    /// (blueprint ruling D15): that component is still the engine for FactionTabState,
    /// the in-game Factions tab, and was deliberately left untouched.
    /// </summary>
    public static class FactionLandingState
    {
        public static bool IsActive { get; private set; }

        private static Dialog_FactionDuringLanding currentDialog;
        private static InspectionTreeItem treeRoot;

        /// <summary>
        /// The tree the scope presents. Read by the scope's own OnPush:
        /// <see cref="Open"/> runs from the Window.PostOpen patch, i.e. inside
        /// WindowStack.Add and therefore BEFORE the Add postfix attaches the scope, so the
        /// root is always written here first.
        /// </summary>
        internal static InspectionTreeItem CurrentTreeRoot
        {
            get { return treeRoot; }
        }

        /// <summary>
        /// Opens the faction landing state for a dialog.
        /// Called from FactionLandingPatch when Dialog_FactionDuringLanding opens.
        /// </summary>
        public static void Open(Dialog_FactionDuringLanding dialog)
        {
            if (dialog == null)
                return;

            currentDialog = dialog;
            IsActive = true;

            // Prevent RimWorld from closing on Enter/Escape — we handle both
            dialog.closeOnAccept = false;
            dialog.closeOnCancel = false;
            // Prevent the dialog from stealing Unity IMGUI keyboard focus.
            // Page_SelectStartingSite has InitialSize=Vector2.zero, so when this dialog
            // closes, TryRemove tries to GUI.FocusWindow on the zero-sized page, which
            // corrupts Unity's focus chain. Since we handle all input through the shell
            // dispatcher (outside GUI.Window), this dialog doesn't need focus.
            dialog.focusWhenOpened = false;

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
                Shell.FactionLandingScope.Live?.ReloadTreePreservingState(treeRoot);
            }
            else
            {
                Shell.FactionLandingScope.Live?.LoadTree(treeRoot);
            }
            AnnounceOpening(factions.Count);
        }

        /// <summary>
        /// The dev-mode context menu (RightBracket) mirroring the dialog's own
        /// embedded "DEV: Show all" checkbox (FactionUIUtility.cs:49, reached
        /// through Dialog_FactionDuringLanding.DoWindowContents), which reveals
        /// hidden and player factions. Gated on <see cref="Prefs.DevMode"/>
        /// exactly like vanilla, and identical to the in-game Factions tab's
        /// toggle (src/Factions/FactionTabState.cs:53). Dev mode = full parity.
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
            Shell.FactionLandingScope.Live?.AnnounceCurrentRow();
        }

        /// <summary>
        /// Closes the faction landing state and resets all fields.
        /// </summary>
        public static void Close()
        {
            IsActive = false;
            currentDialog = null;
            treeRoot = null;
            Shell.FactionLandingScope.Live?.ClearTree();
        }

        /// <summary>
        /// Closes the dialog via WindowStack and announces closure — Escape's second stage,
        /// reached from the scope's own Cancel claim once no
        /// typeahead search is active. (The escapeHandledOnFrame stamp that used to live
        /// here died with its only consumer, FactionLandingPatch's Page.OnCancelKeyPressed
        /// blocker — see that patch's tombstone.)
        /// </summary>
        internal static void CloseDialog()
        {
            if (currentDialog != null)
            {
                Find.WindowStack.TryRemove(currentDialog);
            }
            Close();
            TolkHelper.Speak("RimWorldAccess.Factions.LandingClosed".Loc());
        }

        #region Announcements

        /// <summary>
        /// The opening announcement, reproducing the retired
        /// <c>FactionTreeNavigation.AnnounceOpening</c> word for word: the faction count,
        /// then the first row's label with its child-count expansion suffix and its sibling
        /// position. The first visible row is the root's first child — the root is expanded
        /// and skipped in the visible list, and every faction node starts collapsed — so it
        /// needs no flattened tree to find.
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
