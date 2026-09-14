using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using HarmonyLib;
using RimWorld;
using RimWorld.Planet;
using UnityEngine;
using Verse;

namespace RimWorldAccess.Shell
{
    /// <summary>
    /// Reflection facade for PersonaDirector (<c>RP.RimTalk.PersonaDirector</c>), a
    /// persona-authoring layer over RimTalk's persona editor. The assembly is never referenced at
    /// compile time; its own <c>PersonalityData</c>/<c>TalkRequest</c> types are never resolved as
    /// CLR <see cref="Type"/>s — every call passes the boxed instance straight through
    /// <see cref="MethodInfo.Invoke"/>, or (for the async Evolve result) narrows to the base
    /// <see cref="Task"/>, which every <c>Task&lt;T&gt;</c> is assignable to.
    ///
    /// Three surfaces:
    /// 1. The F12 extras hub's three-way opener for <c>MainButtonWorker_Director</c>, mirroring
    ///    each Activate() branch as its own labeled entry, since the hub's generic per-def entry
    ///    can only replay the plain-click branch.
    /// 2. <see cref="RimTalkPersonaScope"/>'s Edit Notes/Evolve/Set Time buttons, painted by
    ///    <c>Patch_PersonaEditorWindow_DirectorFeatures</c> at fixed pixel offsets inside RimTalk's
    ///    editor. <c>WidgetCapture</c> cannot find them at all: a second, unrelated Harmony postfix
    ///    on the same <c>DoWindowContents</c> draws them, and this codebase's capture bracket has
    ///    no guaranteed ordering against another mod's postfix on the same method. So all three are
    ///    DECLARED actions reproducing the button's own click body by reflection (vehicle A). The
    ///    Evolve button reuses the mod's own static
    ///    <c>evolveTask</c>/<c>evolveResult</c>/<c>evolvingPawn</c> fields, so its postfix keeps
    ///    applying the result into the editor's text buffer as it would for a mouse click.
    /// 3. <see cref="PersonaDirectorBatchScope"/>, the bespoke table for <c>Window_BatchDirector</c>.
    ///
    /// Window_DirectorNotesEditor/Window_LibraryManager/Window_ImportExport opt into the GENERIC
    /// reader instead of bespoke scopes
    /// (<see cref="ScopeForWindow.TryRegisterGenericReaderForWindow"/>): every control is a real
    /// Listing_Standard/Widgets call with no raw-mousePosition selection math, so the shared engine
    /// already reads them. Window_PresetBrowser needs no registration: its constructor sets
    /// <c>absorbInputAroundWindow = true</c>, which
    /// <see cref="ScopeForWindow.GenericReaderEligible"/> already accepts.
    /// </summary>
    internal static class PersonaDirectorCompat
    {
        internal const string PackageId = "RP.RimTalk.PersonaDirector";

        private static readonly Type directorModType;
        private static readonly FieldInfo settingsField; // static, DirectorMod -> DirectorSettings

        private static readonly FieldInfo directorNotesField; // DirectorSettings.directorNotes (string)
        private static readonly FieldInfo enableEvolveField; // DirectorSettings.enableEvolveFeature (bool)
        private static readonly FieldInfo contextField; // DirectorSettings.Context (ContextSettings)
        private static readonly FieldInfo batchFiltersField; // DirectorSettings.BatchFilters (Dictionary<string,bool>)
        private static readonly FieldInfo presetsField; // DirectorSettings.presets (IList of PromptPreset)
        private static readonly FieldInfo selectedPresetIndexField; // DirectorSettings.selectedPresetIndex (int)
        private static readonly FieldInfo incDirectorNotesField; // ContextSettings.Inc_DirectorNotes (bool)
        private static readonly FieldInfo promptPresetLabelField; // PromptPreset.label (string)

        private static readonly Type directorWorldComponentType;
        private static readonly MethodInfo getLastEvolveTickMethod; // (Pawn) -> int
        private static readonly MethodInfo getLastEvolveBioAgeTicksMethod; // (Pawn) -> long
        private static readonly MethodInfo setTimestampMethod; // (Pawn, string) -> void

