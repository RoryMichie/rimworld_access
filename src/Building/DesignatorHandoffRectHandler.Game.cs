using System;
using System.Collections.Generic;
using RimWorld;
using Verse;

namespace RimWorldAccess
{
    /// <summary>
    /// Rect handler for eyedropper-style designators: one cell of work, and the designator's own
    /// DesignateSingleCell selects ANOTHER designator (vanilla's Designator_Eyedropper picks a
    /// paint or carpet; Keyz' Allow Utilities' floor picker picks the floor under the cell).
    /// The per-cell placement path cannot host that swap: it would keep iterating and finalizing
    /// on the picker after DesignatorManagerPatch has already restarted placement on the picked
    /// tool. Here the click is a single vanilla-shaped gate-and-designate, and the restart the
    /// mod's own Select triggers is left to stand.
    /// </summary>
    public sealed class DesignatorHandoffRectHandler : IRectDesignationHandler
    {
        private readonly Type designatorType;

        public DesignatorHandoffRectHandler(Type designatorType)
        {
            this.designatorType = designatorType;
        }

        public bool Handles(Designator designator)
        {
            return designator != null && designatorType != null && designatorType.IsInstanceOfType(designator);
        }

        public RectDesignationResult Designate(Designator designator, CellRect rect, IntVec3 firstCorner,
            IReadOnlyList<IntVec3> cells)
        {
            if (Find.CurrentMap == null)
                return RectDesignationResult.NotHandled;

            long messagesBefore = NotificationAccessibilityPatch.MessageEmissionCount;

            // Vehicle B, mirroring the single click in DesignatorManager.ProcessInputEvents: the
            // designator's own CanDesignateCell decides, and either branch finalizes.
            AcceptanceReport report = designator.CanDesignateCell(firstCorner);
            if (report.Accepted)
            {
                designator.DesignateSingleCell(firstCorner);
                designator.Finalize(true);
            }
            else
            {
                if (!string.IsNullOrEmpty(report.Reason))
                    Messages.Message(report.Reason, MessageTypeDefOf.SilentInput, false);
                else
                    TolkHelper.Speak("RimWorldAccess.Building.Architect.CannotDesignateHere".Loc());
                designator.Finalize(false);
            }

            bool spoke = !report.Accepted
                || NotificationAccessibilityPatch.MessageEmissionCount != messagesBefore;
            return new RectDesignationResult(true, 0, spoke, false);
        }
    }
}
