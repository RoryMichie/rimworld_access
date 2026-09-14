using System.Collections.Generic;
using RimWorld;
using Verse;
using Verse.Sound;

namespace RimWorldAccess.Shell
{
    /// <summary>
    /// The focus scope for the Learning Helper / docs reader
    /// (<see cref="LearningHelperState"/>). Windowless: the surface is the static
    /// state, so the scope rides the focus stack purely through
    /// <see cref="LearningHelperScopeMirror"/>'s Reconcile.
    /// Two content regions — "Lessons" (a show-all checkbox row, then concept rows)
    /// and "Lesson content" (the selected concept's help lines) — plus the Buttons
    /// region (Mark as Learned / Already Learned). Enter on a concept row jumps to
    /// the content region, whose buttons the reader flows down into; Escape from the
    /// lesson (content or buttons) commits, refreshes the list and returns to it,
    /// while Escape on the Lessons list closes the menu.
    /// Typeahead is scoped to the Lessons region alone
    /// (<see cref="ContentRegionSearchable"/>): a cross-region jump would skip
    /// <see cref="LearningHelperState.TrackLineVisit"/> for every line between the
    /// old and new cursor position. Progressive knowledge is begin-on-enter,
    /// track-on-move, commit-on-leave — never per-line.
    /// Modal, so unclaimed keys are swallowed; the backstop for code that does not
    /// consult the focus stack is the retained
    /// <see cref="KeyboardHelper.IsAnyAccessibilityMenuActive"/> membership, and
    /// MapNavigationPatch's camera-pan suppression list keeps its own term because
    /// raw <c>Input.GetKey</c> dolly polls bypass event dispatch entirely.
    /// A silent <see cref="LearningHelperState.Close"/> runs at both session
    /// boundaries (<c>GameStartPatch.Postfix</c> and
    /// <c>MainMenuAccessibilityPatch.Postfix</c>'s Entry reset): a leaked
    /// <c>IsActive</c> would have the mirror re-push this modal every pass,
    /// consuming every key everywhere.
    /// </summary>
    public sealed class LearningHelperScope : ScreenScope
    {
        private const int LessonsRegion = 0;
        private const int ContentRegion = 1;

        private int lastSeenOpenGeneration = -1;
        private bool announcedOpen;

        /// <summary>The region the cursor was in as of the last processed transition, so <see cref="OnRegionChanged"/> can tell leaving the content region from entering it.</summary>
        private int trackedRegionIndex = -1;

        public LearningHelperScope()
        {
            Claim(SharedMenuGrammar.Cancel, OnCancel);
        }

        public override string Name
        {
            get { return "learning-helper"; }
        }

        /// <summary>No owning Window exists for this windowless overlay — self-claim Escape.</summary>
        public override bool OwnsCancel
        {
            get { return true; }
        }

        /// <summary>No vanilla window backs this reader, so Mark as Learned is a DeclaredAction rather than scraped.</summary>
        protected override bool CaptureWindowButtons
        {
            get { return false; }
        }

        protected override bool EnableTypeahead
        {
            get { return true; }
        }

        /// <summary>
        /// Only the Lessons region is searchable: a search landing on an arbitrary
        /// content line would skip <see cref="LearningHelperState.TrackLineVisit"/> for
        /// every line it passed over, silently under-crediting progressive knowledge.
        /// </summary>
        protected override bool ContentRegionSearchable(int region)
        {
            return region == LessonsRegion;
        }

        protected override int ContentRegionCount
        {
            get { return 2; }
        }

        /// <summary>
        /// Down past the last help line continues into the Buttons region and Up
        /// from the first button returns; the Lessons region keeps its hard edge.
        /// </summary>
        protected override bool ContentFlowsToActions(int region)
        {
            return region == ContentRegion;
        }

        /// <summary>
        /// Vanilla fits the Mark-learned button inside the help-text box
        /// (decompiled RimWorld/LearningReadout.cs:308-361): the button ends the
        /// lesson rather than standing beside it.
        /// </summary>
        protected override bool ActionsRegionInTabCycle
        {
            get { return false; }
        }

        protected override string ContentRegionName(int region)
        {
            return region == LessonsRegion
                ? "RimWorldAccess.OverlayMigration.Learning.LessonsRegionName".Translate().ToString()
                : "RimWorldAccess.OverlayMigration.Learning.ContentRegionName".Translate().ToString();
        }

        protected override int ContentItemCount(int region)
        {
            if (region == LessonsRegion)
            {
                return 1 + LearningHelperState.ConceptCount;
            }
            ConceptDef conc = SelectedConcept();
            return conc != null ? LearningHelperState.HelpLines(conc).Length : 0;
        }

