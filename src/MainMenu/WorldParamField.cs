namespace RimWorldAccess
{
    /// <summary>
    /// Identity of each world-parameter field, the dispatch key shared by
    /// <see cref="WorldParamsFieldValues"/> (value/warning/mutation per field)
    /// and the world-params screen scope's Planet region. Odyssey/Biotech-gated
    /// members are only ever presented when the respective mod is active.
    ///
    /// Relocated here from the retired WorldParamsNavigationState (which owned
    /// the field cursor the ScreenScope's region model now replaces) so
    /// <see cref="WorldParamsFieldValues"/> keeps its unchanged dependency.
    /// </summary>
    internal enum WorldParamField
    {
        Seed,
        PlanetCoverage,
        Rainfall,
        Temperature,
        Population,
        LandmarkDensity,  // Odyssey only
        Pollution,        // Biotech only
        MapSize,          // Advanced
        StartingSeason    // Advanced
    }
}
