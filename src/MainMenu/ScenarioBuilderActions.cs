using System.Linq;
using System.Reflection;
using HarmonyLib;
using RimWorld;
using Verse;
using Verse.Sound;

namespace RimWorldAccess
{
    /// <summary>
    /// Scenario Builder's hotkey actions (Alt+A/L/S/R): orchestrates the four
    /// sibling builder-family states (add-part picker, load/save dialogs, seed
    /// randomize) and folds their results back into the current scenario/dirty
    /// state. Presentation refresh after each
    /// mutation now notifies <see cref="RimWorldAccess.Shell.ScenarioEditorScreenScope"/>
    /// directly (its <c>Active</c> singleton, the StartingPawnScreenScope/
    /// WorldParamsScreenScope precedent) instead of touching the retired
    /// ScenarioBuilderState nav/tree-cursor surface (TreeNavigationHelper,
    /// Section, metadata index — all deleted whole this slice).
    /// </summary>
    internal static class ScenarioBuilderActions
    {
        private static readonly MethodInfo addScenPartMethod = AccessTools.Method(typeof(Page_ScenarioEditor), "AddScenPart", new[] { typeof(ScenPartDef) });

        /// <summary>
        /// Opens the add part menu (Alt+A).
        /// </summary>
        public static void OpenAddPartMenu()
        {
            if (ScenarioBuilderState.CurrentScenario == null) return;

            ScenarioBuilderAddPartState.Open(ScenarioBuilderState.CurrentScenario, (ScenPartDef selectedDef) =>
            {
                if (selectedDef != null && ScenarioBuilderState.CurrentPage != null)
                {
                    // Vehicle A: reflect-invoke Page_ScenarioEditor.AddScenPart(ScenPartDef)
                    // directly (decompiled Page_ScenarioEditor.cs:195-199) — it already does
                    // MakeScenPart + Randomize + curScen.parts.Add itself, so the raw
                    // reflected `Scenario.parts` add this used to hand-roll is gone. Private
                    // instance method, no gate to honor.
                    addScenPartMethod.Invoke(ScenarioBuilderState.CurrentPage, new object[] { selectedDef });
                    ScenarioBuilderState.SetDirty();

                    // AddScenPart appends to Scenario.parts, the LAST segment Scenario.AllParts
                    // enumerates (Scenario.cs:65-74: playerFaction, surfaceLayer, then parts in
                    // order) — so the new part is always the last entry right after invocation.
                    ScenPart newPart = ScenarioBuilderState.CurrentScenario.AllParts.Last();

                    TolkHelper.Speak("RimWorldAccess.ScenarioBuilder.AddedPressEnterToEdit".Loc(selectedDef.LabelCap));

                    // Move the scope's cursor to the newly-added part and fully
                    // re-announce it (the structural-change law) — this also
                    // rebuilds the row tree (ScenarioEditorScreenScope.RefreshModel
                    // -> RefreshContent -> ScenarioBuilderState.BuildPartsTree).
                    RimWorldAccess.Shell.ScenarioEditorScreenScope.Active?.NotifyPartAdded(newPart);
                }
            });
        }

        /// <summary>
        /// Opens the load scenario dialog (Alt+L).
        /// </summary>
        public static void OpenLoadDialog()
        {
            // The real vanilla picker (Page_ScenarioEditor's own Load button vehicle); FileListScope drives it.
            Find.WindowStack.Add(new Dialog_ScenarioList_Load((Scenario loadedScenario) =>
            {
                if (loadedScenario != null && ScenarioBuilderState.CurrentPage != null)
                {
                    // MUTATION-C: mirrors Page_ScenarioEditor.DoConfigControls' Load button
                    // body (decompiled Page_ScenarioEditor.cs:93-98, Dialog_ScenarioList_Load's
                    // own onSelect delegate) — sets curScen and seedIsValid=false; both private
                    // fields have no gated setter.
                    AccessTools.Field(typeof(Page_ScenarioEditor), "curScen").SetValue(ScenarioBuilderState.CurrentPage, loadedScenario);
                    // MUTATION-C: same Load button body (decompiled Page_ScenarioEditor.cs:93-98) — seedIsValid=false alongside curScen above.
                    AccessTools.Field(typeof(Page_ScenarioEditor), "seedIsValid").SetValue(ScenarioBuilderState.CurrentPage, false);

                    ScenarioBuilderState.SetCurrentScenario(loadedScenario);
                    ScenarioBuilderState.ResetDirty();

                    TolkHelper.Speak("RimWorldAccess.ScenarioBuilder.LoadedScenario".Loc(loadedScenario.name));

                    // The whole scenario (and every ScenPart in it) was just replaced —
                    // reset the cursor to the top of both regions.
                    RimWorldAccess.Shell.ScenarioEditorScreenScope.Active?.ResetCursorAfterScenarioSwap();
                }
            }));
        }

