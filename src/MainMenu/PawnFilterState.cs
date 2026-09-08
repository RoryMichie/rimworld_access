using System.Collections.Generic;
using System.Linq;
using RimWorld;
using Verse.Sound;

namespace RimWorldAccess
{
    /// <summary>
    /// What the scope must say after an activated filter row: the state mutates, the
    /// scope announces (the S1 StartingPawnState split).
    /// </summary>
    public enum FilterRowOutcome
    {
        /// <summary>Nothing to say here — a picker or prompt opened, or the row refused and the reject cue already played.</summary>
        Silent,

        /// <summary>The row's own value changed; the scope speaks the new state.</summary>
        RowChanged,

        /// <summary>Every row was rebuilt from a wholesale change; the scope returns to the first row and reads it.</summary>
        ListRebuilt,
    }

    /// <summary>
    /// Data and mutation vehicles for the pawn filter editor: skills, traits, demographics,
    /// conditions, settings, and action rows, all built by <see cref="PawnFilterHelper.BuildMenuItems"/>
    /// into <see cref="FilterMenuItem"/>s whose <c>Label</c> is ALREADY a fully composed,
    /// whole-phrase-translated string (e.g. "Age, minimum: 18") — every criterion's
    /// Format() fuses name and value into one phrase.
    ///
    /// Shares the S1 <see cref="StartingPawnState"/> shape: lifecycle, data, mutation.
    /// The row cursor, the typeahead, the section-jump
    /// arithmetic and every focus announcement now live on
    /// <see cref="RimWorldAccess.Shell.PawnFilterScope"/>, a real
    /// <see cref="RimWorldAccess.Shell.ScreenScope"/>. The helper's section-header items are
    /// not rows here either: <see cref="RowSections"/> carries each row's section name, which
    /// the scope speaks as a landing prefix and pages between with PageUp/PageDown.
    /// </summary>
    public static class PawnFilterState
    {
        private static bool isActive;
        private static PawnFilter workingCopy;
        private static readonly List<FilterMenuItem> rows = new List<FilterMenuItem>();
        private static readonly List<string> rowSections = new List<string>();
        private static bool cursorHome;

        public static bool IsActive => isActive;

        /// <summary>The editor's navigable rows, in draw order; section headers are not among them.</summary>
        public static IReadOnlyList<FilterMenuItem> Rows => rows;

        /// <summary>The section each row in <see cref="Rows"/> belongs to, by the same index.</summary>
        public static IReadOnlyList<string> RowSections => rowSections;

        /// <summary>How many criteria the working copy currently filters on (the open announcement's count).</summary>
        public static int ActiveFilterCount => workingCopy == null ? 0 : workingCopy.GetActiveFilterCount();

        public static void Open()
        {
            if (isActive) return;

            // Ensure active filter is initialized
            if (PawnFilterData.ActiveFilter.Skills.Count == 0)
                PawnFilterData.ActiveFilter.InitializeSkills();

            // Create working copy for save/discard behavior
            workingCopy = PawnFilterData.ActiveFilter.Clone();
            isActive = true;
            RebuildRows();
            cursorHome = true;
            // The opening announcement is the scope's own job (PawnFilterScope's
            // ComposeOpenAnnouncement) — see this class's remarks.
        }

        public static void Close(bool save)
        {
            if (!isActive) return;

            if (save)
            {
                PawnFilterData.ActiveFilter.CopyFrom(workingCopy);
                int filterCount = PawnFilterData.ActiveFilter.GetActiveFilterCount();
                TolkHelper.Speak("RimWorldAccess.PawnFilter.FilterSaved".Loc(filterCount));
            }
            else
            {
                TolkHelper.Speak("RimWorldAccess.PawnFilter.EditorClosedDiscarded".Loc());
            }

            isActive = false;
            workingCopy = null;
            rows.Clear();
            rowSections.Clear();
            cursorHome = false;
        }

        public static void SaveAndClose() => Close(save: true);

