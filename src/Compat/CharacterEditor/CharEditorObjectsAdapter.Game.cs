using System;
using System.Collections.Generic;
using System.Linq;
using LudeonTK;
using RimWorld;
using RimWorldAccess.Shell;
using Verse;
using Verse.Sound;

namespace RimWorldAccess
{
    /// <summary>
    /// Adapter for <c>CharacterEditor.DialogObjects</c> in all three modes: Weapon, Apparel and
    /// Object. Filters are a read-only Mode row, the mod-name dropdown every mode draws, then
    /// mode-specific ones (weapon type; layer plus body-part group; thing category def plus
    /// thing category). Results are the mod's own filtered candidate list. Parameters are
    /// adapter-declared and appear only where the selected def's own gates say so — quality,
    /// stuff, style, stack count, plus gender, biological age and faction in Object mode when
    /// <c>Selected.HasRace</c>, matching the mod's own creature pane. All are shown regardless
    /// of the mod's <c>CEditor.IsExtendedUI</c> session toggle. Confirm rides the dialog's own
    /// <c>OnAcceptKeyPressed</c>, which equips or wears the item, adds it to inventory, or
    /// auto-places a race-having thing; any confirm it chains into is a real window the shell
    /// already reads.
    ///
    /// The mode is read once at construction. Flipping the dialog's own in-dialog mode dropdown
    /// by mouse while the scope is attached does not reshape the filters or parameters until
    /// the window reopens: a known limitation, deliberately not fixed.
    /// </summary>
    internal sealed class ObjectsAdapter : CharEditorBrowserAdapterBase
    {
        private enum ParamKind { Quality, Stuff, Style, Stack, Gender, Age, Faction }

        private readonly CharEditorObjectsCompat.ObjectsMode mode;
        private readonly List<ScreenAction> extraActions = new List<ScreenAction>();
        private readonly TextFieldEditSession stackSession = new TextFieldEditSession();
        private readonly TextFieldEditSession ageSession = new TextFieldEditSession();

        /// <summary>
        /// Snapshot of the mod's live <c>lDefs</c>, a HashSet it rebuilds with a fresh
        /// enumeration order on every filter change. Every Results read path shares this one
        /// list until <see cref="InvalidateResults"/> drops it; without that, an index resolved
        /// while describing a row could name a different def by the time Enter activates it.
        /// </summary>
        private List<ThingDef> resultsSnapshot;

        private List<ThingDef> Results
        {
            get
            {
                if (resultsSnapshot == null)
                    resultsSnapshot = CharEditorObjectsCompat.Results(dialog);
                return resultsSnapshot;
            }
        }

        private bool isWeaponMode => mode == CharEditorObjectsCompat.ObjectsMode.Weapon;
        private bool isObjectMode => mode == CharEditorObjectsCompat.ObjectsMode.Object;

        public ObjectsAdapter(Window dialog)
            : base(dialog)
        {
            mode = CharEditorObjectsCompat.CurrentMode(dialog);
        }

        public override string Title =>
            mode == CharEditorObjectsCompat.ObjectsMode.Weapon ? CharEditorObjectsCompat.WeaponModeLabel
            : mode == CharEditorObjectsCompat.ObjectsMode.Object ? CharEditorObjectsCompat.ObjectModeLabel
            : "Apparel".Translate().ToString();

        // Filters: Mode (read-only), mod name, then mode-specific.

