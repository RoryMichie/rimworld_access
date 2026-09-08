using HarmonyLib;
using RimWorld;
using RimWorld.Planet;
using Verse;

namespace RimWorldAccess
{
    /// <summary>
    /// Opens the accessible screen for the Factions tab while the real vanilla window keeps drawing.
    /// This no longer replaces the window, it just makes sure the windowless state is active while it
    /// is open. The IsActive guard is load-bearing: this prefix now runs EVERY FRAME the window is
    /// open.
    /// </summary>
    [HarmonyPatch(typeof(MainTabWindow_Factions), nameof(MainTabWindow_Factions.DoWindowContents))]
    public static class FactionTabPatch
    {
        [HarmonyPrefix]
        public static void Prefix()
        {
            if (FactionTabState.IsActive)
            {
                return;
            }

            // If on the world map, switch to colony map first
            if (Find.World?.renderer?.wantedMode == WorldRenderMode.Planet)
            {
                CameraJumper.TryHideWorld();
                MapNavigationState.RestoreCursorForCurrentMap();
            }

            FactionTabState.Open();
        }
    }
}
