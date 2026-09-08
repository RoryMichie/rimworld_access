using System;
using HarmonyLib;
using RimWorld;
using Verse;

namespace RimWorldAccess
{
    /// <summary>
    /// Harmony patches for the Scenario Builder (Page_ScenarioEditor). Initializes and
    /// closes <see cref="ScenarioBuilderState"/> (now purely a row-data source)
    /// when the editor opens/closes, drives the DoWindowContents-pass hook the
    /// live text-edit sessions need, and keeps the pre-existing unsaved-changes dirty-check
    /// on Page.DoBack. All keyboard navigation/announcement now lives in
    /// <see cref="RimWorldAccess.Shell.ScenarioEditorScreenScope"/> — its own guard twins
    /// (ScenarioEditorScreenScopePatch_CanDoNext/_CanDoBack, in that file) replaced the
    /// retired ScenarioBuilderPatch_CanDoNext/_CanDoBack pair (they
    /// referenced ScenarioBuilderPartEditState/ScenarioBuilderAddPartState/etc., which the
    /// S3 migration deletes or narrows).
    /// </summary>
    [HarmonyPatch(typeof(Page_ScenarioEditor))]
    public static class ScenarioBuilderPatch
    {
        /// <summary>
        /// Drives the live metadata/quantity/text edit sessions' per-pass mirror (the
        /// WorldParamsPatch/WorldParamsScreenScope.OnPageDrawPass precedent) — must run
        /// inside the page's own GUI pass so a vanilla-drawn TextEntry/TextArea (Title/
        /// Summary/Description/Seed) renders the live buffer and Escape keeps the typed
        /// value rather than discarding it.
        /// </summary>
        [HarmonyPatch("DoWindowContents")]
        [HarmonyPrefix]
        public static void DoWindowContents_Prefix()
        {
            try
            {
                RimWorldAccess.Shell.ScenarioEditorScreenScope.Active?.OnHostDrawPass();
            }
            catch (System.Exception ex)
            {
                Log.Error($"[RimWorld Access] Error in ScenarioBuilderPatch DoWindowContents prefix: {ex}");
            }
        }
    }

    /// <summary>
    /// Opens the builder state at Window.PostOpen (Page_ScenarioEditor doesn't override it).
    /// PostOpen, not PreOpen: Find.Scenario resolves the editor through the WindowStack, which
    /// only inserts the window between the two — vanilla part helpers like
    /// ScenPart_StartingResearch.NonRedundantResearchProjects NRE before insertion.
    /// </summary>
    [HarmonyPatch(typeof(Window))]
    [HarmonyPatch("PostOpen")]
    public static class ScenarioBuilderOpenPatch
    {
        [HarmonyPostfix]
        public static void Postfix(Window __instance)
        {
            if (!(__instance is Page_ScenarioEditor page))
            {
                return;
            }
            try
            {
                Scenario scenario = (Scenario)AccessTools.Field(typeof(Page_ScenarioEditor), "curScen").GetValue(page);
                ScenarioBuilderState.Open(scenario, page);
            }
            catch (System.Exception ex)
            {
                Log.Error($"[RimWorld Access] Error in ScenarioBuilderOpenPatch: {ex}");
            }
        }
    }

    /// <summary>
    /// Patch Window.PostClose to detect when Page_ScenarioEditor closes.
    /// We patch Window because Page_ScenarioEditor doesn't override PostClose.
    /// </summary>
    [HarmonyPatch(typeof(Window))]
    [HarmonyPatch("PostClose")]
    public static class ScenarioBuilderClosePatch
    {
        [HarmonyPostfix]
        public static void Postfix(Window __instance)
        {
            try
            {
                // Only handle Page_ScenarioEditor
                if (__instance is Page_ScenarioEditor && ScenarioBuilderState.IsActive)
                {
                    ScenarioBuilderState.Close();
                }
            }
            catch (System.Exception ex)
            {
                Log.Error($"[RimWorld Access] Error in ScenarioBuilderClosePatch: {ex}");
            }
        }
    }

