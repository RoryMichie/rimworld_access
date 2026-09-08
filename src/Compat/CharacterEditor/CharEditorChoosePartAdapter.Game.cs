using System;
using System.Collections.Generic;
using System.Linq;
using RimWorld;
using RimWorldAccess.Shell;
using Verse;

namespace RimWorldAccess
{
    /// <summary>
    /// Adapter for <c>CharacterEditor.DialogChoosePart</c>, reusing the shared browser scope with
    /// zero filters and zero parameters -- a plain radio
    /// list. Chained from <see cref="AddHediffAdapter"/>'s Confirm when a body part is
    /// required and more than one is legal (a singleton is auto-selected by the mod's own
    /// <c>CheckIsReady</c> BEFORE this dialog ever opens -- verified against the decompile: this
    /// dialog's own singleton auto-select at its constructor is consequently dead code from this
    /// call path, never actionable). Confirm invokes the dialog's own gated <c>DoAndClose</c>
    /// (never <c>OnAcceptKeyPressed</c> -- see <see cref="CharEditorHealthBrowserCompat"/>'s class
    /// remarks), which writes back into <c>DialogAddHediff.SelectedPart</c>.
    /// </summary>
    internal sealed class ChoosePartAdapter : CharEditorBrowserAdapterBase
    {
        public ChoosePartAdapter(Window dialog)
            : base(dialog)
        {
        }

        public override string Title => "RimWorldAccess.CharEd.Browser.ChoosePartTitle".Translate().ToString();

        public override int ResultCount => CharEditorHealthBrowserCompat.ChoosePartCandidates(dialog).Count;

        public override string DescribeResultLabel(int index)
        {
            var results = CharEditorHealthBrowserCompat.ChoosePartCandidates(dialog);
            if (index < 0 || index >= results.Count)
                return "";
            BodyPartRecord part = results[index];
            return part == null ? "WholeBody".Translate().ToString() : part.Label.CapitalizeFirst();
        }

        public override string DescribeResultTooltip(int index)
        {
            var results = CharEditorHealthBrowserCompat.ChoosePartCandidates(dialog);
            return index >= 0 && index < results.Count ? results[index]?.def?.description : null;
        }

        public override int SelectedResultIndex
        {
            get
            {
                var results = CharEditorHealthBrowserCompat.ChoosePartCandidates(dialog);
                BodyPartRecord selected = CharEditorHealthBrowserCompat.ChoosePartSelected(dialog);
                return results.FindIndex(p => p == selected);
            }
        }

        public override void SelectResult(int index)
        {
            var results = CharEditorHealthBrowserCompat.ChoosePartCandidates(dialog);
            if (index >= 0 && index < results.Count)
                CharEditorHealthBrowserCompat.ChoosePartSetSelected(dialog, results[index]);
        }

        public override bool Confirm()
        {
            CharEditorHealthBrowserCompat.ChoosePartConfirm(dialog);
            return !Find.WindowStack.IsOpen(dialog);
        }
    }
}
