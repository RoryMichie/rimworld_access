using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using UnityEngine;
using Verse;

namespace RimWorldAccess
{
    /// <summary>
    /// Feeds <see cref="GizmoRectRegistry"/> from vanilla's own per-gizmo draw
    /// calls. <see cref="Gizmo.GizmoOnGUI"/> is abstract (decompiled
    /// Verse/Gizmo.cs:47) with roughly thirty vanilla overriders, so a
    /// declaring-type patch would reach nothing (the CLAUDE.md
    /// inherited-method rule) — each subclass that declares the method needs
    /// its own patch target, found here by walking <see cref="GenTypes.AllTypes"/>
    /// rather than hand-listing them, which also covers every modded gizmo.
    ///
    /// Split into two patch classes, one per target method, rather than one
    /// class with both prefixes: Harmony applies every prefix declared in a
    /// patch class to every method its <c>TargetMethods</c> returns, and
    /// <see cref="Command.GizmoOnGUI"/>'s <c>maxWidth</c> parameter and
    /// <see cref="Command.GizmoOnGUIShrunk"/>'s <c>size</c> parameter don't
    /// share a name, so one prefix could not bind to both target shapes.
    /// </summary>
    [HarmonyPatch]
    internal static class GizmoRectCaptureFullPatch
    {
        internal static IEnumerable<MethodBase> TargetMethods()
        {
            // Skip abstract METHODS below, not abstract types: Command is abstract
            // yet holds the only GizmoOnGUIShrunk body, while Gizmo.GizmoOnGUI is
            // an abstract method with no body to patch.
            //
            // Routed through SafeTypeSweep (see its header for the exact live
            // failure): GenTypes.AllTypes can still hand back a poisoned mod
            // type, and both the IsAssignableFrom check and the
            // AccessTools.DeclaredMethod lookup below can throw TypeLoadException
            // from that single type, not just the initial enumeration.
            foreach (Type type in SafeTypeSweep.AssignableFromSafe(typeof(Gizmo), (_, ex) => ModLogger.LimitedError("Gizmo rect capture (full) error", ex)))
            {
                if (!SafeTypeSweep.TryEvaluate(type, t => AccessTools.DeclaredMethod(t, "GizmoOnGUI",
                        new[] { typeof(Vector2), typeof(float), typeof(GizmoRenderParms) }),
                    out MethodInfo declared, (_, ex) => ModLogger.LimitedError("Gizmo rect capture (full) error", ex)))
                {
                    continue;
                }

                // Open generic declarations (CashRegister.Gizmo_ModifyNumber<T>) cannot
                // be patched: Harmony throws "not fully instantiated" at patch time.
                if (declared != null && !declared.IsAbstract && !declared.ContainsGenericParameters)
                {
                    yield return declared;
                }
            }
        }

        // Rect is vanilla's own: Command.GizmoOnGUI builds
        // new Rect(topLeft.x, topLeft.y, GetWidth(maxWidth), 75f)
        // (decompiled Verse/Command.cs:96-98).
        // Positional binding: overrides rename the parameters (Command_Toggle's
        // GizmoOnGUI calls topLeft "loc"), and Harmony's by-name binding throws
        // "Parameter not found" at patch time for every such override.
        [HarmonyPrefix]
        internal static void PrefixFull(Gizmo __instance,
            [HarmonyArgument(0)] Vector2 topLeft, [HarmonyArgument(1)] float maxWidth)
        {
            try
            {
                GizmoRectRegistry.Record(__instance, new Rect(topLeft.x, topLeft.y, __instance.GetWidth(maxWidth), 75f));
            }
            catch (Exception ex)
            {
                ModLogger.LimitedError("Gizmo rect capture (full) error", ex);
            }
        }
    }

    /// <summary>Shrunk-band half of <see cref="GizmoRectCaptureFullPatch"/> — see its class remarks for why this is a second patch class.</summary>
    [HarmonyPatch]
    internal static class GizmoRectCaptureShrunkPatch
    {
        internal static IEnumerable<MethodBase> TargetMethods()
        {
            // Skip abstract METHODS below, not abstract types: Command is abstract
            // yet holds the only GizmoOnGUIShrunk body, while Gizmo.GizmoOnGUI is
            // an abstract method with no body to patch.
            //
            // Routed through SafeTypeSweep (see its header for the exact live
            // failure): GenTypes.AllTypes can still hand back a poisoned mod
            // type, and both the IsAssignableFrom check and the
            // AccessTools.DeclaredMethod lookup below can throw TypeLoadException
            // from that single type, not just the initial enumeration.
            foreach (Type type in SafeTypeSweep.AssignableFromSafe(typeof(Gizmo), (_, ex) => ModLogger.LimitedError("Gizmo rect capture (shrunk) error", ex)))
            {
                if (!SafeTypeSweep.TryEvaluate(type, t => AccessTools.DeclaredMethod(t, "GizmoOnGUIShrunk",
                        new[] { typeof(Vector2), typeof(float), typeof(GizmoRenderParms) }),
                    out MethodInfo declared, (_, ex) => ModLogger.LimitedError("Gizmo rect capture (shrunk) error", ex)))
                {
                    continue;
                }

                // Open generic declarations (CashRegister.Gizmo_ModifyNumber<T>) cannot
                // be patched: Harmony throws "not fully instantiated" at patch time.
                if (declared != null && !declared.IsAbstract && !declared.ContainsGenericParameters)
                {
                    yield return declared;
                }
            }
        }

        // new Rect(topLeft.x, topLeft.y, size, size) — decompiled Verse/Command.cs:101-105.
        // Positional binding, same reason as PrefixFull: override parameter names vary.
        [HarmonyPrefix]
        internal static void PrefixShrunk(Gizmo __instance,
            [HarmonyArgument(0)] Vector2 topLeft, [HarmonyArgument(1)] float size)
        {
            try
            {
                GizmoRectRegistry.Record(__instance, new Rect(topLeft.x, topLeft.y, size, size));
            }
            catch (Exception ex)
            {
                ModLogger.LimitedError("Gizmo rect capture (shrunk) error", ex);
            }
        }
    }
}
