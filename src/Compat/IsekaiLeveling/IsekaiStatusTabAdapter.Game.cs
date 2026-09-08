using System;
using System.Collections;
using System.Collections.Generic;
using RimWorld;
using RimWorldAccess.Shell;
using Verse;
using Verse.Sound;

namespace RimWorldAccess
{
    /// <summary>
    /// Inspection-tree adapter for ISEKAI RPG LEVELING's ITab_IsekaiStats, the "Status" tab on
    /// every colonist: build title, level and rank, XP, stat points, the six stats with their
    /// descriptions and live effects, class passives, the equipped weapon's mastery, the aura
    /// chooser, the auto-distribute toggle, and the buttons that open the mod's Mastery, Stats
    /// and Constellation windows. Everything is read off IsekaiComponent and spoken through the
    /// mod's own keys; the tab's custom-styled drawing is not consulted.
    /// </summary>
    internal sealed class IsekaiStatusTabAdapter : InspectNodeAdapter
    {
        private readonly Type tabType;

        public IsekaiStatusTabAdapter(Type tabType)
        {
            this.tabType = tabType;
        }

        public override bool Ready => tabType != null && IsekaiCompat.CoreGate.Ensure();

        // Stable dispatch token (l10n-exempt: never displayed raw — DisplayName renders the mod's own tab label).
        public override string CategoryKey => "Isekai Status";

        public override TabHandlerType Handler => TabHandlerType.RichNavigation;

        public override string DisplayName(InspectTabBase tab) => CompatText.ModText("TabStatus");

        public override string CategoryDisplayName(object obj) => CompatText.ModText("TabStatus");

        public override string CategoryLabel(object obj, string displayName)
        {
            object comp = IsekaiCompat.ComponentOf(obj as Pawn);
            if (comp == null)
                return displayName;
            return displayName + ", " + IsekaiCompat.LevelRankLine(IsekaiCompat.Level(comp));
        }

        public override bool CanExpand(object obj)
        {
            return obj is Pawn pawn && pawn.RaceProps.Humanlike && pawn.Faction == Faction.OfPlayer
                && IsekaiCompat.ComponentOf(pawn) != null;
        }

        public override void BuildChildren(InspectionTreeItem categoryItem, object obj, InspectionMode mode)
        {
            if (categoryItem.Children.Count > 0)
                return;
            Pawn pawn = obj as Pawn;
            object comp = IsekaiCompat.ComponentOf(pawn);
            if (comp == null)
                return;

            InspectNodeFactory.GuardedBuild("IsekaiStatusTabAdapter", delegate
            {
                BuildHeader(categoryItem, comp);
                BuildControls(categoryItem, comp);
                BuildStats(categoryItem, comp);
                BuildClassPassive(categoryItem, pawn, comp);
                BuildWeaponMastery(categoryItem, pawn, comp);
                if (mode == InspectionMode.Full)
                    BuildActions(categoryItem, pawn, comp);
            });
        }

        private static void BuildHeader(InspectionTreeItem parent, object comp)
        {
            InspectNodeFactory.LiveDetailLine(parent, delegate
            {
                string title = IsekaiCompat.CurrentTitleName(comp);
                return string.IsNullOrEmpty(title)
                    ? (string)"RimWorldAccess.Compat.Isekai.NoTitle".Translate()
                    : (string)"RimWorldAccess.Compat.Isekai.TitleLine".Translate(title);
            });
            InspectNodeFactory.LiveDetailLine(parent, delegate
            {
                int level = IsekaiCompat.Level(comp);
                string rank = IsekaiCompat.RankStringForLevel(level);
                return IsekaiCompat.ProgressionTitle(rank) + ", " + IsekaiCompat.LevelRankLine(level);
            });
            InspectNodeFactory.LiveDetailLine(parent, () => IsekaiCompat.XpLine(IsekaiCompat.CurrentXp(comp), IsekaiCompat.XpToNext(comp)));
            InspectNodeFactory.LiveDetailLine(parent,
                () => "RimWorldAccess.Compat.Isekai.PointsAvailable".Translate(IsekaiCompat.AvailablePoints(IsekaiCompat.StatsOf(comp))));
        }

