using System;
using System.Collections.Generic;
using System.Linq;
using RimWorld;
using RimWorldAccess.Shell;
using Verse;

namespace RimWorldAccess
{
    /// <summary>
    /// Adapter for <c>CharacterEditor.DialogAddThought</c>, opened both unfiltered from
    /// Needs' "Add thought..." row and Social-preset from Social's own "Add thought" icon (see
    /// <see cref="RimWorldAccess.CharEditorThoughtBrowserCompat"/>'s class remarks). Two ComboBox
    /// filters (mod, type); results are the dialog's own currently-filtered set; selection rides
    /// the dialog's own <c>AThoughtSelected</c> onSelect callback, which resets every conditional
    /// field for the new pick. CONDITIONAL Parameters, present only per the dialog's own gates
    /// (<c>ThoughtTool.HasOtherPawnMember</c>/<c>IsForTitle</c>, plain vanilla stage-count and
    /// thought-class reads -- see the facade's class remarks): target pawn (opens the
    /// <c>ChoosePawnAdapter</c>-covered <c>DialogChoosePawn</c>), stage and royal-title combos
    /// (Enter's float menu over the adapter's own candidate list is the only way either changes --
    /// a combo box is a dropdown, so Left/Right leave them alone), and mood/opinion multiplier
    /// sliders (stepping plus exact entry) -- interactive only for the
    /// thought kinds the mod itself makes them interactive for (<c>Thought_Memory</c>/
    /// <c>Thought_MemorySocial</c>), a plain read-only figure otherwise. CONFIRM invokes the
    /// dialog's own gated <c>DoAndClose</c> only when <c>bAllOk</c> (the mod's OK button does not
    /// even draw otherwise) -- NEVER <c>OnAcceptKeyPressed</c>, which has no override here and
    /// would close without adding (the accept-key trap; see the facade's class remarks).
    /// </summary>
    internal sealed class AddThoughtAdapter : CharEditorBrowserAdapterBase
    {
        private enum ParamKind { TargetPawn, Stage, RoyalTitle, Mood, Opinion }

        private readonly TextFieldEditSession paramSession = new TextFieldEditSession();

        public AddThoughtAdapter(Window dialog)
            : base(dialog)
        {
        }

        public override string Title => "RimWorldAccess.CharEd.Social.AddThought".Translate().ToString();

        protected override IReadOnlyList<CharEditorBrowserFilter> BuildFilters()
        {
            return new List<CharEditorBrowserFilter>
            {
                ModNameCombo(() => CharEditorThoughtBrowserCompat.ModNameCandidates(dialog),
                    () => CharEditorThoughtBrowserCompat.ModName(dialog),
                    name => CharEditorThoughtBrowserCompat.SetModName(dialog, name)),
                Combo("RimWorldAccess.CharEd.Browser.TypeFilter".Translate(),
                    () => CharEditorThoughtBrowserCompat.TypeCandidates(dialog).Select(NameOrAll).ToList(),
                    delegate
                    {
                        string current = CharEditorThoughtBrowserCompat.TypeFilter(dialog);
                        return CharEditorThoughtBrowserCompat.TypeCandidates(dialog).FindIndex(t => t == current);
                    },
                    delegate(int candidateIndex)
                    {
                        List<string> candidates = CharEditorThoughtBrowserCompat.TypeCandidates(dialog);
                        if (candidateIndex >= 0 && candidateIndex < candidates.Count)
                            CharEditorThoughtBrowserCompat.SetTypeFilter(dialog, candidates[candidateIndex]);
                    }),
            };
        }

        public override int ResultCount => CharEditorThoughtBrowserCompat.Results(dialog).Count;

        public override string DescribeResultLabel(int index)
        {
            var results = CharEditorThoughtBrowserCompat.Results(dialog);
            return index >= 0 && index < results.Count ? CharEditorThoughtBrowserCompat.ResolvedLabel(dialog, results[index]) : "";
        }

        public override string DescribeResultTooltip(int index)
        {
            var results = CharEditorThoughtBrowserCompat.Results(dialog);
            return index >= 0 && index < results.Count ? CharEditorThoughtBrowserCompat.ResolvedTooltip(dialog, results[index]) : null;
        }

