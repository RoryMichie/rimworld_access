using RimWorld;
using Verse;

namespace RimWorldAccess
{
    /// <summary>
    /// Adapter for the synthetic Overview category: the object's inspect string flattened to a single
    /// inline line ("Overview: content"), verbatim from the retired IsSingleItemCategory path. The
    /// content itself comes from the base InlineContent pipeline over
    /// InspectionInfoHelper.GetCategoryInfo.
    /// </summary>
    internal sealed class OverviewAdapter : InspectNodeAdapter
    {
        public override string CategoryKey => "Overview";

        public override TabHandlerType Handler => TabHandlerType.RichNavigation;

        public override bool IsInline => true;
    }
}
