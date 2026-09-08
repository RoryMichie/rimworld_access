using RimWorld;
using Verse;

namespace RimWorldAccess
{
    /// <summary>
    /// Synthetic "Temperature" category adapter - opens the temperature control menu
    /// for buildings equipped with a <see cref="CompTempControl"/> comp.
    /// </summary>
    internal sealed class TemperatureAdapter : InspectNodeAdapter
    {
        public override string CategoryKey => "Temperature";

        public override TabHandlerType Handler => TabHandlerType.Action;

        public override void ExecuteAction(object obj)
        {
            if (!(obj is Building building))
                return;

            var tempControl = building.TryGetComp<CompTempControl>();
            if (tempControl != null)
            {
                TempControlMenuState.Open(building);
            }
        }
    }
}