        protected override IReadOnlyList<CharEditorBrowserFilter> BuildFilters()
        {
            var rows = new List<CharEditorBrowserFilter>
            {
                new CharEditorBrowserFilter
                {
                    // Read-only: no candidates, so this row never reaches SetCandidate.
                    Label = "RimWorldAccess.CharEd.Browser.Objects.ModeFilter".Translate(),
                    Candidates = () => new List<string>(),
                    CurrentIndex = () => -1,
                    SetCandidate = candidateIndex => { },
                    ValueLabel = () => Title,
                },
                ModNameCombo(() => CharEditorObjectsCompat.ModNameCandidates(dialog),
                    () => CharEditorObjectsCompat.ModName(dialog),
                    name => CharEditorObjectsCompat.SetModName(dialog, name),
                    // Setting the mod name rebuilds the mod's own lDefs.
                    afterSet: InvalidateResults),
            };
            if (isWeaponMode)
            {
                rows.Add(Combo("RimWorldAccess.CharEd.Browser.Objects.WeaponTypeFilter".Translate(),
                    () => CharEditorObjectsCompat.WeaponTypeCandidates().Select(CharEditorObjectsCompat.WeaponTypeLabel).ToList(),
                    delegate
                    {
                        object current = CharEditorObjectsCompat.WeaponTypeFilter(dialog);
                        List<object> candidates = CharEditorObjectsCompat.WeaponTypeCandidates();
                        return candidates.FindIndex(v => Equals(v, current));
                    },
                    delegate(int candidateIndex)
                    {
                        List<object> candidates = CharEditorObjectsCompat.WeaponTypeCandidates();
                        if (candidateIndex >= 0 && candidateIndex < candidates.Count)
                            CharEditorObjectsCompat.SetWeaponTypeFilter(dialog, candidates[candidateIndex]);
                        InvalidateResults();
                    }));
            }
            else if (isObjectMode)
            {
                rows.Add(Combo("RimWorldAccess.CharEd.Browser.CategoryFilter".Translate(),
                    () => CharEditorObjectsCompat.ThingCategoryDefCandidates().Select(DefOrAll).ToList(),
                    delegate
                    {
                        ThingCategoryDef current = CharEditorObjectsCompat.ThingCategoryDefFilter(dialog);
                        return CharEditorObjectsCompat.ThingCategoryDefCandidates().FindIndex(d => d == current);
                    },
                    delegate(int candidateIndex)
                    {
                        List<ThingCategoryDef> candidates = CharEditorObjectsCompat.ThingCategoryDefCandidates();
                        if (candidateIndex >= 0 && candidateIndex < candidates.Count)
                            CharEditorObjectsCompat.SetThingCategoryDefFilter(dialog, candidates[candidateIndex]);
                        InvalidateResults();
                    }));
                rows.Add(Combo("RimWorldAccess.CharEd.Browser.Objects.ThingCategoryFilter".Translate(),
                    () => CharEditorObjectsCompat.ThingCategoryCandidates().Select(CharEditorObjectsCompat.ThingCategoryLabel).ToList(),
                    delegate
                    {
                        ThingCategory current = CharEditorObjectsCompat.ThingCategoryFilter(dialog);
                        return CharEditorObjectsCompat.ThingCategoryCandidates().FindIndex(c => c == current);
                    },
                    delegate(int candidateIndex)
                    {
                        List<ThingCategory> candidates = CharEditorObjectsCompat.ThingCategoryCandidates();
                        if (candidateIndex >= 0 && candidateIndex < candidates.Count)
                            CharEditorObjectsCompat.SetThingCategoryFilter(dialog, candidates[candidateIndex]);
                        InvalidateResults();
                    }));
            }
            else
            {
                rows.Add(Combo(CharEditorObjectsCompat.LayerLabel,
                    () => CharEditorObjectsCompat.ApparelLayerCandidates().Select(DefOrAll).ToList(),
                    delegate
                    {
                        ApparelLayerDef current = CharEditorObjectsCompat.ApparelLayerFilter(dialog);
                        return CharEditorObjectsCompat.ApparelLayerCandidates().FindIndex(d => d == current);
                    },
                    delegate(int candidateIndex)
                    {
                        List<ApparelLayerDef> candidates = CharEditorObjectsCompat.ApparelLayerCandidates();
                        if (candidateIndex >= 0 && candidateIndex < candidates.Count)
                            CharEditorObjectsCompat.SetApparelLayerFilter(dialog, candidates[candidateIndex]);
                        InvalidateResults();
                    }));
                rows.Add(Combo(CharEditorObjectsCompat.BodyPartGroupsLabel,
                    () => CharEditorObjectsCompat.BodyPartGroupCandidates().Select(DefOrAll).ToList(),
                    delegate
                    {
                        BodyPartGroupDef current = CharEditorObjectsCompat.BodyPartGroupFilter(dialog);
                        return CharEditorObjectsCompat.BodyPartGroupCandidates().FindIndex(d => d == current);
                    },
                    delegate(int candidateIndex)
                    {
                        List<BodyPartGroupDef> candidates = CharEditorObjectsCompat.BodyPartGroupCandidates();
                        if (candidateIndex >= 0 && candidateIndex < candidates.Count)
                            CharEditorObjectsCompat.SetBodyPartGroupFilter(dialog, candidates[candidateIndex]);
                        InvalidateResults();
                    }));
            }
            return rows;
        }

