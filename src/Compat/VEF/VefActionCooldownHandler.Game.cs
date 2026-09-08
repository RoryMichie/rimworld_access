using System;
using System.Reflection;
using Verse;

namespace RimWorldAccess
{
    /// <summary>
    /// Gizmo handler for VEF.Apparels.Command_ActionWithCooldown (the manual
    /// shield-activation button CompShieldField yields — VEF/Apparels/Comps/
    /// CompShieldField.cs lines 31-61). Mirrors its GizmoOnGUI cooldown bar
    /// (lines 46-53): lastUsedTick/cooldownTicks are private, so this is the
    /// one facet that needs reflection at all.
    /// </summary>
    internal sealed class VefActionCooldownHandler : GizmoHandlerBase
    {
        private readonly Type commandType;
        private readonly FieldInfo lastUsedTickField;
        private readonly FieldInfo cooldownTicksField;
        private readonly bool ready;

        public VefActionCooldownHandler(Type commandType)
        {
            this.commandType = commandType;

            var surface = new ReflectionSurface("VefActionCooldownHandler");
            surface.Supplied("VEF.Apparels.Command_ActionWithCooldown", commandType);
            lastUsedTickField = surface.Field(commandType, "lastUsedTick");
            cooldownTicksField = surface.Field(commandType, "cooldownTicks");
            ready = surface.Ready;
        }

        public override bool TryGetStatus(Gizmo gizmo, out string status)
        {
            status = null;
            if (!ready || !commandType.IsInstanceOfType(gizmo))
                return false;

            try
            {
                int lastUsedTick = (int)lastUsedTickField.GetValue(gizmo);
                if (lastUsedTick <= 0)
                    return false;

                int cooldownTicks = (int)cooldownTicksField.GetValue(gizmo);
                if (cooldownTicks <= 0)
                    return false;

                int elapsed = Find.TickManager.TicksGame - lastUsedTick;
                if (elapsed >= cooldownTicks)
                    return false;

                float readyFraction = elapsed / (float)cooldownTicks;
                status = "RimWorldAccess.Compat.Vef.CooldownStatus".Translate(readyFraction.ToStringPercent("F0"));
                return true;
            }
            catch (Exception ex)
            {
                ModLogger.Error($"VefActionCooldownHandler.TryGetStatus failed: {ex.Message}");
                return false;
            }
        }

        // Label/desc/execute DECLINE: Command_Action's defaults flow through
        // CommandGizmoHandler + the generic fallback.
    }
}
