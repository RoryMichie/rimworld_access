using System.Collections.Generic;
using RimWorld;
using Verse;

namespace RimWorldAccess
{
    /// <summary>
    /// Adapter for the Log tab (rework §D2). Builds the Combat Log and Social
    /// Log subcategories for a pawn, each of which lazily builds its own log
    /// entries from the battle/play log on first expansion.
    /// </summary>
    internal sealed class PawnLogAdapter : InspectNodeAdapter
    {
        // Matches ITab_Pawn_Log.MaxLogLines (decompiled RimWorld/ITab_Pawn_Log.cs:35).
        // Vanilla applies this as a single budget shared by its combined
        // combat+social view; our Combat Log and Social Log are separate
        // subcategories, so each gets its own 300-entry budget, keeping the
        // most recent entries.
        private const int MaxLogLines = 300;

        public override string CategoryKey => "Log";

        public override TabHandlerType Handler => TabHandlerType.RichNavigation;

        public override void BuildChildren(InspectionTreeItem categoryItem, object obj, InspectionMode mode)
        {
            Pawn pawn = InspectionTreeBuilder.GetPawnFromThing(obj);
            if (pawn == null)
                return;

            BuildLogChildren(categoryItem, pawn);
        }

        /// <summary>
        /// Builds children for Log category - creates Combat Log and Social Log subcategories.
        /// </summary>
        private static void BuildLogChildren(InspectionTreeItem parentItem, Pawn pawn)
        {
            if (parentItem.Children.Count > 0)
                return; // Already built

            // Add Combat Log as expandable subcategory
            var combatLogItem = new InspectionTreeItem
            {
                Type = InspectionTreeItem.ItemType.SubCategory,
                Label = "RimWorldAccess.Inspection.Tree.CombatLog".Translate(),
                Data = new InspectSectionDatum(pawn, InspectSectionKind.LogCombat),
                IndentLevel = parentItem.IndentLevel + 1,
                IsExpandable = true,
                IsExpanded = false
            };
            combatLogItem.OnActivate = () => BuildCombatLogEntries(combatLogItem, pawn);
            InspectNodeFactory.Attach(parentItem, combatLogItem);

            // Add Social Log as expandable subcategory
            var socialLogItem = new InspectionTreeItem
            {
                Type = InspectionTreeItem.ItemType.SubCategory,
                Label = "RimWorldAccess.Inspection.Tree.SocialLog".Translate(),
                Data = new InspectSectionDatum(pawn, InspectSectionKind.LogSocial),
                IndentLevel = parentItem.IndentLevel + 1,
                IsExpandable = true,
                IsExpanded = false
            };
            socialLogItem.OnActivate = () => BuildSocialLogEntries(socialLogItem, pawn);
            InspectNodeFactory.Attach(parentItem, socialLogItem);
        }

