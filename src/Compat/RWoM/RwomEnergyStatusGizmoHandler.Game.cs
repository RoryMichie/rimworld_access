using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;

namespace RimWorldAccess
{
    /// <summary>
    /// <c>TorannMagic.Gizmo_EnergyStatus</c>: mirrors its
    /// <c>GizmoOnGUI</c> draw order exactly (Gizmo_EnergyStatus.cs:41-360) —
    /// up to ten stacked FillableBars, each with a "cur / max" label and no
    /// gizmo-level label or tooltip of its own (<c>GizmoResult(Clear)</c>,
    /// :383). Bars, in the source's own order: a custom class hediff, psionic,
    /// hate ("death knight"), chi (monk), stamina (fighter), blood (blood
    /// mage), light energy (brightmage), mana (mage), spirit, and necrotic
    /// energy (enchanted item). Each bar is wrapped in its own try/catch,
    /// matching the source's own per-bar try/catch around each
    /// FillableBar+Label pair — one broken bar drops silently from the
    /// readout rather than losing every other bar. The source's SoL bar
    /// (:284-289, drawn without its own label just before the
    /// HediffComp_LightCapacitance bar in the same rect) carries no distinct
    /// reading of its own and is not reported separately; the
    /// HediffComp_LightCapacitance bar it sits beneath (:290-294) is the one
    /// bar with an actual "cur / max" label, cited here as EnergyBarLight.
    /// </summary>
    internal sealed class RwomEnergyStatusGizmoHandler : GizmoHandlerBase
    {
        private readonly Type gizmoType;
        private readonly FieldInfo pawnField;           // Gizmo_EnergyStatus.pawn : Pawn
        private readonly FieldInfo iCompField;          // Gizmo_EnergyStatus.iComp : Enchantment.CompEnchantedItem
        private readonly PropertyInfo necroticEnergyProperty; // CompEnchantedItem.NecroticEnergy

        private readonly Type compMagicType;
        private readonly Type compMightType;
        private readonly PropertyInfo isMagicUserProperty;
        private readonly PropertyInfo isMightUserProperty;
        private readonly FieldInfo magicCustomClassField;
        private readonly FieldInfo mightCustomClassField;
        private readonly PropertyInfo manaProperty;     // CompAbilityUserMagic.Mana : Need_Mana
        private readonly FieldInfo maxMPField;
        private readonly PropertyInfo staminaProperty;  // CompAbilityUserMight.Stamina : Need_Stamina
        private readonly FieldInfo maxSPField;

        private readonly Type customClassType;
        private readonly FieldInfo classHediffField;        // TM_CustomClass.classHediff : HediffDef
        private readonly FieldInfo showHediffOnGizmoField;  // TM_CustomClass.showHediffOnGizmo : bool -- MAY BE NULL (shipped-DLL drift); missing == always shown

        private readonly Type hediffWithCompsExtraType;
        private readonly PropertyInfo maxSeverityProperty;

        private readonly Type lightCapacitanceType;
        private readonly PropertyInfo lightEnergyProperty;
        private readonly PropertyInfo lightEnergyMaxProperty;

        private readonly MethodInfo isPossessedByOrIsSpiritMethod;

        private readonly TraitDef facelessTrait;         // soft: absence only widens the mage/mana bar's inclusion, never breaks it
        private readonly HediffDef psionicHD;
        private readonly HediffDef hateHD;
        private readonly HediffDef chiHD;
        private readonly HediffDef bloodHD;
        private readonly HediffDef lightCapacitanceHD;
        private readonly HediffDef psionicBoostHD;       // soft: a missing boost hediff just means no boost is ever applied
        private readonly HediffDef hateBoostHD;
        private readonly HediffDef bloodBoostHD;
        private readonly NeedDef spiritNeedDef;

        private readonly bool ready;

