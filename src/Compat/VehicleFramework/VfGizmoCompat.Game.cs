using System;
using HarmonyLib;
using Verse;

namespace RimWorldAccess
{
    /// <summary>
    /// Registers gizmo announcement handlers for Vehicle Framework. A compat failure must never
    /// break startup, so the whole body runs under one try/catch.
    /// </summary>
    internal static class VfGizmoCompat
    {
        public static void RegisterGizmoHandlers()
        {
            try
            {
                int count = 0;

                Type refuelGizmoType = AccessTools.TypeByName("Vehicles.Rendering.Gizmo_RefuelableFuelTravel");
                if (refuelGizmoType != null)
                {
                    var refuelHandler = new VfRefuelGizmoHandler(refuelGizmoType);
                    if (refuelHandler.Ready)
                    {
                        GizmoHandlerRegistry.Register(refuelGizmoType, refuelHandler);
                        count++;
                    }
                }

                // Registered on the base type only: Command_TargeterCooldownAction is
                // covered via the registry's byType base-type walk.
                Type turretGizmoType = AccessTools.TypeByName("Vehicles.Rendering.Command_CooldownAction");
                if (turretGizmoType != null)
                {
                    var turretHandler = new VfTurretGizmoHandler(turretGizmoType);
                    if (turretHandler.Ready)
                    {
                        GizmoHandlerRegistry.Register(turretGizmoType, turretHandler);
                        count++;
                    }
                }

                if (count > 0)
                    Log.Message($"[RimWorld Access] VF compat: registered {count} gizmo handlers");
            }
            catch (Exception ex)
            {
                Log.Error($"[RimWorld Access] VF compat registration failed: {ex.Message}");
            }
        }
    }
}
