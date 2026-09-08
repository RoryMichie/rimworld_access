using Verse;

namespace RimWorldAccess
{
    /// <summary>
    /// Verbatim port of the original ExecuteSelected ladder's branch 8 (final
    /// generic Command fallback): propagate to grouped gizmos, ProcessInput,
    /// process group input. Applies to any gizmo not claimed by an earlier
    /// branch, including non-Command status gizmos — Gizmo.ProcessInput is a
    /// base-class virtual, not Command-specific. Always reports handled and
    /// requests the shared epilogue, matching the original's unconditional
    /// fall-through to the post-execution steps.
    /// </summary>
    internal sealed class GenericFallbackGizmoHandler : GizmoHandlerBase
    {
        public override bool TryExecute(Gizmo gizmo, GizmoHandlerContext ctx, out bool runEpilogue)
        {
            runEpilogue = true;

            try
            {
                GizmoNavigationState.PropagateToGroupedGizmos(gizmo, ctx.FakeEvent);
                gizmo.ProcessInput(ctx.FakeEvent);
                GizmoNavigationState.ProcessGroupInput(gizmo, ctx.FakeEvent);
            }
            catch (System.Exception ex)
            {
                ModLogger.Error($"Exception in generic Command execution: {ex.Message}");
                TolkHelper.Speak("RimWorldAccess.Inspection.Gizmo.ErrorExecuting".Loc(ctx.GizmoLabel, ex.Message), SpeechPriority.High);
            }
            return true;
        }
    }
}
