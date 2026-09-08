using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using RimWorld;
using Verse;

namespace RimWorldAccess
{
    /// <summary>Reflection surface over the forge: enhancement comp, forge utility, runes, weapon mastery, and the two workbench windows.</summary>
    internal static class IsekaiForgeCompat
    {
        internal static Type EnhancementType;
        internal static Type RuneDefType;

        private static FieldInfo refinementLevelField;
        private static PropertyInfo maxRuneSlotsProperty;
        private static PropertyInfo usedRuneSlotsProperty;
        private static MethodInfo canRefineMethod;
        private static MethodInfo canAddRuneMethod;
        private static MethodInfo appliedRunesWithRanksMethod;
        private static MethodInfo tryAddRuneMethod;
        private static MethodInfo removeRuneAtMethod;

        private static MethodInfo getRefineCostMethod;
        private static MethodInfo hasMaterialsMethod;
        private static MethodInfo successChanceMethod;
        private static MethodInfo downgradeChanceMethod;
        private static MethodInfo needsRepairMethod;
        private static MethodInfo repairCostMethod;
        private static MethodInfo liveMaxHitPointsMethod;
        private static MethodInfo repairItemMethod;
        private static MethodInfo countOnMapMethod;
        private static PropertyInfo refinementComponentProperty;
        private static FieldInfo costCoreDefField;
        private static FieldInfo costCoreCountField;
        private static FieldInfo costSecondaryDefField;
        private static FieldInfo costSecondaryCountField;
        private static FieldInfo costSteelField;
        private static FieldInfo costComponentsField;
        private static readonly Dictionary<string, MethodInfo> bonusMethods = new Dictionary<string, MethodInfo>();

        private static FieldInfo runeCategoryField;
        private static FieldInfo runeMaxRankField;
        private static MethodInfo runeDescriptionForRankMethod;
        private static MethodInfo romanNumeralMethod;

        private static MethodInfo masteryXpMethod;
        private static MethodInfo masteryTierMethod;
        private static MethodInfo masteryNextTierMethod;
        private static MethodInfo masteryBonusesMethod;
        private static MethodInfo masteryTierLabelMethod;
        private static Type masteryTierEnum;

        private static FieldInfo forgeMapField;
        private static FieldInfo forgeSelectedItemField;
        private static FieldInfo forgeLastResultField;
        private static MethodInfo forgeAllEquipmentMethod;
        private static MethodInfo doRefinementMethod;

        private static FieldInfo runicMapField;
        private static FieldInfo runicSelectedItemField;
        private static FieldInfo runicSelectedSlotField;
        private static FieldInfo runicSelectedRankField;
        private static MethodInfo runicAllEquipmentMethod;
        private static MethodInfo runicAvailableRunesMethod;
        private static MethodInfo findRuneDefAndRankMethod;

        private static readonly string[] BonusMethodNames =
        {
            "GetMeleeDamageBonus", "GetMeleeSpeedBonus", "GetWeaponMassReduction",
            "GetRangedDamageBonus", "GetRangedCooldownReduction", "GetRangedAccuracyBonus",
            "GetArmorBonus", "GetArmorMoveSpeedBonus", "GetArmorMassReduction",
        };

