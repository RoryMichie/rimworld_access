using RimWorld;
using Verse;
using Verse.Sound;

namespace RimWorldAccess
{
    /// <summary>
    /// Adapter for the Wind Turbine Auto-Cut category. Provides the auto-cut
    /// toggle, a Cut Now action, and the plant filter for a building's
    /// <see cref="CompAutoCut"/>.
    /// </summary>
    internal sealed class WindTurbineAutoCutAdapter : InspectNodeAdapter
    {
        public override string CategoryKey => "Auto-Cut Plants";

        public override TabHandlerType Handler => TabHandlerType.RichNavigation;

        public override void BuildChildren(InspectionTreeItem categoryItem, object obj, InspectionMode mode)
        {
            if (!(obj is Building building))
                return;
            BuildWindTurbineAutoCutChildren(categoryItem, building, mode);
        }

        /// <summary>
        /// Builds children for the Wind Turbine Auto-Cut tab.
        /// Toggle for auto-cut, Cut Now action, and plant filter.
        /// </summary>
        private static void BuildWindTurbineAutoCutChildren(InspectionTreeItem parentItem, Building building, InspectionMode mode)
        {
            if (parentItem.Children.Count > 0)
                return;

            var autoCut = building.TryGetComp<CompAutoCut>();
            if (autoCut == null)
                return;

            int indent = parentItem.IndentLevel + 1;
            bool isReadOnly = (mode == InspectionMode.ReadOnly);

            // Auto-cut toggle
            string toggleLabel = "WindTurbineAutoCut_EnabledCheckbox".Translate();
            string stateStr = autoCut.autoCut ? "On".Translate().ToString() : "Off".Translate().ToString();

            var toggleItem = new InspectionTreeItem
            {
                Type = isReadOnly ? InspectionTreeItem.ItemType.DetailText : InspectionTreeItem.ItemType.Action,
                Label = $"{toggleLabel}: {stateStr}",
                IndentLevel = indent,
                IsExpandable = false
            };

            if (!isReadOnly)
            {
                toggleItem.OnActivate = () =>
                {
                    autoCut.autoCut = !autoCut.autoCut;
                    string newState = autoCut.autoCut ? "On".Translate().ToString() : "Off".Translate().ToString();
                    toggleItem.Label = $"{toggleLabel}: {newState}";
                    TolkHelper.SpeakData(newState);
                    SoundDefOf.Click.PlayOneShotOnCamera();
                };
            }
            InspectNodeFactory.Attach(parentItem, toggleItem);

            // Cut Now action
            if (!isReadOnly)
            {
                var cutNowItem = new InspectionTreeItem
                {
                    Type = InspectionTreeItem.ItemType.Action,
                    Label = "AutoCutNow".Translate(),
                    IndentLevel = indent,
                    IsExpandable = false
                };
                cutNowItem.OnActivate = () =>
                {
                    autoCut.DesignatePlantsToCut();
                    TolkHelper.Speak("AutoCutNow".Loc());
                    SoundDefOf.Designate_PlanAdd.PlayOneShotOnCamera();
                };
                InspectNodeFactory.Attach(parentItem, cutNowItem);
            }
        }
    }
}
