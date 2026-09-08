using System;
using System.Collections.Generic;
using Verse;

namespace RimWorldAccess
{
    /// <summary>
    /// Which pre-existing PawnFilter editor section a scalar criterion's menu item belongs to.
    /// Traits is listed for completeness (RequiredTraitsInPool) but is looked up directly rather
    /// than looped, since its visibility and position are tied to the Traits list, not a fixed slot.
    /// </summary>
    public enum FilterSection
    {
        Skills,
        Demographics,
        Conditions,
        Settings,
        Traits
    }

    /// <summary>
    /// One pawn-filter criterion's full behavior: label formatting, Left/Right adjustment,
    /// Shift+Home/End jump-to-extreme, whether it counts as an active filter, its reset value,
    /// cloning, and (where applicable) its pawn-level Evaluate() check. Registered once in
    /// <see cref="PawnFilter.Criteria"/> and consumed generically by PawnFilterHelper's menu
    /// assembly, PawnFilterState's Adjust/JumpToExtreme dispatch, and PawnFilter's
    /// Reset/HasActiveFilters/GetActiveFilterCount/Clone/CopyFrom/Evaluate — so a new scalar
    /// criterion is one descriptor plus one PawnFilter property, not a hand-kept line in every one
    /// of those methods.
    ///
    /// Skills and Traits are NOT covered here: they're per-element collections navigated by
    /// foreach, not single scalar values, so they never had the "one case per method" problem this
    /// exists to retire.
    /// </summary>
    public abstract class PawnFilterCriterion
    {
        public abstract FilterItemType ItemType { get; }
        public abstract FilterSection Section { get; }

        public abstract string Format(PawnFilter filter);

        /// Whether this criterion currently narrows the pawn pool — feeds HasActiveFilters/GetActiveFilterCount.
        public abstract bool IsActive(PawnFilter filter);

        public abstract void Reset(PawnFilter filter);

        /// Copies this criterion's value from <paramref name="source"/> onto <paramref name="target"/>.
        public abstract void CopyFrom(PawnFilter target, PawnFilter source);

        public virtual bool SupportsAdjust => false;
        public virtual void Adjust(PawnFilter filter, int direction, bool shift) { }

        public virtual bool SupportsJumpToExtreme => false;
        public virtual void JumpToExtreme(PawnFilter filter, bool isMax) { }

        /// A paired item type whose label must also refresh after Adjust/JumpToExtreme (min/max pairs).
        public virtual FilterItemType? LinkedItemType => null;

        /// Whether the menu item should currently be shown (e.g. RequiredTraitsInPool only when
        /// optional traits exist). Null means always visible.
        public virtual Func<PawnFilter, bool> VisibleWhen => null;
        public bool IsVisible(PawnFilter filter) => VisibleWhen == null || VisibleWhen(filter);

        /// Pawn-level predicate for PawnFilter.Evaluate(); null for criteria whose check is folded
        /// into a collection check (Skills/Traits) or that don't participate in Evaluate at all
        /// (RerollLimit, RequiredTraitsInPool, the CountOnly* toggles).
        public virtual Func<PawnFilter, Pawn, bool> EvaluatePawn => null;

        /// Matches the existing "!isBaby" grouping in PawnFilter.Evaluate: true for criteria
        /// skipped on baby pawns (Passion, SkillPoints), false for criteria always checked
        /// (Age, Gender, Health, Work).
        public virtual bool SkippedForBaby => false;
    }

