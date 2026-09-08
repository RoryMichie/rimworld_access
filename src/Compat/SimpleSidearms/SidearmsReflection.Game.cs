using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using UnityEngine;
using Verse;

namespace RimWorldAccess
{
    /// <summary>
    /// Simple Sidearms' reflection surface, resolved once at activation (never hard-typereffed).
    /// Mutations ride the mod's own dispatcher: set the public interaction fields it reads, then
    /// invoke handleInteraction with a synthesized left- or right-click Event.
    /// </summary>
    internal static class SidearmsReflection
    {
        public static Type GizmoType;
        public static Type InteractionType;

        public static FieldInfo GizmoParent;
        public static FieldInfo GizmoCarriedRanged;
        public static FieldInfo GizmoCarriedMelee;
        public static FieldInfo GizmoRangedMemories;
        public static FieldInfo GizmoMeleeMemories;
        public static FieldInfo GizmoInteractionWeapon;
        public static FieldInfo GizmoInteractionWeaponType;
        public static FieldInfo GizmoInteractionAsOffhand;
        public static FieldInfo GizmoInteractionIsDuplicate;
        public static MethodInfo GizmoHandleInteraction;

        public static MethodInfo CompGetForPawn;
        public static FieldInfo CompPrimaryWeaponMode;
        public static PropertyInfo CompForcedWeapon;
        public static PropertyInfo CompForcedWeaponWhileDrafted;
        public static PropertyInfo CompDefaultRangedWeapon;
        public static PropertyInfo CompPreferredMeleeWeapon;
        public static PropertyInfo CompPreferredUnarmed;
        public static PropertyInfo CompForcedUnarmed;
        public static PropertyInfo CompForcedUnarmedWhileDrafted;

        public static MethodInfo ExtToPair;
        public static MethodInfo ExtPairLabelCap;
        public static MethodInfo ExtIsToolNotWeapon;
        public static MethodInfo ExtSkillWeaponPreference;

        public static MethodInfo StatCanUseSidearmInstance;
        public static MethodInfo FilterIsManualUse;
        public static MethodInfo FilterIsEmpWeapon;
        public static MethodInfo FilterIsDangerousWeapon;

        public static PropertyInfo ModSettings;
        public static PropertyInfo ModSingleton;
        public static FieldInfo SettingsEverOpened;
        public static FieldInfo SettingsAllowBlockedWeaponUse;
        public static FieldInfo SettingsFumbleRecoveryChance;
        public static MethodInfo SettingsApplyPreset;
        public static Type SettingsPresetType;

        public static FieldInfo PairThing;

        public static FieldInfo TacticowlActive;
        public static FieldInfo TacticowlIsOffHand;
        public static FieldInfo TacticowlCanBeOffHand;
        public static FieldInfo TacticowlIsTwoHanded;
        public static FieldInfo TacticowlDualWieldActive;
        public static FieldInfo VfeActive;
        public static FieldInfo VfeOffHandShield;

