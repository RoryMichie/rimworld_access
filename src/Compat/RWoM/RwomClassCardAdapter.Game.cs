using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using HarmonyLib;
using RimWorld;
using RimWorldAccess.Shell;
using Verse;
using Verse.Sound;

namespace RimWorldAccess
{
    /// <summary>
    /// Inspection-tree adapter for RimWorld of Magic's class cards
    /// (TorannMagic.ITab_Pawn_Magic / ITab_Pawn_Might), built from the mod's own
    /// decision objects: MagicCardUtility/MightCardUtility trait dispatch and
    /// CustomPowersHandler/CustomSkillHandler gating, mirrored exactly.
    /// One instance serves each of magic/might (constructed with <c>isMight</c>):
    /// structurally parallel flows over distinct types, with might-side
    /// simplifications (no scroll-locked abilities, a fourth global skill list, a
    /// hardcoded tiered-ability defName set). AbilityUser.AbilityDef derives
    /// Verse.Def, so ability defs are cast rather than reflected member-by-member.
    /// </summary>
    internal sealed class RwomClassCardAdapter : InspectNodeAdapter
    {
        private static readonly string[] MagicGlobalSkillFieldNames =
        {
            "MagicPowerSkill_global_regen", "MagicPowerSkill_global_eff", "MagicPowerSkill_global_spirit",
        };

        // Might carries a fourth global skill list (endurance) that magic lacks.
        private static readonly string[] MightGlobalSkillFieldNames =
        {
            "MightPowerSkill_global_refresh", "MightPowerSkill_global_seff",
            "MightPowerSkill_global_strength", "MightPowerSkill_global_endurance",
        };

        // MightCardUtility hardcodes this defName set inline where magic calls
        // TM_Calc.IsIconAbility_02/03. Its two source lists differ only in the _III
        // tier, which level &lt; maxLevel already excludes, so one set serves both.
        private static readonly HashSet<string> MightTieredDefNames = new HashSet<string>
        {
            "TM_Grapple", "TM_Grapple_I", "TM_Grapple_II", "TM_Grapple_III",
            "TM_DisablingShot", "TM_DisablingShot_I", "TM_DisablingShot_II", "TM_DisablingShot_III",
            "TM_PhaseStrike", "TM_PhaseStrike_I", "TM_PhaseStrike_II", "TM_PhaseStrike_III",
            "TM_ArrowStorm", "TM_ArrowStorm_I", "TM_ArrowStorm_II", "TM_ArrowStorm_III",
            "TM_PsionicBlast", "TM_PsionicBlast_I", "TM_PsionicBlast_II", "TM_PsionicBlast_III",
            "TM_GraveBlade", "TM_GraveBlade_I", "TM_GraveBlade_II", "TM_GraveBlade_III",
            "TM_Spite", "TM_Spite_I", "TM_Spite_II", "TM_Spite_III",
            "TM_Transpose", "TM_Transpose_I", "TM_Transpose_II", "TM_Transpose_III",
            "TM_StayAlert", "TM_StayAlert_I", "TM_StayAlert_II", "TM_StayAlert_III",
            "TM_MoveOut", "TM_MoveOut_I", "TM_MoveOut_II", "TM_MoveOut_III",
            "TM_HoldTheLine", "TM_HoldTheLine_I", "TM_HoldTheLine_II", "TM_HoldTheLine_III",
        };

        private enum SpecialKind
        {
            None,
            Technomancer,
            ChaosMage,
            SuperSoldier,
        }

        private sealed class TraitRule
        {
            public string TraitDefName;
            public string FieldName;
            public string RemoveDefName;
            public SpecialKind Special;
        }

        // Mirrors MagicCardUtility.DrawMagicCard's trait dispatch. InnerFire is a
        // standalone `if` ahead of this else-if chain (see ResolveAbilityDefSet).
        private static readonly TraitRule[] MagicTraitRules =
        {
            new TraitRule { TraitDefName = "HeartOfFrost", FieldName = "MagicPowersHoF" },
            new TraitRule { TraitDefName = "StormBorn", FieldName = "MagicPowersSB" },
            new TraitRule { TraitDefName = "Arcanist", FieldName = "MagicPowersA" },
            new TraitRule { TraitDefName = "Paladin", FieldName = "MagicPowersP" },
            new TraitRule { TraitDefName = "Summoner", FieldName = "MagicPowersS" },
            new TraitRule { TraitDefName = "Druid", FieldName = "MagicPowersD" },
            new TraitRule { TraitDefName = "Necromancer", FieldName = "MagicPowersN", RemoveDefName = "TM_DeathBolt" },
            new TraitRule { TraitDefName = "Lich", FieldName = "MagicPowersN", RemoveDefName = "TM_LichForm" },
            new TraitRule { TraitDefName = "Priest", FieldName = "MagicPowersPR" },
            new TraitRule { TraitDefName = "TM_Bard", FieldName = "MagicPowersB" },
            new TraitRule { TraitDefName = "Succubus", FieldName = "MagicPowersSD" },
            new TraitRule { TraitDefName = "Warlock", FieldName = "MagicPowersWD" },
            new TraitRule { TraitDefName = "Geomancer", FieldName = "MagicPowersG" },
            new TraitRule { TraitDefName = "Technomancer", Special = SpecialKind.Technomancer },
            new TraitRule { TraitDefName = "BloodMage", FieldName = "MagicPowersBM" },
            new TraitRule { TraitDefName = "Enchanter", FieldName = "MagicPowersE" },
            new TraitRule { TraitDefName = "Chronomancer", FieldName = "MagicPowersC" },
            new TraitRule { TraitDefName = "ChaosMage", Special = SpecialKind.ChaosMage },
            new TraitRule { TraitDefName = "TM_Wanderer", FieldName = "MagicPowersW" },
        };

        // Mirrors MightCardUtility.DrawMightCard's trait dispatch (a plain else-if
        // chain; no standalone-if quirk like magic's InnerFire).
        private static readonly TraitRule[] MightTraitRules =
        {
            new TraitRule { TraitDefName = "Gladiator", FieldName = "MightPowersG" },
            new TraitRule { TraitDefName = "TM_Sniper", FieldName = "MightPowersS" },
            new TraitRule { TraitDefName = "Bladedancer", FieldName = "MightPowersB" },
            new TraitRule { TraitDefName = "Ranger", FieldName = "MightPowersR" },
            new TraitRule { TraitDefName = "Faceless", FieldName = "MightPowersF" },
            new TraitRule { TraitDefName = "TM_Psionic", FieldName = "MightPowersP" },
            new TraitRule { TraitDefName = "DeathKnight", FieldName = "MightPowersDK" },
            new TraitRule { TraitDefName = "TM_Monk", FieldName = "MightPowersM" },
            new TraitRule { TraitDefName = "TM_Wayfarer", FieldName = "MightPowersW" },
            new TraitRule { TraitDefName = "TM_Commander", FieldName = "MightPowersC" },
            new TraitRule { TraitDefName = "TM_SuperSoldier", Special = SpecialKind.SuperSoldier },
        };

        private readonly bool isMight;

        private readonly Type tabType;
        private readonly Type compType;   // CompAbilityUserMagic / CompAbilityUserMight
        private readonly Type dataType;   // MagicData / MightData
        private readonly Type powerType;  // MagicPower / MightPower
        private readonly Type skillType;  // MagicPowerSkill / MightPowerSkill
        private readonly Type needType;   // Need_Mana / Need_Stamina
        private readonly Type calcType;   // TorannMagic.TM_Calc (magic only)

        private readonly PropertyInfo dataProperty;                  // comp.MagicData / comp.MightData
        private readonly PropertyInfo userXPProperty;                // comp.MagicUserXP / MightUserXP
        private readonly PropertyInfo userXPTillNextLevelProperty;   // comp.MagicUserXPTillNextLevel / MightUserXPTillNextLevel
        private readonly PropertyInfo resourceNeedProperty;          // comp.Mana / comp.Stamina
        private readonly FieldInfo customClassField;                 // CompAbilityUserTMBase.customClass
        private readonly PropertyInfo advancedClassesProperty;       // CompAbilityUserTMBase.AdvancedClasses (a property; lazily inits its backing list)
        private readonly FieldInfo chaosPowersField;                 // CompAbilityUserMagic.chaosPowers (magic only)
        private FieldInfo customClassAbilitiesField;                 // TM_CustomClass.classMageAbilities / classFighterAbilities, resolved lazily

        private readonly PropertyInfo levelProperty;                 // data.MagicUserLevel / MightUserLevel
        private readonly PropertyInfo abilityPointsProperty;         // data.MagicAbilityPoints / MightAbilityPoints
        private readonly PropertyInfo allPowersWithSkillsProperty;   // data.AllMagicPowersWithSkills / AllMightPowersWithSkills
        private readonly MethodInfo getSkillPowerMethod;
        private readonly MethodInfo getSkillEfficiencyMethod;
        private readonly MethodInfo getSkillVersatilityMethod;
        private readonly List<MemberInfo> globalSkillListFields = new List<MemberInfo>();
        private readonly MemberInfo allPowersForChaosMageField;      // data.AllMagicPowersForChaosMage (magic only; a property in the shipped DLL)
        private readonly Dictionary<string, MemberInfo> dataFieldCache = new Dictionary<string, MemberInfo>();