        public static void TryRegister()
        {
            try
            {
                Type gizmoType = AccessTools.TypeByName("TorannMagic.Gizmo_EnergyStatus");
                if (gizmoType == null)
                {
                    return; // Silent: the golems/gizmo feature isn't present.
                }

                var handler = new RwomEnergyStatusGizmoHandler(gizmoType);
                if (!handler.ready)
                {
                    return; // Constructor already logged the missing member(s).
                }

                GizmoHandlerRegistry.Register(gizmoType, handler);
            }
            catch (Exception ex)
            {
                ModLogger.Error($"RwomGolemCompat energy status gizmo registration failed: {ex.Message}");
            }
        }

        private RwomEnergyStatusGizmoHandler(Type gizmoType)
        {
            this.gizmoType = gizmoType;

            var surface = new ReflectionSurface("RwomEnergyStatusGizmoHandler");
            surface.Supplied("TorannMagic.Gizmo_EnergyStatus", gizmoType);
            pawnField = surface.Field(gizmoType, "pawn");
            iCompField = surface.Field(gizmoType, "iComp");
            necroticEnergyProperty = surface.Property(iCompField?.FieldType, "NecroticEnergy");

            compMagicType = surface.Type("TorannMagic.CompAbilityUserMagic");
            compMightType = surface.Type("TorannMagic.CompAbilityUserMight");
            isMagicUserProperty = surface.Property(compMagicType, "IsMagicUser");
            isMightUserProperty = surface.Property(compMightType, "IsMightUser");
            magicCustomClassField = surface.Field(compMagicType, "customClass");
            mightCustomClassField = surface.Field(compMightType, "customClass");
            manaProperty = surface.Property(compMagicType, "Mana");
            maxMPField = surface.Field(compMagicType, "maxMP");
            staminaProperty = surface.Property(compMightType, "Stamina");
            maxSPField = surface.Field(compMightType, "maxSP");

            customClassType = surface.Supplied("TM_CustomClass",
                magicCustomClassField?.FieldType ?? mightCustomClassField?.FieldType);
            classHediffField = surface.Field(customClassType, "classHediff");

            hediffWithCompsExtraType = surface.Type("TorannMagic.HediffWithCompsExtra");
            maxSeverityProperty = surface.Property(hediffWithCompsExtraType, "MaxSeverity");

            lightCapacitanceType = surface.Type("TorannMagic.HediffComp_LightCapacitance");
            lightEnergyProperty = surface.Property(lightCapacitanceType, "LightEnergy");
            lightEnergyMaxProperty = surface.Property(lightCapacitanceType, "LightEnergyMax");

            Type calcType = surface.Type("TorannMagic.TM_Calc");
            isPossessedByOrIsSpiritMethod = surface.Method(calcType, "IsPossessedByOrIsSpirit", new[] { typeof(Pawn) });

            psionicHD = DefDatabase<HediffDef>.GetNamedSilentFail("TM_PsionicHD");
            hateHD = DefDatabase<HediffDef>.GetNamedSilentFail("TM_HateHD");
            chiHD = DefDatabase<HediffDef>.GetNamedSilentFail("TM_ChiHD");
            bloodHD = DefDatabase<HediffDef>.GetNamedSilentFail("TM_BloodHD");
            lightCapacitanceHD = DefDatabase<HediffDef>.GetNamedSilentFail("TM_LightCapacitanceHD");
            spiritNeedDef = DefDatabase<NeedDef>.GetNamedSilentFail("TM_SpiritND");

            // Deliberately optional: showHediffOnGizmo (absent from the shipped DLL
            // despite existing in the on-disk source -- treated as "always shown"),
            // the Faceless trait, and the three boost hediffs each narrow a single
            // bar's condition or boost term rather than the whole readout.
            showHediffOnGizmoField = customClassType != null
                ? AccessTools.Field(customClassType, "showHediffOnGizmo")
                : null;
            facelessTrait = DefDatabase<TraitDef>.GetNamedSilentFail("Faceless");
            psionicBoostHD = DefDatabase<HediffDef>.GetNamedSilentFail("TM_Artifact_PsionicBoostHD");
            hateBoostHD = DefDatabase<HediffDef>.GetNamedSilentFail("TM_Artifact_HateBoostHD");
            bloodBoostHD = DefDatabase<HediffDef>.GetNamedSilentFail("TM_Artifact_BloodBoostHD");

            var missingDefs = new List<string>();
            RequireDef(missingDefs, "TM_PsionicHD", psionicHD);
            RequireDef(missingDefs, "TM_HateHD", hateHD);
            RequireDef(missingDefs, "TM_ChiHD", chiHD);
            RequireDef(missingDefs, "TM_BloodHD", bloodHD);
            RequireDef(missingDefs, "TM_LightCapacitanceHD", lightCapacitanceHD);
            RequireDef(missingDefs, "TM_SpiritND", spiritNeedDef);

            ready = surface.Ready && missingDefs.Count == 0;
            if (surface.Ready && missingDefs.Count > 0)
            {
                ModLogger.Error($"RwomEnergyStatusGizmoHandler: could not resolve the defs {string.Join(", ", missingDefs)}; " +
                    "declining the energy status gizmo readout.");
            }
        }

