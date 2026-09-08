using RimWorld;
using Verse;

namespace RimWorldAccess
{
    /// <summary>
    /// Synthetic Plant Selection category adapter; opens the plant selection menu for
    /// <see cref="IPlantToGrowSettable"/> buildings.
    /// </summary>
    internal sealed class PlantSelectionAdapter : InspectNodeAdapter
    {
        public override string CategoryKey => "Plant Selection";

        public override TabHandlerType Handler => TabHandlerType.Action;

        public override void ExecuteAction(object obj)
        {
            if (obj is Building building && building is IPlantToGrowSettable plantGrower)
            {
                PlantSelectionMenuState.Open(plantGrower);
            }
        }
    }
}
