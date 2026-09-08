#if DEBUG
using HarmonyLib;
using Verse;

namespace RimWorldAccess.DevBridge
{
    /// <summary>
    /// Drives the dev bridge from RimWorld's main-thread render loop. UIRootOnGUI runs every
    /// frame at both the main menu and in-game, so this both lazily starts the server and
    /// drains any eval work the HTTP thread has queued. Debug builds only.
    /// </summary>
    [HarmonyPatch(typeof(UIRoot), "UIRootOnGUI")]
    internal static class DevBridgeDrainPatch
    {
        [HarmonyPostfix]
        public static void Postfix()
        {
            DevBridgeServer.EnsureStarted();
            MainThreadDispatcher.DrainPending();

            // Ticks the multi-frame sequence engine once per real frame, after any /inject arm request
            // queued this pass has had a chance to run — so a "wait N" between two steps is N real
            // frames, not zero (see ShellDev.Sequence.Game.cs's header for why that matters).
            RimWorldAccess.Shell.ShellDev.AdvanceSequence();
        }
    }
}
#endif
