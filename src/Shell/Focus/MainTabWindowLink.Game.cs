using System;
using System.Collections.Generic;
using RimWorld;
using Verse;

namespace RimWorldAccess.Shell
{
    /// <summary>
    /// Looks up the topmost live scope of a given type by walking the focus
    /// stack bottom-up from the end backwards, rather than casting
    /// <see cref="FocusStack.Top"/> directly: a drill-in overlay pushed above
    /// a screen (a column dropdown float menu) would otherwise make that
    /// screen's own focus ring vanish even though the screen underneath is
    /// still the correct source for it.
    /// </summary>
    internal static class FocusStackLookup
    {
        internal static T TopmostOfType<T>() where T : class
        {
            IReadOnlyList<FocusScope> scopes = FocusStack.ScopesBottomUp;
            for (int i = scopes.Count - 1; i >= 0; i--)
            {
                T match = scopes[i] as T;
                if (match != null)
                {
                    return match;
                }
            }
            return null;
        }
    }

    /// <summary>
    /// The lifecycle bridge between a windowless screen state and its real vanilla main-tab window
    /// (un-suppressing the eight hijacked <see cref="MainTabWindow"/>s). The vanilla tab window now
    /// stays in the WindowStack while the windowless scope drives it, which means: (1) any opener that
    /// starts a state without going through the window must open the window too, (2) any state close
    /// must close the window, and (3) a per-pass reconcile heals both directions when something outside
    /// the mod moves one of them (a mouse click on another tab button, vanilla's own "click outside any
    /// window closes the open tab" rule).
    /// </summary>
    internal static class MainTabWindowLink
    {
        // One const per tab, MainButtonDef defNames (verified against
        // Core/Defs/Misc/MainButtonDefs/MainButtons.xml and, for Mechs,
        // Biotech/Defs/MainButtonDefs/MainButtons.xml).
        internal const string Wildlife = "Wildlife";
        internal const string Animals = "Animals";
        internal const string Mechs = "Mechs";
        internal const string Assign = "Assign";
        internal const string Work = "Work";
        internal const string Research = "Research";
        internal const string Quests = "Quests";
        internal const string Factions = "Factions";
        internal const string Architect = "Architect";

        private static readonly Dictionary<string, MainButtonDef> resolvedDefs = new Dictionary<string, MainButtonDef>();

        /// <summary>
        /// One entry per linked tab. Adding a tab is a one-line addition to
        /// <see cref="links"/> below; nothing else in this file changes.
        /// </summary>
        private sealed class TabLink
        {
            public readonly string DefName;
            public readonly Func<bool> StateActive;
            public readonly Action CloseStateSilently;

            /// <summary>
            /// Set once the state has been observed active while its window
            /// is open. A window is added to the WindowStack a pass BEFORE
            /// its first DoWindowContents opens the corresponding state, so
            /// without this flag the reconcile would see "window open, state
            /// not active yet" on that first pass and close the
            /// freshly-opened window before it ever drew anything.
            /// </summary>
            public bool armed;

            public TabLink(string defName, Func<bool> stateActive, Action closeStateSilently)
            {
                DefName = defName;
                StateActive = stateActive;
                CloseStateSilently = closeStateSilently;
            }
        }

        // Add one line per tab here; nothing else in this file changes.
        private static readonly TabLink[] links =
        {
            new TabLink(Wildlife, () => WildlifeMenuState.IsActive, WildlifeMenuState.CloseSilent),
            new TabLink(Animals, () => AnimalsMenuState.IsActive, () => AnimalsMenuState.Close(silent: true)),
            new TabLink(Mechs, () => MechsMenuState.IsActive, () => MechsMenuState.Close(silent: true)),
            new TabLink(Assign, () => AssignMenuState.IsActive, AssignMenuState.CloseSilent),
            new TabLink(Work, () => WorkMenuState.IsActive || WorkTableState.IsActive, CloseWhicheverWorkViewIsActive),
            new TabLink(Research, () => WindowlessResearchMenuState.IsActive || WindowlessResearchDetailState.IsActive, CloseWhicheverResearchViewIsActive),
            new TabLink(Quests, () => QuestMenuState.IsActive, () => QuestMenuState.Close(announce: false)),
            new TabLink(Factions, () => FactionTabState.IsActive, FactionTabState.ResetHard),
            new TabLink(Architect, () => ArchitectTreeState.IsActive, CloseArchitectTreeSilently),
        };

