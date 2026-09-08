using RimWorld;
using Verse;

namespace RimWorldAccess
{
    /// <summary>
    /// Adapter for a pawn's synthetic "Work Priorities" inspection category:
    /// a read-only list of visible work types, assigned types (with
    /// priority) first, then capable-but-unassigned types.
    /// </summary>
    internal sealed class PawnWorkPrioritiesAdapter : InspectNodeAdapter
    {
        public override string CategoryKey => "Work Priorities";

        public override TabHandlerType Handler => TabHandlerType.BasicInspectString;

        /// <summary>
        /// Work Priorities renders inline ("Work Priorities: content") — verbatim from the retired
        /// IsSingleItemCategory list. The inline check short-circuits before expandability, so the rich
        /// builder below has never been reachable from the tree; kept for a lead decision on activating
        /// it.
        /// </summary>
        public override bool IsInline => true;

        public override void BuildChildren(InspectionTreeItem categoryItem, object obj, InspectionMode mode)
        {
            Pawn pawn = InspectionTreeBuilder.GetPawnFromThing(obj);
            if (pawn == null)
                return;
            BuildWorkPrioritiesChildren(categoryItem, pawn);
        }

        /// <summary>
        /// Builds read-only children for the synthetic "Work Priorities" category: one leaf row per
        /// visible work type the pawn is capable of, assigned types (with priority) first, then
        /// capable-but-unassigned types. Editing happens in the dedicated Work screen, not here.
        /// </summary>
        private static void BuildWorkPrioritiesChildren(InspectionTreeItem parentItem, Pawn pawn)
        {
            if (pawn?.workSettings == null)
                return;

            void AddRow(string label, WorkTypeDef def)
            {
                InspectNodeFactory.Attach(parentItem, new InspectionTreeItem
                {
                    Type = InspectionTreeItem.ItemType.Item,
                    Label = label,
                    Data = def,
                    IndentLevel = parentItem.IndentLevel + 1,
                    IsExpandable = false
                });
            }

            var workTypes = DefDatabase<WorkTypeDef>.AllDefsListForReading;

            // Assigned work types first, carrying their priority.
            foreach (var workType in workTypes)
            {
                if (!workType.visible || !pawn.workSettings.WorkIsActive(workType))
                    continue;

                int priority = pawn.workSettings.GetPriority(workType);
                string label = priority > 0
                    ? "RimWorldAccess.Pawns.WorkInfo.LinePriority".Translate(workType.labelShort, priority).ToString()
                    : "RimWorldAccess.Pawns.WorkInfo.LineEnabled".Translate(workType.labelShort).ToString();
                AddRow(label, workType);
            }

            // Then types the pawn could do but currently has switched off.
            foreach (var workType in workTypes)
            {
                if (!workType.visible || pawn.workSettings.WorkIsActive(workType) || pawn.WorkTypeIsDisabled(workType))
                    continue;

                AddRow("RimWorldAccess.Pawns.WorkInfo.LineUnassigned".Translate(workType.labelShort).ToString(), workType);
            }

            if (parentItem.Children.Count == 0)
                AddRow("RimWorldAccess.Pawns.WorkInfo.NoWork".Translate().ToString(), null);
        }
    }
}
