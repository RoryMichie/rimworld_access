using System.Collections.Generic;
using RimWorld;
using Verse;
using Verse.Sound;

namespace RimWorldAccess.Shell
{
    /// <summary>
    /// The pawn-filter overlay screens: the filter editor, its preset save prompt, its preset
    /// picker, and the picker's delete confirmation. All four are windowless mirror-pushed overlays
    /// over <c>IsActive</c> states; their backing states carry lifecycle, data and mutation vehicles
    /// only. <see cref="RerollScope"/> at the bottom of this file is NOT a screen and stays a bare
    /// <see cref="FocusScope"/>.
    ///
    /// All four are MODAL, on the base <see cref="FocusScope.IsModal"/> default. That matters: the
    /// filter editor is a full row/region screen where Left/Right adjust a criterion, Delete removes
    /// a trait, PageUp/PageDown page between sections, Tab cycles regions and Alt+S saves and
    /// closes, every one of which would fire on the wrong screen if it leaked through while the
    /// player browses the preset list. A modal sibling above masks the whole surface, so the filter
    /// scope's claims need no sibling-state terms at all.
    ///
    /// Every Enter handler stamps AcceptConsumed and every Escape handler stamps CancelConsumed:
    /// the chassis does it for row activation and typeahead Escape, and the explicit Cancel claims
    /// below do it themselves. Those stamps, with PageBottomButtonsStampGuardPatch and the router
    /// twins, are what keeps a consumed key out of the page's deferred DoBottomButtons poll and the
    /// wanderer dialog's own re-tests.
    /// </summary>
    public sealed class PawnFilterScope : ScreenScope
    {
        /// <summary>Sections take no row of their own: the name is spoken as a landing prefix when the cursor crosses into one, and PageUp/PageDown page between them.</summary>
        private readonly SectionPrefixTracker sectionPrefix =
            new SectionPrefixTracker(trackSilentRows: false, speakFirstLanding: true);

        public PawnFilterScope()
        {
            Claim("pawnFilter.saveAndClose", e => PawnFilterState.SaveAndClose());
            Claim(SharedMenuGrammar.Cancel, e =>
            {
                ShellFrameStamps.MarkCancelConsumed();
                PawnFilterState.SaveAndClose();
            }, when: () => !TypeaheadHasActiveSearch);
            Claim("pawnFilter.jumpToPreviousSection", e => JumpToAdjacentSection(-1));
            Claim("pawnFilter.jumpToNextSection", e => JumpToAdjacentSection(1));
            // Shift scales the step, so these keep their own chords rather than riding the
            // chassis's bare Left/Right adjust, which carries no modifier.
            Claim("pawnFilter.decreaseValue", e => AdjustCurrentValue(-1, e.Shift));
            Claim("pawnFilter.increaseValue", e => AdjustCurrentValue(1, e.Shift));
            Claim("pawnFilter.deleteTrait", e => DeleteCurrentTrait());
            Claim("pawnFilter.valueMin", e => JumpCurrentValueToExtreme(isMax: false));
            Claim("pawnFilter.valueMax", e => JumpCurrentValueToExtreme(isMax: true));
        }

        public override string Name
        {
            get { return "pawn-filter"; }
        }

        public override bool IsLive
        {
            get { return true; }
        }

        protected override bool EnableTypeahead
        {
            get { return true; }
        }

        /// <summary>Unconditional: windowless, so nothing else can close this editor on Escape.</summary>
        public override bool OwnsCancel
        {
            get { return true; }
        }

        protected override bool IncludeCapturedExtrasRegion
        {
            get { return false; }
        }

        protected override bool CaptureWindowButtons
        {
            get { return false; }
        }

        public override void OnPush()
        {
            base.OnPush();
            ResetOpenAnnouncement();
            sectionPrefix.Reset();
        }

        public override void OnFocus()
        {
            base.OnFocus();
            // The state asks the cursor home on an open, a Clear all, and a preset load, the last
            // landing here since it completes in the picker's callback while this scope is covered.
            // Nothing else moves the cursor on a refocus: returning from the trait picker must leave
            // the player on the row they opened it from.
            if (!PawnFilterState.ConsumeCursorHome())
            {
                return;
            }
            sectionPrefix.Reset();
            Model.MoveToRegion(0);
            ListModel rows = Model.CurrentRegion;
            if (rows != null)
            {
                rows.MoveFirst();
            }
        }

        protected override string ComposeOpenAnnouncement()
        {
            int count = PawnFilterState.ActiveFilterCount;
            string countPart = count > 0
                ? "RimWorldAccess.PawnFilter.ActiveFiltersCountSuffix".Translate(count).ToString()
                : "";
            return "RimWorldAccess.PawnFilter.Editor".Translate(countPart);
        }

