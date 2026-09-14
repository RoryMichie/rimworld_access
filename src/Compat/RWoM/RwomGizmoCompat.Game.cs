using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using RimWorld;
using Verse;

namespace RimWorldAccess
{
    /// <summary>
    /// Extension into the JecsAbilityCommandHandler seam
    /// (<see cref="IJecsAbilityExtension"/>) contributing RimWorld of Magic's
    /// autocast toggle from the ability GIZMO itself (map view, right-click).
    /// The card draws no autocast UI of its own at all — this is entirely a
    /// gizmo-side concern, normally drawn/toggled by TorannMagic.TM_Action.
    /// DrawAutoCastForGizmo's own ~40-branch, per-defName right-click ladder
    /// (TM_Action.cs:2881+). That ladder is NEVER replicated here: instead
    /// this resolves the gizmo's matching MagicPower/
    /// MightPower once via MagicData.ReturnMatchingMagicPower/MightData.
    /// ReturnMatchingMightPower and offers the toggle whenever that power's
    /// autocasting is configured, regardless of defName. This covers strictly
    /// MORE abilities than the mod's own ladder — several of its branches
    /// (TM_SuppressiveAura, TM_ProvisionerAura, TM_TaskMasterAura,
    /// TM_CommanderAura, TM_Nightshade) resolve a power but wire no right-click
    /// case at all, a mod oversight the sighted checkbox itself still draws
    /// for regardless. Offering the toggle uniformly is parity WITH that
    /// visible checkbox, not a deviation from it.
    /// </summary>
    internal sealed class RwomGizmoCompat : IJecsAbilityExtension
    {
        private readonly Type commandPawnAbilityType;   // AbilityUser.Command_PawnAbility
        private readonly FieldInfo commandPawnAbilityField; // Command_PawnAbility.pawnAbility
        private readonly PropertyInfo pawnAbilityDefProperty;        // PawnAbility.Def
        private readonly PropertyInfo pawnAbilityAbilityUserProperty; // PawnAbility.AbilityUser -> CompAbilityUser
        private readonly PropertyInfo pawnAbilityPawnProperty;        // PawnAbility.Pawn -> Verse.Pawn

        private readonly Type magicCompType;   // TorannMagic.CompAbilityUserMagic
        private readonly Type mightCompType;   // TorannMagic.CompAbilityUserMight
        private readonly PropertyInfo magicDataProperty; // comp.MagicData
        private readonly PropertyInfo mightDataProperty; // comp.MightData
        private readonly MethodInfo returnMatchingMagicPowerMethod; // MagicData.ReturnMatchingMagicPower(TMAbilityDef)
        private readonly MethodInfo returnMatchingMightPowerMethod; // MightData.ReturnMatchingMightPower(TMAbilityDef)

        private readonly FieldInfo magicPowerAutocastingField; // MagicPower.autocasting
        private readonly FieldInfo mightPowerAutocastingField; // MightPower.autocasting
        private readonly PropertyInfo magicPowerAutoCastProperty; // MagicPower.AutoCast (vehicle B setter)
        private readonly PropertyInfo mightPowerAutoCastProperty; // MightPower.AutoCast (vehicle B setter)

        private readonly FieldInfo incitePassionSkillField; // CompAbilityUserMagic.incitePassionSkill : SkillRecord

        private readonly bool ready;

        public static void TryRegister()
        {
            try
            {
                var compat = new RwomGizmoCompat();
                if (!compat.ready)
                    return; // Constructor already logged if this was a member-level failure.

                JecsAbilityCommandHandler.RegisterExtension(compat);
            }
            catch (Exception ex)
            {
                ModLogger.Error($"RimWorld of Magic gizmo compat registration failed: {ex.Message}");
            }
        }