        /// <summary>
        /// True once per change that must send the cursor back to the first row — the
        /// editor opening, Clear all, a loaded preset. Consumed by the scope's focus pass,
        /// which is also where a preset loaded through the (asynchronous) picker callback
        /// lands.
        /// </summary>
        public static bool ConsumeCursorHome()
        {
            bool pending = cursorHome;
            cursorHome = false;
            return pending;
        }

        private static void RebuildRows()
        {
            rows.Clear();
            rowSections.Clear();
            string section = "";
            foreach (FilterMenuItem item in PawnFilterHelper.BuildMenuItems(workingCopy))
            {
                if (item.IsSectionHeader)
                {
                    section = item.Label;
                    continue;
                }
                rows.Add(item);
                rowSections.Add(section);
            }
        }

        // ===== VALUE ADJUSTMENT =====

        /// <summary>Left/Right on a row (Shift scales the step); false when the row has no value to adjust.</summary>
        public static bool AdjustValue(int row, int direction, bool shift)
        {
            if (row < 0 || row >= rows.Count) return false;
            var item = rows[row];

            // Skill items adjust a per-skill value, not a filter-level one — stays special-cased
            // (there's exactly one FilterItemType.Skill case regardless of skill count, so it
            // never had the "one case per criterion" problem the registry below exists to retire).
            if (item.ItemType == FilterItemType.Skill)
            {
                PawnFilterHelper.AdjustSkillLevel(item.SkillFilter, direction * (shift ? 5 : 1));
                item.Label = PawnFilterHelper.FormatSkillLabel(item.SkillFilter);
                return true;
            }

            var criterion = PawnFilter.FindCriterion(item.ItemType);
            if (criterion == null || !criterion.SupportsAdjust)
            {
                SoundDefOf.ClickReject.PlayOneShotOnCamera();
                return false;
            }

            criterion.Adjust(workingCopy, direction, shift);
            item.Label = criterion.Format(workingCopy);
            RefreshLinkedLabel(criterion);
            return true;
        }

        /// <summary>Shift+Home/Shift+End on a row: its slider to minimum/maximum.</summary>
        public static bool JumpToExtreme(int row, bool isMax)
        {
            if (row < 0 || row >= rows.Count) return false;
            var item = rows[row];

            if (item.ItemType == FilterItemType.Skill)
            {
                item.SkillFilter.MinLevel = isMax ? 20 : 0;
                item.Label = PawnFilterHelper.FormatSkillLabel(item.SkillFilter);
                return true;
            }

            var criterion = PawnFilter.FindCriterion(item.ItemType);
            if (criterion == null || !criterion.SupportsJumpToExtreme)
            {
                SoundDefOf.ClickReject.PlayOneShotOnCamera();
                return false;
            }

            criterion.JumpToExtreme(workingCopy, isMax);
            item.Label = criterion.Format(workingCopy);
            RefreshLinkedLabel(criterion);
            return true;
        }

        /// <summary>Re-formats the row a just-adjusted criterion is paired with (a min/max twin).</summary>
        private static void RefreshLinkedLabel(PawnFilterCriterion criterion)
        {
            if (!criterion.LinkedItemType.HasValue)
                return;
            var linked = PawnFilter.FindCriterion(criterion.LinkedItemType.Value);
            for (int i = 0; i < rows.Count; i++)
            {
                if (rows[i].ItemType == criterion.LinkedItemType.Value)
                {
                    rows[i].Label = linked.Format(workingCopy);
                    break;
                }
            }
        }

        // ===== ACTIVATION =====

