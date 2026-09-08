using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;

namespace RimWorldAccess.Shell
{
    /// <summary>The three fixed controls a <see cref="MechanitorControlGroupGizmo"/> draws above its mech tiles.</summary>
    internal enum MechGizmoElement
    {
        /// <summary>The group-label strip, which vanilla itself turns into a "select every mech" button (decompiled RimWorld/MechanitorControlGroupGizmo.cs:165-176).</summary>
        SelectAll,

        /// <summary>The power icon that opens <see cref="Dialog_RechargeSettings"/> (:178-188).</summary>
        RechargeSettings,

        /// <summary>The work-mode icon whose click opens vanilla's work-mode float menu (:190-196).</summary>
        WorkMode,
    }

    /// <summary>
    /// The rects <see cref="MechanitorControlGroupGizmo.GizmoOnGUI"/> computed for the
    /// drilled-into control group on the current frame, recorded by
    /// <see cref="MechGizmoElementPatch"/> where vanilla computes them. Nothing here
    /// derives geometry: the tile size is solved by an iterative packing loop inside that
    /// method (:201-212), so the only sound source for a tile rect is the loop itself.
    ///
    /// Records are dropped and restarted on every new frame, so a rect can never outlive
    /// the draw that produced it — a gizmo bar that stopped drawing simply reports nothing
    /// and the consumer falls back to the whole-gizmo ring.
    /// </summary>
    internal static class MechGizmoElementRects
    {
        private static readonly Dictionary<MechGizmoElement, Rect> fixedRects = new Dictionary<MechGizmoElement, Rect>();
        private static readonly Dictionary<Pawn, Rect> tileRects = new Dictionary<Pawn, Rect>();

        private static int recordedFrame = -1;

        internal static void RecordFixed(Gizmo gizmo, Rect rect, MechGizmoElement element)
        {
            if (!Tracked(gizmo))
            {
                return;
            }
            OpenFrame();
            fixedRects[element] = rect;
        }

        internal static void RecordTile(Gizmo gizmo, Rect rect, Pawn mech)
        {
            if (mech == null || !Tracked(gizmo))
            {
                return;
            }
            OpenFrame();
            tileRects[mech] = rect;
        }

        internal static bool TryGetFixed(MechGizmoElement element, out Rect rect)
        {
            if (recordedFrame != Time.frameCount)
            {
                rect = default(Rect);
                return false;
            }
            return fixedRects.TryGetValue(element, out rect);
        }

        internal static bool TryGetTile(Pawn mech, out Rect rect)
        {
            if (mech == null || recordedFrame != Time.frameCount)
            {
                rect = default(Rect);
                return false;
            }
            return tileRects.TryGetValue(mech, out rect);
        }

        /// <summary>
        /// Runs on every draw of every control-group gizmo, so the cheap static read comes
        /// first; the reflected control-group compare only happens while the drill-in view
        /// is open. The gizmo instance the state kept is a snapshot from an older
        /// GetGizmos() enumeration, so identity is matched on the control group it draws,
        /// never on the gizmo object.
        /// </summary>
        private static bool Tracked(Gizmo gizmo)
        {
            if (!MechControlGroupState.IsActive)
            {
                return false;
            }
            MechanitorControlGroup group = MechControlGroupState.Group;
            return group != null && ReferenceEquals(group, MechControlGroupState.GetControlGroupFromGizmo(gizmo));
        }

        private static void OpenFrame()
        {
            if (recordedFrame == Time.frameCount)
            {
                return;
            }
            recordedFrame = Time.frameCount;
            fixedRects.Clear();
            tileRects.Clear();
        }
    }

