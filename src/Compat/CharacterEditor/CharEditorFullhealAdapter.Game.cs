using System;
using System.Collections.Generic;
using System.Linq;
using RimWorld;
using RimWorldAccess.Shell;
using Verse;

namespace RimWorldAccess
{
    /// <summary>
    /// Adapter for <c>CharacterEditor.DialogFullheal</c>. No filters, no radio Results;
    /// every current hediff is presented as a Parameters-region Checkbox row instead (the shape
    /// the shared browser scope offers for adapter-owned heterogeneous rows -- see
    /// <see cref="Shell.CharEditorBrowserScope"/>'s class remarks), since the Results region's
    /// shared shape is hard-wired to a single-select RadioButton and cannot express a multi-check
    /// list without widening that shared class for one adapter. Flip and All are ExtraActions.
    /// Confirm removes every checked hediff (resurrecting first if needed) via the dialog's own
    /// gated <c>DoAndClose</c> (never <c>OnAcceptKeyPressed</c> -- see
    /// <see cref="CharEditorHealthBrowserCompat"/>'s class remarks).
    /// </summary>
    internal sealed class FullhealAdapter : CharEditorBrowserAdapterBase
    {
        private readonly List<ScreenAction> extraActions;

        public FullhealAdapter(Window dialog)
            : base(dialog)
        {
            extraActions = new List<ScreenAction>
            {
                new ScreenAction("RimWorldAccess.CharEd.Browser.Health.Flip".Translate(), Flip),
                new ScreenAction("RimWorldAccess.CharEd.Browser.Health.CheckAll".Translate(), CheckAll),
            };
        }

        public override string Title => "RimWorldAccess.CharEd.Browser.FullhealTitle".Translate().ToString();

        public override int ResultCount => 0;
        public override string DescribeResultLabel(int index) => "";
        public override string DescribeResultTooltip(int index) => null;
        public override int SelectedResultIndex => -1;
        public override void SelectResult(int index) { }

        /// <summary>False: the Results region is permanently empty by design (this dialog's whole surface is the checkbox list below), and the scope's empty-confirm check would otherwise misannounce "Closed. Nothing was selected." on every successful full heal.</summary>
        public override bool ConfirmRequiresResultPick => false;

        public override int ParameterCount => CharEditorHealthBrowserCompat.FullhealList(dialog).Count;

        public override ElementDescription DescribeParameterRow(int index)
        {
            var d = new ElementDescription();
            var hediffs = CharEditorHealthBrowserCompat.FullhealList(dialog);
            if (index < 0 || index >= hediffs.Count)
                return d;
            Hediff hediff = hediffs[index];
            d.Label = (hediff.Part != null ? hediff.Part.Label + " " + hediff.Label : hediff.Label).CapitalizeFirst();
            d.Role = ElementRole.Checkbox;
            d.Check = CharEditorHealthBrowserCompat.FullhealIsChecked(dialog, hediff) ? CheckState.Checked : CheckState.Unchecked;
            string extra = hediff.TipStringExtra;
            if (!extra.NullOrEmpty())
                d.Extras = extra;
            return d;
        }

        public override void ActivateParameterRow(int index, Action onChanged)
        {
            var hediffs = CharEditorHealthBrowserCompat.FullhealList(dialog);
            if (index < 0 || index >= hediffs.Count)
                return;
            CharEditorHealthBrowserCompat.FullhealToggle(dialog, hediffs[index]);
            onChanged();
        }

        public override bool Confirm()
        {
            CharEditorHealthBrowserCompat.FullhealConfirm(dialog);
            return !Find.WindowStack.IsOpen(dialog);
        }

        public override IReadOnlyList<ScreenAction> ExtraActions => extraActions;

        private void Flip() => CharEditorHealthBrowserCompat.FullhealFlipAll(dialog);

        private void CheckAll() => CharEditorHealthBrowserCompat.FullhealCheckAll(dialog);
    }
}
