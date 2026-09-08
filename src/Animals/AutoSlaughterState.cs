using System.Collections.Generic;
using System.Linq;
using RimWorld;
using Verse;
using Verse.Sound;

namespace RimWorldAccess
{
    /// <summary>
    /// Data, mutation and lifecycle for <see cref="Dialog_AutoSlaughter"/>: the per-species config
    /// list and cached counts, the column value-space, the numeric-entry sub-mode, and the
    /// vanilla-vehicle mutation methods. Navigation, typeahead and announcement composition live on
    /// <see cref="RimWorldAccess.Shell.AutoSlaughterScope"/>.
    /// The row/column cursor stays here as plain data the scope writes via <see cref="SetCursor"/>
    /// before calling any mutation method, so each mutation body reads the cell the player is on.
    /// </summary>
    public static class AutoSlaughterState
    {
        #region Data Structures

        private struct AnimalCounts
        {
            public int total;
            public int males;
            public int malesYoung;
            public int females;
            public int femalesYoung;
            public int pregnant;
            public int bonded;
        }

        internal enum Column
        {
            MaxTotal = 0,
            MaxMales = 1,
            MaxMalesYoung = 2,
            MaxFemales = 3,
            MaxFemalesYoung = 4,
            AllowPregnant = 5,
            AllowBonded = 6
        }

        internal const int ColumnCount = 7;

        // Column-name keys, resolved via Translate() at the call sites.
        private static readonly string[] ColumnNameKeys = new[]
        {
            "RimWorldAccess.Animals.AutoSlaughter.Column.MaxTotal",
            "RimWorldAccess.Animals.AutoSlaughter.Column.MaxMales",
            "RimWorldAccess.Animals.AutoSlaughter.Column.MaxMalesYoung",
            "RimWorldAccess.Animals.AutoSlaughter.Column.MaxFemales",
            "RimWorldAccess.Animals.AutoSlaughter.Column.MaxFemalesYoung",
            "RimWorldAccess.Animals.AutoSlaughter.Column.AllowPregnant",
            "RimWorldAccess.Animals.AutoSlaughter.Column.AllowBonded"
        };

        internal static string ColumnName(int index) => ColumnNameKeys[index].Translate().ToString();

        #endregion

        #region State Fields

        public static bool IsActive { get; private set; } = false;

        private static Dialog_AutoSlaughter currentDialog;
        private static List<AutoSlaughterConfig> configs = new List<AutoSlaughterConfig>();
        private static Dictionary<ThingDef, AnimalCounts> cachedCounts = new Dictionary<ThingDef, AnimalCounts>();
        private static int currentRowIndex = 0;
        private static int currentColumnIndex = 0;

        private static bool isNumericInputMode = false;
        private static string numericBuffer = "";

        public static int CurrentRowIndex => currentRowIndex;
        internal static int CurrentColumnIndex => currentColumnIndex;
        public static bool IsNumericInputMode => isNumericInputMode;

        /// <summary>Row count the scope's content-region contract reads.</summary>
        internal static int ConfigCount => configs.Count;

        /// <summary>One row's config, by data-row index (0-based).</summary>
        internal static AutoSlaughterConfig GetConfig(int index) => configs[index];

        /// <summary>
        /// Called right before any mutation or mode-entry method, so their own cursor reads resolve
        /// to the cell the player is on. Bookkeeping only — never a game-state write.
        /// </summary>
        internal static void SetCursor(int row, int column)
        {
            currentRowIndex = row;
            currentColumnIndex = column;
        }

        #endregion

        #region Lifecycle

        public static void Open(Dialog_AutoSlaughter dialog)
        {
            if (IsActive) return;

            currentDialog = dialog;
            RefreshConfigs();

            if (configs.Count == 0)
            {
                TolkHelper.Speak("RimWorldAccess.Animals.AutoSlaughter.Menu.NoAnimals".Loc());
                return;
            }

            currentRowIndex = 0;
            currentColumnIndex = 0;
            isNumericInputMode = false;
            numericBuffer = "";
            IsActive = true;

        }

        public static void Close()
        {
            string slaughterSummary = BuildSlaughterSummary();

            IsActive = false;
            currentDialog = null;
            configs.Clear();
            cachedCounts.Clear();
            currentRowIndex = 0;
            currentColumnIndex = 0;
            isNumericInputMode = false;
            numericBuffer = "";

            if (!string.IsNullOrEmpty(slaughterSummary))
                TolkHelper.Speak("RimWorldAccess.Animals.AutoSlaughter.Menu.ClosedWithSummary".Loc(slaughterSummary));
            else
                TolkHelper.Speak("RimWorldAccess.Animals.AutoSlaughter.Menu.Closed".Loc());
        }