        private static void BuildControls(InspectionTreeItem parent, object comp)
        {
            string autoLabel = CompatText.ModText("Isekai_AutoDistribute");
            InspectionTreeItem toggle = InspectNodeFactory.ItemRow(parent, autoLabel, comp);
            toggle.DescribeElement = delegate
            {
                return new ElementDescription
                {
                    Label = autoLabel,
                    Role = ElementRole.Checkbox,
                    Check = IsekaiCompat.AutoDistribute(comp) ? CheckState.Checked : CheckState.Unchecked,
                    Extras = CompatText.Flatten(CompatText.ModText("Isekai_AutoDistribute_Desc")),
                };
            };
            toggle.OnActivate = delegate
            {
                bool next = !IsekaiCompat.AutoDistribute(comp);
                IsekaiCompat.SetAutoDistribute(comp, next);
                (next ? SoundDefOf.Checkbox_TurnedOn : SoundDefOf.Checkbox_TurnedOff).PlayOneShotOnCamera();
            };

            InspectionTreeItem aura = InspectNodeFactory.ActionRow(parent,
                CompatText.ModArgs("Isekai_AuraDisplay_Button", AuraValueLabel(comp)), comp,
                () => IsekaiWindowCompat.OpenAuraMenu(comp), opensOverlayMenu: true);
            aura.DescribeElement = delegate
            {
                return new ElementDescription
                {
                    Label = "RimWorldAccess.Compat.Isekai.AuraRow".Translate(),
                    Role = ElementRole.ComboBox,
                    Value = AuraValueLabel(comp),
                };
            };
        }

        /// <summary>Mirrors ITab_IsekaiStats.AuraButtonValueLabel: mode word, or the chosen (else primary) constellation.</summary>
        private static string AuraValueLabel(object comp)
        {
            string mode = IsekaiCompat.AuraDisplayModeName(comp);
            if (mode == "DefaultFlame")
                return CompatText.ModText("Isekai_AuraDisplay_DefaultFlame");
            if (mode == "None")
                return CompatText.ModText("Isekai_AuraDisplay_None");
            string tree = IsekaiCompat.AuraConstellation(comp);
            object tracker = IsekaiCompat.PassiveTreeOf(comp);
            if (tracker != null && IsekaiTreeCompat.Gate.Ensure())
            {
                object treeDef = string.IsNullOrEmpty(tree) ? null : IsekaiTreeCompat.TreeDefForClass(tree);
                if (treeDef == null || !IsekaiTreeCompat.HasEnteredTree(tracker, treeDef))
                    tree = IsekaiTreeCompat.AssignedTree(tracker);
            }
            return string.IsNullOrEmpty(tree) ? CompatText.ModText("Isekai_AuraDisplay_Constellation") : tree;
        }

        private static void BuildStats(InspectionTreeItem parent, object comp)
        {
            object stats = IsekaiCompat.StatsOf(comp);
            InspectNodeFactory.Section(parent, CompatText.ModText("Isekai_CoreAttributes"), stats, delegate (InspectionTreeItem section)
            {
                foreach (int ordinal in IsekaiCompat.StatDisplayOrder)
                {
                    int captured = ordinal;
                    InspectNodeFactory.LiveSection(section,
                        () => IsekaiCompat.StatAbbreviation(captured) + " " + IsekaiCompat.StatValue(stats, captured),
                        stats, delegate (InspectionTreeItem statSection)
                        {
                            InspectNodeFactory.DetailLine(statSection, IsekaiCompat.StatName(captured));
                            InspectNodeFactory.DetailLines(statSection, IsekaiCompat.StatDescription(captured));
                            InspectNodeFactory.LiveDetailLine(statSection,
                                () => CompatText.Flatten(IsekaiCompat.StatEffects(captured, IsekaiCompat.StatValue(stats, captured))));
                        });
                }
            });
        }

