using System;
using HarmonyLib;
using RimWorld;
using Verse;

namespace RimWorldAccess
{
    /// <summary>
    /// Lifecycle patches for Dialog_AnomalySettings — wires up the keyboard navigation state
    /// when the dialog opens, tears it down on close, and blocks the game's default Cancel
    /// handling so our state can fully own the keyboard.
    /// </summary>
    public static class AnomalySettingsDialogPatch
    {
        [HarmonyPatch(typeof(Window), "PostOpen")]
        public static class Window_PostOpen_AnomalySettings_Patch
        {
            [HarmonyPostfix]
            public static void Postfix(Window __instance)
            {
                if (!(__instance is Dialog_AnomalySettings dialog)) return;
                try
                {
                    AnomalySettingsDialogState.Open(dialog);
                }
                catch (Exception ex)
                {
                    Log.Error($"[AnomalySettingsDialogPatch] PostOpen failed: {ex.Message}");
                }
            }
        }

        [HarmonyPatch(typeof(Window), "PostClose")]
        public static class Window_PostClose_AnomalySettings_Patch
        {
            [HarmonyPostfix]
            public static void Postfix(Window __instance)
            {
                if (!(__instance is Dialog_AnomalySettings)) return;
                try
                {
                    if (AnomalySettingsDialogState.IsActive)
                    {
                        // Capture the close mode BEFORE Close() resets it.
                        bool wasAccept = AnomalySettingsDialogState.wasAcceptClose;
                        AnomalySettingsDialogState.Close();

                        if (wasAccept)
                        {
                            // On Accept, route the user back to the Storyteller row so Enter
                            // advances the wizard naturally. ReturnToStorytellerMode speaks the
                            // storyteller info, so the user knows where focus landed.
                            TolkHelper.SpeakData("RimWorldAccess.AnomalySettings.Saved".Translate((string)"AnomalySettings".Translate()));
                            StorytellerSelectionPatch.ReturnToStorytellerMode();
                        }
                        else
                        {
                            // On Esc/X (discard), keep the cursor on the AnomalySettings row.
                            TolkHelper.SpeakData("RimWorldAccess.AnomalySettings.Closed".Translate((string)"AnomalySettings".Translate()));
                        }
                    }
                }
                catch (Exception ex)
                {
                    Log.Error($"[AnomalySettingsDialogPatch] PostClose failed: {ex.Message}");
                }
            }
        }

        // RETIRED: the four blocker patches, replaced
        // by AnomalySettingsScope's ownership + stamps per the positional-audit
        // law (full reasoning in AnomalySettingsScope's class remarks). Each
        // retired gate, verbatim:
        //
        // Window.OnCancelKeyPressed / Window.OnAcceptKeyPressed:
        //   if (__instance is Dialog_AnomalySettings && AnomalySettingsDialogState.IsActive)
        //       return false;
        //   → replaced by OwnsCancel (default true) + OwnsAccept (true) driving
        //     the I1 Window router twins whenever the scope is the live top.
        //
        // Page.OnCancelKeyPressed:
        //   if (__instance is Page_SelectStoryteller &&
        //       (AnomalySettingsDialogState.IsActive ||
        //        AnomalySettingsDialogState.escapeHandledOnFrame == Time.frameCount))
        //       return false;
        //   → replaced by MarkCancelConsumed in the scope's Cancel handler +
        //     the I1 Page router twin / DoBottomButtons guard (the
        //     escapeHandledOnFrame arm's exact purpose — block the underlying
        //     page's same-frame Cancel re-test after the dialog closes).
        //
        // Page.OnAcceptKeyPressed:
        //   if (__instance is Page_SelectStoryteller && AnomalySettingsDialogState.IsActive)
        //       return false;
        //   → replaced by MarkAcceptConsumed in the scope's Enter handler +
        //     the I1 twins/guard.
    }
}
