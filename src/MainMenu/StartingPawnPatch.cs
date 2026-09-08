using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;

namespace RimWorldAccess
{
    [HarmonyPatch(typeof(Page_ConfigureStartingPawns))]
    [HarmonyPatch("PreOpen")]
    public static class StartingPawnPreOpenPatch
    {
        [HarmonyPostfix]
        public static void Postfix(Page_ConfigureStartingPawns __instance)
        {
            try
            {
                // Site selection can leave world navigation open.
                if (WorldNavigationState.IsActive)
                {
                    WorldNavigationState.Close();
                    StartingSiteContext.Close();
                }

                StartingPawnPatch.SetInstance(__instance);
                PawnFilterData.Initialize();
                StartingPawnState.Open();

                // IMGUI focus dies for the host after a child window closes.
                Find.WindowStack.Notify_ManuallySetFocus(__instance);
            }
            catch (System.Exception ex)
            {
                Log.Error($"[RimWorld Access] Error in StartingPawnPreOpenPatch: {ex}");
            }
        }
    }

    [HarmonyPatch(typeof(Window))]
    [HarmonyPatch("PostClose")]
    public static class StartingPawnPostClosePatch
    {
        [HarmonyPostfix]
        public static void Postfix(Window __instance)
        {
            if (!(__instance is Page_ConfigureStartingPawns)) return;
            try
            {
                StartingPawnPatch.CloseOverlayStates();
                StartingPawnState.Close();
                StartingPawnPatch.SetInstance(null);
            }
            catch (System.Exception ex)
            {
                Log.Error($"[RimWorld Access] Error in StartingPawnPostClosePatch: {ex}");
            }
        }
    }

    /// <summary>
    /// Skips the page's ENTIRE DoWindowContents body — Page.DoBottomButtons' deferred
    /// <c>Cancel.KeyDownEvent → DoBack()</c> poll included — while a windowless float menu is
    /// open.
    /// Skipping the draw, never consuming the event: a focused window's GUI pass can run BEFORE
    /// the dispatcher's main pass and shares Event.current with it (QA R6), so using the event
    /// here would also starve the float menu's own Escape claim and one Escape would neither
    /// close the menu nor back out the page. The postfix below still runs; Harmony postfixes are
    /// unaffected by a skipping prefix.
    /// Page_ConfigureStartingPawns only. The wanderer dialog is a plain Window with no such
    /// deferred poll; its Escape and Enter are owned by StartingPawnScope instead.
    /// </summary>
    [HarmonyPatch(typeof(Page_ConfigureStartingPawns))]
    [HarmonyPatch("DoWindowContents")]
    public static class StartingPawnDoWindowContentsPatch
    {
        [HarmonyPrefix]
        public static bool Prefix()
        {
            return !WindowlessFloatMenuState.IsActive;
        }

        [HarmonyPostfix]
        public static void Postfix(Page_ConfigureStartingPawns __instance)
        {
            try
            {
                RerollState.ProcessBatch();

                if (!StartingPawnState.IsActive) return;

                StartingPawnState.CheckPendingRenameRebuild();

                // Keeps a live name-edit session's buffer mirrored.
                Shell.StartingPawnScreenScope.Active?.OnHostDrawPass();

                // Vanilla's own cursor follows the tree selection.
                int pawnIdx = StartingPawnState.GetSelectedPawnIndex();
                AccessTools.Field(typeof(Page_ConfigureStartingPawns), "curPawnIndex")
                    .SetValue(__instance, pawnIdx);
            }
            catch (System.Exception ex)
            {
                Log.Error($"[RimWorld Access] Error in StartingPawnDoWindowContentsPatch: {ex}");
            }
        }
    }

