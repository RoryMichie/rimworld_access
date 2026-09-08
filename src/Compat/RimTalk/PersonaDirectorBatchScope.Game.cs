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
    /// Keyboard support for PersonaDirector's <c>RimPersonaDirector.Window_BatchDirector</c>: a
    /// sortable pawn table with per-row multi-select, batch/single LLM generation, and per-row
    /// Talk/Quick Gen/Deep Edit actions. The mouse's rubber-band drag-select and click-and-drag
    /// checkbox painting (<c>_isDragging</c>/<c>_dragStartIndex</c>) have no keyboard equivalent,
    /// so the row's default Activate toggles that one pawn's membership, reaching the identical
    /// end SET one row at a time.
    ///
    /// Everything else -- Switch to Simple, the Scene Notes Clear button, Select All, Refresh, the
    /// prompt-slot picker, the filter checkboxes, Batch Gen, and the Single/Batch mode toggle --
    /// is DECLARED here rather than captured. The filter row uses the raw
    /// Checkbox+Label+ButtonInvisible idiom rather than <c>CheckboxLabeled</c>, and this
    /// scope reproduces each control's own field mutation directly (MUTATION-C -- every one of
    /// these backing fields/dictionaries is bare, with no gated setter of its own) rather than
    /// leaning on the capture engine's screen-space fusion heuristic for a shape that has not been
    /// live-verified against PersonaDirector's layout.
    ///
    /// ASYNC GENERATION: QuickGen/BatchGen post the SAME <c>generationTasks</c>/<c>batchTask</c>
    /// fields <c>UpdateAsyncTasks()</c> already polls every <c>DoWindowContents</c> pass, and the
    /// window keeps drawing while open so that poll keeps running. Its own
    /// <c>Messages.Message</c> success/fail lines fire unmodified and are already spoken by the
    /// message-to-speech bridge, so the mod's own completion signal IS the wait-for-settled
    /// signal and no separate poll is needed here.
    ///
    /// ACCEPT/CANCEL: Window_BatchDirector overrides neither <c>OnAcceptKeyPressed</c> nor
    /// <c>OnCancelKeyPressed</c>, so <see cref="ScreenScope"/>'s own defaults are correct.
    /// </summary>
    internal sealed class PersonaDirectorBatchScope : ScreenScope
    {
        private const int TextFieldsRegion = 0;
        private const int PawnsRegion = 1;

        private enum TextFieldItem
        {
            SceneNotes,
            Search,
        }

        private const int ColSelected = 0;
        private const int ColName = 1;
        private const int ColRace = 2;
        private const int ColFaction = 3;
        private const int ColTag = 4;
        private const int ColTalk = 5;
        private const int ColQuickGen = 6;
        private const int ColDeepEdit = 7;

        private readonly Window dialog;
        private readonly TextFieldEditSession session = new TextFieldEditSession();
        private readonly List<Pawn> pawns = new List<Pawn>();

        public PersonaDirectorBatchScope(Window dialog)
        {
            this.dialog = dialog;

            RegisterPopTeardown(session.CancelIfActive);
        }

        public override string Name
        {
            get { return "rimtalk-persona-director-batch"; }
        }

        protected override int ContentRegionCount
        {
            get { return 2; }
        }

        protected override string ContentRegionName(int region)
        {
            return region == TextFieldsRegion
                ? "RimWorldAccess.Compat.RimTalk.PersonaDirector.TextFieldsRegionName".Translate()
                : "RimWorldAccess.Compat.RimTalk.PersonaDirector.PawnsRegionName".Translate();
        }

        protected override void RefreshContent()
        {
            pawns.Clear();
            List<Pawn> live = (List<Pawn>)PersonaDirectorCompat.BatchCachedPawnsField.GetValue(dialog);
            if (live != null)
            {
                pawns.AddRange(live);
            }
        }

        protected override int ContentItemCount(int region)
        {
            return region == TextFieldsRegion ? 2 : pawns.Count;
        }

        protected override int ContentColumnCount(int region)
        {
            return region == PawnsRegion ? 8 : 0;
        }

        protected override TableColumnInfo ContentColumnInfo(int region, int column)
        {
            if (region != PawnsRegion) return null;
            switch (column)
            {
                case ColSelected: return new TableColumnInfo("RimWorldAccess.Compat.RimTalk.PersonaDirector.HeaderSelected".Translate());
                case ColName: return new TableColumnInfo(H("RPD_Batch_Header_Name"), null, true);
                case ColRace: return new TableColumnInfo(H("RPD_Batch_Header_Race"), null, true);
                case ColFaction: return new TableColumnInfo(H("RPD_Batch_Header_Faction"), null, true);
                case ColTag: return new TableColumnInfo(H("RPD_Batch_Header_Tag"), null, true);
                case ColTalk: return new TableColumnInfo(H("RPD_Batch_Button_Talk"));
                case ColQuickGen: return new TableColumnInfo(H("RPD_Batch_Button_QuickGen"));
                default: return new TableColumnInfo(H("RPD_Batch_Button_DeepEdit"));
            }
        }

        private static string H(string key)
        {
            return (string)Translator.Translate(key);
        }

        protected override string ContentCellText(int region, int row, int column)
        {
            if (region != PawnsRegion || row < 0 || row >= pawns.Count) return "";
            Pawn pawn = pawns[row];
            bool generating = IsGenerating(pawn);
            switch (column)
            {
                case ColSelected:
                    return Selected().Contains(pawn)
                        ? "RimWorldAccess.Compat.RimTalk.PersonaDirector.Selected".Translate()
                        : "RimWorldAccess.Compat.RimTalk.PersonaDirector.NotSelected".Translate();
                case ColName:
                    return pawn.LabelShortCap;
                case ColRace:
                    return pawn.genes != null && pawn.genes.Xenotype != null ? pawn.genes.XenotypeLabel : pawn.def.label;
                case ColFaction:
                    return pawn.Faction != null ? pawn.Faction.Name : (string)Translator.Translate("RPD_Faction_None");
                case ColTag:
                    return (string)PersonaDirectorCompat.BatchGetPawnTagMethod.Invoke(dialog, new object[] { pawn });
                default:
                    return generating ? H("RPD_Batch_Status_Generating") : H(TalkLikeKey(column));
            }
        }

        private static string TalkLikeKey(int column)
        {
            switch (column)
            {
                case ColTalk: return "RPD_Batch_Button_Talk";
                case ColQuickGen: return "RPD_Batch_Button_QuickGen";
                default: return "RPD_Batch_Button_DeepEdit";
            }
        }

        private HashSet<Pawn> Selected()
        {
            return (HashSet<Pawn>)PersonaDirectorCompat.BatchSelectedPawnsField.GetValue(dialog);
        }

        private bool IsGenerating(Pawn pawn)
        {
            IDictionary tasks = (IDictionary)PersonaDirectorCompat.BatchGenerationTasksField.GetValue(dialog);
            if (tasks != null && tasks.Contains(pawn))
            {
                Task t = tasks[pawn] as Task;
                if (t != null && !t.IsCompleted) return true;
            }
            Task batch = PersonaDirectorCompat.BatchTaskField.GetValue(dialog) as Task;
            if (batch != null && !batch.IsCompleted)
            {
                List<Pawn> batchPawns = (List<Pawn>)PersonaDirectorCompat.BatchTaskPawnsField.GetValue(dialog);
                if (batchPawns != null && batchPawns.Contains(pawn)) return true;
            }
            return false;
        }

        protected override ElementDescription DescribeContentItem(int region, int index)
        {
            ElementDescription d = new ElementDescription();
            if (region == TextFieldsRegion)
            {
                d.Role = ElementRole.TextField;
                if (index == (int)TextFieldItem.SceneNotes)
                {
                    d.Label = (string)Translator.Translate("RPD_Batch_SceneNotes");
                    string value = PersonaDirectorCompat.DirectorNotesText();
                    if (string.IsNullOrEmpty(value)) d.ValueBlank = true; else d.Value = value;
                }
                else
                {
                    d.Label = "RimWorldAccess.Compat.RimTalk.PersonaDirector.SearchLabel".Translate();
                    string value = (string)PersonaDirectorCompat.BatchSearchTextField.GetValue(dialog) ?? "";
                    if (string.IsNullOrEmpty(value)) d.ValueBlank = true; else d.Value = value;
                }
                return d;
            }

            d.Label = ContentCellText(PawnsRegion, index, ColName);
            return d;
        }

        protected override void ActivateContentItem(int region, int index)
        {
            if (region == TextFieldsRegion)
            {
                BeginEditTextField((TextFieldItem)index);
                return;
            }
            ToggleSelection(index);
        }

        private void ToggleSelection(int index)
        {
            if (index < 0 || index >= pawns.Count) return;
            HashSet<Pawn> selected = Selected();
            Pawn pawn = pawns[index];
            if (!selected.Add(pawn))
            {
                selected.Remove(pawn);
            }
            AnnounceCurrentItem();
        }

        protected override bool ActivateContentCell(int region, int row, int column)
        {
            if (region != PawnsRegion || row < 0 || row >= pawns.Count) return false;
            Pawn pawn = pawns[row];

            switch (column)
            {
                case ColName:
                    if (pawn.Spawned && !pawn.Destroyed)
                    {
                        Find.Selector.ClearSelection();
                        Find.Selector.Select(pawn, true, true);
                        CameraJumper.TryJump(pawn, CameraJumper.MovementMode.Cut);
                    }
                    else
                    {
                        PersonaDirectorCompat.BatchRefreshPawnCacheMethod.Invoke(dialog, null);
                        RefreshModel();
                    }
                    return true;
                case ColTalk:
                    if (IsGenerating(pawn)) return true;
                    PersonaDirectorCompat.OpenRimTalkDialog(pawn);
                    return true;
                case ColQuickGen:
                {
                    if (IsGenerating(pawn)) return true;
                    IDictionary tasks = (IDictionary)PersonaDirectorCompat.BatchGenerationTasksField.GetValue(dialog);
                    string data = PersonaDirectorCompat.BuildCharacterData(pawn);
                    tasks[pawn] = PersonaDirectorCompat.GeneratePersonalityTask(data, pawn.LabelShortCap, pawn);
                    RefreshModel();
                    AnnounceCurrentItem();
                    return true;
                }
                case ColDeepEdit:
                    if (IsGenerating(pawn)) return true;
                    RimTalkPersonaDialogCompat.OpenPersonaEditor(pawn);
                    return true;
                default:
                    return false;
            }
        }

        // Sort (Name/Race/Faction/Tag only, matching DrawHeaderCell). The mod's own sort is a
        // 2-state toggle against this codebase's 3-state cycle; Cleared maps to no sort column,
        // a state the mod displays correctly but cannot reach by clicking.

        /// <summary>MUTATION-C: curSortBy/sortAsc are bare private fields (Window_BatchDirector) DrawHeaderCell itself writes with no gated setter; this mirrors that write, then calls the mod's own RefreshPawnCache to re-derive the sort exactly as a header click would.</summary>
        protected override int ApplyContentSort(int region, int column, SortCycleResult cycle, int currentRow)
        {
            if (region != PawnsRegion) return -1;
            int ordinal = column - ColName; // Name=0,Race=1,Faction=2,Tag=3, matching the mod's own SortBy enum order
            if (ordinal < 0 || ordinal > 3) return -1;

            Pawn current = currentRow >= 0 && currentRow < pawns.Count ? pawns[currentRow] : null;
            Array sortByValues = Enum.GetValues(PersonaDirectorCompat.BatchSortByType);

            if (cycle == SortCycleResult.Cleared)
            {
                PersonaDirectorCompat.BatchSortByField.SetValue(dialog, sortByValues.GetValue(0)); // Name asc: the mod's own default state  // MUTATION-C: bare private fields (Window_BatchDirector), no gated setter.
                PersonaDirectorCompat.BatchSortAscField.SetValue(dialog, true);  // MUTATION-C: bare private fields (Window_BatchDirector), no gated setter.
            }
            else
            {
                PersonaDirectorCompat.BatchSortByField.SetValue(dialog, sortByValues.GetValue(ordinal));  // MUTATION-C: bare private fields (Window_BatchDirector), no gated setter.
                PersonaDirectorCompat.BatchSortAscField.SetValue(dialog, cycle == SortCycleResult.SortedAscending);
            }
            PersonaDirectorCompat.BatchRefreshPawnCacheMethod.Invoke(dialog, null);
            RefreshContent();

            if (current == null) return 0;
            int idx = pawns.IndexOf(current);
            return idx >= 0 ? idx : 0;
        }

        // Text fields. Scene Notes shares DirectorSettings.directorNotes with
        // Window_DirectorNotesEditor; Search is this window's own private field.

        private void BeginEditTextField(TextFieldItem item)
        {
            string current = item == TextFieldItem.SceneNotes
                ? PersonaDirectorCompat.DirectorNotesText()
                : (string)PersonaDirectorCompat.BatchSearchTextField.GetValue(dialog) ?? "";
            string labelKey = item == TextFieldItem.SceneNotes
                ? "RPD_Batch_SceneNotes"
                : "RimWorldAccess.Compat.RimTalk.PersonaDirector.SearchLabel";
            string label = (string)Translator.Translate(labelKey);

            TextFieldSpec spec = new TextFieldSpec(labelKey: "RimWorldAccess.TextInput.LabelDefault", maxLength: null, minLength: 0);
            session.EnterEdit(
                current,
                spec,
                label,
                value => ApplyTextField(item, value),
                AnnounceCurrentItem,
                announcePrompt: true);
        }

        /// <summary>MUTATION-C: _searchText is a bare private string field (Window_BatchDirector) with no gated setter; mirrors DrawFilterSection's own TextField return-assign exactly, then re-derives the filtered list the same way a keystroke would.</summary>
        private void ApplyTextField(TextFieldItem item, string value)
        {
            if (item == TextFieldItem.SceneNotes)
            {
                PersonaDirectorCompat.SetDirectorNotes(value);
            }
            else
            {
                PersonaDirectorCompat.BatchSearchTextField.SetValue(dialog, value ?? "");  // MUTATION-C: bare private field (Window_BatchDirector), no gated setter.
                PersonaDirectorCompat.BatchRefreshPawnCacheMethod.Invoke(dialog, null);
                RefreshModel();
            }
        }

        // Declared (non-captured) actions: Switch to Simple, Clear Notes, Select All, Refresh,
        // the prompt-slot picker, the filter checkboxes, Batch Gen, mode toggle.

        protected override bool CaptureWindowButtons
        {
            get { return false; }
        }

        private static readonly string[] FilterKeys =
        {
            "Colonists", "Prisoners", "Slaves", "Visitors", "Enemies", "Animals", "Mechs", "Anomalies",
        };

        private readonly List<ScreenAction> actions = new List<ScreenAction>();

        protected override IReadOnlyList<ScreenAction> DeclaredActions
        {
            get
            {
                actions.Clear();
                actions.Add(new ScreenAction(
                    (string)Translator.Translate("RPD_Mode_SwitchToSimple"), SwitchToSimple, "rimTalkPersonaDirectorBatch.switchToSimple"));
                actions.Add(new ScreenAction(
                    (string)Translator.Translate("RPD_Button_Clear"), ClearNotes, "rimTalkPersonaDirectorBatch.clearNotes"));
                actions.Add(new ScreenAction(
                    (string)Translator.Translate("RPD_Batch_SelectAll"), SelectAllFilters, "rimTalkPersonaDirectorBatch.selectAll"));
                actions.Add(new ScreenAction(
                    (string)Translator.Translate("RPD_Batch_Refresh"), Refresh, "rimTalkPersonaDirectorBatch.refresh"));
                actions.Add(new ScreenAction(
                    PromptSlotLabel(), OpenPromptSlotMenu, "rimTalkPersonaDirectorBatch.promptSlot"));

                IDictionary filters = PersonaDirectorCompat.Settings_BatchFilters();
                foreach (string key in FilterKeys)
                {
                    if (key == "Anomalies" && !ModsConfig.AnomalyActive) continue;
                    if (filters == null || !filters.Contains(key)) continue;
                    string filterKey = key;
                    bool value = (bool)filters[filterKey];
                    actions.Add(new ScreenAction(
                        (string)Translator.Translate("RPD_Filter_" + filterKey),
                        () => ToggleFilter(filterKey),
                        "rimTalkPersonaDirectorBatch.filter." + filterKey,
                        check: value ? CheckState.Checked : CheckState.Unchecked));
                }

                actions.Add(new ScreenAction(BatchGenLabel(), BatchGen, "rimTalkPersonaDirectorBatch.batchGen"));
                actions.Add(new ScreenAction(ModeToggleLabel(), ToggleMode, "rimTalkPersonaDirectorBatch.modeToggle"));
                return actions;
            }
        }

        private string PromptSlotLabel()
        {
            IList presets = PersonaDirectorCompat.Settings_Presets();
            if (presets == null || presets.Count == 0) return "";
            int index = PersonaDirectorCompat.Settings_SelectedPresetIndex;
            if (index < 0 || index >= presets.Count) index = 0;
            return PersonaDirectorCompat.PromptPresetLabel(presets[index]);
        }

        private string BatchGenLabel()
        {
            return (string)TranslatorFormattedStringExtensions.Translate("RPD_Batch_Button_BatchGen", Selected().Count);
        }

        private string ModeToggleLabel()
        {
            bool batchMode = (bool)PersonaDirectorCompat.BatchSendModeField.GetValue(dialog);
            return (string)Translator.Translate(batchMode ? "RPD_Batch_Button_ModeBatch" : "RPD_Batch_Button_ModeSingle");
        }

        private void SwitchToSimple()
        {
            dialog.Close(true);
            PersonaDirectorCompat.ToggleNotesWindow();
        }

        /// <summary>MUTATION-C: mirrors DrawNotesSection's own return-assign (directorNotes is a bare string field with no gated setter).</summary>
        private void ClearNotes()
        {
            PersonaDirectorCompat.SetDirectorNotes("");
        }

        /// <summary>MUTATION-C: mirrors DrawFilterSection's Select All click exactly (toggles every key to the same value, then refreshes).</summary>
        private void SelectAllFilters()
        {
            IDictionary filters = PersonaDirectorCompat.Settings_BatchFilters();
            if (filters == null) return;
            bool anyOff = false;
            foreach (DictionaryEntry entry in filters)
            {
                if (!(bool)entry.Value) { anyOff = true; break; }
            }
            List<string> keys = filters.Keys.Cast<string>().ToList();
            foreach (string key in keys)
            {
                filters[key] = anyOff;
            }
            PersonaDirectorCompat.BatchRefreshPawnCacheMethod.Invoke(dialog, null);
            RefreshModel();
        }

        private void ToggleFilter(string key)
        {
            IDictionary filters = PersonaDirectorCompat.Settings_BatchFilters();
            if (filters == null || !filters.Contains(key)) return;
            filters[key] = !(bool)filters[key];
            PersonaDirectorCompat.BatchRefreshPawnCacheMethod.Invoke(dialog, null);
            RefreshModel();
        }

        private void Refresh()
        {
            PersonaDirectorCompat.BatchRefreshPawnCacheMethod.Invoke(dialog, null);
            RefreshModel();
        }

        /// <summary>MUTATION-C: mirrors the prompt-slot ButtonText's own FloatMenu build exactly (index 3, "Evolution", is reserved and skipped).</summary>
        private void OpenPromptSlotMenu()
        {
            IList presets = PersonaDirectorCompat.Settings_Presets();
            if (presets == null) return;
            List<FloatMenuOption> options = new List<FloatMenuOption>();
            for (int i = 0; i < presets.Count; i++)
            {
                if (i == 3) continue;
                int index = i;
                string label = PersonaDirectorCompat.PromptPresetLabel(presets[i]);
                options.Add(new FloatMenuOption(label, () => { PersonaDirectorCompat.Settings_SelectedPresetIndex = index; }));
            }
            Find.WindowStack.Add(new FloatMenu(options));
        }

        /// <summary>MUTATION-C: batchTask/batchTaskPawns are bare private fields (Window_BatchDirector) with no gated setter; reproduces DrawGlobalActions' Batch Gen click exactly (single-mode/batch-mode branch, posting into the same generationTasks/batchTask fields UpdateAsyncTasks() already polls).</summary>
        private void BatchGen()
        {
            HashSet<Pawn> selected = Selected();
            bool batchMode = (bool)PersonaDirectorCompat.BatchSendModeField.GetValue(dialog);
            if (batchMode)
            {
                Task existingBatch = PersonaDirectorCompat.BatchTaskField.GetValue(dialog) as Task;
                if (existingBatch == null && selected.Count > 0)
                {
                    List<Pawn> batchPawns = selected.ToList();
                    PersonaDirectorCompat.BatchTaskPawnsField.SetValue(dialog, batchPawns);  // MUTATION-C: bare private field (Window_BatchDirector), no gated setter.
                    string combined = PersonaDirectorCompat.BuildCombinedCharacterData(batchPawns);
                    Pawn representative = batchPawns.FirstOrDefault();
                    PersonaDirectorCompat.BatchTaskField.SetValue(dialog, PersonaDirectorCompat.GenerateBatchPersonaTask(combined, representative));  // MUTATION-C: bare private field (Window_BatchDirector), no gated setter.
                }
            }
            else
            {
                IDictionary tasks = (IDictionary)PersonaDirectorCompat.BatchGenerationTasksField.GetValue(dialog);
                foreach (Pawn pawn in selected)
                {
                    if (tasks.Contains(pawn)) continue;
                    string data = PersonaDirectorCompat.BuildCharacterData(pawn);
                    tasks[pawn] = PersonaDirectorCompat.GeneratePersonalityTask(data, pawn.LabelShortCap, pawn);
                }
            }
            RefreshModel();
        }

        /// <summary>MUTATION-C: batchSendMode is a bare private bool field (Window_BatchDirector) with no gated setter; mirrors the mode-toggle button's own return-assign exactly.</summary>
        private void ToggleMode()
        {
            bool batchMode = (bool)PersonaDirectorCompat.BatchSendModeField.GetValue(dialog);
            PersonaDirectorCompat.BatchSendModeField.SetValue(dialog, !batchMode);  // MUTATION-C: bare private field (Window_BatchDirector), no gated setter.
        }

        // Captured extras: this window draws nothing outside the regions/actions above.

        protected override bool IncludeCapturedExtrasRegion
        {
            get { return false; }
        }

        protected override string ComposeOpenAnnouncement()
        {
            return (string)PersonaDirectorCompat.F12BatchLabel();
        }
    }
}
