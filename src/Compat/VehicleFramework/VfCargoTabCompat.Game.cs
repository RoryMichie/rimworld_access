namespace RimWorldAccess
{
    /// <summary>
    /// Registers the inspection-tree adapter for Vehicle Framework's cargo tab
    /// (Vehicles.ITab_Vehicle_Cargo) and its base airdrop-container storage tab
    /// (Vehicles.ITab_Airdrop_Container). One adapter instance serves both tab types.
    /// </summary>
    internal static class VfCargoTabCompat
    {
        public static void RegisterTabAdapters()
        {
            CompatRegistration.TabAdapter("Vehicles.ITab_Vehicle_Cargo",
                t => new VfCargoTabAdapter(), "VF cargo tab compat", "Vehicles.ITab_Airdrop_Container");
        }
    }
}