        internal static readonly LazyReflectionGate Gate =
            new LazyReflectionGate("Isekai forge", surface =>
            {
                Type enhancement = surface.Type("IsekaiLeveling.Forge.CompForgeEnhancement");
                Type utility = surface.Type("IsekaiLeveling.Forge.ForgeUtility");
                Type refineCost = surface.Type("IsekaiLeveling.Forge.ForgeUtility+RefineCost");
                Type runeDef = surface.Type("IsekaiLeveling.Forge.RuneDef");
                Type mastery = surface.Type("IsekaiLeveling.Forge.WeaponMasteryTracker");
                Type tierEnum = surface.Type("IsekaiLeveling.Forge.MasteryTier");
                Type forgeWindow = surface.Type("IsekaiLeveling.Forge.Window_Forge");
                Type runicWindow = surface.Type("IsekaiLeveling.Forge.Window_RunicStation");

                FieldInfo refinementLevel = surface.Field(enhancement, "refinementLevel");
                PropertyInfo maxSlots = surface.Property(enhancement, "MaxRuneSlots");
                PropertyInfo usedSlots = surface.Property(enhancement, "UsedRuneSlots");
                MethodInfo canRefine = surface.Method(enhancement, "CanRefine", Type.EmptyTypes);
                MethodInfo canAddRune = surface.Method(enhancement, "CanAddRune", Type.EmptyTypes);
                MethodInfo appliedWithRanks = surface.Method(enhancement, "GetAppliedRunesWithRanks", Type.EmptyTypes);
                MethodInfo tryAddRune = runeDef == null ? null : surface.Method(enhancement, "TryAddRune", new[] { runeDef, typeof(int) });
                MethodInfo removeRuneAt = surface.Method(enhancement, "RemoveRuneAt", new[] { typeof(int) });

                MethodInfo getRefineCost = surface.Method(utility, "GetRefineCost", new[] { typeof(int) });
                MethodInfo hasMaterials = refineCost == null ? null : surface.Method(utility, "HasMaterials", new[] { typeof(Map), refineCost });
                MethodInfo successChance = surface.Method(utility, "GetSuccessChance", new[] { typeof(int), typeof(Pawn) });
                MethodInfo downgradeChance = surface.Method(utility, "GetDowngradeChance", new[] { typeof(int) });
                MethodInfo needsRepair = surface.Method(utility, "NeedsRepair", new[] { typeof(Thing) });
                MethodInfo repairCost = surface.Method(utility, "GetRepairEssenceCost", new[] { typeof(Thing) });
                MethodInfo liveMax = surface.Method(utility, "LiveMaxHitPoints", new[] { typeof(Thing) });
                MethodInfo repairItem = surface.Method(utility, "RepairItem", new[] { typeof(Thing), typeof(Map) });
                MethodInfo countOnMap = surface.Method(utility, "CountOnMap", new[] { typeof(Map), typeof(ThingDef) });
                PropertyInfo refinementComponent = surface.Property(utility, "RefinementComponent");
                FieldInfo coreDef = surface.Field(refineCost, "coreDef");
                FieldInfo coreCount = surface.Field(refineCost, "coreCount");
                FieldInfo secondaryDef = surface.Field(refineCost, "secondaryCoreDef");
                FieldInfo secondaryCount = surface.Field(refineCost, "secondaryCoreCount");
                FieldInfo steel = surface.Field(refineCost, "steel");
                FieldInfo components = surface.Field(refineCost, "components");
                var bonuses = new Dictionary<string, MethodInfo>();
                foreach (string name in BonusMethodNames)
                    bonuses[name] = surface.Method(utility, name, new[] { typeof(int) });

                FieldInfo runeCategory = surface.Field(runeDef, "category");
                FieldInfo runeMaxRank = surface.Field(runeDef, "maxRank");
                MethodInfo runeDescriptionForRank = surface.Method(runeDef, "GetStatDescriptionForRank", new[] { typeof(int) });
                MethodInfo romanNumeral = surface.Method(runeDef, "GetRomanNumeral", new[] { typeof(int) });

                MethodInfo masteryXp = surface.Method(mastery, "GetXP", new[] { typeof(string) });
                MethodInfo masteryTier = surface.Method(mastery, "GetMasteryTier", new[] { typeof(string) });
                MethodInfo masteryNext = surface.Method(mastery, "GetXPForNextTier", new[] { typeof(string) });
                MethodInfo masteryBonuses = surface.Method(mastery, "GetMasteryBonuses",
                    new[] { typeof(string), typeof(float).MakeByRefType(), typeof(float).MakeByRefType(), typeof(float).MakeByRefType() });
                MethodInfo masteryTierLabel = tierEnum == null ? null : surface.Method(mastery, "GetTierLabel", new[] { tierEnum });

                FieldInfo forgeMap = surface.Field(forgeWindow, "map");
                FieldInfo forgeSelected = surface.Field(forgeWindow, "selectedItem");
                FieldInfo forgeLastResult = surface.Field(forgeWindow, "lastResultText");
                MethodInfo forgeAllEquipment = surface.Method(forgeWindow, "GetAllEquipment", Type.EmptyTypes);
                MethodInfo doRefinement = enhancement == null ? null : surface.Method(forgeWindow, "DoRefinement", new[] { enhancement, typeof(int) });

                FieldInfo runicMap = surface.Field(runicWindow, "map");
                FieldInfo runicSelected = surface.Field(runicWindow, "selectedItem");
                FieldInfo runicSlot = surface.Field(runicWindow, "selectedSlotIndex");
                FieldInfo runicRank = surface.Field(runicWindow, "selectedRank");
                MethodInfo runicAllEquipment = surface.Method(runicWindow, "GetAllEquipment", Type.EmptyTypes);
                MethodInfo runicAvailable = enhancement == null ? null : surface.Method(runicWindow, "GetAvailableRuneItems", new[] { enhancement });
                MethodInfo findRuneDefAndRank = surface.Method(runicWindow, "FindRuneDefAndRankForItem", new[] { typeof(string) });

                if (!surface.Ready)
                    return false;

                EnhancementType = enhancement;
                RuneDefType = runeDef;
                refinementLevelField = refinementLevel;
                maxRuneSlotsProperty = maxSlots;
                usedRuneSlotsProperty = usedSlots;
                canRefineMethod = canRefine;
                canAddRuneMethod = canAddRune;
                appliedRunesWithRanksMethod = appliedWithRanks;
                tryAddRuneMethod = tryAddRune;
                removeRuneAtMethod = removeRuneAt;
                getRefineCostMethod = getRefineCost;
                hasMaterialsMethod = hasMaterials;
                successChanceMethod = successChance;
                downgradeChanceMethod = downgradeChance;
                needsRepairMethod = needsRepair;
                repairCostMethod = repairCost;
                liveMaxHitPointsMethod = liveMax;
                repairItemMethod = repairItem;
                countOnMapMethod = countOnMap;
                refinementComponentProperty = refinementComponent;
                costCoreDefField = coreDef;
                costCoreCountField = coreCount;
                costSecondaryDefField = secondaryDef;
                costSecondaryCountField = secondaryCount;
                costSteelField = steel;
                costComponentsField = components;
                bonusMethods.Clear();
                foreach (KeyValuePair<string, MethodInfo> pair in bonuses)
                    bonusMethods[pair.Key] = pair.Value;
                runeCategoryField = runeCategory;
                runeMaxRankField = runeMaxRank;
                runeDescriptionForRankMethod = runeDescriptionForRank;
                romanNumeralMethod = romanNumeral;
                masteryXpMethod = masteryXp;
                masteryTierMethod = masteryTier;
                masteryNextTierMethod = masteryNext;
                masteryBonusesMethod = masteryBonuses;
                masteryTierLabelMethod = masteryTierLabel;
                masteryTierEnum = tierEnum;
                forgeMapField = forgeMap;
                forgeSelectedItemField = forgeSelected;
                forgeLastResultField = forgeLastResult;
                forgeAllEquipmentMethod = forgeAllEquipment;
                doRefinementMethod = doRefinement;
                runicMapField = runicMap;
                runicSelectedItemField = runicSelected;
                runicSelectedSlotField = runicSlot;
                runicSelectedRankField = runicRank;
                runicAllEquipmentMethod = runicAllEquipment;
                runicAvailableRunesMethod = runicAvailable;
                findRuneDefAndRankMethod = findRuneDefAndRank;
                return true;
            });

