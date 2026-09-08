using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using RimWorld;
using Verse;
using Verse.Sound;

namespace RimWorldAccess
{
    /// <summary>
    /// Data/mutation facade for Dialog_CreateXenotype (xenotype editor). A pure facade:
    /// navigation, tab/tree/Controls-list state, and announcement composition all live on
    /// <see cref="RimWorldAccess.Shell.XenotypeEditorScope"/> (a
    /// <see cref="RimWorldAccess.Shell.GeneDialogScopeBase"/> subclass).
    /// <see cref="XenotypeTreeBuilder"/> holds the two pure tree-construction functions the
    /// scope calls to rebuild its <c>GeneTreeRegion</c> instances; there is no Controls-list
    /// builder, since Controls-region rows are plain element-role rows described and
    /// activated directly by the scope.
    /// </summary>
    public static class XenotypeEditorState
    {
        // ===== Global State =====
        private static bool isActive;
        public static bool IsActive => isActive;
        private static readonly TextInputController renameController = new TextInputController();
        private static readonly TextFieldSpec renameSpec =
            RimWorldDialogIntrospector.ForGeneCreationDialog("RimWorldAccess.TextInput.LabelXenotype");
        public static bool IsRenaming => TextInputManager.Active == renameController;

        private static Window dialog;

        // ===== Reflection Cache =====
        // Members used only by this facade. Anything XenotypeTreeBuilder also needs lives in
        // XenotypeReflection instead: the split follows call-site sharing, not member kind.
        private static FieldInfo fi_selectedGenes;
        private static MethodInfo mi_accept;
        private static MethodInfo mi_canAccept;

        static XenotypeEditorState()
        {
            fi_selectedGenes = AccessTools.Field(typeof(Dialog_CreateXenotype), "selectedGenes");
            mi_accept = AccessTools.Method(typeof(Dialog_CreateXenotype), "Accept");
            mi_canAccept = AccessTools.Method(typeof(Dialog_CreateXenotype), "CanAccept");
        }

        // ===== Lifecycle =====

