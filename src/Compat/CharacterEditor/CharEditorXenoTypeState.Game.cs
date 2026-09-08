using System;
using System.Collections.Generic;
using System.Linq;
using RimWorld;
using Verse;
using Verse.Sound;

namespace RimWorldAccess
{
    /// <summary>
    /// Data/mutation facade for Character Editor's <c>DialogXenoType</c>, backing
    /// <see cref="Shell.CharEditorXenoTypeScope"/>. Mirrors <c>XenotypeEditorState</c>
    /// member-for-member over the shared <see cref="XenotypeReflection"/> fields (valid on any
    /// <c>GeneCreationDialogBase</c> subclass), adding only the members resolved against
    /// <c>DialogXenoType</c> itself.
    /// Divergences: no world-gen branch exists on this dialog (<see cref="SaveAndApply"/>/<see cref="Save"/>
    /// always act on the dialog's own pawn via <c>Pawn.SetPawnXenotype</c>); <see cref="Save"/> is a genuine
    /// third button, riding the same reflected vehicle (<c>ACheckSaveAnd</c>) with a different bool.
    /// Load custom/premade call the dialog's own private handlers directly (vehicle A).
    /// </summary>
    internal static class CharEditorXenoTypeState
    {
        private static bool isActive;
        public static bool IsActive => isActive;

        private static readonly TextInputController renameController = new TextInputController();
        private static readonly TextFieldSpec renameSpec =
            RimWorldDialogIntrospector.ForGeneCreationDialog("RimWorldAccess.TextInput.LabelXenotype");
        public static bool IsRenaming => TextInputManager.Active == renameController;

        private static Window dialog;

        private static string VanillaLabel(string key) => ((string)key.Translate()).StripTags();

        // Lifecycle.

        /// <summary>Invoked from <see cref="Shell.CharEditorXenoTypeScope"/>'s constructor; ScopeForWindow's factory runs once per window add, so no reentrancy guard is needed.</summary>
        public static void Open(Window dialogInstance)
        {
            try
            {
                if (dialogInstance == null)
                    return;

                dialog = dialogInstance;
                isActive = true;
            }
            catch (Exception ex)
            {
                ModLogger.Error($"Error in CharEditorXenoTypeState.Open: {ex}");
                Close();
            }
        }

        public static void Close()
        {
            isActive = false;
            if (TextInputManager.Active == renameController) TextInputManager.Clear();
            dialog = null;
        }

        public static bool HasSelectedGenes
        {
            get
            {
                var selected = GetSelectedGenes();
                return selected != null && selected.Count > 0;
            }
        }

        public static bool ShouldAutoExpandForSearch(InspectionTreeItem item)
        {
            return item.Data is GeneCategoryDef;
        }

        // Gene selection toggle.

        // MUTATION-C: mirrors DialogXenoType.DrawSection's gene click handler (the same shape
        // vanilla Dialog_CreateXenotype.DrawSection uses -- both are private and inline to their
        // own section's IMGUI loop, no vehicle A/B exists for either).
        public static void ToggleGene(GeneDef gene)
        {
            if (dialog == null) return;

            var selectedList = GetSelectedGenes();
            if (selectedList == null) return;

            bool adding;
            if (selectedList.Contains(gene))
            {
                selectedList.Remove(gene);
                SoundDefOf.Tick_Low.PlayOneShotOnCamera();
                adding = false;
            }
            else
            {
                selectedList.Add(gene);
                SoundDefOf.Tick_High.PlayOneShotOnCamera();
                adding = true;
            }

            bool nameLocked = (bool)XenotypeReflection.XenotypeNameLockedField.GetValue(dialog);
            if (!nameLocked)
            {
                string newName = GeneUtility.GenerateXenotypeNameFromGenes(selectedList);
                XenotypeReflection.XenotypeNameField.SetValue(dialog, newName);
            }

            XenotypeReflection.OnGenesChangedMethod.Invoke(dialog, null);

            string biostats = FormatCurrentBiostats();
            TolkHelper.Speak(adding
                ? "RimWorldAccess.Biotech.XenotypeEditor.GeneAdded".Loc(gene.LabelCap, biostats)
                : "RimWorldAccess.Biotech.XenotypeEditor.GeneRemoved".Loc(gene.LabelCap, biostats));
        }