        private static readonly Type directorUtilsType;
        private static readonly MethodInfo buildCustomCharacterDataMethod; // (Pawn, bool, bool) -> string
        private static readonly MethodInfo buildCombinedCharacterDataMethod; // (List<Pawn>) -> string
        private static readonly MethodInfo prepareEvolveRequestMethod; // (Pawn, Window) -> (object request, string persona)
        private static readonly MethodInfo executeEvolveTaskMethod; // (object request) -> object PersonalityData
        private static readonly MethodInfo generatePersonalityTaskMethod; // (string, string, Pawn) -> Task<PersonalityData> (boxed)
        private static readonly MethodInfo generateBatchPersonaTaskMethod; // (string, Pawn) -> Task<PersonalityData> (boxed)
        private static readonly MethodInfo openRimTalkDialogMethod; // (Pawn) -> void

        private static readonly Type personalityDataType;
        private static readonly PropertyInfo personalityDataPersonaProp; // string

        private static readonly Type patchType; // Patch_PersonaEditorWindow_DirectorFeatures
        private static readonly FieldInfo evolveTaskField; // static Task<string>
        private static readonly FieldInfo evolveResultField; // static string
        private static readonly FieldInfo evolvingPawnField; // static Pawn

        private static readonly Type notesEditorType;
        private static readonly ConstructorInfo notesEditorCtor;
        private static readonly Type batchDirectorType;
        private static readonly ConstructorInfo batchDirectorCtor;
        private static readonly Type libraryManagerType;
        private static readonly Type importExportType;

        private static readonly bool ready;
        private static readonly bool buttonsReady;
        private static readonly bool batchReady;

        public static bool Ready
        {
            get { return ready && ModsConfig.IsActive(PackageId); }
        }

