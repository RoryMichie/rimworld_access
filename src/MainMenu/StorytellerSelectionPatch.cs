using HarmonyLib;
using RimWorld;
using Verse;
using RimWorldAccess.Shell;

namespace RimWorldAccess
{
    /// <summary>
    /// Support for the pre-game storyteller page (<see cref="Page_SelectStoryteller"/>).
    /// All keyboard navigation and announcement now lives in
    /// <see cref="StorytellerScreenScope"/> (src/Shell/Screens/StorytellerScreenScope.Game.cs);
    /// this class keeps only the reflected write helpers the scope rides as its
    /// mutation vehicles, the two wizard-advance poll guards, and the edge-path
    /// Anomaly-dialog return handoff.
    ///
    /// The following no longer exist: the DoWindowContents Prefix/Postfix (one-shot announcement,
    /// mode-indicator box, HostFocusReturn — the page uses no vanilla text field, so
    /// nothing needs IMGUI focus reclaim once the shell drives it), the
    /// CycleNavigationMode*/HandleUp/Down/Home/End/Escape/Backspace/Typeahead/
    /// Announce*Mode navigation surface, TryOpenCustomDifficulty and
    /// OpenAnomalySettingsDialog (region 4 replaces Dialog_AnomalySettings inline),
    /// and the PreOpen/PreClose reset patches (StorytellerNavigationState /
    /// CustomDifficultyEditState are gone; the per-window scope rebuilds from live
    /// page fields each focus).
    /// </summary>
    public static class StorytellerSelectionPatch
    {
        internal static void UpdatePageStoryteller(Page_SelectStoryteller page, StorytellerDef def)
        {
            if (def == null)
                return;
            // MUTATION-C: mirrors StorytellerUI.DrawStorytellerSelectionInterface's
            // portrait ButtonImage branch (decompiled :59-62) — fire the tutorial event,
            // then write the page's private storyteller field. No gated setter exists.
            TutorSystem.Notify_Event("ChooseStoryteller");
            StorytellerScreenScope.StorytellerField(page) = def;
        }

        internal static void UpdatePageDifficulty(Page_SelectStoryteller page, DifficultyDef def)
        {
            if (def == null)
                return;
            Difficulty dv = StorytellerScreenScope.DifficultyValuesField(page);
            DifficultyDef prev = StorytellerScreenScope.DifficultyField(page);
            // MUTATION-C: mirrors the difficulty RadioButton branch (decompiled
            // StorytellerUI :100-114) — a preset copies its values in; switching TO the
            // custom difficulty from a different def seeds from Rough (vanilla's starting
            // point). CopyFrom is vanilla's own method; the private difficulty-field write
            // has no gated setter.
            if (!def.isCustom)
            {
                dv.CopyFrom(def);
            }
            else if (def != prev)
            {
                dv.CopyFrom(DifficultyDefOf.Rough);
            }
            StorytellerScreenScope.DifficultyField(page) = def;
        }

        internal static void SetPermadeath(bool permadeath)
        {
            // MUTATION-C: mirrors StorytellerUI's ReloadAnytimeMode / CommitmentMode
            // RadioButton branches (decompiled :122-132), which write
            // GameInitData.permadeathChosen/permadeath directly; no gated setter exists.
            Find.GameInitData.permadeathChosen = true;
            Find.GameInitData.permadeath = permadeath;
        }

        /// <summary>
        /// Called by <see cref="AnomalySettingsDialogPatch"/> after
        /// <see cref="Dialog_AnomalySettings"/> accept-closes. Region 4 replaced that
        /// dialog for keyboard users on the pre-game page, so this is now an edge path
        /// only reached when a mod re-opens the vanilla dialog over the page (vanilla
        /// itself never offers this dialog in-game — decompiled-verified, see
        /// StorytellerInGameScope's class remarks — but the cast is widened to the
        /// shared StorytellerScopeBase, so either twin
        /// re-announces correctly if a mod ever does). The patch already spoke the
        /// "saved" line; this re-reads whatever row the cursor rests on so focus is
        /// clear when the dialog goes away.
        /// </summary>
        public static void ReturnToStorytellerMode()
        {
            (FocusStack.Top as StorytellerScopeBase)?.ReannounceCurrent();
        }
    }

