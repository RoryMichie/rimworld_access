using System;
using HarmonyLib;
using Verse;

namespace RimWorldAccess
{
    /// <summary>
    /// Wires <see cref="SliderDialogState"/> into the lifecycle of every <see cref="Dialog_Slider"/>.
    /// </summary>
    public static class SliderDialogPatch
    {
        [HarmonyPatch(typeof(Window), "PostOpen")]
        public static class Window_PostOpen_Slider_Patch
        {
            [HarmonyPostfix]
            public static void Postfix(Window __instance)
            {
                if (!(__instance is Dialog_Slider slider)) return;
                try { SliderDialogState.Open(slider); }
                catch (Exception ex) { Log.Error($"[SliderDialogPatch] PostOpen failed: {ex.Message}"); }
            }
        }

        [HarmonyPatch(typeof(Window), "PostClose")]
        public static class Window_PostClose_Slider_Patch
        {
            [HarmonyPostfix]
            public static void Postfix(Window __instance)
            {
                if (!(__instance is Dialog_Slider)) return;
                try
                {
                    if (SliderDialogState.IsActive)
                        SliderDialogState.Close();
                }
                catch (Exception ex) { Log.Error($"[SliderDialogPatch] PostClose failed: {ex.Message}"); }
            }
        }

        // RETIRED: Window_OnAcceptKeyPressed_Slider_Patch.
        // Replaced by SliderDialogScope's own Activate claim (stamps
        // ShellFrameStamps.MarkAcceptConsumed(), then calls SliderDialogState.Confirm())
        // plus OwnsAccept overridden to TRUE — genuinely load-bearing here (UNLIKE
        // Styling/StorytellerInGame's moot-but-harmless shape), since Dialog_Slider's
        // ctor never sets closeOnAccept away from Window's TRUE default, so the base
        // OnAcceptKeyPressed body really would close-without-confirm if not blocked.
        // Retired, verbatim:
        //   [HarmonyPatch(typeof(Window), "OnAcceptKeyPressed")]
        //   public static class Window_OnAcceptKeyPressed_Slider_Patch
        //   {
        //       [HarmonyPrefix]
        //       public static bool Prefix(Window __instance)
        //       {
        //           if (!(__instance is Dialog_Slider) || !SliderDialogState.IsActive) return true;
        //           SliderDialogState.Confirm();
        //           Event.current?.Use();
        //           return false; // Skip vanilla's close-without-confirm.
        //       }
        //   }
    }
}