        protected override ElementDescription DescribeContentItem(int region, int index)
        {
            return region == LessonsRegion ? DescribeLessonsRow(index) : DescribeContentLine(index);
        }

        /// <summary>Row 0 is the "show all lessons" checkbox — vanilla's +/- expand toggle (decompiled RimWorld/LearningReadout.cs:152-163); rows 1..N are concept rows.</summary>
        private ElementDescription DescribeLessonsRow(int index)
        {
            var d = new ElementDescription();
            if (index == 0)
            {
                d.Label = "RimWorldAccess.OverlayMigration.Learning.ModeLabel".Translate().ToString();
                d.Role = ElementRole.Checkbox;
                d.Check = LearningHelperState.ShowAllMode ? CheckState.Checked : CheckState.Unchecked;
                return d;
            }
            ConceptDef conc = LearningHelperState.ConceptAt(index - 1);
            if (conc == null)
            {
                return d;
            }
            d.Label = ConceptHelpOverrides.DisplayLabel(conc) + ", " + LearningHelperState.KnowledgeText(conc);
            return d;
        }

        private ElementDescription DescribeContentLine(int index)
        {
            var d = new ElementDescription { ReadOnly = true };
            ConceptDef conc = SelectedConcept();
            if (conc == null)
            {
                return d;
            }
            string[] lines = LearningHelperState.HelpLines(conc);
            d.Label = index >= 0 && index < lines.Length ? lines[index] : "";
            return d;
        }

        /// <summary>Enter or Space on the mode checkbox toggles it; on a concept row it jumps to the Lesson content region; content lines are no-ops.</summary>
        protected override void ActivateContentItem(int region, int index)
        {
            if (region != LessonsRegion)
            {
                return;
            }
            if (index == 0)
            {
                ToggleMode();
                return;
            }
            EnterLessonContent(index);
        }

        /// <summary>Flip the show-all checkbox, mirroring vanilla's +/- toggle, and announce the resulting list.</summary>
        private void ToggleMode()
        {
            bool showAll = !LearningHelperState.ShowAllMode;
            LearningHelperState.SetMode(showAll);
            (showAll ? SoundDefOf.Checkbox_TurnedOn : SoundDefOf.Checkbox_TurnedOff).PlayOneShotOnCamera();
            RefreshModel();
            Model.CurrentRegion?.MoveFirst();
            AnnounceModeChange();
        }

        // Buttons region: Mark as Learned / Already Learned for the concept
        // currently open in the content region.
        protected override IReadOnlyList<ScreenAction> DeclaredActions
        {
            get { return BuildActions(); }
        }

        private List<ScreenAction> BuildActions()
        {
            var actions = new List<ScreenAction>();
            ConceptDef conc = SelectedConcept();
            if (conc == null)
            {
                return actions;
            }
            if (LearningHelperState.IsComplete(conc))
            {
                // The base's ActivateActionRow speaks the refusal and reason on Enter,
                // so a disabled row needs no Activate delegate.
                actions.Add(new ScreenAction(
                    "AlreadyLearned".Translate().ToString(),
                    null,
                    disabled: true,
                    disabledReason: "AlreadyLearned".Translate().ToString()));
            }
            else
            {
                actions.Add(new ScreenAction("MarkLearned".Translate().ToString(), delegate { MarkLearned(conc); }));
            }
            return actions;
        }

        private void MarkLearned(ConceptDef conc)
        {
            try
            {
                LearningHelperState.MarkLearned(conc);
                RefreshModel();
                TolkHelper.Speak("RimWorldAccess.Learning.MarkLearnedConfirmation".Loc("MarkLearned".Translate()));
            }
            catch (System.Exception ex)
            {
                Log.Warning($"[RimWorld Access] Failed to activate learning helper button: {ex.Message}");
                TolkHelper.Speak("RimWorldAccess.Learning.ButtonActivationFailed".Loc());
            }
        }

        /// <summary>The concept the Lessons cursor rests on, or null on the mode row, an empty list, or before the model has ever been built.</summary>
        private ConceptDef SelectedConcept()
        {
            if (Model.RegionCount <= LessonsRegion)
            {
                return null;
            }
            ListModel region = Model.Region(LessonsRegion);
            if (region == null || region.IsEmpty)
            {
                return null;
            }
            return LearningHelperState.ConceptAt(region.Index - 1);
        }

        private void EnterLessonContent(int row)
        {
            ConceptDef conc = LearningHelperState.ConceptAt(row - 1);
            if (conc == null)
            {
                return;
            }
            MoveResult result = Model.MoveToRegion(ContentRegion);
            if (result.Changed)
            {
                OnRegionChanged(result);
            }
            RefreshModel();
            Model.CurrentRegion?.MoveFirst();
            TrackCurrentLineIfInContent();
            AnnounceRegion();
        }