        private static void RequireDef(List<string> missing, string defName, Def def)
        {
            if (def == null)
            {
                missing.Add(defName);
            }
        }

        public override bool TryGetLabel(Gizmo gizmo, out string label)
        {
            label = null;
            if (!ready || !gizmoType.IsInstanceOfType(gizmo))
            {
                return false;
            }
            label = "RimWorldAccess.Compat.Rwom.EnergyStatusLabel".Translate();
            return true;
        }

        public override bool TryGetStatus(Gizmo gizmo, out string status)
        {
            status = null;
            if (!ready || !gizmoType.IsInstanceOfType(gizmo))
            {
                return false;
            }

            try
            {
                var pawn = pawnField.GetValue(gizmo) as Pawn;
                if (pawn == null || pawn.Dead || pawn.Destroyed || pawn.health?.hediffSet == null)
                {
                    return false; // Matches the source's own "not DestroyedOrNull && not Dead" gate (:43) -- no readout otherwise.
                }

                object iComp = iCompField.GetValue(gizmo);
                object compMagic = RwomGolemCompat.FindComp(pawn, compMagicType);
                object compMight = RwomGolemCompat.FindComp(pawn, compMightType);

                bool isMage = compMagic != null && (bool)isMagicUserProperty.GetValue(compMagic)
                    && (facelessTrait == null || pawn.story?.traits == null || !pawn.story.traits.HasTrait(facelessTrait));
                bool isFighter = compMight != null && (bool)isMightUserProperty.GetValue(compMight);
                bool isPsionic = pawn.health.hediffSet.HasHediff(psionicHD);
                bool isDeathKnight = hateHD != null && pawn.health.hediffSet.HasHediff(hateHD);
                bool isBloodMage = pawn.health.hediffSet.HasHediff(bloodHD);
                bool isMonk = pawn.health.hediffSet.HasHediff(chiHD);
                bool isBrightmage = lightCapacitanceHD != null && pawn.health.hediffSet.HasHediff(lightCapacitanceHD);
                bool isSpirit = (bool)isPossessedByOrIsSpiritMethod.Invoke(null, new object[] { pawn });
                bool isEnchantedItem = iComp != null;

                object customClass = null;
                if (isMage)
                {
                    customClass = magicCustomClassField.GetValue(compMagic);
                }
                else if (isFighter)
                {
                    customClass = mightCustomClassField.GetValue(compMight);
                }

                var fragments = new List<string>();

                if (customClass != null)
                {
                    TryAppendCustomClassBar(customClass, pawn, fragments);
                }
                if (isPsionic)
                {
                    TryAppendSeverityBar(pawn, psionicHD, psionicBoostHD, "RimWorldAccess.Compat.Rwom.EnergyBarPsionic", fragments);
                }
                if (isDeathKnight)
                {
                    TryAppendSeverityBar(pawn, hateHD, hateBoostHD, "RimWorldAccess.Compat.Rwom.EnergyBarHate", fragments);
                }
                if (isMonk)
                {
                    TryAppendSeverityBar(pawn, chiHD, null, "RimWorldAccess.Compat.Rwom.EnergyBarChi", fragments);
                }
                if (isFighter)
                {
                    TryAppendNeedInstantBar(compMight, staminaProperty, maxSPField, "RimWorldAccess.Compat.Rwom.EnergyBarStamina", fragments);
                }
                if (isBloodMage)
                {
                    TryAppendSeverityBar(pawn, bloodHD, bloodBoostHD, "RimWorldAccess.Compat.Rwom.EnergyBarBlood", fragments);
                }
                if (isBrightmage)
                {
                    TryAppendLightBar(pawn, fragments);
                }
                if (isMage)
                {
                    TryAppendNeedInstantBar(compMagic, manaProperty, maxMPField, "RimWorldAccess.Compat.Rwom.EnergyBarMana", fragments);
                }
                if (isSpirit)
                {
                    TryAppendSpiritBar(pawn, fragments);
                }
                if (isEnchantedItem)
                {
                    TryAppendNecroticBar(iComp, fragments);
                }

                if (fragments.Count == 0)
                {
                    return false;
                }
                status = string.Join(". ", fragments);
                return true;
            }
            catch (Exception ex)
            {
                ModLogger.Error($"RwomEnergyStatusGizmoHandler.TryGetStatus failed: {ex.Message}");
                return false;
            }
        }