        /// <summary>A closing overview of every animal marked for slaughter, across all species.</summary>
        private static string BuildSlaughterSummary()
        {
            var manager = Find.CurrentMap?.autoSlaughterManager;
            if (manager == null) return null;

            var slaughterList = manager.AnimalsToSlaughter;
            if (slaughterList == null || slaughterList.Count == 0) return null;

            var groups = slaughterList
                .GroupBy(p => p.def)
                .OrderByDescending(g => g.Count())
                .Select(g => "RimWorldAccess.Animals.AutoSlaughter.Summary.Entry".Translate(g.Count(), g.Key.label).ToString())
                .ToList();

            return "RimWorldAccess.Animals.AutoSlaughter.Summary.MarkedForSlaughter".Translate(string.Join(", ", groups)).ToString();
        }

        #endregion

        #region Config and Count Management

        private static void RefreshConfigs()
        {
            configs.Clear();
            cachedCounts.Clear();
            if (Find.CurrentMap?.autoSlaughterManager?.configs == null) return;

            var manager = Find.CurrentMap.autoSlaughterManager;

            foreach (var config in manager.configs)
            {
                cachedCounts[config.animal] = ComputeCounts(config);
            }

            // Vanilla's own sort: count descending, then label.
            configs = manager.configs
                .OrderByDescending(c => cachedCounts.TryGetValue(c.animal, out var counts) ? counts.total : 0)
                .ThenBy(c => c.animal.label)
                .ToList();
        }

        /// <summary>
        /// Vanilla's own CountPlayerAnimals logic: bonded animals leave the category counts when
        /// allowSlaughterBonded is false, pregnant females likewise when allowSlaughterPregnant is.
        /// </summary>
        private static AnimalCounts ComputeCounts(AutoSlaughterConfig config)
        {
            var counts = new AnimalCounts();
            var map = Find.CurrentMap;
            if (map?.mapPawns?.SpawnedColonyAnimals == null) return counts;

            foreach (Pawn pawn in map.mapPawns.SpawnedColonyAnimals)
            {
                if (pawn.def != config.animal)
                    continue;

                if (!AutoSlaughterManager.CanEverAutoSlaughter(pawn))
                    continue;

                // Bonded animals always join the bonded tally, but skip the category counts when
                // bonded slaughter is disabled.
                if (pawn.relations != null && pawn.relations.GetDirectRelationsCount(PawnRelationDefOf.Bond) > 0)
                {
                    counts.bonded++;
                    if (!config.allowSlaughterBonded)
                        continue;
                }

                if (pawn.gender == Gender.Male)
                {
                    if (pawn.ageTracker?.CurLifeStage?.reproductive == true)
                        counts.males++;
                    else
                        counts.malesYoung++;
                }
                else if (pawn.gender == Gender.Female)
                {
                    if (pawn.ageTracker?.CurLifeStage?.reproductive == true)
                    {
                        // Visible, not merely HasHediff, to match vanilla.
                        Hediff pregnancyHediff = pawn.health?.hediffSet?.GetFirstHediffOfDef(HediffDefOf.Pregnant);
                        if (pregnancyHediff != null && pregnancyHediff.Visible)
                        {
                            counts.pregnant++;
                            if (!config.allowSlaughterPregnant)
                                continue;
                            counts.females++;
                        }
                        else
                        {
                            counts.females++;
                        }
                    }
                    else
                    {
                        counts.femalesYoung++;
                    }
                }

                counts.total++;
            }

            return counts;
        }

        /// <summary>Refreshes cached counts without re-sorting, for after a toggle changes an allow flag.</summary>
        private static void RefreshAllCounts()
        {
            cachedCounts.Clear();
            foreach (var config in configs)
            {
                cachedCounts[config.animal] = ComputeCounts(config);
            }
        }

        private static AnimalCounts GetCounts(AutoSlaughterConfig config)
        {
            if (cachedCounts.TryGetValue(config.animal, out var counts))
                return counts;
            counts = ComputeCounts(config);
            cachedCounts[config.animal] = counts;
            return counts;
        }

        #endregion

        #region Column Abstraction Helpers

