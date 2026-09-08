using RimWorld;
using Verse;
using Verse.Sound;

namespace RimWorldAccess.Shell
{
    /// <summary>
    /// The keyboard scope for the real <see cref="Dialog_CreateXenogerm"/> window (the
    /// gene-processor combining dialog), registered through <see cref="ScopeForWindow"/>
    /// (Shape-A, unchanged from the pre-migration <c>FocusScope</c> router this replaces).
    ///
    /// Migrated from a bare
    /// <see cref="FocusScope"/> stateless router onto <see cref="GeneDialogScopeBase"/> — see
    /// that class's header for the shared three-region shape and the two flagged
    /// blueprint-vs-reality mismatches (no PageUp/Down base mechanism; TreeRegionScope cannot
    /// host two tree regions).
    ///
    /// Construction ordering this relies on (verified by reading
    /// <c>ScopeForWindow.Game.cs</c> directly): <c>ScopeForWindow.Attach</c> is a POSTFIX on
    /// <c>WindowStack.Add</c>, and <c>WindowStack.Add</c>'s own body calls the window's
    /// <c>PostOpen()</c> (which fires <c>XenogermPatch.PostOpen_Patch</c>, calling
    /// <see cref="XenogermState.Open"/>) BEFORE returning. So by the time THIS constructor
    /// runs, <see cref="XenogermState.IsActive"/> is already true and every reflection read
    /// is valid — the constructor can safely build trees and speak the opening announcement.
    /// </summary>
    public sealed class XenogermScope : GeneDialogScopeBase
    {
        private const int ControlBiostats = 0;
        private const int ControlName = 1;
        private const int ControlNameLock = 2;
        private const int ControlRandomize = 3;
        private const int ControlSaveTemplate = 4;
        private const int ControlLoadTemplate = 5;
        private const int ControlStartCombining = 6;
        // Task 6 slice B reachability fix: GeneCreationDialogBase.DrawIconSelector's
        // ButtonImage opens Dialog_SelectXenotypeIcon, but no Controls row reached it
        // by keyboard until now (verified: no such row existed).
        private const int ControlIcon = 7;
        private const int ControlRowCount = 8;

        public XenogermScope(Dialog_CreateXenogerm dialog)
        {
            Claim("xenogerm.startCombining", e => XenogermState.StartCombining());
            Claim("xenogerm.inspect", e => ActivateGeneDialogInfoCard());
            Claim("xenogerm.jumpToNextGenepack", e => JumpSection(true));
            Claim("xenogerm.jumpToPreviousGenepack", e => JumpSection(false));
            Claim("xenogerm.firstAbsolute", e => JumpAbsoluteEdge(true));
            Claim("xenogerm.lastAbsolute", e => JumpAbsoluteEdge(false));

            RebuildTrees();
            RefreshModel();
            if (!XenogermState.HasSelectedGenepacks)
            {
                Model.MoveToRegion(LibraryRegionIndex);
            }
            SoundDefOf.TabOpen.PlayOneShotOnCamera();
            TolkHelper.SpeakData(XenogermState.ComposeOpeningPreamble());
            AnnounceRegion();
        }

        public override string Name
        {
            get { return "xenogerm"; }
        }

        public override bool IsLive
        {
            get { return XenogermState.IsActive; }
        }

        protected override string RegionLabel(int region)
        {
            switch (region)
            {
                case SelectedRegionIndex:
                    return ((string)"SelectedGenepacks".Translate()).StripTags();
                case LibraryRegionIndex:
                    return ((string)"GenepackLibrary".Translate()).StripTags();
                default:
                    return "RimWorldAccess.Shell.GeneDialogs.ControlsRegionName".Translate();
            }
        }

        protected override bool IsToggleTarget(InspectionTreeItem item)
        {
            return item.IndentLevel == 0 && item.Data is Genepack;
        }

        protected override void PerformToggle(InspectionTreeItem item, int regionIndex)
        {
            var genepack = (Genepack)item.Data;
            XenogermState.ToggleGenepack(genepack, adding: regionIndex == LibraryRegionIndex);
        }

        protected override void RebuildTrees()
        {
            SelectedTree.SetRoot(XenogermState.BuildSelectedGenepackTreeRoot(), WrapItems, SubmenuTreeNavigationSetting);
            LibraryTree.SetRoot(XenogermState.BuildLibraryGenepackTreeRoot(), WrapItems, SubmenuTreeNavigationSetting);
        }

        protected override void HandleDialogClose()
        {
            XenogermState.CloseDialog();
        }

        protected override bool TryOpenInfoCardForNonDefData(object data)
        {
            var pack = data as Genepack;
            if (pack == null)
            {
                return false;
            }
            Find.WindowStack.Add(new Dialog_InfoCard(pack));
            return true;
        }

        protected override bool ShouldAutoExpandForSearch(InspectionTreeItem item)
        {
            return XenogermState.ShouldAutoExpandForSearch(item);
        }

        // ------------------------------------------------------------------
        // Controls region (7 rows; the legacy 8th row, Close, is dropped --
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
                        Label = XenogermState.FormatCurrentBiostats(),
                        ReadOnly = true,
                    };
                case ControlName:
                {
                    var d = new ElementDescription
                    {
                        Label = ((string)"XenotypeName".Translate()).CapitalizeFirst(),
                        Role = ElementRole.TextField,
                    };
                    string name = XenogermState.CurrentXenotypeName();
                    if (string.IsNullOrEmpty(name)) d.ValueBlank = true; else d.Value = name;
                    return d;
                }
                case ControlNameLock:
                    return new ElementDescription
                    {
                        Label = "RimWorldAccess.Shell.GeneDialogs.NameLockLabel".Translate(),
                        Role = ElementRole.Checkbox,
                        Check = XenogermState.IsNameLocked() ? CheckState.Checked : CheckState.Unchecked,
                    };
                case ControlRandomize:
                    return new ElementDescription { Label = ((string)"RandomizeName".Translate()).StripTags(), Role = ElementRole.Button };
                case ControlSaveTemplate:
                    return new ElementDescription { Label = ((string)"SaveXenogermTemplate".Translate()).StripTags(), Role = ElementRole.Button };
                case ControlLoadTemplate:
                    return new ElementDescription { Label = ((string)"LoadXenogermTemplate".Translate()).StripTags(), Role = ElementRole.Button };
                case ControlStartCombining:
                    return new ElementDescription { Label = (string)"StartCombining".Translate(), Role = ElementRole.Button };
                case ControlIcon:
                {
                    XenotypeIconDef icon = XenogermState.CurrentIconDef();
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
                    XenogermState.BeginRename();
                    break;
                case ControlNameLock:
                    XenogermState.ToggleNameLock();
                    break;
                case ControlRandomize:
                    XenogermState.RandomizeName();
                    break;
                case ControlSaveTemplate:
                    XenogermState.SaveTemplate();
                    break;
                case ControlLoadTemplate:
                    XenogermState.LoadTemplate();
                    break;
                case ControlStartCombining:
                    XenogermState.StartCombining();
                    break;
                case ControlIcon:
                    XenogermState.OpenIconSelector();
                    break;
            }
        }
    }
}