        private static string BarFragment(string nameKey, float cur, float max)
        {
            return "RimWorldAccess.Compat.Rwom.EnergyBarFragment".Translate(
                nameKey.Translate(), Mathf.RoundToInt(cur), Mathf.RoundToInt(max)).ToString();
        }

        /// <summary>Custom class hediff bar (Gizmo_EnergyStatus.cs:58-72 selection, :176-192 draw).</summary>
        private void TryAppendCustomClassBar(object customClass, Pawn pawn, List<string> fragments)
        {
            try
            {
                var classHediffDef = classHediffField.GetValue(customClass) as HediffDef;
                if (classHediffDef == null)
                {
                    return;
                }
                bool shown = showHediffOnGizmoField == null || (bool)showHediffOnGizmoField.GetValue(customClass);
                if (!shown)
                {
                    return;
                }
                Hediff hediff = pawn.health.hediffSet.GetFirstHediffOfDef(classHediffDef);
                if (!hediffWithCompsExtraType.IsInstanceOfType(hediff))
                {
                    return;
                }
                float max = (float)maxSeverityProperty.GetValue(hediff);
                fragments.Add(BarFragment("RimWorldAccess.Compat.Rwom.EnergyBarCustomClass", hediff.Severity, max));
            }
            catch (Exception ex)
            {
                ModLogger.Error($"RwomEnergyStatusGizmoHandler custom class bar failed: {ex.Message}");
            }
        }

        /// <summary>
        /// A hediff-severity bar out of a fixed 100 plus an optional boost hediff's own Severity added to that max
        /// (psionic: :100-105/193-208; hate: :106-114/210-226; chi: :132-135/227-243, no boost; blood: :115-123/262-278).
        /// </summary>
        private void TryAppendSeverityBar(Pawn pawn, HediffDef hediffDef, HediffDef boostHediffDef, string nameKey, List<string> fragments)
        {
            try
            {
                if (hediffDef == null)
                {
                    return;
                }
                Hediff hediff = pawn.health.hediffSet.GetFirstHediffOfDef(hediffDef);
                if (hediff == null)
                {
                    return;
                }
                float max = 100f;
                if (boostHediffDef != null)
                {
                    Hediff boost = pawn.health.hediffSet.GetFirstHediffOfDef(boostHediffDef);
                    if (boost != null)
                    {
                        max += boost.Severity;
                    }
                }
                fragments.Add(BarFragment(nameKey, hediff.Severity, max));
            }
            catch (Exception ex)
            {
                ModLogger.Error($"RwomEnergyStatusGizmoHandler severity bar ({nameKey}) failed: {ex.Message}");
            }
        }

