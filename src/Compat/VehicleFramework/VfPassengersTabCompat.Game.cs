using Verse;

namespace RimWorldAccess
{
    /// <summary>
    /// Registers the inspection-tree tab adapter for Vehicle Framework's passengers tab
    /// (Vehicles.ITab_Vehicle_Passengers).
    /// </summary>
    internal static class VfPassengersTabCompat
    {
        public static void RegisterTabAdapters()
        {
            CompatRegistration.TabAdapter("Vehicles.ITab_Vehicle_Passengers",
                t =>
                {
                    var adapter = new VfPassengersTabAdapter();
                    adapter.sharedTab = InspectTabManager.GetSharedInstance(t);
                    return adapter;
                },
                "VF passengers tab compat");
        }
    }
}
