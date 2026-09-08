using Verse;
using RimWorld;

namespace RimWorldAccess
{
    /// <summary>
    /// Verbatim port of the original ExecuteSelected ladder's branch 7
    /// (Command_Ability: psycasts, abilities — announce casting or targeting) and
    /// the announcement ladders' `is Command_Ability` fragment (cost, range,
    /// cooldown). Execution never returns early even on exception (the original
    /// try/catch swallows it), so it always falls through to the shared epilogue.
    /// </summary>
    internal sealed class AbilityGizmoHandler : GizmoHandlerBase
    {
        public override bool TryExecute(Gizmo gizmo, GizmoHandlerContext ctx, out bool runEpilogue)
        {
            runEpilogue = true;

            if (!(gizmo is Command_Ability cmdAbility))
                return false;

            try
            {
                GizmoNavigationState.PropagateToGroupedGizmos(gizmo, ctx.FakeEvent);
                gizmo.ProcessInput(ctx.FakeEvent);
                GizmoNavigationState.ProcessGroupInput(gizmo, ctx.FakeEvent);

                // Self-cast abilities (targetRequired == false) don't enter targeting mode,
                // so no AbilityTargetingPatch fires. Announce immediately.
                if (!cmdAbility.Ability.def.targetRequired)
                {
                    TolkHelper.Speak("RimWorldAccess.Inspection.Gizmo.CastingAbility".Loc(
                        cmdAbility.Ability.def.LabelCap));
                }
                // Targeted abilities will be announced by AbilityTargetingPatch
            }
            catch (System.Exception ex)
            {
                ModLogger.Error($"Exception in Command_Ability execution: {ex.Message}");
                TolkHelper.Speak("RimWorldAccess.Inspection.Gizmo.ErrorExecuting".Loc(ctx.GizmoLabel, ex.Message), SpeechPriority.High);
            }
            return true;
        }

        /// <summary>Combat-autopilot autocast state, so the row reads "…, autocast on" when set.</summary>
        public override bool TryGetStatus(Gizmo gizmo, out string status)
        {
            status = null;
            if (!(gizmo is Command_Ability commandAbility) || commandAbility.Ability?.def == null)
                return false;
            if (!CombatAutopilotAutocast.AppliesTo(commandAbility.Pawn))
                return false;
            CombatAutopilotData data = CombatAutopilotComponent.TryGetData(commandAbility.Pawn);
            if (data == null || !data.AutocastFor(commandAbility.Ability.def))
                return false;
            status = "RimWorldAccess.Autopilot.AutocastOn".Translate();
            return true;
        }

        public override bool TryGetDescription(Gizmo gizmo, out string description)
        {
            description = null;
            if (!(gizmo is Command_Ability commandAbility) || commandAbility.Ability?.def == null)
                return false;

            // Command_Ability.defaultDesc is only set during GizmoOnGUIInt on
            // mouse hover, so pull directly from the ability definition instead.
            description = (commandAbility.Ability.def.description ?? "").StripTags();
            return true;
        }

        public override bool TryDescribe(Gizmo gizmo, GizmoDescriptionFragments fragments)
        {
            if (!(gizmo is Command_Ability commandAbility) || commandAbility.Ability == null)
                return false;

            // Cost/range/cooldown text is identical across both composition call
            // sites (unlike Toggle/Target, which diverge) — no Mode branch needed.
            fragments.AbilityInfoAvailable = true;
            fragments.AbilityCostInfo = GizmoNavigationState.GetAbilityCostInfo(commandAbility.Ability);
            fragments.AbilityRangeInfo = GizmoNavigationState.GetAbilityRangeInfo(commandAbility.Ability);
            fragments.AbilityCooldownInfo = GizmoNavigationState.GetAbilityCooldownInfo(commandAbility.Ability);
            return true;
        }
    }
}