    /// <summary>
    /// Wizard-advance guard: blocks the raw keyboard Accept poll (Page.DoBottomButtons,
    /// Page.cs:69) so an Enter this scope consumed for row activation cannot ALSO advance
    /// the wizard. Page_SelectStoryteller overrides CanDoNext (decompiled-verified), so
    /// this patches the declaring type directly. State-based: fires only while the live
    /// top scope is <see cref="StorytellerScreenScope"/> and the Next declared action has
    /// not set <see cref="StorytellerScreenScope.AdvanceRequested"/>. Gated on the keyboard
    /// Accept event so a mouse click on the Next button (KeyDownEvent false) still advances.
    /// </summary>
    [HarmonyPatch(typeof(Page_SelectStoryteller), "CanDoNext")]
    public class StorytellerSelectionPatch_CanDoNext
    {
        [HarmonyPrefix]
        static bool Prefix(ref bool __result)
        {
            if (KeyBindingDefOf.Accept.KeyDownEvent
                && FocusStack.Top is StorytellerScreenScope
                && !StorytellerScreenScope.AdvanceRequested)
            {
                __result = false;
                return false;
            }
            return true;
        }
    }

    /// <summary>
    /// Back-navigation guard twin. Page_SelectStoryteller does not override CanDoBack
    /// (decompiled-verified), so this patches the declaring type (RimWorld.Page) with an
    /// instance guard — see WorldParamsPatch_CanDoBack for why several instance-gated
    /// Page.CanDoBack patches coexist safely. Escape rides the scope's own Cancel
    /// claim (EscapeBack → BackRequested → gate+DoBack), so this blocks the raw
    /// Cancel poll outright while the scope is live: the poll's window pass runs
    /// before OR after the dispatcher depending on IMGUI focus, and the unclaimed-
    /// key modal swallow starves it entirely in the after ordering.
    /// </summary>
    [HarmonyPatch(typeof(Page), "CanDoBack")]
    public class StorytellerSelectionPatch_CanDoBack
    {
        [HarmonyPrefix]
        static bool Prefix(Page __instance, ref bool __result)
        {
            if (!(__instance is Page_SelectStoryteller))
                return true;
            // The scope claims Escape and runs the Back gate itself
            // (StorytellerScreenScope.EscapeBack sets BackRequested); the raw
            // Cancel poll is blocked outright so window-pass ordering can
            // never double-fire Back or starve it (see the world-params twin
            // for the live-traced starvation case).
            if (KeyBindingDefOf.Cancel.KeyDownEvent
                && !StorytellerScreenScope.BackRequested
                && FocusStack.Top is StorytellerScreenScope)
            {
                __result = false;
                return false;
            }
            return true;
        }
    }

    // ==== Patches for IN-GAME storyteller selection ====

    /// <summary>
    /// Opens keyboard navigation when the in-game storyteller page opens.
    /// </summary>
    [HarmonyPatch(typeof(Page_SelectStorytellerInGame), "PreOpen")]
    public static class StorytellerInGamePatch_PreOpen
    {
        [HarmonyPostfix]
        public static void Postfix()
        {
            StorytellerSelectionState.Open();
        }
    }

    /// <summary>
    /// Closes keyboard navigation when the in-game storyteller page closes.
    /// </summary>
    [HarmonyPatch(typeof(Page_SelectStorytellerInGame), "PreClose")]
    public static class StorytellerInGamePatch_PreClose
    {
        [HarmonyPostfix]
        public static void Postfix()
        {
            StorytellerSelectionState.Close();
        }
    }
}
