using System;
using Verse;

namespace RimWorldAccess
{
    /// <summary>
    /// Registers gizmo announcement handlers for the Vanilla Expanded Framework and its MVCF
    /// subsystem. A missing type means the mod is absent or its feature is disabled, and skips
    /// that one registration. A compat failure must never break startup, so the whole body runs
    /// under one try/catch.
    /// </summary>
    internal static class VefGizmoCompat
    {
        public static void RegisterGizmoHandlers()
        {
            try
            {
                int count = 0;

                count += CompatRegistration.GizmoHandler("VEF.Abilities.Command_Ability",
                    t => new VefAbilityCommandHandler(t));
                count += CompatRegistration.GizmoHandler("MVCF.Commands.Command_VerbTargetExtended",
                    t => new MvcfVerbTargetHandler(t));
                count += CompatRegistration.GizmoHandler("VEF.Apparels.Gizmo_EnergyShieldGeneratorStatus",
                    t => new VefShieldFieldGizmoHandler(t));
                count += CompatRegistration.GizmoHandler("VEF.Apparels.Gizmo_EnergyCompShieldStatus",
                    t => new VefShieldBubbleGizmoHandler(t));
                count += CompatRegistration.GizmoHandler("VEF.Apparels.Command_ActionWithCooldown",
                    t => new VefActionCooldownHandler(t));
                count += CompatRegistration.GizmoHandler("VEF.AI.Command_ToggleWithRClick",
                    t => new VefToggleRClickHandler(t));
                count += CompatRegistration.GizmoHandler("VEF.Buildings.Gizmo_SetSecondaryFuelLevel",
                    t => new VefSecondaryFuelHandler(t));

                // By NAME, not by Type, even though the target is vanilla's own
                // RimWorld.Command_Ability: the byType slot for typeof(Command_Ability) already
                // belongs to the core AbilityGizmoHandler, and Register would REPLACE it, whereas
                // the byTypeName tier ADDS this insectoid auto-cast facet alongside it. The
                // handler's own exact-type gate is what keeps it inert on VEF's
                // VEF.Abilities.Command_Ability, whose short type NAME collides with vanilla's.
                var insectoidAutocastHandler = new VefInsectoidAutocastHandler();
                if (insectoidAutocastHandler.Ready)
                {
                    GizmoHandlerRegistry.RegisterByTypeName("Command_Ability", insectoidAutocastHandler);
                    count++;
                }

                if (count > 0)
                    Log.Message($"[RimWorld Access] VEF compat: registered {count} gizmo handlers");
            }
            catch (Exception ex)
            {
                Log.Error($"[RimWorld Access] VEF compat registration failed: {ex.Message}");
            }
        }
    }
}
