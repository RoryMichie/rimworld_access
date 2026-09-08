using System;
using System.Collections.Generic;
using System.Linq;
using RimWorld;
using Verse;

namespace RimWorldAccess
{
    public enum TraitFilterMode
    {
        Required,
        Optional,
        Excluded
    }

    public enum HealthFilterMode
    {
        AllowAll,
        OnlyStartCondition,
        NoPain,
        NoAddiction,
        AllowNone
    }

    public enum WorkFilterMode
    {
        AllowAll,
        NoDumbLabor,
        AllowNone
    }

    public class SkillFilter
    {
        public SkillDef Skill { get; set; }
        public int MinLevel { get; set; }
        public Passion MinPassion { get; set; }

        public bool IsActive => MinLevel > 0 || MinPassion != Passion.None;
    }

    public class TraitFilter
    {
        public TraitDef Def { get; set; }
        public int Degree { get; set; }
        public TraitFilterMode Mode { get; set; }

        public string Label => new Trait(Def, Degree).LabelCap;
    }

    public class PawnFilter
    {
        public List<SkillFilter> Skills { get; set; } = new List<SkillFilter>();
        public List<TraitFilter> Traits { get; set; } = new List<TraitFilter>();
        public int AgeMin { get; set; } = 0;
        public int AgeMax { get; set; } = 120;
        public Gender? Gender { get; set; } = null;
        public HealthFilterMode Health { get; set; } = HealthFilterMode.AllowAll;
        public WorkFilterMode Work { get; set; } = WorkFilterMode.AllowAll;
        public int PassionMin { get; set; } = 0;
        public int PassionMax { get; set; } = 12;
        public int SkillPointsMin { get; set; } = 0;
        public int SkillPointsMax { get; set; } = 240;
        public int RerollLimit { get; set; } = 500;
        public int RequiredTraitsInPool { get; set; } = 0;
        public bool CountOnlyHighestAttack { get; set; } = false;
        public bool CountOnlyPassionSkills { get; set; } = false;

        // ===== CRITERIA REGISTRY =====
        // See PawnFilterCriteria.cs for the descriptor shapes. Skills/Traits stay bespoke below
        // (they're per-element collections, not single scalar values); every other criterion is
        // registered exactly once here and consumed by Reset/HasActiveFilters/
        // GetActiveFilterCount/Evaluate/Clone/CopyFrom below, by PawnFilterHelper's menu assembly,
        // and by PawnFilterState's Adjust/JumpToExtreme dispatch.
        private static readonly List<PawnFilterCriterion> criteriaList = BuildCriteria();
        private static readonly Dictionary<FilterItemType, PawnFilterCriterion> criteriaByType =
            criteriaList.ToDictionary(c => c.ItemType);

        public static IReadOnlyList<PawnFilterCriterion> Criteria => criteriaList;

        public static PawnFilterCriterion FindCriterion(FilterItemType itemType) =>
            criteriaByType.TryGetValue(itemType, out var criterion) ? criterion : null;

