using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using RimWorld;
using Verse;

namespace RimWorldAccess
{
    /// <summary>
    /// Gizmo handler for VEF's SECOND auto-cast system, distinct from
    /// VefAbilityCommandHandler's (which covers VEF.Abilities.Command_Ability and the VPE psycasts
    /// built on it). This one lives on vanilla's own RimWorld.Command_Ability and activates only
    /// for drafted insectoid pawns. VEF's DraftGizmos postfixes Command.GizmoOnGUIInt and, for a
    /// gizmo whose runtime type is EXACTLY Command_Ability (deliberately excluding subclasses),
    /// draws an auto-cast checkmark and toggles it on GizmoState.OpenedFloatMenu, a right-click,
    /// via DraftedActionHolder.GetData(pawn).AutoCastFor/ToggleAutoCastFor.
    ///
    /// The vanilla-side reads are compile-time; only the VEF-side
    /// DraftGizmos/DraftedActionHolder/DraftedActionData members need reflection.
    /// </summary>
    internal sealed class VefInsectoidAutocastHandler : GizmoHandlerBase
    {
        private readonly MethodInfo isPlayerDraftedInsectoidMethod;
        private readonly MethodInfo getDataMethod;
        private readonly MethodInfo autoCastForMethod;
        private readonly MethodInfo toggleAutoCastForMethod;
        private readonly bool ready;

        internal bool Ready => ready;

        public VefInsectoidAutocastHandler()
        {
            var surface = new ReflectionSurface("VefInsectoidAutocastHandler");

            Type draftGizmosType = surface.Type("VEF.AI.DraftGizmos");
            Type draftedActionHolderType = surface.Type("VEF.AI.DraftedActionHolder");

            isPlayerDraftedInsectoidMethod = surface.Method(draftGizmosType, "IsPlayerDraftedInsectoid");
            getDataMethod = surface.Method(draftedActionHolderType, "GetData");

            Type draftedActionDataType = getDataMethod?.ReturnType;
            autoCastForMethod = draftedActionDataType == null ? null
                : surface.Method(draftedActionDataType, "AutoCastFor");
            toggleAutoCastForMethod = draftedActionDataType == null ? null
                : surface.Method(draftedActionDataType, "ToggleAutoCastFor");

            ready = surface.Ready && draftedActionDataType != null;
        }

        /// <summary>
        /// Shared gate: mirrors DraftGizmos.GizmoOnGUIPostfix's own preconditions
        /// (exact-type check, then IsPlayerDraftedInsectoid on the command's pawn)
        /// before either facet below does anything.
        /// </summary>
        private bool Applies(Gizmo gizmo, out Command_Ability cmd, out Pawn pawn)
        {
            cmd = null;
            pawn = null;

            if (!ready)
                return false;

            // Exact-type check, mirroring the VEF patch's own
            // `__instance.GetType() == typeof(Command_Ability)` guard — deliberately
            // excludes subclasses (e.g. VEF.Abilities.Command_Ability, whose type
            // NAME also happens to be "Command_Ability" but which fails this check).
            if (gizmo.GetType() != typeof(Command_Ability))
                return false;

            cmd = (Command_Ability)gizmo;
            pawn = cmd.Pawn;
            if (pawn == null)
                return false;

            try
            {
                return (bool)isPlayerDraftedInsectoidMethod.Invoke(null, new object[] { pawn });
            }
            catch (Exception ex)
            {
                ModLogger.Error($"VefInsectoidAutocastHandler.Applies failed: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// Reachability: this facet is only consulted by GizmoHandlerRegistry
        /// after the byType chain for Command_Ability declines the status facet
        /// entirely (ResolveFacet walks byType first, byTypeName only when every
        /// byType candidate returns null) — AbilityGizmoHandler's TryGetStatus
        /// answers only for our own combat-autopilot autocast, and CommandGizmoHandler explicitly
        /// excludes Command_Ability from its TopRightLabel status (it exists to
        /// avoid double-speaking AbilityGizmoHandler's cost/range/cooldown
        /// fragments, which is unrelated to this auto-cast overlay). So for a
        /// plain vanilla Command_Ability the chain always declines, and this
        /// byTypeName-registered handler is the one that gets to answer.
        /// </summary>
        public override bool TryGetStatus(Gizmo gizmo, out string status)
        {
            status = null;
            if (!Applies(gizmo, out Command_Ability cmd, out Pawn pawn))
                return false;

            try
            {
                object data = getDataMethod.Invoke(null, new object[] { pawn });
                if (data == null)
                    return false;

                if (!(bool)autoCastForMethod.Invoke(data, new object[] { cmd.Ability.def }))
                    return false;

                status = "RimWorldAccess.Compat.Vef.AutoCastOn".Translate();
                return true;
            }
            catch (Exception ex)
            {
                ModLogger.Error($"VefInsectoidAutocastHandler.TryGetStatus failed: {ex.Message}");
                return false;
            }
        }

        public override bool TryGetExtraOptions(Gizmo gizmo, List<FloatMenuOption> options)
        {
            if (!Applies(gizmo, out Command_Ability cmd, out Pawn pawn))
                return false;

            // Vanilla's Command.GizmoOnGUIInt rejects every click on a disabled
            // command (reject-input message, GizmoState.Mouseover) before flag2
            // is ever set, so GizmoOnGUIPostfix's `__result.State ==
            // GizmoState.OpenedFloatMenu` branch is unreachable while disabled —
            // a sighted player cannot right-click into the toggle either.
            if (gizmo.Disabled)
                return false;

            try
            {
                object data = getDataMethod.Invoke(null, new object[] { pawn });
                if (data == null)
                    return false;

                AbilityDef def = cmd.Ability.def;
                bool autoCastNow = (bool)autoCastForMethod.Invoke(data, new object[] { def });
                string label = (autoCastNow
                    ? "RimWorldAccess.Compat.Vef.DisableAutoCast"
                    : "RimWorldAccess.Compat.Vef.EnableAutoCast").Translate();

                options.Add(new FloatMenuOption(label, () => ToggleAutoCast(data, def)));
                return true;
            }
            catch (Exception ex)
            {
                ModLogger.Error($"VefInsectoidAutocastHandler.TryGetExtraOptions failed: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// Vehicle A: the exact public calls DraftGizmos.GizmoOnGUIPostfix's own
        /// body makes on a right-click (GetData(pawn) then
        /// data.ToggleAutoCastFor(cmd.Ability.def)) — no hand-copied state flip.
        /// </summary>
        private void ToggleAutoCast(object data, AbilityDef def)
        {
            try
            {
                toggleAutoCastForMethod.Invoke(data, new object[] { def });

                bool autoCastNow = (bool)autoCastForMethod.Invoke(data, new object[] { def });
                TolkHelper.Speak((autoCastNow
                    ? "RimWorldAccess.Compat.Vef.AutoCastNowOn"
                    : "RimWorldAccess.Compat.Vef.AutoCastNowOff").Loc());
            }
            catch (Exception ex)
            {
                ModLogger.Error($"VefInsectoidAutocastHandler auto-cast toggle failed: {ex.Message}");
            }
        }
    }
}
