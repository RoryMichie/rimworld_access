using System;
using System.Collections.Generic;
using System.Linq;
using RimWorld;
using RimWorldAccess.Shell;
using Verse;

namespace RimWorldAccess
{
    /// <summary>
    /// Adapter for <c>CharacterEditor.DialogAddTrait</c>. Three ComboBox filters (mod name,
    /// stat-modifier, category); results are the dialog's own filtered candidate list; selection
    /// and Confirm ride the dialog's own <c>selectedTrait</c> field and
    /// <c>Window.OnAcceptKeyPressed</c> (which runs <c>DoAndClose</c> -- add, or replace when
    /// opened in edit mode). One extra action: the dialog's own "random pick from the filtered
    /// list" dice.
    /// </summary>
    internal sealed class AddTraitAdapter : CharEditorBrowserAdapterBase
    {
        private readonly List<ScreenAction> extraActions;

        public AddTraitAdapter(Window dialog)
            : base(dialog)
        {
            extraActions = new List<ScreenAction>
            {
                new ScreenAction("RimWorldAccess.CharEd.Browser.Randomize".Translate(), Randomize),
            };
        }

        public override string Title => "RimWorldAccess.CharEd.Character.AddTrait".Translate().ToString();

        protected override IReadOnlyList<CharEditorBrowserFilter> BuildFilters()
        {
            return new List<CharEditorBrowserFilter>
            {
                ModNameCombo(CharEditorCompat.TraitModNames,
                    () => CharEditorBrowserCompat.AddTraitModName(dialog),
                    name => CharEditorBrowserCompat.AddTraitSetModName(dialog, name)),
                Combo("RimWorldAccess.CharEd.Browser.StatModifierFilter".Translate(),
                    () => CharEditorBrowserCompat.AddTraitStatModifierCandidates(dialog)
                        .Select(sm => sm == null ? AllLabel() : sm.stat.LabelCap.ToString()).ToList(),
                    delegate
                    {
                        StatModifier current = CharEditorBrowserCompat.AddTraitStatModifier(dialog);
                        return CharEditorBrowserCompat.AddTraitStatModifierCandidates(dialog).FindIndex(sm => sm == current);
                    },
                    delegate(int candidateIndex)
                    {
                        List<StatModifier> candidates = CharEditorBrowserCompat.AddTraitStatModifierCandidates(dialog);
                        if (candidateIndex >= 0 && candidateIndex < candidates.Count)
                            CharEditorBrowserCompat.AddTraitSetStatModifier(dialog, candidates[candidateIndex]);
                    }),
                Combo("RimWorldAccess.CharEd.Browser.CategoryFilter".Translate(),
                    () => CharEditorBrowserCompat.AddTraitCategoryCandidates(dialog).Select(NameOrAll).ToList(),
                    delegate
                    {
                        string current = CharEditorBrowserCompat.AddTraitCategory(dialog);
                        return CharEditorBrowserCompat.AddTraitCategoryCandidates(dialog).FindIndex(c => c == current);
                    },
                    delegate(int candidateIndex)
                    {
                        List<string> candidates = CharEditorBrowserCompat.AddTraitCategoryCandidates(dialog);
                        if (candidateIndex >= 0 && candidateIndex < candidates.Count)
                            CharEditorBrowserCompat.AddTraitSetCategory(dialog, candidates[candidateIndex]);
                    }),
            };
        }

        public override int ResultCount => CharEditorBrowserCompat.AddTraitResults(dialog).Count;

        public override string DescribeResultLabel(int index)
        {
            var results = CharEditorBrowserCompat.AddTraitResults(dialog);
            if (index < 0 || index >= results.Count)
                return "";
            KeyValuePair<TraitDef, TraitDegreeData> pair = results[index];
            return pair.Value != null ? pair.Value.LabelCap.ToString() : pair.Key.LabelCap.ToString();
        }

        public override string DescribeResultTooltip(int index)
        {
            var results = CharEditorBrowserCompat.AddTraitResults(dialog);
            if (index < 0 || index >= results.Count)
                return null;
            KeyValuePair<TraitDef, TraitDegreeData> pair = results[index];
            Pawn pawn = CharEditorCompat.CurrentPawn;
            if (pawn == null)
                return null;
            // Vanilla tip string for a not-yet-added trait: build the same temporary Trait the
            // mod's own TraitTool.UpdateDicTooltip constructs (TraitTool.cs:60-61), a pure read
            // against public vanilla data -- no CharacterEditor-specific logic reproduced.
            var trait = new Trait(pair.Key, pair.Value?.degree ?? 0);
            return trait.TipString(pawn);
        }

        public override int SelectedResultIndex
        {
            get
            {
                var results = CharEditorBrowserCompat.AddTraitResults(dialog);
                KeyValuePair<TraitDef, TraitDegreeData> selected = CharEditorBrowserCompat.AddTraitSelected(dialog);
                return results.FindIndex(p => p.Key == selected.Key && p.Value == selected.Value);
            }
        }

        public override void SelectResult(int index)
        {
            var results = CharEditorBrowserCompat.AddTraitResults(dialog);
            if (index >= 0 && index < results.Count)
                CharEditorBrowserCompat.AddTraitSetSelected(dialog, results[index]);
        }

        public override IReadOnlyList<ScreenAction> ExtraActions => extraActions;

        private void Randomize()
        {
            CharEditorBrowserCompat.AddTraitRandomize(dialog);
        }
    }
}
