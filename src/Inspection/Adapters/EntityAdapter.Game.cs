using RimWorld;
using Verse;

namespace RimWorldAccess
{
    /// <summary>
    /// Opens the anomaly Entity tab state for a held pawn, or for the holding
    /// platform itself when the platform is the inspected target.
    /// </summary>
    internal sealed class EntityAdapter : InspectNodeAdapter
    {
        public override string CategoryKey => "Entity";

        public override TabHandlerType Handler => TabHandlerType.Action;

        public override void ExecuteAction(object obj)
        {
            if (obj is Pawn pawn && pawn.IsOnHoldingPlatform)
            {
                EntityTabState.Open(pawn);
                return;
            }

            // Held-entity tab is also reachable when the inspected target is
            // the holding platform itself.
            if (obj is Thing entityHolder)
            {
                EntityTabState.Open(entityHolder);
            }
        }
    }
}