        // Save and apply / save.

        /// <summary>DialogXenoType.ACheckSaveAnd(true): saves to file AND applies, the Save-and-Apply button's own delegate.</summary>
        public static void SaveAndApply()
        {
            if (dialog == null) return;
            var savedDialog = dialog;
            Close();
            CharEditorXenoTypeCompat.CheckSaveAnd(savedDialog, apply: true);
        }

        /// <summary>DialogXenoType.ACheckSaveAnd(false): saves without applying, the Save button's own delegate. Vanilla has no such path.</summary>
        public static void Save()
        {
            if (dialog == null) return;
            // DialogXenoType stays open after either button: only doCloseX, Escape or
            // closeOnClickedOutside close it. Announce explicitly, since the window staying open is
            // the mod's only feedback.
            CharEditorXenoTypeCompat.CheckSaveAnd(dialog, apply: false);
            TolkHelper.Speak("RimWorldAccess.CharEd.Saved".Loc());
        }

        // Load custom / premade.

        public static void LoadCustom()
        {
            if (dialog == null) return;

            var files = GenFilePaths.AllCustomXenotypeFiles.ToList();
            if (files.Count == 0)
            {
                SoundDefOf.ClickReject.PlayOneShotOnCamera();
                TolkHelper.Speak("RimWorldAccess.Biotech.XenotypeEditor.NoCustom".Loc());
                return;
            }

            var options = new List<FloatMenuOption>();
            foreach (var file in files)
            {
                string fileName = System.IO.Path.GetFileNameWithoutExtension(file.Name);
                var capturedFile = file;
                options.Add(new FloatMenuOption(fileName, () =>
                {
                    string filePath = capturedFile.FullName;
                    if (GameDataSaveLoader.TryLoadXenotype(filePath, out CustomXenotype xenotype))
                    {
                        CharEditorXenoTypeCompat.LoadCustomXenotype(dialog, xenotype);
                        SoundDefOf.Tick_High.PlayOneShotOnCamera();
                        var selectedList = GetSelectedGenes();
                        int count = selectedList?.Count ?? 0;
                        TolkHelper.Speak(count == 1
                            ? "RimWorldAccess.Biotech.XenotypeEditor.LoadedSummaryOne".Loc(xenotype.name, FormatCurrentBiostats())
                            : "RimWorldAccess.Biotech.XenotypeEditor.LoadedSummaryMany".Loc(xenotype.name, count, FormatCurrentBiostats()));
                    }
                    else
                    {
                        SoundDefOf.ClickReject.PlayOneShotOnCamera();
                        TolkHelper.Speak("RimWorldAccess.Biotech.XenotypeEditor.FailedToLoad".Loc());
                    }
                }));
            }

            WindowlessFloatMenuState.Open(options, false);
        }

        public static void LoadPremade()
        {
            if (dialog == null) return;

            var xenotypes = DefDatabase<XenotypeDef>.AllDefs
                .OrderByDescending(x => x.displayPriority)
                .ToList();

            if (xenotypes.Count == 0)
            {
                SoundDefOf.ClickReject.PlayOneShotOnCamera();
                TolkHelper.Speak("RimWorldAccess.Biotech.XenotypeEditor.NoPremade".Loc());
                return;
            }

            var options = new List<FloatMenuOption>();
            var infoCardDefs = new List<Def>();

            foreach (var xenotype in xenotypes)
            {
                var localXeno = xenotype;
                options.Add(new FloatMenuOption(localXeno.LabelCap, () =>
                {
                    CharEditorXenoTypeCompat.LoadXenotypeDef(dialog, localXeno);
                    SoundDefOf.Tick_High.PlayOneShotOnCamera();
                    var selectedList = GetSelectedGenes();
                    int count = selectedList?.Count ?? 0;
                    TolkHelper.Speak(count == 1
                        ? "RimWorldAccess.Biotech.XenotypeEditor.LoadedSummaryOne".Loc(localXeno.LabelCap, FormatCurrentBiostats())
                        : "RimWorldAccess.Biotech.XenotypeEditor.LoadedSummaryMany".Loc(localXeno.LabelCap, count, FormatCurrentBiostats()));
                }));
                infoCardDefs.Add(localXeno);
            }

            WindowlessFloatMenuState.Open(options, false, infoCardDefs: infoCardDefs);
        }

