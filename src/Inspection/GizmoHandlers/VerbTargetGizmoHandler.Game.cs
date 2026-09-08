using Verse;

namespace RimWorldAccess
{
    /// <summary>
    /// Verbatim port of the original ExecuteSelected ladder's branch 5
    /// (Command_VerbTarget: weapon attacks — announce targeting mode) plus the
    /// weapon-verb label special case from GetGizmoLabel ("weapon: verb" instead
    /// of the command's own label, which vanilla leaves generic).
    /// </summary>
    internal sealed class VerbTargetGizmoHandler : GizmoHandlerBase
    {
        public override bool TryGetLabel(Gizmo gizmo, out string label)
        {
            label = null;
            if (!(gizmo is Command_VerbTarget verbTarget))
                return false;

            string weaponName = verbTarget.ownerThing?.LabelCap ?? "RimWorldAccess.Inspection.Gizmo.UnknownWeaponFallback".Translate().ToString();
            string verbLabel = verbTarget.verb?.ReportLabel ?? "RimWorldAccess.Inspection.Gizmo.AttackFallback".Translate().ToString();
            label = "RimWorldAccess.Inspection.Gizmo.WeaponVerbLabel".Translate(weaponName, verbLabel);
            return true;
        }

        public override bool TryExecute(Gizmo gizmo, GizmoHandlerContext ctx, out bool runEpilogue)
        {
            runEpilogue = true;

            if (!(gizmo is Command_VerbTarget verbTarget))
                return false;

            try
            {
                // Propagate to grouped gizmos first — adds other pawns
                // to targetingSourceAdditionalPawns for group targeting
                GizmoNavigationState.PropagateToGroupedGizmos(gizmo, ctx.FakeEvent);
                // Execute the command (starts targeting for this pawn)
                gizmo.ProcessInput(ctx.FakeEvent);
                GizmoNavigationState.ProcessGroupInput(gizmo, ctx.FakeEvent);

                string weaponName = verbTarget.ownerThing?.LabelCap ?? "RimWorldAccess.Inspection.Gizmo.WeaponFallback".Translate().ToString();
                string verbLabel = verbTarget.verb?.ReportLabel ?? "RimWorldAccess.Inspection.Gizmo.AttackFallback".Translate().ToString();
                TolkHelper.Speak("RimWorldAccess.Inspection.Gizmo.WeaponVerbTargeting".Loc(weaponName, verbLabel));
            }
            catch (System.Exception ex)
            {
                ModLogger.Error($"Exception in Command_VerbTarget execution: {ex.Message}");
                TolkHelper.Speak("RimWorldAccess.Inspection.Gizmo.ErrorExecuting".Loc(ctx.GizmoLabel, ex.Message), SpeechPriority.High);
            }
            return true;
        }
    }
}