        public override int SelectedResultIndex
        {
            get
            {
                var results = CharEditorThoughtBrowserCompat.Results(dialog);
                ThoughtDef selected = CharEditorThoughtBrowserCompat.Selected(dialog);
                return results.FindIndex(t => t == selected);
            }
        }

        public override void SelectResult(int index)
        {
            var results = CharEditorThoughtBrowserCompat.Results(dialog);
            if (index >= 0 && index < results.Count)
                CharEditorThoughtBrowserCompat.SelectThought(dialog, results[index]);
        }

        // ------------------------------------------------------------------
        // Parameters: conditional per the dialog's own gates -- see class remarks.
        // ------------------------------------------------------------------

        private List<ParamKind> ActiveParams()
        {
            var list = new List<ParamKind>();
            ThoughtDef selected = CharEditorThoughtBrowserCompat.Selected(dialog);
            if (selected == null)
                return list;
            if (CharEditorThoughtBrowserCompat.NeedsOtherPawn(selected))
                list.Add(ParamKind.TargetPawn);
            if (CharEditorThoughtBrowserCompat.NeedsStage(selected))
                list.Add(ParamKind.Stage);
            if (CharEditorThoughtBrowserCompat.NeedsTitle(selected))
                list.Add(ParamKind.RoyalTitle);
            if (CharEditorThoughtBrowserCompat.BaseMoodOffset(dialog) != 0f)
                list.Add(ParamKind.Mood);
            bool social = CharEditorThoughtBrowserCompat.IsSocialMemoryKind(selected);
            float opinion = CharEditorThoughtBrowserCompat.OpinionMultiplier(dialog);
            Pawn target = CharEditorThoughtBrowserCompat.TargetPawn(dialog);
            if (social || opinion != 0f || target != null)
                list.Add(ParamKind.Opinion);
            return list;
        }

        public override int ParameterCount => ActiveParams().Count;

        public override ElementDescription DescribeParameterRow(int index)
        {
            var d = new ElementDescription();
            List<ParamKind> kinds = ActiveParams();
            if (index < 0 || index >= kinds.Count)
                return d;
            ThoughtDef selected = CharEditorThoughtBrowserCompat.Selected(dialog);
            switch (kinds[index])
            {
                case ParamKind.TargetPawn:
                {
                    Pawn target = CharEditorThoughtBrowserCompat.TargetPawn(dialog);
                    d.Label = "RimWorldAccess.CharEd.Browser.Thought.TargetPawn".Translate();
                    d.Role = ElementRole.ComboBox;
                    d.Value = target != null ? target.LabelShortCap : "None".Translate().ToString();
                    break;
                }
                case ParamKind.Stage:
                    d.Label = "RimWorldAccess.CharEd.Browser.Thought.Stage".Translate();
                    d.Role = ElementRole.ComboBox;
                    d.Value = CharEditorThoughtBrowserCompat.StageValue(dialog) ?? "";
                    break;
                case ParamKind.RoyalTitle:
                {
                    RoyalTitleDef title = CharEditorThoughtBrowserCompat.RoyalTitleValue(dialog);
                    d.Label = "RimWorldAccess.CharEd.Character.RoyalTitleRow".Translate();
                    d.Role = ElementRole.ComboBox;
                    d.Value = title != null ? title.LabelCap.ToString() : "None".Translate().ToString();
                    break;
                }
                case ParamKind.Mood:
                {
                    bool interactive = selected != null && CharEditorThoughtBrowserCompat.IsMemoryKind(selected);
                    d.Label = "RimWorldAccess.CharEd.Browser.Thought.Mood".Translate();
                    if (interactive)
                    {
                        float value = CharEditorThoughtBrowserCompat.MoodMultiplier(dialog);
                        d.Role = ElementRole.Slider;
                        d.Value = value.ToString("0.#");
                        d.AtMinimum = value <= MoodMin;
                        d.AtMaximum = value >= MoodMax;
                    }
                    else
                    {
                        float baseMood = CharEditorThoughtBrowserCompat.BaseMoodOffset(dialog);
                        float mult = CharEditorThoughtBrowserCompat.MoodMultiplier(dialog);
                        d.ReadOnly = true;
                        d.Value = ((int)(mult * baseMood)).ToString();
                        d.Extras = "RimWorldAccess.CharEd.Browser.Thought.MoodFixed".Translate();
                    }
                    break;
                }
                case ParamKind.Opinion:
                {
                    bool interactive = selected != null && CharEditorThoughtBrowserCompat.IsSocialMemoryKind(selected);
                    d.Label = "RimWorldAccess.CharEd.Browser.Thought.Opinion".Translate();
                    if (interactive)
                    {
                        float value = CharEditorThoughtBrowserCompat.OpinionMultiplier(dialog);
                        d.Role = ElementRole.Slider;
                        d.Value = value.ToString("0.#");
                        d.AtMinimum = value <= OpinionMin;
                        d.AtMaximum = value >= OpinionMax;
                    }
                    else
                    {
                        d.ReadOnly = true;
                        d.Value = ((int)CharEditorThoughtBrowserCompat.OpinionMultiplier(dialog)).ToString();
                        d.Extras = "RimWorldAccess.CharEd.Browser.Thought.OpinionFixed".Translate();
                    }
                    break;
                }
            }
            return d;
        }

