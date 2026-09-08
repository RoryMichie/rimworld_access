using System;
using System.Collections.Generic;
using System.Linq;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.Sound;

namespace RimWorldAccess.Shell
{
    /// <summary>
    /// The keyboard focus scope for <c>Dialog_ReformIdeo</c> (the in-game two-stage
    /// fluid-ideoligion reform dialog).
    /// Window-attached via <see cref="ScopeForWindow.Register"/> (src/Shell/Actions/ShellBootstrap.Game.cs).
    /// <see cref="RimWorldAccess.IdeoReformState"/> is a slim facade — reflection surface,
    /// row source, and mutation vehicles only; see its own class remarks.
    ///
    /// <b>One content region whose identity flips with the dialog's stage</b> — the two stages are
    /// mutually exclusive, never both live.
    /// <list type="number">
    /// <item><b>Changes</b> (stage 1) — the three "choose one change" rows from
    /// <see cref="RimWorldAccess.IdeoReformState.BuildStage1Actions"/>, each a Button row that goes
    /// Disabled, carrying the resolved <c>MessageFluidIdeoOneChangeAllowed</c> text as
    /// <see cref="ElementDescription.Extras"/>, once the fluid one-change rule has spent the change
    /// elsewhere: the row stays navigable and says why. Enter on an enabled row invokes its own
    /// vanilla delegate. The style picker is a <see cref="WindowlessFloatMenuState"/> flow whose
    /// completion reaches this scope only through <see cref="RimWorldAccess.IdeoEditNotifyHub"/>,
    /// never <see cref="OnFocus"/> — see <see cref="NotifyIdeoEdited"/>.</item>
    /// <item><b>Edit</b> (stage 2) — an <see cref="IdeoEditorRegionCore"/> over
    /// <see cref="RimWorldAccess.IdeoReformState.NewIdeo"/>, the reform's scratch working copy, so
    /// every edit here mutates that and never the live ideoligion. The meme/style facets are
    /// filtered out as stage-1 concerns, and the dev-toggle/fluid-points/debug-button options are all
    /// false because <c>Dialog_ReformIdeo</c> never calls
    /// <c>DoIdeoList</c>/<c>DoIdeoDetails</c>/<c>DoFluidIdeo</c>/<c>DoDebugButtons</c>; it hand-calls
    /// the same leaf editors worldgen's <c>DoIdeoDetails</c> calls.
    /// <see cref="IdeoEditorRegionCore.Options.ValidationOrImpactText"/> composes the impact line,
    /// the player warning and the incompatible-precept pair into one persistent status row, replacing
    /// a proactive push-announce with the shared "visit the row" convention.</item>
    /// </list>
    /// Stage swap is structural: <see cref="GoToStage"/> rebuilds the model, resets the cursor to the
    /// top of the new region and speaks a full opening announcement.
    ///
    /// <b>Buttons region, stage-aware.</b> <see cref="CaptureWindowButtons"/> is false because these
    /// declared actions ride the same vanilla vehicles under the same literal labels vanilla's own
    /// buttons draw, so capture would double-present every one. Stage 1 offers Cancel (plain
    /// <c>dialog.Close()</c>; reform is optional, nothing is asked), Reset changes (only while
    /// <see cref="RimWorldAccess.IdeoReformState.AnyChooseOneChanges"/>) and Next. Stage 2 offers
    /// Back, Randomize and "DoneButton", which runs the incompatible-precept gate and then
    /// <c>IdeoDevelopmentUtility.ConfirmChangesToIdeo</c>, whose own lost-precept confirmation box
    /// appears on its own and is never pre-empted. Reset changes/Randomize share one action id and
    /// Next/DoneButton share another, so one wiring covers both stages.
    ///
    /// <b>Escape/Enter.</b> <c>Dialog_ReformIdeo</c> leaves <c>closeOnCancel</c>/<c>closeOnAccept</c>
    /// true and overrides neither key handler, so an unmitigated Escape or Enter would silently close
    /// the dialog and discard the scratch copy. <see cref="OwnsCancel"/> is therefore overridden
    /// unconditionally true: a live claiming modal scope's own swallow eats an unclaimed key BEFORE
    /// vanilla's independent <c>Window.OnCancelKeyPressed</c> poll runs, so fallthrough never works
    /// in practice. Every reachable state claims Cancel explicitly and stamps
    /// <see cref="ShellFrameStamps.MarkCancelConsumed"/> as a second layer for the
    /// <see cref="WindowKeyRouter"/> gate. The Cancel claim is gated only on no active typeahead
    /// search; stage 2 backs to stage 1, stage 1 closes.
    /// </summary>
    public sealed class IdeoReformScreenScope : ScreenScope
    {
        private enum RegionKind { Changes, Edit }