        private readonly FieldInfo tmAbilityDefsField;    // power.TMabilityDefs : List<AbilityDef>
        private readonly PropertyInfo abilityDefProperty; // power.abilityDef (side effect: refreshes maxLevel)
        private readonly PropertyInfo nextLevelAbilityDefProperty;
        private readonly FieldInfo levelField;
        private readonly FieldInfo maxLevelField;
        private readonly FieldInfo learnedField;
        private readonly FieldInfo learnCostField;
        private readonly FieldInfo costToLevelField;
        private readonly FieldInfo requiresScrollField; // magic only; null for might (MightPower has no such field)
        private readonly FieldInfo autocastingField;    // TMDefs.TM_Autocast; only its null-ness matters here
        private readonly FieldInfo autocastField;

        private readonly FieldInfo skillLabelField;
        private readonly FieldInfo skillDescField;
        private readonly FieldInfo skillLevelField;
        private readonly FieldInfo skillLevelMaxField;
        private readonly FieldInfo skillCostToLevelField;

        private readonly FieldInfo needLastGainPctField;
        private readonly FieldInfo needBaseManaGainField;
        private readonly FieldInfo needModifiedManaGainField;
        private readonly FieldInfo needDrainManaSurgeField;
        private readonly FieldInfo needDrainSyrriumField;
        private readonly FieldInfo needDrainEnergyHDField;
        private readonly FieldInfo needDrainManaWeaknessField;
        private readonly FieldInfo needDrainMinionField;
        private readonly FieldInfo needDrainSpritesField;
        private readonly FieldInfo needDrainUndeadField;
        private readonly FieldInfo needDrainManaDrainField;
        private readonly FieldInfo needDrainManaSicknessField;
        private readonly FieldInfo needParacyteCountReductionField;
        private readonly FieldInfo needBaseStaminaGainField;
        private readonly FieldInfo needModifiedStaminaGainField;

        private readonly MethodInfo isIconAbility02Method; // TM_Calc.IsIconAbility_02 (description-composition condition)
        private readonly MethodInfo isIconAbility03Method; // TM_Calc.IsIconAbility_03 (tiered-state condition)
        private readonly PropertyInfo chaosPowersAbilityProperty; // TM_ChaosPowers.Ability

        private readonly Type tmAbilityDefType;              // TorannMagic.TMAbilityDef
        private readonly Type abilityUserAbilityDefType;     // AbilityUser.AbilityDef (TMAbilityDef's base)
        private readonly PropertyInfo autoCastProperty;      // power.AutoCast (vehicle B setter; the mod's own 5-tick debounce)
        private readonly FieldInfo mainVerbField;             // AbilityUser.AbilityDef.MainVerb -> VerbProperties_Ability
        private readonly FieldInfo isViolentField;            // VerbProperties_Ability.isViolent (its OWN field, NOT vanilla VerbProperties.violent)
        private readonly FieldInfo shouldInitializeField;    // TMAbilityDef.shouldInitialize (magic learn effect only)
        private readonly FieldInfo childAbilitiesField;      // TMAbilityDef.childAbilities (magic learn effect only)
        private readonly MethodInfo addPawnAbilityMethod;    // CompAbilityUser.AddPawnAbility(AbilityDef, bool, float)
        private readonly MethodInfo levelUpMethod;           // comp.LevelUp(bool) — god-mode only
        private readonly MethodInfo resetSkillsMethod;       // comp.ResetSkills() — god-mode only
        private readonly MethodInfo levelUpPowerMethod;      // comp.LevelUpPower(MagicPower/MightPower) — vehicle B, does not deduct points
        private readonly MethodInfo fixPowersMethod;         // comp.FixPowers() — might only, called after LevelUpPower

        // Cross-mode reference for the "leveling this skill grants the other mode's
        // powers" side effects (TM_Cantrips_eff, TM_FieldTraining_eff).
        private readonly Type otherCompType;
        private readonly MethodInfo resolveOtherCompMethod;

        // Might-only: the sub-abilities TM_SuperSoldier's Learn effect grants beside
        // its weapon spec, each gated by a comp flag rather than power.learned.
        private readonly FieldInfo skillPistolWhipField;
        private readonly FieldInfo skillSuppressingFireField;
        private readonly FieldInfo skillMk203GLField;
        private readonly FieldInfo skillBuckshotField;
        private readonly FieldInfo skillBreachingChargeField;

        private readonly Dictionary<string, TraitDef> traitCache = new Dictionary<string, TraitDef>();
        private readonly bool ready;
        private InspectTabBase sharedTab;

        public override bool Ready => ready;

        private RwomClassCardAdapter(bool isMight)
        {
            this.isMight = isMight;

            var surface = new ReflectionSurface($"RwomClassCardAdapter ({(isMight ? "Might" : "Magic")})");
            tabType = surface.Type(isMight ? "TorannMagic.ITab_Pawn_Might" : "TorannMagic.ITab_Pawn_Magic");
            compType = surface.Type(isMight ? "TorannMagic.CompAbilityUserMight" : "TorannMagic.CompAbilityUserMagic");
            dataType = surface.Type(isMight ? "TorannMagic.MightData" : "TorannMagic.MagicData");
            powerType = surface.Type(isMight ? "TorannMagic.MightPower" : "TorannMagic.MagicPower");
            skillType = surface.Type(isMight ? "TorannMagic.MightPowerSkill" : "TorannMagic.MagicPowerSkill");
            needType = surface.Type(isMight ? "TorannMagic.Need_Stamina" : "TorannMagic.Need_Mana");
            tmAbilityDefType = surface.Type("TorannMagic.TMAbilityDef");
            abilityUserAbilityDefType = surface.Type("AbilityUser.AbilityDef");
            Type verbPropertiesAbilityType = surface.Type("AbilityUser.VerbProperties_Ability");

            dataProperty = surface.Property(compType, isMight ? "MightData" : "MagicData");
            userXPProperty = surface.Property(compType, isMight ? "MightUserXP" : "MagicUserXP");
            userXPTillNextLevelProperty = surface.Property(compType, isMight ? "MightUserXPTillNextLevel" : "MagicUserXPTillNextLevel");
            resourceNeedProperty = surface.Property(compType, isMight ? "Stamina" : "Mana");
            customClassField = surface.Field(compType, "customClass");
            advancedClassesProperty = surface.Property(compType, "AdvancedClasses");
            levelUpMethod = surface.Method(compType, "LevelUp", new[] { typeof(bool) });
            resetSkillsMethod = surface.Method(compType, "ResetSkills", Type.EmptyTypes);
            levelUpPowerMethod = powerType != null
                ? surface.Method(compType, "LevelUpPower", new[] { powerType })
                : null;

            levelProperty = surface.Property(dataType, isMight ? "MightUserLevel" : "MagicUserLevel");
            abilityPointsProperty = surface.Property(dataType, isMight ? "MightAbilityPoints" : "MagicAbilityPoints");
            allPowersWithSkillsProperty = surface.Property(dataType, isMight ? "AllMightPowersWithSkills" : "AllMagicPowersWithSkills");

            if (tmAbilityDefType != null)
            {
                getSkillPowerMethod = surface.Method(dataType, "GetSkill_Power", new[] { tmAbilityDefType });
                getSkillEfficiencyMethod = surface.Method(dataType, "GetSkill_Efficiency", new[] { tmAbilityDefType });
                getSkillVersatilityMethod = surface.Method(dataType, "GetSkill_Versatility", new[] { tmAbilityDefType });
            }
            if (abilityUserAbilityDefType != null)
            {
                mainVerbField = surface.Field(abilityUserAbilityDefType, "MainVerb");
                addPawnAbilityMethod = surface.Method(compType, "AddPawnAbility",
                    new[] { abilityUserAbilityDefType, typeof(bool), typeof(float) });
            }
            isViolentField = surface.Field(verbPropertiesAbilityType, "isViolent");
            autoCastProperty = surface.Property(powerType, "AutoCast");

            if (isMight)
            {
                fixPowersMethod = surface.Method(compType, "FixPowers", Type.EmptyTypes);
                skillPistolWhipField = surface.Field(compType, "skill_PistolWhip");
                skillSuppressingFireField = surface.Field(compType, "skill_SuppressingFire");
                skillMk203GLField = surface.Field(compType, "skill_Mk203GL");
                skillBuckshotField = surface.Field(compType, "skill_Buckshot");
                skillBreachingChargeField = surface.Field(compType, "skill_BreachingCharge");
            }
            else
            {
                shouldInitializeField = surface.Field(tmAbilityDefType, "shouldInitialize");
                childAbilitiesField = surface.Field(tmAbilityDefType, "childAbilities");
            }

            otherCompType = surface.Type(isMight ? "TorannMagic.CompAbilityUserMagic" : "TorannMagic.CompAbilityUserMight");
            // TM_PawnTracker is nested under TorannMagic.Utils in the shipped 1.6 DLL
            // and un-nested in older builds, hence the two-name lookup.
            Type pawnTrackerType = surface.Supplied("TM_PawnTracker",
                AccessTools.TypeByName("TorannMagic.Utils.TM_PawnTracker")
                    ?? AccessTools.TypeByName("TorannMagic.TM_PawnTracker"));
            resolveOtherCompMethod = otherCompType != null
                ? surface.Method(pawnTrackerType, isMight ? "ResolveMagicComp" : "ResolveMightComp", new[] { otherCompType })
                : null;

            foreach (string fieldName in isMight ? MightGlobalSkillFieldNames : MagicGlobalSkillFieldNames)
            {
                MemberInfo mi = ReflectionSurface.TryFieldOrProperty(dataType, fieldName);
                if (mi != null)
                    globalSkillListFields.Add(mi);
            }

            tmAbilityDefsField = surface.Field(powerType, "TMabilityDefs");
            abilityDefProperty = surface.Property(powerType, "abilityDef");
            nextLevelAbilityDefProperty = surface.Property(powerType, "nextLevelAbilityDef");
            levelField = surface.Field(powerType, "level");
            maxLevelField = surface.Field(powerType, "maxLevel");
            learnedField = surface.Field(powerType, "learned");
            learnCostField = surface.Field(powerType, "learnCost");
            costToLevelField = surface.Field(powerType, "costToLevel");
            autocastingField = surface.Field(powerType, "autocasting");
            autocastField = surface.Field(powerType, "autocast");
            // Magic-only and version-optional: absent means no scroll requirement.
            requiresScrollField = isMight || powerType == null ? null : AccessTools.Field(powerType, "requiresScroll");

            skillLabelField = surface.Field(skillType, "label");
            skillDescField = surface.Field(skillType, "desc");
            skillLevelField = surface.Field(skillType, "level");
            skillLevelMaxField = surface.Field(skillType, "levelMax");
            skillCostToLevelField = surface.Field(skillType, "costToLevel");

            needLastGainPctField = surface.Field(needType, "lastGainPct");

            if (isMight)
            {
                needBaseStaminaGainField = surface.Field(needType, "baseStaminaGain");
                needModifiedStaminaGainField = surface.Field(needType, "modifiedStaminaGain");
            }
            else
            {
                needBaseManaGainField = surface.Field(needType, "baseManaGain");
                needModifiedManaGainField = surface.Field(needType, "modifiedManaGain");
                needDrainManaSurgeField = surface.Field(needType, "drainManaSurge");
                needDrainSyrriumField = surface.Field(needType, "drainSyrrium");
                needDrainEnergyHDField = surface.Field(needType, "drainEnergyHD");
                needDrainManaWeaknessField = surface.Field(needType, "drainManaWeakness");
                needDrainMinionField = surface.Field(needType, "drainMinion");
                needDrainSpritesField = surface.Field(needType, "drainSprites");
                needDrainUndeadField = surface.Field(needType, "drainUndead");
                needDrainManaDrainField = surface.Field(needType, "drainManaDrain");
                needDrainManaSicknessField = surface.Field(needType, "drainManaSickness");
                needParacyteCountReductionField = surface.Field(needType, "paracyteCountReduction");

                calcType = surface.Type("TorannMagic.TM_Calc");
                if (abilityUserAbilityDefType != null)
                {
                    isIconAbility02Method = surface.Method(calcType, "IsIconAbility_02", new[] { abilityUserAbilityDefType });
                    isIconAbility03Method = surface.Method(calcType, "IsIconAbility_03", new[] { abilityUserAbilityDefType });
                }

                // Chaos-mage extras: absent outside that class, so these stay outside
                // the surface and only silence the chaos power list.
                chaosPowersField = compType != null ? AccessTools.Field(compType, "chaosPowers") : null;
                allPowersForChaosMageField = ReflectionSurface.TryFieldOrProperty(dataType, "AllMagicPowersForChaosMage");
                Type chaosPowersItemType = AccessTools.TypeByName("TorannMagic.TM_ChaosPowers");
                chaosPowersAbilityProperty = chaosPowersItemType != null
                    ? AccessTools.Property(chaosPowersItemType, "Ability")
                    : null;
            }

            ready = surface.Ready && globalSkillListFields.Count > 0;
            if (ready)
            {
                sharedTab = InspectTabManager.GetSharedInstance(tabType);
            }
            else if (surface.Ready)
            {
                ModLogger.Error($"RwomClassCardAdapter ({(isMight ? "Might" : "Magic")}): none of the global skill " +
                    "list fields resolved; declining the class card adapter.");
            }
        }