        /// <summary>Stamina/mana bars: <c>Need.CurInstantLevel</c> (vanilla base member, TorannMagic's Need_Stamina/Need_Mana override it) times 100 over maxSP/maxMP times 100 (:244-261 stamina, :304-320 mana).</summary>
        private void TryAppendNeedInstantBar(object comp, PropertyInfo needProperty, FieldInfo maxField, string nameKey, List<string> fragments)
        {
            try
            {
                var need = needProperty.GetValue(comp) as Need;
                if (need == null)
                {
                    return;
                }
                float cur = need.CurInstantLevel * 100f;
                float max = (float)maxField.GetValue(comp) * 100f;
                fragments.Add(BarFragment(nameKey, cur, max));
            }
            catch (Exception ex)
            {
                ModLogger.Error($"RwomEnergyStatusGizmoHandler need-instant bar ({nameKey}) failed: {ex.Message}");
            }
        }

        /// <summary>Brightmage: the HediffComp_LightCapacitance on the TM_LightCapacitanceHD hediff (:290-294); LightEnergy/LightEnergyMax are both properties.</summary>
        private void TryAppendLightBar(Pawn pawn, List<string> fragments)
        {
            try
            {
                Hediff hd = pawn.health.hediffSet.GetFirstHediffOfDef(lightCapacitanceHD);
                if (!(hd is HediffWithComps hwc) || hwc.comps == null)
                {
                    return;
                }
                object hdlc = null;
                foreach (HediffComp comp in hwc.comps)
                {
                    if (lightCapacitanceType.IsInstanceOfType(comp))
                    {
                        hdlc = comp;
                        break;
                    }
                }
                if (hdlc == null)
                {
                    return;
                }
                float cur = (float)lightEnergyProperty.GetValue(hdlc);
                float max = (float)lightEnergyMaxProperty.GetValue(hdlc);
                fragments.Add(BarFragment("RimWorldAccess.Compat.Rwom.EnergyBarLight", cur, max));
            }
            catch (Exception ex)
            {
                ModLogger.Error($"RwomEnergyStatusGizmoHandler light bar failed: {ex.Message}");
            }
        }

        /// <summary>Spirit: the TM_SpiritND need's own CurLevel/MaxLevel (vanilla Need base members) (:321-338).</summary>
        private void TryAppendSpiritBar(Pawn pawn, List<string> fragments)
        {
            try
            {
                Need need = pawn.needs?.TryGetNeed(spiritNeedDef);
                if (need == null)
                {
                    return;
                }
                fragments.Add(BarFragment("RimWorldAccess.Compat.Rwom.EnergyBarSpirit", need.CurLevel, need.MaxLevel));
            }
            catch (Exception ex)
            {
                ModLogger.Error($"RwomEnergyStatusGizmoHandler spirit bar failed: {ex.Message}");
            }
        }

        /// <summary>Enchanted item: iComp.NecroticEnergy out of a fixed 100 (:339-355).</summary>
        private void TryAppendNecroticBar(object iComp, List<string> fragments)
        {
            try
            {
                float cur = (float)necroticEnergyProperty.GetValue(iComp);
                fragments.Add(BarFragment("RimWorldAccess.Compat.Rwom.EnergyBarNecrotic", cur, 100f));
            }
            catch (Exception ex)
            {
                ModLogger.Error($"RwomEnergyStatusGizmoHandler necrotic bar failed: {ex.Message}");
            }
        }
    }
}
