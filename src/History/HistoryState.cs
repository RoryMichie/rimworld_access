using RimWorld;
using Verse;

namespace RimWorldAccess
{
    /// <summary>
    /// Main state management for the History tab accessibility.
    /// Manages tab switching between Graph, Messages, and Statistics tabs.
    /// </summary>
    public static class HistoryState
    {
        /// <summary>
        /// Tab indices — identical to RimWorld's own private HistoryTab enum
        /// (MainTabWindow_History.cs:14-19: Graph = 0, Messages = 1,
        /// Statistics = 2), so no separate RimWorld-index mapping is needed
        /// for visual sync.
        /// </summary>
        public enum Tab
        {
            Graph = 0,
            Messages = 1,
            Statistics = 2
        }

        private const int TabCount = 3;

        private static bool isActive = false;
        // Matches vanilla's own default (MainTabWindow_History.curTab starts
        // at HistoryTab.Graph).
        private static Tab currentTab = Tab.Graph;

        /// <summary>
        /// Gets whether the History menu is currently active.
        /// </summary>
        public static bool IsActive => isActive;

        /// <summary>
        /// Gets the currently selected tab.
        /// </summary>
        public static Tab CurrentTab => currentTab;

        /// <summary>
        /// Opens the History accessibility state.
        /// Called when MainTabWindow_History opens.
        /// </summary>
        public static void Open()
        {
            isActive = true;

            // Matches vanilla's own default tab (Graph) rather than forcing
            // one — SyncVisualTab is a no-op here since PreOpen already set
            // curTab to Graph before HistoryPatch's PostOpen postfix runs.
            currentTab = Tab.Graph;
            SyncVisualTab();

            HistoryGraphState.Open();

            TolkHelper.Speak("RimWorldAccess.History.Intro".Loc());
        }

        /// <summary>
        /// Closes the History accessibility state.
        /// Called when MainTabWindow_History closes.
        /// </summary>
        public static void Close()
        {
            // Close all sub-states
            HistoryGraphState.Close();
            HistoryStatisticsState.Close();
            HistoryMessagesState.Close();

            isActive = false;
            currentTab = Tab.Graph;
        }

        /// <summary>
        /// Switches to the next tab (Tab key).
        /// </summary>
        public static void NextTab()
        {
            CloseCurrentTabState();

            if ((int)currentTab == TabCount - 1)
                MenuHelper.PlayWrapTone();
            currentTab = (Tab)(((int)currentTab + 1) % TabCount);

            SyncVisualTab();
            AnnounceCurrentTab();
            OpenCurrentTabState();
        }

        /// <summary>
        /// Switches to the previous tab (Shift+Tab).
        /// </summary>
        public static void PreviousTab()
        {
            CloseCurrentTabState();

            if ((int)currentTab == 0)
                MenuHelper.PlayWrapTone();
            currentTab = (Tab)(((int)currentTab + TabCount - 1) % TabCount);

            SyncVisualTab();
            AnnounceCurrentTab();
            OpenCurrentTabState();
        }

        /// <summary>
        /// Closes the current tab's sub-state.
        /// </summary>
        private static void CloseCurrentTabState()
        {
            switch (currentTab)
            {
                case Tab.Graph:
                    HistoryGraphState.Close();
                    break;
                case Tab.Statistics:
                    HistoryStatisticsState.Close();
                    break;
                case Tab.Messages:
                    HistoryMessagesState.Close();
                    break;
            }
        }

        /// <summary>
        /// Opens the current tab's sub-state.
        /// </summary>
        private static void OpenCurrentTabState()
        {
            switch (currentTab)
            {
                case Tab.Graph:
                    HistoryGraphState.Open();
                    break;
                case Tab.Statistics:
                    HistoryStatisticsState.Open();
                    break;
                case Tab.Messages:
                    HistoryMessagesState.Open();
                    break;
            }
        }

        /// <summary>
        /// Syncs the visual UI tab to match our internal state. Our enum's
        /// values are identical to RimWorld's own HistoryTab enum, so the
        /// index needs no translation.
        /// </summary>
        private static void SyncVisualTab()
        {
            HistoryHelper.SetCurrentTab((int)currentTab);
        }

        /// <summary>
        /// Announces the current tab name.
        /// </summary>
        private static void AnnounceCurrentTab()
        {
            TolkHelper.Speak("RimWorldAccess.History.Tab.Current".Loc(GetTabName()));
        }

        /// <summary>
        /// Gets the localized tab name for announcements.
        /// </summary>
        public static string GetTabName()
        {
            switch (currentTab)
            {
                case Tab.Graph: return "RimWorldAccess.History.Tab.Graph".Translate();
                case Tab.Messages: return "RimWorldAccess.History.Tab.Messages".Translate();
                default: return "RimWorldAccess.History.Tab.Statistics".Translate();
            }
        }

        /// <summary>
        /// Checks if any sub-state has an active typeahead search.
        /// Used by OnCancelKeyPressed patch to determine if Escape should clear search first.
        /// </summary>
        public static bool HasActiveTypeahead
        {
            get
            {
                if (currentTab == Tab.Graph && HistoryGraphState.HasActiveSearch)
                    return true;
                if (currentTab == Tab.Statistics && HistoryStatisticsState.HasActiveSearch)
                    return true;
                if (currentTab == Tab.Messages && HistoryMessagesState.HasActiveSearch)
                    return true;
                return false;
            }
        }
    }
}