        /// <summary>
        /// Registers an adapter for each tab type; either may be absent or broken
        /// independently. Never breaks startup: a missing type declines silently,
        /// and the constructor already logs a resolution failure.
        /// </summary>
        public static void TryRegister()
        {
            TryRegisterOne(false);
            TryRegisterOne(true);
        }

        private static void TryRegisterOne(bool isMight)
        {
            string tabTypeName = isMight ? "TorannMagic.ITab_Pawn_Might" : "TorannMagic.ITab_Pawn_Magic";
            string logName = $"RimWorld of Magic {(isMight ? "might" : "magic")} class card compat";
            CompatRegistration.TabAdapter(tabTypeName, t => new RwomClassCardAdapter(isMight), logName);
        }

        // Stable English dispatch token, never displayed raw (l10n-exempt).
        public override string CategoryKey => isMight ? "RWoM Might Class" : "RWoM Magic Class";

        public override TabHandlerType Handler => TabHandlerType.RichNavigation;

        public override string DisplayName(InspectTabBase tab) => ResolveTabLabel(tab);

        public override string CategoryDisplayName(object obj) => ResolveTabLabel(sharedTab);

        // The tab's IsVisible dereferences SelPawn.story with no null guard, so this
        // adapter never calls it; BuildChildren's own null checks are the only gate.

        public override void BuildChildren(InspectionTreeItem categoryItem, object obj, InspectionMode mode)
        {
            if (categoryItem.Children.Count > 0)
                return;

            Pawn pawn = InspectionTreeBuilder.GetPawnFromThing(obj);
            if (pawn?.story == null)
                return;

            object comp = FindComp(pawn);
            if (comp == null)
                return;

            object data = dataProperty.GetValue(comp);
            if (data == null)
                return;

            InspectNodeFactory.GuardedBuild("RwomClassCardAdapter", () => BuildHeaderSection(categoryItem, comp, data, pawn, mode));
            InspectNodeFactory.GuardedBuild("RwomClassCardAdapter", () => BuildGlobalSkillsSection(categoryItem, comp, data, pawn, mode));
            InspectNodeFactory.GuardedBuild("RwomClassCardAdapter", () => BuildAbilitySections(categoryItem, comp, data, pawn, mode));
        }

        /// <summary>
        /// Rebuilds the whole category after a mutation: one spend or learn changes
        /// the header readout and every other row's affordability. Routed through
        /// the framework so the extender and parity-capture passes survive.
        /// </summary>
        private void RebuildCategory(InspectionTreeItem categoryItem, object obj, InspectionMode mode)
        {
            InspectionTreeBuilder.RebuildAdapterCategory(categoryItem, obj, mode, this, sharedTab);
        }

        private static string ResolveTabLabel(InspectTabBase tab)
        {
            if (tab != null && !string.IsNullOrEmpty(tab.labelKey))
            {
                try
                {
                    string translated = tab.labelKey.Translate().ToString();
                    if (!string.IsNullOrEmpty(translated) && translated != tab.labelKey)
                        return translated;
                }
                catch
                {
                    // Translation failed; fall through to the type-name fallback.
                }
            }
            return tab?.GetType().Name ?? "RimWorld of Magic"; // l10n-exempt: last-resort fallback only
        }

        private object FindComp(Pawn pawn)
        {
            if (pawn.AllComps == null)
                return null;
            foreach (ThingComp comp in pawn.AllComps)
            {
                if (compType.IsInstanceOfType(comp))
                    return comp;
            }
            return null;
        }

        // ---- Section 1: class info header ----
        // Mirrors MagicCardUtility/MightCardUtility InfoPane, including the god-mode
        // buttons behind the same DebugSettings.godMode flag the card checks.

        private void BuildHeaderSection(InspectionTreeItem categoryItem, object comp, object data, Pawn pawn, InspectionMode mode)
        {
            InspectNodeFactory.Section(categoryItem, "RimWorldAccess.Compat.Rwom.ClassInfoSection".Translate(), comp,
                secItem => BuildHeaderChildren(secItem, categoryItem, comp, data, pawn, mode));
        }

        private void BuildHeaderChildren(InspectionTreeItem secItem, InspectionTreeItem categoryItem, object comp, object data, Pawn pawn, InspectionMode mode)
        {
            int level = (int)levelProperty.GetValue(data);
            int abilityPoints = (int)abilityPointsProperty.GetValue(data);
            int xp = (int)userXPProperty.GetValue(comp);
            int xpTillNext = (int)userXPTillNextLevelProperty.GetValue(comp);

            InspectNodeFactory.DetailLine(secItem, "RimWorldAccess.Compat.Rwom.LevelRow".Translate(level, xp, xpTillNext));
            InspectNodeFactory.DetailLine(secItem, "RimWorldAccess.Compat.Rwom.AbilityPointsRow".Translate(abilityPoints));

            string resourceRow = ComposeResourceRow(comp);
            if (resourceRow != null)
                InspectNodeFactory.DetailLine(secItem, resourceRow);

            if (mode != InspectionMode.ReadOnly && DebugSettings.godMode)
            {
                InspectNodeFactory.ActionRow(secItem, "RimWorldAccess.Compat.Rwom.GodModeLevelUpAction".Translate(), comp,
                    () => OnGodModeLevelUp(categoryItem, comp, pawn, mode));
                InspectNodeFactory.ActionRow(secItem, "RimWorldAccess.Compat.Rwom.GodModeResetAction".Translate(), comp,
                    () => OnGodModeReset(categoryItem, comp, pawn, mode));
            }
        }

