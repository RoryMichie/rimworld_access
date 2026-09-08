using Verse;

namespace RimWorldAccess
{
    /// <summary>
    /// Registers the inspection-tree tab adapter for Vehicle Framework's upgrades tab
    /// (Vehicles.ITab_Vehicle_Upgrades).
    /// </summary>
    internal static class VfUpgradesTabCompat
    {
        public static void RegisterTabAdapters()
        {
            CompatRegistration.TabAdapter("Vehicles.ITab_Vehicle_Upgrades",
                t =>
                {
                    var adapter = new VfUpgradesTabAdapter();
                    adapter.sharedTab = InspectTabManager.GetSharedInstance(t);
                    return adapter;
                },
                "VF upgrades tab compat");
        }
    }
}