        // Icon selector.

        public static XenotypeIconDef CurrentIconDef()
        {
            return dialog == null ? null : XenotypeReflection.IconDefField.GetValue(dialog) as XenotypeIconDef;
        }

        /// <summary>Opens vanilla's icon-selector dialog, GeneCreationDialogBase.DrawIconSelector's own vehicle, which DialogXenoType leaves unmodified.</summary>
        public static void OpenIconSelector()
        {
            if (dialog == null) return;
            Find.WindowStack.Add(new Dialog_SelectXenotypeIcon(CurrentIconDef(), delegate (XenotypeIconDef chosen)
            {
                if (dialog == null || chosen == null) return;
                // MUTATION-C: mirrors GeneCreationDialogBase.DrawIconSelector's own inline delegate
                // (`delegate(XenotypeIconDef i) { iconDef = i; }`) -- a bare field write vanilla
                // performs itself with no method to call instead.
                XenotypeReflection.IconDefField.SetValue(dialog, chosen);
                TolkHelper.SpeakData(chosen.label.NullOrEmpty() ? chosen.defName : chosen.LabelCap.ToString());
            }));
        }

        // Rename.

        internal static void BeginRename()
        {
            if (dialog == null) return;
            string currentName = (string)XenotypeReflection.XenotypeNameField.GetValue(dialog);
            renameController.Begin(currentName ?? string.Empty, renameSpec, OnRenameConfirm, OnRenameCancel, replaceOnType: true);
        }

        private static void OnRenameCancel()
        {
            SoundDefOf.Click.PlayOneShotOnCamera();
            TolkHelper.Speak("RimWorldAccess.Biotech.XenotypeEditor.RenameCancelled".Loc());
        }

        private static void OnRenameConfirm(string newName)
        {
            if (dialog == null) return;
            XenotypeReflection.XenotypeNameField.SetValue(dialog, newName);
            XenotypeReflection.XenotypeNameLockedField.SetValue(dialog, true);
            SoundDefOf.Tick_High.PlayOneShotOnCamera();
            TolkHelper.Speak("RimWorldAccess.Biotech.XenotypeEditor.Renamed".Loc(newName));
        }

        public static void ToggleNameLock()
        {
            if (dialog == null) return;
            bool locked = (bool)XenotypeReflection.XenotypeNameLockedField.GetValue(dialog);
            bool newLocked = !locked;
            // MUTATION-C: mirrors GeneCreationDialogBase's name-lock icon button (inline to
            // DoWindowContents, no separate delegate).
            XenotypeReflection.XenotypeNameLockedField.SetValue(dialog, newLocked);
            (newLocked ? SoundDefOf.Checkbox_TurnedOn : SoundDefOf.Checkbox_TurnedOff).PlayOneShotOnCamera();
            TolkHelper.SpeakData(FormatNameLock());
        }

        public static void RandomizeName()
        {
            if (dialog == null) return;
            var genes = GetSelectedGenes();
            if (genes == null || genes.Count == 0)
            {
                SoundDefOf.ClickReject.PlayOneShotOnCamera();
                TolkHelper.Speak("SelectAGeneToRandomizeName".Loc());
                return;
            }
            string newName = GeneUtility.GenerateXenotypeNameFromGenes(genes);
            // MUTATION-C: mirrors GeneCreationDialogBase's Randomize button (inline to
            // DoWindowContents, no separate delegate).
            XenotypeReflection.XenotypeNameField.SetValue(dialog, newName);
            SoundDefOf.Tick_High.PlayOneShotOnCamera();
            TolkHelper.SpeakData($"{((string)"XenotypeName".Translate()).CapitalizeFirst()}: {newName}");
        }

        public static bool IsInheritable()
        {
            return dialog != null && CharEditorXenoTypeCompat.IsInheritable(dialog);
        }

