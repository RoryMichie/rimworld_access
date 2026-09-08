using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using RimWorld;
using RimWorldAccess.Shell;
using Verse;

namespace RimWorldAccess
{
    /// <summary>
    /// Points vanilla's remaining placement aids at the keyboard cursor.
    ///
    /// A sighted player gets a running commentary while a build designator is live, all of it keyed
    /// to the pointer: the deep-resource note, the distances between this building and others of its
    /// def, and every place worker's own attachment text — "will be linked to", the facility
    /// connections, the substructure footprint (decompiled RimWorld/Designator_Place.cs:37-70).
    /// That text is drawn beside the pointer and describes the cell the pointer is over, so during a
    /// keyboard session it described whatever cell the parked pointer happened to sit on: wrong for
    /// the sighted viewer watching, and wrong for anyone reading the screen over their shoulder.
    /// Running the whole method under the shell's cursor override makes vanilla answer for the cell
    /// the keyboard will actually build on, using vanilla's own text and its own layout.
    ///
    /// Draw-only by construction: this changes which cell vanilla describes, never what it builds.
    /// The one placement aid that DOES mutate — the wall-attachment turn, which rewrites placingRot
    /// — has its own bracket in <see cref="AttachmentFacingPatch"/>.
    ///
    /// Patched by declared-method sweep, not on the base type: <c>DrawMouseAttachments</c> is
    /// virtual and <see cref="Designator_Place"/>'s own override is where the place-worker loop
    /// lives, so a patch on <see cref="Designator"/> would wrap only the half that runs before it,
    /// and a modded designator that overrides without calling base would escape a patch on
    /// Designator_Place alone.
    /// </summary>
    [HarmonyPatch]
    internal static class PlacementAidCursorPatch
    {
        internal static IEnumerable<MethodBase> TargetMethods()
        {
            foreach (Type type in SafeTypeSweep.AssignableFromSafe(typeof(Designator_Place),
                (_, ex) => ModLogger.LimitedError("Placement aid cursor sweep error", ex)))
            {
                if (!SafeTypeSweep.TryEvaluate(type,
                        t => AccessTools.DeclaredMethod(t, "DrawMouseAttachments", Type.EmptyTypes),
                        out MethodInfo declared,
                        (_, ex) => ModLogger.LimitedError("Placement aid cursor sweep error", ex)))
                {
                    continue;
                }
                if (declared != null && !declared.IsAbstract && !declared.ContainsGenericParameters)
                {
                    yield return declared;
                }
            }
        }

        [HarmonyPrefix]
        internal static void Prefix(Designator_Place __instance, out bool __state)
        {
            __state = false;
            try
            {
                IntVec3 cell = KeyboardPlacementCursor(__instance);
                if (!cell.IsValid)
                {
                    return;
                }
                __state = true;
                DevToolTargeting.PushCursorOverride(cell);
            }
            catch (Exception ex)
            {
                __state = false;
                ModLogger.LimitedError("Placement aid cursor error", ex);
            }
        }

        /// <summary>A finalizer, not a postfix: a modded place worker's throw must not leave the cursor override latched.</summary>
        [HarmonyFinalizer]
        internal static Exception Finalizer(bool __state, Exception __exception)
        {
            if (__state)
            {
                DevToolTargeting.PopCursorOverride();
            }
            return __exception;
        }

        /// <summary>
        /// The cell our own keyboard session is aiming this designator at, or invalid when the
        /// pointer owns the gesture — a mouse-selected designator and a live drag are both the
        /// pointer's, exactly as <see cref="AttachmentFacingPatch"/> reads it.
        /// </summary>
        internal static IntVec3 KeyboardPlacementCursor(Designator_Place designator)
        {
            if (!ShapePlacementState.IsActive && !ArchitectState.IsInPlacementMode)
                return IntVec3.Invalid;
            if (Find.DesignatorManager == null || Find.DesignatorManager.Dragger.Dragging)
                return IntVec3.Invalid;
            if (designator != ArchitectPlacementInputPatch.GetActiveDesignator())
                return IntVec3.Invalid;

            Map map = designator.Map;
            IntVec3 cell = MapNavigationState.CurrentCursorPosition;
            return map != null && cell.InBounds(map) ? cell : IntVec3.Invalid;
        }
    }
}
