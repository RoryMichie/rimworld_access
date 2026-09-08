using HarmonyLib;
using RimWorld;
using RimWorld.Planet;
using Verse;

namespace RimWorldAccess
{
    /// <summary>
    /// Opens the accessible screen for the Animals tab while the real vanilla window keeps drawing.
    /// This no longer replaces the window, it just makes sure the windowless state is active while it
    /// is open. The IsActive guard is load-bearing: this prefix now runs EVERY FRAME the window is
    /// open, not just once.
    /// </summary>
    [HarmonyPatch(typeof(MainTabWindow_Animals), nameof(MainTabWindow_Animals.DoWindowContents))]
    public static class AnimalsMenuPatch
    {
        [HarmonyPrefix]
        public static void Prefix()
        {
            if (AnimalsMenuState.IsActive)
            {
                return;
            }

            // If on the world map, switch to colony map first
            if (Find.World?.renderer?.wantedMode == WorldRenderMode.Planet)
            {
                CameraJumper.TryHideWorld();
                MapNavigationState.RestoreCursorForCurrentMap();
            }

            AnimalsMenuState.Open();

            // Open refuses (and announces) for an empty roster — take the window with it.
            if (!AnimalsMenuState.IsActive)
            {
                Shell.MainTabWindowLink.CloseTab(Shell.MainTabWindowLink.Animals);
            }
        }
    }
}
