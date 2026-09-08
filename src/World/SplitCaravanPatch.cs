using HarmonyLib;
using RimWorld.Planet;
using UnityEngine;
using Verse;

namespace RimWorldAccess
{
    /// <summary>
    /// Harmony patches for Dialog_SplitCaravan to enable keyboard navigation.
    /// Activates SplitCaravanState when the dialog opens and handles keyboard input.
    /// </summary>
    [HarmonyPatch(typeof(Dialog_SplitCaravan))]
    public static class SplitCaravanPatch
    {
        // The PostOpen open-state patch is retired: SplitCaravanScope (a
        // ScreenScope) self-constructs from the window via its ScopeForWindow
        // registration and announces on first focus.

        /// <summary>
        /// Patch for PostClose to deactivate keyboard navigation when the dialog closes.
        /// Note: We patch Window.PostClose because Dialog_SplitCaravan doesn't override it.
        /// Announces cancellation unless split was attempted (successful split announces itself).
        /// </summary>
        [HarmonyPatch(typeof(Window), "PostClose")]
        [HarmonyPostfix]
        public static void Window_PostClose_Postfix(Window __instance)
        {
            if (__instance is Dialog_SplitCaravan)
            {
                // Capture split state before Close() resets it
                bool wasSplitAttempted = SplitCaravanState.SplitAttempted;

                SplitCaravanState.Close();

                // Announce cancellation only if user didn't attempt to split
                // (successful split announces itself in SplitCaravanState.Split())
                if (!wasSplitAttempted)
                {
                    TolkHelper.Speak("RimWorldAccess.Caravan.Split.SplitCancelled".Loc());
                }
            }
        }

        /// <summary>
        /// Patch for DoWindowContents to draw visual indicators.
        /// Note: OnCancelKeyPressed is handled by CaravanFormationPatch.Window_OnCancelKeyPressed_Prefix
        /// which covers both Dialog_FormCaravan and Dialog_SplitCaravan.
        /// </summary>
        [HarmonyPatch("DoWindowContents")]
        [HarmonyPostfix]
        public static void DoWindowContents_Postfix(Dialog_SplitCaravan __instance, Rect inRect)
        {
            if (!SplitCaravanState.IsActive)
                return;

            DrawKeyboardModeIndicator(inRect);
        }

        /// <summary>
        /// Draws a visual indicator at the top of the dialog showing that keyboard mode is active.
        /// </summary>
        private static void DrawKeyboardModeIndicator(Rect inRect)
        {
            float indicatorWidth = 250f;
            float indicatorHeight = 30f;
            Rect indicatorRect = new Rect(inRect.x + 10f, inRect.y + 10f, indicatorWidth, indicatorHeight);

            Color backgroundColor = new Color(0.2f, 0.4f, 0.6f, 0.85f);
            Widgets.DrawBoxSolid(indicatorRect, backgroundColor);

            Color borderColor = new Color(0.4f, 0.6f, 1.0f, 1.0f);
            Widgets.DrawBox(indicatorRect, 1);

            Text.Font = GameFont.Tiny;
            Text.Anchor = TextAnchor.MiddleCenter;
            Widgets.Label(indicatorRect, (string)"RimWorldAccess.Caravan.Split.KeyboardModeActive".Translate());

            Text.Anchor = TextAnchor.UpperLeft;
            Text.Font = GameFont.Small;

            float instructionsY = indicatorRect.yMax + 5f;
            float instructionsWidth = 500f;
            float instructionsHeight = 60f;
            Rect instructionsRect = new Rect(inRect.x + 10f, instructionsY, instructionsWidth, instructionsHeight);

            Text.Font = GameFont.Tiny;
            Text.Anchor = TextAnchor.UpperLeft;

            string instructions = "RimWorldAccess.Caravan.Split.KeyboardInstructions".Translate();

            Widgets.Label(instructionsRect, instructions);

            Text.Anchor = TextAnchor.UpperLeft;
            Text.Font = GameFont.Small;
        }
    }
}
