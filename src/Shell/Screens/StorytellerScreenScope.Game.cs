using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using RimWorld;
using Verse;

namespace RimWorldAccess.Shell
{
    /// <summary>
    /// The keyboard focus scope for the pre-game storyteller page
    /// (<see cref="Page_SelectStoryteller"/>), window-attached via ShellBootstrap. A thin subclass
    /// of <see cref="StorytellerScopeBase"/>, which holds the shared
    /// Storytellers/Difficulty/CustomDifficulty regions the in-game twin also uses.
    ///
    /// This subclass owns only the pre-game-specific parts: the Save mode radios; the Anomaly
    /// playstyle radios and, in a SEPARATE region, the selected playstyle's conditional sliders,
    /// split so arrowing through the radios never rewrites the sliders under the cursor; and the
    /// wizard Back/Next vehicle. Together the two Anomaly regions REPLACE
    /// <see cref="Dialog_AnomalySettings"/> for keyboard users, editing the page's live
    /// difficultyValues immediately rather than staging behind Accept/Cancel. Vanilla draws the
    /// Anomaly button and the SaveMode radios only while <c>ProgramState.Entry</c>, so the in-game
    /// twin needs neither region and has no wizard flow at all.
    ///
    /// <see cref="CaptureWindowButtons"/> is false because the page CONTENT draws its own
    /// ButtonTexts, so the Buttons region declares Back/Next itself.
    ///
    /// Every Enter is scope-routed, so the page's deferred Accept poll must never advance the
    /// wizard on an Enter this scope consumed: <see cref="StorytellerSelectionPatch_CanDoNext"/>
    /// blocks that poll while this scope is the live top unless the Next action set
    /// <see cref="AdvanceRequested"/>. Escape reaches vanilla's Back normally except while a
    /// typeahead search is active, when <see cref="StorytellerSelectionPatch_CanDoBack"/> holds
    /// Back so the base clears the search instead.
    /// </summary>
    public sealed class StorytellerScreenScope : StorytellerScopeBase
    {
        // The page's private storyteller/difficulty/difficultyValues; StorytellerSelectionPatch's
        // write helpers reuse these same refs.
        internal static readonly AccessTools.FieldRef<Page_SelectStoryteller, StorytellerDef> StorytellerField =
            AccessTools.FieldRefAccess<Page_SelectStoryteller, StorytellerDef>("storyteller");
        internal static readonly AccessTools.FieldRef<Page_SelectStoryteller, DifficultyDef> DifficultyField =
            AccessTools.FieldRefAccess<Page_SelectStoryteller, DifficultyDef>("difficulty");
        internal static readonly AccessTools.FieldRef<Page_SelectStoryteller, Difficulty> DifficultyValuesField =
            AccessTools.FieldRefAccess<Page_SelectStoryteller, Difficulty>("difficultyValues");

        private static readonly MethodInfo canDoNextMethod = AccessTools.Method(typeof(Page_SelectStoryteller), "CanDoNext");
        private static readonly MethodInfo doNextMethod = AccessTools.Method(typeof(Page), "DoNext");
        private static readonly MethodInfo canDoBackMethod = AccessTools.Method(typeof(Page), "CanDoBack");
        private static readonly MethodInfo doBackMethod = AccessTools.Method(typeof(Page), "DoBack");

        /// <summary>
        /// True only while the Next action drives the vanilla gate-and-advance, so
        /// <see cref="StorytellerSelectionPatch_CanDoNext"/> lets that one explicit CanDoNext call
        /// through while still blocking the raw keyboard Accept poll.
        /// </summary>
        internal static bool AdvanceRequested;

        private readonly Page_SelectStoryteller page;
        private readonly List<ScreenAction> actions = new List<ScreenAction>();

        // Pre-game-only region data, rebuilt every RefreshContent.
        private readonly List<AnomalyRow> anomalyRows = new List<AnomalyRow>();
        private readonly List<DifficultySetting> anomalySliderRows = new List<DifficultySetting>();