        // ---- CompForgeEnhancement ----

        internal static object EnhancementOf(Thing thing)
        {
            if (!(thing is ThingWithComps withComps) || !Gate.Ensure())
                return null;
            foreach (ThingComp comp in withComps.AllComps)
            {
                if (EnhancementType.IsInstanceOfType(comp))
                    return comp;
            }
            return null;
        }

        internal static int RefinementLevel(object comp) => (int)refinementLevelField.GetValue(comp);
        internal static int MaxRuneSlots(object comp) => (int)maxRuneSlotsProperty.GetValue(comp, null);
        internal static int UsedRuneSlots(object comp) => (int)usedRuneSlotsProperty.GetValue(comp, null);
        internal static bool CanRefine(object comp) => (bool)canRefineMethod.Invoke(comp, null);
        internal static bool CanAddRune(object comp) => (bool)canAddRuneMethod.Invoke(comp, null);

        /// <summary>Applied runes as (def, rank) pairs, read off the comp's own tuple list.</summary>
        internal static List<KeyValuePair<Def, int>> AppliedRunes(object comp)
        {
            var result = new List<KeyValuePair<Def, int>>();
            IList tuples = appliedRunesWithRanksMethod.Invoke(comp, null) as IList;
            if (tuples == null)
                return result;
            foreach (object tuple in tuples)
            {
                Def rune = tuple.GetType().GetField("Item1")?.GetValue(tuple) as Def;
                object rank = tuple.GetType().GetField("Item2")?.GetValue(tuple);
                if (rune != null && rank != null)
                    result.Add(new KeyValuePair<Def, int>(rune, (int)rank));
            }
            return result;
        }