        // Vehicle B: comp.LevelUp(true), the call the god-mode "+" button makes.
        private void OnGodModeLevelUp(InspectionTreeItem categoryItem, object comp, Pawn pawn, InspectionMode mode)
        {
            try
            {
                levelUpMethod.Invoke(comp, new object[] { true });
                SoundDefOf.Click.PlayOneShotOnCamera(null);
                int newLevel = (int)levelProperty.GetValue(dataProperty.GetValue(comp));
                RebuildCategory(categoryItem, pawn, mode);
                TolkHelper.Speak("RimWorldAccess.Compat.Rwom.ClassLeveledUp".Loc(newLevel.ToString()));
            }
            catch (Exception ex)
            {
                ModLogger.Error($"RwomClassCardAdapter god-mode level-up failed: {ex.Message}");
                TolkHelper.Speak("RimWorldAccess.Compat.Rwom.ActionFailed".Loc());
            }
        }

        // Vehicle B: comp.ResetSkills(), the call the god-mode "Reset Class" button
        // makes. Runs directly; the card has no confirmation step to ride.
        private void OnGodModeReset(InspectionTreeItem categoryItem, object comp, Pawn pawn, InspectionMode mode)
        {
            try
            {
                resetSkillsMethod.Invoke(comp, null);
                SoundDefOf.Click.PlayOneShotOnCamera(null);
                RebuildCategory(categoryItem, pawn, mode);
                TolkHelper.Speak("RimWorldAccess.Compat.Rwom.ClassReset".Loc());
            }
            catch (Exception ex)
            {
                ModLogger.Error($"RwomClassCardAdapter god-mode reset failed: {ex.Message}");
                TolkHelper.Speak("RimWorldAccess.Compat.Rwom.ActionFailed".Loc());
            }
        }

        // Mirrors InfoPane's mana/stamina label plus its full drain/gain tooltip,
        // folding the sighted label and its hover text into one row.
        private string ComposeResourceRow(object comp)
        {
            object need = resourceNeedProperty.GetValue(comp);
            if (need == null)
                return null;

            float lastGainPct = (float)needLastGainPctField.GetValue(need);
            var sb = new StringBuilder();

            if (isMight)
            {
                float baseGain = (float)needBaseStaminaGainField.GetValue(need);
                float modGain = (float)needModifiedStaminaGainField.GetValue(need);

                sb.Append("RimWorldAccess.Compat.Rwom.StaminaGainRow".Translate(F(lastGainPct * 200f)));
                sb.Append(" ").Append("RimWorldAccess.Compat.Rwom.StaminaBaseGain".Translate(F(200f * baseGain)));
                sb.Append(" ").Append("RimWorldAccess.Compat.Rwom.StaminaRegenAdjustment".Translate(F(200f * modGain)));
            }
            else
            {
                float baseGain = (float)needBaseManaGainField.GetValue(need);
                float modGain = (float)needModifiedManaGainField.GetValue(need);
                float surge = (float)needDrainManaSurgeField.GetValue(need);
                float syrrium = (float)needDrainSyrriumField.GetValue(need);
                float energyHD = (float)needDrainEnergyHDField.GetValue(need);
                float weakness = (float)needDrainManaWeaknessField.GetValue(need);
                float minion = (float)needDrainMinionField.GetValue(need);
                float sprites = (float)needDrainSpritesField.GetValue(need);
                float undead = (float)needDrainUndeadField.GetValue(need);
                float manaDrain = (float)needDrainManaDrainField.GetValue(need);
                float sickness = (float)needDrainManaSicknessField.GetValue(need);
                float paracyte = (float)needParacyteCountReductionField.GetValue(need);

                float modifiedGainSum = 200f * (baseGain + modGain + syrrium + surge);
                float netUpkeep = -200f * (paracyte + sickness + manaDrain + undead + sprites + minion + weakness);

                sb.Append("RimWorldAccess.Compat.Rwom.ManaGainRow".Translate(F(lastGainPct * 200f)));
                sb.Append(" ").Append("RimWorldAccess.Compat.Rwom.ManaBaseGain".Translate(F(200f * baseGain)));
                sb.Append(" ").Append("RimWorldAccess.Compat.Rwom.ManaBaseRegen".Translate(F(200f * modGain)));
                sb.Append(" ").Append("RimWorldAccess.Compat.Rwom.ManaSurge".Translate(F(200f * surge)));
                sb.Append(" ").Append("RimWorldAccess.Compat.Rwom.ManaSyrrium".Translate(F(200f * syrrium)));
                sb.Append(" ").Append("RimWorldAccess.Compat.Rwom.ManaModifiedGain".Translate(F(modifiedGainSum)));
                sb.Append(" ").Append("RimWorldAccess.Compat.Rwom.ManaRegenBonus".Translate(F(200f * energyHD)));
                sb.Append(" ").Append("RimWorldAccess.Compat.Rwom.ManaWeakness".Translate(F(200f * weakness)));
                sb.Append(" ").Append("RimWorldAccess.Compat.Rwom.ManaMinionCost".Translate(F(200f * minion)));
                sb.Append(" ").Append("RimWorldAccess.Compat.Rwom.ManaSpriteCost".Translate(F(200f * sprites)));
                sb.Append(" ").Append("RimWorldAccess.Compat.Rwom.ManaUndeadCost".Translate(F(200f * undead)));
                sb.Append(" ").Append("RimWorldAccess.Compat.Rwom.ManaDrain".Translate(F(200f * manaDrain)));
                sb.Append(" ").Append("RimWorldAccess.Compat.Rwom.ManaSickness".Translate(F(200f * sickness)));
                sb.Append(" ").Append("RimWorldAccess.Compat.Rwom.ManaParacyte".Translate(F(200f * paracyte)));
                sb.Append(" ").Append("RimWorldAccess.Compat.Rwom.ManaNet".Translate(F(netUpkeep)));
            }

            return sb.ToString();
        }

        private static string F(float value) => value.ToString("0.000");

        // ---- Section 2: global skills ----
        // Mirrors DrawLevelBar: one row per global skill list. Unlike per-ability
        // skills there is no learned/costToLevel gate; each level-up costs one point.

        private void BuildGlobalSkillsSection(InspectionTreeItem categoryItem, object comp, object data, Pawn pawn, InspectionMode mode)
        {
            InspectNodeFactory.Section(categoryItem, "RimWorldAccess.Compat.Rwom.GlobalSkillsSection".Translate(), data,
                secItem => BuildGlobalSkillsChildren(secItem, categoryItem, comp, data, pawn, mode));
        }

        private void BuildGlobalSkillsChildren(InspectionTreeItem secItem, InspectionTreeItem categoryItem, object comp, object data, Pawn pawn, InspectionMode mode)
        {
            int abilityPoints = (int)abilityPointsProperty.GetValue(data);
            bool writable = mode != InspectionMode.ReadOnly;
            foreach (MemberInfo listField in globalSkillListFields)
            {
                IList list = ReflectionSurface.ValueOf(listField, data) as IList;
                if (list == null)
                    continue;
                foreach (object skill in list)
                {
                    string rowText = ComposeSkillRow(skill, showCost: false,
                        CanSpendGlobalSkillPoint(skill, abilityPoints));
                    if (writable && CanSpendGlobalSkillPoint(skill, abilityPoints))
                    {
                        object capturedSkill = skill;
                        InspectNodeFactory.ActionRow(secItem, rowText, skill,
                            () => OnSpendGlobalSkillPoint(categoryItem, data, capturedSkill, pawn, mode));
                    }
                    else
                    {
                        InspectNodeFactory.DetailLine(secItem, rowText);
                    }
                }
            }
        }

        // Mirrors DrawLevelBar's gate: level &lt; levelMax and any points at all.
        private bool CanSpendGlobalSkillPoint(object skill, int abilityPoints)
        {
            int level = (int)skillLevelField.GetValue(skill);
            int levelMax = (int)skillLevelMaxField.GetValue(skill);
            return level < levelMax && abilityPoints > 0;
        }

        // Mirrors CustomSkillHandler's gate: learned && level &lt; levelMax &&
        // points &gt; 0 && costToLevel &lt;= points. Might's CustomSkillHandler has
        // no learned check at all, hence the mode-dependent learnedGate.
        private bool CanSpendAbilitySkillPoint(object skill, int abilityPoints, bool abilityLearned)
        {
            bool learnedGate = isMight || abilityLearned;
            if (!learnedGate)
                return false;
            int level = (int)skillLevelField.GetValue(skill);
            int levelMax = (int)skillLevelMaxField.GetValue(skill);
            int costToLevel = (int)skillCostToLevelField.GetValue(skill);
            return level < levelMax && abilityPoints > 0 && costToLevel <= abilityPoints;
        }