        public StorytellerScreenScope(Page_SelectStoryteller page)
        {
            this.page = page;
            // Escape = Back as a dispatcher claim: the raw Cancel poll in Page.DoBottomButtons only
            // fires when the page's window pass runs BEFORE the dispatcher's modal swallow, an
            // ordering that flips with IMGUI focus. The base's search-clear claim wins while a
            // typeahead search is active.
            Claim(SharedMenuGrammar.Cancel, e => EscapeBack(), when: () => !HasActiveTypeaheadSearch);
            Claim("storyteller.next", e => NextAction());
        }

        public override string Name
        {
            get { return "storyteller-page"; }
        }

        protected internal override Window OwnedWindow
        {
            get { return page; }
        }

        /// <summary>Exposed for the CanDoBack guard: Back must yield to a live search-clear Escape.</summary>
        internal bool HasActiveTypeaheadSearch
        {
            get { return TypeaheadHasActiveSearch; }
        }

        /// <summary>The page content draws its own ButtonTexts (Anomaly settings, Reset difficulty); declare Back/Next instead.</summary>
        protected override bool CaptureWindowButtons
        {
            get { return false; }
        }

        /// <summary>Chargen threading for the shared BuildSections catalog — the Anomaly playstyle picker row only appears here.</summary>
        protected override bool IsCharGen
        {
            get { return true; }
        }

        // Per-twin storage: this page has no live Storyteller instance yet, so reads and writes
        // ride the page's own private fields.

        protected override StorytellerDef CurrentStorytellerDef
        {
            get { return StorytellerField(page); }
        }

        protected override DifficultyDef CurrentDifficultyDef
        {
            get { return DifficultyField(page); }
        }

        protected override Difficulty CurrentDifficultyValues
        {
            get { return DifficultyValuesField(page); }
        }

        protected override void ApplyStorytellerDef(StorytellerDef def)
        {
            StorytellerSelectionPatch.UpdatePageStoryteller(page, def);
        }

        protected override void ApplyDifficultyDef(DifficultyDef def, DifficultyDef previous)
        {
            StorytellerSelectionPatch.UpdatePageDifficulty(page, def);
        }

        // ------------------------------------------------------------------
        // Pre-game-only regions: SaveMode, Anomaly.
        // ------------------------------------------------------------------

        protected override void BuildAdditionalRegions(Difficulty dv, DifficultyDef chosenDifficulty)
        {
            BuildAnomalyRows(dv, chosenDifficulty);
            ActiveRegions.Add(RegionKind.SaveMode);
            if (ModsConfig.AnomalyActive && chosenDifficulty != null)
            {
                ActiveRegions.Add(RegionKind.AnomalyPlaystyle);
                ActiveRegions.Add(RegionKind.AnomalySettings);
            }
        }