        internal static bool TryAddRune(object comp, Def rune, int rank) => (bool)tryAddRuneMethod.Invoke(comp, new object[] { rune, rank });
        internal static bool RemoveRuneAt(object comp, int index) => (bool)removeRuneAtMethod.Invoke(comp, new object[] { index });

        // ---- ForgeUtility ----

        internal sealed class RefineCost
        {
            public ThingDef CoreDef;
            public int CoreCount;
            public ThingDef SecondaryCoreDef;
            public int SecondaryCoreCount;
            public int Steel;
            public int Components;
            internal object Boxed;
        }

        internal static RefineCost GetRefineCost(int targetLevel)
        {
            object boxed = getRefineCostMethod.Invoke(null, new object[] { targetLevel });
            return new RefineCost
            {
                CoreDef = costCoreDefField.GetValue(boxed) as ThingDef,
                CoreCount = (int)costCoreCountField.GetValue(boxed),
                SecondaryCoreDef = costSecondaryDefField.GetValue(boxed) as ThingDef,
                SecondaryCoreCount = (int)costSecondaryCountField.GetValue(boxed),
                Steel = (int)costSteelField.GetValue(boxed),
                Components = (int)costComponentsField.GetValue(boxed),
                Boxed = boxed,
            };
        }

        internal static bool HasMaterials(Map map, RefineCost cost) => (bool)hasMaterialsMethod.Invoke(null, new[] { map, cost.Boxed });
        internal static float SuccessChance(int targetLevel) => (float)successChanceMethod.Invoke(null, new object[] { targetLevel, null });
        internal static float DowngradeChance(int targetLevel) => (float)downgradeChanceMethod.Invoke(null, new object[] { targetLevel });
        internal static bool NeedsRepair(Thing item) => (bool)needsRepairMethod.Invoke(null, new object[] { item });
        internal static int RepairEssenceCost(Thing item) => (int)repairCostMethod.Invoke(null, new object[] { item });
        internal static int LiveMaxHitPoints(Thing item) => (int)liveMaxHitPointsMethod.Invoke(null, new object[] { item });
        internal static bool RepairItem(Thing item, Map map) => (bool)repairItemMethod.Invoke(null, new object[] { item, map });
        internal static int CountOnMap(Map map, ThingDef def) => (int)countOnMapMethod.Invoke(null, new object[] { map, def });
        internal static ThingDef RefinementComponent => refinementComponentProperty.GetValue(null, null) as ThingDef;
        internal static float Bonus(string methodName, int level) => (float)bonusMethods[methodName].Invoke(null, new object[] { level });

        // ---- RuneDef ----

        /// <summary>RuneCategory ordinal: 0 = Weapon, 1 = Armor in the mod's enum.</summary>
        internal static int RuneCategoryOrdinal(Def rune) => Convert.ToInt32(runeCategoryField.GetValue(rune));
        internal static int RuneMaxRank(Def rune) => (int)runeMaxRankField.GetValue(rune);
        internal static string RuneDescriptionForRank(Def rune, int rank) => runeDescriptionForRankMethod.Invoke(rune, new object[] { rank }) as string;
        internal static string RomanNumeral(int rank) => romanNumeralMethod.Invoke(null, new object[] { rank }) as string;

        internal static IList AllRuneDefs()
        {
            return typeof(DefDatabase<>).MakeGenericType(RuneDefType).GetProperty("AllDefsListForReading")?.GetValue(null, null) as IList;
        }

        // ---- WeaponMasteryTracker ----

