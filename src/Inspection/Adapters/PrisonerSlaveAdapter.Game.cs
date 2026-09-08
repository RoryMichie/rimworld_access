using RimWorld;
using Verse;

namespace RimWorldAccess
{
    /// <summary>
    /// Inspect-node adapter serving both the Prisoner and Slave tabs, opening PrisonerTabState.
    /// </summary>
    internal sealed class PrisonerSlaveAdapter : InspectNodeAdapter
    {
        private readonly string categoryKey;

        public PrisonerSlaveAdapter(string categoryKey)
        {
            this.categoryKey = categoryKey;
        }

        public override string CategoryKey => categoryKey;

        public override TabHandlerType Handler => TabHandlerType.Action;

        public override void ExecuteAction(object obj)
        {
            if (obj is Pawn pawn && (pawn.IsPrisonerOfColony || pawn.IsSlaveOfColony))
            {
                PrisonerTabState.Open(pawn);
            }
        }
    }
}
