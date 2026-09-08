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
    /// Data/mutation facade for Dialog_CreateXenogerm (gene processor). A pure facade:
    /// navigation, tab/tree state, and announcement composition all live on
    /// <see cref="RimWorldAccess.Shell.XenogermScope"/> (a
    /// <see cref="RimWorldAccess.Shell.GeneDialogScopeBase"/> subclass). This class keeps:
    /// lifecycle (<see cref="Open"/>/<see cref="Close"/>/<see cref="IsActive"/>), the
    /// reflection caches Dialog_CreateXenogerm-concrete-type members need, every mutation
    /// method (toggle/rename/save/load/StartCombining), and the pure formatting helpers the
    /// scope's Controls-region rows read.
    /// </summary>
    public static class XenogermState
    {
        // ===== Global State =====
        private static bool isActive;
        public static bool IsActive => isActive;
        private static readonly TextInputController renameController = new TextInputController();
        private static readonly TextFieldSpec renameSpec =
            RimWorldDialogIntrospector.ForGeneCreationDialog("RimWorldAccess.TextInput.LabelXenogerm");
        public static bool IsRenaming => TextInputManager.Active == renameController;

        private static Window dialog;

        // ===== Reflection Cache =====
        // The members declared on GeneCreationDialogBase, shared with Dialog_CreateXenotype, are
        // resolved once through XenotypeReflection rather than a second time here.
        private static FieldInfo fi_selectedGenepacks;
        private static FieldInfo fi_libraryGenepacks;
        private static FieldInfo fi_unpoweredGenepacks;
        private static FieldInfo fi_geneAssembler;
        private static MethodInfo mi_accept;
        private static MethodInfo mi_canAccept;
        private static PropertyInfo pi_selectedGenes;

        static XenogermState()
        {
            fi_selectedGenepacks = AccessTools.Field(typeof(Dialog_CreateXenogerm), "selectedGenepacks");
            fi_libraryGenepacks = AccessTools.Field(typeof(Dialog_CreateXenogerm), "libraryGenepacks");
            fi_unpoweredGenepacks = AccessTools.Field(typeof(Dialog_CreateXenogerm), "unpoweredGenepacks");
            fi_geneAssembler = AccessTools.Field(typeof(Dialog_CreateXenogerm), "geneAssembler");
            mi_accept = AccessTools.Method(typeof(Dialog_CreateXenogerm), "Accept");
            mi_canAccept = AccessTools.Method(typeof(Dialog_CreateXenogerm), "CanAccept");
            pi_selectedGenes = AccessTools.Property(typeof(Dialog_CreateXenogerm), "SelectedGenes");
        }

        // ===== Lifecycle =====

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
                Log.Error($"[RimWorld Access] Error in XenogermState.Open: {ex}");
                Close();
            }
        }

        public static void Close()
        {
            isActive = false;
            if (TextInputManager.Active == renameController) TextInputManager.Clear();
            dialog = null;
        }

        /// <summary>Whether anything is currently selected — the scope uses this to decide whether to open on Selected or Library.</summary>
        public static bool HasSelectedGenepacks
        {
            get
            {
                var selected = GetSelectedGenepacks();
                return selected != null && selected.Count > 0;
            }
        }

        // ===== Tree building (pure; the scope owns the TreeModel instances) =====

        public static InspectionTreeItem BuildSelectedGenepackTreeRoot()
        {
            return BuildGenepackTree(GetSelectedGenepacks(), GetUnpoweredGenepacks());
        }

        public static InspectionTreeItem BuildLibraryGenepackTreeRoot()
        {
            var selectedList = GetSelectedGenepacks();
            var libraryList = GetLibraryGenepacks();
            var availableLibrary = new List<Genepack>();
            if (libraryList != null)
            {
                foreach (var pack in libraryList)
                {
                    if (selectedList == null || !selectedList.Contains(pack))
                        availableLibrary.Add(pack);
                }
            }
            return BuildGenepackTree(availableLibrary, GetUnpoweredGenepacks());
        }

        private static InspectionTreeItem BuildGenepackTree(List<Genepack> packs, List<Genepack> unpowered)
        {
            var root = new InspectionTreeItem
            {
                Type = InspectionTreeItem.ItemType.Object,
                Label = "Root",
                IsExpandable = true,
                IsExpanded = true,
                IndentLevel = -1
            };

            if (packs == null || packs.Count == 0)
                return root;

            foreach (var pack in packs)
            {
                if (pack?.GeneSet == null || pack.GeneSet.GenesListForReading.NullOrEmpty())
                    continue;

                bool isUnpowered = unpowered != null && unpowered.Contains(pack);
                string label = FormatGenepackLabel(pack, isUnpowered);

                var packNode = new InspectionTreeItem
                {
                    Type = InspectionTreeItem.ItemType.Item,
                    Label = label,
                    Data = pack,
                    IsExpandable = true,
                    IsExpanded = false,
                    IndentLevel = 0
                };

                packNode.OnActivate = () => BuildGenepackChildren(packNode, pack);

                GeneTreeBuilder.AddChild(root, packNode);
            }

            return root;
        }

        private static void BuildGenepackChildren(InspectionTreeItem packNode, Genepack pack)
        {
            if (packNode.Children.Count > 0) return;

            // GeneSet.SortGenes already sorted this list, and DrawGenepack iterates it in that
            // stored order with no further tiebreakers: read it as-is rather than re-sorting.
            var status = GeneConflictReader.StatusFor(dialog as GeneCreationDialogBase, null, selectedSection: true);

            foreach (var gene in pack.GeneSet.GenesListForReading)
            {
                var geneNode = GeneTreeBuilder.CreateGeneNode(gene, packNode.IndentLevel + 1, status: status?.Invoke(gene));
                GeneTreeBuilder.AddChild(packNode, geneNode);
            }
        }

        /// <summary>Whether a tree node's own children should auto-expand for typeahead search — Genepack containers only (a search for a gene name inside a pack should reach it).</summary>
        public static bool ShouldAutoExpandForSearch(InspectionTreeItem item)
        {
            return item.Data is Genepack;
        }

        // ===== Genepack Selection Toggle =====

        // MUTATION-C: mirrors Dialog_CreateXenogerm.DrawSection's genepack click handler
        // (decompiled Dialog_CreateXenogerm.cs, the DrawGenepack branch that adds/removes
        // from selectedGenepacks with Tick_High/Tick_Low then OnGenesChanged); no vehicle A/B
        // exists because that handler is private and inline to the section's IMGUI loop.
        //
        // DEVIATION FROM VANILLA (deliberate, kept): the unpowered-pack
        // rejection below runs on EVERY add, but vanilla's own
        // mouse click on an unpowered pack has NO such gate at all (decompiled
        // Dialog_CreateXenogerm.cs's DrawGenepack only visually dims an unpowered pack and
        // shows a tooltip; its ButtonInvisible click target still fires unconditionally,
        // letting selectedGenepacks.Add succeed). This mod-side restriction predates this
        // migration; preserved as-is rather than "fixed" to match vanilla's permissive mouse
        // behavior, since QA should decide which is correct.
        public static void ToggleGenepack(Genepack genepack, bool adding)
        {
            if (dialog == null) return;

            var selectedList = GetSelectedGenepacks();
            var unpowered = GetUnpoweredGenepacks();

            if (adding)
            {
                if (unpowered != null && unpowered.Contains(genepack))
                {
                    SoundDefOf.ClickReject.PlayOneShotOnCamera();
                    TolkHelper.Speak("GenepackUnusableGenebankUnpowered".Loc());
                    return;
                }

                selectedList.Add(genepack);
                SoundDefOf.Tick_High.PlayOneShotOnCamera();
            }
            else
            {
                selectedList.Remove(genepack);
                SoundDefOf.Tick_Low.PlayOneShotOnCamera();
            }

            bool nameLocked = (bool)XenotypeReflection.XenotypeNameLockedField.GetValue(dialog);
            if (!nameLocked)
            {
                var genes = (List<GeneDef>)pi_selectedGenes.GetValue(dialog);
                string newName = GeneUtility.GenerateXenotypeNameFromGenes(genes);
                XenotypeReflection.XenotypeNameField.SetValue(dialog, newName);
            }

            XenotypeReflection.OnGenesChangedMethod.Invoke(dialog, null);

            // The scope rebuilds the trees and restores the cursor once this returns.
            string packLabel = genepack.LabelNoCount;
            string biostats = FormatCurrentBiostats();
            TolkHelper.Speak(adding
                ? "RimWorldAccess.Biotech.Xenogerm.PackAdded".Loc(packLabel, biostats)
                : "RimWorldAccess.Biotech.Xenogerm.PackRemoved".Loc(packLabel, biostats));
        }

        // ===== Start Combining =====

        // Vehicle B (CanAccept/Accept pairing kept in this file per
        // scripts/check_mutation_doctrine.py's CanAccept-pairing check).
        public static void StartCombining()
        {
            if (dialog == null) return;

            // The dialog's own CanAccept is the gate the vanilla Accept button runs, including the
            // colony's archite-capsule cost. On failure it shows the vanilla rejection Message, which
            // the message pipeline announces, and the editor stays open.
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

            // Deactivate before Accept, which may show a confirmation dialog or close the window;
            // any dialog that appears is a real window driven by its own scope.
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

        // ===== Save / Load Templates =====

        public static void SaveTemplate()
        {
            if (dialog == null) return;

            string name = (string)XenotypeReflection.XenotypeNameField.GetValue(dialog);
            XenotypeIconDef icon = (XenotypeIconDef)XenotypeReflection.IconDefField.GetValue(dialog);
            List<Genepack> selected = GetSelectedGenepacks();

            AcceptanceReport result = CustomXenogermUtility.SaveXenogermTemplate(name, icon, selected);
            if (result.Accepted)
            {
                SoundDefOf.Tick_High.PlayOneShotOnCamera();
                TolkHelper.Speak("XenogermTemplateSaved".Loc(name.Named("NAME")));
            }
            else
            {
                SoundDefOf.ClickReject.PlayOneShotOnCamera();
                TolkHelper.SpeakData(result.Reason.StripTags());
            }
        }

        public static void LoadTemplate()
        {
            if (dialog == null) return;

            var templates = Find.CustomXenogermDatabase.CustomXenogermsForReading;
            if (templates == null || templates.Count == 0)
            {
                SoundDefOf.ClickReject.PlayOneShotOnCamera();
                TolkHelper.Speak("RimWorldAccess.Biotech.Xenogerm.NoSavedTemplates".Loc());
                return;
            }

            var options = new List<FloatMenuOption>();
            foreach (var template in templates)
            {
                string label = FormatTemplateLabel(template);
                var capturedTemplate = template;
                options.Add(new FloatMenuOption(label, () => ApplyTemplate(capturedTemplate)));
            }

            WindowlessFloatMenuState.Open(options, false);
        }

        private static string FormatTemplateLabel(CustomXenogerm template)
        {
            var geneNames = new List<string>();
            foreach (var geneSet in template.genesets)
            {
                foreach (var gene in geneSet.GenesListForReading)
                {
                    geneNames.Add(gene.LabelCap.ToString());
                }
            }

            if (geneNames.Count > 0)
                return "RimWorldAccess.Biotech.Xenogerm.TemplateLabel".Translate(
                    template.name, string.Join(", ", geneNames));
            return template.name;
        }

        // MUTATION-C: mirrors Dialog_CreateXenogerm.DrawSearchRect's Dialog_XenogermList_Load
        // callback (decompiled Dialog_CreateXenogerm.cs:292-320), which sets name/lock/icon,
        // replaces selectedGenepacks, and calls OnGenesChanged; no vehicle A/B exists because
        // that callback is a private inline delegate.
        private static void ApplyTemplate(CustomXenogerm template)
        {
            if (dialog == null) return;

            XenotypeReflection.XenotypeNameField.SetValue(dialog, template.name);
            XenotypeReflection.XenotypeNameLockedField.SetValue(dialog, true);
            XenotypeReflection.IconDefField.SetValue(dialog, template.iconDef);

            var libraryList = GetLibraryGenepacks();
            var selectedList = GetSelectedGenepacks();
            var matched = CustomXenogermUtility.GetMatchingGenepacks(template.genesets, libraryList).ToList();

            selectedList.Clear();
            selectedList.AddRange(matched);
            XenotypeReflection.OnGenesChangedMethod.Invoke(dialog, null);

            var missingGenes = new List<string>();
            foreach (var geneSet in template.genesets)
            {
                if (!matched.Any(gp => gp.GeneSet.Matches(geneSet)))
                {
                    foreach (var gene in geneSet.GenesListForReading)
                        missingGenes.Add(gene.LabelCap.ToString());
                }
            }

            var sb = new System.Text.StringBuilder();
            sb.Append("RimWorldAccess.Biotech.Xenogerm.TemplateLoaded".Translate(template.name, matched.Count));
            if (missingGenes.Count > 0)
            {
                sb.Append("RimWorldAccess.Biotech.Xenogerm.TemplateMissingGenes".Translate(string.Join(", ", missingGenes)));
            }
            sb.Append($" {FormatCurrentBiostats()}");

            SoundDefOf.Tick_High.PlayOneShotOnCamera();
            TolkHelper.SpeakData(sb.ToString());
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

        public static void BeginRename()
        {
            if (dialog == null) return;
            string currentName = (string)XenotypeReflection.XenotypeNameField.GetValue(dialog);
            renameController.Begin(currentName ?? string.Empty, renameSpec, OnRenameConfirm, OnRenameCancel, replaceOnType: true);
        }

        private static void OnRenameCancel()
        {
            SoundDefOf.Click.PlayOneShotOnCamera();
            TolkHelper.Speak("RimWorldAccess.Biotech.Xenogerm.RenameCancelled".Loc());
        }

        // MUTATION-C: mirrors GeneCreationDialogBase's vanilla text field's own auto-lock
        // behavior (decompiled GeneCreationDialogBase.cs:126-137, the inline
        // Widgets.TextField(rect7, xenotypeName, 40, ValidSymbolRegex) whose surrounding code
        // locks the name once the player edits it directly) -- no vehicle A/B exists because
        // the vanilla field is a bare inline IMGUI control with no separate delegate to call.
        private static void OnRenameConfirm(string newName)
        {
            if (dialog == null) return;
            XenotypeReflection.XenotypeNameField.SetValue(dialog, newName);
            XenotypeReflection.XenotypeNameLockedField.SetValue(dialog, true);

            SoundDefOf.Tick_High.PlayOneShotOnCamera();
            TolkHelper.Speak("RimWorldAccess.Biotech.Xenogerm.Renamed".Loc(newName));
        }

        // ===== Name lock toggle =====

        public static void ToggleNameLock()
        {
            if (dialog == null) return;
            bool locked = (bool)XenotypeReflection.XenotypeNameLockedField.GetValue(dialog);
            bool newLocked = !locked;
            XenotypeReflection.XenotypeNameLockedField.SetValue(dialog, newLocked);
            // The same shared name-lock button XenotypeEditorState.ToggleNameLock mirrors: vanilla
            // plays Checkbox_TurnedOn/Off by the NEW state, not a flat click.
            (newLocked ? SoundDefOf.Checkbox_TurnedOn : SoundDefOf.Checkbox_TurnedOff).PlayOneShotOnCamera();
            TolkHelper.SpeakData(FormatNameLock());
        }

        public static void RandomizeName()
        {
            if (dialog == null) return;
            var genes = (List<GeneDef>)pi_selectedGenes.GetValue(dialog);
            if (genes == null || genes.Count == 0)
            {
                SoundDefOf.ClickReject.PlayOneShotOnCamera();
                TolkHelper.Speak("SelectAGeneToRandomizeName".Loc());
                return;
            }
            string newName = GeneUtility.GenerateXenotypeNameFromGenes(genes);
            XenotypeReflection.XenotypeNameField.SetValue(dialog, newName);
            SoundDefOf.Tick_High.PlayOneShotOnCamera();
            TolkHelper.SpeakData($"{((string)"XenotypeName".Translate()).CapitalizeFirst()}: {newName}");
        }

        // ===== Announcements/formatting =====

        public static string ComposeOpeningPreamble()
        {
            int maxGCX = (int)XenotypeReflection.MaxGCXField.GetValue(dialog);
            string header = ((string)"AssembleGenes".Translate()).StripTags();
            string complexity = ((string)"Complexity".Translate()).CapitalizeFirst();
            return "RimWorldAccess.Biotech.GeneDialogsMigration.XenogermOpeningPreamble".Translate(header, complexity, maxGCX).ToString();
        }

        public static string FormatGenepackLabel(Genepack pack, bool isUnpowered)
        {
            var sb = new System.Text.StringBuilder();

            var geneNames = pack.GeneSet.GenesListForReading
                .Select(g => g.LabelCap.ToString())
                .ToList();
            sb.Append(string.Join(", ", geneNames));

            int cpx = pack.GeneSet.ComplexityTotal;
            int met = pack.GeneSet.MetabolismTotal;
            int arc = pack.GeneSet.ArchitesTotal;

            string complexityLabel = ((string)"Complexity".Translate()).ToLower();
            string metabolismLabel = ((string)"Metabolism".Translate()).ToLower();

            sb.Append($", {complexityLabel} {cpx}, {metabolismLabel} {met.ToStringWithSign()}");

            if (arc > 0)
            {
                string architesLabel = ((string)"ArchitesRequired".Translate()).ToLower();
                sb.Append($", {architesLabel} {arc}");
            }

            if (isUnpowered)
            {
                sb.Append(", ");
                sb.Append("RimWorldAccess.Biotech.Xenogerm.PackUnpowered".Translate());
            }

            return sb.ToString();
        }

        public static string FormatCurrentBiostats()
        {
            if (dialog == null) return "";

            int gcx = (int)XenotypeReflection.GcxField.GetValue(dialog);
            int met = (int)XenotypeReflection.MetField.GetValue(dialog);
            int arc = (int)XenotypeReflection.ArcField.GetValue(dialog);
            int maxGCX = (int)XenotypeReflection.MaxGCXField.GetValue(dialog);

            string complexityLabel = ((string)"Complexity".Translate()).CapitalizeFirst();
            string metabolismLabel = ((string)"Metabolism".Translate()).CapitalizeFirst();

            var sb = new System.Text.StringBuilder();
            if (maxGCX >= 0)
                sb.Append("RimWorldAccess.Biotech.Xenogerm.BiostatsOfMax".Translate(complexityLabel, gcx, maxGCX));
            else
                sb.Append("RimWorldAccess.Biotech.Xenogerm.BiostatsNoMax".Translate(complexityLabel, gcx));
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
            if (locked)
                return ((string)"LockNameOn".Translate()).StripTags();
            else
                return ((string)"LockNameOff".Translate()).StripTags();
        }

        // ===== Close =====

        public static void CloseDialog()
        {
            if (dialog != null)
            {
                dialog.Close();
            }
            Close();
            TolkHelper.Speak("Close".Loc());
        }

        // ===== Reflection Accessors =====

        public static List<Genepack> GetSelectedGenepacks()
        {
            return dialog != null ? (List<Genepack>)fi_selectedGenepacks.GetValue(dialog) : null;
        }

        public static List<Genepack> GetLibraryGenepacks()
        {
            return dialog != null ? (List<Genepack>)fi_libraryGenepacks.GetValue(dialog) : null;
        }

        public static List<Genepack> GetUnpoweredGenepacks()
        {
            return dialog != null ? (List<Genepack>)fi_unpoweredGenepacks.GetValue(dialog) : null;
        }
    }
}
