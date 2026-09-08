using System;
using System.Reflection;
using HarmonyLib;
using Verse;

namespace RimWorldAccess.Shell
{
    /// <summary>
    /// Names Rimworld Together's three BLANK click-handler buttons via
    /// <see cref="WidgetCapture.EnterBlankButtonName"/> brackets around the mod's own drawers.
    /// Each site draws its real handler as <c>Widgets.ButtonText(rect, "")</c> and paints the
    /// visible caption separately, so the working control captured as a nameless "button" while
    /// the audible caption was a cosmetic twin whose click does nothing:
    /// <list type="bullet">
    /// <item><c>PatchButton.DoPre</c> — main menu: blank 45x45 quick-connect handler; the
    /// cosmetic twin is the "▶" button its DoPost paints over the same rect.</item>
    /// <item><c>Patch_Page_SelectScenario_DoWindowContents.DoPre</c> — scenario page: blank
    /// bottom-left disconnect handler; the cosmetic twin is DoPost's own "Disconnect".</item>
    /// <item><c>PatchWhenHost.DoPre</c> — world-params page while hosting: blank overlay on
    /// vanilla's Next slot whose click builds and uploads the multiplayer world (vanilla's own
    /// Next underneath never fires for a mouse user — this handler consumes the click in event
    /// order).</item>
    /// </list>
    /// The bracket renames only what THAT drawer records; nothing the mod draws changes.
    /// </summary>
    internal static class RimworldTogetherBlankButtonCompat
    {
        private static readonly MethodInfo patchButtonDoPre;
        private static readonly MethodInfo scenarioPageDoPre;
        private static readonly MethodInfo whenHostDoPre;
        private static readonly bool ready;

        static RimworldTogetherBlankButtonCompat()
        {
            var surface = new ReflectionSurface("RimworldTogetherBlankButtonCompat");
            patchButtonDoPre = surface.Method(surface.Type("RTClient.Patches.Pages.PatchButton"), "DoPre");
            scenarioPageDoPre = surface.Method(surface.Type("RTClient.Patches.Pages.Patch_Page_SelectScenario_DoWindowContents"), "DoPre");
            whenHostDoPre = surface.Method(surface.Type("RTClient.Patches.Pages.PatchWhenHost"), "DoPre");
            ready = surface.Ready;
        }

        public static void Register(Harmony harmony)
        {
            if (!ready)
            {
                return;
            }
            try
            {
                harmony.Patch(patchButtonDoPre,
                    prefix: new HarmonyMethod(typeof(RimworldTogetherBlankButtonCompat), nameof(QuickConnectPrefix)),
                    finalizer: new HarmonyMethod(typeof(RimworldTogetherBlankButtonCompat), nameof(ExitFinalizer)));
                harmony.Patch(scenarioPageDoPre,
                    prefix: new HarmonyMethod(typeof(RimworldTogetherBlankButtonCompat), nameof(DisconnectPrefix)),
                    finalizer: new HarmonyMethod(typeof(RimworldTogetherBlankButtonCompat), nameof(ExitFinalizer)));
                harmony.Patch(whenHostDoPre,
                    prefix: new HarmonyMethod(typeof(RimworldTogetherBlankButtonCompat), nameof(HostWorldPrefix)),
                    finalizer: new HarmonyMethod(typeof(RimworldTogetherBlankButtonCompat), nameof(ExitFinalizer)));
            }
            catch (Exception ex)
            {
                ModLogger.Error("RimworldTogetherBlankButtonCompat: Register failed: " + ex.Message);
            }
        }

        public static void QuickConnectPrefix()
        {
            WidgetCapture.EnterBlankButtonName("RimWorldAccess.Compat.RimworldTogether.Blank.QuickConnect".Translate());
        }

        public static void DisconnectPrefix()
        {
            // The mod's own cosmetic twin caption, so the working row and the
            // painted caption a sighted player reads say the same word.
            WidgetCapture.EnterBlankButtonName("RimWorldAccess.Compat.RimworldTogether.Blank.Disconnect".Translate());
        }

        public static void HostWorldPrefix()
        {
            WidgetCapture.EnterBlankButtonName("RimWorldAccess.Compat.RimworldTogether.Blank.HostWorld".Translate());
        }

        /// <summary>Finalizer, not postfix: the bracket must unwind even when the mod's drawer throws mid-frame, or every later blank button this pass would inherit the name.</summary>
        public static void ExitFinalizer()
        {
            WidgetCapture.ExitBlankButtonName();
        }
    }
}