        private static int GetLimitForColumn(AutoSlaughterConfig config, Column column)
        {
            switch (column)
            {
                case Column.MaxTotal: return config.maxTotal;
                case Column.MaxMales: return config.maxMales;
                case Column.MaxMalesYoung: return config.maxMalesYoung;
                case Column.MaxFemales: return config.maxFemales;
                case Column.MaxFemalesYoung: return config.maxFemalesYoung;
                default: return -1;
            }
        }

        // MUTATION-C: mirrors Dialog_AutoSlaughter.DoMaxColumn's direct writes to
        // AutoSlaughterConfig's maxTotal/maxMales/maxMalesYoung/maxFemales/
        // maxFemalesYoung fields (vanilla's WidgetRow.TextFieldNumeric and its
        // infinity/close-X buttons write these bare fields inline; AutoSlaughterConfig
        // exposes no gated setter). Callers reproduce vanilla's own value space: any
        // int clamped to >= 0 via Mathf.Max(0, val), or -1 for "unlimited" (vanilla's
        // close-X button sets val = -1; its infinity button sets val = the current
        // count when leaving -1).
        private static void SetLimitForColumn(AutoSlaughterConfig config, Column column, int value)
        {
            switch (column)
            {
                case Column.MaxTotal: config.maxTotal = value; break;
                case Column.MaxMales: config.maxMales = value; break;
                case Column.MaxMalesYoung: config.maxMalesYoung = value; break;
                case Column.MaxFemales: config.maxFemales = value; break;
                case Column.MaxFemalesYoung: config.maxFemalesYoung = value; break;
            }
        }

        private static int GetCountForColumn(AnimalCounts counts, Column column)
        {
            switch (column)
            {
                case Column.MaxTotal: return counts.total;
                case Column.MaxMales: return counts.males;
                case Column.MaxMalesYoung: return counts.malesYoung;
                case Column.MaxFemales: return counts.females;
                case Column.MaxFemalesYoung: return counts.femalesYoung;
                default: return 0;
            }
        }

        internal static bool IsNumericColumn(Column column)
        {
            return column != Column.AllowPregnant && column != Column.AllowBonded;
        }

        #endregion

        #region Value Adjustment

        /// <summary>
        /// Adjusts the current column's limit. Leaving unlimited starts from the current count, as
        /// vanilla does; a single decrement from 0 returns to unlimited, multi-step decrements clamp.
        /// </summary>
        public static void AdjustValue(int delta)
        {
            if (configs.Count == 0) return;

            var config = configs[currentRowIndex];
            var column = (Column)currentColumnIndex;

            if (!IsNumericColumn(column))
            {
                ToggleBoolean();
                return;
            }

            int currentLimit = GetLimitForColumn(config, column);

            if (currentLimit == -1)
            {
                // Leaving unlimited: start from the current count and apply the delta.
                var counts = GetCounts(config);
                int currentCount = GetCountForColumn(counts, column);
                int newLimit = currentCount + delta;
                if (newLimit < 0) newLimit = 0;
                SetLimitForColumn(config, column, newLimit);
            }
            else
            {
                int newLimit = currentLimit + delta;

                if (delta == -1 && currentLimit == 0)
                {
                    newLimit = -1;
                }
                else if (newLimit < 0)
                {
                    // Multi-step decrements clamp at 0 rather than wrapping to unlimited.
                    newLimit = 0;
                }

                SetLimitForColumn(config, column, newLimit);
            }

            Find.CurrentMap?.autoSlaughterManager?.Notify_ConfigChanged();
            SoundDefOf.DragSlider.PlayOneShotOnCamera();
        }

        // MUTATION-C: mirrors Dialog_AutoSlaughter.DoAnimalRow's direct writes to
        // AutoSlaughterConfig's allowSlaughterPregnant/allowSlaughterBonded fields
        // (vanilla's Widgets.Checkbox(ref config.allowSlaughterPregnant/Bonded, ...)
        // toggles these bare bools inline; AutoSlaughterConfig exposes no gated setter).
        public static void ToggleBoolean()
        {
            if (configs.Count == 0) return;

            var config = configs[currentRowIndex];
            var column = (Column)currentColumnIndex;

            switch (column)
            {
                case Column.AllowPregnant:
                    config.allowSlaughterPregnant = !config.allowSlaughterPregnant;
                    if (config.allowSlaughterPregnant)
                        SoundDefOf.Checkbox_TurnedOn.PlayOneShotOnCamera();
                    else
                        SoundDefOf.Checkbox_TurnedOff.PlayOneShotOnCamera();
                    break;
                case Column.AllowBonded:
                    config.allowSlaughterBonded = !config.allowSlaughterBonded;
                    if (config.allowSlaughterBonded)
                        SoundDefOf.Checkbox_TurnedOn.PlayOneShotOnCamera();
                    else
                        SoundDefOf.Checkbox_TurnedOff.PlayOneShotOnCamera();
                    break;
                default:
                    return;
            }

            Find.CurrentMap?.autoSlaughterManager?.Notify_ConfigChanged();
            RefreshAllCounts();
        }