        public static bool Resolve()
        {
            try
            {
                GizmoType = AccessTools.TypeByName("SimpleSidearms.rimworld.Gizmo_SidearmsList");
                InteractionType = GizmoType?.GetNestedType("SidearmsListInteraction");
                Type compType = AccessTools.TypeByName("SimpleSidearms.rimworld.CompSidearmMemory");
                Type extType = AccessTools.TypeByName("PeteTimesSix.SimpleSidearms.Extensions");
                Type statType = AccessTools.TypeByName("PeteTimesSix.SimpleSidearms.Utilities.StatCalculator");
                Type filterType = AccessTools.TypeByName("PeteTimesSix.SimpleSidearms.Utilities.GettersFilters");
                Type modType = AccessTools.TypeByName("PeteTimesSix.SimpleSidearms.SimpleSidearms");
                Type settingsType = AccessTools.TypeByName("PeteTimesSix.SimpleSidearms.SimpleSidearms_Settings");
                Type tacticowlType = AccessTools.TypeByName("PeteTimesSix.SimpleSidearms.Compat.Tacticowl");
                if (GizmoType == null || InteractionType == null || compType == null || extType == null
                    || statType == null || filterType == null || modType == null || settingsType == null)
                {
                    return false;
                }

                GizmoParent = AccessTools.Field(GizmoType, "parent");
                GizmoCarriedRanged = AccessTools.Field(GizmoType, "carriedRangedWeapons");
                GizmoCarriedMelee = AccessTools.Field(GizmoType, "carriedMeleeWeapons");
                GizmoRangedMemories = AccessTools.Field(GizmoType, "rangedWeaponMemories");
                GizmoMeleeMemories = AccessTools.Field(GizmoType, "meleeWeaponMemories");
                GizmoInteractionWeapon = AccessTools.Field(GizmoType, "interactionWeapon");
                GizmoInteractionWeaponType = AccessTools.Field(GizmoType, "interactionWeaponType");
                GizmoInteractionAsOffhand = AccessTools.Field(GizmoType, "interactionAsOffhand");
                GizmoInteractionIsDuplicate = AccessTools.Field(GizmoType, "interactionWeaponIsDuplicate");
                GizmoHandleInteraction = AccessTools.Method(GizmoType, "handleInteraction");

                CompGetForPawn = AccessTools.Method(compType, "GetMemoryCompForPawn");
                CompPrimaryWeaponMode = AccessTools.Field(compType, "primaryWeaponMode");
                CompForcedWeapon = AccessTools.Property(compType, "ForcedWeapon");
                CompForcedWeaponWhileDrafted = AccessTools.Property(compType, "ForcedWeaponWhileDrafted");
                CompDefaultRangedWeapon = AccessTools.Property(compType, "DefaultRangedWeapon");
                CompPreferredMeleeWeapon = AccessTools.Property(compType, "PreferredMeleeWeapon");
                CompPreferredUnarmed = AccessTools.Property(compType, "PreferredUnarmed");
                CompForcedUnarmed = AccessTools.Property(compType, "ForcedUnarmed");
                CompForcedUnarmedWhileDrafted = AccessTools.Property(compType, "ForcedUnarmedWhileDrafted");

                ExtToPair = AccessTools.Method(extType, "toThingDefStuffDefPair");
                ExtPairLabelCap = AccessTools.Method(extType, "getLabelCap");
                ExtIsToolNotWeapon = AccessTools.Method(extType, "isToolNotWeapon");
                ExtSkillWeaponPreference = AccessTools.Method(extType, "getSkillWeaponPreference");

                StatCanUseSidearmInstance = AccessTools.Method(statType, "canUseSidearmInstance");
                FilterIsManualUse = AccessTools.Method(filterType, "isManualUse");
                FilterIsEmpWeapon = AccessTools.Method(filterType, "isEMPWeapon");
                FilterIsDangerousWeapon = AccessTools.Method(filterType, "isDangerousWeapon");

                ModSettings = AccessTools.Property(modType, "Settings");
                ModSingleton = AccessTools.Property(modType, "ModSingleton");
                SettingsEverOpened = AccessTools.Field(settingsType, "SettingsEverOpened");
                SettingsAllowBlockedWeaponUse = AccessTools.Field(settingsType, "AllowBlockedWeaponUse");
                SettingsFumbleRecoveryChance = AccessTools.Field(settingsType, "FumbleRecoveryChance");
                SettingsApplyPreset = AccessTools.Method(settingsType, "ApplyPreset");
                SettingsPresetType = AccessTools.TypeByName("PeteTimesSix.SimpleSidearms.Utilities.Enums")
                    ?.GetNestedType("SettingsPreset");

                Type pairType = AccessTools.TypeByName("SimpleSidearms.rimworld.ThingDefStuffDefPair");
                PairThing = pairType != null ? AccessTools.Field(pairType, "thing") : null;

                if (tacticowlType != null)
                {
                    TacticowlActive = AccessTools.Field(tacticowlType, "active");
                    TacticowlIsOffHand = AccessTools.Field(tacticowlType, "isOffHand");
                    TacticowlCanBeOffHand = AccessTools.Field(tacticowlType, "canBeOffHand");
                    TacticowlIsTwoHanded = AccessTools.Field(tacticowlType, "isTwoHanded");
                    TacticowlDualWieldActive = AccessTools.Field(tacticowlType, "dualWieldActive");
                }
                Type vfeType = AccessTools.TypeByName("PeteTimesSix.SimpleSidearms.Compat.VFECore");
                if (vfeType != null)
                {
                    VfeActive = AccessTools.Field(vfeType, "active");
                    VfeOffHandShield = AccessTools.Field(vfeType, "offHandShield");
                }

                return GizmoParent != null && GizmoCarriedRanged != null && GizmoCarriedMelee != null
                    && GizmoRangedMemories != null && GizmoMeleeMemories != null
                    && GizmoInteractionWeapon != null && GizmoInteractionWeaponType != null
                    && GizmoInteractionAsOffhand != null && GizmoHandleInteraction != null
                    && CompGetForPawn != null && CompPrimaryWeaponMode != null
                    && ExtToPair != null && ExtPairLabelCap != null
                    && StatCanUseSidearmInstance != null && ModSettings != null;
            }
            catch (Exception ex)
            {
                ModLogger.Error($"SimpleSidearms reflection resolve failed: {ex.Message}");
                return false;
            }
        }