        /// <summary>The mod's own mood-multiplier slider bounds (DialogAddThought.DrawSlider: SimpleMultiplierSlider(..., -4f, 5f)).</summary>
        private const float MoodMin = -4f;
        private const float MoodMax = 5f;

        /// <summary>The mod's own opinion-multiplier slider bounds (DialogAddThought.DrawSlider: SimpleMultiplierSlider(..., -100f, 100f)).</summary>
        private const float OpinionMin = -100f;
        private const float OpinionMax = 100f;

        /// <summary>
        /// Only the two slider params adjust with Left/Right. Stage and royal title are ComboBox
        /// rows -- dropdowns, not sliders -- so Enter/Space open their pickers instead.
        /// </summary>
        public override bool CanAdjustParameterRow(int index)
        {
            List<ParamKind> kinds = ActiveParams();
            if (index < 0 || index >= kinds.Count)
                return false;
            ThoughtDef selected = CharEditorThoughtBrowserCompat.Selected(dialog);
            switch (kinds[index])
            {
                case ParamKind.Mood:
                    return selected != null && CharEditorThoughtBrowserCompat.IsMemoryKind(selected);
                case ParamKind.Opinion:
                    return selected != null && CharEditorThoughtBrowserCompat.IsSocialMemoryKind(selected);
                default:
                    return false;
            }
        }

        public override void AdjustParameterRow(int index, int direction)
        {
            List<ParamKind> kinds = ActiveParams();
            if (index < 0 || index >= kinds.Count)
                return;
            switch (kinds[index])
            {
                case ParamKind.Mood:
                {
                    float next = Clamp(CharEditorThoughtBrowserCompat.MoodMultiplier(dialog) + direction, MoodMin, MoodMax);
                    CharEditorThoughtBrowserCompat.SetMoodMultiplier(dialog, next);
                    break;
                }
                case ParamKind.Opinion:
                {
                    float next = Clamp(CharEditorThoughtBrowserCompat.OpinionMultiplier(dialog) + direction * 5f, OpinionMin, OpinionMax);
                    CharEditorThoughtBrowserCompat.SetOpinionMultiplier(dialog, next);
                    break;
                }
            }
        }

        private static float Clamp(float value, float min, float max) => value < min ? min : (value > max ? max : value);