        /// <summary>
        /// One skill row: label, level of levelMax, description, and — when the
        /// card's own "+" would be enabled — the point-available phrase. The caller
        /// computes <paramref name="pointAvailable"/>, since the same answer decides
        /// whether the row becomes an Action.
        /// </summary>
        private string ComposeSkillRow(object skill, bool showCost, bool pointAvailable)
        {
            string label = ((string)skillLabelField.GetValue(skill)).Translate();
            int level = (int)skillLevelField.GetValue(skill);
            int levelMax = (int)skillLevelMaxField.GetValue(skill);
            string desc = (string)skillDescField.GetValue(skill);

            var sb = new StringBuilder();
            sb.Append("RimWorldAccess.Compat.Rwom.SkillProgressRow".Translate(label, level, levelMax));
            if (!string.IsNullOrEmpty(desc))
            {
                // Skill descs carry raw newlines; ToSentences also supplies the
                // terminal punctuation.
                sb.Append(" ").Append(SpeechFlatten.ToSentences(desc.Translate()));
            }

            if (pointAvailable)
            {
                if (showCost)
                {
                    int costToLevel = (int)skillCostToLevelField.GetValue(skill);
                    sb.Append(" ").Append("RimWorldAccess.Compat.Rwom.AbilitySkillPointCost".Translate(costToLevel));
                }
                else
                {
                    sb.Append(" ").Append("RimWorldAccess.Compat.Rwom.PointAvailableToSpend".Translate());
                }
            }

            return sb.ToString();
        }

        // MUTATION-C: mirrors MagicCardUtility.DrawLevelBar's global skill "+"
        // handlers (:641-647/:668-673/:695-700) / MightCardUtility's equivalents
        // (:397-402/:424-429/:451-456/:478-483) — bare field mutation, no
        // Try*/Can* twin exists on either MagicData/MightData or the skill type.
        private void OnSpendGlobalSkillPoint(InspectionTreeItem categoryItem, object data, object skill, Pawn pawn, InspectionMode mode)
        {
            try
            {
                int abilityPoints = (int)abilityPointsProperty.GetValue(data);
                if (!CanSpendGlobalSkillPoint(skill, abilityPoints))
                {
                    TolkHelper.Speak("RimWorldAccess.Compat.Rwom.NotEnoughPoints".Loc());
                    return;
                }

                int level = (int)skillLevelField.GetValue(skill);
                int levelMax = (int)skillLevelMaxField.GetValue(skill);
                skillLevelField.SetValue(skill, level + 1);
                abilityPointsProperty.SetValue(data, abilityPoints - 1);

                SoundDefOf.Click.PlayOneShotOnCamera(null);
                string label = ((string)skillLabelField.GetValue(skill)).Translate();
                int pointsAfter = (int)abilityPointsProperty.GetValue(data);
                RebuildCategory(categoryItem, pawn, mode);
                TolkHelper.Speak(PointSpentAnnouncement(label, level + 1, levelMax, pointsAfter));
            }
            catch (Exception ex)
            {
                ModLogger.Error($"RwomClassCardAdapter global skill spend failed: {ex.Message}");
                TolkHelper.Speak("RimWorldAccess.Compat.Rwom.ActionFailed".Loc());
            }
        }

        // ---- Sections 3..N: one section per ability ----

        private void BuildAbilitySections(InspectionTreeItem categoryItem, object comp, object data, Pawn pawn, InspectionMode mode)
        {
            HashSet<object> abilitySet = ResolveAbilityDefSet(comp, data, pawn);
            if (abilitySet == null || abilitySet.Count == 0)
                return;

            IList allPowers = allPowersWithSkillsProperty.GetValue(data) as IList;
            if (allPowers == null)
                return;

            // Mirrors CustomPowersHandler's usedAbilities dedup.
            var usedAbilities = new HashSet<object>();
            foreach (object power in allPowers)
            {
                object abilityDefObj = abilityDefProperty.GetValue(power); // side effect: refreshes power.maxLevel
                if (abilityDefObj == null || !abilitySet.Contains(abilityDefObj))
                    continue;

                if (!isMight && IsWarlockSuccubusExcluded(pawn, data, power))
                    continue;

                if (usedAbilities.Contains(abilityDefObj))
                    continue;
                usedAbilities.Add(abilityDefObj);

                object capturedPower = power;
                object capturedDefObj = abilityDefObj;
                InspectNodeFactory.GuardedBuild("RwomClassCardAdapter", () => BuildAbilitySection(categoryItem, comp, data, capturedPower, capturedDefObj, pawn, mode));
            }
        }

        // Mirrors CustomPowersHandler's Warlock/Succubus cross-exclusion; magic only.
        private bool IsWarlockSuccubusExcluded(Pawn pawn, object data, object power)
        {
            string[] excludedDefNames = { "TM_SoulBond", "TM_ShadowBolt", "TM_Dominate" };

            TraitDef warlock = Trait("Warlock");
            if (warlock != null && pawn.story.traits.HasTrait(warlock))
            {
                IList sdList = DataFieldList(data, "MagicPowersSD");
                return IsAnyReferenceMatch(sdList, excludedDefNames, power);
            }

            TraitDef succubus = Trait("Succubus");
            if (succubus != null && pawn.story.traits.HasTrait(succubus))
            {
                IList wdList = DataFieldList(data, "MagicPowersWD");
                return IsAnyReferenceMatch(wdList, excludedDefNames, power);
            }

            return false;
        }

        private bool IsAnyReferenceMatch(IList powers, string[] abilityDefNames, object power)
        {
            if (powers == null)
                return false;
            foreach (string name in abilityDefNames)
            {
                object candidate = FindPowerByAbilityDefName(powers, name);
                if (candidate != null && ReferenceEquals(candidate, power))
                    return true;
            }
            return false;
        }

        private void BuildAbilitySection(InspectionTreeItem categoryItem, object comp, object data, object power, object abilityDefObj, Pawn pawn, InspectionMode mode)
        {
            var def = (Def)abilityDefObj;
            string abilityLabel = def.LabelCap;

            int level = (int)levelField.GetValue(power);
            int maxLevel = (int)maxLevelField.GetValue(power);
            bool learned = (bool)learnedField.GetValue(power);
            int abilityPoints = (int)abilityPointsProperty.GetValue(data);
            bool writable = mode != InspectionMode.ReadOnly;

            var item = new InspectionTreeItem
            {
                Type = InspectionTreeItem.ItemType.Item,
                ExpandedLabel = abilityLabel,
                Data = power,
                IndentLevel = categoryItem.IndentLevel + 1,
                IsExpandable = true,
                IsExpanded = false,
            };

            string stateText = ComposeAbilityState(power, def, level, maxLevel, learned);
            if (writable && !learned && CanLearn(power, abilityPoints))
            {
                InspectNodeFactory.ActionRow(item, stateText, power,
                    () => OnLearnAbility(categoryItem, comp, data, power, abilityDefObj, pawn, mode));
            }
            else if (writable && learned && CanLevelUpAbility(power, def, level, maxLevel, abilityPoints))
            {
                InspectNodeFactory.ActionRow(item, stateText, power,
                    () => OnLevelUpAbility(categoryItem, comp, data, power, def, pawn, mode));
            }
            else
            {
                InspectNodeFactory.DetailLine(item, stateText);
            }

            string descriptionText = ComposeAbilityDescription(power, def, level, maxLevel);
            if (!string.IsNullOrEmpty(descriptionText))
                InspectNodeFactory.DetailLine(item, descriptionText);

            BuildAbilitySkillRows(item, categoryItem, comp, data, power, abilityDefObj, learned, abilityPoints, pawn, mode);

            string autocastText = ComposeAutocastRow(power);
            if (autocastText != null)
            {
                if (writable && learned)
                {
                    InspectNodeFactory.ActionRow(item, autocastText, power,
                        () => OnToggleAutocast(categoryItem, power, abilityLabel, pawn, mode));
                }
                else
                {
                    InspectNodeFactory.DetailLine(item, autocastText);
                }
            }

            var childLabels = item.Children.Select(c => c.Label).ToList();
            item.Label = childLabels.Count > 0
                ? abilityLabel + ": " + string.Join(" ", childLabels)
                : abilityLabel;

            InspectNodeFactory.Attach(categoryItem, item);
        }

        // Mirrors the affordability gate the card's Learn button checks, so a row
        // becomes an Action only when the sighted button would be clickable.
        private bool CanLearn(object power, int abilityPoints)
        {
            bool requiresScroll = requiresScrollField != null && (bool)requiresScrollField.GetValue(power);
            int learnCost = (int)learnCostField.GetValue(power);
            return !requiresScroll && abilityPoints >= learnCost;
        }

        // The condition under which the card swaps its icon-only display for a
        // clickable ButtonImage (CustomPowersHandler's flag999 && !flag10).
        private bool CanLevelUpAbility(object power, Def def, int level, int maxLevel, int abilityPoints)
        {
            if (level >= maxLevel || !IsTiered(power, def))
                return false;
            int costToLevel = (int)costToLevelField.GetValue(power);
            return costToLevel <= abilityPoints;
        }