        private readonly Dialog_ReformIdeo dialog;
        private readonly IdeoEditorRegionCore editorCore;
        private readonly List<ScreenAction> actions = new List<ScreenAction>(3);
        private List<IdeoReformState.Stage1Action> changeRows = new List<IdeoReformState.Stage1Action>();
        private bool announcedOpen;

        /// <summary>
        /// The <see cref="IdeoBoxDrawPatch.FollowTarget"/> registrant this dialog draws over: its
        /// opening button lives inside a live <c>DoIdeoDetails</c> pane. Captured in
        /// <see cref="OnPush"/> and RESTORED, not nulled, in <see cref="OnPop"/>, so that host's own
        /// follow keeps working once this dialog closes.
        /// </summary>
        private Func<Rect?> previousFollowTarget;

        public IdeoReformScreenScope(Dialog_ReformIdeo dialog)
        {
            this.dialog = dialog;

            editorCore = new IdeoEditorRegionCore(new IdeoEditorRegionCore.Options
            {
                DisplayIdeo = () => IdeoReformState.NewIdeo,
                RefreshModel = RefreshModel,
                AnnounceCurrentItem = AnnounceCurrentItem,
                IncludeDevToggles = false, // Dialog_ReformIdeo never draws the dev list/details chrome at all (census Q1) -- no dev toggles exist on this dialog.
                IncludeFluidDevPoints = false, // reform's own points gate is entry-only (census Q2); the scratch newIdeo never draws DoFluidIdeo.
                IncludeDebugButtons = false, // decompiled-verified: no DoDebugButtons call in either stage.
                IncludeSection = kind => kind != IdeoBuilderHelper.SectionKind.StructureMeme
                    && kind != IdeoBuilderHelper.SectionKind.NormalMemes
                    && kind != IdeoBuilderHelper.SectionKind.Styles, // stage-1 concerns (retired IdeoReformState.RebuildForStage precedent, decompiled :117-119).
                ValidationOrImpactText = BuildValidationOrImpactText,
            });

            // The only way this scope learns that the style picker — a WindowlessFloatMenuState flow
            // with no scope of its own — committed a change.
            IdeoEditNotifyHub.Register(NotifyIdeoEdited);

            Claim(SharedMenuGrammar.Info, OnInfo);
            Claim("ideoReform.advanceOrApply", OnAdvanceOrApply);
            Claim("ideoReform.resetOrRandomize", e => ResetOrRandomize());
            // Gated only on no active search: the base's typeahead-clear claim wins while one is live.
            Claim(SharedMenuGrammar.Cancel, OnCancelKey, when: () => !TypeaheadHasActiveSearch);
        }

        public override string Name
        {
            get { return "ideo-reform"; }
        }

        protected internal override Window OwnedWindow
        {
            get { return dialog; }
        }

        protected override bool IncludeCapturedExtrasRegion
        {
            get { return true; }
        }

        /// <summary>
        /// False: the declared actions below mirror vanilla's own buttons under identical literal
        /// labels, so capture would double-present every one of them.
        /// </summary>
        protected override bool CaptureWindowButtons
        {
            get { return false; }
        }

        protected override bool EnableTypeahead
        {
            get { return true; }
        }

        /// <summary>Unconditionally true — see the class remarks: fallthrough to vanilla's own Window.OnCancelKeyPressed never happens under a live claiming modal scope.</summary>
        public override bool OwnsCancel
        {
            get { return true; }
        }

        // ------------------------------------------------------------------
        // Content model: one region, whose identity flips with the dialog's own stage.
        // ------------------------------------------------------------------