        /// <summary>Enter on a row: the vanilla-equivalent mutation, or the flow the row opens.</summary>
        public static FilterRowOutcome Activate(int row)
        {
            if (row < 0 || row >= rows.Count) return FilterRowOutcome.Silent;
            var item = rows[row];

            switch (item.ItemType)
            {
                case FilterItemType.Skill:
                    PawnFilterHelper.CyclePassion(item.SkillFilter);
                    item.Label = PawnFilterHelper.FormatSkillLabel(item.SkillFilter);
                    return FilterRowOutcome.RowChanged;

                case FilterItemType.AddRequiredTrait:
                    return OpenTraitPicker(TraitFilterMode.Required);

                case FilterItemType.AddExcludedTrait:
                    return OpenTraitPicker(TraitFilterMode.Excluded);

                case FilterItemType.AddOptionalTrait:
                    return OpenTraitPicker(TraitFilterMode.Optional);

                case FilterItemType.CountOnlyHighestAttack:
                    workingCopy.CountOnlyHighestAttack = !workingCopy.CountOnlyHighestAttack;
                    item.Label = PawnFilterHelper.FormatCountOnlyHighestAttackLabel(workingCopy);
                    return FilterRowOutcome.RowChanged;

                case FilterItemType.CountOnlyPassionSkills:
                    workingCopy.CountOnlyPassionSkills = !workingCopy.CountOnlyPassionSkills;
                    item.Label = PawnFilterHelper.FormatCountOnlyPassionSkillsLabel(workingCopy);
                    return FilterRowOutcome.RowChanged;

                case FilterItemType.SavePreset:
                    PawnFilterPresetSaveState.Open(workingCopy);
                    return FilterRowOutcome.Silent;

                case FilterItemType.LoadPreset:
                    PawnFilterPresetLoadState.Open(ApplyLoadedPreset);
                    return FilterRowOutcome.Silent;

                case FilterItemType.ClearAll:
                    workingCopy.Reset();
                    workingCopy.InitializeSkills();
                    RebuildRows();
                    TolkHelper.Speak("ClearAll".Loc());
                    return FilterRowOutcome.ListRebuilt;

                default:
                    // For items that use Left/Right, Enter/Space does nothing special
                    SoundDefOf.ClickReject.PlayOneShotOnCamera();
                    return FilterRowOutcome.Silent;
            }
        }

        private static void ApplyLoadedPreset(PawnFilter loadedFilter)
        {
            if (loadedFilter == null)
                return;
            workingCopy.CopyFrom(loadedFilter);
            workingCopy.InitializeSkills();
            // Re-copy skill filters from loaded data
            foreach (var loadedSkill in loadedFilter.Skills)
            {
                var matchingSkill = workingCopy.Skills.FirstOrDefault(
                    s => s.Skill == loadedSkill.Skill);
                if (matchingSkill != null)
                {
                    matchingSkill.MinLevel = loadedSkill.MinLevel;
                    matchingSkill.MinPassion = loadedSkill.MinPassion;
                }
            }
            RebuildRows();
            cursorHome = true;
            TolkHelper.Speak("RimWorldAccess.PawnFilter.PresetLoaded".Loc(workingCopy.GetActiveFilterCount()));
        }

        private static FilterRowOutcome OpenTraitPicker(TraitFilterMode mode)
        {
            var options = PawnFilterHelper.BuildTraitPickerOptions(workingCopy, mode, () =>
            {
                string modeLabel = PawnFilterHelper.GetTraitModeLabel(mode);
                var lastTrait = workingCopy.Traits.Last();
                TolkHelper.Speak("RimWorldAccess.PawnFilter.TraitAdded".Loc(modeLabel, lastTrait.Label));
                RebuildRows();
            });

            if (options.Count == 0)
            {
                TolkHelper.Speak("RimWorldAccess.PawnFilter.NoAvailableTraits".Loc());
                return FilterRowOutcome.Silent;
            }

            WindowlessFloatMenuState.Open(options, colonistOrders: false);
            return FilterRowOutcome.Silent;
        }

        /// <summary>Delete on a row: removes that trait entry; false when the row is not one.</summary>
        public static bool DeleteTrait(int row)
        {
            if (row < 0 || row >= rows.Count) return false;
            var item = rows[row];

            if (item.ItemType != FilterItemType.TraitEntry || item.TraitFilter == null)
            {
                SoundDefOf.ClickReject.PlayOneShotOnCamera();
                return false;
            }

            string modeLabel = PawnFilterHelper.GetTraitModeLabel(item.TraitFilter.Mode);
            string traitLabel = item.TraitFilter.Label;
            workingCopy.Traits.Remove(item.TraitFilter);
            TolkHelper.Speak("RimWorldAccess.PawnFilter.TraitRemoved".Loc(modeLabel, traitLabel));

            RebuildRows();
            return true;
        }
    }
}
