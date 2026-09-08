using System;
using HarmonyLib;
using RimWorld;

namespace RimWorldAccess.Shell
{
    /// <summary>
    /// Gives hover the colonist bar. The bar paints portraits, mood and the state
    /// icons with raw <c>GUI.DrawTexture</c> calls straight to the screen
    /// (decompiled RimWorld/ColonistBarColonistDrawer.cs:85-158), so no capture
    /// tap sees it and no window brackets it; the entry under the pointer comes
    /// from vanilla's own <c>ColonistOrCorpseAt</c> instead.
    ///
    /// The bracket is what lets a hovered entry carry the state icons' tooltips
    /// (mental state, burning, inspired, sleeping, forming caravan — DrawIcons'
    /// own <c>TipRegion</c> calls, :299-411): they are caller-gated on the mouse
    /// being over the icon, so <see cref="LiveTooltips"/> is the only channel
    /// that can see them.
    /// </summary>
    [HarmonyPatch(typeof(ColonistBar), nameof(ColonistBar.ColonistBarOnGUI))]
    internal static class ColonistBarHoverPatch
    {
        [HarmonyPrefix]
        [HarmonyPriority(Priority.First)]
        public static void Prefix()
        {
            try
            {
                if (HoverSpeech.Enabled)
                {
                    LiveTooltips.BeginBracket();
                }
            }
            catch (Exception ex)
            {
                ModLogger.LimitedError("Colonist bar hover error", ex);
            }
        }

        [HarmonyPostfix]
        public static void Postfix()
        {
            try
            {
                HoverSpeech.EvaluateColonistBar();
            }
            catch (Exception ex)
            {
                ModLogger.LimitedError("Colonist bar hover error", ex);
            }
        }
    }
}
