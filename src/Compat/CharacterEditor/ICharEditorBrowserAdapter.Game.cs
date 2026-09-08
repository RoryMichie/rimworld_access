using System;
using System.Collections.Generic;
using System.Linq;
using RimWorld;
using RimWorldAccess.Shell;
using Verse;

namespace RimWorldAccess
{
    /// <summary>
    /// One small adapter per Character Editor browser-style dialog, isolating the handful of
    /// dialog-specific details behind a shared shape <see cref="Shell.CharEditorBrowserScope"/>
    /// drives -- the <see cref="ITransferLoadDialog"/> pattern applied to
    /// <c>CharacterEditor.DialogAddTrait</c>/<c>DialogChangeBackstory</c>. Every shipped adapter
    /// extends <see cref="CharEditorBrowserAdapterBase"/>, which supplies the shared plumbing and
    /// the defaults each member's remarks describe.
    ///
    /// Filters are a flat, adapter-declared list of <see cref="CharEditorBrowserFilter"/>
    /// descriptors mixing two shapes: a ComboBox filter (one value picked from an ordered candidate
    /// list, e.g. mod name) and a Checkbox filter (a bare bool, e.g. "no blocking skills").
    /// Enter/Space drive both -- neither responds to Left/Right. Every filter change re-queries the
    /// MOD's own result list through the descriptor's own delegates (vehicle A) -- the scope never
    /// filters locally.
    ///
    /// Result selection is DEFERRED: <see cref="SelectResult"/> only writes into the dialog's own
    /// selection field (matching the mod's own list-widget ref-binding); nothing closes until
    /// <see cref="Confirm"/> runs.
    /// </summary>
    internal interface ICharEditorBrowserAdapter
    {
        /// <summary>Already-localized dialog title, spoken once on the scope's first focus.</summary>
        string Title { get; }

        /// <summary>
        /// Spoken once on entry right after <see cref="Title"/> -- normally the deferred-commit
        /// notice ("changes apply when you confirm"), but a dialog whose selection applies
        /// IMMEDIATELY (FindPawn) overrides this with its own entry warning instead.
        /// </summary>
        string EntryAnnouncement { get; }

        /// <summary>Filter rows in display order; rebuilt only when the dialog's filter set itself changes (rare -- most adapters return a fixed list built once).</summary>
        IReadOnlyList<CharEditorBrowserFilter> Filters { get; }

        int ResultCount { get; }
        string DescribeResultLabel(int index);

        /// <summary>The vanilla tip string for a result, or null when the dialog carries no tooltip for it.</summary>
        string DescribeResultTooltip(int index);

        /// <summary>Index of the dialog's own current selection within the current result list, or -1 when unmatched.</summary>
        int SelectedResultIndex { get; }

        /// <summary>Writes the dialog's own selection field (deferred commit -- does not close).</summary>
        void SelectResult(int index);

        /// <summary>
        /// True when this adapter's Enter/Space pick on a Results row commits an ADD immediately or
        /// equivalently, so <see cref="Shell.CharEditorBrowserScope.ActivateResultRow"/> speaks
        /// "{0} added." instead of the default "{0} selected." FALSE for every adapter shipped:
        /// each one's <see cref="SelectResult"/> writes only a deferred field, with the actual
        /// grant or replace running from <see cref="Confirm"/>'s own
        /// <c>Window.OnAcceptKeyPressed</c>/<c>DoAndClose</c>, never from the pick itself.
        /// </summary>
        bool PickCommitsAdd { get; }

        /// <summary>
        /// True for every adapter in this file: <see cref="Confirm"/> genuinely has nothing to
        /// commit when the Results region is empty or unselected, so
        /// <see cref="Shell.CharEditorBrowserScope.PerformConfirm"/>'s empty-confirm
        /// announcement applies. Only <see cref="FullhealAdapter"/> overrides this false -- its
        /// Results region is permanently empty by design (the checkbox list lives in Parameters
        /// instead), so the same check would misfire on every successful full heal.
        /// </summary>
        bool ConfirmRequiresResultPick { get; }

        /// <summary>Invokes the dialog's own OK path (vehicle A). Returns true when the window closed.</summary>
        bool Confirm();

        /// <summary>Closes the dialog window without committing (vehicle A: Window.OnCancelKeyPressed).</summary>
        void Cancel();

        /// <summary>
        /// Parameter rows, drawn AFTER Results (adapter-declared; zero for every adapter except
        /// <see cref="ObjectsAdapter"/> -- e.g. quality/stuff/style/stack). Rows are heterogeneous
        /// per adapter (unlike Filters/
        /// Results, which share one shape across every adapter), so the adapter builds the whole
        /// <see cref="ElementDescription"/> itself rather than the scope assembling it from small
        /// per-field getters.
        /// </summary>
        int ParameterCount { get; }

        ElementDescription DescribeParameterRow(int index);

        /// <summary>True for a Slider or Stepper row -- the only shapes Left/Right moves. False for a ComboBox row (Enter opens its picker instead) and for anything presented read-only.</summary>
        bool CanAdjustParameterRow(int index);

        void AdjustParameterRow(int index, int direction);

        /// <summary>
        /// Enter: a ComboBox row opens its own float-menu picker, a Stepper row its own exact-entry
        /// session -- the adapter owns both, matching how Filters region rows already self-drive
        /// their own picker. Both are DEFERRED (the float-menu pick and the exact-entry commit both
        /// land on a LATER frame, not synchronously when this call returns), so the scope passes
        /// <paramref name="onChanged"/> for the adapter to invoke once the change actually lands --
        /// mirroring how Filters region rows call the scope's own AnnounceFilterChange from inside
        /// their FloatMenuOption delegate.
        /// </summary>
        void ActivateParameterRow(int index, Action onChanged);

        /// <summary>Extra action rows beyond Confirm/Cancel (e.g. Randomize, Remove backstory).</summary>
        IReadOnlyList<ScreenAction> ExtraActions { get; }

        /// <summary>
        /// Cancels any modal <see cref="TextFieldEditSession"/> this adapter owns (e.g. a rename
        /// field), called from <see cref="Shell.CharEditorBrowserScope.OnPop"/>. No-op for an
        /// adapter with no such session.
        /// </summary>
        void CancelPendingEdit();
    }
}
