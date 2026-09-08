using System;
using System.Collections.Generic;
using System.Reflection;
using System.Text.RegularExpressions;
using HarmonyLib;
using RimWorld;
using RimWorld.Planet;
using Verse;
using Verse.Sound;
using UnityEngine;

namespace RimWorldAccess
{
    /// <summary>Reads statistics and archive items out of the History tab's own state.</summary>
    public static class HistoryHelper
    {
        private static readonly Regex TagRegex = new Regex(@"</?[a-zA-Z][^>]*>");

        /// <summary>A single statistic entry from the Statistics tab.</summary>
        public class StatisticEntry
        {
            public string Name { get; set; }
            public string Value { get; set; }
            public string Tooltip { get; set; }

            public StatisticEntry(string name, string value, string tooltip = null)
            {
                Name = name;
                Value = value;
                Tooltip = tooltip;
            }

        }

        /// <summary>Collects all statistics from the Statistics tab, matching what DoStatisticsPage displays.</summary>
        public static List<StatisticEntry> CollectStatistics()
        {
            var stats = new List<StatisticEntry>();

            try
            {
                // Real playtime, not in-game simulation time (Find.GameInfo.RealPlayTimeInteracting).
                if (Find.GameInfo != null)
                {
                    float secondsPlayed = Find.GameInfo.RealPlayTimeInteracting;
                    TimeSpan realPlaytime = TimeSpan.FromSeconds(secondsPlayed);
                    string playtimeFormatted = FormatPlaytime(realPlaytime);
                    stats.Add(new StatisticEntry("Playtime".Translate(), playtimeFormatted));
                }

                if (Find.Storyteller != null)
                {
                    string storyteller = Find.Storyteller.def?.LabelCap ?? "RimWorldAccess.History.Unknown".Translate();
                    stats.Add(new StatisticEntry("Storyteller".Translate(), storyteller));

                    string difficultyName = Find.Storyteller.difficultyDef?.LabelCap ?? "RimWorldAccess.History.Unknown".Translate();
                    stats.Add(new StatisticEntry("Difficulty".Translate(), difficultyName));
                }

                // Map-specific statistics (only if a map is loaded)
                Map currentMap = Find.CurrentMap;
                if (currentMap != null)
                {
                    if (currentMap.wealthWatcher != null)
                    {
                        float totalWealth = currentMap.wealthWatcher.WealthTotal;
                        stats.Add(new StatisticEntry("ThisMapColonyWealthTotal".Translate(), totalWealth.ToString("F0")));

                        float itemWealth = currentMap.wealthWatcher.WealthItems;
                        stats.Add(new StatisticEntry("ThisMapColonyWealthItems".Translate(), itemWealth.ToString("F0")));

                        float buildingWealth = currentMap.wealthWatcher.WealthBuildings;
                        stats.Add(new StatisticEntry("ThisMapColonyWealthBuildings".Translate(), buildingWealth.ToString("F0")));

                        float pawnWealth = currentMap.wealthWatcher.WealthPawns;
                        stats.Add(new StatisticEntry("ThisMapColonyWealthColonistsAndTameAnimals".Translate(), pawnWealth.ToString("F0")));
                    }
                }

                // Threat statistics: global, not map-specific.
                if (Find.StoryWatcher?.statsRecord != null)
                {
                    int numThreatBigs = Find.StoryWatcher.statsRecord.numThreatBigs;
                    stats.Add(new StatisticEntry("NumThreatBigs".Translate(), numThreatBigs.ToString()));

                    int numRaidsEnemy = Find.StoryWatcher.statsRecord.numRaidsEnemy;
                    stats.Add(new StatisticEntry("NumEnemyRaids".Translate(), numRaidsEnemy.ToString()));
                }

                if (currentMap != null && currentMap.damageWatcher != null)
                {
                    float damage = currentMap.damageWatcher.DamageTakenEver;
                    stats.Add(new StatisticEntry("ThisMapDamageTaken".Translate(), damage.ToString("F0")));
                }

                if (Find.StoryWatcher?.statsRecord != null)
                {
                    int colonistsKilled = Find.StoryWatcher.statsRecord.colonistsKilled;
                    stats.Add(new StatisticEntry("ColonistsKilled".Translate(), colonistsKilled.ToString()));

                    int colonistsLaunched = Find.StoryWatcher.statsRecord.colonistsLaunched;
                    stats.Add(new StatisticEntry("ColonistsLaunched".Translate(), colonistsLaunched.ToString()));
                }
            }
            catch (Exception ex)
            {
                Log.Warning($"RimWorld Access: Failed to collect statistics: {ex.Message}");
            }

            return stats;
        }