        private void BuildAnomalyRows(Difficulty dv, DifficultyDef chosenDifficulty)
        {
            anomalyRows.Clear();
            anomalySliderRows.Clear();
            if (dv == null || !ModsConfig.AnomalyActive || chosenDifficulty == null)
                return;

            // Playstyle radios, one per def, disabled with the scenario reason exactly where
            // Dialog_AnomalySettings.DrawPlaystyles disables them.
            foreach (AnomalyPlaystyleDef ps in DefDatabase<AnomalyPlaystyleDef>.AllDefs)
            {
                bool enabled = !Find.Scenario.standardAnomalyPlaystyleOnly || ps == AnomalyPlaystyleDefOf.Standard;
                anomalyRows.Add(new AnomalyRow
                {
                    Playstyle = ps,
                    Enabled = enabled,
                    DisableReason = enabled ? null : (string)("DisabledByScenario".Translate() + ": " + Find.Scenario.name),
                });
            }

            // Conditional sliders for the selected playstyle, wired to the page's live
            // difficultyValues through the shared helper's anomaly primitives.
            //
            // MUTATION-C: activeFractionFloor 0f mirrors Dialog_AnomalySettings.cs:164
            // (listing.Slider(anomalyThreatsActiveFraction, 0f, 1f)) — this region
            // replaces that popup, NOT StorytellerUI's DrawCustomLeft (whose floor is
            // 0.1f). See DifficultySettingsHelper.BuildAnomalySliders' activeFractionFloor
            // remarks for why the two vanilla draw sites disagree.
            foreach (DifficultySetting s in DifficultySettingsHelper.BuildAnomalySliders(
                playstyleGetter: () => dv.AnomalyPlaystyleDef,
                overrideGetter: () => dv.overrideAnomalyThreatsFraction ?? 0.15f,
                overrideSetter: v => dv.overrideAnomalyThreatsFraction = v,
                inactiveGetter: () => dv.anomalyThreatsInactiveFraction,
                inactiveSetter: v => dv.anomalyThreatsInactiveFraction = v,
                activeGetter: () => dv.anomalyThreatsActiveFraction,
                activeSetter: v => dv.anomalyThreatsActiveFraction = v,
                activeFractionFloor: 0f,
                studyGetter: () => dv.studyEfficiencyFactor,
                studySetter: v => dv.studyEfficiencyFactor = v,
                useEnabledConditions: false,
                includeOverride: true))
            {
                anomalySliderRows.Add(s);
            }
        }

        protected override string AdditionalRegionName(RegionKind kind)
        {
            switch (kind)
            {
                case RegionKind.SaveMode: return "RimWorldAccess.Storyteller.SaveMode".Translate();
                case RegionKind.AnomalyPlaystyle: return "RimWorldAccess.Storyteller.AnomalyRegion".Translate();
                case RegionKind.AnomalySettings: return "RimWorldAccess.Storyteller.AnomalySettingsRegion".Translate();
                default: return "";
            }
        }

        protected override int AdditionalRegionItemCount(RegionKind kind)
        {
            switch (kind)
            {
                case RegionKind.SaveMode: return 2;
                case RegionKind.AnomalyPlaystyle: return anomalyRows.Count;
                case RegionKind.AnomalySettings: return anomalySliderRows.Count;
                default: return 0;
            }
        }

        protected override ElementDescription DescribeAdditionalRegionItem(RegionKind kind, int index)
        {
            switch (kind)
            {
                case RegionKind.SaveMode: return DescribeSaveMode(index);
                case RegionKind.AnomalyPlaystyle: return DescribeAnomaly(index);
                case RegionKind.AnomalySettings:
                    var settingsDesc = new ElementDescription();
                    if (index >= 0 && index < anomalySliderRows.Count)
                        FillAnomalySliderRow(settingsDesc, anomalySliderRows[index]);
                    return settingsDesc;
                default: return new ElementDescription();
            }
        }

        private ElementDescription DescribeSaveMode(int index)
        {
            var d = new ElementDescription();
            d.Role = ElementRole.RadioButton;
            bool isReload = index == 0;
            d.Label = isReload
                ? (string)"ReloadAnytimeMode".Translate()
                : (string)"CommitmentMode".TranslateWithBackup("PermadeathMode");
            d.Extras = ((string)(isReload ? "ReloadAnytimeModeInfo" : "PermadeathModeInfo").Translate()).StripTags();
            GameInitData gid = Find.GameInitData;
            bool chosen = gid != null && gid.permadeathChosen;
            d.Selected = chosen && (isReload ? !gid.permadeath : gid.permadeath);
            return d;
        }

        private ElementDescription DescribeAnomaly(int index)
        {
            var d = new ElementDescription();
            if (index < 0 || index >= anomalyRows.Count)
                return d;
            AnomalyRow row = anomalyRows[index];
            d.Label = row.Playstyle.LabelCap;
            d.Role = ElementRole.RadioButton;
            d.Selected = ReferenceEquals(CurrentDifficultyValues.AnomalyPlaystyleDef, row.Playstyle);
            d.Extras = row.Playstyle.description.StripTags();
            if (!row.Enabled)
            {
                d.Disabled = true;
                d.Hint = row.DisableReason;
            }
            return d;
        }

