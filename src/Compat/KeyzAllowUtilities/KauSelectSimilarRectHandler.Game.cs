using System.Collections.Generic;
using RimWorldAccess.Shell;
using Verse;

namespace RimWorldAccess
{
    /// <summary>
    /// Hands a keyboard rectangle to Select Similar through the mod's own DesignateMultiCell.
    /// The per-cell placement path would work cell by cell, but that bypasses the override that
    /// clears a cursor-seeded filter after each drag (so a second rectangle would keep the first
    /// one's item type) and would announce cells rather than the things selected.
    /// </summary>
    internal sealed class KauSelectSimilarRectHandler : IRectDesignationHandler
    {
        public bool Handles(Designator designator)
        {
            return designator != null
                && KauCompat.DesignatorGate.Ensure()
                && KauCompat.SelectSimilarType.IsInstanceOfType(designator);
        }

        public RectDesignationResult Designate(Designator designator, CellRect rect, IntVec3 firstCorner,
            IReadOnlyList<IntVec3> cells)
        {
            if (Find.CurrentMap == null || Find.Selector == null)
                return RectDesignationResult.NotHandled;

            int selectedBefore = Find.Selector.NumSelected;
            long messagesBefore = NotificationAccessibilityPatch.MessageEmissionCount;

            // With nothing selected the mod seeds its filter from the thing under the mouse
            // (Designator_SelectSimilar.GetFilter via UI.MouseCell), which for a mouse drag is the
            // drag's first cell; the override points that read at the first corner instead.
            DevToolTargeting.WithCursorOverride(firstCorner, delegate
            {
                designator.DesignateMultiCell(cells);
                return true;
            });

            int added = Find.Selector.NumSelected - selectedBefore;
            bool spoke = NotificationAccessibilityPatch.MessageEmissionCount != messagesBefore;
            return new RectDesignationResult(true, added > 0 ? added : 0, spoke, true);
        }
    }
}
