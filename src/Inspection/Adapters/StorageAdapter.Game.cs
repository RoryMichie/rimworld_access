using HarmonyLib;
using RimWorld;
using Verse;

namespace RimWorldAccess
{
    /// <summary>
    /// Opens the storage settings menu for stockpile zones, storage buildings resolved the
    /// same way vanilla's ITab_Storage does, and non-building IStoreSettingsParent things
    /// (e.g. Blueprint_Storage).
    /// </summary>
    internal sealed class StorageAdapter : InspectNodeAdapter
    {
        public override string CategoryKey => "Storage";

        public override TabHandlerType Handler => TabHandlerType.Action;

        public override void ExecuteAction(object obj)
        {
            if (obj is Zone zone)
            {
                if (zone is IStoreSettingsParent zoneStorageParent)
                {
                    var settings = zoneStorageParent.GetStoreSettings();
                    if (settings != null)
                    {
                        StorageSettingsMenuState.Open(settings);
                    }
                }
                return;
            }

            // IStoreSettingsParent things that aren't Buildings (e.g., Blueprint_Storage).
            if (obj is IStoreSettingsParent storeParent && !(obj is Building))
            {
                var settings = storeParent.GetStoreSettings();
                if (settings != null)
                {
                    StorageSettingsMenuState.Open(settings);
                }
                return;
            }

            if (obj is Building building)
            {
                var storageParent = ResolveStoreSettingsParent(building);
                var settings = storageParent?.GetStoreSettings();
                if (settings != null)
                {
                    StorageSettingsMenuState.Open(settings, ResolvePriorityVisible(building));
                }
            }
        }

        /// <summary>
        /// Reads IsPrioritySettingVisible from the thing's own ITab_Storage instance (decompiled
        /// RimWorld/ITab_Storage.cs:87 and its ITab_Shells/ITab_BiosculpterNutritionStorage
        /// overrides) instead of hardcoding per entry point. Defaults true when no storage tab is
        /// found (matches the ITab_Storage base). Internal: shared with NutritionStorageAdapter.
        /// </summary>
        internal static bool ResolvePriorityVisible(Thing t)
        {
            var tabs = t.GetInspectTabs();
            if (tabs != null)
            {
                foreach (InspectTabBase tab in tabs)
                {
                    if (tab is ITab_Storage storageTab)
                    {
                        // IsPrioritySettingVisible is a protected virtual property; AccessTools
                        // resolves it against the base declaration and GetValue dispatches
                        // virtually, so the ITab_Shells/ITab_BiosculpterNutritionStorage
                        // overrides are honored.
                        return (bool)AccessTools
                            .Property(typeof(ITab_Storage), "IsPrioritySettingVisible")
                            .GetValue(storageTab);
                    }
                }
            }
            return true;
        }

        /// <summary>
        /// Resolves the <see cref="IStoreSettingsParent"/> for a thing the same way vanilla's
        /// ITab_Storage.GetThingOrThingCompStoreSettingsParent does: the thing itself, or the
        /// first comp implementing the interface (e.g. CompBiosculpterPod for a biosculpter
        /// pod's nutrition storage, which has no dedicated Building subclass).
        /// Internal: shared with NutritionStorageAdapter.
        /// </summary>
        internal static IStoreSettingsParent ResolveStoreSettingsParent(Thing t)
        {
            if (t is IStoreSettingsParent direct)
                return direct;

            if (t is ThingWithComps twc)
            {
                var comps = twc.AllComps;
                for (int i = 0; i < comps.Count; i++)
                {
                    if (comps[i] is IStoreSettingsParent fromComp)
                        return fromComp;
                }
            }

            return null;
        }
    }
}
