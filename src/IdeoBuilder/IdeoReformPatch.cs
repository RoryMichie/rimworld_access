using HarmonyLib;
using RimWorld;
using Verse;

namespace RimWorldAccess
{
    // Lifecycle patches for the in-game two-stage reform dialog. Keyboard input and Escape/Enter
    // ownership belong to RimWorldAccess.Shell.IdeoReformScreenScope, whose OwnsCancel/OwnsAccept
    // the shell's consolidated Window key routers consult directly; Dialog_ReformIdeo overrides
    // neither method, so no subtype twin is needed.

    /// <summary>
    /// Opens IdeoReformState for the shell scope. No IMGUI focus grab is needed: the dispatcher runs
    /// once per frame from UIRootOnGUI whatever window holds focus. Patches the declaring type,
    /// Window.PostOpen, which Dialog_ReformIdeo does not override.
    /// </summary>
    [HarmonyPatch(typeof(Window), "PostOpen")]
    public static class IdeoReformPatch_PostOpen
    {
        [HarmonyPostfix]
        static void Postfix(Window __instance)
        {
            if (__instance is Dialog_ReformIdeo reformDialog)
                IdeoReformState.EnsureOpen(reformDialog);
        }
    }

    [HarmonyPatch(typeof(Window), "PostClose")]
    public static class IdeoReformPatch_PostClose
    {
        [HarmonyPostfix]
        static void Postfix(Window __instance)
        {
            if (__instance is Dialog_ReformIdeo)
            {
                IdeoReformState.Close();
                // The dialog closing over a still-open overlay editor must not leave its IsActive
                // stuck true.
                IdeoBuilderOverlays.CloseAllOverlayEditors();
            }
        }
    }
}
