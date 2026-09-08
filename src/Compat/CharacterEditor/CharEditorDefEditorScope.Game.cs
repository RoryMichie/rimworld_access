using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.Sound;

namespace RimWorldAccess.Shell
{
    /// <summary>
    /// The def-editor pane: a windowless <see cref="TreeRegionScope"/>
    /// pushed above <see cref="CharEditorBrowserScope"/> when its "Def editor..." extra action
    /// fires from the Objects or Genery browser (see the adapters' <c>ExtraActions</c>). Backed by
    /// <see cref="CharEditorDefEditorCompat"/>, whose class remarks list exactly which fields are
    /// editable here and which the mod exposes no vehicle for.
    ///
    /// NOT WINDOW-ATTACHED: <c>DialogGenery</c>/<c>DialogObjects</c> draw this pane INSIDE
    /// themselves (behind the shared <c>CEditor.IsExtendedUI</c> toggle, DialogTemplate.cs), so
    /// there is no separate vanilla window for <see cref="ScopeForWindow"/> to key off. This scope
    /// is pushed directly onto <see cref="FocusStack"/> (mirroring the windowless-overlay shape) and
    /// pops ITSELF on Escape -- there is no <c>Window.OnCancelKeyPressed</c> to ride.
    /// <see cref="OnPush"/> sets <c>IsExtendedUI</c> true (so the underlying window actually runs
    /// its DrawParameter pass -- some fields' side effects, e.g. GeneDef's cached-description
    /// clear, only run while that toggle is on); <see cref="OnPop"/> sets it back false, restoring
    /// the pre-open visual state for a sighted spectator.
    ///
    /// TWO CAPTURED DEFS, never both: constructed over either a <see cref="GeneDef"/> or a
    /// <see cref="ThingDef"/>, captured ONCE at push time. The underlying browser's selection cannot
    /// change while this scope holds focus, since input routes here, so a fixed reference for the
    /// session is correct and matches how the mod's own DrawParameter reads its selection.
    ///
    /// Two row shapes cover every field: scalar rows (steppers, def-combo pickers, read-only info)
    /// sit directly under their section, while list-relational sections add one leaf row per current
    /// element — Stepper when the element carries an editable numeric value, plain when it is pure
    /// membership — plus a trailing "Add..." row opening a <see cref="WindowlessFloatMenuState"/>
    /// picker over the mod's own live "free" candidate set, never a set this facade recomputes, so
    /// the offered defs are exactly the mod's own. Remove rides the shared
    /// <c>charEditor.removeTrait</c> action id while the cursor sits on an element row.
    ///
    /// Add and Remove rebuild only the enclosing section's children and land back on the section
    /// header rather than the new or adjacent element; typeahead reaches the exact row in one
    /// keystroke.
    /// </summary>
    internal sealed partial class CharEditorDefEditorScope : TreeRegionScope
    {
        /// <summary>No maxLength on either: a def field's ceiling comes from the def, and these editors mirror the mod's own unbounded numeric boxes.</summary>
        private static readonly TextFieldSpec FloatSpec = new TextFieldSpec(
            labelKey: null, minLength: 0, allowedChars: CharEdNumericEntry.SignedFloat);
        private static readonly TextFieldSpec IntSpec = new TextFieldSpec(
            labelKey: null, minLength: 0, allowedChars: CharEdNumericEntry.SignedDigits);
        /// <summary>
        /// Free-text rows: unrestricted, as the mod's own widgets are, and empty allowed, since
        /// several of these fields are blank by default on many defs.
        /// </summary>
        private static readonly TextFieldSpec FreeTextSpec = new TextFieldSpec(labelKey: null, minLength: 0);

        private enum RowKind { ReadOnly, Bool, IntStepper, FloatStepper, Combo, MembershipElement, ValueElement, AddAction, TextField }

        private sealed class DefRow
        {
            public string Label;
            public RowKind Kind;
            public Func<string> RenderValue;
            public Func<bool> GetBool;
            public Action<int> Adjust;
            public Action Activate;
            public Action RemoveSelf;
        }

