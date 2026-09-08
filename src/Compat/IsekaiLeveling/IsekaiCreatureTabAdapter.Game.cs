using System;
using RimWorld;
using RimWorldAccess.Shell;
using Verse;

namespace RimWorldAccess
{
    /// <summary>
    /// Inspection-tree adapter for ITab_CreatureStats, the mod's "Status" tab on ranked
    /// creatures: rank and title (elite marked), level, XP for player-owned creatures, the six
    /// creature stats with descriptions, and the Stats button that opens the creature's
    /// allocation window when the player owns it.
    /// </summary>
    internal sealed class IsekaiCreatureTabAdapter : InspectNodeAdapter
    {
        private readonly Type tabType;

        public IsekaiCreatureTabAdapter(Type tabType)
        {
            this.tabType = tabType;
        }

        public override bool Ready => tabType != null && IsekaiCompat.CoreGate.Ensure();

        // Stable dispatch token (l10n-exempt: never displayed raw — DisplayName renders the mod's own tab label).
        public override string CategoryKey => "Isekai Creature";

        public override TabHandlerType Handler => TabHandlerType.RichNavigation;

        public override string DisplayName(InspectTabBase tab) => CompatText.ModText("TabStatus");

        public override string CategoryDisplayName(object obj) => CompatText.ModText("TabStatus");

        public override string CategoryLabel(object obj, string displayName)
        {
            object comp = IsekaiCompat.MobRankOf(obj as Pawn);
            if (comp == null)
                return displayName;
            return displayName + ", " + CompatText.ModArgs("Isekai_LevelRankDisplay",
                IsekaiCompat.MobLevel(comp), IsekaiCompat.MobRankString(comp));
        }

        public override bool CanExpand(object obj)
        {
            return obj is Pawn pawn && IsekaiCompat.MobRankOf(pawn) != null;
        }

        public override void BuildChildren(InspectionTreeItem categoryItem, object obj, InspectionMode mode)
        {
            if (categoryItem.Children.Count > 0)
                return;
            Pawn pawn = obj as Pawn;
            object comp = IsekaiCompat.MobRankOf(pawn);
            if (comp == null)
                return;
            bool playerOwned = pawn.Faction != null && pawn.Faction.IsPlayer;

            InspectNodeFactory.GuardedBuild("IsekaiCreatureTabAdapter", delegate
            {
                InspectNodeFactory.LiveDetailLine(categoryItem, delegate
                {
                    string title = IsekaiCompat.MobRankTitle(comp);
                    if (IsekaiCompat.MobIsElite(comp))
                        title += ", " + "RimWorldAccess.Compat.Isekai.Elite".Translate();
                    return title + ", " + CompatText.ModArgs("Isekai_LevelRankDisplay",
                        IsekaiCompat.MobLevel(comp), IsekaiCompat.MobRankString(comp));
                });
                if (playerOwned)
                {
                    InspectNodeFactory.LiveDetailLine(categoryItem,
                        () => IsekaiCompat.XpLine(IsekaiCompat.MobXp(comp), IsekaiCompat.MobXpToNext(comp)));
                }

                object stats = IsekaiCompat.MobStats(comp);
                InspectNodeFactory.Section(categoryItem, CompatText.ModText("Isekai_CoreAttributes"), stats, delegate (InspectionTreeItem section)
                {
                    foreach (int ordinal in IsekaiCompat.StatDisplayOrder)
                    {
                        int captured = ordinal;
                        InspectNodeFactory.LiveSection(section,
                            () => IsekaiCompat.StatAbbreviation(captured) + " " + IsekaiCompat.StatValue(stats, captured),
                            stats, delegate (InspectionTreeItem statSection)
                            {
                                InspectNodeFactory.DetailLine(statSection, IsekaiCompat.CreatureStatName(captured));
                                InspectNodeFactory.DetailLines(statSection, IsekaiCompat.CreatureStatDescription(captured));
                                InspectNodeFactory.LiveDetailLine(statSection,
                                    () => CompatText.Flatten(IsekaiCompat.CreatureStatEffects(captured, IsekaiCompat.StatValue(stats, captured))));
                            });
                    }
                });

                if (playerOwned && mode == InspectionMode.Full)
                {
                    InspectNodeFactory.ActionRow(categoryItem, CompatText.ModText("Isekai_Stats"), pawn,
                        () => IsekaiWindowCompat.OpenPawnWindow("IsekaiLeveling.UI.Window_CreatureStats", pawn), opensOverlayMenu: true);
                }
            });
        }
    }
}
