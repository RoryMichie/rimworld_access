using System;
using System.Collections.Generic;
using System.Reflection;
using RimWorldAccess.Shell;
using Verse;

namespace RimWorldAccess
{
    /// <summary>
    /// Hands a keyboard rectangle to Allow Tool's area designators the way a mouse drag does:
    /// prime the mod's own UnlimitedAreaDragger, then raise the mod's own drag events around
    /// its own DesignateMultiCell. Everything that decides what happens -- CanDesignateThing,
    /// DesignateThing, the success/failure message, the FinalizeDesignation* pairing, the
    /// strip-mine settings window -- stays inside the mod.
    /// </summary>
    internal sealed class AllowToolRectDesignationHandler : IRectDesignationHandler
    {
        public bool Handles(Designator designator)
        {
            return designator != null
                && AllowToolCompat.RectDesignationGate.Ensure()
                && AllowToolCompat.DraggerOwnerType.IsInstanceOfType(designator);
        }

        public RectDesignationResult Designate(Designator designator, CellRect rect, IntVec3 firstCorner,
            IReadOnlyList<IntVec3> cells)
        {
            Map map = Find.CurrentMap;
            if (map == null)
                return RectDesignationResult.NotHandled;

            object dragger = AllowToolCompat.DraggerProperty.GetValue(designator, null);
            if (dragger == null)
                return RectDesignationResult.NotHandled;

            int selectedBefore = Find.Selector != null ? Find.Selector.NumSelected : 0;
            long messagesBefore = NotificationAccessibilityPatch.MessageEmissionCount;

            try
            {
                // SelectionInProgress is load-bearing, not decoration: Designator_SelectSimilar's
                // SelectingSingleCell is (SelectionInProgress && SelectedArea.Area == 1), and it
                // gates both CanDesignateThing and the single-cell branch of DesignateMultiCell.
                SetStartCell(dragger, firstCorner);
                SetInProgress(dragger, true);
                SetSelectedArea(dragger, CellRect.SingleCell(firstCorner));
                RaiseDragEvent(AllowToolCompat.SelectionStartField, dragger, CellRect.SingleCell(firstCorner));

                SetSelectedArea(dragger, rect);
                RaiseDragEvent(AllowToolCompat.SelectionChangedField, dragger, rect);

                // Designator_StripMine's completion handler opens Dialog_StripMineConfiguration,
                // which sets focusWhenOpened = false and absorbs no input, so without the guard
                // ScopeForWindow.GenericReaderEligible refuses it and the player gets a silent window.
                ScopeDelegateGuard.Run(delegate
                {
                    designator.DesignateMultiCell(cells);
                    RaiseDragEvent(AllowToolCompat.SelectionCompleteField, dragger, rect);
                });
            }
            finally
            {
                // Leaving SelectionInProgress true would make the dragger's own next Update fire
                // OnSelectionEnded a second time, re-running the strip-mine completion handler.
                SetInProgress(dragger, false);
                SetSelectedArea(dragger, CellRect.Empty);
                SetStartCell(dragger, IntVec3.Invalid);
            }

            int selectedNow = Find.Selector != null ? Find.Selector.NumSelected : 0;
            int added = selectedNow - selectedBefore;
            bool spoke = NotificationAccessibilityPatch.MessageEmissionCount != messagesBefore;
            bool isSelection = AllowToolCompat.SelectSimilarType.IsInstanceOfType(designator);

            // Select Similar's single-cell click CLEARS the selection before reselecting
            // (Designator_SelectSimilar.ProcessSingleCellClick), so when the clicked thing was
            // already selected the count delta is 0 despite a live result. For the selection
            // family the honest measure is the selection that exists afterwards.
            if (isSelection && added <= 0)
                added = selectedNow;

            return new RectDesignationResult(true, added > 0 ? added : 0, spoke, isSelection);
        }

        private static void SetSelectedArea(object dragger, CellRect rect)
        {
            // MUTATION-C: mirrors UnlimitedAreaDragger.Update's own write of SelectedArea
            // (allowtool UnlimitedAreaDragger.cs:111); the property is get-only from outside and
            // only a physical mouse drag ever fills it, so no A/B vehicle exists for a keyboard
            // rectangle. This is the mod's own input-surrogate object, not persistent game state.
            AllowToolCompat.SelectedAreaField.SetValue(dragger, rect);
        }

        private static void SetInProgress(object dragger, bool inProgress)
        {
            // MUTATION-C: mirrors UnlimitedAreaDragger's own writes of SelectionInProgress
            // (allowtool UnlimitedAreaDragger.cs:47 on drag start, :63 on drag end). Same
            // reasoning as SetSelectedArea: mouse-drag-only input state on the mod's helper.
            AllowToolCompat.SelectionInProgressField.SetValue(dragger, inProgress);
        }

        private static void SetStartCell(object dragger, IntVec3 cell)
        {
            // MUTATION-C: mirrors UnlimitedAreaDragger's own writes of SelectionStartCell
            // (allowtool UnlimitedAreaDragger.cs:48 on drag start, :65 on drag end). Same
            // reasoning as SetSelectedArea: mouse-drag-only input state on the mod's helper.
            AllowToolCompat.SelectionStartCellField.SetValue(dragger, cell);
        }

        /// <summary>A null delegate means nobody subscribed, which is normal rather than an error.</summary>
        private static void RaiseDragEvent(FieldInfo field, object dragger, CellRect rect)
        {
            Action<CellRect> handler = field.GetValue(dragger) as Action<CellRect>;
            if (handler != null)
                handler(rect);
        }
    }
}