        // MUTATION-C: mirrors MagicCardUtility.CustomPowersHandler's learn button
        // (:1912-1944) / MightCardUtility's equivalent (:1270-1306); no Try*/Can*
        // twin exists on either MagicPower/MightPower or the comp.
        private void OnLearnAbility(InspectionTreeItem categoryItem, object comp, object data, object power, object abilityDefObj, Pawn pawn, InspectionMode mode)
        {
            try
            {
                int abilityPoints = (int)abilityPointsProperty.GetValue(data);
                if (!CanLearn(power, abilityPoints))
                {
                    TolkHelper.Speak("RimWorldAccess.Compat.Rwom.NotEnoughPoints".Loc());
                    return;
                }

                var def = (Def)abilityDefObj;
                int learnCost = (int)learnCostField.GetValue(power);
                learnedField.SetValue(power, true);

                if (!isMight)
                {
                    bool shouldInit = shouldInitializeField != null && (bool)shouldInitializeField.GetValue(abilityDefObj);
                    if (def.defName != "TM_TechnoBit" && shouldInit)
                    {
                        addPawnAbilityMethod.Invoke(comp, new object[] { abilityDefObj, true, -1f });
                    }
                    if (def.defName == "TM_TechnoWeapon")
                    {
                        AddPawnAbilityByDefName(comp, data, "MagicPowersStandalone", "TM_NanoStimulant");
                        object nanoPower = FindPowerByAbilityDefName(DataFieldList(data, "MagicPowersStandalone"), "TM_NanoStimulant");
                        if (nanoPower != null)
                            learnedField.SetValue(nanoPower, true);
                    }
                    IList childAbilities = childAbilitiesField?.GetValue(abilityDefObj) as IList;
                    if (childAbilities != null)
                    {
                        foreach (object child in childAbilities)
                        {
                            bool childShouldInit = shouldInitializeField != null && (bool)shouldInitializeField.GetValue(child);
                            if (childShouldInit)
                                addPawnAbilityMethod.Invoke(comp, new object[] { child, true, -1f });
                        }
                    }
                    abilityPointsProperty.SetValue(data, abilityPoints - learnCost);
                }
                else
                {
                    // Might's own Learn button does NOT call AddPawnAbility or deduct
                    // points; only these three defName cases do anything more.
                    if (def.defName == "TM_PistolSpec")
                    {
                        AddPawnAbilityByDefName(comp, data, "MightPowersSS", "TM_PistolWhip");
                        skillPistolWhipField.SetValue(comp, true);
                    }
                    else if (def.defName == "TM_RifleSpec")
                    {
                        AddPawnAbilityByDefName(comp, data, "MightPowersSS", "TM_SuppressingFire");
                        skillSuppressingFireField.SetValue(comp, true);
                        AddPawnAbilityByDefName(comp, data, "MightPowersSS", "TM_Mk203GL");
                        skillMk203GLField.SetValue(comp, true);
                    }
                    else if (def.defName == "TM_ShotgunSpec")
                    {
                        AddPawnAbilityByDefName(comp, data, "MightPowersSS", "TM_Buckshot");
                        skillBuckshotField.SetValue(comp, true);
                        AddPawnAbilityByDefName(comp, data, "MightPowersSS", "TM_BreachingCharge");
                        skillBreachingChargeField.SetValue(comp, true);
                    }
                }

                SoundDefOf.Click.PlayOneShotOnCamera(null);
                int pointsAfter = (int)abilityPointsProperty.GetValue(data);
                RebuildCategory(categoryItem, pawn, mode);
                TolkHelper.Speak(LearnedAnnouncement(def.LabelCap, pointsAfter));
            }
            catch (Exception ex)
            {
                ModLogger.Error($"RwomClassCardAdapter learn ability failed: {ex.Message}");
                TolkHelper.Speak("RimWorldAccess.Compat.Rwom.ActionFailed".Loc());
            }
        }

        private void AddPawnAbilityByDefName(object comp, object data, string listFieldName, string abilityDefName)
        {
            IList list = DataFieldList(data, listFieldName);
            object targetPower = FindPowerByAbilityDefName(list, abilityDefName);
            if (targetPower == null)
                return;
            object abilityDefObj = abilityDefProperty.GetValue(targetPower);
            addPawnAbilityMethod.Invoke(comp, new object[] { abilityDefObj, true, -1f });
        }

        // Vehicle B (LevelUpPower) + MUTATION-C (mirrors MagicCardUtility.cs:
        // 1984-1985 / MightCardUtility.cs:1339-1341 point deduction paired with
        // LevelUpPower; the vehicle itself does not deduct).
        private void OnLevelUpAbility(InspectionTreeItem categoryItem, object comp, object data, object power, Def def, Pawn pawn, InspectionMode mode)
        {
            try
            {
                int abilityPoints = (int)abilityPointsProperty.GetValue(data);
                int level = (int)levelField.GetValue(power);
                int maxLevel = (int)maxLevelField.GetValue(power);
                if (!CanLevelUpAbility(power, def, level, maxLevel, abilityPoints))
                {
                    TolkHelper.Speak("RimWorldAccess.Compat.Rwom.NotEnoughPoints".Loc());
                    return;
                }

                int costToLevel = (int)costToLevelField.GetValue(power);
                levelUpPowerMethod.Invoke(comp, new object[] { power });
                abilityPointsProperty.SetValue(data, abilityPoints - costToLevel);

                if (isMight)
                {
                    // Might-only: CustomPowersHandler also calls FixPowers() after the
                    // deduction; magic has no equivalent call.
                    fixPowersMethod.Invoke(comp, null);
                }

                SoundDefOf.Click.PlayOneShotOnCamera(null);
                int newLevel = (int)levelField.GetValue(power);
                int pointsAfter = (int)abilityPointsProperty.GetValue(data);
                RebuildCategory(categoryItem, pawn, mode);
                TolkHelper.Speak(PointSpentAnnouncement(def.LabelCap, newLevel, maxLevel, pointsAfter));
            }
            catch (Exception ex)
            {
                ModLogger.Error($"RwomClassCardAdapter ability level-up failed: {ex.Message}");
                TolkHelper.Speak("RimWorldAccess.Compat.Rwom.ActionFailed".Loc());
            }
        }

        // Vehicle B: the MagicPower/MightPower AutoCast setter, with the mod's own
        // 5-tick debounce. The card draws no checkbox; autocast is gizmo-only.
        private void OnToggleAutocast(InspectionTreeItem categoryItem, object power, string abilityLabel, Pawn pawn, InspectionMode mode)
        {
            try
            {
                bool current = (bool)autoCastProperty.GetValue(power);
                autoCastProperty.SetValue(power, !current);
                bool newState = (bool)autoCastProperty.GetValue(power); // re-read: the setter's own debounce may have ignored this call
                if (newState == current)
                {
                    // The setter's debounce swallowed the write; its interactionTick
                    // only advances with game ticks, so a second toggle while paused
                    // is always dropped. Say so rather than restate the old state.
                    TolkHelper.Speak("RimWorldAccess.Compat.Rwom.AutocastUnchanged".Loc(StateWord(newState), abilityLabel));
                    return;
                }

                SoundDefOf.Click.PlayOneShotOnCamera(null);
                RebuildCategory(categoryItem, pawn, mode);
                TolkHelper.Speak("RimWorldAccess.Compat.Rwom.AutocastToggled".Loc(StateWord(newState), abilityLabel));
            }
            catch (Exception ex)
            {
                ModLogger.Error($"RwomClassCardAdapter autocast toggle failed: {ex.Message}");
                TolkHelper.Speak("RimWorldAccess.Compat.Rwom.ActionFailed".Loc());
            }
        }

        private static string StateWord(bool on) => on ? "On".Translate().ToString() : "Off".Translate().ToString();

        // Singular/plural selection for the trailing points-remaining phrase.
        private static Localized LearnedAnnouncement(string label, int pointsAfter)
        {
            return pointsAfter == 1
                ? "RimWorldAccess.Compat.Rwom.LearnedAnnounceOne".Loc(label)
                : "RimWorldAccess.Compat.Rwom.LearnedAnnounce".Loc(label, pointsAfter.ToString());
        }

        private static Localized PointSpentAnnouncement(string label, int level, int levelMax, int pointsAfter)
        {
            return pointsAfter == 1
                ? "RimWorldAccess.Compat.Rwom.PointSpentAnnounceOne".Loc(label, level.ToString(), levelMax.ToString())
                : "RimWorldAccess.Compat.Rwom.PointSpentAnnounce".Loc(label, level.ToString(), levelMax.ToString(), pointsAfter.ToString());
        }

        // Mirrors CustomPowersHandler's state dispatch. The one deviation: a learned
        // untiered ability, which the card shows as a bare icon with no counter,
        // reads as "Learned." so read-only navigation never goes silent.
        private string ComposeAbilityState(object power, Def def, int level, int maxLevel, bool learned)
        {
            if (!learned)
            {
                bool requiresScroll = requiresScrollField != null && (bool)requiresScrollField.GetValue(power);
                int learnCost = (int)learnCostField.GetValue(power);
                return requiresScroll
                    ? "RimWorldAccess.Compat.Rwom.AbilityLocked".Translate()
                    : (learnCost == 1
                        ? "RimWorldAccess.Compat.Rwom.AbilityNotLearnedOne".Translate()
                        : "RimWorldAccess.Compat.Rwom.AbilityNotLearned".Translate(learnCost));
            }

            if (!IsTiered(power, def))
                return "RimWorldAccess.Compat.Rwom.AbilityLearned".Translate();

            string state = "RimWorldAccess.Compat.Rwom.AbilityTieredState".Translate(level, maxLevel);
            if (level < maxLevel)
            {
                int costToLevel = (int)costToLevelField.GetValue(power);
                state = state + " " + (costToLevel == 1
                    ? "RimWorldAccess.Compat.Rwom.AbilityLevelUpCostOne".Translate().ToString()
                    : "RimWorldAccess.Compat.Rwom.AbilityLevelUpCost".Translate(costToLevel).ToString());
            }
            return state;
        }

