using RimWorld;
using Verse;

namespace RimWorldAccess
{
    /// <summary>
    /// Adapter for the biosculpter pod's nutrition storage tab. Resolves storage settings
    /// the same way as <see cref="StorageAdapter"/>; priority-row visibility is read from the
    /// pod's own ITab_BiosculpterNutritionStorage via <see cref="StorageAdapter.ResolvePriorityVisible"/>
    /// rather than hand-mirrored here.
    /// </summary>
    internal sealed class NutritionStorageAdapter : InspectNodeAdapter
    {
        public override string CategoryKey => "Nutrition Storage";

        public override TabHandlerType Handler => TabHandlerType.Action;

        public override void ExecuteAction(object obj)
        {
            if (!(obj is Building building))
                return;

            var storageParent = StorageAdapter.ResolveStoreSettingsParent(building);
            var settings = storageParent?.GetStoreSettings();
            if (settings != null)
            {
                StorageSettingsMenuState.Open(settings, StorageAdapter.ResolvePriorityVisible(building));
            }
        }
    }
}