        private sealed class DefSection
        {
            public string Label;
            public Func<List<DefRow>> Build;
        }

        private readonly Window dialog;
        private readonly GeneDef gene;
        private readonly ThingDef thing;
        private readonly bool isGene;
        private readonly TextFieldEditSession editSession = new TextFieldEditSession();
        private readonly List<ScreenAction> actions = new List<ScreenAction>();
        private List<DefSection> sections = new List<DefSection>();

        private bool announcedOpen;

        public CharEditorDefEditorScope(Window dialog, GeneDef gene)
        {
            this.dialog = dialog;
            this.gene = gene;
            isGene = true;
            RegisterClaims();
        }

        public CharEditorDefEditorScope(Window dialog, ThingDef thing)
        {
            this.dialog = dialog;
            this.thing = thing;
            isGene = false;
            RegisterClaims();
        }

        /// <summary>Chord ids widened from the existing "charEditor" family; removeTrait already carries the "remove the focused list element" grammar.</summary>
        private void RegisterClaims()
        {
            Claim("charEditor.removeTrait", e => PerformRemoveCurrent(), when: OnRemovableRow);
            Claim(SharedMenuGrammar.Cancel, e => PerformCancel(), when: () => !TypeaheadHasActiveSearch);
            // First-letter mnemonic on "Save modifications".
            Claim("charEdDefEditor.saveModifications", e => PerformSaveModifications());

            RegisterPopTeardown(editSession.CancelIfActive);
        }

        public override string Name => "char-editor-def-editor";

        public override bool OwnsCancel => true;

        protected override bool IncludeActionsRegion => true;

        /// <summary>Shift+Enter presses Save modifications from anywhere on this screen: the one-chord proceed.</summary>
        protected override string DefaultAcceptActionId
        {
            get { return "charEdDefEditor.saveModifications"; }
        }

        /// <summary>
        /// The dialog's own bottom-row def-modification buttons. Save is the ONLY path that persists
        /// modifications into the mod's option strings; without it edits live only until the session
        /// ends, exactly as for a sighted user who skips the button. Reset and Reset all revert via
        /// the dialog's own handlers, then the tree rebuilds so the reverted values are re-read.
        /// </summary>
        protected override IReadOnlyList<ScreenAction> DeclaredActions
        {
            get
            {
                actions.Clear();
                actions.Add(new ScreenAction("RimWorldAccess.CharEd.DefEditor.SaveModifications".Translate(), PerformSaveModifications, "charEdDefEditor.saveModifications"));
                actions.Add(new ScreenAction("RimWorldAccess.CharEd.DefEditor.ResetDef".Translate(), delegate
                {
                    CharEditorDefEditorCompat.ResetDefModification(dialog);
                    RebuildAllSections();
                    TolkHelper.SpeakData("RimWorldAccess.CharEd.DefEditor.DefReset".Translate().ToString());
                }));
                actions.Add(new ScreenAction("RimWorldAccess.CharEd.DefEditor.ResetAllDefs".Translate(), delegate
                {
                    CharEditorDefEditorCompat.ResetAllDefModifications(dialog);
                    RebuildAllSections();
                    TolkHelper.SpeakData("RimWorldAccess.CharEd.DefEditor.AllDefsReset".Translate().ToString());
                }));
                return actions;
            }
        }

        protected override string TreeRegionLabel =>
            isGene
                ? (gene != null ? gene.LabelCap.ToString() : "")
                : (thing != null ? thing.LabelCap.ToString() : "");

        public override void OnPush()
        {
            base.OnPush();
            CharEditorDefEditorCompat.SetExtendedUI(true);
            sections = isGene ? BuildGeneSections() : BuildObjectSections();
            BuildTree();
        }

        public override void OnPop()
        {
            base.OnPop();
            CharEditorDefEditorCompat.SetExtendedUI(false);
        }

