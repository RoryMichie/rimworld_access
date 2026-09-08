using System;
using System.Collections.Generic;
using System.Linq;
using RimWorld;
using RimWorldAccess.Shell;
using Verse;

namespace RimWorldAccess
{
    /// <summary>
    /// Adapter for <c>CharacterEditor.DialogChangeFaction</c>. No filters; Results is
    /// the radio faction list (the dialog's own defName-descending order, plus a leading None) --
    /// results and Confirm ride the dialog's own <c>selectedFaction</c> field and its
    /// <c>DoAndClose</c> (invoked directly; no <c>OnAcceptKeyPressed</c> override exists). ONE
    /// extra action, present only while the pawn currently has a faction (the mod's own header
    /// field NPEs on a factionless pawn, so the row simply does not exist
    /// there, matching what the mod can actually draw): Rename, via a
    /// <see cref="TextFieldEditSession"/> writing <c>Pawn.Faction.Name</c> directly (MUTATION-C --
    /// the mod's own header field is a bare property write with no wrapper, DialogChangeFaction.cs
    /// DrawEditLabel). Banish-first-then-SetFaction logic rides the dialog's own OK path
    /// unmodified.
    /// </summary>
    internal sealed class ChangeFactionAdapter : CharEditorBrowserAdapterBase
    {
        private readonly TextFieldEditSession renameSession = new TextFieldEditSession();

        public ChangeFactionAdapter(Window dialog)
            : base(dialog)
        {
        }

        public override string Title => "RimWorldAccess.CharEd.Browser.ChangeFactionTitle".Translate().ToString();

        public override int ResultCount => CharEditorBrowserCompat.ChangeFactionResults(dialog).Count;

        public override string DescribeResultLabel(int index)
        {
            var results = CharEditorBrowserCompat.ChangeFactionResults(dialog);
            if (index < 0 || index >= results.Count)
                return "";
            Faction f = results[index];
            return f == null ? "None".Translate().ToString() : f.Name;
        }

        /// <summary>The mod's own DrawEditLabel tooltip source is the header only; the list rows carry the same Factiondescr logic (player def description, else Faction.GetInfoText()) -- both public vanilla.</summary>
        public override string DescribeResultTooltip(int index)
        {
            var results = CharEditorBrowserCompat.ChangeFactionResults(dialog);
            if (index < 0 || index >= results.Count)
                return null;
            Faction f = results[index];
            if (f == null)
                return null;
            return f.IsPlayer ? f.def.description : f.GetInfoText();
        }

        public override int SelectedResultIndex
        {
            get
            {
                var results = CharEditorBrowserCompat.ChangeFactionResults(dialog);
                Faction selected = CharEditorBrowserCompat.ChangeFactionSelected(dialog);
                return results.IndexOf(selected);
            }
        }

        public override void SelectResult(int index)
        {
            var results = CharEditorBrowserCompat.ChangeFactionResults(dialog);
            if (index >= 0 && index < results.Count)
                CharEditorBrowserCompat.ChangeFactionSetSelected(dialog, results[index]);
        }

        public override bool Confirm()
        {
            return CharEditorBrowserCompat.ChangeFactionConfirm(dialog);
        }

        /// <summary>Recomputed on every read: the pawn's CURRENT faction can be null only before any change is confirmed, and Confirm closes the dialog -- so this only ever needs to reflect the state at open time, but reading it live costs nothing and stays honest if it somehow changes underneath.</summary>
        public override IReadOnlyList<ScreenAction> ExtraActions
        {
            get
            {
                var list = new List<ScreenAction>();
                Pawn pawn = CharEditorBrowserCompat.ChangeFactionPawn(dialog) ?? CharEditorCompat.CurrentPawn;
                if (pawn?.Faction != null)
                {
                    list.Add(new ScreenAction(
                        "RimWorldAccess.CharEd.Browser.ChangeFaction.RenameRow".Translate(pawn.Faction.Name),
                        BeginRename));
                }
                return list;
            }
        }

        public override void CancelPendingEdit()
        {
            renameSession.CancelIfActive();
        }

        /// <summary>
        /// MUTATION-C: mirrors DialogChangeFaction.DrawEditLabel's own commit exactly -- a bare
        /// `Faction.Name = text2;` write with no wrapper method and no length cap in the mod's own
        /// call site (`Widgets.TextField(rect, text)`, no maxLength argument), so this session is
        /// unrestricted too. Applies live per keystroke (matching the mod's own per-differing-frame
        /// commit), not deferred to a separate confirm step.
        /// </summary>
        private void BeginRename()
        {
            Pawn pawn = CharEditorBrowserCompat.ChangeFactionPawn(dialog) ?? CharEditorCompat.CurrentPawn;
            if (pawn?.Faction == null)
                return;
            var spec = new TextFieldSpec(labelKey: null, maxLength: null, minLength: 0);
            renameSession.EnterEdit(
                pawn.Faction.Name,
                spec,
                "RimWorldAccess.CharEd.Browser.ChangeFaction.RenameLabel".Translate().ToString(),
                apply: value => ApplyRename(pawn, value),
                onExit: () => { });
        }

        private static void ApplyRename(Pawn pawn, string value)
        {
            if (pawn?.Faction != null)
                pawn.Faction.Name = value ?? "";
        }
    }
}