        /// <summary>Duplicates the base's setting-row decomposition narrowly, since its FillSettingRow is private to keep its row building self-contained.</summary>
        private static void FillAnomalySliderRow(ElementDescription d, DifficultySetting s)
        {
            d.Label = s.Label;
            d.Role = ElementRole.Slider;
            d.Extras = s.Tooltip;
            if (!s.IsEnabled)
            {
                d.Disabled = true;
                return;
            }
            string ann = s.GetAnnouncement() ?? "";
            string label = s.Label ?? "";
            if (label.Length > 0 && ann.StartsWith(label, System.StringComparison.Ordinal))
                ann = ann.Substring(label.Length);
            ann = ann.TrimStart('.', ':', ' ');
            int boundary = ann.IndexOf(". ", System.StringComparison.Ordinal);
            if (boundary > 0)
                ann = ann.Substring(0, boundary);
            ann = ann.Trim();
            d.Value = ann.Length > 0 ? ann : null;
        }

        protected override void ActivateAdditionalRegionItem(RegionKind kind, int index)
        {
            switch (kind)
            {
                case RegionKind.SaveMode:
                    StorytellerSelectionPatch.SetPermadeath(index == 1);
                    AnnounceCurrentItem();
                    break;
                case RegionKind.AnomalyPlaystyle:
                    ActivateAnomaly(index);
                    break;
                case RegionKind.AnomalySettings:
                    // A slider row: Enter just re-reads the current value.
                    AnnounceCurrentItem();
                    break;
            }
        }

        private void ActivateAnomaly(int index)
        {
            if (index < 0 || index >= anomalyRows.Count)
                return;
            AnomalyRow row = anomalyRows[index];
            if (!row.Enabled)
            {
                // Mirror Dialog_AnomalySettings.DrawPlaystyles' rejected click (decompiled :143).
                Messages.Message(row.DisableReason, MessageTypeDefOf.RejectInput, historical: false);
                return;
            }
            CommitPlaystyle(row.Playstyle);
            AnnounceCurrentItem();
        }

        private void CommitPlaystyle(AnomalyPlaystyleDef playstyle)
        {
            Difficulty dv = CurrentDifficultyValues;
            // MUTATION-C: mirrors Dialog_AnomalySettings.DrawPlaystyles' selection branch
            // (decompiled :135-139) — on the non-override -> override transition seed
            // overrideAnomalyThreatsFraction to the dialog's DefaultOverrideThreatFraction
            // (0.15f), then assign difficulty.AnomalyPlaystyleDef. The AnomalyPlaystyle
            // region replaces that dialog and edits difficultyValues live (no Accept
            // stage), so the assignment is immediate; no gated setter exists for the
            // bare field.
            if (!dv.AnomalyPlaystyleDef.overrideThreatFraction
                && playstyle.overrideThreatFraction
                && !dv.overrideAnomalyThreatsFraction.HasValue)
            {
                dv.overrideAnomalyThreatsFraction = 0.15f;
            }
            // MUTATION-C: (as above) commit the selected playstyle to the live field.
            dv.AnomalyPlaystyleDef = playstyle;
        }