        private static void BuildClassPassive(InspectionTreeItem parent, Pawn pawn, object comp)
        {
            InspectNodeFactory.Section(parent, CompatText.ModText("Isekai_DetailClassPassive"), comp, delegate (InspectionTreeItem section)
            {
                object tracker = IsekaiCompat.PassiveTreeOf(comp);
                IList gimmicks = tracker != null && IsekaiTreeCompat.Gate.Ensure() ? IsekaiTreeCompat.ActiveGimmicks(tracker) : null;
                if (gimmicks == null || gimmicks.Count == 0)
                {
                    InspectNodeFactory.DetailLine(section, "RimWorldAccess.Compat.Isekai.NoClass".Translate());
                    return;
                }
                foreach (object gimmick in gimmicks)
                {
                    object captured = gimmick;
                    InspectNodeFactory.LiveDetailLine(section, delegate
                    {
                        string name = IsekaiTreeCompat.GimmickName(captured);
                        int tier = IsekaiTreeCompat.GimmickTierFor(tracker, captured);
                        if (tier <= 0)
                            return "RimWorldAccess.Compat.Isekai.GimmickNotUnlocked".Translate(name);
                        string status = IsekaiTreeCompat.GimmickStatus(pawn, comp, captured, tier, out bool _);
                        return "RimWorldAccess.Compat.Isekai.GimmickLine".Translate(name, tier, CompatText.Flatten(status));
                    });
                }
            });
        }

        private static void BuildWeaponMastery(InspectionTreeItem parent, Pawn pawn, object comp)
        {
            if (!IsekaiCompat.WeaponMasteryEnabled || !IsekaiForgeCompat.Gate.Ensure())
                return;
            InspectNodeFactory.LiveDetailLine(parent, delegate
            {
                ThingWithComps weapon = pawn.equipment?.Primary;
                object tracker = IsekaiCompat.WeaponMasteryOf(comp);
                if (weapon == null || tracker == null)
                    return "RimWorldAccess.Compat.Isekai.NoWeaponEquipped".Translate();
                int xp = IsekaiForgeCompat.MasteryXp(tracker, weapon.def.defName);
                object tier = IsekaiForgeCompat.MasteryTier(tracker, weapon.def.defName);
                return "RimWorldAccess.Compat.Isekai.WeaponMasteryLine".Translate(
                    weapon.LabelCapNoCount, IsekaiForgeCompat.MasteryTierLabel(tier), xp);
            });
        }

        private static void BuildActions(InspectionTreeItem parent, Pawn pawn, object comp)
        {
            InspectNodeFactory.ActionRow(parent, CompatText.ModText("Isekai_Mastery"), pawn,
                () => IsekaiWindowCompat.OpenPawnWindow("IsekaiLeveling.UI.Window_Mastery", pawn), opensOverlayMenu: true);
            InspectNodeFactory.ActionRow(parent, CompatText.ModText("Isekai_Stats"), pawn,
                () => IsekaiWindowCompat.OpenPawnWindow("IsekaiLeveling.UI.Window_StatsAttribution", pawn), opensOverlayMenu: true);
            InspectNodeFactory.ActionRow(parent, CompatText.ModText("Isekai_SkillTree"), pawn,
                () => IsekaiWindowCompat.OpenPawnWindow("IsekaiLeveling.UI.Window_SkillTree", pawn), opensOverlayMenu: true);

            // The tab gates the forge hammer on dev mode and its point/level buttons on god mode.
            if (Prefs.DevMode)
            {
                InspectNodeFactory.ActionRow(parent, "RimWorldAccess.Compat.Isekai.OpenCharacterForge".Translate(), pawn,
                    () => IsekaiWindowCompat.OpenPawnWindow("IsekaiLeveling.CharacterForge.Window_CharacterForge", pawn), opensOverlayMenu: true);
            }
            if (Prefs.DevMode && DebugSettings.godMode)
            {
                InspectNodeFactory.ActionRow(parent, "RimWorldAccess.Compat.Isekai.DevAddStatPoints".Translate(), comp,
                    () => IsekaiCompat.AddAvailablePoints(IsekaiCompat.StatsOf(comp), 10));
                InspectNodeFactory.ActionRow(parent, "RimWorldAccess.Compat.Isekai.DevAddLevel".Translate(), comp,
                    () => IsekaiCompat.DevAddLevel(comp, 1));
            }
        }
    }
}