        protected override int ContentRegionCount
        {
            get { return 1; }
        }

        protected override string ContentRegionName(int region)
        {
            return (string)"Filter".Translate();
        }

        protected override int ContentItemCount(int region)
        {
            return PawnFilterState.Rows.Count;
        }

        /// <summary>
        /// Each row's Label is already a fused whole phrase built by the criterion's own Format(),
        /// so the value is never a separate field here. The two boolean toggles carry no
        /// <see cref="ElementDescription.Check"/> for the same reason: their label already speaks
        /// On/Off, and a Check would say it twice in one utterance.
        /// </summary>
        protected override ElementDescription DescribeContentItem(int region, int index)
        {
            IReadOnlyList<FilterMenuItem> rows = PawnFilterState.Rows;
            if (index < 0 || index >= rows.Count)
            {
                return new ElementDescription();
            }
            FilterMenuItem item = rows[index];
            ElementDescription d = new ElementDescription
            {
                Label = item.Label,
                Role = RoleForItem(item),
            };
            if (item.ItemType == FilterItemType.Skill && item.SkillFilter != null)
            {
                d.AtMinimum = item.SkillFilter.MinLevel <= 0;
                d.AtMaximum = item.SkillFilter.MinLevel >= 20;
            }
            return d;
        }

        /// <summary>
        /// Role mapping for a filter row: every criterion-backed row adjusts via Left/Right and is a
        /// Stepper; the two boolean toggles are the only rows Enter flips, so Checkbox; the add-trait
        /// and action rows invoke a one-shot action, so Button; an existing trait entry has no Enter
        /// behavior of its own, only Delete, so MenuItem.
        /// </summary>
        private static ElementRole RoleForItem(FilterMenuItem item)
        {
            switch (item.ItemType)
            {
                case FilterItemType.CountOnlyHighestAttack:
                case FilterItemType.CountOnlyPassionSkills:
                    return ElementRole.Checkbox;
                case FilterItemType.AddRequiredTrait:
                case FilterItemType.AddExcludedTrait:
                case FilterItemType.AddOptionalTrait:
                case FilterItemType.SavePreset:
                case FilterItemType.LoadPreset:
                case FilterItemType.ClearAll:
                    return ElementRole.Button;
                case FilterItemType.TraitEntry:
                    return ElementRole.MenuItem;
                default:
                    return ElementRole.Stepper;
            }
        }

        protected override string AnnouncePrefix(int region, int index)
        {
            IReadOnlyList<string> sections = PawnFilterState.RowSections;
            if (index < 0 || index >= sections.Count)
            {
                return null;
            }
            return sectionPrefix.Cross(sections[index]);
        }

        protected override void ActivateContentItem(int region, int index)
        {
            switch (PawnFilterState.Activate(index))
            {
                case FilterRowOutcome.RowChanged:
                    AnnounceCurrentItem();
                    break;
                case FilterRowOutcome.ListRebuilt:
                    RefreshModel();
                    ListModel rows = Model.CurrentRegion;
                    if (rows != null)
                    {
                        rows.MoveFirst();
                    }
                    sectionPrefix.Reset();
                    AnnounceCurrentItem();
                    break;
            }
        }

        /// <summary>PageUp/PageDown: first row of the adjacent section, clamped at the outer sections with the reject sound.</summary>
        private void JumpToAdjacentSection(int direction)
        {
            int row = CurrentRow();
            if (row < 0)
            {
                return;
            }
            IReadOnlyList<string> sections = PawnFilterState.RowSections;
            int target = SectionNavigation.FindAdjacentSectionStart(
                sections.Count, row, i => sections[i], direction > 0);
            if (target < 0)
            {
                SoundDefOf.ClickReject.PlayOneShotOnCamera();
                return;
            }
            Model.CurrentRegion.MoveTo(target);
            AnnounceCurrentItem();
        }

        private void AdjustCurrentValue(int direction, bool shift)
        {
            int row = CurrentRow();
            if (row >= 0 && PawnFilterState.AdjustValue(row, direction, shift))
            {
                AnnounceCurrentItem();
            }
        }

        private void JumpCurrentValueToExtreme(bool isMax)
        {
            int row = CurrentRow();
            if (row >= 0 && PawnFilterState.JumpToExtreme(row, isMax))
            {
                AnnounceCurrentItem();
            }
        }

        private void DeleteCurrentTrait()
        {
            int row = CurrentRow();
            if (row < 0 || !PawnFilterState.DeleteTrait(row))
            {
                return;
            }
            RefreshModel();
            AnnounceCurrentItem();
        }

