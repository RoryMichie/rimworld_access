using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using RimWorld;
using RimWorldAccess.Shell;
using UnityEngine;
using Verse;

namespace RimWorldAccess
{
    /// <summary>
    /// Gizmo handler for AbilityUser.Command_PawnAbility (JecsTools' ability
    /// gizmo — every RimWorld of Magic spell button, plus any other mod built
    /// on the AbilityUser framework). Every member is resolved once by
    /// <see cref="JecsAbilityCompat"/> and handed in here; every facet
    /// declines rather than throws if resolution failed.
    ///
    /// Label DECLINES: Command_PawnAbility's LabelCap (via the base Command
    /// chain) is already correct — CommandGizmoHandler covers it.
    ///
    /// Execution DECLINES: registering this handler for the exact
    /// Command_PawnAbility type means GizmoHandlerRegistry's byType chain
    /// resolves it ahead of Command_Target's TargetGizmoHandler (see
    /// TypeChainResolver — the walk returns the FIRST match, most-derived
    /// first, and never continues past it even if that handler's TryExecute
    /// declines). Declining here therefore routes execution to
    /// GenericFallbackGizmoHandler (gizmo.ProcessInput, unmodified) rather
    /// than TargetGizmoHandler, whose generic "use map navigation to target"
    /// utterance would double up with JecsAbilityCompat's own targeting-session
    /// announcement (range/AoE/instructions), fired from its ProcessInput
    /// postfix immediately after gizmo.ProcessInput returns.
    /// </summary>
    internal sealed class JecsAbilityCommandHandler : GizmoHandlerBase
    {
        private readonly Type commandType;
        private readonly FieldInfo pawnAbilityField;
        private readonly PropertyInfo cooldownTicksLeftProperty;
        private readonly PropertyInfo maxCastingTicksProperty;
        private readonly bool ready;

        private static readonly List<IJecsAbilityExtension> extensions = new List<IJecsAbilityExtension>();

        /// <summary>Registers an extension contributing an extra status suffix and/or extra options.</summary>
        public static void RegisterExtension(IJecsAbilityExtension extension)
        {
            if (extension != null)
                extensions.Add(extension);
        }

        public JecsAbilityCommandHandler(Type commandPawnAbilityType, FieldInfo pawnAbilityField,
            PropertyInfo cooldownTicksLeftProperty, PropertyInfo maxCastingTicksProperty)
        {
            commandType = commandPawnAbilityType;
            this.pawnAbilityField = pawnAbilityField;
            this.cooldownTicksLeftProperty = cooldownTicksLeftProperty;
            this.maxCastingTicksProperty = maxCastingTicksProperty;

            ready = commandType != null && pawnAbilityField != null
                && cooldownTicksLeftProperty != null && maxCastingTicksProperty != null;
        }

        public override bool TryGetDescription(Gizmo gizmo, out string description)
        {
            description = null;
            if (!ready || !commandType.IsInstanceOfType(gizmo))
                return false;

            // Command.Desc is eagerly built at gizmo construction (PawnAbility.GetGizmo sets
            // defaultDesc = powerDef.GetDescription() + PostAbilityVerbCompDesc + "\n"), so no
            // hover priming is needed — read it verbatim, including the mod's own upstream
            // duplication if any.
            description = SpeechFlatten.ToSentences(((Command)gizmo).Desc);
            return description != null;
        }

        public override bool TryGetStatus(Gizmo gizmo, out string status)
        {
            status = null;
            if (!ready || !commandType.IsInstanceOfType(gizmo))
                return false;

            try
            {
                object pawnAbility = pawnAbilityField.GetValue(gizmo);
                if (pawnAbility == null)
                    return false;

                var parts = new List<string>();

                // LIVE values — never Command_PawnAbility.curTicks, a construction-time snapshot.
                int cooldownTicksLeft = (int)cooldownTicksLeftProperty.GetValue(pawnAbility);
                int maxCastingTicks = (int)maxCastingTicksProperty.GetValue(pawnAbility);
                if (cooldownTicksLeft != -1 && cooldownTicksLeft < maxCastingTicks)
                {
                    int secondsLeft = Mathf.CeilToInt(cooldownTicksLeft / 60f);
                    parts.Add((string)"RimWorldAccess.Compat.JecsAbility.Recharging".Translate(secondsLeft));
                }

                foreach (IJecsAbilityExtension ext in extensions)
                {
                    string suffix = SafeStatusSuffix(ext, gizmo);
                    if (!string.IsNullOrEmpty(suffix))
                        parts.Add(suffix);
                }

                if (parts.Count == 0)
                    return false;

                status = string.Join(". ", parts);
                return true;
            }
            catch (Exception ex)
            {
                ModLogger.Error($"JecsAbilityCommandHandler.TryGetStatus failed: {ex.Message}");
                return false;
            }
        }

        public override bool TryGetExtraOptions(Gizmo gizmo, List<FloatMenuOption> options)
        {
            if (!ready || !commandType.IsInstanceOfType(gizmo) || extensions.Count == 0)
                return false;

            bool any = false;
            foreach (IJecsAbilityExtension ext in extensions)
            {
                try
                {
                    if (ext.TryGetExtraOptions(gizmo, options))
                        any = true;
                }
                catch (Exception ex)
                {
                    ModLogger.Error($"JecsAbilityCommandHandler extension TryGetExtraOptions failed: {ex.Message}");
                }
            }
            return any;
        }

        private static string SafeStatusSuffix(IJecsAbilityExtension ext, Gizmo gizmo)
        {
            try
            {
                return ext.TryGetStatusSuffix(gizmo);
            }
            catch (Exception ex)
            {
                ModLogger.Error($"JecsAbilityCommandHandler extension TryGetStatusSuffix failed: {ex.Message}");
                return null;
            }
        }
    }
}
