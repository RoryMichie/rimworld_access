using System.Collections.Generic;
using RimWorld;
using Verse;

namespace RimWorldAccess
{
    /// <summary>
    /// Adapter for the Guest tab (rework §D2), covering non-prisoner,
    /// non-slave guests. Presents the pawn's medical care selector.
    /// </summary>
    internal sealed class GuestAdapter : InspectNodeAdapter
    {
        public override string CategoryKey => "Guest";

        public override TabHandlerType Handler => TabHandlerType.RichNavigation;

        public override void BuildChildren(InspectionTreeItem categoryItem, object obj, InspectionMode mode)
        {
            Pawn pawn = InspectionTreeBuilder.GetPawnFromThing(obj);
            if (pawn == null)
                return;

            BuildGuestChildren(categoryItem, pawn, mode);
        }

        /// <summary>
        /// Builds children for the Guest tab (non-prisoner, non-slave guests).
        /// Shows medical care selector.
        /// </summary>
        private static void BuildGuestChildren(InspectionTreeItem parentItem, Pawn pawn, InspectionMode mode)
        {
            if (parentItem.Children.Count > 0)
                return;

            int indent = parentItem.IndentLevel + 1;
            bool isReadOnly = (mode == InspectionMode.ReadOnly);

            if (pawn.playerSettings == null)
                return;

            // Medical care selector
            string careLabel = "AllowMedicine".Translate();
            string currentCare = pawn.playerSettings.medCare.GetLabel();

            var careItem = new InspectionTreeItem
            {
                Type = isReadOnly ? InspectionTreeItem.ItemType.DetailText : InspectionTreeItem.ItemType.Action,
                Label = $"{careLabel}: {currentCare}",
                IndentLevel = indent,
                IsExpandable = false
            };

            if (!isReadOnly)
            {
                careItem.OnActivate = () =>
                {
                    // Open float menu with all medical care options
                    var options = new List<FloatMenuOption>();
                    foreach (MedicalCareCategory care in System.Enum.GetValues(typeof(MedicalCareCategory)))
                    {
                        var localCare = care;
                        options.Add(new FloatMenuOption(localCare.GetLabel(), () =>
                        {
                            pawn.playerSettings.medCare = localCare;
                            careItem.Label = $"{careLabel}: {localCare.GetLabel()}";
                        }));
                    }
                    WindowlessFloatMenuState.Open(options, false);
                };
            }
            InspectNodeFactory.Attach(parentItem, careItem);
        }
    }
}