        private static List<PawnFilterCriterion> BuildCriteria()
        {
            var list = new List<PawnFilterCriterion>();

            RangeFilterCriterion.Register(list, FilterSection.Skills,
                FilterItemType.PassionMin, FilterItemType.PassionMax,
                f => f.PassionMin, (f, v) => f.PassionMin = v, PawnFilterHelper.FormatPassionMinLabel,
                f => f.PassionMax, (f, v) => f.PassionMax = v, PawnFilterHelper.FormatPassionMaxLabel,
                floor: 0, ceiling: 12, shiftStep: 3,
                evaluatePawn: (f, p) => f.CheckPassionRange(p), skippedForBaby: true);

            RangeFilterCriterion.Register(list, FilterSection.Skills,
                FilterItemType.SkillPointsMin, FilterItemType.SkillPointsMax,
                f => f.SkillPointsMin, (f, v) => f.SkillPointsMin = v, PawnFilterHelper.FormatSkillPointsMinLabel,
                f => f.SkillPointsMax, (f, v) => f.SkillPointsMax = v, PawnFilterHelper.FormatSkillPointsMaxLabel,
                floor: 0, ceiling: 240, shiftStep: 10,
                evaluatePawn: (f, p) => f.CheckSkillPoints(p), skippedForBaby: true);

            list.Add(new SettingFilterCriterion(
                FilterItemType.CountOnlyHighestAttack, FilterSection.Skills,
                adjust: null, jumpToExtreme: null,
                format: PawnFilterHelper.FormatCountOnlyHighestAttackLabel,
                isActiveFn: f => f.CountOnlyHighestAttack,
                resetFn: f => f.CountOnlyHighestAttack = false,
                copyFromFn: (target, source) => target.CountOnlyHighestAttack = source.CountOnlyHighestAttack));

            list.Add(new SettingFilterCriterion(
                FilterItemType.CountOnlyPassionSkills, FilterSection.Skills,
                adjust: null, jumpToExtreme: null,
                format: PawnFilterHelper.FormatCountOnlyPassionSkillsLabel,
                isActiveFn: f => f.CountOnlyPassionSkills,
                resetFn: f => f.CountOnlyPassionSkills = false,
                copyFromFn: (target, source) => target.CountOnlyPassionSkills = source.CountOnlyPassionSkills));

            RangeFilterCriterion.Register(list, FilterSection.Demographics,
                FilterItemType.AgeMin, FilterItemType.AgeMax,
                f => f.AgeMin, (f, v) => f.AgeMin = v, PawnFilterHelper.FormatAgeMinLabel,
                f => f.AgeMax, (f, v) => f.AgeMax = v, PawnFilterHelper.FormatAgeMaxLabel,
                floor: 0, ceiling: 120, shiftStep: 5,
                evaluatePawn: (f, p) => f.CheckAge(p), skippedForBaby: false);

            list.Add(new SettingFilterCriterion(
                FilterItemType.Gender, FilterSection.Demographics,
                adjust: (f, dir) => PawnFilterHelper.CycleGender(f, dir), jumpToExtreme: null,
                format: PawnFilterHelper.FormatGenderLabel,
                isActiveFn: f => f.Gender.HasValue,
                resetFn: f => f.Gender = null,
                copyFromFn: (target, source) => target.Gender = source.Gender,
                evaluatePawn: (f, p) => f.CheckGender(p)));

            list.Add(new SettingFilterCriterion(
                FilterItemType.Health, FilterSection.Conditions,
                adjust: (f, dir) => PawnFilterHelper.CycleHealth(f, dir), jumpToExtreme: null,
                format: PawnFilterHelper.FormatHealthLabel,
                isActiveFn: f => f.Health != HealthFilterMode.AllowAll,
                resetFn: f => f.Health = HealthFilterMode.AllowAll,
                copyFromFn: (target, source) => target.Health = source.Health,
                evaluatePawn: (f, p) => f.CheckHealth(p)));

            list.Add(new SettingFilterCriterion(
                FilterItemType.Work, FilterSection.Conditions,
                adjust: (f, dir) => PawnFilterHelper.CycleWork(f, dir), jumpToExtreme: null,
                format: PawnFilterHelper.FormatWorkLabel,
                isActiveFn: f => f.Work != WorkFilterMode.AllowAll,
                resetFn: f => f.Work = WorkFilterMode.AllowAll,
                copyFromFn: (target, source) => target.Work = source.Work,
                evaluatePawn: (f, p) => f.CheckWork(p)));

            list.Add(new SettingFilterCriterion(
                FilterItemType.RerollLimit, FilterSection.Settings,
                adjust: (f, dir) => PawnFilterHelper.AdjustRerollLimit(f, dir),
                jumpToExtreme: (f, isMax) => f.RerollLimit = isMax ? 50000 : 100,
                format: PawnFilterHelper.FormatRerollLimitLabel,
                isActiveFn: f => false, // a reroll setting, not a pawn filter — never counted as active
                resetFn: f => f.RerollLimit = 500,
                copyFromFn: (target, source) => target.RerollLimit = source.RerollLimit));

            list.Add(new SettingFilterCriterion(
                FilterItemType.RequiredTraitsInPool, FilterSection.Traits,
                adjust: (f, dir) => PawnFilterHelper.AdjustRequiredTraitsInPool(f, dir),
                jumpToExtreme: (f, isMax) =>
                {
                    int optionalCount = f.Traits.Count(t => t.Mode == TraitFilterMode.Optional);
                    f.RequiredTraitsInPool = isMax ? Math.Min(3, optionalCount) : 0;
                },
                format: PawnFilterHelper.FormatRequiredTraitsInPoolLabel,
                isActiveFn: f => f.RequiredTraitsInPool > 0,
                resetFn: f => f.RequiredTraitsInPool = 0,
                copyFromFn: (target, source) => target.RequiredTraitsInPool = source.RequiredTraitsInPool,
                visibleWhen: f => f.Traits.Any(t => t.Mode == TraitFilterMode.Optional)));

            return list;
        }

        public void InitializeSkills()
        {
            Skills.Clear();
            foreach (var skillDef in DefDatabase<SkillDef>.AllDefsListForReading
                .OrderByDescending(s => s.listOrder))
            {
                Skills.Add(new SkillFilter
                {
                    Skill = skillDef,
                    MinLevel = 0,
                    MinPassion = Passion.None
                });
            }
        }

