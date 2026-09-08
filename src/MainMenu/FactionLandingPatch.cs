using HarmonyLib;
using RimWorld;
using Verse;

namespace RimWorldAccess
{
    /// <summary>
    /// Harmony patches for Dialog_FactionDuringLanding to enable keyboard accessibility.
    /// Uses Window.PostOpen/PostClose lifecycle (same pattern as InfoCardPatch).
    /// </summary>
    public static class FactionLandingPatch
    {
        /// <summary>
        /// Postfix patch for Window.PostOpen to activate keyboard navigation when
        /// Dialog_FactionDuringLanding opens. Multiple postfixes on Window.PostOpen
        /// are safe — InfoCardPatch also patches this method with a type check.
        /// </summary>
        [HarmonyPatch(typeof(Window), "PostOpen")]
        public static class Window_PostOpen_Patch
        {
            [HarmonyPostfix]
            public static void Postfix(Window __instance)
            {
                if (__instance is Dialog_FactionDuringLanding dialog)
                {
                    FactionLandingState.Open(dialog);
                }
            }
        }

        /// <summary>
        /// Postfix patch for Window.PostClose to clean up accessibility state when
        /// Dialog_FactionDuringLanding closes.
        /// </summary>
        [HarmonyPatch(typeof(Window), "PostClose")]
        public static class Window_PostClose_Patch
        {
            [HarmonyPostfix]
            public static void Postfix(Window __instance)
            {
                if (__instance is Dialog_FactionDuringLanding && FactionLandingState.IsActive)
                {
                    FactionLandingState.Close();
                }
            }
        }

        // RETIRED: Page_OnCancelKeyPressed_Patch,
        // the Page.OnCancelKeyPressed blocker. Gate verbatim:
        //   if (__instance is Page_SelectStartingSite &&
        //       (FactionLandingState.IsActive || FactionLandingState.escapeHandledOnFrame == Time.frameCount))
        //   {
        //       return false;
        //   }
        //   return true;
        // Per-term audit: the blocked body is Page's own OnCancelKeyPressed,
        // whose entire content is gated on closeOnCancel — false for every
        // Page (the ctor sets it; Page_SelectStartingSite has no
        // OnCancelKeyPressed override, decompiled-verified) — so BOTH
        // terms guarded a no-op; the blocker's own comment ("Notify_PressedCancel
        // ... would call DoBack()") described a path that never ran. The
        // page's REAL Escape=Back is the MAIN-pass raw Cancel poll in
        // DoCustomBottomButtons (ExtraOnGUI), and the real protection was
        // always the -0.22 rung's same-pass Event.current.Use() — now the
        // dispatcher's Use() when FactionLandingScope's Cancel claim
        // consumes (KeyBindingDef.KeyDownEvent returns false for a Use()d
        // event within the same pass), plus the claim's CancelConsumed stamp
        // as belt. escapeHandledOnFrame died with this, its only consumer.
    }
}
