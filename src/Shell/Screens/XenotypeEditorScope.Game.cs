using System.Collections.Generic;
using RimWorld;
using Verse;
using Verse.Sound;

namespace RimWorldAccess.Shell
{
    /// <summary>
    /// The keyboard scope for the real <see cref="Dialog_CreateXenotype"/> window (the custom
    /// xenotype editor), registered through <see cref="ScopeForWindow"/> (Shape-A, unchanged
    /// from the pre-migration <c>FocusScope</c> router this replaces). Same shape as
    /// <see cref="XenogermScope"/> (both dialogs derive from <c>GeneCreationDialogBase</c>) --
    /// see <see cref="GeneDialogScopeBase"/>'s header for the shared design and
    /// <see cref="XenogermScope"/>'s header for the construction-ordering argument
    /// (<c>XenotypeEditorState.Open</c> always completes before this constructor runs).
    ///
    /// One live-scope-stacking note preserved from the pre-migration router: the "ignore
    /// restrictions" toggle can open a real <see cref="Dialog_MessageBox"/> confirmation WHILE
    /// this scope stays on the stack underneath it (its own IsActive/IsLive is not flipped
    /// false first, unlike Xenogerm's Accept-time box) -- safe by the same "a
    /// ScopeForWindow-attached scope always lands on top" argument used everywhere else
    /// (blueprint D4); no extra IsLive gate is needed, and this instance is still valid when
    /// <see cref="XenotypeEditorState.ToggleIgnoreRestrictions"/>'s deferred callback fires.
    /// </summary>
    public sealed class XenotypeEditorScope : GeneDialogScopeBase
    {
        private const int ControlBiostats = 0;
        private const int ControlName = 1;
        private const int ControlNameLock = 2;
        private const int ControlRandomize = 3;
        private const int ControlInheritable = 4;
        private const int ControlIgnoreRestrictions = 5;
        private const int ControlLoadCustom = 6;
        private const int ControlLoadPremade = 7;
        private const int ControlSaveAndApply = 8;
        // GeneCreationDialogBase.DrawIconSelector's ButtonImage opens Dialog_SelectXenotypeIcon;
        // this row is its only keyboard route.
        private const int ControlIcon = 9;
        private const int ControlRowCount = 10;

        public XenotypeEditorScope(Dialog_CreateXenotype dialog)
        {
            Claim("xenotypeEditor.saveAndApply", e => XenotypeEditorState.SaveAndApply());
            Claim("xenotypeEditor.inspect", e => ActivateGeneDialogInfoCard());
            Claim("xenotypeEditor.jumpToNextTopLevel", e => JumpSection(true));
            Claim("xenotypeEditor.jumpToPreviousTopLevel", e => JumpSection(false));
            Claim("xenotypeEditor.firstAbsolute", e => JumpAbsoluteEdge(true));
            Claim("xenotypeEditor.lastAbsolute", e => JumpAbsoluteEdge(false));
            Claim("xenotypeEditor.delete", e => DeleteFocusedGene());

            RebuildTrees();
            RefreshModel();
            if (!XenotypeEditorState.HasSelectedGenes)
            {
                Model.MoveToRegion(LibraryRegionIndex);
            }
            SoundDefOf.TabOpen.PlayOneShotOnCamera();
            TolkHelper.SpeakData(XenotypeEditorState.ComposeOpeningPreamble());
            AnnounceRegion();
        }

        public override string Name
        {
            get { return "xenotype-editor"; }
        }

        public override bool IsLive
        {
            get { return XenotypeEditorState.IsActive; }
        }

        protected override string DefaultAcceptActionId
        {
            get { return "xenotypeEditor.saveAndApply"; }
        }

        /// <summary>Delete on a selected gene removes it — the same toggle Enter runs on that row.</summary>
        private void DeleteFocusedGene()
        {
            RefreshModel();
            InspectionTreeItem item = Model.RegionIndex == SelectedRegionIndex ? CurrentTreeItemOrNull() : null;
            if (item != null && IsToggleTarget(item))
            {
                PerformToggle(item, SelectedRegionIndex);
                RebuildTreesSatisfyingSignal();
                RefreshModel();
                return;
            }
            SoundDefOf.ClickReject.PlayOneShotOnCamera();
        }

        protected override string RegionLabel(int region)
        {
            switch (region)
            {
                case SelectedRegionIndex:
                    return ((string)"SelectedGenes".Translate()).StripTags();
                case LibraryRegionIndex:
                    return ((string)"Genes".Translate()).CapitalizeFirst().StripTags();
                default:
                    return "RimWorldAccess.Shell.GeneDialogs.ControlsRegionName".Translate();
            }
        }