        /// <summary>
        /// Every filter setter rebuilds the mod's own lDefs (MUTATION-C, per
        /// CharEditorObjectsCompat's setters), so the snapshot has to go with it.
        /// </summary>
        private void InvalidateResults()
        {
            resultsSnapshot = null;
        }

        public override int ResultCount => Results.Count;

        public override string DescribeResultLabel(int index)
        {
            var results = Results;
            return index >= 0 && index < results.Count ? results[index].LabelCap.ToString() : "";
        }

        public override string DescribeResultTooltip(int index)
        {
            var results = Results;
            return index >= 0 && index < results.Count ? results[index].description : null;
        }

        public override int SelectedResultIndex
        {
            get
            {
                ThingDef selected = CharEditorObjectsCompat.Selected(dialog);
                return Results.IndexOf(selected);
            }
        }

        /// <summary>Writes the selection and rebuilds the mod's live Selected template, resolving the index against the same snapshot the describe calls served.</summary>
        public override void SelectResult(int index)
        {
            var results = Results;
            if (index < 0 || index >= results.Count)
                return;
            CharEditorObjectsCompat.SetSelected(dialog, results[index]);
            CharEditorObjectsCompat.RebuildSelection(dialog);
        }

        // Parameters: quality, stuff, style, stack, present per the selected def's own gates.

        private List<ParamKind> ActiveParams()
        {
            var list = new List<ParamKind>();
            if (CharEditorObjectsCompat.Selected(dialog) == null)
                return list;
            if (CharEditorObjectsCompat.HasQuality())
                list.Add(ParamKind.Quality);
            ThingDef thingDef = CharEditorObjectsCompat.SelectedThingDef();
            if (CharEditorObjectsCompat.MadeFromStuff(thingDef))
                list.Add(ParamKind.Stuff);
            if (CharEditorObjectsCompat.CanBeStyled(thingDef) && CharEditorObjectsCompat.StyleCandidates().Count > 1)
                list.Add(ParamKind.Style);
            if (CharEditorObjectsCompat.HasStack())
                list.Add(ParamKind.Stack);
            // Object mode's creature pane, gated on Selected.HasRace as the mod's own is.
            if (isObjectMode && CharEditorObjectsCompat.SelectedHasRace())
            {
                list.Add(ParamKind.Gender);
                list.Add(ParamKind.Age);
                list.Add(ParamKind.Faction);
            }
            return list;
        }

        public override int ParameterCount => ActiveParams().Count;

