using RimWorld;
using Verse;
using Verse.Sound;

namespace RimWorldAccess.Shell
{
    /// <summary>
    /// The keyboard focus scope for the Character Editor mod's <c>DialogXenoType</c> (the custom
    /// xenotype creator, opened from the Character tab's Identity row via
    /// <c>CharEditorCompat.OpenXenotypeEditor</c>), registered through <see cref="ScopeForWindow"/>
    /// in <see cref="CharEditorDialogCompat"/>, gated on
    /// <see cref="RimWorldAccess.CharEditorXenoTypeCompat.Ready"/>. Extends
    /// <see cref="GeneDialogScopeBase"/> -- the SAME shared base <see cref="XenotypeEditorScope"/>
    /// rides for the vanilla <c>Dialog_CreateXenotype</c> -- rather than a new hand-built tree host:
    /// <c>DialogXenoType</c> is a SIBLING subclass of the shared <c>GeneCreationDialogBase</c>, not a
    /// child of <c>Dialog_CreateXenotype</c>, so it needs its OWN <see cref="ScopeForWindow.Register"/>
    /// entry and its OWN data facade (<see cref="RimWorldAccess.CharEditorXenoTypeState"/>), but the
    /// ENTIRE Selected/Library/Controls region shape, typeahead, tree jumps, and Alt+I info card come
    /// from the shared base with zero duplication -- see
    /// <see cref="RimWorldAccess.CharEditorXenoTypeCompat"/>'s class remarks for the full REUSED-vs-
    /// ADDED accounting.
    ///
    /// UNLIKE <see cref="XenotypeEditorScope"/>, this scope's OWN constructor calls
    /// <see cref="RimWorldAccess.CharEditorXenoTypeState.Open"/> directly (no separate Harmony
    /// <c>PostOpen</c> patch exists or is needed): <c>ScopeForWindow</c>'s own factory ALREADY
    /// receives the live window instance at the exact moment it is added to the stack, the same
    /// moment a <c>PostOpen</c> patch would fire, and <c>DialogXenoType</c> is a mod-internal type
    /// with no compile-time <c>Type</c> reference available for an attribute-based Harmony patch
    /// (only reflection-resolved, as <see cref="RimWorldAccess.CharEditorXenoTypeCompat"/> already
    /// does) -- adding a manual-patch file for this would duplicate work the factory already does
    /// for free. <see cref="OnPop"/> mirrors this by calling
    /// <see cref="RimWorldAccess.CharEditorXenoTypeState.Close"/> on every exit path (mouse-driven
    /// Close included, via <see cref="ScreenScope.CaptureWindowButtons"/>'s auto-capture of the real
    /// button, or the window simply closing), unlike <see cref="XenotypeEditorScope"/> which relies on
    /// a parallel vanilla-side Close patch not duplicated for a mod dialog.
    ///
    /// ONE EXTRA CONTROLS ROW beyond <see cref="XenotypeEditorScope"/>'s ten: Save (no-apply) --
    /// <c>DialogXenoType</c> draws a genuine third bottom button vanilla's <c>Dialog_CreateXenotype</c>
    /// does not have (see the facade's class remarks). It gets a plain Controls-region row, not a
    /// separate rebindable chord, matching how every OTHER Controls button here (LoadCustom/
    /// LoadPremade/Icon) is Enter-only; only Save-and-Apply keeps its fast <c>xenotypeEditor.saveAndApply</c>
    /// chord, reused verbatim (not re-registered) below, matching plan point 7's "one shared
    /// pseudo-screen key" instruction for sub-scopes that share a chord family.
    /// </summary>
    internal sealed class CharEditorXenoTypeScope : GeneDialogScopeBase
    {
        private const int ControlBiostats = 0;
        private const int ControlName = 1;
        private const int ControlNameLock = 2;
        private const int ControlRandomize = 3;
        private const int ControlInheritable = 4;
        private const int ControlIgnoreRestrictions = 5;
        private const int ControlLoadCustom = 6;
        private const int ControlLoadPremade = 7;
        private const int ControlSave = 8;
        private const int ControlSaveAndApply = 9;
        private const int ControlIcon = 10;
        private const int ControlRowCount = 11;