        private RegionKind CurrentKind
        {
            get { return IdeoReformState.Stage == IdeoReformStage.MemesAndStyles ? RegionKind.Changes : RegionKind.Edit; }
        }

        protected override void RefreshContent()
        {
            if (CurrentKind == RegionKind.Changes)
            {
                changeRows = IdeoReformState.BuildStage1Actions();
            }
            else
            {
                editorCore.Rebuild();
            }
        }

        protected override int ContentRegionCount
        {
            get { return 1; }
        }

        protected override string ContentRegionName(int region)
        {
            return CurrentKind == RegionKind.Changes
                ? (string)"RimWorldAccess.Ideology.Reform.ChangesRegion".Translate()
                : (string)"RimWorldAccess.Ideology.Reform.EditRegion".Translate();
        }

        protected override int ContentItemCount(int region)
        {
            return CurrentKind == RegionKind.Changes ? changeRows.Count : editorCore.RowCount;
        }

        protected override ElementDescription DescribeContentItem(int region, int index)
        {
            return CurrentKind == RegionKind.Changes ? DescribeChangeRow(index) : editorCore.Describe(index);
        }

        protected override void ActivateContentItem(int region, int index)
        {
            if (CurrentKind == RegionKind.Changes)
            {
                ActivateChangeRow(index);
            }
            else
            {
                editorCore.Activate(index);
            }
        }

        // ------------------------------------------------------------------
        // Changes region (stage 1).
        // ------------------------------------------------------------------

        private ElementDescription DescribeChangeRow(int index)
        {
            var d = new ElementDescription();
            if (index < 0 || index >= changeRows.Count)
            {
                return d;
            }
            IdeoReformState.Stage1Action row = changeRows[index];
            d.Label = row.Label;
            d.Role = ElementRole.Button;
            d.Disabled = !row.Enabled;
            if (!row.Enabled && !string.IsNullOrEmpty(row.DisabledReason))
            {
                d.Extras = row.DisabledReason;
            }
            return d;
        }

        /// <summary>Mirrors <see cref="IdeoEditorRegionCore.ActivateSectionRow"/>: a disabled row stays navigable and speaks why.</summary>
        private void ActivateChangeRow(int index)
        {
            if (index < 0 || index >= changeRows.Count)
            {
                return;
            }
            IdeoReformState.Stage1Action row = changeRows[index];
            if (!row.Enabled)
            {
                SoundDefOf.ClickReject.PlayOneShotOnCamera();
                TolkHelper.SpeakData(!string.IsNullOrEmpty(row.DisabledReason)
                    ? row.DisabledReason
                    : (string)"RimWorldAccess.Ideology.Builder.Unavailable".Translate(), SpeechPriority.High);
                return;
            }
            row.Activate?.Invoke();
        }

        // ------------------------------------------------------------------
        // Edit region (stage 2) status row policy.
        // ------------------------------------------------------------------

        /// <summary>
        /// Impact readout, non-blocking player warning, and the incompatible-precept pair formatted
        /// like vanilla's own red label, as one row rather than a push-announce. Re-evaluated on every
        /// <see cref="IdeoEditorRegionCore.Rebuild"/>.
        /// </summary>
        private string BuildValidationOrImpactText()
        {
            Ideo n = IdeoReformState.NewIdeo;
            if (n == null)
            {
                return "";
            }
            var parts = new List<string>();
            string impact = IdeoReformState.BuildImpactLine();
            if (!string.IsNullOrEmpty(impact))
            {
                parts.Add(impact);
            }
            string warning = IdeoBuilderHelper.BuildPlayerWarning(n);
            if (!string.IsNullOrEmpty(warning))
            {
                parts.Add(warning);
            }
            Pair<Precept, Precept> pair = n.FirstIncompatiblePreceptPair();
            if (pair != default(Pair<Precept, Precept>))
            {
                parts.Add(((string)"MessageIdeoIncompatiblePrecepts".Translate(
                    pair.First.Label.Named("PRECEPT1"), pair.Second.Label.Named("PRECEPT2"))).CapitalizeFirst());
            }
            return string.Join(". ", parts);
        }

        // ------------------------------------------------------------------
        // Edit-region focus ring (see IdeoBoxDrawPatch).
        // ------------------------------------------------------------------