        /// <summary>
        /// The architect tree's silent close: the pair
        /// <c>ArchitectTreeScope.PerformClose</c> runs, minus its announcement.
        /// Closing the tree state alone would leave <c>ArchitectState</c> in
        /// category-selection mode, and the next Tab would then take
        /// <c>MapScope.OnToggleArchitect</c>'s reset branch instead of reopening
        /// the menu.
        /// </summary>
        private static void CloseArchitectTreeSilently()
        {
            ArchitectTreeState.Close();
            ArchitectState.Reset();
        }

        /// <summary>
        /// Work has two mutually exclusive views (WorkMenuState focused,
        /// WorkTableState table) sharing one window/tab entry — silences
        /// whichever one is actually active.
        /// </summary>
        private static void CloseWhicheverWorkViewIsActive()
        {
            if (WorkMenuState.IsActive)
            {
                WorkMenuState.CloseSilent();
            }
            if (WorkTableState.IsActive)
            {
                WorkTableState.Close();
            }
        }

        /// <summary>
        /// Research has two states that can be simultaneously active (menu +
        /// drilled-down project detail, RJ6) sharing one window/tab entry —
        /// the same silent expressions StateResetRegistry's own
        /// session-boundary reset already uses for this pair.
        /// </summary>
        private static void CloseWhicheverResearchViewIsActive()
        {
            if (WindowlessResearchDetailState.IsActive)
            {
                WindowlessResearchDetailState.ResetHard();
            }
            if (WindowlessResearchMenuState.IsActive)
            {
                WindowlessResearchMenuState.ResetHard();
            }
        }

        private static MainButtonDef Resolve(string defName)
        {
            MainButtonDef def;
            if (!resolvedDefs.TryGetValue(defName, out def))
            {
                // GetNamedSilentFail, never GetNamed: the Mechs def is absent
                // without Biotech, and that must not throw.
                def = DefDatabase<MainButtonDef>.GetNamedSilentFail(defName);
                resolvedDefs[defName] = def;
            }
            return def;
        }

        internal static void EnsureTabOpen(string defName)
        {
            if (Find.MainTabsRoot == null)
            {
                return;
            }
            MainButtonDef def = Resolve(defName);
            if (def == null)
            {
                return;
            }
            if (Find.MainTabsRoot.OpenTab != def)
            {
                // Vanilla's own tab-opening vehicle (decompiled
                // RimWorld/MainTabsRoot.cs:37), chirp included: none of these
                // states plays a sound of its own when it opens, so this is the
                // only TabOpen the player gets. The close side stays silent —
                // each state's own Close() already plays TabClose.
                Find.MainTabsRoot.SetCurrentTab(def);
            }
        }

        internal static void CloseTab(string defName)
        {
            if (Find.MainTabsRoot == null)
            {
                return;
            }
            MainButtonDef def = Resolve(defName);
            if (def == null)
            {
                return;
            }
            if (Find.MainTabsRoot.OpenTab == def)
            {
                // The state's own close already played its sound/announcement; this must stay
                // silent -- including the refocus re-announce this window's removal would
                // otherwise trigger through NotifyForeignWindowClosed.
                using (FocusStack.SuppressRefocus())
                {
                    Find.MainTabsRoot.EscapeCurrentTab(playSound: false);
                }
            }
        }

        internal static void Reconcile()
        {
            try
            {
                if (Current.ProgramState != ProgramState.Playing || Find.MainTabsRoot == null)
                {
                    return;
                }
                for (int i = 0; i < links.Length; i++)
                {
                    TabLink entry = links[i];
                    MainButtonDef def = Resolve(entry.DefName);
                    if (def == null)
                    {
                        continue;
                    }
                    bool windowOpen = Find.MainTabsRoot.OpenTab == def;
                    bool stateActive = entry.StateActive();
                    if (windowOpen && stateActive)
                    {
                        entry.armed = true;
                        continue;
                    }
                    if (!windowOpen && stateActive)
                    {
                        entry.armed = false;
                        entry.CloseStateSilently();
                        continue;
                    }
                    if (windowOpen && !stateActive && entry.armed)
                    {
                        entry.armed = false;
                        CloseTab(entry.DefName);
                        continue;
                    }
                    if (!windowOpen)
                    {
                        entry.armed = false;
                    }
                }
            }
            catch (Exception ex)
            {
                ModLogger.LimitedError("MainTabWindowLink reconcile error", ex);
            }
        }
    }
}