    /// <summary>
    /// Captures the visible height DrawPawnList's own scroll view uses into
    /// StartingPawnScreenScope, for its scroll-follow write. Not closed-form from the page
    /// layout, so it has to be read live; the patch only copies two floats.
    /// </summary>
    [HarmonyPatch(typeof(Page_ConfigureStartingPawns), "DrawPawnList")]
    public static class StartingPawnListViewportCapturePatch
    {
        [HarmonyPrefix]
        public static void Prefix(Rect rect)
        {
            if (Event.current.type == EventType.Layout)
            {
                return;
            }
            if (!(Shell.FocusStack.Top is Shell.StartingPawnScreenScope))
            {
                return;
            }
            Shell.StartingPawnScreenScope.CaptureVisibleHeight(rect.height - 22f);
        }
    }

    /// <summary>
    /// State-based CanDoBack guard: while one of the page's windowless pawn-filter overlays owns
    /// Escape, the page's deferred <c>Cancel.KeyDownEvent → DoBack()</c> poll is refused. Under
    /// window-pass-first ordering (QA R6) that poll reads the raw Escape before the dispatcher's
    /// claim consumes it, so without this an Escape inside the filter editor also backs the page
    /// out. The float-menu case is covered by the DoWindowContents skip above; these are not.
    /// </summary>
    [HarmonyPatch(typeof(Page), "CanDoBack")]
    public static class StartingPawnCanDoBackGuardPatch
    {
        [HarmonyPrefix]
        public static bool Prefix(Page __instance, ref bool __result)
        {
            if (!(__instance is Page_ConfigureStartingPawns))
            {
                return true;
            }
            if (KeyBindingDefOf.Cancel.KeyDownEvent
                && (PawnFilterState.IsActive
                    || PawnFilterPresetSaveState.IsActive
                    || PawnFilterPresetLoadState.IsActive
                    || PawnFilterPresetDeleteConfirmState.IsActive
                    || RerollState.IsActive))
            {
                __result = false;
                return false;
            }
            return true;
        }
    }

    public static class StartingPawnPatch
    {
        private static Page_ConfigureStartingPawns instance;

        public static void SetInstance(Page_ConfigureStartingPawns page)
        {
            instance = page;
        }

        public static Page_ConfigureStartingPawns GetInstance()
        {
            return instance;
        }

        /// <summary>
        /// Close-side teardown of every windowless overlay the pawn editor can leave open,
        /// shared by both hosts' PostClose patches: nothing stops the page closing under one, and
        /// a surviving flag would leak its mirror scope onto the next screen. Reroll is cancelled
        /// FIRST — its completion callback touches the pawn state, which must still be open.
        /// </summary>
        internal static void CloseOverlayStates()
        {
            if (RerollState.IsActive)
                RerollState.Cancel();
            PawnFilterPresetDeleteConfirmState.ForceClose();
            if (PawnFilterPresetSaveState.IsActive)
                PawnFilterPresetSaveState.Close();
            if (PawnFilterPresetLoadState.IsActive)
                PawnFilterPresetLoadState.Close();
            if (PawnFilterState.IsActive)
                PawnFilterState.Close(save: true);
            if (WindowlessFloatMenuState.IsActive)
                WindowlessFloatMenuState.Close();
        }

        public static bool CanDoNext()
        {
            if (instance == null) return false;
            try
            {
                return (bool)AccessTools.Method(typeof(Page_ConfigureStartingPawns), "CanDoNext")
                    .Invoke(instance, null);
            }
            catch (System.Exception ex)
            {
                Log.Error($"[RimWorld Access] Error calling CanDoNext: {ex}");
                return false;
            }
        }

        public static void DoNext()
        {
            if (instance == null) return;
            try
            {
                AccessTools.Method(typeof(Page_ConfigureStartingPawns), "DoNext").Invoke(instance, null);
            }
            catch (System.Exception ex)
            {
                Log.Error($"[RimWorld Access] Error calling DoNext: {ex}");
            }
        }

        public static void DoBack()
        {
            if (instance == null) return;
            try
            {
                AccessTools.Method(typeof(Page), "DoBack").Invoke(instance, null);
            }
            catch (System.Exception ex)
            {
                Log.Error($"[RimWorld Access] Error calling DoBack: {ex}");
            }
        }
    }
}