        /// <summary>Formats a TimeSpan as playtime through the game's own LetterDay/Hour/Minute/Second keys.</summary>
        private static string FormatPlaytime(TimeSpan playtime)
        {
            var parts = new List<string>();

            if (playtime.Days > 0)
            {
                parts.Add($"{playtime.Days}{"LetterDay".Translate()}");
            }
            if (playtime.Hours > 0)
            {
                parts.Add($"{playtime.Hours}{"LetterHour".Translate()}");
            }
            if (playtime.Minutes > 0)
            {
                parts.Add($"{playtime.Minutes}{"LetterMinute".Translate()}");
            }
            if (playtime.Seconds > 0 || parts.Count == 0)
            {
                parts.Add($"{playtime.Seconds}{"LetterSecond".Translate()}");
            }

            return string.Join(" ", parts);
        }

        /// <summary>Wrapper for IArchivable items with accessor properties.</summary>
        public class ArchiveItemWrapper
        {
            private readonly IArchivable source;

            public ArchiveItemWrapper(IArchivable archivable)
            {
                source = archivable;
            }

            public IArchivable Source => source;

            public string Label => StripTags(source.ArchivedLabel ?? "RimWorldAccess.History.Unknown".Translate());

            public string Tooltip => StripTags(source.ArchivedTooltip ?? "");

            public string[] TooltipLines
            {
                get
                {
                    if (string.IsNullOrEmpty(Tooltip))
                        return new string[0];

                    string[] lines = Tooltip.Split('\n');
                    var nonEmpty = new List<string>();
                    foreach (var line in lines)
                    {
                        string trimmed = line.Trim();
                        if (!string.IsNullOrEmpty(trimmed))
                            nonEmpty.Add(trimmed);
                    }
                    return nonEmpty.ToArray();
                }
            }

            public int Timestamp => source.CreatedTicksGame;

            public bool HasValidTarget => source.LookTargets?.IsValid ?? false;

            public GlobalTargetInfo PrimaryTarget => source.LookTargets?.TryGetPrimaryTarget() ?? GlobalTargetInfo.Invalid;

            public bool IsPinned => Find.Archive?.IsPinned(source) ?? false;

            public bool IsLetter => source is Letter;

            public bool IsMessage => source is Message;

            public bool IsArchivedDialog => source is ArchivedDialog;

            public string TypeLabel => IsLetter
                ? "RimWorldAccess.History.Type.Letter".Translate()
                : IsMessage
                    ? "RimWorldAccess.History.Type.Message".Translate()
                    : IsArchivedDialog
                        ? "RimWorldAccess.History.Type.Dialog".Translate()
                        : "RimWorldAccess.History.Type.Item".Translate();

