using RimWorld;
using Verse;

namespace RimWorldAccess
{
    /// <summary>
    /// Adapter for the animal pen "Pen Auto-Cut" category. Builds the
    /// auto-cut toggle, the Cut Now action, and the plant filter action.
    /// </summary>
    internal sealed class PenAutoCutAdapter : InspectNodeAdapter
    {
        public override string CategoryKey => "Pen Auto-Cut";

        public override TabHandlerType Handler => TabHandlerType.RichNavigation;

        /// <summary>
        /// Pen Auto-Cut expands only when the building carries a pen marker
        /// comp (verbatim from InspectionTreeBuilder.IsExpandableCategory, D3).
        /// </summary>
        public override bool CanExpand(object obj)
        {
            return obj is Building building && building.TryGetComp<CompAnimalPenMarker>() != null;
        }

        public override void BuildChildren(InspectionTreeItem categoryItem, object obj, InspectionMode mode)
        {
            if (!(obj is Building building))
                return;
            BuildPenAutoCutChildren(categoryItem, building);
        }

        /// <summary>
        /// Builds children for the Pen Auto-Cut category.
        /// Shows auto-cut toggle, Cut Now button, and plant filter access.
        /// </summary>
        private static void BuildPenAutoCutChildren(InspectionTreeItem parentItem, Building building)
        {
            var penMarker = building.TryGetComp<CompAnimalPenMarker>();
            if (penMarker == null)
                return;

            int indent = parentItem.IndentLevel + 1;
            bool penEnclosed = penMarker.PenState.Enclosed;

            // Auto-cut toggle
            string PenStateWord(bool enabled) => enabled
                ? "Enabled".Translate().ToString()
                : "Disabled".Translate().ToString();
            string PenUnenclosedSuffix(bool enclosed) => enclosed
                ? ""
                : "RimWorldAccess.Inspection.Tree.PenNotEnclosedSuffix".Translate().ToString();
            string PenAutoCutLabel(bool enabled, bool enclosed) =>
                "RimWorldAccess.Inspection.Tree.PenAutoCutPlantsLabel".Translate(
                    PenStateWord(enabled), PenUnenclosedSuffix(enclosed));

            var toggleItem = new InspectionTreeItem
            {
                Type = InspectionTreeItem.ItemType.Action,
                Label = PenAutoCutLabel(penMarker.autoCut, penEnclosed),
                IndentLevel = indent,
                IsExpandable = false
            };
            toggleItem.OnActivate = () =>
            {
                penMarker.autoCut = !penMarker.autoCut;
                string stateWord = PenStateWord(penMarker.autoCut);
                toggleItem.Label = PenAutoCutLabel(penMarker.autoCut, penMarker.PenState.Enclosed);
                TolkHelper.Speak("RimWorldAccess.Inspection.Tree.PenAutoCutToggleAnnouncement".Loc(stateWord));
            };
            InspectNodeFactory.Attach(parentItem, toggleItem);

            // Cut Now button
            var cutNowItem = new InspectionTreeItem
            {
                Type = InspectionTreeItem.ItemType.Action,
                Label = "RimWorldAccess.Inspection.Tree.PenCutNowLabel".Translate(
                    "AutoCutNow".Translate(),
                    PenUnenclosedSuffix(penEnclosed)),
                IndentLevel = indent,
                IsExpandable = false
            };
            cutNowItem.OnActivate = () =>
            {
                if (penMarker.PenState.Enclosed)
                {
                    penMarker.DesignatePlantsToCut();
                    TolkHelper.Speak("RimWorldAccess.Inspection.Tree.PenDesignatedPlantsForCutting".Loc());
                }
                else
                {
                    TolkHelper.Speak("AutocutUnenclosedPen".Loc());
                }
            };
            InspectNodeFactory.Attach(parentItem, cutNowItem);

            // Plant filter action
            var filterItem = new InspectionTreeItem
            {
                Type = InspectionTreeItem.ItemType.Action,
                Label = "RimWorldAccess.Inspection.Tree.PenPlantFilter".Translate(),
                IndentLevel = indent,
                IsExpandable = false
            };
            filterItem.OnActivate = () =>
            {
                var fixedFilter = penMarker.parent.Map?.animalPenManager?.GetFixedAutoCutFilter();
                ThingFilterMenuState.Open(penMarker.AutoCutFilter, fixedFilter, "RimWorldAccess.Inspection.Tree.PenAutoCutMenuTitle".Translate());
            };
            InspectNodeFactory.Attach(parentItem, filterItem);
        }
    }
}