        private RwomGizmoCompat()
        {
            var surface = new ReflectionSurface("RwomGizmoCompat");
            commandPawnAbilityType = surface.Type("AbilityUser.Command_PawnAbility");

            // Command_PawnAbility.pawnAbility's own FieldType is PawnAbility.
            commandPawnAbilityField = surface.Field(commandPawnAbilityType, "pawnAbility");
            Type pawnAbilityType = commandPawnAbilityField?.FieldType;
            pawnAbilityDefProperty = surface.Property(pawnAbilityType, "Def");
            pawnAbilityAbilityUserProperty = surface.Property(pawnAbilityType, "AbilityUser");
            pawnAbilityPawnProperty = surface.Property(pawnAbilityType, "Pawn");

            magicCompType = surface.Type("TorannMagic.CompAbilityUserMagic");
            mightCompType = surface.Type("TorannMagic.CompAbilityUserMight");
            Type magicDataType = surface.Type("TorannMagic.MagicData");
            Type mightDataType = surface.Type("TorannMagic.MightData");
            Type magicPowerType = surface.Type("TorannMagic.MagicPower");
            Type mightPowerType = surface.Type("TorannMagic.MightPower");
            Type tmAbilityDefType = surface.Type("TorannMagic.TMAbilityDef");

            magicDataProperty = surface.Property(magicCompType, "MagicData");
            mightDataProperty = surface.Property(mightCompType, "MightData");
            returnMatchingMagicPowerMethod = tmAbilityDefType != null
                ? surface.Method(magicDataType, "ReturnMatchingMagicPower", new[] { tmAbilityDefType })
                : null;
            returnMatchingMightPowerMethod = tmAbilityDefType != null
                ? surface.Method(mightDataType, "ReturnMatchingMightPower", new[] { tmAbilityDefType })
                : null;

            magicPowerAutocastingField = surface.Field(magicPowerType, "autocasting");
            mightPowerAutocastingField = surface.Field(mightPowerType, "autocasting");
            magicPowerAutoCastProperty = surface.Property(magicPowerType, "AutoCast");
            mightPowerAutoCastProperty = surface.Property(mightPowerType, "AutoCast");

            incitePassionSkillField = surface.Field(magicCompType, "incitePassionSkill");

            ready = surface.Ready;
        }

        public string TryGetStatusSuffix(Gizmo gizmo)
        {
            if (!ready || !commandPawnAbilityType.IsInstanceOfType(gizmo))
                return null;

            try
            {
                if (!TryResolveContext(gizmo, out object power, out bool isMight, out _, out _) || !HasAutocast(power, isMight))
                    return null;

                PropertyInfo autoCastProperty = isMight ? mightPowerAutoCastProperty : magicPowerAutoCastProperty;
                bool on = (bool)autoCastProperty.GetValue(power);
                return on ? "RimWorldAccess.Compat.Rwom.AutocastStatusOn".Translate() : "RimWorldAccess.Compat.Rwom.AutocastStatusOff".Translate();
            }
            catch (Exception ex)
            {
                ModLogger.Error($"RwomGizmoCompat.TryGetStatusSuffix failed: {ex.Message}");
                return null;
            }
        }

        public bool TryGetExtraOptions(Gizmo gizmo, List<FloatMenuOption> options)
        {
            if (!ready || !commandPawnAbilityType.IsInstanceOfType(gizmo))
                return false;

            try
            {
                if (!TryResolveContext(gizmo, out object power, out bool isMight, out Def def, out object pawnAbility))
                    return false;

                bool any = false;

                if (HasAutocast(power, isMight))
                {
                    PropertyInfo autoCastProperty = isMight ? mightPowerAutoCastProperty : magicPowerAutoCastProperty;
                    options.Add(BuildAutocastToggleOption(power, autoCastProperty, def));
                    any = true;
                }

                if (!isMight && def != null && def.defName == "TM_IncitePassion")
                {
                    object compObj = pawnAbilityAbilityUserProperty.GetValue(pawnAbility);
                    object pawnObj = pawnAbilityPawnProperty.GetValue(pawnAbility);
                    if (compObj != null && pawnObj is Pawn pawn && AddIncitePassionOptions(compObj, pawn, options))
                        any = true;
                }

                return any;
            }
            catch (Exception ex)
            {
                ModLogger.Error($"RwomGizmoCompat.TryGetExtraOptions failed: {ex.Message}");
                return false;
            }
        }

        private bool HasAutocast(object power, bool isMight)
        {
            if (power == null)
                return false;

            FieldInfo autocastingField = isMight ? mightPowerAutocastingField : magicPowerAutocastingField;
            return autocastingField.GetValue(power) != null;
        }

