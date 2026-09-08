using Verse;
using RimWorld;

namespace RimWorldAccess
{
    /// <summary>
    /// Verbatim port of the original ExecuteSelected ladder's branch 6
    /// (Command_Target: announce targeting mode, with special-cased animal
    /// attack-target range info) and the announcement ladders' `is Command_Target`
    /// fragment (range-from-master suffix, only for the animal attack-target
    /// icon). Execution never returns early even on exception (the original
    /// try/catch swallows it), so it always falls through to the shared epilogue.
    /// </summary>
    internal sealed class TargetGizmoHandler : GizmoHandlerBase
    {
        public override bool TryExecute(Gizmo gizmo, GizmoHandlerContext ctx, out bool runEpilogue)
        {
            runEpilogue = true;

            if (!(gizmo is Command_Target cmdTarget))
                return false;

            try
            {
                // Propagate to grouped gizmos first (vanilla order)
                GizmoNavigationState.PropagateToGroupedGizmos(gizmo, ctx.FakeEvent);
                // Execute the command
                gizmo.ProcessInput(ctx.FakeEvent);
                GizmoNavigationState.ProcessGroupInput(gizmo, ctx.FakeEvent);

                // Detect animal attack target commands (Odyssey DLC) by icon match.
                // Both group (from master's Pawn_PlayerSettings) and individual (from animal's
                // Pawn_TrainingTracker) use the same AttackTargetTexture icon.
                if (cmdTarget.icon == Pawn_TrainingTracker.AttackTargetTexture
                    && ctx.Owner is Pawn ownerPawn)
                {
                    // Determine the master pawn:
                    // - Group command: owner IS the drafted master colonist
                    // - Individual command: owner is the animal, master is its assigned master
                    Pawn master = (ownerPawn.IsColonist && ownerPawn.Drafted)
                        ? ownerPawn
                        : ownerPawn.playerSettings?.Master;

                    if (master?.Position.IsValid == true)
                    {
                        float range = Pawn_TrainingTracker.AttackTargetRange;
                        TargetingPatch.SetTargetingContext(master.Position, range);
                        TolkHelper.Speak("RimWorldAccess.Inspection.Gizmo.AnimalTargetingWithRange".Loc(
                            ctx.GizmoLabel, range.ToString("F0")));
                    }
                    else
                    {
                        TolkHelper.Speak("RimWorldAccess.Inspection.Gizmo.UseMapNavigationToTarget".Loc(ctx.GizmoLabel));
                    }
                }
                else
                {
                    TolkHelper.Speak("RimWorldAccess.Inspection.Gizmo.UseMapNavigationToTarget".Loc(ctx.GizmoLabel));
                }
            }
            catch (System.Exception ex)
            {
                ModLogger.Error($"Exception in Command_Target execution: {ex.Message}");
                TolkHelper.Speak("RimWorldAccess.Inspection.Gizmo.ErrorExecuting".Loc(ctx.GizmoLabel, ex.Message), SpeechPriority.High);
            }
            return true;
        }

        public override bool TryDescribe(Gizmo gizmo, GizmoDescriptionFragments fragments)
        {
            if (!(gizmo is Command_Target cmdTargetGizmo) || cmdTargetGizmo.icon != Pawn_TrainingTracker.AttackTargetTexture)
                return false;

            float attackRange = Pawn_TrainingTracker.AttackTargetRange;

            // Unlike Command_Toggle's genuinely diverging Speech/MenuLabel text,
            // both original ladders (AnnounceCurrentGizmoInner and
            // BuildGizmoMenuLabelInner) built this exact same "Range: X tiles from
            // master" wording verbatim — so no mode branch is needed here, and
            // both call sites resolve through the same translated key. No
            // separator baked in — GizmoNavigationState applies the same
            // trailing-period check the original inline code used.
            fragments.TargetRangeSuffix = "RimWorldAccess.Inspection.Gizmo.RangeFromMaster".Translate(attackRange.ToString("F0"));
            return true;
        }
    }
}