            public string DateLabel
            {
                get
                {
                    if (source.CreatedTicksGame <= 0)
                        return "RimWorldAccess.History.UnknownDate".Translate();

                    try
                    {
                        // Vanilla's own row date label reads Find.CurrentMap's tile
                        // (MainTabWindow_History.cs:249), falling back to tile 0 only when no map is
                        // loaded, and passes ABSOLUTE ticks (GenDate.TickGameToAbs) into
                        // DateShortStringAt — the clock that method expects, not the raw game tick.
                        Vector2 location = Find.CurrentMap != null ? Find.WorldGrid.LongLatOf(Find.CurrentMap.Tile) : default;
                        return GenDate.DateShortStringAt(GenDate.TickGameToAbs(source.CreatedTicksGame), location);
                    }
                    catch
                    {
                        return "RimWorldAccess.History.UnknownDate".Translate();
                    }
                }
            }

            /// <summary>Opens the archived item (shows its full content).</summary>
            public void Open()
            {
                try
                {
                    source.OpenArchived();
                }
                catch (Exception ex)
                {
                    Log.Warning($"RimWorld Access: Failed to open archived item: {ex.Message}");
                }
            }

            /// <summary>Toggles the pinned state of this item.</summary>
            public void TogglePin()
            {
                try
                {
                    if (Find.Archive == null)
                        return;

                    bool wasPinned = IsPinned;

                    // Archive uses Pin() and Unpin() methods, not SetPinned
                    if (wasPinned)
                    {
                        Find.Archive.Unpin(source);
                    }
                    else
                    {
                        Find.Archive.Pin(source);
                    }

                    // Vanilla's own row pin-icon click plays this cue pair on toggle
                    // (MainTabWindow_History.cs:269-278).
                    (wasPinned ? SoundDefOf.Checkbox_TurnedOff : SoundDefOf.Checkbox_TurnedOn).PlayOneShotOnCamera();
                }
                catch (Exception ex)
                {
                    Log.Warning($"RimWorld Access: Failed to toggle pin: {ex.Message}");
                }
            }

            /// <summary>Jumps the camera to this item's location.</summary>
            public void JumpTo()
            {
                if (!HasValidTarget)
                {
                    TolkHelper.Speak("RimWorldAccess.History.NoLocation".Loc());
                    return;
                }

                try
                {
                    // For world targets, set the pending tile BEFORE CameraJumper opens the world
                    // view: WorldNavigationState.Open() runs a frame later, when WorldNavigationPatch
                    // sees the mode change, and would otherwise default to the colony tile.
                    if (PrimaryTarget.HasWorldObject)
                    {
                        PlanetTile tile = PrimaryTarget.WorldObject.Tile;
                        if (tile.Valid)
                        {
                            WorldNavigationState.PendingStartTile = tile;
                        }
                    }
                    else if (PrimaryTarget.Tile.Valid && !PrimaryTarget.HasThing && !PrimaryTarget.Cell.IsValid)
                    {
                        WorldNavigationState.PendingStartTile = PrimaryTarget.Tile;
                    }

                    CameraJumper.TryJumpAndSelect(PrimaryTarget);

                    // Also set current tile in case world view was already open (Open() won't be called)
                    if (PrimaryTarget.HasWorldObject)
                    {
                        PlanetTile tile = PrimaryTarget.WorldObject.Tile;
                        if (tile.Valid)
                        {
                            WorldNavigationState.CurrentSelectedTile = tile;
                        }
                    }
                    else if (PrimaryTarget.Tile.Valid && !PrimaryTarget.HasThing && !PrimaryTarget.Cell.IsValid)
                    {
                        WorldNavigationState.CurrentSelectedTile = PrimaryTarget.Tile;
                    }
                    else if (MapNavigationState.IsInitialized && PrimaryTarget.HasThing)
                    {
                        MapNavigationState.CurrentCursorPosition = PrimaryTarget.Thing.Position;
                    }
                    else if (MapNavigationState.IsInitialized && PrimaryTarget.Cell.IsValid)
                    {
                        MapNavigationState.CurrentCursorPosition = PrimaryTarget.Cell;
                    }

                    MapNavigationState.SpeakJumpedTo(GetTargetDescription());
                }
                catch (Exception ex)
                {
                    Log.Warning($"RimWorld Access: Failed to jump to location: {ex.Message}");
                    TolkHelper.Speak("RimWorldAccess.History.FailedToJump".Loc());
                }
            }