        public static void ToggleInheritable()
        {
            if (dialog == null) return;
            bool newInheritable = !CharEditorXenoTypeCompat.IsInheritable(dialog);
            CharEditorXenoTypeCompat.SetInheritable(dialog, newInheritable);
            (newInheritable ? SoundDefOf.Checkbox_TurnedOn : SoundDefOf.Checkbox_TurnedOff).PlayOneShotOnCamera();
            TolkHelper.SpeakData(FormatInheritable());
        }

        public static string FormatInheritable()
        {
            if (dialog == null) return "";
            string label = VanillaLabel("GenesAreInheritable");
            string value = (IsInheritable()
                ? "RimWorldAccess.Biotech.XenotypeEditor.YesValue"
                : "RimWorldAccess.Biotech.XenotypeEditor.NoValue").Translate();
            return "RimWorldAccess.Biotech.XenotypeEditor.NameWithValue".Translate(label, value);
        }

        public static bool IsIgnoringRestrictions()
        {
            return dialog != null && (bool)XenotypeReflection.IgnoreRestrictionsField.GetValue(dialog);
        }

        /// <summary>
        /// Toggles "ignore restrictions", mirroring PostXenotypeOnGUI's inline checkbox handler.
        /// The same shape as <see cref="RimWorldAccess.XenotypeEditorState.ToggleIgnoreRestrictions"/>,
        /// but against this dialog's OWN separately declared confirmation-sent marker, never the
        /// vanilla one. <paramref name="onChanged"/> fires once the field actually flips, so the
        /// scope can rebuild its trees.
        /// </summary>
        // MUTATION-C: mirrors DialogXenoType.PostXenotypeOnGUI's ignore-restrictions checkbox
        // handler (byte-identical to vanilla's own copy of this handler, just against this
        // dialog's own field and its own confirmation-sent marker); no vehicle A/B exists because
        // that handler is a private inline checkbox body. Every SetValue below rides THIS same
        // marker, repeated at each site because check_mutation_doctrine.py matches within a narrow
        // window of the write.
        public static void ToggleIgnoreRestrictions(Action onChanged)
        {
            if (dialog == null) return;
            bool ignoreRestr = (bool)XenotypeReflection.IgnoreRestrictionsField.GetValue(dialog);

            if (!ignoreRestr)
            {
                bool confirmSent = CharEditorXenoTypeCompat.IgnoreRestrictionsConfirmationSent();
                if (!confirmSent)
                {
                    // MUTATION-C: see method header above.
                    CharEditorXenoTypeCompat.SetIgnoreRestrictionsConfirmationSent(true);
                    SoundDefOf.Checkbox_TurnedOn.PlayOneShotOnCamera();
                    Find.WindowStack.Add(new Dialog_MessageBox(
                        (string)"IgnoreRestrictionsConfirmation".Translate(),
                        (string)"Yes".Translate(),
                        () =>
                        {
                            // MUTATION-C: see method header above.
                            XenotypeReflection.IgnoreRestrictionsField.SetValue(dialog, true);
                            TolkHelper.Speak("RimWorldAccess.Biotech.XenotypeEditor.NameWithValue".Loc(
                                VanillaLabel("IgnoreRestrictions"),
                                "RimWorldAccess.Biotech.XenotypeEditor.YesValue".Translate()));
                            onChanged?.Invoke();
                        },
                        (string)"No".Translate(),
                        () =>
                        {
                            TolkHelper.Speak("RimWorldAccess.Biotech.XenotypeEditor.NameWithValue".Loc(
                                VanillaLabel("IgnoreRestrictions"),
                                "RimWorldAccess.Biotech.XenotypeEditor.NoValue".Translate()));
                        }));
                    return;
                }

                // MUTATION-C: see method header above.
                XenotypeReflection.IgnoreRestrictionsField.SetValue(dialog, true);
                SoundDefOf.Checkbox_TurnedOn.PlayOneShotOnCamera();
                TolkHelper.Speak("RimWorldAccess.Biotech.XenotypeEditor.NameWithValue".Loc(
                    VanillaLabel("IgnoreRestrictions"),
                    "RimWorldAccess.Biotech.XenotypeEditor.YesValue".Translate()));
                onChanged?.Invoke();
            }
            else
            {
                // MUTATION-C: see method header above.
                XenotypeReflection.IgnoreRestrictionsField.SetValue(dialog, false);
                var selectedList = GetSelectedGenes();
                int removed = selectedList.RemoveAll(g => g.biostatArc > 0);
                XenotypeReflection.OnGenesChangedMethod.Invoke(dialog, null);
                SoundDefOf.Checkbox_TurnedOff.PlayOneShotOnCamera();
                string msg = "RimWorldAccess.Biotech.XenotypeEditor.NameWithValue".Translate(
                    VanillaLabel("IgnoreRestrictions"),
                    "RimWorldAccess.Biotech.XenotypeEditor.NoValue".Translate());
                if (removed > 0)
                    msg += (removed == 1
                        ? "RimWorldAccess.Biotech.XenotypeEditor.AchiteRemovedOne".Translate()
                        : "RimWorldAccess.Biotech.XenotypeEditor.AchiteRemovedMany".Translate(removed));
                TolkHelper.SpeakData(msg);
                onChanged?.Invoke();
            }
        }