        // A count above one is tiered in both modes; single-tier-looking abilities
        // are additionally matched by TM_Calc.IsIconAbility_03 (magic) or the
        // hardcoded defName set (might).
        private bool IsTiered(object power, Def currentDef)
        {
            IList tmDefs = tmAbilityDefsField.GetValue(power) as IList;
            if (tmDefs != null && tmDefs.Count > 1)
                return true;
            if (isMight)
                return MightTieredDefNames.Contains(currentDef.defName);
            return isIconAbility03Method != null && (bool)isIconAbility03Method.Invoke(null, new object[] { currentDef });
        }

        // Same shape as IsTiered, but magic uses IsIconAbility_02: a smaller set
        // missing max-tier variants, which callers exclude via level &lt; maxLevel.
        private bool IsTieredForDescription(object power, Def currentDef)
        {
            IList tmDefs = tmAbilityDefsField.GetValue(power) as IList;
            if (tmDefs != null && tmDefs.Count > 1)
                return true;
            if (isMight)
                return MightTieredDefNames.Contains(currentDef.defName);
            return isIconAbility02Method != null && (bool)isIconAbility02Method.Invoke(null, new object[] { currentDef });
        }

        // Mirrors CustomPowersHandler's tooltip: current-level description plus the
        // next tier's when one exists (abilityDescDef is a pass-through to abilityDef).
        private string ComposeAbilityDescription(object power, Def def, int level, int maxLevel)
        {
            string currentDesc = SpeechFlatten.ToSentences(def.description);
            bool showNext = level < maxLevel && IsTieredForDescription(power, def);
            if (!showNext)
                return currentDesc;

            object nextDefObj = nextLevelAbilityDefProperty.GetValue(power);
            string nextDesc = SpeechFlatten.ToSentences((nextDefObj as Def)?.description);
            if (string.IsNullOrEmpty(nextDesc))
                return currentDesc;

            string prefix = string.IsNullOrEmpty(currentDesc) ? "" : currentDesc + " ";
            return prefix + "RimWorldAccess.Compat.Rwom.AbilityNextLevelDescription".Translate(nextDesc);
        }

        // Mirrors CustomPowersHandler's up to three skill rows per ability, each
        // gated the way CustomSkillHandler gates its own "+".
        private void BuildAbilitySkillRows(InspectionTreeItem item, InspectionTreeItem categoryItem, object comp, object data,
            object power, object abilityDefObj, bool learned, int abilityPoints, Pawn pawn, InspectionMode mode)
        {
            object[] args = { abilityDefObj };
            TryAddAbilitySkillRow(item, categoryItem, comp, data, power, abilityDefObj,
                getSkillPowerMethod.Invoke(data, args), learned, abilityPoints, pawn, mode);
            TryAddAbilitySkillRow(item, categoryItem, comp, data, power, abilityDefObj,
                getSkillEfficiencyMethod.Invoke(data, args), learned, abilityPoints, pawn, mode);
            TryAddAbilitySkillRow(item, categoryItem, comp, data, power, abilityDefObj,
                getSkillVersatilityMethod.Invoke(data, args), learned, abilityPoints, pawn, mode);
        }

        private void TryAddAbilitySkillRow(InspectionTreeItem item, InspectionTreeItem categoryItem, object comp, object data,
            object power, object abilityDefObj, object skill, bool learned, int abilityPoints, Pawn pawn, InspectionMode mode)
        {
            if (skill == null)
                return;

            bool pointAvailable = CanSpendAbilitySkillPoint(skill, abilityPoints, learned);
            string rowText = ComposeSkillRow(skill, showCost: true, pointAvailable);

            if (mode != InspectionMode.ReadOnly && pointAvailable)
            {
                InspectNodeFactory.ActionRow(item, rowText, skill,
                    () => OnSpendAbilitySkillPoint(categoryItem, comp, data, power, abilityDefObj, skill, pawn, mode));
            }
            else
            {
                InspectNodeFactory.DetailLine(item, rowText);
            }
        }

        // MUTATION-C: mirrors CustomSkillHandler's spend effect and side effects
        // (MagicCardUtility.cs:2037-2075 / MightCardUtility.cs:1404-1429) — bare
        // field mutation, no Try*/Can* twin exists.
        private void OnSpendAbilitySkillPoint(InspectionTreeItem categoryItem, object comp, object data, object power,
            object abilityDefObj, object skill, Pawn pawn, InspectionMode mode)
        {
            try
            {
                bool learned = (bool)learnedField.GetValue(power);
                int abilityPoints = (int)abilityPointsProperty.GetValue(data);
                if (!CanSpendAbilitySkillPoint(skill, abilityPoints, learned))
                {
                    TolkHelper.Speak("RimWorldAccess.Compat.Rwom.NotEnoughPoints".Loc());
                    return;
                }

                // Mirrors CustomSkillHandler's violence-refusal branch.
                if (IsVerbViolentAndPawnIncapable(pawn, abilityDefObj))
                {
                    Messages.Message("IsIncapableOfViolenceLower".Translate(pawn.LabelShort), pawn, MessageTypeDefOf.RejectInput);
                    return;
                }

                int level = (int)skillLevelField.GetValue(skill);
                int levelMax = (int)skillLevelMaxField.GetValue(skill);
                int costToLevel = (int)skillCostToLevelField.GetValue(skill);
                skillLevelField.SetValue(skill, level + 1);
                abilityPointsProperty.SetValue(data, abilityPoints - costToLevel);

                // CustomSkillHandler side effects: leveling one specific skill past a
                // threshold grants the other mode's powers or a follow-on ability.
                string skillLabelKey = (string)skillLabelField.GetValue(skill);
                if (!isMight && skillLabelKey == "TM_Cantrips_eff" && level + 1 >= 15)
                {
                    InvokeResolveOtherComp(pawn);
                }
                if (isMight && skillLabelKey == "TM_FieldTraining_eff" && level + 1 >= 15)
                {
                    InvokeResolveOtherComp(pawn);
                }
                if (!isMight && skillLabelKey == "TM_LightSkip_pwr")
                {
                    if (level + 1 == 1)
                        AddPawnAbilityByDefName(comp, data, "MagicPowersStandalone", "TM_LightSkipMass");
                    if (level + 1 == 2)
                        AddPawnAbilityByDefName(comp, data, "MagicPowersStandalone", "TM_LightSkipGlobal");
                }

                SoundDefOf.Click.PlayOneShotOnCamera(null);
                string label = skillLabelKey.Translate();
                int pointsAfter = (int)abilityPointsProperty.GetValue(data);
                RebuildCategory(categoryItem, pawn, mode);
                TolkHelper.Speak(PointSpentAnnouncement(label, level + 1, levelMax, pointsAfter));
            }
            catch (Exception ex)
            {
                ModLogger.Error($"RwomClassCardAdapter ability skill spend failed: {ex.Message}");
                TolkHelper.Speak("RimWorldAccess.Compat.Rwom.ActionFailed".Loc());
            }
        }

        // Mirrors CustomSkillHandler's check exactly, including the equality (not
        // HasFlag) comparison against WorkTags.Violent. VerbProperties_Ability.
        // isViolent is its own field, distinct from vanilla VerbProperties.violent.
        private bool IsVerbViolentAndPawnIncapable(Pawn pawn, object abilityDefObj)
        {
            if (pawn.story == null || pawn.story.DisabledWorkTagsBackstoryAndTraits != WorkTags.Violent)
                return false;
            object mainVerb = mainVerbField.GetValue(abilityDefObj);
            return mainVerb != null && (bool)isViolentField.GetValue(mainVerb);
        }

        // A no-op if the pawn has no comp for the other mode.
        private void InvokeResolveOtherComp(Pawn pawn)
        {
            if (resolveOtherCompMethod == null || otherCompType == null || pawn.AllComps == null)
                return;
            foreach (ThingComp c in pawn.AllComps)
            {
                if (otherCompType.IsInstanceOfType(c))
                {
                    resolveOtherCompMethod.Invoke(null, new object[] { c });
                    return;
                }
            }
        }

        // The card draws no autocast checkbox (that's gizmo-only), so this row is an
        // addition, actionable once learned — only then does a gizmo state exist.
        private string ComposeAutocastRow(object power)
        {
            object autocasting = autocastingField.GetValue(power);
            if (autocasting == null)
                return null;
            bool on = (bool)autocastField.GetValue(power);
            return on ? "RimWorldAccess.Compat.Rwom.AutocastOn".Translate() : "RimWorldAccess.Compat.Rwom.AutocastOff".Translate();
        }