        internal static int MasteryXp(object tracker, string defName) => (int)masteryXpMethod.Invoke(tracker, new object[] { defName });
        internal static object MasteryTier(object tracker, string defName) => masteryTierMethod.Invoke(tracker, new object[] { defName });
        internal static int MasteryXpForNextTier(object tracker, string defName) => (int)masteryNextTierMethod.Invoke(tracker, new object[] { defName });
        internal static string MasteryTierLabel(object tier) => masteryTierLabelMethod.Invoke(null, new[] { tier }) as string;
        internal static bool MasteryTierIsMax(object tier) => Convert.ToInt32(tier) >= Enum.GetValues(masteryTierEnum).Length - 1;

        internal static void MasteryBonuses(object tracker, string defName, out float hitChance, out float attackSpeed, out float damage)
        {
            object[] args = { defName, 0f, 0f, 0f };
            masteryBonusesMethod.Invoke(tracker, args);
            hitChance = (float)args[1];
            attackSpeed = (float)args[2];
            damage = (float)args[3];
        }

        // ---- Window_Forge ----

        internal static Map ForgeMap(Window w) => forgeMapField.GetValue(w) as Map;
        internal static Thing ForgeSelectedItem(Window w) => forgeSelectedItemField.GetValue(w) as Thing;
        internal static string ForgeLastResult(Window w) => forgeLastResultField.GetValue(w) as string;
        internal static IList ForgeEquipment(Window w) => forgeAllEquipmentMethod.Invoke(w, null) as IList;

        internal static void ForgeSelect(Window w, Thing item)
        {
            // MUTATION-C: mirrors the list-row click in Window_Forge.DrawEquipmentList
            // (selectedItem = item; lastResultText = null), the window's own view state.
            forgeSelectedItemField.SetValue(w, item);
            forgeLastResultField.SetValue(w, null);
        }

        /// <summary>Vehicle A: the window's own refinement runner (attempt, result text, sound, mood).</summary>
        internal static void DoRefinement(Window w, object comp, int targetLevel) => doRefinementMethod.Invoke(w, new[] { comp, targetLevel });

        // ---- Window_RunicStation ----

        internal static Map RunicMap(Window w) => runicMapField.GetValue(w) as Map;
        internal static Thing RunicSelectedItem(Window w) => runicSelectedItemField.GetValue(w) as Thing;
        internal static int RunicSelectedSlot(Window w) => (int)runicSelectedSlotField.GetValue(w);
        internal static int RunicSelectedRank(Window w) => (int)runicSelectedRankField.GetValue(w);
        internal static IList RunicEquipment(Window w) => runicAllEquipmentMethod.Invoke(w, null) as IList;
        internal static IList RunicAvailableRuneItems(Window w, object comp) => runicAvailableRunesMethod.Invoke(w, new[] { comp }) as IList;

        internal static void RunicSelectItem(Window w, Thing item)
        {
            // MUTATION-C: mirrors the list-row click in Window_RunicStation.DrawEquipmentList
            // (selectedItem = item; selectedSlotIndex = -1), the window's own view state.
            runicSelectedItemField.SetValue(w, item);
            runicSelectedSlotField.SetValue(w, -1);
        }

        internal static void RunicSelectSlot(Window w, int index)
        {
            // MUTATION-C: mirrors the slot click in Window_RunicStation.DrawRuneSlots
            // (selectedSlotIndex = i), the window's own view state.
            runicSelectedSlotField.SetValue(w, index);
        }

        internal static void RunicSelectRank(Window w, int rank)
        {
            // MUTATION-C: mirrors the rank button in Window_RunicStation.DrawGodModeRunes
            // (selectedRank = r), the window's own view state.
            runicSelectedRankField.SetValue(w, rank);
        }

        internal static Def RuneDefForItem(string itemDefName, out int rank)
        {
            object tuple = findRuneDefAndRankMethod.Invoke(null, new object[] { itemDefName });
            rank = 1;
            if (tuple == null)
                return null;
            Def def = tuple.GetType().GetField("Item1")?.GetValue(tuple) as Def;
            object boxedRank = tuple.GetType().GetField("Item2")?.GetValue(tuple);
            if (boxedRank != null)
                rank = (int)boxedRank;
            return def;
        }
    }
}
