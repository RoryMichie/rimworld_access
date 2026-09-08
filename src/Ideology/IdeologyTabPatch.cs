using HarmonyLib;
using RimWorld;
using RimWorld.Planet;
using Verse;

namespace RimWorldAccess
{
    /// <summary>
    /// Per-frame sustainer maintenance for ritual ambience sound preview. Runs every frame so the
    /// sustainer stays alive while playing, independent of which host (in-game viewer, worldgen
    /// builder, reform dialog, Archonexus) is currently previewing it. Unchanged by the in-game
    /// viewer's ScreenScope retrofit.
    /// </summary>
    [HarmonyPatch(typeof(UIRoot_Play), nameof(UIRoot_Play.UIRootOnGUI))]
    public static class IdeologyRitualSoundMaintainPatch
    {
        [HarmonyPostfix]
        public static void Postfix()
        {
            RimWorldAccess.Shell.IdeoDetailsTreeRegion.MaintainRitualSound();
        }
    }

    /// <summary>
    /// The ONLY behavior the mod still needs to inject before <see cref="MainTabWindow_Ideos"/>'s own
    /// body draws: if the world map is showing, switch to the colony map first — the sole
    /// non-presentational side effect the retired windowless-hijack <c>Prefix</c> performed
    /// (decompiled-verified). Everything else that prefix used to do — opening
    /// <c>IdeologyTabState</c> and <c>Find.WindowStack.TryRemove(typeof(MainTabWindow_Ideos), ...)</c>
    /// — is GONE: the vanilla window now draws every frame, and
    /// <see cref="RimWorldAccess.Shell.IdeologyViewerScreenScope"/> attaches to it via
    /// <c>ScopeForWindow.Register</c> (src/Shell/Actions/ShellBootstrap.Game.cs) instead of hijacking
    /// it, so mod-added widgets on this tab surface through the captured-extras region for the first
    /// time.
    ///
    /// Patches the DECLARING type (<see cref="Window.PostOpen"/>), instance-gated — decompiled-
    /// verified neither <see cref="MainTabWindow_Ideos"/> nor its base <see cref="MainTabWindow"/>
    /// overrides <c>PostOpen</c>, so the base-type Harmony patch reaches it (the repo-wide
    /// inherited/overridden-method gotcha the project CLAUDE.md calls out).
    /// </summary>
    [HarmonyPatch(typeof(Window), nameof(Window.PostOpen))]
    public static class IdeologyViewerPreSwitchPatch
    {
        [HarmonyPostfix]
        public static void Postfix(Window __instance)
        {
            if (!(__instance is MainTabWindow_Ideos))
            {
                return;
            }

            if (Find.World?.renderer?.wantedMode == WorldRenderMode.Planet)
            {
                CameraJumper.TryHideWorld();
                MapNavigationState.RestoreCursorForCurrentMap();
            }
        }
    }
}