    /// <summary>
    /// Records each element rect of <see cref="MechanitorControlGroupGizmo.GizmoOnGUI"/>
    /// from vanilla's own locals, so the drill-in scope can ring the exact control or mech
    /// tile its cursor is on instead of the whole gizmo.
    ///
    /// The method is monolithic — every element is a local inside one body — so the anchors
    /// are positional and each carries a count assertion. A mismatch means the method's
    /// shape changed under us: the whole rewrite is abandoned (the original stream is
    /// returned) and the ring falls back to the whole gizmo, which is what it did before
    /// this patch existed.
    ///
    /// Anchors, all against decompiled RimWorld/MechanitorControlGroupGizmo.cs:
    /// - The five four-float <c>Rect</c> constructions in IL order are the gizmo band
    ///   (:129), the recharge button (:178), the work-mode button (:190), the tile area
    ///   (:197) and one mech tile per loop pass (:222). Sites 1, 2 and 4 are recorded,
    ///   each immediately after its construction. The shipped IL builds these in place —
    ///   <c>ldloca</c> of the destination, the four arguments, then <c>call Rect::.ctor</c>
    ///   — so the local a site fills is found by walking the arguments back to the address
    ///   that carries them, never by a local index of ours.
    /// - The select-all strip (rect3) is a copy of the contracted band resized to the
    ///   label, so it has no construction of its own; its anchor is the method's single
    ///   <c>Rect.set_height</c> call (:154), after which the strip is final. That anchor
    ///   is checked independently: if it is ever not unique, only the select-all element
    ///   loses its ring and the rest of the patch stands.
    /// - The tile's mech is re-read the way vanilla reads it on the next line (:223), by
    ///   cloning the list and index loads of its own <c>MechsForReading[num9]</c>. Losing
    ///   that load costs the tiles their ring and nothing else, so it is logged and the
    ///   rest of the patch stands.
    /// </summary>
    [HarmonyPatch(typeof(MechanitorControlGroupGizmo), "GizmoOnGUI",
        new[] { typeof(Vector2), typeof(float), typeof(GizmoRenderParms) })]
    internal static class MechGizmoElementPatch
    {
        private const int ExpectedRectSites = 5;
        private const int RechargeSite = 1;
        private const int WorkModeSite = 2;
        private const int TileSite = 4;

        /// <summary>How far past a rect's store the mech load may sit before the tile anchor is treated as lost.</summary>
        private const int TileLookaheadLimit = 32;

        /// <summary>How far back from the height setter the strip's own <c>ldloca</c> may sit.</summary>
        private const int StripLookbehindLimit = 8;

        private static readonly MethodInfo RecordFixedMethod =
            AccessTools.Method(typeof(MechGizmoElementRects), nameof(MechGizmoElementRects.RecordFixed));

        private static readonly MethodInfo RecordTileMethod =
            AccessTools.Method(typeof(MechGizmoElementRects), nameof(MechGizmoElementRects.RecordTile));

        private static readonly MethodInfo RectSetHeight = AccessTools.PropertySetter(typeof(Rect), "height");

        [HarmonyTranspiler]
        public static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            List<CodeInstruction> codes = null;
            try
            {
                codes = new List<CodeInstruction>(instructions);
                return Inject(codes);
            }
            catch (Exception ex)
            {
                ModLogger.Error("Mech gizmo element rect injection failed: " + ex);
                return codes ?? instructions;
            }
        }

        private static List<CodeInstruction> Inject(List<CodeInstruction> codes)
        {
            if (RecordFixedMethod == null || RecordTileMethod == null || RectSetHeight == null)
            {
                ModLogger.Error("Mech gizmo element rect injection: recorder methods not resolved.");
                return codes;
            }

            List<int> sites = new List<int>();
            for (int i = 0; i < codes.Count; i++)
            {
                if (IsRectConstruction(codes[i]))
                {
                    sites.Add(i);
                }
            }
            if (sites.Count != ExpectedRectSites)
            {
                ModLogger.Error("Mech gizmo element rect injection: expected " + ExpectedRectSites
                    + " Rect constructions in MechanitorControlGroupGizmo.GizmoOnGUI, found " + sites.Count + ".");
                return codes;
            }

            if (!TryRectLocal(codes, sites[RechargeSite], out int rechargeLocal)
                || !TryRectLocal(codes, sites[WorkModeSite], out int workModeLocal)
                || !TryRectLocal(codes, sites[TileSite], out int tileLocal))
            {
                return codes;
            }

            List<KeyValuePair<int, CodeInstruction[]>> injections = new List<KeyValuePair<int, CodeInstruction[]>>
            {
                Injection(sites[RechargeSite] + 1, FixedRecord(rechargeLocal, MechGizmoElement.RechargeSettings)),
                Injection(sites[WorkModeSite] + 1, FixedRecord(workModeLocal, MechGizmoElement.WorkMode)),
            };

            CodeInstruction[] tile = TileRecord(codes, sites[TileSite], tileLocal);
            if (tile != null)
            {
                injections.Add(Injection(sites[TileSite] + 1, tile));
            }

            if (TryStripAnchor(codes, out int stripAt, out int stripLocal))
            {
                injections.Add(Injection(stripAt + 1, FixedRecord(stripLocal, MechGizmoElement.SelectAll)));
            }

            injections.Sort((a, b) => b.Key.CompareTo(a.Key));
            foreach (KeyValuePair<int, CodeInstruction[]> injection in injections)
            {
                codes.InsertRange(injection.Key, injection.Value);
            }
            return codes;
        }

