using System.Collections.Generic;
using RimWorld;
using Verse;
using Verse.Sound;

namespace RimWorldAccess.Shell
{
    /// <summary>
    /// ONE scope class over a shared adapter (<see cref="ICharEditorBrowserAdapter"/>) for the
    /// Character Editor mod's filtered browser-style dialogs -- the
    /// <see cref="RimWorldAccess.ITransferLoadDialog"/> pattern applied to
    /// <c>DialogAddTrait</c>/<c>DialogChangeBackstory</c>, registered per exact dialog type in
    /// <see cref="RimWorldAccess.CharEditorDialogCompat.RegisterDialogScopes"/> with the matching
    /// adapter constructed by each factory.
    ///
    /// TWO OR THREE CONTENT REGIONS.
    /// Filters: one ComboBox or Checkbox row per <see cref="ICharEditorBrowserAdapter.Filters"/>
    /// descriptor. Enter/Space open a ComboBox filter's float-menu picker or flip a Checkbox filter;
    /// Left/Right adjust neither. A pick re-queries the adapter's result list, which is ALWAYS a live
    /// read into the mod's own filtered-list field, never cached here.
    /// Results: RadioButton rows whose Selected mirrors the dialog's own selection. Enter writes the
    /// adapter's deferred selection and does NOT close the window.
    /// Parameters: present only when <see cref="ICharEditorBrowserAdapter.ParameterCount"/> is
    /// nonzero. Heterogeneous adapter-built descriptions; Enter opens a float-menu or exact-entry
    /// session the adapter owns, which calls back once the DEFERRED change lands so the row
    /// re-announces itself.
    /// The Buttons region carries OK (riding <c>Window.OnAcceptKeyPressed</c>), Cancel, and the
    /// adapter's own extra actions. <see cref="CaptureWindowButtons"/> is false because every dialog
    /// draws its filter dropdowns as real <c>Widgets.ButtonText</c> calls, which blanket capture would
    /// scrape in alongside the real OK button.
    ///
    /// Escape rides the base <see cref="ScreenScope.OwnsCancel"/> default: both dialogs set
    /// <c>closeOnCancel = true</c>, so vanilla closes the window when no search is running.
    ///
    /// Typeahead searches the Results region by label, deliberately NOT the mod's own
    /// <c>SearchTool</c> buffer, so the mod's list stays exactly as a mouse-only session would see it.
    ///
    /// Enter belongs to this scope: every registered dialog submits inside an
    /// <c>Window.OnAcceptKeyPressed</c> override rather than a raw poll, and
    /// <see cref="WindowStackKeyRouter"/> already gates the sole entry point into that override, so no
    /// dialog-specific Return mask is needed here.
    /// </summary>
    internal sealed class CharEditorBrowserScope : ScreenScope
    {
        private const int FiltersRegion = 0;
        private const int ResultsRegion = 1;
        private const int ParametersRegion = 2;

        private readonly Window dialog;
        private readonly ICharEditorBrowserAdapter adapter;
        private readonly List<ScreenAction> actions = new List<ScreenAction>();

        private bool announcedOpen;

        public CharEditorBrowserScope(Window dialog, ICharEditorBrowserAdapter adapter)
        {
            this.dialog = dialog;
            this.adapter = adapter;
            // First-letter mnemonic on "OK".Translate().
            Claim("charEdBrowser.confirm", e => PerformConfirm());
        }

        public override string Name => "char-editor-browser";

        protected internal override Window OwnedWindow => dialog;

        /// <summary>Trait and backstory names are worth searching.</summary>
        protected override bool EnableTypeahead => true;

        /// <summary>False: the filter dropdowns are real Widgets.ButtonText calls — see the class remarks.</summary>
        protected override bool CaptureWindowButtons => false;

        // ------------------------------------------------------------------
        // Regions.
        // ------------------------------------------------------------------

        /// <summary>The Parameters region appears only for an adapter that declares parameter rows; most declare none.</summary>
        protected override int ContentRegionCount => adapter.ParameterCount > 0 ? 3 : 2;

        protected override string ContentRegionName(int region)
        {
            if (region == FiltersRegion)
                return "RimWorldAccess.CharEd.Browser.FiltersRegion".Translate().ToString();
            if (region == ResultsRegion)
                return "RimWorldAccess.CharEd.Browser.ResultsRegion".Translate().ToString();
            return "RimWorldAccess.CharEd.Browser.ParametersRegion".Translate().ToString();
        }

