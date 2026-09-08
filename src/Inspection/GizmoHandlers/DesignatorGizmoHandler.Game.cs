using Verse;
using RimWorld;
using RimWorld.Planet;

namespace RimWorldAccess
{
    /// <summary>
    /// Verbatim port of the original ExecuteSelected ladder's branch 1
    /// (Designator: Designator_Build material selection, zone expand/shrink
    /// routing, owner-select-then-ProcessInput, placement mode entry). Not
    /// resolved through GizmoHandlerRegistry — see the registry's class doc for
    /// why. Invoked directly by GizmoNavigationState.ExecuteSelected before the
    /// shared owner-selection preamble runs, matching the original code's
    /// early-return shape exactly.
    /// </summary>
    internal static class DesignatorGizmoHandler
    {
        public static void Execute(Designator designator, GizmoHandlerContext ctx)
        {
            if (designator is Designator_Build buildDesignator)
            {
                BuildableDef buildable = buildDesignator.PlacingDef;
                if (ArchitectHelper.RequiresMaterialSelection(buildable))
                {
                    GizmoNavigationState.HandleBuildDesignatorMaterialSelection(buildDesignator, buildable);
                    return;
                }
            }

            string designatorTypeName = designator.GetType().Name;

            if (designatorTypeName.Contains("_Expand")
                && designatorTypeName.Contains("ZoneAdd"))
            {
                // Designator_ZoneAdd expands whatever Find.Selector holds, and the keyboard menu
                // restores the pre-menu selection on close, so the owner has to go back in.
                if (ctx.Owner is Zone ownerZone)
                {
                    Find.Selector.ClearSelection();
                    Find.Selector.Select(ownerZone, playSound: false, forceDesignatorDeselect: false);
                }
                GizmoNavigationState.Close();
                Find.DesignatorManager.Select(designator);
                return;
            }

            if (designatorTypeName == "Designator_ZoneDelete_Shrink")
            {
                // Select the zone owner so GetSelectedZone() works in ShapePlacementState
                if (ctx.Owner != null)
                {
                    Find.Selector.ClearSelection();
                    Find.Selector.Select(ctx.Owner, playSound: false, forceDesignatorDeselect: false);
                }
                GizmoNavigationState.Close();
                // Select the designator - DesignatorManagerPatch will route it to ShapePlacementState
                Find.DesignatorManager.Select(designator);
                return;
            }

            // For Designators opened via cursor objects (not selected pawns),
            // we need to ensure the correct object is selected
            // so the Designator has proper context (e.g., Designator_Install needs to know what to reinstall)
            if (!ctx.PawnJustSelected && ctx.Owner != null)
            {
                // Use WorldSelector for WorldObjects, Selector for map Things
                if (ctx.Owner is WorldObject worldObj && Find.WorldSelector != null)
                {
                    Find.WorldSelector.ClearSelection();
                    Find.WorldSelector.Select(worldObj, playSound: false);
                }
                else if (Find.Selector != null)
                {
                    Find.Selector.ClearSelection();
                    Find.Selector.Select(ctx.Owner, playSound: false, forceDesignatorDeselect: false);
                }
            }

            try
            {
                // Call ProcessInput to let the Designator do its preparation work
                // (Designator_Install does setup like canceling existing blueprints)
                designator.ProcessInput(ctx.FakeEvent);

                // Validate that the designator was actually selected
                if (Find.DesignatorManager != null && Find.DesignatorManager.SelectedDesignator != null)
                {
                    // Close the gizmo menu BEFORE entering placement mode
                    GizmoNavigationState.Close();

                    // Enter our accessible placement mode for consistent behavior
                    // (Space places blueprint, Enter confirms, allows repositioning)
                    ArchitectState.EnterPlacementMode(designator);
                    return;
                }
                else
                {
                    TolkHelper.Speak("RimWorldAccess.Inspection.Gizmo.ErrorActivationFailed".Loc(ctx.GizmoLabel), SpeechPriority.High);
                }
            }
            catch (System.Exception ex)
            {
                ModLogger.Error($"Exception in Designator execution: {ex.Message}");
                TolkHelper.Speak("RimWorldAccess.Inspection.Gizmo.ErrorExecuting".Loc(ctx.GizmoLabel, ex.Message), SpeechPriority.High);
            }

            // Close the gizmo menu AFTER announcing (only reached on error)
            GizmoNavigationState.Close();
        }
    }
}
