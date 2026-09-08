using System.Collections.Generic;
using RimWorld;
using RimWorld.Planet;

namespace RimWorldAccess
{
    /// <summary>
    /// The single point of reflected access into the live Page_CreateWorldParams
    /// instance, shared by WorldParamsNavigationState (world-gen sliders + seed) and
    /// FactionsNavigationState (the factions list) plus its FactionAddMenuState
    /// helper. Before this split each state reflected into the SAME instance
    /// independently -- two separate VanillaAccess.GetField(typeof(Page_CreateWorldParams), ...)
    /// call chains for the page they jointly own. VanillaAccess already caches each
    /// FieldInfo by (type, name), so this bridge's value is a single named surface,
    /// not a second cache layer.
    ///
    /// Bind/Unbind are idempotent field assignments -- both navigation states call
    /// them every DoWindowContents frame (WorldParamsPatch.Prefix) and on every
    /// PreOpen/PreClose reset, mirroring how each used to assign/null its own
    /// currentInstance field.
    /// </summary>
    internal static class WorldParamsPageBridge
    {
        private static Page_CreateWorldParams instance;

        public static bool IsBound => instance != null;

        public static void Bind(Page_CreateWorldParams page) => instance = page;

        public static void Unbind() => instance = null;

        // ===== SLIDERS / DROPDOWNS (Page_CreateWorldParams's own private fields) =====

        public static float PlanetCoverage
        {
            get => instance == null ? default : (float)VanillaAccess.GetField(typeof(Page_CreateWorldParams), "planetCoverage").GetValue(instance);
            set { if (instance != null) VanillaAccess.GetField(typeof(Page_CreateWorldParams), "planetCoverage").SetValue(instance, value); }
        }

        public static OverallRainfall Rainfall
        {
            get => instance == null ? default : (OverallRainfall)VanillaAccess.GetField(typeof(Page_CreateWorldParams), "rainfall").GetValue(instance);
            set { if (instance != null) VanillaAccess.GetField(typeof(Page_CreateWorldParams), "rainfall").SetValue(instance, value); }
        }

        public static OverallTemperature Temperature
        {
            get => instance == null ? default : (OverallTemperature)VanillaAccess.GetField(typeof(Page_CreateWorldParams), "temperature").GetValue(instance);
            set { if (instance != null) VanillaAccess.GetField(typeof(Page_CreateWorldParams), "temperature").SetValue(instance, value); }
        }

        public static OverallPopulation Population
        {
            get => instance == null ? default : (OverallPopulation)VanillaAccess.GetField(typeof(Page_CreateWorldParams), "population").GetValue(instance);
            set { if (instance != null) VanillaAccess.GetField(typeof(Page_CreateWorldParams), "population").SetValue(instance, value); }
        }

        public static LandmarkDensity LandmarkDensity
        {
            get => instance == null ? default : (LandmarkDensity)VanillaAccess.GetField(typeof(Page_CreateWorldParams), "landmarkDensity").GetValue(instance);
            set { if (instance != null) VanillaAccess.GetField(typeof(Page_CreateWorldParams), "landmarkDensity").SetValue(instance, value); }
        }

        public static float Pollution
        {
            get => instance == null ? default : (float)VanillaAccess.GetField(typeof(Page_CreateWorldParams), "pollution").GetValue(instance);
            set { if (instance != null) VanillaAccess.GetField(typeof(Page_CreateWorldParams), "pollution").SetValue(instance, value); }
        }

        public static string SeedString
        {
            get => instance == null ? null : (string)VanillaAccess.GetField(typeof(Page_CreateWorldParams), "seedString").GetValue(instance);
            set { if (instance != null) VanillaAccess.GetField(typeof(Page_CreateWorldParams), "seedString").SetValue(instance, value); }
        }

        // ===== FACTIONS =====

        /// <summary>
        /// The page's live "factions" list -- the same List&lt;FactionDef&gt; instance
        /// vanilla's own UI mutates, so Add/RemoveAt here are visible to it immediately.
        /// Returns an empty (non-null) list when unbound, matching every prior call
        /// site's own "if (currentInstance == null) return new List&lt;FactionDef&gt;()" guard.
        /// </summary>
        public static List<FactionDef> Factions =>
            instance == null ? new List<FactionDef>() : (List<FactionDef>)VanillaAccess.GetField(typeof(Page_CreateWorldParams), "factions").GetValue(instance);

        /// <summary>Category B (Page_CreateWorldParams.ResetFactionCounts is private, no public wrapper) -- vanilla's "Reset factions" button.</summary>
        public static void ResetFactionCounts() => VanillaAccess.GetMethod(typeof(Page_CreateWorldParams), "ResetFactionCounts").Invoke(instance, null);

        // ===== FULL RESET =====

        /// <summary>Category A -- Page_CreateWorldParams's own public Reset(), vanilla's "Reset all" button.</summary>
        public static void ResetPage() => instance?.Reset();
    }
}
