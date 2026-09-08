using HarmonyLib;
using RimWorld;
using RimWorld.Planet;
using Verse;

namespace RimWorldAccess
{
    /// <summary>
    /// Opens the accessible screen for the Assign tab while the real vanilla window keeps drawing. This
    /// no longer replaces the window, it just makes sure the windowless state is active while it is
    /// open. The IsActive guard is load-bearing: this prefix now runs EVERY FRAME the window is open,
    /// not just once. Since MainTabWindow_Assign doesn't override DoWindowContents, this patches the
    /// base class MainTabWindow_PawnTable.DoWindowContents and checks the instance type.
    /// </summary>
    [HarmonyPatch(typeof(MainTabWindow_PawnTable), nameof(MainTabWindow_PawnTable.DoWindowContents))]
    public static class AssignWindowInterceptPatch
    {
        [HarmonyPrefix]
        public static void Prefix(MainTabWindow_PawnTable __instance)
        {
            if (!(__instance is MainTabWindow_Assign) || AssignMenuState.IsActive)
            {
                return;
            }

            if (Find.World?.renderer?.wantedMode == WorldRenderMode.Planet)
            {
                CameraJumper.TryHideWorld();
                MapNavigationState.RestoreCursorForCurrentMap();
            }

            AssignMenuState.Open();

            // Open refuses (and announces) for an empty roster — take the window with it.
            if (!AssignMenuState.IsActive)
            {
                Shell.MainTabWindowLink.CloseTab(Shell.MainTabWindowLink.Assign);
            }
        }
    }
}