        static PersonaDirectorCompat()
        {
            var surface = new ReflectionSurface("PersonaDirector compat");

            directorModType = surface.Type("RimPersonaDirector.DirectorMod");
            Type settingsType = surface.Type("RimPersonaDirector.DirectorSettings");
            Type contextSettingsType = surface.Type("RimPersonaDirector.ContextSettings");
            directorWorldComponentType = surface.Type("RimPersonaDirector.DirectorWorldComponent");
            directorUtilsType = surface.Type("RimPersonaDirector.DirectorUtils");
            personalityDataType = surface.Type("RimTalk.Data.PersonalityData");
            patchType = surface.Type("RimPersonaDirector.Patch_PersonaEditorWindow_DirectorFeatures");

            settingsField = surface.Field(directorModType, "Settings");
            directorNotesField = surface.Field(settingsType, "directorNotes");
            enableEvolveField = surface.Field(settingsType, "enableEvolveFeature");
            contextField = surface.Field(settingsType, "Context");
            incDirectorNotesField = surface.Field(contextSettingsType, "Inc_DirectorNotes");

            getLastEvolveTickMethod = surface.Method(directorWorldComponentType, "GetLastEvolveTick", new[] { typeof(Pawn) });
            getLastEvolveBioAgeTicksMethod = surface.Method(directorWorldComponentType, "GetLastEvolveBioAgeTicks", new[] { typeof(Pawn) });
            setTimestampMethod = surface.Method(directorWorldComponentType, "SetTimestamp", new[] { typeof(Pawn), typeof(string) });

            buildCustomCharacterDataMethod = surface.Method(directorUtilsType, "BuildCustomCharacterData",
                new[] { typeof(Pawn), typeof(bool), typeof(bool) });
            prepareEvolveRequestMethod = surface.Method(directorUtilsType, "PrepareEvolveRequest",
                new[] { typeof(Pawn), typeof(Window) });
            executeEvolveTaskMethod = surface.Method(directorUtilsType, "ExecuteEvolveTask");

            personalityDataPersonaProp = surface.Property(personalityDataType, "Persona");

            evolveTaskField = surface.Field(patchType, "evolveTask");
            evolveResultField = surface.Field(patchType, "evolveResult");
            evolvingPawnField = surface.Field(patchType, "evolvingPawn");

            ready = surface.Ready;

            // The two feature tiers below decline independently and silently, so a drift costs
            // that one optional surface rather than the whole compat.
            Type promptPresetType = AccessTools.TypeByName("RimPersonaDirector.PromptPreset");
            promptPresetLabelField = promptPresetType != null ? AccessTools.Field(promptPresetType, "label") : null;
            if (settingsType != null)
            {
                batchFiltersField = AccessTools.Field(settingsType, "BatchFilters");
                presetsField = AccessTools.Field(settingsType, "presets");
                selectedPresetIndexField = AccessTools.Field(settingsType, "selectedPresetIndex");
            }
            if (directorUtilsType != null)
            {
                buildCombinedCharacterDataMethod = AccessTools.Method(directorUtilsType, "BuildCombinedCharacterData",
                    new[] { typeof(List<Pawn>) });
                generatePersonalityTaskMethod = AccessTools.Method(directorUtilsType, "GeneratePersonalityTask",
                    new[] { typeof(string), typeof(string), typeof(Pawn) });
                generateBatchPersonaTaskMethod = AccessTools.Method(directorUtilsType, "GenerateBatchPersonaTask",
                    new[] { typeof(string), typeof(Pawn) });
                openRimTalkDialogMethod = AccessTools.Method(directorUtilsType, "OpenRimTalkDialog", new[] { typeof(Pawn) });
            }

            notesEditorType = AccessTools.TypeByName("RimPersonaDirector.Window_DirectorNotesEditor");
            notesEditorCtor = notesEditorType != null ? AccessTools.Constructor(notesEditorType, Type.EmptyTypes) : null;
            batchDirectorType = AccessTools.TypeByName("RimPersonaDirector.Window_BatchDirector");
            batchDirectorCtor = batchDirectorType != null ? AccessTools.Constructor(batchDirectorType, Type.EmptyTypes) : null;

            // Assigned here rather than as field initializers: initializers run BEFORE this body,
            // so any initializer reading batchDirectorType would always observe null.
            BatchCachedPawnsField = batchDirectorType != null
                ? AccessTools.Field(batchDirectorType, "cachedPawns") : null;
            BatchSelectedPawnsField = batchDirectorType != null
                ? AccessTools.Field(batchDirectorType, "selectedPawns") : null;
            BatchGenerationTasksField = batchDirectorType != null
                ? AccessTools.Field(batchDirectorType, "generationTasks") : null;
            BatchTaskField = batchDirectorType != null
                ? AccessTools.Field(batchDirectorType, "batchTask") : null;
            BatchTaskPawnsField = batchDirectorType != null
                ? AccessTools.Field(batchDirectorType, "batchTaskPawns") : null;
            BatchSearchTextField = batchDirectorType != null
                ? AccessTools.Field(batchDirectorType, "_searchText") : null;
            BatchSortByField = batchDirectorType != null
                ? AccessTools.Field(batchDirectorType, "curSortBy") : null;
            BatchSortAscField = batchDirectorType != null
                ? AccessTools.Field(batchDirectorType, "sortAsc") : null;
            BatchSendModeField = batchDirectorType != null
                ? AccessTools.Field(batchDirectorType, "batchSendMode") : null;
            BatchSortByType = batchDirectorType != null
                ? AccessTools.Inner(batchDirectorType, "SortBy") : null;
            BatchRefreshPawnCacheMethod = batchDirectorType != null
                ? AccessTools.Method(batchDirectorType, "RefreshPawnCache") : null;
            BatchGetPawnTagMethod = batchDirectorType != null
                ? AccessTools.Method(batchDirectorType, "GetPawnTag", new[] { typeof(Pawn) }) : null;

            libraryManagerType = AccessTools.TypeByName("RimPersonaDirector.Window_LibraryManager");
            importExportType = AccessTools.TypeByName("RimPersonaDirector.Window_ImportExport");

            buttonsReady = ready && notesEditorCtor != null;
            batchReady = ready && batchDirectorCtor != null && batchFiltersField != null
                && presetsField != null && selectedPresetIndexField != null && promptPresetLabelField != null
                && buildCombinedCharacterDataMethod != null && generatePersonalityTaskMethod != null
                && generateBatchPersonaTaskMethod != null && openRimTalkDialogMethod != null
                && BatchCachedPawnsField != null && BatchSelectedPawnsField != null
                && BatchGenerationTasksField != null && BatchTaskField != null && BatchTaskPawnsField != null
                && BatchSearchTextField != null && BatchSortByField != null && BatchSortAscField != null
                && BatchSendModeField != null && BatchSortByType != null
                && BatchRefreshPawnCacheMethod != null && BatchGetPawnTagMethod != null;
        }

        // Settings access

        private static object Settings()
        {
            return settingsField.GetValue(null);
        }

        internal static bool IncludeEditNotesButton()
        {
            if (!Ready) return false;
            object context = contextField.GetValue(Settings());
            return context != null && (bool)incDirectorNotesField.GetValue(context);
        }

        internal static bool IncludeEvolveButtons()
        {
            if (!Ready) return false;
            return (bool)enableEvolveField.GetValue(Settings()) && Find.World != null;
        }

