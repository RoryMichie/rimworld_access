using System;
using System.Collections.Generic;
using System.Linq;
using RimWorld;
using RimWorldAccess.Shell;
using Verse;

namespace RimWorldAccess
{
    /// <summary>
    /// Adapter for <c>CharacterEditor.DialogFindPawn</c>. No filters; Results is the
    /// SAME live <c>EType.Pawns</c> container the editor's own pawn rows and portrait arrows walk
    /// (<see cref="CharEditorCompat.PawnList"/>). Selection switches the edited pawn IMMEDIATELY on
    /// Enter -- the mod's own <c>ASelectPawn</c> assigns
    /// <c>CEditor.Pawn</c> at click time, not deferred to Confirm); Confirm ADDITIONALLY jumps the
    /// camera and switches the active map via the dialog's own <c>DoAndClose</c>
    /// (<c>OnAcceptKeyPressed</c> override exists on this dialog). Cancel just closes -- the pawn
    /// switch, if any already happened, is NOT reverted (mod behavior).
    /// </summary>
    internal sealed class FindPawnAdapter : CharEditorBrowserAdapterBase
    {
        public FindPawnAdapter(Window dialog)
            : base(dialog)
        {
        }

        public override string Title => "RimWorldAccess.CharEd.Browser.FindPawnTitle".Translate().ToString();

        /// <summary>Overrides the shared deferred-commit wording: this dialog's selection is immediate, not deferred.</summary>
        public override string EntryAnnouncement => "RimWorldAccess.CharEd.Browser.FindPawn.SelectingSwitches".Translate().ToString();

        public override int ResultCount => CharEditorCompat.PawnList().Count;

        public override string DescribeResultLabel(int index)
        {
            var results = CharEditorCompat.PawnList();
            return index >= 0 && index < results.Count ? results[index].LabelShortCap : "";
        }

        /// <summary>Pawn.MainDesc(writeFaction: true) -- the SAME public vanilla tooltip getter the dialog's own list view uses.</summary>
        public override string DescribeResultTooltip(int index)
        {
            var results = CharEditorCompat.PawnList();
            return index >= 0 && index < results.Count ? results[index].MainDesc(writeFaction: true) : null;
        }

        /// <summary>The row matching the CURRENTLY edited pawn -- selection already happened by the time this is read, since it is immediate, not deferred.</summary>
        public override int SelectedResultIndex
        {
            get
            {
                var results = CharEditorCompat.PawnList();
                Pawn current = CharEditorCompat.CurrentPawn;
                return results.FindIndex(p => ReferenceEquals(p, current));
            }
        }

        /// <summary>Vehicle A: the dialog's own ASelectPawn -- switches CEditor.Pawn immediately, not deferred.</summary>
        public override void SelectResult(int index)
        {
            var results = CharEditorCompat.PawnList();
            if (index >= 0 && index < results.Count)
                CharEditorBrowserCompat.FindPawnSelect(dialog, results[index]);
        }

    }
}