        public override ElementDescription DescribeParameterRow(int index)
        {
            var d = new ElementDescription();
            List<ParamKind> kinds = ActiveParams();
            if (index < 0 || index >= kinds.Count)
                return d;
            switch (kinds[index])
            {
                case ParamKind.Quality:
                {
                    QualityCategory q = CharEditorObjectsCompat.Quality();
                    d.Label = CharEditorObjectsCompat.QualityLabel;
                    d.Role = ElementRole.ComboBox;
                    d.Value = q.GetLabel().CapitalizeFirst();
                    break;
                }
                case ParamKind.Stuff:
                {
                    ThingDef stuff = CharEditorObjectsCompat.Stuff();
                    d.Label = CharEditorObjectsCompat.StuffLabel;
                    d.Role = ElementRole.ComboBox;
                    d.Value = CharEditorObjectsCompat.StuffOrStyleLabel(stuff);
                    break;
                }
                case ParamKind.Style:
                {
                    ThingStyleDef style = CharEditorObjectsCompat.Style();
                    d.Label = CharEditorObjectsCompat.StyleLabel;
                    d.Role = ElementRole.ComboBox;
                    d.Value = CharEditorObjectsCompat.StuffOrStyleLabel(style);
                    break;
                }
                case ParamKind.Stack:
                {
                    int val = CharEditorObjectsCompat.StackVal();
                    ThingDef thingDef = CharEditorObjectsCompat.SelectedThingDef();
                    int max = thingDef != null ? thingDef.stackLimit : val;
                    d.Label = CharEditorObjectsCompat.CountLabel;
                    d.Role = ElementRole.Stepper;
                    d.Value = val.ToString();
                    d.AtMinimum = val <= 1;
                    d.AtMaximum = val >= max;
                    break;
                }
                case ParamKind.Gender:
                {
                    Gender g = CharEditorObjectsCompat.SelectedGender();
                    d.Label = "RimWorldAccess.CharEd.Actions.Gender".Translate();
                    d.Role = ElementRole.ComboBox;
                    d.Value = g.GetLabel().CapitalizeFirst();
                    break;
                }
                case ParamKind.Age:
                {
                    int age = CharEditorObjectsCompat.SelectedAge();
                    d.Label = "RimWorldAccess.CharEd.Character.AgeBiological".Translate();
                    d.Role = ElementRole.Stepper;
                    d.Value = age.ToString();
                    d.AtMinimum = age <= 1;
                    d.AtMaximum = age >= 100;
                    break;
                }
                case ParamKind.Faction:
                {
                    string faction = CharEditorObjectsCompat.DialogFaction(dialog);
                    d.Label = "Faction".Translate();
                    d.Role = ElementRole.ComboBox;
                    d.Value = faction.NullOrEmpty() ? AllLabel() : faction;
                    break;
                }
            }
            return d;
        }

        /// <summary>
        /// Only the stepper params (stack count, age) adjust with Left/Right; the ComboBox rows
        /// open their pickers on Enter/Space instead.
        /// </summary>
        public override bool CanAdjustParameterRow(int index)
        {
            List<ParamKind> kinds = ActiveParams();
            if (index < 0 || index >= kinds.Count)
                return false;
            ParamKind kind = kinds[index];
            return kind == ParamKind.Stack || kind == ParamKind.Age;
        }

        public override void AdjustParameterRow(int index, int direction)
        {
            List<ParamKind> kinds = ActiveParams();
            if (index < 0 || index >= kinds.Count)
                return;
            bool next = direction > 0;
            switch (kinds[index])
            {
                case ParamKind.Stack:
                {
                    ThingDef thingDef = CharEditorObjectsCompat.SelectedThingDef();
                    int max = thingDef != null ? thingDef.stackLimit : int.MaxValue;
                    int v = CharEditorObjectsCompat.StackVal() + (next ? 1 : -1);
                    if (v < 1) v = 1;
                    if (v > max) v = max;
                    CharEditorObjectsCompat.SetStackVal(v);
                    break;
                }
                case ParamKind.Age:
                    CharEditorObjectsCompat.SetSelectedAge(CharEditorObjectsCompat.SelectedAge() + (next ? 1 : -1));
                    break;
            }
            SoundDefOf.DragSlider.PlayOneShotOnCamera();
        }

        public override void ActivateParameterRow(int index, Action onChanged)
        {
            List<ParamKind> kinds = ActiveParams();
            if (index < 0 || index >= kinds.Count)
                return;
            switch (kinds[index])
            {
                case ParamKind.Quality:
                    ActivateQualityPicker(onChanged);
                    break;
                case ParamKind.Stuff:
                    ActivateStuffPicker(onChanged);
                    break;
                case ParamKind.Style:
                    ActivateStylePicker(onChanged);
                    break;
                case ParamKind.Stack:
                    BeginStackEdit(onChanged);
                    break;
                case ParamKind.Gender:
                    ActivateGenderPicker(onChanged);
                    break;
                case ParamKind.Age:
                    BeginAgeEdit(onChanged);
                    break;
                case ParamKind.Faction:
                    ActivateFactionPicker(onChanged);
                    break;
            }
        }

        private void ActivateGenderPicker(Action onChanged)
        {
            var candidates = new[] { Gender.Male, Gender.Female, Gender.None };
            var options = new List<FloatMenuOption>();
            foreach (Gender g in candidates)
            {
                options.Add(new FloatMenuOption(g.GetLabel().CapitalizeFirst(), delegate
                {
                    CharEditorObjectsCompat.SetSelectedGender(g);
                    onChanged();
                }));
            }
            WindowlessFloatMenuState.OpenTitled("RimWorldAccess.CharEd.Actions.Gender".Translate(), options);
        }

