using System;
using HarmonyLib;
using Verse;

namespace RimWorldAccess
{
    /// <summary>
    /// Registers gizmo announcement handlers for Vanilla Psycasts Expanded. A missing type means
    /// VPE isn't loaded and that registration silently skips. A compat failure must never break
    /// startup, so the whole body runs under one try/catch.
    /// </summary>
    internal static class VpeGizmoCompat
    {
        public static void RegisterGizmoHandlers()
        {
            try
            {
                int count = 0;

                count += TryRegister("VanillaPsycastsExpanded.UI.PsychicStatusGizmo",
                    t => new VpePsychicStatusHandler(t));

                if (count > 0)
                    Log.Message($"[RimWorld Access] VPE compat: registered {count} gizmo handlers");
            }
            catch (Exception ex)
            {
                Log.Error($"[RimWorld Access] VPE compat registration failed: {ex.Message}");
            }
        }

        private static int TryRegister(string typeName, Func<Type, IGizmoHandler> makeHandler)
        {
            Type t = AccessTools.TypeByName(typeName);
            if (t == null)
                return 0;

            GizmoHandlerRegistry.Register(t, makeHandler(t));
            return 1;
        }
    }
}