        private HashSet<object> ResolveAbilityDefSet(object comp, object data, Pawn pawn)
        {
            object customClass = customClassField.GetValue(comp);
            if (customClass != null)
                return GetAbilityList(comp, null);

            // InnerFire is a standalone `if` ahead of DrawMagicCard's else-if chain;
            // folding it in is harmless, as RWoM's class traits are exclusive.
            if (!isMight)
            {
                TraitDef innerFire = Trait("InnerFire");
                if (innerFire != null && pawn.story.traits.HasTrait(innerFire))
                    return GetAbilityList(comp, DataFieldList(data, "MagicPowersIF"));
            }

            foreach (TraitRule rule in isMight ? MightTraitRules : MagicTraitRules)
            {
                TraitDef trait = Trait(rule.TraitDefName);
                if (trait != null && pawn.story.traits.HasTrait(trait))
                    return ResolveRule(rule, comp, data);
            }

            IList advancedClasses = advancedClassesProperty.GetValue(comp, null) as IList;
            if (advancedClasses != null && advancedClasses.Count > 0)
                return GetAbilityList(comp, null);

            return null;
        }

        private HashSet<object> ResolveRule(TraitRule rule, object comp, object data)
        {
            switch (rule.Special)
            {
                case SpecialKind.Technomancer:
                    return TechnomancerAbilityList(comp, data);
                case SpecialKind.ChaosMage:
                    return ChaosMageAbilityList(comp, data);
                case SpecialKind.SuperSoldier:
                    return SuperSoldierAbilityList(comp, data);
                default:
                    HashSet<object> list = GetAbilityList(comp, DataFieldList(data, rule.FieldName));
                    if (rule.RemoveDefName != null)
                        RemoveByDefName(list, rule.RemoveDefName);
                    return list;
            }
        }

        // Mirrors the card's own GetAbilityList: flattens a class power list's
        // TMabilityDefs plus the pawn's custom-class and advanced-class lists.
        private HashSet<object> GetAbilityList(object comp, IList classList)
        {
            var result = new HashSet<object>();
            if (classList != null)
            {
                foreach (object power in classList)
                {
                    IList defs = tmAbilityDefsField.GetValue(power) as IList;
                    if (defs == null)
                        continue;
                    foreach (object def in defs)
                        result.Add(def);
                }
            }

            object customClass = customClassField.GetValue(comp);
            if (customClass != null)
                AddCustomClassAbilities(customClass, result);

            IList advancedClasses = advancedClassesProperty.GetValue(comp, null) as IList;
            if (advancedClasses != null)
            {
                foreach (object cc in advancedClasses)
                    AddCustomClassAbilities(cc, result);
            }

            return result;
        }

        private void AddCustomClassAbilities(object customClassInstance, HashSet<object> result)
        {
            if (customClassAbilitiesField == null)
            {
                string memberName = isMight ? "classFighterAbilities" : "classMageAbilities";
                customClassAbilitiesField = AccessTools.Field(customClassInstance.GetType(), memberName);
                if (customClassAbilitiesField == null)
                {
                    ModLogger.Error($"RwomClassCardAdapter: could not resolve TM_CustomClass.{memberName}; " +
                        "custom-class abilities will not appear.");
                    return;
                }
            }

            IList abilities = customClassAbilitiesField.GetValue(customClassInstance) as IList;
            if (abilities == null)
                return;
            foreach (object def in abilities)
                result.Add(def);
        }

        // Mirrors the Technomancer branch: TechnoBit/Turret/Weapon are mutually
        // exclusive, and the upgrade abilities stay hidden until one is chosen.
        private HashSet<object> TechnomancerAbilityList(object comp, object data)
        {
            IList magicPowersT = DataFieldList(data, "MagicPowersT");
            HashSet<object> list = GetAbilityList(comp, magicPowersT);

            object bit = FindPowerByAbilityDefName(magicPowersT, "TM_TechnoBit");
            object turret = FindPowerByAbilityDefName(magicPowersT, "TM_TechnoTurret");
            object weapon = FindPowerByAbilityDefName(magicPowersT, "TM_TechnoWeapon");

            if (IsLearned(bit))
            {
                RemoveByDefName(list, "TM_TechnoTurret");
                RemoveByDefName(list, "TM_TechnoWeapon");
            }
            else if (IsLearned(turret))
            {
                RemoveByDefName(list, "TM_TechnoBit");
                RemoveByDefName(list, "TM_TechnoWeapon");
            }
            else if (IsLearned(weapon))
            {
                RemoveByDefName(list, "TM_TechnoBit");
                RemoveByDefName(list, "TM_TechnoTurret");
            }
            else
            {
                RemoveByDefName(list, "TM_TechnoShield");
                RemoveByDefName(list, "TM_Overdrive");
                RemoveByDefName(list, "TM_Sabotage");
            }
            return list;
        }

        // Mirrors the TM_SuperSoldier branch: Technomancer's shape over Pistol/Rifle/
        // ShotgunSpec, hiding CQC/FirstAid/60mmMortar until one is chosen.
        private HashSet<object> SuperSoldierAbilityList(object comp, object data)
        {
            IList mightPowersSS = DataFieldList(data, "MightPowersSS");
            HashSet<object> list = GetAbilityList(comp, mightPowersSS);

            object pistol = FindPowerByAbilityDefName(mightPowersSS, "TM_PistolSpec");
            object rifle = FindPowerByAbilityDefName(mightPowersSS, "TM_RifleSpec");
            object shotgun = FindPowerByAbilityDefName(mightPowersSS, "TM_ShotgunSpec");

            if (IsLearned(pistol))
            {
                RemoveByDefName(list, "TM_RifleSpec");
                RemoveByDefName(list, "TM_ShotgunSpec");
            }
            else if (IsLearned(rifle))
            {
                RemoveByDefName(list, "TM_PistolSpec");
                RemoveByDefName(list, "TM_ShotgunSpec");
            }
            else if (IsLearned(shotgun))
            {
                RemoveByDefName(list, "TM_RifleSpec");
                RemoveByDefName(list, "TM_PistolSpec");
            }
            else
            {
                RemoveByDefName(list, "TM_CQC");
                RemoveByDefName(list, "TM_FirstAid");
                RemoveByDefName(list, "TM_60mmMortar");
            }
            return list;
        }

        // Mirrors the ChaosMage branch: the base power list plus each learned rolled
        // chaosPower, matched by reference on the first TMabilityDef as the card does.
        private HashSet<object> ChaosMageAbilityList(object comp, object data)
        {
            IList magicPowersCM = DataFieldList(data, "MagicPowersCM");
            var cmPowers = new List<object>();
            if (magicPowersCM != null)
            {
                foreach (object p in magicPowersCM)
                    cmPowers.Add(p);
            }

            IList chaosPowers = chaosPowersField?.GetValue(comp) as IList;
            IList allForChaosMage = allPowersForChaosMageField == null ? null : ReflectionSurface.ValueOf(allPowersForChaosMageField, data) as IList;
            if (chaosPowers != null && allForChaosMage != null && chaosPowersAbilityProperty != null)
            {
                foreach (object chaosPower in chaosPowers)
                {
                    object abilityDefObj = chaosPowersAbilityProperty.GetValue(chaosPower);
                    if (abilityDefObj == null)
                        continue;
                    object matched = FindPowerByFirstAbilityDefRef(allForChaosMage, abilityDefObj);
                    if (matched != null && IsLearned(matched))
                        cmPowers.Add(matched);
                }
            }

            return GetAbilityList(comp, cmPowers);
        }

        private object FindPowerByAbilityDefName(IList powers, string defName)
        {
            if (powers == null)
                return null;
            foreach (object power in powers)
            {
                if (abilityDefProperty.GetValue(power) is Def def && def.defName == defName)
                    return power;
            }
            return null;
        }

        private object FindPowerByFirstAbilityDefRef(IList powers, object abilityDefObj)
        {
            foreach (object power in powers)
            {
                IList defs = tmAbilityDefsField.GetValue(power) as IList;
                if (defs != null && defs.Count > 0 && ReferenceEquals(defs[0], abilityDefObj))
                    return power;
            }
            return null;
        }

        private bool IsLearned(object power) => power != null && (bool)learnedField.GetValue(power);

        private static void RemoveByDefName(HashSet<object> set, string defName)
        {
            object match = null;
            foreach (object d in set)
            {
                if (d is Def def && def.defName == defName)
                {
                    match = d;
                    break;
                }
            }
            if (match != null)
                set.Remove(match);
        }

        /// <summary>
        /// Cached lookup of a MagicData/MightData member. Power and skill lists are
        /// lazily-initializing properties over lowercase backing fields while plain
        /// members stay fields, hence the field-then-property probe.
        /// </summary>
        private MemberInfo DataField(string name)
        {
            if (!dataFieldCache.TryGetValue(name, out MemberInfo mi))
            {
                mi = ReflectionSurface.TryFieldOrProperty(dataType, name);
                dataFieldCache[name] = mi;
            }
            return mi;
        }

        private IList DataFieldList(object data, string name)
        {
            MemberInfo mi = DataField(name);
            return mi == null ? null : ReflectionSurface.ValueOf(mi, data) as IList;
        }

        private TraitDef Trait(string defName)
        {
            if (!traitCache.TryGetValue(defName, out TraitDef def))
            {
                def = DefDatabase<TraitDef>.GetNamedSilentFail(defName);
                traitCache[defName] = def;
            }
            return def;
        }
    }
}