        /// <summary>The content row under the cursor, or -1 when the cursor is elsewhere or the list is empty.</summary>
        private int CurrentRow()
        {
            RefreshModel();
            ListModel rows = Model.CurrentRegion;
            if (rows == null || rows.IsEmpty || Model.RegionIndex != 0)
            {
                return -1;
            }
            return rows.Index;
        }
    }

    /// <summary>Preset-name save prompt: a "Save as" name row (row 0) plus one row per existing preset to overwrite.</summary>
    public sealed class FilterPresetSaveScope : ScreenScope
    {
        private readonly List<ScreenAction> actions = new List<ScreenAction>();
        private readonly TextFieldEditSession editSession = new TextFieldEditSession();

        public FilterPresetSaveScope()
        {
            Claim(SharedMenuGrammar.Cancel, e =>
            {
                ShellFrameStamps.MarkCancelConsumed();
                CloseAndCancel();
            }, when: () => !TypeaheadHasActiveSearch);

            RegisterPopTeardown(editSession.CancelIfActive);
        }

        public override string Name
        {
            get { return "filter-preset-save"; }
        }

        public override bool IsLive
        {
            get { return true; }
        }

        protected override bool EnableTypeahead
        {
            get { return true; }
        }

        public override bool OwnsCancel
        {
            get { return true; }
        }

        protected override bool IncludeCapturedExtrasRegion
        {
            get { return false; }
        }

        protected override bool CaptureWindowButtons
        {
            get { return false; }
        }

        public override void OnPush()
        {
            base.OnPush();
            ResetOpenAnnouncement();
            // This scope is a long-lived singleton whose Model persists across open/close cycles,
            // and SetCount only CLAMPS the cursor on refresh, so a cursor left on the toolbar by a
            // previous open would silently stay there. Send it home on every open.
            Model.MoveToRegion(0);
            ListModel rows = Model.CurrentRegion;
            if (rows != null)
            {
                rows.MoveFirst();
            }
        }

        protected override string ComposeOpenAnnouncement()
        {
            return "RimWorldAccess.PawnFilter.PresetSave.OpenInstructions"
                .Translate(PawnFilterPresetSaveState.CurrentName);
        }

        /// <summary>Ticked every frame by <see cref="PawnOverlayScopeMirror"/> while the name edit session is open; this scope has no draw pass of its own.</summary>
        internal void MirrorLive()
        {
            editSession.MirrorLive();
        }

        protected override int ContentRegionCount
        {
            get { return 1; }
        }

        protected override string ContentRegionName(int region)
        {
            return (string)"Save".Translate();
        }

        protected override int ContentItemCount(int region)
        {
            return PawnFilterPresetSaveState.ExistingPresets.Count + 1;
        }

        /// <summary>The name row never joins the jump-to-preset search: typing browses the existing presets, and a new name is typed inside the edit session.</summary>
        protected override bool ContentRowSearchable(int region, int row)
        {
            return row > 0;
        }

        protected override ElementDescription DescribeContentItem(int region, int index)
        {
            if (index == 0)
            {
                return new ElementDescription
                {
                    Label = "RimWorldAccess.PawnFilter.PresetSave.SaveAsRow"
                        .Translate(PawnFilterPresetSaveState.CurrentName),
                    Role = ElementRole.TextField,
                };
            }
            IReadOnlyList<string> presets = PawnFilterPresetSaveState.ExistingPresets;
            int presetIndex = index - 1;
            if (presetIndex < 0 || presetIndex >= presets.Count)
            {
                return new ElementDescription();
            }
            return new ElementDescription
            {
                Label = "RimWorldAccess.PawnFilter.PresetSave.OverwriteRow".Translate(presets[presetIndex]),
                Role = ElementRole.Button,
            };
        }

        protected override void ActivateContentItem(int region, int index)
        {
            if (index == 0)
            {
                BeginEditName();
                return;
            }
            PawnFilterPresetSaveState.SaveToExistingPreset(index - 1);
        }

        private void BeginEditName()
        {
            editSession.EnterEdit(
                PawnFilterPresetSaveState.CurrentName ?? string.Empty,
                PawnFilterPresetSaveState.NameSpec,
                "RimWorldAccess.TextInput.LabelFilename".Loc().ToString(),
                PawnFilterPresetSaveState.SetCurrentName,
                FinishEditName,
                onConfirm: PawnFilterPresetSaveState.SaveNewName);
        }