        protected override bool IsToggleTarget(InspectionTreeItem item)
        {
            return item.Data is GeneDef;
        }

        protected override void PerformToggle(InspectionTreeItem item, int regionIndex)
        {
            XenotypeEditorState.ToggleGene((GeneDef)item.Data);
        }

        protected override void RebuildTrees()
        {
            SelectedTree.SetRoot(XenotypeEditorState.BuildSelectedTreeRoot(), WrapItems, SubmenuTreeNavigationSetting);
            LibraryTree.SetRoot(XenotypeEditorState.BuildLibraryTreeRoot(), WrapItems, SubmenuTreeNavigationSetting);
        }

        protected override void HandleDialogClose()
        {
            XenotypeEditorState.CloseDialog();
        }

        protected override bool ShouldAutoExpandForSearch(InspectionTreeItem item)
        {
            return XenotypeEditorState.ShouldAutoExpandForSearch(item);
        }

        /// <summary>Last-seen copy of vanilla's collapsedCategories, for changes-only diffing --
        /// user-driven tree expansion never touches vanilla's map, so an unchanged map means
        /// leave the tree alone.</summary>
        private Dictionary<GeneCategoryDef, bool> lastSeenCollapse;

        protected override void SyncExternalCollapseState()
        {
            Dictionary<GeneCategoryDef, bool> live = XenotypeEditorState.CurrentCollapsedCategories();
            if (live == null)
            {
                return;
            }
            // The base ctor's own RefreshModel reaches this before the subclass ctor body runs,
            // so the snapshot seeds lazily rather than at construction.
            if (lastSeenCollapse == null)
            {
                lastSeenCollapse = new Dictionary<GeneCategoryDef, bool>(live);
                return;
            }
            bool changed = false;
            foreach (KeyValuePair<GeneCategoryDef, bool> kv in live)
            {
                bool last;
                if (!lastSeenCollapse.TryGetValue(kv.Key, out last) || last != kv.Value)
                {
                    changed = true;
                    break;
                }
            }
            if (!changed)
            {
                return;
            }
            InspectionTreeItem anchor = RegionCursorItem(LibraryRegionIndex, LibraryTree);
            LibraryTree.ApplyExternalCategoryCollapse(live, anchor);
            lastSeenCollapse = new Dictionary<GeneCategoryDef, bool>(live);
            SyncRegionAfterRestore(LibraryRegionIndex, LibraryTree);
        }

        /// <summary>
        /// The reverse half of the collapse-parity pair: a keyboard expand or collapse of a
        /// top-level Library category writes vanilla's own map, so the dialog on screen opens and
        /// closes the same categories the reader walks. No sound -- the base already played the
        /// expand/collapse cue, and vanilla's TabOpen/TabClose belongs to the mouse path.
        /// </summary>
        protected override void OnNodeExpansionToggled(int region, InspectionTreeItem node)
        {
            if (region != LibraryRegionIndex || node == null || node.IndentLevel != 0)
            {
                return;
            }
            var category = node.Data as GeneCategoryDef;
            Dictionary<GeneCategoryDef, bool> live = XenotypeEditorState.CurrentCollapsedCategories();
            // Uncategorized-genes headers carry null Data; vanilla's map only holds real defs.
            if (category == null || live == null)
            {
                return;
            }
            bool collapsed = !node.IsExpanded;
            // MUTATION-C: mirrors Dialog_CreateXenotype.DoWindowContents header-click handler
            // (decompiled :241 -- collapsedCategories[displayCategory] flip); no gated vanilla
            // method exists, the click handler is inline IMGUI.
            live[category] = collapsed;
            if (lastSeenCollapse != null)
            {
                // Consume our own write: the changes-only diff above must not read it back as an
                // external change and re-apply it against the cursor law.
                lastSeenCollapse[category] = collapsed;
            }
        }

        // ------------------------------------------------------------------
        // Controls region (9 rows; the legacy 10th row, Close, is dropped --
        // Escape covers it, see GeneDialogScopeBase's OwnsCancel override).
        // ------------------------------------------------------------------

        protected override int ControlsRowCount
        {
            get { return ControlRowCount; }
        }

