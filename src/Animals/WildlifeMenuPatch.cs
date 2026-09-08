using HarmonyLib;
using RimWorld;
using RimWorld.Planet;
using Verse;

namespace RimWorldAccess
{
    /// <summary>
    /// Opens the accessible screen for the Wildlife tab while the real vanilla window keeps drawing.
    /// This no longer replaces the window, it just makes sure the windowless state is active while it
    /// is open. Since MainTabWindow_Wildlife doesn't override DoWindowContents, we patch the base class
    /// MainTabWindow_PawnTable.DoWindowContents and check the instance type. The IsActive guard is
    /// load-bearing: this prefix now runs EVERY FRAME the window is open, not just once.
    /// </summary>
    [HarmonyPatch(typeof(MainTabWindow_PawnTable), nameof(MainTabWindow_PawnTable.DoWindowContents))]
    public static class WildlifeMenuPatch
    {
        [HarmonyPrefix]
        public static void Prefix(MainTabWindow_PawnTable __instance)
        {
            if (!(__instance is MainTabWindow_Wildlife) || WildlifeMenuState.IsActive)
            {
                return;
            }

            if (Find.World?.renderer?.wantedMode == WorldRenderMode.Planet)
            {
                CameraJumper.TryHideWorld();
                MapNavigationState.RestoreCursorForCurrentMap();
            }

            WildlifeMenuState.Open();

            // Open refuses (and announces) for an empty roster — take the window with it.
            if (!WildlifeMenuState.IsActive)
            {
                Shell.MainTabWindowLink.CloseTab(Shell.MainTabWindowLink.Wildlife);
            }
        }
    }
}