        public static object Settings => ModSettings.GetValue(null);

        public static bool TacticowlIsActive => TacticowlActive != null && (bool)TacticowlActive.GetValue(null);

        /// <summary>Whether the mod (or Tacticowl) holds this carried weapon as an offhand.</summary>
        public static bool IsOffHand(ThingWithComps weapon)
        {
            if (!TacticowlIsActive || TacticowlIsOffHand == null)
            {
                return false;
            }
            Delegate d = TacticowlIsOffHand.GetValue(null) as Delegate;
            return d != null && (bool)d.DynamicInvoke(weapon);
        }

        public static bool CanBeOffHand(ThingDef def)
        {
            Delegate d = TacticowlCanBeOffHand?.GetValue(null) as Delegate;
            return d != null && (bool)d.DynamicInvoke(def);
        }

        public static bool IsTwoHanded(ThingDef def)
        {
            Delegate d = TacticowlIsTwoHanded?.GetValue(null) as Delegate;
            return d != null && (bool)d.DynamicInvoke(def);
        }

        /// <summary>Tacticowl's dual-wield setting; its ref-returning FieldRef defeats
        /// DynamicInvoke, and the other equip-as-offhand gates still hold on the true default.</summary>
        public static bool DualWieldActive()
        {
            try
            {
                Delegate d = TacticowlDualWieldActive?.GetValue(null) as Delegate;
                return d == null || (bool)d.DynamicInvoke();
            }
            catch
            {
                return true;
            }
        }

        public static bool VfeIsActive => VfeActive != null && (bool)VfeActive.GetValue(null);

        public static ThingWithComps OffHandShield(Pawn pawn)
        {
            Delegate d = VfeOffHandShield?.GetValue(null) as Delegate;
            return d != null ? d.DynamicInvoke(pawn) as ThingWithComps : null;
        }

        public static object ToPair(Thing weapon)
        {
            return ExtToPair.Invoke(null, new object[] { weapon });
        }

        public static ThingDef PairThingDef(object pair)
        {
            return pair != null ? PairThing?.GetValue(pair) as ThingDef : null;
        }

        public static string PairLabelCap(object pair)
        {
            return (string)ExtPairLabelCap.Invoke(null, new[] { pair });
        }

        public static bool IsToolNotWeapon(object pair)
        {
            return (bool)ExtIsToolNotWeapon.Invoke(null, new[] { pair });
        }

        public static object GetMemoryComp(Pawn pawn)
        {
            return CompGetForPawn.Invoke(null, new object[] { pawn, true });
        }

        /// <summary>Fires one gizmo sub-interaction through the mod's own dispatcher.</summary>
        public static void FireInteraction(Gizmo gizmo, string interaction, int mouseButton,
            ThingWithComps weapon = null, object weaponType = null, bool asOffhand = false, bool isDuplicate = false)
        {
            // MUTATION-C: mirrors Gizmo_SidearmsList.ProcessInput — stages the public click fields, then handleInteraction runs the mod's own gated mutation; A/B impossible, the fields are only set from cursor hit tests mid-draw.
            GizmoInteractionWeapon.SetValue(gizmo, weapon);
            GizmoInteractionWeaponType.SetValue(gizmo, weaponType);
            GizmoInteractionAsOffhand.SetValue(gizmo, asOffhand);
            GizmoInteractionIsDuplicate?.SetValue(gizmo, isDuplicate);
            object interactionValue = Enum.Parse(InteractionType, interaction);
            Event ev = new Event { button = mouseButton };
            GizmoHandleInteraction.Invoke(gizmo, new[] { interactionValue, (object)ev });
        }

        /// <summary>Boxed-pair value equality; both sides may be null (an unset nullable).</summary>
        public static bool PairEquals(object a, object b)
        {
            return a == null ? b == null : a.Equals(b);
        }

        /// <summary>Snapshot of a mod-typed List&lt;ThingDefStuffDefPair&gt; as boxed values.</summary>
        public static List<object> SnapshotPairs(object modList)
        {
            List<object> result = new List<object>();
            if (modList is IEnumerable enumerable)
            {
                foreach (object item in enumerable)
                {
                    result.Add(item);
                }
            }
            return result;
        }
    }
}
