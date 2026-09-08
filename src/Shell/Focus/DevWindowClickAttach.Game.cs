using System;
using HarmonyLib;
using LudeonTK;
using Verse;

namespace RimWorldAccess.Shell
{
    /// <summary>
    /// Gives the dev-mode <see cref="EditWindow"/> family a MOUSE way in to the accessible readers
    /// they already have.
    ///
    /// Those windows attach their scope only for a deliberate open — the F12 &gt; Development
    /// entries arm a flag around the attach, and an uninvited open (the dev toolbar opening tweak
    /// values) deliberately gets no scope so a modal reader can never mask the surface the player
    /// is really driving. Correct, and it left one hole: a player who reaches an already-open dev
    /// window with the mouse had no way to hand it the keyboard.
    ///
    /// A click inside the window IS the deliberate act the arming flags are looking for, and
    /// <see cref="WindowStack.Notify_ClickedInsideWindow"/> is vanilla's own choke point for it:
    /// every window's own GUI pass calls it on each mouse-down inside its rect (decompiled
    /// Verse/Window.cs:224-227), whatever the click goes on to do. Arming both flags together
    /// is not a guess about which window was clicked — each factory is registered for one exact
    /// window type, so only the clicked window's own factory ever runs. The debug log is absent
    /// from the list because it now attaches on open, click or no click.
    ///
    /// Restricted to the dev family on purpose. Every other window either already attaches on Add
    /// or was refused by a posture test that a click says nothing about.
    /// </summary>
    [HarmonyPatch(typeof(WindowStack), nameof(WindowStack.Notify_ClickedInsideWindow))]
    internal static class DevWindowClickAttachPatch
    {
        [HarmonyPostfix]
        public static void Postfix(Window window)
        {
            try
            {
                if (!(window is EditWindow) || ScopeForWindow.HasAttachedScope(window))
                {
                    return;
                }

                DevTweakValuesScope.Arming = true;
                DevInspectorScope.Arming = true;
                try
                {
                    ScopeForWindow.AttachOnDemand(window);
                }
                finally
                {
                    DevTweakValuesScope.Arming = false;
                    DevInspectorScope.Arming = false;
                }
            }
            catch (Exception ex)
            {
                ModLogger.LimitedError("Dev window click attach error", ex);
            }
        }
    }
}