        public override void OnFocus()
        {
            base.OnFocus();
            if (announcedOpen) return;
            announcedOpen = true;
            TolkHelper.SpeakData("RimWorldAccess.CharEd.DefEditor.Opened".Translate(TreeRegionLabel).ToString());
            AnnounceCurrentItem();
        }

        // Tree construction.

        /// <summary>Full re-read after a Reset/Reset all: every section's values may have reverted.</summary>
        private void RebuildAllSections()
        {
            sections = isGene ? BuildGeneSections() : BuildObjectSections();
            BuildTree();
            RefreshModel();
        }

        private void BuildTree()
        {
            Tree.SkipRoot = true;
            var root = new InspectionTreeItem { Label = TreeRegionLabel, IsExpandable = true, IndentLevel = 0 };
            foreach (DefSection section in sections)
            {
                var sectionItem = new InspectionTreeItem
                {
                    Label = section.Label,
                    Data = section,
                    IsExpandable = true,
                    IndentLevel = 1,
                    Parent = root,
                };
                root.Children.Add(sectionItem);
            }
            SetTreeRoot(root);
            RefreshModel();
            SyncRegionFromCurrentTree();
        }

        protected override void OnBeforeExpandNode(InspectionTreeItem item)
        {
            if (item.Data is DefSection section && item.Children.Count == 0)
            {
                foreach (DefRow row in section.Build())
                {
                    item.Children.Add(new InspectionTreeItem
                    {
                        Label = row.Label,
                        Data = row,
                        IsExpandable = false,
                        IndentLevel = item.IndentLevel + 1,
                        Parent = item,
                    });
                }
            }
        }

        private void RebuildSection(InspectionTreeItem sectionItem)
        {
            if (!(sectionItem?.Data is DefSection section)) return;
            sectionItem.Children.Clear();
            foreach (DefRow row in section.Build())
            {
                sectionItem.Children.Add(new InspectionTreeItem
                {
                    Label = row.Label,
                    Data = row,
                    IsExpandable = false,
                    IndentLevel = sectionItem.IndentLevel + 1,
                    Parent = sectionItem,
                });
            }
            Tree.Reflatten();
        }

        // Describe.

        protected override ElementDescription DescribeTreeNode(InspectionTreeItem item)
        {
            var d = new ElementDescription();
            if (item.Data is DefRow row)
            {
                DescribeRow(d, row);
                return d;
            }
            d.Label = item.Label;
            if (item.IsExpandable)
            {
                d.Role = ElementRole.TreeItem;
                d.Expanded = item.IsExpanded;
            }
            return d;
        }

        // Enter opens the value-entry editor, so these rows keep Enter (DefaultAcceptInertness).
        private void DescribeRow(ElementDescription d, DefRow row)
        {
            d.Label = row.Label;
            switch (row.Kind)
            {
                case RowKind.ReadOnly:
                    d.ReadOnly = true;
                    d.Value = row.RenderValue?.Invoke();
                    break;
                case RowKind.Bool:
                    d.Role = ElementRole.Checkbox;
                    d.Check = row.GetBool() ? CheckState.Checked : CheckState.Unchecked;
                    break;
                case RowKind.IntStepper:
                case RowKind.FloatStepper:
                    d.Role = ElementRole.Stepper;
                    d.Value = row.RenderValue?.Invoke();
                    d.EntersEditOnAccept = true;
                    break;
                case RowKind.Combo:
                    d.Role = ElementRole.ComboBox;
                    d.Value = row.RenderValue?.Invoke();
                    break;
                case RowKind.TextField:
                    d.Role = ElementRole.TextField;
                    d.Value = row.RenderValue?.Invoke();
                    break;
                case RowKind.ValueElement:
                    d.Role = ElementRole.Stepper;
                    d.Value = row.RenderValue?.Invoke();
                    d.EntersEditOnAccept = true;
                    break;
                case RowKind.MembershipElement:
                    d.Role = ElementRole.None;
                    break;
                case RowKind.AddAction:
                    d.Role = ElementRole.Button;
                    break;
            }
        }

