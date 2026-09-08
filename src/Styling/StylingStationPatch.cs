using System;
using HarmonyLib;
using RimWorld;
using Verse;

namespace RimWorldAccess
{
    /// <summary>
    /// Harmony lifecycle patches for vanilla's <c>Dialog_StylingStation</c>. Opens
    /// <see cref="StylingStationState"/> when the dialog opens and closes it when the
    /// dialog closes. The Escape/Enter key-blocker prefixes are retired — see their own
    /// tombstone comment below; key routing now lives in
    /// StylingStationScope (src/Shell/Screens/StylingStationScope.Game.cs).
    /// </summary>
    public static class StylingStationPatch
    {
        [HarmonyPatch(typeof(Window), "PostOpen")]
        public static class Window_PostOpen_Styling_Patch
        {
            [HarmonyPostfix]
            public static void Postfix(Window __instance)
            {
                if (!(__instance is Dialog_StylingStation dialog))
                    return;
                try
                {
                    StylingStationState.Open(dialog);
                }
                catch (Exception ex)
                {
                    Log.Error($"[StylingStationPatch] Error in PostOpen: {ex.Message}");
                }
            }
        }

        [HarmonyPatch(typeof(Window), "PostClose")]
        public static class Window_PostClose_Styling_Patch
        {
            [HarmonyPostfix]
            public static void Postfix(Window __instance)
            {
                if (!(__instance is Dialog_StylingStation))
                    return;
                try
                {
                    if (StylingStationState.IsActive)
                    {
                        StylingStationState.Close();
                        TolkHelper.Speak("RimWorldAccess.Styling.Closed".Loc());
                    }
                }
                catch (Exception ex)
                {
                    Log.Error($"[StylingStationPatch] Error in PostClose: {ex.Message}");
                }
            }
        }

        // RETIRED: Window_OnCancelKeyPressed_Styling_Patch and
        // Window_OnAcceptKeyPressed_Styling_Patch. Replaced by StylingStationScope's own
        // Cancel/Activate claims stamping ShellFrameStamps, consulted by the shell's
        // consolidated WindowCancelKeyRouterPatch/WindowAcceptKeyRouterPatch twins.
        // Decompiled-verified: Dialog_StylingStation overrides neither
        // OnCancelKeyPressed nor OnAcceptKeyPressed, and both base Window bodies are
        // already inert for this dialog (closeOnCancel = false; closeOnAccept = false;
        // set in its own ctor) -- these two prefixes were blocking an already-no-op
        // body even before this slice; the router twins now do the same "belt, not
        // load-bearing" job. Retired, verbatim:
        //   [HarmonyPatch(typeof(Window), "OnCancelKeyPressed")]
        //   public static class Window_OnCancelKeyPressed_Styling_Patch
        //   {
        //       [HarmonyPrefix]
        //       public static bool Prefix(Window __instance)
        //       {
        //           if (__instance is Dialog_StylingStation && StylingStationState.IsActive)
        //               return false;
        //           return true;
        //       }
        //   }
        //   [HarmonyPatch(typeof(Window), "OnAcceptKeyPressed")]
        //   public static class Window_OnAcceptKeyPressed_Styling_Patch
        //   {
        //       [HarmonyPrefix]
        //       public static bool Prefix(Window __instance)
        //       {
        //           if (__instance is Dialog_StylingStation && StylingStationState.IsActive)
        //               return false;
        //           return true;
        //       }
        //   }
    }
}
