using RimWorld;
using Verse;

namespace RimWorldAccess
{
    /// <summary>
    /// Synthetic "Rename" category adapter: opens the zone rename state for zones,
    /// or the pen rename state for pen-marker buildings.
    /// </summary>
    internal sealed class RenameAdapter : InspectNodeAdapter
    {
        public override string CategoryKey => "Rename";

        public override TabHandlerType Handler => TabHandlerType.Action;

        public override void ExecuteAction(object obj)
        {
            if (obj is Zone zone)
            {
                ZoneRenameState.Open(zone);
                return;
            }

            if (obj is Building building)
            {
                var penMarker = building.TryGetComp<CompAnimalPenMarker>();
                if (penMarker != null)
                {
                    PenRenameState.Open(penMarker);
                }
            }
        }
    }
}