        // Left/Right.

        protected override void AdjustContentItem(int region, int index, int direction)
        {
            InspectionTreeItem item = TreeItemAt(index);
            if (item?.Data is DefRow row && row.Adjust != null)
            {
                // Refresh-and-announce after the write, as ToggleBool does: the delegate is silent,
                // so without this a stepper adjusts the live def inaudibly.
                row.Adjust(direction);
                RefreshModel();
                AnnounceCurrentItem();
                return;
            }
            base.AdjustContentItem(region, index, direction);
        }

        private InspectionTreeItem TreeItemAt(int index)
        {
            int treeIndex = index - PrefixRowCount;
            if (treeIndex < 0 || treeIndex >= Tree.Count) return null;
            return Tree.Visible[treeIndex];
        }

        // Enter.

        protected override void ActivateTreeNode(InspectionTreeItem item)
        {
            if (item?.Data is DefRow row)
            {
                switch (row.Kind)
                {
                    case RowKind.Bool:
                        ToggleBool(item, row);
                        return;
                    case RowKind.IntStepper:
                    case RowKind.FloatStepper:
                    case RowKind.ValueElement:
                    case RowKind.Combo:
                    case RowKind.AddAction:
                    case RowKind.TextField:
                        row.Activate?.Invoke();
                        return;
                    default:
                        ReannounceRow(item);
                        return;
                }
            }
            if (item != null && item.IsExpandable)
            {
                PerformExpandToggle(item);
            }
        }

        private void PerformExpandToggle(InspectionTreeItem item)
        {
            if (!item.IsExpanded) OnBeforeExpandNode(item);
            item.IsExpanded = !item.IsExpanded;
            Tree.Reflatten();
            (item.IsExpanded ? SoundDefOf.FloatMenu_Open : SoundDefOf.FloatMenu_Cancel).PlayOneShotOnCamera();
            RefreshModel();
            SyncRegionFromCurrentTree();
            AnnounceCurrentItem();
        }

        private void ToggleBool(InspectionTreeItem item, DefRow row)
        {
            row.Activate?.Invoke();
            RefreshModel();
            AnnounceCurrentItem();
        }

        private void ReannounceRow(InspectionTreeItem item)
        {
            RefreshModel();
            AnnounceCurrentItem();
        }

        private void PerformSaveModifications()
        {
            CharEditorDefEditorCompat.SaveDefModification(dialog);
            TolkHelper.SpeakData("RimWorldAccess.CharEd.DefEditor.ModificationsSaved".Translate().ToString());
        }

        // Remove, on the widened charEditor.removeTrait id.

        private bool OnRemovableRow()
        {
            InspectionTreeItem item = CurrentTreeItem();
            return item?.Data is DefRow row && row.RemoveSelf != null;
        }

        private void PerformRemoveCurrent()
        {
            InspectionTreeItem item = CurrentTreeItem();
            if (!(item?.Data is DefRow row) || row.RemoveSelf == null) return;
            InspectionTreeItem sectionItem = item.Parent;
            row.RemoveSelf();
            SoundDefOf.Tick_Tiny.PlayOneShotOnCamera();
            RebuildSection(sectionItem);
            TryRevealAndSelect(sectionItem);
            RefreshModel();
            SyncRegionFromCurrentTree();
            TolkHelper.SpeakData("RimWorldAccess.CharEd.Removed".Translate().ToString());
            AnnounceCurrentItem();
        }

        // Escape.

        private void PerformCancel()
        {
            ShellFrameStamps.MarkCancelConsumed();
            SoundDefOf.CancelMode.PlayOneShotOnCamera();
            FocusStack.Pop(this);
        }

        // Shared editing helpers.

        private void BeginExactEntry(string label, string current, TextFieldSpec spec, Action<string> apply)
        {
            editSession.EnterEdit(current, spec, label, apply, onExit: RefreshAndAnnounce);
        }

