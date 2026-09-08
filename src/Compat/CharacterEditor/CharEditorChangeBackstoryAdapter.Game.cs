using System;
using System.Collections.Generic;
using System.Linq;
using RimWorld;
using RimWorldAccess.Shell;
using Verse;

namespace RimWorldAccess
{
    /// <summary>
    /// Adapter for <c>CharacterEditor.DialogChangeBackstory</c>. One Checkbox filter (no blocking
    /// skills) plus three ComboBox filters (spawn category, per-skill gain threshold, aggregate
    /// sum); results are the dialog's own filtered candidate list; selection and Confirm ride the
    /// dialog's own <c>selectedBackstory</c> field and <c>Window.OnAcceptKeyPressed</c> (which
    /// applies and recalculates via the dialog's own <c>DoAndClose</c>). Two extra actions: Remove
    /// backstory (gated exactly as the mod gates its own Remove button -- childhood slot only
    /// under three biological years) and the dialog's own random-pick dice.
    /// </summary>
    internal sealed class ChangeBackstoryAdapter : CharEditorBrowserAdapterBase
    {
        private readonly bool isChildhood;
        private readonly List<ScreenAction> extraActions;

        public ChangeBackstoryAdapter(Window dialog, bool isChildhood)
            : base(dialog)
        {
            this.isChildhood = isChildhood;
            extraActions = new List<ScreenAction>
            {
                BuildRemoveAction(),
                new ScreenAction("RimWorldAccess.CharEd.Browser.Randomize".Translate(), Randomize),
            };
        }

        private ScreenAction BuildRemoveAction()
        {
            Pawn pawn = CharEditorCompat.CurrentPawn;
            bool canRemove = CharEditorBrowserCompat.BackstoryCanRemove(pawn, isChildhood);
            return new ScreenAction("Remove".Translate(), Remove,
                disabled: !canRemove,
                disabledReason: canRemove ? null : "RimWorldAccess.CharEd.Browser.RemoveBackstoryUnavailable".Translate().ToString());
        }

        public override string Title => (isChildhood ? "Childhood".Translate() : "Adulthood".Translate()).ToString();

        /// <summary>This dialog's dropdowns show nothing at all for an unmatched current value, never the mod's "All" word.</summary>
        protected override IReadOnlyList<CharEditorBrowserFilter> BuildFilters()
        {
            return new List<CharEditorBrowserFilter>
            {
                Checkbox("RimWorldAccess.CharEd.Browser.NoBlockingSkills".Translate(),
                    () => CharEditorBrowserCompat.BackstoryNoBlockingSkills(dialog),
                    () => CharEditorBrowserCompat.ToggleNoBlockingSkills(dialog)),
                Combo("RimWorldAccess.CharEd.Browser.CategoryFilter".Translate(),
                    () => CharEditorBrowserCompat.BackstoryCategoryCandidates(dialog),
                    () => CharEditorBrowserCompat.BackstoryCategoryCandidates(dialog)
                        .FindIndex(c => c == CharEditorBrowserCompat.BackstoryCategory(dialog)),
                    delegate(int candidateIndex)
                    {
                        List<string> candidates = CharEditorBrowserCompat.BackstoryCategoryCandidates(dialog);
                        if (candidateIndex >= 0 && candidateIndex < candidates.Count)
                            CharEditorBrowserCompat.BackstorySetCategory(dialog, candidates[candidateIndex]);
                    },
                    unmatchedLabel: () => ""),
                Combo("RimWorldAccess.CharEd.Browser.SkillGainFilter".Translate(),
                    () => CharEditorBrowserCompat.BackstoryFilterKeyCandidates(dialog),
                    () => CharEditorBrowserCompat.BackstoryFilterKeyCandidates(dialog)
                        .FindIndex(c => c == CharEditorBrowserCompat.BackstoryFilterKey(dialog)),
                    delegate(int candidateIndex)
                    {
                        List<string> candidates = CharEditorBrowserCompat.BackstoryFilterKeyCandidates(dialog);
                        if (candidateIndex >= 0 && candidateIndex < candidates.Count)
                            CharEditorBrowserCompat.BackstorySetFilterKey(dialog, candidates[candidateIndex]);
                    },
                    unmatchedLabel: () => ""),
                Combo("RimWorldAccess.CharEd.Browser.SumFilter".Translate(),
                    () => CharEditorBrowserCompat.BackstorySumFilterCandidates(dialog),
                    () => CharEditorBrowserCompat.BackstorySumFilterCandidates(dialog)
                        .FindIndex(c => c == CharEditorBrowserCompat.BackstorySumFilter(dialog)),
                    delegate(int candidateIndex)
                    {
                        List<string> candidates = CharEditorBrowserCompat.BackstorySumFilterCandidates(dialog);
                        if (candidateIndex >= 0 && candidateIndex < candidates.Count)
                            CharEditorBrowserCompat.BackstorySetSumFilter(dialog, candidates[candidateIndex]);
                    },
                    unmatchedLabel: () => ""),
            };
        }

        public override int ResultCount => CharEditorBrowserCompat.BackstoryResults(dialog).Count;

        public override string DescribeResultLabel(int index)
        {
            var results = CharEditorBrowserCompat.BackstoryResults(dialog);
            if (index < 0 || index >= results.Count)
                return "";
            Pawn pawn = CharEditorCompat.CurrentPawn;
            return results[index].TitleCapFor(pawn?.gender ?? Gender.None);
        }

        public override string DescribeResultTooltip(int index)
        {
            var results = CharEditorBrowserCompat.BackstoryResults(dialog);
            Pawn pawn = CharEditorCompat.CurrentPawn;
            if (index < 0 || index >= results.Count || pawn == null)
                return null;
            // The same FullDescriptionFor(pawn).Resolve() the dialog's own dicBackstory value
            // caches (DialogChangeBackstory.cs:271) -- public vanilla data, computed directly.
            return results[index].FullDescriptionFor(pawn).Resolve();
        }

        public override int SelectedResultIndex
        {
            get
            {
                var results = CharEditorBrowserCompat.BackstoryResults(dialog);
                BackstoryDef selected = CharEditorBrowserCompat.BackstorySelected(dialog);
                return results.IndexOf(selected);
            }
        }

        public override void SelectResult(int index)
        {
            var results = CharEditorBrowserCompat.BackstoryResults(dialog);
            if (index >= 0 && index < results.Count)
                CharEditorBrowserCompat.BackstorySetSelected(dialog, results[index]);
        }

        public override IReadOnlyList<ScreenAction> ExtraActions => extraActions;

        private void Remove()
        {
            Pawn pawn = CharEditorCompat.CurrentPawn;
            if (!CharEditorBrowserCompat.BackstoryCanRemove(pawn, isChildhood))
                return;
            CharEditorBrowserCompat.BackstoryRemove(dialog);
        }

        private void Randomize()
        {
            CharEditorBrowserCompat.BackstoryRandomize(dialog);
        }
    }
}
