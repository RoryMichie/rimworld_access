using HarmonyLib;
using RimWorld;
using Verse;

namespace RimWorldAccess
{
    /// <summary>
    /// Harmony lifecycle patches for Dialog_SellableItems.
    /// Note: Dialog_SellableItems doesn't override PostOpen/Close, so we patch Window directly.
    /// The per-dialog OnCancelKeyPressed blocker this class used to carry is retired:
    /// SellableItemsScope (a ScreenScope) reports OwnsCancel/OwnsAccept and the shared
    /// WindowKeyRouter blocks vanilla's Escape/Enter close for the attached window.
    /// </summary>
    [HarmonyPatch]
    public static class SellableItemsNavigationPatch
    {
        /// <summary>
        /// Postfix patch that runs after Window.PostOpen().
        /// Opens the keyboard navigation interface when Dialog_SellableItems opens.
        /// </summary>
        [HarmonyPatch(typeof(Window), "PostOpen")]
        [HarmonyPostfix]
        public static void Window_PostOpen_Postfix(Window __instance)
        {
            // Only handle Dialog_SellableItems
            if (__instance is Dialog_SellableItems sellableItemsDialog)
            {
                try
                {
                    SellableItemsState.Open(sellableItemsDialog);
                }
                catch (System.Exception ex)
                {
                    Log.Error($"RimWorld Access: Error initializing sellable items navigation: {ex.Message}\n{ex.StackTrace}");
                }
            }
        }

        /// <summary>
        /// Prefix patch for Window.Close to clean up our state when Dialog_SellableItems closes.
        /// </summary>
        [HarmonyPatch(typeof(Window), "Close")]
        [HarmonyPrefix]
        public static void Window_Close_Prefix(Window __instance)
        {
            // Only handle Dialog_SellableItems
            if (__instance is Dialog_SellableItems)
            {
                // Clean up our navigation state when the dialog closes
                if (SellableItemsState.IsActive)
                {
                    SellableItemsState.OnDialogClosing();
                }
            }
        }
    }
}