        private readonly Window dialog;

        private static string VanillaLabel(string key) => ((string)key.Translate()).StripTags();

        public CharEditorXenoTypeScope(Window dialog)
        {
            this.dialog = dialog;
            CharEditorXenoTypeState.Open(dialog);

            // Reused verbatim from XenotypeEditorScope (plan point 7's shared pseudo-screen key):
            // both scopes edit the same conceptual surface (a custom xenotype's gene selection),
            // so the SAME chords make sense bound under either screen instance.
            Claim("xenotypeEditor.saveAndApply", e => CharEditorXenoTypeState.SaveAndApply());
            Claim("xenotypeEditor.inspect", e => ActivateGeneDialogInfoCard());
            Claim("xenotypeEditor.jumpToNextTopLevel", e => JumpSection(true));
            Claim("xenotypeEditor.jumpToPreviousTopLevel", e => JumpSection(false));
            Claim("xenotypeEditor.firstAbsolute", e => JumpAbsoluteEdge(true));
            Claim("xenotypeEditor.lastAbsolute", e => JumpAbsoluteEdge(false));

            RebuildTrees();
            RefreshModel();
            if (!CharEditorXenoTypeState.HasSelectedGenes)
            {
                Model.MoveToRegion(LibraryRegionIndex);
            }
            SoundDefOf.TabOpen.PlayOneShotOnCamera();
            TolkHelper.SpeakData(CharEditorXenoTypeState.ComposeOpeningPreamble());
            AnnounceRegion();
        }

        public override string Name => "char-editor-xeno-type";

        protected internal override Window OwnedWindow => dialog;

        /// <summary>The dialog draws its own real buttons (LoadCustom/LoadPremade/Save/Save-and-Apply/Close, all Widgets.ButtonText) -- Controls region rows already cover every one, matching every sibling CharEditor scope's own reasoning against blanket capture duplicating them.</summary>
        protected override bool CaptureWindowButtons => false;

        public override void OnPop()
        {
            base.OnPop();
            CharEditorXenoTypeState.Close();
        }