        private void FinishEditName()
        {
            // The state may already be closed here, Enter's onConfirm having just saved; the mirror
            // pops this scope on its next Reconcile pass.
            if (!PawnFilterPresetSaveState.IsActive)
            {
                return;
            }
            RefreshModel();
            AnnounceCurrentItem();
        }

        protected override IReadOnlyList<ScreenAction> DeclaredActions
        {
            get
            {
                actions.Clear();
                actions.Add(new ScreenAction("Back".Translate(), CloseAndCancel, SharedMenuGrammar.Cancel));
                return actions;
            }
        }

        private void CloseAndCancel()
        {
            editSession.CancelIfActive();
            PawnFilterPresetSaveState.HandleCancel();
        }
    }

    /// <summary>Preset picker: one row per saved pawn filter, Delete opens the confirmation.</summary>
    public sealed class FilterPresetLoadScope : ScreenScope
    {
        private readonly List<ScreenAction> actions = new List<ScreenAction>();

        public FilterPresetLoadScope()
        {
            Claim(SharedMenuGrammar.Cancel, e =>
            {
                ShellFrameStamps.MarkCancelConsumed();
                PawnFilterPresetLoadState.HandleCancel();
            }, when: () => !TypeaheadHasActiveSearch);
            Claim("filterPresetLoad.delete", e => RequestDeleteCurrent());
        }

        public override string Name
        {
            get { return "filter-preset-load"; }
        }

        public override bool IsLive
        {
            get { return true; }
        }

        protected override bool EnableTypeahead
        {
            get { return true; }
        }

        public override bool OwnsCancel
        {
            get { return true; }
        }

        protected override bool IncludeCapturedExtrasRegion
        {
            get { return false; }
        }

        protected override bool CaptureWindowButtons
        {
            get { return false; }
        }

        public override void OnPush()
        {
            base.OnPush();
            ResetOpenAnnouncement();
            // Singleton Model, as in FilterPresetSaveScope's OnPush.
            Model.MoveToRegion(0);
            ListModel rows = Model.CurrentRegion;
            if (rows != null)
            {
                rows.MoveFirst();
            }
        }

        protected override string ComposeOpenAnnouncement()
        {
            return "RimWorldAccess.PawnFilter.PresetLoad.OpenInstructions"
                .Translate(PawnFilterPresetLoadState.PresetNames.Count);
        }

        protected override int ContentRegionCount
        {
            get { return 1; }
        }

        protected override string ContentRegionName(int region)
        {
            return (string)"Load".Translate();
        }

        protected override int ContentItemCount(int region)
        {
            return PawnFilterPresetLoadState.PresetNames.Count;
        }

        protected override ElementDescription DescribeContentItem(int region, int index)
        {
            IReadOnlyList<string> presets = PawnFilterPresetLoadState.PresetNames;
            if (index < 0 || index >= presets.Count)
            {
                return new ElementDescription();
            }
            string name = presets[index];
            if (string.IsNullOrEmpty(name))
            {
                name = "RimWorldAccess.PawnFilter.PresetLoad.UnnamedFallback".Translate();
            }
            return new ElementDescription { Label = name, Role = ElementRole.MenuItem };
        }

        protected override void ActivateContentItem(int region, int index)
        {
            PawnFilterPresetLoadState.LoadSelected(index);
        }

        private void RequestDeleteCurrent()
        {
            RefreshModel();
            ListModel rows = Model.CurrentRegion;
            if (rows == null || rows.IsEmpty || Model.RegionIndex != 0)
            {
                return;
            }
            PawnFilterPresetLoadState.RequestDelete(rows.Index);
        }

        protected override IReadOnlyList<ScreenAction> DeclaredActions
        {
            get
            {
                actions.Clear();
                actions.Add(new ScreenAction("Back".Translate(),
                    PawnFilterPresetLoadState.HandleCancel, SharedMenuGrammar.Cancel));
                return actions;
            }
        }
    }

    /// <summary>
    /// The picker's delete confirmation: two buttons and nothing else, so a Buttons region alone.
    /// The picker's own "Delete {name}?" prompt is spoken before this opens.
    /// </summary>
    public sealed class FilterPresetDeleteConfirmScope : ScreenScope
    {
        private readonly List<ScreenAction> actions = new List<ScreenAction>();

        public FilterPresetDeleteConfirmScope()
        {
            Claim(SharedMenuGrammar.Cancel, e =>
            {
                ShellFrameStamps.MarkCancelConsumed();
                PawnFilterPresetDeleteConfirmState.Cancel();
            });
        }

        public override string Name
        {
            get { return "filter-preset-delete-confirm"; }
        }

        public override bool IsLive
        {
            get { return true; }
        }

        public override bool OwnsCancel
        {
            get { return true; }
        }

