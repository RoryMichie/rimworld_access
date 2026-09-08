using System;
using System.Collections.Generic;
using System.Linq;
using RimWorld;
using RimWorldAccess.Shell;
using Verse;

namespace RimWorldAccess
{
    /// <summary>
    /// Adapter for <c>CharacterEditor.DialogAddHediff</c>, both add and edit modes (the
    /// SAME dialog type/constructor overload the mod itself uses for both -- see
    /// <see cref="CharEditorCompat.AddHediff"/>/<see cref="CharEditorCompat.EditHediff"/>). Three
    /// ComboBox filters (mod, category, body part); results are the dialog's own filtered
    /// candidate list; selection is a raw field write immediately reconciled (see
    /// <see cref="CharEditorHealthBrowserCompat"/>'s class remarks) so the Parameters region's
    /// gating is always fresh. CONFIRM invokes the dialog's own gated <c>CheckAndDo</c> (never
    /// <c>OnAcceptKeyPressed</c> -- see the same class remarks) which may CHAIN
    /// <c>DialogChoosePart</c>/<c>DialogChoosePawn</c> instead of closing; both chained dialogs are
    /// registered against their own adapters below, and their own Confirm writes back into THIS
    /// dialog's <c>SelectedPart</c>/<c>SelectedPawn</c> property setters, which re-run
    /// <c>CheckIsReady</c> and continue the chain automatically -- no extra code is needed here for
    /// the chain to close.
    /// </summary>
    internal sealed class AddHediffAdapter : CharEditorBrowserAdapterBase
    {
        private enum ParamKind { Override, Severity, Level, Pain, Duration, Permanent }

        private readonly TextFieldEditSession paramSession = new TextFieldEditSession();

        public AddHediffAdapter(Window dialog)
            : base(dialog)
        {
        }

        public override string Title => CharEditorHealthBrowserCompat.HediffInEditMode(dialog)
            ? "RimWorldAccess.CharEd.Browser.EditHediffTitle".Translate().ToString()
            : "RimWorldAccess.CharEd.Browser.AddHediffTitle".Translate().ToString();

        protected override IReadOnlyList<CharEditorBrowserFilter> BuildFilters()
        {
            return new List<CharEditorBrowserFilter>
            {
                ModNameCombo(CharEditorHealthBrowserCompat.HediffModNames,
                    () => CharEditorHealthBrowserCompat.HediffModName(dialog),
                    name => CharEditorHealthBrowserCompat.HediffSetModName(dialog, name)),
                Combo("RimWorldAccess.CharEd.Browser.CategoryFilter".Translate(),
                    () => CharEditorHealthBrowserCompat.HediffCategoryCandidates(dialog),
                    delegate
                    {
                        string current = CharEditorHealthBrowserCompat.HediffCategory(dialog);
                        return CharEditorHealthBrowserCompat.HediffCategoryCandidates(dialog).FindIndex(c => c == current);
                    },
                    delegate(int candidateIndex)
                    {
                        List<string> candidates = CharEditorHealthBrowserCompat.HediffCategoryCandidates(dialog);
                        if (candidateIndex >= 0 && candidateIndex < candidates.Count)
                            CharEditorHealthBrowserCompat.HediffSetCategory(dialog, candidates[candidateIndex]);
                    }),
                Combo("RimWorldAccess.CharEd.Browser.Health.BodyPartFilter".Translate(),
                    () => CharEditorHealthBrowserCompat.HediffBodyPartCandidates(dialog),
                    delegate
                    {
                        string current = CharEditorHealthBrowserCompat.HediffBodyPartFilter(dialog);
                        return CharEditorHealthBrowserCompat.HediffBodyPartCandidates(dialog).FindIndex(c => c == current);
                    },
                    delegate(int candidateIndex)
                    {
                        List<string> candidates = CharEditorHealthBrowserCompat.HediffBodyPartCandidates(dialog);
                        if (candidateIndex >= 0 && candidateIndex < candidates.Count)
                            CharEditorHealthBrowserCompat.HediffSetBodyPartFilter(dialog, candidates[candidateIndex]);
                    }),
            };
        }

        public override int ResultCount => CharEditorHealthBrowserCompat.HediffResults(dialog).Count;

        public override string DescribeResultLabel(int index)
        {
            var results = CharEditorHealthBrowserCompat.HediffResults(dialog);
            return index >= 0 && index < results.Count ? results[index].LabelCap.ToString() : "";
        }

