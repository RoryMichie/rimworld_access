using RimWorld;
using Verse;

namespace RimWorldAccess
{
    /// <summary>
    /// Adapter for the Pen Animals tab; opens the thing-filter menu bound to the
    /// pen marker's animal filter, constrained to the fixed animal filter allowed for pens.
    /// </summary>
    internal sealed class PenAnimalsAdapter : InspectNodeAdapter
    {
        public override string CategoryKey => "Pen Animals";

        public override TabHandlerType Handler => TabHandlerType.Action;

        public override void ExecuteAction(object obj)
        {
            if (!(obj is Building building))
                return;

            var penMarker = building.TryGetComp<CompAnimalPenMarker>();
            if (penMarker != null)
            {
                ThingFilterMenuState.Open(penMarker.AnimalFilter, AnimalPenUtility.GetFixedAnimalFilter(), "Pen Animals");
            }
        }
    }
}