        private void ActivateFactionPicker(Action onChanged)
        {
            List<string> candidates = CharEditorObjectsCompat.DialogFactionCandidates(dialog);
            var options = new List<FloatMenuOption>();
            foreach (string faction in candidates)
            {
                options.Add(new FloatMenuOption(faction, delegate
                {
                    CharEditorObjectsCompat.SetDialogFaction(dialog, faction);
                    onChanged();
                }));
            }
            WindowlessFloatMenuState.OpenTitled("Faction".Translate(), options);
        }

        /// <summary>The mod's own slider bound (`SZWidgets.LabelIntFieldSlider(..., ref ThingTool.SelectedThing.age, 1, 100)`), harvested rather than hand-picked, with the digit cap derived from it.</summary>
        private const int MaxCreatureAge = 100;

        private void BeginAgeEdit(Action onChanged)
        {
            // minLength 1: the mod's own field is never empty (the slider always holds a figure).
            var spec = new TextFieldSpec(labelKey: null, maxLength: MaxCreatureAge.ToString().Length,
                minLength: 1, allowedChars: CharEdNumericEntry.DigitsOnly);
            CharEdNumericEntry.OpenInt(ageSession, "RimWorldAccess.CharEd.Character.AgeBiological".Translate(),
                CharEditorObjectsCompat.SelectedAge(), 1, MaxCreatureAge,
                CharEditorObjectsCompat.SetSelectedAge, onExit: onChanged, spec: spec);
        }

        private void ActivateQualityPicker(Action onChanged)
        {
            List<QualityCategory> candidates = CharEditorObjectsCompat.QualityCandidates();
            var options = new List<FloatMenuOption>();
            foreach (QualityCategory q in candidates)
            {
                options.Add(new FloatMenuOption(q.GetLabel().CapitalizeFirst(), delegate
                {
                    CharEditorObjectsCompat.SetQuality(q);
                    onChanged();
                }));
            }
            WindowlessFloatMenuState.OpenTitled(CharEditorObjectsCompat.QualityLabel, options);
        }

        private void ActivateStuffPicker(Action onChanged)
        {
            List<ThingDef> candidates = CharEditorObjectsCompat.StuffCandidates();
            var options = new List<FloatMenuOption>();
            foreach (ThingDef def in candidates)
            {
                options.Add(new FloatMenuOption(CharEditorObjectsCompat.StuffOrStyleLabel(def), delegate
                {
                    CharEditorObjectsCompat.SetStuff(def);
                    onChanged();
                }));
            }
            WindowlessFloatMenuState.OpenTitled(CharEditorObjectsCompat.StuffLabel, options);
        }

        private void ActivateStylePicker(Action onChanged)
        {
            List<ThingStyleDef> candidates = CharEditorObjectsCompat.StyleCandidates();
            var options = new List<FloatMenuOption>();
            foreach (ThingStyleDef style in candidates)
            {
                options.Add(new FloatMenuOption(CharEditorObjectsCompat.StuffOrStyleLabel(style), delegate
                {
                    CharEditorObjectsCompat.SetStyle(style);
                    onChanged();
                }));
            }
            WindowlessFloatMenuState.OpenTitled(CharEditorObjectsCompat.StyleLabel, options);
        }

        private void BeginStackEdit(Action onChanged)
        {
            ThingDef thingDef = CharEditorObjectsCompat.SelectedThingDef();
            int max = thingDef != null ? thingDef.stackLimit : 1;
            CharEdNumericEntry.OpenInt(stackSession, CharEditorObjectsCompat.CountLabel,
                CharEditorObjectsCompat.StackVal(), 1, max,
                CharEditorObjectsCompat.SetStackVal, onExit: onChanged);
        }

