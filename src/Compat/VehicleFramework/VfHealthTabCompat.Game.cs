namespace RimWorldAccess
{
    /// <summary>
    /// Registers the inspection-tree tab adapter for Vehicle Framework's health tab
    /// (Vehicles.ITab_Vehicle_Health).
    /// </summary>
    internal static class VfHealthTabCompat
    {
        public static void RegisterTabAdapters()
        {
            CompatRegistration.TabAdapter("Vehicles.ITab_Vehicle_Health",
                t => new VfHealthTabAdapter(), "VF health tab compat");
        }
    }
}