            /// <summary>A human-readable description of the target.</summary>
            public string GetTargetDescription()
            {
                if (!HasValidTarget)
                    return "RimWorldAccess.History.Target.Unknown".Translate();

                var target = PrimaryTarget;

                if (target.HasThing)
                {
                    Thing thing = target.Thing;
                    if (thing is Pawn pawn)
                        return pawn.LabelShort;
                    return thing.LabelShort ?? thing.def?.label ?? "RimWorldAccess.History.Target.Thing".Translate();
                }

                if (target.Cell.IsValid)
                {
                    return "RimWorldAccess.History.Target.Position".Translate(target.Cell.x, target.Cell.z);
                }

                if (target.HasWorldObject)
                {
                    return target.WorldObject.LabelShort ?? "RimWorldAccess.History.Target.WorldLocation".Translate();
                }

                return "RimWorldAccess.History.Target.Generic".Translate();
            }

            /// <summary>
            /// The list row's data portion (Pinned flag, Label, TypeLabel, DateLabel), comma-joined,
            /// with NO position suffix — the shared composer adds the position fragment itself, so
            /// baking one in here would double it up.
            /// </summary>
            public string BuildListAnnouncementLabel()
            {
                var parts = new List<string>();

                if (IsPinned)
                    parts.Add("RimWorldAccess.History.Pinned".Translate());

                parts.Add(Label);
                parts.Add(TypeLabel);
                parts.Add(DateLabel);

                return string.Join(", ", parts);
            }

            /// <summary>
            /// The list-view announcement: "Pinned, Label, Letter, Date. X of Y" — content first, so a
            /// listener can stop once they have heard what they need.
            /// </summary>
            public string BuildListAnnouncement(int index, int total)
            {
                string announcement = BuildListAnnouncementLabel();
                string position = MenuHelper.FormatPosition(index, total);
                if (!string.IsNullOrEmpty(position))
                    announcement += $". {position}";

                return announcement;
            }
        }

        /// <summary>Collects archive items from Find.Archive, filtered by type, newest first.</summary>
        public static List<ArchiveItemWrapper> CollectArchiveItems(bool includeLetters, bool includeMessages)
        {
            var items = new List<ArchiveItemWrapper>();

            try
            {
                if (Find.Archive == null || Find.Archive.ArchivablesListForReading == null)
                    return items;

                foreach (IArchivable archivable in Find.Archive.ArchivablesListForReading)
                {
                    // Mirrors MainTabWindow_History.DoMessagesPage's row filter: Letters and
                    // ArchivedDialogs (quest dialogs, caravan meetings, narrative) both gate on the
                    // letters filter; any other third-party IArchivable is shown unconditionally.
                    bool include = (includeLetters || (!(archivable is Letter) && !(archivable is ArchivedDialog)))
                        && (includeMessages || !(archivable is Message));

                    if (include)
                    {
                        items.Add(new ArchiveItemWrapper(archivable));
                    }
                }

                items.Sort((a, b) => b.Timestamp.CompareTo(a.Timestamp));
            }
            catch (Exception ex)
            {
                Log.Warning($"RimWorld Access: Failed to collect archive items: {ex.Message}");
            }

            return items;
        }

        /// <summary>Labels for typeahead search over archive items.</summary>
        public static List<string> GetArchiveLabels(List<ArchiveItemWrapper> items)
        {
            var labels = new List<string>();
            foreach (var item in items)
            {
                labels.Add(item.Label);
            }
            return labels;
        }

        /// <summary>Labels for typeahead search over statistic entries.</summary>
        public static List<string> GetStatisticLabels(List<StatisticEntry> stats)
        {
            var labels = new List<string>();
            foreach (var stat in stats)
            {
                labels.Add(stat.Name);
            }
            return labels;
        }