        /// <summary>The dialog's own tooltip formula (FHediffTooltip: mod name plus description) -- plain public vanilla reads, no reflection needed.</summary>
        public override string DescribeResultTooltip(int index)
        {
            var results = CharEditorHealthBrowserCompat.HediffResults(dialog);
            if (index < 0 || index >= results.Count)
                return null;
            HediffDef def = results[index];
            string modName = def.modContentPack?.Name;
            return modName.NullOrEmpty() ? def.description : modName + "\n" + def.description;
        }

        public override int SelectedResultIndex
        {
            get
            {
                var results = CharEditorHealthBrowserCompat.HediffResults(dialog);
                HediffDef selected = CharEditorHealthBrowserCompat.HediffSelected(dialog);
                return results.FindIndex(d => d == selected);
            }
        }

        /// <summary>Writes the raw selection field, then reconciles synchronously (see CharEditorHealthBrowserCompat's class remarks) so the Parameters region reads fresh gating on the very next describe.</summary>
        public override void SelectResult(int index)
        {
            var results = CharEditorHealthBrowserCompat.HediffResults(dialog);
            if (index < 0 || index >= results.Count)
                return;
            CharEditorHealthBrowserCompat.HediffSetSelected(dialog, results[index]);
            CharEditorHealthBrowserCompat.HediffReconcileSelection(dialog);
        }

        // ------------------------------------------------------------------
        // Parameters: Override (always), then Severity/Level/Pain/Duration/Permanent, each present
        // only when the dialog's own already-reconciled state says so.
        // ------------------------------------------------------------------

        private List<ParamKind> ActiveParams()
        {
            var list = new List<ParamKind> { ParamKind.Override };
            HediffDef def = CharEditorHealthBrowserCompat.HediffSelected(dialog);
            if (def == null)
                return list;
            bool adjustable = CharEditorHealthBrowserCompat.HediffIsAdjustable(dialog);
            bool overridden = CharEditorHealthBrowserCompat.HediffOverrideActive();
            if (adjustable || overridden)
                list.Add(ParamKind.Severity);
            if (CharEditorCompat.HediffDefHasLevel(def))
                list.Add(ParamKind.Level);
            if (CharEditorHealthBrowserCompat.HediffHasPainComp(dialog))
                list.Add(ParamKind.Pain);
            if (CharEditorHealthBrowserCompat.HediffHasDurationComp(dialog))
                list.Add(ParamKind.Duration);
            if ((adjustable || overridden) && (CharEditorHealthBrowserCompat.HediffInjuryProps(dialog) || overridden))
                list.Add(ParamKind.Permanent);
            return list;
        }

        public override int ParameterCount => ActiveParams().Count;

        private static readonly string[] PainLabels =
        {
            "RimWorldAccess.CharEd.Browser.Health.PainNone",
            "RimWorldAccess.CharEd.Browser.Health.PainLow",
            "RimWorldAccess.CharEd.Browser.Health.PainMedium",
            "RimWorldAccess.CharEd.Browser.Health.PainHigh",
        };

        public override ElementDescription DescribeParameterRow(int index)
        {
            var d = new ElementDescription();
            List<ParamKind> kinds = ActiveParams();
            if (index < 0 || index >= kinds.Count)
                return d;
            switch (kinds[index])
            {
                case ParamKind.Override:
                    d.Label = "RimWorldAccess.CharEd.Browser.Health.Override".Translate();
                    d.Role = ElementRole.Checkbox;
                    d.Check = CharEditorHealthBrowserCompat.HediffOverrideActive() ? CheckState.Checked : CheckState.Unchecked;
                    d.Extras = "RimWorldAccess.CharEd.Browser.Health.OverrideDesc".Translate();
                    break;
                case ParamKind.Severity:
                {
                    float value = CharEditorHealthBrowserCompat.HediffSeverity(dialog);
                    float lethal = CharEditorHealthBrowserCompat.HediffLethalSeverity(dialog);
                    d.Label = SeverityLabel();
                    d.Role = ElementRole.Slider;
                    d.Value = value.ToString("0.##");
                    if (lethal >= 0f && value >= lethal)
                        d.Extras = "RimWorldAccess.CharEd.Browser.Health.LethalWarning".Translate();
                    break;
                }
                case ParamKind.Level:
                    d.Label = "Level".Translate();
                    d.Role = ElementRole.Stepper;
                    d.Value = CharEditorHealthBrowserCompat.HediffLevel(dialog).ToString();
                    break;
                case ParamKind.Pain:
                {
                    int val = CharEditorHealthBrowserCompat.HediffPainSliderValue(dialog);
                    int clamped = Math.Max(0, Math.Min(3, val));
                    d.Label = "RimWorldAccess.CharEd.Browser.Health.Pain".Translate();
                    d.Role = ElementRole.Stepper;
                    d.Value = PainLabels[clamped].Translate();
                    d.AtMinimum = clamped <= 0;
                    d.AtMaximum = clamped >= 3;
                    break;
                }
                case ParamKind.Duration:
                {
                    int ticks = Math.Max(0, CharEditorHealthBrowserCompat.HediffDuration(dialog));
                    d.Label = "RimWorldAccess.CharEd.Browser.Health.Duration".Translate();
                    d.Role = ElementRole.Stepper;
                    d.Value = ticks.ToStringTicksToPeriod();
                    d.AtMinimum = ticks <= 0;
                    d.AtMaximum = ticks >= MaxDurationTicks;
                    string extra = CharEditorHealthBrowserCompat.HediffExtraTipString(dialog);
                    if (!extra.NullOrEmpty())
                        d.Extras = extra;
                    break;
                }
                case ParamKind.Permanent:
                    d.Label = "Permanent".Translate().ToString().CapitalizeFirst();
                    d.Role = ElementRole.Checkbox;
                    d.Check = CharEditorHealthBrowserCompat.HediffIsPermanent(dialog) ? CheckState.Checked : CheckState.Unchecked;
                    break;
            }
            return d;
        }