        /// <summary>Escape from the lesson — its content region or its own Buttons region — commits, refreshes the list (a completion may drop the concept out of Active mode) and returns focus to it; only from the Lessons list itself does Escape close the menu.</summary>
        private void OnCancel(KeyEventSnapshot e)
        {
            ShellFrameStamps.MarkCancelConsumed();
            if (Model.RegionIndex == LessonsRegion)
            {
                LearningHelperState.CloseMenu();
            }
            else
            {
                ReturnToList();
            }
        }

        private void ReturnToList()
        {
            MoveResult result = Model.MoveToRegion(LessonsRegion);
            if (result.Changed)
            {
                OnRegionChanged(result);
            }
            else
            {
                LearningHelperState.CommitReading();
                LearningHelperState.RefreshConcepts();
            }
            RefreshModel();
            if (LearningHelperState.ConceptCount == 0)
            {
                string suffix = LearningHelperState.ShowAllMode
                    ? "RimWorldAccess.Learning.NoLessonsAvailable".Translate().ToString()
                    : "RimWorldAccess.OverlayMigration.Learning.NoActiveRemainingShort".Translate().ToString();
                TwoLevelMenuHelper.SpeakReturnToList(suffix);
                return;
            }
            TwoLevelMenuHelper.SpeakReturnToList();
            AnnounceCurrentItem();
        }

        /// <summary>
        /// Silent state sync: the caller always does the speaking. Commits and
        /// refreshes on leaving the content region regardless of destination, and
        /// begins progressive reading on entering it regardless of origin.
        /// </summary>
        protected override void OnRegionChanged(MoveResult result)
        {
            int current = Model.RegionIndex;
            int previous = trackedRegionIndex;
            trackedRegionIndex = current;

            // Knowledge commits whenever the cursor leaves the lesson text, so reading to the end
            // still ticks it up. The concept drops out of the Active list only when the player
            // steps back to the Lessons list, whether from the text or from the lesson's own
            // buttons — never on the way DOWN into those buttons: a finished lesson stays listed,
            // readable and pressable until the player actually steps away.
            bool leftLessonText = previous == ContentRegion && current != ContentRegion;
            bool steppedBackToList = current == LessonsRegion && previous != LessonsRegion;
            if (leftLessonText)
            {
                LearningHelperState.CommitReading();
            }
            if (steppedBackToList)
            {
                LearningHelperState.RefreshConcepts();
            }
            if (leftLessonText || steppedBackToList)
            {
                RefreshModel();
            }
            else if (current == ContentRegion)
            {
                LearningHelperState.BeginReading(SelectedConcept());
            }
        }

        protected override void MoveItem(int delta)
        {
            base.MoveItem(delta);
            TrackCurrentLineIfInContent();
        }

        protected override void MoveItemEdge(bool first)
        {
            base.MoveItemEdge(first);
            TrackCurrentLineIfInContent();
        }

        private void TrackCurrentLineIfInContent()
        {
            if (Model.RegionIndex != ContentRegion)
            {
                return;
            }
            ListModel region = Model.CurrentRegion;
            if (region == null || region.IsEmpty)
            {
                return;
            }
            LearningHelperState.TrackLineVisit(region.Index);
        }

        private void AnnounceModeChange()
        {
            string text;
            if (LearningHelperState.ShowAllMode)
            {
                text = LearningHelperState.ConceptCount > 0
                    ? "RimWorldAccess.OverlayMigration.Learning.ModeAllCount".Translate(LearningHelperState.ConceptCount).ToString()
                    : "RimWorldAccess.Learning.NoLessonsAvailable".Translate().ToString();
            }
            else
            {
                text = LearningHelperState.ConceptCount > 0
                    ? "RimWorldAccess.OverlayMigration.Learning.ModeActive".Translate().ToString()
                    : "RimWorldAccess.OverlayMigration.Learning.NoActiveShort".Translate().ToString();
            }
            TolkHelper.SpeakData(text);
        }

        // The opening announcement is one-shot, guarded by the state's open
        // generation.
        public override void OnPush()
        {
            base.OnPush();
            int generation = LearningHelperState.OpenGeneration;
            if (generation == lastSeenOpenGeneration)
            {
                return;
            }
            lastSeenOpenGeneration = generation;
            trackedRegionIndex = LessonsRegion;
            announcedOpen = false;
        }

        public override void OnFocus()
        {
            base.OnFocus();
            if (announcedOpen)
            {
                return;
            }
            announcedOpen = true;

            RefreshModel();
            Model.MoveToRegion(LessonsRegion);
            Model.CurrentRegion?.MoveFirst();

            if (LearningHelperState.ConceptCount == 0)
            {
                TolkHelper.SpeakData("LearningHelper".Translate() + " " + "RimWorldAccess.OverlayMigration.Learning.OpeningNoActive".Translate());
                return;
            }
            TolkHelper.Speak("LearningHelper".Loc());
            AnnounceCurrentItem();
        }
    }

