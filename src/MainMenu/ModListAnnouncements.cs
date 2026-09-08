using System.Collections.Generic;
using RimWorld;
using Verse;
using RimWorldAccess.Shell;

namespace RimWorldAccess
{
    /// <summary>
    /// The mod list's row-description builders and the column-switch/ column-empty announcements. Split
    /// out of ModListState, then trimmed for the region-grammar rework (ModListScreenScope). The
    /// opening-announcement and arrow/typeahead-triggered announcements this class used to own
    /// (AnnounceOpening, AnnounceCurrentMod, AnnounceWithSearch) are RETIRED: ModListScreenScope's own
    /// OnFocus (open announcement) and the base ScreenScope's shared arrow/typeahead engine (which
    /// reads DescribeContentItem, in turn built from <see cref="BuildModDescription"/> below) replace
    /// them.
    ///
    /// <see cref="BuildModDescription"/>/<see cref="BuildDownloadingDescription"/>
    /// return the raw <see cref="ElementDescription"/> for one row — the SAME
    /// shape ModListScreenScope.DescribeContentItem needs for region 0 — so the
    /// Role/Check/Extras judgment call below (checkbox chosen for its STATE
    /// semantics even though DoModRow draws no literal checkbox glyph — active
    /// vs. inactive is grey-vs-white text color plus which column the row lives
    /// in) has exactly one home instead of two copies.
    /// </summary>
    internal static class ModListAnnouncements
    {
        /// <summary>
        /// Builds the list-level description for a mod row: Name, checkbox
        /// state, position, then warning/error/author as the Extras detail
        /// tail (see class remarks for the Role judgment call).
        /// </summary>
        internal static ElementDescription BuildModDescription(ModMetaData mod, int positionIndex, int positionCount)
        {
            ElementDescription d = new ElementDescription();
            d.Label = mod.Name;
            d.Role = ElementRole.Checkbox;
            d.Check = mod.Active ? CheckState.Checked : CheckState.Unchecked;
            d.PositionIndex = positionIndex;
            d.PositionCount = positionCount;

            var extras = new List<string>();

            // Warning/error right after name for immediate awareness, same as before.
            if (!mod.VersionCompatible)
            {
                extras.Add("ModNotMadeForThisVersionShort".Translate().ToString());
            }

            var warnings = ModListVanillaBridge.GetModWarningsCached();
            if (warnings != null && warnings.TryGetValue(mod.PackageId, out string warning) && !string.IsNullOrEmpty(warning))
            {
                extras.Add("RimWorldAccess.ModList.ContainsErrors".Translate());
            }

            // Author
            if (!mod.AuthorsString.NullOrEmpty())
            {
                extras.Add("RimWorldAccess.ModList.ByAuthor".Translate(mod.AuthorsString));
            }

            if (extras.Count > 0)
            {
                d.Extras = string.Join(". ", extras);
            }

            return d;
        }

        /// <summary>
        /// Description for a downloading-mod placeholder row (Slice 2, parity
        /// with DoModRowDownloading, decompiled Page_ModsConfig.cs:584-602).
        /// The row carries no active/inactive membership -- its only action is
        /// the Details region's Unsubscribe button -- so this is a plain
        /// MenuItem with just the position, otherwise matching
        /// <see cref="BuildModDescription"/>'s shape.
        /// </summary>
        internal static ElementDescription BuildDownloadingDescription(int positionIndex, int positionCount)
        {
            ElementDescription d = new ElementDescription();
            d.Label = "Downloading".Translate().ToString();
            d.Role = ElementRole.MenuItem;
            d.PositionIndex = positionIndex;
            d.PositionCount = positionCount;
            return d;
        }

        /// <summary>
        /// The "Column N of 2" cue folded into both the opening announcement
        /// (<see cref="RimWorldAccess.Shell.ModListScreenScope"/>'s OnFocus) and
        /// <see cref="AnnounceColumnSwitch"/> below, one home for the position math instead of the two
        /// duplicated copies a naive fold-in would need. Active is the left-hand column (1 of 2),
        /// Inactive the right-hand one (2 of 2), matching the vanilla page's own layout.
        /// </summary>
        internal static string ColumnPositionText()
        {
            int index = ModListNavigation.CurrentColumn == ModListColumn.Active ? 1 : 2;
            return "RimWorldAccess.ModList.ColumnPosition".Translate(index, 2).ToString();
        }

        /// <summary>
        /// Column-switch announcement (Left/Right in the Mods region):
        /// header phrase plus the newly-focused row's own description,
        /// composed through the same vocabulary a live focus announcement
        /// uses. Preserved verbatim from the pre-region-grammar shape, save
        /// for the column-position cue folded into the header phrase.
        /// </summary>
        internal static void AnnounceColumnSwitch()
        {
            var list = ModListNavigation.GetCurrentList();
            string columnName = ModListNavigation.CurrentColumn == ModListColumn.Active
                ? "Enabled".Translate().ToString()
                : "Disabled".Translate().ToString();
            int count = (list?.Count ?? 0) + ModListNavigation.DownloadingCount();

            string announcement = "RimWorldAccess.ModList.ColumnHeader".Translate(columnName, count, ColumnPositionText());

            var mod = ModListNavigation.GetSelectedMod();
            if (mod != null)
            {
                ElementDescription d = BuildModDescription(mod, ModListNavigation.SelectedIndex + 1, count);
                announcement += AnnouncementComposer.ComposeFocus(d, TranslatedShellVocabulary.Instance, TextDialogShared.StandardComposeOptions());
            }
            else
            {
                var downloadingItem = ModListNavigation.GetSelectedDownloadingItem();
                if (downloadingItem != null)
                {
                    ElementDescription d = BuildDownloadingDescription(ModListNavigation.SelectedIndex + 1, count);
                    announcement += AnnouncementComposer.ComposeFocus(d, TranslatedShellVocabulary.Instance, TextDialogShared.StandardComposeOptions());
                }
            }

            TolkHelper.SpeakData(announcement);
        }

        /// <summary>Announces that the just-switched-to column has no mods in it.</summary>
        internal static void AnnounceColumnEmpty(string columnName)
        {
            TolkHelper.Speak("RimWorldAccess.ModList.ColumnEmpty".Loc(columnName));
        }
    }
}
