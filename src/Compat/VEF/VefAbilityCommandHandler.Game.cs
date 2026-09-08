using System;
using System.Collections.Generic;
using System.Reflection;
using RimWorld;
using UnityEngine;
using Verse;

namespace RimWorldAccess
{
    /// <summary>
    /// Gizmo handler for VEF.Abilities.Command_Ability (Vanilla Expanded
    /// Framework's ability command, also the base VPE's psycast commands
    /// derive from — the byType chain covers those automatically). Mirrors
    /// VEF/Abilities/Commands/Command_Ability.cs's GizmoOnGUIInt overlays
    /// (cooldown percent, auto-cast icon) and VEF/Abilities/Misc/Ability.cs's
    /// DoAction, whose `Event.current.button == 1` branch toggles auto-cast —
    /// the same branch a sighted player's right-click on the gizmo runs.
    ///
    /// No VEF assembly reference: every member is resolved once here against
    /// the Type handed in by VefGizmoCompat, and every facet declines (rather
    /// than throws) if resolution or a read/invoke fails.
    /// </summary>
    internal sealed class VefAbilityCommandHandler : GizmoHandlerBase
    {
        private readonly Type commandType;
        private readonly FieldInfo abilityField;
        private readonly FieldInfo cooldownField;
        private readonly MethodInfo getCooldownForPawnMethod;
        private readonly PropertyInfo autoCastProperty;
        private readonly PropertyInfo canAutoCastProperty;
        private readonly MethodInfo doActionMethod;
        private readonly bool ready;

        public VefAbilityCommandHandler(Type commandAbilityType)
        {
            commandType = commandAbilityType;

            var surface = new ReflectionSurface("VefAbilityCommandHandler");
            surface.Supplied("VEF.Abilities.Command_Ability", commandAbilityType);

            // The public `ability` field's own FieldType is the Ability type the
            // remaining members resolve against.
            abilityField = surface.Field(commandAbilityType, "ability");
            Type abilityType = abilityField?.FieldType;

            cooldownField = surface.Field(abilityType, "cooldown");
            getCooldownForPawnMethod = surface.Method(abilityType, "GetCooldownForPawn");
            autoCastProperty = surface.Property(abilityType, "AutoCast");
            canAutoCastProperty = surface.Property(abilityType, "CanAutoCast");
            doActionMethod = surface.Method(abilityType, "DoAction");

            ready = surface.Ready;
        }

        // Label/description DECLINE: Command_Ability's ctor (VEF source lines
        // 22-23) fully populates defaultLabel/defaultDesc, so CommandGizmoHandler
        // already covers them via the byType chain.

        public override bool TryGetStatus(Gizmo gizmo, out string status)
        {
            status = null;
            if (!ready || !commandType.IsInstanceOfType(gizmo))
                return false;

            try
            {
                object abilityObj = abilityField.GetValue(gizmo);
                if (abilityObj == null)
                    return false;

                var parts = new List<string>();

                // Mirrors GizmoOnGUIInt lines 57-64: the cooldown overlay only
                // draws while the gizmo is disabled for being on cooldown.
                if (gizmo.Disabled)
                {
                    int cooldown = (int)cooldownField.GetValue(abilityObj);
                    if (cooldown > Find.TickManager.TicksGame)
                    {
                        int cooldownForPawn = (int)getCooldownForPawnMethod.Invoke(abilityObj, null);
                        if (cooldownForPawn > 0)
                        {
                            float remaining = (cooldown - Find.TickManager.TicksGame) / (float)cooldownForPawn;
                            parts.Add("RimWorldAccess.Compat.Vef.CooldownStatus".Translate(
                                (1f - remaining).ToStringPercent("F0")));
                        }
                    }
                }

                // Mirrors GizmoOnGUIInt lines 50-55: the auto-cast checkmark icon.
                if ((bool)autoCastProperty.GetValue(abilityObj))
                    parts.Add("RimWorldAccess.Compat.Vef.AutoCastOn".Translate());

                // Claiming this facet at all SHADOWS Command.TopRightLabel (the
                // "H: 24, P: 1%" cost counter CommandGizmoHandler would otherwise
                // speak via the byType chain's fallthrough) once we've already
                // claimed status for cooldown/auto-cast — the registry's
                // first-non-null-wins walk never reaches CommandGizmoHandler.
                // Sighted players see the cooldown fill, the auto-cast checkmark,
                // and the top-right cost text all at once, so fold the cost text
                // back in here as a final part.
                if (gizmo is Verse.Command cmd)
                {
                    string topRightLabel = cmd.TopRightLabel;
                    if (!string.IsNullOrEmpty(topRightLabel))
                        parts.Add(GizmoTextUtility.FlattenNewlines(topRightLabel.StripTags()));
                }

                if (parts.Count == 0)
                    return false;

                status = string.Join(". ", parts);
                return true;
            }
            catch (Exception ex)
            {
                ModLogger.Error($"VefAbilityCommandHandler.TryGetStatus failed: {ex.Message}");
                return false;
            }
        }