        /// <summary>
        /// The Edit row the cursor rests on, or -1 on stage 1 (whose meme and style boxes belong to
        /// the Changes region, unringed this wave) and off-row.
        /// </summary>
        private int FocusedEditRow()
        {
            if (CurrentKind != RegionKind.Edit)
            {
                return -1;
            }
            ListModel region = Model.CurrentRegion;
            return region != null && !region.IsEmpty ? region.Index : -1;
        }

        protected internal override Rect FocusedContentRect()
        {
            int row = FocusedEditRow();
            return row >= 0 ? editorCore.RowScreenRect(row) : default(Rect);
        }

        /// <summary>
        /// This dialog lays stage 2 out itself over its own private scroll position rather than
        /// calling <c>IdeoUIUtility.DoIdeoDetails</c>, so the follow write reaches it through
        /// <see cref="IdeoBoxDrawPatch.ReformIdeoScrollFollowPatch"/>'s FieldRef seam instead of the
        /// shared postfix. The request/target engine is the same one every other host uses.
        /// </summary>
        private Rect? FollowTargetRect()
        {
            int row = FocusedEditRow();
            return row >= 0 ? editorCore.RowRawRect(row) : null;
        }

        protected override void OnCursorSettled(int region, int index)
        {
            base.OnCursorSettled(region, index);
            if (CurrentKind == RegionKind.Edit)
            {
                IdeoBoxDrawPatch.RequestFollow();
            }
        }

        // ------------------------------------------------------------------
        // Stage swap (structural change).
        // ------------------------------------------------------------------

        private void GoToStage(IdeoReformStage stage)
        {
            IdeoReformState.Stage = stage;
            RefreshModel();
            Model.MoveToRegion(0);
            ListModel region = Model.Region(0);
            if (region != null && !region.IsEmpty)
            {
                region.MoveFirst();
            }
            AnnounceStageOpening(includeIntro: false);
        }

        /// <summary>Speaks the stage: the header/description intro on first open only, else the stage guidance line, plus the current row.</summary>
        private void AnnounceStageOpening(bool includeIntro)
        {
            var parts = new List<string>();
            if (includeIntro)
            {
                parts.Add((string)"ReformIdeoligion".Translate());
                parts.Add((string)"ReformIdeoligionDesc".Translate());
            }
            if (IdeoReformState.Stage == IdeoReformStage.MemesAndStyles)
            {
                parts.Add((string)"ReformIdeoChooseOneChange".Translate());
            }
            else
            {
                parts.Add((string)"ReformIdeoChangeAny".Translate());
                string impact = IdeoReformState.BuildImpactLine();
                if (!string.IsNullOrEmpty(impact))
                {
                    parts.Add(impact);
                }
            }
            ListModel region = Model.Region(0);
            if (region != null && !region.IsEmpty)
            {
                ElementDescription item = DescribeContentItem(0, region.Index);
                parts.Add(AnnouncementComposer.ComposeFocus(item, TranslatedShellVocabulary.Instance, TextDialogShared.StandardComposeOptions()));
            }
            TolkHelper.SpeakData(string.Join(". ", parts.Where(p => !string.IsNullOrEmpty(p))), SpeechPriority.High);
        }

        // ------------------------------------------------------------------
        // Buttons region: Cancel/Reset changes/Next (stage 1) or Back/Randomize/DoneButton (stage 2).
        // ------------------------------------------------------------------

        /// <summary>Shift+Enter presses Next/Done from anywhere; both stages declare the same id.</summary>
        protected override string DefaultAcceptActionId
        {
            get { return "ideoReform.advanceOrApply"; }
        }

        protected override IReadOnlyList<ScreenAction> DeclaredActions
        {
            get
            {
                actions.Clear();
                if (IdeoReformState.Stage == IdeoReformStage.MemesAndStyles)
                {
                    actions.Add(new ScreenAction((string)"Cancel".Translate(), CancelAction, SharedMenuGrammar.Cancel));
                    if (IdeoReformState.AnyChooseOneChanges)
                    {
                        actions.Add(new ScreenAction((string)"ReformIdeoResetChanges".Translate(), ResetOrRandomize, "ideoReform.resetOrRandomize"));
                    }
                    actions.Add(new ScreenAction((string)"Next".Translate(), AdvanceOrApply, "ideoReform.advanceOrApply"));
                }
                else
                {
                    actions.Add(new ScreenAction((string)"Back".Translate(), BackAction, SharedMenuGrammar.Cancel));
                    actions.Add(new ScreenAction((string)"Randomize".Translate(), ResetOrRandomize, "ideoReform.resetOrRandomize"));
                    actions.Add(new ScreenAction((string)"DoneButton".Translate(), AdvanceOrApply, "ideoReform.advanceOrApply"));
                }
                return actions;
            }
        }