        protected override int ContentItemCount(int region)
        {
            if (region == FiltersRegion) return adapter.Filters.Count;
            if (region == ResultsRegion) return adapter.ResultCount;
            return adapter.ParameterCount;
        }

        protected override ElementDescription DescribeContentItem(int region, int index)
        {
            if (region == ParametersRegion)
            {
                return adapter.DescribeParameterRow(index);
            }
            var d = new ElementDescription();
            if (region == FiltersRegion)
            {
                DescribeFilterRow(d, index);
            }
            else
            {
                DescribeResultRow(d, index);
            }
            return d;
        }

        private void DescribeFilterRow(ElementDescription d, int index)
        {
            CharEditorBrowserFilter filter = FilterAt(index);
            if (filter == null)
                return;
            d.Label = filter.Label;
            if (filter.IsCheckbox)
            {
                d.Role = ElementRole.Checkbox;
                d.Check = filter.CheckState() ? CheckState.Checked : CheckState.Unchecked;
            }
            else
            {
                d.Role = ElementRole.ComboBox;
                d.Value = filter.ValueLabel();
            }
        }

        private CharEditorBrowserFilter FilterAt(int index)
        {
            IReadOnlyList<CharEditorBrowserFilter> filters = adapter.Filters;
            return index >= 0 && index < filters.Count ? filters[index] : null;
        }

        private void DescribeResultRow(ElementDescription d, int index)
        {
            d.Label = adapter.DescribeResultLabel(index);
            d.Role = ElementRole.RadioButton;
            d.Selected = index == adapter.SelectedResultIndex;
            d.Extras = adapter.DescribeResultTooltip(index);
        }

        // ------------------------------------------------------------------
        // Left/Right adjust a Parameters-region slider or stepper only. No filter row adjusts in
        // place: a ComboBox filter is a dropdown and a Checkbox filter has no arrow-adjust in the mod
        // either, so both go through ActivateFilterRow. Results has nothing to adjust.
        // ------------------------------------------------------------------

        protected override bool CanAdjustContentItem(int region, int index)
        {
            return region == ParametersRegion && adapter.CanAdjustParameterRow(index);
        }

        protected override void AdjustContentItem(int region, int index, int direction)
        {
            if (region != ParametersRegion)
                return;
            adapter.AdjustParameterRow(index, direction);
            RefreshModel();
            TolkHelper.SpeakData(AnnouncementComposer.ComposeStateChange(
                adapter.DescribeParameterRow(index), TranslatedShellVocabulary.Instance));
        }

        /// <summary>
        /// One utterance for the filter's new state plus the fresh result count. Without the count, a
        /// filter narrowing Results to zero falls silent — the region simply drops out of the Tab
        /// cycle. The count is read live right after RefreshModel, so the two facts never drift.
        /// </summary>
        private void AnnounceFilterChange(int index)
        {
            RefreshModel();
            var d = new ElementDescription();
            DescribeFilterRow(d, index);
            string utterance = AnnouncementComposer.ComposeStateChange(d, TranslatedShellVocabulary.Instance);
            TolkHelper.SpeakData(utterance + " " + ResultCountAnnouncement());
        }

        /// <summary>The result-count phrase for the adapter's CURRENT live count.</summary>
        private string ResultCountAnnouncement()
        {
            int count = adapter.ResultCount;
            if (count == 0)
                return "RimWorldAccess.CharEd.Browser.ResultCountNone".Translate().ToString();
            if (count == 1)
                return "RimWorldAccess.CharEd.Browser.ResultCountOne".Translate().ToString();
            return "RimWorldAccess.CharEd.Browser.ResultCountMany".Translate(count).ToString();
        }

        /// <summary>Callback for a DEFERRED parameter change landing on a later frame — see ICharEditorBrowserAdapter.ActivateParameterRow.</summary>
        private void AnnounceParameterChange(int index)
        {
            RefreshModel();
            TolkHelper.SpeakData(AnnouncementComposer.ComposeStateChange(
                adapter.DescribeParameterRow(index), TranslatedShellVocabulary.Instance));
        }

        // ------------------------------------------------------------------
        // Enter: a float-menu picker for a ComboBox filter, a deferred select on a Result row.
        // ------------------------------------------------------------------