        public static void SetToUnlimited()
        {
            if (configs.Count == 0) return;

            var config = configs[currentRowIndex];
            var column = (Column)currentColumnIndex;

            if (!IsNumericColumn(column))
                return;

            SetLimitForColumn(config, column, -1);

            Find.CurrentMap?.autoSlaughterManager?.Notify_ConfigChanged();
            SoundDefOf.Checkbox_TurnedOff.PlayOneShotOnCamera();
        }

        public static void SetToZero()
        {
            if (configs.Count == 0) return;

            var config = configs[currentRowIndex];
            var column = (Column)currentColumnIndex;

            if (!IsNumericColumn(column))
                return;

            SetLimitForColumn(config, column, 0);

            Find.CurrentMap?.autoSlaughterManager?.Notify_ConfigChanged();
            SoundDefOf.DragSlider.PlayOneShotOnCamera();
        }

        #endregion

        #region Slaughter Warning

        /// <summary>The game's own slaughter computation for this species, across every limit.</summary>
        private static int GetTotalMarkedForSlaughter(AutoSlaughterConfig config)
        {
            var manager = Find.CurrentMap?.autoSlaughterManager;
            if (manager == null) return 0;
            return manager.AnimalsToSlaughter.Count(p => p.def == config.animal);
        }

        #endregion

        #region Cell Presentation

        /// <summary>The formatted value for one config's column — the scope's ContentCellText source of truth.</summary>
        internal static string GetColumnValueString(AutoSlaughterConfig config, Column column)
        {
            var counts = GetCounts(config);

            switch (column)
            {
                case Column.MaxTotal:
                    return FormatCurrentOfMax(counts.total, config.maxTotal);
                case Column.MaxMales:
                    return FormatCurrentOfMax(counts.males, config.maxMales);
                case Column.MaxMalesYoung:
                    return FormatCurrentOfMax(counts.malesYoung, config.maxMalesYoung);
                case Column.MaxFemales:
                    return FormatCurrentOfMax(counts.females, config.maxFemales);
                case Column.MaxFemalesYoung:
                    return FormatCurrentOfMax(counts.femalesYoung, config.maxFemalesYoung);
                case Column.AllowPregnant:
                    return (config.allowSlaughterPregnant
                        ? "RimWorldAccess.Animals.AutoSlaughter.Value.PregnantAllowed".Translate(counts.pregnant)
                        : "RimWorldAccess.Animals.AutoSlaughter.Value.PregnantNotAllowed".Translate(counts.pregnant)).ToString();
                case Column.AllowBonded:
                    return (config.allowSlaughterBonded
                        ? "RimWorldAccess.Animals.AutoSlaughter.Value.BondedAllowed".Translate(counts.bonded)
                        : "RimWorldAccess.Animals.AutoSlaughter.Value.BondedNotAllowed".Translate(counts.bonded)).ToString();
                default:
                    return "RimWorldAccess.Animals.Value.Unknown".Translate().ToString();
            }
        }

        private static string FormatCurrentOfMax(int current, int max)
        {
            if (max == -1)
            {
                return "RimWorldAccess.Animals.AutoSlaughter.Value.Unlimited".Translate(current).ToString();
            }
            else if (current > max)
            {
                int toSlaughter = current - max;
                return "RimWorldAccess.Animals.AutoSlaughter.Value.OverLimit".Translate(max, current, toSlaughter).ToString();
            }
            else
            {
                return "RimWorldAccess.Animals.AutoSlaughter.Value.WithinLimit".Translate(max, current).ToString();
            }
        }

        #endregion

        #region Numeric Input Mode

        internal static void EnterNumericMode()
        {
            if (configs.Count == 0) return;
            var column = (Column)currentColumnIndex;

            if (!IsNumericColumn(column))
            {
                ToggleBoolean();
                return;
            }

            numericBuffer = "";
            isNumericInputMode = true;
            TolkHelper.Speak("RimWorldAccess.Animals.AutoSlaughter.Numeric.Prompt".Loc());
        }

