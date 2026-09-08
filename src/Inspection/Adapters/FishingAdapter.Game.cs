using RimWorld;
using Verse;

namespace RimWorldAccess
{
    /// <summary>
    /// Adapter for fishing zones, opening the fishing zone menu when the category action is executed.
    /// </summary>
    internal sealed class FishingAdapter : InspectNodeAdapter
    {
        public override string CategoryKey => "Fishing";

        public override TabHandlerType Handler => TabHandlerType.Action;

        public override void ExecuteAction(object obj)
        {
            if (obj is Zone zone && zone is Zone_Fishing)
            {
                FishingZoneMenuState.Open(zone);
            }
        }
    }
}