        public void Reset()
        {
            foreach (var skill in Skills)
            {
                skill.MinLevel = 0;
                skill.MinPassion = Passion.None;
            }
            Traits.Clear();
            foreach (var criterion in Criteria)
                criterion.Reset(this);
        }

        public bool HasActiveFilters()
        {
            if (Skills.Any(s => s.IsActive))
                return true;
            if (Traits.Count > 0)
                return true;
            foreach (var criterion in Criteria)
                if (criterion.IsActive(this))
                    return true;
            return false;
        }

        public int GetActiveFilterCount()
        {
            int count = 0;
            count += Skills.Count(s => s.IsActive);
            count += Traits.Count;
            foreach (var criterion in Criteria)
                if (criterion.IsActive(this))
                    count++;
            return count;
        }

        public bool Evaluate(Pawn pawn)
        {
            if (pawn == null) return false;

            // Baby pawns skip skill/trait checks
            bool isBaby = ModsConfig.BiotechActive && pawn.DevelopmentalStage.Baby();

            if (!isBaby)
            {
                if (!CheckSkills(pawn)) return false;
                foreach (var criterion in Criteria)
                {
                    if (criterion.SkippedForBaby && criterion.EvaluatePawn != null && !criterion.EvaluatePawn(this, pawn))
                        return false;
                }
                if (!CheckTraits(pawn)) return false;
            }

            foreach (var criterion in Criteria)
            {
                if (!criterion.SkippedForBaby && criterion.EvaluatePawn != null && !criterion.EvaluatePawn(this, pawn))
                    return false;
            }

            return true;
        }

        private bool CheckSkills(Pawn pawn)
        {
            if (pawn.skills == null) return true;

            foreach (var filter in Skills)
            {
                if (!filter.IsActive) continue;

                var skill = pawn.skills.GetSkill(filter.Skill);
                if (skill == null || skill.TotallyDisabled) return false;

                if (filter.MinLevel > 0 && skill.Level < filter.MinLevel)
                    return false;

                if (filter.MinPassion != Passion.None && (int)skill.passion < (int)filter.MinPassion)
                    return false;
            }

            return true;
        }

        private bool CheckPassionRange(Pawn pawn)
        {
            if (PassionMin <= 0 && PassionMax >= 12) return true;
            if (pawn.skills == null) return true;

            int totalPassions = pawn.skills.skills
                .Count(s => !s.TotallyDisabled && s.passion != Passion.None);
            return totalPassions >= PassionMin && totalPassions <= PassionMax;
        }

        private bool CheckSkillPoints(Pawn pawn)
        {
            if (SkillPointsMin <= 0 && SkillPointsMax >= 240) return true;
            if (pawn.skills == null) return true;

            int totalPoints = 0;
            bool shootingCounted = false;

            foreach (var skill in pawn.skills.skills)
            {
                if (skill.TotallyDisabled) continue;

                if (CountOnlyHighestAttack &&
                    (skill.def == SkillDefOf.Shooting || skill.def == SkillDefOf.Melee))
                {
                    if (!shootingCounted)
                    {
                        var shooting = pawn.skills.GetSkill(SkillDefOf.Shooting);
                        var melee = pawn.skills.GetSkill(SkillDefOf.Melee);
                        int shootingLevel = (shooting != null && !shooting.TotallyDisabled) ? shooting.Level : 0;
                        int meleeLevel = (melee != null && !melee.TotallyDisabled) ? melee.Level : 0;
                        totalPoints += Math.Max(shootingLevel, meleeLevel);
                        shootingCounted = true;
                    }
                    continue;
                }

                if (CountOnlyPassionSkills && skill.passion == Passion.None)
                    continue;

                totalPoints += skill.Level;
            }

            return totalPoints >= SkillPointsMin && totalPoints <= SkillPointsMax;
        }

        private bool CheckTraits(Pawn pawn)
        {
            if (pawn.story?.traits == null) return Traits.Count == 0;

            foreach (var filter in Traits)
            {
                bool hasTrait = pawn.story.traits.HasTrait(filter.Def, filter.Degree);

                if (filter.Mode == TraitFilterMode.Required && !hasTrait)
                    return false;
                if (filter.Mode == TraitFilterMode.Excluded && hasTrait)
                    return false;
            }

            // Check optional trait pool
            if (RequiredTraitsInPool > 0)
            {
                var optionalTraits = Traits.Where(t => t.Mode == TraitFilterMode.Optional);
                int poolMatches = optionalTraits.Count(t => pawn.story.traits.HasTrait(t.Def, t.Degree));
                if (poolMatches < RequiredTraitsInPool)
                    return false;
            }

            return true;
        }

        private bool CheckAge(Pawn pawn)
        {
            if (AgeMin <= 0 && AgeMax >= 120) return true;

            float age = pawn.ageTracker.AgeBiologicalYearsFloat;
            return age >= AgeMin && age <= AgeMax;
        }