        /// <summary>
        /// Settle-commit for this page's own radio regions; the base handles
        /// Storytellers/Difficulty. A radio selects on arrival everywhere, so the Anomaly
        /// playstyles commit too (enabled rows only, silently — a disabled row's rejection
        /// message belongs to Enter, never to the cursor passing over it).
        /// </summary>
        protected override void OnCursorSettled(int region, int index)
        {
            base.OnCursorSettled(region, index);
            if (region < 0 || region >= ActiveRegions.Count)
                return;
            if (ActiveRegions[region] == RegionKind.SaveMode)
            {
                bool permadeath = index == 1;
                GameInitData gid = Find.GameInitData;
                if (gid == null || (gid.permadeathChosen && gid.permadeath == permadeath))
                    return;
                StorytellerSelectionPatch.SetPermadeath(permadeath);
                return;
            }
            if (ActiveRegions[region] == RegionKind.AnomalyPlaystyle
                && index >= 0 && index < anomalyRows.Count)
            {
                AnomalyRow row = anomalyRows[index];
                if (row.Enabled && !ReferenceEquals(CurrentDifficultyValues.AnomalyPlaystyleDef, row.Playstyle))
                {
                    CommitPlaystyle(row.Playstyle);
                }
            }
        }

        /// <summary>Chargen configuration: every radio here is a setup choice the player came to make.</summary>
        protected override bool AutoSelectRadioOnSettle
        {
            get { return true; }
        }

        protected override DifficultySetting ResolveAdjustableSetting(int region, int index)
        {
            if (region >= 0 && region < ActiveRegions.Count
                && ActiveRegions[region] == RegionKind.AnomalySettings
                && index >= 0 && index < anomalySliderRows.Count)
            {
                return anomalySliderRows[index];
            }
            return base.ResolveAdjustableSetting(region, index);
        }

        // Buttons region: Back and Next ride the vanilla page vehicle.

        /// <summary>Next is this page's default proceed button, for the Enter double-press.</summary>
        protected override string DefaultAcceptActionId
        {
            get { return "storyteller.next"; }
        }

        protected override IReadOnlyList<ScreenAction> DeclaredActions
        {
            get
            {
                actions.Clear();
                // Back speaks its Escape chord, Next its Alt+N toolbar chord.
                actions.Add(new ScreenAction("Back".Translate(), BackAction, SharedMenuGrammar.Cancel));
                actions.Add(new ScreenAction("Next".Translate(), NextAction, "storyteller.next"));
                return actions;
            }
        }

        /// <summary>
        /// True only while this scope's Back invocation runs its vanilla gate, letting that one
        /// explicit CanDoBack call through <c>StorytellerSelectionPatch_CanDoBack</c>, which
        /// otherwise blocks the raw Cancel poll outright while the scope is live.
        /// </summary>
        internal static bool BackRequested;

        private void EscapeBack()
        {
            ShellFrameStamps.MarkCancelConsumed();
            BackAction();
        }

        private void BackAction()
        {
            // Mirrors Page.DoBottomButtons' Back branch: gate, then DoBack.
            BackRequested = true;
            try
            {
                if ((bool)canDoBackMethod.Invoke(page, null))
                    doBackMethod.Invoke(page, null);
            }
            finally
            {
                BackRequested = false;
            }
        }

        private void NextAction()
        {
            // Mirrors the Next branch: gate, then DoNext (vehicle B). The flag lets this explicit
            // CanDoNext through the poll guard while the raw keyboard Accept poll stays blocked.
            AdvanceRequested = true;
            try
            {
                if ((bool)canDoNextMethod.Invoke(page, null))
                    doNextMethod.Invoke(page, null);
            }
            finally
            {
                AdvanceRequested = false;
            }
        }

        // ------------------------------------------------------------------
        // Captured-extras suppression, pre-game only: the "AnomalySettings..." button vanilla draws
        // solely at chargen, which the two Anomaly regions replace. Portrait-texture suppression is
        // shared in the base.
        // ------------------------------------------------------------------

        protected override IEnumerable<string> AdditionalOwnPresentedTexts
        {
            get
            {
                yield return (string)"AnomalySettings".Translate() + "...";
            }
        }

        private sealed class AnomalyRow
        {
            public AnomalyPlaystyleDef Playstyle;
            public bool Enabled = true;
            public string DisableReason;
        }
    }
}
