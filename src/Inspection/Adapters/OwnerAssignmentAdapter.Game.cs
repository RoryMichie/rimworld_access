using RimWorld;
using Verse;

namespace RimWorldAccess
{
    /// <summary>
    /// Synthetic "Owner Assignment" category adapter. Opens the vanilla owner-assignment
    /// dialog for non-bed assignable buildings (e.g. thrones) that expose a
    /// <see cref="CompAssignableToPawn"/>.
    /// </summary>
    internal sealed class OwnerAssignmentAdapter : InspectNodeAdapter
    {
        public override string CategoryKey => "Owner Assignment";

        public override TabHandlerType Handler => TabHandlerType.Action;

        public override void ExecuteAction(object obj)
        {
            if (!(obj is Building building))
                return;

            var comp = (building as ThingWithComps)?.TryGetComp<CompAssignableToPawn>();
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