        /// <summary>
        /// Opens the save scenario dialog (Alt+S).
        /// </summary>
        public static void OpenSaveDialog()
        {
            if (ScenarioBuilderState.CurrentScenario == null) return;

            // Cat B: honor the same gate vanilla's Save button checks before opening
            // Dialog_ScenarioList_Save (Page_ScenarioEditor.DoConfigControls: ButtonText("Save")
            // && CheckAllPartsCompatible(curScen)). The method is private static; reflect-invoke
            // it directly rather than hand-copying its maxUses/CanCoexistWith walk, so it stays
            // in sync with vanilla and keeps its own Messages.Message rejection text.
            var checkMethod = AccessTools.Method(typeof(Page_ScenarioEditor), "CheckAllPartsCompatible");
            if (checkMethod != null && !(bool)checkMethod.Invoke(null, new object[] { ScenarioBuilderState.CurrentScenario }))
            {
                return;
            }

            // The real vanilla picker; FileListScope drives it. ResetDirty rides
            // ScenarioSaveInteractionPatch — the onClosed callback also fires on cancel.
            Find.WindowStack.Add(new Dialog_ScenarioList_Save(ScenarioBuilderState.CurrentScenario));
        }

        /// <summary>
        /// Randomizes the scenario seed (Alt+R).
        /// </summary>
        public static void RandomizeSeed()
        {
            if (ScenarioBuilderState.CurrentPage == null) return;

            // Call the private RandomizeSeedAndScenario method
            var method = AccessTools.Method(typeof(Page_ScenarioEditor), "RandomizeSeedAndScenario");
            if (method != null)
            {
                // Matches the button body in full (decompiled Page_ScenarioEditor.cs:105-109):
                // Tick_Tiny, regenerate, then mark the seed field valid again.
                SoundDefOf.Tick_Tiny.PlayOneShotOnCamera();
                method.Invoke(ScenarioBuilderState.CurrentPage, null);
                // MUTATION-C: mirrors Page_ScenarioEditor.DoConfigControls' RandomizeSeed button
                // body (decompiled :105-109, `seedIsValid = true;`) — the private field has no
                // gated setter, the same reflected write OpenLoadDialog above already uses for it.
                AccessTools.Field(typeof(Page_ScenarioEditor), "seedIsValid").SetValue(ScenarioBuilderState.CurrentPage, true);

                // Refresh our reference to the scenario
                ScenarioBuilderState.SetCurrentScenario(
                    (Scenario)AccessTools.Field(typeof(Page_ScenarioEditor), "curScen").GetValue(ScenarioBuilderState.CurrentPage));
                ScenarioBuilderState.SetDirty();

                TolkHelper.Speak("RimWorldAccess.ScenarioBuilder.RandomizedNew".Loc(
                    ScenarioBuilderState.CurrentScenario?.name ?? (string)"RimWorldAccess.ScenarioBuilder.NewScenarioFallback".Translate()));

                // Same as Load: the whole scenario was just replaced.
                RimWorldAccess.Shell.ScenarioEditorScreenScope.Active?.ResetCursorAfterScenarioSwap();
            }
        }
    }
}
