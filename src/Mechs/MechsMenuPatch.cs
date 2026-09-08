using HarmonyLib;
using RimWorld;
using RimWorld.Planet;
using Verse;

namespace RimWorldAccess
{
    /// <summary>
    /// Opens the accessible screen for the Mechs tab while the real vanilla window keeps drawing. This
    /// no longer replaces the window, it just makes sure the windowless state is active while it is
    /// open. The IsActive guard is load-bearing: this prefix now runs EVERY FRAME the window is open,
    /// not just once.
    /// </summary>
    [HarmonyPatch(typeof(MainTabWindow_Mechs), nameof(MainTabWindow_Mechs.DoWindowContents))]
    public static class MechsMenuPatch
    {
        [HarmonyPrefix]
        public static void Prefix()
        {
            if (MechsMenuState.IsActive)
            {
                return;
            }

            // If on the world map, switch to colony map first
            if (Find.World?.renderer?.wantedMode == WorldRenderMode.Planet)
            {
                CameraJumper.TryHideWorld();
                MapNavigationState.RestoreCursorForCurrentMap();
            }

            MechsMenuState.Open();

            // Open refuses (and announces) for an empty roster or missing Biotech — take the window with it.
            if (!MechsMenuState.IsActive)
            {
                Shell.MainTabWindowLink.CloseTab(Shell.MainTabWindowLink.Mechs);
            }
        }
    }
}
