using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using RimWorld;
using Verse;
using Verse.Sound;

namespace RimWorldAccess.Shell
{
    /// <summary>
    /// The Health tab's Operations and Health Settings sub-menus. The mod owns no window here — the
    /// surface is the windowless <see cref="HealthTabState"/> — so the scope rides the focus stack
    /// through <see cref="HealthTabScopeMirror"/>.
    ///
    /// ONE content region whose shape toggles between the two LIST levels (<see cref="Level"/>).
    /// Each level owns its own cursor field, so no saved-row memory is needed:
    /// <see cref="CurrentIndex"/> routes to whichever field <see cref="currentLevel"/> names, and
    /// the base's ListModel cursor is kept in step (<see cref="SyncModelCursor"/>) purely for the
    /// base's own bookkeeping.
    ///
    /// The four PICKER levels are float-menu twins: each mirrors a vanilla float menu (the
    /// medical-care dropdown, MedicalCareUtility.cs:88; the food policy menu,
    /// HealthCardUtility.cs:471; the surgery menu behind Add Bill, HealthCardUtility.cs:264), so
    /// each is a <see cref="WindowlessFloatMenuState"/> menu drawn by the shared float-menu twin.
    /// Navigation, typeahead, activation and Escape come from that state and
    /// <see cref="FloatMenuOverlayScope"/>; only the option lists and activation bodies live here.
    /// The real health card underneath is ringed by <see cref="HealthCardRowCapture"/> and
    /// <see cref="BillRowFocusRing"/>.
    ///
    /// DELIBERATELY NOT using the base's shared typeahead engine
    /// (<see cref="ScreenScope.EnableTypeahead"/> stays false) or the generic composer: this
    /// screen's announcements are bespoke StringBuilder-composed phrases that predate the composer
    /// grammar and are not to be re-worded.
    ///
    /// Reordering a queued operation claims the shared
    /// <see cref="SharedMenuGrammar.ReorderUp"/>/<see cref="SharedMenuGrammar.ReorderDown"/> ids,
    /// gated on <see cref="IsOperationsListLevel"/>. HealthTabState opens FROM the inspection tree
    /// without closing it, so the mirror reconciles immediately after InspectionScopeMirror and
    /// stack order alone gives this scope the keyboard.
    /// </summary>
    public sealed class HealthTabScope : ScreenScope, ICharSink,
        IBillRowFocusSource, IBillAddRowFocusSource, IHealthCardRowFocusSource
    {
        private enum Level
        {
            MedicalSettingsList,
            OperationsList,
        }

        private const int SettingFoodRestriction = 0;
        private const int SettingMedicalCare = 1;
        private const int SettingSelfTend = 2;

        /// <summary>The details row's own index in the operation-actions menu, for the silent reopen after it speaks.</summary>
        private const int ActionViewDetails = 0;

        private Pawn currentPawn;
        private Level currentLevel = Level.OperationsList;
        private bool announcedOpen;

        // Medical Settings.
        private int medicalSettingIndex;
        private readonly List<int> visibleSettingIds = new List<int> { SettingFoodRestriction, SettingMedicalCare, SettingSelfTend };

        // Operations.
        private List<Bill> queuedOperations = new List<Bill>();
        private int operationIndex;

        // One typeahead per list level.
        private readonly TypeaheadSearchHelper settingsTypeahead = new TypeaheadSearchHelper();
        private readonly TypeaheadSearchHelper operationsTypeahead = new TypeaheadSearchHelper();

        public HealthTabScope()
        {
            // Windowless modal: this scope owns Escape itself, unconditionally.
            Claim(SharedMenuGrammar.Cancel, delegate { HandleCancelKey(); });
            Claim(SharedMenuGrammar.SearchBackspace, delegate { HandleTypeaheadBackspace(); },
                when: HasActiveSearch);

            // Ctrl+Up/Down reorder the focused queued operation, and are meaningful only on the
            // Operations list. Registered after the base ctor's CanReorderContentItem-gated claim,
            // which falls through unless overridden.
            Claim(SharedMenuGrammar.ReorderUp, delegate { ReorderOperation(-1); }, when: IsOperationsListLevel);
            Claim(SharedMenuGrammar.ReorderDown, delegate { ReorderOperation(1); }, when: IsOperationsListLevel);
        }

        public override string Name
        {
            get { return "health-tab"; }
        }

        public override bool OwnsCancel
        {
            get { return true; }
        }

        public override ICharSink CharSink
        {
            get { return this; }
        }

        private bool IsOperationsListLevel()
        {
            return currentLevel == Level.OperationsList;
        }

        /// <summary>Bypasses the base's shared typeahead engine entirely — see the class remarks.</summary>
        public override bool HandleChar(char c)
        {
            if (!TypeaheadMatcher.AcceptsSearchChar(c, HasActiveSearch()))
            {
                return false;
            }
            // Consumed whenever the letter/digit gate passes, whatever HandleTypeaheadInput's own
            // internal outcome was.
            HandleTypeaheadInput(c);
            return true;
        }

        // Focus sources for the real health card's own rings.

        public Bill FocusedBill
        {
            get
            {
                if (currentLevel != Level.OperationsList)
                {
                    return null;
                }
                return operationIndex >= 0 && operationIndex < queuedOperations.Count
                    ? queuedOperations[operationIndex]
                    : null;
            }
        }

        public bool FocusedOnAddBillRow
        {
            get { return currentLevel == Level.OperationsList && operationIndex == queuedOperations.Count; }
        }

        public string FocusedHealthCardRow
        {
            get
            {
                if (currentLevel != Level.MedicalSettingsList
                    || medicalSettingIndex < 0 || medicalSettingIndex >= visibleSettingIds.Count)
                {
                    return null;
                }
                switch (visibleSettingIds[medicalSettingIndex])
                {
                    case SettingFoodRestriction: return HealthCardRowCapture.FoodRow;
                    case SettingMedicalCare: return HealthCardRowCapture.CareRow;
                    case SettingSelfTend: return HealthCardRowCapture.SelfTendRow;
                    default: return null;
                }
            }
        }

        // ScreenScope content contract.

        protected override int ContentRegionCount
        {
            get { return 1; }
        }

        /// <summary>Reuses vanilla's own Health ITab label — no new translation key.</summary>
        protected override string ContentRegionName(int region)
        {
            return "TabHealth".Translate().ToString();
        }

        protected override int ContentItemCount(int region)
        {
            switch (currentLevel)
            {
                case Level.MedicalSettingsList: return visibleSettingIds.Count;
                case Level.OperationsList: return queuedOperations.Count + 1; // +1 for "Add Operation"
                default: return 0;
            }
        }

        protected override ElementDescription DescribeContentItem(int region, int index)
        {
            var d = new ElementDescription();
            switch (currentLevel)
            {
                case Level.MedicalSettingsList:
                    if (index >= 0 && index < visibleSettingIds.Count)
                        d.Label = GetMedicalSettingLabel(visibleSettingIds[index]);
                    break;
                case Level.OperationsList:
                    d.Label = index < queuedOperations.Count
                        ? queuedOperations[index].LabelCap.StripTags()
                        : "AddBill".Translate().ToString();
                    break;
            }
            return d;
        }

        protected override void ActivateContentItem(int region, int index)
        {
            DrillDown();
        }

        /// <summary>The current level's own cursor field.</summary>
        private int CurrentIndex
        {
            get
            {
                switch (currentLevel)
                {
                    case Level.MedicalSettingsList: return medicalSettingIndex;
                    case Level.OperationsList: return operationIndex;
                    default: return -1;
                }
            }
            set
            {
                switch (currentLevel)
                {
                    case Level.MedicalSettingsList: medicalSettingIndex = value; break;
                    case Level.OperationsList: operationIndex = value; break;
                }
            }
        }

        /// <summary>The current level's typeahead helper.</summary>
        private TypeaheadSearchHelper CurrentTypeahead
        {
            get
            {
                switch (currentLevel)
                {
                    case Level.MedicalSettingsList: return settingsTypeahead;
                    case Level.OperationsList: return operationsTypeahead;
                    default: return null;
                }
            }
        }

        /// <summary>The current level's row labels, in cursor order — feeds typeahead only.</summary>
        private List<string> CurrentLabels()
        {
            switch (currentLevel)
            {
                case Level.MedicalSettingsList:
                    return visibleSettingIds.Select(id => GetMedicalSettingLabel(id)).ToList();
                case Level.OperationsList:
                    {
                        var labels = queuedOperations.Select(b => b.LabelCap.StripTags()).ToList();
                        labels.Add("AddBill".Translate().ToString().StripTags());
                        return labels;
                    }
                default:
                    return new List<string>();
            }
        }

        private bool HasActiveSearch()
        {
            TypeaheadSearchHelper t = CurrentTypeahead;
            return t != null && t.HasActiveSearch;
        }

        /// <summary>Keeps the base model's cursor in step with the level's own field; bookkeeping only, never read when announcing.</summary>
        private void SyncModelCursor()
        {
            RefreshModel();
            ListModel region = Model.CurrentRegion;
            int idx = CurrentIndex;
            if (region != null && idx >= 0 && idx < region.Count)
            {
                region.MoveTo(idx);
            }
        }

        // Lifecycle.

        public override void OnPush()
        {
            base.OnPush();
            currentPawn = HealthTabState.TargetPawn;
            announcedOpen = false;

            settingsTypeahead.ClearSearch();
            operationsTypeahead.ClearSearch();

            if (HealthTabState.OpenToOperations)
            {
                currentLevel = Level.OperationsList;
                operationIndex = 0;
                queuedOperations.Clear();
                if (currentPawn?.BillStack != null)
                {
                    queuedOperations.AddRange(currentPawn.BillStack.Bills);
                }
            }
            else
            {
                currentLevel = Level.MedicalSettingsList;
                RebuildVisibleSettings();
                medicalSettingIndex = 0;
            }
        }

        public override void OnFocus()
        {
            base.OnFocus();
            if (announcedOpen)
                return;
            announcedOpen = true;
            SoundDefOf.TabOpen.PlayOneShotOnCamera();
            AnnounceCurrentSelection();
        }

        private void RebuildVisibleSettings()
        {
            visibleSettingIds.Clear();
            if (HealthTabHelper.CanConfigureFoodRestriction(currentPawn))
                visibleSettingIds.Add(SettingFoodRestriction);

            // Medical-care and self-tend gates mirror HealthCardUtility.DrawOverviewTab's own,
            // rather than showing both settings regardless of pawn state.
            bool playerCare = currentPawn.Faction == Faction.OfPlayer || currentPawn.HostFaction == Faction.OfPlayer;
            bool bedCare = currentPawn.NonHumanlikeOrWildMan() && currentPawn.InBed() && currentPawn.CurrentBed().Faction == Faction.OfPlayer;
            if (currentPawn.RaceProps.IsFlesh && (playerCare || bedCare)
                && (!currentPawn.IsMutant || currentPawn.mutant.Def.entitledToMedicalCare)
                && currentPawn.playerSettings != null && !currentPawn.Dead && Current.ProgramState == ProgramState.Playing)
            {
                visibleSettingIds.Add(SettingMedicalCare);
            }

            if (Current.ProgramState == ProgramState.Playing && currentPawn.IsColonist && !currentPawn.Dead
                && !currentPawn.DevelopmentalStage.Baby() && currentPawn.playerSettings != null)
            {
                visibleSettingIds.Add(SettingSelfTend);
            }
        }

        private static string GetMedicalSettingLabel(int index)
        {
            switch (index)
            {
                case SettingFoodRestriction: return "AllowFood".Translate();
                case SettingMedicalCare: return "AllowMedicine".Translate();
                case SettingSelfTend: return "AllowSelfTend".Translate();
                default: return "";
            }
        }

        // Navigation.

        protected override void MoveItem(int delta)
        {
            if (HasActiveSearch())
            {
                if (delta > 0) SelectNextMatch(); else SelectPreviousMatch();
            }
            else
            {
                if (delta > 0) SelectNextInternal(); else SelectPreviousInternal();
            }
        }

        private void SelectNextInternal()
        {
            StepInternal(forward: true);
        }

        private void SelectPreviousInternal()
        {
            StepInternal(forward: false);
        }

        private void StepInternal(bool forward)
        {
            RefreshModel();
            int before;
            switch (currentLevel)
            {
                case Level.MedicalSettingsList:
                    before = medicalSettingIndex;
                    medicalSettingIndex = forward
                        ? MenuHelper.SelectNext(medicalSettingIndex, visibleSettingIds.Count, out bool settingWrapped)
                        : MenuHelper.SelectPrevious(medicalSettingIndex, visibleSettingIds.Count, out settingWrapped);
                    if (settingWrapped)
                        MenuHelper.PlayWrapTone();
                    if (medicalSettingIndex == before)
                    {
                        MenuHelper.PlayEdgeTone();
                        return;
                    }
                    break;

                case Level.OperationsList:
                    operationsTypeahead.ClearSearch();
                    before = operationIndex;
                    operationIndex = forward
                        ? MenuHelper.SelectNext(operationIndex, queuedOperations.Count + 1, out bool operationWrapped)
                        : MenuHelper.SelectPrevious(operationIndex, queuedOperations.Count + 1, out operationWrapped);
                    if (operationWrapped)
                        MenuHelper.PlayWrapTone();
                    if (operationIndex == before)
                    {
                        MenuHelper.PlayEdgeTone();
                        return;
                    }
                    break;
            }

            SyncModelCursor();
            SoundDefOf.Tick_Tiny.PlayOneShotOnCamera();
            AnnounceCurrentSelection();
        }

        /// <summary>Home/End are NOT search-aware, unlike Up/Down.</summary>
        protected override void MoveItemEdge(bool first)
        {
            RefreshModel();
            switch (currentLevel)
            {
                case Level.MedicalSettingsList:
                    medicalSettingIndex = first ? 0 : visibleSettingIds.Count - 1;
                    break;
                case Level.OperationsList:
                    operationIndex = first ? 0 : queuedOperations.Count; // Last item is "Add Operation".
                    break;
            }

            SyncModelCursor();
            SoundDefOf.Tick_Tiny.PlayOneShotOnCamera();
            AnnounceCurrentSelection();
        }

        private void SelectNextMatch()
        {
            TypeaheadSearchHelper t = CurrentTypeahead;
            if (t != null && t.HasActiveSearch)
            {
                int next = t.GetNextMatch(CurrentIndex);
                if (next >= 0)
                {
                    MenuHelper.SoundMatchMove(CurrentIndex, next, 1);
                    CurrentIndex = next;
                }
            }

            SyncModelCursor();
            SoundDefOf.Tick_Tiny.PlayOneShotOnCamera();
            AnnounceWithSearch();
        }

        private void SelectPreviousMatch()
        {
            TypeaheadSearchHelper t = CurrentTypeahead;
            if (t != null && t.HasActiveSearch)
            {
                int prev = t.GetPreviousMatch(CurrentIndex);
                if (prev >= 0)
                {
                    MenuHelper.SoundMatchMove(CurrentIndex, prev, -1);
                    CurrentIndex = prev;
                }
            }

            SyncModelCursor();
            SoundDefOf.Tick_Tiny.PlayOneShotOnCamera();
            AnnounceWithSearch();
        }

        // Typeahead, over CurrentLabels/CurrentTypeahead.

        private void HandleTypeaheadInput(char c)
        {
            TypeaheadSearchHelper t = CurrentTypeahead;
            if (t == null)
                return;
            List<string> labels = CurrentLabels();
            if (t.ProcessCharacterInput(c, labels, out int newIndex))
            {
                if (newIndex >= 0)
                {
                    CurrentIndex = newIndex;
                    SyncModelCursor();
                }
                AnnounceWithSearch();
            }
            else
            {
                AnnounceNoMatches(t);
            }
        }

        private void HandleTypeaheadBackspace()
        {
            TypeaheadSearchHelper t = CurrentTypeahead;
            if (t == null || !t.HasActiveSearch)
                return;
            List<string> labels = CurrentLabels();
            if (t.ProcessBackspace(labels, out int newIndex))
            {
                if (newIndex >= 0)
                {
                    CurrentIndex = newIndex;
                    SyncModelCursor();
                }
                AnnounceWithSearch();
            }
        }

        private static void AnnounceNoMatches(TypeaheadSearchHelper typeahead)
        {
            if (!string.IsNullOrEmpty(typeahead.LastFailedSearch))
                typeahead.SpeakNoMatches();
        }

        // Escape.

        private void HandleCancelKey()
        {
            if (HasActiveSearch())
            {
                CurrentTypeahead?.ClearSearchAndAnnounce();
                AnnounceCurrentSelection();
                return;
            }
            GoBack();
        }

        // Drill-down and go-back.

        private void DrillDown()
        {
            settingsTypeahead.ClearSearch();
            operationsTypeahead.ClearSearch();

            switch (currentLevel)
            {
                case Level.MedicalSettingsList:
                    // RebuildVisibleSettings leaves this empty for a pawn the vanilla health card
                    // shows none of these controls for, such as a dead one.
                    if (visibleSettingIds.Count == 0)
                    {
                        SoundDefOf.ClickReject.PlayOneShotOnCamera();
                        break;
                    }
                    switch (visibleSettingIds[medicalSettingIndex])
                    {
                        case SettingFoodRestriction: OpenFoodRestrictionMenu(); break;
                        case SettingMedicalCare: OpenMedicalCareMenu(); break;
                        case SettingSelfTend: ToggleSelfTend(); break;
                    }
                    break;

                case Level.OperationsList:
                    if (operationIndex < queuedOperations.Count)
                    {
                        OpenOperationActionsMenu(0, silent: false);
                    }
                    else
                    {
                        OpenAddRecipeMenu(0, silent: false);
                    }
                    break;
            }
        }

        private void ToggleSelfTend()
        {
            HealthTabHelper.ToggleSelfTend(currentPawn);
            // MUTATION-C: mirrors HealthCardUtility.DrawOverviewTab's AllowSelfTend
            // checkbox handler (lines ~523-536), which reverts+warns inline right
            // after drawing the checkbox; no Try*/AcceptanceReport vehicle exists
            // for this checkbox toggle, so the gate is hand-copied verbatim.
            if (currentPawn.playerSettings?.selfTend == true)
            {
                if (currentPawn.WorkTypeIsDisabled(WorkTypeDefOf.Doctor))
                {
                    // Pawn can never do Doctor work: revert, as vanilla does.
                    currentPawn.playerSettings.selfTend = false;
                    TolkHelper.Speak("MessageCannotSelfTendEver".Loc(
                        currentPawn.LabelShort, currentPawn), SpeechPriority.High);
                }
                else if (currentPawn.workSettings != null
                    && !currentPawn.workSettings.WorkIsActive(WorkTypeDefOf.Doctor))
                {
                    // Doctor work unassigned: warn but allow, as vanilla does.
                    TolkHelper.Speak("MessageSelfTendUnsatisfied".Loc(
                        currentPawn.LabelShort, currentPawn), SpeechPriority.High);
                }
            }
            AnnounceCurrentSelection();
        }

        private void GoBack()
        {
            HealthTabState.Close();
            WindowlessInspectionState.ReannounceCurrentSelection();
        }

        // The picker menus — see the class remarks.

        /// <summary>
        /// Shared open for this screen's four pickers, always <c>announceSelection: false</c>: every
        /// option speaks its own outcome, so the generic "Selected" wording is never wanted. A
        /// refused option reopens its menu silently and speaks the refusal.
        /// </summary>
        private void OpenMenu(List<FloatMenuOption> options, Action onCancel, int startIndex, bool silent)
        {
            WindowlessFloatMenuState.Open(
                options,
                colonistOrders: false,
                startIndex: startIndex,
                announceSelection: false,
                playOpenSound: !silent,
                onClose: cancelled => { if (cancelled) onCancel(); },
                announceFirst: !silent);
        }

        /// <summary>Back one level: re-read the list and speak the cursor's row. Silent when the tab closed under the menu.</summary>
        private void ReturnToList(bool playClick)
        {
            if (!HealthTabState.IsActive)
                return;
            RefreshModel();
            SyncModelCursor();
            if (playClick)
                SoundDefOf.Click.PlayOneShotOnCamera();
            AnnounceCurrentSelection();
        }

        private void OpenFoodRestrictionMenu()
        {
            List<FoodPolicy> restrictions = HealthTabHelper.GetAvailableFoodRestrictions();
            if (restrictions.Count == 0)
            {
                TolkHelper.Speak("NoneLower".Loc());
                SoundDefOf.ClickReject.PlayOneShotOnCamera();
                return;
            }

            var options = new List<FloatMenuOption>();
            foreach (FoodPolicy restriction in restrictions)
            {
                FoodPolicy local = restriction;
                options.Add(new FloatMenuOption(local.label, delegate
                {
                    HealthTabHelper.SetFoodRestriction(currentPawn, local);
                    ReturnToList(playClick: false);
                }));
            }
            OpenMenu(options, delegate { ReturnToList(playClick: true); }, startIndex: 0, silent: false);
        }

        private void OpenMedicalCareMenu()
        {
            var options = new List<FloatMenuOption>();
            foreach (MedicalCareCategory care in HealthTabHelper.GetAvailableMedicalCare())
            {
                MedicalCareCategory local = care;
                options.Add(new FloatMenuOption(local.GetLabel(), delegate
                {
                    HealthTabHelper.SetMedicalCare(currentPawn, local);
                    ReturnToList(playClick: false);
                }));
            }
            OpenMenu(options, delegate { ReturnToList(playClick: true); }, startIndex: 0, silent: false);
        }

        private void OpenOperationActionsMenu(int startIndex, bool silent)
        {
            if (operationIndex < 0 || operationIndex >= queuedOperations.Count)
                return;

            Bill bill = queuedOperations[operationIndex];
            var options = new List<FloatMenuOption>
            {
                new FloatMenuOption("TabBookContents".Translate(), delegate
                {
                    SpeakOperationDetails(bill);
                    SoundDefOf.Click.PlayOneShotOnCamera();
                    OpenOperationActionsMenu(ActionViewDetails, silent: true);
                }),
                new FloatMenuOption("DeleteBillTip".Translate(), delegate
                {
                    HealthTabHelper.RemoveOperation(currentPawn, bill);
                    SoundDefOf.Click.PlayOneShotOnCamera();
                    queuedOperations = HealthTabHelper.GetQueuedOperations(currentPawn);
                    operationIndex = 0;
                    ReturnToList(playClick: false);
                }),
                new FloatMenuOption("GoBack".Translate(), delegate { ReturnToList(playClick: true); }),
            };
            OpenMenu(options, delegate { ReturnToList(playClick: true); }, startIndex, silent);
        }

        private void SpeakOperationDetails(Bill bill)
        {
            var detailSb = new StringBuilder();
            detailSb.Append(bill.LabelCap.StripTags());

            RecipeDef recipe = bill.recipe;

            if (recipe.skillRequirements != null && recipe.skillRequirements.Count > 0)
            {
                string skills = recipe.skillRequirements
                    .Select(sr => $"{sr.skill.LabelCap} {sr.minLevel}")
                    .ToCommaList();
                detailSb.Append($". {"Requires".Translate()}: {skills}");
            }

            if (recipe.ingredients != null && recipe.ingredients.Count > 0)
                detailSb.Append($". {"Ingredients".Translate()}: {recipe.ingredients.Select(i => i.Summary).ToCommaList()}");

            if (recipe.products != null && recipe.products.Count > 0)
                detailSb.Append($". {"Products".Translate()}: {recipe.products.Select(p => $"{p.thingDef.LabelCap} x{p.count}").ToCommaList()}");

            var billMedical = bill as Bill_Medical;
            if (billMedical?.Part != null && recipe.addsHediff?.addedPartProps?.solid == true)
            {
                var replacedParts = new List<string>();
                foreach (BodyPartRecord childPart in billMedical.Part.GetPartAndAllChildParts())
                {
                    if (currentPawn.health.hediffSet.TryGetDirectlyAddedPartFor(childPart, out Hediff existing))
                        replacedParts.Add(existing.Label);
                }
                if (replacedParts.Count > 0)
                    detailSb.Append($". {"Replaces".Translate()}: {replacedParts.ToCommaList().CapitalizeFirst()}");
            }

            TolkHelper.SpeakData(detailSb.ToString());
        }

        private void OpenAddRecipeMenu(int startIndex, bool silent)
        {
            List<RecipeDef> recipes = HealthTabHelper.GetAvailableRecipes(currentPawn);
            if (recipes.Count == 0)
            {
                TolkHelper.Speak("NoneLower".Loc());
                SoundDefOf.ClickReject.PlayOneShotOnCamera();
                return;
            }

            var options = new List<FloatMenuOption>();
            for (int i = 0; i < recipes.Count; i++)
            {
                RecipeDef recipe = recipes[i];
                int index = i;
                var option = new FloatMenuOption(recipe.LabelCap.ToString(),
                    delegate { ActivateRecipe(recipe, index); });
                string detail = DescribeRecipe(recipe);
                if (!string.IsNullOrEmpty(detail))
                    option.tooltip = new TipSignal(detail);
                options.Add(option);
            }
            OpenMenu(options, delegate { ReturnToList(playClick: true); }, startIndex, silent);
        }

        private void ActivateRecipe(RecipeDef recipe, int recipeIndex)
        {
            // A listed recipe can still be globally refused with an explanation, matching
            // HealthCardUtility.DrawMedOperationsTab, which shows such recipes with a null action.
            // Refuse activation and announce the same reason.
            AcceptanceReport recipeReport = recipe.Worker.AvailableReport(currentPawn);
            if (!recipeReport.Accepted && !recipeReport.Reason.NullOrEmpty())
            {
                TolkHelper.Speak("CannotUseReason".Loc(recipeReport.Reason), SpeechPriority.High);
                SoundDefOf.ClickReject.PlayOneShotOnCamera();
                OpenAddRecipeMenu(recipeIndex, silent: true);
                return;
            }

            List<BodyPartRecord> parts = HealthTabHelper.GetPartsForRecipe(currentPawn, recipe);

            if (parts.Count == 0)
            {
                AcceptanceReport report = recipe.Worker.AvailableReport(currentPawn);
                if (report.Accepted && recipe.Worker.AvailableOnNow(currentPawn, null))
                {
                    CommitOperation(recipe, null);
                }
                else
                {
                    RefuseOperation(report, delegate { OpenAddRecipeMenu(recipeIndex, silent: true); });
                }
            }
            else if (parts.Count == 1)
            {
                // One valid part, gated as the body-part menu gates its own choices below: vanilla
                // filters parts through AvailableOnNow before ever listing them.
                if (recipe.Worker.AvailableOnNow(currentPawn, parts[0]))
                {
                    CommitOperation(recipe, parts[0]);
                }
                else
                {
                    RefuseOperation(recipe.Worker.AvailableReport(currentPawn),
                        delegate { OpenAddRecipeMenu(recipeIndex, silent: true); });
                }
            }
            else
            {
                OpenBodyPartMenu(recipe, parts, recipeIndex, 0, silent: false);
            }
        }

        private void OpenBodyPartMenu(RecipeDef recipe, List<BodyPartRecord> parts, int recipeIndex, int startIndex, bool silent)
        {
            var options = new List<FloatMenuOption>();
            for (int i = 0; i < parts.Count; i++)
            {
                BodyPartRecord part = parts[i];
                int index = i;
                var option = new FloatMenuOption(
                    recipe.Worker.GetLabelWhenUsedOn(currentPawn, part).CapitalizeFirst() + ", " + part.LabelCap,
                    delegate { ActivatePart(recipe, parts, recipeIndex, index); });
                string detail = DescribePart(recipe, part);
                if (!string.IsNullOrEmpty(detail))
                    option.tooltip = new TipSignal(detail);
                options.Add(option);
            }
            // Escape steps back to the recipe it drilled in from, keeping the two-step drill.
            OpenMenu(options, delegate { OpenAddRecipeMenu(recipeIndex, silent: false); }, startIndex, silent);
        }

        private void ActivatePart(RecipeDef recipe, List<BodyPartRecord> parts, int recipeIndex, int partIndex)
        {
            BodyPartRecord part = parts[partIndex];
            if (recipe.Worker.AvailableOnNow(currentPawn, part))
            {
                CommitOperation(recipe, part);
                return;
            }
            RefuseOperation(recipe.Worker.AvailableReport(currentPawn),
                delegate { OpenBodyPartMenu(recipe, parts, recipeIndex, partIndex, silent: true); });
        }

        private void CommitOperation(RecipeDef recipe, BodyPartRecord part)
        {
            HealthTabHelper.AddOperation(currentPawn, recipe, part);
            SoundDefOf.Click.PlayOneShotOnCamera();
            queuedOperations = HealthTabHelper.GetQueuedOperations(currentPawn);
            operationIndex = 0;
            ReturnToList(playClick: false);
        }

        private static void RefuseOperation(AcceptanceReport report, Action reopen)
        {
            string reason = report.Reason.NullOrEmpty() ? "" : report.Reason;
            TolkHelper.Speak("CannotUseReason".Loc(reason), SpeechPriority.High);
            SoundDefOf.ClickReject.PlayOneShotOnCamera();
            reopen();
        }

        /// <summary>
        /// Everything about a recipe beyond its label and position, riding the option's tooltip so
        /// <see cref="WindowlessFloatMenuState"/> speaks it while the drawn row stays readable.
        /// </summary>
        private string DescribeRecipe(RecipeDef recipe)
        {
            var sb = new StringBuilder();

            // Refused-with-a-reason recipes stay browsable, as in vanilla; announce why, since
            // activating the row refuses with the same reason.
            AcceptanceReport recipeReport = recipe.Worker.AvailableReport(currentPawn);
            if (!recipeReport.Accepted && !recipeReport.Reason.NullOrEmpty())
                AppendSentence(sb, "RimWorldAccess.Health.RecipeDisabledReason".Translate(recipeReport.Reason));

            if (!string.IsNullOrEmpty(recipe.description))
                AppendSentence(sb, recipe.description);

            if (recipe.skillRequirements != null && recipe.skillRequirements.Count > 0)
            {
                string skills = recipe.skillRequirements
                    .Select(sr => $"{sr.skill.LabelCap} {sr.minLevel}")
                    .ToCommaList();
                AppendSentence(sb, $"{"Requires".Translate()}: {skills}");
            }

            if (recipe.ingredients != null && recipe.ingredients.Count > 0)
                AppendSentence(sb, $"{"Ingredients".Translate()}: {recipe.ingredients.Select(i => i.Summary).ToCommaList()}");

            AppendMissingIngredients(sb, recipe);
            return sb.ToString();
        }

        /// <summary>The body-part twin of <see cref="DescribeRecipe"/>.</summary>
        private string DescribePart(RecipeDef recipe, BodyPartRecord part)
        {
            var sb = new StringBuilder();

            float health = currentPawn.health.hediffSet.GetPartHealth(part);
            float maxHealth = part.def.GetMaxHealth(currentPawn);
            var conditionLabel = HealthUtility.GetPartConditionLabel(currentPawn, part);
            AppendSentence(sb, $"{conditionLabel.First}, {health:F0} / {maxHealth:F0}");

            // Rejection reason or missing ingredients, as vanilla's own float menu shows.
            if (!recipe.Worker.AvailableOnNow(currentPawn, part))
            {
                AcceptanceReport partReport = recipe.Worker.AvailableReport(currentPawn);
                if (!partReport.Reason.NullOrEmpty())
                    AppendSentence(sb, partReport.Reason);
            }
            else
            {
                AppendMissingIngredients(sb, recipe);
            }

            return sb.ToString();
        }

        private void AppendMissingIngredients(StringBuilder sb, RecipeDef recipe)
        {
            if (currentPawn.MapHeld == null)
                return;
            var missing = recipe.PotentiallyMissingIngredients(null, currentPawn.MapHeld).ToList();
            if (missing.Count == 0)
                return;
            AppendSentence(sb, string.Join(", ", missing.Select(
                x => "MissingMedicalBillIngredient".Translate(x.label).ToString())));
        }

        private static void AppendSentence(StringBuilder sb, string sentence)
        {
            if (sb.Length > 0)
                sb.Append(". ");
            sb.Append(sentence);
        }

        // Reorder.

        private void ReorderOperation(int offset)
        {
            if (operationIndex >= queuedOperations.Count)
            {
                SoundDefOf.ClickReject.PlayOneShotOnCamera();
                return;
            }

            Bill bill = queuedOperations[operationIndex];
            int newIndex = operationIndex + offset;

            if (newIndex < 0)
            {
                MenuHelper.SpeakAlreadyAtEdge(MenuHelper.EdgeDirection.Top);
                return;
            }
            if (newIndex >= queuedOperations.Count)
            {
                MenuHelper.SpeakAlreadyAtEdge(MenuHelper.EdgeDirection.Bottom);
                return;
            }

            currentPawn.BillStack.Reorder(bill, offset);
            queuedOperations = HealthTabHelper.GetQueuedOperations(currentPawn);
            operationIndex = newIndex;
            SyncModelCursor();
            SoundDefOf.Tick_Tiny.PlayOneShotOnCamera();
            AnnounceCurrentSelection();
        }

        // Announcements.

        private void AnnounceCurrentSelection()
        {
            var sb = new StringBuilder();

            switch (currentLevel)
            {
                case Level.MedicalSettingsList:
                    // RebuildVisibleSettings leaves this empty for a pawn the health card shows
                    // none of these controls for; announce that rather than indexing nothing.
                    if (visibleSettingIds.Count == 0)
                    {
                        sb.Append("NoneLower".Translate().ToString());
                        break;
                    }
                    int visibleSettingId = visibleSettingIds[medicalSettingIndex];
                    string settingLabel = GetMedicalSettingLabel(visibleSettingId);
                    if (visibleSettingId == SettingFoodRestriction)
                    {
                        string currentFood = HealthTabHelper.GetCurrentFoodRestriction(currentPawn);
                        sb.Append($"{settingLabel}: {currentFood}");
                        sb.Append($". {"FoodRestrictionDescription".Translate()}");
                    }
                    else if (visibleSettingId == SettingMedicalCare)
                    {
                        string currentCare = HealthTabHelper.GetCurrentMedicalCare(currentPawn);
                        sb.Append($"{settingLabel}: {currentCare}");
                        sb.Append($". {"MedicineQualityDescription".Translate()}");
                    }
                    else if (visibleSettingId == SettingSelfTend)
                    {
                        bool enabled = HealthTabHelper.GetSelfTendEnabled(currentPawn);
                        sb.Append($"{settingLabel}: {(enabled ? "On".Translate().ToString() : "Off".Translate().ToString())}");
                        sb.Append($". {"AllowSelfTendTip".Translate(Faction.OfPlayer.def.pawnsPlural, TendUtility.SelfTendQualityFactor.ToStringPercent())}");
                    }

                    {
                        string pos = MenuHelper.FormatPosition(medicalSettingIndex, visibleSettingIds.Count);
                        if (!string.IsNullOrEmpty(pos)) sb.Append($", {pos}");
                    }
                    break;

                case Level.OperationsList:
                    {
                        if (operationIndex < queuedOperations.Count)
                        {
                            Bill bill = queuedOperations[operationIndex];
                            sb.Append(bill.LabelCap.StripTags());
                        }
                        else
                        {
                            sb.Append("AddBill".Translate().ToString());
                        }
                        string opsPos = MenuHelper.FormatPosition(operationIndex, queuedOperations.Count + 1);
                        if (!string.IsNullOrEmpty(opsPos)) sb.Append($", {opsPos}");
                    }
                    break;
            }

            TolkHelper.SpeakData(sb.ToString());
        }

        private void AnnounceWithSearch()
        {
            TypeaheadSearchHelper activeTypeahead = null;
            string itemLabel = null;

            if (currentLevel == Level.MedicalSettingsList && settingsTypeahead.HasActiveSearch)
            {
                activeTypeahead = settingsTypeahead;
                itemLabel = GetMedicalSettingLabel(visibleSettingIds[medicalSettingIndex]);
            }
            else if (currentLevel == Level.OperationsList && operationsTypeahead.HasActiveSearch)
            {
                activeTypeahead = operationsTypeahead;
                if (operationIndex >= 0 && operationIndex < queuedOperations.Count)
                    itemLabel = queuedOperations[operationIndex].LabelCap.StripTags();
            }

            if (activeTypeahead != null && itemLabel != null)
            {
                TolkHelper.SpeakData(activeTypeahead.BuildItemAnnouncement(itemLabel));
            }
            else
            {
                AnnounceCurrentSelection();
            }
        }
    }

    /// <summary>
    /// Keeps <see cref="HealthTabScope"/> in lockstep with <see cref="HealthTabState.IsActive"/>,
    /// reconciled immediately after <see cref="InspectionScopeMirror"/> so stack order alone gives
    /// it the keyboard over the still-open tree. Stands down while an info card is open — the
    /// universal yield every mirror applies, kept as the default even though no live path reaches
    /// one from here. HealthTabState and PrisonerTabState need no sibling gate: both open from the
    /// same tree, and each masks it while open.
    /// </summary>
    internal static class HealthTabScopeMirror
    {
        private static readonly HealthTabScope scope = new HealthTabScope();

        public static void Reconcile()
        {
            if (HealthTabState.IsActive && !InfoCardState.IsActive)
            {
                FocusStack.Push(scope);
            }
            else
            {
                FocusStack.Pop(scope);
            }
        }
    }
}