        public override void ActivateParameterRow(int index, Action onChanged)
        {
            List<ParamKind> kinds = ActiveParams();
            if (index < 0 || index >= kinds.Count)
                return;
            ThoughtDef selected = CharEditorThoughtBrowserCompat.Selected(dialog);
            switch (kinds[index])
            {
                case ParamKind.TargetPawn:
                    CharEditorThoughtBrowserCompat.OpenTargetPawnPicker(dialog);
                    // Dialog opened; the browser scope's own OnFocus return re-describes this row.
                    break;
                case ParamKind.Stage:
                    OpenStagePicker(onChanged);
                    break;
                case ParamKind.RoyalTitle:
                    OpenRoyalTitlePicker(onChanged);
                    break;
                case ParamKind.Mood:
                    if (selected != null && CharEditorThoughtBrowserCompat.IsMemoryKind(selected))
                        BeginMoodEdit(onChanged);
                    else
                        onChanged();
                    break;
                case ParamKind.Opinion:
                    if (selected != null && CharEditorThoughtBrowserCompat.IsSocialMemoryKind(selected))
                        BeginOpinionEdit(onChanged);
                    else
                        onChanged();
                    break;
            }
        }

        private void OpenStagePicker(Action onChanged)
        {
            List<string> candidates = CharEditorThoughtBrowserCompat.StageCandidates(dialog);
            var options = new List<FloatMenuOption>();
            foreach (string candidate in candidates)
            {
                string captured = candidate;
                options.Add(new FloatMenuOption(captured, delegate
                {
                    CharEditorThoughtBrowserCompat.SetStage(dialog, captured);
                    onChanged();
                }));
            }
            if (options.Count == 0)
            {
                onChanged();
                return;
            }
            WindowlessFloatMenuState.OpenTitled("RimWorldAccess.CharEd.Browser.Thought.Stage".Translate(), options);
        }

        private void OpenRoyalTitlePicker(Action onChanged)
        {
            List<RoyalTitleDef> candidates = CharEditorThoughtBrowserCompat.RoyalTitleCandidates(dialog);
            var options = new List<FloatMenuOption>();
            foreach (RoyalTitleDef candidate in candidates)
            {
                RoyalTitleDef captured = candidate;
                options.Add(new FloatMenuOption(captured.LabelCap, delegate
                {
                    CharEditorThoughtBrowserCompat.SetRoyalTitle(dialog, captured);
                    onChanged();
                }));
            }
            if (options.Count == 0)
            {
                onChanged();
                return;
            }
            WindowlessFloatMenuState.OpenTitled("RimWorldAccess.CharEd.Character.RoyalTitleRow".Translate(), options);
        }

        /// <summary>No maxLength: the mod's own controls are sliders with no text limit to harvest. Whole figures only -- both sliders step in ones (mood) and fives (opinion), never fractions.</summary>
        private static TextFieldSpec MultiplierSpec()
        {
            return new TextFieldSpec(labelKey: null, maxLength: null, minLength: 0,
                allowedChars: CharEdNumericEntry.SignedDigits);
        }

        private void BeginMoodEdit(Action onChanged)
        {
            float current = CharEditorThoughtBrowserCompat.MoodMultiplier(dialog);
            CharEdNumericEntry.OpenFloat(paramSession,
                "RimWorldAccess.CharEd.Browser.Thought.Mood".Translate().ToString(),
                current.ToString("0"), MultiplierSpec(), MoodMin, MoodMax,
                v => CharEditorThoughtBrowserCompat.SetMoodMultiplier(dialog, v),
                onExit: onChanged);
        }

        private void BeginOpinionEdit(Action onChanged)
        {
            float current = CharEditorThoughtBrowserCompat.OpinionMultiplier(dialog);
            CharEdNumericEntry.OpenFloat(paramSession,
                "RimWorldAccess.CharEd.Browser.Thought.Opinion".Translate().ToString(),
                current.ToString("0"), MultiplierSpec(), OpinionMin, OpinionMax,
                v => CharEditorThoughtBrowserCompat.SetOpinionMultiplier(dialog, v),
                onExit: onChanged);
        }

        public override bool Confirm()
        {
            if (!CharEditorThoughtBrowserCompat.AllOk(dialog))
                return false;
            CharEditorThoughtBrowserCompat.Confirm(dialog);
            return !Find.WindowStack.IsOpen(dialog);
        }

        public override void CancelPendingEdit()
        {
            paramSession.CancelIfActive();
        }
    }
}
