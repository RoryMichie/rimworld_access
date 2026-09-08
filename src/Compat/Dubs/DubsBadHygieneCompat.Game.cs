using System;
using HarmonyLib;

namespace RimWorldAccess
{
    /// <summary>
    /// Compat for Dubs Bad Hygiene.
    ///
    /// DubsBadHygiene.Gizmo_BoilerStatus is a bare Gizmo (not a Command), so
    /// GizmoNavigationState's generic fallback speaks only its type name
    /// ("Gizmo_BoilerStatus") — InspectionSelfAudit flags this at startup.
    /// The sighted player instead reads boiler.Props.GizmoLabel and
    /// "PowerMode / PowerModes" (Gizmo_BoilerStatus.cs:36/:47), drawn inside
    /// an ImmediateWindow. Registering a handler here supplies that same
    /// label/status through the ordinary GizmoHandlerRegistry path, which
    /// also silences the self-audit warning for this type.
    /// </summary>
    internal static class DubsBadHygieneCompat
    {
        public static void RegisterGizmoHandlers()
        {
            try
            {
                Type boilerStatusType = AccessTools.TypeByName("DubsBadHygiene.Gizmo_BoilerStatus");
                if (boilerStatusType == null)
                {
                    return;
                }

                GizmoHandlerRegistry.Register(boilerStatusType, new DubsBoilerStatusGizmoHandler());
                ModLogger.Msg("Dubs Bad Hygiene compat: registered 1 gizmo handler");
            }
            catch (Exception ex)
            {
                ModLogger.Error("Dubs Bad Hygiene compat registration failed: " + ex.Message);
            }
        }
    }
}
