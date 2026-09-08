using RimWorld;
using Verse;

namespace RimWorldAccess
{
    /// <summary>
    /// Synthetic "Bed Assignment" category adapter - opens the vanilla owner-assignment dialog for beds.
    /// </summary>
    internal sealed class BedAssignmentAdapter : InspectNodeAdapter
    {
        public override string CategoryKey => "Bed Assignment";

        public override TabHandlerType Handler => TabHandlerType.Action;

        public override void ExecuteAction(object obj)
        {
            if (!(obj is Building_Bed bed))
                return;

            var comp = bed.TryGetComp<CompAssignableToPawn>();
            if (comp != null)
            {
                Find.WindowStack.Add(new Dialog_AssignBuildingOwner(comp));
            }
            else
            {
                TolkHelper.Speak("RimWorldAccess.Guard.NoBuildingToConfigure".Loc());
            }
        }
    }
}
