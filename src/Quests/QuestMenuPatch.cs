using HarmonyLib;
using RimWorld;
using RimWorld.Planet;
using Verse;

namespace RimWorldAccess
{
    /// <summary>
    /// Opens the accessible screen for the Quests tab while the real vanilla window keeps drawing. This
    /// no longer replaces the window, it just makes sure the windowless state is active while it is
    /// open. The IsActive guard is load-bearing: this prefix now runs EVERY FRAME the window is open.
    /// </summary>
    [HarmonyPatch(typeof(MainTabWindow_Quests), nameof(MainTabWindow_Quests.DoWindowContents))]
    public static class QuestMenuPatch
    {
        [HarmonyPrefix]
        public static void Prefix()
        {
            if (QuestMenuState.IsActive)
            {
                return;
            }

            // If on the world map, switch to colony map first
            if (Find.World?.renderer?.wantedMode == WorldRenderMode.Planet)
            {
                CameraJumper.TryHideWorld();
                MapNavigationState.RestoreCursorForCurrentMap();
            }

            // A quest link pointed the window at one specific quest before its first pass
            // (Shell.QuestWindowSelectRequestPatch): land the session on that quest rather
            // than on the first row of the Available tab. Open() would clear the request.
            Quest requested = QuestMenuState.PendingSelectQuest;
            if (requested != null)
            {
                QuestMenuState.OpenAndSelectQuest(requested);
                return;
            }

            QuestMenuState.Open();
        }
    }
}