        /// <summary>
        /// Resolves the gizmo's PawnAbility, ability Def, owning comp, and — for a
        /// recognized magic/might comp — its MagicPower/MightPower via ReturnMatching*,
        /// null for abilities the pawn holds no power for. False when the gizmo isn't
        /// a RimWorld of Magic ability at all.
        /// </summary>
        private bool TryResolveContext(Gizmo gizmo, out object power, out bool isMight, out Def def, out object pawnAbility)
        {
            power = null;
            isMight = false;
            def = null;
            pawnAbility = null;

            pawnAbility = commandPawnAbilityField.GetValue(gizmo);
            if (pawnAbility == null)
                return false;

            object comp = pawnAbilityAbilityUserProperty.GetValue(pawnAbility);
            object abilityDefObj = pawnAbilityDefProperty.GetValue(pawnAbility);
            if (comp == null || abilityDefObj == null)
                return false;

            def = abilityDefObj as Def;

            if (magicCompType.IsInstanceOfType(comp))
            {
                object data = magicDataProperty.GetValue(comp);
                power = data == null ? null : returnMatchingMagicPowerMethod.Invoke(data, new object[] { abilityDefObj });
                isMight = false;
                return true;
            }
            if (mightCompType.IsInstanceOfType(comp))
            {
                object data = mightDataProperty.GetValue(comp);
                power = data == null ? null : returnMatchingMightPowerMethod.Invoke(data, new object[] { abilityDefObj });
                isMight = true;
                return true;
            }

            return false; // Some other AbilityUser-based mod's comp; not ours to extend.
        }

        private FloatMenuOption BuildAutocastToggleOption(object power, PropertyInfo autoCastProperty, Def def)
        {
            bool current = (bool)autoCastProperty.GetValue(power);
            string label = (current
                ? "RimWorldAccess.Compat.Rwom.ToggleAutocastOff"
                : "RimWorldAccess.Compat.Rwom.ToggleAutocastOn").Translate();
            return new FloatMenuOption(label, () => ToggleAutocast(power, autoCastProperty, def));
        }

        // Vehicle B: MagicPower.AutoCast / MightPower.AutoCast setter (the mod's
        // own 5-tick debounce) — the exact toggle TM_Action.DrawAutoCastForGizmo's
        // right-click branches call (e.g. TM_Action.cs:3016).
        private void ToggleAutocast(object power, PropertyInfo autoCastProperty, Def def)
        {
            try
            {
                bool current = (bool)autoCastProperty.GetValue(power);
                autoCastProperty.SetValue(power, !current);
                bool newState = (bool)autoCastProperty.GetValue(power); // re-read: the setter's own debounce may have ignored this call
                if (newState == current)
                {
                    // The setter's 5-tick debounce swallowed the write (its
                    // interactionTick only advances with game ticks, so a second
                    // toggle while paused is always dropped). Announce that
                    // nothing changed rather than restating the old state as if
                    // the toggle had taken.
                    TolkHelper.Speak("RimWorldAccess.Compat.Rwom.AutocastUnchanged".Loc(StateWord(newState), def?.LabelCap ?? ""));
                    return;
                }
                TolkHelper.Speak("RimWorldAccess.Compat.Rwom.AutocastToggled".Loc(StateWord(newState), def?.LabelCap ?? ""));
            }
            catch (Exception ex)
            {
                ModLogger.Error($"RwomGizmoCompat autocast toggle failed: {ex.Message}");
            }
        }

        // MUTATION-C: mirrors TorannMagic.TM_Action.DrawAutoCastForGizmo's Incite
        // Passion picker (TM_Action.cs:2984-3008); those closures are built
        // in-draw and unreachable from outside the IMGUI pass, so replicated here
        // against the same pawn.skills.skills passioned-skill list. No Try*/Can*
        // twin exists on either the comp or SkillRecord.
        private bool AddIncitePassionOptions(object comp, Pawn pawn, List<FloatMenuOption> options)
        {
            if (pawn.skills?.skills == null)
                return false;

            bool any = false;
            foreach (SkillRecord sr in pawn.skills.skills)
            {
                if (sr.passion == Passion.None)
                    continue;

                SkillRecord captured = sr;
                Action action = () =>
                {
                    incitePassionSkillField.SetValue(comp, captured);
                    Messages.Message("RimWorldAccess.Compat.Rwom.IncitePassionSelected".Translate(captured.def.label), MessageTypeDefOf.NeutralEvent);
                };
                var fmo = new FloatMenuOption(captured.def.LabelCap + " (" + captured.passion.ToString() + ")", action, MenuOptionPriority.Low, null, null);
                fmo.orderInPriority = 991; // Matches TM_Action.cs:2999's own required ordering (kept against a "StillValid" patch).
                options.Add(fmo);
                any = true;
            }
            return any;
        }

        private static string StateWord(bool on) => on ? "On".Translate().ToString() : "Off".Translate().ToString();
    }
}