        internal static string DirectorNotesText()
        {
            if (!Ready) return "";
            return (string)directorNotesField.GetValue(Settings()) ?? "";
        }

        private static object DirectorWorldComponent()
        {
            return Find.World == null ? null : Find.World.GetComponent(directorWorldComponentType);
        }

        // Edit Notes button: mirrors Patch_PersonaEditorWindow_DirectorFeatures.Postfix's
        // Widgets.ButtonText click, an unconditional new window (vehicle A).

        internal static string EditNotesLabel()
        {
            return (string)Translator.Translate("RPD_Button_EditNotes");
        }

        /// <summary>Mirrors the mod's own hover tooltip content exactly (present-everything rule).</summary>
        internal static string EditNotesTip()
        {
            string notes = DirectorNotesText();
            if (string.IsNullOrEmpty(notes))
            {
                return (string)Translator.Translate("RPD_Tip_NoNotes");
            }
            return string.Format("{0}:\n{1}", (string)Translator.Translate("RPD_Tip_CurrentNotes"), notes);
        }

        internal static void OpenNotesEditorUnconditional()
        {
            if (!buttonsReady) return;
            try
            {
                Find.WindowStack.Add((Window)notesEditorCtor.Invoke(null));
            }
            catch (Exception ex)
            {
                ModLogger.LimitedError("PersonaDirector compat", ex);
            }
        }

        // Evolve button

        internal static bool IsEvolveBusy()
        {
            if (!ready) return false;
            Task task = evolveTaskField.GetValue(null) as Task;
            return task != null && !task.IsCompleted;
        }

        internal static string EvolveLabel()
        {
            return IsEvolveBusy()
                ? (string)Translator.Translate("RPD_Batch_Status_Generating")
                : (string)Translator.Translate("RPD_Button_Evolve");
        }

        internal static string EvolveTip()
        {
            return (string)Translator.Translate("RPD_Tip_Evolve");
        }

        /// <summary>
        /// MUTATION-C: reproduces Patch_PersonaEditorWindow_DirectorFeatures.Postfix's Evolve-click
        /// body exactly (no gated Try*/Can* vehicle exists -- evolveTask/evolveResult/evolvingPawn
        /// are bare private static fields with no accessor of their own), including writing the
        /// SAME static fields the mod's own postfix polls every later pass -- so that postfix keeps
        /// applying the finished result into the persona editor's text buffer on its own, unchanged,
        /// whether the click came from its own painted button or from here.
        /// </summary>
        internal static void TryStartEvolve(Pawn pawn, Window editorWindow)
        {
            if (!ready || pawn == null || editorWindow == null || IsEvolveBusy())
            {
                return; // mirrors the disabled "Generating..." button: no click handler at all
            }
            try
            {
                // MUTATION-C: evolveTask/evolveResult/evolvingPawn are bare private static fields
                // (Patch_PersonaEditorWindow_DirectorFeatures) with no gated setter of their own.
                evolveTaskField.SetValue(null, null);
                evolveResultField.SetValue(null, null);
                evolvingPawnField.SetValue(null, pawn);

                object tuple = prepareEvolveRequestMethod.Invoke(null, new object[] { pawn, editorWindow });
                object request = tuple.GetType().GetField("Item1").GetValue(tuple);
                if (request == null)
                {
                    // Mirrors the mod's own untranslated English literal exactly (vehicle A).
                    Messages.Message("Failed to prepare data.", MessageTypeDefOf.RejectInput, false);
                    return;
                }

                Task<string> task = Task.Run(delegate
                {
                    object result = executeEvolveTaskMethod.Invoke(null, new object[] { request });
                    if (result == null) return null;
                    string persona = (string)personalityDataPersonaProp.GetValue(result);
                    return string.IsNullOrEmpty(persona) ? null : persona.Trim();
                });
                // MUTATION-C: same bare static fields as above.
                evolveTaskField.SetValue(null, task);
                task.ContinueWith(delegate (Task<string> t)
                {
                    // MUTATION-C: same bare static fields as above.
                    if (t.IsCompleted && !t.IsFaulted)
                    {
                        evolveResultField.SetValue(null, t.Result);
                    }
                    evolveTaskField.SetValue(null, null);  // MUTATION-C: same bare static fields as above.
                });
            }
            catch (Exception ex)
            {
                ModLogger.LimitedError("PersonaDirector compat", ex);
            }
        }

        // Set Time button

