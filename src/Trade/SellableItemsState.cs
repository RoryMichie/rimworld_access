using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using RimWorld;
using Verse;
using Verse.Sound;

namespace RimWorldAccess
{
    /// <summary>
    /// Lifecycle bridge for <see cref="Dialog_SellableItems"/> (the read-only
    /// "things this trader will buy" browser). Navigation lives in the
    /// SellableItemsScope ScreenScope (table-model T3b) — this static holds
    /// the IsActive flag the Harmony patches read plus the reflection
    /// accessors over the dialog's own private tab/category machinery, so the
    /// scope reads the game's live decision objects rather than transcribing
    /// them. The pre-table navigator (tab cycling, cursor, hand-built
    /// announcements, typeahead) was retired by the migration.
    /// </summary>
    public static class SellableItemsState
    {
        private static bool isActive = false;
        private static Dialog_SellableItems currentDialog = null;

        // Cached reflection accessors
        private static FieldInfo tabsField = null;
        private static FieldInfo currentCategoryField = null;
        private static FieldInfo pawnsTabOpenField = null;
        private static FieldInfo traderField = null;
        private static MethodInfo getSellableItemsMethod = null;

        public static bool IsActive => isActive;

        public static void Open(Dialog_SellableItems dialog)
        {
            if (dialog == null)
            {
                TolkHelper.Speak("RimWorldAccess.Sellable.Open.NoDialog".Loc());
                return;
            }

            currentDialog = dialog;
            isActive = true;
            InitializeReflection();
            SoundDefOf.TabOpen.PlayOneShotOnCamera();
        }

        /// <summary>Escape: closes the dialog with the closed announcement.</summary>
        public static void Close()
        {
            var dialogToClose = currentDialog;

            isActive = false;
            currentDialog = null;

            if (dialogToClose != null && Find.WindowStack != null)
            {
                Find.WindowStack.TryRemove(dialogToClose, doCloseSound: false);
            }

            TolkHelper.Speak("RimWorldAccess.Sellable.Open.Closed".Loc());
            SoundDefOf.Click.PlayOneShotOnCamera();
        }

        /// <summary>Called when the dialog closes externally (vanilla Close button, accept key).</summary>
        public static void OnDialogClosing()
        {
            if (!isActive)
                return;
            isActive = false;
            currentDialog = null;
        }

        // ------------------------------------------------------------------
        // Reflection accessors for the scope (the dialog's own live objects).
        // ------------------------------------------------------------------

        private static void InitializeReflection()
        {
            if (tabsField == null)
            {
                var dialogType = typeof(Dialog_SellableItems);
                tabsField = AccessTools.Field(dialogType, "tabs");
                currentCategoryField = AccessTools.Field(dialogType, "currentCategory");
                pawnsTabOpenField = AccessTools.Field(dialogType, "pawnsTabOpen");
                traderField = AccessTools.Field(dialogType, "trader");
                getSellableItemsMethod = AccessTools.Method(dialogType, "GetSellableItemsInCategory");
            }
        }

        /// <summary>The dialog's own tab records (labels + the game's own switch actions).</summary>
        public static List<TabRecord> GetTabs()
        {
            if (currentDialog == null || tabsField == null)
                return null;
            return tabsField.GetValue(currentDialog) as List<TabRecord>;
        }

        /// <summary>
        /// Switches the dialog's visible tab through the TabRecord's own
        /// clickedAction — the game's exact tab-click path, closure-captured
        /// category included, so the sighted view follows the region cursor.
        /// </summary>
        public static void ActivateTab(int tabIndex)
        {
            var tabs = GetTabs();
            if (tabs == null || tabIndex < 0 || tabIndex >= tabs.Count)
                return;
            tabs[tabIndex].clickedAction?.Invoke();
        }

        /// <summary>The dialog's own item list for a category tab (null category + pawns=true is the Pawns tab).</summary>
        public static List<ThingDef> GetItemsInCategory(ThingCategoryDef category, bool pawns)
        {
            if (currentDialog == null || getSellableItemsMethod == null)
                return null;
            try
            {
                return getSellableItemsMethod.Invoke(currentDialog, new object[] { category, pawns }) as List<ThingDef>;
            }
            catch (Exception ex)
            {
                Log.Warning($"RimWorld Access: Error getting sellable items: {ex.Message}");
                return null;
            }
        }

        public static ITrader GetTrader()
        {
            if (currentDialog == null || traderField == null)
                return null;
            return traderField.GetValue(currentDialog) as ITrader;
        }
    }
}
