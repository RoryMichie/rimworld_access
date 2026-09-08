using System.Collections.Generic;
using RimWorld;
using Verse;
using Verse.Steam;

namespace RimWorldAccess
{
    /// <summary>
    /// Column state and the data-access helpers for the Page_ModsConfig mod list. Split out of
    /// ModListState, then trimmed for the region-grammar rework (ModListScreenScope). The hand-rolled
    /// row cursor and typeahead this class used to own (SelectNext/Previous, JumpToFirst/Last,
    /// HandleTypeahead, GetModLabels) are RETIRED: ModListScreenScope's region-0 ListModel and the base
    /// ScreenScope's shared typeahead engine replace them (the CANDIDATE NOTE below, formerly flagging
    /// this hand-rolled cursor as the audit's confirmed gap, is resolved by that migration).
    ///
    /// <see cref="SelectedIndex"/> now exists purely as a BRIDGE field:
    /// ModListScreenScope mirrors its own region-0 cursor into it before
    /// calling into <see cref="ModListActions"/> or <see cref="SyncSelection"/>
    /// (both of which read it directly rather than taking a row parameter),
    /// and reads it back afterward since some of those calls advance it
    /// themselves (QuickToggle, MoveUp/MoveDown). It is never the source of
    /// truth for "where is the keyboard cursor" — the scope's own ListModel is.
    /// </summary>
    internal static class ModListNavigation
    {
        private static ModListColumn currentColumn = ModListColumn.Active;
        private static int selectedIndex = 0;

        public static ModListColumn CurrentColumn => currentColumn;
        public static int SelectedIndex => selectedIndex;

        internal static void SetColumn(ModListColumn column)
        {
            currentColumn = column;
        }

        internal static void SetSelectedIndex(int index)
        {
            selectedIndex = index;
        }

        /// <summary>Resets nav state for Open() (matches the original Open()'s inline resets).</summary>
        internal static void ResetForOpen()
        {
            currentColumn = ModListColumn.Active;
            selectedIndex = 0;
        }

        /// <summary>
        /// Resets nav state for Close() (matches the original Close()'s inline
        /// resets — note currentColumn is NOT reset here, exactly as before).
        /// </summary>
        internal static void ResetForClose()
        {
            selectedIndex = 0;
        }

        /// <summary>
        /// Left/Right: flips the active column, resets to its first row, and
        /// speaks the column-switch (or column-empty) announcement — the
        /// pre-existing behavior ModListScreenScope's own Left/Right claim
        /// preserves verbatim, then re-syncs its own region-0 cursor to row 0
        /// to match.
        /// </summary>
        internal static void SwitchColumn()
        {
            currentColumn = (currentColumn == ModListColumn.Active) ? ModListColumn.Inactive : ModListColumn.Active;
            selectedIndex = 0;

            // A column with only downloading placeholder rows (no real mods)
            // still counts as non-empty (Slice 2 parity with DoModRowDownloading).
            var list = GetCurrentList();
            if ((list != null && list.Count > 0) || DownloadingCount() > 0)
            {
                SyncSelection();
                ModListAnnouncements.AnnounceColumnSwitch();
            }
            else
            {
                string columnName = currentColumn == ModListColumn.Active
                    ? "RimWorldAccess.ModList.ColumnActive".Translate().ToString()
                    : "RimWorldAccess.ModList.ColumnInactive".Translate().ToString();
                ModListAnnouncements.AnnounceColumnEmpty(columnName);
            }
        }

        // ============================================================
        // Data access helpers
        // ============================================================

        internal static List<ModMetaData> GetCurrentList()
        {
            if (currentColumn == ModListColumn.Active)
            {
                return ModListVanillaBridge.GetFilteredActiveModList();
            }
            else
            {
                return ModListVanillaBridge.GetFilteredInactiveModList();
            }
        }

        /// <summary>
        /// Downloading-mod placeholder rows (Slice 2, parity with
        /// DoModRowDownloading, decompiled Page_ModsConfig.cs:386-401) appear
        /// only in the Inactive column, appended after the real mod rows --
        /// same placement vanilla draws them in.
        /// </summary>
        internal static int DownloadingCount()
        {
            if (currentColumn != ModListColumn.Inactive) return 0;
            return ModListVanillaBridge.GetDownloadingItems().Count;
        }

        /// <summary>
        /// Non-null only when the cursor sits past the real mod rows into the
        /// appended downloading placeholders. Steam callbacks
        /// (Notify_Subscribed/Installed/Unsubscribed) rebuild the downloading
        /// list asynchronously, so this clamps selectedIndex back into range
        /// first rather than trusting it still fits.
        /// </summary>
        internal static WorkshopItem_Downloading GetSelectedDownloadingItem()
        {
            if (currentColumn != ModListColumn.Inactive) return null;
            ClampSelectedIndex();

            var list = GetCurrentList() ?? new List<ModMetaData>();
            var downloading = ModListVanillaBridge.GetDownloadingItems();
            int downloadingIndex = selectedIndex - list.Count;
            if (downloadingIndex < 0 || downloadingIndex >= downloading.Count) return null;
            return downloading[downloadingIndex];
        }

        internal static ModMetaData GetSelectedMod()
        {
            ClampSelectedIndex();
            var list = GetCurrentList();
            if (list == null || list.Count == 0 || selectedIndex >= list.Count)
                return null;
            return list[selectedIndex];
        }

        /// <summary>
        /// Keeps selectedIndex inside the combined mod-rows-plus-downloading-rows
        /// range. The downloading list is rebuilt asynchronously by Steam
        /// callbacks (WorkshopItems.RebuildItemsList), so it can shrink between
        /// one input event and the next -- every accessor that indexes against
        /// the combined count calls this first.
        /// </summary>
        private static void ClampSelectedIndex()
        {
            var list = GetCurrentList() ?? new List<ModMetaData>();
            int total = list.Count + DownloadingCount();
            if (total <= 0)
            {
                selectedIndex = 0;
            }
            else if (selectedIndex >= total)
            {
                selectedIndex = total - 1;
            }
            else if (selectedIndex < 0)
            {
                selectedIndex = 0;
            }
        }

        internal static void SyncSelection()
        {
            var page = ModListState.CurrentPage;
            if (page == null) return;
            var mod = GetSelectedMod();
            if (mod != null)
            {
                ModListVanillaBridge.SelectMod(page, mod);
            }
        }
    }
}