        /// <summary>Strips XML-style tags from text.</summary>
        public static string StripTags(string text)
        {
            if (string.IsNullOrEmpty(text))
                return text;

            return TagRegex.Replace(text, "");
        }

        /// <summary>The currently open MainTabWindow_History, if any.</summary>
        public static MainTabWindow_History GetOpenHistoryWindow()
        {
            if (Find.WindowStack == null)
                return null;

            foreach (var window in Find.WindowStack.Windows)
            {
                if (window is MainTabWindow_History historyWindow)
                    return historyWindow;
            }

            return null;
        }

        /// <summary>Sets the current tab on the History window for visual sync (0=Graph, 1=Messages, 2=Statistics, RimWorld's enum order).</summary>
        public static void SetCurrentTab(int tabIndex)
        {
            try
            {
                FieldInfo curTabField = AccessTools.Field(typeof(MainTabWindow_History), "curTab");
                if (curTabField != null)
                {
                    // MUTATION-C: mirrors vanilla's own tab TabRecords, each a
                    // bare curTab = HistoryTab.X assignment in its clickedAction
                    // lambda (decompiled MainTabWindow_History.cs:73/77/81) — no
                    // gate to honor, the same shape SetCurrentGraphGroup/
                    // SetGraphSection already carry this marker.
                    Type historyTabType = curTabField.FieldType;
                    object tabValue = Enum.ToObject(historyTabType, tabIndex);
                    curTabField.SetValue(null, tabValue);
                }
            }
            catch (Exception ex)
            {
                Log.Warning($"RimWorld Access: Failed to set current tab: {ex.Message}");
            }
        }

        /// <summary>The Messages tab's filter states, read off the History window.</summary>
        public static (bool showLetters, bool showMessages) GetFilterStates()
        {
            bool showLetters = true;
            bool showMessages = false;

            try
            {
                var window = GetOpenHistoryWindow();
                if (window != null)
                {
                    FieldInfo showLettersField = AccessTools.Field(typeof(MainTabWindow_History), "showLetters");
                    FieldInfo showMessagesField = AccessTools.Field(typeof(MainTabWindow_History), "showMessages");

                    if (showLettersField != null)
                        showLetters = (bool)showLettersField.GetValue(window);
                    if (showMessagesField != null)
                        showMessages = (bool)showMessagesField.GetValue(window);
                }
            }
            catch (Exception ex)
            {
                Log.Warning($"RimWorld Access: Failed to get filter states: {ex.Message}");
            }

            return (showLetters, showMessages);
        }

        /// <summary>Sets the filter states on the History window for visual sync.</summary>
        public static void SetFilterStates(bool showLetters, bool showMessages)
        {
            try
            {
                var window = GetOpenHistoryWindow();
                if (window != null)
                {
                    FieldInfo showLettersField = AccessTools.Field(typeof(MainTabWindow_History), "showLetters");
                    FieldInfo showMessagesField = AccessTools.Field(typeof(MainTabWindow_History), "showMessages");

                    // MUTATION-C: mirrors vanilla's own two Widgets.CheckboxLabeled(...,
                    // ref showLetters/showMessages, ...) two-way-bound checkboxes
                    // (decompiled MainTabWindow_History.cs:153-154) — a bare private-
                    // static field write with no gate to honor, the same shape
                    // SetCurrentGraphGroup/SetGraphSection already carry this marker.
                    if (showLettersField != null)
                        showLettersField.SetValue(window, showLetters);
                    if (showMessagesField != null)
                        showMessagesField.SetValue(window, showMessages);
                }
            }
            catch (Exception ex)
            {
                Log.Warning($"RimWorld Access: Failed to set filter states: {ex.Message}");
            }
        }