        internal static string SetTimeLabel(Pawn pawn)
        {
            if (!ready || pawn == null)
            {
                return (string)Translator.Translate("RPD_Button_SetTime");
            }
            int lastEvolveTick = (int)getLastEvolveTickMethod.Invoke(null, new object[] { pawn });
            if (lastEvolveTick <= 0)
            {
                return (string)Translator.Translate("RPD_Button_SetTime");
            }
            int daysAgo = (GenTicks.TicksGame - lastEvolveTick) / 60000;
            return (string)TranslatorFormattedStringExtensions.Translate("RPD_Button_TimeAgo", daysAgo);
        }

        internal static string SetTimeTip(Pawn pawn)
        {
            if (!ready || pawn == null)
            {
                return (string)Translator.Translate("RPD_Tip_SetTime_Empty");
            }
            int lastEvolveTick = (int)getLastEvolveTickMethod.Invoke(null, new object[] { pawn });
            if (lastEvolveTick <= 0)
            {
                return (string)Translator.Translate("RPD_Tip_SetTime_Empty");
            }
            int daysAgo = (GenTicks.TicksGame - lastEvolveTick) / 60000;
            long bioYearsAgo = (long)getLastEvolveBioAgeTicksMethod.Invoke(null, new object[] { pawn }) / 3600000;
            return (string)TranslatorFormattedStringExtensions.Translate("RPD_Tip_SetTime_Info", daysAgo, bioYearsAgo);
        }

        /// <summary>Vehicle A: reproduces the Set Time button's own click body exactly.</summary>
        internal static void SetTime(Pawn pawn)
        {
            if (!ready || pawn == null) return;
            object component = DirectorWorldComponent();
            if (component == null) return;
            try
            {
                string snapshot = (string)buildCustomCharacterDataMethod.Invoke(null, new object[] { pawn, true, false });
                setTimestampMethod.Invoke(component, new object[] { pawn, snapshot });
                Messages.Message((string)Translator.Translate("RPD_Msg_TimestampUpdated"), MessageTypeDefOf.PositiveEvent, false);
            }
            catch (Exception ex)
            {
                ModLogger.LimitedError("PersonaDirector compat", ex);
            }
        }

        // F12 extras hub: mirrors MainButtonWorker_Director.Activate's three mutually-exclusive
        // branches as three explicit entries, since the hub's generic per-def entry can only
        // replay plain-click.

        internal static string F12NotesLabel()
        {
            return ResolveModOrOwnLabel("RPD_Notes_Title", "RimWorldAccess.Compat.RimTalk.PersonaDirector.F12OpenNotesFallback");
        }

        internal static string F12BatchLabel()
        {
            return ResolveModOrOwnLabel("RPD_Batch_Title", "RimWorldAccess.Compat.RimTalk.PersonaDirector.F12OpenBatchFallback");
        }

        internal static string F12SettingsLabel()
        {
            return "RimWorldAccess.Compat.RimTalk.PersonaDirector.F12OpenSettingsFallback".Translate();
        }

        private static string ResolveModOrOwnLabel(string modKey, string ownFallbackKey)
        {
            return CompatText.ResolveOrFallback(modKey, ownFallbackKey);
        }

        /// <summary>Mirrors MainButtonWorker_Director.Activate's else branch (toggle Window_DirectorNotesEditor).</summary>
        internal static void ToggleNotesWindow()
        {
            if (!buttonsReady) return;
            ToggleWindow(notesEditorType, notesEditorCtor);
        }

        /// <summary>Mirrors MainButtonWorker_Director.Activate's right-click branch (toggle Window_BatchDirector).</summary>
        internal static void ToggleBatchWindow()
        {
            if (!batchReady) return;
            ToggleWindow(batchDirectorType, batchDirectorCtor);
        }

        private static void ToggleWindow(Type windowType, ConstructorInfo ctor)
        {
            try
            {
                Window existing = Find.WindowStack.Windows.FirstOrDefault(w => w.GetType() == windowType);
                if (existing != null)
                {
                    existing.Close(true);
                }
                else
                {
                    Find.WindowStack.Add((Window)ctor.Invoke(null));
                }
            }
            catch (Exception ex)
            {
                ModLogger.LimitedError("PersonaDirector compat", ex);
            }
        }