    /// <summary>
    /// Patches Page.DoBack() to show unsaved changes dialog for scenario builder.
    /// CRITICAL: This is the correct place to intercept Escape key for Page windows.
    /// OnCancelKeyPressed is NEVER called for Pages because closeOnCancel = false.
    /// DoBack() is called by DoBottomButtons() when Escape is pressed and CanDoBack() returns true.
    /// Since we block CanDoBack() when dirty, we also need to handle the dialog here for when
    /// users use the Back button directly. ScenarioEditorScreenScope's own Back
    /// action rides this same Page.DoBack, so the dialog
    /// still fires for both the declared Back action and a raw vanilla mouse click.
    /// </summary>
    [HarmonyPatch(typeof(Page))]
    [HarmonyPatch("DoBack")]
    public static class ScenarioBuilderDoBackPatch
    {
        [HarmonyPrefix]
        public static bool Prefix(Page __instance)
        {
            // Only intercept for Page_ScenarioEditor
            if (__instance is Page_ScenarioEditor)
            {
                // Check for unsaved changes
                if (ScenarioBuilderState.IsActive && ScenarioBuilderState.IsDirty())
                {
                    ShowUnsavedChangesDialog(__instance);
                    return false; // Block original method
                }
            }
            return true; // Let original method run
        }

        /// <summary>
        /// Shows a confirmation dialog for unsaved changes with Save, Discard, and Cancel buttons.
        /// </summary>
        private static void ShowUnsavedChangesDialog(Window editorWindow)
        {
            // Get the prev page so we can navigate back properly after discard
            var prevPage = AccessTools.Field(typeof(Page), "prev")?.GetValue(editorWindow) as Page;

            var dialog = new Dialog_MessageBox(
                "RimWorldAccess.ScenarioBuilder.UnsavedChangesPrompt".Translate(),
                "RimWorldAccess.ScenarioBuilder.UnsavedSave".Translate(),
                () =>
                {
                    // Save then close: the real picker, with the close-out deferred until a
                    // save actually ran (onClosed alone also fires on cancel).
                    ScenarioSaveInteractionPatch.AfterNextSave = () =>
                    {
                        ScenarioBuilderState.Close();
                        editorWindow?.Close();
                    };
                    Find.WindowStack.Add(new Dialog_ScenarioList_Save(
                        ScenarioBuilderState.CurrentScenario,
                        onClosed: ScenarioSaveInteractionPatch.ClearPendingUnlessSaving));
                },
                "RimWorldAccess.ScenarioBuilder.UnsavedDiscard".Translate(),
                () =>
                {
                    // Discard changes and navigate back to scenario list
                    TolkHelper.Speak("RimWorldAccess.ScenarioBuilder.ChangesDiscarded".Loc());
                    ScenarioBuilderState.ResetDirty();
                    ScenarioBuilderState.Close();
                    // Navigate back to previous page (scenario list) instead of just closing
                    if (prevPage != null)
                    {
                        Find.WindowStack.Add(prevPage);
                    }
                    editorWindow?.Close();
                },
                "RimWorldAccess.ScenarioBuilder.UnsavedChangesTitle".Translate(),
                false
            );

            // Add a third button for Cancel
            dialog.buttonCText = "RimWorldAccess.ScenarioBuilder.UnsavedCancel".Translate();
            dialog.buttonCAction = () =>
            {
                TolkHelper.Speak("RimWorldAccess.ScenarioBuilder.ContinuingToEdit".Loc());
            };
            dialog.buttonCClose = true;

            Find.WindowStack.Add(dialog);
        }
    }

    /// <summary>
    /// Builder-side bookkeeping for the real <see cref="Dialog_ScenarioList_Save"/>: an actual
    /// save clears the dirty flag and runs any deferred close-out. The dialog's own onClosed
    /// callback cannot carry these — it fires on cancel too.
    /// </summary>
    [HarmonyPatch(typeof(Dialog_ScenarioList_Save), "DoFileInteraction")]
    public static class ScenarioSaveInteractionPatch
    {
        /// <summary>Runs once after the next save from this dialog completes; cleared on a cancel close.</summary>
        internal static Action AfterNextSave;

        private static bool saveInFlight;

        /// <summary>onClosed hook: drop the deferred close-out unless this close IS the save's own.</summary>
        internal static void ClearPendingUnlessSaving()
        {
            if (!saveInFlight)
            {
                AfterNextSave = null;
            }
        }

        [HarmonyPrefix]
        public static void Prefix()
        {
            saveInFlight = true;
        }

        [HarmonyPostfix]
        public static void Postfix()
        {
            saveInFlight = false;
            if (!ScenarioBuilderState.IsActive)
            {
                AfterNextSave = null;
                return;
            }
            ScenarioBuilderState.ResetDirty();
            Action pending = AfterNextSave;
            AfterNextSave = null;
            pending?.Invoke();
        }
    }
}