        /// <summary>
        /// The dialog's own OK path. In browse mode it closes itself; in placing mode it
        /// deliberately stays open and arms a placement DebugTool instead, so this closes it
        /// once that tool has actually armed and the map can own input. Browse-mode confirm is
        /// untouched: both checks below are already-false no-ops there.
        /// </summary>
        public override bool Confirm()
        {
            DebugTool before = DebugTools.curTool;
            dialog.OnAcceptKeyPressed();
            if (!ReferenceEquals(DebugTools.curTool, before) && DebugTools.curTool != null && Find.WindowStack.IsOpen(dialog))
            {
                dialog.Close();
            }
            return !Find.WindowStack.IsOpen(dialog);
        }

        /// <summary>
        /// Buy, Activate placing mode, and while pinned Rotate/Destroy: the four rows
        /// DrawLowerButtons draws across its own placing-mode branch, gated on
        /// <see cref="CharEditorObjectsCompat.PlacingAvailable"/>.
        /// </summary>
        public override IReadOnlyList<ScreenAction> ExtraActions
        {
            get
            {
                extraActions.Clear();
                ThingDef selected = CharEditorObjectsCompat.Selected(dialog);
                bool placingAvailable = CharEditorObjectsCompat.PlacingAvailable;
                bool inPlacingMode = placingAvailable && CharEditorObjectsCompat.InPlacingMode(dialog);

                if (placingAvailable && !inPlacingMode)
                {
                    if (selected != null)
                    {
                        string label = CharEditorObjectsCompat.BuyLabel + " (" + CharEditorObjectsCompat.PriceLabel
                            + CharEditorObjectsCompat.BuyPrice() + ")";
                        extraActions.Add(new ScreenAction(label, () => CharEditorObjectsCompat.ArmBuy(dialog)));
                    }
                    extraActions.Add(new ScreenAction("RimWorldAccess.CharEd.Objects.ActivatePlacingMode".Translate(),
                        ActivatePlacingMode));
                }
                else if (inPlacingMode)
                {
                    extraActions.Add(new ScreenAction("RimWorldAccess.CharEd.Objects.RotatePlacing".Translate(),
                        RotatePlacing));
                    extraActions.Add(new ScreenAction(CharEditorObjectsCompat.DestroyLabel, ArmDestroy));
                }
                // Present only while a def is selected, matching the mod's own DrawParameterBase
                // gate.
                if (selected != null && CharEditorDefEditorCompat.ObjectReady)
                {
                    extraActions.Add(new ScreenAction("RimWorldAccess.CharEd.DefEditor.OpenAction".Translate(),
                        () => FocusStack.Push(new CharEditorDefEditorScope(dialog, selected))));
                }
                return extraActions;
            }
        }

        public override void CancelPendingEdit()
        {
            stackSession.CancelIfActive();
            ageSession.CancelIfActive();
        }

        // Placing mode; CharEditorObjectsCompat's class remarks carry the mod vehicles.

        /// <summary>Arms no DebugTool of its own, so it needs its own outcome phrase rather than DevToolTargeting's arm mirror.</summary>
        private void ActivatePlacingMode()
        {
            CharEditorObjectsCompat.ActivatePlacingMode(dialog);
            TolkHelper.SpeakData("RimWorldAccess.CharEd.Objects.PlacingModeOn".Translate().ToString());
        }

        private void RotatePlacing()
        {
            CharEditorObjectsCompat.RotatePlacing(dialog);
            TolkHelper.SpeakData("RimWorldAccess.CharEd.Actions.RotatedTo".Translate(
                CharEditorCompat.CurrentPlacingRotation.ToStringHuman()).ToString());
        }

        /// <summary>Destroy always arms today; the reference check guards against a mod change that adds a decline branch.</summary>
        private void ArmDestroy()
        {
            DebugTool before = DebugTools.curTool;
            CharEditorObjectsCompat.ArmDestroy(dialog);
            if (!ReferenceEquals(DebugTools.curTool, before) && DebugTools.curTool != null && Find.WindowStack.IsOpen(dialog))
            {
                dialog.Close();
            }
        }

        private string DefOrAll<T>(T def) where T : Def => def == null ? AllLabel() : def.LabelCap.ToString();

        /// <summary>This dialog family carries its own copy of the mod's "All" word.</summary>
        protected override string AllLabel()
        {
            string all = CharEditorObjectsCompat.AllLabel;
            return all.NullOrEmpty() ? "RimWorldAccess.CharEd.Browser.AllOption".Translate().ToString() : all;
        }
    }
}