        private static KeyValuePair<int, CodeInstruction[]> Injection(int at, CodeInstruction[] emitted)
        {
            return new KeyValuePair<int, CodeInstruction[]>(at, emitted);
        }

        private static bool IsRectConstruction(CodeInstruction code)
        {
            return (code.opcode == OpCodes.Call || code.opcode == OpCodes.Newobj)
                && code.operand is ConstructorInfo ctor
                && ctor.DeclaringType == typeof(Rect)
                && ctor.GetParameters().Length == 4;
        }

        /// <summary>
        /// The local a construction site fills, found by walking its four arguments back
        /// to the <c>ldloca</c> that carries them. Reaching a branch or a jump target on
        /// the way, or landing on anything other than an address load, means the shape
        /// this patch was written against is gone, so the caller abandons the rewrite.
        /// </summary>
        private static bool TryRectLocal(List<CodeInstruction> codes, int site, out int rectLocal)
        {
            rectLocal = -1;

            // The address plus the four floats it is constructed from.
            int needed = 5;
            for (int i = site - 1; i >= 0; i--)
            {
                CodeInstruction code = codes[i];
                if (!TryStackEffect(code, out int pushes, out int pops) || pushes > needed)
                {
                    break;
                }
                needed -= pushes;
                if (needed == 0)
                {
                    if (code.opcode != OpCodes.Ldloca && code.opcode != OpCodes.Ldloca_S)
                    {
                        break;
                    }
                    rectLocal = code.LocalIndex();
                    return true;
                }
                if (code.labels.Count > 0 || !IsStraightLine(code.opcode.FlowControl))
                {
                    break;
                }
                needed += pops;
            }
            ModLogger.Error("Mech gizmo element rect injection: the Rect construction at " + site
                + " fills no local this patch can name.");
            return false;
        }

        private static bool IsStraightLine(FlowControl flow)
        {
            return flow == FlowControl.Next || flow == FlowControl.Call || flow == FlowControl.Meta;
        }

        /// <summary>
        /// What one instruction pushes and pops. Calls read their operand rather than the
        /// opcode's variable stack behaviour; anything else unusual (a <c>dup</c>-shaped
        /// two-value push, a stack-clearing branch) reports false and stops the walk.
        /// </summary>
        private static bool TryStackEffect(CodeInstruction code, out int pushes, out int pops)
        {
            pushes = 0;
            pops = 0;
            if (code.operand is MethodBase called
                && (code.opcode == OpCodes.Call || code.opcode == OpCodes.Callvirt || code.opcode == OpCodes.Newobj))
            {
                MethodInfo method = called as MethodInfo;
                pops = called.GetParameters().Length;
                if (code.opcode == OpCodes.Newobj)
                {
                    pushes = 1;
                    return true;
                }
                if (!called.IsStatic)
                {
                    pops++;
                }
                pushes = method != null && method.ReturnType != typeof(void) ? 1 : 0;
                return true;
            }
            return TryCount(code.opcode.StackBehaviourPush, out pushes) && TryCount(code.opcode.StackBehaviourPop, out pops);
        }