        protected override void ActivateContentItem(int region, int index)
        {
            if (region == ParametersRegion)
            {
                adapter.ActivateParameterRow(index, () => AnnounceParameterChange(index));
            }
            else if (region == FiltersRegion)
            {
                ActivateFilterRow(index);
            }
            else
            {
                ActivateResultRow(index);
            }
        }

        private void ActivateFilterRow(int index)
        {
            CharEditorBrowserFilter filter = FilterAt(index);
            if (filter == null)
                return;
            if (filter.IsCheckbox)
            {
                filter.Toggle();
                AnnounceFilterChange(index);
                return;
            }

            List<string> candidates = filter.Candidates();
            var options = new List<FloatMenuOption>();
            for (int i = 0; i < candidates.Count; i++)
            {
                int captured = i;
                options.Add(new FloatMenuOption(candidates[i], delegate
                {
                    filter.SetCandidate(captured);
                    AnnounceFilterChange(index);
                }));
            }
            WindowlessFloatMenuState.OpenTitled(filter.Label, options);
        }

        /// <summary>
        /// Deferred commit: writes the dialog's own selection field and speaks the picked row's OWN
        /// label, since a bare "selected" never names what got picked. The wording follows
        /// <see cref="ICharEditorBrowserAdapter.PickCommitsAdd"/>. Only a deliberate Enter/Space
        /// reaches here, never browsing, so the tick sounds once per pick and the window stays open.
        /// </summary>
        private void ActivateResultRow(int index)
        {
            if (index < 0 || index >= adapter.ResultCount)
                return;
            adapter.SelectResult(index);
            SoundDefOf.Tick_Tiny.PlayOneShotOnCamera();
            RefreshModel();
            string label = adapter.DescribeResultLabel(index);
            string key = adapter.PickCommitsAdd
                ? "RimWorldAccess.CharEd.Browser.PickedAdded"
                : "RimWorldAccess.CharEd.Browser.PickedSelected";
            TolkHelper.SpeakData(key.Translate(label).ToString());
        }

        // ------------------------------------------------------------------
        // Buttons region.
        // ------------------------------------------------------------------

        /// <summary>Shift+Enter presses OK from anywhere in the picker.</summary>
        protected override string DefaultAcceptActionId
        {
            get { return "charEdBrowser.confirm"; }
        }

        protected override IReadOnlyList<ScreenAction> DeclaredActions
        {
            get
            {
                actions.Clear();
                actions.Add(new ScreenAction("OK".Translate(), PerformConfirm, "charEdBrowser.confirm"));
                actions.Add(new ScreenAction("CancelButton".Translate(), PerformCancel, SharedMenuGrammar.Cancel));
                foreach (ScreenAction extra in adapter.ExtraActions)
                {
                    actions.Add(extra);
                }
                return actions;
            }
        }

        /// <summary>
        /// The dialog's own OK path. Silent on success, the closing window being the feedback, unless
        /// nothing was staged to commit, which is named explicitly. The check runs BEFORE Confirm,
        /// since neither the window nor any reflection binding into it survives a successful close.
        /// <see cref="ICharEditorBrowserAdapter.ConfirmRequiresResultPick"/> excuses an adapter whose
        /// Results region is empty by design.
        /// </summary>
        private void PerformConfirm()
        {
            bool nothingStaged = adapter.ConfirmRequiresResultPick
                && (adapter.ResultCount == 0 || adapter.SelectedResultIndex < 0);
            if (!adapter.Confirm())
            {
                SoundDefOf.ClickReject.PlayOneShotOnCamera();
                return;
            }
            if (nothingStaged)
            {
                TolkHelper.SpeakData("RimWorldAccess.CharEd.Browser.ConfirmedEmpty".Translate().ToString());
            }
        }

        private void PerformCancel()
        {
            adapter.Cancel();
        }

        // ------------------------------------------------------------------
        // Lifecycle.
        // ------------------------------------------------------------------

        public override void OnFocus()
        {
            base.OnFocus();
            if (announcedOpen)
                return;
            announcedOpen = true;
            TolkHelper.SpeakData(adapter.Title + ". " + adapter.EntryAnnouncement);
        }

        /// <summary>Cancels any modal text-edit session the adapter owns; adapters with none no-op.</summary>
        public override void OnPop()
        {
            base.OnPop();
            adapter.CancelPendingEdit();
        }
    }
}
