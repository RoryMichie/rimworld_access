using HarmonyLib;
using RimWorld;

namespace RimWorldAccess
{
    /// <summary>
    /// Opens the accessible architect tree whenever the real architect panel is open, so the panel
    /// and the tree stay one surface however the player opened it.
    ///
    /// Every other linked main tab already has this hook (QuestMenuPatch, and its siblings for
    /// Work, Assign, Animals, Wildlife, Mechs, Research, Factions): the state is opened from the
    /// window's own draw, so a mouse click on the button, a vanilla hotkey and our own opener all
    /// land in the same place. Architect was the one tab whose state had only the keyboard opener
    /// (<c>MapScope.OnToggleArchitect</c> -> <c>ArchitectMenuPatch.OpenArchitectTreeMenu</c>), so
    /// clicking the button left the panel open with no tree, no scope and nothing said, while the
    /// arrows went on panning the map and IMGUI focus settled in the panel's own search field
    /// <see cref="Shell.MainTabWindowLink"/>'s reconcile could not recover it
    /// either: it only closes a window whose state was once seen active.
    ///
    /// The IsActive guard is load-bearing — this prefix runs every frame the panel is open. The
    /// placement-mode guard is the second half of it: picking a designator closes the tree and
    /// leaves <c>ArchitectState</c> in placement mode, and reopening the tree on top of that would
    /// cancel the placement the player just started.
    /// </summary>
    [HarmonyPatch(typeof(MainTabWindow_Architect), nameof(MainTabWindow_Architect.DoWindowContents))]
    public static class ArchitectWindowStatePatch
    {
        [HarmonyPrefix]
        public static void Prefix()
        {
            if (ArchitectTreeState.IsActive || ArchitectState.IsActive)
            {
                return;
            }

            ArchitectMenuPatch.OpenArchitectTreeMenu();
        }
    }
}