        public override bool TryGetExtraOptions(Gizmo gizmo, List<FloatMenuOption> options)
        {
            if (!ready || !commandType.IsInstanceOfType(gizmo))
                return false;

            // Vanilla's Command.GizmoOnGUIInt rejects every click on a disabled
            // command with a reject-input message and GizmoState.Mouseover
            // (decompiled Verse/Command.cs lines 218-227) before it ever reaches
            // the button==1 branch, so a sighted player cannot right-click into
            // Ability.DoAction's auto-cast toggle while the command is disabled.
            // Offering the toggle here would exceed sighted parity.
            if (gizmo.Disabled)
                return false;

            try
            {
                object abilityObj = abilityField.GetValue(gizmo);
                if (abilityObj == null || !(bool)canAutoCastProperty.GetValue(abilityObj))
                    return false;

                bool autoCastNow = (bool)autoCastProperty.GetValue(abilityObj);
                string label = (autoCastNow
                    ? "RimWorldAccess.Compat.Vef.DisableAutoCast"
                    : "RimWorldAccess.Compat.Vef.EnableAutoCast").Translate();

                options.Add(new FloatMenuOption(label, () => ToggleAutoCast(abilityObj)));
                return true;
            }
            catch (Exception ex)
            {
                ModLogger.Error($"VefAbilityCommandHandler.TryGetExtraOptions failed: {ex.Message}");
                return false;
            }
        }

        // TryExecute DECLINES: the generic fallback's ProcessInput -> action ->
        // DoAction(button 0) -> targeting flow already runs and the targeting
        // patches already announce it.

        /// <summary>
        /// Vehicle A: VEF's own Ability.DoAction (Misc/Ability.cs lines 309-317)
        /// branches on Event.current.button == 1 to flip auto-cast — the exact
        /// path a sighted player's right-click on the gizmo runs. We borrow that
        /// branch by temporarily faking the button, rather than hand-copying
        /// its autoCast flip.
        /// </summary>
        private void ToggleAutoCast(object abilityObj)
        {
            try
            {
                Event cur = Event.current;
                if (cur == null)
                    return; // outside OnGUI; nothing safe to do

                int prevButton = cur.button;
                cur.button = 1;
                try
                {
                    doActionMethod.Invoke(abilityObj, null);
                }
                finally
                {
                    cur.button = prevButton;
                }

                bool autoCastNow = (bool)autoCastProperty.GetValue(abilityObj);
                TolkHelper.Speak((autoCastNow
                    ? "RimWorldAccess.Compat.Vef.AutoCastNowOn"
                    : "RimWorldAccess.Compat.Vef.AutoCastNowOff").Loc());
            }
            catch (Exception ex)
            {
                ModLogger.Error($"VefAbilityCommandHandler auto-cast toggle failed: {ex.Message}");
            }
        }
    }
}