    /// <summary>
    /// Keeps <see cref="LearningHelperScope"/> in lockstep with
    /// <see cref="LearningHelperState.IsActive"/>, and runs the announcement
    /// plumbing every pass.
    /// Reconciled in ShellDispatcher's mirror pass immediately before
    /// <see cref="WhatsNewScopeMirror"/>, which stays last of all: both scopes
    /// consume everything while active, and on the rare frame both flags are true
    /// WhatsNew must win the top position.
    /// </summary>
    internal static class LearningHelperScopeMirror
    {
        private static readonly LearningHelperScope scope = new LearningHelperScope();

        public static void Reconcile()
        {
            // Neither call routes input, and both must keep running every OnGUI
            // pass whether or not the reader is open: a new lesson can activate
            // while it is closed.
            LearningHelperPatch.FlushPending();
            DocsTeacher.PollDeferred();

            if (LearningHelperState.IsActive)
            {
                FocusStack.Push(scope);
            }
            else
            {
                FocusStack.Pop(scope);
            }
        }
    }

    /// <summary>
    /// The Shift+Slash Learning Helper opener. Registered on <see cref="MapScope"/> and
    /// <see cref="WorldScope"/> for the in-game map and world, and on every
    /// <see cref="ScreenScope"/> through its base ctor (<see cref="RegisterForScreen"/>) so
    /// '?' opens the helper on any screen — world-gen configuration or in-game menu — even
    /// while that screen is the topmost modal. Escape then dismisses it.
    /// On some non-US layouts a direct '?' arrives as <c>keyCode=None</c> with shift
    /// NOT held; <see cref="KeyboardHelper.RemapCharacterToKeyCode"/> recovers
    /// <c>KeyCode.Slash</c> but does not synthesize <c>shift</c>, so a Shift+Slash
    /// chord cannot match. <see cref="RegisterRemapped"/> covers that half.
    /// </summary>
    internal static class LearningHelperOpenerClaims
    {
        internal static void Register(FocusScope scope)
        {
            scope.RegisterExternalClaim("map.menu.learningHelper",
                delegate (KeyEventSnapshot e) { LearningHelperState.Open(); },
                when: LearningHelperOpenerLive);
        }

        /// <summary>
        /// The bare-Slash twin for remapped layouts. Registered on WorldScope and every
        /// screen, never MapScope, where bare Slash belongs to
        /// map.colonistBar.focusByCursor. WasCharacterRemapped is written at Harmony
        /// priority 900, ahead of the dispatcher's 850, so it is valid here.
        /// </summary>
        internal static void RegisterRemapped(FocusScope scope)
        {
            scope.RegisterExternalClaim("map.menu.learningHelperRemapped",
                delegate (KeyEventSnapshot e) { LearningHelperState.Open(); },
                when: delegate { return KeyboardHelper.WasCharacterRemapped && LearningHelperOpenerLive(); });
        }

        /// <summary>
        /// Both opener variants on a screen scope. Called from the <see cref="ScreenScope"/>
        /// base ctor, so every screen — world-gen configuration and in-game menu alike —
        /// answers '?' while it is the topmost modal.
        /// </summary>
        internal static void RegisterForScreen(FocusScope scope)
        {
            scope.RegisterExternalClaim("map.menu.learningHelper",
                delegate (KeyEventSnapshot e) { LearningHelperState.Open(); },
                when: LearningHelperOpenerLive);
            scope.RegisterExternalClaim("map.menu.learningHelperRemapped",
                delegate (KeyEventSnapshot e) { LearningHelperState.Open(); },
                when: delegate { return KeyboardHelper.WasCharacterRemapped && LearningHelperOpenerLive(); });
        }

        /// <summary>Whether a new helper session may be opened here.</summary>
        private static bool LearningHelperOpenerLive()
        {
            bool inGame = Current.ProgramState == ProgramState.Playing && Find.CurrentMap != null;
            // Find.Tutor exists from scenario-select on, before the world is generated, so the
            // helper is reachable across the whole world-gen flow, not just once a planet exists.
            bool inWorldSetup = Current.ProgramState == ProgramState.Entry && Find.Tutor != null;
            // No AnyLiveModal/WindowsPreventCameraMotion term: top-down dispatch already masks this
            // opener behind any modal above it, config screens host it while modal and camera-locking
            // themselves (this keyboard overlay needs no camera), and IsActive stops '?' re-entry.
            return !LearningHelperState.IsActive
                && (inGame || inWorldSetup)
                && !TutorSystem.TutorialMode
                && TutorSystem.AdaptiveTrainingEnabled;
        }
    }
}
