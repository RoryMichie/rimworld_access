using System;
using System.Collections.Generic;
using System.Linq;
using RimWorld;
using RimWorldAccess.Shell;
using Verse;

namespace RimWorldAccess
{
    /// <summary>
    /// Adapter for <c>CharacterEditor.DialogChoosePawn</c>, reusing the shared browser scope with
    /// one faction/list-source ComboBox filter and zero parameters. Chained from
    /// <see cref="AddHediffAdapter"/>'s Confirm for hediffs needing another pawn (pregnancy
    /// fathers, links); Confirm invokes the dialog's own gated <c>DoAndClose</c> (never
    /// <c>OnAcceptKeyPressed</c>), which writes back into <c>DialogAddHediff.SelectedPawn</c>. The
    /// gender restriction this dialog can carry is baked into its own candidate list at
    /// construction (not user-adjustable), so no separate gender filter row is offered.
    /// </summary>
    internal sealed class ChoosePawnAdapter : CharEditorBrowserAdapterBase
    {
        public ChoosePawnAdapter(Window dialog)
            : base(dialog)
        {
        }

        public override string Title
        {
            get
            {
                string baseTitle = "RimWorldAccess.CharEd.Browser.ChoosePawnTitle".Translate().ToString();
                string custom = CharEditorHealthBrowserCompat.ChoosePawnCustomText(dialog);
                return custom.NullOrEmpty() ? baseTitle : baseTitle + " " + custom;
            }
        }

        protected override IReadOnlyList<CharEditorBrowserFilter> BuildFilters()
        {
            return new List<CharEditorBrowserFilter>
            {
                new CharEditorBrowserFilter
                {
                    Label = "RimWorldAccess.CharEd.ListSource".Translate(),
                    Candidates = CharEditorHealthBrowserCompat.ChoosePawnFactionCandidates,
                    CurrentIndex = delegate
                    {
                        string current = CharEditorHealthBrowserCompat.ChoosePawnSelectedFaction(dialog);
                        return CharEditorHealthBrowserCompat.ChoosePawnFactionCandidates().FindIndex(f => f == current);
                    },
                    SetCandidate = delegate(int candidateIndex)
                    {
                        List<string> candidates = CharEditorHealthBrowserCompat.ChoosePawnFactionCandidates();
                        if (candidateIndex >= 0 && candidateIndex < candidates.Count)
                            CharEditorHealthBrowserCompat.ChoosePawnSetFaction(dialog, candidates[candidateIndex]);
                    },
                    // The dialog's own list-source name is read directly rather than looked up in the
                    // candidate list, which is the editor's shared source list.
                    ValueLabel = () => CharEditorHealthBrowserCompat.ChoosePawnSelectedFaction(dialog) ?? "",
                },
            };
        }

        public override int ResultCount => CharEditorHealthBrowserCompat.ChoosePawnCandidates(dialog).Count;

        /// <summary>Same simplification FindPawnAdapter already established for a plain pawn-list row.</summary>
        public override string DescribeResultLabel(int index)
        {
            var results = CharEditorHealthBrowserCompat.ChoosePawnCandidates(dialog);
            return index >= 0 && index < results.Count ? results[index].LabelShortCap : "";
        }

        public override string DescribeResultTooltip(int index)
        {
            var results = CharEditorHealthBrowserCompat.ChoosePawnCandidates(dialog);
            return index >= 0 && index < results.Count ? results[index].MainDesc(writeFaction: true) : null;
        }

        public override int SelectedResultIndex
        {
            get
            {
                var results = CharEditorHealthBrowserCompat.ChoosePawnCandidates(dialog);
                Pawn selected = CharEditorHealthBrowserCompat.ChoosePawnSelected(dialog);
                return results.FindIndex(p => ReferenceEquals(p, selected));
            }
        }

        public override void SelectResult(int index)
        {
            var results = CharEditorHealthBrowserCompat.ChoosePawnCandidates(dialog);
            if (index >= 0 && index < results.Count)
                CharEditorHealthBrowserCompat.ChoosePawnSetSelected(dialog, results[index]);
        }

        public override bool Confirm()
        {
            CharEditorHealthBrowserCompat.ChoosePawnConfirm(dialog);
            return !Find.WindowStack.IsOpen(dialog);
        }
    }
}
