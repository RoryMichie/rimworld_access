using RimWorld;
using Verse;

namespace RimWorldAccess
{
    /// <summary>
    /// Adapter that opens the bills overlay menu for bill-giver buildings.
    /// </summary>
    internal sealed class BillsAdapter : InspectNodeAdapter
    {
        public override string CategoryKey => "Bills";

        public override TabHandlerType Handler => TabHandlerType.Action;

        public override void ExecuteAction(object obj)
        {
            if (obj is Building building && building is IBillGiver billGiver)
            {
                BillsMenuState.Open(billGiver, building.Position);
            }
        }
    }
}