        /// <summary>
        /// Builds combat log entries for a pawn.
        /// </summary>
        private static void BuildCombatLogEntries(InspectionTreeItem parentItem, Pawn pawn)
        {
            if (parentItem.Children.Count > 0)
                return; // Already built

            var entries = new List<(int ageTicks, string text, LogEntry entry)>();

            if (Find.BattleLog != null)
            {
                foreach (Battle battle in Find.BattleLog.Battles)
                {
                    if (!battle.Concerns(pawn))
                        continue;

                    foreach (LogEntry entry in battle.Entries)
                    {
                        // Vanilla's default (non-"show all") compact view hides entries
                        // whose ShowInCompactView() returns false — e.g. melee misses/
                        // grazes rolled out by DisplayChanceOnMiss unless alwaysShowInCompact
                        // (decompiled RimWorld/ITab_Pawn_Log_Utility.cs:206,
                        // Verse/BattleLogEntry_MeleeCombat.cs:224). We have no "show all"
                        // toggle, so this matches vanilla's default (showAll = false).
                        if (!entry.Concerns(pawn) || !entry.ShowInCompactView())
                            continue;

                        string entryText = entry.ToGameStringFromPOV(pawn).StripTags();
                        string timestamp = entry.Age.ToStringTicksToPeriod();
                        string displayText = "RimWorldAccess.Inspection.Tree.LogEntryFormat".Translate(timestamp, entryText);

                        entries.Add((entry.Age, displayText, entry));
                    }
                }
            }

            // Sort by age (most recent first)
            entries.Sort((a, b) => a.ageTicks.CompareTo(b.ageTicks));

            if (entries.Count > MaxLogLines)
                entries.RemoveRange(MaxLogLines, entries.Count - MaxLogLines);

            if (entries.Count == 0)
            {
                var noEntriesItem = new InspectionTreeItem
                {
                    Type = InspectionTreeItem.ItemType.DetailText,
                    Label = "RimWorldAccess.Inspection.Tree.NoCombatEntries".Translate(),
                    IndentLevel = parentItem.IndentLevel + 1,
                    IsExpandable = false
                };
                InspectNodeFactory.Attach(parentItem, noEntriesItem);
                return;
            }

            foreach (var (ageTicks, displayText, entry) in entries)
            {
                var logItem = new InspectionTreeItem
                {
                    Type = InspectionTreeItem.ItemType.DetailText,
                    Label = displayText,
                    IndentLevel = parentItem.IndentLevel + 1,
                    IsExpandable = false,
                    Data = new LogEntryDatum(pawn, entry)
                };

                if (entry.CanBeClickedFromPOV(pawn))
                {
                    logItem.OnActivate = () =>
                    {
                        entry.ClickedFromPOV(pawn);
                        MapNavigationState.SpeakJumpedTo(null);
                    };
                }

                InspectNodeFactory.Attach(parentItem, logItem);
            }
        }

        /// <summary>
        /// Builds social log entries for a pawn.
        /// </summary>
        private static void BuildSocialLogEntries(InspectionTreeItem parentItem, Pawn pawn)
        {
            if (parentItem.Children.Count > 0)
                return; // Already built

            var entries = new List<(int ageTicks, string text, LogEntry entry)>();

            if (Find.PlayLog != null)
            {
                foreach (LogEntry entry in Find.PlayLog.AllEntries)
                {
                    if (!entry.Concerns(pawn))
                        continue;

                    string entryText = entry.ToGameStringFromPOV(pawn).StripTags();
                    string timestamp = entry.Age.ToStringTicksToPeriod();
                    string displayText = "RimWorldAccess.Inspection.Tree.LogEntryFormat".Translate(timestamp, entryText);

                    entries.Add((entry.Age, displayText, entry));
                }
            }

            // Sort by age (most recent first)
            entries.Sort((a, b) => a.ageTicks.CompareTo(b.ageTicks));

            if (entries.Count > MaxLogLines)
                entries.RemoveRange(MaxLogLines, entries.Count - MaxLogLines);

            if (entries.Count == 0)
            {
                var noEntriesItem = new InspectionTreeItem
                {
                    Type = InspectionTreeItem.ItemType.DetailText,
                    Label = "RimWorldAccess.Inspection.Tree.NoSocialEntries".Translate(),
                    IndentLevel = parentItem.IndentLevel + 1,
                    IsExpandable = false
                };
                InspectNodeFactory.Attach(parentItem, noEntriesItem);
                return;
            }

            foreach (var (ageTicks, displayText, entry) in entries)
            {
                var logItem = new InspectionTreeItem
                {
                    Type = InspectionTreeItem.ItemType.DetailText,
                    Label = displayText,
                    IndentLevel = parentItem.IndentLevel + 1,
                    IsExpandable = false,
                    Data = new LogEntryDatum(pawn, entry)
                };

                if (entry.CanBeClickedFromPOV(pawn))
                {
                    logItem.OnActivate = () =>
                    {
                        entry.ClickedFromPOV(pawn);
                        MapNavigationState.SpeakJumpedTo(null);
                    };
                }

                InspectNodeFactory.Attach(parentItem, logItem);
            }
        }
    }
}