        /// <summary>The mod's own duration slider ceiling (DialogAddHediff.DrawAdjustableTime, CEditor.cs).</summary>
        private const int MaxDurationTicks = 220000;

        /// <summary>Duration step granularity: one in-game hour (2500 ticks) -- a meaningful unit given the 0-220000 tick range, matching the tweak-values idiom used elsewhere (e.g. VfPainterScope's HsvStep) rather than a per-tick step.</summary>
        private const int DurationStepTicks = 2500;

        /// <summary>Severity step granularity: 1/20th of the current range, matching the PsycastStepDivisor idiom (CharacterEditorScope) since a fixed increment cannot work across every hediff's wildly different severity scale.</summary>
        private const float SeverityStepDivisor = 20f;

        public override bool CanAdjustParameterRow(int index)
        {
            List<ParamKind> kinds = ActiveParams();
            if (index < 0 || index >= kinds.Count)
                return false;
            return kinds[index] == ParamKind.Severity || kinds[index] == ParamKind.Level
                || kinds[index] == ParamKind.Pain || kinds[index] == ParamKind.Duration;
        }

        public override void AdjustParameterRow(int index, int direction)
        {
            List<ParamKind> kinds = ActiveParams();
            if (index < 0 || index >= kinds.Count)
                return;
            switch (kinds[index])
            {
                case ParamKind.Severity:
                {
                    float min = CharEditorHealthBrowserCompat.HediffMinSeverity(dialog);
                    float max = CharEditorHealthBrowserCompat.HediffMaxSeverity(dialog);
                    float step = Math.Max(0.01f, (max - min) / SeverityStepDivisor);
                    float value = CharEditorHealthBrowserCompat.HediffSeverity(dialog) + direction * step;
                    bool overridden = CharEditorHealthBrowserCompat.HediffOverrideActive();
                    if (!overridden)
                        value = Math.Max(min, Math.Min(max, value));
                    else if (value < min)
                        value = min;
                    CharEditorHealthBrowserCompat.HediffSetSeverity(dialog, value);
                    break;
                }
                case ParamKind.Level:
                {
                    int value = CharEditorHealthBrowserCompat.HediffLevel(dialog) + direction;
                    if (value < 0) value = 0;
                    CharEditorHealthBrowserCompat.HediffSetLevel(dialog, value);
                    break;
                }
                case ParamKind.Pain:
                {
                    int value = Math.Max(0, Math.Min(3, CharEditorHealthBrowserCompat.HediffPainSliderValue(dialog) + direction));
                    CharEditorHealthBrowserCompat.HediffSetPainSliderValue(dialog, value);
                    break;
                }
                case ParamKind.Duration:
                {
                    int value = CharEditorHealthBrowserCompat.HediffDuration(dialog) + direction * DurationStepTicks;
                    if (value < 0) value = 0;
                    if (value > MaxDurationTicks) value = MaxDurationTicks;
                    CharEditorHealthBrowserCompat.HediffSetDuration(dialog, value);
                    break;
                }
            }
        }

        public override void ActivateParameterRow(int index, Action onChanged)
        {
            List<ParamKind> kinds = ActiveParams();
            if (index < 0 || index >= kinds.Count)
                return;
            switch (kinds[index])
            {
                case ParamKind.Override:
                    CharEditorHealthBrowserCompat.HediffToggleOverride(dialog);
                    onChanged();
                    break;
                case ParamKind.Permanent:
                    CharEditorHealthBrowserCompat.HediffSetPermanent(dialog, !CharEditorHealthBrowserCompat.HediffIsPermanent(dialog));
                    onChanged();
                    break;
                case ParamKind.Severity:
                    BeginSeverityEdit(onChanged);
                    break;
                case ParamKind.Level:
                    BeginLevelEdit(onChanged);
                    break;
                case ParamKind.Duration:
                    BeginDurationEdit(onChanged);
                    break;
                case ParamKind.Pain:
                    // Four fixed categories; Left/Right stepping is already minimal, Enter re-reads.
                    onChanged();
                    break;
            }
        }