        protected override bool IncludeCapturedExtrasRegion
        {
            get { return false; }
        }

        protected override bool CaptureWindowButtons
        {
            get { return false; }
        }

        public override void OnPush()
        {
            base.OnPush();
            // Singleton Model: start on Delete, never on whichever button the previous confirmation
            // was left on.
            Model.MoveToRegion(0);
            ListModel buttons = Model.CurrentRegion;
            if (buttons != null)
            {
                buttons.MoveFirst();
            }
        }

        protected override int ContentRegionCount
        {
            get { return 0; }
        }

        protected override string ContentRegionName(int region)
        {
            return "";
        }

        protected override int ContentItemCount(int region)
        {
            return 0;
        }

        protected override ElementDescription DescribeContentItem(int region, int index)
        {
            return new ElementDescription();
        }

        protected override void ActivateContentItem(int region, int index)
        {
        }

        protected override IReadOnlyList<ScreenAction> DeclaredActions
        {
            get
            {
                actions.Clear();
                actions.Add(new ScreenAction("Delete".Translate(),
                    PawnFilterPresetDeleteConfirmState.Confirm));
                actions.Add(new ScreenAction("Cancel".Translate(),
                    PawnFilterPresetDeleteConfirmState.Cancel, SharedMenuGrammar.Cancel));
                return actions;
            }
        }
    }

    /// <summary>
    /// The filtered-reroll blackout: NOT a screen, and deliberately still a bare
    /// <see cref="FocusScope"/>, recorded in check_scope_chassis.py's exempt list. Escape cancels the
    /// batch; everything else stops at the modal boundary and the dispatcher's native modal swallow
    /// eats the main pass. RerollState stays a NON-member of ShellGuards.MenuOwnsInput, since
    /// FocusStack's AnyLiveModal is already one of that check's OR terms.
    /// </summary>
    public sealed class RerollScope : FocusScope
    {
        public RerollScope()
        {
            Claim(SharedMenuGrammar.Cancel, delegate
            {
                ShellFrameStamps.MarkCancelConsumed();
                RerollState.Cancel();
            });
        }

        public override string Name
        {
            get { return "pawn-reroll"; }
        }

        public override bool IsLive
        {
            get { return true; }
        }
    }

    /// <summary>
    /// Reconciles the pawn-overlay scopes so a later Push lands higher: the filter editor lowest,
    /// then save, load, the delete confirm, and the reroll blackout on top. Runs AFTER the site
    /// mirrors and BEFORE the What's-New tail in the dispatcher; the window-attached
    /// StartingPawnScope anchors beneath automatically.
    ///
    /// The four filter-family gates fold <see cref="PawnScopeGuards.WindowAbovePawnHost"/> — never
    /// re-float a windowless overlay scope above MessageBoxScope or NamePawnScope, and yield to
    /// scopeless vanilla dialogs — which doubles as the stale-flag backstop: host gone, scopes
    /// popped. The REROLL gate is deliberately bare IsActive, the batch being short-lived and
    /// self-finalizing, with the PostClose teardown patches cancelling it if a host closes under it.
    /// </summary>
    internal static class PawnOverlayScopeMirror
    {
        private static readonly PawnFilterScope filter = new PawnFilterScope();
        private static readonly FilterPresetSaveScope save = new FilterPresetSaveScope();
        private static readonly FilterPresetLoadScope load = new FilterPresetLoadScope();
        private static readonly FilterPresetDeleteConfirmScope deleteConfirm = new FilterPresetDeleteConfirmScope();
        private static readonly RerollScope reroll = new RerollScope();

        public static void Reconcile()
        {
            bool standDown = PawnScopeGuards.WindowAbovePawnHost();

            ReconcileOne(filter, PawnFilterState.IsActive && !standDown);
            ReconcileOne(save, PawnFilterPresetSaveState.IsActive && !standDown);
            // The windowless equivalent of FileListScope's draw-pass MirrorLive: keep the save-name
            // backing value equal to the live edit buffer each frame, so Escape returns to browse
            // with the typed name intact.
            if (PawnFilterPresetSaveState.IsActive)
            {
                save.MirrorLive();
            }
            ReconcileOne(load, PawnFilterPresetLoadState.IsActive && !standDown);
            ReconcileOne(deleteConfirm, PawnFilterPresetDeleteConfirmState.IsActive && !standDown);
            ReconcileOne(reroll, RerollState.IsActive);
        }

        private static void ReconcileOne(FocusScope scope, bool live)
        {
            if (live)
            {
                FocusStack.Push(scope);
            }
            else
            {
                FocusStack.Pop(scope);
            }
        }
    }
}