        /// <summary>The one exit voice every row edit here shares: the row re-read from the refreshed model.</summary>
        private void RefreshAndAnnounce()
        {
            RefreshModel();
            AnnounceCurrentItem();
        }

        private static float StepFor(float min, float max)
        {
            float span = max - min;
            if (float.IsInfinity(span) || span > 200f) return 1f;
            if (span > 20f) return 0.5f;
            return 0.1f;
        }

        private static string F2(float v) => v.ToString("0.##", CultureInfo.InvariantCulture);

        // Generic list-row builders.

        /// <summary>A pure-membership list with no per-element value.</summary>
        private List<DefRow> BuildMembershipRows<T>(
            IEnumerable<T> current, Func<HashSet<T>> freeSource,
            Func<T, string> labelFn, Action<T> onAdd, Action<T> onRemove, string addLabel)
        {
            var rows = new List<DefRow>();
            if (current != null)
            {
                foreach (T element in current.ToList())
                {
                    T captured = element;
                    rows.Add(new DefRow
                    {
                        Label = labelFn(captured),
                        Kind = RowKind.MembershipElement,
                        RemoveSelf = () => onRemove(captured),
                    });
                }
            }
            rows.Add(new DefRow
            {
                Label = addLabel,
                Kind = RowKind.AddAction,
                Activate = () => OpenAddMenu(freeSource(), labelFn, onAdd, addLabel),
            });
            return rows;
        }

        private void OpenAddMenu<T>(HashSet<T> candidates, Func<T, string> labelFn, Action<T> onAdd, string title)
        {
            List<T> list = candidates != null ? candidates.ToList() : new List<T>();
            var options = new List<FloatMenuOption>();
            foreach (T c in list)
            {
                T captured = c;
                options.Add(new FloatMenuOption(labelFn(c), delegate
                {
                    onAdd(captured);
                    InspectionTreeItem item = CurrentTreeItem();
                    InspectionTreeItem sectionItem = item != null && item.Data is DefRow ? item.Parent : item;
                    RebuildSection(sectionItem);
                    RefreshModel();
                    SyncRegionFromCurrentTree();
                    AnnounceCurrentItem();
                }));
            }
            WindowlessFloatMenuState.OpenTitled(title, options, "RimWorldAccess.CharEd.DefEditor.NothingToAdd");
        }

        // GeneDef sections.