        // ------------------------------------------------------------------
        // Exact entry (Enter on a numeric parameter row), so
        // severity 0-99 or an 88-hour duration never demands dozens of Left/Right presses.
        // Applied values ride the same clamped setters the stepping path uses.
        // ------------------------------------------------------------------

        /// <summary>
        /// Vanilla's only severity label key is <c>ConfigurableSeverity</c>, a FORMAT string
        /// ("severity: {0}") -- the mod's own slider row takes the part before the colon
        /// (<c>SubstringTo(":")</c>, DialogAddHediff.DrawAdjustableSeverity); mirror that, then
        /// capitalize for row-label style.
        /// </summary>
        private static string SeverityLabel()
        {
            string s = "ConfigurableSeverity".Translate().ToString();
            int colon = s.IndexOf(':');
            if (colon >= 0)
                s = s.Substring(0, colon);
            return s.CapitalizeFirst();
        }

        private void BeginSeverityEdit(Action onChanged)
        {
            float current = CharEditorHealthBrowserCompat.HediffSeverity(dialog);
            // No maxLength: the mod's own control is a slider with no text limit to harvest;
            // range enforcement is the clamped apply below, same bounds as the stepping path.
            var spec = new TextFieldSpec(labelKey: null, maxLength: null, minLength: 0,
                allowedChars: CharEdNumericEntry.UnsignedFloat);
            paramSession.EnterEdit(current.ToString("0.##"), spec,
                SeverityLabel(),
                ApplySeverity,
                onExit: onChanged);
        }

        private void ApplySeverity(string value)
        {
            if (!float.TryParse(value, out float parsed))
                return;
            float min = CharEditorHealthBrowserCompat.HediffMinSeverity(dialog);
            float max = CharEditorHealthBrowserCompat.HediffMaxSeverity(dialog);
            if (!CharEditorHealthBrowserCompat.HediffOverrideActive())
                parsed = Math.Max(min, Math.Min(max, parsed));
            else if (parsed < min)
                parsed = min;
            CharEditorHealthBrowserCompat.HediffSetSeverity(dialog, parsed);
        }

        private void BeginLevelEdit(Action onChanged)
        {
            int current = CharEditorHealthBrowserCompat.HediffLevel(dialog);
            // No maxLength (slider control, nothing to harvest); floor 0, no ceiling -- the mod's
            // own level field has none.
            var spec = new TextFieldSpec(labelKey: null, maxLength: null, minLength: 0,
                allowedChars: CharEdNumericEntry.DigitsOnly);
            CharEdNumericEntry.OpenInt(paramSession, "Level".Translate().ToString(),
                Math.Max(0, current), 0, int.MaxValue,
                v => CharEditorHealthBrowserCompat.HediffSetLevel(dialog, v),
                onExit: onChanged, spec: spec);
        }

        /// <summary>Entry is in HOURS (one step of the mod's own slider = 2500 ticks = one in-game hour), never raw ticks.</summary>
        private void BeginDurationEdit(Action onChanged)
        {
            int ticks = Math.Max(0, CharEditorHealthBrowserCompat.HediffDuration(dialog));
            float hours = ticks / (float)DurationStepTicks;
            var spec = new TextFieldSpec(labelKey: null, maxLength: null, minLength: 0,
                allowedChars: CharEdNumericEntry.UnsignedFloat);
            paramSession.EnterEdit(hours.ToString("0.#"), spec,
                "RimWorldAccess.CharEd.Browser.Health.DurationHoursPrompt".Translate().ToString(),
                value =>
                {
                    if (!float.TryParse(value, out float parsedHours))
                        return;
                    int newTicks = (int)Math.Round(parsedHours * DurationStepTicks);
                    if (newTicks < 0) newTicks = 0;
                    if (newTicks > MaxDurationTicks) newTicks = MaxDurationTicks;
                    CharEditorHealthBrowserCompat.HediffSetDuration(dialog, newTicks);
                },
                onExit: onChanged);
        }

        public override bool Confirm()
        {
            CharEditorHealthBrowserCompat.HediffConfirm(dialog);
            return !Find.WindowStack.IsOpen(dialog);
        }

        public override void CancelPendingEdit()
        {
            paramSession.CancelIfActive();
        }
    }
}
