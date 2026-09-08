using System.Collections.Generic;
using System.Linq;
using RimWorld;
using RimWorldAccess.Shell;
using Verse;

namespace RimWorldAccess
{
    /// <summary>
    /// Adapter for <c>CharacterEditor.DialogGenery</c>, riding
    /// <see cref="CharEditorBrowserScope"/>. Two ComboBox filters (mod name, category); results are the
    /// dialog's own filtered candidate list; selection and Confirm ride the dialog's own
    /// <c>selectedDef</c> field and <c>Window.OnAcceptKeyPressed</c> (<c>DialogTemplate&lt;T&gt;.DoAndClose</c>,
    /// which adds the selected gene to whichever set <c>DialogGenery</c>'s OWN <c>bIsXeno</c> already
    /// captured at its construction -- see <see cref="CharEditorGeneryCompat"/>'s class remarks for
    /// why no extra plumbing is needed here). One extra action: the dialog's own random pick from the
    /// filtered list, plus "Def editor...", present only while a
    /// gene is selected (matching the mod's own <c>DrawParameterBase</c> gate,
    /// <c>if (selectedDef != null)</c>) -- it pushes <see cref="CharEditorDefEditorScope"/> over the
    /// mod's shared <c>CEditor.IsExtendedUI</c> widen toggle. See
    /// <see cref="CharEditorDefEditorCompat"/>'s class remarks for exactly which of GeneDef's
    /// seventy-plus fields that pane covers this slice.
    /// </summary>
    internal sealed class GeneryAdapter : CharEditorBrowserAdapterBase
    {
        private readonly List<ScreenAction> extraActions = new List<ScreenAction>();

        public GeneryAdapter(Window dialog)
            : base(dialog)
        {
        }

        /// <summary>The target gene set is read-only display info, part of the title -- see the facade's own class remarks for why Confirm needs no separate plumbing.</summary>
        public override string Title => "RimWorldAccess.CharEd.Browser.AddGeneTitle".Translate(TargetSetLabel()).ToString();

        private string TargetSetLabel()
        {
            return (CharEditorGeneryCompat.IsXeno(dialog)
                ? "Xenogenes".Translate()
                : "Endogenes".Translate()).ToString().CapitalizeFirst();
        }

        protected override IReadOnlyList<CharEditorBrowserFilter> BuildFilters()
        {
            return new List<CharEditorBrowserFilter>
            {
                ModNameCombo(() => CharEditorGeneryCompat.ModNameCandidates(dialog),
                    () => CharEditorGeneryCompat.ModName(dialog),
                    name => CharEditorGeneryCompat.SetModName(dialog, name)),
                Combo("RimWorldAccess.CharEd.Browser.CategoryFilter".Translate(),
                    () => CharEditorGeneryCompat.CategoryCandidates(dialog).Select(CategoryOrAll).ToList(),
                    delegate
                    {
                        GeneCategoryDef current = CharEditorGeneryCompat.Category(dialog);
                        return CharEditorGeneryCompat.CategoryCandidates(dialog).FindIndex(c => c == current);
                    },
                    delegate(int candidateIndex)
                    {
                        List<GeneCategoryDef> candidates = CharEditorGeneryCompat.CategoryCandidates(dialog);
                        if (candidateIndex >= 0 && candidateIndex < candidates.Count)
                            CharEditorGeneryCompat.SetCategory(dialog, candidates[candidateIndex]);
                    }),
            };
        }

        public override int ResultCount => CharEditorGeneryCompat.Results(dialog).Count;

        public override string DescribeResultLabel(int index)
        {
            var results = CharEditorGeneryCompat.Results(dialog);
            return index >= 0 && index < results.Count ? results[index].LabelCap.ToString() : "";
        }

        /// <summary>GeneDef.description, matching GeneTreeBuilder's own reuse of the same public vanilla field.</summary>
        public override string DescribeResultTooltip(int index)
        {
            var results = CharEditorGeneryCompat.Results(dialog);
            return index >= 0 && index < results.Count ? results[index].description : null;
        }

        public override int SelectedResultIndex
        {
            get
            {
                var results = CharEditorGeneryCompat.Results(dialog);
                GeneDef selected = CharEditorGeneryCompat.Selected(dialog);
                return results.IndexOf(selected);
            }
        }

        public override void SelectResult(int index)
        {
            var results = CharEditorGeneryCompat.Results(dialog);
            if (index >= 0 && index < results.Count)
                CharEditorGeneryCompat.SetSelected(dialog, results[index]);
        }

        public override IReadOnlyList<ScreenAction> ExtraActions
        {
            get
            {
                extraActions.Clear();
                extraActions.Add(new ScreenAction("RimWorldAccess.CharEd.Browser.Randomize".Translate(), Randomize));
                GeneDef selected = CharEditorGeneryCompat.Selected(dialog);
                if (selected != null && CharEditorDefEditorCompat.GeneReady)
                {
                    extraActions.Add(new ScreenAction("RimWorldAccess.CharEd.DefEditor.OpenAction".Translate(),
                        () => FocusStack.Push(new CharEditorDefEditorScope(dialog, selected))));
                }
                return extraActions;
            }
        }

        private void Randomize()
        {
            CharEditorGeneryCompat.Randomize(dialog);
        }

        private string CategoryOrAll(GeneCategoryDef category) =>
            category == null ? AllLabel() : category.LabelCap.ToString();
    }
}
