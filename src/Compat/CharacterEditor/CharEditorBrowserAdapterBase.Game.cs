using System;
using System.Collections.Generic;
using System.Linq;
using RimWorldAccess.Shell;
using Verse;

namespace RimWorldAccess
{
    /// <summary>
    /// Shared plumbing for every <see cref="ICharEditorBrowserAdapter"/>: the base owns the
    /// ceremony each browser dialog repeats -- the dialog handle, the deferred-commit entry notice,
    /// the OK/Cancel vehicles, the empty Parameters and ExtraActions blocks, the "All" label
    /// helpers and the filter-descriptor factories -- while each adapter declares only what its own
    /// dialog does differently (title, filter rows, results, and any parameter rows or extra
    /// actions it actually has).
    /// </summary>
    internal abstract class CharEditorBrowserAdapterBase : ICharEditorBrowserAdapter
    {
        private static readonly IReadOnlyList<ScreenAction> NoExtraActions = new List<ScreenAction>();
        private static readonly IReadOnlyList<CharEditorBrowserFilter> NoFilters = new List<CharEditorBrowserFilter>();

        protected readonly Window dialog;

        private IReadOnlyList<CharEditorBrowserFilter> filters;

        protected CharEditorBrowserAdapterBase(Window dialog)
        {
            this.dialog = dialog;
        }

        public abstract string Title { get; }

        public virtual string EntryAnnouncement => "RimWorldAccess.CharEd.Browser.ChangesApplyOnConfirm".Translate().ToString();

        // ------------------------------------------------------------------
        // Filters.
        // ------------------------------------------------------------------

        public IReadOnlyList<CharEditorBrowserFilter> Filters
        {
            get
            {
                if (filters == null)
                    filters = BuildFilters();
                return filters;
            }
        }

        /// <summary>Builds this dialog's filter rows once, in display order. No rows at all for a plain radio list (ChoosePart, ChangeFaction, FindPawn, Fullheal).</summary>
        protected virtual IReadOnlyList<CharEditorBrowserFilter> BuildFilters()
        {
            return NoFilters;
        }

        protected static CharEditorBrowserFilter Checkbox(string label, Func<bool> state, Action toggle)
        {
            return new CharEditorBrowserFilter
            {
                Label = label,
                CheckState = state,
                Toggle = toggle,
            };
        }

        /// <summary>
        /// A dropdown filter whose spoken value is the current candidate's own label, falling back
        /// to <paramref name="unmatchedLabel"/> (the mod's own "All" word unless the dialog uses
        /// something else) when the dialog's current value is not in the candidate list.
        /// </summary>
        protected CharEditorBrowserFilter Combo(string label, Func<List<string>> candidates, Func<int> currentIndex,
            Action<int> setCandidate, Func<string> unmatchedLabel = null)
        {
            Func<string> unmatched = unmatchedLabel;
            if (unmatched == null)
                unmatched = AllLabel;
            return new CharEditorBrowserFilter
            {
                Label = label,
                Candidates = candidates,
                CurrentIndex = currentIndex,
                SetCandidate = setCandidate,
                ValueLabel = delegate
                {
                    int i = currentIndex();
                    List<string> labels = candidates();
                    return i >= 0 && i < labels.Count ? labels[i] : unmatched();
                },
            };
        }

        /// <summary>
        /// The mod-name dropdown every <c>DialogTemplate&lt;T&gt;</c> browser draws. Candidate
        /// labels come from the raw name list with null shown as "All"; the index and the value
        /// handed to <paramref name="setName"/> both stay on that raw list, so a null entry writes
        /// the null the mod's own dropdown writes. <paramref name="afterSet"/> runs whatever extra
        /// re-query the dialog needs (see ObjectsAdapter's results snapshot).
        /// </summary>
        protected CharEditorBrowserFilter ModNameCombo(Func<List<string>> names, Func<string> currentName,
            Action<string> setName, Action afterSet = null)
        {
            return Combo("RimWorldAccess.CharEd.Browser.ModNameFilter".Translate(),
                () => names().Select(NameOrAll).ToList(),
                delegate
                {
                    string current = currentName();
                    return names().FindIndex(n => n == current);
                },
                delegate(int candidateIndex)
                {
                    List<string> candidates = names();
                    if (candidateIndex >= 0 && candidateIndex < candidates.Count)
                        setName(candidates[candidateIndex]);
                    if (afterSet != null)
                        afterSet();
                });
        }

        // ------------------------------------------------------------------
        // Results.
        // ------------------------------------------------------------------

        public abstract int ResultCount { get; }
        public abstract string DescribeResultLabel(int index);
        public abstract string DescribeResultTooltip(int index);
        public abstract int SelectedResultIndex { get; }
        public abstract void SelectResult(int index);

        public virtual bool PickCommitsAdd => false;

        public virtual bool ConfirmRequiresResultPick => true;

        // ------------------------------------------------------------------
        // Parameters: none unless the dialog declares some.
        // ------------------------------------------------------------------

        public virtual int ParameterCount => 0;
        public virtual ElementDescription DescribeParameterRow(int index) => new ElementDescription();
        public virtual bool CanAdjustParameterRow(int index) => false;
        public virtual void AdjustParameterRow(int index, int direction) { }
        public virtual void ActivateParameterRow(int index, Action onChanged) { }

        // ------------------------------------------------------------------
        // Confirm / Cancel / extra actions.
        // ------------------------------------------------------------------

        /// <summary>Vehicle A: the dialog's own OK path (Window.OnAcceptKeyPressed). Dialogs whose OK is gated behind their own CheckAndDo/DoAndClose override this.</summary>
        public virtual bool Confirm()
        {
            dialog.OnAcceptKeyPressed();
            return !Find.WindowStack.IsOpen(dialog);
        }

        public virtual void Cancel()
        {
            dialog.OnCancelKeyPressed();
        }

        public virtual IReadOnlyList<ScreenAction> ExtraActions => NoExtraActions;

        /// <summary>Only adapters owning a TextFieldEditSession override this.</summary>
        public virtual void CancelPendingEdit()
        {
        }

        // ------------------------------------------------------------------
        // Label helpers.
        // ------------------------------------------------------------------

        /// <summary>The mod's own "All" word, its dropdowns' display text for a null filter value; ObjectsAdapter reads its own dialog family's copy of it.</summary>
        protected virtual string AllLabel()
        {
            string all = CharEditorCompat.AllLabel;
            return all.NullOrEmpty() ? "RimWorldAccess.CharEd.Browser.AllOption".Translate().ToString() : all;
        }

        protected string NameOrAll(string name)
        {
            return name ?? AllLabel();
        }
    }
}