        // Announcements and formatting.

        public static string ComposeOpeningPreamble()
        {
            string header = "RimWorldAccess.CharEd.XenoType.Header".Translate().ToString();
            return header + ".";
        }

        public static string FormatCurrentBiostats()
        {
            if (dialog == null) return "";

            int gcx = (int)XenotypeReflection.GcxField.GetValue(dialog);
            int met = (int)XenotypeReflection.MetField.GetValue(dialog);
            int arc = (int)XenotypeReflection.ArcField.GetValue(dialog);

            string complexityLabel = ((string)"Complexity".Translate()).CapitalizeFirst();
            string metabolismLabel = ((string)"Metabolism".Translate()).CapitalizeFirst();

            var sb = new System.Text.StringBuilder();
            sb.Append($"{complexityLabel} {gcx}");
            sb.Append($", {metabolismLabel} {met.ToStringWithSign()}");

            if (arc > 0)
            {
                string architesLabel = ((string)"ArchitesRequired".Translate()).CapitalizeFirst();
                sb.Append($", {architesLabel} {arc}");
            }

            return sb.ToString();
        }

        public static string CurrentXenotypeName()
        {
            if (dialog == null) return "";
            return (string)XenotypeReflection.XenotypeNameField.GetValue(dialog);
        }

        public static bool IsNameLocked()
        {
            if (dialog == null) return false;
            return (bool)XenotypeReflection.XenotypeNameLockedField.GetValue(dialog);
        }

        public static string FormatNameLock()
        {
            if (dialog == null) return "";
            bool locked = (bool)XenotypeReflection.XenotypeNameLockedField.GetValue(dialog);
            return locked ? VanillaLabel("LockNameOn") : VanillaLabel("LockNameOff");
        }

        public static string FormatNameLockTooltip()
        {
            return VanillaLabel("LockNameButtonDesc");
        }

        // Close.

        internal static void CloseDialog()
        {
            if (dialog != null)
            {
                dialog.Close();
            }
            Close();
            TolkHelper.Speak("Close".Loc());
        }

        // Tree building; the scope owns the TreeModel instances and calls these to rebuild them.

        internal static InspectionTreeItem BuildSelectedTreeRoot()
        {
            var selected = GetSelectedGenes();
            return XenotypeTreeBuilder.BuildSelectedTree(selected,
                GeneConflictReader.StatusFor(dialog as GeneCreationDialogBase, selected, selectedSection: true));
        }

        internal static InspectionTreeItem BuildLibraryTreeRoot()
        {
            bool ignoreRestr = dialog != null && IsIgnoringRestrictions();
            var selected = GetSelectedGenes();
            return XenotypeTreeBuilder.BuildLibraryTree(selected, ignoreRestr,
                GeneConflictReader.StatusFor(dialog as GeneCreationDialogBase, selected, selectedSection: false));
        }

        // Reflection accessors.

        public static List<GeneDef> GetSelectedGenes()
        {
            return dialog != null ? CharEditorXenoTypeCompat.GetSelectedGenes(dialog) : null;
        }
    }
}
