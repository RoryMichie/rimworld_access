using HarmonyLib;
using RimWorld;
using RimWorld.Planet;
using System.Linq;
using Verse;

namespace RimWorldAccess
{
    /// <summary>
    /// Opens the accessible screen for the Work tab while the real vanilla window keeps drawing. This
    /// no longer replaces the window, it just dispatches to either WorkMenuState (focused view) or
    /// WorkTableState (table view) via WorkMenuOpener, based on the DefaultWorkMenuView setting, while
    /// making sure one of them is active. The IsActive guard is load-bearing: this prefix now runs
    /// EVERY FRAME the window is open, not just once.
    /// </summary>
    [HarmonyPatch(typeof(MainTabWindow_Work), nameof(MainTabWindow_Work.DoWindowContents))]
    public static class WorkWindowInterceptPatch
    {
        [HarmonyPrefix]
        public static void Prefix()
        {
            if (WorkMenuState.IsActive || WorkTableState.IsActive)
            {
                return;
            }

            if (Find.World?.renderer?.wantedMode == WorldRenderMode.Planet)
            {
                CameraJumper.TryHideWorld();
                MapNavigationState.RestoreCursorForCurrentMap();
            }

            Pawn targetPawn = null;
            if (Find.Selector?.SelectedPawns?.Count > 0)
                targetPawn = Find.Selector.SelectedPawns.FirstOrDefault(p => p.IsColonist);
            if (targetPawn == null && Find.CurrentMap != null)
                targetPawn = Find.CurrentMap.mapPawns.FreeColonists.FirstOrDefault();

            // No colonist: vanilla drew its own (empty) window here before this wave too.
            if (targetPawn == null)
            {
                return;
            }

            WorkMenuOpener.OpenDefaultView(targetPawn);
            if (!WorkMenuState.IsActive && !WorkTableState.IsActive)
            {
                Shell.MainTabWindowLink.CloseTab(Shell.MainTabWindowLink.Work);
            }
        }
    }

    /// <summary>
    /// Centralized dispatcher for opening the work menu. Reads the
    /// DefaultWorkMenuView mod setting and opens the matching view.
    /// </summary>
    public static class WorkMenuOpener
    {
        public static void OpenDefaultView(Pawn targetPawn)
        {
            var settings = RimWorldAccessMod_Settings.Settings;
            if (settings != null && settings.DefaultWorkMenuView == WorkMenuView.Table)
                WorkTableState.Open(targetPawn);
            else
                WorkMenuState.Open(targetPawn);
        }

        /// <summary>
        /// Swap from focused view to table view without saving/cancelling
        /// (changes are applied in real-time). Persists the chosen view.
        /// </summary>
        public static void SwapToTable()
        {
            if (!WorkMenuState.IsActive) return;
            Pawn currentPawn = WorkMenuState.CurrentPawn;
            WorkMenuState.CloseForSwap();
            RememberView(WorkMenuView.Table);
            WorkTableState.Open(currentPawn);
        }

        /// <summary>
        /// Swap from table view to focused view. Persists the chosen view.
        /// Table-model T2 migration: the table's current pawn now lives on
        /// RimWorldAccess.Shell.WorkTableScope (the pawn row list moved off
        /// WorkTableState), so the caller (the scope's own
        /// "workTable.swapToFocusedView" claim) hands it in directly instead
        /// of this method reading a WorkTableState.CurrentPawn bridge
        /// property.
        /// </summary>
        public static void SwapToFocused(Pawn currentPawn)
        {
            if (!WorkTableState.IsActive) return;
            WorkTableState.Close();
            RememberView(WorkMenuView.Focused);
            WorkMenuState.Open(currentPawn);
        }

        private static void RememberView(WorkMenuView view)
        {
            var mod = LoadedModManager.GetMod<RimWorldAccessMod_Settings>();
            var settings = RimWorldAccessMod_Settings.Settings;
            if (settings == null) return;
            if (settings.DefaultWorkMenuView == view) return;
            settings.DefaultWorkMenuView = view;
            mod?.WriteSettings();
        }
    }

    // WorkTableMenuInputPatch (the table view's keyboard-input side door) is
    // fully deleted, not just gutted — its only member was the Prefix, and
    // it is driven by the shell now (WorkTableScope, attached via
    // WorkTableScopeMirror). Its consume-all tails live on as the modal backstop / the scope's
    // claim-wrapper design — see WorkTableScope.Game.cs's class remarks.
    // (Matches the E4 precedent: PawnSkillsTableMenuInputPatch was deleted
    // the same way, not tombstoned in place.)

    // WorkTableMenuOverlayPatch (the table view's sighted-only status banner) is likewise fully
    // deleted, not tombstoned: the real MainTabWindow_Work window now covers this tab instead, so the
    // banner's only reason to exist — telling a sighted player something was happening off-screen — no
    // longer applies. The scope's OverlayPawnCount/ OverlayCurrentRow/OverlayCurrentColumnName members
    // it used to read stay on WorkTableScope (see that file's remarks).
}
