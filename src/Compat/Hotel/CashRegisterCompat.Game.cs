using System;
using HarmonyLib;
using Verse;

namespace RimWorldAccess
{
    /// <summary>
    /// Compat for CashRegister, the shared register/shift framework underneath
    /// Gastronomy and Storefront.
    ///
    /// CashRegister.Gizmo_Radius is a bare Gizmo_ModifyNumber&lt;Building_CashRegister&gt;
    /// -- the SAME base class shape (protected Title, protected ButtonUp/ButtonDown/
    /// ButtonCenter, protected T[] selection) that Hospitality's Gizmo_GuestBed/
    /// Gizmo_VendingMachine/Gizmo_VendingMachineContent derive from, duplicated
    /// verbatim into this mod's own assembly rather than shared. So this registers
    /// CashRegister's one concrete type against the shared
    /// <see cref="ModifyNumberGizmoHandler"/> instead of copying its plumbing; see
    /// that class's header for the design.
    /// </summary>
    internal static class CashRegisterCompat
    {
        public static void RegisterGizmoHandlers()
        {
            try
            {
                Type radiusGizmoType = AccessTools.TypeByName("CashRegister.Gizmo_Radius");
                if (radiusGizmoType == null)
                {
                    return;
                }

                GizmoHandlerRegistry.Register(radiusGizmoType, new ModifyNumberGizmoHandler());
                ModLogger.Msg("CashRegister compat: registered 1 gizmo handler");
            }
            catch (Exception ex)
            {
                ModLogger.Error($"CashRegister compat registration failed: {ex.Message}");
            }
        }
    }
}