        private List<DefSection> BuildGeneSections()
        {
            var list = new List<DefSection>();
            if (gene == null) return list;

            list.Add(new DefSection { Label = "RimWorldAccess.CharEd.DefEditor.InfoSection".Translate(), Build = BuildGeneInfoRows });
            list.Add(new DefSection { Label = "RimWorldAccess.CharEd.DefEditor.BiostatsSection".Translate(), Build = BuildGeneBiostatRows });
            list.Add(new DefSection { Label = "RimWorldAccess.CharEd.Appearance".Translate(), Build = BuildGeneAppearanceRows });
            if (CharEditorDefEditorCompat.GeneIsBodySizeGene(gene))
            {
                list.Add(new DefSection { Label = "RimWorldAccess.CharEd.DefEditor.BodySizeSection".Translate(), Build = BuildGeneLifeStageRows });
            }
            list.Add(new DefSection { Label = "RimWorldAccess.CharEd.DefEditor.ChemicalHairSection".Translate(), Build = BuildGeneChemicalHairRows });
            list.Add(new DefSection { Label = "RimWorldAccess.CharEd.DefEditor.PassionSection".Translate(), Build = BuildGenePassionRows });

            list.Add(new DefSection { Label = CharEditorDefEditorCompat.Label("STAT_FACTORS"), Build = BuildGeneStatFactorRows });
            list.Add(new DefSection { Label = CharEditorDefEditorCompat.Label("STAT_OFFSETS"), Build = BuildGeneStatOffsetRows });
            list.Add(new DefSection { Label = CharEditorDefEditorCompat.Label("APTITUDE"), Build = BuildGeneAptitudeRows });
            list.Add(new DefSection { Label = CharEditorDefEditorCompat.Label("CAPACITIES"), Build = BuildGeneCapacityRows });
            list.Add(new DefSection { Label = CharEditorDefEditorCompat.Label("ABILITIES"), Build = BuildGeneAbilityRows });
            list.Add(new DefSection { Label = CharEditorDefEditorCompat.Label("FORCEDTRAITS"), Build = BuildGeneForcedTraitRows });
            list.Add(new DefSection { Label = CharEditorDefEditorCompat.Label("SUPPRESSEDTRAITS"), Build = BuildGeneSuppressedTraitRows });
            list.Add(new DefSection { Label = CharEditorDefEditorCompat.Label("IMMUNETO"), Build = BuildGeneImmunityRows });
            list.Add(new DefSection { Label = CharEditorDefEditorCompat.Label("FULLYPROTECTEDFROM"), Build = BuildGeneProtectionRows });
            list.Add(new DefSection { Label = CharEditorDefEditorCompat.Label("DAMAGEFACTOR"), Build = BuildGeneDamageFactorRows });
            list.Add(new DefSection { Label = CharEditorDefEditorCompat.Label("DISABLEDNEEDS"), Build = BuildGeneDisabledNeedRows });
            list.Add(new DefSection { Label = CharEditorDefEditorCompat.Label("FORCEDHEADTYPES"), Build = BuildGeneForcedHeadTypeRows });
            list.Add(new DefSection { Label = CharEditorDefEditorCompat.Label("DISABLEDWORKTAGS"), Build = BuildGeneDisabledWorkTagRows });

            // Param-vehicle writes, via ApplyGeneParam.
            list.Add(new DefSection { Label = "RimWorldAccess.CharEd.DefEditor.GeneralSection".Translate(), Build = BuildGeneGeneralRows });
            list.Add(new DefSection { Label = "RimWorldAccess.CharEd.DefEditor.ChancesSection".Translate(), Build = BuildGeneChancesRows });
            list.Add(new DefSection { Label = "RimWorldAccess.CharEd.DefEditor.BehaviorSection".Translate(), Build = BuildGeneBehaviorRows });
            list.Add(new DefSection { Label = "RimWorldAccess.CharEd.DefEditor.SoundsSection".Translate(), Build = BuildGeneSoundRows });
            list.Add(new DefSection { Label = CharEditorDefEditorCompat.Label("CUSTOMEFFECTDESCRIPTIONS"), Build = BuildGeneCustomEffectDescriptionRows });
            list.Add(new DefSection { Label = CharEditorDefEditorCompat.Label("EXCLUSIONTAGS"), Build = BuildGeneExclusionTagRows });
            list.Add(new DefSection { Label = CharEditorDefEditorCompat.Label("HAIRTAGS"), Build = BuildGeneHairTagRows });
            list.Add(new DefSection { Label = CharEditorDefEditorCompat.Label("BEARDTAGS"), Build = BuildGeneBeardTagRows });
            list.Add(new DefSection { Label = CharEditorDefEditorCompat.Label("RESOURCEGIZMOTHRESHOLD"), Build = BuildGeneGizmoThresholdRows });
            return list;
        }

        private List<DefRow> BuildGeneInfoRows()
        {
            return new List<DefRow>
            {
                new DefRow { Label = "RimWorldAccess.CharEd.DefEditor.DefName".Translate(), Kind = RowKind.ReadOnly, RenderValue = () => gene.defName },
                new DefRow { Label = "RimWorldAccess.CharEd.DefEditor.GeneClass".Translate(), Kind = RowKind.ReadOnly, RenderValue = () => gene.geneClass != null ? gene.geneClass.ToString() : "" },
                new DefRow { Label = "RimWorldAccess.CharEd.DefEditor.ResourceGizmoType".Translate(), Kind = RowKind.ReadOnly, RenderValue = () => gene.resourceGizmoType != null ? gene.resourceGizmoType.ToString() : "" },
            };
        }

    }
}