        public static void HandleNumericDigit(char digit)
        {
            if (!isNumericInputMode) return;

            numericBuffer += digit;
            TolkHelper.SpeakData(numericBuffer, SpeechPriority.Low);
        }

        internal static void HandleNumericBackspace()
        {
            if (!isNumericInputMode || numericBuffer.Length == 0) return;

            numericBuffer = numericBuffer.Substring(0, numericBuffer.Length - 1);
            if (numericBuffer.Length > 0)
                TolkHelper.SpeakData(numericBuffer, SpeechPriority.Low);
            else
                TolkHelper.Speak("RimWorldAccess.Animals.AutoSlaughter.Numeric.Empty".Loc(), SpeechPriority.Low);
        }

        /// <summary>Confirms the typed buffer; mode exit and re-announcement are the scope's job.</summary>
        internal static void ConfirmNumericInput()
        {
            if (!isNumericInputMode) return;

            if (int.TryParse(numericBuffer, out int value) && (value >= 0 || value == -1))
            {
                var config = configs[currentRowIndex];
                var column = (Column)currentColumnIndex;
                SetLimitForColumn(config, column, value);
                Find.CurrentMap?.autoSlaughterManager?.Notify_ConfigChanged();
                SoundDefOf.DragSlider.PlayOneShotOnCamera();
            }
            else
            {
                TolkHelper.Speak("RimWorldAccess.Animals.AutoSlaughter.Numeric.Invalid".Loc());
            }

            isNumericInputMode = false;
            numericBuffer = "";
        }

        internal static void CancelNumericInput()
        {
            isNumericInputMode = false;
            numericBuffer = "";
            TolkHelper.Speak("RimWorldAccess.Animals.AutoSlaughter.Numeric.Cancelled".Loc());
        }

        /// <summary>
        /// Typing "-" to start an unlimited value. Reached through the scope's CharSink on the
        /// literal character, not a keycode claim, since it only means anything mid-buffer; no-ops
        /// once the buffer already holds digits.
        /// </summary>
        internal static void HandleNumericMinusSign()
        {
            if (!isNumericInputMode || numericBuffer.Length != 0) return;
            numericBuffer = "-";
            TolkHelper.Speak("RimWorldAccess.Animals.AutoSlaughter.Numeric.Minus".Loc(), SpeechPriority.Low);
        }

        #endregion

        #region Close Paths

        /// <summary>
        /// Closes ONLY the auto-slaughter dialog, leaving the Animals menu open. Unlike
        /// <see cref="CloseEverything"/> it never calls AnimalsMenuState.Close, and its summary uses
        /// the return phrasing rather than the Escape path's closed-with-summary wording.
        /// </summary>
        public static void ReturnToAnimalsMenu()
        {
            string slaughterSummary = BuildSlaughterSummary();
            Dialog_AutoSlaughter dialog = currentDialog;

            // Clear state before removing the dialog, or PostClose calls Close() again.
            IsActive = false;
            currentDialog = null;
            configs.Clear();
            cachedCounts.Clear();
            currentRowIndex = 0;
            currentColumnIndex = 0;
            isNumericInputMode = false;
            numericBuffer = "";

            if (dialog != null)
                Find.WindowStack.TryRemove(dialog);

            if (!string.IsNullOrEmpty(slaughterSummary))
                TolkHelper.SpeakData(slaughterSummary);
            else
                TolkHelper.Speak("RimWorldAccess.Animals.Menu.ReturnTitle".Loc());
        }

        /// <summary>Escape's base-case close: the dialog and the parent Animals menu together.</summary>
        internal static void CloseEverything()
        {
            string slaughterSummary = BuildSlaughterSummary();
            Dialog_AutoSlaughter dialog = currentDialog;

            // Clear state before removing the dialog, or PostClose calls Close() again.
            IsActive = false;
            currentDialog = null;
            configs.Clear();
            cachedCounts.Clear();
            currentRowIndex = 0;
            currentColumnIndex = 0;
            isNumericInputMode = false;
            numericBuffer = "";

            if (dialog != null)
                Find.WindowStack.TryRemove(dialog);

            bool hasSlaughter = !string.IsNullOrEmpty(slaughterSummary);
            AnimalsMenuState.Close(silent: hasSlaughter);

            if (hasSlaughter)
                TolkHelper.Speak("RimWorldAccess.Animals.Menu.ClosedWithSummary".Loc(slaughterSummary));
        }

        #endregion
    }
}