        /// <summary>Mirrors MainButtonWorker_Director.Activate's shift-click branch (open Dialog_ModSettings).</summary>
        internal static void OpenSettings()
        {
            if (!ready) return;
            try
            {
                Mod mod = LoadedModManager.GetMod(directorModType);
                if (mod != null)
                {
                    Find.WindowStack.Add(new Dialog_ModSettings(mod));
                }
            }
            catch (Exception ex)
            {
                ModLogger.LimitedError("PersonaDirector compat", ex);
            }
        }

        // Registration

        public static void Register()
        {
            if (!Ready)
            {
                return;
            }

            if (notesEditorType != null)
            {
                ScopeForWindow.TryRegisterGenericReaderForWindow("RimPersonaDirector.Window_DirectorNotesEditor");
            }
            if (libraryManagerType != null)
            {
                ScopeForWindow.TryRegisterGenericReaderForWindow("RimPersonaDirector.Window_LibraryManager");
            }
            if (importExportType != null)
            {
                ScopeForWindow.TryRegisterGenericReaderForWindow("RimPersonaDirector.Window_ImportExport");
            }
            // Window_PresetBrowser needs no registration: its constructor sets
            // absorbInputAroundWindow = true, which ScopeForWindow's posture test already
            // accepts.

            if (batchReady)
            {
                ScopeForWindow.Register(batchDirectorType, w => new PersonaDirectorBatchScope(w));
            }

        }


        // Shared reflection surface for PersonaDirectorBatchScope, kept here so every
        // PersonaDirector reflection cache lives in one place. Assigned in the static constructor,
        // immediately after batchDirectorType/batchDirectorCtor.

        internal static readonly FieldInfo BatchCachedPawnsField;
        internal static readonly FieldInfo BatchSelectedPawnsField;
        internal static readonly FieldInfo BatchGenerationTasksField;
        internal static readonly FieldInfo BatchTaskField;
        internal static readonly FieldInfo BatchTaskPawnsField;
        internal static readonly FieldInfo BatchSearchTextField;
        internal static readonly FieldInfo BatchSortByField;
        internal static readonly FieldInfo BatchSortAscField;
        internal static readonly FieldInfo BatchSendModeField;
        internal static readonly Type BatchSortByType;
        internal static readonly MethodInfo BatchRefreshPawnCacheMethod;
        internal static readonly MethodInfo BatchGetPawnTagMethod;

        internal static string BuildCharacterData(Pawn pawn)
        {
            return (string)buildCustomCharacterDataMethod.Invoke(null, new object[] { pawn, false, false });
        }

        internal static string BuildCombinedCharacterData(List<Pawn> pawns)
        {
            return (string)buildCombinedCharacterDataMethod.Invoke(null, new object[] { pawns });
        }

        internal static object GeneratePersonalityTask(string characterData, string labelShortCap, Pawn pawn)
        {
            return generatePersonalityTaskMethod.Invoke(null, new object[] { characterData, labelShortCap, pawn });
        }

        internal static object GenerateBatchPersonaTask(string combinedData, Pawn representative)
        {
            return generateBatchPersonaTaskMethod.Invoke(null, new object[] { combinedData, representative });
        }

        internal static void OpenRimTalkDialog(Pawn target)
        {
            openRimTalkDialogMethod.Invoke(null, new object[] { target });
        }

        internal static IDictionary Settings_BatchFilters()
        {
            return (IDictionary)batchFiltersField.GetValue(Settings());
        }

        internal static IList Settings_Presets()
        {
            return (IList)presetsField.GetValue(Settings());
        }

        /// <summary>MUTATION-C: selectedPresetIndex is a bare int field (DirectorSettings) with no gated setter; mirrors the prompt-slot FloatMenu's own return-assign exactly.</summary>
        internal static int Settings_SelectedPresetIndex
        {
            get { return (int)selectedPresetIndexField.GetValue(Settings()); }
            set { selectedPresetIndexField.SetValue(Settings(), value); }  // MUTATION-C: bare int field (DirectorSettings), no gated setter.
        }

        internal static string PromptPresetLabel(object preset)
        {
            return (string)promptPresetLabelField.GetValue(preset);
        }

        /// <summary>MUTATION-C: directorNotes is a bare string field (DirectorSettings) with no gated setter, shared verbatim by Window_DirectorNotesEditor/Window_BatchDirector's own TextArea return-assigns.</summary>
        internal static void SetDirectorNotes(string value)
        {
            directorNotesField.SetValue(Settings(), value ?? "");  // MUTATION-C: bare string field (DirectorSettings), no gated setter.
        }
    }
}