    /// <summary>
    /// A paired min/max scalar range (Passion, SkillPoints, Age). Registering one range creates
    /// two descriptor instances — one per FilterItemType — that share the same floor/ceiling/step
    /// and cross-clamp each other exactly as the retired AdjustPassion/AdjustSkillPoints/AdjustAge
    /// helpers did. Only the "min" instance reports IsActive/EvaluatePawn, so a pair counts once.
    /// </summary>
    public sealed class RangeFilterCriterion : PawnFilterCriterion
    {
        private readonly FilterItemType itemType;
        private readonly FilterItemType linkedType;
        private readonly FilterSection section;
        private readonly bool isMin;
        private readonly Func<PawnFilter, int> getMin;
        private readonly Action<PawnFilter, int> setMin;
        private readonly Func<PawnFilter, string> formatMin;
        private readonly Func<PawnFilter, int> getMax;
        private readonly Action<PawnFilter, int> setMax;
        private readonly Func<PawnFilter, string> formatMax;
        private readonly int floor;
        private readonly int ceiling;
        private readonly int shiftStep;
        private readonly Func<PawnFilter, Pawn, bool> evaluatePawn;
        private readonly bool skippedForBaby;

        private RangeFilterCriterion(FilterItemType itemType, FilterItemType linkedType, FilterSection section, bool isMin,
            Func<PawnFilter, int> getMin, Action<PawnFilter, int> setMin, Func<PawnFilter, string> formatMin,
            Func<PawnFilter, int> getMax, Action<PawnFilter, int> setMax, Func<PawnFilter, string> formatMax,
            int floor, int ceiling, int shiftStep, Func<PawnFilter, Pawn, bool> evaluatePawn, bool skippedForBaby)
        {
            this.itemType = itemType;
            this.linkedType = linkedType;
            this.section = section;
            this.isMin = isMin;
            this.getMin = getMin;
            this.setMin = setMin;
            this.formatMin = formatMin;
            this.getMax = getMax;
            this.setMax = setMax;
            this.formatMax = formatMax;
            this.floor = floor;
            this.ceiling = ceiling;
            this.shiftStep = shiftStep;
            this.evaluatePawn = evaluatePawn;
            this.skippedForBaby = skippedForBaby;
        }

        public static void Register(List<PawnFilterCriterion> list, FilterSection section,
            FilterItemType minType, FilterItemType maxType,
            Func<PawnFilter, int> getMin, Action<PawnFilter, int> setMin, Func<PawnFilter, string> formatMin,
            Func<PawnFilter, int> getMax, Action<PawnFilter, int> setMax, Func<PawnFilter, string> formatMax,
            int floor, int ceiling, int shiftStep, Func<PawnFilter, Pawn, bool> evaluatePawn, bool skippedForBaby)
        {
            list.Add(new RangeFilterCriterion(minType, maxType, section, true,
                getMin, setMin, formatMin, getMax, setMax, formatMax, floor, ceiling, shiftStep, evaluatePawn, skippedForBaby));
            list.Add(new RangeFilterCriterion(maxType, minType, section, false,
                getMin, setMin, formatMin, getMax, setMax, formatMax, floor, ceiling, shiftStep, evaluatePawn, skippedForBaby));
        }

        private static int Clamp(int v, int lo, int hi) => v < lo ? lo : (v > hi ? hi : v);

        public override FilterItemType ItemType => itemType;
        public override FilterSection Section => section;
        public override FilterItemType? LinkedItemType => linkedType;

        public override string Format(PawnFilter filter) => isMin ? formatMin(filter) : formatMax(filter);

        // Only the min instance counts the pair, so a range never double-contributes to
        // HasActiveFilters/GetActiveFilterCount.
        public override bool IsActive(PawnFilter filter) => isMin && (getMin(filter) > floor || getMax(filter) < ceiling);

        public override void Reset(PawnFilter filter)
        {
            setMin(filter, floor);
            setMax(filter, ceiling);
        }

        public override void CopyFrom(PawnFilter target, PawnFilter source)
        {
            setMin(target, getMin(source));
            setMax(target, getMax(source));
        }

        public override bool SupportsAdjust => true;
        public override void Adjust(PawnFilter filter, int direction, bool shift)
        {
            int amount = direction * (shift ? shiftStep : 1);
            if (isMin)
            {
                setMin(filter, Clamp(getMin(filter) + amount, floor, ceiling));
                if (getMin(filter) > getMax(filter)) setMax(filter, getMin(filter));
            }
            else
            {
                setMax(filter, Clamp(getMax(filter) + amount, floor, ceiling));
                if (getMax(filter) < getMin(filter)) setMin(filter, getMax(filter));
            }
        }

