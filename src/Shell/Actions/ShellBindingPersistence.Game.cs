using System.Collections.Generic;
using Verse;

namespace RimWorldAccess.Shell
{
    /// <summary>
    /// Bridges the pure BindingOverrideSet to the mod settings file. The
    /// override lines live in RimWorldAccessSettings (scribed with everything
    /// else); this class moves them between that list and the live catalog.
    ///
    /// Game-coupled (*.Game.cs — excluded from the test project). Load runs
    /// at startup so persisted rebinds survive round-trips, even while
    /// nothing yet dispatches from the catalog.
    /// </summary>
    public static class ShellBindingPersistence
    {
        // The set as loaded from disk, kept so entries for unknown action ids
        // (removed or not-yet-registered actions) survive a save round-trip.
        private static BindingOverrideSet loaded = new BindingOverrideSet();

        /// <summary>Applies persisted rebinds to the catalog. Call after inventory registration.</summary>
        public static void Load(ActionCatalog catalog)
        {
            RimWorldAccessSettings settings = RimWorldAccessMod_Settings.Settings;
            if (settings == null)
                return;
            loaded = BindingOverrideSet.FromLines(settings.ShellBindingOverrideLines);
            List<string> unknown = loaded.ApplyTo(catalog);
            if (unknown.Count > 0)
                Log.Message("[RimWorld Access] Kept " + unknown.Count + " key rebind(s) for unknown action ids (retained for forward compatibility).");
        }

        /// <summary>Captures the catalog's rebinds (deltas only) into settings and writes them.</summary>
        public static void Save(ActionCatalog catalog)
        {
            RimWorldAccessSettings settings = RimWorldAccessMod_Settings.Settings;
            if (settings == null)
                return;
            loaded = BindingOverrideSet.CaptureFrom(catalog, loaded);
            settings.ShellBindingOverrideLines = loaded.ToLines();
            settings.Write();
        }
    }
}