        /// <summary>
        /// The recorder groups the Graph sub-tab's "Select graph" button offers: DoGraphPage's own
        /// devModeOnly filter (MainTabWindow_History.cs:343-357).
        /// </summary>
        public static List<HistoryAutoRecorderGroup> GetGraphGroups()
        {
            var result = new List<HistoryAutoRecorderGroup>();
            try
            {
                foreach (HistoryAutoRecorderGroup group in Find.History.Groups())
                {
                    if (!group.def.devModeOnly || Prefs.DevMode)
                        result.Add(group);
                }
            }
            catch (Exception ex)
            {
                Log.Warning($"RimWorld Access: Failed to collect graph groups: {ex.Message}");
            }
            return result;
        }

        /// <summary>
        /// The currently graphed recorder group (the window's own <c>historyAutoRecorderGroup</c>
        /// field, seeded in PreOpen and reassigned by "Select graph"). Reading it directly keeps our
        /// selection and the drawn graph one source of truth.
        /// </summary>
        public static HistoryAutoRecorderGroup GetCurrentGraphGroup()
        {
            try
            {
                var window = GetOpenHistoryWindow();
                if (window == null)
                    return null;

                FieldInfo groupField = AccessTools.Field(typeof(MainTabWindow_History), "historyAutoRecorderGroup");
                return groupField?.GetValue(window) as HistoryAutoRecorderGroup;
            }
            catch (Exception ex)
            {
                Log.Warning($"RimWorld Access: Failed to get graph group: {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// Sets the graphed recorder group for visual sync, mirroring the "Select graph"
        /// FloatMenuOption's own bare field write (MainTabWindow_History.cs:354).
        /// </summary>
        public static void SetCurrentGraphGroup(HistoryAutoRecorderGroup group)
        {
            try
            {
                var window = GetOpenHistoryWindow();
                if (window == null)
                    return;

                FieldInfo groupField = AccessTools.Field(typeof(MainTabWindow_History), "historyAutoRecorderGroup");
                // MUTATION-C: mirrors DoGraphPage's "Select graph" FloatMenuOption
                // action (historyAutoRecorderGroup = groupLocal, MainTabWindow_History.cs:354)
                // — a bare field write with no gate in vanilla to honor.
                groupField?.SetValue(window, group);
            }
            catch (Exception ex)
            {
                Log.Warning($"RimWorld Access: Failed to set graph group: {ex.Message}");
            }
        }

        /// <summary>The graphed date-range window (the History window's own <c>graphSection</c> field).</summary>
        public static FloatRange GetGraphSection()
        {
            try
            {
                var window = GetOpenHistoryWindow();
                if (window == null)
                    return new FloatRange(0f, (float)Find.TickManager.TicksGame / 60000f);

                FieldInfo sectionField = AccessTools.Field(typeof(MainTabWindow_History), "graphSection");
                if (sectionField != null)
                    return (FloatRange)sectionField.GetValue(window);
            }
            catch (Exception ex)
            {
                Log.Warning($"RimWorld Access: Failed to get graph section: {ex.Message}");
            }
            return new FloatRange(0f, (float)Find.TickManager.TicksGame / 60000f);
        }

        /// <summary>
        /// Sets the graphed date-range window for visual sync, mirroring the Last30/100/300Days and
        /// AllDays buttons' own bare writes (MainTabWindow_History.cs:323-341).
        /// </summary>
        public static void SetGraphSection(FloatRange section)
        {
            try
            {
                var window = GetOpenHistoryWindow();
                if (window == null)
                    return;

                FieldInfo sectionField = AccessTools.Field(typeof(MainTabWindow_History), "graphSection");
                // MUTATION-C: mirrors the Last30/100/300Days and AllDays buttons'
                // own lambdas (MainTabWindow_History.cs:323-341), each a bare
                // graphSection = new FloatRange(...) write with no gate.
                sectionField?.SetValue(window, section);
            }
            catch (Exception ex)
            {
                Log.Warning($"RimWorld Access: Failed to set graph section: {ex.Message}");
            }
        }
    }
}