        public override bool SupportsJumpToExtreme => true;
        public override void JumpToExtreme(PawnFilter filter, bool isMax)
        {
            if (isMin)
            {
                setMin(filter, isMax ? ceiling : floor);
                if (getMin(filter) > getMax(filter)) setMax(filter, getMin(filter));
            }
            else
            {
                setMax(filter, isMax ? ceiling : floor);
                if (getMax(filter) < getMin(filter)) setMin(filter, getMax(filter));
            }
        }

        public override Func<PawnFilter, Pawn, bool> EvaluatePawn => isMin ? evaluatePawn : null;
        public override bool SkippedForBaby => skippedForBaby;
    }

    /// <summary>
    /// A single scalar/enum/bool criterion (Gender, Health, Work, RerollLimit,
    /// RequiredTraitsInPool, CountOnlyHighestAttack, CountOnlyPassionSkills). Adjust and
    /// JumpToExtreme are optional — pass null for a criterion that doesn't support one (e.g. the
    /// CountOnly* toggles support neither; Gender/Health/Work cycle via Adjust but have no
    /// jump-to-extreme).
    /// </summary>
    public sealed class SettingFilterCriterion : PawnFilterCriterion
    {
        private readonly FilterItemType itemType;
        private readonly FilterSection section;
        private readonly Action<PawnFilter, int> adjust;
        private readonly Action<PawnFilter, bool> jumpToExtreme;
        private readonly Func<PawnFilter, string> format;
        private readonly Func<PawnFilter, bool> isActiveFn;
        private readonly Action<PawnFilter> resetFn;
        private readonly Action<PawnFilter, PawnFilter> copyFromFn;
        private readonly Func<PawnFilter, Pawn, bool> evaluatePawn;
        private readonly Func<PawnFilter, bool> visibleWhen;

        public SettingFilterCriterion(FilterItemType itemType, FilterSection section,
            Action<PawnFilter, int> adjust, Action<PawnFilter, bool> jumpToExtreme,
            Func<PawnFilter, string> format, Func<PawnFilter, bool> isActiveFn,
            Action<PawnFilter> resetFn, Action<PawnFilter, PawnFilter> copyFromFn,
            Func<PawnFilter, Pawn, bool> evaluatePawn = null, Func<PawnFilter, bool> visibleWhen = null)
        {
            this.itemType = itemType;
            this.section = section;
            this.adjust = adjust;
            this.jumpToExtreme = jumpToExtreme;
            this.format = format;
            this.isActiveFn = isActiveFn;
            this.resetFn = resetFn;
            this.copyFromFn = copyFromFn;
            this.evaluatePawn = evaluatePawn;
            this.visibleWhen = visibleWhen;
        }

        public override FilterItemType ItemType => itemType;
        public override FilterSection Section => section;
        public override string Format(PawnFilter filter) => format(filter);
        public override bool IsActive(PawnFilter filter) => isActiveFn(filter);
        public override void Reset(PawnFilter filter) => resetFn(filter);
        public override void CopyFrom(PawnFilter target, PawnFilter source) => copyFromFn(target, source);

        public override bool SupportsAdjust => adjust != null;
        public override void Adjust(PawnFilter filter, int direction, bool shift) => adjust?.Invoke(filter, direction);

        public override bool SupportsJumpToExtreme => jumpToExtreme != null;
        public override void JumpToExtreme(PawnFilter filter, bool isMax) => jumpToExtreme?.Invoke(filter, isMax);

        public override Func<PawnFilter, Pawn, bool> EvaluatePawn => evaluatePawn;
        public override Func<PawnFilter, bool> VisibleWhen => visibleWhen;
    }
}