        private static bool TryCount(StackBehaviour behaviour, out int count)
        {
            switch (behaviour)
            {
                case StackBehaviour.Pop0:
                case StackBehaviour.Push0:
                    count = 0;
                    return true;
                case StackBehaviour.Pop1:
                case StackBehaviour.Popi:
                case StackBehaviour.Popref:
                case StackBehaviour.Push1:
                case StackBehaviour.Pushi:
                case StackBehaviour.Pushi8:
                case StackBehaviour.Pushr4:
                case StackBehaviour.Pushr8:
                case StackBehaviour.Pushref:
                    count = 1;
                    return true;
                case StackBehaviour.Pop1_pop1:
                case StackBehaviour.Popi_pop1:
                case StackBehaviour.Popi_popi:
                case StackBehaviour.Popi_popi8:
                case StackBehaviour.Popi_popr4:
                case StackBehaviour.Popi_popr8:
                case StackBehaviour.Popref_pop1:
                case StackBehaviour.Popref_popi:
                case StackBehaviour.Push1_push1:
                    count = 2;
                    return true;
                case StackBehaviour.Popi_popi_popi:
                case StackBehaviour.Popref_popi_popi:
                case StackBehaviour.Popref_popi_popi8:
                case StackBehaviour.Popref_popi_popr4:
                case StackBehaviour.Popref_popi_popr8:
                case StackBehaviour.Popref_popi_popref:
                case StackBehaviour.Popref_popi_pop1:
                    count = 3;
                    return true;
            }
            count = 0;
            return false;
        }

        private static CodeInstruction[] FixedRecord(int rectLocal, MechGizmoElement element)
        {
            return new[]
            {
                new CodeInstruction(OpCodes.Ldarg_0),
                CodeInstruction.LoadLocal(rectLocal),
                new CodeInstruction(OpCodes.Ldc_I4, (int)element),
                new CodeInstruction(OpCodes.Call, RecordFixedMethod),
            };
        }

        /// <summary>
        /// The tile record, re-reading the mech from vanilla's own next statement rather
        /// than from a local the compiler may or may not have kept: the list and index
        /// loads of that <c>MechsForReading[num9]</c> are cloned verbatim, so the injection
        /// binds to no local index of its own.
        /// </summary>
        private static CodeInstruction[] TileRecord(List<CodeInstruction> codes, int site, int rectLocal)
        {
            int limit = Math.Min(codes.Count, site + TileLookaheadLimit);
            for (int i = site + 1; i < limit; i++)
            {
                if (!IsMechListItem(codes[i]))
                {
                    continue;
                }
                if (i < 2 || !codes[i - 2].IsLdloc() || !codes[i - 1].IsLdloc())
                {
                    break;
                }
                return new[]
                {
                    new CodeInstruction(OpCodes.Ldarg_0),
                    CodeInstruction.LoadLocal(rectLocal),
                    new CodeInstruction(codes[i - 2].opcode, codes[i - 2].operand),
                    new CodeInstruction(codes[i - 1].opcode, codes[i - 1].operand),
                    new CodeInstruction(codes[i].opcode, codes[i].operand),
                    new CodeInstruction(OpCodes.Call, RecordTileMethod),
                };
            }
            ModLogger.Error("Mech gizmo element rect injection: no MechsForReading[i] load follows the tile rect; mech tiles stay unringed.");
            return null;
        }

        private static bool IsMechListItem(CodeInstruction code)
        {
            return code.operand is MethodInfo method
                && method.Name == "get_Item"
                && method.DeclaringType == typeof(List<Pawn>);
        }

        /// <summary>
        /// The method's single <c>Rect.height</c> assignment, after which the select-all
        /// strip is final, together with the local it addresses. False when that anchor is
        /// not unique or addresses nothing, which leaves the strip on the whole-gizmo ring.
        /// </summary>
        private static bool TryStripAnchor(List<CodeInstruction> codes, out int at, out int rectLocal)
        {
            at = -1;
            rectLocal = -1;
            int setters = 0;
            for (int i = 0; i < codes.Count; i++)
            {
                if (codes[i].Calls(RectSetHeight))
                {
                    setters++;
                    at = i;
                }
            }
            if (setters != 1)
            {
                ModLogger.Error("Mech gizmo element rect injection: expected one Rect.height assignment, found " + setters
                    + "; the select-all strip keeps the whole-gizmo ring.");
                return false;
            }
            int floor = Math.Max(0, at - StripLookbehindLimit);
            for (int i = at - 1; i >= floor; i--)
            {
                if (codes[i].opcode == OpCodes.Ldloca || codes[i].opcode == OpCodes.Ldloca_S)
                {
                    rectLocal = codes[i].LocalIndex();
                    return true;
                }
            }
            ModLogger.Error("Mech gizmo element rect injection: the Rect.height assignment addresses no local; the select-all strip keeps the whole-gizmo ring.");
            at = -1;
            return false;
        }
    }
}