        private void OnAdvanceOrApply(KeyEventSnapshot e)
        {
            ShellFrameStamps.MarkAcceptConsumed();
            AdvanceOrApply();
        }

        private void AdvanceOrApply()
        {
            if (IdeoReformState.Stage == IdeoReformStage.MemesAndStyles)
            {
                GoToStage(IdeoReformStage.PreceptsNarrativeAndDeities);
            }
            else
            {
                // Confirm honors the incompatible-precept gate, then rides
                // IdeoDevelopmentUtility.ConfirmChangesToIdeo, whose own lost-precept box appears.
                IdeoReformState.Confirm();
            }
        }

        private void ResetOrRandomize()
        {
            if (IdeoReformState.Stage == IdeoReformStage.MemesAndStyles)
            {
                if (!IdeoReformState.AnyChooseOneChanges)
                {
                    SoundDefOf.ClickReject.PlayOneShotOnCamera();
                    return;
                }
                IdeoReformState.ResetChanges();
            }
            else
            {
                IdeoReformState.RandomizeNewIdeo();
            }
            RefreshModel();
            AnnounceCurrentItem();
        }

        private void CancelAction()
        {
            // Vanilla's own stage-1 Cancel body: reform is optional, so nothing is asked.
            ShellFrameStamps.MarkCancelConsumed();
            dialog.Close();
        }

        private void BackAction()
        {
            ShellFrameStamps.MarkCancelConsumed();
            GoToStage(IdeoReformStage.MemesAndStyles);
        }

        private void OnCancelKey(KeyEventSnapshot e)
        {
            if (IdeoReformState.Stage == IdeoReformStage.PreceptsNarrativeAndDeities)
            {
                BackAction();
            }
            else
            {
                CancelAction();
            }
        }

        // ------------------------------------------------------------------
        // Info card drill-in. Only Edit-region Section rows BuildSections marks inspectable carry any
        // (the meme sections are excluded from this stage).
        // ------------------------------------------------------------------

        private void OnInfo(KeyEventSnapshot e)
        {
            RefreshModel();
            List<Def> defs = CurrentRowInspectableDefs();
            if (defs.Count == 0)
            {
                InfoCardState.SpeakNoInfoCardAvailable();
                return;
            }
            if (defs.Count == 1)
            {
                InfoCardState.OpenInfoCardForDef(defs[0]);
                return;
            }
            var options = new List<FloatMenuOption>();
            foreach (Def def in defs)
            {
                Def captured = def;
                string label = def.label != null ? def.label.CapitalizeFirst() : def.defName;
                options.Add(new FloatMenuOption(label, delegate { InfoCardState.OpenInfoCardForDef(captured); }));
            }
            TolkHelper.Speak("RimWorldAccess.InfoCard.ChooseItemToInspect".Loc());
            WindowlessFloatMenuState.Open(options, false);
        }

        private List<Def> CurrentRowInspectableDefs()
        {
            if (CurrentKind != RegionKind.Edit)
            {
                return new List<Def>();
            }
            ListModel region = Model.CurrentRegion;
            if (region == null || region.IsEmpty || region.Index < 0 || region.Index >= editorCore.RowCount)
            {
                return new List<Def>();
            }
            return editorCore.InspectableDefs(region.Index);
        }

        // ------------------------------------------------------------------
        // Extras haystack.
        // ------------------------------------------------------------------

