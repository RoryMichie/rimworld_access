using HarmonyLib;
using RimWorld;
using RimWorld.Planet;
using Verse;

namespace RimWorldAccess
{
    /// <summary>
    /// Opens the accessible screen for the Research tab while the real vanilla window keeps drawing.
    /// This no longer replaces the window, it just makes sure the windowless state is active while it
    /// is open. The IsActive guard is load-bearing: this prefix now runs EVERY FRAME the window is
    /// open, and
    /// <see cref="WindowlessResearchMenuState.Open"/> speaks its title
    /// unconditionally, so re-entering it every pass would repeat the
    /// announcement forever. Open() cannot refuse (unlike Wildlife/Animals),
    /// so there is no empty-roster CloseTab branch here.
    /// </summary>
    [HarmonyPatch(typeof(MainTabWindow_Research), nameof(MainTabWindow_Research.DoWindowContents))]
    public static class ResearchMenuPatch
    {
        [HarmonyPrefix]
        public static void Prefix()
        {
            if (WindowlessResearchMenuState.IsActive)
            {
                return;
            }

            // If on the world map, switch to colony map first
            if (Find.World?.renderer?.wantedMode == WorldRenderMode.Planet)
            {
                CameraJumper.TryHideWorld();
                MapNavigationState.RestoreCursorForCurrentMap();
            }

            WindowlessResearchMenuState.Open();
        }
    }
}