        protected override ElementDescription DescribeControlsRow(int index)
        {
            switch (index)
            {
                case ControlBiostats:
                    return new ElementDescription
                    {
                        Label = XenotypeEditorState.FormatCurrentBiostats(),
                        ReadOnly = true,
                    };
                case ControlName:
                {
                    var d = new ElementDescription
                    {
                        Label = ((string)"XenotypeName".Translate()).CapitalizeFirst(),
                        Role = ElementRole.TextField,
                    };
                    string name = XenotypeEditorState.CurrentXenotypeName();
                    if (string.IsNullOrEmpty(name)) d.ValueBlank = true; else d.Value = name;
                    return d;
                }
                case ControlNameLock:
                    return new ElementDescription
                    {
                        Label = "RimWorldAccess.Shell.GeneDialogs.NameLockLabel".Translate(),
                        Role = ElementRole.Checkbox,
                        Check = XenotypeEditorState.IsNameLocked() ? CheckState.Checked : CheckState.Unchecked,
                        Extras = XenotypeEditorState.FormatNameLockTooltip(),
                    };
                case ControlRandomize:
                    return new ElementDescription { Label = ((string)"RandomizeName".Translate()).StripTags(), Role = ElementRole.Button };
                case ControlInheritable:
                    return new ElementDescription
                    {
                        Label = ((string)"GenesAreInheritable".Translate()).StripTags(),
                        Role = ElementRole.Checkbox,
                        Check = XenotypeEditorState.IsInheritable() ? CheckState.Checked : CheckState.Unchecked,
                        Extras = ((string)"GenesAreInheritableDesc".Translate()).StripTags(),
                    };
                case ControlIgnoreRestrictions:
                    return new ElementDescription
                    {
                        Label = ((string)"IgnoreRestrictions".Translate()).StripTags(),
                        Role = ElementRole.Checkbox,
                        Check = XenotypeEditorState.IsIgnoringRestrictions() ? CheckState.Checked : CheckState.Unchecked,
                        Extras = ((string)"IgnoreRestrictionsDesc".Translate()).StripTags(),
                    };
                case ControlLoadCustom:
                    return new ElementDescription { Label = ((string)"LoadCustom".Translate()).StripTags(), Role = ElementRole.Button };
                case ControlLoadPremade:
                    return new ElementDescription { Label = ((string)"LoadPremade".Translate()).StripTags(), Role = ElementRole.Button };
                case ControlSaveAndApply:
                    return new ElementDescription { Label = ((string)"SaveAndApply".Translate()).CapitalizeFirst().StripTags(), Role = ElementRole.Button };
                case ControlIcon:
                {
                    XenotypeIconDef icon = XenotypeEditorState.CurrentIconDef();
                    return new ElementDescription
                    {
                        // "SelectIcon" is vanilla's own Dialog_SelectXenotypeIcon header text
                        // ("Select icon") -- reused here since the meaning (opens that same
                        // picker) is identical.
                        Label = ((string)"SelectIcon".Translate()).StripTags(),
                        Role = ElementRole.Button,
                        Value = icon != null ? (icon.label.NullOrEmpty() ? icon.defName : icon.LabelCap.ToString()) : null,
                    };
                }
            }
            return new ElementDescription();
        }

        protected override void ActivateControlsRow(int index)
        {
            switch (index)
            {
                case ControlBiostats:
                    AnnounceCurrentItem();
                    break;
                case ControlName:
                    XenotypeEditorState.BeginRename();
                    break;
                case ControlNameLock:
                    XenotypeEditorState.ToggleNameLock();
                    break;
                case ControlRandomize:
                    XenotypeEditorState.RandomizeName();
                    break;
                case ControlInheritable:
                    XenotypeEditorState.ToggleInheritable();
                    break;
                case ControlIgnoreRestrictions:
                    XenotypeEditorState.ToggleIgnoreRestrictions(OnIgnoreRestrictionsChanged);
                    break;
                case ControlLoadCustom:
                    XenotypeEditorState.LoadCustom();
                    break;
                case ControlLoadPremade:
                    XenotypeEditorState.LoadPremade();
                    break;
                case ControlSaveAndApply:
                    XenotypeEditorState.SaveAndApply();
                    break;
                case ControlIcon:
                    XenotypeEditorState.OpenIconSelector();
                    break;
            }
        }

        /// <summary>
        /// Fires once "ignore restrictions" actually flips (see
        /// <see cref="XenotypeEditorState.ToggleIgnoreRestrictions"/>'s remarks) -- the Library
        /// tree's archite-gene visibility depends on this flag, so it must be rebuilt; no
        /// further announcement here since the mutation method already spoke its own feedback.
        /// </summary>
        private void OnIgnoreRestrictionsChanged()
        {
            if (!IsActiveScope)
            {
                return;
            }
            RebuildTreesSatisfyingSignal();
        }

        /// <summary>True while this scope's dialog is still the live one -- guards the deferred ignore-restrictions callback against a dialog that has since closed.</summary>
        private static bool IsActiveScope
        {
            get { return XenotypeEditorState.IsActive; }
        }
    }
}
