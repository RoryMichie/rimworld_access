using Verse;

namespace RimWorldAccess
{
    /// <summary>
    /// Adapter for the Linked Facilities category. Shows facility provider
    /// info, consumer info, linked buildings, and compatible facilities for a
    /// building.
    /// </summary>
    internal sealed class LinkedFacilitiesAdapter : InspectNodeAdapter
    {
        public override string CategoryKey => "Linked Facilities";

        public override TabHandlerType Handler => TabHandlerType.RichNavigation;

        /// <summary>
        /// Never expandable: "Linked Facilities" was absent from the retired IsExpandableCategory list,
        /// so the node has always rendered through the detailed-info fallback (folded text from
        /// FacilityLinkHelper via GetCategoryInfo) rather than this adapter's rich builder. Kept false
        /// to preserve that behavior; flipping this to true would activate the rich view below — a lead
        /// decision, not a refactor.
        /// </summary>
        public override bool CanExpand(object obj)
        {
            return false;
        }

        public override void BuildChildren(InspectionTreeItem categoryItem, object obj, InspectionMode mode)
        {
            if (!(obj is Building building))
                return;
            BuildFacilityChildren(categoryItem, building);
        }

        /// <summary>
        /// Builds children for the Linked Facilities category.
        /// Shows facility provider info, consumer info, linked buildings, and compatible facilities.
        /// </summary>
        private static void BuildFacilityChildren(InspectionTreeItem parentItem, Building building)
        {
            if (parentItem.Children.Count > 0)
                return;

            int indent = parentItem.IndentLevel + 1;
            var entries = FacilityLinkHelper.GetFacilityEntries(building);

            if (entries.Count == 0)
            {
                InspectNodeFactory.Attach(parentItem, new InspectionTreeItem
                {
                    Type = InspectionTreeItem.ItemType.DetailText,
                    Label = "RimWorldAccess.Inspection.Tree.NoFacilityInfo".Translate(),
                    IndentLevel = indent,
                    IsExpandable = false
                });
                return;
            }

            foreach (var entry in entries)
            {
                InspectNodeFactory.Attach(parentItem, new InspectionTreeItem
                {
                    Type = InspectionTreeItem.ItemType.DetailText,
                    Label = entry.Label,
                    IndentLevel = indent,
                    IsExpandable = false
                });
            }
        }
    }
}