        protected override string RegionLabel(int region)
        {
            switch (region)
            {
                case SelectedRegionIndex:
                    return VanillaLabel("SelectedGenes");
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
            CharEditorXenoTypeState.ToggleGene((GeneDef)item.Data);
        }

        protected override void RebuildTrees()
        {
            SelectedTree.SetRoot(CharEditorXenoTypeState.BuildSelectedTreeRoot(), WrapItems, SubmenuTreeNavigationSetting);
            LibraryTree.SetRoot(CharEditorXenoTypeState.BuildLibraryTreeRoot(), WrapItems, SubmenuTreeNavigationSetting);
        }

        protected override void HandleDialogClose()
        {
            CharEditorXenoTypeState.CloseDialog();
        }

        protected override bool ShouldAutoExpandForSearch(InspectionTreeItem item)
        {
            return CharEditorXenoTypeState.ShouldAutoExpandForSearch(item);
        }

        // ------------------------------------------------------------------
        // Controls region.
        // ------------------------------------------------------------------

        protected override int ControlsRowCount => ControlRowCount;

        protected override ElementDescription DescribeControlsRow(int index)
        {
            switch (index)
            {
                case ControlBiostats:
                    return new ElementDescription
                    {
                        Label = CharEditorXenoTypeState.FormatCurrentBiostats(),
                        ReadOnly = true,
                    };
                case ControlName:
                {
                    var d = new ElementDescription
                    {
                        Label = ((string)"XenotypeName".Translate()).CapitalizeFirst(),
                        Role = ElementRole.TextField,
                    };
                    string name = CharEditorXenoTypeState.CurrentXenotypeName();
                    if (string.IsNullOrEmpty(name)) d.ValueBlank = true; else d.Value = name;
                    return d;
                }
                case ControlNameLock:
                    return new ElementDescription
                    {
                        Label = "RimWorldAccess.Shell.GeneDialogs.NameLockLabel".Translate(),
                        Role = ElementRole.Checkbox,
                        Check = CharEditorXenoTypeState.IsNameLocked() ? CheckState.Checked : CheckState.Unchecked,
                        Extras = CharEditorXenoTypeState.FormatNameLockTooltip(),
                    };
                case ControlRandomize:
                    return new ElementDescription { Label = VanillaLabel("RandomizeName"), Role = ElementRole.Button };
                case ControlInheritable:
                    return new ElementDescription
                    {
                        Label = VanillaLabel("GenesAreInheritable"),
                        Role = ElementRole.Checkbox,
                        Check = CharEditorXenoTypeState.IsInheritable() ? CheckState.Checked : CheckState.Unchecked,
                        Extras = VanillaLabel("GenesAreInheritableDesc"),
                    };
                case ControlIgnoreRestrictions:
                    return new ElementDescription
                    {
                        Label = VanillaLabel("IgnoreRestrictions"),
                        Role = ElementRole.Checkbox,
                        Check = CharEditorXenoTypeState.IsIgnoringRestrictions() ? CheckState.Checked : CheckState.Unchecked,
                        Extras = VanillaLabel("IgnoreRestrictionsDesc"),
                    };
                case ControlLoadCustom:
                    return new ElementDescription { Label = VanillaLabel("LoadCustom"), Role = ElementRole.Button };
                case ControlLoadPremade:
                    return new ElementDescription { Label = VanillaLabel("LoadPremade"), Role = ElementRole.Button };
                case ControlSave:
                    return new ElementDescription { Label = "RimWorldAccess.CharEd.XenoType.Save".Translate(), Role = ElementRole.Button };
                case ControlSaveAndApply:
                    return new ElementDescription { Label = ((string)"SaveAndApply".Translate()).CapitalizeFirst().StripTags(), Role = ElementRole.Button };
                case ControlIcon:
                {
                    XenotypeIconDef icon = CharEditorXenoTypeState.CurrentIconDef();
                    return new ElementDescription
                    {
                        Label = VanillaLabel("SelectIcon"),
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
                    CharEditorXenoTypeState.BeginRename();
                    break;
                case ControlNameLock:
                    CharEditorXenoTypeState.ToggleNameLock();
                    break;
                case ControlRandomize:
                    CharEditorXenoTypeState.RandomizeName();
                    break;
                case ControlInheritable:
                    CharEditorXenoTypeState.ToggleInheritable();
                    break;
                case ControlIgnoreRestrictions:
                    CharEditorXenoTypeState.ToggleIgnoreRestrictions(OnIgnoreRestrictionsChanged);
                    break;
                case ControlLoadCustom:
                    CharEditorXenoTypeState.LoadCustom();
                    break;
                case ControlLoadPremade:
                    CharEditorXenoTypeState.LoadPremade();
                    break;
                case ControlSave:
                    CharEditorXenoTypeState.Save();
                    break;
                case ControlSaveAndApply:
                    CharEditorXenoTypeState.SaveAndApply();
                    break;
                case ControlIcon:
                    CharEditorXenoTypeState.OpenIconSelector();
                    break;
            }
        }

        /// <summary>Fires once "ignore restrictions" actually flips -- the Library tree's archite-gene visibility depends on this flag, so it must be rebuilt.</summary>
        private void OnIgnoreRestrictionsChanged()
        {
            if (!IsActiveScope)
            {
                return;
            }
            RebuildTrees();
        }

        private static bool IsActiveScope => CharEditorXenoTypeState.IsActive;
    }
}
