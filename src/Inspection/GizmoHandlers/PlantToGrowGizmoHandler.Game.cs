using RimWorld;
using Verse;

namespace RimWorldAccess
{
    /// <summary>
    /// Verbatim port of the original ExecuteSelected ladder's branch 2
    /// (Command_SetPlantToGrow: opens the accessible plant selection menu).
    /// Declines when the gizmo's owner isn't an IPlantToGrowSettable, letting the
    /// registry fall through exactly as the original `if` did.
    /// </summary>
    internal sealed class PlantToGrowGizmoHandler : GizmoHandlerBase
    {
        public override bool TryExecute(Gizmo gizmo, GizmoHandlerContext ctx, out bool runEpilogue)
        {
            runEpilogue = false;

            if (!(gizmo is Command_SetPlantToGrow))
                return false;
            if (!(ctx.Owner is IPlantToGrowSettable plantSettable))
                return false;

            GizmoNavigationState.Close();
            PlantSelectionMenuState.Open(plantSettable);
            return true;
        }
    }
}