        private bool CheckGender(Pawn pawn)
        {
            if (!Gender.HasValue) return true;
            return pawn.gender == Gender.Value;
        }

        private static bool IsGeneAffected(Hediff hediff)
        {
            if (!ModsConfig.BiotechActive) return false;
            return hediff is Hediff_ChemicalDependency chemDep && chemDep.LinkedGene != null;
        }

        private bool CheckHealth(Pawn pawn)
        {
            if (Health == HealthFilterMode.AllowAll) return true;
            if (pawn.health?.hediffSet == null) return true;

            var hediffs = pawn.health.hediffSet.hediffs;

            switch (Health)
            {
                case HealthFilterMode.AllowNone:
                    foreach (var hediff in hediffs)
                    {
                        if (!IsGeneAffected(hediff))
                            return false;
                    }
                    return true;

                case HealthFilterMode.OnlyStartCondition:
                    foreach (var hediff in hediffs)
                    {
                        if (IsGeneAffected(hediff))
                            continue;
                        if (hediff.def != HediffDefOf.CryptosleepSickness &&
                            hediff.def != HediffDefOf.Malnutrition)
                            return false;
                    }
                    return true;

                case HealthFilterMode.NoPain:
                    foreach (var hediff in hediffs)
                    {
                        if (IsGeneAffected(hediff))
                            continue;
                        var stage = hediff.CurStage;
                        if (stage != null && stage.painOffset > 0f)
                            return false;
                    }
                    return true;

                case HealthFilterMode.NoAddiction:
                    foreach (var hediff in hediffs)
                    {
                        if (IsGeneAffected(hediff))
                            continue;
                        if (hediff is Hediff_Addiction)
                            return false;
                    }
                    return true;

                default:
                    return true;
            }
        }

        // Fast reroll helper methods - expose individual checks for step-by-step filtering
        public bool CheckAgeForFastReroll(Pawn pawn) => CheckAge(pawn) && CheckGender(pawn);
        public bool CheckSkillsAndTraitsForFastReroll(Pawn pawn) =>
            CheckSkills(pawn) && CheckPassionRange(pawn) && CheckSkillPoints(pawn) && CheckTraits(pawn);
        public bool CheckHealthForFastReroll(Pawn pawn) => CheckHealth(pawn);
        public bool CheckWorkForFastReroll(Pawn pawn) => CheckWork(pawn);

        private bool CheckWork(Pawn pawn)
        {
            switch (Work)
            {
                case WorkFilterMode.AllowAll:
                    return true;

                case WorkFilterMode.AllowNone:
                    return pawn.CombinedDisabledWorkTags == WorkTags.None;

                case WorkFilterMode.NoDumbLabor:
                    return (pawn.CombinedDisabledWorkTags & WorkTags.ManualDumb) == 0;

                default:
                    return true;
            }
        }

        public PawnFilter Clone()
        {
            var clone = new PawnFilter();
            foreach (var criterion in Criteria)
                criterion.CopyFrom(clone, this);

            foreach (var skill in Skills)
            {
                clone.Skills.Add(new SkillFilter
                {
                    Skill = skill.Skill,
                    MinLevel = skill.MinLevel,
                    MinPassion = skill.MinPassion
                });
            }

            foreach (var trait in Traits)
            {
                clone.Traits.Add(new TraitFilter
                {
                    Def = trait.Def,
                    Degree = trait.Degree,
                    Mode = trait.Mode
                });
            }

            return clone;
        }

        public void CopyFrom(PawnFilter source)
        {
            foreach (var criterion in Criteria)
                criterion.CopyFrom(this, source);

            Skills.Clear();
            foreach (var skill in source.Skills)
            {
                Skills.Add(new SkillFilter
                {
                    Skill = skill.Skill,
                    MinLevel = skill.MinLevel,
                    MinPassion = skill.MinPassion
                });
            }

            Traits.Clear();
            foreach (var trait in source.Traits)
            {
                Traits.Add(new TraitFilter
                {
                    Def = trait.Def,
                    Degree = trait.Degree,
                    Mode = trait.Mode
                });
            }
        }
    }

    public static class PawnFilterData
    {
        private static PawnFilter activeFilter = new PawnFilter();

        public static int LastRerollAttempts { get; set; }
        public static bool LastRerollSucceeded { get; set; }

        public static PawnFilter ActiveFilter => activeFilter;

        public static bool HasActiveFilters()
        {
            return activeFilter.HasActiveFilters();
        }

        public static void Initialize()
        {
            activeFilter = new PawnFilter();
            activeFilter.InitializeSkills();
            LastRerollAttempts = 0;
            LastRerollSucceeded = true;
        }

        public static void Reset()
        {
            activeFilter.Reset();
        }
    }
}