        /// <summary>
        /// Deliberately unguarded against reentrancy: this runs from Dialog_CreateXenotype.PostOpen,
        /// which WindowStack.Add calls exactly once per window — unlike a DoWindowContents prefix,
        /// which reruns every frame and does need such a guard.
        /// </summary>
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
                Log.Error($"[RimWorld Access] Error in XenotypeEditorState.Open: {ex}");
                Close();
            }
        }

        public static void Close()
        {
            isActive = false;
            if (TextInputManager.Active == renameController) TextInputManager.Clear();
            dialog = null;
        }

        /// <summary>Whether anything is currently selected -- the scope uses this to decide whether to open on Selected or Library.</summary>
        public static bool HasSelectedGenes
        {
            get
            {
                var selected = GetSelectedGenes();
                return selected != null && selected.Count > 0;
            }
        }

        /// <summary>Whether a tree node's own children should auto-expand for typeahead search -- GeneCategoryDef header containers only.</summary>
        public static bool ShouldAutoExpandForSearch(InspectionTreeItem item)
        {
            return item.Data is GeneCategoryDef;
        }

        // ===== Gene Selection Toggle =====

        // MUTATION-C: mirrors Dialog_CreateXenotype.DrawSection's gene click handler
        // (decompiled Dialog_CreateXenotype.cs, the branch that adds/removes from
        // selectedGenes with Tick_High/Tick_Low then OnGenesChanged); no vehicle A/B
        // exists because that handler is private and inline to the section's IMGUI loop.
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

            // The scope rebuilds the trees and restores the cursor once this returns.
            string biostats = FormatCurrentBiostats();
            TolkHelper.Speak(adding
                ? "RimWorldAccess.Biotech.XenotypeEditor.GeneAdded".Loc(gene.LabelCap, biostats)
                : "RimWorldAccess.Biotech.XenotypeEditor.GeneRemoved".Loc(gene.LabelCap, biostats));
        }

        // ===== Save and Apply =====

        // Vehicle B (CanAccept/Accept pairing kept in this file per
        // scripts/check_mutation_doctrine.py's CanAccept-pairing check).
        public static void SaveAndApply()
        {
            if (dialog == null) return;

            // The dialog's own CanAccept is the gate the vanilla Accept button runs. On failure it
            // shows the vanilla rejection Message, which the message pipeline announces, and the
            // editor stays open to fix and retry.
            try
            {
                if (!(bool)mi_canAccept.Invoke(dialog, null))
                {
                    SoundDefOf.ClickReject.PlayOneShotOnCamera();
                    return;
                }
            }
            catch (Exception ex)
            {
                Log.Error($"[RimWorld Access] Error invoking CanAccept: {ex}");
                return;
            }

            var savedDialog = dialog;
            Close();
            try
            {
                mi_accept.Invoke(savedDialog, null);
            }
            catch (Exception ex)
            {
                Log.Error($"[RimWorld Access] Error invoking Accept: {ex}");
            }
        }

        // ===== Load Custom / Premade =====

        public static void LoadCustom()
        {
            if (dialog == null) return;
            var savedDialog = dialog;
            // MUTATION-C: mirrors Dialog_CreateXenotype.DrawSearchRect's LoadCustom callback
            // (decompiled Dialog_CreateXenotype.cs:449-459) field-for-field, ignoreRestrictions
            // assigned AFTER OnGenesChanged; no vehicle A/B exists because that callback is a
            // private inline delegate. The dialog, its version gate, and the scope refresh are
            // vanilla's own.
            Find.WindowStack.Add(new Dialog_XenotypeList_Load(delegate (CustomXenotype xenotype)
            {
                if (savedDialog == null || !isActive) return;
                XenotypeReflection.XenotypeNameField.SetValue(savedDialog, xenotype.name);
                XenotypeReflection.XenotypeNameLockedField.SetValue(savedDialog, true);
                var selectedList = (List<GeneDef>)fi_selectedGenes.GetValue(savedDialog);
                selectedList.Clear();
                selectedList.AddRange(xenotype.genes);
                XenotypeReflection.InheritableField.SetValue(savedDialog, xenotype.inheritable);
                XenotypeReflection.IconDefField.SetValue(savedDialog, xenotype.IconDef);
                XenotypeReflection.OnGenesChangedMethod.Invoke(savedDialog, null);
                XenotypeReflection.IgnoreRestrictionsField.SetValue(savedDialog,
                    xenotype.genes.Any(g => g.biostatArc > 0) || !WithinAcceptableBiostatLimits(savedDialog));
                SoundDefOf.Tick_High.PlayOneShotOnCamera();
                TolkHelper.Speak(xenotype.genes.Count == 1
                    ? "RimWorldAccess.Biotech.XenotypeEditor.LoadedSummaryOne".Loc(xenotype.name, FormatCurrentBiostats())
                    : "RimWorldAccess.Biotech.XenotypeEditor.LoadedSummaryMany".Loc(xenotype.name, xenotype.genes.Count, FormatCurrentBiostats()));
            }));
        }

        private static bool WithinAcceptableBiostatLimits(Window dialogInstance)
        {
            return (bool)XenotypeReflection.WithinAcceptableBiostatLimitsMethod.Invoke(
                dialogInstance, new object[] { false });
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
                options.Add(new FloatMenuOption(localXeno.LabelCap, () => ApplyPremadeXenotype(localXeno)));
                infoCardDefs.Add(localXeno);
            }

            WindowlessFloatMenuState.Open(options, false, infoCardDefs: infoCardDefs);
        }

        // MUTATION-C: mirrors Dialog_CreateXenotype.DrawSearchRect's LoadPremade
        // FloatMenuOption callback (decompiled Dialog_CreateXenotype.cs:469-476) field-for-field,
        // including the ignoreRestrictions formula assigned AFTER OnGenesChanged; no vehicle A/B
        // exists because that callback is a private inline delegate.
        private static void ApplyPremadeXenotype(XenotypeDef xenotype)
        {
            if (dialog == null) return;

            XenotypeReflection.XenotypeNameField.SetValue(dialog, xenotype.label);

            var selectedList = GetSelectedGenes();
            selectedList.Clear();
            selectedList.AddRange(xenotype.genes);

            XenotypeReflection.InheritableField.SetValue(dialog, xenotype.inheritable);

            XenotypeReflection.OnGenesChangedMethod.Invoke(dialog, null);

            XenotypeReflection.IgnoreRestrictionsField.SetValue(dialog,
                selectedList.Any(g => g.biostatArc > 0) || !WithinAcceptableBiostatLimits(dialog));

            SoundDefOf.Tick_High.PlayOneShotOnCamera();
            TolkHelper.Speak(selectedList.Count == 1
                ? "RimWorldAccess.Biotech.XenotypeEditor.LoadedSummaryOne".Loc(xenotype.LabelCap, FormatCurrentBiostats())
                : "RimWorldAccess.Biotech.XenotypeEditor.LoadedSummaryMany".Loc(xenotype.LabelCap, selectedList.Count, FormatCurrentBiostats()));
        }

        /// <summary>
        /// The dialog's live per-category collapse map. The scope diffs it to follow vanilla-side
        /// collapse changes and writes its own keyboard expand/collapse back into it.
        /// </summary>
        internal static Dictionary<GeneCategoryDef, bool> CurrentCollapsedCategories()
        {
            return dialog != null
                ? (Dictionary<GeneCategoryDef, bool>)XenotypeReflection.CollapsedCategoriesField.GetValue(dialog)
                : null;
        }

        // ===== Icon selector =====

        /// <summary>The dialog's current icon (GeneCreationDialogBase.iconDef) -- read by the Controls-region icon-selector row.</summary>
        public static XenotypeIconDef CurrentIconDef()
        {
            return dialog == null ? null : XenotypeReflection.IconDefField.GetValue(dialog) as XenotypeIconDef;
        }

        /// <summary>
        /// Opens vanilla's own icon-selector dialog, the window DrawIconSelector's ButtonImage opens.
        /// Nothing re-announces when focus returns to the Controls row, so the callback below speaks
        /// the chosen icon itself.
        /// </summary>
        public static void OpenIconSelector()
        {
            if (dialog == null) return;
            Find.WindowStack.Add(new Dialog_SelectXenotypeIcon(CurrentIconDef(), delegate (XenotypeIconDef chosen)
            {
                if (dialog == null || chosen == null) return;
                // MUTATION-C: mirrors GeneCreationDialogBase.DrawIconSelector's own
                // inline delegate (`delegate(XenotypeIconDef i) { iconDef = i; }`) --
                // a bare field write vanilla performs itself with no method to call
                // instead (private inline delegate, no vehicle A/B exists).
                XenotypeReflection.IconDefField.SetValue(dialog, chosen);
                TolkHelper.SpeakData(chosen.label.NullOrEmpty() ? chosen.defName : chosen.LabelCap.ToString());
            }));
        }

        // ===== Rename =====

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
            // MUTATION-C: mirrors GeneCreationDialogBase's name-lock icon button
            // (decompiled GeneCreationDialogBase.cs:201-212, Widgets.ButtonImage
            // flipping xenotypeNameLocked directly); no vehicle A/B exists because
            // that button is inline to DoWindowContents with no separate delegate.
            XenotypeReflection.XenotypeNameLockedField.SetValue(dialog, newLocked);
            // Vanilla plays Checkbox_TurnedOn/Off by the NEW state, not a flat click.
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
            // MUTATION-C: mirrors GeneCreationDialogBase's Randomize button
            // (decompiled GeneCreationDialogBase.cs:141-153, Widgets.ButtonText
            // writing xenotypeName via GeneUtility.GenerateXenotypeNameFromGenes);
            // no vehicle A/B exists because that button is inline to DoWindowContents.
            XenotypeReflection.XenotypeNameField.SetValue(dialog, newName);
            SoundDefOf.Tick_High.PlayOneShotOnCamera();
            TolkHelper.SpeakData($"{((string)"XenotypeName".Translate()).CapitalizeFirst()}: {newName}");
        }

        public static void ToggleInheritable()
        {
            if (dialog == null) return;
            bool inheritable = (bool)XenotypeReflection.InheritableField.GetValue(dialog);
            bool newInheritable = !inheritable;
            // MUTATION-C: mirrors Dialog_CreateXenotype.PostXenotypeOnGUI's inheritable
            // checkbox (decompiled Dialog_CreateXenotype.cs:396-404, Widgets.CheckboxLabeled
            // writing the field by ref); no vehicle A/B exists because that checkbox is
            // inline to PostXenotypeOnGUI with no separate delegate to call.
            XenotypeReflection.InheritableField.SetValue(dialog, newInheritable);
            // CheckboxLabeled plays Checkbox_TurnedOn/Off by the NEW state, not a flat click.
            (newInheritable ? SoundDefOf.Checkbox_TurnedOn : SoundDefOf.Checkbox_TurnedOff).PlayOneShotOnCamera();
            TolkHelper.SpeakData(FormatInheritable());
        }

        /// <summary>
        /// Toggles "ignore restrictions". <paramref name="onChanged"/> fires once the field actually
        /// flips — immediately, or later from the confirmation dialog's Yes callback — so the scope
        /// can rebuild and re-sync; the confirmation path runs asynchronously relative to this call.
        /// </summary>
        // MUTATION-C: mirrors Dialog_CreateXenotype.PostXenotypeOnGUI's ignore-restrictions
        // checkbox handler (decompiled Dialog_CreateXenotype.cs:407-429), INCLUDING reuse of
        // the SAME vanilla ignoreRestrictionsConfirmationSent static by reflection -- toggling
        // via keyboard marks the identical one-time-ever flag vanilla's mouse checkbox would,
        // so the confirmation shows once across both input modes. No vehicle A/B exists because
        // that handler is a private inline checkbox body. Every SetValue below rides THIS same
        // marker (each is one line of the mirrored handler); repeated at each site as
        // "see method header above" so the mutation-doctrine ratchet's own narrow window sees it.
        public static void ToggleIgnoreRestrictions(Action onChanged)
        {
            if (dialog == null) return;
            bool ignoreRestr = (bool)XenotypeReflection.IgnoreRestrictionsField.GetValue(dialog);

            if (!ignoreRestr)
            {
                bool confirmSent = (bool)XenotypeReflection.IgnoreRestrictionsConfirmationSentField.GetValue(null);
                if (!confirmSent)
                {
                    // MUTATION-C: see method header above.
                    XenotypeReflection.IgnoreRestrictionsConfirmationSentField.SetValue(null, true);
                    // Vanilla's checkbox flips and plays Checkbox_TurnedOn on click, BEFORE the
                    // confirmation box opens: its Yes callback is empty (the field is already true)
                    // and No silently reverts with no further sound, both mirrored below.
                    SoundDefOf.Checkbox_TurnedOn.PlayOneShotOnCamera();
                    Find.WindowStack.Add(new Dialog_MessageBox(
                        (string)"IgnoreRestrictionsConfirmation".Translate(),
                        (string)"Yes".Translate(),
                        () =>
                        {
                            // MUTATION-C: see method header above.
                            XenotypeReflection.IgnoreRestrictionsField.SetValue(dialog, true);
                            TolkHelper.Speak("RimWorldAccess.Biotech.XenotypeEditor.NameWithValue".Loc(
                                ((string)"IgnoreRestrictions".Translate()).StripTags(),
                                "RimWorldAccess.Biotech.XenotypeEditor.YesValue".Translate()));
                            onChanged?.Invoke();
                        },
                        (string)"No".Translate(),
                        () =>
                        {
                            TolkHelper.Speak("RimWorldAccess.Biotech.XenotypeEditor.NameWithValue".Loc(
                                ((string)"IgnoreRestrictions".Translate()).StripTags(),
                                "RimWorldAccess.Biotech.XenotypeEditor.NoValue".Translate()));
                        }));
                    return;
                }

                // MUTATION-C: see method header above.
                XenotypeReflection.IgnoreRestrictionsField.SetValue(dialog, true);
                // Checkbox_TurnedOn for the new state, not a flat click.
                SoundDefOf.Checkbox_TurnedOn.PlayOneShotOnCamera();
                TolkHelper.Speak("RimWorldAccess.Biotech.XenotypeEditor.NameWithValue".Loc(
                    ((string)"IgnoreRestrictions".Translate()).StripTags(),
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
                // Checkbox_TurnedOff for the new state, not a flat click.
                SoundDefOf.Checkbox_TurnedOff.PlayOneShotOnCamera();
                string msg = "RimWorldAccess.Biotech.XenotypeEditor.NameWithValue".Translate(
                    ((string)"IgnoreRestrictions".Translate()).StripTags(),
                    "RimWorldAccess.Biotech.XenotypeEditor.NoValue".Translate());
                if (removed > 0)
                    msg += (removed == 1
                        ? "RimWorldAccess.Biotech.XenotypeEditor.AchiteRemovedOne".Translate()
                        : "RimWorldAccess.Biotech.XenotypeEditor.AchiteRemovedMany".Translate(removed));
                TolkHelper.SpeakData(msg);
                onChanged?.Invoke();
            }
        }

        // ===== Announcements/formatting =====

        public static string ComposeOpeningPreamble()
        {
            string header = ((string)"CreateXenotype".Translate()).CapitalizeFirst().StripTags();
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

            var leftChosenGroups = XenotypeReflection.LeftChosenGroupsField.GetValue(dialog) as System.Collections.IList;
            if (leftChosenGroups != null && leftChosenGroups.Count > 0)
            {
                sb.Append($". {((string)"GenesConflict".Translate()).StripTags()}");
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
            if (locked)
                return ((string)"LockNameOn".Translate()).StripTags();
            else
                return ((string)"LockNameOff".Translate()).StripTags();
        }

        public static string FormatNameLockTooltip()
        {
            return ((string)"LockNameButtonDesc".Translate()).StripTags();
        }

        public static bool IsInheritable()
        {
            return dialog != null && (bool)XenotypeReflection.InheritableField.GetValue(dialog);
        }

        public static string FormatInheritable()
        {
            if (dialog == null) return "";
            string label = ((string)"GenesAreInheritable".Translate()).StripTags();
            string value = (IsInheritable()
                ? "RimWorldAccess.Biotech.XenotypeEditor.YesValue"
                : "RimWorldAccess.Biotech.XenotypeEditor.NoValue").Translate();
            return "RimWorldAccess.Biotech.XenotypeEditor.NameWithValue".Translate(label, value);
        }

        public static bool IsIgnoringRestrictions()
        {
            return dialog != null && (bool)XenotypeReflection.IgnoreRestrictionsField.GetValue(dialog);
        }

        // ===== Close =====

        internal static void CloseDialog()
        {
            if (dialog != null)
            {
                dialog.Close();
            }
            Close();
            TolkHelper.Speak("Close".Loc());
        }

        // ===== Tree building (pure; the scope owns the TreeModel instances) =====

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

        // ===== Reflection Accessors =====

        public static List<GeneDef> GetSelectedGenes()
        {
            return dialog != null ? (List<GeneDef>)fi_selectedGenes.GetValue(dialog) : null;
        }
    }
}