        protected override IEnumerable<string> AdditionalPresentedTexts
        {
            get
            {
                Ideo n = IdeoReformState.NewIdeo;
                // The title and description draw on BOTH stages, above the stage switch. They are
                // spoken once in the stage announcement but drawn every frame, so the captured-extras
                // diff needs them listed persistently whichever region is current.
                yield return (string)"ReformIdeoligion".Translate();
                yield return (string)"ReformIdeoligionDesc".Translate();
                if (CurrentKind == RegionKind.Changes)
                {
                    // The per-box header captions are already covered: BuildStage1Actions labels each
                    // Changes row with the identical literal key vanilla draws above the box.
                    yield return (string)"ReformIdeoChooseOneChange".Translate();
                    if (n != null)
                    {
                        // Each meme box draws the meme's OWN label, which the Changes rows above do
                        // not mirror — those speak vanilla's header caption, not the current pick.
                        if (n.StructureMeme != null)
                        {
                            yield return n.StructureMeme.LabelCap.ToString();
                            if (n.StructureMeme.Icon != null && !string.IsNullOrEmpty(n.StructureMeme.Icon.name))
                            {
                                yield return n.StructureMeme.Icon.name;
                            }
                        }
                        foreach (string text in IdeoDetailsPresentedTexts.NonStructureMemeNamesFused(n))
                        {
                            yield return text;
                        }
                        if (n.thingStyleCategories != null)
                        {
                            foreach (var style in n.thingStyleCategories)
                            {
                                if (style?.category?.Icon != null && !string.IsNullOrEmpty(style.category.Icon.name))
                                {
                                    yield return style.category.Icon.name;
                                }
                            }
                        }
                    }
                    yield break;
                }

                // Stage 2's own header: the stage announce is transient speech, not a persistent
                // presented text.
                yield return (string)"ReformIdeoChangeAny".Translate();

                // Stage 2 draws the same IdeoUIUtility leaf methods every other edit host presents.
                foreach (string text in IdeoDetailsPresentedTexts.EditModeCaption(editAffordancesVisible: true))
                {
                    yield return text;
                }
                foreach (string text in IdeoDetailsPresentedTexts.AddPreceptButtonLabels(editAffordancesVisible: true))
                {
                    yield return text;
                }
                foreach (string text in IdeoDetailsPresentedTexts.DeitySectionButtonLabels(editAffordancesVisible: true))
                {
                    yield return text;
                }
                foreach (string text in IdeoDetailsPresentedTexts.DescriptionLockIconStates(editAffordancesVisible: true))
                {
                    yield return text;
                }
                if (n != null)
                {
                    foreach (string text in IdeoDetailsPresentedTexts.ForIdeo(n))
                    {
                        yield return text;
                    }
                }
            }
        }

        // ------------------------------------------------------------------
        // Lifecycle.
        // ------------------------------------------------------------------

        public override void OnPush()
        {
            base.OnPush();
            IdeoBoxDrawPatch.AddInterest();
            // This dialog always opens over a live DoIdeoDetails host, so the slot is never empty
            // here; capture it and restore it in OnPop rather than nulling.
            previousFollowTarget = IdeoBoxDrawPatch.FollowTarget;
            IdeoBoxDrawPatch.FollowTarget = FollowTargetRect;
        }

        public override void OnPop()
        {
            IdeoBoxDrawPatch.FollowTarget = previousFollowTarget;
            IdeoBoxDrawPatch.RemoveInterest();
            base.OnPop();
            IdeoEditNotifyHub.Unregister(NotifyIdeoEdited);
        }

        /// <summary>
        /// Called by IdeoSymbolEditState's AfterEdit dispatcher while this scope is the top-of-stack
        /// registrant (the style-picker path): a plain refresh and re-announce, not a stage reopening.
        /// </summary>
        private void NotifyIdeoEdited(bool announce)
        {
            RefreshModel();
            if (announce)
            {
                AnnounceCurrentItem();
            }
        }

        public override void OnFocus()
        {
            base.OnFocus();
            if (announcedOpen)
            {
                // Returning from a child window, dialog or overlay editor: refresh and re-announce.
                RefreshModel();
                AnnounceCurrentItem();
                return;
            }
            announcedOpen = true;
            RefreshModel();
            Model.MoveToRegion(0);
            ListModel region = Model.Region(0);
            if (region != null && !region.IsEmpty)
            {
                region.MoveFirst();
            }
            AnnounceStageOpening(includeIntro: true);
        }
    }
}
